using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PipStudio
{
    // =================================================================================
    //  Utility PiPs: PiPs whose picture PiP Studio draws itself (volume, system monitor, clock, now playing).
    //  They are ordinary PiPs in every other way (snapping, sizes, opacity, title bar, shortcuts, controller, sessions):
    //  PipWindow asks the widget to paint into its picture a few times a second, and hands it the clicks, drags and
    //  wheel turns that land on its buttons and sliders (everywhere else, dragging moves the PiP as usual).
    // =================================================================================
    abstract class Widget
    {
        public static readonly string[] Ids = { "volume", "monitor", "clock", "media" };
        public static readonly string[] Names = { "Volume", "System monitor", "Clock", "Now playing" };
        public static readonly string[] Glyphs = { "", "", "", "" };

        public static Widget Create(string id)
        {
            switch (id)
            {
                case "monitor": return new MonitorWidget();
                case "clock": return new ClockWidget();
                case "media": return new MediaWidget();
                default: return new VolumeWidget();
            }
        }

        public static string NameOf(string id) { int i = Array.IndexOf(Ids, id); return Names[i < 0 ? 0 : i]; }

        /// <summary>The PiP's shape (width / height) and how often it's redrawn (ms).</summary>
        public abstract double Aspect { get; }
        public virtual int Interval { get { return 1000; } }
        public abstract void Paint(Graphics g, Rectangle r, float s);

        // what can be clicked, in picture coordinates (filled in while painting)
        protected class Hit { public Rectangle R; public string Kind, Key; }
        protected readonly List<Hit> hits = new List<Hit>();
        protected Hit dragging;
        public bool Busy { get { return dragging != null; } }

        protected Hit HitAt(Point p) { for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(p)) return hits[i]; return null; }
        public virtual bool IsInteractive(Point p) { return HitAt(p) != null; }
        public virtual void MouseDown(Point p) { }
        public virtual void MouseMove(Point p) { }
        public virtual void MouseUp(Point p) { dragging = null; }
        public virtual bool Wheel(Point p, int delta) { return false; }
        public virtual void Dispose() { }

        // ------------------------------------------------------------ drawing helpers (dark glass, accent fills)
        protected static readonly Color Text = Color.FromArgb(240, 240, 245), Sub = Color.FromArgb(150, 150, 162), Track = Color.FromArgb(58, 58, 66);

        protected static void Background(Graphics g, Rectangle r)
        {
            using (var b = new LinearGradientBrush(r, Color.FromArgb(30, 30, 36), Color.FromArgb(16, 16, 20), 90f)) g.FillRectangle(b, r);
        }

        protected static void Label(Graphics g, string text, Font f, Rectangle r, Color c, TextFormatFlags extra = 0)
        {
            LT.DrawText(g, text, f, r, c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | extra);
        }

        protected static void Glyph(Graphics g, string glyph, float px, Rectangle r, Color c)
        {
            TextRenderer.DrawText(g, glyph, U.Icons(px), r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        protected static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected static void Fill(Graphics g, RectangleF r, float rad, Color c) { using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p); }

        /// <summary>A rounded slider: groove, accent fill and a round knob.</summary>
        protected static void Slider(Graphics g, Rectangle r, double v, float s, bool active)
        {
            v = U.Clamp(v, 0, 1);
            float h = 6 * s, y = r.Y + (r.Height - h) / 2f;
            Fill(g, new RectangleF(r.X, y, r.Width, h), h / 2, Track);
            if (v > 0) Fill(g, new RectangleF(r.X, y, Math.Max(h, (float)(r.Width * v)), h), h / 2, Theme.Accent);
            float k = (active ? 16 : 14) * s, kx = r.X + (float)(r.Width * v) - k / 2, ky = r.Y + (r.Height - k) / 2f;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, ky, k, k);
        }

        protected static double SliderValue(Rectangle r, Point p) { return U.Clamp((p.X - r.X) / (double)Math.Max(1, r.Width), 0, 1); }

        protected static string Pct(double v) { return (int)Math.Round(v * 100) + "%"; }
    }

    // ---------------------------------------------------------------------------- Volume
    /// <summary>The speakers' volume and mute, then a slider for each app that makes sound (the ones playing first).</summary>
    class VolumeWidget : Widget
    {
        public override double Aspect { get { return 1.5; } }
        public override int Interval { get { return Busy ? 33 : 250; } }

        List<Audio.AppVolume> apps = new List<Audio.AppVolume>();
        float master = -1; bool masterMuted;
        DateTime refreshed = DateTime.MinValue;

        void Refresh(bool force)
        {
            if (!force && (DateTime.UtcNow - refreshed).TotalMilliseconds < 1000) return;
            refreshed = DateTime.UtcNow;
            master = Audio.GetMasterVolume() ?? -1;
            masterMuted = Audio.GetMasterMute() ?? false;
            try { apps = Audio.Apps().OrderByDescending(a => a.Playing).ThenByDescending(a => a.Score).ThenBy(a => a.Name).ToList(); } catch { }
        }

        public override void Paint(Graphics g, Rectangle r, float s)
        {
            if (!Busy) Refresh(false);
            Background(g, r);
            hits.Clear();
            int pad = (int)(14 * s), rowH = (int)(44 * s), x = r.X + pad, w = r.Width - 2 * pad;
            Label(g, "Volume", U.UIBold(13 * s), new Rectangle(x, r.Y + pad / 2, w, (int)(26 * s)), Text);
            int y = r.Y + pad / 2 + (int)(30 * s);
            Row(g, s, x, y, w, rowH, "", "Main volume", master < 0 ? 0 : master, masterMuted, "", true);
            y += rowH + (int)(6 * s);
            using (var p = new Pen(Color.FromArgb(45, 255, 255, 255), Math.Max(1, s))) g.DrawLine(p, x, y - (int)(3 * s), x + w, y - (int)(3 * s));
            if (apps.Count == 0)
                Label(g, "No app is making sound right now", U.UI(11.5f * s), new Rectangle(x, y, w, rowH), Sub);
            foreach (var a in apps)
            {
                if (y + rowH > r.Bottom - pad / 3) break;
                Row(g, s, x, y, w, rowH, a.Playing ? "" : "", a.Name, a.Volume, a.Muted, a.Process, false);
                y += rowH;
            }
        }

        void Row(Graphics g, float s, int x, int y, int w, int h, string glyph, string name, double v, bool muted, string key, bool isMaster)
        {
            int btn = (int)(30 * s);
            var mute = new Rectangle(x, y + (h - btn) / 2, btn, btn);
            Fill(g, mute, 8 * s, Color.FromArgb(muted ? 70 : 34, 255, 255, 255));
            Glyph(g, muted ? "" : glyph, 13 * s, mute, muted ? Sub : Text);
            hits.Add(new Hit { R = mute, Kind = isMaster ? "mmute" : "mute", Key = key });
            int tx = x + btn + (int)(10 * s), pctW = (int)(40 * s), tw = w - btn - (int)(10 * s);
            Label(g, name, U.UI(11 * s), new Rectangle(tx, y + (int)(2 * s), tw - pctW, (int)(18 * s)), muted ? Sub : Text);
            Label(g, Pct(v), U.UI(11 * s), new Rectangle(tx + tw - pctW, y + (int)(2 * s), pctW, (int)(18 * s)), Sub, TextFormatFlags.Right);
            var sl = new Rectangle(tx, y + (int)(20 * s), tw, (int)(20 * s));
            Slider(g, sl, muted ? 0 : v, s, dragging != null && dragging.Key == key && dragging.Kind.EndsWith("slider"));
            hits.Add(new Hit { R = Rectangle.Inflate(sl, 0, (int)(4 * s)), Kind = isMaster ? "mslider" : "slider", Key = key });
        }

        void Set(Hit h, double v)
        {
            if (h.Kind == "mslider") { Audio.SetMasterVolume((float)v); master = (float)v; if (v > 0) masterMuted = false; }
            else if (h.Kind == "slider")
            {
                Audio.SetVolume(h.Key, (float)v);
                if (v > 0 && Audio.GetMute(h.Key) == true) Audio.SetMute(h.Key, false);
                var a = apps.FirstOrDefault(x => x.Process == h.Key); if (a != null) { a.Volume = (float)v; if (v > 0) a.Muted = false; }
            }
        }

        public override void MouseDown(Point p)
        {
            var h = HitAt(p);
            if (h == null) return;
            if (h.Kind == "mmute") { Audio.SetMasterMute(!masterMuted); Refresh(true); return; }
            if (h.Kind == "mute") { var m = Audio.GetMute(h.Key) ?? false; Audio.SetMute(h.Key, !m); Refresh(true); return; }
            dragging = h;
            Set(h, SliderValue(h.R, p));
        }

        public override void MouseMove(Point p) { if (dragging != null) Set(dragging, SliderValue(dragging.R, p)); }
        public override void MouseUp(Point p) { dragging = null; Refresh(true); }

        public override bool Wheel(Point p, int delta)
        {
            var h = HitAt(p);
            if (h == null || !h.Kind.EndsWith("slider")) return false;
            double cur = h.Kind == "mslider" ? Math.Max(0, master) : (apps.FirstOrDefault(a => a.Process == h.Key) ?? new Audio.AppVolume()).Volume;
            Set(h, U.Clamp(cur + (delta > 0 ? 0.04 : -0.04), 0, 1));
            return true;
        }
    }

    // ---------------------------------------------------------------------------- System monitor
    /// <summary>CPU, GPU, memory and network use, each with a graph of the last minute.</summary>
    class MonitorWidget : Widget
    {
        public override double Aspect { get { return 1.6; } }
        public MonitorWidget() { SysStats.Use(true); }
        public override void Dispose() { SysStats.Use(false); }

        public override void Paint(Graphics g, Rectangle r, float s)
        {
            Background(g, r);
            int pad = (int)(10 * s), gap = (int)(8 * s);
            int tw = (r.Width - 2 * pad - gap) / 2, th = (r.Height - 2 * pad - gap) / 2;
            var snap = SysStats.Snapshot();
            Tile(g, s, new Rectangle(r.X + pad, r.Y + pad, tw, th), "CPU", snap.Cpu < 0 ? "—" : Pct(snap.Cpu), "", snap.CpuHist, 1, Theme.Accent);
            Tile(g, s, new Rectangle(r.X + pad + tw + gap, r.Y + pad, tw, th), "GPU", snap.Gpu < 0 ? "—" : Pct(snap.Gpu), "", snap.GpuHist, 1, Color.FromArgb(120, 200, 120));
            Tile(g, s, new Rectangle(r.X + pad, r.Y + pad + th + gap, tw, th), "Memory", Pct(snap.Mem),
                snap.MemTotalGb > 0 ? snap.MemUsedGb.ToString("0.0", U.CI) + " / " + snap.MemTotalGb.ToString("0", U.CI) + " GB" : "", snap.MemHist, 1, Color.FromArgb(200, 150, 255));
            double top = Math.Max(1, Math.Max(snap.DownHist.DefaultIfEmpty(0).Max(), snap.UpHist.DefaultIfEmpty(0).Max()));
            Tile(g, s, new Rectangle(r.X + pad + tw + gap, r.Y + pad + th + gap, tw, th), "Network", "↓ " + Rate(snap.Down), "↑ " + Rate(snap.Up), snap.DownHist, top, Color.FromArgb(255, 180, 90), snap.UpHist);
        }

        static string Rate(double bps)
        {
            if (bps >= 1e6) return (bps / 1e6).ToString(bps >= 1e7 ? "0" : "0.0", U.CI) + " MB/s";
            if (bps >= 1e3) return (bps / 1e3).ToString("0", U.CI) + " KB/s";
            return ((int)bps) + " B/s";
        }

        static void Tile(Graphics g, float s, Rectangle t, string name, string big, string small, double[] hist, double max, Color c, double[] hist2 = null)
        {
            Fill(g, t, 10 * s, Color.FromArgb(26, 255, 255, 255));
            int p = (int)(10 * s);
            // the graph fills the tile's lower half
            var gr = new RectangleF(t.X + p, t.Y + t.Height * 0.48f, t.Width - 2 * p, t.Height * 0.52f - p);
            Graph(g, gr, hist, max, c, true);
            if (hist2 != null) Graph(g, gr, hist2, max, Color.FromArgb(160, 160, 220, 255), false);
            Label(g, name, U.UI(10.5f * s), new Rectangle(t.X + p, t.Y + (int)(6 * s), t.Width - 2 * p, (int)(16 * s)), Sub);
            Label(g, big, U.UIBold(15 * s), new Rectangle(t.X + p, t.Y + (int)(22 * s), t.Width - 2 * p, (int)(24 * s)), Text);
            if (small != "") Label(g, small, U.UI(10 * s), new Rectangle(t.X + p, t.Y + (int)(6 * s), t.Width - 2 * p, (int)(16 * s)), Sub, TextFormatFlags.Right);
        }

        static void Graph(Graphics g, RectangleF r, double[] h, double max, Color c, bool area)
        {
            if (h == null || h.Length < 2 || r.Width < 4 || r.Height < 4) return;
            var pts = new PointF[h.Length];
            for (int i = 0; i < h.Length; i++)
                pts[i] = new PointF(r.X + r.Width * i / (SysStats.HistoryLength - 1) + r.Width * (SysStats.HistoryLength - h.Length) / (SysStats.HistoryLength - 1),
                                    r.Bottom - (float)(U.Clamp(h[i] / max, 0, 1) * r.Height));
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (area)
            {
                var poly = new List<PointF>(pts) { new PointF(pts[pts.Length - 1].X, r.Bottom), new PointF(pts[0].X, r.Bottom) };
                using (var b = new SolidBrush(Color.FromArgb(55, c))) g.FillPolygon(b, poly.ToArray());
            }
            using (var pen = new Pen(c, 1.6f)) g.DrawLines(pen, pts);
            g.SmoothingMode = old;
        }
    }

    /// <summary>Samples CPU, GPU, memory and network once a second on its own thread, while a system monitor is open.</summary>
    static class SysStats
    {
        public const int HistoryLength = 60;

        public class Snap
        {
            public double Cpu = -1, Gpu = -1, Mem, MemUsedGb, MemTotalGb, Down, Up;
            public double[] CpuHist = new double[0], GpuHist = new double[0], MemHist = new double[0], DownHist = new double[0], UpHist = new double[0];
        }

        static readonly object gate = new object();
        static int users;
        static Thread thread;
        static Snap last = new Snap();
        static readonly Queue<double> cpuH = new Queue<double>(), gpuH = new Queue<double>(), memH = new Queue<double>(), downH = new Queue<double>(), upH = new Queue<double>();

        public static void Use(bool on)
        {
            lock (gate)
            {
                users = Math.Max(0, users + (on ? 1 : -1));
                if (users > 0 && thread == null)
                {
                    thread = new Thread(Run) { IsBackground = true, Name = "System monitor", Priority = ThreadPriority.BelowNormal };
                    thread.Start();
                }
            }
        }

        public static Snap Snapshot() { lock (gate) return last; }

        static void Push(Queue<double> q, double v) { q.Enqueue(v); while (q.Count > HistoryLength) q.Dequeue(); }

        static void Run()
        {
            long idle0 = 0, total0 = 0, rx0 = -1, tx0 = -1;
            var gpu = new Gpu();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double t0 = 0;
            while (true)
            {
                lock (gate) if (users == 0) { thread = null; gpu.Dispose(); return; }
                var n = new Snap();
                try
                {
                    long idle, kernel, user;
                    if (GetSystemTimes(out idle, out kernel, out user))
                    {
                        long total = kernel + user;                      // kernel time includes idle time
                        if (total0 > 0 && total > total0) n.Cpu = U.Clamp(1 - (idle - idle0) / (double)(total - total0), 0, 1);
                        idle0 = idle; total0 = total;
                    }
                }
                catch { }
                try
                {
                    var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
                    if (GlobalMemoryStatusEx(ref m))
                    {
                        n.MemTotalGb = m.ullTotalPhys / 1073741824.0;
                        n.MemUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
                        n.Mem = m.ullTotalPhys > 0 ? n.MemUsedGb / n.MemTotalGb : 0;
                    }
                }
                catch { }
                try
                {
                    long rx = 0, tx = 0;
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                        var st = ni.GetIPStatistics();
                        rx += st.BytesReceived; tx += st.BytesSent;
                    }
                    double t = clock.Elapsed.TotalSeconds, dt = Math.Max(0.2, t - t0);
                    if (rx0 >= 0) { n.Down = Math.Max(0, (rx - rx0) / dt); n.Up = Math.Max(0, (tx - tx0) / dt); }
                    rx0 = rx; tx0 = tx; t0 = t;
                }
                catch { }
                n.Gpu = gpu.Read();
                lock (gate)
                {
                    if (n.Cpu >= 0) Push(cpuH, n.Cpu);
                    if (n.Gpu >= 0) Push(gpuH, n.Gpu);
                    Push(memH, n.Mem); Push(downH, n.Down); Push(upH, n.Up);
                    n.CpuHist = cpuH.ToArray(); n.GpuHist = gpuH.ToArray(); n.MemHist = memH.ToArray(); n.DownHist = downH.ToArray(); n.UpHist = upH.ToArray();
                    if (n.Cpu < 0) n.Cpu = last.Cpu;
                    last = n;
                }
                Thread.Sleep(1000);
            }
        }

        /// <summary>GPU use like Task Manager shows it: the busiest engine, summing every app using it (Windows' GPU Engine counters).</summary>
        class Gpu : IDisposable
        {
            IntPtr query, counter;
            bool ok;

            public Gpu()
            {
                try
                {
                    ok = PdhOpenQuery(null, IntPtr.Zero, out query) == 0
                      && PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out counter) == 0;
                    if (ok) PdhCollectQueryData(query);
                }
                catch { ok = false; }
            }

            public double Read()
            {
                if (!ok) return -1;
                try
                {
                    if (PdhCollectQueryData(query) != 0) return -1;
                    uint size = 0, count;
                    int rc = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero);
                    if (rc != PDH_MORE_DATA || size == 0) return -1;
                    IntPtr buf = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref size, out count, buf) != 0) return -1;
                        var perEngine = new Dictionary<string, double>();
                        int item = IntPtr.Size + 8 + 8;                    // name pointer, then { status, padding, double }
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr p = buf + i * item;
                            string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p)) ?? "";
                            double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(p + IntPtr.Size + 8));
                            int at = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                            string engine = at >= 0 ? name.Substring(at) : name;   // the same engine, used by every process
                            double sum; perEngine.TryGetValue(engine, out sum);
                            perEngine[engine] = sum + v;
                        }
                        return perEngine.Count == 0 ? 0 : U.Clamp(perEngine.Values.Max() / 100.0, 0, 1);
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                catch { return -1; }
            }

            public void Dispose() { if (query != IntPtr.Zero) { try { PdhCloseQuery(query); } catch { } query = IntPtr.Zero; } }
        }

        const uint PDH_FMT_DOUBLE = 0x00000200;
        const int PDH_MORE_DATA = unchecked((int)0x800007D2);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr items);
        [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    }

    // ---------------------------------------------------------------------------- Clock
    /// <summary>The time, big, and the date under it, in your Windows formats.</summary>
    class ClockWidget : Widget
    {
        public override double Aspect { get { return 2.2; } }

        public override void Paint(Graphics g, Rectangle r, float s)
        {
            Background(g, r);
            var now = DateTime.Now;
            var cul = System.Globalization.CultureInfo.CurrentCulture;
            string time = now.ToString(cul.DateTimeFormat.ShortTimePattern, cul);
            string date = now.ToString(cul.DateTimeFormat.LongDatePattern, cul);
            // as big as the picture allows
            float px = Math.Max(10, Math.Min(r.Height * 0.42f, r.Width / Math.Max(4, time.Length) * 1.55f));
            var big = new Rectangle(r.X, r.Y + (int)(r.Height * 0.12f), r.Width, (int)(r.Height * 0.55f));
            TextRenderer.DrawText(g, time, U.UIBold(px), big, Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            var small = new Rectangle(r.X + (int)(8 * s), r.Y + (int)(r.Height * 0.66f), r.Width - (int)(16 * s), (int)(r.Height * 0.22f));
            TextRenderer.DrawText(g, date, U.UI(Math.Max(8, Math.Min(px * 0.32f, 16 * s))), small, Sub,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    // ---------------------------------------------------------------------------- Now playing
    /// <summary>What's playing on the PC (any app that shows in Windows' media controls), with previous, play/pause and next.</summary>
    class MediaWidget : Widget
    {
        public override double Aspect { get { return 2.4; } }
        public MediaWidget() { MediaNow.Use(true); }
        public override void Dispose() { MediaNow.Use(false); }

        public override void Paint(Graphics g, Rectangle r, float s)
        {
            Background(g, r);
            hits.Clear();
            var m = MediaNow.Current;
            int pad = (int)(12 * s);
            int art = r.Height - 2 * pad;
            var artR = new Rectangle(r.X + pad, r.Y + pad, art, art);
            if (m.Art != null)
            {
                var old = g.InterpolationMode; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var path = Round(artR, 10 * s)) { var clip = g.Clip; g.SetClip(path); g.DrawImage(m.Art, artR); g.Clip = clip; }
                g.InterpolationMode = old;
            }
            else
            {
                Fill(g, artR, 10 * s, Color.FromArgb(34, 255, 255, 255));
                Glyph(g, "", art * 0.35f, artR, Sub);
            }
            int x = artR.Right + pad, w = r.Right - pad - x;
            if (!m.HasSession)
            {
                Label(g, "Nothing is playing", U.UIBold(13 * s), new Rectangle(x, r.Y, w, r.Height), Sub);
                return;
            }
            Label(g, m.Title == "" ? "Unknown title" : m.Title, U.UIBold(13.5f * s), new Rectangle(x, r.Y + pad, w, (int)(22 * s)), Text);
            Label(g, m.Artist, U.UI(11.5f * s), new Rectangle(x, r.Y + pad + (int)(22 * s), w, (int)(18 * s)), Sub);
            // progress, when the app reports it
            if (m.Duration > 0)
            {
                var bar = new RectangleF(x, r.Y + pad + (int)(46 * s), w, 4 * s);
                Fill(g, bar, 2 * s, Track);
                Fill(g, new RectangleF(bar.X, bar.Y, Math.Max(4 * s, bar.Width * (float)U.Clamp(m.PositionNow / m.Duration, 0, 1)), bar.Height), 2 * s, Theme.Accent);
            }
            int b = (int)(34 * s), gap = (int)(10 * s), by = r.Bottom - pad - b, bx = x;
            Button(g, s, new Rectangle(bx, by, b, b), "", "prev", false);
            Button(g, s, new Rectangle(bx + b + gap, by, b, b), m.Playing ? "" : "", "play", true);
            Button(g, s, new Rectangle(bx + 2 * (b + gap), by, b, b), "", "next", false);
        }

        void Button(Graphics g, float s, Rectangle r, string glyph, string key, bool main)
        {
            var c = main ? Theme.Accent : Color.FromArgb(40, 255, 255, 255);
            Fill(g, r, r.Height / 2f, c);
            Glyph(g, glyph, 13 * s, r, main ? Theme.OnAccent : Text);
            hits.Add(new Hit { R = r, Kind = "button", Key = key });
        }

        public override void MouseDown(Point p)
        {
            var h = HitAt(p);
            if (h != null) MediaNow.Command(h.Key);
        }
    }

    /// <summary>Windows' media session (what the volume flyout shows): polled once a second while a Now playing PiP is open.</summary>
    static class MediaNow
    {
        public class Info
        {
            public bool HasSession, Playing;
            public string Title = "", Artist = "";
            public double Position, Duration;
            public DateTime At = DateTime.UtcNow;
            public Bitmap Art;
            public double PositionNow { get { return Playing ? Position + (DateTime.UtcNow - At).TotalSeconds : Position; } }
        }

        public static volatile Info Current = new Info();
        static int users;
        static CancellationTokenSource cts;
        static Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager manager;
        static string artFor;

        public static void Use(bool on)
        {
            users = Math.Max(0, users + (on ? 1 : -1));
            if (users > 0 && cts == null) { cts = new CancellationTokenSource(); var t = cts.Token; Task.Run(() => Loop(t)); }
            if (users == 0 && cts != null) { cts.Cancel(); cts = null; }
        }

        static async Task Loop(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try { await Poll(); } catch { }
                try { await Task.Delay(1000, stop); } catch { return; }
            }
        }

        static async Task Poll()
        {
            if (manager == null) manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var ses = manager.GetCurrentSession();
            var n = new Info();
            if (ses != null)
            {
                n.HasSession = true;
                var pb = ses.GetPlaybackInfo();
                n.Playing = pb != null && pb.PlaybackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var props = await ses.TryGetMediaPropertiesAsync();
                if (props != null) { n.Title = props.Title ?? ""; n.Artist = props.Artist ?? ""; }
                var tl = ses.GetTimelineProperties();
                if (tl != null)
                {
                    n.Duration = (tl.EndTime - tl.StartTime).TotalSeconds;
                    n.Position = (tl.Position - tl.StartTime).TotalSeconds + (n.Playing ? (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds : 0);
                    if (n.Duration <= 1 || n.Position < 0 || n.Position > n.Duration + 5) n.Duration = 0;
                }
                // the cover, once per track
                string key = n.Title + "\u0001" + n.Artist;
                var prev = Current;
                if (key == artFor) n.Art = prev.Art;
                else
                {
                    artFor = key;
                    try
                    {
                        if (props != null && props.Thumbnail != null)
                        {
                            var ras = await props.Thumbnail.OpenReadAsync();
                            using (var st = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(ras))
                            using (var ms = new System.IO.MemoryStream())
                            {
                                st.CopyTo(ms); ms.Position = 0;
                                using (var img = Image.FromStream(ms)) n.Art = new Bitmap(img);
                            }
                        }
                    }
                    catch { n.Art = null; }
                }
            }
            n.At = DateTime.UtcNow;
            Current = n;
        }

        public static void Command(string what)
        {
            Task.Run(async () =>
            {
                try
                {
                    if (manager == null) manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    var ses = manager.GetCurrentSession();
                    if (ses == null) return;
                    if (what == "play") await ses.TryTogglePlayPauseAsync();
                    else if (what == "next") await ses.TrySkipNextAsync();
                    else if (what == "prev") await ses.TrySkipPreviousAsync();
                    await Task.Delay(250);
                    await Poll();
                }
                catch { }
            });
        }
    }
}
