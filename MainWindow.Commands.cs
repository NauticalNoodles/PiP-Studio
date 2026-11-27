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
    // The command bar above each page (Task Manager style). Part of MainUI (MainWindow.cs).
    static partial class MainUI
    {
        // ----------------------------------------------------------------------------- command bar (Task Manager style)
        /// <summary>A button above a page, as the controller's options menu shows it.</summary>
        class PageCommand { public string Glyph, Text; public Action Run; }

        static Wpf.Ui.Controls.Button Cmd(string glyph, string text, Action click, bool enabled = true)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var g = new TextBlock { Text = glyph, FontFamily = Fluent.IconFont, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            sp.Children.Add(g);
            sp.Children.Add(new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var b = new Wpf.Ui.Controls.Button
            {
                Content = sp,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,     // the commands line up in columns (AlignCommands)
                Margin = new Thickness(0, 0, 4, 2),
                Padding = new Thickness(11, 4, 11, 4),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
                BorderThickness = new Thickness(0),
                IsEnabled = enabled
            };
            b.Click += (o, e) => { try { click(); } catch (Exception ex) { App.ReportError(ex); } };
            b.Tag = new PageCommand { Glyph = glyph, Text = text, Run = click };   // the controller's Menu button lists these
            return b;
        }

        /// <summary>When the commands wrap onto a second line, every one gets the widest one's width so they line up in
        /// columns; when they all fit on one row they keep their own widths (no big gaps). Measured once the text is in
        /// place (after translation), and again when the window's width changes.</summary>
        static void AlignCommands()
        {
            if (cmdBar == null) return;
            double max = 0, sum = 0;
            foreach (UIElement c in cmdBar.Children)
            {
                c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                max = Math.Max(max, c.DesiredSize.Width);
                sum += c.DesiredSize.Width;
            }
            double avail = cmdBar.ActualWidth;
            bool fits = avail <= 0 || sum <= avail + 0.5;
            double want = fits || max <= 0 ? double.NaN : Math.Ceiling(max);
            if (!(double.IsNaN(want) && double.IsNaN(cmdBar.ItemWidth)) && cmdBar.ItemWidth != want) cmdBar.ItemWidth = want;
        }

        static void BuildCommands()
        {
            if (cmdBar == null) return;
            cmdBar.Children.Clear();
            cmdBar.ItemWidth = double.NaN;
            Dispatch(AlignCommands, DispatcherPriority.Loaded);
            if (ExplainPages.Contains(page ?? ""))
            {
                // the explanations panel on the right: hide it, or bring it back
                cmdBar.Children.Add(Cmd(App.S.ShowExplainers ? "\uED1A" : "\uE890", App.S.ShowExplainers ? "Hide explanations" : "Show explanations", ToggleExplainers));
            }
            if (page == "windows")
            {
                var it = SelItem();
                cmdBar.Children.Add(Cmd("", "Open in PiP", OpenSelected, it != null));
                cmdBar.Children.Add(Cmd("", "Click a window", () => App.I.PickOnScreen(null)));
                cmdBar.Children.Add(Cmd("", "Screen region", () => App.I.PipScreenRegion(null)));
                cmdBar.Children.Add(Cmd("", "Refresh", () => { U.ClearProcessCache(); iconCache.Clear(); winSig = ""; RefreshWindows(); }));
                bool real = it != null && !it.IsDisplay && !it.IsAuto && it.Hwnd != IntPtr.Zero;
                cmdBar.Children.Add(Cmd("\uE8A7", "Treat as a PiP", () =>
                {
                    var s2 = SelItem();
                    if (s2 == null || s2.IsDisplay || s2.IsAuto || s2.Hwnd == IntPtr.Zero) return;
                    IntPtr mh = s2.Hwnd;
                    string app = ExternalPip.AppName(mh);
                    if (U.CoversScreen(mh))
                    {
                        // a full-screen window kept on top of everything would leave you stuck under it
                        Ask("Full-screen windows can't be PiPs", "It would stay on top of everything else on that display. Switch it to windowed mode first, then try again.", "OK", null, null);
                        return;
                    }
                    // say what it means first: it changes how the window behaves (it stays on top, snaps, follows your shortcuts)
                    string which = ExternalPip.MarksWholeKind(mh)
                        ? "This applies to every window like this one from " + app + ", now and later."
                        : "This applies to this window only, until it closes.";
                    Ask("Treat this window as a PiP?",
                        "PiP Studio will handle it like another app's picture-in-picture window:\n\n" +
                        "•  It moves to Running PiPs and snaps to PiP spots\n" +
                        "•  It stays on top of your other windows once you move or steer it\n" +
                        "•  Your PiP shortcuts and controller move, resize, fade and close it\n\n" +
                        which + " You can undo it any time under Running PiPs > Marked as PiPs, which also takes it off always on top.",
                        "Treat as a PiP", "Cancel", ok =>
                        {
                            if (!ok || !Native.IsWindow(mh)) return;
                            ExternalPip.MarkPip(mh);                        // it moves to Running PiPs, where PiP Studio can steer it
                            winSig = ""; runSig = ""; RefreshWindows(); BuildCommands();
                        });
                }, real));
            }
            else if (page == "running" && SelectedExternal() != IntPtr.Zero && Native.IsIconic(SelectedExternal()))
            {
                // another app's PiP that's minimized: bring it back, or stop treating it as a PiP
                Func<IntPtr> x = SelectedExternal;
                cmdBar.Children.Add(Cmd("\uE923", "Restore", () => { ExternalPip.Restore(x()); DelayRun(250, () => { runSig = ""; RefreshRunning(); BuildCommands(); }); }, true));
                cmdBar.Children.Add(Cmd("\uE711", "Close", () => { ExternalPip.Handle("close", x()); DelayRun(300, () => { runSig = ""; RefreshRunning(); BuildCommands(); }); }, true));
                cmdBar.Children.Add(Cmd("\uE894", "Not a PiP", () => { ExternalPip.MarkNotPip(x()); runSig = ""; RefreshRunning(); RefreshMarked(); BuildCommands(); }, true));
            }
            else if (page == "running" && SelectedExternal() != IntPtr.Zero)
            {
                // another app's PiP: what PiP Studio can do with it
                Func<IntPtr> x = SelectedExternal;
                cmdBar.Children.Add(Cmd("\uE8B0", "Next corner", () => { ExternalPip.Handle("corner", x()); runSig = ""; RefreshRunning(); }, true));
                cmdBar.Children.Add(Cmd("\uE740", "Bigger", () => { ExternalPip.Handle("grow", x()); runSig = ""; RefreshRunning(); }, true));
                cmdBar.Children.Add(Cmd("\uE73F", "Smaller", () => { ExternalPip.Handle("shrink", x()); runSig = ""; RefreshRunning(); }, true));
                cmdBar.Children.Add(Cmd("\uE767", "Mute", () => ExternalPip.Handle("mute", x()), true));
                cmdBar.Children.Add(Cmd("\uE711", "Close", () => { ExternalPip.Handle("close", x()); DelayRun(300, () => { runSig = ""; RefreshRunning(); BuildCommands(); }); }, true));
                cmdBar.Children.Add(Cmd("\uE712", "Options", () => { var r = runList.SelectedItem as PipRowItem; if (r != null) ShowPipOptions(r); }, true));
                cmdBar.Children.Add(Cmd("\uE894", "Not a PiP", () => { ExternalPip.MarkNotPip(x()); runSig = ""; RefreshRunning(); RefreshMarked(); BuildCommands(); }, true));
            }
            else if (page == "running")
            {
                var p = SelectedPip();
                bool any = App.I.Pips.Any(x => !x.IsClosing), sel = p != null;
                bool win = sel && p.St.Kind == SourceKind.Window;
                cmdBar.Children.Add(Cmd("", "Change source", () => { var q = SelectedPip(); if (q != null) App.I.ShowPicker(q); }, sel));
                cmdBar.Children.Add(Cmd("", "Crop", () => { var q = SelectedPip(); if (q != null) q.StartCrop(); }, sel));
                cmdBar.Children.Add(Cmd("", "Show source", () => { var q = SelectedPip(); if (q != null) q.BringSourceToFront(); }, win));
                cmdBar.Children.Add(Cmd(win && p.St.Follow ? "" : "", "Follow", () => { var q = SelectedPip(); if (q != null) { q.SetFollow(!q.St.Follow); runSig = ""; RefreshRunning(); BuildCommands(); } }, win));
                cmdBar.Children.Add(Cmd(App.I.HiddenAll ? "" : "", App.I.HiddenAll ? "Show all" : "Hide all", () => { App.I.ToggleHideAll(); BuildCommands(); }, any));
                Wpf.Ui.Controls.Button layBtn = null;                        // saved layouts: open one, or save these PiPs as one
                layBtn = Cmd("\uF0E2", "Layouts", () =>
                {
                    var cm = new ContextMenu { PlacementTarget = layBtn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                    foreach (var l in Layouts.All)
                    {
                        var lay = l;
                        var mi = new MenuItem { Header = lay.Name };
                        mi.Click += (o, e) => { Layouts.Open(lay, true); DelayRun(400, () => { runSig = ""; RefreshRunning(); BuildCommands(); }); };
                        cm.Items.Add(mi);
                    }
                    if (Layouts.All.Count > 0) cm.Items.Add(new Separator());
                    var save = new MenuItem { Header = L.T("Save these PiPs as a layout…"), IsEnabled = App.I.Pips.Any(x => !x.IsClosing) };
                    save.Click += (o, e) => AskLayoutName();
                    cm.Items.Add(save);
                    var manage = new MenuItem { Header = L.T("Manage layouts…") };
                    manage.Click += (o, e) => { Navigate("behavior", true); DelayRun(350, () => ScrollToLayouts()); };
                    cm.Items.Add(manage);
                    cm.IsOpen = true;
                });
                cmdBar.Children.Add(layBtn);
                cmdBar.Children.Add(Cmd("\uE712", "Options", () => { var r = runList.SelectedItem as PipRowItem; if (r != null && r.Pip != null) ShowPipOptions(r); }, sel));
                cmdBar.Children.Add(Cmd("", "Close", () => { var q = SelectedPip(); if (q != null) q.CloseAnimated(); }, sel));
            }
            cmdBar.Visibility = cmdBar.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
