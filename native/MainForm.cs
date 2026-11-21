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
    private readonly FlowLayoutPanel content = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private readonly NotifyIcon tray;
    private readonly string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickDrop", "settings.json");
    private TransferServer? transfer;
    private string? previousFolder;
    private bool exitRequested;
    private Label status = new();
    private string[] selectedFiles = [];
    private readonly bool screenshotMode;
    private readonly float displayScale;
    private int P(float logical) => (int)Math.Round(logical * displayScale);
    private Padding Pad(int left, int top, int right, int bottom) => new(P(left), P(top), P(right), P(bottom));

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    public MainForm(bool screenshotMode = false)
    {
        this.screenshotMode = screenshotMode;
        Text = "QuickDrop";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        _ = Handle;
        displayScale = DeviceDpi / 96f;
        Width = P(480); Height = P(580); MinimumSize = new Size(P(420), P(480));
        content.Padding = Pad(16, 48, 16, 24);
        BackColor = Ui.Paper;
        Font = new Font("Segoe UI", 10);
        using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("QuickDrop.Assets.QuickDrop.ico"))
            if (stream is not null) Icon = new Icon(stream);
        AllowDrop = true;
        Controls.Add(content);
        try { previousFolder = JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(settingsFile))?.PreviousFolder; } catch { }
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open QuickDrop", null, (_, _) => ShowPopup());
        var startupMenu = new ToolStripMenuItem("Start with Windows") { Checked = StartsWithWindows(), CheckOnClick = true };
        startupMenu.CheckedChanged += (_, _) => SetStartup(startupMenu.Checked);
        menu.Items.Add(startupMenu);
        menu.Items.Add("Exit", null, (_, _) => { exitRequested = true; Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "QuickDrop", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowPopup();
        AttachDrop(this);
        AttachDrop(content);
        ShowHome();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!screenshotMode && !RegisterHotKey(Handle, HotkeyId, HotkeyMods, HotkeyKey)) SetStatus("Ctrl+Alt+Q is in use. Open QuickDrop from the tray instead.");
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

    private void Clear(bool back = false)
    {
        foreach (Control old in content.Controls.Cast<Control>().ToArray())
        {
            old.Dispose();
        }
        content.Controls.Clear();
        var header = new Panel { Width = P(424), Height = P(38), Margin = Pad(0, 0, 0, 20) };
        if (back)
        {
            var backButton = new TileButton { Text = "", AccessibleName = "Back", IconName = "back", Fill = Ui.White, Compact = true, Width = P(38), Height = P(38), Location = new Point(0, 0) };
            backButton.Click += (_, _) => ShowHome();
            header.Controls.Add(backButton);
        }
        header.Controls.Add(new Label { Text = "File transfer", Font = new Font("Segoe UI Variable Text", 18, FontStyle.Bold), ForeColor = Ui.Ink, AutoSize = true, Location = new Point(P(back ? 50 : 0), P(3)) });
        content.Controls.Add(header);
        status = new Label { AutoSize = true, MaximumSize = new Size(P(380), 0), ForeColor = Color.FromArgb(130, 55, 60), Margin = Pad(0, 15, 0, 0), Font = new Font("Segoe UI", 9) };
    }

    private TileButton AddButton(string label, Action action, Color fill, string subtitle = "", string icon = "", bool compact = false, bool centered = false, int height = 64, Color? foreground = null, Padding? margin = null)
    {
        var button = new TileButton { Text = label, Width = P(422), Height = P(height), Fill = fill, Foreground = foreground ?? Ui.Ink, Subtitle = subtitle, IconName = icon, Compact = compact, Centered = centered, Margin = margin ?? Pad(0, 0, 0, 12) };
        button.Click += (_, _) => action();
        content.Controls.Add(button);
        return button;
    }

    private void AddCopy(string text, int bottom = 20, bool muted = false, bool bold = false)
        => content.Controls.Add(new Label { Text = text, Font = new Font("Segoe UI Variable Text", 10.5f, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = muted ? Ui.Muted : Ui.Ink, AutoSize = true, MaximumSize = new Size(P(422), 0), Margin = Pad(0, 0, 0, bottom) });

    private void AddCenteredCopy(string text, int bottom = 4, bool muted = false, bool bold = false)
        => content.Controls.Add(new Label { Text = text, Font = new Font("Segoe UI Variable Text", muted ? 9 : 10.5f, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = muted ? Ui.Muted : Ui.Ink, Width = P(422), Height = P(muted ? 32 : 26), TextAlign = ContentAlignment.MiddleCenter, Margin = Pad(0, 0, 0, bottom) });

    private void SetStatus(string text) { if (IsDisposed) return; status.Text = text; }

    private void ShowHome()
    {
        Clear();
        AddCopy("Move files between this laptop and your iPhone on the same Wi-Fi network.", 24, muted: true);
        AddButton("Send from iPhone", ShowReceiveChoices, Ui.Sage, icon: "phone");
        AddButton("Send from laptop", ShowSendChoices, Ui.Ultra, icon: "laptop");
        content.Controls.Add(status);
    }

    private void ShowReceiveChoices()
    {
        Clear(back: true);
        AddCopy("Choose where iPhone photos, videos and documents save on this laptop.", 16);
        AddButton("Default place", () => _ = ReceiveAsync(DefaultFolder), Ui.Sage, DefaultFolder, compact: true, height: 62);
        if (previousFolder is not null) AddButton("Previous sent place", () => _ = ReceiveAsync(previousFolder), Ui.White, previousFolder, compact: true, height: 62);
        AddButton("New place…", () => {
            using var picker = new FolderBrowserDialog { Description = "Choose where iPhone files save", UseDescriptionForTitle = true, SelectedPath = previousFolder ?? DefaultFolder };
            if (picker.ShowDialog(this) == DialogResult.OK) _ = ReceiveAsync(picker.SelectedPath);
        }, Ui.White, compact: true, height: 50);
        content.Controls.Add(status);
    }

    private void ShowSendChoices()
    {
        selectedFiles = [];
        ShowSendSelection();
    }

    private void ShowSendSelection()
    {
        Clear(back: true);
        var dropArea = new DropTarget { Width = P(422), Height = P(66), Margin = Pad(0, 0, 0, 16) };
        AttachDrop(dropArea);
        content.Controls.Add(dropArea);
        AddButton("Choose files", () => {
            using var picker = new OpenFileDialog { Multiselect = true, Title = "Choose files to send" };
            if (picker.ShowDialog(this) == DialogResult.OK) SelectFiles(picker.FileNames);
        }, Ui.White, icon: "upload", compact: true, centered: true, height: 50);
        if (selectedFiles.Length > 0)
        {
            var names = string.Join("\n", selectedFiles.Select(Path.GetFileName));
            var list = new Label { Text = names, Width = P(422), Height = P(Math.Min(160, 24 + selectedFiles.Length * 22)), Padding = Pad(12, 12, 12, 12), BackColor = Ui.White, ForeColor = Ui.Ink, Font = new Font("Segoe UI Variable Text", 10), Margin = Pad(0, 4, 0, 16), AutoEllipsis = true };
            content.Controls.Add(list);
            AddButton("Create QR code", () => _ = SendAsync(selectedFiles), Ui.Ink, icon: "download", compact: true, centered: true, height: 50, foreground: Ui.White);
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
            ShowQr(next.Url, $"Saving to {folder}", receiving: true);
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
            ShowQr(next.Url, paths.Length == 1 ? "Opening this QR starts the download directly." : "Opening this QR starts the download directly as one ZIP file.", receiving: false);
        }
        catch (Exception error) { SetStatus(error.Message); }
    }

    private void ShowQr(string url, string description, bool receiving)
    {
        Clear(back: true);
        if (receiving) AddCopy(description, 16);
        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var code = new PngByteQRCode(data);
        using var stream = new MemoryStream(code.GetGraphic(7));
        using var decoded = new Bitmap(stream);
        content.Controls.Add(new QrCard(new Bitmap(decoded)) { Size = new Size(P(256), P(256)), Margin = Pad(83, 4, 0, 12) });
        AddCenteredCopy("Scan with your iPhone Camera", bold: true);
        AddCenteredCopy("Link expires in 10 minutes. Keep the laptop awake during transfer.", bottom: 12, muted: true);
        if (!receiving) AddCenteredCopy(description);
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
