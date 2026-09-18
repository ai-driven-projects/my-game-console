using System.Drawing.Drawing2D;
using System.Drawing.Text;
using MyGameConsole.App;
using MyGameConsole.Models;
using MyGameConsole.Native;
using MyGameConsole.Services;

namespace MyGameConsole.Forms;

/// <summary>
/// Tela do console: launcher em tela cheia, estilo PlayStation / Big Picture, navegável com
/// controle (XInput ou HID/DirectInput), teclado e mouse. Toda a interface é desenhada em <see cref="OnPaint"/>.
/// A janela nunca é destruída ao fechar: apenas fica oculta, para reabrir rápido pelo atalho.
/// </summary>
public sealed partial class ConsoleForm : Form
{
    private sealed class Tile
    {
        public required string Glyph { get; init; }
        /// <summary>Imagem desenhada no lugar do glifo (ex.: ícone do Steam). O glifo vira reserva.</summary>
        public Image? Image { get; init; }
        public required string Title { get; init; }
        public string Subtitle { get; init; } = string.Empty;
        public required Action OnSelect { get; init; }
        public Func<bool>? IsOn { get; init; }
        /// <summary>Jogo da biblioteca do Steam: o tile vira a capa vertical dele.</summary>
        public SteamGame? Game { get; init; }
        /// <summary>Largura / altura do tile (1 = quadrado; capas de jogo são 2:3).</summary>
        public float Aspect { get; init; } = 1f;
        public RectangleF Bounds { get; set; }
    }

    private sealed class Row
    {
        public required string Title { get; init; }
        public List<Tile> Tiles { get; } = [];
        public int Scroll { get; set; }
        /// <summary>Altura dos tiles em relação ao tamanho padrão (a fileira de jogos é mais alta).</summary>
        public float Scale { get; init; } = 1f;
        /// <summary>Botões pequenos de energia no alto da tela, desenhados pelo cabeçalho e não como fileira.</summary>
        public bool IsTopBar { get; init; }
    }

    private const float GameCapsuleAspect = 2f / 3f;
    private const float GamesRowScale = 1.3f;

    private const short StickDeadZone = 16000;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(140);

    private readonly SettingsService _settings;
    private readonly SteamService _steam;
    private readonly SteamLibraryService _library;
    private readonly GameArtCache _art = new();
    private readonly GameModeService _gameMode;
    private readonly PowerService _power;
    private readonly DisplayService _display;
    private readonly KeyboardBacklightService _backlight;
    private readonly ControllerService _controllers;
    private readonly ControllerMouseService _mouse;
    private readonly VirtualKeyboardService _keyboard;
    private readonly UpdateService _updates;
    private readonly Action _openSettings;
    private readonly Action<AppShortcut> _launchShortcut;
    private readonly Action _exitApp;
    private readonly Action _restartElevated;

    private readonly List<Row> _rows = [];
    private int _row = 1; // 0 é a barra de energia no alto; começa na fileira de jogos
    private int _col;

    private readonly System.Windows.Forms.Timer _inputTimer = new() { Interval = 40 };
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 1000 };
    private GamepadButtons _prevButtons = GamepadButtons.All;
    private int _prevDx;
    private int _prevDy;
    private DateTime _repeatAt;

    // Escolha entre duas opções (ex.: Sim/Não) desenhada na própria tela, para funcionar com o controle.
    private string? _confirmText;
    private string _confirmYesLabel = "Sim";
    private string _confirmNoLabel = "Não";
    private Action? _confirmAction;   // opção da esquerda
    private Action? _confirmNoAction; // opção da direita
    private bool _confirmYes;         // esquerda selecionada
    private RectangleF _confirmYesRect;
    private RectangleF _confirmNoRect;

    // Aviso temporário (erros, feedback de ações).
    private string? _notice;
    private DateTime _noticeUntil;
    private bool _noticeIsError;

    public ConsoleForm(
        SettingsService settings,
        SteamService steam,
        SteamLibraryService library,
        GameModeService gameMode,
        PowerService power,
        DisplayService display,
        KeyboardBacklightService backlight,
        ControllerService controllers,
        ControllerMouseService mouse,
        VirtualKeyboardService keyboard,
        UpdateService updates,
        Action openSettings,
        Action<AppShortcut> launchShortcut,
        Action exitApp,
        Action restartElevated)
    {
        _settings = settings;
        _steam = steam;
        _library = library;
        _gameMode = gameMode;
        _power = power;
        _display = display;
        _backlight = backlight;
        _controllers = controllers;
        _mouse = mouse;
        _keyboard = keyboard;
        _updates = updates;
        _openSettings = openSettings;
        _launchShortcut = launchShortcut;
        _exitApp = exitApp;
        _restartElevated = restartElevated;

        Text = "My Game Console";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        BackColor = Theme.BgTop;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _inputTimer.Tick += (_, _) => PollController();
        _clockTimer.Tick += (_, _) => Invalidate();
        // Progresso do download e mudanças de estado da atualização aparecem na página de configurações.
        _updates.StateChanged += (_, _) => { if (_settingsOpen && Visible) Invalidate(); };
    }

    // ------------------------------------------------------------------
    // Ciclo de vida
    // ------------------------------------------------------------------

    /// <summary>Mostra (ou traz para frente) a tela do console em tela cheia no monitor principal.</summary>
    public void ShowLauncher()
    {
        if (_row == 0) (_row, _col) = (1, 0); // não reabrir com "Desligar" selecionado
        BuildTiles();
        _confirmText = null;
        _confirmAction = null;
        _settingsOpen = false;
        CloseCapture();
        _prevButtons = GamepadButtons.All; // ignora botões já pressionados no momento de abrir
        _prevDx = _prevDy = 0;

        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        Bounds = screen.Bounds;

        if (!Visible) Show();
        TopMost = true;
        Activate();
        BringToFront();
        NativeMethods.SetForegroundWindow(Handle);
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _inputTimer.Start();
            _clockTimer.Start();
        }
        else
        {
            if (_screenOff) ExitScreenOff(); // nunca deixar a tela apagada sem quem a acorde
            _inputTimer.Stop();
            _clockTimer.Stop();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inputTimer.Dispose();
            _clockTimer.Dispose();
            _steamIcon?.Dispose();
            _art.Dispose();
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------
    // Conteúdo
    // ------------------------------------------------------------------

    private void BuildTiles()
    {
        // A lista é refeita a cada abertura (jogos instalados e ordem de "jogado por último" mudam);
        // a seleção acompanha o mesmo item, não a mesma posição.
        var selectedTitle = CurrentTile?.Title;
        _rows.Clear();

        var power = new Row { Title = "Energia", IsTopBar = true };
        power.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphMoon,
            Title = "Suspender",
            Subtitle = "Suspende o PC, ou só apaga a tela e a luz do teclado (o controle acorda).",
            OnSelect = () => Choose(
                "Suspender o computador ou só apagar tela e teclado?",
                "Suspender o PC", () => { Hide(); _power.Sleep(); },
                "Só tela e teclado", EnterScreenOff,
                defaultLeft: true),
        });
        power.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphRefresh,
            Title = "Reiniciar",
            Subtitle = "Reinicia o computador.",
            OnSelect = () => Confirm("Reiniciar o computador?", _power.Restart),
        });
        power.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphPower,
            Title = "Desligar",
            Subtitle = "Desliga o computador.",
            OnSelect = () => Confirm("Desligar o computador?", _power.Shutdown),
        });
        power.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphClose,
            Title = "Sair do app",
            Subtitle = "Fecha o My Game Console (o Modo Game continua aplicado).",
            OnSelect = () => Confirm("Fechar o My Game Console?", _exitApp),
        });

        var games = new Row { Title = "Jogos", Scale = GamesRowScale };
        games.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphPlay,
            Image = SteamIcon(),
            Title = "Steam Big Picture",
            Subtitle = SteamStatusText(),
            Aspect = GameCapsuleAspect,
            OnSelect = () => _ = OpenBigPictureFromConsoleAsync(),
        });

        foreach (var game in _library.LoadInstalledGames())
        {
            games.Tiles.Add(new Tile
            {
                Glyph = Theme.GlyphGame,
                Title = game.Name,
                Subtitle = GameUsageText(game),
                Game = game,
                Aspect = GameCapsuleAspect,
                OnSelect = () => _ = LaunchGameFromConsoleAsync(game),
            });
        }

        foreach (var sc in _settings.Current.Shortcuts)
        {
            var shortcut = sc;
            games.Tiles.Add(new Tile
            {
                Glyph = Theme.GlyphPlay,
                Title = shortcut.Name,
                Subtitle = SafeFileName(shortcut.Path),
                Aspect = GameCapsuleAspect,
                OnSelect = () => { _launchShortcut(shortcut); Hide(); },
            });
        }

        var system = new Row { Title = "Sistema" };
        system.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphGame,
            Title = "Modo Game",
            Subtitle = "Checklist do que o app aplica (barra, ícones, papel de parede, senha ao acordar) e do que falta fazer à mão.",
            IsOn = () => _gameMode.IsEnabled,
            OnSelect = OpenGameModePage,
        });
        system.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphController,
            Title = "Controle",
            Subtitle = "Mapa dos atalhos do controle, mouse pelo analógico e teclado virtual, com o desenho do controle.",
            IsOn = () => _mouse.IsEnabled,
            OnSelect = OpenControllerPage,
        });
        system.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphHome,
            Title = "Área de trabalho",
            Subtitle = "Fecha esta tela e volta ao Windows.",
            OnSelect = Hide,
        });
        system.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphSettings,
            Title = "Configurações",
            Subtitle = "Opções do app, Modo Game, controle e atalhos, tudo pelo controle.",
            OnSelect = OpenSettingsPage,
        });

        _rows.Add(power);
        _rows.Add(games);
        _rows.Add(system);

        _row = Math.Clamp(_row, 0, _rows.Count - 1);
        _col = Math.Clamp(_col, 0, _rows[_row].Tiles.Count - 1);

        if (selectedTitle is not null && CurrentTile?.Title != selectedTitle)
        {
            int col = _rows[_row].Tiles.FindIndex(t => t.Title == selectedTitle);
            if (col >= 0) _col = col;
        }
    }

    /// <summary>"Jogado hoje · 12 h de jogo", a partir do que o Steam registra na conta.</summary>
    private static string GameUsageText(SteamGame game)
    {
        if (game.LastPlayed is not { } last) return "Ainda não jogado";

        int days = (DateTime.Today - last.Date).Days;
        var when = days switch
        {
            <= 0 => "Jogado hoje",
            1 => "Jogado ontem",
            < 30 => $"Jogado há {days} dias",
            _ => $"Jogado em {last:d 'de' MMMM 'de' yyyy}",
        };

        var time = game.PlaytimeMinutes switch
        {
            <= 0 => null,
            < 60 => $"{game.PlaytimeMinutes} min de jogo",
            var m => $"{m / 60} h de jogo",
        };

        return time is null ? when : $"{when}   ·   {time}";
    }

    /// <summary>
    /// Inicia o jogo pelo Steam e mantém esta tela (que fica sempre por cima) até outra janela tomar o
    /// primeiro plano: o próprio jogo ou algum aviso do Steam (atualização, nuvem). Esconder antes faria o
    /// Windows recusar o foco para o jogo, e esconder só no fim do tempo deixaria o jogo atrás desta tela.
    /// </summary>
    private async Task LaunchGameFromConsoleAsync(SteamGame game)
    {
        try
        {
            ShowNotice($"Abrindo {game.Name}...");
            _library.Launch(game);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                if (!Visible) return; // o usuário fechou esta tela enquanto esperava
                var foreground = NativeMethods.GetForegroundWindow();
                if (foreground != IntPtr.Zero && foreground != Handle) break;
            }

            if (Visible) Hide();
        }
        catch (Exception ex)
        {
            ShowNotice(ex.Message, isError: true);
        }
    }

    private Bitmap? _steamIcon;
    private string? _steamIconSource;

    /// <summary>Ícone real do Steam instalado (extraído do steam.exe), em cache. Null se o Steam não for encontrado.</summary>
    private Bitmap? SteamIcon()
    {
        var exe = _steam.SteamExePath;
        if (!string.Equals(exe, _steamIconSource, StringComparison.OrdinalIgnoreCase))
        {
            _steamIcon?.Dispose();
            _steamIcon = IconExtractor.Extract(exe);
            _steamIconSource = exe;
        }

        return _steamIcon;
    }

    private string SteamStatusText() =>
        !_steam.IsInstalled ? "Steam não encontrado"
        : _steam.IsBigPictureActive ? "Big Picture aberto: traz para a frente"
        : _steam.IsRunning ? "Steam aberto: muda para o Big Picture"
        : "Abre o Steam em modo Big Picture";

    private static string SafeFileName(string path)
    {
        try { return Path.GetFileName(path); }
        catch { return path; }
    }

    /// <summary>
    /// Abre o Big Picture mantendo esta tela visível até a janela dele estar em primeiro plano; só então
    /// esconde. Enquanto esta tela está na frente, o app é o processo em primeiro plano e o Windows aceita
    /// dar o foco ao Steam; se escondêssemos antes, o pedido de foco seria ignorado (só piscaria na barra).
    /// </summary>
    private async Task OpenBigPictureFromConsoleAsync()
    {
        try
        {
            if (!_steam.IsBigPictureActive) ShowNotice("Abrindo o Steam Big Picture...");
            var focused = await _steam.OpenBigPictureAsync();
            if (!Visible) return; // o usuário fechou esta tela enquanto esperava
            Hide();
            if (!focused) ShowNotice("O Big Picture não apareceu a tempo. Confira a janela do Steam.", isError: true);
        }
        catch (Exception ex)
        {
            ShowNotice(ex.Message, isError: true);
        }
    }

    private void ToggleGameMode()
    {
        try
        {
            _gameMode.Toggle();
            ShowNotice(_gameMode.IsEnabled
                ? "Modo Game ativado. Os ajustes continuam valendo após reiniciar."
                : "Modo Game desativado. Área de trabalho restaurada.");
        }
        finally
        {
            // Mesmo com falha parcial o estado mudou: a lista do checklist e o tile precisam refletir isso.
            RebuildPage();
            BuildTiles();
        }
    }

    // ------------------------------------------------------------------
    // Navegação
    // ------------------------------------------------------------------

    private Tile? CurrentTile =>
        _rows.Count > 0 && _col < _rows[_row].Tiles.Count ? _rows[_row].Tiles[_col] : null;

    private void MoveSelection(int dx, int dy)
    {
        if (_confirmText is not null)
        {
            if (dx != 0) _confirmYes = dx < 0;
            Invalidate();
            return;
        }

        if (_capture != CaptureKind.None) return;

        if (_settingsOpen)
        {
            SettingsMove(dx, dy);
            return;
        }

        if (_rows.Count == 0) return;

        if (dy != 0)
        {
            // Os tiles têm larguras diferentes em cada fileira (e a barra de energia fica à direita):
            // vai para o tile da outra fileira mais próximo na horizontal, não para a mesma posição.
            float fromX = CurrentTile is { Bounds.IsEmpty: false } from ? from.Bounds.X + from.Bounds.Width / 2f : float.NaN;
            int target = Math.Clamp(_row + dy, 0, _rows.Count - 1);
            if (target != _row)
            {
                _row = target;
                _col = float.IsNaN(fromX) ? Math.Clamp(_col, 0, _rows[_row].Tiles.Count - 1) : NearestTile(_rows[_row], fromX);
            }
        }

        if (dx != 0)
        {
            _col = Math.Clamp(_col + dx, 0, _rows[_row].Tiles.Count - 1);
        }

        Invalidate();
    }

    /// <summary>Índice do tile visível da fileira cujo centro está mais perto de <paramref name="x"/>.</summary>
    private static int NearestTile(Row row, float x)
    {
        int best = Math.Clamp(row.Scroll, 0, Math.Max(0, row.Tiles.Count - 1));
        float bestDistance = float.MaxValue;
        for (int i = 0; i < row.Tiles.Count; i++)
        {
            var b = row.Tiles[i].Bounds;
            if (b.IsEmpty) continue; // fora da tela
            float distance = Math.Abs(b.X + b.Width / 2f - x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private void ActivateSelection()
    {
        if (_confirmText is not null)
        {
            var action = _confirmYes ? _confirmAction : _confirmNoAction;
            _confirmText = null;
            _confirmAction = null;
            _confirmNoAction = null;
            Invalidate();
            Run(action);
            return;
        }

        if (_capture != CaptureKind.None) return;

        if (_settingsOpen)
        {
            SettingsActivate();
            return;
        }

        Run(CurrentTile?.OnSelect);
    }

    private void GoBack()
    {
        if (_confirmText is not null)
        {
            _confirmText = null;
            _confirmAction = null;
            Invalidate();
            return;
        }

        if (_capture != CaptureKind.None)
        {
            CloseCapture();
            return;
        }

        if (_settingsOpen)
        {
            CloseSettingsPage();
            return;
        }

        Hide();
    }

    private void Confirm(string question, Action action) =>
        Choose(question, "Sim", action, "Não", null, defaultLeft: false); // padrão seguro: "Não"

    /// <summary>Overlay com duas opções (esquerda e direita), cada uma com sua ação. ◀ ▶ escolhe, A confirma, B cancela.</summary>
    private void Choose(string question, string leftLabel, Action? leftAction, string rightLabel, Action? rightAction, bool defaultLeft)
    {
        _confirmText = question;
        _confirmYesLabel = leftLabel;
        _confirmNoLabel = rightLabel;
        _confirmAction = leftAction;
        _confirmNoAction = rightAction;
        _confirmYes = defaultLeft;
        Invalidate();
    }

    private void Run(Action? action)
    {
        if (action is null) return;

        try
        {
            action();
        }
        catch (Exception ex)
        {
            ShowNotice(ex.Message, isError: true);
        }
    }

    private void ShowNotice(string text, bool isError = false)
    {
        _notice = text;
        _noticeIsError = isError;
        _noticeUntil = DateTime.Now.AddSeconds(isError ? 8 : 4);
        Invalidate();
    }

    // ------------------------------------------------------------------
    // Entrada: teclado, mouse e controle
    // ------------------------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_screenOff)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            ExitScreenOff();
            return;
        }

        if (_capture == CaptureKind.Hotkey)
        {
            HandleHotkeyCapture(e);
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;

        switch (e.KeyCode)
        {
            case Keys.Left: MoveSelection(-1, 0); break;
            case Keys.Right: MoveSelection(1, 0); break;
            case Keys.Up: MoveSelection(0, -1); break;
            case Keys.Down: MoveSelection(0, 1); break;
            case Keys.Enter:
            case Keys.Space: ActivateSelection(); break;
            case Keys.Escape:
            case Keys.Back: GoBack(); break;
            default:
                e.Handled = false;
                e.SuppressKeyPress = false;
                base.OnKeyDown(e);
                break;
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Enter or Keys.Escape
        || base.IsInputKey(keyData);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_screenOff)
        {
            ExitScreenOff();
            return;
        }

        if (_confirmText is not null)
        {
            if (_confirmYesRect.Contains(e.Location) && !_confirmYes) { _confirmYes = true; Invalidate(); }
            else if (_confirmNoRect.Contains(e.Location) && _confirmYes) { _confirmYes = false; Invalidate(); }
            return;
        }

        if (_capture != CaptureKind.None) return;

        if (_settingsOpen)
        {
            SettingsMouseMove(e.Location);
            return;
        }

        for (int r = 0; r < _rows.Count; r++)
        {
            var tiles = _rows[r].Tiles;
            for (int c = 0; c < tiles.Count; c++)
            {
                if (tiles[c].Bounds.Contains(e.Location) && (r != _row || c != _col))
                {
                    _row = r;
                    _col = c;
                    Invalidate();
                    return;
                }
            }
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_screenOff)
        {
            ExitScreenOff();
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        if (_confirmText is not null)
        {
            if (_confirmYesRect.Contains(e.Location)) { _confirmYes = true; ActivateSelection(); }
            else if (_confirmNoRect.Contains(e.Location)) { _confirmYes = false; ActivateSelection(); }
            return;
        }

        if (_capture != CaptureKind.None) return;

        if (_settingsOpen)
        {
            SettingsMouseClick(e.Location);
            return;
        }

        if (CurrentTile is { } tile && tile.Bounds.Contains(e.Location))
        {
            ActivateSelection();
        }
    }

    private void PollController()
    {
        if (!Visible) return;

        if (_capture != CaptureKind.None && DateTime.UtcNow > _captureUntil)
        {
            CloseCapture();
            ShowNotice("Tempo esgotado. Nada foi alterado.");
        }

        if (_capture == CaptureKind.HidButton)
        {
            PollHidCapture();
            return;
        }

        if (_settingsOpen) SettingsTick();

        var buttons = GamepadButtons.None;
        int dx = 0, dy = 0;

        foreach (var pad in _controllers.ReadStates())
        {
            buttons |= pad.Buttons;

            if ((pad.Buttons & GamepadButtons.DpadLeft) != 0 || pad.ThumbLX < -StickDeadZone) dx = -1;
            else if ((pad.Buttons & GamepadButtons.DpadRight) != 0 || pad.ThumbLX > StickDeadZone) dx = 1;

            if ((pad.Buttons & GamepadButtons.DpadUp) != 0 || pad.ThumbLY > StickDeadZone) dy = -1;
            else if ((pad.Buttons & GamepadButtons.DpadDown) != 0 || pad.ThumbLY < -StickDeadZone) dy = 1;
        }

        if (_screenOff)
        {
            // Tela apagada: qualquer botão novo ou movimento do direcional/analógico acorda; nada mais é processado.
            var newly = buttons & ~_prevButtons;
            _prevButtons = buttons;
            _prevDx = dx;
            _prevDy = dy;
            if (newly != GamepadButtons.None || dx != 0 || dy != 0) ExitScreenOff();
            return;
        }

        var now = DateTime.UtcNow;
        bool moving = dx != 0 || dy != 0;

        if (dx != _prevDx || dy != _prevDy)
        {
            if (moving)
            {
                MoveSelection(dx, dy);
                _repeatAt = now + RepeatDelay;
            }
        }
        else if (moving && now >= _repeatAt)
        {
            MoveSelection(dx, dy);
            _repeatAt = now + RepeatInterval;
        }

        _prevDx = dx;
        _prevDy = dy;

        var pressed = buttons & ~_prevButtons;
        _prevButtons = buttons;

        if ((pressed & GamepadButtons.A) != 0) ActivateSelection();
        else if ((pressed & GamepadButtons.B) != 0) GoBack();
    }

    // ------------------------------------------------------------------
    // Desenho
    // ------------------------------------------------------------------

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // tudo é pintado em OnPaint
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        if (w <= 0 || h <= 0) return;

        DrawBackground(g, w, h);
        DrawHeader(g, w, h);

        if (_settingsOpen)
        {
            DrawSettings(g, w, h);
            DrawSettingsFooter(g, w, h);
        }
        else
        {
            DrawRows(g, w, h);
            DrawFooter(g, w, h);
        }

        DrawVersion(g, w, h);

        if (_notice is not null)
        {
            if (DateTime.Now < _noticeUntil) DrawNotice(g, w, h);
            else _notice = null;
        }

        if (_confirmText is not null)
        {
            DrawConfirm(g, w, h);
        }
        else if (_capture != CaptureKind.None)
        {
            DrawCapture(g, w, h);
        }
    }

    private void DrawBackground(Graphics g, int w, int h)
    {
        // Na tela inicial, com um jogo selecionado, o fundo vira a arte panorâmica dele (estilo PS5).
        if (!_settingsOpen && CurrentTile?.Game is { } game && _art.Backdrop(game, new Size(w, h)) is { } backdrop)
        {
            g.DrawImageUnscaled(backdrop, 0, 0);
            return;
        }

        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Theme.BgTop, Theme.BgBottom, 90f))
        {
            g.FillRectangle(bg, 0, 0, w, h);
        }

        using var glowPath = new GraphicsPath();
        glowPath.AddEllipse(new RectangleF(-w * 0.1f, -h * 0.4f, w * 1.2f, h * 1.1f));
        using var glow = new PathGradientBrush(glowPath)
        {
            CenterColor = Color.FromArgb(55, Theme.Accent),
            SurroundColors = [Color.FromArgb(0, Theme.Accent)],
            CenterPoint = new PointF(w * 0.5f, h * 0.05f),
        };
        g.FillPath(glow, glowPath);
    }

    /// <summary>
    /// Cabeçalho numa linha só, tudo centrado no mesmo eixo: à esquerda o controle e o nome do app; à direita
    /// o relógio e (na tela inicial) os botões de energia. Embaixo, à direita, o status ou o nome do botão
    /// de energia selecionado.
    /// </summary>
    private void DrawHeader(Graphics g, int w, int h)
    {
        float u = h / 100f;
        float mx = w * 0.06f;
        float barTop = u * 4.5f;
        float barH = u * 5.6f;
        float cy = barTop + barH / 2f;

        using var textBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var accentBrush = new SolidBrush(Theme.Accent);

        // marca: glifo do controle e nome, alinhados pelo centro do desenho (não da caixa da fonte)
        float brandRight = DrawInkCentered(g, Theme.GlyphGame, Theme.IconFontName, FontStyle.Regular, u * 3.6f, accentBrush, mx, cy);
        DrawInkCentered(g, "MY GAME CONSOLE", "Segoe UI", FontStyle.Bold, u * 2.3f, textBrush, brandRight + u * 1.5f, cy);

        var topBar = _rows.FirstOrDefault(r => r.IsTopBar);
        bool showPower = !_settingsOpen && topBar is not null;
        float clockRight = w - mx;

        if (topBar is not null)
        {
            foreach (var t in topBar.Tiles) t.Bounds = RectangleF.Empty;
        }

        if (showPower)
        {
            float d = barH;
            float gap = u * 1.1f;
            float x = w - mx - topBar!.Tiles.Count * d - (topBar.Tiles.Count - 1) * gap;
            bool barActive = _rows[_row] == topBar;

            using var glyphFont = new Font(Theme.IconFontName, d * 0.36f, GraphicsUnit.Pixel);
            using var fill = new SolidBrush(Color.FromArgb(170, Theme.Tile));
            using var darkBrush = new SolidBrush(Theme.BgTop);
            using var glowBrush = new SolidBrush(Color.FromArgb(70, Theme.Accent));
            using var ringPen = new Pen(Color.FromArgb(60, Theme.Muted), u * 0.12f);
            var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            // divisória entre o relógio e os botões
            using (var sepPen = new Pen(Color.FromArgb(70, Theme.Muted), u * 0.12f))
            {
                float sx = x - u * 2.2f;
                g.DrawLine(sepPen, sx, cy - u * 1.7f, sx, cy + u * 1.7f);
                clockRight = sx - u * 2.2f;
            }

            for (int i = 0; i < topBar.Tiles.Count; i++)
            {
                var t = topBar.Tiles[i];
                bool selected = barActive && i == _col;
                var rect = new RectangleF(x + i * (d + gap), cy - d / 2f, d, d);
                t.Bounds = rect;

                if (selected)
                {
                    var glow = rect;
                    glow.Inflate(u * 0.6f, u * 0.6f);
                    g.FillEllipse(glowBrush, glow);
                    g.FillEllipse(accentBrush, rect);
                }
                else
                {
                    g.FillEllipse(fill, rect);
                    g.DrawEllipse(ringPen, rect);
                }

                // Os glifos da fonte de ícones já vêm centrados na caixa do caractere.
                g.DrawString(t.Glyph, glyphFont, selected ? darkBrush : mutedBrush, rect, center);
            }
        }

        // relógio
        var now = DateTime.Now;
        DrawInkCentered(g, now.ToString("HH:mm"), "Segoe UI Light", FontStyle.Regular, u * 4.4f, textBrush, clockRight, cy, alignRight: true);

        // linha de baixo: nome do botão de energia selecionado, ou o status
        using var statusFont = new Font("Segoe UI", u * 1.8f, GraphicsUnit.Pixel);
        float statusY = barTop + barH + u * 1.3f;

        if (showPower && _rows[_row] == topBar && CurrentTile is { } powerTile)
        {
            using var boldFont = new Font(statusFont, FontStyle.Bold);
            var sub = powerTile.Subtitle;
            var subSize = g.MeasureString(sub, statusFont);
            var titleText = powerTile.Title + "   ·   ";
            var titleSize = g.MeasureString(titleText, boldFont);
            float right = w - mx;
            g.DrawString(sub, statusFont, mutedBrush, right - subSize.Width, statusY);
            g.DrawString(titleText, boldFont, accentBrush, right - subSize.Width - titleSize.Width, statusY);
            return;
        }

        var pads = _controllers.ConnectedCount switch
        {
            0 => "Nenhum controle",
            1 => "1 controle",
            var n => $"{n} controles",
        };
        var steam = !_steam.IsInstalled ? "Steam ausente"
            : _steam.IsBigPictureActive ? "Big Picture ativo"
            : _steam.IsRunning ? "Steam aberto"
            : "Steam fechado";
        var status = $"{now:dddd, d 'de' MMMM}   ·   {pads}   ·   {steam}";
        var statusSize = g.MeasureString(status, statusFont);
        g.DrawString(status, statusFont, mutedBrush, w - mx - statusSize.Width, statusY);
    }

    /// <summary>
    /// Desenha o texto com o centro vertical da tinta em <paramref name="cy"/>. O <c>DrawString</c> centra a
    /// caixa da linha, que inclui espaço para acentos e descendentes, e por isso cada fonte (a de ícones, a do
    /// texto, a do relógio) ficava numa altura diferente. Começa em <paramref name="x"/> (ou termina nele,
    /// com <paramref name="alignRight"/>) e devolve a borda direita do desenho.
    /// </summary>
    private static float DrawInkCentered(Graphics g, string text, string family, FontStyle style, float emPixels,
        Brush brush, float x, float cy, bool alignRight = false)
    {
        using var path = new GraphicsPath();
        using (var ff = new FontFamily(family))
        {
            path.AddString(text, ff, (int)style, emPixels, PointF.Empty, StringFormat.GenericTypographic);
        }

        var ink = path.GetBounds();
        if (ink.IsEmpty) return x;

        float left = alignRight ? x - ink.Width : x;
        using (var m = new Matrix())
        {
            m.Translate(left - ink.X, cy - (ink.Y + ink.Height / 2f));
            path.Transform(m);
        }

        g.FillPath(brush, path);
        return left + ink.Width;
    }

    private void DrawRows(Graphics g, int w, int h)
    {
        float u = h / 100f;
        float baseTile = Math.Min(w / 7.5f, h * 0.19f);
        float gap = baseTile * 0.14f;
        float mx = w * 0.06f;
        float avail = w - 2 * mx;
        float y = h * 0.225f;

        using var rowTitleFont = new Font("Segoe UI", u * 1.9f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var titleFont = new Font("Segoe UI", u * 2.0f, GraphicsUnit.Pixel);
        using var titleSelFont = new Font("Segoe UI", u * 2.3f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subFont = new Font("Segoe UI", u * 1.7f, GraphicsUnit.Pixel);
        using var insideFont = new Font("Segoe UI", u * 1.7f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var glyphFont = new Font(Theme.IconFontName, baseTile * 0.40f, GraphicsUnit.Pixel);
        using var pillFont = new Font("Segoe UI", u * 1.25f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var arrowFont = new Font("Segoe UI", u * 3f, GraphicsUnit.Pixel);
        using var accentPen = new Pen(Theme.Accent, u * 0.35f);

        var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var topCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter };
        var oneLine = new StringFormat(topCenter) { FormatFlags = StringFormatFlags.NoWrap };

        for (int r = 0; r < _rows.Count; r++)
        {
            var row = _rows[r];
            if (row.IsTopBar) continue;

            bool activeRow = r == _row;
            float th = baseTile * row.Scale;
            var widths = row.Tiles.Select(t => th * t.Aspect).ToArray();

            float Span(int from, int to)
            {
                float s = 0;
                for (int i = from; i <= to; i++) s += widths[i];
                return s + Math.Max(0, to - from) * gap;
            }

            // rolagem: o selecionado sempre inteiro na tela, sem sobrar espaço vazio no fim
            if (activeRow && row.Tiles.Count > 0)
            {
                if (_col < row.Scroll) row.Scroll = _col;
                while (row.Scroll < _col && Span(row.Scroll, _col) > avail) row.Scroll++;
            }
            row.Scroll = Math.Clamp(row.Scroll, 0, Math.Max(0, row.Tiles.Count - 1));
            while (row.Scroll > 0 && Span(row.Scroll - 1, row.Tiles.Count - 1) <= avail) row.Scroll--;

            int alpha = activeRow ? 255 : 120;
            using var rowBrush = new SolidBrush(activeRow ? Theme.Text : Theme.Muted);
            using var textBrush = new SolidBrush(Color.FromArgb(alpha, Theme.Text));
            using var subBrush = new SolidBrush(Color.FromArgb(alpha, Theme.Muted));
            using var tileBrush = new SolidBrush(Color.FromArgb(alpha, Theme.Tile));
            using var tileSelBrush = new SolidBrush(Color.FromArgb(alpha, Theme.TileSelected));
            using var glowBrush = new SolidBrush(Color.FromArgb(60, Theme.Accent));
            using var dimBrush = new SolidBrush(Color.FromArgb(255 - alpha, Theme.BgTop));

            g.DrawString(row.Title.ToUpperInvariant(), rowTitleFont, rowBrush, mx, y - u * 4.6f);

            // limpa retângulos de tiles fora da tela (para o mouse não acertá-los)
            foreach (var t in row.Tiles) t.Bounds = RectangleF.Empty;

            int last = row.Scroll;
            float x = mx;
            for (int i = row.Scroll; i < row.Tiles.Count; i++)
            {
                if (x + widths[i] > mx + avail + 0.5f) break;

                var t = row.Tiles[i];
                bool selected = activeRow && i == _col;
                var baseRect = new RectangleF(x, y, widths[i], th);
                var rect = baseRect;
                x += widths[i] + gap;
                last = i + 1;

                if (selected)
                {
                    rect.Inflate(baseTile * 0.05f, baseTile * 0.05f);
                    var glowRect = rect;
                    glowRect.Inflate(u * 0.9f, u * 0.9f);
                    using var glowPath = RoundedRect(glowRect, u * 2.2f);
                    g.FillPath(glowBrush, glowPath);
                }

                t.Bounds = rect;
                bool capsule = t.Aspect < 1f;
                using var path = RoundedRect(rect, u * 1.8f);

                var art = t.Game is { } game ? _art.Capsule(game, Size.Round(baseRect.Size)) : null;
                if (art is not null)
                {
                    // Pincel de textura em vez de recorte: bordas arredondadas suavizadas.
                    using var artBrush = new TextureBrush(art, WrapMode.Clamp);
                    artBrush.TranslateTransform(rect.X, rect.Y);
                    artBrush.ScaleTransform(rect.Width / art.Width, rect.Height / art.Height);
                    g.FillPath(artBrush, path);
                    if (!activeRow) g.FillPath(dimBrush, path);
                }
                else
                {
                    g.FillPath(selected ? tileSelBrush : tileBrush, path);
                    DrawTileIcon(g, t, rect, capsule, alpha, activeRow, glyphFont, textBrush, center);

                    if (capsule)
                    {
                        // Sem capa, o nome vai dentro do tile, como numa capa de verdade.
                        var nameRect = new RectangleF(rect.X + u * 1f, rect.Y + rect.Height * 0.66f, rect.Width - u * 2f, rect.Height * 0.3f);
                        g.DrawString(t.Title, insideFont, textBrush, nameRect, topCenter);
                    }
                }

                if (selected) g.DrawPath(accentPen, path);

                if (t.IsOn is not null)
                {
                    bool on = t.IsOn();
                    var pillText = on ? "LIGADO" : "DESLIGADO";
                    var pillSize = g.MeasureString(pillText, pillFont);
                    var pill = new RectangleF(rect.Right - pillSize.Width - u * 2.4f, rect.Top + u * 1.2f, pillSize.Width + u * 1.6f, pillSize.Height + u * 0.4f);
                    using var pillBrush = new SolidBrush(Color.FromArgb(alpha, on ? Theme.Success : Theme.PillOff));
                    using var pillPath = RoundedRect(pill, pill.Height / 2f);
                    g.FillPath(pillBrush, pillPath);
                    g.DrawString(pillText, pillFont, textBrush, pill, center);
                }

                // Capas já trazem o nome: o título embaixo só aparece no selecionado. Tiles quadrados sempre têm.
                if (!capsule || selected)
                {
                    float labelW = capsule ? Math.Max(rect.Width + gap * 0.8f, baseTile * 2.6f) : rect.Width + gap * 0.8f;
                    var labelRect = new RectangleF(rect.X + rect.Width / 2f - labelW / 2f, rect.Bottom + u * 1.2f, labelW, u * 3.2f);
                    KeepInside(ref labelRect, w, mx);
                    g.DrawString(t.Title, selected ? titleSelFont : titleFont, textBrush, labelRect, oneLine);

                    if (selected && !string.IsNullOrEmpty(t.Subtitle))
                    {
                        var subRect = new RectangleF(rect.X + rect.Width / 2f - baseTile * 1.4f, labelRect.Bottom + u * 0.3f, baseTile * 2.8f, u * 4.5f);
                        KeepInside(ref subRect, w, mx);
                        g.DrawString(t.Subtitle, subFont, subBrush, subRect, topCenter);
                    }
                }
            }

            // indicadores de rolagem
            if (row.Scroll > 0)
            {
                g.DrawString("‹", arrowFont, rowBrush, new RectangleF(mx * 0.35f, y, mx * 0.5f, th), center);
            }
            if (last < row.Tiles.Count)
            {
                g.DrawString("›", arrowFont, rowBrush, new RectangleF(w - mx * 0.85f, y, mx * 0.5f, th), center);
            }

            y += th + u * 14.5f;
        }
    }

    /// <summary>Ícone do tile sem capa: a imagem (ex.: ícone do Steam) ou o glifo. Nas capas, fica no alto.</summary>
    private static void DrawTileIcon(Graphics g, Tile t, RectangleF rect, bool capsule, int alpha, bool activeRow,
        Font glyphFont, Brush textBrush, StringFormat center)
    {
        float cy = capsule ? rect.Y + rect.Height * 0.4f : rect.Y + rect.Height / 2f;

        if (t.Image is not { } image)
        {
            g.DrawString(t.Glyph, glyphFont, textBrush, new RectangleF(rect.X, cy - rect.Width / 2f, rect.Width, rect.Width), center);
            return;
        }

        float side = capsule ? rect.Width * 0.55f : rect.Width * 0.5f;
        var imgRect = new RectangleF(rect.X + (rect.Width - side) / 2f, cy - side / 2f, side, side);
        if (activeRow)
        {
            g.DrawImage(image, imgRect);
            return;
        }

        // fileira inativa: mesma transparência dos glifos
        using var attrs = new System.Drawing.Imaging.ImageAttributes();
        attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha / 255f });
        g.DrawImage(image, Rectangle.Round(imgRect), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attrs);
    }

    private static void KeepInside(ref RectangleF rect, int w, float mx)
    {
        if (rect.X < mx * 0.5f) rect.X = mx * 0.5f;
        if (rect.Right > w - mx * 0.5f) rect.X = w - mx * 0.5f - rect.Width;
    }

    private void DrawFooter(Graphics g, int w, int h)
    {
        float u = h / 100f;
        float mx = w * 0.06f;
        float y = h - u * 7f;

        using var font = new Font("Segoe UI", u * 1.9f, GraphicsUnit.Pixel);
        using var mutedBrush = new SolidBrush(Theme.Muted);

        float x = mx;
        x = DrawHint(g, font, x, y, "A", "Selecionar", u);
        x = DrawHint(g, font, x, y, "B", "Voltar", u);
        DrawHint(g, font, x, y, "+", "Navegar", u);

        var hotkey = _settings.Current.LauncherHotkey;
        var right = string.IsNullOrWhiteSpace(hotkey)
            ? "Esc  Fechar"
            : $"Esc  Fechar   ·   Atalho: {hotkey}";
        var size = g.MeasureString(right, font);
        g.DrawString(right, font, mutedBrush, w - mx - size.Width, y + (u * 3.4f - size.Height) / 2f);
    }

    /// <summary>Versão do app, discreta, no canto inferior direito (abaixo do rodapé). Avisa quando há versão nova.</summary>
    private void DrawVersion(Graphics g, int w, int h)
    {
        float u = h / 100f;
        var text = _updates.Available is { } update
            ? $"v{_updates.CurrentVersionText}   ·   {update.VersionText} disponível"
            : $"v{_updates.CurrentVersionText}";

        using var font = new Font("Segoe UI", u * 1.5f, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(_updates.Available is null ? Color.FromArgb(110, Theme.Muted) : Theme.Accent);
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, w - u * 2f - size.Width, h - u * 1.2f - size.Height);
    }

    private static float DrawHint(Graphics g, Font font, float x, float y, string button, string label, float u)
    {
        float d = u * 3.4f;
        var circle = new RectangleF(x, y, d, d);

        using var fill = new SolidBrush(Theme.TileSelected);
        using var pen = new Pen(Theme.Muted, u * 0.15f);
        using var textBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var boldFont = new Font(font, FontStyle.Bold);

        g.FillEllipse(fill, circle);
        g.DrawEllipse(pen, circle);
        var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(button, boldFont, textBrush, circle, center);

        var size = g.MeasureString(label, font);
        g.DrawString(label, font, mutedBrush, x + d + u * 1f, y + (d - size.Height) / 2f);

        return x + d + u * 1f + size.Width + u * 4f;
    }

    private void DrawConfirm(Graphics g, int w, int h)
    {
        float u = h / 100f;

        using (var dim = new SolidBrush(Color.FromArgb(175, 0, 0, 0)))
        {
            g.FillRectangle(dim, 0, 0, w, h);
        }

        float pw = Math.Max(w * 0.5f, u * 70f);
        float ph = u * 30f;
        var panel = new RectangleF((w - pw) / 2f, (h - ph) / 2f, pw, ph);

        using var panelBrush = new SolidBrush(Theme.Tile);
        using var accentPen = new Pen(Theme.Accent, u * 0.25f);
        using (var path = RoundedRect(panel, u * 2f))
        {
            g.FillPath(panelBrush, path);
            g.DrawPath(accentPen, path);
        }

        using var qFont = new Font("Segoe UI", u * 2.8f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bFont = new Font("Segoe UI", u * 2.1f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var hintFont = new Font("Segoe UI", u * 1.6f, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Theme.Text);
        using var darkBrush = new SolidBrush(Theme.BgTop);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var accentBrush = new SolidBrush(Theme.Accent);
        using var buttonBrush = new SolidBrush(Theme.TileSelected);

        var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        g.DrawString(_confirmText, qFont, textBrush,
            new RectangleF(panel.X + u * 2f, panel.Y + u * 2f, panel.Width - u * 4f, ph * 0.42f), center);

        float bw = pw * 0.38f;
        float bh = u * 7f;
        float by = panel.Bottom - bh - u * 6f;
        _confirmYesRect = new RectangleF(panel.X + pw * 0.5f - bw - u * 1.5f, by, bw, bh);
        _confirmNoRect = new RectangleF(panel.X + pw * 0.5f + u * 1.5f, by, bw, bh);

        DrawButton(g, _confirmYesRect, _confirmYesLabel, _confirmYes);
        DrawButton(g, _confirmNoRect, _confirmNoLabel, !_confirmYes);

        g.DrawString("◀ ▶ escolher   ·   A confirmar   ·   B cancelar", hintFont, mutedBrush,
            new RectangleF(panel.X, panel.Bottom - u * 4.5f, panel.Width, u * 3f), center);

        void DrawButton(Graphics gr, RectangleF rect, string text, bool selected)
        {
            using var path = RoundedRect(rect, u * 1.2f);
            gr.FillPath(selected ? accentBrush : buttonBrush, path);
            gr.DrawString(text, bFont, selected ? darkBrush : textBrush, rect, center);
        }
    }

    private void DrawNotice(Graphics g, int w, int h)
    {
        float u = h / 100f;
        using var font = new Font("Segoe UI", u * 1.9f, GraphicsUnit.Pixel);
        var size = g.MeasureString(_notice, font);
        float pw = Math.Min(size.Width + u * 5f, w * 0.8f);
        float ph = u * 5.5f;
        var rect = new RectangleF((w - pw) / 2f, h - u * 15f, pw, ph);

        using var brush = new SolidBrush(Color.FromArgb(230, _noticeIsError ? Theme.Danger : Theme.TileSelected));
        using var textBrush = new SolidBrush(Theme.Text);
        using var path = RoundedRect(rect, ph / 2f);
        g.FillPath(brush, path);

        var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        g.DrawString(_notice, font, textBrush, rect, center);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius) => Shapes.RoundedRect(r, radius);
}
