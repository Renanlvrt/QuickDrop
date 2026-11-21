using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace QuickDrop;

internal static class Ui
{
    internal static readonly Color Paper = Color.FromArgb(244, 245, 248);
    internal static readonly Color Ink = Color.FromArgb(44, 44, 46);
    internal static readonly Color Sage = Color.FromArgb(181, 201, 185);
    internal static readonly Color Ultra = Color.FromArgb(153, 168, 212);
    internal static readonly Color White = Color.White;
    internal static readonly Color Muted = Color.FromArgb(116, 116, 118);

    internal static GraphicsPath Rounded(RectangleF rectangle, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class TileButton : Control
{
    private float ScaleFactor => DeviceDpi / 96f;
    private int P(float value) => (int)Math.Round(value * ScaleFactor);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Color Fill { get; set; } = Ui.White;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Color Foreground { get; set; } = Ui.Ink;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal string Subtitle { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal string IconName { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool Compact { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool Centered { get; set; }
    private bool hovering;

    public TileButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        Cursor = Cursors.Hand;
        Height = 76;
    }

    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new RectangleF(P(1), P(1), Width - P(3), Height - P(3));
        using var shape = Ui.Rounded(rectangle, P(Compact ? 12 : 17));
        Color fill = hovering ? ControlPaint.Light(Fill, .08f) : Fill;
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, shape);
        int textX = P(IconName.Length > 0 ? (Centered ? 0 : Compact ? 47 : 60) : (Centered ? 0 : Compact ? 14 : 20));
        using var font = new Font("Segoe UI Variable Text", Compact ? 10.5f : 11f, FontStyle.Bold);
        using var ink = new SolidBrush(Foreground);
        int titleY = Subtitle.Length > 0 ? P(11) : (Height - font.Height) / 2;
        if (Centered && IconName.Length > 0)
        {
            var textSize = g.MeasureString(Text, font);
            int groupWidth = P(28) + (int)Math.Ceiling(textSize.Width);
            int iconX = Math.Max(P(5), (Width - groupWidth) / 2);
            DrawIcon(g, IconName, new Rectangle(iconX, (Height - P(20)) / 2, P(20), P(20)));
            textX = iconX + P(28);
        }
        else if (IconName.Length > 0) DrawIcon(g, IconName, new Rectangle(P(Compact ? 14 : 20), Height / 2 - P(12), P(24), P(24)));
        var textArea = new RectangleF(textX, titleY, Width - textX - P(Centered ? 0 : 12), font.Height + P(4));
        if (Centered && IconName.Length == 0) textArea = new RectangleF(0, titleY, Width, font.Height + P(4));
        using var format = new StringFormat { Alignment = Centered && IconName.Length == 0 ? StringAlignment.Center : StringAlignment.Near };
        g.DrawString(Text, font, ink, textArea, format);
        if (Subtitle.Length > 0)
        {
            using var secondaryFont = new Font("Segoe UI", 8.5f);
            using var secondaryInk = new SolidBrush(Ui.Muted);
            g.DrawString(Subtitle, secondaryFont, secondaryInk, new RectangleF(textX, titleY + P(25), Width - textX - P(12), Height - titleY - P(28)));
        }
        if (Focused) { using var focus = new Pen(Ui.Ink, P(2)) { DashStyle = DashStyle.Dot }; g.DrawPath(focus, shape); }
    }

    private void DrawIcon(Graphics g, string icon, Rectangle area)
    {
        var state = g.Save();
        g.TranslateTransform(area.X, area.Y);
        g.ScaleTransform(area.Width / 24f, area.Height / 24f);
        area = new Rectangle(0, 0, 24, 24);
        using var pen = new Pen(Foreground, 2.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (icon == "phone")
        {
            g.DrawRectangle(pen, area.X + 6, area.Y + 1, 12, 22);
            g.DrawLine(pen, area.X + 10, area.Y + 19, area.X + 14, area.Y + 19);
        }
        else if (icon == "laptop")
        {
            g.DrawRectangle(pen, area.X + 3, area.Y + 3, 18, 14);
            g.DrawLine(pen, area.X, area.Y + 21, area.X + 24, area.Y + 21);
        }
        else if (icon == "upload")
        {
            g.DrawLine(pen, area.X + 12, area.Y + 4, area.X + 12, area.Y + 17);
            g.DrawLine(pen, area.X + 7, area.Y + 9, area.X + 12, area.Y + 4);
            g.DrawLine(pen, area.X + 17, area.Y + 9, area.X + 12, area.Y + 4);
            g.DrawLine(pen, area.X + 4, area.Y + 19, area.X + 20, area.Y + 19);
        }
        else if (icon == "back")
        {
            g.DrawLine(pen, area.X + 18, area.Y + 12, area.X + 5, area.Y + 12);
            g.DrawLine(pen, area.X + 5, area.Y + 12, area.X + 11, area.Y + 6);
            g.DrawLine(pen, area.X + 5, area.Y + 12, area.X + 11, area.Y + 18);
        }
        else if (icon == "download")
        {
            g.DrawLine(pen, area.X + 12, area.Y + 4, area.X + 12, area.Y + 17);
            g.DrawLine(pen, area.X + 7, area.Y + 12, area.X + 12, area.Y + 17);
            g.DrawLine(pen, area.X + 17, area.Y + 12, area.X + 12, area.Y + 17);
            g.DrawLine(pen, area.X + 4, area.Y + 21, area.X + 20, area.Y + 21);
        }
        g.Restore(state);
    }
}

internal sealed class DropTarget : Control
{
    private int P(float value) => (int)Math.Round(value * DeviceDpi / 96f);
    public DropTarget()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 102;
        AllowDrop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new RectangleF(P(2), P(2), Width - P(5), Height - P(5));
        using var shape = Ui.Rounded(rectangle, P(17));
        using var white = new SolidBrush(Ui.White);
        using var border = new Pen(Color.FromArgb(180, 186, 190), P(2)) { DashStyle = DashStyle.Dash };
        g.FillPath(white, shape);
        g.DrawPath(border, shape);
        using var font = new Font("Segoe UI", 9.5f);
        TextRenderer.DrawText(g, "Drag files here, or choose them from your laptop.", font, Rectangle.Round(rectangle), Ui.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }
}

internal sealed class QrCard : Control
{
    private int P(float value) => (int)Math.Round(value * DeviceDpi / 96f);
    private readonly Bitmap qr;

    public QrCard(Bitmap qr)
    {
        this.qr = qr;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(256, 256);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) qr.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Ui.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), P(12));
        using var background = new SolidBrush(Ui.White);
        g.FillPath(background, shape);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(qr, new Rectangle(P(8), P(8), Width - P(16), Height - P(16)));
    }
}
