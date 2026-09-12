using MyGameConsole.Services;

namespace MyGameConsole.Forms;

/// <summary>
/// Janela clássica (teclado/mouse) de atualização: mostra a versão nova e as notas da release,
/// baixa o instalador com barra de progresso e o executa. Aberta pelo menu da bandeja ou pelo
/// clique no aviso de "nova versão". Na tela do console o mesmo fluxo é feito pelos overlays dela.
/// </summary>
public sealed class UpdateForm : Form
{
    private readonly UpdateService _updates;
    private readonly Action _exitApp;

    private readonly Label _lblTitle = new() { AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold) };
    private readonly Label _lblVersions = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly TextBox _txtNotes = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BackColor = SystemColors.Window,
        Font = new Font("Consolas", 9.5f),
    };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Height = 18 };
    private readonly Label _lblStatus = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(520, 0) };
    private readonly Button _btnInstall = new() { Text = "Baixar e instalar", Width = 150 };
    private readonly Button _btnSite = new() { Text = "Ver no GitHub", Width = 120 };
    private readonly Button _btnLater = new() { Text = "Depois", Width = 100, DialogResult = DialogResult.Cancel };

    private CancellationTokenSource? _cts;

    public UpdateForm(UpdateService updates, Action exitApp)
    {
        _updates = updates;
        _exitApp = exitApp;

        Text = "My Game Console — Atualização";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 420);
        Font = new Font("Segoe UI", 9.5f);

        BuildLayout();
        _updates.StateChanged += OnStateChanged;
        Refresh(fromEvent: false);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // título
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // versões
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // notas
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // progresso
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // botões

        root.Controls.Add(_lblTitle, 0, 0);
        _lblVersions.Margin = new Padding(3, 0, 3, 8);
        root.Controls.Add(_lblVersions, 0, 1);

        var grpNotes = new GroupBox { Text = "Novidades", Dock = DockStyle.Fill, Padding = new Padding(8) };
        grpNotes.Controls.Add(_txtNotes);
        root.Controls.Add(grpNotes, 0, 2);

        _progress.Margin = new Padding(3, 10, 3, 3);
        root.Controls.Add(_progress, 0, 3);
        root.Controls.Add(_lblStatus, 0, 4);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        _btnInstall.Click += async (_, _) => await DownloadAndInstallAsync();
        _btnSite.Click += (_, _) => Safe(_updates.OpenReleasePage);
        _btnLater.Click += (_, _) => Close();
        buttons.Controls.AddRange([_btnLater, _btnSite, _btnInstall]);
        root.Controls.Add(buttons, 0, 5);

        AcceptButton = _btnInstall;
        CancelButton = _btnLater;
        Controls.Add(root);
    }

    private void OnStateChanged(object? sender, EventArgs e) => Refresh(fromEvent: true);

    private void Refresh(bool fromEvent)
    {
        if (IsDisposed) return;

        var info = _updates.Available;
        _lblTitle.Text = info is null
            ? "Nenhuma atualização disponível"
            : $"Nova versão {info.VersionText} disponível";
        _lblVersions.Text = info is null
            ? $"Versão instalada: {_updates.CurrentVersionText}"
            : $"Versão instalada: {_updates.CurrentVersionText}   →   Nova: {info.VersionText}   ({FormatSize(info.MsiSize)})";

        var notes = info?.Notes;
        _txtNotes.Text = string.IsNullOrWhiteSpace(notes) ? "(a release não tem notas)" : notes.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        var state = _updates.State;
        _progress.Value = (int)Math.Round(_updates.Progress * 100);
        _progress.Visible = state is UpdateState.Downloading or UpdateState.ReadyToInstall;
        _lblStatus.Text = state switch
        {
            UpdateState.Downloading => $"Baixando o instalador... {_updates.Progress:P0}",
            UpdateState.ReadyToInstall => "Instalador baixado e conferido. Ao instalar, o My Game Console será fechado e reaberto ao final.",
            UpdateState.Failed => _updates.Error ?? "Falhou.",
            _ => "Ao instalar, o My Game Console será fechado e reaberto ao final.",
        };
        _lblStatus.ForeColor = state == UpdateState.Failed ? Color.Firebrick : SystemColors.GrayText;

        _btnInstall.Text = state == UpdateState.ReadyToInstall ? "Instalar agora" : "Baixar e instalar";
        _btnInstall.Enabled = info is not null && state is UpdateState.Available or UpdateState.ReadyToInstall or UpdateState.Failed;

        if (fromEvent && state == UpdateState.Failed) _btnLater.Focus();
    }

    private async Task DownloadAndInstallAsync()
    {
        _btnInstall.Enabled = false;
        _cts = new CancellationTokenSource();
        try
        {
            if (_updates.State != UpdateState.ReadyToInstall)
            {
                await _updates.DownloadAsync(_cts.Token);
            }

            _updates.Install();
            Close();
            _exitApp();
        }
        catch (OperationCanceledException)
        {
            // fechado durante o download
        }
        catch (Exception ex)
        {
            _lblStatus.Text = ex.Message;
            _lblStatus.ForeColor = Color.Firebrick;
            _btnInstall.Enabled = true;
        }
    }

    private void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = ex.Message;
            _lblStatus.ForeColor = Color.Firebrick;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _updates.StateChanged -= OnStateChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnFormClosed(e);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        <= 0 => "tamanho desconhecido",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.0} MB",
    };
}
