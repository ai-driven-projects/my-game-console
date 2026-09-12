using MyGameConsole.Models;

namespace MyGameConsole.Forms;

/// <summary>
/// "Repouso de console": a tela (e, quando possível, a luz do teclado) apaga, mas o PC continua
/// ligado e o app impede a suspensão por inatividade. Qualquer botão ou movimento do controle,
/// tecla ou mouse reacende tudo e volta para a tela do console.
/// </summary>
public sealed partial class ConsoleForm
{
    private bool _screenOff;

    public bool IsScreenOff => _screenOff;

    private void EnterScreenOff()
    {
        _confirmText = null;
        _confirmAction = null;
        _confirmNoAction = null;
        _screenOff = true;
        _prevButtons = GamepadButtons.All; // o botão que confirmou ainda pode estar pressionado

        _display.KeepSystemAwake(true);
        _backlight.TryTurnOff();
        _display.TurnOff(Handle);
        Invalidate();
    }

    private void ExitScreenOff()
    {
        if (!_screenOff) return;
        _screenOff = false;
        _prevButtons = GamepadButtons.All; // o botão que acordou não deve virar A/B na tela

        _display.KeepSystemAwake(false);
        _display.TurnOn(Handle);
        _backlight.TryRestore();

        if (Visible)
        {
            Activate();
            Invalidate();
        }
    }
}
