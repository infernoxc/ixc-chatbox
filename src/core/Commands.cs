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
    public static readonly string[] Builtins = { "np", "queue", "skip", "uptime", "wrongsong", "ttsskip", "ttson", "ttsoff", "pause", "resume", "viewers", "songs", "commands", "followage", "clip" };
    static readonly Dictionary<string, DateTime> lastUse = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    public static int Used;

    public static List<Dictionary<string, object>> DefaultList() {
      return new List<Dictionary<string, object>> {
        Cmd("!np", "Now playing: {song}", "everyone", 10, "np"), Cmd("!queue", "Up next: {queue}", "everyone", 15, "queue"),
        Cmd("!skip", "Skipped: {song}", "moderator", 0, "skip"), Cmd("!uptime", "Live for {uptime}", "everyone", 15, "uptime"),
        Cmd("!wrongsong", "", "everyone", 0, "wrongsong"), Cmd("!ttsskip", "", "moderator", 0, "ttsskip"),
        Cmd("!ttson", "Chat voice is on", "moderator", 0, "ttson"), Cmd("!ttsoff", "Chat voice is off", "moderator", 0, "ttsoff"),
        Cmd("!viewers", "{viewers} watching right now", "everyone", 30, "viewers"), Cmd("!commands", "Commands: {commands}", "everyone", 30, "commands"),
        Cmd("!followage", "{user} has followed for {followage}", "everyone", 10, "followage"), Cmd("!clip", "Clip: {clip}", "everyone", 30, "clip") }; }

    // ready-made commands for the Commands page. Text ones start switched off until you put your own links in.
    public static List<Dictionary<string, object>> Presets() {
      var have = new HashSet<string>(Triggers(), StringComparer.OrdinalIgnoreCase);
      Func<Dictionary<string, object>, string, Dictionary<string, object>> P = (c, note) => { c["note"] = note; c["present"] = have.Contains(J.Str(c, "trigger", "")); return c; };
      var discord = Cmd("!discord", "Join the Discord: (paste your invite link here)", "everyone", 30, null); discord["enabled"] = false;
      var socials = Cmd("!socials", "Follow me everywhere: (your links here)", "everyone", 30, null); socials["enabled"] = false; socials["aliases"] = new List<string> { "!links" };
      return new List<Dictionary<string, object>> {
        P(DefaultList()[0], "now playing - all platforms"), P(DefaultList()[3], "how long you've been live - all platforms (needs the live time from the platform)"),
        P(Cmd("!commands", "Commands: {commands}", "everyone", 30, "commands"), "lists the commands the chatter can use on their platform"),
        P(Cmd("!followage", "{user} has followed for {followage}", "everyone", 10, "followage"), "Twitch only, with your Twitch account connected (Kick, YouTube and Rumble don't offer this to apps)"),
        P(Cmd("!clip", "Clip: {clip}", "everyone", 30, "clip"), "Twitch: makes a Twitch clip (Twitch account connected, live). Other platforms: saves OBS's replay buffer on your PC if it's running"),
        P(discord, "starts switched off - put your invite link in, then switch it on"), P(socials, "starts switched off - put your links in, then switch it on") }; }
    public static Dictionary<string, object> AddPresets(List<string> triggers) {
      var list = List(); var added = new List<string>();
      foreach (var p in Presets()) { var t = J.Str(p, "trigger", ""); if (!triggers.Contains(t) || J.Bool(p, "present", false)) continue; p.Remove("note"); p.Remove("present"); list.Add(p); added.Add(t); }
      if (added.Count == 0) return J.D("ok", true, "added", added);
      var err = Settings.Set("commands.list", list); return err == null ? J.D("ok", true, "added", added) : J.D("ok", false, "error", err); }
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
        // optional different answer per platform (empty = the normal answer)
        var per = new Dictionary<string, object>(); var pd = J.Obj(d, "responses");
        if (pd != null) foreach (var p in Settings.PlatformIds) { var x = Regex.Replace(J.Str(pd, p, "").Trim(), "[\\r\\n]+", " "); if (x.Length > 450) x = x.Substring(0, 450); if (x.Length > 0) per[p] = x; }
        string oerr; var obs = ObsControl.ValidateAction(J.Get(d, "obs"), out oerr); if (oerr != null) { err = trig + ": " + oerr; return null; }
        if (builtin == null && resp.Length == 0 && per.Count == 0 && obs == null) { err = trig + " needs a response"; return null; }
        var perm = J.Str(d, "permission", "everyone"); if (!Settings.Roles.Contains(perm)) perm = "everyone";
        var plats = J.List(d, "platforms").Where(x => Settings.PlatformIds.Contains(x)).Distinct().ToList();
        outp.Add(J.D("id", Regex.IsMatch(J.Str(d, "id", ""), "^[\\w-]{1,20}$") ? J.Str(d, "id", "") : U.Token(5), "trigger", trig, "aliases", aliases, "response", resp, "permission", perm,
          "cooldownSec", Math.Max(0, Math.Min(3600, J.Int(d, "cooldownSec", 0))), "userCooldownSec", Math.Max(0, Math.Min(3600, J.Int(d, "userCooldownSec", 0))),
          "platforms", plats, "enabled", J.Bool(d, "enabled", true), "builtin", builtin, "responses", per, "obs", obs)); }
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
      string reply = null; var builtin = J.Str(cmd, "builtin", null); var resp = J.Str(J.Obj(cmd, "responses") ?? new Dictionary<string, object>(), m.Platform, ""); if (resp.Length == 0) resp = J.Str(cmd, "response", "");
      // OBS action (switch scene, show / hide a source) first, then the answer
      var obsAct = J.Obj(cmd, "obs"); if (obsAct != null) { var oe = ObsControl.Run(obsAct); if (oe != null) Log.Warn("obs", word + ": " + oe); }
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
        case "commands": return Fill(resp, m, args, b);
        case "followage": { var f = FollowAge(m); return f.StartsWith("!") ? f.Substring(1) : Fill(resp.Replace("{followage}", f), m, args, b); }
        case "clip": { var c = Clip(m); return c.StartsWith("!") ? c.Substring(1) : Fill(resp.Replace("{clip}", c), m, args, b); }
      }
      return null; }
    // the commands this chatter can use here (platform, permission), shortest list that fits one chat message
    static string Available(ChatMsg m) {
      var l = new List<string>(); foreach (var c in List()) { if (!J.Bool(c, "enabled", true)) continue; var pl = J.List(c, "platforms"); if (m != null && pl.Count > 0 && !pl.Contains(m.Platform)) continue;
        if (m != null && Perm.Check(m, J.Str(c, "permission", "everyone")) != null) continue; if (m == null && J.Str(c, "permission", "everyone") != "everyone") continue; l.Add(J.Str(c, "trigger", "")); }
      if (Settings.Bool("songRequests.enabled")) l.AddRange(Settings.List("songRequests.commands").Take(1).Select(x => x.StartsWith("!") ? x : "!" + x));
      var s = string.Join(" ", l.Distinct()); return s.Length > 400 ? s.Substring(0, s.LastIndexOf(' ', 397)) + " ..." : s; }
    static string Ago(TimeSpan t) {
      int days = (int)t.TotalDays; if (days >= 365) { int y = days / 365, mo = (days % 365) / 30; return y + " year" + (y == 1 ? "" : "s") + (mo > 0 ? " " + mo + " month" + (mo == 1 ? "" : "s") : ""); }
      if (days >= 30) { int mo = days / 30, d = days % 30; return mo + " month" + (mo == 1 ? "" : "s") + (d > 0 ? " " + d + " day" + (d == 1 ? "" : "s") : ""); }
      if (days >= 1) return days + " day" + (days == 1 ? "" : "s"); return Math.Max(1, (int)t.TotalHours) + " hour" + ((int)t.TotalHours == 1 ? "" : "s"); }
    // "!" + text = the whole answer (can't tell), otherwise the time
    static string FollowAge(ChatMsg m) {
      if (m.Platform != "twitch") return "!Follow age only works on Twitch - " + (Platforms.Get(m.Platform) == null ? m.Platform : Platforms.Get(m.Platform).Label) + " doesn't share it with apps";
      if (m.Broadcaster) return "!You can't follow your own channel :)";
      if (!Accounts.Has("twitch")) return "!Follow age needs the streamer's Twitch account connected in IXC";
      var bid = J.Str(Accounts.Info("twitch"), "userId", ""); if (string.IsNullOrEmpty(m.UserId) || bid.Length == 0) return "!Couldn't check that right now";
      var r = Accounts.Helix("GET", "channels/followers?broadcaster_id=" + bid + "&user_id=" + Uri.EscapeDataString(m.UserId), null);
      if (r == null || !r.Ok) return "!Couldn't check follow age right now" + (r != null && r.Code == 401 ? " (sign in to Twitch again in IXC)" : "");
      var d = J.Get(J.Parse(r.Body), "data") as ArrayList; if (d == null || d.Count == 0) return "!@" + m.Name + " isn't following yet";
      DateTime at; if (!DateTime.TryParse(J.Str((Dictionary<string, object>)d[0], "followed_at", ""), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out at)) return "!Couldn't read the follow date";
      return Ago(DateTime.UtcNow - at); }
    // Twitch clip through Helix; anywhere else (or without a Twitch account) OBS's replay buffer when it's running
    static string Clip(ChatMsg m) {
      if (m.Platform == "twitch" && Accounts.Has("twitch")) {
        var a = Accounts.Info("twitch"); var scopes = J.List(a, "scopes");
        if (scopes.Count == 0 || scopes.Contains("clips:edit")) {
          var r = Accounts.Helix("POST", "clips?broadcaster_id=" + J.Str(a, "userId", ""), null);
          if (r != null && r.Ok) { var d = J.Get(J.Parse(r.Body), "data") as ArrayList; var id = d != null && d.Count > 0 ? J.Str((Dictionary<string, object>)d[0], "id", "") : "";
            if (id.Length > 0) { Log.Info("chat", "clip made for " + m.Name + ": " + id); return "https://clips.twitch.tv/" + id; } }
          if (r != null && r.Code == 404) { if (ObsControl.SaveReplay() == null) return "!Twitch only clips live streams - saved a replay clip on the streamer's PC instead"; return "!Clips can only be made while the stream is live"; }
          if (r != null) Log.Warn("chat", "clip failed: " + r.Code + " " + U.Trunc(r.Body ?? r.Error ?? "", 200)); } }
      var rb = ObsControl.SaveReplay(); if (rb == null) return "!Saved a replay clip on the streamer's PC";
      return "!" + (m.Platform == "twitch" ? (Accounts.Has("twitch") ? "Couldn't make a Twitch clip (sign in to Twitch again in IXC to allow clips)" : "Clips need the streamer's Twitch account connected in IXC") : "Clips here need OBS's replay buffer running") + " (" + rb + ")"; }
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
          case "amount": return m == null ? "" : m.Amount ?? "";
          case "song": return cur == null ? "nothing" : Music.Describe(cur);
          case "title": return cur == null ? "" : J.Str(cur, "title", "");
          case "artist": return cur == null ? "" : J.Str(cur, "artist", "");
          case "queue": return Music.UpNext(3);
          case "uptime": return Uptime() ?? "offline";
          case "viewers": { var t = Viewers.Total(); return t == null ? "?" : t.Value.ToString("N0", CultureInfo.InvariantCulture); }
          case "count": return n.ToString();
          case "commands": return Available(m);
          default: return x.Value; } }); }
  }
}
