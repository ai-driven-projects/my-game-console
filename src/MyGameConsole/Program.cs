using System.Diagnostics;
using MyGameConsole.App;

namespace MyGameConsole;

internal static class Program
{
    private const string MutexName = @"Local\MyGameConsole.SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        // "--wake-password <spec>": instância elevada (UAC) lançada pelo próprio app só para gravar
        // a senha ao acordar nos planos de energia. Não abre a bandeja nem disputa o mutex.
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], Services.PowerService.WakePasswordArgument, StringComparison.OrdinalIgnoreCase))
            {
                return Services.PowerService.RunWakePasswordCommand(args[i + 1]);
            }
        }

        // "--wait-for-pid N": usado ao reiniciar como administrador; espera a instância antiga sair
        // antes de disputar o mutex de instância única.
        WaitForPreviousInstance(args);

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        // Segunda instância: encerra em silêncio (o app já está na bandeja).
        if (!isFirstInstance) return 0;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

        Application.Run(new TrayApplicationContext());
        return 0;
    }

    private static void WaitForPreviousInstance(string[] args)
    {
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (!string.Equals(args[i], "--wait-for-pid", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(args[i + 1], out var pid)) return;

            try
            {
                using var previous = Process.GetProcessById(pid);
                previous.WaitForExit(15000);
            }
            catch
            {
                // já saiu
            }

            return;
        }
    }

    private static void ShowFatal(Exception? ex)
    {
        MessageBox.Show(
            $"Ocorreu um erro inesperado:\n\n{ex?.Message}",
            "My Game Console",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
