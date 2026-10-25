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
          foreach (var oo in (J.Get(pd, "outputs") as ArrayList) ?? new ArrayList()) { var o = oo as Dictionary<string, object>; if (o == null) continue;
            var e = Entry("Aitum Multistream", J.Str(o, "name", ""), J.Str(o, "stream_server", ""), J.Str(o, "stream_key", "").Length > 0, J.Int(o, "audio_track", 0) + 1);
            e["platform"] = PlatformOf(J.Str(o, "name", ""), J.Str(o, "stream_server", "")); e["ownEncoder"] = o.ContainsKey("video_encoder") && J.Str(o, "video_encoder", "").Length > 0; l.Add(e); } } }
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

    public static async Task<Dictionary<string, object>> Status() {
      Dictionary<string, object> main; var conf = Configured(out main); var live = new List<Dictionary<string, object>>(); string liveErr = null;
      try { live = await LiveOutputs(); } catch (Exception e) { liveErr = U.Plain(e); }
      var plats = new Dictionary<string, object>();
      foreach (var p in Plats) {
        var mine = conf.Where(e => J.Str(e, "platform", "") == p).ToList(); if (main != null && J.Str(main, "platform", "") == p) mine.Insert(0, main);
        var issues = new List<string>();
        if (mine.Count > 1) issues.Add(p + " is set up " + mine.Count + " times (" + string.Join(", ", mine.Select(e => J.Str(e, "via", ""))) + "): start only one, or the platform gets two streams");
        foreach (var e in mine) { if (!J.Bool(e, "serverSet", false)) issues.Add(J.Str(e, "via", "") + " \"" + J.Str(e, "name", "") + "\" has no server address"); if (!J.Bool(e, "keySet", false)) issues.Add(J.Str(e, "via", "") + " \"" + J.Str(e, "name", "") + "\" has no stream key"); }
        // live: OBS's main output when it goes to this platform; plugin outputs are matched by name (which plugin output belongs to which platform
        // is only known when its OBS output name says so)
        var act = live.Where(o => (main != null && J.Str(main, "platform", "") == p && Regex.IsMatch(J.Str(o, "output", ""), "^(adv_stream|simple_stream)$")) || J.Str(o, "output", "").ToLowerInvariant().Contains(p)).ToList();
        string state = !Obs.Link.Ready ? "DISCONNECTED" : act.Any(o => J.Bool(o, "reconnecting", false)) ? "CONNECTING" : act.Count > 0 ? "STREAMING" : mine.Count == 0 ? "NOT CONFIGURED" : issues.Count > 0 ? "ERROR" : "READY";
        plats[p] = J.D("state", state, "outputs", mine, "issues", issues, "live", act, "liveNote", act.Count > 0 ? "OBS is sending (local output active); the platform's confirmation is not checked" : ""); }
      var unmatched = live.Where(o => !Plats.Any(p => J.Str(o, "output", "").ToLowerInvariant().Contains(p)) && !Regex.IsMatch(J.Str(o, "output", ""), "^(adv_stream|simple_stream)$")).Select(o => J.Str(o, "output", "")).ToList();
      return J.D("ok", true, "obs", Obs.Link.Ready ? "connected" : Obs.Link.State, "platforms", plats, "main", main, "mainIssue", main != null && J.Bool(main, "ixcRelay", false) ? "OBS's Start Streaming button sends to " + J.Str(main, "server", "") + " (IXC's old multistream relay), which IXC no longer runs: set Settings > Stream to your main platform" : null,
        "otherActiveOutputs", unmatched, "liveError", liveErr, "note", "Settings are read from OBS's saved files, which OBS rewrites when you change them in its windows. Check All reads them again."); }

    public static bool Api(Ctx ctx, string path, string m) {
      if (path != "/api/outputs") return false;
      try { Http.Json(ctx, Status().Result); } catch (Exception e) { Http.Json(ctx, 500, J.D("ok", false, "error", U.Plain(e))); }
      return true; }
  }
}
