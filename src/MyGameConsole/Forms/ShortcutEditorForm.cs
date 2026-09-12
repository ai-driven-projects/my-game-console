using MyGameConsole.Models;

namespace MyGameConsole.Forms;

/// <summary>
/// Diálogo simples para criar/editar um atalho do menu da bandeja.
/// </summary>
public sealed class ShortcutEditorForm : Form
{
    private readonly TextBox _txtName = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox _txtArgs = new() { Dock = DockStyle.Fill };

    public AppShortcut Shortcut { get; }

    public ShortcutEditorForm(AppShortcut shortcut)
    {
        Shortcut = shortcut;

        Text = "Atalho";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 170);
        Font = new Font("Segoe UI", 9.5f);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var btnBrowse = new Button { Text = "...", Width = 36 };
        btnBrowse.Click += (_, _) => Browse();

        table.Controls.Add(new Label { Text = "Nome:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        table.Controls.Add(_txtName, 1, 0);
        table.SetColumnSpan(_txtName, 2);

        table.Controls.Add(new Label { Text = "Programa / URL:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(_txtPath, 1, 1);
        table.Controls.Add(btnBrowse, 2, 1);

        table.Controls.Add(new Label { Text = "Argumentos:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        table.Controls.Add(_txtArgs, 1, 2);
        table.SetColumnSpan(_txtArgs, 2);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        var btnCancel = new Button { Text = "Cancelar", Width = 100, DialogResult = DialogResult.Cancel };
        var btnOk = new Button { Text = "OK", Width = 100 };
        btnOk.Click += (_, _) => Accept();
        buttons.Controls.AddRange([btnCancel, btnOk]);
        table.Controls.Add(buttons, 0, 3);
        table.SetColumnSpan(buttons, 3);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Controls.Add(table);

        _txtName.Text = shortcut.Name;
        _txtPath.Text = shortcut.Path;
        _txtArgs.Text = shortcut.Arguments ?? string.Empty;
    }

    private void Browse()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Selecione o programa",
            Filter = "Executáveis e atalhos (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Todos os arquivos (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _txtPath.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(_txtName.Text))
            {
                _txtName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }
    }

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text) || string.IsNullOrWhiteSpace(_txtPath.Text))
        {
            MessageBox.Show(this, "Informe o nome e o programa do atalho.", "Atalho",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Shortcut.Name = _txtName.Text.Trim();
        Shortcut.Path = _txtPath.Text.Trim();
        Shortcut.Arguments = string.IsNullOrWhiteSpace(_txtArgs.Text) ? null : _txtArgs.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
