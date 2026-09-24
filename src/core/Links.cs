// IXC Core - connections to Streamer.bot (chat) and OBS (audio routing). Both reconnect by themselves with back-off.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
// Passwords are read from IXC's config or from the program's own settings file on this PC. They never leave this process.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  // one reconnecting JSON WebSocket connection (shared by the Streamer.bot and OBS clients)
  public abstract class Link {
    protected ClientWebSocket ws; readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    public volatile bool Ready; public string Status = "not connected", Name; public DateTime Since = DateTime.Now; public int Connects;
    protected readonly Dictionary<string, TaskCompletionSource<Dictionary<string, object>>> Pending = new Dictionary<string, TaskCompletionSource<Dictionary<string, object>>>();
    int idN; AutoResetEvent wake = new AutoResetEvent(false);
    protected abstract string Url();
    protected abstract Task Handshake();                                   // after connect: auth + subscribe
    protected abstract void OnText(Dictionary<string, object> m, string raw);
    public void Start() { new Thread(Loop) { IsBackground = true, Name = Name }.Start(); }
    public void Kick() { wake.Set(); }                                       // retry now (e.g. user pressed "Reconnect")
    void Loop() {
      int delay = 1000;
      while (true) {
        bool connected = false;
        try { RunOnce(ref connected); } catch (Exception e) { var ie = e is AggregateException ? e.InnerException : e; if (ie is AggregateException) ie = ie.InnerException; Status = Friendly(ie); }
        if (Ready) Ready = false; lock (Pending) { foreach (var t in Pending.Values) t.TrySetResult(null); Pending.Clear(); }
        if (connected) { Interlocked.Increment(ref Diag.Reconnects); Diag.Ev(Name + " disconnected - reconnecting"); delay = 1000; OnDisconnected(); }
        wake.WaitOne(delay); delay = Math.Min(delay * 2, 15000);
      }
    }
    protected virtual void OnDisconnected() { }
    protected virtual string Friendly(Exception e) { return e == null ? "error" : e.Message; }
    void RunOnce(ref bool connected) {
      var w = new ClientWebSocket(); w.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
      using (var cts = new CancellationTokenSource(4000)) w.ConnectAsync(new Uri(Url()), cts.Token).Wait();
      ws = w; Status = "handshake"; var recv = RecvLoop(w);
      Handshake().Wait(); connected = true; Connects++; Since = DateTime.Now; Ready = true; Status = "connected"; Diag.Ev(Name + " connected");
      recv.Wait();
      try { w.Dispose(); } catch { }
    }
    async Task RecvLoop(ClientWebSocket w) {
      var buf = new byte[32768]; var ms = new MemoryStream();
      while (w.State == WebSocketState.Open) {
        ms.SetLength(0); WebSocketReceiveResult r;
        do { r = await w.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None); if (r.MessageType == WebSocketMessageType.Close) return; ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
        var raw = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        try { OnText(J.Parse(raw), raw); } catch (Exception e) { Diag.Err(Name + " message: " + e.Message); }
      }
    }
    protected async Task Tx(string json) { var b = Encoding.UTF8.GetBytes(json); await sendLock.WaitAsync(); try { await ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None); } finally { sendLock.Release(); } }
    protected string NewId() { return "ixc" + Interlocked.Increment(ref idN); }
    protected void Complete(string id, Dictionary<string, object> m) { TaskCompletionSource<Dictionary<string, object>> t; lock (Pending) { if (!Pending.TryGetValue(id, out t)) return; Pending.Remove(id); } t.TrySetResult(m); }
    protected async Task<Dictionary<string, object>> Ask(string id, string json, int ms) {
      var t = new TaskCompletionSource<Dictionary<string, object>>(); lock (Pending) Pending[id] = t;
      try { await Tx(json); } catch { lock (Pending) Pending.Remove(id); return null; }
      if (await Task.WhenAny(t.Task, Task.Delay(ms)) != t.Task) { lock (Pending) Pending.Remove(id); return null; }
      return t.Task.Result; }
    protected static string Sha(string s) { using (var h = SHA256.Create()) return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(s))); }
  }

  // ---------------- Streamer.bot WebSocket server (chat for Twitch, Kick, YouTube) ----------------
  public class SbLink : Link {
    public Action<Dictionary<string, object>, string> OnEvent; public string AuthState = "none"; public List<string> Broadcasters = new List<string>();
    Dictionary<string, object> hello;
    public SbLink() { Name = "Streamer.bot"; }
    protected override string Url() { return J.Str(Cfg.D, "streamerbot.websocketUrl", "ws://127.0.0.1:8080/"); }
    protected override string Friendly(Exception e) { return e is WebSocketException || (e != null && e.Message.Contains("Unable to connect")) ? "Streamer.bot not reachable (is it running with its WebSocket server on?)" : base.Friendly(e); }
    readonly ManualResetEventSlim gotHello = new ManualResetEventSlim(false);
    protected override async Task Handshake() {
      if (!await Task.Run(() => gotHello.Wait(4000))) throw new Exception("no hello from Streamer.bot");
      gotHello.Reset();
      var a = J.Obj(hello, "authentication");
      if (a != null) {
        string pw = Password();
        var r = await Ask("auth", J.Ser(J.D("request", "Authenticate", "id", "auth", "authentication", Sha(Sha(pw + J.Str(a, "salt", "")) + J.Str(a, "challenge", "")))), 4000);
        AuthState = r == null ? "no answer" : J.Str(r, "status", "?") == "ok" ? "ok" : ("failed: " + J.Str(r, "error", "?"));
        if (AuthState != "ok") Diag.Err("Streamer.bot authentication " + AuthState + (pw.Length == 0 ? " (no password found)" : ""));
      } else AuthState = "not required";
      await Tx(J.Ser(J.D("request", "Subscribe", "id", "sub", "events", J.D(
        "Twitch", new[] { "ChatMessage", "ViewerCountUpdate", "StreamOnline", "StreamOffline", "ChatMessageDeleted", "UserBanned", "UserTimedOut" },
        "YouTube", new[] { "Message", "StatisticsUpdated", "BroadcastStarted", "BroadcastEnded", "MessageDeleted", "UserBanned" },
        "Kick", new[] { "ChatMessage", "ViewerCountUpdate", "StreamOnline", "StreamOffline", "ChatMessageDeleted", "UserBanned", "UserTimedOut" }))));
      var t = LoadBroadcasters();
    }
    async Task LoadBroadcasters() {   // your own account names (TTS "ignore my account")
      for (int i = 0; i < 40 && !Ready; i++) await Task.Delay(100);
      var r = await Request(J.D("request", "GetBroadcaster"), 4000); if (r == null) return;
      var names = new List<string>(); Collect(r, names, 0);
      lock (Broadcasters) { Broadcasters.Clear(); Broadcasters.AddRange(names.Distinct(StringComparer.OrdinalIgnoreCase)); }
      if (names.Count > 0) Diag.Ev("your accounts (from Streamer.bot): " + string.Join(", ", Broadcasters)); }
    static void Collect(object o, List<string> names, int depth) {
      var d = o as Dictionary<string, object>; if (d == null || depth > 5) { var a = o as System.Collections.ArrayList; if (a != null) foreach (var x in a) Collect(x, names, depth + 1); return; }
      foreach (var kv in d) { var s = kv.Value as string; var k = kv.Key.ToLowerInvariant();
        if (s != null && s.Length > 1 && (k == "broadcastuser" || k == "broadcastusername" || k == "broadcasteruser" || k == "broadcasterusername" || k == "username" || k == "displayname" || k == "channelname" || k == "login")) names.Add(s);
        else Collect(kv.Value, names, depth + 1); } }
    public string Password() {
      var p = J.Str(Cfg.D, "streamerbot.password", ""); if (p.Length > 0) return p;
      foreach (var f in SettingsFiles()) { try { if (!File.Exists(f)) continue; var s = J.Parse(File.ReadAllText(f)); var pw = J.Str(s, "websockets.authPassword", ""); if (pw.Length > 0) return pw; } catch { } }
      return ""; }
    static IEnumerable<string> SettingsFiles() {
      var sp = J.Str(Cfg.D, "streamerbot.settingsPath", "auto");
      if (sp.Length > 0 && sp != "auto") { yield return sp; yield break; }
      // Streamer.bot is portable: look at running copies first, then the usual places
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var pr in System.Diagnostics.Process.GetProcessesByName("Streamer.bot")) { string f = null; try { f = Path.Combine(Path.GetDirectoryName(pr.MainModule.FileName), "data", "settings.json"); } catch { } if (f != null && seen.Add(f)) yield return f; }
      var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      foreach (var root in new[] { Path.Combine(home, "Desktop"), Path.Combine(home, "Documents"), Path.Combine(home, "Downloads"), home, "C:\\", "D:\\" }) {
        string[] dirs; try { dirs = Directory.GetDirectories(root, "*treamer*bot*"); } catch { continue; }
        foreach (var d in dirs) { var f = Path.Combine(d, "data", "settings.json"); if (seen.Add(f)) yield return f; } } }
    protected override void OnText(Dictionary<string, object> m, string raw) {
      if (m.ContainsKey("event") && m.ContainsKey("data")) { if (OnEvent != null) OnEvent(m, raw); return; }
      if (J.Str(m, "request", "") == "Hello" || (!Ready && hello == null && m.ContainsKey("info"))) { hello = m; gotHello.Set(); return; }
      var id = J.Str(m, "id", null); if (id != null) Complete(id, m); }
    protected override void OnDisconnected() { hello = null; }
    public Task<Dictionary<string, object>> Request(Dictionary<string, object> req, int ms) { if (!Ready) return Task.FromResult<Dictionary<string, object>>(null); var id = NewId(); req["id"] = id; return Ask(id, J.Ser(req), ms); }
  }

  // ---------------- OBS (obs-websocket 5, built into OBS 28+) ----------------
  public class ObsLink : Link {
    public Action<string, Dictionary<string, object>> OnObsEvent; public Action OnReady; public string ObsVersion = "";
    Dictionary<string, object> helloD; readonly ManualResetEventSlim gotHello = new ManualResetEventSlim(false), identified = new ManualResetEventSlim(false);
    public ObsLink() { Name = "OBS"; }
    static Dictionary<string, object> ObsSettings() { try { var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugin_config", "obs-websocket", "config.json"); if (File.Exists(f)) return J.Parse(File.ReadAllText(f)); } catch { } return new Dictionary<string, object>(); }
    protected override string Url() { var u = J.Str(Cfg.D, "obs.websocketUrl", "auto"); if (u.Length > 0 && u != "auto") return u; return "ws://127.0.0.1:" + J.Int(ObsSettings(), "server_port", 4455) + "/"; }
    public bool Enabled() { return J.Bool(ObsSettings(), "server_enabled", true); }
    protected override string Friendly(Exception e) { return (e is WebSocketException || (e != null && e.Message.Contains("Unable to connect"))) ? (Enabled() ? "OBS not running (or its WebSocket server is off)" : "OBS WebSocket server is turned off (OBS > Tools > WebSocket Server Settings)") : base.Friendly(e); }
    protected override async Task Handshake() {
      if (!await Task.Run(() => gotHello.Wait(4000))) throw new Exception("no hello from OBS"); gotHello.Reset(); identified.Reset();
      var d = J.D("rpcVersion", 1, "eventSubscriptions", 8 /* Inputs */);
      var a = J.Obj(helloD, "authentication");
      if (a != null) { var pw = J.Str(Cfg.D, "obs.password", ""); if (pw.Length == 0) pw = J.Str(ObsSettings(), "server_password", "");
        d["authentication"] = Sha(Sha(pw + J.Str(a, "salt", "")) + J.Str(a, "challenge", "")); }
      ObsVersion = J.Str(helloD, "obsWebSocketVersion", "");
      await Tx(J.Ser(J.D("op", 1, "d", d)));
      if (!await Task.Run(() => identified.Wait(4000))) throw new Exception(a != null ? "OBS refused the password (set obs.password in config.json)" : "OBS did not identify us");
      if (OnReady != null) { var t = Task.Run(() => { Thread.Sleep(50); try { OnReady(); } catch (Exception e) { Diag.Err("obs ready: " + e.Message); } }); }
    }
    protected override void OnText(Dictionary<string, object> m, string raw) {
      int op = J.Int(m, "op", -1); var d = J.Obj(m, "d") ?? new Dictionary<string, object>();
      if (op == 0) { helloD = d; gotHello.Set(); }
      else if (op == 2) identified.Set();
      else if (op == 7) Complete(J.Str(d, "requestId", ""), d);
      else if (op == 5 && OnObsEvent != null) OnObsEvent(J.Str(d, "eventType", ""), J.Obj(d, "eventData") ?? new Dictionary<string, object>());
    }
    // returns responseData, or throws with OBS's own error text
    public async Task<Dictionary<string, object>> Call(string type, Dictionary<string, object> data) {
      if (!Ready) throw new Exception(Status);
      var id = NewId(); var r = await Ask(id, J.Ser(J.D("op", 6, "d", J.D("requestType", type, "requestId", id, "requestData", data ?? new Dictionary<string, object>()))), 5000);
      if (r == null) throw new Exception("OBS did not answer");
      if (!J.Bool(r, "requestStatus.result", false)) throw new Exception(J.Str(r, "requestStatus.comment", "OBS error " + J.Str(r, "requestStatus.code", "?")));
      return J.Obj(r, "responseData") ?? new Dictionary<string, object>(); }
  }
}
