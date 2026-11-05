# SurfCore Auto-start

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Build](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/actions/workflows/build.yml/badge.svg)](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/MirLearn-perHsona/SurfCore-Auto-Start?display_name=tag)](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/releases)

[English](README.en.md) | 简体中文

**SurfCore Auto-start** 是一个 Windows 定时开关机小工具，用于让 DeepSeek Harness 的 **SurfCore** 插件更自动化：开机后按顺序拉起 DSH、SurfCore、SnowLuma，并自动登录控制台、注入指定 QQ 进程。

它的**定时开关机功能可以完全单独使用**，不依赖 DeepSeek Harness 或 SurfCore。

![主界面](docs/screenshot-main.png)

## 功能

- **四套每周计划**：重启、休眠、唤醒、关机各自独立，可分别设置周一至周日的星期、小时、分钟；全部在同一块区域内用滚轮上下选择、左键确定。
- **快捷设置**：一键把 7 天的重启 / 休眠 / 唤醒 / 关机时间分别设为一致。
- **唤醒联动**：必须先开启“休眠”，才能开启“唤醒”；关闭休眠时唤醒自动关闭并置灰，避免出现“唤不醒”的空计划。
- **关机保护**：关机前 1 分钟倒计时，可取消；最近 3 分钟有真实键鼠操作时自动跳过本次关机。
- **SurfCore 自启动**：打开开关并填写 SnowLuma 控制台令牌与 QQ 号后，按 DSH → SurfCore bridge → SnowLuma → OneBot → QQ 注入的顺序自动启动。总开关关闭时该功能不生效并置灰。
- **小窗界面**：600×540 浅色无边框窗口，顶部为总开关、重启长条、休眠/唤醒与关机，下方为四标签时间设置。

## 使用

1. 从 [Releases](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/releases) 下载 `SurfCore.Auto-start.exe`。
2. 建议放到 `C:\SurfCoreAutoStart\`，用桌面 **SurfCore Auto-start** 快捷方式启动（快捷方式会调用高权限登录任务）。
3. 打开总开关；按需打开重启 / 休眠 / 唤醒 / 关机开关，再在对应标签设置时间；修改后会自动同步 Windows 计划任务。
4. 需要 SurfCore 自启动时，打开对应开关，填写 **SnowLuma 控制台令牌** 和 **QQ 号**，并提前让 QQ 登录、开启 QQ 的“开机自启”和“自动登录”。

> 不要直接从聊天工具、网页或 H 盘双击 exe，否则可能没有管理员权限，无法写入计划任务。请使用桌面快捷方式。

## 休眠与唤醒限制

Windows 计划任务只能把电脑从**睡眠 / 休眠**唤醒，不能把已经完全关机的主板重新上电。

- 需要“定时关机后还能自动开机”时，请使用 **休眠 + 唤醒**，不要使用“关机”计划。
- “唤醒”依赖“休眠”开启；休眠关闭时，唤醒按钮会置灰。
- 如果必须完全关机后定时上电，需要在 BIOS/UEFI 中设置 RTC Wake / Resume by Alarm。
- 定时重启会清空内存负荷，但重启后电脑是开机状态，不会停留在关机状态。

可用以下命令检查唤醒定时器：

```powershell
powercfg /waketimers
```

## 构建

需要 Windows 10/11 x64 和 .NET SDK 6.0+。

```powershell
dotnet build SurfCoreAutoStart.csproj -c Release
dotnet publish SurfCoreAutoStart.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## AI 生成声明

> **本项目的代码与文档全权由 deepseek-v4.1-Flash 生成，请注意分辨。**
> 可能存在错误、遗漏或不合理的设计，请自行审查、测试后再使用。

## Contributors

- **MirLearn-perHsona**（作者）

## License

[MIT License](LICENSE)，Copyright (c) 2026 YG。
