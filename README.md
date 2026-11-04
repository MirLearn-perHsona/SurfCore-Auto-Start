# SurfCore Auto-start

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Build](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/actions/workflows/build.yml/badge.svg)](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/MirLearn-perHsona/SurfCore-Auto-Start?display_name=tag)](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/releases)

[English](README.en.md) | 简体中文

**SurfCore Auto-start** 是一个 Windows 定时开关机小工具，用于让 DeepSeek Harness 的 **SurfCore** 插件更自动化：开机后按顺序拉起 DSH、SurfCore、SnowLuma，并自动登录控制台、注入指定 QQ 进程。

它的**定时开关机功能可以完全单独使用**，不依赖 DeepSeek Harness 或 SurfCore。

![主界面](docs/screenshot-main.png)

## 功能

- **每周定时开关机**：周一至周日分别设置；星期、小时、分钟都在同一块区域内用滚轮上下选择、左键确定。
- **快捷设置**：一键把 7 天的开机时间或关机时间设为一致；开机、关机总开关相互独立。
- **关机保护**：关机前 1 分钟倒计时，可取消；最近 3 分钟有真实键鼠操作时自动跳过本次关机。
- **SurfCore 自启动**：打开开关并填写 SnowLuma 控制台令牌与 QQ 号后，按 DSH → SurfCore bridge → SnowLuma → OneBot → QQ 注入的顺序自动启动。
- **小窗界面**：600×540 浅色无边框窗口，所有开关和选择器均为自绘，简单直接。

## 使用

1. 从 [Releases](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/releases) 下载 `SurfCore.Auto-start.exe`。
2. 建议放到 `C:\SurfCoreAutoStart\`，用桌面 **SurfCore Auto-start** 快捷方式启动（快捷方式会调用高权限登录任务）。
3. 打开总开关，设置星期与开关机时间；修改后会自动同步 Windows 计划任务。
4. 需要 SurfCore 自启动时，打开对应开关，填写 **SnowLuma 控制台令牌** 和 **QQ 号**，并提前让 QQ 登录、开启 QQ 的“开机自启”和“自动登录”。

> 不要直接从聊天工具、网页或 H 盘双击 exe，否则可能没有管理员权限，无法写入计划任务。请使用桌面快捷方式。

## 定时开机限制

Windows 计划任务只能把电脑从**睡眠 / 休眠**唤醒，不能把已经完全关机的主板重新上电。若要在完全关机后定时开机，需要在 BIOS/UEFI 中设置 RTC Wake / Resume by Alarm。

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
