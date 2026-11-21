using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using QRCoder;

namespace QuickDrop;

internal sealed class MainForm : Form
{
    private const int HotkeyId = 0x5144;
    private const uint HotkeyMods = 0x0003; // Ctrl+Alt, distinct from QuickPlan's Alt+S.
    private const uint HotkeyKey = 0x44; // D
    private readonly FlowLayoutPanel content = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20) };
    private readonly NotifyIcon tray;
    private readonly string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickDrop", "settings.json");
    private TransferServer? transfer;
    private string? previousFolder;
    private bool exitRequested;
    private Label status = new();

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);

    public MainForm()
    {
        Text = "QuickDrop";
        Width = 470; Height = 590; MinimumSize = new Size(400, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(246, 249, 251);
        AllowDrop = true;
        Controls.Add(content);
        try { previousFolder = JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(settingsFile))?.PreviousFolder; } catch { }
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open QuickDrop", null, (_, _) => ShowPopup());
        menu.Items.Add("Exit", null, (_, _) => { exitRequested = true; Close(); });
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "QuickDrop", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowPopup();
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => {
            var paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
            if (paths is { Length: > 0 }) _ = SendAsync(paths.Where(File.Exists).ToArray());
        };
        ShowHome();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!RegisterHotKey(Handle, HotkeyId, HotkeyMods, HotkeyKey)) SetStatus("Ctrl+Alt+D is in use. Open QuickDrop from the tray instead.");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Environment.GetCommandLineArgs().Contains("--hidden")) Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exitRequested && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        UnregisterHotKey(Handle, HotkeyId);
        tray.Visible = false;
        tray.Dispose();
        if (transfer is not null) _ = transfer.DisposeAsync();
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && message.WParam.ToInt32() == HotkeyId) ShowPopup();
        base.WndProc(ref message);
    }

    private void ShowPopup() { if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Show(); Activate(); }
    private string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QuickDrop", "sentFiles");

    private void Clear(string heading)
    {
        foreach (Control old in content.Controls.Cast<Control>().ToArray())
        {
            if (old is PictureBox picture) picture.Image?.Dispose();
            old.Dispose();
        }
        content.Controls.Clear();
        content.Controls.Add(new Label { Text = heading, Font = new Font(Font.FontFamily, 23, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 20) });
        status = new Label { AutoSize = true, MaximumSize = new Size(385, 0), ForeColor = Color.FromArgb(100, 40, 50), Margin = new Padding(0, 15, 0, 0) };
    }

    private Button Button(string label, Action action)
    {
        var button = new Button { Text = label, Width = 385, Height = 54, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 0, 10) };
        button.FlatAppearance.BorderColor = Color.FromArgb(210, 225, 230);
        button.Click += (_, _) => action();
        content.Controls.Add(button);
        return button;
    }

    private void SetStatus(string text) { if (IsDisposed) return; status.Text = text; }

    private void ShowHome()
    {
        Clear("QuickDrop");
        content.Controls.Add(new Label { Text = "Send files between this laptop and an iPhone on the same Wi-Fi. Ctrl+Alt+D opens QuickDrop.", AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(0, 0, 0, 22) });
        Button("Send from iPhone", ShowReceiveChoices);
        Button("Send from laptop", ShowSendChoices);
        var startup = new CheckBox { Text = "Start with Windows", AutoSize = true, Checked = StartsWithWindows(), Margin = new Padding(0, 15, 0, 0) };
        startup.CheckedChanged += (_, _) => SetStartup(startup.Checked);
        content.Controls.Add(startup);
        content.Controls.Add(status);
    }

    private void ShowReceiveChoices()
    {
        Clear("Send from iPhone");
        Button("← Back", ShowHome);
        Button($"Default place  ·  {DefaultFolder}", () => _ = ReceiveAsync(DefaultFolder));
        if (previousFolder is not null) Button($"Previous place  ·  {previousFolder}", () => _ = ReceiveAsync(previousFolder));
        Button("New place…", () => {
            using var picker = new FolderBrowserDialog { Description = "Choose where iPhone files save", UseDescriptionForTitle = true, SelectedPath = previousFolder ?? DefaultFolder };
            if (picker.ShowDialog(this) == DialogResult.OK) _ = ReceiveAsync(picker.SelectedPath);
        });
        content.Controls.Add(status);
    }

    private void ShowSendChoices()
    {
        Clear("Send from laptop");
        Button("← Back", ShowHome);
        Button("Choose files", () => {
            using var picker = new OpenFileDialog { Multiselect = true, Title = "Choose files to send" };
            if (picker.ShowDialog(this) == DialogResult.OK) _ = SendAsync(picker.FileNames);
        });
        content.Controls.Add(new Label { Text = "You can also drag files into this window.", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
        content.Controls.Add(status);
    }

    private async Task ReceiveAsync(string folder)
    {
        SetStatus("Preparing QR code…");
        try
        {
            var next = await TransferServer.StartAsync(folder, [], name => {
                if (IsHandleCreated) BeginInvoke(() => SetStatus($"Received: {name}"));
            });
            if (transfer is not null) await transfer.DisposeAsync();
            transfer = next;
            previousFolder = folder;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
                File.WriteAllText(settingsFile, JsonSerializer.Serialize(new LocalSettings(folder)));
            }
            catch { /* transfer still works; only remembering the folder failed */ }
            ShowQr(next.Url, $"Saving to {folder}");
        }
        catch (Exception error) { SetStatus(error.Message); }
    }

    private async Task SendAsync(string[] paths)
    {
        if (paths.Length == 0) { SetStatus("Choose files first."); return; }
        SetStatus("Preparing QR code…");
        try
        {
            var next = await TransferServer.StartAsync(null, paths);
            if (transfer is not null) await transfer.DisposeAsync();
            transfer = next;
            ShowQr(next.Url, paths.Length == 1 ? "Scanning starts the download." : "Scanning downloads one ZIP with all selected files.");
        }
        catch (Exception error) { SetStatus(error.Message); }
    }

    private void ShowQr(string url, string description)
    {
        Clear("Scan with iPhone");
        Button("← Back", ShowHome);
        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var code = new PngByteQRCode(data);
        using var stream = new MemoryStream(code.GetGraphic(7));
        using var decoded = new Bitmap(stream);
        content.Controls.Add(new PictureBox { Image = new Bitmap(decoded), Width = 270, Height = 270, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(45, 5, 0, 12) });
        content.Controls.Add(new Label { Text = description, AutoSize = true, MaximumSize = new Size(380, 0) });
        content.Controls.Add(new Label { Text = "The link expires in 10 minutes. Keep the laptop awake.", AutoSize = true, MaximumSize = new Size(380, 0) });
        content.Controls.Add(status);
    }

    private static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("QuickDrop") is string;
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (enabled) key?.SetValue("QuickDrop", $"\"{Environment.ProcessPath}\" --hidden");
        else key?.DeleteValue("QuickDrop", throwOnMissingValue: false);
    }

    private sealed record LocalSettings(string PreviousFolder);
}
