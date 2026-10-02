// IXC Core - program entry, the local web server (pages for OBS, the dashboard, the HTTP API, one WebSocket) and routing.
// On Windows, "ixc-core.exe" starts the tray + supervisor (Supervisor.cs), which runs "ixc-core.exe --worker" and restarts it
// if it ever crashes. Everything below runs in the worker.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public static class Program {
    public static int Port = 8767, WantedPort = 8767; public static bool HasMusic, HasChat, TestMode, PortMoved; public static string Version = "3.0.0";
    public static string BaseUrl { get { return "http://localhost:" + Port; } }
    public static readonly Dictionary<string, string> Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string, object> defaults = new Dictionary<string, object>();
    public static string Default(string k) { return J.Str(defaults, k, ""); }
    // only pages served by IXC itself (and tools without a browser Origin) may use the API. "null" origins (sandboxed
    // frames, file://) are refused: a web page could otherwise use one to control IXC.
    public static bool LocalOrigin(string o) {
      if (string.IsNullOrEmpty(o)) return true; if (o == "null") return false;
      Uri u; return Uri.TryCreate(o, UriKind.Absolute, out u) && (u.Host == "localhost" || u.Host == "127.0.0.1") && u.Port == Port && u.Scheme == "http"; }
    static int exitCode = -1; public static volatile bool KeepFilesOnExit;
    // keepFiles: the files on disk were just replaced (backup restore / reset) - don't overwrite them with what's in memory
    public static void RequestRestart(string why, bool keepFiles) { if (keepFiles) KeepFilesOnExit = true; Log.Info("app", "restarting IXC (" + why + ")"); Task.Delay(800).ContinueWith(_ => Exit(3)); }
    public static void RequestRestart(string why) { RequestRestart(why, false); }
    public static void Exit(int code) { if (Interlocked.CompareExchange(ref exitCode, code, -1) != -1) return; SaveAll(); Environment.Exit(code); }
    static void SaveAll() { try { if (!KeepFilesOnExit) { Cfg.SaveNow(); if (Music.Enabled) Music.SaveNow(); } Log.Flush(); } catch { } }

    [STAThread]
    public static int Main(string[] args) {
      string cfgPath = null; bool worker = !U.IsWindows; bool noTray = false;
      for (int i = 0; i < args.Length; i++) {
        if (args[i] == "--config" && i + 1 < args.Length) cfgPath = args[++i]; else if (args[i] == "--test") { TestMode = true; worker = true; }
        else if (args[i] == "--worker") worker = true; else if (args[i] == "--no-tray") noTray = true; }
      if (cfgPath == null) { var la = Environment.GetEnvironmentVariable("LOCALAPPDATA"); if (string.IsNullOrEmpty(la)) la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); cfgPath = Path.Combine(la, "IXC-OBS", "config.json"); }
      if (!worker && !noTray) return Supervisor.Run(args, cfgPath);
      return Worker(cfgPath);
    }

    static int Worker(string cfgPath) {
      var dataDir = Path.GetDirectoryName(Path.GetFullPath(cfgPath)); Directory.CreateDirectory(dataDir);
      Log.Init(Path.Combine(dataDir, "logs"));
      AppDomain.CurrentDomain.UnhandledException += (s, e) => { Log.Err("app", "IXC crashed: " + e.ExceptionObject); Log.Flush(); };
      TaskScheduler.UnobservedTaskException += (s, e) => { Log.Debug("app", "background task error: " + U.Plain(e.Exception)); e.SetObserved(); };
      Cfg.AppRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
      try { Version = File.ReadAllText(Path.Combine(Cfg.AppRoot, "core", "VERSION")).Trim(); } catch { }
      try { var df = Path.Combine(Cfg.AppRoot, "core", "ixc.defaults.json"); if (File.Exists(df)) defaults = J.Parse(File.ReadAllText(df)); } catch { }
      Secrets.Load(dataDir); Cfg.Load(cfgPath); Log.Verbose = Settings.Bool("diagnostics.verboseLog");
      foreach (var a in new[] { "music", "chat", "app", "overlay" }) { var d = Path.Combine(Cfg.AppRoot, a); if (Directory.Exists(d)) Roots[a] = d; }
      Roots["core"] = Path.Combine(Cfg.AppRoot, "core", "web");
      HasMusic = Roots.ContainsKey("music"); HasChat = Roots.ContainsKey("chat");
      ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; ServicePointManager.DefaultConnectionLimit = 32; ServicePointManager.Expect100Continue = false;

      // the port: normally 8767. If another program took it, use the next free one and fix OBS's sources automatically.
      WantedPort = Port = Settings.Int("helper.port");
      // one IXC per data folder: if it already runs, this copy just exits (the tray then opens the dashboard)
      var running = Http.Request("GET", "http://127.0.0.1:" + Port + "/api/ping", null, null, null, 1500);
      if (running.Ok && J.Str(J.Parse(running.Body), "data", "") == U.ShaHex(dataDir.ToLowerInvariant()).Substring(0, 12)) { Console.WriteLine("IXC is already running on port " + Port); return 0; }
      if (!Bind(ref Port)) return 0;   // another IXC is already running
      if (Port != WantedPort) { PortMoved = true; Log.Warn("app", "port " + WantedPort + " is used by another program - IXC uses " + Port + " instead"); }
      var mutex = new Mutex(false, "Local\\IXC.Core." + Port);
      Log.Info("app", "IXC " + Version + " started on " + BaseUrl + (TestMode ? " (TEST MODE)" : "") + " - data in " + dataDir);

      Hub.OnMessage = OnWs;
      Settings.Changed += k => { if (k == "diagnostics.verboseLog") Log.Verbose = Settings.Bool(k); if (k.StartsWith("overlays.") || k.StartsWith("chat.") || k.StartsWith("general.")) Hub.Publish("settings", J.D("type", "settings", "key", k, "value", Settings.Value(k))); };
      NetWatch.Init();
      Safe("TTS", () => { if (HasChat) Tts.Init(); });
      Safe("chat", () => { if (HasChat) { Chat.Init(); Platforms.Init(); } });
      Safe("music", () => { if (HasMusic) Music.Init(); });
      Safe("OBS", () => Obs.Init());
      Safe("phone", () => Remote.Init());
      Safe("status", () => Status.Init());
      Safe("updates", () => Updates.Init());
      WatchFiles();
      AppDomain.CurrentDomain.ProcessExit += (s, e) => SaveAll();
      Thread.Sleep(Timeout.Infinite); GC.KeepAlive(mutex); return 0;
    }
    static void Safe(string what, Action a) { try { a(); } catch (Exception e) { Log.Err("app", what + " could not start: " + U.Plain(e)); } }
    static bool Bind(ref int port) {
      Web.Handler = Handle;
      for (int p = port; p < port + 30; p++) {
        string err; bool ok = Web.Start(p, out err);
        // right after a restart the old copy may still be letting go of the port: wait a few seconds before moving to another one
        for (int i = 0; !ok && p == port && i < 10; i++) { var r0 = Http.Request("GET", "http://127.0.0.1:" + p + "/api/ping", null, null, null, 1000); if (r0.Ok && (r0.Body ?? "").Contains("ixc-core")) break; Thread.Sleep(500); ok = Web.Start(p, out err); }
        if (ok) { port = p; return true; }
        if (p == port) { var r = Http.Request("GET", "http://127.0.0.1:" + p + "/api/ping", null, null, null, 2000); if (r.Ok && (r.Body ?? "").Contains("ixc-core")) { Console.WriteLine("IXC is already running on port " + p); return false; } Log.Warn("app", "port " + p + " busy: " + err); } }
      throw new Exception("no free port for IXC"); }
    static FileSystemWatcher fsw; static Timer reloadT;
    // pages reload themselves when a newer version of a page is installed
    static void WatchFiles() {
      try { reloadT = new Timer(_ => { Log.Info("app", "page files changed - asking pages to reload"); Hub.Publish("reload", J.D("type", "reload", "v", U.Now())); });
        fsw = new FileSystemWatcher(Cfg.AppRoot) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
        FileSystemEventHandler h = (s, e) => { var x = Path.GetExtension(e.FullPath).ToLowerInvariant(); if (x == ".html" || x == ".js" || x == ".css") reloadT.Change(1500, Timeout.Infinite); };
        fsw.Changed += h; fsw.Created += h; fsw.Renamed += (s, e) => h(s, e); fsw.EnableRaisingEvents = true; } catch (Exception e) { Log.Warn("app", "file watch: " + e.Message); } }

    static readonly Dictionary<string, string> Alias = new Dictionary<string, string> { { "/player.html", "/music/player.html" }, { "/dock.html", "/music/dock.html" }, { "/tts.html", "/chat/tts.html" },
      { "/diag", "/app/" }, { "/diag.html", "/app/" }, { "/core/diag.html", "/app/" }, { "/app", "/app/" }, { "/overlay", "/overlay/" } };
    static void Handle(Ctx ctx) {
      var rq = ctx.Request; string path = rq.Url.AbsolutePath;
      var host = (rq.Headers["Host"] ?? "").ToLowerInvariant();
      if (Quick.IsTunnelHost(host)) { Quick.Handle(ctx); return; }   // from the internet through Quick connect: only the phone page and its socket
      if (host != "localhost:" + Port && host != "127.0.0.1:" + Port && host != "[::1]:" + Port) { Http.Send(ctx, 421, "wrong host", "text/plain"); return; }   // DNS rebinding
      bool api = path.StartsWith("/api/") || path == "/ws" || path.StartsWith("/oauth/");
      if (api && !LocalOrigin(rq.Headers["Origin"])) { Log.Warn("net", "refused a request from another website (" + U.Trunc(rq.Headers["Origin"], 80) + ")"); Http.Json(ctx, 403, J.D("error", "origin not allowed")); return; }
      if (rq.HttpMethod == "OPTIONS") { ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST"; ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type"; Http.Send(ctx, 204, "", "text/plain"); return; }
      if (path == "/ws") { if (rq.IsWebSocketRequest) Hub.RunLocal(ctx); else Http.Send(ctx, 400, "websocket only", "text/plain"); return; }
      if (path == "/") { ctx.Response.Redirect("/app/"); return; }
      if (path.StartsWith("/oauth/")) { var p = path.Substring(7).Trim('/'); var msg = Accounts.Callback(p, rq.QueryString); Http.Send(ctx, 200, "<!doctype html><meta charset=utf-8><title>IXC</title><body style=\"font:16px Segoe UI,sans-serif;background:#0f0e13;color:#eee;display:grid;place-items:center;height:90vh\"><div><h2>IXC</h2><p>" + WebUtility.HtmlEncode(msg) + "</p></div>", "text/html; charset=utf-8"); return; }
      if (Alias.ContainsKey(path)) path = Alias[path];
      if (path.StartsWith("/api/")) { Api(ctx, path); return; }
      if (!Http.Static(ctx, path, Roots)) Http.Send(ctx, 404, "not found", "text/plain");
    }
    static void Api(Ctx ctx, string path) {
      var q = ctx.Request.QueryString; string m = ctx.Request.HttpMethod;
      if (path == "/api/ping") { Http.Json(ctx, J.D("ok", true, "app", "ixc-core", "version", Version, "music", HasMusic, "chat", HasChat, "port", Port, "data", U.ShaHex(Cfg.DataDir.ToLowerInvariant()).Substring(0, 12))); return; }
      if (path == "/api/version") { Http.Json(ctx, J.D("v", Version)); return; }
      if (path == "/api/status") { Http.Json(ctx, Status.Msg()); return; }
      if (path == "/api/diag") { Http.Json(ctx, DiagSnapshot()); return; }
      if (path == "/api/logs") { Http.Json(ctx, J.D("entries", Log.Tail(Math.Min(400, Math.Max(10, ParseInt(q["n"], 150))), string.IsNullOrEmpty(q["area"]) ? null : q["area"]), "folder", Log.Dir)); return; }
      if (path == "/api/settings") {
        if (m == "POST") { var b = Http.BodyJson(ctx); var err = Settings.Apply(J.Obj(b, "patch") ?? b); if (err != null) { Http.Json(ctx, 400, J.D("ok", false, "error", err)); return; } }
        Http.Json(ctx, J.D("ok", true, "values", Settings.Snapshot(true), "schema", Settings.Schema(), "voices", HasChat ? Tts.VoiceList() : null, "outputFolder", Output.Dir, "dataFolder", Cfg.DataDir)); return; }
      if (path == "/api/health") { Http.Json(ctx, Health.Run()); return; }
      if (path == "/api/repair" && m == "POST") { Http.Json(ctx, Health.Repair(J.Str(Http.BodyJson(ctx), "action", ""))); return; }
      if (path == "/api/backups") { if (m == "POST") { var b = Http.BodyJson(ctx); var name = J.Str(b, "restore", ""); Http.Json(ctx, name.Length > 0 ? Backup.Restore(name) : Backup.Create("manual")); return; } Http.Json(ctx, J.D("backups", Backup.List())); return; }
      if (path == "/api/diagnostics/export" && m == "POST") { Http.Json(ctx, Diagnostics.Export()); return; }
      if (path == "/api/open" && m == "POST") { OpenThing(J.Str(Http.BodyJson(ctx), "what", "")); Http.Json(ctx, J.D("ok", true)); return; }
      if (path == "/api/system/restart" && m == "POST") { Http.Json(ctx, J.D("ok", true)); RequestRestart("asked from the dashboard"); return; }
      if (path == "/api/system/quit" && m == "POST") { Http.Json(ctx, J.D("ok", true)); Log.Info("app", "quit from the dashboard"); Task.Delay(500).ContinueWith(_ => Exit(4)); return; }
      if (path == "/api/update/check") { Http.Json(ctx, Updates.Check()); return; }
      if (path == "/api/accounts") { Http.Json(ctx, Accounts.All()); return; }
      if (path.StartsWith("/api/accounts/") && m == "POST") { var parts = path.Split('/'); if (parts.Length == 5) { var p = parts[3]; var act = parts[4];
          if (act == "connect") { Http.Json(ctx, Accounts.Begin(p)); return; } if (act == "signout") { Accounts.SignOut(p); Http.Json(ctx, Accounts.All()); return; } if (act == "cancel") { Accounts.Cancel(p); Http.Json(ctx, Accounts.All()); return; }
          if (act == "rumble") { var err = Accounts.SetRumbleUrl(J.Str(Http.BodyJson(ctx), "url", "")); Http.Json(ctx, err == null ? 200 : 400, J.D("ok", err == null, "error", err)); return; } } }
      if (path == "/api/obs/detect") { Http.Json(ctx, Obs.Detect()); return; }
      if (path == "/api/obs/check") { Http.Json(ctx, Obs.Check().Result); return; }
      if (path == "/api/obs/remove" && m == "POST") { Http.Json(ctx, Obs.RemoveAll().Result); return; }
      if (path == "/api/obs/overlay" && m == "POST") { var b = Http.BodyJson(ctx); try { Http.Json(ctx, Obs.AddOverlay(J.Str(b, "name", "Overlay"), BaseUrl + J.Str(b, "path", "/overlay/"), J.Int(b, "width", 800), J.Int(b, "height", 200)).Result); } catch (Exception e) { Http.Json(ctx, 400, J.D("ok", false, "error", U.Plain(e))); } return; }
      if (path == "/api/firstrun/done" && m == "POST") { Settings.Set("general.firstRunDone", true); Http.Json(ctx, J.D("ok", true)); return; }
      if (Viewers.Api(ctx, path, q) || NowPlaying.Api(ctx, path)) return;
      if (HasMusic && Music.Api(ctx, path, m, q)) return;
      if (HasChat && (Chat.Api(ctx, path, m, q) || Tts.Api(ctx, path, m, q))) return;
      if (Remote.LocalApi(ctx, path, m)) return;
      Http.Json(ctx, 404, J.D("error", "unknown api"));
    }
    static int ParseInt(string s, int d) { int n; return int.TryParse(s, out n) ? n : d; }
    static void OpenThing(string what) {
      string target = what == "logs" ? Log.Dir : what == "output" ? Output.Dir : what == "exports" ? Path.Combine(Cfg.DataDir, "exports") : what == "data" ? Cfg.DataDir : null;
      if (target == null || TestMode) return; Directory.CreateDirectory(target);
      try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception e) { Log.Warn("app", "could not open " + what + ": " + e.Message); } }
    public static Dictionary<string, object> DiagSnapshot() {
      var p = System.Diagnostics.Process.GetCurrentProcess();
      var d = J.D("version", Version, "time", DateTime.Now.ToString("HH:mm:ss"), "port", Port,
        "process", J.D("pid", p.Id, "ramMB", Math.Round(p.WorkingSet64 / 1048576.0, 1), "threads", p.Threads.Count, "handles", p.HandleCount, "uptimeMin", (int)(DateTime.Now - Log.Started).TotalMinutes, "errors", Log.Errors, "warnings", Log.Warnings, "reconnects", Log.Reconnects),
        "clients", J.D("musicPlayer", Hub.Count("player"), "ttsSource", Hub.Count("tts"), "pages", Hub.Count("chat") + Hub.Count("app") + Hub.Count("overlay") + Hub.Count("musicdock"), "phones", Hub.CountRemote()),
        "obs", Obs.Link.StatusInfo(), "network", J.D("lastWake", NetWatch.LastWake == DateTime.MinValue ? "" : NetWatch.LastWake.ToString("HH:mm:ss"), "reason", NetWatch.LastReason));
      if (HasMusic) d["music"] = Music.DiagInfo();
      if (HasChat) { d["chat"] = Chat.DiagInfo(); d["tts"] = Tts.DiagInfo(); d["filters"] = new Dictionary<string, int>(Filters.Counts); d["commands"] = J.D("used", Commands.Used, "songRequests", J.D("accepted", SongRequests.Accepted, "refused", SongRequests.Refused)); }
      d["phone"] = Remote.DiagInfo(); d["events"] = Log.Tail(60, null);
      return d; }

    // ---------- WebSocket messages shared by every page ----------
    static void OnWs(Client c, string type, Dictionary<string, object> msg) {
      if (type == "ping") { Hub.Send(c, J.D("type", "pong", "t", J.Str(msg, "t", ""))); return; }
      if (type == "hello") { Hub.Send(c, J.D("type", "hello", "version", Version, "music", HasMusic, "chat", HasChat, "port", Port));
        if (c.Topics.Contains("status")) Hub.Send(c, Status.Msg()); if (c.Topics.Contains("viewers")) Hub.Send(c, Viewers.Snapshot());
        if (c.Topics.Contains("settings")) Hub.Send(c, J.D("type", "settings.all", "values", Settings.Snapshot(!c.Remote))); }
      if (type == "settings.set") {
        var patch = J.Obj(msg, "patch") ?? new Dictionary<string, object>();
        if (c.Remote && patch.Keys.Any(k => !Remote.PhoneSettings.Contains(k))) { Hub.Send(c, J.D("type", "error", "error", "That setting can only be changed on the PC", "reqId", J.Str(msg, "reqId", ""))); return; }
        var err = Settings.Apply(patch); Hub.Send(c, J.D("type", "settings.saved", "reqId", J.Str(msg, "reqId", ""), "ok", err == null, "error", err)); return; }
      if (type == "viewers.get") { Hub.Send(c, Viewers.Snapshot()); return; }
      if (HasMusic) Music.OnWs(c, type, msg);
      if (HasChat) { Chat.OnWs(c, type, msg); Tts.OnWs(c, type, msg); }
      Remote.OnWs(c, type, msg);
    }
  }
}
