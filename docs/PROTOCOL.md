# 接口与实现记录

本机只读发现：型号 `Blade 14 - RZ09-0530`，VID `1532`，PID `02C5`。通过 SetupAPI 和 HID caps 枚举，不发送探测用的全零写报文，不猜测其他型号的接口。

物理 `MI_02` HID 集合提供 91 字节 feature report，Usage `01:02`。键盘输入集合的 feature 长度为 0，`MI_03` 是另一种 51 字节接口，均不用于控制。鼠标集合可能拒绝读写权限打开，因此回退到 `CreateFile` 的零数据访问权限，仍使用 feature IOCTL；保持共享读写，不接管键盘输入。

USB 报文是零 report ID 加 90 字节 Razer payload。CRC 为 payload 字节 2–87 的 XOR。响应检查 report ID、事务、命令、数据长度、CRC、成功状态及余包字段。`0792` 的余包字段有已记录的特殊情况。Windows `BytesReturned` 可不计零 report ID，允许 90 字节计数，但仍校验整个 91 字节缓冲区。

HID 交换使用重叠 IOCTL 和取消；每个请求串行化，并用本用户会话的命名 mutex 避免 Helper 实例互相打断。发出一次写请求后只有限次读取响应；被覆盖的只读查询最多重发 3 次。写请求不会因为响应被覆盖而盲目重发。这个 mutex 无法约束雷云，因而仍需处理它的竞争。

| 命令 | 用途 | 允许范围 |
| --- | --- | --- |
| 0081 / 0084 | 固件 / 设备模式读取 | 只读 |
| 0E84 / 0E04 | Blade 键盘亮度读取 / 设置 | 0–255，回读验证 |
| 0383 | 背光状态查询 / 闲置保活 | `01 05 00`，只读，不改亮度或设备模式 |
| 0D82 / 0D02 | 两组性能 / 风扇模式 | 平衡 0、性能 2、自定义 4、安静 5、省电 6；风扇字段只允许自动 0 |
| 0D87 / 0D07 | 自定义 CPU / GPU 档位记录读取 / 设置 | 用户只能选择 0–2（低 / 中 / 高）；CPU 3 仅用于恢复已有增强记录；禁止实验降压 4 |
| 0D88 | 实际风扇转速读取 | 两组，显示读数，不写风扇 |
| 0F82 | 固件灯效预设记录读取 | VARSTORE 1 / backlight 5 |
| 030A | 标准灯效 | 关闭、常亮、单色呼吸、光谱、两个波浪方向及 NOSTORE 自定义帧 |
| 030B | 原生常亮颜色帧 | 本型号 6×16，行 0–5、列 0–15、固定 padding，无保存帧选项 |
| 0792 / 0712 | 充电上限 | 80% D0 / 取消上限 50，回读验证 |
| 0004 | 恢复原生 Fn 模式 | 仅 00 00；禁止写 03 驱动模式 |

标准常亮在已有驱动模式中使用 `030A [06,R,G,B]`；原生模式使用 `030B` 六行颜色和 `030A [05,00]` 激活易失帧，保护 Fn 层并避免保存帧。VARSTORE getter 不能证明 NOSTORE 帧的实际显示色，所以原生帧成功指令的依据是传输及固件响应，不能宣称颜色回读成功。

Windows 刷新率通过 `QueryDisplayConfig` 识别 LVDS / eDP / 内嵌连接，不按“主显示器”猜测内屏。如果同一源映射内外屏，拒绝自动修改。只提供当前分辨率、色深、方向和标志匹配的 `EnumDisplaySettings` 列表；先 `CDS_TEST`，再动态应用，未写显示模式注册表、未启用不安全模式。手动切换先启动独立保护进程并等待就绪，进程仅在屏幕名称、尺寸和新刷新率仍匹配时回退。

Windows 电源使用现有 power overlay。`PowerGetActualOverlayScheme` 参数是 `GUID*`（C# `out Guid`），而 `PowerGetActiveScheme` 是分配的 `GUID**`（C# `out IntPtr` 后 LocalFree）；两者不能混用。没有创建或删除电源计划，也没有逐项改 CPU / GPU 电压、功率或散热设置。

背光保持由 Windows 当前会话的 `GUID_SESSION_DISPLAY_STATUS` 通知和 WTS 会话通知控制。仅在显示状态明确为 1、会话未锁定、未睡眠、未暂停且选项开启时，以三秒间隔调用只读 `0383`。执行前再次检查活动桌面；屏幕未知、关闭、变暗、锁屏、断开会话或睡眠时停止。控制队列忙时跳过，不累计请求；失败后间隔 30 秒重试。没有 `SendInput`、电源请求、阻止睡眠或驱动模式写入。窗口自动状态刷新只在近期有用户活动时读取硬件，避免未启用保持时定期唤亮背光。

宏采用临时低级输入钩子和标准 Windows `SendInput`。播放时持续检查前台窗口和停止键；停止及失败后释放自己模拟按下的键与鼠标。不会自动提权，因此对更高权限目标的输入失败会报错。XML 禁用 DTD、外部解析器，有文件、事件、时间和数据范围上限；不加载可执行脚本。

主要来源：

- [OpenRazer 的 02C5 设备能力及 6×16 矩阵](https://github.com/openrazer/openrazer/blob/master/daemon/openrazer_daemon/hardware/keyboards.py)。
- [OpenRazer 键盘型号对应命令](https://github.com/openrazer/openrazer/blob/master/driver/razerkbd_driver.c)及[协议构造](https://github.com/openrazer/openrazer/blob/master/driver/razerchromacommon.c)。
- [razer-ctl 的性能、亮度、充电协议](https://github.com/blauzim/razer-ctl/blob/main/librazer/src/command.rs)及[枚举值](https://github.com/blauzim/razer-ctl/blob/main/librazer/src/types.rs)。
- [G-Helper 的 Windows power overlay 声明](https://github.com/seerge/g-helper/blob/main/app/Mode/PowerNative.cs)。
- [Microsoft 显示切换 API](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-changedisplaysettingsexw)及[活动显示拓扑查询](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig)。
- [razer-ctl 的原生 Fn 模式闲置渐暗与只读保活说明](https://github.com/sqmagellan/razer-ctl#quirks)。其实机记录来自 Blade 16，背光查询已另在本机 PID 02C5 验证。
- [Microsoft 屏幕状态 GUID](https://learn.microsoft.com/en-us/windows/win32/power/power-setting-guids)及[会话状态通知](https://learn.microsoft.com/en-us/windows/win32/termserv/wm-wtssession-change)。

这些是协议事实和接口声明的参考，程序代码为本地独立 C# 实现。没有提供新启用 CPU Boost / Hyperboost / Undervolt、手动低转速、任意命令透传或结束 dGPU 进程的功能。CPU Boost 3 的写入仅是失败时恢复已读取的原记录；不会供用户新选用。AMD Curve Optimizer 是独立接口，未调用任何雷云 DLL 或相关驱动。

性能事务先取得稳定供电、两区模式和自定义档位快照；瞬时两区差异有限次重读，持续差异不写入。写入前复核快照和供电；接电专用模式与档位逐次写入前检查供电，随后回读。单区或 CPU / GPU 写入部分失败时恢复原记录和原两区模式。电源变化后不恢复接电专用模式，退回平衡和自动风扇并报告回退未完整成功。无法完整读取的自定义档位、未知模式或手动风扇状态均拒绝变更。接电时读取到已有省电 6 可以退出，也允许失败后回写该既有快照；界面不会主动在接电时新选择省电预设。
