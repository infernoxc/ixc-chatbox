// IXC Core - Streamer.bot as a managed background backend: detect it, test its local WebSocket API, start and stop it (only a process IXC
// started itself), and keep the settings (endpoint, password, program path) in one place. The chat/event/reply code itself is SbLink in
// Platforms.cs; this file never touches Streamer.bot's own data folder and never writes its settings.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  // IXC's own replies come back from Streamer.bot as chat events: remember what was sent so the echo is shown (as yours) but never
  // counted, run as a command or read out loud, which would loop.
  public static class SentEcho {
    static readonly List<KeyValuePair<string, DateTime>> Sent = new List<KeyValuePair<string, DateTime>>();
    static string Key(string platform, string text) { return (platform ?? "").ToLowerInvariant() + "|" + System.Text.RegularExpressions.Regex.Replace((text ?? "").Trim(), "\\s+", " "); }
    public static void Note(string platform, string text) { lock (Sent) { Prune(); Sent.Add(new KeyValuePair<string, DateTime>(Key(platform, text), DateTime.Now)); } }
    public static void Forget(string platform, string text) { lock (Sent) { var k = Key(platform, text); var i = Sent.FindIndex(x => x.Key == k); if (i >= 0) Sent.RemoveAt(i); } }
    // true once per remembered send (the first matching event within 20 s)
    public static bool Take(string platform, string text) { lock (Sent) { Prune(); var k = Key(platform, text); var i = Sent.FindIndex(x => x.Key == k); if (i < 0) return false; Sent.RemoveAt(i); return true; } }
    static void Prune() { var cut = DateTime.Now.AddSeconds(-20); Sent.RemoveAll(x => x.Value < cut); }
  }

  public static class SbManager {
    static readonly object L = new object();
    static int managedPid; static long managedStartTicks; static string managedExe = "";
    static Timer mon; static readonly List<DateTime> starts = new List<DateTime>();
    static string lastStartError = "";
    // the program IXC looks for; tests point this at a harmless stand-in so a real Streamer.bot is never involved
    static string ExeName { get { return Ep.Get("sb_exe", "Streamer.bot.exe"); } }
    static string ProcName { get { return Path.GetFileNameWithoutExtension(ExeName); } }

    // ---------- settings ----------
    static string ManagedFile { get { return Path.Combine(Cfg.DataDir, "streamerbot-managed.json"); } }
    public static void Init() {
      try { if (File.Exists(ManagedFile)) { var d = J.Parse(File.ReadAllText(ManagedFile)); managedPid = J.Int(d, "pid", 0); managedStartTicks = J.Long(d, "startTicks", 0); managedExe = J.Str(d, "exe", ""); if (!IsManagedAlive()) ClearManaged(); } } catch { ClearManaged(); }
      mon = new Timer(_ => { try { Tick(); } catch (Exception e) { Log.Err("streamerbot", "monitor: " + e.Message); } }, null, 8000, 10000); }
    static void ClearManaged() { managedPid = 0; managedStartTicks = 0; managedExe = ""; try { File.Delete(ManagedFile); } catch { } }

    // only a loopback address is accepted: Streamer.bot's API carries the chat and can run actions
    public static string ValidateUrl(string url) {
      Uri u; if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out u) || (u.Scheme != "ws" && u.Scheme != "wss")) return "The address must look like ws://127.0.0.1:8080/";
      var h = u.Host.ToLowerInvariant(); if (h != "127.0.0.1" && h != "localhost" && h != "[::1]" && h != "::1") return "For safety IXC only connects to Streamer.bot on this PC (127.0.0.1 or localhost)";
      return null; }

    // ---------- detection (read-only) ----------
    static Process[] Running() { try { return Process.GetProcessesByName(ProcName); } catch { return new Process[0]; } }
    public static IEnumerable<string> Folders() {
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var cfg = Settings.Str("platforms.streamerbot.exePath").Trim(); if (cfg.Length > 0) { var d = cfg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(cfg) : cfg; if (!string.IsNullOrEmpty(d) && seen.Add(d)) yield return d; }
      foreach (var pr in Running()) { string f = null; try { f = Path.GetDirectoryName(pr.MainModule.FileName); } catch { } if (f != null && seen.Add(f)) yield return f; }
      foreach (var sf in SbLink.SettingsFilesPublic()) { var data = Path.GetDirectoryName(sf); var d = data == null ? null : Path.GetDirectoryName(data); if (d != null && seen.Add(d)) yield return d; } }
    static string ExeIn(string dir) { try { var e = Path.Combine(dir, ExeName); return File.Exists(e) ? e : null; } catch { return null; } }
    public static string ResolveExe() { foreach (var d in Folders()) { var e = ExeIn(d); if (e != null) return e; } return null; }
    static string Version(string exe) { try { return FileVersionInfo.GetVersionInfo(exe).FileVersion; } catch { return ""; } }

    // what Streamer.bot's own settings say about its WebSocket server: flags only, the password is never read into the result
    static Dictionary<string, object> SbSettings(string exe) {
      try { var f = Path.Combine(Path.GetDirectoryName(exe), "data", "settings.json"); if (!File.Exists(f)) return null;
        var s = J.Parse(File.ReadAllText(f)); var w = J.Obj(s, "websockets"); if (w == null) return null;
        var addr = J.Str(w, "address", ""); var port = J.Int(w, "port", 8080);
        return J.D("serverOn", J.Bool(w, "autoStart", false), "address", addr, "port", port, "endpoint", J.Str(w, "endpoint", "/"), "authEnabled", J.Bool(w, "enableAuth", false), "passwordInSettings", J.Str(w, "authPassword", "").Length > 0,
          "localOnly", addr == "127.0.0.1" || addr == "localhost" || addr == "::1"); } catch { return null; } }

    static bool IsManagedAlive() {
      if (managedPid <= 0) return false;
      try { var p = Process.GetProcessById(managedPid); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == managedStartTicks && string.Equals(p.ProcessName, ProcName, StringComparison.OrdinalIgnoreCase); } catch { return false; } }

    public static Dictionary<string, object> Snapshot() {
      var procs = Running(); var exe = ResolveExe(); string runPath = null; foreach (var p in procs) { try { runPath = p.MainModule.FileName; } catch { } }
      var folders = Folders().ToList(); var shown = runPath ?? exe;
      var link = Platforms.Sb; var pwSaved = (Secrets.Str("streamerbot.password") ?? "").Length > 0;
      var sbs = shown == null ? null : SbSettings(shown);
      string pwSource = pwSaved ? "saved in IXC" : (sbs != null && J.Bool(sbs, "passwordInSettings", false) ? "found in Streamer.bot's settings" : "none");
      return J.D("installed", exe != null || runPath != null, "path", shown ?? "", "folders", folders, "version", shown == null ? "" : Version(shown),
        "running", procs.Length > 0, "processes", procs.Length, "managed", IsManagedAlive(), "managedPid", IsManagedAlive() ? managedPid : 0,
        "mode", Settings.Str("platforms.streamerbot.mode"), "launch", Settings.Str("platforms.streamerbot.launch"), "url", Settings.Str("platforms.streamerbot.websocketUrl"), "exePath", Settings.Str("platforms.streamerbot.exePath"),
        "passwordSet", pwSource != "none", "passwordSource", pwSource, "settings", sbs,
        "link", J.D("state", link.State, "detail", link.Detail, "ready", link.Ready, "auth", link.AuthState, "since", (long)(link.Since.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds,
          "lastEvent", link.LastEvent == DateTime.MinValue ? null : (object)(long)(link.LastEvent.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds, "events", link.EventCount, "accounts", link.Broadcasters.ToList()),
        "startError", lastStartError); }

    // ---------- test: a one-shot connection, separate from the running link ----------
    public static async Task<Dictionary<string, object>> Test() {
      var url = Ep.Get("streamerbot", Settings.Str("platforms.streamerbot.websocketUrl")); var bad = Ep.Get("streamerbot", "").Length > 0 ? null : ValidateUrl(url);
      var r = J.D("ok", false, "reachable", false, "authRequired", false, "authOk", false, "version", "", "name", "", "accounts", new List<string>(), "error", "", "step", "address");
      if (bad != null) { r["error"] = bad; return r; }
      using (var ws = new ClientWebSocket()) using (var cts = new CancellationTokenSource(8000)) {
        try {
          r["step"] = "connect"; await ws.ConnectAsync(new Uri(url), cts.Token); r["reachable"] = true;
          r["step"] = "hello"; var hello = J.Parse(await Recv(ws, cts.Token)); var info = J.Obj(hello, "info"); if (info != null) { r["version"] = J.Str(info, "version", ""); r["name"] = J.Str(info, "name", ""); }
          var a = J.Obj(hello, "authentication"); r["authRequired"] = a != null;
          if (a != null) {
            r["step"] = "password"; var pw = Platforms.Sb.Password();
            if (pw.Length == 0) { r["error"] = "Streamer.bot asks for a password and IXC has none. Enter it below (Streamer.bot > Servers/Clients > WebSocket Server)."; return r; }
            await Send(ws, J.Ser(J.D("request", "Authenticate", "id", "t-auth", "authentication", U.Sha(U.Sha(pw + J.Str(a, "salt", "")) + J.Str(a, "challenge", "")))), cts.Token);
            var ans = await RecvId(ws, "t-auth", cts.Token);
            if (J.Str(ans, "status", "") != "ok") { r["error"] = "Streamer.bot refused the password."; return r; } }
          r["authOk"] = true;
          r["step"] = "accounts"; await Send(ws, J.Ser(J.D("request", "GetBroadcaster", "id", "t-bc")), cts.Token);
          var bc = await RecvId(ws, "t-bc", cts.Token); var names = new List<string>(); SbLink.CollectNames(bc, names, 0); r["accounts"] = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
          r["ok"] = true; r["step"] = "done"; return r; }
        catch (OperationCanceledException) { r["error"] = (bool)r["reachable"] ? "Streamer.bot connected but did not answer in time (" + r["step"] + ")." : "Streamer.bot did not answer in time."; return r; }
        catch (Exception e) { var m = U.Plain(e); r["error"] = (bool)r["reachable"] ? "Streamer.bot closed the connection (" + r["step"] + "): " + m : "Could not connect: Streamer.bot is not running or its WebSocket Server is off (" + m + ")"; return r; }
        finally { try { ws.Abort(); } catch { } } } }
    static async Task Send(ClientWebSocket ws, string s, CancellationToken ct) { await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(s)), WebSocketMessageType.Text, true, ct); }
    static async Task<string> Recv(ClientWebSocket ws, CancellationToken ct) {
      var buf = new byte[16384]; var sb = new StringBuilder();
      while (true) { var x = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct); if (x.MessageType == WebSocketMessageType.Close) throw new Exception("closed by Streamer.bot"); sb.Append(Encoding.UTF8.GetString(buf, 0, x.Count)); if (x.EndOfMessage) return sb.ToString(); } }
    static async Task<Dictionary<string, object>> RecvId(ClientWebSocket ws, string id, CancellationToken ct) {
      for (int i = 0; i < 40; i++) { var m = J.Parse(await Recv(ws, ct)); if (J.Str(m, "id", "") == id) return m; }   // events may arrive in between
      throw new Exception("no answer to " + id); }

    // ---------- save (IXC settings only) ----------
    // patch: url, password (write-only; "" keeps it), exePath, launch, mode. Returns null when saved, else the reason; nothing is stored then.
    public static string Save(Dictionary<string, object> b) {
      var names = new List<string>(); foreach (var k in new[] { "url", "password", "exePath", "launch", "mode" }) if (b != null && b.ContainsKey(k)) names.Add(k);
      string err = null;
      try {
        if (b == null || names.Count == 0) err = "nothing to save";
        else {
          var patch = new Dictionary<string, object>();
          if (b.ContainsKey("url")) { var u = J.Str(b, "url", "").Trim(); err = ValidateUrl(u); if (err == null) patch["platforms.streamerbot.websocketUrl"] = u; }
          if (err == null && b.ContainsKey("exePath")) { var p = J.Str(b, "exePath", "").Trim(); if (p.Length > 0) { var e = p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : ExeIn(p); if (e == null || !File.Exists(e) || !string.Equals(Path.GetFileName(e), ExeName, StringComparison.OrdinalIgnoreCase)) err = "That is not a Streamer.bot.exe (pick the file or its folder)"; else p = e; } patch["platforms.streamerbot.exePath"] = p; }
          if (err == null && b.ContainsKey("launch")) { var l = J.Str(b, "launch", ""); if (l != "never" && l != "withIxc") err = "launch must be never or withIxc"; else patch["platforms.streamerbot.launch"] = l; }
          if (err == null && b.ContainsKey("mode")) { var m = J.Str(b, "mode", ""); if (m != "auto" && m != "on" && m != "off") err = "mode must be auto, on or off"; else patch["platforms.streamerbot.mode"] = m; }
          string pw = null; if (err == null && b.ContainsKey("password")) { pw = J.Str(b, "password", ""); if (pw.Length > 0 && (pw.Length > 200 || pw.Trim() != pw)) err = "That password has leading or trailing spaces or is too long"; }
          if (err == null) {
            if (patch.Count > 0) err = Settings.Apply(patch);
            if (err == null && pw != null && pw.Length > 0) { var old = Secrets.Str("streamerbot.password"); Secrets.Set("streamerbot.password", pw);
              if (Secrets.SaveError != null) { Secrets.Set("streamerbot.password", old); err = "IXC couldn't write its encrypted settings file, so the password was not saved"; } }
            if (err == null) { Platforms.Sb.Restart(); Hub.Publish("status", Status_Msg()); } } }
      } catch (Exception e) { err = "couldn't save (" + e.GetType().Name + ")"; }
      Log.Info("streamerbot", err == null ? "settings saved (" + string.Join(", ", names) + ")" : "settings NOT saved (fields: " + string.Join(", ", names) + "): " + err);
      return err; }
    static object Status_Msg() { return global::IXC.Status.Msg(); }

    // ---------- start / stop ----------
    // Never starts a second copy, never passes flags (none are documented), never touches the data folder.
    public static string Start() {
      lock (L) {
        lastStartError = "";
        if (Running().Length > 0) { Log.Info("streamerbot", "start skipped: Streamer.bot is already running"); return IsManagedAlive() ? "Streamer.bot is already running (started by IXC)" : "Streamer.bot is already running (it was not started by IXC, so IXC leaves it alone)"; }
        var exe = ResolveExe(); if (exe == null) { lastStartError = "Streamer.bot.exe was not found. Choose its folder under Program."; return lastStartError; }
        try {
          var psi = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true, WindowStyle = ProcessWindowStyle.Minimized };
          var p = Process.Start(psi); if (p == null) { lastStartError = "Windows did not start Streamer.bot"; return lastStartError; }
          managedPid = p.Id; managedExe = exe; try { managedStartTicks = p.StartTime.ToUniversalTime().Ticks; } catch { managedStartTicks = DateTime.UtcNow.Ticks; }
          try { File.WriteAllText(ManagedFile, J.Ser(J.D("pid", managedPid, "startTicks", managedStartTicks, "exe", exe))); } catch { }
          lock (starts) { starts.Add(DateTime.Now); }
          Log.Info("streamerbot", "started Streamer.bot " + Version(exe) + " (pid " + managedPid + ")"); return null; }
        catch (Exception e) { lastStartError = "Could not start Streamer.bot: " + U.Plain(e); Log.Err("streamerbot", lastStartError); return lastStartError; } } }

    // only the process IXC started (same pid and start time). A normal close first; force only when the user asks for it.
    public static string Stop(bool force) {
      lock (L) {
        if (!IsManagedAlive()) { ClearManaged(); return Running().Length > 0 ? "Streamer.bot is running but IXC did not start it, so IXC will not stop it" : "Streamer.bot is not running"; }
        try {
          var p = Process.GetProcessById(managedPid); p.CloseMainWindow();
          if (!p.WaitForExit(10000)) { if (!force) return "Streamer.bot did not close (it may be minimized to its tray). Close it there, or use Force stop (unsaved changes inside Streamer.bot could be lost)."; p.Kill(); p.WaitForExit(5000); }
          ClearManaged(); Log.Info("streamerbot", "stopped Streamer.bot" + (force ? " (forced)" : "")); return null; }
        catch (Exception e) { return "Could not stop Streamer.bot: " + U.Plain(e); } } }

    // keeps Streamer.bot up when "start with IXC" is on: at most 3 starts in 10 minutes, so a crash loop is not a restart loop
    static void Tick() {
      if (Settings.Str("platforms.streamerbot.launch") != "withIxc" || Settings.Str("platforms.streamerbot.mode") == "off") return;
      if (Running().Length > 0) return;
      lock (starts) { starts.RemoveAll(x => x < DateTime.Now.AddMinutes(-10)); if (starts.Count >= 3) { lastStartError = "Streamer.bot keeps closing; IXC stopped restarting it (3 starts in 10 minutes)"; return; } }
      var e = Start(); if (e != null) Log.Info("streamerbot", "auto-start: " + e); }

    // ---------- API ----------
    public static bool Api(Ctx ctx, string path, string m) {
      if (!path.StartsWith("/api/streamerbot")) return false;
      try {
        if (path == "/api/streamerbot" && m == "GET") { Http.Json(ctx, Snapshot()); return true; }
        if (m != "POST") return false;
        var b = Http.BodyJson(ctx);
        if (path == "/api/streamerbot/config") { var err = Save(b); Http.Json(ctx, err == null ? 200 : 400, J.D("ok", err == null, "error", err, "status", Snapshot())); return true; }
        if (path == "/api/streamerbot/test") { var r = Test().Result; Log.Info("streamerbot", "connection test: " + ((bool)r["ok"] ? "ok" : "failed at " + r["step"])); Http.Json(ctx, r); return true; }
        if (path == "/api/streamerbot/start") { var err = Start(); Http.Json(ctx, err == null ? 200 : 400, J.D("ok", err == null, "error", err, "status", Snapshot())); return true; }
        if (path == "/api/streamerbot/stop") { var err = Stop(J.Bool(b, "force", false)); Http.Json(ctx, err == null ? 200 : 400, J.D("ok", err == null, "error", err, "status", Snapshot())); return true; }
      } catch (Exception e) { Http.Json(ctx, 500, J.D("ok", false, "error", "Streamer.bot: " + U.Plain(e))); return true; }
      return false; }
  }
}
