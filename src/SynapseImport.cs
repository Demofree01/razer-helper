using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RazerHelper
{
    public sealed class SynapseCandidate
    {
        public string Name { get; set; }
        public string SourceId { get; set; }
        public string Source { get; set; }
        public string Error { get; set; }
        public MacroDefinition Macro { get; set; }
        internal MacroDefinition LegacyMacro { get; set; }
        public override string ToString() { return Name + (Macro == null ? " · 无法转换：" + Error : " · " + Macro.Events.Count + " 步"); }
    }
    public sealed class SynapseBinding
    {
        public string MacroId { get; set; }
        public string MacroName { get; set; }
        public string Key { get; set; }
        public bool Hypershift { get; set; }
        public string Profile { get; set; }
        public string Playback { get; set; }
        public int Repeat { get; set; }
        public int Product { get; set; }
    }
    public sealed class SynapseScan
    {
        public List<SynapseCandidate> Macros { get; set; }
        public List<SynapseBinding> Bindings { get; set; }
        public List<string> Notes { get; set; }
        public SynapseScan() { Macros = new List<SynapseCandidate>(); Bindings = new List<SynapseBinding>(); Notes = new List<string>(); }
    }
    public static class SynapseImport
    {
        internal static Dictionary<string, object> Obj(object value) { var d = value as Dictionary<string, object>; if (d == null) throw new InvalidDataException("雷云数据结构不符。"); return d; }
        internal static string Str(Dictionary<string, object> d, string key, string fallback) { object v; return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : fallback; }
        internal static int Num(Dictionary<string, object> d, string key, int fallback) { object v; if (!d.TryGetValue(key, out v)) return fallback; if (!(v is int) && !(v is long)) throw new InvalidDataException("雷云数字字段无效：" + key); return checked(Convert.ToInt32(v)); }
        private static IEnumerable<object> Items(object value) { var list = value as IEnumerable; if (list == null || value is string || value is IDictionary) throw new InvalidDataException("雷云列表格式不符。"); return list.Cast<object>(); }
        public static SynapseScan Scan()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Razer\RazerAppEngine\User Data");
            var scan = new SynapseScan(); string json = null, source = null;
            string db = Path.Combine(directory, @"Default\Local Storage\leveldb");
            try
            {
                if (Directory.Exists(db))
                {
                    var entries = LevelDbSnapshot.Read(db).Where(x => x.Key.IndexOf("synapseMacros", StringComparison.OrdinalIgnoreCase) >= 0).OrderByDescending(x => x.Value.Sequence).ToList();
                    if (entries.Count != 0)
                    {
                        // A deletion is authoritative: never resurrect a removed list from old logs.
                        if (entries[0].Value.Deleted) { scan.Notes.Add("当前缓存的宏列表已删除，未使用旧日志复原。"); return scan; }
                        json = LevelDbSnapshot.Decode(entries[0].Value.Value); source = "当前本地缓存（序列 " + entries[0].Value.Sequence + "）";
                    }
                }
            }
            catch (Exception e) { scan.Notes.Add("当前缓存未完整解析：" + e.Message); }
            var mappings = new Dictionary<string, SynapseBinding>(); var mappingTimes = new Dictionary<string,string>(); var histories = new List<Tuple<string, string, string>>();
            string logs = Path.Combine(directory, "Logs");
            if (Directory.Exists(logs))
            {
                foreach (string file in Directory.GetFiles(logs, "macro*.log").Where(x => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(x), @"^macro\d?\.log$")))
                {
                    if (new FileInfo(file).Length > 8 * 1024 * 1024) continue;
                    try
                    {
                        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string line; while ((line = reader.ReadLine()) != null)
                            {
                                if (line.Length > 2 * 1024 * 1024) continue;
                                if (line.Contains("localStorageSetItem") && line.Contains("synapseMacros"))
                                {
                                    try { var data = Obj(Store.Serializer().DeserializeObject(JsonFragment(line, line.IndexOf('{')))); var payload = Obj(data["payload"]); if (Str(payload, "key", "") == "synapseMacros") histories.Add(Tuple.Create(line.Substring(0, Math.Min(24, line.Length)), Str(payload, "value", ""), Path.GetFileName(file))); } catch (Exception) { }
                                }
                                if (line.Contains("macroReducer addMultipleMapping"))
                                {
                                    int p = line.IndexOf("addMultipleMapping") + "addMultipleMapping".Length; p = line.IndexOf('[', p); if (p < 0) continue;
                                    try
                                    {
                                        foreach (object item in Items(Store.Serializer().DeserializeObject(JsonFragment(line, p))))
                                        {
                                            var map = Obj(item); if (Str(map, "outputType", "") != "macroGroup") continue; var group = Obj(map["macroGroup"]);
                                            var binding = new SynapseBinding { MacroId = Str(group, "guid", ""), MacroName = Str(group, "name", ""), Key = Str(map, "inputID", ""), Hypershift = map.ContainsKey("isHyperShift") && Convert.ToBoolean(map["isHyperShift"]), Profile = Str(map, "profile", ""), Product = Num(map, "productId", 0), Playback = Str(group, "macroPlaybackOption", "Once"), Repeat = Num(group, "repeatCount", 1) };
                                            string id = binding.Product + ":" + binding.Profile + ":" + binding.Key + ":" + binding.Hypershift;
                                            string stamp=line.Substring(0,Math.Min(24,line.Length));string prior;
                                            if(!mappingTimes.TryGetValue(id,out prior) || String.CompareOrdinal(stamp,prior)>=0){mappings[id] = binding;mappingTimes[id]=stamp;}
                                        }
                                    }
                                    catch (Exception) { }
                                }
                            }
                        }
                    }
                    catch (IOException e) { scan.Notes.Add("日志暂不可读：" + Path.GetFileName(file) + " / " + e.Message); }
                }
            }
            if (json == null && histories.Count != 0) { var newest = histories.OrderBy(x => x.Item1, StringComparer.Ordinal).Last(); json = newest.Item2; source = "历史日志 " + newest.Item3 + " " + newest.Item1; scan.Notes.Add("宏来自历史日志快照，可能包含已修改或删除的版本；请核对预览。"); }
            if (json != null) ConvertList(json, source, scan); else scan.Notes.Add("未找到可读宏列表，可使用雷云导出的文件导入。");
            scan.Bindings.AddRange(mappings.Values.Where(x => x.Product == 709));
            scan.Notes.Add("绑定来自日志，仅作参考，可能含旧配置；导入不会启用绑定。文本内容在默认预览和诊断输出中隐藏。");
            scan.Notes.Add("鼠标动作的代码映射及坐标需手动核对；雷云设为不记录移动的宏不迁移其轨迹。"); return scan;
        }
        public static string JsonFragment(string text, int start)
        {
            if (start < 0 || start >= text.Length) throw new InvalidDataException("缺少 JSON。"); int depth = 0; bool quote = false, escape = false;
            for (int p = start; p < text.Length; p++) { char c = text[p]; if (quote) { if (escape) escape = false; else if (c == '\\') escape = true; else if (c == '"') quote = false; } else { if (c == '"') quote = true; else if (c == '{' || c == '[') depth++; else if (c == '}' || c == ']') { if (--depth == 0) return text.Substring(start, p - start + 1); } } } throw new InvalidDataException("JSON 截断。");
        }
        public static void ConvertList(string json, string source, SynapseScan scan)
        {
            if (json.Length > 8 * 1024 * 1024) throw new InvalidDataException("雷云宏列表过大。");
            foreach (object item in Items(Store.Serializer().DeserializeObject(json)))
            {
                if (scan.Macros.Count >= 256) throw new InvalidDataException("宏扫描数量超过上限。"); var d = Obj(item); string name = Str(d, "name", "未命名宏"), id = Str(d, "guid", ""); if (id == "00000000-0000-0000-0000-000000000000") continue;
                var candidate = new SynapseCandidate { Name = String.IsNullOrWhiteSpace(name) ? "未命名宏" : name, SourceId = id, Source = source };
                try { candidate.Macro = ConvertMacro(d); candidate.LegacyMacro = ConvertMacro(d, false); } catch (Exception e) { candidate.Error = e.Message; }
                scan.Macros.Add(candidate);
            }
        }
        public static MacroDefinition ConvertMacro(Dictionary<string, object> d)
        {
            return ConvertMacro(d, true);
        }
        private static MacroDefinition ConvertMacro(Dictionary<string, object> d, bool inputText)
        {
            var macro = new MacroDefinition { Id = Str(d, "guid", Guid.NewGuid().ToString("N")), Name = Str(d, "name", "导入的宏"), MaxRunSeconds = 600 };
            var engine = Obj(d["appEngine"]); string move = Str(engine, "mouseMoveType", "none"); int delaySetting = Num(d, "delaySetting", 0);
            if (delaySetting != 0) throw new InvalidDataException("尚未验证此宏的全局延迟设置，请使用录制延迟或导出文件。");
            foreach (object item in Items(engine["events"]))
            {
                var e = Obj(item); string kind = Str(e, "type", ""); var action = new MacroEvent();
                switch (kind)
                {
                    case "delay": action.Kind = "Delay"; action.DelayMs = Num(e, "ms", -1); break;
                    case "keyboard":
                        action.Kind = "Key";
                        if (e.ContainsKey("scancode")) { int flag = Num(e, "flag", -1); if (flag < 0 || flag > 3) throw new InvalidDataException("未知扫描码标志。"); action.Code = Num(e, "scancode", -1); action.Up = (flag & 1) != 0; action.Extended = (flag & 2) != 0; }
                        else { if (Num(e, "page", 0) != 7) throw new InvalidDataException("尚未支持此多媒体键页面。"); int flag = Num(e, "flag", -1); if (flag != 0 && flag != 1) throw new InvalidDataException("未知 HID 键盘标志。"); var key = HidKey(Num(e, "id", -1)); action.Code = key.Code; action.Extended = key.Extended; action.Up = flag == 0; }
                        break;
                    // Synapse's clipboard event is a Text Function: it inserts text.
                    // A bare clipboard write loses the implicit paste and reports success
                    // without typing. Use Unicode input so copied pictures stay intact.
                    case "clipboard": action.Kind = inputText ? "Text" : "Clipboard"; action.Text = Str(e, "text", ""); break;
                    case "text": action.Kind = "Text"; action.Text = Str(e, "text", ""); break;
                    case "mouseMove": if (move == "none") continue; if (move != "absolute" && move != "relative") throw new InvalidDataException("鼠标坐标模式尚未验证。"); action.Kind = "Move"; action.X = Num(e, "x", 0); action.Y = Num(e, "y", 0); action.Absolute = move == "absolute"; break;
                    case "mouse":
                        // Synapse event IDs encode down/up pairs; preserve all buttons.
                        int button = Num(e, "id", 0); if (button < 1 || button > 10) throw new InvalidDataException("尚未验证此鼠标事件代码。"); action.Kind = "Mouse"; action.Code = (button + 1) / 2; action.Up = button % 2 == 0; break;
                    default: throw new InvalidDataException("未验证的雷云动作：" + kind + "；未丢弃动作或部分导入。");
                }
                macro.Events.Add(action);
            }
            macro.Validate(); return macro;
        }
        public static int RepairLegacyText(MacroDefinition macro, IEnumerable<SynapseCandidate> candidates)
        {
            if (!macro.Events.Any(e => e.Kind == "Clipboard")) return 0;
            string events = Store.Serializer().Serialize(macro.Events);
            var matches = candidates.Where(c => c.Macro != null && c.LegacyMacro != null &&
                String.Equals(c.SourceId, macro.Id, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(Store.Serializer().Serialize(c.LegacyMacro.Events), events, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1) return 0;
            var candidate = matches[0];
            int count = candidate.LegacyMacro.Events.Count(e => e.Kind == "Clipboard");
            // Preserve edits to the name, target, limits, permissions and bindings.
            // Only an exact match to a read-only Synapse snapshot may be repaired.
            macro.Events = Store.Serializer().Deserialize<List<MacroEvent>>(Store.Serializer().Serialize(candidate.Macro.Events));
            return count;
        }
        public static MacroEvent HidKey(int id)
        {
            int vk;
            if (id >= 4 && id <= 29) vk = 0x41 + id - 4;
            else if (id >= 30 && id <= 38) vk = 0x31 + id - 30;
            else if (id == 39) vk = 0x30;
            else if (id >= 58 && id <= 69) vk = 0x70 + id - 58;
            else if (id >= 224 && id <= 231) vk = new[] { 0xa2, 0xa0, 0xa4, 0x5b, 0xa3, 0xa1, 0xa5, 0x5c }[id - 224];
            else
            {
                var keys = new Dictionary<int, int> { {40,13},{41,27},{42,8},{43,9},{44,32},{45,0xbd},{46,0xbb},{47,0xdb},{48,0xdd},{49,0xdc},{51,0xba},{52,0xde},{53,0xc0},{54,0xbc},{55,0xbe},{56,0xbf},{57,0x14},{70,0x2c},{71,0x91},{73,0x2d},{74,0x24},{75,0x21},{76,0x2e},{77,0x23},{78,0x22},{79,0x27},{80,0x25},{81,0x28},{82,0x26} };
                if (!keys.TryGetValue(id, out vk)) throw new InvalidDataException("未支持的 HID 按键 " + id);
            }
            uint scan = Native.MapVirtualKey((uint)vk, 4); if (scan == 0 || (scan & 0xff00) == 0xe100) throw new InvalidDataException("此按键无法转换为可靠扫描码。"); return new MacroEvent { Kind = "Key", Code = (int)(scan & 0xff), Extended = (scan & 0xff00) == 0xe000 };
        }
    }
}
