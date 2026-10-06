using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace RazerHelper
{
    public sealed class DisplayGuard : IDisposable
    {
        private readonly DisplayInfo original;
        private readonly int changed;
        private readonly EventWaitHandle confirmed;
        private bool complete;
        public static string Quote(string value)
        {
            var output = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') output.Append('\\', slashes * 2 + 1); else output.Append('\\', slashes);
                output.Append(c); slashes = 0;
            }
            output.Append('\\', slashes * 2); return output.Append('"').ToString();
        }
        public DisplayGuard(DisplayInfo screen, int rate)
        {
            WindowsDisplay.ValidateRate(screen.Name, rate); original = screen; changed = rate;
            string name = @"Local\RazerHelper-Display-" + Guid.NewGuid().ToString("N");
            confirmed = new EventWaitHandle(false, EventResetMode.ManualReset, name);
            bool watcherReady = false;
            using (var armed = new EventWaitHandle(false, EventResetMode.ManualReset, name + "-armed"))
            {
                try
                {
                    var args = new[] { "--display-guard", screen.Name, screen.Hz.ToString(), rate.ToString(), screen.Width.ToString(), screen.Height.ToString(), name, "--data-dir", Store.DirectoryPath };
                    var start = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, String.Join(" ", args.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                    using (var process = Process.Start(start)) { if (!armed.WaitOne(5000)) throw new InvalidOperationException("屏幕回退保护未就绪；未切换刷新率。"); watcherReady = true; }
                    WindowsDisplay.Set(screen.Name, rate);
                }
                catch
                {
                    // Keep the independent watcher armed if immediate rollback itself fails.
                    bool safeToDisarm = !watcherReady;
                    if (watcherReady) try { WindowsDisplay.RestoreIfUnchanged(screen.Name, screen.Hz, rate, screen.Width, screen.Height); safeToDisarm = true; } catch (Exception e) { Log.Write("立即恢复屏幕失败：" + e.Message); }
                    if (safeToDisarm) confirmed.Set(); confirmed.Dispose(); throw;
                }
            }
        }
        public void Confirm() { if (!complete) { complete = true; confirmed.Set(); } }
        public void Revert()
        {
            if (complete) return;
            WindowsDisplay.RestoreIfUnchanged(original.Name, original.Hz, changed, original.Width, original.Height);
            Confirm();
        }
        public void Dispose()
        {
            try { Revert(); } finally { confirmed.Dispose(); }
        }
        internal static int Watch(string[] args)
        {
            if (args.Length != 7 || !args[6].StartsWith(@"Local\RazerHelper-Display-", StringComparison.Ordinal)) return 2;
            Guid token; if (!Guid.TryParseExact(args[6].Substring(@"Local\RazerHelper-Display-".Length), "N", out token)) return 2;
            int oldRate, newRate, width, height;
            if (!Int32.TryParse(args[2], out oldRate) || !Int32.TryParse(args[3], out newRate) || !Int32.TryParse(args[4], out width) || !Int32.TryParse(args[5], out height) || oldRate < 24 || oldRate > 500 || newRate < 24 || newRate > 500) return 2;
            try
            {
                using (var acknowledged = EventWaitHandle.OpenExisting(args[6]))
                using (var armed = EventWaitHandle.OpenExisting(args[6] + "-armed"))
                {
                    armed.Set();
                    if (!acknowledged.WaitOne(15000)) WindowsDisplay.RestoreIfUnchanged(args[1], oldRate, newRate, width, height);
                }
                return 0;
            }
            catch (Exception e) { Log.Write("屏幕回退保护：" + e.Message); return 1; }
        }
    }
}
