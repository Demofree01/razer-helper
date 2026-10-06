using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RazerHelper
{
    // Independent read-only reader for Chromium Local Storage. Never opens a DB
    // through LevelDB, takes its lock, writes a manifest, or performs recovery.
    public sealed class LevelDbValue
    {
        public ulong Sequence; public bool Deleted; public byte[] Value;
    }
    public static class LevelDbSnapshot
    {
        private const int MaximumBlock = 16 * 1024 * 1024;
        public static Dictionary<string, LevelDbValue> Read(string directory)
        {
            var result = new Dictionary<string, LevelDbValue>();
            string[] paths=Directory.GetFiles(directory).Where(x => x.EndsWith(".ldb", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".log", StringComparison.OrdinalIgnoreCase)).ToArray();
            if(paths.Length>256)throw new InvalidDataException("本地缓存文件数量超过扫描上限。");long total=0;
            foreach (string path in paths)
            {
                var info = new FileInfo(path); long size = info.Length; DateTime changed = info.LastWriteTimeUtc;
                if (size > 64 * 1024 * 1024) throw new InvalidDataException("本地缓存文件超过扫描上限。");
                total+=size;if(total>256L*1024*1024)throw new InvalidDataException("本地缓存总量超过扫描上限。");
                byte[] data;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                { if(file.Length!=size)throw new IOException("雷云缓存正在变化，请重试扫描。");data = new byte[(int)size]; int read = 0; while (read < data.Length) { int n = file.Read(data, read, data.Length - read); if (n == 0) throw new IOException("缓存读取不完整。"); read += n; } }
                info.Refresh(); if (info.Length != size || info.LastWriteTimeUtc != changed) throw new IOException("雷云缓存正在变化，请稳定后重试扫描。");
                if (path.EndsWith(".ldb", StringComparison.OrdinalIgnoreCase)) Table(data, result); else Journal(data, result);
            }
            return result;
        }
        private static ulong Var(byte[] b, ref int p)
        {
            ulong value = 0; for (int shift = 0; shift < 64; shift += 7) { if (p >= b.Length) throw new InvalidDataException("缓存整数截断。"); byte x = b[p++]; if (shift == 63 && x > 1) throw new InvalidDataException("缓存整数溢出。"); value |= (ulong)(x & 127) << shift; if (x < 128) return value; } throw new InvalidDataException("缓存整数过长。");
        }
        private static byte[] Slice(byte[] b, int p, int n) { if (p < 0 || n < 0 || n > MaximumBlock || p > b.Length - n) throw new InvalidDataException("缓存块范围无效。"); var r = new byte[n]; Buffer.BlockCopy(b, p, r, 0, n); return r; }
        private static uint Crc(byte[] b) { uint crc = 0xffffffff; foreach (byte x in b) { crc ^= x; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0x82f63b78u : 0); } return ~crc; }
        private static void CheckCrc(uint expected, byte[] b) { uint crc = Crc(b); uint mask = ((crc >> 15) | (crc << 17)) + 0xa282ead8u; if (mask != expected) throw new InvalidDataException("雷云缓存校验不符，拒绝导入不完整记录。"); }
        public static byte[] Snappy(byte[] input)
        {
            int p = 0; ulong length = Var(input, ref p); if (length > MaximumBlock) throw new InvalidDataException("解压块过大。"); var output = new byte[(int)length]; int at = 0;
            while (p < input.Length)
            {
                byte tag = input[p++]; int kind = tag & 3, count, offset;
                if (kind == 0)
                {
                    count = tag >> 2;
                    if (count < 60) count++;
                    else { int bytes = count - 59; if (p + bytes > input.Length) throw new InvalidDataException("Snappy 截断。"); uint n = 0; for (int i = 0; i < bytes; i++) n |= (uint)input[p++] << (i * 8); if (n >= MaximumBlock) throw new InvalidDataException("Snappy 长度过大。"); count = (int)n + 1; }
                    if (count > output.Length - at || count > input.Length - p) throw new InvalidDataException("Snappy 字面量越界。"); Buffer.BlockCopy(input, p, output, at, count); p += count; at += count; continue;
                }
                count = kind == 1 ? 4 + ((tag >> 2) & 7) : 1 + (tag >> 2);
                int need = kind == 1 ? 1 : kind == 2 ? 2 : 4; if (p + need > input.Length) throw new InvalidDataException("Snappy 复制截断。");
                uint distance = kind == 1 ? (uint)((tag & 0xe0) << 3) : 0; for (int i = 0; i < need; i++) distance |= (uint)input[p++] << (8 * i); offset = (int)distance;
                if (distance == 0 || distance > at || count > output.Length - at) throw new InvalidDataException("Snappy 复制越界。"); for (int i = 0; i < count; i++) { output[at] = output[at - offset]; at++; }
            }
            if (at != output.Length) throw new InvalidDataException("Snappy 解压不完整。"); return output;
        }
        private static byte[] Block(byte[] file, byte[] handle)
        {
            int p = 0; ulong offset = Var(handle, ref p), length = Var(handle, ref p);
            if (offset > Int32.MaxValue || length > MaximumBlock) throw new InvalidDataException("缓存块过大。"); int start = (int)offset, n = (int)length;
            byte[] stored = Slice(file, start, n + 1); if (start > file.Length - n - 5) throw new InvalidDataException("缓存块尾截断。"); CheckCrc(BitConverter.ToUInt32(file, start + n + 1), stored);
            byte compression = stored[n]; byte[] content = Slice(stored, 0, n);
            if (compression == 0) return content; if (compression == 1) return Snappy(content); throw new InvalidDataException("不支持的缓存压缩类型。");
        }
        private static List<KeyValuePair<byte[], byte[]>> Entries(byte[] block)
        {
            if (block.Length < 4) throw new InvalidDataException("缓存块太短。"); uint restarts = BitConverter.ToUInt32(block, block.Length - 4);
            if (restarts > (block.Length - 4) / 4) throw new InvalidDataException("缓存重启表无效。"); int end = block.Length - 4 - (int)restarts * 4, p = 0; byte[] previous = new byte[0]; var entries = new List<KeyValuePair<byte[], byte[]>>();
            while (p < end)
            {
                ulong shared = Var(block, ref p), suffix = Var(block, ref p), size = Var(block, ref p);
                if (shared > (ulong)previous.Length || shared+suffix > MaximumBlock || size > MaximumBlock || (ulong)p + suffix + size > (ulong)end) throw new InvalidDataException("缓存条目越界。");
                byte[] key = new byte[(int)(shared + suffix)]; Buffer.BlockCopy(previous, 0, key, 0, (int)shared); Buffer.BlockCopy(block, p, key, (int)shared, (int)suffix); p += (int)suffix; byte[] value = Slice(block, p, (int)size); p += (int)size; previous = key; entries.Add(new KeyValuePair<byte[], byte[]>(key, value));
            }
            return entries;
        }
        private static void Keep(Dictionary<string, LevelDbValue> result, byte[] key, ulong sequence, byte type, byte[] value)
        {
            string topic = null;
            foreach (string item in new[] { "synapseMacros", "synapseMappings" }) if (Contains(key, Encoding.UTF8.GetBytes(item)) || Contains(key, Encoding.Unicode.GetBytes(item))) { topic = item; break; }
            if (topic == null) return;if(type>1)throw new InvalidDataException("宏缓存记录类型不受支持。"); string name = topic + ":" + Convert.ToBase64String(key);
            if(result.Count>=512 && !result.ContainsKey(name))throw new InvalidDataException("宏缓存条目超过扫描上限。");
            LevelDbValue before; if (!result.TryGetValue(name, out before) || sequence >= before.Sequence) result[name] = new LevelDbValue { Sequence = sequence, Deleted = type == 0, Value = value };
        }
        private static bool Contains(byte[] data, byte[] needle) { for (int i = 0; i <= data.Length - needle.Length; i++) { bool match = true; for (int j = 0; j < needle.Length; j++) if (data[i + j] != needle[j]) { match = false; break; } if (match) return true; } return false; }
        private static void Table(byte[] file, Dictionary<string, LevelDbValue> result)
        {
            if (file.Length < 48 || BitConverter.ToUInt64(file, file.Length - 8) != 0xdb4775248b80fb57UL) throw new InvalidDataException("无效 LevelDB 表。");
            int footer = file.Length - 48; Var(file, ref footer); Var(file, ref footer); int index = footer; Var(file, ref footer); Var(file, ref footer);
            foreach (var item in Entries(Block(file, Slice(file, index, footer - index))))
                foreach (var entry in Entries(Block(file, item.Value)))
                {
                    if (entry.Key.Length < 8) throw new InvalidDataException("缓存内部键截断。"); ulong tag = BitConverter.ToUInt64(entry.Key, entry.Key.Length - 8); Keep(result, Slice(entry.Key, 0, entry.Key.Length - 8), tag >> 8, (byte)tag, entry.Value);
                }
        }
        private static void Journal(byte[] file, Dictionary<string, LevelDbValue> result)
        {
            using (var fragments = new MemoryStream())
            {
                int p = 0;
                while (p + 7 <= file.Length)
                {
                    int boundary = 32768 - p % 32768; if (boundary < 7) { p += boundary; continue; }
                    uint crc = BitConverter.ToUInt32(file, p); int length = file[p + 4] | file[p + 5] << 8; byte type = file[p + 6]; p += 7;
                    if (length == 0 && type == 0) { p += boundary - 7; continue; }
                    if (length > boundary - 7 || p + length > file.Length) break; // Ignore an uncommitted trailing record.
                    byte[] data = Slice(file, p, length); p += length; var checkedBytes = new byte[length + 1]; checkedBytes[0] = type; Buffer.BlockCopy(data, 0, checkedBytes, 1, length); CheckCrc(crc, checkedBytes);
                    if (type == 1) { Batch(data, result); fragments.SetLength(0); }
                    else if (type == 2) { fragments.SetLength(0); fragments.Write(data, 0, data.Length); }
                    else if (type == 3 || type == 4) { if (fragments.Length == 0) continue; if (fragments.Length + data.Length > MaximumBlock) throw new InvalidDataException("缓存记录过大。"); fragments.Write(data, 0, data.Length); if (type == 4) { Batch(fragments.ToArray(), result); fragments.SetLength(0); } }
                    else throw new InvalidDataException("缓存日志类型无效。");
                }
            }
        }
        private static void Batch(byte[] data, Dictionary<string, LevelDbValue> result)
        {
            if (data.Length < 12) throw new InvalidDataException("缓存批次截断。"); ulong sequence = BitConverter.ToUInt64(data, 0); uint count = BitConverter.ToUInt32(data, 8); if (count > 100000) throw new InvalidDataException("缓存批次过大。"); int p = 12;
            for (uint i = 0; i < count; i++) { if (p >= data.Length) throw new InvalidDataException("缓存批次截断。"); byte type = data[p++]; if (type != 0 && type != 1) throw new InvalidDataException("缓存写入类型无效。"); ulong n = Var(data, ref p); if (n > MaximumBlock) throw new InvalidDataException("缓存键过大。"); byte[] key = Slice(data, p, (int)n); p += (int)n; byte[] value = null; if (type == 1) { n = Var(data, ref p); if (n > MaximumBlock) throw new InvalidDataException("缓存值过大。"); value = Slice(data, p, (int)n); p += (int)n; } Keep(result, key, sequence + i, type, value); }
            if (p != data.Length) throw new InvalidDataException("缓存批次尾不匹配。");
        }
        public static string Decode(byte[] data) { if (data == null || data.Length == 0) return ""; if (data[0] == 0) return Encoding.Unicode.GetString(data, 1, data.Length - 1); if (data[0] == 1) return Encoding.UTF8.GetString(data, 1, data.Length - 1); return Encoding.UTF8.GetString(data); }
    }
}
