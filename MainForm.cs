using System.Drawing.Drawing2D;
using System.Text;

namespace SurfCoreAutoStart;

internal sealed class MainForm : Form
{
    private readonly AppConfig _cfg;
    private readonly bool _preview;
    private readonly bool _previewExpanded;
    private readonly bool _previewQuick;
    private readonly Icon _appIcon;

    private readonly ToggleSwitch _masterToggle;
    private readonly ToggleSwitch _restartToggle;
    private readonly ToggleSwitch _wakeToggle;
    private readonly ToggleSwitch _hibernateToggle;
    private readonly ToggleSwitch _shutdownToggle;
    private readonly TabSwitch _tab;
    private readonly DayWheel _dayWheel;
    private readonly ValueWheel _hourWheel;
    private readonly ValueWheel _minuteWheel;
    private readonly QuickStrip _quickStrip;
    private readonly OptionStrip _powerStrip;
    private readonly ToggleSwitch _surfToggle;
    private readonly FlatButton _launchButton;
    private readonly FlatButton _deleteButton;
    private readonly InputBox _passwordBox;
    private readonly InputBox _qqBox;
    private readonly IconButton _minButton;
    private readonly IconButton _closeButton;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private readonly System.Windows.Forms.Timer _statusTimer;

    private QuickPopup _popup;
    private OptionsPopup _powerPopup;
    private bool _loading = true;
    private bool _expanded;
    private int _editIndex = 2;
    private int _selectedDay;
    private bool _syncBusy;
    private bool _syncAgain;
    private bool _syncErrorShown;
    private bool _launching;
    private bool _deleting;
    private string _status = "就绪";
    private bool _statusError;
    private DateTime _statusAt = DateTime.Now;
    private string _taskStatus = "计划任务 · 检查中…";
    private bool _taskError;

    private const float BaseWidth = 600f;
    private const float BaseHeight = 540f;
    private const float ExpandedHeight = 626f;

    public MainForm(AppConfig cfg = null, bool preview = false, bool previewExpanded = false, bool previewQuick = false)
    {
        _cfg = cfg ?? ConfigStore.Load();
        _preview = preview;
        _previewExpanded = previewExpanded;
        _previewQuick = previewQuick;
        _selectedDay = ((int)DateTime.Today.DayOfWeek + 6) % 7;

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(iconPath)) _appIcon = new Icon(iconPath, 32, 32);
        }
        catch { }

        Text = "SurfCore Auto-start";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowIcon = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        ClientSize = new Size(600, 540);

        _masterToggle = new ToggleSwitch { Surface = Theme.Card };
        _restartToggle = new ToggleSwitch { Surface = Theme.Card, Size = new Size(30, 17) };
        _wakeToggle = new ToggleSwitch { Surface = Theme.Card, Size = new Size(26, 15) };
        _hibernateToggle = new ToggleSwitch { Surface = Theme.Card, Size = new Size(26, 15) };
        _shutdownToggle = new ToggleSwitch { Surface = Theme.Card, Size = new Size(26, 15) };
        _tab = new TabSwitch { Surface = Theme.Card, Items = new[] { "重启", "休眠", "唤醒", "关机" }, SelectedIndex = 2 };
        _dayWheel = new DayWheel { Surface = Theme.Card };
        _hourWheel = new ValueWheel { Surface = Theme.Card, Min = 0, Max = 23, Value = 7 };
        _minuteWheel = new ValueWheel { Surface = Theme.Card, Min = 0, Max = 59, Value = 0 };
        _quickStrip = new QuickStrip { Surface = Theme.Card };
        _powerStrip = new OptionStrip { Surface = Theme.Card, Label = "关机选项", Options = new bool[2] };
        _surfToggle = new ToggleSwitch { Surface = Theme.Card, Size = new Size(40, 22) };
        _launchButton = new FlatButton { Text = "立即启动", Style = FlatButtonStyle.Soft, Surface = Theme.Card, Radius = 8 };
        _deleteButton = new FlatButton { Text = "卸载", Style = FlatButtonStyle.Danger, Surface = Theme.Bg, Radius = 6 };
        _passwordBox = new InputBox { Surface = Theme.Card, Password = true, Placeholder = "SnowLuma 控制台令牌" };
        _qqBox = new InputBox { Surface = Theme.Card, DigitsOnly = true, MaxDigits = 12, Placeholder = "QQ 号（用于匹配进程）" };
        _minButton = new IconButton { Kind = IconKind.Minimize, Surface = Theme.Bg };
        _closeButton = new IconButton { Kind = IconKind.Close, Surface = Theme.Bg };
        _saveTimer = new System.Windows.Forms.Timer { Interval = 450 };
        _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };

        Controls.AddRange(new Control[]
        {
            _masterToggle, _restartToggle, _wakeToggle, _hibernateToggle, _shutdownToggle, _tab, _dayWheel, _hourWheel, _minuteWheel,
            _quickStrip, _powerStrip, _surfToggle, _launchButton, _deleteButton, _passwordBox, _qqBox,
            _minButton, _closeButton
        });

        _masterToggle.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _cfg.MasterEnabled = _masterToggle.Checked;
            UpdateEnabledStates();
            ScheduleSave();
            SyncScheduleAsync();
            SetStatus(_cfg.MasterEnabled ? "总开关已开启" : "总开关已关闭，计划任务已移除");
        };
        _restartToggle.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _cfg.RestartEnabled = _restartToggle.Checked;
            UpdateEnabledStates();
            ScheduleSave();
            SyncScheduleAsync();
        };
        _hibernateToggle.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _cfg.HibernateEnabled = _hibernateToggle.Checked;
            if (!_cfg.HibernateEnabled && _cfg.BootEnabled)
            {
                _cfg.BootEnabled = false;
                _loading = true;
                _wakeToggle.Checked = false;
                _loading = false;
            }
            UpdateEnabledStates();
            RefreshScheduleUi();
            ScheduleSave();
            SyncScheduleAsync();
            SetStatus(_cfg.HibernateEnabled ? "休眠已开启，可以启用唤醒" : "休眠已关闭，唤醒已禁用");
        };
        _wakeToggle.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            if (_wakeToggle.Checked && !_cfg.HibernateEnabled)
            {
                _loading = true;
                _wakeToggle.Checked = false;
                _loading = false;
                return;
            }
            _cfg.BootEnabled = _wakeToggle.Checked;
            ScheduleSave();
            SyncScheduleAsync();
        };
        _shutdownToggle.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _cfg.ShutdownEnabled = _shutdownToggle.Checked;
            ScheduleSave();
            SyncScheduleAsync();
        };
        _tab.SelectionChanged += (_, _) =>
        {
            _editIndex = _tab.SelectedIndex;
            RefreshScheduleUi();
        };
        _dayWheel.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            _selectedDay = _dayWheel.SelectedIndex;
            LoadDayIntoValueWheels();
            Invalidate();
        };
        _dayWheel.DayEnabledChanged += (_, _) =>
        {
            if (_loading) return;
            var sched = _cfg.GetSchedule(_editIndex);
            sched.Days[_selectedDay].Enabled = _dayWheel.GetEnabled(_selectedDay);
            ScheduleSave();
            SyncScheduleAsync();
            Invalidate();
        };
        _hourWheel.ValueChanged += (_, _) => OnTimeEdited();
        _minuteWheel.ValueChanged += (_, _) => OnTimeEdited();
        _quickStrip.OpenRequested += (_, _) => OpenQuickPopup();
        _powerStrip.OpenRequested += (_, _) => OpenPowerPopup();
        _deleteButton.Click += (_, _) => DeleteEverything();
        _surfToggle.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _cfg.SurfCoreAutoStart = _surfToggle.Checked;
            SetExpanded(_surfToggle.Checked);
            ScheduleSave();
            SetStatus(_surfToggle.Checked ? "SurfCore 自启动已开启" : "SurfCore 自启动已关闭");
        };
        _launchButton.Click += async (_, _) => await RunLaunchChainAsync(false);
        _passwordBox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            _cfg.SetSnowLumaPassword(_passwordBox.Text);
            ScheduleSave();
        };
        _qqBox.TextChanged += (_, _) =>
        {
            if (_loading) return;
            _cfg.QqNumber = _qqBox.Text.Trim();
            ScheduleSave();
        };
        _minButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _closeButton.Click += (_, _) => Close();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _statusTimer.Tick += (_, _) =>
        {
            if (!_statusError && (DateTime.Now - _statusAt).TotalSeconds > 6 && _status != "就绪")
            {
                _status = "就绪";
                Invalidate();
            }
        };

        ApplyConfigToControls();
        _statusTimer.Start();
    }

    private float Sc => DeviceDpi / 96f;
    private int L(float v) => (int)Math.Round(v * Sc);
    private Rectangle R(float x, float y, float w, float h) => new(L(x), L(y), L(w), L(h));

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        LayoutAll();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRegion();
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        ApplyRegion();
    }

    private void ApplyRegion()
    {
        if (ClientSize.Width < 10 || ClientSize.Height < 10) return;
        try
        {
            using var path = Theme.Rounded(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), 14f * Sc);
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
        }
        catch { }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // 无边框窗体在首次创建句柄时会短暂套用标题栏尺寸，等样式生效后再校准一次。
        ClientSize = new Size(L(BaseWidth), L(_expanded ? ExpandedHeight : BaseHeight));
        LayoutAll();
        ApplyRegion();
        BeginInvoke(new Action(() => { ApplyRegion(); LayoutAll(); }));
        if (_preview)
        {
            if (_previewQuick) BeginInvoke(new Action(OpenQuickPopup));
            return;
        }
        Log.Info("启动: data=" + PathResolver.DataDir + " config=" + ConfigStore.ConfigPath +
                 " admin=" + IsElevated());
        if (!IsElevated())
        {
            MessageBox.Show(this,
                "当前启动方式没有管理员权限，Windows 会拒绝创建定时开关机任务。\n\n" +
                "请关闭本窗口，双击桌面上的 “SurfCore Auto-start” 快捷方式。\n" +
                "不要直接从聊天工具、网页或 H: 盘双击 exe，否则会继承受限权限。",
                "需要管理员权限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            BeginInvoke(new Action(Close));
            return;
        }
        if (!StartupRegistration.Ensure())
            SetStatus("警告：写入开机自启动失败", true);
        SyncScheduleAsync(false);
        if (_cfg.SurfCoreAutoStart && _cfg.MasterEnabled) _ = AutoStartAsync();
    }

    private static bool IsElevated()
    {
        return Native.IsHighIntegrity();
    }

    private void RelaunchElevated()
    {
        if (Program.RelaunchElevated()) { Close(); return; }
        SetStatus("已取消管理员授权", true);
    }

    private void DeleteEverything()
    {
        if (MessageBox.Show(this, "是否确定卸载？", "卸载 SurfCore Auto-start",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        if (MessageBox.Show(this, "卸载将还原所有已修改设置至初始状态。\n\n确定继续吗？",
                "再次确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        // 低权限实例既删不掉安装目录，也删不掉高权限任务；宁可中止，避免留下半卸载状态。
        if (!IsElevated())
        {
            MessageBox.Show(this,
                "卸载需要管理员权限。\n\n请关闭本窗口，双击桌面的 “SurfCore Auto-start” 快捷方式后重试。",
                "无法卸载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 即使计划任务因权限不足删不掉，也要移除自启动项并继续清理文件与缓存。
        var taskFailed = !Scheduler.RemoveAll();
        var startupFailed = !StartupRegistration.Remove();
        _deleting = true;
        _saveTimer.Stop();

        try
        {
            var exe = Application.ExecutablePath;
            var dir = AppContext.BaseDirectory.TrimEnd('\\');
            var data = PathResolver.DataDir.TrimEnd('\\');
            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SurfCore Auto-start");
            var desktopLnk = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "SurfCore Auto-start.lnk");
            // 只有目录里全是本程序文件时才递归删除，避免误删用户自己的工作目录。
            var ownedDir = IsOwnedDirectory(dir);
            var pid = Environment.ProcessId;
            var bat = Path.Combine(Path.GetTempPath(), "surfcore-autostart-cleanup-" + Guid.NewGuid().ToString("N") + ".cmd");
            var lines = new List<string>
            {
                "@echo off",
                "ping 127.0.0.1 -n 2 >nul 2>nul",
                "set /a n=0",
                ":waitproc",
                $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul",
                "if errorlevel 1 goto delete",
                "set /a n+=1",
                "if %n% GEQ 240 goto delete",
                "ping 127.0.0.1 -n 2 >nul 2>nul",
                "goto waitproc",
                ":delete",
                "set /a n=0",
                ":retryexe",
                $"del /f /q \"{exe}\" >nul 2>nul",
                $"if not exist \"{exe}\" goto cleanup",
                "set /a n+=1",
                "if %n% GEQ 30 goto cleanup",
                "ping 127.0.0.1 -n 2 >nul 2>nul",
                "goto retryexe",
                ":cleanup",
                $"del /f /q \"{dir}\\SurfCore Auto-start.*\" >nul 2>nul",
                $"del /f /q \"{dir}\\app.ico\" >nul 2>nul",
                $"del /f /q \"{dir}\\start.bat\" >nul 2>nul",
                $"del /f /q \"{desktopLnk}\" >nul 2>nul",
                $"rmdir /s /q \"{data}\" >nul 2>nul",
                $"rmdir /s /q \"{local}\" >nul 2>nul",
                ownedDir ? $"rmdir /s /q \"{dir}\" >nul 2>nul" : $"rmdir \"{dir}\" >nul 2>nul",
                "del /f /q \"%~f0\" >nul 2>nul"
            };
            File.WriteAllLines(bat, lines, Encoding.Default);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + bat + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Warn("启动卸载脚本失败: " + ex.Message);
            _deleting = false;
            MessageBox.Show(this, "卸载脚本启动失败：" + ex.Message, "卸载未完成",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var done = "文件与缓存已删除，感谢您的使用。";
        if (taskFailed)
            done += "\n\n注意：部分 Windows 计划任务未能卸载，请以管理员身份在“任务计划程序”中卸载 SurfCoreAutoStart-Restart / -Hibernate / -Boot / -Shutdown。";
        if (startupFailed)
            done += "\n\n注意：开机自启动任务 SurfCoreAutoStart-App 未能卸载，请在“任务计划程序”中手动删除。";
        MessageBox.Show(this, done, "SurfCore Auto-start",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }

    private static bool IsOwnedDirectory(string dir)
    {
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!IsOwnedDirectory(sub)) return false;
            }
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("SurfCore Auto-start.", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, "app.ico", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, "start.bat", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, "README.md", StringComparison.OrdinalIgnoreCase)) continue;
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task AutoStartAsync()
    {
        await Task.Delay(900);
        if (!IsDisposed) await RunLaunchChainAsync(true);
    }

    private void ApplyConfigToControls()
    {
        _loading = true;
        _masterToggle.Checked = _cfg.MasterEnabled;
        _restartToggle.Checked = _cfg.RestartEnabled;
        _hibernateToggle.Checked = _cfg.HibernateEnabled;
        _wakeToggle.Checked = _cfg.BootEnabled && _cfg.HibernateEnabled;
        _shutdownToggle.Checked = _cfg.ShutdownEnabled;
        _tab.SelectedIndex = _editIndex;
        _surfToggle.Checked = _cfg.SurfCoreAutoStart;
        _powerStrip.Options = new[] { _cfg.ShutdownCountdown, _cfg.OnlineNoShutdown };
        _passwordBox.Text = _cfg.SnowLumaPassword;
        _qqBox.Text = _cfg.QqNumber;
        _expanded = _cfg.SurfCoreAutoStart || _previewExpanded;
        if (_previewExpanded) _surfToggle.Checked = true;
        _loading = false;

        SetExpanded(_expanded, false);
        RefreshScheduleUi();
        UpdateEnabledStates();
        _status = _preview ? "预览模式" : "就绪";
        _statusAt = DateTime.Now;
    }

    private void LayoutAll()
    {
        var s = Sc;
        var targetH = _expanded ? ExpandedHeight : BaseHeight;
        var want = new Size(L(BaseWidth), L(targetH));
        if (ClientSize != want) ClientSize = want;

        _minButton.Bounds = R(BaseWidth - 82, 8, 34, 28);
        _closeButton.Bounds = R(BaseWidth - 44, 8, 34, 28);

        _masterToggle.Bounds = R(40, 76, 44, 24);
        _restartToggle.Bounds = R(280, 59, 30, 17);
        _wakeToggle.Bounds = R(278, 97, 26, 15);
        _hibernateToggle.Bounds = R(362, 97, 26, 15);
        _shutdownToggle.Bounds = R(450, 97, 26, 15);
        _powerStrip.Bounds = R(528, 52, 44, 70);

        _tab.Bounds = R(42, 148, 240, 28);
        _dayWheel.Bounds = R(42, 204, 210, 150);
        _hourWheel.Bounds = R(262, 204, 116, 150);
        _minuteWheel.Bounds = R(388, 204, 116, 150);
        _quickStrip.Bounds = R(514, 204, 44, 150);

        _surfToggle.Bounds = R(42, 445, 40, 22);
        _launchButton.Bounds = R(474, 442, 84, 28);
        _deleteButton.Bounds = R(24, _expanded ? 592 : 506, 56, 22);
        _passwordBox.Bounds = R(42, 500, 300, 34);
        _qqBox.Bounds = R(352, 500, 206, 34);
        _passwordBox.Visible = _expanded;
        _qqBox.Visible = _expanded;

        if (_expanded)
        {
            var screen = Screen.FromControl(this).WorkingArea;
            if (Top + Height > screen.Bottom) Top = Math.Max(screen.Top, screen.Bottom - Height);
        }
        Invalidate();
    }

    private void SetExpanded(bool expanded, bool moveWindow = true)
    {
        if (_expanded == expanded && IsHandleCreated) return;
        _expanded = expanded;
        LayoutAll();
        if (moveWindow)
        {
            var screen = Screen.FromControl(this).WorkingArea;
            if (Top + Height > screen.Bottom) Top = Math.Max(screen.Top, screen.Bottom - Height);
        }
    }

    private void RefreshScheduleUi()
    {
        _loading = true;
        _dayWheel.SelectedIndex = _selectedDay;
        var sched = _cfg.GetSchedule(_editIndex);
        _dayWheel.SetAll(sched.Days.Select(d => d.Enabled).ToArray());
        var day = sched.Days[_selectedDay];
        _hourWheel.Value = day.Hour;
        _minuteWheel.Value = day.Minute;
        _quickStrip.BootDailySame = _cfg.BootDailySame;
        _quickStrip.RestartDailySame = _cfg.RestartDailySame;
        _quickStrip.HibernateDailySame = _cfg.HibernateDailySame;
        _quickStrip.ShutdownDailySame = _cfg.ShutdownDailySame;
        _loading = false;
        UpdateEnabledStates();
        Invalidate();
    }

    private void LoadDayIntoValueWheels()
    {
        _loading = true;
        var sched = _cfg.GetSchedule(_editIndex);
        var day = sched.Days[_selectedDay];
        _hourWheel.Value = day.Hour;
        _minuteWheel.Value = day.Minute;
        _loading = false;
    }

    private void OnTimeEdited()
    {
        if (_loading) return;
        var sched = _cfg.GetSchedule(_editIndex);
        var day = sched.Days[_selectedDay];
        day.Hour = _hourWheel.Value;
        day.Minute = _minuteWheel.Value;
        if (_cfg.GetDailySame(_editIndex))
        {
            foreach (var d in sched.Days)
            {
                d.Hour = day.Hour;
                d.Minute = day.Minute;
            }
        }
        _cfg.ApplyLinkInvariants();

        _loading = true;
        var cur = sched.Days[_selectedDay];
        _hourWheel.Value = cur.Hour;
        _minuteWheel.Value = cur.Minute;
        _loading = false;

        ScheduleSave();
        SyncScheduleAsync();
        Invalidate();
    }

    private void UpdateEnabledStates()
    {
        var master = _cfg.MasterEnabled;
        _restartToggle.Enabled = master;
        _hibernateToggle.Enabled = master;
        _wakeToggle.Enabled = master && _cfg.HibernateEnabled;
        _shutdownToggle.Enabled = master;
        _surfToggle.Enabled = master;
        _hourWheel.Enabled = true;
        _minuteWheel.Enabled = true;
        Invalidate();
    }

    private void OpenQuickPopup()
    {
        try
        {
            if (_popup != null && !_popup.IsDisposed) { _popup.Close(); _popup = null; }
            var popup = new QuickPopup(_cfg.RestartDailySame, _cfg.HibernateDailySame, _cfg.BootDailySame, _cfg.ShutdownDailySame);
            popup.RestartDailySameChanged += on => OnDailySameChanged(0, on);
            popup.HibernateDailySameChanged += on => OnDailySameChanged(1, on);
            popup.BootDailySameChanged += on => OnDailySameChanged(2, on);
            popup.ShutdownDailySameChanged += on => OnDailySameChanged(3, on);
            popup.FormClosed += (_, _) => { if (ReferenceEquals(_popup, popup)) _popup = null; };
            _popup = popup;
            popup.ShowBelow(_quickStrip);
        }
        catch (Exception ex)
        {
            Log.Warn("打开快捷设置失败: " + ex.Message);
        }
    }

    private void OpenPowerPopup()
    {
        try
        {
            if (_powerPopup != null && !_powerPopup.IsDisposed) { _powerPopup.Close(); _powerPopup = null; }
            var popup = new OptionsPopup(
                "关机选项",
                new[] { "关机倒计时提醒", "在线时不关机" },
                new[] { "关机前 1 分钟弹窗，可取消", "最近 3 分钟有键鼠操作则跳过" },
                new[] { _cfg.ShutdownCountdown, _cfg.OnlineNoShutdown },
                "仅对定时关机生效");
            popup.OptionChanged += OnPowerOptionChanged;
            popup.FormClosed += (_, _) => { if (ReferenceEquals(_powerPopup, popup)) _powerPopup = null; };
            _powerPopup = popup;
            popup.ShowBelow(_powerStrip);
        }
        catch (Exception ex)
        {
            Log.Warn("打开关机选项失败: " + ex.Message);
        }
    }

    private void OnPowerOptionChanged(int index, bool on)
    {
        if (index == 0) _cfg.ShutdownCountdown = on;
        else if (index == 1) _cfg.OnlineNoShutdown = on;
        _powerStrip.Options = new[] { _cfg.ShutdownCountdown, _cfg.OnlineNoShutdown };
        ScheduleSave();
        SyncScheduleAsync();
        SetStatus(index switch
        {
            0 => on ? "已启用：关机前 1 分钟提醒，可取消" : "已关闭：关机前不再提醒",
            _ => on ? "已启用：最近 3 分钟有人操作则跳过关机" : "已关闭：在线时也按时关机"
        });
    }

    private string ActionName(int index) => index switch
    {
        0 => "重启", 1 => "休眠", 2 => "唤醒", _ => "关机"
    };

    private string SummaryText()
    {
        var parts = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var sched = _cfg.GetSchedule(i);
            var day = sched.Days[_selectedDay];
            var enabled = _cfg.MasterEnabled && _cfg.IsActionEnabled(i);
            if (i == 2) enabled = enabled && _cfg.HibernateEnabled;
            var label = _cfg.GetDailySame(i) ? "每天" : DayName(_selectedDay);
            parts.Add(enabled ? $"{ActionName(i)} {label} {day.TimeText}" : $"{ActionName(i)} —");
        }
        return string.Join(" · ", parts);
    }

    private void OnDailySameChanged(int index, bool on)
    {
        if (on)
        {
            var sched = _cfg.GetSchedule(index);
            var baseDay = sched.Days[_selectedDay];
            foreach (var d in sched.Days)
            {
                d.Hour = baseDay.Hour;
                d.Minute = baseDay.Minute;
            }
        }
        _cfg.SetDailySame(index, on);
        RefreshScheduleUi();
        ScheduleSave();
        SyncScheduleAsync();
        SetStatus(on ? $"已启用：7 天{ActionName(index)}时间一致" : $"已关闭：各天{ActionName(index)}时间可分别设置");
    }

    private void ScheduleSave()
    {
        if (_preview) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        if (_preview) return;
        try
        {
            _cfg.MasterEnabled = _masterToggle.Checked;
            _cfg.RestartEnabled = _restartToggle.Checked;
            _cfg.BootEnabled = _wakeToggle.Checked;
            _cfg.HibernateEnabled = _hibernateToggle.Checked;
            _cfg.ShutdownEnabled = _shutdownToggle.Checked;
            _cfg.SurfCoreAutoStart = _surfToggle.Checked;
            _cfg.QqNumber = _qqBox.Text.Trim();
            _cfg.SetSnowLumaPassword(_passwordBox.Text);
            ConfigStore.Save(_cfg);
        }
        catch (Exception ex)
        {
            Log.Warn("保存配置失败: " + ex.Message);
        }
    }

    private async void SyncScheduleAsync(bool force = true)
    {
        if (_preview) return;
        if (!force && !IsElevated())
        {
            if (Scheduler.TasksMatch(_cfg))
            {
                SetTaskStatus(_cfg.MasterEnabled ? "计划任务 · 已同步" : "计划任务 · 未启用", false);
                return;
            }
        }
        if (_syncBusy) { _syncAgain = true; return; }
        _syncBusy = true;
        try
        {
            do
            {
                _syncAgain = false;
                var snapshot = _cfg.DeepClone();
                SetTaskStatus("计划任务 · 同步中…", false);
                var result = await Task.Run(() => Scheduler.Apply(snapshot));
                if (IsDisposed) return;
                if (result.Success)
                {
                    _syncErrorShown = false;
                    var active = result.RestartActive || result.HibernateActive || result.WakeActive || result.ShutdownActive;
                    SetTaskStatus(active ? "计划任务 · 已同步" : "计划任务 · 未启用", false);
                }
                else
                {
                    ShowSyncError(result.Message);
                }
            } while (_syncAgain);
        }
        catch (Exception ex)
        {
            Log.Error("同步计划任务失败", ex);
            ShowSyncError(ex.Message);
        }
        finally
        {
            _syncBusy = false;
        }
    }

    private void ShowSyncError(string details)
    {
        details = (details ?? "").Trim();
        if (details.Length == 0) details = "未知错误";
        if (details.Length > 900) details = details.Substring(0, 900) + "…";

        SetTaskStatus("计划任务 · 未生效", true);
        SetStatus("计划任务没有生效，定时开关机暂不可用", true);
        Log.Warn("计划任务同步失败: " + details);

        if (_syncErrorShown) return;
        _syncErrorShown = true;

        var text = "Windows 计划任务没有更新成功，因此定时重启/休眠/唤醒/关机暂时不会生效。\n\n" +
                   "常见原因：\n" +
                   "· 当前账户没有创建计划任务的权限\n" +
                   "· 安全软件拦截了计划任务写入\n" +
                   "· Windows 任务计划服务未正常运行\n\n" +
                   "详细信息：" + details;
        if (!Program.ElevatedLaunch)
        {
            text += "\n\n是否以管理员身份重新启动本程序再试？";
            var r = MessageBox.Show(this, text, "计划任务没有生效",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (r == DialogResult.Yes) RelaunchElevated();
        }
        else
        {
            text += "\n\n本程序已经用管理员权限重启过仍然失败，请检查 Windows 任务计划服务或安全软件（尤其是“受控文件夹访问/勒索软件防护”）。";
            MessageBox.Show(this, text, "计划任务没有生效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RunLaunchChainAsync(bool silent)
    {
        if (_launching) return;
        _launching = true;
        _launchButton.Enabled = false;
        _launchButton.Text = "启动中…";
        try
        {
            var snapshot = _cfg.DeepClone();
            SetStatus("正在启动 DeepSeek Harness / SurfCore…");
            var result = await Task.Run(() => LaunchService.RunAsync(snapshot, ReportStatus));
            SetStatus(result.Success ? result.Message : result.Message, !result.Success);
            if (!result.Success && !silent)
                MessageBox.Show(this, result.Message, "SurfCore Auto-start", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            Log.Error("启动链失败", ex);
            SetStatus("启动失败：" + ex.Message, true);
        }
        finally
        {
            _launching = false;
            _launchButton.Enabled = true;
            _launchButton.Text = "立即启动";
        }
    }

    private void ReportStatus(string message)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(new Action(() => ReportStatus(message)));
            else SetStatus(message);
        }
        catch { }
    }

    private void SetStatus(string message, bool error = false)
    {
        _status = message ?? "";
        _statusError = error;
        _statusAt = DateTime.Now;
        Invalidate(R(84, _expanded ? 586 : 500, 284, 44));
    }

    private void SetTaskStatus(string message, bool error)
    {
        _taskStatus = message;
        _taskError = error;
        Invalidate(R(294, _expanded ? 586 : 500, 290, 44));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Bg);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Theme.Prepare(g);
        var s = Sc;

        DrawTitle(g, s);

        Ui.DrawCard(g, R(24, 52, 240, 72));
        Ui.DrawCard(g, R(272, 52, 248, 30));
        Ui.DrawCard(g, R(272, 88, 168, 34));
        Ui.DrawCard(g, R(444, 88, 76, 34));
        Theme.DrawText(g, "总开关", Theme.Font11B, Theme.Text, R(94, 62, 158, 22));
        Theme.DrawText(g, "每周定时计划", Theme.Font8_5, Theme.SubText, R(94, 86, 158, 18));

        Theme.DrawText(g, "重启", Theme.Font9_5, Theme.Text, R(320, 57, 58, 18));
        Theme.DrawText(g, _cfg.Restart.Days[_selectedDay].TimeText, Theme.Font8_5,
            _cfg.RestartEnabled && _cfg.MasterEnabled ? Theme.Text : Theme.Muted, R(392, 57, 116, 18), StringAlignment.Far);

        using (var pen = new Pen(Theme.CardBorder, 1f * s))
            g.DrawLine(pen, L(356), L(94), L(356), L(116));
        Theme.DrawText(g, "唤醒", Theme.Font9, Theme.Text, R(306, 89, 44, 16));
        Theme.DrawText(g, _cfg.Boot.Days[_selectedDay].TimeText, Theme.Font8,
            _cfg.BootEnabled && _cfg.HibernateEnabled && _cfg.MasterEnabled ? Theme.SubText : Theme.Muted,
            R(306, 105, 44, 13));
        Theme.DrawText(g, "休眠", Theme.Font9, Theme.Text, R(386, 89, 44, 16));
        Theme.DrawText(g, _cfg.Hibernate.Days[_selectedDay].TimeText, Theme.Font8,
            _cfg.HibernateEnabled && _cfg.MasterEnabled ? Theme.SubText : Theme.Muted,
            R(386, 105, 44, 13));

        Theme.DrawText(g, "关机", Theme.Font9, Theme.Text, R(478, 89, 40, 16));
        Theme.DrawText(g, _cfg.Shutdown.Days[_selectedDay].TimeText, Theme.Font8,
            _cfg.ShutdownEnabled && _cfg.MasterEnabled ? Theme.SubText : Theme.Muted,
            R(478, 105, 40, 13));

        Ui.DrawCard(g, R(24, 134, 552, 282));
        var sched = _cfg.GetSchedule(_editIndex);
        var current = sched.Days[_selectedDay];
        Theme.DrawText(g, $"{DayName(_selectedDay)} · {current.TimeText}", Theme.Font9, Theme.SubText,
            R(292, 152, 266, 20), StringAlignment.Far);
        Theme.DrawText(g, "星期", Theme.Font8, Theme.Muted, R(50, 186, 120, 14));
        Theme.DrawText(g, "小时", Theme.Font8, Theme.Muted, R(262, 186, 116, 14), StringAlignment.Center);
        Theme.DrawText(g, "分钟", Theme.Font8, Theme.Muted, R(388, 186, 116, 14), StringAlignment.Center);

        var summary = SummaryText();
        Theme.DrawText(g, summary, Theme.Font8, Theme.Text, R(42, 362, 396, 20));
        Theme.DrawText(g, "滚轮选择 · 左键确定", Theme.Font8, Theme.Muted, R(438, 362, 120, 20), StringAlignment.Far);

        var cardH = _expanded ? 148 : 62;
        Ui.DrawCard(g, R(24, 426, 552, cardH));
        Theme.DrawText(g, "SurfCore 自启动", Theme.Font10B, Theme.Text, R(92, 431, 240, 20));
        Theme.DrawText(g, "启动本程序时拉起 DSH · SurfCore · 控制台", Theme.Font8_5, Theme.SubText, R(92, 452, 360, 18));
        if (_expanded)
        {
            using var pen = new Pen(Theme.CardBorder, 1f * s);
            g.DrawLine(pen, L(42), L(488), L(558), L(488));
            Theme.DrawText(g, "控制台自动登录，并按 QQ 号注入对应进程", Theme.Font8, Theme.Muted, R(42, 540, 500, 16));
            if (_surfToggle.Checked)
                Theme.DrawText(g, "提示：请设置 QQ 开机自启与自动登录，以保证运行", Theme.Font8_5, Theme.Warn, R(42, 556, 510, 18));
        }

        DrawStatus(g, s);
    }

    private void DrawTitle(Graphics g, float s)
    {
        if (_appIcon != null)
        {
            g.DrawIcon(_appIcon, R(20, 10, 24, 24));
        }
        else
        {
            Theme.FillRound(g, R(20, 10, 24, 24), 7f * s, Theme.Accent);
            using var pen = new Pen(Color.White, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawEllipse(pen, R(26, 16, 12, 12));
            g.DrawLine(pen, L(32), L(19), L(32), L(22));
            g.DrawLine(pen, L(32), L(22), L(35), L(23));
        }
        Theme.DrawText(g, "SurfCore Auto-start", Theme.Font11B, Theme.Text, R(52, 11, 176, 22));
        Theme.DrawText(g, "v0.1.2", Theme.Font8, Theme.Muted, R(226, 15, 48, 16));
    }

    private void DrawStatus(Graphics g, float s)
    {
        var y = _expanded ? 594f : 508f;
        Theme.DrawText(g, _status, Theme.Font8_5, _statusError ? Theme.Danger : Theme.SubText, R(88, y, 278, 20));
        Theme.DrawText(g, _taskStatus, Theme.Font8, _taskError ? Theme.Danger : Theme.Muted, R(370, y, 206, 20), StringAlignment.Far);
    }

    private static string DayName(int index) => index switch
    {
        0 => "周一", 1 => "周二", 2 => "周三", 3 => "周四", 4 => "周五", 5 => "周六", _ => "周日"
    };

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && e.Y < L(44))
        {
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }
        base.OnMouseDown(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _saveTimer.Stop();
        _statusTimer.Stop();
        if (!_deleting) SaveNow();
        try { _popup?.Close(); } catch { }
        try { _powerPopup?.Close(); } catch { }
        base.OnFormClosing(e);
    }
}
