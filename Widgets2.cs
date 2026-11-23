using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace PipStudio
{
    /// <summary>A stopwatch and a countdown timer: switch between them at the top; the wheel over the time sets the timer.</summary>
    class TimerWidget : Widget
    {
        public override double Aspect { get { return 1.9; } }
        public override int Interval { get { return running ? 100 : 400; } }

        bool countdown, running, ringing;
        DateTime startedAt;
        TimeSpan banked;                                       // time counted before the last start (stopwatch), or used (timer)
        TimeSpan length = TimeSpan.FromMinutes(5);             // the timer's length
        DateTime ringUntil;
        Rectangle setArea;                                     // where the wheel sets the timer (not a button: dragging there moves the PiP)

        TimeSpan Counted { get { return banked + (running ? DateTime.Now - startedAt : TimeSpan.Zero); } }
        TimeSpan Left { get { var l = length - Counted; return l < TimeSpan.Zero ? TimeSpan.Zero : l; } }

        public override void Paint(Graphics g, Rectangle r, float s)
        {
            Background(g, r);
            hits.Clear();
            if (countdown && running && Left == TimeSpan.Zero) Ring();
            int pad = (int)(10 * s);

            // Stopwatch | Timer
            int tabW = (int)(84 * s), tabH = (int)(22 * s);
            var tabs = new Rectangle(r.X + (r.Width - 2 * tabW) / 2, r.Y + pad, 2 * tabW, tabH);
            Fill(g, tabs, tabH / 2f, Tint(22));
            var sel = new Rectangle(countdown ? tabs.X + tabW : tabs.X, tabs.Y, tabW, tabH);
            Fill(g, sel, tabH / 2f, Tint(60));
            Label(g, "Stopwatch", U.UI(9.5f * s), new Rectangle(tabs.X, tabs.Y, tabW, tabH), countdown ? Sub : Text, TextFormatFlags.HorizontalCenter);
            Label(g, "Timer", U.UI(9.5f * s), new Rectangle(tabs.X + tabW, tabs.Y, tabW, tabH), countdown ? Text : Sub, TextFormatFlags.HorizontalCenter);
            hits.Add(new Hit { R = new Rectangle(tabs.X, tabs.Y, tabW, tabH), Kind = "button", Key = "stopwatch" });
            hits.Add(new Hit { R = new Rectangle(tabs.X + tabW, tabs.Y, tabW, tabH), Kind = "button", Key = "timer" });

            // the time, as big as it fits
            var t = countdown ? Left : Counted;
            string text = t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                                            : t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
            if (!countdown) text += "." + (t.Milliseconds / 100);
            if (countdown && running) text = t.TotalHours >= 1 ? text : t.Minutes.ToString("00") + ":" + Math.Ceiling(t.Seconds + t.Milliseconds / 1000.0).ToString("00");
            var timeR = new Rectangle(r.X + pad, tabs.Bottom + (int)(4 * s), r.Width - 2 * pad, r.Height - tabH - 2 * pad - (int)(40 * s));
            float px = Math.Max(10, Math.Min(timeR.Height * 0.62f, timeR.Width / Math.Max(5, text.Length) * 1.5f));
            bool flash = ringing && DateTime.Now < ringUntil && (DateTime.Now.Millisecond / 250) % 2 == 0;
            if (ringing && DateTime.Now >= ringUntil) ringing = false;
            Txt(g, ringing ? L.T("Time's up") : text, U.UIBold(ringing ? px * 0.6f : px), timeR, flash ? Theme.Accent : Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            setArea = Rectangle.Empty;
            if (countdown && !running && Counted == TimeSpan.Zero)
            {
                setArea = timeR;                                        // the wheel here sets the length
                Label(g, "Scroll to set", U.UI(8.5f * s), new Rectangle(timeR.X, timeR.Bottom - (int)(12 * s), timeR.Width, (int)(12 * s)), Sub, TextFormatFlags.HorizontalCenter);
            }

            // buttons: [-1 min] [start/pause] [reset] [+1 min]
            int bh = (int)(30 * s), bw = (int)(30 * s), gap = (int)(10 * s), by = r.Bottom - pad - bh;
            var keys = new List<Tuple<string, string, bool>>();
            if (countdown) keys.Add(Tuple.Create("", "minus", false));
            keys.Add(Tuple.Create(running ? "" : "", "toggle", true));
            keys.Add(Tuple.Create("", "reset", false));
            if (countdown) keys.Add(Tuple.Create("", "plus", false));
            int total = keys.Count * bw + (keys.Count - 1) * gap + (int)(24 * s);
            int x = r.X + (r.Width - total) / 2;
            foreach (var k in keys)
            {
                int w = k.Item3 ? bw + (int)(24 * s) : bw;
                Button(g, s, new Rectangle(x, by, w, bh), k.Item1, k.Item2, k.Item3);
                x += w + gap;
            }
        }

        void Ring()
        {
            running = false; banked = length;
            ringing = true; ringUntil = DateTime.Now.AddSeconds(8);
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
        }

        public override void MouseDown(Point p)
        {
            var h = HitAt(p);
            if (h == null) return;
            ringing = false;
            switch (h.Key)
            {
                case "stopwatch": if (countdown) { countdown = false; Reset(); } break;
                case "timer": if (!countdown) { countdown = true; Reset(); } break;
                case "toggle":
                    if (running) { banked += DateTime.Now - startedAt; running = false; }
                    else
                    {
                        if (countdown && Left == TimeSpan.Zero) Reset();
                        startedAt = DateTime.Now; running = true;
                    }
                    break;
                case "reset": Reset(); break;
                case "plus": Adjust(1); break;
                case "minus": Adjust(-1); break;
            }
        }

        void Reset() { running = false; banked = TimeSpan.Zero; ringing = false; }

        /// <summary>A minute more or less: on the timer's length while it hasn't started, on the time left while it runs.</summary>
        void Adjust(int minutes)
        {
            var nl = length + TimeSpan.FromMinutes(minutes);
            if (nl < TimeSpan.FromMinutes(1) && Counted == TimeSpan.Zero) nl = TimeSpan.FromMinutes(1);
            if (nl < Counted + TimeSpan.FromSeconds(1)) nl = Counted + TimeSpan.FromSeconds(1);
            if (nl > TimeSpan.FromHours(24)) nl = TimeSpan.FromHours(24);
            length = nl;
        }

        public override bool Wheel(Point p, int delta)
        {
            if (!setArea.Contains(p)) return false;
            Adjust(delta > 0 ? 1 : -1);
            return true;
        }
    }

    /// <summary>How long a ping takes to your router and to the internet (with a graph), and how many get lost: for spotting
    /// whether lag comes from your Wi-Fi or from further out. Click the internet line to switch which server it pings.</summary>
    class PingWidget : Widget
    {
        public override double Aspect { get { return 1.7; } }
        public PingWidget() { Pinger.Use(true); }
        public override void Dispose() { Pinger.Use(false); }

        public override void Paint(Graphics g, Rectangle r, float s)
        {
            Background(g, r);
            hits.Clear();
            int pad = (int)(12 * s), w = r.Width - 2 * pad, x = r.X + pad, y = r.Y + pad;
            var n = Pinger.Snapshot();
            Color netC = Ser(255, 180, 90), lanC = Ser(90, 200, 230);

            Label(g, "Ping", U.UIBold(12 * s), new Rectangle(x, y, w, (int)(18 * s)), Text);
            Label(g, Pinger.ServerName, U.UI(9.5f * s), new Rectangle(x, y, w, (int)(18 * s)), Theme.Accent, TextFormatFlags.Right);
            int sw = (int)Math.Ceiling(g.MeasureString(L.T(Pinger.ServerName), U.UI(9.5f * s)).Width);
            hits.Add(new Hit { R = new Rectangle(x + w - sw - (int)(4 * s), y, sw + (int)(4 * s), (int)(18 * s)), Kind = "button", Key = "server" });
            y += (int)(24 * s);

            // the internet's graph (the router's in a fainter line)
            var gr = new Rectangle(x, y, w, (int)(r.Height * 0.34f));
            Fill(g, gr, 6 * s, Tint(22));
            double top = Math.Max(20, Math.Max(n.NetHist.DefaultIfEmpty(0).Max(), n.LanHist.DefaultIfEmpty(0).Max()) * 1.15);
            var inner = new RectangleF(gr.X + 3 * s, gr.Y + 4 * s, gr.Width - 6 * s, gr.Height - 6 * s);
            MonitorWidget.Graph(g, inner, n.LanHist, top, Color.FromArgb(170, lanC), false);
            MonitorWidget.Graph(g, inner, n.NetHist, top, netC, true);
            Label(g, ((int)top) + " ms", U.UI(8 * s), new Rectangle(gr.X + (int)(5 * s), gr.Y + (int)(3 * s), w, (int)(12 * s)), Sub);
            y = gr.Bottom + (int)(8 * s);

            y = Line(g, s, x, y, w, "Internet", netC, n.Net, n.NetAvg, n.NetJitter, n.NetLoss);
            y = Line(g, s, x, y, w, "Router", lanC, n.Lan, n.LanAvg, n.LanJitter, n.LanLoss);
        }

        int Line(Graphics g, float s, int x, int y, int w, string name, Color c, double ms, double avg, double jitter, double loss)
        {
            int h = (int)(18 * s);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x, y + h / 2f - 3 * s, 6 * s, 6 * s);
            Label(g, name, U.UI(10 * s), new Rectangle(x + (int)(11 * s), y, (int)(w * 0.3f), h), Sub);
            Label(g, ms < 0 ? "—" : Ms(ms), U.UIBold(11 * s), new Rectangle(x + (int)(w * 0.3f), y, (int)(w * 0.22f), h), Text);
            string more = avg < 0 ? "" : L.T("avg") + " " + Ms(avg) + "  ±" + Ms(jitter) + "  " + L.T("lost") + " " + Math.Round(loss * 100) + "%";
            Label(g, more, U.UI(9 * s), new Rectangle(x + (int)(w * 0.5f), y, (int)(w * 0.5f), h), Sub, TextFormatFlags.Right);
            return y + h + (int)(4 * s);
        }

        static string Ms(double v) { return (v < 10 ? v.ToString("0.#", U.CI) : Math.Round(v).ToString(U.CI)) + " ms"; }

        public override void MouseDown(Point p)
        {
            var h = HitAt(p);
            if (h != null && h.Key == "server") Pinger.NextServer();
        }
    }

    /// <summary>Pings the router (default gateway) and an internet server once a second, on its own thread, while a Ping PiP is open.</summary>
    static class Pinger
    {
        public class Snap
        {
            public double Net = -1, Lan = -1, NetAvg = -1, LanAvg = -1, NetJitter, LanJitter, NetLoss, LanLoss;
            public double[] NetHist = new double[0], LanHist = new double[0];
        }

        static readonly string[] Servers = { "1.1.1.1", "8.8.8.8", "9.9.9.9" };
        static readonly string[] ServerNames = { "Cloudflare", "Google", "Quad9" };
        static int server;
        public static string ServerName { get { return ServerNames[server]; } }
        public static void NextServer() { lock (gate) { server = (server + 1) % Servers.Length; net.Clear(); } }

        static readonly object gate = new object();
        static int users;
        static Thread thread;
        static readonly List<double> net = new List<double>(), lan = new List<double>();   // -1 = lost
        static Snap last = new Snap();

        public static void Use(bool on)
        {
            lock (gate)
            {
                users = Math.Max(0, users + (on ? 1 : -1));
                if (users > 0 && thread == null) { thread = new Thread(Run) { IsBackground = true, Name = "Ping" }; thread.Start(); }
            }
        }

        public static Snap Snapshot() { lock (gate) return last; }

        static void Run()
        {
            using (var p1 = new Ping())
            using (var p2 = new Ping())
            {
                while (true)
                {
                    lock (gate) if (users == 0) { thread = null; return; }
                    var t0 = DateTime.Now;
                    string target; lock (gate) target = Servers[server];
                    var gw = Gateway();
                    double a = Send(p1, target), b = gw == null ? -2 : Send(p2, gw);
                    lock (gate)
                    {
                        Add(net, a);
                        if (b != -2) Add(lan, b);
                        last = Summarize();
                    }
                    int wait = 1000 - (int)(DateTime.Now - t0).TotalMilliseconds;
                    if (wait > 0) Thread.Sleep(wait);
                }
            }
        }

        static double Send(Ping p, string host)
        {
            try
            {
                var r = p.Send(host, 900);
                return r.Status == IPStatus.Success ? Math.Max(0.5, r.RoundtripTime) : -1;
            }
            catch { return -1; }
        }

        static void Add(List<double> l, double v) { l.Add(v); while (l.Count > SysStats.HistoryLength) l.RemoveAt(0); }

        static Snap Summarize()
        {
            var n = new Snap();
            Fill(net, out n.Net, out n.NetAvg, out n.NetJitter, out n.NetLoss, out n.NetHist);
            Fill(lan, out n.Lan, out n.LanAvg, out n.LanJitter, out n.LanLoss, out n.LanHist);
            return n;
        }

        static void Fill(List<double> l, out double now, out double avg, out double jitter, out double loss, out double[] hist)
        {
            now = l.Count > 0 ? l[l.Count - 1] : -1;
            var ok = l.Where(v => v >= 0).ToList();
            avg = ok.Count > 0 ? ok.Average() : -1;
            jitter = 0;
            for (int i = 1; i < ok.Count; i++) jitter += Math.Abs(ok[i] - ok[i - 1]);
            if (ok.Count > 1) jitter /= ok.Count - 1;
            loss = l.Count > 0 ? l.Count(v => v < 0) / (double)l.Count : 0;
            hist = l.Select(v => v < 0 ? 0 : v).ToArray();
        }

        /// <summary>The default gateway of the connection that's up (the router), or null.</summary>
        static string Gateway()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var gw in ni.GetIPProperties().GatewayAddresses)
                        if (gw.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !gw.Address.Equals(IPAddress.Any)) return gw.Address.ToString();
                }
            }
            catch { }
            return null;
        }
    }
}
