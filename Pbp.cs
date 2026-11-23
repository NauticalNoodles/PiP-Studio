using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PipStudio
{
    /// <summary>
    /// Side by side (picture-by-picture), experimental: a strip along one edge of a display (the right third by default) is
    /// reserved for PiPs, the way the taskbar reserves its own: Windows fits maximized apps beside it, PiP Studio shrinks
    /// full-screen windows (borderless games) to the rest of the display, and the PiPs on that display stack in the strip.
    /// Turning it off gives the space back, and puts the windows and PiPs back where they were.
    /// </summary>
    static class Pbp
    {
        public static bool Active { get { return bar != null; } }
        /// <summary>Where the PiPs go while it's on.</summary>
        public static Rectangle Strip { get { return strip; } }
        /// <summary>The full-screen windows it made smaller, and where they were.</summary>
        public static Dictionary<IntPtr, Rectangle> Shrunk { get { return new Dictionary<IntPtr, Rectangle>(shrunk); } }
        static Screen screen;
        static Rectangle strip;                                  // where the PiPs go
        static BarWindow bar;
        static readonly Dictionary<IntPtr, Rectangle> shrunk = new Dictionary<IntPtr, Rectangle>();   // full-screen windows made smaller: where they were
        static readonly HashSet<IntPtr> refused = new HashSet<IntPtr>();                                // ones that wouldn't shrink
        static readonly Dictionary<PipWindow, Rectangle> before = new Dictionary<PipWindow, Rectangle>(); // PiPs: where they were
        public static event Action Changed;

        /// <summary>The area PiPs use on a display: the strip while side by side is on there, otherwise the usual work area.</summary>
        public static Rectangle AreaOf(Screen s) { return Active && screen != null && s.DeviceName == screen.DeviceName ? strip : s.WorkingArea; }
        public static Rectangle AreaOf(Rectangle r) { return AreaOf(Screen.FromRectangle(r)); }

        public static void Toggle() { if (Active) Off(); else On(); }

        public static void On()
        {
            if (Active) return;
            var app = App.I;
            var cur = app.CurrentPip();
            screen = cur != null ? Screen.FromRectangle(cur.TargetBounds) : Screen.FromPoint(Cursor.Position);
            var mb = screen.Bounds;
            var oldWork = screen.WorkingArea;
            int side = U.Clamp(App.S.PbpSide, 0, 2);
            double frac = U.Clamp(App.S.PbpPercent, 15, 60) / 100.0;
            uint edge = side == 0 ? ABE_RIGHT : side == 1 ? ABE_LEFT : ABE_BOTTOM;
            var want = side == 2 ? new Rectangle(mb.Left, mb.Bottom - (int)(mb.Height * frac), mb.Width, (int)(mb.Height * frac))
                                 : new Rectangle(side == 0 ? mb.Right - (int)(mb.Width * frac) : mb.Left, mb.Top, (int)(mb.Width * frac), mb.Height);
            bar = new BarWindow();
            var got = bar.Reserve(edge, want);
            if (got.Width < 50 || got.Height < 50) { bar.Release(); bar = null; app.Balloon("Side by side didn't start", "Windows wouldn't reserve that part of the screen.", true); return; }
            strip = Rectangle.Intersect(got, oldWork);           // not under the taskbar
            if (strip.Width < 50 || strip.Height < 50) strip = got;
            ShrinkFullScreen();
            Arrange(true);
            Raise();
        }

        public static void Off()
        {
            if (!Active) return;
            try { bar.Release(); } catch { }
            bar = null;
            // the windows it shrank go back once Windows has given the space back (before that, it would squeeze them again)
            var back = new Dictionary<IntPtr, Rectangle>(shrunk);
            shrunk.Clear(); refused.Clear();
            Action restore = () =>
            {
                foreach (var kv in back)
                    if (Native.IsWindow(kv.Key))
                        try { Native.SetWindowPos(kv.Key, IntPtr.Zero, kv.Value.X, kv.Value.Y, kv.Value.Width, kv.Value.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE); } catch { }
            };
            restore();
            App.RunSoon(restore, 400);
            foreach (var kv in before)
                if (!kv.Key.IsClosing && !kv.Key.IsDisposed) kv.Key.MoveTo(kv.Value, true);
            before.Clear();
            screen = null;
            Raise();
        }

        /// <summary>Every second while it's on: a full-screen window that appeared (a game you started) is shrunk too.</summary>
        public static void Tick()
        {
            if (!Active) return;
            ShrinkFullScreen();
            foreach (var h in shrunk.Keys.Where(h => !Native.IsWindow(h)).ToList()) shrunk.Remove(h);
        }

        /// <summary>The game area: the display minus the strip.</summary>
        static Rectangle Rest()
        {
            var mb = screen.Bounds;
            int side = U.Clamp(App.S.PbpSide, 0, 2);
            if (side == 2) return new Rectangle(mb.Left, mb.Top, mb.Width, strip.Top - mb.Top);
            return side == 0 ? new Rectangle(mb.Left, mb.Top, strip.Left - mb.Left, mb.Height) : new Rectangle(strip.Right, mb.Top, mb.Right - strip.Right, mb.Height);
        }

        static void ShrinkFullScreen()
        {
            foreach (var h in WinEnum.AltTab())
            {
                if (U.IsOurs(h) || shrunk.ContainsKey(h) || refused.Contains(h) || Native.IsIconic(h)) continue;
                Screen s;
                try { s = Screen.FromHandle(h); } catch { continue; }
                if (s.DeviceName != screen.DeviceName) continue;
                var wr = U.WindowRect(h);
                bool covers = wr.Left <= s.Bounds.Left && wr.Top <= s.Bounds.Top && wr.Right >= s.Bounds.Right && wr.Bottom >= s.Bounds.Bottom;
                if (!covers) continue;                           // maximized apps fit beside the strip by themselves
                var r = Rest();
                Native.SetWindowPos(h, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                var now = U.WindowRect(h);
                if (now.Width < wr.Width - 20 || now.Height < wr.Height - 20) shrunk[h] = wr;
                else
                {
                    refused.Add(h);                              // exclusive full screen, or a game that keeps its own size
                    App.I.Balloon(L.T("Couldn't make {0} smaller").Replace("{0}", ExternalPip.AppName(h)),
                        "It runs in exclusive full screen or keeps its own size. Switch it to borderless or windowed mode in its settings.", true);
                }
            }
        }

        /// <summary>Stacks the PiPs on that display in the strip, as big as fits (all the same width, each its own shape).</summary>
        public static void Arrange(bool remember)
        {
            if (!Active) return;
            // hidden ones too (hidden while their window is in front, or Hide all): they come back in the strip
            var pips = App.I.Pips.Where(p => !p.IsClosing && !p.InTheater &&
                                             (Screen.FromRectangle(p.TargetBounds).DeviceName == screen.DeviceName || before.ContainsKey(p))).ToList();
            if (pips.Count == 0) return;
            pips = pips.OrderBy(p => p.TargetBounds.Top).ThenBy(p => p.TargetBounds.Left).ToList();
            if (remember) foreach (var p in pips) if (!before.ContainsKey(p)) before[p] = p.TargetBounds;
            int gap = (int)Math.Round(10 * pips[0].UiScale), m = gap;
            bool row = U.Clamp(App.S.PbpSide, 0, 2) == 2;
            var aspects = pips.Select(p => Math.Max(0.2, p.TargetBounds.Width / (double)Math.Max(1, p.TargetBounds.Height))).ToList();
            var slots = row ? Row(aspects, gap, m) : Column(aspects, gap, m);
            for (int i = 0; i < pips.Count; i++) pips[i].MoveTo(slots[i], true);
        }

        static List<Rectangle> Column(List<double> aspects, int gap, int m)
        {
            double w = strip.Width - 2 * m;
            double total = aspects.Sum(a => w / a) + gap * (aspects.Count - 1);
            double avail = strip.Height - 2 * m;
            if (total > avail) w *= (avail - gap * (aspects.Count - 1)) / (total - gap * (aspects.Count - 1));
            var list = new List<Rectangle>();
            double y = strip.Top + m;
            foreach (var a in aspects)
            {
                int ww = (int)w, hh = (int)(w / a);
                list.Add(new Rectangle(strip.Left + (strip.Width - ww) / 2, (int)y, ww, hh));
                y += hh + gap;
            }
            return list;
        }

        static List<Rectangle> Row(List<double> aspects, int gap, int m)
        {
            double h = strip.Height - 2 * m;
            double total = aspects.Sum(a => h * a) + gap * (aspects.Count - 1);
            double avail = strip.Width - 2 * m;
            if (total > avail) h *= (avail - gap * (aspects.Count - 1)) / (total - gap * (aspects.Count - 1));
            var list = new List<Rectangle>();
            double x = strip.Left + m;
            foreach (var a in aspects)
            {
                int hh = (int)h, ww = (int)(h * a);
                list.Add(new Rectangle((int)x, strip.Top + (strip.Height - hh) / 2, ww, hh));
                x += ww + gap;
            }
            return list;
        }

        public static void Forget(PipWindow p) { before.Remove(p); }

        static void Raise() { var c = Changed; if (c != null) c(); App.I.RaiseState(); }

        // ------------------------------------------------------------ the reserved strip (an "app bar", like the taskbar)
        const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3;
        const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
        const int ABN_POSCHANGED = 1;

        [StructLayout(LayoutKind.Sequential)]
        struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage, uEdge;
            public Native.RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);

        /// <summary>An invisible window that holds the reservation (Windows gives the space back if it goes away).</summary>
        class BarWindow : NativeWindow
        {
            readonly uint callback = RegisterWindowMessage("PiPStudio.SideBySide");
            uint edge;
            Rectangle wanted;
            bool registered;

            public BarWindow()
            {
                CreateHandle(new CreateParams { Caption = "PiP Studio side by side", ExStyle = Native.WS_EX_TOOLWINDOW, Style = unchecked((int)0x80000000) });   // WS_POPUP, never shown
            }

            APPBARDATA Data()
            {
                return new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)), hWnd = Handle, uCallbackMessage = callback, uEdge = edge };
            }

            public Rectangle Reserve(uint e, Rectangle r)
            {
                edge = e; wanted = r;
                var d = Data();
                if (!registered) { SHAppBarMessage(ABM_NEW, ref d); registered = true; }
                return Place();
            }

            Rectangle Place()
            {
                var d = Data();
                d.rc = Native.RECT.From(wanted);
                SHAppBarMessage(ABM_QUERYPOS, ref d);           // Windows may move it off other bars on that edge...
                int w = wanted.Width, h = wanted.Height;
                if (edge == ABE_RIGHT) d.rc.Left = d.rc.Right - w;
                else if (edge == ABE_LEFT) d.rc.Right = d.rc.Left + w;
                else if (edge == ABE_BOTTOM) d.rc.Top = d.rc.Bottom - h;
                SHAppBarMessage(ABM_SETPOS, ref d);             // ...and this is where it ends up
                return Rectangle.FromLTRB(d.rc.Left, d.rc.Top, d.rc.Right, d.rc.Bottom);
            }

            public void Release()
            {
                if (registered) { var d = Data(); SHAppBarMessage(ABM_REMOVE, ref d); registered = false; }
                try { DestroyHandle(); } catch { }
            }

            protected override void WndProc(ref Message m)
            {
                if (registered && m.Msg == (int)callback && m.WParam.ToInt64() == ABN_POSCHANGED) { try { Place(); } catch { } }
                base.WndProc(ref m);
            }
        }
    }
}
