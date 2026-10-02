// IXC Core - chat: every platform's messages become one normalized message, then go through
//   duplicate check -> filters -> pages / overlays / phones -> commands + song requests -> TTS.
// A failing platform never touches the others: each one is its own source with its own state (see Platforms.cs).
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
using System.Threading.Tasks;

namespace IXC {
  public class ChatMsg {
    public string Platform, Id, UserId = "", Name, Login, Color, Avatar, Text = "", Kind = "chat", Amount = "", Via = "ixc";
    public List<string> Badges = new List<string>(); public List<Dictionary<string, object>> Parts = new List<Dictionary<string, object>>();
    public bool Broadcaster, Mod, Vip, Sub, FirstTime, Me, Test, Old; public bool? Follower;
    public DateTime At = DateTime.Now; public List<string> Flags = new List<string>(); public bool Hidden, NoTts;
    // emote positions (code points) for TTS cleanup, and the names of emotes in the text
    public List<KeyValuePair<int, int>> EmoteSpans = new List<KeyValuePair<int, int>>(); public HashSet<string> EmoteNames = new HashSet<string>(StringComparer.Ordinal);
    public int Level { get { return Broadcaster ? 5 : Mod ? 4 : Vip ? 3 : Sub ? 2 : Follower == true ? 1 : 0; } }
    public string Key { get { return Platform + ":" + (Login ?? Name ?? "").ToLowerInvariant(); } }
    public Dictionary<string, object> View() {
      return J.D("id", Id, "platform", Platform, "kind", Kind, "name", Name, "login", Login, "userId", UserId, "color", Color, "avatar", Avatar, "badges", Badges,
        "text", Text, "parts", Parts, "amount", Amount, "at", U.Now() - (long)(DateTime.Now - At).TotalMilliseconds, "old", Old,
        "roles", J.D("broadcaster", Broadcaster, "mod", Mod, "vip", Vip, "sub", Sub, "follower", Follower, "first", FirstTime), "flags", Flags, "hidden", Hidden, "me", Me, "via", Via); }
    // plain text -> parts (emote names / positions already known)
    public void TextParts() { if (Parts.Count == 0 && Text.Length > 0) Parts.Add(J.D("t", "text", "v", Text)); }
  }

  public static class Chat {
    public static bool Enabled;
    static readonly LinkedList<ChatMsg> History = new LinkedList<ChatMsg>();
    static readonly LinkedList<string> SeenIds = new LinkedList<string>(); static readonly HashSet<string> SeenSet = new HashSet<string>();
    static readonly LinkedList<KeyValuePair<string, DateTime>> SeenText = new LinkedList<KeyValuePair<string, DateTime>>();
    public static readonly LinkedList<Dictionary<string, object>> Alerts = new LinkedList<Dictionary<string, object>>();
    public static int Received, Duplicates, Hidden; public static readonly Dictionary<string, int> PerPlatform = new Dictionary<string, int>();
    public static DateTime LastMessage = DateTime.MinValue;
    static readonly Dictionary<string, KeyValuePair<string, DateTime>> Chatters = new Dictionary<string, KeyValuePair<string, DateTime>>(StringComparer.OrdinalIgnoreCase);

    public static void Init() { Enabled = true;
      Settings.Changed += k => { if (k == "commands.list" || k == "chat.hiddenCommands" || k == "chat.extraCommandsFile") Task.Run(() => PushSuggestions()); }; }

    // ---------- the pipeline ----------
    public static void Ingest(ChatMsg m) {
      if (m == null || string.IsNullOrEmpty(m.Platform)) return;
      if (m.Kind == "chat" && string.IsNullOrWhiteSpace(m.Text) && m.Parts.Count == 0) return;
      if (m.Name == null) m.Name = m.Login ?? "someone"; if (m.Login == null) m.Login = m.Name;
      // Streamer.bot and IXC's own connection can both deliver the same platform: IXC's own wins
      if (m.Via == "streamerbot" && Platforms.HandlesChat(m.Platform)) return;
      if (!Seen(m)) { Duplicates++; return; }
      LastMessage = DateTime.Now; Received++; lock (PerPlatform) { int c; PerPlatform.TryGetValue(m.Platform, out c); PerPlatform[m.Platform] = c + 1; }
      if (m.Kind == "chat") lock (Chatters) { Chatters[m.Name] = new KeyValuePair<string, DateTime>(m.Platform, DateTime.Now); if (Chatters.Count > 3000) { foreach (var k in Chatters.OrderBy(x => x.Value.Value).Take(1000).Select(x => x.Key).ToList()) Chatters.Remove(k); } }
      m.TextParts();
      if (m.Kind == "chat") { try { Filters.Apply(m); } catch (Exception e) { Log.Err("chat", "filter: " + e.Message); } }
      if (m.Hidden) Hidden++;
      lock (History) { History.AddLast(m); while (History.Count > 150) History.RemoveFirst(); }
      Hub.Publish("chat", J.D("type", "chat.msg", "m", m.View()));
      if (m.Kind != "chat") { AddAlert(m); return; }
      if (m.Old) return;
      Log.Debug("chat", m.Platform + " " + m.Name + ": " + U.Trunc(m.Text, 200));
      try { if (Commands.Handle(m)) return; } catch (Exception e) { Log.Err("chat", "command: " + e.Message); }
      if (!m.NoTts) { try { Tts.OnChat(m); } catch (Exception e) { Log.Err("tts", "chat to TTS: " + e.Message); } } }
    static bool Seen(ChatMsg m) {
      string key = m.Id != null ? m.Platform + ":" + m.Id : null;
      lock (SeenSet) {
        if (key != null) { if (!SeenSet.Add(key)) return false; SeenIds.AddLast(key); while (SeenIds.Count > 2000) { SeenSet.Remove(SeenIds.First.Value); SeenIds.RemoveFirst(); } return true; }
        // no id (Rumble, some Streamer.bot events): same person, same text, same platform within 3 s = the same message
        var tk = m.Platform + "|" + (m.Login ?? "") + "|" + m.Kind + "|" + m.Text; var now = DateTime.Now;
        while (SeenText.Count > 0 && (now - SeenText.First.Value.Value).TotalSeconds > 3) SeenText.RemoveFirst();
        if (SeenText.Any(x => x.Key == tk)) return false; SeenText.AddLast(new KeyValuePair<string, DateTime>(tk, now)); return true; } }
    static void AddAlert(ChatMsg m) {
      var a = J.D("type", "alert", "kind", m.Kind, "platform", m.Platform, "name", m.Name, "text", m.Text, "amount", m.Amount, "at", U.Now(), "old", m.Old);
      lock (Alerts) { Alerts.AddLast(a); while (Alerts.Count > 50) Alerts.RemoveFirst(); }
      if (!m.Old) { Hub.Publish("alerts", a); Log.Info("chat", m.Platform + " " + m.Kind + ": " + m.Name + (m.Amount.Length > 0 ? " " + m.Amount : "")); } }
    public static List<Dictionary<string, object>> RecentAlerts(int n) { lock (Alerts) return Alerts.Reverse().Take(n).ToList(); }

    // moderation from the platform: hide deleted messages / messages of banned users everywhere
    public static void Delete(string platform, string id, string login) {
      lock (History) { var gone = History.Where(x => x.Platform == platform && ((id != null && x.Id == id) || (id == null && login != null && string.Equals(x.Login, login, StringComparison.OrdinalIgnoreCase)))).ToList(); foreach (var g in gone) History.Remove(g); }
      Hub.Publish("chat", J.D("type", "chat.del", "platform", platform, "id", id, "login", login));
      Tts.Forget(platform, id, login); }
    public static void DeleteByUserId(string platform, string userId) {
      List<ChatMsg> gone; lock (History) { gone = History.Where(x => x.Platform == platform && x.UserId == userId).ToList(); foreach (var g in gone) History.Remove(g); }
      foreach (var g in gone) Hub.Publish("chat", J.D("type", "chat.del", "platform", platform, "id", g.Id, "login", (string)null)); }
    public static void ClearAll(string platform) { lock (History) { foreach (var g in History.Where(x => x.Platform == platform).ToList()) History.Remove(g); } Hub.Publish("chat", J.D("type", "chat.clear", "platform", platform)); }

    // ---------- sending (reply box, phone, command answers) ----------
    public static async Task<List<Dictionary<string, object>>> Send(string platform, string message) {
      var res = new List<Dictionary<string, object>>(); message = Regex.Replace((message ?? "").Trim(), "[\\r\\n]+", " "); if (message.Length == 0) return res; if (message.Length > 480) message = message.Substring(0, 480);
      platform = (platform ?? "all").ToLowerInvariant();
      var targets = platform == "all" ? Platforms.SendTargets() : new List<string> { platform };
      if (targets.Count == 0) { res.Add(J.D("platform", "all", "ok", false, "error", Accounts.Available("twitch") || Accounts.Available("kick") || Accounts.Available("youtube") ? "IXC can't reply anywhere yet: open Streamer.bot (IXC replies through it), or connect your account under Platforms & accounts." : "IXC can't reply anywhere yet: open Streamer.bot with its WebSocket server on - IXC replies through it.")); return res; }
      var tasks = targets.Select(async p => { string err; try { err = await Platforms.Send(p, message); } catch (Exception e) { err = U.Plain(e); } return J.D("platform", p, "ok", err == null, "error", err ?? ""); }).ToList();
      foreach (var t in tasks) res.Add(await t);
      Log.Info("chat", "sent to " + string.Join("+", res.Where(r => (bool)r["ok"]).Select(r => r["platform"])) + (res.Any(r => !(bool)r["ok"]) ? " (failed: " + string.Join(", ", res.Where(r => !(bool)r["ok"]).Select(r => r["platform"] + " - " + r["error"])) + ")" : ""));
      return res; }
    // a reply to one message (commands, song requests): same platform; silently skipped when IXC can't send there
    public static void Reply(ChatMsg to, string text) {
      if (to == null || to.Test) { if (to != null) Hub.Publish("chat", J.D("type", "chat.reply", "platform", to.Platform, "text", text)); return; }
      Task.Run(async () => { var err = await Platforms.Send(to.Platform, text); if (err != null) { Log.Info("chat", "could not answer on " + to.Platform + ": " + err); Hub.Publish("chat", J.D("type", "chat.reply", "platform", to.Platform, "text", text, "error", err)); } }); }

    // ---------- reply-box suggestions ----------
    // everything "!" can complete in the chat box: IXC's commands, Streamer.bot's commands and an optional extra commands file
    public static async Task<Dictionary<string, object>> Suggest() {
      var cmds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase); var from = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
      foreach (var c in Commands.Triggers()) { cmds.Add(c); from[c] = "IXC"; }
      foreach (var c in await Platforms.Sb.Commands()) { cmds.Add(c); if (!from.ContainsKey(c)) from[c] = "Streamer.bot"; }
      var cf = Settings.Str("chat.extraCommandsFile");
      if (cf.Length > 0 && File.Exists(cf)) { try { var cs = File.ReadAllText(cf);
        foreach (Match mm in Regex.Matches(cs, "\\{\\s*\"([a-z0-9]+)\"\\s*,")) cmds.Add("!" + mm.Groups[1].Value);
        foreach (Match mm in Regex.Matches(cs, "case\\s+\"([a-z0-9]+)\"\\s*:")) cmds.Add("!" + mm.Groups[1].Value); } catch { } }
      foreach (var h in Settings.List("chat.hiddenCommands")) cmds.Remove(h.StartsWith("!") ? h : "!" + h);
      List<object> users; lock (Chatters) users = Chatters.OrderByDescending(x => x.Value.Value).Take(300).Select(x => (object)J.D("name", x.Key, "platform", x.Value.Key)).ToList();
      return J.D("commands", cmds.ToList(), "from", from, "users", users); }
    // push a fresh list to every open chat box (Streamer.bot connected, commands changed)
    public static async Task PushSuggestions() { try { var d = new Dictionary<string, object>(await Suggest()); d["type"] = "chat.suggest"; Hub.Publish("chat", d); } catch (Exception e) { Log.Debug("chat", "suggestions: " + e.Message); } }

    public static Dictionary<string, object> DiagInfo() {
      return J.D("messages", Received, "perPlatform", new Dictionary<string, int>(PerPlatform), "duplicatesDropped", Duplicates, "hiddenByFilters", Hidden,
        "lastMessage", LastMessage == DateTime.MinValue ? "none yet" : LastMessage.ToString("HH:mm:ss"), "platforms", Platforms.StatusAll(), "yourAccounts", Own()); }
    public static List<string> Own() { var l = Platforms.OwnNames(); l.AddRange(Settings.List("tts.ownNames")); return l.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
    static List<object> HistoryView() { lock (History) return History.Select(h => (object)h.View()).ToList(); }

    // ---------- API ----------
    public static bool Api(Ctx ctx, string path, string m, System.Collections.Specialized.NameValueCollection q) {
      if (path == "/api/chat/send" && m == "POST") { var b = Http.BodyJson(ctx); var r = Send(J.Str(b, "platform", "all"), J.Str(b, "message", "")).Result; Http.Json(ctx, J.D("results", r)); return true; }
      if (path == "/api/chat/suggest") { Http.Json(ctx, Suggest().Result); return true; }
      if (path == "/api/chat/history") { Http.Json(ctx, J.D("messages", HistoryView())); return true; }
      if (path == "/api/chat/status") { Http.Json(ctx, J.D("platforms", Platforms.StatusAll())); return true; }
      if (path == "/api/chat/reconnect" && m == "POST") { Platforms.ReconnectAll(); Http.Json(ctx, J.D("ok", true)); return true; }
      if (path == "/api/chat/inject" && m == "POST") {   // tests only (--test): a fake message through the same pipeline as real ones
        if (!Program.TestMode) { Http.Json(ctx, 403, J.D("error", "only in test mode")); return true; }
        var b = Http.BodyJson(ctx); var cm = FromTest(b); Ingest(cm); Http.Json(ctx, J.D("ok", true, "hidden", cm.Hidden, "flags", cm.Flags, "noTts", cm.NoTts)); return true; }
      return false; }
    static ChatMsg FromTest(Dictionary<string, object> b) {
      var cm = new ChatMsg { Platform = J.Str(b, "platform", "twitch"), Id = J.Str(b, "id", null), Name = J.Str(b, "name", "Tester"), Login = J.Str(b, "login", null), Text = J.Str(b, "text", ""),
        Kind = J.Str(b, "kind", "chat"), Mod = J.Bool(b, "mod", false), Vip = J.Bool(b, "vip", false), Sub = J.Bool(b, "sub", false), Broadcaster = J.Bool(b, "broadcaster", false),
        Via = J.Str(b, "via", "ixc"), Amount = J.Str(b, "amount", "") };
      if (J.Get(b, "follower") is bool) cm.Follower = J.Bool(b, "follower", false);
      foreach (var e in J.Objs(b, "emotes")) { cm.EmoteNames.Add(J.Str(e, "name", "")); if (J.Get(e, "start") != null) cm.EmoteSpans.Add(new KeyValuePair<int, int>(J.Int(e, "start", 0), J.Int(e, "end", 0))); }
      cm.Login = cm.Login ?? cm.Name.ToLowerInvariant(); return cm; }
    public static void OnWs(Client c, string type, Dictionary<string, object> msg) {
      if (type == "hello" && c.Topics.Contains("chat")) { Hub.Send(c, J.D("type", "chat.history", "messages", HistoryView())); Hub.Send(c, J.D("type", "chat.status", "platforms", Platforms.StatusAll())); }
      if (type == "hello" && c.Topics.Contains("alerts")) Hub.Send(c, J.D("type", "alerts.recent", "alerts", RecentAlerts(10)));
      else if (type == "chat.send") { var id = J.Str(msg, "reqId", ""); Send(J.Str(msg, "platform", "all"), J.Str(msg, "message", "")).ContinueWith(r => Hub.Send(c, J.D("type", "chat.sent", "reqId", id, "results", r.IsFaulted ? new List<Dictionary<string, object>>() : r.Result))); }
      else if (type == "chat.suggest") { Suggest().ContinueWith(r => { if (r.IsFaulted) return; var d = new Dictionary<string, object>(r.Result); d["type"] = "chat.suggest"; Hub.Send(c, d); }); }
      else if (type == "chat.reconnect") Platforms.ReconnectAll(); }
  }

  // ---------- filters: every rule is optional; "tts" = don't read it out, "hide" = also hide it from overlays ----------
  public static class Filters {
    static readonly LinkedList<KeyValuePair<string, DateTime>> recent = new LinkedList<KeyValuePair<string, DateTime>>();
    static readonly Regex Link = new Regex("(https?://|www\\.)\\S+|\\b[\\w-]+\\.(com|net|org|gg|tv|io|in|ly|me|co|app|xyz|link|live|ru|de|uk|shop|store|site|online)(/\\S*)?\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
    static void Hit(ChatMsg m, string rule, string action) {
      if (action == "off" || action == null) return; m.Flags.Add(rule); m.NoTts = true; if (action == "hide") m.Hidden = true;
      lock (Counts) { int n; Counts.TryGetValue(rule, out n); Counts[rule] = n + 1; } }
    public static bool InList(string name, IEnumerable<string> list) { if (string.IsNullOrEmpty(name)) return false; var n = Norm(name); return list.Any(x => Norm(x) == n); }
    public static string Norm(string s) { return Regex.Replace((s ?? "").ToLowerInvariant().TrimStart('@'), "[^\\p{L}\\p{N}]", ""); }
    public static void Apply(ChatMsg m) {
      if (!Settings.Bool("chat.filters.enabled")) return;
      var text = m.Text ?? "";
      if (InList(m.Name, Settings.List("chat.filters.whitelist")) || InList(m.Login, Settings.List("chat.filters.whitelist"))) return;
      if (InList(m.Name, Settings.List("chat.filters.blacklist")) || InList(m.Login, Settings.List("chat.filters.blacklist"))) { Hit(m, "blocked user", "hide"); return; }
      int need = Array.IndexOf(Settings.Roles, Settings.Str("chat.filters.minRole"));
      if (need > 0 && m.Level < need) Hit(m, "below " + Settings.Str("chat.filters.minRole"), "hide");
      if (m.Broadcaster || (m.Mod && Settings.Bool("chat.filters.modsBypass")) || (m.Sub && Settings.Bool("chat.filters.subsBypass"))) return;
      var low = text.ToLowerInvariant();
      var words = Settings.List("chat.filters.bannedWordList");
      if (words.Count > 0 && words.Any(w => WordHit(low, w.ToLowerInvariant()))) Hit(m, "banned word", Settings.Str("chat.filters.bannedWords"));
      if (Link.IsMatch(text)) Hit(m, "link", Settings.Str("chat.filters.links"));
      int letters = text.Count(char.IsLetter), upper = text.Count(char.IsUpper);
      if (letters >= 10 && upper * 100 >= letters * Settings.Int("chat.filters.capsPercent")) Hit(m, "caps", Settings.Str("chat.filters.caps"));
      if (Regex.IsMatch(text, "(.)\\1{" + (Settings.Int("chat.filters.maxRepeat") - 1) + ",}")) Hit(m, "repeated characters", Settings.Str("chat.filters.repeatChars"));
      if (text.Length > Settings.Int("chat.filters.maxLength")) Hit(m, "too long", Settings.Str("chat.filters.longMessage"));
      // zalgo / ascii-art / walls of symbols
      int marks = text.Count(ch => CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark), symbols = text.Count(ch => !char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch) && !char.IsSurrogate(ch));
      if (marks > 8 || (text.Length >= 20 && symbols * 100 / Math.Max(1, text.Length) > 60 && m.Parts.All(p => J.Str(p, "t", "") != "emote"))) Hit(m, "suspicious characters", Settings.Str("chat.filters.suspicious"));
      // the same person repeating themself / many people pasting the same thing
      var me = m.Key + "|" + Norm(text); var any = "|" + Norm(text); var now = DateTime.Now; int win = Settings.Int("chat.filters.duplicateSec");
      lock (recent) {
        while (recent.Count > 0 && (now - recent.First.Value.Value).TotalSeconds > Math.Max(win, 30)) recent.RemoveFirst();
        if (Norm(text).Length > 0 && recent.Any(x => x.Key == me && (now - x.Value).TotalSeconds <= win)) Hit(m, "duplicate", Settings.Str("chat.filters.duplicate"));
        else if (Norm(text).Length > 12 && recent.Count(x => x.Key.EndsWith(any) && !x.Key.StartsWith(m.Key + "|")) >= 2) Hit(m, "copy-paste", Settings.Str("chat.filters.copyPaste"));
        recent.AddLast(new KeyValuePair<string, DateTime>(me, now)); while (recent.Count > 3000) recent.RemoveFirst(); } }
    static bool WordHit(string low, string w) { if (w.Length == 0) return false; if (w.Contains("*")) return Regex.IsMatch(low, "\\b" + Regex.Escape(w).Replace("\\*", "\\w*") + "\\b"); return Regex.IsMatch(low, "(^|\\W)" + Regex.Escape(w) + "($|\\W)"); }
  }
}
