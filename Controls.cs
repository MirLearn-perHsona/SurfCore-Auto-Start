using System.Drawing.Drawing2D;

namespace SurfCoreAutoStart;

internal static class Ui
{
    public static float S(Control c) => c.DeviceDpi / 96f;

    public static void DrawCard(Graphics g, RectangleF r, float radius = 12f, bool strong = false)
    {
        Theme.FillRound(g, r, radius, Theme.Card);
        Theme.DrawRound(g, r, radius, strong ? Theme.CardBorderStrong : Theme.CardBorder, 1f);
    }
}

internal abstract class ThemedControl : Control
{
    protected ThemedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    public Color Surface { get; set; } = Theme.Bg;
    protected float Dpi => DeviceDpi / 96f;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Surface);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Invalidate();
    }
}

internal sealed class ToggleSwitch : ThemedControl
{
    private bool _checked;
    private bool _hover;

    public ToggleSwitch()
    {
        Cursor = Cursors.Hand;
        Size = new Size(44, 24);
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler CheckedChanged;

    protected override void OnClick(EventArgs e)
    {
        if (!Enabled) return;
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        var h = Height;
        var track = new RectangleF(0, (Height - h * 0.82f) / 2f, Width, h * 0.82f);
        var trackColor = !Enabled
            ? Color.FromArgb(226, 232, 240)
            : _checked ? (_hover ? Theme.AccentDark : Theme.Accent) : (_hover ? Color.FromArgb(186, 199, 214) : Theme.TrackOff);
        Theme.FillRound(g, track, track.Height / 2f, trackColor);

        var knob = track.Height - 4f * Dpi;
        var kx = _checked ? track.Right - knob - 2f * Dpi : track.Left + 2f * Dpi;
        var ky = track.Top + 2f * Dpi;
        using (var shadow = new SolidBrush(Color.FromArgb(38, 15, 23, 42)))
            g.FillEllipse(shadow, kx, ky + 1f * Dpi, knob, knob);
        using (var b = new SolidBrush(Enabled ? Color.White : Color.FromArgb(248, 250, 252)))
            g.FillEllipse(b, kx, ky, knob, knob);
    }
}

internal enum FlatButtonStyle { Primary, Soft, Ghost, Danger }

internal sealed class FlatButton : ThemedControl
{
    private bool _hover;
    private bool _down;

    public FlatButton()
    {
        Cursor = Cursors.Hand;
        Size = new Size(96, 30);
    }

    public FlatButtonStyle Style { get; set; } = FlatButtonStyle.Primary;
    public float Radius { get; set; } = 8f;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Color fill, border, text;
        switch (Style)
        {
            case FlatButtonStyle.Primary:
                fill = _down ? Theme.AccentDark : _hover ? Color.FromArgb(11, 101, 126) : Theme.Accent;
                border = fill;
                text = Color.White;
                break;
            case FlatButtonStyle.Soft:
                fill = _down ? Theme.AccentSoft2 : _hover ? Theme.AccentSoft2 : Theme.AccentSoft;
                border = Theme.AccentSoft2;
                text = Theme.AccentDark;
                break;
            case FlatButtonStyle.Danger:
                fill = _down ? Color.FromArgb(159, 18, 57) : _hover ? Theme.Danger : Color.FromArgb(225, 29, 72);
                border = fill;
                text = Color.White;
                break;
            default:
                fill = _down ? Color.FromArgb(241, 245, 249) : _hover ? Color.FromArgb(248, 250, 252) : Color.Transparent;
                border = _hover ? Theme.CardBorder : Color.Transparent;
                text = Theme.SubText;
                break;
        }
        if (fill != Color.Transparent) Theme.FillRound(g, r, Radius, fill);
        if (border != Color.Transparent) Theme.DrawRound(g, r, Radius, border, 1f);
        Theme.DrawText(g, Text, Theme.Font9_5, Enabled ? text : Theme.Muted, r,
            StringAlignment.Center, StringAlignment.Center);
    }
}

internal enum IconKind { Minimize, Close }

internal sealed class IconButton : ThemedControl
{
    private bool _hover;

    public IconButton()
    {
        Cursor = Cursors.Hand;
        Size = new Size(34, 28);
    }

    public IconKind Kind { get; set; }
    public Color HoverColor { get; set; } = Color.FromArgb(226, 232, 240);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        if (_hover)
        {
            var color = Kind == IconKind.Close ? Color.FromArgb(254, 226, 226) : HoverColor;
            Theme.FillRound(g, new RectangleF(2, 2, Width - 4, Height - 4), 7f * Dpi, color);
        }
        var penColor = Kind == IconKind.Close && _hover ? Theme.Danger : Theme.SubText;
        using var pen = new Pen(penColor, 1.5f * Dpi) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var cx = Width / 2f;
        var cy = Height / 2f;
        var d = 5f * Dpi;
        if (Kind == IconKind.Minimize)
        {
            g.DrawLine(pen, cx - d, cy + 0.5f, cx + d, cy + 0.5f);
        }
        else
        {
            g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
            g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);
        }
    }
}

internal sealed class TabSwitch : ThemedControl
{
    private int _selected;
    private int _hover = -1;

    public TabSwitch()
    {
        Cursor = Cursors.Hand;
        Size = new Size(168, 28);
    }

    public string[] Items { get; set; } = Array.Empty<string>();
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var v = Math.Clamp(value, 0, Math.Max(0, Items.Length - 1));
            if (_selected == v) return;
            _selected = v;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler SelectionChanged;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var idx = Hit(e.X);
        if (idx != _hover) { _hover = idx; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            var idx = Hit(e.X);
            if (idx >= 0) SelectedIndex = idx;
        }
        base.OnMouseDown(e);
    }

    private int Hit(int x)
    {
        if (Items.Length == 0) return -1;
        var w = Width / (float)Items.Length;
        var idx = (int)(x / w);
        return idx >= 0 && idx < Items.Length ? idx : -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, r, Height / 2f, Color.FromArgb(241, 245, 249));
        if (Items.Length == 0) return;
        var w = Width / (float)Items.Length;
        for (var i = 0; i < Items.Length; i++)
        {
            var cell = new RectangleF(i * w + 2f * Dpi, 2f * Dpi, w - 4f * Dpi, Height - 4f * Dpi);
            if (i == _selected)
            {
                using (var sh = new SolidBrush(Color.FromArgb(20, 15, 23, 42)))
                    Theme.FillRound(g, new RectangleF(cell.X, cell.Y + 1f * Dpi, cell.Width, cell.Height), (Height - 4f * Dpi) / 2f, Color.FromArgb(20, 15, 23, 42));
                Theme.FillRound(g, cell, (Height - 4f * Dpi) / 2f, Color.White);
                Theme.DrawRound(g, cell, (Height - 4f * Dpi) / 2f, Theme.CardBorder, 1f);
            }
            var color = i == _selected ? Theme.AccentDark : (_hover == i ? Theme.Text : Theme.SubText);
            Theme.DrawText(g, Items[i], i == _selected ? Theme.Font9_5 : Theme.Font9, color, cell,
                StringAlignment.Center, StringAlignment.Center);
        }
    }
}

internal sealed class DayWheel : ThemedControl
{
    private int _selected;
    private readonly bool[] _enabled = new bool[7];

    public DayWheel()
    {
        Cursor = Cursors.Hand;
        Size = new Size(210, 154);
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public string[] DayNames { get; } = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var v = Math.Clamp(value, 0, 6);
            if (v == _selected) return;
            _selected = v;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool GetEnabled(int i) => _enabled[Math.Clamp(i, 0, 6)];

    public void SetAll(bool[] values)
    {
        for (var i = 0; i < 7; i++) _enabled[i] = values != null && i < values.Length && values[i];
        Invalidate();
    }

    public event EventHandler SelectedIndexChanged;
    public event EventHandler DayEnabledChanged;

    private float RowHeight => Height / 7f;

    private int HitRow(int y) => Math.Clamp((int)(y / RowHeight), 0, 6);

    private RectangleF CheckRect(int row)
    {
        var rowH = RowHeight;
        return new RectangleF(Width - 25f * Dpi, row * rowH + (rowH - 13f * Dpi) / 2f, 13f * Dpi, 13f * Dpi);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            var row = HitRow(e.Y);
            if (CheckRect(row).Contains(e.Location))
            {
                if (row != SelectedIndex) SelectedIndex = row;
                _enabled[row] = !_enabled[row];
                Invalidate();
                DayEnabledChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (row != SelectedIndex)
            {
                SelectedIndex = row;
            }
            else
            {
                _enabled[row] = !_enabled[row];
                Invalidate();
                DayEnabledChanged?.Invoke(this, EventArgs.Empty);
            }
            Focus();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        SelectedIndex = (SelectedIndex + (e.Delta > 0 ? -1 : 1) + 7) % 7;
        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up) { SelectedIndex = (SelectedIndex + 6) % 7; e.Handled = true; }
        else if (e.KeyCode == Keys.Down) { SelectedIndex = (SelectedIndex + 1) % 7; e.Handled = true; }
        else if (e.KeyCode == Keys.Space) { _enabled[SelectedIndex] = !_enabled[SelectedIndex]; Invalidate(); DayEnabledChanged?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        var rowH = RowHeight;
        for (var i = 0; i < 7; i++)
        {
            var row = new RectangleF(0, i * rowH + 1.5f * Dpi, Width, rowH - 3f * Dpi);
            var selected = i == SelectedIndex;
            if (selected)
            {
                Theme.FillRound(g, row, 7f * Dpi, Theme.AccentSoft);
                Theme.FillRound(g, new RectangleF(0, row.Y + rowH * 0.22f, 3f * Dpi, rowH * 0.56f), 1.5f * Dpi, Theme.Accent);
            }
            var textColor = !_enabled[i] ? Theme.Muted : selected ? Theme.AccentDark : Theme.Text;
            var font = selected ? Theme.Font10B : Theme.Font9_5;
            Theme.DrawText(g, DayNames[i], font, textColor,
                new RectangleF(14f * Dpi, row.Y, Width - 52f * Dpi, row.Height), StringAlignment.Near);
            var box = CheckRect(i);
            Theme.DrawCheck(g, box, _enabled[i]);
        }
    }
}

internal sealed class ValueWheel : ThemedControl
{
    private int _value;
    private int _hoverRow = int.MinValue;

    public ValueWheel()
    {
        Cursor = Cursors.Hand;
        Size = new Size(116, 154);
    }

    public int Min { get; set; }
    public int Max { get; set; } = 59;
    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, Min, Max);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler ValueChanged;

    private int Count => Max - Min + 1;
    private float RowHeight => Height / 5f;

    private int ValueAtRow(int row)
    {
        var offset = row - 2;
        var v = (_value + offset - Min) % Count;
        if (v < 0) v += Count;
        return v + Min;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!Enabled) { base.OnMouseWheel(e); return; }
        var v = _value + (e.Delta > 0 ? -1 : 1);
        if (v < Min) v = Max;
        if (v > Max) v = Min;
        Value = v;
        base.OnMouseWheel(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var row = Math.Clamp((int)(e.Y / RowHeight), 0, 4);
        if (row != _hoverRow) { _hoverRow = row; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverRow = int.MinValue; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left) Value = ValueAtRow(Math.Clamp((int)(e.Y / RowHeight), 0, 4));
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        var rowH = RowHeight;
        for (var i = 0; i < 5; i++)
        {
            var row = new RectangleF(0, i * rowH + 1.5f * Dpi, Width, rowH - 3f * Dpi);
            var dist = Math.Abs(i - 2);
            if (dist == 0)
            {
                Theme.FillRound(g, row, 8f * Dpi, Theme.AccentSoft);
                Theme.DrawRound(g, row, 8f * Dpi, Theme.AccentSoft2, 1f);
            }
            else if (_hoverRow == i)
            {
                Theme.FillRound(g, row, 8f * Dpi, Color.FromArgb(246, 249, 251));
            }
            var value = ValueAtRow(i);
            var color = dist == 0 ? Theme.AccentDark : dist == 1 ? Theme.SubText : Theme.Muted;
            var font = dist == 0 ? Theme.Font13B : dist == 1 ? Theme.Font10B : Theme.Font9;
            Theme.DrawText(g, value.ToString("00"), font, color, row, StringAlignment.Center, StringAlignment.Center);
        }
    }
}

internal sealed class QuickStrip : ThemedControl
{
    private bool _hover;

    public QuickStrip()
    {
        Cursor = Cursors.Hand;
        Size = new Size(44, 154);
    }

    public bool BootDailySame { get; set; }
    public bool RestartDailySame { get; set; }
    public bool HibernateDailySame { get; set; }
    public bool ShutdownDailySame { get; set; }

    public event EventHandler OpenRequested;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Theme.FillRound(g, r, 9f * Dpi, _hover ? Theme.AccentSoft : Color.FromArgb(248, 250, 252));
        Theme.DrawRound(g, r, 9f * Dpi, _hover ? Theme.AccentSoft2 : Theme.CardBorder, 1f);
        var chars = "快捷设置".ToCharArray();
        var font = Theme.Font8_5;
        var charH = 19f * Dpi;
        var total = chars.Length * charH;
        var startY = (Height - total) / 2f - 8f * Dpi;
        for (var i = 0; i < chars.Length; i++)
        {
            Theme.DrawText(g, chars[i].ToString(), font, _hover ? Theme.AccentDark : Theme.SubText,
                new RectangleF(0, startY + i * charH, Width, charH), StringAlignment.Center, StringAlignment.Center);
        }
        var dotY = Height - 14f * Dpi;
        var states = new[] { RestartDailySame, HibernateDailySame, BootDailySame, ShutdownDailySame };
        var step = 7.5f * Dpi;
        var startX = Width / 2f - (states.Length - 1) * step / 2f - 2f * Dpi;
        for (var i = 0; i < states.Length; i++)
        {
            var dot = new RectangleF(startX + i * step, dotY, 4f * Dpi, 4f * Dpi);
            using var b = new SolidBrush(states[i] ? Theme.Accent : Theme.CardBorderStrong);
            g.FillEllipse(b, dot);
        }
    }
}

internal sealed class OptionStrip : ThemedControl
{
    private bool _hover;

    public OptionStrip()
    {
        Cursor = Cursors.Hand;
        Size = new Size(44, 72);
    }

    public string Label { get; set; } = "关机选项";
    public bool[] Options { get; set; } = new bool[2];
    public event EventHandler OpenRequested;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) OpenRequested?.Invoke(this, EventArgs.Empty);
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Theme.FillRound(g, r, 9f * Dpi, _hover ? Theme.AccentSoft : Color.FromArgb(248, 250, 252));
        Theme.DrawRound(g, r, 9f * Dpi, _hover ? Theme.AccentSoft2 : Theme.CardBorder, 1f);

        var chars = (Label ?? "").ToCharArray();
        var font = Theme.Font8;
        var charH = 15f * Dpi;
        var total = chars.Length * charH;
        var startY = (Height - total) / 2f - 5f * Dpi;
        for (var i = 0; i < chars.Length; i++)
        {
            Theme.DrawText(g, chars[i].ToString(), font, _hover ? Theme.AccentDark : Theme.SubText,
                new RectangleF(0, startY + i * charH, Width, charH), StringAlignment.Center, StringAlignment.Center);
        }

        var count = Options?.Length ?? 0;
        var dotY = Height - 11f * Dpi;
        var dotStart = Width / 2f - (count * 7f - 3f) * Dpi / 2f;
        for (var i = 0; i < count; i++)
        {
            var dot = new RectangleF(dotStart + i * 7f * Dpi, dotY, 3.5f * Dpi, 3.5f * Dpi);
            using var b = new SolidBrush(Options[i] ? Theme.Accent : Theme.CardBorderStrong);
            g.FillEllipse(b, dot);
        }
    }
}

internal sealed class OptionsPopup : Form
{
    private readonly string _title;
    private readonly string[] _titles;
    private readonly string[] _subtitles;
    private readonly bool[] _values;
    private readonly string _hint;
    private int _hoverRow = -1;
    private float DpiS => DeviceDpi / 96f;

    public OptionsPopup(string title, string[] titles, string[] subtitles, bool[] values, string hint = "")
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Card;
        DoubleBuffered = true;
        KeyPreview = true;
        _title = title ?? "";
        _titles = titles ?? Array.Empty<string>();
        _subtitles = subtitles ?? Array.Empty<string>();
        _values = values is null ? new bool[_titles.Length] : (bool[])values.Clone();
        _hint = hint ?? "";
        var s = DpiS;
        var h = 44 + _titles.Length * 38 + (_hint.Length > 0 ? 26 : 10);
        ClientSize = new Size((int)(300 * s), (int)(h * s));
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    public event Action<int, bool> OptionChanged;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;
            return cp;
        }
    }

    private RectangleF RowRect(int index)
    {
        var s = DpiS;
        return new RectangleF(10 * s, (38 + index * 38) * s, (300 - 20) * s, 34 * s);
    }

    public void ShowBelow(Control anchor)
    {
        var s = DpiS;
        var screen = Screen.FromControl(anchor).WorkingArea;
        var x = anchor.PointToScreen(new Point(0, anchor.Height + (int)(6 * s))).X;
        var y = anchor.PointToScreen(Point.Empty).Y + anchor.Height + (int)(6 * s);
        x = Math.Min(x, screen.Right - Width - (int)(8 * s));
        y = Math.Min(y, screen.Bottom - Height - (int)(8 * s));
        Location = new Point(Math.Max(screen.Left + 8, x), Math.Max(screen.Top + 8, y));
        Show(anchor.FindForm());
        BringToFront();
        Activate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var row = -1;
        for (var i = 0; i < _titles.Length; i++)
            if (RowRect(i).Contains(e.Location)) { row = i; break; }
        if (row != _hoverRow) { _hoverRow = row; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverRow = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            for (var i = 0; i < _titles.Length; i++)
            {
                if (!RowRect(i).Contains(e.Location)) continue;
                _values[i] = !_values[i];
                Invalidate();
                OptionChanged?.Invoke(i, _values[i]);
                break;
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        var s = DpiS;
        var full = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);
        Theme.FillRound(g, full, 12f * s, Theme.Card);
        Theme.DrawRound(g, full, 12f * s, Theme.CardBorderStrong, 1f);
        Theme.DrawText(g, _title, Theme.Font9, Theme.Muted, new RectangleF(14 * s, 10 * s, 260 * s, 20 * s), StringAlignment.Near);

        for (var i = 0; i < _titles.Length; i++)
        {
            var row = RowRect(i);
            if (_hoverRow == i) Theme.FillRound(g, row, 7f * s, Color.FromArgb(248, 250, 252));
            var box = new RectangleF(row.X + 4 * s, row.Y + (row.Height - 15 * s) / 2f, 15 * s, 15 * s);
            Theme.DrawCheck(g, box, _values[i]);
            var color = _values[i] ? Theme.Text : Theme.SubText;
            Theme.DrawText(g, _titles[i], Theme.Font9_5, color,
                new RectangleF(row.X + 27 * s, row.Y + 1 * s, row.Width - 30 * s, 18 * s), StringAlignment.Near);
            Theme.DrawText(g, _subtitles.Length > i ? _subtitles[i] : "", Theme.Font8, Theme.Muted,
                new RectangleF(row.X + 27 * s, row.Y + 18 * s, row.Width - 30 * s, 14 * s), StringAlignment.Near);
        }

        if (_hint.Length > 0)
            Theme.DrawText(g, _hint, Theme.Font8, Theme.Warn,
                new RectangleF(14 * s, (ClientSize.Height / s - 22) * s, (300 - 28) * s, 18 * s), StringAlignment.Near);
    }
}

internal sealed class InputBox : ThemedControl
{
    private readonly TextBox _box;
    private bool _suppress;
    private bool _focused;

    public new event EventHandler TextChanged;

    public InputBox()
    {
        BackColor = Theme.Card;
        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = Theme.Font9_5,
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = Theme.Text,
            TabStop = true
        };
        _box.TextChanged += (_, _) =>
        {
            if (!_suppress) TextChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        };
        _box.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        _box.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        _box.KeyPress += BoxOnKeyPress;
        Controls.Add(_box);
        Size = new Size(220, 34);
    }

    public bool DigitsOnly { get; set; }
    public int MaxDigits { get; set; } = 0;
    public TextBox InnerBox => _box;

    public new string Text
    {
        get => _box.Text;
        set
        {
            _suppress = true;
            _box.Text = value ?? "";
            _suppress = false;
            Invalidate();
        }
    }

    public string Placeholder
    {
        get => _box.PlaceholderText;
        set => _box.PlaceholderText = value;
    }

    public bool Password
    {
        get => _box.UseSystemPasswordChar;
        set => _box.UseSystemPasswordChar = value;
    }

    public void FocusInput() => _box.Focus();

    private void BoxOnKeyPress(object sender, KeyPressEventArgs e)
    {
        if (DigitsOnly && !char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        if (MaxDigits > 0 && !char.IsControl(e.KeyChar) && _box.Text.Length >= MaxDigits && _box.SelectionLength == 0) e.Handled = true;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_box == null) return;
        var s = Dpi;
        _box.SetBounds((int)(12 * s), (Height - _box.PreferredHeight) / 2, Width - (int)(24 * s), _box.PreferredHeight);
    }

    protected override void OnClick(EventArgs e)
    {
        _box.Focus();
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Theme.FillRound(g, r, 8f * Dpi, Color.FromArgb(248, 250, 252));
        Theme.DrawRound(g, r, 8f * Dpi, _focused ? Theme.Accent : Theme.CardBorderStrong, _focused ? 1.4f * Dpi : 1f);
    }
}

internal sealed class CheckRow : ThemedControl
{
    private bool _checked;
    private bool _hover;

    public CheckRow()
    {
        Cursor = Cursors.Hand;
        Height = 30;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler CheckedChanged;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { Checked = !Checked; Focus(); }
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        if (_hover) Theme.FillRound(g, new RectangleF(0, 0, Width, Height), 7f * Dpi, Color.FromArgb(248, 250, 252));
        var box = new RectangleF(8 * Dpi, (Height - 15 * Dpi) / 2f, 15 * Dpi, 15 * Dpi);
        Theme.DrawCheck(g, box, _checked);
        Theme.DrawText(g, Text, Theme.Font9_5, _checked ? Theme.Text : Theme.SubText,
            new RectangleF(31 * Dpi, 0, Width - 36 * Dpi, Height), StringAlignment.Near);
    }
}

internal sealed class QuickPopup : Form
{
    private bool _restartDailySame;
    private bool _hibernateDailySame;
    private bool _bootDailySame;
    private bool _shutdownDailySame;
    private int _hoverRow = -1;
    private float DpiS => DeviceDpi / 96f;

    public QuickPopup(bool restartDailySame, bool hibernateDailySame, bool bootDailySame, bool shutdownDailySame)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Card;
        DoubleBuffered = true;
        KeyPreview = true;
        _restartDailySame = restartDailySame;
        _hibernateDailySame = hibernateDailySame;
        _bootDailySame = bootDailySame;
        _shutdownDailySame = shutdownDailySame;
        var s = DpiS;
        ClientSize = new Size((int)(236 * s), (int)(224 * s));
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    public event Action<bool> RestartDailySameChanged;
    public event Action<bool> HibernateDailySameChanged;
    public event Action<bool> BootDailySameChanged;
    public event Action<bool> ShutdownDailySameChanged;

    private RectangleF RowRect(int index)
    {
        var s = DpiS;
        return new RectangleF(10 * s, (38 + index * 34) * s, (236 - 20) * s, 30 * s);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;
            return cp;
        }
    }

    public void ShowBelow(Control anchor)
    {
        var s = DpiS;
        var screen = Screen.FromControl(anchor).WorkingArea;
        var x = anchor.PointToScreen(new Point(0, anchor.Height + (int)(6 * s))).X;
        var y = anchor.PointToScreen(Point.Empty).Y + anchor.Height + (int)(6 * s);
        x = Math.Min(x, screen.Right - Width - (int)(8 * s));
        y = Math.Min(y, screen.Bottom - Height - (int)(8 * s));
        Location = new Point(Math.Max(screen.Left + 8, x), Math.Max(screen.Top + 8, y));
        Show(anchor.FindForm());
        BringToFront();
        Activate();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var row = -1;
        for (var i = 0; i < 4; i++)
            if (RowRect(i).Contains(e.Location)) { row = i; break; }
        if (row != _hoverRow) { _hoverRow = row; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hoverRow = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            for (var i = 0; i < 4; i++)
            {
                if (!RowRect(i).Contains(e.Location)) continue;
                switch (i)
                {
                    case 0:
                        _restartDailySame = !_restartDailySame;
                        RestartDailySameChanged?.Invoke(_restartDailySame);
                        break;
                    case 1:
                        _hibernateDailySame = !_hibernateDailySame;
                        HibernateDailySameChanged?.Invoke(_hibernateDailySame);
                        break;
                    case 2:
                        _bootDailySame = !_bootDailySame;
                        BootDailySameChanged?.Invoke(_bootDailySame);
                        break;
                    default:
                        _shutdownDailySame = !_shutdownDailySame;
                        ShutdownDailySameChanged?.Invoke(_shutdownDailySame);
                        break;
                }
                Invalidate();
                break;
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        var s = DpiS;
        var full = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);
        Theme.FillRound(g, full, 12f * s, Theme.Card);
        Theme.DrawRound(g, full, 12f * s, Theme.CardBorderStrong, 1f);
        Theme.DrawText(g, "快捷设置", Theme.Font9, Theme.Muted, new RectangleF(14 * s, 10 * s, 120 * s, 20 * s), StringAlignment.Near);
        DrawRow(g, 0, "重启时间每天一致", "7 天重启使用同一时间");
        DrawRow(g, 1, "休眠时间每天一致", "7 天休眠使用同一时间");
        DrawRow(g, 2, "唤醒时间每天一致", "7 天唤醒使用同一时间");
        DrawRow(g, 3, "关机时间每天一致", "7 天关机使用同一时间");
        Theme.DrawText(g, "提示：同刻可能刚唤醒又休眠/关机", Theme.Font8, Theme.Warn,
            new RectangleF(14 * s, 180 * s, 212 * s, 18 * s), StringAlignment.Near);
    }

    private void DrawRow(Graphics g, int index, string title, string subtitle)
    {
        var s = DpiS;
        var row = RowRect(index);
        if (_hoverRow == index) Theme.FillRound(g, row, 7f * s, Color.FromArgb(248, 250, 252));
        var box = new RectangleF(row.X + 4 * s, row.Y + (row.Height - 15 * s) / 2f, 15 * s, 15 * s);
        var on = index switch { 0 => _restartDailySame, 1 => _hibernateDailySame, 2 => _bootDailySame, _ => _shutdownDailySame };
        Theme.DrawCheck(g, box, on);
        var color = on ? Theme.Text : Theme.SubText;
        Theme.DrawText(g, title, Theme.Font9_5, color, new RectangleF(row.X + 27 * s, row.Y + 2 * s, row.Width - 30 * s, 17 * s), StringAlignment.Near);
        Theme.DrawText(g, subtitle, Theme.Font8, Theme.Muted, new RectangleF(row.X + 27 * s, row.Y + 17 * s, row.Width - 30 * s, 13 * s), StringAlignment.Near);
    }
}