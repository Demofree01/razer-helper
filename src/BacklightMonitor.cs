using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace RazerHelper
{
    public sealed class BacklightPolicy
    {
        private volatile int displayState = -1;
        private volatile bool unlocked, suspended;
        public int DisplayState { get { return displayState; } }
        public bool Unlocked { get { return unlocked; } }
        public bool Suspended { get { return suspended; } }
        public void SetDisplay(int value) { displayState = value >= 0 && value <= 2 ? value : -1; }
        public void SetUnlocked(bool value) { unlocked = value; }
        public void SetSuspended(bool value) { suspended = value; }
        public bool Permitted(Settings settings) { return settings.KeepKeyboardLit && !settings.Paused && displayState == 1 && unlocked && !suspended; }
    }

    internal sealed class BacklightMonitor : IDisposable
    {
        internal static readonly Guid SessionDisplay = new Guid("2b84c20e-ad23-4ddf-93db-05ffbd7efca5");
        private readonly IntPtr window;
        private IntPtr displayNotification;
        private bool sessionRegistered;
        private readonly Controller controller;
        private readonly Action<string, bool> notice;
        private readonly Timer timer;
        private readonly BacklightPolicy policy = new BacklightPolicy();
        private volatile bool disposed;
        private bool busy, active, confirmed;
        private DateTime retryAfter = DateTime.MinValue;
        private int failures;
        public bool Ready { get { return displayNotification != IntPtr.Zero && sessionRegistered; } }
        public BacklightMonitor(IntPtr window, Controller controller, Action<string, bool> notice)
        {
            this.window = window; this.controller = controller; this.notice = notice;
            timer = new Timer { Interval = 3000 }; timer.Tick += delegate { Pulse(); };
        }
        public void Start()
        {
            // The owner assigns this monitor before registration, so a synchronous
            // initial display notification cannot be lost during construction.
            policy.SetUnlocked(InteractiveDesktop());
            sessionRegistered = Native.WTSRegisterSessionNotification(window, 0);
            Guid setting = SessionDisplay;
            displayNotification = Native.RegisterPowerSettingNotification(window, ref setting, 0);
            if (!Ready) { string message = "背光保持：无法注册屏幕或锁屏通知，功能已暂停。"; Log.Write(message); if (controller.Settings.KeepKeyboardLit) notice(message, true); }
            Refresh();
        }
        internal static bool InteractiveDesktop()
        {
            IntPtr desktop = Native.OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS only.
            if (desktop == IntPtr.Zero) return false;
            try
            {
                var name = new StringBuilder(256); uint needed;
                return Native.GetUserObjectInformation(desktop, 2, name, (uint)(name.Capacity * 2), out needed) && String.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
            }
            finally { Native.CloseDesktop(desktop); }
        }
        internal static bool RecentInput()
        {
            var input = new Native.LastInput { Size = (uint)Marshal.SizeOf(typeof(Native.LastInput)) };
            return Native.GetLastInputInfo(ref input) && unchecked((uint)Environment.TickCount - input.Time) < 2000;
        }
        public void HandleMessage(ref Message message)
        {
            if (disposed) return;
            if (message.Msg == 0x218)
            {
                int kind = message.WParam.ToInt32();
                if (kind == 0x8013 && message.LParam != IntPtr.Zero)
                {
                    Guid setting = (Guid)Marshal.PtrToStructure(message.LParam, typeof(Guid));
                    if (setting == SessionDisplay && Marshal.ReadInt32(message.LParam, 16) == 4) policy.SetDisplay(Marshal.ReadInt32(message.LParam, 20));
                }
                else if (kind == 4) policy.SetSuspended(true);
                else if (kind == 7 || kind == 18) { policy.SetSuspended(false); policy.SetUnlocked(InteractiveDesktop()); }
                Refresh();
            }
            else if (message.Msg == 0x2b1)
            {
                int kind = message.WParam.ToInt32();
                if (kind == 2 || kind == 4 || kind == 6 || kind == 7 || kind == 11) policy.SetUnlocked(false);
                else if (kind == 1 || kind == 3 || kind == 5 || kind == 8) policy.SetUnlocked(true);
                Refresh();
            }
        }
        public void Refresh()
        {
            if (disposed) return;
            bool shouldRun = Ready && policy.Permitted(controller.Settings);
            if (shouldRun) timer.Start(); else timer.Stop();
            if (active != shouldRun)
            {
                active = shouldRun;
                Log.Write(shouldRun ? "背光保持：已激活，每 3 秒只查询背光状态。" : "背光保持：已暂停（关闭选项、暂停策略、锁屏、熄屏或睡眠）。");
                if (shouldRun) Pulse();
            }
        }
        private bool Permitted() { return !disposed && Ready && policy.Permitted(controller.Settings) && InteractiveDesktop(); }
        private async void Pulse()
        {
            if (busy || DateTime.UtcNow < retryAfter || !Permitted()) return;
            busy = true;
            try
            {
                if (await controller.KeepBacklight(Permitted))
                {
                    failures = 0; retryAfter = DateTime.MinValue;
                    if (!confirmed && !disposed) { confirmed = true; Log.Write("背光保持：固件查询已确认，未写亮度或设备模式。"); }
                }
            }
            catch (Exception e)
            {
                retryAfter = DateTime.UtcNow.AddSeconds(30);
                Log.Write("背光保持查询失败，30 秒后再试：" + e.Message);
                if (failures++ == 0 && !disposed) notice("背光保持暂不可用：" + e.Message, true);
            }
            finally { busy = false; }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; timer.Stop(); timer.Dispose();
            if (displayNotification != IntPtr.Zero) Native.UnregisterPowerSettingNotification(displayNotification);
            if (sessionRegistered) Native.WTSUnRegisterSessionNotification(window);
        }
    }
}
