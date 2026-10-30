// IXC Core - channel point rewards (Twitch + Kick), sound effects, and spoken alerts.
// Rewards: the streamer sets them up once in IXC (title, cost, what it does); IXC creates them on Twitch (and on Kick when allowed),
// and when a viewer claims one IXC runs it: a song request, any chat command (skip, shuffle, volume...), a sound effect, a message read aloud.
// A song request that can't be added gives the Twitch points back. Redemptions arrive from Twitch EventSub, Kick's chat socket,
// Streamer.bot (Kick.bot) or POST /api/rewards/redeem, so every way a streamer is set up works; the same claim twice counts once.
// Sounds: up to 20 named sound files, played through the IXC TTS source in OBS (rewards, "!sound name", alerts).
// Spoken alerts: follow / sub / gift / raid / Super Chat / member / reward: an optional sound per kind, then the voice reads it.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IXC {
  public static class Rewards {
    public static readonly string[] Actions = { "songrequest", "command", "sound", "tts", "message", "none" };
    public static readonly string[] Plats = { "twitch", "kick" };
    public static int Redeemed, Ran, Refunded, Failed;
    static readonly Dictionary<string, DateTime> seen = new Dictionary<string, DateTime>(), lastRun = new Dictionary<string, DateTime>();
    public static readonly LinkedList<Dictionary<string, object>> Recent = new LinkedList<Dictionary<string, object>>();

    public static List<Dictionary<string, object>> List() { var l = Settings.Value("rewards.list") as ArrayList; return l == null ? new List<Dictionary<string, object>>() : l.Cast<object>().OfType<Dictionary<string, object>>().ToList(); }
    // ready-made rewards the dashboard offers (nothing is created until the streamer adds them)
    public static List<Dictionary<string, object>> Presets() {
      return new List<Dictionary<string, object>> {
        R("Request a song", 500, "Type a song name or a YouTube link", true, "songrequest", ""), R("Skip the song", 1000, "Skips the song that is playing", false, "command", "!skip"),
        R("Previous song", 800, "Plays the last song again", false, "command", "!previous"), R("Shuffle the music", 600, "Turns shuffle on or off", false, "command", "!shuffle"),
        R("Music volume", 400, "Type a volume from 0 to 100", true, "command", "!volume"), R("Pause the music", 1500, "Pauses the music", false, "command", "!pause"),
        R("Play a sound", 200, "Type the name of a sound (see !sounds)", true, "sound", ""), R("Read my message aloud", 300, "Type a message for the stream voice", true, "tts", ""),
        R("Hydrate!", 100, "Remind the streamer to drink water", false, "message", "{user} says: time to drink water!") }; }
    static Dictionary<string, object> R(string title, int cost, string prompt, bool input, string action, string arg) {
      return J.D("id", U.Token(5), "title", title, "cost", cost, "prompt", prompt, "input", input, "action", action, "arg", arg, "platforms", Plats.ToList(), "enabled", true, "cooldownSec", 0, "color", "#9147FF", "twitchId", "", "kickId", ""); }

    public static object ValidateList(object v, out string err) {
      err = null; var src = v as ArrayList; if (src == null) { err = "rewards must be a list"; return null; }
      var r = new ArrayList(); var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var o in src) { var d = o as Dictionary<string, object>; if (d == null) continue; if (r.Count >= 50) { err = "at most 50 rewards"; return null; }
        var title = Regex.Replace(J.Str(d, "title", "").Trim(), "[\\x00-\\x1f]", ""); if (title.Length == 0) { err = "every reward needs a title"; return null; }
        if (title.Length > 45) { err = "\"" + title + "\": a title can have at most 45 characters (Twitch's limit)"; return null; }
        if (!titles.Add(title)) { err = "two rewards are called \"" + title + "\""; return null; }
        var action = J.Str(d, "action", "none"); if (!Actions.Contains(action)) { err = title + ": unknown action " + action; return null; }
        var arg = Regex.Replace(J.Str(d, "arg", "").Trim(), "[\\x00-\\x1f]", ""); if (arg.Length > 200) arg = arg.Substring(0, 200);
        if (action == "command" && !Regex.IsMatch(arg, "^![\\w-]{1,30}$")) { err = title + ": choose the chat command it runs (like !skip)"; return null; }
        if (action == "message" && arg.Length == 0) { err = title + ": type the message it posts"; return null; }
        var color = J.Str(d, "color", "#9147FF"); if (!Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$")) color = "#9147FF";
        var plats = J.List(d, "platforms").Where(p => Plats.Contains(p)).Distinct().ToList();
        var id = J.Str(d, "id", ""); if (!Regex.IsMatch(id, "^[\\w-]{1,40}$")) id = U.Token(5);
        r.Add(J.D("id", id, "title", title, "cost", Math.Max(1, Math.Min(10000000, J.Int(d, "cost", 100))), "prompt", U.Trunc(Regex.Replace(J.Str(d, "prompt", ""), "[\\x00-\\x1f]", " ").Trim(), 200),
          "input", J.Bool(d, "input", false), "action", action, "arg", arg, "platforms", plats, "enabled", J.Bool(d, "enabled", true), "cooldownSec", Math.Max(0, Math.Min(86400, J.Int(d, "cooldownSec", 0))),
          "color", color, "twitchId", Regex.IsMatch(J.Str(d, "twitchId", ""), "^[\\w-]{0,80}$") ? J.Str(d, "twitchId", "") : "", "kickId", Regex.IsMatch(J.Str(d, "kickId", ""), "^[\\w-]{0,80}$") ? J.Str(d, "kickId", "") : "")); }
      return r; }

    // ---------- a viewer claimed a reward (from any source) ----------
    public static void OnRedeem(string platform, string redemptionId, string rewardId, string title, string name, string login, string userId, string input, string via) {
      title = (title ?? "").Trim(); name = string.IsNullOrWhiteSpace(name) ? (login ?? "someone") : name.Trim(); input = (input ?? "").Trim(); if (title.Length == 0 || !Plats.Contains(platform)) return;
      // the same claim can come twice (Kick's chat socket and Streamer.bot): by its id, else the same person + reward within 5 s
      var hard = string.IsNullOrEmpty(redemptionId) ? null : platform + "#" + redemptionId; var soft = platform + "|" + name.ToLowerInvariant() + "|" + title.ToLowerInvariant() + "|" + input + "|";
      lock (seen) { DateTime t; var now = DateTime.Now; foreach (var k in seen.Where(x => (now - x.Value).TotalSeconds > 600).Select(x => x.Key).ToList()) seen.Remove(k);
        if (hard != null && seen.ContainsKey(hard)) return;
        if (seen.Where(x => x.Key.StartsWith(soft) && x.Key != soft + via && (now - x.Value).TotalSeconds < 5).Any()) return;   // another source already delivered it
        if (hard != null) seen[hard] = now; seen[soft + via] = now; }
      Redeemed++;
      var who = new ChatMsg { Platform = platform, Id = "reward-" + (redemptionId != null && redemptionId.Length > 0 ? redemptionId : U.Token(6)), Kind = "reward", Name = name, Login = login ?? name, UserId = userId ?? "", Text = "redeemed " + title + (input.Length > 0 ? ": " + input : ""), Via = via };
      Chat.Ingest(who);   // shows in chat + alerts (and is spoken when spoken alerts are on)
      var def = List().FirstOrDefault(d => (rewardId != null && rewardId.Length > 0 && (J.Str(d, "twitchId", "") == rewardId || J.Str(d, "kickId", "") == rewardId)) || string.Equals(J.Str(d, "title", ""), title, StringComparison.OrdinalIgnoreCase));
      string result;
      if (def == null) result = "not set up in IXC";
      else if (!Settings.Bool("rewards.on")) result = "rewards are turned off in IXC";
      else if (!J.Bool(def, "enabled", true)) result = "turned off in IXC";
      else if (!J.List(def, "platforms").Contains(platform)) result = "not used on " + platform;
      else result = null;
      if (result != null) { Note(who, title, input, result, false); return; }
      int cd = J.Int(def, "cooldownSec", 0);
      lock (lastRun) { DateTime t; if (cd > 0 && lastRun.TryGetValue(J.Str(def, "id", ""), out t) && (DateTime.Now - t).TotalSeconds < cd) { Done(who, def, rewardId, redemptionId, input, "still on cooldown"); return; } lastRun[J.Str(def, "id", "")] = DateTime.Now; }
      Task.Run(() => { string err; try { err = Run(def, who, input); } catch (Exception e) { err = U.Plain(e); } Done(who, def, rewardId, redemptionId, input, err); }); }

    // runs what the reward does; null = done, otherwise why it didn't work
    static string Run(Dictionary<string, object> def, ChatMsg who, string input) {
      var arg = J.Str(def, "arg", "");
      switch (J.Str(def, "action", "none")) {
        case "songrequest": {
          if (input.Length == 0) return "no song name was typed";
          var wait = new System.Threading.ManualResetEventSlim(false); string why = "no answer"; bool ok = false;
          Action<ChatMsg, bool, string> h = (m, good, reason) => { if (m != who) return; ok = good; why = reason; wait.Set(); };
          SongRequests.Done += h;
          try { SongRequests.Handle(who, input); if (!wait.Wait(45000)) return null; }   // no answer in time: don't refund what may still be added
          finally { SongRequests.Done -= h; }
          return ok ? null : why; }
        case "command": {
          var word = arg.ToLowerInvariant(); if (!Commands.List().Any(c => J.Bool(c, "enabled", true) && (J.Str(c, "trigger", "") == word || J.List(c, "aliases").Contains(word)))) return arg + " isn't an active command in IXC";
          // the streamer chose to sell this command, so the viewer may use it now (moderator-only commands too, no cooldown)
          var m = new ChatMsg { Platform = who.Platform, Id = who.Id + "-cmd", Name = who.Name, Login = who.Login, UserId = who.UserId, Text = (arg + " " + input).Trim(), Mod = true, NoTts = true, Via = who.Via };
          Commands.Handle(m); return null; }
        case "sound": { var name = arg.Length > 0 ? arg : input; return Sounds.Play(name, who, true); }
        case "tts": {
          if (input.Length == 0) return "no message was typed";
          var clean = Tts.Clean(input, null); if (clean == null) return "nothing readable in the message";
          Tts.Enqueue(new TtsItem { Text = input, Said = Tts.SpeakName(who.Name) + " says " + clean, User = who.Name, Login = who.Login, Platform = who.Platform, MsgId = who.Id, Priority = 2 }); return null; }
        case "message": { var t = Fill(arg, who, J.Str(def, "title", ""), input); if (t.Length > 0) Chat.Reply(who, t); return null; }
      }
      return null; }
    static void Done(ChatMsg who, Dictionary<string, object> def, string rewardId, string redemptionId, string input, string err) {
      var title = J.Str(def, "title", "");
      if (err == null) { Ran++; Note(who, title, input, "done", true); Fulfil(who.Platform, def, rewardId, redemptionId, true); return; }
      Failed++; bool back = Fulfil(who.Platform, def, rewardId, redemptionId, false);
      Note(who, title, input, err + (back ? " - points given back" : ""), false);
      if (Settings.Bool("rewards.replyOnFail") && J.Str(def, "action", "") != "songrequest") Chat.Reply(who, "@" + who.Name + " " + title + ": " + err + (back ? " (your points were given back)" : "")); }
    static void Note(ChatMsg who, string title, string input, string result, bool ok) {
      var n = J.D("platform", who.Platform, "user", who.Name, "title", title, "input", input, "result", result, "ok", ok, "at", U.Now());
      lock (Recent) { Recent.AddFirst(n); while (Recent.Count > 30) Recent.RemoveLast(); }
      Log.Info("chat", who.Platform + " " + who.Name + " redeemed \"" + title + "\": " + result); Hub.Publish("alerts", J.D("type", "rewards.run", "r", n)); }
    // Twitch: mark the claim done, or cancel it (points back). Only rewards IXC created can be changed (Twitch's rule).
    static bool Fulfil(string platform, Dictionary<string, object> def, string rewardId, string redemptionId, bool ok) {
      if (platform != "twitch" || string.IsNullOrEmpty(redemptionId) || string.IsNullOrEmpty(rewardId) || J.Str(def, "twitchId", "") != rewardId) return false;
      if (!ok && !Settings.Bool("rewards.refund")) return false;
      var bid = J.Str(Accounts.Info("twitch"), "userId", ""); if (bid.Length == 0) return false;
      var r = Accounts.Helix("PATCH", "channel_points/custom_rewards/redemptions?broadcaster_id=" + bid + "&reward_id=" + Uri.EscapeDataString(rewardId) + "&id=" + Uri.EscapeDataString(redemptionId), J.Ser(J.D("status", ok ? "FULFILLED" : "CANCELED")));
      if (r == null || !r.Ok) { Log.Warn("chat", "Twitch didn't " + (ok ? "complete" : "refund") + " the reward claim (" + (r == null ? "sign-in needed" : r.Code.ToString()) + ")"); return false; }
      if (!ok) Refunded++; return !ok; }
    public static string Fill(string tpl, ChatMsg m, string title, string input) {
      return (tpl ?? "").Replace("{user}", m.Name ?? "someone").Replace("{reward}", title ?? "").Replace("{input}", input ?? "").Replace("{platform}", m.Platform ?? "").Trim(); }

    // ---------- create / update the rewards on the platforms ----------
    public static Dictionary<string, object> Sync() {
      var list = List(); var res = new List<object>(); bool changed = false;
      var tw = Accounts.Info("twitch");
      if (list.Any(d => J.List(d, "platforms").Contains("twitch"))) {
        if (tw == null) res.Add(J.D("platform", "twitch", "ok", false, "error", "connect your Twitch account in IXC first"));
        else if (!J.List(tw, "scopes").Contains("channel:manage:redemptions")) res.Add(J.D("platform", "twitch", "ok", false, "error", "connect your Twitch account again in IXC (Platforms & Accounts) to allow channel point rewards"));
        else { string err = SyncTwitch(list, J.Str(tw, "userId", ""), ref changed); res.Add(J.D("platform", "twitch", "ok", err == null, "error", err)); } }
      if (list.Any(d => J.List(d, "platforms").Contains("kick"))) {
        var ki = Accounts.Info("kick");
        if (ki == null || !J.List(ki, "scopes").Contains("channel:rewards:write")) res.Add(J.D("platform", "kick", "ok", false, "manual", true, "error", "Kick: create rewards with exactly these titles in your Kick dashboard (Channel Points), or turn on \"Let IXC create Kick rewards\" and connect Kick again"));
        else { string err = SyncKick(list, ref changed); res.Add(J.D("platform", "kick", "ok", err == null, "error", err)); } }
      if (changed) { var al = new ArrayList(); foreach (var d in list) al.Add(d); Settings.Set("rewards.list", al); }
      return J.D("ok", res.Cast<Dictionary<string, object>>().All(x => J.Bool(x, "ok", false)), "results", res); }
    static string SyncTwitch(List<Dictionary<string, object>> list, string bid, ref bool changed) {
      var mine = Accounts.Helix("GET", "channel_points/custom_rewards?broadcaster_id=" + bid + "&only_manageable_rewards=true", null);
      if (mine == null) return "Twitch sign-in needed";
      if (!mine.Ok) return TwitchWhy(mine);
      var have = new HashSet<string>(J.Objs(J.Parse(mine.Body), "data").Select(x => J.Str(x, "id", "")));
      var errs = new List<string>();
      foreach (var d in list) {
        var id = J.Str(d, "twitchId", ""); bool use = J.List(d, "platforms").Contains("twitch");
        if (!use) { if (id.Length > 0 && have.Contains(id)) Accounts.Helix("PATCH", "channel_points/custom_rewards?broadcaster_id=" + bid + "&id=" + id, J.Ser(J.D("is_enabled", false))); continue; }
        int cd = J.Int(d, "cooldownSec", 0);
        var body = J.Ser(J.D("title", J.Str(d, "title", ""), "cost", J.Int(d, "cost", 100), "prompt", J.Str(d, "prompt", ""), "is_enabled", J.Bool(d, "enabled", true), "background_color", J.Str(d, "color", "#9147FF"),
          "is_user_input_required", J.Bool(d, "input", false), "is_global_cooldown_enabled", cd > 0, "global_cooldown_seconds", Math.Max(1, cd), "should_redemptions_skip_request_queue", false));
        var r = id.Length > 0 && have.Contains(id) ? Accounts.Helix("PATCH", "channel_points/custom_rewards?broadcaster_id=" + bid + "&id=" + id, body) : Accounts.Helix("POST", "channel_points/custom_rewards?broadcaster_id=" + bid, body);
        if (r != null && r.Ok) { var nid = J.Str(J.Objs(J.Parse(r.Body), "data").FirstOrDefault(), "id", ""); if (nid.Length > 0 && nid != id) { d["twitchId"] = nid; changed = true; } }
        else errs.Add(J.Str(d, "title", "") + ": " + (r == null ? "sign-in needed" : TwitchWhy(r))); }
      return errs.Count == 0 ? null : string.Join("; ", errs.Take(3)); }
    static string TwitchWhy(Http.Resp r) {
      var msg = J.Str(J.Parse(r.Body ?? "{}"), "message", r.Body ?? "");
      if (r.Code == 403 && Regex.IsMatch(msg, "partner|affiliate", RegexOptions.IgnoreCase)) return "Twitch channel points need Affiliate or Partner - IXC keeps these rewards and creates them once your channel has channel points";
      if (Regex.IsMatch(msg, "DUPLICATE", RegexOptions.IgnoreCase)) return "a reward with this title already exists on Twitch (made outside IXC) - rename one of them";
      return r.Code + " " + U.Trunc(msg, 140); }
    static string SyncKick(List<Dictionary<string, object>> list, ref bool changed) {
      var mine = Accounts.KickApi("GET", "channels/rewards", null); if (mine == null) return "Kick sign-in needed"; if (!mine.Ok) return "Kick: " + mine.Code + " " + U.Trunc(mine.Body, 140);
      var have = J.Objs(J.Parse(mine.Body), "data").ToDictionary(x => J.Str(x, "id", ""), x => x);
      var errs = new List<string>();
      foreach (var d in list) {
        var id = J.Str(d, "kickId", ""); if (!J.List(d, "platforms").Contains("kick")) continue;
        if (id.Length == 0) { var same = have.Values.FirstOrDefault(x => string.Equals(J.Str(x, "title", ""), J.Str(d, "title", ""), StringComparison.OrdinalIgnoreCase)); if (same != null) { id = J.Str(same, "id", ""); d["kickId"] = id; changed = true; } }
        var body = J.Ser(J.D("title", J.Str(d, "title", ""), "cost", J.Int(d, "cost", 100), "description", J.Str(d, "prompt", ""), "is_enabled", J.Bool(d, "enabled", true), "background_color", J.Str(d, "color", "#53FC18"), "is_user_input_required", J.Bool(d, "input", false), "should_redemptions_skip_request_queue", true));
        var r = id.Length > 0 && have.ContainsKey(id) ? Accounts.KickApi("PATCH", "channels/rewards/" + id, body) : Accounts.KickApi("POST", "channels/rewards", body);
        if (r != null && r.Ok) { var o = J.Parse(r.Body); var nid = J.Str(o, "data.id", ""); if (nid.Length > 0 && nid != id) { d["kickId"] = nid; changed = true; } }
        else errs.Add(J.Str(d, "title", "") + ": " + (r == null ? "sign-in needed" : r.Code + " " + U.Trunc(r.Body, 100))); }
      return errs.Count == 0 ? null : string.Join("; ", errs.Take(3)); }

    // ---------- API ----------
    public static bool Api(Ctx ctx, string path, string m, System.Collections.Specialized.NameValueCollection q) {
      if (path == "/api/rewards") { Http.Json(ctx, J.D("rewards", List(), "presets", Presets(), "recent", RecentList(), "sounds", Sounds.Names(), "twitch", Scopes("twitch"), "kick", Scopes("kick"), "stats", J.D("redeemed", Redeemed, "ran", Ran, "failed", Failed, "refunded", Refunded))); return true; }
      if (path == "/api/rewards/sync" && m == "POST") { Http.Json(ctx, Sync()); return true; }
      // other tools (Kick.bot / Streamer.bot "Fetch URL", a test) can hand IXC a claim: platform, title, user, input
      if (path == "/api/rewards/redeem" && m == "POST") {
        var b = Http.BodyJson(ctx); var p = J.Str(b, "platform", "kick").ToLowerInvariant(); var t = J.Str(b, "title", J.Str(b, "reward", ""));
        if (!Plats.Contains(p) || t.Trim().Length == 0) { Http.Json(ctx, 400, J.D("error", "send platform (twitch or kick) and the reward title")); return true; }
        OnRedeem(p, J.Str(b, "id", ""), J.Str(b, "rewardId", ""), t, J.Str(b, "user", "someone"), J.Str(b, "login", null), J.Str(b, "userId", ""), J.Str(b, "input", ""), "api");
        Http.Json(ctx, J.D("ok", true)); return true; }
      return false; }
    static List<Dictionary<string, object>> RecentList() { lock (Recent) return Recent.ToList(); }
    static Dictionary<string, object> Scopes(string p) { var a = Accounts.Info(p); var sc = a == null ? new List<string>() : J.List(a, "scopes");
      return J.D("connected", a != null, "canManage", p == "twitch" ? sc.Contains("channel:manage:redemptions") : sc.Contains("channel:rewards:write")); }
  }

  // ======================= sound effects =======================
  public static class Sounds {
    static readonly Dictionary<string, DateTime> last = new Dictionary<string, DateTime>();
    static readonly Dictionary<string, KeyValuePair<DateTime, byte[]>> cache = new Dictionary<string, KeyValuePair<DateTime, byte[]>>();
    public static int Played;
    static readonly string[] Exts = { ".mp3", ".wav", ".ogg", ".m4a", ".webm" };
    public static string Folder { get { var d = Path.Combine(Cfg.DataDir ?? ".", "sounds"); Directory.CreateDirectory(d); return d; } }
    public static List<Dictionary<string, object>> List() { var l = Settings.Value("sounds.list") as ArrayList; return l == null ? new List<Dictionary<string, object>>() : l.Cast<object>().OfType<Dictionary<string, object>>().ToList(); }
    public static List<string> Names() { return List().Where(s => J.Bool(s, "enabled", true)).Select(s => J.Str(s, "name", "")).ToList(); }
    public static object ValidateList(object v, out string err) {
      err = null; var src = v as ArrayList; if (src == null) { err = "sounds must be a list"; return null; }
      var r = new ArrayList(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var o in src) { var d = o as Dictionary<string, object>; if (d == null) continue; if (r.Count >= 20) { err = "at most 20 sounds"; return null; }
        var name = J.Str(d, "name", "").Trim().ToLowerInvariant().TrimStart('!'); if (!Regex.IsMatch(name, "^[a-z0-9][a-z0-9_-]{0,19}$")) { err = "sound name \"" + name + "\": use 1-20 letters or numbers, no spaces (viewers type it)"; return null; }
        if (!names.Add(name)) { err = "two sounds are called " + name; return null; }
        var file = J.Str(d, "file", "").Trim().Trim('"'); if (file.Length > 400) { err = name + ": the file path is too long"; return null; }
        if (file.Length > 0 && !Exts.Contains(Path.GetExtension(file).ToLowerInvariant())) { err = name + ": use an mp3, wav, ogg or m4a file"; return null; }
        r.Add(J.D("name", name, "file", file, "volume", Math.Max(0, Math.Min(100, J.Int(d, "volume", 80))), "cooldownSec", Math.Max(0, Math.Min(3600, J.Int(d, "cooldownSec", 10))), "enabled", J.Bool(d, "enabled", true))); }
      return r; }
    // "!sound name" / "!sounds" in chat; true when the message was one of them
    public static bool OnChat(ChatMsg m) {
      var cmd = Settings.Str("sounds.command").Trim().ToLowerInvariant(); if (cmd.Length < 2 || !Settings.Bool("sounds.on")) return false;
      var text = (m.Text ?? "").Trim(); var sp = text.IndexOf(' '); var word = (sp < 0 ? text : text.Substring(0, sp)).ToLowerInvariant(); var arg = sp < 0 ? "" : text.Substring(sp + 1).Trim().ToLowerInvariant();
      if (word == cmd + "s") { var n = Names(); Chat.Reply(m, n.Count == 0 ? "No sounds are set up yet" : "Sounds: " + string.Join(", ", n) + " - type " + cmd + " name"); return true; }
      if (word != cmd) return false;
      if (!Settings.Bool("sounds.chat")) return true;   // sounds only through rewards
      var why = Perm.Check(m, Settings.Str("sounds.permission")); if (why != null) return true;
      var err = Play(arg, m, false); if (err != null && err.StartsWith("no sound called")) Chat.Reply(m, "@" + m.Name + " " + err + " - type " + cmd + "s for the list");
      return true; }
    // plays a sound by name; null = playing (queued), otherwise why not
    public static string Play(string name, ChatMsg who, bool paid) {
      name = (name ?? "").Trim().ToLowerInvariant().TrimStart('!'); if (name.Length == 0) return "no sound name was given";
      if (!Settings.Bool("sounds.on")) return "sounds are turned off in IXC";
      var s = List().FirstOrDefault(x => J.Str(x, "name", "") == name && J.Bool(x, "enabled", true)); if (s == null) return "no sound called " + name;
      if (!paid && !(who != null && (who.Mod || who.Broadcaster))) lock (last) {
        DateTime t; var now = DateTime.Now; int cd = J.Int(s, "cooldownSec", 0), g = Settings.Int("sounds.cooldownSec");
        if (cd > 0 && last.TryGetValue(name, out t) && (now - t).TotalSeconds < cd) return name + " is on cooldown";
        if (g > 0 && last.TryGetValue("*", out t) && (now - t).TotalSeconds < g) return "sounds are on cooldown";
        last[name] = now; last["*"] = now; }
      return PlayFile(J.Str(s, "file", ""), J.Int(s, "volume", 80), name, who, paid ? 2 : 1); }
    public static string PlayFile(string file, int volume, string label, ChatMsg who, int prio) {
      string mime, err; var bytes = Load(file, out mime, out err); if (bytes == null) return err;
      Played++;
      Tts.Enqueue(new TtsItem { Text = "(sound) " + label, Said = "(sound) " + label, User = who == null ? "IXC" : who.Name, Login = who == null ? "" : who.Login, Platform = who == null ? "test" : who.Platform, Priority = prio,
        Volume = Math.Max(0, Math.Min(100, volume * Settings.Int("sounds.volume") / 100)), Audio = bytes, Mime = mime, Engine = "sound file", IsSound = true });
      return null; }
    static byte[] Load(string file, out string mime, out string err) {
      mime = null; err = null; file = (file ?? "").Trim().Trim('"');
      if (file.Length == 0) { err = "no sound file is chosen"; return null; }
      if (!Path.IsPathRooted(file)) file = Path.Combine(Folder, file);
      var ext = Path.GetExtension(file).ToLowerInvariant(); if (!Exts.Contains(ext)) { err = "not a sound file: " + Path.GetFileName(file); return null; }
      var fi = new FileInfo(file); if (!fi.Exists) { err = "the sound file is missing: " + Path.GetFileName(file); return null; }
      if (fi.Length > 8 * 1024 * 1024) { err = Path.GetFileName(file) + " is larger than 8 MB"; return null; }
      mime = ext == ".mp3" ? "audio/mpeg" : ext == ".wav" ? "audio/wav" : ext == ".ogg" ? "audio/ogg" : ext == ".webm" ? "audio/webm" : "audio/mp4";
      lock (cache) { KeyValuePair<DateTime, byte[]> c; if (cache.TryGetValue(fi.FullName, out c) && c.Key == fi.LastWriteTimeUtc) return c.Value;
        try { var b = File.ReadAllBytes(fi.FullName); if (cache.Count > 40) cache.Clear(); cache[fi.FullName] = new KeyValuePair<DateTime, byte[]>(fi.LastWriteTimeUtc, b); return b; } catch (Exception e) { err = "can't read " + Path.GetFileName(file) + ": " + U.Plain(e); return null; } } }
    public static bool Api(Ctx ctx, string path, string m, System.Collections.Specialized.NameValueCollection q) {
      if (path == "/api/sounds/play" && m == "POST") { var b = Http.BodyJson(ctx); var err = J.Str(b, "file", "").Length > 0 ? PlayFile(J.Str(b, "file", ""), J.Int(b, "volume", 80), "test", null, 2) : Play(J.Str(b, "name", ""), null, true);
        if (err != null) Http.Json(ctx, 400, J.D("error", err)); else Http.Json(ctx, J.D("ok", true)); return true; }
      // the dashboard sends a sound file (raw bytes, up to 1 MB); it is kept in IXC's data folder\sounds
      if (path == "/api/sounds/upload" && m == "POST") {
        var name = Regex.Replace((q["name"] ?? "sound").ToLowerInvariant(), "[^a-z0-9_-]", ""); var ext = "." + Regex.Replace((q["ext"] ?? "mp3").ToLowerInvariant(), "[^a-z0-9]", "");
        if (name.Length == 0) name = "sound"; if (name.Length > 40) name = name.Substring(0, 40);
        if (!Exts.Contains(ext)) { Http.Json(ctx, 400, J.D("error", "use an mp3, wav, ogg or m4a file")); return true; }
        var bytes = ctx.Request.BodyBytes; if (bytes == null || bytes.Length < 100) { Http.Json(ctx, 400, J.D("error", "the file is empty")); return true; }
        var f = Path.Combine(Folder, name + ext); File.WriteAllBytes(f, bytes); Log.Info("app", "sound file saved: " + Path.GetFileName(f) + " (" + bytes.Length / 1024 + " KB)");
        Http.Json(ctx, J.D("ok", true, "file", f)); return true; }
      return false; }
  }

  // ======================= spoken alerts =======================
  public static class SpokenAlerts {
    public static readonly string[] Kinds = { "follow", "sub", "gift", "raid", "superchat", "member", "reward" };
    public static readonly Dictionary<string, string> Defaults = new Dictionary<string, string> {
      { "follow", "{user} just followed. Thank you!" }, { "sub", "{user} just subscribed! {text}" }, { "gift", "{user} {text}. Thank you so much!" }, { "raid", "{user} is raiding with {amount}! Welcome raiders!" },
      { "superchat", "{user} sent a Super Chat of {amount}. {text}" }, { "member", "{user} just became a member!" }, { "reward", "{user} {text}" } };
    static readonly Dictionary<string, DateTime> last = new Dictionary<string, DateTime>();
    public static int Spoken, Grouped;
    public static void On(ChatMsg m) {
      if (m == null || m.Old || !Settings.Bool("alerts.speak") || !Kinds.Contains(m.Kind)) return;
      bool speak = Settings.Bool("alerts." + m.Kind + ".speak"); var sound = Settings.Str("alerts." + m.Kind + ".sound").Trim(); if (!speak && sound.Length == 0) return;
      // a burst (gift bomb, follow bots): one spoken alert per kind per burst window
      int burst = Settings.Int("alerts.burstSec"); var key = m.Platform + "|" + m.Kind + (m.Kind == "reward" ? "|" + (m.Name ?? "").ToLowerInvariant() : "");
      lock (last) { DateTime t; if (burst > 0 && last.TryGetValue(key, out t) && (DateTime.Now - t).TotalSeconds < burst) { Grouped++; return; } last[key] = DateTime.Now; }
      if (sound.Length > 0) { var err = Sounds.PlayFile(sound, Settings.Int("alerts.soundVolume"), m.Kind + " alert", m, 2); if (err != null) Log.Warn("tts", m.Kind + " alert sound: " + err); }
      if (!speak) return;
      var text = Settings.Str("alerts." + m.Kind + ".say").Replace("{user}", Tts.SpeakName(m.Name)).Replace("{text}", m.Text ?? "").Replace("{amount}", m.Amount ?? "").Replace("{platform}", m.Platform ?? "");
      var clean = Tts.Clean(text, null); if (clean == null) return;
      Spoken++; var v = Settings.Str("alerts.voice").Trim();
      Tts.Enqueue(new TtsItem { Text = text, Said = clean, User = m.Name, Login = m.Login, Platform = m.Platform, MsgId = m.Id, Priority = 2, VoiceKey = v.Length > 0 ? v : null }); }
  }
}
