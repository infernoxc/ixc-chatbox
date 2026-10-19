// IXC Core - multistream destinations: where the one stream OBS encodes should also go (Twitch, YouTube, Kick, Facebook ... up to 8).
// This step is the destination registry only: each destination's server URL and stream key are saved (encrypted for this Windows
// user, the key is write-only and never shown, logged or sent back), and "Check" proves the server is reachable - TCP, TLS for rtmps://
// and the RTMP handshake, with retries and a cancellable timeout - WITHOUT sending the stream key or any video.
// NOT implemented yet: forwarding the stream (the relay engine). Nothing here starts, stops or touches the OBS stream.
// Part of IXC - Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public static class Multistream {
    public const int Max = 8;
    const string Store = "multistream.destinations";
    static readonly object L = new object();
    sealed class St { public string State = "idle", Detail = ""; public DateTime At = DateTime.MinValue; public int Attempt; public CancellationTokenSource Cts; }
    static readonly Dictionary<string, St> States = new Dictionary<string, St>();

    // Server URLs the platforms publish. A preset is only a starting point: every platform can change or personalise its ingest
    // (Kick and TikTok give each account its own address), so "url" is empty where the streamer has to copy it from the platform's dashboard.
    static readonly object[][] Presets = {
      new object[] { "twitch", "Twitch", "rtmp://live.twitch.tv/app", "Twitch also offers a closer ingest server in your dashboard." },
      new object[] { "youtube", "YouTube", "rtmp://a.rtmp.youtube.com/live2", "Key: YouTube Studio > Go live > Stream." },
      new object[] { "kick", "Kick", "", "Copy the server URL (rtmps://...) and key from your Kick dashboard > Stream." },
      new object[] { "facebook", "Facebook", "rtmps://live-api-s.facebook.com:443/rtmp/", "Facebook keys are short-lived: create a new one for each live." },
      new object[] { "tiktok", "TikTok", "", "TikTok gives the server URL and key (account must have LIVE access) in TikTok LIVE Studio." },
      new object[] { "rumble", "Rumble", "", "Copy the server URL and key from your Rumble live dashboard." },
      new object[] { "trovo", "Trovo", "", "Copy the server URL and key from your Trovo creator dashboard." },
      new object[] { "custom", "Custom", "", "Any RTMP or RTMPS server." } };

    static Dictionary<string, object> All() { var o = Secrets.Get(Store) as Dictionary<string, object>; return o ?? new Dictionary<string, object>(); }
    static void Put(Dictionary<string, object> all) { Secrets.Set(Store, all); }
    static St StateOf(string id) { lock (L) { St s; if (!States.TryGetValue(id, out s)) States[id] = s = new St(); return s; } }

    // "rtmp(s)://host[:port]/app" - no login in the address and no "?key=": the key goes in its own field and is never part of a URL IXC shows
    public static string ValidateUrl(string url) {
      url = (url ?? "").Trim(); if (url.Length == 0) return "Enter the server URL (it starts with rtmp:// or rtmps://)";
      Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "rtmp" && u.Scheme != "rtmps")) return "The server URL must start with rtmp:// or rtmps://";
      if (u.Host.Length == 0) return "The server URL has no host name";
      if (u.UserInfo.Length > 0) return "Remove the user name or password from the URL";
      if (u.Query.Length > 0) return "Remove everything after the ? in the URL (the stream key goes in the Stream key field)";
      return null; }
    public static string Redact(string url) { Uri u; return Uri.TryCreate(url ?? "", UriKind.Absolute, out u) ? u.Scheme + "://" + u.Host + (u.IsDefaultPort ? "" : ":" + u.Port) : "(invalid address)"; }

    // for the relay: the destinations in their saved order, why one cannot be used, and its address + key (only ever held in memory by the worker)
    public static List<string> Ids() { return All().OrderBy(kv => J.Str((Dictionary<string, object>)kv.Value, "order", "")).Select(kv => kv.Key).ToList(); }
    public static string NotReady(string id) {
      var all = All(); if (!all.ContainsKey(id)) return "removed"; var d = (Dictionary<string, object>)all[id];
      if (!J.Bool(d, "enabled", true)) return "turned off"; if (ValidateUrl(J.Str(d, "url", "")) != null) return "no valid server URL"; if (J.Str(d, "key", "").Length == 0) return "no stream key saved"; return null; }
    public static string[] Target(string id) { var all = All(); if (!all.ContainsKey(id)) return null; var d = (Dictionary<string, object>)all[id]; return new[] { J.Str(d, "url", ""), J.Str(d, "key", ""), J.Str(d, "name", id) }; }
    static Dictionary<string, object> View(string id, Dictionary<string, object> d) {
      var s = StateOf(id);
      return J.D("id", id, "platform", J.Str(d, "platform", "custom"), "name", J.Str(d, "name", ""), "url", J.Str(d, "url", ""), "enabled", J.Bool(d, "enabled", true),
        "keySet", J.Str(d, "key", "").Length > 0, "relay", Relay.OutView(id), "state", s.State, "detail", s.Detail, "attempt", s.Attempt, "checkedAgoSec", s.At == DateTime.MinValue ? -1 : (int)(DateTime.Now - s.At).TotalSeconds); }
    public static Dictionary<string, object> Snapshot() {
      var all = All(); var list = all.OrderBy(kv => J.Str((Dictionary<string, object>)kv.Value, "order", "")).Select(kv => (object)View(kv.Key, (Dictionary<string, object>)kv.Value)).ToList();
      return J.D("max", Max, "destinations", list, "presets", Presets.Select(p => (object)J.D("platform", p[0], "label", p[1], "url", p[2], "suggested", ((string)p[2]).Length > 0, "note", p[3])).ToList(),
        "relay", Relay.Status(),
        "obsStreaming", ObsControl.Streaming); }

    public static Dictionary<string, object> Save(Dictionary<string, object> b, out string err) {
      err = null; var all = All(); var id = J.Str(b, "id", ""); bool isNew = id.Length == 0 || !all.ContainsKey(id);
      if (isNew && all.Count >= Max) { err = "IXC manages up to " + Max + " destinations"; return null; }
      var d = isNew ? new Dictionary<string, object>() : (Dictionary<string, object>)all[id];
      var url = b.ContainsKey("url") ? J.Str(b, "url", "") : J.Str(d, "url", ""); err = ValidateUrl(url); if (err != null) return null;
      var platform = (b.ContainsKey("platform") ? J.Str(b, "platform", "custom") : J.Str(d, "platform", "custom")).ToLowerInvariant();
      if (!Presets.Any(p => (string)p[0] == platform)) platform = "custom";
      var name = (b.ContainsKey("name") ? J.Str(b, "name", "") : J.Str(d, "name", "")).Trim(); if (name.Length == 0) name = Presets.First(p => (string)p[0] == platform)[1].ToString();
      if (name.Length > 40) name = name.Substring(0, 40);
      // the key: absent or empty = keep the saved one; "clearKey" removes it. It is stored encrypted and never returned.
      var key = J.Str(d, "key", ""); var nk = J.Str(b, "key", "").Trim();
      if (nk.Length > 0) { if (nk.Length > 512 || Regex.IsMatch(nk, "\\s")) { err = "That stream key doesn't look right (no spaces, at most 512 characters)"; return null; } key = nk; }
      if (J.Bool(b, "clearKey", false)) key = "";
      if (isNew) { id = "d" + U.Token(4).Replace("-", "x").Replace("_", "y"); d["order"] = DateTime.Now.Ticks.ToString("D19"); }
      d["platform"] = platform; d["name"] = name; d["url"] = url.Trim(); d["enabled"] = b.ContainsKey("enabled") ? J.Bool(b, "enabled", true) : J.Bool(d, "enabled", true); d["key"] = key;
      all[id] = d; Put(all); Cancel(id); Relay.Refresh(id); var s = StateOf(id); s.State = "idle"; s.Detail = ""; s.Attempt = 0;
      Log.Info("stream", "multistream destination " + (isNew ? "added" : "saved") + ": " + name + " (" + Redact(url) + ", key " + (key.Length > 0 ? "saved" : "not set") + ")");
      return View(id, d); }

    public static bool Delete(string id) {
      var all = All(); if (!all.ContainsKey(id)) return false; var name = J.Str((Dictionary<string, object>)all[id], "name", ""); all.Remove(id); Put(all); Cancel(id); Relay.Drop(id); lock (L) States.Remove(id);
      Log.Info("stream", "multistream destination removed: " + name); return true; }

    static void Cancel(string id) { CancellationTokenSource c; lock (L) { St s; if (!States.TryGetValue(id, out s)) return; c = s.Cts; s.Cts = null; } if (c != null) { try { c.Cancel(); } catch { } } }

    // ----- reachability check: TCP -> TLS (rtmps) -> RTMP handshake; no key, no video -----
    static int Ms(string name, int def) { return int.Parse(Ep.Get(name, def.ToString())); }
    public static string Probe(string url, CancellationToken ct) {
      Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return "invalid address";
      int port = u.Port > 0 ? u.Port : (u.Scheme == "rtmps" ? 443 : 1935); int timeout = Ms("multistream_timeout_ms", 8000);
      using (var tcp = new TcpClient()) {
        try {
          var conn = tcp.ConnectAsync(u.Host, port);
          if (!conn.Wait(timeout, ct)) return "no answer from " + u.Host + ":" + port + " within " + timeout / 1000 + " s";
        } catch (OperationCanceledException) { return "cancelled"; }
        catch (Exception e) { var x = e is AggregateException && e.InnerException != null ? e.InnerException : e; return "could not connect to " + u.Host + ":" + port + " (" + x.Message + ")"; }
        tcp.ReceiveTimeout = timeout; tcp.SendTimeout = timeout;
        using (ct.Register(() => { try { tcp.Close(); } catch { } })) {
          try {
            System.IO.Stream st = tcp.GetStream();
            if (u.Scheme == "rtmps") { var ssl = new SslStream(st); ssl.AuthenticateAsClient(u.Host, null, System.Security.Authentication.SslProtocols.Tls12, true); st = ssl; }
            var c1 = new byte[1537]; c1[0] = 3; var rnd = new byte[1528]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(rnd); Array.Copy(rnd, 0, c1, 9, 1528);   // C0, then C1: time(4)=0, zero(4), random(1528)
            st.Write(c1, 0, c1.Length); st.Flush();
            var resp = new byte[1 + 1536 + 1536]; int got = 0;
            while (got < resp.Length) { int n = st.Read(resp, got, resp.Length - got); if (n <= 0) return "the server closed the connection during the RTMP handshake"; got += n; }
            if (resp[0] != 3) return "the server does not speak RTMP (version byte " + resp[0] + ")";
            for (int i = 0; i < 1528; i++) if (resp[1 + 1536 + 8 + i] != rnd[i]) return "the server's RTMP handshake reply was wrong";
            return null;
          } catch (System.Security.Authentication.AuthenticationException e) { return "secure connection failed (" + e.Message + ")"; }
          catch (Exception e) { if (ct.IsCancellationRequested) return "cancelled"; return (e is System.IO.IOException && e.InnerException is SocketException ? e.InnerException.Message : e.Message) + " (while talking to " + u.Host + ")"; } } } }

    // up to 3 attempts, waiting 1x, 2x ... between them (multistream_retry_ms); the state is visible while it runs; delete / edit / a new check cancels it
    public static bool StartCheck(string id, out string err) {
      err = null; var all = All(); if (!all.ContainsKey(id)) { err = "unknown destination"; return false; }
      var d = (Dictionary<string, object>)all[id]; var url = J.Str(d, "url", ""); var verr = ValidateUrl(url); if (verr != null) { err = verr; return false; }
      Cancel(id); var cts = new CancellationTokenSource(); var s = StateOf(id); lock (L) { s.Cts = cts; s.State = "checking"; s.Detail = "connecting to " + Redact(url); s.Attempt = 0; }
      int attempts = Ms("multistream_attempts", 3), gap = Ms("multistream_retry_ms", 2000); var name = J.Str(d, "name", id);
      Task.Run(() => {
        string last = null;
        for (int a = 1; a <= attempts && !cts.IsCancellationRequested; a++) {
          lock (L) { s.Attempt = a; s.Detail = (a > 1 ? "retrying (" + a + "/" + attempts + "): " : "") + "connecting to " + Redact(url); }
          last = Probe(url, cts.Token); if (last == null || cts.IsCancellationRequested) break;
          if (a < attempts) { try { Task.Delay(gap * a, cts.Token).Wait(); } catch { break; } } }
        lock (L) { if (s.Cts != cts) return; s.Cts = null; s.At = DateTime.Now;
          if (last == null) { s.State = "ok"; s.Detail = "server reachable (RTMP handshake done; stream key not used)"; } else { s.State = "failed"; s.Detail = last; } }
        if (last == null) Log.Info("stream", "multistream check ok: " + name + " (" + Redact(url) + ")"); else Log.Warn("stream", "multistream check failed: " + name + " (" + Redact(url) + "): " + last); });
      return true; }

    public static bool Api(Ctx ctx, string path, string m) {
      if (!path.StartsWith("/api/multistream")) return false;
      try {
        if (path == "/api/multistream") { Http.Json(ctx, Snapshot()); return true; }
        var b = m == "POST" ? Http.BodyJson(ctx) : new Dictionary<string, object>(); string err;
        if (path == "/api/multistream/save" && m == "POST") { var v = Save(b, out err); Http.Json(ctx, err == null ? 200 : 400, err == null ? J.D("ok", true, "destination", v) : J.D("ok", false, "error", err)); return true; }
        if (path == "/api/multistream/delete" && m == "POST") { var ok = Delete(J.Str(b, "id", "")); Http.Json(ctx, ok ? 200 : 404, J.D("ok", ok, "error", ok ? "" : "unknown destination")); return true; }
        if (path == "/api/multistream/relay") { string rerr = null; if (m == "POST") { var act = J.Str(b, "action", ""); if (act == "start") rerr = Relay.Start(); else if (act == "stop") Relay.Stop(); else rerr = "action must be start or stop"; }
          Http.Json(ctx, rerr == null ? 200 : 400, rerr == null ? J.D("ok", true, "relay", Relay.Status()) : J.D("ok", false, "error", rerr)); return true; }
        if (path == "/api/multistream/check" && m == "POST") { var ok = StartCheck(J.Str(b, "id", ""), out err); Http.Json(ctx, ok ? 202 : 400, J.D("ok", ok, "error", err ?? "")); return true; } }
      catch (Exception e) { Log.Err("stream", "multistream: " + e.Message); Http.Json(ctx, 500, J.D("ok", false, "error", "multistream: " + U.Plain(e))); return true; }
      return false; }
  }
}
