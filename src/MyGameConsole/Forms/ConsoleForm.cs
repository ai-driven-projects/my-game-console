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
        public RectangleF Bounds { get; set; }
    }

    private sealed class Row
    {
        public required string Title { get; init; }
        public List<Tile> Tiles { get; } = [];
        public int Scroll { get; set; }
    }

    private const short StickDeadZone = 16000;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(420);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(140);

    private readonly SettingsService _settings;
    private readonly SteamService _steam;
    private readonly GameModeService _gameMode;
    private readonly PowerService _power;
    private readonly DisplayService _display;
    private readonly KeyboardBacklightService _backlight;
    private readonly ControllerService _controllers;
    private readonly UpdateService _updates;
    private readonly Action _openSettings;
    private readonly Action<AppShortcut> _launchShortcut;
    private readonly Action _exitApp;
    private readonly Action _restartElevated;

    private readonly List<Row> _rows = [];
    private int _row;
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
        GameModeService gameMode,
        PowerService power,
        DisplayService display,
        KeyboardBacklightService backlight,
        ControllerService controllers,
        UpdateService updates,
        Action openSettings,
        Action<AppShortcut> launchShortcut,
        Action exitApp,
        Action restartElevated)
    {
        _settings = settings;
        _steam = steam;
        _gameMode = gameMode;
        _power = power;
        _display = display;
        _backlight = backlight;
        _controllers = controllers;
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
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------
    // Conteúdo
    // ------------------------------------------------------------------

    private void BuildTiles()
    {
        _rows.Clear();

        var games = new Row { Title = "Jogos e apps" };
        games.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphPlay,
            Image = SteamIcon(),
            Title = "Steam Big Picture",
            Subtitle = SteamStatusText(),
            OnSelect = () => { _steam.OpenBigPicture(); Hide(); },
        });

        foreach (var sc in _settings.Current.Shortcuts)
        {
            var shortcut = sc;
            games.Tiles.Add(new Tile
            {
                Glyph = Theme.GlyphPlay,
                Title = shortcut.Name,
                Subtitle = SafeFileName(shortcut.Path),
                OnSelect = () => { _launchShortcut(shortcut); Hide(); },
            });
        }

        var system = new Row { Title = "Sistema" };
        system.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphGame,
            Title = "Modo Game",
            Subtitle = "Barra oculta, ícones escondidos e papel de parede do console. Continua após reiniciar.",
            IsOn = () => _gameMode.IsEnabled,
            OnSelect = ToggleGameMode,
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
        system.Tiles.Add(new Tile
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
        system.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphRefresh,
            Title = "Reiniciar",
            Subtitle = "Reinicia o computador.",
            OnSelect = () => Confirm("Reiniciar o computador?", _power.Restart),
        });
        system.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphPower,
            Title = "Desligar",
            Subtitle = "Desliga o computador.",
            OnSelect = () => Confirm("Desligar o computador?", _power.Shutdown),
        });
        system.Tiles.Add(new Tile
        {
            Glyph = Theme.GlyphClose,
            Title = "Sair do app",
            Subtitle = "Fecha o My Game Console (o Modo Game continua aplicado).",
            OnSelect = () => Confirm("Fechar o My Game Console?", _exitApp),
        });

        _rows.Add(games);
        _rows.Add(system);

        _row = Math.Clamp(_row, 0, _rows.Count - 1);
        _col = Math.Clamp(_col, 0, _rows[_row].Tiles.Count - 1);
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
        : _steam.IsBigPictureActive ? "Big Picture já está aberto"
        : _steam.IsRunning ? "Steam em execução"
        : "Abre o Steam em modo Big Picture";

    private static string SafeFileName(string path)
    {
        try { return Path.GetFileName(path); }
        catch { return path; }
    }

    private void ToggleGameMode()
    {
        _gameMode.Toggle();
        ShowNotice(_gameMode.IsEnabled
            ? "Modo Game ativado. Os ajustes continuam valendo após reiniciar."
            : "Modo Game desativado. Área de trabalho restaurada.");
        BuildTiles();
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
            _row = Math.Clamp(_row + dy, 0, _rows.Count - 1);
            _col = Math.Clamp(_col, 0, _rows[_row].Tiles.Count - 1);
        }

        if (dx != 0)
        {
            _col = Math.Clamp(_col + dx, 0, _rows[_row].Tiles.Count - 1);
        }

        Invalidate();
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

    private static void DrawBackground(Graphics g, int w, int h)
    {
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

    private void DrawHeader(Graphics g, int w, int h)
    {
        float u = h / 100f;
        float mx = w * 0.06f;
        float top = u * 4.5f;

        using var brandFont = new Font("Segoe UI", u * 2.3f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var glyphFont = new Font(Theme.IconFontName, u * 3.2f, GraphicsUnit.Pixel);
        using var clockFont = new Font("Segoe UI Light", u * 5f, GraphicsUnit.Pixel);
        using var statusFont = new Font("Segoe UI", u * 1.8f, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var accentBrush = new SolidBrush(Theme.Accent);

        // marca
        g.DrawString(Theme.GlyphGame, glyphFont, accentBrush, mx, top);
        g.DrawString("MY GAME CONSOLE", brandFont, textBrush, mx + u * 4.2f, top + u * 0.5f);

        // relógio + status à direita
        var now = DateTime.Now;
        var time = now.ToString("HH:mm");
        var timeSize = g.MeasureString(time, clockFont);
        g.DrawString(time, clockFont, textBrush, w - mx - timeSize.Width, top - u * 1.2f);

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
        g.DrawString(status, statusFont, mutedBrush, w - mx - statusSize.Width, top + u * 4.6f);
    }

    private void DrawRows(Graphics g, int w, int h)
    {
        float u = h / 100f;
        float tile = Math.Min(w / 7.5f, h * 0.21f);
        float gap = tile * 0.14f;
        float mx = w * 0.06f;
        float top = h * 0.22f;
        float rowHeight = tile * 1.12f + u * 11f;
        float avail = w - 2 * mx;
        int visible = Math.Max(1, (int)((avail + gap) / (tile + gap)));

        using var rowTitleFont = new Font("Segoe UI", u * 1.9f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var titleFont = new Font("Segoe UI", u * 2.0f, GraphicsUnit.Pixel);
        using var titleSelFont = new Font("Segoe UI", u * 2.3f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var subFont = new Font("Segoe UI", u * 1.7f, GraphicsUnit.Pixel);
        using var glyphFont = new Font(Theme.IconFontName, tile * 0.40f, GraphicsUnit.Pixel);
        using var pillFont = new Font("Segoe UI", u * 1.25f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var arrowFont = new Font("Segoe UI", u * 3f, GraphicsUnit.Pixel);
        using var accentPen = new Pen(Theme.Accent, u * 0.35f);

        var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var topCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter };

        for (int r = 0; r < _rows.Count; r++)
        {
            var row = _rows[r];
            bool activeRow = r == _row;
            float y = top + r * rowHeight;

            if (activeRow)
            {
                if (_col < row.Scroll) row.Scroll = _col;
                if (_col >= row.Scroll + visible) row.Scroll = _col - visible + 1;
            }
            row.Scroll = Math.Clamp(row.Scroll, 0, Math.Max(0, row.Tiles.Count - visible));

            int alpha = activeRow ? 255 : 120;
            using var rowBrush = new SolidBrush(activeRow ? Theme.Text : Theme.Muted);
            using var textBrush = new SolidBrush(Color.FromArgb(alpha, Theme.Text));
            using var subBrush = new SolidBrush(Color.FromArgb(alpha, Theme.Muted));
            using var tileBrush = new SolidBrush(Color.FromArgb(alpha, Theme.Tile));
            using var tileSelBrush = new SolidBrush(Color.FromArgb(alpha, Theme.TileSelected));
            using var glowBrush = new SolidBrush(Color.FromArgb(60, Theme.Accent));

            g.DrawString(row.Title.ToUpperInvariant(), rowTitleFont, rowBrush, mx, y - u * 4.6f);

            // limpa retângulos de tiles fora da tela (para o mouse não acertá-los)
            foreach (var t in row.Tiles) t.Bounds = RectangleF.Empty;

            int last = Math.Min(row.Tiles.Count, row.Scroll + visible);
            for (int i = row.Scroll; i < last; i++)
            {
                var t = row.Tiles[i];
                bool selected = activeRow && i == _col;
                float x = mx + (i - row.Scroll) * (tile + gap);
                var rect = new RectangleF(x, y, tile, tile);

                if (selected)
                {
                    rect.Inflate(tile * 0.05f, tile * 0.05f);
                    var glowRect = rect;
                    glowRect.Inflate(u * 0.9f, u * 0.9f);
                    using var glowPath = RoundedRect(glowRect, u * 2.2f);
                    g.FillPath(glowBrush, glowPath);
                }

                t.Bounds = rect;

                using (var path = RoundedRect(rect, u * 1.8f))
                {
                    g.FillPath(selected ? tileSelBrush : tileBrush, path);
                    if (selected) g.DrawPath(accentPen, path);
                }

                if (t.Image is { } image)
                {
                    float side = tile * 0.5f;
                    var imgRect = new RectangleF(rect.X + (rect.Width - side) / 2f, rect.Y + (rect.Height - side) / 2f, side, side);
                    if (activeRow)
                    {
                        g.DrawImage(image, imgRect);
                    }
                    else
                    {
                        // fileira inativa: mesma transparência dos glifos
                        using var attrs = new System.Drawing.Imaging.ImageAttributes();
                        attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha / 255f });
                        g.DrawImage(image, Rectangle.Round(imgRect), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attrs);
                    }
                }
                else
                {
                    g.DrawString(t.Glyph, glyphFont, textBrush, rect, center);
                }

                if (t.IsOn is not null)
                {
                    bool on = t.IsOn();
                    var pillText = on ? "LIGADO" : "DESLIGADO";
                    var pillSize = g.MeasureString(pillText, pillFont);
                    var pill = new RectangleF(rect.Right - pillSize.Width - u * 2.4f, rect.Top + u * 1.2f, pillSize.Width + u * 1.6f, pillSize.Height + u * 0.4f);
                    using var pillBrush = new SolidBrush(Color.FromArgb(alpha, on ? Theme.Success : Color.FromArgb(90, 100, 110)));
                    using var pillPath = RoundedRect(pill, pill.Height / 2f);
                    g.FillPath(pillBrush, pillPath);
                    g.DrawString(pillText, pillFont, textBrush, pill, center);
                }

                var labelRect = new RectangleF(rect.X - gap * 0.4f, rect.Bottom + u * 1.2f, rect.Width + gap * 0.8f, u * 3.2f);
                g.DrawString(t.Title, selected ? titleSelFont : titleFont, textBrush, labelRect, topCenter);

                if (selected && !string.IsNullOrEmpty(t.Subtitle))
                {
                    var subRect = new RectangleF(rect.X - tile * 0.6f, labelRect.Bottom + u * 0.3f, rect.Width + tile * 1.2f, u * 4.5f);
                    if (subRect.X < mx * 0.5f) subRect.X = mx * 0.5f;
                    if (subRect.Right > w - mx * 0.5f) subRect.X = w - mx * 0.5f - subRect.Width;
                    g.DrawString(t.Subtitle, subFont, subBrush, subRect, topCenter);
                }
            }

            // indicadores de rolagem
            if (row.Scroll > 0)
            {
                g.DrawString("‹", arrowFont, rowBrush, new RectangleF(mx * 0.35f, y, mx * 0.5f, tile), center);
            }
            if (last < row.Tiles.Count)
            {
                g.DrawString("›", arrowFont, rowBrush, new RectangleF(w - mx * 0.85f, y, mx * 0.5f, tile), center);
            }
        }
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

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
