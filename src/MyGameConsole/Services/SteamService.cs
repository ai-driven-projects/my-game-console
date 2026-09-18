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
    /// <summary>Classe da janela da interface do Steam (fica no steamwebhelper.exe, não no steam.exe).</summary>
    private const string SteamWindowClass = "SDL_app";
    private static readonly TimeSpan BigPictureFocusTimeout = TimeSpan.FromSeconds(12);
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

    public bool IsBigPictureActive => FindBigPictureWindow() != IntPtr.Zero;

    /// <summary>
    /// Janela do Big Picture, ou zero. O título depende do idioma do Steam ("Steam Big Picture Mode",
    /// "Steam — Modo Big Picture"...), por isso procura pela classe da janela e por "Big Picture" no título.
    /// </summary>
    private static IntPtr FindBigPictureWindow()
    {
        foreach (var w in ForegroundWindow.ListVisible())
        {
            if (w.ClassName == SteamWindowClass
                && (w.Title.Contains("Big Picture", StringComparison.OrdinalIgnoreCase)
                    || w.Title.Contains("Big-Picture", StringComparison.OrdinalIgnoreCase)))
            {
                return w.Handle;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Abre o Big Picture sem esperar: se já estiver aberto, só traz para a frente; senão pede ao Steam
    /// e tenta dar o foco em segundo plano quando a janela aparecer (melhor esforço). Para a tela do
    /// console, prefira <see cref="OpenBigPictureAsync"/>, que garante o foco.
    /// </summary>
    public void OpenBigPicture()
    {
        if (!LaunchBigPicture()) return;
        _ = Task.Run(() => WaitAndFocusBigPictureAsync());
    }

    /// <summary>
    /// Abre o Big Picture e espera a janela dele aparecer para trazê-la para a frente. Chamado da interface
    /// (com a tela do console ainda visível), a continuação roda na thread da interface enquanto este app é
    /// o processo em primeiro plano, e nesse caso o Windows sempre aceita o pedido de foco. Devolve se a
    /// janela ficou em primeiro plano dentro do tempo limite.
    /// </summary>
    public async Task<bool> OpenBigPictureAsync()
    {
        if (!LaunchBigPicture()) return true;
        return await WaitAndFocusBigPictureAsync();
    }

    /// <summary>
    /// Se o Big Picture já estiver aberto, traz para a frente e devolve falso (nada a esperar). Senão, se o
    /// Steam estiver aberto (mesmo só na bandeja), pede a ele que mude para o Big Picture; se não estiver,
    /// inicia o Steam já em Big Picture. Devolve verdadeiro quando a janela ainda vai aparecer.
    /// </summary>
    private bool LaunchBigPicture()
    {
        var exe = SteamExePath
            ?? throw new InvalidOperationException("Steam não encontrado. Configure o caminho nas opções.");

        var existing = FindBigPictureWindow();
        if (existing != IntPtr.Zero)
        {
            ForegroundWindow.TryBringToFront(existing);
            return false;
        }

        // Só vale enquanto este app está em primeiro plano: deixa o Steam tomar o foco por conta própria.
        ForegroundWindow.AllowNextProcessToTakeFocus();

        if (IsRunning)
        {
            // "steam.exe -bigpicture" numa instância já aberta só mostra a janela normal do Steam;
            // a URL é o que de fato muda a interface para o Big Picture.
            OpenSteamUrl("steam://open/bigpicture");
        }
        else
        {
            // Sempre sem elevação, mesmo que o app esteja como administrador.
            ProcessLauncher.Start(exe, "-bigpicture", Path.GetDirectoryName(exe));
        }

        return true;
    }

    /// <summary>Espera a janela do Big Picture aparecer (o Steam pode demorar) e a traz para a frente.</summary>
    private static async Task<bool> WaitAndFocusBigPictureAsync()
    {
        var deadline = DateTime.UtcNow + BigPictureFocusTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            var hwnd = FindBigPictureWindow();
            if (hwnd == IntPtr.Zero) continue;

            if (ForegroundWindow.TryBringToFront(hwnd)) return true;
            // Recém-criada, a janela pode ainda não aceitar o foco; tenta de novo no próximo ciclo.
        }

        return false;
    }

    /// <summary>Janela do Big Picture em primeiro plano agora (o Steam também lê o controle e pode tomar a frente).</summary>
    public bool IsBigPictureInForeground
    {
        get
        {
            var foreground = NativeMethods.GetForegroundWindow();
            return foreground != IntPtr.Zero && foreground == FindBigPictureWindow();
        }
    }

    /// <summary>
    /// Minimiza o Big Picture sem ativar outra janela. Com a tela do console aberta, ele fica fora do caminho: um
    /// botão do controle não o traz para a frente por acaso. O cartão "Steam Big Picture" o restaura.
    /// </summary>
    public void MinimizeBigPicture()
    {
        var hwnd = FindBigPictureWindow();
        if (hwnd != IntPtr.Zero && !NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWMINNOACTIVE);
        }
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
