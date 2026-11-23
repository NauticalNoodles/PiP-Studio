using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace PipStudio
{
    /// <summary>
    /// Saved layouts: a set of PiPs (their sources, places, sizes and options) under a name, like "Gaming" or "Work", that you
    /// bring back in one go. A layout can also open by itself when an app starts, and close its PiPs when that app exits.
    /// Kept in layouts.ini next to the settings.
    /// </summary>
    class Layout
    {
        public string Name = "";
        public string App = "";              // a process name ("eldenring"): opens when it starts; "" = only by hand
        public bool CloseWithApp = true;     // ...and its PiPs close when that app exits
        public List<List<string>> Pips = new List<List<string>>();   // each PiP's saved state (PipState as key=value lines)

        /// <summary>The PiPs this layout opened (to close when its app exits).</summary>
        public readonly List<PipWindow> Opened = new List<PipWindow>();
        public bool AppWasRunning;

        public int Count { get { return Pips.Count; } }
    }

    static class Layouts
    {
        static List<Layout> all;
        public static event Action Changed;
        static string FilePath { get { return Path.Combine(Settings.Dir, "layouts.ini"); } }

        public static List<Layout> All { get { if (all == null) Load(); return all; } }

        public static Layout Find(string name) { return All.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }

        static void Load()
        {
            all = new List<Layout>();
            try
            {
                if (File.Exists(FilePath)) all = Parse(File.ReadAllLines(FilePath));
            }
            catch (Exception ex) { App.ReportError(ex); }
            foreach (var l in all) l.AppWasRunning = Running(l.App);   // an app that's already running doesn't open its layout again
        }

        public static List<Layout> Parse(IEnumerable<string> lines)
        {
            var list = new List<Layout>();
            Layout cur = null;
            List<string> head = null, pip = null;
            Action finishHead = () =>
            {
                if (cur != null && head != null) { Ser.Read(cur, Ser.ParseKv(head)); head = null; }
            };
            foreach (var raw in lines)
            {
                string t = raw.Trim();
                if (t == "[layout]") { finishHead(); cur = new Layout(); list.Add(cur); head = new List<string>(); pip = null; }
                else if (t == "[pip]" && cur != null) { finishHead(); pip = new List<string>(); cur.Pips.Add(pip); }
                else if (pip != null) pip.Add(t);
                else if (head != null) head.Add(t);
            }
            finishHead();
            return list.Where(l => l.Name.Length > 0).ToList();
        }

        public static string Format(IEnumerable<Layout> layouts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; PiP Studio layouts");
            foreach (var l in layouts)
            {
                sb.AppendLine("[layout]");
                sb.AppendLine("Name=" + Ser.ToStr(l.Name));
                sb.AppendLine("App=" + Ser.ToStr(l.App));
                sb.AppendLine("CloseWithApp=" + Ser.ToStr(l.CloseWithApp));
                foreach (var p in l.Pips)
                {
                    sb.AppendLine("[pip]");
                    foreach (var line in p) sb.AppendLine(line);
                }
            }
            return sb.ToString();
        }

        /// <summary>Writes layouts.ini; <paramref name="notify"/>: the list changed (the window rebuilds its Layouts section).</summary>
        public static void Save(bool notify = true)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                U.SafeWrite(FilePath, Format(All));
            }
            catch (Exception ex) { App.ReportError(ex); }
            var c = Changed;
            if (notify && c != null) c();
        }

        /// <summary>Replaces every layout (importing settings).</summary>
        public static void ReplaceAll(List<Layout> layouts)
        {
            all = layouts;
            foreach (var l in all) l.AppWasRunning = Running(l.App);
            Save();
        }

        /// <summary>The PiPs that are open now, saved under a name (replacing a layout with that name, keeping its app).</summary>
        public static Layout SaveCurrent(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return null;
            var l = Find(name);
            if (l == null) { l = new Layout { Name = name }; All.Add(l); }
            l.Pips.Clear();
            foreach (var p in PipStudio.App.I.Pips.Where(x => !x.IsClosing))
            {
                p.SnapshotState();
                var sb = new StringBuilder();
                Ser.Write(p.St, sb);
                l.Pips.Add(sb.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList());
            }
            Save();
            return l;
        }

        public static void Delete(Layout l) { All.Remove(l); Save(); }

        public static void SetApp(Layout l, string app)
        {
            l.App = app ?? "";
            l.AppWasRunning = Running(l.App);
            Save(false);
        }

        public static void SetCloseWithApp(Layout l, bool on) { l.CloseWithApp = on; Save(false); }

        /// <summary>
        /// Brings a layout back. By hand (<paramref name="replace"/>) it takes the place of the PiPs that are open; when its app
        /// starts it opens next to them. A window that isn't open yet gets a PiP that waits for it.
        /// </summary>
        public static int Open(Layout l, bool replace)
        {
            var app = PipStudio.App.I;
            if (replace)
                foreach (var p in app.Pips.Where(x => !x.IsClosing).ToList()) p.CloseAnimated();
            if (app.HiddenAll) app.ToggleHideAll();                          // show them again (with the new ones)
            var states = new List<PipState>();
            foreach (var b in l.Pips.Where(b => !PipStudio.App.Retired(b)))
            {
                var st = new PipState();
                Ser.Read(st, Ser.ParseKv(b));
                states.Add(st);
            }
            if (!PipStudio.App.S.AllowMultiple) states = states.Take(1).ToList();   // one PiP at a time: the first
            l.Opened.RemoveAll(p => p.IsClosing || p.IsDisposed);
            int n = 0;
            foreach (var st in states)
            {
                var p = app.OpenState(st, true);
                if (p != null) { l.Opened.Add(p); n++; }
            }
            return n;
        }

        /// <summary>Called every few seconds: opens the layouts whose app just started, closes the PiPs of those whose app exited.</summary>
        public static void Tick()
        {
            if (all == null || all.Count == 0) return;
            foreach (var l in all)
            {
                if (l.App.Length == 0) continue;
                bool running = Running(l.App);
                if (running && !l.AppWasRunning) Open(l, false);
                else if (!running && l.AppWasRunning && l.CloseWithApp)
                {
                    foreach (var p in l.Opened.Where(p => !p.IsClosing && !p.IsDisposed).ToList()) p.CloseAnimated();
                    l.Opened.Clear();
                }
                l.AppWasRunning = running;
            }
        }

        static bool Running(string app)
        {
            if (string.IsNullOrEmpty(app)) return false;
            try
            {
                var ps = Process.GetProcessesByName(app);
                bool any = ps.Length > 0;
                foreach (var p in ps) p.Dispose();
                return any;
            }
            catch { return false; }
        }

        /// <summary>Apps that have a window open now (for picking the app a layout opens with), by process name.</summary>
        public static List<string> AppsWithWindows()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var h in WinEnum.AltTab())
                {
                    if (U.IsOurs(h)) continue;
                    string n = U.ProcessName(h);
                    if (!string.IsNullOrEmpty(n) && !PipRules.NotPipProcesses.Contains(n)) names.Add(n);
                }
            }
            catch { }
            return names.ToList();
        }
    }
}
