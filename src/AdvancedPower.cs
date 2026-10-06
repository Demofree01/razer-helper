using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace RazerHelper
{
    public sealed class ProcessorPolicyState
    {
        public Guid Plan { get; set; }
        public Guid ActivePlan { get; set; }
        public bool AC { get; set; }
        public Guid? Overlay { get; set; }
        public int? Minimum { get; set; }
        public int? Maximum { get; set; }
        public int? Boost { get; set; }
        public int? Epp { get; set; }
    }
    public interface IProcessorPolicy
    {
        ProcessorPolicyState Read(bool ac);
        ProcessorPolicyState ReadPlan(Guid plan, bool ac);
        void Write(Guid plan, bool ac, string setting, int value);
        void Activate(Guid plan);
    }
    public sealed class WindowsProcessorPolicy : IProcessorPolicy
    {
        private static readonly Guid Group = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        private static readonly Dictionary<string, Guid> Keys = new Dictionary<string, Guid> { { "Minimum", new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c") }, { "Maximum", new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec") }, { "Boost", new Guid("be337238-0d82-4146-a960-4f3749d470c7") }, { "Epp", new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863") } };
        [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid plan, ref Guid group, ref Guid key, out uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid plan, ref Guid group, ref Guid key, out uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid plan, ref Guid group, ref Guid key, uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid plan, ref Guid group, ref Guid key, uint value);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid plan);
        public ProcessorPolicyState Read(bool ac)
        {
            var power = WindowsPower.Read(); Guid plan; if (!Guid.TryParse(power.Plan, out plan)) throw new InvalidOperationException("当前 Windows 电源计划不可读。");
            Guid overlay;if(!Guid.TryParse(power.Mode,out overlay))throw new InvalidOperationException("当前 Windows 电源模式不可读。");
            var result = ReadPlan(Target(plan,overlay), ac); var after = WindowsPower.Read();
            if (power.Plan != after.Plan || power.Mode != after.Mode) throw new InvalidOperationException("读取期间电源计划变化，请刷新重试。");
            return result;
        }
        internal static Guid Target(Guid plan,Guid overlay) { return overlay==Guid.Empty?plan:overlay; }
        public ProcessorPolicyState ReadPlan(Guid plan, bool ac)
        {
            var power=WindowsPower.Read();Guid overlay,active; var state = new ProcessorPolicyState { Plan = plan, AC = ac, Minimum = Get(plan, ac, "Minimum"), Maximum = Get(plan, ac, "Maximum"), Boost = Get(plan, ac, "Boost"), Epp = Get(plan, ac, "Epp") };
            if (Guid.TryParse(power.Mode, out overlay)) state.Overlay = overlay;if(Guid.TryParse(power.Plan,out active))state.ActivePlan=active; return state;
        }
        private static int? Get(Guid plan, bool ac, string name) { Guid group = Group, key = Keys[name]; uint value; uint code = ac ? PowerReadACValueIndex(IntPtr.Zero, ref plan, ref group, ref key, out value) : PowerReadDCValueIndex(IntPtr.Zero, ref plan, ref group, ref key, out value); return code == 0 && value <= Int32.MaxValue ? (int?)value : null; }
        public void Write(Guid plan, bool ac, string name, int value)
        {
            Guid group = Group, key; if (!Keys.TryGetValue(name, out key)) throw new ArgumentException("未知 CPU 策略项。");
            uint code = ac ? PowerWriteACValueIndex(IntPtr.Zero, ref plan, ref group, ref key, (uint)value) : PowerWriteDCValueIndex(IntPtr.Zero, ref plan, ref group, ref key, (uint)value);
            if (code != 0) throw new Win32Exception((int)code, "Windows 拒绝写入 CPU 策略");
        }
        public void Activate(Guid plan)
        {
            var power = WindowsPower.Read(); Guid overlay,active;
            if (!Guid.TryParse(power.Plan,out active) || !Guid.TryParse(power.Mode, out overlay) || Target(active,overlay)!=plan || !ProcessorPolicyTransaction.KnownOverlay(overlay)) throw new InvalidOperationException("刷新前电源计划变化或电源模式不可恢复。");
            if(overlay!=Guid.Empty){WindowsPower.Set(overlay);if(WindowsPower.Read().Plan!=power.Plan)throw new InvalidOperationException("刷新期间基础电源计划变化。");return;}
            uint code = PowerSetActiveScheme(IntPtr.Zero, ref plan); if (code != 0) throw new Win32Exception((int)code, "Windows 拒绝刷新电源计划");
            if (WindowsPower.Read().Plan != plan.ToString()) throw new InvalidOperationException("刷新期间其他程序切换了电源计划。");
            if (WindowsPower.Overlay() != overlay) throw new InvalidOperationException("刷新期间电源模式变化，未覆盖其他程序的选择。");
        }
    }
    public sealed class ProcessorPolicyChange
    {
        public ProcessorPolicyState Before { get; set; }
        public ProcessorPolicyState After { get; set; }
    }
    public static class ProcessorPolicyTransaction
    {
        private static Dictionary<string, int?> Values(ProcessorPolicyState state) { return new Dictionary<string, int?> { { "Minimum", state.Minimum }, { "Maximum", state.Maximum }, { "Boost", state.Boost }, { "Epp", state.Epp } }; }
        internal static bool KnownOverlay(Guid overlay) { return overlay == WindowsPower.Balanced || overlay == WindowsPower.Efficiency || overlay == WindowsPower.Performance; }
        public static void Validate(ProcessorPolicyState desired)
        {
            if (desired.Minimum.HasValue && (desired.Minimum < 0 || desired.Minimum > 100) || desired.Maximum.HasValue && (desired.Maximum < 1 || desired.Maximum > 100) || desired.Epp.HasValue && (desired.Epp < 0 || desired.Epp > 100) || desired.Boost.HasValue && (desired.Boost < 0 || desired.Boost > 6)) throw new ArgumentOutOfRangeException("desired", "CPU 策略值超出 Windows 范围。");
        }
        public static ProcessorPolicyChange Apply(IProcessorPolicy api, ProcessorPolicyState expected, ProcessorPolicyState desired)
        {
            Validate(desired); var before = api.Read(expected.AC);
            var old = Values(before); var proposed = Values(desired); var changes = proposed.Where(x => x.Value.HasValue && x.Value != old[x.Key]).ToList();
            if (before.Plan != expected.Plan || before.ActivePlan!=expected.ActivePlan || before.Overlay != expected.Overlay || Values(expected).Any(x => x.Value != old[x.Key])) throw new InvalidOperationException("电源计划 / 策略已变化，请刷新后重试。");
            if (desired.Plan != before.Plan || desired.ActivePlan!=before.ActivePlan || desired.AC != before.AC) throw new InvalidOperationException("目标计划或供电侧与读取值不符。");
            if (changes.Count != 0 && (!before.Overlay.HasValue || !KnownOverlay(before.Overlay.Value))) throw new InvalidOperationException("当前 Windows 电源模式不可恢复，未写入策略。");
            int? min = desired.Minimum ?? before.Minimum, max = desired.Maximum ?? before.Maximum;
            if (!min.HasValue || !max.HasValue || min > max) throw new InvalidOperationException("无法确认最小 / 最大处理器状态，或最小值大于最大值。");
            if (changes.Any(x => !old[x.Key].HasValue)) throw new InvalidOperationException("某项原值不可读，未执行不可回退写入。");
            var written = new List<KeyValuePair<string, int?>>();
            try
            {
                foreach (var item in changes) { var active = api.Read(expected.AC); if (active.Plan != before.Plan || active.ActivePlan!=before.ActivePlan || active.Overlay != before.Overlay) throw new InvalidOperationException("写入期间电源计划或模式变化。"); written.Add(item); api.Write(before.Plan, before.AC, item.Key, item.Value.Value); }
                if (changes.Count != 0) api.Activate(before.Plan);
                var after = api.Read(before.AC); if (after.Plan != before.Plan || after.ActivePlan!=before.ActivePlan || after.Overlay != before.Overlay || changes.Any(x => Values(after)[x.Key] != x.Value)) throw new InvalidOperationException("CPU 策略回读不符。");
                return new ProcessorPolicyChange { Before = before, After = after };
            }
            catch (Exception error)
            {
                var failed = new List<Exception>();
                foreach (var item in written.AsEnumerable().Reverse()) try { api.Write(before.Plan, before.AC, item.Key, old[item.Key].Value); } catch (Exception e) { failed.Add(e); }
                // Restore values in the original plan, but never switch back a plan
                // that another application selected while the transaction was running.
                try { var active=api.Read(before.AC); if (active.Plan == before.Plan && active.ActivePlan==before.ActivePlan && active.Overlay == before.Overlay && written.Count != 0) api.Activate(before.Plan); } catch (Exception e) { failed.Add(e); }
                try { var back=Values(api.ReadPlan(before.Plan,before.AC)); if (written.Any(x=>back[x.Key]!=old[x.Key])) throw new InvalidOperationException("CPU 原策略未通过恢复回读。"); } catch (Exception e) { failed.Add(e); }
                if (failed.Count != 0) { failed.Insert(0, error); throw new AggregateException("CPU 策略未完整恢复，请检查保存的原值。", failed); } throw;
            }
        }
    }
    public sealed class NvidiaSnapshot
    {
        public string Name { get; set; }
        public string Uuid { get; set; }
        public double? PowerWatts { get; set; }
        public double? PowerLimit { get; set; }
        public double? DefaultLimit { get; set; }
        public double? MinimumLimit { get; set; }
        public double? MaximumLimit { get; set; }
        public double? Temperature { get; set; }
        public double? Usage { get; set; }
        public double? CoreMHz { get; set; }
        public double? MemoryMHz { get; set; }
        public bool CanLimitPower { get { return PowerLimit.HasValue && MinimumLimit.HasValue && MaximumLimit.HasValue && MinimumLimit > 0 && MaximumLimit >= MinimumLimit && PowerLimit >= MinimumLimit && PowerLimit <= MaximumLimit; } }
    }
    public static class NvidiaPower
    {
        private static string Smi { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"); } }
        private static string Execute(string args)
        {
            if (!File.Exists(Smi)) throw new NotSupportedException("系统未安装 NVIDIA 查询工具。");
            using (var p = new Process { StartInfo = new ProcessStartInfo { FileName = Smi, Arguments = args, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } })
            {
                p.Start(); Task<string> output = p.StandardOutput.ReadToEndAsync(), error = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(8000)) { p.Kill(); throw new TimeoutException("NVIDIA 查询超时（仅结束本程序启动的查询进程）。"); }
                if (p.ExitCode != 0) throw new InvalidOperationException("NVIDIA 返回：" + error.Result.Trim()); return output.Result.Trim();
            }
        }
        public static NvidiaSnapshot Read() { string output = Execute("--query-gpu=name,uuid,power.draw,power.limit,power.default_limit,power.min_limit,power.max_limit,temperature.gpu,utilization.gpu,clocks.gr,clocks.mem --format=csv,noheader,nounits"); string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries); if (lines.Length != 1) throw new NotSupportedException("仅为单 NVIDIA GPU 启用此接口。"); return Parse(lines[0]); }
        public static NvidiaSnapshot Parse(string line)
        {
            string[] x = line.Split(',').Select(v => v.Trim()).ToArray(); if (x.Length != 11) throw new InvalidDataException("NVIDIA 数据列不符。");
            if (!System.Text.RegularExpressions.Regex.IsMatch(x[1], @"^GPU-[0-9a-fA-F-]{36}$")) throw new InvalidDataException("GPU 标识不符。");
            return new NvidiaSnapshot { Name=x[0], Uuid=x[1], PowerWatts=Number(x[2]), PowerLimit=Number(x[3]), DefaultLimit=Number(x[4]), MinimumLimit=Number(x[5]), MaximumLimit=Number(x[6]), Temperature=Number(x[7]), Usage=Number(x[8]), CoreMHz=Number(x[9]), MemoryMHz=Number(x[10]) };
        }
        private static double? Number(string value) { double n; return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !Double.IsInfinity(n) && !Double.IsNaN(n) && n >= 0 ? (double?)n : null; }
        public static void CheckLimit(NvidiaSnapshot gpu, double value) { if (!gpu.CanLimitPower) throw new NotSupportedException("驱动没有提供可回读的当前功率上限；不开放瓦数调节。"); if (value < gpu.MinimumLimit || value > gpu.MaximumLimit || Double.IsNaN(value) || Double.IsInfinity(value)) throw new ArgumentOutOfRangeException("value"); }
        public static void SetLimit(NvidiaSnapshot expected, double watts)
        {
            CheckLimit(expected, watts); var before = Read(); if (before.Uuid != expected.Uuid || before.PowerLimit != expected.PowerLimit) throw new InvalidOperationException("GPU 状态变化，请刷新后重试。"); CheckLimit(before, watts);
            string id = before.Uuid;
            try { Execute("-i " + id + " -pl " + watts.ToString("0.###", CultureInfo.InvariantCulture)); var after = Read(); if (after.Uuid != id || !after.PowerLimit.HasValue || Math.Abs(after.PowerLimit.Value - watts) > 0.5) throw new InvalidOperationException("GPU 功率上限回读不符。"); }
            catch (Exception error) { try { Execute("-i " + id + " -pl " + before.PowerLimit.Value.ToString("0.###", CultureInfo.InvariantCulture)); var back = Read(); if (back.Uuid != id || back.PowerLimit != before.PowerLimit) throw new InvalidOperationException("原 GPU 上限未恢复。"); } catch (Exception restore) { throw new AggregateException("GPU 上限未完整恢复。", error, restore); } throw; }
        }
    }
}
