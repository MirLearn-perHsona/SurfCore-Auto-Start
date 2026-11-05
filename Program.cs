using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;

namespace SurfCoreAutoStart;

internal static class Program
{
    internal static bool ElevatedLaunch { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        // schtasks 的输出是系统 OEM 代码页（中文系统为 GBK），注册后才能正确解码错误信息。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Application.ThreadException += (_, e) => Fatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal(e.ExceptionObject as Exception);

        ElevatedLaunch = HasFlag(args, "--elevated");

        try
        {
            if (HasFlag(args, "--apply"))
                return ApplyMode(args);
            if (HasFlag(args, "--diag"))
                return DiagMode(args);
            if (HasFlag(args, "--launch-test"))
                return LaunchTestMode(args);
            if (HasFlag(args, "--shutdown-guard"))
            {
                Application.Run(new ShutdownGuardForm(false));
                return 0;
            }
            if (HasFlag(args, "--guard-demo"))
            {
                Application.Run(new ShutdownGuardForm(true));
                return 0;
            }
            if (HasFlag(args, "--screenshot"))
                return ScreenshotMode(args);
            if (HasFlag(args, "--guard-screenshot"))
                return GuardScreenshotMode(args);

            if (HasFlag(args, "--install"))
                return InstallMode();

            // 从 H 盘、聊天工具或旧快捷方式双击时会继承 Medium 令牌。
            // 已安装时直接交给高权限登录任务，避免每次启动都弹“需要管理员权限”。
            if (!Native.IsHighIntegrity())
            {
                if (Installer.IsUsable && StartupRegistration.RunHighTask())
                    return 0;

                if (Installer.RequestElevatedInstall())
                    return 0;

                // 用户取消 UAC。只在确实需要安装时提示一次，不进入主界面。
                MessageBox.Show(
                    "首次运行或修复安装需要管理员权限，用来创建开机任务和桌面快捷方式。\n\n" +
                    "已取消，本次没有启动。",
                    "SurfCore Auto-start", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            // 高权限实例负责自愈：缺失的安装文件、开机任务、桌面快捷方式都会在这里补上。
            var wasInstalled = Installer.IsInstalledApp;
            try
            {
                Installer.EnsureHighEnvironment();
            }
            catch (Exception ex)
            {
                Log.Error("初始化安装环境失败", ex);
                MessageBox.Show("初始化失败：" + ex.Message, "SurfCore Auto-start",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            if (!wasInstalled)
            {
                // 当前是下载目录/H 盘里的副本；高权限环境准备好后交给安装目录的实例。
                try
                {
                    Installer.StartInstalled();
                }
                catch (Exception ex)
                {
                    Log.Error("启动安装目录实例失败", ex);
                    MessageBox.Show(ex.Message, "SurfCore Auto-start",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return 0;
            }

            if (!EnsureSingleInstance())
                return 0;

            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            Fatal(ex);
            return 1;
        }
    }

    /// <summary>UAC 提权后的安装入口：部署文件、强制重建任务与快捷方式，然后启动安装目录实例。</summary>
    private static int InstallMode()
    {
        try
        {
            Installer.EnsureHighEnvironment(forceTask: true);
            Installer.StartInstalled();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("安装失败", ex);
            MessageBox.Show("安装失败：" + ex.Message, "SurfCore Auto-start",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static Mutex _instanceMutex;

    private static bool EnsureSingleInstance()
    {
        bool created;
        try
        {
            _instanceMutex = new Mutex(true, @"Local\SurfCoreAutoStart.SingleInstance", out created);
        }
        catch
        {
            return true;
        }
        if (created) return true;

        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(
                         Path.GetFileNameWithoutExtension(Environment.ProcessPath)))
            {
                if (p.Id == Environment.ProcessId) continue;
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero) continue;
                ShowWindow(h, 9);
                SetForegroundWindow(h);
                break;
            }
        }
        catch { }
        return false;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static bool CanWriteDataDir()
    {
        try
        {
            Directory.CreateDirectory(PathResolver.DataDir);
            var probe = Path.Combine(PathResolver.DataDir, ".write-probe");
            File.WriteAllText(probe, DateTime.Now.ToString("O"));
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    internal static bool RelaunchElevated()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = StartupRegistration.ExePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "--elevated"
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch { return false; }
    }

    private static int ApplyMode(string[] args)
    {
        var cfg = ConfigStore.Load();
        var result = Scheduler.Apply(cfg);
        var data = new
        {
            result.Success,
            result.Message,
            result.RestartActive,
            result.HibernateActive,
            result.WakeActive,
            result.ShutdownActive,
            result.RestartDays,
            result.HibernateDays,
            result.WakeDays,
            result.ShutdownDays,
            config = new
            {
                cfg.MasterEnabled,
                cfg.RestartEnabled,
                cfg.HibernateEnabled,
                cfg.BootEnabled,
                cfg.ShutdownEnabled,
                cfg.RestartDailySame,
                cfg.HibernateDailySame,
                cfg.BootDailySame,
                cfg.ShutdownDailySame
            }
        };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        var outPath = ArgValue(args, "--result");
        if (!string.IsNullOrEmpty(outPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");
            File.WriteAllText(outPath, json);
        }
        Log.Info("ApplyMode: " + json);
        return result.Success ? 0 : 1;
    }

    private static int LaunchTestMode(string[] args)
    {
        var cfg = ConfigStore.Load();
        cfg.Normalize();
        var log = new StringBuilder();
        var result = LaunchService.RunAsync(cfg, s => log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {s}")).GetAwaiter().GetResult();
        var data = new
        {
            result.Success,
            result.Message,
            result.Token,
            result.Injected,
            result.InjectedPid,
            log = log.ToString()
        };
        var outPath = ArgValue(args, "--launch-result");
        if (string.IsNullOrWhiteSpace(outPath)) outPath = "launch-result.json";
        var full = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        File.WriteAllText(full, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        return result.Success ? 0 : 1;
    }

    private static int DiagMode(string[] args)
    {
        var outPath = ArgValue(args, "--diag");
        if (string.IsNullOrWhiteSpace(outPath)) outPath = "diag.txt";
        var lines = new List<string>
        {
            "DataDir=[" + PathResolver.DataDir + "]",
            "LogsDir=[" + PathResolver.LogsDir + "]",
            "ConfigPath=[" + ConfigStore.ConfigPath + "]",
            "EnvSURFCORE=[" + (Environment.GetEnvironmentVariable("SURFCORE_AUTOSTART_DATA") ?? "<null>") + "]",
            "AppData=[" + Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "]",
            "BaseDir=[" + AppContext.BaseDirectory + "]",
            "Cwd=[" + Environment.CurrentDirectory + "]",
            "Admin=" + IsAdmin(),
            "Identity=" + System.Security.Principal.WindowsIdentity.GetCurrent().Name,
            "Session=" + System.Diagnostics.Process.GetCurrentProcess().SessionId,
            "Interactive=" + Environment.UserInteractive,
        };
        try
        {
            Directory.CreateDirectory(PathResolver.LogsDir);
            var probe = Path.Combine(PathResolver.LogsDir, "diag-probe.txt");
            File.WriteAllText(probe, DateTime.Now.ToString("O"));
            lines.Add("WriteProbe=OK " + probe);
            var appLog = Path.Combine(PathResolver.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(appLog, "[diag] append " + DateTime.Now.ToString("O") + Environment.NewLine);
            lines.Add("AppendLog=OK " + appLog);
        }
        catch (Exception ex)
        {
            lines.Add("WriteProbe=FAIL " + ex.GetType().Name + ": " + ex.Message);
        }
        try
        {
            var cfg = new AppConfig { MasterEnabled = true, BootEnabled = true, ShutdownEnabled = true };
            cfg.Boot = ScheduleConfig.CreateDefault(7, 0);
            cfg.Shutdown = ScheduleConfig.CreateDefault(23, 0);
            cfg.Normalize();
            var r = Scheduler.Apply(cfg);
            lines.Add("TaskProbe=" + (r.Success ? "OK" : "FAIL") + " msg=" + r.Message);
            var off = cfg.DeepClone();
            off.MasterEnabled = false;
            Scheduler.Apply(off);
        }
        catch (Exception ex)
        {
            lines.Add("TaskProbe=EX " + ex.GetType().Name + ": " + ex.Message);
        }
        var full = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        File.WriteAllText(full, string.Join(Environment.NewLine, lines));
        return 0;
    }

    private static bool IsAdmin()
    {
        return Native.IsHighIntegrity();
    }

    private static int ScreenshotMode(string[] args)
    {
        var path = ArgValue(args, "--screenshot");
        if (string.IsNullOrWhiteSpace(path)) path = "screenshot.png";
        var expanded = HasFlag(args, "--expanded");
        var quick = HasFlag(args, "--quick");
        var power = HasFlag(args, "--power");

        var cfg = BuildPreviewConfig();
        if (HasFlag(args, "--master-off")) cfg.MasterEnabled = false;
        if (HasFlag(args, "--hibernate-off")) { cfg.HibernateEnabled = false; cfg.BootEnabled = false; }
        using var form = new MainForm(cfg, preview: true, previewExpanded: expanded, previewQuick: quick);
        form.Show();
        form.TopMost = true;
        form.Activate();
        form.BringToFront();
        Application.DoEvents();
        if (power)
        {
            var mi = typeof(MainForm).GetMethod("OpenPowerPopup",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            mi?.Invoke(form, null);
            Application.DoEvents();
            Thread.Sleep(300);
            Application.DoEvents();
        }
        Thread.Sleep(expanded ? 700 : 500);
        Application.DoEvents();

        try
        {
            // 无边框窗体的 Bounds 会带 DWM 阴影边距，截图应以客户区为准。
            var bounds = form.RectangleToScreen(form.ClientRectangle);
            if (quick)
            {
                var popup = Application.OpenForms.OfType<QuickPopup>().FirstOrDefault();
                if (popup != null && popup.Visible)
                    bounds = Rectangle.Union(bounds, popup.Bounds);
            }
            if (power)
            {
                var popup = Application.OpenForms.OfType<OptionsPopup>().FirstOrDefault();
                if (popup != null && popup.Visible)
                    bounds = Rectangle.Union(bounds, popup.Bounds);
            }
            using var bmp = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
            bmp.Save(full, ImageFormat.Png);
            Log.Info("截图已保存: " + full);
        }
        catch (Exception ex)
        {
            Log.Warn("屏幕截图失败，尝试 DrawToBitmap: " + ex.Message);
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
            bmp.Save(full, ImageFormat.Png);
        }

        form.Close();
        return 0;
    }

    private static int GuardScreenshotMode(string[] args)
    {
        var path = ArgValue(args, "--guard-screenshot");
        if (string.IsNullOrWhiteSpace(path)) path = "guard.png";
        using var form = new ShutdownGuardForm(true);
        form.Show();
        form.TopMost = true;
        form.Activate();
        Application.DoEvents();
        Thread.Sleep(900);
        Application.DoEvents();
        var bounds = form.RectangleToScreen(form.ClientRectangle);
        using var bmp = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        bmp.Save(full, ImageFormat.Png);
        form.Close();
        return 0;
    }

    private static AppConfig BuildPreviewConfig()
    {
        var cfg = new AppConfig
        {
            MasterEnabled = true,
            RestartEnabled = true,
            HibernateEnabled = true,
            BootEnabled = true,
            ShutdownEnabled = true,
            RestartDailySame = false,
            HibernateDailySame = false,
            BootDailySame = false,
            ShutdownDailySame = false
        };
        cfg.Restart = ScheduleConfig.CreateDefault(4, 0);
        cfg.Hibernate = ScheduleConfig.CreateDefault(23, 30);
        cfg.Boot = ScheduleConfig.CreateDefault(7, 0);
        cfg.Shutdown = ScheduleConfig.CreateDefault(22, 0);
        for (var i = 0; i < 7; i++)
        {
            cfg.Restart.Days[i].Enabled = i < 5;
            cfg.Hibernate.Days[i].Enabled = i < 5;
            cfg.Boot.Days[i].Enabled = i < 5;
            cfg.Shutdown.Days[i].Enabled = i < 5;
        }
        cfg.Normalize();
        return cfg;
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static string ArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static void Fatal(Exception ex)
    {
        try { Log.Error("未处理异常", ex); } catch { }
        try
        {
            MessageBox.Show(ex?.ToString() ?? "未知错误", "SurfCore Auto-start 发生错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
