// IXC Core - foundation: JSON helpers, settings, encrypted secrets, logs, the WebSocket hub and HTTP plumbing.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License. C# 5 / .NET Framework 4.8 (built with the csc.exe that ships with Windows).
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace IXC {
  // ---------------- JSON ----------------
  public static class J {
    public static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static Dictionary<string, object> Parse(string s) { try { return S.Deserialize<Dictionary<string, object>>(s ?? "") ?? new Dictionary<string, object>(); } catch { return new Dictionary<string, object>(); } }
    public static bool TryParse(string s, out Dictionary<string, object> d) { d = null; try { d = S.Deserialize<Dictionary<string, object>>(s ?? ""); return d != null; } catch { return false; } }
    public static object ParseAny(string s) { try { return S.DeserializeObject(s ?? ""); } catch { return null; } }
    public static string Ser(object o) { return S.Serialize(o); }
    public static object Get(Dictionary<string, object> d, string path) {
      object cur = d; foreach (var p in path.Split('.')) { var dd = cur as Dictionary<string, object>; if (dd == null || !dd.ContainsKey(p)) return null; cur = dd[p]; } return cur; }
    public static string Str(Dictionary<string, object> d, string path, string def) { var v = Get(d, path); return v == null || v is Dictionary<string, object> || v is ArrayList ? def : Convert.ToString(v, Inv); }
    public static int Int(Dictionary<string, object> d, string path, int def) { var v = Get(d, path); try { return v == null ? def : Convert.ToInt32(v, Inv); } catch { return def; } }
    public static long Long(Dictionary<string, object> d, string path, long def) { var v = Get(d, path); try { return v == null ? def : Convert.ToInt64(v, Inv); } catch { return def; } }
    public static double Num(Dictionary<string, object> d, string path, double def) { var v = Get(d, path); try { return v == null ? def : Convert.ToDouble(v, Inv); } catch { return def; } }
    public static bool Bool(Dictionary<string, object> d, string path, bool def) { var v = Get(d, path); if (v is bool) return (bool)v; var s = v as string; if (s != null) { bool b; if (bool.TryParse(s, out b)) return b; } return def; }
    public static List<string> List(Dictionary<string, object> d, string path) {
      var v = Get(d, path) as IEnumerable; var r = new List<string>(); if (v == null || v is string) return r; foreach (var x in v) if (x != null && !(x is Dictionary<string, object>)) r.Add(Convert.ToString(x, Inv)); return r; }
    public static List<Dictionary<string, object>> Objs(Dictionary<string, object> d, string path) {
      var v = Get(d, path) as IEnumerable; var r = new List<Dictionary<string, object>>(); if (v == null || v is string) return r; foreach (var x in v) { var o = x as Dictionary<string, object>; if (o != null) r.Add(o); } return r; }
    public static Dictionary<string, object> Obj(Dictionary<string, object> d, string path) { return Get(d, path) as Dictionary<string, object>; }
    public static Dictionary<string, object> D(params object[] kv) { var d = new Dictionary<string, object>(); for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1]; return d; }
    // deep copy through the serializer (settings handed to pages must never be the live objects)
    public static Dictionary<string, object> Clone(Dictionary<string, object> d) { return d == null ? null : Parse(Ser(d)); }
    public static string Pretty(string json) {
      var sb = new StringBuilder(); int ind = 0; bool q = false;
      for (int i = 0; i < json.Length; i++) { char c = json[i];
        if (q) { sb.Append(c); if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); continue; } if (c == '"') q = false; continue; }
        if (c == '"') { q = true; sb.Append(c); }
        else if (c == '{' || c == '[') { sb.Append(c); if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); continue; } sb.Append("\r\n").Append(' ', (++ind) * 2); }
        else if (c == '}' || c == ']') { sb.Append("\r\n").Append(' ', Math.Max(0, --ind) * 2).Append(c); }
        else if (c == ',') { sb.Append(",\r\n").Append(' ', ind * 2); }
        else if (c == ':') sb.Append(": ");
        else sb.Append(c); }
      return sb.ToString(); }
  }

  // ---------------- small utilities ----------------
  public static class U {
    static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();
    public static string Token(int bytes) { var b = new byte[bytes]; lock (Rng) Rng.GetBytes(b); return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
    public static string Sha(string s) { using (var h = SHA256.Create()) return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""))); }
    public static string ShaHex(string s) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""))).Replace("-", "").ToLowerInvariant(); }
    public static bool SlowEq(string a, string b) { if (a == null || b == null || a.Length != b.Length) return false; int d = 0; for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i]; return d == 0; }
    public static int Rand(int max) { var b = new byte[4]; lock (Rng) Rng.GetBytes(b); return (int)(BitConverter.ToUInt32(b, 0) % (uint)Math.Max(1, max)); }
    public static long Now() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds; }
    public static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }
    public static string Trunc(string s, int n) { if (s == null) return ""; return s.Length > n ? s.Substring(0, n) : s; }
    // exponential back-off with jitter: 1 s, 2 s, 4 s ... capped, +-20 %
    public static int Backoff(int attempt, int minMs, int maxMs) { double b = Math.Min(maxMs, minMs * Math.Pow(2, Math.Min(attempt, 16))); return (int)(b * (0.8 + Rand(400) / 1000.0)); }
    public static string Plain(Exception e) { while ((e is AggregateException || e is System.Reflection.TargetInvocationException) && e.InnerException != null) e = e.InnerException; return e == null ? "error" : e.Message; }
  }

  // ---------------- logs: one file per area, a ring buffer for the dashboard, secrets are never written ----------------
  public static class Log {
    public class Entry { public DateTime At; public string Area, Level, Msg; }
    static readonly LinkedList<Entry> Recent = new LinkedList<Entry>(); static readonly object L = new object();
    static readonly BlockingCollection<KeyValuePair<string, string>> Q = new BlockingCollection<KeyValuePair<string, string>>(5000);
    public static string Dir; public static bool Verbose; public static int Errors, Warnings, Reconnects; public static DateTime Started = DateTime.Now;
    static readonly UTF8Encoding NoBom = new UTF8Encoding(false);
    public static readonly string[] Areas = { "app", "net", "obs", "streamerbot", "music", "chat", "tts", "phone", "auth", "overlay", "update" };
    static readonly Regex[] Secret = {
      new Regex("(oauth:|Bearer\\s+|access_token[\"=:\\s]+|refresh_token[\"=:\\s]+|client_secret[\"=:\\s]+|\"token\"\\s*:\\s*\"|password[\"=:\\s]+|[#&?](c|p|code)=)[^\\s\"&,}]+", RegexOptions.IgnoreCase | RegexOptions.Compiled) };
    public static void Init(string dir) {
      Dir = dir; try { Directory.CreateDirectory(dir); } catch { }
      var t = new Thread(Writer) { IsBackground = true, Name = "log" }; t.Start(); }
    public static string Redact(string s) { if (string.IsNullOrEmpty(s)) return s ?? ""; foreach (var r in Secret) s = r.Replace(s, m => m.Groups[1].Value + "[hidden]"); return s; }
    static void Add(string area, string level, string msg, bool write) {
      msg = Redact(msg); var e = new Entry { At = DateTime.Now, Area = area, Level = level, Msg = msg };
      lock (L) { Recent.AddLast(e); while (Recent.Count > 400) Recent.RemoveFirst(); }
      if (write && Dir != null) Q.TryAdd(new KeyValuePair<string, string>(area, e.At.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level.ToUpperInvariant().PadRight(5) + " " + msg)); }
    public static void Debug(string area, string msg) { Add(area, "debug", msg, Verbose); }
    public static void Info(string area, string msg) { Add(area, "info", msg, true); }
    public static void Warn(string area, string msg) { Interlocked.Increment(ref Warnings); Add(area, "warn", msg, true); }
    public static void Err(string area, string msg) { Interlocked.Increment(ref Errors); Add(area, "error", msg, true); }
    public static List<Dictionary<string, object>> Tail(int n, string area) {
      lock (L) return Recent.Where(e => area == null || e.Area == area).Reverse().Take(n).Reverse()
        .Select(e => J.D("at", e.At.ToString("HH:mm:ss"), "area", e.Area, "level", e.Level, "msg", e.Msg)).ToList(); }
    static void Writer() {
      foreach (var kv in Q.GetConsumingEnumerable()) {
        try { var f = Path.Combine(Dir, kv.Key + ".log");
          var fi = new FileInfo(f); if (fi.Exists && fi.Length > 1500000) { var old = f + ".1"; if (File.Exists(old)) File.Delete(old); File.Move(f, old); }
          var sb = new StringBuilder(kv.Value).Append("\r\n"); KeyValuePair<string, string> more; int n = 0;
          // batch lines of the same area that are already waiting (bursts of chat = one disk write)
          while (n++ < 200 && Q.TryTake(out more)) { if (more.Key == kv.Key) sb.Append(more.Value).Append("\r\n"); else { File.AppendAllText(Path.Combine(Dir, more.Key + ".log"), more.Value + "\r\n", NoBom); } }
          File.AppendAllText(f, sb.ToString(), NoBom); }
        catch { } } }
    public static void Flush() { for (int i = 0; i < 40 && Q.Count > 0; i++) Thread.Sleep(25); }
  }

  // ---------------- settings: %LOCALAPPDATA%\IXC-OBS\config.json ----------------
  // Atomic writes with a rolling backup. A corrupt file never stops IXC: it is kept aside and the last good copy is used.
  public static class Cfg {
    public static string FilePath, DataDir, AppRoot; public static Dictionary<string, object> D = new Dictionary<string, object>();
    public static string LoadProblem = "";
    public static readonly object L = new object();
    public static void Load(string path) {
      FilePath = path; DataDir = Path.GetDirectoryName(Path.GetFullPath(path)); Directory.CreateDirectory(DataDir);
      Dictionary<string, object> d = null; string why = null;
      if (File.Exists(path)) {
        string text = null; try { text = File.ReadAllText(path, Encoding.UTF8); } catch (Exception e) { why = "could not be read (" + e.Message + ")"; }
        if (text != null) { text = text.TrimStart('﻿'); if (text.Trim().Length == 0) why = "was empty"; else if (!J.TryParse(text, out d)) why = "was damaged (not valid JSON)"; } }
      if (d == null && why != null) {
        var keep = path + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); try { File.Copy(path, keep, true); } catch { }
        foreach (var b in new[] { path + ".bak", path + ".bak2" }) { Dictionary<string, object> bd; try { if (File.Exists(b) && J.TryParse(File.ReadAllText(b, Encoding.UTF8).TrimStart('﻿'), out bd)) { d = bd; why += " - restored the last good copy"; break; } } catch { } }
        if (d == null) why += " - started with default settings";
        LoadProblem = "Your settings file " + why + ". The damaged file was kept as " + Path.GetFileName(keep) + ".";
      }
      D = d ?? new Dictionary<string, object>();
      var before = J.Ser(D); Settings.Migrate(D);
      if (why != null || !File.Exists(path) || J.Ser(D) != before) SaveNow();
    }
    public static object Get(string path) { lock (L) return J.Get(D, path); }
    public static void Set(string path, object value) { lock (L) {
      var parts = path.Split('.'); var cur = D;
      for (int i = 0; i < parts.Length - 1; i++) { var nx = cur.ContainsKey(parts[i]) ? cur[parts[i]] as Dictionary<string, object> : null; if (nx == null) { nx = new Dictionary<string, object>(); cur[parts[i]] = nx; } cur = nx; }
      if (value == null) cur.Remove(parts[parts.Length - 1]); else cur[parts[parts.Length - 1]] = value; } }
    static Timer saveT;
    public static void Save() { lock (L) { if (saveT == null) saveT = new Timer(_ => SaveNow()); saveT.Change(500, Timeout.Infinite); } }   // debounced: sliders don't hammer the disk
    public static void SaveNow() {
      string json; lock (L) json = J.Pretty(J.Ser(D));
      lock (saveLock) {
        try { var tmp = FilePath + ".tmp"; File.WriteAllText(tmp, json, new UTF8Encoding(false));
          if (File.Exists(FilePath)) { try { if (File.Exists(FilePath + ".bak")) File.Copy(FilePath + ".bak", FilePath + ".bak2", true); } catch { } File.Replace(tmp, FilePath, FilePath + ".bak"); } else File.Move(tmp, FilePath); }
        catch (Exception e) { Log.Err("app", "settings save failed: " + e.Message); } } }
    static readonly object saveLock = new object();
    public static string Snapshot() { lock (L) return J.Ser(D); }
  }

  // ---------------- secrets: tokens, device keys - encrypted for this Windows user (DPAPI), never in config.json or logs ----------------
  public static class Secrets {
    static Dictionary<string, object> S = new Dictionary<string, object>(); static string file; static readonly object L = new object();
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("IXC-OBS secrets v1");
    public static string Problem = "";
    public static void Load(string dir) {
      file = Path.Combine(dir, "secrets.dat");
      if (!File.Exists(file)) return;
      try { var raw = ProtectedData.Unprotect(File.ReadAllBytes(file), Entropy, DataProtectionScope.CurrentUser); S = J.Parse(Encoding.UTF8.GetString(raw)); }
      catch (Exception e) { Problem = "Saved sign-ins could not be read (" + e.Message + "). Please connect your accounts again."; Log.Err("auth", "secrets unreadable: " + e.Message); try { File.Copy(file, file + ".unreadable", true); } catch { } S = new Dictionary<string, object>(); } }
    public static object Get(string key) { lock (L) { object v; return S.TryGetValue(key, out v) ? v : null; } }
    public static Dictionary<string, object> Obj(string key) { var v = Get(key) as Dictionary<string, object>; return v == null ? null : J.Clone(v); }
    public static string Str(string key) { return Get(key) as string; }
    public static void Set(string key, object value) { lock (L) { if (value == null) S.Remove(key); else S[key] = value; } Save(); }
    public static bool Has(string key) { lock (L) return S.ContainsKey(key); }
    public static void Save() {
      byte[] data; lock (L) data = Encoding.UTF8.GetBytes(J.Ser(S));
      try { var enc = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser); var tmp = file + ".tmp"; File.WriteAllBytes(tmp, enc);
        if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file); }
      catch (Exception e) { Log.Err("auth", "secrets save failed: " + e.Message); } }
    public static List<string> Keys() { lock (L) return S.Keys.ToList(); }
  }

  // ---------------- WebSocket hub: every page / phone keeps ONE connection and subscribes to topics ----------------
  // A client is any transport (local WebSocket, or a phone through the relay). Messages to one client are sent strictly in
  // order by a single drain loop; a client that stops reading is dropped instead of growing memory.
  public class Client {
    public string Id = U.Token(6), Role = "?", Ip = "", Device = "", DeviceId = ""; public bool Remote, Authed;
    public HashSet<string> Topics = new HashSet<string>(); public DateTime Connected = DateTime.Now, LastSeen = DateTime.Now;
    public Func<string, Task> Transport; public Action Kill;
    public int MsgCount, Rtt; public DateTime MsgWindow = DateTime.Now;
    internal readonly ConcurrentQueue<string> Out = new ConcurrentQueue<string>(); internal int Draining, Queued; internal volatile bool Dead;
  }
  public static class Hub {
    public static readonly List<Client> Clients = new List<Client>();
    public static Action<Client, string, Dictionary<string, object>> OnMessage;
    public static int Count(string role) { lock (Clients) return Clients.Count(c => c.Role == role && (!c.Remote || c.Authed)); }
    public static int CountRemote() { lock (Clients) return Clients.Count(c => c.Remote && c.Authed); }
    public static List<Client> Remotes() { lock (Clients) return Clients.Where(c => c.Remote && c.Authed).ToList(); }
    public static bool AnyTopic(string topic) { lock (Clients) return Clients.Any(c => c.Topics.Contains(topic)); }
    public static void Publish(string topic, object msg) { PublishRaw(topic, J.Ser(msg)); }
    public static void PublishRaw(string topic, string json) {
      List<Client> list; lock (Clients) list = Clients.Where(c => c.Topics.Contains(topic)).ToList();
      foreach (var c in list) Enqueue(c, json); }
    public static void Send(Client c, object msg) { Enqueue(c, msg is string ? (string)msg : J.Ser(msg)); }
    static void Enqueue(Client c, string json) {
      if (c.Dead) return;
      if (Interlocked.Increment(ref c.Queued) > 400) { c.Dead = true; Log.Warn("net", "a page stopped reading (" + c.Role + ") - disconnected it"); try { if (c.Kill != null) c.Kill(); } catch { } return; }
      c.Out.Enqueue(json);
      if (Interlocked.CompareExchange(ref c.Draining, 1, 0) == 0) Task.Run(() => Drain(c)); }
    static async Task Drain(Client c) {
      while (true) {
        string s;
        while (c.Out.TryDequeue(out s)) { Interlocked.Decrement(ref c.Queued); if (c.Dead) continue;
          try { var t = c.Transport(s); if (await Task.WhenAny(t, Task.Delay(15000)) != t) throw new TimeoutException("send timed out"); await t; }
          catch { c.Dead = true; try { if (c.Kill != null) c.Kill(); } catch { } } }
        Interlocked.Exchange(ref c.Draining, 0);
        if (c.Out.IsEmpty || Interlocked.CompareExchange(ref c.Draining, 1, 0) != 0) return; } }
    public static void Add(Client c) { lock (Clients) Clients.Add(c); }
    public static void Remove(Client c) {
      bool was; lock (Clients) was = Clients.Remove(c); c.Dead = true;
      if (was && OnMessage != null) { try { OnMessage(c, "_closed", null); } catch (Exception e) { Log.Err("net", "close handler: " + e.Message); } } }
    // one incoming text message from any client (local or phone)
    public static void Handle(Client c, string text) {
      c.LastSeen = DateTime.Now;
      Dictionary<string, object> msg; if (!J.TryParse(text, out msg)) { Send(c, J.D("type", "error", "error", "bad message")); return; }
      var type = J.Str(msg, "type", "");
      if (c.Remote) {
        if (!c.Authed) return;
        if ((DateTime.Now - c.MsgWindow).TotalSeconds > 10) { c.MsgWindow = DateTime.Now; c.MsgCount = 0; }
        if (++c.MsgCount > 80) { Send(c, J.D("type", "error", "error", "too many messages - slow down")); return; }
        if (!Remote.AllowedMessage(type)) { Send(c, J.D("type", "error", "error", "not available from a phone: " + type)); return; } }
      if (type == "hello") { if (!c.Remote) c.Role = U.Trunc(J.Str(msg, "role", "page"), 24); lock (Clients) foreach (var t in J.List(msg, "topics")) if (!c.Remote || Remote.AllowedTopic(t)) c.Topics.Add(t); }
      if (OnMessage != null) { try { OnMessage(c, type, msg); } catch (Exception e) { Log.Err("app", "message " + type + ": " + e.Message); Send(c, J.D("type", "error", "error", "IXC could not do that: " + e.Message, "reqId", J.Str(msg, "reqId", ""))); } } }

    // a local page (OBS dock / source / dashboard) on /ws
    public static void RunLocal(Ctx ctx) {
      var ws = Web.Accept(ctx); var c = new Client { Ip = "local", Authed = true };
      c.Transport = s => ws.SendText(s); c.Kill = () => ws.Abort();
      Add(c);
      Task.Run(async () => {
        try { while (ws.Open) { var text = await ws.Receive(1048576, 120000); if (text == null) break; Handle(c, text); } }
        catch { }
        finally { Remove(c); ws.Abort(); } }); }
    public static async Task<string> ReceiveText(WebSocket ws, byte[] buf, MemoryStream ms, int max) {
      ms.SetLength(0); WebSocketReceiveResult r;
      do { r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None); if (r.MessageType == WebSocketMessageType.Close) return null; ms.Write(buf, 0, r.Count); if (ms.Length > max) return null; } while (!r.EndOfMessage);
      return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length); }
  }

  // ---------------- HTTP plumbing ----------------
  public static class Http {
    public static readonly Dictionary<string, string> Types = new Dictionary<string, string> { { ".html", "text/html; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" }, { ".css", "text/css; charset=utf-8" },
      { ".png", "image/png" }, { ".svg", "image/svg+xml" }, { ".ico", "image/x-icon" }, { ".json", "application/json" }, { ".webmanifest", "application/manifest+json" }, { ".woff2", "font/woff2" },
      { ".txt", "text/plain; charset=utf-8" } };
    public static void Send(Ctx c, int code, string body, string type) { SendBytes(c, code, Encoding.UTF8.GetBytes(body ?? ""), type ?? "application/json; charset=utf-8"); }
    public static void Json(Ctx c, object o) { Send(c, 200, J.Ser(o), null); }
    public static void Json(Ctx c, int code, object o) { Send(c, code, J.Ser(o), null); }
    static void Headers(Ctx c) {
      var r = c.Response; r.Headers["Cache-Control"] = "no-store"; r.Headers["X-Content-Type-Options"] = "nosniff"; r.Headers["Referrer-Policy"] = "no-referrer";
      var o = c.Request.Headers["Origin"]; if (!string.IsNullOrEmpty(o) && Program.LocalOrigin(o)) r.Headers["Access-Control-Allow-Origin"] = o; }
    public static void SendBytes(Ctx c, int code, byte[] b, string type) {
      var r = c.Response; try { r.StatusCode = code; r.ContentType = type; Headers(c); r.ContentLength64 = b.Length; if (c.Request.HttpMethod != "HEAD") r.OutputStream.Write(b, 0, b.Length); }
      catch { } finally { try { r.Close(); } catch { } } }
    // streams a file with HTTP Range support (needed for seeking in local music)
    public static void SendFile(Ctx c, string file, string type) {
      var r = c.Response;
      try { using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
          long len = fs.Length, from = 0, to = len - 1; var range = c.Request.Headers["Range"];
          var m = range == null ? Match.Empty : Regex.Match(range, "^bytes=(\\d*)-(\\d*)$");
          if (m.Success) { if (m.Groups[1].Value.Length > 0) { from = long.Parse(m.Groups[1].Value); if (m.Groups[2].Value.Length > 0) to = Math.Min(len - 1, long.Parse(m.Groups[2].Value)); }
            else if (m.Groups[2].Value.Length > 0) { from = Math.Max(0, len - long.Parse(m.Groups[2].Value)); }
            if (from > to || from >= len) { r.StatusCode = 416; r.Headers["Content-Range"] = "bytes */" + len; r.Close(); return; }
            r.StatusCode = 206; r.Headers["Content-Range"] = "bytes " + from + "-" + to + "/" + len; } else r.StatusCode = 200;
          r.ContentType = type; Headers(c); r.Headers["Accept-Ranges"] = "bytes"; r.ContentLength64 = to - from + 1;
          if (c.Request.HttpMethod == "HEAD") return;
          fs.Position = from; var buf = new byte[65536]; long left = to - from + 1;
          while (left > 0) { int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left)); if (n <= 0) break; r.OutputStream.Write(buf, 0, n); left -= n; } } }
      catch { try { r.StatusCode = 500; } catch { } }
      finally { try { r.Close(); } catch { } } }
    public static string Body(Ctx c) { if (c.Request.ContentLength64 > 262144) return "";
      using (var sr = new StreamReader(c.Request.InputStream, Encoding.UTF8)) { var buf = new char[262145]; int n = 0, k; while (n < buf.Length && (k = sr.Read(buf, n, buf.Length - n)) > 0) n += k; return n > 262144 ? "" : new string(buf, 0, n); } }
    public static Dictionary<string, object> BodyJson(Ctx c) { return J.Parse(Body(c)); }
    // files under the known page folders only; no directory escapes; only known file types
    public static bool Static(Ctx c, string path, Dictionary<string, string> roots) {
      string rel; try { rel = Uri.UnescapeDataString(path.TrimStart('/')); } catch { return false; }
      if (rel.IndexOf('\0') >= 0 || rel.Contains("..")) return false;
      var parts = rel.Split(new[] { '/' }, 2); if (parts.Length < 2 || !roots.ContainsKey(parts[0])) return false;
      if (parts[1].Length == 0) parts[1] = "index.html";
      string baseDir = Path.GetFullPath(roots[parts[0]]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
      string f; try { f = Path.GetFullPath(Path.Combine(baseDir, parts[1].Replace('/', Path.DirectorySeparatorChar))); } catch { return false; }
      string ext = Path.GetExtension(f).ToLowerInvariant();
      if (!f.StartsWith(baseDir, U.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !Types.ContainsKey(ext) || !File.Exists(f)) return false;
      SendBytes(c, 200, File.ReadAllBytes(f), Types[ext]); return true; }
    // outgoing requests (platform APIs, YouTube, updates)
    public const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
    public class Resp { public int Code; public string Body, Error; public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); public bool Ok { get { return Code >= 200 && Code < 300; } } }
    public static Resp Request(string method, string url, string body, string contentType, Dictionary<string, string> headers, int timeoutMs) {
      var res = new Resp();
      try { var r = (HttpWebRequest)WebRequest.Create(url); r.Method = method; r.Timeout = timeoutMs; r.ReadWriteTimeout = timeoutMs; r.UserAgent = UA;
        r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; r.Headers["Accept-Language"] = "en-US,en;q=0.9";
        if (headers != null) foreach (var kv in headers) { if (kv.Key == "Accept") r.Accept = kv.Value; else if (kv.Key == "User-Agent") r.UserAgent = kv.Value; else if (kv.Key == "Referer") r.Referer = kv.Value; else r.Headers[kv.Key] = kv.Value; }
        if (body != null) { var b = Encoding.UTF8.GetBytes(body); r.ContentType = contentType ?? "application/json"; r.ContentLength = b.Length; using (var st = r.GetRequestStream()) st.Write(b, 0, b.Length); }
        using (var resp = (HttpWebResponse)r.GetResponse()) Read(resp, res); }
      catch (WebException we) { var hr = we.Response as HttpWebResponse; if (hr != null) { try { Read(hr, res); } catch { } } else res.Error = we.Message; if (res.Error == null) res.Error = "HTTP " + res.Code; }
      catch (Exception e) { res.Error = e.Message; }
      return res; }
    static void Read(HttpWebResponse resp, Resp res) {
      res.Code = (int)resp.StatusCode; foreach (string k in resp.Headers.AllKeys) res.Headers[k] = resp.Headers[k];
      using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) { var buf = new char[8192]; var sb = new StringBuilder(); int n; while ((n = sr.Read(buf, 0, buf.Length)) > 0) { sb.Append(buf, 0, n); if (sb.Length > 8000000) break; } res.Body = sb.ToString(); } }
    public static Resp Get(string url, int timeoutMs) { return Request("GET", url, null, null, null, timeoutMs); }
  }
}
