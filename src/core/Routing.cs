// IXC Core - per-platform audio: which stream destinations hear the music and the chat voice (TTS).
// Windows has no "per-platform" audio, so IXC uses OBS audio tracks: each platform's output in OBS (multistream plugin or
// separate outputs) records one track (Twitch 1, Kick 2, YouTube 3 by default), and IXC switches the tracks of the
// "IXC Music Player" and "IXC TTS" sources on or off. Every change is read back from OBS: "applied" means OBS confirmed it.
// Tracks no platform uses (recording, VOD track) are never touched.
// Settings per source ("music" | "tts"): <src>.routing.mode = all | off | twitch | kick | youtube | custom,
// and for custom <src>.routing.on.<platform>. The track map music.routing.tracks.<platform> is shared by both sources.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public static class Routing {
    public static readonly string[] Plats = { "twitch", "kick", "youtube" };
    public static readonly string[] Sources = { "music", "tts" };
    class St { public string Status = "not applied yet", Actual = ""; public bool Ok; public Timer Debounce; }
    static readonly Dictionary<string, St> st = new Dictionary<string, St> { { "music", new St() }, { "tts", new St() } };

    public static void Init() {
      Settings.Changed += k => {
        if (k.StartsWith("music.routing.tracks")) { foreach (var s in Sources) Later(s); return; }
        foreach (var s in Sources) if (k.StartsWith(s + ".routing")) Later(s); }; }
    // several keys change together (mode + on.*): apply once, a moment later
    static void Later(string src) { var s = st[src]; lock (s) { if (s.Debounce == null) s.Debounce = new Timer(_ => { var t = Apply(src); }); s.Debounce.Change(80, Timeout.Infinite); } }

    public static string InputName(string src) { return Settings.Str(src + ".routing.inputName"); }
    public static List<int> Tracks(string p) { var t = Settings.List("music.routing.tracks." + p).Select(s => { int n; return int.TryParse(s, out n) ? n : 0; }).Where(n => n >= 1 && n <= 6).Distinct().ToList(); if (t.Count == 0) t.Add(p == "twitch" ? 1 : p == "kick" ? 2 : 3); return t; }
    public static string Mode(string src) { return Settings.Str(src + ".routing.mode"); }
    // does platform p hear this source (from the settings, not from OBS)
    public static bool On(string src, string p) {
      var m = Mode(src); return m == "all" || (m == "custom" ? Settings.Bool(src + ".routing.on." + p) : m == p); }
    public static Dictionary<string, object> Wanted(string src) { var d = new Dictionary<string, object>(); foreach (var p in Plats) d[p] = On(src, p); return d; }

    // one platform switched on or off: stored as the simplest mode that means the same (all / off / one platform / custom)
    public static string Toggle(string src, string p, bool on) {
      if (!Sources.Contains(src)) return "unknown audio source " + src; if (!Plats.Contains(p)) return "unknown platform " + p;
      var want = Plats.ToDictionary(x => x, x => x == p ? on : On(src, x)); return SetSet(src, want); }
    public static string SetSet(string src, Dictionary<string, bool> want) {
      var n = want.Count(kv => kv.Value); string mode = n == Plats.Length ? "all" : n == 0 ? "off" : n == 1 ? want.First(kv => kv.Value).Key : "custom";
      var patch = new Dictionary<string, object> { { src + ".routing.mode", mode } };
      foreach (var p in Plats) patch[src + ".routing.on." + p] = want[p];
      return Settings.Apply(patch); }

    static string TrackText(Dictionary<string, object> tr) { if (tr == null) return "?"; var on = tr.Where(kv => kv.Value is bool && (bool)kv.Value).Select(kv => kv.Key).ToList(); return on.Count == 0 ? "no tracks" : "track " + string.Join("+", on); }
    static List<int> WantTracks(string src) { return Plats.Where(p => On(src, p)).SelectMany(Tracks).Distinct().ToList(); }
    static List<int> Managed() { return Plats.SelectMany(Tracks).Distinct().ToList(); }
    static bool Matches(string src, Dictionary<string, object> tr) { var mine = WantTracks(src); return tr != null && Managed().All(n => J.Bool(tr, n.ToString(), false) == mine.Contains(n)); }

    public static void OnObsReady() { foreach (var s in Sources) if (Enabled(s)) { var t = Apply(s); } }
    static bool Enabled(string src) { return src == "music" ? Music.Enabled : Chat.Enabled; }
    public static void OnObsEvent(string type, Dictionary<string, object> d) {
      if (type != "InputAudioTracksChanged" && type != "InputCreated" && type != "InputNameChanged") return;
      var name = J.Str(d, "inputName", J.Str(d, "oldInputName", ""));
      if (type == "InputNameChanged") {   // renamed in OBS: follow the new name instead of losing the source
        var oldName = J.Str(d, "oldInputName", "");
        foreach (var src in Sources) if (oldName.Length > 0 && string.Equals(oldName, InputName(src), StringComparison.OrdinalIgnoreCase)) { Cfg.Set(src + ".routing.inputName", name); Cfg.Save(); } }   // the loop below then re-applies under the new name
      foreach (var src in Sources) {
        if (!string.Equals(name, InputName(src), StringComparison.OrdinalIgnoreCase)) continue; var s = st[src];
        if (type == "InputAudioTracksChanged") { var tr = J.Obj(d, "inputAudioTracks"); s.Actual = TrackText(tr);
          if (!Matches(src, tr)) { s.Status = "tracks were changed in OBS by hand: now " + s.Actual; s.Ok = false; Log.Info("obs", src + " tracks changed in OBS by hand: " + s.Actual); } Changed(src); }
        else { var t = Apply(src); } } }

    public static async Task<Dictionary<string, object>> Apply(string src) {
      var s = st[src];
      if (!Obs.Link.Ready) { s.Ok = false; s.Status = "saved - applies when OBS connects"; Changed(src); return Result(src, false, s.Status, true); }
      // right after OBS opens its WebSocket it can still be loading the scene collection ("not ready"): retry briefly
      for (int attempt = 1; ; attempt++) { var r = await Once(src); var e = J.Str(r, "error", "");
        if (!J.Bool(r, "ok", false) && e.IndexOf("not ready", StringComparison.OrdinalIgnoreCase) >= 0 && attempt < 10) { await Task.Delay(500); continue; } return r; } }
    static async Task<Dictionary<string, object>> Once(string src) {
      var s = st[src];
      try {
        await Obs.ResolveInput(src);   // the source may have been renamed in OBS: find it by its address
        var mine = WantTracks(src); var want = new Dictionary<string, object>();
        foreach (var n in Managed()) want[n.ToString()] = mine.Contains(n);
        await Obs.Link.Call("SetInputAudioTracks", J.D("inputName", InputName(src), "inputAudioTracks", want));
        var got = J.Obj(await Obs.Link.Call("GetInputAudioTracks", J.D("inputName", InputName(src))), "inputAudioTracks");   // what OBS really has now
        s.Actual = TrackText(got); s.Ok = Matches(src, got);
        s.Status = s.Ok ? "applied and checked in OBS: " + s.Actual : "OBS did not take the change: " + s.Actual;
        Log.Info("obs", (src == "tts" ? "chat voice" : "music") + " goes to " + Describe(src) + ": " + s.Status); Changed(src);
        return Result(src, s.Ok, s.Ok ? null : s.Status, false); }
      catch (Exception e) {
        s.Ok = false; var msg = U.Plain(e);
        s.Status = msg.Contains("No source was found") || msg.Contains("not found") ? "OBS has no source called \"" + InputName(src) + "\" - run System check > Repair OBS" : "OBS: " + msg;
        Log.Warn("obs", src + " routing: " + s.Status); Changed(src); return Result(src, false, s.Status, false); } }
    public static string Describe(string src) { var on = Plats.Where(p => On(src, p)).ToList(); return on.Count == 0 ? "no platform" : on.Count == Plats.Length ? "all platforms" : string.Join(" + ", on); }
    static Dictionary<string, object> Result(string src, bool ok, string err, bool pending) {
      var r = Info(src); r["ok"] = ok; if (err != null) r["error"] = err; if (pending) r["pending"] = true; return r; }
    static void Changed(string src) { try { if (src == "music") Music.Changed(); else Tts.Changed(); } catch (NullReferenceException) { } }   // TTS off in this build

    public static Dictionary<string, object> Info(string src) {
      var s = st[src];
      return J.D("source", src, "mode", Mode(src), "on", Wanted(src), "ok", s.Ok && Obs.Link.Ready, "status", Obs.Link.Ready ? s.Status : "applies when OBS is open", "tracks", s.Actual,
        "obs", Obs.Link.Ready ? "connected" : Obs.Link.State, "input", InputName(src), "map", J.D("twitch", Tracks("twitch"), "kick", Tracks("kick"), "youtube", Tracks("youtube"))); }

    // POST {mode} or {platform, on}; GET = state
    public static Dictionary<string, object> Handle(string src, Dictionary<string, object> b, out bool bad) {
      bad = false; string err;
      if (b.ContainsKey("platform")) err = Toggle(src, J.Str(b, "platform", ""), J.Bool(b, "on", true));
      else { var mode = J.Str(b, "mode", "all"); err = mode == "custom" ? "pick the platforms one by one" : Settings.Set(src + ".routing.mode", mode); }
      if (err != null) { bad = true; return J.D("ok", false, "error", err); }
      lock (st[src]) if (st[src].Debounce != null) st[src].Debounce.Change(Timeout.Infinite, Timeout.Infinite);   // applied right here, not again in a moment
      return Apply(src).Result; }
    public static bool Api(Ctx ctx, string path, string m) {
      var src = path == "/api/music/route" ? "music" : path == "/api/tts/route" ? "tts" : null; if (src == null) return false;
      if (m != "POST") { Http.Json(ctx, Info(src)); return true; }
      bool bad; var r = Handle(src, Http.BodyJson(ctx), out bad); Http.Json(ctx, bad ? 400 : 200, r); return true; }
    public static void OnWs(Client c, string type, Dictionary<string, object> msg) {
      if (type != "music.route" && type != "tts.route") return; var src = type == "music.route" ? "music" : "tts"; var id = J.Str(msg, "reqId", "");
      Task.Run(() => { Dictionary<string, object> r; bool bad; try { r = Handle(src, msg, out bad); } catch (Exception e) { r = J.D("ok", false, "error", U.Plain(e)); }
        Hub.Send(c, J.D("type", src + ".routed", "reqId", id, "result", r)); }); }
  }
}
