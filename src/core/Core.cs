// IXC Core - one small native process for IXC Music + IXC ChatBox (replaces the PowerShell helper + chat relay of v1).
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License. C# 5 / .NET Framework 4.8, compiled on your PC by the csc.exe that ships with Windows.
//   local:  http://localhost:<helper.port>/   pages for OBS, HTTP API, and one WebSocket (/ws) that pushes live updates (no polling)
//   remote: http://127.0.0.1:<remote.port>/   only the phone page + pairing + an authenticated WebSocket; reached through a Cloudflare tunnel
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace IXC {
  // ---------------- small JSON helpers ----------------
  public static class J {
    public static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
    public static Dictionary<string, object> Parse(string s) { try { return S.Deserialize<Dictionary<string, object>>(s) ?? new Dictionary<string, object>(); } catch { return new Dictionary<string, object>(); } }
    public static string Ser(object o) { return S.Serialize(o); }
    public static object Get(Dictionary<string, object> d, string path) {
      object cur = d; foreach (var p in path.Split('.')) { var dd = cur as Dictionary<string, object>; if (dd == null || !dd.ContainsKey(p)) return null; cur = dd[p]; } return cur; }
    public static string Str(Dictionary<string, object> d, string path, string def) { var v = Get(d, path); return v == null ? def : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture); }
    public static int Int(Dictionary<string, object> d, string path, int def) { var v = Get(d, path); try { return v == null ? def : Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return def; } }
    public static double Num(Dictionary<string, object> d, string path, double def) { var v = Get(d, path); try { return v == null ? def : Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return def; } }
    public static bool Bool(Dictionary<string, object> d, string path, bool def) { var v = Get(d, path); if (v is bool) return (bool)v; if (v is string) { bool b; if (bool.TryParse((string)v, out b)) return b; } return def; }
    public static List<string> List(Dictionary<string, object> d, string path) {
      var v = Get(d, path) as IEnumerable; var r = new List<string>(); if (v == null || v is string) return r; foreach (var x in v) if (x != null) r.Add(Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture)); return r; }
    public static Dictionary<string, object> Obj(Dictionary<string, object> d, string path) { return Get(d, path) as Dictionary<string, object>; }
    public static Dictionary<string, object> D(params object[] kv) { var d = new Dictionary<string, object>(); for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1]; return d; }
    // readable JSON for config.json (the serializer writes one long line)
    public static string Pretty(string json) {
      var sb = new StringBuilder(); int ind = 0; bool q = false;
      for (int i = 0; i < json.Length; i++) { char c = json[i];
        if (q) { sb.Append(c); if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); continue; } if (c == '"') q = false; continue; }
        if (c == '"') { q = true; sb.Append(c); }
        else if (c == '{' || c == '[') { sb.Append(c); if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); continue; } sb.Append("\r\n").Append(' ', (++ind) * 2); }
        else if (c == '}' || c == ']') { sb.Append("\r\n").Append(' ', (--ind) * 2).Append(c); }
        else if (c == ',') { sb.Append(",\r\n").Append(' ', ind * 2); }
        else if (c == ':') sb.Append(": ");
        else sb.Append(c); }
      return sb.ToString(); }
  }

  // ---------------- configuration (%LOCALAPPDATA%\IXC-OBS\config.json) ----------------
  public static class Cfg {
    public static string Path, DataDir, AppRoot; public static Dictionary<string, object> D = new Dictionary<string, object>();
    static readonly object L = new object();
    public static void Load(string path) {
      Path = path; DataDir = System.IO.Path.GetDirectoryName(path); Directory.CreateDirectory(DataDir);
      if (File.Exists(path)) D = J.Parse(File.ReadAllText(path, Encoding.UTF8)); }
    public static void Set(string path, object value) { lock (L) {
      var parts = path.Split('.'); var cur = D;
      for (int i = 0; i < parts.Length - 1; i++) { var nx = cur.ContainsKey(parts[i]) ? cur[parts[i]] as Dictionary<string, object> : null; if (nx == null) { nx = new Dictionary<string, object>(); cur[parts[i]] = nx; } cur = nx; }
      cur[parts[parts.Length - 1]] = value; } }
    static Timer saveT;
    public static void Save() { lock (L) { if (saveT == null) saveT = new Timer(_ => SaveNow()); saveT.Change(400, Timeout.Infinite); } }   // debounced: sliders don't hammer the disk
    public static void SaveNow() { lock (L) { try { var tmp = Path + ".tmp"; File.WriteAllText(tmp, J.Pretty(J.Ser(D)), new UTF8Encoding(false)); if (File.Exists(Path)) File.Replace(tmp, Path, null); else File.Move(tmp, Path); } catch (Exception e) { Diag.Err("config save: " + e.Message); } } }
  }

  // ---------------- diagnostics: event log + counters (cheap; the panel only samples while it is open) ----------------
  public static class Diag {
    static readonly LinkedList<string> Events = new LinkedList<string>(); static readonly object L = new object();
    public static int Errors, Reconnects; public static DateTime Started = DateTime.Now; public static bool Verbose, Enabled = true;
    static string logFile;
    public static void Init() { logFile = System.IO.Path.Combine(Cfg.DataDir, "ixc-core.log"); try { if (File.Exists(logFile) && new FileInfo(logFile).Length > 2000000) File.Delete(logFile); } catch { } }
    static void Add(string line) { lock (L) { Events.AddLast(line); while (Events.Count > 200) Events.RemoveFirst(); } }
    public static void Ev(string m) { var line = DateTime.Now.ToString("HH:mm:ss") + " " + m; Add(line); if (Verbose) Write(line); }
    public static void Info(string m) { var line = DateTime.Now.ToString("HH:mm:ss") + " " + m; Add(line); Write(line); }
    public static void Err(string m) { Interlocked.Increment(ref Errors); var line = DateTime.Now.ToString("HH:mm:ss") + " ERROR " + m; Add(line); Write(line); }
    static void Write(string line) { try { lock (L) File.AppendAllText(logFile, DateTime.Now.ToString("yyyy-MM-dd ") + line + "\r\n"); } catch { } }
    public static List<string> Recent(int n) { lock (L) { return Events.Skip(Math.Max(0, Events.Count - n)).ToList(); } }
    static TimeSpan lastCpu; static DateTime lastT = DateTime.MinValue; static double lastPct;
    public static Dictionary<string, object> Process() {
      var p = System.Diagnostics.Process.GetCurrentProcess(); var now = DateTime.Now;
      lock (L) { if (lastT != DateTime.MinValue) { var dt = (now - lastT).TotalMilliseconds; if (dt > 200) lastPct = (p.TotalProcessorTime - lastCpu).TotalMilliseconds / dt / Environment.ProcessorCount * 100; }
        lastCpu = p.TotalProcessorTime; lastT = now; }
      return J.D("pid", p.Id, "cpuPercent", Math.Round(lastPct, 2), "ramMB", Math.Round(p.WorkingSet64 / 1048576.0, 1), "privateMB", Math.Round(p.PrivateMemorySize64 / 1048576.0, 1),
        "threads", p.Threads.Count, "handles", p.HandleCount, "uptimeSec", (int)(now - Started).TotalSeconds, "errors", Errors, "reconnects", Reconnects); }
  }

  // ---------------- WebSocket hub: every page keeps ONE connection and subscribes to topics ----------------
  public class Client {
    public WebSocket Ws; public string Role = "?"; public HashSet<string> Topics = new HashSet<string>(); public bool Remote; public string Session;
    public readonly SemaphoreSlim SendLock = new SemaphoreSlim(1, 1); public DateTime Connected = DateTime.Now; public string Ip = "";
    public int MsgCount, Pending; public DateTime MsgWindow = DateTime.Now;
  }
  public static class Hub {
    public static readonly List<Client> Clients = new List<Client>();
    public static Action<Client, string, Dictionary<string, object>> OnMessage;   // set by Program
    public static int Count(string role) { lock (Clients) return Clients.Count(c => c.Role == role); }
    public static int CountRemote() { lock (Clients) return Clients.Count(c => c.Remote); }
    public static bool AnyTopic(string topic) { lock (Clients) return Clients.Any(c => c.Topics.Contains(topic)); }
    public static List<Client> ByRole(string role) { lock (Clients) return Clients.Where(c => c.Role == role).ToList(); }
    public static void Publish(string topic, object msg) { PublishRaw(topic, J.Ser(msg)); }
    public static void PublishRaw(string topic, string json) {
      List<Client> list; lock (Clients) list = Clients.Where(c => c.Topics.Contains(topic)).ToList(); if (list.Count == 0) return;
      var b = Encoding.UTF8.GetBytes(json); foreach (var c in list) SendBytes(c, b); }
    public static void Send(Client c, object msg) { SendBytes(c, Encoding.UTF8.GetBytes(msg is string ? (string)msg : J.Ser(msg))); }
    static void SendBytes(Client c, byte[] b) {
      if (Interlocked.Increment(ref c.Pending) > 200) { Interlocked.Decrement(ref c.Pending); try { c.Ws.Abort(); } catch { } return; }   // a stuck client can't grow memory forever
      Task.Run(async () => { try { if (!await c.SendLock.WaitAsync(5000)) return;
          try { if (c.Ws.State == WebSocketState.Open) await c.Ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None); } catch { } finally { c.SendLock.Release(); } }
        catch { } finally { Interlocked.Decrement(ref c.Pending); } }); }
    public static async Task Run(HttpListenerContext ctx, bool remote, string ip) {
      WebSocketContext wsc; try { wsc = await ctx.AcceptWebSocketAsync(null, TimeSpan.FromSeconds(20)); } catch { try { ctx.Response.StatusCode = 400; ctx.Response.Close(); } catch { } return; }
      var c = new Client { Ws = wsc.WebSocket, Remote = remote, Ip = ip };
      var buf = new byte[16384]; var ms = new MemoryStream();
      try {
        if (remote) {   // a phone must prove its session within 5 s, before it is registered or can subscribe to anything
          var first = ReceiveText(c.Ws, buf, ms, 16384); if (await Task.WhenAny(first, Task.Delay(5000)) != first || first.Result == null) return;
          string tok = J.Str(J.Parse(first.Result), "session", "");
          if (!Remote.CheckSession(tok, ip)) { await SendNow(c, J.Ser(J.D("type", "auth", "ok", false))); return; }
          c.Session = tok; c.Role = "mobile"; await SendNow(c, J.Ser(J.D("type", "auth", "ok", true, "version", Program.Version))); Diag.Ev("phone connected (" + ip + ")");
        }
        lock (Clients) Clients.Add(c);
        while (c.Ws.State == WebSocketState.Open) {
          var text = await ReceiveText(c.Ws, buf, ms, remote ? 16384 : 1048576); if (text == null) break;
          if (c.Remote) {
            if ((DateTime.Now - c.MsgWindow).TotalSeconds > 10) { c.MsgWindow = DateTime.Now; c.MsgCount = 0; }
            if (++c.MsgCount > 60) { Diag.Ev("phone flood limit - disconnected"); break; }
            if (!Remote.CheckSession(c.Session, ip)) { await SendNow(c, J.Ser(J.D("type", "auth", "ok", false))); break; } }
          var msg = J.Parse(text); var type = J.Str(msg, "type", "");
          if (c.Remote && !Remote.AllowedMessage(type)) { Send(c, J.D("type", "error", "error", "not allowed from a phone: " + type)); continue; }
          if (type == "hello") { if (!c.Remote) c.Role = J.Str(msg, "role", "page"); lock (Clients) foreach (var t in J.List(msg, "topics")) if (!c.Remote || Remote.AllowedTopic(t)) c.Topics.Add(t); }
          if (OnMessage != null) { try { OnMessage(c, type, msg); } catch (Exception e) { Diag.Err("ws " + type + ": " + e.Message); } }
        }
      } catch { }
      finally { bool was; lock (Clients) was = Clients.Remove(c); if (c.Remote && was) Diag.Ev("phone disconnected"); if (was && OnMessage != null) try { OnMessage(c, "_closed", null); } catch { }
        try { c.Ws.Dispose(); } catch { } }
    }
    static async Task SendNow(Client c, string json) { var b = Encoding.UTF8.GetBytes(json); try { await c.Ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None); } catch { } }
    static async Task<string> ReceiveText(WebSocket ws, byte[] buf, MemoryStream ms, int max) {
      ms.SetLength(0); WebSocketReceiveResult r;
      do { r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None); if (r.MessageType == WebSocketMessageType.Close) return null; ms.Write(buf, 0, r.Count); if (ms.Length > max) return null; } while (!r.EndOfMessage);
      return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length); }
  }

  // ---------------- HTTP plumbing ----------------
  public static class Http {
    public static readonly Dictionary<string, string> Types = new Dictionary<string, string> { { ".html", "text/html; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" }, { ".css", "text/css; charset=utf-8" },
      { ".png", "image/png" }, { ".svg", "image/svg+xml" }, { ".ico", "image/x-icon" }, { ".json", "application/json" }, { ".webmanifest", "application/manifest+json" } };
    public static void Send(HttpListenerContext c, int code, string body, string type) { SendBytes(c, code, Encoding.UTF8.GetBytes(body ?? ""), type ?? "application/json; charset=utf-8"); }
    public static void Json(HttpListenerContext c, object o) { Send(c, 200, J.Ser(o), null); }
    public static void Json(HttpListenerContext c, int code, object o) { Send(c, code, J.Ser(o), null); }
    public static void SendBytes(HttpListenerContext c, int code, byte[] b, string type) {
      var r = c.Response; try { r.StatusCode = code; r.ContentType = type; r.Headers["Cache-Control"] = "no-store"; r.Headers["X-Content-Type-Options"] = "nosniff";
        bool local = c.Request.LocalEndPoint.Port == Program.Port;
        var o = c.Request.Headers["Origin"]; if (local && !string.IsNullOrEmpty(o) && o != "null" && Program.LocalOrigin(o)) r.Headers["Access-Control-Allow-Origin"] = o;
        if (!local) { r.Headers["X-Frame-Options"] = "DENY"; r.Headers["Referrer-Policy"] = "no-referrer"; }
        r.ContentLength64 = b.Length; r.OutputStream.Write(b, 0, b.Length); } catch { } finally { try { r.Close(); } catch { } } }
    public static string Body(HttpListenerContext c) { if (c.Request.ContentLength64 > 262144) return "";
      using (var sr = new StreamReader(c.Request.InputStream, Encoding.UTF8)) { var buf = new char[262145]; int n = 0, k; while (n < buf.Length && (k = sr.Read(buf, n, buf.Length - n)) > 0) n += k; return n > 262144 ? "" : new string(buf, 0, n); } }
    // files under <app>/music, <app>/chat, <app>/core/web only; no directory escapes; only known file types
    public static bool Static(HttpListenerContext c, string path, Dictionary<string, string> roots) {
      string rel = Uri.UnescapeDataString(path.TrimStart('/')); var parts = rel.Split(new[] { '/' }, 2); if (parts.Length < 2 || !roots.ContainsKey(parts[0])) return false;
      string baseDir = System.IO.Path.GetFullPath(roots[parts[0]]).TrimEnd('\\') + "\\";
      string f; try { f = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, parts[1])); } catch { return false; }
      string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
      if (!f.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) || !Types.ContainsKey(ext) || !File.Exists(f)) return false;
      SendBytes(c, 200, File.ReadAllBytes(f), Types[ext]); return true; }
  }

  // ---------------- program ----------------
  public static class Program {
    public static int Port = 8767; public static bool HasMusic, HasChat, TestMode; public static string Version = "2.0.0";
    public static readonly Dictionary<string, string> Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public static bool LocalOrigin(string o) { if (string.IsNullOrEmpty(o) || o == "null") return true;
      Uri u; return Uri.TryCreate(o, UriKind.Absolute, out u) && (u.Host == "localhost" || u.Host == "127.0.0.1") && (u.Scheme == "http" || u.Scheme == "https"); }

    public static void Main(string[] args) {
      string cfgPath = null;
      for (int i = 0; i < args.Length; i++) { if (args[i] == "--config" && i + 1 < args.Length) cfgPath = args[++i]; else if (args[i] == "--test") TestMode = true; }
      if (cfgPath == null) cfgPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IXC-OBS", "config.json");
      Cfg.AppRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
      Cfg.Load(cfgPath); Diag.Init(); Diag.Verbose = J.Bool(Cfg.D, "diagnostics.verboseLog", false); Diag.Enabled = J.Bool(Cfg.D, "diagnostics.enabled", true);
      try { Version = File.ReadAllText(System.IO.Path.Combine(Cfg.AppRoot, "core", "VERSION")).Trim(); } catch { }
      Port = J.Int(Cfg.D, "helper.port", 8767);
      foreach (var a in new[] { "music", "chat" }) { var d = System.IO.Path.Combine(Cfg.AppRoot, a); if (Directory.Exists(d)) Roots[a] = d; }
      Roots["core"] = System.IO.Path.Combine(Cfg.AppRoot, "core", "web");
      HasMusic = Roots.ContainsKey("music"); HasChat = Roots.ContainsKey("chat");
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; ServicePointManager.DefaultConnectionLimit = 16; ServicePointManager.Expect100Continue = false;
      bool created; var mutex = new Mutex(true, "Local\\IXC.Core." + Port, out created); if (!created) { Console.WriteLine("IXC Core is already running on port " + Port); return; }
      var l = new HttpListener(); l.Prefixes.Add("http://localhost:" + Port + "/"); l.Prefixes.Add("http://127.0.0.1:" + Port + "/");
      try { l.Start(); } catch (Exception e) { Diag.Err("port " + Port + " is busy (" + e.Message + ") - is the old IXC helper still running?"); return; }
      Diag.Info("IXC Core " + Version + " started on port " + Port + " (music: " + HasMusic + ", chat: " + HasChat + (TestMode ? ", TEST MODE" : "") + ")");
      Hub.OnMessage = OnWs;
      if (HasChat) { Tts.Init(); Chat.Init(); }
      if (HasMusic) Music.Init();
      Remote.Init();
      WatchFiles();
      diagTimer = new Timer(_ => { try { if (Hub.AnyTopic("diag")) Hub.Publish("diag", J.D("type", "diag", "d", DiagSnapshot(false))); } catch { } }, null, 2000, 2000);
      new Thread(() => Loop(l, false)) { IsBackground = true, Name = "http-local" }.Start();
      AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { Remote.Shutdown(); Cfg.SaveNow(); } catch { } };
      Thread.Sleep(Timeout.Infinite); GC.KeepAlive(mutex);
    }
    static Timer diagTimer, reloadT; static FileSystemWatcher fsw;
    // pages reload themselves when a newer version of a page is installed (replaces the old 5-second /api/version polling)
    static void WatchFiles() {
      try { reloadT = new Timer(_ => { Diag.Ev("page files changed - asking pages to reload"); Hub.Publish("reload", J.D("type", "reload", "v", DateTime.UtcNow.Ticks)); });
        fsw = new FileSystemWatcher(Cfg.AppRoot) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
        FileSystemEventHandler h = (s, e) => { var x = System.IO.Path.GetExtension(e.FullPath).ToLowerInvariant(); if (x == ".html" || x == ".js" || x == ".css") reloadT.Change(1500, Timeout.Infinite); };
        fsw.Changed += h; fsw.Created += h; fsw.Renamed += (s, e) => h(s, e); fsw.EnableRaisingEvents = true; } catch (Exception e) { Diag.Err("file watch: " + e.Message); } }
    public static void Loop(HttpListener l, bool remote) {
      while (l.IsListening) { HttpListenerContext ctx; try { ctx = l.GetContext(); } catch { break; }
        ThreadPool.QueueUserWorkItem(_ => { try { Handle(ctx, remote); } catch (Exception e) { Diag.Err("http " + ctx.Request.Url.AbsolutePath + ": " + e.Message); try { Http.Json(ctx, 500, J.D("error", e.Message)); } catch { } } }); } }

    static readonly Dictionary<string, string> Alias = new Dictionary<string, string> { { "/player.html", "/music/player.html" }, { "/dock.html", "/music/dock.html" }, { "/tts.html", "/chat/tts.html" },
      { "/diag", "/core/diag.html" }, { "/diag.html", "/core/diag.html" }, { "/m", "/core/mobile.html" } };
    public static void Handle(HttpListenerContext ctx, bool remote) {
      var rq = ctx.Request; string path = rq.Url.AbsolutePath;
      if (remote) { Remote.Handle(ctx, path); return; }
      if ((path.StartsWith("/api/") || path == "/ws") && !LocalOrigin(rq.Headers["Origin"])) { Http.Json(ctx, 403, J.D("error", "origin not allowed")); return; }   // random websites can't drive IXC
      if (rq.HttpMethod == "OPTIONS") { ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST"; ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type"; Http.Send(ctx, 204, "", "text/plain"); return; }
      if (path == "/ws") { if (rq.IsWebSocketRequest) { var t = Hub.Run(ctx, false, "local"); } else Http.Send(ctx, 400, "websocket only", "text/plain"); return; }
      if (path == "/") { ctx.Response.Redirect(HasMusic ? "/music/dock.html" : "/chat/chat.html?dock=1"); ctx.Response.Close(); return; }
      if (Alias.ContainsKey(path)) path = Alias[path];
      if (path.StartsWith("/api/")) { Api(ctx, path); return; }
      if (!Http.Static(ctx, path, Roots)) Http.Send(ctx, 404, "not found", "text/plain");
    }
    static void Api(HttpListenerContext ctx, string path) {
      var q = ctx.Request.QueryString; string m = ctx.Request.HttpMethod;
      if (path == "/api/ping") { Http.Json(ctx, J.D("ok", true, "app", "ixc-core", "version", Version, "music", HasMusic, "chat", HasChat)); return; }
      if (path == "/api/diag") { if (!Diag.Enabled) Http.Json(ctx, 403, J.D("error", "diagnostics are turned off (diagnostics.enabled)")); else Http.Json(ctx, DiagSnapshot(false)); return; }
      if (path == "/api/version") { Http.Json(ctx, J.D("v", Version)); return; }
      if (HasMusic && Music.Api(ctx, path, m, q)) return;
      if (HasChat && (Chat.Api(ctx, path, m, q) || Tts.Api(ctx, path, m, q))) return;
      if (Remote.LocalApi(ctx, path, m)) return;
      Http.Json(ctx, 404, J.D("error", "unknown api"));
    }
    public static Dictionary<string, object> DiagSnapshot(bool forPhone) {
      var d = J.D("version", Version, "time", DateTime.Now.ToString("HH:mm:ss"));
      if (!Diag.Enabled) { d["disabled"] = true; return d; }
      d["process"] = Diag.Process();
      d["clients"] = J.D("musicPlayer", Hub.Count("player"), "musicDock", Hub.Count("musicdock"), "ttsPlayer", Hub.Count("tts"), "chatPages", Hub.Count("chat"), "phones", Hub.CountRemote(), "diagPanels", Hub.Count("diag"));
      if (HasMusic) d["music"] = Music.DiagInfo();
      if (HasChat) { d["chat"] = Chat.DiagInfo(); d["tts"] = Tts.DiagInfo(); }
      d["remote"] = Remote.DiagInfo();
      d["events"] = Diag.Recent(forPhone ? 15 : 60);
      return d; }

    static void OnWs(Client c, string type, Dictionary<string, object> msg) {
      if (type == "ping") { Hub.Send(c, J.D("type", "pong", "t", J.Str(msg, "t", ""))); return; }
      if (type == "diag.get") { Hub.Send(c, J.D("type", "diag", "d", DiagSnapshot(c.Remote))); return; }
      if (type == "hello") Hub.Send(c, J.D("type", "hello", "version", Version, "music", HasMusic, "chat", HasChat));
      if (HasMusic) Music.OnWs(c, type, msg);
      if (HasChat) { Chat.OnWs(c, type, msg); Tts.OnWs(c, type, msg); }
      Remote.OnWs(c, type, msg);
    }
  }
}
