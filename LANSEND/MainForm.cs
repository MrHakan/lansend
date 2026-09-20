using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LANSEND;

internal sealed class MainForm : Form
{
    private readonly PeerDiscoveryService _discovery;
    private readonly TransferServer _server;
    private readonly TransferClient _client;
    private readonly ControlPipeService _controlPipe;
    private readonly bool _startHidden;
    private readonly List<string> _pendingPaths;
    private readonly ListView _peerList = new();
    private readonly Label _selectedFilesLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Button _sendButton = new();
    private readonly Button _openFolderButton = new();
    private readonly Button _refreshButton = new();
    private readonly NotifyIcon _trayIcon;
    private bool _exitRequested;
    private CancellationTokenSource? _sendCancellation;

    public MainForm(
        PeerDiscoveryService discovery,
        TransferServer server,
        TransferClient client,
        ControlPipeService controlPipe,
        IEnumerable<string> initialPaths,
        bool startHidden)
    {
        _discovery = discovery;
        _server = server;
        _client = client;
        _controlPipe = controlPipe;
        _startHidden = startHidden;
        _pendingPaths = initialPaths
            .Where(path => !path.StartsWith("--", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Text = "LANSEND";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 480);
        Size = new Size(920, 620);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);

        BuildUi();

        _trayIcon = BuildTrayIcon();
        _discovery.PeersChanged += DiscoveryOnPeersChanged;
        _server.ConfirmTransferAsync = ConfirmIncomingTransferAsync;
        _server.TransferCompleted += ServerOnTransferCompleted;
        _controlPipe.RequestReceived += ControlPipeOnRequestReceived;
        Shown += MainFormOnShown;
    }

    private void BuildUi()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 88,
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
            Text = "Yerel ağdaki cihazlara hızlı dosya gönder",
            ForeColor = Color.FromArgb(220, 235, 248),
            AutoSize = true,
            Location = new Point(27, 52)
        });
        Controls.Add(header);

        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 78,
            Padding = new Padding(20, 12, 20, 14),
            BackColor = Color.White
        };

        _sendButton.Text = "Seçili cihaza gönder";
        _sendButton.Width = 170;
        _sendButton.Height = 36;
        _sendButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _sendButton.Location = new Point(bottom.Width - 190, 12);
        _sendButton.Click += async (_, _) => await SendSelectedAsync();
        bottom.Controls.Add(_sendButton);

        _openFolderButton.Text = "LANSEND klasörünü aç";
        _openFolderButton.Width = 170;
        _openFolderButton.Height = 36;
        _openFolderButton.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _openFolderButton.Location = new Point(20, 12);
        _openFolderButton.Click += (_, _) => OpenReceiveFolder();
        bottom.Controls.Add(_openFolderButton);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = Color.FromArgb(80, 90, 102);
        _statusLabel.Location = new Point(215, 22);
        _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        bottom.Controls.Add(_statusLabel);
        bottom.Resize += (_, _) => _sendButton.Left = bottom.ClientSize.Width - _sendButton.Width - 20;
        Controls.Add(bottom);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20, 18, 20, 12),
            BackColor = Color.FromArgb(245, 247, 250)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(content);

        var title = new Label
        {
            Text = "Bağlı cihazlar",
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 43, 52),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        content.Controls.Add(title, 0, 0);

        var fileBar = new Panel { Dock = DockStyle.Fill };
        _selectedFilesLabel.AutoSize = false;
        _selectedFilesLabel.Dock = DockStyle.Fill;
        _selectedFilesLabel.ForeColor = Color.FromArgb(80, 90, 102);
        fileBar.Controls.Add(_selectedFilesLabel);
        _refreshButton.Text = "Yenile";
        _refreshButton.Width = 78;
        _refreshButton.Height = 27;
        _refreshButton.Dock = DockStyle.Right;
        _refreshButton.Click += (_, _) => RefreshPeers();
        fileBar.Controls.Add(_refreshButton);
        content.Controls.Add(fileBar, 0, 1);

        _peerList.Dock = DockStyle.Fill;
        _peerList.View = View.Details;
        _peerList.FullRowSelect = true;
        _peerList.GridLines = true;
        _peerList.MultiSelect = false;
        _peerList.HideSelection = false;
        _peerList.BackColor = Color.White;
        _peerList.Columns.Add("Cihaz adı", 260);
        _peerList.Columns.Add("Kullanıcı", 210);
        _peerList.Columns.Add("IP adresi", 170);
        _peerList.Columns.Add("Durum", 120);
        _peerList.DoubleClick += async (_, _) => await SendSelectedAsync();
        content.Controls.Add(_peerList, 0, 2);

        UpdateSelectedFilesLabel();
        UpdateStatus("Cihazlar aranıyor...");
    }

    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("LANSEND'i aç", null, (_, _) => ShowWindow());
        menu.Items.Add("LANSEND klasörünü aç", null, (_, _) => OpenReceiveFolder());
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
        RefreshPeers();
        if (_startHidden && _pendingPaths.Count == 0)
        {
            Hide();
            ShowInTaskbar = false;
        }
        else
        {
            ShowWindow();
        }
    }

    private void DiscoveryOnPeersChanged(object? sender, EventArgs e)
    {
        RunOnUi(RefreshPeers);
    }

    private void RefreshPeers()
    {
        if (IsDisposed)
        {
            return;
        }

        var selectedId = (_peerList.SelectedItems.Count > 0 ? _peerList.SelectedItems[0].Tag as PeerInfo : null)?.Id;
        _peerList.BeginUpdate();
        try
        {
            _peerList.Items.Clear();
            foreach (var peer in _discovery.GetPeers())
            {
                var item = new ListViewItem(peer.DeviceName) { Tag = peer };
                item.SubItems.Add(peer.UserName);
                item.SubItems.Add(peer.IpAddress);
                item.SubItems.Add("Hazır");
                _peerList.Items.Add(item);
                if (peer.Id == selectedId)
                {
                    item.Selected = true;
                }
            }
        }
        finally
        {
            _peerList.EndUpdate();
        }

        if (_peerList.Items.Count == 0)
        {
            UpdateStatus("Başka LANSEND cihazı bulunamadı. Diğer cihazlarda da LANSEND açık olmalı.");
        }
        else if (_pendingPaths.Count > 0)
        {
            UpdateStatus($"{_peerList.Items.Count} cihaz hazır. Göndermek için bir cihaz seçin.");
        }
        else
        {
            UpdateStatus($"{_peerList.Items.Count} cihaz bulundu.");
        }
    }

    public void HandleExternalRequest(ControlRequest request)
    {
        if (request.Command.Equals("send", StringComparison.OrdinalIgnoreCase))
        {
            _pendingPaths.Clear();
            _pendingPaths.AddRange(request.Paths.Where(path => !path.StartsWith("--", StringComparison.OrdinalIgnoreCase)));
            UpdateSelectedFilesLabel();
        }

        ShowWindow();
        RefreshPeers();
    }

    private async Task SendSelectedAsync()
    {
        if (_peerList.SelectedItems.Count == 0 || _peerList.SelectedItems[0].Tag is not PeerInfo peer)
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
            await _client.SendAsync(peer, build.Files, progress, _sendCancellation.Token);
            UpdateStatus($"Aktarım tamamlandı: {peer.DeviceName}");
            MessageBox.Show(this, $"{build.Files.Count} dosya {peer.DeviceName} cihazına gönderildi.", "LANSEND", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private Task<bool> ConfirmIncomingTransferAsync(IncomingTransferRequest request)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return Task.FromResult(false);
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUi(() =>
        {
            var preview = string.Join(Environment.NewLine, request.Files.Take(5).Select(file =>
                $"• {Path.GetFileName(file.RelativePath)} ({FormatBytes(file.Length)})"));
            if (request.Files.Count > 5)
            {
                preview += Environment.NewLine + $"• ... ve {request.Files.Count - 5} dosya daha";
            }

            var result = MessageBox.Show(
                this,
                $"{request.DeviceName} / {request.UserName} ({request.RemoteIpAddress}) cihazı {request.Files.Count} dosya göndermek istiyor.\n\n{preview}\n\nDosyalar Documents\\LANSEND klasörüne kaydedilsin mi?",
                "LANSEND - Gelen aktarım",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            completion.TrySetResult(result == DialogResult.Yes);
        });
        return completion.Task;
    }

    private void ServerOnTransferCompleted(object? sender, TransferCompletedEventArgs e)
    {
        RunOnUi(() =>
        {
            UpdateStatus($"Gelen aktarım tamamlandı — {e.ReceivedPaths.Count} dosya kaydedildi.");
            _trayIcon.ShowBalloonTip(3500, "LANSEND", $"{e.ReceivedPaths.Count} dosya Documents\\LANSEND klasörüne kaydedildi.", ToolTipIcon.Info);
        });
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

    private void OpenReceiveFolder()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LANSEND");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
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
        if (IsDisposed)
        {
            return;
        }

        _statusLabel.Text = text;
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
            _sendCancellation?.Cancel();
            _sendCancellation?.Dispose();
            _trayIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
