using MyGameConsole.Models;

namespace MyGameConsole.Services;

/// <summary>Estado real de cada ajuste do Modo Game no Windows. Nulo = não foi possível ler.</summary>
public sealed record GameModeStatus(
    bool? TaskbarHidden,
    bool? DesktopIconsHidden,
    bool? WallpaperApplied,
    bool? WakePasswordSkipped,
    bool? SetupPromptsHidden);

/// <summary>
/// "Modo Game": conjunto de ajustes de área de trabalho que ficam aplicados de forma persistente
/// (barra auto-ocultar, ícones escondidos, papel de parede do console, sem senha ao acordar, sem a tela de
/// "concluir a configuração" do Windows). O estado original é
/// guardado ao ativar e restaurado ao desativar; ao iniciar o app, os ajustes são reaplicados.
/// </summary>
public sealed class GameModeService
{
    private readonly SettingsService _settings;
    private readonly DesktopTweaksService _tweaks;
    private readonly PowerService _power;

    public GameModeService(SettingsService settings, DesktopTweaksService tweaks, PowerService power)
    {
        _settings = settings;
        _tweaks = tweaks;
        _power = power;
    }

    public bool IsEnabled => _settings.Current.GameModeEnabled;

    public event EventHandler? StateChanged;

    public void Enable()
    {
        var s = _settings.Current;
        s.GameModeBackup ??= TakeSnapshot();

        // Persistir antes de aplicar: se algo falhar no meio, o backup já está salvo.
        _settings.Update(x =>
        {
            x.GameModeEnabled = true;
            x.StartWithWindows = true; // sem o app no boot os ajustes não seriam reaplicados
        });

        try
        {
            Apply(s, interactive: true);
        }
        finally
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Disable()
    {
        var s = _settings.Current;
        var backup = s.GameModeBackup;

        try
        {
            if (backup is not null)
            {
                Restore(backup);
            }
        }
        finally
        {
            _settings.Update(x =>
            {
                x.GameModeEnabled = false;
                x.GameModeBackup = null;
            });
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Toggle()
    {
        if (IsEnabled) Disable();
        else Enable();
    }

    /// <summary>
    /// Reaplica os ajustes: no início do app (para valerem após reiniciar) e quando as opções mudam.
    /// <paramref name="interactive"/> falso é o caso do início do app: nada que peça UAC (senha ao acordar)
    /// é tentado, para não incomodar a cada boot; o checklist mostra o que ficou pendente.
    /// </summary>
    public void ReapplyIfEnabled(bool interactive = true)
    {
        if (!IsEnabled) return;
        Apply(_settings.Current, interactive);
    }

    // ------------------------------------------------------------------
    // Checklist: o que está de fato aplicado no Windows agora
    // ------------------------------------------------------------------

    /// <summary>Estado real de cada ajuste no Windows, para a tela de checklist do Modo Game.</summary>
    public GameModeStatus Inspect() => new(
        TaskbarHidden: SafeRead(() => _tweaks.IsTaskbarAutoHide),
        DesktopIconsHidden: SafeRead(() => _tweaks.AreDesktopIconsHidden),
        WallpaperApplied: SafeRead(IsWallpaperApplied),
        WakePasswordSkipped: SafeRead(() => !_power.IsWakePasswordRequiredAnywhere()),
        SetupPromptsHidden: SafeRead(() => _tweaks.AreSetupPromptsDisabled));

    private static bool? SafeRead(Func<bool> read)
    {
        try { return read(); }
        catch { return null; }
    }

    /// <summary>O papel de parede atual é o do Modo Game (o escolhido pelo usuário, o que vem com o app ou a reserva gerada).</summary>
    private bool IsWallpaperApplied()
    {
        var current = _tweaks.GetWallpaper().Path;
        if (string.IsNullOrEmpty(current) || _tweaks.BackgroundType != DesktopTweaksService.BackgroundPicture) return false;

        var custom = _settings.Current.GameModeWallpaperPath;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return SamePath(current, custom);

        return SamePath(current, _tweaks.DefaultWallpaperPath)
            || (Path.GetFileName(current).StartsWith("wallpaper_v", StringComparison.OrdinalIgnoreCase)
                && SamePath(Path.GetDirectoryName(current), _settings.Directory));
    }

    private static bool SamePath(string? a, string? b)
    {
        if (a is null || b is null) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    // ------------------------------------------------------------------

    private DesktopStateSnapshot TakeSnapshot()
    {
        var (path, style, tile) = _tweaks.GetWallpaper();
        return new DesktopStateSnapshot
        {
            WallpaperPath = path,
            WallpaperStyle = style,
            TileWallpaper = tile,
            BackgroundType = _tweaks.BackgroundType,
            TaskbarAutoHide = _tweaks.IsTaskbarAutoHide,
            DesktopIconsHidden = _tweaks.AreDesktopIconsHidden,
            WakePasswordByScheme = TryReadWakePassword(),
            SetupPrompts = _tweaks.GetSetupPrompts(),
        };
    }

    /// <summary>Nulo se o Windows recusar a leitura; a aplicação tenta de novo antes de mexer no ajuste.</summary>
    private Dictionary<string, WakePasswordState>? TryReadWakePassword()
    {
        try { return _power.GetWakePasswordByScheme(); }
        catch { return null; }
    }

    /// <summary>
    /// Aplica cada ajuste ligado; para os desligados, devolve o valor original (se houver backup).
    /// Falhas individuais não impedem os demais ajustes.
    /// </summary>
    private void Apply(AppSettings s, bool interactive)
    {
        var backup = s.GameModeBackup;
        var errors = new List<string>();

        Try(errors, "barra de tarefas", () =>
        {
            if (s.GameModeHideTaskbar) _tweaks.SetTaskbarAutoHide(true);
            else if (backup is not null) _tweaks.SetTaskbarAutoHide(backup.TaskbarAutoHide);
        });

        Try(errors, "ícones da área de trabalho", () =>
        {
            if (s.GameModeHideDesktopIcons) _tweaks.SetDesktopIconsHidden(true);
            else if (backup is not null) _tweaks.SetDesktopIconsHidden(backup.DesktopIconsHidden);
        });

        Try(errors, "papel de parede", () =>
        {
            if (s.GameModeApplyWallpaper)
            {
                ApplyWallpaper(s);
            }
            else if (backup is not null)
            {
                RestoreWallpaper(backup);
            }
        });

        Try(errors, "senha ao acordar", () =>
        {
            // Backups de versões antigas (ou cuja leitura falhou) não têm o valor original: guarda agora, antes de mexer.
            if (backup is not null && backup.WakePasswordByScheme is null)
            {
                var original = _power.GetWakePasswordByScheme();
                _settings.Update(_ => backup.WakePasswordByScheme = original);
            }

            // Gravar exige administrador (UAC). Se não for permitido agora, fica pendente no checklist.
            if (s.GameModeSkipPasswordOnWake) _power.SetWakePasswordRequired(false, allowElevation: interactive);
            else if (backup?.WakePasswordByScheme is { } states) _power.RestoreWakePassword(states, allowElevation: interactive);
        });

        Try(errors, "tela de concluir a configuração", () =>
        {
            // Backups de versões antigas não têm o valor original: guarda agora, antes de mexer.
            if (backup is not null && backup.SetupPrompts is null)
            {
                var original = _tweaks.GetSetupPrompts();
                _settings.Update(_ => backup.SetupPrompts = original);
            }

            if (s.GameModeHideSetupPrompts) _tweaks.SetSetupPromptsDisabled();
            else if (backup?.SetupPrompts is { } values) _tweaks.RestoreSetupPrompts(values);
        });

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Modo Game aplicado com problemas: " + string.Join("; ", errors));
        }
    }

    private void Restore(DesktopStateSnapshot backup)
    {
        var errors = new List<string>();

        Try(errors, "barra de tarefas", () => _tweaks.SetTaskbarAutoHide(backup.TaskbarAutoHide));
        Try(errors, "ícones da área de trabalho", () => _tweaks.SetDesktopIconsHidden(backup.DesktopIconsHidden));
        Try(errors, "papel de parede", () => RestoreWallpaper(backup));
        Try(errors, "senha ao acordar", () =>
        {
            if (backup.WakePasswordByScheme is { } states) _power.RestoreWakePassword(states, allowElevation: true);
        });
        Try(errors, "tela de concluir a configuração", () =>
        {
            if (backup.SetupPrompts is { } values) _tweaks.RestoreSetupPrompts(values);
        });

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Modo Game desativado com problemas: " + string.Join("; ", errors));
        }
    }

    /// <summary>Devolve o papel de parede original, inclusive o tipo (imagem, cor sólida, apresentação).</summary>
    private void RestoreWallpaper(DesktopStateSnapshot backup) =>
        _tweaks.SetWallpaper(backup.WallpaperPath, backup.WallpaperStyle, backup.TileWallpaper, backup.BackgroundType);

    /// <summary>
    /// Aplica a imagem escolhida pelo usuário ou, sem ela, o papel de parede padrão que vem com o app.
    /// Se o Windows recusar o padrão (WebP sem decodificador), cai para a arte gerada pelo app.
    /// </summary>
    private void ApplyWallpaper(AppSettings s)
    {
        var custom = s.GameModeWallpaperPath;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
        {
            EnsureWallpaper(Path.GetFullPath(custom));
            return;
        }

        var bundled = _tweaks.DefaultWallpaperPath;
        if (bundled is not null)
        {
            try
            {
                EnsureWallpaper(bundled);
                return;
            }
            catch (InvalidOperationException)
            {
                // sem suporte a WebP neste Windows: usa a reserva abaixo
            }
        }

        EnsureWallpaper(_tweaks.EnsureFallbackWallpaper(_settings.Directory));
    }

    private void EnsureWallpaper(string wanted)
    {
        var current = _tweaks.GetWallpaper().Path;
        // Reaplica se a imagem mudou ou se a Personalização ainda não está em "Imagem";
        // caso contrário o Explorer descarta a imagem no próximo logon.
        if (!string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase)
            || _tweaks.BackgroundType != DesktopTweaksService.BackgroundPicture)
        {
            _tweaks.SetWallpaper(wanted);
        }
    }

    private static void Try(List<string> errors, string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            errors.Add($"{what} ({ex.Message})");
        }
    }
}
