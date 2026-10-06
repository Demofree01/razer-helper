using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RazerHelper
{
    public static class PowerPolicy
    {
        public static int? DesiredRate(Settings settings, bool? onAC)
        {
            if (settings.Paused || !settings.AutoRefresh || !onAC.HasValue) return null;
            return onAC.Value ? settings.ACRate : 60;
        }
    }
    public sealed class Controller : IDisposable
    {
        private readonly SemaphoreSlim operation = new SemaphoreSlim(1, 1);
        public Settings Settings { get; private set; }
        public event Action<string, bool> Notice;
        public Controller(Settings settings) { Settings = settings; }
        private void Tell(string message, bool error) { Log.Write(message); var notice = Notice; if (notice != null) notice(message, error); }
        public Task<bool> Run(string name, Action action)
        {
            return Task.Run(async delegate
            {
                await operation.WaitAsync();
                try { action(); Tell(name + "完成", false); return true; }
                catch (Exception e) { Tell(name + "：" + e.Message, true); return false; }
                finally { operation.Release(); }
            });
        }
        public Task Hardware(string name, Action<RazerHardware> action)
        {
            return Run(name, delegate { using (var hardware = RazerHardware.Open(false)) action(hardware); });
        }
        public Task<bool> KeepBacklight(Func<bool> permitted)
        {
            return Task.Run(async delegate
            {
                if (!permitted() || !await operation.WaitAsync(0)) return false;
                try
                {
                    if (!permitted()) return false;
                    using (var hardware = RazerHardware.Open(true))
                    {
                        if (!permitted()) return false;
                        hardware.KeepBacklightAwake(); return true;
                    }
                }
                finally { operation.Release(); }
            });
        }
        public Task ApplyPower(bool startup)
        {
            return Run("自动策略", delegate
            {
                if (Settings.Paused) return;
                var power = WindowsPower.Read();
                if (!power.OnAC.HasValue) { Tell("电源状态未知，已跳过自动切换。", true); return; }
                var errors = new List<string>(); int? rate = PowerPolicy.DesiredRate(Settings, power.OnAC);
                if (rate.HasValue)
                {
                    try { var screen = WindowsDisplay.BuiltIn(); if (screen.Hz != rate) WindowsDisplay.Set(screen.Name, rate.Value); }
                    catch (Exception e) { errors.Add("刷新率：" + e.Message); }
                }
                if (Settings.AutoWindowsMode)
                {
                    try { WindowsPower.Set(power.OnAC.Value ? Settings.ACWindowsMode : Settings.BatteryWindowsMode); }
                    catch (Exception e) { errors.Add("Windows 电源：" + e.Message); }
                }
                if (Settings.AutoBladeMode || Settings.DimOnBattery || (startup && Settings.RestoreLightingOnStart))
                {
                    try
                    {
                        using (var hardware = RazerHardware.Open(false))
                        {
                            if (Settings.AutoBladeMode)
                            {
                                var mode = (BladeMode)Enum.Parse(typeof(BladeMode), power.OnAC.Value ? Settings.ACBladeMode : Settings.BatteryBladeMode);
                                if (mode == BladeMode.Custom) hardware.SetCustomPerformance(Settings.CustomCpuLevel, Settings.CustomGpuLevel);
                                else hardware.SetMode(mode);
                            }
                            if (startup && Settings.RestoreLightingOnStart) hardware.SetEffect((LightEffect)Enum.Parse(typeof(LightEffect), Settings.Effect), (byte)Settings.Red, (byte)Settings.Green, (byte)Settings.Blue, Settings.Direction);
                            if (Settings.DimOnBattery || (startup && Settings.RestoreLightingOnStart)) hardware.SetBrightness((int)Math.Round((Settings.DimOnBattery && !power.OnAC.Value ? Settings.BatteryBrightness : Settings.Brightness) * 2.55));
                        }
                    }
                    catch (Exception e) { errors.Add("Blade：" + e.Message); }
                }
                if (errors.Count != 0) throw new InvalidOperationException(String.Join("；", errors));
            });
        }
        public Task RestoreBaseline()
        {
            return Run("恢复初次启动前设置", delegate
            {
                var baseline = Settings.Baseline;
                if (baseline == null || baseline.Model != HidDiscovery.Model()) throw new InvalidOperationException("缺少本机原始设置。");
                Settings.Paused = true; Store.Save(Settings); var errors = new List<string>();
                try { if (!String.IsNullOrEmpty(baseline.Overlay)) WindowsPower.Set(new Guid(baseline.Overlay)); } catch (Exception e) { errors.Add("Windows 电源：" + e.Message); }
                try
                {
                    var screen = WindowsDisplay.BuiltIn();
                    var original = baseline.Displays.Find(x => x.Internal && x.Width == screen.Width && x.Height == screen.Height);
                    if (original != null && original.Hz != screen.Hz) WindowsDisplay.Set(screen.Name, original.Hz);
                }
                catch (Exception e) { errors.Add("屏幕：" + e.Message); }
                if (baseline.Hardware != null)
                {
                    try
                    {
                        using (var hardware = RazerHardware.Open(false))
                        {
                            var old = baseline.Hardware;
                            if (old.FanMode == 0 && BladePerformancePolicy.Known(old.Mode))
                            {
                                if (old.Mode == 4 && old.CpuLevel.HasValue && old.GpuLevel.HasValue && old.CpuLevel <= 2 && old.GpuLevel <= 2) hardware.SetCustomPerformance(old.CpuLevel.Value, old.GpuLevel.Value);
                                else if (old.Mode == 4) errors.Add("原自定义 CPU / GPU 档位无法完整恢复，请在雷云恢复。");
                                else hardware.SetMode((BladeMode)old.Mode);
                            }
                            hardware.SetBrightness(old.Brightness);
                            if (old.ChargeLimit.HasValue) hardware.SetChargeLimit(old.ChargeLimit.Value == 80);
                            if (old.Effect == 0) hardware.SetEffect(LightEffect.Off, 0, 0, 0, 1);
                            else if (old.Effect == 3) hardware.SetEffect(LightEffect.Spectrum, 0, 0, 0, 1);
                            else errors.Add("原复杂灯效的颜色无法回读，可在雷云中恢复原配置。");
                            if (old.DeviceMode == 0) hardware.NativeFnMode();
                            else if (hardware.Read().DeviceMode != old.DeviceMode) errors.Add("原雷云 Fn 控制层需启动雷云恢复。");
                        }
                    }
                    catch (Exception e) { errors.Add("Blade：" + e.Message); }
                }
                if (Startup.Enabled() != baseline.StartupEnabled) Startup.Set(baseline.StartupEnabled);
                if (errors.Count != 0) throw new InvalidOperationException(String.Join("；", errors));
            });
        }
        public void Dispose() { /* Pending operations retain the semaphore until process exit. */ }
    }
}
