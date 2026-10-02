// IXC Core - live numbers for overlays and the dashboard: viewer counts per platform + total, Now Playing, and the
// one-glance status (OBS, platforms, music, TTS, phone). Old numbers are never shown as current: a count that wasn't
// refreshed recently is reported as "stale" with no number.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace IXC {
  public static class Viewers {
    static Timer pubT;
    static double MaxAge { get { return Math.Max(120, Settings.Int("viewers.refreshSec") * 3); } }
    public static Dictionary<string, object> View(ChatSource s) {
      var v = s.Viewers; bool fresh = v.At != DateTime.MinValue && (DateTime.Now - v.At).TotalSeconds <= MaxAge;
      string state = !s.Configured ? "off" : v.At == DateTime.MinValue ? (v.Note.Length > 0 ? "unavailable" : "waiting") : !fresh ? "stale" : v.Live == false ? "offline" : v.Count != null ? "live" : "unavailable";
      if (fresh && v.Note.Length > 0 && (DateTime.Now - v.At).TotalSeconds > Settings.Int("viewers.refreshSec") * 1.5) state = "stale";
      return J.D("platform", s.Id, "label", s.Label, "count", state == "live" ? v.Count : null, "live", fresh ? v.Live : null, "state", state, "note", v.Note, "source", v.Source,
        "updatedSec", v.At == DateTime.MinValue ? -1 : (int)(DateTime.Now - v.At).TotalSeconds, "startedAt", v.StartedAt == null ? null : v.StartedAt.Value.ToString("o")); }
    public static int? Total() { int? t = null; foreach (var s in Platforms.All) { var v = View(s); if ((string)v["state"] == "live") t = (t ?? 0) + (int)v["count"]; } return t; }
    public static Dictionary<string, object> Snapshot() {
      var p = new Dictionary<string, object>(); bool partial = false;
      foreach (var s in Platforms.All) { var v = View(s); p[s.Id] = v; var st = (string)v["state"]; if (s.Configured && (st == "stale" || st == "unavailable" || st == "waiting")) partial = true; }
      return J.D("type", "viewers", "platforms", p, "total", Total(), "partial", partial, "at", DateTime.Now.ToString("HH:mm:ss"), "uptime", Commands.Uptime()); }
    public static void Changed() { if (pubT == null) pubT = new Timer(_ => { try { var s = Snapshot(); Hub.Publish("viewers", s); Output.Write("viewers.txt", Text(s, null)); Output.Write("viewers-total.txt", s["total"] == null ? "-" : ((int)s["total"]).ToString("N0", CultureInfo.InvariantCulture)); } catch (Exception e) { Log.Err("overlay", "viewer output: " + e.Message); } }); pubT.Change(250, Timeout.Infinite); }
    public static string Text(Dictionary<string, object> s, string only) {
      var parts = new List<string>(); var pl = (Dictionary<string, object>)s["platforms"];
      foreach (var kv in pl) { var v = (Dictionary<string, object>)kv.Value; if ((string)v["state"] == "off") continue; if (only != null && only != kv.Key) continue;
        var st = (string)v["state"]; parts.Add(v["label"] + ": " + (st == "live" ? ((int)v["count"]).ToString("N0", CultureInfo.InvariantCulture) : st == "offline" ? "offline" : "-")); }
      if (only == "total") return s["total"] == null ? "-" : ((int)s["total"]).ToString("N0", CultureInfo.InvariantCulture);
      if (only == null) parts.Add("Total: " + (s["total"] == null ? "-" : ((int)s["total"]).ToString("N0", CultureInfo.InvariantCulture)));
      return string.Join(" | ", parts); }
    public static bool Api(Ctx ctx, string path, System.Collections.Specialized.NameValueCollection q) {
      if (path == "/api/viewers") { Http.Json(ctx, Snapshot()); return true; }
      if (path == "/api/viewers.txt") { Http.Send(ctx, 200, Text(Snapshot(), q["p"]), "text/plain; charset=utf-8"); return true; }
      return false; }
  }

  // text files for OBS "Text (GDI+) > Read from file" and other tools: %LOCALAPPDATA%\IXC-OBS\output\
  public static class Output {
    static readonly Dictionary<string, string> last = new Dictionary<string, string>();
    public static string Dir { get { return Path.Combine(Cfg.DataDir, "output"); } }
    public static void Write(string name, string text) {
      lock (last) { string old; if (last.TryGetValue(name, out old) && old == text) return; last[name] = text; }
      try { Directory.CreateDirectory(Dir); var f = Path.Combine(Dir, name); File.WriteAllText(f + ".tmp", text, new UTF8Encoding(false)); if (File.Exists(f)) File.Replace(f + ".tmp", f, null); else File.Move(f + ".tmp", f); }
      catch (Exception e) { Log.Debug("overlay", "output " + name + ": " + e.Message); } }
  }

  public static class NowPlaying {
    public static Dictionary<string, object> Data() {
      var cur = Music.Current(); var st = J.Obj(Music.State(), "s");
      if (cur == null) return J.D("playing", false, "title", "", "artist", "", "text", "", "pos", 0, "dur", 0);
      var kind = J.Str(cur, "kind", ""); var id = J.Str(cur, "id", "");
      return J.D("playing", J.Bool(st, "playing", false), "title", J.Str(st, "title", ""), "artist", J.Str(cur, "artist", ""), "by", J.Str(cur, "by", ""), "platform", J.Str(cur, "platform", ""),
        "pos", J.Num(st, "pos", 0), "dur", J.Num(st, "dur", 0), "kind", kind, "art", kind == "yt" && id.Length == 11 ? "https://i.ytimg.com/vi/" + id + "/mqdefault.jpg" : "", "text", Text(cur), "at", U.Now()); }
    static string Text(Dictionary<string, object> cur) {
      var st = J.Obj(Music.State(), "s"); var title = J.Str(st, "title", ""); var artist = J.Str(cur, "artist", "");
      var t = Settings.Str("nowPlaying.textFormat").Replace("{title}", title).Replace("{artist}", artist).Replace("{by}", J.Str(cur, "by", ""));
      t = t.Trim(); t = System.Text.RegularExpressions.Regex.Replace(t, "\\s*[-|]\\s*$", "").Trim(); return t; }
    public static void Update() { if (!Settings.Bool("nowPlaying.writeFile")) return; var d = Data(); Output.Write("nowplaying.txt", (bool)d["playing"] ? (string)d["text"] : ""); }
    public static bool Api(Ctx ctx, string path) {
      if (path == "/api/nowplaying") { Http.Json(ctx, Data()); return true; }
      if (path == "/api/nowplaying.txt") { var d = Data(); Http.Send(ctx, 200, (bool)d["playing"] ? (string)d["text"] : "", "text/plain; charset=utf-8"); return true; }
      return false; }
  }

  // one small message with everything the dashboard's home screen needs
  public static class Status {
    static Timer t; static string lastJson = "";
    public static void Init() { t = new Timer(_ => { try { var j = J.Ser(Msg()); if (j != lastJson && Hub.AnyTopic("status")) { lastJson = j; Hub.PublishRaw("status", j); } } catch { } }, null, 3000, 3000); }
    public static Dictionary<string, object> Msg() {
      var issues = Health.QuickIssues();
      string overall = issues.Any(i => (string)i["level"] == "error") ? "error" : issues.Any(i => (string)i["level"] == "warn") ? "warning" : "healthy";
      var d = J.D("type", "status", "version", Program.Version, "overall", overall, "issues", issues, "firstRun", !Settings.Bool("general.firstRunDone"),
        "obs", J.D("state", Obs.Link.State, "detail", Obs.Link.Detail, "connected", Obs.Link.Ready, "setup", Obs.SetupOk, "setupNote", Obs.LastSetup),
        "phone", Remote.Summary(), "update", Updates.Summary());
      if (Music.Enabled) { var cur = Music.Current(); var st = J.Obj(Music.State(), "s"); d["music"] = J.D("playing", J.Bool(st, "playing", false), "title", cur == null ? "" : Music.Describe(cur), "queue", Music.Queue().Count, "player", Hub.Count("player") > 0, "ducked", J.Bool(st, "ducked", false), "note", J.Str(st, "note", "")); }
      if (Chat.Enabled) { d["chat"] = Platforms.StatusAll(); d["tts"] = J.D("on", Tts.On, "paused", Tts.Paused, "speaking", Tts.Speaking, "source", Hub.Count("tts") > 0); d["viewers"] = Viewers.Snapshot(); }
      return d; }
  }
}
