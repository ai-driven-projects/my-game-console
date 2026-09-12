using System.Runtime.InteropServices;
using MyGameConsole.Native;

namespace MyGameConsole.Services;

/// <summary>
/// Liga e desliga a tela sem suspender o PC ("repouso de console"): a máquina segue ligada e o
/// app pode reacender a tela quando o controle for usado. Entrada de controle não conta como
/// atividade para o Windows, por isso <see cref="TurnOn"/> simula um leve movimento do mouse.
/// Chame sempre da mesma thread (a da interface): SetThreadExecutionState é por thread.
/// </summary>
public sealed class DisplayService
{
    /// <summary>Apaga a tela. O handle é usado só para entregar o comando ao DefWindowProc.</summary>
    public void TurnOff(IntPtr windowHandle)
    {
        NativeMethods.SendMessage(windowHandle, NativeMethods.WM_SYSCOMMAND,
            (IntPtr)NativeMethods.SC_MONITORPOWER, (IntPtr)NativeMethods.MONITOR_OFF);
    }

    /// <summary>Reacende a tela por três caminhos: pedido de energia, comando ao monitor e um toque no mouse.</summary>
    public void TurnOn(IntPtr windowHandle)
    {
        NativeMethods.SetThreadExecutionState(NativeMethods.ES_DISPLAY_REQUIRED);
        NativeMethods.SendMessage(windowHandle, NativeMethods.WM_SYSCOMMAND,
            (IntPtr)NativeMethods.SC_MONITORPOWER, (IntPtr)NativeMethods.MONITOR_ON);

        var nudge = new NativeMethods.Input[]
        {
            new() { type = NativeMethods.INPUT_MOUSE, mi = new NativeMethods.MouseInput { dx = 1, dy = 0, dwFlags = NativeMethods.MOUSEEVENTF_MOVE } },
            new() { type = NativeMethods.INPUT_MOUSE, mi = new NativeMethods.MouseInput { dx = -1, dy = 0, dwFlags = NativeMethods.MOUSEEVENTF_MOVE } },
        };
        NativeMethods.SendInput((uint)nudge.Length, nudge, Marshal.SizeOf<NativeMethods.Input>());
    }

    /// <summary>
    /// Impede (ou volta a permitir) que o Windows suspenda o PC por inatividade enquanto a tela
    /// estiver apagada, para o controle conseguir "acordar" a tela depois.
    /// </summary>
    public void KeepSystemAwake(bool keep)
    {
        NativeMethods.SetThreadExecutionState(keep
            ? NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED
            : NativeMethods.ES_CONTINUOUS);
    }
}
