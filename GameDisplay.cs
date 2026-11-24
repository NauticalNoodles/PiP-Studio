using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PipStudio
{
    /// <summary>
    /// Side by side with a virtual display (experimental): the game runs full screen on a second, invisible display (a virtual
    /// display driver, installed separately), sized to the space beside the strip. PiP Studio shows the game's window there,
    /// live, the way it shows a PiP (Windows composes it: no copying, no delay to speak of), with the PiPs in the strip. Works
    /// for games that won't run in a smaller window. Keyboard and controller reach the game; the mouse is on the other display.
    /// </summary>
    static class GameDisplay
    {
        public const string DriverUrl = "https://github.com/VirtualDrivers/Virtual-Display-Driver";

        /// <summary>For the screenshots: show this window as the game, wherever it is.</summary>
        public static IntPtr Override;

        /// <summary>The display picked for the game (Settings), if it's connected and isn't the one the strip is on.</summary>
        public static Screen Target(Screen main)
        {
            var name = App.S.PbpGameDisplay ?? "";
            var s = Screen.AllScreens.FirstOrDefault(x => x.DeviceName == name);
            return s != null && (main == null || s.DeviceName != main.DeviceName) ? s : null;
        }

        /// <summary>The window to show: the game full screen on that display (or the top one there).</summary>
        public static IntPtr FindGame(Screen t)
        {
            if (Override != IntPtr.Zero && Native.IsWindow(Override)) return Override;
            if (t == null) return IntPtr.Zero;
            IntPtr first = IntPtr.Zero;
            foreach (var h in WinEnum.AltTab())
            {
                if (U.IsOurs(h) || Native.IsIconic(h)) continue;
                Screen s;
                try { s = Screen.FromHandle(h); } catch { continue; }
                if (s.DeviceName != t.DeviceName) continue;
                if (U.CoversScreen(h)) return h;
                if (first == IntPtr.Zero) first = h;
            }
            return first;
        }

        /// <summary>"Display 2: Virtual Display (1288 × 1080)" for every display but the main one.</summary>
        public static List<Tuple<string, string>> Choices()
        {
            var list = new List<Tuple<string, string>>();
            foreach (var s in Screen.AllScreens)
            {
                string n = s.DeviceName.Replace(@"\\.\DISPLAY", L.T("Display") + " ");
                string mon = MonitorName(s.DeviceName);
                list.Add(Tuple.Create(s.DeviceName, n + (mon.Length > 0 ? ": " + mon : "") + " (" + s.Bounds.Width + " × " + s.Bounds.Height + ")" + (s.Primary ? " · " + L.T("main") : "")));
            }
            return list;
        }

        /// <summary>
        /// Sets the game display to the resolution with the closest shape to the space beside the strip (it has to offer one:
        /// virtual display drivers let you add your own). The message says what happened.
        /// </summary>
        public static string MatchResolution(Screen t, Size want)
        {
            if (t == null) return L.T("Pick the game display first.");
            var modes = new HashSet<Size>();
            var dm = NewMode();
            for (int i = 0; EnumDisplaySettings(t.DeviceName, i, ref dm); i++) modes.Add(new Size(dm.dmPelsWidth, dm.dmPelsHeight));
            if (modes.Count == 0) return L.T("That display doesn't list any resolutions.");
            double aim = want.Width / (double)want.Height;
            var best = modes.OrderBy(m => Math.Abs(m.Width / (double)m.Height - aim)).ThenBy(m => Math.Abs(m.Height - want.Height)).First();
            string wantText = want.Width + " × " + want.Height;
            if (Math.Abs(best.Width / (double)best.Height - aim) > 0.04)
                return L.T("It has no resolution of that shape. Add {0} in the virtual display driver's settings, then try again.").Replace("{0}", wantText);
            var cur = NewMode();
            EnumDisplaySettings(t.DeviceName, ENUM_CURRENT_SETTINGS, ref cur);
            if (cur.dmPelsWidth == best.Width && cur.dmPelsHeight == best.Height) return L.T("It's already at {0}.").Replace("{0}", best.Width + " × " + best.Height);
            cur.dmPelsWidth = best.Width; cur.dmPelsHeight = best.Height;
            cur.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;
            int r = ChangeDisplaySettingsEx(t.DeviceName, ref cur, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            return r == 0 ? L.T("Set to {0}. Now set the game to the same, full screen, on that display.").Replace("{0}", best.Width + " × " + best.Height)
                          : L.T("Windows didn't change it (error {0}).").Replace("{0}", r.ToString());
        }

        static string MonitorName(string device)
        {
            try
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                return EnumDisplayDevices(device, 0, ref dd, 0) ? (dd.DeviceString ?? "").Trim() : "";
            }
            catch { return ""; }
        }

        static DEVMODE NewMode() { return new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) }; }

        const int ENUM_CURRENT_SETTINGS = -1, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, CDS_UPDATEREGISTRY = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string device, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, int index, ref DISPLAY_DEVICE dd, int flags);
    }

    /// <summary>The game, shown live beside the strip (a DWM thumbnail of its window, like a PiP's): kept in shape, black around it.
    /// Clicking it hands the keyboard to the game.</summary>
    class GameView : Form
    {
        IntPtr thumb, src;

        public GameView()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Text = "PiP Studio game display";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            U.ExcludeFromCapture(Handle, App.S.HideFromCapture);
        }

        public IntPtr Source { get { return src; } }

        public void SetSource(IntPtr h)
        {
            if (h == src) { Fit(); return; }
            if (thumb != IntPtr.Zero) { Native.DwmUnregisterThumbnail(thumb); thumb = IntPtr.Zero; }
            src = h;
            if (h != IntPtr.Zero && IsHandleCreated && Native.DwmRegisterThumbnail(Handle, h, out thumb) != 0) thumb = IntPtr.Zero;
            Fit();
            Invalidate();
        }

        void Fit()
        {
            if (thumb == IntPtr.Zero) return;
            Native.SIZE sz;
            Native.DwmQueryThumbnailSourceSize(thumb, out sz);
            var c = ClientRectangle;
            Rectangle dst = c;
            if (sz.cx > 0 && sz.cy > 0)
            {
                double k = Math.Min(c.Width / (double)sz.cx, c.Height / (double)sz.cy);
                int w = (int)(sz.cx * k), h = (int)(sz.cy * k);
                dst = new Rectangle(c.X + (c.Width - w) / 2, c.Y + (c.Height - h) / 2, w, h);
            }
            var p = new Native.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY | Native.DWM_TNP_SOURCECLIENTAREAONLY,
                rcDestination = Native.RECT.From(dst), opacity = 255, fVisible = true, fSourceClientAreaOnly = true
            };
            Native.DwmUpdateThumbnailProperties(thumb, ref p);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            if (src != IntPtr.Zero) return;
            // nothing on the game display yet: how to get the game there
            var r = Rectangle.Inflate(ClientRectangle, -ClientSize.Width / 8, 0);
            float s = U.ScaleOf(Handle);
            LT.DrawText(g, "Waiting for the game", U.UIBold(20 * s), new Rectangle(r.X, r.Y + r.Height / 2 - (int)(60 * s), r.Width, (int)(40 * s)), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            LT.DrawText(g, "Start it on the game display, full screen: pick that display in the game's settings, or press Windows + Shift + arrow while it's in front.",
                U.UI(12 * s), new Rectangle(r.X, r.Y + r.Height / 2 - (int)(10 * s), r.Width, (int)(80 * s)), Color.FromArgb(170, 170, 180), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (src != IntPtr.Zero) U.ForceForeground(src);             // the keyboard goes to the game
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (thumb != IntPtr.Zero) { try { Native.DwmUnregisterThumbnail(thumb); } catch { } thumb = IntPtr.Zero; }
            base.Dispose(disposing);
        }
    }
}
