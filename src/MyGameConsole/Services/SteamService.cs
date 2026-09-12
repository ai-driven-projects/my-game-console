using MyGameConsole.App;
using System.Diagnostics;
using Microsoft.Win32;
using MyGameConsole.Native;

namespace MyGameConsole.Services;

/// <summary>
/// Integração com o Steam: detecção da instalação, estado e Big Picture Mode.
/// </summary>
public sealed class SteamService
{
    private const string BigPictureWindowTitle = "Steam Big Picture Mode";
    private readonly SettingsService _settings;

    public SteamService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>Caminho da pasta do Steam ou null se não encontrado.</summary>
    public string? FindSteamPath()
    {
        var overridePath = _settings.Current.SteamPathOverride;
        if (!string.IsNullOrWhiteSpace(overridePath) && Directory.Exists(overridePath))
        {
            return overridePath;
        }

        try
        {
            using var hkcu = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (hkcu?.GetValue("SteamPath") is string p1 && Directory.Exists(p1))
            {
                return Path.GetFullPath(p1);
            }

            using var hklm = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (hklm?.GetValue("InstallPath") is string p2 && Directory.Exists(p2))
            {
                return Path.GetFullPath(p2);
            }
        }
        catch
        {
            // ignorar problemas de acesso ao registro
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    public string? SteamExePath
    {
        get
        {
            var dir = FindSteamPath();
            if (dir is null) return null;
            var exe = Path.Combine(dir, "steam.exe");
            return File.Exists(exe) ? exe : null;
        }
    }

    public bool IsInstalled => SteamExePath is not null;

    public bool IsRunning => Process.GetProcessesByName("steam").Length > 0;

    public bool IsBigPictureActive =>
        NativeMethods.FindWindow(null, BigPictureWindowTitle) != IntPtr.Zero;

    public void OpenBigPicture()
    {
        var exe = SteamExePath
            ?? throw new InvalidOperationException("Steam não encontrado. Configure o caminho nas opções.");

        if (IsBigPictureActive)
        {
            var hwnd = NativeMethods.FindWindow(null, BigPictureWindowTitle);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(hwnd);
            return;
        }

        // Se o Steam já estiver aberto, o executável repassa os argumentos para a instância em execução.
        // Sempre sem elevação, mesmo que o app esteja como administrador.
        ProcessLauncher.Start(exe, "-bigpicture", Path.GetDirectoryName(exe));
    }

    public void CloseBigPicture()
    {
        if (!IsBigPictureActive) return;
        OpenSteamUrl("steam://close/bigpicture");
    }

    public void OpenSteamUrl(string url)
    {
        ProcessLauncher.Start(url);
    }
}
