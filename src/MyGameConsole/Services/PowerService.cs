using System.Diagnostics;
using MyGameConsole.Native;

namespace MyGameConsole.Services;

public sealed class PowerService
{
    public void Sleep()
    {
        NativeMethods.SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
    }

    public void Hibernate()
    {
        NativeMethods.SetSuspendState(hibernate: true, forceCritical: false, disableWakeEvent: false);
    }

    public void Shutdown() => RunShutdown("/s /t 0");

    public void Restart() => RunShutdown("/r /t 0");

    private static void RunShutdown(string args)
    {
        Process.Start(new ProcessStartInfo("shutdown", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }
}
