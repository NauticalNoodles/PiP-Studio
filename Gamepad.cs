using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace PipStudio
{
    // =================================================================================
    //  Game controllers: Xbox (XInput, including the Guide button), PlayStation,
    //  Switch and generic pads (Windows joystick API). Ported from UniversalPiP.
    //
    //  Unified button mask:
    //    0x1 D↑  0x2 D↓  0x4 D←  0x8 D→  0x10 Start  0x20 Back  0x40 L3  0x80 R3
    //    0x100 LB  0x200 RB  0x400 Guide  0x800 Touchpad/Capture
    //    0x1000 A  0x2000 B  0x4000 X  0x8000 Y   (by position: bottom, right, left, top)
    //    0x10000 LT  0x20000 RT   then the sticks as buttons:
    //    0x40000.. L-stick ↑↓←→   0x400000.. R-stick ↑↓←→
    // =================================================================================
    static class Pad
    {
        [StructLayout(LayoutKind.Sequential)]
        struct XSTATE { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }

        [DllImport("xinput1_4.dll")] static extern int XInputGetState(int idx, out XSTATE s);
        [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern int XInputGetStateEx(int idx, out XSTATE s);   // also reports the Guide button

        [StructLayout(LayoutKind.Sequential)]
        struct JOYINFOEX { public uint dwSize, dwFlags, dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos, dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct JOYCAPS
        {
            public ushort wMid, wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax, wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
        }

        [DllImport("winmm.dll")] static extern uint joyGetPosEx(uint id, ref JOYINFOEX info);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern uint joyGetDevCapsW(IntPtr id, out JOYCAPS caps, uint size);

        public struct State { public int B; public byte LT, RT; public short LX, LY, RX, RY; public string Kind; }

        // Button names per family: Xbox / PlayStation / Nintendo
        static readonly (int m, string[] n)[] Names = {
            (1, new[] { "D↑", "D↑", "D↑" }), (2, new[] { "D↓", "D↓", "D↓" }), (4, new[] { "D←", "D←", "D←" }), (8, new[] { "D→", "D→", "D→" }),
            (0x10, new[] { "Menu", "Options", "+" }), (0x20, new[] { "View", "Share", "−" }), (0x40, new[] { "L3", "L3", "L3" }), (0x80, new[] { "R3", "R3", "R3" }),
            (0x100, new[] { "LB", "L1", "L" }), (0x200, new[] { "RB", "R1", "R" }),
            (0x400, new[] { "Guide", "PS", "Home" }), (0x800, new[] { "—", "Touchpad", "Capture" }),
            (0x10000, new[] { "LT", "L2", "ZL" }), (0x20000, new[] { "RT", "R2", "ZR" }),
            (0x40000, new[] { "L-Stick ↑", "L-Stick ↑", "L-Stick ↑" }), (0x80000, new[] { "L-Stick ↓", "L-Stick ↓", "L-Stick ↓" }),
            (0x100000, new[] { "L-Stick ←", "L-Stick ←", "L-Stick ←" }), (0x200000, new[] { "L-Stick →", "L-Stick →", "L-Stick →" }),
            (0x400000, new[] { "R-Stick ↑", "R-Stick ↑", "R-Stick ↑" }), (0x800000, new[] { "R-Stick ↓", "R-Stick ↓", "R-Stick ↓" }),
            (0x1000000, new[] { "R-Stick ←", "R-Stick ←", "R-Stick ←" }), (0x2000000, new[] { "R-Stick →", "R-Stick →", "R-Stick →" }),
            (0x1000, new[] { "A", "✕", "B" }), (0x2000, new[] { "B", "○", "A" }), (0x4000, new[] { "X", "□", "Y" }), (0x8000, new[] { "Y", "△", "X" }) };

        // DirectInput button index -> unified mask (0x10000 / 0x20000 = triggers)
        static readonly int[] PsMap = { 0x4000, 0x1000, 0x2000, 0x8000, 0x100, 0x200, 0x10000, 0x20000, 0x20, 0x10, 0x40, 0x80, 0x400, 0x800 };
        static readonly int[] NinMap = { 0x1000, 0x2000, 0x4000, 0x8000, 0x100, 0x200, 0x10000, 0x20000, 0x20, 0x10, 0x40, 0x80, 0x400, 0x800 };
        static readonly int[] GenMap = { 0x1000, 0x2000, 0x4000, 0x8000, 0x100, 0x200, 0x20, 0x10, 0x40, 0x80 };
        static readonly Dictionary<uint, string> joyKind = new Dictionary<uint, string>();
        static bool exBroken, xinputMissing;

        public static string Name(int mask, string kind)
        {
            if (mask == 0) return "Unbound";
            int col = kind == "ps" ? 1 : kind == "switch" ? 2 : 0;
            return string.Join(" + ", Names.Where(x => (mask & x.m) != 0).Select(x => x.n[col]));
        }

        public static string FamilyName(string kind)
        {
            return kind == "ps" ? "PlayStation controller" : kind == "switch" ? "Switch controller" : kind == "xbox" ? "Xbox controller" : "Controller";
        }

        static int StickBits(int x, int y, int up, int dn, int lf, int rt)
        {
            const int th = 16000;
            if (Math.Max(Math.Abs(x), Math.Abs(y)) < th) return 0;
            if (Math.Abs(y) >= Math.Abs(x)) return y > 0 ? up : dn;
            return x > 0 ? rt : lf;
        }

        static void AddVirtual(ref State s)
        {
            if (s.LT > 150) s.B |= 0x10000;
            if (s.RT > 150) s.B |= 0x20000;
            s.B |= StickBits(s.LX, s.LY, 0x40000, 0x80000, 0x100000, 0x200000);
            s.B |= StickBits(s.RX, s.RY, 0x400000, 0x800000, 0x1000000, 0x2000000);
        }

        static string lastKind = "xbox";
        // Polling an empty controller slot is slow (Microsoft says not to do it every frame, and the joystick API is worse).
        // Done 60 times a second that stalls the CPU and costs games frames, so empty slots are only re-checked every 2 seconds.
        static readonly bool[] xAlive = new bool[4];
        static readonly HashSet<uint> joyAlive = new HashSet<uint>();
        static DateTime nextScan = DateTime.MinValue;

        /// <summary>
        /// Reads every connected controller and merges them, so any of them can drive the PiP (an Xbox pad and a DualShock
        /// at the same time, say). Buttons are combined, each stick takes whichever controller pushes it furthest, and the
        /// button names follow the controller you used last.
        /// </summary>
        public static bool Read(out State s)
        {
            s = default(State);
            bool any = false, anyXInput = false;
            string activeKind = null;
            var now = DateTime.UtcNow;
            bool scan = now >= nextScan;
            if (scan) nextScan = now.AddSeconds(2);

            if (!xinputMissing)
            {
                try
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (!xAlive[i] && !scan) continue;                 // empty slot: only look again on the next scan
                        XSTATE x;
                        int xr;
                        if (!exBroken) { try { xr = XInputGetStateEx(i, out x); } catch (EntryPointNotFoundException) { exBroken = true; xr = XInputGetState(i, out x); } }
                        else xr = XInputGetState(i, out x);
                        xAlive[i] = xr == 0;
                        if (xr != 0) continue;
                        any = anyXInput = true;
                        var one = new State { B = x.Buttons, LT = x.LT, RT = x.RT, LX = x.LX, LY = x.LY, RX = x.RX, RY = x.RY, Kind = "xbox" };
                        AddVirtual(ref one);
                        if (one.B != 0 && activeKind == null) activeKind = "xbox";
                        Merge(ref s, one);
                    }
                }
                catch (DllNotFoundException) { xinputMissing = true; }
                catch { }
            }

            try
            {
                for (uint id = 0; id < 8; id++)
                {
                    if (!joyAlive.Contains(id) && !scan) continue;
                    var ji = new JOYINFOEX { dwSize = 52, dwFlags = 0xFF };
                    if (joyGetPosEx(id, ref ji) != 0) { joyKind.Remove(id); joyAlive.Remove(id); continue; }
                    joyAlive.Add(id);
                    string kind;
                    if (!joyKind.TryGetValue(id, out kind))
                    {
                        kind = "generic";
                        JOYCAPS caps;
                        if (joyGetDevCapsW((IntPtr)id, out caps, (uint)Marshal.SizeOf(typeof(JOYCAPS))) == 0)
                            kind = caps.wMid == 0x054C ? "ps" : caps.wMid == 0x057E ? "switch" : caps.wMid == 0x045E ? "msxinput" : "generic";
                        joyKind[id] = kind;
                    }
                    // Xbox controllers also show up here; they're already read through XInput (with the right button layout)
                    if (kind == "msxinput" && anyXInput) continue;
                    if (kind == "msxinput") kind = "xbox";
                    any = true;
                    var one = new State();
                    int[] map = kind == "ps" ? PsMap : kind == "switch" ? NinMap : GenMap;
                    for (int bi = 0; bi < map.Length; bi++)
                    {
                        if ((ji.dwButtons & (1u << bi)) == 0) continue;
                        int mk = map[bi];
                        if (mk == 0x10000) one.LT = 255; else if (mk == 0x20000) one.RT = 255; else one.B |= mk;
                    }
                    uint pv = ji.dwPOV & 0xFFFF;
                    if (pv != 0xFFFF)
                    {
                        if (pv >= 31500 || pv <= 4500) one.B |= 1;
                        if (pv >= 4500 && pv <= 13500) one.B |= 8;
                        if (pv >= 13500 && pv <= 22500) one.B |= 2;
                        if (pv >= 22500 && pv <= 31500) one.B |= 4;
                    }
                    one.LX = (short)Math.Clamp((int)ji.dwXpos - 32768, -32768, 32767);
                    one.LY = (short)Math.Clamp(32768 - (int)ji.dwYpos, -32768, 32767);
                    one.RX = (short)Math.Clamp((int)ji.dwZpos - 32768, -32768, 32767);
                    one.RY = (short)Math.Clamp(32768 - (int)ji.dwRpos, -32768, 32767);
                    one.Kind = kind;
                    AddVirtual(ref one);
                    if (one.B != 0 && activeKind == null) activeKind = kind;
                    Merge(ref s, one);
                }
            }
            catch { }

            if (activeKind != null) lastKind = activeKind;
            s.Kind = lastKind;
            return any;
        }

        static void Merge(ref State into, State one)
        {
            into.B |= one.B;
            into.LT = Math.Max(into.LT, one.LT);
            into.RT = Math.Max(into.RT, one.RT);
            if (Math.Abs((int)one.LX) + Math.Abs((int)one.LY) > Math.Abs((int)into.LX) + Math.Abs((int)into.LY)) { into.LX = one.LX; into.LY = one.LY; }
            if (Math.Abs((int)one.RX) + Math.Abs((int)one.RY) > Math.Abs((int)into.RX) + Math.Abs((int)into.RY)) { into.RX = one.RX; into.RY = one.RY; }
        }
    }
}
