using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MyGameConsole.Models;
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

    // ------------------------------------------------------------------
    // Senha ao acordar
    // (Configurações > Contas > Opções de entrada > "Se você esteve ausente, quando o Windows
    //  deverá exigir que você entre novamente?"). É um ajuste por plano de energia, e notebooks
    //  como os Legion trocam de plano pelo Fn+Q, por isso o app lê e grava em todos os planos.
    //  Ler funciona como usuário comum; gravar exige administrador. Quando o processo não é
    //  elevado, o app relança a si mesmo elevado (UAC) só para gravar, com "--wake-password".
    // ------------------------------------------------------------------

    /// <summary>Argumento de linha de comando do modo "só gravar a senha ao acordar e sair".</summary>
    public const string WakePasswordArgument = "--wake-password";

    private const int ErrorAccessDenied = 5;
    private const int ErrorCancelled = 1223; // usuário recusou o UAC
    private const int UacTimeoutMs = 120_000;

    /// <summary>Valor atual de "exigir senha ao acordar" em cada plano de energia, chaveado pelo GUID do plano.</summary>
    public Dictionary<string, WakePasswordState> GetWakePasswordByScheme()
    {
        var result = new Dictionary<string, WakePasswordState>(StringComparer.OrdinalIgnoreCase);
        foreach (var scheme in EnumerateSchemes())
        {
            var ac = NativeMethods.PowerReadACValueIndex(IntPtr.Zero, scheme,
                NativeMethods.GUID_NO_SUBGROUP, NativeMethods.GUID_LOCK_CONSOLE_ON_WAKE, out var acValue);
            var dc = NativeMethods.PowerReadDCValueIndex(IntPtr.Zero, scheme,
                NativeMethods.GUID_NO_SUBGROUP, NativeMethods.GUID_LOCK_CONSOLE_ON_WAKE, out var dcValue);

            // Plano sem o valor gravado usa o padrão do Windows (exigir senha).
            result[scheme.ToString()] = new WakePasswordState
            {
                PluggedIn = ac != NativeMethods.ERROR_SUCCESS || acValue != 0,
                OnBattery = dc != NativeMethods.ERROR_SUCCESS || dcValue != 0,
            };
        }

        return result;
    }

    /// <summary>Verdadeiro se algum plano de energia ainda pede senha ao acordar (na tomada ou na bateria).</summary>
    public bool IsWakePasswordRequiredAnywhere() =>
        GetWakePasswordByScheme().Values.Any(v => v.PluggedIn || v.OnBattery);

    /// <summary>
    /// Liga ou desliga a senha ao acordar em todos os planos de energia. Não faz nada se já estiver assim.
    /// Devolve falso quando a mudança precisaria de administrador e <paramref name="allowElevation"/> é falso.
    /// </summary>
    public bool SetWakePasswordRequired(bool required, bool allowElevation)
    {
        var wanted = EnumerateSchemes().ToDictionary(
            g => g.ToString(),
            _ => new WakePasswordState { PluggedIn = required, OnBattery = required },
            StringComparer.OrdinalIgnoreCase);
        return Write(wanted, allowElevation);
    }

    /// <summary>Devolve o valor guardado de cada plano. Planos que não existem mais são ignorados.</summary>
    public bool RestoreWakePassword(Dictionary<string, WakePasswordState> backup, bool allowElevation) =>
        Write(backup, allowElevation);

    /// <summary>
    /// Entrada do processo elevado: aplica a especificação vinda de <see cref="WakePasswordArgument"/>
    /// ("guid:ac:dc,guid:ac:dc", com 1 = exigir senha) e devolve o código de saída.
    /// </summary>
    public static int RunWakePasswordCommand(string spec)
    {
        try
        {
            var changes = ParseSpec(spec);
            ApplyDirect(changes);
            return 0;
        }
        catch (Win32Exception ex)
        {
            return ex.NativeErrorCode == 0 ? 1 : ex.NativeErrorCode;
        }
        catch
        {
            return 1;
        }
    }

    private bool Write(Dictionary<string, WakePasswordState> wanted, bool allowElevation)
    {
        var current = GetWakePasswordByScheme();
        var changes = wanted
            .Where(kv => current.TryGetValue(kv.Key, out var c)
                         && (c.PluggedIn != kv.Value.PluggedIn || c.OnBattery != kv.Value.OnBattery))
            .Select(kv => (Scheme: Guid.Parse(kv.Key), kv.Value.PluggedIn, kv.Value.OnBattery))
            .ToList();
        if (changes.Count == 0) return true;

        try
        {
            ApplyDirect(changes);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorAccessDenied && !StartupService.IsProcessElevated)
        {
            if (!allowElevation) return false;
            ApplyElevated(changes);
            return true;
        }
    }

    private static void ApplyDirect(List<(Guid Scheme, bool PluggedIn, bool OnBattery)> changes)
    {
        foreach (var (scheme, pluggedIn, onBattery) in changes)
        {
            Check(NativeMethods.PowerWriteACValueIndex(IntPtr.Zero, scheme,
                NativeMethods.GUID_NO_SUBGROUP, NativeMethods.GUID_LOCK_CONSOLE_ON_WAKE, pluggedIn ? 1u : 0u));
            Check(NativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, scheme,
                NativeMethods.GUID_NO_SUBGROUP, NativeMethods.GUID_LOCK_CONSOLE_ON_WAKE, onBattery ? 1u : 0u));
        }

        ReapplyActiveScheme();
    }

    /// <summary>Relança este executável como administrador (UAC) só para gravar, e espera ele terminar.</summary>
    private static void ApplyElevated(List<(Guid Scheme, bool PluggedIn, bool OnBattery)> changes)
    {
        const string what = "mudar a senha ao acordar";
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Não foi possível determinar o caminho do executável.");
        var spec = string.Join(",", changes.Select(c => $"{c.Scheme}:{(c.PluggedIn ? 1 : 0)}:{(c.OnBattery ? 1 : 0)}"));

        Process? p;
        try
        {
            p = Process.Start(new ProcessStartInfo(exe, $"{WakePasswordArgument} {spec}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new InvalidOperationException(
                $"Permissão recusada no UAC ao {what}. Dá para fazer à mão em Configurações > Contas > Opções de entrada.");
        }

        if (p is null) throw new InvalidOperationException($"Não foi possível {what}.");

        if (!p.WaitForExit(UacTimeoutMs))
        {
            throw new InvalidOperationException($"A confirmação do UAC para {what} não foi respondida a tempo.");
        }

        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"Falha ao {what} ({new Win32Exception(p.ExitCode).Message}).");
        }
    }

    private static List<(Guid Scheme, bool PluggedIn, bool OnBattery)> ParseSpec(string spec)
    {
        var list = new List<(Guid, bool, bool)>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(':');
            if (f.Length != 3) throw new FormatException(part);
            list.Add((Guid.Parse(f[0]), f[1] == "1", f[2] == "1"));
        }

        return list;
    }

    /// <summary>Mudanças no plano ativo só valem depois de reativá-lo (o mesmo que o powercfg /setactive faz).</summary>
    private static void ReapplyActiveScheme()
    {
        if (NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var ptr) != NativeMethods.ERROR_SUCCESS || ptr == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var active = Marshal.PtrToStructure<Guid>(ptr);
            Check(NativeMethods.PowerSetActiveScheme(IntPtr.Zero, active));
        }
        finally
        {
            NativeMethods.LocalFree(ptr);
        }
    }

    private static IEnumerable<Guid> EnumerateSchemes()
    {
        for (uint i = 0; ; i++)
        {
            uint size = 16;
            var rc = NativeMethods.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.ACCESS_SCHEME, i, out var guid, ref size);
            if (rc == NativeMethods.ERROR_NO_MORE_ITEMS) yield break;
            Check(rc);
            yield return guid;
        }
    }

    private static void Check(uint rc)
    {
        if (rc != NativeMethods.ERROR_SUCCESS)
        {
            throw new Win32Exception((int)rc);
        }
    }

    private static void RunShutdown(string args)
    {
        Process.Start(new ProcessStartInfo("shutdown", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }
}
