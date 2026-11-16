using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace PipStudio
{
    /// <summary>
    /// Crash reports. When PiP Studio crashes, it writes what went wrong to crash.txt in its settings folder. Crashes too
    /// deep to catch are noticed by a "running" marker that a normal exit removes: if it's still there at the next start
    /// and errors were logged during that run, the run ended in a crash.
    /// Next time PiP Studio starts it says so (a notification, so it never pops up over a game) and offers to send the
    /// report: that opens a new GitHub issue in the browser with the bug report form already filled in, so you see
    /// exactly what's sent and send it yourself. Nothing is uploaded on its own. Your Windows user name and PC name are
    /// taken out of the report. Settings > General > About has "Report a problem" for anything that isn't a crash.
    /// </summary>
    static class CrashReports
    {
        const string NewIssue = "https://github.com/NauticalNoodles/PiP-Studio/issues/new";
        const int MaxUrl = 7600;                                     // browsers and GitHub cope with about 8000 characters

        static string CrashFile { get { return Path.Combine(Settings.Dir, "crash.txt"); } }
        static string Marker { get { return Path.Combine(Settings.Dir, "running.marker"); } }
        static string ErrorLog { get { return Path.Combine(Settings.Dir, "errors.log"); } }

        /// <summary>At start-up, before anything else: catch fatal errors from here on.</summary>
        public static void Install()
        {
            AppDomain.CurrentDomain.UnhandledException += (o, e) =>
            {
                if (e.IsTerminating) WriteCrash(e.ExceptionObject as Exception);
            };
        }

        /// <summary>Once PiP Studio is up: look at how the last run ended, then mark this one as running.</summary>
        public static void CheckLastRun()
        {
            string report = null;
            try
            {
                if (File.Exists(CrashFile)) report = File.ReadAllText(CrashFile);
                else if (File.Exists(Marker))
                {
                    // ended without a normal exit: only call it a crash when errors were logged during that run
                    var started = File.GetLastWriteTimeUtc(Marker);
                    if (File.Exists(ErrorLog) && File.GetLastWriteTimeUtc(ErrorLog) > started)
                        report = Header("PiP Studio stopped without closing normally.") + RecentErrors(started);
                }
            }
            catch { }
            try { Directory.CreateDirectory(Settings.Dir); File.WriteAllText(Marker, DateTime.UtcNow.ToString("o")); } catch { }
            if (report == null) return;
            string r = report;
            App.I.Balloon("PiP Studio closed unexpectedly", "Click to send a crash report, so it can be fixed.", true, null, () => Offer(r));
            App.RunSoon(() => { try { File.Delete(CrashFile); } catch { } }, 2000);   // offered once
        }

        /// <summary>A normal exit: not a crash.</summary>
        public static void CleanExit() { try { File.Delete(Marker); } catch { } }

        static void WriteCrash(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllText(CrashFile, Header("PiP Studio crashed.") + Describe(ex) + RecentErrors(DateTime.UtcNow.AddMinutes(-10)));
            }
            catch { }
        }

        // ------------------------------------------------------------ what a report says
        static string Header(string what)
        {
            var sb = new StringBuilder();
            sb.AppendLine(what);
            sb.AppendLine("Version: " + Updater.CurrentText + (Packaged.IsPackaged ? " (Microsoft Store)" : ""));
            sb.AppendLine("Windows: " + WindowsVersion());
            sb.AppendLine("When: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC");
            try { sb.AppendLine("Displays: " + System.Windows.Forms.Screen.AllScreens.Length + ", Lite mode: " + (App.S != null && App.S.LiteMode ? "on" : "off")); } catch { }
            sb.AppendLine();
            return sb.ToString();
        }

        static string Describe(Exception ex)
        {
            if (ex == null) return "(no details)\n";
            var sb = new StringBuilder();
            for (var e = ex; e != null; e = e.InnerException)
            {
                sb.AppendLine(e.GetType().FullName + ": " + e.Message);
                sb.AppendLine(Trim(e.StackTrace ?? "", 2500));
                if (e.InnerException != null) sb.AppendLine("--- caused by:");
            }
            return sb.ToString();
        }

        /// <summary>The last few errors.log entries written since <paramref name="since"/>.</summary>
        static string RecentErrors(DateTime since)
        {
            try
            {
                if (!File.Exists(ErrorLog)) return "";
                var text = File.ReadAllText(ErrorLog);
                var entries = text.Split(new[] { Environment.NewLine + Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(x => { DateTime t; return x.Length > 19 && DateTime.TryParse(x.Substring(0, 19), out t) && t.ToUniversalTime() >= since.AddSeconds(-1); })
                    .Reverse().Take(3).Reverse().ToList();
                if (entries.Count == 0) return "";
                return "\nRecent errors:\n" + string.Join("\n\n", entries.Select(x => Trim(x, 1500)));
            }
            catch { return ""; }
        }

        static string Trim(string s, int max) { return s.Length <= max ? s : s.Substring(0, max) + "\n…"; }

        static string WindowsVersion()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int build = Environment.OSVersion.Version.Build;
                    string name = build >= 22000 ? "Windows 11" : "Windows 10";
                    string display = k == null ? null : k.GetValue("DisplayVersion") as string;
                    return name + (display != null ? " " + display : "") + " (build " + build + ")";
                }
            }
            catch { return Environment.OSVersion.VersionString; }
        }

        /// <summary>Takes your Windows user name and PC name out of a report (they show up in file paths).</summary>
        public static string Scrub(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile)) s = s.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
                if (Environment.UserName.Length > 2) s = s.Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
                if (Environment.MachineName.Length > 2) s = s.Replace(Environment.MachineName, "<pc>", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            return s;
        }

        // ------------------------------------------------------------ sending
        /// <summary>The crash dialog: send (opens the filled-in GitHub issue), copy, or not now.</summary>
        static void Offer(string report)
        {
            report = Scrub(report);
            var preview = new System.Windows.Controls.TextBox
            {
                Text = report, IsReadOnly = true, TextWrapping = System.Windows.TextWrapping.Wrap, MaxHeight = 180,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas, Cascadia Mono"), FontSize = 11
            };
            var copy = new System.Windows.Controls.Button { Content = "Copy report", Margin = new System.Windows.Thickness(0, 8, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Padding = new System.Windows.Thickness(14, 4, 14, 4) };
            copy.Click += (o, e) => { try { System.Windows.Clipboard.SetText(report); copy.Content = "Copied"; } catch { } };
            var panel = new System.Windows.Controls.StackPanel();
            panel.Children.Add(preview);
            panel.Children.Add(copy);
            MainUI.Ask("PiP Studio closed unexpectedly",
                "Sending this report helps get it fixed. It opens a new issue on GitHub in your browser with this already filled in, so you can check it, add what you were doing, and send it yourself. Your Windows user name and PC name are left out.",
                "Send report", "Not now", ok => { if (ok) OpenIssue("Crash: " + FirstLine(report), "PiP Studio closed unexpectedly. What were you doing at the time?", report); },
                false, panel);
        }

        /// <summary>Settings > General > About > Report a problem: a new issue with the version, Windows and recent errors filled in.</summary>
        public static void ReportProblem()
        {
            string details = Scrub(Header("Problem report.") + RecentErrors(DateTime.UtcNow.AddDays(-1)));
            OpenIssue("", "", details);
        }

        static string FirstLine(string report)
        {
            var lines = report.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var ex = lines.FirstOrDefault(x => x.Contains("Exception:"));
            string t = ex ?? lines.FirstOrDefault() ?? "PiP Studio";
            return t.Length > 90 ? t.Substring(0, 90) + "…" : t;
        }

        /// <summary>Opens GitHub's bug report form, filled in from a link (field ids from .github/ISSUE_TEMPLATE/bug_report.yml).</summary>
        static void OpenIssue(string title, string what, string details)
        {
            Func<string, string> q = Uri.EscapeDataString;
            string build(string d) => NewIssue + "?template=bug_report.yml&labels=bug"
                + (title.Length > 0 ? "&title=" + q(title) : "")
                + (what.Length > 0 ? "&what=" + q(what) : "")
                + "&version=" + q(Updater.CurrentText)
                + "&windows=" + q(WindowsVersion())
                + "&extra=" + q("```\n" + d.TrimEnd() + "\n```");
            string url = build(details);
            for (int keep = details.Length; url.Length > MaxUrl && keep > 200; )
            {
                keep = (int)(keep * 0.8);                                  // too long for a link: shorten the details
                url = build(details.Substring(0, keep) + "\n… (shortened; use Copy report for all of it)");
            }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { App.ReportError(ex); }
        }
    }
}
