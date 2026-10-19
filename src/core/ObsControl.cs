// IXC Core - OBS control over obs-websocket 5 (OBS 28+): streaming / recording state, scene switching, showing and hiding
// sources, OBS actions for chat commands (!brb -> scene "BRB"), and the connection form (address, password, Test connection).
// Every request returns OBS's own error text when OBS refuses it (unknown scene, request not supported by this OBS version).
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public static class ObsControl {
    public static volatile bool Streaming, Recording; public static string Scene = "";
    // state known only while connected; after a disconnect it's unknown (never "not streaming" by guess)
    public static Dictionary<string, object> State() {
      bool c = Obs.Link.Ready;
      return J.D("connected", c, "state", Obs.Link.State, "detail", Obs.Link.Detail, "version", Obs.Link.ObsVersion, "websocketVersion", Obs.Link.WsVersion,
        "streaming", c ? (object)Streaming : null, "recording", c ? (object)Recording : null, "scene", c ? Scene : "", "address", Settings.Str("obs.websocketUrl"),
        "passwordSet", (Secrets.Str("obs.password") ?? "").Length > 0); }
    public static void OnReady() {
      Task.Run(async () => {
        try { Streaming = J.Bool(await Obs.Link.Call("GetStreamStatus", null), "outputActive", false); } catch (Exception e) { Log.Debug("obs", "stream status: " + e.Message); }
        try { Recording = J.Bool(await Obs.Link.Call("GetRecordStatus", null), "outputActive", false); } catch (Exception e) { Log.Debug("obs", "record status: " + e.Message); }
        try { Scene = J.Str(await Obs.Link.Call("GetCurrentProgramScene", null), "currentProgramSceneName", J.Str(await Obs.Link.Call("GetCurrentProgramScene", null), "sceneName", "")); } catch { }
        Publish(); }); }
    public static void OnEvent(string type, Dictionary<string, object> d) {
      if (type == "StreamStateChanged") { var was = Streaming; Streaming = J.Bool(d, "outputActive", false); if (was != Streaming) Log.Info("obs", Streaming ? "OBS started streaming" : "OBS stopped streaming"); Publish(); }
      else if (type == "RecordStateChanged") { var was = Recording; Recording = J.Bool(d, "outputActive", false); if (was != Recording) Log.Info("obs", Recording ? "OBS started recording" : "OBS stopped recording"); Publish(); }
      else if (type == "CurrentProgramSceneChanged") { Scene = J.Str(d, "sceneName", ""); Publish(); } }
    static void Publish() { Hub.Publish("status", Status.Msg()); }

    // ---------- scenes and sources ----------
    public static async Task<Dictionary<string, object>> Scenes() {
      var r = await Obs.Link.Call("GetSceneList", null); var names = new List<string>();
      var arr = J.Get(r, "scenes") as ArrayList; if (arr != null) foreach (var o in arr) { var d = o as Dictionary<string, object>; if (d != null) names.Add(J.Str(d, "sceneName", "")); }
      names.Reverse();   // OBS lists the bottom scene first; show them in the order of OBS's Scenes panel
      return J.D("ok", true, "current", J.Str(r, "currentProgramSceneName", ""), "scenes", names); }
    public static async Task<Dictionary<string, object>> Sources(string scene) {
      var r = await Obs.Link.Call("GetSceneItemList", J.D("sceneName", scene)); var items = new List<object>();
      var arr = J.Get(r, "sceneItems") as ArrayList; if (arr != null) foreach (var o in arr) { var d = o as Dictionary<string, object>; if (d != null) items.Add(J.D("id", J.Int(d, "sceneItemId", 0), "name", J.Str(d, "sourceName", ""), "visible", J.Bool(d, "sceneItemEnabled", true))); }
      items.Reverse(); return J.D("ok", true, "scene", scene, "sources", items); }
    public static async Task<string> SwitchScene(string scene) {
      if (string.IsNullOrWhiteSpace(scene)) return "which scene?";
      try { await Obs.Link.Call("SetCurrentProgramScene", J.D("sceneName", scene)); Log.Info("obs", "switched to scene \"" + scene + "\""); return null; }
      catch (Exception e) { return "OBS: " + U.Plain(e); } }
    // visible: "show", "hide" or "toggle"
    public static async Task<string> SetSource(string scene, string source, string visible) {
      if (string.IsNullOrWhiteSpace(scene)) scene = Scene; if (string.IsNullOrWhiteSpace(source)) return "which source?";
      try {
        var id = J.Int(await Obs.Link.Call("GetSceneItemId", J.D("sceneName", scene, "sourceName", source)), "sceneItemId", 0);
        bool on = visible == "show" || (visible == "toggle" && !J.Bool(await Obs.Link.Call("GetSceneItemEnabled", J.D("sceneName", scene, "sceneItemId", id)), "sceneItemEnabled", true));
        await Obs.Link.Call("SetSceneItemEnabled", J.D("sceneName", scene, "sceneItemId", id, "sceneItemEnabled", on));
        Log.Info("obs", (on ? "showed" : "hid") + " \"" + source + "\" in \"" + scene + "\""); return null; }
      catch (Exception e) { return "OBS: " + U.Plain(e); } }

    // saves OBS's replay buffer (Settings > Output > Replay Buffer, started); null when OBS saved it
    public static string SaveReplay() {
      if (!Obs.Link.Ready) return "OBS isn't connected";
      try { if (!J.Bool(Obs.Link.Call("GetReplayBufferStatus", null).Result, "outputActive", false)) return "OBS's replay buffer isn't running";
        Obs.Link.Call("SaveReplayBuffer", null).Wait(); Log.Info("obs", "saved the replay buffer"); return null; }
      catch (Exception e) { return "OBS: " + U.Plain(e); } }
    // ---------- OBS action of a chat command: { type: scene|show|hide|toggle, scene, source } ----------
    public static Dictionary<string, object> ValidateAction(object v, out string err) {
      err = null; var d = v as Dictionary<string, object>; if (d == null) return null;
      var type = J.Str(d, "type", ""); if (type.Length == 0 || type == "none") return null;
      if (!new[] { "scene", "show", "hide", "toggle" }.Contains(type)) { err = "unknown OBS action " + type; return null; }
      var scene = J.Str(d, "scene", "").Trim(); var source = J.Str(d, "source", "").Trim();
      if (scene.Length > 200 || source.Length > 200) { err = "scene and source names can be at most 200 characters"; return null; }
      if (type == "scene" && scene.Length == 0) { err = "pick the scene to switch to"; return null; }
      if (type != "scene" && source.Length == 0) { err = "pick the source to show or hide"; return null; }
      return J.D("type", type, "scene", scene, "source", source); }
    // runs the action; returns null when done, otherwise why not (said in chat as the answer)
    public static string Run(Dictionary<string, object> a) {
      if (a == null) return null; if (!Obs.Link.Ready) return "OBS isn't connected";
      var type = J.Str(a, "type", "");
      return type == "scene" ? SwitchScene(J.Str(a, "scene", "")).Result : SetSource(J.Str(a, "scene", ""), J.Str(a, "source", ""), type).Result; }

    // ---------- connection form ----------
    public static Dictionary<string, object> SetConnection(Dictionary<string, object> b) {
      var url = (J.Str(b, "address", "auto") ?? "").Trim(); if (url.Length == 0) url = "auto";
      if (url != "auto") {
        if (Regex.IsMatch(url, "^[\\w.-]+:\\d{1,5}$")) url = "ws://" + url;   // "127.0.0.1:4455" is fine too
        Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "ws" && u.Scheme != "wss") || u.Port < 1) return J.D("ok", false, "error", "Use an address like 127.0.0.1:4455 (OBS > Tools > WebSocket Server Settings)");
        url = u.Scheme + "://" + u.Host + ":" + u.Port + "/"; }
      var err = Settings.Apply(J.D("obs.websocketUrl", url)); if (err != null) return J.D("ok", false, "error", err);
      if (b.ContainsKey("password")) { var pw = J.Str(b, "password", ""); Secrets.Set("obs.password", pw.Length == 0 ? null : pw); }
      var t0 = DateTime.Now; var f0 = Obs.Link.Failures; Obs.Link.Restart();
      // Test connection: wait a few seconds for the result of this attempt (not an "auth" left over from the last one)
      Func<bool> refused = () => Obs.Link.State == "auth" && (Obs.Link.Since >= t0 || Obs.Link.Failures > f0);
      var until = DateTime.Now.AddSeconds(Math.Max(1, Math.Min(15, J.Int(b, "waitSec", 6))));
      Thread.Sleep(300);
      while (DateTime.Now < until && !Obs.Link.Ready && !refused()) Thread.Sleep(200);
      var st = State(); st["ok"] = Obs.Link.Ready;
      if (!Obs.Link.Ready) st["error"] = refused() ? "OBS refused the password - copy it from OBS > Tools > WebSocket Server Settings > Show Connect Info"
        : Obs.Link.Detail.Length > 0 ? Obs.Link.Detail : "OBS didn't answer at " + (url == "auto" ? "the address from OBS's settings" : url) + " - is OBS open with its WebSocket server on?";
      return st; }

    // ---------- Stage 0: read-only report of what THIS OBS can do (requests, outputs, video settings, status, Aitum vendors) ----------
    // Only look-up requests are sent. Nothing that returns a stream key or a password is ever called (GetStreamServiceSettings, GetOutputSettings,
    // profile parameters and so on are deliberately not used), and nothing here changes OBS.
    static async Task<Dictionary<string, object>> Try(string type, Dictionary<string, object> data) {
      try { return await Obs.Link.Call(type, data); } catch (Exception e) { return J.D("_error", U.Plain(e)); } }
    static Dictionary<string, object> Pick(Dictionary<string, object> d, params string[] keys) { var r = new Dictionary<string, object>(); foreach (var k in keys) if (d.ContainsKey(k)) r[k] = d[k]; if (d.ContainsKey("_error")) r["_error"] = d["_error"]; return r; }
    // "deep" adds more look-ups for a plugin load test: the canvases OBS knows (GetCanvasList) with their scene names, and the read-only "status" and
    // "get_scenes" requests of the Aitum Vertical Canvas vendor. Still no request that changes anything or carries a key (any field named like key/password/secret/token is dropped).
    static object Scrub(object o) {
      var d = o as Dictionary<string, object>; if (d != null) { var r = new Dictionary<string, object>(); foreach (var kv in d) { if (Regex.IsMatch(kv.Key, "key|password|secret|token", RegexOptions.IgnoreCase)) continue; r[kv.Key] = Scrub(kv.Value); } return r; }
      var a = o as ArrayList; if (a != null) { var l = new List<object>(); foreach (var x in a) l.Add(Scrub(x)); return l; } return o; }
    static async Task<Dictionary<string, object>> DeepLookups() {
      var canv = await Try("GetCanvasList", null); var list = new List<object>();
      foreach (var cv in J.Objs(canv, "canvases")) { var uuid = J.Str(cv, "canvasUuid", ""); var sc = await Try("GetSceneList", J.D("canvasUuid", uuid)); var names = new List<string>(); foreach (var s in J.Objs(sc, "scenes")) names.Add(J.Str(s, "sceneName", ""));
        list.Add(J.D("canvas", Scrub(cv), "scenes", sc.ContainsKey("_error") ? (object)J.D("_error", sc["_error"]) : names)); }
      var vv = new Dictionary<string, object>();
      foreach (var rt in new[] { "status", "get_scenes" }) { var r = await Try("CallVendorRequest", J.D("vendorName", "aitum-vertical-canvas", "requestType", rt)); vv[rt] = r.ContainsKey("_error") ? J.D("ok", false, "error", r["_error"]) : J.D("ok", true, "answer", Scrub(r.ContainsKey("responseData") ? r["responseData"] : r)); }
      return J.D("canvasList", canv.ContainsKey("_error") ? (object)J.D("_error", canv["_error"]) : list, "verticalVendor", vv); }
    public static async Task<Dictionary<string, object>> Capabilities(bool deep) { var r = await Capabilities(); if (deep && J.Bool(r, "ok", false)) r["deep"] = await DeepLookups(); return r; }
    public static async Task<Dictionary<string, object>> Capabilities() {
      if (!Obs.Link.Ready) return J.D("ok", false, "error", "OBS isn't connected (" + (Obs.Link.Detail.Length > 0 ? Obs.Link.Detail : Obs.Link.State) + ")");
      var v = await Try("GetVersion", null); var reqs = J.List(v, "availableRequests"); reqs.Sort(StringComparer.Ordinal);
      var interesting = new Regex("Output|Stream|Record|Video|Profile|Canvas|Vendor|Hotkey|SceneItemTransform|SceneItemEnabled|Filter|Replay|VirtualCam", RegexOptions.IgnoreCase);
      var outs = new List<object>(); var ol = await Try("GetOutputList", null);
      foreach (var o in J.Objs(ol, "outputs")) {
        var name = J.Str(o, "outputName", ""); var st = await Try("GetOutputStatus", J.D("outputName", name));
        outs.Add(J.D("name", name, "kind", J.Str(o, "outputKind", ""), "width", J.Int(o, "outputWidth", 0), "height", J.Int(o, "outputHeight", 0), "active", J.Bool(o, "outputActive", false), "flags", o.ContainsKey("outputFlags") ? o["outputFlags"] : null,
          "status", Pick(st, "outputActive", "outputReconnecting", "outputTimecode", "outputDuration", "outputCongestion", "outputBytes", "outputSkippedFrames", "outputTotalFrames"))); }
      var vendors = new Dictionary<string, object>();
      foreach (var vn in new[] { "aitum-multistream", "aitum-vertical-canvas" }) {
        var vr = await Try("CallVendorRequest", J.D("vendorName", vn, "requestType", "version"));
        vendors[vn] = vr.ContainsKey("_error") ? J.D("registered", false, "error", vr["_error"]) : J.D("registered", true, "answer", vr.ContainsKey("responseData") ? vr["responseData"] : vr); }
      var hk = await Try("GetHotkeyList", null); var hkNames = J.List(hk, "hotkeys");
      var prof = await Try("GetProfileList", null); var stats = await Try("GetStats", null);
      return J.D("ok", true, "readOnly", true, "generatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        "note", "Only look-up requests were sent. No request that returns a stream key or password was made, and nothing in OBS was changed.",
        "obs", Pick(v, "obsVersion", "obsWebSocketVersion", "rpcVersion", "platform", "platformDescription", "_error"),
        "requestCount", reqs.Count, "requests", reqs, "interestingRequests", reqs.Where(r => interesting.IsMatch(r)).ToList(),
        "hasCanvasRequests", reqs.Any(r => r.IndexOf("Canvas", StringComparison.OrdinalIgnoreCase) >= 0), "hasVendorRequest", reqs.Contains("CallVendorRequest"),
        "videoSettings", Pick(await Try("GetVideoSettings", null), "baseWidth", "baseHeight", "outputWidth", "outputHeight", "fpsNumerator", "fpsDenominator"),
        "streamStatus", Pick(await Try("GetStreamStatus", null), "outputActive", "outputReconnecting", "outputTimecode", "outputDuration", "outputCongestion", "outputBytes", "outputSkippedFrames", "outputTotalFrames"),
        "recordStatus", Pick(await Try("GetRecordStatus", null), "outputActive", "outputPaused", "outputTimecode", "outputBytes"),
        "profile", Pick(prof, "currentProfileName", "profiles"), "stats", Pick(stats, "cpuUsage", "memoryUsage", "activeFps", "averageFrameRenderTime", "renderSkippedFrames", "outputSkippedFrames"),
        "outputs", outs, "outputCount", outs.Count, "vendors", vendors,
        "hotkeys", J.D("count", hkNames.Count, "matching", hkNames.Where(n => Regex.IsMatch(n, "aitum|vertical|multistream|canvas", RegexOptions.IgnoreCase)).ToList())); }
    public static bool Api(Ctx ctx, string path, string m) {
      if (!path.StartsWith("/api/obs/")) return false;
      try {
        if (path == "/api/obs/state") { Http.Json(ctx, State()); return true; }
        if (path == "/api/obs/capabilities") { Http.Json(ctx, Capabilities().Result); return true; }
        if (path == "/api/obs/connection" && m == "POST") { var r = SetConnection(Http.BodyJson(ctx)); Http.Json(ctx, r); return true; }
        if (!Obs.Link.Ready && (path == "/api/obs/scenes" || path == "/api/obs/sources" || path == "/api/obs/scene" || path == "/api/obs/source")) { Http.Json(ctx, 409, J.D("ok", false, "error", "OBS isn't connected (" + Obs.Link.Detail + ")")); return true; }
        if (path == "/api/obs/scenes") { Http.Json(ctx, Scenes().Result); return true; }
        if (path == "/api/obs/sources") { Http.Json(ctx, Sources(ctx.Request.QueryString["scene"] ?? Scene).Result); return true; }
        if (path == "/api/obs/scene" && m == "POST") { var e = SwitchScene(J.Str(Http.BodyJson(ctx), "scene", "")).Result; Http.Json(ctx, e == null ? 200 : 400, J.D("ok", e == null, "error", e)); return true; }
        if (path == "/api/obs/source" && m == "POST") { var b = Http.BodyJson(ctx); var v = J.Str(b, "visible", "toggle"); if (v != "show" && v != "hide") v = "toggle";
          var e = SetSource(J.Str(b, "scene", ""), J.Str(b, "source", ""), v).Result; Http.Json(ctx, e == null ? 200 : 400, J.D("ok", e == null, "error", e)); return true; } }
      catch (Exception e) { Http.Json(ctx, 400, J.D("ok", false, "error", "OBS: " + U.Plain(e))); return true; }
      return false; }
  }
}
