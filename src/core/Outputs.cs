// IXC Core - three-platform stream status (Twitch, YouTube, Kick) for streams sent by OBS itself and its multistream plugins
// (Aitum Multistream, obs-multi-rtmp, OBS's own Settings > Stream). Read-only: it reads those plugins' saved settings and OBS's live
// output list, never changes them and never starts or stops anything. Stream keys are never read into a result: only "set" or "not set".
// States: NOT CONFIGURED (no output for that platform), ERROR (an output that cannot work, e.g. no server, or the platform set up twice),
// READY (set up, not running), CONNECTING (OBS reports it reconnecting), STREAMING (OBS reports the output active and sending data:
// "local output active", which is NOT a confirmation from the platform that it receives the stream), DISCONNECTED (OBS not connected).
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IXC {
  public static class Outputs {
    public static readonly string[] Plats = { "twitch", "youtube", "kick" };
    static string ObsDir { get { return Ep.Get("obs_config_dir", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio")); } }

    // which platform a stream goes to: by its name first ("KICK (track 2)"), then by the server address
    public static string PlatformOf(string name, string server) {
      var n = (name ?? "").ToLowerInvariant(); foreach (var p in Plats) if (n.Contains(p)) return p;
      var s = (server ?? "").ToLowerInvariant();
      if (s.Contains("youtube.com")) return "youtube"; if (s.Contains("kick.com") || s.Contains("global-contribute.live-video.net")) return "kick";
      if (s.Contains("twitch.tv") || s.Contains("contribute.live-video.net")) return "twitch"; return null; }
    static string Host(string server) { Uri u; return Uri.TryCreate(server ?? "", UriKind.Absolute, out u) ? u.Scheme + "://" + u.Host + (u.IsDefaultPort ? "" : ":" + u.Port) : ""; }
    static Dictionary<string, object> Entry(string via, string name, string server, bool keySet, int track) {
      return J.D("via", via, "name", name, "server", Host(server), "serverSet", (server ?? "").Trim().Length > 0, "keySet", keySet, "audioTrack", track); }

    static string ActiveProfileDir() {
      try { foreach (var f in new[] { "user.ini", "global.ini" }) { var p = Path.Combine(ObsDir, f); if (!File.Exists(p)) continue;
          var m = Regex.Match(File.ReadAllText(p), @"^ProfileDir=(.+)$", RegexOptions.Multiline); if (m.Success) return m.Groups[1].Value.Trim(); } } catch { }
      return "Untitled"; }
    static string ActiveProfileName() {
      try { foreach (var f in new[] { "user.ini", "global.ini" }) { var p = Path.Combine(ObsDir, f); if (!File.Exists(p)) continue;
          var m = Regex.Match(File.ReadAllText(p), @"^Profile=(.+)$", RegexOptions.Multiline); if (m.Success) return m.Groups[1].Value.Trim(); } } catch { }
      return ActiveProfileDir(); }
    static Dictionary<string, object> ReadJson(string path) { try { return File.Exists(path) ? J.Parse(File.ReadAllText(path)) : null; } catch { return null; } }

    // every configured stream destination, from the saved settings (what OBS loaded when it started)
    public static List<Dictionary<string, object>> Configured(out Dictionary<string, object> main) {
      var l = new List<Dictionary<string, object>>(); var prof = Path.Combine(ObsDir, "basic", "profiles", ActiveProfileDir());
      main = null; var svc = ReadJson(Path.Combine(prof, "service.json"));
      if (svc != null) { var st = J.Obj(svc, "settings") ?? new Dictionary<string, object>(); var server = J.Str(st, "server", ""); var service = J.Str(st, "service", "");
        main = Entry("OBS Settings > Stream", service.Length > 0 ? service : "Custom", server, J.Str(st, "key", "").Length > 0, 1);
        main["platform"] = PlatformOf(service, server); main["ixcRelay"] = Regex.IsMatch(server, @"^rtmps?://(127\.0\.0\.1|localhost):\d+/", RegexOptions.IgnoreCase); }
      var aitum = ReadJson(Path.Combine(ObsDir, "plugin_config", "aitum-multistream", "config.json"));
      if (aitum != null) { var pn = ActiveProfileName();
        foreach (var po in (J.Get(aitum, "profiles") as ArrayList) ?? new ArrayList()) { var pd = po as Dictionary<string, object>; if (pd == null || J.Str(pd, "name", "") != pn) continue;
          int ai = 0; foreach (var oo in (J.Get(pd, "outputs") as ArrayList) ?? new ArrayList()) { var o = oo as Dictionary<string, object>; if (o == null) continue; var idx = ai++;
            var e = Entry("Aitum Multistream", J.Str(o, "name", ""), J.Str(o, "stream_server", ""), J.Str(o, "stream_key", "").Length > 0, J.Int(o, "audio_track", 0) + 1);
            e["platform"] = PlatformOf(J.Str(o, "name", ""), J.Str(o, "stream_server", "")); e["ownEncoder"] = o.ContainsKey("video_encoder") && J.Str(o, "video_encoder", "").Length > 0; e["index"] = idx; l.Add(e); } } }
      var mr = ReadJson(Path.Combine(prof, "obs-multi-rtmp.json"));
      if (mr != null) { var mix = new Dictionary<int, int>();
        foreach (var ao in (J.Get(mr, "audio_configs") as ArrayList) ?? new ArrayList()) { var a = ao as Dictionary<string, object>; if (a != null) mix[J.Int(a, "id", 0)] = J.Int(a, "mixerId", 0) + 1; }
        foreach (var to in (J.Get(mr, "targets") as ArrayList) ?? new ArrayList()) { var t = to as Dictionary<string, object>; if (t == null) continue; var sp = J.Obj(t, "service-param") ?? new Dictionary<string, object>(); int tr;
          var e = Entry("obs-multi-rtmp", J.Str(t, "name", ""), J.Str(sp, "server", ""), J.Str(sp, "key", "").Length > 0, mix.TryGetValue(J.Int(t, "audio-config", 0), out tr) ? tr : 1);
          e["platform"] = PlatformOf(J.Str(t, "name", ""), J.Str(sp, "server", "")); l.Add(e); } }
      return l; }

    static async Task<List<Dictionary<string, object>>> LiveOutputs() {
      var l = new List<Dictionary<string, object>>(); if (!Obs.Link.Ready) return l;
      var r = await Obs.Link.Call("GetOutputList", null);
      foreach (var oo in (J.Get(r, "outputs") as ArrayList) ?? new ArrayList()) { var o = oo as Dictionary<string, object>; if (o == null || !J.Bool(o, "outputActive", false)) continue;
        var name = J.Str(o, "outputName", ""); if (Regex.IsMatch(name, "file_output|virtualcam|replay|record", RegexOptions.IgnoreCase)) continue;
        Dictionary<string, object> s = null; try { s = await Obs.Link.Call("GetOutputStatus", J.D("outputName", name)); } catch { }
        l.Add(J.D("output", name, "reconnecting", s != null && J.Bool(s, "outputReconnecting", false), "bytes", s == null ? 0 : J.Long(s, "outputBytes", 0), "congestion", s == null ? 0.0 : J.Num(s, "outputCongestion", 0))); }
      return l; }

    // an OBS output belongs to platform p when its name says so, or it is Aitum's output for p (aitum_multi_output_<n> or containing the output's name)
    static bool Mine(string obsName, string p, List<Dictionary<string, object>> mine) {
      var n = obsName.ToLowerInvariant(); if (n.Contains(p)) return true;
      return mine.Any(e => J.Str(e, "via", "") == "Aitum Multistream" && (n == "aitum_multi_output_" + J.Int(e, "index", -1) || (J.Str(e, "name", "").Length > 0 && n.Contains(J.Str(e, "name", "").ToLowerInvariant())))); }
    public static async Task<Dictionary<string, object>> Status() {
      Dictionary<string, object> main; var conf = Configured(out main); var live = new List<Dictionary<string, object>>(); string liveErr = null;
      try { live = await LiveOutputs(); } catch (Exception e) { liveErr = U.Plain(e); }
      var plats = new Dictionary<string, object>();
      foreach (var p in Plats) {
        var mine = conf.Where(e => J.Str(e, "platform", "") == p).ToList(); if (main != null && J.Str(main, "platform", "") == p) mine.Insert(0, main);
        // only a complete output (server + key) can send: incomplete leftovers are no duplicate, and only an error when nothing complete exists
        var issues = new List<string>(); var notes = new List<string>();
        Func<Dictionary<string, object>, bool> usable = e => J.Bool(e, "serverSet", false) && J.Bool(e, "keySet", false);
        var ready = mine.Where(usable).ToList();
        if (ready.Count > 1) issues.Add(p + " is set up " + ready.Count + " times (" + string.Join(", ", ready.Select(e => J.Str(e, "via", ""))) + "): start only one, or the platform gets two streams");
        foreach (var e in mine.Where(e => !usable(e))) {
          var why = J.Str(e, "via", "") + " \"" + J.Str(e, "name", "") + "\" has no " + (!J.Bool(e, "serverSet", false) && !J.Bool(e, "keySet", false) ? "server address or stream key" : !J.Bool(e, "serverSet", false) ? "server address" : "stream key");
          if (ready.Count > 0) notes.Add(why + " (unused - it cannot send)"); else issues.Add(why); }
        // live: OBS's main output when it goes to this platform; plugin outputs are matched by name (which plugin output belongs to which platform
        // is only known when its OBS output name says so)
        var act = live.Where(o => (main != null && J.Str(main, "platform", "") == p && Regex.IsMatch(J.Str(o, "output", ""), "^(adv_stream|simple_stream)$")) || Mine(J.Str(o, "output", ""), p, mine)).ToList();
        string state = !Obs.Link.Ready ? "DISCONNECTED" : act.Any(o => J.Bool(o, "reconnecting", false)) ? "CONNECTING" : act.Count > 0 ? "STREAMING" : mine.Count == 0 ? "NOT CONFIGURED" : issues.Count > 0 ? "ERROR" : "READY";
        plats[p] = J.D("state", state, "outputs", mine, "issues", issues, "notes", notes, "live", act, "liveNote", act.Count > 0 ? "OBS is sending (local output active); the platform's confirmation is not checked" : ""); }
      var unmatched = live.Where(o => !Plats.Any(p => Mine(J.Str(o, "output", ""), p, conf.Where(e => J.Str(e, "platform", "") == p).ToList())) && !Regex.IsMatch(J.Str(o, "output", ""), "^(adv_stream|simple_stream)$")).Select(o => J.Str(o, "output", "")).ToList();
      return J.D("ok", true, "obs", Obs.Link.Ready ? "connected" : Obs.Link.State, "platforms", plats, "main", main, "mainIssue", main != null && J.Bool(main, "ixcRelay", false) ? "OBS's Start Streaming button sends to " + J.Str(main, "server", "") + " (IXC's old multistream relay), which IXC no longer runs: set Settings > Stream to your main platform" : null,
        "otherActiveOutputs", unmatched, "liveError", liveErr, "note", "Settings are read from OBS's saved files, which OBS rewrites when you change them in its windows. Check All reads them again."); }

    // ---------- Start / Stop through Aitum Multistream (obs-websocket vendor "aitum-multistream", requests start_output / stop_output) ----------
    // Off until AITUM-SINK-TEST.ps1 proved, against a local fake ingest, which field Aitum reads the output name from; that field is saved as
    // streams.aitumParam. Only complete (READY) Aitum outputs are started, never one that is already running or starting, never NOT CONFIGURED.
    static readonly HashSet<string> busy = new HashSet<string>();
    public static string ControlsOff { get { return Settings.Str("streams.aitumParam").Length == 0 ? "Start/Stop from IXC is off until the safe Aitum test has passed (D:\\OBS\\New OBS File\\AITUM-SINK-TEST.ps1). Use the Aitum Multistream dock in OBS." : null; } }
    static Dictionary<string, object> AitumOutput(Dictionary<string, object> plat) {
      var l = J.Get(plat, "outputs") as List<Dictionary<string, object>>; if (l == null) return null;
      return l.FirstOrDefault(e => J.Str(e, "via", "") == "Aitum Multistream" && J.Bool(e, "serverSet", false) && J.Bool(e, "keySet", false)); }
    public static async Task<Dictionary<string, object>> Control(string which, bool start, bool confirm) {
      var off = ControlsOff; if (off != null) return J.D("ok", false, "error", off);
      if (!Obs.Link.Ready) return J.D("ok", false, "error", "OBS isn't connected");
      var targets = which == "all" ? Plats.ToList() : Plats.Contains(which) ? new List<string> { which } : null; if (targets == null) return J.D("ok", false, "error", "unknown platform " + which);
      var st = await Status(); var plats = J.Obj(st, "platforms"); var results = new Dictionary<string, object>();
      if (!start && !confirm && targets.Any(p => { var s = J.Str(J.Obj(plats, p), "state", ""); return s == "STREAMING" || s == "CONNECTING"; }))
        return J.D("ok", false, "needConfirm", true, "error", "This stops a stream that is live. Confirm to stop it.");
      foreach (var p in targets) {
        var pl = J.Obj(plats, p); var state = J.Str(pl, "state", ""); var o = AitumOutput(pl); string res;
        if (o == null) res = state == "NOT CONFIGURED" ? "skipped: not configured" : "skipped: no complete Aitum output for " + p;
        else if (start && state != "READY") res = state == "STREAMING" || state == "CONNECTING" ? "skipped: already running" : "skipped: " + state + " (" + string.Join("; ", (J.Get(pl, "issues") as List<string>) ?? new List<string>()) + ")";
        else if (!start && state != "STREAMING" && state != "CONNECTING") res = "skipped: not running";
        else {
          lock (busy) { if (busy.Contains(p)) { results[p] = J.D("ok", false, "result", "skipped: already " + (start ? "starting" : "stopping")); continue; } busy.Add(p); }
          try {
            var r = await Obs.Link.Call("CallVendorRequest", J.D("vendorName", "aitum-multistream", "requestType", start ? "start_output" : "stop_output", "requestData", J.D(Settings.Str("streams.aitumParam"), J.Str(o, "name", ""))));
            res = (start ? "start" : "stop") + " sent to Aitum (\"" + J.Str(o, "name", "") + "\")"; Log.Info("obs", "streams: " + res + " for " + p); }
          catch (Exception e) { res = "failed: " + U.Plain(e); Log.Warn("obs", "streams: " + p + " " + res); }
          finally { lock (busy) busy.Remove(p); } }
        results[p] = J.D("ok", !res.StartsWith("failed"), "result", res); }
      return J.D("ok", results.Values.Cast<Dictionary<string, object>>().All(x => J.Bool(x, "ok", false)), "results", results, "note", "Started means Aitum accepted the request; watch the state change to STREAMING (OBS sending) here and the platform's own dashboard."); }

    public static bool Api(Ctx ctx, string path, string m) {
      if (path == "/api/outputs") {
        try { var s = Status().Result; s["controls"] = J.D("enabled", ControlsOff == null, "reason", ControlsOff); Http.Json(ctx, s); } catch (Exception e) { Http.Json(ctx, 500, J.D("ok", false, "error", U.Plain(e))); }
        return true; }
      if ((path == "/api/outputs/start" || path == "/api/outputs/stop") && m == "POST") {
        var b = Http.BodyJson(ctx); var r = Control(J.Str(b, "platform", "all"), path.EndsWith("start"), J.Bool(b, "confirm", false)).Result;
        Http.Json(ctx, J.Bool(r, "ok", false) || J.Bool(r, "needConfirm", false) || r.ContainsKey("results") ? 200 : 400, r); return true; }
      return false; }
  }
}
