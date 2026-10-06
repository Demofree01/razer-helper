using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RazerHelper
{
    public sealed class MacroBinding
    {
        public string MacroId { get; set; }
        public string Modifier { get; set; }
        public int Scan { get; set; }
        public bool Extended { get; set; }
        public bool Enabled { get; set; }
        public string Playback { get; set; }
        public int Repeat { get; set; }
        public MacroBinding() { Modifier = "Fn"; Playback = "Once"; Repeat = 1; }
        public void Validate(IEnumerable<MacroDefinition> macros)
        {
            if (!macros.Any(x => String.Equals(x.Id, MacroId, StringComparison.OrdinalIgnoreCase))) throw new System.IO.InvalidDataException("绑定的宏不存在。");
            if (Modifier != "Fn" && Modifier != "CtrlAlt") throw new System.IO.InvalidDataException("宏绑定修饰键不受支持。");
            if (Scan < 1 || Scan > 0x7f || Scan == 0x58 || new[] { 0x1d, 0x2a, 0x36, 0x38, 0x5b, 0x5c }.Contains(Scan)) throw new System.IO.InvalidDataException("不能绑定修饰键或停止键 F12。");
            if (!new[] { "Once", "Repeat", "WhileHeld", "Toggle" }.Contains(Playback) || Repeat < 1 || Repeat > 1000) throw new System.IO.InvalidDataException("播放方式 / 次数无效。");
            if (Modifier == "CtrlAlt" && Playback == "WhileHeld") throw new System.IO.InvalidDataException("Ctrl+Alt 需释放修饰键后播放，请选择播放一次、指定次数或再次按下停止。");
        }
        public override string ToString() { string mode=Playback=="Once"?"一次":Playback=="Repeat"?Repeat+" 次":Playback=="WhileHeld"?"按住播放":"再次按下停止"; return (Enabled ? "已启用 · " : "未启用 · ") + (Modifier=="CtrlAlt"?"Ctrl+Alt":Modifier) + " + " + new MacroEvent { Kind = "Key", Code = Scan, Extended = Extended }.Description().Replace(" · 按下", "") + " · " + mode; }
    }
    public sealed class FnSignal
    {
        public string Device { get; set; }
        public string Down { get; set; }
        public string Up { get; set; }
        public void Validate()
        {
            if (String.IsNullOrEmpty(Device) || Device.IndexOf("vid_1532&pid_02c5", StringComparison.OrdinalIgnoreCase) < 0 || Device.Length > 1024 || Down == Up || !Hex(Down) || !Hex(Up)) throw new System.IO.InvalidDataException("Fn 信号需来自本机 Blade，并有不同的按下与释放报文。");
        }
        private static bool Hex(string x) { return x != null && x.Length >= 2 && x.Length <= 1024 && x.Length % 2 == 0 && x.All(c => Uri.IsHexDigit(c)); }
    }
    public sealed class FnState
    {
        private long expires, began;
        public void Observe(FnSignal signal, string device, string report, long now)
        {
            if (signal == null || !String.Equals(signal.Device, device, StringComparison.OrdinalIgnoreCase)) return;
            if (String.Equals(signal.Up, report, StringComparison.OrdinalIgnoreCase)) expires = 0;
            else if (String.Equals(signal.Down, report, StringComparison.OrdinalIgnoreCase)) { began=now; expires = now + 2000; }
        }
        public bool Active(long now) { return now >= began && now < expires; }
        public void Reset() { expires = 0; }
    }
    public sealed class RawSignal
    {
        public string Device, Report;
        public int Seen=1;
        public override string ToString() { return Report + " · " + Seen+" 次 · " + (Device.IndexOf("mi_03", StringComparison.OrdinalIgnoreCase) >= 0 ? "MI_03" : "内置键盘"); }
    }
    public sealed class RawFnReader : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
        [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint header);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder name, ref uint size);
        private readonly IntPtr window;
        private readonly Device[] registrations;
        public event Action<RawSignal> Signal;
        public RawFnReader(IntPtr window)
        {
            this.window = window;
            var list=new List<Device>{new Device{Page=1,Usage=6,Flags=0x100,Target=window}};
            foreach(var endpoint in HidDiscovery.Enumerate().Where(x=>x.Product==0x02c5 && x.InputLength>0 && (x.UsagePage==0x59 || x.UsagePage==12 || x.UsagePage==1 && x.Usage==128)))
                if(!list.Any(x=>x.Page==endpoint.UsagePage && x.Usage==endpoint.Usage)) list.Add(new Device{Page=(ushort)endpoint.UsagePage,Usage=(ushort)endpoint.Usage,Flags=0x100,Target=window});
            registrations=list.ToArray();
            // INPUTSINK observes reports without changing device mode or suppressing legacy input.
            if (!RegisterRawInputDevices(registrations, (uint)registrations.Length, (uint)Marshal.SizeOf(typeof(Device)))) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法注册 Fn 原始输入通道");
        }
        public void Message(IntPtr input)
        {
            uint size = 0, header = (uint)(IntPtr.Size == 8 ? 24 : 16); GetRawInputData(input, 0x10000003, IntPtr.Zero, ref size, header);
            if (size < header + 8 || size > 65536) return; IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(input, 0x10000003, buffer, ref size, header) != size) return;
                int type=Marshal.ReadInt32(buffer); if(type!=1 && type!=2)return;
                IntPtr device = Marshal.ReadIntPtr(buffer, 8); uint length = 1024; var name = new StringBuilder(1024);
                if (GetRawInputDeviceInfo(device, 0x20000007, name, ref length) == UInt32.MaxValue) return;
                string path = name.ToString(); if (path.IndexOf("vid_1532&pid_02c5", StringComparison.OrdinalIgnoreCase) < 0) return;
                if(type==1)
                {
                    if(size<header+16)return;
                    byte[] key=new byte[7];key[0]=0xfe;
                    Marshal.Copy(IntPtr.Add(buffer,(int)header),key,1,4);Marshal.Copy(IntPtr.Add(buffer,(int)header+6),key,5,2);
                    var handler=Signal;if(handler!=null)handler(new RawSignal{Device=path,Report=BitConverter.ToString(key).Replace("-","")});return;
                }
                int reportSize = Marshal.ReadInt32(buffer, (int)header), count = Marshal.ReadInt32(buffer, (int)header + 4);
                if (reportSize < 1 || reportSize > 512 || count < 1 || count > 64 || (long)header + 8 + (long)reportSize * count > size) return;
                for (int i = 0; i < count; i++) { var report = new byte[reportSize]; Marshal.Copy(IntPtr.Add(buffer, (int)header + 8 + i * reportSize), report, 0, report.Length); var handler = Signal; if (handler != null) handler(new RawSignal { Device = path, Report = BitConverter.ToString(report).Replace("-", "") }); }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        public void Dispose() { RegisterRawInputDevices(registrations.Select(x=>new Device{Page=x.Page,Usage=x.Usage,Flags=1,Target=IntPtr.Zero}).ToArray(), (uint)registrations.Length, (uint)Marshal.SizeOf(typeof(Device))); GC.KeepAlive(window); }
    }
    public sealed class MacroBindingEngine : IDisposable
    {
        private delegate IntPtr Callback(int code, IntPtr wp, IntPtr lp);
        [StructLayout(LayoutKind.Sequential)] private struct Key { public uint Vk, Scan, Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, Callback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wp, IntPtr lp);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        private readonly Settings settings; private readonly MacroEngine engine; private readonly SynchronizationContext context;
        private readonly FnState fn = new FnState(); private readonly Dictionary<string, MacroBinding> held = new Dictionary<string, MacroBinding>();
        private readonly Callback callback; private IntPtr hook; private readonly RawFnReader raw; private string running; private bool disposed;
        private readonly System.Windows.Forms.Timer fnExpiry;
        public event Action<string> Notice;
        public MacroBindingEngine(IntPtr window, Settings settings, MacroEngine engine)
        {
            this.settings=settings; this.engine=engine; context=SynchronizationContext.Current ?? new SynchronizationContext(); callback=KeyEvent;
            if (settings.MacroBindings.Any(x => x.Enabled && x.Modifier == "Fn") && settings.FnSignal != null)
            {
                raw = new RawFnReader(window); raw.Signal += delegate(RawSignal signal) { fn.Observe(settings.FnSignal, signal.Device, signal.Report, Environment.TickCount & Int32.MaxValue); StopExpiredFn(); };
                fnExpiry=new System.Windows.Forms.Timer{Interval=100};fnExpiry.Tick+=delegate{StopExpiredFn();};fnExpiry.Start();
            }
            if (settings.MacroBindings.Any(x => x.Enabled && (x.Modifier != "Fn" || settings.FnSignal != null)))
            { hook=SetWindowsHookEx(13, callback, GetModuleHandle(null), 0); if(hook==IntPtr.Zero){if(raw!=null)raw.Dispose();if(fnExpiry!=null)fnExpiry.Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法启用宏绑定");} }
        }
        public void Message(IntPtr input) { if (raw != null) raw.Message(input); }
        private void StopExpiredFn()
        {
            if(!fn.Active(Environment.TickCount & Int32.MaxValue) && held.Values.Any(x=>x.Modifier=="Fn" && x.Playback=="WhileHeld" && x.MacroId==running))engine.Stop();
        }
        private IntPtr KeyEvent(int code, IntPtr wp, IntPtr lp)
        {
            if(code<0 || disposed) return CallNextHookEx(hook,code,wp,lp); var key=(Key)Marshal.PtrToStructure(lp,typeof(Key));
            if((key.Flags&0x12)!=0) return CallNextHookEx(hook,code,wp,lp);
            string id=key.Scan+":"+((key.Flags&1)!=0); bool up=(key.Flags&0x80)!=0; MacroBinding bound;
            if (held.TryGetValue(id,out bound))
            {
                if(up){held.Remove(id); if(bound.Playback=="WhileHeld" && running==bound.MacroId) engine.Stop(); else if(bound.Playback!="Toggle" && bound.Playback!="WhileHeld") Start(bound); }
                return new IntPtr(1);
            }
            bool desktop=BacklightMonitor.InteractiveDesktop();
            if(up || engine.Recording || !desktop) { if(!desktop)fn.Reset(); return CallNextHookEx(hook,code,wp,lp); }
            bound=settings.MacroBindings.FirstOrDefault(x=>x.Enabled && x.Scan==key.Scan && x.Extended==((key.Flags&1)!=0) && (x.Modifier=="Fn" ? fn.Active(Environment.TickCount & Int32.MaxValue) : Native.GetAsyncKeyState(0x11)<0 && Native.GetAsyncKeyState(0x12)<0));
            if(bound==null) return CallNextHookEx(hook,code,wp,lp); held[id]=bound;
            if(bound.Playback=="WhileHeld" || bound.Playback=="Toggle") Start(bound); return new IntPtr(1);
        }
        private void Start(MacroBinding binding)
        {
            context.Post(delegate
            {
                try
                {
                    if(disposed || !BacklightMonitor.InteractiveDesktop())return;
                    if(binding.Playback=="WhileHeld" && (!held.Values.Contains(binding) || binding.Modifier=="Fn" && !fn.Active(Environment.TickCount & Int32.MaxValue)))return;
                    if(binding.Playback=="Toggle" && running==binding.MacroId && engine.Playing){engine.Stop();running=null;return;}
                    var original=settings.Macros.First(x=>String.Equals(x.Id,binding.MacroId,StringComparison.OrdinalIgnoreCase)); var macro=Store.Serializer().Deserialize<MacroDefinition>(Store.Serializer().Serialize(original));
                    int repeat=binding.Playback=="Repeat" ? binding.Repeat : 1;
                    if(binding.Playback=="Toggle" || binding.Playback=="WhileHeld")
                    {
                        var plan=MacroPlan.Compile(macro,settings.Macros); long delay=plan.Sum(x=>(long)x.DelayMs);
                        repeat=(int)Math.Min(1000,Math.Min(100000 / Math.Max(1,plan.Count),macro.MaxRunSeconds*1000L / Math.Max(1,delay)));
                    }
                    if(repeat!=1) macro.Events=new List<MacroEvent>{new MacroEvent{Kind="Loop",Repeat=repeat,Children=macro.Events}};
                    var library=settings.Macros.Where(x=>x.Id!=macro.Id).Concat(new[]{macro}).ToList(); engine.Play(macro,library,false);running=macro.Id;
                }
                catch(Exception e){string message="宏绑定未启动："+e.Message;Log.Write(message);var handler=Notice;if(handler!=null)handler(message);}
            },null);
        }
        public void Dispose(){if(disposed)return;disposed=true;if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}if(raw!=null)raw.Dispose();if(fnExpiry!=null)fnExpiry.Dispose();if(running!=null && engine.Playing)engine.Stop();held.Clear();fn.Reset();}
    }
}
