using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LANSEND;

internal sealed class MainForm : Form
{
    private readonly LanDeviceDiscoveryService _discovery;
    private readonly DirectSmbTransferService _transfer;
    private readonly DeviceProfileStore _profileStore;
    private readonly ControlPipeService _controlPipe;
    private readonly bool _startHidden;
    private readonly List<string> _pendingPaths;
    private readonly ListView _deviceList = new();
    private readonly Label _selectedFilesLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Button _sendButton = new();
    private readonly Button _openTargetButton = new();
    private readonly Button _scanButton = new();
    private readonly Button _addButton = new();
    private readonly NotifyIcon _trayIcon;
    private List<DeviceProfile> _devices;
    private bool _exitRequested;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _sendCancellation;

    public MainForm(
        LanDeviceDiscoveryService discovery,
        DirectSmbTransferService transfer,
        DeviceProfileStore profileStore,
        ControlPipeService controlPipe,
        IEnumerable<string> initialPaths,
        bool startHidden)
    {
        _discovery = discovery;
        _transfer = transfer;
        _profileStore = profileStore;
        _controlPipe = controlPipe;
        _startHidden = startHidden;
        _devices = _profileStore.Load();
        _pendingPaths = initialPaths
            .Where(path => !path.StartsWith("--", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Text = "LANSEND";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 500);
        Size = new Size(980, 640);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);

        BuildUi();

        _trayIcon = BuildTrayIcon();
        _controlPipe.RequestReceived += ControlPipeOnRequestReceived;
        Shown += MainFormOnShown;
    }

    private void BuildUi()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 96,
            BackColor = Color.FromArgb(29, 78, 121),
            Padding = new Padding(24, 15, 24, 10)
        };
        header.Controls.Add(new Label
        {
            Text = "LANSEND",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 12)
        });
        header.Controls.Add(new Label
        {
            Text = "Karşı bilgisayarda LANSEND açık olmadan IP üzerinden gönder",
            ForeColor = Color.FromArgb(220, 235, 248),
            AutoSize = true,
            Location = new Point(27, 52)
        });

        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 86,
            Padding = new Padding(20, 12, 20, 14),
            BackColor = Color.White
        };
        ConfigureBottomButton(_addButton, "IP ile ekle", 96);
        _addButton.Location = new Point(20, 12);
        _addButton.Click += (_, _) => AddDevice();
        bottom.Controls.Add(_addButton);

        ConfigureBottomButton(_scanButton, "Ağı tara", 96);
        _scanButton.Location = new Point(125, 12);
        _scanButton.Click += async (_, _) => await ScanAsync();
        bottom.Controls.Add(_scanButton);

        ConfigureBottomButton(_openTargetButton, "Hedef paylaşımı aç", 160);
        _openTargetButton.Location = new Point(230, 12);
        _openTargetButton.Click += (_, _) => OpenTargetShare();
        bottom.Controls.Add(_openTargetButton);

        _sendButton.Text = "Seçili cihaza gönder";
        _sendButton.Width = 175;
        _sendButton.Height = 36;
        _sendButton.Top = 12;
        _sendButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _sendButton.Click += async (_, _) => await SendSelectedAsync();
        bottom.Controls.Add(_sendButton);

        _statusLabel.AutoEllipsis = true;
        _statusLabel.ForeColor = Color.FromArgb(80, 90, 102);
        _statusLabel.Location = new Point(395, 22);
        _statusLabel.Height = 36;
        _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        bottom.Controls.Add(_statusLabel);
        bottom.Resize += (_, _) =>
        {
            _sendButton.Left = bottom.ClientSize.Width - _sendButton.Width - 20;
            _statusLabel.Width = Math.Max(100, _sendButton.Left - _statusLabel.Left - 16);
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20, 18, 20, 12),
            BackColor = Color.FromArgb(245, 247, 250)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(new Label
        {
            Text = "SMB açık cihazlar",
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 43, 52),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _selectedFilesLabel.AutoSize = false;
        _selectedFilesLabel.Dock = DockStyle.Fill;
        _selectedFilesLabel.ForeColor = Color.FromArgb(80, 90, 102);
        content.Controls.Add(_selectedFilesLabel, 0, 1);

        _deviceList.Dock = DockStyle.Fill;
        _deviceList.View = View.Details;
        _deviceList.FullRowSelect = true;
        _deviceList.GridLines = true;
        _deviceList.MultiSelect = false;
        _deviceList.HideSelection = false;
        _deviceList.BackColor = Color.White;
        _deviceList.Columns.Add("Cihaz adı", 245);
        _deviceList.Columns.Add("Kullanıcı", 205);
        _deviceList.Columns.Add("IP adresi", 165);
        _deviceList.Columns.Add("Paylaşım", 145);
        _deviceList.Columns.Add("Durum", 135);
        _deviceList.DoubleClick += async (_, _) => await SendSelectedAsync();
        _deviceList.MouseDown += DeviceListOnMouseDown;
        _deviceList.ContextMenuStrip = BuildDeviceContextMenu();
        content.Controls.Add(_deviceList, 0, 2);

        Controls.Add(content);
        Controls.Add(bottom);
        Controls.Add(header);

        UpdateSelectedFilesLabel();
        UpdateStatus("Ağı taramak için Ağı tara düğmesine basın.");
    }

    private static void ConfigureBottomButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 36;
        button.Anchor = AnchorStyles.Left | AnchorStyles.Top;
    }

    private ContextMenuStrip BuildDeviceContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Gönder", null, async (_, _) => await SendSelectedAsync());
        menu.Items.Add("Düzenle", null, (_, _) => EditSelectedDevice());
        menu.Items.Add("Hedef paylaşımını aç", null, (_, _) => OpenTargetShare());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Listeden sil", null, (_, _) => RemoveSelectedDevice());
        return menu;
    }

    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("LANSEND'i aç", null, (_, _) => ShowWindow());
        menu.Items.Add("Ağı tara", null, async (_, _) => await ScanAsync());
        menu.Items.Add("Hedef paylaşımını aç", null, (_, _) => OpenTargetShare());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => ExitApplication());

        var tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "LANSEND",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowWindow();
        return tray;
    }

    private void MainFormOnShown(object? sender, EventArgs e)
    {
        RefreshDeviceList();
        if (_startHidden && _pendingPaths.Count == 0)
        {
            Hide();
            ShowInTaskbar = false;
        }

        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        if (_scanCancellation is not null)
        {
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        _scanButton.Enabled = false;
        UpdateStatus("Yerel ağdaki IP adresleri ve SMB paylaşımı taranıyor...");
        try
        {
            var devices = await _discovery.ScanAsync(_devices, _scanCancellation.Token);
            _devices = devices.ToList();
            _profileStore.Save(_devices);
            RefreshDeviceList();
            if (_devices.Count == 0)
            {
                UpdateStatus("SMB cihazı bulunamadı. Hedefte paylaşımı açın veya IP ile ekleyin.");
            }
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Ağ taraması iptal edildi.");
        }
        catch (Exception exception)
        {
            UpdateStatus("Ağ taraması başarısız.");
            MessageBox.Show(this, exception.Message, "LANSEND - Tarama başarısız", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _scanButton.Enabled = true;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    private void RefreshDeviceList()
    {
        if (IsDisposed)
        {
            return;
        }

        var selectedId = _deviceList.SelectedItems.Count > 0
            ? (_deviceList.SelectedItems[0].Tag as DeviceProfile)?.Id
            : null;
        _deviceList.BeginUpdate();
        try
        {
            _deviceList.Items.Clear();
            foreach (var device in _devices)
            {
                var item = new ListViewItem(device.DeviceName) { Tag = device };
                item.SubItems.Add(string.IsNullOrWhiteSpace(device.UserName) ? "—" : device.UserName);
                item.SubItems.Add(device.IpAddress);
                item.SubItems.Add(device.ShareName);
                item.SubItems.Add(device.Status);
                _deviceList.Items.Add(item);
                if (device.Id == selectedId)
                {
                    item.Selected = true;
                }
            }
        }
        finally
        {
            _deviceList.EndUpdate();
        }

        if (_devices.Count == 0)
        {
            UpdateStatus("SMB cihazı bulunamadı. Hedefte paylaşımı açın veya IP ile ekleyin.");
        }
        else if (_pendingPaths.Count > 0)
        {
            UpdateStatus($"{_devices.Count} kayıt bulundu. Göndermek için çevrimiçi bir cihaz seçin.");
        }
        else
        {
            UpdateStatus($"{_devices.Count} cihaz listelendi.");
        }
    }

    private void DeviceListOnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right && _deviceList.GetItemAt(e.X, e.Y) is ListViewItem item)
        {
            item.Selected = true;
        }
    }

    private void AddDevice()
    {
        using var dialog = new DeviceEditorDialog(null);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Profile is null)
        {
            return;
        }

        UpsertDevice(dialog.Profile);
    }

    private void EditSelectedDevice()
    {
        if (GetSelectedDevice() is not { } selected)
        {
            MessageBox.Show(this, "Önce listeden bir cihaz seçin.", "LANSEND", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new DeviceEditorDialog(selected);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Profile is not null)
        {
            UpsertDevice(dialog.Profile);
        }
    }

    private void UpsertDevice(DeviceProfile profile)
    {
        var index = _devices.FindIndex(device =>
            device.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase) ||
            device.IpAddress.Equals(profile.IpAddress, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            profile.IsOnline = _devices[index].IsOnline;
            _devices[index] = profile;
        }
        else
        {
            _devices.Add(profile);
        }

        _profileStore.Save(_devices);
        RefreshDeviceList();
    }

    private void RemoveSelectedDevice()
    {
        if (GetSelectedDevice() is not { } selected)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"{selected.DeviceName} cihazı listeden silinsin mi?",
            "LANSEND",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result != DialogResult.Yes)
        {
            return;
        }

        _devices.RemoveAll(device => device.Id.Equals(selected.Id, StringComparison.OrdinalIgnoreCase));
        _profileStore.Save(_devices);
        RefreshDeviceList();
    }

    private DeviceProfile? GetSelectedDevice()
    {
        return _deviceList.SelectedItems.Count == 0
            ? null
            : _deviceList.SelectedItems[0].Tag as DeviceProfile;
    }

    public void HandleExternalRequest(ControlRequest request)
    {
        if (request.Command.Equals("send", StringComparison.OrdinalIgnoreCase))
        {
            _pendingPaths.Clear();
            _pendingPaths.AddRange(request.Paths
                .Where(path => !path.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            UpdateSelectedFilesLabel();
        }

        ShowWindow();
        _ = ScanAsync();
    }

    private async Task SendSelectedAsync()
    {
        if (GetSelectedDevice() is not { } device)
        {
            MessageBox.Show(this, "Önce listeden bir cihaz seçin.", "LANSEND", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_pendingPaths.Count == 0)
        {
            MessageBox.Show(this, "Gönderilecek dosya seçilmedi. Dosya Gezgini'nde sağ tık → Send to → LANSEND kullanın.", "LANSEND", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var build = TransferFileBuilder.Build(_pendingPaths);
        if (build.Files.Count == 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, build.Errors), "LANSEND", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (build.Errors.Count > 0)
        {
            var continueSending = MessageBox.Show(
                this,
                "Bazı dosyalar okunamadı:\n\n" + string.Join(Environment.NewLine, build.Errors.Take(5)) + "\n\nOkunabilen dosyalar gönderilsin mi?",
                "LANSEND",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (continueSending != DialogResult.Yes)
            {
                return;
            }
        }

        _sendCancellation?.Dispose();
        _sendCancellation = new CancellationTokenSource();
        _sendButton.Enabled = false;
        try
        {
            var progress = new Progress<TransferProgress>(value =>
                UpdateStatus($"{value.CurrentFile} gönderiliyor — {value.Percentage:0}% ({FormatBytes(value.BytesSent)} / {FormatBytes(value.TotalBytes)})"));

            try
            {
                await _transfer.SendAsync(device, build.Files, null, progress, _sendCancellation.Token);
            }
            catch (SmbAuthenticationRequiredException)
            {
                using var credentialsDialog = new CredentialDialog(device.UserName);
                if (credentialsDialog.ShowDialog(this) != DialogResult.OK || credentialsDialog.Credentials is null)
                {
                    UpdateStatus("SMB kimlik bilgileri girilmedi.");
                    return;
                }

                var credentials = credentialsDialog.Credentials;
                device.UserName = credentials.UserName;
                _profileStore.Save(_devices);
                await _transfer.SendAsync(device, build.Files, credentials, progress, _sendCancellation.Token);
            }

            UpdateStatus($"Aktarım tamamlandı: {device.DeviceName}");
            MessageBox.Show(this, $"{build.Files.Count} öğe {device.DeviceName} cihazındaki Documents\\LANSEND klasörüne gönderildi.", "LANSEND", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _pendingPaths.Clear();
            UpdateSelectedFilesLabel();
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Aktarım iptal edildi.");
        }
        catch (Exception exception)
        {
            UpdateStatus("Aktarım başarısız.");
            MessageBox.Show(this, exception.Message, "LANSEND - Aktarım başarısız", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _sendButton.Enabled = true;
            _sendCancellation?.Dispose();
            _sendCancellation = null;
        }
    }

    private void OpenTargetShare()
    {
        if (GetSelectedDevice() is not { } device)
        {
            MessageBox.Show(this, "Önce listeden bir cihaz seçin.", "LANSEND", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = device.UncPath,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "LANSEND - Paylaşım açılamadı", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ControlPipeOnRequestReceived(object? sender, ControlRequest request)
    {
        RunOnUi(() => HandleExternalRequest(request));
    }

    private void UpdateSelectedFilesLabel()
    {
        if (_pendingPaths.Count == 0)
        {
            _selectedFilesLabel.Text = "Gönderilecek dosya yok — Dosya Gezgini'nde sağ tık → Send to → LANSEND";
            return;
        }

        var count = _pendingPaths.Count;
        var names = string.Join(", ", _pendingPaths.Take(3).Select(Path.GetFileName));
        _selectedFilesLabel.Text = $"Gönderilecek: {count} öğe — {names}{(count > 3 ? " ..." : string.Empty)}";
    }

    private void ShowWindow()
    {
        if (IsDisposed)
        {
            return;
        }

        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void UpdateStatus(string text)
    {
        if (!IsDisposed)
        {
            _statusLabel.Text = text;
        }
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _scanCancellation?.Cancel();
        _sendCancellation?.Cancel();
        _trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            return;
        }

        _trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scanCancellation?.Cancel();
            _scanCancellation?.Dispose();
            _sendCancellation?.Cancel();
            _sendCancellation?.Dispose();
            _trayIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
