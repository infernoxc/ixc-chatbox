// IXC Core - local music: songs from folders on this PC. Files are only ever served by their id from the scanned list, so a
// page can never ask for an arbitrary path.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace IXC {
  public static class Local {
    class Song { public string Id, Path, Title, Artist, Ext; }
    static Dictionary<string, Song> songs = new Dictionary<string, Song>(); static readonly object L = new object(); static volatile bool scanning; static Timer t;
    static readonly Dictionary<string, string> Types = new Dictionary<string, string> { { ".mp3", "audio/mpeg" }, { ".m4a", "audio/mp4" }, { ".aac", "audio/aac" }, { ".ogg", "audio/ogg" }, { ".opus", "audio/ogg" }, { ".wav", "audio/wav" }, { ".flac", "audio/flac" }, { ".webm", "audio/webm" } };
    public static int Count { get { lock (L) return songs.Count; } }
    public static void Init() { t = new Timer(_ => Scan(), null, 1500, 600000); }
    public static void Rescan() { t.Change(300, 600000); }
    static void Scan() {
      if (scanning) return; scanning = true;
      try {
        var found = new Dictionary<string, Song>(); int n = 0;
        foreach (var dir in Settings.List("music.localFolders")) {
          if (!Directory.Exists(dir)) { Log.Warn("music", "music folder not found: " + dir); continue; }
          var stack = new Stack<KeyValuePair<string, int>>(); stack.Push(new KeyValuePair<string, int>(dir, 0));
          while (stack.Count > 0 && n < 20000) { var cur = stack.Pop();
            try { foreach (var f in Directory.GetFiles(cur.Key)) { var ext = Path.GetExtension(f).ToLowerInvariant(); if (!Types.ContainsKey(ext)) continue;
                var id = U.ShaHex(f.ToLowerInvariant()).Substring(0, 16); if (found.ContainsKey(id)) continue; found[id] = Read(f, id, ext); n++; }
              if (cur.Value < 8) foreach (var d in Directory.GetDirectories(cur.Key)) stack.Push(new KeyValuePair<string, int>(d, cur.Value + 1)); }
            catch (Exception e) { Log.Debug("music", "skipped " + cur.Key + ": " + e.Message); } } }
        lock (L) songs = found; if (found.Count > 0 || Settings.List("music.localFolders").Count > 0) Log.Info("music", "local music: " + found.Count + " songs");
        Hub.Publish("music.state", J.D("type", "music.local", "count", found.Count, "folders", Settings.List("music.localFolders"))); }
      catch (Exception e) { Log.Err("music", "local music scan: " + e.Message); }
      finally { scanning = false; } }
    static Song Read(string f, string id, string ext) {
      var s = new Song { Id = id, Path = f, Ext = ext }; var name = Path.GetFileNameWithoutExtension(f);
      var m = Regex.Match(name, "^(?:\\d{1,3}[ ._-]+)?(.+?)\\s+-\\s+(.+)$"); if (m.Success) { s.Artist = m.Groups[1].Value.Trim(); s.Title = m.Groups[2].Value.Trim(); } else s.Title = Regex.Replace(name, "^\\d{1,3}[ ._-]+", "").Trim();
      if (ext == ".mp3") { try { Id3(f, s); } catch { } }
      if (string.IsNullOrEmpty(s.Title)) s.Title = name; return s; }
    // ID3v2.3 / 2.4 title + artist (TIT2 / TPE1)
    static void Id3(string f, Song s) {
      using (var fs = File.OpenRead(f)) { var h = new byte[10]; if (fs.Read(h, 0, 10) < 10 || h[0] != 'I' || h[1] != 'D' || h[2] != '3') return; int ver = h[3];
        int size = (h[6] & 0x7f) << 21 | (h[7] & 0x7f) << 14 | (h[8] & 0x7f) << 7 | (h[9] & 0x7f); if (size > 2000000) size = 2000000; var b = new byte[size]; int got = fs.Read(b, 0, size); int p = 0;
        if ((h[5] & 0x40) != 0 && got > 4) { int ext = ver == 4 ? ((b[0] & 0x7f) << 21 | (b[1] & 0x7f) << 14 | (b[2] & 0x7f) << 7 | (b[3] & 0x7f)) : (b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]) + 4; p = ext; }
        while (p + 10 <= got) { var id = Encoding.ASCII.GetString(b, p, 4); if (id[0] == 0) break;
          int len = ver == 4 ? ((b[p + 4] & 0x7f) << 21 | (b[p + 5] & 0x7f) << 14 | (b[p + 6] & 0x7f) << 7 | (b[p + 7] & 0x7f)) : (b[p + 4] << 24 | b[p + 5] << 16 | b[p + 6] << 8 | b[p + 7]);
          if (len <= 0 || p + 10 + len > got) break;
          if (id == "TIT2" || id == "TPE1") { var txt = Text(b, p + 10, len); if (txt.Length > 0) { if (id == "TIT2") s.Title = txt; else s.Artist = txt; } }
          p += 10 + len; } } }
    static string Text(byte[] b, int at, int len) {
      if (len < 2) return ""; int enc = b[at]; string r;
      if (enc == 0) r = Encoding.GetEncoding(28591).GetString(b, at + 1, len - 1); else if (enc == 1) r = Encoding.Unicode.GetString(b, at + 1, len - 1).TrimStart('﻿', '￾');
      else if (enc == 2) r = Encoding.BigEndianUnicode.GetString(b, at + 1, len - 1); else r = Encoding.UTF8.GetString(b, at + 1, len - 1);
      return r.Replace("\0", " ").Trim(); }
    static Dictionary<string, object> View(Song s) { return J.D("kind", "local", "local", s.Id, "title", s.Title, "artist", s.Artist ?? "", "channel", "on this PC", "length", ""); }
    public static Dictionary<string, object> Get(string id) { lock (L) { Song s; return songs.TryGetValue(id ?? "", out s) ? View(s) : null; } }
    public static List<Dictionary<string, object>> Search(string q, int n) {
      var words = Regex.Split((q ?? "").ToLowerInvariant(), "\\W+").Where(w => w.Length > 0).ToList(); if (words.Count == 0) return new List<Dictionary<string, object>>();
      lock (L) return songs.Values.Where(s => { var hay = (s.Title + " " + s.Artist + " " + Path.GetFileName(s.Path)).ToLowerInvariant(); return words.All(w => hay.Contains(w)); }).Take(n).Select(View).ToList(); }
    public static void Serve(Ctx ctx, string id) {
      Song s; lock (L) songs.TryGetValue(id ?? "", out s);
      if (s == null || !File.Exists(s.Path)) { Http.Send(ctx, 404, "file not found", "text/plain"); return; }
      Http.SendFile(ctx, s.Path, Types[s.Ext]); }
    public static Dictionary<string, object> Folders() { return J.D("folders", Settings.List("music.localFolders"), "songs", Count, "scanning", scanning); }
    public static Dictionary<string, object> AddFolder(string path) {
      if (string.IsNullOrEmpty(path)) path = Pick();
      if (string.IsNullOrEmpty(path)) return J.D("ok", false, "error", "No folder chosen");
      try { path = Path.GetFullPath(path); } catch { return J.D("ok", false, "error", "That isn't a folder path"); }
      if (!Directory.Exists(path)) return J.D("ok", false, "error", "That folder doesn't exist");
      var l = Settings.List("music.localFolders"); if (!l.Contains(path, StringComparer.OrdinalIgnoreCase)) l.Add(path);
      var err = Settings.Set("music.localFolders", l); return err != null ? J.D("ok", false, "error", err) : J.D("ok", true, "folders", l); }
    public static Dictionary<string, object> RemoveFolder(string path) { var l = Settings.List("music.localFolders").Where(x => !string.Equals(x, path, StringComparison.OrdinalIgnoreCase)).ToList(); Settings.Set("music.localFolders", l); return J.D("ok", true, "folders", l); }
    // the Windows "choose a folder" window, opened by IXC itself (web pages can't see folder paths)
    static string Pick() {
      if (!U.IsWindows || Program.TestMode) return null; string result = null;
      var th = new Thread(() => { try { using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder with your music (IXC plays the songs in it and its subfolders)", ShowNewFolderButton = false }) {
            var owner = new System.Windows.Forms.Form { TopMost = true, ShowInTaskbar = false, Width = 0, Height = 0, StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen }; owner.Show(); owner.Activate();
            if (d.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK) result = d.SelectedPath; owner.Close(); } } catch (Exception e) { Log.Warn("music", "folder picker: " + e.Message); } });
      th.SetApartmentState(ApartmentState.STA); th.Start(); th.Join(); return result; }
  }
}
