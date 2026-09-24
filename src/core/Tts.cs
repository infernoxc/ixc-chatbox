// IXC Core - chat text-to-speech. Decides WHAT to read on the PC side (not in a web page), so it works whenever IXC runs,
// no matter which chat pages are open. Audio plays in the OBS source /chat/tts.html, so viewers hear it on the tracks you pick.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
//   chat event -> dedup -> filters (off / own account / bots / never-speak / blocked words / commands / rate limits) -> cleanup -> queue
//   queue -> (drop stale) -> synthesize (Edge neural voice, or the offline Windows voice as fallback) -> tts.html plays it -> next
using System;
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
    public int N, Priority; public string Text, Said, User, Platform, VoiceKey, Status = "queued", Engine = "", Error = ""; public DateTime At = DateTime.Now, PlayedAt;
    public byte[] Audio; public string Mime = "audio/mpeg"; public Task Synth; public int SynthMs;
    public Dictionary<string, object> View() { return J.D("n", N, "text", Said, "user", User, "platform", Platform, "voice", VoiceKey, "status", Status, "engine", Engine, "priority", Priority,
      "at", At.ToString("HH:mm:ss"), "age", (int)(DateTime.Now - At).TotalSeconds, "error", Error, "replayable", true); }
  }
  public class Voice { public string Key, Label, Name, Engine = "edge"; public int Rate, Pitch; public bool IsNew; }

  public static class Tts {
    static readonly object L = new object();
    static readonly List<TtsItem> Queue = new List<TtsItem>(); static readonly LinkedList<TtsItem> History = new LinkedList<TtsItem>();
    static readonly LinkedList<Dictionary<string, object>> Skipped = new LinkedList<Dictionary<string, object>>();
    static TtsItem current; static int nextN; static DateTime currentUntil; static Timer watchdog, stateT;
    public static int Played, Failed, Enqueued; static long synthMsTotal; static int synthCount;
    public static readonly Dictionary<string, int> FilterCounts = new Dictionary<string, int>();
    static readonly LinkedList<DateTime> recentTimes = new LinkedList<DateTime>(); static readonly Dictionary<string, DateTime> lastByUser = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    static readonly LinkedList<KeyValuePair<string, DateTime>> recentTexts = new LinkedList<KeyValuePair<string, DateTime>>();
    static string edgeError = ""; static int edgeFails; static DateTime edgeOffUntil = DateTime.MinValue; static string lastEngine = "";

    // ---------- settings (config.json "tts"; the dock / phone change them live) ----------
    static readonly string[] DefaultBots = { "streamerbot", "streamer.bot", "streamer_bot", "nightbot", "streamelements", "moobot", "fossabot", "wizebot", "botrix", "botrixoficial", "kickbot", "sery_bot", "soundalerts", "streamlabs", "commanderroot" };
    public static bool On { get { return J.Bool(Cfg.D, "tts.on", false); } }   // the dock's TTS ON/OFF switch (v1 kept it per page)
    static string S(string k, string d) { return J.Str(Cfg.D, "tts." + k, d); }
    static int I(string k, int d) { return J.Int(Cfg.D, "tts." + k, d); }
    static bool B(string k, bool d) { return J.Bool(Cfg.D, "tts." + k, d); }
    public static Dictionary<string, object> Settings() {
      return J.D("enabled", On, "voice", S("voice", "in-male"), "readMode", S("readMode", "name"), "speed", I("speed", 0), "pitch", I("pitch", 0), "volume", I("volume", 100),
        "maxChars", I("maxChars", 200), "queueMax", I("queueMax", 6), "staleSec", I("staleSec", 60), "perMinute", I("perMinute", 20), "userCooldownSec", I("userCooldownSec", 3),
        "ignoreOwn", B("ignoreOwn", true), "ignoreBots", B("ignoreBots", true), "readEmotes", B("readEmotes", false), "readEmoji", B("readEmoji", false), "readLinks", B("readLinks", false),
        "neverSpeak", J.List(Cfg.D, "tts.neverSpeak"), "blockedWords", J.List(Cfg.D, "tts.blockedWords"), "botNames", J.List(Cfg.D, "tts.botNames"), "ownNames", J.List(Cfg.D, "tts.ownNames"),
        "fallback", B("fallback", true)); }
    static readonly Dictionary<string, string> Kinds = new Dictionary<string, string> { { "enabled", "bool" }, { "voice", "voice" }, { "readMode", "mode" }, { "speed", "int:-50:100" }, { "pitch", "int:-30:30" },
      { "volume", "int:0:100" }, { "maxChars", "int:40:500" }, { "queueMax", "int:1:50" }, { "staleSec", "int:10:600" }, { "perMinute", "int:1:120" }, { "userCooldownSec", "int:0:120" },
      { "ignoreOwn", "bool" }, { "ignoreBots", "bool" }, { "readEmotes", "bool" }, { "readEmoji", "bool" }, { "readLinks", "bool" }, { "fallback", "bool" },
      { "neverSpeak", "list" }, { "blockedWords", "list" }, { "botNames", "list" }, { "ownNames", "list" } };
    public static string Apply(Dictionary<string, object> patch) {
      if (patch == null) return "nothing to change";
      foreach (var kv in patch) { string kind; if (!Kinds.TryGetValue(kv.Key, out kind)) return "unknown setting: " + kv.Key; var v = kv.Value;
        if (kind == "bool") { if (!(v is bool)) return kv.Key + " must be true/false"; }
        else if (kind.StartsWith("int")) { var p = kind.Split(':'); int n; try { n = Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return kv.Key + " must be a number"; } v = Math.Max(int.Parse(p[1]), Math.Min(int.Parse(p[2]), n)); }
        else if (kind == "voice") { if (!Voices().ContainsKey(Convert.ToString(v))) return "unknown voice: " + v; }
        else if (kind == "mode") { var s = Convert.ToString(v); if (s != "all" && s != "name" && s != "tts") return "readMode must be all, name or tts"; }
        else if (kind == "list") { var a = v as System.Collections.ArrayList; if (a == null) { var s = v as string; if (s == null) return kv.Key + " must be a list"; a = new System.Collections.ArrayList(s.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries)); }
          v = a.Cast<object>().Select(x => Convert.ToString(x).Trim().TrimStart('@')).Where(x => x.Length > 0 && x.Length < 60).Distinct(StringComparer.OrdinalIgnoreCase).Take(300).ToList(); }
        Cfg.Set("tts." + (kv.Key == "enabled" ? "on" : kv.Key), v);
        if (kv.Key == "enabled" && !(bool)v) { lock (L) { foreach (var it in Queue) { it.Status = "cancelled (TTS off)"; AddHistory(it); } Queue.Clear(); } StopPlayback("TTS turned off"); }
        Diag.Ev("TTS setting " + kv.Key + " = " + (v is List<string> ? string.Join(",", (List<string>)v) : Convert.ToString(v, CultureInfo.InvariantCulture))); }
      Cfg.Save(); Changed(); return null; }

    // ---------- voices ----------
    static Dictionary<string, Voice> voiceCache; static DateTime voiceAt;
    public static Dictionary<string, Voice> Voices() {
      if (voiceCache != null && (DateTime.Now - voiceAt).TotalSeconds < 5) return voiceCache;
      var v = new Dictionary<string, Voice>();
      Action<string, string, string, int, int, bool> add = (k, label, name, rate, pitch, isNew) => v[k] = new Voice { Key = k, Label = label, Name = name, Rate = rate, Pitch = pitch, IsNew = isNew };
      add("in-male", "Indian male - Prabhat", "en-IN-PrabhatNeural", 0, 0, false);
      add("in-female", "Indian female - Neerja", "en-IN-NeerjaNeural", 0, 0, false);
      add("in-male-2", "Indian male 2 - Madhur (faster)", "hi-IN-MadhurNeural", 10, 0, true);
      add("in-female-2", "Indian female 2 - Neerja Expressive (faster)", "en-IN-NeerjaExpressiveNeural", 8, 0, true);
      add("us-male", "US male - Andrew", "en-US-AndrewNeural", 0, 0, false);
      add("us-female", "US female - Jenny", "en-US-JennyNeural", 0, 0, false);
      add("jarvis", "Jarvis-style - Ryan (UK)", "en-GB-RyanNeural", 6, -3, false);
      var cv = J.Obj(Cfg.D, "tts.voices");
      if (cv != null) foreach (var kv in cv) { var d = kv.Value as Dictionary<string, object>; if (d == null) continue; var name = J.Str(d, "voice", ""); if (name.Length == 0) continue;
        Voice old; v.TryGetValue(kv.Key, out old);
        v[kv.Key] = new Voice { Key = kv.Key, Name = name, Label = J.Str(d, "label", old != null && old.Name == name ? old.Label : kv.Key + " - " + name), Rate = ParseNum(J.Str(d, "rate", old != null && old.Name == name ? old.Rate + "%" : "0")), Pitch = ParseNum(J.Str(d, "pitch", old != null && old.Name == name ? old.Pitch + "Hz" : "0")), IsNew = old != null && old.IsNew }; }
      v["local"] = new Voice { Key = "local", Label = "Windows voice (offline, " + (Sapi.Best(null) ?? "none installed") + ")", Name = "", Engine = "sapi" };
      voiceCache = v; voiceAt = DateTime.Now; return v; }
    static int ParseNum(string s) { var m = Regex.Match(s ?? "", "[-+]?\\d+"); return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : 0; }

    public static void Init() {
      watchdog = new Timer(_ => { bool stuck; lock (L) stuck = current != null && DateTime.Now > currentUntil; if (stuck) { Diag.Ev("TTS: player did not report back - moving on"); Finish(null, "played (no confirmation)"); } }, null, 1000, 1000);
      stateT = new Timer(_ => Hub.Publish("tts", StateMsg()));
    }
    static void Changed() { stateT.Change(150, Timeout.Infinite); }

    // ---------- chat -> queue ----------
    static void Skip(ChatMsg c, string reason, string text) {
      lock (FilterCounts) { int n; FilterCounts.TryGetValue(reason, out n); FilterCounts[reason] = n + 1; }
      lock (L) { Skipped.AddLast(J.D("at", DateTime.Now.ToString("HH:mm:ss"), "user", c == null ? "" : c.Name, "platform", c == null ? "" : c.Platform, "reason", reason, "text", text == null ? "" : (text.Length > 80 ? text.Substring(0, 80) + "..." : text))); while (Skipped.Count > 30) Skipped.RemoveFirst(); }
      Changed(); }
    static bool InList(string name, IEnumerable<string> list) { if (string.IsNullOrEmpty(name)) return false; var n = Norm(name); return list.Any(x => Norm(x) == n); }
    static string Norm(string s) { return Regex.Replace((s ?? "").ToLowerInvariant().TrimStart('@'), "[^\\p{L}\\p{N}]", ""); }
    public static void OnChat(ChatMsg c) {
      if (!On) { lock (FilterCounts) { int n; FilterCounts.TryGetValue("TTS is off", out n); FilterCounts["TTS is off"] = n + 1; } return; }
      var text = c.Text.Trim();
      // same person, same words on several platforms at once (your "ALL" replies, multistream mirrors) = say it once
      var key = Norm(c.Name) + "|" + Norm(text);
      lock (recentTexts) { while (recentTexts.Count > 0 && (DateTime.Now - recentTexts.First.Value.Value).TotalSeconds > 15) recentTexts.RemoveFirst();
        if (recentTexts.Any(x => x.Key == key)) { Skip(c, "mirrored on another platform", text); return; }
        if (text.Length > 12 && recentTexts.Count(x => x.Key.EndsWith("|" + Norm(text))) >= 2) { Skip(c, "copy-paste spam", text); return; }
        recentTexts.AddLast(new KeyValuePair<string, DateTime>(key, DateTime.Now)); }
      if (B("ignoreOwn", true) && (c.Broadcaster || c.Me || InList(c.Name, Chat.Own()) || InList(c.Login, Chat.Own()))) { Skip(c, "your own account", text); return; }
      if (B("ignoreBots", true) && (InList(c.Name, DefaultBots.Concat(J.List(Cfg.D, "tts.botNames"))) || InList(c.Login, J.List(Cfg.D, "tts.botNames")))) { Skip(c, "bot account", text); return; }
      if (InList(c.Name, J.List(Cfg.D, "tts.neverSpeak")) || InList(c.Login, J.List(Cfg.D, "tts.neverSpeak"))) { Skip(c, "never-speak list", text); return; }
      var low = text.ToLowerInvariant(); var bw = J.List(Cfg.D, "tts.blockedWords").FirstOrDefault(w => w.Length > 0 && low.Contains(w.ToLowerInvariant()));
      if (bw != null) { Skip(c, "blocked word", text); return; }
      int prio = 0; var mode = S("readMode", "name");
      var tm = Regex.Match(text, "^!tts\\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
      if (tm.Success) { text = tm.Groups[1].Value; prio = 1; }
      else if (mode == "tts") { Skip(c, "only !tts messages are read", text); return; }
      else if (text.StartsWith("!")) { Skip(c, "chat command", text); return; }
      // rate limits: per user, and overall per minute (bursts)
      DateTime last; lock (lastByUser) { if (lastByUser.TryGetValue(c.Platform + ":" + c.Login, out last) && (DateTime.Now - last).TotalSeconds < I("userCooldownSec", 3)) { Skip(c, "same user too fast", text); return; } lastByUser[c.Platform + ":" + c.Login] = DateTime.Now; if (lastByUser.Count > 2000) lastByUser.Clear(); }
      lock (recentTimes) { while (recentTimes.Count > 0 && (DateTime.Now - recentTimes.First.Value).TotalSeconds > 60) recentTimes.RemoveFirst(); if (recentTimes.Count >= I("perMinute", 20) && prio == 0) { Skip(c, "busy chat (per-minute limit)", text); return; } recentTimes.AddLast(DateTime.Now); }
      var clean = Clean(text, c); if (clean == null) { Skip(c, "nothing readable (only emotes/links/symbols)", text); return; }
      var said = mode == "name" ? SpeakName(c.Name) + " says " + clean : clean;
      Enqueue(new TtsItem { Text = text, Said = said, User = c.Name, Platform = c.Platform, Priority = prio });
    }
    public static TtsItem Enqueue(TtsItem it) {
      if (string.IsNullOrEmpty(it.VoiceKey)) it.VoiceKey = S("voice", "in-male");
      lock (L) {
        it.N = ++nextN; Enqueued++;
        int max = I("queueMax", 6);
        while (Queue.Count >= max) { var drop = Queue.Where(q => q.Priority <= it.Priority).OrderBy(q => q.Priority).ThenBy(q => q.At).FirstOrDefault();
          if (drop == null) { it.Status = "dropped (queue full)"; AddHistory(it); Skip(null, "queue full", it.Said); return it; }
          Queue.Remove(drop); drop.Status = "dropped (queue full)"; AddHistory(drop); lock (FilterCounts) { int n; FilterCounts.TryGetValue("queue full (oldest dropped)", out n); FilterCounts["queue full (oldest dropped)"] = n + 1; } }
        Queue.Add(it); }
      Diag.Ev("TTS queued #" + it.N + " (" + it.Platform + " " + it.User + ")");
      Changed(); Pump(); return it; }

    // ---------- text cleanup ----------
    static readonly Regex UrlRe = new Regex("(https?://|www\\.)\\S+|\\b[\\w-]+\\.(com|net|org|gg|tv|io|in|ly|me|co|app|xyz|link|live)(/\\S*)?\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static string SpeakName(string n) { n = Regex.Replace(n ?? "someone", "[_\\-.]+", " "); n = Regex.Replace(n, "(?<=\\p{L})\\d{3,}$", ""); n = Regex.Replace(n, "(?<=\\p{Ll})(?=\\p{Lu})", " "); n = n.Trim(); return n.Length == 0 ? "someone" : n; }
    public static string Clean(string t, ChatMsg c) {
      if (c != null && !B("readEmotes", false) && c.EmoteSpans.Count > 0 && c.Text.Trim() == t) {   // Twitch/Kick emote positions (Unicode code points)
        var cps = new List<string>(); for (int i = 0; i < t.Length; i++) { if (char.IsHighSurrogate(t[i]) && i + 1 < t.Length) { cps.Add(t.Substring(i, 2)); i++; } else cps.Add(t[i].ToString()); }
        foreach (var sp in c.EmoteSpans) { if (sp.Key < 0 || sp.Value >= cps.Count || sp.Value < sp.Key) continue; var word = string.Concat(cps.Skip(sp.Key).Take(sp.Value - sp.Key + 1));
          if (c.EmoteNames.Count > 0 && !c.EmoteNames.Contains(word)) continue;   // positions don't match the text: rely on the emote names below
          for (int i = sp.Key; i <= sp.Value; i++) cps[i] = i == sp.Key ? " " : ""; }
        t = string.Concat(cps); }
      t = Regex.Replace(t, "\\[emote:\\d+:([^\\]]*)\\]", B("readEmotes", false) ? " $1 " : " ");
      if (c != null && !B("readEmotes", false) && c.EmoteNames.Count > 0) t = string.Join(" ", t.Split(' ').Where(w => !c.EmoteNames.Contains(w)));
      t = UrlRe.Replace(t, B("readLinks", false) ? "$0" : " link ");
      t = Regex.Replace(t, "@([\\w.]+)", m => SpeakName(m.Groups[1].Value));
      if (!B("readEmoji", false)) { t = Regex.Replace(t, "[\\uD800-\\uDBFF][\\uDC00-\\uDFFF]", " "); t = Regex.Replace(t, "[\\u2600-\\u27BF\\uFE0F\\u200D\\u2B00-\\u2BFF\\u2300-\\u23FF]", " "); }
      t = Regex.Replace(t, "(.)\\1{3,}", "$1$1");                                       // "noooooo" -> "noo", "!!!!!" -> "!!"
      t = Regex.Replace(t, "\\b(\\w+)(\\W+\\1\\b){2,}", "$1 $1", RegexOptions.IgnoreCase);   // "lol lol lol lol" -> "lol lol"
      t = Regex.Replace(t, "\\b\\d{7,}\\b", " a long number ");
      t = Regex.Replace(t, "\\b(\\d{1,6})(k)\\b", "$1 thousand", RegexOptions.IgnoreCase);
      int letters = t.Count(char.IsLetter), upper = t.Count(char.IsUpper); if (letters >= 8 && upper > letters * 0.7) t = t.ToLowerInvariant();   // shouting reads as spelled-out letters
      t = Regex.Replace(t, "\\b\\p{Lu}{5,}\\b", m => m.Value.ToLowerInvariant());                       // single shouted words too (short ones like GG / LOL stay)
      t = Regex.Replace(t, "[<>{}\\[\\]|\\\\^~`*_#=]+", " ");
      t = Regex.Replace(t, "\\s+", " ").Trim();
      if (!t.Any(char.IsLetterOrDigit) || Regex.IsMatch(t, "^(link\\s*)+$")) return null;
      int max = I("maxChars", 200); if (t.Length > max) { var cut = t.LastIndexOf(' ', max); t = t.Substring(0, cut > max / 2 ? cut : max); }
      return t; }

    // ---------- playback loop ----------
    static int busy;
    public static void Pump() {
      if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
      Task.Run(() => {
        try {
          TtsItem it = null;
          lock (L) {
            if (current != null || Hub.Count("tts") == 0) return;
            while (Queue.Count > 0) { var best = Queue.OrderByDescending(q => q.Priority).ThenBy(q => q.At).First(); Queue.Remove(best);
              if (best.Priority < 2 && (DateTime.Now - best.At).TotalSeconds > I("staleSec", 60)) { best.Status = "dropped (too old)"; AddHistory(best); lock (FilterCounts) { int n; FilterCounts.TryGetValue("too old when its turn came", out n); FilterCounts["too old when its turn came"] = n + 1; } continue; }
              it = best; break; }
            if (it == null) return;
            current = it; it.Status = "preparing"; currentUntil = DateTime.Now.AddSeconds(40); }
          Changed();
          EnsureSynth(it).Wait();
          lock (L) { var nx = Queue.OrderByDescending(q => q.Priority).ThenBy(q => q.At).FirstOrDefault(); if (nx != null) EnsureSynth(nx); }   // prepare the next one while this plays
          if (it.Audio == null) { Failed++; Finish(it, "failed: " + it.Error); return; }
          lock (L) { if (current != it) return; it.Status = "playing"; it.PlayedAt = DateTime.Now; currentUntil = DateTime.Now.AddSeconds(8 + it.Audio.Length / (it.Mime == "audio/wav" ? 32000.0 : 6000.0)); }
          Hub.Publish("tts.play", J.D("type", "tts.play", "n", it.N, "url", "/api/tts/audio?n=" + it.N, "volume", I("volume", 100)));
          Diag.Ev("TTS playing #" + it.N + " via " + it.Engine); Changed();
        } catch (Exception e) { Diag.Err("TTS: " + e.Message); lock (L) current = null; }
        finally { Interlocked.Exchange(ref busy, 0); bool again; lock (L) again = current == null && Queue.Count > 0 && Hub.Count("tts") > 0; if (again) Pump(); }
      }); }
    static void Finish(TtsItem it, string status) {
      lock (L) { if (it == null) it = current; if (it == null) return; if (current == it) current = null; it.Status = status; if (status.StartsWith("played")) Played++; AddHistory(it); }
      Changed(); Pump(); }
    static void AddHistory(TtsItem it) { lock (L) { History.Remove(it); History.AddLast(it); while (History.Count > 40) History.RemoveFirst(); int k = 0; foreach (var h in History.Reverse()) if (++k > 5 && h != current) { h.Audio = null; h.Synth = null; } } }   // keep audio only for the last few (replay)
    static void StopPlayback(string why) { Hub.Publish("tts.play", J.D("type", "tts.stop")); TtsItem c; lock (L) c = current; if (c != null) Finish(c, why); }

    static Task EnsureSynth(TtsItem it) {
      lock (it) { if (it.Audio != null) return Task.FromResult(0); if (it.Synth == null) it.Synth = Task.Run(() => Synthesize(it)); return it.Synth; } }
    static void Synthesize(TtsItem it) {
      var sw = System.Diagnostics.Stopwatch.StartNew(); Voice v; if (!Voices().TryGetValue(it.VoiceKey ?? "", out v)) v = Voices()["in-male"];
      int rate = v.Rate + I("speed", 0), pitch = v.Pitch + I("pitch", 0);
      if (v.Engine == "edge" && DateTime.Now > edgeOffUntil) {
        for (int a = 0; a < 2 && it.Audio == null; a++) {
          try { var b = EdgeTts.Synth(it.Said, v.Name, rate, pitch); if (b != null && b.Length > 800) { it.Audio = b; it.Mime = "audio/mpeg"; it.Engine = "Edge neural (" + v.Name + ")"; edgeFails = 0; edgeError = ""; } else edgeError = "empty audio"; }
          catch (Exception e) { edgeError = e.Message; } }
        if (it.Audio == null && ++edgeFails >= 2) { edgeOffUntil = DateTime.Now.AddSeconds(60); Diag.Err("Edge neural voice failed (" + edgeError + ") - using the offline Windows voice for 60 s"); }
      }
      if (it.Audio == null && (v.Engine == "sapi" || B("fallback", true))) {
        try { string used; it.Audio = Sapi.Synth(it.Said, v.Engine == "sapi" ? null : v.Name, rate, out used); it.Mime = "audio/wav"; it.Engine = "Windows offline (" + used + ")"; }
        catch (Exception e) { it.Error = "offline voice: " + e.Message; } }
      if (it.Audio == null && it.Error == "") it.Error = edgeError.Length > 0 ? edgeError : "no voice available";
      it.SynthMs = (int)sw.ElapsedMilliseconds; if (it.Audio != null) { Interlocked.Add(ref synthMsTotal, it.SynthMs); Interlocked.Increment(ref synthCount); lastEngine = it.Engine; } }

    // ---------- state for docks / phones / diagnostics ----------
    static Dictionary<string, object> StateMsg() {
      lock (L) return J.D("type", "tts.state", "settings", Settings(), "current", current == null ? null : current.View(),
        "queue", Queue.OrderByDescending(q => q.Priority).ThenBy(q => q.At).Select(q => q.View()).ToList(), "history", History.Reverse().Take(15).Select(h => h.View()).ToList(),
        "skipped", Skipped.Reverse().Take(12).ToList(), "players", Hub.Count("tts"), "engine", EngineStatus()); }
    static string EngineStatus() { return DateTime.Now < edgeOffUntil ? "offline Windows voice (neural voice failed: " + edgeError + ")" : lastEngine.Length > 0 ? lastEngine : "Edge neural (ready)"; }
    public static Dictionary<string, object> DiagInfo() {
      lock (L) return J.D("enabled", On, "voice", S("voice", "in-male"), "engine", EngineStatus(), "neuralError", edgeError, "queue", Queue.Count, "current", current == null ? null : current.Said,
        "played", Played, "failed", Failed, "queued", Enqueued, "avgSynthMs", synthCount == 0 ? 0 : (int)(synthMsTotal / synthCount), "ttsSourcesConnected", Hub.Count("tts"),
        "notRead", new Dictionary<string, int>(FilterCounts), "offlineVoices", Sapi.List()); }
    public static List<object> VoiceList() { return Voices().Values.Select(v => (object)J.D("key", v.Key, "label", v.Label, "engine", v.Engine, "isNew", v.IsNew)).ToList(); }

    public static TtsItem Replay(int n) { TtsItem h; lock (L) h = History.FirstOrDefault(x => x.N == n); if (h == null) return null;
      return Enqueue(new TtsItem { Text = h.Text, Said = h.Said, User = h.User, Platform = h.Platform, VoiceKey = h.VoiceKey, Priority = 2, Audio = h.Audio, Mime = h.Mime, Engine = h.Engine }); }
    public static void Clear() { lock (L) { foreach (var it in Queue) { it.Status = "cleared"; AddHistory(it); } Queue.Clear(); } Changed(); }
    public static void SkipNow() { StopPlayback("skipped"); }
    public static TtsItem Say(string text, string voice, int prio) {
      text = (text ?? "").Trim(); if (text.Length == 0) return null; var clean = Clean(text, null) ?? text; if (clean.Length > 500) clean = clean.Substring(0, 500);
      return Enqueue(new TtsItem { Text = text, Said = clean, User = "you", Platform = "test", VoiceKey = voice != null && Voices().ContainsKey(voice) ? voice : null, Priority = prio }); }

    // ---------- API ----------
    public static bool Api(HttpListenerContext ctx, string path, string m, System.Collections.Specialized.NameValueCollection q) {
      if (!path.StartsWith("/api/tts") && path != "/api/say" && path != "/api/says" && path != "/api/skip" && path != "/api/tts.mp3") return false;
      if (path == "/api/tts/audio" || path == "/api/tts.mp3") { int n; int.TryParse(q["n"], out n); TtsItem it; lock (L) it = current != null && current.N == n ? current : History.Concat(Queue).FirstOrDefault(x => x.N == n);
        if (it == null) { Http.Send(ctx, 404, "no such message", "text/plain"); return true; } EnsureSynth(it).Wait(20000);
        if (it.Audio == null) Http.Send(ctx, 502, "tts failed: " + it.Error, "text/plain"); else Http.SendBytes(ctx, 200, it.Audio, it.Mime); return true; }
      if (path == "/api/tts/state") { Http.Json(ctx, StateMsg()); return true; }
      if (path == "/api/tts/settings") { if (m == "POST") { var err = Apply(J.Parse(Http.Body(ctx))); if (err != null) { Http.Json(ctx, 400, J.D("error", err)); return true; } }
        Http.Json(ctx, J.D("settings", Settings(), "voices", VoiceList())); return true; }
      if ((path == "/api/tts/say" || path == "/api/say") && m == "POST") { var b = J.Parse(Http.Body(ctx)); var it = Say(J.Str(b, "text", ""), J.Str(b, "voice", null), 2); Http.Json(ctx, J.D("n", it == null ? 0 : it.N, "players", Hub.Count("tts"))); return true; }
      if ((path == "/api/tts/skip" || path == "/api/skip") && m == "POST") { SkipNow(); Http.Json(ctx, J.D("ok", true)); return true; }
      if (path == "/api/tts/clear" && m == "POST") { Clear(); Http.Json(ctx, J.D("ok", true)); return true; }
      if (path == "/api/tts/replay" && m == "POST") { var it = Replay(J.Int(J.Parse(Http.Body(ctx)), "n", 0)); Http.Json(ctx, it == null ? 404 : 200, J.D("n", it == null ? 0 : it.N)); return true; }
      if (path == "/api/says") { Http.Json(ctx, J.D("seq", 0, "skip", 0, "err", "", "items", new object[0], "note", "update the IXC ChatBox TTS source")); return true; }   // v1 page (it reloads into v2)
      return false; }
    public static void OnWs(Client c, string type, Dictionary<string, object> msg) {
      switch (type) {
        case "hello": if (c.Role == "tts") { Diag.Ev("TTS source connected"); Pump(); } if (c.Topics.Contains("tts")) { Hub.Send(c, StateMsg()); Hub.Send(c, J.D("type", "tts.voices", "voices", VoiceList())); } break;
        case "_closed": if (c.Role == "tts") Diag.Ev("TTS source disconnected"); break;
        case "tts.done": { int n = J.Int(msg, "n", -1); bool ok = J.Bool(msg, "ok", true); TtsItem cur; lock (L) cur = current; if (cur != null && cur.N == n) { if (!ok) Failed++; Finish(cur, ok ? "played" : "failed: player could not play it (" + J.Str(msg, "error", "?") + ")"); } break; }
        case "tts.set": { var err = Apply(J.Obj(msg, "patch")); if (err != null) Hub.Send(c, J.D("type", "error", "error", err)); break; }
        case "tts.say": { var it = Say(J.Str(msg, "text", ""), J.Str(msg, "voice", null), 2); if (it != null && Hub.Count("tts") == 0) Hub.Send(c, J.D("type", "error", "error", "No TTS source is open in OBS (add /chat/tts.html as a browser source) - queued until one connects.")); break; }
        case "tts.test": { Voice v; var key = J.Str(msg, "voice", S("voice", "in-male")); Voices().TryGetValue(key, out v); Say(key == "jarvis" ? "Good evening. All systems are online, and chat is connected." : "Hi, this is " + (v == null ? "the chat voice" : v.Label.Split('-')[0].Trim().ToLowerInvariant() + " voice") + ". Chat text to speech is working.", key, 2); if (Hub.Count("tts") == 0) Hub.Send(c, J.D("type", "error", "error", "No TTS source is open in OBS - add http://localhost:" + Program.Port + "/chat/tts.html as a browser source.")); break; }
        case "tts.skip": SkipNow(); break;
        case "tts.clear": Clear(); break;
        case "tts.replay": Replay(J.Int(msg, "n", 0)); break;
      } }
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
    static List<string> cache;
    public static List<string> List() {
      if (cache != null) return cache;
      try { using (var s = new System.Speech.Synthesis.SpeechSynthesizer()) cache = s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name + " (" + v.VoiceInfo.Culture.Name + ", " + v.VoiceInfo.Gender + ")").ToList(); }
      catch { cache = new List<string>(); } return cache; }
    // best local voice for a neural voice: Indian English (Heera/Ravi, if the Windows English (India) voice pack is installed) with the same gender, else any English
    public static string Best(string neural) {
      try { using (var s = new System.Speech.Synthesis.SpeechSynthesizer()) {
        var vs = s.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo).ToList(); if (vs.Count == 0) return null;
        bool female = neural != null && Regex.IsMatch(neural, "Neerja|Swara|Jenny|Aria|Female|Zira|Heera", RegexOptions.IgnoreCase);
        var g = neural == null ? (System.Speech.Synthesis.VoiceGender?)null : female ? System.Speech.Synthesis.VoiceGender.Female : System.Speech.Synthesis.VoiceGender.Male;
        var pick = vs.FirstOrDefault(v => v.Culture.Name == "en-IN" && (g == null || v.Gender == g)) ?? vs.FirstOrDefault(v => v.Culture.Name == "en-IN")
                ?? vs.FirstOrDefault(v => v.Culture.Name.StartsWith("en") && (g == null || v.Gender == g)) ?? vs.First();
        return pick.Name; } } catch { return null; } }
    public static byte[] Synth(string text, string neural, int ratePct, out string used) {
      byte[] result = null; string u = null; Exception err = null;
      var t = new Thread(() => { try { using (var s = new System.Speech.Synthesis.SpeechSynthesizer()) using (var ms = new MemoryStream()) {
            u = Best(neural); if (u == null) throw new Exception("no Windows voices installed"); s.SelectVoice(u);
            s.Rate = Math.Max(-10, Math.Min(10, (int)Math.Round(ratePct / 10.0))); s.Volume = 100;
            s.SetOutputToAudioStream(ms, new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, System.Speech.AudioFormat.AudioBitsPerSample.Sixteen, System.Speech.AudioFormat.AudioChannel.Mono));
            s.Speak(text); s.SetOutputToNull(); result = Wav(ms.ToArray(), 16000); } } catch (Exception e) { err = e; } });
      t.IsBackground = true; t.Start(); if (!t.Join(20000)) throw new Exception("offline voice timed out");
      if (err != null) throw err; used = u; return result; }
    static byte[] Wav(byte[] pcm, int rate) {
      var ms = new MemoryStream(); var w = new BinaryWriter(ms);
      w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Length); w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
      w.Write(Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length); w.Write(pcm); w.Flush(); return ms.ToArray(); }
  }
}
