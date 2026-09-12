using System.Drawing.Drawing2D;
using MyGameConsole.App;
using MyGameConsole.Models;
using MyGameConsole.Services;

namespace MyGameConsole.Forms;

/// <summary>
/// Página de configurações dentro da tela do console: lista vertical de opções com tamanhos grandes,
/// painel de descrição ao lado, tudo desenhado em OnPaint e operável só com o controle
/// (A seleciona ou alterna, ◀ ▶ ajusta, B volta). Cada mudança é salva na hora em settings.json.
/// A tecla de atalho e o mapeamento dos botões HID são capturados na própria tela.
/// </summary>
public sealed partial class ConsoleForm
{
    private enum CaptureKind { None, Hotkey, HidButton }

    private sealed class SettingItem
    {
        public required string Title { get; init; }
        public string Description { get; init; } = string.Empty;
        public bool IsHeader { get; init; }
        public bool Danger { get; init; }
        public Func<string>? Value { get; init; }
        public Func<bool>? IsOn { get; init; }
        public Action? OnSelect { get; init; }
        public Action<int>? OnAdjust { get; init; }
        /// <summary>Texto extra (ao vivo) mostrado no painel de detalhes.</summary>
        public Func<string>? Extra { get; init; }
        public RectangleF Bounds { get; set; }
    }

    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(12);

    private readonly List<SettingItem> _items = [];
    private int _itemIndex;
    private int _itemScroll;
    private bool _settingsOpen;
    private string? _lastExtra;

    private CaptureKind _capture;
    private string _captureTitle = string.Empty;
    private string _captureHint = string.Empty;
    private Action<int>? _captureHidTarget;
    private bool _captureArmed;
    private DateTime _captureUntil;

    private SettingItem? CurrentItem =>
        _settingsOpen && _itemIndex >= 0 && _itemIndex < _items.Count ? _items[_itemIndex] : null;

    // ------------------------------------------------------------------
    // Abrir / fechar / salvar
    // ------------------------------------------------------------------

    private void OpenSettingsPage()
    {
        _itemIndex = 0;
        _itemScroll = 0;
        BuildSettingsItems();
        _settingsOpen = true;
        Invalidate();
    }

    private void CloseSettingsPage()
    {
        _settingsOpen = false;
        BuildTiles(); // atalhos ou Modo Game podem ter mudado
        Invalidate();
    }

    private void SetSetting(Action<AppSettings> mutate)
    {
        _settings.Update(mutate);
        BuildSettingsItems();
        Invalidate();
    }

    // ------------------------------------------------------------------
    // Itens
    // ------------------------------------------------------------------

    private void BuildSettingsItems()
    {
        _items.Clear();

        Header("Geral");
        Toggle("Iniciar com o Windows",
            "O app abre junto com o Windows e fica na bandeja, pronto para o atalho ou o gesto do controle.",
            () => _settings.Current.StartWithWindows, v => SetSetting(s => s.StartWithWindows = v));
        Toggle("Iniciar como administrador",
            "Usa uma tarefa agendada no logon com privilégios elevados, em vez da chave Run. Necessário para apagar a luz " +
            "do teclado em notebooks Lenovo no repouso de console. Ligar pede confirmação do UAC uma vez. " +
            "O Steam e os atalhos continuam sendo abertos sem privilégios.",
            () => _settings.Current.StartElevated, v => SetSetting(s => s.StartElevated = v));
        _items.Add(new SettingItem
        {
            Title = "Reiniciar o app agora como administrador",
            Description = "Fecha e reabre o My Game Console elevado (pede confirmação do UAC). Vale até o próximo reinício do app.",
            Value = () => StartupService.IsProcessElevated ? "Já é administrador" : "Sem privilégios agora",
            OnSelect = () =>
            {
                if (StartupService.IsProcessElevated) { ShowNotice("O app já está rodando como administrador."); return; }
                Confirm("Reiniciar o My Game Console como administrador?", _restartElevated);
            },
        });
        Toggle("Abrir esta tela ao iniciar o app",
            "Mostra a tela do console assim que o app inicia. Bom para ligar o PC direto no modo console.",
            () => _settings.Current.OpenLauncherOnStart, v => SetSetting(s => s.OpenLauncherOnStart = v));
        Toggle("Abrir Steam Big Picture ao iniciar o app",
            "Inicia o Steam em modo Big Picture junto com o app.",
            () => _settings.Current.OpenBigPictureOnStart, v => SetSetting(s => s.OpenBigPictureOnStart = v));
        Toggle("Abrir Big Picture ao conectar um controle",
            "Quando o primeiro controle conectar, o Big Picture abre sozinho.",
            () => _settings.Current.OpenBigPictureOnControllerConnect, v => SetSetting(s => s.OpenBigPictureOnControllerConnect = v));
        Toggle("Abrir esta tela segurando − e + no controle",
            "Segure Back + Start (− e + no 8BitDo, View + Menu no Xbox) por meio segundo, em qualquer janela.",
            () => _settings.Current.OpenLauncherWithControllerCombo, v => SetSetting(s => s.OpenLauncherWithControllerCombo = v));
        _items.Add(new SettingItem
        {
            Title = "Tecla de atalho desta tela",
            Description = "Pressione A e, em seguida, a combinação no teclado (ex.: Ctrl+Alt+G). " +
                          "Use Ctrl, Alt ou Shift com uma tecla, ou F1 a F24. Backspace desativa.",
            Value = () => string.IsNullOrWhiteSpace(_settings.Current.LauncherHotkey) ? "Desativada" : _settings.Current.LauncherHotkey!,
            OnSelect = StartHotkeyCapture,
        });

        Header("Modo Console");
        Toggle("Encerrar o explorer.exe no Modo Console",
            "Esconde a barra de tarefas e a área de trabalho enquanto o Modo Console estiver ativo. É restaurado ao sair.",
            () => _settings.Current.HideExplorerInConsoleMode, v => SetSetting(s => s.HideExplorerInConsoleMode = v));
        Toggle("Abrir Big Picture no Modo Console",
            "Ao entrar no Modo Console, o Steam Big Picture abre automaticamente.",
            () => _settings.Current.OpenBigPictureInConsoleMode, v => SetSetting(s => s.OpenBigPictureInConsoleMode = v));

        Header("Modo Game");
        Toggle("Modo Game",
            "Aplica os ajustes abaixo e mantém após reiniciar o PC. Desligar restaura a área de trabalho original.",
            () => _gameMode.IsEnabled, v => { if (v != _gameMode.IsEnabled) ToggleGameMode(); });
        Toggle("Barra de tarefas em auto-ocultar",
            "A barra some e reaparece ao levar o cursor para a parte de baixo da tela.",
            () => _settings.Current.GameModeHideTaskbar, v => SetSetting(s => s.GameModeHideTaskbar = v));
        Toggle("Esconder os ícones da área de trabalho",
            "Deixa a área de trabalho limpa, só com o papel de parede.",
            () => _settings.Current.GameModeHideDesktopIcons, v => SetSetting(s => s.GameModeHideDesktopIcons = v));
        Toggle("Aplicar papel de parede do console",
            "Usa o papel de parede padrão do app ou a imagem escolhida abaixo.",
            () => _settings.Current.GameModeApplyWallpaper, v => SetSetting(s => s.GameModeApplyWallpaper = v));
        _items.Add(new SettingItem
        {
            Title = "Imagem do papel de parede",
            Description = "A escolhe uma imagem (abre o seletor de arquivos do Windows). ◀ volta ao papel de parede padrão do app.",
            Value = () => string.IsNullOrWhiteSpace(_settings.Current.GameModeWallpaperPath) ? "Padrão do app" : SafeFileName(_settings.Current.GameModeWallpaperPath!),
            OnSelect = BrowseWallpaper,
            OnAdjust = dx => { if (dx < 0 && _settings.Current.GameModeWallpaperPath is not null) SetSetting(s => s.GameModeWallpaperPath = null); },
        });

        Header("Controle");
        _items.Add(new SettingItem
        {
            Title = "Controles conectados",
            Description = "Controles Xbox e compatíveis (inclusive 8BitDo por dongle 2.4G ou cabo) chegam por XInput e não precisam de mapeamento. " +
                          "O 8BitDo por Bluetooth chega como HID (DirectInput) e usa o mapeamento abaixo. " +
                          "Pressione botões para ver os números que o controle envia.",
            Value = () => _controllers.ConnectedCount switch { 0 => "Nenhum", 1 => "1 controle", var n => $"{n} controles" },
            Extra = () => string.Join("\n", _controllers.DescribeControllers()),
        });
        HidMapItem("A", m => m.A, (m, n) => m.A = n);
        HidMapItem("B", m => m.B, (m, n) => m.B = n);
        HidMapItem("X", m => m.X, (m, n) => m.X = n);
        HidMapItem("Y", m => m.Y, (m, n) => m.Y = n);
        HidMapItem("− (Back)", m => m.Back, (m, n) => m.Back = n);
        HidMapItem("+ (Start)", m => m.Start, (m, n) => m.Start = n);
        _items.Add(new SettingItem
        {
            Title = "Restaurar mapeamento padrão (8BitDo)",
            Description = "Volta ao layout D-input do 8BitDo: A=1, B=2, X=4, Y=5, −=11, +=12.",
            OnSelect = () => { SetSetting(s => s.HidButtons = new HidButtonMap()); ShowNotice("Mapeamento padrão restaurado."); },
        });

        Header("Steam");
        _items.Add(new SettingItem
        {
            Title = "Pasta do Steam",
            Description = "Normalmente é detectada sozinha pelo registro. A escolhe a pasta manualmente (abre o seletor do Windows). ◀ volta à detecção automática.",
            Value = () =>
            {
                var manual = _settings.Current.SteamPathOverride;
                if (!string.IsNullOrWhiteSpace(manual)) return manual;
                var detected = _steam.FindSteamPath();
                return detected is null ? "Não detectada" : $"Automática: {detected}";
            },
            OnSelect = BrowseSteamPath,
            OnAdjust = dx => { if (dx < 0 && _settings.Current.SteamPathOverride is not null) SetSetting(s => s.SteamPathOverride = null); },
        });

        Header("Atalhos de jogos e apps");
        _items.Add(new SettingItem
        {
            Title = "Adicionar atalho",
            Description = "Escolha um programa (.exe), atalho (.lnk) ou script. Ele aparece na fileira \"Jogos e apps\" desta tela e no menu da bandeja.",
            Value = () => _settings.Current.Shortcuts.Count switch { 0 => "Nenhum atalho", 1 => "1 atalho", var n => $"{n} atalhos" },
            OnSelect = AddShortcut,
        });
        foreach (var sc in _settings.Current.Shortcuts)
        {
            var shortcut = sc;
            _items.Add(new SettingItem
            {
                Title = shortcut.Name,
                Description = $"Caminho: {shortcut.Path}" +
                              (string.IsNullOrWhiteSpace(shortcut.Arguments) ? string.Empty : $"\nArgumentos: {shortcut.Arguments}") +
                              "\n\nA remove este atalho (com confirmação). Para mudar nome ou argumentos, use a janela clássica em \"Avançado\".",
                Value = () => "A  Remover",
                OnSelect = () => Confirm($"Remover o atalho \"{shortcut.Name}\"?", () =>
                {
                    SetSetting(s => s.Shortcuts.Remove(shortcut));
                    ShowNotice($"Atalho \"{shortcut.Name}\" removido.");
                }),
            });
        }

        Header("Atualizações");
        Toggle("Verificar atualizações ao iniciar",
            "Ao abrir o app, consulta as versões publicadas no GitHub e avisa na bandeja quando houver uma nova. " +
            "Nada é baixado nem instalado sem você confirmar.",
            () => _settings.Current.CheckForUpdatesOnStart, v => SetSetting(s => s.CheckForUpdatesOnStart = v));
        _items.Add(new SettingItem
        {
            Title = "Verificar atualizações agora",
            Description = "Consulta a release mais recente no GitHub. Se houver uma versão nova, pergunta antes de baixar o instalador " +
                          "e executá-lo: o My Game Console é fechado, atualizado e reaberto ao final (o Windows pede o UAC).",
            Value = () => _updates.StatusText,
            Extra = () => _updates.Available is { } u
                ? $"Novidades da versão {u.VersionText}:\n{(string.IsNullOrWhiteSpace(u.Notes) ? "(a release não tem notas)" : u.Notes)}"
                : string.Empty,
            OnSelect = () => _ = RunUpdateFlowAsync(),
        });

        Header("Avançado");
        _items.Add(new SettingItem
        {
            Title = "Janela de configurações clássica",
            Description = "Abre a janela tradicional, para teclado e mouse. Útil para editar nome e argumentos dos atalhos.",
            OnSelect = () => { Hide(); _openSettings(); },
        });

        _itemIndex = Math.Clamp(_itemIndex, 0, _items.Count - 1);
        if (_items[_itemIndex].IsHeader) _itemIndex = NextSelectable(_itemIndex, 1);
    }

    private void Header(string title) => _items.Add(new SettingItem { Title = title, IsHeader = true });

    // ------------------------------------------------------------------
    // Atualização (verificar -> confirmar -> baixar -> instalar), tudo com os overlays da própria tela
    // ------------------------------------------------------------------

    private async Task RunUpdateFlowAsync()
    {
        try
        {
            switch (_updates.State)
            {
                case UpdateState.Checking:
                case UpdateState.Downloading:
                    return; // já em andamento; o valor do item mostra o progresso
                case UpdateState.ReadyToInstall:
                    ConfirmInstallUpdate();
                    return;
                case UpdateState.Available:
                    ConfirmDownloadUpdate();
                    return;
            }

            ShowNotice("Consultando as releases no GitHub...");
            var info = await _updates.CheckAsync();
            if (!Visible || !_settingsOpen) return;

            if (info is null)
            {
                ShowNotice($"Você já está na versão mais recente ({_updates.CurrentVersionText}).");
                return;
            }

            if (_updates.State == UpdateState.ReadyToInstall) ConfirmInstallUpdate();
            else ConfirmDownloadUpdate();
        }
        catch (Exception ex)
        {
            ShowNotice(ex.Message, isError: true);
        }
    }

    private void ConfirmDownloadUpdate()
    {
        var u = _updates.Available!;
        Confirm($"Baixar e instalar a versão {u.VersionText}? O My Game Console será fechado para atualizar.",
            () => _ = DownloadAndInstallUpdateAsync());
    }

    private void ConfirmInstallUpdate()
    {
        var u = _updates.Available!;
        Confirm($"Instalar a versão {u.VersionText} agora? O My Game Console será fechado para atualizar.", InstallUpdate);
    }

    private async Task DownloadAndInstallUpdateAsync()
    {
        try
        {
            await _updates.DownloadAsync();
            InstallUpdate();
        }
        catch (Exception ex)
        {
            ShowNotice(ex.Message, isError: true);
        }
    }

    private void InstallUpdate()
    {
        _updates.Install();
        _exitApp();
    }

    private void Toggle(string title, string description, Func<bool> get, Action<bool> set)
    {
        _items.Add(new SettingItem
        {
            Title = title,
            Description = description,
            IsOn = get,
            OnSelect = () => set(!get()),
            OnAdjust = dx => { bool v = dx > 0; if (get() != v) set(v); },
        });
    }

    private void HidMapItem(string label, Func<HidButtonMap, int> get, Action<HidButtonMap, int> set)
    {
        HidButtonMap Map() => _settings.Current.HidButtons ??= new HidButtonMap();

        _items.Add(new SettingItem
        {
            Title = $"Botão {label} no controle HID",
            Description = $"Qual botão físico do controle Bluetooth/HID faz o papel de {label}. " +
                          "Pressione A e depois o botão no controle. ◀ ▶ ajusta o número manualmente.",
            Value = () => $"HID nº {get(Map())}",
            OnSelect = () => StartHidCapture(label, n => SetSetting(s => set(s.HidButtons ??= new HidButtonMap(), n))),
            OnAdjust = dx => SetSetting(s =>
            {
                var m = s.HidButtons ??= new HidButtonMap();
                set(m, Math.Clamp(get(m) + dx, 1, 32));
            }),
        });
    }

    // ------------------------------------------------------------------
    // Navegação
    // ------------------------------------------------------------------

    private int NextSelectable(int from, int dir)
    {
        for (int i = from + dir; i >= 0 && i < _items.Count; i += dir)
        {
            if (!_items[i].IsHeader) return i;
        }

        return Math.Clamp(from, 0, Math.Max(0, _items.Count - 1));
    }

    private void SettingsMove(int dx, int dy)
    {
        if (dy != 0)
        {
            _itemIndex = NextSelectable(_itemIndex, dy);
            Invalidate();
        }

        if (dx != 0 && CurrentItem is { OnAdjust: { } adjust })
        {
            Run(() => adjust(dx));
        }
    }

    private void SettingsActivate() => Run(CurrentItem?.OnSelect);

    private void SettingsMouseMove(Point p)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (!_items[i].IsHeader && _items[i].Bounds.Contains(p) && i != _itemIndex)
            {
                _itemIndex = i;
                Invalidate();
                return;
            }
        }
    }

    private void SettingsMouseClick(Point p)
    {
        if (CurrentItem is { } item && item.Bounds.Contains(p)) SettingsActivate();
    }

    /// <summary>Chamado a cada leitura do controle: redesenha se o texto ao vivo do item mudou.</summary>
    private void SettingsTick()
    {
        var extra = CurrentItem?.Extra?.Invoke();
        if (extra != _lastExtra)
        {
            _lastExtra = extra;
            Invalidate();
        }
    }

    // ------------------------------------------------------------------
    // Captura de tecla de atalho e de botão HID
    // ------------------------------------------------------------------

    private void StartHotkeyCapture()
    {
        _capture = CaptureKind.Hotkey;
        _captureTitle = "Pressione a combinação de teclas no teclado";
        _captureHint = "Ctrl, Alt ou Shift + tecla, ou F1 a F24   ·   Backspace desativa   ·   Esc cancela";
        _captureUntil = DateTime.UtcNow + CaptureTimeout;
        Invalidate();
    }

    private void StartHidCapture(string label, Action<int> assign)
    {
        if (_controllers.HidCount == 0)
        {
            ShowNotice("Nenhum controle HID/Bluetooth conectado. Controles XInput (dongle ou cabo) não precisam de mapeamento.");
            return;
        }

        _capture = CaptureKind.HidButton;
        _captureHidTarget = assign;
        _captureArmed = false;
        _captureTitle = $"Pressione no controle o botão que será \"{label}\"";
        _captureHint = "Solte todos os botões e pressione só o desejado   ·   Esc cancela";
        _captureUntil = DateTime.UtcNow + CaptureTimeout;
        Invalidate();
    }

    private void CloseCapture()
    {
        if (_capture == CaptureKind.None) return;
        _capture = CaptureKind.None;
        _captureHidTarget = null;
        _captureArmed = false;
        _prevButtons = GamepadButtons.All; // o botão ainda pode estar pressionado; não deve virar A/B
        Invalidate();
    }

    private void HandleHotkeyCapture(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode == Keys.Escape)
        {
            CloseCapture();
            return;
        }

        if (e.KeyCode is Keys.Back or Keys.Delete)
        {
            SetSetting(s => s.LauncherHotkey = null);
            CloseCapture();
            ShowNotice("Tecla de atalho desativada.");
            return;
        }

        var text = HotkeyService.Format(e);
        if (text is null) return; // só modificador pressionado

        if (!e.Control && !e.Alt && !e.Shift && e.KeyCode is < Keys.F1 or > Keys.F24)
        {
            ShowNotice("Use Ctrl, Alt ou Shift junto com a tecla, ou uma tecla de F1 a F24.", isError: true);
            return;
        }

        SetSetting(s => s.LauncherHotkey = text);
        CloseCapture();
        ShowNotice($"Tecla de atalho: {text}");
    }

    private void PollHidCapture()
    {
        var pressed = _controllers.ReadHidPressedButtons().ToList();

        if (!_captureArmed)
        {
            if (pressed.Count == 0) _captureArmed = true;
            return;
        }

        if (pressed.Count == 0) return;

        int n = pressed[0];
        var assign = _captureHidTarget;
        CloseCapture();
        Run(() => assign?.Invoke(n));
        ShowNotice($"Botão HID nº {n} atribuído.");
    }

    // ------------------------------------------------------------------
    // Diálogos do Windows (única parte que precisa de mouse ou teclado)
    // ------------------------------------------------------------------

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
            SetSetting(s => s.GameModeWallpaperPath = dlg.FileName);
            ShowNotice("Papel de parede atualizado.");
        }

        Activate();
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
            SetSetting(s => s.SteamPathOverride = dlg.SelectedPath);
        }

        Activate();
    }

    private void AddShortcut()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Escolha o programa, atalho ou script",
            Filter = "Programas e atalhos|*.exe;*.lnk;*.bat;*.cmd;*.url|Todos os arquivos|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var name = Path.GetFileNameWithoutExtension(dlg.FileName);
            SetSetting(s => s.Shortcuts.Add(new AppShortcut { Name = name, Path = dlg.FileName }));
            ShowNotice($"Atalho \"{name}\" adicionado.");
        }

        Activate();
    }

    // ------------------------------------------------------------------
    // Desenho
    // ------------------------------------------------------------------

    private void DrawSettings(Graphics g, int w, int h)
    {
        float u = h / 100f;
        float mx = w * 0.06f;
        float listX = mx;
        float listW = w * 0.54f;
        float top = h * 0.215f;
        float bottom = h - u * 11f;
        float rowH = u * 6.4f;
        float gap = u * 0.7f;
        int visible = Math.Max(1, (int)((bottom - top + gap) / (rowH + gap)));

        if (_itemIndex < _itemScroll) _itemScroll = _itemIndex;
        if (_itemIndex >= _itemScroll + visible) _itemScroll = _itemIndex - visible + 1;
        // Mantém o cabeçalho da seção visível quando o item selecionado é o primeiro dela.
        if (_itemScroll > 0 && _items[_itemScroll - 1].IsHeader && _itemIndex < _itemScroll + visible - 1) _itemScroll--;
        _itemScroll = Math.Clamp(_itemScroll, 0, Math.Max(0, _items.Count - visible));

        using var pageFont = new Font("Segoe UI", u * 2.6f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var headerFont = new Font("Segoe UI", u * 1.7f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var titleFont = new Font("Segoe UI", u * 2.1f, GraphicsUnit.Pixel);
        using var titleSelFont = new Font("Segoe UI", u * 2.1f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var valueFont = new Font("Segoe UI", u * 1.9f, GraphicsUnit.Pixel);
        using var pillFont = new Font("Segoe UI", u * 1.3f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var panelTitleFont = new Font("Segoe UI", u * 2.4f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var panelFont = new Font("Segoe UI", u * 1.85f, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var accentBrush = new SolidBrush(Theme.Accent);
        using var dangerBrush = new SolidBrush(Theme.Danger);
        using var tileBrush = new SolidBrush(Color.FromArgb(200, Theme.Tile));
        using var tileSelBrush = new SolidBrush(Theme.TileSelected);
        using var glowBrush = new SolidBrush(Color.FromArgb(60, Theme.Accent));
        using var panelBrush = new SolidBrush(Color.FromArgb(110, Theme.Tile));
        using var accentPen = new Pen(Theme.Accent, u * 0.3f);

        var leftMid = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        var rightMid = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap };
        var bottomLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Far };
        var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        g.DrawString("CONFIGURAÇÕES", pageFont, textBrush, mx, h * 0.15f);

        foreach (var it in _items) it.Bounds = RectangleF.Empty;

        int last = Math.Min(_items.Count, _itemScroll + visible);
        for (int i = _itemScroll; i < last; i++)
        {
            var it = _items[i];
            float y = top + (i - _itemScroll) * (rowH + gap);
            var rect = new RectangleF(listX, y, listW, rowH);

            if (it.IsHeader)
            {
                g.DrawString(it.Title.ToUpperInvariant(), headerFont, mutedBrush,
                    new RectangleF(rect.X + u * 0.4f, rect.Y, rect.Width, rect.Height - u * 0.6f), bottomLeft);
                continue;
            }

            it.Bounds = rect;
            bool selected = i == _itemIndex;

            if (selected)
            {
                var glowRect = rect;
                glowRect.Inflate(u * 0.7f, u * 0.7f);
                using var glowPath = RoundedRect(glowRect, u * 1.8f);
                g.FillPath(glowBrush, glowPath);
            }

            using (var path = RoundedRect(rect, u * 1.4f))
            {
                g.FillPath(selected ? tileSelBrush : tileBrush, path);
                if (selected) g.DrawPath(accentPen, path);
            }

            float valueW = listW * 0.42f;
            var titleRect = new RectangleF(rect.X + u * 2.4f, rect.Y, rect.Width - valueW - u * 4f, rect.Height);
            g.DrawString(it.Title, selected ? titleSelFont : titleFont, it.Danger ? dangerBrush : textBrush, titleRect, leftMid);

            if (it.IsOn is not null)
            {
                bool on = it.IsOn();
                var pillText = on ? "LIGADO" : "DESLIGADO";
                var size = g.MeasureString(pillText, pillFont);
                float ph = size.Height + u * 0.8f;
                var pill = new RectangleF(rect.Right - size.Width - u * 4.4f, rect.Y + (rowH - ph) / 2f, size.Width + u * 2.4f, ph);
                using var pillBrush = new SolidBrush(on ? Theme.Success : Color.FromArgb(90, 100, 110));
                using var pillPath = RoundedRect(pill, pill.Height / 2f);
                g.FillPath(pillBrush, pillPath);
                g.DrawString(pillText, pillFont, textBrush, pill, center);
            }
            else if (it.Value is not null)
            {
                var valueRect = new RectangleF(rect.Right - valueW - u * 2.4f, rect.Y, valueW, rect.Height);
                g.DrawString(it.Value(), valueFont, selected ? accentBrush : mutedBrush, valueRect, rightMid);
            }
        }

        // barra de rolagem
        if (_items.Count > visible)
        {
            var track = new RectangleF(listX + listW + u * 1.4f, top, u * 0.6f, bottom - top);
            float thumbH = Math.Max(u * 3f, track.Height * visible / _items.Count);
            float thumbY = track.Y + (track.Height - thumbH) * _itemScroll / Math.Max(1, _items.Count - visible);
            using var trackBrush = new SolidBrush(Color.FromArgb(60, Theme.Muted));
            using var thumbBrush = new SolidBrush(Theme.Muted);
            using (var tp = RoundedRect(track, track.Width / 2f)) g.FillPath(trackBrush, tp);
            using (var hp = RoundedRect(new RectangleF(track.X, thumbY, track.Width, thumbH), track.Width / 2f)) g.FillPath(thumbBrush, hp);
        }

        // painel de detalhes do item selecionado
        if (CurrentItem is { } cur)
        {
            float px = listX + listW + u * 5f;
            var panel = new RectangleF(px, top, w - mx - px, bottom - top);
            using (var pp = RoundedRect(panel, u * 1.6f)) g.FillPath(panelBrush, pp);

            float pad = u * 2.6f;
            float innerW = panel.Width - 2 * pad;
            float yy = panel.Y + pad;

            g.DrawString(cur.Title, panelTitleFont, textBrush, new RectangleF(panel.X + pad, yy, innerW, u * 9f));
            yy += g.MeasureString(cur.Title, panelTitleFont, (int)innerW).Height + u * 1.5f;

            if (!string.IsNullOrEmpty(cur.Description))
            {
                var dRect = new RectangleF(panel.X + pad, yy, innerW, Math.Max(0, panel.Bottom - yy - pad));
                g.DrawString(cur.Description, panelFont, mutedBrush, dRect);
                yy += g.MeasureString(cur.Description, panelFont, (int)innerW).Height + u * 2f;
            }

            if (cur.Extra?.Invoke() is { Length: > 0 } extra && yy < panel.Bottom - pad)
            {
                var eRect = new RectangleF(panel.X + pad, yy, innerW, panel.Bottom - yy - pad);
                g.DrawString(extra, panelFont, accentBrush, eRect);
            }
        }
    }

    private void DrawSettingsFooter(Graphics g, int w, int h)
    {
        float u = h / 100f;
        float mx = w * 0.06f;
        float y = h - u * 7f;

        using var font = new Font("Segoe UI", u * 1.9f, GraphicsUnit.Pixel);
        using var mutedBrush = new SolidBrush(Theme.Muted);

        float x = mx;
        x = DrawHint(g, font, x, y, "A", "Selecionar / alternar", u);
        x = DrawHint(g, font, x, y, "◀▶", "Ajustar", u);
        DrawHint(g, font, x, y, "B", "Voltar", u);

        const string right = "Esc  Voltar   ·   As alterações são salvas na hora";
        var size = g.MeasureString(right, font);
        g.DrawString(right, font, mutedBrush, w - mx - size.Width, y + (u * 3.4f - size.Height) / 2f);
    }

    private void DrawCapture(Graphics g, int w, int h)
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
        using var hintFont = new Font("Segoe UI", u * 1.8f, GraphicsUnit.Pixel);
        using var liveFont = new Font("Segoe UI", u * 2.2f, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var accentBrush = new SolidBrush(Theme.Accent);

        var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        g.DrawString(_captureTitle, qFont, textBrush,
            new RectangleF(panel.X + u * 2f, panel.Y + u * 2f, panel.Width - u * 4f, ph * 0.4f), center);

        if (_capture == CaptureKind.HidButton)
        {
            var pressed = _controllers.ReadHidPressedButtons().ToList();
            var live = pressed.Count == 0
                ? (_captureArmed ? "Aguardando o botão..." : "Solte todos os botões do controle")
                : $"Botões pressionados: {string.Join(", ", pressed)}";
            g.DrawString(live, liveFont, accentBrush,
                new RectangleF(panel.X, panel.Y + ph * 0.48f, panel.Width, u * 5f), center);
        }

        int remaining = Math.Max(0, (int)Math.Ceiling((_captureUntil - DateTime.UtcNow).TotalSeconds));
        g.DrawString($"{_captureHint}   ·   {remaining}s", hintFont, mutedBrush,
            new RectangleF(panel.X + u * 2f, panel.Bottom - u * 6f, panel.Width - u * 4f, u * 4f), center);
    }
}
