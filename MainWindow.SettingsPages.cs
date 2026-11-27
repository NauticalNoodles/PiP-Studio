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
    // The settings pages: Appearance, Behavior, Controller, Shortcuts, Settings and Phone. Part of MainUI (MainWindow.cs).
    static partial class MainUI
    {
        // ----------------------------------------------------------------------------- the settings pages
        static void BuildSettingsPages()
        {
            var S = App.S;
            string[] spots = { "Top left", "Top right", "Bottom left", "Bottom right" };

            // ---------------- Appearance
            var ap = NewPage("appearance");
            Section(ap, "Colors");
            Card(ap, "", "App theme", "Light, dark or OLED black, for PiP Studio's windows and menus",
                Combo(new[] { "Use my Windows setting", "Light", "Dark", "OLED" }, S.ThemeMode, v => S.ThemeMode = v), "light dark mode night oled black amoled");
            Button colorBtn = null;
            var accent = MakeExpander(ap, "", "Accent color", "Borders, highlights and controller hints", null, false, "color colour highlight");
            Row(accent, "Use my Windows accent color", null, Switch(() => S.UseSystemAccent, v => { S.UseSystemAccent = v; if (colorBtn != null) colorBtn.IsEnabled = !v; }), "system automatic");
            colorBtn = (Button)ColorButton(() => S.AccentColor, v => S.AccentColor = v);
            colorBtn.IsEnabled = !S.UseSystemAccent;
            Row(accent, "Custom accent color", "Used when the Windows accent color is off", colorBtn, "custom pick");

            Section(ap, "PiP window");
            Card(ap, "", "Rounded corners", "Windows 11 only", Switch(() => S.RoundedCorners, v => S.RoundedCorners = v), "round radius corner");
            var border = MakeExpander(ap, "", "Border", "A thin outline around each PiP, in your accent color or one you pick", Switch(() => S.ShowBorder, v => S.ShowBorder = v), false, "outline frame edge");
            Row(border, "Width", null, SliderBox(1, 8, S.BorderWidth, " px", v => S.BorderWidth = v), "thickness");
            var borderColorBtn = (Button)ColorButton(() => S.BorderColor, v => S.BorderColor = v);
            borderColorBtn.IsEnabled = !S.BorderUseAccent;
            Row(border, "Match accent color", null, Switch(() => S.BorderUseAccent, v => { S.BorderUseAccent = v; borderColorBtn.IsEnabled = !v; }), "accent theme");
            Row(border, "Border color", "Used when Match accent color is off", borderColorBtn, "colour outline pick letterbox");

            Card(ap, "\uE8C1", "Messages under the PiP", "When you switch sources or change a mode, the message shows below the PiP instead of over the picture", Switch(() => S.OsdBelow, v => S.OsdBelow = v), "caption osd title name label below toast");

            BuildTitleBarSettings(ap);

            Section(ap, "Effects");
            Card(ap, "\uE945", "Lite mode", "For slower PCs: turns off animations, the Mica background, shadows and live previews",
                Switch(() => S.LiteMode, v => { S.LiteMode = v; ApplyLite(); }), "performance fast low end slow lag light simple battery");
            Card(ap, "", "Animation effects", "Glide, fade and slide effects in PiP Studio and on PiPs", Switch(() => S.Animations, v => S.Animations = v), "motion animate smooth");

            // ---------------- Behavior
            var be = NewPage("behavior");
            Section(be, "New PiPs");
            var np = MakeExpander(be, "", "Where and how they open", null, null, true, "default new");
            Row(np, "Position", null, PositionCombo(), "corner start middle edge top bottom left right");
            Row(np, "Size", null, Combo(SizePresets.Names, S.DefaultSize, v => S.DefaultSize = v), "big small");
            Row(np, "Opacity", null, SliderBox(10, 100, S.DefaultOpacityPercent, "%", v => S.DefaultOpacityPercent = v), "transparency");
            var lim = MakeExpander(be, "\uE740", "Size limits", "Keep tall or wide apps from getting too big. Applies to new PiPs, size presets, resizing and source changes (theater mode ignores them).", null, false, "advanced maximum max width height tall huge big limit cap");
            Row(lim, "Limit width", null, Switch(() => S.LimitWidth, v => S.LimitWidth = v), "maximum width wide");
            Row(lim, "Maximum width", "Share of the screen", SliderBox(10, 95, S.MaxWidthPercent, "%", v => S.MaxWidthPercent = v), "maximum width wide percent");
            Row(lim, "Limit height", null, Switch(() => S.LimitHeight, v => S.LimitHeight = v), "maximum height tall");
            Row(lim, "Maximum height", "Share of the screen", SliderBox(10, 95, S.MaxHeightPercent, "%", v => S.MaxHeightPercent = v), "maximum height tall percent");
            Card(be, "\uE8A7", "Control other apps' PiP windows", "When no PiP Studio PiP is open, your shortcuts and controller move, resize, fade, mute and close the picture-in-picture window of Chrome, Edge, Firefox and other apps.",
                Switch(() => S.ControlOtherPips, v => S.ControlOtherPips = v), "chrome firefox edge browser native video picture in picture external other");
            Card(be, "\uE71E", "Find other apps' PiPs automatically", "Small always-on-top windows, like video players' mini modes, count as PiPs. Mark one as Not a PiP on the Running PiPs page.",
                Switch(() => S.DetectOtherPips, v => S.DetectOtherPips = v), "detect find automatic floating mini player always on top video telegram zoom spotify vlc");
            var marksBtn = new Button { Content = "Reset", Padding = new Thickness(14, 4, 14, 4) };
            marksBtn.Click += (o, e) => { ExternalPip.ClearMarks(); runSig = ""; winSig = ""; };
            Card(be, "\uE894", "Windows you marked", "Forget every Treat as a PiP and Not a PiP you chose", marksBtn, "mark treat not pip reset forget list");
            Card(be, "\uE7C9", "Show controls on other apps' PiPs", "Point at another app's PiP to get PiP Studio's title bar over it: drag it to move the PiP, scroll on it to change the volume, and use the title bar buttons you picked.",
                Switch(() => S.HoverOtherPips, v => S.HoverOtherPips = v), "hover controls title bar buttons point mouse chrome firefox discord external other");
            Card(be, "\uE8B0", "Snap other apps' PiPs to spots", "Other apps' PiP windows land on the same corners and edge spots as PiP Studio's PiPs, when they open and when you let go of them after dragging.",
                Switch(() => S.SnapOtherPips, v => S.SnapOtherPips = v), "snap corners spots chrome firefox discord external other");
            Card(be, "\uE737", "Allow more than one PiP at a time", "When off, opening a new PiP switches the one that's already open",
                Switch(() => S.AllowMultiple, v => S.AllowMultiple = v), "multiple several many limit one single");
            Card(be, "", "Follow the window I just left", "When you switch windows, the PiP switches to the one you were on",
                Switch(() => S.FollowPreviousDefault, v => S.FollowPreviousDefault = v), "auto switch previous last active alt tab");

            Section(be, "Placement");
            var snap = MakeExpander(be, "", "Snap after moving", "Glides to the nearest spot when you let go", Switch(() => S.SnapToCorners, v => S.SnapToCorners = v), false, "snap corner magnet");
            Row(snap, "Middle of each edge too", "8 spots instead of 4 corners — also used by the arrow keys and D-pad", Switch(() => S.SnapToEdges, v => S.SnapToEdges = v), "edges spots positions");
            Row(snap, "Gap from the screen edges", null, SliderBox(0, 120, S.EdgeMargin, " px", v => S.EdgeMargin = v), "margin distance");
            Card(be, "", "Stay on one display", "Moving past the edge won't hop to the next display", Switch(() => S.LockToMonitor, v => S.LockToMonitor = v), "monitor lock screen hop");

            Section(be, "Mouse");
            var hover = MakeExpander(be, "", "When the pointer is over a PiP", "Move out of the way slides it aside — hold Ctrl to grab it instead",
                Combo(new[] { "Show controls", "Fade out", "Do nothing", "Move out of the way" }, S.HoverAction, v => S.HoverAction = v), false, "hover mouse dodge fade controls");
            Row(hover, "Show move arrows", "Arrow buttons send the PiP to the next spot — or the next display", Switch(() => S.ShowMoveArrows, v => S.ShowMoveArrows = v), "arrows throw move");
            Row(hover, "Fade-out opacity", "How visible a PiP stays while it fades under the pointer", SliderBox(0, 100, S.HoverFadePercent, "%", v => S.HoverFadePercent = v), "transparent");
            Row(hover, "Click-through opacity", "How visible a ghost-mode PiP is under the pointer", SliderBox(0, 100, S.ClickThroughHoverPercent, "%", v => S.ClickThroughHoverPercent = v), "ghost transparent");
            Card(be, "", "Right-click on a PiP", null, Combo(new[] { "Show the menu", "Close it", "Bring its source to the front", "Theater mode", "Do nothing" }, S.RightClickAction, v => S.RightClickAction = v), "context menu right click");
            Card(be, "", "Middle-click closes a PiP", null, Switch(() => S.MiddleClickCloses, v => S.MiddleClickCloses = v), "wheel click close");

            Section(be, "Smart hiding");
            Card(be, "", "Hide a PiP while its window is in front", "It would only repeat what's already on screen", Switch(() => S.HideWhenSourceActive, v => S.HideWhenSourceActive = v), "auto hide active focused duplicate");
            Card(be, "", "Hide PiPs while a fullscreen app is on the same display", "Borderless games still show PiPs unless this is on", Switch(() => S.HideInFullscreen, v => S.HideInFullscreen = v), "game fullscreen presentation");

            Section(be, "Sources");
            Card(be, "", "Reconnect when a closed app opens again", null, Switch(() => S.AutoReattach, v => S.AutoReattach = v), "reattach restore reopen");
            Card(be, "", "Keep minimized sources playing", "Minimizing a source tucks it off-screen so the PiP keeps moving. Click it in the taskbar to bring it back.", Switch(() => S.KeepAliveMinimized, v => S.KeepAliveMinimized = v), "minimize park hide offscreen");
            var cap = MakeExpander(be, "", "Screen regions", "Window sources always run at full speed", null, false, "fps capture region display");
            Row(cap, "Frame rate", null, SliderBox(5, 60, S.CaptureFps, " fps", v => S.CaptureFps = v), "fps smooth");
            Row(cap, "Show the pointer", null, Switch(() => S.CaptureCursor, v => S.CaptureCursor = v), "cursor mouse");
            BuildLayouts(be);

            // ---------------- Controller
            var pc = NewPage("controller");
            var padSwitch = new Wpf.Ui.Controls.ToggleSwitch();
            padSwitch.Click += (o, e) => App.I.SetPadOn(padSwitch.IsChecked == true);
            var status = Card(pc, "", "Controller controls", " ", padSwitch, "gamepad xbox playstation switch joystick");
            Card(pc, "\uE7FC", "Controller-friendly mode", "Bigger layout, a focus ring and button hints, so you can use this window with a controller. Pressing a key on the keyboard switches back.",
                Combo(new[] { "Off", "When I use a controller", "Always" }, S.PadUiMode, v => { S.PadUiMode = v; Dispatch(() => SetPadVisual(v == 2)); }), "gamepad console big picture tv couch navigate ui");
            Card(pc, "\uE7B3", "Fade controller hints when idle", "The PiP's name and button hints fade after a few seconds without input. Any button brings them back. Less for Windows to draw over your game.",
                Switch(() => S.PadHintsAutoHide, v => S.PadHintsAutoHide = v), "hints fade hide idle performance fps");
            Section(pc, "Options");
            var opts = MakeExpander(pc, "", "Controller options", null, null, false, "gamepad options");
            Row(opts, "Use game controllers", "Xbox, PlayStation, Switch and most others", Switch(() => S.PadEnabled, v => S.PadEnabled = v), "enable");
            Row(opts, "Turn controls on when PiP Studio starts", null, Switch(() => S.PadStartOn, v => S.PadStartOn = v), "startup");
            Row(opts, "Release the game's focus while controls are on", "The game ignores the controller while you move the PiP, and gets focus back afterwards", Switch(() => S.PadFocusSink, v => S.PadFocusSink = v), "focus game sink");
            Row(opts, "Show button hints on the PiP", null, Switch(() => S.PadHints, v => S.PadHints = v), "hints help chips");
            Section(pc, "Buttons");
            var resetPad = new Button { Content = "Reset" };
            var binds = MakeExpander(pc, "", "Button assignments", "Click a binding, then press the button or combination. Right-click clears it. Trigger: press, double press or hold.", resetPad, true, "binding buttons");
            var padButtons = new Dictionary<string, Button>();
            var modeButtons = new Dictionary<string, Button>();
            string[] modeNames = { "Press", "Double press", "Hold" };
            foreach (var a in App.I.Actions)
            {
                var act = a;
                var pb = new Button { MinWidth = 150, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Click, then press the button(s). Right-click to clear." };
                var mb = new Button { MinWidth = 112, ToolTip = "Press once, press twice quickly, or hold" };
                var ctl = new StackPanel { Orientation = Orientation.Horizontal };
                ctl.Children.Add(pb);
                ctl.Children.Add(mb);
                padButtons[act.Key] = pb;
                modeButtons[act.Key] = mb;
                pb.Click += (o, e) =>
                {
                    if (!App.I.PadConnected) { pb.Content = "Connect a controller"; DelayRun(1500, () => { if (refreshBindings != null) refreshBindings(); }); return; }
                    pb.Content = "Press button(s)…";
                    App.I.CapturePad(mask => { App.I.PadMask[act.Key] = mask; App.I.SavePadBindings(); if (refreshBindings != null) refreshBindings(); });
                };
                pb.MouseRightButtonUp += (o, e) => { App.I.PadMask[act.Key] = 0; App.I.SavePadBindings(); if (refreshBindings != null) refreshBindings(); };
                mb.Click += (o, e) => { App.I.PadMode[act.Key] = (App.I.PadMode[act.Key] + 1) % 3; App.I.SavePadBindings(); if (refreshBindings != null) refreshBindings(); };
                Row(binds, act.Label, null, ctl, "controller button gamepad binding " + act.Brief);
            }
            resetPad.Click += (o, e) => { App.I.ResetPadBindings(); if (refreshBindings != null) refreshBindings(); };
            refreshBindings = () =>
            {
                var A = App.I;
                foreach (var kv in padButtons) kv.Value.Content = Pad.Name(A.PadMask[kv.Key], A.PadKind);
                foreach (var kv in modeButtons) kv.Value.Content = modeNames[A.PadMode[kv.Key]];
                padSwitch.IsChecked = A.PadOn;
                padSwitch.IsEnabled = A.PadConnected;
                status.Title.Text = A.PadConnected ? Pad.FamilyName(A.PadKind) + " connected" : "No controller connected";
                status.Desc.Text = A.PadConnected
                    ? (A.PadOn ? "Controls are on — the PiP has an accent outline. " + Pad.Name(A.PadMask["padoff"], A.PadKind) + " turns them off."
                               : "Press " + Pad.Name(A.PadMask["pad"], A.PadKind) + " to turn controls on.")
                    : "Connect an Xbox, PlayStation or Switch controller by USB or Bluetooth.";
            };
            refreshBindings();

            // ---------------- Shortcuts (grouped, like Windows Settings)
            var sc = NewPage("shortcuts");
            Note(sc, "These work anywhere in Windows. Click a shortcut, then press the new keys (Backspace clears, Esc cancels). Right-click clears it. They act on the PiP you used or pointed at most recently.");
            var groups = new[]
            {
                Tuple.Create("", "Open and choose", new[] { "showapp", "newpip", "pipactive", "region", "prevsrc", "nextsrc", "follow" }),
                Tuple.Create("", "Move and size", new[] { "up", "down", "left", "right", "corner", "nextmon", "grow", "shrink", "theater", "dodge" }),
                Tuple.Create("", "Look and visibility", new[] { "opup", "opdown", "toggleall", "clickthrough", "crop" }),
                Tuple.Create("", "Source and media", new[] { "startpip", "swap", "mute", "volup", "voldown", "playpause", "pad", "close" }),
            };
            var keyButtons = new List<Tuple<string, Button>>();
            bool firstGroup = true;
            foreach (var grp in groups)
            {
                var ex = MakeExpander(sc, grp.Item1, grp.Item2, null, null, firstGroup, "shortcut hotkey keyboard");
                firstGroup = false;
                foreach (var key in grp.Item3)
                {
                    var act = App.I.Actions.FirstOrDefault(x => x.Key == key);
                    if (act == null || act.HkField == null) continue;
                    var kb = new Button { MinWidth = 190, Content = KeyText(act.HkField), ToolTip = "Click, then press keys. Right-click to clear." };
                    string field = act.HkField;
                    kb.Click += (o, e) => BeginKeyCapture(field, kb);
                    kb.MouseRightButtonUp += (o, e) => { HK.Set(App.S, field, ""); kb.Content = KeyText(field); App.I.SettingsChanged(true); };
                    keyButtons.Add(Tuple.Create(field, kb));
                    Row(ex, act.Label, null, kb, "shortcut hotkey keyboard key " + act.Brief);
                }
            }
            Card(sc, "", "Reset shortcuts", "Puts every shortcut back to its default", PlainButton("Reset", () =>
            {
                var d = new Settings();
                foreach (var f in typeof(Settings).GetFields().Where(f => f.Name.StartsWith("HK_"))) f.SetValue(App.S, f.GetValue(d));
                foreach (var kb in keyButtons) kb.Item2.Content = KeyText(kb.Item1);
                App.I.SettingsChanged(true);
            }), "default restore");

            // ---------------- Settings (general)
            var ge = NewPage("settings");
            Section(ge, "Language");
            Card(ge, "\uE774", "Language", "Switches right away",
                Combo(L.Names, Math.Max(0, Array.IndexOf(L.Codes, S.Language ?? "")), v =>
                {
                    if (L.Codes[v] == (S.Language ?? "")) return;
                    S.Language = L.Codes[v];
                    w.Dispatcher.BeginInvoke(new Action(SwitchLanguage), DispatcherPriority.Background);
                }), "language arabic chinese english spanish portuguese french german russian japanese translate idioma idioma langue sprache язык 言語 عربي 中文 语言 اللغة");
            Section(ge, "Startup");
            Card(ge, "", "Start PiP Studio when I sign in", "It starts quietly in the tray", Switch(() => App.StartupEnabled, v => { try { App.StartupEnabled = v; } catch { } }), "boot login windows startup");
            Card(ge, "", "Bring back my PiPs when PiP Studio starts", null, Switch(() => S.RestoreSessions, v => S.RestoreSessions = v), "restore session remember layout");
            Card(ge, "", "Notifications", "Tips and alerts from the tray icon", Switch(() => S.ShowTips, v => S.ShowTips = v), "balloon toast notification tips");
            Section(ge, "Privacy");
            Card(ge, "", "Hide PiPs from screenshots, recordings and screen sharing", "OBS, Discord, Teams and the Snipping Tool won't see them. Costs a little performance, so leave it off unless you stream or share your screen.", Switch(() => S.HideFromCapture, v => S.HideFromCapture = v), "capture stream share record obs");
            Section(ge, "Performance");
            Card(ge, "", "Keep running in the tray when I close this window", "Otherwise closing this window quits PiP Studio", Switch(() => S.CloseToTray, v => S.CloseToTray = v), "tray background close quit");
            Card(ge, "", "Release memory while idle", "Gives unused memory back to Windows while PiP Studio sits in the tray", Switch(() => S.MinimizeMemory, v => S.MinimizeMemory = v), "ram memory trim");
            {
                var bk = new StackPanel { Orientation = Orientation.Horizontal };
                var exp = PlainButton("Export…", ExportSettings);
                exp.Margin = new Thickness(0, 0, 8, 0);
                bk.Children.Add(exp);
                bk.Children.Add(PlainButton("Import…", ImportSettings));
                Card(ge, "\uE777", "Back up settings", "Save your settings, shortcuts and layouts to a file, to keep a copy or move them to another PC. Paired phones stay on this PC.", bk, "backup export import save restore transfer move copy file");
            }
            if (!Packaged.IsPackaged)                                     // the Store version can't restart itself elevated
            {
                Section(ge, "Administrator");
                if (App.IsElevated) Card(ge, "", "Running as administrator", "PiP Studio can show windows of apps that run as administrator", null, "admin elevated uac");
                else Card(ge, "", "Restart as administrator", "Needed only to show windows of apps that run as administrator", PlainButton("Restart", App.I.RestartElevated), "admin elevated uac");
                var always = new Wpf.Ui.Controls.ToggleSwitch { IsChecked = S.AlwaysAdmin && AdminStart.TaskExists() };
                always.Click += (o, e) =>
                {
                    bool want = always.IsChecked == true;
                    bool ok = want ? AdminStart.Create() : AdminStart.Delete();   // Windows asks for permission either way
                    if (!ok) { always.IsChecked = !want; return; }               // you said no: nothing changed
                    S.AlwaysAdmin = want;
                    App.I.SettingsChanged();
                    if (want && !App.IsElevated)
                        Ask("Restart as administrator now?", "From now on PiP Studio starts as administrator without asking, also when it starts at sign-in.",
                            "Restart now", "Later", yes => { if (yes) AdminStart.RestartThroughTask(); });
                };
                Card(ge, "\uE7EF", "Always start as administrator", "Without asking each time, also at sign-in. For games and apps that run as administrator: PiP Studio can then show their windows and take their focus for controller controls. Windows asks once to set it up.", always, "admin elevated uac always startup sign in task");
            }
            if (Updater.Supported) BuildUpdateSettings(ge, S);   // the Store version gets its updates from the Store
            Section(ge, "About");
            var about = MakeExpander(ge, "", "PiP Studio", "Version " + Updater.CurrentText, PlainButton("Exit PiP Studio", App.I.ExitApp), false, "version about exit quit");
            Row(about, "Settings folder", Packaged.RealPath(Settings.Dir), PlainButton("Open", () => { try { Directory.CreateDirectory(Settings.Dir); Process.Start("explorer.exe", "\"" + Packaged.RealPath(Settings.Dir) + "\""); } catch { } }), "folder files log");
            Row(about, "Report a problem", "Opens a bug report on GitHub with your PiP Studio and Windows versions and recent errors filled in", PlainButton("Report", CrashReports.ReportProblem), "bug report problem crash issue feedback github");
            Row(about, "Picture-in-picture for any window or part of the screen", "Live window mirroring uses Windows' own thumbnail system, so it costs almost nothing to run.", null, "about");

            BuildPhonePage(NewPage("phone"), S);                          // the phone companion has its own page
            NewPage("search");
        }
    }
}
