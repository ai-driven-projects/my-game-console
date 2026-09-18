using MyGameConsole.Models;

namespace MyGameConsole.Services;

/// <summary>Estado real de cada ajuste do Modo Game no Windows. Nulo = não foi possível ler.</summary>
public sealed record GameModeStatus(
    bool? TaskbarHidden,
    bool? DesktopIconsHidden,
    bool? WallpaperApplied,
    bool? WakePasswordSkipped,
    bool? SetupPromptsHidden,
    bool? BootsToConsole,
    bool? LockScreenApplied);

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
    private readonly StartupService _startup;
    private readonly LockScreenService _lockScreen;

    public GameModeService(SettingsService settings, DesktopTweaksService tweaks, PowerService power,
        StartupService startup, LockScreenService lockScreen)
    {
        _settings = settings;
        _tweaks = tweaks;
        _power = power;
        _startup = startup;
        _lockScreen = lockScreen;
    }

    /// <summary>O app abre direto na tela do console ao iniciar (Modo Game com "Entrar direto no console").</summary>
    public bool BootsToConsole => IsEnabled && _settings.Current.GameModeBootToConsole;

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
            Restore(backup);
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
        SetupPromptsHidden: SafeRead(() => _tweaks.AreSetupPromptsDisabled),
        BootsToConsole: SafeRead(() => _tweaks.IsStartupDelayDisabled && !_startup.IsTaskDelayed),
        LockScreenApplied: SafeRead(() => _lockScreen.IsApplied));

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
            StartupDelay = _tweaks.GetStartupDelay(),
            LockScreen = TryReadLockScreen(),
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

        // Desligados, barra e ícones voltam ao normal da área de trabalho (ver Restore).
        Try(errors, "barra de tarefas", () =>
        {
            if (s.GameModeHideTaskbar) _tweaks.SetTaskbarAutoHide(true);
            else if (backup is not null) _tweaks.SetTaskbarAutoHide(false);
        });

        Try(errors, "ícones da área de trabalho", () =>
        {
            if (s.GameModeHideDesktopIcons) _tweaks.SetDesktopIconsHidden(true);
            else if (backup is not null) _tweaks.SetDesktopIconsHidden(false);
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

        Try(errors, "entrar direto no console", () =>
        {
            // Backups de versões antigas não têm o valor original: guarda agora, antes de mexer.
            if (backup is not null && backup.StartupDelay is null)
            {
                var original = _tweaks.GetStartupDelay();
                _settings.Update(_ => backup.StartupDelay = original);
            }

            if (s.GameModeBootToConsole)
            {
                _tweaks.SetStartupDelayDisabled();
                // A tarefa elevada de versões antigas espera 5 s no logon; recriá-la pede o UAC (só em ação do usuário).
                if (s.StartElevated) _startup.RemoveTaskDelay(allowElevation: interactive);
            }
            else if (backup?.StartupDelay is { } values)
            {
                _tweaks.RestoreStartupDelay(values);
            }
        });

        Try(errors, "tela de bloqueio", () =>
        {
            // Backups de versões antigas (ou cuja leitura falhou) não têm o valor original: guarda agora, antes de mexer.
            if (backup is not null && backup.LockScreen is null)
            {
                var original = _lockScreen.Get();
                _settings.Update(_ => backup.LockScreen = original);
            }

            // Gravar exige administrador (UAC). Se não for permitido agora, fica pendente no checklist.
            if (s.GameModeLockScreen) _lockScreen.Apply(LockScreenImage(s), allowElevation: interactive);
            else if (backup?.LockScreen is { } values) _lockScreen.Restore(values, allowElevation: interactive);
        });

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Modo Game aplicado com problemas: " + string.Join("; ", errors));
        }
    }

    /// <summary>Nulo se o Windows recusar a leitura; a aplicação tenta de novo antes de mexer no ajuste.</summary>
    private Dictionary<string, string?>? TryReadLockScreen()
    {
        try { return _lockScreen.Get(); }
        catch { return null; }
    }

    /// <summary>
    /// Imagem para a tela de bloqueio: a mesma do papel de parede do Modo Game. A escolhida pelo usuário vai direto;
    /// o papel de parede padrão (WebP, que o GDI+ não lê) vai pela cópia em JPEG que o próprio Windows guarda do
    /// papel de parede atual; sem ela, a arte gerada pelo app.
    /// </summary>
    private string LockScreenImage(AppSettings s)
    {
        var custom = s.GameModeWallpaperPath;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)
            && Path.GetExtension(custom).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp")
        {
            return custom;
        }

        var transcoded = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        if (SafeRead(IsWallpaperApplied) == true && File.Exists(transcoded)) return transcoded;

        return _tweaks.EnsureFallbackWallpaper(_settings.Directory);
    }

    /// <summary>
    /// Desliga o Modo Game. Barra de tarefas e ícones voltam sempre ao normal da área de trabalho (barra sempre
    /// visível, atalhos na tela), mesmo sem backup: o instantâneo tirado ao ativar pode ter pego os dois já
    /// escondidos (por uma desativação anterior que falhou), e restaurá-lo deixava tudo escondido para sempre.
    /// O resto volta ao valor original guardado.
    /// </summary>
    private void Restore(DesktopStateSnapshot? backup)
    {
        var errors = new List<string>();

        Try(errors, "barra de tarefas", () => _tweaks.SetTaskbarAutoHide(false));
        Try(errors, "ícones da área de trabalho", () => _tweaks.SetDesktopIconsHidden(false));

        if (backup is not null) RestoreFromBackup(backup, errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Modo Game desativado com problemas: " + string.Join("; ", errors));
        }
    }

    private void RestoreFromBackup(DesktopStateSnapshot backup, List<string> errors)
    {
        Try(errors, "papel de parede", () => RestoreWallpaper(backup));
        Try(errors, "senha ao acordar", () =>
        {
            if (backup.WakePasswordByScheme is { } states) _power.RestoreWakePassword(states, allowElevation: true);
        });
        Try(errors, "tela de concluir a configuração", () =>
        {
            if (backup.SetupPrompts is { } values) _tweaks.RestoreSetupPrompts(values);
        });
        Try(errors, "entrar direto no console", () =>
        {
            if (backup.StartupDelay is { } values) _tweaks.RestoreStartupDelay(values);
        });
        Try(errors, "tela de bloqueio", () =>
        {
            if (backup.LockScreen is { } values) _lockScreen.Restore(values, allowElevation: true);
        });
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
