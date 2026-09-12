using System.Diagnostics;
using Microsoft.Win32;

namespace MyGameConsole.Services;

/// <summary>
/// Leitura do login automático do Windows (o que o netplwiz configura). O app só consulta: ligar exige
/// digitar a senha do usuário, e isso fica a cargo do próprio usuário, na tela do Windows.
/// </summary>
public sealed class LogonService
{
    private const string WinlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
    private const string PasswordLessKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device";

    /// <summary>O Windows entra direto na conta ao ligar o PC, sem pedir senha.</summary>
    public bool IsAutoLogonEnabled
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(WinlogonKey);
                return key?.GetValue("AutoAdminLogon") is string on && on.Trim() == "1"
                    && key.GetValue("DefaultUserName") is string user && !string.IsNullOrWhiteSpace(user);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// "Permitir apenas a entrada do Windows Hello" está ligado. Com isso ligado, o netplwiz esconde a
    /// opção de login automático, então precisa ser desligado antes.
    /// </summary>
    public bool IsWindowsHelloOnly
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(PasswordLessKey);
                return key?.GetValue("DevicePasswordLessBuildVersion") is int v && v != 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Abre a janela "Contas de Usuário" (netplwiz), onde o login automático é ligado.</summary>
    public void OpenUserAccounts() =>
        Process.Start(new ProcessStartInfo("netplwiz") { UseShellExecute = true });

    /// <summary>Abre Configurações > Contas > Opções de entrada.</summary>
    public void OpenSignInOptions() =>
        Process.Start(new ProcessStartInfo("ms-settings:signinoptions") { UseShellExecute = true });
}
