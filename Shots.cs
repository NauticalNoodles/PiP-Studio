using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PipStudio
{
    /// <summary>
    /// "PiPStudio.exe --screenshots folder": opens the window on each page, saves a picture of it, and quits. Used by the
    /// screenshots workflow on GitHub's Windows machines to make the screenshots for the README and the Store, so they
    /// always show the current version. It changes the settings it needs (dark theme, explanations on), so it's meant
    /// for a machine with no settings of its own.
    /// </summary>
    static partial class MainUI
    {
        public static string ShotsDir(string[] args)
        {
            int i = Array.FindIndex(args, a => a.Equals("--screenshots", StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>A line in progress.txt in the screenshots folder (the workflow keeps it, to see how far it got).</summary>
        public static void ShotsLog(string[] args, string msg)
        {
            string dir = ShotsDir(args);
            if (dir == null) return;
            try { Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir, "progress.txt"), DateTime.Now.ToString("HH:mm:ss.fff ") + msg + "\r\n"); } catch { }
        }
        static string shotsDir;
        static void Log(string msg) { if (shotsDir != null) ShotsLog(new[] { "--screenshots", shotsDir }, msg); }

        public static void TakeShots(string dir)
        {
            shotsDir = dir;
            Directory.CreateDirectory(dir);
            Log("taking screenshots");
            var S = App.S;
            S.ThemeMode = 2; S.ShowExplainers = true; S.CompanionOn = !Packaged.IsPackaged; S.AllowMultiple = true;
            App.I.SettingsChanged();
            var steps = new List<Action>();
            steps.Add(() =>
            {
                Open("windows");
                w.WindowState = WindowState.Normal;
                var wa = SystemParameters.WorkArea;                       // never bigger than the screen (Windows would squeeze it back)
                w.Left = wa.Left; w.Top = wa.Top; w.Width = Math.Min(1440, wa.Width); w.Height = Math.Min(900, wa.Height);
                Log("window open, " + w.Width + "x" + w.Height + " of " + wa.Width + "x" + wa.Height);
            });
            // a window to show in a PiP, if the workflow opened one (Notepad). A step of its own: if it asks something,
            // the timer keeps going.
            foreach (var title in new[] { "Sunset timelapse", "Lo-fi radio" })   // the pages the workflow opened, one PiP each
                steps.Add(() =>
                {
                    var src = WinEnum.AltTab().FirstOrDefault(h => !U.IsOurs(h) && (U.WindowTitle(h) ?? "").StartsWith(title, StringComparison.OrdinalIgnoreCase));
                    if (src != IntPtr.Zero) App.I.UseWindow(src, null);
                    Log(src != IntPtr.Zero ? "PiP of " + title : "no " + title);
                });
            Action<string, string> shot = (pg, name) =>
            {
                steps.Add(() => Navigate(pg, false));
                steps.Add(() => Save(Path.Combine(dir, name + ".png")));
            };
            shot("windows", "windows");
            steps.Add(() => App.I.ToggleHideAll());                           // the PiPs out of the way of the live preview
            steps.Add(() => SaveFromScreen(Path.Combine(dir, "windows-screen.png")));   // with the live preview, which Windows draws on top
            steps.Add(() => App.I.ToggleHideAll());
            shot("running", "running");
            shot("appearance", "appearance");
            shot("behavior", "behavior");
            shot("controller", "controller");
            shot("shortcuts", "shortcuts");
            shot("settings", "settings");
            shot("phone", "phone");
            // side by side: one of the demo pages made full-screen, then turned on: the page should shrink, the PiPs move to the strip
            steps.Add(() =>
            {
                var sun = WinEnum.AltTab().FirstOrDefault(h => (U.WindowTitle(h) ?? "").StartsWith("Sunset timelapse", StringComparison.OrdinalIgnoreCase));
                var sb = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                if (sun != IntPtr.Zero) { Native.SetWindowPos(sun, IntPtr.Zero, sb.X, sb.Y, sb.Width, sb.Height, Native.SWP_NOACTIVATE); Log("side by side: page made full-screen " + U.WindowRect(sun)); }
                w.WindowState = WindowState.Minimized;
            });
            steps.Add(() =>
            {
                var before = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                Pbp.On();
                Log("side by side: on=" + Pbp.Active + " strip=" + Pbp.Strip + " work area " + before + " -> " + System.Windows.Forms.Screen.PrimaryScreen.WorkingArea);
                foreach (var kv in Pbp.Shrunk) Log("side by side: shrank " + U.WindowTitle(kv.Key) + " from " + kv.Value + " to " + U.WindowRect(kv.Key));
            });
            steps.Add(() =>
            {
                foreach (var p in App.I.Pips.Where(x => !x.IsClosing)) Log("side by side: PiP " + p.DisplayName + " at " + p.TargetBounds);
                var sb = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                using (var full = new System.Drawing.Bitmap(sb.Width, sb.Height))
                {
                    using (var g = System.Drawing.Graphics.FromImage(full)) g.CopyFromScreen(sb.Location, System.Drawing.Point.Empty, sb.Size);
                    using (var small = new System.Drawing.Bitmap(full, new System.Drawing.Size(960, 540))) small.Save(Path.Combine(dir, "share-pbp.png"));
                }
                Pbp.Off();
                Log("side by side: off, work area " + System.Windows.Forms.Screen.PrimaryScreen.WorkingArea);
                w.WindowState = WindowState.Normal;
            });
            steps.Add(() => { var sun = WinEnum.AltTab().FirstOrDefault(h => (U.WindowTitle(h) ?? "").StartsWith("Sunset timelapse", StringComparison.OrdinalIgnoreCase)); if (sun != IntPtr.Zero) Log("side by side: page back at " + U.WindowRect(sun)); });
            steps.Add(() => { foreach (var p in App.I.Pips.Where(x => !x.IsClosing)) Log("side by side: PiP " + p.DisplayName + " back at " + p.TargetBounds); });
            // side by side, black bars: the demo game page fills the screen (drawn the way a full-screen game set to the narrower
            // resolution looks), then black bars turn on: the PiPs should fill the bars beside it
            steps.Add(() =>
            {
                var game = WinEnum.AltTab().FirstOrDefault(h => (U.WindowTitle(h) ?? "").StartsWith("Demo game", StringComparison.OrdinalIgnoreCase));
                if (game == IntPtr.Zero) { Log("black bars: no demo game window"); return; }
                var sb = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                var wr = U.WindowRect(game); var cr = U.ClientScreenRect(game);
                int l = cr.Left - wr.Left, t = cr.Top - wr.Top, r = wr.Right - cr.Right, b = wr.Bottom - cr.Bottom;   // its frame and title bar go off-screen
                Native.SetWindowPos(game, IntPtr.Zero, sb.X - l, sb.Y - t, sb.Width + l + r, sb.Height + t + b, 0);
                U.ForceForeground(game);
                Log("black bars: game page at " + U.ClientScreenRect(game));
            });
            steps.Add(() =>
            {
                S.PbpMode = 1;
                Pbp.On();
                Log("black bars: on=" + Pbp.Active + " bars " + Pbp.Strip2 + " and " + Pbp.Strip + ", game size " + Pbp.GameSize(System.Windows.Forms.Screen.PrimaryScreen));
            });
            steps.Add(() =>
            {
                foreach (var p in App.I.Pips.Where(x => !x.IsClosing)) Log("black bars: PiP " + p.DisplayName + " at " + p.TargetBounds + (p.Visible ? "" : " (hidden)"));
                var sb = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                using (var full = new System.Drawing.Bitmap(sb.Width, sb.Height))
                {
                    using (var g = System.Drawing.Graphics.FromImage(full)) g.CopyFromScreen(sb.Location, System.Drawing.Point.Empty, sb.Size);
                    using (var small = new System.Drawing.Bitmap(full, new System.Drawing.Size(1280, 720))) small.Save(Path.Combine(dir, "share-pbp-blackbars.png"));
                }
                Pbp.Off();
                S.PbpMode = 0;
                var game = WinEnum.AltTab().FirstOrDefault(h => (U.WindowTitle(h) ?? "").StartsWith("Demo game", StringComparison.OrdinalIgnoreCase));
                if (game != IntPtr.Zero) Native.ShowWindow(game, 6);       // minimized, out of the way
            });
            // side by side, virtual display: the demo game in a window the shape of the space beside the strip (what a virtual
            // display sized for it would hold; this machine has one display), shown as the game
            steps.Add(() =>
            {
                var game = WinEnum.AltTab().FirstOrDefault(h => (U.WindowTitle(h) ?? "").StartsWith("Demo game", StringComparison.OrdinalIgnoreCase));
                if (game == IntPtr.Zero) { Log("virtual display: no demo game window"); return; }
                Native.ShowWindow(game, 9);
                var sb = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                var size = Pbp.RestSize(System.Windows.Forms.Screen.PrimaryScreen);
                var wr = U.WindowRect(game); var cr = U.ClientScreenRect(game);
                int l = cr.Left - wr.Left, t = cr.Top - wr.Top, r = wr.Right - cr.Right, b = wr.Bottom - cr.Bottom;
                Native.SetWindowPos(game, IntPtr.Zero, sb.X - l, sb.Y - t, size.Width + l + r, size.Height + t + b, 0);
                GameDisplay.Override = game;
                Log("virtual display: game window " + U.ClientScreenRect(game) + " for a game area of " + size);
            });
            steps.Add(() =>
            {
                S.PbpMode = 2;
                Pbp.On();
                Log("virtual display: on=" + Pbp.Active + " strip " + Pbp.Strip + ", showing " + U.WindowTitle(Pbp.ShownGame));
            });
            steps.Add(() =>
            {
                foreach (var p in App.I.Pips.Where(x => !x.IsClosing)) Log("virtual display: PiP " + p.DisplayName + " at " + p.TargetBounds + (p.Visible ? "" : " (hidden)"));
                var sb = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                using (var full = new System.Drawing.Bitmap(sb.Width, sb.Height))
                {
                    using (var g = System.Drawing.Graphics.FromImage(full)) g.CopyFromScreen(sb.Location, System.Drawing.Point.Empty, sb.Size);
                    using (var small = new System.Drawing.Bitmap(full, new System.Drawing.Size(1280, 720))) small.Save(Path.Combine(dir, "share-pbp-virtual.png"));
                }
                Pbp.Off();
                GameDisplay.Override = IntPtr.Zero;
                S.PbpMode = 0;
                var game = WinEnum.AltTab().FirstOrDefault(h => (U.WindowTitle(h) ?? "").StartsWith("Demo game", StringComparison.OrdinalIgnoreCase));
                if (game != IntPtr.Zero) Native.ShowWindow(game, 6);
            });
            // the setting, opened, with black bars picked (so the resolution to use shows)
            steps.Add(() =>
            {
                w.WindowState = WindowState.Normal;
                S.PbpMode = 2; S.ShowExplainers = false;
                SwitchLanguage();                                         // rebuilt, so the setting shows Black bars and the resolution
                Navigate("behavior", false);
            });
            steps.Add(() =>
            {
                if (pbpRows != null) { pbpRows.Open(); }
            });
            steps.Add(() =>
            {
                if (pbpRows != null) try { var last = pbpRows.Rows.Children[pbpRows.Rows.Children.Count - 1] as FrameworkElement; if (last != null) last.BringIntoView(); } catch { }
            });
            steps.Add(() =>
            {
                string tmp = Path.Combine(dir, "behavior-pbp.png");
                Save(tmp);
                using (var big = new System.Drawing.Bitmap(tmp))
                using (var small = new System.Drawing.Bitmap(big, new System.Drawing.Size(1200, (int)(1200.0 * big.Height / big.Width)))) small.Save(Path.Combine(dir, "share-pbp-setting.png"));
                S.PbpMode = 0; S.ShowExplainers = true;
            });
            steps.Add(() => { S.ThemeMode = 3; SwitchLanguage(); Navigate("appearance", false); });   // OLED (rebuilt, so the theme list shows it)
            steps.Add(() => Save(Path.Combine(dir, "appearance-oled.png")));
            steps.Add(() => { S.ThemeMode = 1; SwitchLanguage(); Navigate("behavior", false); });     // light
            steps.Add(() => Save(Path.Combine(dir, "behavior-light.png")));
            steps.Add(() => { S.ThemeMode = 2; App.I.SettingsChanged(); App.I.ExitApp(); });

            // each step on the window's thread, from a thread of its own at top priority: nothing on the window can hold it up
            var ui = Dispatcher.CurrentDispatcher;                          // the window isn't open yet: this thread's
            var th = new System.Threading.Thread(() =>
            {
                for (int n = 0; n < steps.Count; n++)
                {
                    System.Threading.Thread.Sleep(2200);                    // time for the page's animations to settle
                    Log("step " + n);
                    int k = n;
                    try { ui.Invoke(steps[k], DispatcherPriority.Send); }
                    catch (Exception ex) { try { File.AppendAllText(Path.Combine(dir, "errors.txt"), ex + "\r\n"); } catch { } }
                }
            }) { IsBackground = true };
            th.Start();
        }

        /// <summary>The window as it is on screen (includes the live preview, which Windows draws over the window).</summary>
        static void SaveFromScreen(string path)
        {
            var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
            var r = U.FrameRect(h);                                     // the visible frame, without the invisible resize border
            using (var bmp = new System.Drawing.Bitmap(r.Width, r.Height))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, System.Drawing.Point.Empty, r.Size);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            Log("saved " + Path.GetFileName(path));
        }

        /// <summary>The window drawn at twice its size on its own solid background (Mica can't be captured this way).</summary>
        static void Save(string path)
        {
            var root = w.Content as FrameworkElement;
            if (root == null) return;
            root.UpdateLayout();
            double W = root.ActualWidth, H = root.ActualHeight, k = 2;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var bg = (w.TryFindResource("AppBg") as Brush) ?? Brushes.Black;
                dc.DrawRectangle(bg, null, new Rect(0, 0, W, H));
                dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, W, H));
            }
            var bmp = new RenderTargetBitmap((int)(W * k), (int)(H * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = File.Create(path)) enc.Save(f);
            Log("saved " + System.IO.Path.GetFileName(path));
        }
    }
}
