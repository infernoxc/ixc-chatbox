// IXC Core - outgoing WebSocket connections (OBS, Streamer.bot, Twitch, Kick, the phone relay). Every link reconnects by
// itself with exponential back-off, is disposed properly after every failure, and wakes up at once when the network
// comes back or the PC resumes from sleep.
// Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IXC {
  public abstract class WsLink {
    public string Name = "link", Area = "net";
    public volatile bool Ready; public volatile string State = "off"; public string Detail = ""; public DateTime Since = DateTime.Now; public int Connects, Failures;
    public DateTime LastMessage = DateTime.MinValue;
    protected volatile ClientWebSocket ws; readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
    protected readonly Dictionary<string, TaskCompletionSource<Dictionary<string, object>>> Pending = new Dictionary<string, TaskCompletionSource<Dictionary<string, object>>>();
    readonly AutoResetEvent wake = new AutoResetEvent(false); int idN; Thread thread; volatile bool restart;
    protected int IdleTimeoutSec = 0;      // >0: no message for this long = dead connection (reconnect)
    protected int MaxBackoffMs = 60000;
    protected abstract string Url();
    protected virtual Dictionary<string, string> RequestHeaders() { return null; }
    protected abstract Task Handshake();
    // called before each connection attempt: reset "the server said hello" flags here, not in Handshake - servers that greet at once
    // (Kick, Twitch EventSub) can do it before Handshake runs, and a reset there would throw their hello away
    protected virtual void BeforeConnect() { }
    protected abstract void OnText(string raw);
    protected virtual bool Wanted() { return true; }
    protected virtual string WhyNotWanted() { return "turned off"; }
    protected virtual void OnConnected() { }
    protected virtual void OnDisconnected() { }
    protected virtual string Friendly(Exception e) { return U.Plain(e); }

    public void Start() { if (thread != null) return; NetWatch.Register(this); thread = new Thread(Loop) { IsBackground = true, Name = Name }; thread.Start(); }
    public void Kick() { wake.Set(); }                                      // retry now (network back, user pressed "Reconnect")
    public void Restart() { restart = true; var w = ws; if (w != null) { try { w.Abort(); } catch { } } wake.Set(); }   // settings changed: drop and reconnect
    protected void SetState(string state, string detail) { if (State != state || Detail != detail) { State = state; Detail = detail ?? ""; Since = DateTime.Now; StateChanged(); } }
    protected virtual void StateChanged() { }

    void Loop() {
      while (true) {
        if (!Wanted()) { if (Ready) Ready = false; SetState("off", WhyNotWanted()); wake.WaitOne(5000); continue; }
        restart = false; bool connected = false; DateTime start = DateTime.Now; string err = null;
        try { RunOnce(ref connected); } catch (Exception e) { err = Friendly(e); }
        Ready = false; lock (Pending) { foreach (var t in Pending.Values) t.TrySetResult(null); Pending.Clear(); }
        if (connected) { try { OnDisconnected(); } catch (Exception e) { Log.Err(Area, Name + " disconnect handler: " + e.Message); } }
        if (connected && (DateTime.Now - start).TotalSeconds > 30) Failures = 0;
        if (restart) { Failures = 0; continue; }
        if (!Wanted()) continue;
        Failures++; if (connected) Interlocked.Increment(ref Log.Reconnects);
        var delay = NextDelay(err);
        if (err != null && State != "auth" && State != "unavailable" && State != "ratelimited") SetState("reconnecting", err);
        else if (err == null && State == "connected") SetState("reconnecting", "connection closed");
        if (Failures <= 2 || Failures % 10 == 0) Log.Info(Area, Name + " " + (connected ? "disconnected" : "could not connect") + (err != null ? " (" + err + ")" : "") + " - retrying in " + Math.Max(1, (delay + 500) / 1000) + " s");
        wake.WaitOne(delay); }
    }
    protected virtual int NextDelay(string err) {
      if (State == "auth") return 300000;                                    // waiting for the user to sign in again: don't hammer
      if (State == "unavailable") return Math.Max(30000, U.Backoff(Failures, 1000, MaxBackoffMs));
      return U.Backoff(Failures - 1, 1000, MaxBackoffMs); }

    void RunOnce(ref bool connected) {
      using (var w = new ClientWebSocket()) {
        w.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        var hs = RequestHeaders(); if (hs != null) foreach (var kv in hs) w.Options.SetRequestHeader(kv.Key, kv.Value);
        SetState(Failures == 0 ? "connecting" : "reconnecting", Detail);
        BeforeConnect();
        using (var cts = new CancellationTokenSource(10000)) {
          try { w.ConnectAsync(new Uri(Url()), cts.Token).Wait(); }
          catch (Exception e) { if (cts.IsCancellationRequested) throw new Exception("no answer (timed out)"); throw new Exception(Friendly(e)); } }
        ws = w; LastMessage = DateTime.Now; var recv = RecvLoop(w);
        try { var h = Handshake(); if (!h.Wait(15000)) throw new Exception("no answer during sign-in"); if (h.IsFaulted) throw h.Exception; }
        catch { try { w.Abort(); } catch { } try { recv.Wait(2000); } catch { } throw; }
        connected = true; Connects++; Ready = true; SetState("connected", ""); Log.Info(Area, Name + " connected");
        try { OnConnected(); } catch (Exception e) { Log.Err(Area, Name + " connect handler: " + e.Message); }
        while (!recv.Wait(5000)) {
          if (restart) { try { w.Abort(); } catch { } }
          else if (IdleTimeoutSec > 0 && (DateTime.Now - LastMessage).TotalSeconds > IdleTimeoutSec) { Log.Info(Area, Name + ": no data for " + IdleTimeoutSec + " s - reconnecting"); try { w.Abort(); } catch { } }
          else if (!Wanted()) { try { w.Abort(); } catch { } } }
        if (recv.IsFaulted && !restart) { var e = recv.Exception.InnerException; if (e != null && !(e is WebSocketException) && !(e is ObjectDisposedException) && !(e is OperationCanceledException)) throw e; }
        ws = null; } }
    async Task RecvLoop(ClientWebSocket w) {
      var buf = new byte[65536]; var ms = new MemoryStream();
      while (w.State == WebSocketState.Open) {
        ms.SetLength(0); WebSocketReceiveResult r;
        do { r = await w.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None); if (r.MessageType == WebSocketMessageType.Close) return; ms.Write(buf, 0, r.Count); if (ms.Length > 16000000) throw new Exception("message too large"); } while (!r.EndOfMessage);
        LastMessage = DateTime.Now; var raw = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        try { OnText(raw); } catch (Exception e) { Log.Err(Area, Name + " message: " + e.Message); } } }

    public async Task Tx(string text) {
      var w = ws; if (w == null || w.State != WebSocketState.Open) throw new Exception(Name + " is not connected");
      var b = Encoding.UTF8.GetBytes(text); if (!await sendLock.WaitAsync(10000)) throw new Exception(Name + " is busy");
      try { await w.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None); } finally { sendLock.Release(); } }
    protected string NewId() { return "ixc" + Interlocked.Increment(ref idN); }
    protected void Complete(string id, Dictionary<string, object> m) { TaskCompletionSource<Dictionary<string, object>> t; lock (Pending) { if (!Pending.TryGetValue(id, out t)) return; Pending.Remove(id); } t.TrySetResult(m); }
    protected async Task<Dictionary<string, object>> Ask(string id, string json, int ms) {
      var t = new TaskCompletionSource<Dictionary<string, object>>(); lock (Pending) Pending[id] = t;
      try { await Tx(json); } catch { lock (Pending) Pending.Remove(id); return null; }
      if (await Task.WhenAny(t.Task, Task.Delay(ms)) != t.Task) { lock (Pending) Pending.Remove(id); return null; }
      return t.Task.Result; }
    public Dictionary<string, object> StatusInfo() { return J.D("state", State, "detail", Detail, "since", Since.ToString("HH:mm:ss"), "connects", Connects); }
  }

  // network changes and sleep/wake: wake every link and poller at once instead of waiting for the back-off
  public static class NetWatch {
    static readonly List<WsLink> links = new List<WsLink>(); public static event Action Woke; static Timer t; static DateTime last = DateTime.Now;
    public static DateTime LastWake = DateTime.MinValue; public static string LastReason = "";
    public static void Register(WsLink l) { lock (links) links.Add(l); }
    public static void Init() {
      try { NetworkChange.NetworkAvailabilityChanged += (s, e) => { if (e.IsAvailable) Wake("network is back"); }; NetworkChange.NetworkAddressChanged += (s, e) => Wake("network changed"); } catch (Exception e) { Log.Warn("net", "network change events unavailable: " + e.Message); }
      // a timer that suddenly fires much later than planned = the PC slept
      t = new Timer(_ => { var now = DateTime.Now; var gap = (now - last).TotalSeconds; last = now; if (gap > 30) Wake("PC woke up from sleep"); }, null, 5000, 5000); }
    static DateTime lastWake = DateTime.MinValue;
    public static void Wake(string why) {
      if ((DateTime.Now - lastWake).TotalSeconds < 3) return; lastWake = DateTime.Now; LastWake = lastWake; LastReason = why;
      Log.Info("net", why + " - reconnecting everything now");
      List<WsLink> l; lock (links) l = links.ToList(); foreach (var x in l) x.Kick();
      if (Woke != null) foreach (Action h in Woke.GetInvocationList()) { try { h(); } catch { } } }
  }
}
