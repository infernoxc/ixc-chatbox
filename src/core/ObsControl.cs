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

    public static bool Api(Ctx ctx, string path, string m) {
      if (!path.StartsWith("/api/obs/")) return false;
      try {
        if (path == "/api/obs/state") { Http.Json(ctx, State()); return true; }
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
