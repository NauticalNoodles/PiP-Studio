using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

namespace PipStudio
{
    /// <summary>
    /// "Always start as administrator" (Settings > General > Administrator). Some games run as administrator, and only a
    /// PiP Studio that does too can take their focus or show their windows.
    /// Turning it on creates a scheduled task that runs PiP Studio with the highest rights (one UAC prompt). From then on
    /// every normal start (shortcut, sign-in, anything) hands over to that task and quits, so PiP Studio comes back as
    /// administrator without asking. The start's own arguments (--startup for sign-in) travel along in a small file, so a
    /// start at sign-in still starts quietly in the tray. Turning it off deletes the task (one more UAC prompt).
    /// Not for the Store version, which can't run as administrator.
    /// </summary>
    static class AdminStart
    {
        public const string TaskName = "PiP Studio (administrator)";
        static string Handover { get { return Path.Combine(Settings.Dir, "admin-start.txt"); } }

        /// <summary>At the very start: when it's on and this copy isn't elevated, start the elevated one and say so (true).</summary>
        public static bool HandOver(string[] args)
        {
            if (Packaged.IsPackaged || App.IsElevated) return false;
            try
            {
                var s = Settings.Load();
                if (!s.AlwaysAdmin) return false;
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllText(Handover, DateTime.UtcNow.Ticks + "\n" + string.Join("\n", args));
                if (Schtasks("/Run /TN \"" + TaskName + "\"", false) == 0) return true;
                File.Delete(Handover);                                    // the task is gone: just start normally
            }
            catch { }
            return false;
        }

        /// <summary>In the elevated copy the task started: the arguments the normal start handed over (if it's fresh).</summary>
        public static string[] TakeArgs(string[] args)
        {
            if (!App.IsElevated || !File.Exists(Handover)) return args;
            try
            {
                var lines = File.ReadAllLines(Handover);
                File.Delete(Handover);
                long ticks;
                if (lines.Length == 0 || !long.TryParse(lines[0], out ticks) || (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds > 30) return args;
                return args.Concat(lines.Skip(1).Where(x => x.Length > 0)).Distinct().ToArray();
            }
            catch { return args; }
        }

        public static bool TaskExists() { return !Packaged.IsPackaged && Schtasks("/Query /TN \"" + TaskName + "\"", false) == 0; }

        /// <summary>Creates the task (asks for permission). False when you said no or it didn't work.</summary>
        public static bool Create()
        {
            string xml = Path.Combine(Path.GetTempPath(), "PiPStudio-admin-task.xml");
            try
            {
                string user = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                File.WriteAllText(xml, TaskXml(Environment.ProcessPath, user), Encoding.Unicode);   // schtasks wants UTF-16
                return Schtasks("/Create /TN \"" + TaskName + "\" /XML \"" + xml + "\" /F", true) == 0;
            }
            catch { return false; }
            finally { try { File.Delete(xml); } catch { } }
        }

        /// <summary>Deletes the task (asks for permission). False when you said no.</summary>
        public static bool Delete()
        {
            if (!TaskExists()) return true;
            return Schtasks("/Delete /TN \"" + TaskName + "\" /F", !App.IsElevated) == 0;
        }

        /// <summary>Starts PiP Studio again through the task (elevated, no prompt) and quits this copy.</summary>
        public static void RestartThroughTask()
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllText(Handover, DateTime.UtcNow.Ticks + "\n--replace");   // the new copy waits for this one to quit
                App.I.SaveSession();
                if (Schtasks("/Run /TN \"" + TaskName + "\"", false) == 0) { App.I.ExitApp(); return; }
            }
            catch { }
            App.I.RestartElevated();                                      // the task didn't run: the usual way
        }

        static int Schtasks(string arguments, bool elevate)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", arguments) { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                if (elevate) { psi.UseShellExecute = true; psi.Verb = "runas"; }
                else psi.UseShellExecute = false;
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(15000)) return -1;
                    return p.ExitCode;
                }
            }
            catch { return -1; }                                          // e.g. you said no to the UAC prompt
        }

        /// <summary>The task: run PiP Studio as you, with the highest rights, on demand only, no time limit, on battery too.
        /// Parallel, so a second start reaches the running copy (which then shows its window) instead of being ignored.</summary>
        static string TaskXml(string exe, string userSid)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n"
                + "<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
                + "  <RegistrationInfo><Description>Starts PiP Studio as administrator without asking each time. Turn off \"Always start as administrator\" in PiP Studio to remove it.</Description></RegistrationInfo>\n"
                + "  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(userSid) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n"
                + "  <Settings>\n"
                + "    <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>\n"
                + "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n"
                + "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit><AllowStartOnDemand>true</AllowStartOnDemand><Priority>5</Priority>\n"
                + "  </Settings>\n"
                + "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(exe) + "</Command><Arguments>--from-task</Arguments></Exec></Actions>\n"
                + "</Task>\n";
        }
    }
}
