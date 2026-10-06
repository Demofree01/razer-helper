# Razer Helper

[中文](README.md) | **English**

A lightweight, offline Windows tray utility for Razer Blade keyboard lighting, firmware performance presets, refresh rates, charge limits, and local macros. The interface and tray workflow are inspired by [G-Helper](https://github.com/seerge/g-helper), for users who want fewer Synapse background dependencies.

> **Compatibility: Tested only on the Razer Blade 14 (2025), RZ09-0530. Successful operation is not guaranteed on another unit of the same model, similar models, or different machines. BIOS, firmware, driver, and Windows versions may affect behavior. Advanced parameter writes and native Fn/Hypershift triggering are not hardware-validated and are excluded from the tested feature set.**
>
> **目前仅在雷蛇灵刃 14 2025 款（RZ09-0530）上测试通过。同型号、同类型及其他机型不保证可以运行成功。进阶参数写入与真实 Fn／Hypershift 触发尚未完成实机验证。**

Questions, problems, and suggestions are welcome at any time in [GitHub Issues](https://github.com/Demofree01/razer-helper/issues), in either Chinese or English.

## Download and first run

1. Download `RazerHelper-version-win-x64.zip` from [Releases](https://github.com/Demofree01/razer-helper/releases). GitHub's automatic `Source code` archives contain source, not a ready-to-run portable application.
2. Extract the complete ZIP into a fixed directory and run `RazerHelper.exe`. Keep `RazerHelper.exe.config` next to it. Current version: **1.1.1**, targeting Windows x64 and .NET Framework 4.8. Hardware testing used Windows 11.
3. Exit Synapse normally from its tray menu to avoid competing lighting/performance writes. For long-term replacement, you may disable Synapse in Windows Startup Apps. Helper does not terminate Synapse processes, disable services, uninstall drivers, or edit Synapse settings for you.
4. First run saves the readable original state for restoration. Check the detected model, power source, and internal display on the Performance/Display page.
5. Closing the main window leaves Helper running in the tray. Use tray **退出** (Exit) to quit. **设置 → 登录 Windows 后在托盘启动** (Settings → Start in tray at Windows login) is optional, off by default, and only creates a startup entry for the current user.

Runtime requires no Synapse, account, login, or Internet connection. There is no WebView, telemetry, automatic updater, or bundled third-party kernel driver. **The application UI is currently Chinese; this guide translates its relevant labels.**

**Initial automation:** battery 60 Hz switching is enabled, with the selected rate restored on AC. Other automatic firmware presets, Windows modes, lighting restoration, backlight keep-alive, and macro bindings are off by default. Initial AC refresh selection uses the detected internal rate when above 60 Hz; otherwise it starts at 120 Hz. Select **设置 → 暂停所有自动策略** (Pause all automatic policies) to suspend automation.

## Synapse replacement coverage

| Synapse feature / need | Helper 1.1.1 coverage | Status and limits |
|---|---|---|
| Keyboard brightness/basic effects | Static RGB, single-color breathing, spectrum, wave both ways, off; brightness 0–100% | Implemented; firmware-dependent colors; no complex Chroma or per-key editor |
| Idle lighting timeout | Optional backlight keep-alive with display on and session unlocked | Confirmed on the test machine; off by default |
| AC performance presets | Balanced, Silent, Performance, Custom | Implemented using firmware presets and automatic fans |
| Battery presets | Balanced, Battery Saver; separate AC/battery selections | Implemented; AC-only modes remain unavailable on battery |
| Custom CPU/GPU levels | Low/Medium/High for each | Partial; levels are not exact watts; not every combination was tested |
| Power-source automation | Blade presets, Windows modes, and internal refresh rate | Implemented with independent switches |
| Battery 60 Hz | 60 Hz on battery, selected internal rate on AC | Implemented; manual changes have 15-second confirmation and separate rollback |
| Charge protection | Existing firmware 80% limit and full charging | Implemented; no arbitrary percentage threshold |
| CPU energy policies | Minimum/maximum state, boost, EPP; separate AC/battery values | Manual apply/restore implemented; hardware reads checked, writes await manual testing |
| NVIDIA information | On-demand power, temperature, utilization, core/memory clocks | Reading implemented; no continuous background dGPU polling |
| GPU power limit in watts | Manual setting only with readable current limit and bounds | Test laptop reports current limit N/A, so disabled; no verified TGP control here |
| Software macros | Local editing, recording, playback, keyboard/mouse, text, loops, and more | Partial; manual migration/binding; target application compatibility needs testing |
| Synapse macro migration | Read-only scan, source/action preview, selected import; XML/JSON | Recognized formats only; not all Synapse versions supported |
| Macro hotkeys | Ctrl+Alt+letters/digits/brackets; legacy Ctrl+Alt+F6–F11 | Implemented; shortcuts must not conflict with other applications |
| Fn/Hypershift macros | Signal observation and binding drafts, gated on reliable calibration | **Incomplete; a reliable independent Fn signal is not confirmed on the test laptop. Not a working Hypershift replacement** |
| Fixed fan speed/manual curves | Automatic fans and RPM display | Fixed speeds/curves not implemented |
| Exact CPU TDP/PPT, thermal limits, undervolting | No validated device-specific backend | Not implemented; no Synapse AMD overclocking component, BIOS, or SMU writes |
| GPU overclocking/undervolting/curves/MUX | Not provided | Not implemented |
| Chroma Studio/game integrations/cloud sync/AI templates | Not provided | Not implemented; Helper is local |

## Performance, display, and energy policies

### Blade firmware presets

Open **性能／屏幕** (Performance/Display), select AC/battery presets separately, then click **应用接电预设** (Apply AC preset) or **应用电池预设** (Apply battery preset) for the current source. Selecting a preset alone does not immediately apply a firmware mode.

AC offers Balanced, Silent, Performance, Custom; battery offers Balanced and Battery Saver. Enable **随插拔电源应用以上 Blade 预设** (Apply these Blade presets when power changes) to apply saved presets at startup, resume, and power-source changes. Verify manually before enabling automation.

For Custom, select **CPU：低／中／高** and **GPU：低／中／高** (Low/Medium/High), then **应用自定义（接电）** (Apply Custom on AC). Helper initially adopts existing readable firmware levels. These records take effect only in Custom mode. Firmware allocates power: **a level does not mean a fixed 15 W or 30 W**. Fans remain automatic. If Synapse left manual fans active, restore Automatic there first.

Windows Best Efficiency/Balanced/Best Performance is separate. A Blade preset does not imply Windows Best Performance. Its independent automation is under **设置 → Windows 电源自动策略** (Windows power automation).

Writes are rejected when a restorable state is unreadable, a mode is unknown, manual fans are active, or inconsistent readings persist. Failure attempts restoration. Power disconnection mid-change stops AC-only writes and falls back to Balanced, reporting incomplete restoration of the original AC state. Do not bypass model checks or send unrecognized commands.

### Refresh rate

The internal-display controls list driver-provided rates at the current resolution. Click **60 Hz**, or select a rate and **切换并确认** (Switch and confirm). Confirm within 15 seconds if the image is correct; otherwise an independent process attempts restoration.

**拔电自动 60 Hz；接电恢复所选刷新率** (Battery 60 Hz; restore selected rate on AC) controls only the built-in panel. Cloned topology, disconnected panels, unknown power, and driver rejection cause a skipped change; external panels are not controlled. The test panel exposed 48, 60, 75, 100, 120 Hz. Other systems depend on actual enumeration.

### Advanced energy controls: read first, apply manually

1. Open **进阶能耗调节（手动）** (Advanced energy controls, manual). Choose **接电参数** (AC) or **电池参数** (Battery). Opening the window and **只读刷新** (Read-only refresh) do not write.
2. Review the Windows mode/policy, then edit CPU minimum/maximum state, boost, EPP. Minimum must not exceed maximum. EPP 0 favors performance; 100 favors efficiency. These are **Windows policies, not watts or voltages**.
3. Only **手动应用 CPU 策略** (Manually apply CPU policy) writes. Helper saves original values, checks for changes, applies, and reads back. Windows policies persist after exit; Helper does not rewrite advanced values at startup or AC changes.
4. Undo with **恢复上次 CPU 原值** (Restore previous CPU values). Restoration refuses to overwrite values/plans changed elsewhere; review the backup. Main Restore initial settings does not restore advanced CPU policies.
5. **只读查询 GPU** (Read-only GPU query) reads NVIDIA information. Missing current limit, incomplete bounds, or unavailable existing NVIDIA tooling disable setting. Readable bounds alone are insufficient: a reliable current value is required for rollback.

Advanced writes and stress tests were not performed on the test laptop. Write paths were simulated for later manual evaluation. [Advanced capabilities](docs/ADVANCED.md) are documented in Chinese.

## Keyboard lighting

1. Open **键盘灯效** (Keyboard lighting), select effect/color/direction, then **应用灯效** (Apply effect). Use **仅应用亮度** (Apply brightness only) for brightness alone.
2. Optionally enable **启动／唤醒时应用已保存的灯效与亮度** (Restore lighting at startup/resume) or **电池时降低亮度** (Dim on battery).
3. If lighting fades while idle and wakes with a key, enable **屏幕亮着时保持背光（避免闲置渐暗）** (Keep backlight while screen on). A read-only query about every three seconds keeps it active without overwriting brightness. Fn brightness/off still works. Keep-alive stops on lock, display off/dim, sleep, or paused policies.
4. If normal Fn functions need restoration after leaving Synapse, use **设置 → 启用原生 Fn** (Enable native Fn). This restores a firmware mode; **it does not enable Fn macro triggering**.

Native static RGB uses volatile color frames, not custom frames saved to flash. Idle timeout/sleep may discard them, requiring reapplication. Keep-alive does not overwrite effects. The displayed effect record is a stored preset and can differ from volatile color. Complex Synapse Chroma configurations cannot be fully restored.

## Macro editing, migration, and shortcuts

### Create or record

On **宏** (Macros), choose **新建宏** (New) or **编辑所选宏** (Edit selected). Set name, optional target EXE, runtime limit, and actions. Supported: delay, separate keyboard down/up, five mouse buttons, wheel, relative/absolute movement, Unicode text, clipboard text, nested macro, loop, launch, and command. Reorder as needed.

**3 秒后开始录制** (Record after three seconds) captures keyboard, mouse buttons, wheel only while explicitly recording, for at most 60 seconds. Esc/F12 stops recording. Regular recording does not capture movement continuously; import/edit movement instead. Macro content and bindings are saved separately.

Launch/command requires an absolute EXE path and arguments; no shell command is generated. External and manual clipboard actions require explicit per-macro permission and default off. Calling/called macros must both permit the relevant actions. Text/arguments are hidden by default in previews but their real contents are saved in settings and exports.

### Migrate: scan → preview → manually import

1. Click **只读扫描雷云宏** (Read-only Synapse scan). Helper reads local caches/logs without modifying, repairing, or locking the Synapse database.
2. Check names, source timestamps, action counts, and steps. Logs may be historical snapshots, **not the latest version visible in Synapse**. Reveal text/arguments only when needed.
3. Select candidates and import. Existing IDs are not overwritten; empty macros, unknown actions, and unsupported formats are rejected. Import does not play, enable shortcuts, or authorize external actions.
4. Review delays, key codes, mouse coordinates, target EXE, then bind manually.

**导入 XML／JSON** (Import XML/JSON) supports Helper JSON, recognized Synapse 4 `appEngine.events` JSON, and common keyboard/mouse XML; not every historical format. Version 1.1.1 converts recognized Synapse text events to direct Unicode input, fixing earlier clipboard-only behavior. Manually added **剪贴板** (Clipboard) actions still only write the clipboard: **they do not paste automatically**. Add paste keys/delay if needed. Irrecoverable non-text original clipboard content causes rejection.

### Test before enabling a shortcut

1. Prepare a plain text input field, select a macro, click **3 秒后播放一次** (Play after three seconds), and focus the target before countdown ends.
2. Keep the same foreground target. Focus changes, lock, timeout, input failure, or F12 stops playback. Helper refuses input into its own window.
3. Open **按键绑定 → Hypershift／自定义组合键** (Key bindings → Hypershift/custom shortcut). Select macro/key and **Ctrl+Alt**, choose once/repeat/toggle, check **启用此绑定** (Enable), and **保存并生效** (Save and activate). Release Ctrl, Alt, and the trigger key after triggering so modifiers do not alter injected actions.
4. Legacy Ctrl+Alt+F6–F11 remains under **录制与快捷键** (Recording/shortcuts). Avoid duplicate bindings; conflicts are reported.

Fn is not an ordinary Windows modifier. The test laptop has no confirmed reliable independent Fn signal, so **Fn/Hypershift remains incomplete**. The observation window supports research/manual calibration. Without stable distinct Fn-down/up reports, keep Fn disabled and use Ctrl+Alt. Hold-to-play is only for calibrated Fn, not Ctrl+Alt.

Limits: 1–600 seconds per run, 64 saved macros, 4096 actions per macro, 8 nesting levels, 100000 expanded actions. Stopping releases keys/buttons injected by playback. **宏动作已发送** (Macro actions sent) means the input API accepted actions, not proof the target processed them. Elevated windows, game input, focus, EXE restrictions, and delays can affect behavior. Helper never elevates automatically. [Macro/Fn notes](docs/MACROS.md) are in Chinese.

## Settings, updates, and restoration

Files are under `%LOCALAPPDATA%\RazerHelper`, available through **设置 → 配置与日志** (Configuration and logs):

| File / directory | Contents |
|---|---|
| `settings.json` | Preferences, macros, bindings, first captured baseline |
| `settings.json.bak` | Previous successfully saved configuration |
| `helper.log` | Rotating control/error log; no macro text or command arguments |
| `deleted-macros/` | JSON backups retained on deletion |
| `advanced/last-cpu-change.json` | Original/applied values of the last manual CPU change |

**Macro text is stored in plaintext in settings, backups, and exports. Do not publish password macros, full settings, or unchecked Synapse logs.** Releases exclude development-machine settings, macros, logs, and diagnostics.

To update, quit from tray, back up local settings, extract/run the new bundle. If changing directories, disable login startup in the old program before enabling it in the new one. Same-directory updates usually need no startup change. Exiting does not automatically undo firmware/Windows settings.

**恢复初始设置** (Restore initial settings) pauses automation, then attempts restoration of the first captured Windows mode, internal rate at the same resolution, restorable Blade preset, brightness, simple effect, charge limit, and Helper startup state. Unreadable complex effects/Fn layers require separate restoration; Helper does not claim to reproduce all Synapse configuration. Advanced CPU policies use their own restore button.

To uninstall, disable login startup, exit, then remove the extracted directory. Back up local macros first. Removing the application does not remove user settings or restore device state automatically.

## Troubleshooting and feedback

- **Unrecognized model:** hardware writes are restricted to recognized RZ09-0530, USB `1532:02C5`. Other models are rejected by default; generic Windows features also depend on capabilities.
- **Cannot safely roll back current mode:** normally exit Synapse, restore automatic fans there, then inspect. Version 1.0.2 fixed false rejection after battery-saver state remained on reconnecting AC. Unknown/unreadable states remain rejected.
- **Macro status but no text:** use 1.1.1 direct Text actions; check foreground field, optional target EXE, permissions. Manual Clipboard needs separate paste keys. Do not test into Helper itself.
- **Fn not recognized:** known limitation; use Ctrl+Alt. Enable native Fn does not mean Hypershift support.
- **Settings/effects overwritten:** check Synapse, Chroma, other hardware utilities, or conflicting automation.
- **Refresh/GPU watts unavailable:** availability follows driver capability and reliable readback; hidden options are not forced on.

Feel free to [leave an issue at any time](https://github.com/Demofree01/razer-helper/issues), in Chinese or English. Include model/RZ ID, BIOS, Windows/Helper versions, AC/battery state, reproduction steps, expected/actual behavior, and redacted error text. Do not include passwords, full macros, or account details. Model requests are welcome, but operation on every machine cannot be promised.

## Validation and building

Hardware evidence comes from one Blade 14 (2025): BIOS 1.06, keyboard firmware 1.4, Windows 11, Ryzen AI 9 365/RTX 5060 Laptop. Basic modes, brightness, simple-effect commands, 80%/full charge, Windows modes, 60↔120 Hz, and display rollback have readback evidence. Idle keep-alive and Exit Synapse → unplug → reconnect → apply performance were user-confirmed. Colors were not instrument-measured; every CPU/GPU combination, sleep/resume, and target application were not individually tested.

Version 1.1.1 passes **120 offline tests**. Macro input, external actions, advanced writes, and failure rollback use simulations. Unit tests do not establish hardware/application compatibility. [Validation](docs/VALIDATION.md), [protocol](docs/PROTOCOL.md), and [performance](docs/PERFORMANCE.md) notes are in Chinese.

Build in Windows PowerShell without NuGet, an extra SDK, or third-party drivers:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Tests
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

Uses Windows' built-in `Framework64/v4.0.30319/csc.exe`, C# 5/.NET Framework 4.8 x64. Outputs: `dist/`; portable bundles: `release/`. Normal tests do not change hardware or play real macros.

The Release workflow builds, tests, and packages on a GitHub Windows runner, publishing the versioned ZIP and SHA256. It runs on version/release-note/workflow changes on main, or manual dispatch, never automatically on users' computers. Existing complete releases are not overwritten. Add notes at `docs/releases/version.md`. The application does not automatically check for updates.

Read-only developer diagnostics:

```powershell
.\dist\RazerHelper.Cli.exe --diagnose .\artifacts\diagnostics.json
.\dist\RazerHelper.Cli.exe --advanced-status
.\dist\RazerHelper.Cli.exe --scan-synapse
```

Inspect device paths/details before sharing diagnostics. Developer switches `--exercise-safe`, `--exercise-lighting-charge`, `--exercise-performance`, `--arm-display-watchdog` temporarily write settings; they are not read-only diagnostics or normal usage. `--no-startup-apply` only skips that startup application, leaving saved policies and subsequent power events active.

## License and references

[MIT License](LICENSE). Independently implemented; not an official Razer product or affiliated with Razer. Protocol/interface facts reference [OpenRazer](https://github.com/openrazer/openrazer), [razer-ctl](https://github.com/blauzim/razer-ctl), [R-Helper](https://github.com/Fatalution/r-helper), and [G-Helper](https://github.com/seerge/g-helper). Their programs and Synapse components are not bundled, called, or modified. See [protocol notes](docs/PROTOCOL.md) for sources.
