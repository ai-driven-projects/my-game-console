using MyGameConsole.Models;

namespace MyGameConsole.Services;

/// <summary>
/// "Modo Game": conjunto de ajustes de área de trabalho que ficam aplicados de forma persistente
/// (barra auto-ocultar, ícones escondidos, papel de parede do console). O estado original é
/// guardado ao ativar e restaurado ao desativar; ao iniciar o app, os ajustes são reaplicados.
/// </summary>
public sealed class GameModeService
{
    private readonly SettingsService _settings;
    private readonly DesktopTweaksService _tweaks;

    public GameModeService(SettingsService settings, DesktopTweaksService tweaks)
    {
        _settings = settings;
        _tweaks = tweaks;
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
            Apply(s);
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

    /// <summary>Chamado no início do app: garante que os ajustes continuem valendo após reiniciar.</summary>
    public void ReapplyIfEnabled()
    {
        if (!IsEnabled) return;
        Apply(_settings.Current);
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
        };
    }

    /// <summary>
    /// Aplica cada ajuste ligado; para os desligados, devolve o valor original (se houver backup).
    /// Falhas individuais não impedem os demais ajustes.
    /// </summary>
    private void Apply(AppSettings s)
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
