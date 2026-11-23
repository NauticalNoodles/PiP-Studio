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
            // the utility PiPs: open each one, and check it draws (the picture goes to the folder, the result to the notes)
            foreach (var id in Widget.Ids)
                steps.Add(() =>
                {
                    var p = App.I.CreatePipForWidget(id);
                    Log(p == null ? "utility " + id + ": didn't open" : "utility " + id + ": open, " + p.Width + "x" + p.Height);
                });
            steps.Add(() =>
            {
                foreach (var p in App.I.Pips.Where(x => !x.IsClosing && x.IsWidget).ToList())
                {
                    try
                    {
                        using (var b = p.PreviewSnapshot())
                        {
                            if (b == null) { Log("utility " + p.St.Widget + ": no picture"); continue; }
                            var colors = new HashSet<int>();
                            for (int y = 0; y < b.Height; y += 7) for (int x = 0; x < b.Width; x += 7) colors.Add(b.GetPixel(x, y).ToArgb());
                            b.Save(Path.Combine(dir, "utility-" + p.St.Widget + ".png"));
                            if (p.St.Widget == "timer" || p.St.Widget == "ping")
                                using (var hd = p.RenderWidgetAt(600)) hd.Save(Path.Combine(dir, "share-" + p.St.Widget + ".png"));
                            if (p.St.Widget == "details")                       // sharp ones to share (the workflow prints them into its log)
                            {
                                using (var hd = p.RenderWidgetAt(1000)) hd.Save(Path.Combine(dir, "share-details.png"));
                                foreach (var t in new[] { 1, 3 })              // light and OLED too
                                {
                                    S.ThemeMode = t; Theme.Refresh();
                                    using (var hd = p.RenderWidgetAt(600)) hd.Save(Path.Combine(dir, "share-details-" + (t == 1 ? "light" : "oled") + ".png"));
                                }
                                S.ThemeMode = 2; Theme.Refresh();
                                Log("as administrator: " + App.IsElevated);
                            }
                            Log("utility " + p.St.Widget + ": drew " + b.Width + "x" + b.Height + ", " + colors.Count + " colours");
                        }
                    }
                    catch (Exception ex) { Log("utility " + p.St.Widget + ": " + ex.GetType().Name + " " + ex.Message); }
                }
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
