using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace SurfCoreAutoStart;

/// <summary>
/// 首次运行/自愈安装：把 exe 部署到固定目录，创建高权限登录任务和桌面快捷方式。
/// 这样用户从下载目录、H 盘或聊天工具直接运行一次后，后续都走同一个高权限入口。
/// </summary>
internal static class Installer
{
    internal const string TaskName = StartupRegistration.HighTaskName;

    internal static string InstallDir => @"C:\SurfCoreAutoStart";
    internal static string InstalledExe => Path.Combine(InstallDir, "SurfCore Auto-start.exe");
    internal static string StartBat => Path.Combine(InstallDir, "start.bat");
    internal static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "SurfCore Auto-start.lnk");

    internal static bool IsInstalledApp
    {
        get
        {
            try
            {
                var current = Environment.ProcessPath;
                return !string.IsNullOrWhiteSpace(current) &&
                       string.Equals(Path.GetFullPath(current), Path.GetFullPath(InstalledExe),
                           StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    /// <summary>任务存在且安装目录里有 exe，才认为可以直接交给高权限任务。</summary>
    internal static bool IsUsable => File.Exists(InstalledExe) && StartupRegistration.HighTaskExists();

    internal static bool RequestElevatedInstall()
    {
        try
        {
            // 直接从 H 盘/下载目录提权时，Windows 会把目标进程压回 Medium，
            // 于是高权限实例仍然写不了 C:\SurfCoreAutoStart。先把程序暂存到
            // 本机临时目录，再对本地副本提权，才能拿到真正的高完整性令牌。
            var exe = StageForElevation();
            if (string.IsNullOrWhiteSpace(exe))
                return false;
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "--install",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Path.GetTempPath()
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // 用户在 UAC 里点了“否”。
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn("请求管理员安装失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>把当前程序（单文件 exe 或 build 输出的 exe+dll）复制到本地临时目录。</summary>
    private static string StageForElevation()
    {
        try
        {
            var sourceDir = AppContext.BaseDirectory;
            var stageDir = Path.Combine(Path.GetTempPath(), "SurfCoreAutoStartSetup");
            Directory.CreateDirectory(stageDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var name = Path.GetFileName(file);
                if (!name.StartsWith("SurfCore Auto-start", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("app.ico", StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(file, Path.Combine(stageDir, name), true);
            }

            var stagedExe = Path.Combine(stageDir, Path.GetFileName(Environment.ProcessPath ?? ""));
            return File.Exists(stagedExe) ? stagedExe : null;
        }
        catch (Exception ex)
        {
            Log.Warn("暂存安装程序失败: " + ex.Message);
            return null;
        }
    }

    /// <summary>高权限进程调用；返回 true 表示已把工作交给安装目录里的新实例。</summary>
    internal static void EnsureHighEnvironment(bool forceTask = false)
    {
        Directory.CreateDirectory(InstallDir);

        if (!IsInstalledApp)
        {
            var source = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                throw new InvalidOperationException("找不到当前程序文件，无法部署。");

            // 只有来源版本不旧于已安装版本时才覆盖，避免误运行旧副本造成降级。
            if (ShouldDeploy(source))
            {
                // 先复制到临时名再替换，避免覆盖到一半留下损坏的 exe。
                var tmp = InstalledExe + ".new";
                File.Copy(source, tmp, true);
                if (File.Exists(InstalledExe))
                {
                    try { File.SetAttributes(InstalledExe, FileAttributes.Normal); } catch { }
                    File.Delete(InstalledExe);
                }
                File.Move(tmp, InstalledExe);
            }

            // build 输出不是单文件，必须把同名 dll/json 一起部署，否则安装目录的 exe 起不来。
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
            {
                var name = Path.GetFileName(file);
                if (!name.StartsWith("SurfCore Auto-start", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("app.ico", StringComparison.OrdinalIgnoreCase))
                    continue;
                var dest = Path.Combine(InstallDir, name);
                if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(dest),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(file, dest, true);
            }
        }

        WriteStartBat();
        if (forceTask || !StartupRegistration.HighTaskExists())
            CreateHighTask();
        TryCreateDesktopShortcut();
    }

    internal static void StartInstalled()
    {
        if (!StartupRegistration.RunHighTask())
            throw new InvalidOperationException("无法启动高权限任务，请检查任务计划程序。");
    }

    private static bool ShouldDeploy(string source)
    {
        try
        {
            if (!File.Exists(InstalledExe)) return true;
            var src = FileVersionInfo.GetVersionInfo(source).FileVersion;
            var dst = FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion;
            if (!Version.TryParse(src, out var s) || !Version.TryParse(dst, out var d))
                return true;
            // 版本号重置（如从 1.0.1 回退到 0.1）也要允许覆盖，否则安装目录会一直停留在旧版。
            return s != d;
        }
        catch
        {
            return true;
        }
    }

    private static void WriteStartBat()
    {
        // 任务计划直接执行 exe；这个 bat 只作为手动排错/旧快捷方式的兼容入口。
        var text = "@echo off\r\n" +
                   "cd /d \"" + InstallDir + "\"\r\n" +
                   "start \"\" \"" + InstalledExe + "\"\r\n";
        File.WriteAllText(StartBat, text, Encoding.Default);
    }

    private static void CreateHighTask()
    {
        var user = WindowsIdentity.GetCurrent().Name;
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        var xml = BuildTaskXml(user, sid, includeSecurity: true);
        var temp = Path.Combine(Path.GetTempPath(),
            "surfcore-task-" + Guid.NewGuid().ToString("N") + ".xml");

        try
        {
            File.WriteAllText(temp, xml, Encoding.Unicode);
            if (!TryCreateTask(temp))
            {
                // 某些系统不接受 XML 里的 SecurityDescriptor，退回普通任务。
                xml = BuildTaskXml(user, sid, includeSecurity: false);
                File.WriteAllText(temp, xml, Encoding.Unicode);
                if (!TryCreateTask(temp))
                    throw new InvalidOperationException("创建开机任务失败，请以管理员身份运行后重试。");
            }

            if (!StartupRegistration.HighTaskExists())
                throw new InvalidOperationException("开机任务创建后未在系统中找到。");
            Log.Info("已创建高权限登录任务 " + TaskName);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static bool TryCreateTask(string xmlPath)
    {
        try
        {
            var output = Run("schtasks.exe",
                "/Create /TN \"" + TaskName + "\" /XML \"" + xmlPath + "\" /F");
            if (output.ExitCode == 0) return true;
            Log.Warn("schtasks 创建任务失败: " + output.Text);
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn("schtasks 创建任务异常: " + ex.Message);
            return false;
        }
    }

    private static string BuildTaskXml(string user, string sid, bool includeSecurity)
    {
        var security = includeSecurity && !string.IsNullOrWhiteSpace(sid)
            ? "<SecurityDescriptor>D:(A;;FA;;;BA)(A;;FA;;;SY)(A;;FA;;;" + sid + ")</SecurityDescriptor>"
            : "";
        var now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        return
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
            "  <RegistrationInfo>\r\n" +
            "    <Date>" + now + "</Date>\r\n" +
            "    <Author>" + Escape(user) + "</Author>\r\n" +
            "    <Description>SurfCore Auto-start 登录启动（最高权限）</Description>\r\n" +
            security +
            "  </RegistrationInfo>\r\n" +
            "  <Triggers>\r\n" +
            "    <LogonTrigger>\r\n" +
            "      <Enabled>true</Enabled>\r\n" +
            "      <UserId>" + Escape(user) + "</UserId>\r\n" +
            "    </LogonTrigger>\r\n" +
            "  </Triggers>\r\n" +
            "  <Principals>\r\n" +
            "    <Principal id=\"Author\">\r\n" +
            "      <UserId>" + Escape(user) + "</UserId>\r\n" +
            "      <LogonType>InteractiveToken</LogonType>\r\n" +
            "      <RunLevel>HighestAvailable</RunLevel>\r\n" +
            "    </Principal>\r\n" +
            "  </Principals>\r\n" +
            "  <Settings>\r\n" +
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
            "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
            "    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
            "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
            "    <IdleSettings>\r\n" +
            "      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n" +
            "      <RestartOnIdle>false</RestartOnIdle>\r\n" +
            "    </IdleSettings>\r\n" +
            "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
            "    <Enabled>true</Enabled>\r\n" +
            "    <Hidden>false</Hidden>\r\n" +
            "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
            "    <WakeToRun>false</WakeToRun>\r\n" +
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
            "    <Priority>7</Priority>\r\n" +
            "  </Settings>\r\n" +
            "  <Actions Context=\"Author\">\r\n" +
            "    <Exec>\r\n" +
            "      <Command>" + Escape(InstalledExe) + "</Command>\r\n" +
            "      <WorkingDirectory>" + Escape(InstallDir) + "</WorkingDirectory>\r\n" +
            "    </Exec>\r\n" +
            "  </Actions>\r\n" +
            "</Task>\r\n";
    }

    private static void TryCreateDesktopShortcut()
    {
        try
        {
            var lnk = DesktopShortcut;
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new InvalidOperationException("系统未注册 WScript.Shell");

            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(lnk);
            shortcut.TargetPath = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
            shortcut.Arguments = "/Run /TN \"" + TaskName + "\"";
            shortcut.WorkingDirectory = InstallDir;
            shortcut.IconLocation = InstalledExe + ",0";
            shortcut.Description = "SurfCore Auto-start";
            shortcut.Save();
            Log.Info("已创建桌面快捷方式: " + lnk);
        }
        catch (Exception ex)
        {
            // 快捷方式失败不应阻止程序运行；下次启动还会再试。
            Log.Warn("创建桌面快捷方式失败: " + ex.Message);
        }
    }

    private static string Escape(string value) =>
        (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    private static (int ExitCode, string Text) Run(string file, string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        });
        if (p == null) return (-1, "无法启动 " + file);
        var output = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        return (p.ExitCode, (output + "\n" + error).Trim());
    }
}
