using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RazerHelper
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct DeviceInterface
        {
            public int Size;
            public Guid ClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct HidAttributes
        {
            public int Size;
            public ushort Vendor, Product, Version;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct HidCaps
        {
            public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort LinkNodes, InputButtons, InputValues, InputIndices;
            public ushort OutputButtons, OutputValues, OutputIndices;
            public ushort FeatureButtons, FeatureValues, FeatureIndices;
        }
        [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] internal static extern bool HidD_GetAttributes(SafeFileHandle h, ref HidAttributes attributes);
        [DllImport("hid.dll")] internal static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] internal static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);
        [DllImport("hid.dll", SetLastError = true)] internal static extern bool HidD_SetFeature(SafeFileHandle h, byte[] data, int length);
        [DllImport("hid.dll", SetLastError = true)] internal static extern bool HidD_GetFeature(SafeFileHandle h, byte[] data, int length);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr dev, ref Guid guid, uint index, ref DeviceInterface info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterface info, IntPtr detail, uint size, out uint required, IntPtr dev);
        [DllImport("setupapi.dll")] internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool DeviceIoControl(SafeFileHandle file, uint code, IntPtr input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out uint returned, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
        [DllImport("kernel32.dll")] internal static extern bool GetSystemPowerStatus(out PowerStatus status);
        [StructLayout(LayoutKind.Sequential)]
        internal struct PowerStatus
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public uint BatteryLifeTime, BatteryFullLifeTime;
        }
        [DllImport("kernel32.dll")] internal static extern IntPtr LocalFree(IntPtr data);
        [DllImport("powrprof.dll")] internal static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
        [DllImport("powrprof.dll")] internal static extern uint PowerGetActualOverlayScheme(out Guid guid);
        [DllImport("powrprof.dll")] internal static extern uint PowerSetActiveOverlayScheme(Guid guid);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr RegisterPowerSettingNotification(IntPtr window, ref Guid setting, uint flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnregisterPowerSettingNotification(IntPtr notification);
        [DllImport("wtsapi32.dll", SetLastError = true)] internal static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
        [DllImport("wtsapi32.dll", SetLastError = true)] internal static extern bool WTSUnRegisterSessionNotification(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetUserObjectInformation(IntPtr handle, int index, System.Text.StringBuilder info, uint length, out uint needed);
        [StructLayout(LayoutKind.Sequential)] internal struct LastInput { public uint Size, Time; }
        [DllImport("user32.dll")] internal static extern bool GetLastInputInfo(ref LastInput input);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code, uint type);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetKeyNameText(int parameter, System.Text.StringBuilder text, int size);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
        [StructLayout(LayoutKind.Sequential)]
        internal struct Input { public uint Type; public InputUnion Data; }
        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyboardInput Keyboard;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    }
}
