using System.Drawing;
using System.Windows.Forms;

namespace LANSEND;

internal sealed class CredentialDialog : Form
{
    private readonly TextBox _userNameTextBox = new();
    private readonly TextBox _passwordTextBox = new();

    public CredentialDialog(string? suggestedUserName)
    {
        Text = "SMB kimlik bilgileri";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(430, 220);
        Font = new Font("Segoe UI", 9F);

        _userNameTextBox.Text = suggestedUserName ?? string.Empty;
        _passwordTextBox.UseSystemPasswordChar = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(18, 18, 18, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        AddField(layout, 0, "Windows kullanıcı adı", _userNameTextBox);
        AddField(layout, 1, "Parola", _passwordTextBox);

        var note = new Label
        {
            Text = "Bu bilgiler yalnızca bu aktarım için kullanılır ve kaydedilmez.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(85, 95, 105)
        };
        layout.Controls.Add(note, 0, 2);
        layout.SetColumnSpan(note, 2);
        Controls.Add(layout);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 0, 18, 10),
            WrapContents = false
        };
        var connectButton = new Button { Text = "Bağlan", Width = 88, Height = 30 };
        var cancelButton = new Button { Text = "İptal", Width = 88, Height = 30, DialogResult = DialogResult.Cancel };
        connectButton.Click += (_, _) => AcceptCredentials();
        buttons.Controls.Add(connectButton);
        buttons.Controls.Add(cancelButton);
        Controls.Add(buttons);
        AcceptButton = connectButton;
        CancelButton = cancelButton;
    }

    public SmbCredentials? Credentials { get; private set; }

    private static void AddField(TableLayoutPanel panel, int row, string labelText, TextBox textBox)
    {
        panel.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, row);
        textBox.Dock = DockStyle.Fill;
        panel.Controls.Add(textBox, 1, row);
    }

    private void AcceptCredentials()
    {
        if (string.IsNullOrWhiteSpace(_userNameTextBox.Text) || string.IsNullOrEmpty(_passwordTextBox.Text))
        {
            MessageBox.Show(this, "Kullanıcı adı ve parola gereklidir.", "LANSEND", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Credentials = new SmbCredentials
        {
            UserName = _userNameTextBox.Text.Trim(),
            Password = _passwordTextBox.Text
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
