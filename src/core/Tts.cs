// IXC Core - chat text-to-speech. IXC itself decides WHAT to read (not a web page), so it works whenever IXC runs.
// Audio plays in the OBS source /chat/tts.html, so viewers hear it on the tracks you pick, and music is lowered meanwhile.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
//   chat -> filters (off / platform / role / own account / bots / never-speak / blocked words / commands / rate limits) -> cleanup -> queue
//   queue -> (drop stale) -> synthesize (Edge neural voice, or the offline Windows voice) -> tts.html plays it -> next
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public class TtsItem {
    public bool IsSound;   // a sound effect / alert sound (a file), not speech
    public int N, Priority, Rate, Pitch, Volume = 100; public string Text, Said, User, Login, Platform, MsgId, VoiceKey, Status = "queued", Engine = "", Error = ""; public DateTime At = DateTime.Now, PlayedAt;
    public byte[] Audio; public string Mime = "audio/mpeg"; public Task Synth; public int SynthMs;
    public volatile bool Cancelled; public volatile Action Abort;   // skip / clear stops a voice request that is still running
    public void Cancel() { Cancelled = true; var a = Abort; if (a != null) a(); }
    public Dictionary<string, object> View() { return J.D("n", N, "text", Said, "user", User, "platform", Platform, "voice", VoiceKey, "status", Status, "engine", Engine, "priority", Priority,
      "at", At.ToString("HH:mm:ss"), "age", (int)(DateTime.Now - At).TotalSeconds, "error", Error, "replayable", true); }
  }
  public class Voice { public string Key, Label, Name, Engine = "edge", Style, Locale, Gender, Kind = ""; public int Rate, Pitch; public bool IsNew; }

  public static class Tts {
    static readonly object L = new object();
    static readonly List<TtsItem> Queue = new List<TtsItem>(); static readonly LinkedList<TtsItem> History = new LinkedList<TtsItem>();
    static readonly LinkedList<Dictionary<string, object>> Skipped = new LinkedList<Dictionary<string, object>>();
    static TtsItem current; static int nextN; static DateTime currentUntil; static Timer watchdog, stateT, unduckT;
    public static int Played, Failed, Enqueued; static long synthMsTotal; static int synthCount;
    public static readonly Dictionary<string, int> FilterCounts = new Dictionary<string, int>();
    static readonly LinkedList<DateTime> recentTimes = new LinkedList<DateTime>(); static readonly Dictionary<string, DateTime> lastByUser = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    static readonly LinkedList<KeyValuePair<string, DateTime>> recentTexts = new LinkedList<KeyValuePair<string, DateTime>>();
    static string edgeError = ""; static int edgeFails; static DateTime edgeOffUntil = DateTime.MinValue; static string lastEngine = ""; static bool pausedPlayback;

    static readonly string[] DefaultBots = { "streamerbot", "streamer.bot", "streamer_bot", "nightbot", "streamelements", "moobot", "fossabot", "wizebot", "botrix", "botrixoficial", "kickbot", "sery_bot", "soundalerts", "streamlabs", "commanderroot", "ixc" };
    public static bool On { get { return Settings.Bool("tts.on"); } }
    public static bool Paused { get { return Settings.Bool("tts.paused"); } }
    static string S(string k) { return Settings.Str("tts." + k); }
    static int I(string k) { return Settings.Int("tts." + k); }
    static bool B(string k) { return Settings.Bool("tts." + k); }
    public static Dictionary<string, object> SettingsView() {
      var d = new Dictionary<string, object>(); foreach (var kv in Settings.Snapshot(true)) if (kv.Key.StartsWith("tts.")) d[kv.Key.Substring(4)] = kv.Value;
      d["enabled"] = On; d["azureReady"] = AzureTts.Ready; d["azureKeySet"] = Secrets.Has("azure.speechKey"); d["azure"] = AzureTts.View(); d["offline"] = OfflineView(); return d; }
    public static Dictionary<string, object> OfflineView() {
      bool hi = Sapi.Has("hi"), inEn = Sapi.Has("en-IN"); var all = Sapi.List();
      return J.D("voices", all, "hindi", hi, "indianEnglish", inEn, "on", B("fallback") && S("engine") != "azure",
        "message", all.Count == 0 ? "No Windows voices found - there is no offline backup" : (hi ? "" : "No Hindi Windows voice (install the Hindi speech pack). ") + (inEn ? "" : "No English (India) Windows voice (install the English (India) speech pack). ") + (hi && inEn ? "Hindi and Indian English Windows voices installed." : "")); }

    // ---------- voices ----------
    static Dictionary<string, Voice> voiceCache; static DateTime voiceAt; static string sapiBest; static DateTime sapiAt = DateTime.MinValue;
    public static Dictionary<string, Voice> Voices() {
      if (voiceCache != null && (DateTime.Now - voiceAt).TotalSeconds < 10) return voiceCache;
      var v = new Dictionary<string, Voice>();
      Action<string, string, string, int, int, bool> add = (k, label, name, rate, pitch, isNew) => v[k] = new Voice { Key = k, Label = label, Name = name, Rate = rate, Pitch = pitch, IsNew = isNew };
      add("in-male", "Indian male - Prabhat", "en-IN-PrabhatNeural", 0, 0, false);
      add("in-female", "Indian female - Neerja", "en-IN-NeerjaNeural", 0, 0, false);
      add("in-male-2", "Indian male 2 - Madhur (faster)", "hi-IN-MadhurNeural", 10, 0, false);
      add("in-female-2", "Indian female 2 - Neerja Expressive (faster)", "en-IN-NeerjaExpressiveNeural", 8, 0, false);
      add("us-male", "US male - Andrew", "en-US-AndrewNeural", 0, 0, false);
      add("us-male-2", "US male 2 - Guy", "en-US-GuyNeural", 0, 0, true);
      add("us-female", "US female - Jenny", "en-US-JennyNeural", 0, 0, false);
      add("us-female-2", "US female 2 - Aria", "en-US-AriaNeural", 0, 0, true);
      add("uk-female", "UK female - Sonia", "en-GB-SoniaNeural", 0, 0, true);
      add("jarvis", "Jarvis-style - Ryan (UK)", "en-GB-RyanNeural", 6, -3, false);
      add("au-male", "Australian male - William", "en-AU-WilliamNeural", 0, 0, true);
      foreach (var av in AzureTts.MenuVoices()) v[av.Key] = av;   // official Azure voices: only after the user added a key, and only the ones Azure itself lists
      AzureTts.MaybeRefresh();
      var cv = Settings.Obj("tts.voices");
      foreach (var kv in cv) { var d = kv.Value as Dictionary<string, object>; if (d == null) continue; var name = J.Str(d, "voice", ""); if (name.Length == 0 || !Regex.IsMatch(name, "^[A-Za-z]{2,3}-[A-Za-z]{2,4}-\\w+Neural$")) continue;
        Voice old; v.TryGetValue(kv.Key, out old);
        v[kv.Key] = new Voice { Key = kv.Key, Name = name, Label = J.Str(d, "label", old != null && old.Name == name ? old.Label : kv.Key + " - " + name), Rate = ParseNum(J.Str(d, "rate", "0")), Pitch = ParseNum(J.Str(d, "pitch", "0")),
          Engine = J.Str(d, "engine", "edge") == "azure" ? "azure" : "edge", Locale = LocaleOf(name), Style = Regex.IsMatch(J.Str(d, "style", ""), "^[a-z][a-z-]{2,30}$") ? J.Str(d, "style", "") : null }; }
      if ((DateTime.Now - sapiAt).TotalMinutes > 10) { sapiBest = Sapi.Best(null); sapiAt = DateTime.Now; }
      v["local"] = new Voice { Key = "local", Label = "Windows voice (offline, " + (sapiBest ?? "none installed") + ")", Name = "", Engine = "sapi" };
      voiceCache = v; voiceAt = DateTime.Now; return v; }
    static int ParseNum(string s) { var m = Regex.Match(s ?? "", "[-+]?\\d+"); return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : 0; }
    public static List<object> VoiceList() { return Voices().Values.Select(v => (object)J.D("key", v.Key, "label", v.Label, "engine", v.Engine, "isNew", v.IsNew, "locale", v.Locale ?? LocaleOf(v.Name), "kind", v.Kind)).ToList(); }

    public static void Init() {
      AzureTts.Load();
      watchdog = new Timer(_ => Watch(), null, 1000, 1000);
      stateT = new Timer(_ => Hub.Publish("tts", StateMsg()));
      unduckT = new Timer(_ => { bool idle; lock (L) idle = current == null || current.Status != "playing"; if (idle) Music.Duck(false); });
      Settings.Changed += k => {
        if (!k.StartsWith("tts.")) return;
        if (k == "tts.voices" || k == "tts.azure.region" || k == "tts.engine") voiceCache = null;
        if (k == "tts.on" && !On) { lock (L) { foreach (var it in Queue) { it.Cancel(); it.Status = "cancelled (TTS off)"; AddHistory(it); } Queue.Clear(); } StopPlayback("TTS turned off"); }
        if (k == "tts.paused") { if (Paused) { Hub.Publish("tts.play", J.D("type", "tts.pause")); pausedPlayback = true; } else { Hub.Publish("tts.play", J.D("type", "tts.resume")); pausedPlayback = false; lock (L) if (current != null) currentUntil = DateTime.Now.AddSeconds(30); Pump(); } }
        Changed(); }; }
    static void Watch() {
      bool stuck; lock (L) { if (current != null && (Paused || pausedPlayback)) currentUntil = currentUntil.AddSeconds(1); stuck = current != null && DateTime.Now > currentUntil; }
      if (stuck) { Log.Warn("tts", "the TTS source did not report back - moving on"); Finish(null, "played (no confirmation)"); } }
    public static void Changed() { stateT.Change(150, Timeout.Infinite); }

    // ---------- chat -> queue ----------
    static void Skip(ChatMsg c, string reason, string text) {
      lock (FilterCounts) { int n; FilterCounts.TryGetValue(reason, out n); FilterCounts[reason] = n + 1; }
      lock (L) { Skipped.AddLast(J.D("at", DateTime.Now.ToString("HH:mm:ss"), "user", c == null ? "" : c.Name, "platform", c == null ? "" : c.Platform, "reason", reason, "text", text == null ? "" : (text.Length > 80 ? text.Substring(0, 80) + "..." : text))); while (Skipped.Count > 30) Skipped.RemoveFirst(); }
      Changed(); }
    static bool InList(string name, IEnumerable<string> list) { return Filters.InList(name, list); }
    static string Norm(string s) { return Filters.Norm(s); }
    // per-user > role > platform > default
    public static Dictionary<string, object> VoiceFor(ChatMsg c) {
      var users = Settings.Obj("tts.users"); object o;
      foreach (var k in new[] { c.Platform + ":" + Norm(c.Login), "*:" + Norm(c.Login), c.Platform + ":" + Norm(c.Name), "*:" + Norm(c.Name) }) if (users.TryGetValue(k, out o) && o is Dictionary<string, object>) return J.Clone((Dictionary<string, object>)o);
      var roles = Settings.Obj("tts.roleVoices");
      foreach (var r in new[] { c.Broadcaster ? "broadcaster" : null, c.Mod ? "moderator" : null, c.Vip ? "vip" : null, c.Sub ? "subscriber" : null }) if (r != null && roles.TryGetValue(r, out o) && o is Dictionary<string, object>) return J.Clone((Dictionary<string, object>)o);
      var plats = Settings.Obj("tts.platformVoices"); if (plats.TryGetValue(c.Platform, out o) && o is Dictionary<string, object>) return J.Clone((Dictionary<string, object>)o);
      return new Dictionary<string, object>(); }
    public static void OnChat(ChatMsg c) {
      if (!On) { lock (FilterCounts) { int n; FilterCounts.TryGetValue("TTS is off", out n); FilterCounts["TTS is off"] = n + 1; } return; }
      var text = (c.Text ?? "").Trim(); if (text.Length == 0) return;
      if (!Settings.List("tts.platforms").Contains(c.Platform)) { Skip(c, c.Platform + " isn't read out (TTS platforms)", text); return; }
      int need = Array.IndexOf(Settings.Roles, S("minRole")); if (need > 0 && c.Level < need) { Skip(c, "only " + S("minRole") + "s are read", text); return; }
      // same person, same words on several platforms at once (your "ALL" replies, multistream mirrors) = say it once
      var key = Norm(c.Name) + "|" + Norm(text);
      lock (recentTexts) { while (recentTexts.Count > 0 && (DateTime.Now - recentTexts.First.Value.Value).TotalSeconds > 15) recentTexts.RemoveFirst();
        if (recentTexts.Any(x => x.Key == key)) { Skip(c, "mirrored on another platform", text); return; }
        recentTexts.AddLast(new KeyValuePair<string, DateTime>(key, DateTime.Now)); }
      if (B("ignoreOwn") && (c.Broadcaster || c.Me || InList(c.Name, Chat.Own()) || InList(c.Login, Chat.Own()))) { Skip(c, "your own account", text); return; }
      if (B("ignoreBots") && (InList(c.Name, DefaultBots.Concat(Settings.List("tts.botNames"))) || InList(c.Login, Settings.List("tts.botNames")))) { Skip(c, "bot account", text); return; }
      if (InList(c.Name, Settings.List("tts.neverSpeak")) || InList(c.Login, Settings.List("tts.neverSpeak"))) { Skip(c, "never-speak list", text); return; }
      var vc = VoiceFor(c); if (!J.Bool(vc, "enabled", true)) { Skip(c, "voice turned off for this person", text); return; }
      var low = text.ToLowerInvariant(); var bw = Settings.List("tts.blockedWords").FirstOrDefault(w => w.Length > 0 && low.Contains(w.ToLowerInvariant()));
      if (bw != null) { Skip(c, "blocked word", text); return; }
      int prio = 0; var mode = S("readMode");
      var tm = Regex.Match(text, "^!tts\\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
      if (tm.Success) { text = tm.Groups[1].Value; prio = 1; }
      else if (mode == "tts") { Skip(c, "only !tts messages are read", text); return; }
      else if (text.StartsWith("!")) { Skip(c, "chat command", text); return; }
      DateTime last; lock (lastByUser) { if (lastByUser.TryGetValue(c.Key, out last) && (DateTime.Now - last).TotalSeconds < I("userCooldownSec")) { Skip(c, "same person too fast", text); return; } lastByUser[c.Key] = DateTime.Now; if (lastByUser.Count > 2000) lastByUser.Clear(); }
      lock (recentTimes) { while (recentTimes.Count > 0 && (DateTime.Now - recentTimes.First.Value).TotalSeconds > 60) recentTimes.RemoveFirst(); if (recentTimes.Count >= I("perMinute") && prio == 0) { Skip(c, "busy chat (per-minute limit)", text); return; } recentTimes.AddLast(DateTime.Now); }
      var clean = Clean(text, c); if (clean == null) { Skip(c, "nothing readable (only emotes/links/symbols)", text); return; }
      var said = mode == "name" ? SpeakName(c.Name) + " says " + clean : clean;
      var it = new TtsItem { Text = text, Said = said, User = c.Name, Login = c.Login, Platform = c.Platform, MsgId = c.Id, Priority = prio, VoiceKey = J.Str(vc, "voice", null) };
      if (vc.ContainsKey("speed")) it.Rate = J.Int(vc, "speed", 0) - I("speed"); if (vc.ContainsKey("pitch")) it.Pitch = J.Int(vc, "pitch", 0) - I("pitch"); it.Volume = J.Int(vc, "volume", 100);
      Enqueue(it); }
    public static TtsItem Enqueue(TtsItem it) {
      if (string.IsNullOrEmpty(it.VoiceKey) || !Voices().ContainsKey(it.VoiceKey)) it.VoiceKey = S("voice"); if (!Voices().ContainsKey(it.VoiceKey)) it.VoiceKey = "in-male";
      lock (L) {
        it.N = ++nextN; Enqueued++;
        int max = I("queueMax");
        while (Queue.Count >= max) { var drop = Queue.Where(q => q.Priority <= it.Priority).OrderBy(q => q.Priority).ThenBy(q => q.At).FirstOrDefault();
          if (drop == null) { it.Status = "dropped (queue full)"; AddHistory(it); Skip(null, "queue full", it.Said); return it; }
          Queue.Remove(drop); drop.Status = "dropped (queue full)"; AddHistory(drop); lock (FilterCounts) { int n; FilterCounts.TryGetValue("queue full (oldest dropped)", out n); FilterCounts["queue full (oldest dropped)"] = n + 1; } }
        Queue.Add(it); }
      Log.Debug("tts", "queued #" + it.N + " (" + it.Platform + " " + it.User + ")");
      Changed(); Pump(); return it; }
    // a moderator deleted the message / banned the person: don't read it any more
    public static void Forget(string platform, string id, string login) {
      lock (L) { var gone = Queue.Where(q => q.Platform == platform && ((id != null && q.MsgId == id) || (id == null && login != null && string.Equals(q.Login, login, StringComparison.OrdinalIgnoreCase)))).ToList();
        foreach (var g in gone) { Queue.Remove(g); g.Status = "removed (deleted by a moderator)"; AddHistory(g); } if (gone.Count == 0) return; }
      Changed(); }

    // ---------- text cleanup ----------
    static readonly Regex UrlRe = new Regex("(https?://|www\\.)\\S+|\\b[\\w-]+\\.(com|net|org|gg|tv|io|in|ly|me|co|app|xyz|link|live)(/\\S*)?\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static string SpeakName(string n) { n = Regex.Replace(n ?? "someone", "[_\\-.]+", " "); n = Regex.Replace(n, "(?<=\\p{L})\\d{3,}$", ""); n = Regex.Replace(n, "(?<=\\p{Ll})(?=\\p{Lu})", " "); n = n.Trim(); return n.Length == 0 ? "someone" : n; }
    public static string Clean(string t, ChatMsg c) {
      if (c != null && !B("readEmotes") && c.EmoteSpans.Count > 0 && c.Text.Trim() == t) {   // Twitch/Kick emote positions (Unicode code points)
        var cps = new List<string>(); for (int i = 0; i < t.Length; i++) { if (char.IsHighSurrogate(t[i]) && i + 1 < t.Length) { cps.Add(t.Substring(i, 2)); i++; } else cps.Add(t[i].ToString()); }
        foreach (var sp in c.EmoteSpans) { if (sp.Key < 0 || sp.Value >= cps.Count || sp.Value < sp.Key) continue; var word = string.Concat(cps.Skip(sp.Key).Take(sp.Value - sp.Key + 1));
          if (c.EmoteNames.Count > 0 && !c.EmoteNames.Contains(word)) continue;
          for (int i = sp.Key; i <= sp.Value; i++) cps[i] = i == sp.Key ? " " : ""; }
        t = string.Concat(cps); }
      t = Regex.Replace(t, "\\[emote:\\d+:([^\\]]*)\\]", B("readEmotes") ? " $1 " : " ");
      if (c != null && !B("readEmotes") && c.EmoteNames.Count > 0) t = string.Join(" ", t.Split(' ').Where(w => !c.EmoteNames.Contains(w)));
      t = UrlRe.Replace(t, B("readLinks") ? "$0" : " link ");
      t = Regex.Replace(t, "@([\\w.]+)", m => SpeakName(m.Groups[1].Value));
      t = Pronounce(t);
      if (!B("readEmoji")) { t = Regex.Replace(t, "[\\uD800-\\uDBFF][\\uDC00-\\uDFFF]", " "); t = Regex.Replace(t, "[\\u2600-\\u27BF\\uFE0F\\u200D\\u2B00-\\u2BFF\\u2300-\\u23FF]", " "); }
      t = Regex.Replace(t, "\\b\\d{7,}\\b", " a long number ");                          // numbers first, so "10000" is never shortened below
      t = Regex.Replace(t, "\\b(\\d{1,6})(k)\\b", "$1 thousand", RegexOptions.IgnoreCase);
      t = Regex.Replace(t, "([^\\d\\s])\\1{3,}", "$1$1");                                 // "noooooo" -> "noo", "!!!!!" -> "!!" (digits untouched)
      t = Regex.Replace(t, "\\b(\\w+)(\\W+\\1\\b){2,}", "$1 $1", RegexOptions.IgnoreCase);   // "lol lol lol lol" -> "lol lol"
      int letters = t.Count(char.IsLetter), upper = t.Count(char.IsUpper); if (letters >= 8 && upper > letters * 0.7) t = t.ToLowerInvariant();   // shouting reads as spelled-out letters
      t = Regex.Replace(t, "\\b\\p{Lu}{5,}\\b", m => m.Value.ToLowerInvariant());
      t = Regex.Replace(t, "[<>{}\\[\\]|\\\\^~`*_#=]+", " ");
      t = Regex.Replace(t, "\\s+", " ").Trim();
      if (!t.Any(char.IsLetterOrDigit) || Regex.IsMatch(t, "^(link\\s*)+$")) return null;
      int max = I("maxChars"); if (t.Length > max) { var cut = t.LastIndexOf(' ', max); t = t.Substring(0, cut > max / 2 ? cut : max); }
      return t; }

    // the user's pronunciation list: "word=how to say it", whole words only, any script (Devanagari vowel signs count as part of a word)
    static string Pronounce(string t) {
      foreach (var line in Settings.List("tts.pronounce")) { int eq = line.IndexOf('='); if (eq < 1) continue; string w = line.Substring(0, eq).Trim(), say = line.Substring(eq + 1).Trim(); if (w.Length == 0) continue;
        t = Regex.Replace(t, "(?<![\\p{L}\\p{M}\\p{N}])" + Regex.Escape(w) + "(?![\\p{L}\\p{M}\\p{N}])", say.Replace("$", "$$"), RegexOptions.IgnoreCase); }
      return t; }

    // ---------- playback loop ----------
    static int busy;
    public static void Pump() {
      if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
      Task.Run(() => {
        TtsItem it = null;
        try {
          lock (L) {
            if (current != null || Hub.Count("tts") == 0 || Paused) return;
            while (Queue.Count > 0) { var best = Queue.OrderByDescending(q => q.Priority).ThenBy(q => q.At).First(); Queue.Remove(best);
              if (best.Priority < 2 && (DateTime.Now - best.At).TotalSeconds > I("staleSec")) { best.Status = "dropped (too old)"; AddHistory(best); lock (FilterCounts) { int n; FilterCounts.TryGetValue("too old when its turn came", out n); FilterCounts["too old when its turn came"] = n + 1; } continue; }
              it = best; break; }
            if (it == null) return;
            current = it; it.Status = "preparing"; currentUntil = DateTime.Now.AddSeconds(40); }
          Changed();
          var syn = EnsureSynth(it); if (!syn.Wait(35000)) it.Error = "the voice took too long";
          if (it.Cancelled) return;   // skipped or cleared while the voice was being made
          lock (L) { var nx = Queue.OrderByDescending(q => q.Priority).ThenBy(q => q.At).FirstOrDefault(); if (nx != null) EnsureSynth(nx); }   // prepare the next one while this plays
          if (it.Audio == null) { Interlocked.Increment(ref Failed); Finish(it, "failed: " + (it.Error.Length > 0 ? it.Error : "no audio")); return; }
          lock (L) { if (current != it) return; it.Status = "playing"; it.PlayedAt = DateTime.Now; currentUntil = DateTime.Now.AddSeconds(8 + it.Audio.Length / (it.Mime == "audio/wav" ? 32000.0 : 6000.0)); }
          unduckT.Change(Timeout.Infinite, Timeout.Infinite); Music.Duck(true);
          int vol = Math.Max(0, Math.Min(100, I("volume") * it.Volume / 100));
          Hub.Publish("tts.play", J.D("type", "tts.play", "n", it.N, "url", "/api/tts/audio?n=" + it.N, "volume", vol, "user", it.User, "platform", it.Platform, "text", it.Said));
          Log.Debug("tts", "playing #" + it.N + " via " + it.Engine); Changed();
        } catch (Exception e) { Log.Err("tts", "playback: " + U.Plain(e)); lock (L) { if (current == it) current = null; } }
        finally { Interlocked.Exchange(ref busy, 0); bool again; lock (L) again = current == null && Queue.Count > 0 && Hub.Count("tts") > 0 && !Paused; if (again) Pump(); }
      }); }
    static void Finish(TtsItem it, string status) {
      lock (L) { if (it == null) it = current; if (it == null) return; if (current == it) current = null; it.Status = status; if (status.StartsWith("played")) Played++; AddHistory(it); }
      unduckT.Change(Queue.Count > 0 && !Paused ? 1500 : 350, Timeout.Infinite);   // the next message follows quickly: keep the music down in between
      Hub.Publish("tts", J.D("type", "tts.done", "n", it.N)); Changed(); Pump(); }
    static void AddHistory(TtsItem it) { lock (L) { History.Remove(it); History.AddLast(it); while (History.Count > 40) History.RemoveFirst(); int k = 0; foreach (var h in History.Reverse()) if (++k > 5 && h != current) { h.Audio = null; h.Synth = null; } } }
    static void StopPlayback(string why) { Hub.Publish("tts.play", J.D("type", "tts.stop")); TtsItem c; lock (L) c = current; if (c != null) { c.Cancel(); Finish(c, why); } else Music.Duck(false); }

    static Task EnsureSynth(TtsItem it) {
      lock (it) { if (it.Audio != null) return Task.FromResult(0); if (it.Synth == null) it.Synth = Task.Run(() => Synthesize(it)); return it.Synth; } }
    static void Synthesize(TtsItem it) {
      var sw = System.Diagnostics.Stopwatch.StartNew(); Voice v; if (!Voices().TryGetValue(it.VoiceKey ?? "", out v)) v = Voices()["in-male"];
      int rate = Math.Max(-50, Math.Min(150, v.Rate + I("speed") + it.Rate)), pitch = Math.Max(-50, Math.Min(50, v.Pitch + I("pitch") + it.Pitch));
      try {
        if (Program.TestMode && Environment.GetEnvironmentVariable("IXC_TTS_FAKE") == "1") { it.Audio = Sapi.Silence(Math.Min(4000, 300 + it.Said.Length * 40)); it.Mime = "audio/wav"; it.Engine = "test voice"; return; }
        // engine: auto = Azure when set up (Edge's free voices otherwise), Windows voice as backup; azure = Azure only; windows = Windows voices only
        string eng = S("engine"), online = ""; bool windowsOnly = eng == "windows" || v.Engine == "sapi";
        bool azure = !windowsOnly && (v.Engine == "azure" || eng == "azure" || (v.Engine == "edge" && AzureTts.Ready));   // the Edge voice names are Azure voice names too
        if (azure) {
          try { var b = AzureTts.Speak(it.Said, v, rate, pitch, it); it.Audio = b; it.Mime = "audio/mpeg"; it.Engine = "Azure Speech (" + v.Name + (v.Style != null ? ", " + v.Style : "") + ")"; }
          catch (AzureError e) { if (e.Kind == "cancelled") { it.Error = "cancelled"; return; } online = e.Message; } }
        else if (!windowsOnly && v.Engine == "edge" && DateTime.Now > edgeOffUntil) {
          for (int a = 0; a < 2 && it.Audio == null; a++) {
            try { var b = EdgeTts.Synth(it.Said, v.Name, rate, pitch); if (b != null && b.Length > 800) { it.Audio = b; it.Mime = "audio/mpeg"; it.Engine = "Edge neural (" + v.Name + ")"; edgeFails = 0; edgeError = ""; } else edgeError = "empty audio"; }
            catch (Exception e) { edgeError = U.Plain(e); } }
          if (it.Audio == null) { online = edgeError; if (Interlocked.Increment(ref edgeFails) >= 2) { edgeOffUntil = DateTime.Now.AddSeconds(60); Log.Warn("tts", "neural voice failed (" + edgeError + ") - using the offline Windows voice for 60 s"); } } }
        else if (!windowsOnly) online = "the online voice is paused after errors (" + edgeError + ")";
        if (it.Audio == null && !it.Cancelled && (windowsOnly || (eng != "azure" && B("fallback")))) {
          string err; var name = Sapi.Pick(v.Engine == "sapi" ? null : NeededLocale(v, it.Said), GenderOf(v), out err);
          if (name == null) it.Error = err + (online.Length > 0 ? " (online voice: " + online + ")" : "");
          else try { string used; it.Audio = Sapi.SynthWith(it.Said, name, rate, out used); it.Mime = "audio/wav"; it.Engine = "Windows offline (" + used + ")" + (online.Length > 0 ? " - backup, online voice: " + online : ""); }
            catch (Exception e) { it.Error = "offline voice: " + U.Plain(e); } }
        if (it.Audio == null && it.Error == "") it.Error = online.Length > 0 ? online : "no voice available"; }
      catch (Exception e) { it.Error = U.Plain(e); }
      finally { it.SynthMs = (int)sw.ElapsedMilliseconds; if (it.Audio != null) { Interlocked.Add(ref synthMsTotal, it.SynthMs); Interlocked.Increment(ref synthCount); lastEngine = it.Engine; } } }
    // ---------- languages ----------
    public static string LocaleOf(string name) { var m = Regex.Match(name ?? "", "^([a-z]{2,3}-[A-Z]{2,4})-"); return m.Success ? m.Groups[1].Value : null; }
    public static string LanguageName(string loc) { return loc == "hi-IN" ? "Hindi (hi-IN)" : loc == "en-IN" ? "English (India)" : loc != null && loc.StartsWith("hi") ? "Hindi" : loc != null && loc.StartsWith("en") ? "English (" + loc + ")" : loc; }
    static readonly Regex Devanagari = new Regex("[\\u0900-\\u097F]");
    // the language a Windows backup voice must speak: Hindi when the voice is Hindi or the text is written in Devanagari (English Windows voices skip it), else the voice's own
    static string NeededLocale(Voice v, string text) { var loc = v.Locale ?? LocaleOf(v.Name) ?? "en-IN"; return Devanagari.IsMatch(text ?? "") ? "hi-IN" : loc; }
    static string GenderOf(Voice v) { return !string.IsNullOrEmpty(v.Gender) ? v.Gender : Regex.IsMatch(v.Name ?? "", "Neerja|Swara|Jenny|Aria|Sonia|Ava|Female", RegexOptions.IgnoreCase) ? "Female" : "Male"; }
    // long text -> parts of at most max characters, cut after sentence ends (. ! ? ।), then commas, then spaces
    public static List<string> Split(string text, int max) {
      var r = new List<string>(); text = (text ?? "").Trim(); if (text.Length <= max) { r.Add(text); return r; }
      var cur = new StringBuilder();
      foreach (var s in Regex.Split(text, "(?<=[.!?\\u0964\\u0965])\\s+")) {
        var piece = s;
        while (piece.Length > max) { int cut = piece.LastIndexOf(", ", max, StringComparison.Ordinal); if (cut < max / 3) cut = piece.LastIndexOf(' ', max); if (cut < max / 3) cut = max - 1;
          if (cur.Length > 0) { r.Add(cur.ToString()); cur.Clear(); } r.Add(piece.Substring(0, cut + 1).Trim()); piece = piece.Substring(cut + 1).Trim(); }
        if (cur.Length > 0 && cur.Length + 1 + piece.Length > max) { r.Add(cur.ToString()); cur.Clear(); }
        if (piece.Length > 0) { if (cur.Length > 0) cur.Append(' '); cur.Append(piece); } }
      if (cur.Length > 0) r.Add(cur.ToString()); return r.Where(x => x.Length > 0).ToList(); }
    public static void VoicesChanged() { voiceCache = null; Changed(); }

    // ---------- state for docks / phones / diagnostics ----------
    public static Dictionary<string, object> StateMsg() {
      lock (L) return J.D("type", "tts.state", "settings", SettingsView(), "current", current == null ? null : current.View(), "paused", Paused,
        "queue", Queue.OrderByDescending(q => q.Priority).ThenBy(q => q.At).Select(q => q.View()).ToList(), "history", History.Reverse().Take(15).Select(h => h.View()).ToList(),
        "skipped", Skipped.Reverse().Take(12).ToList(), "players", Hub.Count("tts"), "engine", EngineStatus(), "speaking", current != null && current.Status == "playing", "route", Routing.Info("tts")); }
    static string EngineStatus() { var az = AzureTts.State; if (S("engine") == "windows") return "Windows voices only" + (lastEngine.StartsWith("Windows") ? " - last: " + lastEngine : "");
      if (az == "paused" || az == "rejected") return "Azure: " + J.Str(AzureTts.View(), "message", "") + (lastEngine.Length > 0 ? " - last: " + lastEngine : "");
      if (AzureTts.Ready) return lastEngine.Length > 0 ? lastEngine : "Azure Speech (ready)";
      return DateTime.Now < edgeOffUntil ? "offline Windows voice (neural voice failed: " + edgeError + ")" : lastEngine.Length > 0 ? lastEngine : "Edge neural (ready)"; }
    public static bool Speaking { get { lock (L) return current != null && current.Status == "playing"; } }
    public static Dictionary<string, object> DiagInfo() {
      lock (L) return J.D("enabled", On, "paused", Paused, "voice", S("voice"), "engine", EngineStatus(), "neuralError", edgeError, "azure", AzureTts.View(), "engineSetting", S("engine"), "queue", Queue.Count, "current", current == null ? null : current.Said,
        "played", Played, "failed", Failed, "queued", Enqueued, "avgSynthMs", synthCount == 0 ? 0 : (int)(synthMsTotal / synthCount), "ttsSourcesConnected", Hub.Count("tts"),
        "notRead", new Dictionary<string, int>(FilterCounts), "offlineVoices", Sapi.List()); }
    public static TtsItem Replay(int n) { TtsItem h; lock (L) h = History.FirstOrDefault(x => x.N == n); if (h == null) return null;
      return Enqueue(new TtsItem { Text = h.Text, Said = h.Said, User = h.User, Platform = h.Platform, VoiceKey = h.VoiceKey, Rate = h.Rate, Pitch = h.Pitch, Volume = h.Volume, Priority = 2, Audio = h.Audio, Mime = h.Mime, Engine = h.Engine }); }
    public static void Clear() { lock (L) { foreach (var it in Queue) { it.Cancel(); it.Status = "cleared"; AddHistory(it); } Queue.Clear(); } Changed(); }
    public static void SkipNow() { StopPlayback("skipped"); }
    public static TtsItem Say(string text, string voice, int prio) {
      text = (text ?? "").Trim(); if (text.Length == 0) return null; var clean = Clean(text, null) ?? text; if (clean.Length > 500) clean = clean.Substring(0, 500);
      return Enqueue(new TtsItem { Text = text, Said = clean, User = "you", Platform = "test", VoiceKey = voice, Priority = prio }); }
    // the dashboard's "voices for people" list
    public static string SetUserVoice(string who, string platform, Dictionary<string, object> v) {
      who = Norm(who); if (who.Length == 0) return "type a name";
      var users = Settings.Obj("tts.users"); var key = (Settings.PlatformIds.Contains(platform) ? platform : "*") + ":" + who;
      if (v == null) users.Remove(key); else users[key] = v; return Settings.Set("tts.users", users); }

    // ---------- API ----------
    public static bool Api(Ctx ctx, string path, string m, System.Collections.Specialized.NameValueCollection q) {
      if (!path.StartsWith("/api/tts") && path != "/api/say" && path != "/api/skip" && path != "/api/tts.mp3") return false;
      if (path == "/api/tts/audio" || path == "/api/tts.mp3") { int n; int.TryParse(q["n"], out n); TtsItem it; lock (L) it = current != null && current.N == n ? current : History.Concat(Queue).FirstOrDefault(x => x.N == n);
        if (it == null) { Http.Send(ctx, 404, "no such message", "text/plain"); return true; } EnsureSynth(it).Wait(20000);
        if (it.Audio == null) Http.Send(ctx, 502, "tts failed: " + it.Error, "text/plain"); else Http.SendBytes(ctx, 200, it.Audio, it.Mime); return true; }
      if (path == "/api/tts/state") { Http.Json(ctx, StateMsg()); return true; }
      if (path == "/api/tts/voices") { Http.Json(ctx, J.D("voices", VoiceList())); return true; }
      if (path == "/api/tts/settings") { if (m == "POST") { var err = ApplyLegacy(Http.BodyJson(ctx)); if (err != null) { Http.Json(ctx, 400, J.D("error", err)); return true; } }
        Http.Json(ctx, J.D("settings", SettingsView(), "voices", VoiceList())); return true; }
      if ((path == "/api/tts/say" || path == "/api/say") && m == "POST") { var b = Http.BodyJson(ctx); var it = Say(J.Str(b, "text", ""), J.Str(b, "voice", null), 2); Http.Json(ctx, J.D("n", it == null ? 0 : it.N, "players", Hub.Count("tts"))); return true; }
      if ((path == "/api/tts/skip" || path == "/api/skip") && m == "POST") { SkipNow(); Http.Json(ctx, J.D("ok", true)); return true; }
      if (path == "/api/tts/clear" && m == "POST") { Clear(); Http.Json(ctx, J.D("ok", true)); return true; }
      if (path == "/api/tts/replay" && m == "POST") { var it = Replay(J.Int(Http.BodyJson(ctx), "n", 0)); Http.Json(ctx, it == null ? 404 : 200, J.D("n", it == null ? 0 : it.N)); return true; }
      if (path == "/api/tts/azure" && m == "POST") { var b = Http.BodyJson(ctx); var err = AzureTts.Configure(J.Str(b, "region", null), J.Str(b, "key", null), J.Bool(b, "remove", false));
        string check = null; if (err == null && !J.Bool(b, "remove", false) && AzureTts.Ready) check = AzureTts.FetchCatalog();   // loads the voice list and so checks the key once
        if (err == null) VoicesChanged(); Http.Json(ctx, err == null ? 200 : 400, J.D("ok", err == null, "error", err, "ready", AzureTts.Ready, "region", AzureTts.Region, "keySet", Secrets.Has("azure.speechKey"), "checkError", check, "azure", AzureTts.View())); return true; }
      if (path == "/api/tts/azure-test" && m == "POST") { string err; int n = AzureTts.Test(out err); VoicesChanged(); Http.Json(ctx, err == null ? 200 : 400, J.D("ok", err == null, "error", err, "bytes", n, "azure", AzureTts.View())); return true; }
      // preview: made like a chat message but only returned to the dashboard - not queued, not played in OBS, music not lowered
      if (path == "/api/tts/preview" && m == "POST") { var b = Http.BodyJson(ctx); var text = Clean(J.Str(b, "text", ""), null);
        if (text == null) { Http.Json(ctx, 400, J.D("ok", false, "error", "Type a test sentence")); return true; } if (text.Length > 300) text = text.Substring(0, 300);
        var it = new TtsItem { Text = text, Said = text, User = "preview", Platform = "test", VoiceKey = J.Str(b, "voice", S("voice")) };
        var t = Task.Run(() => Synthesize(it)); if (!t.Wait(35000)) { it.Cancel(); it.Error = "the voice took too long"; }
        Http.Json(ctx, it.Audio == null ? 502 : 200, J.D("ok", it.Audio != null, "error", it.Audio == null ? it.Error : null, "engine", it.Engine, "mime", it.Mime, "ms", it.SynthMs, "audio", it.Audio == null ? null : Convert.ToBase64String(it.Audio))); return true; }
      if (path == "/api/tts/user" && m == "POST") { var b = Http.BodyJson(ctx); var err = SetUserVoice(J.Str(b, "name", ""), J.Str(b, "platform", "*"), J.Bool(b, "remove", false) ? null : J.Obj(b, "voice")); Http.Json(ctx, err == null ? 200 : 400, J.D("ok", err == null, "error", err)); return true; }
      return false; }
    // the v2 dock sends {voice, speed, enabled, ...}: map those names to settings keys
    static string ApplyLegacy(Dictionary<string, object> patch) {
      if (patch == null || patch.Count == 0) return "nothing to change"; var p = new Dictionary<string, object>();
      foreach (var kv in patch) p[kv.Key == "enabled" ? "tts.on" : kv.Key.StartsWith("tts.") ? kv.Key : "tts." + kv.Key] = kv.Value;
      return Settings.Apply(p); }
    public static void OnWs(Client c, string type, Dictionary<string, object> msg) {
      switch (type) {
        case "hello": if (c.Role == "tts") { Log.Info("tts", "TTS source connected"); if (Paused) Hub.Send(c, J.D("type", "tts.pause")); Pump(); } if (c.Topics.Contains("tts")) { Hub.Send(c, StateMsg()); Hub.Send(c, J.D("type", "tts.voices", "voices", VoiceList())); } break;
        case "_closed": if (c.Role == "tts") { Log.Info("tts", "TTS source disconnected"); if (Hub.Count("tts") == 0) { TtsItem cur; lock (L) cur = current; if (cur != null && cur.Status == "playing") Finish(cur, "interrupted (TTS source closed)"); } } break;
        case "tts.done": { int n = J.Int(msg, "n", -1); bool ok = J.Bool(msg, "ok", true); TtsItem cur; lock (L) cur = current; if (cur != null && cur.N == n) { if (!ok) Interlocked.Increment(ref Failed); Finish(cur, ok ? "played" : "failed: the TTS source could not play it (" + J.Str(msg, "error", "?") + ")"); } break; }
        case "tts.set": { var err = ApplyLegacy(J.Obj(msg, "patch")); if (err != null) Hub.Send(c, J.D("type", "error", "error", err)); break; }
        case "tts.say": { var it = Say(J.Str(msg, "text", ""), J.Str(msg, "voice", null), 2); if (it != null && Hub.Count("tts") == 0) Hub.Send(c, J.D("type", "error", "error", "The chat voice source isn't open in OBS yet - it will speak as soon as OBS is open (System check sets it up).")); break; }
        case "tts.test": { Voice v; var key = J.Str(msg, "voice", S("voice")); Voices().TryGetValue(key, out v); Say(key == "jarvis" ? "Good evening. All systems are online, and chat is connected." : "Hi, this is " + (v == null ? "the chat voice" : v.Label.Split('-')[0].Trim().ToLowerInvariant() + " voice") + ". Chat text to speech is working.", key, 2); if (Hub.Count("tts") == 0) Hub.Send(c, J.D("type", "error", "error", "The chat voice source isn't open in OBS - open OBS (System check sets it up).")); break; }
        case "tts.skip": SkipNow(); break;
        case "tts.clear": Clear(); break;
        case "tts.pause": Settings.Set("tts.paused", true); break;
        case "tts.resume": Settings.Set("tts.paused", false); break;
        case "tts.replay": Replay(J.Int(msg, "n", 0)); break;
        case "tts.user": { var err = SetUserVoice(J.Str(msg, "name", ""), J.Str(msg, "platform", "*"), J.Bool(msg, "remove", false) ? null : J.Obj(msg, "voice")); if (err != null) Hub.Send(c, J.D("type", "error", "error", err)); break; }
      } }
  }

  // ---------------- Azure Speech (official Microsoft service; the user's own key, free tier about 500,000 characters a month) ----------------
  // The key is stored encrypted in secrets.dat (Windows DPAPI, current user - the same protection Windows Credential Manager uses), is never shown
  // again and never written to a log or an error text. Limits: the key sits on this PC, so anything running as this Windows user could read it;
  // a separate token server would be needed for stronger protection (see docs/VOICES.md). The voice list comes from Azure itself (voices/list).
  public class AzureError : Exception {
    public string Kind;   // auth, region, network, timeout, quota, server, bad, cancelled, skipped
    public AzureError(string kind, string msg) : base(msg) { Kind = kind; }
    public bool Transient { get { return Kind == "network" || Kind == "timeout" || Kind == "server"; } }
  }
  public static class AzureTts {
    public static string Region { get { return (Settings.Str("tts.azure.region") ?? "").Trim().ToLowerInvariant(); } }
    static bool GoodRegion(string r) { return Regex.IsMatch(r ?? "", "^[a-z0-9]{3,30}$"); }
    public static bool Ready { get { return GoodRegion(Region) && !string.IsNullOrEmpty(Secrets.Str("azure.speechKey")); } }
    static int TimeoutMs { get { return Math.Max(3, Settings.Int("tts.azure.timeoutSec")) * 1000; } }

    // ---- circuit breaker: two failures in a row -> no Azure calls for tts.azure.cooldownSec, then one trial request; a rejected key or an unknown region stops calls until Save ----
    static readonly object BL = new object(); static int fails; static DateTime offUntil = DateTime.MinValue; static bool configBad, probing; static DateTime warnAt = DateTime.MinValue;
    public static string LastError = "";
    public static void Reset() { lock (BL) { fails = 0; offUntil = DateTime.MinValue; configBad = false; probing = false; LastError = ""; } }
    static bool Enter(out string why) {
      lock (BL) {
        why = null; if (!Ready) { why = "Azure Speech is not set up (add the region and key on the Chat voice page)"; return false; }
        if (configBad) { why = LastError; return false; }
        if (DateTime.Now < offUntil) { why = "Azure is paused for " + (int)Math.Ceiling((offUntil - DateTime.Now).TotalSeconds) + " s after errors (" + LastError + ")"; return false; }
        if (fails >= 2) { if (probing) { why = "Azure is being re-checked"; return false; } probing = true; }
        return true; } }
    static void Ok() { lock (BL) { fails = 0; offUntil = DateTime.MinValue; probing = false; LastError = ""; } }
    static void Fail(AzureError e) {
      lock (BL) { probing = false; if (e.Kind == "cancelled" || e.Kind == "skipped") return; LastError = e.Message;
        if (e.Kind == "auth") configBad = true;   // an unknown region looks the same as no internet, so it only pauses
        else if (e.Kind != "bad" && ++fails >= 2) offUntil = DateTime.Now.AddSeconds(Math.Max(10, Settings.Int("tts.azure.cooldownSec")));
        if ((DateTime.Now - warnAt).TotalSeconds > 30) { warnAt = DateTime.Now; Log.Warn("tts", "Azure Speech: " + e.Message); } } }
    public static string State { get { lock (BL) return !Ready ? "off" : configBad ? "rejected" : DateTime.Now < offUntil ? "paused" : Catalog == null ? "unchecked" : "ready"; } }
    public static Dictionary<string, object> View() {
      string st = State, msg;
      lock (BL) msg = st == "off" ? (Secrets.Has("azure.speechKey") ? "Key saved - add the region" : "Not set up") : st == "rejected" ? LastError + " - check the key and region, then press Save"
        : st == "paused" ? "Not reachable (" + LastError + "). Using the Windows voice; trying Azure again in " + (int)Math.Ceiling((offUntil - DateTime.Now).TotalSeconds) + " s"
        : st == "unchecked" ? "Saved - press Test to load the voice list" : "Ready - " + Catalog.Count + " voices from Azure";
      return J.D("state", st, "message", msg, "region", Region, "keySet", Secrets.Has("azure.speechKey"), "voices", Catalog == null ? 0 : Catalog.Count, "lastError", LastError); }

    public static string Configure(string region, string key, bool remove) {
      if (remove) { Secrets.Set("azure.speechKey", null); Reset(); SetCatalog(null); return null; }
      region = (region ?? "").Trim().ToLowerInvariant(); key = (key ?? "").Trim();
      if (region.Length > 0 && !GoodRegion(region)) return "The region looks like centralindia, eastus or southeastasia (letters and numbers only)";
      if (key.Length > 0 && !Regex.IsMatch(key, "^[A-Za-z0-9]{20,100}$")) return "That doesn't look like an Azure Speech key (32 or more letters and numbers, no spaces)";
      if (region.Length == 0 && !GoodRegion(Region)) return "Type the region of your Speech resource first (shown next to the key in Azure, for example centralindia)";
      if (key.Length == 0 && !Secrets.Has("azure.speechKey")) return "Paste the key from your Speech resource in Azure";
      if (region.Length > 0 && region != Region) { var e = Settings.Set("tts.azure.region", region); if (e != null) return e; SetCatalog(null); }
      if (key.Length > 0) { Secrets.Set("azure.speechKey", key); SetCatalog(null); }
      Reset();
      return Secrets.SaveError == null ? null : "Could not save the key (" + Secrets.SaveError + ")"; }

    // ---- one HTTP request; errors become AzureError with a plain reason (never the key) ----
    static HttpWebRequest Req(string url, string method, TtsItem it) {
      var req = (HttpWebRequest)WebRequest.Create(url); req.Method = method; req.Timeout = TimeoutMs; req.ReadWriteTimeout = TimeoutMs; req.UserAgent = "IXC";
      req.Headers["Ocp-Apim-Subscription-Key"] = Secrets.Str("azure.speechKey");
      if (it != null) it.Abort = () => { try { req.Abort(); } catch { } };
      return req; }
    static byte[] Run(HttpWebRequest req, byte[] body, TtsItem it) {
      try {
        if (it != null && it.Cancelled) throw new AzureError("cancelled", "cancelled");
        if (body != null) { req.ContentLength = body.Length; using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length); }
        using (var resp = (HttpWebResponse)req.GetResponse()) using (var ms = new MemoryStream()) { resp.GetResponseStream().CopyTo(ms); return ms.ToArray(); } }
      catch (WebException e) {
        if (it != null && it.Cancelled) throw new AzureError("cancelled", "cancelled");
        var r = e.Response as HttpWebResponse;
        if (r != null) { int code = (int)r.StatusCode; r.Close();
          if (code == 401 || code == 403) throw new AzureError("auth", "Azure rejected the key or the region (HTTP " + code + ")");
          if (code == 429) throw new AzureError("quota", "Azure's free quota is used up or there were too many requests (429)");
          if (code == 400) throw new AzureError("bad", "Azure could not read this voice or text (HTTP 400)");
          throw new AzureError(code >= 500 ? "server" : "bad", "Azure Speech error (HTTP " + code + ")"); }
        if (e.Status == WebExceptionStatus.NameResolutionFailure) throw new AzureError("region", "Azure region '" + Region + "' was not found (or there is no internet)");
        if (e.Status == WebExceptionStatus.Timeout) throw new AzureError("timeout", "Azure Speech did not answer within " + TimeoutMs / 1000 + " s");
        throw new AzureError("network", "Azure Speech could not be reached (" + e.Status + ")"); }
      finally { if (it != null) it.Abort = null; } }
    static byte[] Request(string text, Voice v, int ratePct, int pitchHz, TtsItem it) {
      var url = Ep.Get("AZURE_TTS", "https://" + Region + ".tts.speech.microsoft.com/cognitiveservices/v1");
      var req = Req(url, "POST", it); req.ContentType = "application/ssml+xml"; req.Headers["X-Microsoft-OutputFormat"] = "audio-24khz-48kbitrate-mono-mp3";
      return Run(req, Encoding.UTF8.GetBytes(Ssml(text, v, ratePct, pitchHz)), it); }
    public static string Ssml(string text, Voice v, int ratePct, int pitchHz) {
      string rate = (ratePct >= 0 ? "+" : "") + ratePct + "%", pitch = (pitchHz >= 0 ? "+" : "") + pitchHz + "Hz", lang = v.Locale ?? Tts.LocaleOf(v.Name) ?? "en-US";
      var inner = "<prosody pitch='" + pitch + "' rate='" + rate + "'>" + System.Security.SecurityElement.Escape(text) + "</prosody>";
      if (!string.IsNullOrEmpty(v.Style)) inner = "<mstts:express-as style='" + v.Style + "'>" + inner + "</mstts:express-as>";
      return "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='https://www.w3.org/2001/mstts' xml:lang='" + lang + "'><voice name='" + v.Name + "'>" + inner + "</voice></speak>"; }

    // a chat message: long text is split at sentence ends, each part is one request (with tts.azure.retries retries for network errors), MP3 parts are joined
    public static byte[] Speak(string text, Voice v, int ratePct, int pitchHz, TtsItem it) {
      string why; if (!Enter(out why)) throw new AzureError("skipped", why);
      var all = new MemoryStream(); int retries = Math.Max(0, Math.Min(2, Settings.Int("tts.azure.retries")));
      try {
        foreach (var part in Tts.Split(text, 220)) {
          byte[] b = null;
          for (int a = 0; b == null; a++) {
            try { b = Request(part, v, ratePct, pitchHz, it); }
            catch (AzureError e) { if (e.Kind == "cancelled" || !e.Transient || a >= retries) throw; } }
          if (b.Length < 200) throw new AzureError("server", "Azure answered without audio");
          all.Write(b, 0, b.Length); } }
      catch (AzureError e) { Fail(e); throw; }
      Ok(); return all.ToArray(); }

    // ---- voice catalogue from Azure, kept in azure-voices.json (not secret) so it survives restarts ----
    public static List<Dictionary<string, object>> Catalog; static DateTime catalogAt; static int refreshing;
    static string CatalogFile { get { return Path.Combine(Cfg.DataDir ?? ".", "azure-voices.json"); } }
    static void SetCatalog(List<Dictionary<string, object>> list) {
      Catalog = list; catalogAt = DateTime.Now; Tts.VoicesChanged();
      try { if (list == null) File.Delete(CatalogFile); else File.WriteAllText(CatalogFile, J.Ser(J.D("region", Region, "at", DateTime.UtcNow.ToString("o"), "voices", list))); } catch { } }
    public static void Load() {
      try { if (!File.Exists(CatalogFile)) return; var d = J.Parse(File.ReadAllText(CatalogFile)); if (J.Str(d, "region", "") != Region) return;
        var arr = d.ContainsKey("voices") ? d["voices"] as ArrayList : null; if (arr == null) return;
        Catalog = arr.Cast<object>().OfType<Dictionary<string, object>>().ToList(); DateTime at; catalogAt = DateTime.TryParse(J.Str(d, "at", ""), null, DateTimeStyles.RoundtripKind, out at) ? at.ToLocalTime() : DateTime.MinValue; } catch { } }
    public static string FetchCatalog() {
      try { if (!Ready) return "Azure Speech is not set up";
        var url = Ep.Get("AZURE_VOICES", "https://" + Region + ".tts.speech.microsoft.com/cognitiveservices/voices/list");
        var raw = Encoding.UTF8.GetString(Run(Req(url, "GET", null), null, null)); var arr = J.S.Deserialize<ArrayList>(raw);
        var keep = new List<Dictionary<string, object>>();
        foreach (var o in arr.Cast<object>().OfType<Dictionary<string, object>>()) {
          string name = J.Str(o, "ShortName", ""), loc = J.Str(o, "Locale", ""); if (!Regex.IsMatch(name, "^[A-Za-z]{2,3}-[A-Za-z]{2,4}-\\w+$")) continue;
          if (J.Str(o, "VoiceType", "Neural") != "Neural" || J.Str(o, "Status", "") == "Deprecated") continue;
          var second = (o.ContainsKey("SecondaryLocaleList") ? o["SecondaryLocaleList"] as ArrayList : null) ?? new ArrayList();
          var styles = (o.ContainsKey("StyleList") ? o["StyleList"] as ArrayList : null) ?? new ArrayList();
          bool native = loc == "en-IN" || loc == "hi-IN", multi = !native && (second.Contains("hi-IN") || second.Contains("en-IN")), whisper = styles.Contains("whispering");
          if (!native && !multi && !whisper) continue;
          keep.Add(J.D("name", name, "locale", loc, "gender", J.Str(o, "Gender", ""), "display", J.Str(o, "DisplayName", name), "kind", native ? "native" : multi ? "multilingual" : "style",
            "hindi", loc == "hi-IN" || second.Contains("hi-IN"), "whisper", whisper)); }
        if (keep.Count == 0) return "Azure answered, but offers no Indian English or Hindi voices in region " + Region;
        SetCatalog(keep); Ok(); return null; }
      catch (AzureError e) { Fail(e); return e.Message; }
      catch (Exception e) { return "Azure's voice list could not be read (" + U.Plain(e) + ")"; } }
    // refresh in the background once a day, never while Azure is paused or rejected
    public static void MaybeRefresh() {
      if (!Ready || State == "rejected" || State == "paused" || (Catalog != null && (DateTime.Now - catalogAt).TotalHours < 24)) return;
      if (Interlocked.Exchange(ref refreshing, 1) == 1) return;
      Task.Run(() => { try { FetchCatalog(); } finally { Interlocked.Exchange(ref refreshing, 0); } }); }

    // voices for the menu: every native en-IN / hi-IN voice, multilingual voices that list Hindi or Indian English, a whisper voice, and short stable names
    public static List<Voice> MenuVoices() {
      var r = new List<Voice>(); var cat = Catalog; if (!Ready || cat == null) return r;
      Func<Dictionary<string, object>, Voice> mk = d => { string name = J.Str(d, "name", ""), loc = J.Str(d, "locale", ""), kind = J.Str(d, "kind", ""), g = J.Str(d, "gender", "");
        return new Voice { Key = "az-" + Regex.Replace(name, "Neural$", "").ToLowerInvariant(), Name = name, Locale = loc, Gender = g, Kind = kind, Engine = "azure", IsNew = true,
          Label = "Azure · " + (kind == "native" ? (loc == "hi-IN" ? "Hindi (India)" : "English (India)") + " · native" : kind == "multilingual" ? "multilingual (" + loc + ", also " + (J.Bool(d, "hindi", false) ? "Hindi" : "Indian English") + ")" : loc) + " · " + J.Str(d, "display", name) + " · " + g.ToLowerInvariant() }; };
      Func<string, string, string, string, Dictionary<string, object>> find = (loc, kind, g, pref) => {
        var pool = cat.Where(d => (loc == null || J.Str(d, "locale", "") == loc) && J.Str(d, "kind", "") == kind && J.Str(d, "gender", "").Equals(g, StringComparison.OrdinalIgnoreCase)).ToList();
        return pool.FirstOrDefault(d => J.Str(d, "name", "").Contains(pref)) ?? pool.FirstOrDefault(); };
      Action<string, string, Dictionary<string, object>, string, int, int> alias = (key, label, d, style, rate, pitch) => { if (d == null) return; var v = mk(d);
        v.Key = key; v.Label = label + " - " + J.Str(d, "display", v.Name) + " (Azure, " + (v.Kind == "native" ? "native " + v.Locale : v.Kind) + ")"; v.Style = style; v.Rate = rate; v.Pitch = pitch; r.Add(v); };
      alias("az-in-female", "Azure Indian female", find("en-IN", "native", "Female", "Neerja"), null, 0, 0); alias("az-in-male", "Azure Indian male", find("en-IN", "native", "Male", "Prabhat"), null, 0, 0);
      alias("az-hi-female", "Azure Hindi female", find("hi-IN", "native", "Female", "Swara"), null, 0, 0); alias("az-hi-male", "Azure Hindi male", find("hi-IN", "native", "Male", "Madhur"), null, 0, 0);
      alias("az-multi-female", "Azure multilingual female", find(null, "multilingual", "Female", "Ava"), null, 0, 0); alias("az-multi-male", "Azure multilingual male", find(null, "multilingual", "Male", "Andrew"), null, 0, 0);
      alias("az-whisper", "Azure soft whisper (slow, low, whispering style)", cat.FirstOrDefault(d => J.Bool(d, "whisper", false) && J.Str(d, "name", "") == "en-US-JennyNeural") ?? cat.FirstOrDefault(d => J.Bool(d, "whisper", false)), "whispering", -15, -4);
      foreach (var d in cat.Where(d => J.Str(d, "kind", "") != "style").OrderBy(d => J.Str(d, "kind", "")).ThenBy(d => J.Str(d, "locale", "")).ThenBy(d => J.Str(d, "name", ""))) r.Add(mk(d));
      return r; }

    // "Test" button: reloads the voice list and makes one short real request; nothing is played and nothing is read out in chat
    public static int Test(out string err) {
      if (!Ready) { err = "Azure Speech is not set up (add the region and key, then press Save)"; return 0; }
      Reset(); err = FetchCatalog(); if (err != null) return 0;
      var d = Catalog.FirstOrDefault(x => J.Str(x, "locale", "") == "en-IN") ?? Catalog.First();
      try { var b = Request("Namaste, this is a test.", new Voice { Name = J.Str(d, "name", ""), Locale = J.Str(d, "locale", "") }, 0, 0, null); if (b.Length < 200) { err = "Azure answered without audio"; return 0; } Ok(); return b.Length; }
      catch (AzureError e) { Fail(e); err = e.Message; return 0; } }
  }

  // ---------------- Microsoft Edge "Read aloud" neural voices ----------------
  // Unofficial: the endpoint Microsoft Edge uses for Read Aloud. Not a documented API; it may change or stop working, and its use is
  // subject to Microsoft's terms. The token is the public constant shipped in Edge (also used by the open-source edge-tts project).
  public static class EdgeTts {
    const string Token = "6A5AA1D4EAFF4E9FB37E23D68491D6F4", Ver = "1-143.0.3650.75", HostName = "speech.platform.bing.com";
    static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();
    public static byte[] Synth(string text, string voice, int ratePct, int pitchHz) {
      long ticks = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 11644473600L) * 10000000L; ticks -= ticks % 3000000000L;
      string gec; using (var sh = SHA256.Create()) gec = BitConverter.ToString(sh.ComputeHash(Encoding.ASCII.GetBytes(ticks + Token))).Replace("-", "");
      string cid = Guid.NewGuid().ToString("N");
      string path = "/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken=" + Token + "&Sec-MS-GEC=" + gec + "&Sec-MS-GEC-Version=" + Ver + "&ConnectionId=" + cid;
      using (var tcp = new TcpClient()) {
        tcp.ReceiveTimeout = 12000; tcp.SendTimeout = 12000;
        if (!tcp.ConnectAsync(HostName, 443).Wait(8000)) throw new Exception("neural voice service not reachable (internet?)");
        using (var ssl = new SslStream(tcp.GetStream(), false)) {
          ssl.AuthenticateAsClient(HostName, null, System.Security.Authentication.SslProtocols.Tls12, true);
          var kb = new byte[16]; Rng.GetBytes(kb);
          var req = "GET " + path + " HTTP/1.1\r\nHost: " + HostName + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + Convert.ToBase64String(kb) + "\r\nSec-WebSocket-Version: 13\r\n" +
                    "Origin: chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold\r\nPragma: no-cache\r\nCache-Control: no-cache\r\nAccept-Language: en-US,en;q=0.9\r\n" +
                    "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0\r\n\r\n";
          var rb = Encoding.ASCII.GetBytes(req); ssl.Write(rb, 0, rb.Length); ssl.Flush();
          var head = new StringBuilder(); while (!head.ToString().EndsWith("\r\n\r\n")) { int ch = ssl.ReadByte(); if (ch < 0) throw new Exception("no handshake"); head.Append((char)ch); if (head.Length > 8192) throw new Exception("bad handshake"); }
          if (!head.ToString().StartsWith("HTTP/1.1 101")) throw new Exception("neural voice refused: " + head.ToString().Split('\r')[0]);
          string ts = DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT+0000 (Coordinated Universal Time)";
          Send(ssl, Encoding.UTF8.GetBytes("X-Timestamp:" + ts + "\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}"));
          string rate = (ratePct >= 0 ? "+" : "") + ratePct + "%", pitch = (pitchHz >= 0 ? "+" : "") + pitchHz + "Hz";
          Send(ssl, Encoding.UTF8.GetBytes("X-RequestId:" + cid + "\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:" + ts + "Z\r\nPath:ssml\r\n\r\n" +
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'><voice name='" + voice + "'><prosody pitch='" + pitch + "' rate='" + rate + "' volume='+0%'>" + System.Security.SecurityElement.Escape(text) + "</prosody></voice></speak>"));
          var audio = new MemoryStream(); var deadline = DateTime.Now.AddSeconds(20);
          while (DateTime.Now < deadline) {
            int op; var b = Recv(ssl, out op);
            if (op == 2 && b.Length > 2) { int hl = b[0] * 256 + b[1]; if (hl + 2 <= b.Length && Encoding.ASCII.GetString(b, 2, hl).Contains("Path:audio")) audio.Write(b, 2 + hl, b.Length - 2 - hl); }
            else if (op == 1 && Encoding.UTF8.GetString(b).Contains("Path:turn.end")) break;
            else if (op == 8) break; }
          return audio.ToArray(); } } }
    static void Send(Stream s, byte[] p) {
      var h = new List<byte> { 0x81 }; int len = p.Length;
      if (len < 126) h.Add((byte)(0x80 | len)); else if (len < 65536) { h.Add(0xFE); h.Add((byte)(len >> 8)); h.Add((byte)(len & 255)); } else { h.Add(0xFF); for (int i = 7; i >= 0; i--) h.Add((byte)(((long)len >> (8 * i)) & 255)); }
      var mask = new byte[4]; Rng.GetBytes(mask); h.AddRange(mask); var body = new byte[len]; for (int i = 0; i < len; i++) body[i] = (byte)(p[i] ^ mask[i % 4]);
      var all = h.Concat(body).ToArray(); s.Write(all, 0, all.Length); s.Flush(); }
    static byte[] ReadN(Stream s, int n) { var b = new byte[n]; int o = 0; while (o < n) { int r = s.Read(b, o, n - o); if (r <= 0) throw new Exception("connection closed"); o += r; } return b; }
    static byte[] Recv(Stream s, out int op) {
      var data = new MemoryStream(); op = 0; bool fin;
      do { var h = ReadN(s, 2); fin = (h[0] & 0x80) != 0; int o = h[0] & 0x0F; if (o != 0) op = o; long len = h[1] & 0x7F;
        if (len == 126) { var e = ReadN(s, 2); len = e[0] * 256 + e[1]; } else if (len == 127) { var e = ReadN(s, 8); len = 0; foreach (var x in e) len = len * 256 + x; }
        if (len > 4000000) throw new Exception("frame too large"); if (len > 0) { var p = ReadN(s, (int)len); data.Write(p, 0, p.Length); } } while (!fin);
      return data.ToArray(); }
  }

  // ---------------- offline fallback: the voices installed in Windows (SAPI) ----------------
  public static class Sapi {
    public class Local { public string Name, Culture, Gender; }
    static List<Local> cache; static DateTime cacheAt = DateTime.MinValue;
    // installed Windows voices; in test mode IXC_SAPI_FAKE="en-IN:Heera:Female;hi-IN:Kalpana:Female" stands in for them
    public static List<Local> Installed() {
      if (cache != null && (DateTime.Now - cacheAt).TotalSeconds < 60) return cache;
      var fake = Program.TestMode ? Environment.GetEnvironmentVariable("IXC_SAPI_FAKE") : null; var r = new List<Local>();
      if (fake != null) r = fake.Split(';').Where(x => x.Contains(':')).Select(x => { var p = x.Split(':'); return new Local { Culture = p[0], Name = p[1], Gender = p.Length > 2 ? p[2] : "" }; }).ToList();
      else try { using (var s = new System.Speech.Synthesis.SpeechSynthesizer()) r = s.GetInstalledVoices().Where(v => v.Enabled).Select(v => new Local { Name = v.VoiceInfo.Name, Culture = v.VoiceInfo.Culture.Name, Gender = v.VoiceInfo.Gender.ToString() }).ToList(); } catch { }
      cache = r; cacheAt = DateTime.Now; return r; }
    public static List<string> List() { return Installed().Select(v => v.Name + " (" + v.Culture + ", " + v.Gender + ")").ToList(); }
    public static bool Has(string lang) { return Installed().Any(v => v.Culture.StartsWith(lang, StringComparison.OrdinalIgnoreCase)); }
    // a Windows voice in the SAME language (never another language): exact locale first (en-IN for Indian English), then the same language, preferring the gender
    public static string Pick(string locale, string gender, out string err) {
      err = null; var vs = Installed(); if (vs.Count == 0) { err = "No Windows voices are installed"; return null; }
      Func<List<Local>, Local> byGender = pool => pool.FirstOrDefault(v => !string.IsNullOrEmpty(gender) && v.Gender.Equals(gender, StringComparison.OrdinalIgnoreCase)) ?? pool.FirstOrDefault();
      if (string.IsNullOrEmpty(locale)) { var pick = byGender(vs.Where(v => v.Culture == "en-IN").ToList()) ?? byGender(vs.Where(v => v.Culture.StartsWith("en")).ToList()) ?? vs.First(); return pick.Name; }
      var lang = locale.Split('-')[0]; var same = vs.Where(v => v.Culture.Split('-')[0].Equals(lang, StringComparison.OrdinalIgnoreCase)).ToList();
      if (same.Count == 0) { err = "No Windows voice for " + Tts.LanguageName(locale) + " is installed. Install the " + (lang == "hi" ? "Hindi" : lang == "en" ? "English (India)" : locale) + " speech pack (Windows Settings > Time & language > Speech > Add voices), or pick another voice."; return null; }
      var exact = same.Where(v => v.Culture.Equals(locale, StringComparison.OrdinalIgnoreCase)).ToList(); if (exact.Count == 0 && lang == "en") exact = same.Where(v => v.Culture == "en-IN").ToList();
      return (byGender(exact) ?? byGender(same)).Name; }
    // best local voice for a neural voice (used by the system check)
    public static string Best(string neural) { string err; return Pick(neural == null ? null : Tts.LocaleOf(neural), neural != null && Regex.IsMatch(neural, "Neerja|Swara|Jenny|Aria|Sonia|Female|Zira|Heera|Kalpana", RegexOptions.IgnoreCase) ? "Female" : neural == null ? null : "Male", out err); }
    public static byte[] Synth(string text, string neural, int ratePct, out string used) { var name = Best(neural); if (name == null) throw new Exception("no Windows voices installed"); return SynthWith(text, name, ratePct, out used); }
    public static byte[] SynthWith(string text, string name, int ratePct, out string used) {
      if (Program.TestMode && Environment.GetEnvironmentVariable("IXC_SAPI_FAKE") != null) { used = name; return Silence(Math.Min(4000, 300 + text.Length * 40)); }
      byte[] result = null; Exception err = null;
      var t = new Thread(() => { try { using (var s = new System.Speech.Synthesis.SpeechSynthesizer()) using (var ms = new MemoryStream()) {
            s.SelectVoice(name); s.Rate = Math.Max(-10, Math.Min(10, (int)Math.Round(ratePct / 10.0))); s.Volume = 100;
            s.SetOutputToAudioStream(ms, new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, System.Speech.AudioFormat.AudioBitsPerSample.Sixteen, System.Speech.AudioFormat.AudioChannel.Mono));
            s.Speak(text); s.SetOutputToNull(); result = Wav(ms.ToArray(), 16000); } } catch (Exception e) { err = e; } });
      t.IsBackground = true; t.Start(); if (!t.Join(20000)) throw new Exception("offline voice timed out");
      if (err != null) throw err; used = name; return result; }
    public static byte[] Silence(int ms) { return Wav(new byte[16000 * 2 * ms / 1000], 16000); }
    static byte[] Wav(byte[] pcm, int rate) {
      var ms = new MemoryStream(); var w = new BinaryWriter(ms);
      w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Length); w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
      w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length); w.Write(pcm); w.Flush(); return ms.ToArray(); }
  }
}
