using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PipStudio
{
    /// <summary>
    /// Updates from GitHub releases, for the installer and portable versions (the Store updates its own version).
    /// - Checks https://api.github.com/repos/NauticalNoodles/PiP-Studio/releases/latest: once a day when "Check for updates
    ///   automatically" is on, or when you press "Check now". Nothing about you or your PC is sent.
    /// - Installing downloads the release's portable zip and checks it against the release's SHA256SUMS.txt. A running exe
    ///   can be renamed but not overwritten, so the current exe becomes PiPStudio.exe.old, the new one takes its place, and
    ///   the new exe starts with --replace (it waits for this one to quit, see Program.Main). The .old file is deleted on the
    ///   next start.
    /// - When the exe's folder can't be written (installed for all users in Program Files), it runs the release's
    ///   installer instead, which asks for permission and closes PiP Studio itself.
    /// </summary>
    static class Updater
    {
        const string Repo = "NauticalNoodles/PiP-Studio";
        const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
        /// <summary>The installer's AppId (installer\PiPStudio.iss) as Windows' Apps list knows it.</summary>
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{FE41C80D-B047-4C51-BF61-37DAF8585357}_is1";

        public enum State { Idle, Checking, UpToDate, Available, Downloading, Installing, Failed }

        public static State Now { get; private set; }
        public static Version Latest { get; private set; }
        public static string ReleaseUrl { get; private set; }
        public static int Percent { get; private set; }
        public static string Error { get; private set; }
        /// <summary>Raised on the UI thread whenever the state above changes.</summary>
        public static event Action Changed;

        static string zipUrl, setupUrl, sumsUrl;
        static System.Windows.Forms.Timer timer;
        static readonly HttpClient http = MakeClient();

        /// <summary>This copy's version (from &lt;Version&gt; in PiPStudio.csproj).</summary>
        public static readonly Version Current = ReadVersion();
        public static string CurrentText { get { return Current.ToString(3); } }

        /// <summary>False for the Store version: the Store installs its updates itself.</summary>
        public static bool Supported { get { return !Packaged.IsPackaged; } }

        static Version ReadVersion()
        {
            try
            {
                var v = Assembly.GetEntryAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
            catch { return new Version(0, 0, 0); }
        }

        static HttpClient MakeClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("PiPStudio/" + ReadVersion().ToString(3));   // GitHub's API requires a User-Agent
            return c;
        }

        static void Set(State s)
        {
            Now = s;
            var h = Changed;
            if (h != null) { try { h(); } catch (Exception ex) { App.ReportError(ex); } }
        }

        // ------------------------------------------------------------ start-up and the daily check
        /// <summary>Called once at start-up: tidies up after an update and starts the daily check.</summary>
        public static void Start(bool justUpdated)
        {
            if (!Supported) return;
            App.RunSoon(CleanUp, 8000);
            if (justUpdated) App.I.Balloon("PiP Studio was updated", "You're now on version " + CurrentText + ".", true, "settings");
            timer = new System.Windows.Forms.Timer { Interval = 60 * 60 * 1000 };   // look once an hour whether a day has passed
            timer.Tick += (o, e) => AutoCheck();
            timer.Start();
            App.RunSoon(AutoCheck, 45000);                                           // not right away: start-up is busy enough
        }

        static void AutoCheck()
        {
            if (!App.S.CheckForUpdates || Now == State.Checking || Now == State.Downloading || Now == State.Installing) return;
            long last;
            if (long.TryParse(App.S.LastUpdateCheck, out last) && (DateTime.UtcNow - new DateTime(last, DateTimeKind.Utc)).TotalHours < 23) return;
            Check(false);
        }

        /// <summary>Removes the previous exe and the download folder left by an update.</summary>
        static void CleanUp()
        {
            try { string old = Environment.ProcessPath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true); } catch { }
        }

        static string TempDir { get { return Path.Combine(Path.GetTempPath(), "PiPStudio-update"); } }

        // ------------------------------------------------------------ checking
        /// <summary>Looks for a newer release. <paramref name="manual"/>: you pressed "Check now", so failures are shown.</summary>
        public static async void Check(bool manual)
        {
            if (!Supported || Now == State.Checking || Now == State.Downloading || Now == State.Installing) return;
            Set(State.Checking);
            try
            {
                var found = await FetchLatest();
                App.S.LastUpdateCheck = DateTime.UtcNow.Ticks.ToString(U.CI);
                App.I.SaveSettingsSoon();
                if (found == null || found <= Current) { Set(State.UpToDate); return; }
                Latest = found;
                Set(State.Available);
                if (!manual && App.S.UpdateNotified != found.ToString(3))
                {
                    App.S.UpdateNotified = found.ToString(3);
                    App.I.Balloon("Update available", "PiP Studio " + found.ToString(3) + " is out. Click to update.", true, "settings");
                }
            }
            catch (Exception ex)
            {
                Error = Friendly(ex);
                Set(manual ? State.Failed : State.Idle);   // a failed check in the background stays quiet
            }
        }

        /// <summary>The newest published release's version, or null when there's none. Also notes its download links.</summary>
        static async Task<Version> FetchLatest()
        {
            using (var resp = await http.GetAsync(LatestApi))
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;   // no releases yet
                resp.EnsureSuccessStatusCode();
                using (var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()))
                {
                    var root = doc.RootElement;
                    string tag = root.GetProperty("tag_name").GetString() ?? "";
                    Version v;
                    if (!Version.TryParse(tag.TrimStart('v', 'V'), out v)) return null;
                    v = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
                    ReleaseUrl = root.TryGetProperty("html_url", out var page) ? page.GetString() : ReleasesPage;
                    zipUrl = setupUrl = sumsUrl = null;
                    string vs = v.ToString(3);
                    foreach (var a in root.GetProperty("assets").EnumerateArray())
                    {
                        string name = a.GetProperty("name").GetString() ?? "";
                        string url = a.GetProperty("browser_download_url").GetString();
                        if (name.Equals("PiPStudio-" + vs + "-win-x64-portable.zip", StringComparison.OrdinalIgnoreCase)) zipUrl = url;
                        else if (name.Equals("PiPStudio-Setup-" + vs + ".exe", StringComparison.OrdinalIgnoreCase)) setupUrl = url;
                        else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sumsUrl = url;
                    }
                    return v;
                }
            }
        }

        // ------------------------------------------------------------ installing
        /// <summary>Downloads and installs <see cref="Latest"/>, then restarts PiP Studio on it.</summary>
        public static async void Install()
        {
            if (!CanInstall) return;
            Percent = 0;
            Set(State.Downloading);
            try
            {
                if (sumsUrl == null) throw new UpdateException("This release can't be checked (it has no SHA256SUMS.txt). Download it from GitHub instead.");
                Dictionary<string, string> sums = ParseSums(await http.GetStringAsync(sumsUrl));
                Directory.CreateDirectory(TempDir);

                string exe = Environment.ProcessPath;
                if (!CanWrite(Path.GetDirectoryName(exe)))
                {
                    // installed for all users: let the installer do it (it asks for permission and closes PiP Studio)
                    if (setupUrl == null) throw new UpdateException("PiP Studio's folder can't be changed, and this release has no installer.");
                    string setup = await Download(setupUrl, sums);
                    Set(State.Installing);
                    App.I.SaveSession();
                    Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true });
                    Set(State.Available);
                    return;
                }

                if (zipUrl == null) throw new UpdateException("This release has no portable zip to update from.");
                string zip = await Download(zipUrl, sums);
                Set(State.Installing);
                string fresh = exe + ".new";
                await Task.Run(() => Extract(zip, fresh));
                Swap(exe, fresh);
                MarkInstalledVersion(Path.GetDirectoryName(exe), Latest.ToString(3));

                App.I.SaveSession();
                Process.Start(new ProcessStartInfo(exe, "--replace --updated") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
                App.I.ExitApp();
            }
            catch (Exception ex)
            {
                Error = Friendly(ex);
                Set(State.Failed);
            }
        }

        /// <summary>SHA256SUMS.txt: "hash  file name" per line (the format of sha256sum and the release workflow).</summary>
        static Dictionary<string, string> ParseSums(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                int sp = line.IndexOf(' ');
                if (sp != 64) continue;
                d[line.Substring(sp).Trim().TrimStart('*')] = line.Substring(0, 64).ToLowerInvariant();
            }
            return d;
        }

        /// <summary>Downloads to the temp folder, showing progress, and makes sure the file matches its SHA-256.</summary>
        static async Task<string> Download(string url, Dictionary<string, string> sums)
        {
            string name = Path.GetFileName(new Uri(url).LocalPath);
            string expected;
            if (!sums.TryGetValue(name, out expected)) throw new UpdateException(name + " isn't listed in the release's SHA256SUMS.txt.");
            string path = Path.Combine(TempDir, name);
            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? -1;
                using (var src = await resp.Content.ReadAsStreamAsync())
                using (var dst = File.Create(path))
                {
                    var buf = new byte[81920];
                    long got = 0;
                    int n, shown = -1;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n);
                        got += n;
                        int pct = total > 0 ? (int)(got * 100 / total) : 0;
                        if (pct != shown) { shown = pct; Percent = pct; Set(State.Downloading); }
                    }
                }
            }
            string actual = await Task.Run(() =>
            {
                using (var f = File.OpenRead(path)) return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
            });
            if (actual != expected)
            {
                try { File.Delete(path); } catch { }
                throw new UpdateException("The download was damaged (its fingerprint doesn't match). Please try again.");
            }
            return path;
        }

        static void Extract(string zip, string to)
        {
            using (var z = ZipFile.OpenRead(zip))
            {
                var e = z.Entries.FirstOrDefault(x => x.FullName.Equals("PiPStudio.exe", StringComparison.OrdinalIgnoreCase));
                if (e == null) throw new UpdateException("The update doesn't contain PiPStudio.exe.");
                e.ExtractToFile(to, true);
            }
            if (new FileInfo(to).Length < 1024 * 1024) throw new UpdateException("The update's PiPStudio.exe looks incomplete.");
        }

        /// <summary>Puts the new exe in place of the running one. The running exe can be renamed, just not replaced.</summary>
        static void Swap(string exe, string fresh)
        {
            string old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(fresh, exe); }
            catch
            {
                try { File.Move(old, exe); } catch { }   // put the current version back
                throw;
            }
        }

        /// <summary>Keeps Settings > Apps showing the right version when the installer put PiP Studio in this folder.</summary>
        static void MarkInstalledVersion(string dir, string version)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey, true))
                {
                    if (k == null) return;
                    string loc = (k.GetValue("InstallLocation") as string ?? "").TrimEnd('\\');
                    if (!loc.Equals(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                    k.SetValue("DisplayVersion", version);
                    k.SetValue("DisplayName", "PiP Studio version " + version);
                }
            }
            catch { }
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".pipstudio-write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        static string Friendly(Exception ex)
        {
            if (ex is UpdateException) return ex.Message;
            if (ex is HttpRequestException || ex is TaskCanceledException) return "Couldn't reach GitHub. Check your internet connection and try again.";
            if (ex is UnauthorizedAccessException || ex is IOException) return "Couldn't replace PiP Studio's files: " + ex.Message;
            return ex.Message;
        }

        class UpdateException : Exception { public UpdateException(string m) : base(m) { } }

        // ------------------------------------------------------------ text for the settings page and the tray
        /// <summary>One line describing where things stand, for the Updates card.</summary>
        public static string Status
        {
            get
            {
                switch (Now)
                {
                    case State.Checking: return "Checking for updates…";
                    case State.UpToDate: return "You're up to date (version " + CurrentText + ")";
                    case State.Available: return "Version " + Latest.ToString(3) + " is available";
                    case State.Downloading: return "Downloading the update… " + Percent + "%";
                    case State.Installing: return "Installing the update…";
                    case State.Failed: return Error ?? "The update didn't work";
                    default: return "You have version " + CurrentText;
                }
            }
        }

        /// <summary>A newer version was found and nothing is in progress (also after a failed try, to try again).</summary>
        public static bool CanInstall
        {
            get { return Supported && Latest != null && Latest > Current && (Now == State.Available || Now == State.Failed); }
        }

        /// <summary>Checking, downloading or installing right now.</summary>
        public static bool Busy { get { return Now == State.Checking || Now == State.Downloading || Now == State.Installing; } }

        public static void OpenReleasePage()
        {
            try { Process.Start(new ProcessStartInfo(ReleaseUrl ?? ReleasesPage) { UseShellExecute = true }); } catch { }
        }
    }
}
