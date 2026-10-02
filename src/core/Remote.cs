// IXC Core - phone remote from anywhere (4G/5G, other Wi-Fi, another city) with no port forwarding, firewall or router setup.
//
//   phone --HTTPS/WSS--> IXC relay (Cloudflare Worker, one fixed address) <--WSS-- this PC (outgoing connection only)
//
// * Nothing on this PC listens on the network: IXC dials OUT to the relay and keeps that connection alive.
// * The relay address never changes, so phones stay paired across IXC/PC restarts, network changes and router restarts.
// * Pairing: the QR holds a one-time code (random, 5 minutes, single use) in the URL #fragment (never sent in requests).
//   The phone trades it for its own device key; the PC stores only a hash of it. Devices can be renamed and revoked.
// * The relay only forwards messages; this PC checks every phone's key and limits what a phone may do.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public class RelayLink : WsLink {
    readonly ManualResetEventSlim ready = new ManualResetEventSlim(false); volatile string relayError;
    public RelayLink() { Name = "Phone relay"; Area = "phone"; IdleTimeoutSec = 75; MaxBackoffMs = 60000; }
    protected override bool Wanted() { return Settings.Bool("remote.enabled") && Remote.RelayUrl.Length > 0; }
    protected override string WhyNotWanted() { return !Settings.Bool("remote.enabled") ? "phone remote is turned off" : "this build of IXC has no relay address (see docs/DEVELOPER-SETUP.md)"; }
    protected override string Url() { var u = Remote.RelayUrl.TrimEnd('/'); u = Regex.Replace(u, "^http", "ws"); return u + "/pc/" + Remote.PcId; }
    protected override void StateChanged() { Remote.Publish(); }
    protected override async Task Handshake() {
      ready.Reset(); relayError = null;
      await Tx(J.Ser(J.D("t", "hello", "secret", Remote.PcSecret, "v", Program.Version)));
      if (!await Task.Run(() => ready.Wait(10000))) throw new Exception("the relay did not answer");
      if (relayError != null) {
        if (relayError == "bad-secret") { Remote.NewIdentity(); throw new Exception("the relay didn't recognise this PC - created a new phone address (phones must scan a new QR)"); }
        throw new Exception("relay: " + relayError); } }
    Timer pingT;
    protected override void OnConnected() { Log.Info("phone", "connected to the phone relay"); Remote.Publish();
      if (pingT == null) pingT = new Timer(_ => { if (Ready) { try { var t = Tx("{\"t\":\"ping\"}"); } catch { } } }, null, 25000, 25000); }
    protected override void OnDisconnected() { Remote.DropAll(); }
    protected override void OnText(string raw) {
      var m = J.Parse(raw); var t = J.Str(m, "t", ""); var c = J.Str(m, "c", "");
      switch (t) {
        case "ready": ready.Set(); break;
        case "error": relayError = J.Str(m, "error", "error"); ready.Set(); break;
        case "pong": break;
        case "open": { var cc = c; Remote.Open(cc, J.Str(m, "ip", ""), J.Str(m, "ua", ""), s => SendTo(cc, s), () => CloseConn(cc, "closed by IXC")); break; }
        case "msg": Remote.Incoming(c, J.Str(m, "d", "")); break;
        case "close": Remote.Closed(c); break; } }
    public Task SendTo(string conn, string text) { return Tx(J.Ser(J.D("t", "msg", "c", conn, "d", text))); }
    public void CloseConn(string conn, string reason) { try { var t = Tx(J.Ser(J.D("t", "close", "c", conn, "reason", reason))); } catch { } }
  }

  public static class Remote {
    public static readonly RelayLink Link = new RelayLink();
    static readonly Dictionary<string, Client> conns = new Dictionary<string, Client>();
    static string pairHash; static DateTime pairUntil = DateTime.MinValue; static string pairUrl, pairMode = "";
    static readonly Dictionary<string, List<DateTime>> fails = new Dictionary<string, List<DateTime>>();
    static int pairOk, pairRefused; static readonly object L = new object();

    public static string RelayUrl { get { var u = Settings.Str("remote.relayUrl"); if (u.Length == 0) u = Program.Default("relayUrl"); return Ep.Get("relay", u); } }
    public static string PcId { get { var id = Secrets.Str("remote.pcId"); if (id == null) { NewIdentity(); id = Secrets.Str("remote.pcId"); } return id; } }
    public static string PcSecret { get { var s = Secrets.Str("remote.pcSecret"); if (s == null) { NewIdentity(); s = Secrets.Str("remote.pcSecret"); } return s; } }
    public static void NewIdentity() { Secrets.Set("remote.pcId", U.Token(16)); Secrets.Set("remote.pcSecret", U.Token(32)); Secrets.Set("remote.devices", null); Log.Warn("phone", "new phone address created - phones need to scan a new QR code"); }

    public static void Init() {
      lock (L) { var l = Devices(); if (l.RemoveAll(d => J.Bool(d, "temp", false)) > 0) SaveDevices(l); }   // Quick connect keys never outlive IXC
      Link.Start(); Quick.Init();
      Settings.Changed += k => { if (!k.StartsWith("remote.")) return; Link.Restart(); if (!Settings.Bool("remote.enabled")) Quick.Stop("phone remote turned off"); }; }
    // ---------- what phones may do ----------
    static readonly HashSet<string> Topics = new HashSet<string> { "chat", "tts", "music.state", "status", "viewers", "alerts", "reload" };
    static readonly HashSet<string> Messages = new HashSet<string> { "hello", "ping", "rtt", "chat.send", "chat.suggest", "chat.reconnect", "tts.set", "tts.say", "tts.test", "tts.skip", "tts.clear", "tts.replay", "tts.pause", "tts.resume",
      "music.cmd", "music.add", "music.search", "music.route", "settings.set", "viewers.get", "remote.logout" };
    public static bool AllowedTopic(string t) { return Topics.Contains(t); }
    public static bool AllowedMessage(string t) { return Messages.Contains(t); }
    public static readonly string[] PhoneSettings = { "tts.on", "tts.paused", "tts.voice", "tts.volume", "tts.speed", "tts.readMode", "tts.platforms", "chat.dockPlatforms", "songRequests.enabled",
      "music.volume", "music.shuffle", "music.repeat", "music.autoplay", "music.ducking.enabled", "music.routing.mode" };

    // ---------- connections through the relay ----------
    // a phone connection, through the relay or through Quick connect (connection ids starting with "q:")
    public static bool Open(string conn, string ip, string ua, Func<string, Task> transport, Action close) {
      var c = new Client { Remote = true, Authed = false, Ip = ip, Device = U.Trunc(ua, 120), Role = "mobile" };
      c.Transport = transport; c.Kill = () => { close(); Closed(conn); };
      lock (conns) { if (conns.Count >= 12) { close(); return false; } conns[conn] = c; }
      Hub.Add(c); Log.Debug("phone", "phone connecting (" + (IsQuick(conn) ? "Quick connect" : "relay") + ")"); return true; }
    public static bool IsQuick(string conn) { return conn.StartsWith("q:"); }
    // tell the phone it was signed out, and close its connection only after that message went out
    // (closing at once could overtake the message, and the phone would only see "disconnected")
    static void SignOut(Client c) {
      Task t; try { t = c.Transport(J.Ser(J.D("type", "auth", "ok", false, "reason", "revoked"))) ?? Task.FromResult(0); } catch { t = Task.FromResult(0); }
      Task.WhenAny(t, Task.Delay(3000)).ContinueWith(_ => { try { c.Kill(); } catch { } }); }
    public static int QuickPhones() { lock (conns) { int n = 0; foreach (var kv in conns) if (IsQuick(kv.Key) && kv.Value.Authed) n++; return n; } }
    public static void Closed(string conn) { Client c; lock (conns) { if (!conns.TryGetValue(conn, out c)) return; conns.Remove(conn); } Hub.Remove(c); if (c.Authed) { Log.Info("phone", "phone disconnected (" + DeviceName(c.DeviceId) + ")"); Publish(); } }
    // the relay connection dropped: its phones are gone (Quick connect phones are not affected)
    public static void DropAll() { Drop(k => !IsQuick(k)); }
    // Quick connect stopped: its phones are gone, and their temporary keys are forgotten
    public static void DropQuick() { Drop(IsQuick); lock (L) { var l = Devices(); if (l.RemoveAll(d => J.Bool(d, "temp", false)) > 0) SaveDevices(l); } lock (L) if (pairMode == "quick") { pairHash = null; pairUrl = null; } Publish(); }
    static void Drop(Func<string, bool> which) { List<Client> l; lock (conns) { var keys = conns.Keys.Where(which).ToList(); l = keys.Select(k => conns[k]).ToList(); foreach (var k in keys) conns.Remove(k); }
      foreach (var c in l) { Hub.Remove(c); try { c.Kill(); } catch { } } if (l.Count > 0) Publish(); }
    public static void Incoming(string conn, string text) {
      Client c; lock (conns) conns.TryGetValue(conn, out c); if (c == null) return;
      if (text.Length > 65536) return;
      if (!c.Authed) { PreAuth(c, conn, text); return; }
      Dictionary<string, object> m; if (J.TryParse(text, out m) && J.Str(m, "type", "") == "rtt") { c.Rtt = J.Int(m, "ms", 0); Touch(c.DeviceId, c.Ip); return; }
      if (!CheckDevice(c.DeviceId)) { SignOut(c); return; }
      Hub.Handle(c, text); }
    static void PreAuth(Client c, string conn, string text) {
      Dictionary<string, object> m; if (!J.TryParse(text, out m)) return; var type = J.Str(m, "type", "");
      if (type == "pair") {
        if (Limited(c.Ip) || Limited("*all*")) { Hub.Send(c, J.D("type", "paired", "ok", false, "error", "Too many wrong codes. Wait 10 minutes, then scan a new QR code.")); return; }
        bool good; lock (L) { good = pairHash != null && DateTime.Now < pairUntil && U.SlowEq(U.Sha(J.Str(m, "code", "")), pairHash); if (good) { pairHash = null; pairUrl = null; } }   // single use
        if (!good) { Fail(c.Ip); Fail("*all*"); pairRefused++; Log.Info("phone", "pairing refused (wrong, used or expired code) from " + c.Ip); Hub.Send(c, J.D("type", "paired", "ok", false, "error", "This QR code has expired or was already used. Make a new one on the PC.")); return; }
        var token = U.Token(32); var id = U.Token(9); var name = DefaultName(J.Str(m, "device", c.Device));
        var dev = J.D("id", id, "name", name, "hash", U.Sha(token), "created", DateTime.Now.ToString("o"), "lastSeen", DateTime.Now.ToString("o"), "ip", c.Ip, "ua", U.Trunc(c.Device, 120), "temp", IsQuick(conn));
        lock (L) { var list = Devices(); list.Add(dev); while (list.Count > 10) list.RemoveAt(0); SaveDevices(list); }
        c.Authed = true; c.DeviceId = id; pairOk++;
        Hub.Send(c, J.D("type", "paired", "ok", true, "deviceId", id, "token", token, "name", name, "version", Program.Version));
        Log.Info("phone", "phone paired: " + name + " (" + c.Ip + ")"); Publish(); Hub.Publish("remote", J.D("type", "remote.paired", "name", name)); return; }
      if (type == "auth") {
        var id = J.Str(m, "deviceId", ""); var token = J.Str(m, "token", "");
        Dictionary<string, object> dev; lock (L) dev = Devices().FirstOrDefault(d => J.Str(d, "id", "") == id);
        if (dev == null || !U.SlowEq(U.Sha(token), J.Str(dev, "hash", "-"))) { if (Limited(c.Ip)) return; Fail(c.Ip); Hub.Send(c, J.D("type", "auth", "ok", false, "reason", dev == null ? "unknown" : "bad")); Log.Info("phone", "phone sign-in refused (" + (dev == null ? "removed device" : "wrong key") + ") from " + c.Ip); return; }
        if (Expired(dev)) { Hub.Send(c, J.D("type", "auth", "ok", false, "reason", "expired")); RemoveDevice(id); return; }
        c.Authed = true; c.DeviceId = id; Touch(id, c.Ip);
        Hub.Send(c, J.D("type", "auth", "ok", true, "version", Program.Version, "name", J.Str(dev, "name", ""))); Log.Info("phone", "phone connected: " + J.Str(dev, "name", "")); Publish(); return; }
      if (type == "ping") Hub.Send(c, J.D("type", "pong", "t", J.Str(m, "t", ""))); }
    static string DefaultName(string ua) {
      ua = ua ?? ""; string os = ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad" : ua.Contains("Android") ? "Android phone" : ua.Contains("Windows") ? "Windows PC" : ua.Contains("Mac") ? "Mac" : "Phone";
      string br = ua.Contains("SamsungBrowser") ? "Samsung Internet" : ua.Contains("EdgA") || ua.Contains("Edg/") ? "Edge" : ua.Contains("CriOS") || ua.Contains("Chrome") ? "Chrome" : ua.Contains("Firefox") || ua.Contains("FxiOS") ? "Firefox" : ua.Contains("Safari") ? "Safari" : "";
      return os + (br.Length > 0 ? " (" + br + ")" : ""); }
    static bool Limited(string key) { lock (fails) { List<DateTime> l; if (!fails.TryGetValue(key, out l)) return false; l.RemoveAll(t => (DateTime.Now - t).TotalMinutes > 10); return l.Count >= (key == "*all*" ? 30 : 6); } }
    static void Fail(string key) { lock (fails) { List<DateTime> l; if (!fails.TryGetValue(key, out l)) { l = new List<DateTime>(); fails[key] = l; if (fails.Count > 5000) fails.Clear(); } l.Add(DateTime.Now); } }

    // ---------- devices ----------
    static List<Dictionary<string, object>> Devices() { var v = Secrets.Get("remote.devices") as System.Collections.ArrayList; var l = new List<Dictionary<string, object>>(); if (v != null) foreach (var o in v) { var d = o as Dictionary<string, object>; if (d != null) l.Add(J.Clone(d)); } return l; }
    static void SaveDevices(List<Dictionary<string, object>> l) { Secrets.Set("remote.devices", new System.Collections.ArrayList(l)); }
    static bool Expired(Dictionary<string, object> d) { DateTime t; return DateTime.TryParse(J.Str(d, "lastSeen", ""), null, System.Globalization.DateTimeStyles.RoundtripKind, out t) && (DateTime.Now - t).TotalDays > Settings.Int("remote.deviceDays"); }
    static bool CheckDevice(string id) { lock (L) return Devices().Any(d => J.Str(d, "id", "") == id); }
    static DateTime lastTouch = DateTime.MinValue;
    static void Touch(string id, string ip) { if ((DateTime.Now - lastTouch).TotalMinutes < 5) return; lastTouch = DateTime.Now; lock (L) { var l = Devices(); foreach (var d in l) if (J.Str(d, "id", "") == id) { d["lastSeen"] = DateTime.Now.ToString("o"); d["ip"] = ip; } SaveDevices(l); } }
    static string DeviceName(string id) { lock (L) { var d = Devices().FirstOrDefault(x => J.Str(x, "id", "") == id); return d == null ? "phone" : J.Str(d, "name", "phone"); } }
    public static string RenameDevice(string id, string name) { name = Regex.Replace(name ?? "", "[\\x00-\\x1f<>]", "").Trim(); if (name.Length == 0 || name.Length > 40) return "Use a name of 1 to 40 characters";
      lock (L) { var l = Devices(); var d = l.FirstOrDefault(x => J.Str(x, "id", "") == id); if (d == null) return "That device isn't paired any more"; d["name"] = name; SaveDevices(l); } Publish(); return null; }
    public static void RemoveDevice(string id) {
      lock (L) { var l = Devices(); l.RemoveAll(x => J.Str(x, "id", "") == id); SaveDevices(l); }
      List<Client> kick; lock (conns) kick = conns.Values.Where(x => x.DeviceId == id).ToList(); foreach (var c in kick) { SignOut(c); }
      Log.Info("phone", "phone removed"); Publish(); }
    public static void RemoveAll() { lock (L) SaveDevices(new List<Dictionary<string, object>>()); lock (L) { pairHash = null; pairUrl = null; } List<Client> kick; lock (conns) kick = conns.Values.ToList(); foreach (var c in kick) { SignOut(c); } Log.Info("phone", "all phones removed"); Publish(); }
    public static void Disconnect(string id) { List<Client> kick; lock (conns) kick = conns.Values.Where(x => x.DeviceId == id).ToList(); foreach (var c in kick) c.Kill(); }
    // mode: "relay" (permanent, needs the IXC relay), "quick" (temporary Cloudflare link, no setup) or "" (relay when it's ready, else quick)
    public static Dictionary<string, object> Pair(string mode) {
      if (!Settings.Bool("remote.enabled")) return J.D("ok", false, "error", "The phone remote is turned off (Settings > Phone).");
      if (mode == "quick" || (mode != "relay" && !(RelayUrl.Length > 0 && Link.Ready))) return PairQuick();
      if (RelayUrl.Length == 0) return J.D("ok", false, "error", "No permanent phone relay is set up - use Quick connect.");
      if (!Link.Ready) { Link.Kick(); for (int i = 0; i < 40 && !Link.Ready; i++) Thread.Sleep(250); }
      if (!Link.Ready) return J.D("ok", false, "error", "Can't reach the phone relay right now (" + (Link.Detail.Length > 0 ? Link.Detail : "no internet?") + "). Use Quick connect instead.");
      return NewCode("relay", RelayUrl.TrimEnd('/') + "/p/" + PcId); }
    static Dictionary<string, object> PairQuick() {
      if (!Quick.Supported) return J.D("ok", false, "error", "Quick connect works on Windows.");
      if (Quick.State != "online") { Quick.Start(); return J.D("ok", false, "starting", true, "state", Quick.State, "error", Quick.Message); }
      return NewCode("quick", Quick.Url + "/p/" + Quick.Id); }
    static Dictionary<string, object> NewCode(string mode, string page) {
      var code = U.Token(18);
      lock (L) { pairHash = U.Sha(code); pairUntil = DateTime.Now.AddMinutes(5); pairMode = mode; pairUrl = page + "#c=" + code; }
      Log.Info("phone", "phone QR code created (" + (mode == "quick" ? "Quick connect" : "relay") + ", valid 5 minutes, one use)"); Publish();
      return J.D("ok", true, "url", pairUrl, "mode", mode, "expiresIn", 300); }
    public static bool QuickPairing { get { lock (L) return pairMode == "quick" && pairHash != null && DateTime.Now < pairUntil; } }
    public static void CancelPair() { lock (L) { pairHash = null; pairUrl = null; } Publish(); }

    // ---------- state ----------
    public static Dictionary<string, object> Summary() {
      int phones = Hub.CountRemote(); int devices; lock (L) devices = Devices().Count;
      return J.D("relay", RelayUrl.Length == 0 ? "not-configured" : Link.State, "detail", Link.Detail, "connected", phones, "devices", devices); }
    public static Dictionary<string, object> StateMsg() {
      List<Dictionary<string, object>> devs; lock (L) devs = Devices();
      var online = Hub.Remotes();
      var list = devs.Select(d => { var cs = online.Where(c => c.DeviceId == J.Str(d, "id", "")).ToList();
        return (object)J.D("id", J.Str(d, "id", ""), "name", J.Str(d, "name", ""), "created", J.Str(d, "created", ""), "lastSeen", J.Str(d, "lastSeen", ""), "online", cs.Count > 0, "latencyMs", cs.Count > 0 ? cs.Max(c => c.Rtt) : 0,
          "temp", J.Bool(d, "temp", false), "connection", cs.Count == 0 ? "" : J.Bool(d, "temp", false) ? "Quick connect" : "relay"); }).ToList();
      bool pairing; lock (L) pairing = pairHash != null && DateTime.Now < pairUntil;
      return J.D("type", "remote.state", "enabled", Settings.Bool("remote.enabled"), "configured", RelayUrl.Length > 0, "relay", Link.State, "detail", Link.Detail, "devices", list,
        "pairing", pairing, "pairMode", pairMode, "pairExpiresIn", pairing ? (int)(pairUntil - DateTime.Now).TotalSeconds : 0, "pairings", pairOk, "refused", pairRefused,
        "quick", J.D("state", Quick.State, "message", Quick.Message, "url", Quick.Url, "supported", Quick.Supported)); }
    static Timer pubT;
    public static void Publish() { if (pubT == null) pubT = new Timer(_ => { Hub.Publish("remote", StateMsg()); Hub.Publish("status", Status.Msg()); }); pubT.Change(100, Timeout.Infinite); }
    public static Dictionary<string, object> DiagInfo() { var d = StateMsg(); d.Remove("type"); d["relayUrl"] = RelayUrl; return d; }

    // ---------- PC-side API (dashboard / docks) ----------
    public static bool LocalApi(Ctx ctx, string path, string m) {
      if (!path.StartsWith("/api/remote/")) return false;
      if (path == "/api/remote/status") { Http.Json(ctx, StateMsg()); return true; }
      if (m != "POST") { Http.Json(ctx, 405, J.D("error", "POST only")); return true; }
      var b = Http.BodyJson(ctx);
      switch (path) {
        case "/api/remote/pair": { var r = Pair(J.Str(b, "mode", "")); Http.Json(ctx, J.Bool(r, "ok", false) || J.Bool(r, "starting", false) ? 200 : 503, r); return true; }
        case "/api/remote/quick/stop": Quick.Stop("stopped by you"); Http.Json(ctx, StateMsg()); return true;
        case "/api/remote/cancel": CancelPair(); Http.Json(ctx, StateMsg()); return true;
        case "/api/remote/rename": { var err = RenameDevice(J.Str(b, "id", ""), J.Str(b, "name", "")); Http.Json(ctx, err == null ? 200 : 400, J.D("ok", err == null, "error", err)); return true; }
        case "/api/remote/revoke": if (J.Str(b, "id", "").Length > 0) RemoveDevice(J.Str(b, "id", "")); else RemoveAll(); Http.Json(ctx, StateMsg()); return true;
        case "/api/remote/disconnect": Disconnect(J.Str(b, "id", "")); Http.Json(ctx, StateMsg()); return true;
        case "/api/remote/reconnect": Link.Restart(); Http.Json(ctx, StateMsg()); return true;
        case "/api/remote/stop": RemoveAll(); Quick.Stop("phone access turned off"); Http.Json(ctx, StateMsg()); return true; }
      Http.Json(ctx, 404, J.D("error", "unknown")); return true; }
    public static void OnWs(Client c, string type, Dictionary<string, object> msg) {
      if (type == "remote.logout" && c.Remote) { RemoveDevice(c.DeviceId); }
      else if (type == "hello" && !c.Remote && c.Topics.Contains("remote")) Hub.Send(c, StateMsg()); }
  }
}
