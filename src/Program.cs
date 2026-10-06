using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RazerHelper
{
    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wp, IntPtr lp);
        internal static readonly uint ShowMessage = RegisterWindowMessage("RazerHelper.Show.02C5.v1");
        internal static readonly uint QuitMessage = RegisterWindowMessage("RazerHelper.Quit.02C5.v1");
        private static object CheckMacro(MacroDefinition macro, IEnumerable<MacroDefinition> library)
        {
            string reason="";try{MacroPlan.Compile(macro,library);}catch(Exception error){reason=error.Message;}
            return new{macro.Id,Ready=reason.Length==0,Reason=reason,TextActions=macro.Events.Count(e=>e.Kind=="Text"),ClipboardActions=macro.Events.Count(e=>e.Kind=="Clipboard")};
        }
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var arguments = args.ToList();
            int data = arguments.IndexOf("--data-dir");
            if (data >= 0) { if (data + 1 >= arguments.Count) return 2; Store.DirectoryPath = Path.GetFullPath(arguments[data + 1]); arguments.RemoveRange(data, 2); }
            args = arguments.ToArray();
            try
            {
                if (args.Length != 0)
                {
                    if (args[0] == "--display-guard") return DisplayGuard.Watch(args);
                    if (args[0] == "--diagnose" || args[0] == "--status")
                    {
                        string json = Store.Serializer().Serialize(Diagnose());
                        if (args.Length > 1) Store.WriteAtomic(Path.GetFullPath(args[1]), json); else Console.WriteLine(json); return 0;
                    }
                    if (args[0] == "--exercise-safe")
                    {
                        if (args.Length != 2) return 2; return Exercise(Path.GetFullPath(args[1]));
                    }
                    if (args[0] == "--exercise-lighting-charge") { if (args.Length != 2) return 2; return ExerciseExtras(Path.GetFullPath(args[1])); }
                    if (args[0] == "--probe-performance")
                    {
                        using (var hardware = RazerHardware.Open(true)) Console.WriteLine(Store.Serializer().Serialize(hardware.ReadPerformance())); return 0;
                    }
                    if (args[0] == "--scan-synapse")
                    {
                        var scan = SynapseImport.Scan(); string summary = Store.Serializer().Serialize(new { Notes = scan.Notes, Macros = scan.Macros.Select(x => new { x.Name, x.Source, x.Error, Steps = x.Macro == null ? 0 : x.Macro.Events.Count }), Bindings = scan.Bindings });
                        if (args.Length == 2) Store.WriteAtomic(Path.GetFullPath(args[1]), summary); else Console.WriteLine(summary); return 0;
                    }
                    if(args[0]=="--advanced-status")
                    {
                        var cpu=new WindowsProcessorPolicy();var status=new Dictionary<string,object>{{"CapturedAt",DateTimeOffset.Now.ToString("o")},{"CpuAC",cpu.Read(true)},{"CpuDC",cpu.Read(false)}};
                        try{status["Nvidia"]=NvidiaPower.Read();}catch(Exception error){status["NvidiaError"]=error.Message;}
                        string json=Store.Serializer().Serialize(status);if(args.Length==2)Store.WriteAtomic(Path.GetFullPath(args[1]),json);else Console.WriteLine(json);return 0;
                    }
                    if(args[0]=="--check-config"){var settings=Store.Load();Console.WriteLine(Store.Serializer().Serialize(new{Valid=true,Macros=settings.Macros.Count,Bindings=settings.MacroBindings.Count,EnabledBindings=settings.MacroBindings.Count(x=>x.Enabled),FnConfigured=settings.FnSignal!=null,Playback=settings.Macros.Select(m=>CheckMacro(m,settings.Macros)).ToList()}));return 0;}
                    if(args[0]=="--repair-synapse-text")
                    {
                        if(args.Length!=1)return 2;
                        var settings=Store.Load();var scan=SynapseImport.Scan();int repaired=0,actions=0;
                        foreach(var macro in settings.Macros){int count=SynapseImport.RepairLegacyText(macro,scan.Macros);if(count>0){repaired++;actions+=count;}}
                        string backup=null;
                        if(repaired>0){backup=Path.Combine(Store.DirectoryPath,"settings.before-macro-text-fix-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".json");File.Copy(Store.ConfigPath,backup,false);Store.Save(settings);}
                        Console.WriteLine(Store.Serializer().Serialize(new{RepairedMacros=repaired,TextActions=actions,Backup=backup,Played=false}));return 0;
                    }
                    if (args[0] == "--exercise-performance") { if (args.Length != 2) return 2; return ExercisePerformance(Path.GetFullPath(args[1])); }
                    if (args[0] == "--arm-display-watchdog")
                    {
                        if (args.Length != 2) return 2;
                        var screen = WindowsDisplay.BuiltIn(); if (screen.Hz == 60) throw new InvalidOperationException("需要非 60 Hz 初始状态。");
                        Store.WriteAtomic(Path.GetFullPath(args[1]), Store.Serializer().Serialize(screen));
                        var watchdog = new DisplayGuard(screen, 60);
                        Console.WriteLine("Armed; parent exits without confirmation. The watcher must restore " + screen.Hz + " Hz.");
                        GC.KeepAlive(watchdog); return 0;
                    }
                    if (args[0] == "--quit") { PostMessage(new IntPtr(0xffff), QuitMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                    if (args[0] == "--probe-backlight")
                    {
                        using (var hardware = RazerHardware.Open(true)) Console.WriteLine("Backlight query: " + hardware.KeepBacklightAwake());
                        Console.WriteLine("Interactive desktop: " + BacklightMonitor.InteractiveDesktop()); return 0;
                    }
                    if (args[0] == "--render-ui")
                    {
                        if (args.Length != 2) return 2;
                        using (var controller = new Controller(new Settings())) using (var form = new MainForm(controller, false, true)) form.Render(Path.GetFullPath(args[1])); return 0;
                    }
                    if(args[0]=="--render-advanced"){if(args.Length!=2)return 2;UiPreview.Render(Path.GetFullPath(args[1]));return 0;}
                    if (args.Any(x=>x!="--tray" && x!="--no-startup-apply") || args.Distinct().Count()!=args.Length) { Console.Error.WriteLine("Usage: RazerHelper.exe [--tray] [--no-startup-apply] | --status | --advanced-status | --scan-synapse | --render-ui directory | --quit"); return 2; }
                }
                bool created;
                using (var instance = new Mutex(true, @"Local\RazerHelper-Main-02C5", out created))
                {
                    if (!created) { if (!args.Contains("--tray")) PostMessage(new IntPtr(0xffff), ShowMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                    try
                    {
                        var settings = Store.Load();
                        if (settings.Baseline == null)
                        {
                            settings.Baseline = Store.Capture();
                            var screen = settings.Baseline.Displays.FirstOrDefault(x => x.Internal);
                            if (screen != null && screen.Hz > 60) settings.ACRate = screen.Hz;
                            if (settings.Baseline.Hardware != null) settings.Brightness = (int)Math.Round(settings.Baseline.Hardware.Brightness / 2.55);
                            Store.Save(settings);
                        }
                        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Log.Write("界面异常：" + e.Exception.Message); MessageBox.Show(e.Exception.Message, "Razer Helper"); };
                        using (var controller = new Controller(settings)) using (var form = new MainForm(controller, args.Contains("--tray"), false)){form.SkipStartupPolicy=args.Contains("--no-startup-apply");Application.Run(form);}
                    }
                    finally { instance.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception e)
            {
                Log.Write("启动失败：" + e.Message);
                if (args.Length != 0) Console.Error.WriteLine(e);
                else MessageBox.Show(e.Message + "\n配置保留在 " + Store.DirectoryPath, "Razer Helper", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        internal static Dictionary<string, object> Diagnose()
        {
            var report = new Dictionary<string, object> { { "Version", "1.1.0" }, { "CapturedAt", DateTimeOffset.Now.ToString("o") }, { "Model", HidDiscovery.Model() }, { "Power", WindowsPower.Read() }, { "Displays", WindowsDisplay.Read() }, { "HidEndpoints", HidDiscovery.Enumerate() }, { "Runtime", SynapseStatus.Read() } };
            try { using (var h = RazerHardware.Open(true)) report["Hardware"] = h.Read(); }
            catch (Exception e) { report["HardwareError"] = e.Message; }
            return report;
        }
        private static int ExercisePerformance(string path)
        {
            PerformanceState before, after = null; HardwareState unrelatedBefore, unrelatedAfter = null;
            using (var h = RazerHardware.Open(true)) { before = h.ReadPerformance(); unrelatedBefore = h.Read(); }
            Store.WriteAtomic(path + ".before.json", Store.Serializer().Serialize(new { Performance = before, Hardware = unrelatedBefore }));
            if (before.OnAC != true || before.FanMode != 0 || !new[] { 0, 2, 4, 5 }.Contains(before.Mode) || !before.CpuLevel.HasValue || before.CpuLevel < 0 || before.CpuLevel > 2 || !before.GpuLevel.HasValue || before.GpuLevel < 0 || before.GpuLevel > 2)
                throw new InvalidOperationException("性能验证要求稳定接电、自动风扇和可回读的普通 CPU / GPU 档位；未执行写入。");
            var results = new List<string>(); Exception failure = null, restoreFailure = null;
            using (var h = RazerHardware.Open(false))
            {
                try
                {
                    h.SetCustomPerformance(before.CpuLevel.Value, before.GpuLevel.Value);
                    results.Add("Custom mode with existing levels verified");
                    int lowerCpu = Math.Max(0, before.CpuLevel.Value - 1), lowerGpu = Math.Max(0, before.GpuLevel.Value - 1);
                    h.SetCustomPerformance(lowerCpu, lowerGpu);
                    var reduced = h.ReadPerformance();
                    if (reduced.Mode != 4 || reduced.CpuLevel != lowerCpu || reduced.GpuLevel != lowerGpu || reduced.FanMode != 0) throw new InvalidOperationException("降低档位后的回读不符。");
                    results.Add("Only lower or equal stock CPU / GPU levels applied and verified");
                }
                catch (Exception e) { failure = e; }
                finally { try { h.RestorePerformance(before); results.Add("Original mode and both custom records restored"); } catch (Exception e) { restoreFailure = e; } }
                try { after = h.ReadPerformance(); unrelatedAfter = h.Read(); } catch (Exception e) { if (restoreFailure == null) restoreFailure = e; }
            }
            bool restored = after != null && after.Mode == before.Mode && after.FanMode == before.FanMode && after.CpuLevel == before.CpuLevel && after.GpuLevel == before.GpuLevel && after.OnAC == before.OnAC;
            bool unrelatedPreserved = unrelatedAfter != null && unrelatedBefore.Brightness == unrelatedAfter.Brightness && unrelatedBefore.Effect == unrelatedAfter.Effect && unrelatedBefore.ChargeLimit == unrelatedAfter.ChargeLimit && unrelatedBefore.DeviceMode == unrelatedAfter.DeviceMode;
            bool passed = failure == null && restoreFailure == null && restored && unrelatedPreserved;
            Store.WriteAtomic(path, Store.Serializer().Serialize(new { Passed = passed, Restored = restored, UnrelatedPreserved = unrelatedPreserved, Before = before, After = after, Results = results, Failure = failure == null ? null : failure.ToString(), RestoreFailure = restoreFailure == null ? null : restoreFailure.ToString() }));
            Console.WriteLine("Passed=" + passed + "; Restored=" + restored + "; UnrelatedPreserved=" + unrelatedPreserved); return passed ? 0 : 1;
        }
        private static int Exercise(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var results = new List<object>(); var before = Diagnose(); Store.WriteAtomic(path + ".before.json", Store.Serializer().Serialize(before));
            var screen = WindowsDisplay.BuiltIn(); Guid originalPower = WindowsPower.Overlay(); HardwareState original;
            using (var hardware = RazerHardware.Open(true)) original = hardware.Read();
            if (original.FanMode != 0 || !new[] { 0, 2, 5 }.Contains(original.Mode) || original.Effect != 3)
                throw new InvalidOperationException("安全验证要求自动风扇、已知预设和可恢复的光谱灯效；未执行写入。");
            bool passed = true;
            using (var hardware = RazerHardware.Open(false))
            {
                try
                {
                    Test(results, "brightness", delegate { hardware.SetBrightness(Math.Max(0, original.Brightness - 5)); if (hardware.ReadBrightness() != Math.Max(0, original.Brightness - 5)) throw new Exception("Readback"); hardware.SetBrightness(original.Brightness); });
                    Test(results, "mode-balanced", delegate { hardware.SetMode(BladeMode.Balanced); });
                    Test(results, "mode-silent", delegate { hardware.SetMode(BladeMode.Silent); });
                    if (WindowsPower.Read().OnAC == true) Test(results, "mode-performance", delegate { hardware.SetMode(BladeMode.Performance); });
                    hardware.SetMode((BladeMode)original.Mode);
                    Test(results, "lighting-static", delegate { hardware.SetEffect(LightEffect.Static, 64, 180, 100, 1); });
                    Test(results, "lighting-breathing", delegate { hardware.SetEffect(LightEffect.Breathing, 64, 180, 100, 1); });
                    Test(results, "lighting-wave", delegate { hardware.SetEffect(LightEffect.Wave, 0, 0, 0, 1); });
                    Test(results, "lighting-off", delegate { hardware.SetEffect(LightEffect.Off, 0, 0, 0, 1); });
                    hardware.SetEffect(LightEffect.Spectrum, 0, 0, 0, 1);
                    if (original.ChargeLimit.HasValue) Test(results, "charge-limit-same-value", delegate { hardware.SetChargeLimit(original.ChargeLimit.Value == 80); });
                    Test(results, "windows-efficiency", delegate { WindowsPower.Set("Efficiency"); });
                    Test(results, "windows-balanced", delegate { WindowsPower.Set("Balanced"); });
                    Test(results, "windows-performance", delegate { WindowsPower.Set("Performance"); });
                    WindowsPower.Set(originalPower);
                    Test(results, "display-60-and-restore", delegate { using (var guard = new DisplayGuard(screen, 60)) { if (WindowsDisplay.BuiltIn().Hz != 60) throw new Exception("Readback"); guard.Revert(); } if (WindowsDisplay.BuiltIn().Hz != screen.Hz) throw new Exception("Rollback"); });
                }
                catch (Exception e) { passed = false; results.Add(new { Name = "exercise", Passed = false, Error = e.Message }); }
                finally
                {
                    try { hardware.SetMode((BladeMode)original.Mode); hardware.SetBrightness(original.Brightness); hardware.SetEffect(LightEffect.Spectrum, 0, 0, 0, 1); }
                    catch (Exception e) { passed = false; results.Add(new { Name = "hardware-rollback", Passed = false, Error = e.Message }); }
                    try { WindowsPower.Set(originalPower); if (WindowsDisplay.BuiltIn().Hz != screen.Hz) WindowsDisplay.Set(WindowsDisplay.BuiltIn().Name, screen.Hz); }
                    catch (Exception e) { passed = false; results.Add(new { Name = "windows-rollback", Passed = false, Error = e.Message }); }
                }
            }
            var after = Diagnose(); var finalHardware = after.ContainsKey("Hardware") ? (HardwareState)after["Hardware"] : null;
            bool restored = finalHardware != null && finalHardware.Brightness == original.Brightness && finalHardware.Mode == original.Mode && finalHardware.FanMode == original.FanMode && finalHardware.Effect == original.Effect && finalHardware.ChargeLimit == original.ChargeLimit && WindowsPower.Overlay() == originalPower && WindowsDisplay.BuiltIn().Hz == screen.Hz;
            passed &= restored;
            // Each Test records a failure and lets cleanup run before this aggregate is written.
            passed &= !results.Any(x => Store.Serializer().Serialize(x).Contains("\"Passed\":false"));
            Store.WriteAtomic(path, Store.Serializer().Serialize(new { Passed = passed, Restored = restored, Results = results, After = after }));
            Console.WriteLine("Passed=" + passed + "; Restored=" + restored + "; " + path); return passed ? 0 : 1;
        }
        private static void Test(List<object> results, string name, Action action)
        {
            try { action(); results.Add(new { Name = name, Passed = true }); }
            catch (Exception e) { results.Add(new { Name = name, Passed = false, Error = e.Message }); }
        }
        private static int ExerciseExtras(string path)
        {
            var results = new List<object>(); HardwareState original;
            using (var hardware = RazerHardware.Open(true)) original = hardware.Read();
            if (original.Effect != 3 || !original.ChargeLimit.HasValue) throw new InvalidOperationException("仅在原光谱灯效和已知充电阈值时执行补充验证。");
            using (var hardware = RazerHardware.Open(false))
            {
                try
                {
                    Test(results, "solid-RGB-current-device-mode", delegate { hardware.SetEffect(LightEffect.Static, 50, 180, 110, 1); });
                    Test(results, "charge-full", delegate { hardware.SetChargeLimit(false); if (hardware.Read().ChargeLimit != 100) throw new Exception("Readback"); });
                    Test(results, "charge-80", delegate { hardware.SetChargeLimit(true); if (hardware.Read().ChargeLimit != 80) throw new Exception("Readback"); });
                }
                finally
                {
                    Test(results, "restore-spectrum", delegate { hardware.SetEffect(LightEffect.Spectrum, 0, 0, 0, 1); });
                    Test(results, "restore-original-charge-limit", delegate { hardware.SetChargeLimit(original.ChargeLimit == 80); });
                }
            }
            HardwareState after; using (var hardware = RazerHardware.Open(true)) after = hardware.Read();
            bool restored = after.Effect == original.Effect && after.ChargeLimit == original.ChargeLimit && after.Mode == original.Mode && after.Brightness == original.Brightness;
            bool passed = restored && !results.Any(x => Store.Serializer().Serialize(x).Contains("\"Passed\":false"));
            Store.WriteAtomic(path, Store.Serializer().Serialize(new { Passed = passed, Restored = restored, Results = results, After = after }));
            Console.WriteLine("Passed=" + passed + "; Restored=" + restored); return passed ? 0 : 1;
        }
    }
}
