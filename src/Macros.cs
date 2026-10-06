using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace RazerHelper
{
    public sealed partial class MacroEvent
    {
        public string Kind { get; set; }
        public int Code { get; set; }
        public bool Extended { get; set; }
        public bool Up { get; set; }
        public int DelayMs { get; set; }
        public MacroEvent Release() { return new MacroEvent { Kind = Kind, Code = Code, Extended = Extended, Up = true }; }
        public string Identity { get { return Kind + ":" + Code + ":" + Extended; } }
        public string Description()
        {
            string advanced = AdvancedDescription(); if (advanced != null) return advanced;
            if (Kind == "Wheel") return "滚轮 " + (Code > 0 ? "向上 " : "向下 ") + Math.Abs(Code / 120) + " 格";
            string name;
            if (Kind == "Key")
            {
                var text = new System.Text.StringBuilder(128);
                Native.GetKeyNameText((Code << 16) | (Extended ? (1 << 24) : 0), text, 128);
                name = text.Length == 0 ? "键 " + Code.ToString("X2") : text.ToString();
            }
            else name = Code == 1 ? "鼠标左键" : Code == 2 ? "鼠标右键" : Code == 3 ? "鼠标中键" : Code == 4 ? "鼠标侧键 1" : "鼠标侧键 2";
            return name + (Up ? " · 释放" : " · 按下");
        }
    }
    public sealed partial class MacroDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string TargetProcess { get; set; }
        public int HotKey { get; set; }
        public bool AllowExternalActions { get; set; }
        public bool AllowClipboard { get; set; }
        public int MaxRunSeconds { get; set; }
        public List<MacroEvent> Events { get; set; }
        public MacroDefinition() { Id = Guid.NewGuid().ToString("N"); Name = "新宏"; TargetProcess = ""; MaxRunSeconds = 60; Events = new List<MacroEvent>(); }
        public void Validate()
        {
            Guid id; if (!Guid.TryParse(Id, out id)) throw new InvalidDataException("宏标识无效。");
            if (String.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(Char.IsControl)) throw new InvalidDataException("宏名称必须为 1–80 个字符。");
            if (TargetProcess == null || TargetProcess.Length > 128 || TargetProcess.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("目标程序必须为文件名。");
            if (HotKey != 0 && (HotKey < 0x75 || HotKey > 0x7a)) throw new InvalidDataException("快捷键只允许 Ctrl+Alt+F6–F11。");
            if (Events == null || Events.Count == 0 || Events.Count > 4096) throw new InvalidDataException("宏事件数必须为 1–4096。");
            if (MaxRunSeconds < 1 || MaxRunSeconds > 600) throw new InvalidDataException("宏运行上限为 1–600 秒。");
            int count = 0; long duration = 0;
            foreach (var e in Events)
            {
                ValidateAdvanced(e, 0, ref count);
                if (e == null || e.DelayMs < 0 || e.DelayMs > 60000) throw new InvalidDataException("单次延时必须为 0–60000 毫秒。");
                duration += e.DelayMs;
                switch (e.Kind)
                {
                    case "Key": if (e.Code < 1 || e.Code > 0x7f || e.Code == 0x58) throw new InvalidDataException("无效扫描码，F12 保留为停止键。"); break;
                    case "Mouse": if (e.Code < 1 || e.Code > 5 || e.Extended) throw new InvalidDataException("无效鼠标按钮。"); break;
                    case "Wheel": if (e.Code == 0 || e.Code < -1200 || e.Code > 1200 || e.Code % 120 != 0 || e.Up || e.Extended) throw new InvalidDataException("无效滚轮事件。"); break;
                    default: break; // Advanced event validation also checks nested input events.
                }
            }
            if (duration > MaxRunSeconds * 1000L) throw new InvalidDataException("宏延时超过所选运行上限。");
        }
        public override string ToString() { return Name + " · " + Events.Count + " 步" + (HotKey == 0 ? "" : " · Ctrl+Alt+F" + (HotKey - 0x6f)); }
    }
    public static class MacroImport
    {
        public static MacroDefinition Read(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("宏文件超过 1 MB。");
            MacroDefinition macro;
            if (String.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase)) macro = Xml(File.ReadAllText(path));
            else
            {
                string json = File.ReadAllText(path);
                var objectValue = Store.Serializer().DeserializeObject(json) as Dictionary<string, object>;
                macro = objectValue != null && objectValue.ContainsKey("appEngine") ? SynapseImport.ConvertMacro(objectValue) : Store.Serializer().Deserialize<MacroDefinition>(json);
            }
            if (macro == null) throw new InvalidDataException("宏文件为空。");
            macro.Id = Guid.NewGuid().ToString("N"); macro.HotKey = 0; macro.AllowExternalActions = false; macro.AllowClipboard = false; macro.Validate(); return macro;
        }
        public static MacroDefinition Xml(string text)
        {
            if (text.Length > 1024 * 1024) throw new InvalidDataException("宏 XML 过大。");
            var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
            XDocument document;
            using (var reader = XmlReader.Create(new StringReader(text), options)) document = XDocument.Load(reader);
            if (document.Root == null || document.Root.Name.LocalName != "Macro") throw new InvalidDataException("需要雷云导出的 Macro XML。");
            var macro = new MacroDefinition { Name = Value(document.Root, "Name", "导入的宏") };
            var events = document.Root.Elements().SingleOrDefault(x => x.Name.LocalName == "MacroEvents");
            if (events == null) throw new InvalidDataException("宏 XML 缺少 MacroEvents。");
            foreach (var item in events.Elements())
            {
                if (item.Name.LocalName != "MacroEvent") throw new InvalidDataException("未知宏事件结构。");
                int type = Number(item, "Type", -1); int delay = Number(item, "Delay", 0); var action = new MacroEvent { DelayMs = delay };
                if (type == 1)
                {
                    var key = item.Elements().SingleOrDefault(x => x.Name.LocalName == "KeyEvent" || x.Name.LocalName == "KeyboardEvent");
                    if (key == null) throw new InvalidDataException("缺少 KeyEvent。");
                    int scan = Number(key, "Makecode", -1); int state = Number(key, "State", 0);
                    if (state != 0 && state != 1) throw new InvalidDataException("键盘 State 必须为 0 或 1。");
                    if ((scan & 0xff00) == 0xe000) { action.Extended = true; scan &= 0xff; }
                    else if (scan >= 0x100 && scan <= 0x17f) { action.Extended = true; scan -= 0x100; }
                    action.Kind = "Key"; action.Code = scan; action.Up = state == 1;
                }
                else if (type == 2)
                {
                    var mouse = item.Elements().SingleOrDefault(x => x.Name.LocalName == "MouseEvent");
                    if (mouse == null || mouse.Elements().Any(x => x.Name.LocalName != "MouseButton" && x.Name.LocalName != "State")) throw new InvalidDataException("此鼠标事件格式不受支持。");
                    int state = Number(mouse, "State", 0); if (state != 0 && state != 1) throw new InvalidDataException("鼠标 State 必须为 0 或 1。");
                    action.Kind = "Mouse"; action.Code = Number(mouse, "MouseButton", -1); action.Up = state == 1;
                }
                else throw new InvalidDataException("此雷云 XML 包含尚未支持的事件类型 " + type + "，整个导入已取消。");
                macro.Events.Add(action);
            }
            macro.Validate(); return macro;
        }
        private static string Value(XElement element, string name, string fallback)
        {
            var nodes = element.Elements().Where(x => x.Name.LocalName == name).ToArray();
            if (nodes.Length > 1) throw new InvalidDataException("XML 字段重复：" + name);
            return nodes.Length == 0 ? fallback : nodes[0].Value;
        }
        private static int Number(XElement element, string name, int fallback)
        {
            string text = Value(element, name, fallback.ToString()); int value;
            if (!Int32.TryParse(text, out value)) throw new InvalidDataException("无效 XML 数字：" + name); return value;
        }
    }
    public static class MacroRunner
    {
        public static void Run(MacroDefinition macro, CancellationToken token, Func<bool> focus, Action<MacroEvent> send)
        {
            Run(macro, new[] { macro }, token, focus, send, null, null);
        }
        public static void Run(MacroDefinition macro, IEnumerable<MacroDefinition> library, CancellationToken token, Func<bool> focus, Action<MacroEvent> send, Action<MacroEvent> external, Action<int> simulatedWait)
        {
            var plan = MacroPlan.Compile(macro, library); var held = new Dictionary<string, MacroEvent>(); var clock = Stopwatch.StartNew();
            try
            {
                foreach (var e in plan)
                {
                    int remaining = e.DelayMs;
                    do
                    {
                        token.ThrowIfCancellationRequested();
                        if (clock.ElapsedMilliseconds > macro.MaxRunSeconds * 1000L) throw new OperationCanceledException("已达到宏运行时长上限。");
                        if (!focus()) throw new OperationCanceledException("目标窗口发生变化，宏已停止。");
                        int chunk = Math.Min(20, remaining);
                        if (chunk > 0) { if (simulatedWait != null) simulatedWait(chunk); else if (token.WaitHandle.WaitOne(chunk)) token.ThrowIfCancellationRequested(); }
                        remaining -= chunk;
                    } while (remaining > 0);
                    token.ThrowIfCancellationRequested();
                    if (!focus()) throw new OperationCanceledException("目标窗口发生变化，宏已停止。");
                    if (e.Kind == "Delay") continue;
                    if (e.Kind == "Launch" || e.Kind == "Command" || e.Kind == "Clipboard") { if (external == null) throw new InvalidOperationException("缺少外部动作执行器。"); external(e); continue; }
                    if (e.Kind == "Text")
                    {
                        foreach (char character in e.Text) { token.ThrowIfCancellationRequested(); if (clock.ElapsedMilliseconds > macro.MaxRunSeconds * 1000L) throw new OperationCanceledException("已达到宏运行时长上限。"); if (!focus()) throw new OperationCanceledException("目标窗口发生变化。"); var unicode = new MacroEvent { Kind = "Unicode", Code = character }; held[unicode.Identity] = unicode; send(unicode); send(unicode.Release()); held.Remove(unicode.Identity); }
                        continue;
                    }
                    if (e.Kind == "Key" || e.Kind == "Mouse")
                    {
                        if (!e.Up) held[e.Identity] = e;
                        send(e); if (e.Up) held.Remove(e.Identity);
                    }
                    else send(e);
                }
            }
            finally
            {
                // Release only inputs this playback pressed, including after focus loss or SendInput failure.
                var failures = new List<Exception>();
                foreach (var e in held.Values.Reverse()) { try { send(e.Release()); } catch (Exception error) { failures.Add(error); } }
                if (failures.Count != 0) { Log.Write("宏清理失败：" + failures[0].Message); throw new AggregateException("Windows 拒绝释放部分模拟输入。", failures); }
            }
        }
    }
    public sealed partial class MacroEngine : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wparam, IntPtr lparam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [StructLayout(LayoutKind.Sequential)] private struct KeyHook { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseHook { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        private IntPtr keyboard, mouse;
        private HookProc keyCallback, mouseCallback;
        private readonly SynchronizationContext context;
        private readonly Stopwatch recordingClock = new Stopwatch();
        private readonly HashSet<string> recordHeld = new HashSet<string>();
        private MacroDefinition recording;
        private long lastEvent;
        private readonly System.Windows.Forms.Timer limitTimer;
        private CancellationTokenSource cancel;
        private Task playback;
        private volatile bool disposed;
        private int playSerial;
        public event Action<MacroDefinition> Recorded;
        public event Action<string> Notice;
        public bool Recording { get { return recording != null; } }
        public bool Playing { get { return playback != null && !playback.IsCompleted; } }
        public MacroEngine()
        {
            context = SynchronizationContext.Current ?? new SynchronizationContext();
            limitTimer = new System.Windows.Forms.Timer { Interval = 500 };
            limitTimer.Tick += delegate { if (recording != null && recordingClock.ElapsedMilliseconds >= 59000) StopRecording(); };
        }
        public void Record(string name, string target)
        {
            if(disposed)throw new ObjectDisposedException("MacroEngine");
            if (Recording || Playing) throw new InvalidOperationException("已有录制或播放正在进行。");
            recording = new MacroDefinition { Name = name, TargetProcess = target ?? "" }; lastEvent = 0; recordHeld.Clear();
            keyCallback = KeyboardCallback; mouseCallback = MouseCallback;
            IntPtr module = GetModuleHandle(null);
            keyboard = SetWindowsHookEx(13, keyCallback, module, 0);
            mouse = SetWindowsHookEx(14, mouseCallback, module, 0);
            if (keyboard == IntPtr.Zero || mouse == IntPtr.Zero)
            {
                StopHooks(); recording = null; throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法启动录制");
            }
            recordingClock.Restart(); limitTimer.Start();
        }
        private void Append(MacroEvent e)
        {
            if (recording == null) return;
            if (recording.Events.Count >= 4000) { context.Post(delegate { StopRecording(); }, null); return; }
            if (e.Kind != "Wheel")
            {
                if (e.Up) { if (!recordHeld.Remove(e.Identity)) return; } else recordHeld.Add(e.Identity);
            }
            long now = recordingClock.ElapsedMilliseconds;
            e.DelayMs = (int)Math.Min(60000, now - lastEvent); lastEvent = now; recording.Events.Add(e);
        }
        private IntPtr KeyboardCallback(int code, IntPtr wp, IntPtr lp)
        {
            if (code >= 0 && recording != null)
            {
                var data = (KeyHook)Marshal.PtrToStructure(lp, typeof(KeyHook));
                if ((data.Flags & 0x12) == 0)
                {
                    if (data.Key == 0x1b || data.Key == 0x7b) { context.Post(delegate { StopRecording(); }, null); return new IntPtr(1); }
                    if (data.Scan >= 1 && data.Scan <= 0x7f) Append(new MacroEvent { Kind = "Key", Code = (int)data.Scan, Extended = (data.Flags & 1) != 0, Up = (data.Flags & 0x80) != 0 });
                }
            }
            return CallNextHookEx(keyboard, code, wp, lp);
        }
        private IntPtr MouseCallback(int code, IntPtr wp, IntPtr lp)
        {
            if (code >= 0 && recording != null)
            {
                var data = (MouseHook)Marshal.PtrToStructure(lp, typeof(MouseHook));
                if ((data.Flags & 3) == 0)
                {
                    int message = wp.ToInt32(); int button = 0; bool up = false;
                    if (message == 0x201 || message == 0x202) { button = 1; up = message == 0x202; }
                    else if (message == 0x204 || message == 0x205) { button = 2; up = message == 0x205; }
                    else if (message == 0x207 || message == 0x208) { button = 3; up = message == 0x208; }
                    else if (message == 0x20b || message == 0x20c) { button = ((data.Data >> 16) == 1) ? 4 : 5; up = message == 0x20c; }
                    else if (message == 0x20a) { int delta = (short)(data.Data >> 16); if (delta != 0 && delta % 120 == 0 && Math.Abs(delta) <= 1200) Append(new MacroEvent { Kind = "Wheel", Code = delta }); }
                    if (button != 0) Append(new MacroEvent { Kind = "Mouse", Code = button, Up = up });
                }
            }
            return CallNextHookEx(mouse, code, wp, lp);
        }
        private void StopHooks()
        {
            if (keyboard != IntPtr.Zero) { UnhookWindowsHookEx(keyboard); keyboard = IntPtr.Zero; }
            if (mouse != IntPtr.Zero) { UnhookWindowsHookEx(mouse); mouse = IntPtr.Zero; }
            limitTimer.Stop(); recordingClock.Stop();
        }
        public void StopRecording()
        {
            if (recording == null) return; StopHooks(); var result = recording; recording = null;
            if (result.Events.Count == 0) { Tell("没有录到事件。"); return; }
            try { result.Validate(); var handler = Recorded; if (handler != null) handler(result); }
            catch (Exception e) { Tell("录制未保存：" + e.Message); }
        }
        public static string ForegroundProcess()
        {
            uint pid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            try { using (var process = Process.GetProcessById((int)pid)) return process.ProcessName + ".exe"; } catch { return ""; }
        }
        private void Tell(string message)
        {
            context.Post(delegate { var handler = Notice; if (handler != null) handler(message); }, null);
        }
        public void Play(MacroDefinition macro, bool countdown)
        {
            Play(macro, new[] { macro }, countdown);
        }
        public void Play(MacroDefinition macro, IEnumerable<MacroDefinition> library, bool countdown)
        {
            if(disposed)throw new ObjectDisposedException("MacroEngine");
            // Freeze the macro library before another UI edit can change a running plan.
            var snapshot = Store.Serializer().Deserialize<List<MacroDefinition>>(Store.Serializer().Serialize(library.ToList()));
            macro = snapshot.First(x => x.Id == macro.Id); MacroPlan.Compile(macro, snapshot);
            if (Playing || Recording) throw new InvalidOperationException("已有宏正在运行。");
            RestoreClipboardNow();int serial=++playSerial;
            if (cancel != null) cancel.Dispose(); cancel = new CancellationTokenSource(); var token = cancel.Token;
            playback = Task.Run(delegate
            {
                try
                {
                    if (countdown)
                    {
                        Tell("3 秒后播放，请切换到目标窗口。F12 停止。");
                        for (int i = 0; i < 150; i++) { if (token.WaitHandle.WaitOne(20)) token.ThrowIfCancellationRequested(); if (Native.GetAsyncKeyState(0x7b) < 0) throw new OperationCanceledException("F12"); }
                    }
                    // Wait for all shortcut modifiers to be released before injecting a macro.
                    for (int i = 0; i < 100 && new[] { 0x11, 0x12, 0x10 }.Any(x => Native.GetAsyncKeyState(x) < 0); i++)
                        if (token.WaitHandle.WaitOne(20)) token.ThrowIfCancellationRequested();
                    if (new[] { 0x11, 0x12, 0x10 }.Any(x => Native.GetAsyncKeyState(x) < 0)) throw new OperationCanceledException("快捷键仍被按住。");
                    IntPtr window = Native.GetForegroundWindow();
                    if (window == IntPtr.Zero) throw new OperationCanceledException("没有前台窗口。");
                    uint targetPid;Native.GetWindowThreadProcessId(window,out targetPid);
                    if(targetPid==(uint)Process.GetCurrentProcess().Id)throw new OperationCanceledException("当前仍是 Helper 窗口；请在倒计时内切换到要输入的窗口后重试。");
                    if (macro.TargetProcess.Length != 0 && !String.Equals(macro.TargetProcess, ForegroundProcess(), StringComparison.OrdinalIgnoreCase)) throw new OperationCanceledException("当前窗口不是宏指定的目标程序。");
                    MacroRunner.Run(macro, snapshot, token, delegate { return Native.GetForegroundWindow() == window && Native.GetAsyncKeyState(0x7b) >= 0 && BacklightMonitor.InteractiveDesktop(); }, Send, ExternalAction, null);
                    Tell("宏动作已发送。");
                }
                catch (OperationCanceledException e) { Tell("宏已停止。" + e.Message); }
                catch (Exception e) { Tell("宏播放失败：" + e.Message); }
                finally { RestoreClipboard(serial); }
            });
        }
        internal static Native.Input Encode(MacroEvent e)
        {
            var input = new Native.Input();
            if (e.Kind == "Key" || e.Kind == "Unicode")
            {
                input.Type = 1; input.Data.Keyboard.Scan = (ushort)e.Code; input.Data.Keyboard.Flags = (e.Kind == "Unicode" ? 4u : 8u) | (e.Up ? 2u : 0u) | (e.Extended ? 1u : 0u); input.Data.Keyboard.Extra = new UIntPtr(0x52485f31);
            }
            else
            {
                input.Type = 0; input.Data.Mouse.Extra = new UIntPtr(0x52485f31);
                if (e.Kind == "Move")
                {
                    var area = System.Windows.Forms.SystemInformation.VirtualScreen;
                    if (e.Absolute) { if (!area.Contains(e.X, e.Y)) throw new InvalidOperationException("鼠标坐标不在当前屏幕范围内。"); input.Data.Mouse.X = (int)((e.X - area.Left) * 65535L / Math.Max(1, area.Width - 1)); input.Data.Mouse.Y = (int)((e.Y - area.Top) * 65535L / Math.Max(1, area.Height - 1)); input.Data.Mouse.Flags = 0xc001; }
                    else { input.Data.Mouse.X = e.X; input.Data.Mouse.Y = e.Y; input.Data.Mouse.Flags = 1; }
                }
                else if (e.Kind == "Wheel") { input.Data.Mouse.Flags = 0x800; input.Data.Mouse.Data = unchecked((uint)e.Code); }
                else
                {
                    input.Data.Mouse.Flags = e.Code == 1 ? (e.Up ? 4u : 2u) : e.Code == 2 ? (e.Up ? 16u : 8u) : e.Code == 3 ? (e.Up ? 64u : 32u) : (e.Up ? 256u : 128u);
                    if (e.Code >= 4) input.Data.Mouse.Data = (uint)(e.Code - 3);
                }
            }
            return input;
        }
        private static void Send(MacroEvent e)
        {
            var input = Encode(e);
            if (Native.SendInput(1, new[] { input }, Marshal.SizeOf(input)) != 1) throw new InvalidOperationException("Windows 拒绝模拟输入；目标程序可能具有更高权限。");
        }
        public void Stop() { if (cancel != null) cancel.Cancel(); StopRecording(); }
        public void Dispose() { if(disposed)return;disposed=true;Stop();RestoreClipboardNow();if (playback != null) try { playback.Wait(1500); } catch { } limitTimer.Dispose(); }
    }
}
