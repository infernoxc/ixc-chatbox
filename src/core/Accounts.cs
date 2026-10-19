// IXC Core - "Connect account" for Twitch, YouTube and Kick. Tokens are stored encrypted (Secrets) and refreshed before they
// expire. Twitch uses its device sign-in (no secret needed). YouTube and Kick need an app secret for the last step; that step
// goes through the IXC relay (relay/), so no secret ships inside IXC.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public static class Accounts {
    public static readonly string[] TwitchScopes = { "chat:read", "chat:edit", "moderator:read:followers", "channel:read:subscriptions", "moderator:manage:chat_messages", "moderator:manage:banned_users", "clips:edit" };
    const string GoogleScope = "https://www.googleapis.com/auth/youtube.force-ssl";
    static readonly string[] KickScopes = { "user:read", "channel:read", "chat:write", "moderation:ban" };
    static readonly object L = new object();
    static readonly Dictionary<string, Dictionary<string, object>> Flows = new Dictionary<string, Dictionary<string, object>>();   // sign-ins in progress

    // app IDs: the streamer's own developer apps (entered in IXC, kept in the encrypted store) come first, then the ones built in.
    // A client secret is only ever the streamer's own, on their own PC: none ships inside the installer.
    static string Own(string app, string part) { return (Secrets.Str("apps." + app + "." + part) ?? "").Trim(); }
    static string IdOf(string app, string builtIn) { var o = Own(app, "id"); return o.Length > 0 ? o : Program.Default(builtIn); }
    static string TwitchId { get { return IdOf("twitch", "twitchClientId"); } }
    static string GoogleId { get { return IdOf("google", "googleClientId"); } }
    static string KickId { get { return IdOf("kick", "kickClientId"); } }
    static string SecretOf(string p) { var app = p == "youtube" ? "google" : p; var o = Own(app, "secret"); return o.Length > 0 && Own(app, "id").Length > 0 ? o : Program.Default(p == "youtube" ? "googleClientSecret" : "kickClientSecret"); }
    static string Relay { get { return Remote.RelayUrl; } }
    public static bool Available(string p) {
      if (p == "twitch") return TwitchId.Length > 0;
      if (p == "youtube") return GoogleId.Length > 0 && (SecretOf(p).Length > 0 || Relay.Length > 0);
      if (p == "kick") return KickId.Length > 0 && (SecretOf(p).Length > 0 || Relay.Length > 0);
      return false; }
    // why IXC can't send as this account yet, in the order the streamer has to fix it (null = the account is ready): app -> sign-in -> still valid
    public static string ReplyBlocker(string p, string label) {
      var app = p == "youtube" ? "Google (YouTube)" : label;
      if (!Available(p)) return "To reply on " + label + ": add your " + app + " app (Platforms & accounts > Your apps), then connect your " + label + " account";
      var a = Raw(p); if (a == null) return "To reply on " + label + ": connect your " + label + " account (Platforms & accounts > Connect)";
      if (J.Bool(a, "invalid", false)) { var why = J.Str(a, "reason", ""); return label + ": " + (why.Length > 0 ? why : "sign-in expired - connect your " + label + " account again"); }
      return null; }
    public static string RedirectUri(string p) { return (p == "youtube" ? "http://127.0.0.1:" : "http://localhost:") + Program.Port + "/oauth/" + p; }
    // what the "Your apps" form shows: IDs (public) and whether a secret is saved, never the secret
    public static Dictionary<string, object> Apps() {
      Func<string, string, Dictionary<string, object>> one = (app, p) => J.D("id", Own(app, "id"), "secretSet", Own(app, "secret").Length > 0, "own", Own(app, "id").Length > 0,
        "builtIn", Program.Default(app == "google" ? "googleClientId" : app + "ClientId").Length > 0, "available", Available(p), "redirect", p == "twitch" ? "" : RedirectUri(p));
      return J.D("twitch", one("twitch", "twitch"), "google", one("google", "youtube"), "kick", one("kick", "kick")); }
    static readonly Dictionary<string, string> IdPattern = new Dictionary<string, string> {
      { "twitch", "^[a-z0-9]{20,40}$" }, { "google", "^[\\w.-]{3,120}\\.apps\\.googleusercontent\\.com$" }, { "kick", "^[A-Za-z0-9]{10,64}$" } };
    // patch: { twitch: { id }, google: { id, secret }, kick: { id, secret } }. A missing field is left as it is, "" clears it.
    // Returns null when saved, otherwise the problem (then nothing is saved).
    // Diagnostics (auth.log) name the fields and the outcome only - never a value. The write is read back before "saved" is reported.
    public static string SetApps(Dictionary<string, object> patch) {
      var names = new List<string>(); if (patch != null) foreach (var app in new[] { "twitch", "google", "kick" }) { var d = J.Obj(patch, app); if (d == null) continue; foreach (var f in new[] { "id", "secret" }) if (d.ContainsKey(f)) names.Add(app + "." + f); }
      string err; try { err = SetAppsCore(patch); } catch (Exception e) { err = "IXC couldn't write its encrypted settings (" + e.GetType().Name + ")"; }
      if (err != null) Log.Info("auth", "your app settings were NOT saved (HTTP 400; fields: " + (names.Count > 0 ? string.Join(", ", names) : "none") + "): " + err);
      return err; }
    static string SetAppsCore(Dictionary<string, object> patch) {
      if (patch == null) return "nothing to save";
      var todo = new List<KeyValuePair<string, string>>();
      foreach (var app in new[] { "twitch", "google", "kick" }) {
        var d = J.Obj(patch, app); if (d == null) continue;
        if (d.ContainsKey("id")) { var id = (J.Str(d, "id", "") ?? "").Trim();
          if (id.Length > 0 && !Regex.IsMatch(id, IdPattern[app])) return app == "google" ? "That doesn't look like a Google client ID (it ends with .apps.googleusercontent.com)" : "That doesn't look like a " + (app == "twitch" ? "Twitch" : "Kick") + " Client ID - copy it again from the developer page";
          todo.Add(new KeyValuePair<string, string>("apps." + app + ".id", id)); }
        if (d.ContainsKey("secret") && app != "twitch") { var sec = (J.Str(d, "secret", "") ?? "").Trim();
          if (sec.Length > 0 && (sec.Length < 10 || sec.Length > 200 || Regex.IsMatch(sec, "\\s"))) return "That doesn't look like a client secret - copy it again";
          todo.Add(new KeyValuePair<string, string>("apps." + app + ".secret", sec)); } }
      if (todo.Count == 0) return "nothing to save";
      var old = todo.ToDictionary(kv => kv.Key, kv => Secrets.Get(kv.Key));
      foreach (var kv in todo) Secrets.Set(kv.Key, kv.Value.Length == 0 ? null : kv.Value);
      if (Secrets.SaveError != null) { foreach (var kv in old) Secrets.Set(kv.Key, kv.Value); return "IXC couldn't write its encrypted settings file (" + Secrets.SaveError + "), so nothing was saved"; }
      foreach (var kv in todo) { var back = (Secrets.Str(kv.Key) ?? "").Trim(); if (back != kv.Value) return "IXC wrote your " + kv.Key.Split('.')[1] + " " + kv.Key.Split('.')[2] + " but could not read it back, so it was not kept"; }
      Log.Info("auth", "your app settings were saved (" + string.Join(", ", todo.Select(kv => kv.Key.Split('.')[1] + " " + kv.Key.Split('.')[2] + (kv.Value.Length == 0 ? " cleared" : ""))) + ")");
      Changed("twitch"); return null; }

    // ---------- stored accounts ----------
    static Dictionary<string, object> Raw(string p) { return Secrets.Obj("account." + p); }
    static void Save(string p, Dictionary<string, object> a) { Secrets.Set("account." + p, a); Changed(p); }
    public static bool Has(string p) { return Raw(p) != null; }
    public static bool IsInvalid(string p) { var a = Raw(p); return a != null && J.Bool(a, "invalid", false); }
    public static Dictionary<string, object> Info(string p) {
      var a = Raw(p); if (a == null) return null;
      return J.D("login", J.Str(a, "login", ""), "displayName", J.Str(a, "displayName", ""), "userId", J.Str(a, "userId", ""), "scopes", J.List(a, "scopes"), "invalid", J.Bool(a, "invalid", false), "reason", J.Str(a, "reason", ""), "since", J.Str(a, "since", "")); }
    public static void MarkInvalid(string p, string why) { var a = Raw(p); if (a == null || J.Bool(a, "invalid", false)) return; a["invalid"] = true; a["reason"] = why; Save(p, a); Log.Warn("auth", p + ": " + why); }
    public static void SignOut(string p) {
      var a = Raw(p);
      if (a != null && p == "twitch") { var tok = J.Str(a, "access", ""); Task.Run(() => Http.Request("POST", Ep.Get("twitch_id", "https://id.twitch.tv") + "/oauth2/revoke", "client_id=" + TwitchId + "&token=" + Uri.EscapeDataString(tok), "application/x-www-form-urlencoded", null, 8000)); }
      Secrets.Set("account." + p, null); Log.Info("auth", "signed out of " + p); Changed(p); var s = Platforms.Get(p); if (s != null) s.Restart(); }
    static void Changed(string p) { Hub.Publish("accounts", J.D("type", "accounts", "accounts", All())); Hub.Publish("status", Status.Msg()); }
    public static Dictionary<string, object> All() {
      var d = new Dictionary<string, object>();
      foreach (var p in new[] { "twitch", "youtube", "kick" }) { Dictionary<string, object> flow; lock (L) Flows.TryGetValue(p, out flow);
        d[p] = J.D("available", Available(p), "account", Info(p), "flow", flow == null ? null : J.D("state", flow["state"], "message", flow["message"], "userCode", J.Str(flow, "userCode", ""), "url", J.Str(flow, "url", ""))); }
      d["apps"] = Apps(); d["rumble"] = J.D("available", true, "account", RumbleUrl().Length > 0 ? J.D("login", "API link saved") : null);
      return d; }
    public static string RumbleUrl() { var u = Secrets.Str("rumble.apiUrl") ?? ""; return u.Length > 0 ? Ep.Get("rumble_api", u) : ""; }
    public static string SetRumbleUrl(string url) {
      url = (url ?? "").Trim(); if (url.Length == 0) { Secrets.Set("rumble.apiUrl", null); Platforms.Rumble.Restart(); Changed("rumble"); return null; }
      Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https" || !(u.Host == "rumble.com" || u.Host.EndsWith(".rumble.com"))) return "That doesn't look like a Rumble Live Stream API link (it starts with https://rumble.com/-livestream-api/...).";
      Secrets.Set("rumble.apiUrl", url);
      // one restart only: switching Rumble on restarts it by itself (two quick restarts could mark a new message as "old")
      if (Settings.Bool("platforms.rumble.enabled")) Platforms.Rumble.Restart(); else Settings.Set("platforms.rumble.enabled", true);
      Changed("rumble"); return null; }

    // ---------- tokens ----------
    public static string AccessToken(string p, string needScope) {
      var a = Raw(p); if (a == null || J.Bool(a, "invalid", false)) return null;
      if (needScope != null && !J.List(a, "scopes").Contains(needScope)) return null;
      if (J.Long(a, "expiresAt", 0) - U.Now() < 300000 && !Refresh(p)) return null;
      return J.Str(Raw(p), "access", null); }
    static readonly Dictionary<string, object> refreshLocks = new Dictionary<string, object> { { "twitch", new object() }, { "youtube", new object() }, { "kick", new object() } };
    // Google ends the sign-in of an app left in "Testing" after 7 days (refresh answers 400 invalid_grant): the usual reason a YouTube sign-in "expires"
    const string ExpiredGoogle = "sign-in expired - connect again. If it keeps expiring after a week: Google Cloud > OAuth consent screen > Publish app (apps in Testing lose their sign-in after 7 days)";
    public static bool Refresh(string p) {
      lock (refreshLocks[p]) {
        var a = Raw(p); if (a == null) return false; if (J.Long(a, "expiresAt", 0) - U.Now() > 300000) return true;
        var rt = J.Str(a, "refresh", ""); if (rt.Length == 0) { MarkInvalid(p, "sign-in expired"); return false; }
        Http.Resp r;
        if (p == "twitch") r = Http.Request("POST", Ep.Get("twitch_id", "https://id.twitch.tv") + "/oauth2/token", "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(rt) + "&client_id=" + TwitchId, "application/x-www-form-urlencoded", null, 12000);
        else r = Broker(p, J.D("grant_type", "refresh_token", "refresh_token", rt));
        if (r.Code == 400 || r.Code == 401) { MarkInvalid(p, p == "youtube" ? ExpiredGoogle : "sign-in expired - please connect again"); return false; }
        if (!r.Ok) { Log.Warn("auth", p + " token refresh failed: " + (r.Error ?? r.Code.ToString())); return false; }   // network trouble: keep the account, try later
        var t = J.Parse(r.Body); Apply(a, t); Save(p, a); Log.Info("auth", p + " sign-in refreshed"); return true; } }
    static void Apply(Dictionary<string, object> a, Dictionary<string, object> t) {
      a["access"] = J.Str(t, "access_token", ""); var rt = J.Str(t, "refresh_token", ""); if (rt.Length > 0) a["refresh"] = rt;
      a["expiresAt"] = U.Now() + J.Long(t, "expires_in", 3600) * 1000; a["invalid"] = false; a["reason"] = "";
      var sc = J.Get(t, "scope"); if (sc is string) a["scopes"] = ((string)sc).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(); else if (sc is ArrayList) a["scopes"] = ((ArrayList)sc).Cast<object>().Select(x => x.ToString()).ToList(); }
    static Http.Resp Broker(string p, Dictionary<string, object> body) {
      var secret = SecretOf(p);
      if (secret.Length > 0) {   // the streamer's own app (or a developer build): talk to the provider directly, no relay
        body["client_id"] = p == "youtube" ? GoogleId : KickId; body["client_secret"] = secret;
        var form = string.Join("&", body.Select(kv => kv.Key + "=" + Uri.EscapeDataString(Convert.ToString(kv.Value))));
        return Http.Request("POST", p == "youtube" ? Ep.Get("google_token", "https://oauth2.googleapis.com/token") : Ep.Get("kick_id", "https://id.kick.com") + "/oauth/token", form, "application/x-www-form-urlencoded", null, 15000); }
      if (Relay.Length == 0) return new Http.Resp { Error = "the IXC relay isn't set up" };
      return Http.Request("POST", Relay.TrimEnd('/') + "/oauth/" + p + "/token", J.Ser(body), "application/json", null, 15000); }

    // ---------- platform APIs (retry once after a refresh on 401) ----------
    static Http.Resp Api(string p, string baseUrl, string method, string path, string body, Func<string, Dictionary<string, string>> hdr) {
      for (int i = 0; i < 2; i++) {
        var tok = AccessToken(p, null); if (tok == null) return null;
        var r = Http.Request(method, baseUrl + path, body, "application/json", hdr(tok), 15000);
        if (r.Code == 401 && i == 0) { var a = Raw(p); if (a != null) { a["expiresAt"] = 0L; Secrets.Set("account." + p, a); } if (Refresh(p)) continue; return r; }
        return r; }
      return null; }
    public static Http.Resp Helix(string method, string path, string body) { return Api("twitch", Ep.Get("twitch_api", "https://api.twitch.tv/helix") + "/", method, path, body, t => new Dictionary<string, string> { { "Client-Id", TwitchId }, { "Authorization", "Bearer " + t } }); }
    public static Http.Resp KickApi(string method, string path, string body) { return Api("kick", Ep.Get("kick_api", "https://api.kick.com/public/v1") + "/", method, path, body, t => new Dictionary<string, string> { { "Authorization", "Bearer " + t }, { "Accept", "application/json" } }); }
    public static Http.Resp GoogleApi(string method, string path, string body) { return Api("youtube", Ep.Get("google_api", "https://www.googleapis.com/youtube/v3") + "/", method, path, body, t => new Dictionary<string, string> { { "Authorization", "Bearer " + t } }); }

    // ---------- sign-in flows ----------
    static void Flow(string p, string state, string message, Dictionary<string, object> extra) {
      var f = J.D("state", state, "message", message, "at", DateTime.Now); if (extra != null) foreach (var kv in extra) f[kv.Key] = kv.Value;
      lock (L) Flows[p] = f; Changed(p); }
    public static Dictionary<string, object> Begin(string p) {
      if (!Available(p)) return J.D("ok", false, "error", "Add your " + (p == "twitch" ? "Twitch" : p == "youtube" ? "Google (YouTube)" : "Kick") + " app first (Platforms & accounts > Your apps), then connect.");
      if (p == "twitch") return BeginTwitch();
      var verifier = U.Token(48); string challenge; using (var h = SHA256.Create()) challenge = Convert.ToBase64String(h.ComputeHash(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
      var state = U.Token(16); var redirect = RedirectUri(p);
      string url = p == "youtube"
        ? Ep.Get("google_auth", "https://accounts.google.com/o/oauth2/v2/auth") + "?client_id=" + Uri.EscapeDataString(GoogleId) + "&redirect_uri=" + Uri.EscapeDataString(redirect) + "&response_type=code&scope=" + Uri.EscapeDataString(GoogleScope) + "&access_type=offline&prompt=consent&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + state
        : Ep.Get("kick_id", "https://id.kick.com") + "/oauth/authorize?response_type=code&client_id=" + Uri.EscapeDataString(KickId) + "&redirect_uri=" + Uri.EscapeDataString(redirect) + "&scope=" + Uri.EscapeDataString(string.Join(" ", KickScopes)) + "&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + state;
      Flow(p, "waiting", "Finish signing in in your browser", J.D("url", url, "verifier", verifier, "oauthState", state, "redirect", redirect));
      OpenBrowser(url); return J.D("ok", true, "url", url); }
    static Dictionary<string, object> BeginTwitch() {
      var r = Http.Request("POST", Ep.Get("twitch_id", "https://id.twitch.tv") + "/oauth2/device", "client_id=" + TwitchId + "&scopes=" + Uri.EscapeDataString(string.Join(" ", TwitchScopes)), "application/x-www-form-urlencoded", null, 12000);
      if (!r.Ok) return J.D("ok", false, "error", "Couldn't reach Twitch (" + (r.Error ?? r.Code.ToString()) + ")");
      var d = J.Parse(r.Body); var code = J.Str(d, "user_code", ""); var url = J.Str(d, "verification_uri", "https://www.twitch.tv/activate"); var device = J.Str(d, "device_code", "");
      int interval = Math.Max(2, J.Int(d, "interval", 5)), expires = J.Int(d, "expires_in", 1800);
      Flow("twitch", "waiting", "Approve IXC on Twitch (code " + code + ")", J.D("userCode", code, "url", url));
      OpenBrowser(url);
      Task.Run(() => {
        var until = DateTime.Now.AddSeconds(expires);
        while (DateTime.Now < until) {
          Thread.Sleep(interval * 1000);
          Dictionary<string, object> cur; lock (L) Flows.TryGetValue("twitch", out cur); if (cur == null || J.Str(cur, "userCode", "") != code) return;   // cancelled / restarted
          var t = Http.Request("POST", Ep.Get("twitch_id", "https://id.twitch.tv") + "/oauth2/token", "client_id=" + TwitchId + "&scopes=" + Uri.EscapeDataString(string.Join(" ", TwitchScopes)) + "&device_code=" + device + "&grant_type=urn:ietf:params:oauth:grant-type:device_code", "application/x-www-form-urlencoded", null, 12000);
          if (t.Ok) { Finish("twitch", J.Parse(t.Body)); return; }
          var msg = J.Str(J.Parse(t.Body ?? ""), "message", t.Error ?? "");
          if (msg.Contains("authorization_pending")) continue; if (msg.Contains("slow_down")) { interval++; continue; }
          if (t.Code == 0) continue;   // network blip
          Flow("twitch", "failed", msg.Contains("denied") ? "Twitch sign-in was cancelled" : "Twitch sign-in failed: " + msg, null); return; }
        Flow("twitch", "failed", "The Twitch code expired - try again", null); });
      return J.D("ok", true, "userCode", code, "url", url); }
    // browser redirect back to IXC (YouTube / Kick)
    public static string Callback(string p, System.Collections.Specialized.NameValueCollection q) {
      Dictionary<string, object> f; lock (L) Flows.TryGetValue(p, out f);
      if (f == null || J.Str(f, "state", "") != "waiting") return "This sign-in link has expired. Start again from IXC.";
      if (!U.SlowEq(q["state"] ?? "", J.Str(f, "oauthState", "-"))) return "This sign-in didn't come from IXC (state mismatch). Start again from IXC.";
      if (!string.IsNullOrEmpty(q["error"])) { Flow(p, "failed", "Sign-in was cancelled (" + q["error"] + ")", null); return "Sign-in was cancelled. You can close this tab."; }
      var r = Broker(p, J.D("grant_type", "authorization_code", "code", q["code"] ?? "", "code_verifier", J.Str(f, "verifier", ""), "redirect_uri", J.Str(f, "redirect", "")));
      if (!r.Ok) { Flow(p, "failed", "Sign-in failed: " + (r.Error ?? (r.Code + " " + U.Trunc(r.Body, 150))), null); return "Sign-in failed. Go back to IXC for details."; }
      Finish(p, J.Parse(r.Body)); var i = Info(p); return i != null && !J.Bool(i, "invalid", false) ? "Connected! You can close this tab and go back to IXC." : "Sign-in failed. Go back to IXC for details."; }
    static void Finish(string p, Dictionary<string, object> tok) {
      var a = new Dictionary<string, object> { { "since", DateTime.Now.ToString("yyyy-MM-dd HH:mm") } }; Apply(a, tok);
      try {
        if (p == "twitch") { var v = Http.Request("GET", Ep.Get("twitch_id", "https://id.twitch.tv") + "/oauth2/validate", null, null, new Dictionary<string, string> { { "Authorization", "OAuth " + J.Str(a, "access", "") } }, 12000); var d = J.Parse(v.Body ?? "");
          a["login"] = J.Str(d, "login", ""); a["userId"] = J.Str(d, "user_id", ""); a["displayName"] = J.Str(d, "login", ""); if (!a.ContainsKey("scopes") || ((List<string>)a["scopes"]).Count == 0) a["scopes"] = J.List(d, "scopes"); }
        else if (p == "youtube") { var v = Http.Request("GET", Ep.Get("google_api", "https://www.googleapis.com/youtube/v3") + "/channels?part=snippet&mine=true", null, null, new Dictionary<string, string> { { "Authorization", "Bearer " + J.Str(a, "access", "") } }, 12000);
          var items = J.Get(J.Parse(v.Body ?? ""), "items") as ArrayList; var it = items != null && items.Count > 0 ? (Dictionary<string, object>)items[0] : null;
          a["userId"] = it == null ? "" : J.Str(it, "id", ""); a["displayName"] = it == null ? "" : J.Str(it, "snippet.title", ""); a["login"] = it == null ? "" : J.Str(it, "snippet.customUrl", J.Str(it, "snippet.title", "")); }
        else if (p == "kick") { var v = Http.Request("GET", Ep.Get("kick_api", "https://api.kick.com/public/v1") + "/users", null, null, new Dictionary<string, string> { { "Authorization", "Bearer " + J.Str(a, "access", "") } }, 12000);
          var arr = J.Get(J.Parse(v.Body ?? ""), "data") as ArrayList; var it = arr != null && arr.Count > 0 ? (Dictionary<string, object>)arr[0] : null;
          a["userId"] = it == null ? "" : J.Str(it, "user_id", ""); a["login"] = it == null ? "" : J.Str(it, "name", ""); a["displayName"] = a["login"]; } }
      catch (Exception e) { Log.Warn("auth", p + " profile lookup failed: " + e.Message); }
      Save(p, a); Log.Info("auth", "signed in to " + p + " as " + J.Str(a, "login", "?"));
      // first sign-in: use it as the channel too, so a beginner doesn't have to type it
      var src = Platforms.Get(p);
      if (src != null) { var patch = new Dictionary<string, object>(); if (src.Channel.Length == 0) { var ch = p == "youtube" ? J.Str(a, "userId", "") : J.Str(a, "login", ""); if (ch.Length > 0) patch["platforms." + p + ".channel"] = ch; }
        if (!src.Enabled) patch["platforms." + p + ".enabled"] = true; if (patch.Count > 0) Settings.Apply(patch); else src.Restart(); }
      // only now say "connected", so the window never shows a sign-in whose channel isn't set yet
      Flow(p, "connected", "Connected as " + J.Str(a, "displayName", J.Str(a, "login", "you")), null); }
    public static void Cancel(string p) { lock (L) Flows.Remove(p); Changed(p); }
    public static void OpenBrowser(string url) {
      if (Program.TestMode) { Log.Info("auth", "(test) would open " + url.Split('?')[0]); return; }
      try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception e) { Log.Warn("auth", "could not open the browser: " + e.Message); } }
  }
}
