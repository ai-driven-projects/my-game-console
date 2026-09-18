using MyGameConsole.App;
using MyGameConsole.Models;

namespace MyGameConsole.Forms;

/// <summary>
/// Página "Controle" da tela do console: o mapa dos atalhos do controle, com um controle desenhado
/// ao lado (<see cref="GamepadArt"/>). O item selecionado acende no desenho os botões que ele usa —
/// é assim que os atalhos ficam fáceis de decorar. Daqui também se liga o mouse pelo analógico e o
/// teclado virtual.
/// </summary>
public sealed partial class ConsoleForm
{
    private void OpenControllerPage() => OpenPage("CONTROLE", BuildControllerItems, DrawGamepadArt);

    /// <summary>Desenha o controle no topo do painel de detalhes, com os botões do item selecionado acesos.</summary>
    private void DrawGamepadArt(Graphics g, RectangleF area) =>
        GamepadArt.Draw(g, area, CurrentItem?.Highlight?.Invoke() ?? PadPart.None);

    /// <summary>O analógico que move o cursor agora, para acender o certo no desenho.</summary>
    private PadPart MouseStick =>
        _settings.Current.ControllerMouseUseRightStick ? PadPart.RightStick : PadPart.LeftStick;

    // ------------------------------------------------------------------
    // Itens
    // ------------------------------------------------------------------

    private void BuildControllerItems()
    {
        _items.Clear();

        Header("Mouse pelo analógico");
        Toggle("Mouse pelo analógico",
            "O analógico direito move o cursor do mouse em qualquer janela do Windows, sem precisar de teclado. " +
            "O esquerdo fica livre: é ele que navega menus, o Big Picture e os jogos. " +
            "Continua ligado depois de fechar o app.\n\n" +
            "Enquanto esta tela estiver aberta o mouse fica em pausa: aqui o controle navega a própria tela. " +
            "Feche com B para usá-lo no Windows.",
            () => _mouse.IsEnabled,
            v =>
            {
                _mouse.SetEnabled(v);
                RebuildPage();
                ShowNotice(v
                    ? "Mouse pelo analógico ligado. Feche esta tela (B) para usá-lo no Windows."
                    : "Mouse pelo analógico desligado.");
            },
            MouseStick);
        _items.Add(new SettingItem
        {
            Title = "Analógico que move o cursor",
            Description = "O direito é o padrão, para não disputar com o esquerdo, que navega menus e jogos. " +
                          "Troque para o esquerdo só se o seu controle não enviar o analógico direito — dá para " +
                          "conferir em Configurações > Controle > \"Controles conectados\", que mostra a posição dos dois. " +
                          "A ou ◀ ▶ alterna.",
            Value = () => _settings.Current.ControllerMouseUseRightStick ? "Direito" : "Esquerdo",
            Highlight = () => _settings.Current.ControllerMouseUseRightStick ? PadPart.RightStick : PadPart.LeftStick,
            OnSelect = () => SetSetting(s => s.ControllerMouseUseRightStick = !s.ControllerMouseUseRightStick),
            OnAdjust = dx => SetSetting(s => s.ControllerMouseUseRightStick = dx < 0),
        });
        _items.Add(new SettingItem
        {
            Title = "Velocidade do cursor",
            Description = "Quanto o cursor anda com o analógico no fim do curso. ◀ ▶ ajusta de 1 (lento) a 5 (rápido). " +
                          "Perto do centro do analógico o cursor sempre anda devagar, para acertar alvos pequenos.",
            Value = () => $"{Math.Clamp(_settings.Current.ControllerMouseSpeed, 1, 5)} de 5",
            Highlight = () => MouseStick,
            OnSelect = () => SetSetting(s => s.ControllerMouseSpeed = s.ControllerMouseSpeed >= 5 ? 1 : s.ControllerMouseSpeed + 1),
            OnAdjust = dx => SetSetting(s => s.ControllerMouseSpeed = Math.Clamp(s.ControllerMouseSpeed + dx, 1, 5)),
        });
        Info("Clique esquerdo", "A", PadPart.A,
            "Com o mouse pelo analógico ligado, A é o clique esquerdo. Segurar A mantém o botão pressionado: dá para arrastar.");
        Info("Clique direito", "X", PadPart.X,
            "Abre o menu de contexto, como o botão direito do mouse.");
        Info("Clique do meio", "Y", PadPart.Y,
            "O mesmo que apertar a rodinha do mouse (abrir um link em nova aba, por exemplo).");
        Info("Rolar a página", "Direcional ▲ ▼", PadPart.Dpad,
            "Cima e baixo no direcional rolam a página, como a rodinha do mouse. Segure para continuar rolando. " +
            "A roda fica no direcional porque o analógico direito move o cursor e o esquerdo é do app que estiver na frente.");
        Info("Por que dois botões juntos não clicam", "A, X ou Y sozinhos", PadPart.A | PadPart.X | PadPart.Y,
            "O clique só sai com um botão de face pressionado por vez. É o que permite usar X + A e Y + B como atalhos " +
            "sem clicar sem querer.");

        Header("Teclado virtual");
        Toggle("Mostrar o teclado virtual agora",
            "Abre o teclado na tela para digitar com o cursor movido pelo analógico (clique com A). " +
            "Fecha do mesmo jeito, ou pelo próprio teclado.",
            () => _keyboard.IsVisible,
            v =>
            {
                if (v) _keyboard.Show();
                else _keyboard.Hide();
                RebuildPage();
                ShowNotice(v
                    ? "Teclado virtual aberto no Windows, atrás desta tela. Feche com B para usá-lo."
                    : "Teclado virtual fechado.");
            });
        _items.Add(new SettingItem
        {
            Title = "Tipo de teclado",
            Description = "\"De toque\" é o teclado do Windows 11, com teclas grandes. \"Clássico\" é o osk.exe, uma janela " +
                          "comum que funciona em qualquer Windows e pode ser movida e redimensionada. A alterna entre os dois.",
            Value = () => _settings.Current.VirtualKeyboardClassic ? "Clássico (osk.exe)" : "De toque (Windows)",
            OnSelect = () => SetSetting(s => s.VirtualKeyboardClassic = !s.VirtualKeyboardClassic),
            OnAdjust = dx => SetSetting(s => s.VirtualKeyboardClassic = dx > 0),
        });

        Header("Atalhos do controle");
        foreach (var combo in ControllerCombo.All)
        {
            var c = combo;
            Toggle($"{c.ButtonsText}   ·   {c.Title}",
                $"Segure {c.ButtonsText} por {c.HoldText}, em qualquer janela do Windows: {c.Title.ToLowerInvariant()}. " +
                "O app lê o controle direto, então o atalho funciona até com um jogo em primeiro plano — e é por isso " +
                "que ele pode atrapalhar em jogos que usem esses botões juntos. Nesse caso, desligue o atalho aqui.",
                () => c.IsEnabledIn(_settings.Current),
                v => SetSetting(s => c.SetEnabledIn(s, v)),
                ToPadPart(c.Buttons));
        }
        Info("Enquanto esta tela está aberta", "Atalhos em pausa", PadPart.None,
            "Os atalhos valem no Windows, não aqui: nesta tela o controle navega a própria tela (A seleciona, B volta). " +
            "Feche com B para usá-los.");
    }

    /// <summary>Item só de leitura: entra no mapa de atalhos e acende os botões dele no desenho.</summary>
    private void Info(string title, string value, PadPart highlight, string description)
    {
        _items.Add(new SettingItem
        {
            Title = title,
            Description = description,
            Value = () => value,
            Highlight = () => highlight,
        });
    }

    private static PadPart ToPadPart(GamepadButtons buttons)
    {
        var part = PadPart.None;
        if ((buttons & GamepadButtons.A) != 0) part |= PadPart.A;
        if ((buttons & GamepadButtons.B) != 0) part |= PadPart.B;
        if ((buttons & GamepadButtons.X) != 0) part |= PadPart.X;
        if ((buttons & GamepadButtons.Y) != 0) part |= PadPart.Y;
        if ((buttons & GamepadButtons.Back) != 0) part |= PadPart.Back;
        if ((buttons & GamepadButtons.Start) != 0) part |= PadPart.Start;
        if ((buttons & (GamepadButtons.DpadUp | GamepadButtons.DpadDown | GamepadButtons.DpadLeft | GamepadButtons.DpadRight)) != 0)
        {
            part |= PadPart.Dpad;
        }

        return part;
    }
}
