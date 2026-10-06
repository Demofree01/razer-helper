using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace RazerHelper
{
    public sealed class Baseline
    {
        public string Model { get; set; }
        public string CapturedAt { get; set; }
        public string Overlay { get; set; }
        public List<DisplayInfo> Displays { get; set; }
        public HardwareState Hardware { get; set; }
        public bool StartupEnabled { get; set; }
    }
    public sealed class Settings
    {
        public int Version { get; set; }
        public bool Paused { get; set; }
        public bool AutoRefresh { get; set; }
        public int ACRate { get; set; }
        public bool AutoWindowsMode { get; set; }
        public string ACWindowsMode { get; set; }
        public string BatteryWindowsMode { get; set; }
        public bool AutoBladeMode { get; set; }
        public string ACBladeMode { get; set; }
        public string BatteryBladeMode { get; set; }
        public bool CustomLevelsConfigured { get; set; }
        public int CustomCpuLevel { get; set; }
        public int CustomGpuLevel { get; set; }
        public bool RestoreLightingOnStart { get; set; }
        public string Effect { get; set; }
        public int Red { get; set; }
        public int Green { get; set; }
        public int Blue { get; set; }
        public int Direction { get; set; }
        public int Brightness { get; set; }
        public bool DimOnBattery { get; set; }
        public bool KeepKeyboardLit { get; set; }
        public int BatteryBrightness { get; set; }
        public Baseline Baseline { get; set; }
        public List<MacroDefinition> Macros { get; set; }
        public List<MacroBinding> MacroBindings { get; set; }
        public FnSignal FnSignal { get; set; }
        public Settings()
        {
            Version = 1; AutoRefresh = true; ACRate = 120;
            ACWindowsMode = "Balanced"; BatteryWindowsMode = "Efficiency";
            ACBladeMode = "Balanced"; BatteryBladeMode = "BatterySaver"; CustomCpuLevel = 1; CustomGpuLevel = 1;
            Effect = "Spectrum"; Red = 50; Green = 220; Blue = 110; Direction = 1;
            Brightness = 50; BatteryBrightness = 25; Macros = new List<MacroDefinition>(); MacroBindings = new List<MacroBinding>();
        }
        public void Validate()
        {
            if (Version != 1) throw new InvalidDataException("配置版本不受支持。");
            if (ACRate < 24 || ACRate > 500 || Brightness < 0 || Brightness > 100 || BatteryBrightness < 0 || BatteryBrightness > 100) throw new InvalidDataException("配置参数超出允许范围。");
            WindowsPower.ForName(ACWindowsMode); WindowsPower.ForName(BatteryWindowsMode);
            BladeMode blade;
            if (!Enum.TryParse<BladeMode>(ACBladeMode, out blade) || !BladePerformancePolicy.Permitted(blade, true) || !Enum.TryParse<BladeMode>(BatteryBladeMode, out blade) || !BladePerformancePolicy.Permitted(blade, false)) throw new InvalidDataException("无效的接电 / 电池 Blade 预设。");
            if (CustomCpuLevel < 0 || CustomCpuLevel > 2 || CustomGpuLevel < 0 || CustomGpuLevel > 2) throw new InvalidDataException("自定义档位仅允许低、中、高。");
            LightEffect effect;
            if (!Enum.TryParse<LightEffect>(Effect, out effect) || !Enum.IsDefined(typeof(LightEffect), effect)) throw new InvalidDataException("无效的灯效。");
            if (new[] { Red, Green, Blue }.Any(x => x < 0 || x > 255) || (Direction != 1 && Direction != 2)) throw new InvalidDataException("无效的灯效参数。");
            if (Macros == null || Macros.Count > 64) throw new InvalidDataException("宏数量上限为 64。");
            foreach (var macro in Macros) macro.Validate();
            if (Macros.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Macros.Count) throw new InvalidDataException("宏标识重复。");
            if (MacroBindings == null || MacroBindings.Count > 64) throw new InvalidDataException("宏绑定数量上限为 64。");
            foreach (var binding in MacroBindings) binding.Validate(Macros);
            if (FnSignal == null && MacroBindings.Any(x=>x.Enabled && x.Modifier=="Fn")) throw new InvalidDataException("Fn 绑定需先识别按下 / 释放信号。");
            if (MacroBindings.Where(x => x.Enabled).GroupBy(x => x.Modifier + ":" + x.Scan + ":" + x.Extended).Any(x => x.Count() > 1)) throw new InvalidDataException("同一组合键不能启用多个宏。");
            if (FnSignal != null) FnSignal.Validate();
            if (Macros.Where(x => x.HotKey != 0).GroupBy(x => x.HotKey).Any(x => x.Count() > 1)) throw new InvalidDataException("宏快捷键重复。");
        }
    }
    public static class Store
    {
        public static string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RazerHelper");
        public static string ConfigPath { get { return Path.Combine(DirectoryPath, "settings.json"); } }
        public const int MaximumConfigBytes = 32 * 1024 * 1024;
        public static JavaScriptSerializer Serializer() { var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumConfigBytes, RecursionLimit = 48 }; serializer.RegisterConverters(new[] { new MacroEventConverter() }); return serializer; }
        public static Settings Load()
        {
            if (!File.Exists(ConfigPath)) return new Settings();
            if (new FileInfo(ConfigPath).Length > MaximumConfigBytes) throw new InvalidDataException("配置文件过大。");
            var settings = Serializer().Deserialize<Settings>(File.ReadAllText(ConfigPath, Encoding.UTF8));
            if (settings == null) throw new InvalidDataException("配置为空。");
            // The old UI incorrectly offered Silent on battery. Migrate only that
            // legacy value; keep automatic performance switching opt-in.
            if (settings.BatteryBladeMode == "Silent") settings.BatteryBladeMode = "BatterySaver";
            settings.Validate(); return settings;
        }
        public static void Save(Settings settings) { settings.Validate(); string json=Serializer().Serialize(settings);if(Encoding.UTF8.GetByteCount(json)>MaximumConfigBytes)throw new InvalidDataException("配置超过 32 MB 字节上限，原文件已保留。");WriteAtomic(ConfigPath, json); }
        public static void WriteAtomic(string path, string text)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path)); Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(text); file.Write(bytes, 0, bytes.Length); file.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static Baseline Capture()
        {
            var baseline = new Baseline { Model = HidDiscovery.Model(), CapturedAt = DateTimeOffset.Now.ToString("o"), Overlay = WindowsPower.Read().Mode, Displays = WindowsDisplay.Read(), StartupEnabled = Startup.Enabled() };
            try { using (var hardware = RazerHardware.Open(true)) baseline.Hardware = hardware.Read(); }
            catch (Exception e) { Log.Write("硬件基线暂不可读：" + e.Message); }
            return baseline;
        }
    }
    public static class Log
    {
        private static readonly object Gate = new object();
        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Store.DirectoryPath); string path = Path.Combine(Store.DirectoryPath, "helper.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) { string rotated = path + ".1"; if (File.Exists(rotated)) File.Delete(rotated); File.Move(path, rotated); }
                    File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* Diagnostics must never prevent hardware cleanup. */ }
        }
    }
    public static class Startup
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private static string Command { get { return "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --tray"; } }
        public static bool Enabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath)) return key != null && String.Equals(Convert.ToString(key.GetValue("RazerHelper", "")), Command, StringComparison.OrdinalIgnoreCase);
        }
        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                string current = Convert.ToString(key.GetValue("RazerHelper", ""));
                if (current.Length != 0 && !String.Equals(current, Command, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("同名启动项已有其他路径；请先在启动应用中处理。");
                if (enabled) key.SetValue("RazerHelper", Command, RegistryValueKind.String);
                else if (Enabled()) key.DeleteValue("RazerHelper", false);
            }
        }
    }
    public static class SynapseStatus
    {
        public static Dictionary<string, object> Read()
        {
            var result = new Dictionary<string, object>();
            using (var p = Process.GetCurrentProcess()) result["HelperWorkingSetMB"] = Math.Round(p.WorkingSet64 / 1048576.0, 1);
            int count = 0; long memory = 0;
            foreach (var p in Process.GetProcessesByName("RazerAppEngine"))
                using (p) { try { memory += p.WorkingSet64; count++; } catch (InvalidOperationException) { } }
            result["SynapseUiProcesses"] = count; result["SynapseUiWorkingSetMB"] = Math.Round(memory / 1048576.0, 1);
            var services = new List<object>();
            foreach (var service in System.ServiceProcess.ServiceController.GetServices())
                using (service)
                {
                    try { if (service.DisplayName.IndexOf("Razer", StringComparison.OrdinalIgnoreCase) >= 0) services.Add(new { service.ServiceName, service.DisplayName, Status = service.Status.ToString() }); }
                    catch (SystemException) { }
                }
            result["Services"] = services; return result;
        }
    }
}
