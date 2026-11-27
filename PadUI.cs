using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PipStudio
{
    // =================================================================================
    //  Controller-friendly mode for the PiP Studio window.
    //  A bigger layout, an accent focus ring that glides between items, a bar of
    //  button hints, and full navigation with a controller:
    //    D-pad / left stick  move        A  select / open      B  back
    //    LB / RB             pages       Menu  navigation      right stick  scroll
    // =================================================================================
    static partial class MainUI
    {
        const int BtnA = 0x1000, BtnB = 0x2000, BtnX = 0x4000, BtnY = 0x8000, BtnLB = 0x100, BtnRB = 0x200, BtnMenu = 0x10, BtnGuide = 0x400;
        const int DirUp = 0x1 | 0x40000, DirDown = 0x2 | 0x80000, DirLeft = 0x4 | 0x100000, DirRight = 0x8 | 0x200000;
        const int RsUp = 0x400000, RsDown = 0x800000;
        const double PadScale = 1.15;

        static double uiScale = 1;
        static bool padVisual;
        static int padHeldDir;
        static DateTime padNextRepeat;
        static Rect ringTarget = Rect.Empty;
        static DispatcherTimer ringTimer;
        static Grid bodyGrid;
        static Canvas padLayer;
        static Border padRing, padBar;
        static StackPanel padHintsPanel, tabStrip;
        static Grid padTabs;
        static Canvas tabCanvas;
        static System.Windows.Shapes.Rectangle tabBar;
        static Border tabLB, tabRB;
        static readonly List<Border> tabs = new List<Border>();
        static string hintsKind;
        static readonly Dictionary<UIElement, Action> activators = new Dictionary<UIElement, Action>();
        static readonly MethodInfo onClick = typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Custom clickable rows (settings links, expander headers) become focusable and work with Enter, Space and the A button.</summary>
        static void MakeActivatable(FrameworkElement el, Action act)
        {
            el.Focusable = true;
            el.FocusVisualStyle = null;
            activators[el] = act;
            el.KeyDown += (o, e) =>
            {
                if (e.OriginalSource != el || (e.Key != Key.Enter && e.Key != Key.Space)) return;
                act();
                e.Handled = true;
            };
        }

        static void PadUiInit(Window win)
        {
            bodyGrid = F<Grid>("BodyGrid");
            padLayer = F<Canvas>("PadLayer");
            padRing = F<Border>("PadRing");
            padBar = F<Border>("PadBar");
            padHintsPanel = F<StackPanel>("PadHintsPanel");
            padTabs = F<Grid>("PadTabs"); tabStrip = F<StackPanel>("TabStrip"); tabCanvas = F<Canvas>("TabCanvas");
            tabBar = F<System.Windows.Shapes.Rectangle>("TabBar"); tabLB = F<Border>("TabLB"); tabRB = F<Border>("TabRB");
            BuildTabs();
            osk = null; oskTarget = null; dlgHost = null; dlgDone = null;
            padVisual = false; kbNav = false; uiScale = 1; padHeldDir = 0; ringTarget = Rect.Empty; hintsKind = null;
            bodyGrid.LayoutTransform = new ScaleTransform(1, 1);

            win.PreviewGotKeyboardFocus += (o, e) => { if (padVisual || kbNav) Dispatch(() => UpdateRing(true), DispatcherPriority.Loaded); };
            win.PreviewKeyDown += (o, e) =>
            {
                if (padVisual && App.S.PadUiMode == 1) SetPadVisual(false);       // a key press: back to the normal layout
                if (!e.Handled) KeyNav(e);
            };
            // the mouse: the keyboard's focus ring goes away until the arrows (or Tab) are used again
            win.PreviewMouseDown += (o, e) => { if (kbNav) { kbNav = false; if (!padVisual) { ringTimer.Stop(); UpdateRing(false); } } };
            win.Deactivated += (o, e) => padHeldDir = 0;
            // smooth-scroll focused settings into view instead of WPF's instant jump
            settingsHost.AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler((o, e) =>
            {
                if (!padVisual && !kbNav) return;
                var fe = e.TargetObject as FrameworkElement;
                if (fe == null) return;
                e.Handled = true;
                ScrollIntoViewSmooth(fe);
            }));
            win.Closed += (o, e) => { if (ringTimer != null) ringTimer.Stop(); activators.Clear(); };

            ringTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            ringTimer.Tick += (o, e) => UpdateRing(true);
            if (App.S.PadUiMode == 2) win.Loaded += (o, e) => SetPadVisual(true);
        }

        static bool kbNav;      // moving around with the keyboard: the focus ring shows, like in controller mode

        /// <summary>
        /// The arrow keys move between settings, rows and buttons (the way the D-pad does) instead of scrolling the page,
        /// and Enter switches, opens or presses what has the focus. Text boxes keep left/right for the cursor.
        /// </summary>
        static void KeyNav(KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            var k = e.Key;
            if (k == Key.Tab) { StartKbRing(); return; }                   // Tab works as always, now with the ring
            int dir = k == Key.Up ? 1 : k == Key.Down ? 2 : k == Key.Left ? 3 : k == Key.Right ? 4 : 0;
            var d = Focused();
            bool typing = d is TextBox || d is PasswordBox;
            if (dir != 0)
            {
                if (typing && (dir == 3 || dir == 4)) return;                // the text cursor
                if (d == winSearch && dir == 2) return;                     // the search box's own Down: into the list
                e.Handled = true;
                StartKbRing();
                if (DialogOpen) { DialogPad(0, dir == 1 ? DirUp : dir == 2 ? DirDown : dir == 3 ? DirLeft : DirRight); return; }
                if (OskOpen) { OskMove(dir); return; }
                PadMove(dir);
                return;
            }
            if ((k == Key.Enter || k == Key.Return) && !typing && !DialogOpen && !OskOpen && FocusIsInside())
            {
                if (d is ListBoxItem || Up<ListBoxItem>(d) != null) return;    // the tables handle Enter themselves
                e.Handled = true;
                StartKbRing();
                PadActivate();
            }
        }

        static void StartKbRing()
        {
            kbNav = true;
            if (!ringTimer.IsEnabled) ringTimer.Start();
            Dispatch(() => UpdateRing(false), DispatcherPriority.Loaded);
        }

        /// <summary>Turns the controller layout on or off ("Always" keeps it on).</summary>
        static void SetPadVisual(bool on)
        {
            if (w == null || bodyGrid == null) return;
            if (App.S.PadUiMode == 2) on = true;
            if (App.S.PadUiMode == 0) on = false;
            if (on == padVisual && (!on || hintsKind == App.I.PadKind)) return;
            padVisual = on;
            uiScale = on ? PadScale : 1;
            var st = (ScaleTransform)bodyGrid.LayoutTransform;
            st.ScaleX = st.ScaleY = uiScale;
            lastColsFor = -1;
            ApplyLayout(false);
            LayoutDetails();                     // explanations: a panel in controller mode, a PiP otherwise
            Dispatch(() => MoveIndicator(false), DispatcherPriority.Loaded);
            HideThumb();
            DelayRun(120, UpdatePreview);

            padTabs.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) Dispatch(() => MoveIndicator(false), DispatcherPriority.Loaded);
            if (on)
            {
                BuildPadHints();
                Dispatch(() => UpdateTabs(false), DispatcherPriority.Loaded);
                Anim.Enter(padTabs, -8, 0, 250);
                padBar.Visibility = Visibility.Visible;
                Anim.Enter(padHintsPanel, 8, 0, 250);
                ringTarget = Rect.Empty;
                ringTimer.Start();
                if (!FocusIsInside()) Dispatch(FocusDefault, DispatcherPriority.Loaded);
                else Dispatch(() => UpdateRing(false), DispatcherPriority.Loaded);
            }
            else
            {
                if (OskOpen) CloseOsk();
                padBar.Visibility = Visibility.Collapsed;
                if (!kbNav) ringTimer.Stop();
                Anim.Fade(padRing, 0, 120);
                ringTarget = Rect.Empty;
            }
        }

        static void BuildPadHints()
        {
            string kind = App.I.PadKind;
            hintsKind = kind;
            padHintsPanel.Children.Clear();
            Action<List<Pad.Glyph>, string> add = (glyphs, label) =>
            {
                var row = GlyphRow(glyphs, 24);
                row.Margin = new Thickness(18, 0, 8, 0);
                padHintsPanel.Children.Add(row);
                var l = Fluent.Label(label, 13, "Sub", false);
                l.VerticalAlignment = VerticalAlignment.Center;
                padHintsPanel.Children.Add(l);
            };
            add(Pad.Glyphs(BtnA, kind), "Select");
            add(Pad.Glyphs(BtnB, kind), "Back");
            var tabs = Pad.Glyphs(BtnLB, kind); tabs.AddRange(Pad.Glyphs(BtnRB, kind));
            add(tabs, "Tabs");
            add(Pad.Glyphs(BtnY, kind), "Search");
            if (ExplainPages.Contains(page ?? "")) add(Pad.Glyphs(BtnX, kind), App.S.ShowExplainers ? "Hide explanations" : "Explanations");
            add(Pad.Glyphs(BtnMenu, kind), "Options");
            add(new List<Pad.Glyph> { Pad.Group("R-Stick"), Pad.Glyph.Of("↕") }, "Scroll");
            tabLB.Child = GlyphRow(Pad.Glyphs(BtnLB, kind), 28);
            tabRB.Child = GlyphRow(Pad.Glyphs(BtnRB, kind), 28);
        }

        /// <summary>Controller buttons as they look on this controller: round face buttons in their own colours (Xbox's
        /// green A, PlayStation's blue ✕...), pills for bumpers, triggers and Options, plain text between them.</summary>
        static StackPanel GlyphRow(List<Pad.Glyph> glyphs, double size)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var gl in glyphs)
            {
                var fg = new SolidColorBrush(Color.FromRgb(gl.Fg.R, gl.Fg.G, gl.Fg.B));
                if (gl.Plain)
                {
                    var t = Fluent.Label(gl.Text, size * 0.5, "Sub", false);
                    t.VerticalAlignment = VerticalAlignment.Center;
                    t.Margin = new Thickness(1, 0, 4, 0);
                    row.Children.Add(t);
                    continue;
                }
                bool symbol = gl.Text.Length == 1 && gl.Text[0] > 0x2000;
                if (gl.Shape == "cross")                                  // the D-pad: just the cross
                {
                    row.Children.Add(new TextBlock { Text = "✛", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = size * 0.95, Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 210)),
                                                     VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) });
                    continue;
                }
                var tb = new TextBlock
                {
                    Text = gl.Text, Foreground = fg, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    FontSize = gl.Shape == "round" ? size * (symbol ? 0.6 : 0.55) : size * 0.4,
                    FontFamily = symbol ? new FontFamily("Segoe UI Symbol") : Fluent.TextFont
                };
                var b = new Border { Child = tb, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb(62, 62, 70)) };
                double big, mid, small;
                switch (gl.Shape)
                {
                    case "bumper":                                        // a wide, flat shoulder: its outer top corner rounder
                        b.Height = size * 0.72; b.MinWidth = size * 1.5; b.Padding = new Thickness(size * 0.25, 0, size * 0.25, 0);
                        big = b.Height * 0.5; small = b.Height * 0.2;
                        b.CornerRadius = gl.Left ? new CornerRadius(big, small, small, small) : new CornerRadius(small, big, small, small);
                        break;
                    case "trigger":                                       // taller, with a rounded top
                        b.Height = size; b.MinWidth = size * 1.15; b.Padding = new Thickness(size * 0.2, size * 0.08, size * 0.2, 0);
                        big = size * 0.5; mid = size * 0.3; small = size * 0.14;
                        b.CornerRadius = gl.Left ? new CornerRadius(big, mid, small, small) : new CornerRadius(mid, big, small, small);
                        break;
                    case "pill":                                          // Options, Share, Guide
                        b.Height = size * 0.72; b.Padding = new Thickness(size * 0.3, 0, size * 0.3, 0); b.CornerRadius = new CornerRadius(size * 0.36);
                        break;
                    default:                                              // face buttons, sticks, + and −
                        b.Height = size; b.Width = size; b.CornerRadius = new CornerRadius(size / 2);
                        break;
                }
                row.Children.Add(b);
            }
            return row;
        }

        static Border Chip(string text)
        {
            var chip = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 3, 10, 4), MinWidth = 28 };
            chip.SetResourceReference(Border.BackgroundProperty, "Tile");
            var t = Fluent.Label(text, 13, "Text", false);
            t.FontWeight = FontWeights.SemiBold;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            chip.Child = t;
            return chip;
        }

        static void BuildTabs()
        {
            tabs.Clear();
            tabStrip.Children.Clear();
            for (int i = 0; i < navLabels.Length; i++)
            {
                int idx = i;
                var hov = new Border { CornerRadius = new CornerRadius(6), Opacity = 0 };
                hov.SetResourceReference(Border.BackgroundProperty, "Hover");
                var label = Fluent.Label(navLabels[i], 15, "Sub", false);
                label.Margin = new Thickness(18, 9, 18, 10);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                var g = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(2, 0, 2, 0) };
                g.Children.Add(hov); g.Children.Add(label);
                var tab = new Border { Child = g, Tag = label };
                Anim.Hover(g, hov);
                g.MouseLeftButtonUp += (o, e) => GoToTab(idx);
                tabs.Add(tab);
                tabStrip.Children.Add(tab);
            }
        }

        static void GoToTab(int j)
        {
            if (navBtns == null || j < 0 || j >= navKeys.Length) return;
            if (searchBox != null && searchBox.Text.Length > 0) searchBox.Text = "";
            navBtns[j].IsChecked = true;
            if (page != navKeys[j]) Navigate(navKeys[j]);
        }

        /// <summary>Highlights the current page's tab and slides the accent underline under it.</summary>
        static void UpdateTabs(bool animate)
        {
            if (!padVisual || tabs.Count == 0 || tabBar == null) return;
            int sel = Array.IndexOf(navKeys, page);
            for (int i = 0; i < tabs.Count; i++)
            {
                var l = (TextBlock)tabs[i].Tag;
                l.SetResourceReference(TextBlock.ForegroundProperty, i == sel ? "Text" : "Sub");
                l.FontWeight = i == sel ? FontWeights.SemiBold : FontWeights.Normal;
            }
            if (sel < 0) { Anim.Fade(tabBar, 0, 120); return; }
            var t = tabs[sel];
            if (t.ActualWidth <= 0) { Dispatch(() => UpdateTabs(false), DispatcherPriority.Loaded); return; }
            Point p;
            try { p = t.TranslatePoint(new Point(0, 0), tabCanvas); } catch { return; }
            double bw = Math.Max(16, t.ActualWidth - 36), x = p.X + (t.ActualWidth - bw) / 2, y = p.Y + t.ActualHeight + 3;
            Canvas.SetTop(tabBar, y);
            if (!animate || !Anim.On || tabBar.Opacity < 0.5)
            {
                tabBar.BeginAnimation(Canvas.LeftProperty, null); tabBar.BeginAnimation(FrameworkElement.WidthProperty, null);
                Canvas.SetLeft(tabBar, x); tabBar.Width = bw;
                Anim.Fade(tabBar, 1, 150);
                return;
            }
            var dur = TimeSpan.FromMilliseconds(300);
            tabBar.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, dur) { EasingFunction = Anim.Quint });
            tabBar.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(bw, dur) { EasingFunction = Anim.Quint });
        }

        /// <summary>Called by the engine on every controller poll. Returns true when this window used the input.</summary>
        public static bool HandlePad(int b, int pressed)
        {
            if (w == null || App.S.PadUiMode == 0 || !w.IsVisible || !w.IsActive || w.WindowState == WindowState.Minimized) { padHeldDir = 0; return false; }
            if ((pressed & BtnGuide) != 0) return false;                     // the Guide/PS/Home button keeps its global job
            if (DialogOpen) { DialogPad(b, pressed); return true; }           // a question on screen always gets the controller
            // controller controls are on: the controller steers the PiP, even with this window in front
            if (App.I.PadOn) { padHeldDir = 0; return false; }
            // the on/off and start-PiP combos always reach the PiP engine
            foreach (var k in new[] { "pad", "startpip" })
            {
                int m;
                if (App.I.PadMask.TryGetValue(k, out m) && m != 0 && (b & m) == m && (pressed & m) != 0) { padHeldDir = 0; return false; }
            }
            if (pressed != 0 && !padVisual) SetPadVisual(true);
            else if (pressed != 0 && hintsKind != App.I.PadKind) BuildPadHints();

            int dir = (b & DirUp) != 0 ? 1 : (b & DirDown) != 0 ? 2 : (b & DirLeft) != 0 ? 3 : (b & DirRight) != 0 ? 4 : 0;
            var now = DateTime.UtcNow;
            bool oskOpen = OskOpen;
            Action<int> move = oskOpen ? (Action<int>)OskMove : PadMove;
            if (dir == 0) padHeldDir = 0;
            else if (dir != padHeldDir) { padHeldDir = dir; padNextRepeat = now.AddMilliseconds(420); move(dir); }
            else if (now >= padNextRepeat) { padNextRepeat = now.AddMilliseconds(95); move(dir); }

            if (oskOpen)
            {
                // on-screen keyboard: A types, X deletes, Y adds a space, B or Menu closes
                if ((pressed & BtnA) != 0) PadActivate();
                if ((pressed & BtnX) != 0) OskBackspace();
                if ((pressed & BtnY) != 0) OskType(" ");
                if ((pressed & (BtnB | BtnMenu)) != 0) CloseOsk();
                return true;
            }
            if ((pressed & BtnY) != 0) { PadSearch(); return true; }
            if ((pressed & BtnX) != 0 && ExplainPages.Contains(page ?? "")) { ToggleExplainers(); return true; }   // X: explanations on / off
            if ((pressed & BtnX) != 0 && page == "windows")
            {
                var it = SelItem();
                if (it != null && it.MayFreeze) { ShowFreezeDialog(it); return true; }      // X: the freeze fix for this window
            }

            if ((pressed & BtnA) != 0) PadActivate();
            if ((pressed & BtnB) != 0) PadBack();
            if ((pressed & BtnLB) != 0) PadPage(-1);
            if ((pressed & BtnRB) != 0) PadPage(1);
            if ((pressed & BtnMenu) != 0) { if (padVisual) ShowPageOptions(); else PadMenu(); }   // Menu: the buttons above the page
            if ((b & (RsUp | RsDown)) != 0) PadScroll((b & RsDown) != 0 ? 1 : -1);
            return true;
        }

        // ---------------------------------------------------------------- focus helpers
        static DependencyObject Focused() { return Keyboard.FocusedElement as DependencyObject; }

        static bool FocusIsInside()
        {
            var v = Focused() as Visual;
            return v != null && w != null && w.IsAncestorOf(v) && v != w;
        }

        static bool InNav(DependencyObject d) { var v = d as Visual; return v != null && navPane != null && navPane.IsAncestorOf(v); }

        static T Up<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T)) d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        static bool FocusItem(ListBox lb, int i)
        {
            if (lb == null || i < 0 || i >= lb.Items.Count) return false;
            lb.SelectedIndex = i;
            lb.ScrollIntoView(lb.Items[i]);
            var c = lb.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
            if (c == null) { lb.UpdateLayout(); c = lb.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem; }
            return c != null && c.Focus();
        }

        static bool InDetails(DependencyObject d)
        {
            var v = d as Visual;
            return v != null && detailsCard != null && detailsCard.IsAncestorOf(v);
        }

        /// <summary>Focus the details panel's main button (Open in picture-in-picture).</summary>
        static bool FocusDetails()
        {
            if (detailsCard == null || detailsCard.Visibility != Visibility.Visible || page != "windows") return false;
            var open = w.FindName("OpenBtn") as UIElement;
            if (open != null && open.IsVisible && open.IsEnabled && open.Focus()) return true;
            return detailsCard.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)) && InDetails(Focused());
        }

        static List<UIElement> CmdButtons()
        {
            var l = new List<UIElement>();
            if (cmdBar == null) return l;
            foreach (UIElement c in cmdBar.Children) if (c.IsEnabled && c.IsVisible && c.Focusable) l.Add(c);
            return l;
        }

        static bool FocusCmdBar(int index)
        {
            var btns = CmdButtons();
            if (btns.Count == 0) return false;
            return btns[Math.Max(0, Math.Min(btns.Count - 1, index))].Focus();
        }

        static void FocusNav()
        {
            if (navBtns == null || padVisual) return;                         // controller mode has tabs, not a focusable menu
            int i = Array.IndexOf(navKeys, page);
            navBtns[Math.Max(0, i)].Focus();
        }

        static void FocusContent()
        {
            if (page == "windows")
            {
                if (autoList.SelectedItem != null) FocusItem(autoList, 0);
                else if (!FocusItem(winList, Math.Max(0, winList.SelectedIndex))) FocusItem(autoList, 0);
            }
            else if (page == "running")
            {
                if (!FocusItem(runList, Math.Max(0, runList.SelectedIndex))) FocusNav();
            }
            else
            {
                StackPanel pg;
                if (pages.TryGetValue(page, out pg) && pg.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)) && FocusIsInside())
                {
                    SmoothScroll.Jump(pageScroll, 0);
                    return;
                }
                FocusNav();
            }
        }

        static void FocusDefault()
        {
            if (w == null) return;
            FocusContent();
            if (!FocusIsInside()) FocusNav();
            UpdateRing(false);
        }

        // ---------------------------------------------------------------- actions
        static void PadMove(int dir)
        {
            var d = Focused();

            // an open drop-down: up/down picks, left/right do nothing
            var cbi = d as ComboBoxItem;
            var openCombo = cbi != null ? ItemsControl.ItemsControlFromItemContainer(cbi) as ComboBox : d as ComboBox;
            if (openCombo != null && openCombo.IsDropDownOpen)
            {
                if (dir == 1 || dir == 2)
                {
                    int j = Math.Max(0, Math.Min(openCombo.Items.Count - 1, openCombo.SelectedIndex + (dir == 2 ? 1 : -1)));
                    openCombo.SelectedIndex = j;
                    var c = openCombo.ItemContainerGenerator.ContainerFromIndex(j) as ComboBoxItem;
                    if (c != null) c.Focus();
                }
                return;
            }

            if (!FocusIsInside()) { FocusDefault(); return; }

            // sliders: left/right change the value
            var sl = d as Slider ?? Up<Slider>(d);
            if (sl != null && (dir == 3 || dir == 4))
            {
                double step = Math.Max(sl.SmallChange, (sl.Maximum - sl.Minimum) / 20);
                sl.Value = Math.Max(sl.Minimum, Math.Min(sl.Maximum, sl.Value + (dir == 4 ? step : -step)));
                return;
            }

            // the details panel on the right (Open in PiP, the freeze fix): up/down inside it, left back to the list
            if (InDetails(d))
            {
                if (dir == 3) { FocusContent(); return; }
                if (dir == 4) return;
                var cur = d as UIElement;
                if (cur != null && cur.MoveFocus(new TraversalRequest(dir == 1 ? FocusNavigationDirection.Up : FocusNavigationDirection.Down)) && !InDetails(Focused()))
                    cur.Focus();                                             // don't wander out of the panel
                return;
            }

            // tables: up/down walk the rows (and between Auto and the table)
            var lbi = d as ListBoxItem ?? Up<ListBoxItem>(d);
            if (lbi != null && dir == 4)
            {
                var owner = ItemsControl.ItemsControlFromItemContainer(lbi) as ListBox;
                if ((owner == winList || owner == autoList) && FocusDetails()) return;   // right: into the details panel
                var row = owner == winList ? owner.ItemContainerGenerator.ItemFromContainer(lbi) as WinRowItem : null;
                if (row != null && row.MayFreeze) { owner.SelectedItem = row; ShowFreezeDialog(row); return; }   // narrow: the warning's dialog
            }
            if (lbi != null && (dir == 1 || dir == 2))
            {
                var lb = ItemsControl.ItemsControlFromItemContainer(lbi) as ListBox;
                if (lb != null)
                {
                    int i = lb.ItemContainerGenerator.IndexFromContainer(lbi), j = i + (dir == 2 ? 1 : -1);
                    if (FocusItem(lb, j)) return;
                    if (lb == winList && j < 0 && FocusItem(autoList, 0)) return;
                    if (lb == autoList && j > 0 && FocusItem(winList, 0)) return;
                    if (j < 0 && lb == autoList && winSearch != null && winSearch.IsVisible) { winSearch.Focus(); return; }   // up to the window search
                    if (j < 0) FocusCmdBar(0);                                 // above the top row: the page's commands
                    return;                                                   // bottom of the table: stay put
                }
            }

            // the window search box: up to the commands, down into the list
            if (d == winSearch && winSearch != null)
            {
                if (dir == 1) FocusCmdBar(0);
                else if (dir == 2 && !FocusItem(autoList, 0)) FocusItem(winList, 0);
                else if (dir == 4) FocusDetails();
                return;
            }

            // the command bar at the top of the page (Open in PiP, Click a window, ...)
            var inBar = cmdBar != null && Up<WrapPanel>(d) == cmdBar;
            if (inBar)
            {
                var btns = CmdButtons();
                int i = btns.IndexOf(d as UIElement ?? Up<ButtonBase>(d));
                if (dir == 3 && i > 0) btns[i - 1].Focus();
                else if (dir == 4 && i >= 0 && i < btns.Count - 1) btns[i + 1].Focus();
                else if (dir == 2) { if (page == "windows" && winSearch != null && winSearch.IsVisible) winSearch.Focus(); else FocusContent(); }
                return;
            }

            // everything else: WPF's spatial navigation
            var el = d as UIElement;
            var before = d;
            var req = new TraversalRequest(dir == 1 ? FocusNavigationDirection.Up : dir == 2 ? FocusNavigationDirection.Down : dir == 3 ? FocusNavigationDirection.Left : FocusNavigationDirection.Right);
            bool moved = el != null && el.MoveFocus(req) && FocusIsInside() && Focused() != before;
            if (!moved)
            {
                if (dir == 1 && !InNav(d)) FocusCmdBar(0);                   // top of the page: up to the commands
                else if (dir == 3 && !InNav(d)) FocusNav();                        // left edge of the page: into the menu
                else if (dir == 4 && InNav(d)) FocusContent();               // right out of the menu: into the page
            }
            else if (InNav(d) && !InNav(Focused()) && dir == 4) FocusContent();
        }

        static void PadActivate()
        {
            var d = Focused();
            var cbi = d as ComboBoxItem;
            if (cbi != null)
            {
                var cb = ItemsControl.ItemsControlFromItemContainer(cbi) as ComboBox;
                if (cb != null) { cb.SelectedItem = cb.ItemContainerGenerator.ItemFromContainer(cbi); cb.IsDropDownOpen = false; cb.Focus(); }
                return;
            }
            if (!FocusIsInside()) { FocusDefault(); return; }

            var ue = d as UIElement;
            Action act;
            if (ue != null && activators.TryGetValue(ue, out act)) { act(); return; }

            var combo = d as ComboBox ?? Up<ComboBox>(d);
            if (combo != null) { combo.IsDropDownOpen = !combo.IsDropDownOpen; return; }

            var lbi = d as ListBoxItem ?? Up<ListBoxItem>(d);
            if (lbi != null)
            {
                var lb = ItemsControl.ItemsControlFromItemContainer(lbi) as ListBox;
                if (lb != null) lb.SelectedItem = lb.ItemContainerGenerator.ItemFromContainer(lbi);
                if (lb == winList || lb == autoList) OpenSelected();
                else if (lb == runList) { var p = SelectedPip(); if (p != null) p.BringSourceToFront(); }
                return;
            }

            var btn = d as ButtonBase ?? Up<ButtonBase>(d);
            if (btn != null && btn.IsEnabled && onClick != null)
            {
                try { onClick.Invoke(btn, null); } catch { }                // toggles toggle, buttons click, nav items navigate
                if (btn is RadioButton && InNav(btn)) DelayRun(Anim.On ? 200 : 20, FocusContent);
                return;
            }
            var tb = d as TextBox;
            if (tb != null) { OpenOsk(tb, tb == winSearch ? "Search windows" : "Type"); return; }
        }

        static void PadBack()
        {
            var d = Focused();
            var cbi = d as ComboBoxItem;
            var cb = cbi != null ? ItemsControl.ItemsControlFromItemContainer(cbi) as ComboBox : d as ComboBox;
            if (cb != null && cb.IsDropDownOpen) { cb.IsDropDownOpen = false; cb.Focus(); return; }
            if (overlayOpen) { overlayOpen = false; ApplyLayout(true); FocusContent(); return; }
            if (searchBox != null && searchBox.Text.Length > 0) { searchBox.Text = ""; FocusContent(); return; }
            if (padVisual)
            {
                if (page != "windows") { GoToTab(0); DelayRun(Anim.On ? 200 : 20, FocusContent); }
                return;
            }
            if (!InNav(d)) { FocusNav(); return; }
            if (page != "windows") { Navigate("windows"); DelayRun(Anim.On ? 200 : 20, FocusNav); }
        }

        static void PadPage(int delta)
        {
            if (navBtns == null) return;
            int i = Array.IndexOf(navKeys, page);
            if (i < 0) i = 0;
            int j = ((i + delta) % navKeys.Length + navKeys.Length) % navKeys.Length;
            bool inNav = InNav(Focused());
            GoToTab(j);
            DelayRun(Anim.On ? 200 : 20, () => { if (inNav) FocusNav(); else FocusContent(); });
        }

        static void PadMenu()
        {
            if (burger == null) return;
            if (onClick != null) try { onClick.Invoke(burger, null); } catch { }
            DelayRun(Anim.On ? 120 : 10, () => { if (overlayOpen || !compact) FocusNav(); else FocusContent(); });
        }

        static void PadScroll(int dir)
        {
            ScrollViewer sv = null;
            if (page == "windows") sv = FindScroll(winList);
            else if (page == "running") sv = FindScroll(runList);
            else sv = pageScroll;
            if (sv != null) SmoothScroll.By(sv, dir * 28);
        }

        static ScrollViewer FindScroll(DependencyObject d)
        {
            if (d == null) return null;
            var sv = d as ScrollViewer;
            if (sv != null) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) { var r = FindScroll(VisualTreeHelper.GetChild(d, i)); if (r != null) return r; }
            return null;
        }

        // ---------------------------------------------------------------- questions (instead of Windows message boxes)
        static Grid dlgHost;
        static Action<bool> dlgDone;
        static UIElement dlgYes, dlgNo, dlgExtra;

        static bool DialogOpen { get { return dlgHost != null && dlgHost.Visibility == Visibility.Visible; } }

        /// <summary>
        /// Asks a question inside PiP Studio's window. Works with mouse, keyboard (Enter / Esc) and controller (A / B, left / right).
        /// Pass no = null for a message with just one button. done(true) means the first button.
        /// </summary>
        public static void Ask(string title, string text, string yes, string no, Action<bool> done, bool warn = false, FrameworkElement extra = null, UIElement extraFocus = null)
        {
            Open(null);
            if (w == null || padLayer == null) { if (done != null) done(false); return; }
            var root = padLayer.Parent as Grid;
            if (root == null) { if (done != null) done(false); return; }
            if (dlgHost == null)
            {
                dlgHost = new Grid { Visibility = Visibility.Collapsed };
                dlgHost.SetResourceReference(Panel.BackgroundProperty, "Smoke");
                Grid.SetRowSpan(dlgHost, 3);
                Panel.SetZIndex(dlgHost, 45);                             // over the page and keyboard, under the focus ring
                dlgHost.PreviewKeyDown += (o, e) =>
                {
                    if (e.Key == Key.Escape) { CloseDialog(false); e.Handled = true; }
                };
                root.Children.Add(dlgHost);
            }
            if (DialogOpen) CloseDialog(false);                           // one question at a time
            dlgDone = done;

            var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), MaxWidth = 480, MinWidth = 340,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            card.SetResourceReference(Border.BackgroundProperty, "PaneOverlay");
            card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            if (!App.S.LiteMode) card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 12, Direction = 270, Opacity = 0.4, Color = Colors.Black };
            var sp = new StackPanel();
            var body = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            if (warn)
            {
                var g = Fluent.Glyph("\uE7BA", 18, "Attention");
                g.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                g.Margin = new Thickness(0, 0, 10, 0);
                head.Children.Add(g);
            }
            var t = Fluent.Label(title, 20, "Text", false);
            t.FontWeight = FontWeights.SemiBold;
            head.Children.Add(t);
            body.Children.Add(head);
            var msg = Fluent.Label(text, 14, "Sub", true);
            msg.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(msg);
            if (extra != null) body.Children.Add(extra);
            dlgExtra = extraFocus;
            sp.Children.Add(body);

            var bar = new Border { Padding = new Thickness(24, 18, 24, 22), CornerRadius = new CornerRadius(0, 0, 8, 8) };
            bar.SetResourceReference(Border.BackgroundProperty, "CardSecondary");
            var btns = new UniformGrid { Rows = 1, Columns = no == null ? 1 : 2, HorizontalAlignment = HorizontalAlignment.Stretch };
            var yb = new Wpf.Ui.Controls.Button { Content = yes, Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, no == null ? 0 : 4, 0), Height = 34, IsDefault = true };
            yb.Click += (o, e) => CloseDialog(true);
            btns.Children.Add(yb);
            dlgYes = yb; dlgNo = null;
            if (no != null)
            {
                var nb = new Wpf.Ui.Controls.Button { Content = no, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 0, 0, 0), Height = 34, IsCancel = true };
                nb.Click += (o, e) => CloseDialog(false);
                btns.Children.Add(nb);
                dlgNo = nb;
            }
            bar.Child = btns;
            sp.Children.Add(bar);
            card.Child = sp;

            dlgHost.Children.Clear();
            dlgHost.Children.Add(card);
            dlgHost.Visibility = Visibility.Visible;
            HideThumb();
            Anim.Fade(dlgHost, 1, 150);
            Anim.Enter(card, 16, 0, 260);
            // a risky question starts on the safe answer
            var first = warn && dlgNo != null ? dlgNo : dlgYes;
            Dispatch(() => (dlgList != null && extraFocus != null ? extraFocus : first).Focus(), DispatcherPriority.Loaded);   // a menu starts on its first entry
        }

        static void CloseDialog(bool yes)
        {
            if (dlgHost == null) return;
            dlgHost.Visibility = Visibility.Collapsed;
            dlgHost.Children.Clear();
            var cb = dlgDone;
            dlgDone = null;
            dlgExtra = null;
            dlgList = null;
            DelayRun(60, UpdatePreview);
            if (padVisual) Dispatch(FocusContent, DispatcherPriority.Loaded);
            if (cb != null) cb(yes);
        }

        static List<ButtonBase> dlgList;                    // the options menu: its entries, top to bottom

        /// <summary>
        /// The controller's Menu button: the buttons above the page (Open in PiP, Click a window, Screen region...) in a menu,
        /// since the D-pad moves through the list and can't reach them. Up and down choose, A picks, B or Menu closes.
        /// </summary>
        static void ShowPageOptions()
        {
            if (cmdBar == null) return;
            var cmds = cmdBar.Children.OfType<ButtonBase>().Where(x => x.Tag is PageCommand && x.Visibility == Visibility.Visible).ToList();
            if (cmds.Count == 0) return;
            var list = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            var entries = new List<ButtonBase>();
            foreach (var c in cmds)
            {
                var pc = (PageCommand)c.Tag;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = pc.Glyph, FontFamily = Fluent.IconFont, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
                sp.Children.Add(new TextBlock { Text = L.T(pc.Text), FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
                var e = new Wpf.Ui.Controls.Button
                {
                    Content = sp, Height = 46, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 0, 4), IsEnabled = c.IsEnabled,
                    Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary
                };
                var run = pc.Run;
                e.Click += (o, a) => { CloseDialog(false); DelayRun(80, () => { try { run(); } catch (Exception ex) { App.ReportError(ex); } }); };
                list.Children.Add(e);
                entries.Add(e);
            }
            var first = entries.FirstOrDefault(x => x.IsEnabled);
            Ask(L.T("Options") + " · " + PageTitleText(), "", "Close", null, null, false, list, first);
            dlgList = entries;
        }

        static string PageTitleText()
        {
            int i = Array.IndexOf(navKeys, page ?? "");
            return i >= 0 ? L.T(navLabels[i]) : "";
        }

        static void DialogPad(int b, int pressed)
        {
            if (dlgList != null)
            {
                // the options menu: up and down through the entries (and the Close button below them)
                var cur = Focused() as UIElement;
                var stops = dlgList.Where(x => x.IsEnabled).Cast<UIElement>().ToList();
                stops.Add(dlgYes);
                int i = stops.IndexOf(cur);
                if ((pressed & DirUp) != 0) stops[i <= 0 ? stops.Count - 1 : i - 1].Focus();
                else if ((pressed & DirDown) != 0) stops[i < 0 || i >= stops.Count - 1 ? 0 : i + 1].Focus();
                if ((pressed & BtnA) != 0)
                {
                    var bb = Focused() as ButtonBase;
                    if (bb != null && dlgList.Contains(bb) && onClick != null) { try { onClick.Invoke(bb, null); } catch { } }
                    else CloseDialog(false);
                }
                else if ((pressed & (BtnB | BtnMenu)) != 0) CloseDialog(false);
                if (!padVisual && pressed != 0) SetPadVisual(true);
                return;
            }
            var f = Focused();
            bool onExtra = dlgExtra != null && f == dlgExtra;
            if ((pressed & DirUp) != 0 && dlgExtra != null && dlgExtra.IsEnabled) dlgExtra.Focus();          // up: the option above the buttons
            else if ((pressed & DirDown) != 0 && onExtra) dlgYes.Focus();
            else if ((pressed & DirLeft) != 0 && !onExtra) dlgYes.Focus();
            else if ((pressed & DirRight) != 0 && !onExtra && dlgNo != null) dlgNo.Focus();
            if ((pressed & BtnA) != 0)
            {
                var bb = Focused() as ButtonBase;
                if (onExtra && bb != null && onClick != null) { try { onClick.Invoke(bb, null); } catch { } }   // flip the switch, keep the dialog open
                else CloseDialog(Focused() == dlgYes);                                  // A answers with the focused button
            }
            else if ((pressed & BtnB) != 0) CloseDialog(false);
            if (!padVisual && pressed != 0) SetPadVisual(true);
        }

        // ---------------------------------------------------------------- search + on-screen keyboard
        static Border osk;
        static TextBlock oskText, oskTitle;
        static TextBox oskTarget;
        static StackPanel oskRows;

        static bool OskOpen { get { return osk != null && osk.Visibility == Visibility.Visible; } }

        /// <summary>Y: search. The window search on the Windows page, the settings search everywhere else.</summary>
        static void PadSearch()
        {
            if (page == "windows" && winSearch != null) { winSearch.Focus(); OpenOsk(winSearch, "Search windows"); }
            else if (searchBox != null) OpenOsk(searchBox, "Find a setting");
        }

        static void BuildOsk()
        {
            var root = padLayer.Parent as Grid;
            if (root == null) return;
            osk = new Border
            {
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 14),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20),
                Visibility = Visibility.Collapsed
            };
            osk.SetResourceReference(Border.BackgroundProperty, "PaneOverlay");
            osk.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            if (!App.S.LiteMode) osk.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.35, Color = Colors.Black };
            Grid.SetRow(osk, 1);
            Panel.SetZIndex(osk, 40);                                    // under the focus ring (50), over the page
            var sp = new StackPanel();
            oskTitle = Fluent.Label("Search", 12, "Sub", false);
            sp.Children.Add(oskTitle);
            var field = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 7, 12, 8), Margin = new Thickness(0, 6, 0, 12), MinWidth = 560 };
            field.SetResourceReference(Border.BackgroundProperty, "Tile");
            field.SetResourceReference(Border.BorderBrushProperty, "Accent");
            field.BorderThickness = new Thickness(0, 0, 0, 2);
            oskText = Fluent.Label("", 16, "Text", false);
            field.Child = oskText;
            sp.Children.Add(field);

            oskRows = new StackPanel();
            KeyboardNavigation.SetDirectionalNavigation(oskRows, KeyboardNavigationMode.Contained);
            foreach (var row in new[] { "1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm,.-" })
            {
                var r = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (char c in row)
                {
                    string ch = c.ToString();
                    r.Children.Add(OskKey(ch, 50, () => OskType(ch)));
                }
                oskRows.Children.Add(r);
            }
            var last = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            last.Children.Add(OskKey("Space", 230, () => OskType(" ")));
            last.Children.Add(OskKey("\uE750", 74, OskBackspace, true));
            last.Children.Add(OskKey("Clear", 84, () => { if (oskTarget != null) { oskTarget.Text = ""; SyncOsk(); } }));
            var done = OskKey("Done", 100, CloseOsk);
            done.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            last.Children.Add(done);
            oskRows.Children.Add(last);
            sp.Children.Add(oskRows);
            var help = Fluent.Label(Pad.Name(BtnX, App.I.PadKind) + " delete   ·   " + Pad.Name(BtnY, App.I.PadKind) + " space   ·   " + Pad.Name(BtnB, App.I.PadKind) + " done", 12, "Tertiary", false);
            help.HorizontalAlignment = HorizontalAlignment.Center;
            help.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(help);
            osk.Child = sp;
            root.Children.Add(osk);
        }

        static Wpf.Ui.Controls.Button OskKey(string text, double width, Action act, bool glyph = false)
        {
            var b = new Wpf.Ui.Controls.Button { Width = width, Height = 42, Margin = new Thickness(3), Padding = new Thickness(0), FocusVisualStyle = null };
            var t = new TextBlock { Text = text, FontSize = glyph ? 16 : 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (glyph) t.FontFamily = Fluent.IconFont;
            b.Content = t;
            b.Click += (o, e) => act();
            return b;
        }

        static void OpenOsk(TextBox target, string title)
        {
            if (osk == null) BuildOsk();
            if (osk == null || target == null) return;
            oskTarget = target;
            oskTitle.Text = title;
            SyncOsk();
            osk.Visibility = Visibility.Visible;
            Anim.Enter(osk, 24, 0, 260);
            // start on "q", like a console keyboard
            var first = ((StackPanel)oskRows.Children[1]).Children[0] as UIElement;
            Dispatch(() => { if (first != null) first.Focus(); }, DispatcherPriority.Loaded);
        }

        static void SyncOsk()
        {
            if (oskText == null || oskTarget == null) return;
            oskText.Text = oskTarget.Text + "▏";
        }

        static void OskType(string s)
        {
            if (oskTarget == null) return;
            oskTarget.Text += s;
            oskTarget.CaretIndex = oskTarget.Text.Length;
            SyncOsk();
        }

        static void OskBackspace()
        {
            if (oskTarget == null || oskTarget.Text.Length == 0) return;
            oskTarget.Text = oskTarget.Text.Substring(0, oskTarget.Text.Length - 1);
            SyncOsk();
        }

        static void CloseOsk()
        {
            if (osk == null) return;
            var target = oskTarget;
            osk.Visibility = Visibility.Collapsed;
            oskTarget = null;
            // jump to the results: the filtered window list, or the settings results page
            if (target == winSearch) { if (!FocusItem(winList, 0)) FocusItem(autoList, 0); }
            else if (target == searchBox && page == "search") FocusContent();
            else if (target != null && target.IsVisible) target.Focus();
            else FocusContent();
        }

        static void OskMove(int dir)
        {
            var el = Focused() as UIElement;
            if (el == null || !(el is Visual) || !osk.IsAncestorOf((Visual)el)) { var first = ((StackPanel)oskRows.Children[1]).Children[0] as UIElement; if (first != null) first.Focus(); return; }
            el.MoveFocus(new TraversalRequest(dir == 1 ? FocusNavigationDirection.Up : dir == 2 ? FocusNavigationDirection.Down : dir == 3 ? FocusNavigationDirection.Left : FocusNavigationDirection.Right));
        }

        // ---------------------------------------------------------------- focus ring
        static void UpdateRing(bool animate)
        {
            if (padRing == null || padLayer == null) return;
            if (!(padVisual || kbNav) || w == null || !w.IsActive) { if (padRing.Opacity > 0) Anim.Fade(padRing, 0, 120); ringTarget = Rect.Empty; return; }
            var fe = Focused() as FrameworkElement;
            if (fe == null || !FocusIsInside() || !fe.IsVisible || fe.ActualWidth < 1)
            {
                if (padRing.Opacity > 0) Anim.Fade(padRing, 0, 120);
                ringTarget = Rect.Empty;
                return;
            }
            Rect r;
            try { r = fe.TransformToVisual(padLayer).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight)); }
            catch { return; }
            r.Inflate(3, 3);
            // keep the ring inside the visible part of a scrolling page
            var sv = Up<ScrollViewer>(fe);
            if (sv != null)
            {
                try
                {
                    Rect view = sv.TransformToVisual(padLayer).TransformBounds(new Rect(0, 0, sv.ActualWidth, sv.ActualHeight));
                    var clipped = Rect.Intersect(r, view);
                    if (clipped.IsEmpty || clipped.Height < 6) { if (padRing.Opacity > 0) Anim.Fade(padRing, 0, 100); ringTarget = Rect.Empty; return; }
                    r = clipped;
                }
                catch { }
            }
            bool appearing = ringTarget.IsEmpty || padRing.Opacity < 0.5;
            if (!ringTarget.IsEmpty && Math.Abs(r.X - ringTarget.X) < 0.5 && Math.Abs(r.Y - ringTarget.Y) < 0.5
                && Math.Abs(r.Width - ringTarget.Width) < 0.5 && Math.Abs(r.Height - ringTarget.Height) < 0.5) return;
            ringTarget = r;
            if (!animate || appearing || !Anim.On)
            {
                padRing.BeginAnimation(Canvas.LeftProperty, null); padRing.BeginAnimation(Canvas.TopProperty, null);
                padRing.BeginAnimation(FrameworkElement.WidthProperty, null); padRing.BeginAnimation(FrameworkElement.HeightProperty, null);
                Canvas.SetLeft(padRing, r.X); Canvas.SetTop(padRing, r.Y); padRing.Width = r.Width; padRing.Height = r.Height;
                if (padRing.Opacity < 1) Anim.Fade(padRing, 1, 150);
                return;
            }
            var dur = TimeSpan.FromMilliseconds(180);
            padRing.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(r.X, dur) { EasingFunction = Anim.Quint });
            padRing.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(r.Y, dur) { EasingFunction = Anim.Quint });
            padRing.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(r.Width, dur) { EasingFunction = Anim.Quint });
            padRing.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(r.Height, dur) { EasingFunction = Anim.Quint });
            if (padRing.Opacity < 1) Anim.Fade(padRing, 1, 120);
        }
    }
}
