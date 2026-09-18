using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace MyGameConsole.Services;

/// <summary>
/// Iniciar junto com o Windows de duas formas:
/// - usuário comum: entrada em HKCU\...\Run;
/// - administrador: tarefa agendada no logon com "privilégios mais altos" (necessária, por exemplo,
///   para controlar a luz do teclado em notebooks Lenovo). Criar ou remover essa tarefa passa pelo UAC.
/// </summary>
public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MyGameConsole";
    private const string TaskName = "MyGameConsole";
    private const int ErrorCancelled = 1223; // usuário recusou o UAC
    private const int UacTimeoutMs = 120_000;

    private static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Não foi possível determinar o caminho do executável.");

    /// <summary>Se este processo está rodando elevado (como administrador).</summary>
    public static bool IsProcessElevated { get; } = ComputeElevated();

    private static bool ComputeElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public bool IsRunKeyEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value
                && value.Trim('"').Equals(ExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>A tarefa elevada existe e aponta para este executável.</summary>
    public bool IsTaskEnabled =>
        QueryTaskXml() is { } xml && xml.Contains(ExePath, StringComparison.OrdinalIgnoreCase);

    public bool TaskExists => QueryTaskXml() is not null;

    /// <summary>
    /// A tarefa elevada existe e ainda espera alguns segundos depois do logon (criada por versões antigas do app,
    /// que usavam &lt;Delay&gt;PT5S&lt;/Delay&gt;).
    /// </summary>
    public bool IsTaskDelayed =>
        QueryTaskXml() is { } xml && xml.Contains("<Delay>", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Recria a tarefa elevada sem o atraso no logon. Recriar exige administrador: sem elevação e sem
    /// <paramref name="allowElevation"/>, não faz nada e devolve falso (fica pendente no checklist).
    /// </summary>
    public bool RemoveTaskDelay(bool allowElevation)
    {
        if (!IsTaskDelayed) return true;
        // Só mexe na tarefa deste executável: outra cópia do app (instalada ou de desenvolvimento) cuida da sua.
        if (!IsTaskEnabled) return false;
        if (!allowElevation && !IsProcessElevated) return false;
        CreateTask();
        return true;
    }

    public bool IsEnabled => IsRunKeyEnabled || IsTaskEnabled;

    /// <summary>
    /// Sincroniza o registro com a escolha do usuário. Com <paramref name="elevated"/>, cria a tarefa
    /// (pede UAC uma vez) e remove a entrada do Run; sem, faz o inverso.
    /// A entrada do Run é gravada antes de tentar a tarefa: se o UAC for recusado ou ignorado,
    /// o app continua iniciando com o Windows, ainda que sem privilégios.
    /// </summary>
    public void Set(bool enabled, bool elevated)
    {
        if (!enabled)
        {
            DisableRunKey();
            if (TaskExists) DeleteTask();
            return;
        }

        if (elevated)
        {
            if (IsTaskEnabled)
            {
                DisableRunKey();
                return;
            }

            EnableRunKey();
            CreateTask();
            DisableRunKey();
        }
        else
        {
            EnableRunKey();
            if (TaskExists) DeleteTask();
        }
    }

    // ------------------------------------------------------------------
    // HKCU\...\Run
    // ------------------------------------------------------------------

    private static void EnableRunKey()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{ExePath}\"");
    }

    private static void DisableRunKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    // ------------------------------------------------------------------
    // Tarefa agendada elevada
    // ------------------------------------------------------------------

    private static string? QueryTaskXml()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", $"/query /tn \"{TaskName}\" /xml")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // O schtasks escreve na página de código do console (ANSI/OEM), não em UTF-16.
                // Decodificar como Unicode transformava o XML em lixo e a tarefa nunca era reconhecida.
            });
            if (p is null) return null;

            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return p.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static void CreateTask()
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"MyGameConsole-task-{Environment.ProcessId}.xml");
        File.WriteAllText(xmlPath, BuildTaskXml(), Encoding.Unicode);
        try
        {
            RunElevated($"/create /tn \"{TaskName}\" /xml \"{xmlPath}\" /f",
                "criar a tarefa de inicialização como administrador");
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { /* temporário */ }
        }
    }

    private static void DeleteTask()
    {
        RunElevated($"/delete /tn \"{TaskName}\" /f", "remover a tarefa de inicialização como administrador");
    }

    /// <summary>Roda o schtasks elevado (UAC). Sem prompt se este processo já for administrador.</summary>
    private static void RunElevated(string arguments, string what)
    {
        Process? p;
        try
        {
            p = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new InvalidOperationException($"Permissão recusada no UAC ao {what}.");
        }

        if (p is null) throw new InvalidOperationException($"Não foi possível {what}.");

        // O prompt do UAC pode ficar aberto um bom tempo; sem resposta, não dá para ler o ExitCode.
        if (!p.WaitForExit(UacTimeoutMs))
        {
            throw new InvalidOperationException($"A confirmação do UAC para {what} não foi respondida a tempo.");
        }

        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"Falha ao {what} (schtasks retornou {p.ExitCode}).");
        }
    }

    private static string BuildTaskXml()
    {
        string user;
        using (var identity = WindowsIdentity.GetCurrent())
        {
            user = identity.Name;
        }

        var exe = SecurityElement.Escape(ExePath);
        var dir = SecurityElement.Escape(Path.GetDirectoryName(ExePath) ?? string.Empty);
        var userXml = SecurityElement.Escape(user);

        // Sem atraso no gatilho de logon: o app abre a tela do console antes de a área de trabalho terminar de
        // carregar (o ícone da bandeja volta sozinho quando a barra de tarefas aparece).
        // Ajustes importantes para um app de bandeja em notebook: sem limite de tempo de execução
        // (o padrão do schtasks mata a tarefa após 72 h) e sem restrições de bateria.
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Inicia o My Game Console no logon com privilégios de administrador.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{userXml}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{userXml}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>false</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
                <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <WorkingDirectory>{dir}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
