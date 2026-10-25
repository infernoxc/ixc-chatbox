// IXC Core - timed messages ("Follow me everywhere ..." every 15 minutes), the native replacement for Streamer.bot timers.
// One scheduler, independent of the platform connections: a reconnect never sends a message again, and the last send time is
// saved, so restarting IXC doesn't either. Never two timer messages closer than timers.gapSec, never a burst after a pause.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public static class Timers {
    static Timer tick; static readonly object L = new object();
    static Dictionary<string, object> state = new Dictionary<string, object>();   // id -> { last: ms since epoch, lines: chat count at last send }
    static long chatLines; static DateTime lastAny = DateTime.MinValue; static volatile bool busy;
    static readonly Dictionary<string, string> why = new Dictionary<string, string>();
    static string StateFile { get { return Path.Combine(Cfg.DataDir, "timers-state.json"); } }
    static int TickMs { get { int t; return int.TryParse(Ep.Get("timers_tick_ms", "5000"), out t) ? Math.Max(200, t) : 5000; } }
    static int MinuteMs { get { int t; return int.TryParse(Ep.Get("timers_minute_ms", "60000"), out t) ? Math.Max(100, t) : 60000; } }

    public static void Init() {
      try { if (File.Exists(StateFile)) state = J.Parse(File.ReadAllText(StateFile)) ?? new Dictionary<string, object>(); } catch (Exception e) { Log.Warn("chat", "timers: couldn't read the last send times (" + e.Message + ") - starting fresh"); }
      tick = new Timer(_ => Tick(), null, TickMs, TickMs); }
    // real chat from viewers (not old history, not IXC's own messages) counts towards "at least N chat messages in between"
    public static void OnChat(ChatMsg m) { if (m.Kind == "chat" && !m.Old && !m.Me) Interlocked.Increment(ref chatLines); }

    public static List<Dictionary<string, object>> List() {
      var v = Cfg.Get("timers.list") as IEnumerable; if (v == null || v is string) return new List<Dictionary<string, object>>();
      string err; return ValidateList(v, out err) as List<Dictionary<string, object>> ?? new List<Dictionary<string, object>>(); }
    public static object ValidateList(object v, out string err) {
      err = null; var src = v as IEnumerable; if (src == null || v is string || v is Dictionary<string, object>) { err = "timers must be a list"; return null; }
      var outp = new List<Dictionary<string, object>>(); var ids = new HashSet<string>();
      foreach (var o in src) { var d = o as Dictionary<string, object>; if (d == null) continue; if (outp.Count >= 50) { err = "at most 50 timers"; return null; }
        var msg = Regex.Replace(J.Str(d, "message", "").Trim(), "[\\r\\n]+", " "); if (msg.Length == 0) { err = "a timer needs a message"; return null; } if (msg.Length > 450) { err = "a timer message can be at most 450 characters"; return null; }
        var name = J.Str(d, "name", "").Trim(); if (name.Length == 0) name = msg.Length > 30 ? msg.Substring(0, 30) + "…" : msg; if (name.Length > 60) name = name.Substring(0, 60);
        var id = J.Str(d, "id", ""); if (!Regex.IsMatch(id, "^[\\w-]{1,20}$") || !ids.Add(id)) { id = U.Token(6); ids.Add(id); }
        outp.Add(J.D("id", id, "name", name, "message", msg, "intervalMin", Math.Max(1, Math.Min(240, J.Int(d, "intervalMin", 15))),
          "minLines", Math.Max(0, Math.Min(500, J.Int(d, "minLines", 5))), "liveOnly", J.Bool(d, "liveOnly", true), "enabled", J.Bool(d, "enabled", true),
          "platforms", J.List(d, "platforms").Where(x => Settings.PlatformIds.Contains(x) && x != "rumble").Distinct().ToList())); }
      return outp; }

    // where a timer goes now: its platforms (all when empty) that IXC can send to; live only = only platforms that are live right now
    static List<string> Targets(Dictionary<string, object> t, out string reason) {
      reason = null; var want = J.List(t, "platforms"); var cand = Platforms.All.Where(s => s.Configured && s.Id != "rumble" && (want.Count == 0 || want.Contains(s.Id))).ToList();
      if (cand.Count == 0) { reason = want.Count == 0 ? "no platform is set up" : "its platforms aren't set up"; return new List<string>(); }
      if (J.Bool(t, "liveOnly", true)) { cand = cand.Where(s => s.Viewers.Live == true && (DateTime.Now - s.Viewers.At).TotalSeconds < 300).ToList(); if (cand.Count == 0) { reason = "waiting for you to go live"; return new List<string>(); } }
      var ok = cand.Where(s => s.CanSend || s.ViaSb).Select(s => s.Id).ToList();
      if (ok.Count == 0) reason = "IXC can't post on " + string.Join(", ", cand.Select(s => s.Label)) + " yet - start Streamer.bot with that platform connected in it";
      return ok; }
    static long Last(string id) { lock (L) { var d = J.Obj(state, id); return d == null ? 0 : J.Long(d, "last", 0); } }
    static long LinesAt(string id) { lock (L) { var d = J.Obj(state, id); return d == null ? 0 : J.Long(d, "lines", 0); } }
    static void Mark(string id) { lock (L) { state[id] = J.D("last", U.Now(), "lines", Interlocked.Read(ref chatLines)); try { File.WriteAllText(StateFile + ".tmp", J.Ser(state), new UTF8Encoding(false)); if (File.Exists(StateFile)) File.Replace(StateFile + ".tmp", StateFile, null); else File.Move(StateFile + ".tmp", StateFile); } catch (Exception e) { Log.Debug("chat", "timers state: " + e.Message); } } }

    static void Tick() {
      if (busy) return; busy = true;
      try {
        if (!Settings.Bool("timers.enabled")) { lock (why) why.Clear(); return; }
        var list = List(); var gapMs = Settings.Int("timers.gapSec") * 1000L * MinuteMs / 60000; var now = U.Now();
        // a timer that was just created or switched on waits one full interval first (like Streamer.bot), it doesn't fire at once
        foreach (var t in list) { var id = J.Str(t, "id", ""); if (Last(id) == 0) Mark(id); }
        Dictionary<string, object> due = null; List<string> dueTargets = null;
        foreach (var t in list.OrderBy(x => Last(J.Str(x, "id", "")))) {
          var id = J.Str(t, "id", ""); string reason = null;
          if (!J.Bool(t, "enabled", true)) reason = "paused";
          else {
            var left = Last(id) + J.Int(t, "intervalMin", 15) * (long)MinuteMs - now; var lines = Interlocked.Read(ref chatLines) - LinesAt(id); var needLines = J.Int(t, "minLines", 0);
            List<string> targets; string tr; targets = Targets(t, out tr);
            if (left > 0) reason = "next in " + Math.Max(1, (int)Math.Ceiling(left / (double)MinuteMs)) + " min";
            else if (tr != null) reason = tr;
            else if (lines < needLines) reason = "waiting for " + (needLines - lines) + " more chat message" + (needLines - lines == 1 ? "" : "s");
            else if ((DateTime.Now - lastAny).TotalMilliseconds < gapMs) reason = "waiting a moment after the last timer";
            else if (due == null) { due = t; dueTargets = targets; reason = "sending"; }
            else reason = "next after " + J.Str(due, "name", ""); }
          lock (why) why[id] = reason; }
        if (due != null) Fire(due, dueTargets, false); }
      catch (Exception e) { Log.Err("chat", "timers: " + e.Message); }
      finally { busy = false; } }

    static void Fire(Dictionary<string, object> t, List<string> targets, bool manual) {
      var id = J.Str(t, "id", ""); Mark(id); lastAny = DateTime.Now;   // marked before sending: a slow or failing send is never repeated
      var text = Commands.Fill(J.Str(t, "message", ""), null, "", "timer:" + id);
      var res = new List<string>();
      foreach (var p in targets) { string err; try { err = Platforms.Send(p, text).Result; } catch (Exception e) { err = U.Plain(e); } res.Add(err == null ? p : p + " (failed: " + err + ")"); }
      Log.Info("chat", "timer \"" + J.Str(t, "name", "") + "\"" + (manual ? " (sent by hand)" : "") + " -> " + (res.Count == 0 ? "nowhere" : string.Join(", ", res)));
      Hub.Publish("timers", J.D("type", "timers", "timers", View())); }

    // "Send now" from the Timers page (ignores the interval and live only, still only where IXC can post)
    public static Dictionary<string, object> SendNow(string id) {
      var t = List().FirstOrDefault(x => J.Str(x, "id", "") == id); if (t == null) return J.D("ok", false, "error", "no such timer");
      var copy = J.Clone(t); copy["liveOnly"] = false; string reason; var targets = Targets(copy, out reason);
      if (targets.Count == 0) return J.D("ok", false, "error", reason);
      Fire(t, targets, true); return J.D("ok", true, "sentTo", targets); }

    public static List<Dictionary<string, object>> View() {
      var outp = new List<Dictionary<string, object>>();
      foreach (var t in List()) { var id = J.Str(t, "id", ""); var v = J.Clone(t); string r; lock (why) why.TryGetValue(id, out r);
        var last = Last(id); v["lastSent"] = last == 0 ? null : (object)last; v["status"] = !Settings.Bool("timers.enabled") ? "timers are off" : r ?? "starting"; outp.Add(v); }
      return outp; }
    public static bool Api(Ctx ctx, string path, string method) {
      if (path == "/api/timers") { Http.Json(ctx, J.D("enabled", Settings.Bool("timers.enabled"), "timers", View())); return true; }
      if (path == "/api/timers/send" && method == "POST") { var r = SendNow(J.Str(Http.BodyJson(ctx), "id", "")); Http.Json(ctx, (bool)r["ok"] ? 200 : 400, r); return true; }
      return false; }
  }
}
