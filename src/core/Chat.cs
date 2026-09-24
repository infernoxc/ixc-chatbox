// IXC Core - chat: one Streamer.bot connection shared by every chat page, the TTS queue and phones.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IXC {
  // what TTS and dedup need from a chat event (Streamer.bot payloads differ per platform and version, so parsing is tolerant)
  public class ChatMsg {
    public string Platform, Id, Name, Login, Text; public bool Broadcaster, Mod, Vip, Sub, Test, Me; public List<KeyValuePair<int, int>> EmoteSpans = new List<KeyValuePair<int, int>>();
    public HashSet<string> EmoteNames = new HashSet<string>(StringComparer.Ordinal); public DateTime At = DateTime.Now;
  }

  public static class Chat {
    public static readonly SbLink Sb = new SbLink();
    static readonly LinkedList<string> History = new LinkedList<string>();   // last raw events (pages and phones get them on connect)
    static readonly LinkedList<string> SeenIds = new LinkedList<string>(); static readonly HashSet<string> SeenSet = new HashSet<string>();
    public static int Received, Duplicates; public static Dictionary<string, int> PerPlatform = new Dictionary<string, int>();
    public static Dictionary<string, object> Viewers = J.D("twitch", null, "kick", null, "youtube", null);
    static DateTime lastEvent = DateTime.MinValue;

    static System.Threading.Timer tick;
    public static void Init() { Sb.OnEvent = OnEvent; Sb.Start(); tick = new System.Threading.Timer(_ => Tick(), null, 2000, 2000); }

    static void OnEvent(Dictionary<string, object> m, string raw) {
      string src = J.Str(m, "event.source", "").ToLowerInvariant(), type = J.Str(m, "event.type", ""); var data = J.Obj(m, "data") ?? new Dictionary<string, object>();
      if (src != "twitch" && src != "kick" && src != "youtube") return;
      lastEvent = DateTime.Now;
      if (type == "ViewerCountUpdate" || type == "StatisticsUpdated") { var n = FindCount(data, 0); if (n != null) lock (Viewers) Viewers[src] = n; Publish(raw, false); return; }
      if (type == "StreamOffline" || type == "BroadcastEnded") { lock (Viewers) Viewers[src] = null; Publish(raw, false); return; }
      if (type == "ChatMessage" || type == "Message") {
        var cm = Parse(src, data); if (cm == null) return;
        if (cm.Id != null) { var key = src + ":" + cm.Id; lock (SeenSet) { if (!SeenSet.Add(key)) { Duplicates++; Diag.Ev("duplicate chat event dropped (" + src + ")"); return; } SeenIds.AddLast(key); if (SeenIds.Count > 600) { SeenSet.Remove(SeenIds.First.Value); SeenIds.RemoveFirst(); } } }
        Received++; lock (PerPlatform) { int c; PerPlatform.TryGetValue(src, out c); PerPlatform[src] = c + 1; }
        Publish(raw, true);
        Tts.OnChat(cm);
        return; }
      Publish(raw, false);   // deletions / bans: pages may hide those messages
    }
    static void Publish(string raw, bool keep) {
      var json = "{\"type\":\"chat\",\"e\":" + raw + "}";
      if (keep) lock (History) { History.AddLast(raw); if (History.Count > 80) History.RemoveFirst(); }
      Hub.PublishRaw("chat", json); }
    static object FindCount(object o, int depth) {
      var d = o as Dictionary<string, object>; if (d == null || depth > 4) return null;
      foreach (var kv in d) if ((kv.Value is int || kv.Value is long || kv.Value is decimal || kv.Value is double) && Regex.IsMatch(kv.Key, "viewer|concurrent|watching", RegexOptions.IgnoreCase)) return Convert.ToInt32(kv.Value);
      foreach (var kv in d) { var r = FindCount(kv.Value, depth + 1); if (r != null) return r; } return null; }

    static string Pick(params object[] v) { foreach (var x in v) { var s = x as string; if (!string.IsNullOrWhiteSpace(s)) return s; } return null; }
    static object G(Dictionary<string, object> d, string k) { if (d == null) return null; object v; return d.TryGetValue(k, out v) ? v : null; }
    static bool B(object v) { return v is bool && (bool)v; }
    public static ChatMsg Parse(string src, Dictionary<string, object> d) {
      var m = G(d, "message") as Dictionary<string, object> ?? new Dictionary<string, object>();
      var u = (G(d, "user") ?? G(m, "user") ?? G(d, "sender")) as Dictionary<string, object> ?? new Dictionary<string, object>();
      var c = new ChatMsg { Platform = src };
      c.Name = Pick(G(m, "displayName"), G(m, "username"), G(u, "name"), G(u, "displayName"), G(u, "display_name"), G(u, "login"), G(u, "username"), G(d, "displayName"), G(d, "userName"), G(d, "username"), G(d, "user_name"));
      c.Login = Pick(G(m, "username"), G(u, "login"), G(u, "username"), G(u, "name"), c.Name);
      c.Text = Pick(G(m, "message"), G(m, "text"), G(m, "content"), G(d, "message") as string, G(d, "text"), G(d, "messageText"), G(d, "content"));
      if (c.Text == null) { var parts = (G(m, "parts") ?? G(d, "parts")) as ArrayList; if (parts != null) { var sb = new StringBuilder(); foreach (var p in parts) { var pd = p as Dictionary<string, object>; if (pd != null && G(pd, "type") as string != "emote") sb.Append(G(pd, "text") as string); } c.Text = sb.ToString(); } }
      if (string.IsNullOrWhiteSpace(c.Text)) return null;
      c.Id = Pick(G(m, "msgId"), G(m, "messageId"), G(m, "id") as string, G(d, "messageId"), G(d, "msgId"), G(d, "eventId"), G(d, "id") as string);
      int role = 0; foreach (var r in new[] { G(m, "role"), G(u, "role"), G(d, "role") }) { if (r is int) role = Math.Max(role, (int)r); var rs = r as string; if (rs != null) { rs = rs.ToLowerInvariant(); if (rs.Contains("broadcaster") || rs == "owner") role = 4; else if (rs.Contains("mod")) role = Math.Max(role, 3); else if (rs == "vip") role = Math.Max(role, 2); } }
      c.Broadcaster = role >= 4 || B(G(u, "isOwner")) || B(G(u, "isBroadcaster")) || B(G(d, "isBroadcaster")) || B(G(m, "isBroadcaster"));
      c.Mod = role == 3 || B(G(u, "isModerator")) || B(G(m, "isModerator")); c.Vip = role == 2 || B(G(u, "isVip"));
      c.Sub = B(G(m, "subscriber")) || B(G(u, "subscribed")) || B(G(u, "isSubscribed")) || B(G(u, "isSponsor"));
      c.Test = B(G(m, "isTest")) || B(G(d, "isTest")); c.Me = B(G(m, "isMe"));
      foreach (var ev in new[] { G(m, "emotes"), G(d, "emotes") }) { var a = ev as ArrayList; if (a == null) continue;
        foreach (var e in a) { var ed = e as Dictionary<string, object>; if (ed == null) continue; var nm = Pick(G(ed, "name"), G(ed, "code")); if (nm != null) c.EmoteNames.Add(nm);
          if (G(ed, "startIndex") is int && G(ed, "endIndex") is int) c.EmoteSpans.Add(new KeyValuePair<int, int>((int)G(ed, "startIndex"), (int)G(ed, "endIndex"))); } }
      return c; }

    public static Dictionary<string, object> DiagInfo() {
      lock (Viewers) return J.D("streamerbot", Sb.Ready ? "connected" : Sb.Status, "auth", Sb.AuthState, "connectedSince", Sb.Ready ? Sb.Since.ToString("HH:mm:ss") : null, "reconnects", Math.Max(0, Sb.Connects - 1),
        "messages", Received, "perPlatform", new Dictionary<string, int>(PerPlatform), "duplicatesDropped", Duplicates, "lastEvent", lastEvent == DateTime.MinValue ? "none yet" : lastEvent.ToString("HH:mm:ss"),
        "viewers", new Dictionary<string, object>(Viewers), "yourAccounts", Own()); }
    public static List<string> Own() { var l = new List<string>(); lock (Sb.Broadcasters) l.AddRange(Sb.Broadcasters); l.AddRange(J.List(Cfg.D, "tts.ownNames")); return l.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }

    // ---------- sending (reply box, phone) ----------
    public static async Task<List<Dictionary<string, object>>> SendChat(string platform, string message) {
      var res = new List<Dictionary<string, object>>(); message = (message ?? "").Trim(); if (message.Length == 0) return res; if (message.Length > 480) message = message.Substring(0, 480);
      platform = (platform ?? "all").ToLowerInvariant(); var targets = platform == "all" ? new[] { "twitch", "kick", "youtube" } : new[] { platform };
      var tasks = targets.Where(p => p == "twitch" || p == "kick" || p == "youtube").Select(async p => {
        var r = await Sb.Request(J.D("request", "SendMessage", "platform", p, "message", message, "bot", false, "internal", true), 6000);
        bool ok = r != null && J.Str(r, "status", "") == "ok";
        return J.D("platform", p, "ok", ok, "error", ok ? "" : r == null ? (Sb.Ready ? "no answer from Streamer.bot" : "Streamer.bot not connected") : J.Str(r, "error", "failed")); }).ToList();
      foreach (var t in tasks) res.Add(await t);
      Diag.Ev("chat sent to " + string.Join("+", res.Where(r => (bool)r["ok"]).Select(r => r["platform"])) + (res.Any(r => !(bool)r["ok"]) ? " (failed: " + string.Join(",", res.Where(r => !(bool)r["ok"]).Select(r => r["platform"])) + ")" : ""));
      return res; }
    static Dictionary<string, object> sugCache; static DateTime sugAt;
    public static async Task<Dictionary<string, object>> Suggest() {
      if (sugCache != null && (DateTime.Now - sugAt).TotalSeconds < 30) return sugCache;
      var cmds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
      var rc = await Sb.Request(J.D("request", "GetCommands"), 3000);
      if (rc != null) { var arr = J.Get(rc, "commands") as ArrayList; if (arr != null) foreach (var o in arr) { var od = o as Dictionary<string, object>; if (od == null) continue; if (!J.Bool(od, "enabled", true)) continue; foreach (var cm in J.List(od, "commands")) if (cm.StartsWith("!")) cmds.Add(cm); } }
      var cf = J.Str(Cfg.D, "chat.extraCommandsFile", J.Str(Cfg.D, "chatRelay.extraCommandsFile", ""));
      if (cf.Length > 0 && File.Exists(cf)) { var cs = File.ReadAllText(cf);
        foreach (Match mm in Regex.Matches(cs, "\\{\\s*\"([a-z0-9]+)\"\\s*,")) cmds.Add("!" + mm.Groups[1].Value);
        foreach (Match mm in Regex.Matches(cs, "case\\s+\"([a-z0-9]+)\"\\s*:\\s*(?:Reply|return|if|Handle|var|string|\\{)")) cmds.Add("!" + mm.Groups[1].Value); }
      foreach (var h in J.List(Cfg.D, "chat.hiddenCommands").Concat(J.List(Cfg.D, "chatRelay.hiddenCommands"))) cmds.Remove(h.StartsWith("!") ? h : "!" + h);
      var users = new List<object>(); var rv = await Sb.Request(J.D("request", "GetActiveViewers"), 3000);
      if (rv != null) { var va = J.Get(rv, "viewers") as ArrayList; if (va != null) foreach (var o in va) { var vd = o as Dictionary<string, object>; if (vd == null) continue; var nm = Pick(G(vd, "display"), G(vd, "login"), G(vd, "name")); if (nm != null) users.Add(J.D("name", nm, "platform", J.Str(vd, "type", "twitch").ToLowerInvariant())); } }
      sugCache = J.D("commands", cmds.ToList(), "users", users); sugAt = DateTime.Now; return sugCache; }

    // ---------- API ----------
    public static bool Api(HttpListenerContext ctx, string path, string m, System.Collections.Specialized.NameValueCollection q) {
      if (path == "/api/chat/send" && m == "POST") { var b = J.Parse(Http.Body(ctx)); var r = SendChat(J.Str(b, "platform", "all"), J.Str(b, "message", "")).Result; Http.Json(ctx, J.D("results", r)); return true; }
      if (path == "/api/chat/suggest") { Http.Json(ctx, Suggest().Result); return true; }
      if (path == "/api/chat/history") { lock (History) Http.Send(ctx, 200, "{\"events\":[" + string.Join(",", History) + "]}", null); return true; }
      if (path == "/api/chat/reconnect" && m == "POST") { Sb.Kick(); Http.Json(ctx, J.D("ok", true)); return true; }
      if (path == "/api/chat/inject" && m == "POST") {   // tests only (--test): feed a fake chat event through the same path as real ones
        if (!Program.TestMode) { Http.Json(ctx, 403, J.D("error", "only in test mode")); return true; }
        var body = Http.Body(ctx); OnEvent(J.Parse(body), body); Http.Json(ctx, J.D("ok", true)); return true; }
      return false; }
    public static void OnWs(Client c, string type, Dictionary<string, object> msg) {
      if (type == "hello" && c.Topics.Contains("chat")) { string hist; lock (History) hist = string.Join(",", History); Hub.Send(c, "{\"type\":\"chat.history\",\"events\":[" + hist + "]}");
        lock (Viewers) Hub.Send(c, J.D("type", "chat.status", "streamerbot", Sb.Ready, "status", Sb.Status, "viewers", new Dictionary<string, object>(Viewers))); }
      else if (type == "chat.send") { var id = J.Str(msg, "reqId", ""); var t = SendChat(J.Str(msg, "platform", "all"), J.Str(msg, "message", "")).ContinueWith(r => Hub.Send(c, J.D("type", "chat.sent", "reqId", id, "results", r.Result))); }
      else if (type == "chat.suggest") { var t = Suggest().ContinueWith(r => { var d = new Dictionary<string, object>(r.Result); d["type"] = "chat.suggest"; Hub.Send(c, d); }); }
    }
    static bool wasReady;
    public static void Tick() { if (Sb.Ready != wasReady) { wasReady = Sb.Ready; Hub.Publish("chat", J.D("type", "chat.status", "streamerbot", Sb.Ready, "status", Sb.Status)); } }
  }
}
