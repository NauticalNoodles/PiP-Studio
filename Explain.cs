using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace PipStudio
{
    // =================================================================================
    //  Animated explainers for settings: a small looping scene in a panel on the right
    //  of the Appearance, Behavior and Controller pages. Point at (or move the focus to)
    //  a setting and the panel shows what it does.
    // =================================================================================
    static partial class MainUI
    {
        const double SW = 264, SH = 150;                 // the little "screen" the scenes play on

        class Explainer
        {
            public string Title, Text;
            public Action<Canvas, Storyboard> Build;
        }

        static readonly Dictionary<string, Explainer> explainers = new Dictionary<string, Explainer>();
        static readonly Dictionary<string, string> explainByTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> explainFirst = new Dictionary<string, string>();
        static Border explainCard;
        static Canvas explainStage;
        static TextBlock explainTitle, explainText;
        static Storyboard explainSb;
        static string explainShown;

        static readonly Dictionary<FrameworkElement, string> explainEls = new Dictionary<FrameworkElement, string>();

        /// <summary>The setting under the pointer (or, failing that, the focused one) decides what plays.</summary>
        static void EvalHover()
        {
            if (explainCard == null || explainCard.Visibility != Visibility.Visible) return;
            if (Mouse.Captured != null) return;                         // a drop-down or slider drag is in progress: wait
            string id = IdFor(Mouse.DirectlyOver as DependencyObject);
            if (id != null) ShowExplainer(id);
        }

        static string IdFor(DependencyObject d)
        {
            while (d != null)
            {
                var fe = d as FrameworkElement;
                string id;
                if (fe != null && explainEls.TryGetValue(fe, out id)) return id;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        static void HookHover()
        {
            settingsHost.MouseMove += (o, e) => EvalHover();
            // scrolling moves settings under a still pointer: check again once it settles
            pageScroll.ScrollChanged += (o, e) => { if (pageScroll.IsMouseOver) DelayRun(90, EvalHover); };
            // a drop-down just closed (pointer may now sit over a different setting): go by the setting you changed
            settingsHost.AddHandler(UIElement.LostMouseCaptureEvent, new MouseEventHandler((o, e) => DelayRun(60, () =>
            {
                string id = IdFor(Keyboard.FocusedElement as DependencyObject);
                if (id != null) ShowExplainer(id); else EvalHover();
            })), true);
        }

        static readonly HashSet<string> ExplainPages = new HashSet<string> { "appearance", "behavior", "controller", "settings", "phone" };

        static void ExplainInit()
        {
            explainCard = F<Border>("ExplainCard");
            explainStage = F<Canvas>("ExplainStage");
            explainTitle = F<TextBlock>("ExplainTitle");
            explainText = F<TextBlock>("ExplainText");
            explainShown = null;
            explainFirst.Clear();
            explainEls.Clear();
            HookHover();
            if (explainers.Count > 0) return;

            EAdd("snap", "Snap spots", "When you let go of a PiP it glides to the nearest spot: the 4 corners, plus the middle of each edge with 8 spots on. The arrow keys and D-pad use the same spots.", ScSnap,
                "Snap after moving", "Middle of each edge too", "Glides to the nearest spot when you let go");
            EAdd("margin", "Gap from the edges", "How far a PiP sits from the edges of the screen.", ScMargin,
                "Gap from the screen edges");
            EAdd("position", "Where new PiPs open", "A new PiP pops up in the corner you pick here.", ScPosition,
                "Position");
            EAdd("size", "Size of new PiPs", "Eight sizes from Extra small to Extra large, set by width so a PiP keeps its shape. Bigger and Smaller step through the same sizes. Your choice is highlighted.", ScSize,
                "Size");
            EAdd("hover", "When the pointer is over a PiP", "A PiP can show its controls, fade, do nothing, or slide out of the way when the pointer reaches it. When it slides aside, hold Ctrl to grab it instead. Fade-out opacity sets how see-through it gets.", ScHover,
                "When the pointer is over a PiP", "Fade-out opacity");
            EAdd("arrows", "Move arrows", "Arrow buttons on the PiP send it to the next spot, or across to the next display.", ScArrows,
                "Show move arrows");
            EAdd("theme", "App theme", "Light or dark for PiP Studio's windows and menus, or follow Windows.", ScTheme,
                "App theme");
            EAdd("accent", "Accent color", "The color of highlights, switches, the focus ring and borders. Use Windows' color or pick your own.", ScAccent,
                "Accent color", "Use my Windows accent color", "Custom accent color");
            EAdd("anims", "Animation effects", "Glide, fade and slide effects in PiP Studio and on PiPs. Off, things jump straight into place.", ScAnims,
                "Animation effects");
            EAdd("lite", "Lite mode", "For slower PCs: turns off animations, the Mica background, shadows and live previews. PiPs themselves work the same.", ScLite,
                "Lite mode");
            EAdd("ghost", "Ghost mode", "Clicks go straight through the PiP to the window behind it.", ScGhost,
                "Click-through opacity");
            EAdd("corners", "Rounded corners", "Square or rounded corners, like other Windows 11 windows.", ScCorners,
                "Rounded corners");
            EAdd("border", "Border", "A thin outline around each PiP, in your accent color or one you pick.", ScBorder,
                "Border", "Border color", "Match accent color", "Width");
            EAdd("detectpips", "Find other apps' PiPs", "Besides browser PiPs and Discord pop-outs, PiP Studio treats small always-on-top windows of other apps as their PiPs: video players' mini modes, Chrome's document PiPs (Spotify, YouTube Music, Meet), Telegram and Zoom floating videos and so on. If it picks up something that isn't one, select it on the Running PiPs page and choose Not a PiP. To add a window it missed, select it on the Windows page and choose Treat as a PiP. For browsers and Discord the mark lasts until that window closes; for other apps it's remembered.", null,
                "Find other apps' PiPs automatically", "Windows you marked");
            EAdd("titlebar", "Title bar buttons", "The buttons a PiP's title bar shows when you point at it, in your order. Previous and Next window switch what the PiP shows, and Bigger and Smaller step through the sizes. More options always stays, so the full menu is never out of reach. On a narrow PiP, buttons on the left make room first.", ScTitleBar,
                "Title bar buttons", "Reset title bar buttons");
            EAdd("messages", "Messages under the PiP", "When you switch sources or change a mode, the message shows in a small caption below the PiP instead of over the picture. Volume changes show a level bar there too.", ScMessages,
                "Messages under the PiP");
            EAdd("follow", "Follow the window you left", "When you switch windows, the PiP switches to the one you were just on, so the window you left stays in view. Auto PiPs always do this.", ScFollow,
                "Follow the window I just left");
            EAdd("hidesrc", "Hide while its window is in front", "No point showing a window twice: the PiP steps away while you're in its window.", ScHideSrc,
                "Hide a PiP while its window is in front");
            EAdd("fullscreen", "Hide over full-screen apps", "PiPs on a display get out of the way while a full-screen app or game runs there.", ScFullscreen,
                "Hide PiPs while a fullscreen app is on the same display");
            EAdd("limits", "Size limits", "Tall or wide apps stop growing at the size you set, as a share of the screen. Applies to new PiPs, size presets, resizing and source changes. Theater mode ignores them.", ScLimits,
                "Size limits", "Limit height", "Maximum height", "Limit width", "Maximum width");
            EAdd("opacity", "Opacity", "How see-through a new PiP is.", ScOpacity,
                "Opacity");
            EAdd("theater", "Theater mode", "A big, centered PiP for when you want to watch properly. Press again to go back.", ScTheater,
                "Theater mode");
            EAdd("minimized", "Keep minimized sources playing", "Minimizing a source tucks it off-screen so the PiP keeps moving. Click it in the taskbar to bring it back.", ScMinimized,
                "Keep minimized sources playing");
            EAdd("rightclick", "Right-click on a PiP", "What a right-click on a PiP does: open its menu, close it, show its window, theater mode, or nothing.", ScRightClick,
                "Right-click on a PiP");
            EAdd("multi", "More than one PiP", "On: each new PiP opens alongside the others. Off: opening a new PiP switches the one that's already open to the new window.", ScMulti,
                "Allow more than one PiP at a time");
            EAdd("external", "Other apps' PiPs", "When no PiP Studio PiP is open, your shortcuts and controller move, resize, fade, mute and close the picture-in-picture windows of Chrome, Edge, Firefox, Discord and other apps. They also land on the same spots as your PiPs, when they open and when you let go after dragging them.", ScExternal,
                "Control other apps' PiP windows", "Snap other apps' PiPs to spots");
            EAdd("exthover", "Controls on other apps' PiPs", "Point at the PiP window of Chrome, Firefox, Discord or another app and PiP Studio's title bar appears over its top edge. Drag the bar to move the PiP, scroll on it to change the volume, minimize it, and use the same buttons as your own PiPs' title bar: show the app, theater mode, bigger, smaller, next corner, close and more. The rest of the PiP keeps working as usual.", ScExtHover,
                "Show controls on other apps' PiPs");
            EAdd("controller", "Controller controls", "Move, size and switch PiPs with an Xbox, PlayStation, Switch or most other controllers. With Release the game's focus on, the game ignores the controller while you steer the PiP and gets focus back afterwards. Hints show which button does what, and fade after a few idle seconds.", ScController,
                "Use game controllers", "Release the game's focus while controls are on", "Show button hints on the PiP", "Controller options");
            EAdd("windowstyle", "Window style", "Windows 11 is the normal look. Xbox dresses this window like the console's dashboard: always dark, Xbox green highlights, rounder tiles, and buttons that light up green with a white outline when you reach them with a controller or the keyboard. Only this window changes: your PiPs, menus and the window picker keep their look.", ScLooks,
                "Window style");
            EAdd("phone", "Phone companion", "Your phone becomes a remote for PiP Studio: move and resize PiPs, switch what they show, and set the volume of every app, from the phone's browser on your home Wi-Fi. Nothing goes over the internet, and everything between phone and PC is encrypted.", ScPhone,
                "Control PiP Studio from your phone");
            EAdd("pair", "Pairing a phone", "Like pairing Moonlight with Sunshine: scan the code, tap Pair on your phone, and type the PIN it shows into PiP Studio. The PIN only matches when nothing sits between the two, and only paired phones can connect. Remove a phone any time.", ScPair,
                "Pair a phone");
            // the rows inside Title bar buttons (one per button) explain the title bar, unless they have an explanation of their own
            foreach (var t in TitleBarItems.Labels.Concat(new[] { "Reset title bar buttons" }))
                if (!explainByTitle.ContainsKey(t)) explainByTitle[t] = "titlebar";
        }

        static void EAdd(string id, string title, string text, Action<Canvas, Storyboard> build, params string[] titles)
        {
            explainers[id] = new Explainer { Title = title, Text = text, Build = build };
            foreach (var t in titles) explainByTitle[t] = id;
        }

        /// <summary>Called for every setting as it's built: pointing at it or focusing it shows its explainer.</summary>
        static void AttachExplainer(string title, FrameworkElement el)
        {
            if (title == null || el == null) return;
            string id;
            if (!explainByTitle.TryGetValue(title, out id))
            {
                // no animation for this one: show its own name and description over a quiet scene
                if (!ExplainPages.Contains(buildingPage ?? "")) return;
                id = "plain:" + title;
                if (!explainers.ContainsKey(id))
                    explainers[id] = new Explainer { Title = title, Text = string.IsNullOrWhiteSpace(lastDesc) ? "" : lastDesc, Build = null };   // no animation: text only
            }
            else if (buildingPage != null && !explainFirst.ContainsKey(buildingPage)) explainFirst[buildingPage] = id;
            string show = id;
            explainEls[el] = show;
            // keyboard / controller focus moving onto it (the mouse is handled by EvalHover)
            el.IsKeyboardFocusWithinChanged += (o, e) => { if (el.IsKeyboardFocusWithin && Mouse.Captured == null) ShowExplainer(show); };
        }

        static bool ExplainWanted() { return App.S.ShowExplainers && ExplainPages.Contains(page ?? ""); }

        /// <summary>Show or hide the explanations panel (button on the panel, a page command, or X on a controller).</summary>
        public static void ToggleExplainers()
        {
            App.S.ShowExplainers = !App.S.ShowExplainers;
            App.I.SettingsChanged();
            ApplyLayout(true);                                      // with the panel gone the menu can open again (and the other way round)
            LayoutDetails();
            BuildCommands();
            if (padVisual) BuildPadHints();
        }

        /// <summary>A setting changed: replay the current explainer so it shows the new choice.</summary>
        public static void RefreshExplainer()
        {
            if (explainShown == null || explainCard == null || explainCard.Visibility != Visibility.Visible) return;
            // replay for the setting you just changed (it has the focus), so it shows the new choice
            DelayRun(120, () =>
            {
                string id = IdFor(Keyboard.FocusedElement as DependencyObject) ?? explainShown;
                if (id != null) ShowExplainer(id, true);
            });
        }

        static void ExplainForPage()
        {
            string id;
            if (ExplainWanted() && explainFirst.TryGetValue(page, out id)) ShowExplainer(id, true);
        }

        static void ShowExplainer(string id, bool force = false)
        {
            Explainer ex;
            if (explainStage == null || !explainers.TryGetValue(id, out ex)) return;
            if (id == explainShown && !force) return;
            explainShown = id;
            if (explainSb != null) explainSb.Stop(explainStage);
            explainStage.Children.Clear();
            explainTitle.Text = ex.Title;
            explainText.Text = ex.Text;
            var stageBox = ((FrameworkElement)explainStage.Parent).Parent as FrameworkElement;   // the dark preview box
            if (ex.Build == null)
            {
                // this setting has no animation: show just its name and description
                if (stageBox != null) stageBox.Visibility = Visibility.Collapsed;
                explainSb = null;
                return;
            }
            if (stageBox != null) stageBox.Visibility = Visibility.Visible;
            var sb = new Storyboard();
            ex.Build(explainStage, sb);
            LoopAsOne(sb);
            explainSb = sb;
            explainStage.BeginAnimation(UIElement.OpacityProperty, null);
            if (Anim.On) { sb.RepeatBehavior = RepeatBehavior.Forever; sb.Begin(explainStage, true); }
            else { sb.Begin(explainStage, true); sb.Seek(explainStage, TimeSpan.FromSeconds(1.2), TimeSeekOrigin.BeginTime); sb.Pause(explainStage); explainStage.Opacity = 1; }   // still frame
        }

        /// <summary>
        /// Makes a scene restart as one: instead of each piece travelling back to its start at its own pace (the pointer first,
        /// then the PiP...), everything holds where it ended, the whole scene fades out, snaps back, and fades in again.
        /// </summary>
        static void LoopAsOne(Storyboard sb)
        {
            double end = 0;
            foreach (var tl in sb.Children)
            {
                var a = tl as DoubleAnimationUsingKeyFrames;
                if (a == null || a.KeyFrames.Count < 3) { if (a != null && a.KeyFrames.Count > 0) end = Math.Max(end, a.KeyFrames[a.KeyFrames.Count - 1].KeyTime.TimeSpan.TotalSeconds); continue; }
                // trailing keyframes that only travel back to the start value: drop them (only for tracks that start at 0s;
                // short blips like a press flash start later and must keep their "off" keyframe)
                if (a.KeyFrames[0].KeyTime.TimeSpan == TimeSpan.Zero)
                {
                    double first = a.KeyFrames[0].Value;
                    while (a.KeyFrames.Count > 2 && Math.Abs(a.KeyFrames[a.KeyFrames.Count - 1].Value - first) < 0.01)
                        a.KeyFrames.RemoveAt(a.KeyFrames.Count - 1);
                }
                end = Math.Max(end, a.KeyFrames[a.KeyFrames.Count - 1].KeyTime.TimeSpan.TotalSeconds);
            }
            if (end <= 0) end = 3;
            double total = end + 1.1;                                   // hold the final picture, then fade out
            var fade = new DoubleAnimationUsingKeyFrames();
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.25))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(total - 0.3))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(total))));
            Storyboard.SetTarget(fade, explainStage);
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
            sb.Children.Add(fade);
            sb.Duration = new Duration(TimeSpan.FromSeconds(total));
        }

        // ---------------------------------------------------------------- building blocks
        static Brush EB(string key) { return (Brush)Fluent.App.TryFindResource(key) ?? Brushes.Gray; }
        static Brush EHex(string h) { return (Brush)new BrushConverter().ConvertFromString(h); }

        static FrameworkElement EWin(double x, double y, double w, double h, string body, string bar)
        {
            var g = new Grid { Width = w, Height = h };
            g.Children.Add(new Rectangle { Fill = EHex(body), RadiusX = 3, RadiusY = 3 });
            g.Children.Add(new Rectangle { Fill = EHex(bar), Height = 9, VerticalAlignment = VerticalAlignment.Top, RadiusX = 3, RadiusY = 3 });
            Canvas.SetLeft(g, x); Canvas.SetTop(g, y);
            return g;
        }

        /// <summary>A PiP: a video-ish picture (gradient, play mark and a moving progress bar).</summary>
        static Grid EPip(double w = 84, double h = 48, string label = null)
        {
            var g = new Grid { Width = w, Height = h };
            var pic = new Rectangle { Fill = EB("Accent") };
            g.Children.Add(pic);
            if (label != null)
                g.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            else
                g.Children.Add(new Path { Data = Geometry.Parse("M0,0 L9,5.5 L0,11 Z"), Fill = Brushes.White, Opacity = 0.9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(new Rectangle { Height = 2, Fill = Brushes.White, Opacity = 0.25, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 6, 5) });
            return g;
        }

        static Path ECursor()
        {
            return new Path
            {
                Data = Geometry.Parse("M0,0 L0,15 L4,11 L7,17 L9.5,16 L6.5,10 L11.5,10 Z"),
                Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, IsHitTestVisible = false
            };
        }

        /// <summary>A label on a scene: white text on a black box with a thin border, readable over anything.</summary>
        static Border ETag(TextBlock t)
        {
            t.Opacity = 1;
            t.Foreground = t.Foreground ?? Brushes.White;
            return new Border
            {
                Child = t, Background = new SolidColorBrush(Color.FromArgb(240, 58, 58, 64)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 3)
            };
        }

        static void EPut(Canvas c, UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); c.Children.Add(e); }

        /// <summary>Keyframes for one property: pairs of (seconds, value). Eased like the rest of the app.</summary>
        static void EK(Storyboard sb, DependencyObject target, DependencyProperty prop, params double[] tv)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            for (int i = 0; i + 1 < tv.Length; i += 2)
                a.KeyFrames.Add(new EasingDoubleKeyFrame(tv[i + 1], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tv[i])), new QuinticEase { EasingMode = EasingMode.EaseOut }));
            Storyboard.SetTarget(a, target);
            Storyboard.SetTargetProperty(a, new PropertyPath(prop));
            sb.Children.Add(a);
        }

        /// <summary>The 9 grid positions of a w×h PiP with an edge gap (0..2 across, 0..2 down).</summary>
        static Point ESpot(int gx, int gy, double w, double h, double m = 10)
        {
            double x = gx == 0 ? m : gx == 2 ? SW - m - w : (SW - w) / 2;
            double y = gy == 0 ? m : gy == 2 ? SH - m - h : (SH - h) / 2;
            return new Point(x, y);
        }

        /// <summary>Moves an element through spots, holding at each one.</summary>
        static void ETour(Storyboard sb, UIElement e, double w, double h, int[][] spots, double hold = 0.55, double move = 0.4)
        {
            var xs = new List<double>(); var ys = new List<double>();
            double t = 0;
            foreach (var sp in spots)
            {
                var p = ESpot(sp[0], sp[1], w, h);
                xs.Add(t); xs.Add(p.X); ys.Add(t); ys.Add(p.Y);
                t += hold;
                xs.Add(t); xs.Add(p.X); ys.Add(t); ys.Add(p.Y);
                t += move;
            }
            var f = ESpot(spots[0][0], spots[0][1], w, h);
            xs.Add(t); xs.Add(f.X); ys.Add(t); ys.Add(f.Y);
            EK(sb, e, Canvas.LeftProperty, xs.ToArray());
            EK(sb, e, Canvas.TopProperty, ys.ToArray());
        }

        // ---- scenes that follow your current settings ----

        /// <summary>Drag a PiP and let go: it glides to the nearest spot (or stays put with snapping off).</summary>
        static void ScSnap(Canvas c, Storyboard sb)
        {
            bool snap = App.S.SnapToCorners, eight = App.S.SnapToEdges;
            const double w = 72, h = 41;
            var spots = new List<Point>();
            for (int gx = 0; gx < 3; gx++)
                for (int gy = 0; gy < 3; gy++)
                {
                    if (gx == 1 && gy == 1) continue;
                    if (!eight && (gx == 1 || gy == 1)) continue;
                    spots.Add(ESpot(gx, gy, w, h));
                }
            if (snap)
                foreach (var sp in spots)
                    EPut(c, new Ellipse { Width = 6, Height = 6, Fill = EB("Accent"), Opacity = 0.55 }, sp.X + w / 2 - 3, sp.Y + h / 2 - 3);

            // picked up from the bottom-left spot and let go a little short of the right edge, just below the middle
            var a = ESpot(0, 2, w, h);
            double dx = SW - 10 - w - 34, dy = (SH - h) / 2 + 14;
            // where it lands: the nearest spot by centre distance, exactly like real PiPs (or right where it was dropped)
            var land = new Point(dx, dy);
            if (snap)
            {
                double best = double.MaxValue;
                foreach (var sp in spots)
                {
                    double d = Math.Pow(sp.X - dx, 2) + Math.Pow(sp.Y - dy, 2);
                    if (d < best) { best = d; land = sp; }
                }
            }
            var pip = EPip(w, h); EPut(c, pip, a.X, a.Y);
            var cur = ECursor(); EPut(c, cur, a.X + w / 2, a.Y + h / 2);
            Panel.SetZIndex(cur, 5);
            // drag (pointer and PiP together), let go, then the PiP glides to its spot while the pointer stays where it let go
            EK(sb, pip, Canvas.LeftProperty, 0, a.X, 0.6, a.X, 1.7, dx, 2.0, dx, 2.7, land.X);
            EK(sb, pip, Canvas.TopProperty, 0, a.Y, 0.6, a.Y, 1.7, dy, 2.0, dy, 2.7, land.Y);
            EK(sb, cur, Canvas.LeftProperty, 0, a.X + w / 2, 0.6, a.X + w / 2, 1.7, dx + w / 2, 2.7, dx + w / 2);
            EK(sb, cur, Canvas.TopProperty, 0, a.Y + h / 2, 0.6, a.Y + h / 2, 1.7, dy + h / 2, 2.7, dy + h / 2);
        }

        /// <summary>A PiP whose title bar slides in with the buttons you picked, in your order.</summary>
        static void ScTitleBar(Canvas c, Storyboard sb)
        {
            c.Children.Clear();
            double w = 204, h = 115, x = (SW - w) / 2, y = (SH - h) / 2 + 4;
            var pip = EPip(w, h);
            EPut(c, pip, x, y);
            var bar = new Grid { Width = w, Height = 20, Background = new SolidColorBrush(Color.FromArgb(215, 22, 22, 26)), Opacity = 0 };
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 3, 0) };
            foreach (var glyph in EFitBar(TitleBarItems.On(App.S.TitleBarButtons).Select(TitleBarItems.Glyph).ToList(), w, 17))
                sp.Children.Add(new TextBlock
                {
                    Text = glyph, FontFamily = Fluent.IconFont, FontSize = 9, Foreground = Brushes.White,
                    Width = 17, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                });
            bar.Children.Add(sp);
            EPut(c, bar, x, y);
            EK(sb, bar, UIElement.OpacityProperty, 0, 0, 0.7, 0, 1.0, 1, 3.6, 1);
        }

        /// <summary>Like the real title bars: when the buttons don't fit, the ones on the left make room first, but the last two
        /// (More options and Close, or Minimize and Close) stay.</summary>
        static List<string> EFitBar(List<string> glyphs, double barWidth, double each)
        {
            int max = Math.Max(2, (int)((barWidth - 30) / each));
            var l = new List<string>(glyphs);
            while (l.Count > max) l.RemoveAt(0);
            return l;
        }

        /// <summary>The pointer reaches another app's PiP and PiP Studio's title bar fades in over its top edge, with the
        /// buttons it would really have there: mute, your title bar buttons that work on other apps' PiPs, minimize.</summary>
        static void ScExtHover(Canvas c, Storyboard sb)
        {
            const double w = 150, h = 86;
            var p = ESpot(2, 2, w, h);
            var g = new Grid { Width = w, Height = h };
            g.Children.Add(new Rectangle { Fill = EB("Accent") });
            g.Children.Add(new Rectangle { Height = 11, Fill = EHex("#202124"), VerticalAlignment = VerticalAlignment.Top });   // the app's own top strip
            var appIcon = EBadge(8); appIcon.HorizontalAlignment = HorizontalAlignment.Left; appIcon.VerticalAlignment = VerticalAlignment.Top; appIcon.Margin = new Thickness(3, 1.5, 0, 0);
            g.Children.Add(appIcon);
            g.Children.Add(new Path { Data = Geometry.Parse("M0,0 L11,6.5 L0,13 Z"), Fill = Brushes.White, Opacity = 0.9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            EPut(c, g, p.X, p.Y);

            // the same buttons ExtHover builds
            var keep = new HashSet<string> { "theater", "shrink", "grow", "corner", "display", "source", "more", "close" };
            var glyphs = new List<string> { "\uE767" };                                         // mute
            foreach (var k in TitleBarItems.On(App.S.TitleBarButtons))
                if (keep.Contains(k) && (k != "display" || System.Windows.Forms.Screen.AllScreens.Length > 1)) glyphs.Add(TitleBarItems.Glyph(k));
            int ci = glyphs.IndexOf(TitleBarItems.Glyph("close"));
            if (ci < 0) ci = glyphs.Count;
            glyphs.Insert(ci, "\uE921");                                                         // minimize

            var bar = new Grid { Width = w, Height = 17, Opacity = 0 };
            bar.Children.Add(new Rectangle { Fill = EHex("#F0141418") });
            var name = new TextBlock { Text = "Browser", Foreground = Brushes.White, FontSize = 8, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            bar.Children.Add(name);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 2, 0) };
            foreach (var glyph in EFitBar(glyphs, w, 12))
                sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Fluent.IconFont, FontSize = 7.5, Foreground = Brushes.White, Width = 12, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            bar.Children.Add(sp);
            EPut(c, bar, p.X, p.Y);

            var cur = ECursor(); EPut(c, cur, 40, 40);
            double cx = p.X + w / 2, cy = p.Y + h / 2 + 6;
            EK(sb, cur, Canvas.LeftProperty, 0, 40, 0.5, 40, 1.3, cx, 3.0, cx, 3.7, 40, 4.2, 40);
            EK(sb, cur, Canvas.TopProperty, 0, 40, 0.5, 40, 1.3, cy, 3.0, cy, 3.7, 40, 4.2, 40);
            EK(sb, bar, UIElement.OpacityProperty, 0, 0, 1.2, 0, 1.45, 1, 3.1, 1, 3.4, 0);
            if (!App.S.HoverOtherPips || !App.S.ControlOtherPips) bar.Visibility = Visibility.Collapsed;   // off: pointing at it does nothing
            var tag = new TextBlock { Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.8,
                Text = App.S.HoverOtherPips && App.S.ControlOtherPips ? "PiP Studio's title bar appears" : "Nothing happens" };
            EPut(c, ETag(tag), 6, 6);
        }

        /// <summary>The window styles side by side as tiny windows, the one you picked lifted and outlined.</summary>
        static void ScLooks(Canvas c, Storyboard sb)
        {
            c.Children.Clear();
            const double w = 100, h = 72, gap = 22;
            int n = Looks.Names.Length;
            double x0 = (SW - (n * w + (n - 1) * gap)) / 2, y = (SH - h) / 2 + 2;
            for (int i = 0; i < n; i++)
            {
                bool chosen = i == Looks.Current, xbox = i == Looks.Xbox;
                double r = xbox ? 9 : 6;
                Brush body = xbox ? (Brush)new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#0E0E0E"), (Color)ColorConverter.ConvertFromString("#0F1A10"), 90)
                                  : EHex(Theme.Dark ? "#202020" : "#F3F3F3");
                Brush nav = xbox ? EHex("#141414") : body;
                Brush tile = xbox ? EHex("#1C1C1C") : EHex(Theme.Dark ? "#2D2D2D" : "#FFFFFF");
                Brush sel = xbox ? EHex("#107C10") : EB("Accent");
                var g = new Grid { Width = w, Height = h };
                var frame = new Border { CornerRadius = new CornerRadius(r), Background = body, BorderThickness = new Thickness(1), BorderBrush = EHex("#55808080"), ClipToBounds = true };
                var inner = new Grid();
                inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
                inner.RowDefinitions.Add(new RowDefinition());
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                inner.ColumnDefinitions.Add(new ColumnDefinition());
                var nv = new StackPanel { Background = nav, Margin = new Thickness(0, 0, 0, 0) };
                for (int k = 0; k < 4; k++)                                // navigation items, the first one selected
                    nv.Children.Add(new Border { Height = 6, Margin = new Thickness(3, 3, 3, 0), CornerRadius = new CornerRadius(xbox ? 3 : 2),
                        Background = k == 0 ? sel : (xbox ? EHex("#2A2A2A") : EHex("#33808080")) });
                Grid.SetRow(nv, 1); inner.Children.Add(nv);
                var tiles = new StackPanel { Margin = new Thickness(5, 4, 5, 0) };
                for (int k = 0; k < 3; k++)
                {
                    bool focused = xbox && k == 1;                         // the Xbox look's white focus outline
                    tiles.Children.Add(new Border { Height = 12, Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(xbox ? 4 : 2),
                        Background = focused ? EHex("#1E9E1E") : tile, BorderThickness = new Thickness(focused ? 1.5 : 0), BorderBrush = Brushes.White });
                }
                Grid.SetRow(tiles, 1); Grid.SetColumn(tiles, 1); inner.Children.Add(tiles);
                frame.Child = inner;
                g.Children.Add(frame);
                if (chosen) g.Children.Add(new Border { CornerRadius = new CornerRadius(r + 2), BorderThickness = new Thickness(2), BorderBrush = xbox ? EHex("#107C10") : EB("Accent"), Margin = new Thickness(-3) });
                double x = x0 + i * (w + gap);
                EPut(c, g, x, y);
                var t = new TextBlock { Text = Looks.Names[i], Foreground = chosen ? (xbox ? EHex("#5FD35F") : EB("Accent")) : Brushes.Gray, FontSize = 9, FontWeight = chosen ? FontWeights.SemiBold : FontWeights.Normal, Width = w, TextAlignment = TextAlignment.Center };
                EPut(c, t, x, y + h + 6);
                if (chosen) EK(sb, g, Canvas.TopProperty, 0, y, 0.8, y - 6, 1.6, y);
            }
        }

        /// <summary>A phone (with a PiP card: buttons and a volume slider) next to a PC screen: tapping Corner on the phone sends
        /// the PiP to the other corner, dragging the slider turns the sound down.</summary>
        static void ScPhone(Canvas c, Storyboard sb)
        {
            c.Children.Clear();
            // the PC screen with a game and a PiP in a corner
            var pc = new Grid { Width = 150, Height = 92 };
            pc.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = EHex("#2B3140"), BorderBrush = EHex("#55808080"), BorderThickness = new Thickness(1) });
            EPut(c, pc, 14, 18);
            EPut(c, new Rectangle { Width = 40, Height = 4, Fill = EHex("#55808080"), RadiusX = 2, RadiusY = 2 }, 69, 114);
            var pip = EPip(44, 26);
            EPut(c, pip, 14 + 150 - 44 - 6, 18 + 92 - 26 - 6);
            EK(sb, pip, Canvas.LeftProperty, 0, 114, 1.3, 114, 1.7, 20, 4.2, 20);
            EK(sb, pip, Canvas.TopProperty, 0, 78, 1.3, 78, 1.7, 24, 4.2, 24);
            // the sound waves next to the PC, getting smaller when the slider moves
            var wave = new TextBlock { Text = "\uE995", FontFamily = Fluent.IconFont, FontSize = 16, Foreground = Brushes.White };
            EPut(c, wave, 132, 120);
            EK(sb, wave, UIElement.OpacityProperty, 0, .9, 2.4, .9, 3.2, .3, 4.2, .3);

            // the phone: a dark glass card with a title, three buttons and a slider
            var phone = new Grid { Width = 66, Height = 124 };
            phone.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = EHex("#0E0E10"), BorderBrush = EHex("#88FFFFFF"), BorderThickness = new Thickness(1.5) });
            EPut(c, phone, 182, 12);
            EPut(c, new Border { Width = 54, Height = 70, CornerRadius = new CornerRadius(7), Background = EHex("#26FFFFFF") }, 188, 30);
            EPut(c, new Rectangle { Width = 30, Height = 3, Fill = Brushes.White, Opacity = .8, RadiusX = 1.5, RadiusY = 1.5 }, 193, 36);
            var btns = new Border[3];
            for (int i = 0; i < 3; i++)
            {
                btns[i] = new Border { Width = 14, Height = 12, CornerRadius = new CornerRadius(4), Background = EHex("#33FFFFFF") };
                EPut(c, btns[i], 193 + i * 16, 46);
            }
            EK(sb, btns[0], UIElement.OpacityProperty, 0, 1, 1.0, 1, 1.15, .35, 1.4, 1);       // "Corner" pressed
            var track = new Rectangle { Width = 44, Height = 3, Fill = EHex("#40FFFFFF"), RadiusX = 1.5, RadiusY = 1.5 };
            EPut(c, track, 193, 80);
            var fill = new Rectangle { Height = 3, Fill = Brushes.White, RadiusX = 1.5, RadiusY = 1.5 };
            EPut(c, fill, 193, 80);
            EK(sb, fill, FrameworkElement.WidthProperty, 0, 36, 2.4, 36, 3.2, 12, 4.2, 12);
            var knob = new Border { Width = 11, Height = 7, CornerRadius = new CornerRadius(3.5), Background = Brushes.White };
            EPut(c, knob, 224, 78);
            EK(sb, knob, Canvas.LeftProperty, 0, 224, 2.4, 224, 3.2, 200, 4.2, 200);
            // a finger: tap Corner, then drag the slider
            var cur = ECursor(); EPut(c, cur, 236, 120);
            EK(sb, cur, Canvas.LeftProperty, 0, 236, 0.8, 199, 2.0, 199, 2.4, 229, 3.2, 205, 4.2, 236);
            EK(sb, cur, Canvas.TopProperty, 0, 120, 0.8, 52, 2.0, 52, 2.4, 81, 3.2, 81, 4.2, 120);
        }

        /// <summary>Pairing: the phone shows a PIN, it's typed into PiP Studio's dialog digit by digit, and both get a tick.</summary>
        static void ScPair(Canvas c, Storyboard sb)
        {
            c.Children.Clear();
            const string pin = "318472";
            // the phone with its PIN
            var phone = new Grid { Width = 70, Height = 124 };
            phone.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = EHex("#0E0E10"), BorderBrush = EHex("#88FFFFFF"), BorderThickness = new Thickness(1.5) });
            EPut(c, phone, 16, 13);
            for (int i = 0; i < 6; i++)
            {
                var t = new Border { Width = 9, Height = 13, CornerRadius = new CornerRadius(3), Background = EHex("#2EFFFFFF"),
                    Child = new TextBlock { Text = pin[i].ToString(), Foreground = Brushes.White, FontSize = 8, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                EPut(c, t, 22 + i * 10 + (i >= 3 ? 2 : 0), 64);
            }
            // PiP Studio's Pair a phone dialog: a box the digits appear in, one by one
            var dlg = new Border { Width = 128, Height = 82, CornerRadius = new CornerRadius(6), Background = EHex("#2A2A2E"), BorderBrush = EHex("#55808080"), BorderThickness = new Thickness(1) };
            EPut(c, dlg, 118, 34);
            EPut(c, new TextBlock { Text = "Pair a phone", Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.SemiBold }, 128, 42);
            EPut(c, new Border { Width = 70, Height = 16, CornerRadius = new CornerRadius(3), BorderBrush = EB("Accent"), BorderThickness = new Thickness(1), Background = EHex("#1A1A1E") }, 128, 62);
            for (int i = 0; i < 6; i++)
            {
                var d = new TextBlock { Text = pin[i].ToString(), Foreground = Brushes.White, FontSize = 9, Opacity = 0 };
                EPut(c, d, 133 + i * 10, 64);
                double t0 = 0.8 + i * 0.28;
                EK(sb, d, UIElement.OpacityProperty, 0, 0, t0, 0, t0 + 0.05, 1, 4.6, 1);
            }
            var btn = new Border { Width = 36, Height = 16, CornerRadius = new CornerRadius(3), Background = EB("Accent"),
                Child = new TextBlock { Text = "Pair", Foreground = Brushes.White, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            EPut(c, btn, 204, 62);
            EK(sb, btn, UIElement.OpacityProperty, 0, 1, 2.7, 1, 2.85, .5, 3.0, 1);
            // a dotted line between them, and a tick on both once it's paired
            var link = new Line { X1 = 88, Y1 = 75, X2 = 118, Y2 = 75, Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 3 }, Opacity = .45 };
            c.Children.Add(link);
            foreach (var at in new[] { new Point(44, 92), new Point(176, 90) })
            {
                var ok = new TextBlock { Text = "\uE73E", FontFamily = Fluent.IconFont, FontSize = 14, Foreground = EHex("#30D158"), Opacity = 0 };
                EPut(c, ok, at.X, at.Y);
                EK(sb, ok, UIElement.OpacityProperty, 0, 0, 3.1, 0, 3.3, 1, 4.6, 1);
            }
            var tag = new TextBlock { Text = "Type the phone's PIN", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.8 };
            EPut(c, ETag(tag), 6, 6);
        }

        /// <summary>The eight sizes, smallest to largest, yours highlighted.</summary>
        static void ScSize(Canvas c, Storyboard sb)
        {
            c.Children.Clear();
            double[] ws = { 14, 18, 21.5, 25, 29, 33, 39, 45 };  // same proportions as the real presets
            string[] names = { "XS", "S", "S+", "M", "M+", "L", "L+", "XL" };
            double total = 0; foreach (var v in ws) total += v; total += (ws.Length - 1) * 4;
            double x = (SW - total) / 2;
            for (int i = 0; i < ws.Length; i++)
            {
                double w = ws[i], h = w * 9 / 16;
                bool chosen = i == App.S.DefaultSize;
                var pip = EPip(w, h);
                if (chosen) pip.Children.Add(new Rectangle { Stroke = EB("Accent"), StrokeThickness = 2 });
                pip.Opacity = chosen ? 1 : 0.35;
                EPut(c, pip, x, SH - 40 - h);
                var t = new TextBlock { Text = names[i], Foreground = chosen ? EB("Accent") : Brushes.Gray, FontSize = 9, FontWeight = chosen ? FontWeights.SemiBold : FontWeights.Normal, Width = w + 4, TextAlignment = TextAlignment.Center };
                EPut(c, t, x - 2, SH - 32);
                if (chosen) EK(sb, pip, Canvas.TopProperty, 0, SH - 40 - h, 0.8, SH - 46 - h, 1.6, SH - 40 - h);
                x += w + 4;
            }
        }

        /// <summary>The pointer reaches a PiP and it does what you picked: controls, fade, nothing, or step aside.</summary>
        static void ScHover(Canvas c, Storyboard sb)
        {
            int mode = App.S.HoverAction;                              // 0 controls, 1 fade, 2 nothing, 3 move out of the way
            var p = ESpot(2, 2, 96, 54);
            var pip = EPip(96, 54); EPut(c, pip, p.X, p.Y);
            var cur = ECursor(); EPut(c, cur, 60, 40);
            double cx = p.X + 48, cy = p.Y + 30;
            EK(sb, cur, Canvas.LeftProperty, 0, 60, 0.5, 60, 1.3, cx, 2.9, cx, 3.6, 60, 4.2, 60);
            EK(sb, cur, Canvas.TopProperty, 0, 40, 0.5, 40, 1.3, cy, 2.9, cy, 3.6, 40, 4.2, 40);
            switch (mode)
            {
                case 0:
                    {
                        // the control bar fades in over the picture
                        var bar = new Grid { Height = 14, VerticalAlignment = VerticalAlignment.Top, Opacity = 0 };
                        bar.Children.Add(new Rectangle { Fill = EHex("#CC141418") });
                        var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) };
                        foreach (var g in new[] { "\uE71A", "\uE740", "\uE711" })
                            icons.Children.Add(new TextBlock { Text = g, FontFamily = Fluent.IconFont, FontSize = 7, Foreground = Brushes.White, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                        bar.Children.Add(icons);
                        pip.Children.Add(bar);
                        EK(sb, bar, UIElement.OpacityProperty, 0, 0, 1.2, 0, 1.4, 1, 2.9, 1, 3.2, 0);
                        break;
                    }
                case 1:
                    double to = Math.Max(0.1, App.S.HoverFadePercent / 100.0);
                    EK(sb, pip, UIElement.OpacityProperty, 0, 1, 1.1, 1, 1.5, to, 2.9, to, 3.3, 1, 4.2, 1);
                    break;
                case 3:
                    // it slides across to the other side (right → left) and stays there, like the real PiP
                    EK(sb, pip, Canvas.LeftProperty, 0, p.X, 1.2, p.X, 1.7, ESpot(0, 2, 96, 54).X, 4.2, ESpot(0, 2, 96, 54).X);
                    break;
            }
            var tag = new TextBlock { Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.8,
                Text = mode == 0 ? "Shows controls" : mode == 1 ? "Fades to " + App.S.HoverFadePercent + "%" : mode == 2 ? "Nothing happens" : "Moves out of the way" };
            EPut(c, ETag(tag), 6, 6);
        }

        /// <summary>A PiP moving to a new spot: gliding with animations on, jumping with them off.</summary>
        static void ScAnims(Canvas c, Storyboard sb)
        {
            bool on = App.S.Animations && !App.S.LiteMode;
            var a = ESpot(0, 2, 84, 48); var b = ESpot(2, 2, 84, 48);
            var pip = EPip(); EPut(c, pip, a.X, a.Y);
            var tag = new TextBlock { Text = on ? "Glides into place" : "Jumps into place", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.8 };
            EPut(c, ETag(tag), 6, 6);
            if (on)
            {
                EK(sb, pip, Canvas.LeftProperty, 0, a.X, 0.8, a.X, 1.6, b.X, 2.8, b.X, 3.6, a.X);
                return;
            }
            var jx = new DoubleAnimationUsingKeyFrames();
            jx.KeyFrames.Add(new DiscreteDoubleKeyFrame(a.X, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            jx.KeyFrames.Add(new DiscreteDoubleKeyFrame(b.X, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))));
            jx.KeyFrames.Add(new DiscreteDoubleKeyFrame(a.X, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.0))));
            jx.KeyFrames.Add(new DiscreteDoubleKeyFrame(a.X, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.6))));
            Storyboard.SetTarget(jx, pip); Storyboard.SetTargetProperty(jx, new PropertyPath(Canvas.LeftProperty));
            sb.Children.Add(jx);
        }

        /// <summary>A tall app and a wide app growing: each stops at your height or width limit (or keeps going with no limit).</summary>
        static void ScLimits(Canvas c, Storyboard sb)
        {
            c.Children.Clear();
            const double m = 8;
            double usableH = SH - 2 * m, usableW = SW - 2 * m;
            double maxH = App.S.LimitHeight ? usableH * App.S.MaxHeightPercent / 100.0 : usableH;
            double maxW = App.S.LimitWidth ? usableW * App.S.MaxWidthPercent / 100.0 : usableW;
            if (App.S.LimitHeight)
            {
                EPut(c, new Rectangle { Width = SW - 2 * m, Height = 1, Stroke = EB("Accent"), StrokeDashArray = new DoubleCollection { 4, 3 } }, m, SH - m - maxH);
                EPut(c, ETag(new TextBlock { Text = "Max height " + App.S.MaxHeightPercent + "%", Foreground = Brushes.White, FontSize = 9 }), m + 2, SH - m - maxH - 19);
            }
            if (App.S.LimitWidth)
            {
                EPut(c, new Rectangle { Width = 1, Height = SH - 2 * m, Stroke = EB("Accent"), StrokeDashArray = new DoubleCollection { 4, 3 } }, SW - m - maxW, m);
                EPut(c, ETag(new TextBlock { Text = "Max width " + App.S.MaxWidthPercent + "%", Foreground = Brushes.White, FontSize = 9 }), SW - m - maxW + 4, m);
            }
            // a tall app (phone shape), anchored bottom right
            var tall = EPip(18, 32); EPut(c, tall, SW - m - 18, SH - m - 32);
            double th = Math.Min(usableH, maxH), tw = Math.Min(th * 0.56, maxW); th = tw / 0.56;
            EK(sb, tall, UIElement.OpacityProperty, 0, 1, 2.6, 1, 2.8, 0, 5.2, 0, 5.4, 1);
            EK(sb, tall, FrameworkElement.HeightProperty, 0, 32, 0.6, 32, 1.8, th, 2.6, th, 5.4, 32);
            EK(sb, tall, FrameworkElement.WidthProperty, 0, 18, 0.6, 18, 1.8, tw, 2.6, tw, 5.4, 18);
            EK(sb, tall, Canvas.TopProperty, 0, SH - m - 32, 0.6, SH - m - 32, 1.8, SH - m - th, 2.6, SH - m - th, 5.4, SH - m - 32);
            EK(sb, tall, Canvas.LeftProperty, 0, SW - m - 18, 0.6, SW - m - 18, 1.8, SW - m - tw, 2.6, SW - m - tw, 5.4, SW - m - 18);
            // a wide app (ultrawide shape)
            var wide = EPip(48, 16); wide.Opacity = 0; EPut(c, wide, SW - m - 48, SH - m - 16);
            double ww = Math.Min(usableW, maxW), wh = Math.Min(ww / 3, maxH); ww = wh * 3;
            EK(sb, wide, UIElement.OpacityProperty, 0, 0, 2.7, 0, 2.9, 1, 5.2, 1, 5.4, 0);
            EK(sb, wide, FrameworkElement.WidthProperty, 0, 48, 3.2, 48, 4.4, ww, 5.4, ww);
            EK(sb, wide, FrameworkElement.HeightProperty, 0, 16, 3.2, 16, 4.4, wh, 5.4, wh);
            EK(sb, wide, Canvas.LeftProperty, 0, SW - m - 48, 3.2, SW - m - 48, 4.4, SW - m - ww, 5.4, SW - m - ww);
            EK(sb, wide, Canvas.TopProperty, 0, SH - m - 16, 3.2, SH - m - 16, 4.4, SH - m - wh, 5.4, SH - m - wh);
        }

        /// <summary>Opening a second window: a second PiP appears (allowed) or the one PiP switches to it (not allowed).</summary>
        static void ScMulti(Canvas c, Storyboard sb)
        {
            bool many = App.S.AllowMultiple;
            var a = ESpot(2, 2, 76, 43);
            var first = EPip(76, 43, "A"); EPut(c, first, a.X, a.Y);
            var tag = new TextBlock { Text = many ? "A second PiP opens" : "The PiP switches to the new window", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.8 };
            EPut(c, ETag(tag), 6, 6);
            if (many)
            {
                var b = ESpot(0, 2, 76, 43);
                var second = EPip(76, 43, "B"); second.Opacity = 0; EPut(c, second, b.X, b.Y + 12);
                EK(sb, second, UIElement.OpacityProperty, 0, 0, 1.0, 0, 1.4, 1, 3.2, 1, 3.6, 0);
                EK(sb, second, Canvas.TopProperty, 0, b.Y + 12, 1.0, b.Y + 12, 1.5, b.Y, 3.6, b.Y);
            }
            else
            {
                var swap = EPip(76, 43, "B"); swap.Opacity = 0; EPut(c, swap, a.X, a.Y);
                EK(sb, swap, UIElement.OpacityProperty, 0, 0, 1.0, 0, 1.4, 1, 3.2, 1, 3.6, 0);
            }
        }

        /// <summary>Right-clicking a PiP does what you picked: menu, close, show its window, theater, or nothing.</summary>
        static void ScRightClick(Canvas c, Storyboard sb)
        {
            int mode = App.S.RightClickAction;                         // 0 menu, 1 close, 2 bring source to front, 3 theater, 4 nothing
            var src = EWin(20, 24, 120, 74, "#3A3F4B", "#4A5060");
            var p = ESpot(2, 2, 84, 48);
            var pip = EPip(); EPut(c, pip, p.X, p.Y);
            Panel.SetZIndex(pip, 3);
            var cur = ECursor(); EPut(c, cur, 40, 30); Panel.SetZIndex(cur, 10);
            double cx = p.X + 40, cy = p.Y + 22;
            EK(sb, cur, Canvas.LeftProperty, 0, 40, 0.4, 40, 1.2, cx, 3.2, cx, 3.8, 40, 4.4, 40);
            EK(sb, cur, Canvas.TopProperty, 0, 30, 0.4, 30, 1.2, cy, 3.2, cy, 3.8, 30, 4.4, 30);
            var ring = new Ellipse { Width = 22, Height = 22, Stroke = Brushes.White, StrokeThickness = 1.5, Opacity = 0 };
            EPut(c, ring, cx - 11, cy - 11); Panel.SetZIndex(ring, 9);
            EK(sb, ring, UIElement.OpacityProperty, 0, 0, 1.3, 0, 1.35, 1, 1.8, 0);
            var tag = new TextBlock { Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.8,
                Text = mode == 0 ? "Right-click: menu" : mode == 1 ? "Right-click: close" : mode == 2 ? "Right-click: show its window" : mode == 3 ? "Right-click: theater mode" : "Right-click: nothing" };
            EPut(c, ETag(tag), 6, 6);
            switch (mode)
            {
                case 0:
                    {
                        var menu = new Border { Width = 64, CornerRadius = new CornerRadius(4), Background = EHex("#2C2C2C"), BorderBrush = EHex("#454545"), BorderThickness = new Thickness(1), Padding = new Thickness(6, 5, 6, 5), Opacity = 0 };
                        var lines = new StackPanel();
                        for (int i = 0; i < 4; i++) lines.Children.Add(new Rectangle { Height = 3, Width = i == 1 ? 34 : 46, Fill = Brushes.White, Opacity = 0.7, Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Left });
                        menu.Child = lines;
                        EPut(c, menu, cx - 66, cy - 58); Panel.SetZIndex(menu, 8);
                        EK(sb, menu, UIElement.OpacityProperty, 0, 0, 1.4, 0, 1.6, 1, 3.0, 1, 3.2, 0);
                        break;
                    }
                case 1:
                    EK(sb, pip, UIElement.OpacityProperty, 0, 1, 1.4, 1, 1.8, 0, 3.6, 0, 4.0, 1);
                    break;
                case 2:
                    {
                        var front = EWin(20, 24, 120, 74, "#465068", "#5B6785");
                        var bar = new Rectangle { Width = 120, Height = 9, Fill = EB("Accent"), RadiusX = 3, RadiusY = 3 };
                        front.Opacity = 0; bar.Opacity = 0;
                        c.Children.Add(front); EPut(c, bar, 20, 24);
                        Panel.SetZIndex(front, 4); Panel.SetZIndex(bar, 5);
                        EK(sb, front, UIElement.OpacityProperty, 0, 0, 1.4, 0, 1.8, 1, 3.4, 1, 3.8, 0);
                        EK(sb, bar, UIElement.OpacityProperty, 0, 0, 1.4, 0, 1.8, 1, 3.4, 1, 3.8, 0);
                        break;
                    }
                case 3:
                    {
                        double w = SW * 0.62, h = w * 48 / 84;
                        EK(sb, pip, FrameworkElement.WidthProperty, 0, 84, 1.4, 84, 2.0, w, 3.4, w, 4.0, 84);
                        EK(sb, pip, FrameworkElement.HeightProperty, 0, 48, 1.4, 48, 2.0, h, 3.4, h, 4.0, 48);
                        EK(sb, pip, Canvas.LeftProperty, 0, p.X, 1.4, p.X, 2.0, (SW - w) / 2, 3.4, (SW - w) / 2, 4.0, p.X);
                        EK(sb, pip, Canvas.TopProperty, 0, p.Y, 1.4, p.Y, 2.0, (SH - h) / 2, 3.4, (SH - h) / 2, 4.0, p.Y);
                        break;
                    }
            }
        }

        // ---------------------------------------------------------------- scenes
        /// <summary>A tiny PiP Studio window: side menu, title, a couple of setting cards with switches.</summary>
        static Grid EMiniApp(bool dark, Brush accent)
        {
            Brush bg = EHex(dark ? "#202020" : "#F3F3F3"), card = EHex(dark ? "#2B2B2B" : "#FFFFFF"), line = EHex(dark ? "#3A3A3A" : "#E5E5E5"),
                  text = EHex(dark ? "#FFFFFF" : "#1A1A1A"), sub = EHex(dark ? "#9A9A9A" : "#6B6B6B");
            var g = new Grid { Width = 200, Height = 120 };
            g.Children.Add(new Rectangle { Fill = bg, RadiusX = 6, RadiusY = 6, Stroke = line, StrokeThickness = 1 });
            var cv = new Canvas();
            g.Children.Add(cv);
            for (int i = 0; i < 4; i++) EPut(cv, new Rectangle { Width = 34, Height = 4, Fill = sub, RadiusX = 2, RadiusY = 2, Opacity = 0.7 }, 14, 24 + i * 16);
            EPut(cv, new Rectangle { Width = 3, Height = 10, Fill = accent, RadiusX = 1.5, RadiusY = 1.5 }, 8, 21);
            EPut(cv, new Rectangle { Width = 60, Height = 7, Fill = text, RadiusX = 3, RadiusY = 3 }, 62, 14);
            for (int i = 0; i < 2; i++)
            {
                double y = 32 + i * 34;
                EPut(cv, new Rectangle { Width = 126, Height = 28, Fill = card, RadiusX = 4, RadiusY = 4, Stroke = line, StrokeThickness = 1 }, 62, y);
                EPut(cv, new Rectangle { Width = 50, Height = 4, Fill = text, RadiusX = 2, RadiusY = 2, Opacity = 0.85 }, 70, y + 9);
                EPut(cv, new Rectangle { Width = 36, Height = 3, Fill = sub, RadiusX = 1.5, RadiusY = 1.5 }, 70, y + 17);
                bool on = i == 0;
                EPut(cv, new Rectangle { Width = 20, Height = 11, RadiusX = 5.5, RadiusY = 5.5, Fill = on ? accent : Brushes.Transparent, Stroke = on ? accent : sub, StrokeThickness = 1 }, 160, y + 8.5);
                EPut(cv, new Ellipse { Width = 7, Height = 7, Fill = on ? (dark ? Brushes.Black : Brushes.White) : sub }, on ? 170 : 162, y + 10.5);
            }
            return g;
        }

        /// <summary>The app crossfading between light and dark (or showing the one you picked).</summary>
        static void ScTheme(Canvas c, Storyboard sb)
        {
            c.Children.Clear();
            var light = EMiniApp(false, EB("Accent")); var dark = EMiniApp(true, EB("Accent"));
            EPut(c, light, (SW - 200) / 2, (SH - 120) / 2);
            EPut(c, dark, (SW - 200) / 2, (SH - 120) / 2);
            int mode = App.S.ThemeMode;                                // 0 follow Windows, 1 light, 2 dark
            var tag = new TextBlock { Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold, Text = mode == 0 ? "Follows Windows" : mode == 1 ? "Light" : "Dark" };
            EPut(c, ETag(tag), 6, SH - 24);
            if (mode == 1) { dark.Opacity = 0; return; }
            if (mode == 2) return;
            EK(sb, dark, UIElement.OpacityProperty, 0, 0, 1.2, 0, 1.8, 1, 3.4, 1, 4.0, 0);
        }

        /// <summary>The same window cycling through accent colors.</summary>
        static void ScAccent(Canvas c, Storyboard sb)
        {
            c.Children.Clear();
            string[] cols = { "#0078D4", "#E3008C", "#00B294", "#FF8C00" };
            var mine = EMiniApp(true, EB("Accent"));
            EPut(c, mine, (SW - 200) / 2, (SH - 120) / 2);
            double t = 1.4;
            foreach (var col in cols)
            {
                var v = EMiniApp(true, EHex(col)); v.Opacity = 0;
                EPut(c, v, (SW - 200) / 2, (SH - 120) / 2);
                EK(sb, v, UIElement.OpacityProperty, 0, 0, t, 0, t + 0.3, 1, t + 1.1, 1, t + 1.4, 0, 8.0, 0);
                t += 1.4;
            }
        }


        /// <summary>See-through window with a shadow, then the plain Lite version.</summary>
        static void ScLite(Canvas c, Storyboard sb)
        {
            var full = EMiniApp(true, EB("Accent"));
            full.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Opacity = 0.6 };
            ((Rectangle)full.Children[0]).Fill = new SolidColorBrush(Color.FromArgb(200, 32, 32, 40));
            var lite = EMiniApp(true, EB("Accent")); lite.Opacity = 0;
            EPut(c, full, (SW - 200) / 2, (SH - 120) / 2);
            EPut(c, lite, (SW - 200) / 2, (SH - 120) / 2);
            var tagBox = ETag(new TextBlock { Text = "Lite", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold });
            tagBox.Opacity = 0;
            EPut(c, tagBox, 6, SH - 24);
            EK(sb, lite, UIElement.OpacityProperty, 0, 0, 1.4, 0, 1.8, 1, 3.4, 1, 3.8, 0);
            EK(sb, tagBox, UIElement.OpacityProperty, 0, 0, 1.4, 0, 1.8, 1, 3.4, 1, 3.8, 0);
        }

        /// <summary>The pointer clicks an arrow on the PiP and it hops to the next spot.</summary>
        static void ScArrows(Canvas c, Storyboard sb)
        {
            var pip = EPip(96, 54);
            var arrow = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = EHex("#CC141418"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) };
            arrow.Child = new Path { Data = Geometry.Parse("M0,0 L5,4 L0,8"), Stroke = Brushes.White, StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            pip.Children.Add(arrow);
            var a = ESpot(0, 2, 96, 54); var b = ESpot(1, 2, 96, 54);
            EPut(c, pip, a.X, a.Y);
            var cur = ECursor(); EPut(c, cur, 40, 30);
            double ax = a.X + 96 - 12, ay = a.Y + 27;
            EK(sb, cur, Canvas.LeftProperty, 0, 40, 0.4, 40, 1.2, ax, 1.6, ax, 2.0, ax + (b.X - a.X), 3.0, ax + (b.X - a.X), 3.6, 40);
            EK(sb, cur, Canvas.TopProperty, 0, 30, 0.4, 30, 1.2, ay, 3.0, ay, 3.6, 30);
            EK(sb, pip, Canvas.LeftProperty, 0, a.X, 1.6, a.X, 2.0, b.X, 3.2, b.X, 3.6, a.X);
        }

        /// <summary>A new PiP pops up at the chosen corner.</summary>
        static void ScPosition(Canvas c, Storyboard sb)
        {
            string tag = PipWindow.StartTag();                         // TL, T, TR, L, R, BL, B, BR
            int gx = tag.EndsWith("L") ? 0 : tag.EndsWith("R") ? 2 : 1, gy = tag.StartsWith("T") ? 0 : tag.StartsWith("B") ? 2 : 1;
            var p = ESpot(gx, gy, 84, 48);
            double offY = gy == 2 ? 14 : gy == 0 ? -14 : 0, offX = gy == 1 ? (gx == 2 ? 14 : -14) : 0;
            var pip = EPip(); EPut(c, pip, p.X + offX, p.Y + offY);
            pip.Opacity = 0;
            EK(sb, pip, UIElement.OpacityProperty, 0, 0, 0.5, 0, 0.9, 1, 2.6, 1);
            EK(sb, pip, Canvas.TopProperty, 0, p.Y + offY, 0.5, p.Y + offY, 1.0, p.Y);
            EK(sb, pip, Canvas.LeftProperty, 0, p.X + offX, 0.5, p.X + offX, 1.0, p.X);
        }


        static void ScMargin(Canvas c, Storyboard sb)
        {
            var pip = EPip();
            EPut(c, pip, SW - 84 - 2, SH - 48 - 2);
            // guides showing the gap
            var gx = new Rectangle { Height = 1.5, Fill = EB("Accent") };
            var gy = new Rectangle { Width = 1.5, Fill = EB("Accent") };
            EPut(c, gx, 0, SH - 26); EPut(c, gy, SW - 44, 0);
            EK(sb, pip, Canvas.LeftProperty, 0, SW - 86, 0.6, SW - 86, 1.3, SW - 84 - 22, 2.6, SW - 84 - 22, 3.3, SW - 86);
            EK(sb, pip, Canvas.TopProperty, 0, SH - 50, 0.6, SH - 50, 1.3, SH - 48 - 22, 2.6, SH - 48 - 22, 3.3, SH - 50);
            EK(sb, gx, Canvas.LeftProperty, 0, SW - 2, 0.6, SW - 2, 1.3, SW - 22, 2.6, SW - 22, 3.3, SW - 2);
            EK(sb, gx, FrameworkElement.WidthProperty, 0, 2, 0.6, 2, 1.3, 22, 2.6, 22, 3.3, 2);
            EK(sb, gy, Canvas.TopProperty, 0, SH - 2, 0.6, SH - 2, 1.3, SH - 22, 2.6, SH - 22, 3.3, SH - 2);
            EK(sb, gy, FrameworkElement.HeightProperty, 0, 2, 0.6, 2, 1.3, 22, 2.6, 22, 3.3, 2);
        }


        static void ScGhost(Canvas c, Storyboard sb)
        {
            c.Children.Add(EWin(98, 52, 140, 84, "#2F343F", "#40465A"));   // the window behind the PiP
            var hit = new Rectangle { Width = 140, Height = 84, Stroke = EB("Accent"), StrokeThickness = 2, RadiusX = 3, RadiusY = 3, Opacity = 0 };
            EPut(c, hit, 98, 52);
            var pip = EPip(); pip.Opacity = 0.45;
            var p = ESpot(2, 2, 84, 48);
            EPut(c, pip, p.X, p.Y);
            var ring = new Ellipse { Width = 6, Height = 6, Stroke = Brushes.White, StrokeThickness = 1.5, Opacity = 0 };
            EPut(c, ring, p.X + 40, p.Y + 20);
            var cur = ECursor();
            EPut(c, cur, 40, 30);
            EK(sb, cur, Canvas.LeftProperty, 0, 40, 0.4, 40, 1.2, p.X + 40, 3.4, p.X + 40, 4.0, 40);
            EK(sb, cur, Canvas.TopProperty, 0, 30, 0.4, 30, 1.2, p.Y + 20, 3.4, p.Y + 20, 4.0, 30);
            // click: a ripple, and the window *behind* the PiP lights up
            EK(sb, ring, UIElement.OpacityProperty, 0, 0, 1.5, 0, 1.55, 1, 2.2, 0, 4.0, 0);
            EK(sb, ring, FrameworkElement.WidthProperty, 0, 6, 1.5, 6, 2.2, 26, 4.0, 26);
            EK(sb, ring, FrameworkElement.HeightProperty, 0, 6, 1.5, 6, 2.2, 26, 4.0, 26);
            EK(sb, ring, Canvas.LeftProperty, 0, p.X + 40, 1.5, p.X + 40, 2.2, p.X + 30, 4.0, p.X + 30);
            EK(sb, ring, Canvas.TopProperty, 0, p.Y + 20, 1.5, p.Y + 20, 2.2, p.Y + 10, 4.0, p.Y + 10);
            EK(sb, hit, UIElement.OpacityProperty, 0, 0, 1.55, 0, 1.7, 1, 3.4, 1, 3.8, 0, 4.0, 0);
        }

        static void ScCorners(Canvas c, Storyboard sb)
        {
            var g = new Grid { Width = 140, Height = 79 };
            var r = new Rectangle { Fill = EB("Accent") };
            g.Children.Add(r);
            EPut(c, g, (SW - 140) / 2, (SH - 79) / 2);
            double rad = App.S.RoundedCorners ? 10 : 0;
            var tagC = new TextBlock { Text = App.S.RoundedCorners ? "Rounded" : "Square", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold };
            EPut(c, ETag(tagC), 6, 6);
            r.RadiusX = r.RadiusY = rad;
            EK(sb, r, Rectangle.RadiusXProperty, 0, rad, 1.2, rad, 1.6, 10 - rad, 2.6, 10 - rad, 3.0, rad, 3.6, rad);
            EK(sb, r, Rectangle.RadiusYProperty, 0, rad, 1.2, rad, 1.6, 10 - rad, 2.6, 10 - rad, 3.0, rad, 3.6, rad);
        }

        static void ScBorder(Canvas c, Storyboard sb)
        {
            var g = EPip(120, 68);
            var line = new Rectangle { Stroke = App.S.BorderUseAccent ? EB("Accent") : new SolidColorBrush(Color.FromRgb(App.S.BorderColor.R, App.S.BorderColor.G, App.S.BorderColor.B)), StrokeThickness = 0 };
            if (!App.S.BorderUseAccent && App.S.BorderColor.GetBrightness() < 0.15) line.Stroke = Brushes.Black;
            g.Children.Add(line);
            EPut(c, g, (SW - 120) / 2, (SH - 68) / 2);
            EK(sb, line, Shape.StrokeThicknessProperty, 0, 0, 0.8, 0, 1.3, 3, 2.6, 3, 3.1, 0);
        }

        static void ScMessages(Canvas c, Storyboard sb)
        {
            var p = ESpot(1, 0, 110, 62);
            var pip = EPip(110, 62);
            EPut(c, pip, p.X, 18);
            var pill = new Border { CornerRadius = new CornerRadius(9), Background = EHex("#141418"), Padding = new Thickness(10, 3, 10, 4), Opacity = 0 };
            pill.Child = new TextBlock { Text = "Volume 60%", Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.SemiBold };
            EPut(c, pill, p.X + 18, 18 + 62 + 6);
            var bar = new Rectangle { Height = 3, Width = 0, Fill = EB("Accent"), RadiusX = 1.5, RadiusY = 1.5 };
            EPut(c, bar, p.X + 18, 18 + 62 + 30);
            EK(sb, pill, UIElement.OpacityProperty, 0, 0, 0.6, 0, 0.9, 1, 2.6, 1, 3.0, 0, 3.6, 0);
            EK(sb, bar, FrameworkElement.WidthProperty, 0, 0, 0.9, 0, 1.8, 44, 2.6, 44, 3.0, 0, 3.6, 0);
        }

        static void ScFollow(Canvas c, Storyboard sb)
        {
            // two windows; the one you're in has an accent title bar, the PiP shows the other
            var a = EWin(12, 22, 96, 66, "#3A3F4B", "#4A5060"); var b = EWin(116, 22, 96, 66, "#3A3F4B", "#4A5060");
            c.Children.Add(a); c.Children.Add(b);
            var la = new TextBlock { Text = "A", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold }; EPut(c, la, 54, 46);
            var lb = new TextBlock { Text = "B", Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold }; EPut(c, lb, 158, 46);
            var barA = new Rectangle { Width = 96, Height = 9, Fill = EB("Accent"), RadiusX = 3, RadiusY = 3 }; EPut(c, barA, 12, 22);
            var barB = new Rectangle { Width = 96, Height = 9, Fill = EB("Accent"), RadiusX = 3, RadiusY = 3, Opacity = 0 }; EPut(c, barB, 116, 22);
            var pipA = EPip(70, 40, "A"); var pipB = EPip(70, 40, "B");
            var p = ESpot(2, 2, 70, 40);
            EPut(c, pipB, p.X, p.Y); EPut(c, pipA, p.X, p.Y);
            pipA.Opacity = 0;
            EK(sb, barA, UIElement.OpacityProperty, 0, 1, 1.6, 1, 1.8, 0, 3.4, 0, 3.6, 1);
            EK(sb, barB, UIElement.OpacityProperty, 0, 0, 1.6, 0, 1.8, 1, 3.4, 1, 3.6, 0);
            EK(sb, pipA, UIElement.OpacityProperty, 0, 0, 1.8, 0, 2.1, 1, 3.6, 1, 3.9, 0);
            var cur = ECursor(); EPut(c, cur, 60, 70);
            EK(sb, cur, Canvas.LeftProperty, 0, 60, 1.0, 60, 1.6, 160, 2.6, 160, 3.2, 60, 3.6, 60);
            EK(sb, cur, Canvas.TopProperty, 0, 70, 1.0, 70, 1.6, 70, 3.6, 70);
        }

        static void ScHideSrc(Canvas c, Storyboard sb)
        {
            var a = EWin(20, 22, 130, 80, "#3A3F4B", "#4A5060");
            c.Children.Add(a);
            var bar = new Rectangle { Width = 130, Height = 9, Fill = EB("Accent"), RadiusX = 3, RadiusY = 3, Opacity = 0 }; EPut(c, bar, 20, 22);
            var p = ESpot(2, 2, 84, 48);
            var pip = EPip(); EPut(c, pip, p.X, p.Y);
            EK(sb, bar, UIElement.OpacityProperty, 0, 0, 1.2, 0, 1.4, 1, 2.8, 1, 3.0, 0);
            EK(sb, pip, UIElement.OpacityProperty, 0, 1, 1.4, 1, 1.7, 0, 2.9, 0, 3.2, 1, 4.0, 1);
            EK(sb, pip, Canvas.TopProperty, 0, p.Y, 1.4, p.Y, 1.7, p.Y + 10, 2.9, p.Y + 10, 3.2, p.Y);
        }

        static void ScFullscreen(Canvas c, Storyboard sb)
        {
            var game = new Rectangle { Fill = EHex("#1F6F4A"), RadiusX = 3, RadiusY = 3 };
            EPut(c, game, 30, 30); game.Width = 110; game.Height = 64;
            var p = ESpot(2, 2, 84, 48);
            var pip = EPip(); EPut(c, pip, p.X, p.Y);
            EK(sb, game, Canvas.LeftProperty, 0, 30, 0.8, 30, 1.4, 0, 2.8, 0, 3.4, 30);
            EK(sb, game, Canvas.TopProperty, 0, 30, 0.8, 30, 1.4, 0, 2.8, 0, 3.4, 30);
            EK(sb, game, FrameworkElement.WidthProperty, 0, 110, 0.8, 110, 1.4, SW, 2.8, SW, 3.4, 110);
            EK(sb, game, FrameworkElement.HeightProperty, 0, 64, 0.8, 64, 1.4, SH, 2.8, SH, 3.4, 64);
            EK(sb, pip, UIElement.OpacityProperty, 0, 1, 1.2, 1, 1.4, 0, 3.0, 0, 3.4, 1, 4.0, 1);
            Panel.SetZIndex(pip, 5);
        }


        static void ScOpacity(Canvas c, Storyboard sb)
        {
            c.Children.Add(EWin(40, 24, 150, 96, "#2F343F", "#40465A"));   // something behind it to see through to
            var pip = EPip(120, 68);
            EPut(c, pip, 80, 44);
            var t = new TextBlock { Text = App.S.DefaultOpacityPercent + "%", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold };
            EPut(c, ETag(t), 6, SH - 24);
            EK(sb, pip, UIElement.OpacityProperty, 0, 1, 0.8, 1, 1.6, App.S.DefaultOpacityPercent < 95 ? App.S.DefaultOpacityPercent / 100.0 : 0.6, 2.8, App.S.DefaultOpacityPercent < 95 ? App.S.DefaultOpacityPercent / 100.0 : 0.6, 3.6, 1);
        }

        static void ScTheater(Canvas c, Storyboard sb)
        {
            var g = EPip(84, 48);
            var p = ESpot(2, 2, 84, 48);
            EPut(c, g, p.X, p.Y);
            double w = SW * 0.62, h = w * 48 / 84;
            EK(sb, g, FrameworkElement.WidthProperty, 0, 84, 0.8, 84, 1.5, w, 2.9, w, 3.6, 84);
            EK(sb, g, FrameworkElement.HeightProperty, 0, 48, 0.8, 48, 1.5, h, 2.9, h, 3.6, 48);
            EK(sb, g, Canvas.LeftProperty, 0, p.X, 0.8, p.X, 1.5, (SW - w) / 2, 2.9, (SW - w) / 2, 3.6, p.X);
            EK(sb, g, Canvas.TopProperty, 0, p.Y, 0.8, p.Y, 1.5, (SH - h) / 2, 2.9, (SH - h) / 2, 3.6, p.Y);
        }

        static void ScMinimized(Canvas c, Storyboard sb)
        {
            var task = new Rectangle { Width = SW, Height = 12, Fill = EHex("#202228") }; EPut(c, task, 0, SH - 12);
            var w = EWin(20, 20, 120, 76, "#3A3F4B", "#4A5060");
            c.Children.Add(w);
            var p = ESpot(2, 0, 84, 48);
            var pip = EPip(); EPut(c, pip, p.X, p.Y);
            var prog = new Rectangle { Height = 2, Width = 4, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 5) };
            pip.Children.Add(prog);
            EK(sb, prog, FrameworkElement.WidthProperty, 0, 4, 4.0, 72);
            // the window drops into the taskbar and comes back; the PiP keeps playing
            EK(sb, w, UIElement.OpacityProperty, 0, 1, 0.8, 1, 1.3, 0, 2.8, 0, 3.3, 1, 4.0, 1);
            EK(sb, w, Canvas.TopProperty, 0, 20, 0.8, 20, 1.3, SH - 10, 2.8, SH - 10, 3.3, 20);
        }


        /// <summary>A round app badge (like an app icon) for the "other app" scene.</summary>
        static Grid EBadge(double size)
        {
            var g = new Grid { Width = size, Height = size };
            g.Children.Add(new Ellipse { Fill = EHex("#E8E8EC") });
            g.Children.Add(new Ellipse { Fill = EHex("#4A7FE0"), Margin = new Thickness(size * 0.22) });
            return g;
        }

        static void ScExternal(Canvas c, Storyboard sb)
        {
            // the app's own window (a browser) behind, with its icon in the title bar
            var app = new Grid { Width = 150, Height = 96 };
            app.Children.Add(new Rectangle { Fill = EHex("#2F343F"), RadiusX = 4, RadiusY = 4 });
            app.Children.Add(new Rectangle { Fill = EHex("#40465A"), Height = 14, VerticalAlignment = VerticalAlignment.Top, RadiusX = 4, RadiusY = 4 });
            var appIcon = EBadge(10); appIcon.HorizontalAlignment = HorizontalAlignment.Left; appIcon.VerticalAlignment = VerticalAlignment.Top; appIcon.Margin = new Thickness(4, 2, 0, 0);
            app.Children.Add(appIcon);
            app.Children.Add(new Rectangle { Fill = EHex("#3A4050"), Margin = new Thickness(10, 22, 10, 22), RadiusX = 2, RadiusY = 2 });   // where the video was
            EPut(c, app, 12, 14);

            // its PiP: the same icon on the PiP's own title bar
            const double w = 84, h = 52;
            var g = new Grid { Width = w, Height = h };
            g.Children.Add(new Rectangle { Fill = EB("Accent") });
            g.Children.Add(new Rectangle { Height = 11, Fill = EHex("#202124"), VerticalAlignment = VerticalAlignment.Top });
            var pipIcon = EBadge(8); pipIcon.HorizontalAlignment = HorizontalAlignment.Left; pipIcon.VerticalAlignment = VerticalAlignment.Top; pipIcon.Margin = new Thickness(3, 1.5, 0, 0);
            g.Children.Add(pipIcon);
            g.Children.Add(new Path { Data = Geometry.Parse("M0,0 L9,5.5 L0,11 Z"), Fill = Brushes.White, Opacity = 0.9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 11, 0, 0) });
            Panel.SetZIndex(g, 3);

            // a dotted line ties the PiP to the window it came from
            var link = new Line { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 3 }, Opacity = 0.45 };
            c.Children.Add(link);

            var start = new Point(112, 62);                               // popped out over the window
            var p = ESpot(2, 2, w, h);
            EPut(c, g, start.X, start.Y);
            var pill = ETag(new TextBlock { Text = "Browser's own PiP", Foreground = Brushes.White, FontSize = 9.5, FontWeight = FontWeights.SemiBold });
            pill.Opacity = 0;
            EPut(c, pill, p.X - 22, p.Y - 22);
            Panel.SetZIndex(pill, 4);

            link.X1 = 12 + 9; link.Y1 = 14 + 7;                          // from the window's icon
            EK(sb, link, Line.X2Property, 0, start.X + 7, 0.8, start.X + 7, 1.6, p.X - 10, 1.9, p.X - 10, 2.4, p.X + 7);
            EK(sb, link, Line.Y2Property, 0, start.Y + 5, 0.8, start.Y + 5, 1.6, p.Y - 6, 1.9, p.Y - 6, 2.4, p.Y + 5);
            // dragged loosely, then it glides onto PiP Studio's spot and gets labelled
            EK(sb, g, Canvas.LeftProperty, 0, start.X, 0.8, start.X, 1.6, p.X - 17, 1.9, p.X - 17, 2.4, p.X);
            EK(sb, g, Canvas.TopProperty, 0, start.Y, 0.8, start.Y, 1.6, p.Y - 11, 1.9, p.Y - 11, 2.4, p.Y);
            EK(sb, pill, UIElement.OpacityProperty, 0, 0, 2.4, 0, 2.7, 1);
        }

        static void ScController(Canvas c, Storyboard sb)
        {
            var g = EPip(110, 62);
            var ring = new Rectangle { Stroke = EB("Accent"), StrokeThickness = 2.5 };
            g.Children.Add(ring);
            var hints = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var h in new[] { "A", "B", "LB", "RB" })
            {
                var chip = new Border { CornerRadius = new CornerRadius(6), Background = EHex("#18181C"), Padding = new Thickness(5, 0, 5, 1), Margin = new Thickness(1.5, 0, 1.5, 0) };
                chip.Child = new TextBlock { Text = h, Foreground = EB("Accent"), FontSize = 8, FontWeight = FontWeights.Bold };
                hints.Children.Add(chip);
            }
            g.Children.Add(hints);
            EPut(c, g, 10, 10);
            ETour(sb, g, 110, 62, new[] { new[] { 0, 0 }, new[] { 2, 0 }, new[] { 2, 2 }, new[] { 0, 2 } }, 0.7, 0.35);
            // a white flash on each press
            var flash = new List<double>();
            for (int i = 0; i < 4; i++) { double t = 0.7 + i * 1.05; flash.AddRange(new[] { t - 0.01, 0.0, t, 1.0, t + 0.18, 0.0 }); }
            var w = new Rectangle { Stroke = Brushes.White, StrokeThickness = 2.5, Opacity = 0 };
            g.Children.Add(w);
            EK(sb, w, UIElement.OpacityProperty, flash.ToArray());
        }
    }
}
