using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace RazerHelper
{
    internal sealed class FakeTransport : IReportTransport
    {
        public readonly List<byte[]> Requests = new List<byte[]>();
        public byte Mode1 = 2, Mode2 = 2, FanMode, Brightness = 255, Effect = 3, DeviceMode, CpuLevel = 2, GpuLevel;
        public bool FailZone2Once, FailGpuOnce, TransientZone2Once, IgnoreBrightness, BadBacklightRegion;
        public Action<byte[]> AfterRequest;
        public int ForeignReplyOnce;
        public byte[] Exchange(byte[] request)
        {
            Requests.Add((byte[])request.Clone());
            var response = (byte[])request.Clone(); response[1] = 2;
            int command = request[7] * 256 + request[8];
            if (ForeignReplyOnce == command) { response[2] ^= 1; ForeignReplyOnce = 0; }
            if (command == 0x0d82) { response[11] = request[10] == 1 ? Mode1 : Mode2; response[12] = FanMode; if (request[10] == 2 && TransientZone2Once) { response[11] = 6; TransientZone2Once = false; } }
            if (command == 0x0d02)
            {
                if (request[10] == 2 && FailZone2Once) { response[1] = 3; FailZone2Once = false; }
                else if (request[10] == 1) Mode1 = request[11]; else Mode2 = request[11];
            }
            if (command == 0x0e84) response[10] = Brightness;
            if (command == 0x0383) { response[10] = BadBacklightRegion ? (byte)1 : (byte)5; response[11] = Brightness; }
            if (command == 0x0084) response[9] = DeviceMode;
            if (command == 0x0e04 && !IgnoreBrightness) Brightness = request[10];
            if (command == 0x0f82) response[11] = Effect;
            if (command == 0x0d87) response[11] = request[10] == 1 ? CpuLevel : GpuLevel;
            if (command == 0x0d07)
            {
                if (request[10] == 2 && FailGpuOnce) { response[1] = 3; FailGpuOnce = false; }
                else if (request[10] == 1) CpuLevel = request[11]; else GpuLevel = request[11];
            }
            if (command == 0x030a) Effect = request[9] == 0 ? (byte)0 : request[9] == 6 ? (byte)1 : request[9] == 3 ? (byte)2 : request[9] == 4 ? (byte)3 : request[9] == 5 ? (byte)5 : (byte)4;
            response[89] = 0; for (int i = 3; i < 89; i++) response[89] ^= response[i];
            if (AfterRequest != null) AfterRequest(request);
            return response;
        }
        public void Dispose() { }
        public int Writes { get { return Requests.Count(x => !RazerProtocol.IsRead((ushort)(x[7] * 256 + x[8]))); } }
    }
    internal static partial class Tests
    {
        private static int passed, failed;
        private static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); }
        private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
        private static void Test(string name, Action action) { try { action(); Console.WriteLine("PASS " + name); passed++; } catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); failed++; } }
        private static MacroDefinition Macro(params MacroEvent[] events) { return new MacroDefinition { Name = "test", Events = events.ToList() }; }
        private static byte[] Reply(byte[] request, byte status) { var r = (byte[])request.Clone(); r[1] = status; return r; }
        [STAThread]
        public static int Main()
        {
            Store.DirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts", "unit-data");
            Test("native-ABI-layouts", delegate
            {
                Assert(Marshal.SizeOf(typeof(Native.HidCaps)) == 64, "HIDP_CAPS"); Assert(Marshal.SizeOf(typeof(Native.DeviceInterface)) == 32, "SP_DEVICE_INTERFACE_DATA");
                Assert(Marshal.SizeOf(typeof(WindowsDisplay.DevMode)) == 220, "DEVMODEW"); Assert(Marshal.SizeOf(typeof(WindowsDisplay.PathInfo)) == 72, "DISPLAYCONFIG_PATH_INFO"); Assert(Marshal.SizeOf(typeof(Native.Input)) == 40, "INPUT");
                Assert(Marshal.SizeOf(typeof(Native.LastInput)) == 8, "LASTINPUTINFO");
                var api = typeof(Native).GetMethod("PowerGetActualOverlayScheme", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert(api.GetParameters()[0].ParameterType == typeof(Guid).MakeByRefType(), "Overlay is GUID*, not GUID**");
            });
            Test("hardware-model-gating", delegate { Assert(HidDiscovery.IsSupportedModel("Blade 14 - RZ09-0530"), "Target model"); Assert(!HidDiscovery.IsSupportedModel("Blade 16 RZ09-0528"), "Other model"); });
            Test("wire-layout-and-checksum", delegate { var r = RazerProtocol.Build(0x0e84, new byte[] { 1, 0 }, 0x1f); Assert(r.Length == 91 && r[0] == 0 && r[2] == 0x1f && r[6] == 2 && r[7] == 0x0e && r[8] == 0x84 && r[9] == 1 && r[89] == 0x89, "Known wire vector"); });
            Test("valid-response", delegate { var r = RazerProtocol.Build(0x0081, new byte[] { 0, 0 }, 0x1f); Assert(RazerProtocol.Parse(r, Reply(r, 2)).Length == 2, "Payload length"); });
            Test("response-corruption-rejected", delegate { var r = RazerProtocol.Build(0x0081, new byte[] { 0, 0 }, 0x1f); var reply = Reply(r, 2); reply[9] ^= 1; Throws<InvalidOperationException>(() => RazerProtocol.Parse(r, reply)); });
            Test("foreign-transaction-rejected", delegate { var r = RazerProtocol.Build(0x0081, new byte[] { 0, 0 }, 0x1f); var reply = Reply(r, 2); reply[2]++; Throws<InvalidOperationException>(() => RazerProtocol.Parse(r, reply)); });
            Test("unsupported-command-fails-once", delegate { var r = RazerProtocol.Build(0x0081, new byte[] { 0, 0 }, 0x1f); Throws<NotSupportedException>(() => RazerProtocol.Parse(r, Reply(r, 5))); });
            Test("firmware-busy-is-not-success", delegate { var r = RazerProtocol.Build(0x0081, new byte[] { 0, 0 }, 0x1f); Throws<InvalidOperationException>(() => RazerProtocol.Parse(r, Reply(r, 1))); });
            Test("truncated-payload-rejected", delegate { var r = RazerProtocol.Build(0x0081, new byte[] { 0, 0 }, 0x1f); Throws<InvalidOperationException>(() => RazerProtocol.Parse(r, new byte[30])); });
            Test("read-only-rejects-mutation-before-transport", delegate { var fake = new FakeTransport(); using (var protocol = new RazerProtocol(fake, true)) Throws<InvalidOperationException>(() => protocol.Send(0x0e04, 1, 30)); Assert(fake.Requests.Count == 0, "No IO"); });
            Test("overwritten-read-can-retry", delegate { var fake = new FakeTransport { ForeignReplyOnce = 0x0e84 }; using (var p = new RazerProtocol(fake, true)) Assert(p.Send(0x0e84, 1, 0)[1] == 255, "Read recovered"); Assert(fake.Requests.Count == 2 && fake.Writes == 0, "Only queries repeated"); });
            Test("overwritten-write-never-blindly-retries", delegate { var fake = new FakeTransport { ForeignReplyOnce = 0x0e04 }; using (var p = new RazerProtocol(fake, false)) Throws<InvalidOperationException>(() => p.Send(0x0e04, 1, 30)); Assert(fake.Writes == 1, "Exactly one write"); });
            Test("unsafe-commands-blocked", delegate
            {
                Throws<ArgumentException>(() => RazerProtocol.Validate(0x0d07, new byte[] { 1, 1, 4 }, false));
                Throws<ArgumentException>(() => RazerProtocol.Validate(0x0d07, new byte[] { 1, 2, 3 }, false));
                Throws<ArgumentException>(() => RazerProtocol.Validate(0x0004, new byte[] { 3, 0 }, false));
                Throws<ArgumentException>(() => RazerProtocol.Validate(0x0d02, new byte[] { 1, 1, 0, 1 }, false));
                Throws<ArgumentException>(() => RazerProtocol.Validate(0x0d02, new byte[] { 1, 1, 7, 0 }, false));
                Throws<ArgumentException>(() => RazerProtocol.Validate(0x0712, new byte[] { 0xb2 }, false));
                Throws<ArgumentException>(() => RazerProtocol.Validate(0x030a, new byte[] { 5, 1 }, false));
                Throws<ArgumentException>(() => RazerProtocol.Validate(0xffff, new byte[0], false));
            });
            Test("oversized-command-blocked", delegate { Throws<ArgumentException>(() => RazerProtocol.Build(0x0d82, new byte[81], 0x1f)); });
            Test("volatile-solid-RGB-frame", delegate { var fake = new FakeTransport(); using (var h = new RazerHardware(new RazerProtocol(fake, false))) h.SetEffect(LightEffect.Static, 20, 100, 80, 1); var rows = fake.Requests.Where(x => x[8] == 0x0b).ToArray(); Assert(rows.Length == 6 && rows[5][10] == 5 && rows.All(x => x[12] == 15), "6x16 matrix"); Assert(fake.Requests.Any(x => x[8] == 0x0a && x[9] == 5 && x[10] == 0), "NOSTORE activation"); });
            Test("solid-RGB-does-not-change-device-mode", delegate { var fake = new FakeTransport { DeviceMode = 3 }; using (var h = new RazerHardware(new RazerProtocol(fake, false))) h.SetEffect(LightEffect.Static, 20, 100, 80, 1); Assert(fake.Effect == 1 && !fake.Requests.Any(x => x[7] == 0 && x[8] == 4), "Existing Driver mode preserved"); });
            Test("matrix-out-of-bounds-blocked", delegate { var row = new byte[70]; row[0] = 0xff; row[1] = 6; row[3] = 15; Throws<ArgumentException>(() => RazerProtocol.Validate(0x030b, row, false)); });
            Test("brightness-validates-and-restores-on-failure", delegate { var fake = new FakeTransport { IgnoreBrightness = true }; using (var h = new RazerHardware(new RazerProtocol(fake, false))) { Throws<InvalidOperationException>(() => h.SetBrightness(120)); Assert(fake.Brightness == 255 && fake.Writes == 2, "Original retained with rollback"); } });
            Test("brightness-out-of-range-does-no-IO", delegate { var fake = new FakeTransport(); using (var h = new RazerHardware(new RazerProtocol(fake, false))) Throws<ArgumentOutOfRangeException>(() => h.SetBrightness(256)); Assert(fake.Requests.Count == 0, "No IO"); });
            Test("two-zone-mode-partial-failure-rolls-back", delegate { var fake = new FakeTransport { FailZone2Once = true }; using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) Throws<InvalidOperationException>(() => h.SetMode(BladeMode.Balanced)); Assert(fake.Mode1 == 2 && fake.Mode2 == 2, "Both original zones restored"); });
            Test("manual-fans-refuse-automatic-mode-write", delegate { var fake = new FakeTransport { FanMode = 1 }; using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) Throws<InvalidOperationException>(() => h.SetMode(BladeMode.Balanced)); Assert(fake.Writes == 0, "No writes"); });
            Test("unknown-mode-refuses-unrestorable-write", delegate { var fake = new FakeTransport { Mode1 = 7, Mode2 = 7 }; using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) Throws<InvalidOperationException>(() => h.SetMode(BladeMode.Silent)); Assert(fake.Writes == 0, "No writes"); });
            Test("stale-battery-saver-on-AC-can-switch", delegate { var fake = new FakeTransport { Mode1 = 6, Mode2 = 6 }; using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) h.SetMode(BladeMode.Performance); Assert(fake.Mode1 == 2 && fake.Mode2 == 2, "Both zones leave stale mode 6"); });
            Test("stale-battery-saver-on-AC-failure-restores", delegate { var fake = new FakeTransport { Mode1 = 6, Mode2 = 6, FailZone2Once = true }; using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) Throws<InvalidOperationException>(() => h.SetMode(BladeMode.Performance)); Assert(fake.Mode1 == 6 && fake.Mode2 == 6, "Both original mode 6 zones restored"); });
            Test("battery-only-allows-balanced-and-saver", delegate
            {
                var fake = new FakeTransport { Mode1 = 0, Mode2 = 0 }; using (var h = new RazerHardware(new RazerProtocol(fake, false), () => false))
                {
                    h.SetMode(BladeMode.BatterySaver); Assert(fake.Mode1 == 6 && fake.Mode2 == 6, "Saver applied"); h.SetMode(BladeMode.Balanced);
                    int count = fake.Requests.Count;
                    foreach (var mode in new[] { BladeMode.Performance, BladeMode.Silent, BladeMode.Custom }) Throws<InvalidOperationException>(() => h.SetMode(mode));
                    Assert(fake.Requests.Count == count, "No transport for AC-only modes");
                }
            });
            Test("AC-rejects-new-battery-saver-preset", delegate { var fake = new FakeTransport(); using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) Throws<InvalidOperationException>(() => h.SetMode(BladeMode.BatterySaver)); Assert(fake.Requests.Count == 0, "Before any IO"); });
            Test("unknown-power-refuses-performance-before-IO", delegate { var fake = new FakeTransport(); using (var h = new RazerHardware(new RazerProtocol(fake, false), () => null)) Throws<InvalidOperationException>(() => h.SetMode(BladeMode.Balanced)); Assert(fake.Requests.Count == 0, "No IO"); });
            Test("custom-mode-with-known-levels-can-be-left", delegate { var fake = new FakeTransport { Mode1 = 4, Mode2 = 4 }; using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) h.SetMode(BladeMode.Balanced); Assert(fake.Mode1 == 0 && fake.Mode2 == 0 && fake.CpuLevel == 2 && fake.GpuLevel == 0, "Records preserved"); });
            Test("experimental-undervolt-record-blocks-custom-mutation", delegate { var fake = new FakeTransport { Mode1 = 4, Mode2 = 4, CpuLevel = 4 }; using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) Throws<InvalidOperationException>(() => h.SetMode(BladeMode.Performance)); Assert(fake.Writes == 0, "No unverified rollback writes"); });
            Test("custom-levels-use-only-auto-fans-and-supported-commands", delegate { var fake = new FakeTransport(); using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) h.SetCustomPerformance(1, 1); Assert(fake.Mode1 == 4 && fake.Mode2 == 4 && fake.CpuLevel == 1 && fake.GpuLevel == 1, "Custom readback"); Assert(fake.Requests.Where(x => x[8] == 2 && x[7] == 0x0d).All(x => x[12] == 0), "Firmware auto fans"); Assert(fake.Requests.All(x => RazerProtocol.IsRead((ushort)(x[7] * 256 + x[8])) || (x[7] == 0x0d && (x[8] == 2 || x[8] == 7))), "No voltage or unrelated writes"); });
            Test("custom-GPU-failure-restores-mode-and-CPU-record", delegate { var fake = new FakeTransport { FailGpuOnce = true }; using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) Throws<InvalidOperationException>(() => h.SetCustomPerformance(1, 1)); Assert(fake.Mode1 == 2 && fake.Mode2 == 2 && fake.CpuLevel == 2 && fake.GpuLevel == 0, "Entire original snapshot restored"); });
            Test("custom-input-outside-normal-range-does-no-IO", delegate { var fake = new FakeTransport(); using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) { Throws<ArgumentOutOfRangeException>(() => h.SetCustomPerformance(3, 1)); Throws<ArgumentOutOfRangeException>(() => h.SetCustomPerformance(1, -1)); } Assert(fake.Requests.Count == 0, "No IO"); });
            Test("transient-zone-mismatch-is-reread", delegate { var fake = new FakeTransport { TransientZone2Once = true }; using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) h.SetMode(BladeMode.Balanced); Assert(fake.Mode1 == 0 && fake.Mode2 == 0, "Recovered stable read"); });
            Test("persistent-zone-mismatch-makes-no-writes", delegate { var fake = new FakeTransport { Mode2 = 6 }; using (var h = new RazerHardware(new RazerProtocol(fake, false), () => true)) Throws<InvalidOperationException>(() => h.SetMode(BladeMode.Balanced)); Assert(fake.Writes == 0, "No guessed restoration"); });
            Test("power-loss-between-zone-writes-restores-balanced", delegate
            {
                var fake = new FakeTransport(); bool ac = true;
                fake.AfterRequest = delegate(byte[] r) { if (r[7] == 0x0d && r[8] == 2 && r[10] == 1 && r[11] == 4) ac = false; };
                using (var h = new RazerHardware(new RazerProtocol(fake, false), () => ac)) Throws<AggregateException>(() => h.SetCustomPerformance(1, 1));
                Assert(fake.Mode1 == 0 && fake.Mode2 == 0 && fake.CpuLevel == 2 && fake.GpuLevel == 0, "Balanced, unchanged custom records");
                Assert(!fake.Requests.Any(x => x[7] == 0x0d && x[8] == 2 && x[10] == 2 && x[11] == 4), "Second AC-only write prevented");
            });
            Test("power-loss-after-CPU-write-stops-GPU-and-restores-balanced", delegate
            {
                var fake = new FakeTransport(); bool ac = true;
                fake.AfterRequest = delegate(byte[] r) { if (r[7] == 0x0d && r[8] == 7 && r[10] == 1) ac = false; };
                using (var h = new RazerHardware(new RazerProtocol(fake, false), () => ac)) Throws<AggregateException>(() => h.SetCustomPerformance(1, 1));
                Assert(fake.Mode1 == 0 && fake.Mode2 == 0 && fake.GpuLevel == 0, "Balanced, no further level write");
                Assert(fake.Requests.Count(x => x[7] == 0x0d && x[8] == 7) == 1, "No GPU or AC-only rollback level");
            });
            Test("unknown-power-does-not-switch-rate", delegate { var s = new Settings(); Assert(PowerPolicy.DesiredRate(s, null) == null, "Unknown skips"); });
            Test("AC-DC-refresh-policy", delegate { var s = new Settings { ACRate = 120 }; Assert(PowerPolicy.DesiredRate(s, true) == 120 && PowerPolicy.DesiredRate(s, false) == 60, "AC120 DC60"); s.Paused = true; Assert(PowerPolicy.DesiredRate(s, false) == null, "Pause"); s.Paused = false; s.AutoRefresh = false; Assert(PowerPolicy.DesiredRate(s, false) == null, "Disabled"); });
            Test("backlight-keepalive-is-a-query-only", delegate { var fake = new FakeTransport { Brightness = 180 }; using (var h = new RazerHardware(new RazerProtocol(fake, true))) Assert(h.KeepBacklightAwake() == 180, "Brightness returned"); Assert(fake.Writes == 0 && fake.Requests.Single()[8] == 0x83 && fake.DeviceMode == 0, "No control-state writes"); });
            Test("backlight-keepalive-respects-Fn-off", delegate { var fake = new FakeTransport { Brightness = 0 }; using (var h = new RazerHardware(new RazerProtocol(fake, true))) Assert(h.KeepBacklightAwake() == 0, "Manual off retained"); Assert(fake.Brightness == 0 && fake.Writes == 0, "Never forces saved brightness"); });
            Test("backlight-wrong-region-rejected", delegate { var fake = new FakeTransport { BadBacklightRegion = true }; using (var h = new RazerHardware(new RazerProtocol(fake, true))) Throws<InvalidOperationException>(() => h.KeepBacklightAwake()); });
            Test("backlight-default-and-unknown-display-fail-closed", delegate { var p = new BacklightPolicy(); var s = new Settings(); p.SetDisplay(1); p.SetUnlocked(true); Assert(!p.Permitted(s), "Opt-in"); s.KeepKeyboardLit = true; p.SetDisplay(99); Assert(!p.Permitted(s), "Unknown display pauses"); p.SetDisplay(1); Assert(p.Permitted(s), "Known visible session permits"); });
            Test("backlight-off-and-dim-display-pause", delegate { var p = new BacklightPolicy(); var s = new Settings { KeepKeyboardLit = true }; p.SetUnlocked(true); foreach (int state in new[] { 0, 2 }) { p.SetDisplay(state); Assert(!p.Permitted(s), "No keepalive when screen is off or dimmed"); } });
            Test("backlight-lock-suspend-and-global-pause", delegate { var p = new BacklightPolicy(); var s = new Settings { KeepKeyboardLit = true }; p.SetDisplay(1); Assert(!p.Permitted(s), "Locked pauses"); p.SetUnlocked(true); p.SetSuspended(true); Assert(!p.Permitted(s), "Sleep pauses"); p.SetSuspended(false); s.Paused = true; Assert(!p.Permitted(s), "Global pause"); s.Paused = false; Assert(p.Permitted(s), "Resumed unlocked session"); });
            Test("external-connectors-never-classed-internal", delegate { Assert(WindowsDisplay.InternalTechnology(11), "eDP"); Assert(!WindowsDisplay.InternalTechnology(5) && !WindowsDisplay.InternalTechnology(10), "HDMI and DP external"); });
            Test("XML-key-and-mouse-import", delegate { var m = MacroImport.Xml("<Macro><Name>sample</Name><MacroEvents><MacroEvent><Type>1</Type><KeyEvent><Makecode>17</Makecode></KeyEvent></MacroEvent><MacroEvent><Type>2</Type><Delay>50</Delay><MouseEvent><MouseButton>2</MouseButton><State>1</State></MouseEvent></MacroEvent></MacroEvents></Macro>"); Assert(m.Events.Count == 2 && m.Events[0].Code == 17 && !m.Events[0].Up && m.Events[1].Up && m.Events[1].DelayMs == 50, "Imported event semantics"); });
            Test("XML-extended-key-import", delegate { var m = MacroImport.Xml("<Macro><Name>sample</Name><MacroEvents><MacroEvent><Type>1</Type><KeyEvent><Makecode>57373</Makecode><State>1</State></KeyEvent></MacroEvent></MacroEvents></Macro>"); Assert(m.Events[0].Extended && m.Events[0].Code == 29 && m.Events[0].Up, "E0 prefix"); });
            Test("XML-DTD-external-entities-blocked", delegate { Throws<System.Xml.XmlException>(() => MacroImport.Xml("<!DOCTYPE Macro [<!ENTITY secret SYSTEM 'file:///C:/Windows/win.ini'>]><Macro><Name>&secret;</Name></Macro>")); });
            Test("XML-unsupported-event-is-not-silently-dropped", delegate { Throws<InvalidDataException>(() => MacroImport.Xml("<Macro><Name>sample</Name><MacroEvents><MacroEvent><Type>9</Type></MacroEvent></MacroEvents></Macro>")); });
            Test("macro-F12-reserved", delegate { Throws<InvalidDataException>(() => Macro(new MacroEvent { Kind = "Key", Code = 0x58 }).Validate()); });
            Test("macro-arbitrary-actions-blocked", delegate { Throws<InvalidDataException>(() => Macro(new MacroEvent { Kind = "Shell", Code = 1 }).Validate()); });
            Test("macro-duration-bound", delegate { Throws<InvalidDataException>(() => Macro(new MacroEvent { Kind = "Key", Code = 17, DelayMs = 40000 }, new MacroEvent { Kind = "Key", Code = 17, DelayMs = 40000, Up = true }).Validate()); });
            Test("macro-cancellation-releases-held-key", delegate { var sent = new List<MacroEvent>(); var cancel = new CancellationTokenSource(); var m = Macro(new MacroEvent { Kind = "Key", Code = 17 }, new MacroEvent { Kind = "Key", Code = 17, Up = true }); Throws<OperationCanceledException>(() => MacroRunner.Run(m, cancel.Token, () => true, delegate(MacroEvent e) { sent.Add(e); if (!e.Up) cancel.Cancel(); })); Assert(sent.Count == 2 && sent[1].Up, "Released after cancellation"); });
            Test("macro-focus-loss-releases-held-input", delegate { var sent = new List<MacroEvent>(); bool focus = true; var m = Macro(new MacroEvent { Kind = "Mouse", Code = 1 }, new MacroEvent { Kind = "Mouse", Code = 1, Up = true }); Throws<OperationCanceledException>(() => MacroRunner.Run(m, CancellationToken.None, () => focus, delegate(MacroEvent e) { sent.Add(e); focus = false; })); Assert(sent.Count == 2 && sent[1].Up, "Mouse released"); });
            Test("macro-unmatched-down-is-cleaned-up", delegate { var sent = new List<MacroEvent>(); MacroRunner.Run(Macro(new MacroEvent { Kind = "Key", Code = 17 }), CancellationToken.None, () => true, sent.Add); Assert(sent.Count == 2 && sent[1].Up, "Synthetic release"); });
            Test("macro-cleanup-failure-is-reported", delegate { var m = Macro(new MacroEvent { Kind = "Key", Code = 17 }); Throws<AggregateException>(() => MacroRunner.Run(m, CancellationToken.None, () => true, delegate(MacroEvent e) { if (e.Up) throw new Exception("blocked"); })); });
            Test("SendInput-encoding", delegate { var key = MacroEngine.Encode(new MacroEvent { Kind = "Key", Code = 29, Extended = true, Up = true }); Assert(key.Type == 1 && key.Data.Keyboard.Flags == 11 && key.Data.Keyboard.Key == 0, "Scan code with key-up and extended"); var mouse = MacroEngine.Encode(new MacroEvent { Kind = "Mouse", Code = 5 }); Assert(mouse.Data.Mouse.Flags == 128 && mouse.Data.Mouse.Data == 2, "X2"); });
            Test("config-invalid-schema-and-mode-rejected", delegate { Throws<InvalidDataException>(() => new Settings { Version = 99 }.Validate()); Throws<ArgumentException>(() => new Settings { ACWindowsMode = "cmd.exe" }.Validate()); });
            Test("config-AC-DC-mode-restrictions", delegate { Throws<InvalidDataException>(() => new Settings { BatteryBladeMode = "Performance" }.Validate()); Throws<InvalidDataException>(() => new Settings { ACBladeMode = "BatterySaver" }.Validate()); Throws<InvalidDataException>(() => new Settings { CustomCpuLevel = 3 }.Validate()); });
            Test("legacy-battery-silent-migrates-without-enabling-auto", delegate { Store.WriteAtomic(Store.ConfigPath, Store.Serializer().Serialize(new Settings { BatteryBladeMode = "Silent", AutoBladeMode = false, KeepKeyboardLit = true })); var s = Store.Load(); Assert(s.BatteryBladeMode == "BatterySaver" && !s.AutoBladeMode && s.KeepKeyboardLit, "Only legacy battery choice migrated"); });
            Test("atomic-config-preserves-backup", delegate { var s = new Settings(); Store.Save(s); s.ACRate = 100; Store.Save(s); Assert(Store.Load().ACRate == 100 && File.Exists(Store.ConfigPath + ".bak"), "New and backup exist"); });
            Test("full-macro-capacity-round-trips", delegate
            {
                var s = new Settings();
                for (int i = 0; i < 64; i++) { var macro = new MacroDefinition { Name = new String('\u5b8f', 80) }; for (int j = 0; j < 4096; j++) macro.Events.Add(new MacroEvent { Kind = "Wheel", Code = -1200 }); s.Macros.Add(macro); }
                Store.Save(s); Assert(new FileInfo(Store.ConfigPath).Length > 2 * 1024 * 1024, "Exercises the previous capacity boundary");
                var loaded = Store.Load(); Assert(loaded.Macros.Count == 64 && loaded.Macros.All(x => x.Events.Count == 4096), "All permitted macros retained");
                Store.Save(new Settings());
            });
            Test("duplicate-macro-hotkeys-rejected", delegate { var s = new Settings(); var a = Macro(new MacroEvent { Kind = "Key", Code = 17 }); var b = Macro(new MacroEvent { Kind = "Key", Code = 18 }); a.HotKey = 117; b.HotKey = 117; s.Macros.Add(a); s.Macros.Add(b); Throws<InvalidDataException>(s.Validate); });
            Test("watchdog-command-line-quoting", delegate { Assert(DisplayGuard.Quote("C:\\folder name\\") == "\"C:\\folder name\\\\\"", "Trailing slash"); Assert(DisplayGuard.Quote("a\"b") == "\"a\\\"b\"", "Embedded quote"); });
            AdvancedTests();
            Console.WriteLine("RESULT: " + passed + " passed; " + failed + " failed."); return failed == 0 ? 0 : 1;
        }
    }
}
