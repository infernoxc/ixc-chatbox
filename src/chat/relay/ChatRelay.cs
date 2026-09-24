// IXC ChatBox relay - lets the chat dock (and optionally your phone) SEND chat messages and get !command / @user suggestions.
// Part of IXC ChatBox - Copyright (c) 2026 Ishan (InFerNoxC) - MIT License.
// - holds ONE authenticated connection to Streamer.bot's WebSocket server; the password is read from your config or from
//   Streamer.bot's own settings file on this PC and is never sent to any web page
// - keeps the last 300 chat events so the phone view can show recent history
// - HTTP API:  /  (phone chat page)   /api/events?since=N   POST /api/send   /api/suggest   /api/ping   /api/phone (PC only)
//   Requests from other devices need ?key=<phone key>; requests from this PC do not. By default it only listens on localhost.
using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Net; using System.Net.WebSockets;
using System.Security.Cryptography; using System.Text; using System.Text.RegularExpressions; using System.Threading; using System.Web.Script.Serialization;

public static class ChatRelay {
    static string Root, Key, SbSettings, CmdFile, SbUrl, SbPassword; static string[] Hidden = new string[0]; static bool PhoneEnabled; static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    static ClientWebSocket Sb; static readonly object SbLock = new object(); static bool SbReady;
    static readonly List<KeyValuePair<long, string>> Events = new List<KeyValuePair<long, string>>(); static long Seq;
    static readonly Dictionary<string, string> Replies = new Dictionary<string, string>();
    public static string Log = "";

    public static void Run(string root, string key, string sbUrl, string sbPassword, string sbSettings, string cmdFile, int port, bool phone, string[] hidden) {
        Root = Path.GetFullPath(root); Key = key; SbUrl = sbUrl; SbPassword = sbPassword ?? ""; SbSettings = sbSettings ?? ""; CmdFile = cmdFile ?? ""; PhoneEnabled = phone; Hidden = hidden ?? new string[0];
        new Thread(SbLoop) { IsBackground = true }.Start();
        var l = new HttpListener();
        if (phone) l.Prefixes.Add("http://+:" + port + "/");                    // phone access: needs the URL reservation made by enable-phone-access.ps1
        else { l.Prefixes.Add("http://localhost:" + port + "/"); l.Prefixes.Add("http://127.0.0.1:" + port + "/"); }
        l.Start();
        while (l.IsListening) { var ctx = l.GetContext(); ThreadPool.QueueUserWorkItem(_ => { try { Handle(ctx); } catch (Exception e) { try { Send(ctx, 500, "{\"error\":" + Json.Serialize(e.Message) + "}"); } catch { } } }); }
    }

    // ---------- Streamer.bot ----------
    static void SbLoop() {
        while (true) {
            try {
                var ws = new ClientWebSocket(); ws.ConnectAsync(new Uri(SbUrl), CancellationToken.None).Wait();
                var hello = Json.Deserialize<Dictionary<string, object>>(Recv(ws));
                if (hello.ContainsKey("authentication")) {
                    var a = (Dictionary<string, object>)hello["authentication"];
                    string pw = ReadPassword(); var sha = SHA256.Create();
                    string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(pw + a["salt"])));
                    string auth = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + a["challenge"])));
                    Tx(ws, "{\"request\":\"Authenticate\",\"id\":\"auth\",\"authentication\":\"" + auth + "\"}");
                }
                Tx(ws, "{\"request\":\"Subscribe\",\"id\":\"sub\",\"events\":{\"Twitch\":[\"ChatMessage\",\"ViewerCountUpdate\",\"StreamOffline\"],\"YouTube\":[\"Message\",\"StatisticsUpdated\",\"BroadcastEnded\"],\"Kick\":[\"ChatMessage\",\"ViewerCountUpdate\",\"StreamOffline\"]}}");
                lock (SbLock) { Sb = ws; SbReady = true; } Log = "connected " + DateTime.Now;
                while (ws.State == WebSocketState.Open) {
                    string m = Recv(ws); if (m == null) break;
                    if (m.Contains("\"event\"") && m.Contains("\"data\"")) { lock (Events) { Events.Add(new KeyValuePair<long, string>(++Seq, m)); if (Events.Count > 300) Events.RemoveRange(0, 50); } }
                    else { var mid = Regex.Match(m, "\"id\"\\s*:\\s*\"([^\"]+)\""); if (mid.Success) lock (Replies) Replies[mid.Groups[1].Value] = m; }
                }
            } catch (Exception e) { Log = "sb error " + e.Message; }
            lock (SbLock) { SbReady = false; } Thread.Sleep(3000);
        }
    }
    static string ReadPassword() {
        if (SbPassword.Length > 0) return SbPassword;
        try { if (SbSettings.Length > 0 && File.Exists(SbSettings)) { var s = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(SbSettings)); var w = (Dictionary<string, object>)s["websockets"]; return (string)w["authPassword"]; } } catch { }
        Log = "Streamer.bot asks for a password but none is configured (see docs/CHAT.md)"; return ""; }
    static string Recv(ClientWebSocket ws) { var buf = new byte[65536]; var ms = new MemoryStream(); WebSocketReceiveResult r;
        do { r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result; if (r.MessageType == WebSocketMessageType.Close) return null; ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
        return Encoding.UTF8.GetString(ms.ToArray()); }
    static void Tx(ClientWebSocket ws, string s) { var b = Encoding.UTF8.GetBytes(s); lock (ws) ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait(); }
    static string Request(string json, string id, int ms) {
        ClientWebSocket ws; lock (SbLock) { if (!SbReady) return null; ws = Sb; }
        Tx(ws, json); var until = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < until) { lock (Replies) { string r; if (Replies.TryGetValue(id, out r)) { Replies.Remove(id); return r; } } Thread.Sleep(40); }
        return null; }

    // ---------- HTTP ----------
    static void Handle(HttpListenerContext ctx) {
        var rq = ctx.Request; string path = rq.Url.AbsolutePath;
        bool local = IPAddress.IsLoopback(rq.RemoteEndPoint.Address);
        if (rq.HttpMethod == "OPTIONS") { Send(ctx, 204, ""); return; }
        if (path.StartsWith("/api/") && !local && rq.QueryString["key"] != Key) { Send(ctx, 403, "{\"error\":\"wrong key\"}"); return; }
        if (path == "/api/ping") { string au; lock (Replies) Replies.TryGetValue("auth", out au); Send(ctx, 200, "{\"ok\":true,\"app\":\"ixc-chat-relay\",\"streamerbot\":" + (SbReady ? "true" : "false") + ",\"auth\":" + Json.Serialize(au ?? "none") + ",\"phone\":" + (PhoneEnabled ? "true" : "false") + ",\"log\":" + Json.Serialize(Log) + "}"); return; }
        if (path == "/api/phone") {   // only answered on the PC itself: the phone link (with the key) for the QR code
            if (!local) { Send(ctx, 403, "{}"); return; }
            if (!PhoneEnabled) { Send(ctx, 200, "{\"urls\":[],\"disabled\":true}"); return; }
            var urls = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254"))
                .Select(a => "http://" + a + ":" + rq.Url.Port + "/?key=" + Key).ToList();
            Send(ctx, 200, "{\"urls\":" + Json.Serialize(urls) + "}"); return; }
        if (path == "/api/events") {
            long since; if (!long.TryParse(rq.QueryString["since"], out since)) since = -1; var sb = new StringBuilder(); long seq;
            lock (Events) { seq = Seq; IEnumerable<KeyValuePair<long, string>> list = since < 0 ? Events.Skip(Math.Max(0, Events.Count - 60)) : Events.Where(e => e.Key > since);
                sb.Append(string.Join(",", list.Select(e => e.Value))); }
            Send(ctx, 200, "{\"seq\":" + seq + ",\"events\":[" + sb + "]}"); return; }
        if (path == "/api/send" && rq.HttpMethod == "POST") {
            var body = Json.Deserialize<Dictionary<string, object>>(new StreamReader(rq.InputStream, Encoding.UTF8).ReadToEnd());
            string msg = (Convert.ToString(body["message"]) ?? "").Trim(); string plat = Convert.ToString(body["platform"] ?? "all").ToLower();
            if (msg.Length == 0) { Send(ctx, 400, "{\"error\":\"empty\"}"); return; } if (msg.Length > 480) msg = msg.Substring(0, 480);
            var targets = plat == "all" ? new[] { "twitch", "kick", "youtube" } : new[] { plat }; var res = new List<string>();
            foreach (var p in targets) { string id = "snd" + Guid.NewGuid().ToString("N");
                string r = Request("{\"request\":\"SendMessage\",\"id\":\"" + id + "\",\"platform\":\"" + p + "\",\"message\":" + Json.Serialize(msg) + ",\"bot\":false,\"internal\":true}", id, 5000);
                bool ok = r != null && r.Contains("\"status\":\"ok\""); string err = r == null ? "no answer from Streamer.bot" : Regex.Match(r, "\"error\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
                res.Add("{\"platform\":\"" + p + "\",\"ok\":" + (ok ? "true" : "false") + ",\"error\":" + Json.Serialize(ok ? "" : err) + "}"); }
            Send(ctx, 200, "{\"results\":[" + string.Join(",", res) + "]}"); return; }
        if (path == "/api/suggest") {
            var cmds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string rc = Request("{\"request\":\"GetCommands\",\"id\":\"cmds\"}", "cmds", 3000);
            if (rc != null) foreach (Match m in Regex.Matches(rc, "\"commands\":\\[([^\\]]*)\\]")) foreach (Match c in Regex.Matches(m.Groups[1].Value, "\"(![^\"]+)\"")) cmds.Add(c.Groups[1].Value);
            if (File.Exists(CmdFile)) { string cs = File.ReadAllText(CmdFile);
                foreach (Match m in Regex.Matches(cs, "\\{\\s*\"([a-z0-9]+)\"\\s*,")) cmds.Add("!" + m.Groups[1].Value);
                foreach (Match m in Regex.Matches(cs, "case\\s+\"([a-z0-9]+)\"\\s*:\\s*(?:Reply|return|if|Handle|var|string|\\{)")) cmds.Add("!" + m.Groups[1].Value); }
            foreach (var h in Hidden) cmds.Remove(h.StartsWith("!") ? h : "!" + h);
            string rv = Request("{\"request\":\"GetActiveViewers\",\"id\":\"viewers\"}", "viewers", 3000);
            var users = new List<string>();
            if (rv != null) foreach (Match m in Regex.Matches(rv, "\"type\":\"(\\w+)\",\"login\":\"[^\"]*\",\"display\":\"([^\"]+)\"")) users.Add("{\"name\":" + Json.Serialize(m.Groups[2].Value) + ",\"platform\":\"" + m.Groups[1].Value + "\"}");
            Send(ctx, 200, "{\"commands\":" + Json.Serialize(cmds.ToList()) + ",\"users\":[" + string.Join(",", users) + "]}"); return; }
        // static files from the chat folder (phone page)
        string rel = Uri.UnescapeDataString(path.TrimStart('/')); if (rel == "") rel = "chat.html";
        string f = Path.GetFullPath(Path.Combine(Root, rel));
        if (!f.StartsWith(Root, StringComparison.OrdinalIgnoreCase) || !File.Exists(f) || !(f.EndsWith(".html") || f.EndsWith(".js") || f.EndsWith(".png") || f.EndsWith(".css") || f.EndsWith(".ico") || f.EndsWith(".json"))) { Send(ctx, 404, "not found"); return; }
        var bytes = File.ReadAllBytes(f); string type = f.EndsWith(".html") ? "text/html; charset=utf-8" : f.EndsWith(".js") ? "text/javascript" : f.EndsWith(".png") ? "image/png" : f.EndsWith(".json") ? "application/json" : "text/css";
        SendBytes(ctx, 200, bytes, type);
    }
    static void Send(HttpListenerContext c, int code, string s) { SendBytes(c, code, Encoding.UTF8.GetBytes(s), "application/json; charset=utf-8"); }
    static void SendBytes(HttpListenerContext c, int code, byte[] b, string type) { var r = c.Response; r.StatusCode = code; r.ContentType = type;
        r.Headers["Access-Control-Allow-Origin"] = "*"; r.Headers["Access-Control-Allow-Headers"] = "Content-Type"; r.Headers["Cache-Control"] = "no-store";
        try { r.OutputStream.Write(b, 0, b.Length); } catch { } r.Close(); }
}
