using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace PipStudio
{
    /// <summary>
    /// The processor's temperature and power and the PC's fan speeds, from LibreHardwareMonitor's sensor library. Only
    /// while PiP Studio runs as administrator: reading these sensors goes through the PawnIO driver, which Windows only
    /// lets administrators use (and which people install themselves, from pawnio.eu). Kept in a class of its own so the
    /// library isn't even loaded otherwise.
    /// </summary>
    static class HwSensors
    {
        static Computer pc;
        static bool tried;
        static double lastT = -10;
        static double cpuTemp = -1, cpuPower = -1;
        static List<Tuple<string, int>> fans = new List<Tuple<string, int>>();

        /// <summary>Whether the PawnIO driver is installed (the same check LibreHardwareMonitor makes).</summary>
        public static bool DriverInstalled
        {
            get
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
                        return k != null;
                }
                catch { return false; }
            }
        }

        /// <summary>Called on the system monitor's thread, about once a second; reads the sensors every 2 seconds.</summary>
        public static void Fill(SysStats.Snap n, double t)
        {
            n.SensorsShown = true;
            n.SensorsDriver = DriverInstalled;
            if (!tried)
            {
                tried = true;
                try
                {
                    pc = new Computer { IsCpuEnabled = true, IsMotherboardEnabled = true, IsControllerEnabled = true };
                    pc.Open();
                }
                catch (Exception ex) { pc = null; App.ReportError(ex); }
            }
            if (pc != null && t - lastT >= 2)
            {
                lastT = t;
                double temp = -1, coreMax = -1, power = -1;
                var f = new List<Tuple<string, int>>();
                try
                {
                    foreach (var hw in pc.Hardware) Visit(hw, ref temp, ref coreMax, ref power, f);
                }
                catch { }
                cpuTemp = temp > 0 ? temp : coreMax;
                cpuPower = power;
                fans = f;
            }
            if (cpuTemp > 0) n.CpuTempC = cpuTemp;                       // better than the motherboard's thermal zone
            n.CpuPowerW = cpuPower;
            n.Fans = fans;
        }

        static void Visit(IHardware hw, ref double temp, ref double coreMax, ref double power, List<Tuple<string, int>> fans)
        {
            hw.Update();
            foreach (var s in hw.Sensors)
            {
                if (s.Value == null || float.IsNaN(s.Value.Value)) continue;
                double v = s.Value.Value;
                string name = s.Name ?? "";
                if (hw.HardwareType == HardwareType.Cpu && s.SensorType == SensorType.Temperature && v > 0 && v < 130)
                {
                    // Intel: "CPU Package"; AMD: "Core (Tctl/Tdie)"
                    if (name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0) temp = Math.Max(temp, v);
                    else if (name.StartsWith("Core", StringComparison.OrdinalIgnoreCase) || name.StartsWith("CCD", StringComparison.OrdinalIgnoreCase)) coreMax = Math.Max(coreMax, v);
                }
                else if (hw.HardwareType == HardwareType.Cpu && s.SensorType == SensorType.Power && name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 && v > 0)
                    power = Math.Max(power, v);
                else if (s.SensorType == SensorType.Fan && v > 0 && v < 20000)
                    fans.Add(Tuple.Create(name, (int)Math.Round(v)));
            }
            foreach (var sub in hw.SubHardware) Visit(sub, ref temp, ref coreMax, ref power, fans);
        }

        /// <summary>Lets the driver go when the last System details PiP closes.</summary>
        public static void Close()
        {
            try { if (pc != null) pc.Close(); } catch { }
            pc = null; tried = false;
        }
    }
}
