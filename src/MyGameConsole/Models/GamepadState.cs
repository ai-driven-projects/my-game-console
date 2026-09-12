namespace MyGameConsole.Models;

/// <summary>Botões digitais de um controle, com os mesmos valores de bit do XInput.</summary>
[Flags]
public enum GamepadButtons : ushort
{
    None = 0,
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
    All = 0xFFFF,
}

/// <summary>
/// Estado normalizado de um controle, independente da origem (XInput ou HID/DirectInput).
/// Analógicos no intervalo -32768..32767; eixo Y positivo para cima, como no XInput.
/// </summary>
public readonly record struct GamepadState(GamepadButtons Buttons, short ThumbLX, short ThumbLY);
