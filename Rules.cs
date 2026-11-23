using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace PipStudio
{
    /// <summary>
    /// Decisions that only need numbers and names, kept apart from Windows so the tests (tests/) can check them on any machine:
    /// where PiPs go when one is dropped on another, and which windows of other apps count as PiPs.
    /// </summary>
    static class Stacking
    {
        /// <summary>
        /// Where the movable rectangles that <paramref name="r"/> overlaps go, so r can stay: each is pushed out the way it was
        /// pushed (away from what landed on it, along the side they overlap least, with a gap), and anything it then lands on
        /// is pushed in turn, so they end up stacked. Keys are indexes into <paramref name="movable"/>; only moved ones are
        /// listed. Null when they can't all fit in <paramref name="area"/>, or r lands on something that can't move.
        /// </summary>
        /// <param name="pushDx">With <paramref name="pushDy"/>: push everything this way instead (-1, 0 or 1), even past r. A controller
        /// move up into a taken spot pushes the PiPs there down, so the two swap places.</param>
        public static Dictionary<int, Rectangle> PlanBump(Rectangle r, IList<Rectangle> movable, IEnumerable<Rectangle> fixedOnes, Rectangle area, int gap, int pushDx = 0, int pushDy = 0)
        {
            var placed = new Dictionary<int, Rectangle>();
            var fixedRects = new List<Rectangle> { r };
            var stuck = fixedOnes.ToList();
            if (stuck.Any(f => f.IntersectsWith(r))) return null;              // dropped on something that can't move
            fixedRects.AddRange(stuck);
            Func<int, Rectangle> at = i => placed.ContainsKey(i) ? placed[i] : movable[i];
            for (int guard = 0; guard < 4 * (movable.Count + 1); guard++)
            {
                // the next one sitting on something that has its place: the one nearest r first
                int hit = -1; double hitD = double.MaxValue;
                for (int i = 0; i < movable.Count; i++)
                {
                    if (placed.ContainsKey(i)) continue;
                    var cur0 = at(i);
                    if (!fixedRects.Any(f => f.IntersectsWith(cur0))) continue;
                    double d = Dist(cur0, r);
                    if (d < hitD) { hitD = d; hit = i; }
                }
                if (hit < 0) return placed;
                Rectangle cur = at(hit);
                Rectangle best = Rectangle.Empty; double bestD = double.MaxValue;
                foreach (var f in fixedRects.Where(f => f.IntersectsWith(cur)).ToList())
                {
                    // pushed away from what it's on, never past it (that would swap them round), along the side they overlap
                    // least (dropped on its top edge: it goes down; on its side: sideways)
                    double dx = (cur.X + cur.Width / 2.0) - (f.X + f.Width / 2.0), dy = (cur.Y + cur.Height / 2.0) - (f.Y + f.Height / 2.0);
                    int ox = Math.Min(cur.Right, f.Right) - Math.Max(cur.Left, f.Left), oy = Math.Min(cur.Bottom, f.Bottom) - Math.Max(cur.Top, f.Top);
                    bool vert = oy <= ox;
                    bool[] away = { vert && dy <= 0, vert && dy >= 0, !vert && dx <= 0, !vert && dx >= 0 };
                    if (pushDx != 0 || pushDy != 0) away = new[] { pushDy < 0, pushDy > 0, pushDx < 0, pushDx > 0 };
                    var cands = new[]
                    {
                        new Rectangle(cur.X, f.Top - gap - cur.Height, cur.Width, cur.Height),    // above
                        new Rectangle(cur.X, f.Bottom + gap, cur.Width, cur.Height),              // below
                        new Rectangle(f.Left - gap - cur.Width, cur.Y, cur.Width, cur.Height),    // left
                        new Rectangle(f.Right + gap, cur.Y, cur.Width, cur.Height),               // right
                    };
                    for (int k = 0; k < cands.Length; k++)
                    {
                        var c = cands[k];
                        if (!away[k] || !area.Contains(c) || fixedRects.Any(o => o.IntersectsWith(c))) continue;
                        double d = Math.Abs(c.X - cur.X) + Math.Abs(c.Y - cur.Y) + (k >= 2 ? 1 : 0);   // up or down wins a tie
                        if (d < bestD) { bestD = d; best = c; }
                    }
                }
                if (bestD == double.MaxValue) return null;                    // no room
                placed[hit] = best;
                fixedRects.Add(best);
            }
            return null;
        }

        /// <summary>r moved up (or down) past the rectangles it overlaps, so it stacks next to them.</summary>
        public static Rectangle StackPast(Rectangle r, IList<Rectangle> others, bool goDown, int gap)
        {
            for (int i = 0; i <= others.Count; i++)
            {
                int hit = -1;
                for (int k = 0; k < others.Count; k++) if (others[k].IntersectsWith(r)) { hit = k; break; }
                if (hit < 0) break;
                r.Y = goDown ? others[hit].Bottom + gap : others[hit].Top - gap - r.Height;
            }
            return r;
        }

        /// <summary>
        /// Where a PiP goes when it's put at <paramref name="want"/> (a snap spot, a drop, a new PiP), and which others move
        /// for it: always on the screen, never on top of another PiP when there's any way round it.
        /// <list type="bullet">
        /// <item>The spot is free: it goes there.</item>
        /// <item><paramref name="stackFirst"/> (snapping, controller moves, new PiPs): it stacks next to the PiP(s) there,
        /// above or below as <paramref name="stackDown"/> says.</item>
        /// <item><paramref name="allowBump"/> (things you do to this PiP): it stays and the others are pushed aside.</item>
        /// <item>Otherwise it stacks either way, or, when the screen is full, stays put (overlapping beats off-screen).</item>
        /// </list>
        /// </summary>
        public static Rectangle Place(Rectangle want, IList<Rectangle> movable, IList<Rectangle> stuck, Rectangle area, int gap,
                                      bool stackDown, bool stackFirst, bool allowBump, out Dictionary<int, Rectangle> bump)
        {
            bump = new Dictionary<int, Rectangle>();
            want = Inside(want, area);
            var all = movable.Concat(stuck).ToList();
            Func<Rectangle, bool> free = c => area.Contains(c) && !all.Any(o => o.IntersectsWith(c));
            if (!all.Any(o => o.IntersectsWith(want))) return want;
            if (stackFirst) { var a = StackPast(want, all, stackDown, gap); if (free(a)) return a; }
            if (allowBump)
            {
                var plan = PlanBump(want, movable, stuck, area, gap);
                if (plan != null) { bump = plan; return want; }
            }
            var b1 = StackPast(want, all, stackDown, gap); if (free(b1)) return b1;
            var b2 = StackPast(want, all, !stackDown, gap); if (free(b2)) return b2;
            return want;
        }

        /// <summary>r moved (not resized) so it's inside area, as far as it fits.</summary>
        public static Rectangle Inside(Rectangle r, Rectangle area)
        {
            int x = Math.Max(area.Left, Math.Min(r.X, area.Right - r.Width));
            int y = Math.Max(area.Top, Math.Min(r.Y, area.Bottom - r.Height));
            return new Rectangle(x, y, r.Width, r.Height);
        }

        /// <summary>Whether a PiP going from <paramref name="from"/> to <paramref name="to"/> actually moved the way you pressed
        /// (dx, dy: -1, 0 or 1), by more than a few pixels: a move that lands back where it was (or goes the other way) doesn't count.</summary>
        public static bool MovedToward(Rectangle from, Rectangle to, int dx, int dy, int minPx = 8)
        {
            double mx = (to.X + to.Width / 2.0) - (from.X + from.Width / 2.0), my = (to.Y + to.Height / 2.0) - (from.Y + from.Height / 2.0);
            return dx * mx + dy * my >= minPx;
        }

        static double Dist(Rectangle a, Rectangle b)
        {
            double dx = (a.X + a.Width / 2.0) - (b.X + b.Width / 2.0), dy = (a.Y + a.Height / 2.0) - (b.Y + b.Height / 2.0);
            return dx * dx + dy * dy;
        }
    }

    /// <summary>What PiP Studio knows about another app's window when deciding whether it's that app's PiP.</summary>
    class WindowFacts
    {
        public string Title = "", Class = "", Process = "";
        public int Style, ExStyle;
        public bool HasOwner, Cloaked;   // filled in by ExternalPip.Facts
        public Rectangle Rect, Monitor;
    }

    static class PipRules
    {
        public const int WS_CHILD = 0x40000000, WS_THICKFRAME = 0x40000;
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x8000000;

        public static readonly HashSet<string> Titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Picture in picture", "Picture-in-Picture", "Picture-in-picture", "Picture in Picture", "Bild im Bild", "Image dans l'image",
            "Imagen en imagen", "Picture-in-picture video", "Picture in picture video"
        };

        public static readonly HashSet<string> DiscordNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "discord", "discordptb", "discordcanary", "vesktop", "legcord" };

        public static readonly HashSet<string> NotPipClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "tooltips_class32", "#32768", "#32770",
            "NotifyIconOverflowWindow", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
            "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd", "Xaml_WindowedPopupClass", "SysShadow", "IME", "MSCTFIME UI"
        };

        public static readonly HashSet<string> NotPipProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost", "LockApp",
            "GameBar", "GameBarFTServer", "XboxGameBarWidgets", "NVIDIA Overlay", "nvcontainer", "RadeonSoftware", "AMDRSServ",
            "MSIAfterburner", "RTSS", "EncoderServer", "ScreenClippingHost", "SnippingTool", "PowerToys", "PowerToys.AlwaysOnTop",
            "Taskmgr", "SystemSettings", "ApplicationFrameHost"
        };

        public static bool IsPipTitle(string t) { return t != null && Titles.Contains(t.Trim()); }

        /// <summary>Resizable by its edges: PiPs and pop-outs are; menus, flyouts and notifications aren't.</summary>
        public static bool Resizable(WindowFacts w) { return (w.Style & WS_THICKFRAME) != 0; }

        /// <summary>A window that behaves like a PiP: always on top, small, picture-shaped, resizable, a normal top-level window
        /// you can click (not a click-through overlay, a menu, a tooltip or part of Windows).</summary>
        public static bool LooksLikePip(WindowFacts w)
        {
            if ((w.ExStyle & WS_EX_TOPMOST) == 0) return false;
            if ((w.ExStyle & (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)) != 0) return false;   // overlays, notifications
            if ((w.Style & WS_CHILD) != 0) return false;
            if (!Resizable(w)) return false;                                  // menus and flyouts (like Discord's tray menu)
            if (w.HasOwner) return false;                                     // dialogs and pop-ups
            if (w.Cloaked) return false;
            if (NotPipClasses.Contains(w.Class) || NotPipProcesses.Contains(w.Process)) return false;
            var r = w.Rect;
            if (r.Width < 160 || r.Height < 90) return false;
            if ((double)r.Width * r.Height > 0.45 * w.Monitor.Width * w.Monitor.Height) return false;   // big: not a PiP
            double a = r.Width / (double)r.Height;
            return a >= 0.45 && a <= 2.8;
        }

        /// <summary>
        /// Discord's popped-out call/stream window. Its main window's title always ends in "Discord" ("Friends - Discord",
        /// "#general | Server - Discord"); the pop-out is a second, resizable window of the same app with any other (or no) title.
        /// </summary>
        public static bool IsDiscordPopout(WindowFacts w)
        {
            if (w.Class != "Chrome_WidgetWin_1" || !DiscordNames.Contains(w.Process)) return false;
            string title = (w.Title ?? "").Trim();
            if (title.EndsWith("Discord", StringComparison.OrdinalIgnoreCase)) return false;
            if (title.IndexOf("overlay", StringComparison.OrdinalIgnoreCase) >= 0) return false;   // the in-game overlay
            if ((w.ExStyle & (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT)) != 0) return false;           // click-through helpers
            if (!Resizable(w)) return false;                                  // its tray menu and other pop-ups
            if (w.Rect.Width < 200 || w.Rect.Height < 120) return false;
            // the overlay covers the whole screen (even when Shift+Tab makes it clickable); a call pop-out is small
            return (double)w.Rect.Width * w.Rect.Height < 0.6 * w.Monitor.Width * w.Monitor.Height;
        }
    }
}
