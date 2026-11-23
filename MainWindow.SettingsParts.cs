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
    // Building blocks for the settings pages (cards, expanders, rows, switches, lists), and the sections that build themselves: title bar, phone, layouts, updates, settings in a file. Part of MainUI (MainWindow.cs).
    static partial class MainUI
    {
        // ----------------------------------------------------------------------------- Settings building blocks
        //  SettingsCard: 4 px radius, 68 min height, 16 px padding, 20 px icon then 16 px gap, body + caption text.
        //  SettingsExpander: header card with a chevron; rows below on a secondary fill, separated by hairlines.
        static StackPanel NewPage(string key)
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };
            settingsHost.Children.Add(sp);
            pages[key] = sp;
            buildingPage = key;
            buildingReveal = null;
            return sp;
        }

        static void Section(StackPanel p, string text)
        {
            var t = Fluent.Label(text, 14, "Text");
            t.FontWeight = FontWeights.SemiBold;
            t.Margin = new Thickness(1, p.Children.Count == 0 ? 2 : 30, 0, 8);
            p.Children.Add(t);
        }

        static void Note(StackPanel p, string text)
        {
            var t = Fluent.Label(text, 12, "Sub");
            t.Margin = new Thickness(1, 0, 0, 12);
            p.Children.Add(t);
        }

        static void Index(string title, string text, FrameworkElement el, Action reveal = null)
        {
            AttachExplainer(title, el);
            searchIndex.Add(new SearchEntry { Title = title, Text = text ?? "", Page = buildingPage, Element = el, Reveal = reveal });
        }

        /// <summary>The icon / title / description / control layout every card and row uses.</summary>
        static string lastDesc;      // the full description of the setting being built (shown under its explainer)

        /// <summary>Short descriptions shown next to settings; the full text moves to the explanations panel.</summary>
        static readonly Dictionary<string, string> ShortDesc = new Dictionary<string, string>
        {
            { "Border", "An outline around each PiP" },
            { "Messages under the PiP", "Show messages below the PiP, not on it" },
            { "When the pointer is over a PiP", "What a PiP does when you point at it" },
            { "Show move arrows", "Arrow buttons that hop to the next spot" },
            { "Fade-out opacity", "How see-through it gets" },
            { "Click-through opacity", "How see-through ghost mode is" },
            { "Lite mode", "Fewer effects, for slower PCs" },
            { "Animation effects", "Glide and fade effects" },
            { "Size limits", "Stop tall or wide apps getting too big" },
            { "Control other apps' PiP windows", "Steer browser and app PiPs too" },
            { "Snap other apps' PiPs to spots", "They land on the same spots as yours" },
            { "Allow more than one PiP at a time", "Off: a new PiP replaces the open one" },
            { "Follow the window I just left", "The PiP shows the window you just left" },
            { "Middle of each edge too", "8 spots instead of 4 corners" },
            { "Stay on one display", "Don't hop to the next display" },
            { "Hide PiPs while a fullscreen app is on the same display", "Includes borderless games" },
            { "Keep minimized sources playing", "The PiP keeps moving when you minimize" },
            { "Controller-friendly mode", "Bigger layout and button hints" },
            { "Fade controller hints when idle", "Hints fade after a few seconds" },
            { "Release the game's focus while controls are on", "The game ignores the controller meanwhile" },
            { "Button assignments", "Click, then press a button. Right-click clears it." },
            { "Hide PiPs from screenshots, recordings and screen sharing", "Costs a little performance" },
            { "Release memory while idle", "Give memory back while in the tray" },
            { "Picture-in-picture for any window or part of the screen", "Uses Windows' own live thumbnails" },
            { "Running as administrator", "Can show admin apps' windows" },
            { "Restart as administrator", "Only needed for admin apps' windows" },
        };

        static Grid CardContent(string glyph, string title, string desc, FrameworkElement control, out TextBlock titleBlock, out TextBlock descBlock)
        {
            lastDesc = desc;
            string sd;
            if (desc != null && title != null && ShortDesc.TryGetValue(title, out sd)) desc = sd;   // the long text goes under the animation
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(glyph == null ? 0 : 36) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (glyph != null) g.Children.Add(Fluent.Glyph(glyph, 20));
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            titleBlock = Fluent.Label(title, 14);
            sp.Children.Add(titleBlock);
            descBlock = null;
            if (!string.IsNullOrEmpty(desc))
            {
                descBlock = Fluent.Label(desc, 12, "Sub");
                descBlock.Margin = new Thickness(0, 1, 0, 0);
                if (lastDesc != null && lastDesc != desc) descBlock.ToolTip = lastDesc;   // the full text, also when explanations are hidden
                sp.Children.Add(descBlock);
            }
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            if (control != null)
            {
                control.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(control, 2);
                g.Children.Add(control);
            }
            return g;
        }

        static CardParts Card(StackPanel p, string glyph, string title, string desc, FrameworkElement control, string keywords = "")
        {
            TextBlock tb, db;
            var content = CardContent(glyph, title, desc, control, out tb, out db);
            var b = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), MinHeight = 68, Margin = new Thickness(0, 0, 0, 4), Child = content };
            b.SetResourceReference(Border.BackgroundProperty, "Card");
            b.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            p.Children.Add(b);
            Index(title, (desc ?? "") + " " + keywords + (L.Active ? " " + L.T(title) + " " + L.T(desc ?? "") : ""), b);
            return new CardParts { Root = b, Title = tb, Desc = db };
        }

        /// <summary>A whole-card button (search results, links): fading hover and press like SettingsCard with IsClickEnabled.</summary>
        static Grid ClickRow(StackPanel p, string glyph, string title, string desc, Action click)
        {
            var root = new Grid { Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, MinHeight = 68 };
            var bg = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
            bg.SetResourceReference(Border.BackgroundProperty, "Card");
            bg.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            var hov = new Border { CornerRadius = new CornerRadius(4) };
            hov.SetResourceReference(Border.BackgroundProperty, "Hover");
            var pr = new Border { CornerRadius = new CornerRadius(4) };
            pr.SetResourceReference(Border.BackgroundProperty, "Pressed");
            TextBlock tb, db;
            var chev = Fluent.Glyph("", 12, "Sub");
            var content = CardContent(glyph, title, desc, chev, out tb, out db);
            content.Margin = new Thickness(16, 12, 16, 12);
            root.Children.Add(bg); root.Children.Add(hov); root.Children.Add(pr); root.Children.Add(content);
            Anim.Hover(root, hov, pr);
            root.MouseLeftButtonUp += (o, e) => click();
            MakeActivatable(root, click);
            p.Children.Add(root);
            return root;
        }

        class Expander
        {
            public StackPanel Rows;
            public Action Open;
            public CardParts Header;
        }

        static Expander MakeExpander(StackPanel p, string glyph, string title, string desc, FrameworkElement headerControl, bool startOpen = false, string keywords = "")
        {
            var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            var head = new Grid { Cursor = Cursors.Hand, MinHeight = 68 };
            var headBg = new Border { CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
            headBg.SetResourceReference(Border.BackgroundProperty, "Card");
            headBg.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            var hov = new Border { CornerRadius = new CornerRadius(4) };
            hov.SetResourceReference(Border.BackgroundProperty, "Hover");
            var pr = new Border { CornerRadius = new CornerRadius(4) };
            pr.SetResourceReference(Border.BackgroundProperty, "Pressed");

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (headerControl != null) { headerControl.VerticalAlignment = VerticalAlignment.Center; right.Children.Add(headerControl); }
            var chevHost = new Grid { Width = 32, Height = 32, Margin = new Thickness(headerControl != null ? 12 : 0, 0, -6, 0) };
            var chev = Fluent.Glyph("", 12, "Text");
            chev.HorizontalAlignment = HorizontalAlignment.Center;
            var rot = new RotateTransform(0);
            chev.RenderTransform = rot;
            chev.RenderTransformOrigin = new Point(0.5, 0.5);
            chevHost.Children.Add(chev);
            right.Children.Add(chevHost);

            TextBlock tb, db;
            var content = CardContent(glyph, title, desc, right, out tb, out db);
            content.Margin = new Thickness(16, 12, 16, 12);
            head.Children.Add(headBg); head.Children.Add(hov); head.Children.Add(pr); head.Children.Add(content);
            Anim.Hover(head, hov, pr);

            var rows = new StackPanel();
            var rowsHost = new Border { BorderThickness = new Thickness(1, 0, 1, 1), CornerRadius = new CornerRadius(0, 0, 4, 4), Visibility = Visibility.Collapsed, ClipToBounds = true, Child = rows };
            rowsHost.SetResourceReference(Border.BackgroundProperty, "CardSecondary");
            rowsHost.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            outer.Children.Add(head);
            outer.Children.Add(rowsHost);
            p.Children.Add(outer);

            bool open = false;
            Action<bool, bool> set = null;
            set = (wantOpen, animate) =>
            {
                if (wantOpen == open) return;
                open = wantOpen;
                var r0 = new CornerRadius(4, 4, wantOpen ? 0 : 4, wantOpen ? 0 : 4);
                headBg.CornerRadius = hov.CornerRadius = pr.CornerRadius = r0;
                bool anim = animate && Anim.On;
                if (anim) rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(wantOpen ? 180 : 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = Anim.Smooth });
                else { rot.BeginAnimation(RotateTransform.AngleProperty, null); rot.Angle = wantOpen ? 180 : 0; }
                if (wantOpen)
                {
                    rowsHost.Visibility = Visibility.Visible;
                    if (!anim) { rowsHost.BeginAnimation(FrameworkElement.HeightProperty, null); rowsHost.Height = double.NaN; return; }
                    double width = outer.ActualWidth > 0 ? outer.ActualWidth : 800;
                    rowsHost.Height = double.NaN;
                    rowsHost.Measure(new Size(width, double.PositiveInfinity));
                    double h = rowsHost.DesiredSize.Height;
                    var a = new DoubleAnimation(0, h, TimeSpan.FromMilliseconds(Math.Min(420, 200 + h / 6))) { EasingFunction = Anim.Quint };
                    a.Completed += (o, e) => { if (open) { rowsHost.BeginAnimation(FrameworkElement.HeightProperty, null); rowsHost.Height = double.NaN; } };
                    rowsHost.BeginAnimation(FrameworkElement.HeightProperty, a);
                    rows.Opacity = 0;
                    Anim.Fade(rows, 1, 250, 60);
                }
                else
                {
                    if (!anim) { rowsHost.Visibility = Visibility.Collapsed; return; }
                    var a = new DoubleAnimation(rowsHost.ActualHeight, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = Anim.Smooth };
                    a.Completed += (o, e) => { if (!open) { rowsHost.Visibility = Visibility.Collapsed; rowsHost.BeginAnimation(FrameworkElement.HeightProperty, null); rowsHost.Height = double.NaN; } };
                    rowsHost.BeginAnimation(FrameworkElement.HeightProperty, a);
                    Anim.Fade(rows, 0, 120);
                }
            };
            head.MouseLeftButtonUp += (o, e) =>
            {
                // clicks on the control in the header (a toggle or a list) don't open the expander
                var d = e.OriginalSource as DependencyObject;
                while (d != null && d != head) { if (d == headerControl) return; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d); }
                set(!open, true);
            };
            if (startOpen) set(true, false);
            MakeActivatable(head, () => set(!open, true));

            var ex = new Expander { Rows = rows, Open = () => set(true, false), Header = new CardParts { Root = null, Title = tb, Desc = db } };
            buildingReveal = ex.Open;
            Index(title, (desc ?? "") + " " + keywords + (L.Active ? " " + L.T(title) + " " + L.T(desc ?? "") : ""), outer);
            return ex;
        }

        /// <summary>A row inside an expander: lines up with the header's text, hairline between rows.</summary>
        static Grid Row(Expander ex, string title, string desc, FrameworkElement control, string keywords = "")
        {
            TextBlock tb, db;
            var content = CardContent(null, title, desc, control, out tb, out db);
            var row = new Grid { MinHeight = 52 };
            content.Margin = new Thickness(52, 10, 16, 10);
            if (ex.Rows.Children.Count > 0)
            {
                var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
                line.SetResourceReference(Border.BackgroundProperty, "CardBorder");
                row.Children.Add(line);
            }
            row.Children.Add(content);
            ex.Rows.Children.Add(row);
            Index(title, (desc ?? "") + " " + keywords + (L.Active ? " " + L.T(title) + " " + L.T(desc ?? "") : ""), row, ex.Open);
            return row;
        }

        /// <summary>Appearance > PiP window > Title bar buttons: a switch per button, and arrows to move the ones that are on
        /// left or right. The rows follow the title bar's order; buttons that are off are listed after them.</summary>
        static void BuildTitleBarSettings(StackPanel ap)
        {
            var ex = MakeExpander(ap, "\uE737", "Title bar buttons", "Pick the buttons on a PiP's title bar and their order", null, false,
                "title bar buttons toolbar previous next window customize order arrange header");
            var rows = new Dictionary<string, Grid>();
            var lefts = new Dictionary<string, Button>();
            var rights = new Dictionary<string, Button>();
            var switches = new Dictionary<string, Wpf.Ui.Controls.ToggleSwitch>();
            Action relayout = null;

            Action<List<string>> save = on =>
            {
                App.S.TitleBarButtons = string.Join(",", on);
                App.I.SettingsChanged();
                relayout();
            };
            Func<string, string, string, Button> arrow = (key, glyph, tip) =>
            {
                var b = new Wpf.Ui.Controls.Button
                {
                    Content = new TextBlock { Text = glyph, FontFamily = Fluent.IconFont, FontSize = 12 },
                    Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent, BorderThickness = new Thickness(0),
                    Width = 32, Height = 32, Padding = new Thickness(0), Margin = new Thickness(0, 0, 2, 0), ToolTip = tip
                };
                return b;
            };

            var all = TitleBarItems.On(App.S.TitleBarButtons);
            foreach (var k in TitleBarItems.Keys) if (!all.Contains(k)) all.Add(k);
            foreach (var key in all)
            {
                string k = key;
                var l = arrow(k, "\uE76B", "Move left");
                var r = arrow(k, "\uE76C", "Move right");
                var sw = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = false, Margin = new Thickness(10, 0, 0, 0) };
                l.Click += (o, e) =>
                {
                    var on = TitleBarItems.On(App.S.TitleBarButtons);
                    int i = on.IndexOf(k);
                    if (i > 0) { on.RemoveAt(i); on.Insert(i - 1, k); save(on); }
                };
                r.Click += (o, e) =>
                {
                    var on = TitleBarItems.On(App.S.TitleBarButtons);
                    int i = on.IndexOf(k);
                    if (i >= 0 && i < on.Count - 1) { on.RemoveAt(i); on.Insert(i + 1, k); save(on); }
                };
                sw.Click += (o, e) =>
                {
                    var on = TitleBarItems.On(App.S.TitleBarButtons);
                    if (sw.IsChecked == true && !on.Contains(k))
                    {
                        // a button switched on goes just left of More options / Close
                        int at = on.Count;
                        for (int i = 0; i < on.Count; i++) if (on[i] == "more" || on[i] == "close") { at = i; break; }
                        on.Insert(at, k);
                    }
                    else if (sw.IsChecked != true) on.Remove(k);
                    save(on);
                };
                var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                ctl.Children.Add(l); ctl.Children.Add(r); ctl.Children.Add(sw);
                var label = TitleBarItems.Label(k);
                var row = Row(ex, label, k == TitleBarItems.Required ? "Always shown, so the full menu is never out of reach" : null, ctl, "title bar button " + k);
                // every row gets a divider line; the first one's is hidden when the rows are put in order
                bool hasLine = row.Children.Count > 0 && row.Children[0] is Border && ((Border)row.Children[0]).Height == 1;
                if (!hasLine)
                {
                    var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
                    line.SetResourceReference(Border.BackgroundProperty, "CardBorder");
                    row.Children.Insert(0, line);
                }
                rows[k] = row; lefts[k] = l; rights[k] = r; switches[k] = sw;
            }
            var reset = new Button { Content = "Reset", Padding = new Thickness(14, 4, 14, 4) };
            reset.Click += (o, e) => save(TitleBarItems.On(TitleBarItems.Default));
            var resetRow = Row(ex, "Reset title bar buttons", "Back to the default buttons and order", reset, "default restore");

            // a preview of the title bar at the top: your buttons, in your order, redrawn on every change
            var previewIcons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 3, 0) };
            var previewBar = new Grid { Height = 32, Background = new SolidColorBrush(Color.FromRgb(20, 20, 24)) };
            previewBar.Children.Add(new TextBlock { Text = "Your video", Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 242)), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            previewBar.Children.Add(previewIcons);
            var previewPic = new Border { Height = 46 };
            previewPic.SetResourceReference(Border.BackgroundProperty, "Accent");
            previewPic.Opacity = 0.55;
            var previewPip = new StackPanel { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
            previewPip.Children.Add(previewBar);
            previewPip.Children.Add(previewPic);
            var previewClip = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = previewPip, HorizontalAlignment = HorizontalAlignment.Left };
            var previewRow = new Border { Padding = new Thickness(52, 12, 16, 14), Child = previewClip };
            Action redrawPreview = () =>
            {
                previewIcons.Children.Clear();
                foreach (var k in TitleBarItems.On(App.S.TitleBarButtons))
                    previewIcons.Children.Add(new TextBlock
                    {
                        Text = TitleBarItems.Glyph(k), FontFamily = Fluent.IconFont, FontSize = 13, Width = 30,
                        Foreground = new SolidColorBrush(Color.FromRgb(238, 238, 244)), TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center, ToolTip = TitleBarItems.Label(k)
                    });
                previewPip.Width = Math.Max(240, previewIcons.Children.Count * 30 + 120);   // wide enough for every button and a bit of title
            };

            relayout = () =>
            {
                var on = TitleBarItems.On(App.S.TitleBarButtons);
                var seq = new List<string>(on);
                foreach (var k in TitleBarItems.Keys) if (!seq.Contains(k)) seq.Add(k);
                ex.Rows.Children.Clear();
                redrawPreview();
                ex.Rows.Children.Add(previewRow);
                for (int i = 0; i < seq.Count; i++)
                {
                    string k = seq[i];
                    var row = rows[k];
                    ((Border)row.Children[0]).Visibility = Visibility.Visible;              // the preview sits above the first row
                    ex.Rows.Children.Add(row);
                    int at = on.IndexOf(k);
                    lefts[k].IsEnabled = at > 0;
                    rights[k].IsEnabled = at >= 0 && at < on.Count - 1;
                    switches[k].IsChecked = at >= 0;
                    switches[k].IsEnabled = k != TitleBarItems.Required;
                }
                ex.Rows.Children.Add(resetRow);
            };
            relayout();
        }

        // controls ---------------------------------------------------------------------
        static Wpf.Ui.Controls.ToggleSwitch Switch(Func<bool> get, Action<bool> set)
        {
            var ts = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = get() };
            ts.Click += (o, e) => { set(ts.IsChecked == true); App.I.SettingsChanged(); };
            return ts;
        }

        // where new PiPs open: corners, plus the middle of each edge while 8 spots are on
        static readonly string[] PosLabels = { "Top left", "Top right", "Bottom left", "Bottom right", "Top middle", "Bottom middle", "Left middle", "Right middle" };
        static readonly int[] PosOrder = { 0, 4, 1, 6, 7, 2, 5, 3 };          // shown in reading order
        static ComboBox posCombo;
        static List<int> posValues = new List<int>();
        static bool posSync;

        static ComboBox PositionCombo()
        {
            posCombo = new ComboBox { MinWidth = 200 };
            posCombo.SelectionChanged += (o, e) =>
            {
                if (posSync || posCombo.SelectedIndex < 0) return;
                App.S.DefaultCorner = posValues[posCombo.SelectedIndex];
                App.I.SettingsChanged();
            };
            FillPositions();
            return posCombo;
        }

        /// <summary>Rebuilds the Position choices (the middle spots come and go with "Middle of each edge too").</summary>
        public static void FillPositions()
        {
            if (posCombo == null) return;
            bool edges = App.S.SnapToEdges;
            var vals = PosOrder.Where(v => v < 4 || edges).ToList();
            int cur = App.S.DefaultCorner;
            if (!vals.Contains(cur)) cur = cur == 4 ? 1 : cur == 6 ? 2 : 3;   // a middle spot that's switched off: show its corner
            if (posValues.SequenceEqual(vals) && posCombo.Items.Count == vals.Count && posCombo.SelectedIndex == vals.IndexOf(cur)) return;
            posSync = true;
            posValues = vals;
            posCombo.Items.Clear();
            foreach (var v in vals) posCombo.Items.Add(L.Plain(L.T(PosLabels[v])));
            L.Combo(posCombo);
            posCombo.SelectedIndex = vals.IndexOf(cur);
            posSync = false;
        }

        static ComboBox Combo(string[] options, int value, Action<int> set)
        {
            var cb = new ComboBox { MinWidth = 200 };
            foreach (var o in options) cb.Items.Add(L.Plain(L.T(o)));   // the drop-down list lives in a popup, so translate up front
            L.Combo(cb);
            cb.SelectedIndex = Math.Max(0, Math.Min(options.Length - 1, value));
            cb.SelectionChanged += (o, e) => { if (cb.SelectedIndex >= 0) { set(cb.SelectedIndex); App.I.SettingsChanged(); } };
            return cb;
        }

        static FrameworkElement SliderBox(double min, double max, double value, string suffix, Action<int> set)
        {
            var dp = new DockPanel { Width = 240 };
            var val = Fluent.Label((int)value + suffix, 14, "Sub", false);
            val.Width = 56;
            val.TextAlignment = TextAlignment.Right;
            DockPanel.SetDock(val, Dock.Right);
            dp.Children.Add(val);
            var sl = new Slider { Minimum = min, Maximum = max, Value = value, VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = true, TickFrequency = 1 };
            dp.Children.Add(sl);
            sl.ValueChanged += (o, e) => { val.Text = (int)sl.Value + suffix; set((int)sl.Value); App.I.SettingsChanged(); };
            return dp;
        }

        static FrameworkElement ColorButton(Func<System.Drawing.Color> get, Action<System.Drawing.Color> set)
        {
            var c0 = get();
            var swatch = new Border { Width = 24, Height = 16, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromRgb(c0.R, c0.G, c0.B)), Margin = new Thickness(0, 0, 10, 0), BorderThickness = new Thickness(1) };
            swatch.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(swatch);
            sp.Children.Add(new TextBlock { Text = "Choose", VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = sp, MinWidth = 120 };
            b.Click += (o, e) =>
            {
                using (var cd = new System.Windows.Forms.ColorDialog { Color = get(), FullOpen = true })
                {
                    if (cd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    set(cd.Color);
                    swatch.Background = new SolidColorBrush(Color.FromRgb(cd.Color.R, cd.Color.G, cd.Color.B));
                    App.I.SettingsChanged();
                }
            };
            return b;
        }

        static Button PlainButton(string text, Action click, bool accent = false)
        {
            if (accent)
            {
                var ub = new Wpf.Ui.Controls.Button { Content = text, Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, MinWidth = 120 };
                ub.Click += (o, e) => click();
                return ub;
            }
            var b = new Button { Content = text, MinWidth = 120 };
            b.Click += (o, e) => click();
            return b;
        }

        /// <summary>The Phone page: the companion on or off, the address and Pair a phone, then the paired phones with Rename
        /// and Remove.</summary>
        static void BuildPhonePage(StackPanel pp, Settings S)
        {
            if (Packaged.IsPackaged)
            {
                Card(pp, "\uE8EA", "Not in the Microsoft Store version", "The phone companion needs network access the Store version doesn't have. The installer and portable versions from GitHub include it.", null, "phone companion store");
                return;
            }
            Section(pp, "Phone companion");
            Card(pp, "\uE8EA", "Control PiP Studio from your phone", "Manage your PiPs and every app's volume from your phone's browser, over your home Wi-Fi. Windows may ask you to allow PiP Studio on private networks.",
                Switch(() => S.CompanionOn, v => { S.CompanionOn = v; Companion.Apply(); }), "phone mobile android iphone remote companion volume mixer wifi qr");
            var pairBtn = PlainButton("Pair a phone", ShowPairing, true);
            var pairCard = Card(pp, "\uED14", "Pair a phone", "Turn on the switch above first", pairBtn, "pair qr code connect phone scan pin");

            Section(pp, "Paired phones");
            var phonesHost = new StackPanel();
            pp.Children.Add(phonesHost);

            refreshCompanion = () =>
            {
                pairBtn.IsEnabled = Companion.Running;
                string url = Companion.Url;
                if (pairCard.Desc != null)
                    pairCard.Desc.Text = !S.CompanionOn ? "Turn on the switch above first"
                        : Companion.Problem ?? (url == null ? "This PC isn't on a network right now" : "Your phone opens " + url);
                phonesHost.Children.Clear();
                var devices = Companion.Devices();
                if (devices.Count == 0)
                {
                    var none = Fluent.Label("No phones are paired yet. Press Pair a phone above to add one.", 13, "Sub");
                    none.Margin = new Thickness(4, 2, 0, 8);
                    phonesHost.Children.Add(none);
                }
                foreach (var d in devices)
                {
                    var dev = d;
                    string seen = dev.LastSeen == null ? "Not used since PiP Studio started" : "Last used " + dev.LastSeen.Value.ToString("t");
                    var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                    var rename = PlainButton("Rename", () => RenamePhone(dev));
                    rename.Margin = new Thickness(0, 0, 8, 0);
                    var remove = PlainButton("Remove", () =>
                        Ask("Remove " + dev.Name + "?", "It stops working right away. You can pair it again any time.", "Remove", "Cancel", ok => { if (ok) Companion.Remove(dev.Id); }));
                    buttons.Children.Add(rename); buttons.Children.Add(remove);
                    Card(phonesHost, "\uE8EA", dev.Name, seen, buttons, "paired phone device rename remove");
                }
            };
            refreshCompanion();
        }

        // ---------------------------------------------------------------- saved layouts (Behavior page)
        static FrameworkElement layoutsAnchor;

        static void BuildLayouts(StackPanel be)
        {
            Section(be, "Layouts");
            var saveBtn = PlainButton("Save", AskLayoutName, true);
            var head = Card(be, "\uF0E2", "Save these PiPs as a layout", "Bring back a set of PiPs (their windows, places and sizes) in one go, from here, the Running PiPs page or the tray menu. A layout can also open by itself when an app starts.", saveBtn, "layout preset profile workspace save restore arrangement gaming work");
            layoutsAnchor = head.Root;
            var host = new StackPanel();
            be.Children.Add(host);
            refreshLayouts = () =>
            {
                host.Children.Clear();
                if (Layouts.All.Count == 0)
                {
                    var none = Fluent.Label("No layouts yet. Open the PiPs you want, then press Save above.", 13, "Sub");
                    none.Margin = new Thickness(4, 2, 0, 8);
                    host.Children.Add(none);
                }
                foreach (var l in Layouts.All)
                {
                    var lay = l;
                    var openBtn = PlainButton("Open", () => Layouts.Open(lay, true));
                    Func<string> desc = () => L.T(lay.Count == 1 ? "1 PiP" : "{0} PiPs").Replace("{0}", lay.Count.ToString()) + (lay.App.Length > 0 ? " · " + L.T("opens when {0} starts").Replace("{0}", lay.App) : "");
                    var ex = MakeExpander(host, "\uF0E2", lay.Name, desc(), openBtn, false, "layout " + lay.Name);
                    // the app it opens with: apps with a window open now, plus the one it has
                    var apps = Layouts.AppsWithWindows();
                    if (lay.App.Length > 0 && !apps.Contains(lay.App, StringComparer.OrdinalIgnoreCase)) apps.Insert(0, lay.App);
                    var opts = new List<string> { L.T("Only when you open it") };
                    opts.AddRange(apps);
                    int cur = lay.App.Length == 0 ? 0 : 1 + apps.FindIndex(a => a.Equals(lay.App, StringComparison.OrdinalIgnoreCase));
                    Row(ex, "Open when this app starts", "Its PiPs open next to the ones you have. Apps with a window open now are listed.",
                        Combo(opts.ToArray(), cur, v =>
                        {
                            Layouts.SetApp(lay, v <= 0 ? "" : apps[v - 1]);
                            if (ex.Header != null && ex.Header.Desc != null) ex.Header.Desc.Text = desc();
                        }), "automatic app game rule start launch");
                    Row(ex, "Close its PiPs when the app exits", null, Switch(() => lay.CloseWithApp, v => Layouts.SetCloseWithApp(lay, v)), "automatic close exit quit");
                    Row(ex, "Replace with the PiPs open now", null, PlainButton("Replace", () =>
                        Ask(L.T("Replace {0}?").Replace("{0}", lay.Name), "It will bring back the PiPs that are open now instead.", "Replace", "Cancel", ok => { if (ok) Layouts.SaveCurrent(lay.Name); })), "update overwrite");
                    Row(ex, "Delete this layout", null, PlainButton("Delete", () =>
                        Ask(L.T("Delete {0}?").Replace("{0}", lay.Name), "Its PiPs stay open; only the saved layout goes.", "Delete", "Cancel", ok => { if (ok) Layouts.Delete(lay); })), "remove");
                }
            };
            refreshLayouts();
        }

        static void ScrollToLayouts()
        {
            if (layoutsAnchor != null) try { layoutsAnchor.BringIntoView(); } catch { }
        }

        /// <summary>Asks for a name and saves the PiPs that are open as a layout (asking first before replacing one).</summary>
        public static void AskLayoutName()
        {
            if (!App.I.Pips.Any(x => !x.IsClosing)) { Ask("No PiPs are open", "Open the PiPs you want first, then save them as a layout.", "OK", null, null); return; }
            int n = Layouts.All.Count + 1;
            while (Layouts.Find(L.T("Layout") + " " + n) != null) n++;
            var box = new TextBox { Text = L.T("Layout") + " " + n, MaxLength = 40, MinWidth = 280, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 14, 0, 0) };
            box.Loaded += (o, e) => box.SelectAll();
            Ask("Save as a layout", "A name for this set of PiPs, like Gaming or Work.", "Save", "Cancel", ok =>
            {
                string name = (box.Text ?? "").Trim();
                if (!ok || name.Length == 0) return;
                if (Layouts.Find(name) != null)
                    DelayRun(200, () => Ask(L.T("Replace {0}?").Replace("{0}", name), "A layout with that name exists. It will bring back the PiPs that are open now instead.", "Replace", "Cancel", ok2 => { if (ok2) Layouts.SaveCurrent(name); }));
                else Layouts.SaveCurrent(name);
            }, false, box, box);
        }

        // ---------------------------------------------------------------- settings in a file (Settings page)
        static void ExportSettings()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "PiP Studio settings" + Backup.Extension,
                Filter = L.T("PiP Studio settings") + " (*" + Backup.Extension + ")|*" + Backup.Extension,
                DefaultExt = Backup.Extension
            };
            if (dlg.ShowDialog(w) != true) return;
            try { Backup.Export(dlg.FileName); Ask("Settings saved", "Import this file on the Settings page of PiP Studio on another PC (or this one) to bring them back.", "OK", null, null); }
            catch (Exception ex) { Ask("Couldn't save the settings", ex.Message, "OK", null, null, true); }
        }

        static void ImportSettings()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("PiP Studio settings") + " (*" + Backup.Extension + ")|*" + Backup.Extension + "|" + L.T("All files") + " (*.*)|*.*" };
            if (dlg.ShowDialog(w) != true) return;
            string file = dlg.FileName;
            Ask("Replace your settings?", "Your settings, shortcuts and layouts change to the ones in the file. Paired phones stay as they are.", "Replace", "Cancel", ok =>
            {
                if (!ok) return;
                string err = Backup.Import(file);
                if (err != null) { DelayRun(200, () => Ask("Couldn't read the settings", err, "OK", null, null, true)); return; }
                DelayRun(100, () => { SwitchLanguage(); Navigate("settings", false); });   // rebuilt with the new settings (and language)
            });
        }

        /// <summary>Rename a paired phone (its name comes from what the phone's browser says it is, like "Pixel 7").</summary>
        static void RenamePhone(Companion.Device dev)
        {
            var box = new TextBox { Text = dev.Name, MaxLength = 40, MinWidth = 280, Padding = new Thickness(8, 6, 8, 6) };
            box.Loaded += (o, e) => box.SelectAll();
            Ask("Rename phone", "The name shows here, so you can tell your phones apart.", "Save", "Cancel", ok =>
            {
                string name = (box.Text ?? "").Trim();
                if (ok && name.Length > 0) Companion.Rename(dev.Id, name);
            }, false, box, box);
        }

        /// <summary>The Pair a phone dialog, like Moonlight and Sunshine: the phone shows a 6-digit PIN and you type it here.
        /// The QR code only carries the page's address; phones can start pairing only while this dialog is open.</summary>
        static void ShowPairing()
        {
            string url = Companion.Url;
            if (url == null)
            {
                Ask("No network", "PiP Studio can't find this PC's address on your local network. Connect to Wi-Fi or Ethernet and try again.", "OK", null, null);
                return;
            }
            Companion.BeginPairing();
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), MinWidth = 300 };
            try
            {
                using (var gen = new QRCoder.QRCodeGenerator())
                using (var data = gen.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M))
                {
                    byte[] png = new QRCoder.PngByteQRCode(data).GetGraphic(8);   // black on white, with a quiet border
                    var img = new BitmapImage();
                    img.BeginInit(); img.StreamSource = new MemoryStream(png); img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit(); img.Freeze();
                    var frame = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new Image { Source = img, Width = 180, Height = 180, SnapsToDevicePixels = true } };
                    RenderOptions.SetBitmapScalingMode(frame.Child, BitmapScalingMode.NearestNeighbor);   // crisp squares
                    panel.Children.Add(frame);
                }
            }
            catch (Exception ex) { App.ReportError(ex); }
            var addr = Fluent.Label("Or open " + url + " on your phone", 13, "Sub");
            addr.TextAlignment = TextAlignment.Center;
            addr.Margin = new Thickness(0, 10, 0, 14);
            panel.Children.Add(addr);

            var pinRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var pinBox = new TextBox { Width = 150, FontSize = 22, MaxLength = 7, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 4, 6, 4) };
            var pairBtn = PlainButton("Pair", () => { }, true);
            pairBtn.Margin = new Thickness(8, 0, 0, 0);
            pinRow.Children.Add(pinBox); pinRow.Children.Add(pairBtn);
            panel.Children.Add(pinRow);
            var status = Fluent.Label("Waiting for your phone…", 13, "Sub");
            status.TextAlignment = TextAlignment.Center;
            status.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(status);

            bool done = false;
            Action tryPin = () =>
            {
                if (done) return;
                string name;
                switch (Companion.ConfirmPin(pinBox.Text, out name))
                {
                    case Companion.PinResult.Paired:
                        done = true;
                        status.Text = "Paired with " + name + ". You can close this.";
                        status.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                        pinBox.IsEnabled = pairBtn.IsEnabled = false;
                        break;
                    case Companion.PinResult.Wrong: status.Text = "That PIN doesn't match. Check the PIN on your phone."; pinBox.SelectAll(); break;
                    case Companion.PinResult.TooMany: status.Text = "Too many wrong PINs. Start pairing again on your phone."; pinBox.Clear(); break;
                    default: status.Text = "No phone is showing a PIN yet. Open the page on your phone and tap Pair."; break;
                }
            };
            pairBtn.Click += (o, e) => tryPin();
            pinBox.KeyDown += (o, e) => { if (e.Key == Key.Enter) { tryPin(); e.Handled = true; } };
            Action onChange = () =>
            {
                if (!done && Companion.PhoneWaiting) status.Text = "Your phone is showing a PIN. Type it here.";
            };
            Companion.Changed += onChange;
            Ask("Pair a phone",
                "1. Scan this code with your phone's camera (it needs to be on the same Wi-Fi as this PC) and tap Pair.\n2. Your phone shows a 6-digit PIN. Type it below.",
                "Close", null, ok => { Companion.Changed -= onChange; Companion.EndPairing(); }, false, panel, pinBox);
        }

        /// <summary>Settings > General > Updates: where things stand, Check now / Update now / What's new, and the daily check.</summary>
        static void BuildUpdateSettings(StackPanel ge, Settings S)
        {
            Section(ge, "Updates");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var whatsNew = PlainButton("What's new", Updater.OpenReleasePage);
            var check = PlainButton("Check now", () => Updater.Check(true));
            var install = PlainButton("Update now", Updater.Install, true);
            whatsNew.Margin = new Thickness(0, 0, 8, 0);
            check.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(whatsNew); buttons.Children.Add(check); buttons.Children.Add(install);
            var card = Card(ge, "", "Updates", Updater.Status, buttons, "update upgrade new version download latest");
            Card(ge, "", "Check for updates automatically", "Once a day, on GitHub. Nothing about you or your PC is sent.", Switch(() => S.CheckForUpdates, v => S.CheckForUpdates = v), "update automatic check github");

            refreshUpdates = () =>
            {
                if (card.Desc != null) card.Desc.Text = Updater.Status;
                bool busy = Updater.Busy;
                install.Visibility = Updater.CanInstall ? Visibility.Visible : Visibility.Collapsed;
                check.Visibility = Updater.CanInstall ? Visibility.Collapsed : Visibility.Visible;
                whatsNew.Visibility = Updater.Latest != null ? Visibility.Visible : Visibility.Collapsed;
                check.IsEnabled = install.IsEnabled = !busy;
            };
            refreshUpdates();
        }
    }
}
