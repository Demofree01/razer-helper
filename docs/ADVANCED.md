# 进阶能耗能力与手动测试（1.1.1）

进阶功能只做只读实机检查和模拟写入测试，没有在测试机应用新的进阶 CPU / GPU 参数、安装或加载第三方内核驱动、运行雷云 AMD 超频组件。详细使用说明见 [中文](../README.md) / [English](../README.en.md)。

## 本机可行性

| 功能 | 本次实现 | 本机状态 |
|---|---|---|
| CPU / GPU 固件低中高档位 | 已有自定义模式功能 | CPU 高→中→高回读通过；GPU 更高档未实测 |
| CPU 最小 / 最大处理器状态 | Windows 策略百分比，手动应用和恢复 | 可读；写入待人工测试 |
| CPU 睿频策略、EPP | 读取并编辑当前 Windows 模式使用的策略 | 可读；写入待人工测试 |
| CPU 精确 TDP / PPT、温度墙 | 已评估 SMU / RyzenAdj 路径，未提供 setter | Strix Point 有开源支持线索，尚缺本机可验证的读写后端 |
| CPU Curve Optimizer / 降压 | 未调用本机雷云组件 | 不探测未知 SMU 命令，不加载 AMD 超频 DLL |
| GPU 功耗 / 温度 / 利用率 / 频率 | 手动只读查询现有 NVIDIA 工具 | 本机可读，不后台轮询独显 |
| GPU 功率上限 W | 仅当当前上限和边界均可回读才允许手动设置 | 本机当前上限 N/A，按钮禁用 |
| GPU 核心 / 显存偏移、电压 / 频率曲线 | 未接入 NVAPI 后端 | 本次没有调节滑块或伪装成已支持 |

G-Helper 的硬件功能依赖 ASUS ACPI WMI、型号相关的固件接口及 AMD / NVIDIA 后端。它的思路可以参考，ASUS 命令不能直接用于 Razer。本机 CPU 属于 Strix Point；RyzenAdj 源码包含该族的功率设置分支，但初始化涉及驱动、内存和 SMU 通信，仅凭 CPU 名称不能确认本机固件允许读写，更不能保证失败时精确恢复。此次保留为研究路径。

本机只读 NVIDIA 输出：默认上限 45 W、最小 5 W、最大 125 W、**当前上限 N/A**。这些数字不足以证明 5–125 W 可调。没有可靠当前值就不能建立回退快照，因此该机器不开放瓦数设置。

## Windows 模式覆盖策略

2026-10-06 只读时，基础计划为 `381b4222-f694-41f0-9685-ff5bb260df2e`，Windows 模式为“最佳性能”。基础计划接电值为最小 5%、最大 100%、睿频 2、EPP 20；电池 EPP 90。

“最佳性能”覆盖策略 `ded574b5-45a0-4f42-8737-46345c09c238` 的接电 / 电池值为最小 80%、最大 100%、睿频 2、EPP 10。Helper 读取和编辑该模式实际使用的策略；处于平衡模式时才编辑基础计划。界面同时显示模式、供电侧和目标策略 GUID，避免只改基础计划却被覆盖。

最小 / 最大状态与 EPP 的单位都是 Windows 百分比，不代表固定 CPU 瓦数或时钟。EPP 0 偏向性能，100 偏向节能。具体功耗、温度、时钟和续航变化取决于系统、负载与固件，未进行压力测试。手动写入后这些是 Windows 保存的策略，即使退出 Helper 也会保留，Windows 会使用对应接电 / 电池值；Helper 不在开机或插拔时重新写进阶参数。

## 操作

1. 打开“性能 / 屏幕 → 进阶能耗调节（手动）”。窗口只读加载 CPU，GPU 需另点“只读查询 GPU”。
2. 选择接电或电池，检查模式及原值，再调整所需项。没有点击“手动应用 CPU 策略”就没有写入。切换 Windows 模式后先刷新。
3. 点击应用时保存完整原值到 `%LOCALAPPDATA%\RazerHelper\advanced\last-cpu-change.json`，检查计划、模式和数值未被其他应用改变，逐项写入，再刷新当前模式并回读。
4. 使用“恢复上次 CPU 原值”恢复同一模式 / 供电侧。恢复也会检查当前状态与上次应用后读数一致；如果你已手动改了计划或其他软件改了数值，会拒绝覆盖，先查看保存的原值再处理。

写入中断时逆序尝试恢复并回读。其他程序已选择的新基础计划或电源模式不会被恢复过程切回旧选择。单项原值不可读、模式未知、最小值高于最大值、超范围或写权限不足时明确报错。回读验证指策略数据一致，不表示测得实际硬件功耗变化。

“恢复初始设置”只恢复旧版功能的首次基线。进阶 CPU 策略使用本页独立恢复按钮；原备份也保留 `.bak`。

## 接口参考

- [G-Helper FAQ](https://github.com/seerge/g-helper/wiki/FAQ)
- [G-Helper PowerNative](https://github.com/seerge/g-helper/blob/main/app/Mode/PowerNative.cs)
- [RyzenAdj API 源码](https://github.com/FlyGoat/RyzenAdj/blob/master/lib/api.c)
- [Microsoft EPP](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-perfenergypreference)
- [Microsoft 最大处理器状态](https://learn.microsoft.com/en-us/windows-hardware/customize/power-settings/options-for-perf-state-engine-maxperformance)
- [Microsoft PowerWriteACValueIndex](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powerwriteacvalueindex)
- [NVIDIA SMI 说明](https://docs.nvidia.com/deploy/nvidia-smi/index.html)
