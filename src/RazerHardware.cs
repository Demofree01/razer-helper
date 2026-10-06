using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RazerHelper
{
    public enum BladeMode : byte { Balanced = 0, Performance = 2, Custom = 4, Silent = 5, BatterySaver = 6 }
    public enum LightEffect { Off, Static, Breathing, Spectrum, Wave }
    public sealed class HardwareState
    {
        public int Brightness { get; set; }
        public int Mode { get; set; }
        public int FanMode { get; set; }
        public int Fan1Rpm { get; set; }
        public int Fan2Rpm { get; set; }
        public int? ChargeLimit { get; set; }
        public int? Effect { get; set; }
        public int DeviceMode { get; set; }
        public string Firmware { get; set; }
        public int? CpuLevel { get; set; }
        public int? GpuLevel { get; set; }
    }
    public sealed class RazerProtocol : IDisposable
    {
        private readonly IReportTransport transport;
        private readonly Mutex gate = new Mutex(false, @"Local\RazerHelper-1532-02C5-HID");
        private byte sequence = 0x20;
        public bool ReadOnly { get; private set; }
        public RazerProtocol(IReportTransport transport, bool readOnly) { this.transport = transport; ReadOnly = readOnly; }
        public static bool IsRead(ushort command) { return new ushort[] { 0x0081, 0x0084, 0x0383, 0x0e84, 0x0f82, 0x0d82, 0x0d88, 0x0d87, 0x0792 }.Contains(command); }
        public static void Validate(ushort command, byte[] args, bool readOnly)
        {
            if (args == null || args.Length > 80) throw new ArgumentException("Invalid argument length.");
            if (readOnly && !IsRead(command)) throw new InvalidOperationException("只读模式禁止写入硬件。");
            bool allowed = false;
            switch (command)
            {
                case 0x0081: case 0x0084: allowed = args.SequenceEqual(new byte[] { 0, 0 }); break;
                case 0x0383: allowed = args.SequenceEqual(new byte[] { 1, 5, 0 }); break;
                case 0x0e84: allowed = args.SequenceEqual(new byte[] { 1, 0 }); break;
                case 0x0f82: allowed = args.SequenceEqual(new byte[] { 1, 5, 0 }); break;
                case 0x0d82: allowed = args.Length == 4 && args[0] == 0 && (args[1] == 1 || args[1] == 2) && args[2] == 0 && args[3] == 0; break;
                case 0x0d88: allowed = args.Length == 3 && args[0] == 0 && (args[1] == 1 || args[1] == 2) && args[2] == 0; break;
                case 0x0d87: allowed = args.Length == 3 && args[0] == 0 && (args[1] == 1 || args[1] == 2) && args[2] == 0; break;
                case 0x0792: allowed = args.SequenceEqual(new byte[] { 0 }); break;
                case 0x0e04: allowed = args.Length == 2 && args[0] == 1; break;
                case 0x0d02: allowed = args.Length == 4 && args[0] == 1 && (args[1] == 1 || args[1] == 2) && BladePerformancePolicy.Known(args[2]) && args[3] == 0; break;
                case 0x0d07: allowed = args.Length == 3 && args[0] == 1 && ((args[1] == 1 && args[2] <= 3) || (args[1] == 2 && args[2] <= 2)); break;
                case 0x0712: allowed = args.Length == 1 && (args[0] == 0xd0 || args[0] == 0x50); break;
                case 0x0004: allowed = args.SequenceEqual(new byte[] { 0, 0 }); break;
                case 0x030a:
                    allowed = (args.Length == 1 && (args[0] == 0 || args[0] == 4)) ||
                        (args.Length == 2 && args[0] == 1 && (args[1] == 1 || args[1] == 2)) ||
                        args.SequenceEqual(new byte[] { 5, 0 }) ||
                        (args.Length == 4 && args[0] == 6) || (args.Length == 8 && args[0] == 3 && args[1] == 1);
                    break;
                case 0x030b:
                    allowed = args.Length == 70 && args[0] == 0xff && args[1] < 6 && args[2] == 0 && args[3] == 15 && args.Skip(52).All(x => x == 0);
                    break;
                // No raw passthrough, flash, voltage/undervolt, hyperboost, manual fan, or Driver-mode writes.
            }
            if (!allowed) throw new ArgumentException("硬件命令不在安全允许列表中：" + command.ToString("X4"));
        }
        public static byte[] Build(ushort command, byte[] args, byte transaction)
        {
            if (args == null || args.Length > 80) throw new ArgumentException("Invalid argument length.");
            var report = new byte[91]; report[2] = transaction; report[6] = (byte)args.Length; report[7] = (byte)(command >> 8); report[8] = (byte)command;
            Array.Copy(args, 0, report, 9, args.Length);
            for (int i = 3; i < 89; i++) report[89] ^= report[i];
            return report;
        }
        public static byte[] Parse(byte[] request, byte[] response)
        {
            if (response == null || response.Length != 91 || response[0] != 0 || response[5] != 0 || response[6] > 80) throw new InvalidOperationException("无效的 HID 响应。");
            if (response[2] != request[2] || response[7] != request[7] || response[8] != request[8]) throw new InvalidOperationException(String.Format("HID 响应被覆盖 (请求 {0:X2}{1:X2}/{2:X2}，收到 {3:X2}{4:X2}/{5:X2})。请退出雷云后重试。", request[7], request[8], request[2], response[7], response[8], response[2]));
            byte crc = 0; for (int i = 3; i < 89; i++) crc ^= response[i];
            if (crc != response[89]) throw new InvalidOperationException("HID 响应校验失败。");
            if (response[1] == 5) throw new NotSupportedException("固件不支持此功能。");
            if (response[1] != 2) throw new InvalidOperationException("固件状态 " + response[1] + "，未确认成功。");
            ushort command = (ushort)(request[7] * 256 + request[8]);
            if ((response[3] != request[3] || response[4] != request[4]) && command != 0x0792) throw new InvalidOperationException("不完整的硬件响应。");
            return response.Skip(9).Take(response[6]).ToArray();
        }
        public byte[] Send(ushort command, params byte[] args)
        {
            Validate(command, args, ReadOnly);
            bool locked = false;
            try
            {
                try { locked = gate.WaitOne(2500); } catch (AbandonedMutexException) { locked = true; }
                if (!locked) throw new TimeoutException("其他 Helper 请求正在使用硬件。");
                int attempts = IsRead(command) ? 3 : 1;
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    byte id = command == 0x030a || command == 0x030b || command == 0x0e04 ? (byte)0xff : ++sequence;
                    if (sequence > 0x7e) sequence = 0x20;
                    var request = Build(command, args, id);
                    try { return Parse(request, transport.Exchange(request)); }
                    catch (InvalidOperationException) { if (attempt + 1 >= attempts) throw; Thread.Sleep(4); }
                }
                throw new InvalidOperationException("未收到硬件响应。");
            }
            finally { if (locked) gate.ReleaseMutex(); }
        }
        public void Dispose() { transport.Dispose(); gate.Dispose(); }
    }

    public sealed partial class RazerHardware : IDisposable
    {
        private readonly RazerProtocol protocol;
        private readonly Func<bool?> powerSource;
        public RazerHardware(RazerProtocol protocol) : this(protocol, ReadPowerSource) { }
        public RazerHardware(RazerProtocol protocol, Func<bool?> powerSource) { this.protocol = protocol; this.powerSource = powerSource; }
        public static RazerHardware Open(bool readOnly)
        {
            if (!HidDiscovery.IsSupportedModel(HidDiscovery.Model())) throw new NotSupportedException("此版本仅为 Blade 14 2025 / RZ09-0530 / 1532:02C5 启用硬件控制。");
            var endpoint = HidDiscovery.Enumerate().FirstOrDefault(x => x.Product == 0x02c5 && x.FeatureLength == 91 && x.Path.IndexOf("&mi_02", StringComparison.OrdinalIgnoreCase) >= 0);
            if (endpoint == null) throw new InvalidOperationException("未找到内置键盘 HID 控制通道。");
            var hardware = new RazerHardware(new RazerProtocol(new HidTransport(endpoint), readOnly));
            try { hardware.protocol.Send(0x0081, 0, 0); return hardware; }
            catch { hardware.Dispose(); throw; }
        }
        private static void Size(byte[] data, int min) { if (data.Length < min) throw new InvalidOperationException("硬件数据长度不足。"); }
        public int ReadBrightness() { var data = protocol.Send(0x0e84, 1, 0); Size(data, 2); if (data[0] != 1) throw new InvalidOperationException("亮度区域不匹配。"); return data[1]; }
        public int KeepBacklightAwake()
        {
            // A brightness QUERY refreshes the firmware's idle fade without writing
            // brightness, changing device mode, or simulating user input.
            var data = protocol.Send(0x0383, 1, 5, 0); Size(data, 3);
            if (data[1] != 5) throw new InvalidOperationException("背光查询区域不匹配。");
            return data[2];
        }
        public byte[] ReadZone(int zone)
        {
            var data = protocol.Send(0x0d82, 0, (byte)zone, 0, 0); Size(data, 4);
            if (data[1] != zone) throw new InvalidOperationException("性能区域不匹配。"); return data;
        }
        public int ReadEffect() { var data = protocol.Send(0x0f82, 1, 5, 0); Size(data, 3); if (data[1] != 5) throw new InvalidOperationException("灯效区域不匹配。"); return data[2]; }
        public int ReadDeviceMode() { var data = protocol.Send(0x0084, 0, 0); Size(data, 1); return data[0]; }
        public HardwareState Read()
        {
            var firmware = protocol.Send(0x0081, 0, 0); Size(firmware, 2);
            var zones = ReadConsistentZones(); var first = zones[0];
            var state = new HardwareState { Brightness = ReadBrightness(), Mode = first[2], FanMode = first[3], Firmware = firmware[0] + "." + firmware[1] };
            var mode = protocol.Send(0x0084, 0, 0); Size(mode, 1); state.DeviceMode = mode[0];
            try { state.Effect = ReadEffect(); } catch (NotSupportedException) { }
            try { var care = protocol.Send(0x0792, 0); Size(care, 1); state.ChargeLimit = care[0] == 0xd0 ? 80 : care[0] == 0x50 ? (int?)100 : null; } catch (NotSupportedException) { }
            try { var rpm = protocol.Send(0x0d88, 0, 1, 0); Size(rpm, 3); if (rpm[1] == 1 && rpm[2] <= 100) state.Fan1Rpm = rpm[2] * 100; } catch (NotSupportedException) { }
            try { var rpm = protocol.Send(0x0d88, 0, 2, 0); Size(rpm, 3); if (rpm[1] == 2 && rpm[2] <= 100) state.Fan2Rpm = rpm[2] * 100; } catch (NotSupportedException) { }
            try { state.CpuLevel = ReadLevel(1); state.GpuLevel = ReadLevel(2); } catch (NotSupportedException) { }
            return state;
        }
        public void SetBrightness(int raw)
        {
            if (raw < 0 || raw > 255) throw new ArgumentOutOfRangeException("raw");
            int before = ReadBrightness();
            if (before == raw) return;
            try { protocol.Send(0x0e04, 1, (byte)raw); if (ReadBrightness() != raw) throw new InvalidOperationException("亮度回读失败，可能被雷云覆盖。"); }
            catch { try { protocol.Send(0x0e04, 1, (byte)before); } catch { } throw; }
        }
        public void SetMode(BladeMode mode)
        {
            ChangePerformance(mode, null, null);
        }
        public void SetEffect(LightEffect effect, byte r, byte g, byte b, int direction)
        {
            bool volatileFrame = effect == LightEffect.Static && ReadDeviceMode() == 0;
            switch (effect)
            {
                case LightEffect.Off: protocol.Send(0x030a, 0); break;
                case LightEffect.Static:
                    if (!volatileFrame) { protocol.Send(0x030a, 6, r, g, b); break; }
                    // Native-mode solid RGB uses the documented 6×16 volatile matrix,
                    // avoiding Driver mode and persistent frame storage.
                    for (byte row = 0; row < 6; row++)
                    {
                        var pixels = new byte[70]; pixels[0] = 0xff; pixels[1] = row; pixels[3] = 15;
                        for (int column = 0; column < 16; column++) { pixels[4 + column * 3] = r; pixels[5 + column * 3] = g; pixels[6 + column * 3] = b; }
                        protocol.Send(0x030b, pixels);
                    }
                    protocol.Send(0x030a, 5, 0); break;
                case LightEffect.Breathing: protocol.Send(0x030a, 3, 1, r, g, b, 0, 0, 0); break;
                case LightEffect.Spectrum: protocol.Send(0x030a, 4); break;
                case LightEffect.Wave: protocol.Send(0x030a, 1, (byte)direction); break;
                default: throw new ArgumentException("未知灯效。");
            }
            // 0F82/VARSTORE reads the stored preset; a NOSTORE custom frame has no
            // reliable active-color getter. Its per-packet firmware ACK is the evidence.
            if (volatileFrame) return;
            int expected = effect == LightEffect.Off ? 0 : effect == LightEffect.Static ? 1 : effect == LightEffect.Breathing ? 2 : effect == LightEffect.Spectrum ? 3 : 4;
            if (ReadEffect() != expected) throw new InvalidOperationException("灯效回读失败，可能被雷云或 Chroma 覆盖。");
        }
        public void SetChargeLimit(bool enable)
        {
            var before = protocol.Send(0x0792, 0); Size(before, 1);
            if (before[0] != 0xd0 && before[0] != 0x50) throw new InvalidOperationException("现有充电阈值无法安全回退。");
            byte desired = enable ? (byte)0xd0 : (byte)0x50;
            try { protocol.Send(0x0712, desired); if (protocol.Send(0x0792, 0)[0] != desired) throw new InvalidOperationException("充电上限回读失败。"); }
            catch { try { protocol.Send(0x0712, before[0]); } catch { } throw; }
        }
        public void NativeFnMode()
        {
            protocol.Send(0x0004, 0, 0);
            if (protocol.Send(0x0084, 0, 0)[0] != 0) throw new InvalidOperationException("原生 Fn 模式回读失败。");
        }
        public void Dispose() { protocol.Dispose(); }
        public static string ModeLabel(int value) { return value == 0 ? "平衡" : value == 2 ? "性能" : value == 4 ? "自定义" : value == 5 ? "安静" : value == 6 ? "省电" : "未识别模式 (" + value + ")"; }
    }
}
