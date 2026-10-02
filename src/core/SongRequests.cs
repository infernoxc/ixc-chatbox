// IXC Core - song requests from chat: !sr <song name or link>. Permission, cooldowns, queue limits, duplicates, banned songs /
// artists and a maximum length are all checked before anything is added. Requests show who asked in the queue.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IXC {
  public static class SongRequests {
    static readonly Dictionary<string, DateTime> last = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase); static DateTime lastAny = DateTime.MinValue, lastOffNote = DateTime.MinValue;
    public static int Accepted, Refused;
    static void No(ChatMsg m, string why) { Refused++; Log.Info("music", "song request from " + m.Name + " refused: " + why); if (Settings.Bool("songRequests.replyInChat")) Chat.Reply(m, "@" + m.Name + " " + why); Hub.Publish("music.state", J.D("type", "music.request", "ok", false, "user", m.Name, "platform", m.Platform, "error", why)); }
    public static List<Dictionary<string, object>> Pending() { var q = Music.Queue(); int cur = Music.CurrentIndex; return q.Where((x, i) => i > cur && J.Str(x, "by", "").Length > 0).ToList(); }
    public static void Handle(ChatMsg m, string args) {
      if (!Settings.Bool("songRequests.enabled")) { if ((DateTime.Now - lastOffNote).TotalSeconds > 120) { lastOffNote = DateTime.Now; No(m, "song requests are turned off right now"); } return; }
      if (!Settings.List("songRequests.platforms").Contains(m.Platform)) return;
      var why = Perm.Check(m, Settings.Str("songRequests.permission")); if (why != null) { No(m, "song requests are for " + why.Replace(" only", "")); return; }
      if (args.Length == 0) { No(m, "type a song name or a YouTube link after " + Settings.List("songRequests.commands").FirstOrDefault()); return; }
      bool staff = m.Mod || m.Broadcaster; var now = DateTime.Now;
      if (!staff) {
        int days = Settings.Int("songRequests.minAccountAgeDays");
        if (days > 0 && m.Platform == "twitch") { var created = Platforms.Twitch.AccountCreated(m.UserId); if (created != null && (DateTime.UtcNow - created.Value).TotalDays < days) { No(m, "your account must be at least " + days + " days old to request songs"); return; } }
        lock (last) {
          DateTime t; int cd = Settings.Int("songRequests.cooldownSec"), ucd = Settings.Int("songRequests.userCooldownSec");
          if (cd > 0 && (now - lastAny).TotalSeconds < cd) { No(m, "please wait " + (int)Math.Ceiling(cd - (now - lastAny).TotalSeconds) + " s before the next request"); return; }
          if (ucd > 0 && last.TryGetValue(m.Key, out t) && (now - t).TotalSeconds < ucd) { No(m, "you can request again in " + (int)Math.Ceiling(ucd - (now - t).TotalSeconds) + " s"); return; } }
        var pend = Pending();
        if (pend.Count >= Settings.Int("songRequests.maxQueue")) { No(m, "the request queue is full - try again later"); return; }
        if (pend.Count(x => J.Str(x, "by", "") == m.Name && J.Str(x, "platform", "") == m.Platform) >= Settings.Int("songRequests.maxPerUser")) { No(m, "you already have " + Settings.Int("songRequests.maxPerUser") + " songs waiting"); return; } }
      bool isLink = Regex.IsMatch(args, "^(https?://|www\\.)|youtu\\.?be|spotify[.:]", RegexOptions.IgnoreCase) || Regex.IsMatch(args, "^[\\w-]{11}$") && Regex.IsMatch(args, "[0-9_-]");
      if (isLink && !Settings.Bool("songRequests.allowLinks")) { No(m, "links aren't allowed - type the song name instead"); return; }
      if (!isLink && !Settings.Bool("songRequests.allowSearch")) { No(m, "please paste a YouTube link"); return; }
      // a video link that is also part of a playlist: request just that video
      if (isLink && Regex.IsMatch(args, "[?&]v=[\\w-]{11}|youtu\\.be/[\\w-]{11}") && args.Contains("list=")) args = Regex.Replace(args, "[?&]list=[\\w-]+", "");
      lock (last) { last[m.Key] = now; lastAny = now; }
      Task.Run(() => { try { Process(m, args, staff); } catch (Exception e) { Log.Err("music", "song request: " + e.Message); No(m, "couldn't add that song right now"); } }); }
    static void Process(ChatMsg m, string args, bool staff) {
      var r = Music.Resolve(args);
      if (!J.Bool(r, "ok", false)) { No(m, Short(J.Str(r, "error", "couldn't find that song"))); return; }
      if (r.ContainsKey("items") || J.Str(r, "kind", "") == "youtube-playlist") { No(m, "please request one song, not a playlist"); return; }
      var item = J.Obj(r, "item"); var title = J.Str(r, "title", J.Str(item, "title", "")); var artist = J.Str(item, "artist", J.Str(r, "author", ""));
      if (J.Str(item, "kind", "") == "yt") {
        var info = Music.VideoInfo(J.Str(item, "id", ""));
        if (J.Bool(info, "ok", false)) {
          if (J.Bool(info, "live", false)) { No(m, "live streams can't be requested"); return; }
          if (!J.Bool(info, "embeddable", true) || (J.Str(info, "status", "OK") != "OK" && J.Str(info, "status", "") != "")) { No(m, "that video can't be played outside YouTube"); return; }
          if (J.Int(info, "durationSec", 0) > 0) item["durationSec"] = J.Int(info, "durationSec", 0); if (artist.Length == 0) artist = J.Str(info, "author", ""); } }
      int max = Settings.Int("songRequests.maxDurationSec"), len = J.Int(item, "durationSec", 0);
      if (!staff && max > 0 && len > max) { No(m, "that song is too long (" + Fmt(len) + ", the limit is " + Fmt(max) + ")"); return; }
      var hay = (title + " " + artist + " " + J.Str(item, "id", "")).ToLowerInvariant();
      if (!staff && Settings.List("songRequests.bannedSongs").Any(b => b.Length > 1 && hay.Contains(b.ToLowerInvariant()))) { No(m, "that song can't be requested"); return; }
      if (!staff && Settings.List("songRequests.bannedArtists").Any(b => b.Length > 1 && (artist + " " + title).ToLowerInvariant().Contains(b.ToLowerInvariant()))) { No(m, "that artist can't be requested"); return; }
      var key = J.Str(item, "id", J.Str(item, "local", ""));
      var q = Music.Queue(); int cur = Music.CurrentIndex;
      if (q.Where((x, i) => i >= cur).Any(x => J.Str(x, "id", J.Str(x, "local", "?")) == key)) { No(m, "\"" + U.Trunc(title, 60) + "\" is already in the queue"); return; }
      item["by"] = m.Name; item["platform"] = m.Platform; if (artist.Length > 0 && J.Str(item, "artist", "").Length == 0 && J.Str(item, "kind", "") != "local") item["artist"] = artist;
      var at = Music.AddItem(item, "add"); int ahead = Math.Max(0, at - Math.Max(0, Music.CurrentIndex));
      Accepted++; Log.Info("music", "song request from " + m.Platform + " " + m.Name + ": " + title);
      Hub.Publish("music.state", J.D("type", "music.request", "ok", true, "user", m.Name, "platform", m.Platform, "title", title));
      if (Settings.Bool("songRequests.replyInChat")) Chat.Reply(m, "@" + m.Name + " added \"" + U.Trunc(title, 80) + "\"" + (ahead > 0 ? " (#" + ahead + " in the queue)" : " - playing now")); }
    static string Short(string e) { e = Regex.Replace(e ?? "", "\\s+", " "); return U.Trunc(char.ToLowerInvariant(e.Length > 0 ? e[0] : ' ') + (e.Length > 1 ? e.Substring(1) : ""), 140); }
    static string Fmt(int s) { return (s / 60) + ":" + (s % 60).ToString("00"); }
    public static string RemoveLast(ChatMsg m) {
      var mine = Pending().LastOrDefault(x => J.Str(x, "by", "") == m.Name && J.Str(x, "platform", "") == m.Platform);
      if (mine == null) return "@" + m.Name + " you have no song waiting";
      Music.Command("remove", J.D("uid", J.Str(mine, "uid", ""))); return "@" + m.Name + " removed \"" + U.Trunc(J.Str(mine, "title", "your song"), 80) + "\""; }
  }
}
