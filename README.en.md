# SurfCore Auto-start

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Build](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/actions/workflows/build.yml/badge.svg)](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/MirLearn-perHsona/SurfCore-Auto-Start?display_name=tag)](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/releases)

English | [简体中文](README.md)

**SurfCore Auto-start** is a Windows scheduling utility that makes the **SurfCore** plugin for DeepSeek Harness more autonomous: after boot it starts DSH, SurfCore and SnowLuma in order, signs in to the SnowLuma console automatically, and injects the selected QQ process.

Its **scheduled power on/off feature works completely standalone** — it does not require DeepSeek Harness or SurfCore.

![Main window](docs/screenshot-main.png)

## Features

- **Four independent weekly schedules**: restart, hibernate, wake and shutdown; each has its own weekday/hour/minute settings selected with the mouse wheel and confirmed with a left click.
- **Quick setup**: apply the same time to all 7 days for restart, hibernate, wake or shutdown independently.
- **Wake depends on hibernate**: the wake toggle can only be enabled while hibernate is on; turning hibernate off clears and greys out wake, so no wake task can exist without a preceding hibernate.
- **Shutdown protection**: a cancellable 1-minute countdown before shutdown; shutdown is skipped when real keyboard/mouse activity occurred in the last 3 minutes. The same options apply to scheduled hibernate as well.
- **SurfCore auto-start**: with the toggle enabled and the SnowLuma console token and QQ number filled in, start DSH → SurfCore bridge → SnowLuma → OneBot → QQ injection in the correct order. It is disabled and greyed out while the master switch is off.
- **Compact UI**: a 600×540 light, borderless window with a master switch, a restart strip, a combined hibernate/wake card and a shutdown card, plus four time-setting tabs.

## Usage

1. Download `SurfCore.Auto-start.exe` from [Releases](https://github.com/MirLearn-perHsona/SurfCore-Auto-Start/releases).
2. Recommended: place it in `C:\SurfCoreAutoStart\` and launch it from the desktop **SurfCore Auto-start** shortcut (the shortcut runs an elevated logon task).
3. Turn on the master switch; enable restart / hibernate / wake / shutdown as needed, then set times on the matching tab. Changes are written to Windows Task Scheduler automatically.
4. For SurfCore auto-start, enable its toggle, enter the **SnowLuma console token** and **QQ number**, and make sure QQ is already signed in with “Start with Windows” and “Auto sign-in” enabled.

> Do not double-click the exe directly from a chat app, a web page, or the H: drive — it may not receive administrator rights and cannot create scheduled tasks. Use the desktop shortcut instead.

## The program cannot power on a fully shut-down PC

> **Important: this program cannot implement "power on after a full shutdown".**  
> Windows Task Scheduler can only wake the PC from **sleep/hibernation**. Once the PC is fully shut down (S5), only the motherboard can power it on.  
> If you need a schedule that powers on after a full shutdown, you must enable **RTC Wake / Resume by Alarm** in **BIOS/UEFI** yourself — **this program cannot do it**.

Windows Task Scheduler can wake the PC from **sleep / hibernation**, but it cannot power on a motherboard that is fully shut down.

- To shut down and later resume automatically, use **hibernate + wake**, not the shutdown schedule.
- **Wake requires hibernate to be enabled**; when hibernate is off, the wake button is greyed out.
- For a full power-on schedule, configure RTC Wake / Resume by Alarm in the BIOS/UEFI.
- A scheduled restart clears memory load, but the PC comes back on immediately instead of staying off.

Check wake timers with:

```powershell
powercfg /waketimers
```

## Build

Requires Windows 10/11 x64 and .NET SDK 6.0+.

```powershell
dotnet build SurfCoreAutoStart.csproj -c Release
dotnet publish SurfCoreAutoStart.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## AI-generated notice

> **All code and documentation in this project were generated entirely by deepseek-v4.1-Flash. Please review and verify them carefully.**
> There may be bugs, omissions or questionable design decisions. Inspect and test the project before relying on it.

## Contributors

- **MirLearn-perHsona** (author)

## License

[MIT License](LICENSE), Copyright (c) 2026 YG.
