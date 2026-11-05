using System.Text.Json;
using System.Text.Json.Serialization;

namespace SurfCoreAutoStart;

internal sealed class DaySetting
{
    public bool Enabled { get; set; }
    public int Hour { get; set; }
    public int Minute { get; set; }

    public DaySetting Clone() => new() { Enabled = Enabled, Hour = Hour, Minute = Minute };

    [JsonIgnore]
    public string TimeText => $"{Hour:00}:{Minute:00}";
}

internal sealed class ScheduleConfig
{
    public List<DaySetting> Days { get; set; } = new();

    public static ScheduleConfig CreateDefault(int hour, int minute)
    {
        var s = new ScheduleConfig();
        for (var i = 0; i < 7; i++)
            s.Days.Add(new DaySetting { Enabled = true, Hour = hour, Minute = minute });
        return s;
    }

    public void Normalize(int fallbackHour, int fallbackMinute)
    {
        Days ??= new List<DaySetting>();
        while (Days.Count < 7) Days.Add(new DaySetting { Enabled = true, Hour = fallbackHour, Minute = fallbackMinute });
        if (Days.Count > 7) Days.RemoveRange(7, Days.Count - 7);
        foreach (var d in Days)
        {
            d.Hour = Math.Clamp(d.Hour, 0, 23);
            d.Minute = Math.Clamp(d.Minute, 0, 59);
        }
    }

    [JsonIgnore]
    public int EnabledCount => Days.Count(d => d.Enabled);
    public DaySetting this[int index] => Days[index];

    public ScheduleConfig Clone() => new()
    {
        Days = Days.Select(d => d.Clone()).ToList()
    };
}

internal sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 3;
    public bool MasterEnabled { get; set; }
    public bool BootEnabled { get; set; }
    public bool RestartEnabled { get; set; }
    public bool HibernateEnabled { get; set; }
    public bool ShutdownEnabled { get; set; }
    public ScheduleConfig Boot { get; set; } = ScheduleConfig.CreateDefault(7, 0);
    public ScheduleConfig Restart { get; set; } = ScheduleConfig.CreateDefault(12, 0);
    public ScheduleConfig Hibernate { get; set; } = ScheduleConfig.CreateDefault(23, 0);
    public ScheduleConfig Shutdown { get; set; } = ScheduleConfig.CreateDefault(23, 0);
    /// <summary>7 天开机/唤醒时间保持同一时刻（不影响其他动作）。</summary>
    public bool BootDailySame { get; set; }
    public bool RestartDailySame { get; set; }
    public bool HibernateDailySame { get; set; }
    /// <summary>7 天关机时间保持同一时刻（不影响其他动作）。</summary>
    public bool ShutdownDailySame { get; set; }

    /// <summary>关机前 1 分钟弹出倒计时窗口，允许取消。</summary>
    public bool ShutdownCountdown { get; set; } = true;

    /// <summary>关机前检测最近 3 分钟是否有键鼠输入，有人在使用则跳过本次关机。</summary>
    public bool OnlineNoShutdown { get; set; }

    /// <summary>定时关机改为休眠；Windows 计划任务只能从睡眠/休眠唤醒，完全关机无法自动开机。</summary>
    public bool HibernateOnShutdown { get; set; } = true;
    public bool SurfCoreAutoStart { get; set; }
    public string SnowLumaPasswordProtected { get; set; } = "";
    public string QqNumber { get; set; } = "";

    public string DshPath { get; set; } = "";
    public string SurfCoreDir { get; set; } = "";
    public string SnowLumaDir { get; set; } = "";
    public int SnowLumaPort { get; set; } = 5099;
    public int SurfCorePort { get; set; } = 3100;

    [JsonIgnore]
    public string SnowLumaPassword { get; set; } = "";

    [JsonIgnore]
    public bool LastEditingBoot { get; set; } = true;

    public ScheduleConfig GetSchedule(bool boot) => boot ? Boot : Shutdown;

    /// <summary>0=重启, 1=休眠, 2=唤醒, 3=关机。</summary>
    public ScheduleConfig GetSchedule(int index) => index switch
    {
        0 => Restart,
        1 => Hibernate,
        2 => Boot,
        _ => Shutdown
    };

    public bool GetDailySame(int index) => index switch
    {
        0 => RestartDailySame,
        1 => HibernateDailySame,
        2 => BootDailySame,
        _ => ShutdownDailySame
    };

    public void SetDailySame(int index, bool on)
    {
        if (index == 0) RestartDailySame = on;
        else if (index == 1) HibernateDailySame = on;
        else if (index == 2) BootDailySame = on;
        else ShutdownDailySame = on;
        ApplyLinkInvariants();
    }

    public bool IsActionEnabled(int index) => index switch
    {
        0 => RestartEnabled,
        1 => HibernateEnabled,
        2 => BootEnabled,
        _ => ShutdownEnabled
    };

    public void SetActionEnabled(int index, bool on)
    {
        if (index == 0) RestartEnabled = on;
        else if (index == 1) HibernateEnabled = on;
        else if (index == 2) BootEnabled = on;
        else ShutdownEnabled = on;
        if (!HibernateEnabled) BootEnabled = false;
    }

    /// <summary>有开机计划时，定时关机的实际动作是否改为休眠。</summary>
    [JsonIgnore]
    public bool ShouldHibernateForBoot =>
        HibernateOnShutdown && BootEnabled && Boot != null && Boot.Days.Any(d => d.Enabled);

    public void Normalize()
    {
        if (SchemaVersion < 3)
        {
            // v0.1 只有“开机/关机”两个计划：把原来的关机动作迁移成独立的休眠计划，
            // 这样“唤醒”仍然有休眠可依赖，也不会和新的关机计划同刻冲突。
            Hibernate = (Shutdown ?? ScheduleConfig.CreateDefault(23, 0)).Clone();
            Restart = ScheduleConfig.CreateDefault(12, 0);
            RestartEnabled = false;
            HibernateEnabled = HibernateOnShutdown;
            ShutdownEnabled = !HibernateOnShutdown;
            RestartDailySame = false;
            HibernateDailySame = false;
            SchemaVersion = 3;
        }
        Restart ??= ScheduleConfig.CreateDefault(12, 0);
        Hibernate ??= ScheduleConfig.CreateDefault(23, 0);
        Boot ??= ScheduleConfig.CreateDefault(7, 0);
        Shutdown ??= ScheduleConfig.CreateDefault(23, 0);
        Restart.Normalize(12, 0);
        Hibernate.Normalize(23, 0);
        Boot.Normalize(7, 0);
        Shutdown.Normalize(23, 0);
        if (!HibernateEnabled) BootEnabled = false;
        QqNumber = (QqNumber ?? "").Trim();
        SnowLumaPort = SnowLumaPort is > 0 and < 65536 ? SnowLumaPort : 5099;
        SurfCorePort = SurfCorePort is > 0 and < 65536 ? SurfCorePort : 3100;
        if (!string.IsNullOrEmpty(SnowLumaPasswordProtected) && string.IsNullOrEmpty(SnowLumaPassword))
            SnowLumaPassword = Native.Unprotect(SnowLumaPasswordProtected);
        ApplyLinkInvariants();
    }

    public void ApplyLinkInvariants()
    {
        // 每个动作各自统一 7 天时间；不同动作之间互不影响。
        if (RestartDailySame) UnifyDays(Restart);
        if (HibernateDailySame) UnifyDays(Hibernate);
        if (BootDailySame) UnifyDays(Boot);
        if (ShutdownDailySame) UnifyDays(Shutdown);
    }

    private static void UnifyDays(ScheduleConfig schedule)
    {
        var baseDay = schedule.Days[0];
        for (var i = 1; i < 7; i++)
        {
            schedule.Days[i].Hour = baseDay.Hour;
            schedule.Days[i].Minute = baseDay.Minute;
        }
    }

    public void SetSnowLumaPassword(string password)
    {
        SnowLumaPassword = password ?? "";
        SnowLumaPasswordProtected = string.IsNullOrEmpty(SnowLumaPassword) ? "" : Native.Protect(SnowLumaPassword);
    }

    public AppConfig DeepClone()
    {
        var json = JsonSerializer.Serialize(this, ConfigStore.JsonOptions);
        var clone = JsonSerializer.Deserialize<AppConfig>(json, ConfigStore.JsonOptions) ?? new AppConfig();
        clone.SnowLumaPassword = SnowLumaPassword;
        clone.LastEditingBoot = LastEditingBoot;
        return clone;
    }
}

internal static class PathResolver
{
    public static string DataDir
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("SURFCORE_AUTOSTART_DATA");
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SurfCore Auto-start");
        }
    }

    public static string LogsDir => Path.Combine(DataDir, "logs");

    public static string DshExe()
    {
        var candidates = new[]
        {
            @"H:\AI\deepseek\DeepSeek Harness.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DeepSeek Harness", "DeepSeek Harness.exe")
        };
        foreach (var c in candidates) if (File.Exists(c)) return c;
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("DeepSeek Harness"))
            {
                try
                {
                    if (!string.IsNullOrEmpty(p.MainModule?.FileName)) return p.MainModule.FileName;
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
        return "";
    }

    public static string SurfCoreDir()
    {
        var candidates = new List<string> { @"H:\AI\surfcore" };
        try
        {
            var exeDir = AppContext.BaseDirectory;
            candidates.Add(Path.GetFullPath(Path.Combine(exeDir, "..", "surfcore")));
            candidates.Add(Path.GetFullPath(Path.Combine(exeDir, "surfcore")));
        }
        catch { }
        return candidates.FirstOrDefault(Directory.Exists) ?? @"H:\AI\surfcore";
    }

    public static string SnowLumaDir(string surfCoreDir)
    {
        if (!Directory.Exists(surfCoreDir)) return "";
        var exact = Path.Combine(surfCoreDir, "SnowLuma-v2.4.0-win-x64");
        if (Directory.Exists(exact)) return exact;
        try
        {
            return Directory.GetDirectories(surfCoreDir, "SnowLuma*").OrderByDescending(x => x).FirstOrDefault() ?? "";
        }
        catch { return ""; }
    }

    public static string NodeExe(string snowLumaDir)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(snowLumaDir)) candidates.Add(Path.Combine(snowLumaDir, "node.exe"));
        try
        {
            var dshRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh", "dsh-runtimes");
            if (Directory.Exists(dshRoot))
                foreach (var dir in Directory.GetDirectories(dshRoot))
                {
                    var p = Path.Combine(dir, "dependencies", "node", "bin", "node.exe");
                    if (File.Exists(p)) candidates.Add(p);
                }
        }
        catch { }
        foreach (var c in candidates) if (File.Exists(c)) return c;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(part.Trim(), "node.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return "";
    }
}

internal static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string ConfigPath => Path.Combine(PathResolver.DataDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            Directory.CreateDirectory(PathResolver.DataDir);
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
                var migrated = cfg.SchemaVersion < 3;
                cfg.Normalize();
                if (migrated) Save(cfg);
                return cfg;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("读取配置失败: " + ex.Message);
        }
        var fresh = new AppConfig();
        fresh.Normalize();
        var dsh = PathResolver.DshExe();
        var surf = PathResolver.SurfCoreDir();
        fresh.DshPath = dsh;
        fresh.SurfCoreDir = surf;
        fresh.SnowLumaDir = PathResolver.SnowLumaDir(surf);
        return fresh;
    }

    public static void Save(AppConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(PathResolver.DataDir);
            cfg.Normalize();
            var json = JsonSerializer.Serialize(cfg, JsonOptions);
            var tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, ConfigPath, true);
        }
        catch (Exception ex)
        {
            Log.Warn("保存配置失败: " + ex.Message);
        }
    }
}

internal static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception ex = null)
        => Write("ERROR", ex == null ? message : message + " :: " + ex);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(PathResolver.LogsDir);
                var path = Path.Combine(PathResolver.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不能影响主流程；诊断信息由界面弹窗展示。
        }
    }
}
