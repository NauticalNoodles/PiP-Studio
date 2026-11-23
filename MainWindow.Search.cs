using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PipStudio
{
    // Searching the settings, and capturing a key for a shortcut. Part of MainUI (MainWindow.cs).
    static partial class MainUI
    {
        // ----------------------------------------------------------------------------- search
        static void OnSearch()
        {
            UpdateSearchHint();
            string q = searchBox.Text.Trim();
            if (q.Length == 0)
            {
                if (page == "search") Navigate(lastSettingsPage);
                return;
            }
            var sp = pages["search"];
            sp.Children.Clear();
            var words = q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var hits = searchIndex.Where(s => { string hay = (s.Title + " " + s.Text + " " + s.Page).ToLowerInvariant(); return words.All(x => hay.Contains(x)); })
                                  .OrderBy(s => s.Title.ToLowerInvariant().Contains(words[0]) ? 0 : 1).Take(40).ToList();
            if (hits.Count == 0)
            {
                var none = Fluent.Label("No settings match “" + q + "”. Try a different word.", 14, "Sub");
                none.Margin = new Thickness(2, 4, 0, 0);
                sp.Children.Add(none);
            }
            foreach (var h in hits)
            {
                var hit = h;
                var b = ClickRow(sp, "", hit.Title, navLabels[Array.IndexOf(navKeys, hit.Page)], () => GoToSetting(hit));
            }
            if (page != "search")
            {
                page = "search";
                pageTitle.Text = "Search results";
                pageWindows.Visibility = pageRunning.Visibility = Visibility.Collapsed;
                pageScroll.Visibility = Visibility.Visible;
                foreach (var kv in pages) kv.Value.Visibility = kv.Key == "search" ? Visibility.Visible : Visibility.Collapsed;
                foreach (var nb in navBtns) nb.IsChecked = false;
                MoveIndicator(true);
                BuildCommands();
            }
            AnimatePage();
        }

        /// <summary>Glide the settings page so the element is in view (like BringIntoView, but animated).</summary>
        static void ScrollIntoViewSmooth(FrameworkElement el)
        {
            if (pageScroll == null || el == null || !el.IsVisible) { if (el != null) el.BringIntoView(); return; }
            double y;
            try { y = el.TranslatePoint(new Point(0, 0), pageScroll).Y; } catch { el.BringIntoView(); return; }
            double view = pageScroll.ViewportHeight, h = el.ActualHeight, m = 24;
            if (y >= m && y + h <= view - m) return;                     // already fully visible
            SmoothScroll.To(pageScroll, pageScroll.VerticalOffset + y - m);
        }

        static void GoToSetting(SearchEntry hit)
        {
            searchBox.Text = "";
            Navigate(hit.Page);
            if (hit.Reveal != null) hit.Reveal();
            DelayRun(Anim.On ? 380 : 30, () =>
            {
                if (w == null) return;
                ScrollIntoViewSmooth(hit.Element);
                Flash(hit.Element);
            });
        }

        /// <summary>Brief accent outline so the eye lands on the setting you searched for.</summary>
        static void Flash(FrameworkElement el)
        {
            var host = el as Panel;
            if (host is StackPanel && ((StackPanel)host).Children.Count > 0) host = ((StackPanel)host).Children[0] as Panel;   // expander: ring its header
            var ring = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2), IsHitTestVisible = false, Opacity = 0 };
            ring.SetResourceReference(Border.BorderBrushProperty, "Accent");
            if (host == null)
            {
                var b = el as Border;
                if (b == null || !(b.Child is Panel)) return;
                host = (Panel)b.Child;
            }
            if (host is Grid) { Grid.SetColumnSpan(ring, 99); Grid.SetRowSpan(ring, 99); }
            ring.Margin = new Thickness(-1);
            host.Children.Add(ring);
            Anim.Fade(ring, 1, 150, 0, () => Anim.Fade(ring, 0, 600, 900, () => host.Children.Remove(ring)));
        }

        // ----------------------------------------------------------------------------- key capture
        static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (captureKb != null)
            {
                var k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin || k == Key.ImeProcessed) return;
                e.Handled = true;
                if (k == Key.Escape) { EndKeyCapture(); return; }
                if (k == Key.Back || k == Key.Delete) { HK.Set(App.S, captureKb, ""); EndKeyCapture(); return; }
                int mods = 0;
                var mm = Keyboard.Modifiers;
                if ((mm & ModifierKeys.Alt) != 0) mods |= 1;
                if ((mm & ModifierKeys.Control) != 0) mods |= 2;
                if ((mm & ModifierKeys.Shift) != 0) mods |= 4;
                if ((mm & ModifierKeys.Windows) != 0) mods |= 8;
                if (mods == 0 || mods == 4) { captureKbBtn.Content = "Add Ctrl, Alt or Win…"; return; }
                HK.Set(App.S, captureKb, HK.Spec(mods, KeyInterop.VirtualKeyFromKey(k)));
                EndKeyCapture();
                return;
            }
            if (e.Key == Key.Escape && overlayOpen) { overlayOpen = false; ApplyLayout(true); e.Handled = true; return; }
            if ((e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) || (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control))
            {
                if (page == "windows" && winSearch != null) { winSearch.Focus(); winSearch.SelectAll(); e.Handled = true; return; }
                if (compact) OpenPane();
                searchBox.Focus(); searchBox.SelectAll(); e.Handled = true;
            }
            else if (e.Key == Key.F5) { winSig = runSig = ""; RefreshWindows(); RefreshRunning(); e.Handled = true; }
        }

        static void BeginKeyCapture(string field, Button btn)
        {
            if (captureKb != null) EndKeyCapture();
            App.I.UnregisterHotkeys();
            captureKb = field;
            captureKbBtn = btn;
            btn.Content = "Press keys…";
            w.Focus();
        }

        static void EndKeyCapture()
        {
            var btn = captureKbBtn;
            string field = captureKb;
            captureKb = null;
            captureKbBtn = null;
            App.I.SettingsChanged(true);
            if (btn != null && field != null) btn.Content = KeyText(field);
        }

        static string KeyText(string field) { string d = HK.Display(field); return d.Length == 0 ? "Not set" : d.Replace("+", " + "); }
    }
}
