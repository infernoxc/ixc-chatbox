// IXC Core - OBS Studio: finds OBS, turns on its WebSocket server, adds the IXC docks and browser sources by itself,
// and keeps one obs-websocket 5 connection for audio routing and setup checks.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public class ObsLink : WsLink {
    public Action<string, Dictionary<string, object>> OnObsEvent; public Action OnReady; public string ObsVersion = "", WsVersion = "";
    Dictionary<string, object> helloD; readonly ManualResetEventSlim gotHello = new ManualResetEventSlim(false), identified = new ManualResetEventSlim(false);
    public ObsLink() { Name = "OBS"; Area = "obs"; MaxBackoffMs = 15000; }
    bool Manual { get { var u = Settings.Str("obs.websocketUrl"); return u.Length > 0 && u != "auto"; } }
    protected override bool Wanted() { return Manual || Obs.Running(); }
    protected override string WhyNotWanted() { return "OBS is not open"; }
    protected override string Url() { return Manual ? Settings.Str("obs.websocketUrl") : "ws://127.0.0.1:" + J.Int(Obs.WsConfig(), "server_port", 4455) + "/"; }
    protected override string Friendly(Exception e) {
      var m = U.Plain(e); if (e is WebSocketException || m.Contains("Unable to connect") || m.Contains("refused") || m.Contains("timed out"))
        return Manual || J.Bool(Obs.WsConfig(), "server_enabled", false) ? "OBS is open but its WebSocket server isn't answering yet" : "OBS's WebSocket server is turned off (IXC can turn it on - see System check)";
      return m; }
    protected override async Task Handshake() {
      identified.Reset();
      if (!await Task.Run(() => gotHello.Wait(5000))) throw new Exception("OBS did not say hello");
      gotHello.Reset();
      var d = J.D("rpcVersion", 1, "eventSubscriptions", 1 | 4 | 8);   // General, Scenes, Inputs
      var a = J.Obj(helloD, "authentication");
      if (a != null) { var pw = Secrets.Str("obs.password") ?? ""; if (pw.Length == 0) pw = J.Str(Obs.WsConfig(), "server_password", "");
        d["authentication"] = U.Sha(U.Sha(pw + J.Str(a, "salt", "")) + J.Str(a, "challenge", "")); }
      WsVersion = J.Str(helloD, "obsWebSocketVersion", "");
      await Tx(J.Ser(J.D("op", 1, "d", d)));
      if (!await Task.Run(() => identified.Wait(5000))) { if (a != null) { SetState("auth", "OBS refused the WebSocket password"); throw new Exception("OBS refused the WebSocket password (System check > Repair OBS fixes this)"); } throw new Exception("OBS did not accept the connection"); } }
    protected override void OnConnected() {
      Task.Run(async () => { try { var v = await Call("GetVersion", null); ObsVersion = J.Str(v, "obsVersion", ""); } catch { }
        if (OnReady != null) { try { OnReady(); } catch (Exception e) { Log.Err("obs", "after connect: " + e.Message); } } }); }
    protected override void OnText(string raw) {
      var m = J.Parse(raw); int op = J.Int(m, "op", -1); var d = J.Obj(m, "d") ?? new Dictionary<string, object>();
      if (op == 0) { helloD = d; gotHello.Set(); }
      else if (op == 2) identified.Set();
      else if (op == 7) Complete(J.Str(d, "requestId", ""), d);
      else if (op == 5 && OnObsEvent != null) OnObsEvent(J.Str(d, "eventType", ""), J.Obj(d, "eventData") ?? new Dictionary<string, object>()); }
    protected override void StateChanged() { Obs.Changed(); }
    // returns responseData, or throws with OBS's own error text
    public async Task<Dictionary<string, object>> Call(string type, Dictionary<string, object> data) {
      if (!Ready) throw new Exception(State == "off" ? "OBS is not open" : "OBS is not connected (" + (Detail.Length > 0 ? Detail : State) + ")");
      var id = NewId(); var r = await Ask(id, J.Ser(J.D("op", 6, "d", J.D("requestType", type, "requestId", id, "requestData", data ?? new Dictionary<string, object>()))), 8000);
      if (r == null) throw new Exception("OBS did not answer");
      if (!J.Bool(r, "requestStatus.result", false)) throw new Exception(J.Str(r, "requestStatus.comment", "OBS error " + J.Str(r, "requestStatus.code", "?")));
      return J.Obj(r, "responseData") ?? new Dictionary<string, object>(); }
  }

  public static class Obs {
    public static readonly ObsLink Link = new ObsLink();
    public const string AudioScene = "IXC Audio", PlayerInput = "IXC Music Player", TtsInput = "IXC TTS";
    public static string ConfigDir { get { var o = Environment.GetEnvironmentVariable("IXC_OBS_CONFIG"); return !string.IsNullOrEmpty(o) ? o : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio"); } }
    static string WsFile { get { return Path.Combine(ConfigDir, "plugin_config", "obs-websocket", "config.json"); } }
    public static Dictionary<string, object> WsConfig() { try { if (File.Exists(WsFile)) return J.Parse(File.ReadAllText(WsFile)); } catch { } return new Dictionary<string, object>(); }
    static DateTime runAt = DateTime.MinValue; static bool runCache;
    public static bool Running() {
      if ((DateTime.Now - runAt).TotalSeconds < 3) return runCache;
      bool r = false; try { r = Process.GetProcessesByName("obs64").Length > 0 || Process.GetProcessesByName("obs").Length > 0 || Process.GetProcessesByName("obs32").Length > 0; } catch { }
      runCache = r; runAt = DateTime.Now; return r; }
    public static void Init() {
      Link.OnReady = () => { Log.Info("obs", "connected to OBS " + Link.ObsVersion + " (obs-websocket " + Link.WsVersion + ")"); var t = AutoSetup(false); if (Music.Enabled) Music.OnObsReady(); };
      Link.OnObsEvent = (type, d) => { if (Music.Enabled) Music.OnObsEvent(type, d); };
      Link.Start(); }
    public static void Changed() { Hub.Publish("status", Status.Msg()); }

    // ---------- detection ----------
    public static Dictionary<string, object> Detect() {
      string exe = null, ver = "";
      try { foreach (var n in new[] { "obs64", "obs" }) foreach (var p in Process.GetProcessesByName(n)) { try { exe = p.MainModule.FileName; } catch { } if (exe != null) break; } } catch { }
      if (exe == null && U.IsWindows) {
        foreach (var k in new[] { "SOFTWARE\\OBS Studio", "SOFTWARE\\WOW6432Node\\OBS Studio" }) { try { using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(k)) { var dir = key == null ? null : key.GetValue("") as string; if (dir != null) { var f = Path.Combine(dir, "bin", "64bit", "obs64.exe"); if (File.Exists(f)) { exe = f; break; } } } } catch { } }
        if (exe == null) foreach (var pf in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramW6432") }) {
          if (string.IsNullOrEmpty(pf)) continue; var f = Path.Combine(pf, "obs-studio", "bin", "64bit", "obs64.exe"); if (File.Exists(f)) { exe = f; break; } }
        if (exe == null) { var steam = Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "C:\\Program Files (x86)", "Steam", "steamapps", "common", "OBS Studio", "bin", "64bit", "obs64.exe"); if (File.Exists(steam)) exe = steam; } }
      if (exe != null) { try { ver = FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? ""; } catch { } }
      var ws = WsConfig(); bool configured = Directory.Exists(ConfigDir);
      return J.D("installed", exe != null || configured, "path", exe, "version", ver.Length > 0 ? ver : Link.ObsVersion, "running", Running(), "usedBefore", configured,
        "websocketEnabled", J.Bool(ws, "server_enabled", false), "websocketPort", J.Int(ws, "server_port", 4455), "connected", Link.Ready, "state", Link.State, "detail", Link.Detail); }

    // ---------- turn on obs-websocket (OBS must be closed: it rewrites the file when it exits) ----------
    public static string EnableWebSocket() {
      if (Running()) { var c = WsConfig(); if (J.Bool(c, "server_enabled", false)) return null; return "Close OBS first (File > Exit) - IXC will then turn on its WebSocket server by itself."; }
      try { var c = WsConfig(); bool changed = false;
        if (!J.Bool(c, "server_enabled", false)) { c["server_enabled"] = true; changed = true; }
        if (J.Int(c, "server_port", 0) == 0) { c["server_port"] = 4455; changed = true; }
        if (!c.ContainsKey("auth_required") || J.Str(c, "server_password", "").Length < 6) { c["auth_required"] = true; c["server_password"] = U.Token(12); changed = true; }
        if (!c.ContainsKey("alerts_enabled")) { c["alerts_enabled"] = false; changed = true; }
        if (J.Get(c, "first_load") as bool? != false) { c["first_load"] = false; changed = true; }
        if (!changed) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(WsFile));
        if (File.Exists(WsFile)) File.Copy(WsFile, WsFile + ".before-ixc.bak", true);
        File.WriteAllText(WsFile, J.Pretty(J.Ser(c)), new UTF8Encoding(false));
        Log.Info("obs", "turned on OBS's WebSocket server (port " + J.Int(c, "server_port", 4455) + ")"); Link.Kick(); return null; }
      catch (Exception e) { Log.Err("obs", "could not turn on the WebSocket server: " + e.Message); return "Could not change OBS's settings: " + e.Message; } }

    // ---------- docks (Docks > Custom Browser Docks), OBS must be closed ----------
    public static string DockUrl(string app) { return app == "music" ? Program.BaseUrl + "/music/dock.html" : app == "chat" ? Program.BaseUrl + "/chat/chat.html?dock=1&viewers=1" : Program.BaseUrl + "/app/?dock=1"; }
    public static string IniFile() {
      foreach (var n in new[] { "user.ini", "global.ini" }) { var f = Path.Combine(ConfigDir, n); try { if (File.Exists(f) && Regex.IsMatch(File.ReadAllText(f), "(?m)^\\[BasicWindow\\]")) return f; } catch { } }
      return null; }
    public static List<string> DockTitles() {
      var f = IniFile(); var r = new List<string>(); if (f == null) return r;
      var m = Regex.Match(File.ReadAllText(f), "(?m)^ExtraBrowserDocks=(.*)$"); if (!m.Success) return r;
      var arr = J.ParseAny(m.Groups[1].Value.Trim()) as object[]; if (arr != null) foreach (var o in arr) { var d = o as Dictionary<string, object>; if (d != null) r.Add(J.Str(d, "title", "")); }
      return r; }
    public static string AddDocks(IEnumerable<string> apps) {
      if (Running()) return "Close OBS first (File > Exit) - then IXC adds its panels to OBS by itself.";
      var f = IniFile(); if (f == null) return Directory.Exists(ConfigDir) ? "OBS hasn't saved its window layout yet: open OBS once, close it, then try again." : "OBS hasn't been opened on this PC yet: open OBS once, close it, then try again.";
      try {
        var t = File.ReadAllText(f); var m = Regex.Match(t, "(?m)^ExtraBrowserDocks=(.*)$");
        var docks = new List<object>(); if (m.Success) { var arr = J.ParseAny(m.Groups[1].Value.Trim()) as object[]; if (arr != null) docks.AddRange(arr); }
        var titles = new Dictionary<string, string> { { "music", "IXC Music" }, { "chat", "IXC ChatBox" }, { "app", "IXC" } };
        foreach (var a in apps) { var title = titles[a];
          docks.RemoveAll(o => { var d = o as Dictionary<string, object>; return d != null && J.Str(d, "title", "") == title; });
          docks.Add(J.D("title", title, "url", DockUrl(a), "uuid", Guid.NewGuid().ToString("N"))); }
        var line = "ExtraBrowserDocks=" + J.Ser(docks);
        File.Copy(f, f + ".before-ixc-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", true);
        if (m.Success) t = t.Remove(m.Index, m.Length).Insert(m.Index, line);
        else t = Regex.Replace(t, "(?m)^\\[BasicWindow\\]\\r?$", "[BasicWindow]\r\n" + line);
        File.WriteAllText(f, t, new UTF8Encoding(false));
        Log.Info("obs", "added the IXC panels to OBS (" + string.Join(", ", apps) + ")"); return null; }
      catch (Exception e) { Log.Err("obs", "could not add docks: " + e.Message); return "Could not add the IXC panels: " + e.Message; } }

    // ---------- browser sources, created through obs-websocket while OBS runs ----------
    static int setupBusy;
    public static string LastSetup = "not run yet"; public static bool SetupOk;
    static Dictionary<string, object> BrowserSettings(string url, int w, int h) {
      return J.D("url", url, "width", w, "height", h, "fps_custom", true, "fps", 30, "reroute_audio", true, "shutdown", false, "restart_when_active", false, "css", "body { background-color: rgba(0,0,0,0); margin: 0; overflow: hidden; }"); }
    static async Task<List<Dictionary<string, object>>> Inputs() {
      var r = await Link.Call("GetInputList", J.D("inputKind", "browser_source")); var list = new List<Dictionary<string, object>>();
      var arr = J.Get(r, "inputs") as ArrayList; if (arr != null) foreach (var o in arr) { var d = o as Dictionary<string, object>; if (d != null) list.Add(d); } return list; }
    static async Task<string> FindByUrl(string part, string preferName) {
      foreach (var i in await Inputs()) { var n = J.Str(i, "inputName", ""); if (n == preferName) return n; }
      foreach (var i in await Inputs()) { var n = J.Str(i, "inputName", ""); try { var s = await Link.Call("GetInputSettings", J.D("inputName", n)); if (J.Str(s, "inputSettings.url", "").Contains(part)) return n; } catch { } }
      return null; }
    static async Task<List<string>> Scenes() {
      var r = await Link.Call("GetSceneList", null); var l = new List<string>(); var arr = J.Get(r, "scenes") as ArrayList;
      if (arr != null) foreach (var o in arr) { var d = o as Dictionary<string, object>; if (d != null) l.Add(J.Str(d, "sceneName", "")); } return l; }
    static async Task<bool> InScene(string scene, string source) {
      try { var r = await Link.Call("GetSceneItemId", J.D("sceneName", scene, "sourceName", source)); return J.Int(r, "sceneItemId", 0) > 0; } catch { return false; } }
    static async Task EnsureInput(string scene, string name, string kind, Dictionary<string, object> settings, bool keepUserUrl) {
      bool exists = false; try { await Link.Call("GetInputSettings", J.D("inputName", name)); exists = true; } catch { }
      if (!exists) { await Link.Call("CreateInput", J.D("sceneName", scene, "inputName", name, "inputKind", kind, "inputSettings", settings, "sceneItemEnabled", true)); Log.Info("obs", "created source \"" + name + "\""); return; }
      if (!keepUserUrl) await Link.Call("SetInputSettings", J.D("inputName", name, "inputSettings", settings, "overlay", true));
      if (!await InScene(scene, name)) await Link.Call("CreateSceneItem", J.D("sceneName", scene, "sourceName", name, "sceneItemEnabled", true)); }
    // the music player and the TTS voice live in one scene "IXC Audio", which is nested into every scene so they're always heard
    public static async Task<Dictionary<string, object>> AutoSetup(bool force) {
      if (Interlocked.CompareExchange(ref setupBusy, 1, 0) != 0) return J.D("ok", false, "error", "setup is already running");
      var steps = new List<object>();
      try {
        if (!Link.Ready) { LastSetup = "OBS is not connected"; SetupOk = false; return J.D("ok", false, "error", "OBS is not connected yet. " + (Link.Detail.Length > 0 ? Link.Detail : "Open OBS."), "steps", steps); }
        if (!force && J.Bool(Cfg.D, "obs.setupDone", false) && !Settings.Bool("general.advanced")) { await VerifyPorts(); return J.D("ok", true, "skipped", true); }
        var scenes = await Scenes();
        if (!scenes.Contains(AudioScene)) { await Link.Call("CreateScene", J.D("sceneName", AudioScene)); steps.Add("created scene " + AudioScene); scenes = await Scenes(); }
        if (Music.Enabled) {
          var existing = await FindByUrl("/music/player.html", PlayerInput); var name = existing ?? PlayerInput;
          if (existing != null && existing != PlayerInput) { Cfg.Set("music.routing.inputName", existing); Cfg.Save(); }
          await EnsureInput(AudioScene, name, "browser_source", BrowserSettings(Program.BaseUrl + "/music/player.html", 320, 180), false); steps.Add("music player source ready"); }
        if (Chat.Enabled) {
          var existing = await FindByUrl("/chat/tts.html", TtsInput); var name = existing ?? TtsInput;
          await EnsureInput(AudioScene, name, "browser_source", BrowserSettings(Program.BaseUrl + "/chat/tts.html", 64, 64), false); steps.Add("chat voice (TTS) source ready"); }
        int added = 0;
        foreach (var s in scenes) { if (s == AudioScene) continue; if (!await InScene(s, AudioScene)) { try { await Link.Call("CreateSceneItem", J.D("sceneName", s, "sourceName", AudioScene, "sceneItemEnabled", true)); added++; } catch (Exception e) { Log.Warn("obs", "could not add IXC Audio to scene " + s + ": " + e.Message); } } }
        if (added > 0) steps.Add("added IXC Audio to " + added + " scene(s)");
        Cfg.Set("obs.setupDone", true); Cfg.Save(); SetupOk = true; LastSetup = "OBS is set up (" + DateTime.Now.ToString("HH:mm") + ")";
        Log.Info("obs", "OBS setup done: " + string.Join("; ", steps.Cast<string>()));
        if (Music.Enabled) Music.OnObsReady();
        return J.D("ok", true, "steps", steps); }
      catch (Exception e) { SetupOk = false; LastSetup = "OBS setup failed: " + U.Plain(e); Log.Err("obs", LastSetup); return J.D("ok", false, "error", LastSetup, "steps", steps); }
      finally { Interlocked.Exchange(ref setupBusy, 0); Changed(); } }
    // IXC moved to another port (another program took 8767): point every IXC browser source at the new address
    static async Task VerifyPorts() {
      try { foreach (var i in await Inputs()) { var n = J.Str(i, "inputName", ""); var s = await Link.Call("GetInputSettings", J.D("inputName", n)); var url = J.Str(s, "inputSettings.url", "");
          var m = Regex.Match(url, "^http://(localhost|127\\.0\\.0\\.1):(\\d+)(/(music|chat|overlay|core|app)/.*)$");
          if (m.Success && m.Groups[2].Value != Program.Port.ToString()) { await Link.Call("SetInputSettings", J.D("inputName", n, "inputSettings", J.D("url", Program.BaseUrl + m.Groups[3].Value), "overlay", true)); Log.Info("obs", "updated the address of \"" + n + "\" to port " + Program.Port); } } }
      catch (Exception e) { Log.Warn("obs", "port check: " + e.Message); } }
    // dashboard > Overlays > "Add to OBS": puts the overlay into the scene that is on air now
    public static async Task<Dictionary<string, object>> AddOverlay(string name, string url, int w, int h) {
      var cur = await Link.Call("GetCurrentProgramScene", null); var scene = J.Str(cur, "currentProgramSceneName", J.Str(cur, "sceneName", ""));
      if (scene.Length == 0) throw new Exception("OBS has no scene");
      var input = "IXC " + name; bool exists = false; try { await Link.Call("GetInputSettings", J.D("inputName", input)); exists = true; } catch { }
      var settings = BrowserSettings(url, w, h); settings["reroute_audio"] = false;
      if (!exists) await Link.Call("CreateInput", J.D("sceneName", scene, "inputName", input, "inputKind", "browser_source", "inputSettings", settings, "sceneItemEnabled", true));
      else { await Link.Call("SetInputSettings", J.D("inputName", input, "inputSettings", settings, "overlay", true)); if (!await InScene(scene, input)) await Link.Call("CreateSceneItem", J.D("sceneName", scene, "sourceName", input, "sceneItemEnabled", true)); }
      Log.Info("overlay", "added \"" + input + "\" to scene \"" + scene + "\"");
      return J.D("ok", true, "scene", scene, "source", input); }
    // uninstall: remove IXC's scene and sources from OBS (only things IXC created)
    public static async Task<Dictionary<string, object>> RemoveAll() {
      if (!Link.Ready) return J.D("ok", false, "error", "OBS is not open");
      var removed = new List<string>();
      foreach (var i in await Inputs()) { var n = J.Str(i, "inputName", ""); if (!n.StartsWith("IXC ")) continue; try { await Link.Call("RemoveInput", J.D("inputName", n)); removed.Add(n); } catch { } }
      try { await Link.Call("RemoveScene", J.D("sceneName", AudioScene)); removed.Add(AudioScene); } catch { }
      Cfg.Set("obs.setupDone", null); Cfg.Save(); Log.Info("obs", "removed IXC from OBS: " + string.Join(", ", removed));
      return J.D("ok", true, "removed", removed); }
    public static async Task<Dictionary<string, object>> Check() {
      var d = Detect(); if (!Link.Ready) return J.D("ok", false, "obs", d);
      bool scene = false, player = false, tts = false;
      try { scene = (await Scenes()).Contains(AudioScene); } catch { }
      try { player = await FindByUrl("/music/player.html", PlayerInput) != null; } catch { }
      try { tts = await FindByUrl("/chat/tts.html", TtsInput) != null; } catch { }
      return J.D("ok", scene && (!Music.Enabled || player) && (!Chat.Enabled || tts), "obs", d, "audioScene", scene, "playerSource", player, "ttsSource", tts); }
  }
}
