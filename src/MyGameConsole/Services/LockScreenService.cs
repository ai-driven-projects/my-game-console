using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace MyGameConsole.Services;

/// <summary>
/// Imagem da tela de bloqueio (e da tela de entrada, que usa a mesma) definida para o computador, pelos valores
/// de <c>HKLM\...\PersonalizationCSP</c> — os mesmos que o Windows usa quando a imagem vem de uma política.
/// Enquanto está definida, o Windows mostra essa imagem e trava a escolha em Personalização > Tela de bloqueio;
/// apagar os valores devolve a escolha ao usuário.
///
/// A tela de bloqueio é desenhada por um processo do sistema, que não lê o perfil do usuário: a imagem é copiada
/// para <see cref="ImagePath"/>, em ProgramData. Gravar em HKLM exige administrador: sem elevação, o app relança a
/// si mesmo elevado (UAC) com <see cref="LockScreenArgument"/>, como faz com a senha ao acordar.
/// </summary>
public sealed class LockScreenService
{
    /// <summary>Argumento de linha de comando do modo "só gravar a tela de bloqueio e sair".</summary>
    public const string LockScreenArgument = "--lock-screen";

    private const string CspKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";
    private const string PathValue = "LockScreenImagePath";
    private const string UrlValue = "LockScreenImageUrl";
    private const string StatusValue = "LockScreenImageStatus";
    private static readonly string[] Values = [PathValue, UrlValue, StatusValue];

    private const int ErrorCancelled = 1223; // usuário recusou o UAC
    private const int UacTimeoutMs = 120_000;

    /// <summary>Cópia da imagem que a tela de bloqueio usa (legível pelo sistema).</summary>
    public static string ImagePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyGameConsole", "lockscreen.jpg");

    /// <summary>Valores atuais (nulo = ausente). Ler funciona sem administrador.</summary>
    public Dictionary<string, string?> Get()
    {
        using var key = Registry.LocalMachine.OpenSubKey(CspKey);
        return Values.ToDictionary(v => v, v => key?.GetValue(v)?.ToString());
    }

    /// <summary>A tela de bloqueio está com a imagem do app.</summary>
    public bool IsApplied
    {
        get
        {
            var values = Get();
            return values[StatusValue] == "1"
                && string.Equals(values[PathValue], ImagePath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(ImagePath);
        }
    }

    /// <summary>
    /// Usa <paramref name="sourceImage"/> (JPG, PNG ou BMP) na tela de bloqueio. Devolve falso quando precisaria de
    /// administrador e <paramref name="allowElevation"/> é falso (o checklist mostra como pendente).
    /// </summary>
    public bool Apply(string sourceImage, bool allowElevation)
    {
        var wanted = new Dictionary<string, string?>
        {
            [PathValue] = ImagePath,
            [UrlValue] = ImagePath,
            [StatusValue] = "1",
        };

        var jpeg = ToJpeg(sourceImage);
        bool sameImage = File.Exists(ImagePath) && FilesEqual(jpeg, ImagePath);
        if (sameImage && IsApplied) return true;

        return Run(new Command(sameImage ? null : jpeg, wanted), allowElevation);
    }

    /// <summary>
    /// Regrava a imagem como JPEG ao lado das configurações do usuário: a tela de bloqueio recebe sempre o mesmo
    /// formato, qualquer que seja a origem (PNG, BMP, o JPEG que o Windows guarda do papel de parede).
    /// </summary>
    private static string ToJpeg(string source)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyGameConsole");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "lockscreen.jpg");

        using var stream = new MemoryStream(File.ReadAllBytes(source));
        using var image = Image.FromStream(stream);
        var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
        using var parameters = new System.Drawing.Imaging.EncoderParameters(1);
        parameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
        image.Save(target, codec, parameters);
        return target;
    }

    /// <summary>Devolve os valores guardados antes do Modo Game (normalmente: nenhum, e a escolha volta ao usuário).</summary>
    public bool Restore(Dictionary<string, string?> original, bool allowElevation)
    {
        var current = Get();
        if (Values.All(v => current[v] == original.GetValueOrDefault(v))) return true;
        return Run(new Command(null, original), allowElevation);
    }

    /// <summary>
    /// Entrada do processo elevado: aplica o comando vindo de <see cref="LockScreenArgument"/> (JSON em Base64)
    /// e devolve o código de saída.
    /// </summary>
    public static int RunCommand(string spec)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(spec));
            var command = JsonSerializer.Deserialize<Command>(json) ?? throw new FormatException();
            ApplyDirect(command);
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

    /// <summary>O que gravar: a imagem a copiar (nula = manter) e os valores (nulo no valor = apagar).</summary>
    private sealed record Command(string? Source, Dictionary<string, string?> Values);

    private static bool Run(Command command, bool allowElevation)
    {
        try
        {
            ApplyDirect(command);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException
                                   && !StartupService.IsProcessElevated)
        {
            if (!allowElevation) return false;
            ApplyElevated(command);
            return true;
        }
    }

    private static void ApplyDirect(Command command)
    {
        if (command.Source is { } source)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ImagePath)!);
            File.Copy(source, ImagePath, overwrite: true);
        }

        using var key = Registry.LocalMachine.CreateSubKey(CspKey, writable: true)
            ?? throw new UnauthorizedAccessException();
        foreach (var name in Values)
        {
            var value = command.Values.GetValueOrDefault(name);
            if (value is null) key.DeleteValue(name, throwOnMissingValue: false);
            else if (name == StatusValue) key.SetValue(name, int.Parse(value), RegistryValueKind.DWord);
            else key.SetValue(name, value, RegistryValueKind.String);
        }
    }

    /// <summary>Relança este executável como administrador (UAC) só para gravar, e espera ele terminar.</summary>
    private static void ApplyElevated(Command command)
    {
        const string what = "mudar a imagem da tela de bloqueio";
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Não foi possível determinar o caminho do executável.");
        var spec = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command)));

        Process? p;
        try
        {
            p = Process.Start(new ProcessStartInfo(exe, $"{LockScreenArgument} {spec}")
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

        if (!p.WaitForExit(UacTimeoutMs))
        {
            throw new InvalidOperationException($"A confirmação do UAC para {what} não foi respondida a tempo.");
        }

        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"Falha ao {what} ({new Win32Exception(p.ExitCode).Message}).");
        }
    }

    private static bool FilesEqual(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            return fa.Length == fb.Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch
        {
            return false;
        }
    }
}
