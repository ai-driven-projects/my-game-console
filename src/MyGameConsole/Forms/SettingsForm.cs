using MyGameConsole.Models;
using MyGameConsole.Services;

namespace MyGameConsole.Forms;

/// <summary>
/// Janela de configurações. Construída em código (sem designer) para facilitar manutenção.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly SettingsService _settings;
    private readonly SteamService _steam;
    private readonly ControllerService _controllers;

    private readonly Label _lblControllers = new() { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(560, 0) };
    private readonly System.Windows.Forms.Timer _controllersTimer = new() { Interval = 200 };

    private readonly CheckBox _chkStartWithWindows = new() { Text = "Iniciar junto com o Windows", AutoSize = true };
    private readonly CheckBox _chkStartElevated = new() { Text = "Iniciar como administrador (tarefa agendada; luz do teclado Lenovo; pede UAC uma vez)", AutoSize = true };
    private readonly CheckBox _chkLauncherOnStart = new() { Text = "Abrir a tela do console ao iniciar o app", AutoSize = true };
    private readonly CheckBox _chkBpOnStart = new() { Text = "Abrir Steam Big Picture ao iniciar o app", AutoSize = true };
    private readonly CheckBox _chkBpOnController = new() { Text = "Abrir Big Picture ao conectar um controle", AutoSize = true };
    private readonly CheckBox _chkHideExplorer = new() { Text = "Modo Console: encerrar o explorer.exe (barra e área de trabalho)", AutoSize = true };
    private readonly CheckBox _chkBpInConsole = new() { Text = "Modo Console: abrir Big Picture automaticamente", AutoSize = true };
    private readonly CheckBox _chkCheckUpdates = new() { Text = "Verificar atualizações ao iniciar o app (releases no GitHub)", AutoSize = true };

    private readonly TextBox _txtHotkey = new() { Dock = DockStyle.Fill, ReadOnly = true, Cursor = Cursors.Hand };
    private readonly CheckBox _chkControllerCombo = new() { Text = "Abrir a tela do console segurando − e + (Back + Start) no controle", AutoSize = true };

    private readonly CheckBox _chkGmTaskbar = new() { Text = "Barra de tarefas em auto-ocultar (aparece ao levar o cursor para baixo)", AutoSize = true };
    private readonly CheckBox _chkGmIcons = new() { Text = "Esconder os ícones da área de trabalho", AutoSize = true };
    private readonly CheckBox _chkGmWallpaper = new() { Text = "Aplicar papel de parede do console", AutoSize = true };
    private readonly CheckBox _chkGmNoWakePassword = new() { Text = "Entrar sem senha ao acordar da suspensão (pede o UAC uma vez; Win+L continua pedindo senha)", AutoSize = true };
    private readonly CheckBox _chkGmNoSetupPrompts = new() { Text = "Não mostrar a tela \"Vamos concluir a configuração do seu dispositivo\"", AutoSize = true };
    private readonly TextBox _txtWallpaper = new() { Dock = DockStyle.Fill };

    private readonly TextBox _txtSteamPath = new() { Dock = DockStyle.Fill };
    private readonly Label _lblSteamDetected = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private readonly ListBox _lstShortcuts = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public SettingsForm(SettingsService settings, SteamService steam, ControllerService controllers)
    {
        _settings = settings;
        _steam = steam;
        _controllers = controllers;

        Text = "My Game Console — Configurações";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 840);
        Font = new Font("Segoe UI", 9.5f);

        BuildLayout();
        LoadValues();

        _controllersTimer.Tick += (_, _) => UpdateControllersLabel();
        UpdateControllersLabel();
        _controllersTimer.Start();
    }

    /// <summary>Mostra os controles vistos pelo app e, nos HID, os números dos botões pressionados (para acertar o mapeamento).</summary>
    private void UpdateControllersLabel()
    {
        var lines = _controllers.DescribeControllers().ToList();
        var map = _settings.Current.HidButtons ?? new HidButtonMap();
        var text = lines.Count == 0
            ? "Controles: nenhum detectado (XInput ou HID/DirectInput)."
            : "Controles:\n" + string.Join("\n", lines);
        text += $"\nMapeamento HID atual (settings.json → HidButtons): A={map.A} B={map.B} X={map.X} Y={map.Y} Back(−)={map.Back} Start(+)={map.Start}";

        if (_lblControllers.Text != text) _lblControllers.Text = text;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _controllersTimer.Stop();
        _controllersTimer.Dispose();
        base.OnFormClosed(e);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
            AutoSize = true,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // geral
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // modo game
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // steam
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // atalhos
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // botões

        // --- Geral -------------------------------------------------------
        var grpGeneral = new GroupBox { Text = "Geral", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        var generalTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        generalTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        generalTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var flowGeneral = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        flowGeneral.Controls.AddRange([_chkStartWithWindows, _chkStartElevated, _chkLauncherOnStart, _chkBpOnStart, _chkBpOnController, _chkHideExplorer, _chkBpInConsole, _chkCheckUpdates]);
        generalTable.Controls.Add(flowGeneral, 0, 0);
        generalTable.SetColumnSpan(flowGeneral, 2);

        _txtHotkey.KeyDown += OnHotkeyKeyDown;
        _txtHotkey.PlaceholderText = "Clique aqui e pressione a combinação (Backspace limpa)";
        generalTable.Controls.Add(new Label { Text = "Tecla de atalho da tela do console:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, 1);
        generalTable.Controls.Add(_txtHotkey, 1, 1);

        _chkControllerCombo.Margin = new Padding(3, 8, 3, 3);
        generalTable.Controls.Add(_chkControllerCombo, 0, 2);
        generalTable.SetColumnSpan(_chkControllerCombo, 2);

        _lblControllers.Margin = new Padding(3, 8, 3, 3);
        generalTable.Controls.Add(_lblControllers, 0, 3);
        generalTable.SetColumnSpan(_lblControllers, 2);

        grpGeneral.Controls.Add(generalTable);
        root.Controls.Add(grpGeneral, 0, 0);

        // --- Modo Game ---------------------------------------------------
        var grpGame = new GroupBox { Text = "Modo Game (persistente, continua após reiniciar)", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        var gameTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        gameTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gameTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gameTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var flowGame = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        flowGame.Controls.AddRange([_chkGmTaskbar, _chkGmIcons, _chkGmWallpaper, _chkGmNoWakePassword, _chkGmNoSetupPrompts]);
        gameTable.Controls.Add(flowGame, 0, 0);
        gameTable.SetColumnSpan(flowGame, 3);

        var btnWallpaper = new Button { Text = "Procurar...", AutoSize = true };
        btnWallpaper.Click += (_, _) => BrowseWallpaper();
        _txtWallpaper.PlaceholderText = "Vazio = papel de parede padrão do app";
        gameTable.Controls.Add(new Label { Text = "Imagem personalizada:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        gameTable.Controls.Add(_txtWallpaper, 1, 1);
        gameTable.Controls.Add(btnWallpaper, 2, 1);

        var lblGameHint = new Label
        {
            Text = "Ative o Modo Game pelo menu da bandeja ou pela tela do console. Ao desativar, a área de trabalho original é restaurada.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(540, 0),
        };
        gameTable.Controls.Add(lblGameHint, 0, 2);
        gameTable.SetColumnSpan(lblGameHint, 3);

        grpGame.Controls.Add(gameTable);
        root.Controls.Add(grpGame, 0, 1);

        // --- Steam -------------------------------------------------------
        var grpSteam = new GroupBox { Text = "Steam", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        var steamTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        steamTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        steamTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        steamTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var btnBrowse = new Button { Text = "Procurar...", AutoSize = true };
        btnBrowse.Click += (_, _) => BrowseSteamPath();

        steamTable.Controls.Add(new Label { Text = "Pasta do Steam (opcional):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        steamTable.Controls.Add(_txtSteamPath, 1, 0);
        steamTable.Controls.Add(btnBrowse, 2, 0);
        steamTable.Controls.Add(_lblSteamDetected, 1, 1);
        steamTable.SetColumnSpan(_lblSteamDetected, 2);
        grpSteam.Controls.Add(steamTable);
        root.Controls.Add(grpSteam, 0, 2);

        // --- Atalhos -----------------------------------------------------
        var grpShortcuts = new GroupBox { Text = "Atalhos (jogos, launchers, apps) — aparecem no menu e na tela do console", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var shortcutsTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        shortcutsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shortcutsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var shortcutButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        var btnAdd = new Button { Text = "Adicionar...", Width = 110 };
        var btnEdit = new Button { Text = "Editar...", Width = 110 };
        var btnRemove = new Button { Text = "Remover", Width = 110 };
        btnAdd.Click += (_, _) => AddShortcut();
        btnEdit.Click += (_, _) => EditShortcut();
        btnRemove.Click += (_, _) => RemoveShortcut();
        _lstShortcuts.DoubleClick += (_, _) => EditShortcut();
        shortcutButtons.Controls.AddRange([btnAdd, btnEdit, btnRemove]);

        shortcutsTable.Controls.Add(_lstShortcuts, 0, 0);
        shortcutsTable.Controls.Add(shortcutButtons, 1, 0);
        grpShortcuts.Controls.Add(shortcutsTable);
        root.Controls.Add(grpShortcuts, 0, 3);

        // --- Botões ------------------------------------------------------
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        var btnCancel = new Button { Text = "Cancelar", Width = 100, DialogResult = DialogResult.Cancel };
        var btnSave = new Button { Text = "Salvar", Width = 100 };
        btnSave.Click += (_, _) => { SaveValues(); Close(); };
        btnCancel.Click += (_, _) => Close();
        buttons.Controls.AddRange([btnCancel, btnSave]);
        root.Controls.Add(buttons, 0, 4);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        Controls.Add(root);
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode is Keys.Back or Keys.Delete)
        {
            _txtHotkey.Text = string.Empty;
            return;
        }

        var text = HotkeyService.Format(e);
        if (text is null) return; // só modificador pressionado

        if (!e.Control && !e.Alt && !e.Shift && e.KeyCode is < Keys.F1 or > Keys.F24)
        {
            // Sem modificador, só aceita teclas de função para não sequestrar teclas comuns.
            return;
        }

        _txtHotkey.Text = text;
    }

    private void LoadValues()
    {
        var s = _settings.Current;
        _chkStartWithWindows.Checked = s.StartWithWindows;
        _chkStartElevated.Checked = s.StartElevated;
        _chkLauncherOnStart.Checked = s.OpenLauncherOnStart;
        _chkBpOnStart.Checked = s.OpenBigPictureOnStart;
        _chkBpOnController.Checked = s.OpenBigPictureOnControllerConnect;
        _chkHideExplorer.Checked = s.HideExplorerInConsoleMode;
        _chkBpInConsole.Checked = s.OpenBigPictureInConsoleMode;
        _chkCheckUpdates.Checked = s.CheckForUpdatesOnStart;
        _txtHotkey.Text = s.LauncherHotkey ?? string.Empty;
        _chkControllerCombo.Checked = s.OpenLauncherWithControllerCombo;

        _chkGmTaskbar.Checked = s.GameModeHideTaskbar;
        _chkGmIcons.Checked = s.GameModeHideDesktopIcons;
        _chkGmWallpaper.Checked = s.GameModeApplyWallpaper;
        _chkGmNoWakePassword.Checked = s.GameModeSkipPasswordOnWake;
        _chkGmNoSetupPrompts.Checked = s.GameModeHideSetupPrompts;
        _txtWallpaper.Text = s.GameModeWallpaperPath ?? string.Empty;

        _txtSteamPath.Text = s.SteamPathOverride ?? string.Empty;

        var detected = _steam.FindSteamPath();
        _lblSteamDetected.Text = detected is null
            ? "Steam não detectado automaticamente."
            : $"Detectado: {detected}";

        _lstShortcuts.Items.Clear();
        foreach (var sc in s.Shortcuts)
        {
            _lstShortcuts.Items.Add(Clone(sc));
        }
    }

    private void SaveValues()
    {
        _settings.Update(s =>
        {
            s.StartWithWindows = _chkStartWithWindows.Checked;
            s.StartElevated = _chkStartElevated.Checked;
            s.OpenLauncherOnStart = _chkLauncherOnStart.Checked;
            s.OpenBigPictureOnStart = _chkBpOnStart.Checked;
            s.OpenBigPictureOnControllerConnect = _chkBpOnController.Checked;
            s.HideExplorerInConsoleMode = _chkHideExplorer.Checked;
            s.OpenBigPictureInConsoleMode = _chkBpInConsole.Checked;
            s.CheckForUpdatesOnStart = _chkCheckUpdates.Checked;
            s.LauncherHotkey = string.IsNullOrWhiteSpace(_txtHotkey.Text) ? null : _txtHotkey.Text.Trim();
            s.OpenLauncherWithControllerCombo = _chkControllerCombo.Checked;

            s.GameModeHideTaskbar = _chkGmTaskbar.Checked;
            s.GameModeHideDesktopIcons = _chkGmIcons.Checked;
            s.GameModeApplyWallpaper = _chkGmWallpaper.Checked;
            s.GameModeSkipPasswordOnWake = _chkGmNoWakePassword.Checked;
            s.GameModeHideSetupPrompts = _chkGmNoSetupPrompts.Checked;
            s.GameModeWallpaperPath = string.IsNullOrWhiteSpace(_txtWallpaper.Text) ? null : _txtWallpaper.Text.Trim();

            s.SteamPathOverride = string.IsNullOrWhiteSpace(_txtSteamPath.Text) ? null : _txtSteamPath.Text.Trim();
            s.Shortcuts = _lstShortcuts.Items.Cast<AppShortcut>().Select(Clone).ToList();
        });
    }

    private void BrowseWallpaper()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Escolha a imagem do papel de parede do Modo Game",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif;*.tif;*.tiff;*.jxr|Todos os arquivos|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _txtWallpaper.Text = dlg.FileName;
        }
    }

    private void BrowseSteamPath()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Selecione a pasta onde o Steam está instalado (contém steam.exe)",
            UseDescriptionForTitle = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _txtSteamPath.Text = dlg.SelectedPath;
        }
    }

    private void AddShortcut()
    {
        using var dlg = new ShortcutEditorForm(new AppShortcut());
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _lstShortcuts.Items.Add(dlg.Shortcut);
        }
    }

    private void EditShortcut()
    {
        if (_lstShortcuts.SelectedItem is not AppShortcut current) return;

        using var dlg = new ShortcutEditorForm(Clone(current));
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var idx = _lstShortcuts.SelectedIndex;
            _lstShortcuts.Items[idx] = dlg.Shortcut;
        }
    }

    private void RemoveShortcut()
    {
        if (_lstShortcuts.SelectedIndex >= 0)
        {
            _lstShortcuts.Items.RemoveAt(_lstShortcuts.SelectedIndex);
        }
    }

    private static AppShortcut Clone(AppShortcut sc) => new()
    {
        Name = sc.Name,
        Path = sc.Path,
        Arguments = sc.Arguments,
    };
}
