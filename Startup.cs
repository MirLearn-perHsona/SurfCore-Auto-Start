using Microsoft.Win32;

namespace SurfCoreAutoStart;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SurfCore Auto-start";
    private const string HighTaskName = "SurfCoreAutoStart-App";

    private static bool HighTaskExists()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/Query /TN \"" + HighTaskName + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p == null) return false;
            p.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>用当前用户身份触发高权限登录任务；任务 DACL 已授予当前用户 FULL ACCESS。</summary>
    public static bool RunHighTask()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/Run /TN \"" + HighTaskName + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void RemoveRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
        }
        catch { }
    }

    public static string ExePath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("SURFCORE_AUTOSTART_EXE");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

            var process = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(process) &&
                Path.GetFileName(process).StartsWith("SurfCore Auto-start", StringComparison.OrdinalIgnoreCase))
                return process;

            var candidate = Path.Combine(AppContext.BaseDirectory, "SurfCore Auto-start.exe");
            if (File.Exists(candidate)) return candidate;
            return process ?? Application.ExecutablePath;
        }
    }

    public static bool IsEnabled()
    {
        if (HighTaskExists()) return true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var value = key?.GetValue(ValueName) as string;
            return string.Equals(value?.Trim('"'), ExePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool Ensure()
    {
        // 已配置“最高权限登录任务”时，不再写 HKCU\Run，否则会启动两次。
        if (HighTaskExists())
        {
            RemoveRunValue();
            return true;
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            key?.SetValue(ValueName, "\"" + ExePath + "\"");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("写入开机自启动失败: " + ex.Message);
            // 某些受限进程会拒绝写 HKCU\Run，但值可能已经正确存在；
            // 这时不要再向用户显示失败提示。
            return IsEnabled();
        }
    }

    public static void Remove()
    {
        RemoveRunValue();
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/Delete /TN \"" + HighTaskName + "\" /F",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            p?.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            Log.Warn("移除开机自启动任务失败: " + ex.Message);
        }
    }
}
