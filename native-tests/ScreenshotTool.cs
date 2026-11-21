using System.Drawing;
using System.Reflection;
using QuickDrop;

internal static class ScreenshotTool
{
    public static void Capture()
    {
        Exception? failure = null;
        var thread = new Thread(() => {
            try
            {
                Application.EnableVisualStyles();
                using var form = new MainForm(screenshotMode: true) { StartPosition = FormStartPosition.Manual, Location = new Point(-10000, -10000), ShowInTaskbar = false };
                form.Show();
                Application.DoEvents();
                Directory.CreateDirectory("images");
                Save(form, "images/home.png");
                typeof(MainForm).GetMethod("ShowSendChoices", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
                Application.DoEvents();
                Save(form, "images/send.png");
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private static void Save(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path);
    }
}
