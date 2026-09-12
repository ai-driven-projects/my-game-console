namespace MyGameConsole.Services;

/// <summary>
/// Orquestra o "Modo Console": esconde o shell do Windows e abre o Big Picture,
/// deixando o PC com cara de console. Ao sair, restaura tudo.
/// </summary>
public sealed class ConsoleModeService
{
    private readonly SettingsService _settings;
    private readonly ShellService _shell;
    private readonly SteamService _steam;

    public bool IsActive { get; private set; }

    public event EventHandler? StateChanged;

    public ConsoleModeService(SettingsService settings, ShellService shell, SteamService steam)
    {
        _settings = settings;
        _shell = shell;
        _steam = steam;
    }

    public void Enter()
    {
        if (IsActive) return;

        var s = _settings.Current;

        if (s.OpenBigPictureInConsoleMode && _steam.IsInstalled)
        {
            _steam.OpenBigPicture();
        }

        if (s.HideExplorerInConsoleMode)
        {
            _shell.StopShell();
        }

        IsActive = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Exit()
    {
        if (!IsActive) return;

        _shell.StartShell();

        IsActive = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle()
    {
        if (IsActive) Exit();
        else Enter();
    }

    /// <summary>Garante que o shell volte ao encerrar o app, mesmo em modo console.</summary>
    public void EnsureShellRestored()
    {
        if (!_shell.IsShellRunning)
        {
            _shell.StartShell();
        }
    }
}
