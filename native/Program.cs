namespace QuickDrop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, @"Local\QuickDrop.Renanlvrt", out bool first);
        if (!first) { MessageBox.Show("QuickDrop is already running. Use Ctrl+Alt+D or its tray icon.", "QuickDrop"); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
