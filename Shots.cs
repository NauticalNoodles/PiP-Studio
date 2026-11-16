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

        public static void TakeShots(string dir)
        {
            Directory.CreateDirectory(dir);
            var S = App.S;
            S.ThemeMode = 2; S.ShowExplainers = true; S.CompanionOn = !Packaged.IsPackaged;
            App.I.SettingsChanged();
            // a window to show in a PiP, if the workflow opened one (Notepad)
            try
            {
                var src = WinEnum.AltTab().FirstOrDefault(h => !U.IsOurs(h) && U.ProcessName(h).Equals("notepad", StringComparison.OrdinalIgnoreCase));
                if (src != IntPtr.Zero) App.I.UseWindow(src, null);
            }
            catch (Exception ex) { App.ReportError(ex); }

            Open("windows");
            w.WindowState = WindowState.Normal;
            w.Left = 0; w.Top = 0; w.Width = 1440; w.Height = 900;

            var steps = new List<Action>();
            Action<string, string> shot = (pg, name) =>
            {
                steps.Add(() => Navigate(pg, false));
                steps.Add(() => Save(Path.Combine(dir, name + ".png")));
            };
            shot("windows", "windows");
            shot("running", "running");
            shot("appearance", "appearance");
            shot("behavior", "behavior");
            shot("controller", "controller");
            shot("shortcuts", "shortcuts");
            shot("settings", "settings");
            shot("phone", "phone");
            steps.Add(() => { S.ThemeMode = 3; App.I.SettingsChanged(); Navigate("appearance", false); });   // OLED
            steps.Add(() => Save(Path.Combine(dir, "appearance-oled.png")));
            steps.Add(() => { S.ThemeMode = 1; App.I.SettingsChanged(); Navigate("behavior", false); });     // light
            steps.Add(() => Save(Path.Combine(dir, "behavior-light.png")));
            steps.Add(() => { S.ThemeMode = 2; App.I.SettingsChanged(); App.I.ExitApp(); });

            int n = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };   // time for the page's animations to settle
            t.Tick += (o, e) =>
            {
                if (n >= steps.Count) { t.Stop(); return; }
                try { steps[n++](); } catch (Exception ex) { App.ReportError(ex); File.AppendAllText(Path.Combine(dir, "errors.txt"), ex + "\r\n"); }
            };
            t.Start();
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
        }
    }
}
