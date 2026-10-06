// IXC Core - built-in chat connections: Twitch, Kick, YouTube, Rumble, plus Streamer.bot (optional).
// Reading chat needs no sign-in on Twitch, Kick and YouTube. Signing in (Accounts.cs) adds replies, follower events and
// viewer counts where the platform requires it. Every platform is isolated: its own thread, state and back-off.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  // endpoints can be pointed at local test servers (only in --test mode)
  public static class Ep {
    public static string Get(string name, string def) { if (!Program.TestMode) return def; var v = Environment.GetEnvironmentVariable("IXC_EP_" + name.ToUpperInvariant()); return string.IsNullOrEmpty(v) ? def : v; }
  }

  public class ViewerInfo {
    public int? Count; public bool? Live; public DateTime At = DateTime.MinValue; public string Note = "", Source = ""; public DateTime? StartedAt;
    public void Set(int? count, bool? live, string source, DateTime? started) { Count = count; Live = live; Source = source; StartedAt = started; At = DateTime.Now; Note = ""; Viewers.Changed(); }
    public void Fail(string note) { Note = note; Viewers.Changed(); }
  }

  public abstract class ChatSource {
    public string Id, Label; public ViewerInfo Viewers = new ViewerInfo();
    public abstract bool Configured { get; }
    public abstract string State { get; }
    public abstract string Detail { get; }
    public virtual DateTime StateSince { get { return DateTime.Now; } }
    public abstract void Start();
    public abstract void Restart();
    public virtual void Kick() { }
    public virtual bool CanRead { get { return Configured && State == "connected"; } }
    public virtual bool CanSend { get { return false; } }
    public virtual string SendNote { get { return "IXC can't send messages to " + Label; } }
    public virtual Task<string> Send(string text) { return Task.FromResult(SendNote); }
    public virtual List<string> OwnNames() { return new List<string>(); }
    public virtual void PollViewers() { }
    public string Channel { get { return Settings.Str("platforms." + Id + ".channel").Trim(); } }
    public bool Enabled { get { return Settings.Bool("platforms." + Id + ".enabled"); } }
    public Dictionary<string, object> Status() {
      var acc = Accounts.Info(Id);
      return J.D("id", Id, "label", Label, "configured", Configured, "enabled", Enabled, "channel", Id == "rumble" ? (Accounts.RumbleUrl().Length > 0 ? "set" : "") : Channel, "state", Configured ? State : "off", "detail", Configured ? Detail : (Enabled ? "add your channel" : "turned off"),
        "canSend", CanSend || ViaSb, "sendVia", CanSend ? "ixc" : ViaSb ? "streamerbot" : "", "sendNote", CanSend || ViaSb ? "" : SendNote, "account", acc, "viewers", global::IXC.Viewers.View(this)); }
    // no IXC sign-in for this platform, but Streamer.bot is connected: replies go through Streamer.bot (Rumble can't send at all)
    public bool ViaSb { get { return !CanSend && Id != "rumble" && Platforms.Sb.Ready; } }
    protected Timer viewerT;
    protected void StartViewerPolling() {
      viewerT = new Timer(_ => { if (!Configured) return; try { PollViewers(); } catch (Exception e) { Viewers.Fail(U.Plain(e)); Log.Debug("net", Label + " viewers: " + U.Plain(e)); } }, null, 3000, Settings.Int("viewers.refreshSec") * 1000);
      Settings.Changed += k => { if (k == "viewers.refreshSec") viewerT.Change(1000, Settings.Int("viewers.refreshSec") * 1000); }; }
    public void PollNow() { if (viewerT != null) viewerT.Change(200, Settings.Int("viewers.refreshSec") * 1000); }
    protected static void Publish() { Hub.Publish("chat", J.D("type", "chat.status", "platforms", Platforms.StatusAll())); Hub.Publish("status", global::IXC.Status.Msg()); }
  }

  // ======================= Twitch (IRC over WebSocket; Helix with a sign-in) =======================
  public class TwitchSource : ChatSource {
    readonly TwitchIrc irc; readonly TwitchEventSub es;
    public TwitchSource() { Id = "twitch"; Label = "Twitch"; irc = new TwitchIrc(this); es = new TwitchEventSub(this); }
    public override bool Configured { get { return Enabled && Login.Length > 0; } }
    public string Login { get { return Regex.Replace(Channel.ToLowerInvariant(), "^(https?://)?(www\\.)?twitch\\.tv/|[@#\\s/]", ""); } }
    public override string State { get { return irc.State; } }
    public override string Detail { get { return irc.Detail; } }
    public override DateTime StateSince { get { return irc.Since; } }
    public override void Start() { irc.Start(); es.Start(); StartViewerPolling(); }
    public override void Restart() { irc.Restart(); es.Restart(); PollNow(); }
    public override void Kick() { irc.Kick(); es.Kick(); }
    public override bool CanSend { get { return irc.Ready && irc.Authed; } }
    public override string SendNote { get { return !Accounts.Has("twitch") ? "Sign in to Twitch (Accounts) to reply" : irc.Ready ? "Twitch sign-in expired - sign in again" : "Twitch chat is not connected"; } }
    public override async Task<string> Send(string text) {
      if (!CanSend) return SendNote;
      try { await irc.Tx("PRIVMSG #" + Login + " :" + text); irc.EchoOwn(text); return null; } catch (Exception e) { return U.Plain(e); } }
    public override List<string> OwnNames() { var a = Accounts.Info("twitch"); var l = new List<string> { Login }; if (a != null) l.Add(J.Str(a, "login", "")); return l.Where(x => x.Length > 0).ToList(); }
    public override void PollViewers() {
      if (!Accounts.Has("twitch")) { PublicViewers(); return; }
      var r = Accounts.Helix("GET", "streams?user_login=" + Uri.EscapeDataString(Login), null);
      if (r == null) { Viewers.Fail("Twitch sign-in needed"); return; }
      if (!r.Ok) { Viewers.Fail("Twitch: " + (r.Error ?? ("HTTP " + r.Code))); return; }
      var data = J.Get(J.Parse(r.Body), "data") as ArrayList; var s = data != null && data.Count > 0 ? data[0] as Dictionary<string, object> : null;
      if (s == null) Viewers.Set(null, false, "Twitch", null);
      else { DateTime st; Viewers.Set(J.Int(s, "viewer_count", 0), true, "Twitch", DateTime.TryParse(J.Str(s, "started_at", ""), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out st) ? (DateTime?)st.ToLocalTime() : null); } }
    // not signed in: the same public data twitch.tv's own pages use (no account needed; unofficial, so it may change)
    void PublicViewers() {
      var q = "[{\"query\":\"query{user(login:\\\"" + Regex.Replace(Login, "[^A-Za-z0-9_]", "") + "\\\"){stream{viewersCount createdAt}}}\"}]";
      var r = Http.Request("POST", Ep.Get("twitch_gql", "https://gql.twitch.tv/gql"), q, "text/plain;charset=UTF-8", new Dictionary<string, string> { { "Client-Id", "kimne78kx3ncx6brgo4mv6wki5h1ko" } }, 12000);
      if (!r.Ok) { Viewers.Fail("Twitch viewers: " + (r.Error ?? ("HTTP " + r.Code)) + " - sign in to Twitch for the official numbers"); return; }
      var arr = J.Parse("{\"a\":" + r.Body + "}"); var a = J.Get(arr, "a") as ArrayList; var d = a != null && a.Count > 0 ? a[0] as Dictionary<string, object> : null;
      if (d == null || J.Get(d, "data.user") == null) { Viewers.Fail("Twitch has no channel called \"" + Login + "\""); return; }
      var stream = J.Obj(d, "data.user.stream");
      if (stream == null) { Viewers.Set(null, false, "Twitch", null); return; }
      DateTime st; Viewers.Set(J.Int(stream, "viewersCount", 0), true, "Twitch", DateTime.TryParse(J.Str(stream, "createdAt", ""), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out st) ? (DateTime?)st.ToLocalTime() : null); }
    // follower / account age checks for commands and song requests (Helix, cached)
    readonly Dictionary<string, KeyValuePair<DateTime, object>> cache = new Dictionary<string, KeyValuePair<DateTime, object>>();
    public bool? IsFollower(string userId) {
      if (string.IsNullOrEmpty(userId) || !Accounts.Has("twitch")) return null; var key = "f:" + userId;
      lock (cache) { KeyValuePair<DateTime, object> v; if (cache.TryGetValue(key, out v) && (DateTime.Now - v.Key).TotalMinutes < 10) return (bool?)v.Value; }
      var bid = J.Str(Accounts.Info("twitch") ?? new Dictionary<string, object>(), "userId", ""); if (bid.Length == 0) return null;
      var r = Accounts.Helix("GET", "channels/followers?broadcaster_id=" + bid + "&user_id=" + userId, null); if (r == null || !r.Ok) return null;
      var d = J.Get(J.Parse(r.Body), "data") as ArrayList; bool f = d != null && d.Count > 0; lock (cache) cache[key] = new KeyValuePair<DateTime, object>(DateTime.Now, f); return f; }
    public DateTime? AccountCreated(string userId) {
      if (string.IsNullOrEmpty(userId) || !Accounts.Has("twitch")) return null; var key = "c:" + userId;
      lock (cache) { KeyValuePair<DateTime, object> v; if (cache.TryGetValue(key, out v)) return (DateTime?)v.Value; }
      var r = Accounts.Helix("GET", "users?id=" + userId, null); if (r == null || !r.Ok) return null;
      var d = J.Get(J.Parse(r.Body), "data") as ArrayList; DateTime t; if (d == null || d.Count == 0 || !DateTime.TryParse(J.Str((Dictionary<string, object>)d[0], "created_at", ""), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out t)) return null;
      lock (cache) { cache[key] = new KeyValuePair<DateTime, object>(DateTime.Now, (DateTime?)t); if (cache.Count > 5000) cache.Clear(); } return t; }
  }

  public class TwitchIrc : WsLink {
    readonly TwitchSource src; public volatile bool Authed; string nick = ""; bool forceAnon; string room = "";
    readonly ManualResetEventSlim welcome = new ManualResetEventSlim(false), joined = new ManualResetEventSlim(false); volatile string failure;
    public TwitchIrc(TwitchSource s) { src = s; Name = "Twitch chat"; Area = "chat"; IdleTimeoutSec = 400; }
    protected override bool Wanted() { return src.Configured; }
    protected override string WhyNotWanted() { return src.Enabled ? "add your Twitch channel name" : "turned off"; }
    protected override string Url() { return Ep.Get("twitch_irc", "wss://irc-ws.chat.twitch.tv:443"); }
    protected override void StateChanged() { ChatSourcePublish(); }
    static void ChatSourcePublish() { Hub.Publish("chat", J.D("type", "chat.status", "platforms", Platforms.StatusAll())); Hub.Publish("status", Status.Msg()); }
    protected override async Task Handshake() {
      welcome.Reset(); joined.Reset(); failure = null; room = src.Login;
      string token = forceAnon ? null : Accounts.AccessToken("twitch", "chat:edit");
      var acc = Accounts.Info("twitch");
      if (token != null && acc != null) { nick = J.Str(acc, "login", ""); await Tx("PASS oauth:" + token); await Tx("NICK " + nick); Authed = true; }
      else { nick = "justinfan" + (10000 + U.Rand(80000)); await Tx("PASS SCHMOOPIIE"); await Tx("NICK " + nick); Authed = false; }
      await Tx("CAP REQ :twitch.tv/tags twitch.tv/commands");
      if (!await Task.Run(() => welcome.Wait(8000)) || failure != null) {
        if (Authed && failure != null && failure.Contains("auth")) { Accounts.MarkInvalid("twitch", "Twitch did not accept the sign-in"); forceAnon = true; throw new Exception("Twitch sign-in was refused - reading chat without it"); }
        throw new Exception(failure ?? "Twitch chat did not answer"); }
      await Tx("JOIN #" + room);
      if (!await Task.Run(() => joined.Wait(8000))) { if (failure != null) { SetState("unavailable", failure); throw new Exception(failure); } throw new Exception("could not join #" + room); }
      forceAnon = false; }
    public void EchoOwn(string text) {   // Twitch doesn't send your own messages back to you: show them in IXC
      var acc = Accounts.Info("twitch"); var name = acc == null ? nick : J.Str(acc, "displayName", nick);
      var m = new ChatMsg { Platform = "twitch", Id = "own-" + U.Token(6), Name = name, Login = nick, Text = text, Me = true, Broadcaster = string.Equals(nick, src.Login, StringComparison.OrdinalIgnoreCase) };
      Chat.Ingest(m); }
    protected override void OnText(string raw) { foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) Line(line); }
    static string Unesc(string v) { var sb = new StringBuilder(); for (int i = 0; i < v.Length; i++) { if (v[i] == '\\' && i + 1 < v.Length) { char n = v[++i]; sb.Append(n == 's' ? ' ' : n == ':' ? ';' : n == 'r' ? '\r' : n == 'n' ? '\n' : n); } else sb.Append(v[i]); } return sb.ToString(); }
    void Line(string line) {
      var tags = new Dictionary<string, string>(); string rest = line;
      if (rest.StartsWith("@")) { int sp = rest.IndexOf(' '); if (sp < 0) return; foreach (var t in rest.Substring(1, sp - 1).Split(';')) { int eq = t.IndexOf('='); if (eq > 0) tags[t.Substring(0, eq)] = Unesc(t.Substring(eq + 1)); else tags[t] = ""; } rest = rest.Substring(sp + 1); }
      string prefix = ""; if (rest.StartsWith(":")) { int sp = rest.IndexOf(' '); if (sp < 0) return; prefix = rest.Substring(1, sp - 1); rest = rest.Substring(sp + 1); }
      string trailing = null; int ti = rest.IndexOf(" :"); if (ti >= 0) { trailing = rest.Substring(ti + 2); rest = rest.Substring(0, ti); } else if (rest.StartsWith(":")) { trailing = rest.Substring(1); rest = ""; }
      var parts = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); if (parts.Length == 0) return; var cmd = parts[0];
      string login = prefix.Contains("!") ? prefix.Substring(0, prefix.IndexOf('!')) : prefix;
      switch (cmd) {
        case "PING": var t = Tx("PONG :" + (trailing ?? "tmi.twitch.tv")); break;
        case "001": welcome.Set(); break;
        case "RECONNECT": Log.Info("chat", "Twitch asked IXC to reconnect"); try { ws.Abort(); } catch { } break;
        case "JOIN": if (string.Equals(login, nick, StringComparison.OrdinalIgnoreCase)) joined.Set(); break;
        case "ROOMSTATE": joined.Set(); break;
        case "NOTICE": {
          var id = Get(tags, "msg-id"); var text = trailing ?? "";
          if (text.Contains("Login authentication failed") || text.Contains("Improperly formatted auth")) { failure = "auth: " + text; welcome.Set(); }
          else if (id == "msg_channel_suspended" || id == "tos_ban" || id == "msg_banned") { failure = "Twitch: " + text; joined.Set(); }
          else if (id == "msg_ratelimit" || id == "msg_duplicate" || id == "msg_slowmode" || id == "msg_followersonly" || id == "msg_subsonly" || id == "msg_emoteonly") { Log.Info("chat", "Twitch didn't send your message: " + text); Hub.Publish("chat", J.D("type", "chat.reply", "platform", "twitch", "text", "", "error", "Twitch: " + text)); }
          else Log.Debug("chat", "Twitch notice: " + text);
          break; }
        case "PRIVMSG": {
          var text = trailing ?? ""; bool action = false;
          if (text.StartsWith("\u0001ACTION ") && text.EndsWith("\u0001")) { text = text.Substring(8, text.Length - 9); action = true; }
          var m = new ChatMsg { Platform = "twitch", Id = Get(tags, "id"), UserId = Get(tags, "user-id") ?? "", Login = login, Name = NonEmpty(Get(tags, "display-name"), login), Color = NonEmpty(Get(tags, "color"), null), Text = text };
          Roles(m, tags); m.FirstTime = Get(tags, "first-msg") == "1"; if (action) m.Flags.Add("action");
          if (string.Equals(login, nick, StringComparison.OrdinalIgnoreCase) && Authed) m.Me = true;
          Emotes(m, Get(tags, "emotes")); Chat.Ingest(m); break; }
        case "USERNOTICE": {
          var kind = Get(tags, "msg-id") ?? ""; var sys = Get(tags, "system-msg") ?? ""; string k = null;
          if (kind == "sub" || kind == "resub") k = "sub"; else if (kind == "subgift" || kind == "submysterygift" || kind == "giftpaidupgrade" || kind == "anongiftpaidupgrade") k = "gift"; else if (kind == "raid") k = "raid";
          if (trailing != null && trailing.Length > 0) { var m = new ChatMsg { Platform = "twitch", Id = Get(tags, "id") + "-text", UserId = Get(tags, "user-id") ?? "", Login = Get(tags, "login") ?? login, Name = NonEmpty(Get(tags, "display-name"), login), Color = NonEmpty(Get(tags, "color"), null), Text = trailing }; Roles(m, tags); Emotes(m, Get(tags, "emotes")); Chat.Ingest(m); }
          if (k != null) Chat.Ingest(new ChatMsg { Platform = "twitch", Id = Get(tags, "id"), Kind = k, Login = Get(tags, "login") ?? login, Name = NonEmpty(Get(tags, "display-name"), Get(tags, "login")), Text = sys, Amount = k == "raid" ? (Get(tags, "msg-param-viewerCount") ?? "") + " viewers" : "" });
          break; }
        case "CLEARMSG": Chat.Delete("twitch", Get(tags, "target-msg-id"), null); break;
        case "CLEARCHAT": if (string.IsNullOrEmpty(trailing)) Chat.ClearAll("twitch"); else Chat.Delete("twitch", null, trailing); break;
      } }
    static string Get(Dictionary<string, string> t, string k) { string v; return t.TryGetValue(k, out v) ? v : null; }
    static string NonEmpty(string a, string b) { return string.IsNullOrEmpty(a) ? b : a; }
    static void Roles(ChatMsg m, Dictionary<string, string> tags) {
      var badges = Get(tags, "badges") ?? "";
      m.Broadcaster = badges.Contains("broadcaster/"); m.Mod = Get(tags, "mod") == "1" || badges.Contains("moderator/"); m.Vip = badges.Contains("vip/") || Get(tags, "vip") != null;
      m.Sub = Get(tags, "subscriber") == "1" || badges.Contains("subscriber/") || badges.Contains("founder/"); }
    // "25:0-4,12-16/1902:6-10" - positions are Unicode code points
    static void Emotes(ChatMsg m, string spec) {
      var cps = new List<string>(); for (int i = 0; i < m.Text.Length; i++) { if (char.IsHighSurrogate(m.Text[i]) && i + 1 < m.Text.Length) { cps.Add(m.Text.Substring(i, 2)); i++; } else cps.Add(m.Text[i].ToString()); }
      var spans = new List<Tuple<int, int, string>>();
      if (!string.IsNullOrEmpty(spec)) foreach (var e in spec.Split('/')) { var c = e.IndexOf(':'); if (c < 0) continue; var id = e.Substring(0, c);
        foreach (var r in e.Substring(c + 1).Split(',')) { var d = r.Split('-'); int a, b; if (d.Length == 2 && int.TryParse(d[0], out a) && int.TryParse(d[1], out b) && a >= 0 && b < cps.Count && a <= b) spans.Add(Tuple.Create(a, b, id)); } }
      int pos = 0; var sb = new StringBuilder();
      foreach (var s in spans.OrderBy(x => x.Item1)) { if (s.Item1 < pos) continue;
        for (int i = pos; i < s.Item1; i++) sb.Append(cps[i]); if (sb.Length > 0) { m.Parts.Add(J.D("t", "text", "v", sb.ToString())); sb.Clear(); }
        var name = string.Concat(cps.Skip(s.Item1).Take(s.Item2 - s.Item1 + 1)); m.EmoteNames.Add(name); m.EmoteSpans.Add(new KeyValuePair<int, int>(s.Item1, s.Item2));
        m.Parts.Add(J.D("t", "emote", "name", name, "url", "https://static-cdn.jtvnw.net/emoticons/v2/" + Uri.EscapeDataString(s.Item3) + "/default/dark/2.0")); pos = s.Item2 + 1; }
      if (m.Parts.Count > 0) { for (int i = pos; i < cps.Count; i++) sb.Append(cps[i]); if (sb.Length > 0) m.Parts.Add(J.D("t", "text", "v", sb.ToString())); } }
  }

  // follower events need EventSub (a Twitch sign-in with the follower permission)
  public class TwitchEventSub : WsLink {
    readonly TwitchSource src; string nextUrl; readonly ManualResetEventSlim welcomed = new ManualResetEventSlim(false); string session;
    public TwitchEventSub(TwitchSource s) { src = s; Name = "Twitch events"; Area = "chat"; IdleTimeoutSec = 30; }
    protected override bool Wanted() { var a = Accounts.Info("twitch"); return src.Configured && a != null && J.List(a, "scopes").Contains("moderator:read:followers") && !J.Bool(a, "invalid", false) && string.Equals(J.Str(a, "login", ""), src.Login, StringComparison.OrdinalIgnoreCase); }
    protected override string WhyNotWanted() { return "follower alerts need you signed in to Twitch as the channel owner"; }
    protected override string Url() { var u = nextUrl ?? Ep.Get("twitch_eventsub", "wss://eventsub.wss.twitch.tv/ws"); nextUrl = null; return u; }
    protected override void BeforeConnect() { welcomed.Reset(); }
    protected override async Task Handshake() {
      if (!await Task.Run(() => welcomed.Wait(10000))) throw new Exception("no welcome from Twitch events");
      var uid = J.Str(Accounts.Info("twitch"), "userId", "");
      var body = J.Ser(J.D("type", "channel.follow", "version", "2", "condition", J.D("broadcaster_user_id", uid, "moderator_user_id", uid), "transport", J.D("method", "websocket", "session_id", session)));
      var r = await Task.Run(() => Accounts.Helix("POST", "eventsub/subscriptions", body));
      if (r == null || !r.Ok) throw new Exception("Twitch didn't allow follower alerts (" + (r == null ? "sign-in needed" : r.Code + " " + U.Trunc(r.Body, 120)) + ")"); }
    protected override void OnText(string raw) {
      var m = J.Parse(raw); var type = J.Str(m, "metadata.message_type", "");
      if (type == "session_welcome") { session = J.Str(m, "payload.session.id", ""); IdleTimeoutSec = J.Int(m, "payload.session.keepalive_timeout_seconds", 10) + 20; welcomed.Set(); }
      else if (type == "session_reconnect") { nextUrl = J.Str(m, "payload.session.reconnect_url", null); try { ws.Abort(); } catch { } }
      else if (type == "notification" && J.Str(m, "payload.subscription.type", "") == "channel.follow") {
        var e = J.Obj(m, "payload.event"); Chat.Ingest(new ChatMsg { Platform = "twitch", Id = "follow-" + J.Str(e, "user_id", "") + "-" + J.Str(e, "followed_at", ""), Kind = "follow", Name = J.Str(e, "user_name", "someone"), Login = J.Str(e, "user_login", ""), Text = "followed" }); } }
  }

  // ======================= Kick (public chat socket; official API with a sign-in) =======================
  public class KickSource : ChatSource {
    readonly KickPusher pusher; public string ChannelId = "", UserId = "", ChatroomId = ""; public string ResolveError = "";
    public KickSource() { Id = "kick"; Label = "Kick"; pusher = new KickPusher(this); }
    public string Slug { get { return Regex.Replace(Channel.ToLowerInvariant(), "^(https?://)?(www\\.)?kick\\.com/|[@\\s/]", "").Replace("_", "-"); } }
    public override bool Configured { get { return Enabled && Slug.Length > 0; } }
    public override string State { get { return pusher.State; } }
    public override string Detail { get { return pusher.Detail; } }
    public override DateTime StateSince { get { return pusher.Since; } }
    public override void Start() { pusher.Start(); StartViewerPolling(); }
    public override void Restart() { ChatroomId = ""; pusher.Restart(); PollNow(); }
    public override void Kick() { pusher.Kick(); }
    public override bool CanSend { get { return Accounts.Has("kick") && !Accounts.IsInvalid("kick"); } }
    public override string SendNote { get { return !Accounts.Has("kick") ? "Sign in to Kick (Accounts) to reply" : "Kick sign-in expired - sign in again"; } }
    // channel lookup: kick.com's public channel data (has the chat room); the official API as a fallback (no chat room id)
    public bool Resolve() {
      var slug = Slug; var cached = J.Str(Cfg.D, "platforms.kick.cache", "");
      if (cached.StartsWith(slug + "|")) { var p = cached.Split('|'); if (p.Length >= 4) { ChannelId = p[1]; UserId = p[2]; ChatroomId = p[3]; } }
      var r = Http.Request("GET", Ep.Get("kick_web", "https://kick.com") + "/api/v2/channels/" + Uri.EscapeDataString(slug), null, null, new Dictionary<string, string> { { "Accept", "application/json" }, { "Referer", "https://kick.com/" + slug } }, 12000);
      if (r.Ok) { var d = J.Parse(r.Body); ChannelId = J.Str(d, "id", ""); UserId = J.Str(d, "user_id", ""); ChatroomId = J.Str(d, "chatroom.id", "");
        if (ChatroomId.Length > 0) { Cfg.Set("platforms.kick.cache", slug + "|" + ChannelId + "|" + UserId + "|" + ChatroomId); Cfg.Save(); }
        ResolveError = ""; UpdateViewers(d); return ChatroomId.Length > 0; }
      if (r.Code == 404) { ResolveError = "Kick has no channel called \"" + slug + "\""; ChatroomId = ""; return false; }
      ResolveError = r.Code == 403 || r.Code == 429 ? "kick.com refused the lookup (" + r.Code + ")" : "kick.com unreachable (" + (r.Error ?? r.Code.ToString()) + ")";
      if (UserId.Length == 0 && Accounts.Has("kick")) { var o = Accounts.KickApi("GET", "channels?slug=" + Uri.EscapeDataString(slug), null); if (o != null && o.Ok) { var arr = J.Get(J.Parse(o.Body), "data") as ArrayList; if (arr != null && arr.Count > 0) UserId = J.Str((Dictionary<string, object>)arr[0], "broadcaster_user_id", ""); } }
      return ChatroomId.Length > 0; }   // a cached chat room keeps chat working while kick.com blocks lookups
    void UpdateViewers(Dictionary<string, object> d) {
      var ls = J.Obj(d, "livestream"); if (ls == null) { Viewers.Set(null, false, "Kick", null); return; }
      DateTime st; var started = DateTime.TryParse(J.Str(ls, "created_at", J.Str(ls, "start_time", "")), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out st) ? (DateTime?)st.ToLocalTime() : null;
      Viewers.Set(J.Int(ls, "viewer_count", J.Int(ls, "viewers", 0)), J.Bool(ls, "is_live", true), "Kick", started); }
    public override void PollViewers() {
      var r = Http.Request("GET", Ep.Get("kick_web", "https://kick.com") + "/api/v2/channels/" + Uri.EscapeDataString(Slug), null, null, new Dictionary<string, string> { { "Accept", "application/json" } }, 12000);
      if (r.Ok) { UpdateViewers(J.Parse(r.Body)); return; }
      if (Accounts.Has("kick") && UserId.Length > 0) {
        var o = Accounts.KickApi("GET", "livestreams?broadcaster_user_id=" + UserId, null);
        if (o != null && o.Ok) { var arr = J.Get(J.Parse(o.Body), "data") as ArrayList; if (arr == null || arr.Count == 0) Viewers.Set(null, false, "Kick", null); else { var s = (Dictionary<string, object>)arr[0]; DateTime st; Viewers.Set(J.Int(s, "viewer_count", 0), true, "Kick", DateTime.TryParse(J.Str(s, "started_at", ""), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out st) ? (DateTime?)st.ToLocalTime() : null); } return; } }
      Viewers.Fail(r.Code == 403 ? "kick.com is blocking viewer lookups right now" + (Accounts.Has("kick") ? "" : " - sign in to Kick to use the official API") : "Kick: " + (r.Error ?? ("HTTP " + r.Code))); }
    public override async Task<string> Send(string text) {
      if (!CanSend) return SendNote;
      if (UserId.Length == 0) await Task.Run(() => Resolve()); if (UserId.Length == 0) { var a = Accounts.Info("kick"); if (a != null && string.Equals(J.Str(a, "login", ""), Slug, StringComparison.OrdinalIgnoreCase)) UserId = J.Str(a, "userId", ""); }
      if (UserId.Length == 0) return "couldn't find the Kick channel";
      var r = await Task.Run(() => Accounts.KickApi("POST", "chat", J.Ser(J.D("broadcaster_user_id", long.Parse(UserId), "content", text, "type", "user"))));
      if (r == null) return SendNote; if (r.Code == 429) return "Kick: sending too fast"; if (!r.Ok) return "Kick: " + (r.Code > 0 ? r.Code + " " + U.Trunc(r.Body, 120) : r.Error);
      return null; }
    public override List<string> OwnNames() { var l = new List<string> { Slug }; var a = Accounts.Info("kick"); if (a != null) l.Add(J.Str(a, "login", "")); return l.Where(x => x.Length > 0).ToList(); }
  }

  public class KickPusher : WsLink {
    readonly KickSource src; readonly ManualResetEventSlim established = new ManualResetEventSlim(false);
    public KickPusher(KickSource s) { src = s; Name = "Kick chat"; Area = "chat"; IdleTimeoutSec = 150; }
    protected override bool Wanted() { return src.Configured; }
    protected override string WhyNotWanted() { return src.Enabled ? "add your Kick channel name" : "turned off"; }
    protected override void StateChanged() { Hub.Publish("chat", J.D("type", "chat.status", "platforms", Platforms.StatusAll())); Hub.Publish("status", Status.Msg()); }
    protected override string Url() {
      if (src.ChatroomId.Length == 0 && !src.Resolve()) { var e = src.ResolveError; if (e.StartsWith("Kick has no channel")) SetState("unavailable", e); throw new Exception(e); }
      var key = J.Str(Cfg.D, "platforms.kick.pusherKey", "32cbd69e4b950bf97679"); var cluster = J.Str(Cfg.D, "platforms.kick.pusherCluster", "us2");
      return Ep.Get("kick_pusher", "wss://ws-" + cluster + ".pusher.com/app/" + key) + "?protocol=7&client=js&version=8.4.0&flash=false"; }
    protected override void BeforeConnect() { established.Reset(); }
    // Pusher expects the client to ping when the room is quiet. Without it a quiet chat looked dead after 150 s and IXC dropped
    // and re-opened the connection again and again (each time risking messages in the gap). The pong also resets the idle timer.
    protected override void OnConnected() {
      var mine = ws;
      Task.Run(async () => {
        while (mine != null && ws == mine && mine.State == WebSocketState.Open) {
          await Task.Delay(45000);
          if (ws != mine) break;
          try { await Tx("{\"event\":\"pusher:ping\",\"data\":{}}"); } catch (Exception) { break; } } }); }
    protected override async Task Handshake() {
      if (!await Task.Run(() => established.Wait(10000))) throw new Exception("Kick chat did not answer");
      foreach (var ch in new[] { "chatrooms." + src.ChatroomId + ".v2", "chatroom_" + src.ChatroomId, "channel." + src.ChannelId, "channel_" + src.ChannelId })
        if (!ch.EndsWith(".") && !ch.EndsWith("_")) await Tx(J.Ser(J.D("event", "pusher:subscribe", "data", J.D("auth", "", "channel", ch)))); }
    protected override void OnText(string raw) {
      var m = J.Parse(raw); var ev = J.Str(m, "event", ""); var dataRaw = J.Get(m, "data");
      var data = dataRaw is string ? J.Parse((string)dataRaw) : dataRaw as Dictionary<string, object> ?? new Dictionary<string, object>();
      switch (ev) {
        case "pusher:connection_established": established.Set(); return;
        case "pusher:ping": var t = Tx("{\"event\":\"pusher:pong\",\"data\":{}}"); return;
        case "pusher:error": Log.Warn("chat", "Kick chat error: " + raw); return;
        case "App\\Events\\ChatMessageEvent": Chat.Ingest(Msg(data)); return;
        case "App\\Events\\MessageDeletedEvent": Chat.Delete("kick", J.Str(data, "message.id", null), null); return;
        case "App\\Events\\UserBannedEvent": Chat.Delete("kick", null, J.Str(data, "user.username", J.Str(data, "user.slug", null))); return;
        case "App\\Events\\ChatroomClearEvent": Chat.ClearAll("kick"); return;
        case "App\\Events\\SubscriptionEvent": Chat.Ingest(new ChatMsg { Platform = "kick", Id = "sub-" + J.Str(data, "username", "") + "-" + J.Str(data, "months", ""), Kind = "sub", Name = J.Str(data, "username", "someone"), Text = "subscribed" + (J.Int(data, "months", 0) > 1 ? " for " + J.Int(data, "months", 0) + " months" : "") }); return;
        case "App\\Events\\GiftedSubscriptionsEvent": { var who = J.List(data, "gifted_usernames"); Chat.Ingest(new ChatMsg { Platform = "kick", Id = "gift-" + J.Str(data, "gifter_username", "") + "-" + U.Token(4), Kind = "gift", Name = J.Str(data, "gifter_username", "someone"), Text = "gifted " + who.Count + " sub" + (who.Count == 1 ? "" : "s") }); return; }
        case "App\\Events\\StreamHostEvent": Chat.Ingest(new ChatMsg { Platform = "kick", Id = "host-" + J.Str(data, "host_username", "") + "-" + U.Token(4), Kind = "raid", Name = J.Str(data, "host_username", "someone"), Text = "is hosting", Amount = J.Int(data, "number_viewers", 0) + " viewers" }); return;
        case "App\\Events\\FollowersUpdated": if (J.Bool(data, "followed", false) && J.Str(data, "username", "").Length > 0) Chat.Ingest(new ChatMsg { Platform = "kick", Id = "follow-" + J.Str(data, "username", "") + "-" + J.Str(data, "created_at", U.Now().ToString()), Kind = "follow", Name = J.Str(data, "username", ""), Text = "followed" }); return;
        case "App\\Events\\StopStreamBroadcast": src.Viewers.Set(null, false, "Kick", null); return;
      } }
    static ChatMsg Msg(Dictionary<string, object> d) {
      var s = J.Obj(d, "sender") ?? new Dictionary<string, object>();
      var m = new ChatMsg { Platform = "kick", Id = J.Str(d, "id", null), UserId = J.Str(s, "id", ""), Name = J.Str(s, "username", "someone"), Login = J.Str(s, "slug", J.Str(s, "username", "")), Color = J.Str(s, "identity.color", null), Text = J.Str(d, "content", "") };
      foreach (var b in J.Objs(s, "identity.badges")) { var t = J.Str(b, "type", ""); if (t == "broadcaster") m.Broadcaster = true; else if (t == "moderator") m.Mod = true; else if (t == "vip") m.Vip = true; else if (t == "subscriber" || t == "founder") m.Sub = true; }
      // [emote:37226:KEKW]
      int pos = 0; foreach (Match e in Regex.Matches(m.Text, "\\[emote:(\\d+):([^\\]]*)\\]")) {
        if (e.Index > pos) m.Parts.Add(J.D("t", "text", "v", m.Text.Substring(pos, e.Index - pos)));
        m.Parts.Add(J.D("t", "emote", "name", e.Groups[2].Value, "url", "https://files.kick.com/emotes/" + e.Groups[1].Value + "/fullsize")); m.EmoteNames.Add(e.Groups[2].Value); pos = e.Index + e.Length; }
      if (m.Parts.Count > 0 && pos < m.Text.Length) m.Parts.Add(J.D("t", "text", "v", m.Text.Substring(pos)));
      return m; }
  }

  // ======================= YouTube (the live chat the YouTube website itself uses; Data API with a sign-in) =======================
  public class YouTubeSource : ChatSource {
    volatile string state = "off", detail = ""; DateTime since = DateTime.Now; Thread th; readonly AutoResetEvent wake = new AutoResetEvent(false); volatile bool restart;
    public string VideoId = "", LiveChatId = ""; string apiKey = "", clientVersion = "2.20250925.01.00", continuation = ""; DateTime? started;
    // reconnect bookkeeping: a short "ended" blip must not flip the chip to Not live, and messages sent while IXC was reconnecting must still be read out
    DateTime openedAt = DateTime.MinValue, lastPollOk = DateTime.MinValue; string lastChatVideo = "";
    public YouTubeSource() { Id = "youtube"; Label = "YouTube"; }
    public override bool Configured { get { return Enabled && Channel.Length > 0; } }
    public override string State { get { return state; } }
    public override string Detail { get { return detail; } }
    public override DateTime StateSince { get { return since; } }
    void Set(string s, string d) { if (state != s || detail != d) { state = s; detail = d ?? ""; since = DateTime.Now; Publish(); } }
    public override void Start() { th = new Thread(Loop) { IsBackground = true, Name = "YouTube chat" }; th.Start(); StartViewerPolling(); NetWatch.Woke += () => wake.Set(); }
    public override void Restart() { restart = true; VideoId = ""; LiveChatId = ""; continuation = ""; endSignals = 0; wake.Set(); PollNow(); }
    public override void Kick() { wake.Set(); }
    // the live video Streamer.bot names in its YouTube events: used when the channel page doesn't show the stream
    // (YouTube's "confirm you're not a bot" page on some networks, an unlisted stream, another page layout)
    volatile string sbVideo = ""; int endSignals; DateTime firstEnd;
    // the stream whose chat just ended: for a while YouTube's page can still name it and its chat page can still look live, so
    // the "running live chat" and Streamer.bot fallbacks don't count for it (the page's own "live" marker still does)
    string endedVideo = ""; DateTime endedAt = DateTime.MinValue;
    bool JustEnded(string id) { return id.Length > 0 && id == endedVideo && (DateTime.Now - endedAt).TotalMinutes < 10; }
    static int EndConfirmMs { get { return int.Parse(Ep.Get("youtube_end_confirm_ms", "45000")); } }
    public void StreamerBotVideo(string id) {
      if (id == null || !Regex.IsMatch(id, "^[\\w-]{11}$") || id == sbVideo) return;
      sbVideo = id; Log.Info("chat", "YouTube: Streamer.bot names your live stream " + id);
      if (state != "connected") wake.Set(); }
    public void StreamerBotEnded() { sbVideo = ""; }
    public override bool CanSend { get { return Accounts.Has("youtube") && !Accounts.IsInvalid("youtube") && VideoId.Length > 0; } }
    public override string SendNote { get { return !Accounts.Has("youtube") ? "Sign in to YouTube (Accounts) to reply" : VideoId.Length == 0 ? "YouTube: you're not live right now" : "YouTube sign-in expired - sign in again"; } }
    string Base { get { return Ep.Get("youtube_web", "https://www.youtube.com"); } }
    static Dictionary<string, string> Hdr() { return new Dictionary<string, string> { { "Cookie", "SOCS=CAI; CONSENT=YES+1" }, { "Accept-Language", "en-US,en;q=0.9" } }; }
    void Loop() {
      int fails = 0;
      while (true) {
        if (!Configured) { Set("off", Enabled ? "add your YouTube channel" : "turned off"); wake.WaitOne(5000); continue; }
        restart = false; int wait;
        try {
          if (continuation.Length == 0) {
            // the stream is found once; after that IXC stays on it (like a Twitch or Kick connection) and only re-opens its chat
            if (VideoId.Length == 0) {
              if (state != "connected") Set(fails == 0 ? "connecting" : "reconnecting", "looking for your live stream");
              if (!FindLive()) { Set("unavailable", notLiveWhy + " - IXC checks again every 30 s"); wait = 30000; fails = 0; wake.WaitOne(wait); continue; } }
            OpenChat(); openedAt = DateTime.Now; if (state != "connected") Log.Info("chat", "YouTube chat connected (" + VideoId + ")"); Set("connected", ""); }
          int timeout = Poll(); fails = 0; endSignals = 0; lastPollOk = DateTime.Now; lastChatVideo = VideoId; wait = Math.Max(1000, Math.Min(10000, timeout)); }
        catch (Exception e) {
          fails++; continuation = ""; var msg = U.Plain(e);
          if (msg.StartsWith("ended")) {
            // YouTube's chat sometimes answers "no chat" for a moment while the stream goes on: the stream counts as ended only when
            // that keeps happening for a while; until then the chat is re-opened and the state stays as it is
            if (endSignals++ == 0) firstEnd = DateTime.Now;
            if (endSignals >= 3 && (DateTime.Now - firstEnd).TotalMilliseconds >= EndConfirmMs) {
              if (VideoId == sbVideo) sbVideo = ""; endedVideo = VideoId; endedAt = DateTime.Now; VideoId = ""; LiveChatId = ""; endSignals = 0; Set("unavailable", "the live stream ended"); Log.Info("chat", "YouTube live chat ended"); wait = 15000; }
            else { Log.Debug("chat", "YouTube chat: " + msg + " - checking again"); wait = Math.Max(2000, EndConfirmMs / 3); } }
          else {
            // one failed request isn't shown; a connection that keeps failing is
            if (fails >= 2) Set("reconnecting", msg); wait = U.Backoff(fails - 1, 2000, 60000);
            if (fails <= 2 || fails % 10 == 0) Log.Info("chat", "YouTube chat: " + msg + " - retrying in " + wait / 1000 + " s"); } }
        if (restart) continue; wake.WaitOne(wait); } }
    // "@handle", channel URL, /live URL, a video URL or an 11-character video id
    // the video a channel's /live page shows. YouTube doesn't always include the same tags (the canonical link was missing on
    // some pages in Oct 2026), so several places are checked, from the most to the least specific
    static readonly string[] VideoIdPatterns = {
      "<link rel=\"canonical\" href=\"https://www\\.youtube\\.com/watch\\?v=([\\w-]{11})\"",
      "<meta property=\"og:url\" content=\"https://www\\.youtube\\.com/watch\\?v=([\\w-]{11})\"",
      "\"videoDetails\":\\{\"videoId\":\"([\\w-]{11})\"",
      "<link rel=\"shortlinkUrl\" href=\"https://youtu\\.be/([\\w-]{11})\"" };
    // YouTube sometimes answers with "Sign in to confirm you're not a bot" instead of the page (some networks, VPNs, data centres)
    static bool BotCheck(string page) { return Regex.IsMatch(page ?? "", "\"playabilityStatus\":\\{\"status\":\"LOGIN_REQUIRED\"") && Regex.IsMatch(page, "not a bot", RegexOptions.IgnoreCase); }
    const string BotCheckNote = "YouTube is asking this network to confirm it's not a bot (happens on some networks and VPNs), so IXC can't see if you're live";
    public static string LiveVideoId(string page) {
      foreach (var p in VideoIdPatterns) { var m = Regex.Match(page ?? "", p); if (m.Success) return m.Groups[1].Value; }
      return null; }
    string notLiveWhy = "not live right now";
    bool FindLive() {
      bool found = false; Exception err = null;
      try { found = FindLiveOnPage(); } catch (Exception e) { err = e; }
      if (!found && sbVideo.Length > 0 && !JustEnded(sbVideo)) { VideoId = sbVideo; started = null; Log.Info("chat", "YouTube: the channel page shows no live stream, using the one Streamer.bot names (" + VideoId + ")"); return true; }
      if (err != null) throw err; return found; }
    bool FindLiveOnPage() {
      notLiveWhy = "not live right now";
      var c = Channel.Trim(); var vm = Regex.Match(c, "(?:v=|youtu\\.be/|/live/|/shorts/|/embed/)([\\w-]{11})"); string page = null;
      if (vm.Success || Regex.IsMatch(c, "^[\\w-]{11}$")) { VideoId = vm.Success ? vm.Groups[1].Value : c; page = Http.Request("GET", Base + "/watch?v=" + VideoId, null, null, Hdr(), 15000).Body ?? ""; }
      else {
        string path; var m = Regex.Match(c, "youtube\\.com/(@[\\w.\\-]+|channel/UC[\\w-]{22}|c/[\\w.\\-]+|user/[\\w.\\-]+)", RegexOptions.IgnoreCase);
        if (m.Success) path = m.Groups[1].Value; else if (c.StartsWith("UC") && c.Length == 24) path = "channel/" + c; else path = "@" + c.TrimStart('@');
        var r = Http.Request("GET", Base + "/" + path + "/live", null, null, Hdr(), 15000);
        if (r.Code == 404) throw new Exception("YouTube has no channel \"" + c + "\"");
        if (!r.Ok) throw new Exception("YouTube unreachable (" + (r.Error ?? r.Code.ToString()) + ")");
        page = r.Body ?? ""; var id = LiveVideoId(page);
        if (id == null) { VideoId = ""; notLiveWhy = BotCheck(page) ? BotCheckNote : "your channel has no live stream right now"; return false; } VideoId = id; }
      bool live = Regex.IsMatch(page, "\"isLive(Now)?\":true") || Regex.IsMatch(page, "\"isLiveContent\":true[^}]*\"isLive\":true");
      var st = Regex.Match(page, "\"startTimestamp\":\"([^\"]+)\""); DateTime t; started = st.Success && DateTime.TryParse(st.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out t) ? (DateTime?)t.ToLocalTime() : null;
      if (!live) {
        if (Regex.IsMatch(page, "\"isUpcoming\":true")) { notLiveWhy = "the stream is scheduled but not started - press Go live in YouTube Studio"; VideoId = ""; return false; }
        // the page names a video but leaves out YouTube's "live" markers (seen on home connections in Oct 2026, and with YouTube's
        // bot check): the video's own live chat decides - a running live chat means the stream is live
        if (!JustEnded(VideoId) && LiveChatRunning(VideoId)) { Log.Info("chat", "YouTube: the page doesn't say \"live\", but the live chat of " + VideoId + " is running - connecting"); return true; }
        notLiveWhy = BotCheck(page) ? BotCheckNote : "YouTube says the stream is not live"; VideoId = ""; return false; }
      return true; }
    // a live (not replay) chat with a continuation: the stream is on air
    bool LiveChatRunning(string id) {
      try {
        var r = Http.Request("GET", Base + "/live_chat?is_popout=1&v=" + id, null, null, Hdr(), 15000); var h = r.Body ?? "";
        return r.Ok && Regex.IsMatch(h, "\"(invalidationContinuationData|timedContinuationData|reloadContinuationData)\"") && !h.Contains("\"isReplay\":true") && !h.Contains("liveChatReplayContinuationData"); }
      catch { return false; } }
    void OpenChat() {
      var r = Http.Request("GET", Base + "/live_chat?is_popout=1&v=" + VideoId, null, null, Hdr(), 15000);
      if (!r.Ok) throw new Exception("couldn't open the live chat (" + (r.Error ?? r.Code.ToString()) + ")");
      var h = r.Body ?? ""; var k = Regex.Match(h, "\"INNERTUBE_API_KEY\":\"([^\"]+)\""); var v = Regex.Match(h, "\"INNERTUBE_CLIENT_VERSION\":\"([^\"]+)\"");
      if (k.Success) apiKey = k.Groups[1].Value; if (v.Success) clientVersion = v.Groups[1].Value;
      var c = Regex.Match(h, "\"continuation\":\"([^\"]+)\""); if (!c.Success) { if (h.Contains("Chat is disabled") || h.Contains("chat is disabled")) throw new Exception("chat is turned off for this live stream"); throw new Exception("ended: no live chat found"); }
      continuation = c.Groups[1].Value;
      // messages already in chat when IXC connects are shown, but not read out or treated as commands
      var di = h.IndexOf("ytInitialData"); if (di > 0) { var js = Regex.Match(h.Substring(di), "^ytInitialData\"?\\]?\\s*=\\s*(\\{.*?\\});\\s*</script>", RegexOptions.Singleline); if (js.Success) { var d = J.Parse(js.Groups[1].Value); var acts = J.Get(d, "contents.liveChatRenderer.actions") as ArrayList; if (acts != null) Actions(acts, true); } } }
    int Poll() {
      var body = J.Ser(J.D("context", J.D("client", J.D("clientName", "WEB", "clientVersion", clientVersion, "hl", "en")), "continuation", continuation));
      var r = Http.Request("POST", Base + "/youtubei/v1/live_chat/get_live_chat?prettyPrint=false" + (apiKey.Length > 0 ? "&key=" + apiKey : ""), body, "application/json", Hdr(), 15000);
      if (!r.Ok) { if (r.Code == 404 || r.Code == 403) throw new Exception("ended: YouTube closed the live chat"); throw new Exception("YouTube chat: " + (r.Error ?? ("HTTP " + r.Code))); }
      var d = J.Parse(r.Body); var lc = J.Obj(d, "continuationContents.liveChatContinuation");
      if (lc == null) throw new Exception("ended: no more live chat");
      var acts = J.Get(lc, "actions") as ArrayList; if (acts != null) Actions(acts, false);
      var conts = J.Get(lc, "continuations") as ArrayList; if (conts == null || conts.Count == 0) throw new Exception("ended: live chat finished");
      var c0 = conts[0] as Dictionary<string, object>; var cd = J.Obj(c0, "invalidationContinuationData") ?? J.Obj(c0, "timedContinuationData") ?? J.Obj(c0, "reloadContinuationData");
      if (cd == null) throw new Exception("ended: live chat finished");
      continuation = J.Str(cd, "continuation", ""); return J.Int(cd, "timeoutMs", 3000); }
    static string Runs(Dictionary<string, object> msg, ChatMsg m) {
      var sb = new StringBuilder(); var runs = J.Get(msg, "runs") as ArrayList; if (runs == null) return J.Str(msg, "simpleText", "");
      foreach (var o in runs) { var r = o as Dictionary<string, object>; if (r == null) continue;
        if (r.ContainsKey("text")) { var t = J.Str(r, "text", ""); sb.Append(t); if (m != null) m.Parts.Add(J.D("t", "text", "v", t)); }
        else if (r.ContainsKey("emoji")) { var e = J.Obj(r, "emoji"); var sc = J.List(e, "shortcuts"); var name = sc.Count > 0 ? sc[0] : J.Str(e, "emojiId", "");
          var custom = J.Bool(e, "isCustomEmoji", false); var thumbs = J.Get(e, "image.thumbnails") as ArrayList; var url = thumbs != null && thumbs.Count > 0 ? J.Str((Dictionary<string, object>)thumbs[thumbs.Count - 1], "url", "") : "";
          if (custom) { sb.Append(name); if (m != null) { m.EmoteNames.Add(name); m.Parts.Add(J.D("t", "emote", "name", name, "url", url)); } }
          else { var id = J.Str(e, "emojiId", ""); sb.Append(id); if (m != null) m.Parts.Add(J.D("t", "text", "v", id)); } } }
      return sb.ToString(); }
    void Actions(ArrayList acts, bool initial) {
      // same stream, short gap (<= 5 min): messages that arrived while IXC was reconnecting are new, not history. Repeats are dropped by the message id.
      DateTime resumeFrom = DateTime.MaxValue;
      if (initial && VideoId == lastChatVideo && lastPollOk != DateTime.MinValue && (DateTime.Now - lastPollOk).TotalMinutes <= 5) resumeFrom = lastPollOk.AddSeconds(-5);
      foreach (var a0 in acts) { var a = a0 as Dictionary<string, object>; if (a == null) continue;
        var item = J.Obj(a, "addChatItemAction.item");
        if (item != null) { var m = Item(item); if (m != null) { m.Old = initial && !(m.At >= resumeFrom && (DateTime.Now - m.At).TotalSeconds < 300); Chat.Ingest(m); } continue; }
        var del = J.Str(a, "markChatItemAsDeletedAction.targetItemId", J.Str(a, "removeChatItemAction.targetItemId", null)); if (del != null) { Chat.Delete("youtube", del, null); continue; }
        var byAuthor = J.Str(a, "markChatItemsByAuthorAsDeletedAction.externalChannelId", J.Str(a, "removeChatItemByAuthorAction.externalChannelId", null)); if (byAuthor != null) Chat.DeleteByUserId("youtube", byAuthor); } }
    ChatMsg Item(Dictionary<string, object> item) {
      string kind = null; Dictionary<string, object> r = null;
      foreach (var kv in new[] { "liveChatTextMessageRenderer", "liveChatPaidMessageRenderer", "liveChatMembershipItemRenderer", "liveChatPaidStickerRenderer", "liveChatSponsorshipsGiftPurchaseAnnouncementRenderer" }) { r = J.Obj(item, kv); if (r != null) { kind = kv; break; } }
      if (r == null) return null;
      var m = new ChatMsg { Platform = "youtube", Id = J.Str(r, "id", null), UserId = J.Str(r, "authorExternalChannelId", ""), Name = J.Str(r, "authorName.simpleText", "someone") };
      m.Login = m.Name.TrimStart('@');
      var ph = J.Get(r, "authorPhoto.thumbnails") as ArrayList; if (ph != null && ph.Count > 0) m.Avatar = J.Str((Dictionary<string, object>)ph[ph.Count - 1], "url", null);
      foreach (var b in J.Objs(r, "authorBadges")) { var br = J.Obj(b, "liveChatAuthorBadgeRenderer"); var icon = J.Str(br, "icon.iconType", ""); if (icon == "OWNER") m.Broadcaster = true; else if (icon == "MODERATOR") m.Mod = true; else if (J.Obj(br, "customThumbnail") != null) m.Sub = true; }
      long usec = J.Long(r, "timestampUsec", 0); if (usec > 0) m.At = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(usec / 1000).ToLocalTime();
      if (kind == "liveChatTextMessageRenderer") { m.Text = Runs(J.Obj(r, "message") ?? new Dictionary<string, object>(), m); }
      else if (kind == "liveChatPaidMessageRenderer" || kind == "liveChatPaidStickerRenderer") { m.Kind = "superchat"; m.Amount = J.Str(r, "purchaseAmountText.simpleText", ""); m.Text = Runs(J.Obj(r, "message") ?? new Dictionary<string, object>(), null); }
      else if (kind == "liveChatMembershipItemRenderer") { m.Kind = "member"; m.Text = Runs(J.Obj(r, "headerSubtext") ?? new Dictionary<string, object>(), null); }
      else { m.Kind = "gift"; m.Name = J.Str(r, "authorName.simpleText", J.Str(r, "header.liveChatSponsorshipsHeaderRenderer.authorName.simpleText", "someone")); m.Text = Runs(J.Obj(r, "header.liveChatSponsorshipsHeaderRenderer.primaryText") ?? new Dictionary<string, object>(), null); }
      return m; }
    public override void PollViewers() {
      if (VideoId.Length == 0) {
        // IXC can't see the stream itself; a recent number from Streamer.bot is kept rather than replaced by "offline"
        if (Viewers.Source == "Streamer.bot" && (DateTime.Now - Viewers.At).TotalSeconds < 120) return;
        if (state == "unavailable" || state == "off") Viewers.Set(null, false, "YouTube", null); return; }
      var body = J.Ser(J.D("context", J.D("client", J.D("clientName", "WEB", "clientVersion", clientVersion, "hl", "en")), "videoId", VideoId));
      var r = Http.Request("POST", Base + "/youtubei/v1/updated_metadata?prettyPrint=false" + (apiKey.Length > 0 ? "&key=" + apiKey : ""), body, "application/json", Hdr(), 12000);
      if (!r.Ok) { Viewers.Fail("YouTube: " + (r.Error ?? ("HTTP " + r.Code))); return; }
      var m = Regex.Match(r.Body ?? "", "\"originalViewCount\":\"(\\d+)\""); if (!m.Success) m = Regex.Match(r.Body ?? "", "\"text\":\"([\\d,\\.]+) watching");
      if (m.Success) Viewers.Set(int.Parse(m.Groups[1].Value.Replace(",", "").Replace(".", ""), CultureInfo.InvariantCulture), true, "YouTube", started);
      else Viewers.Fail("YouTube didn't report a viewer count"); }
    public override async Task<string> Send(string text) {
      if (!CanSend) return SendNote;
      if (LiveChatId.Length == 0) { var r = await Task.Run(() => Accounts.GoogleApi("GET", "videos?part=liveStreamingDetails&id=" + VideoId, null)); if (r == null || !r.Ok) return "YouTube: " + (r == null ? SendNote : r.Code + " " + U.Trunc(r.Body, 160));
        var items = J.Get(J.Parse(r.Body), "items") as ArrayList; LiveChatId = items != null && items.Count > 0 ? J.Str((Dictionary<string, object>)items[0], "liveStreamingDetails.activeLiveChatId", "") : ""; if (LiveChatId.Length == 0) return "YouTube: this stream has no active chat"; }
      var res = await Task.Run(() => Accounts.GoogleApi("POST", "liveChat/messages?part=snippet", J.Ser(J.D("snippet", J.D("liveChatId", LiveChatId, "type", "textMessageEvent", "textMessageDetails", J.D("messageText", text))))));
      if (res == null) return SendNote; if (res.Code == 403 && (res.Body ?? "").Contains("quota")) return "YouTube's daily sending limit is used up"; if (!res.Ok) return "YouTube: " + res.Code + " " + U.Trunc(res.Body, 160);
      return null; }
    public override List<string> OwnNames() { var a = Accounts.Info("youtube"); return a == null ? new List<string>() : new List<string> { J.Str(a, "displayName", "") }; }
  }

  // ======================= Rumble (your personal Live Stream API link) =======================
  public class RumbleSource : ChatSource {
    volatile string state = "off", detail = ""; Thread th; readonly AutoResetEvent wake = new AutoResetEvent(false); bool first = true;
    public RumbleSource() { Id = "rumble"; Label = "Rumble"; }
    public override bool Configured { get { return Enabled && Accounts.RumbleUrl().Length > 0; } }
    public override string State { get { return state; } }
    public override string Detail { get { return detail; } }
    public override string SendNote { get { return "Rumble doesn't let apps send chat messages"; } }
    void Set(string s, string d) { if (state != s || detail != d) { state = s; detail = d ?? ""; Publish(); } }
    public override void Start() { th = new Thread(Loop) { IsBackground = true, Name = "Rumble chat" }; th.Start(); NetWatch.Woke += () => wake.Set(); }
    public override void Restart() { first = true; wake.Set(); }
    public override void Kick() { wake.Set(); }
    void Loop() {
      int fails = 0;
      while (true) {
        if (!Configured) { Set("off", Enabled ? "paste your Rumble Live Stream API link" : "turned off"); wake.WaitOne(5000); continue; }
        int wait = 5000;
        try {
          var r = Http.Request("GET", Accounts.RumbleUrl(), null, null, new Dictionary<string, string> { { "Accept", "application/json" } }, 12000);
          if (r.Code == 401 || r.Code == 403) { Set("auth", "Rumble didn't accept the API link - copy it again from rumble.com"); wait = 60000; }
          else if (r.Code == 429) { Set("ratelimited", "Rumble asked IXC to slow down"); wait = 30000; }
          else if (!r.Ok) throw new Exception("Rumble: " + (r.Error ?? ("HTTP " + r.Code)));
          else { Parse(J.Parse(r.Body)); fails = 0; } }
        catch (Exception e) { fails++; Set("reconnecting", U.Plain(e)); wait = U.Backoff(fails - 1, 5000, 60000); }
        wake.WaitOne(wait); } }
    void Parse(Dictionary<string, object> d) {
      var lss = J.Objs(d, "livestreams"); var ls = lss.FirstOrDefault(x => J.Bool(x, "is_live", false)) ?? lss.FirstOrDefault();
      if (ls == null || !J.Bool(ls, "is_live", false)) { Set("unavailable", "not live right now"); Viewers.Set(null, false, "Rumble", null); first = true; return; }
      Set("connected", ""); DateTime st;
      Viewers.Set(J.Int(ls, "watching_now", 0), true, "Rumble", DateTime.TryParse(J.Str(ls, "created_on", ""), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out st) ? (DateTime?)st.ToLocalTime() : null);
      foreach (var x in J.Objs(ls, "chat.recent_messages")) Chat.Ingest(new ChatMsg { Platform = "rumble", Id = "r" + U.ShaHex(J.Str(x, "username", "") + J.Str(x, "created_on", "") + J.Str(x, "text", "")).Substring(0, 16), Name = J.Str(x, "username", "someone"), Text = J.Str(x, "text", ""), Old = first });
      foreach (var x in J.Objs(ls, "chat.recent_rants")) Chat.Ingest(new ChatMsg { Platform = "rumble", Id = "rant" + U.ShaHex(J.Str(x, "username", "") + J.Str(x, "created_on", "") + J.Str(x, "text", "")).Substring(0, 16), Kind = "superchat", Name = J.Str(x, "username", "someone"), Text = J.Str(x, "text", ""), Amount = "$" + J.Num(x, "amount_dollars", 0).ToString("0.00", CultureInfo.InvariantCulture), Old = first });
      var lf = J.Obj(d, "followers.latest_follower"); if (lf != null && !first) Chat.Ingest(new ChatMsg { Platform = "rumble", Id = "f" + J.Str(lf, "username", "") + J.Str(lf, "followed_on", ""), Kind = "follow", Name = J.Str(lf, "username", ""), Text = "followed" });
      first = false; }
  }

  // ======================= Streamer.bot (optional: still works for people who already use it) =======================
  public class SbLink : WsLink {
    public string AuthState = "none"; public List<string> Broadcasters = new List<string>(); Dictionary<string, object> hello;
    readonly ManualResetEventSlim gotHello = new ManualResetEventSlim(false); DateTime lastRunCheck = DateTime.MinValue; bool runCache;
    public SbLink() { Name = "Streamer.bot"; Area = "streamerbot"; MaxBackoffMs = 30000; }
    string Mode { get { return Settings.Str("platforms.streamerbot.mode"); } }
    bool ProcessRunning() { if ((DateTime.Now - lastRunCheck).TotalSeconds < 5) return runCache; lastRunCheck = DateTime.Now; try { runCache = System.Diagnostics.Process.GetProcessesByName("Streamer.bot").Length > 0; } catch { runCache = false; } return runCache; }
    protected override bool Wanted() { var m = Mode; return m == "on" || (m == "auto" && (ProcessRunning() || Program.TestMode && Ep.Get("streamerbot", "").Length > 0)); }
    protected override string WhyNotWanted() { return Mode == "off" ? "turned off" : "Streamer.bot is not running (optional)"; }
    protected override string Url() { return Ep.Get("streamerbot", Settings.Str("platforms.streamerbot.websocketUrl")); }
    protected override string Friendly(Exception e) { var m = U.Plain(e); return e is WebSocketException || m.Contains("Unable to connect") || m.Contains("refused") ? "Streamer.bot is running but its WebSocket server is off (Servers/Clients > WebSocket Server)" : m; }
    protected override void StateChanged() { Hub.Publish("chat", J.D("type", "chat.status", "platforms", Platforms.StatusAll())); Hub.Publish("status", Status.Msg()); }
    protected override async Task Handshake() {
      if (!await Task.Run(() => gotHello.Wait(5000))) throw new Exception("Streamer.bot did not say hello");
      gotHello.Reset(); var a = J.Obj(hello, "authentication");
      if (a != null) {
        string pw = Password(); var r = await Ask("auth", J.Ser(J.D("request", "Authenticate", "id", "auth", "authentication", U.Sha(U.Sha(pw + J.Str(a, "salt", "")) + J.Str(a, "challenge", "")))), 5000);
        AuthState = r == null ? "no answer" : J.Str(r, "status", "?") == "ok" ? "ok" : ("refused: " + J.Str(r, "error", "?"));
        if (AuthState != "ok") { SetState("auth", "Streamer.bot refused the password" + (pw.Length == 0 ? " (none found)" : "")); throw new Exception("Streamer.bot password " + AuthState); } }
      else AuthState = "not required";
      var sub = await Ask("sub", J.Ser(J.D("request", "Subscribe", "id", "sub", "events", J.D(
        "Twitch", new[] { "ChatMessage", "ViewerCountUpdate", "StreamOnline", "StreamOffline", "ChatMessageDeleted", "UserBanned", "UserTimedOut", "Follow", "Sub", "ReSub", "GiftSub", "Raid" },
        "YouTube", new[] { "Message", "StatisticsUpdated", "BroadcastStarted", "BroadcastEnded", "MessageDeleted", "UserBanned", "SuperChat", "NewSponsor" },
        "Kick", new[] { "ChatMessage", "ViewerCountUpdate", "StreamOnline", "StreamOffline", "ChatMessageDeleted", "UserBanned", "UserTimedOut", "Follow", "Subscription", "GiftSubscription" }))), 5000);
      if (sub == null || J.Str(sub, "status", "ok") != "ok") throw new Exception("Streamer.bot did not accept the event subscription"); }
    protected override void OnConnected() { Task.Run(() => LoadBroadcasters()); cmdAt = DateTime.MinValue; Task.Run(() => Chat.PushSuggestions()); }
    async Task LoadBroadcasters() {
      var r = await Request(J.D("request", "GetBroadcaster"), 5000); if (r == null) return;
      var names = new List<string>(); Collect(r, names, 0);
      lock (Broadcasters) { Broadcasters.Clear(); Broadcasters.AddRange(names.Distinct(StringComparer.OrdinalIgnoreCase)); }
      if (names.Count > 0) Log.Info("streamerbot", "your accounts (from Streamer.bot): " + string.Join(", ", Broadcasters)); }
    static void Collect(object o, List<string> names, int depth) {
      var d = o as Dictionary<string, object>; if (d == null || depth > 5) { var a = o as ArrayList; if (a != null) foreach (var x in a) Collect(x, names, depth + 1); return; }
      foreach (var kv in d) { var s = kv.Value as string; var k = kv.Key.ToLowerInvariant();
        if (s != null && s.Length > 1 && (k == "broadcastuser" || k == "broadcastusername" || k == "broadcasteruser" || k == "broadcasterusername" || k == "username" || k == "displayname" || k == "channelname" || k == "login")) names.Add(s);
        else Collect(kv.Value, names, depth + 1); } }
    public string Password() {
      var p = Secrets.Str("streamerbot.password") ?? ""; if (p.Length > 0) return p;
      foreach (var f in SettingsFiles()) { try { if (!File.Exists(f)) continue; var s = J.Parse(File.ReadAllText(f)); var pw = J.Str(s, "websockets.authPassword", J.Str(s, "websocketServer.password", "")); if (pw.Length > 0) return pw; } catch { } }
      return ""; }
    static IEnumerable<string> SettingsFiles() {
      var sp = Settings.Str("platforms.streamerbot.settingsPath");
      if (sp.Length > 0 && sp != "auto") { yield return sp; yield break; }
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var pr in System.Diagnostics.Process.GetProcessesByName("Streamer.bot")) { string f = null; try { f = Path.Combine(Path.GetDirectoryName(pr.MainModule.FileName), "data", "settings.json"); } catch { } if (f != null && seen.Add(f)) yield return f; }
      var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      foreach (var root in new[] { Path.Combine(home, "Desktop"), Path.Combine(home, "Documents"), Path.Combine(home, "Downloads"), home }) {
        string[] dirs; try { dirs = Directory.GetDirectories(root, "*treamer*bot*"); } catch { continue; }
        foreach (var d in dirs) { var f = Path.Combine(d, "data", "settings.json"); if (seen.Add(f)) yield return f; } } }
    protected override void OnText(string raw) {
      var m = J.Parse(raw);
      if (m.ContainsKey("event") && m.ContainsKey("data")) { try { SbEvents.Handle(m); } catch (Exception e) { Log.Err("streamerbot", "event: " + e.Message); } return; }
      if (J.Str(m, "request", "") == "Hello" || (!Ready && m.ContainsKey("info") && !m.ContainsKey("id"))) { hello = m; gotHello.Set(); return; }
      var id = J.Str(m, "id", null); if (id != null) Complete(id, m); }
    public Task<Dictionary<string, object>> Request(Dictionary<string, object> req, int ms) { if (!Ready) return Task.FromResult<Dictionary<string, object>>(null); var id = NewId(); req["id"] = id; return Ask(id, J.Ser(req), ms); }
    static List<string> cmdCache = new List<string>(); static DateTime cmdAt = DateTime.MinValue;
    // the chat commands set up in Streamer.bot (every enabled command's triggers); only a good answer is cached
    public async Task<List<string>> Commands() {
      if (!Ready) return new List<string>(); if ((DateTime.Now - cmdAt).TotalSeconds < 30) return cmdCache;
      var rc = await Request(J.D("request", "GetCommands"), 3000); var arr = rc == null ? null : J.Get(rc, "commands") as ArrayList;
      if (arr == null) return cmdCache;
      var l = new List<string>();
      foreach (var o in arr) { var od = o as Dictionary<string, object>; if (od == null || !J.Bool(od, "enabled", true)) continue;
        foreach (var cm in J.List(od, "commands")) { var t = (cm ?? "").Trim(); if (t.Length > 1 && t.Length <= 40 && !t.Contains(" ")) l.Add(t); } }
      cmdCache = l; cmdAt = DateTime.Now; return l; }
    public async Task<string> SendTo(string platform, string message) {
      if (!Ready) return "Streamer.bot is not connected";
      var r = await Request(J.D("request", "SendMessage", "platform", platform, "message", message, "bot", false, "internal", true), 6000);
      if (r == null) return "no answer from Streamer.bot"; return J.Str(r, "status", "") == "ok" ? null : "Streamer.bot: " + J.Str(r, "error", "failed"); }
  }

  // Streamer.bot events -> normalized messages (payloads differ per platform and Streamer.bot version, so parsing is tolerant)
  public static class SbEvents {
    static object G(Dictionary<string, object> d, string k) { if (d == null) return null; object v; return d.TryGetValue(k, out v) ? v : null; }
    static string Pick(params object[] v) { foreach (var x in v) { var s = x as string; if (!string.IsNullOrWhiteSpace(s)) return s; } return null; }
    static bool B(object v) { return v is bool && (bool)v; }
    public static void Handle(Dictionary<string, object> m) {
      string src = J.Str(m, "event.source", "").ToLowerInvariant(), type = J.Str(m, "event.type", ""); var data = J.Obj(m, "data") ?? new Dictionary<string, object>();
      if (src != "twitch" && src != "kick" && src != "youtube") return;
      var vs = Platforms.Get(src);
      if (src == "youtube") { if (type == "BroadcastEnded") Platforms.YouTube.StreamerBotEnded(); else { var vid = VideoId(data, 0); if (vid != null) Platforms.YouTube.StreamerBotVideo(vid); else if (!ytShapeLogged && type == "Message") { ytShapeLogged = true; Log.Info("streamerbot", "YouTube message without a stream id; fields: " + Shape(data, 0)); } } }
      if (type == "ViewerCountUpdate" || type == "StatisticsUpdated") { if (vs != null && !Platforms.HandlesViewers(src)) { var n = FindCount(data, 0); if (n != null) vs.Viewers.Set(n, true, "Streamer.bot", null); } return; }
      if (type == "StreamOffline" || type == "BroadcastEnded") { if (vs != null && !Platforms.HandlesViewers(src)) vs.Viewers.Set(null, false, "Streamer.bot", null); return; }
      if (type == "ChatMessageDeleted" || type == "MessageDeleted") { Chat.Delete(src, Pick(G(data, "messageId"), G(data, "msgId"), G(data, "targetMessageId"), G(data, "id")), null); return; }
      if (type == "UserBanned" || type == "UserTimedOut") { var u = G(data, "user") as Dictionary<string, object>; Chat.Delete(src, null, Pick(u == null ? null : G(u, "login"), u == null ? null : G(u, "name"), G(data, "userName"), G(data, "username"))); return; }
      var cm = Parse(src, data); if (cm == null) return; cm.Via = "streamerbot";
      if (type == "Follow") { cm.Kind = "follow"; cm.Text = "followed"; } else if (type == "Sub" || type == "ReSub" || type == "Subscription" || type == "NewSponsor") { cm.Kind = "sub"; if (string.IsNullOrEmpty(cm.Text)) cm.Text = "subscribed"; }
      else if (type == "GiftSub" || type == "GiftSubscription") { cm.Kind = "gift"; cm.Text = "gifted a sub"; } else if (type == "Raid") { cm.Kind = "raid"; cm.Text = "raided"; }
      else if (type == "SuperChat") { cm.Kind = "superchat"; cm.Amount = Pick(G(data, "amount"), G(data, "formattedAmount")) ?? ""; }
      else if (type != "ChatMessage" && type != "Message") return;
      if (cm.Kind == "chat" && string.IsNullOrWhiteSpace(cm.Text)) return;
      Chat.Ingest(cm); }
    // the live video in a Streamer.bot YouTube event: data.broadcast.id (a broadcast's id is its video id) or a videoId field
    static string VideoId(object o, int depth) {
      var d = o as Dictionary<string, object>; if (d == null || depth > 3) return null;
      foreach (var kv in d) { var k = kv.Key.ToLowerInvariant(); var sv = kv.Value as string;
        if (sv != null && (k == "videoid" || k == "broadcastid" || k == "liveid") && Regex.IsMatch(sv, "^[\\w-]{11}$")) return sv;
        var sub = kv.Value as Dictionary<string, object>;
        if (sub != null && k.Contains("broadcast")) { var id = G(sub, "id") as string; if (id != null && Regex.IsMatch(id, "^[\\w-]{11}$")) return id; } }
      foreach (var kv in d) { var r = VideoId(kv.Value, depth + 1); if (r != null) return r; } return null; }
    static bool ytShapeLogged;
    // field names only (no values), to see what a Streamer.bot version sends
    static string Shape(object o, int depth) { var d = o as Dictionary<string, object>; if (d == null || depth > 2) return ""; return string.Join(",", d.Select(kv => kv.Key + (kv.Value is Dictionary<string, object> ? "{" + Shape(kv.Value, depth + 1) + "}" : ""))); }
    static int? FindCount(object o, int depth) {
      var d = o as Dictionary<string, object>; if (d == null || depth > 4) return null;
      foreach (var kv in d) if ((kv.Value is int || kv.Value is long || kv.Value is decimal || kv.Value is double) && Regex.IsMatch(kv.Key, "viewer|concurrent|watching", RegexOptions.IgnoreCase)) return Convert.ToInt32(kv.Value);
      foreach (var kv in d) { var r = FindCount(kv.Value, depth + 1); if (r != null) return r; } return null; }
    public static ChatMsg Parse(string src, Dictionary<string, object> d) {
      var m = G(d, "message") as Dictionary<string, object> ?? new Dictionary<string, object>();
      var u = (G(d, "user") ?? G(m, "user") ?? G(d, "sender")) as Dictionary<string, object> ?? new Dictionary<string, object>();
      var c = new ChatMsg { Platform = src };
      c.Name = Pick(G(m, "displayName"), G(m, "username"), G(u, "name"), G(u, "displayName"), G(u, "display_name"), G(u, "login"), G(u, "username"), G(d, "displayName"), G(d, "userName"), G(d, "username"), G(d, "user_name")) ?? "someone";
      c.Login = Pick(G(m, "username"), G(u, "login"), G(u, "username"), G(u, "name"), c.Name);
      c.UserId = Pick(G(u, "id"), G(m, "userId"), G(d, "userId")) ?? "";
      c.Text = Pick(G(m, "message"), G(m, "text"), G(m, "content"), G(d, "message") as string, G(d, "text"), G(d, "messageText"), G(d, "content")) ?? "";
      c.Color = Pick(G(m, "color"), G(u, "color"), G(d, "color")); c.Avatar = Pick(G(u, "profileImageUrl"), G(u, "profilePicture"), G(u, "profile_pic"), G(u, "avatar"), G(u, "avatarUrl"), G(d, "profileImageUrl"));
      var parts = (G(m, "parts") ?? G(d, "parts")) as ArrayList;
      if (parts != null) { var sb = new StringBuilder(); foreach (var p in parts) { var pd = p as Dictionary<string, object>; if (pd == null) continue; var type = G(pd, "type") as string; var emote = G(pd, "emote") as Dictionary<string, object>;
          var url = Pick(G(pd, "imageUrl"), emote == null ? null : G(emote, "imageUrl")); var t = G(pd, "text") as string ?? "";
          if ((type == "emote" || emote != null) && url != null) { c.Parts.Add(J.D("t", "emote", "name", t, "url", url)); c.EmoteNames.Add(t); } else c.Parts.Add(J.D("t", "text", "v", t)); sb.Append(t); }
        if (c.Text.Length == 0) c.Text = sb.ToString(); }
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
  }

  // ======================= registry =======================
  public static class Platforms {
    public static readonly TwitchSource Twitch = new TwitchSource(); public static readonly KickSource Kick = new KickSource();
    public static readonly YouTubeSource YouTube = new YouTubeSource(); public static readonly RumbleSource Rumble = new RumbleSource();
    public static readonly SbLink Sb = new SbLink();
    public static readonly List<ChatSource> All = new List<ChatSource> { Twitch, Kick, YouTube, Rumble };
    public static ChatSource Get(string id) { return All.FirstOrDefault(s => s.Id == id); }
    public static void Init() {
      foreach (var s in All) { try { s.Start(); } catch (Exception e) { Log.Err("chat", s.Label + " could not start: " + e.Message); } }
      Sb.Start();
      Settings.Changed += k => {
        var m = Regex.Match(k, "^platforms\\.(\\w+)\\."); if (!m.Success) return;
        if (m.Groups[1].Value == "streamerbot") { Sb.Restart(); return; }
        var s = Get(m.Groups[1].Value); if (s != null) s.Restart(); Hub.Publish("chat", J.D("type", "chat.status", "platforms", StatusAll())); }; }
    public static bool IsSetUp(string p) { var s = Get(p); return s != null && s.Configured; }
    public static bool HandlesChat(string p) { var s = Get(p); return s != null && s.Configured && s.State == "connected"; }
    public static bool HandlesViewers(string p) { var s = Get(p); return s != null && s.Configured && s.Viewers.Count != null && (DateTime.Now - s.Viewers.At).TotalSeconds < 120; }
    public static void ReconnectAll() { foreach (var s in All) s.Restart(); Sb.Restart(); }
    public static List<string> SendTargets() {
      var l = new List<string>();
      foreach (var s in All) if ((s.Configured && s.CanSend) || (Sb.Ready && s.Id != "rumble")) l.Add(s.Id);
      return l; }
    public static async Task<string> Send(string platform, string text) {
      var s = Get(platform); if (s == null) return "unknown platform " + platform;
      if (s.Configured && s.CanSend) return await s.Send(text);
      // not signed in to this platform in IXC (or the sign-in isn't available in this build): Streamer.bot sends it when it's connected
      if (Sb.Ready && platform != "rumble") return await Sb.SendTo(platform, text);
      return s.Configured ? s.SendNote : s.Label + " isn't set up in IXC"; }
    public static List<string> OwnNames() { var l = new List<string>(); foreach (var s in All) { try { l.AddRange(s.OwnNames()); } catch { } } lock (Sb.Broadcasters) l.AddRange(Sb.Broadcasters); return l.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
    public static Dictionary<string, object> StatusAll() {
      var d = new Dictionary<string, object>(); foreach (var s in All) d[s.Id] = s.Status();
      d["streamerbot"] = J.D("id", "streamerbot", "label", "Streamer.bot", "state", Sb.State, "detail", Sb.Detail, "auth", Sb.AuthState, "mode", Settings.Str("platforms.streamerbot.mode"), "configured", Settings.Str("platforms.streamerbot.mode") != "off");
      return d; }
  }
}
