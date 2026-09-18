using MyGameConsole.App;
using MyGameConsole.Services;

namespace MyGameConsole.Forms;

/// <summary>
/// Página "Modo Game" da tela do console: checklist do que o app aplica sozinho (com o estado real de
/// cada ajuste no Windows) e do que o usuário precisa fazer à mão, porque o app não consegue
/// (ex.: login automático, que exige digitar a senha). Usa a mesma lista/painel da página de configurações.
/// </summary>
public sealed partial class ConsoleForm
{
    private readonly LogonService _logon = new();

    private void OpenGameModePage() => OpenPage("MODO GAME", BuildGameModeItems);

    private void BuildGameModeItems()
    {
        _items.Clear();
        var s = _settings.Current;
        bool enabled = _gameMode.IsEnabled;
        var real = _gameMode.Inspect();

        Header("Estado");
        Toggle("Modo Game",
            "Aplica os ajustes da lista \"Feito pelo app\" e mantém após reiniciar o PC. Desligar restaura tudo como estava. " +
            "Cada ajuste pode ser ligado ou desligado individualmente na lista.",
            () => _gameMode.IsEnabled, v => { if (v != _gameMode.IsEnabled) ToggleGameMode(); });

        Header("Feito pelo app");
        AutoItem("Barra de tarefas em auto-ocultar",
            "A barra some e reaparece ao levar o cursor para a parte de baixo da tela.",
            s.GameModeHideTaskbar, enabled, real.TaskbarHidden, v => SetSetting(x => x.GameModeHideTaskbar = v));
        AutoItem("Esconder os ícones da área de trabalho",
            "Deixa a área de trabalho limpa, só com o papel de parede.",
            s.GameModeHideDesktopIcons, enabled, real.DesktopIconsHidden, v => SetSetting(x => x.GameModeHideDesktopIcons = v));
        AutoItem("Papel de parede do console",
            "Usa o papel de parede padrão do app ou a imagem escolhida em Configurações.",
            s.GameModeApplyWallpaper, enabled, real.WallpaperApplied, v => SetSetting(x => x.GameModeApplyWallpaper = v));
        AutoItem("Entrar sem senha ao acordar",
            "Ao voltar da suspensão ou hibernação, o Windows entra direto, sem pedir senha, em todos os planos de energia " +
            "(o mesmo que \"Nunca\" em Contas > Opções de entrada). Bloquear com Win+L continua pedindo senha. " +
            "Gravar esse ajuste exige administrador: o Windows pede confirmação do UAC uma vez.",
            s.GameModeSkipPasswordOnWake, enabled, real.WakePasswordSkipped, v => SetSetting(x => x.GameModeSkipPasswordOnWake = v),
            pendingHint: "O Windows ainda pede senha ao acordar. Desligue e ligue este ajuste (pede o UAC) ou faça à mão: " +
                         "Configurações > Contas > Opções de entrada > \"Se você esteve ausente...\" > Nunca.");
        AutoItem("Sem a tela de concluir a configuração",
            "O Windows deixa de abrir, ao entrar ou após atualizações, a tela cheia \"Vamos concluir a configuração do seu " +
            "dispositivo\" e a de boas-vindas/novidades (o mesmo que desmarcar essas opções em Sistema > Notificações > " +
            "Configurações adicionais).",
            s.GameModeHideSetupPrompts, enabled, real.SetupPromptsHidden, v => SetSetting(x => x.GameModeHideSetupPrompts = v));
        AutoItem("Iniciar com o Windows",
            "Ligado junto com o Modo Game: sem o app no boot, os ajustes não seriam reaplicados e a tela do console não abriria pelo controle.",
            s.StartWithWindows, enabled, s.StartWithWindows, v => SetSetting(x => x.StartWithWindows = v));

        Header("Para você fazer à mão");
        ManualItem("Entrar no Windows sem senha ao ligar o PC",
            "O app não faz isso sozinho porque exige digitar a sua senha do Windows.\n\n" +
            "1. Configurações > Contas > Opções de entrada: desligue \"Para maior segurança, permita apenas a entrada do Windows Hello\" (se estiver ligado).\n" +
            "2. Win+R, digite netplwiz e Enter. Desmarque \"Os usuários devem digitar um nome de usuário e senha para usar este computador\", OK e digite a senha.\n" +
            "3. Reinicie para conferir.\n\n" +
            "A abre a janela certa (precisa de teclado para a senha).",
            done: _logon.IsAutoLogonEnabled,
            doneText: "O Windows já entra direto na sua conta ao ligar.",
            pendingExtra: () => _logon.IsWindowsHelloOnly
                ? "Detectado: \"apenas Windows Hello\" está ligado. Desligue-o primeiro (passo 1), senão a opção do passo 2 não aparece."
                : "O Windows ainda pede senha ao ligar o PC.",
            onSelect: () =>
            {
                if (_logon.IsWindowsHelloOnly) _logon.OpenSignInOptions();
                else _logon.OpenUserAccounts();
                Hide();
            });
        ManualItem("Steam instalado",
            "O Big Picture é a interface de console do Steam. Instale o Steam em store.steampowered.com e entre na sua conta; " +
            "depois o app o detecta sozinho (ou informe a pasta em Configurações > Steam).\n\nA abre a página de download.",
            done: _steam.IsInstalled,
            doneText: _steam.SteamExePath is { } exe ? $"Encontrado em {exe}" : "Steam encontrado.",
            pendingExtra: () => "Steam não encontrado neste PC.",
            onSelect: () =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://store.steampowered.com/about/") { UseShellExecute = true });
                Hide();
            });
    }

    /// <summary>
    /// Ajuste que o app aplica sozinho. O selo mostra o estado real: APLICADO (o Windows está como o Modo Game quer),
    /// PENDENTE (Modo Game ligado, mas o Windows não está assim), AO ATIVAR (Modo Game desligado) ou DESLIGADO
    /// (o usuário desligou este ajuste). A ou ◀ ▶ liga/desliga o ajuste.
    /// </summary>
    private void AutoItem(string title, string description, bool wanted, bool gameModeOn, bool? applied, Action<bool> set,
        string pendingHint = "O Windows não está com este ajuste. Desligue e ligue o Modo Game para reaplicar.")
    {
        (string, Color) Status() =>
            !wanted ? ("DESLIGADO", Theme.PillOff)
            : !gameModeOn ? ("AO ATIVAR", Theme.PillOff)
            : applied is null ? ("SEM LEITURA", Theme.Warning)
            : applied.Value ? ("APLICADO", Theme.Success)
            : ("PENDENTE", Theme.Warning);

        _items.Add(new SettingItem
        {
            Title = title,
            Description = description + "\n\nA ou ◀ ▶ liga/desliga este ajuste.",
            Status = Status,
            Extra = () => Status().Item1 switch
            {
                "PENDENTE" => pendingHint,
                "SEM LEITURA" => "Não foi possível ler o estado atual deste ajuste no Windows.",
                "AO ATIVAR" => "Será aplicado quando o Modo Game for ligado.",
                _ => string.Empty,
            },
            OnSelect = () => set(!wanted),
            OnAdjust = dx => { bool v = dx > 0; if (wanted != v) set(v); },
        });
    }

    /// <summary>Passo que só o usuário pode fazer. O selo mostra FEITO ou PENDENTE; A abre a tela certa do Windows.</summary>
    private void ManualItem(string title, string description, bool done, string doneText, Func<string> pendingExtra, Action onSelect)
    {
        _items.Add(new SettingItem
        {
            Title = title,
            Description = description,
            Status = () => done ? ("FEITO", Theme.Success) : ("PENDENTE", Theme.Warning),
            Extra = () => done ? doneText : pendingExtra(),
            OnSelect = onSelect,
        });
    }
}
