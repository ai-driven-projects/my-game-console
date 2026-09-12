using MyGameConsole.Models;

namespace MyGameConsole.Services;

/// <summary>
/// Gesto global do controle: segurar Back + Start ("−" e "+" no 8BitDo, View + Menu no Xbox)
/// por um curto período dispara <see cref="Triggered"/>. Funciona com qualquer janela em foco e
/// com qualquer origem de controle (XInput ou HID), pois lê o <see cref="ControllerService"/>
/// diretamente, sem depender de foco de janela.
/// </summary>
public sealed class ControllerComboService : IDisposable
{
    private const GamepadButtons ComboMask = GamepadButtons.Back | GamepadButtons.Start;

    /// <summary>Tempo que a combinação precisa ficar segurada para disparar.</summary>
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(500);

    private readonly ControllerService _controllers;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 60 };

    private DateTime? _heldSince;
    private bool _fired;

    /// <summary>Disparado uma vez por gesto; para repetir é preciso soltar os botões.</summary>
    public event EventHandler? Triggered;

    public ControllerComboService(ControllerService controllers)
    {
        _controllers = controllers;
        _timer.Tick += (_, _) => Tick();
    }

    public bool Enabled
    {
        get => _timer.Enabled;
        set
        {
            if (value == _timer.Enabled) return;
            Reset();
            _timer.Enabled = value;
        }
    }

    private void Tick()
    {
        if (_controllers.ConnectedCount == 0)
        {
            Reset();
            return;
        }

        bool comboDown = false;
        foreach (var pad in _controllers.ReadStates())
        {
            if ((pad.Buttons & ComboMask) == ComboMask)
            {
                comboDown = true;
                break;
            }
        }

        if (!comboDown)
        {
            Reset();
            return;
        }

        var now = DateTime.UtcNow;
        _heldSince ??= now;

        if (!_fired && now - _heldSince.Value >= HoldDuration)
        {
            _fired = true;
            Triggered?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Reset()
    {
        _heldSince = null;
        _fired = false;
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
