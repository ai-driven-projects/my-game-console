namespace MyGameConsole.Models;

/// <summary>
/// Atalho do controle: uma combinação de botões segurada por um instante, que vale em qualquer janela
/// (o app lê o controle direto, sem depender de foco). São poucas e fixas, de propósito: a ideia é
/// decorar. A tela "Controle" mostra todas com o desenho do controle.
/// </summary>
/// <param name="Id">Identificador usado nos eventos.</param>
/// <param name="Buttons">Botões que precisam estar pressionados juntos.</param>
/// <param name="ButtonsText">Como a combinação é escrita na tela, ex.: "X + A".</param>
/// <param name="Title">O que o atalho faz.</param>
public sealed record ControllerCombo(string Id, GamepadButtons Buttons, string ButtonsText, string Title)
{
    /// <summary>Tempo padrão que a combinação precisa ficar segurada para disparar.</summary>
    public static readonly TimeSpan DefaultHold = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Quanto tempo segurar. A gravação pede mais: − e A também navegam o Big Picture, e meio segundo com os
    /// dois apertados acontecia sem querer, começando uma gravação que ninguém pediu.
    /// </summary>
    public TimeSpan Hold { get; init; } = DefaultHold;

    /// <summary>"meio segundo" ou "1,5 segundo", para os textos de ajuda.</summary>
    public string HoldText => Hold == DefaultHold ? "meio segundo" : $"{Hold.TotalSeconds:0.#} segundo";

    public const string OpenConsole = "console";
    public const string ToggleMouse = "mouse";
    public const string ToggleKeyboard = "keyboard";
    public const string ToggleRecording = "recording";

    public static readonly ControllerCombo Console = new(
        OpenConsole, GamepadButtons.Back | GamepadButtons.Start, "− + +", "Abrir a tela do console");

    public static readonly ControllerCombo Mouse = new(
        ToggleMouse, GamepadButtons.X | GamepadButtons.A, "X + A", "Ligar ou desligar o mouse pelo analógico");

    public static readonly ControllerCombo Keyboard = new(
        ToggleKeyboard, GamepadButtons.Y | GamepadButtons.B, "Y + B", "Mostrar ou esconder o teclado virtual");

    public static readonly ControllerCombo Recording = new(
        ToggleRecording, GamepadButtons.Back | GamepadButtons.A, "− + A", "Começar ou parar a gravação da tela")
    {
        Hold = TimeSpan.FromSeconds(1.5),
    };

    /// <summary>Todos os atalhos, na ordem em que aparecem na tela "Controle".</summary>
    public static readonly ControllerCombo[] All = [Console, Mouse, Keyboard, Recording];

    /// <summary>Este atalho está ligado nas configurações.</summary>
    public bool IsEnabledIn(AppSettings settings) => Id switch
    {
        OpenConsole => settings.OpenLauncherWithControllerCombo,
        ToggleMouse => settings.ToggleMouseWithControllerCombo,
        ToggleKeyboard => settings.ToggleKeyboardWithControllerCombo,
        ToggleRecording => settings.ToggleRecordingWithControllerCombo,
        _ => false,
    };

    public void SetEnabledIn(AppSettings settings, bool enabled)
    {
        switch (Id)
        {
            case OpenConsole: settings.OpenLauncherWithControllerCombo = enabled; break;
            case ToggleMouse: settings.ToggleMouseWithControllerCombo = enabled; break;
            case ToggleKeyboard: settings.ToggleKeyboardWithControllerCombo = enabled; break;
            case ToggleRecording: settings.ToggleRecordingWithControllerCombo = enabled; break;
        }
    }

    /// <summary>Os atalhos ligados agora, para o <see cref="Services.ControllerComboService"/> vigiar.</summary>
    public static IEnumerable<ControllerCombo> EnabledIn(AppSettings settings) => All.Where(c => c.IsEnabledIn(settings));
}
