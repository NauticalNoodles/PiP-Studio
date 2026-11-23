using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PipStudio
{
    /// <summary>
    /// Settings in a file, to move them to another PC or keep a copy: every setting (shortcuts, controller buttons, look and
    /// behavior) and the saved layouts. Paired phones stay behind: they belong to this PC.
    /// </summary>
    static class Backup
    {
        public const string Extension = ".pipstudio";

        /// <summary>Settings that only make sense on this PC.</summary>
        static readonly HashSet<string> ThisPcOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CompanionDevices", "CompanionOn", "GpuPrefsAdded", "UpdateNotified", "FirstRun"
        };

        public static void Export(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; PiP Studio settings (" + Updater.CurrentText + "), " + DateTime.Now.ToString("yyyy-MM-dd"));
            sb.AppendLine("[settings]");
            var all = new StringBuilder();
            Ser.Write(App.S, all);
            foreach (var line in all.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = line.IndexOf('=');
                if (i > 0 && ThisPcOnly.Contains(line.Substring(0, i))) continue;
                sb.AppendLine(line);
            }
            sb.AppendLine("[layouts]");
            sb.Append(Layouts.Format(Layouts.All));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Reads a file made by Export into the settings and layouts. Null when it worked, otherwise what went wrong.</summary>
        public static string Import(string path)
        {
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception ex) { return ex.Message; }
            var settings = new List<string>();
            var layouts = new List<string>();
            List<string> cur = null;
            foreach (var raw in lines)
            {
                string t = raw.Trim();
                if (t == "[settings]") { cur = settings; continue; }
                if (t == "[layouts]") { cur = layouts; continue; }
                if (cur != null) cur.Add(raw);
            }
            if (settings.Count == 0) return L.T("This isn't a PiP Studio settings file.");
            var kv = Ser.ParseKv(settings);
            foreach (var k in ThisPcOnly) kv.Remove(k);
            Ser.Read(App.S, kv);
            App.S.Save();
            if (layouts.Count > 0) Layouts.ReplaceAll(Layouts.Parse(layouts));
            App.I.SettingsChanged(true);                                  // the shortcuts too
            return null;
        }
    }
}
