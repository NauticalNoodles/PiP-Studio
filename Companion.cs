using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PipStudio
{
    /// <summary>
    /// Phone companion (Settings > General > Phone companion): control PiPs and every app's volume from a phone's browser.
    /// - Off by default. When on, PiP Studio listens on one port (48765, or the next free one) and answers only devices on
    ///   the local network (private and link-local addresses); nothing goes over the internet.
    /// - Pairing works like Moonlight and Sunshine: the phone shows a 6-digit PIN and you type it into PiP Studio's Pair a
    ///   phone dialog (phones can only start pairing while it's open). Behind the PIN is an X25519 key exchange, so the PIN
    ///   only matches when nobody sat in between (CompanionCrypto). Each phone then has its own key; PiP Studio keeps it
    ///   locked to your Windows account (DPAPI), and Remove forgets it.
    /// - Every request after that is encrypted and authenticated (ChaCha20-Poly1305) through /api/call, answers too, with
    ///   a timestamp and a list of recent nonces so old messages can't be replayed.
    /// - The phone page (companion\index.html + crypto.js) is served by PiP Studio itself and asks for the state about once
    ///   a second. It travels unencrypted (the browser needs it before any keys exist), so someone able to tamper with your
    ///   Wi-Fi traffic could swap the page itself; reading or forging the messages isn't possible.
    /// - A plain TcpListener with a tiny HTTP/1.1 reader, because HttpListener needs administrator rights to listen on the
    ///   network. Requests are small JSON; everything that touches PiPs or audio runs on the UI thread.
    /// </summary>
    static class Companion
    {
        const int DefaultPort = 48765, MaxHeader = 16 * 1024, MaxBody = 64 * 1024;

        static TcpListener listener;
        static CancellationTokenSource stop;
        static readonly object gate = new object();

        /// <summary>The port it's listening on, or 0 when it's off.</summary>
        public static int Port { get; private set; }
        public static bool Running { get { return Port != 0; } }
        /// <summary>Why it couldn't start, for the settings page.</summary>
        public static string Problem { get; private set; }
        /// <summary>Raised on the UI thread when a phone pairs, or its list changes.</summary>
        public static event Action Changed;

        // ------------------------------------------------------------ on and off
        /// <summary>Starts or stops listening to match the setting (at start-up and when the switch changes).</summary>
        public static void Apply()
        {
            if (App.S.CompanionOn && !Packaged.IsPackaged) Start(); else Stop();
        }

        static void Start()
        {
            if (Running) return;
            Problem = null;
            int want = App.S.CompanionPort > 0 ? App.S.CompanionPort : DefaultPort;
            for (int p = want; p < want + 10; p++)
            {
                listener = Listen(p);
                if (listener != null) { Port = p; break; }
            }
            if (!Running) { Problem = "Couldn't open a network port. Another app may be using it."; Raise(); return; }
            if (App.S.CompanionPort != Port) { App.S.CompanionPort = Port; App.I.SaveSettingsSoon(); }   // keep the same address next time
            stop = new CancellationTokenSource();
            var token = stop.Token;
            var l2 = listener;
            Task.Run(() => AcceptLoop(l2, token));
            Raise();
        }

        /// <summary>IPv4 and IPv6 on one socket, or IPv4 alone on PCs with IPv6 turned off. Null when the port is taken.</summary>
        static TcpListener Listen(int port)
        {
            if (Socket.OSSupportsIPv6)
            {
                try
                {
                    var l = new TcpListener(IPAddress.IPv6Any, port);
                    l.Server.DualMode = true;
                    l.Start();
                    return l;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse) { return null; }
                catch (SocketException) { }                                     // IPv6 is there but unusable: plain IPv4
            }
            try
            {
                var l4 = new TcpListener(IPAddress.Any, port);
                l4.Start();
                return l4;
            }
            catch (SocketException) { return null; }
        }

        public static void Stop()
        {
            if (!Running) return;
            try { stop.Cancel(); } catch { }
            try { listener.Stop(); } catch { }
            listener = null;
            Port = 0;
            EndPairing();
            Raise();
        }

        static void Raise()
        {
            var h = Changed;
            if (h == null) return;
            OnUi(() => { h(); return 0; });
        }

        // ------------------------------------------------------------ the address phones use
        /// <summary>This PC's address on the local network (the network with the default route), or null.</summary>
        public static string LocalAddress()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var ip = ni.GetIPProperties();
                    if (!ip.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
                    var a = ip.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork && IsLocal(u.Address));
                    if (a != null) return a.Address.ToString();
                }
            }
            catch { }
            return null;
        }

        /// <summary>The page's address, e.g. http://192.168.1.20:48765/</summary>
        public static string Url { get { string a = LocalAddress(); return a == null || !Running ? null : "http://" + a + ":" + Port + "/"; } }

        /// <summary>Private, link-local or loopback: a device on this network, not the internet.</summary>
        static bool IsLocal(IPAddress a)
        {
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            if (IPAddress.IsLoopback(a)) return true;
            if (a.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = a.GetAddressBytes();
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
            }
            if (a.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var b = a.GetAddressBytes();
                return a.IsIPv6LinkLocal || (b[0] & 0xFE) == 0xFC;                   // fe80::/10, fc00::/7
            }
            return false;
        }

        // ------------------------------------------------------------ pairing (see CompanionCrypto for how the PIN works)
        static bool pairingOpen;
        static readonly List<PairSession> sessions = new List<PairSession>();
        static int wrongPins;
        static readonly Dictionary<string, DateTime> lastSeen = new Dictionary<string, DateTime>();
        static readonly Dictionary<string, List<DateTime>> failures = new Dictionary<string, List<DateTime>>();
        static readonly Dictionary<string, byte[]> keyCache = new Dictionary<string, byte[]>();

        /// <summary>One phone partway through pairing.</summary>
        class PairSession
        {
            public string Id, Name, Pin, DeviceId;
            public byte[] Commit, PcPriv, PcPub, PcNonce, Key;
            public DateTime Started = DateTime.UtcNow;
            public bool Expired { get { return (DateTime.UtcNow - Started).TotalMinutes > 10; } }
        }

        /// <summary>A paired phone. Its key is kept locked to this Windows account (DPAPI).</summary>
        public class Device { public string Id, Sealed, Name; public DateTime? LastSeen; }

        /// <summary>The Pair a phone dialog opened: phones may start pairing until EndPairing.</summary>
        public static void BeginPairing() { lock (gate) { pairingOpen = true; sessions.Clear(); wrongPins = 0; } }

        public static void EndPairing() { lock (gate) { pairingOpen = false; sessions.Clear(); } }

        public enum PinResult { Paired, Wrong, NoPhone, TooMany }

        /// <summary>You typed the PIN a phone shows. The phone whose exchange gives that PIN is paired.</summary>
        public static PinResult ConfirmPin(string pin, out string phoneName)
        {
            phoneName = null;
            pin = new string((pin ?? "").Where(char.IsDigit).ToArray());
            PairSession match = null;
            lock (gate)
            {
                sessions.RemoveAll(x => x.Expired);
                var waiting = sessions.Where(x => x.Pin != null && x.DeviceId == null).ToList();
                if (waiting.Count == 0) return PinResult.NoPhone;
                var hits = waiting.Where(x => SameText(x.Pin, pin)).ToList();
                if (hits.Count != 1)                                                // no match (or, absurdly, two): not paired
                {
                    if (++wrongPins >= 5) { sessions.Clear(); wrongPins = 0; return PinResult.TooMany; }
                    return PinResult.Wrong;
                }
                match = hits[0];
                match.DeviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
                foreach (var other in waiting) if (other != match) sessions.Remove(other);   // one phone per PIN typed
            }
            var d = new Device { Id = match.DeviceId, Sealed = CompanionCrypto.Protect(match.Key), Name = match.Name };
            var l = Devices(); l.Add(d); SaveDevices(l);
            lock (gate) { keyCache[d.Id] = match.Key; lastSeen[d.Id] = DateTime.Now; }
            phoneName = d.Name;
            Raise();
            return PinResult.Paired;
        }

        public static List<Device> Devices()
        {
            var l = new List<Device>();
            foreach (var part in (App.S.CompanionDevices ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = part.Split('|');
                if (f.Length < 3) continue;
                DateTime seen;
                bool known;
                lock (gate) known = lastSeen.TryGetValue(f[0], out seen);
                l.Add(new Device { Id = f[0], Sealed = f[1], Name = Uri.UnescapeDataString(f[2]), LastSeen = known ? seen : (DateTime?)null });
            }
            return l;
        }

        static void SaveDevices(List<Device> l)
        {
            App.S.CompanionDevices = string.Join(";", l.Select(d => d.Id + "|" + d.Sealed + "|" + Uri.EscapeDataString(d.Name ?? "Phone")));
            App.I.SaveSettingsSoon();
        }

        /// <summary>Forgets a phone: its key stops working right away.</summary>
        public static void Remove(string id)
        {
            OnUi(() => { SaveDevices(Devices().Where(d => d.Id != id).ToList()); return 0; });
            lock (gate) keyCache.Remove(id);
            Raise();
        }

        /// <summary>A paired phone's key, or null.</summary>
        static byte[] KeyFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (gate) { byte[] k; if (keyCache.TryGetValue(id, out k)) return k; }
            var d = OnUi(Devices).FirstOrDefault(x => x.Id == id);
            var key = d == null ? null : CompanionCrypto.Unprotect(d.Sealed);
            if (key != null) lock (gate) keyCache[id] = key;
            return key;
        }

        static bool SameText(string a, string b)
        {
            return a != null && b != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
        }

        /// <summary>Too many wrong keys or codes from one address: wait a minute (guessing is pointless anyway, but cheap to stop).</summary>
        static bool Blocked(string ip)
        {
            lock (gate)
            {
                List<DateTime> l;
                if (!failures.TryGetValue(ip, out l)) return false;
                l.RemoveAll(t => (DateTime.UtcNow - t).TotalMinutes > 1);
                return l.Count >= 10;
            }
        }

        static void Failed(string ip)
        {
            lock (gate)
            {
                List<DateTime> l;
                if (!failures.TryGetValue(ip, out l)) failures[ip] = l = new List<DateTime>();
                l.Add(DateTime.UtcNow);
            }
        }

        // ------------------------------------------------------------ HTTP
        class Request { public string Method, Path, Host, Ip; public byte[] Body; }

        static async Task AcceptLoop(TcpListener l, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await l.AcceptTcpClientAsync(token); }
                catch { break; }
                _ = Task.Run(() => Serve(c));
            }
        }

        static async Task Serve(TcpClient c)
        {
            using (c)
            {
                try
                {
                    var ep = c.Client.RemoteEndPoint as IPEndPoint;
                    if (ep == null || !IsLocal(ep.Address)) return;                  // not from this network: no answer at all
                    c.ReceiveTimeout = c.SendTimeout = 5000;
                    var s = c.GetStream();
                    var req = await Read(s);
                    if (req == null) return;
                    req.Ip = ep.Address.ToString();
                    await Respond(s, req);
                }
                catch { }
            }
        }

        static async Task<Request> Read(NetworkStream s)
        {
            var head = new MemoryStream();
            var one = new byte[1];
            int match = 0;
            using (var cts = new CancellationTokenSource(5000))
            {
                while (match < 4)                                                     // up to the blank line after the headers
                {
                    int n = await s.ReadAsync(one, 0, 1, cts.Token);
                    if (n == 0 || head.Length > MaxHeader) return null;
                    head.WriteByte(one[0]);
                    match = (one[0] == (match % 2 == 0 ? '\r' : '\n')) ? match + 1 : (one[0] == '\r' ? 1 : 0);
                }
                var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                var first = lines[0].Split(' ');
                if (first.Length < 3) return null;
                var r = new Request { Method = first[0].ToUpperInvariant(), Path = first[1] };
                int len = 0;
                foreach (var line in lines.Skip(1))
                {
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string k = line.Substring(0, colon).Trim().ToLowerInvariant(), v = line.Substring(colon + 1).Trim();
                    if (k == "host") r.Host = v;
                    else if (k == "content-length") int.TryParse(v, out len);
                }
                if (len < 0 || len > MaxBody) return null;
                r.Body = new byte[len];
                int got = 0;
                while (got < len)
                {
                    int n = await s.ReadAsync(r.Body, got, len - got, cts.Token);
                    if (n == 0) return null;
                    got += n;
                }
                return r;
            }
        }

        static async Task Send(NetworkStream s, int code, string type, byte[] body, bool cache = false)
        {
            string status = code == 200 ? "OK" : code == 401 ? "Unauthorized" : code == 403 ? "Forbidden" : code == 404 ? "Not Found" : code == 429 ? "Too Many Requests" : "Bad Request";
            var h = new StringBuilder();
            h.Append("HTTP/1.1 ").Append(code).Append(' ').Append(status).Append("\r\n");
            h.Append("Content-Type: ").Append(type).Append("\r\n");
            h.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            h.Append("Cache-Control: ").Append(cache ? "max-age=3600" : "no-store").Append("\r\n");
            h.Append("X-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nX-Frame-Options: DENY\r\n");
            h.Append("Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'\r\n");
            h.Append("Connection: close\r\n\r\n");
            var hb = Encoding.ASCII.GetBytes(h.ToString());
            await s.WriteAsync(hb, 0, hb.Length);
            await s.WriteAsync(body, 0, body.Length);
        }

        static Task Json(NetworkStream s, object o, int code = 200) { return Send(s, code, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(o)); }

        static async Task Respond(NetworkStream s, Request r)
        {
            // only answer to this PC's address, never a name: a web page can't point a name it controls at this PC (DNS rebinding)
            string host = r.Host ?? "";
            if (host.StartsWith("[")) host = host.Substring(1, Math.Max(0, host.IndexOf(']') - 1));
            else if (host.Count(ch => ch == ':') == 1) host = host.Substring(0, host.IndexOf(':'));
            IPAddress hostIp;
            if (!IPAddress.TryParse(host, out hostIp) && host != "localhost") { await Send(s, 403, "text/plain", Encoding.UTF8.GetBytes("Use the address PiP Studio shows.")); return; }

            string path = r.Path.Split('?')[0];
            if (r.Method == "GET" && (path == "/" || path == "/index.html")) { await Send(s, 200, "text/html; charset=utf-8", Resource("companion.index.html")); return; }
            if (r.Method == "GET" && path == "/crypto.js") { await Send(s, 200, "text/javascript; charset=utf-8", Resource("companion.crypto.js")); return; }
            if (r.Method == "GET" && path == "/icon.png") { await Send(s, 200, "image/png", Resource("companion.icon.png"), true); return; }
            if (r.Method == "GET" && path == "/manifest.webmanifest")
            {
                var m = new Dictionary<string, object>
                {
                    ["name"] = "PiP Studio", ["short_name"] = "PiP Studio", ["start_url"] = "/", ["display"] = "standalone",
                    ["background_color"] = "#0b0e22", ["theme_color"] = "#0b0e22",
                    ["icons"] = new[] { new Dictionary<string, string> { ["src"] = "/icon.png", ["sizes"] = "256x256", ["type"] = "image/png" } }
                };
                await Send(s, 200, "application/manifest+json", JsonSerializer.SerializeToUtf8Bytes(m), true);
                return;
            }
            if (!path.StartsWith("/api/")) { await Send(s, 404, "text/plain", Encoding.UTF8.GetBytes("Not found")); return; }
            if (Blocked(r.Ip)) { await Json(s, new { error = "Too many tries. Wait a minute." }, 429); return; }

            JsonElement body = default(JsonElement);
            if (r.Body.Length > 0)
            {
                try { body = JsonDocument.Parse(r.Body).RootElement.Clone(); }
                catch { await Json(s, new { error = "Bad request" }, 400); return; }
            }

            if (r.Method == "POST" && path == "/api/pair/start") { await PairStart(s, r, body); return; }
            if (r.Method == "POST" && path == "/api/pair/reveal") { await PairReveal(s, r, body); return; }
            if (r.Method == "POST" && path == "/api/pair/status") { await PairStatus(s, body); return; }
            if (r.Method == "POST" && path == "/api/call") { await Call(s, r, body); return; }
            await Json(s, new { error = "Not found" }, 404);
        }

        static byte[] B64(string s) { if (string.IsNullOrEmpty(s)) return null; try { return Convert.FromBase64String(s); } catch { return null; } }
        static byte[] Utf8(string s) { return Encoding.UTF8.GetBytes(s); }

        // ------------------------------------------------------------ pairing over the network
        /// <summary>Step 1: the phone commits to its key; PiP Studio answers with a fresh key of its own.</summary>
        static async Task PairStart(NetworkStream s, Request r, JsonElement body)
        {
            var commit = B64(Str(body, "commit"));
            if (commit == null || commit.Length != 32) { Failed(r.Ip); await Json(s, new { error = "Bad request" }, 400); return; }
            string name = (Str(body, "name") ?? "Phone").Trim();
            if (name.Length == 0) name = "Phone";
            if (name.Length > 40) name = name.Substring(0, 40);
            PairSession ps;
            lock (gate)
            {
                if (!pairingOpen) { ps = null; }
                else
                {
                    sessions.RemoveAll(x => x.Expired);
                    while (sessions.Count >= 4) sessions.RemoveAt(0);               // a few phones at once at most
                    byte[] priv, pub;
                    CompanionCrypto.KeyPair(out priv, out pub);
                    ps = new PairSession { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), Name = name, Commit = commit, PcPriv = priv, PcPub = pub, PcNonce = RandomNumberGenerator.GetBytes(16) };
                    sessions.Add(ps);
                }
            }
            if (ps == null) { await Json(s, new { error = "not-open" }, 403); return; }
            await Json(s, new { session = ps.Id, spub = Convert.ToBase64String(ps.PcPub), ns = Convert.ToBase64String(ps.PcNonce) });
        }

        /// <summary>Step 2: the phone shows its key (it must match its commitment). Both sides now know the PIN.</summary>
        static async Task PairReveal(NetworkStream s, Request r, JsonElement body)
        {
            string id = Str(body, "session");
            var cpub = B64(Str(body, "cpub"));
            var nc = B64(Str(body, "nc"));
            bool ok = false;
            lock (gate)
            {
                var ps = sessions.FirstOrDefault(x => x.Id == id && !x.Expired && x.Pin == null);
                if (ps != null && cpub != null && cpub.Length == 32 && nc != null && nc.Length == 16
                    && CryptographicOperations.FixedTimeEquals(CompanionCrypto.Commit(cpub, nc), ps.Commit))
                {
                    var shared = CompanionCrypto.Shared(ps.PcPriv, cpub);
                    if (shared != null)
                    {
                        ps.Pin = CompanionCrypto.Pin(shared, cpub, ps.PcPub, nc, ps.PcNonce);
                        ps.Key = CompanionCrypto.DeviceKey(shared, cpub, ps.PcPub, nc, ps.PcNonce);
                        ps.PcPriv = null;                                            // done with it
                        ok = true;
                    }
                }
            }
            if (!ok) { Failed(r.Ip); await Json(s, new { error = "Pairing failed. Start again." }, 403); return; }
            Raise();                                                                 // the dialog: a phone is showing a PIN
            await Json(s, new { ok = true });
        }

        /// <summary>The phone waits here until you type its PIN in PiP Studio.</summary>
        static async Task PairStatus(NetworkStream s, JsonElement body)
        {
            string id = Str(body, "session");
            object answer;
            lock (gate)
            {
                var ps = sessions.FirstOrDefault(x => x.Id == id && !x.Expired);
                if (ps == null) answer = new { state = "gone" };
                else if (ps.DeviceId != null) { answer = new { state = "paired", id = ps.DeviceId, pc = Environment.MachineName }; sessions.Remove(ps); }
                else answer = new { state = "waiting" };
            }
            await Json(s, answer);
        }

        /// <summary>True while a phone shows a PIN and waits for you to type it.</summary>
        public static bool PhoneWaiting { get { lock (gate) return sessions.Any(x => x.Pin != null && x.DeviceId == null && !x.Expired); } }

        // ------------------------------------------------------------ encrypted calls from paired phones
        static readonly Dictionary<string, Dictionary<string, DateTime>> seenNonces = new Dictionary<string, Dictionary<string, DateTime>>();
        const double ClockSlackMinutes = 5;

        /// <summary>Every request from a paired phone: {id, n, c} where c is the sealed {m, p, b, t}. The answer is sealed too.</summary>
        static async Task Call(NetworkStream s, Request r, JsonElement body)
        {
            string id = Str(body, "id"), nB64 = Str(body, "n");
            var key = KeyFor(id);
            var nonce = B64(nB64);
            var sealedReq = B64(Str(body, "c"));
            var plain = key == null || nonce == null || sealedReq == null ? null
                : CompanionCrypto.Open(CompanionCrypto.Direction(key, "c2s"), nonce, sealedReq, Utf8("c2s|" + id));
            if (plain == null) { Failed(r.Ip); await Json(s, new { error = "Not paired" }, 401); return; }

            JsonElement req;
            try { req = JsonDocument.Parse(plain).RootElement.Clone(); }
            catch { await Json(s, new { error = "Bad request" }, 400); return; }
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            double t = Num(req, "t") ?? 0;
            if (Math.Abs(now - t) > ClockSlackMinutes * 60000) { await Json(s, new { error = "clock", now }, 409); return; }   // the phone corrects its clock and tries again
            bool fresh;
            lock (gate)
            {
                Dictionary<string, DateTime> seen;
                if (!seenNonces.TryGetValue(id, out seen)) seenNonces[id] = seen = new Dictionary<string, DateTime>();
                foreach (var old in seen.Where(x => (DateTime.UtcNow - x.Value).TotalMinutes > ClockSlackMinutes * 2).Select(x => x.Key).ToList()) seen.Remove(old);
                fresh = !seen.ContainsKey(nB64);                                     // a message seen before is a replay
                if (fresh) { seen[nB64] = DateTime.UtcNow; lastSeen[id] = DateTime.Now; }
            }
            if (!fresh) { await Json(s, new { error = "replay" }, 409); return; }

            object result = Route(Str(req, "m"), Str(req, "p"), req.TryGetProperty("b", out var b) ? b : default(JsonElement));
            var answer = JsonSerializer.SerializeToUtf8Bytes(result ?? new { error = "Not found" });
            var rn = RandomNumberGenerator.GetBytes(12);
            var sealedAnswer = CompanionCrypto.Seal(CompanionCrypto.Direction(key, "s2c"), rn, answer, Utf8("s2c|" + id + "|" + nB64));
            await Json(s, new { n = Convert.ToBase64String(rn), c = Convert.ToBase64String(sealedAnswer) });
        }

        static object Route(string method, string path, JsonElement body)
        {
            switch ((method ?? "") + " " + (path ?? ""))
            {
                case "GET /api/state": return OnUi(State);
                case "POST /api/pip": return new { ok = OnUi(() => PipAction(Str(body, "id"), Str(body, "action"), Num(body, "value"))) };
                case "POST /api/global": return new { ok = OnUi(() => GlobalAction(Str(body, "action"))) };
                case "POST /api/audio": return new { ok = OnUi(() => AudioAction(Str(body, "process"), Num(body, "volume"), Bool(body, "muted"))) };
            }
            return null;
        }

        static byte[] Resource(string name)
        {
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (st == null) return Encoding.UTF8.GetBytes("Missing " + name);
                var ms = new MemoryStream();
                st.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static string Str(JsonElement e, string k) { return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null; }
        static double? Num(JsonElement e, string k) { return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null; }
        static bool? Bool(JsonElement e, string k) { return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : (bool?)null; }

        /// <summary>Runs on PiP Studio's UI thread and waits for the answer (PiPs, windows and audio all live there).</summary>
        static T OnUi<T>(Func<T> f)
        {
            var d = System.Windows.Application.Current != null ? System.Windows.Application.Current.Dispatcher : null;
            if (d == null || d.CheckAccess()) return f();
            return d.Invoke(f, System.Windows.Threading.DispatcherPriority.Normal, CancellationToken.None, TimeSpan.FromSeconds(4));
        }

        // ------------------------------------------------------------ what the phone sees and does
        static string PipId(PipWindow p) { return "p" + RuntimeHelpers.GetHashCode(p).ToString("x"); }
        static string ExtId(IntPtr h) { return "x" + h.ToInt64().ToString("x"); }

        static object State()
        {
            var pips = new List<object>();
            foreach (var p in App.I.Pips.Where(x => !x.IsClosing))
            {
                bool win = p.St.Kind == SourceKind.Window && !string.IsNullOrEmpty(p.St.ProcessName);
                pips.Add(new
                {
                    id = PipId(p), kind = "own", title = p.DisplayName,
                    app = !win ? "Screen" : p.Src != IntPtr.Zero ? ExternalPip.AppName(p.Src) : char.ToUpper(p.St.ProcessName[0]) + p.St.ProcessName.Substring(1),
                    window = win, minimized = false, theater = p.InTheater,
                    opacity = Math.Round(p.St.Opacity, 2),
                    volume = win ? Audio.GetVolume(p.St.ProcessName) : null,
                    muted = win ? Audio.GetMute(p.St.ProcessName) : null
                });
            }
            if (App.S.ControlOtherPips)
            {
                foreach (var h in ExternalPip.FindAll()) pips.Add(Ext(h, false));
                foreach (var h in ExternalPip.MinimizedPips()) pips.Add(Ext(h, true));
            }
            var audio = Audio.Apps().Where(a => !string.Equals(a.Process, "PiPStudio", StringComparison.OrdinalIgnoreCase))
                .Select(a => new { process = a.Process, name = a.Name, volume = Math.Round(a.Volume, 2), muted = a.Muted, playing = a.Playing }).ToList();
            return new { pc = Environment.MachineName, version = Updater.CurrentText, hidden = App.I.HiddenAll, pips, audio };
        }

        static object Ext(IntPtr h, bool minimized)
        {
            string proc = U.ProcessName(h);
            return new
            {
                id = ExtId(h), kind = "app", title = ExternalPip.AppName(h) + "'s own PiP", app = ExternalPip.AppName(h),
                window = true, minimized, theater = false,
                opacity = Math.Round(ExternalPip.OpacityOf(h), 2),
                volume = Audio.GetVolume(proc), muted = Audio.GetMute(proc)
            };
        }

        static bool PipAction(string id, string action, double? value)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(action)) return false;
            if (id[0] == 'p')
            {
                var p = App.I.Pips.FirstOrDefault(x => !x.IsClosing && PipId(x) == id);
                if (p == null) return false;
                switch (action)
                {
                    case "corner": p.CycleCorner(); return true;
                    case "nextmon": p.NextMonitor(); return true;
                    case "grow": p.StepSize(1); return true;
                    case "shrink": p.StepSize(-1); return true;
                    case "theater": p.ToggleTheater(); return true;
                    case "prev": p.CycleSource(-1); return true;
                    case "next": p.CycleSource(1); return true;
                    case "show": if (p.St.Kind == SourceKind.Window) p.BringSourceToFront(); return true;
                    case "mute": p.ToggleMute(); return true;
                    case "playpause": U.MediaKey(0xB3); return true;
                    case "close": p.CloseAnimated(); return true;
                    case "opacity": if (value == null) return false; p.SetOpacity(Math.Max(0.2, Math.Min(1, value.Value))); return true;
                    case "volume": if (value == null) return false; p.SetVolume((float)Math.Max(0, Math.Min(1, value.Value))); return true;
                }
                return false;
            }
            if (id[0] == 'x')
            {
                long v;
                if (!long.TryParse(id.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out v)) return false;
                var h = new IntPtr(v);
                if (!Native.IsWindow(h)) return false;
                if (!ExternalPip.FindAll().Contains(h) && !ExternalPip.MinimizedPips().Contains(h)) return false;   // only other apps' PiPs, nothing else
                switch (action)
                {
                    case "restore": ExternalPip.Restore(h); return true;
                    case "show": return ExternalPip.Handle("swap", h);
                    case "playpause": U.MediaKey(0xB3); return true;
                    case "opacity": if (value == null) return false; ExternalPip.SetOpacityPercent(h, (int)Math.Round(Math.Max(0.2, Math.Min(1, value.Value)) * 100)); return true;
                    case "volume": if (value == null) return false; Audio.SetVolume(U.ProcessName(h), (float)Math.Max(0, Math.Min(1, value.Value))); return true;
                    case "corner": case "nextmon": case "grow": case "shrink": case "theater": case "mute": case "close": case "minimize":
                        return ExternalPip.Handle(action, h);
                }
                return false;
            }
            return false;
        }

        static bool GlobalAction(string action)
        {
            switch (action)
            {
                case "startpip": App.I.RunAction("startpip"); return true;
                case "toggleall": App.I.RunAction("toggleall"); return true;
                case "playpause": U.MediaKey(0xB3); return true;
            }
            return false;
        }

        static bool AudioAction(string process, double? volume, bool? muted)
        {
            if (string.IsNullOrEmpty(process)) return false;
            if (!Audio.Apps().Any(a => string.Equals(a.Process, process, StringComparison.OrdinalIgnoreCase))) return false;   // only apps that have sound
            if (volume != null) Audio.SetVolume(process, (float)Math.Max(0, Math.Min(1, volume.Value)));
            if (muted != null) Audio.SetMute(process, muted.Value);
            return true;
        }
    }
}
