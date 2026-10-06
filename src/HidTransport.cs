using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RazerHelper
{
    public sealed class HidEndpoint
    {
        public string Path { get; set; }
        public int Vendor { get; set; }
        public int Product { get; set; }
        public int UsagePage { get; set; }
        public int Usage { get; set; }
        public int FeatureLength { get; set; }
        public int InputLength { get; set; }
        public override string ToString() { return String.Format("{0:X4}:{1:X4} usage {2:X4}:{3:X4} feature={4}", Vendor, Product, UsagePage, Usage, FeatureLength); }
    }

    public static class HidDiscovery
    {
        public static string Model()
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                return key == null ? "Unknown" : Convert.ToString(key.GetValue("SystemProductName", "Unknown"));
        }
        public static List<HidEndpoint> Enumerate()
        {
            Guid guid; Native.HidD_GetHidGuid(out guid);
            IntPtr set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1)) throw new Win32Exception();
            var found = new List<HidEndpoint>();
            try
            {
                for (uint index = 0; index < 512; index++)
                {
                    var info = new Native.DeviceInterface(); info.Size = Marshal.SizeOf(info);
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref info))
                    {
                        if (Marshal.GetLastWin32Error() == 259) break;
                        throw new Win32Exception();
                    }
                    uint size;
                    Native.SetupDiGetDeviceInterfaceDetail(set, ref info, IntPtr.Zero, 0, out size, IntPtr.Zero);
                    if (size < 8 || size > 65536) continue;
                    IntPtr buffer = Marshal.AllocHGlobal((int)size);
                    string path;
                    try
                    {
                        Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref info, buffer, size, out size, IntPtr.Zero)) continue;
                        path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                    if (path == null || path.IndexOf("vid_1532", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    using (SafeFileHandle h = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                    {
                        if (h.IsInvalid) continue;
                        var a = new Native.HidAttributes(); a.Size = Marshal.SizeOf(a);
                        if (!Native.HidD_GetAttributes(h, ref a) || a.Vendor != 0x1532) continue;
                        IntPtr pp;
                        if (!Native.HidD_GetPreparsedData(h, out pp)) continue;
                        try
                        {
                            Native.HidCaps caps;
                            if (Native.HidP_GetCaps(pp, out caps) >= 0)
                                found.Add(new HidEndpoint { Path = path, Vendor = a.Vendor, Product = a.Product, UsagePage = caps.UsagePage, Usage = caps.Usage, FeatureLength = caps.FeatureLength, InputLength = caps.InputLength });
                        }
                        finally { Native.HidD_FreePreparsedData(pp); }
                    }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return found;
        }
        public static bool IsSupportedModel(string model) { return model != null && model.IndexOf("RZ09-0530", StringComparison.OrdinalIgnoreCase) >= 0; }
    }

    public interface IReportTransport : IDisposable { byte[] Exchange(byte[] request); }

    internal sealed class HidTransport : IReportTransport
    {
        private readonly SafeFileHandle handle;
        private readonly object sync = new object();
        public HidTransport(HidEndpoint endpoint)
        {
            if (endpoint.Vendor != 0x1532 || endpoint.Product != 0x02c5 || endpoint.FeatureLength != 91 ||
                endpoint.Path.IndexOf("&mi_02", StringComparison.OrdinalIgnoreCase) < 0)
                throw new NotSupportedException("仅允许已识别的 Blade 14 2025 控制接口。");
            handle = Native.CreateFile(endpoint.Path, 0xc0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                handle = Native.CreateFile(endpoint.Path, 0, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开键盘控制接口"); }
            }
        }
        private byte[] Feature(uint code, byte[] bytes)
        {
            IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
            IntPtr ov = Marshal.AllocHGlobal(IntPtr.Size * 3 + 8);
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset))
            {
                try
                {
                    Marshal.Copy(bytes, 0, buffer, bytes.Length);
                    for (int i = 0; i < IntPtr.Size * 3 + 8; i++) Marshal.WriteByte(ov, i, 0);
                    Marshal.WriteIntPtr(ov, IntPtr.Size * 2 + 8, ready.SafeWaitHandle.DangerousGetHandle());
                    uint returned;
                    bool ok = Native.DeviceIoControl(handle, code, buffer, (uint)bytes.Length, buffer, (uint)bytes.Length, out returned, ov);
                    if (!ok)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != 997) throw new Win32Exception(error, "HID feature 操作失败");
                        if (!ready.WaitOne(1500))
                        {
                            Native.CancelIoEx(handle, ov);
                            // The kernel must release both buffers before they can be freed.
                            Native.GetOverlappedResult(handle, ov, out returned, true);
                            throw new TimeoutException("硬件控制超时，已取消请求。");
                        }
                        if (!Native.GetOverlappedResult(handle, ov, out returned, false)) throw new Win32Exception();
                    }
                    // Windows may omit the zero report-ID byte from BytesReturned.
                    if (code == 0xb0192 && returned < bytes.Length - 1) throw new InvalidOperationException("HID 响应长度不足：" + returned);
                    var result = new byte[bytes.Length]; Marshal.Copy(buffer, result, 0, result.Length); return result;
                }
                finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(ov); }
            }
        }
        public byte[] Exchange(byte[] request)
        {
            if (request.Length != 91) throw new ArgumentException("Report must be 91 bytes.");
            lock (sync)
            {
                Feature(0xb0191, request);
                byte[] response = null;
                for (int poll = 0; poll < 5; poll++)
                {
                    Thread.Sleep(poll == 0 ? 4 : 8);
                    response = Feature(0xb0192, new byte[91]);
                    if (response[2] == request[2] && response[7] == request[7] && response[8] == request[8] && response[1] != 0 && response[1] != 1) return response;
                }
                return response;
            }
        }
        public void Dispose() { handle.Dispose(); }
    }
}
