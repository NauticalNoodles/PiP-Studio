using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PipStudio
{
    // =================================================================================
    //  Hidden window: global hotkeys, "open the app" from a second launch, display and theme changes
    // =================================================================================
    class HostWindow : NativeWindow
    {
        public const string Caption = "PiPStudio_HostWindow";
        public const int WM_SHOWAPP = 0x8001;
        readonly App app;

        public HostWindow(App a)
        {
            app = a;
            var cp = new CreateParams();
            cp.Caption = Caption;
            CreateHandle(cp);
            // a copy started normally can still ask this one to show its window when this one runs as administrator
            // (Windows otherwise drops messages from normal apps to administrator ones, and the second launch does nothing)
            try { Native.ChangeWindowMessageFilterEx(Handle, (uint)WM_SHOWAPP, 1, IntPtr.Zero); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_HOTKEY: app.OnHotkey((int)m.WParam.ToInt64()); return;
                case WM_SHOWAPP: app.ShowMain(null); return;
                case 0x7E: app.OnDisplayChanged(); break;                 // WM_DISPLAYCHANGE
                case 0x1A: app.OnThemeMaybeChanged(); break;              // WM_SETTINGCHANGE (light/dark switch)
                case 0x320: app.OnThemeMaybeChanged(); break;             // WM_DWMCOLORIZATIONCOLORCHANGED (accent)
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>One thing the user can trigger, from the keyboard and/or a controller.</summary>
    class ActionDef
    {
        public string Key, Label, Brief, HkField;
        public int PadDefault;
        public Action Run;
        public ActionDef(string key, string label, string brief, string hkField, int padDefault, Action run)
        {
            Key = key; Label = label; Brief = brief; HkField = hkField; PadDefault = padDefault; Run = run;
        }
    }

    /// <summary>A 1×1 window that takes keyboard focus away from a game while controller controls are on (from UniversalPiP).</summary>
    class FocusSink : Form
    {
        public FocusSink()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Bounds = new Rectangle(wa.Right - 2, wa.Bottom - 2, 1, 1);
            Opacity = 0.01;
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW; return cp; }
        }

        public void Grab()
        {
            if (!Visible) Show();
            U.ForceForeground(Handle);
            if (Native.GetForegroundWindow() == Handle) { Activate(); Focus(); }   // make it the real keyboard focus, so the game gets "you lost focus"
        }
        public void Release() { if (Visible) Hide(); }
    }

    // =================================================================================
    //  "Frozen preview" fix for Chromium apps (Chrome, Edge, Spotify, Discord, ...) — from UniversalPiP.
    //  They stop drawing while another window covers them; this switch turns that off.
    // =================================================================================
    static class FrozenFix
    {
        public const string Flag = "--disable-features=CalculateNativeWinOcclusion";

        public static string ExePath(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return null;
            return U.ExePathOf(U.Pid(hwnd));                                 // also for apps running as administrator
        }

        /// <summary>True when the app looks Chromium/Electron/CEF based (these are the ones that freeze).</summary>
        public static bool LooksChromium(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return false;
            try
            {
                string dir = Path.GetDirectoryName(exe);
                if (File.Exists(Path.Combine(dir, "chrome_elf.dll")) || File.Exists(Path.Combine(dir, "libcef.dll"))) return true;
                if (File.Exists(Path.Combine(dir, "resources", "app.asar")) || File.Exists(Path.Combine(dir, "resources.pak"))) return true;
                foreach (var sub in Directory.GetDirectories(dir))   // Chrome / Edge keep chrome_elf.dll in a version folder
                    if (File.Exists(Path.Combine(sub, "chrome_elf.dll")) || File.Exists(Path.Combine(sub, "msedge_elf.dll"))) return true;
            }
            catch { }
            string n = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
            return n == "chrome" || n == "msedge" || n == "brave" || n == "opera" || n == "vivaldi" || n == "spotify" || n == "discord" || n == "slack" || n == "code";
        }

        /// <summary>Closes the app and starts it again with the fix. Asks first.</summary>
        public static void Relaunch(IntPtr hwnd)
        {
            string exe = ExePath(hwnd);
            string name = U.ProcessName(hwnd);
            if (exe == null)
            {
                MainUI.Ask("Fix frozen picture", "This app's program file can't be read (it may be running as administrator), so it can't be restarted from here.", "OK", null, null);
                return;
            }
            MainUI.Ask("Restart " + name + "?",
                "This closes " + name + " and starts it again with a setting that keeps its window drawing while other windows cover it.\n\n" +
                "Anything unsaved in it will be lost. It only helps Chromium-based apps (Chrome, Edge, Spotify, Discord, ...).",
                "Restart " + name, "Cancel", yes => { if (yes) RelaunchNow(hwnd); }, true);
        }

        /// <summary>Closes the app and starts it again with the fix, without asking (the caller already did).</summary>
        public static void RelaunchNow(IntPtr hwnd)
        {
            string exe = ExePath(hwnd);
            string name = U.ProcessName(hwnd);
            if (exe == null) { MainUI.Ask("Fix frozen picture", "This app's program file can't be read (it may be running as administrator), so it can't be restarted from here.", "OK", null, null); return; }
            foreach (var pr in Process.GetProcessesByName(name)) { try { pr.Kill(); } catch { } pr.Dispose(); }
            try { Process.Start(new ProcessStartInfo(exe, Flag) { UseShellExecute = false }); } catch { }
        }

        /// <summary>Every shortcut on the desktop, in the Start menu and pinned to the taskbar that points at this exe.</summary>
        public static List<string> FindShortcuts(string exe)
        {
            var found = new List<string>();
            var dirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar")
            };
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return found;
                dynamic sh = Activator.CreateInstance(t);
                foreach (var d in dirs)
                {
                    if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
                    string[] files;
                    try { files = Directory.GetFiles(d, "*.lnk", SearchOption.AllDirectories); } catch { continue; }
                    foreach (var f in files)
                    {
                        try
                        {
                            dynamic lk = sh.CreateShortcut(f);
                            string target = lk.TargetPath;
                            if (!string.IsNullOrEmpty(target) && string.Equals(Path.GetFullPath(target), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) found.Add(f);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return found;
        }

        public static bool HasFlag(string args) { return args != null && args.IndexOf(Flag, StringComparison.OrdinalIgnoreCase) >= 0; }

        public static int CountWithFlag(List<string> files)
        {
            int n = 0;
            try
            {
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                foreach (var f in files) { try { dynamic lk = sh.CreateShortcut(f); if (HasFlag((string)lk.Arguments)) n++; } catch { } }
            }
            catch { }
            return n;
        }

        /// <summary>Adds (on) or removes the switch in the shortcuts. Returns (changed, failed).</summary>
        public static Tuple<int, int> EditShortcuts(List<string> files, bool on)
        {
            int ok = 0, bad = 0;
            try
            {
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                foreach (var f in files)
                {
                    try
                    {
                        dynamic lk = sh.CreateShortcut(f);
                        string a = (string)lk.Arguments ?? "";
                        if (on == HasFlag(a)) { ok++; continue; }
                        a = on ? (Flag + " " + a).Trim() : a.Replace(Flag, "", StringComparison.OrdinalIgnoreCase).Trim();
                        lk.Arguments = a;
                        lk.Save();
                        ok++;
                    }
                    catch { bad++; }
                }
            }
            catch { bad += files.Count; }
            return Tuple.Create(ok, bad);
        }
    }

    // =================================================================================
    //  Application core: PiP registry, actions, hotkeys, controller, tray, sessions
    // =================================================================================
    class App
    {
        public static App I;
        public static Settings S;
        public static Icon AppIcon;
        public static IntPtr AppIconHandle;

        // Hooks set by the main window (WPF) layer
        public static Action<string> OpenMain;        // open the main window on a page: windows, running, appearance, behavior, controller, shortcuts, settings
        public static Action CloseMain;

        public readonly List<PipWindow> Pips = new List<PipWindow>();
        PipWindow current;
        /// <summary>The PiP your shortcuts and controller steer (the last one you used or pointed at). With controller controls
        /// on, it alone wears the controller outline.</summary>
        public PipWindow Current
        {
            get { return current; }
            set { if (current == value) return; current = value; RefreshPadVisuals(); }
        }
        public bool HiddenAll;
        public int ModalOpen;
        public IntPtr FullscreenWindow = IntPtr.Zero;
        public ActionDef[] Actions;
        public event Action StateChanged;             // something the main window shows has changed

        // Controller
        public bool PadConnected, PadOn;
        public string PadKind = "xbox";
        public readonly Dictionary<string, int> PadMask = new Dictionary<string, int>();
        public readonly Dictionary<string, int> PadMode = new Dictionary<string, int>();   // 0 press, 1 double press, 2 hold
        int prevBtn, padTick, capMask, holdMask;
        Action<int> padCapture;
        string holdKey;
        DateTime holdStart;
        readonly Dictionary<string, DateTime> lastTap = new Dictionary<string, DateTime>();
        FocusSink sink;
        IntPtr savedFg;

        // Window history (for "follow the window I just left")
        IntPtr curFg, prevFg;
        public IntPtr PreviousWindow { get { return prevFg; } }

        readonly HostWindow host;
        readonly NotifyIcon tray;
        readonly Timer bgTimer = new Timer(), padTimer = new Timer(), fgTimer = new Timer(), trimTimer = new Timer();
        readonly List<int> registered = new List<int>();
        bool pickerOpen, cleaned, saveDue, settingsDue;
        int bgTicks;
        static DateTime lastErrorTip = DateTime.MinValue;

        public App(string[] args)
        {
            I = this;
            if (S == null) S = Settings.Load();
            // PiP Studio never needs to compete with a game for the CPU: the picture itself is drawn by Windows (DWM)
            try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal; } catch { }
            try { if (!string.IsNullOrEmpty(S.GpuPrefsAdded)) GpuPref.RemoveAdded(); } catch { }   // the GPU option is gone: undo what it set
            Theme.Refresh();
            AppIcon = U.MakeAppIcon(32, Theme.AccentBase);
            AppIconHandle = AppIcon.Handle;
            ToolStripManager.Renderer = new FluentMenuRenderer();

            Actions = new[] {
                new ActionDef("showapp", "Open PiP Studio", "App", "HK_ShowApp", 0, () => ShowMain(null)),
                new ActionDef("newpip", "Choose a window to PiP", "Pick", "HK_NewPip", 0, () => ShowPicker(null)),
                new ActionDef("pipactive", "PiP the active window", "PiP", "HK_PipActive", 0, PipActiveWindow),
                new ActionDef("region", "PiP a region of the screen", "Region", "HK_ScreenRegion", 0, () => PipScreenRegion(null)),
                new ActionDef("toggleall", "Hide / show all PiPs", "Hide", "HK_ToggleAll", 0x8000, ToggleHideAll),
                new ActionDef("clickthrough", "Click-through (ghost mode)", "Ghost", "HK_ClickThrough", 0x80, ToggleClickThrough),
                new ActionDef("up", "Move PiP up", "Up", "HK_MoveUp", 0x1, () => WithCurrent(p => p.MoveDir(0, -1))),
                new ActionDef("down", "Move PiP down", "Down", "HK_MoveDown", 0x2, () => WithCurrent(p => p.MoveDir(0, 1))),
                new ActionDef("left", "Move PiP left", "Left", "HK_MoveLeft", 0x4, () => WithCurrent(p => p.MoveDir(-1, 0))),
                new ActionDef("right", "Move PiP right", "Right", "HK_MoveRight", 0x8, () => WithCurrent(p => p.MoveDir(1, 0))),
                new ActionDef("corner", "Move PiP to the next corner", "Corner", "HK_CycleCorner", 0, () => WithCurrent(p => p.CycleCorner())),
                new ActionDef("nextmon", "Move PiP to the next display", "Display", "HK_NextMonitor", 0, () => WithCurrent(p => p.NextMonitor())),
                new ActionDef("grow", "Make PiP bigger", "Size +", "HK_Grow", 0x20000, () => WithCurrent(p => p.StepSize(1))),
                new ActionDef("shrink", "Make PiP smaller", "Size −", "HK_Shrink", 0x10000, () => WithCurrent(p => p.StepSize(-1))),
                new ActionDef("opup", "More opaque", "Opacity +", "HK_OpacityUp", 0x400000, () => WithCurrent(p => p.StepOpacity(0.1))),
                new ActionDef("opdown", "More transparent", "Opacity −", "HK_OpacityDown", 0x800000, () => WithCurrent(p => p.StepOpacity(-0.1))),
                new ActionDef("theater", "Theater mode on / off", "Theater", "HK_Theater", 0x4000, () => WithCurrent(p => p.ToggleTheater())),
                new ActionDef("prevsrc", "Show the previous window (wraps around through Auto)", "Prev", "HK_PrevSource", 0x100, () => WithCurrent(p => p.CycleSource(-1))),
                new ActionDef("nextsrc", "Show the next window (wraps around through Auto)", "Next", "HK_NextSource", 0x200, () => WithCurrent(p => p.CycleSource(1))),
                new ActionDef("follow", "Follow the window I just left (on / off)", "Follow", "HK_Follow", 0, () => WithCurrent(p => p.SetFollow(!p.St.Follow))),
                new ActionDef("dodge", "Move out of the mouse's way (on / off)", "Dodge", "HK_Dodge", 0, ToggleDodge),
                new ActionDef("swap", "Bring the source window to the front", "Show", "HK_Swap", 0x1000, () =>
                {
                    // you chose this window: turning controller controls off later shouldn't send you back to the game
                    if (PadOn) savedFg = IntPtr.Zero;
                    WithCurrent(p => p.BringSourceToFront());
                }),
                new ActionDef("mute", "Mute / unmute the source app", "Mute", "HK_Mute", 0x40, () => WithCurrent(p => p.ToggleMute())),
                new ActionDef("volup", "Turn the source app's volume up", "Volume +", "HK_VolumeUp", 0x2000000, () => WithCurrent(p => p.VolumeStep(1))),
                new ActionDef("voldown", "Turn the source app's volume down", "Volume −", "HK_VolumeDown", 0x1000000, () => WithCurrent(p => p.VolumeStep(-1))),
                new ActionDef("playpause", "Media play / pause", "Play", "HK_PlayPause", 0, () => U.MediaKey(0xB3)),
                new ActionDef("crop", "Crop the PiP", "Crop", "HK_Crop", 0, () => WithCurrent(p => p.StartCrop())),
                new ActionDef("close", "Close the PiP", "Close", "HK_Close", 0, () => WithCurrent(p => p.CloseAnimated())),
                new ActionDef("nextpip", "Control the next PiP", "Switch PiP", "HK_NextPip", 0x200000, () => SwitchPip(1)),
                new ActionDef("prevpip", "Control the previous PiP", "Switch PiP", "HK_PrevPip", 0x100000, () => SwitchPip(-1)),
                new ActionDef("pad", "Controller controls on / off", "Controls", "HK_Pad", 0x30, () => SetPadOn(!PadOn)),
                new ActionDef("startpip", "Start a PiP on Auto (when none is open)", "Start", "HK_StartPip", 0x30, StartPipIfNone),
                new ActionDef("padoff", "Turn controller controls off", "Off", null, 0x2000, () => SetPadOn(false)),
            };
            LoadPadBindings();
            PadOn = S.PadStartOn;
            applied = S.Clone();

            host = new HostWindow(this);

            tray = new NotifyIcon();
            tray.Icon = U.MakeAppIcon(Math.Max(16, SystemInformation.SmallIconSize.Width), Theme.AccentBase);
            tray.Text = "PiP Studio";
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Opening += (o, e) => { BuildTrayMenu(); e.Cancel = false; };
            tray.MouseClick += (o, e) => { if (e.Button == MouseButtons.Left) ShowMain(null); };
            tray.BalloonTipClicked += (o, e) => { var a = balloonClick; balloonClick = null; Safe(() => { if (a != null) a(); else ShowMain(null); }); };
            tray.BalloonTipClosed += (o, e) => balloonClick = null;
            tray.Visible = true;

            RegisterHotkeys();

            bgTimer.Interval = 500;
            bgTimer.Tick += (o, e) => Safe(BackgroundTick);
            bgTimer.Start();
            ExternalPip.StartWatching();
            padTimer.Interval = 16;
            padTimer.Tick += (o, e) => Safe(PadTick);
            padTimer.Start();
            fgTimer.Interval = 150;
            fgTimer.Tick += (o, e) => Safe(ForegroundTick);
            fgTimer.Start();
            trimTimer.Interval = 45000;
            trimTimer.Tick += (o, e) => { if (S.MinimizeMemory) U.Trim(); };
            trimTimer.Start();

            SystemEvents.SessionEnding += (o, e) => { try { SaveSession(); } catch { } CrashReports.CleanExit(); };   // Windows shutting down isn't a crash

            if (S.RestoreSessions) RestoreSession();
            bool silent = args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase))
                          || (Packaged.IsPackaged && args.Length == 0 && Packaged.LaunchedAtStartup());
            if (S.FirstRun)
            {
                S.FirstRun = false;
                S.Save();
                Balloon("PiP Studio is running", U.XboxMode
                    ? "Press " + HK.Display("HK_NewPip") + " any time to put a window in picture-in-picture. Start PiP Studio again to open its window."   // Xbox mode has no tray
                    : "Press " + HK.Display("HK_NewPip") + " any time to put a window in picture-in-picture. Click this tray icon to open PiP Studio.", true);
            }
            bool updated = args.Any(a => a.Equals("--updated", StringComparison.OrdinalIgnoreCase));   // restarted by Updater.Install
            string shots = MainUI.ShotsDir(args);
            // the window opens unless Windows started it (sign-in), also when PiPs came back: after a restart (as administrator,
            // or an update) it would otherwise seem to vanish
            bool replaced = args.Any(a => a.Equals("--replace", StringComparison.OrdinalIgnoreCase));
            if (shots != null) { MainUI.ShotsLog(args, "scheduled"); RunSoon(() => MainUI.TakeShots(shots), 1500); }      // the screenshots workflow
            else if (updated || replaced || !silent) RunSoon(() => ShowMain(updated ? "settings" : null), 50);
            else if (S.MinimizeMemory) RunSoon(U.Trim, 6000);
            Updater.Start(updated);
            Companion.Apply();                                            // the phone companion, if you turned it on
            CrashReports.CheckLastRun();                                  // offer to report it if the last run crashed
        }

        static void Safe(Action a) { try { a(); } catch (Exception ex) { ReportError(ex); } }

        public static void RunSoon(Action a, int ms)
        {
            var t = new Timer();
            t.Interval = Math.Max(1, ms);
            t.Tick += (o, e) => { t.Stop(); t.Dispose(); Safe(a); };
            t.Start();
        }

        public void RaiseState() { var h = StateChanged; if (h != null) h(); }

        // ---------------------------------------------------------------- errors & tips
        public static void ReportError(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                string f = Path.Combine(Settings.Dir, "errors.log");
                if (File.Exists(f) && new FileInfo(f).Length > 262144) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("s", U.CI) + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
            try
            {
                if (I != null && (DateTime.Now - lastErrorTip).TotalSeconds > 15)
                {
                    lastErrorTip = DateTime.Now;
                    I.Balloon("Something went wrong", ex.Message + " (click to open errors.log)", true, null, () =>
                    {
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Settings.Dir, "errors.log")) { UseShellExecute = true }); } catch { }
                    });
                }
            }
            catch { }
        }

        Action balloonClick;

        /// <summary>A Windows notification. Clicking it does <paramref name="onClick"/>, or opens PiP Studio at <paramref name="page"/>.</summary>
        public void Balloon(string title, string text, bool force = false, string page = "windows", Action onClick = null)
        {
            if (!force && !S.ShowTips) return;
            string pg = page;
            balloonClick = onClick ?? (() => ShowMain(pg));
            try { tray.ShowBalloonTip(4000, L.T(title), L.T(text), ToolTipIcon.Info); } catch { }
        }

        // ---------------------------------------------------------------- main window
        public void ShowMain(string page)
        {
            var h = OpenMain;
            if (h != null) h(page);
        }

        /// <summary>Settings were changed from the main window: apply them, save shortly after.</summary>
        Settings applied;   // the settings open PiPs were last updated with (to tell what just changed)

        /// <summary>Settings were changed in the main window: apply them to every open PiP right away, save shortly after.</summary>
        public void SettingsChanged(bool hotkeys = false)
        {
            Theme.Refresh();
            try { MainUI.RefreshExplainer(); MainUI.FillPositions(); } catch { }    // the explainer shows your new choice; Position offers middle spots with 8 spots on
            settingsDue = true;
            if (hotkeys) RegisterHotkeys();
            var old = applied ?? S.Clone();
            applied = S.Clone();
            foreach (var p in Pips.ToArray()) { try { p.ApplySettings(old); } catch (Exception ex) { ReportError(ex); } }
            if (old.AllowMultiple && !S.AllowMultiple) KeepOnlyOnePip();
            RaiseState();
        }

        public void ApplySettings()
        {
            foreach (var p in Pips.ToArray()) { try { p.ApplySettings(null); } catch { } }
        }

        /// <summary>One PiP at a time: keeps the one you used last and closes the rest.</summary>
        void KeepOnlyOnePip()
        {
            var keep = Current != null && !Current.IsClosing ? Current : Pips.LastOrDefault(p => !p.IsClosing);
            foreach (var p in Pips.ToArray()) if (p != keep && !p.IsClosing) p.CloseAnimated();
        }

        /// <summary>The open PiP to reuse when only one is allowed (null when more are allowed or none is open).</summary>
        PipWindow ReusablePip()
        {
            if (S.AllowMultiple) return null;
            var p = CurrentPip();
            if (p == null) return null;
            if (HiddenAll) HiddenAll = false;
            return p;
        }

        public void OnThemeMaybeChanged()
        {
            RunSoon(() =>
            {
                bool wasDark = Theme.Dark;
                Color wasAcc = Theme.AccentBase;
                Theme.Refresh();
                if (wasDark != Theme.Dark || wasAcc != Theme.AccentBase) ApplySettings();
            }, 200);
        }

        public void OnDisplayChanged()
        {
            RunSoon(() => { foreach (var p in Pips.ToArray()) { try { p.OnDisplayChanged(); } catch { } } }, 600);
        }

        // ---------------------------------------------------------------- hotkeys
        public void RegisterHotkeys()
        {
            UnregisterHotkeys();
            var failed = new List<string>();
            for (int i = 0; i < Actions.Length; i++)
            {
                if (Actions[i].HkField == null) continue;
                int m, vk;
                if (!HK.Parse(HK.Get(S, Actions[i].HkField), out m, out vk) || vk == 0) continue;
                int id = 100 + i;
                bool repeat = Actions[i].Key == "grow" || Actions[i].Key == "shrink" || Actions[i].Key == "opup" || Actions[i].Key == "opdown" || Actions[i].Key == "volup" || Actions[i].Key == "voldown";
                if (Native.RegisterHotKey(host.Handle, id, (uint)(m | (repeat ? 0 : 0x4000)), (uint)vk)) registered.Add(id);
                else failed.Add(HK.Format(m, vk));
            }
            if (failed.Count > 0) Balloon("Some shortcuts are taken by another app", string.Join(", ", failed) + " — click to change them.", false, "shortcuts");
        }

        public void UnregisterHotkeys()
        {
            foreach (int id in registered) Native.UnregisterHotKey(host.Handle, id);
            registered.Clear();
        }

        public void OnHotkey(int id)
        {
            if (id == FocusKeyId) { OnFocusKey(); return; }
            int i = id - 100;
            if (i < 0 || i >= Actions.Length) return;
            RunAction(Actions[i].Key);
        }

        public void RunAction(string key)
        {
            var a = Actions.FirstOrDefault(x => x.Key == key);
            if (a == null) return;
            // no PiP Studio PiP open: steer the browser's / app's own PiP window instead, if there is one
            try { if (S.ControlOtherPips && CurrentPip() == null && ExternalPip.Handle(key)) return; } catch (Exception ex) { ReportError(ex); }
            try { a.Run(); } catch (Exception ex) { ReportError(ex); }
        }

        public PipWindow CurrentPip()
        {
            if (Current != null && !Current.IsClosing && Pips.Contains(Current)) return Current;
            return Pips.LastOrDefault(p => !p.IsClosing);
        }

        void WithCurrent(Action<PipWindow> a)
        {
            var p = CurrentPip();
            if (p == null) { Balloon("No PiP is open", "Press " + HK.Display("HK_NewPip") + " to open one.", true); return; }
            a(p);
        }

        // ---------------------------------------------------------------- controller
        void LoadPadBindings()
        {
            PadMask.Clear(); PadMode.Clear();
            foreach (var a in Actions) { PadMask[a.Key] = a.PadDefault; PadMode[a.Key] = 0; }
            if (string.IsNullOrEmpty(S.PadBindings)) return;
            foreach (var part in S.PadBindings.Split(';'))
            {
                int eq = part.IndexOf('='), colon = part.IndexOf(':');
                if (eq <= 0 || colon <= eq) continue;
                string k = part.Substring(0, eq);
                int mask, mode;
                if (!PadMask.ContainsKey(k)) continue;
                if (int.TryParse(part.Substring(eq + 1, colon - eq - 1), out mask)) PadMask[k] = mask;
                if (int.TryParse(part.Substring(colon + 1), out mode) && mode >= 0 && mode <= 2) PadMode[k] = mode;
            }
        }

        public void SavePadBindings()
        {
            S.PadBindings = string.Join(";", Actions.Select(a => a.Key + "=" + PadMask[a.Key] + ":" + PadMode[a.Key]));
            settingsDue = true;
            RefreshPadVisuals();
        }

        public void ResetPadBindings()
        {
            S.PadBindings = "";
            LoadPadBindings();
            settingsDue = true;
            RefreshPadVisuals();
        }

        /// <summary>Records the next button combination (finished when every button is released).</summary>
        public void CapturePad(Action<int> done) { capMask = 0; padCapture = done; }
        public void CancelPadCapture() { padCapture = null; capMask = 0; }

        public bool PadActive { get { return PadConnected && PadOn; } }

        public void SetPadOn(bool on)
        {
            bool was = PadOn;
            PadOn = on;
            if (was != on && S.PadFocusSink && PadConnected)
            {
                if (on)
                {
                    IntPtr f = Native.GetForegroundWindow();
                    if (f != IntPtr.Zero && !U.IsOurs(f))
                    {
                        savedFg = f;
                        if (sink == null) sink = new FocusSink();
                        gameIsAdmin = !IsElevated && U.IsElevatedWindow(f);
                        TakeFocusForPad();                 // the game stops reacting to the controller while it isn't focused
                    }
                }
                else
                {
                    if (sink != null) sink.Release();
                    if (savedFg != IntPtr.Zero && Native.IsWindow(savedFg)) U.ForceForeground(savedFg);   // hand focus back to the game
                    savedFg = IntPtr.Zero;
                }
            }
            RefreshPadVisuals();
            var p = CurrentPip();
            if (on && gameIsAdmin && was != on && PadConnected) WarnAdminGame();
            if (p != null && was != on && PadConnected)
                p.Osd(!on ? "Controller controls off"
                    : gameIsAdmin ? "Controller controls on · The game runs as admin, so it still gets the controller"
                    : "Controller controls on · " + Pad.Name(PadMask["padoff"] != 0 ? PadMask["padoff"] : PadMask["pad"], PadKind) + " turns them off");
            else if (was != on && !PadConnected && on) Balloon("No controller found", "Connect an Xbox, PlayStation or Switch controller to use controller controls.", true, "controller");
            RaiseState();
        }

        // ---------------------------------------------------------------- taking focus from the game for controller controls
        // Windows only lets a program take the focus when your latest input went to it. A controller isn't input as far as
        // Windows is concerned (PiP Studio reads it directly), so after Start + Select it usually refuses, unless you happened
        // not to touch the keyboard or mouse for a few minutes. A shortcut does count (that's why Ctrl+Alt+G worked), so
        // PiP Studio briefly registers F24, a key no keyboard has and no game uses, sends itself that key, and takes the
        // focus when it arrives as a shortcut. A game running as administrator blocks all of this unless PiP Studio does too.
        const int FocusKeyId = 90;
        const ushort VK_F24 = 0x87;
        bool focusKeyArmed, gameIsAdmin;
        readonly HashSet<string> warnedAdmin = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TakeFocusForPad()
        {
            if (!gameIsAdmin && !focusKeyArmed && Native.RegisterHotKey(host.Handle, FocusKeyId, 0x4000, VK_F24))   // MOD_NOREPEAT
            {
                focusKeyArmed = true;
                var inputs = new Native.INPUT[2];
                inputs[0].type = inputs[1].type = 1;                               // INPUT_KEYBOARD
                inputs[0].u.ki.wVk = inputs[1].u.ki.wVk = VK_F24;
                inputs[1].u.ki.dwFlags = 2;                                        // KEYEVENTF_KEYUP
                if (Native.SendInput(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT))) == 2)
                {
                    RunSoon(() => { if (focusKeyArmed) { DisarmFocusKey(); sink.Grab(); RetryGrab(8); } }, 400);   // it never came: try anyway
                    return;
                }
                DisarmFocusKey();
            }
            sink.Grab();                                                           // F24 belongs to another app, or the game is admin
            RetryGrab(8);
        }

        void OnFocusKey()
        {
            if (!focusKeyArmed) return;
            DisarmFocusKey();
            if (!PadOn || sink == null || savedFg == IntPtr.Zero) return;
            sink.Grab();                                                           // allowed now: the latest input was ours
            RetryGrab(8);
        }

        void DisarmFocusKey()
        {
            focusKeyArmed = false;
            try { Native.UnregisterHotKey(host.Handle, FocusKeyId); } catch { }
        }

        /// <summary>Once per game per session: a notification that offers to restart PiP Studio as administrator.</summary>
        void WarnAdminGame()
        {
            string game = U.ProcessName(savedFg) ?? "";
            if (Packaged.IsPackaged || !warnedAdmin.Add(game)) return;              // the Store version can't run as administrator
            Balloon("The game runs as administrator", "So it keeps getting your controller. Click to restart PiP Studio as administrator, which fixes that.", true, null, RestartElevated);
        }

        /// <summary>Some games take focus straight back (or Windows refuses the first try): check again for about a second.</summary>
        void RetryGrab(int left)
        {
            if (left <= 0) return;
            var t = new Timer { Interval = 150 };
            t.Tick += (o, e) =>
            {
                t.Stop(); t.Dispose();
                if (!PadOn || sink == null || savedFg == IntPtr.Zero) return;
                IntPtr f = Native.GetForegroundWindow();
                if (f == savedFg || (f != IntPtr.Zero && !U.IsOurs(f))) { sink.Grab(); RetryGrab(left - 1); }
            };
            t.Start();
        }

        void RefreshPadVisuals()
        {
            var cur = CurrentPip();
            foreach (var p in Pips) { try { p.SetPadActive(PadActive && p == cur); } catch { } }
        }

        /// <summary>Hands control to the next (or previous) PiP, in reading order (top to bottom, left to right).</summary>
        void SwitchPip(int dir)
        {
            var list = Pips.Where(p => !p.IsClosing && p.Visible).OrderBy(p => p.TargetBounds.Top / 40).ThenBy(p => p.TargetBounds.Left).ToList();
            if (list.Count == 0) { Balloon("No PiP is open", "Press " + HK.Display("HK_NewPip") + " to open one.", true); return; }
            int i = list.IndexOf(CurrentPip());
            var next = list[i < 0 ? 0 : ((i + dir) % list.Count + list.Count) % list.Count];
            Current = next;
            RefreshPadVisuals();
            next.PadPulse();
            next.Osd(list.Count == 1 ? L.T("The only PiP") : U.Trunc(next.DisplayName, 50));
        }

        /// <summary>Button hints shown on the PiP while controller controls are on.</summary>
        /// <summary>Button hints shown on the PiP while controller controls are on: the buttons (as they look on this
        /// controller) and what they do.</summary>
        public List<Tuple<List<Pad.Glyph>, string>> PadHints()
        {
            var l = new List<Tuple<List<Pad.Glyph>, string>>();
            if (!S.PadHints) return l;
            var dirs = new[] { "up", "down", "left", "right" }.Select(k => PadMask[k]).Where(m => m != 0).ToList();
            if (dirs.Count > 0)
            {
                bool dpad = dirs.All(m => m >= 1 && m <= 8);
                var g = new List<Pad.Glyph>();
                if (dpad) g.Add(Pad.Group("D-pad"));
                else foreach (var m in dirs) { if (g.Count > 0) g.Add(Pad.Glyph.Of(" ")); g.AddRange(Pad.Glyphs(m, PadKind)); }
                l.Add(Tuple.Create(g, "Move"));
            }
            // paired controls share one hint: "R-Stick ↕  Opacity" when both halves sit on one axis of a stick or the D-pad
            var used = new HashSet<string> { "up", "down", "left", "right", "pad", "startpip" };
            foreach (var pr in new[] { Tuple.Create("grow", "shrink", "Size"), Tuple.Create("opup", "opdown", "Opacity"),
                                       Tuple.Create("volup", "voldown", "Volume"), Tuple.Create("nextsrc", "prevsrc", "Source"),
                                       Tuple.Create("nextpip", "prevpip", "Switch PiP") })
            {
                used.Add(pr.Item1); used.Add(pr.Item2);
                int m1 = PadMask[pr.Item1], m2 = PadMask[pr.Item2];
                if (m1 == 0 && m2 == 0) continue;
                if (m1 != 0 && m2 != 0)
                {
                    var axis = Axis(m1, m2);
                    List<Pad.Glyph> g;
                    if (axis != null) g = new List<Pad.Glyph> { Pad.Group(axis.Item1), Pad.Glyph.Of(axis.Item2) };
                    else { g = HintGlyphs(pr.Item1); g.Add(Pad.Glyph.Of("/")); g.AddRange(HintGlyphs(pr.Item2)); }
                    l.Add(Tuple.Create(g, pr.Item3));
                }
                else
                {
                    string k = m1 != 0 ? pr.Item1 : pr.Item2;
                    l.Add(Tuple.Create(HintGlyphs(k), Actions.First(x => x.Key == k).Brief));
                }
            }
            // every other bound control
            foreach (var a in Actions)
            {
                if (used.Contains(a.Key) || PadMask[a.Key] == 0) continue;
                l.Add(Tuple.Create(HintGlyphs(a.Key), a.Brief));
            }
            return l;
        }

        List<Pad.Glyph> HintGlyphs(string key)
        {
            var g = Pad.Glyphs(PadMask[key], PadKind);
            if (PadMode[key] == 1) g.Add(Pad.Glyph.Of("×2"));
            else if (PadMode[key] == 2) g.Add(Pad.Glyph.Of("hold"));
            return g;
        }

        /// <summary>("R-Stick", "↕") when two single-button bindings are the two ends of one axis of the same stick (or the D-pad).</summary>
        static Tuple<string, string> Axis(int a, int b)
        {
            var groups = new[] { Tuple.Create("D-pad", 0x1, 0x2, 0x4, 0x8), Tuple.Create("L-Stick", 0x40000, 0x80000, 0x100000, 0x200000), Tuple.Create("R-Stick", 0x400000, 0x800000, 0x1000000, 0x2000000) };
            foreach (var g in groups)
            {
                if ((a == g.Item2 && b == g.Item3) || (a == g.Item3 && b == g.Item2)) return Tuple.Create(g.Item1, "↕");
                if ((a == g.Item4 && b == g.Item5) || (a == g.Item5 && b == g.Item4)) return Tuple.Create(g.Item1, "↔");
            }
            return null;
        }

        static readonly HashSet<string> Continuous = new HashSet<string> { "opup", "opdown", "volup", "voldown", "grow", "shrink" };
        string repKey;
        int repMask;
        DateTime repNext;

        /// <summary>
        /// Next / previous through everything: Auto, every window, and other apps' own PiP windows. Landing on another app's PiP
        /// closes PiP Studio's PiP and hands the controls to that window; leaving it opens a PiP Studio PiP again.
        /// </summary>
        public void CycleAll(int dir, PipWindow own, IntPtr ext)
        {
            IntPtr autoMark = new IntPtr(-1);
            var exts = S.ControlOtherPips ? ExternalPip.FindAll() : new List<IntPtr>();
            var seq = new List<IntPtr> { autoMark };
            seq.AddRange(WinEnum.AltTab().Where(h => !U.IsOurs(h)));
            seq.AddRange(exts);
            int cur = own != null ? (own.St.Auto ? 0 : seq.IndexOf(own.Src)) : seq.IndexOf(ext);
            int n = seq.Count;
            int next = cur < 0 ? (dir > 0 ? 0 : n - 1) : ((cur + dir) % n + n) % n;
            IntPtr t = seq[next];

            if (exts.Contains(t))
            {
                ExternalPip.Preferred = t;
                if (own != null) own.CloseAnimated();
                ExternalPip.Announce(t);
                return;
            }
            if (own != null)
            {
                if (t == autoMark) own.SetAuto(true);
                else { own.ClearAuto(); own.Attach(t); }
                return;
            }
            // coming from another app's PiP: open a PiP Studio PiP for the pick
            ExternalPip.Preferred = IntPtr.Zero;
            var p = t == autoMark ? CreateAutoPip() : CreatePipForWindow(t);
            if (p != null) RefreshPadVisuals();
        }

        /// <summary>The window you were in before controller controls took focus is no longer where you want to return.</summary>
        public void ForgetSavedFocus() { if (PadOn) savedFg = IntPtr.Zero; }

        ExtOverlay extOverlay;
        IntPtr extH;
        DateTime extFind;

        /// <summary>Controller controls on and steering another app's PiP: border and hints on top of it.</summary>
        void SyncExternalOverlay(int pressed)
        {
            bool want = PadOn && PadConnected && S.ControlOtherPips && CurrentPip() == null;
            if (want)
            {
                if ((DateTime.UtcNow - extFind).TotalMilliseconds > 400 || !Native.IsWindow(extH))
                {
                    extFind = DateTime.UtcNow;
                    extH = ExternalPip.Find();
                }
                want = extH != IntPtr.Zero && Native.IsWindowVisible(extH) && !Native.IsIconic(extH);
            }
            if (!want)
            {
                if (extOverlay != null && !extOverlay.IsDisposed && extOverlay.Visible) extOverlay.Hide();
                return;
            }
            if (extOverlay == null || extOverlay.IsDisposed) extOverlay = new ExtOverlay();
            ExternalPip.Pin(extH);                                       // controls are on it: in front, and it stays there afterwards
            if (pressed != 0) extOverlay.Input();
            extOverlay.Sync(extH);
        }

        void PadTick()
        {
            if (!S.PadEnabled)
            {
                if (PadConnected) { PadConnected = false; RefreshPadVisuals(); RaiseState(); }
                return;
            }
            padTick++;
            if (!PadConnected && padCapture == null && (padTick & 31) != 0) return;   // no controller: look for one about twice a second
            Pad.State s;
            bool ok = Pad.Read(out s);
            if (ok != PadConnected || (ok && s.Kind != PadKind))
            {
                PadConnected = ok;
                if (ok) PadKind = s.Kind;
                else if (sink != null) sink.Release();
                RefreshPadVisuals();
                RaiseState();
            }
            if (!ok) { SyncExternalOverlay(0); return; }
            int b = s.B, pressed = b & ~prevBtn;
            prevBtn = b;
            SyncExternalOverlay(pressed);

            if (padCapture != null)
            {
                if (b != 0) capMask |= b;
                else if (capMask != 0) { var cb = padCapture; int m = capMask; padCapture = null; capMask = 0; cb(m); }
                return;
            }

            // PiP Studio's own window is focused: the controller drives the window (controller-friendly mode)
            if (MainUI.HandlePad(b, pressed)) { holdKey = null; repKey = null; return; }

            if (pressed != 0 && PadOn) { var cp = CurrentPip(); if (cp != null) cp.PadPulse(); }

            if (holdKey != null)
            {
                if ((b & holdMask) != holdMask) holdKey = null;
                else if ((DateTime.UtcNow - holdStart).TotalMilliseconds >= 600) { string k = holdKey; holdKey = null; RunAction(k); }
            }
            // continuous controls (volume, opacity, size): keep going while the button or stick is held
            if (repKey != null)
            {
                if (!PadOn || (b & repMask) != repMask) repKey = null;
                else if (DateTime.UtcNow >= repNext) { repNext = DateTime.UtcNow.AddMilliseconds(80); RunAction(repKey); }
            }
            if (pressed == 0) return;

            string best = null;
            int bestN = 0;
            foreach (var a in Actions)
            {
                int mk = PadMask[a.Key];
                if (mk == 0 || (b & mk) != mk || (pressed & mk) == 0) continue;
                if (!PadOn && a.Key != "pad" && a.Key != "startpip") continue;   // controls off: only the on/off and start combos are heard
                // "start a PiP" and "controls on/off" can share a combo: with no PiP open it starts one, otherwise it toggles controls
                bool noPip = !Pips.Any(x => !x.IsClosing) && !(S.ControlOtherPips && ExternalPip.Find() != IntPtr.Zero);   // another app's PiP counts
                if (a.Key == "startpip" && !noPip) continue;
                if (a.Key == "pad" && noPip && PadMask["startpip"] == mk) continue;
                int n = BitOperations.PopCount((uint)mk);
                if (n > bestN) { best = a.Key; bestN = n; }
            }
            if (best == null) return;
            int mode = PadMode[best];
            if (mode == 0)
            {
                RunAction(best);
                if (Continuous.Contains(best)) { repKey = best; repMask = PadMask[best]; repNext = DateTime.UtcNow.AddMilliseconds(320); }
            }
            else if (mode == 1)
            {
                DateTime t0, now = DateTime.UtcNow;
                if (lastTap.TryGetValue(best, out t0) && (now - t0).TotalMilliseconds < 400) { lastTap.Remove(best); RunAction(best); }
                else lastTap[best] = now;
            }
            else { holdKey = best; holdMask = PadMask[best]; holdStart = DateTime.UtcNow; }
        }

        // ---------------------------------------------------------------- follow the previous window
        void ForegroundTick()
        {
            IntPtr f = Native.GetForegroundWindow();
            if (f == IntPtr.Zero || f == curFg || U.IsOurs(f) || !WinEnum.IsAltTab(f)) return;
            if (curFg != IntPtr.Zero && Native.IsWindow(curFg) && WinEnum.IsAltTab(curFg)) prevFg = curFg;
            curFg = f;
            if (prevFg == IntPtr.Zero) return;
            foreach (var p in Pips.ToArray())
            {
                if (p.IsClosing || !p.St.Follow || p.St.Kind != SourceKind.Window) continue;
                IntPtr to = prevFg;
                if (p.St.Auto && (!Native.IsWindow(to) || Native.IsIconic(to) || !WinEnum.IsAltTab(to)))
                    to = FirstAvailableWindow(p.Src);              // Auto: the window you left was minimized or closed, take the next available one
                if (to == IntPtr.Zero || p.Src == to) continue;
                p.Attach(to);
            }
        }

        void ToggleDodge()
        {
            S.HoverAction = S.HoverAction == 3 ? 0 : 3;
            settingsDue = true;
            var p = CurrentPip();
            if (p != null) p.Osd(S.HoverAction == 3 ? "Moves out of the mouse's way · hold Ctrl to grab it" : "Stays put under the mouse");
            RaiseState();
        }

        // ---------------------------------------------------------------- actions
        public void ShowPicker(PipWindow forPip)
        {
            if (pickerOpen) return;
            pickerOpen = true;
            ModalOpen++;
            try
            {
                while (true)
                {
                    var picked = QuickPicker.Show(forPip != null);
                    if (picked == null) return;
                    if (picked.PickOnScreen)
                    {
                        IntPtr h = ScreenWindowPicker.Pick();
                        if (h == IntPtr.Zero) continue;
                        UseWindow(h, forPip);
                        return;
                    }
                    if (picked.Region)
                    {
                        Rectangle r = SelectScreenRegion();
                        if (r.IsEmpty) continue;
                        UseScreen(r, forPip);
                        return;
                    }
                    if (picked.Window != IntPtr.Zero) UseWindow(picked.Window, forPip);
                    else if (!picked.Screen.IsEmpty) UseScreen(picked.Screen, forPip);
                    return;
                }
            }
            finally { pickerOpen = false; ModalOpen--; }
        }

        public void PickOnScreen(PipWindow forPip)
        {
            IntPtr h = ScreenWindowPicker.Pick();
            if (h != IntPtr.Zero) UseWindow(h, forPip);
        }

        public void UseWindow(IntPtr h, PipWindow forPip)
        {
            if (forPip != null && !forPip.IsClosing) { forPip.ClearAuto(); forPip.Attach(h); }
            else CreatePipForWindow(h);
        }

        public void UseScreen(Rectangle r, PipWindow forPip)
        {
            if (forPip != null && !forPip.IsClosing) forPip.SetScreenSource(r);
            else CreatePipForScreen(r);
        }

        public void PipActiveWindow()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero) fg = Native.GetAncestor(fg, 2);
            string cls = fg == IntPtr.Zero ? "" : U.ClassName(fg);
            if (fg == IntPtr.Zero || U.IsOurs(fg) || !Native.IsWindowVisible(fg) || cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd")
            {
                Balloon("Nothing to PiP", "Click the window you want first, then press " + HK.Display("HK_PipActive") + ".", true);
                return;
            }
            CreatePipForWindow(fg);
        }

        public void PipScreenRegion(PipWindow forPip)
        {
            Rectangle r = SelectScreenRegion();
            if (!r.IsEmpty) UseScreen(r, forPip);
        }

        Rectangle SelectScreenRegion()
        {
            ModalOpen++;
            try
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                using (var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppRgb))
                {
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(vs.Location, Point.Empty, vs.Size);
                    using (var sel = new RegionSelector(bmp, vs, RectangleF.Empty, true, "Drag to select the part of the screen you want to watch"))
                    {
                        if (sel.ShowDialog() != DialogResult.OK) return Rectangle.Empty;
                        RectangleF f = sel.ResultFraction;
                        return new Rectangle(
                            vs.X + (int)Math.Round(f.X * vs.Width), vs.Y + (int)Math.Round(f.Y * vs.Height),
                            Math.Max(16, (int)Math.Round(f.Width * vs.Width)), Math.Max(16, (int)Math.Round(f.Height * vs.Height)));
                    }
                }
            }
            finally { ModalOpen--; }
        }

        public PipWindow CreatePipForWindow(IntPtr h)
        {
            var reuse = ReusablePip();
            if (reuse != null) { reuse.ClearAuto(); reuse.Attach(h); App.I.Current = reuse; RaiseState(); return reuse; }
            var st = new PipState();
            st.Kind = SourceKind.Window;
            st.Opacity = S.DefaultOpacityPercent / 100.0;
            st.Follow = S.FollowPreviousDefault;
            return CreatePip(st, h);
        }

        /// <summary>The window you were using before the one in front (what Auto should show), else the first available one.</summary>
        public IntPtr LastUsedWindow(PipWindow self)
        {
            IntPtr fg = Native.GetForegroundWindow();
            foreach (var c in new[] { prevFg, U.IsOurs(fg) ? curFg : IntPtr.Zero })
            {
                if (c == IntPtr.Zero || c == fg || !Native.IsWindow(c) || Native.IsIconic(c) || U.IsOurs(c) || !WinEnum.IsAltTab(c)) continue;
                if (Pips.Any(x => x != self && !x.IsClosing && x.Src == c)) continue;   // already in another PiP
                return c;
            }
            IntPtr first = FirstAvailableWindow(IntPtr.Zero);
            return first != IntPtr.Zero ? first : (self != null ? self.Src : IntPtr.Zero);
        }

        /// <summary>Auto PiP whose window just came to the front: the window you left, or else the next available one.</summary>
        public IntPtr AutoAlternative(IntPtr src)
        {
            IntPtr p = prevFg;
            if (p != IntPtr.Zero && p != src && Native.IsWindow(p) && !Native.IsIconic(p) && !U.IsOurs(p) && WinEnum.IsAltTab(p)
                && !Pips.Any(x => !x.IsClosing && x.Src == p)) return p;
            return FirstAvailableWindow(src);
        }

        /// <summary>First window in Alt-Tab order that isn't ours, isn't the foreground window and isn't minimized.</summary>
        public IntPtr FirstAvailableWindow(IntPtr exclude)
        {
            IntPtr fg = Native.GetForegroundWindow();
            IntPtr fallback = IntPtr.Zero;
            foreach (var w in WinEnum.AltTab())
            {
                if (w == exclude || U.IsOurs(w) || Native.IsIconic(w)) continue;
                bool shown = false;
                foreach (var p in Pips) if (!p.IsClosing && p.Src == w) { shown = true; break; }
                if (shown) continue;
                if (w == fg) { if (fallback == IntPtr.Zero) fallback = w; continue; }
                return w;
            }
            return fallback;
        }

        /// <summary>Auto PiP: starts on the first available window, then always follows the last active one (ignores the follow setting).</summary>
        /// <summary>Opens a PiP on Auto when none is open. Sharing its combo with controller controls, it also turns those on, so you can place it right away.</summary>
        void StartPipIfNone()
        {
            var open = Pips.FirstOrDefault(x => !x.IsClosing);
            if (open != null) { open.Osd("A PiP is already open"); return; }
            var p = CreateAutoPip();
            if (p != null && PadConnected && PadMask["pad"] != 0 && PadMask["pad"] == PadMask["startpip"] && !PadOn) SetPadOn(true);
        }

        public PipWindow CreateAutoPip()
        {
            IntPtr h = FirstAvailableWindow(IntPtr.Zero);
            var reuse = ReusablePip();
            if (reuse != null)
            {
                if (h != IntPtr.Zero) reuse.Attach(h);
                reuse.St.Auto = true; reuse.St.Follow = true;
                Current = reuse; RaiseState(); SaveSoon();
                return reuse;
            }
            if (h == IntPtr.Zero) { Balloon("Nothing to PiP", "Open a window first — Auto picks it up from there.", true); return null; }
            var st = new PipState();
            st.Kind = SourceKind.Window;
            st.Opacity = S.DefaultOpacityPercent / 100.0;
            st.Auto = true;
            st.Follow = true;
            return CreatePip(st, h);
        }

        public PipWindow CreatePipForScreen(Rectangle r)
        {
            var reuse = ReusablePip();
            if (reuse != null) { reuse.SetScreenSource(r); App.I.Current = reuse; RaiseState(); return reuse; }
            var st = new PipState();
            st.Kind = SourceKind.Screen;
            st.ScreenRect = r;
            st.ClientOnly = false;
            st.Opacity = S.DefaultOpacityPercent / 100.0;
            return CreatePip(st, IntPtr.Zero);
        }

        public PipWindow CreatePip(PipState st, IntPtr src)
        {
            var p = new PipWindow(st, src);
            Pips.Add(p);
            Current = p;
            if (st.HasBounds && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(st.Bounds))) p.Bounds = st.Bounds;
            else p.PlaceDefault();
            p.Show();
            RefreshPadVisuals();
            SaveSoon();
            RaiseState();
            return p;
        }

        public void OnPipClosed(PipWindow p)
        {
            Pips.Remove(p);
            if (Current == p) Current = null;
            if (!cleaned) { SaveSoon(); RaiseState(); RefreshPadVisuals(); }
        }

        public void ToggleHideAll()
        {
            HiddenAll = !HiddenAll;
            if (HiddenAll && Pips.Count > 0) Balloon("PiPs hidden", "Press " + HK.Display("HK_ToggleAll") + " or click here to bring them back.", false, null, () => { if (HiddenAll) ToggleHideAll(); });
            RaiseState();
        }

        public void ToggleClickThrough()
        {
            var ghosts = Pips.Where(p => p.St.ClickThrough && !p.IsClosing).ToList();
            if (ghosts.Count > 0) { foreach (var p in ghosts) p.SetClickThrough(false); return; }
            WithCurrent(p => p.SetClickThrough(true));
        }

        public void CloseAll() { foreach (var p in Pips.ToArray()) p.CloseAnimated(); }

        /// <summary>Snap spots at the top and the middle of the sides stack downwards when taken; the others upwards.</summary>
        public static bool SpotGoesDown(string spot) { return spot.StartsWith("T") || spot == "L" || spot == "R"; }

        /// <summary>
        /// Where <paramref name="self"/> goes when it's put at <paramref name="want"/>, and which PiPs move for it
        /// (Stacking.Place): on the screen, stacked next to PiPs in the way or pushing them aside.
        /// </summary>
        public Rectangle Place(PipWindow self, Rectangle want, bool stackDown, bool stackFirst, bool allowBump, out Dictionary<PipWindow, Rectangle> bump)
        {
            int gap = (int)Math.Round(12 * self.UiScale);
            Rectangle wa = Screen.FromRectangle(want).WorkingArea;
            var others = Pips.Where(p => p != self && !p.IsClosing && p.Visible).ToList();
            var movable = others.Where(p => !p.St.Locked && !p.InTheater && wa.IntersectsWith(p.TargetBounds)).ToList();
            var stuck = others.Except(movable).Select(p => p.TargetBounds).ToList();
            Dictionary<int, Rectangle> plan;
            var r = Stacking.Place(want, movable.Select(p => p.TargetBounds).ToList(), stuck, wa, gap, stackDown, stackFirst, allowBump, out plan);
            bump = plan.ToDictionary(kv => movable[kv.Key], kv => kv.Value);
            return r;
        }

        /// <summary>
        /// The PiPs in the way of <paramref name="r"/> pushed one way (dx, dy), even past it: for swapping places with a
        /// controller. Null when they don't fit on the screen that way, or one of them can't move.
        /// </summary>
        public Dictionary<PipWindow, Rectangle> PlanPush(PipWindow self, Rectangle r, int dx, int dy)
        {
            int gap = (int)Math.Round(12 * self.UiScale);
            Rectangle wa = Screen.FromRectangle(r).WorkingArea;
            var others = Pips.Where(p => p != self && !p.IsClosing && p.Visible).ToList();
            var movable = others.Where(p => !p.St.Locked && !p.InTheater && wa.IntersectsWith(p.TargetBounds)).ToList();
            var stuck = others.Except(movable).Select(p => p.TargetBounds);
            var plan = Stacking.PlanBump(r, movable.Select(p => p.TargetBounds).ToList(), stuck, wa, gap, dx, dy);
            return plan == null ? null : plan.ToDictionary(kv => movable[kv.Key], kv => kv.Value);
        }

        public bool FullscreenOn(PipWindow p)
        {
            if (FullscreenWindow == IntPtr.Zero || FullscreenWindow == p.Src) return false;
            if (U.XboxMode) return false;                                 // there every app is full screen: PiPs would never show
            try { return Screen.FromHandle(FullscreenWindow).Bounds.IntersectsWith(p.Bounds); } catch { return false; }
        }

        void BackgroundTick()
        {
            FullscreenWindow = IntPtr.Zero;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && !U.IsOurs(fg))
            {
                string cls = U.ClassName(fg);
                if (cls != "Progman" && cls != "WorkerW" && cls != "Shell_TrayWnd")
                {
                    Rectangle wr = U.WindowRect(fg);
                    Rectangle sb = Screen.FromHandle(fg).Bounds;
                    if (wr.Left <= sb.Left && wr.Top <= sb.Top && wr.Right >= sb.Right && wr.Bottom >= sb.Bottom) FullscreenWindow = fg;
                }
            }
            bgTicks++;
            if (bgTicks % 6 == 0) { try { Layouts.Tick(); } catch (Exception ex) { ReportError(ex); } }   // layouts that open with an app
            if (bgTicks % 4 == 0)
            {
                if (saveDue) { saveDue = false; try { SaveSession(); } catch { } }
                if (settingsDue) { settingsDue = false; S.Save(); }
            }
        }

        // ---------------------------------------------------------------- tray menu
        /// <summary>"Layouts ›": each saved layout (picking one opens it in place of the PiPs that are open), and saving a new one.</summary>
        public static ToolStripMenuItem LayoutsMenu()
        {
            var m = Menus.Item("Layouts", null);
            foreach (var l in Layouts.All)
            {
                var lay = l;
                m.DropDownItems.Add(Menus.Item(lay.Name.Replace("&", "&&"), () => Layouts.Open(lay, true)));
            }
            if (Layouts.All.Count > 0) m.DropDownItems.Add(new ToolStripSeparator());
            m.DropDownItems.Add(Menus.Item("Save these PiPs as a layout…", () => { I.ShowMain("behavior"); MainUI.AskLayoutName(); }));
            return m;
        }

        void BuildTrayMenu()
        {
            var menu = tray.ContextMenuStrip;
            var items = menu.Items;
            Menus.Clear(items);
            var open = Menus.Item("Open PiP Studio", () => ShowMain(null), "HK_ShowApp");
            open.Font = new Font(SystemFonts.MenuFont, FontStyle.Bold);
            items.Add(open);
            if (Updater.CanInstall) items.Add(Menus.Item("Update to version " + Updater.Latest.ToString(3) + "…", () => ShowMain("settings")));
            foreach (var mh in ExternalPip.Minimized())                  // other apps' PiPs minimized from their title bar (some have no taskbar button)
            {
                var h = mh;
                items.Add(Menus.Item(("Restore " + ExternalPip.AppName(h) + "'s PiP").Replace("&", "&&"), () => ExternalPip.RestoreMinimized(h)));
            }
            items.Add(new ToolStripSeparator());
            items.Add(Menus.Item("Choose a window…", () => ShowPicker(null), "HK_NewPip"));
            items.Add(Menus.Item("Click a window on screen…", () => PickOnScreen(null)));
            items.Add(Menus.Item("Mirror a screen region…", () => PipScreenRegion(null), "HK_ScreenRegion"));
            items.Add(LayoutsMenu());

            var live = Pips.Where(p => !p.IsClosing).ToList();
            if (live.Count > 0)
            {
                items.Add(new ToolStripSeparator());
                foreach (var p in live)
                {
                    var pip = p;
                    var sub = Menus.Item(U.Trunc(pip.DisplayName, 42).Replace("&", "&&"), null);
                    sub.DropDownItems.Add(Menus.Item("Change source…", () => ShowPicker(pip)));
                    sub.DropDownItems.Add(Menus.Item("Crop…", pip.StartCrop));
                    sub.DropDownItems.Add(Menus.Check("Click-through", pip.St.ClickThrough, () => pip.SetClickThrough(!pip.St.ClickThrough)));
                    sub.DropDownItems.Add(Menus.Check("Follow the window I just left", pip.St.Follow, () => pip.SetFollow(!pip.St.Follow)));
                    sub.DropDownItems.Add(Menus.Item("Move back to the default spot", pip.PlaceDefaultAnimated));
                    sub.DropDownItems.Add(new ToolStripSeparator());
                    sub.DropDownItems.Add(Menus.Item("Close", pip.CloseAnimated));
                    items.Add(sub);
                }
            }

            items.Add(new ToolStripSeparator());
            if (PadConnected) items.Add(Menus.Check("Controller controls (" + Pad.FamilyName(PadKind) + ")", PadOn, () => SetPadOn(!PadOn), "HK_Pad"));
            items.Add(Menus.Check("Hide all PiPs", HiddenAll, ToggleHideAll, "HK_ToggleAll"));
            if (live.Any(p => p.St.ClickThrough))
                items.Add(Menus.Item("Turn off click-through everywhere", () => { foreach (var p in live) p.SetClickThrough(false); }));
            if (live.Count > 0) items.Add(Menus.Item("Close all PiPs", CloseAll));
            items.Add(new ToolStripSeparator());
            items.Add(Menus.Item("Settings", () => ShowMain("settings")));
            items.Add(Menus.Item("Exit", ExitApp));
            Menus.Style(menu);
        }

        public void PrepareMenuFocus() { try { Native.SetForegroundWindow(host.Handle); } catch { } }

        // ---------------------------------------------------------------- startup with Windows
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool StartupEnabled
        {
            get
            {
                if (Packaged.IsPackaged) return Packaged.StartupEnabled;      // Store version: the package's startup task
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("PiPStudio") != null; }
                catch { return false; }
            }
            set
            {
                if (Packaged.IsPackaged) { Packaged.StartupEnabled = value; return; }
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("PiPStudio", "\"" + Environment.ProcessPath + "\" --startup");
                    else k.DeleteValue("PiPStudio", false);
                }
            }
        }

        public static bool IsElevated
        {
            get
            {
                try
                {
                    using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                        return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        public void RestartElevated()
        {
            try
            {
                SaveSession();
                Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--replace") { UseShellExecute = true, Verb = "runas" });
            }
            catch { return; }   // the user said no to the UAC prompt
            ExitApp();
        }

        // ---------------------------------------------------------------- sessions
        static string SessionPath { get { return Path.Combine(Settings.Dir, "session.ini"); } }

        public void SaveSoon() { saveDue = true; }
        public void SaveSettingsSoon() { settingsDue = true; }

        public void SaveSession()
        {
            var sb = new StringBuilder();
            sb.AppendLine("; PiP Studio session — restored on next start");
            foreach (var p in Pips)
            {
                if (p.IsClosing) continue;
                p.SnapshotState();
                sb.AppendLine("[pip]");
                Ser.Write(p.St, sb);
            }
            Directory.CreateDirectory(Settings.Dir);
            U.SafeWrite(SessionPath, sb.ToString());
        }

        bool RestoreSession()
        {
            try
            {
                if (!File.Exists(SessionPath)) return false;
                var blocks = new List<List<string>>();
                List<string> cur = null;
                foreach (var raw in File.ReadAllLines(SessionPath))
                {
                    string t = raw.Trim();
                    if (t == "[pip]") { cur = new List<string>(); blocks.Add(cur); }
                    else if (cur != null) cur.Add(t);
                }
                int n = 0;
                blocks.RemoveAll(Retired);
                if (!S.AllowMultiple && blocks.Count > 1) blocks = blocks.Take(1).ToList();
                foreach (var b in blocks)
                {
                    var st = new PipState();
                    Ser.Read(st, Ser.ParseKv(b));
                    if (OpenState(st, S.AutoReattach) != null) n++;
                }
                return n > 0;
            }
            catch (Exception ex) { ReportError(ex); return false; }
        }

        /// <summary>A saved PiP of a kind PiP Studio no longer has (the utility PiPs of the 1.0.4 test builds): skipped.</summary>
        public static bool Retired(List<string> savedPip) { return savedPip.Any(l => l.Trim() == "Kind=Widget"); }

        /// <summary>
        /// Opens a saved PiP (a session or a layout): the same source if it's there (the window found again by its app, kind
        /// and title), in the same place. A window that isn't open yet gets a PiP that waits for it when <paramref name="wait"/>.
        /// Null when nothing opened (a window that isn't there).
        /// </summary>
        public PipWindow OpenState(PipState st, bool wait)
        {
            if (st.Kind == SourceKind.Screen)
            {
                if (st.ScreenRect.Width < 8 || st.ScreenRect.Height < 8) return null;
                return CreatePip(st, IntPtr.Zero);
            }
            IntPtr h = WinEnum.FindMatch(st.ProcessName, st.ClassName, st.Title);
            if (st.Auto) { st.Follow = true; if (h == IntPtr.Zero) h = FirstAvailableWindow(IntPtr.Zero); }
            if (h == IntPtr.Zero && !wait && !st.Auto) return null;
            return CreatePip(st, h);
        }

        // ---------------------------------------------------------------- exit
        public void ExitApp()
        {
            Cleanup();
            var wapp = System.Windows.Application.Current;
            if (wapp != null) wapp.Shutdown();
        }

        public void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            bgTimer.Stop(); padTimer.Stop(); fgTimer.Stop(); trimTimer.Stop();
            try { Companion.Stop(); } catch { }
            CrashReports.CleanExit();
            try { SaveSession(); } catch { }
            try { var c = CloseMain; if (c != null) c(); } catch { }
            foreach (var p in Pips.ToArray()) { try { p.ShutdownClose(); } catch { } }
            if (sink != null) { try { sink.Close(); } catch { } }
            UnregisterHotkeys();
            try { tray.Visible = false; tray.Dispose(); } catch { }
            try { S.Save(); } catch { }
            try { host.DestroyHandle(); } catch { }
        }
    }
}
