// IXC Core - a small HTTP/1.1 + WebSocket server on the loopback address only (127.0.0.1 and ::1).
// It replaces Windows' HttpListener (http.sys): no URL reservations, no "access denied" or reserved-port surprises, and the
// exact same code runs in the automated tests. Only what IXC needs: GET/POST/HEAD/OPTIONS with Content-Length bodies,
// keep-alive, Range responses for local music, and WebSockets (RFC 6455) for live updates.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public class Req {
    public string HttpMethod = "GET", RawTarget = "/", RemoteIp = ""; public Uri Url; public NameValueCollection QueryString = new NameValueCollection();
    public WebHeaderCollection Headers = new WebHeaderCollection(); public byte[] BodyBytes = new byte[0];
    public long ContentLength64 { get { return BodyBytes.Length; } }
    public Stream InputStream { get { return new MemoryStream(BodyBytes, false); } }
    public bool IsWebSocketRequest { get { return HttpMethod == "GET" && (Headers["Upgrade"] ?? "").Equals("websocket", StringComparison.OrdinalIgnoreCase) && (Headers["Connection"] ?? "").IndexOf("upgrade", StringComparison.OrdinalIgnoreCase) >= 0 && !string.IsNullOrEmpty(Headers["Sec-WebSocket-Key"]); } }
  }
  public class Resp {
    internal Ctx ctx; public int StatusCode = 200; public string ContentType = "text/plain"; public WebHeaderCollection Headers = new WebHeaderCollection(); public long ContentLength64 = -1;
    internal bool headSent, closed, keepAlive = true; internal Stream net; internal MemoryStream buf = new MemoryStream(); internal OutStream outStream;
    public Stream OutputStream { get { if (outStream == null) outStream = new OutStream(this); return outStream; } }
    public void Redirect(string url) { StatusCode = 302; Headers["Location"] = url; }
    internal void SendHead(long len) {
      if (headSent) return; headSent = true; var sb = new StringBuilder();
      sb.Append("HTTP/1.1 ").Append(StatusCode).Append(' ').Append(Reason(StatusCode)).Append("\r\n");
      if (StatusCode != 101) { sb.Append("Content-Type: ").Append(ContentType ?? "text/plain").Append("\r\n"); if (len >= 0) sb.Append("Content-Length: ").Append(len).Append("\r\n"); sb.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n"); }
      foreach (string k in Headers.AllKeys) sb.Append(k).Append(": ").Append(Headers[k]).Append("\r\n");
      sb.Append("\r\n"); var b = Encoding.ASCII.GetBytes(sb.ToString()); net.Write(b, 0, b.Length); }
    public void Close() {
      if (closed) return; closed = true;
      try { if (!headSent) { SendHead(buf.Length); if (ctx.Request.HttpMethod != "HEAD" && buf.Length > 0) net.Write(buf.GetBuffer(), 0, (int)buf.Length); } net.Flush(); }
      catch { keepAlive = false; } }
    static string Reason(int c) { switch (c) { case 101: return "Switching Protocols"; case 200: return "OK"; case 204: return "No Content"; case 206: return "Partial Content"; case 302: return "Found"; case 400: return "Bad Request"; case 403: return "Forbidden"; case 404: return "Not Found"; case 405: return "Method Not Allowed"; case 413: return "Payload Too Large"; case 416: return "Range Not Satisfiable"; case 421: return "Misdirected Request"; case 426: return "Upgrade Required"; case 429: return "Too Many Requests"; case 500: return "Internal Server Error"; case 502: return "Bad Gateway"; case 503: return "Service Unavailable"; default: return "Status"; } }
    // writes go to a buffer, or straight to the socket once a Content-Length was set (large files)
    internal class OutStream : Stream {
      readonly Resp r; public OutStream(Resp r) { this.r = r; }
      public override void Write(byte[] b, int o, int n) { if (r.ContentLength64 >= 0) { r.SendHead(r.ContentLength64); if (r.ctx.Request.HttpMethod != "HEAD") r.net.Write(b, o, n); } else r.buf.Write(b, o, n); }
      public override void Flush() { } public override bool CanRead { get { return false; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return true; } }
      public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
      public override int Read(byte[] b, int o, int n) { throw new NotSupportedException(); } public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); } public override void SetLength(long v) { throw new NotSupportedException(); } }
  }
  public class Ctx { public Req Request = new Req(); public Resp Response = new Resp(); internal Stream Net; internal TcpClient Tcp; internal bool Upgraded; }

  public static class Web {
    static readonly List<TcpListener> listeners = new List<TcpListener>();
    public static Action<Ctx> Handler; static int open;
    public static int OpenConnections { get { return open; } }
    // true when the port could be opened on 127.0.0.1 (::1 is added when the PC has IPv6)
    public static bool Start(int port, out string error) {
      error = null; var v4 = new TcpListener(IPAddress.Loopback, port);
      // Windows: exclusive use (no other program can take the port over). Linux (tests): allow re-binding over closed connections.
      try { if (U.IsWindows) v4.ExclusiveAddressUse = true; else v4.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); v4.Start(256); } catch (Exception e) { error = e.Message; return false; }
      lock (listeners) listeners.Add(v4); Accept(v4);
      try { if (Socket.OSSupportsIPv6) { var v6 = new TcpListener(IPAddress.IPv6Loopback, port); v6.Start(256); lock (listeners) listeners.Add(v6); Accept(v6); } } catch { }
      return true; }
    public static void Stop() { lock (listeners) { foreach (var l in listeners) try { l.Stop(); } catch { } listeners.Clear(); } }
    static void Accept(TcpListener l) {
      new Thread(() => { while (true) { TcpClient c; try { c = l.AcceptTcpClient(); } catch { lock (listeners) if (!listeners.Contains(l)) return; Thread.Sleep(50); continue; }
          if (Interlocked.Increment(ref open) > 600) { Interlocked.Decrement(ref open); try { c.Close(); } catch { } continue; }
          var t = Task.Run(() => Serve(c)); } }) { IsBackground = true, Name = "http-accept" }.Start(); }
    static async Task Serve(TcpClient tcp) {
      bool upgraded = false;
      try { tcp.NoDelay = true; var ns = tcp.GetStream(); var reader = new Reader(ns);
        var ip = ((IPEndPoint)tcp.Client.RemoteEndPoint).Address.ToString();
        for (int n = 0; n < 1000; n++) {
          var ctx = await ReadRequest(reader, ns, ip, n == 0 ? 15000 : 60000, tcp); if (ctx == null) break; ctx.Tcp = tcp;
          try { Handler(ctx); } catch (Exception e) { Log.Err("net", "http " + ctx.Request.Url.AbsolutePath + ": " + U.Plain(e)); if (!ctx.Response.headSent) { ctx.Response.StatusCode = 500; ctx.Response.ContentType = "application/json"; var b = Encoding.UTF8.GetBytes(J.Ser(J.D("error", U.Plain(e)))); ctx.Response.buf.SetLength(0); ctx.Response.buf.Write(b, 0, b.Length); } }
          if (ctx.Upgraded) { upgraded = true; return; }   // the WebSocket owns the connection now
          ctx.Response.Close(); if (!ctx.Response.keepAlive) break; } }
      catch { }
      finally { Interlocked.Decrement(ref open); if (!upgraded) { try { tcp.Close(); } catch { } } } }
    class Reader { public readonly Stream S; public byte[] Buf = new byte[16384]; public int Pos, Len; public Reader(Stream s) { S = s; } }
    // a read that gives up after ms (closing the connection), because NetworkStream ignores cancellation once a read started
    internal static async Task<int> ReadT(Stream s, byte[] b, int o, int n, int ms, TcpClient tcp) {
      var t = s.ReadAsync(b, o, n); if (await Task.WhenAny(t, Task.Delay(ms)) != t) { try { tcp.Close(); } catch { } var ignore = t.ContinueWith(x => { var e = x.Exception; }); return -1; }
      try { return await t; } catch { return -1; } }
    static async Task<Ctx> ReadRequest(Reader r, Stream ns, string ip, int idleMs, TcpClient tcp) {
      // request line + headers (max 16 KB)
      var head = new StringBuilder(); int matched = 0; int wait = idleMs;
      while (true) {
        if (r.Pos >= r.Len) { int k = await ReadT(r.S, r.Buf, 0, r.Buf.Length, wait, tcp); if (k <= 0) return null; r.Pos = 0; r.Len = k; wait = 15000; }
        char ch = (char)r.Buf[r.Pos++]; head.Append(ch); if (head.Length > 16384) return null;
        matched = (ch == '\r' && (matched == 0 || matched == 2)) || (ch == '\n' && (matched == 1 || matched == 3)) ? matched + 1 : (ch == '\r' ? 1 : 0);
        if (matched == 4) break; }
      var lines = head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None); var first = lines[0].Split(' ');
      var ctx = new Ctx { Net = ns }; ctx.Response.ctx = ctx; ctx.Response.net = ns; var rq = ctx.Request; rq.RemoteIp = ip;
      if (first.Length != 3 || !first[2].StartsWith("HTTP/1.")) { ctx.Response.StatusCode = 400; ctx.Response.keepAlive = false; rq.Url = new Uri("http://localhost/"); return ctx; }
      rq.HttpMethod = first[0].ToUpperInvariant(); rq.RawTarget = first[1];
      for (int i = 1; i < lines.Length; i++) { var l = lines[i]; if (l.Length == 0) continue; int c = l.IndexOf(':'); if (c <= 0) continue; try { rq.Headers.Add(l.Substring(0, c).Trim(), l.Substring(c + 1).Trim()); } catch { } }
      if (first[2] == "HTTP/1.0" || (rq.Headers["Connection"] ?? "").Equals("close", StringComparison.OrdinalIgnoreCase)) ctx.Response.keepAlive = false;
      try { rq.Url = new Uri("http://localhost" + (rq.RawTarget.StartsWith("/") ? rq.RawTarget : "/" + rq.RawTarget)); } catch { rq.Url = new Uri("http://localhost/"); ctx.Response.StatusCode = 400; ctx.Response.keepAlive = false; return ctx; }
      rq.QueryString = ParseQuery(rq.Url.Query);
      // body (Content-Length only; IXC's pages never send chunked bodies)
      if (!string.IsNullOrEmpty(rq.Headers["Transfer-Encoding"])) { ctx.Response.StatusCode = 400; ctx.Response.keepAlive = false; return ctx; }
      long len = 0; long.TryParse(rq.Headers["Content-Length"] ?? "0", out len);
      if (len < 0 || len > 1048576) { ctx.Response.StatusCode = 413; ctx.Response.keepAlive = false; return ctx; }
      if (len > 0) { var body = new byte[len]; int got = 0;
        while (got < len) { if (r.Pos < r.Len) { int k = (int)Math.Min(len - got, r.Len - r.Pos); Buffer.BlockCopy(r.Buf, r.Pos, body, got, k); r.Pos += k; got += k; continue; }
          int n = await ReadT(r.S, r.Buf, 0, r.Buf.Length, 15000, tcp); if (n <= 0) return null; r.Pos = 0; r.Len = n; }
        rq.BodyBytes = body; }
      return ctx; }
    static NameValueCollection ParseQuery(string q) {
      var n = new NameValueCollection(); if (string.IsNullOrEmpty(q)) return n; if (q.StartsWith("?")) q = q.Substring(1);
      foreach (var p in q.Split('&')) { if (p.Length == 0) continue; int e = p.IndexOf('='); string k = e < 0 ? p : p.Substring(0, e), v = e < 0 ? "" : p.Substring(e + 1);
        try { n.Add(Uri.UnescapeDataString(k.Replace('+', ' ')), Uri.UnescapeDataString(v.Replace('+', ' '))); } catch { } }
      return n; }

    // ---------- WebSocket (RFC 6455, server side) ----------
    public class WsConn {
      readonly Stream s; readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1); public volatile bool Open = true; readonly TcpClient tcp;
      public WsConn(Stream s, TcpClient tcp) { this.s = s; this.tcp = tcp; }
      public async Task SendText(string text) { await Send(1, Encoding.UTF8.GetBytes(text)); }
      async Task Send(int op, byte[] p) {
        if (!Open) return; var h = new List<byte> { (byte)(0x80 | op) };
        if (p.Length < 126) h.Add((byte)p.Length); else if (p.Length < 65536) { h.Add(126); h.Add((byte)(p.Length >> 8)); h.Add((byte)(p.Length & 255)); } else { h.Add(127); for (int i = 7; i >= 0; i--) h.Add((byte)(((long)p.Length >> (8 * i)) & 255)); }
        if (!await sendLock.WaitAsync(15000)) throw new TimeoutException("send blocked");
        try { var hb = h.ToArray(); await s.WriteAsync(hb, 0, hb.Length); if (p.Length > 0) await s.WriteAsync(p, 0, p.Length); await s.FlushAsync(); }
        catch { Open = false; throw; } finally { sendLock.Release(); } }
      async Task ReadN(byte[] b, int n, int ms) { int o = 0; while (o < n) { int k = await ReadT(s, b, o, n - o, ms, tcp); if (k <= 0) throw new IOException("closed"); o += k; } }
      // returns the next text message, or null when the connection closed
      public async Task<string> Receive(int max, int idleMs) {
        var msg = new MemoryStream(); var hdr = new byte[14];
        while (Open) {
          {
            try {
              await ReadN(hdr, 2, idleMs); bool fin = (hdr[0] & 0x80) != 0; int op = hdr[0] & 0x0F; bool masked = (hdr[1] & 0x80) != 0; long len = hdr[1] & 0x7F;
              if (len == 126) { await ReadN(hdr, 2, 15000); len = hdr[0] << 8 | hdr[1]; } else if (len == 127) { await ReadN(hdr, 8, 15000); len = 0; for (int i = 0; i < 8; i++) len = len << 8 | hdr[i]; }
              if (!masked || len > max) { await Close(1002); return null; }   // clients must mask; oversized = close
              var mask = new byte[4]; await ReadN(mask, 4, 15000); var p = new byte[len]; if (len > 0) await ReadN(p, (int)len, 15000);
              for (int i = 0; i < p.Length; i++) p[i] ^= mask[i & 3];
              if (op == 8) { await Close(1000); return null; }
              if (op == 9) { await Send(10, p); continue; }
              if (op == 10) continue;
              if (op == 1 || op == 2 || op == 0) { msg.Write(p, 0, p.Length); if (msg.Length > max) { await Close(1009); return null; } if (fin) return Encoding.UTF8.GetString(msg.GetBuffer(), 0, (int)msg.Length); continue; }
              await Close(1002); return null; }
            catch { Open = false; return null; } } }
        return null; }
      public async Task Close(int code) { try { await Send(8, new[] { (byte)(code >> 8), (byte)(code & 255) }); } catch { } Open = false; try { tcp.Close(); } catch { } }
      public void Abort() { Open = false; try { tcp.Close(); } catch { } }
    }
    public static WsConn Accept(Ctx ctx) {
      var key = ctx.Request.Headers["Sec-WebSocket-Key"]; string accept;
      using (var sha = SHA1.Create()) accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
      var r = ctx.Response; r.StatusCode = 101; r.Headers["Upgrade"] = "websocket"; r.Headers["Connection"] = "Upgrade"; r.Headers["Sec-WebSocket-Accept"] = accept;
      r.SendHead(-1); ctx.Net.Flush(); ctx.Upgraded = true; r.closed = true;
      return new WsConn(ctx.Net, ctx.Tcp); }
  }
}
