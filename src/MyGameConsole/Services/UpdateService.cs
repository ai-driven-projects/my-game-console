using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MyGameConsole.App;

namespace MyGameConsole.Services;

public enum UpdateState
{
    /// <summary>Nada consultado ainda.</summary>
    Idle,
    Checking,
    UpToDate,
    /// <summary>Há uma versão mais nova publicada; o instalador ainda não foi baixado.</summary>
    Available,
    Downloading,
    /// <summary>O instalador da nova versão está baixado (e conferido) em disco.</summary>
    ReadyToInstall,
    Failed,
}

/// <summary>Dados de uma release publicada no GitHub que é mais nova do que a versão em execução.</summary>
public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string ReleaseUrl,
    string Notes,
    string MsiName,
    string MsiUrl,
    long MsiSize,
    string? Sha256Url)
{
    public string VersionText => UpdateService.Format(Version);
}

/// <summary>
/// Verifica, baixa e instala atualizações publicadas como releases no GitHub
/// (https://github.com/ai-driven-projects/my-game-console/releases).
///
/// Contrato com o script de publicação (scripts/release.ps1):
/// - a tag da release é a versão com prefixo "v" (ex.: v1.0.1) e segue Major.Minor.Patch;
/// - a release traz o instalador "MyGameConsole-&lt;versão&gt;-Setup.msi" e, opcionalmente, um
///   "*.msi.sha256" com o hash do instalador, que é conferido antes de instalar.
///
/// A instalação é feita pelo próprio MSI (msiexec), que fecha o app, substitui os arquivos e oferece
/// reabrir ao concluir. Os eventos são entregues na thread da interface (contexto capturado na criação).
/// </summary>
public sealed class UpdateService
{
    public const string RepoOwner = "ai-driven-projects";
    public const string RepoName = "my-game-console";
    public static string ReleasesPageUrl => $"https://github.com/{RepoOwner}/{RepoName}/releases";

    private static readonly Uri LatestReleaseApi = new($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly string _downloadDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyGameConsole", "updates");

    private int _lastReportedPercent = -1;

    public UpdateService()
    {
        CleanupOldDownloads();
    }

    /// <summary>Versão do app em execução (Major.Minor.Patch, vinda do &lt;Version&gt; do csproj).</summary>
    public Version CurrentVersion { get; } = ReadCurrentVersion();

    public string CurrentVersionText => Format(CurrentVersion);

    public UpdateState State { get; private set; }

    /// <summary>Atualização encontrada na última verificação, se houver.</summary>
    public UpdateInfo? Available { get; private set; }

    /// <summary>Progresso do download (0 a 1).</summary>
    public double Progress { get; private set; }

    /// <summary>Caminho do instalador baixado, quando <see cref="State"/> é <see cref="UpdateState.ReadyToInstall"/>.</summary>
    public string? DownloadedPath { get; private set; }

    public string? Error { get; private set; }

    public DateTime? LastCheck { get; private set; }

    public bool IsBusy => State is UpdateState.Checking or UpdateState.Downloading;

    /// <summary>Disparado (na thread da interface) a cada mudança de estado ou de progresso.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Texto curto do estado atual, para menus e para a tela do console.</summary>
    public string StatusText => State switch
    {
        UpdateState.Checking => "Verificando...",
        UpdateState.UpToDate => $"Versão {CurrentVersionText} (atualizado)",
        UpdateState.Available => $"Nova versão {Available!.VersionText} disponível",
        UpdateState.Downloading => $"Baixando {Available!.VersionText}... {Progress:P0}",
        UpdateState.ReadyToInstall => $"Versão {Available!.VersionText} pronta para instalar",
        UpdateState.Failed => $"Falhou: {Error}",
        _ => $"Versão {CurrentVersionText}",
    };

    // ------------------------------------------------------------------
    // Verificar
    // ------------------------------------------------------------------

    /// <summary>
    /// Consulta a release mais recente no GitHub. Retorna os dados se ela for mais nova que a versão em
    /// execução, ou nulo se já estiver atualizado. Lança <see cref="InvalidOperationException"/> com
    /// mensagem amigável em caso de falha (sem rede, limite da API, release sem instalador).
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (IsBusy) return Available;

        SetState(UpdateState.Checking);
        try
        {
            var latest = await FetchLatestReleaseAsync(ct).ConfigureAwait(false);
            LastCheck = DateTime.Now;

            if (latest is null || latest.Version <= CurrentVersion)
            {
                Available = null;
                SetState(UpdateState.UpToDate);
                return null;
            }

            if (string.IsNullOrEmpty(latest.MsiUrl))
            {
                throw new InvalidOperationException(
                    $"A versão {latest.VersionText} foi publicada sem o instalador (.msi). Veja a página de releases.");
            }

            Available = latest;
            var cached = Path.Combine(_downloadDir, latest.MsiName);
            if (DownloadedPath is null && File.Exists(cached) && new FileInfo(cached).Length == latest.MsiSize)
            {
                // Já baixado numa sessão anterior (e conferido na hora): não baixa de novo.
                DownloadedPath = cached;
            }

            SetState(DownloadedPath is not null ? UpdateState.ReadyToInstall : UpdateState.Available);
            return latest;
        }
        catch (OperationCanceledException)
        {
            SetState(UpdateState.Idle);
            throw;
        }
        catch (Exception ex)
        {
            Fail(DescribeCheckFailure(ex));
            throw new InvalidOperationException(Error, ex);
        }
    }

    private static async Task<UpdateInfo?> FetchLatestReleaseAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Nenhuma release publicada ainda (ou repositório privado, que a API não expõe sem token).
            return null;
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException("O GitHub limitou as consultas por enquanto. Tente novamente em alguns minutos.");
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? string.Empty : string.Empty;
        if (!TryParseVersion(tag, out var version))
        {
            throw new InvalidOperationException($"A release mais recente tem uma tag inválida (\"{tag}\"); esperado algo como v1.0.1.");
        }

        var releaseUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
        var notes = root.TryGetProperty("body", out var bodyProp) && bodyProp.ValueKind == JsonValueKind.String
            ? bodyProp.GetString()!.Trim()
            : string.Empty;

        string msiName = string.Empty, msiUrl = string.Empty;
        string? shaUrl = null;
        long msiSize = 0;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;

                if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    msiName = name;
                    msiUrl = url;
                    msiSize = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0;
                }
                else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                {
                    shaUrl = url;
                }
            }
        }

        return new UpdateInfo(version, tag, releaseUrl ?? ReleasesPageUrl, notes, msiName, msiUrl, msiSize, shaUrl);
    }

    private static string DescribeCheckFailure(Exception ex) => ex switch
    {
        InvalidOperationException => ex.Message,
        HttpRequestException or TaskCanceledException => "Não foi possível consultar o GitHub. Verifique a conexão com a internet.",
        JsonException => "O GitHub respondeu em um formato inesperado.",
        _ => ex.Message,
    };

    // ------------------------------------------------------------------
    // Baixar
    // ------------------------------------------------------------------

    /// <summary>
    /// Baixa o instalador da atualização encontrada para %LocalAppData%\MyGameConsole\updates,
    /// confere o SHA-256 (se a release publicou um) e deixa o estado em <see cref="UpdateState.ReadyToInstall"/>.
    /// </summary>
    public async Task<string> DownloadAsync(CancellationToken ct = default)
    {
        var info = Available ?? throw new InvalidOperationException("Nenhuma atualização disponível para baixar.");
        if (State == UpdateState.Downloading) throw new InvalidOperationException("O download já está em andamento.");
        if (State == UpdateState.ReadyToInstall && DownloadedPath is not null && File.Exists(DownloadedPath)) return DownloadedPath;

        Directory.CreateDirectory(_downloadDir);
        var target = Path.Combine(_downloadDir, info.MsiName);
        var partial = target + ".part";

        Progress = 0;
        _lastReportedPercent = -1;
        SetState(UpdateState.Downloading);

        try
        {
            using (var response = await Http.GetAsync(info.MsiUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? info.MsiSize;

                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total > 0) ReportProgress(done / (double)total);
                }
            }

            if (info.Sha256Url is not null)
            {
                await VerifySha256Async(partial, info.Sha256Url, ct).ConfigureAwait(false);
            }

            File.Move(partial, target, overwrite: true);
            DownloadedPath = target;
            Progress = 1;
            SetState(UpdateState.ReadyToInstall);
            return target;
        }
        catch (OperationCanceledException)
        {
            TryDelete(partial);
            SetState(UpdateState.Available);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(partial);
            Fail(ex is InvalidOperationException ? ex.Message
                : ex is HttpRequestException ? "Falha ao baixar o instalador. Verifique a conexão com a internet."
                : ex.Message);
            throw new InvalidOperationException(Error, ex);
        }
    }

    private static async Task VerifySha256Async(string file, string sha256Url, CancellationToken ct)
    {
        var text = await Http.GetStringAsync(sha256Url, ct).ConfigureAwait(false);
        // Formato "hash  nome-do-arquivo" (como o sha256sum); aceita também só o hash.
        var expected = text.Trim().Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expected is null || expected.Length != 64)
        {
            throw new InvalidOperationException("O arquivo de verificação (.sha256) da release é inválido.");
        }

        await using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O instalador baixado não confere com o hash publicado na release. Download descartado.");
        }
    }

    private void ReportProgress(double value)
    {
        Progress = Math.Clamp(value, 0, 1);
        var percent = (int)(Progress * 100);
        if (percent == _lastReportedPercent) return;
        _lastReportedPercent = percent;
        RaiseStateChanged();
    }

    // ------------------------------------------------------------------
    // Instalar
    // ------------------------------------------------------------------

    /// <summary>
    /// Executa o instalador baixado (msiexec). O MSI pede o UAC, fecha este app, substitui os arquivos
    /// e oferece reabri-lo ao concluir. Quem chama deve encerrar o app logo em seguida, para que o
    /// Modo Console (explorer) seja restaurado pelo caminho normal de saída.
    /// </summary>
    public void Install()
    {
        var path = DownloadedPath;
        if (State != UpdateState.ReadyToInstall || path is null || !File.Exists(path))
        {
            throw new InvalidOperationException("O instalador da atualização ainda não foi baixado.");
        }

        Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{path}\"")
        {
            UseShellExecute = true,
            WorkingDirectory = _downloadDir,
        });
    }

    /// <summary>Abre no navegador a página da release (ou a lista de releases).</summary>
    public void OpenReleasePage() =>
        ProcessLauncher.Start(Available?.ReleaseUrl ?? ReleasesPageUrl);

    // ------------------------------------------------------------------
    // Auxiliares
    // ------------------------------------------------------------------

    public static string Format(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    /// <summary>Aceita "v1.2.3", "1.2.3" ou "1.2" e normaliza para Major.Minor.Patch.</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        if (!Version.TryParse(s, out var parsed)) return false;

        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }

    private static Version ReadCurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromMinutes(10), // downloads grandes; a consulta à API é rápida de qualquer forma
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MyGameConsole", Format(ReadCurrentVersion())));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    /// <summary>Apaga instaladores de versões já instaladas (ou downloads interrompidos) da pasta de updates.</summary>
    private void CleanupOldDownloads()
    {
        try
        {
            if (!Directory.Exists(_downloadDir)) return;

            foreach (var file in Directory.EnumerateFiles(_downloadDir))
            {
                var name = Path.GetFileName(file);
                if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(file);
                    continue;
                }

                // MyGameConsole-1.0.1-Setup.msi -> 1.0.1
                var parts = Path.GetFileNameWithoutExtension(name).Split('-');
                if (parts.Length >= 2 && TryParseVersion(parts[1], out var v) && v <= CurrentVersion)
                {
                    TryDelete(file);
                }
            }
        }
        catch
        {
            // limpeza é melhor esforço
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // ignora
        }
    }

    private void Fail(string message)
    {
        Error = message;
        SetState(UpdateState.Failed);
    }

    private void SetState(UpdateState state)
    {
        State = state;
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        if (_ui is not null)
        {
            _ui.Post(_ => StateChanged?.Invoke(this, EventArgs.Empty), null);
        }
        else
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
