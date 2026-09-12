namespace MyGameConsole.Models;

/// <summary>
/// Configurações persistidas em %LocalAppData%\MyGameConsole\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Registrar o app para iniciar junto com o Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Iniciar como administrador (tarefa agendada no logon, em vez da chave Run). Necessário para
    /// controlar a luz do teclado em notebooks Lenovo. Criar a tarefa pede confirmação do UAC uma vez.
    /// </summary>
    public bool StartElevated { get; set; }

    /// <summary>Abrir o Steam Big Picture assim que o app iniciar.</summary>
    public bool OpenBigPictureOnStart { get; set; }

    /// <summary>Abrir a tela do console (launcher em tela cheia) assim que o app iniciar.</summary>
    public bool OpenLauncherOnStart { get; set; }

    /// <summary>Abrir o Big Picture automaticamente quando um controle for conectado.</summary>
    public bool OpenBigPictureOnControllerConnect { get; set; }

    /// <summary>No "Modo Console", encerrar o explorer.exe (barra de tarefas/área de trabalho).</summary>
    public bool HideExplorerInConsoleMode { get; set; } = true;

    /// <summary>No "Modo Console", abrir o Big Picture automaticamente.</summary>
    public bool OpenBigPictureInConsoleMode { get; set; } = true;

    /// <summary>Caminho manual do Steam (opcional). Se vazio, é detectado pelo registro.</summary>
    public string? SteamPathOverride { get; set; }

    /// <summary>Atalhos personalizados exibidos no menu da bandeja e na tela do console.</summary>
    public List<AppShortcut> Shortcuts { get; set; } = [];

    /// <summary>Consultar as releases do GitHub ao iniciar o app e avisar quando houver versão nova.</summary>
    public bool CheckForUpdatesOnStart { get; set; } = true;

    // ------------------------------------------------------------------
    // Tecla de atalho
    // ------------------------------------------------------------------

    /// <summary>Atalho global que abre a tela do console. Ex.: "Ctrl+Alt+G". Vazio desativa.</summary>
    public string? LauncherHotkey { get; set; } = "Ctrl+Alt+G";

    /// <summary>Abrir a tela do console segurando Back + Start ("−" e "+") no controle.</summary>
    public bool OpenLauncherWithControllerCombo { get; set; } = true;

    /// <summary>
    /// Mapeamento dos botões de controles HID/DirectInput (ex.: 8BitDo por Bluetooth), pelo número
    /// do botão no HID (começa em 1). O padrão segue o layout "Android/D-input" usado pelo 8BitDo.
    /// A tela de configurações mostra os números dos botões pressionados para ajudar a ajustar.
    /// </summary>
    public HidButtonMap HidButtons { get; set; } = new();

    // ------------------------------------------------------------------
    // Modo Game (persistente entre reinicializações)
    // ------------------------------------------------------------------

    /// <summary>Modo Game ativo: os ajustes abaixo ficam aplicados, mesmo após reiniciar o PC.</summary>
    public bool GameModeEnabled { get; set; }

    /// <summary>Modo Game: barra de tarefas em auto-ocultar (aparece ao levar o cursor para baixo).</summary>
    public bool GameModeHideTaskbar { get; set; } = true;

    /// <summary>Modo Game: esconder os ícones da área de trabalho.</summary>
    public bool GameModeHideDesktopIcons { get; set; } = true;

    /// <summary>Modo Game: aplicar o papel de parede do console.</summary>
    public bool GameModeApplyWallpaper { get; set; } = true;

    /// <summary>Papel de parede personalizado do Modo Game. Vazio usa o papel padrão que vem com o app (wallpaper.webp).</summary>
    public string? GameModeWallpaperPath { get; set; }

    /// <summary>Estado original da área de trabalho, guardado ao ativar o Modo Game para restaurar ao desativar.</summary>
    public DesktopStateSnapshot? GameModeBackup { get; set; }
}

/// <summary>Números (a partir de 1) dos botões HID que fazem o papel de cada botão do console.</summary>
public sealed class HidButtonMap
{
    public int A { get; set; } = 1;
    public int B { get; set; } = 2;
    public int X { get; set; } = 4;
    public int Y { get; set; } = 5;
    public int Back { get; set; } = 11;
    public int Start { get; set; } = 12;
}

public sealed class AppShortcut
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Arguments { get; set; }

    public override string ToString() => Name;
}

/// <summary>Fotografia das configurações de área de trabalho que o Modo Game altera.</summary>
public sealed class DesktopStateSnapshot
{
    public string WallpaperPath { get; set; } = string.Empty;
    public string WallpaperStyle { get; set; } = "10";
    public string TileWallpaper { get; set; } = "0";
    /// <summary>
    /// Tipo de plano de fundo da Personalização (0 = imagem, 1 = cor sólida, 2 = apresentação,
    /// 3 = Windows Spotlight). Nulo em backups antigos: nesse caso é deduzido do caminho.
    /// </summary>
    public int? BackgroundType { get; set; }
    public bool TaskbarAutoHide { get; set; }
    public bool DesktopIconsHidden { get; set; }
}
