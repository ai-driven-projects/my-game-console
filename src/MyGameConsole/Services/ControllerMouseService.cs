using System.Runtime.InteropServices;
using MyGameConsole.Models;
using MyGameConsole.Native;

namespace MyGameConsole.Services;

/// <summary>
/// Mouse pelo controle: o analógico direito move o cursor em qualquer janela do Windows, A/X/Y são os
/// cliques e o direcional ▲▼ rola a página. O analógico esquerdo fica livre de propósito — é o que navega
/// menus, o Big Picture e os jogos, e mexer no cursor junto criaria um conflito (quem precisar pode trocar
/// em <see cref="AppSettings.ControllerMouseUseRightStick"/>). Os eventos são injetados com SendInput,
/// então valem para o sistema inteiro, sem depender de foco.
///
/// O estado fica em <see cref="AppSettings.ControllerMouseEnabled"/> (persistente). Enquanto a tela do
/// console está aberta o serviço fica em <see cref="Suspended"/>: lá o controle navega a própria tela.
/// </summary>
public sealed class ControllerMouseService : IDisposable
{
    /// <summary>Botões de face: o clique só sai quando exatamente um deles está pressionado.</summary>
    private const GamepadButtons FaceButtons = GamepadButtons.A | GamepadButtons.B | GamepadButtons.X | GamepadButtons.Y;

    /// <summary>Zona morta dos analógicos, em fração do curso total.</summary>
    private const float DeadZone = 0.22f;

    /// <summary>Repetição da rolagem pelo direcional.</summary>
    private static readonly TimeSpan ScrollRepeat = TimeSpan.FromMilliseconds(110);

    private readonly SettingsService _settings;
    private readonly ControllerService _controllers;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };

    private float _restX;
    private float _restY;
    private bool _leftDown;
    private bool _rightDown;
    private bool _middleDown;
    private GamepadButtons _prevButtons;
    private DateTime _nextDpadScroll;
    private bool _suspended;

    /// <summary>Disparado quando o mouse pelo controle é ligado ou desligado.</summary>
    public event EventHandler? StateChanged;

    public ControllerMouseService(SettingsService settings, ControllerService controllers)
    {
        _settings = settings;
        _controllers = controllers;
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Ligado nas configurações (continua valendo depois de fechar o app).</summary>
    public bool IsEnabled => _settings.Current.ControllerMouseEnabled;

    /// <summary>Ativo de verdade agora: ligado e não suspenso.</summary>
    public bool IsRunning => _timer.Enabled;

    /// <summary>Pausa temporária: na tela do console o controle navega a própria tela.</summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            Sync();
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled) return;
        _settings.Update(s => s.ControllerMouseEnabled = enabled);
        Sync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle() => SetEnabled(!IsEnabled);

    /// <summary>Liga ou desliga a leitura conforme a configuração e a suspensão. Chamar quando as configurações mudarem.</summary>
    public void Sync()
    {
        bool run = IsEnabled && !_suspended;
        if (run == _timer.Enabled) return;

        if (!run) ReleaseButtons();
        _restX = _restY = 0f;
        _prevButtons = GamepadButtons.None;
        _timer.Enabled = run;
    }

    // ------------------------------------------------------------------

    private void Tick()
    {
        if (_controllers.ConnectedCount == 0) return;

        bool right = _settings.Current.ControllerMouseUseRightStick;
        var buttons = GamepadButtons.None;
        float moveX = 0f, moveY = 0f;

        foreach (var pad in _controllers.ReadStates())
        {
            buttons |= pad.Buttons;

            // Com mais de um controle, vale o que estiver mais longe do centro (quem está mexendo manda).
            var (mx, my) = right
                ? StickVector(pad.ThumbRX, pad.ThumbRY)
                : StickVector(pad.ThumbLX, pad.ThumbLY);
            if (MathF.Abs(mx) + MathF.Abs(my) > MathF.Abs(moveX) + MathF.Abs(moveY))
            {
                moveX = mx;
                moveY = my;
            }
        }

        Move(moveX, moveY);
        Scroll(buttons);
        Click(buttons);
        _prevButtons = buttons;
    }

    /// <summary>Direção e intensidade do analógico, já com zona morta e curva de resposta (-1 a 1 em cada eixo).</summary>
    private static (float X, float Y) StickVector(short x, short y)
    {
        float nx = x / 32767f;
        float ny = y / 32767f;
        float mag = MathF.Min(1f, MathF.Sqrt(nx * nx + ny * ny));
        if (mag <= DeadZone) return (0f, 0f);

        // Curva: perto da zona morta o cursor anda devagar; no fim do curso, rápido.
        float t = (mag - DeadZone) / (1f - DeadZone);
        float response = MathF.Pow(t, 1.8f);
        return (nx / mag * response, ny / mag * response);
    }

    private void Move(float x, float y)
    {
        if (x == 0f && y == 0f)
        {
            _restX = _restY = 0f;
            return;
        }

        // Velocidade 1..5 vira 10..26 pixels por quadro (o timer roda a ~60 quadros por segundo).
        float speed = 6f + Math.Clamp(_settings.Current.ControllerMouseSpeed, 1, 5) * 4f;
        float dx = x * speed + _restX;
        float dy = -y * speed + _restY; // no controle o Y é positivo para cima; na tela, para baixo
        int ix = (int)dx;
        int iy = (int)dy;
        _restX = dx - ix; // a sobra fracionária volta no quadro seguinte: movimentos lentos não travam
        _restY = dy - iy;

        if (ix != 0 || iy != 0) Send(NativeMethods.MOUSEEVENTF_MOVE, dx: ix, dy: iy);
    }

    /// <summary>
    /// Rolagem pelo direcional ▲▼: uma "casa" da roda ao pressionar e depois de tantos em tantos milissegundos
    /// enquanto segurar. Os dois analógicos estão ocupados (o direito move o cursor, o esquerdo fica livre para
    /// o app que estiver na frente), então a roda fica aqui.
    /// </summary>
    private void Scroll(GamepadButtons buttons)
    {
        int dpad = (buttons & GamepadButtons.DpadUp) != 0 ? 1 : (buttons & GamepadButtons.DpadDown) != 0 ? -1 : 0;
        if (dpad == 0) return;

        var now = DateTime.UtcNow;
        bool pressedNow = ((buttons & ~_prevButtons) & (GamepadButtons.DpadUp | GamepadButtons.DpadDown)) != 0;
        if (!pressedNow && now < _nextDpadScroll) return;

        _nextDpadScroll = now + ScrollRepeat;
        Send(NativeMethods.MOUSEEVENTF_WHEEL, data: (uint)(dpad * NativeMethods.WHEEL_DELTA));
    }

    /// <summary>
    /// Cliques: A (esquerdo), X (direito) e Y (do meio). Só saem com exatamente um botão de face
    /// pressionado, para que as combinações de atalho (X + A, Y + B) não cliquem sem querer.
    /// Segurar o botão mantém o clique pressionado: arrastar funciona.
    /// </summary>
    private void Click(GamepadButtons buttons)
    {
        var face = buttons & FaceButtons;
        SetButton(ref _leftDown, face == GamepadButtons.A, NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP);
        SetButton(ref _rightDown, face == GamepadButtons.X, NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP);
        SetButton(ref _middleDown, face == GamepadButtons.Y, NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP);
    }

    private static void SetButton(ref bool state, bool wanted, uint downFlag, uint upFlag)
    {
        if (state == wanted) return;
        state = wanted;
        Send(wanted ? downFlag : upFlag);
    }

    /// <summary>Solta o que estiver pressionado ao pausar ou desligar, para não deixar um botão preso.</summary>
    private void ReleaseButtons()
    {
        SetButton(ref _leftDown, false, 0, NativeMethods.MOUSEEVENTF_LEFTUP);
        SetButton(ref _rightDown, false, 0, NativeMethods.MOUSEEVENTF_RIGHTUP);
        SetButton(ref _middleDown, false, 0, NativeMethods.MOUSEEVENTF_MIDDLEUP);
    }

    private static void Send(uint flags, int dx = 0, int dy = 0, uint data = 0)
    {
        var input = new NativeMethods.Input[]
        {
            new()
            {
                type = NativeMethods.INPUT_MOUSE,
                mi = new NativeMethods.MouseInput { dx = dx, dy = dy, mouseData = data, dwFlags = flags },
            },
        };
        NativeMethods.SendInput(1, input, Marshal.SizeOf<NativeMethods.Input>());
    }

    public void Dispose()
    {
        ReleaseButtons();
        _timer.Dispose();
    }
}
