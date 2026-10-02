// IXC Core - System check (with "Fix automatically" buttons), repairs, backups, diagnostics export and safe updates.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public static class Health {
    static Dictionary<string, object> Item(string id, string name, string level, string msg, string fix, string fixLabel) {
      return J.D("id", id, "name", name, "level", level, "message", msg, "fix", fix, "fixLabel", fixLabel); }
    // cheap checks for the status bar (no network)
    public static List<Dictionary<string, object>> QuickIssues() {
      var l = new List<Dictionary<string, object>>();
      if (Cfg.LoadProblem.Length > 0) l.Add(Item("config", "Settings", "warn", Cfg.LoadProblem, "backup.list", "Restore a backup"));
      if (Secrets.Problem.Length > 0) l.Add(Item("secrets", "Sign-ins", "warn", Secrets.Problem, null, null));
      if (Program.PortMoved) l.Add(Item("port", "IXC address", "warn", "Another program uses port " + Program.WantedPort + ", so IXC moved to " + Program.Port + ". IXC updates its OBS sources; the OBS panels need \"Repair OBS\".", "obs.docks", "Repair OBS panels"));
      if (Obs.Running() && !Obs.Link.Ready && Obs.Link.State != "connecting") l.Add(Item("obs", "OBS", "warn", "OBS is open but IXC can't talk to it: " + Obs.Link.Detail, J.Bool(Obs.WsConfig(), "server_enabled", false) ? "obs.reconnect" : "obs.websocket", "Fix"));
      if (Obs.Link.Ready && Music.Enabled && Hub.Count("player") == 0) l.Add(Item("player", "Music player", "warn", "The music player source isn't running in OBS.", "obs.setup", "Set up OBS"));
      if (Obs.Link.Ready && Chat.Enabled && Hub.Count("tts") == 0 && Tts.On) l.Add(Item("ttsSource", "Chat voice", "warn", "The chat voice source isn't running in OBS.", "obs.setup", "Set up OBS"));
      if (Chat.Enabled) foreach (var s in Platforms.All) {
        if (!s.Configured) continue; var st = s.State;
        if (st == "auth") l.Add(Item("platform." + s.Id, s.Label, "warn", s.Label + ": " + s.Detail, "account." + s.Id, "Sign in again"));
        else if (st == "reconnecting" && (DateTime.Now - s.StateSince).TotalSeconds > 60) l.Add(Item("platform." + s.Id, s.Label, "warn", s.Label + " chat keeps disconnecting: " + s.Detail, "platform." + s.Id, "Reconnect"));
        if (Accounts.IsInvalid(s.Id)) l.Add(Item("account." + s.Id, s.Label + " sign-in", "warn", "Your " + s.Label + " sign-in expired, so IXC can't reply there.", "account." + s.Id, "Sign in again")); }
      if (Settings.Bool("remote.enabled") && Remote.RelayUrl.Length > 0 && Remote.Link.State == "reconnecting" && Remote.Link.Failures > 3) l.Add(Item("phone", "Phone", "warn", "Can't reach the phone relay: " + Remote.Link.Detail, "phone.reconnect", "Reconnect"));
      return l; }

    // the full System check (a few seconds; uses the network)
    public static Dictionary<string, object> Run() {
      var l = new List<Dictionary<string, object>>();
      Action<Func<Dictionary<string, object>>> add = f => { try { var r = f(); if (r != null) l.Add(r); } catch (Exception e) { l.Add(Item("check", "Check", "warn", "a check failed: " + U.Plain(e), null, null)); } };
      add(() => Item("core", "IXC", "ok", "IXC " + Program.Version + " is running at " + Program.BaseUrl, null, null));
      add(() => Cfg.LoadProblem.Length > 0 ? Item("config", "Settings", "warn", Cfg.LoadProblem, "backup.list", "Restore a backup") : Item("config", "Settings", "ok", "Settings are valid and saved", null, null));
      add(() => { try { var f = Path.Combine(Cfg.DataDir, ".write-test"); File.WriteAllText(f, "ok"); File.Delete(f); return Item("disk", "Storage", "ok", "IXC can save its files", null, null); } catch (Exception e) { return Item("disk", "Storage", "error", "IXC can't write to " + Cfg.DataDir + ": " + e.Message, null, null); } });
      add(() => { var r = Http.Request("GET", Ep.Get("net_check", "https://www.gstatic.com/generate_204"), null, null, null, 6000); return r.Code == 204 || r.Ok ? Item("network", "Internet", "ok", "Internet works", null, null) : Item("network", "Internet", "error", "No internet connection (" + (r.Error ?? r.Code.ToString()) + "). Local features keep working; online ones reconnect when it's back.", null, null); });
      // OBS
      var obs = Obs.Detect();
      add(() => !J.Bool(obs, "installed", false) ? Item("obs", "OBS Studio", "error", "OBS Studio isn't installed. Get it free at obsproject.com, then run the check again.", "open:https://obsproject.com/download", "Download OBS") : Item("obs", "OBS Studio", "ok", "OBS Studio " + J.Str(obs, "version", "") + " found", null, null));
      add(() => !J.Bool(obs, "websocketEnabled", false) && !Obs.Link.Ready ? Item("obsws", "OBS connection", "error", "OBS's WebSocket server is off. " + (Obs.Running() ? "Close OBS, then click Fix." : "Click Fix to turn it on."), "obs.websocket", "Fix automatically")
        : !J.Bool(obs, "running", false) && !Obs.Link.Ready ? Item("obsws", "OBS connection", "off", "Open OBS - IXC connects to it by itself.", null, null)
        : Obs.Link.Ready ? Item("obsws", "OBS connection", "ok", "Connected to OBS", null, null) : Item("obsws", "OBS connection", "warn", "OBS is open but not connected: " + Obs.Link.Detail, "obs.reconnect", "Reconnect"));
      if (Obs.Link.Ready) {
        var c = Obs.Check().Result;
        add(() => J.Bool(c, "ok", false) ? Item("obssetup", "OBS sources", "ok", "IXC's music and chat voice sources are in OBS", null, null) : Item("obssetup", "OBS sources", "error", "IXC's sources are missing in OBS (" + (J.Bool(c, "audioScene", false) ? "" : "IXC Audio scene, ") + (J.Bool(c, "playerSource", false) ? "" : "music player, ") + (J.Bool(c, "ttsSource", false) ? "" : "chat voice") + ")", "obs.setup", "Set up OBS"));
        if (Music.Enabled) add(() => Hub.Count("player") > 0 ? Item("player", "Music player", "ok", "The music player is running in OBS", null, null) : Item("player", "Music player", "warn", "The music player source isn't running. If it's hidden in OBS, show it (it is invisible on stream anyway).", "obs.setup", "Repair"));
        if (Chat.Enabled) add(() => Hub.Count("tts") > 0 ? Item("ttsSource", "Chat voice source", "ok", "The chat voice source is running in OBS", null, null) : Item("ttsSource", "Chat voice source", "warn", "The chat voice source isn't running in OBS.", "obs.setup", "Repair"));
        if (Music.Enabled) add(() => { var r = Music.ApplyRoute(Settings.Str("music.routing.mode")).Result; return J.Bool(r, "ok", false) ? Item("audio", "Music audio", "ok", "Music goes to: " + Settings.Str("music.routing.mode").ToUpperInvariant() + " (" + J.Str(r, "tracks", "") + ")", null, null) : Item("audio", "Music audio", "warn", J.Str(r, "error", J.Str(r, "status", "")), "audio.repair", "Repair audio"); }); }
      add(() => { if (Obs.Running()) return Item("docks", "OBS panels", "ok", "Panels: " + string.Join(", ", Obs.DockTitles().Where(t => t.StartsWith("IXC")).DefaultIfEmpty("(add them with Fix after closing OBS)")), null, null);
        var dt = Obs.DockTitles(); return dt.Any(x => x.StartsWith("IXC")) ? Item("docks", "OBS panels", "ok", "IXC panels are in OBS (Docks menu)", null, null) : Item("docks", "OBS panels", "warn", "IXC's panels aren't in OBS yet", "obs.docks", "Add panels"); });
      // chat platforms + accounts
      if (Chat.Enabled) {
        if (!Platforms.All.Any(s => s.Configured)) add(() => Item("platforms", "Chat", "warn", "No streaming platform is connected yet", "page:accounts", "Connect accounts"));
        foreach (var s in Platforms.All) { var src = s; if (!src.Configured) continue;
          add(() => { var st = src.State; var lvl = st == "connected" ? "ok" : st == "unavailable" ? "ok" : st == "auth" ? "error" : "warn";
            return Item("platform." + src.Id, src.Label + " chat", lvl, st == "connected" ? "Connected" + (src.CanSend ? " - IXC can reply" : " (read-only: " + src.SendNote + ")") : src.Detail, st == "auth" ? "account." + src.Id : st == "connected" || st == "unavailable" ? null : "platform." + src.Id, st == "auth" ? "Sign in again" : "Reconnect"); });
          if (Accounts.IsInvalid(src.Id)) add(() => Item("account." + src.Id, src.Label + " sign-in", "warn", "Sign-in expired", "account." + src.Id, "Sign in again")); }
        add(() => { var m = Settings.Str("platforms.streamerbot.mode"); if (m == "off") return Item("streamerbot", "Streamer.bot", "off", "Not used (optional)", null, null);
          return Platforms.Sb.Ready ? Item("streamerbot", "Streamer.bot", "ok", "Connected (optional extra)", null, null) : Item("streamerbot", "Streamer.bot", m == "on" ? "warn" : "off", "Not connected: " + Platforms.Sb.Detail + " (optional - IXC works without it)", m == "on" ? "streamerbot.reconnect" : null, "Reconnect"); });
        // TTS: synthesize a short test phrase (without playing it)
        add(() => { var it = new TtsItem { Said = "test", VoiceKey = Settings.Str("tts.voice") }; var ok = TestVoice(it);
          return ok == null ? Item("tts", "Chat voice", "ok", "Voice works (" + it.Engine + ")" + (Tts.On ? "" : " - chat voice is switched off"), null, null) : Item("tts", "Chat voice", "warn", "The voice couldn't speak: " + ok, "tts.repair", "Repair"); }); }
      // phone
      add(() => !Settings.Bool("remote.enabled") ? Item("phone", "Phone", "off", "Phone remote is turned off", null, null)
        : Remote.RelayUrl.Length == 0 ? Item("phone", "Phone", "off", "This build has no phone relay address yet (developer setup)", null, null)
        : Remote.Link.Ready ? Item("phone", "Phone", "ok", "Phone relay connected - " + J.Int(Remote.Summary(), "devices", 0) + " phone(s) paired", null, null)
        : Item("phone", "Phone", "warn", "Can't reach the phone relay: " + Remote.Link.Detail, "phone.reconnect", "Reconnect"));
      add(() => { var u = Updates.Summary(); return J.Bool(u, "available", false) ? Item("update", "Updates", "warn", "IXC " + J.Str(u, "latest", "") + " is available (you have " + Program.Version + ")", "update.install", "Update now") : Item("update", "Updates", "ok", "IXC is up to date" + (J.Str(u, "checked", "").Length > 0 ? "" : " (not checked yet)"), null, null); });
      string overall = l.Any(i => (string)i["level"] == "error") ? "error" : l.Any(i => (string)i["level"] == "warn") ? "warning" : "healthy";
      Log.Info("app", "system check: " + overall + " (" + string.Join(", ", l.Where(i => (string)i["level"] != "ok" && (string)i["level"] != "off").Select(i => i["name"])) + ")");
      return J.D("overall", overall, "items", l, "at", DateTime.Now.ToString("HH:mm:ss")); }
    static string TestVoice(TtsItem it) {
      if (Program.TestMode && Environment.GetEnvironmentVariable("IXC_TTS_FAKE") == "1") { it.Engine = "test voice"; return null; }
      Voice v; if (!Tts.Voices().TryGetValue(it.VoiceKey, out v)) v = Tts.Voices()["in-male"];
      if (v.Engine == "edge") { try { var b = EdgeTts.Synth("test", v.Name, 0, 0); if (b != null && b.Length > 800) { it.Engine = "neural voice"; return null; } } catch (Exception e) { it.Error = U.Plain(e); } }
      try { string used; Sapi.Synth("test", null, 0, out used); it.Engine = "offline Windows voice " + used + (it.Error.Length > 0 ? " - neural voice unavailable: " + it.Error : ""); return null; } catch (Exception e) { return (it.Error.Length > 0 ? it.Error + "; " : "") + "offline voice: " + U.Plain(e); } }

    // ---------- repairs ----------
    public static Dictionary<string, object> Repair(string action) {
      Log.Info("app", "repair: " + action);
      try {
        if (action.StartsWith("platform.")) { var s = Platforms.Get(action.Substring(9)); if (s == null) return Fail("unknown platform"); s.Restart(); return Ok(s.Label + " is reconnecting"); }
        if (action.StartsWith("account.")) { var r = Accounts.Begin(action.Substring(8)); return J.Bool(r, "ok", false) ? Ok("Finish signing in in your browser") : Fail(J.Str(r, "error", "")); }
        if (action.StartsWith("open:")) { Accounts.OpenBrowser(action.Substring(5)); return Ok("Opened in your browser"); }
        switch (action) {
          case "obs.websocket": { var e = Obs.EnableWebSocket(); return e == null ? Ok("OBS's WebSocket server is on - open OBS and IXC connects by itself") : Fail(e); }
          case "obs.reconnect": Obs.Link.Restart(); return Ok("Reconnecting to OBS");
          case "obs.setup": { if (!Obs.Link.Ready) { var e = Obs.EnableWebSocket(); return Fail(e ?? "Open OBS first - IXC then adds its sources by itself"); } var r = Obs.AutoSetup(true).Result; return J.Bool(r, "ok", false) ? Ok("OBS is set up: " + string.Join(", ", ((List<object>)r["steps"]).Cast<string>())) : Fail(J.Str(r, "error", "")); }
          case "obs.docks": { var e = Obs.AddDocks(new[] { "app", "chat", "music" }.Where(a => a == "app" || (a == "chat" && Chat.Enabled) || (a == "music" && Music.Enabled))); return e == null ? Ok("IXC's panels were added to OBS - open OBS and find them under Docks") : Fail(e); }
          case "streamerbot.reconnect": Platforms.Sb.Restart(); return Ok("Reconnecting to Streamer.bot");
          case "phone.reconnect": Remote.Link.Restart(); return Ok("Reconnecting to the phone relay");
          case "phone.reset": Remote.NewIdentity(); Remote.Link.Restart(); return Ok("New phone address created - scan a new QR code on each phone");
          case "tts.repair": Tts.Clear(); Tts.SkipNow(); Music.Duck(false); Tts.Voices(); if (Settings.Bool("tts.paused")) Settings.Set("tts.paused", false); return Ok("Chat voice reset (queue cleared, music volume restored)");
          case "audio.repair": Music.Duck(false); { var r = Music.ApplyRoute(Settings.Str("music.routing.mode")).Result; return J.Bool(r, "ok", false) ? Ok("Music audio repaired: " + J.Str(r, "tracks", "")) : Fail(J.Str(r, "error", "")); }
          case "overlay.reload": Hub.Publish("reload", J.D("type", "reload", "v", U.Now())); return Ok("All overlays and panels were asked to reload");
          case "music.reload": { var cur = Music.Current(); Music.Command("play", null); return Ok(cur == null ? "Nothing to reload" : "Reloaded the current song"); }
          case "cache.clear": Directory.CreateDirectory(Cfg.DataDir); Cfg.Set("platforms.kick.cache", null); Cfg.Save(); Hub.Publish("reload", J.D("type", "reload", "v", U.Now())); return Ok("Caches cleared");
          case "config.reset": { var b = Backup.Create("before-reset"); var keep = J.D("general", J.D("firstRunDone", true), "helper", J.D("port", Program.Port));   // keep IXC's address so OBS's sources keep working
            var ow = Settings.Str("obs.websocketUrl"); if (ow != "auto") keep["obs"] = J.D("websocketUrl", ow); var ru = Cfg.Get("remote.relayUrl") as string; if (!string.IsNullOrEmpty(ru)) keep["remote"] = J.D("relayUrl", ru); lock (Cfg.L) Cfg.D = keep; Settings.Migrate(Cfg.D); Cfg.SaveNow(); Program.RequestRestart("settings reset", true); return Ok("Settings reset (a backup was made: " + J.Str(b, "name", "") + "). IXC restarts now."); }
          case "connections.all": Platforms.ReconnectAll(); Obs.Link.Restart(); Remote.Link.Restart(); return Ok("Reconnecting everything");
          case "update.install": { var r = Updates.Install(false); return J.Bool(r, "ok", false) ? Ok(J.Str(r, "message", "")) : Fail(J.Str(r, "error", "")); }
          case "reinstall": { var r = Updates.Install(true); return J.Bool(r, "ok", false) ? Ok(J.Str(r, "message", "")) : Fail(J.Str(r, "error", "")); }
          case "backup.list": return J.D("ok", true, "message", "", "page", "backups");
        }
        return Fail("unknown repair: " + action); }
      catch (Exception e) { Log.Err("app", "repair " + action + ": " + U.Plain(e)); return Fail(U.Plain(e)); } }
    static Dictionary<string, object> Ok(string m) { return J.D("ok", true, "message", m); }
    static Dictionary<string, object> Fail(string m) { return J.D("ok", false, "error", m); }
  }

  // ---------- backups: settings, sign-ins, phones, commands, voices, overlays, queue ----------
  public static class Backup {
    static string Dir { get { return Path.Combine(Cfg.DataDir, "backups"); } }
    static readonly string[] Files = { "config.json", "secrets.dat", "music.json" };
    public static Dictionary<string, object> Create(string label) {
      Cfg.SaveNow(); Music.SaveNow(); Directory.CreateDirectory(Dir);
      var stem = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Regex.Replace(label ?? "manual", "[^\\w.-]", ""); var name = stem + ".zip"; var path = Path.Combine(Dir, name);
      for (int i = 2; File.Exists(path); i++) { name = stem + "-" + i + ".zip"; path = Path.Combine(Dir, name); }
      using (var z = ZipFile.Open(path, ZipArchiveMode.Create)) foreach (var f in Files) { var src = Path.Combine(Cfg.DataDir, f); if (File.Exists(src)) z.CreateEntryFromFile(src, f); }
      foreach (var old in Directory.GetFiles(Dir, "*.zip").OrderByDescending(x => x).Skip(15)) { try { File.Delete(old); } catch { } }
      Log.Info("app", "backup created: " + name); return J.D("ok", true, "name", name); }
    public static List<object> List() {
      if (!Directory.Exists(Dir)) return new List<object>();
      return Directory.GetFiles(Dir, "*.zip").OrderByDescending(x => x).Select(f => (object)J.D("name", Path.GetFileName(f), "size", new FileInfo(f).Length, "at", File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm"))).ToList(); }
    public static Dictionary<string, object> Restore(string name) {
      if (!Regex.IsMatch(name ?? "", "^[\\w.-]+\\.zip$")) return J.D("ok", false, "error", "bad backup name");
      var path = Path.Combine(Dir, name); if (!File.Exists(path)) return J.D("ok", false, "error", "That backup doesn't exist");
      Create("before-restore");
      Program.KeepFilesOnExit = true;   // from here on, never write the old in-memory state over the restored files
      using (var z = ZipFile.OpenRead(path)) foreach (var e in z.Entries) { if (!Files.Contains(e.FullName)) continue; var tmp = Path.Combine(Cfg.DataDir, e.FullName + ".restore"); e.ExtractToFile(tmp, true); var dst = Path.Combine(Cfg.DataDir, e.FullName); if (File.Exists(dst)) File.Replace(tmp, dst, null); else File.Move(tmp, dst); }
      Log.Info("app", "restored backup " + name + " - restarting"); Program.RequestRestart("backup restored", true);
      return J.D("ok", true, "message", "Backup restored. IXC restarts now."); }
  }

  // ---------- diagnostics export: logs + system info, never passwords or tokens ----------
  public static class Diagnostics {
    public static Dictionary<string, object> Export() {
      Log.Flush(); var dir = Path.Combine(Cfg.DataDir, "exports"); Directory.CreateDirectory(dir);
      var path = Path.Combine(dir, "IXC-diagnostics-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".zip");
      using (var z = ZipFile.Open(path, ZipArchiveMode.Create)) {
        foreach (var f in Directory.GetFiles(Log.Dir, "*.log*")) { var e = z.CreateEntry("logs/" + Path.GetFileName(f)); using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) { string text; using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var r = new StreamReader(fs)) text = r.ReadToEnd(); w.Write(Log.Redact(Anon(text))); } }
        var info = J.D("ixc", Program.Version, "time", DateTime.Now.ToString("o"), "os", Environment.OSVersion.VersionString, "dotnet", Environment.Version.ToString(), "is64", Environment.Is64BitOperatingSystem,
          "culture", System.Globalization.CultureInfo.CurrentCulture.Name, "port", Program.Port, "obs", Obs.Detect(), "status", Status.Msg(), "diag", Program.DiagSnapshot(), "check", Health.Run(), "settings", SafeSettings());
        var ie = z.CreateEntry("system.json"); using (var w = new StreamWriter(ie.Open(), new UTF8Encoding(false))) w.Write(Log.Redact(Anon(J.Pretty(J.Ser(info))))); }
      Log.Info("app", "diagnostics exported: " + Path.GetFileName(path));
      return J.D("ok", true, "path", path, "folder", dir, "name", Path.GetFileName(path)); }
    static Dictionary<string, object> SafeSettings() { var s = Settings.Snapshot(true); foreach (var k in s.Keys.ToList()) if (Regex.IsMatch(k, "key|secret|password|token|relayUrl", RegexOptions.IgnoreCase)) s[k] = (Convert.ToString(s[k]) ?? "").Length > 0 ? "(set)" : ""; return s; }
    // the Windows user name in paths is personal information
    static string Anon(string s) { var u = Environment.UserName; if (u.Length > 1) s = Regex.Replace(s, "(?i)(Users[\\\\/]+)" + Regex.Escape(u), "$1<user>"); return s; }
  }

  // ---------- updates: check -> backup -> download -> verify -> install (installer keeps the old version for rollback) ----------
  public static class Updates {
    static string latest = "", notes = "", setupUrl = "", sumsUrl = "", checkedAt = "", error = ""; static volatile bool busy; static string progress = ""; static Timer t;
    static string Repo { get { var r = Program.Default("updateRepo"); return r.Length > 0 ? r : "infernoxc/ixc-chatbox"; } }
    public static void Init() { t = new Timer(_ => { if (Settings.Bool("general.checkUpdates")) Check(); }, null, 60000, 6 * 3600 * 1000); }
    public static Dictionary<string, object> Summary() { return J.D("current", Program.Version, "latest", latest, "available", Newer(latest, Program.Version), "checked", checkedAt, "error", error, "busy", busy, "progress", progress, "notes", notes); }
    public static bool Newer(string a, string b) { Version va, vb; return Version.TryParse((a ?? "").TrimStart('v'), out va) && Version.TryParse((b ?? "").TrimStart('v'), out vb) && va > vb; }
    public static Dictionary<string, object> Check() {
      var r = Http.Request("GET", Ep.Get("github_api", "https://api.github.com") + "/repos/" + Repo + "/releases/latest", null, null, new Dictionary<string, string> { { "Accept", "application/vnd.github+json" }, { "User-Agent", "IXC-Updater" } }, 15000);
      checkedAt = DateTime.Now.ToString("HH:mm");
      if (!r.Ok) { error = "couldn't check for updates (" + (r.Error ?? r.Code.ToString()) + ")"; return Summary(); }
      var d = J.Parse(r.Body); latest = J.Str(d, "tag_name", "").TrimStart('v'); notes = U.Trunc(J.Str(d, "body", ""), 4000); error = ""; setupUrl = ""; sumsUrl = "";
      foreach (var a in J.Objs(d, "assets")) { var n = J.Str(a, "name", ""); var u = J.Str(a, "browser_download_url", "");
        if (Regex.IsMatch(n, "^IXC-(Suite-)?Setup-v?[\\d.]+\\.exe$", RegexOptions.IgnoreCase)) setupUrl = u; else if (n.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sumsUrl = u; }
      if (Newer(latest, Program.Version)) { Log.Info("update", "IXC " + latest + " is available"); Hub.Publish("status", Status.Msg()); }
      return Summary(); }
    public static Dictionary<string, object> Install(bool reinstall) {
      if (busy) return J.D("ok", false, "error", "an update is already running");
      if (!U.IsWindows && !Program.TestMode) return J.D("ok", false, "error", "updates install on Windows only");
      Check(); if (!reinstall && !Newer(latest, Program.Version)) return J.D("ok", false, "error", "IXC is already up to date");
      if (setupUrl.Length == 0 || sumsUrl.Length == 0) return J.D("ok", false, "error", "the release has no installer or checksum file - download it from GitHub instead");
      busy = true; progress = "backing up";
      Task.Run(() => {
        try {
          Backup.Create("before-update-" + latest);
          progress = "downloading"; Publish();
          var dir = Path.Combine(Cfg.DataDir, "updates"); Directory.CreateDirectory(dir); var file = Path.Combine(dir, Path.GetFileName(new Uri(setupUrl).AbsolutePath));
          using (var wc = new WebClient()) { wc.Headers["User-Agent"] = "IXC-Updater"; wc.DownloadFile(setupUrl, file + ".part"); }
          progress = "checking the download"; Publish();
          var sums = Http.Get(sumsUrl, 15000); if (!sums.Ok) throw new Exception("couldn't download the checksum file");
          string hash; using (var s = File.OpenRead(file + ".part")) using (var h = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
          var want = Regex.Match(sums.Body, "([0-9a-fA-F]{64})\\s+\\*?" + Regex.Escape(Path.GetFileName(file))); if (!want.Success) throw new Exception("the installer isn't listed in the checksum file");
          if (!string.Equals(want.Groups[1].Value, hash, StringComparison.OrdinalIgnoreCase)) { File.Delete(file + ".part"); throw new Exception("the download is damaged (checksum mismatch) - nothing was changed"); }
          if (File.Exists(file)) File.Delete(file); File.Move(file + ".part", file);
          progress = "installing - IXC restarts by itself"; Publish(); Log.Info("update", "installing " + Path.GetFileName(file));
          if (Program.TestMode) { progress = "verified (test mode: not installed)"; busy = false; Publish(); return; }
          Process.Start(new ProcessStartInfo(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true }); }
        catch (Exception e) { error = U.Plain(e); progress = "failed: " + error; busy = false; Log.Err("update", "update failed: " + error); Publish(); } });
      return J.D("ok", true, "message", "Updating to " + latest + " - IXC backs up your settings, installs, and restarts by itself."); }
    static void Publish() { Hub.Publish("status", Status.Msg()); }
  }
}
