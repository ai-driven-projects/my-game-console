namespace MyGameConsole.Models;

/// <summary>
/// Configurações persistidas em %LocalAppData%\MyGameConsole\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Registrar o app para iniciar junto com o Windows. Ligado por padrão: o app é a porta de entrada do PC em modo
    /// console, e sem ele no boot a tela do console não abre sozinha.
    /// </summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>
    /// Iniciar como administrador (tarefa agendada no logon, em vez da chave Run). Necessário para
    /// controlar a luz do teclado em notebooks Lenovo. Criar a tarefa pede confirmação do UAC uma vez.
    /// </summary>
    public bool StartElevated { get; set; }

    /// <summary>Abrir o Steam Big Picture assim que o app iniciar.</summary>
    public bool OpenBigPictureOnStart { get; set; }

    /// <summary>
    /// Abrir a tela do console (launcher em tela cheia) assim que o app iniciar. Ligado por padrão: ao ligar o PC, a
    /// primeira coisa que aparece é o console, navegável pelo controle.
    /// </summary>
    public bool OpenLauncherOnStart { get; set; } = true;

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
    // Controle: mouse pelo analógico, teclado virtual e atalhos
    // ------------------------------------------------------------------

    /// <summary>Mover o cursor do mouse com o analógico do controle, em qualquer janela do Windows.</summary>
    public bool ControllerMouseEnabled { get; set; }

    /// <summary>
    /// Qual analógico move o cursor. O direito é o padrão: o esquerdo é o que navega menus e jogos,
    /// e usá-lo para o mouse faria o cursor andar toda vez que se navega em outro app. Controles HID que
    /// não publicam o analógico direito (o app mostra isso em "Controles conectados") precisam do esquerdo.
    /// </summary>
    public bool ControllerMouseUseRightStick { get; set; } = true;

    /// <summary>Velocidade do cursor movido pelo analógico, de 1 (lento) a 5 (rápido).</summary>
    public int ControllerMouseSpeed { get; set; } = 3;

    /// <summary>Teclado virtual: falso usa o teclado de toque do Windows (TabTip); verdadeiro, o clássico (osk.exe).</summary>
    public bool VirtualKeyboardClassic { get; set; }

    /// <summary>Ligar e desligar o mouse pelo analógico segurando X + A no controle.</summary>
    public bool ToggleMouseWithControllerCombo { get; set; } = true;

    /// <summary>Mostrar e esconder o teclado virtual segurando Y + B no controle.</summary>
    public bool ToggleKeyboardWithControllerCombo { get; set; } = true;

    // ------------------------------------------------------------------
    // Gravação da tela
    // ------------------------------------------------------------------

    /// <summary>Começar e parar a gravação da tela segurando − + A no controle.</summary>
    public bool ToggleRecordingWithControllerCombo { get; set; } = true;

    /// <summary>Atalho global que começa e para a gravação. Ex.: "Ctrl+Alt+R". Vazio desativa.</summary>
    public string? RecordingHotkey { get; set; } = "Ctrl+Alt+R";

    /// <summary>Gravar também o som do PC (o que sai nos alto-falantes ou no fone).</summary>
    public bool RecordingCaptureAudio { get; set; } = true;

    /// <summary>Gravar também o microfone padrão (narração), misturado ao som do PC na mesma faixa.</summary>
    public bool RecordingCaptureMicrophone { get; set; }

    /// <summary>Quadros por segundo da gravação: 30 ou 60.</summary>
    public int RecordingFramerate { get; set; } = 60;

    /// <summary>Pasta das gravações. Vazio usa Vídeos\My Game Console.</summary>
    public string? RecordingFolder { get; set; }

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

    /// <summary>
    /// Modo Game: entrar direto ao acordar da suspensão/hibernação, sem pedir senha (como na inicialização
    /// com login automático). Vale para todos os planos de energia. Bloquear com Win+L continua pedindo senha.
    /// Gravar exige administrador: o app pede o UAC uma vez ao ligar/desligar (nunca no início do app).
    /// </summary>
    public bool GameModeSkipPasswordOnWake { get; set; } = true;

    /// <summary>
    /// Modo Game: não mostrar a tela cheia "Vamos concluir a configuração do seu dispositivo" nem a de
    /// boas-vindas após atualizações (ajustes do usuário, sem administrador).
    /// </summary>
    public bool GameModeHideSetupPrompts { get; set; } = true;

    /// <summary>
    /// Modo Game: o PC entra direto na tela do console ao ligar, como um console. O app abre a tela do console antes
    /// de tudo (ela cobre a área de trabalho enquanto o Windows termina de carregar) e tira os atrasos de inicialização:
    /// o do Windows para os apps de inicialização (sem administrador) e o da tarefa agendada do app (UAC uma vez).
    /// </summary>
    public bool GameModeBootToConsole { get; set; } = true;

    /// <summary>
    /// Modo Game: tela de bloqueio e de entrada com o papel de parede do console. É um ajuste do computador
    /// (HKLM), por isso gravar pede o UAC uma vez, como a senha ao acordar.
    /// </summary>
    public bool GameModeLockScreen { get; set; } = true;

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
    /// <summary>
    /// "Exigir senha ao acordar" de cada plano de energia (chave = GUID do plano).
    /// Nulo em backups antigos: é preenchido na primeira reaplicação, antes de mexer no ajuste.
    /// </summary>
    public Dictionary<string, WakePasswordState>? WakePasswordByScheme { get; set; }
    /// <summary>
    /// Valores originais das telas de "concluir a configuração" (nulo no valor = não existia).
    /// Nulo em backups antigos: é preenchido na primeira reaplicação, antes de mexer no ajuste.
    /// </summary>
    public Dictionary<string, int?>? SetupPrompts { get; set; }
    /// <summary>
    /// Valores originais do atraso do Windows para os apps de inicialização (nulo no valor = não existia).
    /// Nulo em backups antigos: é preenchido na primeira reaplicação, antes de mexer no ajuste.
    /// </summary>
    public Dictionary<string, int?>? StartupDelay { get; set; }
    /// <summary>
    /// Valores originais da imagem da tela de bloqueio definida para o computador (nulo no valor = não existia).
    /// Nulo em backups antigos: é preenchido na primeira reaplicação, antes de mexer no ajuste.
    /// </summary>
    public Dictionary<string, string?>? LockScreen { get; set; }
}

/// <summary>"Exigir senha ao acordar" de um plano de energia, na tomada e na bateria.</summary>
public sealed class WakePasswordState
{
    public bool PluggedIn { get; set; } = true;
    public bool OnBattery { get; set; } = true;
}
