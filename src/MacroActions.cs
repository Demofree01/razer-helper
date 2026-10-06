using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace RazerHelper
{
    public sealed partial class MacroEvent
    {
        public string Text { get; set; }
        public string ActionPath { get; set; }
        public string Arguments { get; set; }
        public string MacroId { get; set; }
        public int Repeat { get; set; }
        public List<MacroEvent> Children { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool Absolute { get; set; }
        public MacroEvent() { Text = ""; ActionPath = ""; Arguments = ""; Repeat = 1; }
        private string AdvancedDescription()
        {
            switch (Kind)
            {
                case "Delay": return "延迟 " + DelayMs + " ms";
                case "Text": return "输入文本（" + Text.Length + " 字符，预览隐藏）";
                case "Clipboard": return "设置剪贴板文本（" + Text.Length + " 字符，预览隐藏）";
                case "Move": return (Absolute ? "鼠标绝对坐标 " : "鼠标相对移动 ") + X + ", " + Y;
                case "Launch": return "启动：" + Path.GetFileName(ActionPath);
                case "Command": return "命令：" + Path.GetFileName(ActionPath) + "（参数隐藏）";
                case "Macro": return "调用宏 " + MacroId;
                case "Loop": return "循环 " + Repeat + " 次 · " + (Children == null ? 0 : Children.Count) + " 个动作";
                default: return null;
            }
        }
    }
    public sealed partial class MacroDefinition
    {
        private static void ValidateAdvanced(MacroEvent e, int depth, ref int count)
        {
            if (e == null || depth > 8 || ++count > 4096 || e.DelayMs < 0 || e.DelayMs > 60000) throw new InvalidDataException("宏动作、嵌套深度或延时超出范围。");
            if (e.Kind != "Loop" && e.Children != null && e.Children.Count != 0) throw new InvalidDataException("只有循环可包含子动作。");
            switch (e.Kind)
            {
                case "Key": if (e.Code < 1 || e.Code > 0x7f || e.Code == 0x58) throw new InvalidDataException("无效扫描码，F12 为停止键。"); break;
                case "Mouse": if (e.Code < 1 || e.Code > 5 || e.Extended) throw new InvalidDataException("无效鼠标按钮。"); break;
                case "Wheel": if (e.Code == 0 || Math.Abs((long)e.Code) > 1200 || e.Code % 120 != 0 || e.Up || e.Extended) throw new InvalidDataException("无效滚轮事件。"); break;
                case "Delay": break;
                case "Text": case "Clipboard": if (e.Text == null || e.Text.Length == 0 || e.Text.Length > 8192 || e.Text.IndexOf('\0') >= 0) throw new InvalidDataException("文本长度须为 1–8192 字符。"); break;
                case "Move": if (Math.Abs((long)e.X) > 65535 || Math.Abs((long)e.Y) > 65535) throw new InvalidDataException("鼠标坐标超出范围。"); break;
                case "Launch": case "Command":
                    if (String.IsNullOrWhiteSpace(e.ActionPath) || e.ActionPath.Length > 1024 || e.ActionPath.Any(Char.IsControl) || !Path.IsPathRooted(e.ActionPath) || !String.Equals(Path.GetFullPath(e.ActionPath),e.ActionPath.Replace('/','\\'),StringComparison.OrdinalIgnoreCase) || !String.Equals(Path.GetExtension(e.ActionPath), ".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("启动 / 命令须指定完整绝对路径的 EXE；不会自动寻找或调用 shell。");
                    if (e.Arguments == null || e.Arguments.Length > 4096 || e.Arguments.IndexOf('\0') >= 0) throw new InvalidDataException("命令参数无效。"); break;
                case "Macro": Guid id; if (!Guid.TryParse(e.MacroId, out id)) throw new InvalidDataException("被调用宏的标识无效。"); break;
                case "Loop":
                    if (e.Repeat < 1 || e.Repeat > 1000 || e.Children == null || e.Children.Count == 0) throw new InvalidDataException("循环需有子动作，次数为 1–1000。");
                    foreach (var child in e.Children) ValidateAdvanced(child, depth + 1, ref count); break;
                default: throw new InvalidDataException("不支持的宏动作：" + e.Kind);
            }
        }
    }
    public static class MacroPlan
    {
        public static List<MacroEvent> Compile(MacroDefinition macro, IEnumerable<MacroDefinition> library)
        {
            return Compile(macro, library, true);
        }
        internal static List<MacroEvent> Compile(MacroDefinition macro, IEnumerable<MacroDefinition> library, bool checkPermissions)
        {
            macro.Validate(); var all = library.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var result = new List<MacroEvent>(); var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { macro.Id };
            Expand(macro.Events, macro, macro, all, stack, 0, result, checkPermissions);
            long time = result.Sum(x => (long)x.DelayMs);
            if (time > macro.MaxRunSeconds * 1000L) throw new InvalidDataException("展开循环 / 嵌套宏后的延时超过运行上限。");
            return result;
        }
        private static void Expand(IEnumerable<MacroEvent> events, MacroDefinition owner, MacroDefinition root, Dictionary<string, MacroDefinition> all, HashSet<string> stack, int depth, List<MacroEvent> result, bool checkPermissions)
        {
            if (depth > 8) throw new InvalidDataException("宏调用 / 循环最多嵌套 8 层。");
            foreach (var e in events)
            {
                if (checkPermissions && (e.Kind == "Launch" || e.Kind == "Command") && (!root.AllowExternalActions || !owner.AllowExternalActions)) throw new InvalidOperationException("此宏包含启动 / 命令动作，请在编辑器逐个确认并勾选允许外部动作。");
                if (checkPermissions && e.Kind == "Clipboard" && (!root.AllowClipboard || !owner.AllowClipboard)) throw new InvalidOperationException("此宏含剪贴板文本，请在编辑器确认并允许剪贴板动作。");
                if (e.Kind == "Loop" || e.Kind == "Macro")
                {
                    if (e.DelayMs != 0) Add(result, new MacroEvent { Kind = "Delay", DelayMs = e.DelayMs });
                    if (e.Kind == "Loop") for (int i = 0; i < e.Repeat; i++) Expand(e.Children, owner, root, all, stack, depth + 1, result, checkPermissions);
                    else
                    {
                        MacroDefinition called;
                        if (!all.TryGetValue(e.MacroId, out called)) throw new InvalidDataException("被调用宏尚未导入：" + e.MacroId);
                        if(called.TargetProcess.Length!=0 && !String.Equals(called.TargetProcess,root.TargetProcess,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("调用宏与被调用宏的目标程序不一致，请先统一目标 EXE。");
                        if (!stack.Add(called.Id)) throw new InvalidDataException("宏相互调用形成循环。");
                        called.Validate(); Expand(called.Events, called, root, all, stack, depth + 1, result, checkPermissions); stack.Remove(called.Id);
                    }
                }
                else Add(result, e);
            }
        }
        private static void Add(List<MacroEvent> result, MacroEvent e) { if (result.Count >= 100000) throw new InvalidDataException("展开后的动作数超过 100000。"); result.Add(e); }
    }
    public sealed class MacroEventConverter : JavaScriptConverter
    {
        public override IEnumerable<Type> SupportedTypes { get { return new[] { typeof(MacroEvent) }; } }
        public override IDictionary<string, object> Serialize(object obj, JavaScriptSerializer serializer)
        {
            var e = (MacroEvent)obj; var d = new Dictionary<string, object> { { "Kind", e.Kind } };
            if (e.Code != 0) d["Code"] = e.Code; if (e.Extended) d["Extended"] = true; if (e.Up) d["Up"] = true; if (e.DelayMs != 0) d["DelayMs"] = e.DelayMs;
            if (e.Kind == "Text" || e.Kind == "Clipboard") d["Text"] = e.Text;
            if (e.Kind == "Launch" || e.Kind == "Command") { d["ActionPath"] = e.ActionPath; d["Arguments"] = e.Arguments; }
            if (e.Kind == "Move") { d["X"] = e.X; d["Y"] = e.Y; d["Absolute"] = e.Absolute; }
            if (e.Kind == "Macro") d["MacroId"] = e.MacroId;
            if (e.Kind == "Loop") { d["Repeat"] = e.Repeat; d["Children"] = e.Children; }
            return d;
        }
        public override object Deserialize(IDictionary<string, object> d, Type type, JavaScriptSerializer serializer)
        {
            var e = new MacroEvent();
            foreach (var item in d)
            {
                switch (item.Key)
                {
                    case "Kind": e.Kind = Convert.ToString(item.Value); break;
                    case "Code": e.Code = Integer(item.Value); break;
                    case "Extended": e.Extended = Convert.ToBoolean(item.Value); break;
                    case "Up": e.Up = Convert.ToBoolean(item.Value); break;
                    case "DelayMs": e.DelayMs = Integer(item.Value); break;
                    case "Text": e.Text = Convert.ToString(item.Value); break;
                    case "ActionPath": e.ActionPath = Convert.ToString(item.Value); break;
                    case "Arguments": e.Arguments = Convert.ToString(item.Value); break;
                    case "MacroId": e.MacroId = Convert.ToString(item.Value); break;
                    case "Repeat": e.Repeat = Integer(item.Value); break;
                    case "X": e.X = Integer(item.Value); break;
                    case "Y": e.Y = Integer(item.Value); break;
                    case "Absolute": e.Absolute = Convert.ToBoolean(item.Value); break;
                    case "Children": if (item.Value != null) e.Children = serializer.ConvertToType<List<MacroEvent>>(item.Value); break;
                    case "Identity": break; // Read the derived field written by Helper 1.0.x.
                    default: throw new InvalidDataException("未知宏字段：" + item.Key);
                }
            }
            return e;
        }
        private static int Integer(object value) { if (!(value is int) && !(value is long)) throw new InvalidDataException("宏数字字段必须为整数。"); return checked(Convert.ToInt32(value)); }
    }
    internal interface ITextClipboard
    {
        long Sequence { get; }
        bool OnlyText();
        string ReadText();
        void WriteText(string value);
    }
    internal sealed class WindowsTextClipboard : ITextClipboard
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern uint GetClipboardSequenceNumber();
        public long Sequence{get{return GetClipboardSequenceNumber();}}
        public bool OnlyText(){var original=Clipboard.GetDataObject();return original==null || !original.GetFormats(false).Any(x=>x!=DataFormats.Text && x!=DataFormats.UnicodeText && x!=DataFormats.StringFormat && x!="Locale");}
        public string ReadText(){return Clipboard.ContainsText()?Clipboard.GetText():null;}
        public void WriteText(string value){if(value==null)Clipboard.Clear();else Clipboard.SetText(value);}
    }
    internal sealed class MacroClipboardSession
    {
        private bool touched;private string original,written;private long sequence;
        internal void Write(ITextClipboard clipboard,string text)
        {
            if(!touched || clipboard.Sequence!=sequence){long before=clipboard.Sequence;if(!clipboard.OnlyText())throw new InvalidOperationException("剪贴板含非文本内容，无法无损恢复；请使用直接输入文本动作。");string prior=clipboard.ReadText();if(clipboard.Sequence!=before)throw new InvalidOperationException("读取期间剪贴板变化，未执行写入。");original=prior;}
            touched=true;written=text;try{clipboard.WriteText(text);}finally{sequence=clipboard.Sequence;}
        }
        internal void Restore(ITextClipboard clipboard)
        {
            if(!touched)return;if(clipboard.Sequence==sequence && clipboard.OnlyText() && clipboard.ReadText()==written)clipboard.WriteText(original);touched=false;
        }
    }
    public sealed partial class MacroEngine
    {
        private readonly MacroClipboardSession clipboardSession=new MacroClipboardSession();
        private readonly ITextClipboard clipboardAdapter=new WindowsTextClipboard();
        private void ExternalAction(MacroEvent e)
        {
            if (e.Kind == "Clipboard")
            {
                Exception error = null;
                context.Send(delegate
                {
                    try
                    {
                        if(disposed || cancel==null || cancel.IsCancellationRequested)throw new OperationCanceledException("宏已停止。");
                        clipboardSession.Write(clipboardAdapter,e.Text);
                    }
                    catch (Exception ex) { error = ex; }
                }, null);
                if (error != null) throw error; return;
            }
            if (!File.Exists(e.ActionPath)) throw new FileNotFoundException("宏指定的程序不存在。");
            using(var process=Process.Start(new ProcessStartInfo { FileName = e.ActionPath, Arguments = e.Arguments, UseShellExecute = e.Kind == "Launch", CreateNoWindow = e.Kind == "Command", WorkingDirectory = Path.GetDirectoryName(e.ActionPath) })){ }
        }
        private void RestoreClipboard(int serial)
        {
            Task.Delay(300).ContinueWith(delegate{if(!disposed)context.Post(delegate{if(serial==playSerial)RestoreClipboardNow();},null);});
        }
        private void RestoreClipboardNow(){try{clipboardSession.Restore(clipboardAdapter);}catch{Tell("剪贴板原文本未能恢复，请手动检查。");}}
    }
}
