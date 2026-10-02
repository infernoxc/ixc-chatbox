// IXC Core - Quick connect: the phone remote with no setup at all.
//
//   phone --HTTPS/WSS--> https://<random>.trycloudflare.com --> cloudflared.exe (outgoing tunnel from this PC) --> IXC (127.0.0.1)
//
// * Cloudflare's free "quick tunnel": no account, no router or firewall setup, works on mobile data. The address is temporary:
//   it changes every time the tunnel starts, so phones scan a new QR code each time (their keys are forgotten when it stops).
// * cloudflared.exe is Cloudflare's official program, downloaded once from its GitHub releases and kept only when Windows
//   confirms a valid signature from Cloudflare, Inc. Windows ends it together with IXC (job object).
// * Through the tunnel only the phone page and the phone's WebSocket exist (everything else is 404); the phone still has to
//   pair with the one-time code, and then gets the same limited phone permissions as through the relay.
// * The tunnel stops after 30 minutes without a phone, when the phone remote is turned off, and when IXC stops.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public static class Quick {
    static readonly object TL = new object(); static Process tunnel; static string host, id = "";
    static volatile string state = "off", message = ""; static DateTime lastActive = DateTime.Now; static Timer idleT;
    public static string State { get { return state; } }
    public static string Message { get { return message; } }
    public static string Url { get { var h = host; return h == null ? null : (TestHttp ? "http://" + h + ":" + Program.Port : "https://" + h); } }
    public static string Id { get { return id; } }
    // tests run a stand-in for cloudflared and reach IXC over plain http
    static string TestExe { get { if (!Program.TestMode) return null; var v = Environment.GetEnvironmentVariable("IXC_CLOUDFLARED"); return string.IsNullOrEmpty(v) ? null : v; } }
    static bool TestHttp { get { return TestExe != null; } }
    public static bool Supported { get { return U.IsWindows || TestExe != null; } }
    static string Exe { get { return TestExe ?? Path.Combine(Cfg.DataDir, "bin", "cloudflared.exe"); } }
    const int IdleMinutes = 30;

    public static void Init() {
      idleT = new Timer(_ => IdleCheck(), null, 60000, 60000);
      AppDomain.CurrentDomain.ProcessExit += (s, e) => Stop("IXC is closing"); }
    static void Set(string s, string msg) { state = s; message = msg ?? ""; Remote.Publish(); }

    public static void Start() {
      lock (TL) { lastActive = DateTime.Now; if (state == "starting" || state == "downloading" || state == "online") return; state = "starting"; message = "Starting Quick connect…"; }
      Remote.Publish(); Task.Run(() => Run()); }
    static void Run() {
      try {
        if (!Ensure()) return;
        Set("starting", "Opening a secure link through Cloudflare…");
        var psi = new ProcessStartInfo(Exe, "tunnel --no-autoupdate --protocol http2 --url http://127.0.0.1:" + Program.Port) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        Process p; try { p = Process.Start(psi); } catch (Exception e) { Set("error", "Couldn't start Cloudflare's tunnel program: " + e.Message); return; }
        string lastErr = "";
        lock (TL) { tunnel = p; host = null; id = U.Token(9); }
        AddToJob(p);
        DataReceivedEventHandler h = (s, e) => { if (e.Data == null) return; var line = e.Data; Log.Debug("phone", "cloudflared: " + U.Trunc(line, 300));
          var m = Regex.Match(line, "https://([a-z0-9-]+\\.trycloudflare\\.com)"); if (m.Success && host == null) { lock (TL) if (tunnel == p) host = m.Groups[1].Value; }
          if (line.Contains("Registered tunnel connection") && host != null && state != "online") { Set("online", ""); Log.Info("phone", "Quick connect is on (" + host + ")"); }
          if (line.Contains(" ERR ") && !line.Contains("Retrying")) lastErr = Regex.Replace(line, "^\\S+\\s+ERR\\s+", ""); };
        p.ErrorDataReceived += h; p.OutputDataReceived += h; p.EnableRaisingEvents = true;
        p.Exited += (s, e) => { bool mine; lock (TL) { mine = tunnel == p; if (mine) { tunnel = null; host = null; } }
          if (!mine) return; Remote.DropQuick();
          if (state != "off") Set("error", "Cloudflare's tunnel stopped" + (lastErr.Length > 0 ? ": " + U.Trunc(lastErr, 160) : "") + ". Press Show QR code to try again.");
          Log.Info("phone", "Quick connect stopped"); };
        p.BeginErrorReadLine(); p.BeginOutputReadLine();
        for (int i = 0; i < 300 && state != "online"; i++) { Thread.Sleep(200); lock (TL) if (tunnel != p) return; }
        if (state != "online") { Stop(null); Set("error", "Cloudflare didn't open the link in time" + (lastErr.Length > 0 ? " (" + U.Trunc(lastErr, 160) + ")" : "") + ". Check the internet connection and try again."); } }
      catch (Exception e) { Set("error", "Quick connect failed: " + U.Plain(e)); Log.Err("phone", "Quick connect: " + U.Plain(e)); } }

    public static void Stop(string why) {
      Process p; lock (TL) { p = tunnel; tunnel = null; host = null; }
      if (p != null) { try { if (!p.HasExited) p.Kill(); } catch { } Log.Info("phone", "Quick connect stopped" + (why != null ? " (" + why + ")" : "")); Remote.DropQuick(); }
      if (why != null && state != "off") Set("off", ""); }
    static void IdleCheck() {
      if (state != "online") return;
      if (Remote.QuickPhones() > 0 || Remote.QuickPairing) { lastActive = DateTime.Now; return; }
      if ((DateTime.Now - lastActive).TotalMinutes >= IdleMinutes) Stop("no phone for " + IdleMinutes + " minutes"); }

    // ---------- cloudflared.exe: Cloudflare's official build, downloaded once, signature checked by Windows ----------
    static bool Ensure() {
      if (File.Exists(Exe)) return true;
      if (!U.IsWindows) { Set("error", "Quick connect works on Windows."); return false; }
      var url = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"; var part = Exe + ".part";
      try {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe));
        Set("downloading", "First time only: downloading Cloudflare's tunnel program (about 60 MB)…"); Log.Info("phone", "downloading " + url);
        using (var wc = new WebClient()) { wc.Headers["User-Agent"] = "IXC/" + Program.Version;
          wc.DownloadProgressChanged += (s, e) => { if (e.ProgressPercentage % 10 == 0) Set("downloading", "First time only: downloading Cloudflare's tunnel program… " + e.ProgressPercentage + "%"); };
          wc.DownloadFileTaskAsync(new Uri(url), part).Wait(); }
        Set("downloading", "Checking the download's signature…");
        var ps = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"$s = Get-AuthenticodeSignature -LiteralPath '" + part.Replace("'", "''") + "'; if ($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match 'O=\\\"?Cloudflare') { exit 0 } else { Write-Output $s.Status; exit 1 }\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
        var outp = ps.StandardOutput.ReadToEnd(); ps.WaitForExit(60000);
        if (ps.ExitCode != 0) { try { File.Delete(part); } catch { } Set("error", "The downloaded tunnel program wasn't validly signed by Cloudflare (" + outp.Trim() + "), so IXC deleted it."); Log.Err("phone", message); return false; }
        if (File.Exists(Exe)) File.Delete(Exe); File.Move(part, Exe); Log.Info("phone", "cloudflared downloaded, signature verified (Cloudflare, Inc.)"); return true; }
      catch (Exception e) { try { File.Delete(part); } catch { } Set("error", "Couldn't download Cloudflare's tunnel program: " + U.Plain(e) + ". Check the internet connection and try again."); Log.Err("phone", message); return false; } }

    // ---------- requests that arrive through the tunnel ----------
    public static bool IsTunnelHost(string reqHost) {
      var h = host; if (h == null || string.IsNullOrEmpty(reqHost)) return false;
      return reqHost == h || (TestHttp && reqHost == h + ":" + Program.Port); }
    static readonly Dictionary<string, string> SEC = new Dictionary<string, string> { { "X-Content-Type-Options", "nosniff" }, { "X-Frame-Options", "DENY" }, { "Referrer-Policy", "no-referrer" }, { "Cache-Control", "no-store" } };
    public static void Handle(Ctx ctx) {
      var rq = ctx.Request; var path = rq.Url.AbsolutePath; var parts = path.Trim('/').Split('/'); lastActive = DateTime.Now;
      foreach (var kv in SEC) ctx.Response.Headers[kv.Key] = kv.Value;
      if (rq.HttpMethod == "GET" && path == "/health") { Http.Send(ctx, 200, "IXC quick connect ok", "text/plain"); return; }
      if (rq.HttpMethod == "GET" && parts.Length == 2 && parts[0] == "p" && parts[1] == id && !rq.IsWebSocketRequest) {
        var f = Path.Combine(Program.Roots["core"], "phone.html"); if (!File.Exists(f)) { Http.Send(ctx, 404, "not found", "text/plain"); return; }
        var self = Url ?? ""; var wsSelf = Regex.Replace(self, "^http", "ws");
        ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src https: data:; connect-src " + wsSelf + " " + self + "; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        Http.Send(ctx, 200, File.ReadAllText(f), "text/html; charset=utf-8"); return; }
      if (parts.Length == 2 && parts[0] == "phone" && parts[1] == id && rq.IsWebSocketRequest) {
        var origin = rq.Headers["Origin"] ?? ""; if (origin != Url) { Http.Send(ctx, 403, "origin not allowed", "text/plain"); return; }
        Phone(ctx); return; }
      Http.Send(ctx, 404, "not found", "text/plain"); }
    static void Phone(Ctx ctx) {
      var rq = ctx.Request; var ip = rq.Headers["CF-Connecting-IP"] ?? rq.RemoteIp; var ua = rq.Headers["User-Agent"] ?? "";
      var ws = Web.Accept(ctx); var conn = "q:" + U.Token(9);
      if (!Remote.Open(conn, ip, ua, s => ws.SendText(s), () => ws.Abort())) { ws.Abort(); return; }
      Task.Run(async () => {
        try {
          await ws.SendText("{\"relay\":\"pc-online\"}");   // the phone page speaks the relay's protocol: "your PC is here"
          while (ws.Open) { var text = await ws.Receive(65536, 90000); if (text == null) break; lastActive = DateTime.Now;
            if (text == "{\"t\":\"ping\"}") { await ws.SendText("{\"t\":\"pong\"}"); continue; }
            Remote.Incoming(conn, text); } }
        catch { }
        finally { Remote.Closed(conn); ws.Abort(); } }); }

    // ---------- Windows ends cloudflared together with IXC, even after a crash ----------
    static IntPtr job = IntPtr.Zero;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int len);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [StructLayout(LayoutKind.Sequential)] struct JOBOBJECT_BASIC_LIMIT_INFORMATION { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public int LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public int ActiveProcessLimit; public UIntPtr Affinity; public int PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] struct IO_COUNTERS { public ulong a, b, c, d, e, f; }
    [StructLayout(LayoutKind.Sequential)] struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION { public JOBOBJECT_BASIC_LIMIT_INFORMATION Basic; public IO_COUNTERS Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    static void AddToJob(Process p) {
      if (!U.IsWindows) return;
      try { if (job == IntPtr.Zero) { job = CreateJobObject(IntPtr.Zero, null); var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION(); info.Basic.LimitFlags = 0x2000; /* KILL_ON_JOB_CLOSE */
          SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))); }
        AssignProcessToJobObject(job, p.Handle); } catch (Exception e) { Log.Warn("phone", "job object: " + e.Message); } }
  }
}
