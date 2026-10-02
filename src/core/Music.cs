// IXC Core - music. The queue, position and settings live HERE (saved to music.json), not inside the OBS browser source, so
// nothing is lost when OBS restarts, clears its cache or the source reloads. player.html only plays what IXC tells it to:
//   IXC -> player: music.play {item, startAt, autoplay} | music.ctl {op: pause/resume/stop/seek/volume/duck}
//   player -> IXC: music.ev {ev: ready/playing/paused/ended/error/progress, pos, dur, title}
// Also: YouTube search / "up next", YouTube + Spotify link checks, local music folders, and the audio-destination switch.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public static class Music {
    public static bool Enabled;
    static readonly object L = new object();
    // queue items: { uid, kind: yt | ytlist | local | q (matched on YouTube when its turn comes), id, list, local, q, ms, title, artist, durationSec, by, platform, from, bad, why, auto }
    static List<Dictionary<string, object>> queue = new List<Dictionary<string, object>>();
    static int index = -1; static bool playing, userPaused; static double pos, dur; static string note = "", liveTitle = ""; static DateTime posAt = DateTime.Now;
    static readonly List<string> history = new List<string>(); static int autoChain, errorsInRow; static volatile bool autoBusy;
    static string stateFile; static Timer saveT, progressT; static DateTime lastPlayerMsg = DateTime.MinValue;
    static bool ducked; static string routeStatus = "not applied yet", routeActual = ""; static bool routeOk;
    public static int Searches, Resolves;

    public static void Init() {
      Enabled = true; stateFile = Path.Combine(Cfg.DataDir, "music.json"); LoadState();
      Settings.Changed += k => { if (k.StartsWith("music.routing")) { var t = ApplyRoute(Settings.Str("music.routing.mode")); } if (k == "music.volume") Ctl(J.D("op", "volume", "v", Settings.Int("music.volume"))); if (k.StartsWith("music.") || k.StartsWith("nowPlaying.")) Changed(); if (k == "music.localFolders") Local.Rescan(); };
      // while playing, the player reports its position every few seconds; if it goes silent, it was closed or crashed
      progressT = new Timer(_ => Watchdog(), null, 5000, 5000);
      Local.Init(); }

    // ---------- persistence ----------
    static void LoadState() {
      try { if (!File.Exists(stateFile)) return; var d = J.Parse(File.ReadAllText(stateFile, Encoding.UTF8));
        lock (L) { queue = J.Objs(d, "queue").Select(Clean).Where(x => x != null).ToList(); index = Math.Min(J.Int(d, "index", -1), queue.Count - 1); pos = J.Num(d, "pos", 0); history.AddRange(J.List(d, "history")); } }
      catch (Exception e) { Log.Err("music", "music.json unreadable (" + e.Message + ") - starting with an empty queue"); try { File.Copy(stateFile, stateFile + ".damaged", true); } catch { } } }
    static void Save() { if (saveT == null) saveT = new Timer(_ => SaveNow()); saveT.Change(800, Timeout.Infinite); }
    public static void SaveNow() {
      string json; lock (L) json = J.Ser(J.D("queue", queue, "index", index, "pos", Math.Round(pos, 1), "history", history.Skip(Math.Max(0, history.Count - 60)).ToList()));
      try { var tmp = stateFile + ".tmp"; File.WriteAllText(tmp, json, new UTF8Encoding(false)); if (File.Exists(stateFile)) File.Replace(tmp, stateFile, stateFile + ".bak"); else File.Move(tmp, stateFile); }
      catch (Exception e) { Log.Err("music", "could not save the queue: " + e.Message); } }
    static Dictionary<string, object> Clean(Dictionary<string, object> it) {
      if (it == null) return null; var d = new Dictionary<string, object>();
      foreach (var k in new[] { "uid", "kind", "id", "list", "local", "q", "title", "artist", "by", "platform", "from", "why" }) { var v = J.Str(it, k, null); if (v != null) d[k] = U.Trunc(v, 300); }
      foreach (var k in new[] { "ms", "durationSec" }) if (it.ContainsKey(k)) d[k] = J.Int(it, k, 0);
      foreach (var k in new[] { "bad", "auto" }) if (J.Bool(it, k, false)) d[k] = true;
      if (!d.ContainsKey("uid")) d["uid"] = U.Token(6);
      if (!d.ContainsKey("kind")) d["kind"] = d.ContainsKey("list") ? "ytlist" : d.ContainsKey("local") ? "local" : d.ContainsKey("id") ? "yt" : d.ContainsKey("q") ? "q" : null;
      if (d["kind"] == null) return null;
      if ((string)d["kind"] == "yt" && !Regex.IsMatch(J.Str(d, "id", ""), "^[\\w-]{11}$")) return null;
      if ((string)d["kind"] == "ytlist" && !Regex.IsMatch(J.Str(d, "list", ""), "^[\\w-]{10,64}$")) return null;
      return d; }

    // ---------- state for docks, phones, overlays ----------
    public static Dictionary<string, object> Current() { lock (L) return index >= 0 && index < queue.Count ? queue[index] : null; }
    public static double Position() { lock (L) return playing ? pos + (DateTime.Now - posAt).TotalSeconds : pos; }
    public static string Describe(Dictionary<string, object> it) { var t = J.Str(it, "title", ""); var a = J.Str(it, "artist", ""); if (t.Length == 0) t = liveTitle.Length > 0 ? liveTitle : J.Str(it, "q", "a song"); return a.Length > 0 && !t.Contains(a) ? t + " - " + a : t; }
    public static string UpNext(int n) { lock (L) { var l = new List<string>(); for (int i = index + 1; i < queue.Count && l.Count < n; i++) if (!J.Bool(queue[i], "bad", false)) l.Add(Describe(queue[i])); return l.Count == 0 ? "nothing queued" : string.Join(" | ", l); } }
    public static Dictionary<string, object> State() {
      lock (L) {
        var cur = Current();
        return J.D("type", "music.state", "s", J.D("queue", queue, "index", index, "playing", playing, "paused", !playing && cur != null, "pos", Math.Round(Position(), 1), "dur", Math.Round(dur, 1),
          "title", cur == null ? "" : (liveTitle.Length > 0 ? liveTitle : J.Str(cur, "title", "")), "artist", cur == null ? "" : J.Str(cur, "artist", ""), "note", note,
          "volume", Settings.Int("music.volume"), "shuffle", Settings.Bool("music.shuffle"), "repeat", Settings.Bool("music.repeat"), "autoplay", Settings.Bool("music.autoplay"), "autostart", Settings.Bool("music.autostart"),
          "ducked", ducked, "playerConnected", Hub.Count("player") > 0, "ready", Hub.Count("player") > 0, "t", U.Now()), "route", RouteInfo()); } }
    static Timer pubT;
    public static void Changed() { if (pubT == null) pubT = new Timer(_ => { Hub.Publish("music.state", State()); NowPlaying.Update(); }); pubT.Change(60, Timeout.Infinite); }
    static void SetNote(string n) { note = n ?? ""; Changed(); }

    // ---------- talking to the player source ----------
    static void ToPlayer(Dictionary<string, object> m) { Hub.Publish("music.player", m); }
    static void Ctl(Dictionary<string, object> c) { c["type"] = "music.ctl"; ToPlayer(c); }
    static void Load(int i, bool autoplay, double startAt) {
      Dictionary<string, object> it; lock (L) { if (queue.Count == 0) return; index = ((i % queue.Count) + queue.Count) % queue.Count; it = queue[index]; liveTitle = ""; pos = startAt; dur = J.Int(it, "durationSec", 0); posAt = DateTime.Now; errorsInRow = it.ContainsKey("bad") ? errorsInRow : 0; }
      if ((string)it["kind"] == "q") { var want = index; SetNote("Finding \"" + J.Str(it, "title", J.Str(it, "q", "")) + "\" on YouTube..."); Task.Run(() => {
          var mt = MatchYouTube(J.Str(it, "q", ""), J.Int(it, "ms", 0));
          lock (L) { if (index != want || !queue.Contains(it)) return; if (mt != null) { it["kind"] = "yt"; it["id"] = mt["id"]; if (!it.ContainsKey("durationSec") && mt.ContainsKey("length")) it["durationSec"] = Secs((string)mt["length"]); } else { it["bad"] = true; it["why"] = "no match found on YouTube"; } }
          if (mt != null) Load(want, autoplay, startAt); else { SetNote("Skipped: " + Describe(it) + " (no match on YouTube)"); Next("error"); } }); return; }
      userPaused = !autoplay; if (autoplay) playing = true;
      if (J.Str(it, "kind", "") == "yt" || J.Str(it, "kind", "") == "local") { var key = J.Str(it, "id", J.Str(it, "local", "")); lock (L) { history.Remove(key); history.Add(key); if (history.Count > 80) history.RemoveAt(0); } }
      var item = J.Clone(it); if (J.Str(it, "kind", "") == "local") item["url"] = "/api/music/local/" + J.Str(it, "local", "");
      ToPlayer(J.D("type", "music.play", "item", item, "startAt", Math.Floor(startAt), "autoplay", autoplay, "volume", Settings.Int("music.volume"), "duck", DuckLevel(), "shuffle", Settings.Bool("music.shuffle")));
      note = ""; Log.Info("music", (autoplay ? "playing " : "loaded ") + Describe(it)); Save(); Changed(); }
    // next playable song; wraps only with Repeat; Autoplay adds similar songs when the queue runs out
    static void Next(string reason) {
      int n; int cur; lock (L) { n = queue.Count; cur = index; }
      if (n == 0) { Stop("Queue is empty"); return; }
      if (Settings.Bool("music.shuffle") && n > 1) { List<int> ok; lock (L) ok = Enumerable.Range(0, n).Where(k => k != cur && !J.Bool(queue[k], "bad", false)).ToList(); if (ok.Count > 0) { Load(ok[U.Rand(ok.Count)], true, 0); return; } }
      bool wrap = Settings.Bool("music.repeat") && !(Settings.Bool("music.autoplay") && reason != "user");
      for (int step = 1; step <= n; step++) { int k = cur + step; if (k >= n && !wrap) break; bool bad; lock (L) bad = J.Bool(queue[k % n], "bad", false); if (!bad) { Load(k % n, true, 0); return; } }
      if (Settings.Bool("music.autoplay")) { AutoFill(); return; }
      Stop("End of queue"); }
    static void Stop(string why) { playing = false; Ctl(J.D("op", "stop")); SetNote(why); Log.Info("music", why); }
    static void AutoFill() {
      if (autoBusy) return; if (autoChain >= 3) { Stop("Autoplay: similar songs would not play - add a song"); return; } autoBusy = true; autoChain++; SetNote("Autoplay: finding similar songs...");
      Task.Run(() => {
        try {
          string seed = null; lock (L) { for (int k = index; k >= 0 && seed == null; k--) { if (k < queue.Count && J.Str(queue[k], "kind", "") == "yt" && !J.Bool(queue[k], "bad", false)) seed = J.Str(queue[k], "id", null); } if (seed == null) seed = history.LastOrDefault(h => Regex.IsMatch(h, "^[\\w-]{11}$")); }
          if (seed == null) { Stop("Autoplay: nothing to base it on - add a song"); return; }
          string exclude; lock (L) exclude = string.Join(",", history.Concat(queue.Select(q => J.Str(q, "id", ""))).Where(x => x.Length > 0).Distinct());
          var rel = Related(seed, exclude); if (rel.Count == 0) { Stop("Autoplay: no similar songs found"); return; }
          int first; lock (L) { first = queue.Count; foreach (var r in rel.Take(3)) queue.Add(J.D("uid", U.Token(6), "kind", "yt", "id", r["id"], "title", r["title"], "durationSec", Secs((string)r["length"]), "auto", true));
            while (queue.Count > 80 && index > 0) { queue.RemoveAt(0); index--; first--; } }
          Log.Info("music", "autoplay added " + Math.Min(3, rel.Count) + " similar songs"); Load(first, true, 0); }
        catch (Exception e) { Stop("Autoplay: couldn't reach YouTube"); Log.Warn("music", "autoplay: " + e.Message); }
        finally { autoBusy = false; } }); }

    // ---------- commands from docks / phones / chat ----------
    public static string Command(string cmd, Dictionary<string, object> c) {
      c = c ?? new Dictionary<string, object>(); int i = J.Int(c, "i", -1);
      switch (cmd) {
        case "play": userPaused = false; autoChain = 0; { var cur = Current(); if (cur == null) { if (queue.Count > 0) Load(0, true, 0); else return "The queue is empty - add a song first"; } else if (J.Bool(cur, "bad", false)) Next("user"); else if (!LoadedInPlayer) Load(index, true, Position()); else { playing = true; Ctl(J.D("op", "resume")); } } break;
        case "pause": userPaused = true; Ctl(J.D("op", "pause")); break;
        case "toggle": return Command(playing ? "pause" : "play", c);
        case "next": autoChain = 0; Next("user"); break;
        case "prev": { if (Position() > 5) { Ctl(J.D("op", "seek", "t", 0)); pos = 0; posAt = DateTime.Now; break; } int n = queue.Count; for (int s = 1; s < n; s++) { int k = index - s; if (k < 0 && !Settings.Bool("music.repeat")) break; int kk = (k + n) % n; if (!J.Bool(queue[kk], "bad", false)) { Load(kk, true, 0); return null; } } Ctl(J.D("op", "seek", "t", 0)); break; }
        case "jump": { if (i < 0 || i >= queue.Count) return "That song isn't in the queue any more"; if (i == index && LoadedInPlayer && !J.Bool(queue[i], "bad", false)) return Command("play", null); lock (L) { queue[i].Remove("bad"); queue[i].Remove("why"); } autoChain = 0; Load(i, true, 0); break; }
        case "remove": { var uid = J.Str(c, "uid", null); lock (L) { if (uid != null) i = queue.FindIndex(q => J.Str(q, "uid", "") == uid); if (i < 0 || i >= queue.Count) return "That song isn't in the queue any more"; }
          bool wasCur = i == index; bool wasPlaying = playing; lock (L) { queue.RemoveAt(i); if (i < index) index--; }
          if (wasCur) { if (queue.Count == 0) { lock (L) index = -1; Stop(""); } else { lock (L) index = Math.Min(i, queue.Count) - 1; if (wasPlaying) Next("remove"); else { lock (L) index = Math.Min(i, queue.Count - 1); Load(index, false, 0); } } }
          break; }
        case "move": { int f = J.Int(c, "from", -1), t = J.Int(c, "to", -1); lock (L) { if (f < 0 || f >= queue.Count || t < 0 || t >= queue.Count) return "can't move there"; var it = queue[f]; queue.RemoveAt(f); queue.Insert(t, it);
            if (index == f) index = t; else if (f < index && t >= index) index--; else if (f > index && t <= index) index++; } break; }
        case "clear": lock (L) { queue.Clear(); index = -1; } Stop(""); break;
        case "clearPlayed": lock (L) { if (index > 0) { queue.RemoveRange(0, index); index = 0; } } break;
        case "volume": return Settings.Set("music.volume", J.Int(c, "v", 40));
        case "shuffle": return Settings.Set("music.shuffle", J.Bool(c, "on", false));
        case "repeat": return Settings.Set("music.repeat", J.Bool(c, "on", true));
        case "autoplay": return Settings.Set("music.autoplay", J.Bool(c, "on", true));
        case "autostart": return Settings.Set("music.autostart", J.Bool(c, "on", false));
        case "seek": { var t = Math.Max(0, J.Num(c, "t", 0)); pos = t; posAt = DateTime.Now; Ctl(J.D("op", "seek", "t", t)); break; }
        default: return "unknown music command: " + cmd; }
      Save(); Changed(); return null; }
    static bool LoadedInPlayer { get { return Hub.Count("player") > 0 && (DateTime.Now - lastPlayerMsg).TotalSeconds < 30 && lastLoadedUid == J.Str(Current() ?? new Dictionary<string, object>(), "uid", "?"); } }
    static string lastLoadedUid = "";
    // add a resolved item ("add" = end of the queue, "playnext", "playnow")
    public static int AddItem(Dictionary<string, object> it, string mode) {
      it = Clean(it); if (it == null) return -1; int at;
      lock (L) {
        if (mode == "playnow" || mode == "playnext") { at = index < 0 ? 0 : index + 1; queue.Insert(at, it); }
        else { queue.Add(it); at = queue.Count - 1; }
        while (queue.Count > 500 && index > 0) { queue.RemoveAt(0); index--; at--; } }
      autoChain = 0;
      if (mode == "playnow") Load(at, true, 0);
      else if (index < 0 || (!playing && !userPaused && Hub.Count("player") > 0 && (note.StartsWith("End of queue") || note.StartsWith("Autoplay") || note.StartsWith("Queue is empty") || note.Length == 0 && Current() == null))) Load(at, true, 0);
      Save(); Changed(); return at; }
    public static List<Dictionary<string, object>> Queue() { lock (L) return queue.Select(J.Clone).ToList(); }
    public static int IndexOf(string uid) { lock (L) return queue.FindIndex(q => J.Str(q, "uid", "") == uid); }
    public static int CurrentIndex { get { lock (L) return index; } }

    // ---------- events from the player source ----------
    static void PlayerEvent(Client c, Dictionary<string, object> m) {
      lastPlayerMsg = DateTime.Now; var ev = J.Str(m, "ev", ""); var uid = J.Str(m, "uid", "");
      bool forCurrent; lock (L) forCurrent = uid.Length == 0 || (Current() != null && J.Str(Current(), "uid", "") == uid);
      if (J.Get(m, "pos") != null && forCurrent) { pos = J.Num(m, "pos", pos); posAt = DateTime.Now; }
      if (J.Get(m, "dur") != null && forCurrent && J.Num(m, "dur", 0) > 0) dur = J.Num(m, "dur", dur);
      var t = J.Str(m, "title", ""); if (t.Length > 0 && forCurrent) { liveTitle = t; var cur = Current(); if (cur != null && J.Str(cur, "kind", "") == "yt" && J.Str(cur, "title", "") != t) { lock (L) cur["title"] = t; Save(); } }
      switch (ev) {
        case "ready": Log.Info("music", "player ready" + (J.Bool(m, "yt", true) ? "" : " (YouTube unavailable - only local songs can play)"));
          if (forCurrent && uid.Length > 0 && J.Bool(m, "playing", false)) { lastLoadedUid = uid; playing = true; break; }   // the page only reconnected: keep playing
          OnPlayerReady(); break;
        case "loaded": lastLoadedUid = uid; break;
        case "playing": if (forCurrent) { lastLoadedUid = uid; playing = true; note = ""; autoChain = 0; errorsInRow = 0; } break;
        case "paused": if (forCurrent) { playing = false; if (!userPaused) Log.Info("music", "player paused by itself"); } break;
        case "ended": if (forCurrent) { playing = false; Next("ended"); } return;
        case "error": if (forCurrent) PlayerError(J.Int(m, "code", 0), J.Str(m, "msg", "")); return;
        case "progress": break;
        case "legacy": Legacy(m); return; }
      if (ev != "progress") Changed(); }
    static void PlayerError(int code, string msg) {
      var it = Current(); if (it == null) return;
      var why = code == 101 || code == 150 || code == 153 ? "blocked by its owner outside YouTube" : code == 100 ? "removed or private" : code == 2 ? "invalid video" : code == 404 ? "file missing" : msg.Length > 0 ? msg : "could not play";
      lock (L) { it["bad"] = true; it["why"] = why; }
      errorsInRow++; Log.Warn("music", "skipped " + Describe(it) + " (" + why + ")"); SetNote("Skipped: " + Describe(it) + " (" + why + ")");
      if (errorsInRow >= 8) { Stop("Several songs in a row couldn't play - check your internet"); errorsInRow = 0; return; }
      Task.Delay(700).ContinueWith(_ => Next("error")); }
    static void OnPlayerReady() {
      // the player (re)connected: OBS started, the source reloaded, or IXC restarted. Resume where we were.
      var cur = Current(); lastLoadedUid = "";
      Ctl(J.D("op", "duck", "level", DuckLevel(), "fadeMs", 0));
      if (cur == null) return;
      bool start = playing || (Settings.Bool("music.autostart") && !userPaused && !startedOnce);
      startedOnce = true; Load(index, start, Position()); }
    static bool startedOnce;
    // v2 kept the queue inside the browser source: take it over once
    static void Legacy(Dictionary<string, object> m) {
      lock (L) { if (queue.Count > 0) return; var q = J.Objs(m, "queue").Select(Clean).Where(x => x != null).ToList(); if (q.Count == 0) return; queue = q; index = Math.Min(J.Int(m, "index", 0), q.Count - 1); pos = J.Num(m, "lastPos", 0); }
      foreach (var k in new[] { "shuffle", "repeat", "autoplay", "autostart" }) if (J.Get(m, k) is bool) Cfg.Set("music." + k, J.Bool(m, k, false));
      if (J.Get(m, "volume") != null) Cfg.Set("music.volume", J.Int(m, "volume", 40)); Cfg.Save();
      Log.Info("music", "took over the v2 queue from the player (" + queue.Count + " songs)"); SaveNow(); OnPlayerReady(); Changed(); }
    static void Watchdog() {
      if (!playing) return;
      if (Hub.Count("player") == 0) { playing = false; SetNote("The music player source isn't open in OBS"); return; }
      if ((DateTime.Now - lastPlayerMsg).TotalSeconds > 25) { Log.Warn("music", "player stopped reporting - reloading the song"); lastPlayerMsg = DateTime.Now; var cur = Current(); if (cur != null) Load(index, true, Position()); } }

    // ---------- ducking: music gets quieter while the chat voice speaks ----------
    public static void Duck(bool on) {
      if (!Settings.Bool("music.ducking.enabled")) on = false; if (on == ducked) return; ducked = on;
      Ctl(J.D("op", "duck", "level", DuckLevel(), "fadeMs", on ? Settings.Int("music.ducking.fadeDownMs") : Settings.Int("music.ducking.fadeUpMs")));
      Log.Debug("music", on ? "ducking for TTS" : "TTS done - music back to normal"); Changed(); }
    // 0..1 multiplier the player applies on top of the normal volume (the normal volume itself is never changed)
    static double DuckLevel() {
      if (!ducked) return 1.0; var vol = Math.Max(1, Settings.Int("music.volume")); double lvl = Settings.Int("music.ducking.level") / 100.0;
      double min = Settings.Int("music.ducking.minVolume") / (double)vol; return Math.Round(Math.Min(1.0, Math.Max(lvl, Math.Min(min, 1.0))), 3); }

    // ---------- audio destination (which OBS audio tracks carry the music) ----------
    static readonly string[] Plats = { "twitch", "kick", "youtube" };
    static string InputName() { return Settings.Str("music.routing.inputName"); }
    static bool InputMatches(string n) { return string.Equals(n, InputName(), StringComparison.OrdinalIgnoreCase); }
    public static List<int> Tracks(string p) { var t = Settings.List("music.routing.tracks." + p).Select(s => { int n; return int.TryParse(s, out n) ? n : 0; }).Where(n => n >= 1 && n <= 6).Distinct().ToList(); if (t.Count == 0) t.Add(p == "twitch" ? 1 : p == "kick" ? 2 : 3); return t; }
    static string TrackText(Dictionary<string, object> tr) { if (tr == null) return "?"; var on = tr.Where(kv => kv.Value is bool && (bool)kv.Value).Select(kv => kv.Key).ToList(); return on.Count == 0 ? "no tracks" : "track " + string.Join("+", on); }
    static string ModeFor(Dictionary<string, object> tr) {
      if (tr == null) return "?"; Func<int, bool> on = n => J.Bool(tr, n.ToString(), false);
      var hit = Plats.Where(p => Tracks(p).All(on)).ToList(); var managed = Plats.SelectMany(Tracks).Distinct().ToList();
      if (hit.Count == 3) return "all"; if (hit.Count == 1 && managed.Count(on) == Tracks(hit[0]).Count) return hit[0]; if (managed.All(n => !on(n))) return "off"; return "custom"; }
    public static void OnObsReady() { var t = ApplyRoute(Settings.Str("music.routing.mode")); }
    public static void OnObsEvent(string type, Dictionary<string, object> d) {
      if ((type == "InputAudioTracksChanged" || type == "InputCreated" || type == "InputNameChanged") && InputMatches(J.Str(d, "inputName", J.Str(d, "oldInputName", "")))) {
        if (type == "InputAudioTracksChanged") { var tr = J.Obj(d, "inputAudioTracks"); routeActual = TrackText(tr); if (ModeFor(tr) != Settings.Str("music.routing.mode")) { routeStatus = "tracks were changed in OBS by hand: now " + routeActual; routeOk = false; Log.Info("obs", "music tracks changed in OBS by hand: " + routeActual); } Changed(); }
        else { var t = ApplyRoute(Settings.Str("music.routing.mode")); } } }
    public static async Task<Dictionary<string, object>> ApplyRoute(string mode) {
      if (!Obs.Link.Ready) { routeOk = false; routeStatus = "saved - applies when OBS connects"; Changed(); return J.D("ok", false, "pending", true, "mode", mode, "error", routeStatus); }
      // right after OBS opens its WebSocket it can still be loading the scene collection ("not ready"): retry briefly
      for (int attempt = 1; ; attempt++) { var r = await RouteOnce(mode); var e = J.Str(r, "error", "");
        if (!J.Bool(r, "ok", false) && e.IndexOf("not ready", StringComparison.OrdinalIgnoreCase) >= 0 && attempt < 10) { await Task.Delay(500); continue; } return r; } }
    static async Task<Dictionary<string, object>> RouteOnce(string mode) {
      try {
        var want = new Dictionary<string, object>(); var managed = Plats.SelectMany(Tracks).Distinct().ToList();
        var mine = mode == "all" ? managed : mode == "off" ? new List<int>() : Tracks(mode);
        foreach (var n in managed) want[n.ToString()] = mine.Contains(n);   // tracks no platform uses (recording etc.) are left alone
        await Obs.Link.Call("SetInputAudioTracks", J.D("inputName", InputName(), "inputAudioTracks", want));
        var got = J.Obj(await Obs.Link.Call("GetInputAudioTracks", J.D("inputName", InputName())), "inputAudioTracks");   // verify what OBS really has now
        routeActual = TrackText(got); routeOk = managed.All(n => J.Bool(got, n.ToString(), false) == mine.Contains(n));
        routeStatus = routeOk ? "applied and checked in OBS: " + routeActual : "OBS did not take the change: " + routeActual;
        Log.Info("obs", "music destination " + mode.ToUpperInvariant() + ": " + routeStatus); Changed();
        return J.D("ok", routeOk, "mode", mode, "tracks", routeActual, "status", routeStatus); }
      catch (Exception e) {
        routeOk = false; var msg = U.Plain(e);
        routeStatus = msg.Contains("No source was found") || msg.Contains("not found") ? "OBS has no source called \"" + InputName() + "\" - run System check > Repair OBS" : "OBS: " + msg;
        Log.Warn("obs", "music routing: " + routeStatus); Changed(); return J.D("ok", false, "mode", mode, "error", routeStatus); } }
    static Dictionary<string, object> RouteInfo() {
      return J.D("mode", Settings.Str("music.routing.mode"), "ok", routeOk && Obs.Link.Ready, "status", Obs.Link.Ready ? routeStatus : "applies when OBS is open", "tracks", routeActual,
        "obs", Obs.Link.Ready ? "connected" : Obs.Link.State, "input", InputName(), "map", J.D("twitch", Tracks("twitch"), "kick", Tracks("kick"), "youtube", Tracks("youtube"))); }

    // ---------- YouTube search / related (public pages; optional official API key for search) ----------
    static readonly Dictionary<string, KeyValuePair<DateTime, object>> cache = new Dictionary<string, KeyValuePair<DateTime, object>>();
    // only successful answers are cached: a short internet hiccup must not block a song for minutes
    static object Cached(string key, int sec, Func<object> f, Func<object, bool> keep) {
      lock (cache) { KeyValuePair<DateTime, object> v; if (cache.TryGetValue(key, out v) && (DateTime.Now - v.Key).TotalSeconds < sec) return v.Value; }
      var r = f(); if (keep == null || keep(r)) lock (cache) { cache[key] = new KeyValuePair<DateTime, object>(DateTime.Now, r); if (cache.Count > 400) cache.Clear(); } return r; }
    static string Un(string s) { try { return Regex.Unescape(s); } catch { return s; } }
    static string YtBase { get { return Ep.Get("youtube_web", "https://www.youtube.com"); } }
    static string GetPage(string url) { var r = Http.Request("GET", url, null, null, new Dictionary<string, string> { { "Cookie", "SOCS=CAI; CONSENT=YES+1" } }, 15000); if (!r.Ok) throw new Exception(r.Error ?? ("HTTP " + r.Code)); return r.Body ?? ""; }
    public static List<Dictionary<string, object>> Search(string q) {
      if (string.IsNullOrWhiteSpace(q)) return new List<Dictionary<string, object>>();
      var local = Local.Search(q, 5);
      List<Dictionary<string, object>> yt;
      try { yt = YtSearch(q); } catch (Exception e) { if (local.Count > 0) { Log.Warn("music", "YouTube search failed (" + U.Plain(e) + ") - showing your own songs only"); return local; } throw; }
      return local.Concat(yt).ToList(); }
    static List<Dictionary<string, object>> YtSearch(string q) {
      return (List<Dictionary<string, object>>)Cached("s:" + q.ToLowerInvariant(), 300, () => { Searches++;
        var key = Settings.Str("music.youtubeApiKey"); if (key.Length > 0) { try { return ApiSearch(q, key); } catch (Exception e) { Log.Warn("music", "YouTube Data API search failed, using the public page: " + e.Message); } }
        var h = GetPage(YtBase + "/results?search_query=" + Uri.EscapeDataString(q) + "&sp=EgIQAQ%253D%253D");
        var outp = new List<Dictionary<string, object>>(); var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(h, "\"videoRenderer\":\\{\"videoId\":\"([\\w-]{11})\"(.{0,4000}?)\"title\":\\{\"runs\":\\[\\{\"text\":\"((?:[^\"\\\\]|\\\\.)*)\"")) {
          var id = m.Groups[1].Value; if (!seen.Add(id)) continue; var rest = h.Substring(m.Index, Math.Min(6000, h.Length - m.Index));
          var len = Regex.Match(rest, "\"lengthText\":\\{\"accessibility\":\\{\"accessibilityData\":\\{\"label\":\"[^\"]*\"\\}\\},\"simpleText\":\"([^\"]+)\"").Groups[1].Value;
          var ch = Regex.Match(rest, "\"ownerText\":\\{\"runs\":\\[\\{\"text\":\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value;
          bool live = Regex.IsMatch(rest.Substring(0, Math.Min(rest.Length, 3000)), "BADGE_STYLE_TYPE_LIVE_NOW|\"isLive\":true");
          if (live) continue;
          outp.Add(J.D("kind", "yt", "id", id, "title", Un(m.Groups[3].Value), "length", len, "channel", Un(ch))); if (outp.Count >= 15) break; }
        return outp; }, r => ((List<Dictionary<string, object>>)r).Count > 0); }
    static List<Dictionary<string, object>> ApiSearch(string q, string key) {
      var r = Http.Get("https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&videoEmbeddable=true&maxResults=15&q=" + Uri.EscapeDataString(q) + "&key=" + Uri.EscapeDataString(key), 15000);
      if (!r.Ok) throw new Exception(r.Error ?? ("HTTP " + r.Code)); var d = J.Parse(r.Body);
      var outp = new List<Dictionary<string, object>>(); var items = J.Get(d, "items") as ArrayList; if (items == null) return outp;
      foreach (var o in items) { var it = o as Dictionary<string, object>; var id = J.Str(it, "id.videoId", null); if (id == null) continue; outp.Add(J.D("kind", "yt", "id", id, "title", WebUtility.HtmlDecode(J.Str(it, "snippet.title", "")), "length", "", "channel", J.Str(it, "snippet.channelTitle", ""))); }
      return outp; }
    public static List<Dictionary<string, object>> Related(string id, string exclude) {
      if (!Regex.IsMatch(id ?? "", "^[\\w-]{11}$")) return new List<Dictionary<string, object>>();
      var all = (List<Dictionary<string, object>>)Cached("r:" + id, 900, () => {
        var h = GetPage(YtBase + "/watch?v=" + id); var outp = new List<Dictionary<string, object>>(); int i = h.IndexOf("\"secondaryResults\""); if (i < 0) return outp;
        var sec = h.Substring(i, Math.Min(600000, h.Length - i)); var seen = new HashSet<string> { id };
        foreach (var chunk in sec.Split(new[] { "\"lockupViewModel\":{" }, StringSplitOptions.None).Skip(1)) {
          var cid = Regex.Match(chunk, "\"contentId\":\"([^\"]+)\"").Groups[1].Value; var ctype = Regex.Match(chunk, "\"contentType\":\"([^\"]+)\"").Groups[1].Value;
          if (cid.Length != 11 || !ctype.Contains("VIDEO") || !seen.Add(cid)) continue;
          var title = Regex.Match(chunk, "\"title\":\\{\"content\":\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value; if (title.Length == 0) continue;
          var len = Regex.Match(chunk.Substring(0, Math.Min(4000, chunk.Length)), "\"text\":\"(\\d{1,2}:\\d{2}(?::\\d{2})?)\"").Groups[1].Value; if (len.Length == 0) continue;
          int s2 = Secs(len); if (s2 > 720 || s2 < 60) continue;
          var tt = Un(title); if (Regex.IsMatch(tt, "gameplay|trailer|review|reaction|tutorial|podcast|\\bnews\\b|vlog|highlights|unboxing|#shorts|walkthrough", RegexOptions.IgnoreCase)) continue;
          int score = Regex.IsMatch(tt, "music|song|lyric|official|audio|\\bncs\\b|feat|remix|lofi|\\bft\\.|\\bmix\\b|beats|\\(.*\\)| - ", RegexOptions.IgnoreCase) ? 1 : 0;
          outp.Add(J.D("id", cid, "title", tt, "length", len, "s", score)); if (outp.Count >= 12) break; }
        return outp; }, r => ((List<Dictionary<string, object>>)r).Count > 0);
      var skip = new HashSet<string>((exclude ?? "").Split(','));
      return all.Where(x => !skip.Contains((string)x["id"])).OrderByDescending(x => (int)x["s"]).Take(8).Select(x => J.D("id", x["id"], "title", x["title"], "length", x["length"])).ToList(); }
    public static int Secs(string len) { int s = 0; foreach (var p in (len ?? "").Split(':')) { int n; if (!int.TryParse(p, out n)) return 0; s = s * 60 + n; } return s; }
    // video details from the watch page: length, live, playable (used for song request limits)
    public static Dictionary<string, object> VideoInfo(string id) {
      return (Dictionary<string, object>)Cached("v:" + id, 1800, () => {
        try { var h = GetPage(YtBase + "/watch?v=" + id);
          var len = Regex.Match(h, "\"lengthSeconds\":\"(\\d+)\""); var live = Regex.IsMatch(h, "\"isLiveContent\":true") && Regex.IsMatch(h, "\"isLive(Now)?\":true");
          var status = Regex.Match(h, "\"playabilityStatus\":\\{\"status\":\"(\\w+)\""); var embed = !Regex.IsMatch(h, "\"playableInEmbed\":false");
          var author = Regex.Match(h, "\"author\":\"((?:[^\"\\\\]|\\\\.)*)\"");
          return J.D("ok", true, "durationSec", len.Success ? int.Parse(len.Groups[1].Value) : 0, "live", live, "status", status.Success ? status.Groups[1].Value : "", "embeddable", embed, "author", author.Success ? Un(author.Groups[1].Value) : ""); }
        catch (Exception e) { return J.D("ok", false, "error", e.Message); } }, r => J.Bool((Dictionary<string, object>)r, "ok", false)); }

    // ---------- links: YouTube video / playlist, Spotify track / album / playlist, or a song name ----------
    public static Dictionary<string, object> Resolve(string input) {
      Resolves++; var s = (input ?? "").Trim(); if (s.Length == 0) return Fail("empty", "Paste a link or type a song name.");
      if (s.StartsWith("local:")) { var li = Local.Get(s.Substring(6)); return li == null ? Fail("notfound", "That file isn't in your music folders any more.") : Ok("local", li, J.Str(li, "title", ""), "from your music folder"); }
      Match m;
      if ((m = Regex.Match(s, "^(?:https?://)?(?:open|play)\\.spotify\\.com/(?:intl-[a-z]{2}(?:-[a-z]{2})?/)?(?:embed/)?(track|album|playlist|artist|episode|show)/([A-Za-z0-9]{22})", RegexOptions.IgnoreCase)).Success ||
          (m = Regex.Match(s, "^spotify:(track|album|playlist|artist|episode|show):([A-Za-z0-9]{22})$", RegexOptions.IgnoreCase)).Success)
        return Spotify.Resolve(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value);
      if (Regex.IsMatch(s, "^(https?://)?(spotify\\.link|spoti\\.fi)/", RegexOptions.IgnoreCase)) return Fail("unsupported", "Short Spotify links (spotify.link) can't be read. In Spotify use Share > Copy link to song / playlist.");
      bool yt = Regex.IsMatch(s, "^(https?://)?([\\w-]+\\.)?(youtube\\.com|youtu\\.be|youtube-nocookie\\.com)/", RegexOptions.IgnoreCase);
      if (!yt && Regex.IsMatch(s, "^[\\w-]{11}$") && Regex.IsMatch(s, "[0-9_-]|[A-Z].*[a-z]|[a-z].*[A-Z]")) { s = "https://youtu.be/" + s; yt = true; }
      if (yt) {
        var list = Regex.Match(s, "[?&]list=([\\w-]+)").Groups[1].Value; var id = Regex.Match(s, "(?:[?&]v=|youtu\\.be/|/shorts/|/embed/|/live/|/v/)([\\w-]{11})").Groups[1].Value;
        if (list.Length > 0 && Regex.IsMatch(list, "^(RD|UL|LL$|WL$)")) { if (id.Length == 0) return Fail("unsupported", "YouTube Mix / Liked / Watch Later lists are personal to your account and can't be played in OBS. Paste a normal playlist or a video link."); list = ""; }
        if (list.Length > 0) { var r = OEmbed(YtBase + "/playlist?list=" + list); if (!(bool)r["ok"]) return Fail(Convert.ToString(r["code"]), "This playlist can't be used: " + r["why"]);
          var res = Ok("youtube-playlist", J.D("kind", "ytlist", "list", list, "title", r["title"]), (string)r["title"], "playlist by " + r["author"]); res["duplicate"] = InQueue(null, list); return res; }
        if (id.Length == 0) return Fail("invalid", "That YouTube link has no video in it. Open the video and copy its link again.");
        var v = OEmbed(YtBase + "/watch?v=" + id); if (!(bool)v["ok"]) return Fail(Convert.ToString(v["code"]), "This video can't be used: " + v["why"]);
        var rv = Ok("youtube-video", J.D("kind", "yt", "id", id, "title", v["title"], "artist", ""), (string)v["title"], (string)v["author"]); rv["duplicate"] = InQueue(id, null); rv["author"] = v["author"]; return rv; }
      if (Regex.IsMatch(s, "^(https?://|www\\.)", RegexOptions.IgnoreCase)) return Fail("unsupported", "Only YouTube and Spotify links are supported (for other sites, search the song name).");
      var sr = Search(s); if (sr.Count == 0) return Fail("notfound", "No results for \"" + s + "\".");
      var best = sr[0]; var item = J.Clone(best); item.Remove("channel"); item.Remove("length"); if (best.ContainsKey("length")) item["durationSec"] = Secs((string)best["length"]);
      var ok = Ok(J.Str(best, "kind", "yt") == "local" ? "local" : "search", item, (string)best["title"], J.Str(best, "kind", "") == "local" ? "from your music folder" : "first search result");
      ok["duplicate"] = J.Str(best, "kind", "") == "yt" && InQueue((string)best["id"], null); ok["author"] = J.Str(best, "channel", ""); return ok; }
    static bool InQueue(string id, string list) { lock (L) return queue.Any(q => (list != null && J.Str(q, "list", null) == list) || (list == null && id != null && J.Str(q, "id", null) == id && J.Str(q, "list", null) == null)); }
    public static Dictionary<string, object> Ok(string kind, Dictionary<string, object> item, string title, string sub) { return J.D("ok", true, "kind", kind, "item", item, "title", title, "subtitle", sub); }
    public static Dictionary<string, object> Fail(string code, string msg) { return J.D("ok", false, "code", code, "error", msg); }
    static Dictionary<string, object> OEmbed(string url) {
      return (Dictionary<string, object>)Cached("o:" + url, 600, () => {
        var r = Http.Get(Ep.Get("youtube_web", "https://www.youtube.com") + "/oembed?format=json&url=" + Uri.EscapeDataString(url), 10000);
        if (r.Ok) { var d = J.Parse(r.Body); return J.D("ok", true, "title", J.Str(d, "title", "YouTube"), "author", J.Str(d, "author_name", "")); }
        int c = r.Code; string why = c == 401 || c == 403 ? "it is private, or its owner doesn't allow playing it outside YouTube" : c == 404 ? "it doesn't exist, was deleted, or is private" : c == 400 ? "it doesn't exist (check the link)" : c == 0 ? "couldn't reach YouTube (" + r.Error + ")" : "YouTube answered " + c;
        return J.D("ok", false, "code", c == 0 ? "network" : c == 404 ? "notfound" : c == 400 ? "invalid" : "private", "why", why); }, r => J.Str((Dictionary<string, object>)r, "code", "") != "network"); }
    // best YouTube match: a length close to Spotify's, and "official audio / topic" uploads
    public static Dictionary<string, object> MatchYouTube(string q, int durMs) {
      List<Dictionary<string, object>> r; try { r = Search(q).Where(x => J.Str(x, "kind", "yt") == "yt").ToList(); } catch { return null; } if (r.Count == 0) return null; if (durMs <= 0) return r[0];
      return r.Take(6).OrderBy(x => { int s = Secs((string)x["length"]); return (s <= 0 ? 999 : Math.Abs(s - durMs / 1000)) - (Regex.IsMatch((string)x["title"], "official audio|audio|topic", RegexOptions.IgnoreCase) ? 3 : 0); }).First(); }

    // ---------- API / WebSocket ----------
    public static Dictionary<string, object> DiagInfo() {
      lock (L) return J.D("playerConnected", Hub.Count("player") > 0, "playing", playing, "title", Current() == null ? "" : Describe(Current()), "queue", queue.Count, "note", note,
        "destination", Settings.Str("music.routing.mode"), "routing", Obs.Link.Ready ? routeStatus : "applies when OBS is open", "routingOk", routeOk && Obs.Link.Ready, "tracks", routeActual,
        "ducked", ducked, "searches", Searches, "linkChecks", Resolves, "localFiles", Local.Count, "lastPlayerMessage", lastPlayerMsg == DateTime.MinValue ? "never" : lastPlayerMsg.ToString("HH:mm:ss")); }
    public static Dictionary<string, object> Add(string url, string mode, bool force, string by, string platform) {
      if (mode != "add" && mode != "playnow" && mode != "playnext") mode = "add";
      var r = Resolve(url); if (!(bool)r["ok"]) return r;
      if (r.ContainsKey("duplicate") && (bool)r["duplicate"] && !force && mode == "add") { r["ok"] = false; r["code"] = "duplicate"; r["error"] = "\"" + r["title"] + "\" is already in the queue."; return r; }
      if (r.ContainsKey("items")) { var items = (List<Dictionary<string, object>>)r["items"]; int first = -1; foreach (var it in items) { if (by != null) it["by"] = by; var at = AddItem(it, mode == "playnow" && first < 0 ? "playnow" : mode == "playnow" ? "playnext" : mode); if (first < 0) first = at; } r["position"] = first; }
      else { var it = (Dictionary<string, object>)r["item"]; if (by != null) { it["by"] = by; it["platform"] = platform; } r["position"] = AddItem(it, mode); }
      return r; }
    public static bool Api(Ctx ctx, string path, string m, System.Collections.Specialized.NameValueCollection q) {
      if (path.StartsWith("/api/music/local/")) { Local.Serve(ctx, path.Substring("/api/music/local/".Length)); return true; }
      switch (path) {
        case "/api/music/state": case "/api/state": Http.Json(ctx, J.Obj(State(), "s")); return true;
        case "/api/music/cmd": case "/api/cmd": if (m != "POST") return false; { var b = Http.BodyJson(ctx); var err = Command(J.Str(b, "cmd", ""), b); Http.Json(ctx, err == null ? 200 : 400, J.D("ok", err == null, "error", err)); return true; }
        case "/api/search": case "/api/music/search": try { Http.Json(ctx, Search(q["q"])); } catch (Exception e) { Http.Json(ctx, 502, J.D("error", "Search failed: " + U.Plain(e))); } return true;
        case "/api/related": try { Http.Json(ctx, Related(q["id"], q["exclude"])); } catch (Exception e) { Http.Json(ctx, 502, J.D("error", U.Plain(e))); } return true;
        case "/api/music/resolve": Http.Json(ctx, Resolve(q["url"])); return true;
        case "/api/music/add": if (m != "POST") return false; { var b = Http.BodyJson(ctx); Http.Json(ctx, Add(J.Str(b, "url", ""), J.Str(b, "mode", "add"), J.Bool(b, "force", false), null, null)); return true; }
        case "/api/music/route": if (m == "POST") { var err = Settings.Set("music.routing.mode", J.Str(Http.BodyJson(ctx), "mode", "all")); if (err != null) { Http.Json(ctx, 400, J.D("ok", false, "error", err)); return true; } Http.Json(ctx, ApplyRoute(Settings.Str("music.routing.mode")).Result); } else Http.Json(ctx, RouteInfo()); return true;
        case "/api/music/spotify-secret": if (m != "POST") return false; { var sec = J.Str(Http.BodyJson(ctx), "secret", "").Trim(); if (sec.Length > 0 && !Regex.IsMatch(sec, "^[A-Za-z0-9]{20,64}$")) { Http.Json(ctx, 400, J.D("ok", false, "error", "That doesn't look like a Spotify Client Secret")); return true; } Secrets.Set("spotify.clientSecret", sec.Length == 0 ? null : sec); Http.Json(ctx, J.D("ok", true)); return true; }
        case "/api/music/folders": if (m == "POST") { var b = Http.BodyJson(ctx); Http.Json(ctx, Local.AddFolder(J.Str(b, "path", null))); } else Http.Json(ctx, Local.Folders()); return true;
        case "/api/obs/call": {   // tests only (--test)
          if (!Program.TestMode || m != "POST") { Http.Json(ctx, 403, J.D("error", "only in test mode")); return true; }
          var b = Http.BodyJson(ctx); var type = J.Str(b, "type", ""); if (!Regex.IsMatch(type, "^(Get\\w+|SetInputAudioTracks)$")) { Http.Json(ctx, 403, J.D("error", "not allowed")); return true; }
          try { Http.Json(ctx, J.D("ok", true, "d", Obs.Link.Call(type, J.Obj(b, "data")).Result)); } catch (Exception e) { Http.Json(ctx, J.D("ok", false, "error", U.Plain(e))); } return true; }
      }
      return false; }
    public static void OnWs(Client c, string type, Dictionary<string, object> msg) {
      switch (type) {
        case "hello": if (c.Topics.Contains("music.state")) Hub.Send(c, State()); if (c.Role == "player") { lock (c.Topics) c.Topics.Add("music.player"); Log.Info("music", "player source connected"); } break;
        case "_closed": if (c.Role == "player") { Log.Info("music", "player source disconnected"); Changed(); } break;
        case "music.ev": if (c.Role == "player") PlayerEvent(c, msg); break;
        case "music.cmd": { var cm = J.Obj(msg, "c") ?? msg; var err = Command(J.Str(cm, "cmd", ""), cm); if (err != null) Hub.Send(c, J.D("type", "error", "error", err, "reqId", J.Str(msg, "reqId", ""))); break; }
        case "music.add": { var id = J.Str(msg, "reqId", ""); var url = J.Str(msg, "url", ""); var mode = J.Str(msg, "mode", "add"); bool force = J.Bool(msg, "force", false);
          Task.Run(() => { Dictionary<string, object> r; try { r = Add(url, mode, force, null, null); } catch (Exception e) { r = Fail("error", U.Plain(e)); } var d = new Dictionary<string, object>(r); d["type"] = "music.added"; d["reqId"] = id; d.Remove("items"); Hub.Send(c, d); }); break; }
        case "music.search": { var id = J.Str(msg, "reqId", ""); var qq = J.Str(msg, "q", "");
          Task.Run(() => { object res; try { res = J.D("type", "music.results", "reqId", id, "results", Search(qq)); } catch (Exception e) { res = J.D("type", "music.results", "reqId", id, "results", new object[0], "error", "Search failed: " + U.Plain(e)); } Hub.Send(c, res); }); break; }
        case "music.route": { var mode = J.Str(msg, "mode", "all"); var id = J.Str(msg, "reqId", ""); var err = Settings.Set("music.routing.mode", mode);
          if (err != null) { Hub.Send(c, J.D("type", "music.routed", "reqId", id, "result", J.D("ok", false, "error", err))); break; }
          ApplyRoute(mode).ContinueWith(t => Hub.Send(c, J.D("type", "music.routed", "reqId", id, "result", t.IsFaulted ? J.D("ok", false, "error", U.Plain(t.Exception)) : t.Result))); break; }
        case "music.log": Log.Debug("music", "player: " + J.Str(msg, "ev", "") + " " + U.Trunc(J.Str(msg, "info", ""), 200)); break;
      } }
  }

  // ---------- Spotify links -> the same song on YouTube ----------
  public static class Spotify {
    static string tok; static DateTime until;
    static string Token() {
      var id = Settings.Str("music.spotify.clientId"); var secret = Secrets.Str("spotify.clientSecret") ?? ""; if (id.Length == 0 || secret.Length == 0) return null;
      if (tok != null && DateTime.Now < until) return tok;
      var r = Http.Request("POST", "https://accounts.spotify.com/api/token", "grant_type=client_credentials", "application/x-www-form-urlencoded", new Dictionary<string, string> { { "Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(id + ":" + secret)) } }, 10000);
      if (r.Code == 400 || r.Code == 401) throw new Exception("Spotify rejected your app keys"); if (!r.Ok) throw new Exception("couldn't reach Spotify (" + (r.Error ?? r.Code.ToString()) + ")");
      var d = J.Parse(r.Body); tok = J.Str(d, "access_token", null); until = DateTime.Now.AddSeconds(J.Int(d, "expires_in", 3600) - 120); return tok; }
    static Dictionary<string, object> Api(string path, string t) { var r = Http.Request("GET", "https://api.spotify.com/v1/" + path, null, null, new Dictionary<string, string> { { "Authorization", "Bearer " + t } }, 12000); if (!r.Ok) { var e = new WebExceptionLike(r.Code, r.Error ?? ("HTTP " + r.Code)); throw e; } return J.Parse(r.Body); }
    class WebExceptionLike : Exception { public int Code; public WebExceptionLike(int c, string m) : base(m) { Code = c; } }
    public static Dictionary<string, object> Resolve(string kind, string id) {
      var url = "https://open.spotify.com/" + kind + "/" + id;
      if (kind == "artist" || kind == "episode" || kind == "show") return Music.Fail("unsupported", "Spotify " + kind + " links aren't supported. Use a song, album or playlist link.");
      string title = null; var o = Http.Get("https://open.spotify.com/oembed?url=" + Uri.EscapeDataString(url), 10000);
      if (o.Ok) title = J.Str(J.Parse(o.Body), "title", null); else if (o.Code == 404 || o.Code == 400) return Music.Fail("notfound", "Spotify doesn't know this " + kind + " (deleted, private, or not available in your country)."); else if (o.Code == 0) return Music.Fail("network", "Couldn't reach Spotify: " + o.Error);
      string t = null, tErr = null; try { t = Token(); } catch (Exception e) { tErr = e.Message; }
      if (kind == "track") {
        string artist = ""; int durMs = 0;
        if (t != null) { try { var tr = Api("tracks/" + id, t); title = J.Str(tr, "name", title); var ar = J.Get(tr, "artists") as ArrayList; if (ar != null && ar.Count > 0) artist = J.Str((Dictionary<string, object>)ar[0], "name", ""); durMs = J.Int(tr, "duration_ms", 0); } catch (Exception e) { tErr = "Spotify API: " + e.Message; } }
        if (string.IsNullOrEmpty(title)) return Music.Fail("notfound", "Couldn't read this Spotify song.");
        var m = Music.MatchYouTube((artist.Length > 0 ? artist + " - " : "") + title + " audio", durMs); if (m == null) return Music.Fail("notfound", "Couldn't find \"" + title + "\" on YouTube.");
        var res = Music.Ok("spotify-track", J.D("kind", "yt", "id", m["id"], "title", title, "artist", artist, "from", "spotify", "durationSec", durMs / 1000), (artist.Length > 0 ? artist + " - " : "") + title, "Spotify song, playing the YouTube match: " + m["title"]);
        if (artist.Length == 0) res["note"] = "Matched by song title only" + (tErr != null ? " (" + tErr + ")" : " - add Spotify app keys in Settings > Music for exact matches") + ", so it may pick a different version.";
        res["author"] = artist; return res; }
      if (t == null) return Music.Fail("needs-spotify-app", "\"" + (title ?? "This " + kind) + "\": Spotify " + kind + "s need your own free Spotify app keys (Settings > Music > Spotify)." + (tErr != null ? " " + tErr : ""));
      try {
        var tracks = new List<Dictionary<string, object>>(); string next = kind == "playlist" ? "playlists/" + id + "/tracks?limit=100&fields=items(track(name,duration_ms,is_local,artists(name))),next" : "albums/" + id + "/tracks?limit=50";
        int pages = 0; while (next != null && pages++ < 3) { var d = Api(next, t); var items = J.Get(d, "items") as ArrayList;
          if (items != null) foreach (var o2 in items) { var it = o2 as Dictionary<string, object>; var tr = kind == "playlist" ? J.Obj(it, "track") : it; if (tr == null || J.Bool(tr, "is_local", false)) continue;
            var ar = J.Get(tr, "artists") as ArrayList; var artist = ar != null && ar.Count > 0 ? J.Str((Dictionary<string, object>)ar[0], "name", "") : "";
            var nm = J.Str(tr, "name", ""); if (nm.Length == 0) continue; tracks.Add(J.D("kind", "q", "q", (artist.Length > 0 ? artist + " - " : "") + nm + " audio", "title", nm, "artist", artist, "ms", J.Int(tr, "duration_ms", 0), "from", "spotify")); }
          var nx = J.Str(d, "next", null); next = nx == null ? null : nx.Replace("https://api.spotify.com/v1/", ""); }
        if (tracks.Count == 0) return Music.Fail("empty", "This Spotify " + kind + " has no playable songs.");
        var res = J.D("ok", true, "kind", "spotify-" + kind, "items", tracks.Take(150).ToList(), "title", title ?? "Spotify " + kind, "subtitle", tracks.Count + " songs - each is matched on YouTube when its turn comes");
        if (tracks.Count > 150) res["note"] = "Only the first 150 songs were added."; return res; }
      catch (WebExceptionLike e) { return Music.Fail(e.Code == 404 ? "private" : "error", e.Code == 404 ? "Spotify won't share this " + kind + ". Private playlists and Spotify's own playlists (Discover Weekly, Top 50...) aren't available to apps. Copy the songs to a public playlist of your own." : "Spotify error: " + e.Message); } }
  }
}
