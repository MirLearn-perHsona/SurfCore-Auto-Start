using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SurfCoreAutoStart;

internal sealed class SchedulerResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool RestartActive { get; set; }
    public bool HibernateActive { get; set; }
    public bool WakeActive { get; set; }
    public bool ShutdownActive { get; set; }
    public int RestartDays { get; set; }
    public int HibernateDays { get; set; }
    public int WakeDays { get; set; }
    public int ShutdownDays { get; set; }
}

internal enum ScheduledAction
{
    Restart,
    Hibernate,
    Wake,
    Shutdown
}

internal static class Scheduler
{
    public const string RestartTaskName = "SurfCoreAutoStart-Restart";
    public const string HibernateTaskName = "SurfCoreAutoStart-Hibernate";
    // 沿用旧任务名，避免升级时残留一个永不删除的旧唤醒任务。
    public const string WakeTaskName = "SurfCoreAutoStart-Boot";
    public const string ShutdownTaskName = "SurfCoreAutoStart-Shutdown";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static string Schtasks => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");

    public static SchedulerResult Apply(AppConfig cfg)
    {
        var result = new SchedulerResult();
        try
        {
            var restartWanted = cfg.MasterEnabled && cfg.RestartEnabled && cfg.Restart.EnabledCount > 0;
            var hibernateWanted = cfg.MasterEnabled && cfg.HibernateEnabled && cfg.Hibernate.EnabledCount > 0;
            var wakeWanted = cfg.MasterEnabled && cfg.HibernateEnabled && cfg.BootEnabled && cfg.Boot.EnabledCount > 0;
            var shutdownWanted = cfg.MasterEnabled && cfg.ShutdownEnabled && cfg.Shutdown.EnabledCount > 0;

            result.RestartDays = cfg.Restart.EnabledCount;
            result.HibernateDays = cfg.Hibernate.EnabledCount;
            result.WakeDays = cfg.Boot.EnabledCount;
            result.ShutdownDays = cfg.Shutdown.EnabledCount;

            var errors = new List<string>();

            if (hibernateWanted || wakeWanted)
                EnsureWakeSupport();

            ApplyOne(ScheduledAction.Restart, restartWanted, cfg.Restart, cfg, errors, active => result.RestartActive = active);
            ApplyOne(ScheduledAction.Hibernate, hibernateWanted, cfg.Hibernate, cfg, errors, active => result.HibernateActive = active);
            ApplyOne(ScheduledAction.Wake, wakeWanted, cfg.Boot, cfg, errors, active => result.WakeActive = active);
            ApplyOne(ScheduledAction.Shutdown, shutdownWanted, cfg.Shutdown, cfg, errors, active => result.ShutdownActive = active);

            result.Success = errors.Count == 0;
            result.Message = errors.Count > 0 ? string.Join("；", errors) : "计划任务已同步";
            if (!result.RestartActive) result.RestartActive = TaskExists(RestartTaskName);
            if (!result.HibernateActive) result.HibernateActive = TaskExists(HibernateTaskName);
            if (!result.WakeActive) result.WakeActive = TaskExists(WakeTaskName);
            if (!result.ShutdownActive) result.ShutdownActive = TaskExists(ShutdownTaskName);
            Log.Info($"Scheduler.Apply master={cfg.MasterEnabled} restart={cfg.RestartEnabled} hibernate={cfg.HibernateEnabled} " +
                     $"wake={cfg.BootEnabled} shutdown={cfg.ShutdownEnabled} active={result.RestartActive}/{result.HibernateActive}/{result.WakeActive}/{result.ShutdownActive} msg={result.Message}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
            Log.Error("Scheduler.Apply 失败", ex);
        }
        return result;
    }

    private static void ApplyOne(ScheduledAction action, bool wanted, ScheduleConfig schedule, AppConfig cfg,
        List<string> errors, Action<bool> setActive)
    {
        var name = TaskName(action);
        if (wanted)
        {
            var r = CreateTask(action, schedule, cfg);
            if (!r.ok) errors.Add(Label(action) + "任务: " + r.message);
            else setActive(true);
        }
        else if (TaskExists(name))
        {
            var r = DeleteTask(name);
            if (!r.ok) errors.Add("卸载" + Label(action) + "任务: " + r.message);
        }
    }

    public static bool WakeTaskExists() => TaskExists(WakeTaskName);
    public static bool ShutdownTaskExists() => TaskExists(ShutdownTaskName);
    public static bool RestartTaskExists() => TaskExists(RestartTaskName);
    public static bool HibernateTaskExists() => TaskExists(HibernateTaskName);

    public static bool TasksMatch(AppConfig cfg)
    {
        var restart = cfg.MasterEnabled && cfg.RestartEnabled && cfg.Restart.EnabledCount > 0;
        var hibernate = cfg.MasterEnabled && cfg.HibernateEnabled && cfg.Hibernate.EnabledCount > 0;
        var wake = cfg.MasterEnabled && cfg.HibernateEnabled && cfg.BootEnabled && cfg.Boot.EnabledCount > 0;
        var shutdown = cfg.MasterEnabled && cfg.ShutdownEnabled && cfg.Shutdown.EnabledCount > 0;
        return restart == TaskExists(RestartTaskName)
            && hibernate == TaskExists(HibernateTaskName)
            && wake == TaskExists(WakeTaskName)
            && shutdown == TaskExists(ShutdownTaskName);
    }

    /// <summary>删除本程序创建的全部计划任务；删除失败时返回 false。</summary>
    public static bool RemoveAll()
    {
        var ok = true;
        foreach (var name in new[] { RestartTaskName, HibernateTaskName, WakeTaskName, ShutdownTaskName })
        {
            var r = DeleteTask(name);
            if (!r.ok) { ok = false; Log.Warn("删除计划任务失败 " + name + ": " + r.message); }
        }
        return ok;
    }

    private static string TaskName(ScheduledAction action) => action switch
    {
        ScheduledAction.Restart => RestartTaskName,
        ScheduledAction.Hibernate => HibernateTaskName,
        ScheduledAction.Wake => WakeTaskName,
        _ => ShutdownTaskName
    };

    private static string Label(ScheduledAction action) => action switch
    {
        ScheduledAction.Restart => "重启",
        ScheduledAction.Hibernate => "休眠",
        ScheduledAction.Wake => "唤醒",
        _ => "关机"
    };

    private static bool TaskExists(string name)
    {
        var r = RunSchtasks($"/Query /TN \"{name}\"", 8000);
        return r.exitCode == 0;
    }

    private static (bool ok, string message) DeleteTask(string name)
    {
        if (!TaskExists(name)) return (true, "");
        var r = RunSchtasks($"/Delete /TN \"{name}\" /F", 10000);
        if (r.exitCode == 0) return (true, "");
        if (TryDeleteCom(name, out var comError)) return (true, "");
        var detail = Trim(r.output);
        if (!string.IsNullOrWhiteSpace(comError)) detail += "；COM: " + comError;
        return (false, detail);
    }

    private static bool TryRegisterCom(string name, string xml, out string error)
    {
        error = "";
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) { error = "任务计划 COM 服务不可用"; return false; }
            dynamic service = Activator.CreateInstance(type);
            service.Connect();
            dynamic root = service.GetFolder("\\");
            dynamic task = service.NewTask(0);
            task.XmlText = xml;
            // 6 = TASK_CREATE_OR_UPDATE，3 = TASK_LOGON_INTERACTIVE_TOKEN
            root.RegisterTaskDefinition(name, task, 6, null, null, 3);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryDeleteCom(string name, out string error)
    {
        error = "";
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) { error = "任务计划 COM 服务不可用"; return false; }
            dynamic service = Activator.CreateInstance(type);
            service.Connect();
            dynamic root = service.GetFolder("\\");
            root.DeleteTask(name, 0);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static (bool ok, string message) CreateTask(ScheduledAction action, ScheduleConfig schedule, AppConfig cfg)
    {
        var name = TaskName(action);
        var leadSeconds = action == ScheduledAction.Shutdown
            ? cfg.OnlineNoShutdown ? 180 : cfg.ShutdownCountdown ? 60 : 0
            : 0;
        var triggers = new XElement(Ns + "Triggers");
        for (var i = 0; i < 7; i++)
        {
            var day = schedule.Days[i];
            if (!day.Enabled) continue;
            var dow = i == 6 ? DayOfWeek.Sunday : (DayOfWeek)(i + 1);
            var start = NextOccurrence(dow, day.Hour, day.Minute, leadSeconds);
            triggers.Add(
                new XElement(Ns + "CalendarTrigger",
                    new XElement(Ns + "StartBoundary", start.ToString("yyyy-MM-dd'T'HH:mm:ss")),
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "ScheduleByWeek",
                        new XElement(Ns + "WeeksInterval", "1"),
                        new XElement(Ns + "DaysOfWeek", new XElement(Ns + DayElement(dow))))));
        }

        var useGuard = action == ScheduledAction.Shutdown && (cfg.ShutdownCountdown || cfg.OnlineNoShutdown);
        var command = action switch
        {
            ScheduledAction.Restart => "shutdown.exe",
            ScheduledAction.Hibernate => "shutdown.exe",
            ScheduledAction.Wake => "cmd.exe",
            _ => useGuard ? StartupRegistration.ExePath : "shutdown.exe"
        };
        var arguments = action switch
        {
            ScheduledAction.Restart => "/r /t 0",
            ScheduledAction.Hibernate => "/h",
            ScheduledAction.Wake => "/c exit",
            _ => useGuard ? "--shutdown-guard" : "/s /t 0"
        };
        var description = action switch
        {
            ScheduledAction.Restart => "SurfCore Auto-start 定时重启（清空内存负荷）",
            ScheduledAction.Hibernate => "SurfCore Auto-start 定时休眠（保留现场，供定时唤醒）",
            ScheduledAction.Wake => "SurfCore Auto-start 定时唤醒（从睡眠/休眠唤醒，并补跑错过的事件）",
            _ => "SurfCore Auto-start 定时关机"
        };

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-16", "yes"),
            new XElement(Ns + "Task", new XAttribute("version", "1.2"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Date", DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss")),
                    new XElement(Ns + "Author", Environment.UserName),
                    new XElement(Ns + "Description", description),
                    new XElement(Ns + "URI", "\\" + name)),
                triggers,
                new XElement(Ns + "Principals",
                    new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                        new XElement(Ns + "UserId", CurrentUserSid()),
                        new XElement(Ns + "LogonType", "InteractiveToken"),
                        new XElement(Ns + "RunLevel", "LeastPrivilege"))),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "AllowHardTerminate", "true"),
                    new XElement(Ns + "StartWhenAvailable", action == ScheduledAction.Wake ? "true" : "false"),
                    new XElement(Ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(Ns + "IdleSettings",
                        new XElement(Ns + "StopOnIdleEnd", "false"),
                        new XElement(Ns + "RestartOnIdle", "false")),
                    new XElement(Ns + "AllowStartOnDemand", "true"),
                    new XElement(Ns + "Enabled", "true"),
                    new XElement(Ns + "Hidden", "false"),
                    new XElement(Ns + "RunOnlyIfIdle", "false"),
                    // 只有唤醒任务占用唤醒定时器，其他任务不能抢占唯一下一个唤醒点。
                    new XElement(Ns + "WakeToRun", action == ScheduledAction.Wake ? "true" : "false"),
                    new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(Ns + "Priority", "7")),
                new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", command),
                        new XElement(Ns + "Arguments", arguments)))));

        // 任务 XML 只是传给 schtasks 的临时文件，不应依赖 %APPDATA% 的写权限。
        // 某些机器上 AppData 子目录会被安全软件/受限令牌拒绝，放到系统临时目录最稳妥。
        var dir = Path.Combine(Path.GetTempPath(), "SurfCoreAutoStart");
        Directory.CreateDirectory(dir);
        var xmlPath = Path.Combine(dir, action.ToString().ToLowerInvariant() + ".xml");
        var xml = doc.ToString(SaveOptions.DisableFormatting);
        var settings = new XmlWriterSettings { Encoding = Encoding.Unicode, Indent = true };
        using (var writer = XmlWriter.Create(xmlPath, settings)) doc.Save(writer);

        var r = RunSchtasks($"/Create /TN \"{name}\" /XML \"{xmlPath}\" /F", 20000);
        try { File.Delete(xmlPath); } catch { }
        if (r.exitCode == 0) return (true, "");

        // 某些安全软件只拦截 schtasks.exe，任务计划 COM 接口仍可用；作为兜底再试一次。
        if (TryRegisterCom(name, xml, out var comError)) return (true, "");
        var detail = Trim(r.output);
        if (!string.IsNullOrWhiteSpace(comError)) detail += "；COM: " + comError;
        return (false, detail);
    }

    /// <summary>确保系统允许休眠，并允许计划任务唤醒电脑；失败只记日志，不阻止任务创建。</summary>
    private static void EnsureWakeSupport()
    {
        RunTool("powercfg.exe", "/hibernate on", 15000);
        RunTool("powercfg.exe", "/SETACVALUEINDEX SCHEME_CURRENT SUB_SLEEP RTCWAKE 1", 10000);
        RunTool("powercfg.exe", "/SETDCVALUEINDEX SCHEME_CURRENT SUB_SLEEP RTCWAKE 1", 10000);
        RunTool("powercfg.exe", "/SETACTIVE SCHEME_CURRENT", 10000);
    }

    private static void RunTool(string file, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return;
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit(timeoutMs);
            if (p.ExitCode != 0)
                Log.Warn($"{file} {args} -> {p.ExitCode}: {(output + error).Trim()}");
        }
        catch (Exception ex)
        {
            Log.Warn($"{file} {args} 失败: {ex.Message}");
        }
    }

    private static DateTime NextOccurrence(DayOfWeek day, int hour, int minute, int leadSeconds)
    {
        var today = DateTime.Today;
        var diff = ((int)day - (int)today.DayOfWeek + 7) % 7;
        var dt = today.AddDays(diff).AddHours(hour).AddMinutes(minute).AddSeconds(-leadSeconds);
        if (dt <= DateTime.Now) dt = dt.AddDays(7);
        return dt;
    }

    private static string DayElement(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Monday",
        DayOfWeek.Tuesday => "Tuesday",
        DayOfWeek.Wednesday => "Wednesday",
        DayOfWeek.Thursday => "Thursday",
        DayOfWeek.Friday => "Friday",
        DayOfWeek.Saturday => "Saturday",
        _ => "Sunday"
    };

    private static string CurrentUserSid()
    {
        try { return WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName; }
        catch { return Environment.UserName; }
    }

    private static (int exitCode, string output) RunSchtasks(string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(Schtasks, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                psi.StandardOutputEncoding = Encoding.GetEncoding(936);
                psi.StandardErrorEncoding = Encoding.GetEncoding(936);
            }
            catch { }
            using var p = Process.Start(psi);
            if (p == null) return (-1, "无法启动 schtasks.exe");
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return (-1, "schtasks 超时");
            }
            var text = (output + "\n" + error).Trim();
            if (p.ExitCode != 0) Log.Warn($"schtasks {args} -> {p.ExitCode}: {text}");
            return (p.ExitCode, text);
        }
        catch (Exception ex)
        {
            Log.Error("调用 schtasks 失败", ex);
            return (-1, ex.Message);
        }
    }

    private static string Trim(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "未知错误（任务计划程序拒绝）";
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length > 400 ? s[..400] : s;
    }
}
