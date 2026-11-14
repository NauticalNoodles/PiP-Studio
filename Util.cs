using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PipStudio
{
    // =================================================================================
    //  Helpers
    // =================================================================================
    enum SourceKind { Window, Screen }
    enum FitMode { Fit, Fill, Stretch }
    enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    static class U
    {
        public static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        static readonly int ourPid = Process.GetCurrentProcess().Id;
        static readonly Dictionary<int, string> pidNames = new Dictionary<int, string>();
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static string WindowTitle(IntPtr h)
        {
            int n = Native.GetWindowTextLength(h);
            if (n <= 0) return "";
            var sb = new StringBuilder(n + 2);
            Native.GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string ClassName(IntPtr h)
        {
            var sb = new StringBuilder(256);
            Native.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static int Pid(IntPtr h) { uint pid; Native.GetWindowThreadProcessId(h, out pid); return (int)pid; }

        public static void ClearProcessCache() { lock (pidNames) pidNames.Clear(); }

        public static string ProcessName(int pid)
        {
            string n;
            lock (pidNames) { if (pidNames.TryGetValue(pid, out n)) return n; }
            try { using (var p = Process.GetProcessById(pid)) n = p.ProcessName; } catch { n = ""; }
            lock (pidNames) { pidNames[pid] = n; }
            return n;
        }

        public static string ProcessName(IntPtr h) { return ProcessName(Pid(h)); }
        public static bool IsOurs(IntPtr h) { return Pid(h) == ourPid; }

        /// <summary>DWMWA_CLOAKED: 0 = shown, 1 = hidden by the app itself, 2 = hidden by the shell (another virtual desktop,
        /// or a background window in Xbox mode, which shows one window at a time), 4 = inherited from its owner.</summary>
        public static int CloakState(IntPtr h)
        {
            int v;
            return Native.DwmGetWindowAttribute(h, 14, out v, 4) == 0 ? v : 0;
        }

        public static Rectangle WindowRect(IntPtr h) { Native.RECT r; Native.GetWindowRect(h, out r); return r.ToRectangle(); }

        public static Rectangle FrameRect(IntPtr h)
        {
            Native.RECT r;
            if (Native.DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) == 0) return r.ToRectangle();
            return WindowRect(h);
        }

        public static Rectangle ClientScreenRect(IntPtr h)
        {
            Native.RECT r;
            Native.GetClientRect(h, out r);
            var p = new Native.POINT();
            Native.ClientToScreen(h, ref p);
            return new Rectangle(p.X, p.Y, r.Right, r.Bottom);
        }

        public static Size SourceSize(IntPtr h, bool clientOnly)
        {
            if (h == IntPtr.Zero) return Size.Empty;
            if (clientOnly) { Native.RECT r; Native.GetClientRect(h, out r); return new Size(r.Right, r.Bottom); }
            return WindowRect(h).Size;
        }

        public static float SystemScale()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        public static float ScaleOf(IntPtr hwnd)
        {
            try { uint d = Native.GetDpiForWindow(hwnd); if (d > 0) return d / 96f; } catch { }
            return SystemScale();
        }

        public static float ScaleAt(Point p)
        {
            try
            {
                var pt = new Native.POINT { X = p.X, Y = p.Y };
                IntPtr mon = Native.MonitorFromPoint(pt, 2);
                uint dx, dy;
                if (Native.GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx / 96f;
            }
            catch { }
            return SystemScale();
        }

        public static Point LParamPoint(IntPtr lp)
        {
            long v = lp.ToInt64();
            return new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
        }

        public static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        public static double Approach(double cur, double target, double k)
        {
            double n = cur + (target - cur) * k;
            if (Math.Abs(target - n) < 0.01) n = target;
            return n;
        }

        public static Rectangle Lerp(Rectangle a, Rectangle b, double t)
        {
            return new Rectangle(
                (int)Math.Round(a.X + (b.X - a.X) * t), (int)Math.Round(a.Y + (b.Y - a.Y) * t),
                (int)Math.Round(a.Width + (b.Width - a.Width) * t), (int)Math.Round(a.Height + (b.Height - a.Height) * t));
        }

        public static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Computes where a source rectangle lands inside an area. For Fill, shrinks srcR so it covers the area.</summary>
        public static Rectangle ComputeFit(ref Rectangle srcR, Rectangle area, FitMode mode)
        {
            if (area.Width <= 0 || area.Height <= 0 || srcR.Width <= 0 || srcR.Height <= 0) return area;
            if (mode == FitMode.Stretch) return area;
            double sx = area.Width / (double)srcR.Width, sy = area.Height / (double)srcR.Height;
            if (mode == FitMode.Fit)
            {
                double k = Math.Min(sx, sy);
                int w = Math.Max(1, (int)Math.Round(srcR.Width * k)), h = Math.Max(1, (int)Math.Round(srcR.Height * k));
                return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
            }
            double kk = Math.Max(sx, sy);
            int nw = Math.Max(1, (int)Math.Round(area.Width / kk)), nh = Math.Max(1, (int)Math.Round(area.Height / kk));
            srcR = new Rectangle(srcR.X + (srcR.Width - nw) / 2, srcR.Y + (srcR.Height - nh) / 2, nw, nh);
            return area;
        }

        public static Rectangle FitRect(Size src, Rectangle box)
        {
            var r = new Rectangle(Point.Empty, src);
            return ComputeFit(ref r, box, FitMode.Fit);
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            if (rad <= 0.5f) { p.AddRectangle(r); return p; }
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Font F(string family, float px, FontStyle style)
        {
            string key = family + "|" + px.ToString("0.0", CI) + "|" + (int)style;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                try { f = new Font(family, px, style, GraphicsUnit.Pixel); }
                catch { f = new Font(FontFamily.GenericSansSerif, px, style, GraphicsUnit.Pixel); }
                fonts[key] = f;
            }
            return f;
        }
        public static Font UI(float px) { return F("Segoe UI", px, FontStyle.Regular); }
        public static Font UIBold(float px) { return F("Segoe UI Semibold", px, FontStyle.Regular); }
        static string iconFamily;
        /// <summary>Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10 (same code points).</summary>
        public static string IconFamily
        {
            get
            {
                if (iconFamily == null)
                {
                    iconFamily = "Segoe MDL2 Assets";
                    try
                    {
                        using (var f = new Font("Segoe Fluent Icons", 10f))
                            if (f.Name == "Segoe Fluent Icons") iconFamily = "Segoe Fluent Icons";
                    }
                    catch { }
                }
                return iconFamily;
            }
        }
        public static Font Icons(float px) { return F(IconFamily, px, FontStyle.Regular); }

        public static bool KeyHeld(int vk) { return (Native.GetKeyState(vk) & 0x8000) != 0; }

        /// <summary>Releases unused memory back to Windows (the app mostly idles in the tray).</summary>
        public static void Trim()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                Native.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }

        public static void MediaKey(byte vk)
        {
            Native.keybd_event(vk, 0, 1, UIntPtr.Zero);
            Native.keybd_event(vk, 0, 3, UIntPtr.Zero);
        }

        /// <summary>Animations are on, and Lite mode isn't.</summary>
        public static bool Animations { get { return App.S != null && App.S.Animations && !App.S.LiteMode; } }

        public static void ForceForeground(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            if (!Native.IsWindow(h)) return;
            if (Native.IsIconic(h)) Native.ShowWindow(h, 9);
            if (Native.GetForegroundWindow() == h) return;
            // No fake input of any kind: even an empty mouse event reaches the window under the pointer as "the mouse moved",
            // which makes video players (FreeTube's PiP, YouTube...) pop up their controls.
            if (Native.SetForegroundWindow(h) && Native.GetForegroundWindow() == h) return;

            // No fake key presses here: games see those (an Alt tap switches some to keyboard prompts or opens menus).
            // 1) share input state with the foreground window's thread, which lets us take the foreground
            IntPtr fg = Native.GetForegroundWindow();
            uint dummy;
            uint fgThread = Native.GetWindowThreadProcessId(fg, out dummy);
            uint me = Native.GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && Native.AttachThreadInput(me, fgThread, true);
            Native.BringWindowToTop(h);
            Native.SetForegroundWindow(h);
            if (attached) Native.AttachThreadInput(me, fgThread, false);
            if (Native.GetForegroundWindow() == h) return;

            // 2) games that lock the foreground (LockSetForegroundWindow): switch the way Alt+Tab does, which that lock doesn't block
            try { Native.SwitchToThisWindow(h, true); } catch { }
        }

        public static void RoundCorners(IntPtr h, bool round)
        {
            int pref = round ? 2 : 1;
            try { Native.DwmSetWindowAttribute(h, 33, ref pref, 4); } catch { }
            int none = unchecked((int)0xFFFFFFFE);   // DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE: no system outline
            try { Native.DwmSetWindowAttribute(h, 34, ref none, 4); } catch { }
        }

        public static void ExcludeFromCapture(IntPtr h, bool exclude)
        {
            try
            {
                if (!exclude) { Native.SetWindowDisplayAffinity(h, 0); return; }
                if (!Native.SetWindowDisplayAffinity(h, 0x11)) Native.SetWindowDisplayAffinity(h, 0x1);
            }
            catch { }
        }

        /// <summary>The window's icon handle (shared — never destroy it), or zero.</summary>
        public static IntPtr WindowIconHandle(IntPtr h, bool big)
        {
            IntPtr hi = IntPtr.Zero, res;
            if (Native.SendMessageTimeout(h, 0x7F, new IntPtr(big ? 1 : 2), IntPtr.Zero, 2, 60, out res) != IntPtr.Zero) hi = res;
            if (hi == IntPtr.Zero && Native.SendMessageTimeout(h, 0x7F, new IntPtr(big ? 2 : 1), IntPtr.Zero, 2, 60, out res) != IntPtr.Zero) hi = res;
            if (hi == IntPtr.Zero) hi = Native.GetClassLongPtr(h, big ? -14 : -34);
            if (hi == IntPtr.Zero) hi = Native.GetClassLongPtr(h, big ? -34 : -14);
            return hi;
        }

        public static Icon MakeAppIcon(int size, Color accent)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float s = size / 32f;
                    using (var outer = RoundRect(new RectangleF(1.5f * s, 4.5f * s, 29 * s, 23 * s), 4.5f * s))
                    using (var fill = new SolidBrush(Color.FromArgb(30, 32, 40)))
                    using (var pen = new Pen(Color.FromArgb(230, 232, 240), Math.Max(1.2f, 2f * s)))
                    {
                        g.FillPath(fill, outer);
                        g.DrawPath(pen, outer);
                    }
                    using (var inner = RoundRect(new RectangleF(14.5f * s, 14.5f * s, 13 * s, 10 * s), 2.5f * s))
                    using (var b = new SolidBrush(accent))
                        g.FillPath(b, inner);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        public static string Trunc(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        public static bool LooksBlank(Bitmap b)
        {
            try
            {
                for (int yi = 1; yi < 10; yi++)
                    for (int xi = 1; xi < 10; xi++)
                    {
                        Color c = b.GetPixel(b.Width * xi / 10, b.Height * yi / 10);
                        if (c.R + c.G + c.B > 24) return false;
                    }
            }
            catch { }
            return true;
        }
    }

    static class WinEnum
    {
        static readonly HashSet<string> shellClasses = new HashSet<string> {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "Button"
        };

        public static bool IsAltTab(IntPtr h)
        {
            if (!Native.IsWindowVisible(h)) return false;
            if (U.IsOurs(h)) return false;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            bool app = (ex & Native.WS_EX_APPWINDOW) != 0;
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && !app) return false;
            if ((ex & Native.WS_EX_NOACTIVATE) != 0 && !app) return false;
            if (Native.GetWindow(h, 4) != IntPtr.Zero && !app) return false;
            if (Native.GetWindowTextLength(h) == 0) return false;
            if (ExternalPip.IsAppPip(h)) return false;   // other apps' own PiPs (browser PiPs, Discord pop-outs) are handled separately
            string cls = U.ClassName(h);
            int cloak = U.CloakState(h);
            if (cloak != 0)
            {
                // The shell hides every background window in Xbox mode (and windows on other virtual desktops). Those are
                // real, open windows, so keep them. Skip windows their own app hid, and suspended Store-app frames.
                if ((cloak & 1) != 0) return false;
                if (cls == "ApplicationFrameWindow" || cls == "Windows.UI.Core.CoreWindow") return false;
            }
            if (shellClasses.Contains(cls)) return false;
            return true;
        }

        public static List<IntPtr> AltTab()
        {
            var list = new List<IntPtr>();
            Native.EnumWindows((h, l) => { if (IsAltTab(h)) list.Add(h); return true; }, IntPtr.Zero);
            return list;
        }

        public static IntPtr WindowAt(Point pt)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((h, l) =>
            {
                if (!IsAltTab(h) || Native.IsIconic(h)) return true;
                if (U.FrameRect(h).Contains(pt)) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static IntPtr FindMatch(string proc, string cls, string title)
        {
            IntPtr best = IntPtr.Zero;
            int bestScore = 0;
            title = title ?? "";
            foreach (var h in AltTab())
            {
                if (!string.IsNullOrEmpty(proc) && !string.Equals(U.ProcessName(h), proc, StringComparison.OrdinalIgnoreCase)) continue;
                int s = 1;
                if (!string.IsNullOrEmpty(cls) && U.ClassName(h) == cls) s += 2;
                string t = U.WindowTitle(h);
                if (t == title) s += 4;
                else if (title.Length > 0 && t.Length > 0 && (t.Contains(title) || title.Contains(t))) s += 1;
                if (s > bestScore) { bestScore = s; best = h; }
            }
            int needed = string.IsNullOrEmpty(cls) ? 1 : 3;
            return bestScore >= needed ? best : IntPtr.Zero;
        }
    }

    // =================================================================================
    //  Settings & persistence
    // =================================================================================
    class Settings
    {
        public bool FirstRun = true;
        public bool RestoreSessions = true;
        public bool HideFromCapture = false;      // excluding windows from capture adds compositor work, so it's opt-in
        public bool ShowTips = true;
        public int DefaultCorner = (int)Corner.BottomRight;
        public int DefaultSize = 3;
        public bool LimitWidth = false;            // advanced size limits, as a share of the screen's work area
        public int MaxWidthPercent = 40;
        public bool LimitHeight = true;            // on by default so tall apps (phones, chat windows) don't fill the screen
        public int MaxHeightPercent = 50;
        public int DefaultOpacityPercent = 100;
        public bool SnapToCorners = true;
        public int EdgeMargin = 24;
        public bool Animations = true;
        public bool LiteMode = false;
        public bool ShowExplainers = true;
        public string TitleBarButtons = TitleBarItems.Default;   // the PiP title bar's buttons, left to right
        public bool DetectOtherPips = true;          // small always-on-top windows of other apps count as their PiPs
        public string PipApps = "";                  // "process|class;..." you marked as PiPs
        public string NotPipApps = "";               // "process|class;..." you marked as not PiPs
        public string Language = "";                 // "" follows Windows; en, ar, zh          // animated explanations next to settings
        public bool ControlOtherPips = true;
        public bool SnapOtherPips = true;         // other apps' PiPs snap to PiP Studio's spots when they appear and when you let go of them      // steer Chrome/Firefox/FreeTube's own PiP windows when no PiP Studio PiP is open
        public string GpuPrefsAdded = "";         // left over from the removed GPU option: exe paths to clean up once             // for slower PCs: no animations, Mica, shadows or live previews
        public int HoverAction = 0;                 // 0 controls, 1 fade, 2 nothing, 3 move out of the way (dodge)
        public bool SnapToEdges = true;             // also snap to the middle of each edge (8 spots instead of 4)
        public bool LockToMonitor = false;          // directional moves never hop to another display
        public bool ShowMoveArrows = false;         // arrow buttons on the hover controls
        public int RightClickAction = 0;            // 0 menu, 1 close, 2 bring source to front, 3 theater, 4 nothing
        public bool FollowPreviousDefault = false;  // new PiPs follow the window you just left
        public bool AllowMultiple = false;          // off: opening a new PiP switches the one that's already open
        public bool UseSystemAccent = true;
        public int ThemeMode = 0;                   // 0 follow Windows, 1 light, 2 dark
        public bool MinimizeMemory = true;          // close the main window to the tray instead of keeping it in memory
        public bool CloseToTray = true;
        // Controller
        public bool PadEnabled = true;              // watch for controllers at all
        public bool PadStartOn = false;             // controller controls are on when the app starts
        public bool PadFocusSink = true;            // take focus away from the game while controller controls are on
        public bool PadHints = true;                // button hints on the PiP while controls are on
        public bool PadHintsAutoHide = true;        // controller hints and title fade after a few idle seconds (less for Windows to draw over a game)
        public int PadUiMode = 1;                   // controller-friendly settings window: 0 off, 1 when a controller is used, 2 always
        public string PadBindings = "";             // action=mask:mode;...  (empty = defaults)
        public int HoverFadePercent = 25;
        public int ClickThroughHoverPercent = 15;
        public bool HideWhenSourceActive = true;
        public bool HideInFullscreen = false;
        public bool MiddleClickCloses = true;
        public bool AutoReattach = true;
        public bool KeepAliveMinimized = false;
        public int CaptureFps = 30;
        public bool CaptureCursor = true;
        public bool RoundedCorners = false;
        public bool ShowBorder = false;
        public int SettingsVersion = 0;           // bumped when defaults change so old files get migrated
        public int BorderWidth = 2;
        public bool BorderUseAccent = false;      // border follows the accent color
        public Color BorderColor = Color.Black;
        public bool OsdBelow = true;              // show messages (new source, mode changes) in a caption under the PiP, not over the picture
        public Color AccentColor = Color.FromArgb(76, 141, 255);
        public Color BackgroundColor = Color.Black;

        // Hotkeys: "modifiers,virtualKey"  (Alt=1, Ctrl=2, Shift=4, Win=8)
        public string HK_NewPip = "3,80";        // Ctrl+Alt+P
        public string HK_PipActive = "3,79";     // Ctrl+Alt+O
        public string HK_ScreenRegion = "3,82";  // Ctrl+Alt+R
        public string HK_ToggleAll = "3,72";     // Ctrl+Alt+H
        public string HK_ClickThrough = "3,84";  // Ctrl+Alt+T
        public string HK_CycleCorner = "3,67";   // Ctrl+Alt+C
        public string HK_Grow = "3,187";         // Ctrl+Alt+=
        public string HK_Shrink = "3,189";       // Ctrl+Alt+-
        public string HK_OpacityUp = "3,33";     // Ctrl+Alt+PgUp
        public string HK_OpacityDown = "3,34";   // Ctrl+Alt+PgDn
        public string HK_Theater = "3,70";       // Ctrl+Alt+F
        public string HK_Swap = "3,83";          // Ctrl+Alt+S
        public string HK_Mute = "3,77";          // Ctrl+Alt+M
        public string HK_VolumeUp = "7,38";      // Ctrl+Alt+Shift+Up
        public string HK_VolumeDown = "7,40";    // Ctrl+Alt+Shift+Down
        public string HK_PlayPause = "3,32";     // Ctrl+Alt+Space
        public string HK_Crop = "3,75";          // Ctrl+Alt+K
        public string HK_NextMonitor = "3,78";   // Ctrl+Alt+N
        public string HK_Close = "3,88";         // Ctrl+Alt+X
        public string HK_MoveUp = "3,38";        // Ctrl+Alt+Up
        public string HK_MoveDown = "3,40";      // Ctrl+Alt+Down
        public string HK_MoveLeft = "3,37";      // Ctrl+Alt+Left
        public string HK_MoveRight = "3,39";     // Ctrl+Alt+Right
        public string HK_PrevSource = "3,219";   // Ctrl+Alt+[
        public string HK_NextSource = "3,221";   // Ctrl+Alt+]
        public string HK_Follow = "";
        public string HK_Dodge = "";
        public string HK_Pad = "3,71";           // Ctrl+Alt+G  (controller controls on/off)
        public string HK_StartPip = "3,73";      // Ctrl+Alt+I  (start a PiP on Auto when none is open)
        public string HK_ShowApp = "3,65";       // Ctrl+Alt+A  (open PiP Studio)

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PiPStudio"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public Settings Clone() { return (Settings)MemberwiseClone(); }

        public static Settings Load()
        {
            var s = new Settings();
            try { if (File.Exists(FilePath)) Ser.Read(s, Ser.ParseKv(File.ReadAllLines(FilePath))); } catch { }
            if (s.SettingsVersion < 1) { s.RoundedCorners = false; s.ShowBorder = false; s.SettingsVersion = 1; }
            if (s.SettingsVersion < 2) { s.ShowMoveArrows = false; s.SettingsVersion = 2; }
            if (s.SettingsVersion < 3)
            {
                s.SnapToEdges = true;                                   // 8 snap spots by default
                if (s.BorderUseAccent) { s.BorderUseAccent = false; s.BorderColor = Color.Black; }   // black border by default (a picked color is kept)
                s.SettingsVersion = 3;
            }
            if (s.SettingsVersion < 4)
            {
                int[] map = { 0, 2, 4, 6 };                              // 4 sizes became 7 (Small, Small+, Medium, ...)
                s.DefaultSize = map[U.Clamp(s.DefaultSize, 0, 3)];
                s.SettingsVersion = 4;
            }
            if (s.SettingsVersion < 5)
            {
                s.DefaultSize = U.Clamp(s.DefaultSize + 1, 0, 7);           // Extra small was added in front
                s.SettingsVersion = 5;
            }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("; PiP Studio settings");
                Ser.Write(this, sb);
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }
    }

    class PipState
    {
        public SourceKind Kind = SourceKind.Window;
        public string ProcessName = "", ClassName = "", Title = "";
        public Rectangle ScreenRect;
        public RectangleF Crop = new RectangleF(0, 0, 1, 1);
        public bool ClientOnly = true;
        public double Opacity = 1.0;
        public FitMode Fit = FitMode.Fit;
        public bool ClickThrough = false;
        public bool Locked = false;
        public bool TopMost = true;
        public Rectangle Bounds;
        public bool HasBounds = false;
        public bool Follow = false;               // switch to the window you just left whenever you change windows
        public bool Auto = false;                 // Auto source: starts on the first available window, always follows the last active one
        public int Aspect = 0;                    // 0 = the picture's own shape, otherwise one of AspectChoices
        public int Snap = 0;                      // 0 = follow Settings, 1 = always snap, 2 = never snap
        public bool TrimBars = false;             // crop away the black bars around the video

        public PipState Clone() { return (PipState)MemberwiseClone(); }
        public bool IsCropped { get { return Crop.X > 0.0005f || Crop.Y > 0.0005f || Crop.Width < 0.999f || Crop.Height < 0.999f; } }
    }

    static class Ser
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public static string ToStr(object v)
        {
            if (v == null) return "";
            if (v is bool) return (bool)v ? "1" : "0";
            if (v is Color) return ((Color)v).ToArgb().ToString(CI);
            if (v is Rectangle) { var r = (Rectangle)v; return string.Join(",", new[] { r.X, r.Y, r.Width, r.Height }.Select(i => i.ToString(CI))); }
            if (v is RectangleF) { var r = (RectangleF)v; return string.Join(",", new[] { r.X, r.Y, r.Width, r.Height }.Select(f => f.ToString("R", CI))); }
            if (v is double) return ((double)v).ToString("R", CI);
            if (v is float) return ((float)v).ToString("R", CI);
            if (v is Enum) return v.ToString();
            if (v is string) return ((string)v).Replace("\r", " ").Replace("\n", " ");
            return Convert.ToString(v, CI);
        }

        public static object FromStr(string s, Type t)
        {
            if (t == typeof(string)) return s;
            if (t == typeof(bool)) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (t == typeof(int)) return int.Parse(s, CI);
            if (t == typeof(double)) return double.Parse(s, CI);
            if (t == typeof(float)) return float.Parse(s, CI);
            if (t == typeof(Color)) return Color.FromArgb(int.Parse(s, CI));
            if (t == typeof(Rectangle))
            {
                var p = s.Split(',');
                return new Rectangle(int.Parse(p[0], CI), int.Parse(p[1], CI), int.Parse(p[2], CI), int.Parse(p[3], CI));
            }
            if (t == typeof(RectangleF))
            {
                var p = s.Split(',');
                return new RectangleF(float.Parse(p[0], CI), float.Parse(p[1], CI), float.Parse(p[2], CI), float.Parse(p[3], CI));
            }
            if (t.IsEnum) return Enum.Parse(t, s, true);
            throw new NotSupportedException();
        }

        public static void Write(object o, StringBuilder sb)
        {
            foreach (var f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                sb.AppendLine(f.Name + "=" + ToStr(f.GetValue(o)));
        }

        public static void Read(object o, Dictionary<string, string> kv)
        {
            foreach (var f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string v;
                if (!kv.TryGetValue(f.Name, out v)) continue;
                try { f.SetValue(o, FromStr(v, f.FieldType)); } catch { }
            }
        }

        public static Dictionary<string, string> ParseKv(IEnumerable<string> lines)
        {
            var d = new Dictionary<string, string>();
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                d[line.Substring(0, i).Trim()] = line.Substring(i + 1);
            }
            return d;
        }
    }

    static class HK
    {
        public static bool Parse(string spec, out int mods, out int vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(spec)) return false;
            var p = spec.Split(',');
            if (p.Length != 2) return false;
            return int.TryParse(p[0], NumberStyles.Integer, U.CI, out mods) && int.TryParse(p[1], NumberStyles.Integer, U.CI, out vk);
        }

        public static string Spec(int mods, int vk) { return mods.ToString(U.CI) + "," + vk.ToString(U.CI); }

        public static string KeyName(int vk)
        {
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F).ToString(U.CI);
            if (vk >= 0x60 && vk <= 0x69) return "Num " + (vk - 0x60).ToString(U.CI);
            switch (vk)
            {
                case 0xBB: return "="; case 0xBD: return "-"; case 0xDB: return "["; case 0xDD: return "]";
                case 0xBA: return ";"; case 0xDE: return "'"; case 0xBC: return ","; case 0xBE: return ".";
                case 0xBF: return "/"; case 0xC0: return "`"; case 0xDC: return "\\";
                case 0x20: return "Space"; case 0x21: return "PgUp"; case 0x22: return "PgDn";
                case 0x23: return "End"; case 0x24: return "Home"; case 0x25: return "Left"; case 0x26: return "Up";
                case 0x27: return "Right"; case 0x28: return "Down"; case 0x2D: return "Insert"; case 0x2E: return "Delete";
                case 0x6B: return "Num +"; case 0x6D: return "Num -"; case 0x6A: return "Num *"; case 0x6F: return "Num /";
            }
            return ((Keys)vk).ToString();
        }

        public static string Format(int mods, int vk)
        {
            if (vk == 0) return "(none)";
            var sb = new StringBuilder();
            if ((mods & 8) != 0) sb.Append("Win+");
            if ((mods & 2) != 0) sb.Append("Ctrl+");
            if ((mods & 1) != 0) sb.Append("Alt+");
            if ((mods & 4) != 0) sb.Append("Shift+");
            sb.Append(KeyName(vk));
            return sb.ToString();
        }

        public static string Get(Settings s, string field)
        {
            var f = typeof(Settings).GetField(field);
            if (f == null) return "";
            return (f.GetValue(s) as string) ?? "";
        }

        public static void Set(Settings s, string field, string value)
        {
            var f = typeof(Settings).GetField(field);
            if (f != null) f.SetValue(s, value);
        }

        public static string Display(string field)
        {
            int m, vk;
            return Parse(Get(App.S, field), out m, out vk) && vk != 0 ? Format(m, vk) : "";
        }
    }

    // =================================================================================
    //  Theme (follows Windows light/dark and the Windows accent colour)
    // =================================================================================
    static class Theme
    {
        public static bool Dark = true;
        public static Color Accent, AccentBase, Bg, Layer, Card, CardHover, Stroke, Text, SubText, Disabled;
        public static event Action Changed;

        static Theme() { Refresh(); }

        static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        public static bool SystemDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    if (v is int && (int)v == 1) return false;
                }
            }
            catch { }
            return true;
        }

        public static Color SystemAccent()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    object v = k == null ? null : k.GetValue("AccentColor");
                    if (v is int)
                    {
                        int abgr = (int)v;
                        return Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                    }
                }
            }
            catch { }
            return Color.FromArgb(0, 120, 212);
        }

        public static void Refresh()
        {
            int mode = App.S != null ? App.S.ThemeMode : 0;
            Dark = mode == 2 || (mode == 0 && SystemDark());
            AccentBase = (App.S == null || App.S.UseSystemAccent) ? SystemAccent() : App.S.AccentColor;
            // Windows uses a lighter shade of the accent on dark surfaces and a deeper one on light surfaces
            Accent = Dark ? U.Blend(AccentBase, Color.White, 0.30) : U.Blend(AccentBase, Color.Black, 0.08);
            if (Dark)
            {
                Bg = Hex(0x202020); Layer = Hex(0x2C2C2C); Card = Hex(0x2B2B2B); CardHover = Hex(0x3A3A3A);
                Stroke = Hex(0x404040); Text = Hex(0xFFFFFF); SubText = Hex(0xC8C8C8); Disabled = Hex(0x7A7A7A);
            }
            else
            {
                Bg = Hex(0xF3F3F3); Layer = Hex(0xF9F9F9); Card = Hex(0xFFFFFF); CardHover = Hex(0xEAEAEA);
                Stroke = Hex(0xE0E0E0); Text = Hex(0x1B1B1B); SubText = Hex(0x5C5C5C); Disabled = Hex(0xA0A0A0);
            }
            var c = Changed;
            if (c != null) c();
        }

        /// <summary>Black or white, whichever reads better on the accent colour.</summary>
        public static Color OnAccent
        {
            get
            {
                double l = (0.2126 * Accent.R + 0.7152 * Accent.G + 0.0722 * Accent.B) / 255.0;
                return l > 0.55 ? Color.Black : Color.White;
            }
        }
    }

    // =================================================================================
    //  Windows 11 style menus (rounded, roomy, light or dark)
    // =================================================================================
    class FluentColors : ProfessionalColorTable
    {
        public FluentColors() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return Theme.Layer; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Layer; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Layer; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Layer; } }
        public override Color MenuBorder { get { return Theme.Stroke; } }
        public override Color MenuItemBorder { get { return Theme.CardHover; } }
        public override Color MenuItemSelected { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.CardHover; } }
        public override Color MenuItemPressedGradientMiddle { get { return Theme.CardHover; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.CardHover; } }
        public override Color SeparatorDark { get { return Theme.Stroke; } }
        public override Color SeparatorLight { get { return Theme.Stroke; } }
        public override Color CheckBackground { get { return Theme.Layer; } }
        public override Color CheckSelectedBackground { get { return Theme.CardHover; } }
        public override Color CheckPressedBackground { get { return Theme.CardHover; } }
    }

    class FluentMenuRenderer : ToolStripProfessionalRenderer
    {
        public FluentMenuRenderer() : base(new FluentColors()) { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Layer)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (!(e.ToolStrip is ToolStripDropDown)) return;
            using (var p = new Pen(Theme.Stroke)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using (var path = U.RoundRect(r, 4))
            using (var b = new SolidBrush(Theme.CardHover))
                g.FillPath(b, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Theme.Stroke)) e.Graphics.DrawLine(p, 0, y, e.Item.Width, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var mi = e.Item as ToolStripMenuItem;
            bool isShortcut = mi != null && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString) && e.Text == mi.ShortcutKeyDisplayString;
            e.TextColor = !e.Item.Enabled ? Theme.Disabled : (isShortcut ? Theme.SubText : Theme.Text);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.SubText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Theme.Accent, Math.Max(1.6f, r.Width / 9f)))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                g.DrawLines(p, new[] {
                    new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                    new PointF(r.Left + r.Width * 0.80f, r.Top + r.Height * 0.30f) });
            }
        }
    }

    /// <summary>The buttons the PiP title bar can show, in the order Settings lists them. "More options" always stays,
    /// so the full menu is never out of reach.</summary>
    static class TitleBarItems
    {
        public static readonly string[] Keys = { "prev", "next", "change", "theater", "crop", "corner", "display", "source", "ghost", "more", "close" };
        public static readonly string[] Labels = { "Previous window", "Next window", "Change source…", "Theater mode", "Crop", "Next corner", "Next display", "Bring source to front", "Click-through (ghost mode)", "More options", "Close" };
        public static readonly string[] Glyphs = { "\uE892", "\uE893", "\uE8AB", "\uE740", "\uE7A8", "\uE8B3", "\uE7F4", "\uE8A7", "\uED1A", "\uE712", "\uE8BB" };
        public const string Default = "prev,next,theater,crop,source,ghost,more,close";
        public const string Required = "more";

        /// <summary>The buttons that are on, left to right (unknown names dropped, More options always included).</summary>
        public static List<string> On(string setting)
        {
            var l = new List<string>();
            foreach (var k in (setting ?? Default).Split(','))
            {
                string t = k.Trim();
                if (Array.IndexOf(Keys, t) >= 0 && !l.Contains(t)) l.Add(t);
            }
            if (!l.Contains(Required)) l.Insert(l.Contains("close") ? l.IndexOf("close") : l.Count, Required);
            return l;
        }

        public static string Label(string key) { int i = Array.IndexOf(Keys, key); return i < 0 ? key : Labels[i]; }
        public static string Glyph(string key) { int i = Array.IndexOf(Keys, key); return i < 0 ? "" : Glyphs[i]; }
    }

    /// <summary>Shapes a PiP can be locked to (Running PiPs > options). 0 follows the picture.</summary>
    static class AspectChoices
    {
        public static readonly string[] Names = { "Match the picture", "16:9", "4:3", "21:9", "1:1", "9:16" };
        static readonly double[] Values = { 0, 16.0 / 9, 4.0 / 3, 21.0 / 9, 1, 9.0 / 16 };
        public static double Value(int i) { return i > 0 && i < Values.Length ? Values[i] : 0; }
    }

    /// <summary>The 8 PiP sizes, set by width (a share of the screen) so a PiP keeps its shape. Shared by PiP Studio's PiPs and other apps' PiPs.</summary>
    static class SizePresets
    {
        public static readonly string[] Names = { "Extra small", "Small", "Small+", "Medium", "Medium+", "Large", "Large+", "Extra large" };
        public static readonly double[] Fr = { 0.14, 0.18, 0.215, 0.25, 0.29, 0.33, 0.39, 0.45 };
        public static int Count { get { return Fr.Length; } }
        public static double Frac(int i) { return Fr[U.Clamp(i, 0, Fr.Length - 1)]; }

        /// <summary>The preset closest to a width.</summary>
        public static int Nearest(int width, int screenW)
        {
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < Fr.Length; i++) { double d = Math.Abs(screenW * Fr[i] - width); if (d < bd) { bd = d; best = i; } }
            return best;
        }

        static DateTime last = DateTime.MinValue;
        /// <summary>Holding a key or button steps through the sizes at a readable pace instead of racing to the end.</summary>
        public static bool Throttle()
        {
            var now = DateTime.UtcNow;
            if ((now - last).TotalMilliseconds < 200) return false;
            last = now;
            return true;
        }

        /// <summary>The next preset bigger (dir 1) or smaller (dir -1) than the current width, or -1 at the end.
        /// sizeOf gives the real width a preset ends up at (size limits can cap it), so a capped step is skipped.</summary>
        public static int Next(int curW, int screenW, int dir, Func<int, int> sizeOf)
        {
            if (dir > 0)
            {
                for (int i = 0; i < Fr.Length; i++)
                    if (screenW * Fr[i] > curW * 1.03 && sizeOf(i) > curW + 2) return i;
            }
            else
            {
                for (int i = Fr.Length - 1; i >= 0; i--)
                    if (screenW * Fr[i] < curW * 0.97 && sizeOf(i) < curW - 2) return i;
            }
            return -1;
        }
    }

    static class Menus
    {
        static readonly Padding ItemPad = new Padding(4, 5, 4, 5);

        public static ToolStripMenuItem Item(string text, Action a)
        {
            var it = new ToolStripMenuItem(text);
            it.Padding = ItemPad;
            if (a != null) it.Click += (o, e) => a();
            return it;
        }

        public static ToolStripMenuItem Item(string text, Action a, string hotkeyField)
        {
            var it = Item(text, a);
            string hk = HK.Display(hotkeyField);
            if (hk.Length > 0) it.ShortcutKeyDisplayString = hk;
            return it;
        }

        public static ToolStripMenuItem Check(string text, bool on, Action a)
        {
            var it = Item(text, a);
            it.Checked = on;
            return it;
        }

        public static ToolStripMenuItem Check(string text, bool on, Action a, string hotkeyField)
        {
            var it = Item(text, a, hotkeyField);
            it.Checked = on;
            return it;
        }

        public static ToolStripMenuItem Header(string text)
        {
            var it = new ToolStripMenuItem(text.Replace("&", "&&"));
            it.Padding = ItemPad;
            it.Enabled = false;
            return it;
        }

        public static void Clear(ToolStripItemCollection items)
        {
            var old = items.Cast<ToolStripItem>().ToList();
            items.Clear();
            foreach (var o in old) o.Dispose();
        }

        /// <summary>Windows 11 look for a menu and all its submenus: rounded corners, system font, roomy padding.</summary>
        public static void Style(ToolStripDropDown dd)
        {
            dd.Font = SystemFonts.MenuFont;
            L.Menu(dd.Items);
            dd.Padding = new Padding(2, 4, 2, 4);
            dd.Opened -= RoundOnOpen;
            dd.Opened += RoundOnOpen;
            foreach (ToolStripItem item in dd.Items)
            {
                var mi = item as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems) Style(mi.DropDown);
            }
        }

        static void RoundOnOpen(object sender, EventArgs e)
        {
            var dd = sender as ToolStripDropDown;
            if (dd == null || !dd.IsHandleCreated) return;
            int pref = 2; // DWMWCP_ROUND — 8 px, like Windows 11's own context menus
            try { Native.DwmSetWindowAttribute(dd.Handle, 33, ref pref, 4); } catch { }
        }
    }
    /// <summary>
    /// Windows' per-app GPU preference (Settings > System > Display > Graphics), stored in
    /// HKCU\Software\Microsoft\DirectX\UserGpuPreferences. Apps read it when they start, so a change applies on their next launch.
    /// </summary>
    static class GpuPref
    {
        const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

        static HashSet<string> Added()
        {
            return new HashSet<string>((App.S.GpuPrefsAdded ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Undoes every preference PiP Studio set (leaves ones you made yourself alone).</summary>
        public static void RemoveAdded()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(KeyPath, true))
                    if (k != null)
                        foreach (var exe in Added())
                            if ((k.GetValue(exe) as string) == "GpuPreference=2;") k.DeleteValue(exe, false);
            }
            catch { }
            App.S.GpuPrefsAdded = "";
            App.S.Save();
        }
    }
}
