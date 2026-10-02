// IXC Core - chat commands: built-in ones (!np, !queue, !skip, !uptime, !wrongsong, !ttsskip ...) and your own
// (!discord -> "Join us at ..."), each with a permission, cooldowns and platforms. Song requests (!sr) are in SongRequests.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace IXC {
  public static class Perm {
    // can this chatter use something that needs "role"? returns null when allowed, otherwise the reason
    public static string Check(ChatMsg m, string role) {
      int need = Array.IndexOf(Settings.Roles, role); if (need <= 0) return null;
      if (m.Level >= need) return null;
      if (role == "follower") {
        if (m.Follower == null && m.Platform == "twitch") m.Follower = Platforms.Twitch.IsFollower(m.UserId);
        if (m.Follower == true) return null;
        if (m.Follower == null) return "followers only (IXC can only check followers on Twitch, signed in)";
        return "followers only"; }
      return role == "subscriber" ? "subscribers only" : role == "vip" ? "VIPs and moderators only" : role == "moderator" ? "moderators only" : "the streamer only"; }
  }

  public static class Commands {
    public static readonly string[] Builtins = { "np", "queue", "skip", "uptime", "wrongsong", "ttsskip", "ttson", "ttsoff", "pause", "resume", "viewers", "songs" };
    static readonly Dictionary<string, DateTime> lastUse = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    public static int Used;

    public static List<Dictionary<string, object>> DefaultList() {
      return new List<Dictionary<string, object>> {
        Cmd("!np", "Now playing: {song}", "everyone", 10, "np"), Cmd("!queue", "Up next: {queue}", "everyone", 15, "queue"),
        Cmd("!skip", "Skipped: {song}", "moderator", 0, "skip"), Cmd("!uptime", "Live for {uptime}", "everyone", 15, "uptime"),
        Cmd("!wrongsong", "", "everyone", 0, "wrongsong"), Cmd("!ttsskip", "", "moderator", 0, "ttsskip"),
        Cmd("!ttson", "Chat voice is on", "moderator", 0, "ttson"), Cmd("!ttsoff", "Chat voice is off", "moderator", 0, "ttsoff"),
        Cmd("!viewers", "{viewers} watching right now", "everyone", 30, "viewers") }; }
    static Dictionary<string, object> Cmd(string t, string resp, string perm, int cd, string builtin) {
      return J.D("id", builtin ?? U.Token(5), "trigger", t, "aliases", new List<string>(), "response", resp, "permission", perm, "cooldownSec", cd, "userCooldownSec", 0, "platforms", new List<string>(), "enabled", true, "builtin", builtin); }
    public static List<Dictionary<string, object>> List() {
      var v = Cfg.Get("commands.list") as IEnumerable; if (v == null || v is string) return DefaultList();
      string err; var l = ValidateList(v, out err) as List<Dictionary<string, object>>; return l ?? DefaultList(); }
    public static List<string> Triggers() { var l = new List<string>(); foreach (var c in List()) if (J.Bool(c, "enabled", true)) { l.Add(J.Str(c, "trigger", "")); l.AddRange(J.List(c, "aliases")); } l.AddRange(Settings.List("songRequests.commands")); return l; }

    public static object ValidateList(object v, out string err) {
      err = null; var src = v as IEnumerable; if (src == null || v is string || v is Dictionary<string, object>) { err = "commands must be a list"; return null; }
      var outp = new List<Dictionary<string, object>>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var o in src) { var d = o as Dictionary<string, object>; if (d == null) continue; if (outp.Count >= 200) { err = "at most 200 commands"; return null; }
        var trig = Trig(J.Str(d, "trigger", "")); if (trig == null) { err = "a command needs a name like !discord (letters, numbers, - or _)"; return null; }
        if (!seen.Add(trig)) { err = trig + " is used twice"; return null; }
        var aliases = J.List(d, "aliases").Select(Trig).Where(x => x != null && seen.Add(x)).Take(10).ToList();
        var builtin = J.Str(d, "builtin", null); if (builtin != null && !Builtins.Contains(builtin)) builtin = null;
        var resp = J.Str(d, "response", "").Trim(); if (resp.Length > 450) resp = resp.Substring(0, 450); resp = Regex.Replace(resp, "[\\r\\n]+", " ");
        if (builtin == null && resp.Length == 0) { err = trig + " needs a response"; return null; }
        var perm = J.Str(d, "permission", "everyone"); if (!Settings.Roles.Contains(perm)) perm = "everyone";
        var plats = J.List(d, "platforms").Where(x => Settings.PlatformIds.Contains(x)).Distinct().ToList();
        outp.Add(J.D("id", Regex.IsMatch(J.Str(d, "id", ""), "^[\\w-]{1,20}$") ? J.Str(d, "id", "") : U.Token(5), "trigger", trig, "aliases", aliases, "response", resp, "permission", perm,
          "cooldownSec", Math.Max(0, Math.Min(3600, J.Int(d, "cooldownSec", 0))), "userCooldownSec", Math.Max(0, Math.Min(3600, J.Int(d, "userCooldownSec", 0))),
          "platforms", plats, "enabled", J.Bool(d, "enabled", true), "builtin", builtin)); }
      foreach (var sr in Settings.List("songRequests.commands")) if (seen.Contains(sr.StartsWith("!") ? sr : "!" + sr)) { err = sr + " is already the song request command"; return null; }
      return outp; }
    static string Trig(string t) { t = (t ?? "").Trim().ToLowerInvariant(); if (!t.StartsWith("!")) t = "!" + t; return Regex.IsMatch(t, "^![\\p{L}\\p{N}_-]{1,30}$") ? t : null; }

    // returns true when the message was a command (TTS doesn't read it)
    public static bool Handle(ChatMsg m) {
      var text = (m.Text ?? "").Trim(); if (!text.StartsWith("!") || text.Length < 2) return false;
      var sp = text.IndexOf(' '); var word = (sp < 0 ? text : text.Substring(0, sp)).ToLowerInvariant(); var args = sp < 0 ? "" : text.Substring(sp + 1).Trim();
      if (Settings.List("songRequests.commands").Any(c => (c.StartsWith("!") ? c : "!" + c).Equals(word, StringComparison.OrdinalIgnoreCase))) { SongRequests.Handle(m, args); return true; }
      if (!Settings.Bool("commands.enabled")) return false;
      var cmd = List().FirstOrDefault(c => J.Bool(c, "enabled", true) && (J.Str(c, "trigger", "") == word || J.List(c, "aliases").Contains(word)));
      if (cmd == null) return word == "!tts" ? false : true;   // unknown !commands (other bots) are still not read out
      var plats = J.List(cmd, "platforms"); if (plats.Count > 0 && !plats.Contains(m.Platform)) return true;
      var why = Perm.Check(m, J.Str(cmd, "permission", "everyone")); if (why != null) { Log.Debug("chat", word + " refused for " + m.Name + ": " + why); return true; }
      var id = J.Str(cmd, "id", word); var now = DateTime.Now;
      if (!m.Mod && !m.Broadcaster) lock (lastUse) {
        DateTime t; int cd = J.Int(cmd, "cooldownSec", 0), ucd = J.Int(cmd, "userCooldownSec", 0);
        if (cd > 0 && lastUse.TryGetValue(id, out t) && (now - t).TotalSeconds < cd) return true;
        if (ucd > 0 && lastUse.TryGetValue(id + "|" + m.Key, out t) && (now - t).TotalSeconds < ucd) return true;
        lastUse[id] = now; lastUse[id + "|" + m.Key] = now; if (lastUse.Count > 5000) lastUse.Clear(); }
      Used++; lock (counts) { int n; counts.TryGetValue(id, out n); counts[id] = n + 1; }
      string reply = null; var builtin = J.Str(cmd, "builtin", null); var resp = J.Str(cmd, "response", "");
      try { reply = builtin != null ? Builtin(builtin, m, args, resp) : Fill(resp, m, args, id); } catch (Exception e) { Log.Err("chat", word + ": " + e.Message); }
      if (!string.IsNullOrEmpty(reply)) Chat.Reply(m, reply);
      Log.Info("chat", m.Platform + " " + m.Name + " used " + word);
      return true; }
    static string Builtin(string b, ChatMsg m, string args, string resp) {
      switch (b) {
        case "np": return Music.Current() == null ? "Nothing is playing right now" : Fill(resp, m, args, b);
        case "queue": return Fill(resp, m, args, b);
        case "skip": { var cur = Music.Current(); if (cur == null) return "Nothing is playing"; var r = Fill(resp, m, args, b); Music.Command("next", null); return r; }
        case "uptime": return Uptime() == null ? "The stream isn't live right now" : Fill(resp, m, args, b);
        case "wrongsong": return SongRequests.RemoveLast(m);
        case "songs": return Fill(resp, m, args, b);
        case "ttsskip": Tts.SkipNow(); return Fill(resp, m, args, b);
        case "ttson": Settings.Set("tts.on", true); return Fill(resp, m, args, b);
        case "ttsoff": Settings.Set("tts.on", false); return Fill(resp, m, args, b);
        case "pause": Music.Command("pause", null); return Fill(resp, m, args, b);
        case "resume": Music.Command("play", null); return Fill(resp, m, args, b);
        case "viewers": return Viewers.Total() == null ? "Viewer count isn't available right now" : Fill(resp, m, args, b);
      }
      return null; }
    public static string Uptime() {
      var st = Platforms.All.Where(s => s.Configured && s.Viewers.Live == true && s.Viewers.StartedAt != null).Select(s => s.Viewers.StartedAt.Value).OrderBy(x => x).FirstOrDefault();
      if (st == default(DateTime)) return null; var t = DateTime.Now - st; if (t.TotalSeconds < 0) t = TimeSpan.Zero;
      return (t.TotalHours >= 1 ? (int)t.TotalHours + "h " : "") + t.Minutes + "m"; }
    public static string Fill(string tpl, ChatMsg m, string args, string id) {
      if (string.IsNullOrEmpty(tpl)) return "";
      var cur = Music.Current(); int n; lock (counts) counts.TryGetValue(id ?? "", out n);
      var target = args.Split(' ').FirstOrDefault(x => x.Length > 0) ?? (m == null ? "" : m.Name);
      return Regex.Replace(tpl, "\\{(\\w+)\\}", x => {
        switch (x.Groups[1].Value.ToLowerInvariant()) {
          case "user": return m == null ? "" : "@" + m.Name;
          case "name": return m == null ? "" : m.Name;
          case "target": return target.TrimStart('@');
          case "args": return args;
          case "platform": return m == null ? "" : m.Platform;
          case "song": return cur == null ? "nothing" : Music.Describe(cur);
          case "title": return cur == null ? "" : J.Str(cur, "title", "");
          case "artist": return cur == null ? "" : J.Str(cur, "artist", "");
          case "queue": return Music.UpNext(3);
          case "uptime": return Uptime() ?? "offline";
          case "viewers": { var t = Viewers.Total(); return t == null ? "?" : t.Value.ToString("N0", CultureInfo.InvariantCulture); }
          case "count": return n.ToString();
          default: return x.Value; } }); }
  }
}
