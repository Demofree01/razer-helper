using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace RazerHelper
{
    public sealed class PowerInfo
    {
        public bool? OnAC { get; set; }
        public int? BatteryPercent { get; set; }
        public string Plan { get; set; }
        public string Mode { get; set; }
    }
    public static class WindowsPower
    {
        public static readonly Guid Efficiency = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        public static readonly Guid Balanced = Guid.Empty;
        public static readonly Guid Performance = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");
        public static PowerInfo Read()
        {
            Native.PowerStatus s;
            var p = new PowerInfo();
            if (Native.GetSystemPowerStatus(out s))
            {
                p.OnAC = s.ACLineStatus == 255 ? (bool?)null : s.ACLineStatus == 1;
                p.BatteryPercent = s.BatteryLifePercent <= 100 ? (int?)s.BatteryLifePercent : null;
            }
            IntPtr ptr;
            if (Native.PowerGetActiveScheme(IntPtr.Zero, out ptr) == 0)
            {
                try { p.Plan = ((Guid)Marshal.PtrToStructure(ptr, typeof(Guid))).ToString(); }
                finally { Native.LocalFree(ptr); }
            }
            try { p.Mode = Overlay().ToString(); }
            catch (EntryPointNotFoundException) { p.Mode = null; }
            return p;
        }
        public static Guid Overlay()
        {
            Guid guid;
            uint result = Native.PowerGetActualOverlayScheme(out guid);
            if (result != 0) throw new Win32Exception((int)result, "无法读取 Windows 电源模式");
            return guid;
        }
        public static Guid ForName(string name)
        {
            switch (name) { case "Efficiency": return Efficiency; case "Balanced": return Balanced; case "Performance": return Performance; default: throw new ArgumentException("未知 Windows 电源模式"); }
        }
        public static void Set(string name) { Set(ForName(name)); }
        public static void Set(Guid guid)
        {
            if (guid != Efficiency && guid != Balanced && guid != Performance) throw new ArgumentException("电源模式不在允许列表中。");
            uint result = Native.PowerSetActiveOverlayScheme(guid);
            if (result != 0) throw new Win32Exception((int)result, "Windows 拒绝切换电源模式");
            if (Overlay() != guid) throw new InvalidOperationException("Windows 电源模式未通过回读验证。");
        }
        public static string Label(string value)
        {
            if (value == Efficiency.ToString()) return "最佳能效";
            if (value == Performance.ToString()) return "最佳性能";
            if (value == Balanced.ToString()) return "平衡";
            return "系统自定义";
        }
    }

    public sealed class DisplayInfo
    {
        public string Name { get; set; }
        public bool Internal { get; set; }
        public bool ClonedWithExternal { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Hz { get; set; }
        public int[] Rates { get; set; }
        public override string ToString() { return String.Format("{0} · {1} × {2} · {3} Hz", Internal ? "内置屏幕" : "外接屏幕", Width, Height, Hz); }
    }
    public static class WindowsDisplay
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] internal struct Source { public Luid Adapter; public uint Id, Mode, Status; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rational { public uint Numerator, Denominator; }
        [StructLayout(LayoutKind.Sequential)] internal struct Target
        {
            public Luid Adapter; public uint Id, Mode, Technology, Rotation, Scaling;
            public Rational Refresh; public uint ScanLine; public int Available; public uint Status;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct PathInfo { public Source Source; public Target Target; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)] internal struct InfoHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct SourceName
        {
            public InfoHeader Header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            public ushort SpecVersion, DriverVersion, Size, DriverExtra;
            public uint Fields;
            public int X, Y;
            public uint Orientation, FixedOutput;
            public short Color, Duplex, YResolution, TTOption, Collate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
            public ushort LogPixels;
            public uint Bits, Width, Height, DisplayFlags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
        }
        [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [In, Out] PathInfo[] pathInfo, ref uint modes, IntPtr modeInfo, IntPtr topology);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DisplayConfigGetDeviceInfo(ref SourceName name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string name, int number, ref DevMode mode);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string name, ref DevMode mode, IntPtr hwnd, uint flags, IntPtr param);

        private static DevMode Empty() { var mode = new DevMode(); mode.Size = (ushort)Marshal.SizeOf(mode); return mode; }
        internal static DevMode Current(string name)
        {
            var mode = Empty();
            if (!EnumDisplaySettings(name, -1, ref mode)) throw new InvalidOperationException("屏幕已断开或当前模式不可读。");
            return mode;
        }
        internal static bool InternalTechnology(uint technology) { return technology == 0x80000000 || technology == 11 || technology == 13 || technology == 6; }
        public static List<DisplayInfo> Read()
        {
            for (int retry = 0; retry < 3; retry++)
            {
                uint pc, mc; int result = GetDisplayConfigBufferSizes(2, out pc, out mc);
                if (result != 0) throw new Win32Exception(result);
                if (pc > 64 || mc > 512) throw new InvalidOperationException("异常的显示拓扑大小。");
                var paths = new PathInfo[pc]; IntPtr modes = Marshal.AllocHGlobal((int)Math.Max(mc, 1) * 64);
                try { result = QueryDisplayConfig(2, ref pc, paths, ref mc, modes, IntPtr.Zero); }
                finally { Marshal.FreeHGlobal(modes); }
                if (result == 122) continue;
                if (result != 0) throw new Win32Exception(result);
                var displays = new List<DisplayInfo>();
                foreach (var group in paths.Take((int)pc).GroupBy(x => x.Source.Adapter.High + ":" + x.Source.Adapter.Low + ":" + x.Source.Id))
                {
                    var first = group.First();
                    var sn = new SourceName(); sn.Header.Type = 1; sn.Header.Size = (uint)Marshal.SizeOf(sn); sn.Header.Adapter = first.Source.Adapter; sn.Header.Id = first.Source.Id;
                    if (DisplayConfigGetDeviceInfo(ref sn) != 0 || String.IsNullOrEmpty(sn.Name)) continue;
                    var current = Current(sn.Name);
                    var rates = new HashSet<int>();
                    for (int index = 0; index < 4096; index++)
                    {
                        var mode = Empty(); if (!EnumDisplaySettings(sn.Name, index, ref mode)) break;
                        if (SameGeometry(current, mode) && mode.Frequency >= 24 && mode.Frequency <= 500) rates.Add((int)mode.Frequency);
                    }
                    bool internalAny = group.Any(x => InternalTechnology(x.Target.Technology));
                    bool externalAny = group.Any(x => !InternalTechnology(x.Target.Technology));
                    displays.Add(new DisplayInfo { Name = sn.Name, Internal = internalAny && !externalAny, ClonedWithExternal = internalAny && externalAny, Width = (int)current.Width, Height = (int)current.Height, Hz = (int)current.Frequency, Rates = rates.OrderBy(x => x).ToArray() });
                }
                return displays;
            }
            throw new InvalidOperationException("显示拓扑正在变化，请稍后重试。");
        }
        private static bool SameGeometry(DevMode a, DevMode b) { return a.Width == b.Width && a.Height == b.Height && a.Bits == b.Bits && a.Orientation == b.Orientation && a.DisplayFlags == b.DisplayFlags; }
        public static DisplayInfo BuiltIn()
        {
            var internalScreens = Read().Where(x => x.Internal).ToArray();
            if (internalScreens.Length != 1) throw new InvalidOperationException("无法唯一识别内置屏幕；复制屏幕模式下不会自动切换。");
            return internalScreens[0];
        }
        public static void ValidateRate(string name, int rate)
        {
            var display = BuiltIn();
            if (display.Name != name || !display.Rates.Contains(rate)) throw new ArgumentException("此刷新率不在当前内置屏幕同分辨率的受支持列表中。");
            var mode = Current(name); mode.Frequency = (uint)rate; mode.Fields = 0x400000;
            int result = ChangeDisplaySettingsEx(name, ref mode, IntPtr.Zero, 2, IntPtr.Zero);
            if (result != 0) throw new InvalidOperationException("显示驱动拒绝测试模式，代码 " + result);
        }
        public static void Set(string name, int rate)
        {
            ValidateRate(name, rate);
            var mode = Current(name); mode.Frequency = (uint)rate; mode.Fields = 0x400000;
            int result = ChangeDisplaySettingsEx(name, ref mode, IntPtr.Zero, 0, IntPtr.Zero);
            if (result != 0) throw new InvalidOperationException("刷新率切换失败，代码 " + result);
            if (Current(name).Frequency != rate) throw new InvalidOperationException("刷新率回读与请求不符。");
        }
        internal static bool RestoreIfUnchanged(string name, int original, int changed, int width, int height)
        {
            var builtIn = BuiltIn();
            if (builtIn.Name != name || builtIn.Hz != changed || builtIn.Width != width || builtIn.Height != height) return false;
            Set(name, original); return true;
        }
    }
}
