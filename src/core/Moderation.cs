// IXC Core - moderation from the chat panel (delete a message, time out, ban) through each platform's official API, as the
// account connected in IXC (the streamer, or a moderator of the channel):
//   Twitch  - Helix: DELETE /moderation/chat, POST /moderation/bans (scopes moderator:manage:chat_messages, moderator:manage:banned_users)
//   YouTube - Data API: liveChatMessages.delete, liveChatBans.insert
//   Kick    - public API: POST /moderation/bans (scope moderation:ban); deleting single messages isn't offered
//   Rumble  - no API for moderation
// What a platform answers is shown as it is: nothing is reported as done unless the platform said so.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace IXC {
  public static class Moderation {
    static readonly Dictionary<string, string> twitchIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // what the panel may offer per platform right now
    public static Dictionary<string, object> Caps(string p) {
      bool acc = Accounts.Has(p) && !Accounts.IsInvalid(p);
      if (p == "twitch") return J.D("delete", acc, "timeout", acc, "ban", acc);
      if (p == "youtube") return J.D("delete", acc, "timeout", acc, "ban", acc);
      if (p == "kick") return J.D("delete", false, "timeout", acc, "ban", acc);
      return J.D("delete", false, "timeout", false, "ban", false); }

    // action: delete | timeout | ban. Returns null when the platform confirmed it, otherwise why not.
    public static string Do(string p, string action, string msgId, string userId, string login, int seconds, string reason) {
      if (!new[] { "delete", "timeout", "ban" }.Contains(action)) return "unknown action";
      if (!J.Bool(Caps(p), action, false)) return p == "rumble" ? "Rumble doesn't let apps moderate chat" : p == "kick" && action == "delete" ? "Deleting single Kick messages isn't available to apps - time out or ban instead" : "Connect your " + (Platforms.Get(p) == null ? p : Platforms.Get(p).Label) + " account to moderate (Platforms & accounts)";
      if (action == "timeout") seconds = Math.Max(10, Math.Min(1209600, seconds <= 0 ? 600 : seconds));
      reason = (reason ?? "").Trim(); if (reason.Length > 200) reason = reason.Substring(0, 200);
      string err;
      try { err = p == "twitch" ? Twitch(action, msgId, userId, login, seconds, reason) : p == "youtube" ? YouTube(action, msgId, userId, seconds) : Kick(action, userId, seconds, reason); }
      catch (Exception e) { err = U.Plain(e); }
      if (err == null) {
        Log.Info("chat", p + ": " + (action == "delete" ? "deleted a message from " : action == "timeout" ? "timed out " : "banned ") + (login ?? userId) + (action == "timeout" ? " for " + seconds + " s" : ""));
        if (action == "delete") Chat.Delete(p, msgId, null); else Chat.Delete(p, null, login); }
      else Log.Warn("chat", p + " " + action + " failed: " + err);
      return err; }

    static string Answer(Http.Resp r, string platform, string scopeHint) {
      if (r == null) return "Connect your " + platform + " account first";
      if (r.Ok) return null;
      var body = U.Trunc(r.Body ?? "", 200);
      if (r.Code == 401 || (r.Code == 403 && body.IndexOf("scope", StringComparison.OrdinalIgnoreCase) >= 0)) return "Sign in to " + platform + " again: IXC needs the moderation permission" + (scopeHint.Length > 0 ? " (" + scopeHint + ")" : "");
      if (r.Code == 403) return platform + ": your account isn't a moderator of this channel";
      return platform + ": " + (r.Code > 0 ? r.Code + " " + body : r.Error); }

    static string Twitch(string action, string msgId, string userId, string login, int seconds, string reason) {
      var a = Accounts.Info("twitch"); var me = J.Str(a, "userId", ""); var chan = Platforms.Twitch.Channel.Trim().TrimStart('#').ToLowerInvariant();
      var scopes = J.List(a, "scopes"); var need = action == "delete" ? "moderator:manage:chat_messages" : "moderator:manage:banned_users";
      if (scopes.Count > 0 && !scopes.Contains(need)) return "Sign in to Twitch again: IXC needs the moderation permission (" + need + ")";
      string bid; lock (twitchIds) twitchIds.TryGetValue(chan, out bid);
      if (string.IsNullOrEmpty(bid)) {
        if (string.Equals(J.Str(a, "login", ""), chan, StringComparison.OrdinalIgnoreCase)) bid = me;
        else { var r = Accounts.Helix("GET", "users?login=" + Uri.EscapeDataString(chan), null); if (r == null || !r.Ok) return Answer(r, "Twitch", ""); var arr = J.Get(J.Parse(r.Body), "data") as ArrayList; bid = arr != null && arr.Count > 0 ? J.Str((Dictionary<string, object>)arr[0], "id", "") : ""; }
        if (bid.Length == 0) return "Twitch: couldn't find the channel " + chan; lock (twitchIds) twitchIds[chan] = bid; }
      if (action == "delete") {
        if (string.IsNullOrEmpty(msgId)) return "that message has no id";
        return Answer(Accounts.Helix("DELETE", "moderation/chat?broadcaster_id=" + bid + "&moderator_id=" + me + "&message_id=" + Uri.EscapeDataString(msgId), null), "Twitch", need); }
      if (string.IsNullOrEmpty(userId)) return "Twitch didn't say who sent this message";
      var data = J.D("user_id", userId); if (action == "timeout") data["duration"] = seconds; if (reason.Length > 0) data["reason"] = reason;
      return Answer(Accounts.Helix("POST", "moderation/bans?broadcaster_id=" + bid + "&moderator_id=" + me, J.Ser(J.D("data", data))), "Twitch", need); }

    static string YouTube(string action, string msgId, string channelId, int seconds) {
      if (action == "delete") {
        if (string.IsNullOrEmpty(msgId)) return "that message has no id";
        return Answer(Accounts.GoogleApi("DELETE", "liveChat/messages?id=" + Uri.EscapeDataString(msgId), null), "YouTube", ""); }
      if (string.IsNullOrEmpty(channelId)) return "YouTube didn't say who sent this message";
      var lc = Platforms.YouTube.EnsureLiveChatId(); if (lc.StartsWith("!")) return lc.Substring(1);
      var sn = J.D("liveChatId", lc, "type", action == "timeout" ? "temporary" : "permanent", "bannedUserDetails", J.D("channelId", channelId));
      if (action == "timeout") sn["banDurationSeconds"] = seconds;
      return Answer(Accounts.GoogleApi("POST", "liveChat/bans?part=snippet", J.Ser(J.D("snippet", sn))), "YouTube", ""); }

    static string Kick(string action, string userId, int seconds, string reason) {
      long uid; if (!long.TryParse(userId ?? "", out uid)) return "Kick didn't say who sent this message";
      var bid = Platforms.Kick.UserId; if (bid.Length == 0) { Platforms.Kick.Resolve(); bid = Platforms.Kick.UserId; } long b; if (!long.TryParse(bid, out b)) return "couldn't find the Kick channel";
      var body = J.D("broadcaster_user_id", b, "user_id", uid); if (action == "timeout") body["duration"] = Math.Max(1, (int)Math.Round(seconds / 60.0)); if (reason.Length > 0) body["reason"] = reason;   // Kick takes minutes
      return Answer(Accounts.KickApi("POST", "moderation/bans", J.Ser(body)), "Kick", "moderation:ban"); }

    public static bool Api(Ctx ctx, string path, string m) {
      if (path != "/api/chat/moderate" || m != "POST") return false;
      var b = Http.BodyJson(ctx);
      var err = Do(J.Str(b, "platform", ""), J.Str(b, "action", ""), J.Str(b, "id", null), J.Str(b, "userId", null), J.Str(b, "login", null), J.Int(b, "seconds", 600), J.Str(b, "reason", ""));
      Http.Json(ctx, err == null ? 200 : 400, J.D("ok", err == null, "error", err)); return true; }
  }
}
