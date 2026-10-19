// IXC Core - native RTMP relay: OBS streams ONE encoded stream to IXC (a local RTMP ingest on 127.0.0.1), and IXC forwards the very same
// audio/video packets - never decoded, never re-encoded - to up to 8 RTMP / RTMPS destinations. Each destination has its own connection,
// bounded queue, timeouts and reconnect back-off, so one slow or failing destination never affects the others or OBS.
// Written from Adobe's public RTMP 1.0 specification (handshake, chunk stream, AMF0 commands, control messages). No third-party code.
// Stream keys are only held in memory while connecting and are never logged or shown.
// Part of IXC - Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public sealed class RtmpMsg { public byte Type; public uint Ts, StreamId; public byte[] Payload; public bool Key, Header; }

  // ---------- AMF0 (the subset RTMP commands use) ----------
  public static class Amf {
    static void Str(MemoryStream m, string s) { var b = Encoding.UTF8.GetBytes(s); m.WriteByte((byte)(b.Length >> 8)); m.WriteByte((byte)b.Length); m.Write(b, 0, b.Length); }
    static void Value(MemoryStream m, object v) {
      if (v == null) m.WriteByte(5);
      else if (v is string) { m.WriteByte(2); Str(m, (string)v); }
      else if (v is double) { m.WriteByte(0); var b = BitConverter.GetBytes((double)v); if (BitConverter.IsLittleEndian) Array.Reverse(b); m.Write(b, 0, 8); }
      else if (v is bool) { m.WriteByte(1); m.WriteByte((byte)((bool)v ? 1 : 0)); }
      else if (v is object[]) { m.WriteByte(3); var kv = (object[])v; for (int i = 0; i + 1 < kv.Length; i += 2) { Str(m, (string)kv[i]); Value(m, kv[i + 1]); } m.WriteByte(0); m.WriteByte(0); m.WriteByte(9); }
      else throw new ArgumentException("AMF type"); }
    // items: string, double, bool, null, or object[] {key, value, key, value ...} for an AMF object
    public static byte[] Cmd(params object[] items) { var m = new MemoryStream(); foreach (var it in items) Value(m, it); return m.ToArray(); }
    public static List<object> Decode(byte[] p, int off) { var l = new List<object>(); int i = off; try { while (i < p.Length) l.Add(Read(p, ref i)); } catch (Exception) { } return l; }
    static string Utf(byte[] p, ref int i, int n) { var s = Encoding.UTF8.GetString(p, i, n); i += n; return s; }
    static object Read(byte[] p, ref int i) {
      byte t = p[i++];
      switch (t) {
        case 0: { var b = new byte[8]; Array.Copy(p, i, b, 0, 8); i += 8; if (BitConverter.IsLittleEndian) Array.Reverse(b); return BitConverter.ToDouble(b, 0); }
        case 1: return p[i++] != 0;
        case 2: { int n = (p[i] << 8) | p[i + 1]; i += 2; return Utf(p, ref i, n); }
        case 3: return Props(p, ref i);
        case 5: case 6: return null;
        case 8: i += 4; return Props(p, ref i);
        case 10: { int n = (p[i] << 24) | (p[i + 1] << 16) | (p[i + 2] << 8) | p[i + 3]; i += 4; var l = new List<object>(); for (int k = 0; k < n; k++) l.Add(Read(p, ref i)); return l; }
        case 12: { int n = (p[i] << 24) | (p[i + 1] << 16) | (p[i + 2] << 8) | p[i + 3]; i += 4; return Utf(p, ref i, n); }
        default: throw new Exception("AMF type " + t); } }
    static Dictionary<string, object> Props(byte[] p, ref int i) {
      var d = new Dictionary<string, object>();
      while (true) { int n = (p[i] << 8) | p[i + 1]; i += 2; if (n == 0 && p[i] == 9) { i++; return d; } var k = Utf(p, ref i, n); d[k] = Read(p, ref i); } }
    public static string Str(List<object> l, int i) { return i < l.Count ? l[i] as string : null; }
    public static double Num(List<object> l, int i) { return i < l.Count && l[i] is double ? (double)l[i] : 0; }
    public static string Code(List<object> l) { foreach (var o in l) { var d = o as Dictionary<string, object>; object c; if (d != null && d.TryGetValue("code", out c) && c is string) return (string)c; } return null; }
  }

  // ---------- chunk stream: reading ----------
  public sealed class RtmpReader {
    readonly Stream s; public int ChunkSize = 128; public long Total; readonly byte[] one = new byte[1];
    sealed class Cs { public uint Ts, Delta, Len, Sid; public byte Type; public bool Ext; public MemoryStream Buf; public int Need; }
    readonly Dictionary<int, Cs> cs = new Dictionary<int, Cs>();
    public RtmpReader(Stream s) { this.s = s; }
    public byte[] Raw(int n) { var b = new byte[n]; int g = 0; while (g < n) { int r = s.Read(b, g, n - g); if (r <= 0) throw new EndOfStreamException("the connection was closed"); g += r; } Total += n; return b; }
    int Byte() { int r = s.Read(one, 0, 1); if (r <= 0) throw new EndOfStreamException("the connection was closed"); Total++; return one[0]; }
    public RtmpMsg Read() {
      while (true) {
        int b0 = Byte(); int fmt = b0 >> 6, id = b0 & 63;
        if (id == 0) id = Byte() + 64; else if (id == 1) { int a = Byte(); int b = Byte(); id = a + b * 256 + 64; }
        Cs c; if (!cs.TryGetValue(id, out c)) cs[id] = c = new Cs();
        bool fresh = c.Buf == null;
        if (fmt <= 2) {
          var h = Raw(fmt == 0 ? 11 : fmt == 1 ? 7 : 3); uint t = (uint)((h[0] << 16) | (h[1] << 8) | h[2]);
          if (fmt <= 1) { c.Len = (uint)((h[3] << 16) | (h[4] << 8) | h[5]); c.Type = h[6]; }
          if (fmt == 0) c.Sid = (uint)(h[7] | (h[8] << 8) | (h[9] << 16) | (h[10] << 24));
          c.Ext = t == 0xFFFFFF; if (c.Ext) { var e = Raw(4); t = (uint)((e[0] << 24) | (e[1] << 16) | (e[2] << 8) | e[3]); }
          if (fmt == 0) { c.Ts = t; c.Delta = 0; } else { c.Delta = t; if (fresh) c.Ts += t; } }
        else { if (c.Ext) Raw(4); if (fresh) c.Ts += c.Delta; }
        if (fresh) { if (c.Len > 16000000) throw new Exception("message too large"); c.Buf = new MemoryStream((int)Math.Min(c.Len, 1 << 20)); c.Need = (int)c.Len; }
        int take = Math.Min(c.Need, ChunkSize); if (take > 0) { var d = Raw(take); c.Buf.Write(d, 0, take); c.Need -= take; }
        if (c.Need == 0) { var m = new RtmpMsg { Type = c.Type, Ts = c.Ts, StreamId = c.Sid, Payload = c.Buf.ToArray() }; c.Buf = null; return m; } } }
  }

  // ---------- chunk stream: writing (one lock, one socket write per message) ----------
  public sealed class RtmpWriter {
    readonly Stream s; public int ChunkSize = 128; readonly object L = new object();
    public RtmpWriter(Stream s) { this.s = s; }
    static int Csid(byte type) { return type <= 6 ? 2 : type == 20 ? 3 : type == 8 ? 4 : type == 9 ? 5 : 6; }
    public void Send(byte type, uint ts, uint sid, byte[] payload) {
      int csid = Csid(type); bool ext = ts >= 0xFFFFFF; var ms = new MemoryStream(payload.Length + payload.Length / 64 + 32);
      ms.WriteByte((byte)csid); uint t3 = ext ? 0xFFFFFF : ts;
      ms.WriteByte((byte)(t3 >> 16)); ms.WriteByte((byte)(t3 >> 8)); ms.WriteByte((byte)t3);
      ms.WriteByte((byte)(payload.Length >> 16)); ms.WriteByte((byte)(payload.Length >> 8)); ms.WriteByte((byte)payload.Length); ms.WriteByte(type);
      ms.WriteByte((byte)sid); ms.WriteByte((byte)(sid >> 8)); ms.WriteByte((byte)(sid >> 16)); ms.WriteByte((byte)(sid >> 24));
      int cs; lock (L) cs = ChunkSize;
      if (ext) { ms.WriteByte((byte)(ts >> 24)); ms.WriteByte((byte)(ts >> 16)); ms.WriteByte((byte)(ts >> 8)); ms.WriteByte((byte)ts); }
      int off = 0; while (true) {
        int n = Math.Min(cs, payload.Length - off); ms.Write(payload, off, n); off += n; if (off >= payload.Length) break;
        ms.WriteByte((byte)(0xC0 | csid)); if (ext) { ms.WriteByte((byte)(ts >> 24)); ms.WriteByte((byte)(ts >> 16)); ms.WriteByte((byte)(ts >> 8)); ms.WriteByte((byte)ts); } }
      var all = ms.GetBuffer(); int len = (int)ms.Length;
      lock (L) { s.Write(all, 0, len); s.Flush(); } }
    public void SetChunkSize(int n) { Send(1, 0, 0, Be32((uint)n)); lock (L) ChunkSize = n; }
    public static byte[] Be32(uint v) { return new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }; }
  }

  // ---------- one destination: connects, publishes, forwards, reconnects ----------
  public sealed class RtmpOut {
    public readonly string Id; readonly Func<string[]> cfg;   // -> { url, key, name } at each (re)connect, so edits apply on the next connection
    public volatile string State = "connecting", Detail = ""; public long Bytes, Frames, Dropped; public int Reconnects; public DateTime Since = DateTime.Now;
    const int MaxQueueBytes = 16 * 1024 * 1024, MaxQueueMsgs = 2000;
    readonly object ql = new object(), wl = new object(); readonly Queue<RtmpMsg> q = new Queue<RtmpMsg>(); int qBytes; bool accepting, needKey = true, failed; uint baseTs;
    volatile bool stop; Thread th; volatile TcpClient tcp;
    public RtmpOut(string id, Func<string[]> cfg) { Id = id; this.cfg = cfg; }
    public void Start() { th = new Thread(Run) { IsBackground = true, Name = "multistream " + Id }; th.Start(); }
    // never blocks the caller: the worker says goodbye itself; if it is stuck in the network it is cut after 2 s
    public void Stop() { stop = true; lock (ql) Monitor.PulseAll(ql); lock (wl) Monitor.PulseAll(wl); var t = th; Task.Run(() => { if (t != null && !t.Join(2000)) CloseNow(); }); }
    void CloseNow() { var t = tcp; try { if (t != null) t.Close(); } catch (Exception) { } }
    void Fail() { lock (ql) { failed = true; Monitor.PulseAll(ql); } }
    static string Clean(string s, string key) { s = s ?? ""; if (!string.IsNullOrEmpty(key)) s = s.Replace(key, "***"); return s.Length > 200 ? s.Substring(0, 200) : s; }

    public void Push(RtmpMsg m) {
      lock (ql) {
        if (!accepting || stop) return;
        if (needKey) { if (m.Type == 9 && m.Key && !m.Header) { needKey = false; baseTs = m.Ts; } else { if (m.Type == 9) Dropped++; return; } }   // join at a keyframe, audio included
        if (qBytes + m.Payload.Length > MaxQueueBytes || q.Count >= MaxQueueMsgs) { Dropped += q.Count + 1; q.Clear(); qBytes = 0; needKey = true; Detail = "slow connection - skipped video until the next keyframe"; return; }
        q.Enqueue(m); qBytes += m.Payload.Length; Monitor.Pulse(ql); } }

    void Run() {
      int attempt = 0; int baseMs = int.Parse(Ep.Get("multistream_retry_ms", "1000")), maxMs = int.Parse(Ep.Get("multistream_backoff_max_ms", "30000"));
      while (!stop) {
        string[] c = cfg(); if (c == null) { State = "idle"; Detail = "removed"; return; }
        string key = c[1], err = null; bool wasLive = false;
        try { State = attempt == 0 ? "connecting" : "reconnecting"; Session(c[0], key, c[2], out wasLive); }
        catch (Exception e) { err = Clean(e is AggregateException && e.InnerException != null ? e.InnerException.Message : e.Message, key); }
        lock (ql) { accepting = false; q.Clear(); qBytes = 0; needKey = true; failed = false; }
        CloseNow(); if (stop) break;
        if (wasLive) attempt = 0;
        int wait = (int)Math.Min((long)maxMs, (long)baseMs << Math.Min(attempt, 10)); attempt++; Reconnects++;
        State = "reconnecting"; Detail = (err ?? "connection ended") + " - retrying in " + (wait < 1000 ? wait + " ms" : (wait / 1000) + " s");
        Log.Warn("stream", "multistream " + c[2] + " (" + Multistream.Redact(c[0]) + "): " + Detail);
        lock (wl) { if (!stop) Monitor.Wait(wl, wait); } }
      State = "idle"; Detail = ""; }

    void Session(string url, string key, string name, out bool wasLive) {
      wasLive = false; var u = new Uri(url); int port = u.Port > 0 ? u.Port : (u.Scheme == "rtmps" ? 443 : 1935); int to = int.Parse(Ep.Get("multistream_timeout_ms", "10000"));
      var cl = new TcpClient(); tcp = cl;
      var conn = cl.ConnectAsync(u.Host, port); if (!conn.Wait(to)) throw new Exception("no answer from " + u.Host + ":" + port); cl.NoDelay = true; cl.SendTimeout = 15000; cl.ReceiveTimeout = to;
      Stream st = cl.GetStream();
      if (u.Scheme == "rtmps") { var ssl = Ep.Get("multistream_insecure_tls", "") == "1" ? new SslStream(st, false, (a, b, c2, d) => true) : new SslStream(st); ssl.AuthenticateAsClient(u.Host, null, System.Security.Authentication.SslProtocols.Tls12, true); st = ssl; }   // explicit: this exe is not marked as .NET 4.8, so the default would be TLS 1.0, which platforms refuse
      var rd = new RtmpReader(st); var wr = new RtmpWriter(st); string app = u.AbsolutePath.Trim('/'); string tc = u.Scheme + "://" + u.Host + ":" + port + "/" + app;
      // handshake: C0 C1 -> S0 S1 S2 -> C2
      var c01 = new byte[1537]; c01[0] = 3; var rnd = new byte[1528]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(rnd); Array.Copy(rnd, 0, c01, 9, 1528); st.Write(c01, 0, 1537); st.Flush();
      var s0 = rd.Raw(1); if (s0[0] != 3) throw new Exception("the server does not speak RTMP"); var s1 = rd.Raw(1536); rd.Raw(1536); st.Write(s1, 0, 1536); st.Flush();
      wr.SetChunkSize(4096);
      wr.Send(20, 0, 0, Amf.Cmd("connect", 1.0, new object[] { "app", app, "type", "nonprivate", "flashVer", "FMLE/3.0 (compatible; IXC)", "tcUrl", tc }));
      var res = Await(rd, wr, "_result", 1.0); var ccode = Amf.Code(res); if (ccode != null && ccode != "NetConnection.Connect.Success") throw new Exception("the server refused the connection (" + ccode + ")");
      wr.Send(20, 0, 0, Amf.Cmd("releaseStream", 2.0, null, key)); wr.Send(20, 0, 0, Amf.Cmd("FCPublish", 3.0, null, key));
      wr.Send(20, 0, 0, Amf.Cmd("createStream", 4.0, null));
      var cs = Await(rd, wr, "_result", 4.0); uint sid = (uint)Amf.Num(cs, 3); if (sid == 0) throw new Exception("the server did not open a stream");
      wr.Send(20, 0, sid, Amf.Cmd("publish", 5.0, null, key, "live"));
      var ps = Await(rd, wr, "onStatus", -1); var pcode = Amf.Code(ps);
      if (pcode != "NetStream.Publish.Start") throw new Exception("the server did not accept the stream (" + (pcode ?? "no answer") + ")");
      cl.ReceiveTimeout = 0;
      // the stream's description and codec headers first, exactly as OBS sent them; then from the next keyframe on
      RtmpMsg meta, aSeq, vSeq; Relay.Headers(out meta, out aSeq, out vSeq);
      if (meta != null) wr.Send(18, 0, sid, meta.Payload); if (aSeq != null) wr.Send(8, 0, sid, aSeq.Payload); if (vSeq != null) wr.Send(9, 0, sid, vSeq.Payload);
      var reader = new Thread(() => { try { Pump(rd, wr); } catch (Exception) { } Fail(); }) { IsBackground = true, Name = "multistream rx " + Id }; reader.Start();
      lock (ql) { accepting = true; needKey = true; failed = false; } State = "live"; Detail = ""; Since = DateTime.Now; wasLive = true;
      Log.Info("stream", "multistream live: " + name + " (" + Multistream.Redact(url) + ")");
      while (true) {
        RtmpMsg m; lock (ql) { while (q.Count == 0 && !stop && !failed) Monitor.Wait(ql, 500); if (stop || failed) break; m = q.Dequeue(); qBytes -= m.Payload.Length; }
        long d = (long)(int)unchecked(m.Ts - baseTs); uint ts = d < 0 ? 0 : (uint)d;
        wr.Send(m.Type, ts, sid, m.Payload); Interlocked.Add(ref Bytes, m.Payload.Length); Interlocked.Increment(ref Frames); }
      if (stop) { try { wr.Send(20, 0, sid, Amf.Cmd("FCUnpublish", 6.0, null, key)); wr.Send(20, 0, sid, Amf.Cmd("deleteStream", 7.0, null, (double)sid)); } catch (Exception) { } return; }
      throw new Exception("the connection to the server was lost"); }

    // waits for a command (control messages are answered meanwhile); txn < 0 = any
    static List<object> Await(RtmpReader rd, RtmpWriter wr, string name, double txn) {
      for (int i = 0; i < 200; i++) {
        var m = rd.Read(); if (m.Type == 20) { var l = Amf.Decode(m.Payload, 0); var n = Amf.Str(l, 0);
          if (n == "_error") throw new Exception("the server answered with an error (" + (Amf.Code(l) ?? "no code") + ")");
          if (n == name && (txn < 0 || Amf.Num(l, 1) == txn)) return l;
          if (n == "onStatus" && name != "onStatus") { var cde = Amf.Code(l); if (cde != null && cde.IndexOf("Success", StringComparison.Ordinal) < 0 && cde.IndexOf("Start", StringComparison.Ordinal) < 0) throw new Exception("the server said " + cde); } }
        else Control(m, rd, wr); }
      throw new Exception("the server did not answer"); }
    static void Control(RtmpMsg m, RtmpReader rd, RtmpWriter wr) {
      if (m.Type == 1 && m.Payload.Length >= 4) rd.ChunkSize = (int)(((uint)m.Payload[0] << 24 | (uint)m.Payload[1] << 16 | (uint)m.Payload[2] << 8 | m.Payload[3]) & 0x7FFFFFFF);
      else if (m.Type == 6) wr.Send(5, 0, 0, RtmpWriter.Be32(2500000));
      else if (m.Type == 4 && m.Payload.Length >= 6 && m.Payload[0] == 0 && m.Payload[1] == 6) wr.Send(4, 0, 0, new byte[] { 0, 7, m.Payload[2], m.Payload[3], m.Payload[4], m.Payload[5] }); }
    // after publishing the server only talks occasionally (pings, window acknowledgements, stream status): answer, and notice when it hangs up
    void Pump(RtmpReader rd, RtmpWriter wr) {
      long acked = 0; long win = 2500000;
      while (!stop) {
        var m = rd.Read();
        if (m.Type == 5 && m.Payload.Length >= 4) win = (long)((uint)m.Payload[0] << 24 | (uint)m.Payload[1] << 16 | (uint)m.Payload[2] << 8 | m.Payload[3]);
        else if (m.Type == 20) { var l = Amf.Decode(m.Payload, 0); var code = Amf.Code(l); if (code != null && (code.StartsWith("NetStream.Publish.") && code != "NetStream.Publish.Start" || code == "NetStream.Unpublish.Success" || code.StartsWith("NetConnection.Connect.") && code != "NetConnection.Connect.Success")) throw new Exception("the server ended the stream (" + code + ")"); }
        else Control(m, rd, wr);
        if (win > 0 && rd.Total - acked >= win) { acked = rd.Total; wr.Send(3, 0, 0, RtmpWriter.Be32((uint)rd.Total)); } } }
  }

  // ---------- the local ingest OBS pushes to ----------
  public sealed class RtmpIngest {
    TcpListener lis; Thread acc; volatile bool stop; public int Port; public volatile bool Receiving; public DateTime Since; public long Msgs, Bytes; readonly string token; TcpClient cur;
    public RtmpIngest(string token) { this.token = token; }
    public string Start(int port) {
      try { lis = new TcpListener(IPAddress.Loopback, port); lis.Start(); } catch (Exception e) { return "IXC could not listen on 127.0.0.1:" + port + " (" + e.Message + ") - another program may be using that port"; }
      Port = ((IPEndPoint)lis.LocalEndpoint).Port; acc = new Thread(Accept) { IsBackground = true, Name = "multistream ingest" }; acc.Start(); return null; }
    public void Stop() { stop = true; try { lis.Stop(); } catch (Exception) { } var c = cur; try { if (c != null) c.Close(); } catch (Exception) { } }
    void Accept() {
      while (!stop) {
        TcpClient c; try { c = lis.AcceptTcpClient(); } catch (Exception) { return; }
        if (Receiving) { try { c.Close(); } catch (Exception) { } continue; }   // one publisher (OBS) at a time
        Receiving = true; cur = c; var cc = c; new Thread(() => { try { Session(cc); } catch (Exception e) { if (!stop) Log.Info("stream", "multistream ingest: " + e.Message); } finally { Receiving = false; try { cc.Close(); } catch (Exception) { } } }) { IsBackground = true, Name = "multistream publisher" }.Start(); } }
    void Session(TcpClient c) {
      c.NoDelay = true; c.ReceiveTimeout = 30000; c.SendTimeout = 15000; Stream st = c.GetStream(); var rd = new RtmpReader(st); var wr = new RtmpWriter(st); bool publishing = false;
      try {
        var c0 = rd.Raw(1); if (c0[0] != 3) throw new Exception("not RTMP"); var c1 = rd.Raw(1536);
        var s = new byte[1 + 1536 + 1536]; s[0] = 3; var rnd = new byte[1528]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(rnd); Array.Copy(rnd, 0, s, 9, 1528); Array.Copy(c1, 0, s, 1 + 1536, 1536); st.Write(s, 0, s.Length); st.Flush(); rd.Raw(1536);
        long acked = rd.Total; Since = DateTime.Now;
        while (!stop) {
          var m = rd.Read(); Bytes += m.Payload.Length; Msgs++;
          if (m.Type == 1 && m.Payload.Length >= 4) rd.ChunkSize = (int)(((uint)m.Payload[0] << 24 | (uint)m.Payload[1] << 16 | (uint)m.Payload[2] << 8 | m.Payload[3]) & 0x7FFFFFFF);
          else if (m.Type == 4 && m.Payload.Length >= 6 && m.Payload[0] == 0 && m.Payload[1] == 6) wr.Send(4, 0, 0, new byte[] { 0, 7, m.Payload[2], m.Payload[3], m.Payload[4], m.Payload[5] });
          else if (m.Type == 8 || m.Type == 9 || m.Type == 18) { if (publishing) Relay.OnMessage(m); }
          else if (m.Type == 20) {
            var l = Amf.Decode(m.Payload, 0); var name = Amf.Str(l, 0); double txn = Amf.Num(l, 1);
            if (name == "connect") {
              wr.Send(5, 0, 0, RtmpWriter.Be32(2500000)); wr.Send(6, 0, 0, new byte[] { 0x00, 0x26, 0x25, 0xA0, 2 }); wr.SetChunkSize(4096);
              wr.Send(20, 0, 0, Amf.Cmd("_result", txn, new object[] { "fmsVer", "FMS/3,0,1,123", "capabilities", 31.0 }, new object[] { "level", "status", "code", "NetConnection.Connect.Success", "description", "Connection succeeded.", "objectEncoding", 0.0 })); }
            else if (name == "createStream") wr.Send(20, 0, 0, Amf.Cmd("_result", txn, null, 1.0));
            else if (name == "releaseStream" || name == "FCPublish") wr.Send(20, 0, 0, Amf.Cmd("_result", txn, null, null));
            else if (name == "publish") {
              var key = Amf.Str(l, 3) ?? ""; int q = key.IndexOf('?'); if (q >= 0) key = key.Substring(0, q);
              if (key != token) { wr.Send(20, 0, 1, Amf.Cmd("onStatus", 0.0, null, new object[] { "level", "error", "code", "NetStream.Publish.BadName", "description", "Wrong stream key" })); Log.Warn("stream", "multistream ingest: a publisher used the wrong IXC stream key"); return; }
              wr.Send(20, 0, 1, Amf.Cmd("onStatus", 0.0, null, new object[] { "level", "status", "code", "NetStream.Publish.Start", "description", "Publishing" }));
              publishing = true; Relay.OnPublishStart(); }
            else if (name == "deleteStream" || name == "FCUnpublish") { if (name == "deleteStream") return; } }
          if (rd.Total - acked >= 2500000) { acked = rd.Total; wr.Send(3, 0, 0, RtmpWriter.Be32((uint)rd.Total)); } } }
      finally { if (publishing) Relay.OnPublishEnd(); } }
  }

  // ---------- the hub: what OBS sends goes to every destination ----------
  public static class Relay {
    static readonly object L = new object(); static RtmpIngest ingest; static readonly Dictionary<string, RtmpOut> Outs = new Dictionary<string, RtmpOut>(); static readonly Dictionary<string, string> Skipped = new Dictionary<string, string>();
    static RtmpMsg meta, aSeq, vSeq; public static volatile bool Publishing; static long msgs;
    public static string Token() {
      var d = Secrets.Get("multistream.ingest") as Dictionary<string, object>; var t = d == null ? "" : J.Str(d, "token", "");
      if (t.Length == 0) { t = "ixc-" + U.Token(12).Replace("-", "a").Replace("_", "b"); Secrets.Set("multistream.ingest", J.D("token", t)); } return t; }
    public static bool Running { get { lock (L) return ingest != null; } }
    public static string Start() {
      lock (L) { if (ingest != null) return null; var i = new RtmpIngest(Token()); var err = i.Start(int.Parse(Ep.Get("multistream_ingest_port", "19350"))); if (err != null) return err; ingest = i; }
      Log.Info("stream", "multistream relay listening on 127.0.0.1:" + ingest.Port); return null; }
    public static void Stop() { RtmpIngest i; lock (L) { i = ingest; ingest = null; } if (i != null) { i.Stop(); Log.Info("stream", "multistream relay stopped"); } OnPublishEnd(); }
    public static void Headers(out RtmpMsg m, out RtmpMsg a, out RtmpMsg v) { lock (L) { m = meta; a = aSeq; v = vSeq; } }

    public static void OnPublishStart() {
      lock (L) {
        meta = aSeq = vSeq = null; Publishing = true; msgs = 0; Skipped.Clear();
        foreach (var kv in Outs) kv.Value.Stop(); Outs.Clear();
        foreach (var id in Multistream.Ids()) {
          var why = Multistream.NotReady(id); if (why != null) { Skipped[id] = why; continue; }
          var cid = id; var o = new RtmpOut(id, () => Multistream.Target(cid)); Outs[id] = o; o.Start(); } }
      Log.Info("stream", "multistream: OBS started streaming to IXC - " + Outs.Count + " destination(s)"); }
    public static void OnPublishEnd() {
      List<RtmpOut> l; lock (L) { Publishing = false; l = Outs.Values.ToList(); Outs.Clear(); meta = aSeq = vSeq = null; }
      foreach (var o in l) o.Stop(); if (l.Count > 0) Log.Info("stream", "multistream: OBS stopped streaming to IXC"); }
    public static void OnMessage(RtmpMsg m) {
      var p = m.Payload;
      if (m.Type == 9 && p.Length > 1) { byte b0 = p[0]; bool ex = (b0 & 0x80) != 0; int ft = ex ? (b0 >> 4) & 7 : b0 >> 4; m.Key = ft == 1; m.Header = ex ? (b0 & 0x0F) == 0 : ((b0 & 0x0F) == 7 && p[1] == 0); }
      else if (m.Type == 8 && p.Length > 1) m.Header = (p[0] >> 4) == 10 && p[1] == 0;
      RtmpOut[] outs;
      lock (L) { msgs++; if (m.Type == 9 && m.Header) vSeq = m; else if (m.Type == 8 && m.Header) aSeq = m; else if (m.Type == 18) { var l = Amf.Decode(p, 0); if (l.Count > 0 && (Amf.Str(l, 0) == "onMetaData" || Amf.Str(l, 0) == "@setDataFrame")) meta = m; }
        outs = Outs.Values.ToArray(); }
      foreach (var o in outs) o.Push(m); }
    public static void Drop(string id) { RtmpOut o; lock (L) { if (!Outs.TryGetValue(id, out o)) return; Outs.Remove(id); } o.Stop(); }
    // a saved change applies to a running destination: restart just that one
    public static void Refresh(string id) {
      lock (L) { if (!Publishing) return; RtmpOut o; if (Outs.TryGetValue(id, out o)) { o.Stop(); Outs.Remove(id); } Skipped.Remove(id);
        var why = Multistream.NotReady(id); if (why != null) { Skipped[id] = why; return; } var cid = id; var n = new RtmpOut(id, () => Multistream.Target(cid)); Outs[id] = n; n.Start(); } }
    public static Dictionary<string, object> OutView(string id) {
      RtmpOut o; string why = null; lock (L) { Outs.TryGetValue(id, out o); Skipped.TryGetValue(id, out why); }
      if (o != null) return J.D("state", o.State, "detail", o.Detail, "sentMB", Math.Round(Interlocked.Read(ref o.Bytes) / 1048576.0, 2), "frames", Interlocked.Read(ref o.Frames), "droppedFrames", Interlocked.Read(ref o.Dropped), "reconnects", o.Reconnects);
      return J.D("state", why != null ? "skipped" : "idle", "detail", why ?? "", "sentMB", 0.0, "frames", 0L, "droppedFrames", 0L, "reconnects", 0); }
    public static Dictionary<string, object> Status() {
      RtmpIngest i; long m; lock (L) { i = ingest; m = msgs; }
      return J.D("ready", true, "running", i != null, "publishing", Publishing,
        "serverUrl", i == null ? "" : "rtmp://127.0.0.1:" + i.Port + "/live", "streamKey", i == null ? "" : Token(),
        "howTo", "In OBS: Settings > Stream > Service: Custom. Server and Stream key as shown here. IXC then sends that one stream to every destination below. Nothing else in OBS changes - IXC does not edit your OBS settings.",
        "messagesIn", m); }
  }
}
