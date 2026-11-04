using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SurfCoreAutoStart;

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(243, 246, 248);
    public static readonly Color Card = Color.White;
    public static readonly Color CardBorder = Color.FromArgb(226, 232, 240);
    public static readonly Color CardBorderStrong = Color.FromArgb(203, 213, 225);
    public static readonly Color Text = Color.FromArgb(31, 41, 55);
    public static readonly Color SubText = Color.FromArgb(107, 114, 128);
    public static readonly Color Muted = Color.FromArgb(148, 163, 184);
    public static readonly Color Accent = Color.FromArgb(13, 116, 144);
    public static readonly Color AccentDark = Color.FromArgb(8, 90, 113);
    public static readonly Color AccentSoft = Color.FromArgb(226, 244, 246);
    public static readonly Color AccentSoft2 = Color.FromArgb(207, 236, 240);
    public static readonly Color TrackOff = Color.FromArgb(203, 213, 225);
    public static readonly Color Danger = Color.FromArgb(190, 18, 60);
    public static readonly Color Warn = Color.FromArgb(180, 83, 9);

    public static readonly Font Font8 = new("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Font8_5 = new("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Font9 = new("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Font9_5 = new("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Font10 = new("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Font10B = new("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Font11 = new("Microsoft YaHei UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Font11B = new("Microsoft YaHei UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Font12B = new("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Font13B = new("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Font14B = new("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Font16B = new("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point);

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0) return p;
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color color)
    {
        using var p = Rounded(r, radius);
        using var b = new SolidBrush(color);
        g.FillPath(b, p);
    }

    public static void DrawRound(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
    {
        using var p = Rounded(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, p);
    }

    public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect,
        StringAlignment align = StringAlignment.Near, StringAlignment line = StringAlignment.Center)
    {
        using var b = new SolidBrush(color);
        using var sf = new StringFormat { Alignment = align, LineAlignment = line, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, font, b, rect, sf);
    }

    public static void DrawCheck(Graphics g, RectangleF box, bool on)
    {
        if (on)
        {
            FillRound(g, box, 4, Accent);
            using var pen = new Pen(Color.White, Math.Max(1.6f, box.Width / 7f));
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawLines(pen, new[]
            {
                new PointF(box.X + box.Width * 0.25f, box.Y + box.Height * 0.52f),
                new PointF(box.X + box.Width * 0.44f, box.Y + box.Height * 0.72f),
                new PointF(box.X + box.Width * 0.77f, box.Y + box.Height * 0.30f)
            });
        }
        else
        {
            DrawRound(g, box, 4, CardBorderStrong, 1.1f);
        }
    }
}
