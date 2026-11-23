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
    // The Windows page: the list of windows and displays to PiP, and the live preview. Part of MainUI (MainWindow.cs).
    static partial class MainUI
    {
        // ----------------------------------------------------------------------------- windows page
        static ImageSource IconFor(IntPtr hwnd)
        {
            ImageSource src;
            if (iconCache.TryGetValue(hwnd, out src)) return src;
            src = null;
            IntPtr hi = U.WindowIconHandle(hwnd, true);
            if (hi != IntPtr.Zero)
            {
                try
                {
                    var bs = Imaging.CreateBitmapSourceFromHIcon(hi, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bs.Freeze();
                    src = bs;
                }
                catch { }
            }
            iconCache[hwnd] = src;
            return src;
        }

        static void RefreshWindows()
        {
            if (winList == null) return;
            var items = new List<WinRowItem>();
            var used = new HashSet<IntPtr>(App.I.Pips.Where(p => !p.IsClosing && p.St.Kind == SourceKind.Window).Select(p => p.Src));
            int order = 0;
            foreach (var h in WinEnum.AltTab())
            {
                var r = U.WindowRect(h);
                bool min = Native.IsIconic(h);
                items.Add(new WinRowItem
                {
                    Hwnd = h,
                    Title = U.WindowTitle(h),
                    App = U.ProcessName(h),
                    SizeText = min ? "—" : r.Width + " × " + r.Height,
                    Status = used.Contains(h) ? "In PiP" : min ? "Minimized" : "",
                    Order = order++
                });
            }
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var b = screens[i].Bounds;
                items.Add(new WinRowItem
                {
                    IsDisplay = true,
                    ScreenBounds = b,
                    Title = "Display " + (i + 1) + (screens[i].Primary ? " (main)" : ""),
                    App = "Whole screen",
                    SizeText = b.Width + " × " + b.Height,
                    Status = App.I.Pips.Any(p => !p.IsClosing && p.St.Kind == SourceKind.Screen && p.St.ScreenRect == b) ? "In PiP" : "",
                    Glyph = "",
                    Order = 10000 + i
                });
            }
            string q = winFilter.Trim();
            if (q.Length > 0)
                items = items.Where(x => x.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || x.App.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            // Auto option: its own list above the table, outside sorting and filtering
            IntPtr autoNow = App.I.FirstAvailableWindow(IntPtr.Zero);
            var autoPip = App.I.Pips.FirstOrDefault(p => !p.IsClosing && p.St.Auto);
            if (autoPip != null && autoPip.Src != IntPtr.Zero) autoNow = autoPip.Src;
            var autoRow = new WinRowItem
            {
                IsAuto = true,
                Hwnd = autoNow,
                Title = "Auto",
                App = "Starts on the first available window, then switches to the one you last used. Skips minimized and closed windows.",
                SizeText = "\u2014",
                Status = autoPip != null ? "In PiP" : "",
                Glyph = "\uE895",
                Order = -1
            };
            Func<WinRowItem, IComparable> keyOf = x => sortKey == "name" ? (IComparable)x.Title.ToLowerInvariant()
                : sortKey == "app" ? x.App.ToLowerInvariant()
                : sortKey == "size" ? (IComparable)(x.SizeText == "—" ? 0L : (long)ParseArea(x.SizeText))
                : sortKey == "status" ? (IComparable)x.Status
                : x.Order;
            items = (sortDesc ? items.OrderByDescending(keyOf) : items.OrderBy(keyOf)).ToList();

            string aSig = autoRow.Hwnd + ":" + autoRow.Status;
            if (aSig != autoSig)
            {
                autoSig = aSig;
                bool autoWasSel = autoList.SelectedItem != null;
                syncingSel = true;
                autoList.ItemsSource = new[] { autoRow };
                if (autoWasSel) autoList.SelectedIndex = 0;
                syncingSel = false;
                if (autoWasSel) OnWinSelected();
            }

            string sig = "n" + items.Count + "|" + string.Join("|", items.Select(x => x.Hwnd + ":" + x.Title + ":" + x.App + ":" + x.SizeText + ":" + x.Status + ":" + x.IsDisplay + ":" + x.IsAuto));
            if (sig == winSig) return;
            winSig = sig;
            foreach (var x in items) if (!x.IsDisplay && !x.IsAuto) { x.Icon = IconFor(x.Hwnd); x.MayFreeze = MayFreeze(x.Hwnd); }

            var prev = winList.SelectedItem as WinRowItem;
            winList.ItemsSource = items;
            WinRowItem again = null;
            if (prev != null) again = items.FirstOrDefault(x => prev.IsAuto ? x.IsAuto : prev.IsDisplay ? (x.IsDisplay && x.ScreenBounds == prev.ScreenBounds) : (!x.IsAuto && !x.IsDisplay && x.Hwnd == prev.Hwnd));
            if (again == null && items.Count > 0 && prev == null && autoList.SelectedItem == null) again = items[0];
            syncingSel = true;
            winList.SelectedItem = again;
            syncingSel = false;
            OnWinSelected();
        }

        /// <summary>The selected Windows-page entry: the Auto option or a row in the table.</summary>
        static WinRowItem SelItem()
        {
            if (autoList != null && autoList.SelectedItem is WinRowItem) return (WinRowItem)autoList.SelectedItem;
            return winList == null ? null : winList.SelectedItem as WinRowItem;
        }

        static double ParseArea(string s)
        {
            var p = s.Split('×');
            int a, b;
            if (p.Length == 2 && int.TryParse(p[0].Trim(), out a) && int.TryParse(p[1].Trim(), out b)) return (double)a * b;
            return 0;
        }

        static IntPtr shownSel = IntPtr.Zero;
        static bool shownSelDisplay, shownSelAuto;

        static void OnWinSelected()
        {
            if (winList == null) return;
            var it = SelItem();
            bool same = it != null && it.Hwnd == shownSel && it.IsDisplay == shownSelDisplay && it.IsAuto == shownSelAuto;
            shownSel = it == null ? IntPtr.Zero : it.Hwnd;
            shownSelDisplay = it != null && it.IsDisplay;
            shownSelAuto = it != null && it.IsAuto;
            selTitle.Text = it == null ? "Nothing selected" : it.Title;
            selSub.Text = it == null ? "Pick a window in the list."
                : it.IsAuto ? "Starts on the first available window, then switches to the one you were last using, even with Follow turned off."
                    + (it.Hwnd != IntPtr.Zero ? " Right now: " + U.Trunc(U.WindowTitle(it.Hwnd), 48) : "")
                : it.IsDisplay ? "Mirrors this whole display" : it.App + (it.Status.Length > 0 ? " · " + it.Status : "");
            previewNote.Text = it == null ? "Live preview" : it.IsAuto && it.Hwnd == IntPtr.Zero ? "Waiting for a window" : it.IsDisplay ? "" : Native.IsIconic(it.Hwnd) ? "Minimized — it will be restored" : "";
            previewNote.FontFamily = it != null && it.IsDisplay ? Fluent.IconFont : Fluent.TextFont;
            previewNote.FontSize = it != null && it.IsDisplay ? 40 : 12;
            BuildCommands();
            if (!same)
            {
                RefreshFixState(it);
                HideThumb();
                if (detailsCard.Visibility == Visibility.Visible) { Anim.Enter(selTitle, 6, 0, 200); Anim.Enter(selSub, 6, 30, 200); }
            }
            UpdatePreview();
        }

        static void OpenSelected()
        {
            var it = SelItem();
            if (it == null) return;
            if (it.IsAuto) App.I.CreateAutoPip();
            else if (it.IsDisplay) App.I.UseScreen(it.ScreenBounds, null);
            else if (Native.IsWindow(it.Hwnd)) App.I.UseWindow(it.Hwnd, null);
            winSig = "";
            DelayRun(300, RefreshWindows);
        }

        // live preview: a DWM thumbnail drawn onto this window over the preview box; it fades in
        static void HideThumb()
        {
            if (thumb != IntPtr.Zero) { Native.DwmUnregisterThumbnail(thumb); thumb = IntPtr.Zero; }
            thumbSrc = IntPtr.Zero;
            thumbRect = System.Drawing.Rectangle.Empty;
            thumbAlpha = 0;
        }

        static void FadeThumbIn()
        {
            thumbAlpha = Anim.On ? Math.Min(255, thumbAlpha + 26) : 255;
            var props = new Native.DWM_THUMBNAIL_PROPERTIES { dwFlags = Native.DWM_TNP_OPACITY, opacity = (byte)thumbAlpha };
            Native.DwmUpdateThumbnailProperties(thumb, ref props);
        }

        static void UpdatePreview()
        {
            if (w == null || previewBox == null) return;
            var it = SelItem();
            bool want = !App.S.LiteMode && !sizing && !DialogOpen && !OskOpen && w.IsActive && page == "windows" && it != null && !it.IsDisplay && previewBox.IsVisible && w.WindowState != WindowState.Minimized
                        && Native.IsWindow(it.Hwnd) && !Native.IsIconic(it.Hwnd)
                        && pageWindows.Opacity > 0.99 && detailsCard.Opacity > 0.99 && !IsMoving(pageWindows) && !IsMoving(detailsCard);
            if (!want) { HideThumb(); return; }
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (thumbSrc != it.Hwnd)
            {
                HideThumb();
                IntPtr t;
                if (Native.DwmRegisterThumbnail(hwnd, it.Hwnd, out t) != 0) return;
                thumb = t;
                thumbSrc = it.Hwnd;
                thumbAlpha = 0;
            }
            System.Drawing.Rectangle box;
            try
            {
                Rect rb = previewBox.TransformToAncestor(w).TransformBounds(new Rect(0, 0, previewBox.ActualWidth, previewBox.ActualHeight));   // includes the controller-mode zoom
                var dpi = VisualTreeHelper.GetDpi(w);
                int pad = (int)(8 * uiScale * dpi.DpiScaleX);
                box = new System.Drawing.Rectangle((int)Math.Round(rb.X * dpi.DpiScaleX) + pad, (int)Math.Round(rb.Y * dpi.DpiScaleY) + pad,
                    (int)Math.Round(rb.Width * dpi.DpiScaleX) - 2 * pad, (int)Math.Round(rb.Height * dpi.DpiScaleY) - 2 * pad);
            }
            catch { return; }
            Native.SIZE sz;
            if (Native.DwmQueryThumbnailSourceSize(thumb, out sz) != 0 || sz.cx < 2 || sz.cy < 2) return;
            var dst = U.FitRect(new System.Drawing.Size(sz.cx, sz.cy), box);
            if (dst == thumbRect) return;
            thumbRect = dst;
            var props = new Native.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = Native.DWM_TNP_RECTDESTINATION | Native.DWM_TNP_VISIBLE | Native.DWM_TNP_OPACITY,
                rcDestination = Native.RECT.From(dst),
                opacity = (byte)thumbAlpha,
                fVisible = true
            };
            Native.DwmUpdateThumbnailProperties(thumb, ref props);
        }

        static System.Windows.Media.Effects.Effect padGlow;

        /// <summary>The XAML with a solid background instead of Mica (parsed this way so Lite mode never flashes Mica).</summary>
        internal static string LiteXaml(string xaml)
        {
            return xaml.Replace("WindowBackdropType='Mica'", "WindowBackdropType='None' Background='{DynamicResource AppBg}'");
        }

        /// <summary>Applies Lite mode to the open window: backdrop, shadows and the live preview.</summary>
        static void ApplyLite()
        {
            if (w == null) return;
            bool lite = App.S.LiteMode;
            var fw = w as Wpf.Ui.Controls.FluentWindow;
            bool solid = lite || Theme.Oled;                              // Lite mode, or the OLED theme (black, no Mica)
            try { if (fw != null) fw.WindowBackdropType = solid ? Wpf.Ui.Controls.WindowBackdropType.None : Wpf.Ui.Controls.WindowBackdropType.Mica; } catch { }
            if (solid) w.SetResourceReference(Control.BackgroundProperty, "AppBg");
            else w.Background = Brushes.Transparent;
            if (padRing != null) padRing.Effect = lite ? null : padGlow;
            if (lite && navPane != null) navPane.Effect = null;
            if (previewBox != null) previewBox.Visibility = lite ? Visibility.Collapsed : Visibility.Visible;
            HideThumb();
            if (!lite) DelayRun(100, UpdatePreview);
        }

        static bool IsMoving(UIElement el)
        {
            var tt = el.RenderTransform as TranslateTransform;
            return tt != null && Math.Abs(tt.Y) > 0.5;
        }

        static void RefreshFixState(WinRowItem it)
        {
            if (chromiumCard == null) return;
            int token = ++fixToken;
            if (it == null || it.IsDisplay || it.IsAuto) { chromiumCard.Visibility = Visibility.Collapsed; return; }
            string exe = FrozenFix.ExePath(it.Hwnd);
            if (!FrozenFix.LooksChromium(exe)) { chromiumCard.Visibility = Visibility.Collapsed; return; }
            if (chromiumCard.Visibility != Visibility.Visible) { chromiumCard.Visibility = Visibility.Visible; Anim.Enter(chromiumCard, 8, 60, 260); }
            fixAlways.IsEnabled = false;
            fixState.Text = "Looking for this app's shortcuts…";
            Task.Run(() =>
            {
                var files = FrozenFix.FindShortcuts(exe);
                int with = FrozenFix.CountWithFlag(files);
                Fluent.App.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token != fixToken || fixAlways == null) return;
                    fixSync = true;
                    fixAlways.IsChecked = files.Count > 0 && with == files.Count;
                    fixSync = false;
                    fixAlways.IsEnabled = files.Count > 0;
                    fixState.Text = files.Count == 0 ? "No desktop, Start or taskbar shortcut was found for this app."
                        : files.Count + (files.Count == 1 ? " shortcut found." : " shortcuts found.") + " Takes effect the next time you start the app from one of them.";
                }));
            });
        }

        /// <summary>Chromium-based apps (Chrome, Edge, Discord, Spotify...) can freeze in a PiP: flagged with a warning icon on their row.</summary>
        static bool MayFreeze(IntPtr h)
        {
            string exe = FrozenFix.ExePath(h);
            if (exe == null) return false;
            bool v;
            if (!chromiumExe.TryGetValue(exe, out v)) { v = FrozenFix.LooksChromium(exe); chromiumExe[exe] = v; }
            return v;
        }

        /// <summary>The freeze fix with both options: restart now, or always start the app with the fix.</summary>
        static void ShowFreezeDialog(WinRowItem it)
        {
            if (it == null || it.IsDisplay || it.IsAuto) return;
            string exe = FrozenFix.ExePath(it.Hwnd);
            string name = U.ProcessName(it.Hwnd);
            var extra = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var always = new Wpf.Ui.Controls.ToggleSwitch { Content = "Always start " + name + " with the fix", IsEnabled = false };
            var state = Fluent.Label("Looking for this app's shortcuts…", 12, "Tertiary", true);
            state.Margin = new Thickness(0, 6, 0, 0);
            extra.Children.Add(always); extra.Children.Add(state);
            bool sync = true;
            Action load = () => Task.Run(() =>
            {
                var files = exe == null ? new List<string>() : FrozenFix.FindShortcuts(exe);
                int with = FrozenFix.CountWithFlag(files);
                Fluent.App.Dispatcher.BeginInvoke(new Action(() =>
                {
                    sync = true;
                    always.IsChecked = files.Count > 0 && with == files.Count;
                    sync = false;
                    always.IsEnabled = files.Count > 0;
                    state.Text = files.Count == 0 ? "No desktop, Start or taskbar shortcut was found for this app."
                        : files.Count + (files.Count == 1 ? " shortcut found." : " shortcuts found.") + " Takes effect the next time you start the app from one of them.";
                }));
            });
            always.Click += (o, e) =>
            {
                if (sync || exe == null) return;
                bool on = always.IsChecked == true;
                always.IsEnabled = false;
                state.Text = on ? "Adding the fix to the shortcuts…" : "Removing the fix from the shortcuts…";
                Task.Run(() =>
                {
                    var r = FrozenFix.EditShortcuts(FrozenFix.FindShortcuts(exe), on);
                    Fluent.App.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        string msg = (on ? "Added the fix to " : "Removed the fix from ") + r.Item1 + (r.Item1 == 1 ? " shortcut." : " shortcuts.");
                        if (r.Item2 > 0) msg += " " + r.Item2 + " couldn't be changed (all-users shortcuts need administrator rights).";
                        state.Text = msg;
                        always.IsEnabled = true;
                        fixToken++; RefreshFixState(SelItem());
                    }));
                });
            };
            load();
            IntPtr h = it.Hwnd;
            Ask("The picture may freeze",
                name + " stops drawing while other windows cover it, so its PiP can freeze. Restarting it with a fix keeps it drawing. Anything unsaved in it will be lost.",
                "Restart with the fix", "Close", yes =>
                {
                    if (!yes) return;
                    FrozenFix.RelaunchNow(h);
                    DelayRun(5000, () => { winSig = ""; RefreshWindows(); });
                }, true, extra, always);
        }

        static void ApplyAlwaysFix(bool on)
        {
            if (fixSync) return;
            var it = SelItem();
            if (it == null || it.IsDisplay) return;
            string exe = FrozenFix.ExePath(it.Hwnd);
            if (exe == null) return;
            fixAlways.IsEnabled = false;
            fixState.Text = on ? "Adding the fix to the shortcuts…" : "Removing the fix from the shortcuts…";
            Task.Run(() =>
            {
                var r = FrozenFix.EditShortcuts(FrozenFix.FindShortcuts(exe), on);
                Fluent.App.Dispatcher.BeginInvoke(new Action(() =>
                {
                    string msg = (on ? "Added the fix to " : "Removed the fix from ") + r.Item1 + (r.Item1 == 1 ? " shortcut." : " shortcuts.");
                    if (r.Item2 > 0) msg += " " + r.Item2 + " couldn't be changed (all-users shortcuts need administrator rights).";
                    if (fixState != null) fixState.Text = msg;
                    if (winList != null) { fixToken++; RefreshFixState(SelItem()); }
                }));
            });
        }
    }
}
