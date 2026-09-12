using System.Diagnostics;
using MyGameConsole.Native;

namespace MyGameConsole.Services;

/// <summary>
/// Controle do shell do Windows (explorer.exe): esconder barra de tarefas/área de trabalho
/// para uma experiência "console" e restaurar depois.
/// </summary>
public sealed class ShellService
{
    private static string ExplorerPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    /// <summary>Verdadeiro se a barra de tarefas (shell) está ativa.</summary>
    public bool IsShellRunning =>
        NativeMethods.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;

    public void StopShell()
    {
        if (!IsShellRunning) return;

        // taskkill encerra o processo do shell; janelas do Explorador de Arquivos também são fechadas.
        using var p = Process.Start(new ProcessStartInfo("taskkill", "/f /im explorer.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        p?.WaitForExit(5000);
    }

    public void StartShell()
    {
        if (IsShellRunning) return;

        Process.Start(new ProcessStartInfo(ExplorerPath)
        {
            UseShellExecute = true,
        });
    }

    public void RestartShell()
    {
        StopShell();
        Thread.Sleep(500);
        StartShell();
    }
}
