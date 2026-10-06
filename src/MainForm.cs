using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RazerHelper
{
    internal sealed class Choice
    {
        public string Value, Text;
        public Choice(string value, string text) { Value = value; Text = text; }
        public override string ToString() { return Text; }
    }
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(18, 23, 29), Surface = Color.FromArgb(29, 35, 43), Raised = Color.FromArgb(42, 50, 61), Text = Color.FromArgb(228, 234, 240), Muted = Color.FromArgb(153, 168, 181), Green = Color.FromArgb(106, 224, 160);
        public static Button Button(string text, Action action, bool accent)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(88, 34), Margin = new Padding(0, 4, 8, 4), Padding = new Padding(9, 3, 9, 3), FlatStyle = FlatStyle.Flat, BackColor = accent ? Green : Raised, ForeColor = accent ? Background : Text, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = 0; b.Click += delegate { action(); }; return b;
        }
        public static Label Label(string text, bool muted)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = muted ? Muted : Text, MaximumSize = new Size(530, 0), Margin = new Padding(0, 4, 0, 6) };
        }
        public static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0) }; row.Controls.AddRange(controls); return row;
        }
        public static ComboBox Combo(params Choice[] items)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Raised, ForeColor = Text, Width = 148, Margin = new Padding(0, 8, 10, 6) }; combo.Items.AddRange(items); return combo;
        }
        public static CheckBox Check(string text, bool value)
        {
            return new CheckBox { Text = text, Checked = value, AutoSize = true, ForeColor = Text, Margin = new Padding(0, 7, 0, 7), Padding = new Padding(0, 2, 0, 2) };
        }
        public static TextBox Input(string text, int width)
        {
            return new TextBox { Text = text, Width = width, BorderStyle = BorderStyle.FixedSingle, BackColor = Raised, ForeColor = Text, Margin = new Padding(0, 8, 10, 6) };
        }
        public static Panel Card(string title, params Control[] controls)
        {
            var card = new CardPanel { BackColor = Surface, Padding = new Padding(16, 10, 16, 12), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12) };
            var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, RowCount = controls.Length + 1, Margin = new Padding(0), Padding = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var heading = Label(title, false); heading.Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold); layout.Controls.Add(heading, 0, 0);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int i = 0; i < controls.Length; i++) { layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); controls[i].Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top; layout.Controls.Add(controls[i], 0, i + 1); }
            card.Body = layout; card.Controls.Add(layout); return card;
        }
        public static void Select(ComboBox combo, string value) { for (int i = 0; i < combo.Items.Count; i++) if (((Choice)combo.Items[i]).Value == value) { combo.SelectedIndex = i; return; } combo.SelectedIndex = 0; }
        public static string Value(ComboBox combo) { return ((Choice)combo.SelectedItem).Value; }
    }
    internal sealed class CardPanel : Panel
    {
        public TableLayoutPanel Body;
        public override Size GetPreferredSize(Size proposed)
        {
            if (Body == null) return base.GetPreferredSize(proposed);
            int width = proposed.Width > 0 ? proposed.Width : Math.Max(Width, 200);
            var inner = Body.GetPreferredSize(new Size(Math.Max(1, width - Padding.Horizontal), 0));
            return new Size(width, inner.Height + Padding.Vertical);
        }
    }

    internal sealed class RefreshDialog : Form
    {
        private readonly DisplayGuard guard;
        private readonly Timer countdown;
        private readonly Label message;
        private int seconds = 13;
        public RefreshDialog(DisplayGuard guard, int rate)
        {
            this.guard = guard; Text = "确认刷新率"; ClientSize = new Size(405, 160); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; BackColor = Theme.Background; ForeColor = Theme.Text; Font = new Font("Microsoft YaHei UI", 9); Padding = new Padding(20);
            message = Theme.Label("已切换到 " + rate + " Hz。\n13 秒内确认，否则自动恢复。", false); message.Dock = DockStyle.Top;
            var buttons = Theme.Row(Theme.Button("保留设置", delegate { guard.Confirm(); DialogResult = DialogResult.OK; Close(); }, true), Theme.Button("恢复", delegate { guard.Revert(); Close(); }, false)); buttons.Dock = DockStyle.Bottom;
            Controls.Add(message); Controls.Add(buttons);
            countdown = new Timer { Interval = 1000 }; countdown.Tick += delegate { seconds--; message.Text = "屏幕显示正常时保留设置。\n" + seconds + " 秒后自动恢复。"; if (seconds <= 0) Close(); }; countdown.Start();
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { countdown.Dispose(); guard.Dispose(); base.OnFormClosed(e); }
    }

    internal sealed class MainForm : Form
    {
        private readonly Controller controller;
        private readonly Settings settings;
        private readonly bool preview;
        private readonly bool startTray;
        private readonly List<Panel> pages = new List<Panel>();
        private readonly Panel content;
        private readonly Label status, machine, hardwareStatus, powerStatus, displayStatus, servicesStatus, lightingStatus, memoryStatus;
        private readonly NotifyIcon tray;
        private readonly Timer powerDebounce, statusTimer;
        private readonly ToolTip details = new ToolTip { AutoPopDelay = 15000, InitialDelay = 300 };
        private MacroEngine macros;
        private MacroBindingEngine bindings;
        internal bool SkipStartupPolicy { get; set; }
        private BacklightMonitor backlight;
        private readonly ListBox macroList;
        private readonly TextBox macroName, macroTarget;
        private readonly ComboBox macroHotkey, effect, rates, acBlade, dcBlade, cpuLevel, gpuLevel;
        private readonly Button applyAcBlade, applyDcBlade, applyCustom;
        private readonly Label customStatus, macroPlaybackStatus;
        private readonly TrackBar brightness;
        private readonly CheckBox autoRefresh, startup, paused;
        private Color chosenColor;
        private bool closing, updating, refreshing, displayPreview, reapplyLighting;
        private readonly uint showMessage = Program.ShowMessage, quitMessage = Program.QuitMessage;
        public MainForm(Controller controller, bool startTray, bool preview)
        {
            this.controller = controller; settings = controller.Settings; this.startTray = startTray; this.preview = preview;
            Text = "Razer Helper " + typeof(MainForm).Assembly.GetName().Version.ToString(3) + " · 本地控制"; Font = new Font("Microsoft YaHei UI", 9); BackColor = Theme.Background; ForeColor = Theme.Text; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(640, 880); MinimumSize = new Size(565, 620); StartPosition = FormStartPosition.CenterScreen; Icon = Brand.Icon();
            var header = new Panel { Dock = DockStyle.Top, Height = 98, Padding = new Padding(24, 16, 24, 5) };
            var title = Theme.Label("RAZER HELPER", false); title.Font = new Font("Segoe UI", 18, FontStyle.Bold); title.ForeColor = Theme.Green; title.Dock = DockStyle.Top;
            machine = Theme.Label(HidDiscovery.Model() + "  ·  离线可用", true); machine.Dock = DockStyle.Bottom; header.Controls.Add(title); header.Controls.Add(machine);
            var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 49, Padding = new Padding(24, 0, 12, 0), WrapContents = false };
            string[] tabNames = { "性能 / 屏幕", "键盘灯效", "宏", "设置" };
            for (int i = 0; i < tabNames.Length; i++) { int index = i; tabs.Controls.Add(Theme.Button(tabNames[i], delegate { ShowPage(index); }, false)); }
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 73, Padding = new Padding(24, 8, 24, 8) };
            status = Theme.Label("准备读取本机状态…", true); status.AutoSize = false; status.AutoEllipsis = true; status.Height = 32; status.Dock = DockStyle.Top;
            memoryStatus = Theme.Label("关闭窗口后继续在托盘运行", true); memoryStatus.Font = new Font("Microsoft YaHei UI", 8); memoryStatus.Dock = DockStyle.Bottom;
            footer.Controls.Add(status); footer.Controls.Add(memoryStatus);
            content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 10, 24, 0) };
            Controls.Add(content); Controls.Add(footer); Controls.Add(tabs); Controls.Add(header);

            hardwareStatus = Theme.Label("读取性能模式与风扇…", true); powerStatus = Theme.Label("读取 Windows 电源模式…", true); displayStatus = Theme.Label("识别内置屏幕…", true);
            rates = Theme.Combo(new Choice("120", "120 Hz")); rates.SelectedIndex = 0;
            autoRefresh = Theme.Check("拔电自动 60 Hz；接电恢复所选刷新率", settings.AutoRefresh);
            autoRefresh.CheckedChanged += delegate { settings.AutoRefresh = autoRefresh.Checked; Save(); QueuePower(); };
            rates.SelectedIndexChanged += delegate { if (!updating && rates.SelectedItem != null) { settings.ACRate = Int32.Parse(Theme.Value(rates)); Save(); } };
            acBlade = Theme.Combo(new Choice("Balanced", "平衡"), new Choice("Silent", "安静"), new Choice("Performance", "性能"), new Choice("Custom", "自定义")); Theme.Select(acBlade, settings.ACBladeMode);
            dcBlade = Theme.Combo(new Choice("Balanced", "平衡"), new Choice("BatterySaver", "省电")); Theme.Select(dcBlade, settings.BatteryBladeMode);
            acBlade.SelectedIndexChanged += delegate { if (updating) return; settings.ACBladeMode = Theme.Value(acBlade); if (Save() && settings.AutoBladeMode) QueuePower(); };
            dcBlade.SelectedIndexChanged += delegate { if (updating) return; settings.BatteryBladeMode = Theme.Value(dcBlade); if (Save() && settings.AutoBladeMode) QueuePower(); };
            applyAcBlade = Theme.Button("应用接电预设", delegate { ApplyBlade(true); }, true);
            applyDcBlade = Theme.Button("应用电池预设", delegate { ApplyBlade(false); }, false);
            applyAcBlade.Enabled = applyDcBlade.Enabled = false;
            var autoBlade = Theme.Check("随插拔电源应用以上 Blade 预设", settings.AutoBladeMode);
            autoBlade.CheckedChanged += delegate { settings.AutoBladeMode = autoBlade.Checked; if (Save()) QueuePower(); };
            cpuLevel = Theme.Combo(new Choice("0", "CPU：低"), new Choice("1", "CPU：中"), new Choice("2", "CPU：高")); Theme.Select(cpuLevel, settings.CustomCpuLevel.ToString());
            gpuLevel = Theme.Combo(new Choice("0", "GPU：低"), new Choice("1", "GPU：中"), new Choice("2", "GPU：高")); Theme.Select(gpuLevel, settings.CustomGpuLevel.ToString());
            cpuLevel.Width = gpuLevel.Width = 135;
            cpuLevel.SelectedIndexChanged += delegate { SaveCustomLevels(); }; gpuLevel.SelectedIndexChanged += delegate { SaveCustomLevels(); };
            customStatus = Theme.Label("读取自定义档位记录…", true);
            applyCustom = Theme.Button("应用自定义（接电）", delegate
            {
                SaveCustomLevels(); settings.ACBladeMode = "Custom";
                updating = true; Theme.Select(acBlade, "Custom"); updating = false;
                if (Save()) ApplyBlade(true);
            }, true); applyCustom.Enabled = false;
            var windowsButtons = Theme.Row(PowerButton("Efficiency", "最佳能效"), PowerButton("Balanced", "平衡"), PowerButton("Performance", "最佳性能"));
            AddPage(Theme.Card("Blade 性能模式", hardwareStatus, Theme.Row(Theme.Label("接电预设", true), acBlade, applyAcBlade), Theme.Row(Theme.Label("电池预设", true), dcBlade, applyDcBlade), autoBlade), Theme.Card("自定义性能档位", customStatus, Theme.Row(cpuLevel, gpuLevel), Theme.Row(applyCustom), Theme.Label("选择自定义模式后生效，风扇由固件自动管理。\n档位由固件分配功率；CPU 电压优化暂未接入。", true)), Theme.Card("Windows 电源模式", powerStatus, windowsButtons, Theme.Row(Theme.Button("进阶能耗调节（手动）",AdvancedPower,false))), Theme.Card("内置屏幕", displayStatus, Theme.Row(Theme.Button("60 Hz", delegate { PreviewRate(60); }, false), rates, Theme.Button("切换并确认", delegate { PreviewRate(Int32.Parse(Theme.Value(rates))); }, false)), autoRefresh));

            chosenColor = Color.FromArgb(settings.Red, settings.Green, settings.Blue);
            brightness = new TrackBar { Minimum = 0, Maximum = 100, Value = settings.Brightness, TickStyle = TickStyle.None, BackColor = Theme.Surface, Width = 350, Height = 38, Margin = new Padding(0) };
            var brightnessText = Theme.Label(settings.Brightness + "%", false); brightnessText.Margin = new Padding(8, 10, 0, 0);
            brightness.ValueChanged += delegate { brightnessText.Text = brightness.Value + "%"; };
            effect = Theme.Combo(new Choice("Static", "常亮"), new Choice("Breathing", "呼吸"), new Choice("Spectrum", "光谱循环"), new Choice("Wave", "波浪"), new Choice("Off", "关闭灯效")); Theme.Select(effect, settings.Effect);
            var direction = Theme.Combo(new Choice("1", "波浪向右"), new Choice("2", "波浪向左")); Theme.Select(direction, settings.Direction.ToString());
            Button color = null; color = Theme.Button("选择颜色", delegate { using (var dialog = new ColorDialog { Color = chosenColor, FullOpen = true }) if (dialog.ShowDialog(this) == DialogResult.OK) { chosenColor = dialog.Color; color.BackColor = chosenColor; color.ForeColor = chosenColor.GetBrightness() > 0.5 ? Color.Black : Color.White; } }, false); color.BackColor = chosenColor; color.ForeColor = Color.Black;
            var apply = Theme.Button("应用灯效", delegate
            {
                settings.Effect = Theme.Value(effect); settings.Direction = Int32.Parse(Theme.Value(direction)); settings.Red = chosenColor.R; settings.Green = chosenColor.G; settings.Blue = chosenColor.B; settings.Brightness = brightness.Value;
                if (!Save()) return;
                LightEffect selected = (LightEffect)Enum.Parse(typeof(LightEffect), settings.Effect); var selectedColor = chosenColor; int selectedDirection = settings.Direction; int raw = (int)Math.Round(brightness.Value * 2.55);
                controller.Hardware("键盘灯效", delegate(RazerHardware h) { h.SetEffect(selected, selectedColor.R, selectedColor.G, selectedColor.B, selectedDirection); h.SetBrightness(raw); });
            }, true);
            var applyBrightness = Theme.Button("仅应用亮度", delegate { settings.Brightness = brightness.Value; if (Save()) { int raw = (int)Math.Round(brightness.Value * 2.55); controller.Hardware("键盘亮度", h => h.SetBrightness(raw)); } }, false);
            lightingStatus = Theme.Label("读取当前灯效…", true);
            var restoreLight = Theme.Check("启动 / 唤醒时应用已保存的灯效与亮度", settings.RestoreLightingOnStart); restoreLight.CheckedChanged += delegate { settings.RestoreLightingOnStart = restoreLight.Checked; Save(); };
            var dim = Theme.Check("电池供电时降低键盘亮度", settings.DimOnBattery); dim.CheckedChanged += delegate { settings.DimOnBattery = dim.Checked; Save(); QueuePower(); };
            var keepLit = Theme.Check("屏幕亮着时保持背光（避免闲置渐暗）", settings.KeepKeyboardLit);
            keepLit.CheckedChanged += delegate
            {
                bool old = settings.KeepKeyboardLit; settings.KeepKeyboardLit = keepLit.Checked;
                if (!Save()) { settings.KeepKeyboardLit = old; if (keepLit.Checked != old) keepLit.Checked = old; }
                if (backlight != null) backlight.Refresh();
            };
            var batteryBrightness = new NumericUpDown { Minimum = 0, Maximum = 100, Value = settings.BatteryBrightness, Width = 80, BackColor = Theme.Raised, ForeColor = Theme.Text, Margin = new Padding(0, 7, 8, 5) }; batteryBrightness.ValueChanged += delegate { settings.BatteryBrightness = (int)batteryBrightness.Value; Save(); };
            AddPage(Theme.Card("键盘背光", lightingStatus, Theme.Row(brightness, brightnessText), Theme.Row(applyBrightness)), Theme.Card("灯效", Theme.Row(effect, color), Theme.Row(direction, apply), Theme.Label("常亮和呼吸使用所选颜色，光谱与波浪由键盘运行。", true)), Theme.Card("灯光自动策略", keepLit, Theme.Label("锁屏、熄屏和睡眠时暂停；Fn 调暗或关灯仍有效。", true), restoreLight, dim, Theme.Row(Theme.Label("电池亮度 (%)", true), batteryBrightness)));

            macroList = new ListBox { BackColor = Theme.Raised, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Height = 110, Dock = DockStyle.Top, IntegralHeight = false };
            macroName = Theme.Input("新宏", 170); macroTarget = Theme.Input("", 190);
            macroHotkey = Theme.Combo(new Choice("0", "无快捷键"), new Choice("117", "Ctrl+Alt+F6"), new Choice("118", "Ctrl+Alt+F7"), new Choice("119", "Ctrl+Alt+F8"), new Choice("120", "Ctrl+Alt+F9"), new Choice("121", "Ctrl+Alt+F10"), new Choice("122", "Ctrl+Alt+F11")); Theme.Select(macroHotkey, "0");
            macroHotkey.Width = 200;
            macroPlaybackStatus=Theme.Label("点击“3 秒后播放一次”后，请切换到要输入的窗口。F12 停止。",true);
            macroList.SelectedIndexChanged += delegate { var selected = SelectedMacro(); if (selected != null) { macroName.Text = selected.Name; macroTarget.Text = selected.TargetProcess; Theme.Select(macroHotkey, selected.HotKey.ToString()); } };
            AddPage(Theme.Card("本地宏", macroList, Theme.Row(Theme.Button("新建宏",NewMacro,false),Theme.Button("编辑所选宏",EditMacro,false),Theme.Button("只读扫描雷云宏",ScanSynapse,false)),Theme.Row(Theme.Button("导入 XML / JSON", ImportMacro, false), Theme.Button("导出", ExportMacro, false), Theme.Button("查看步骤", PreviewMacro, false)), Theme.Row(Theme.Button("3 秒后播放一次", PlayMacro, true), Theme.Button("停止 (F12)", delegate { if (macros != null) macros.Stop(); }, false), Theme.Button("删除", DeleteMacro, false)),macroPlaybackStatus),Theme.Card("按键绑定",Theme.Row(Theme.Button("Hypershift / 自定义组合键",EditBindings,false)),Theme.Label("宏编辑与按键绑定分别保存。选择组合键并勾选启用后，点击“保存并生效”。",true)), Theme.Card("录制与快捷键", Theme.Row(Theme.Label("名称", true), macroName), Theme.Row(Theme.Label("目标程序 (可选)", true), macroTarget), Theme.Row(Theme.Button("3 秒后开始录制", RecordMacro, false), Theme.Button("停止录制", delegate { if (macros != null) macros.StopRecording(); }, false)), Theme.Row(macroHotkey, Theme.Button("保存快捷键", BindMacro, false)), Theme.Label("Esc 或 F12 停止录制；F12 停止播放。录制最多 60 秒。\n只在手动录制时记录按键，窗口切换后停止播放。", true)));

            startup = Theme.Check("登录 Windows 后在托盘启动", !preview && Startup.Enabled()); startup.CheckedChanged += delegate { if (updating || preview) return; try { Startup.Set(startup.Checked); Notify("已更新本程序启动项。", false); } catch (Exception e) { updating = true; startup.Checked = Startup.Enabled(); updating = false; Notify(e.Message, true); } };
            paused = Theme.Check("暂停所有自动策略", settings.Paused); paused.CheckedChanged += delegate { settings.Paused = paused.Checked; Save(); QueuePower(); if (backlight != null) backlight.Refresh(); };
            var autoWindows = Theme.Check("随插拔电源切换 Windows 电源模式", settings.AutoWindowsMode); autoWindows.CheckedChanged += delegate { settings.AutoWindowsMode = autoWindows.Checked; Save(); QueuePower(); };
            var acWindows = Theme.Combo(new Choice("Efficiency", "接电：最佳能效"), new Choice("Balanced", "接电：平衡"), new Choice("Performance", "接电：最佳性能")); Theme.Select(acWindows, settings.ACWindowsMode);
            var dcWindows = Theme.Combo(new Choice("Efficiency", "电池：最佳能效"), new Choice("Balanced", "电池：平衡")); Theme.Select(dcWindows, settings.BatteryWindowsMode);
            acWindows.Width = 200; dcWindows.Width = 200;
            acWindows.SelectedIndexChanged += delegate { settings.ACWindowsMode = Theme.Value(acWindows); Save(); }; dcWindows.SelectedIndexChanged += delegate { settings.BatteryWindowsMode = Theme.Value(dcWindows); Save(); };
            servicesStatus = Theme.Label("雷云状态待读取", true);
            AddPage(Theme.Card("运行", startup, paused, Theme.Row(Theme.Button("配置与日志", delegate { Open(Store.DirectoryPath); }, false), Theme.Button("恢复初始设置", Restore, false))), Theme.Card("Windows 电源自动策略", autoWindows, Theme.Row(acWindows, dcWindows)), Theme.Card("电池保护", Theme.Label("使用设备已有的 80% 充电上限，可随时恢复充满。", true), Theme.Row(Theme.Button("充电上限 80%", delegate { controller.Hardware("充电上限", h => h.SetChargeLimit(true)); }, false), Theme.Button("恢复充满", delegate { controller.Hardware("恢复充满", h => h.SetChargeLimit(false)); }, false))), Theme.Card("雷云与兼容性", servicesStatus, Theme.Label("雷云或 Chroma 可能覆盖设置。可在其托盘菜单退出，\n然后在 Windows 启动应用中关闭它的自启。", true), Theme.Row(Theme.Button("启动应用", delegate { Open("ms-settings:startupapps"); }, false), Theme.Button("查看服务", ShowServices, false), Theme.Button("启用原生 Fn", delegate { controller.Hardware("原生 Fn", h => h.NativeFnMode()); }, false))));

            var menu = new ContextMenuStrip(); menu.Items.Add("打开 Razer Helper", null, delegate { ShowWindow(); }); menu.Items.Add("内置屏幕 60 Hz", null, delegate { ShowWindow(); PreviewRate(60); }); menu.Items.Add("暂停 / 恢复自动策略", null, delegate { paused.Checked = !paused.Checked; }); menu.Items.Add("停止宏 (F12)", null, delegate { if (macros != null) macros.Stop(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出", null, delegate { Exit(); });
            tray = new NotifyIcon { Icon = Icon, Text = "Razer Helper · 本地控制", ContextMenuStrip = menu, Visible = !preview }; tray.DoubleClick += delegate { ShowWindow(); };
            powerDebounce = new Timer { Interval = 1800 }; powerDebounce.Tick += delegate { powerDebounce.Stop(); if (displayPreview) { QueuePower(); return; } bool lighting = reapplyLighting; reapplyLighting = false; controller.ApplyPower(lighting); RefreshStatus(); };
            statusTimer = new Timer { Interval = 10000 }; statusTimer.Tick += delegate { if (Visible && BacklightMonitor.RecentInput() && BacklightMonitor.InteractiveDesktop()) RefreshStatus(); }; if (!preview) statusTimer.Start();
            controller.Notice += Notify;
            ShowPage(0); ReloadMacros();
        }
        private void SaveCustomLevels()
        {
            if (updating || cpuLevel.SelectedItem == null || gpuLevel.SelectedItem == null) return;
            settings.CustomCpuLevel = Int32.Parse(Theme.Value(cpuLevel)); settings.CustomGpuLevel = Int32.Parse(Theme.Value(gpuLevel)); settings.CustomLevelsConfigured = true; Save();
        }
        private void ApplyBlade(bool onAC)
        {
            var mode = (BladeMode)Enum.Parse(typeof(BladeMode), onAC ? settings.ACBladeMode : settings.BatteryBladeMode);
            int cpu = settings.CustomCpuLevel, gpu = settings.CustomGpuLevel;
            controller.Hardware("Blade " + RazerHardware.ModeLabel((int)mode), delegate(RazerHardware h)
            {
                if (WindowsPower.Read().OnAC != onAC) throw new InvalidOperationException("供电状态已变化，请应用当前供电方式的预设。");
                if (mode == BladeMode.Custom) h.SetCustomPerformance(cpu, gpu); else h.SetMode(mode);
            });
        }
        private Button PowerButton(string mode, string text) { return Theme.Button(text, delegate { controller.Run("Windows " + text, delegate { WindowsPower.Set(mode); }); }, false); }
        private void AddPage(params Panel[] cards)
        {
            var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Visible = false, BackColor = Theme.Background };
            var stack = new TableLayoutPanel { ColumnCount = 1, RowCount = cards.Length, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0), Margin = new Padding(0) }; stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < cards.Length; i++) { stack.RowStyles.Add(new RowStyle(SizeType.AutoSize)); stack.Controls.Add(cards[i], 0, i); }
            page.Controls.Add(stack); pages.Add(page); content.Controls.Add(page);
        }
        private void ShowPage(int index) { for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == index; pages[index].BringToFront(); }
        public void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); if (!preview) RefreshStatus(); }
        public void Exit() { closing = true; if (macros != null) macros.Stop(); Close(); }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e); var working = Screen.FromControl(this).WorkingArea; if (Height > working.Height - 40) Height = Math.Max(MinimumSize.Height, working.Height - 40); if (preview) return;
            macros = new MacroEngine(); macros.Recorded += delegate(MacroDefinition macro) { AddMacro(macro, "录制已保存：" + macro.Events.Count + " 步。"); if(!closing)ShowWindow(); }; macros.Notice += delegate(string message) { ReportMacro(message,message.StartsWith("宏播放失败：",StringComparison.Ordinal)); };
            RegisterMacroKeys(); RebuildBindings();
            backlight = new BacklightMonitor(Handle, controller, Notify); backlight.Start();
            RefreshStatus(); if(!SkipStartupPolicy)controller.ApplyPower(true);
            if (startTray) BeginInvoke((Action)delegate { Hide(); });
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!closing && !preview && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            if(bindings!=null){bindings.Dispose();bindings=null;}if (macros != null) macros.Dispose(); powerDebounce.Stop(); statusTimer.Stop(); tray.Visible = false;
            if (backlight != null) backlight.Dispose();
            base.OnFormClosing(e);
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (statusTimer != null && !preview) { if (Visible) statusTimer.Start(); else statusTimer.Stop(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if(bindings!=null)bindings.Dispose(); if (backlight != null) backlight.Dispose(); if (tray != null) tray.Dispose(); details.Dispose(); if (powerDebounce != null) powerDebounce.Dispose(); if (statusTimer != null) statusTimer.Dispose(); if (controller != null) controller.Notice -= Notify; }
            base.Dispose(disposing);
        }
        protected override void WndProc(ref Message m)
        {
            if (backlight != null) backlight.HandleMessage(ref m);
            if(m.Msg==0xff && bindings!=null)bindings.Message(m.LParam);
            if (m.Msg == showMessage) { ShowWindow(); return; }
            if (m.Msg == quitMessage) { Exit(); return; }
            if (m.Msg == 0x312 && macros != null)
            {
                int id = m.WParam.ToInt32(); if (id == 900) macros.Stop();
                else if (id >= 1000 && id < 1000 + settings.Macros.Count) { try { macros.Play(settings.Macros[id - 1000], settings.Macros, false); } catch (Exception e) { ReportMacro("宏未启动："+e.Message, true); } }
                return;
            }
            if (m.Msg == 0x218 && (m.WParam.ToInt32() == 10 || m.WParam.ToInt32() == 18 || m.WParam.ToInt32() == 7)) { if (m.WParam.ToInt32() != 10) reapplyLighting = true; QueuePower(); }
            if (m.Msg == 0x7e && !preview && !displayPreview && IsHandleCreated) BeginInvoke((Action)RefreshStatus);
            base.WndProc(ref m);
        }
        private void QueuePower() { if (preview || powerDebounce == null) return; powerDebounce.Stop(); powerDebounce.Start(); }
        private void ReportMacro(string message,bool error)
        {
            if(IsDisposed || !IsHandleCreated)return;
            if(InvokeRequired){BeginInvoke((Action)delegate{ReportMacro(message,error);});return;}
            Log.Write(message);macroPlaybackStatus.Text=message;macroPlaybackStatus.ForeColor=error?Color.FromArgb(244,158,143):Theme.Text;Notify(message,error);
        }
        private bool Save() { if (preview) return true; try { Store.Save(settings); return true; } catch (Exception e) { Notify("配置未保存：" + e.Message, true); return false; } }
        private void Notify(string text, bool error)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke((Action)delegate { Notify(text, error); }); return; }
            status.Text = text; details.SetToolTip(status, text); status.ForeColor = error ? Color.FromArgb(244, 158, 143) : Theme.Green;
            if (error && !Visible && !preview) tray.ShowBalloonTip(4000, "Razer Helper", text, ToolTipIcon.Warning);
            if (Visible && !preview) RefreshStatus();
        }
        private async void RefreshStatus()
        {
            if (refreshing || preview || IsDisposed) return; refreshing = true;
            try
            {
                var snapshot = await Task.Run(delegate
                {
                    var p = WindowsPower.Read(); var d = WindowsDisplay.Read(); HardwareState h = null; string error = null;
                    try { using (var hardware = RazerHardware.Open(true)) h = hardware.Read(); } catch (Exception e) { error = e.Message; }
                    var processes = System.Diagnostics.Process.GetProcessesByName("RazerAppEngine"); long memory = 0; foreach (var process in processes) using (process) try { memory += process.WorkingSet64; } catch { }
                    return new { Power = p, Displays = d, Hardware = h, Error = error, Count = processes.Length, Memory = memory };
                });
                if (IsDisposed) return;
                SetStatus(snapshot.Power, snapshot.Displays, snapshot.Hardware, snapshot.Error, snapshot.Count, snapshot.Memory);
            }
            catch (Exception e) { status.Text = "读取状态：" + e.Message; status.ForeColor = Theme.Muted; }
            finally { refreshing = false; }
        }
        private void SetStatus(PowerInfo p, List<DisplayInfo> displays, HardwareState h, string error, int synapse, long memory)
        {
            powerStatus.Text = (p.OnAC == true ? "接通电源" : p.OnAC == false ? "电池供电" : "电源状态未知") + " · 电量 " + (p.BatteryPercent.HasValue ? p.BatteryPercent + "%" : "未知") + " · " + WindowsPower.Label(p.Mode);
            var screen = displays.FirstOrDefault(x => x.Internal);
            displayStatus.Text = screen == null ? "内置屏幕不可用或处于复制模式。" : screen.ToString();
            updating = true;
            if (screen != null)
            {
                string desired = settings.ACRate.ToString(); rates.Items.Clear(); foreach (int rate in screen.Rates) rates.Items.Add(new Choice(rate.ToString(), rate + " Hz"));
                if (rates.Items.Count != 0) Theme.Select(rates, desired); rates.Enabled = rates.Items.Count != 0;
            }
            updating = false;
            hardwareStatus.Text = h == null ? "硬件暂不可用：" + error : RazerHardware.ModeLabel(h.Mode) + " · " + (h.FanMode == 0 ? "自动风扇" : "手动风扇") + " · " + h.Fan1Rpm + " / " + h.Fan2Rpm + " RPM";
            bool controllable = h != null && h.FanMode == 0 && BladePerformancePolicy.Known(h.Mode) && (h.Mode != 4 || BladePerformancePolicy.LevelsRestorable(h.CpuLevel, h.GpuLevel));
            applyAcBlade.Enabled = controllable && p.OnAC == true;
            applyDcBlade.Enabled = controllable && p.OnAC == false;
            applyCustom.Enabled = controllable && p.OnAC == true && BladePerformancePolicy.LevelsRestorable(h.CpuLevel, h.GpuLevel);
            customStatus.Text = h == null ? "自定义档位暂不可读。" : "自定义档位记录：CPU " + BladePerformancePolicy.LevelLabel(h.CpuLevel) + " · GPU " + BladePerformancePolicy.LevelLabel(h.GpuLevel) + (h.Mode == 4 ? "（当前生效）" : "（自定义时生效）");
            if (h != null && !settings.CustomLevelsConfigured && h.CpuLevel.HasValue && h.CpuLevel <= 2 && h.GpuLevel.HasValue && h.GpuLevel <= 2)
            {
                settings.CustomCpuLevel = h.CpuLevel.Value; settings.CustomGpuLevel = h.GpuLevel.Value; settings.CustomLevelsConfigured = true;
                updating = true; Theme.Select(cpuLevel, settings.CustomCpuLevel.ToString()); Theme.Select(gpuLevel, settings.CustomGpuLevel.ToString()); updating = false; Save();
            }
            details.SetToolTip(hardwareStatus, h == null ? error : "固件模式代码 " + h.Mode + "，风扇代码 " + h.FanMode + "。接电与电池预设分别保存。手动风扇需先在雷云改为自动。");
            lightingStatus.Text = h == null ? "暂未读取到键盘状态。" : "当前亮度 " + Math.Round(h.Brightness / 2.55) + "% · 灯效记录 " + EffectLabel(h.Effect) + " · 充电上限 " + (h.ChargeLimit.HasValue ? h.ChargeLimit + "%" : "未知");
            servicesStatus.Text = synapse == 0 ? "雷云界面未运行。" : "雷云界面 " + synapse + " 个进程 · " + Math.Round(memory / 1048576.0) + " MB";
            using (var process = System.Diagnostics.Process.GetCurrentProcess()) memoryStatus.Text = "本程序 " + Math.Round(process.WorkingSet64 / 1048576.0, 1) + " MB · 关闭窗口后在托盘运行";
            string tip = "Razer Helper · " + (p.OnAC == true ? "AC" : "电池") + " " + p.BatteryPercent + "%" + (screen == null ? "" : " · " + screen.Hz + " Hz"); tray.Text = tip.Substring(0, Math.Min(63, tip.Length));
        }
        private static string EffectLabel(int? value) { return value == 0 ? "关闭" : value == 1 || value == 5 ? "常亮" : value == 2 ? "呼吸" : value == 3 ? "光谱循环" : value == 4 ? "波浪" : "自定义"; }
        private async void PreviewRate(int rate)
        {
            if (preview || displayPreview) return; displayPreview = true;
            try
            {
                var screen = WindowsDisplay.BuiltIn(); if (screen.Hz == rate) { Notify("已经是 " + rate + " Hz。", false); return; }
                var guard = await Task.Run(delegate { return new DisplayGuard(screen, rate); });
                if (IsDisposed) { guard.Dispose(); return; }
                using (var dialog = new RefreshDialog(guard, rate)) dialog.ShowDialog(this);
                Notify("屏幕设置已处理。", false);
            }
            catch (Exception e) { Notify(e.Message, true); }
            finally { displayPreview = false; RefreshStatus(); }
        }
        private MacroDefinition SelectedMacro() { return macroList.SelectedItem as MacroDefinition; }
        private void ReloadMacros() { macroList.Items.Clear(); foreach (var macro in settings.Macros) macroList.Items.Add(macro); if (macroList.Items.Count != 0) macroList.SelectedIndex = 0; }
        private void AddMacro(MacroDefinition macro, string success)
        {
            if(SaveMacroBatch(new List<MacroDefinition>{macro})){macroList.SelectedItem=macro;Notify(success,false);}
        }
        private bool SaveMacroBatch(List<MacroDefinition> incoming)
        {
            if(settings.Macros.Count+incoming.Count>64){Notify("宏数量上限为 64，未导入。",true);return false;}
            int count=settings.Macros.Count;settings.Macros.AddRange(incoming);
            if(!Save()){settings.Macros.RemoveRange(count,incoming.Count);return false;}
            ReloadMacros();RegisterMacroKeys();RebuildBindings();Notify("已保存 "+incoming.Count+" 个宏，绑定未自动启用。",false);return true;
        }
        private void AdvancedPower(){if(preview)return;using(var form=new AdvancedPowerForm(controller))form.ShowDialog(this);}
        private void NewMacro(){if(preview)return;var initial=new MacroDefinition{Events=new List<MacroEvent>{new MacroEvent{Kind="Delay",DelayMs=100}}};using(var form=new MacroEditorForm(initial,settings.Macros.Concat(new[]{initial})))if(form.ShowDialog(this)==DialogResult.OK)AddMacro(form.Result,"新宏已保存。请在独立绑定页选择按键。");}
        private void EditMacro()
        {
            var selected=SelectedMacro();if(preview || selected==null)return;
            using(var form=new MacroEditorForm(selected,settings.Macros))if(form.ShowDialog(this)==DialogResult.OK)
            {
                int index=settings.Macros.IndexOf(selected);settings.Macros[index]=form.Result;
                if(!Save()){settings.Macros[index]=selected;return;}ReloadMacros();macroList.SelectedItem=form.Result;RegisterMacroKeys();RebuildBindings();Notify("宏编辑已保存。",false);
            }
        }
        private void ScanSynapse(){if(preview)return;using(var form=new SynapseScanForm(settings,SaveMacroBatch))form.ShowDialog(this);}
        private void RebuildBindings()
        {
            if(bindings!=null){bindings.Dispose();bindings=null;}if(preview || closing || macros==null)return;
            try{bindings=new MacroBindingEngine(Handle,settings,macros);bindings.Notice+=delegate(string message){ReportMacro(message,true);};}catch(Exception ex){ReportMacro("宏绑定未启用："+ex.Message,true);}
        }
        private void EditBindings()
        {
            if(preview)return;if(macros!=null)macros.Stop();if(bindings!=null){bindings.Dispose();bindings=null;}
            try
            {
                using(var form=new MacroBindingsForm(settings))if(form.ShowDialog(this)==DialogResult.OK)
                {
                    var old=settings.MacroBindings;var oldFn=settings.FnSignal;settings.MacroBindings=form.Result;settings.FnSignal=form.FnResult;
                    if(!Save()){settings.MacroBindings=old;settings.FnSignal=oldFn;}else Notify("宏按键绑定已保存。",false);
                }
            }
            finally{RebuildBindings();}
        }
        private void RegisterMacroKeys()
        {
            if (preview || !IsHandleCreated) return; for (int i = 0; i < 64; i++) Native.UnregisterHotKey(Handle, 1000 + i);
            for (int i = 0; i < settings.Macros.Count; i++) if (settings.Macros[i].HotKey != 0 && !Native.RegisterHotKey(Handle, 1000 + i, 0x4003, (uint)settings.Macros[i].HotKey)) Notify("快捷键被其他程序占用：" + settings.Macros[i].Name, true);
        }
        private void ImportMacro()
        {
            if (preview) return;
            using (var dialog = new OpenFileDialog { Filter = "宏文件|*.xml;*.json", Multiselect = false }) if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                try { if (settings.Macros.Count >= 64) throw new InvalidOperationException("宏数量已达到上限。"); var macro = MacroImport.Read(dialog.FileName); AddMacro(macro, "已导入，请查看步骤后绑定快捷键。"); }
                catch (Exception e) { Notify("导入失败：" + e.Message, true); }
            }
        }
        private void ExportMacro()
        {
            var macro = SelectedMacro(); if (macro == null) return;
            using (var dialog = new SaveFileDialog { Filter = "Razer Helper 宏|*.json", FileName = "macro.json" }) if (dialog.ShowDialog(this) == DialogResult.OK) try { Store.WriteAtomic(dialog.FileName, Store.Serializer().Serialize(macro)); Notify("宏已导出。", false); } catch (Exception e) { Notify(e.Message, true); }
        }
        private void PreviewMacro()
        {
            var macro = SelectedMacro(); if (macro == null) return;
            string text = macro.Name + "\r\n目标：" + (macro.TargetProcess.Length == 0 ? "当前窗口" : macro.TargetProcess) + "\r\n\r\n" + String.Join("\r\n", macro.Events.Select((x, i) => (i + 1) + ". 等待 " + x.DelayMs + " ms · " + x.Description()));
            using (var dialog = new Form { Text = "宏步骤", Size = new Size(540, 480), StartPosition = FormStartPosition.CenterParent }) { dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Text = text, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Font, BackColor = Theme.Background, ForeColor = Theme.Text }); dialog.ShowDialog(this); }
        }
        private void PlayMacro() { var macro = SelectedMacro(); if (macro == null || macros == null) return; try { macros.Play(macro, settings.Macros, true); } catch (Exception e) { ReportMacro("宏未启动："+e.Message, true); } }
        private async void RecordMacro()
        {
            if (macros == null || macros.Playing || macros.Recording) return;
            if (settings.Macros.Count >= 64) { Notify("宏数量已达到 64；请先导出并删除不需要的宏。", true); return; }
            string name = macroName.Text, target = macroTarget.Text.Trim();
            if (String.IsNullOrWhiteSpace(name) || name.Length > 80 || target.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { Notify("名称或目标程序无效。", true); return; }
            Notify("3 秒后开始录制，Esc / F12 停止。", false); Hide();
            for (int i = 0; i < 150; i++)
            {
                await Task.Delay(20);
                if (IsDisposed || closing) return;
                if (Native.GetAsyncKeyState(0x7b) < 0 || Native.GetAsyncKeyState(0x1b) < 0) { ShowWindow(); Notify("已取消录制。", false); return; }
            }
            if (IsDisposed || closing) return;
            try { macros.Record(name, target); tray.ShowBalloonTip(2500, "正在录制宏", "Esc 或 F12 停止。", ToolTipIcon.Info); }
            catch (Exception e) { ShowWindow(); Notify(e.Message, true); }
        }
        private void BindMacro()
        {
            var macro = SelectedMacro(); if (macro == null) return; int key = Int32.Parse(Theme.Value(macroHotkey));
            if (key != 0 && settings.Macros.Any(x => x != macro && x.HotKey == key)) { Notify("此快捷键已绑定其他宏。", true); return; }
            string oldName = macro.Name, oldTarget = macro.TargetProcess; int oldKey = macro.HotKey;
            try { macro.Name = macroName.Text; macro.TargetProcess = macroTarget.Text.Trim(); macro.HotKey = key; macro.Validate(); }
            catch (Exception e) { macro.Name = oldName; macro.TargetProcess = oldTarget; macro.HotKey = oldKey; Notify(e.Message, true); return; }
            if (Save()) { RegisterMacroKeys(); ReloadMacros(); RebuildBindings(); macroList.SelectedItem = macro; Notify("宏快捷键已保存。", false); }
            else { macro.Name = oldName; macro.TargetProcess = oldTarget; macro.HotKey = oldKey; }
        }
        private void DeleteMacro()
        {
            var macro = SelectedMacro(); if (macro == null || preview) return;
            try
            {
                if(settings.Macros.Any(x=>x!=macro && ReferencesMacro(x.Events,macro.Id)))throw new InvalidOperationException("其他宏调用了此宏，请先在编辑器移除调用。");
                Store.WriteAtomic(Path.Combine(Store.DirectoryPath, "deleted-macros", macro.Id + ".json"), Store.Serializer().Serialize(macro));
                int index = settings.Macros.IndexOf(macro); settings.Macros.Remove(macro);var oldBindings=settings.MacroBindings;settings.MacroBindings=oldBindings.Where(x=>!String.Equals(x.MacroId,macro.Id,StringComparison.OrdinalIgnoreCase)).ToList();
                if (!Save()) { settings.Macros.Insert(index, macro);settings.MacroBindings=oldBindings; return; }
                RegisterMacroKeys(); ReloadMacros();RebuildBindings(); Notify("已删除宏及其绑定，宏备份保留在 deleted-macros。", false);
            }
            catch (Exception e) { Notify(e.Message, true); }
        }
        private static bool ReferencesMacro(IEnumerable<MacroEvent> events,string id){return events.Any(x=>x.Kind=="Macro" && String.Equals(x.MacroId,id,StringComparison.OrdinalIgnoreCase) || x.Children!=null && ReferencesMacro(x.Children,id));}
        private void Restore() { controller.RestoreBaseline().ContinueWith(delegate { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { paused.Checked = settings.Paused; updating = true; startup.Checked = Startup.Enabled(); updating = false; }); }); }
        private static void Open(string path) { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception e) { MessageBox.Show(e.Message, "Razer Helper"); } }
        private void ShowServices()
        {
            string text = Store.Serializer().Serialize(SynapseStatus.Read());
            using (var dialog = new Form { Text = "雷蛇服务 (只读)", Size = new Size(620, 400), StartPosition = FormStartPosition.CenterParent }) { dialog.Controls.Add(new TextBox { Text = text.Replace("},{", "},\r\n{"), Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, BackColor = Theme.Background, ForeColor = Theme.Text, Font = Font }); dialog.ShowDialog(this); }
        }
        public void Render(string directory)
        {
            Directory.CreateDirectory(directory); Opacity = 0; ShowInTaskbar = false; Show(); Application.DoEvents(); status.Text = "离线界面预览";
            SetStatus(new PowerInfo { OnAC = true, BatteryPercent = 80, Mode = WindowsPower.Balanced.ToString() }, new List<DisplayInfo> { new DisplayInfo { Internal = true, Name = @"\\.\DISPLAY1", Width = 2880, Height = 1800, Hz = 120, Rates = new[] { 60, 120 } } }, new HardwareState { Mode = 2, FanMode = 0, CpuLevel = 2, GpuLevel = 0, Brightness = 128, Fan1Rpm = 2400, Fan2Rpm = 2300, ChargeLimit = 80, Effect = 3 }, null, 0, 0);
            for (int i = 0; i < pages.Count; i++) { ShowPage(i); Application.DoEvents(); using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(directory, "ui-" + i + ".png")); } var layout = new List<object>(); Describe(pages[i], "page", layout); Store.WriteAtomic(Path.Combine(directory, "layout-" + i + ".json"), Store.Serializer().Serialize(layout)); }
            closing = true; Close();
        }
        private static void Describe(Control control, string path, List<object> output)
        {
            var table = control as TableLayoutPanel;
            output.Add(new { Path = path, Type = control.GetType().Name, Bounds = control.Bounds.ToString(), Preferred = control.GetPreferredSize(new Size(control.Width, 0)).ToString(), control.AutoSize, FontSize = control.Font.Size, Rows = table == null ? "" : String.Join(",", table.GetRowHeights()), Text = control is Label ? control.Text : "" });
            for (int i = 0; i < control.Controls.Count; i++) Describe(control.Controls[i], path + "/" + i, output);
        }
    }
    internal static class Brand
    {
        public static Icon Icon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Theme.Green)) g.FillPolygon(brush, new[] { new Point(18, 2), new Point(7, 18), new Point(15, 18), new Point(12, 30), new Point(26, 12), new Point(18, 12) });
                IntPtr icon = bitmap.GetHicon(); try { return (Icon)System.Drawing.Icon.FromHandle(icon).Clone(); } finally { DestroyIcon(icon); }
            }
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    }
}
