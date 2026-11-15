using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PipStudio
{
    // =================================================================================
    //  Entry point: a WPF application (WPF-UI windows) that also hosts the WinForms PiP engine
    //  on the same UI thread.
    // =================================================================================
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }

            bool created;
            var mutex = new Mutex(true, "PiPStudio.SingleInstance.v2", out created);
            if (!created && args.Any(a => a == "--replace"))
            {
                try { created = mutex.WaitOne(10000); } catch (AbandonedMutexException) { created = true; }
            }
            if (!created)
            {
                IntPtr h = Native.FindWindow(null, HostWindow.Caption);
                if (h != IntPtr.Zero) Native.PostMessage(h, HostWindow.WM_SHOWAPP, IntPtr.Zero, IntPtr.Zero);
                return 0;
            }

            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (o, e) => App.ReportError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (o, e) => App.ReportError(e.ExceptionObject as Exception);

            var wapp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            wapp.DispatcherUnhandledException += (o, e) =>
            {
                if (!(e.Exception is ArgumentException && (e.Exception.StackTrace ?? "").Contains("TitleBar"))) App.ReportError(e.Exception);
                e.Handled = true;
            };
            System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();

            App.S = Settings.Load();
            L.Init(App.S.Language);
            Theme.Refresh();
            Fluent.Init(wapp);
            MainUI.Init();
            new App(args);
            wapp.Exit += (o, e) => { try { if (App.I != null) App.I.Cleanup(); } catch { } };
            wapp.Run();
            GC.KeepAlive(mutex);
            return 0;
        }
    }

    // =================================================================================
    //  Windows 11 resources shared by every WPF window: theme dictionaries and short brush keys
    // =================================================================================
    static class Fluent
    {
        public static Application App;
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly FontFamily TextFont = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        static bool appliedDark;

        public static void Init(Application a)
        {
            App = a;
            appliedDark = Theme.Dark;
            a.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Theme.Dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light });
            a.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
            try { Wpf.Ui.Appearance.ApplicationAccentColorManager.ApplySystemAccent(); } catch { }
            PickBrushes();
            Theme.Changed += OnThemeChanged;
        }

        static Brush Hex(string hex) { var b = (Brush)new BrushConverter().ConvertFrom(hex); b.Freeze(); return b; }

        /// <summary>Copies WPF-UI's theme brushes into short keys (re-run whenever light/dark or the accent changes).</summary>
        public static void PickBrushes()
        {
            var r = App.Resources;
            bool dark = Theme.Dark;
            Func<string, string, string, Brush> pick = (key, dk, lt) => (App.TryFindResource(key) as Brush) ?? Hex(dark ? dk : lt);
            var acc = Theme.Accent;
            var accBrush = new SolidColorBrush(Color.FromRgb(acc.R, acc.G, acc.B));
            accBrush.Freeze();
            r["Accent"] = accBrush;
            var on = Theme.OnAccent;
            r["OnAccent"] = new SolidColorBrush(Color.FromRgb(on.R, on.G, on.B));
            r["Card"] = pick("CardBackgroundFillColorDefaultBrush", "#0DFFFFFF", "#B3FFFFFF");
            r["CardSecondary"] = pick("CardBackgroundFillColorSecondaryBrush", "#08FFFFFF", "#80F6F6F6");
            r["CardBorder"] = pick("CardStrokeColorDefaultBrush", "#19000000", "#0F000000");
            r["Layer"] = pick("LayerFillColorDefaultBrush", "#4C3A3A3A", "#80FFFFFF");
            r["LayerBorder"] = pick("CardStrokeColorDefaultBrush", "#19000000", "#0F000000");
            r["Divider"] = pick("DividerStrokeColorDefaultBrush", "#15FFFFFF", "#0F000000");
            r["Text"] = pick("TextFillColorPrimaryBrush", "#FFFFFF", "#E4000000");
            r["Sub"] = pick("TextFillColorSecondaryBrush", "#C5FFFFFF", "#9E000000");
            r["Tertiary"] = pick("TextFillColorTertiaryBrush", "#87FFFFFF", "#72000000");
            r["Hover"] = pick("SubtleFillColorSecondaryBrush", "#0FFFFFFF", "#09000000");
            r["Pressed"] = pick("SubtleFillColorTertiaryBrush", "#0AFFFFFF", "#06000000");
            r["NavSel"] = pick("SubtleFillColorSecondaryBrush", "#0FFFFFFF", "#09000000");
            r["Tile"] = pick("ControlFillColorDefaultBrush", "#0FFFFFFF", "#B3FFFFFF");
            r["TrackBrush"] = pick("ControlStrongFillColorDefaultBrush", "#8BFFFFFF", "#72000000");
            r["Attention"] = pick("SystemFillColorAttentionBackgroundBrush", "#08FFFFFF", "#80F6F6F6");
            r["PaneOverlay"] = pick("SolidBackgroundFillColorTertiaryBrush", "#2C2C2C", "#F9F9F9");
            r["Smoke"] = Hex(dark ? "#4D000000" : "#33000000");
            r["Preview"] = Hex(dark ? "#33000000" : "#14000000");
            r["AppBg"] = Hex(dark ? "#202020" : "#F3F3F3");             // solid window background (Lite mode): Windows 11's own app colors
            r["LookTitle"] = Brushes.Transparent;                         // behind the title bar and the side pane; the Windows 10 / 7 looks
            r["LookNav"] = Brushes.Transparent;                           // set their own (Looks.cs)
        }

        static void OnThemeChanged()
        {
            if (App == null) return;
            if (Theme.Dark != appliedDark)
            {
                appliedDark = Theme.Dark;
                ApplyWpfUiTheme(Theme.Dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
            }
            PickBrushes();
        }

        /// <summary>Switches WPF-UI between light and dark (looked up at run time so any 3.x version works).</summary>
        static void ApplyWpfUiTheme(Wpf.Ui.Appearance.ApplicationTheme theme)
        {
            try
            {
                var t = typeof(Wpf.Ui.Appearance.SystemThemeWatcher).Assembly.GetType("Wpf.Ui.Appearance.ApplicationThemeManager");
                var m = t == null ? null : t.GetMethods().FirstOrDefault(x => x.Name == "Apply" && x.GetParameters().Length >= 1
                                                                   && x.GetParameters()[0].ParameterType == typeof(Wpf.Ui.Appearance.ApplicationTheme));
                if (m == null) return;
                var ps = m.GetParameters();
                var args = new object[ps.Length];
                args[0] = theme;
                for (int i = 1; i < ps.Length; i++)
                    args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null);
                m.Invoke(null, args);
            }
            catch { }
        }

        public static TextBlock Glyph(string glyph, double size, string brushKey = "Text")
        {
            var t = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return t;
        }

        public static TextBlock Label(string text, double size, string brushKey = "Text", bool wrap = true)
        {
            var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
            if (wrap) t.TextWrapping = TextWrapping.Wrap; else t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return t;
        }
    }

    // =================================================================================
    //  Motion — Windows 11 timings: hover in 83 ms, out 167 ms, entrance ~300 ms with a fast-out/slow-in curve
    // =================================================================================
    /// <summary>Smooth, eased mouse-wheel scrolling for WPF ScrollViewers (WPF jumps a fixed step by default).</summary>
    static class SmoothScroll
    {
        class State
        {
            public ScrollViewer Sv;
            public double Target, Current;
            public bool Running;
            public System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();
            public EventHandler Tick;
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, State> states = new System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, State>();

        public static void Attach(ScrollViewer sv)
        {
            if (sv == null) return;
            State st;
            if (states.TryGetValue(sv, out st)) return;
            st = new State { Sv = sv, Target = sv.VerticalOffset, Current = sv.VerticalOffset };
            states.Add(sv, st);
            sv.PreviewMouseWheel += (o, e) =>
            {
                if (!Anim.On || sv.ScrollableHeight <= 0 || e.Handled) return;
                if (!st.Running) { st.Target = sv.VerticalOffset; st.Current = sv.VerticalOffset; }
                st.Target = Math.Max(0, Math.Min(sv.ScrollableHeight, st.Target - e.Delta * 0.85));   // ~100 px a notch, like Windows 11 apps
                e.Handled = true;
                Start(st);
            };
            sv.ScrollChanged += (o, e) => { if (!st.Running) { st.Target = sv.VerticalOffset; st.Current = sv.VerticalOffset; } };
            sv.Unloaded += (o, e) => Stop(st);
        }

        /// <summary>Scroll smoothly to an offset (or jump when animations are off).</summary>
        public static void To(ScrollViewer sv, double offset)
        {
            if (sv == null) return;
            offset = Math.Max(0, Math.Min(sv.ScrollableHeight, offset));
            State st;
            if (!Anim.On || !states.TryGetValue(sv, out st)) { sv.ScrollToVerticalOffset(offset); return; }
            if (!st.Running) st.Current = sv.VerticalOffset;
            st.Target = offset;
            Start(st);
        }

        /// <summary>Scroll smoothly by an amount (adds to a scroll already in progress).</summary>
        public static void By(ScrollViewer sv, double delta)
        {
            if (sv == null) return;
            State st;
            if (!Anim.On || !states.TryGetValue(sv, out st)) { sv.ScrollToVerticalOffset(sv.VerticalOffset + delta); return; }
            if (!st.Running) { st.Target = sv.VerticalOffset; st.Current = sv.VerticalOffset; }
            st.Target = Math.Max(0, Math.Min(sv.ScrollableHeight, st.Target + delta));
            Start(st);
        }

        /// <summary>Jump straight to an offset, cancelling any scroll in progress.</summary>
        public static void Jump(ScrollViewer sv, double offset)
        {
            if (sv == null) return;
            State st;
            if (states.TryGetValue(sv, out st)) { Stop(st); st.Target = st.Current = offset; }
            sv.ScrollToVerticalOffset(offset);
        }

        static void Start(State st)
        {
            if (st.Running) return;
            st.Running = true;
            st.Clock.Restart();
            st.Tick = (o, e) =>
            {
                double dt = Math.Min(0.05, st.Clock.Elapsed.TotalSeconds);
                st.Clock.Restart();
                st.Target = Math.Max(0, Math.Min(st.Sv.ScrollableHeight, st.Target));
                double k = 1 - Math.Exp(-dt * 13);                        // frame-rate independent ease-out
                st.Current += (st.Target - st.Current) * k;
                if (Math.Abs(st.Target - st.Current) < 0.5) { st.Current = st.Target; st.Sv.ScrollToVerticalOffset(st.Current); Stop(st); return; }
                st.Sv.ScrollToVerticalOffset(st.Current);
            };
            CompositionTarget.Rendering += st.Tick;
        }

        static void Stop(State st)
        {
            if (!st.Running) return;
            st.Running = false;
            if (st.Tick != null) CompositionTarget.Rendering -= st.Tick;
            st.Tick = null;
        }

        /// <summary>Hook up the ScrollViewer inside a ListBox (or any templated control) once it's loaded.</summary>
        public static void AttachInside(Control c)
        {
            if (c == null) return;
            Action go = () => Attach(Find(c));
            if (c.IsLoaded) go(); else c.Loaded += (o, e) => go();
        }

        static ScrollViewer Find(DependencyObject d)
        {
            if (d == null) return null;
            var sv = d as ScrollViewer;
            if (sv != null) return sv;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) { var r = Find(VisualTreeHelper.GetChild(d, i)); if (r != null) return r; }
            return null;
        }
    }

    static class Anim
    {
        public static bool On { get { return PipStudio.App.S == null || (PipStudio.App.S.Animations && !PipStudio.App.S.LiteMode); } }

        static IEasingFunction Frozen(IEasingFunction e) { var f = e as Freezable; if (f != null) f.Freeze(); return e; }
        public static readonly IEasingFunction Decel = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });
        public static readonly IEasingFunction Smooth = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });
        public static readonly IEasingFunction Quint = Frozen(new QuinticEase { EasingMode = EasingMode.EaseOut });

        public static void To(UIElement el, DependencyProperty dp, double to, int ms, IEasingFunction ease = null, int delayMs = 0, Action done = null)
        {
            if (!On || ms <= 0)
            {
                el.BeginAnimation(dp, null);
                el.SetValue(dp, to);
                if (done != null) done();
                return;
            }
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(delayMs) };
            if (done != null) a.Completed += (o, e) => done();
            el.BeginAnimation(dp, a);
        }

        public static void Fade(UIElement el, double to, int ms, int delayMs = 0, Action done = null) { To(el, UIElement.OpacityProperty, to, ms, Decel, delayMs, done); }

        /// <summary>Pointer-over fill that fades in and out like Windows 11 controls.</summary>
        public static void Hover(UIElement area, UIElement fill, UIElement pressedFill = null)
        {
            fill.Opacity = 0;
            area.MouseEnter += (o, e) => Fade(fill, 1, 83);
            area.MouseLeave += (o, e) => { Fade(fill, 0, 167); if (pressedFill != null) Fade(pressedFill, 0, 100); };
            if (pressedFill != null)
            {
                pressedFill.Opacity = 0;
                area.PreviewMouseLeftButtonDown += (o, e) => Fade(pressedFill, 1, 50);
                area.PreviewMouseLeftButtonUp += (o, e) => Fade(pressedFill, 0, 167);
            }
        }

        /// <summary>Entrance: rises a little and fades in.</summary>
        public static void Enter(UIElement el, double dy, int delayMs, int ms = 300)
        {
            if (!On) { el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = 1; el.RenderTransform = null; return; }
            var tt = new TranslateTransform(0, dy);
            el.RenderTransform = tt;
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Math.Min(ms, 220)))
            { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = Decel, FillBehavior = FillBehavior.Stop });
            el.Opacity = 1;
            if (delayMs > 0)
            {
                // hide until the delayed animation starts
                var hold = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                hold.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                hold.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs))));
                hold.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs + Math.Min(ms, 220))), Decel));
                el.BeginAnimation(UIElement.OpacityProperty, hold);
            }
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = Quint });
        }
    }
}
