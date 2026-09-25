// IXC Core - music: dock <-> player commands and state (pushed, not polled), YouTube search / "up next", link checking for
// YouTube + Spotify, and the audio-destination switch (which OBS audio tracks the music player source is sent to).
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IXC {
  public static class Music {
    public static readonly ObsLink Obs = new ObsLink();
    static readonly List<KeyValuePair<int, string>> Cmds = new List<KeyValuePair<int, string>>(); static int seq;   // v1 HTTP polling fallback
    static string state = "{}"; static Dictionary<string, object> stateD = new Dictionary<string, object>(); static DateTime stateAt = DateTime.MinValue;
    static string routeStatus = "not applied yet", routeActual = ""; static bool routeOk; static DateTime routeAt;
    public static int Searches, Resolves;

    public static void Init() {
      Obs.OnReady = () => { Diag.Ev("OBS connected (obs-websocket " + Obs.ObsVersion + ")"); ApplyRoute(Mode(), false).Wait(); };
      Obs.OnObsEvent = (type, d) => {
        if ((type == "InputAudioTracksChanged" || type == "InputCreated" || type == "InputNameChanged") && InputMatches(J.Str(d, "inputName", J.Str(d, "oldInputName", "")))) {
          if (type == "InputAudioTracksChanged") { var tr = J.Obj(d, "inputAudioTracks"); routeActual = TrackText(tr); var m = ModeFor(tr);
            if (m != Mode()) { routeStatus = "tracks were changed in OBS: now " + routeActual; routeOk = false; Diag.Ev("music tracks changed in OBS by hand: " + routeActual); } PublishRoute(); }
          else { var t = ApplyRoute(Mode(), false); } } };
      Obs.Start();
      obsTick = new System.Threading.Timer(_ => { if (Obs.Ready != lastObs) { lastObs = Obs.Ready; if (!lastObs) PublishRoute(); } }, null, 3000, 3000);   // docks learn when OBS closes
    }
    static System.Threading.Timer obsTick; static bool lastObs;

    // ---------- audio destination ----------
    static readonly string[] Platforms = { "twitch", "kick", "youtube" };
    public static string Mode() { return J.Str(Cfg.D, "music.routing.mode", "all"); }
    static string InputName() { return J.Str(Cfg.D, "music.routing.inputName", "IXC Music Player"); }
    static bool InputMatches(string n) { return string.Equals(n, InputName(), StringComparison.OrdinalIgnoreCase); }
    public static List<int> Tracks(string platform) { var t = J.List(Cfg.D, "music.routing.tracks." + platform).Select(s => { int n; return int.TryParse(s, out n) ? n : 0; }).Where(n => n >= 1 && n <= 6).ToList();
      if (t.Count == 0) t.Add(platform == "twitch" ? 1 : platform == "kick" ? 2 : 3); return t; }
    static string TrackText(Dictionary<string, object> tr) { if (tr == null) return "?"; var on = tr.Where(kv => kv.Value is bool && (bool)kv.Value).Select(kv => kv.Key).ToList(); return on.Count == 0 ? "no tracks" : "track " + string.Join("+", on); }
    static string ModeFor(Dictionary<string, object> tr) {
      if (tr == null) return "?"; Func<int, bool> on = n => J.Bool(tr, n.ToString(), false);
      var hit = Platforms.Where(p => Tracks(p).All(on)).ToList(); var managed = Platforms.SelectMany(Tracks).Distinct().ToList();
      if (hit.Count == 3) return "all"; if (hit.Count == 1 && managed.Count(on) == Tracks(hit[0]).Count) return hit[0]; if (managed.All(n => !on(n))) return "off"; return "custom"; }
    public static async Task<Dictionary<string, object>> ApplyRoute(string mode, bool save) {
      mode = (mode ?? "all").ToLowerInvariant(); if (mode != "all" && mode != "off" && !Platforms.Contains(mode)) return J.D("ok", false, "error", "mode must be all, twitch, kick, youtube or off");
      if (save) { Cfg.Set("music.routing.mode", mode); Cfg.Save(); }
      if (!Obs.Ready) { routeOk = false; routeStatus = "saved - will apply when OBS connects (" + Obs.Status + ")"; PublishRoute(); return J.D("ok", false, "pending", true, "mode", mode, "error", routeStatus); }
      // right after OBS opens its WebSocket it can still be loading the scene collection; a request in that window comes
      // back "OBS is not ready to perform the request" - not a real failure, just early, so retry briefly instead of
      // surfacing an error (this used to need a second manual click on the destination button to clear)
      for (int attempt = 1; attempt <= 8; attempt++) {
        var r = await ApplyRouteOnce(mode);
        if (!(bool)r["ok"] && (string)r["error"] != null && ((string)r["error"]).IndexOf("not ready", StringComparison.OrdinalIgnoreCase) >= 0 && attempt < 8) { await Task.Delay(500); continue; }
        return r; }
      return await ApplyRouteOnce(mode); }
    static async Task<Dictionary<string, object>> ApplyRouteOnce(string mode) {
      try {
        var want = new Dictionary<string, object>(); var managed = Platforms.SelectMany(Tracks).Distinct().ToList();
        var mine = mode == "all" ? managed : mode == "off" ? new List<int>() : Tracks(mode);
        foreach (var n in managed) want[n.ToString()] = mine.Contains(n);                              // tracks not used by any platform (recording etc.) are left alone
        await Obs.Call("SetInputAudioTracks", J.D("inputName", InputName(), "inputAudioTracks", want));
        var got = J.Obj(await Obs.Call("GetInputAudioTracks", J.D("inputName", InputName())), "inputAudioTracks");   // verify what OBS really has now
        routeActual = TrackText(got); routeOk = managed.All(n => J.Bool(got, n.ToString(), false) == mine.Contains(n)); routeAt = DateTime.Now;
        routeStatus = routeOk ? "applied and verified in OBS: " + routeActual : "OBS did not take the change: " + routeActual;
        Diag.Ev("music destination " + mode.ToUpperInvariant() + ": " + routeStatus); PublishRoute();
        return J.D("ok", routeOk, "mode", mode, "tracks", routeActual, "status", routeStatus);
      } catch (Exception e) {
        routeOk = false; routeStatus = e.Message.Contains("No source was found") || e.Message.Contains("not found") ? "OBS has no source named \"" + InputName() + "\" (set music.routing.inputName)" : "OBS error: " + e.Message;
        Diag.Err("music routing: " + routeStatus); PublishRoute(); return J.D("ok", false, "mode", mode, "error", routeStatus); } }
    static Dictionary<string, object> RouteInfo() { return J.D("type", "music.route", "mode", Mode(), "ok", routeOk && Obs.Ready, "status", Obs.Ready ? routeStatus : "saved - applies when OBS connects (" + Obs.Status + ")", "tracks", routeActual, "obs", Obs.Ready ? "connected" : Obs.Status, "input", InputName(),
      "map", J.D("twitch", Tracks("twitch"), "kick", Tracks("kick"), "youtube", Tracks("youtube"))); }
    static void PublishRoute() { Hub.Publish("music.state", RouteInfo()); }

    // ---------- commands + state ----------
    static void Command(string json) {
      lock (Cmds) { seq++; Cmds.Add(new KeyValuePair<int, string>(seq, json)); if (Cmds.Count > 200) Cmds.RemoveRange(0, 100); }
      Hub.PublishRaw("music.cmd", "{\"type\":\"music.cmd\",\"c\":" + json + "}"); }
    static void SetState(string json) {
      state = json; stateAt = DateTime.Now; stateD = J.Parse(json);
      Hub.PublishRaw("music.state", "{\"type\":\"music.state\",\"s\":" + json + "}"); }
    static bool InQueue(string id, string list) {
      var q = J.Get(stateD, "queue") as ArrayList; if (q == null) return false;
      foreach (var o in q) { var d = o as Dictionary<string, object>; if (d == null) continue; if (list != null && J.Str(d, "list", null) == list) return true; if (list == null && id != null && J.Str(d, "id", null) == id && J.Str(d, "list", null) == null) return true; }
      return false; }

    // ---------- YouTube search / related (public web pages, no API key: this can break when YouTube changes its pages) ----------
    const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0 Safari/537.36";
    static string Get(string url, int timeoutMs) { var r = (HttpWebRequest)WebRequest.Create(url); r.UserAgent = UA; r.Headers["Accept-Language"] = "en-US,en;q=0.9"; r.Timeout = timeoutMs; r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
      using (var resp = (HttpWebResponse)r.GetResponse()) using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd(); }
    static int Code(Exception e) { var we = e as WebException; if (we == null && e.InnerException is WebException) we = (WebException)e.InnerException; return we != null && we.Response is HttpWebResponse ? (int)((HttpWebResponse)we.Response).StatusCode : 0; }
    static readonly Dictionary<string, KeyValuePair<DateTime, object>> cache = new Dictionary<string, KeyValuePair<DateTime, object>>();
    static object Cached(string key, int sec, Func<object> f) { lock (cache) { KeyValuePair<DateTime, object> v; if (cache.TryGetValue(key, out v) && (DateTime.Now - v.Key).TotalSeconds < sec) return v.Value; }
      var r = f(); lock (cache) { cache[key] = new KeyValuePair<DateTime, object>(DateTime.Now, r); if (cache.Count > 300) cache.Clear(); } return r; }
    static string Un(string s) { try { return Regex.Unescape(s); } catch { return s; } }
    public static List<Dictionary<string, object>> Search(string q) {
      if (string.IsNullOrWhiteSpace(q)) return new List<Dictionary<string, object>>();
      return (List<Dictionary<string, object>>)Cached("s:" + q.ToLowerInvariant(), 300, () => { Searches++;
        var key = J.Str(Cfg.D, "music.youtubeApiKey", ""); if (key.Length > 0) { try { return ApiSearch(q, key); } catch (Exception e) { Diag.Err("YouTube Data API search failed, using the public page: " + e.Message); } }
        var h = Get("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(q) + "&sp=EgIQAQ%253D%253D", 15000);
        var outp = new List<Dictionary<string, object>>(); var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(h, "\"videoRenderer\":\\{\"videoId\":\"([\\w-]{11})\"(.{0,4000}?)\"title\":\\{\"runs\":\\[\\{\"text\":\"((?:[^\"\\\\]|\\\\.)*)\"")) {
          var id = m.Groups[1].Value; if (!seen.Add(id)) continue; var rest = h.Substring(m.Index, Math.Min(6000, h.Length - m.Index));
          var len = Regex.Match(rest, "\"lengthText\":\\{\"accessibility\":\\{\"accessibilityData\":\\{\"label\":\"[^\"]*\"\\}\\},\"simpleText\":\"([^\"]+)\"").Groups[1].Value;
          var ch = Regex.Match(rest, "\"ownerText\":\\{\"runs\":\\[\\{\"text\":\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value;
          outp.Add(J.D("id", id, "title", Un(m.Groups[3].Value), "length", len, "channel", Un(ch))); if (outp.Count >= 15) break; }
        return outp; }); }
    static List<Dictionary<string, object>> ApiSearch(string q, string key) {   // official YouTube Data API v3 (optional; 100 quota units per search, 10,000 per day free)
      var d = J.Parse(Get("https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&videoEmbeddable=true&maxResults=15&q=" + Uri.EscapeDataString(q) + "&key=" + Uri.EscapeDataString(key), 15000));
      var outp = new List<Dictionary<string, object>>(); var items = J.Get(d, "items") as ArrayList; if (items == null) return outp;
      foreach (var o in items) { var it = o as Dictionary<string, object>; var id = J.Str(it, "id.videoId", null); if (id == null) continue; outp.Add(J.D("id", id, "title", WebUtility.HtmlDecode(J.Str(it, "snippet.title", "")), "length", "", "channel", J.Str(it, "snippet.channelTitle", ""))); }
      return outp; }
    public static List<Dictionary<string, object>> Related(string id, string exclude) {
      if (!Regex.IsMatch(id ?? "", "^[\\w-]{11}$")) return new List<Dictionary<string, object>>();
      var all = (List<Dictionary<string, object>>)Cached("r:" + id, 900, () => {
        var h = Get("https://www.youtube.com/watch?v=" + id, 15000); var outp = new List<Dictionary<string, object>>(); int i = h.IndexOf("\"secondaryResults\""); if (i < 0) return outp;
        var sec = h.Substring(i, Math.Min(600000, h.Length - i)); var seen = new HashSet<string> { id };
        foreach (var chunk in sec.Split(new[] { "\"lockupViewModel\":{" }, StringSplitOptions.None).Skip(1)) {
          var cid = Regex.Match(chunk, "\"contentId\":\"([^\"]+)\"").Groups[1].Value; var ctype = Regex.Match(chunk, "\"contentType\":\"([^\"]+)\"").Groups[1].Value;
          if (cid.Length != 11 || !ctype.Contains("VIDEO") || !seen.Add(cid)) continue;
          var title = Regex.Match(chunk, "\"title\":\\{\"content\":\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value; if (title.Length == 0) continue;
          var len = Regex.Match(chunk.Substring(0, Math.Min(4000, chunk.Length)), "\"text\":\"(\\d{1,2}:\\d{2}(?::\\d{2})?)\"").Groups[1].Value; if (len.Length == 0) continue;
          int s2 = 0; foreach (var p in len.Split(':')) s2 = s2 * 60 + int.Parse(p); if (s2 > 720 || s2 < 60) continue;
          var tt = Un(title); if (Regex.IsMatch(tt, "gameplay|trailer|review|reaction|tutorial|podcast|\\bnews\\b|vlog|highlights|unboxing|#shorts|walkthrough", RegexOptions.IgnoreCase)) continue;
          int score = Regex.IsMatch(tt, "music|song|lyric|official|audio|\\bncs\\b|feat|remix|lofi|\\bft\\.|\\bmix\\b|beats|\\(.*\\)| - ", RegexOptions.IgnoreCase) ? 1 : 0;
          outp.Add(J.D("id", cid, "title", tt, "length", len, "s", score)); if (outp.Count >= 12) break; }
        return outp; });
      var skip = new HashSet<string>((exclude ?? "").Split(','));
      return all.Where(x => !skip.Contains((string)x["id"])).OrderByDescending(x => (int)x["s"]).Take(8).Select(x => J.D("id", x["id"], "title", x["title"], "length", x["length"])).ToList(); }

    // ---------- links: YouTube video / playlist, Spotify track / album / playlist ----------
    // YouTube links are checked with YouTube's official oEmbed endpoint. Spotify audio can't be streamed into OBS, so Spotify
    // links are matched to the same song on YouTube. Track titles come from Spotify's official oEmbed endpoint; artists, albums
    // and playlists need your own free Spotify developer app (music.spotify.clientId / clientSecret) and the official Web API.
    public static Dictionary<string, object> Resolve(string input) {
      Resolves++; var s = (input ?? "").Trim(); if (s.Length == 0) return Fail("empty", "Paste a link or type a song name.");
      Match m;
      if ((m = Regex.Match(s, "^(?:https?://)?(?:open|play)\\.spotify\\.com/(?:intl-[a-z]{2}(?:-[a-z]{2})?/)?(?:embed/)?(track|album|playlist|artist|episode|show)/([A-Za-z0-9]{22})", RegexOptions.IgnoreCase)).Success ||
          (m = Regex.Match(s, "^spotify:(track|album|playlist|artist|episode|show):([A-Za-z0-9]{22})$", RegexOptions.IgnoreCase)).Success)
        return Spotify(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value);
      if (Regex.IsMatch(s, "^(https?://)?(spotify\\.link|spoti\\.fi)/", RegexOptions.IgnoreCase)) return Fail("unsupported", "Short Spotify links (spotify.link) can't be read. In Spotify use Share > Copy link to song / playlist.");
      bool yt = Regex.IsMatch(s, "^(https?://)?([\\w-]+\\.)?(youtube\\.com|youtu\\.be|youtube-nocookie\\.com)/", RegexOptions.IgnoreCase);
      if (!yt && Regex.IsMatch(s, "^[\\w-]{11}$") && Regex.IsMatch(s, "[0-9_-]|[A-Z].*[a-z]|[a-z].*[A-Z]")) { s = "https://youtu.be/" + s; yt = true; }
      if (yt) {
        var list = Regex.Match(s, "[?&]list=([\\w-]+)").Groups[1].Value; var id = Regex.Match(s, "(?:[?&]v=|youtu\\.be/|/shorts/|/embed/|/live/|/v/)([\\w-]{11})").Groups[1].Value;
        if (list.Length > 0 && Regex.IsMatch(list, "^(RD|UL|LL$|WL$)")) {
          if (id.Length == 0) return Fail("unsupported", "YouTube Mix / Liked / Watch Later lists are personal to your account and can't be played in OBS. Paste a normal playlist or a video link.");
          list = ""; }   // mix radio: play the video instead
        if (list.Length > 0) { var r = OEmbed("https://www.youtube.com/playlist?list=" + list); if (!(bool)r["ok"]) return Fail(Convert.ToString(r["code"]), "This playlist can't be used: " + r["why"]);
          var res = Ok("youtube-playlist", J.D("list", list, "title", r["title"]), (string)r["title"], "playlist by " + r["author"]); res["duplicate"] = InQueue(null, list); return res; }
        if (id.Length == 0) return Fail("invalid", "That YouTube link has no video in it. Open the video and copy its link again.");
        var v = OEmbed("https://www.youtube.com/watch?v=" + id); if (!(bool)v["ok"]) return Fail(Convert.ToString(v["code"]), "This video can't be used: " + v["why"]);
        var rv = Ok("youtube-video", J.D("id", id, "title", v["title"]), (string)v["title"], (string)v["author"]); rv["duplicate"] = InQueue(id, null);
        rv["note"] = "Age-restricted and region-blocked videos pass this check but may be skipped when they play (YouTube doesn't allow them outside youtube.com)."; return rv; }
      if (Regex.IsMatch(s, "^(https?://|www\\.)", RegexOptions.IgnoreCase)) return Fail("unsupported", "Only YouTube and Spotify links are supported (for other sites, search the song name).");
      var sr = Search(s); if (sr.Count == 0) return Fail("notfound", "No YouTube results for \"" + s + "\".");
      return Ok("search", J.D("id", sr[0]["id"], "title", sr[0]["title"]), (string)sr[0]["title"], "first search result"); }
    static Dictionary<string, object> Ok(string kind, Dictionary<string, object> item, string title, string sub) { return J.D("ok", true, "kind", kind, "item", item, "title", title, "subtitle", sub); }
    static Dictionary<string, object> Fail(string code, string msg) { return J.D("ok", false, "code", code, "error", msg); }
    static Dictionary<string, object> OEmbed(string url) {
      return (Dictionary<string, object>)Cached("o:" + url, 600, () => {
        try { var d = J.Parse(Get("https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(url), 10000)); return J.D("ok", true, "title", J.Str(d, "title", "YouTube"), "author", J.Str(d, "author_name", "")); }
        catch (Exception e) { int c = Code(e);
          string why = c == 401 || c == 403 ? "it is private, or its owner doesn't allow playing it outside YouTube" : c == 404 ? "it doesn't exist, was deleted, or is private" : c == 400 ? "it doesn't exist (check the link)" : c == 0 ? "couldn't reach YouTube (" + e.Message + ")" : "YouTube answered " + c;
          return J.D("ok", false, "code", c == 0 ? "network" : c == 404 ? "notfound" : c == 400 ? "invalid" : "private", "why", why); } }); }

    static string spToken; static DateTime spTokenUntil;
    static string SpotifyToken() {
      var id = J.Str(Cfg.D, "music.spotify.clientId", ""); var secret = J.Str(Cfg.D, "music.spotify.clientSecret", ""); if (id.Length == 0 || secret.Length == 0) return null;
      if (spToken != null && DateTime.Now < spTokenUntil) return spToken;
      var r = (HttpWebRequest)WebRequest.Create("https://accounts.spotify.com/api/token"); r.Method = "POST"; r.ContentType = "application/x-www-form-urlencoded"; r.Timeout = 10000;
      r.Headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(id + ":" + secret));
      var b = Encoding.ASCII.GetBytes("grant_type=client_credentials"); using (var st = r.GetRequestStream()) st.Write(b, 0, b.Length);
      try { using (var resp = r.GetResponse()) using (var sr = new StreamReader(resp.GetResponseStream())) { var d = J.Parse(sr.ReadToEnd()); spToken = J.Str(d, "access_token", null); spTokenUntil = DateTime.Now.AddSeconds(J.Int(d, "expires_in", 3600) - 120); return spToken; } }
      catch (Exception e) { throw new Exception(Code(e) == 400 || Code(e) == 401 ? "Spotify rejected your clientId / clientSecret" : "couldn't reach Spotify (" + e.Message + ")"); } }
    static Dictionary<string, object> SpApi(string path, string tok) {
      var r = (HttpWebRequest)WebRequest.Create("https://api.spotify.com/v1/" + path); r.Timeout = 12000; r.Headers["Authorization"] = "Bearer " + tok;
      using (var resp = r.GetResponse()) using (var sr = new StreamReader(resp.GetResponseStream())) return J.Parse(sr.ReadToEnd()); }
    static Dictionary<string, object> Spotify(string kind, string id) {
      var url = "https://open.spotify.com/" + kind + "/" + id;
      if (kind == "artist" || kind == "episode" || kind == "show") return Fail("unsupported", "Spotify " + kind + " links aren't supported. Use a song, album or playlist link.");
      string title = null; try { title = J.Str(J.Parse(Get("https://open.spotify.com/oembed?url=" + Uri.EscapeDataString(url), 10000)), "title", null); }
      catch (Exception e) { int c = Code(e); if (c == 404 || c == 400) return Fail("notfound", "Spotify doesn't know this " + kind + " (deleted, private, or not available in your country)."); if (c == 0) return Fail("network", "Couldn't reach Spotify: " + e.Message); }
      string tok = null; string tokErr = null; try { tok = SpotifyToken(); } catch (Exception e) { tokErr = e.Message; }
      if (kind == "track") {
        string q = title, artist = ""; int durMs = 0;
        if (tok != null) { try { var t = SpApi("tracks/" + id, tok); title = J.Str(t, "name", title); var ar = J.Get(t, "artists") as ArrayList; if (ar != null && ar.Count > 0) artist = J.Str((Dictionary<string, object>)ar[0], "name", ""); durMs = J.Int(t, "duration_ms", 0); } catch (Exception e) { tokErr = "Spotify API: " + e.Message; } }
        if (string.IsNullOrEmpty(title)) return Fail("notfound", "Couldn't read this Spotify song.");
        q = (artist.Length > 0 ? artist + " - " : "") + title + " audio";
        var m = MatchYouTube(q, durMs); if (m == null) return Fail("notfound", "Couldn't find \"" + title + "\" on YouTube.");
        var res = Ok("spotify-track", J.D("id", m["id"], "title", (artist.Length > 0 ? artist + " - " : "") + title, "from", "spotify"), (artist.Length > 0 ? artist + " - " : "") + title, "Spotify song, playing the YouTube match: " + m["title"]);
        res["duplicate"] = InQueue((string)m["id"], null);
        if (artist.Length == 0) res["note"] = "Matched by song title only" + (tokErr != null ? " (" + tokErr + ")" : " - add your Spotify developer app keys for exact artist matching") + ", so it may pick a different version.";
        return res; }
      // album / playlist: needs the official Web API (free developer app of your own)
      if (tok == null) return Fail("needs-spotify-app", "\"" + (title ?? "This " + kind) + "\": Spotify " + kind + "s need your own free Spotify developer app (music.spotify.clientId and clientSecret in config.json - see docs/CONFIGURATION.md)." + (tokErr != null ? " " + tokErr : ""));
      try {
        var tracks = new List<Dictionary<string, object>>(); string next = kind == "playlist" ? "playlists/" + id + "/tracks?limit=100&fields=items(track(name,duration_ms,is_local,artists(name))),next" : "albums/" + id + "/tracks?limit=50";
        int pages = 0; while (next != null && pages++ < 3) { var d = SpApi(next, tok); var items = J.Get(d, "items") as ArrayList;
          if (items != null) foreach (var o in items) { var it = o as Dictionary<string, object>; var tr = kind == "playlist" ? J.Obj(it, "track") : it; if (tr == null || J.Bool(tr, "is_local", false)) continue;
            var ar = J.Get(tr, "artists") as ArrayList; var artist = ar != null && ar.Count > 0 ? J.Str((Dictionary<string, object>)ar[0], "name", "") : "";
            var nm = J.Str(tr, "name", ""); if (nm.Length == 0) continue; tracks.Add(J.D("q", (artist.Length > 0 ? artist + " - " : "") + nm + " audio", "title", (artist.Length > 0 ? artist + " - " : "") + nm, "ms", J.Int(tr, "duration_ms", 0), "from", "spotify")); }
          var nx = J.Str(d, "next", null); next = nx == null ? null : nx.Replace("https://api.spotify.com/v1/", ""); }
        if (tracks.Count == 0) return Fail("empty", "This Spotify " + kind + " has no playable songs.");
        var res = J.D("ok", true, "kind", "spotify-" + kind, "items", tracks.Take(150).ToList(), "title", title ?? "Spotify " + kind, "subtitle", tracks.Count + " songs - each is matched on YouTube when its turn comes");
        if (tracks.Count > 150) res["note"] = "Only the first 150 songs were added."; return res;
      } catch (Exception e) { int c = Code(e);
        return Fail(c == 404 ? "private" : "error", c == 404 ? "Spotify won't share this " + kind + ". Private playlists, and Spotify's own editorial / algorithmic playlists (Discover Weekly, Top 50...) aren't available to apps. Copy the songs to a public playlist of your own." : "Spotify error: " + e.Message); } }
    // best YouTube match: prefer a length close to Spotify's, and "official audio / topic" uploads
    static Dictionary<string, object> MatchYouTube(string q, int durMs) {
      var r = Search(q); if (r.Count == 0) return null; if (durMs <= 0) return r[0];
      Func<string, int> secs = len => { int s = 0; foreach (var p in (len ?? "").Split(':')) { int n; if (!int.TryParse(p, out n)) return -1; s = s * 60 + n; } return s; };
      return r.Take(6).OrderBy(x => { int s = secs((string)x["length"]); return (s <= 0 ? 999 : Math.Abs(s - durMs / 1000)) - (Regex.IsMatch((string)x["title"], "official audio|audio|topic", RegexOptions.IgnoreCase) ? 3 : 0); }).First(); }

    // ---------- API ----------
    public static Dictionary<string, object> DiagInfo() {
      var alive = Hub.Count("player") > 0 && J.Bool(stateD, "ready", false) && (!J.Bool(stateD, "playing", false) || (DateTime.Now - stateAt).TotalSeconds < 12);   // a paused player only reports on changes
      var q = J.Get(stateD, "queue") as ArrayList;
      return J.D("playerConnected", Hub.Count("player") > 0, "playerReporting", alive, "playing", J.Bool(stateD, "playing", false), "title", J.Str(stateD, "title", ""), "queue", q == null ? 0 : q.Count,
        "note", J.Str(stateD, "note", ""), "destination", Mode(), "routing", Obs.Ready ? routeStatus : "saved - applies when OBS connects", "routingOk", routeOk && Obs.Ready, "tracks", routeActual, "obs", Obs.Ready ? "connected (obs-websocket " + Obs.ObsVersion + ")" : Obs.Status,
        "searches", Searches, "linkChecks", Resolves, "spotifyApp", J.Str(Cfg.D, "music.spotify.clientId", "").Length > 0); }
    static void Add(Dictionary<string, object> r, string mode, bool force, Action<object> reply) {
      if (!(bool)r["ok"]) { reply(r); return; }
      if (r.ContainsKey("duplicate") && (bool)r["duplicate"] && !force && mode == "add") { r["ok"] = false; r["code"] = "duplicate"; r["error"] = "\"" + r["title"] + "\" is already in the queue."; reply(r); return; }
      if (r.ContainsKey("items")) { var items = (List<Dictionary<string, object>>)r["items"]; Command(J.Ser(J.D("cmd", "addmany", "items", items, "play", mode == "playnow"))); }
      else Command(J.Ser(J.D("cmd", mode, "item", r["item"])));
      reply(r); }
    public static bool Api(HttpListenerContext ctx, string path, string m, System.Collections.Specialized.NameValueCollection q) {
      switch (path) {
        case "/api/cmd": if (m != "POST") return false; { var b = Http.Body(ctx); if (J.Parse(b).Count == 0) { Http.Json(ctx, 400, J.D("error", "bad command")); return true; } Command(b); Http.Json(ctx, J.D("n", seq)); return true; }
        case "/api/cmds": { int since; int.TryParse(q["since"], out since); string list; lock (Cmds) list = string.Join(",", Cmds.Where(c => c.Key > since).Select(c => c.Value)); Http.Send(ctx, 200, "{\"seq\":" + seq + ",\"cmds\":[" + list + "]}", null); return true; }
        case "/api/state": if (m == "POST") { SetState(Http.Body(ctx)); Http.Json(ctx, J.D()); } else Http.Send(ctx, 200, state, null); return true;
        case "/api/search": try { Http.Json(ctx, Search(q["q"])); } catch (Exception e) { Http.Json(ctx, 502, J.D("error", "YouTube search failed: " + e.Message)); } return true;
        case "/api/related": try { Http.Json(ctx, Related(q["id"], q["exclude"])); } catch (Exception e) { Http.Json(ctx, 502, J.D("error", e.Message)); } return true;
        case "/api/music/resolve": Http.Json(ctx, Resolve(q["url"])); return true;
        case "/api/music/match": { var r = Search(q["q"]); int ms; int.TryParse(q["ms"], out ms); var best = MatchYouTube(q["q"], ms); Http.Json(ctx, best == null ? (object)J.D("ok", false) : J.D("ok", true, "id", best["id"], "title", best["title"])); return true; }
        case "/api/music/add": if (m != "POST") return false; { var b = J.Parse(Http.Body(ctx)); object res = null; Add(Resolve(J.Str(b, "url", "")), J.Str(b, "mode", "add"), J.Bool(b, "force", false), x => res = x); Http.Json(ctx, res); return true; }
        case "/api/music/route": if (m == "POST") Http.Json(ctx, ApplyRoute(J.Str(J.Parse(Http.Body(ctx)), "mode", "all"), true).Result); else Http.Json(ctx, RouteInfo()); return true;
        case "/api/obs/call": {   // tests only (--test): read-back of OBS state for the routing tests
          if (!Program.TestMode || m != "POST") { Http.Json(ctx, 403, J.D("error", "only in test mode")); return true; }
          var b = J.Parse(Http.Body(ctx)); var type = J.Str(b, "type", "");
          if (!Regex.IsMatch(type, "^(Get\\w+|SetInputAudioTracks)$")) { Http.Json(ctx, 403, J.D("error", "not allowed")); return true; }
          try { Http.Json(ctx, J.D("ok", true, "d", Obs.Call(type, J.Obj(b, "data")).Result)); } catch (Exception e) { Http.Json(ctx, J.D("ok", false, "error", (e.InnerException ?? e).Message)); } return true; }
        case "/api/log": if (m == "POST") { Diag.Ev("player " + Http.Body(ctx)); Http.Json(ctx, J.D()); } else Http.Send(ctx, 200, string.Join("\n", Diag.Recent(200)), "text/plain; charset=utf-8"); return true;
      }
      return false; }
    public static void OnWs(Client c, string type, Dictionary<string, object> msg) {
      switch (type) {
        case "hello": if (c.Topics.Contains("music.state")) { Hub.Send(c, "{\"type\":\"music.state\",\"s\":" + state + "}"); Hub.Send(c, RouteInfo()); } if (c.Role == "player") Diag.Ev("music player connected"); break;
        case "_closed": if (c.Role == "player") Diag.Ev("music player disconnected"); break;
        case "music.cmd": { var cmd = J.Obj(msg, "c"); if (cmd != null) Command(J.Ser(cmd)); break; }
        case "music.state": { var s = J.Obj(msg, "s"); if (s != null) SetState(J.Ser(s)); break; }
        case "music.log": Diag.Ev("player " + J.Str(msg, "ev", "") + " " + J.Str(msg, "info", "")); break;
        case "music.add": { var id = J.Str(msg, "reqId", ""); var url = J.Str(msg, "url", ""); var mode = J.Str(msg, "mode", "add"); bool force = J.Bool(msg, "force", false);
          if (mode != "add" && mode != "playnow" && mode != "playnext") mode = "add";
          Task.Run(() => { Dictionary<string, object> r; try { r = Resolve(url); } catch (Exception e) { r = Fail("error", e.Message); }
            Add(r, mode, force, x => { var d = new Dictionary<string, object>((Dictionary<string, object>)x); d["type"] = "music.added"; d["reqId"] = id; Hub.Send(c, d); }); }); break; }
        case "music.search": { var id = J.Str(msg, "reqId", ""); var qq = J.Str(msg, "q", "");
          Task.Run(() => { object res; try { res = J.D("type", "music.results", "reqId", id, "results", Search(qq)); } catch (Exception e) { res = J.D("type", "music.results", "reqId", id, "results", new object[0], "error", e.Message); } Hub.Send(c, res); }); break; }
        case "music.route": { var mode = J.Str(msg, "mode", "all"); ApplyRoute(mode, true).ContinueWith(t => Hub.Send(c, J.D("type", "music.routed", "result", t.Result))); break; }
      } }
  }
}
