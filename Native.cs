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
    //  Win32 interop
    // =================================================================================
    static class Native
    {
        public const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
        public const int WS_CHILD = 0x40000000;
        public const uint GW_OWNER = 4;
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                         WS_EX_APPWINDOW = 0x40000, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
        public const int WM_NCHITTEST = 0x84, WM_MOUSEACTIVATE = 0x21, WM_NCLBUTTONDOWN = 0xA1,
                         WM_NCLBUTTONDBLCLK = 0xA3, WM_NCRBUTTONDOWN = 0xA4, WM_NCRBUTTONUP = 0xA5,
                         WM_NCMBUTTONDOWN = 0xA7, WM_NCMBUTTONUP = 0xA8, WM_SIZING = 0x214,
                         WM_ENTERSIZEMOVE = 0x231, WM_EXITSIZEMOVE = 0x232, WM_MOUSEWHEEL = 0x20A,
                         WM_DPICHANGED = 0x2E0, WM_HOTKEY = 0x312;
        public const int HTTRANSPARENT = -1, HTCLIENT = 1, HTCAPTION = 2, MA_NOACTIVATE = 3;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);
        public const int DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_RECTSOURCE = 0x2, DWM_TNP_OPACITY = 0x4,
                         DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;
        public const int SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
            public static RECT From(Rectangle r) { return new RECT(r.Left, r.Top, r.Right, r.Bottom); }
            public Rectangle ToRectangle() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length, flags, showCmd;
            public POINT ptMinPosition, ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
            public int xHotspot, yHotspot;
            public IntPtr hbmMask, hbmColor;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int val);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr h, bool altTab);

        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
        [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int cx, int cy, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, IntPtr l, int flags, int timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr64(IntPtr h, int idx);
        [DllImport("user32.dll", EntryPoint = "GetClassLongW")] static extern int GetClassLong32(IntPtr h, int idx);
        public static IntPtr GetClassLongPtr(IntPtr h, int idx)
        {
            return IntPtr.Size == 8 ? GetClassLongPtr64(h, idx) : new IntPtr(GetClassLong32(h, idx));
        }
        [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint x, out uint y);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

        [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr prev);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);

        [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);
        [DllImport("dwmapi.dll")] public static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    }

    // =================================================================================
    //  Per-app audio (Windows Core Audio session API)
    // =================================================================================
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr a, int b, out IntPtr c);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr a, int b, out IntPtr c);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr a, IntPtr b);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr a, IntPtr b);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(IntPtr a, IntPtr b);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr a);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr a);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    static class Audio
    {
        static List<ISimpleAudioVolume> Sessions(string processName)
        {
            var list = new List<ISimpleAudioVolume>();
            if (string.IsNullOrEmpty(processName)) return list;
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return list;
                Guid iid = typeof(IAudioSessionManager2).GUID;
                object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null) return list;
                var mgr = (IAudioSessionManager2)o;
                IAudioSessionEnumerator se;
                if (mgr.GetSessionEnumerator(out se) != 0 || se == null) return list;
                int n;
                se.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    object so;
                    if (se.GetSession(i, out so) != 0 || so == null) continue;
                    var c2 = so as IAudioSessionControl2;
                    if (c2 == null) continue;
                    uint pid;
                    c2.GetProcessId(out pid);
                    if (pid == 0) continue;
                    if (!string.Equals(U.ProcessName((int)pid), processName, StringComparison.OrdinalIgnoreCase)) continue;
                    var v = so as ISimpleAudioVolume;
                    if (v != null) list.Add(v);
                }
            }
            catch { }
            return list;
        }

        public static bool? GetMute(string processName)
        {
            var s = Sessions(processName);
            if (s.Count == 0) return null;
            bool m;
            try { s[0].GetMute(out m); return m; } catch { return null; }
        }

        public static void SetMute(string processName, bool mute)
        {
            Guid g = Guid.Empty;
            foreach (var v in Sessions(processName)) { try { v.SetMute(mute, ref g); } catch { } }
        }

        public static float? GetVolume(string processName)
        {
            var s = Sessions(processName);
            if (s.Count == 0) return null;
            float f;
            try { s[0].GetMasterVolume(out f); return f; } catch { return null; }
        }

        public static void SetVolume(string processName, float level)
        {
            Guid g = Guid.Empty;
            level = Math.Max(0f, Math.Min(1f, level));
            foreach (var v in Sessions(processName)) { try { v.SetMasterVolume(level, ref g); } catch { } }
        }

        // ------------------------------------------------------------ the mixer (phone companion): every app with sound
        /// <summary>An app in Windows' volume mixer: all its sessions on the default output, under one process name.</summary>
        public class AppVolume
        {
            public string Process, Name;
            public float Volume;
            public bool Muted, Playing;
        }

        static readonly Dictionary<string, string> niceNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Apps with an audio session on the default output, like Windows' volume mixer lists them.</summary>
        public static List<AppVolume> Apps()
        {
            var byName = new Dictionary<string, AppVolume>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return new List<AppVolume>();
                Guid iid = typeof(IAudioSessionManager2).GUID;
                object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0 || o == null) return new List<AppVolume>();
                IAudioSessionEnumerator se;
                if (((IAudioSessionManager2)o).GetSessionEnumerator(out se) != 0 || se == null) return new List<AppVolume>();
                int n;
                se.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    object so;
                    if (se.GetSession(i, out so) != 0 || so == null) continue;
                    var c2 = so as IAudioSessionControl2;
                    var v = so as ISimpleAudioVolume;
                    if (c2 == null || v == null) continue;
                    uint pid;
                    c2.GetProcessId(out pid);
                    if (pid == 0) continue;                                        // Windows' system sounds
                    string proc = U.ProcessName((int)pid);
                    if (string.IsNullOrEmpty(proc)) continue;                      // the app has closed
                    int state;
                    c2.GetState(out state);
                    if (state == 2) continue;                                      // AudioSessionStateExpired
                    AppVolume a;
                    if (!byName.TryGetValue(proc, out a))
                    {
                        float f; bool m;
                        v.GetMasterVolume(out f);
                        v.GetMute(out m);
                        a = new AppVolume { Process = proc, Name = NiceName(proc, (int)pid), Volume = f, Muted = m };
                        byName[proc] = a;
                    }
                    if (state == 1) a.Playing = true;                              // AudioSessionStateActive
                }
            }
            catch { }
            var l = new List<AppVolume>(byName.Values);
            l.Sort((x, y) => x.Playing != y.Playing ? (x.Playing ? -1 : 1) : string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase));
            return l;
        }

        /// <summary>The app's own name ("Google Chrome", "Spotify") from its program file, or its process name.</summary>
        static string NiceName(string proc, int pid)
        {
            string n;
            if (niceNames.TryGetValue(proc, out n)) return n;
            try
            {
                using (var p = System.Diagnostics.Process.GetProcessById(pid))
                    n = p.MainModule.FileVersionInfo.FileDescription;
            }
            catch { n = null; }                                                    // e.g. an app running as administrator
            if (string.IsNullOrWhiteSpace(n)) n = char.ToUpper(proc[0]) + proc.Substring(1);
            niceNames[proc] = n.Trim();
            return niceNames[proc];
        }
    }

}
