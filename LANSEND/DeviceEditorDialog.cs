using System.Net;
using System.Drawing;
using System.Windows.Forms;

namespace LANSEND;

internal sealed class DeviceEditorDialog : Form
{
    private readonly DeviceProfile? _source;
    private readonly TextBox _deviceNameTextBox = new();
    private readonly TextBox _ipAddressTextBox = new();
    private readonly TextBox _userNameTextBox = new();
    private readonly TextBox _shareNameTextBox = new();

    public DeviceEditorDialog(DeviceProfile? profile)
    {
        _source = profile;
        Text = profile is null ? "IP ile cihaz ekle" : "Cihazı düzenle";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(430, 275);
        Font = new Font("Segoe UI", 9F);

        _deviceNameTextBox.Text = profile?.DeviceName ?? string.Empty;
        _ipAddressTextBox.Text = profile?.IpAddress ?? string.Empty;
        _userNameTextBox.Text = profile?.UserName ?? string.Empty;
        _shareNameTextBox.Text = string.IsNullOrWhiteSpace(profile?.ShareName)
            ? AppConstants.DefaultShareName
            : profile!.ShareName;

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(18, 18, 18, 8)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddField(fields, 0, "Cihaz adı", _deviceNameTextBox);
        AddField(fields, 1, "IP adresi / bilgisayar", _ipAddressTextBox);
        AddField(fields, 2, "Windows kullanıcı adı", _userNameTextBox);
        AddField(fields, 3, "SMB paylaşım adı", _shareNameTextBox);

        var hint = new Label
        {
            Text = "Kullanıcı adı isteğe bağlıdır; erişim gerekirse gönderim sırasında parola sorulur.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(85, 95, 105)
        };
        fields.Controls.Add(hint, 0, 4);
        fields.SetColumnSpan(hint, 2);
        Controls.Add(fields);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 0, 18, 10),
            WrapContents = false
        };
        var saveButton = new Button { Text = "Kaydet", Width = 88, Height = 30 };
        var cancelButton = new Button { Text = "İptal", Width = 88, Height = 30, DialogResult = DialogResult.Cancel };
        saveButton.Click += (_, _) => SaveProfile();
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        Controls.Add(buttons);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    public DeviceProfile? Profile { get; private set; }

    private static void AddField(TableLayoutPanel panel, int row, string labelText, TextBox textBox)
    {
        panel.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        textBox.Dock = DockStyle.Fill;
        panel.Controls.Add(textBox, 1, row);
    }

    private void SaveProfile()
    {
        var ipAddress = _ipAddressTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ipAddress) ||
            (IPAddress.TryParse(ipAddress, out var parsed) && parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) ||
            (!IPAddress.TryParse(ipAddress, out _) && Uri.CheckHostName(ipAddress) == UriHostNameType.Unknown))
        {
            MessageBox.Show(this, "Geçerli bir IPv4 adresi veya bilgisayar adı girin.", "LANSEND", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _ipAddressTextBox.Focus();
            return;
        }

        var shareName = _shareNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(shareName) || shareName.Contains('\\') || shareName.Contains('/'))
        {
            MessageBox.Show(this, "Geçerli bir SMB paylaşım adı girin.", "LANSEND", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _shareNameTextBox.Focus();
            return;
        }

        Profile = new DeviceProfile
        {
            Id = _source?.Id ?? Guid.NewGuid().ToString("N"),
            DeviceName = string.IsNullOrWhiteSpace(_deviceNameTextBox.Text) ? ipAddress : _deviceNameTextBox.Text.Trim(),
            IpAddress = ipAddress,
            UserName = _userNameTextBox.Text.Trim(),
            ShareName = shareName
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
