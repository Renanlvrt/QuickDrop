using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var iconPath = Path.Combine(root, "native", "Assets", "QuickDrop.ico");
var previewPath = Path.Combine(root, "images", "icon.png");
Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);

var sizes = new[] { 16, 32, 48, 256 };
var pngs = new List<byte[]>();
foreach (var size in sizes)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.Clear(Color.Transparent);
    graphics.ScaleTransform(size / 256f, size / 256f);
    using (var background = Rounded(new RectangleF(8, 8, 240, 240), 54))
    using (var ink = new SolidBrush(Color.FromArgb(44, 44, 46))) graphics.FillPath(ink, background);
    using (var outline = new Pen(Color.White, 22) { StartCap = LineCap.Round, EndCap = LineCap.Round })
    {
        graphics.DrawArc(outline, new Rectangle(62, 60, 126, 126), 38, 310);
        graphics.DrawLine(outline, 155, 159, 191, 195);
    }
    using (var sage = new Pen(Color.FromArgb(181, 201, 185), 17) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
    {
        graphics.DrawLine(sage, 66, 205, 117, 205);
        graphics.DrawLine(sage, 104, 192, 117, 205);
        graphics.DrawLine(sage, 104, 218, 117, 205);
    }
    using var png = new MemoryStream();
    bitmap.Save(png, ImageFormat.Png);
    pngs.Add(png.ToArray());
    if (size == 256) bitmap.Save(previewPath, ImageFormat.Png);
}

using var file = File.Create(iconPath);
using var writer = new BinaryWriter(file);
writer.Write((ushort)0);
writer.Write((ushort)1);
writer.Write((ushort)sizes.Length);
int offset = 6 + 16 * sizes.Length;
for (int index = 0; index < sizes.Length; index++)
{
    var size = sizes[index];
    writer.Write((byte)(size == 256 ? 0 : size));
    writer.Write((byte)(size == 256 ? 0 : size));
    writer.Write((byte)0);
    writer.Write((byte)0);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write((uint)pngs[index].Length);
    writer.Write((uint)offset);
    offset += pngs[index].Length;
}
foreach (var png in pngs) writer.Write(png);

static GraphicsPath Rounded(RectangleF area, float radius)
{
    var path = new GraphicsPath();
    float diameter = radius * 2;
    path.AddArc(area.Left, area.Top, diameter, diameter, 180, 90);
    path.AddArc(area.Right - diameter, area.Top, diameter, diameter, 270, 90);
    path.AddArc(area.Right - diameter, area.Bottom - diameter, diameter, diameter, 0, 90);
    path.AddArc(area.Left, area.Bottom - diameter, diameter, diameter, 90, 90);
    path.CloseFigure();
    return path;
}
