using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SurfCoreAutoStart;

internal sealed class ShutdownGuardForm : Form
{
    private readonly AppConfig _cfg;
    private readonly bool _demo;
    private readonly bool _hibernate;
    private readonly DateTime _shutdownAt;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly FlatButton _cancelButton;
    private readonly FlatButton _nowButton;

    private DateTime _lastHumanInput;
    private bool _countdownShown;
    private bool _skipMode;
    private bool _done;
    private DateTime _closeAt;
    private int _secondsLeft;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private HookProc _keyboardProc;
    private HookProc _mouseProc;

    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const uint LlkhfInjected = 0x10;
    private const uint LlmhfInjected = 0x01;
    private const uint LlmhfLowerIlInjected = 0x02;

    public ShutdownGuardForm(bool demo = false)
    {
        _demo = demo;
        _cfg = ConfigStore.Load();
        _hibernate = !demo && _cfg.ShouldHibernateForBoot;
        if (demo)
        {
            _shutdownAt = DateTime.Now.AddSeconds(9);
            _cfg.ShutdownCountdown = true;
            _cfg.OnlineNoShutdown = false;
        }
        else
        {
            var dayIndex = ((int)DateTime.Today.DayOfWeek + 6) % 7;
            var day = _cfg.Shutdown.Days[dayIndex];
            _shutdownAt = DateTime.Today.AddHours(day.Hour).AddMinutes(day.Minute);
            if (_shutdownAt < DateTime.Now.AddSeconds(-2))
                _shutdownAt = DateTime.Now.AddSeconds(3);
        }

        Text = "SurfCore Auto-start";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Theme.Card;
        DoubleBuffered = true;
        Opacity = 0;
        ClientSize = new Size(400, 200);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        _cancelButton = new FlatButton
        {
            Text = "取消关机", Style = FlatButtonStyle.Soft, Surface = Theme.Card,
            Size = new Size(120, 32), Visible = false
        };
        _nowButton = new FlatButton
        {
            Text = "立即关机", Style = FlatButtonStyle.Danger, Surface = Theme.Card,
            Size = new Size(120, 32), Visible = false
        };
        _cancelButton.Click += (_, _) => CancelShutdown();
        _nowButton.Click += (_, _) => ShutdownNow();
        Controls.Add(_cancelButton);
        Controls.Add(_nowButton);

        _timer = new System.Windows.Forms.Timer { Interval = 200 };
        _timer.Tick += (_, _) => TickGuard();

        if (_cfg.OnlineNoShutdown) InstallHooks();
        _lastHumanInput = GetLastInputTime();
        _timer.Start();
    }

    private float Sc => DeviceDpi / 96f;
    private int L(float v) => (int)Math.Round(v * Sc);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var screen = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + (screen.Height - Height) / 2);
        LayoutButtons();
        if (_demo) ShowCountdown();
    }

    private void LayoutButtons()
    {
        _cancelButton.Bounds = new Rectangle(L(80), L(145), L(120), L(32));
        _nowButton.Bounds = new Rectangle(L(212), L(145), L(120), L(32));
    }

    private void TickGuard()
    {
        if (IsDisposed) return;
        var now = DateTime.Now;

        if (_skipMode)
        {
            if (now >= _closeAt) Close();
            return;
        }
        if (_done) return;

        var remaining = (_shutdownAt - now).TotalSeconds;

        // “在线时不关机”应在真正到点时再看最近 3 分钟有没有输入，
        // 而不是在提前 3 分钟启动 guard 时就立刻跳过。
        if (_cfg.OnlineNoShutdown && remaining <= 0 &&
            (now - _lastHumanInput).TotalSeconds <= 180)
        {
            ShowSkipped();
            return;
        }

        if (_cfg.ShutdownCountdown && remaining <= 60 && !_countdownShown) ShowCountdown();
        if (_countdownShown)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling(remaining));
            if (seconds != _secondsLeft) { _secondsLeft = seconds; Invalidate(); }
        }
        if (remaining <= 0) ShutdownNow();
    }

    private void ShowCountdown()
    {
        if (_countdownShown) return;
        _countdownShown = true;
        _secondsLeft = Math.Max(0, (int)Math.Ceiling((_shutdownAt - DateTime.Now).TotalSeconds));
        Opacity = 1;
        ShowInTaskbar = false;
        _cancelButton.Visible = true;
        _nowButton.Visible = true;
        TopMost = true;
        ShowInTaskbar = true;
        BringToFront();
        Activate();
        Invalidate();
        Log.Info($"关机倒计时开始: {_secondsLeft}s");
    }

    private void ShowSkipped()
    {
        if (_skipMode) return;
        _skipMode = true;
        _closeAt = DateTime.Now.AddSeconds(6);
        Opacity = 1;
        ShowInTaskbar = false;
        _cancelButton.Visible = false;
        _nowButton.Visible = false;
        BringToFront();
        Invalidate();
        Log.Info("检测到最近 3 分钟内有键鼠操作，已跳过本次关机");
    }

    private void CancelShutdown()
    {
        if (_done) return;
        _done = true;
        _timer.Stop();
        Log.Info("用户取消了定时关机");
        Close();
    }

    private void ShutdownNow()
    {
        if (_done) return;
        _done = true;
        _timer.Stop();
        Log.Info(_hibernate ? "执行定时休眠（用于下次定时开机）" : "执行定时关机");
        if (_demo)
        {
            Close();
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", _hibernate ? "/h" : "/s /t 0")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex)
        {
            Log.Error("调用 shutdown.exe 失败", ex);
            MessageBox.Show(this, "无法执行关机：" + ex.Message, "SurfCore Auto-start",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        UninstallHooks();
        base.OnFormClosing(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Card);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Theme.Prepare(g);
        var s = Sc;
        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 14f * s);
        using (var brush = new SolidBrush(Theme.Card)) g.FillPath(brush, path);
        using (var pen = new Pen(_skipMode ? Theme.CardBorderStrong : Theme.AccentSoft2, 1f * s)) g.DrawPath(pen, path);

        if (_skipMode)
        {
            Theme.DrawText(g, "已跳过本次关机", Theme.Font13B, Theme.Text, R(24, 46, 352, 28), StringAlignment.Center);
            Theme.DrawText(g, "检测到最近 3 分钟内有键盘或鼠标操作", Theme.Font9_5, Theme.SubText,
                R(24, 86, 352, 24), StringAlignment.Center);
            Theme.DrawText(g, "为了让电脑保持在线，本次定时关机已取消", Theme.Font8_5, Theme.Muted,
                R(24, 116, 352, 20), StringAlignment.Center);
            return;
        }

        Theme.DrawText(g, "SurfCore Auto-start", Theme.Font9, Theme.Muted, R(24, 16, 200, 18), StringAlignment.Near);
        Theme.DrawText(g, _hibernate ? "定时休眠" : "定时关机", Theme.Font12B, Theme.Text, R(24, 38, 200, 24), StringAlignment.Near);
        Theme.DrawText(g, _secondsLeft.ToString("00"), Theme.Font16B, Theme.AccentDark, R(238, 28, 138, 56), StringAlignment.Center);
        Theme.DrawText(g, _hibernate ? "秒后自动休眠（定时开机）" : "秒后自动关机", Theme.Font9_5, Theme.SubText, R(24, 82, 220, 20), StringAlignment.Near);
        Theme.DrawText(g, "点击“取消关机”可以取消本次关机", Theme.Font8, Theme.Muted, R(24, 106, 352, 18), StringAlignment.Near);
    }

    private Rectangle R(float x, float y, float w, float h) => new(L(x), L(y), L(w), L(h));

    private static DateTime GetLastInputTime()
    {
        try
        {
            var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info)) return DateTime.MinValue;
            var idle = unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0;
            if (idle < 0) idle = 0;
            return DateTime.Now.AddSeconds(-idle);
        }
        catch { return DateTime.MinValue; }
    }

    private void InstallHooks()
    {
        try
        {
            _keyboardProc = KeyboardHookProc;
            _mouseProc = MouseHookProc;
            var module = GetModuleHandle(null);
            _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
            _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
        }
        catch (Exception ex)
        {
            Log.Warn("安装输入监控钩子失败: " + ex.Message);
        }
    }

    private void UninstallHooks()
    {
        try { if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook); } catch { }
        try { if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook); } catch { }
        _keyboardHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                // 只把真实键鼠输入算作“有人在用”；AI/自动化注入的输入带 injected 标记，不计入。
                if ((info.flags & LlkhfInjected) == 0) _lastHumanInput = DateTime.Now;
            }
            catch { }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                if ((info.flags & (LlmhfInjected | LlmhfLowerIlInjected)) == 0) _lastHumanInput = DateTime.Now;
            }
            catch { }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public int x;
        public int y;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);
}
