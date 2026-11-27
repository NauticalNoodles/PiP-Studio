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
    // ---------------------------------------------------------------------------------
    //  Rows shown in the tables (public so WPF data binding can read them)
    // ---------------------------------------------------------------------------------
    public class WinRowItem
    {
        public IntPtr Hwnd { get; set; }
        public bool IsDisplay { get; set; }
        public string Title { get; set; }
        public string App { get; set; }
        public string SizeText { get; set; }
        public string Status { get; set; }
        public ImageSource Icon { get; set; }
        public string Glyph { get; set; }
        public bool IsAuto { get; set; }
        public bool MayFreeze { get; set; }
        public Visibility FreezeVis { get { return MayFreeze ? Visibility.Visible : Visibility.Collapsed; } }
        internal System.Drawing.Rectangle ScreenBounds;
        internal int Order;
    }

    public class PipRowItem
    {
        public string Title { get; set; }
        public string App { get; set; }
        public string Position { get; set; }
        public string SizeText { get; set; }
        public string OpacityText { get; set; }
        public string Mode { get; set; }
        public ImageSource Icon { get; set; }
        public string Glyph { get; set; }
        internal PipWindow Pip;
        internal IntPtr External;          // another app's own PiP window (Chrome, Firefox, FreeTube...)
    }

    class SearchEntry
    {
        public string Title, Text, Page;
        public FrameworkElement Element;
        public Action Reveal;          // opens the expander it lives in
    }

    class CardParts
    {
        public Border Root;
        public TextBlock Title, Desc;
    }

    // =================================================================================
    //  The main window — built to the Windows 11 Settings / Task Manager design:
    //  Mica, NavigationView metrics with a sliding selection pill, a rounded content layer,
    //  SettingsCard / SettingsExpander rows, Task Manager tables and Fluent motion.
    // =================================================================================
    static partial class MainUI
    {
        static Window w;
        static bool exiting, closedHintShown;

        static ListBox winList, runList, autoList;
        static StackPanel markedList;          // Running PiPs > Marked as PiPs
        static Border markedBox;
        static string markedSig = "";
        static bool syncingSel, sizing;
        static string autoSig = "";
        static Border navPane, previewBox, chromiumCard, detailsCard, contentLayer;
        static readonly Dictionary<string, bool> chromiumExe = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        static StackPanel pageHeader;
        static bool detailsWanted = true;
        static TextBlock pageTitle, searchHint, padStatus, selTitle, selSub, runEmpty, fixState, previewNote;
        static TextBox searchBox, winSearch;
        static TextBlock winSearchHint;
        static Grid searchWrap;
        static WrapPanel cmdBar;
        static Grid pageWindows, pageRunning, winHeader, runHeader, navRoot;
        static ScrollViewer pageScroll;
        static StackPanel settingsHost;
        static RadioButton[] navBtns;
        static Button burger, searchIconBtn;
        static Rectangle indicator;
        static Canvas indCanvas;
        static double indTop = -1;
        static Wpf.Ui.Controls.ToggleSwitch fixAlways;
        static Button fixNow;
        static readonly Dictionary<string, StackPanel> pages = new Dictionary<string, StackPanel>();
        static readonly List<SearchEntry> searchIndex = new List<SearchEntry>();
        static readonly string[] navKeys = { "windows", "running", "appearance", "behavior", "controller", "shortcuts", "settings", "phone" };
        static readonly string[] navLabels = { "Windows", "Running PiPs", "Appearance", "Behavior", "Controller", "Shortcuts", "Settings", "Phone" };
        static string page = "windows", buildingPage, lastSettingsPage = "appearance";
        static Action buildingReveal;
        static bool compact, userCompact, overlayOpen, paneStyled, lastExpanded = true, fixSync;
        static Border navSpace, scrim;
        static readonly Dictionary<FrameworkElement, int> widthTokens = new Dictionary<FrameworkElement, int>();
        static int fixToken;
        static DispatcherTimer tick;
        static int tickCount;
        static readonly Dictionary<IntPtr, ImageSource> iconCache = new Dictionary<IntPtr, ImageSource>();
        static string winSig = "", runSig = "", sortKey = "", winFilter = "";
        static bool sortDesc;

        static IntPtr thumb, thumbSrc;
        static System.Drawing.Rectangle thumbRect;
        static int thumbAlpha;

        static string captureKb;
        static Button captureKbBtn;
        static Action refreshBindings;
        static Action refreshUpdates;   // the Updates card on the settings page, while the window is open
        static Action refreshCompanion; // the Phone companion section, while the window is open
        static Action refreshLayouts;   // the Layouts section, while the window is open

        public static void Init()
        {
            App.OpenMain = Open;
            App.CloseMain = () => { if (w != null && !exiting) { exiting = true; w.Close(); } };
            Updater.Changed += () => { var r = refreshUpdates; if (r != null) r(); };
            Companion.Changed += () => { var r = refreshCompanion; if (r != null) r(); };
            Layouts.Changed += () => DelayRun(30, () => { var r = refreshLayouts; if (r != null) r(); });
            Theme.Changed += () => { if (w != null) w.Dispatcher.BeginInvoke(new Action(ApplyLite)); };   // OLED: solid black, no Mica
        }

        // ----------------------------------------------------------------------------- open / close
        public static void Open(string pageKey)
        {
            if (w == null) Build();
            if (!w.IsVisible)
            {
                // keep the window invisible (DWM cloak) until WPF has drawn its first frame, so it never flashes white
                var helper = new WindowInteropHelper(w);
                if (helper.Handle == IntPtr.Zero) helper.EnsureHandle();
                Cloak(helper.Handle, true);
                w.Show();
                UncloakAfterRender(helper.Handle);
            }
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            try { U.ForceForeground(new WindowInteropHelper(w).Handle); } catch { }
            if (pageKey != null) Navigate(pageKey);
            if (U.XboxMode && App.S.PadUiMode == 1 && !padVisual) Dispatch(() => SetPadVisual(true), DispatcherPriority.Loaded);   // Xbox mode: you're on a controller
            DelayRun(2500, MaybeAskForRating);                            // Store version: once, after a few days of use
        }

        static T F<T>(string name) where T : class { return (T)w.FindName(name); }

        /// <summary>Applies the language picked in Settings right away: rebuilds this window in place, same spot and size, back on the same page.</summary>
        static void SwitchLanguage()
        {
            L.Init(App.S.Language);
            App.I.SettingsChanged();
            var old = w;
            if (old == null) return;
            bool max = old.WindowState == WindowState.Maximized;
            var rb = old.RestoreBounds;
            string pg = page;
            exiting = true;                 // closing it must not quit or show the "still running" tip
            try { old.Close(); } catch { }
            exiting = false;
            Build();
            if (!rb.IsEmpty) { w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = rb.Left; w.Top = rb.Top; w.Width = rb.Width; w.Height = rb.Height; }
            if (max) w.WindowState = WindowState.Maximized;
            Open(pg == "search" ? "settings" : pg);
            Dispatch(() => L.Walk(w), DispatcherPriority.Loaded);
        }

        internal static void Cloak(IntPtr h, bool on)
        {
            if (h == IntPtr.Zero) return;
            int v = on ? 1 : 0;
            try { Native.DwmSetWindowAttribute(h, 13, ref v, 4); } catch { }   // DWMWA_CLOAK
        }

        internal static void UncloakAfterRender(IntPtr h)
        {
            int frames = 0;
            bool done = false;
            EventHandler tick = null;
            Action finish = () =>
            {
                if (done) return;
                done = true;
                CompositionTarget.Rendering -= tick;
                Cloak(h, false);
            };
            tick = (o, e) => { if (++frames >= 2) finish(); };              // two frames: the first one has reached the screen
            CompositionTarget.Rendering += tick;
            DelayRun(400, finish);                                           // never stay hidden
        }

        static void Build()
        {
            exiting = false;
            Fluent.PickBrushes();
            var win = (Window)XamlReader.Parse((App.S.LiteMode || Theme.Oled ? LiteXaml(Xaml) : Xaml).Replace("Microsoft YaHei UI", L.Cjk));   // the right font for the language's characters
            w = win;
            L.Watch(win);
            win.ManipulationBoundaryFeedback += (o, e) => e.Handled = true;   // touch: scrolling past the end doesn't make the whole window bounce
            ImageSource appIcon = null;
            if (App.AppIconHandle != IntPtr.Zero)
            {
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(App.AppIconHandle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    appIcon = src;
                    win.Icon = src;
                }
                catch { }
            }
            F<Image>("AppIcon").Source = appIcon;

            winList = F<ListBox>("WinList"); runList = F<ListBox>("RunList"); autoList = F<ListBox>("AutoList"); autoSig = ""; winFilter = ""; lastColsFor = -1;
            SmoothScroll.AttachInside(winList); SmoothScroll.AttachInside(runList);
            markedList = F<StackPanel>("MarkedList"); markedBox = F<Border>("MarkedBox"); markedSig = "";
            SmoothScroll.Attach(F<ScrollViewer>("MarkedScroll"));
            F<Button>("MarkedClear").Click += (o, e) => Ask("Remove every mark?",
                "Every window you marked with Treat as a PiP goes back to being a normal window, and comes off always on top.",
                "Remove all", "Cancel", ok =>
                {
                    if (!ok) return;
                    foreach (var m in ExternalPip.Marks()) ExternalPip.Unmark(m);
                    runSig = ""; winSig = ""; RefreshRunning(); RefreshMarked(); BuildCommands();
                });
            SmoothScroll.Attach(F<ScrollViewer>("DetailsScroll"));
            navPane = F<Border>("NavPane"); previewBox = F<Border>("PreviewBox"); chromiumCard = F<Border>("ChromiumCard"); detailsCard = F<Border>("DetailsCard"); pageHeader = F<StackPanel>("PageHeader");
            contentLayer = F<Border>("ContentLayer");
            pageTitle = F<TextBlock>("PageTitle"); searchHint = F<TextBlock>("SearchHint"); padStatus = F<TextBlock>("PadStatus");
            selTitle = F<TextBlock>("SelTitle"); selSub = F<TextBlock>("SelSub"); runEmpty = F<TextBlock>("RunEmpty"); fixState = F<TextBlock>("FixState");
            previewNote = F<TextBlock>("PreviewNote");
            searchBox = F<TextBox>("SearchBox"); searchWrap = F<Grid>("SearchWrap"); cmdBar = F<WrapPanel>("CmdBar");
            cmdBar.SizeChanged += (o, e) => { if (e.WidthChanged) AlignCommands(); };   // they may fit on one row now, or not
            pageWindows = F<Grid>("PageWindows"); pageRunning = F<Grid>("PageRunning"); winHeader = F<Grid>("WinHeader"); runHeader = F<Grid>("RunHeader");
            navRoot = F<Grid>("NavRoot");
            pageScroll = F<ScrollViewer>("PageScroll"); settingsHost = F<StackPanel>("SettingsHost");
            SmoothScroll.Attach(pageScroll);
            burger = F<Button>("Burger"); searchIconBtn = F<Button>("SearchIconBtn");
            navSpace = F<Border>("NavSpace"); scrim = F<Border>("Scrim");
            overlayOpen = false; paneStyled = false; lastExpanded = true;
            indicator = F<Rectangle>("Indicator"); indCanvas = F<Canvas>("IndCanvas"); indTop = -1;
            fixAlways = F<Wpf.Ui.Controls.ToggleSwitch>("FixAlways"); fixNow = F<Button>("FixNow");
            navBtns = Enumerable.Range(0, navKeys.Length).Select(i => F<RadioButton>("Nav" + i)).ToArray();
            for (int i = 0; i < navBtns.Length; i++)
            {
                int idx = i;
                navBtns[i].Checked += (o, e) => { if (page != navKeys[idx]) Navigate(navKeys[idx]); };
                navBtns[i].Click += (o, e) => { if (overlayOpen) { overlayOpen = false; ApplyLayout(true); } };
                navBtns[i].Click += (o, e) => { if (searchBox.Text.Length > 0 && page != navKeys[idx]) searchBox.Text = ""; };
            }

            // tables
            BuildHeader(winHeader, new[] { "Name", "App", "Size", "Status" }, new[] { "name", "app", "size", "status" }, true);
            BuildHeader(runHeader, new[] { "Showing", "App", "Position", "Size", "Opacity", "Mode" }, null, false);
            lastColsFor = -1;
            // the Auto option sits above the table; only one of the two lists has a selection at a time
            winList.SelectionChanged += (o, e) =>
            {
                if (syncingSel) return;
                if (winList.SelectedItem != null && autoList.SelectedItem != null) { syncingSel = true; autoList.SelectedItem = null; syncingSel = false; }
                OnWinSelected();
            };
            autoList.SelectionChanged += (o, e) =>
            {
                if (syncingSel) return;
                if (autoList.SelectedItem != null && winList.SelectedItem != null) { syncingSel = true; winList.SelectedItem = null; syncingSel = false; }
                OnWinSelected();
            };
            winList.MouseDoubleClick += (o, e) => { if (winList.SelectedItem != null) OpenSelected(); };
            autoList.MouseDoubleClick += (o, e) => { if (autoList.SelectedItem != null) OpenSelected(); };
            winList.KeyDown += (o, e) => { if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; } };
            autoList.KeyDown += (o, e) => { if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; } };
            F<Wpf.Ui.Controls.Button>("OpenBtn").Click += (o, e) => OpenSelected();
            // the warning icon on a row opens the freeze fix dialog for that window
            winList.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((o, e) =>
            {
                var b = e.OriginalSource as FrameworkElement;
                if (b == null || !"FreezeWarn".Equals(b.Tag)) return;
                var it = b.DataContext as WinRowItem;
                if (it == null) return;
                winList.SelectedItem = it;
                ShowFreezeDialog(it);
                e.Handled = true;
            }));
            fixNow.Click += (o, e) =>
            {
                var it = SelItem();
                if (it == null || it.IsDisplay) return;
                FrozenFix.Relaunch(it.Hwnd);
                DelayRun(5000, () => { winSig = ""; RefreshWindows(); });
            };
            fixAlways.Click += (o, e) => ApplyAlwaysFix(fixAlways.IsChecked == true);
            runList.SelectionChanged += (o, e) => BuildCommands();
            // options for one PiP: the ⋯ button on its row, or a right-click on the row
            runList.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((o, e) =>
            {
                var b = e.OriginalSource as FrameworkElement;
                if (b == null || !"RowMore".Equals(b.Tag as string)) return;
                var row = b.DataContext as PipRowItem;
                if (row == null) return;
                runList.SelectedItem = row;
                ShowPipOptions(row);
                e.Handled = true;
            }));
            runList.PreviewMouseRightButtonUp += (o, e) =>
            {
                var c = ItemsControl.ContainerFromElement(runList, e.OriginalSource as DependencyObject) as ListBoxItem;
                var row = c == null ? null : c.DataContext as PipRowItem;
                if (row == null) return;
                runList.SelectedItem = row;
                ShowPipOptions(row);
                e.Handled = true;
            };
            runList.MouseDoubleClick += (o, e) => { var p = SelectedPip(); if (p != null) p.BringSourceToFront(); else { var x = SelectedExternal(); if (x != IntPtr.Zero) U.ForceForeground(x); } };

            // search
            searchBox.TextChanged += (o, e) => OnSearch();
            // the Windows page has its own search, just for windows
            winSearch = F<TextBox>("WinSearch"); winSearchHint = F<TextBlock>("WinSearchHint");
            winSearch.TextChanged += (o, e) =>
            {
                winSearchHint.Visibility = winSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                winFilter = winSearch.Text.Trim(); winSig = ""; RefreshWindows();
            };
            winSearch.KeyDown += (o, e) =>
            {
                if (e.Key == Key.Escape && winSearch.Text.Length > 0) { winSearch.Text = ""; e.Handled = true; }
                else if (e.Key == Key.Down || e.Key == Key.Enter)
                {
                    if (winList.Items.Count > 0)
                    {
                        winList.SelectedIndex = Math.Max(0, winList.SelectedIndex);
                        var c = winList.ItemContainerGenerator.ContainerFromIndex(winList.SelectedIndex) as ListBoxItem;
                        if (c != null) c.Focus();
                    }
                    e.Handled = true;
                }
            };
            searchBox.GotKeyboardFocus += (o, e) => UpdateSearchHint();
            searchBox.LostKeyboardFocus += (o, e) => UpdateSearchHint();
            searchBox.KeyDown += (o, e) =>
            {
                if (e.Key == Key.Escape) { searchBox.Text = ""; e.Handled = true; }
                else if (e.Key == Key.Enter && page == "search")
                {
                    Action act;
                    var first = pages["search"].Children.OfType<UIElement>().FirstOrDefault(x => activators.ContainsKey(x));
                    if (first != null && activators.TryGetValue(first, out act)) act();
                    e.Handled = true;
                }
            };
            searchIconBtn.Click += (o, e) => { OpenPane(); Dispatch(() => searchBox.Focus(), DispatcherPriority.Input); };
            scrim.MouseLeftButtonDown += (o, e) => { overlayOpen = false; ApplyLayout(true); e.Handled = true; };
            win.PreviewKeyDown += OnPreviewKeyDown;
            // the live preview is a second copy of the source for Windows to draw: only while this window is in use
            win.Deactivated += (o, e) => HideThumb();
            win.Activated += (o, e) => DelayRun(60, UpdatePreview);
            PadUiInit(win);
            ExplainInit();
            F<Button>("ExplainHide").Click += (o, e) => ToggleExplainers();
            F<Button>("ExplainX").Click += (o, e) => ToggleExplainers();
            padGlow = padRing.Effect;
            ApplyLite();

            // navigation pane
            burger.Click += (o, e) =>
            {
                if (IsWide()) { userCompact = !userCompact; overlayOpen = false; }   // wide window: the pane pushes the content
                else overlayOpen = !overlayOpen;                                       // narrow window: the pane opens over the content
                ApplyLayout(true);
            };
            win.SizeChanged += (o, e) => ApplyLayout(false);
            navRoot.SizeChanged += (o, e) => MoveIndicator(false);

            BuildSettingsPages();

            win.SourceInitialized += (o, e) =>
            {
                try { if (App.S.ThemeMode == 0) Wpf.Ui.Appearance.SystemThemeWatcher.Watch(win); } catch { }
                // while the user drags the window edge, hide the live preview: the DWM thumbnail can't keep pace with WPF's layout and shakes
                try
                {
                    var src = HwndSource.FromHwnd(new WindowInteropHelper(win).Handle);
                    if (src != null) src.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                    {
                        if (msg == 0x0231) { sizing = true; HideThumb(); }                     // WM_ENTERSIZEMOVE
                        else if (msg == 0x0232) { sizing = false; DelayRun(60, UpdatePreview); } // WM_EXITSIZEMOVE
                        return IntPtr.Zero;
                    });
                }
                catch { }
            };
            win.Loaded += (o, e) => { Dispatch(() => MoveIndicator(false), DispatcherPriority.Loaded); AnimatePage(); Dispatch(() => L.Walk(w), DispatcherPriority.Loaded); };
            win.Closing += (o, e) =>
            {
                if (!exiting && !App.S.CloseToTray)
                {
                    exiting = true;
                    win.Dispatcher.BeginInvoke(new Action(App.I.ExitApp));
                    return;
                }
                if (!exiting && !closedHintShown && App.S.ShowTips)
                {
                    closedHintShown = true;
                    App.I.Balloon("PiP Studio is still running", U.XboxMode
                        ? "Your shortcuts and controller keep working. Start PiP Studio again to open this window."   // Xbox mode has no tray
                        : "Your shortcuts keep working. Click the tray icon to open it again.");
                }
            };
            win.Closed += (o, e) => OnClosed(win);

            App.I.StateChanged += OnEngineState;
            tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            tick.Tick += (o, e) => OnTick();
            tick.Start();

            var wa = SystemParameters.WorkArea;
            win.Width = Math.Min(1120, wa.Width - 40);
            win.Height = Math.Min(740, wa.Height - 40);
            Navigate(page == "search" ? "windows" : page, false);
            ApplyLayout(false);
            UpdateSearchHint();
        }

        static void OnClosed(Window win)
        {
            try { var m = typeof(Wpf.Ui.Appearance.SystemThemeWatcher).GetMethod("UnWatch"); if (m != null) m.Invoke(null, new object[] { win }); } catch { }
            if (w != win) return;
            if (tick != null) { tick.Stop(); tick = null; }
            if (App.I != null) App.I.StateChanged -= OnEngineState;
            HideThumb();
            if (captureKb != null) EndKeyCapture();
            if (App.I != null) App.I.CancelPadCapture();
            w = null; winList = runList = autoList = null; markedList = null; markedBox = null; navPane = previewBox = chromiumCard = detailsCard = contentLayer = null; explainCard = null; explainStage = null; if (explainSb != null) { explainSb = null; } pageHeader = null;
            pageTitle = searchHint = padStatus = selTitle = selSub = runEmpty = fixState = previewNote = null; searchBox = null; winSearch = null; winSearchHint = null; searchWrap = null; cmdBar = null;
            pageWindows = pageRunning = winHeader = runHeader = navRoot = null; pageScroll = null; settingsHost = null; navBtns = null;
            burger = searchIconBtn = null; indicator = null; indCanvas = null; navSpace = scrim = null; widthTokens.Clear();
            fixAlways = null; fixNow = null; refreshBindings = null; refreshUpdates = null; refreshCompanion = null; refreshLayouts = null; layoutsAnchor = null;
            pages.Clear(); searchIndex.Clear(); iconCache.Clear(); winSig = runSig = "";
            if (!exiting && App.S.MinimizeMemory) App.RunSoon(U.Trim, 1500);
        }

        static void Dispatch(Action a, DispatcherPriority p = DispatcherPriority.Background)
        {
            if (w != null) w.Dispatcher.BeginInvoke(a, p);
        }

        static void DelayRun(int ms, Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (o, e) => { t.Stop(); try { a(); } catch (Exception ex) { App.ReportError(ex); } };
            t.Start();
        }
    }
}
