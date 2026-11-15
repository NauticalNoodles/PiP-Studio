using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PipStudio
{
    /// <summary>
    /// What changes when PiP Studio runs as the Microsoft Store (MSIX) version instead of the installer or portable exe:
    /// - "Start when I sign in" uses the package's startup task (a Store app can't add itself to the registry Run key).
    /// - It can tell when Windows started it at sign-in, so it starts quietly in the tray.
    /// - Its %AppData% folder is redirected into the package's private storage, so "Settings folder" opens that.
    /// </summary>
    static class Packaged
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFamilyName(ref int length, StringBuilder name);

        const int APPMODEL_ERROR_NO_PACKAGE = 15700;
        /// <summary>Must match the TaskId in msix\AppxManifest.xml.</summary>
        public const string StartupTaskId = "PiPStudioStartup";

        static bool? packaged;

        /// <summary>True for the Microsoft Store version.</summary>
        public static bool IsPackaged
        {
            get
            {
                if (packaged == null)
                {
                    try { int len = 0; packaged = GetCurrentPackageFullName(ref len, null) != APPMODEL_ERROR_NO_PACKAGE; }
                    catch { packaged = false; }       // Windows without the package APIs: never packaged
                }
                return packaged.Value;
            }
        }

        static string FamilyName
        {
            get
            {
                try
                {
                    int len = 0;
                    GetCurrentPackageFamilyName(ref len, null);
                    if (len <= 0) return null;
                    var sb = new StringBuilder(len);
                    return GetCurrentPackageFamilyName(ref len, sb) == 0 ? sb.ToString() : null;
                }
                catch { return null; }
            }
        }

        static Windows.ApplicationModel.StartupTask Task()
        {
            return Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>"Start PiP Studio when I sign in" for the Store version.</summary>
        public static bool StartupEnabled
        {
            get
            {
                try
                {
                    var s = Task().State;
                    return s == Windows.ApplicationModel.StartupTaskState.Enabled || s == Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    var t = Task();
                    if (value) t.RequestEnableAsync().AsTask().GetAwaiter().GetResult();   // stays off if you turned it off in Task Manager
                    else t.Disable();
                }
                catch { }
            }
        }

        /// <summary>True when Windows started the Store version at sign-in (its startup task can't pass --startup).</summary>
        public static bool LaunchedAtStartup()
        {
            try
            {
                var a = Windows.ApplicationModel.AppInstance.GetActivatedEventArgs();
                return a != null && a.Kind == Windows.ApplicationModel.Activation.ActivationKind.StartupTask;
            }
            catch { return Environment.TickCount64 < 3 * 60 * 1000; }   // couldn't ask: a launch right after sign-in is most likely the startup task
        }

        /// <summary>Where a folder under %AppData% really is for the Store version (Windows keeps it inside the package's storage).</summary>
        public static string RealPath(string path)
        {
            if (!IsPackaged || string.IsNullOrEmpty(path)) return path;
            try
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string fam = FamilyName;
                if (fam == null || !path.StartsWith(roaming, StringComparison.OrdinalIgnoreCase)) return path;
                string rel = path.Substring(roaming.Length).TrimStart('\\');
                string redirected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", fam, "LocalCache", "Roaming", rel);
                return Directory.Exists(redirected) ? redirected : path;
            }
            catch { return path; }
        }
    }
}
