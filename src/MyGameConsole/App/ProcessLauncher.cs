using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MyGameConsole.Services;

namespace MyGameConsole.App;

/// <summary>
/// Abre programas, atalhos e URLs. Quando o app está rodando como administrador, os filhos herdariam
/// a elevação (Steam e jogos como administrador é má ideia); por isso, nesse caso, o lançamento é
/// delegado ao Explorer do usuário, que roda sem privilégios. Argumentos são preservados por meio
/// de um .lnk temporário, já que o Explorer não repassa argumentos.
/// </summary>
public static class ProcessLauncher
{
    public static void Start(string target, string? arguments = null, string? workingDirectory = null)
    {
        if (!StartupService.IsProcessElevated)
        {
            Process.Start(new ProcessStartInfo(target, arguments ?? string.Empty)
            {
                UseShellExecute = true,
                WorkingDirectory = workingDirectory ?? string.Empty,
            });
            return;
        }

        var launch = string.IsNullOrWhiteSpace(arguments)
            ? target
            : CreateShortcut(target, arguments, workingDirectory);

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{launch}\"") { UseShellExecute = true });
    }

    private static string CreateShortcut(string target, string arguments, string? workingDirectory)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyGameConsole", "launch");
        Directory.CreateDirectory(dir);

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(target + "\n" + arguments + "\n" + workingDirectory)))[..12];
        var path = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(target)}-{hash}.lnk");
        if (File.Exists(path)) return path;

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell indisponível para criar o atalho.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = target;
        link.Arguments = arguments;
        link.WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(target) ?? string.Empty;
        link.Save();
        return path;
    }
}
