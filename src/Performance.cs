using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RazerHelper
{
    public static class BladePerformancePolicy
    {
        public static bool Known(int mode) { return mode == 0 || mode == 2 || mode == 4 || mode == 5 || mode == 6; }
        public static bool Permitted(BladeMode mode, bool? onAC)
        {
            return onAC.HasValue && (onAC.Value ? mode == BladeMode.Balanced || mode == BladeMode.Performance || mode == BladeMode.Custom || mode == BladeMode.Silent : mode == BladeMode.Balanced || mode == BladeMode.BatterySaver);
        }
        public static bool LevelsRestorable(int? cpu, int? gpu) { return cpu.HasValue && cpu >= 0 && cpu <= 3 && gpu.HasValue && gpu >= 0 && gpu <= 2; }
        public static string LevelLabel(int? value) { return !value.HasValue ? "未知" : value == 0 ? "低" : value == 1 ? "中" : value == 2 ? "高" : value == 3 ? "增强记录" : "未知档位 " + value; }
    }
    public sealed class PerformanceState
    {
        public int Mode { get; set; }
        public int FanMode { get; set; }
        public int? CpuLevel { get; set; }
        public int? GpuLevel { get; set; }
        public bool? OnAC { get; set; }
    }
    public sealed partial class RazerHardware
    {
        private static bool? ReadPowerSource()
        {
            Native.PowerStatus status;
            if (!Native.GetSystemPowerStatus(out status) || status.ACLineStatus > 1) return null;
            return status.ACLineStatus == 1;
        }
        private byte[][] ReadConsistentZones()
        {
            byte[] one = null, two = null;
            for (int i = 0; i < 3; i++)
            {
                one = ReadZone(1); two = ReadZone(2);
                if (one[2] == two[2] && one[3] == two[3]) return new[] { one, two };
                Thread.Sleep(8);
            }
            throw new InvalidOperationException(String.Format("两区状态持续不同（模式 {0}/{1}，风扇 {2}/{3}）；未更改参数。可能正在切换电源或被雷云控制。", one[2], two[2], one[3], two[3]));
        }
        public int ReadLevel(int cluster)
        {
            if (cluster != 1 && cluster != 2) throw new ArgumentOutOfRangeException("cluster");
            var data = protocol.Send(0x0d87, 0, (byte)cluster, 0); Size(data, 3);
            if (data[1] != cluster) throw new InvalidOperationException("CPU / GPU 档位区域不匹配。");
            return data[2];
        }
        public PerformanceState ReadPerformance()
        {
            for (int i = 0; i < 3; i++)
            {
                bool? power = powerSource(); var before = ReadConsistentZones();
                var state = new PerformanceState { Mode = before[0][2], FanMode = before[0][3], OnAC = power };
                try { state.CpuLevel = ReadLevel(1); state.GpuLevel = ReadLevel(2); } catch (NotSupportedException) { }
                bool levelsStable = !state.CpuLevel.HasValue || !state.GpuLevel.HasValue || (ReadLevel(1) == state.CpuLevel && ReadLevel(2) == state.GpuLevel);
                var after = ReadConsistentZones();
                if (levelsStable && before[0][2] == after[0][2] && before[0][3] == after[0][3] && powerSource() == power) return state;
                Thread.Sleep(8);
            }
            throw new InvalidOperationException("读取期间性能模式或供电状态变化，未更改参数；请稳定后重试。");
        }
        private static void Restorable(PerformanceState state, bool requireLevels)
        {
            if (!BladePerformancePolicy.Known(state.Mode)) throw new InvalidOperationException("当前模式代码 " + state.Mode + " 未验证，未更改参数。已支持平衡 0、性能 2、自定义 4、安静 5、省电 6。");
            if (state.FanMode != 0) throw new InvalidOperationException("当前风扇模式代码 " + state.FanMode + " 不是固件自动；未更改参数。请先在雷云选择自动风扇。");
            if ((state.Mode == 4 || requireLevels) && !BladePerformancePolicy.LevelsRestorable(state.CpuLevel, state.GpuLevel)) throw new InvalidOperationException("无法完整回读自定义档位（CPU " + state.CpuLevel + "，GPU " + state.GpuLevel + "），未更改参数。实验性降压档位不支持回写。");
        }
        private void WriteMode(int mode)
        {
            for (byte zone = 1; zone <= 2; zone++)
            {
                if ((mode == 2 || mode == 4 || mode == 5) && powerSource() != true) throw new InvalidOperationException("接电模式写入前供电已变化。");
                protocol.Send(0x0d02, 1, zone, (byte)mode, 0);
            }
        }
        private void VerifyMode(int mode)
        {
            var actual = ReadConsistentZones();
            if (actual[0][2] != mode || actual[0][3] != 0) throw new InvalidOperationException("模式回读不符（期望 " + mode + "，当前 " + actual[0][2] + "/风扇 " + actual[0][3] + "）；可能被雷云覆盖。");
        }
        private void WriteLevel(int cluster, int level)
        {
            if (powerSource() != true) throw new InvalidOperationException("CPU / GPU 档位写入前供电已变化。");
            protocol.Send(0x0d07, 1, (byte)cluster, (byte)level);
        }
        public void SetCustomPerformance(int cpu, int gpu)
        {
            if (cpu < 0 || cpu > 2 || gpu < 0 || gpu > 2) throw new ArgumentOutOfRangeException("level", "本机只提供 CPU / GPU 低、中、高固件档位。");
            ChangePerformance(BladeMode.Custom, cpu, gpu);
        }
        private void ChangePerformance(BladeMode mode, int? cpu, int? gpu)
        {
            if (!Enum.IsDefined(typeof(BladeMode), mode)) throw new ArgumentException("未知性能模式。");
            bool? power = powerSource();
            if (!BladePerformancePolicy.Permitted(mode, power)) throw new InvalidOperationException(!power.HasValue ? "供电状态未知，未更改性能参数。" : power.Value ? "接电仅支持平衡、安静、性能和自定义；省电预设用于电池。" : "使用电池仅支持平衡和省电；请接电后使用安静、性能或自定义。");
            var before = ReadPerformance(); Restorable(before, mode == BladeMode.Custom);
            if (before.OnAC != power) throw new InvalidOperationException("供电状态已变化，未更改参数。");
            if (before.Mode == (int)mode && (!cpu.HasValue || (before.CpuLevel == cpu && before.GpuLevel == gpu))) return;
            var latest = ReadConsistentZones();
            if (latest[0][2] != before.Mode || latest[0][3] != before.FanMode || powerSource() != power) throw new InvalidOperationException("模式或供电状态已被其他控制器更改，未写入参数。");
            if ((before.Mode == 4 || mode == BladeMode.Custom) && (ReadLevel(1) != before.CpuLevel || ReadLevel(2) != before.GpuLevel)) throw new InvalidOperationException("自定义档位已被其他控制器更改，未写入参数。");
            bool levelsTouched = false;
            try
            {
                if (before.Mode != (int)mode) WriteMode((int)mode);
                VerifyMode((int)mode);
                if (powerSource() != power) throw new InvalidOperationException("切换期间供电状态变化。");
                if (cpu.HasValue)
                {
                    if (ReadLevel(1) != cpu) { levelsTouched = true; WriteLevel(1, cpu.Value); }
                    if (ReadLevel(2) != gpu) { levelsTouched = true; WriteLevel(2, gpu.Value); }
                    if (ReadLevel(1) != cpu || ReadLevel(2) != gpu) throw new InvalidOperationException("CPU / GPU 档位回读不符。");
                }
                VerifyMode((int)mode);
                if (powerSource() != power) throw new InvalidOperationException("切换期间供电状态变化。");
            }
            catch (Exception error)
            {
                try { RestorePerformance(before, levelsTouched); }
                catch (Exception rollback) { Log.Write("性能回退未完整成功：" + rollback.Message); throw new AggregateException("性能切换失败，回退结果：" + rollback.Message, error, rollback); }
                throw;
            }
        }
        public void RestorePerformance(PerformanceState before) { RestorePerformance(before, before.OnAC == true); }
        private void RestoreOriginalMode(PerformanceState before)
        {
            if (powerSource() != before.OnAC || (before.Mode != 0 && before.Mode != 6 && powerSource() != true))
            {
                WriteMode(0); VerifyMode(0);
                throw new InvalidOperationException("供电状态变化，已退回平衡和自动风扇，未恢复原接电模式。");
            }
            try { WriteMode(before.Mode); VerifyMode(before.Mode); }
            catch
            {
                if (powerSource() != before.OnAC) { WriteMode(0); VerifyMode(0); }
                throw;
            }
            if (powerSource() != before.OnAC)
            {
                WriteMode(0); VerifyMode(0);
                throw new InvalidOperationException("回退期间供电变化，已退回平衡和自动风扇。");
            }
        }
        private void RestorePerformance(PerformanceState before, bool levelsTouched)
        {
            Restorable(before, levelsTouched);
            bool? now = powerSource();
            if (now != before.OnAC || ((!now.HasValue || !now.Value) && before.Mode != 0 && before.Mode != 6))
            {
                WriteMode(0); VerifyMode(0);
                throw new InvalidOperationException("供电状态变化，已退回平衡和自动风扇，未恢复原接电模式。");
            }
            var errors = new List<Exception>();
            try
            {
                if (levelsTouched || before.Mode == 4)
                {
                    if (now != true) throw new InvalidOperationException("电池时不恢复自定义档位。");
                    WriteMode(4); VerifyMode(4);
                    if (ReadLevel(1) != before.CpuLevel) WriteLevel(1, before.CpuLevel.Value);
                    if (ReadLevel(2) != before.GpuLevel) WriteLevel(2, before.GpuLevel.Value);
                    if (ReadLevel(1) != before.CpuLevel || ReadLevel(2) != before.GpuLevel) throw new InvalidOperationException("原自定义档位未恢复。");
                }
            }
            catch (Exception e) { errors.Add(e); }
            finally { try { RestoreOriginalMode(before); } catch (Exception e) { errors.Add(e); } }
            if (errors.Count != 0) throw new AggregateException("原性能状态未完整恢复。", errors);
        }
    }
}
