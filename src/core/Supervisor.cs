// IXC Core - the tray icon and supervisor (Windows). It runs the worker ("ixc-core.exe --worker"), restarts it if it crashes
// (at most 3 times in 10 minutes, then it asks you instead of looping), and gives you Open / Restart / Quit in the tray.
// "Start and stop with OBS" (general.followObs, on by default): at Windows sign-in only this small tray process starts
// ("--background"); it starts IXC when OBS opens and stops it general.obsGraceSec after OBS closes (a quick OBS restart
// doesn't stop it). Opening IXC yourself starts it at once, OBS or not. One copy only (named mutex).
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace IXC {
  public static class Supervisor {
    static NotifyIcon tray; static Process worker; static readonly List<DateTime> crashes = new List<DateTime>(); static volatile bool quitting, stopped;
    static string exe, cfgPath; static SynchronizationContext ui; static ToolStripMenuItem restartItem; static int port = 8767;
    static volatile bool sleeping, obsWas; static DateTime obsGoneAt = DateTime.MinValue; static readonly object WL = new object();
    public static int Run(string[] args, string config) {
      bool created; var mutex = new Mutex(true, "Local\\IXC.Supervisor", out created);
      cfgPath = config; exe = Process.GetCurrentProcess().MainModule.FileName;
      try { var d = J.Parse(File.ReadAllText(cfgPath)); port = J.Int(d, "helper.port", 8767); } catch { }
      var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\IXC.Open");
      if (!created) { wake.Set(); return 0; }   // already running: it starts IXC if it waits for OBS, then shows the dashboard
      bool background = args.Contains("--background");
      Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
      ui = new WindowsFormsSynchronizationContext(); SynchronizationContext.SetSynchronizationContext(ui);
      var menu = new ContextMenuStrip();
      menu.Items.Add("Open IXC", null, (s, e) => WakeAndOpen(""));
      menu.Items.Add("System check", null, (s, e) => OpenDashboard("#check"));
      menu.Items.Add("Connect phone", null, (s, e) => OpenDashboard("#phone"));
      menu.Items.Add(new ToolStripSeparator());
      restartItem = new ToolStripMenuItem("Restart IXC", null, (s, e) => Restart("restarted from the tray")); menu.Items.Add(restartItem);
      menu.Items.Add("Open logs folder", null, (s, e) => { try { Process.Start(Path.Combine(Path.GetDirectoryName(cfgPath), "logs")); } catch { } });
      menu.Items.Add(new ToolStripSeparator());
      menu.Items.Add("Quit IXC", null, (s, e) => Quit());
      tray = new NotifyIcon { Text = "IXC - starting", Icon = LoadIcon(), Visible = true, ContextMenuStrip = menu };
      tray.DoubleClick += (s, e) => WakeAndOpen("");
      tray.BalloonTipClicked += (s, e) => { if (stopped) Restart("restarted after a problem"); else WakeAndOpen(""); };
      obsWas = ObsRunning();
      if (background && FollowObs() && !obsWas) { sleeping = true; SetTip("IXC - starts when OBS opens"); } else StartWorker();
      new Thread(() => { while (true) { wake.WaitOne(); WakeAndOpen(""); } }) { IsBackground = true }.Start();
      new Thread(Watch) { IsBackground = true }.Start();
      // first run (or an update that needs attention): open the dashboard once IXC answers
      if (!sleeping) new Thread(() => { for (int i = 0; i < 40; i++) { Thread.Sleep(500); var r = Http.Get("http://127.0.0.1:" + Port() + "/api/status", 2000); if (r.Ok) { var d = J.Parse(r.Body); SetTip("IXC " + J.Str(d, "version", "")); if (J.Bool(d, "firstRun", false) || J.Bool(J.Parse(Read()), "general.openDashboardOnStart", false)) OpenDashboard(""); return; } } }) { IsBackground = true }.Start();
      Application.Run(); GC.KeepAlive(mutex); return 0; }
    static string Read() { try { return File.ReadAllText(cfgPath); } catch { return "{}"; } }
    static int Port() { try { return J.Int(J.Parse(Read()), "helper.port", port); } catch { return port; } }
    static Icon LoadIcon() { try { var f = Path.Combine(Path.GetDirectoryName(exe), "ixc.ico"); if (File.Exists(f)) return new Icon(f); } catch { } return SystemIcons.Application; }
    static void SetTip(string t) { ui.Post(_ => { if (tray != null) tray.Text = t.Length > 63 ? t.Substring(0, 63) : t; }, null); }
    static bool FollowObs() { try { return J.Bool(J.Parse(Read()), "general.followObs", true); } catch { return true; } }
    static int Grace() { try { return Math.Max(0, J.Int(J.Parse(Read()), "general.obsGraceSec", 20)); } catch { return 20; } }
    static bool ObsRunning() { try { return Process.GetProcessesByName("obs64").Length > 0 || Process.GetProcessesByName("obs").Length > 0 || Process.GetProcessesByName("obs32").Length > 0; } catch { return false; } }
    static bool WorkerAlive() { try { return worker != null && !worker.HasExited; } catch { return false; } }
    // every 3 s: OBS opened -> start IXC; OBS closed (and still closed after the grace time) -> stop IXC, keep the tray
    static void Watch() {
      while (true) {
        Thread.Sleep(3000);
        try {
          bool run = ObsRunning(); bool follow = FollowObs();
          lock (WL) {
            if (!follow && sleeping) { sleeping = false; Log("start and stop with OBS was switched off - starting IXC"); StartWorker(); }
            if (run) { obsGoneAt = DateTime.MinValue; if (sleeping) { sleeping = false; Log("OBS opened - starting IXC"); StartWorker(); } }
            else if (obsWas) obsGoneAt = DateTime.Now;   // OBS just closed
            obsWas = run;
            if (follow && !run && obsGoneAt != DateTime.MinValue && (DateTime.Now - obsGoneAt).TotalSeconds >= Grace() && WorkerAlive() && !stopped) {
              obsGoneAt = DateTime.MinValue; Log("OBS closed - stopping IXC until OBS opens again"); Sleep(); } } }
        catch (Exception e) { Log("watch: " + e.Message); } } }
    // stop the worker but keep the tray (and wait for OBS)
    static void Sleep() {
      sleeping = true; quitting = true;
      try { Http.Request("POST", "http://127.0.0.1:" + Port() + "/api/system/quit", "{}", "application/json", null, 2000); if (worker != null && !worker.WaitForExit(5000)) worker.Kill(); } catch { }
      quitting = false; SetTip("IXC - starts when OBS opens"); }
    static void WakeAndOpen(string hash) {
      lock (WL) { if (sleeping || (!WorkerAlive() && !stopped)) { sleeping = false; Log("opened by you - starting IXC"); StartWorker(); } }
      for (int i = 0; i < 30; i++) { if (Http.Get("http://127.0.0.1:" + Port() + "/api/status", 1500).Ok) break; Thread.Sleep(500); }
      OpenDashboard(hash); }
    // one line per lifecycle change in logs\supervisor.log (the worker's own log is separate)
    static void Log(string msg) {
      try { var dir = Path.Combine(Path.GetDirectoryName(cfgPath), "logs"); Directory.CreateDirectory(dir); var f = Path.Combine(dir, "supervisor.log");
        if (File.Exists(f) && new FileInfo(f).Length > 200000) File.Delete(f); File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine); } catch { } }
    static void StartWorker() {
      stopped = false; sleeping = false; if (WorkerAlive()) return;
      var psi = new ProcessStartInfo(exe, "--worker --config \"" + cfgPath + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe) };
      try { worker = Process.Start(psi); worker.EnableRaisingEvents = true; worker.Exited += (s, e) => OnExit(); }
      catch (Exception e) { Balloon("IXC could not start", e.Message, ToolTipIcon.Error); stopped = true; } }
    static void OnExit() {
      if (quitting) return; int code; try { code = worker.ExitCode; } catch { code = -1; }
      if (code == 3) { StartWorker(); return; }                  // restart asked for (settings reset, backup restore, update)
      if (code == 4 || code == 0) { if (sleeping) return; ui.Post(_ => Quit(), null); return; }   // quit asked for / another copy was already running
      lock (crashes) { crashes.Add(DateTime.Now); crashes.RemoveAll(t => (DateTime.Now - t).TotalMinutes > 10); }
      bool auto = true; try { auto = J.Bool(J.Parse(Read()), "general.autoRestart", true); } catch { }
      int n; lock (crashes) n = crashes.Count;
      if (auto && n <= 3) { Balloon("IXC restarted", "IXC stopped unexpectedly and was restarted. Your settings and queue are safe.", ToolTipIcon.Warning); Thread.Sleep(n * 2000); StartWorker(); return; }
      stopped = true; SetTip("IXC stopped - right-click > Restart");
      Balloon("IXC stopped unexpectedly", "Click here to restart IXC. If this keeps happening, open IXC > System check > Export diagnostics.", ToolTipIcon.Error); }
    static void Restart(string why) {
      quitting = true; try { if (worker != null && !worker.HasExited) { worker.Kill(); worker.WaitForExit(5000); } } catch { } quitting = false;
      lock (crashes) crashes.Clear(); StartWorker(); }
    static void Quit() {
      quitting = true; try { Http.Request("POST", "http://127.0.0.1:" + Port() + "/api/system/quit", "{}", "application/json", null, 2000); if (worker != null && !worker.WaitForExit(4000)) worker.Kill(); } catch { }
      if (tray != null) { tray.Visible = false; tray.Dispose(); } Application.Exit(); }
    static void Balloon(string title, string text, ToolTipIcon icon) { ui.Post(_ => { try { tray.ShowBalloonTip(8000, title, text, icon); } catch { } }, null); }
    // the dashboard opens as its own app window (Microsoft Edge "app" mode, built into Windows 10/11), else the default browser
    public static void OpenDashboard(string hash) {
      var url = "http://localhost:" + Port() + "/app/" + hash;
      foreach (var edge in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)") + "\\Microsoft\\Edge\\Application\\msedge.exe", Environment.GetEnvironmentVariable("ProgramFiles") + "\\Microsoft\\Edge\\Application\\msedge.exe" }) {
        try { if (File.Exists(edge)) { Process.Start(new ProcessStartInfo(edge, "--app=\"" + url + "\" --window-size=1280,860") { UseShellExecute = false }); return; } } catch { } }
      try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
  }
}
