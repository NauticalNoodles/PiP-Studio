using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PipStudio
{
    /// <summary>
    /// Other apps' own picture-in-picture windows (Chrome, Edge, Firefox, Brave, Opera, and Electron apps such as FreeTube).
    /// When no PiP Studio PiP is open, PiP Studio's shortcuts and controller controls steer that window instead:
    /// move and snap, size, opacity, volume, mute and close.
    /// </summary>
    static class ExternalPip
    {
        static readonly HashSet<string> Titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Picture in picture", "Picture-in-Picture", "Picture-in-picture", "Picture in Picture", "Bild im Bild", "Image dans l'image",
            "Imagen en imagen", "Picture-in-picture video", "Picture in picture video"
        };

        static readonly Dictionary<IntPtr, byte> alpha = new Dictionary<IntPtr, byte>();

        /// <summary>The app PiP picked with next / previous (otherwise the one in front is used).</summary>
        public static IntPtr Preferred;
        static IntPtr announced;
        static ExtSplash splash;

        public static bool IsPipTitle(string t) { return t != null && Titles.Contains(t.Trim()); }

        static readonly HashSet<string> DiscordNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "discord", "discordptb", "discordcanary", "vesktop", "legcord" };

        /// <summary>Another app's own PiP: a browser PiP window (by title), or a Discord pop-out (a call or stream popped out of the app).</summary>
        public static bool IsAppPip(IntPtr h)
        {
            if (U.IsOurs(h)) return false;
            string key = AppKey(h);
            if (sessionNot.Contains(h) || NotSet().Contains(key)) return false;   // you said it isn't one
            if (sessionPip.Contains(h) || PipSet().Contains(key)) return true;      // you said it is
            string t = U.WindowTitle(h);
            if (IsPipTitle(t)) return true;
            if (IsDiscordPopout(h, t)) return true;
            return App.S.DetectOtherPips && LooksLikePip(h);
        }

        // ------------------------------------------------------------ finding PiPs of apps PiP Studio doesn't know by name
        /// <summary>A window that behaves like a PiP: always on top, small, picture-shaped, a normal top-level window you can click
        /// (not a click-through overlay, a tooltip or part of Windows). Video players' mini modes, Chrome's document PiPs
        /// (Spotify, YouTube Music, Meet), Telegram and Zoom floating videos and the like.</summary>
        static bool LooksLikePip(IntPtr h)
        {
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TOPMOST) == 0) return false;
            if ((ex & (Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE)) != 0) return false;     // overlays, notifications
            if ((Native.GetWindowLong(h, Native.GWL_STYLE) & Native.WS_CHILD) != 0) return false;
            if (Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return false;                  // dialogs and pop-ups
            if (U.CloakState(h) != 0) return false;
            if (NotPipClasses.Contains(U.ClassName(h))) return false;
            if (NotPipProcesses.Contains(U.ProcessName(h))) return false;
            var r = U.WindowRect(h);
            if (r.Width < 160 || r.Height < 90) return false;
            var mon = Screen.FromHandle(h).Bounds;
            if ((double)r.Width * r.Height > 0.45 * mon.Width * mon.Height) return false;        // a big always-on-top window isn't a PiP
            double a = r.Width / (double)r.Height;
            return a >= 0.45 && a <= 2.8;
        }

        static readonly HashSet<string> NotPipClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "tooltips_class32", "#32768", "#32770",
            "NotifyIconOverflowWindow", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
            "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd", "Xaml_WindowedPopupClass", "SysShadow", "IME", "MSCTFIME UI"
        };

        static readonly HashSet<string> NotPipProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost", "LockApp",
            "GameBar", "GameBarFTServer", "XboxGameBarWidgets", "NVIDIA Overlay", "nvcontainer", "RadeonSoftware", "AMDRSServ",
            "MSIAfterburner", "RTSS", "EncoderServer", "ScreenClippingHost", "SnippingTool", "PowerToys", "PowerToys.AlwaysOnTop",
            "Taskmgr", "SystemSettings", "ApplicationFrameHost"
        };

        // ------------------------------------------------------------ what you told PiP Studio
        /// <summary>An app's window kind: process and window class, e.g. "vlc|Qt5QWindowIcon".</summary>
        public static string AppKey(IntPtr h) { return (U.ProcessName(h) ?? "").ToLowerInvariant() + "|" + U.ClassName(h); }

        /// <summary>Browsers and Electron apps use one window class for everything, so a mark there applies to that window only
        /// (until it closes) instead of every window of the app.</summary>
        static bool SharedClass(IntPtr h)
        {
            string c = U.ClassName(h);
            return c == "Chrome_WidgetWin_1" || c == "MozillaWindowClass" || c == "ApplicationFrameWindow";
        }

        static readonly HashSet<IntPtr> sessionPip = new HashSet<IntPtr>(), sessionNot = new HashSet<IntPtr>();
        static string pipText, notText;
        static HashSet<string> pipSet = new HashSet<string>(), notSet = new HashSet<string>();

        static HashSet<string> Parse(string text)
        {
            return new HashSet<string>((text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The kinds you marked as PiPs / not PiPs (parsed once per change: these are checked for every window).</summary>
        static HashSet<string> PipSet() { if (App.S.PipApps != pipText) { pipText = App.S.PipApps; pipSet = Parse(pipText); } return pipSet; }
        static HashSet<string> NotSet() { if (App.S.NotPipApps != notText) { notText = App.S.NotPipApps; notSet = Parse(notText); } return notSet; }

        static string Without(string list, string key)
        {
            var l = new List<string>((list ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            l.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            return string.Join(";", l);
        }

        static string With(string list, string key) { return Without(list, key) + (string.IsNullOrEmpty(Without(list, key)) ? "" : ";") + key; }

        /// <summary>"Treat as a PiP" on the Windows page.</summary>
        public static void MarkPip(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            string key = AppKey(h);
            sessionNot.Remove(h);
            App.S.NotPipApps = Without(App.S.NotPipApps, key);
            if (SharedClass(h)) sessionPip.Add(h); else App.S.PipApps = With(App.S.PipApps, key);
            App.I.SettingsChanged();
        }

        /// <summary>"Not a PiP" on the Running PiPs page: undoes your own mark, or keeps PiP Studio from treating it as one.</summary>
        public static void MarkNotPip(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            string key = AppKey(h);
            bool wasMarked = sessionPip.Remove(h) | PipSet().Contains(key);
            App.S.PipApps = Without(App.S.PipApps, key);
            if (!wasMarked || IsAppPip(h))                                    // still detected on its own: remember that it isn't one
            {
                if (SharedClass(h)) sessionNot.Add(h); else App.S.NotPipApps = With(App.S.NotPipApps, key);
            }
            pinned.Remove(h);
            App.I.SettingsChanged();
        }

        public static void ClearMarks()
        {
            App.S.PipApps = ""; App.S.NotPipApps = "";
            sessionPip.Clear(); sessionNot.Clear();
            App.I.SettingsChanged();
        }

        /// <summary>
        /// Discord's popped-out call/stream window. Its main window's title always ends in "Discord" ("Friends - Discord",
        /// "#general | Server - Discord"); the pop-out is a second window of the same app with any other (or no) title.
        /// </summary>
        static bool IsDiscordPopout(IntPtr h, string title)
        {
            if (U.ClassName(h) != "Chrome_WidgetWin_1") return false;
            if (!DiscordNames.Contains(U.ProcessName(h))) return false;
            if ((title ?? "").Trim().EndsWith("Discord", StringComparison.OrdinalIgnoreCase)) return false;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_NOACTIVATE) != 0) return false;        // Discord's overlay and notification helpers
            var r = U.WindowRect(h);
            return r.Width >= 200 && r.Height >= 120;
        }

        /// <summary>A short note under the window: this is another app's own PiP (shown once each time control moves to it).</summary>
        public static void Announce(IntPtr h)
        {
            announced = h;
            if (h == IntPtr.Zero) return;
            if (splash == null || splash.IsDisposed) splash = new ExtSplash();
            splash.ShowFor(h, AppName(h) + "'s own PiP", -1);
        }

        /// <summary>A message under the window, with a level bar when level is 0..1 (volume, opacity), like PiP Studio's own PiPs.</summary>
        public static void Note(IntPtr h, string text, double level = -1)
        {
            if (h == IntPtr.Zero) return;
            if (splash == null || splash.IsDisposed) splash = new ExtSplash();
            splash.ShowFor(h, text, level);
        }

        public static string AppName(IntPtr h)
        {
            string p = U.ProcessName(h);
            if (string.IsNullOrEmpty(p)) return "This app";
            switch (p.ToLowerInvariant())
            {
                case "chrome": return "Chrome";
                case "msedge": return "Edge";
                case "firefox": return "Firefox";
                case "brave": return "Brave";
                case "opera": return "Opera";
                case "vivaldi": return "Vivaldi";
                case "freetube": return "FreeTube";
                case "discord": return "Discord";
            }
            return char.ToUpper(p[0]) + p.Substring(1);
        }
        static readonly Dictionary<IntPtr, Rectangle> theaterRestore = new Dictionary<IntPtr, Rectangle>();

        /// <summary>The topmost visible PiP window from another app, or zero.</summary>
        public static IntPtr Find()
        {
            if (Preferred != IntPtr.Zero && Native.IsWindow(Preferred) && Native.IsWindowVisible(Preferred) && IsAppPip(Preferred)) return Preferred;
            Preferred = IntPtr.Zero;
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((h, l) =>
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h) || U.IsOurs(h)) return true;
                if (!IsAppPip(h)) return true;
                found = h;
                return false;                                            // EnumWindows goes top to bottom: the first one is in front
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Every visible PiP window from other apps, front to back.</summary>
        public static List<IntPtr> FindAll()
        {
            var l = new List<IntPtr>();
            Native.EnumWindows((h, x) =>
            {
                if (Native.IsWindowVisible(h) && !Native.IsIconic(h) && !U.IsOurs(h) && IsAppPip(h)) l.Add(h);
                return true;
            }, IntPtr.Zero);
            return l;
        }

        /// <summary>Opacity PiP Studio set on it, 0..1.</summary>
        public static double OpacityOf(IntPtr h) { byte a; return alpha.TryGetValue(h, out a) ? a / 255.0 : 1; }

        /// <summary>Where it sits, in the same words as PiP Studio's own PiPs.</summary>
        public static string SpotName(IntPtr h)
        {
            int cx, cy; Cell(h, out cx, out cy);
            string[,] n = { { "Top left", "Left", "Bottom left" }, { "Top", "Middle", "Bottom" }, { "Top right", "Right", "Bottom right" } };
            return n[cx, cy];
        }

        /// <summary>Runs a PiP Studio action on another app's PiP. False when it doesn't apply (no such window, or an action it can't do).</summary>
        public static bool Handle(string key) { return Handle(key, Find()); }

        public static bool Handle(string key, IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h)) return false;
            if (key == "nextsrc" || key == "prevsrc") { App.I.CycleAll(key == "nextsrc" ? 1 : -1, null, h); return true; }
            Pin(h);                                                       // steering it: bring it to the front and keep it there
            if (h != announced) Announce(h);                              // first time it's steered: say whose PiP it is
            switch (key)
            {
                case "up": Move(h, 0, -1); return true;
                case "down": Move(h, 0, 1); return true;
                case "left": Move(h, -1, 0); return true;
                case "right": Move(h, 1, 0); return true;
                case "corner": NextCorner(h); return true;
                case "nextmon": NextMonitor(h); return true;
                case "grow": StepSize(h, 1); return true;
                case "shrink": StepSize(h, -1); return true;
                case "theater": Theater(h); return true;
                case "opup": Opacity(h, 26); return true;
                case "opdown": Opacity(h, -26); return true;
                case "volup": Volume(h, 0.05f); return true;
                case "voldown": Volume(h, -0.05f); return true;
                case "mute":
                    {
                        string p = U.ProcessName(h);
                        bool now = !(Audio.GetMute(p) ?? false);
                        Audio.SetMute(p, now);
                        Note(h, now ? "Muted" : "Sound on");
                        return true;
                    }
                case "close": Native.PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); return true;   // WM_CLOSE: the app closes its PiP
                case "swap": ShowApp(h); return true;
            }
            // shortcuts that don't depend on a PiP keep doing their usual job
            if (Global.Contains(key)) return false;
            // everything else is a PiP Studio feature this window doesn't have: say so instead of "No PiP is open"
            string what;
            if (!Unsupported.TryGetValue(key, out what))
            {
                var a = App.I.Actions.FirstOrDefault(x => x.Key == key);
                what = a != null ? a.Brief : "That";
            }
            Note(h, what + " isn't available for " + AppName(h) + "'s own PiP");
            return true;
        }

        static readonly HashSet<string> Global = new HashSet<string> { "showapp", "newpip", "pipactive", "region", "pad", "padoff", "startpip", "playpause" };

        static readonly Dictionary<string, string> Unsupported = new Dictionary<string, string>
        {
            { "clickthrough", "Ghost mode" }, { "toggleall", "Hiding" }, { "follow", "Follow" }, { "dodge", "Dodge" },
            { "crop", "Cropping" }, { "pipactive", "That" }
        };

        static double Dpi(IntPtr h) { try { uint d = Native.GetDpiForWindow(h); return d > 0 ? d / 96.0 : 1; } catch { return 1; } }
        static Rectangle Bounds(IntPtr h) { return U.WindowRect(h); }
        static Rectangle Work(IntPtr h) { return Screen.FromHandle(h).WorkingArea; }
        static int Margin(IntPtr h) { return (int)Math.Round(App.S.EdgeMargin * Dpi(h)); }   // same gap from the edge as PiP Studio's PiPs

        // ------------------------------------------------------------ snapping when they appear or after you drag them
        static readonly Timer watch = new Timer { Interval = 150 };
        static readonly Dictionary<IntPtr, Rectangle> lastRect = new Dictionary<IntPtr, Rectangle>();
        static readonly HashSet<IntPtr> moved = new HashSet<IntPtr>();
        // PiPs whose app moved them on its own (no mouse drag, not PiP Studio). The app is managing that window itself,
        // e.g. Discord's overlays, so PiP Studio stops snapping it instead of fighting the app over the position.
        static readonly HashSet<IntPtr> appControlled = new HashSet<IntPtr>();

        public static void StartWatching()
        {
            watch.Tick += (o, e) => { try { WatchTick(); } catch { } };
            watch.Start();
            hoverTimer.Tick += (o, e) => { try { HoverTick(); } catch (Exception ex) { App.ReportError(ex); } };
            hoverTimer.Start();
        }

        /// <summary>True for another app's PiP that PiP Studio is currently keeping track of.</summary>
        public static bool IsKnown(IntPtr h) { return h != IntPtr.Zero && lastRect.ContainsKey(h); }

        // ------------------------------------------------------------ hover controls (ExtHover)
        static readonly Timer hoverTimer = new Timer { Interval = 40 };
        static ExtHover hover;

        /// <summary>Point at another app's PiP and PiP Studio's title bar appears over it (see ExtHover).</summary>
        static void HoverTick()
        {
            bool on = App.S.ControlOtherPips && App.S.HoverOtherPips && !App.I.PadActive;   // controller controls have their own overlay
            if (!on || lastRect.Count == 0)
            {
                if (hover != null && !hover.IsDisposed) hover.Tick(IntPtr.Zero);
                return;
            }
            if (hover == null || hover.IsDisposed) hover = new ExtHover();
            hover.Tick(hover.TargetUnderCursor());
        }

        /// <summary>Runs a title bar action on another app's PiP (the same ones its shortcuts do).</summary>
        public static void HoverAction(IntPtr h, string key) { Handle(key, h); }

        /// <summary>Dragging another app's PiP by PiP Studio's title bar. Letting go snaps it like a normal drag (WatchTick).</summary>
        public static void DragTo(IntPtr h, int x, int y)
        {
            if (!Native.IsWindow(h)) return;
            Native.SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        // ------------------------------------------------------------ keeping steered PiPs on top
        // Some apps' PiPs (Discord's pop-out) aren't always-on-top, so the game covers them as soon as it gets focus back,
        // e.g. when controller controls end. Once you've steered one, PiP Studio keeps it on top until it closes.
        static readonly HashSet<IntPtr> pinned = new HashSet<IntPtr>();

        public static void Pin(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h)) return;
            pinned.Add(h);
            KeepOnTop(h);
        }

        static void KeepOnTop(IntPtr h)
        {
            if ((Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0) return;
            Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        static void PinTick()
        {
            if (pinned.Count == 0) return;
            foreach (var h in new List<IntPtr>(pinned))
            {
                if (!Native.IsWindow(h)) { pinned.Remove(h); continue; }
                if (Native.IsWindowVisible(h) && !Native.IsIconic(h)) KeepOnTop(h);   // the app (or a game taking focus) dropped it: put it back
            }
        }

        static void WatchTick()
        {
            if (App.S.ControlOtherPips) PinTick();
            if (!App.S.ControlOtherPips) { lastRect.Clear(); moved.Clear(); appControlled.Clear(); return; }
            bool mouseDown = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
            var now = FindAll();
            foreach (var gone in new List<IntPtr>(lastRect.Keys)) if (!now.Contains(gone)) { lastRect.Remove(gone); moved.Remove(gone); appControlled.Remove(gone); freeAspect.Remove(gone); trimToken.Remove(gone); minWidth.Remove(gone); trimChoice.Remove(gone); aspectChoice.Remove(gone); snapChoice.Remove(gone); }
            foreach (var h in now)
            {
                if (theaterRestore.ContainsKey(h)) continue;              // theater: leave it centered
                var r = Bounds(h);
                Rectangle prev;
                if (!lastRect.TryGetValue(h, out prev))
                {
                    lastRect[h] = r;
                    if (!mouseDown && App.S.SnapOtherPips && IsFreeShape(h))
                    {
                        // Discord pop-outs have no shape of their own: start at 16:9, then trim off any black bars
                        var fs = Fit(h, r.Width, 16.0 / 9);
                        ResizeTo(h, fs.Width, fs.Height);
                        TrimBarsSoon(h, 4);
                    }
                    if (!mouseDown && SnapsHere(h)) SnapNearest(h);       // just appeared: put it on a spot, once
                    continue;
                }
                if (prev == r)
                {
                    if (moved.Contains(h) && !mouseDown)
                    {
                        moved.Remove(h);
                        if (!appControlled.Contains(h) && SnapsHere(h)) SnapNearest(h);   // you let go: glide to the nearest spot
                    }
                    continue;
                }
                lastRect[h] = r;
                if (mouseDown) moved.Add(h);                              // you're dragging or resizing it
                else if (!moved.Contains(h)) appControlled.Add(h);        // the app moved it by itself: leave it alone from now on
            }
        }

        /// <summary>Puts it on the nearest of PiP Studio's spots: the 4 corners, plus the middle of each edge when 8 spots are on.</summary>
        static void SnapNearest(IntPtr h)
        {
            var b = Bounds(h);
            var wa = Work(h);
            int m = Margin(h);
            var cx = b.Left + b.Width / 2.0; var cy = b.Top + b.Height / 2.0;
            double best = double.MaxValue; int bx = 0, by = 0;
            for (int gx = 0; gx < 3; gx++)
                for (int gy = 0; gy < 3; gy++)
                {
                    if (gx == 1 && gy == 1) continue;
                    if (!App.S.SnapToEdges && (gx == 1 || gy == 1)) continue;
                    int x = gx == 0 ? wa.Left + m : gx == 2 ? wa.Right - m - b.Width : wa.Left + (wa.Width - b.Width) / 2;
                    int y = gy == 0 ? wa.Top + m : gy == 2 ? wa.Bottom - m - b.Height : wa.Top + (wa.Height - b.Height) / 2;
                    double d = Math.Pow(x + b.Width / 2.0 - cx, 2) + Math.Pow(y + b.Height / 2.0 - cy, 2);
                    if (d < best) { best = d; bx = x; by = y; }
                }
            if (bx != b.Left || by != b.Top) Place(h, new Rectangle(bx, by, b.Width, b.Height));
        }

        static void Place(IntPtr h, Rectangle r)
        {
            Native.SetWindowPos(h, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            if (lastRect.ContainsKey(h)) lastRect[h] = Bounds(h);        // our own moves don't count as the app moving it
        }

        /// <summary>Column/row (0..2) of the 3×3 grid of snap spots nearest to the window.</summary>
        static void Cell(IntPtr h, out int cx, out int cy)
        {
            var b = Bounds(h); var wa = Work(h);
            double fx = (b.Left + b.Width / 2.0 - wa.Left) / wa.Width, fy = (b.Top + b.Height / 2.0 - wa.Top) / wa.Height;
            cx = fx < 0.34 ? 0 : fx > 0.66 ? 2 : 1;
            cy = fy < 0.34 ? 0 : fy > 0.66 ? 2 : 1;
        }

        static void SnapTo(IntPtr h, int cx, int cy, Rectangle wa)
        {
            var b = Bounds(h);
            int m = Margin(h);
            int x = cx == 0 ? wa.Left + m : cx == 2 ? wa.Right - m - b.Width : wa.Left + (wa.Width - b.Width) / 2;
            int y = cy == 0 ? wa.Top + m : cy == 2 ? wa.Bottom - m - b.Height : wa.Top + (wa.Height - b.Height) / 2;
            Place(h, new Rectangle(x, y, b.Width, b.Height));
        }

        static void Move(IntPtr h, int dx, int dy)
        {
            int cx, cy;
            Cell(h, out cx, out cy);
            int nx = Math.Max(0, Math.Min(2, cx + dx)), ny = Math.Max(0, Math.Min(2, cy + dy));
            if (!App.S.SnapToEdges)
            {
                // corners only: skip the middle of each edge
                if (nx == 1) nx = dx > 0 ? 2 : dx < 0 ? 0 : (cx == 1 ? 2 : cx);
                if (ny == 1) ny = dy > 0 ? 2 : dy < 0 ? 0 : (cy == 1 ? 2 : cy);
            }
            if (nx == 1 && ny == 1) { nx += dx; ny += dy; }               // never park it in the middle of the screen
            nx = Math.Max(0, Math.Min(2, nx)); ny = Math.Max(0, Math.Min(2, ny));
            SnapTo(h, nx, ny, Work(h));
        }

        static void NextCorner(IntPtr h)
        {
            int cx, cy;
            Cell(h, out cx, out cy);
            // clockwise: top-left, top-right, bottom-right, bottom-left
            int[][] order = { new[] { 0, 0 }, new[] { 2, 0 }, new[] { 2, 2 }, new[] { 0, 2 } };
            int i = 0;
            for (int k = 0; k < 4; k++) if (order[k][0] == (cx == 1 ? 0 : cx) && order[k][1] == (cy == 1 ? 0 : cy)) i = k;
            var n = order[(i + 1) % 4];
            SnapTo(h, n[0], n[1], Work(h));
        }

        static void NextMonitor(IntPtr h)
        {
            var screens = Screen.AllScreens;
            if (screens.Length < 2) return;
            var cur = Screen.FromHandle(h);
            int i = Array.IndexOf(screens, cur);
            var next = screens[(i + 1) % screens.Length].WorkingArea;
            int cx, cy;
            Cell(h, out cx, out cy);
            var b = Bounds(h);
            Place(h, new Rectangle(next.Left, next.Top, Math.Min(b.Width, next.Width), Math.Min(b.Height, next.Height)));
            SnapTo(h, cx, cy, next);
        }

        // ------------------------------------------------------------ sizes: the same 7 presets as PiP Studio's PiPs
        /// <summary>Steps to the next size preset by width. Browser PiPs keep their shape (the browser fits it to the video);
        /// Discord pop-outs have no fixed shape, so they go 16:9 and are then trimmed until the black bars around the video are gone.</summary>
        static void StepSize(IntPtr h, int dir)
        {
            if (!SizePresets.Throttle()) return;
            var b = Bounds(h); var wa = Work(h);
            bool free = IsFreeShape(h) && AspectOf(h) == 0;
            bool remembered = free && freeAspect.ContainsKey(h);
            // your locked shape; for free-shape windows the bar-free shape once found (16:9 until then); else its own shape
            double aspect = ShapeFor(h);
            int floor; minWidth.TryGetValue(h, out floor);
            int i = SizePresets.Next(b.Width, wa.Width, dir, k => Math.Max(floor, Fit(h, (int)(wa.Width * SizePresets.Frac(k)), aspect).Width));
            if (i < 0) { Note(h, dir > 0 ? "Largest size" : "Smallest size"); return; }
            var sz = Fit(h, (int)(wa.Width * SizePresets.Frac(i)), aspect);
            ResizeTo(h, sz.Width, sz.Height);
            Note(h, SizePresets.Names[i]);
            if (free && !remembered) TrimBarsSoon(h, 4);   // no bar-free shape yet: find it
        }

        /// <summary>Width and height for a width and shape, within the size limits and the screen.</summary>
        static Size Fit(IntPtr h, int w, double aspect)
        {
            var wa = Work(h);
            int minW = (int)Math.Round(200 * Dpi(h));
            int maxW = (int)(wa.Width * (App.S.LimitWidth ? App.S.MaxWidthPercent / 100.0 : 0.9));
            int maxH = (int)(wa.Height * (App.S.LimitHeight ? App.S.MaxHeightPercent / 100.0 : 0.9));
            w = Math.Max(minW, Math.Min(maxW, w));
            int hgt = (int)Math.Round(w / aspect);
            if (hgt > maxH) { hgt = maxH; w = (int)Math.Round(hgt * aspect); }
            return new Size(w, hgt);
        }

        static void ResizeTo(IntPtr h, int w, int hgt)
        {
            var b = Bounds(h); var wa = Work(h);
            // grow away from the nearest screen edges, like PiP Studio's own PiPs
            int cx, cy;
            Cell(h, out cx, out cy);
            int x = cx == 2 ? b.Right - w : cx == 1 ? b.Left + (b.Width - w) / 2 : b.Left;
            int y = cy == 2 ? b.Bottom - hgt : cy == 1 ? b.Top + (b.Height - hgt) / 2 : b.Top;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - w));
            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - hgt));
            Place(h, new Rectangle(x, y, w, hgt));
            // apps can refuse to get smaller than their own minimum (Discord does): re-anchor with the size it really took,
            // so it doesn't hang off the screen, and remember that minimum so Smaller stops there
            var got = Bounds(h);
            if (got.Width > w + 2 || got.Height > hgt + 2)
            {
                if (got.Width > w + 2) minWidth[h] = got.Width;
                int gx = cx == 2 ? b.Right - got.Width : cx == 1 ? b.Left + (b.Width - got.Width) / 2 : b.Left;
                int gy = cy == 2 ? b.Bottom - got.Height : cy == 1 ? b.Top + (b.Height - got.Height) / 2 : b.Top;
                gx = Math.Max(wa.Left, Math.Min(gx, wa.Right - got.Width));
                gy = Math.Max(wa.Top, Math.Min(gy, wa.Bottom - got.Height));
                Place(h, new Rectangle(gx, gy, got.Width, got.Height));
            }
        }

        static readonly Dictionary<IntPtr, int> minWidth = new Dictionary<IntPtr, int>();

        /// <summary>PiP windows whose shape doesn't follow the video (Discord's pop-out): the app letterboxes the video inside.</summary>
        static bool IsFreeShape(IntPtr h)
        {
            bool on;
            if (trimChoice.TryGetValue(h, out on)) return on;                     // you chose for this window
            return DiscordNames.Contains(U.ProcessName(h));
        }

        // ------------------------------------------------------------ per-window options (Running PiPs > options), until it closes
        static readonly Dictionary<IntPtr, bool> trimChoice = new Dictionary<IntPtr, bool>();
        static readonly Dictionary<IntPtr, int> aspectChoice = new Dictionary<IntPtr, int>(), snapChoice = new Dictionary<IntPtr, int>();

        public static bool TrimsBars(IntPtr h) { return IsFreeShape(h); }
        public static int AspectOf(IntPtr h) { int a; return aspectChoice.TryGetValue(h, out a) ? a : 0; }
        public static int SnapModeOf(IntPtr h) { int m; return snapChoice.TryGetValue(h, out m) ? m : 0; }
        static bool SnapsHere(IntPtr h) { int m = SnapModeOf(h); return m == 0 ? App.S.SnapOtherPips : m == 1; }

        public static int OpacityPercent(IntPtr h) { return (int)Math.Round(OpacityOf(h) * 100); }

        public static void SetOpacityPercent(IntPtr h, int pct)
        {
            byte a;
            if (!alpha.TryGetValue(h, out a)) a = 255;
            int target = (int)Math.Round(U.Clamp(pct, 20, 100) * 2.55);
            if (target != a) Opacity(h, target - a);
        }

        public static float? VolumeOf(IntPtr h) { return Audio.GetVolume(U.ProcessName(h)); }

        public static void SetVolumeLevel(IntPtr h, float v)
        {
            string p = U.ProcessName(h);
            v = Math.Max(0, Math.Min(1, v));
            Audio.SetVolume(p, v);
            if (v > 0 && Audio.GetMute(p) == true) Audio.SetMute(p, false);
            Note(h, "Volume " + (int)Math.Round(v * 100) + "%", v);
        }

        public static int PresetIndex(IntPtr h) { return SizePresets.Nearest(Bounds(h).Width, Work(h).Width); }

        /// <summary>The shape a size step or preset uses: yours, then the bar-free one found, then 16:9 for free-shape windows, else its own.</summary>
        static double ShapeFor(IntPtr h)
        {
            double locked = AspectChoices.Value(AspectOf(h));
            if (locked > 0) return locked;
            double known;
            if (IsFreeShape(h) && freeAspect.TryGetValue(h, out known)) return known;
            if (IsFreeShape(h)) return 16.0 / 9;
            var b = Bounds(h);
            return b.Height > 0 ? b.Width / (double)b.Height : 16.0 / 9;
        }

        public static void SetSizePreset(IntPtr h, int i)
        {
            i = U.Clamp(i, 0, SizePresets.Count - 1);
            var sz = Fit(h, (int)(Work(h).Width * SizePresets.Frac(i)), ShapeFor(h));
            ResizeTo(h, sz.Width, sz.Height);
            Note(h, SizePresets.Names[i]);
            if (IsFreeShape(h) && AspectOf(h) == 0 && !freeAspect.ContainsKey(h)) TrimBarsSoon(h, 4);
        }

        public static void SetAspect(IntPtr h, int i)
        {
            if (i <= 0) aspectChoice.Remove(h); else aspectChoice[h] = i;
            var b = Bounds(h);
            var sz = Fit(h, b.Width, ShapeFor(h));
            ResizeTo(h, sz.Width, sz.Height);
            Note(h, AspectChoices.Names[U.Clamp(i, 0, AspectChoices.Names.Length - 1)]);
            if (i <= 0 && IsFreeShape(h)) { freeAspect.Remove(h); TrimBarsSoon(h, 4); }
        }

        public static void SetTrim(IntPtr h, bool on)
        {
            trimChoice[h] = on;
            freeAspect.Remove(h);
            if (on && AspectOf(h) == 0) TrimBarsSoon(h, 4);
            Note(h, on ? "Removing black bars" : "Black bars left alone");
        }

        public static void SetSnapMode(IntPtr h, int mode)
        {
            if (mode <= 0) snapChoice.Remove(h); else snapChoice[h] = mode;
            if (SnapsHere(h)) SnapNearest(h);
        }

        // ------------------------------------------------------------ black bars
        static readonly Dictionary<IntPtr, double> freeAspect = new Dictionary<IntPtr, double>();
        static readonly Dictionary<IntPtr, int> trimToken = new Dictionary<IntPtr, int>();

        /// <summary>Looks for black bars around the video a moment after a resize (the app needs a frame to redraw)
        /// and trims the window so the video fills it: up to a few passes, each one closer.</summary>
        static void TrimBarsSoon(IntPtr h, int passes, bool trimmed = false)
        {
            int tok; trimToken.TryGetValue(h, out tok); tok++; trimToken[h] = tok;
            var t = new Timer { Interval = 350 };
            t.Tick += (o, e) =>
            {
                t.Stop(); t.Dispose();
                int now; if (!trimToken.TryGetValue(h, out now) || now != tok) return;   // a newer resize took over
                if (!Native.IsWindow(h)) { trimToken.Remove(h); freeAspect.Remove(h); return; }
                int r = TrimBars(h, trimmed);
                if (r > 0 && passes > 1) TrimBarsSoon(h, passes - 1, true);
            };
            t.Start();
        }

        /// <summary>One pass: 1 if it changed the size, 0 if there are no bars, -1 if it couldn't tell.</summary>
        static int TrimBars(IntPtr h, bool trimmed)
        {
            var b = Bounds(h);
            if (b.Width < 40 || b.Height < 40) return -1;
            int top, bottom, left, right;
            if (!FindBars(h, b, out top, out bottom, out left, out right)) return -1;
            int dh = 0;
            if (top + bottom >= 4) dh = -(top + bottom);                           // letterbox: too tall for the video
            else if (left + right >= 4)                                            // pillarbox: too short for the video
            {
                int vw = b.Width - left - right;
                dh = (int)Math.Round(b.Height * (left + right) / (double)Math.Max(1, vw));
            }
            if (dh == 0)
            {
                // no bars left after trimming: remember this shape while the window is open (a first look with no bars proves less, so it's checked again next time)
                if (trimmed) freeAspect[h] = b.Width / (double)b.Height;
                return 0;
            }
            var sz = Fit(h, b.Width, b.Width / (double)Math.Max(40, b.Height + dh));
            if (sz.Height == b.Height) return -1;
            ResizeTo(h, sz.Width, sz.Height);
            return 1;                                                           // the next pass checks it; the shape is remembered once no bars are left
        }

        /// <summary>Measures the bars around the video: solid dark strips (black, or the app's dark grey background like Discord's).
        /// Bars are measured on the middle of each side, so the title strip's buttons and the rounded corners don't get in the way,
        /// and each pair counts as twice the thinner side (the title strip on top would otherwise look like part of a bar).
        /// False when there's nothing reliable to go on (a blank window, or the whole edge is picture).</summary>
        static bool FindBars(IntPtr h, Rectangle b, out int top, out int bottom, out int left, out int right)
        {
            // Discord's own picture first: its pop-out is see-through beside the video, so a screen copy would show whatever's behind it.
            // The screen is only a fallback for when the app can't draw itself (then its picture comes back blank).
            for (int pass = 0; pass < 2; pass++)
                if (Measure(h, b, pass == 1, out top, out bottom, out left, out right)) return true;
            top = bottom = left = right = 0;
            return false;
        }

        static bool Measure(IntPtr h, Rectangle b, bool fromScreen, out int top, out int bottom, out int left, out int right)
        {
            top = bottom = left = right = 0;
            using (var bmp = Grab(h, b, fromScreen))
                return bmp != null && Letterbox.Find(bmp, out top, out bottom, out left, out right);
        }

        /// <summary>A capture that came back as one flat color (the app couldn't draw itself).</summary>
        static bool Blank(Bitmap bmp)
        {
            var c0 = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
            for (int y = 2; y < bmp.Height - 2; y += Math.Max(1, bmp.Height / 24))
                for (int x = 2; x < bmp.Width - 2; x += Math.Max(1, bmp.Width / 24))
                {
                    var c = bmp.GetPixel(x, y);
                    if (Math.Abs(c.R - c0.R) > 12 || Math.Abs(c.G - c0.G) > 12 || Math.Abs(c.B - c0.B) > 12) return false;
                }
            return true;
        }

        /// <summary>The window's picture: drawn by the app itself (unaffected by what's behind it), or copied from the screen.</summary>
        static Bitmap Grab(IntPtr h, Rectangle b, bool fromScreen)
        {
            Bitmap bmp = null;
            try
            {
                bmp = new Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                if (fromScreen)
                {
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                    return bmp;
                }
                bool ok;
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try { ok = Native.PrintWindow(h, hdc, 2); }   // PW_RENDERFULLCONTENT: Chromium/Electron windows too
                    finally { g.ReleaseHdc(hdc); }
                }
                // hardware-decoded video often comes back black from PrintWindow; the PiP is on top, so the screen has the real picture
                if (!ok || Blank(bmp)) { bmp.Dispose(); return null; }
                return bmp;
            }
            catch { if (bmp != null) bmp.Dispose(); return null; }
        }

        static void Theater(IntPtr h)
        {
            Rectangle back;
            if (theaterRestore.TryGetValue(h, out back)) { theaterRestore.Remove(h); Place(h, back); return; }
            var b = Bounds(h); var wa = Work(h);
            theaterRestore[h] = b;
            double aspect = b.Height > 0 ? b.Width / (double)b.Height : 16.0 / 9;
            int w = (int)(wa.Width * 0.62), hgt = (int)Math.Round(w / aspect);
            if (hgt > wa.Height * 0.85) { hgt = (int)(wa.Height * 0.85); w = (int)Math.Round(hgt * aspect); }
            Place(h, new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - hgt) / 2, w, hgt));
        }

        /// <summary>Show: bring up the app the PiP belongs to (the browser window playing the video), not the PiP itself.</summary>
        static void ShowApp(IntPtr h)
        {
            int pid = U.Pid(h);
            string proc = U.ProcessName(h);
            IntPtr main = IntPtr.Zero, sameName = IntPtr.Zero;
            foreach (var w in WinEnum.AltTab())                       // front to back: the app's most recent window wins
            {
                if (w == h) continue;
                if (U.Pid(w) == pid) { main = w; break; }
                if (sameName == IntPtr.Zero && string.Equals(U.ProcessName(w), proc, StringComparison.OrdinalIgnoreCase)) sameName = w;
            }
            if (main == IntPtr.Zero) main = sameName;                 // multi-process apps: another window of the same app
            if (main == IntPtr.Zero) { Note(h, "Couldn't find " + AppName(h) + "'s window"); return; }
            App.I.ForgetSavedFocus();                                 // you chose this window: turning controls off won't jump back
            U.ForceForeground(main);
            Note(h, "Showing " + AppName(h));
        }

        static void Opacity(IntPtr h, int delta)
        {
            byte a;
            if (!alpha.TryGetValue(h, out a)) a = 255;
            int n = Math.Max(51, Math.Min(255, a + delta));               // never fully invisible (20% minimum)
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if (n >= 255)
            {
                if ((ex & Native.WS_EX_LAYERED) != 0) Native.SetLayeredWindowAttributes(h, 0, 255, 2);
                alpha.Remove(h);
                OpacityNote(h, 255);
                return;
            }
            if ((ex & Native.WS_EX_LAYERED) == 0) Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | Native.WS_EX_LAYERED);
            Native.SetLayeredWindowAttributes(h, 0, (byte)n, 2);            // LWA_ALPHA
            alpha[h] = (byte)n;
            OpacityNote(h, n);
        }

        static void OpacityNote(IntPtr h, int a) { Note(h, "Opacity " + (int)Math.Round(a / 2.55) + "%", a / 255.0); }


        static void Volume(IntPtr h, float delta)
        {
            string p = U.ProcessName(h);
            float v = Audio.GetVolume(p) ?? 1f;
            float nv = Math.Max(0, Math.Min(1, v + delta));
            if (delta > 0 && Audio.GetMute(p) == true) Audio.SetMute(p, false);
            Audio.SetVolume(p, nv);
            Note(h, "Volume " + (int)Math.Round(nv * 100) + "%", nv);
        }
    }

    /// <summary>
    /// Controller controls on and another app's PiP is the one being steered: an outline around it (white flash on each press),
    /// its name at the top and the button hints at the bottom, like PiP Studio's own PiPs. Click-through, drawn on top.
    /// </summary>
    class ExtOverlay : Form
    {
        static readonly Color Key = Color.FromArgb(255, 0, 255);
        IntPtr target;
        string title = "";
        DateTime pulseUntil = DateTime.MinValue, lastInput = DateTime.Now, lastTop = DateTime.MinValue;
        bool hintsShown = true, pulseShown;

        public ExtOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Key;
            TransparencyKey = Key;
            Text = "PiP controls";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            U.ExcludeFromCapture(Handle, true);
        }

        public void Input()
        {
            lastInput = DateTime.Now;
            pulseUntil = DateTime.Now.AddMilliseconds(180);
        }

        public void Sync(IntPtr h)
        {
            if (h != target)
            {
                target = h;
                lastInput = DateTime.Now;
                string app = U.ProcessName(h);
                title = ExternalPip.AppName(h) + "'s own PiP";
                Invalidate();
            }
            var r = U.WindowRect(h);
            if (Bounds != r) Bounds = r;
            bool hints = !App.S.PadHintsAutoHide || (DateTime.Now - lastInput).TotalSeconds < 4;
            bool pulse = DateTime.Now < pulseUntil;
            if (hints != hintsShown || pulse != pulseShown) { hintsShown = hints; pulseShown = pulse; Invalidate(); }
            if (!Visible) Show();
            // the app's PiP is topmost too: stay above it
            if ((DateTime.Now - lastTop).TotalMilliseconds > 500)
            {
                lastTop = DateTime.Now;
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Key);
            float s = Native.GetDpiForWindow(Handle) / 96f;
            if (s <= 0) s = 1;
            int t = Math.Max(2, (int)Math.Round(3 * s));
            using (var pen = new Pen(pulseShown ? Color.White : Theme.Accent, t) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
                g.DrawRectangle(pen, new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1));
            if (!hintsShown) return;
            // name pill at the top
            Font f = U.UIBold(12 * s);
            int pad = (int)(12 * s), h = (int)(26 * s), top = (int)(8 * s);
            int tw = LT.MeasureText(title, f, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            int w = Math.Min(ClientSize.Width - (int)(16 * s), tw + pad * 2);
            if (w > h)
            {
                var r = new Rectangle((ClientSize.Width - w) / 2, top, w, h);
                using (var path = U.RoundRect(r, h / 2f))
                using (var b = new SolidBrush(Color.FromArgb(20, 20, 24)))
                    g.FillPath(b, path);
                LT.DrawText(g, title, f, r, Color.FromArgb(240, 240, 246),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            if (App.S.PadHints) PipOverlay.DrawHintChips(g, s, ClientSize);
        }
    }

    /// <summary>
    /// PiP Studio's title bar over another app's PiP while you point at it: the app's name and the title bar buttons you picked
    /// in Settings (the ones that make sense for a window PiP Studio doesn't own), plus mute. Drag the bar to move the PiP,
    /// scroll on it to change the volume. Only the bar takes clicks; everything under it stays the app's own.
    /// PiP Studio can't see the mouse inside another app's window, so it watches the pointer's position instead.
    /// </summary>
    class ExtHover : Form
    {
        class HBtn
        {
            public Rectangle R;
            public string Glyph, Tip, Key;
            public bool On;
        }

        static readonly Color Key = Color.FromArgb(255, 1, 0, 1);
        static readonly Color BarColor = Color.FromArgb(20, 20, 24);

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
        const uint GA_ROOT = 2;

        IntPtr target;
        DateTime lastOver = DateTime.MinValue, lastTop = DateTime.MinValue;
        double alpha;
        float scale = 1;
        readonly List<HBtn> btns = new List<HBtn>();
        Rectangle barRect;
        int hot = -1, pressed = -1;
        bool dragging;
        Point dragStart;
        Rectangle dragFrom;

        public ExtHover()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Key;
            TransparencyKey = Key;          // the transparent part (where tooltips go) lets clicks through to the app
            Text = "PiP controls";
            Opacity = 0;
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
            U.ExcludeFromCapture(Handle, true);   // the controls never show up in recordings or screen sharing
        }

        /// <summary>The other app's PiP under the pointer, the one the bar is on while you're over the bar, or zero.</summary>
        public IntPtr TargetUnderCursor()
        {
            if (dragging) return target;
            var p = Cursor.Position;
            IntPtr root = Native.GetAncestor(WindowFromPoint(p), GA_ROOT);
            if (root == IntPtr.Zero) return IntPtr.Zero;
            if (IsHandleCreated && root == Handle) return target;
            return ExternalPip.IsKnown(root) ? root : IntPtr.Zero;
        }

        /// <summary>Called every 40 ms with the PiP the pointer is over (zero for none): fades in or out and follows the PiP.</summary>
        public void Tick(IntPtr over)
        {
            if (over != IntPtr.Zero && Native.IsWindow(over) && Native.IsWindowVisible(over) && !Native.IsIconic(over))
            {
                if (over != target) { target = over; hot = pressed = -1; Invalidate(); }
                lastOver = DateTime.Now;
            }
            bool want = target != IntPtr.Zero && Native.IsWindow(target) && (DateTime.Now - lastOver).TotalMilliseconds < 350;   // a moment's grace to reach the bar
            alpha = U.Approach(alpha, want ? 0.96 : 0, U.Animations ? 0.25 : 1);
            if (alpha < 0.02)
            {
                if (Visible) Hide();
                if (!want) { target = IntPtr.Zero; dragging = false; }
                return;
            }
            Place();
            if (!Visible) Show();
            if (Math.Abs(Opacity - alpha) > 0.01) Opacity = alpha;
            // the app's PiP is topmost too, and steering it puts it back on top: stay above it
            if ((DateTime.Now - lastTop).TotalMilliseconds > 400)
            {
                lastTop = DateTime.Now;
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        /// <summary>Over the top of the PiP: the bar, and room under it for a tooltip (see-through and click-through).</summary>
        void Place()
        {
            var r = U.WindowRect(target);
            uint d = Native.GetDpiForWindow(target);
            float s = d > 0 ? d / 96f : 1;
            if (s != scale) { scale = s; Invalidate(); }
            int h = Math.Min(r.Height, (int)(64 * s));
            var b = new Rectangle(r.X, r.Y, r.Width, h);
            if (Bounds != b) { Bounds = b; Invalidate(); }
        }

        /// <summary>The buttons you picked for the title bar, minus ones a window PiP Studio doesn't own can't do.</summary>
        void BuildLayout()
        {
            btns.Clear();
            float s = scale;
            int W = ClientSize.Width, bh = (int)(32 * s), bw = (int)(30 * s);
            barRect = new Rectangle(0, 0, W, bh);
            if (target == IntPtr.Zero) return;

            var defs = new List<HBtn>();
            var on = TitleBarItems.On(App.S.TitleBarButtons);
            foreach (var k in on)
            {
                HBtn b = Make(k);
                if (b != null) defs.Add(b);
            }
            bool? muted = Audio.GetMute(U.ProcessName(target));
            defs.Insert(0, new HBtn { Key = "mute", Glyph = muted == true ? "" : "", Tip = "Mute — scroll to change volume", On = muted == true });

            // a narrow PiP drops buttons from the left; Close stays
            int maxBtns = Math.Max(1, (W - (int)(60 * s)) / bw);
            while (defs.Count > maxBtns)
            {
                int i = defs.FindIndex(b => b.Key != "close");
                if (i < 0) break;
                defs.RemoveAt(i);
            }
            int x = W - (int)(3 * s) - defs.Count * bw;
            foreach (var b in defs)
            {
                b.R = new Rectangle(x, (bh - bw) / 2 + 1, bw, bw - 2);
                x += bw;
                btns.Add(b);
            }
        }

        HBtn Make(string key)
        {
            string app = ExternalPip.AppName(target);
            switch (key)
            {
                case "prev": return new HBtn { Key = "prevsrc", Glyph = TitleBarItems.Glyph(key), Tip = "Previous window" };
                case "next": return new HBtn { Key = "nextsrc", Glyph = TitleBarItems.Glyph(key), Tip = "Next window" };
                case "theater": return new HBtn { Key = "theater", Glyph = "", Tip = "Theater mode" };
                case "corner": return new HBtn { Key = "corner", Glyph = TitleBarItems.Glyph(key), Tip = "Next corner" };
                case "display": return Screen.AllScreens.Length < 2 ? null : new HBtn { Key = "nextmon", Glyph = TitleBarItems.Glyph(key), Tip = "Move to next display" };
                case "source": return new HBtn { Key = "swap", Glyph = TitleBarItems.Glyph(key), Tip = "Show " + app };
                case "more": return new HBtn { Key = "more", Glyph = TitleBarItems.Glyph(key), Tip = "Options in PiP Studio" };
                case "close": return new HBtn { Key = "close", Glyph = TitleBarItems.Glyph(key), Tip = "Close" };
                default: return null;   // change source, crop and ghost mode only work on PiP Studio's own PiPs
            }
        }

        int IndexAt(Point p)
        {
            for (int i = 0; i < btns.Count; i++) if (btns[i].R.Contains(p)) return i;
            return -1;
        }

        void Run(HBtn b)
        {
            IntPtr h = target;
            if (b.Key == "more") { App.I.ShowMain("running"); return; }
            ExternalPip.HoverAction(h, b.Key);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Key);
            BuildLayout();
            if (target == IntPtr.Zero) return;
            float s = scale;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(BarColor)) g.FillRectangle(b, barRect);

            int left = btns.Count > 0 ? btns[0].R.Left : ClientSize.Width;
            var titleRect = new Rectangle((int)(10 * s), 0, Math.Max(0, left - (int)(14 * s)), barRect.Height);
            if (titleRect.Width > 20)
                LT.DrawText(g, ExternalPip.AppName(target), U.UI(12 * s), titleRect, Color.FromArgb(235, 235, 242),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            for (int i = 0; i < btns.Count; i++)
            {
                var bt = btns[i];
                if (i == hot)
                {
                    Color hc = bt.Key == "close" ? Color.FromArgb(196, 43, 28) : Color.FromArgb(58, 60, 70);
                    using (var path = U.RoundRect(Rectangle.Inflate(bt.R, -2, -2), 5 * s))
                    using (var b = new SolidBrush(hc))
                        g.FillPath(b, path);
                }
                LT.DrawText(g, bt.Glyph, U.Icons(13 * s), bt.R, bt.On ? Theme.Accent : Color.FromArgb(238, 238, 244),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            if (hot >= 0 && hot < btns.Count && !dragging)
            {
                var bt = btns[hot];
                Font tf = U.UI(11 * s);
                Size ts = LT.MeasureText(bt.Tip, tf);
                int tw = ts.Width + (int)(14 * s), th = ts.Height + (int)(6 * s);
                int tx = U.Clamp(bt.R.X + bt.R.Width / 2 - tw / 2, 4, Math.Max(4, ClientSize.Width - tw - 4));
                var tr = new Rectangle(tx, barRect.Bottom + (int)(4 * s), tw, th);
                if (tr.Bottom <= ClientSize.Height)
                    using (var path = U.RoundRect(tr, 4 * s))
                    using (var b = new SolidBrush(BarColor))
                    {
                        g.FillPath(b, path);
                        LT.DrawText(g, bt.Tip, tf, tr, Color.FromArgb(225, 225, 232),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            lastOver = DateTime.Now;
            if (dragging)
            {
                var p = Cursor.Position;
                ExternalPip.DragTo(target, dragFrom.X + p.X - dragStart.X, dragFrom.Y + p.Y - dragStart.Y);
                return;
            }
            int i = IndexAt(e.Location);
            if (i != hot) { hot = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot != -1) { hot = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || target == IntPtr.Zero) return;
            pressed = IndexAt(e.Location);
            if (pressed < 0 && barRect.Contains(e.Location))
            {
                dragging = true;                       // grab the bar to move the PiP
                dragStart = Cursor.Position;
                dragFrom = U.WindowRect(target);
                ExternalPip.Pin(target);
                hot = -1;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging) { dragging = false; Invalidate(); return; }
            int i = IndexAt(e.Location);
            if (e.Button == MouseButtons.Left && i >= 0 && i == pressed) Run(btns[i]);
            pressed = -1;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (target == IntPtr.Zero) return;
            ExternalPip.HoverAction(target, e.Delta > 0 ? "volup" : "voldown");
            Invalidate();
        }
    }

    /// <summary>A pill under another app's PiP that says whose it is, fading out after a moment.</summary>
    class ExtSplash : Form
    {
        static readonly Color Fill = Color.FromArgb(20, 20, 24);
        readonly Timer timer = new Timer { Interval = 30 };
        string text = "";
        DateTime until;
        float scale = 1;
        double level = -1;

        public ExtSplash()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Fill;
            Text = "PiP note";
            Opacity = 0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            timer.Tick += (o, e) =>
            {
                double left = (until - DateTime.Now).TotalMilliseconds;
                double target = left > 0 ? 0.96 : 0;
                Opacity = U.Approach(Opacity, target, U.Animations ? 0.2 : 1);
                if (left <= 0 && Opacity < 0.02) { timer.Stop(); Hide(); }
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            U.RoundCorners(Handle, true);
            U.ExcludeFromCapture(Handle, true);
        }

        public void ShowFor(IntPtr h, string t, double lvl)
        {
            text = t;
            level = lvl;
            uint d = Native.GetDpiForWindow(h);
            scale = d > 0 ? d / 96f : 1;
            var pb = U.WindowRect(h);
            bool bar = level >= 0;
            int ph = (int)Math.Round((bar ? 46 : 30) * scale), pad = (int)Math.Round(14 * scale), gap = (int)Math.Round(6 * scale);
            int tw = LT.MeasureText(text, U.UIBold(13 * scale), Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            int w = Math.Max(bar ? (int)Math.Round(200 * scale) : 0, tw + pad * 2);
            var wa = Screen.FromHandle(h).WorkingArea;
            int y = pb.Bottom + gap;
            if (y + ph > wa.Bottom) y = pb.Top - gap - ph;
            if (y < wa.Top) y = pb.Bottom - gap - ph;
            int x = Math.Max(wa.Left, Math.Min(pb.Left + (pb.Width - w) / 2, wa.Right - w));
            Bounds = new Rectangle(x, y, w, ph);
            Invalidate();
            until = DateTime.Now.AddMilliseconds(bar ? 1200 : 1800);
            if (!Visible) Show();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Fill);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (level < 0) { LT.DrawText(g, text, U.UIBold(13 * scale), ClientRectangle, Color.White, flags); return; }
            // text on top, a rounded level bar under it (same look as PiP Studio's own PiPs)
            int pad = (int)Math.Round(14 * scale), bh = Math.Max(4, (int)Math.Round(4 * scale));
            LT.DrawText(g, text, U.UIBold(13 * scale), new Rectangle(0, (int)Math.Round(4 * scale), ClientSize.Width, (int)Math.Round(24 * scale)), Color.White, flags);
            var track = new Rectangle(pad, ClientSize.Height - pad + 2 - bh, ClientSize.Width - 2 * pad, bh);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(70, 70, 78))) Pill(g, b, track);
            int fw = (int)Math.Round(track.Width * Math.Max(0, Math.Min(1, level)));
            if (fw > 0) using (var b = new SolidBrush(Theme.Accent)) Pill(g, b, new Rectangle(track.X, track.Y, Math.Max(bh, fw), bh));
        }

        static void Pill(Graphics g, Brush b, Rectangle r)
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int d = r.Height;
                path.AddArc(r.X, r.Y, d, d, 90, 180);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
                path.CloseFigure();
                g.FillPath(b, path);
            }
        }
    }

    /// <summary>Finds the bars around a video in a picture: solid dark strips (black, or an app's dark grey background) on
    /// each side, measured on the middle of each side so title strips, buttons and rounded corners don't get in the way.
    /// Used for other apps' PiPs (trimming the window) and PiP Studio's own PiPs (cropping the picture).</summary>
    static class Letterbox
    {
        /// <summary>Bars in pixels: an even margin around everything (a frame) isn't counted, each pair counts as twice its
        /// thinner side (letterboxing is centered), and only one pair is kept. False when there's nothing to go on.</summary>
        public static bool Find(Bitmap bmp, out int top, out int bottom, out int left, out int right)
        {
            top = bottom = left = right = 0;
            bool flat = false;
            {
                int W = bmp.Width, H = bmp.Height;
                var data = bmp.LockBits(new Rectangle(0, 0, W, H), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int stride = data.Stride;
                    var px = new byte[stride * H];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
                    Func<int, int, int> at = (x, y) => y * stride + x * 4;
                    // a bar pixel: close to the bar's color, which must be dark
                    Func<int, int, int, bool> like = (x, y, refO) =>
                    {
                        int o = at(x, y);
                        return Math.Abs(px[o] - px[refO]) <= 10 && Math.Abs(px[o + 1] - px[refO + 1]) <= 10 && Math.Abs(px[o + 2] - px[refO + 2]) <= 10;
                    };
                    Func<int, bool> darkRef = o => px[o] <= 60 && px[o + 1] <= 60 && px[o + 2] <= 60;
                    int x0 = W / 5, x1 = W - W / 5, y0 = H / 5, y1 = H - H / 5;
                    int sx = Math.Max(1, (x1 - x0) / 48), sy = Math.Max(1, (y1 - y0) / 48);

                    // Measures from one edge inward over up to two solid dark strips: the window's own frame (Discord draws a thin
                    // dark-grey one) and then the black bar beside the video. A column/row belongs to a strip when, across the
                    // middle 60% of the other direction, it matches that strip's color.
                    Func<int, int, bool, int> run = (start, step, cols) =>
                    {
                        int limit = (cols ? W : H) / 2;
                        Func<int, int, bool> lineLike = (pos, refO) =>
                        {
                            if (cols) { for (int y = y0; y < y1; y += sy) if (!like(pos, y, refO)) return false; }
                            else { for (int x = x0; x < x1; x += sx) if (!like(x, pos, refO)) return false; }
                            return true;
                        };
                        Func<int, int> refAt = pos => cols ? at(pos, H / 2) : at(W / 2, pos);
                        int n = 2;                                                // skip the outermost 2 px (window edge line)
                        for (int seg = 0; seg < 3 && n < limit; seg++)
                        {
                            int refO = refAt(start + n * step);
                            if (!darkRef(refO)) break;
                            int s0 = n;
                            while (n < limit && lineLike(start + n * step, refO)) n++;
                            if (n == s0) break;
                        }
                        return n > 3 ? n : 0;
                    };
                    Func<int, int, int> colRun = (start, step) => run(start, step, true);
                    Func<int, int, int> rowRun = (start, step) => run(start, step, false);
                    left = colRun(0, 1); right = colRun(W - 1, -1);
                    top = rowRun(0, 1); bottom = rowRun(H - 1, -1);
                    if (left >= W / 2 || top >= H / 2) flat = true;                  // one flat color: nothing showing yet
                }
                finally { bmp.UnlockBits(data); }
                if (flat) return false;
            }
            // a margin the app keeps around the video on every side (a frame) isn't a bar
            int frame = Math.Min(Math.Min(top, bottom), Math.Min(left, right));
            top -= frame; bottom -= frame; left -= frame; right -= frame;
            // letterboxing is centered: count each pair as twice the thinner side
            int v = Math.Min(top, bottom), hz = Math.Min(left, right);
            top = bottom = v < 3 ? 0 : v;
            left = right = hz < 3 ? 0 : hz;
            if (top > 0 && left > 0)
            {
                // bars on all sides: keep the bigger pair (the other is usually the window's frame)
                if (top >= left) left = right = 0; else top = bottom = 0;
            }
            return true;
        }
    }
}
