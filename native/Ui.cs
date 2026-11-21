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
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Color Fill { get; set; } = Ui.White;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal Color Foreground { get; set; } = Ui.Ink;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal string Subtitle { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal string IconName { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool Compact { get; set; }
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
        var rectangle = new RectangleF(1, 1, Width - 3, Height - 3);
        using var shape = Ui.Rounded(rectangle, Compact ? 12 : 17);
        Color fill = hovering ? ControlPaint.Light(Fill, .08f) : Fill;
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, shape);
        if (Fill == Ui.White) { using var pen = new Pen(Color.FromArgb(225, 229, 234), 1); g.DrawPath(pen, shape); }
        int textX = IconName.Length > 0 ? 59 : 18;
        if (IconName.Length > 0) DrawIcon(g, IconName, new Rectangle(19, Height / 2 - 12, 24, 24));
        using var font = new Font("Segoe UI", Compact ? 10.5f : 11.5f, FontStyle.Bold);
        using var ink = new SolidBrush(Foreground);
        int titleY = Subtitle.Length > 0 ? 11 : (Height - font.Height) / 2;
        g.DrawString(Text, font, ink, new RectangleF(textX, titleY, Width - textX - 12, font.Height + 4));
        if (Subtitle.Length > 0)
        {
            using var secondaryFont = new Font("Segoe UI", 8.5f);
            using var secondaryInk = new SolidBrush(Color.FromArgb(210, Foreground));
            g.DrawString(Subtitle, secondaryFont, secondaryInk, new RectangleF(textX, titleY + 25, Width - textX - 12, Height - titleY - 28));
        }
        if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(Rectangle.Round(rectangle), -5, -5));
    }

    private void DrawIcon(Graphics g, string icon, Rectangle area)
    {
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
        else if (icon == "download")
        {
            g.DrawLine(pen, area.X + 12, area.Y + 4, area.X + 12, area.Y + 17);
            g.DrawLine(pen, area.X + 7, area.Y + 12, area.X + 12, area.Y + 17);
            g.DrawLine(pen, area.X + 17, area.Y + 12, area.X + 12, area.Y + 17);
            g.DrawLine(pen, area.X + 4, area.Y + 21, area.X + 20, area.Y + 21);
        }
    }
}

internal sealed class DropTarget : Control
{
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
        var rectangle = new RectangleF(2, 2, Width - 5, Height - 5);
        using var shape = Ui.Rounded(rectangle, 17);
        using var white = new SolidBrush(Ui.White);
        using var border = new Pen(Color.FromArgb(180, 186, 190), 2) { DashStyle = DashStyle.Dash };
        g.FillPath(white, shape);
        g.DrawPath(border, shape);
        using var font = new Font("Segoe UI", 9.5f);
        TextRenderer.DrawText(g, "Drag files here, or choose them from your laptop.", font, Rectangle.Round(rectangle), Ui.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }
}
