using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PipStudio
{
    // =================================================================================
    //  The floating PiP window
    // =================================================================================
    class PipWindow : Form
    {
        public readonly PipState St;
        public IntPtr Src;
        IntPtr thumb = IntPtr.Zero;
        PipOverlay overlay;
        readonly System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer slowTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer capTimer = new System.Windows.Forms.Timer();
        ContextMenuStrip menu;
        bool menuOpen;

        double visCur = 0, hoverCur = 1, lastEff = 0;
        bool layered = true;      // starts layered (invisible) for the fade-in; dropped once fully opaque
        int lastAlpha = -1;
        bool hovering, lastHoverPaint, lastPulsePaint;
        DateTime reattachForcedUntil = DateTime.MinValue;
        DateTime lastOver = DateTime.MinValue;
        bool animating;
        Rectangle animFrom, animTo;
        DateTime animStart;
        const double AnimMs = 230;
        bool inTheater;
        Rectangle theaterRestore;
        Bitmap capBmp;
        Rectangle capDst;
        bool parked;
        Native.WINDOWPLACEMENT parkedWp;
        bool sourceLost;
        DateTime lastReattach = DateTime.MinValue;
        Size lastSrcSize;
        bool lastIconic;
        DateTime lastAutoSkip = DateTime.MinValue;
        bool closingAnim, shuttingDown, moving;
        string osdText = "";
        DateTime osdUntil = DateTime.MinValue;
        float scale = 1f;
        public double ContentAspect = 16.0 / 9.0;
        public bool? MuteState;
        bool padActive;
        DateTime pulseUntil = DateTime.MinValue, dodgeAfter = DateTime.MinValue;
        bool thumbErrorShown;

        public float UiScale { get { return scale; } }
        public bool IsClosing { get { return closingAnim || IsDisposed || Disposing; } }
        public bool IsParked { get { return parked; } }
        public bool InTheater { get { return inTheater; } }
        public string OsdText { get { return osdText; } }
        double osdLevel = -1;
        /// <summary>0..1 when the current message has a level bar (volume), otherwise -1.</summary>
        public double OsdLevel { get { return OsdActive ? osdLevel : -1; } }
        public bool OsdActive { get { return DateTime.Now < osdUntil && osdText.Length > 0; } }
        public Rectangle TargetBounds { get { return animating ? animTo : Bounds; } }
        public bool HasSource { get { return St.Kind != SourceKind.Window || (Src != IntPtr.Zero && !sourceLost); } }

        public string DisplayName
        {
            get
            {
                if (St.Kind == SourceKind.Screen) return "Screen region " + St.ScreenRect.Width + "×" + St.ScreenRect.Height;
                if (!string.IsNullOrEmpty(St.Title)) return St.Title;
                return string.IsNullOrEmpty(St.ProcessName) ? "PiP" : St.ProcessName;
            }
        }

        public PipWindow(PipState st, IntPtr src)
        {
            St = st;
            Src = src;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = App.S.BackgroundColor;
            Icon = App.AppIcon;
            Text = "PiP Studio";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            MinimumSize = new Size(96, 54);
            scale = U.ScaleAt(Cursor.Position);

            if (St.Kind == SourceKind.Window)
            {
                if (Src != IntPtr.Zero && Native.IsWindow(Src)) CaptureSourceIdentity();
                else { Src = IntPtr.Zero; sourceLost = true; }
            }
            ComputeAspect();

            uiTimer.Interval = 15;
            uiTimer.Tick += (o, e) => SafeRun(UiTick);
            slowTimer.Interval = 400;
            slowTimer.Tick += (o, e) => SafeRun(SlowTick);
            capTimer.Tick += (o, e) => SafeRun(CaptureTick);
            overlay = new PipOverlay(this);
        }

        static void SafeRun(Action a) { try { a(); } catch (Exception ex) { App.ReportError(ex); } }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                if (St == null || St.TopMost) cp.ExStyle |= Native.WS_EX_TOPMOST;
                if (St == null || layered) cp.ExStyle |= Native.WS_EX_LAYERED;
                if (St != null && St.ClickThrough) cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetLayeredWindowAttributes(Handle, 0, 0, 2);   // invisible until the fade-in starts
            lastAlpha = 0;
            scale = U.ScaleOf(Handle);
            ApplyWindowChrome();
            RegisterThumb();
        }

        void ApplyWindowChrome()
        {
            if (!IsHandleCreated) return;
            U.RoundCorners(Handle, App.S.RoundedCorners);
            U.ExcludeFromCapture(Handle, App.S.HideFromCapture || St.Kind == SourceKind.Screen);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            overlay.Owner = this;
            if (St.Kind == SourceKind.Window && Src != IntPtr.Zero && Native.IsIconic(Src))
            {
                if (App.S.KeepAliveMinimized) Park();
                else Native.ShowWindow(Src, 4);
            }
            lastIconic = Src != IntPtr.Zero && Native.IsIconic(Src);
            capTimer.Interval = Math.Max(10, 1000 / Math.Max(1, App.S.CaptureFps));
            if (St.Kind != SourceKind.Window) capTimer.Start();
            uiTimer.Start();
            slowTimer.Start();
            if (St.ClickThrough) ApplyClickThrough();
        }

        /// <summary>Re-applies every setting to this open PiP. <paramref name="old"/> is what was set before, so changes to
        /// placement and "new PiP" defaults move, resize or restyle it now instead of only affecting the next PiP.</summary>
        public void ApplySettings(Settings old)
        {
            var S = App.S;
            BackColor = S.BackgroundColor;
            ApplyWindowChrome();
            capTimer.Interval = Math.Max(10, 1000 / Math.Max(1, S.CaptureFps));
            ComputeAspect();
            UpdateThumb();
            if (overlay != null) overlay.ApplySettings();
            Invalidate();
            if (old == null) { FitToAspect(false); return; }

            if (old.DefaultOpacityPercent != S.DefaultOpacityPercent) St.Opacity = S.DefaultOpacityPercent / 100.0;
            if (old.FollowPreviousDefault != S.FollowPreviousDefault && St.Kind == SourceKind.Window && !St.Auto) St.Follow = S.FollowPreviousDefault;
            if (S.KeepAliveMinimized && !old.KeepAliveMinimized && St.Kind == SourceKind.Window && Src != IntPtr.Zero && Native.IsIconic(Src) && !parked) Park();
            if (!S.ShowMoveArrows || old.HoverAction != S.HoverAction || old.TitleBarButtons != S.TitleBarButtons) { if (overlay != null) overlay.Invalidate(); }

            bool sizeChanged = old.DefaultSize != S.DefaultSize;
            // size limits changed: re-fit right away (FitToAspect below applies them)
            bool cornerChanged = old.DefaultCorner != S.DefaultCorner;
            bool placementChanged = old.EdgeMargin != S.EdgeMargin || old.SnapToEdges != S.SnapToEdges || old.SnapToCorners != S.SnapToCorners
                                    || old.ShowBorder != S.ShowBorder || old.BorderWidth != S.BorderWidth;
            if (moving || inTheater) { FitToAspect(false); return; }
            if (sizeChanged)
            {
                var wa = WorkArea();
                Size sz = FitSizeIn((int)(wa.Width * SizePresets.Frac(S.DefaultSize)), wa, 0.85);
                MoveTo(AnchoredResize(sz), true);
            }
            else FitToAspect(false);
            if (cornerChanged) SnapToSpot(StartTag(), true);
            else if ((placementChanged || sizeChanged) && SnapOn) SnapToSpot(NearestSpot(S.SnapToEdges), true);
            App.I.SaveSoon();
        }

        // ------------------------------------------------------------------ source
        void CaptureSourceIdentity()
        {
            St.ProcessName = U.ProcessName(Src);
            St.ClassName = U.ClassName(Src);
            St.Title = U.WindowTitle(Src);
        }

        Size FullSourceSize()
        {
            if (St.Kind == SourceKind.Screen) return St.ScreenRect.Size;
            return U.SourceSize(Src, St.ClientOnly);
        }

        Rectangle CropRect(Size sz)
        {
            RectangleF c = St.Crop;
            int x = (int)Math.Round(c.X * sz.Width), y = (int)Math.Round(c.Y * sz.Height);
            int w = Math.Max(1, (int)Math.Round(c.Width * sz.Width)), h = Math.Max(1, (int)Math.Round(c.Height * sz.Height));
            return new Rectangle(x, y, w, h);
        }

        void ComputeAspect()
        {
            if (St.Aspect > 0) { ContentAspect = AspectChoices.Value(St.Aspect); return; }   // you locked the shape
            if (St.Kind == SourceKind.Window && (Src == IntPtr.Zero || Native.IsIconic(Src))) return;
            Size sz = FullSourceSize();
            if (sz.Width < 2 || sz.Height < 2) return;
            Rectangle r = CropRect(sz);
            ContentAspect = U.Clamp(r.Width / (double)r.Height, 0.1, 10);
        }

        void RegisterThumb()
        {
            UnregisterThumb();
            if (!IsHandleCreated || St.Kind != SourceKind.Window || Src == IntPtr.Zero) return;
            IntPtr t;
            int hr = Native.DwmRegisterThumbnail(Handle, Src, out t);
            if (hr == 0 && t != IntPtr.Zero) { thumb = t; UpdateThumb(); return; }
            if (thumbErrorShown) return;
            thumbErrorShown = true;
            IntPtr srcCopy = Src;
            App.RunSoon(() =>
            {
                if (hr == unchecked((int)0x80070005) && !App.IsElevated && !Packaged.IsPackaged)
                {
                    MainUI.Ask("Needs administrator", U.ProcessName(srcCopy) + " is running as administrator, so Windows won't let PiP Studio show it. Restart PiP Studio as administrator?",
                        "Restart as administrator", "Not now", yes => { if (yes) App.I.RestartElevated(); });
                }
                else MainUI.Ask("Can't show this window", "Windows refused to mirror this window (error 0x" + hr.ToString("X8") + "). Try another window.", "OK", null, null);
            }, 50);
        }

        // ------------------------------------------------------------------ controller visuals
        public void SetPadActive(bool on)
        {
            if (on) lastPadInput = DateTime.Now;
            if (on == padActive) return;
            padActive = on;
            UpdateThumb();
            Invalidate();
            if (overlay != null) overlay.Invalidate();
        }
        public bool PadActiveShown { get { return padActive; } }

        PipOutline outline;

        /// <summary>Controller controls on and the PiP faded (opacity or hover fade): draw a solid outline on top so you can still see it.</summary>
        void SyncOutline()
        {
            bool want = padActive && !IsClosing && !closingAnim && (!Visible || lastEff < 0.9) && App.I.PadActive;   // also while hidden: shows where it is
            if (!want)
            {
                if (outline != null && !outline.IsDisposed && outline.Visible) outline.Hide();
                return;
            }
            if (outline == null || outline.IsDisposed) outline = new PipOutline(St.TopMost);
            Color c = DateTime.Now < pulseUntil ? Color.White : Theme.Accent;
            int t = Math.Max(2, (int)Math.Round(3 * UiScale));
            outline.Sync(Bounds, c, t, App.S.RoundedCorners);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible && !(padActive && App.I.PadActive) && outline != null && !outline.IsDisposed && outline.Visible) outline.Hide();
        }

        void DisposeOutline()
        {
            if (outline != null) { try { if (!outline.IsDisposed) { outline.Close(); outline.Dispose(); } } catch { } outline = null; }
        }

        DateTime lastPadInput = DateTime.MinValue;

        public void PadPulse()
        {
            lastPadInput = DateTime.Now;
            pulseUntil = DateTime.Now.AddMilliseconds(180);
            Invalidate();
        }

        // ------------------------------------------------------------------ source switching
        /// <summary>Shows the next / previous window in the Alt-Tab order (or the next display for a whole-screen PiP).</summary>
        public void CycleSource(int dir)
        {
            App.I.Current = this;
            if (St.Kind == SourceKind.Screen)
            {
                var screens = Screen.AllScreens;
                int i = Array.FindIndex(screens, sc => sc.Bounds == St.ScreenRect);
                if (i < 0) { Osd("This PiP shows a screen region"); return; }
                var ns = screens[((i + dir) % screens.Length + screens.Length) % screens.Length];
                SetScreenSource(ns.Bounds);
                Osd("Display " + (Array.IndexOf(screens, ns) + 1));
                return;
            }
            // other apps' PiPs join the cycle: Auto, windows, then their PiP windows, then back to Auto
            if (App.S.ControlOtherPips && ExternalPip.FindAll().Count > 0) { App.I.CycleAll(dir, this, IntPtr.Zero); return; }
            // the cycle is: Auto, window 1, window 2, ... window N, then back to Auto
            var list = WinEnum.AltTab().Where(h => !U.IsOurs(h)).ToList();
            if (St.Auto)
            {
                if (list.Count == 0) { Osd("No other windows"); return; }
                IntPtr pick = dir > 0 ? list[0] : list[list.Count - 1];
                ClearAuto();
                if (pick == Src) Osd(U.Trunc(DisplayName, 60)); else Attach(pick);
                return;
            }
            int cur = list.IndexOf(Src);
            int next = cur < 0 ? (dir > 0 ? 0 : list.Count - 1) : cur + dir;
            if (next < 0 || next >= list.Count) { SetAuto(true); return; }
            if (list[next] == Src) { Osd("No other windows"); return; }
            Attach(list[next]);
        }

        /// <summary>Called when the user explicitly picks a source: Auto stops and Follow returns to the settings default.</summary>
        public void ClearAuto()
        {
            if (!St.Auto) return;
            St.Auto = false;
            St.Follow = App.S.FollowPreviousDefault;
        }

        public void SetAuto(bool on)
        {
            if (St.Kind != SourceKind.Window) return;
            St.Auto = on;
            St.Follow = on || App.S.FollowPreviousDefault;
            if (!on) { Osd("Auto off"); return; }
            // Auto shows the window you were using before the one in front, so jump straight to it
            IntPtr want = App.I.LastUsedWindow(this);
            if (want != IntPtr.Zero && want != Src) Attach(want);
            Osd("Auto — " + U.Trunc(DisplayName, 50));
        }

        public void SetFollow(bool on)
        {
            St.Follow = on;
            if (!on) St.Auto = false;
            Osd(on ? "Follows the window you just left" : "Stays on " + U.Trunc(DisplayName, 40));
            if (on && App.I.PreviousWindow != IntPtr.Zero && App.I.PreviousWindow != Src && Native.IsWindow(App.I.PreviousWindow)
                && Native.GetForegroundWindow() != App.I.PreviousWindow && St.Kind == SourceKind.Window)
                Attach(App.I.PreviousWindow);
            App.I.SaveSoon();
        }

        public void FixFrozen()
        {
            if (St.Kind != SourceKind.Window || Src == IntPtr.Zero) return;
            IntPtr old = Src;
            FrozenFix.Relaunch(old);
            // the app comes back as a new window: reconnect to it when it appears
            sourceLost = true;
            UnregisterThumb();
            Src = IntPtr.Zero;
            parked = false;
            lastReattach = DateTime.Now;
            reattachForcedUntil = DateTime.Now.AddSeconds(40);
            Invalidate();
        }

        public bool SourceLooksChromium
        {
            get { return St.Kind == SourceKind.Window && Src != IntPtr.Zero && FrozenFix.LooksChromium(FrozenFix.ExePath(Src)); }
        }

        public void RightClick()
        {
            switch (App.S.RightClickAction)
            {
                case 1: CloseAnimated(); break;
                case 2: BringSourceToFront(); break;
                case 3: ToggleTheater(); break;
                case 4: break;
                default: ShowMenu(); break;
            }
        }

        void UnregisterThumb()
        {
            if (thumb != IntPtr.Zero) { Native.DwmUnregisterThumbnail(thumb); thumb = IntPtr.Zero; }
        }

        public void UpdateThumb()
        {
            if (thumb == IntPtr.Zero) return;
            var p = new Native.DWM_THUMBNAIL_PROPERTIES();
            bool iconic = Src == IntPtr.Zero || Native.IsIconic(Src);
            Size full = FullSourceSize();
            if (iconic || full.Width < 2 || full.Height < 2)
            {
                p.dwFlags = Native.DWM_TNP_VISIBLE;
                p.fVisible = false;
                Native.DwmUpdateThumbnailProperties(thumb, ref p);
                return;
            }
            Rectangle srcR = CropRect(full);
            Rectangle dst = U.ComputeFit(ref srcR, ContentRect(), St.Fit);
            bool wholeSource = !St.IsCropped && St.Fit != FitMode.Fill;   // like UniversalPiP: no source rectangle unless cropping
            p.dwFlags = Native.DWM_TNP_RECTDESTINATION | (wholeSource ? 0 : Native.DWM_TNP_RECTSOURCE) | Native.DWM_TNP_VISIBLE |
                        Native.DWM_TNP_OPACITY | Native.DWM_TNP_SOURCECLIENTAREAONLY;
            p.rcDestination = Native.RECT.From(dst);
            p.rcSource = Native.RECT.From(srcR);
            p.opacity = 255;
            p.fVisible = true;
            p.fSourceClientAreaOnly = St.ClientOnly;
            Native.DwmUpdateThumbnailProperties(thumb, ref p);
        }

        public void Attach(IntPtr h)
        {
            if (h == IntPtr.Zero || !Native.IsWindow(h)) return;
            if (parked) Unpark(false, true);
            capTimer.Stop();
            DisposeCapture();
            St.Kind = SourceKind.Window;
            Src = h;
            sourceLost = false;
            St.Crop = new RectangleF(0, 0, 1, 1);
            CaptureSourceIdentity();
            if (Native.IsIconic(h))
            {
                if (App.S.KeepAliveMinimized) Park();
                else Native.ShowWindow(h, 4);
            }
            lastIconic = Native.IsIconic(h);
            RegisterThumb();
            ComputeAspect();
            lastSrcSize = FullSourceSize();
            if (St.TrimBars) App.RunSoon(() => { if (!IsClosing && St.TrimBars && Src == h) TrimBarsNow(true); }, 700);
            FitToAspect(true);
            ApplyWindowChrome();
            Osd(U.Trunc(St.Title, 60));
            Invalidate();
            if (overlay != null) overlay.Invalidate();
            App.I.SaveSoon();
        }

        public void SetScreenSource(Rectangle r)
        {
            ClearAuto();
            if (parked) Unpark(false, true);
            UnregisterThumb();
            Src = IntPtr.Zero;
            sourceLost = false;
            St.Kind = SourceKind.Screen;
            St.ScreenRect = r;
            St.ClientOnly = false;
            St.Crop = new RectangleF(0, 0, 1, 1);
            St.Title = "";
            St.ProcessName = "";
            ComputeAspect();
            FitToAspect(true);
            ApplyWindowChrome();
            capTimer.Interval = Math.Max(10, 1000 / Math.Max(1, App.S.CaptureFps));
            capTimer.Start();
            Invalidate();
            App.I.SaveSoon();
        }

        void OnSourceLost()
        {
            UnregisterThumb();
            Src = IntPtr.Zero;
            parked = false;
            sourceLost = true;
            Invalidate();
            if (St.Auto)
            {
                IntPtr next = App.I.FirstAvailableWindow(IntPtr.Zero);
                if (next != IntPtr.Zero) { Attach(next); return; }
                Osd("Auto — waiting for a window");
                return;
            }
            if (!App.S.AutoReattach) CloseAnimated();
            else Osd("Source closed — waiting for it to come back");
        }

        void TryReattach()
        {
            IntPtr h = WinEnum.FindMatch(St.ProcessName, St.ClassName, St.Title);
            if (h == IntPtr.Zero) return;
            RectangleF keepCrop = St.Crop;
            Attach(h);
            St.Crop = keepCrop;
            ComputeAspect();
            UpdateThumb();
            FitToAspect(true);
        }

        bool IsSourceForeground()
        {
            if (Src == IntPtr.Zero) return false;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == Src) return true;
            return fg != IntPtr.Zero && Native.GetAncestor(fg, 3) == Src;
        }

        // ------------------------------------------------------------------ park (keep a hidden source rendering)
        public void Park()
        {
            if (St.Kind != SourceKind.Window || Src == IntPtr.Zero || parked) return;
            var wp = new Native.WINDOWPLACEMENT();
            wp.length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
            if (!Native.GetWindowPlacement(Src, ref wp)) return;
            parkedWp = wp;
            bool wasMax = wp.showCmd == 3 || (wp.showCmd == 2 && (wp.flags & 2) != 0);
            Size sz = wp.rcNormalPosition.ToRectangle().Size;
            if (wasMax || sz.Width < 50 || sz.Height < 50) sz = Screen.FromHandle(Src).WorkingArea.Size;
            Rectangle vs = SystemInformation.VirtualScreen;
            var nwp = wp;
            nwp.flags = 0;
            nwp.showCmd = 4; // SW_SHOWNOACTIVATE
            nwp.rcNormalPosition = new Native.RECT(vs.Right + 400, vs.Top + 100, vs.Right + 400 + sz.Width, vs.Top + 100 + sz.Height);
            Native.SetWindowPlacement(Src, ref nwp);
            parked = true;
            lastIconic = false;
            Osd("Source hidden — still playing");
            ComputeAspect();
            UpdateThumb();
        }

        public void Unpark(bool activate, bool exact = false)
        {
            if (!parked) return;
            parked = false;
            if (Src == IntPtr.Zero || !Native.IsWindow(Src)) return;
            var wp = parkedWp;
            bool wasMin = wp.showCmd == 2 || wp.showCmd == 6 || wp.showCmd == 7;
            if (wasMin && exact) wp.showCmd = 7;                       // SW_SHOWMINNOACTIVE
            else if (wasMin) wp.showCmd = (wp.flags & 2) != 0 ? 3 : 1;
            if (!activate && wp.showCmd == 1) wp.showCmd = 4;
            wp.flags = 0;
            Native.SetWindowPlacement(Src, ref wp);
            if (activate) U.ForceForeground(Src);
        }

        public void TogglePark()
        {
            if (parked) { Unpark(true); Osd("Source restored"); }
            else Park();
        }

        // ------------------------------------------------------------------ geometry
        // Spots: the 3×3 grid of a display minus its centre — TL T TR / L R / BL B BR (from UniversalPiP)
        static readonly string[,] Tags = { { "TL", "T", "TR" }, { "L", null, "R" }, { "BL", "B", "BR" } };
        static readonly string[] SpotNames = { "Top left", "Top", "Top right", "Left", "Right", "Bottom left", "Bottom", "Bottom right" };
        static readonly string[] SpotKeys = { "TL", "T", "TR", "L", "R", "BL", "B", "BR" };
        public static string CornerTag(Corner c) { return c == Corner.TopLeft ? "TL" : c == Corner.TopRight ? "TR" : c == Corner.BottomLeft ? "BL" : "BR"; }

        int BorderPx()
        {
            int b = App.S.ShowBorder ? Math.Max(1, (int)Math.Round(App.S.BorderWidth * scale)) : 0;
            if (padActive) b = Math.Max(b, (int)Math.Round(3 * scale));
            return b;
        }
        int MarginPx() { return (int)Math.Round(App.S.EdgeMargin * scale); }

        Rectangle ContentRect()
        {
            int b = BorderPx();
            Rectangle r = ClientRectangle;
            r.Inflate(-b, -b);
            return r;
        }

        Rectangle WorkArea() { return Screen.FromRectangle(TargetBounds).WorkingArea; }

        /// <summary>Where new PiPs open: a corner (0-3), or the middle of an edge (4 top, 5 bottom, 6 left, 7 right) when 8 spots are on.</summary>
        public static string StartTag()
        {
            int v = App.S.DefaultCorner;
            if (v >= 4 && v <= 7)
            {
                if (App.S.SnapToEdges) return v == 4 ? "T" : v == 5 ? "B" : v == 6 ? "L" : "R";
                return v == 4 ? "TR" : v == 5 ? "BR" : v == 6 ? "BL" : "BR";   // middle spots off: the nearest corner
            }
            return CornerTag((Corner)U.Clamp(v, 0, 3));
        }

        Point SpotPoint(string spot, Size sz, Rectangle wa)
        {
            int m = MarginPx();
            int x = spot.EndsWith("L") ? wa.Left + m : spot.EndsWith("R") ? wa.Right - m - sz.Width : wa.Left + (wa.Width - sz.Width) / 2;
            int y = spot.StartsWith("T") ? wa.Top + m : spot.StartsWith("B") ? wa.Bottom - m - sz.Height : wa.Top + (wa.Height - sz.Height) / 2;
            return new Point(x, y);
        }

        static void SpotRC(string spot, out int r, out int c)
        {
            r = c = -1;
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) if (Tags[i, j] == spot) { r = i; c = j; }
        }

        public string NearestSpot(bool edges)
        {
            var wa = WorkArea();
            var b = TargetBounds;
            double fx = (b.Left + b.Width / 2.0 - wa.Left) / wa.Width, fy = (b.Top + b.Height / 2.0 - wa.Top) / wa.Height;
            int col = edges ? (fx < 1 / 3.0 ? 0 : fx > 2 / 3.0 ? 2 : 1) : (fx < 0.5 ? 0 : 2);
            int row = edges ? (fy < 1 / 3.0 ? 0 : fy > 2 / 3.0 ? 2 : 1) : (fy < 0.5 ? 0 : 2);
            if (row == 1 && col == 1)
            {
                if (Math.Abs(fx - 0.5) > Math.Abs(fy - 0.5)) col = fx < 0.5 ? 0 : 2;
                else row = fy < 0.5 ? 0 : 2;
            }
            return Tags[row, col];
        }

        Size SizeForWidth(int w)
        {
            int b = 2 * BorderPx();
            return new Size(w, (int)Math.Round((w - b) / ContentAspect) + b);
        }

        Size FitSizeIn(int w, Rectangle wa, double maxHFrac, bool limits = true)
        {
            // advanced size limits (Settings > Behavior > Size limits); theater mode ignores them
            limits = limits && !inTheater;
            if (limits && App.S.LimitWidth) w = Math.Min(w, Math.Max((int)(140 * scale), (int)(wa.Width * App.S.MaxWidthPercent / 100.0)));
            if (limits && App.S.LimitHeight) maxHFrac = Math.Min(maxHFrac, App.S.MaxHeightPercent / 100.0);
            Size sz = SizeForWidth(w);
            int maxH = Math.Max((int)(100 * scale), (int)(wa.Height * maxHFrac));
            if (sz.Height > maxH)
            {
                int b = 2 * BorderPx();
                sz = new Size((int)Math.Round((maxH - b) * ContentAspect) + b, maxH);
            }
            return sz;
        }

        Rectangle AnchoredResize(Size ns)
        {
            var wa = WorkArea();
            var tb = TargetBounds;
            int cx = tb.Left + tb.Width / 2, cy = tb.Top + tb.Height / 2;
            if (inTheater) return new Rectangle(cx - ns.Width / 2, cy - ns.Height / 2, ns.Width, ns.Height);
            double fx = (cx - wa.Left) / (double)wa.Width, fy = (cy - wa.Top) / (double)wa.Height;
            int x = fx > 2 / 3.0 ? tb.Right - ns.Width : fx < 1 / 3.0 ? tb.Left : cx - ns.Width / 2;
            int y = fy > 2 / 3.0 ? tb.Bottom - ns.Height : fy < 1 / 3.0 ? tb.Top : cy - ns.Height / 2;
            return new Rectangle(x, y, ns.Width, ns.Height);
        }

        public void MoveTo(Rectangle r, bool animate)
        {
            if (animate && U.Animations)
            {
                animFrom = Bounds;
                animTo = r;
                animStart = DateTime.Now;
                animating = true;
            }
            else
            {
                animating = false;
                Bounds = r;
            }
        }

        public void PlaceDefault()
        {
            Point cur = Cursor.Position;
            Screen sc = Screen.FromPoint(cur);
            scale = U.ScaleAt(cur);
            var wa = sc.WorkingArea;
            Size sz = FitSizeIn((int)(wa.Width * SizePresets.Frac(App.S.DefaultSize)), wa, 0.8);
            string spot = StartTag();
            Rectangle target = new Rectangle(SpotPoint(spot, sz, wa), sz);
            Dictionary<PipWindow, Rectangle> none;
            target = App.I.Place(this, target, App.SpotGoesDown(spot), true, false, out none);
            if (U.Animations)
            {
                int slide = (int)(48 * scale);
                Rectangle start = target;
                if (spot == "T") start.Y -= slide;                  // middle spots slide in from their own edge
                else if (spot == "B") start.Y += slide;
                else start.X += spot.EndsWith("R") ? slide : -slide;
                Bounds = start;
                MoveTo(target, true);
            }
            else Bounds = target;
        }

        public void PlaceDefaultAnimated()
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            string spot = StartTag();
            Size sz = TargetBounds.Size;
            if (sz.Width > wa.Width || sz.Height > wa.Height) sz = FitSizeIn((int)(wa.Width * 0.25), wa, 0.8);
            inTheater = false;
            PlaceAt(new Rectangle(SpotPoint(spot, sz, wa), sz), App.SpotGoesDown(spot), true, false, true);
        }

        /// <summary>Puts this PiP at want (or next to it: Stacking.Place) and moves the PiPs that make room for it.</summary>
        bool PlaceAt(Rectangle want, bool stackDown, bool stackFirst, bool allowBump, bool animate)
        {
            Dictionary<PipWindow, Rectangle> bump;
            var r = App.I.Place(this, want, stackDown, stackFirst, allowBump, out bump);
            bool moved = r != TargetBounds;
            if (moved) MoveTo(r, animate);
            foreach (var kv in bump) kv.Key.MoveTo(kv.Value, animate);
            return moved || bump.Count > 0;
        }

        public void SnapToSpot(string spot, bool animate)
        {
            inTheater = false;
            var wa = WorkArea();
            Size sz = TargetBounds.Size;
            PlaceAt(new Rectangle(SpotPoint(spot, sz, wa), sz), App.SpotGoesDown(spot), true, false, animate);
        }

        public void SnapTo(Corner c, bool animate) { SnapToSpot(CornerTag(c), animate); }

        void ClampToWorkArea(bool animate)
        {
            var wa = WorkArea();
            var b = TargetBounds;
            int x = U.Clamp(b.X, wa.Left, Math.Max(wa.Left, wa.Right - b.Width));
            int y = U.Clamp(b.Y, wa.Top, Math.Max(wa.Top, wa.Bottom - b.Height));
            if (x != b.X || y != b.Y) MoveTo(new Rectangle(x, y, b.Width, b.Height), animate);
        }

        public void FitToAspect(bool animate)
        {
            if (St.Fit != FitMode.Fit) return;
            var wa = WorkArea();
            Size sz = FitSizeIn(TargetBounds.Width, wa, inTheater ? 0.85 : 0.9);
            if (sz == TargetBounds.Size) return;
            MoveTo(AnchoredResize(sz), animate);
        }

        /// <summary>The next corner clockwise; a corner where it can't really go (taken, and stacking there lands it back
        /// where it is) is skipped.</summary>
        public void CycleCorner()
        {
            string[] order = { "TL", "TR", "BR", "BL" };
            int i = Array.IndexOf(order, NearestSpot(false));
            var wa = WorkArea();
            var b = TargetBounds;
            inTheater = false;
            for (int k = 1; k <= 3; k++)
            {
                string spot = order[(i + k) % 4];
                Dictionary<PipWindow, Rectangle> bump;
                var r = App.I.Place(this, new Rectangle(SpotPoint(spot, b.Size, wa), b.Size), App.SpotGoesDown(spot), true, true, out bump);
                if (Math.Abs(r.X - b.X) + Math.Abs(r.Y - b.Y) < 16) continue;
                MoveTo(r, true);
                foreach (var kv in bump) kv.Key.MoveTo(kv.Value, true);
                App.I.SaveSoon();
                return;
            }
        }

        public void SetSizePreset(int idx)
        {
            var wa = WorkArea();
            idx = U.Clamp(idx, 0, SizePresets.Count - 1);
            Size sz = PresetSize(idx, wa);
            string spot = NearestSpot(App.S.SnapToEdges);
            inTheater = false;
            // it keeps its spot (or where it's anchored) and pushes the PiPs it grows into aside
            if (SnapOn) PlaceAt(new Rectangle(SpotPoint(spot, sz, wa), sz), App.SpotGoesDown(spot), true, true, true);
            else PlaceAt(AnchoredResize(sz), App.SpotGoesDown(spot), false, true, true);
            Osd(SizePresets.Names[idx]);
        }

        /// <summary>A preset's size: its width, with the height from the picture's shape (or the PiP's current shape when it doesn't fit to the picture).</summary>
        Size PresetSize(int idx, Rectangle wa)
        {
            int w = (int)(wa.Width * SizePresets.Frac(idx));
            if (St.Fit == FitMode.Fit) return FitSizeIn(w, wa, 0.85);
            var tb = TargetBounds;
            return new Size(w, (int)Math.Round(tb.Height * (w / (double)Math.Max(1, tb.Width))));
        }

        // ------------------------------------------------------------ per-PiP options (Running PiPs > options)
        /// <summary>Snapping for this PiP: your Settings choice, unless you set it for this PiP.</summary>
        public bool SnapOn { get { return St.Snap == 0 ? App.S.SnapToCorners : St.Snap == 1; } }

        /// <summary>The size preset closest to this PiP's width.</summary>
        public int PresetIndex { get { return SizePresets.Nearest(TargetBounds.Width, WorkArea().Width); } }

        public void SetAspect(int i)
        {
            St.Aspect = Math.Max(0, i);
            ComputeAspect();
            UpdateThumb();
            FitToAspect(true);
            Osd(AspectChoices.Names[U.Clamp(St.Aspect, 0, AspectChoices.Names.Length - 1)]);
            App.I.SaveSoon();
        }

        public void SetSnap(int mode)
        {
            St.Snap = U.Clamp(mode, 0, 2);
            if (SnapOn && !inTheater) SnapToSpot(NearestSpot(App.S.SnapToEdges), true);
            Osd(St.Snap == 0 ? "Snapping follows Settings" : St.Snap == 1 ? "Snaps to spots" : "Doesn't snap");
            App.I.SaveSoon();
        }

        public void SetTrimBars(bool on)
        {
            St.TrimBars = on;
            if (on) TrimBarsNow(false);
            else
            {
                St.Crop = new RectangleF(0, 0, 1, 1);
                ComputeAspect();
                UpdateThumb();
                FitToAspect(true);
                Osd("Showing the whole picture");
            }
            App.I.SaveSoon();
        }

        /// <summary>Crops away the black bars around the video in the source (the same check as Discord pop-outs).</summary>
        void TrimBarsNow(bool quiet)
        {
            Bitmap bmp = null;
            try { bmp = TakeSourceSnapshot(false); } catch { }
            if (bmp == null) { if (!quiet) Osd("Couldn't look at the source right now"); return; }
            int top, bottom, left, right;
            float W = bmp.Width, H = bmp.Height;
            bool found;
            using (bmp) found = Letterbox.Find(bmp, out top, out bottom, out left, out right) && top + bottom + left + right > 0;
            if (!found) { if (!quiet) Osd("No black bars found"); return; }
            St.Crop = new RectangleF(left / W, top / H, (W - left - right) / W, (H - top - bottom) / H);
            ComputeAspect();
            UpdateThumb();
            FitToAspect(true);
            if (!quiet) Osd("Black bars removed");
        }

        /// <summary>Bigger / smaller: steps to the next of the 7 sizes, keeping the shape.</summary>
        public void StepSize(int dir)
        {
            if (!SizePresets.Throttle()) return;
            var wa = WorkArea();
            int cw = TargetBounds.Width;
            int i = SizePresets.Next(cw, wa.Width, dir, k => PresetSize(k, wa).Width);
            if (i < 0) { Osd(dir > 0 ? "Largest size" : "Smallest size"); return; }
            inTheater = false;
            PlaceAt(AnchoredResize(PresetSize(i, wa)), App.SpotGoesDown(NearestSpot(App.S.SnapToEdges)), false, true, true);   // grows into others: they move aside
            Osd(SizePresets.Names[i]);
            App.I.SaveSoon();
        }

        public void OriginalSize()
        {
            Size full = FullSourceSize();
            if (full.Width < 2) return;
            Rectangle cr = CropRect(full);
            int b = 2 * BorderPx();
            var wa = WorkArea();
            Size sz = new Size(Math.Min(cr.Width + b, (int)(wa.Width * 0.95)), Math.Min(cr.Height + b, (int)(wa.Height * 0.95)));
            if (St.Fit == FitMode.Fit) sz = FitSizeIn(sz.Width, wa, 0.95);
            MoveTo(AnchoredResize(sz), true);
            Osd("Original size");
        }

        public void ToggleTheater()
        {
            if (!inTheater)
            {
                theaterRestore = TargetBounds;
                var wa = WorkArea();
                Size sz = FitSizeIn((int)(wa.Width * 0.62), wa, 0.85, false);
                if (St.Fit != FitMode.Fit) sz = new Size((int)(wa.Width * 0.62), (int)(wa.Height * 0.62));
                inTheater = true;
                MoveTo(new Rectangle(wa.Left + (wa.Width - sz.Width) / 2, wa.Top + (wa.Height - sz.Height) / 2, sz.Width, sz.Height), true);
                Osd("Theater mode");
            }
            else
            {
                inTheater = false;
                MoveTo(theaterRestore, true);
            }
        }

        /// <summary>Same spot, but on another display (keeps the size relative to the screen).</summary>
        void MoveToScreen(Screen ns, string spot, bool animate)
        {
            Screen curS = Screen.FromRectangle(TargetBounds);
            double rel = TargetBounds.Width / (double)curS.WorkingArea.Width;
            var wa = ns.WorkingArea;
            Size sz = St.Fit == FitMode.Fit ? FitSizeIn((int)(wa.Width * rel), wa, 0.9)
                                            : new Size((int)(wa.Width * rel), (int)(TargetBounds.Height * (wa.Width / (double)curS.WorkingArea.Width)));
            inTheater = false;
            PlaceAt(new Rectangle(SpotPoint(spot, sz, wa), sz), App.SpotGoesDown(spot), true, false, animate);
        }

        public void NextMonitor()
        {
            var screens = Screen.AllScreens;
            if (screens.Length < 2) { Osd("Only one display"); return; }
            Screen curS = Screen.FromRectangle(TargetBounds);
            int i = 0;
            for (int k = 0; k < screens.Length; k++) if (screens[k].DeviceName == curS.DeviceName) i = k;
            MoveToScreen(screens[(i + 1) % screens.Length], NearestSpot(App.S.SnapToEdges), U.Animations);
        }

        // Nearest display in a direction, preferring ones that line up with the current one (from UniversalPiP)
        static Screen Neighbour(Screen cur, int hx, int hy)
        {
            var c = cur.Bounds;
            double cx = c.Left + c.Width / 2.0, cy = c.Top + c.Height / 2.0, bestS = double.MaxValue;
            Screen best = null;
            foreach (var sc in Screen.AllScreens)
            {
                if (sc.DeviceName == cur.DeviceName) continue;
                var r = sc.Bounds;
                double mx = r.Left + r.Width / 2.0, my = r.Top + r.Height / 2.0;
                double along = hx != 0 ? (mx - cx) * hx : (my - cy) * hy, perp = hx != 0 ? Math.Abs(my - cy) : Math.Abs(mx - cx);
                if (along <= 0) continue;
                double s = along + 3 * perp;
                if (s < bestS) { bestS = s; best = sc; }
            }
            return best;
        }

        /// <summary>One step in a direction through the spots; past the edge it hops to the next display.</summary>
        public void MoveDir(int dx, int dy)
        {
            App.I.Current = this;
            bool edges = App.S.SnapToEdges;
            int step = edges ? 1 : 2;
            string spot = inTheater ? (dx < 0 ? "L" : dx > 0 ? "R" : dy < 0 ? "T" : "B") : NearestSpot(edges);
            if (!edges && inTheater) spot = NearestSpot(false);
            int r, c;
            SpotRC(spot, out r, out c);
            if (r < 0) return;
            int nr = r + dy * step, nc = c + dx * step;
            if (nr == 1 && nc == 1) { nr += dy; nc += dx; }
            if (nr >= 0 && nr <= 2 && nc >= 0 && nc <= 2)
            {
                // the next spot that way where it really moves: a taken spot means stacking next to the PiP there (on the side
                // it comes from), or pushing it aside; a spot where that lands it back where it was is skipped
                var wa = WorkArea();
                var b = TargetBounds;
                for (int tr = nr, tc = nc; tr >= 0 && tr <= 2 && tc >= 0 && tc <= 2; tr += dy * step, tc += dx * step)
                {
                    if ((tr == 1 && tc == 1) || Tags[tr, tc] == null) continue;
                    string to = Tags[tr, tc];
                    bool down = dy > 0 ? false : dy < 0 ? true : App.SpotGoesDown(to);
                    Dictionary<PipWindow, Rectangle> bump;
                    var want = Stacking.Inside(new Rectangle(SpotPoint(to, b.Size, wa), b.Size), wa);
                    var dest = App.I.Place(this, want, down, true, true, out bump);
                    if (!Stacking.MovedToward(b, dest, dx, dy))
                    {
                        // stacking next to the PiPs there gets nowhere (a tall PiP under a small one, moving up): swap places,
                        // pushing them the other way, past this one
                        Dictionary<PipWindow, Rectangle> swap = null;
                        if (Stacking.MovedToward(b, want, dx, dy))
                        {
                            swap = App.I.PlanPush(this, want, -dx, -dy);
                            // no room for both that way (the column is too short): move them aside, toward the middle of the screen
                            if (swap == null && dy != 0) swap = App.I.PlanPush(this, want, want.Left + want.Width / 2 > wa.Left + wa.Width / 2 ? -1 : 1, 0);
                            if (swap == null && dx != 0) swap = App.I.PlanPush(this, want, 0, want.Top + want.Height / 2 > wa.Top + wa.Height / 2 ? -1 : 1);
                        }
                        if (swap == null) continue;
                        dest = want; bump = swap;
                    }
                    inTheater = false;
                    MoveTo(dest, true);
                    foreach (var kv in bump) kv.Key.MoveTo(kv.Value, true);
                    App.I.SaveSoon();
                    return;
                }
                Osd("No room that way");
                return;
            }
            int hx = (nc < 0 || nc > 2) ? dx : 0, hy = (nr < 0 || nr > 2) ? dy : 0;
            if (App.S.LockToMonitor) { Osd("Locked to this display"); return; }
            Screen ns = Neighbour(Screen.FromRectangle(TargetBounds), hx, hy);
            if (ns == null) return;
            int er = hy != 0 ? (hy > 0 ? 0 : 2) : U.Clamp(nr, 0, 2);
            int ec = hx != 0 ? (hx > 0 ? 0 : 2) : U.Clamp(nc, 0, 2);
            if (!edges) { if (er == 1) er = r == 0 ? 0 : 2; if (ec == 1) ec = c == 0 ? 0 : 2; }
            if (er == 1 && ec == 1) ec = hx > 0 ? 0 : 2;
            MoveToScreen(ns, Tags[er, ec], U.Animations);
            App.I.SaveSoon();
        }

        void AfterUserMove()
        {
            HideDropPreview();
            inTheater = false;
            Dictionary<PipWindow, Rectangle> bump;
            var r = PlanDrop(out bump);
            if (r != TargetBounds) MoveTo(r, true);
            foreach (var kv in bump) kv.Key.MoveTo(kv.Value, true);
            App.I.SaveSoon();
        }

        /// <summary>
        /// Where this PiP goes when you let go of it here, and where the PiPs it lands on go: it stays where you put it (snapped
        /// to its spot, or kept on the screen) and PiPs in the way are bumped out of it, into a stack. With snapping, a spot
        /// that's taken means stacking on the side of that PiP you dropped it on. If they can't all fit, this one makes room
        /// for itself instead (stacks next to them). Changes nothing: the drop and the preview while dragging both use it.
        /// </summary>
        Rectangle PlanDrop(out Dictionary<PipWindow, Rectangle> bump)
        {
            var wa = WorkArea();
            var b = TargetBounds;
            Rectangle want = SnapOn ? new Rectangle(SpotPoint(NearestSpot(App.S.SnapToEdges), b.Size, wa), b.Size) : Stacking.Inside(b, wa);
            // stacking goes on the side of the PiP there that you dropped it on
            var occ = App.I.Pips.FirstOrDefault(p => p != this && !p.IsClosing && p.Visible && p.TargetBounds.IntersectsWith(want));
            bool down = occ != null && b.Top + b.Height / 2 > occ.TargetBounds.Top + occ.TargetBounds.Height / 2;
            return App.I.Place(this, want, down, SnapOn, true, out bump);
        }

        // ------------------------------------------------------------ drop preview (while you drag)
        static readonly List<DropGhost> ghosts = new List<DropGhost>();
        DateTime lastPreview = DateTime.MinValue;

        /// <summary>While you drag (or resize): faint outlines where this PiP and the PiPs it would push will land.</summary>
        void UpdateDropPreview()
        {
            if (!moving || (DateTime.Now - lastPreview).TotalMilliseconds < 50) return;
            lastPreview = DateTime.Now;
            var spots = new List<Rectangle>();
            try
            {
                Dictionary<PipWindow, Rectangle> bump;
                var r = PlanDrop(out bump);
                var b = Bounds;
                if (Math.Abs(r.X - b.X) + Math.Abs(r.Y - b.Y) + Math.Abs(r.Width - b.Width) > 6) spots.Add(r);   // it won't stay right here
                spots.AddRange(bump.Values);
            }
            catch { }
            while (ghosts.Count < spots.Count) ghosts.Add(new DropGhost());
            for (int i = 0; i < ghosts.Count; i++)
            {
                if (i < spots.Count) ghosts[i].ShowAt(spots[i], this, App.S.RoundedCorners);
                else if (ghosts[i].Visible) ghosts[i].Hide();
            }
        }

        static void HideDropPreview()
        {
            foreach (var g in ghosts) if (!g.IsDisposed && g.Visible) g.Hide();
        }

        /// <summary>Displays were added, removed or rearranged: keep the PiP on a screen.</summary>
        public void OnDisplayChanged()
        {
            var tb = TargetBounds;
            bool onScreen = Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(tb));
            if (!onScreen) { PlaceDefaultAnimated(); return; }
            scale = IsHandleCreated ? U.ScaleOf(Handle) : scale;
            if (inTheater) return;
            if (SnapOn) SnapToSpot(NearestSpot(App.S.SnapToEdges), false);
            else ClampToWorkArea(false);
        }

        // ------------------------------------------------------------------ crop & zoom
        public void StartCrop()
        {
            if (!HasSource) { Osd("No source to crop"); return; }
            Bitmap snap = TakeSourceSnapshot();
            if (snap == null) { Osd("Couldn't capture the source to crop it"); return; }
            App.I.ModalOpen++;
            try
            {
                Screen sc = Screen.FromRectangle(Bounds);
                using (var sel = new RegionSelector(snap, sc.Bounds, St.Crop, false, "Crop — drag to choose the part of " + U.Trunc(DisplayName, 40) + " to show"))
                {
                    if (sel.ShowDialog() == DialogResult.OK)
                    {
                        St.Crop = sel.ResultFraction;
                        ComputeAspect();
                        UpdateThumb();
                        FitToAspect(true);
                        App.I.SaveSoon();
                    }
                }
            }
            finally
            {
                App.I.ModalOpen--;
                snap.Dispose();
            }
        }

        public void ResetCrop()
        {
            St.Crop = new RectangleF(0, 0, 1, 1);
            ComputeAspect();
            UpdateThumb();
            FitToAspect(true);
            Osd("Full view");
        }

        /// <summary>What this PiP shows right now, cropped like the PiP, for the phone companion's preview. Never steals focus;
        /// null when the source can't be captured (minimized, closed, or an app PrintWindow can't draw).</summary>
        public Bitmap PreviewSnapshot()
        {
            var full = TakeSourceSnapshot(false);
            if (full == null) return null;
            var r = St.Kind == SourceKind.Screen ? new Rectangle(Point.Empty, full.Size) : CropRect(full.Size);
            r.Intersect(new Rectangle(Point.Empty, full.Size));
            if (r.Width < 2 || r.Height < 2 || r.Size == full.Size) return full;
            var part = full.Clone(r, full.PixelFormat);
            full.Dispose();
            return part;
        }

        Bitmap TakeSourceSnapshot(bool allowScreen = true)
        {
            if (St.Kind == SourceKind.Screen)
            {
                Rectangle r = St.ScreenRect;
                var b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                return b;
            }
            if (Src == IntPtr.Zero || !Native.IsWindow(Src) || Native.IsIconic(Src)) return null;
            Size sz = FullSourceSize();
            if (sz.Width < 2 || sz.Height < 2) return null;
            var bmp = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                try { Native.PrintWindow(Src, hdc, St.ClientOnly ? 3u : 2u); }
                finally { g.ReleaseHdc(hdc); }
            }
            if (U.LooksBlank(bmp) && !parked && !allowScreen) { bmp.Dispose(); return null; }   // no focus-stealing fallback
            if (U.LooksBlank(bmp) && !parked)
            {
                // Some apps don't support PrintWindow — grab it from the screen instead.
                Rectangle r = St.ClientOnly ? U.ClientScreenRect(Src) : U.WindowRect(Src);
                IntPtr prev = Native.GetForegroundWindow();
                U.ForceForeground(Src);
                Thread.Sleep(350);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                if (prev != IntPtr.Zero && prev != Src) U.ForceForeground(prev);
            }
            return bmp;
        }

        void ZoomAt(Point screenPt, double f)
        {
            Size full = FullSourceSize();
            if (full.Width < 2) return;
            Rectangle srcR = CropRect(full);
            Rectangle dst = U.ComputeFit(ref srcR, ContentRect(), St.Fit);
            Point cp = PointToClient(screenPt);
            double fx = U.Clamp((cp.X - dst.X) / (double)Math.Max(1, dst.Width), 0, 1);
            double fy = U.Clamp((cp.Y - dst.Y) / (double)Math.Max(1, dst.Height), 0, 1);
            RectangleF c = St.Crop;
            double sx = c.X + fx * c.Width, sy = c.Y + fy * c.Height;
            double nw = c.Width * f, nh = c.Height * f;
            if (nw > 1 || nh > 1) { double k = Math.Min(1 / nw, 1 / nh); nw *= k; nh *= k; }
            if (nw < 0.03 || nh < 0.03) return;
            double nx = U.Clamp(sx - fx * nw, 0, 1 - nw), ny = U.Clamp(sy - fy * nh, 0, 1 - nh);
            St.Crop = new RectangleF((float)nx, (float)ny, (float)nw, (float)nh);
            UpdateThumb();
            if (St.Kind == SourceKind.Screen) CaptureTick();
            Osd("Zoom " + (1 / Math.Max(nw, nh)).ToString("0.0", U.CI) + "×");
            App.I.SaveSoon();
        }

        // ------------------------------------------------------------------ opacity / modes
        public void StepOpacity(double d) { SetOpacity(St.Opacity + d); }

        public void SetOpacity(double v)
        {
            St.Opacity = U.Clamp(v, 0.1, 1.0);
            Osd("Opacity " + (int)Math.Round(St.Opacity * 100) + "%");
            App.I.SaveSoon();
        }

        void ApplyClickThrough() { ApplyAlpha(lastEff); }

        /// <summary>
        /// Same approach as UniversalPiP's native renderer: the PiP is a layered window only while it is see-through
        /// or click-through. At full opacity it is a plain window, so Windows composes the live picture directly
        /// instead of alpha-blending the whole window every frame.
        /// </summary>
        void ApplyAlpha(double eff)
        {
            lastEff = eff;
            if (!IsHandleCreated) return;
            bool need = eff < 0.999 || St.ClickThrough;
            int alpha = U.Clamp((int)Math.Round(eff * 255), 0, 255);
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            int nex = (ex & ~(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT)) | (need ? Native.WS_EX_LAYERED : 0) | (St.ClickThrough ? Native.WS_EX_TRANSPARENT : 0);
            if (nex != ex)
            {
                Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, nex);
                lastAlpha = -1;
                if (!need) Native.RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x80 | 0x400);   // repaint now that it isn't layered
            }
            layered = need;
            if (need && alpha != lastAlpha) { Native.SetLayeredWindowAttributes(Handle, 0, (byte)alpha, 2); lastAlpha = alpha; }
        }

        public void SetClickThrough(bool on)
        {
            St.ClickThrough = on;
            ApplyClickThrough();
            Osd(on ? "Click-through on · " + HK.Display("HK_ClickThrough") + " turns it off" : "Click-through off");
            App.I.SaveSoon();
        }

        public void SetTopMost(bool on)
        {
            St.TopMost = on;
            if (IsHandleCreated)
                Native.SetWindowPos(Handle, on ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (overlay != null) overlay.SetTopMost(on);
            Osd(on ? "Always on top" : "Not on top");
        }

        public void SetFit(FitMode f)
        {
            St.Fit = f;
            UpdateThumb();
            if (f == FitMode.Fit) FitToAspect(true);
            if (St.Kind == SourceKind.Screen) CaptureTick();
            Invalidate();
            Osd(f == FitMode.Fit ? "Fit" : (f == FitMode.Fill ? "Fill" : "Stretch"));
        }

        public void SetClientOnly(bool on)
        {
            St.ClientOnly = on;
            St.Crop = new RectangleF(0, 0, 1, 1);
            ComputeAspect();
            UpdateThumb();
            FitToAspect(true);
        }

        public void SetLocked(bool on)
        {
            St.Locked = on;
            Osd(on ? "Position locked" : "Position unlocked");
        }

        // ------------------------------------------------------------------ media & audio
        public void ToggleMute()
        {
            if (St.Kind != SourceKind.Window || string.IsNullOrEmpty(St.ProcessName)) { Osd("No app audio for this source"); return; }
            bool? m = Audio.GetMute(St.ProcessName);
            if (m == null) { Osd(St.ProcessName + " isn't playing audio"); return; }
            Audio.SetMute(St.ProcessName, !m.Value);
            MuteState = !m.Value;
            Osd(m.Value ? "Unmuted" : "Muted");
            if (overlay != null) overlay.Invalidate();
        }

        public void VolumeStep(int dir)
        {
            if (St.Kind != SourceKind.Window || string.IsNullOrEmpty(St.ProcessName)) { Osd("No app audio for this source"); return; }
            float? v = Audio.GetVolume(St.ProcessName);
            if (v == null) { Osd(St.ProcessName + " isn't playing audio"); return; }
            float nv = Math.Max(0f, Math.Min(1f, v.Value + 0.05f * dir));
            Audio.SetVolume(St.ProcessName, nv);
            if (MuteState == true && dir > 0) { Audio.SetMute(St.ProcessName, false); MuteState = false; }
            Osd("Volume " + (int)Math.Round(nv * 100) + "%", nv);
        }

        public void SetVolume(float v)
        {
            if (string.IsNullOrEmpty(St.ProcessName)) return;
            Audio.SetVolume(St.ProcessName, v);
            Osd("Volume " + (int)Math.Round(v * 100) + "%", v);
        }

        public void BringSourceToFront()
        {
            if (St.Kind != SourceKind.Window || Src == IntPtr.Zero) { Osd("This PiP shows a screen region"); return; }
            if (parked) Unpark(true);
            else U.ForceForeground(Src);
        }

        public void Duplicate()
        {
            var st = St.Clone();
            st.HasBounds = false;
            st.ClickThrough = false;
            App.I.CreatePip(st, Src);
        }

        // ------------------------------------------------------------------ input
        int HitTestAt(Point p)
        {
            if (St.Locked) return Native.HTCLIENT;
            int g = (int)Math.Round(8 * scale);
            bool l = p.X < g, r = p.X >= Width - g, t = p.Y < g, b = p.Y >= Height - g;
            if (t && l) return 13;
            if (t && r) return 14;
            if (b && l) return 16;
            if (b && r) return 17;
            if (l) return 10;
            if (r) return 11;
            if (t) return 12;
            if (b) return 15;
            return Native.HTCAPTION;
        }

        void EnforceAspect(ref Message m)
        {
            var r = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
            int edge = (int)m.WParam.ToInt64();
            int b = 2 * BorderPx();
            double a = ContentAspect;
            if (a <= 0) return;
            int w = r.Right - r.Left - b, h = r.Bottom - r.Top - b;
            if (edge == 1 || edge == 2) { h = (int)Math.Round(w / a); r.Bottom = r.Top + h + b; }
            else if (edge == 3 || edge == 6) { w = (int)Math.Round(h * a); r.Right = r.Left + w + b; }
            else
            {
                h = (int)Math.Round(w / a);
                if (edge == 4 || edge == 5) r.Top = r.Bottom - h - b;
                else r.Bottom = r.Top + h + b;
            }
            Marshal.StructureToPtr(r, m.LParam, false);
        }

        public void HandleWheel(int delta, bool ctrl, bool shift, Point screenPt)
        {
            App.I.Current = this;
            if (ctrl) StepOpacity(delta > 0 ? 0.05 : -0.05);
            else if (shift) ZoomAt(screenPt, delta > 0 ? 0.85 : 1 / 0.85);
            else if (!St.Locked) StepSize(delta > 0 ? 1 : -1);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_NCHITTEST:
                    if (St.ClickThrough) { m.Result = new IntPtr(Native.HTTRANSPARENT); return; }
                    m.Result = new IntPtr(HitTestAt(PointToClient(U.LParamPoint(m.LParam))));
                    return;
                case Native.WM_MOUSEACTIVATE:
                    m.Result = new IntPtr(Native.MA_NOACTIVATE);
                    return;
                case Native.WM_NCLBUTTONDBLCLK:
                    ToggleTheater();
                    return;
                case Native.WM_NCRBUTTONDOWN:
                case Native.WM_NCMBUTTONDOWN:
                    return;
                case Native.WM_NCRBUTTONUP:
                    RightClick();
                    return;
                case Native.WM_NCMBUTTONUP:
                    if (App.S.MiddleClickCloses) CloseAnimated();
                    return;
                case Native.WM_NCLBUTTONDOWN:
                    App.I.Current = this;
                    animating = false;
                    break;
                case Native.WM_ENTERSIZEMOVE:
                    moving = true;
                    animating = false;
                    break;
                case Native.WM_EXITSIZEMOVE:
                    base.WndProc(ref m);
                    moving = false;
                    AfterUserMove();
                    return;
                case Native.WM_SIZING:
                    if (St.Fit == FitMode.Fit) EnforceAspect(ref m);
                    break;
                case Native.WM_MOUSEWHEEL:
                    {
                        long wp = m.WParam.ToInt64();
                        int delta = (short)((wp >> 16) & 0xFFFF);
                        int keys = (int)(wp & 0xFFFF);
                        HandleWheel(delta, (keys & 0x8) != 0, (keys & 0x4) != 0, U.LParamPoint(m.LParam));
                        m.Result = IntPtr.Zero;
                        return;
                    }
                case Native.WM_DPICHANGED:
                    scale = ((int)m.WParam.ToInt64() & 0xFFFF) / 96f;
                    UpdateThumb();
                    Invalidate();
                    if (overlay != null) overlay.Invalidate();
                    m.Result = IntPtr.Zero;
                    return;
            }
            base.WndProc(ref m);
        }

        // Locked mode delivers client-area mouse messages instead of caption ones.
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            ToggleTheater();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) RightClick();
            else if (e.Button == MouseButtons.Middle && App.S.MiddleClickCloses) CloseAnimated();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (moving) UpdateDropPreview();
            UpdateThumb();
            if (overlay != null) overlay.SyncBounds();
            Invalidate();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (overlay != null) overlay.SyncBounds();
            if (moving) UpdateDropPreview();
        }

        // ------------------------------------------------------------------ timers
        void UiTick()
        {
            if (IsDisposed) return;
            DateTime now = DateTime.Now;

            if (animating)
            {
                double t = (now - animStart).TotalMilliseconds / AnimMs;
                if (t >= 1) { t = 1; animating = false; }
                double e = 1 - Math.Pow(1 - t, 3);
                Bounds = U.Lerp(animFrom, animTo, e);
            }

            Point cur = Cursor.Position;
            bool over = Visible && visCur > 0.05 && Bounds.Contains(cur);
            if (over) { lastOver = now; App.I.Current = this; }
            hovering = over || (now - lastOver).TotalMilliseconds < 250 || menuOpen;

            bool srcActive = St.Kind == SourceKind.Window && IsSourceForeground();
            // Auto: the window it shows is now in front, so showing it again would only hide the PiP. Move on to another one.
            if (St.Auto && srcActive && !parked && !moving && !menuOpen && (DateTime.Now - lastAutoSkip).TotalMilliseconds > 400)
            {
                lastAutoSkip = DateTime.Now;
                IntPtr next = App.I.AutoAlternative(Src);
                if (next != IntPtr.Zero) { Attach(next); srcActive = false; }
            }
            if (parked && srcActive) Unpark(false);

            double visTarget = 1;
            if (closingAnim || App.I.HiddenAll) visTarget = 0;
            else if (menuOpen) visTarget = 1;
            else if (App.S.HideWhenSourceActive && srcActive && !parked) visTarget = 0;
            else if (App.S.HideInFullscreen && App.I.FullscreenOn(this)) visTarget = 0;

            double hoverTarget = 1;
            if (hovering && !moving && !menuOpen)
            {
                if (St.ClickThrough) hoverTarget = App.S.ClickThroughHoverPercent / 100.0;
                else if (App.S.HoverAction == 1) hoverTarget = App.S.HoverFadePercent / 100.0;
            }

            bool anim = U.Animations;
            visCur = U.Approach(visCur, visTarget, anim ? 0.14 : 1);
            hoverCur = U.Approach(hoverCur, hoverTarget, anim ? 0.18 : 1);

            if (closingAnim && visCur < 0.03) { Close(); return; }

            double eff = St.Opacity * visCur * hoverCur;
            if (eff < 0.015)
            {
                if (Visible) Hide();
            }
            else
            {
                ApplyAlpha(eff);
                if (!Visible) Show();
            }

            if (hovering != lastHoverPaint) { lastHoverPaint = hovering; Invalidate(); }
            bool pulsing = now < pulseUntil;
            if (pulsing != lastPulsePaint) { lastPulsePaint = pulsing; Invalidate(); }

            // "Move out of the way": slide to the other side when the pointer arrives (hold Ctrl to grab it instead)
            bool ctrl = U.KeyHeld(0x11);
            if (App.S.HoverAction == 3 && over && !ctrl && !moving && !animating && !menuOpen && !St.ClickThrough
                && Control.MouseButtons == MouseButtons.None && now > dodgeAfter && visTarget > 0)
            {
                dodgeAfter = now.AddMilliseconds(800);
                string sp = inTheater ? "C" : NearestSpot(true);
                string opp = sp == "TL" ? "TR" : sp == "TR" ? "TL" : sp == "L" ? "R" : sp == "R" ? "L" : sp == "BL" ? "BR" : sp == "BR" ? "BL" : sp == "T" ? "B" : sp == "B" ? "T" : null;
                if (opp != null) SnapToSpot(opp, true);
            }

            bool controlsMode = App.S.HoverAction == 0 || (App.S.HoverAction == 3 && ctrl);
            bool showControls = Visible && hovering && !St.ClickThrough && controlsMode && !moving && visTarget > 0;
            // controller controls on: the PiP's title, hints and messages stay up even while the picture itself is hidden
            // after a few seconds without input the title and hints fade (any button brings them back), so there's less over the game
            bool padRecent = !App.S.PadHintsAutoHide || (DateTime.Now - lastPadInput).TotalSeconds < 4 || !Visible;
            bool padUi = padActive && App.I.PadActive && !closingAnim && padRecent;
            bool showPad = padUi;
            overlay.Tick(showControls, (Visible || padUi) && OsdActive && !App.S.OsdBelow && OsdLevel < 0, showPad);
            SyncOutline();

            // Run at 60 fps only while something moves; otherwise just watch for the pointer (like UniversalPiP's 60 ms hover check)
            bool busy = animating || visCur != visTarget || hoverCur != hoverTarget || hovering || pulsing || closingAnim || OsdActive || overlay.Busy;
            int want = busy ? 15 : 60;
            if (uiTimer.Interval != want) uiTimer.Interval = want;
        }

        void SlowTick()
        {
            if (IsDisposed) return;
            if (St.Kind == SourceKind.Window)
            {
                if (Src != IntPtr.Zero && !Native.IsWindow(Src)) OnSourceLost();
                if (St.Auto && Src != IntPtr.Zero && (parked || Native.IsIconic(Src)) && (DateTime.Now - lastAutoSkip).TotalMilliseconds > 700)
                {
                    // Auto: a minimized source is skipped for the next available window (Attach un-parks it)
                    lastAutoSkip = DateTime.Now;
                    IntPtr next = App.I.FirstAvailableWindow(Src);
                    if (next != IntPtr.Zero) Attach(next);
                }
                if (Src != IntPtr.Zero)
                {
                    bool ic = Native.IsIconic(Src);
                    if (ic != lastIconic)
                    {
                        lastIconic = ic;
                        if (ic && App.S.KeepAliveMinimized && !parked) Park();
                        UpdateThumb();
                        Invalidate();
                    }
                    if (!ic)
                    {
                        Size sz = FullSourceSize();
                        if (sz != lastSrcSize && sz.Width > 1 && sz.Height > 1)
                        {
                            lastSrcSize = sz;
                            double old = ContentAspect;
                            ComputeAspect();
                            UpdateThumb();
                            if (Math.Abs(old - ContentAspect) > 0.01 && !moving) FitToAspect(true);
                        }
                    }
                    string t = U.WindowTitle(Src);
                    if (t.Length > 0 && t != St.Title) { St.Title = t; overlay.Invalidate(); }
                    if (overlay.Visible) MuteState = Audio.GetMute(St.ProcessName);
                }
                else if (sourceLost && St.Auto && (DateTime.Now - lastReattach).TotalSeconds > 2)
                {
                    lastReattach = DateTime.Now;
                    IntPtr next = App.I.FirstAvailableWindow(IntPtr.Zero);
                    if (next != IntPtr.Zero) Attach(next);
                }
                else if (sourceLost && (App.S.AutoReattach || DateTime.Now < reattachForcedUntil) && (DateTime.Now - lastReattach).TotalSeconds > 2)
                {
                    lastReattach = DateTime.Now;
                    TryReattach();
                }
            }
            // Fullscreen apps and the taskbar sometimes push us down; reassert our place.
            // Only when the always-on-top flag was actually lost: re-asserting it every time makes Windows recompose.
            if (St.TopMost && App.I.ModalOpen == 0 && Visible && !menuOpen && (Native.GetWindowLong(Handle, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0)
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // ------------------------------------------------------------------ screen capture
        void DisposeCapture()
        {
            if (capBmp != null) { capBmp.Dispose(); capBmp = null; }
        }

        void CaptureTick()
        {
            if (St.Kind != SourceKind.Screen || !Visible || IsDisposed) return;
            Rectangle content = ContentRect();
            if (content.Width < 2 || content.Height < 2) return;
            Rectangle srcR = CropRect(St.ScreenRect.Size);
            srcR.Offset(St.ScreenRect.Location);
            Rectangle dst = U.ComputeFit(ref srcR, content, St.Fit);
            if (capBmp == null || capBmp.Width != dst.Width || capBmp.Height != dst.Height)
            {
                DisposeCapture();
                capBmp = new Bitmap(Math.Max(1, dst.Width), Math.Max(1, dst.Height), PixelFormat.Format32bppRgb);
            }
            using (var g = Graphics.FromImage(capBmp))
            {
                IntPtr hdc = g.GetHdc();
                IntPtr sdc = Native.GetDC(IntPtr.Zero);
                try
                {
                    Native.SetStretchBltMode(hdc, 4);
                    Native.SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
                    Native.StretchBlt(hdc, 0, 0, dst.Width, dst.Height, sdc, srcR.X, srcR.Y, srcR.Width, srcR.Height, Native.SRCCOPY);
                    if (App.S.CaptureCursor) DrawCursor(hdc, srcR, dst.Size);
                }
                finally
                {
                    Native.ReleaseDC(IntPtr.Zero, sdc);
                    g.ReleaseHdc(hdc);
                }
            }
            capDst = dst;
            Invalidate(dst);
        }

        // ------------------------------------------------------------ screen readers (Narrator and the like)
        protected override AccessibleObject CreateAccessibilityInstance() { return new PipAccess(this); }

        /// <summary>What the PiP shows, in a sentence.</summary>
        public string AccessibleSummary
        {
            get
            {
                if (St.Kind == SourceKind.Screen) return L.T("A part of the screen");
                return L.T("Picture-in-picture of {0}").Replace("{0}", string.IsNullOrEmpty(St.ProcessName) ? DisplayName : St.ProcessName);
            }
        }

        void DrawCursor(IntPtr hdc, Rectangle srcR, Size dstSz)
        {
            var ci = new Native.CURSORINFO();
            ci.cbSize = Marshal.SizeOf(typeof(Native.CURSORINFO));
            if (!Native.GetCursorInfo(ref ci) || (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero) return;
            if (!srcR.Contains(ci.ptScreenPos.X, ci.ptScreenPos.Y)) return;
            int hx = 0, hy = 0;
            Native.ICONINFO ii;
            if (Native.GetIconInfo(ci.hCursor, out ii))
            {
                hx = ii.xHotspot; hy = ii.yHotspot;
                if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
            }
            double sx = dstSz.Width / (double)srcR.Width, sy = dstSz.Height / (double)srcR.Height;
            int cs = Native.GetSystemMetrics(13);
            int dw = Math.Max(8, (int)Math.Round(cs * sx)), dh = Math.Max(8, (int)Math.Round(cs * sy));
            int x = (int)Math.Round((ci.ptScreenPos.X - srcR.X - hx) * sx);
            int y = (int)Math.Round((ci.ptScreenPos.Y - srcR.Y - hy) * sy);
            Native.DrawIconEx(hdc, x, y, ci.hCursor, dw, dh, 0, IntPtr.Zero, 3);
        }

        // ------------------------------------------------------------------ painting
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color bg = App.S.BackgroundColor;
            int b = BorderPx();
            if (b > 0)
            {
                Color bc;
                Color baseC = App.S.BorderUseAccent ? Theme.Accent : App.S.BorderColor;
                if (padActive) bc = DateTime.Now < pulseUntil ? Color.White : Theme.Accent;   // controller controls: accent outline (stands out from a black border), white flash on input
                else if (App.S.BorderUseAccent) bc = hovering ? baseC : U.Blend(baseC, Color.FromArgb(20, 20, 24), 0.55);
                else bc = hovering ? U.Blend(baseC, Color.White, 0.25) : baseC;      // custom color: shown exactly, a bit brighter on hover
                g.Clear(bc);
                using (var br = new SolidBrush(bg)) g.FillRectangle(br, ContentRect());
            }
            else g.Clear(bg);

            if (St.Kind != SourceKind.Window)
            {
                if (capBmp != null) g.DrawImageUnscaled(capBmp, capDst.Location);
                return;
            }
            if (sourceLost || Src == IntPtr.Zero)
            {
                string who = string.IsNullOrEmpty(St.ProcessName) ? "the source" : St.ProcessName;
                DrawCenterMessage(g, "", App.S.AutoReattach ? "Waiting for " + who + "…" : "Source closed",
                    App.S.AutoReattach ? "It will reconnect automatically when it reopens." : "Right-click › Source › Change source…");
                return;
            }
            if (Native.IsIconic(Src) && !parked)
                DrawCenterMessage(g, "", "Source is minimized",
                    "Restore it, or use right-click › Source › Hide source window to keep it playing out of sight.");
        }

        void DrawCenterMessage(Graphics g, string glyph, string l1, string l2)
        {
            Rectangle c = ContentRect();
            int y = c.Top + c.Height / 2 - (int)(46 * scale);
            Color dim = Color.FromArgb(160, 160, 172);
            LT.DrawText(g, glyph, U.Icons(22 * scale), new Rectangle(c.X, y, c.Width, (int)(30 * scale)), dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            LT.DrawText(g, l1, U.UIBold(13 * scale), new Rectangle(c.X + 8, y + (int)(34 * scale), c.Width - 16, (int)(22 * scale)), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            LT.DrawText(g, l2, U.UI(11.5f * scale), new Rectangle(c.X + 12, y + (int)(58 * scale), c.Width - 24, (int)(48 * scale)), dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        public void Osd(string text) { Osd(text, -1); }

        public void Osd(string text, double level)
        {
            osdLevel = level;
            osdText = text ?? "";
            osdUntil = DateTime.Now.AddMilliseconds(1300);
            if (overlay != null) overlay.Invalidate();
        }

        // ------------------------------------------------------------------ context menu
        public void ShowMenu()
        {
            if (IsClosing) return;
            App.I.Current = this;
            if (menu == null)
            {
                menu = new ContextMenuStrip();
                menu.Opened += (o, e) => { menuOpen = true; };
                menu.Closed += (o, e) => { menuOpen = false; };
            }
            var items = menu.Items;
            Menus.Clear(items);
            bool win = St.Kind == SourceKind.Window;

            items.Add(Menus.Header(U.Trunc(DisplayName, 48)));
            items.Add(new ToolStripSeparator());

            var src = Menus.Item("Source", null);
            src.DropDownItems.Add(Menus.Item("Change source…", () => App.I.ShowPicker(this)));
            src.DropDownItems.Add(Menus.Item("Mirror a screen region instead…", () => App.I.PipScreenRegion(this)));
            src.DropDownItems.Add(new ToolStripSeparator());
            src.DropDownItems.Add(Menus.Item("Crop…", StartCrop, "HK_Crop"));
            var reset = Menus.Item("Show everything (reset crop & zoom)", ResetCrop);
            reset.Enabled = St.IsCropped;
            src.DropDownItems.Add(reset);
            if (win)
            {
                src.DropDownItems.Add(Menus.Check("Hide the source's title bar", St.ClientOnly, () => SetClientOnly(!St.ClientOnly)));
                src.DropDownItems.Add(new ToolStripSeparator());
                src.DropDownItems.Add(Menus.Item("Bring source to front", BringSourceToFront, "HK_Swap"));
                var park = Menus.Check("Hide source window (keeps playing)", parked, TogglePark);
                park.Enabled = Src != IntPtr.Zero;
                src.DropDownItems.Add(park);
                src.DropDownItems.Add(new ToolStripSeparator());
                src.DropDownItems.Add(Menus.Item("Previous window", () => CycleSource(-1), "HK_PrevSource"));
                src.DropDownItems.Add(Menus.Item("Next window", () => CycleSource(1), "HK_NextSource"));
                src.DropDownItems.Add(Menus.Check("Auto (first window, then the one you just left)", St.Auto, () => SetAuto(!St.Auto)));
                if (Src != IntPtr.Zero && SourceLooksChromium)
                {
                    src.DropDownItems.Add(new ToolStripSeparator());
                    src.DropDownItems.Add(Menus.Item("Fix frozen picture (restart " + U.ProcessName(Src) + ")…", FixFrozen));
                }
            }
            else if (St.Kind == SourceKind.Screen)
            {
                var screens = Screen.AllScreens;
                if (screens.Length > 1 && screens.Any(sc => sc.Bounds == St.ScreenRect))
                {
                    src.DropDownItems.Add(new ToolStripSeparator());
                    src.DropDownItems.Add(Menus.Item("Previous display", () => CycleSource(-1), "HK_PrevSource"));
                    src.DropDownItems.Add(Menus.Item("Next display", () => CycleSource(1), "HK_NextSource"));
                }
            }
            items.Add(src);

            var size = Menus.Item("Size", null);
            for (int i = 0; i < SizePresets.Count; i++) { int idx = i; size.DropDownItems.Add(Menus.Item(SizePresets.Names[i], () => SetSizePreset(idx))); }
            size.DropDownItems.Add(new ToolStripSeparator());
            size.DropDownItems.Add(Menus.Check("Theater mode", inTheater, ToggleTheater, "HK_Theater"));
            size.DropDownItems.Add(Menus.Item("Original size (1:1 pixels)", OriginalSize));
            size.DropDownItems.Add(Menus.Item("Bigger", () => StepSize(1), "HK_Grow"));
            size.DropDownItems.Add(Menus.Item("Smaller", () => StepSize(-1), "HK_Shrink"));
            items.Add(size);

            var pos = Menus.Item("Position", null);
            string ns = NearestSpot(true);
            for (int i = 0; i < SpotKeys.Length; i++)
            {
                string key = SpotKeys[i];
                if (!App.S.SnapToEdges && key.Length == 1 && key != ns) continue;   // edge spots only when enabled (or when it's sitting there)
                pos.DropDownItems.Add(Menus.Check(SpotNames[i], ns == key && !inTheater, () => SnapToSpot(key, true)));
            }
            pos.DropDownItems.Add(new ToolStripSeparator());
            var nm = Menus.Item("Move to next display", NextMonitor, "HK_NextMonitor");
            nm.Enabled = Screen.AllScreens.Length > 1;
            pos.DropDownItems.Add(nm);
            pos.DropDownItems.Add(Menus.Check("Snap after moving", App.S.SnapToCorners, () => { App.S.SnapToCorners = !App.S.SnapToCorners; App.I.SaveSettingsSoon(); }));
            pos.DropDownItems.Add(Menus.Check("Snap to the middle of edges too", App.S.SnapToEdges, () => { App.S.SnapToEdges = !App.S.SnapToEdges; App.I.SaveSettingsSoon(); }));
            pos.DropDownItems.Add(Menus.Check("Move out of the mouse's way", App.S.HoverAction == 3, () => App.I.RunAction("dodge"), "HK_Dodge"));
            pos.DropDownItems.Add(Menus.Check("Lock position && size", St.Locked, () => SetLocked(!St.Locked)));
            items.Add(pos);

            var op = Menus.Item("Opacity", null);
            foreach (int pct in new[] { 100, 90, 80, 70, 60, 50, 40, 30, 20 })
            {
                int v = pct;
                op.DropDownItems.Add(Menus.Check(v + "%", Math.Abs(St.Opacity * 100 - v) < 2.5, () => SetOpacity(v / 100.0)));
            }
            items.Add(op);

            var fit = Menus.Item("Scaling", null);
            fit.DropDownItems.Add(Menus.Check("Fit (keep shape)", St.Fit == FitMode.Fit, () => SetFit(FitMode.Fit)));
            fit.DropDownItems.Add(Menus.Check("Fill (crop edges to fill)", St.Fit == FitMode.Fill, () => SetFit(FitMode.Fill)));
            fit.DropDownItems.Add(Menus.Check("Stretch", St.Fit == FitMode.Stretch, () => SetFit(FitMode.Stretch)));
            items.Add(fit);

            var media = Menus.Item("Media && audio", null);
            media.DropDownItems.Add(Menus.Item("Play / pause", () => U.MediaKey(0xB3), "HK_PlayPause"));
            media.DropDownItems.Add(Menus.Item("Next track", () => U.MediaKey(0xB0)));
            media.DropDownItems.Add(Menus.Item("Previous track", () => U.MediaKey(0xB1)));
            if (win && !string.IsNullOrEmpty(St.ProcessName))
            {
                media.DropDownItems.Add(new ToolStripSeparator());
                bool? muted = Audio.GetMute(St.ProcessName);
                var mute = Menus.Check("Mute " + St.ProcessName, muted == true, ToggleMute, "HK_Mute");
                mute.Enabled = muted != null;
                media.DropDownItems.Add(mute);
                var vol = Menus.Item("Volume", null);
                vol.Enabled = muted != null;
                float? curVol = Audio.GetVolume(St.ProcessName);
                foreach (int pct in new[] { 100, 75, 50, 25, 10 })
                {
                    int v = pct;
                    vol.DropDownItems.Add(Menus.Check(v + "%", curVol != null && Math.Abs(curVol.Value * 100 - v) < 3, () => SetVolume(v / 100f)));
                }
                media.DropDownItems.Add(vol);
                if (muted == null) media.DropDownItems.Add(Menus.Header("(" + St.ProcessName + " isn't playing audio)"));
            }
            items.Add(media);

            items.Add(new ToolStripSeparator());
            if (win) items.Add(Menus.Check("Follow the window I just left", St.Follow, () => SetFollow(!St.Follow), "HK_Follow"));   // used often enough to sit at the top level
            items.Add(Menus.Check("Click-through (ghost mode)", St.ClickThrough, () => SetClickThrough(!St.ClickThrough), "HK_ClickThrough"));
            items.Add(Menus.Check("Always on top", St.TopMost, () => SetTopMost(!St.TopMost)));
            if (App.S.AllowMultiple) items.Add(Menus.Item("Duplicate this PiP", Duplicate));
            items.Add(new ToolStripSeparator());
            if (App.I.PadConnected)
                items.Add(Menus.Check("Controller controls", App.I.PadOn, () => App.I.SetPadOn(!App.I.PadOn), "HK_Pad"));
            items.Add(Menus.Item("New PiP…", () => App.I.ShowPicker(null), "HK_NewPip"));
            items.Add(Menus.Item("Open PiP Studio", () => App.I.ShowMain("running"), "HK_ShowApp"));
            items.Add(Menus.Item("Close", CloseAnimated, "HK_Close"));

            Menus.Style(menu);
            App.I.PrepareMenuFocus();
            menu.Show(Cursor.Position);
        }

        // ------------------------------------------------------------------ closing
        public void SnapshotState()
        {
            St.Bounds = inTheater ? theaterRestore : TargetBounds;
            St.HasBounds = St.Bounds.Width > 0;
        }

        public void CloseAnimated()
        {
            if (closingAnim) return;
            closingAnim = true;
            if (!U.Animations || !Visible) Close();
        }

        public void ShutdownClose()
        {
            shuttingDown = true;
            closingAnim = true;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            uiTimer.Stop(); slowTimer.Stop(); capTimer.Stop();
            uiTimer.Dispose(); slowTimer.Dispose(); capTimer.Dispose();
            UnregisterThumb();
            if (parked) Unpark(false, true);
            if (overlay != null)
            {
                try { if (!overlay.IsDisposed) { overlay.Close(); overlay.Dispose(); } } catch { }
                overlay = null;
            }
            if (menu != null) { menu.Dispose(); menu = null; }
            DisposeOutline();
            DisposeCapture();
            base.OnFormClosed(e);
            if (!shuttingDown || App.I.Pips.Contains(this)) App.I.OnPipClosed(this);
        }
    }

    // =================================================================================
    //  Hover controls (separate layered window above the PiP — DWM thumbnails cover GDI drawing)
    // =================================================================================
    /// <summary>What screen readers see of a PiP: its name and what it shows.</summary>
    class PipAccess : Control.ControlAccessibleObject
    {
        readonly PipWindow p;
        public PipAccess(PipWindow p) : base(p) { this.p = p; }
        public override string Name { get { return p.DisplayName; } set { } }
        public override string Description { get { return p.AccessibleSummary; } }
        public override AccessibleRole Role { get { return AccessibleRole.Graphic; } }
        public override string Value { get { return p.AccessibleSummary; } set { } }
    }

    /// <summary>A faint, click-through outline of where a PiP will land when you let go (shown while you drag one).</summary>
    class DropGhost : Form
    {
        bool round;

        public DropGhost()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Opacity = 0.32;
            Text = "PiP drop preview";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            U.ExcludeFromCapture(Handle, App.S.HideFromCapture);
        }

        /// <summary>Shows it over r, just under the PiP you're dragging.</summary>
        public void ShowAt(Rectangle r, Form under, bool rounded)
        {
            if (round != rounded) { round = rounded; if (IsHandleCreated) U.RoundCorners(Handle, round); }
            if (!Visible) { Bounds = r; Show(); if (IsHandleCreated) U.RoundCorners(Handle, round); }
            else if (Bounds != r) Bounds = r;
            try { Native.SetWindowPos(Handle, under.Handle, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); } catch { }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(U.Blend(Theme.Accent, Color.Black, 0.35));
            int t = Math.Max(2, (int)Math.Round(3 * U.ScaleOf(Handle)));
            using (var pen = new Pen(Color.White, t) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    /// <summary>A click-through outline drawn over a PiP while controller controls are on, so you can see it even when the PiP is faded out.</summary>
    class PipOutline : Form
    {
        static readonly Color Key = Color.FromArgb(255, 0, 255);
        readonly bool topMost;
        Color color = Color.White;
        int thickness = 3;
        bool round;

        public PipOutline(bool topMost)
        {
            this.topMost = topMost;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Key;
            TransparencyKey = Key;
            Text = "PiP outline";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && IsHandleCreated) U.ExcludeFromCapture(Handle, App.S.HideFromCapture);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
                if (topMost) cp.ExStyle |= Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            U.ExcludeFromCapture(Handle, App.S.HideFromCapture);
        }

        public void Sync(Rectangle bounds, Color c, int t, bool rounded)
        {
            if (Bounds != bounds) Bounds = bounds;
            if (c != color || t != thickness || rounded != round) { color = c; thickness = t; round = rounded; Invalidate(); }
            if (!Visible) Show();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Key);
            using (var pen = new Pen(color, thickness) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
            {
                var r = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                if (!round) { g.DrawRectangle(pen, r); return; }
                int d = Math.Min(16, Math.Min(r.Width, r.Height) / 2);
                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    g.DrawPath(pen, path);       // no anti-aliasing: the transparency key needs hard edges
                }
            }
        }
    }

    /// <summary>Small click-through pill under a PiP that shows its messages (new source, mode changes).</summary>
    class PipCaption : Form
    {
        static readonly Color Fill = Color.FromArgb(20, 20, 24);
        readonly bool topMost;
        string text = "";
        float scale = 1;
        double level = -1;          // 0..1: draw a level bar (volume) under the text

        public PipCaption(bool topMost)
        {
            this.topMost = topMost;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Fill;
            Text = "PiP title";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Opacity = 0;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
                if (topMost) cp.ExStyle |= Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            U.RoundCorners(Handle, true);
            U.ExcludeFromCapture(Handle, App.S.HideFromCapture);
        }

        public void SetText(string t, float s, double lvl = -1)
        {
            t = t ?? "";
            if (t == text && Math.Abs(s - scale) < 0.001f && Math.Abs(lvl - level) < 0.001) return;
            text = t; scale = s; level = lvl;
            Invalidate();
        }

        public void Place(PipWindow pip)
        {
            float s = scale;
            Rectangle pb = pip.Bounds;
            bool bar = level >= 0;
            int h = (int)Math.Round((bar ? 46 : 30) * s), pad = (int)Math.Round(14 * s), gap = (int)Math.Round(6 * s);
            int tw;
            tw = LT.MeasureText(text, U.UIBold(13 * s), Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;   // U.UI fonts are cached: don't dispose
            int w = Math.Max(bar ? (int)Math.Round(200 * s) : h * 2, Math.Min(Math.Max(pb.Width, bar ? (int)Math.Round(200 * s) : 0), tw + pad * 2));
            Rectangle wa = Screen.FromRectangle(pb).WorkingArea;
            int y = pb.Bottom + gap;
            if (y + h > wa.Bottom) y = pb.Top - gap - h;          // no room below: put it above instead
            if (y < wa.Top) y = pb.Bottom - gap - h;              // no room either way: tuck it inside the bottom edge
            int x = pb.Left + (pb.Width - w) / 2;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - w));
            var r = new Rectangle(x, y, w, h);
            if (Bounds != r) Bounds = r;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Fill);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (level < 0) { LT.DrawText(g, text, U.UIBold(13 * scale), ClientRectangle, Color.White, flags); return; }
            // text on top, a rounded level bar under it
            int pad = (int)Math.Round(14 * scale), bh = Math.Max(4, (int)Math.Round(4 * scale));
            var textRect = new Rectangle(0, (int)Math.Round(4 * scale), ClientSize.Width, (int)Math.Round(24 * scale));
            LT.DrawText(g, text, U.UIBold(13 * scale), textRect, Color.White, flags);
            var track = new Rectangle(pad, ClientSize.Height - pad + 2 - bh, ClientSize.Width - 2 * pad, bh);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(70, 70, 78))) FillPill(g, b, track);
            int fw = (int)Math.Round(track.Width * Math.Max(0, Math.Min(1, level)));
            if (fw > 0) using (var b = new SolidBrush(Theme.Accent)) FillPill(g, b, new Rectangle(track.X, track.Y, Math.Max(bh, fw), bh));
        }

        static void FillPill(Graphics g, Brush b, Rectangle r)
        {
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int d = r.Height;
                path.AddArc(r.X, r.Y, d, d, 90, 180);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
                path.CloseFigure();
                g.FillPath(b, path);
            }
        }
    }

    class PipOverlay : Form
    {
        class OBtn
        {
            public Rectangle R;
            public string Glyph, Tip;
            public Action Click;
            public bool On, Volume, Round;
            public OBtn(string glyph, string tip, Action click) { Glyph = glyph; Tip = tip; Click = click; }
            public int Order;      // position from the right in the title bar
            public string Key = "";
        }

        static readonly Color Key = Color.FromArgb(255, 1, 0, 1);
        static readonly Color BarColor = Color.FromArgb(20, 20, 24);
        readonly PipWindow pip;
        readonly List<OBtn> btns = new List<OBtn>();
        Rectangle barRect, pillRect, titleRect;
        int hot = -1, pressed = -1;
        double alpha = 0;
        bool showCtl, showOsd, showPad;
        double target;
        public bool Busy { get { return Math.Abs(alpha - target) > 0.001 || Math.Abs(capAlpha - capTarget) > 0.001; } }

        public PipOverlay(PipWindow p)
        {
            pip = p;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Key;
            TransparencyKey = Key;
            Text = "PiP controls";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Opacity = 0;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                if (pip == null || pip.St.TopMost) cp.ExStyle |= Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplySettings();
        }

        public void ApplySettings()
        {
            if (!IsHandleCreated) return;
            U.RoundCorners(Handle, App.S.RoundedCorners);
            U.ExcludeFromCapture(Handle, App.S.HideFromCapture || pip.St.Kind == SourceKind.Screen);
            Invalidate();
        }

        public void SetTopMost(bool on)
        {
            if (caption != null && !caption.IsDisposed && caption.IsHandleCreated)
                Native.SetWindowPos(caption.Handle, on ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (IsHandleCreated)
                Native.SetWindowPos(Handle, on ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void SyncBounds()
        {
            if (IsDisposed || pip.IsDisposed) return;
            if (Bounds != pip.Bounds) Bounds = pip.Bounds;
            if (caption != null && caption.Visible) caption.Place(pip);
        }

        // messages (the new source's name, mode changes...) shown in a caption under the PiP instead of over the picture
        PipCaption caption;
        double capAlpha = 0, capTarget = 0;

        void UpdateCaption()
        {
            bool want = (App.S.OsdBelow || pip.OsdLevel >= 0) && pip.OsdActive && (pip.Visible || pip.PadActiveShown) && !pip.IsDisposed;   // volume always gets its bar under the PiP
            capTarget = want ? 0.96 : 0;
            if (want)
            {
                if (caption == null || caption.IsDisposed) caption = new PipCaption(pip.St.TopMost);
                caption.SetText(pip.OsdText, pip.UiScale, pip.OsdLevel);
            }
            if (caption == null || caption.IsDisposed) return;
            capAlpha = U.Approach(capAlpha, capTarget, U.Animations ? 0.22 : 1);
            if (capAlpha < 0.02)
            {
                if (caption.Visible) caption.Hide();
                return;
            }
            caption.Place(pip);
            if (!caption.Visible) caption.Show();
            if (Math.Abs(caption.Opacity - capAlpha) > 0.01) caption.Opacity = capAlpha;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && caption != null) { try { if (!caption.IsDisposed) { caption.Close(); caption.Dispose(); } } catch { } caption = null; }
            base.Dispose(disposing);
        }

        public void Tick(bool ctl, bool osd, bool pad)
        {
            if (IsDisposed) return;
            if (ctl != showCtl || osd != showOsd || pad != showPad)
            {
                showCtl = ctl;
                showOsd = osd;
                showPad = pad;
                if (!ctl) hot = -1;
                Invalidate();
            }
            target = (ctl || osd || pad) ? 0.96 : 0;
            alpha = U.Approach(alpha, target, U.Animations ? 0.22 : 1);
            if (alpha < 0.02)
            {
                if (Visible) Hide();
                UpdateCaption();
                return;
            }
            if (!Visible)
            {
                SyncBounds();
                Show();
            }
            if (Math.Abs(Opacity - alpha) > 0.01) Opacity = alpha;
            UpdateCaption();
        }

        /// <summary>One title bar button by its Settings name, or null when it doesn't apply to this PiP.</summary>
        OBtn TitleButton(string key, bool win)
        {
            OBtn b;
            switch (key)
            {
                case "prev": b = new OBtn(TitleBarItems.Glyph(key), "Previous window", () => pip.CycleSource(-1)); break;
                case "next": b = new OBtn(TitleBarItems.Glyph(key), "Next window", () => pip.CycleSource(1)); break;
                case "change": b = new OBtn(TitleBarItems.Glyph(key), "Change source…", () => App.I.ShowPicker(pip)); break;
                case "theater": b = new OBtn(pip.InTheater ? "\uE73F" : "\uE740", pip.InTheater ? "Exit theater mode" : "Theater mode", pip.ToggleTheater); break;
                case "shrink": b = new OBtn(TitleBarItems.Glyph(key), "Make PiP smaller", () => pip.StepSize(-1)); break;
                case "grow": b = new OBtn(TitleBarItems.Glyph(key), "Make PiP bigger", () => pip.StepSize(1)); break;
                case "crop": b = new OBtn(TitleBarItems.Glyph(key), "Crop", pip.StartCrop); break;
                case "corner": b = new OBtn(TitleBarItems.Glyph(key), "Next corner", pip.CycleCorner); break;
                case "display": if (Screen.AllScreens.Length < 2) return null; b = new OBtn(TitleBarItems.Glyph(key), "Move to next display", pip.NextMonitor); break;
                case "source": if (!win) return null; b = new OBtn(TitleBarItems.Glyph(key), "Bring source to front", pip.BringSourceToFront); break;
                case "ghost": b = new OBtn(TitleBarItems.Glyph(key), "Click-through (ghost mode)", () => pip.SetClickThrough(true)); break;
                case "more": b = new OBtn(TitleBarItems.Glyph(key), "More options", pip.ShowMenu); break;
                case "close": b = new OBtn(TitleBarItems.Glyph(key), "Close", pip.CloseAnimated); break;
                default: return null;
            }
            b.Key = key;
            return b;
        }

        static int Rank(OBtn b) { return b.Key == "close" ? -2 : b.Key == "more" ? -1 : b.Order; }

        void BuildLayout()
        {
            btns.Clear();
            barRect = pillRect = titleRect = Rectangle.Empty;
            if (!showCtl) return;
            float s = pip.UiScale;
            int W = ClientSize.Width, H = ClientSize.Height;
            int bh = (int)(32 * s), bw = (int)(30 * s);
            barRect = new Rectangle(0, 0, W, bh);
            bool win = pip.St.Kind == SourceKind.Window;

            // the buttons you picked in Settings (Appearance > PiP window > Title bar buttons), laid out from the right
            var defs = new List<OBtn>();
            var on = TitleBarItems.On(App.S.TitleBarButtons);
            for (int k = on.Count - 1; k >= 0; k--)
            {
                var b = TitleButton(on[k], win);
                if (b != null) { b.Order = defs.Count; defs.Add(b); }
            }
            // a narrow PiP drops buttons from the left, but Close and More options are the last to go
            defs.Sort((t1, t2) => Rank(t1).CompareTo(Rank(t2)));

            int maxBtns = Math.Max(1, (W - (int)(60 * s)) / bw);
            int x = W - (int)(3 * s);
            var kept = defs.Take(maxBtns).ToList();
            kept.Sort((t1, t2) => t1.Order.CompareTo(t2.Order));                    // back to your order (right to left)
            foreach (var b in kept)
            {
                x -= bw;
                b.R = new Rectangle(x, (bh - bw) / 2 + 1, bw, bw - 2);
                btns.Add(b);
            }
            titleRect = new Rectangle((int)(10 * s), 0, Math.Max(0, x - (int)(14 * s)), bh);

            if (win && H > (int)(118 * s) && W > (int)(170 * s))
            {
                var mute = new OBtn(pip.MuteState == true ? "" : "", "Mute — scroll to change volume", pip.ToggleMute);
                mute.On = pip.MuteState == true;
                mute.Volume = true;
                var pd = new List<OBtn> {
                    new OBtn("", "Previous track", () => U.MediaKey(0xB1)),
                    new OBtn("", "Play / pause", () => U.MediaKey(0xB3)),
                    new OBtn("", "Next track", () => U.MediaKey(0xB0)),
                    mute
                };
                int pw = pd.Count * bw + (int)(14 * s), ph = (int)(38 * s);
                pillRect = new Rectangle((W - pw) / 2, H - ph - (int)(10 * s), pw, ph);
                int px = pillRect.X + (int)(7 * s);
                foreach (var b in pd)
                {
                    b.R = new Rectangle(px, pillRect.Y + (ph - bw) / 2, bw, bw);
                    px += bw;
                    btns.Add(b);
                }
            }

            // Move arrows (from UniversalPiP): click one to send the PiP that way, across displays too
            if (App.S.ShowMoveArrows && H > (int)(150 * s) && W > (int)(200 * s))
            {
                int ab = (int)(28 * s), m = (int)(8 * s);
                int midY = barRect.Bottom + (((pillRect.IsEmpty ? H : pillRect.Top) - barRect.Bottom) - ab) / 2;
                var arrows = new[] {
                    new OBtn("\uE76B", "Move left", () => pip.MoveDir(-1, 0)) { R = new Rectangle(m, midY, ab, ab) },
                    new OBtn("\uE76C", "Move right", () => pip.MoveDir(1, 0)) { R = new Rectangle(W - m - ab, midY, ab, ab) },
                    new OBtn("\uE70E", "Move up", () => pip.MoveDir(0, -1)) { R = new Rectangle((W - ab) / 2, barRect.Bottom + m, ab, ab) },
                    new OBtn("\uE70D", "Move down", () => pip.MoveDir(0, 1)) { R = new Rectangle((W - ab) / 2, (pillRect.IsEmpty ? H : pillRect.Top) - m - ab, ab, ab) },
                };
                foreach (var a in arrows) { a.Round = true; btns.Add(a); }
            }
        }

        int IndexAt(Point p)
        {
            for (int i = 0; i < btns.Count; i++) if (btns[i].R.Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Key);
            BuildLayout();
            float s = pip.UiScale;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (showCtl)
            {
                using (var b = new SolidBrush(BarColor)) g.FillRectangle(b, barRect);
                if (titleRect.Width > 20)
                {
                    string title = pip.DisplayName;
                    if (pip.IsParked) title = "◌ " + title;
                    LT.DrawText(g, title, U.UI(12 * s), titleRect, Color.FromArgb(235, 235, 242),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
                if (!pillRect.IsEmpty)
                    using (var path = U.RoundRect(pillRect, pillRect.Height / 2f))
                    using (var b = new SolidBrush(BarColor))
                        g.FillPath(b, path);

                for (int i = 0; i < btns.Count; i++)
                {
                    var bt = btns[i];
                    if (bt.Round)
                    {
                        using (var b = new SolidBrush(i == hot ? Theme.Accent : Color.FromArgb(28, 28, 32)))
                            g.FillEllipse(b, bt.R);
                        using (var p = new Pen(Color.FromArgb(90, 90, 98), 1f))
                            g.DrawEllipse(p, bt.R);
                        LT.DrawText(g, bt.Glyph, U.Icons(11 * s), bt.R, i == hot ? Theme.OnAccent : Color.White,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                        continue;
                    }
                    if (i == hot)
                    {
                        Color hc = bt.Glyph == "" ? Color.FromArgb(196, 43, 28) : Color.FromArgb(58, 60, 70);
                        using (var path = U.RoundRect(Rectangle.Inflate(bt.R, -2, -2), 5 * s))
                        using (var b = new SolidBrush(hc))
                            g.FillPath(b, path);
                    }
                    Color gc = bt.On ? Theme.Accent : Color.FromArgb(238, 238, 244);
                    LT.DrawText(g, bt.Glyph, U.Icons(13 * s), bt.R, gc,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }

                if (hot >= 0 && hot < btns.Count)
                {
                    var bt = btns[hot];
                    Font tf = U.UI(11 * s);
                    Size ts = LT.MeasureText(bt.Tip, tf);
                    int tw = ts.Width + (int)(14 * s), th = ts.Height + (int)(6 * s);
                    bool inBar = bt.R.Bottom <= barRect.Bottom;
                    int tx = U.Clamp(bt.R.X + bt.R.Width / 2 - tw / 2, 4, Math.Max(4, ClientSize.Width - tw - 4));
                    int ty = inBar ? barRect.Bottom + (int)(6 * s) : pillRect.Y - th - (int)(6 * s);
                    if (bt.Round)
                    {
                        ty = bt.R.Bottom + (int)(6 * s);
                        if (ty + th > ClientSize.Height - 4) ty = bt.R.Top - th - (int)(6 * s);
                        if (bt.R.X < ClientSize.Width / 4) tx = bt.R.Right + (int)(6 * s);
                        else if (bt.R.Right > ClientSize.Width * 3 / 4) tx = bt.R.Left - tw - (int)(6 * s);
                        if (bt.R.X < ClientSize.Width / 4 || bt.R.Right > ClientSize.Width * 3 / 4) ty = bt.R.Y + (bt.R.Height - th) / 2;
                    }
                    var tr = new Rectangle(tx, ty, tw, th);
                    using (var path = U.RoundRect(tr, 4 * s))
                    using (var b = new SolidBrush(BarColor))
                        g.FillPath(b, path);
                    LT.DrawText(g, bt.Tip, tf, tr, Color.FromArgb(225, 225, 232),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            }

            if (showPad && !showCtl)
            {
                DrawPadTitle(g, s);
                if (App.S.PadHints) DrawPadHints(g, s);
            }

            if (pip.OsdActive && !App.S.OsdBelow && pip.OsdLevel < 0)
            {
                Font f = U.UIBold(14 * s);
                string text = pip.OsdText;
                Size ts = LT.MeasureText(text, f);
                int w = Math.Min(ClientSize.Width - 12, ts.Width + (int)(28 * s)), h = ts.Height + (int)(14 * s);
                var r = new Rectangle((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h);
                using (var path = U.RoundRect(r, h / 2f))
                using (var b = new SolidBrush(BarColor))
                    g.FillPath(b, path);
                LT.DrawText(g, text, f, r, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        /// <summary>Controller button hints along the bottom of the PiP (from UniversalPiP's HUD).</summary>
        /// <summary>Controller controls: the PiP's name in a pill at the top, so you know which PiP you're steering (even when it's hidden).</summary>
        void DrawPadTitle(Graphics g, float s)
        {
            string title = pip.DisplayName;
            if (string.IsNullOrEmpty(title)) return;
            if (pip.IsParked) title = "◌ " + title;
            Font f = U.UIBold(12 * s);
            int pad = (int)(12 * s), h = (int)(26 * s), top = (int)(8 * s);
            int tw = LT.MeasureText(title, f, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            int w = Math.Min(ClientSize.Width - (int)(16 * s), tw + pad * 2);
            if (w < h) return;
            var r = new Rectangle((ClientSize.Width - w) / 2, top, w, h);
            using (var path = U.RoundRect(r, h / 2f))
            using (var b = new SolidBrush(BarColor))
                g.FillPath(b, path);
            LT.DrawText(g, title, f, new Rectangle(r.X + pad / 2, r.Y, r.Width - pad, r.Height), Color.FromArgb(240, 240, 246),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        void DrawPadHints(Graphics g, float s) { DrawHintChips(g, s, ClientSize); }

        /// <summary>The controller hint chips along the bottom (also used on other apps' PiPs): the buttons as they look on
        /// this controller (Xbox's coloured letters, PlayStation's shapes...), then what they do.</summary>
        internal static void DrawHintChips(Graphics g, float s, Size client)
        {
            var hints = App.I.PadHints();
            if (hints.Count == 0) return;
            Font fn = U.UI(11 * s);
            int padX = (int)(8 * s), gap = (int)(4 * s), chipH = (int)(24 * s);
            int maxW = client.Width - 2 * padX;
            var chips = new List<Tuple<List<Pad.Glyph>, string, int, int>>();   // buttons, label, buttons' width, total width
            foreach (var h in hints)
            {
                int bw = GlyphsWidth(g, h.Item1, s, chipH);
                int lw = LT.MeasureText(h.Item2, fn, Size.Empty, TextFormatFlags.NoPadding).Width;
                chips.Add(Tuple.Create(h.Item1, h.Item2, bw, bw + lw + (int)(18 * s)));
            }
            // wrap into rows, laid out from the bottom up
            var rows = new List<List<Tuple<List<Pad.Glyph>, string, int, int>>>();
            var row = new List<Tuple<List<Pad.Glyph>, string, int, int>>();
            int rowW = 0;
            foreach (var c in chips)
            {
                if (row.Count > 0 && rowW + gap + c.Item4 > maxW) { rows.Add(row); row = new List<Tuple<List<Pad.Glyph>, string, int, int>>(); rowW = 0; }
                row.Add(c);
                rowW += (row.Count > 1 ? gap : 0) + c.Item4;
            }
            if (row.Count > 0) rows.Add(row);
            int maxRows = Math.Max(1, (client.Height / 2) / (chipH + gap));
            if (rows.Count > maxRows) rows = rows.Take(maxRows).ToList();          // hints are in priority order: keep the first ones
            int y = client.Height - (int)(8 * s) - rows.Count * (chipH + gap) + gap;
            foreach (var r in rows)
            {
                int total = r.Sum(c => c.Item4) + gap * (r.Count - 1);
                int x = (client.Width - total) / 2;
                foreach (var c in r)
                {
                    var rc = new Rectangle(x, y, c.Item4, chipH);
                    using (var path = U.RoundRect(rc, chipH / 2f))
                    using (var b = new SolidBrush(Color.FromArgb(24, 24, 28)))
                        g.FillPath(b, path);
                    DrawGlyphs(g, c.Item1, x + (int)(4 * s), y, chipH, s);
                    var lr = new Rectangle(x + (int)(4 * s) + c.Item3 + (int)(6 * s), y, c.Item4 - c.Item3 - (int)(10 * s), chipH);
                    LT.DrawText(g, c.Item2, fn, lr, Color.FromArgb(225, 225, 232), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    x += c.Item4 + gap;
                }
                y += chipH + gap;
            }
        }

        static readonly Dictionary<int, Font> symbolFonts = new Dictionary<int, Font>();

        static Font GlyphFont(Pad.Glyph gl, float px)
        {
            // the shapes (✕ ○ □ △ ≡ ⧉ ✛) are in Segoe UI Symbol; letters in Segoe UI Semibold
            bool symbol = gl.Text.Length == 1 && gl.Text[0] > 0x2000;
            if (!symbol) return U.UIBold(px * 0.75f);
            int k = (int)Math.Round(px);
            Font f;
            if (!symbolFonts.TryGetValue(k, out f)) symbolFonts[k] = f = new Font("Segoe UI Symbol", k, FontStyle.Bold, GraphicsUnit.Pixel);
            return f;
        }

        static int GlyphWidth(Graphics g, Pad.Glyph gl, float s, int h)
        {
            int d = h - (int)(4 * s);
            int tw = gl.Plain ? 0 : LT.MeasureText(gl.Text, U.UIBold(8.5f * s), Size.Empty, TextFormatFlags.NoPadding).Width;
            switch (gl.Shape)
            {
                case "plain": return LT.MeasureText(gl.Text, U.UI(10.5f * s), Size.Empty, TextFormatFlags.NoPadding).Width;
                case "bumper": return Math.Max((int)(d * 1.5), tw + (int)(d * 0.5));
                case "trigger": return Math.Max((int)(d * 1.15), tw + (int)(d * 0.4));
                case "pill": return tw + (int)(d * 0.6);
                default: return d;                                   // round, cross
            }
        }

        static int GlyphsWidth(Graphics g, List<Pad.Glyph> gs, float s, int h)
        {
            return gs.Sum(x => GlyphWidth(g, x, s, h)) + (int)(3 * s) * Math.Max(0, gs.Count - 1);
        }

        /// <summary>A rounded rectangle with its own radius at each corner (top-left, top-right, bottom-right, bottom-left).</summary>
        static System.Drawing.Drawing2D.GraphicsPath Corners(RectangleF r, float tl, float tr, float br, float bl)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            Action<float, float, float, float> arc = (x, y, rad, start) => { if (rad > 0.5f) p.AddArc(x, y, rad * 2, rad * 2, start, 90); else p.AddLine(x, y, x, y); };
            arc(r.X, r.Y, tl, 180);
            arc(r.Right - tr * 2, r.Y, tr, 270);
            arc(r.Right - br * 2, r.Bottom - br * 2, br, 0);
            arc(r.X, r.Bottom - bl * 2, bl, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Draws a row of button glyphs, each in its controller's shape: round face buttons in their colours, flat
        /// shoulder bumpers, rounded-top triggers, pills for Options and Guide, the D-pad's cross, plain text between.</summary>
        internal static void DrawGlyphs(Graphics g, List<Pad.Glyph> gs, int x, int y, int h, float s)
        {
            int d = h - (int)(4 * s), top = y + (h - d) / 2;
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            foreach (var gl in gs)
            {
                int w = GlyphWidth(g, gl, s, h);
                if (gl.Plain)
                    LT.DrawText(g, gl.Text, U.UI(10.5f * s), new Rectangle(x, y, w + 2, h), Color.FromArgb(200, 200, 210), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                else if (gl.Shape == "cross")
                    LT.DrawText(g, "✛", GlyphFont(gl, d * 0.95f), new Rectangle(x, y, w, h), Color.FromArgb(200, 200, 210), flags);
                else
                {
                    RectangleF r;
                    System.Drawing.Drawing2D.GraphicsPath path;
                    if (gl.Shape == "bumper")
                    {
                        float bh = d * 0.72f; r = new RectangleF(x, y + (h - bh) / 2, w, bh);
                        float big = bh * 0.5f, small = bh * 0.2f;
                        path = gl.Left ? Corners(r, big, small, small, small) : Corners(r, small, big, small, small);
                    }
                    else if (gl.Shape == "trigger")
                    {
                        r = new RectangleF(x, top, w, d);
                        float big = d * 0.5f, mid = d * 0.3f, small = d * 0.14f;
                        path = gl.Left ? Corners(r, big, mid, small, small) : Corners(r, mid, big, small, small);
                    }
                    else if (gl.Shape == "pill")
                    {
                        float ph = d * 0.72f; r = new RectangleF(x, y + (h - ph) / 2, w, ph);
                        path = Corners(r, ph / 2, ph / 2, ph / 2, ph / 2);
                    }
                    else { r = new RectangleF(x, top, w, d); path = Corners(r, d / 2f, d / 2f, d / 2f, d / 2f); }
                    using (path)
                    using (var b = new SolidBrush(Color.FromArgb(62, 62, 70)))
                        g.FillPath(b, path);
                    var f = gl.Shape == "round" ? GlyphFont(gl, d * 0.62f) : U.UIBold(8.5f * s);
                    var tr = Rectangle.Round(r);
                    if (gl.Shape == "trigger") tr.Y += (int)(d * 0.06f);
                    LT.DrawText(g, gl.Text, f, tr, gl.Fg, flags);
                }
                x += w + (int)(3 * s);
            }
            g.SmoothingMode = oldMode;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            {
                Point p = PointToClient(U.LParamPoint(m.LParam));
                BuildLayout();
                bool interactive = showCtl && (IndexAt(p) >= 0 || pillRect.Contains(p));
                m.Result = new IntPtr(interactive ? Native.HTCLIENT : Native.HTTRANSPARENT);
                return;
            }
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            if (m.Msg == Native.WM_MOUSEWHEEL)
            {
                long wp = m.WParam.ToInt64();
                int delta = (short)((wp >> 16) & 0xFFFF);
                int keys = (int)(wp & 0xFFFF);
                if (hot >= 0 && hot < btns.Count && btns[hot].Volume) pip.VolumeStep(delta > 0 ? 1 : -1);
                else pip.HandleWheel(delta, (keys & 0x8) != 0, (keys & 0x4) != 0, U.LParamPoint(m.LParam));
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = IndexAt(e.Location);
            if (h != hot) { hot = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot != -1) { hot = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) pressed = IndexAt(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { pip.ShowMenu(); return; }
            if (e.Button != MouseButtons.Left) return;
            int i = IndexAt(e.Location);
            if (i >= 0 && i == pressed)
            {
                pressed = -1;
                Action a = btns[i].Click;
                try { a(); } catch (Exception ex) { App.ReportError(ex); }
                return;
            }
            pressed = -1;
        }
    }

}
