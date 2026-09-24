// IXC Core - phone access from anywhere (mobile data, other Wi-Fi) without opening router ports.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
//
//   phone --HTTPS/WSS--> Cloudflare --> cloudflared.exe (outbound tunnel from this PC) --> 127.0.0.1:<remote.port> (this file)
//
// * The remote listener binds to 127.0.0.1 only: nothing listens on your network or the internet directly.
// * The tunnel only starts when you open the phone QR code, and stops again after remote.idleMinutes without a phone.
// * Through it, only the phone page, pairing and ONE authenticated WebSocket exist. Everything else is 404.
// * Pairing: the QR holds a one-time code (valid 5 minutes, in the URL #fragment so it never appears in request logs).
//   The phone trades it for a random session token (kept in memory only; sliding remote.sessionHours, max 7 days).
// * Origin + Host must match the tunnel address; failed pairing attempts and message floods are rate-limited.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public static class Remote {
    class Session { public DateTime Created, LastSeen; public string Device = "", Ip = ""; }
    static readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>();
    static string pairHash; static DateTime pairUntil = DateTime.MinValue;
    static readonly Dictionary<string, List<DateTime>> fails = new Dictionary<string, List<DateTime>>(), hits = new Dictionary<string, List<DateTime>>();
    static int pairFailsTotal, pairOk;
    static HttpListener listener; public static int Port = 8769;
    static Process tunnel; static string tunnelUrl, tunnelHost, tunnelStatus = "off", tunnelError = "", cfVersion = ""; static bool reachable; static DateTime tunnelStarted, lastActivity = DateTime.Now;
    static readonly object TL = new object(); static Timer idleT; static IntPtr job = IntPtr.Zero;
    static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public static bool Enabled { get { return J.Bool(Cfg.D, "remote.enabled", true); } }
    static string Exe { get { var p = J.Str(Cfg.D, "remote.cloudflaredPath", ""); return p.Length > 0 ? p : Path.Combine(Cfg.DataDir, "bin", "cloudflared.exe"); } }

    public static void Init() {
      Port = J.Int(Cfg.D, "remote.port", Program.Port + 2);
      if (!Enabled) { tunnelStatus = "disabled in config (remote.enabled)"; return; }
      listener = new HttpListener(); listener.Prefixes.Add("http://127.0.0.1:" + Port + "/");
      try { listener.Start(); new Thread(() => Program.Loop(listener, true)) { IsBackground = true, Name = "http-remote" }.Start(); }
      catch (Exception e) { tunnelStatus = "error"; tunnelError = "port " + Port + " busy: " + e.Message; Diag.Err("remote listener: " + tunnelError); listener = null; }
      idleT = new Timer(_ => IdleCheck(), null, 60000, 60000);
    }
    public static void Shutdown() { StopTunnel("IXC is closing"); }

    // ---------------- tokens + sessions ----------------
    static string NewToken(int bytes) { var b = new byte[bytes]; Rng.GetBytes(b); return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
    static string Hash(string s) { using (var h = SHA256.Create()) return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""))); }
    static bool SlowEq(string a, string b) { if (a == null || b == null || a.Length != b.Length) return false; int d = 0; for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i]; return d == 0; }
    public static bool CheckSession(string tok, string ip) {
      if (string.IsNullOrEmpty(tok) || tok.Length > 100) return false; var h = Hash(tok);
      lock (Sessions) { Session s; if (!Sessions.TryGetValue(h, out s)) return false;
        var now = DateTime.Now; if (now - s.LastSeen > TimeSpan.FromHours(J.Int(Cfg.D, "remote.sessionHours", 12)) || now - s.Created > TimeSpan.FromDays(7)) { Sessions.Remove(h); return false; }
        s.LastSeen = now; s.Ip = ip; lastActivity = now; return true; } }
    static bool Limited(Dictionary<string, List<DateTime>> table, string ip, int max, int seconds, bool add) {
      lock (table) { List<DateTime> l; if (!table.TryGetValue(ip, out l)) { l = new List<DateTime>(); table[ip] = l; if (table.Count > 5000) table.Clear(); }
        var cut = DateTime.Now.AddSeconds(-seconds); l.RemoveAll(t => t < cut); if (l.Count >= max) return true; if (add) l.Add(DateTime.Now); return false; } }
    static readonly HashSet<string> Topics = new HashSet<string> { "chat", "tts", "music.state", "diag", "reload" };
    static readonly HashSet<string> Messages = new HashSet<string> { "hello", "ping", "chat.send", "chat.suggest", "tts.set", "tts.say", "tts.test", "tts.skip", "tts.clear", "tts.replay",
      "music.cmd", "music.add", "music.search", "music.route", "diag.get", "remote.logout" };
    public static bool AllowedTopic(string t) { return Topics.Contains(t); }
    public static bool AllowedMessage(string t) { return Messages.Contains(t); }

    // ---------------- requests arriving through the tunnel ----------------
    public static void Handle(HttpListenerContext ctx, string path) {
      var rq = ctx.Request; string ip = rq.Headers["Cf-Connecting-Ip"] ?? "direct";
      string host = (rq.Headers["Host"] ?? "").ToLowerInvariant();
      bool hostOk = tunnelHost != null && host == tunnelHost || (Program.TestMode && host == "127.0.0.1:" + Port);
      if (!hostOk) { Http.Send(ctx, 421, "unknown host", "text/plain"); return; }
      if (Limited(hits, ip, 240, 60, true)) { Http.Json(ctx, 429, J.D("error", "too many requests - wait a minute")); return; }
      string origin = rq.Headers["Origin"]; string self = (Program.TestMode && host.StartsWith("127.0.0.1") ? "http://" : "https://") + host;
      if (path == "/" || path == "/m" || path == "/m/") { ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src https: data:; media-src 'none'; connect-src 'self' wss://" + host + (Program.TestMode ? " ws://" + host : "") + "; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        var f = Path.Combine(Program.Roots["core"], "mobile.html"); if (File.Exists(f)) Http.SendBytes(ctx, 200, File.ReadAllBytes(f), "text/html; charset=utf-8"); else Http.Send(ctx, 404, "mobile page missing", "text/plain"); return; }
      if (path == "/core/ixc.js") { if (!Http.Static(ctx, path, Program.Roots)) Http.Send(ctx, 404, "", "text/plain"); return; }
      if (path == "/api/health") { Http.Json(ctx, J.D("ok", true)); return; }
      if (origin != self) { Http.Json(ctx, 403, J.D("error", "origin not allowed")); return; }
      if (path == "/api/pair" && rq.HttpMethod == "POST") {
        if (Limited(fails, ip, 5, 600, false) || Limited(fails, "*all*", 30, 600, false)) { Http.Json(ctx, 429, J.D("error", "too many wrong codes - wait 10 minutes and scan a new QR")); return; }
        var b = J.Parse(Http.Body(ctx)); var code = J.Str(b, "code", ""); bool good;
        lock (TL) { good = pairHash != null && DateTime.Now < pairUntil && SlowEq(Hash(code), pairHash); if (good) pairHash = null; }   // single use
        if (!good) { Limited(fails, ip, 5, 600, true); Limited(fails, "*all*", 30, 600, true); pairFailsTotal++; Diag.Ev("phone pairing refused (wrong or expired code) from " + ip); Http.Json(ctx, 401, J.D("error", "This QR code has expired or was already used. Scan a new one from the PC.")); return; }
        var tok = NewToken(32); var dev = J.Str(b, "device", ""); if (dev.Length > 60) dev = dev.Substring(0, 60);
        lock (Sessions) { Sessions[Hash(tok)] = new Session { Created = DateTime.Now, LastSeen = DateTime.Now, Device = dev, Ip = ip }; while (Sessions.Count > 10) Sessions.Remove(Sessions.OrderBy(kv => kv.Value.LastSeen).First().Key); }
        pairOk++; lastActivity = DateTime.Now; Diag.Info("phone paired (" + (dev.Length > 0 ? dev : "unknown device") + ", " + ip + ")"); Hub.Publish("remote", StateMsg());
        Http.Json(ctx, J.D("session", tok, "hours", J.Int(Cfg.D, "remote.sessionHours", 12))); return; }
      if (path == "/ws") { if (!rq.IsWebSocketRequest) { Http.Send(ctx, 400, "websocket only", "text/plain"); return; } var t = Hub.Run(ctx, true, ip); return; }
      Http.Send(ctx, 404, "not found", "text/plain");
    }
    public static void OnWs(Client c, string type, Dictionary<string, object> msg) {
      if (type == "remote.logout" && c.Remote) { lock (Sessions) Sessions.Remove(Hash(c.Session)); Diag.Ev("phone signed out"); try { c.Ws.Abort(); } catch { } }
      else if (type == "hello" && !c.Remote && c.Topics.Contains("remote")) Hub.Send(c, StateMsg()); }

    // ---------------- cloudflared quick tunnel ----------------
    static void SetStatus(string s, string err) { tunnelStatus = s; if (err != null) tunnelError = err; Hub.Publish("remote", StateMsg()); }
    static bool EnsureCloudflared() {
      if (File.Exists(Exe)) return true;
      if (!J.Bool(Cfg.D, "remote.allowDownload", true)) { SetStatus("error", "cloudflared.exe not found at " + Exe); return false; }
      var url = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"; var part = Exe + ".part";
      try { Directory.CreateDirectory(Path.GetDirectoryName(Exe)); SetStatus("downloading cloudflared (Cloudflare's official tunnel program, about 60 MB, one time)", "");
        Diag.Info("downloading " + url);
        using (var wc = new WebClient()) { wc.Headers["User-Agent"] = "IXC-Core"; wc.DownloadFile(url, part); }
        // only keep it if Windows says it is validly signed by Cloudflare
        var ps = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"$s = Get-AuthenticodeSignature -LiteralPath '" + part.Replace("'", "''") + "'; if ($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match 'O=\\\"?Cloudflare') { exit 0 } else { Write-Output $s.Status; exit 1 }\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
        var outp = ps.StandardOutput.ReadToEnd(); ps.WaitForExit(60000);
        if (ps.ExitCode != 0) { try { File.Delete(part); } catch { } SetStatus("error", "the downloaded cloudflared.exe was not validly signed by Cloudflare (" + outp.Trim() + ") - deleted it"); Diag.Err(tunnelError); return false; }
        File.Move(part, Exe); Diag.Info("cloudflared downloaded and signature verified (Cloudflare, Inc.)"); return true; }
      catch (Exception e) { try { File.Delete(part); } catch { } SetStatus("error", "couldn't download cloudflared: " + e.Message); Diag.Err(tunnelError); return false; } }
    public static bool StartTunnel(int waitMs) {
      lock (TL) {
        if (!Enabled || listener == null) return false;
        if (tunnel != null && !tunnel.HasExited) { if (tunnelUrl != null) return true; }
        else {
          if (!EnsureCloudflared()) return false;
          tunnelUrl = null; tunnelHost = null; reachable = false; SetStatus("starting", "");
          var psi = new ProcessStartInfo(Exe, "tunnel --no-autoupdate --protocol http2 --url http://127.0.0.1:" + Port) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
          try { tunnel = Process.Start(psi); } catch (Exception e) { SetStatus("error", "couldn't start cloudflared: " + e.Message); return false; }
          AddToJob(tunnel); tunnelStarted = DateTime.Now; lastActivity = DateTime.Now; var p = tunnel;
          DataReceivedEventHandler h = (s, e) => { if (e.Data == null) return; var line = e.Data;
            if (Diag.Verbose) Diag.Ev("cloudflared: " + line);
            var m = Regex.Match(line, "https://([a-z0-9-]+\\.trycloudflare\\.com)"); if (m.Success && tunnelUrl == null) { tunnelHost = m.Groups[1].Value; tunnelUrl = "https://" + tunnelHost; Diag.Info("tunnel address " + tunnelUrl); SetStatus("connecting", null); }
            if (line.Contains("Registered tunnel connection")) { if (tunnelStatus != "online") { SetStatus("online", ""); var t = Task.Run(() => VerifyReachable()); } }
            var v = Regex.Match(line, "Version ([\\d.]+)"); if (v.Success) cfVersion = v.Groups[1].Value;
            if (line.Contains(" ERR ") && !line.Contains("Retrying")) tunnelError = line.Length > 200 ? line.Substring(line.Length - 200) : line; };
          p.ErrorDataReceived += h; p.OutputDataReceived += h; p.EnableRaisingEvents = true;
          p.Exited += (s, e) => { if (tunnel == p) { tunnel = null; tunnelUrl = null; tunnelHost = null; reachable = false; SetStatus("off", tunnelStatus == "stopping" ? "" : "cloudflared stopped" + (tunnelError.Length > 0 ? ": " + tunnelError : "")); Diag.Ev("tunnel stopped"); } };
          p.BeginErrorReadLine(); p.BeginOutputReadLine();
          Diag.Info("tunnel starting (cloudflared quick tunnel)");
        } }
      var until = DateTime.Now.AddMilliseconds(waitMs); while (DateTime.Now < until && tunnelStatus != "online") { Thread.Sleep(200); if (tunnel == null) break; }
      return tunnelUrl != null && tunnelStatus == "online"; }
    static void VerifyReachable() {   // proves the phone URL works from the internet (the request leaves this PC and comes back through Cloudflare)
      for (int i = 0; i < 15 && tunnelUrl != null; i++) {
        try { var r = (HttpWebRequest)WebRequest.Create(tunnelUrl + "/api/health"); r.Timeout = 8000; r.UserAgent = "IXC-Core self-check";
          using (var resp = (HttpWebResponse)r.GetResponse()) if (resp.StatusCode == HttpStatusCode.OK) { reachable = true; Diag.Info("tunnel verified reachable from the internet (" + (i + 1) + " tries)"); Hub.Publish("remote", StateMsg()); return; } }
        catch { } Thread.Sleep(2000); }
      Diag.Err("tunnel is up but not reachable yet from the internet (Cloudflare DNS can take a moment)"); Hub.Publish("remote", StateMsg()); }
    public static void StopTunnel(string why) {
      lock (TL) { var p = tunnel; if (p == null) return; tunnelStatus = "stopping"; try { if (!p.HasExited) p.Kill(); } catch { } tunnel = null; tunnelUrl = null; tunnelHost = null; reachable = false; }
      Diag.Info("tunnel stopped (" + why + ")"); SetStatus("off", ""); }
    static void IdleCheck() {
      if (tunnel == null) return; bool phones = Hub.CountRemote() > 0; bool pairing = DateTime.Now < pairUntil && pairHash != null;
      if (phones || pairing) { lastActivity = DateTime.Now; return; }
      if ((DateTime.Now - lastActivity).TotalMinutes >= J.Int(Cfg.D, "remote.idleMinutes", 30)) StopTunnel("no phone for " + J.Int(Cfg.D, "remote.idleMinutes", 30) + " minutes"); }

    // make Windows kill cloudflared if IXC Core ends in any way (crash, Task Manager)
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int len);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [StructLayout(LayoutKind.Sequential)] struct JOBOBJECT_BASIC_LIMIT_INFORMATION { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public int LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public int ActiveProcessLimit; public UIntPtr Affinity; public int PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] struct IO_COUNTERS { public ulong a, b, c, d, e, f; }
    [StructLayout(LayoutKind.Sequential)] struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION { public JOBOBJECT_BASIC_LIMIT_INFORMATION Basic; public IO_COUNTERS Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    static void AddToJob(Process p) {
      try { if (job == IntPtr.Zero) { job = CreateJobObject(IntPtr.Zero, null); var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION(); info.Basic.LimitFlags = 0x2000; /* KILL_ON_JOB_CLOSE */
          SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))); }
        AssignProcessToJobObject(job, p.Handle); } catch (Exception e) { Diag.Err("job object: " + e.Message); } }

    // ---------------- PC-side API (the docks' phone button) ----------------
    static Dictionary<string, object> StateMsg() {
      int n; lock (Sessions) n = Sessions.Count;
      return J.D("type", "remote.state", "enabled", Enabled, "tunnel", tunnelStatus, "url", tunnelUrl, "reachable", reachable, "error", tunnelError, "sessions", n, "phones", Hub.CountRemote(),
        "pairingActive", pairHash != null && DateTime.Now < pairUntil, "pairExpiresIn", pairHash != null && DateTime.Now < pairUntil ? (int)(pairUntil - DateTime.Now).TotalSeconds : 0); }
    public static Dictionary<string, object> DiagInfo() {
      var d = StateMsg(); d.Remove("type"); d["cloudflared"] = File.Exists(Exe) ? (cfVersion.Length > 0 ? cfVersion : "installed") : "not downloaded yet";
      d["tunnelUptimeMin"] = tunnel != null ? (int)(DateTime.Now - tunnelStarted).TotalMinutes : 0; d["pairings"] = pairOk; d["refusedPairings"] = pairFailsTotal;
      d["tunnelRamMB"] = 0.0; try { if (tunnel != null && !tunnel.HasExited) { tunnel.Refresh(); d["tunnelRamMB"] = Math.Round(tunnel.WorkingSet64 / 1048576.0, 1); } } catch { }
      return d; }
    public static bool LocalApi(HttpListenerContext ctx, string path, string m) {
      if (!path.StartsWith("/api/remote/")) return false;
      if (path == "/api/remote/status") { Http.Json(ctx, StateMsg()); return true; }
      if (m != "POST") { Http.Json(ctx, 405, J.D("error", "POST only")); return true; }
      if (path == "/api/remote/pair") {
        if (!Enabled) { Http.Json(ctx, 403, J.D("ok", false, "error", "Phone access is turned off (remote.enabled in config.json).")); return true; }
        if (!StartTunnel(45000)) { Http.Json(ctx, 503, J.D("ok", false, "error", tunnelError.Length > 0 ? tunnelError : "the tunnel didn't come up (status: " + tunnelStatus + ")", "status", StateMsg())); return true; }
        var code = NewToken(18); lock (TL) { pairHash = Hash(code); pairUntil = DateTime.Now.AddMinutes(5); }
        Diag.Ev("phone QR code created (valid 5 minutes, one use)"); Hub.Publish("remote", StateMsg());
        Http.Json(ctx, J.D("ok", true, "pairUrl", tunnelUrl + "/m#p=" + code, "url", tunnelUrl, "expiresIn", 300, "reachable", reachable)); return true; }
      if (path == "/api/remote/stop") { lock (TL) pairHash = null; StopTunnel("stopped from the PC"); Http.Json(ctx, StateMsg()); return true; }
      if (path == "/api/remote/revoke") { lock (Sessions) Sessions.Clear(); lock (TL) pairHash = null; List<Client> ph; lock (Hub.Clients) ph = Hub.Clients.Where(x => x.Remote).ToList(); foreach (var c in ph) try { c.Ws.Abort(); } catch { }
        Diag.Info("all phones signed out"); Hub.Publish("remote", StateMsg()); Http.Json(ctx, StateMsg()); return true; }
      Http.Json(ctx, 404, J.D("error", "unknown")); return true; }
  }
}
