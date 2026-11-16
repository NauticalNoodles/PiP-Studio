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
    /// - Pairing: "Pair a phone" shows a QR code with a one-time code that works once, only while that dialog is open. The
    ///   phone trades it for its own key, which it keeps; PiP Studio keeps only a SHA-256 of each key, and Remove forgets one.
    /// - The phone page (companion\index.html) is served by PiP Studio itself and polls /api/state about once a second.
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

        // ------------------------------------------------------------ pairing
        static string pairCode;
        static DateTime pairUntil;
        static readonly Dictionary<string, DateTime> lastSeen = new Dictionary<string, DateTime>();
        static readonly Dictionary<string, List<DateTime>> failures = new Dictionary<string, List<DateTime>>();

        /// <summary>A paired phone. Only a hash of its key is kept.</summary>
        public class Device { public string Id, Hash, Name; public DateTime? LastSeen; }

        /// <summary>Starts pairing: returns the one-time code for the QR (valid for 10 minutes, once, until EndPairing).</summary>
        public static string BeginPairing()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";              // no 0/O or 1/I, in case it's typed
            var b = RandomNumberGenerator.GetBytes(8);
            var sb = new StringBuilder();
            foreach (var x in b) sb.Append(letters[x % letters.Length]);
            lock (gate) { pairCode = sb.ToString(); pairUntil = DateTime.UtcNow.AddMinutes(10); }
            return pairCode;
        }

        public static void EndPairing() { lock (gate) pairCode = null; }

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
                l.Add(new Device { Id = f[0], Hash = f[1], Name = Uri.UnescapeDataString(f[2]), LastSeen = known ? seen : (DateTime?)null });
            }
            return l;
        }

        static void SaveDevices(List<Device> l)
        {
            App.S.CompanionDevices = string.Join(";", l.Select(d => d.Id + "|" + d.Hash + "|" + Uri.EscapeDataString(d.Name ?? "Phone")));
            App.I.SaveSettingsSoon();
        }

        /// <summary>Forgets a phone: its key stops working right away.</summary>
        public static void Remove(string id)
        {
            OnUi(() => { SaveDevices(Devices().Where(d => d.Id != id).ToList()); return 0; });
            Raise();
        }

        static string Sha(string s) { return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant(); }

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
        class Request { public string Method, Path, Host, Auth, Ip; public byte[] Body; }

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
                    else if (k == "authorization") r.Auth = v;
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
            if (r.Method == "GET" && path == "/icon.png") { await Send(s, 200, "image/png", Resource("companion.icon.png"), true); return; }
            if (r.Method == "GET" && path == "/manifest.webmanifest")
            {
                var m = new Dictionary<string, object>
                {
                    ["name"] = "PiP Studio", ["short_name"] = "PiP Studio", ["start_url"] = "/", ["display"] = "standalone",
                    ["background_color"] = "#111214", ["theme_color"] = "#111214",
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

            if (r.Method == "POST" && path == "/api/pair") { await Pair(s, r, body); return; }

            var device = Authorize(r);
            if (device == null) { Failed(r.Ip); await Json(s, new { error = "Not paired" }, 401); return; }
            lock (gate) lastSeen[device.Id] = DateTime.Now;

            if (r.Method == "GET" && path == "/api/state") { await Json(s, OnUi(State)); return; }
            if (r.Method == "POST" && path == "/api/pip") { bool ok = OnUi(() => PipAction(Str(body, "id"), Str(body, "action"), Num(body, "value"))); await Json(s, new { ok }); return; }
            if (r.Method == "POST" && path == "/api/global") { bool ok = OnUi(() => GlobalAction(Str(body, "action"))); await Json(s, new { ok }); return; }
            if (r.Method == "POST" && path == "/api/audio") { bool ok = OnUi(() => AudioAction(Str(body, "process"), Num(body, "volume"), Bool(body, "muted"))); await Json(s, new { ok }); return; }
            await Json(s, new { error = "Not found" }, 404);
        }

        static async Task Pair(NetworkStream s, Request r, JsonElement body)
        {
            string code = (Str(body, "code") ?? "").Trim().ToUpperInvariant();
            bool ok;
            lock (gate)
            {
                ok = pairCode != null && DateTime.UtcNow < pairUntil && SameText(code, pairCode);
                if (ok) pairCode = null;                                             // works once
            }
            if (!ok) { Failed(r.Ip); await Json(s, new { error = "That code didn't work. Check it, or press Pair a phone in PiP Studio again for a new one." }, 403); return; }
            string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string name = (Str(body, "name") ?? "Phone").Trim();
            if (name.Length == 0) name = "Phone";
            if (name.Length > 40) name = name.Substring(0, 40);
            var d = new Device { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(), Hash = Sha(key), Name = name };
            OnUi(() => { var l = Devices(); l.Add(d); SaveDevices(l); return 0; });
            lock (gate) lastSeen[d.Id] = DateTime.Now;
            Raise();
            await Json(s, new { token = key, pc = Environment.MachineName });
        }

        static Device Authorize(Request r)
        {
            if (r.Auth == null || !r.Auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
            string h = Sha(r.Auth.Substring(7).Trim());
            return OnUi(Devices).FirstOrDefault(d => SameText(d.Hash, h));
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
