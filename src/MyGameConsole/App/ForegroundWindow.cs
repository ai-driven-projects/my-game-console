using System.Runtime.InteropServices;
using MyGameConsole.Native;

namespace MyGameConsole.App;

/// <summary>
/// Localiza janelas de nível superior de outros processos e as traz para a frente.
/// O Windows só deixa um processo mudar a janela em primeiro plano se ele mesmo estiver em primeiro plano
/// (ou tiver recebido a última entrada); fora disso, SetForegroundWindow só pisca o botão na barra.
/// Por isso <see cref="TryBringToFront"/> usa o truque clássico de anexar a fila de entrada da thread
/// em primeiro plano antes de pedir o foco.
/// </summary>
public static class ForegroundWindow
{
    public readonly record struct TopLevelWindow(IntPtr Handle, string ClassName, string Title, uint ProcessId);

    [ThreadStatic] private static List<TopLevelWindow>? _collected;

    /// <summary>Janelas de nível superior visíveis, na ordem Z (a da frente primeiro).</summary>
    public static unsafe List<TopLevelWindow> ListVisible()
    {
        var list = new List<TopLevelWindow>();
        _collected = list;
        try
        {
            NativeMethods.EnumWindows(&Collect, IntPtr.Zero);
        }
        finally
        {
            _collected = null;
        }

        return list;
    }

    [UnmanagedCallersOnly]
    private static unsafe int Collect(IntPtr hwnd, IntPtr lParam)
    {
        try
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return 1;

            const int max = 256;
            char* buffer = stackalloc char[max];
            int n = NativeMethods.GetWindowText(hwnd, buffer, max);
            var title = n > 0 ? new string(buffer, 0, n) : string.Empty;
            n = NativeMethods.GetClassName(hwnd, buffer, max);
            var cls = n > 0 ? new string(buffer, 0, n) : string.Empty;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);

            _collected?.Add(new TopLevelWindow(hwnd, cls, title, pid));
        }
        catch
        {
            // nunca deixar uma exceção atravessar o callback nativo
        }

        return 1; // continuar
    }

    /// <summary>
    /// Restaura (se minimizada) e traz a janela para a frente. Devolve se ela ficou em primeiro plano.
    /// Pode ser chamado de qualquer thread.
    /// </summary>
    public static bool TryBringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        if (NativeMethods.GetForegroundWindow() == hwnd) return true;

        // Tentativa direta: funciona quando este processo está em primeiro plano (ex.: a tela do console aberta).
        NativeMethods.SetForegroundWindow(hwnd);
        if (NativeMethods.GetForegroundWindow() == hwnd) return true;

        // Fora isso, anexa a fila de entrada da thread que está em primeiro plano, pede o foco e desanexa.
        var foreground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint thisThread = NativeMethods.GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != thisThread
            && NativeMethods.AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(thisThread, foregroundThread, false);
        }

        return NativeMethods.GetForegroundWindow() == hwnd;
    }

    /// <summary>
    /// Autoriza o próximo processo que mostrar uma janela a tomar o primeiro plano (só vale enquanto este
    /// processo estiver em primeiro plano). Útil antes de pedir a outro app, já aberto, que mostre uma janela.
    /// </summary>
    public static void AllowNextProcessToTakeFocus() => NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
}
