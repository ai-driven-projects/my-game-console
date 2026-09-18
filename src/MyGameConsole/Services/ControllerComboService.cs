using MyGameConsole.Models;

namespace MyGameConsole.Services;

/// <summary>
/// Atalhos globais do controle (ver <see cref="ControllerCombo"/>): segurar uma combinação de botões
/// por um curto período dispara <see cref="Triggered"/>. Funciona com qualquer janela em foco e com
/// qualquer origem de controle (XInput ou HID), pois lê o <see cref="ControllerService"/> diretamente,
/// sem depender de foco de janela.
/// </summary>
public sealed class ControllerComboService : IDisposable
{
    /// <summary>Tempo que a combinação precisa ficar segurada para disparar.</summary>
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMilliseconds(500);

    private sealed class Watch(ControllerCombo combo)
    {
        public ControllerCombo Combo { get; } = combo;
        public DateTime? HeldSince { get; set; }
        public bool Fired { get; set; }
    }

    private readonly ControllerService _controllers;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 60 };
    private readonly List<Watch> _watches = [];

    /// <summary>Disparado uma vez por gesto; para repetir é preciso soltar os botões.</summary>
    public event EventHandler<ControllerCombo>? Triggered;

    public ControllerComboService(ControllerService controllers)
    {
        _controllers = controllers;
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>
    /// Define quais atalhos estão ligados agora (os demais deixam de ser vigiados). Um atalho que já estava
    /// sendo vigiado mantém o seu estado: sem isso, salvar uma configuração logo depois de um gesto (o que
    /// acontece justamente quando o gesto liga ou desliga algo) zeraria o "já disparou" e o atalho dispararia
    /// de novo enquanto os botões continuassem pressionados.
    /// </summary>
    public void SetActive(IEnumerable<ControllerCombo> combos)
    {
        var updated = new List<Watch>();
        foreach (var combo in combos)
        {
            updated.Add(_watches.FirstOrDefault(w => w.Combo.Id == combo.Id) ?? new Watch(combo));
        }

        _watches.Clear();
        _watches.AddRange(updated);
        _timer.Enabled = _watches.Count > 0;
    }

    private void Tick()
    {
        if (_controllers.ConnectedCount == 0)
        {
            Reset();
            return;
        }

        var buttons = GamepadButtons.None;
        foreach (var pad in _controllers.ReadStates()) buttons |= pad.Buttons;

        var now = DateTime.UtcNow;
        List<ControllerCombo>? fired = null;

        foreach (var watch in _watches)
        {
            if ((buttons & watch.Combo.Buttons) != watch.Combo.Buttons)
            {
                watch.HeldSince = null;
                watch.Fired = false;
                continue;
            }

            watch.HeldSince ??= now;
            if (watch.Fired || now - watch.HeldSince.Value < HoldDuration) continue;

            watch.Fired = true;
            (fired ??= []).Add(watch.Combo);
        }

        // Os avisos saem fora do laço: quem trata um atalho costuma salvar configurações, e isso volta aqui
        // em SetActive — mexer na lista durante o foreach quebraria a enumeração.
        if (fired is null) return;
        foreach (var combo in fired) Triggered?.Invoke(this, combo);
    }

    private void Reset()
    {
        foreach (var watch in _watches)
        {
            watch.HeldSince = null;
            watch.Fired = false;
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
