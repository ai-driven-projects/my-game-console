using MyGameConsole.Models;
using MyGameConsole.Native;

namespace MyGameConsole.Services;

/// <summary>
/// Detecção e leitura de controles de duas origens, entregues em um estado unificado
/// (<see cref="GamepadState"/>): XInput (Xbox e compatíveis, até 4) e HID genéricos
/// (DirectInput, como o 8BitDo por Bluetooth), lidos pelo <see cref="HidGamepadService"/>.
/// </summary>
public sealed class ControllerService : IDisposable
{
    public const int MaxXInputControllers = 4;

    private const GamepadButtons KnownXInputButtons =
        GamepadButtons.DpadUp | GamepadButtons.DpadDown | GamepadButtons.DpadLeft | GamepadButtons.DpadRight |
        GamepadButtons.Start | GamepadButtons.Back |
        GamepadButtons.A | GamepadButtons.B | GamepadButtons.X | GamepadButtons.Y;

    private readonly SettingsService _settings;
    private readonly HidGamepadService _hid = new();
    private readonly bool[] _xinputConnected = new bool[MaxXInputControllers];
    private bool _xinputAvailable = true;

    public int ConnectedCount { get; private set; }
    public int XInputCount { get; private set; }
    public int HidCount => _hid.Count;

    /// <summary>Disparado quando a quantidade de controles conectados muda (antigo, novo).</summary>
    public event Action<int, int>? CountChanged;

    public ControllerService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>Atualiza a lista de controles conectados (XInput e HID). Chamar periodicamente.</summary>
    public void Poll()
    {
        int xinput = 0;
        if (_xinputAvailable)
        {
            for (uint i = 0; i < MaxXInputControllers; i++)
            {
                try
                {
                    _xinputConnected[i] = NativeMethods.XInputGetState(i, out _) == NativeMethods.ERROR_SUCCESS;
                }
                catch (DllNotFoundException)
                {
                    // xinput1_4.dll ausente (muito raro no Windows 10/11)
                    _xinputAvailable = false;
                    Array.Clear(_xinputConnected);
                    break;
                }

                if (_xinputConnected[i]) xinput++;
            }
        }
        XInputCount = xinput;

        try
        {
            _hid.Refresh();
        }
        catch
        {
            // Falha na enumeração HID não pode derrubar o app; tenta de novo no próximo ciclo.
        }

        int count = xinput + _hid.Count;
        if (count != ConnectedCount)
        {
            var old = ConnectedCount;
            ConnectedCount = count;
            CountChanged?.Invoke(old, count);
        }
    }

    /// <summary>Estado atual de cada controle conectado, já normalizado.</summary>
    public IEnumerable<GamepadState> ReadStates()
    {
        if (_xinputAvailable)
        {
            for (uint i = 0; i < MaxXInputControllers; i++)
            {
                if (!_xinputConnected[i]) continue;
                if (NativeMethods.XInputGetState(i, out var state) != NativeMethods.ERROR_SUCCESS) continue;

                var pad = state.Gamepad;
                yield return new GamepadState((GamepadButtons)pad.Buttons & KnownXInputButtons, pad.ThumbLX, pad.ThumbLY, pad.ThumbRX, pad.ThumbRY);
            }
        }

        var map = _settings.Current.HidButtons ?? new HidButtonMap();
        foreach (var device in _hid.Devices)
        {
            if (!device.IsAlive) continue;
            yield return Convert(device.Read(), map);
        }
    }

    /// <summary>Números (a partir de 1) dos botões HID pressionados agora, em qualquer controle HID. Usado para mapear botões.</summary>
    public IEnumerable<int> ReadHidPressedButtons()
    {
        var pressed = new SortedSet<int>();
        foreach (var device in _hid.Devices)
        {
            if (!device.IsAlive) continue;
            var mask = device.Read().ButtonMask;
            for (int n = 1; n <= 32; n++)
            {
                if ((mask & (1u << (n - 1))) != 0) pressed.Add(n);
            }
        }

        return pressed;
    }

    /// <summary>Uma linha por controle, para diagnóstico na tela de configurações.</summary>
    public IEnumerable<string> DescribeControllers()
    {
        for (int i = 0; i < MaxXInputControllers; i++)
        {
            if (_xinputConnected[i]) yield return $"XInput slot {i + 1}: controle Xbox ou compatível";
        }

        foreach (var device in _hid.Devices)
        {
            if (!device.IsAlive) continue;
            var raw = device.Read();
            var pressed = new List<string>(4);
            for (int n = 1; n <= 32; n++)
            {
                if ((raw.ButtonMask & (1u << (n - 1))) != 0) pressed.Add(n.ToString());
            }

            var dpad = string.Concat(raw.Up ? "↑" : "", raw.Down ? "↓" : "", raw.Left ? "←" : "", raw.Right ? "→" : "");
            yield return $"HID (DirectInput) {device.Name} [{device.VendorId:X4}:{device.ProductId:X4}] — " +
                         $"botões: {(pressed.Count == 0 ? "nenhum" : string.Join(", ", pressed))}" +
                         (dpad.Length > 0 ? $" — direcional {dpad}" : string.Empty) +
                         Sticks(raw);
        }
    }

    /// <summary>
    /// Posição dos dois analógicos, em porcentagem do curso. O mouse pelo controle é movido pelo analógico
    /// direito, que nem todo controle HID publica: aqui dá para ver se ele chega.
    /// </summary>
    private static string Sticks(HidRawState raw) =>
        $" — analógicos: esquerdo ({Percent(raw.X)}, {Percent(raw.Y)}), direito ({Percent(raw.RX)}, {Percent(raw.RY)})";

    private static string Percent(short axis) => $"{axis * 100 / short.MaxValue}%";

    private static GamepadState Convert(HidRawState raw, HidButtonMap map)
    {
        var buttons = GamepadButtons.None;
        if (raw.Up) buttons |= GamepadButtons.DpadUp;
        if (raw.Down) buttons |= GamepadButtons.DpadDown;
        if (raw.Left) buttons |= GamepadButtons.DpadLeft;
        if (raw.Right) buttons |= GamepadButtons.DpadRight;
        if (IsPressed(raw.ButtonMask, map.A)) buttons |= GamepadButtons.A;
        if (IsPressed(raw.ButtonMask, map.B)) buttons |= GamepadButtons.B;
        if (IsPressed(raw.ButtonMask, map.X)) buttons |= GamepadButtons.X;
        if (IsPressed(raw.ButtonMask, map.Y)) buttons |= GamepadButtons.Y;
        if (IsPressed(raw.ButtonMask, map.Back)) buttons |= GamepadButtons.Back;
        if (IsPressed(raw.ButtonMask, map.Start)) buttons |= GamepadButtons.Start;

        return new GamepadState(buttons, raw.X, raw.Y, raw.RX, raw.RY);
    }

    private static bool IsPressed(uint mask, int button) =>
        button is >= 1 and <= 32 && (mask & (1u << (button - 1))) != 0;

    public void Dispose()
    {
        _hid.Dispose();
    }
}
