using System.Diagnostics;
using System.Reflection;
using MyGameConsole.Forms;
using MyGameConsole.Models;
using MyGameConsole.Services;

namespace MyGameConsole.App;

/// <summary>
/// Contexto principal da aplicação: mantém o ícone na bandeja, o menu de contexto,
/// a tecla de atalho global, a tela do console e o timer de monitoramento (controles, Steam).
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppTitle = "My Game Console";

    private readonly SettingsService _settings = new();
    private readonly SteamService _steam;
    private readonly SteamLibraryService _library;
    private readonly ControllerService _controllers;
    private readonly ShellService _shell = new();
    private readonly PowerService _power = new();
    private readonly DisplayService _display = new();
    private readonly KeyboardBacklightService _backlight = new();
    private readonly StartupService _startup = new();
    private readonly DesktopTweaksService _tweaks = new();
    private readonly LockScreenService _lockScreen = new();
    private readonly ConsoleModeService _consoleMode;
    private readonly GameModeService _gameMode;
    private readonly HotkeyService _hotkey = new();
    private readonly HotkeyService _recordHotkey = new();
    private readonly ControllerComboService _controllerCombo;
    private readonly ControllerMouseService _mouse;
    private readonly VirtualKeyboardService _keyboard;
    private readonly UpdateService _updates = new();
    private readonly ScreenRecorderService _recorder;

    private readonly NotifyIcon _tray;
    private readonly Icon _appIcon;
    private Icon? _recordingIcon;
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _updateCheckTimer;

    private SettingsForm? _settingsForm;
    private ConsoleForm? _console;
    private UpdateForm? _updateForm;
    private bool _balloonOpensUpdate;

    public TrayApplicationContext()
    {
        _settings.Load();
        _controllers = new ControllerService(_settings);
        _steam = new SteamService(_settings);
        _library = new SteamLibraryService(_steam);
        _consoleMode = new ConsoleModeService(_settings, _shell, _steam);
        _gameMode = new GameModeService(_settings, _tweaks, _power, _startup, _lockScreen);
        _controllerCombo = new ControllerComboService(_controllers);
        _mouse = new ControllerMouseService(_settings, _controllers);
        _keyboard = new VirtualKeyboardService(_settings);
        _recorder = new ScreenRecorderService(_settings);

        _appIcon = LoadAppIcon();
        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = AppTitle,
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _tray.DoubleClick += (_, _) => Safe(ShowConsole);
        _tray.BalloonTipClicked += (_, _) => { if (_balloonOpensUpdate) { _balloonOpensUpdate = false; Safe(ShowUpdateForm); } };
        _tray.BalloonTipClosed += (_, _) => _balloonOpensUpdate = false;
        _menu.Opening += (_, _) => BuildMenu();

        _consoleMode.StateChanged += (_, _) => UpdateTrayText();
        _gameMode.StateChanged += (_, _) => UpdateTrayText();
        _controllers.CountChanged += OnControllerCountChanged;
        _settings.Changed += (_, _) => OnSettingsChanged();
        _hotkey.Pressed += (_, _) => Safe(ShowConsole);
        _controllerCombo.Triggered += (_, combo) => OnControllerComboTriggered(combo);
        _mouse.StateChanged += (_, _) => OnMouseStateChanged();
        _recordHotkey.Pressed += (_, _) => ToggleRecording();
        _recorder.StateChanged += (_, _) => OnRecordingStateChanged();
        // Suspender no meio de uma gravação a deixaria horas parada na mesma imagem: fecha o arquivo antes.
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _pollTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _pollTimer.Tick += (_, _) => Poll();
        _pollTimer.Start();

        // Verificação automática de atualização, alguns segundos após o início (não atrasa o boot nem o Big Picture).
        _updateCheckTimer = new System.Windows.Forms.Timer { Interval = 20000 };
        _updateCheckTimer.Tick += (_, _) =>
        {
            _updateCheckTimer.Stop();
            if (_settings.Current.CheckForUpdatesOnStart) _ = CheckForUpdatesAsync(silent: true);
        };
        _updateCheckTimer.Start();

        Application.ApplicationExit += (_, _) => OnAppExit();

        // Entrar direto no console: a tela abre antes de todo o resto e cobre a área de trabalho enquanto o
        // Windows termina de carregar. Quem liga o PC vai do "Bem-vindo" direto para o console.
        if (_settings.Current.OpenLauncherOnStart || _gameMode.BootsToConsole)
        {
            Safe(ShowConsole);
        }

        SyncStartupRegistration();
        RegisterHotkey();
        RegisterRecordingHotkey();
        SyncControllerFeatures();
        Poll();

        // Modo Game é persistente: reaplica os ajustes a cada início (inclusive após reiniciar o PC),
        // sem pedir UAC: o que exigir administrador aparece como pendente no checklist da tela do console.
        // No boot o app pode abrir antes da barra de tarefas (que alguns ajustes usam): espera por ela.
        ReapplyGameModeWhenShellReady();

        if (_settings.Current.OpenBigPictureOnStart)
        {
            Safe(_steam.OpenBigPicture);
        }

        // Início silencioso: nenhum balão na bandeja. As formas de abrir a tela do console
        // (tecla de atalho e gesto do controle) aparecem no menu e na dica do item "Abrir tela do console".
    }

    /// <summary>Tempo máximo esperando a barra de tarefas no boot (no Modo Console ela nem existe).</summary>
    private static readonly TimeSpan ShellWaitLimit = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Reaplica o Modo Game assim que a barra de tarefas existir (na hora, se já existe). Quando ela aparece
    /// depois do app, traz a tela do console de volta para a frente: a barra também fica sempre visível e,
    /// chegando por último, cobriria a parte de baixo da tela.
    /// </summary>
    private void ReapplyGameModeWhenShellReady()
    {
        static bool ShellReady() => Native.NativeMethods.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;

        if (ShellReady())
        {
            Safe(() => _gameMode.ReapplyIfEnabled(interactive: false));
            return;
        }

        var giveUpAt = DateTime.UtcNow + ShellWaitLimit;
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            if (!ShellReady() && DateTime.UtcNow < giveUpAt) return;
            timer.Dispose();
            Safe(() => _gameMode.ReapplyIfEnabled(interactive: false));
            if (_console is { IsDisposed: false, Visible: true }) _console.KeepOnTop();
        };
        timer.Start();
    }

    // ------------------------------------------------------------------
    // Menu
    // ------------------------------------------------------------------

    private void BuildMenu()
    {
        _menu.Items.Clear();

        // Status
        var steamStatus = !_steam.IsInstalled ? "Steam: não encontrado"
            : _steam.IsBigPictureActive ? "Steam: Big Picture ativo"
            : _steam.IsRunning ? "Steam: em execução"
            : "Steam: fechado";

        var padStatus = _controllers.ConnectedCount switch
        {
            0 => "Nenhum controle conectado",
            1 => "1 controle conectado",
            var n => $"{n} controles conectados",
        };

        _menu.Items.Add(new ToolStripMenuItem(steamStatus) { Enabled = false });
        _menu.Items.Add(new ToolStripMenuItem(padStatus) { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());

        // Tela do console
        var hotkey = _settings.Current.LauncherHotkey;
        var openConsole = new ToolStripMenuItem(string.IsNullOrWhiteSpace(hotkey)
            ? "Abrir tela do console"
            : $"Abrir tela do console\t{hotkey}")
        {
            Font = new Font(_menu.Font, FontStyle.Bold),
            ToolTipText = _settings.Current.OpenLauncherWithControllerCombo
                ? "No controle: segure − e + (Back + Start) por meio segundo."
                : null,
        };
        openConsole.Click += (_, _) => Safe(ShowConsole);
        _menu.Items.Add(openConsole);

        // Modo Game (persistente)
        var gameMode = new ToolStripMenuItem("Modo Game")
        {
            Checked = _gameMode.IsEnabled,
            ToolTipText = "Barra de tarefas auto-ocultar, ícones escondidos e papel de parede do console. " +
                          "Fica aplicado mesmo após reiniciar; desmarque para restaurar a área de trabalho.",
        };
        gameMode.Click += (_, _) => Safe(_gameMode.Toggle);
        _menu.Items.Add(gameMode);

        // Modo Console (sem explorer)
        var consoleItem = new ToolStripMenuItem("Modo Console (sem explorer)")
        {
            Checked = _consoleMode.IsActive,
            CheckOnClick = false,
            ToolTipText = "Encerra o explorer.exe e abre o Big Picture. Marque novamente para sair.",
        };
        consoleItem.Click += (_, _) => Safe(_consoleMode.Toggle);
        _menu.Items.Add(consoleItem);

        // Mouse pelo analógico e teclado virtual (os mesmos da tela "Controle" e dos atalhos X + A e Y + B)
        var mouseItem = new ToolStripMenuItem("Mouse pelo analógico")
        {
            Checked = _mouse.IsEnabled,
            ToolTipText = "O analógico direito move o cursor; A clica, X abre o menu de contexto. No controle: segure X + A.",
        };
        mouseItem.Click += (_, _) => Safe(_mouse.Toggle);
        _menu.Items.Add(mouseItem);

        var keyboardItem = new ToolStripMenuItem("Teclado virtual")
        {
            Checked = _keyboard.IsVisible,
            ToolTipText = "Mostra o teclado na tela para digitar com o controle. No controle: segure Y + B.",
        };
        keyboardItem.Click += (_, _) => Safe(_keyboard.Toggle);
        _menu.Items.Add(keyboardItem);

        // Gravação da tela (o mesmo do cartão da tela do console, do atalho − + A e da tecla de atalho)
        var recordHotkey = _settings.Current.RecordingHotkey;
        var recordText = _recorder.IsRecording
            ? $"Parar gravação ({FormatElapsed(_recorder.Elapsed)})"
            : "Gravar a tela";
        var recordItem = new ToolStripMenuItem(string.IsNullOrWhiteSpace(recordHotkey) ? recordText : $"{recordText}\t{recordHotkey}")
        {
            Checked = _recorder.IsRecording,
            ToolTipText = "Grava a tela em que está o jogo, com o som do PC, em MP4. No controle: segure − e A por 1,5 segundo.",
        };
        recordItem.Click += (_, _) => ToggleRecording();
        var recordingsFolder = new ToolStripMenuItem("Abrir pasta das gravações");
        recordingsFolder.Click += (_, _) => Safe(OpenRecordingsFolder);
        _menu.Items.Add(recordItem);
        _menu.Items.Add(recordingsFolder);

        _menu.Items.Add(new ToolStripSeparator());

        var bigPicture = new ToolStripMenuItem("Abrir Steam Big Picture") { Enabled = _steam.IsInstalled };
        bigPicture.Click += (_, _) => Safe(_steam.OpenBigPicture);
        _menu.Items.Add(bigPicture);

        var closeBigPicture = new ToolStripMenuItem("Fechar Big Picture") { Enabled = _steam.IsBigPictureActive };
        closeBigPicture.Click += (_, _) => Safe(_steam.CloseBigPicture);
        _menu.Items.Add(closeBigPicture);

        var shellItem = new ToolStripMenuItem(_shell.IsShellRunning
            ? "Esconder barra de tarefas (explorer)"
            : "Restaurar barra de tarefas (explorer)");
        shellItem.Click += (_, _) => Safe(() =>
        {
            if (_shell.IsShellRunning) _shell.StopShell();
            else _shell.StartShell();
        });
        _menu.Items.Add(shellItem);

        _menu.Items.Add(new ToolStripSeparator());

        // Atalhos personalizados
        var shortcuts = _settings.Current.Shortcuts;
        if (shortcuts.Count > 0)
        {
            var shortcutsMenu = new ToolStripMenuItem("Atalhos");
            foreach (var sc in shortcuts)
            {
                var item = new ToolStripMenuItem(sc.Name) { ToolTipText = sc.Path };
                item.Click += (_, _) => Safe(() => LaunchShortcut(sc));
                shortcutsMenu.DropDownItems.Add(item);
            }
            _menu.Items.Add(shortcutsMenu);
        }

        // Energia
        var powerMenu = new ToolStripMenuItem("Energia");
        powerMenu.DropDownItems.Add("Suspender", null, (_, _) => Safe(_power.Sleep));
        powerMenu.DropDownItems.Add("Hibernar", null, (_, _) => Safe(_power.Hibernate));
        powerMenu.DropDownItems.Add(new ToolStripSeparator());
        powerMenu.DropDownItems.Add("Reiniciar...", null, (_, _) => ConfirmThen("reiniciar o computador", _power.Restart));
        powerMenu.DropDownItems.Add("Desligar...", null, (_, _) => ConfirmThen("desligar o computador", _power.Shutdown));
        _menu.Items.Add(powerMenu);

        _menu.Items.Add(new ToolStripSeparator());

        // Opções rápidas
        var startWithWindows = new ToolStripMenuItem("Iniciar com o Windows")
        {
            Checked = _settings.Current.StartWithWindows,
        };
        startWithWindows.Click += (_, _) =>
            _settings.Update(s => s.StartWithWindows = !s.StartWithWindows);
        _menu.Items.Add(startWithWindows);

        var autoBp = new ToolStripMenuItem("Abrir Big Picture ao conectar controle")
        {
            Checked = _settings.Current.OpenBigPictureOnControllerConnect,
        };
        autoBp.Click += (_, _) =>
            _settings.Update(s => s.OpenBigPictureOnControllerConnect = !s.OpenBigPictureOnControllerConnect);
        _menu.Items.Add(autoBp);

        _menu.Items.Add("Configurações...", null, (_, _) => ShowSettings());

        // Atualização: vira "Atualizar para X" (em negrito) assim que uma versão nova é encontrada.
        var update = _updates.Available;
        var updateItem = new ToolStripMenuItem(update is null
            ? $"Verificar atualizações...\tv{_updates.CurrentVersionText}"
            : $"Atualizar para a versão {update.VersionText}...")
        {
            Enabled = !_updates.IsBusy || update is not null,
            ToolTipText = _updates.StatusText,
        };
        if (update is not null) updateItem.Font = new Font(_menu.Font, FontStyle.Bold);
        updateItem.Click += (_, _) =>
        {
            if (_updates.Available is not null) Safe(ShowUpdateForm);
            else _ = CheckForUpdatesAsync(silent: false);
        };
        _menu.Items.Add(updateItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Sair", null, (_, _) => ExitApplication());
    }

    // ------------------------------------------------------------------
    // Monitoramento
    // ------------------------------------------------------------------

    private void Poll()
    {
        _controllers.Poll();
        UpdateTrayText();
    }

    private void OnControllerCountChanged(int oldCount, int newCount)
    {
        if (newCount > oldCount)
        {
            if (_settings.Current.OpenBigPictureOnControllerConnect
                && oldCount == 0
                && _steam.IsInstalled
                && !_steam.IsBigPictureActive)
            {
                Safe(_steam.OpenBigPicture);
            }
        }
    }

    private void UpdateTrayText()
    {
        var mode = _consoleMode.IsActive ? "Modo Console"
            : _gameMode.IsEnabled ? "Modo Game"
            : "Modo Desktop";
        var pads = _controllers.ConnectedCount;
        var text = $"{AppTitle} — {mode}\n{pads} controle(s)";
        if (_recorder.IsRecording) text += $"\nGravando a tela ({FormatElapsed(_recorder.Elapsed)})";
        // NotifyIcon.Text tem limite de 127 caracteres.
        _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    private void OnSettingsChanged()
    {
        SyncStartupRegistration();
        RegisterHotkey();
        RegisterRecordingHotkey();
        SyncControllerFeatures();
        // Se as opções do Modo Game mudaram enquanto ele está ativo, aplica na hora (o usuário está na tela: pode pedir UAC).
        Safe(() => _gameMode.ReapplyIfEnabled(interactive: true));
        UpdateTrayText();
    }

    // ------------------------------------------------------------------
    // Ações auxiliares
    // ------------------------------------------------------------------

    private void ShowConsole()
    {
        if (_console is null || _console.IsDisposed)
        {
            _console = new ConsoleForm(
                _settings, _steam, _library, _gameMode, _power, _display, _backlight, _controllers, _mouse, _keyboard, _updates, _recorder,
                openSettings: ShowSettings,
                toggleRecording: ToggleRecording,
                launchShortcut: LaunchShortcut,
                exitApp: ExitApplication,
                restartElevated: RestartElevated);

            // Na tela do console o controle navega a própria tela: o mouse pelo analógico fica em pausa.
            _console.VisibleChanged += (_, _) => _mouse.Suspended = _console is { IsDisposed: false, Visible: true };
        }

        _console.ShowLauncher();
    }

    private static void LaunchShortcut(AppShortcut sc)
    {
        if (string.IsNullOrWhiteSpace(sc.Path))
        {
            throw new InvalidOperationException($"O atalho \"{sc.Name}\" não tem caminho configurado.");
        }

        var workingDirectory = File.Exists(sc.Path) ? Path.GetDirectoryName(sc.Path) : null;
        ProcessLauncher.Start(sc.Path, sc.Arguments, workingDirectory);
    }

    /// <summary>Reabre o app como administrador (UAC) e encerra esta instância.</summary>
    private void RestartElevated()
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Não foi possível determinar o caminho do executável.");

        try
        {
            Process.Start(new ProcessStartInfo(exe, $"--wait-for-pid {Environment.ProcessId}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe),
            });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Permissão recusada no UAC. O app continua sem privilégios.");
        }

        ExitApplication();
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings, _steam, _controllers);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    // ------------------------------------------------------------------
    // Atualizações (releases no GitHub)
    // ------------------------------------------------------------------

    /// <summary>
    /// Consulta a release mais recente. Em modo silencioso (início do app) só avisa se houver versão nova;
    /// clicando no aviso, abre a janela de atualização. Em modo manual, sempre dá um retorno.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool silent)
    {
        try
        {
            var info = await _updates.CheckAsync();

            if (info is null)
            {
                if (!silent)
                {
                    _tray.ShowBalloonTip(4000, AppTitle,
                        $"Você já está na versão mais recente ({_updates.CurrentVersionText}).", ToolTipIcon.Info);
                }
                return;
            }

            if (silent)
            {
                _balloonOpensUpdate = true;
                _tray.ShowBalloonTip(10000, AppTitle,
                    $"Nova versão {info.VersionText} disponível (instalada: {_updates.CurrentVersionText}). Clique aqui para atualizar.",
                    ToolTipIcon.Info);
            }
            else
            {
                ShowUpdateForm();
            }
        }
        catch (Exception ex)
        {
            if (!silent) _tray.ShowBalloonTip(6000, AppTitle, ex.Message, ToolTipIcon.Warning);
        }
    }

    private void ShowUpdateForm()
    {
        if (_updateForm is { IsDisposed: false })
        {
            _updateForm.Activate();
            return;
        }

        _updateForm = new UpdateForm(_updates, ExitApplication);
        _updateForm.FormClosed += (_, _) => _updateForm = null;
        _updateForm.Show();
        _updateForm.Activate();
    }

    private (bool Enabled, bool Elevated)? _startupSynced;

    private void SyncStartupRegistration()
    {
        var wanted = (_settings.Current.StartWithWindows, _settings.Current.StartElevated);
        if (_startupSynced == wanted) return; // evita consultar o Agendador a cada mudança de configuração

        try
        {
            _startup.Set(wanted.Item1, wanted.Item2);
            _startupSynced = wanted;
        }
        catch (Exception ex) when (wanted.Item2)
        {
            // Sem o aval do UAC não há tarefa elevada: volta para a inicialização comum e avisa.
            _startupSynced = null;
            _settings.Update(s => s.StartElevated = false);
            _tray.ShowBalloonTip(6000, AppTitle,
                $"{ex.Message} A inicialização com o Windows foi mantida sem privilégios de administrador.",
                ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(4000, AppTitle, ex.Message, ToolTipIcon.Error);
        }
    }

    /// <summary>Atalhos do controle ligados e leitura do analógico para o mouse, conforme as configurações.</summary>
    private void SyncControllerFeatures()
    {
        _controllerCombo.SetActive(ControllerCombo.EnabledIn(_settings.Current));
        _mouse.Sync();
    }

    /// <summary>
    /// Aviso na tela ao ligar ou desligar o mouse pelo analógico, venha a mudança do gesto X + A, do menu da
    /// bandeja ou da tela do console: sem ele não dá para saber em que estado o controle está. O teclado virtual
    /// não precisa de aviso — ele próprio aparece e some na tela.
    /// </summary>
    private void OnMouseStateChanged()
    {
        // Com a tela do console aberta, o aviso sai nela mesma (rodapé), no lugar do flutuante.
        if (_console is { IsDisposed: false, Visible: true }) return;

        bool on = _mouse.IsEnabled;
        ToastForm.Show(
            on ? "Mouse pelo analógico ligado" : "Mouse pelo analógico desligado",
            Theme.GlyphMouse,
            on ? Theme.Success : Theme.Danger,
            crossed: !on);
    }

    // ------------------------------------------------------------------
    // Gravação da tela
    // ------------------------------------------------------------------

    /// <summary>Segundos de contagem antes de gravar.</summary>
    private const int RecordingCountdownSeconds = 3;

    private System.Windows.Forms.Timer? _countdown;
    private int _countdownLeft;

    /// <summary>
    /// Começa ou para a gravação (bandeja, tecla de atalho, − + A no controle, cartão da tela do console). Começar
    /// passa por uma contagem "3, 2, 1": o aviso diz que a gravação vai começar e some antes do primeiro quadro, então
    /// não aparece no vídeo. Acionar de novo durante a contagem a cancela.
    /// </summary>
    private void ToggleRecording()
    {
        if (_countdown is not null)
        {
            StopCountdown();
            ShowRecordingMessage("Gravação cancelada", Theme.PillOff);
            return;
        }

        if (_recorder.IsRecording)
        {
            Safe(_recorder.Stop); // o aviso de "salva" sai em OnRecordingStateChanged
            return;
        }

        _countdownLeft = RecordingCountdownSeconds;
        ShowCountdown();
        _countdown = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdown.Tick += (_, _) =>
        {
            if (--_countdownLeft > 0)
            {
                ShowCountdown();
                return;
            }

            StopCountdown();
            Safe(_recorder.Start);
        };
        _countdown.Start();
    }

    private void ShowCountdown()
    {
        if (_console is { IsDisposed: false, Visible: true })
        {
            _console.ShowRecordingCountdown(_countdownLeft);
        }
        else
        {
            ToastForm.Show($"A gravação da tela começa em {_countdownLeft}", _countdownLeft.ToString(), Theme.Danger, glyphIsText: true);
        }
    }

    /// <summary>Para a contagem e tira o aviso da tela na hora.</summary>
    private void StopCountdown()
    {
        _countdown?.Dispose();
        _countdown = null;
        ToastForm.CloseCurrent();
        if (_console is { IsDisposed: false, Visible: true }) _console.ClearNoticeNow();
    }

    /// <summary>Aviso da gravação: no rodapé da tela do console, se ela estiver aberta, ou no aviso flutuante.</summary>
    private void ShowRecordingMessage(string text, Color accent)
    {
        if (_console is { IsDisposed: false, Visible: true }) return; // a tela do console avisa por conta própria
        ToastForm.Show(text, Theme.GlyphRecord, accent);
    }

    /// <summary>
    /// Ícone da bandeja com a bolinha vermelha enquanto grava e o aviso de gravação salva (ou do erro que a parou).
    /// </summary>
    private void OnRecordingStateChanged()
    {
        bool recording = _recorder.IsRecording;
        _tray.Icon = recording ? _recordingIcon ??= CreateRecordingIcon(_appIcon) : _appIcon;
        UpdateTrayText();

        var file = Path.GetFileName(_recorder.CurrentFile);
        if (_recorder.LastError is { } error)
        {
            _tray.ShowBalloonTip(8000, AppTitle, $"A gravação parou: {error} O que foi gravado até ali ficou em {file}.", ToolTipIcon.Error);
            return;
        }

        // O começo não tem aviso: a contagem já avisou, e um aviso agora sairia no vídeo. O fim avisa onde salvou.
        if (!recording) ShowRecordingMessage($"Gravação salva: {file}", Theme.Success);
    }

    private void OpenRecordingsFolder()
    {
        Directory.CreateDirectory(_recorder.Folder);
        ProcessLauncher.Start(_recorder.Folder);
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend) _recorder.Stop();
    }

    internal static string FormatElapsed(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    /// <summary>O ícone do app com uma bolinha vermelha no canto, como o "REC" das câmeras.</summary>
    private static Icon CreateRecordingIcon(Icon source)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var sized = new Icon(source, 32, 32)) g.DrawIcon(sized, new Rectangle(0, 0, 32, 32));
            using var ring = new SolidBrush(Color.White);
            using var dot = new SolidBrush(Theme.Danger);
            g.FillEllipse(ring, 15, 15, 17, 17);
            g.FillEllipse(dot, 17, 17, 13, 13);
        }

        var handle = bitmap.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        Native.NativeMethods.DestroyIcon(handle);
        return icon;
    }

    private void OnControllerComboTriggered(ControllerCombo combo)
    {
        // Com a tela do console aberta, ela mesma trata o controle; os atalhos valem no Windows.
        if (_console is { IsDisposed: false, Visible: true }) return;

        switch (combo.Id)
        {
            case ControllerCombo.OpenConsole:
                Safe(ShowConsole);
                break;

            case ControllerCombo.ToggleMouse:
                Safe(_mouse.Toggle); // o aviso na tela sai em OnMouseStateChanged, venha de onde vier a mudança
                break;

            case ControllerCombo.ToggleKeyboard:
                Safe(_keyboard.Toggle);
                break;

            case ControllerCombo.ToggleRecording:
                ToggleRecording(); // o aviso sai em OnRecordingStateChanged
                break;
        }
    }

    private void RegisterHotkey()
    {
        var hotkey = _settings.Current.LauncherHotkey;
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            _hotkey.Unregister();
            return;
        }

        if (string.Equals(hotkey, _hotkey.RegisteredHotkey, StringComparison.OrdinalIgnoreCase)) return;

        if (!_hotkey.Register(hotkey))
        {
            _tray.ShowBalloonTip(4000, AppTitle,
                $"Não foi possível registrar o atalho \"{hotkey}\". Ele pode ser inválido ou estar em uso por outro programa.",
                ToolTipIcon.Warning);
        }
    }

    private void RegisterRecordingHotkey()
    {
        var hotkey = _settings.Current.RecordingHotkey;
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            _recordHotkey.Unregister();
            return;
        }

        if (string.Equals(hotkey, _recordHotkey.RegisteredHotkey, StringComparison.OrdinalIgnoreCase)) return;

        if (!_recordHotkey.Register(hotkey))
        {
            _tray.ShowBalloonTip(4000, AppTitle,
                $"Não foi possível registrar o atalho de gravação \"{hotkey}\". Ele pode ser inválido ou estar em uso por outro programa.",
                ToolTipIcon.Warning);
        }
    }

    private void ConfirmThen(string action, Action run)
    {
        var result = MessageBox.Show(
            $"Tem certeza que deseja {action}?",
            AppTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result == DialogResult.Yes)
        {
            Safe(run);
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
            _tray.ShowBalloonTip(4000, AppTitle, ex.Message, ToolTipIcon.Error);
        }
    }

    private void ExitApplication()
    {
        _pollTimer.Stop();
        _updateCheckTimer.Stop();
        _controllerCombo.SetActive([]);
        _mouse.Suspended = true; // solta qualquer botão do mouse que esteja pressionado
        _countdown?.Dispose();
        _countdown = null;
        _recorder.Stop(); // fecha o MP4: sem o índice no fim, o arquivo não abre
        ToastForm.CloseCurrent();
        _hotkey.Dispose();
        _recordHotkey.Dispose();
        _consoleMode.EnsureShellRestored();
        _tray.Visible = false;
        ExitThread();
    }

    private void OnAppExit()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _recorder.Stop(); // saída sem passar pelo "Sair" (fim da sessão do Windows)
        _tray.Visible = false;
        _tray.Dispose();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
            if (stream is not null)
            {
                return new Icon(stream);
            }
        }
        catch
        {
            // cai no ícone padrão
        }

        return SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Dispose();
            _updateCheckTimer.Dispose();
            _updateForm?.Dispose();
            _controllerCombo.Dispose();
            _mouse.Dispose();
            _controllers.Dispose();
            _hotkey.Dispose();
            _recordHotkey.Dispose();
            _recorder.Dispose();
            _recordingIcon?.Dispose();
            _menu.Dispose();
            _tray.Dispose();
            _settingsForm?.Dispose();
            _console?.Dispose();
        }

        base.Dispose(disposing);
    }
}
