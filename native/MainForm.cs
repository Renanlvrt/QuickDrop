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
    private const uint HotkeyKey = 0x51; // Q
    private readonly FlowLayoutPanel content = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24) };
    private readonly NotifyIcon tray;
    private readonly string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickDrop", "settings.json");
    private TransferServer? transfer;
    private string? previousFolder;
    private bool exitRequested;
    private Label status = new();
    private string[] selectedFiles = [];

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);

    public MainForm()
    {
        Text = "QuickDrop";
        Width = 470; Height = 590; MinimumSize = new Size(440, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Paper;
        Font = new Font("Segoe UI", 10);
        AllowDrop = true;
        Controls.Add(content);
        try { previousFolder = JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(settingsFile))?.PreviousFolder; } catch { }
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open QuickDrop", null, (_, _) => ShowPopup());
        menu.Items.Add("Exit", null, (_, _) => { exitRequested = true; Close(); });
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "QuickDrop", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowPopup();
        AttachDrop(this);
        AttachDrop(content);
        ShowHome();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!RegisterHotKey(Handle, HotkeyId, HotkeyMods, HotkeyKey)) SetStatus("Ctrl+Alt+Q is in use. Open QuickDrop from the tray instead.");
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

    private void Clear(string heading, bool back = false)
    {
        foreach (Control old in content.Controls.Cast<Control>().ToArray())
        {
            if (old is PictureBox picture) picture.Image?.Dispose();
            old.Dispose();
        }
        content.Controls.Clear();
        if (back) AddButton("←  Back", ShowHome, Ui.White, compact: true, height: 38, width: 95, margin: new Padding(0, 0, 0, 18));
        content.Controls.Add(new Label { Text = heading, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        status = new Label { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = Color.FromArgb(130, 55, 60), Margin = new Padding(0, 15, 0, 0), Font = new Font("Segoe UI", 9) };
    }

    private TileButton AddButton(string label, Action action, Color fill, string subtitle = "", string icon = "", bool compact = false, int height = 76, int width = 385, Padding? margin = null)
    {
        var button = new TileButton { Text = label, Width = width, Height = height, Fill = fill, Subtitle = subtitle, IconName = icon, Compact = compact, Margin = margin ?? new Padding(0, 0, 0, 12) };
        button.Click += (_, _) => action();
        content.Controls.Add(button);
        return button;
    }

    private void AddCopy(string text, int bottom = 20)
        => content.Controls.Add(new Label { Text = text, Font = new Font("Segoe UI", 10), ForeColor = Ui.Ink, AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(0, 0, 0, bottom) });

    private void SetStatus(string text) { if (IsDisposed) return; status.Text = text; }

    private void ShowHome()
    {
        Clear("QuickDrop");
        AddCopy("Move files between your laptop and iPhone. Choose a direction to get started.", 20);
        AddButton("Send from iPhone", ShowReceiveChoices, Ui.Sage, "Scan a QR code and upload files", "phone");
        AddButton("Send from laptop", ShowSendChoices, Ui.Ultra, "Choose files, then scan to download", "laptop");
        var startup = new CheckBox { Text = "Start with Windows", AutoSize = true, Checked = StartsWithWindows(), Margin = new Padding(2, 14, 0, 0), ForeColor = Ui.Ink };
        startup.CheckedChanged += (_, _) => SetStartup(startup.Checked);
        content.Controls.Add(startup);
        AddCopy("Ctrl+Alt+Q opens QuickDrop · Same Wi-Fi required", 0);
        content.Controls.Add(status);
    }

    private void ShowReceiveChoices()
    {
        Clear("Send from iPhone", back: true);
        AddCopy("Where should the files go?", 14);
        AddButton("Default place", () => _ = ReceiveAsync(DefaultFolder), Ui.Sage, DefaultFolder, "download");
        if (previousFolder is not null) AddButton("Previous place", () => _ = ReceiveAsync(previousFolder), Ui.White, previousFolder, "download");
        AddButton("New place", () => {
            using var picker = new FolderBrowserDialog { Description = "Choose where iPhone files save", UseDescriptionForTitle = true, SelectedPath = previousFolder ?? DefaultFolder };
            if (picker.ShowDialog(this) == DialogResult.OK) _ = ReceiveAsync(picker.SelectedPath);
        }, Ui.White, "Choose a folder on this laptop", "download");
        content.Controls.Add(status);
    }

    private void ShowSendChoices()
    {
        selectedFiles = [];
        ShowSendSelection();
    }

    private void ShowSendSelection()
    {
        Clear("Send from laptop", back: true);
        AddCopy("Choose files to send to your iPhone.", 16);
        var dropArea = new DropTarget { Width = 385, Height = 112, Margin = new Padding(0, 0, 0, 16) };
        AttachDrop(dropArea);
        content.Controls.Add(dropArea);
        AddButton("Choose files", () => {
            using var picker = new OpenFileDialog { Multiselect = true, Title = "Choose files to send" };
            if (picker.ShowDialog(this) == DialogResult.OK) SelectFiles(picker.FileNames);
        }, Ui.White, "Pick photos, videos, or documents", "upload", compact: true, height: 64);
        if (selectedFiles.Length > 0)
        {
            AddCopy(selectedFiles.Length == 1 ? Path.GetFileName(selectedFiles[0]) : $"{selectedFiles.Length} files selected", 8);
            AddButton("Create QR code", () => _ = SendAsync(selectedFiles), Ui.Ultra, "Scan it to download on your iPhone", "phone", compact: true, height: 66);
        }
        content.Controls.Add(status);
    }

    private void SelectFiles(string[] paths)
    {
        selectedFiles = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ShowSendSelection();
    }

    private void AttachDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        control.DragDrop += (_, e) => {
            var paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
            if (paths is { Length: > 0 }) SelectFiles(paths);
        };
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
        Clear("Scan with iPhone", back: true);
        AddCopy("Open your iPhone camera and scan this code.", 10);
        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var code = new PngByteQRCode(data);
        using var stream = new MemoryStream(code.GetGraphic(7));
        using var decoded = new Bitmap(stream);
        content.Controls.Add(new PictureBox { Image = new Bitmap(decoded), Width = 286, Height = 286, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Ui.White, Margin = new Padding(48, 0, 0, 14), Padding = new Padding(8) });
        AddCopy(description, 6);
        AddCopy("The link expires in 10 minutes. Keep the laptop awake.", 0);
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
