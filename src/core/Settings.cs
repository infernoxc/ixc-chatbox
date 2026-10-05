// IXC Core - every user-facing setting in one table: type, default and limits. The dashboard, docks and phone change settings
// only through Settings.Apply, so a bad value can never reach config.json (and nobody ever has to edit that file by hand).
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace IXC {
  public class Spec { public string Key, Kind; public object Def; public int Min, Max; public string[] Options; public bool Advanced; }

  public static class Settings {
    public static readonly Dictionary<string, Spec> All = new Dictionary<string, Spec>();
    public static readonly string[] PlatformIds = { "twitch", "kick", "youtube", "rumble" };
    public static readonly string[] Roles = { "everyone", "follower", "subscriber", "vip", "moderator", "broadcaster" };
    public static event Action<string> Changed;   // fired with the key that changed (after it is stored)

    static void B(string k, bool d) { All[k] = new Spec { Key = k, Kind = "bool", Def = d }; }
    static void I(string k, int d, int min, int max) { All[k] = new Spec { Key = k, Kind = "int", Def = d, Min = min, Max = max }; }
    static void S(string k, string d, int max) { All[k] = new Spec { Key = k, Kind = "str", Def = d, Max = max }; }
    static void E(string k, string d, params string[] opts) { All[k] = new Spec { Key = k, Kind = "enum", Def = d, Options = opts }; }
    static void L(string k, int maxItems, params string[] d) { All[k] = new Spec { Key = k, Kind = "list", Def = d.ToList(), Max = maxItems }; }
    static void O(string k) { All[k] = new Spec { Key = k, Kind = "obj", Def = new Dictionary<string, object>() }; }
    static void Adv(string k) { All[k].Advanced = true; }

    static Settings() {
      // general
      B("general.firstRunDone", false); B("general.advanced", false); B("general.autoRestart", true); B("general.checkUpdates", true); B("general.autoUpdate", true); B("general.openDashboardOnStart", false);
      B("diagnostics.verboseLog", false); Adv("diagnostics.verboseLog");
      // platforms (built-in connections; Streamer.bot is optional)
      foreach (var p in PlatformIds) B("platforms." + p + ".enabled", false);
      S("platforms.twitch.channel", "", 60); S("platforms.kick.channel", "", 60); S("platforms.youtube.channel", "", 200);
      E("platforms.streamerbot.mode", "auto", "auto", "on", "off"); S("platforms.streamerbot.websocketUrl", "ws://127.0.0.1:8080/", 200); S("platforms.streamerbot.settingsPath", "auto", 400);
      Adv("platforms.streamerbot.websocketUrl"); Adv("platforms.streamerbot.settingsPath");
      // chat display + filters
      L("chat.dockPlatforms", 8, "twitch", "kick", "youtube", "rumble");
      B("chat.filters.enabled", true);
      E("chat.filters.minRole", "everyone", Roles);
      E("chat.filters.links", "tts", "off", "tts", "hide"); E("chat.filters.caps", "tts", "off", "tts", "hide"); I("chat.filters.capsPercent", 70, 30, 100);
      E("chat.filters.repeatChars", "tts", "off", "tts", "hide"); I("chat.filters.maxRepeat", 8, 3, 50);
      E("chat.filters.longMessage", "tts", "off", "tts", "hide"); I("chat.filters.maxLength", 300, 40, 2000);
      E("chat.filters.duplicate", "hide", "off", "tts", "hide"); I("chat.filters.duplicateSec", 30, 5, 600);
      E("chat.filters.copyPaste", "tts", "off", "tts", "hide");
      E("chat.filters.suspicious", "hide", "off", "tts", "hide");
      E("chat.filters.bannedWords", "hide", "off", "tts", "hide"); L("chat.filters.bannedWordList", 500);
      L("chat.filters.blacklist", 1000); L("chat.filters.whitelist", 1000);
      B("chat.filters.modsBypass", true); B("chat.filters.subsBypass", false);
      L("chat.hiddenCommands", 200); S("chat.extraCommandsFile", "", 400); Adv("chat.extraCommandsFile");
      // commands
      B("commands.enabled", true); All["commands.list"] = new Spec { Key = "commands.list", Kind = "commands", Def = new ArrayList() };
      // song requests
      B("songRequests.enabled", false); L("songRequests.commands", 10, "!sr", "!song");
      E("songRequests.permission", "everyone", Roles); I("songRequests.minAccountAgeDays", 0, 0, 3650);
      I("songRequests.cooldownSec", 0, 0, 3600); I("songRequests.userCooldownSec", 60, 0, 3600);
      I("songRequests.maxQueue", 20, 1, 200); I("songRequests.maxPerUser", 3, 1, 50); I("songRequests.maxDurationSec", 600, 0, 7200);
      L("songRequests.bannedSongs", 1000); L("songRequests.bannedArtists", 1000); L("songRequests.platforms", 8, "twitch", "kick", "youtube", "rumble");
      B("songRequests.allowLinks", true); B("songRequests.allowSearch", true); B("songRequests.replyInChat", true);
      // TTS
      B("tts.on", false); B("tts.paused", false); S("tts.voice", "in-male", 60); E("tts.readMode", "name", "all", "name", "tts");
      I("tts.speed", 0, -50, 100); I("tts.pitch", 0, -30, 30); I("tts.volume", 100, 0, 100);
      I("tts.maxChars", 200, 40, 500); I("tts.queueMax", 6, 1, 50); I("tts.staleSec", 60, 10, 600); I("tts.perMinute", 20, 1, 120); I("tts.userCooldownSec", 3, 0, 120);
      B("tts.ignoreOwn", true); B("tts.ignoreBots", true); B("tts.readEmotes", false); B("tts.readEmoji", false); B("tts.readLinks", false); B("tts.fallback", true);
      L("tts.neverSpeak", 300); L("tts.blockedWords", 300); L("tts.botNames", 300); L("tts.ownNames", 50);
      L("tts.platforms", 8, "twitch", "kick", "youtube", "rumble"); E("tts.minRole", "everyone", Roles); B("tts.modControls", true);
      All["tts.users"] = new Spec { Key = "tts.users", Kind = "voices", Def = new Dictionary<string, object>() };
      All["tts.roleVoices"] = new Spec { Key = "tts.roleVoices", Kind = "voices", Def = new Dictionary<string, object>() };
      All["tts.platformVoices"] = new Spec { Key = "tts.platformVoices", Kind = "voices", Def = new Dictionary<string, object>() };
      O("tts.voices"); Adv("tts.voices");
      // music
      I("music.volume", 40, 0, 100); B("music.shuffle", false); B("music.repeat", true); B("music.autoplay", true); B("music.autostart", false);
      E("music.routing.mode", "all", "all", "twitch", "kick", "youtube", "off"); S("music.routing.inputName", "IXC Music Player", 100);
      L("music.routing.tracks.twitch", 6, "1"); L("music.routing.tracks.kick", 6, "2"); L("music.routing.tracks.youtube", 6, "3");
      L("music.localFolders", 20);
      B("music.ducking.enabled", true); I("music.ducking.level", 25, 0, 100); I("music.ducking.fadeDownMs", 400, 0, 5000); I("music.ducking.fadeUpMs", 900, 0, 8000); I("music.ducking.minVolume", 3, 0, 100);
      S("music.youtubeApiKey", "", 100); Adv("music.youtubeApiKey"); S("music.spotify.clientId", "", 100); Adv("music.spotify.clientId");
      S("nowPlaying.textFormat", "{title} - {artist}", 200); B("nowPlaying.writeFile", true);
      // viewers
      I("viewers.refreshSec", 30, 15, 300);
      // overlays: each overlay keeps a flat set of style values (validated in Validate below)
      foreach (var o in new[] { "nowplaying", "queue", "chat", "viewers", "tts", "alerts", "status" }) O("overlays." + o);
      // phone
      B("remote.enabled", true); S("remote.relayUrl", "", 200); Adv("remote.relayUrl"); I("remote.deviceDays", 90, 1, 365);
      // OBS
      S("obs.websocketUrl", "auto", 200); Adv("obs.websocketUrl");
      I("helper.port", 8767, 1024, 65535); Adv("helper.port");
    }

    // ---------- read ----------
    public static object Value(string key) { var v = Cfg.Get(key); Spec s; if (All.TryGetValue(key, out s)) { var ok = Coerce(s, v); if (ok == null && s.Kind == "commands") return Commands.DefaultList(); return ok ?? Copy(s.Def); } return v; }
    static object Copy(object d) { var l = d as List<string>; if (l != null) return new List<string>(l); var dd = d as Dictionary<string, object>; if (dd != null) return J.Clone(dd); var al = d as ArrayList; if (al != null) return new ArrayList(al); return d; }
    public static bool Bool(string k) { var v = Value(k); return v is bool && (bool)v; }
    public static int Int(string k) { var v = Value(k); try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return 0; } }
    public static string Str(string k) { return Convert.ToString(Value(k), CultureInfo.InvariantCulture) ?? ""; }
    public static List<string> List(string k) { var v = Value(k) as List<string>; return v ?? new List<string>(); }
    public static Dictionary<string, object> Obj(string k) { return Value(k) as Dictionary<string, object> ?? new Dictionary<string, object>(); }
    public static bool Has(string k) { return Cfg.Get(k) != null; }

    // all values with defaults applied (what pages see) - never contains secrets, those live in Secrets
    public static Dictionary<string, object> Snapshot(bool includeAdvanced) {
      var d = new Dictionary<string, object>(); foreach (var s in All.Values) if (includeAdvanced || !s.Advanced) d[s.Key] = Value(s.Key); return d; }
    public static List<object> Schema() {
      return All.Values.Select(s => (object)J.D("key", s.Key, "kind", s.Kind, "def", s.Def, "min", s.Min, "max", s.Max, "options", s.Options, "advanced", s.Advanced)).ToList(); }

    // ---------- write ----------
    // returns null when everything was applied, otherwise the first problem (nothing is applied then)
    public static string Apply(Dictionary<string, object> patch) {
      if (patch == null || patch.Count == 0) return "nothing to change";
      var ok = new List<KeyValuePair<string, object>>();
      foreach (var kv in patch) {
        Spec s; if (!All.TryGetValue(kv.Key, out s)) return "unknown setting: " + kv.Key;
        if (kv.Value == null) { ok.Add(new KeyValuePair<string, object>(kv.Key, null)); continue; }   // null = back to the default
        string err; var v = Validate(s, kv.Value, out err); if (err != null) return err;
        ok.Add(new KeyValuePair<string, object>(kv.Key, v)); }
      foreach (var kv in ok) Cfg.Set(kv.Key, kv.Value);
      Cfg.SaveNow();   // written at once (atomic): a crash a moment later can't lose the change
      foreach (var kv in ok) { Log.Info("app", "setting " + kv.Key + " = " + (Regex.IsMatch(kv.Key, "key|secret|token|password", RegexOptions.IgnoreCase) ? (kv.Value == null ? "(default)" : "(hidden)") : Describe(kv.Value))); if (Changed != null) { foreach (Action<string> h in Changed.GetInvocationList()) { try { h(kv.Key); } catch (Exception e) { Log.Err("app", "after changing " + kv.Key + ": " + e.Message); } } } }
      return null; }
    public static string Set(string key, object value) { return Apply(J.D(key, value)); }
    static string Describe(object v) { if (v == null) return "(default)"; var l = v as IEnumerable; if (v is string || l == null) return U.Trunc(Convert.ToString(v, CultureInfo.InvariantCulture), 80); return "(" + l.Cast<object>().Count() + " items)"; }

    static object Coerce(Spec s, object v) { string err; if (v == null) return null; var r = Validate(s, v, out err); return err == null ? r : null; }
    public static object Validate(Spec s, object v, out string err) {
      err = null;
      switch (s.Kind) {
        case "bool": if (v is bool) return v; { bool b; if (v is string && bool.TryParse((string)v, out b)) return b; } err = Name(s) + " must be on or off"; return null;
        case "int": { double d; try { d = Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { err = Name(s) + " must be a number"; return null; } if (double.IsNaN(d)) { err = Name(s) + " must be a number"; return null; } return (int)Math.Max(s.Min, Math.Min(s.Max, Math.Round(d))); }
        case "str": { if (v is Dictionary<string, object> || v is ArrayList) { err = Name(s) + " must be text"; return null; } var t = Convert.ToString(v, CultureInfo.InvariantCulture).Trim(); if (t.Length > s.Max) t = t.Substring(0, s.Max); return Regex.Replace(t, "[\\x00-\\x1f]", ""); }
        case "enum": { var t = Convert.ToString(v, CultureInfo.InvariantCulture); if (s.Options.Contains(t)) return t; err = Name(s) + " must be one of: " + string.Join(", ", s.Options); return null; }
        case "list": {
          IEnumerable src = v as ArrayList; if (src == null) src = v as List<string>; if (src == null) { var t = v as string; if (t == null) { err = Name(s) + " must be a list"; return null; } src = t.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries); }
          return src.Cast<object>().Where(x => x != null && !(x is Dictionary<string, object>)).Select(x => Regex.Replace(Convert.ToString(x, CultureInfo.InvariantCulture).Trim(), "[\\x00-\\x1f]", ""))
            .Where(x => x.Length > 0 && x.Length <= 200).Distinct(StringComparer.OrdinalIgnoreCase).Take(s.Max).ToList(); }
        case "obj": { var d = v as Dictionary<string, object>; if (d == null) { err = Name(s) + " must be a group of settings"; return null; } return FlatStyle(d, out err); }
        case "voices": { var d = v as Dictionary<string, object>; if (d == null) { err = "voices must be a list"; return null; } return VoiceMap(d, out err); }
        case "commands": return Commands.ValidateList(v, out err);
      }
      err = "can't change " + s.Key; return null; }
    static string Name(Spec s) { return s.Key; }
    // overlay styles and custom voices: one level of simple values, short strings, nothing executable
    static Dictionary<string, object> FlatStyle(Dictionary<string, object> d, out string err) {
      err = null; var r = new Dictionary<string, object>();
      foreach (var kv in d.Take(80)) {
        if (!Regex.IsMatch(kv.Key, "^[A-Za-z][A-Za-z0-9_.-]{0,40}$")) { err = "bad name: " + kv.Key; return null; }
        var v = kv.Value;
        if (v is bool || v is int || v is long || v is double || v is decimal) r[kv.Key] = v;
        else if (v is string) { var t = (string)v; if (t.Length > 300) t = t.Substring(0, 300); if (Regex.IsMatch(t, "[<>]|javascript:|expression\\(|url\\(", RegexOptions.IgnoreCase)) { err = kv.Key + " contains characters that aren't allowed"; return null; } r[kv.Key] = t; }
        else if (v is Dictionary<string, object>) { var inner = FlatStyle((Dictionary<string, object>)v, out err); if (err != null) return null; r[kv.Key] = inner; }
        else if (v is ArrayList) { r[kv.Key] = ((ArrayList)v).Cast<object>().Where(x => x is string || x is int || x is bool).Take(50).Select(x => x is string ? U.Trunc((string)x, 100) : x).ToList(); }
        else if (v == null) continue;
        else { err = kv.Key + " has an unsupported value"; return null; } }
      return r; }
    // "who" -> { voice, speed, pitch, volume, enabled }
    public static Dictionary<string, object> VoiceMap(Dictionary<string, object> d, out string err) {
      err = null; var r = new Dictionary<string, object>();
      foreach (var kv in d.Take(2000)) {
        var key = kv.Key.Trim().ToLowerInvariant(); if (key.Length == 0 || key.Length > 80) { err = "bad name: " + kv.Key; return null; }
        var o = kv.Value as Dictionary<string, object>; if (o == null) { err = kv.Key + ": voice settings missing"; return null; }
        var e = new Dictionary<string, object>();
        var voice = J.Str(o, "voice", ""); if (voice.Length > 0) { if (!Tts.Voices().ContainsKey(voice)) { err = "unknown voice: " + voice; return null; } e["voice"] = voice; }
        if (o.ContainsKey("speed")) e["speed"] = Math.Max(-50, Math.Min(100, J.Int(o, "speed", 0)));
        if (o.ContainsKey("pitch")) e["pitch"] = Math.Max(-30, Math.Min(30, J.Int(o, "pitch", 0)));
        if (o.ContainsKey("volume")) e["volume"] = Math.Max(0, Math.Min(100, J.Int(o, "volume", 100)));
        e["enabled"] = J.Bool(o, "enabled", true);
        r[key] = e; }
      return r; }

    // ---------- upgrade older config.json files ----------
    public static void Migrate(Dictionary<string, object> d) {
      // v2 kept the Streamer.bot password and the Spotify secret in config.json: move them into the encrypted store
      var sbPw = J.Str(d, "streamerbot.password", ""); if (sbPw.Length > 0) { Secrets.Set("streamerbot.password", sbPw); }
      var spSec = J.Str(d, "music.spotify.clientSecret", ""); if (spSec.Length > 0) { Secrets.Set("spotify.clientSecret", spSec); }
      var obsPw = J.Str(d, "obs.password", ""); if (obsPw.Length > 0) { Secrets.Set("obs.password", obsPw); }
      var sb = J.Obj(d, "streamerbot");
      if (sb != null) { var url = J.Str(sb, "websocketUrl", ""); var path = J.Str(sb, "settingsPath", "");
        var pl = J.Obj(d, "platforms") ?? new Dictionary<string, object>(); d["platforms"] = pl; var ps = J.Obj(pl, "streamerbot") ?? new Dictionary<string, object>(); pl["streamerbot"] = ps;
        if (url.Length > 0 && !ps.ContainsKey("websocketUrl")) ps["websocketUrl"] = url; if (path.Length > 0 && !ps.ContainsKey("settingsPath")) ps["settingsPath"] = path;
        d.Remove("streamerbot"); }
      var sp = J.Obj(d, "music.spotify"); if (sp != null) sp.Remove("clientSecret");
      var obs = J.Obj(d, "obs"); if (obs != null) obs.Remove("password");
      var rem = J.Obj(d, "remote"); if (rem != null) foreach (var k in new[] { "port", "sessionHours", "idleMinutes", "allowDownload", "cloudflaredPath" }) rem.Remove(k);
      var cr = J.Obj(d, "chatRelay"); if (cr != null) { var chat = J.Obj(d, "chat") ?? new Dictionary<string, object>(); d["chat"] = chat;
        if (!chat.ContainsKey("hiddenCommands") && cr.ContainsKey("hiddenCommands")) chat["hiddenCommands"] = cr["hiddenCommands"]; d.Remove("chatRelay"); }
      d.Remove("_readme"); d["version"] = Program.Version;
      // anyone upgrading from v2 already finished setup with Streamer.bot: don't show the first-run wizard again
      if (J.Get(d, "general.firstRunDone") == null && (J.Get(d, "tts") != null || J.Get(d, "music") != null)) { var g = J.Obj(d, "general") ?? new Dictionary<string, object>(); d["general"] = g; g["firstRunDone"] = true; }
    }
  }
}
