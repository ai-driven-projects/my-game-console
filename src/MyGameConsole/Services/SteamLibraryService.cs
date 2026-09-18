using MyGameConsole.App;

namespace MyGameConsole.Services;

/// <summary>Jogo instalado da biblioteca do Steam, com as artes do cache local e o tempo de jogo.</summary>
public sealed record SteamGame(
    int AppId,
    string Name,
    DateTime? LastPlayed,
    int PlaytimeMinutes,
    string? CapsulePath,
    string? HeroPath,
    string? LogoPath,
    string? HeaderPath);

/// <summary>
/// Lê a biblioteca do Steam direto dos arquivos locais, sem internet nem login: as pastas de biblioteca
/// (<c>steamapps/libraryfolders.vdf</c>), os jogos instalados (<c>appmanifest_*.acf</c>), o tempo de jogo da
/// conta (<c>userdata/&lt;id&gt;/config/localconfig.vdf</c>) e as artes que o próprio Steam guarda em
/// <c>appcache/librarycache</c> (capa vertical, fundo, logo). Os formatos não são documentados pela Valve:
/// tudo o que falhar vira "sem dado", nunca uma exceção.
/// </summary>
public sealed class SteamLibraryService
{
    // Ferramentas que aparecem como "instaladas" mas não são jogos: redistribuíveis, Proton, runtimes Linux.
    // Pelo nome, que é estável entre versões (cada Proton novo ganha outro appid).
    private const int SteamworksCommonAppId = 228980;

    private static readonly string[] ToolNamePrefixes =
        ["Proton ", "Steam Linux Runtime", "Steamworks Common", "Steamworks SDK"];

    private readonly SteamService _steam;

    public SteamLibraryService(SteamService steam)
    {
        _steam = steam;
    }

    /// <summary>Jogos instalados, do jogado mais recentemente para o mais antigo (nunca jogados por último, por nome).</summary>
    public IReadOnlyList<SteamGame> LoadInstalledGames()
    {
        var steamDir = _steam.FindSteamPath();
        if (steamDir is null) return [];

        try
        {
            var usage = LoadUsage(steamDir);
            var cacheDir = Path.Combine(steamDir, "appcache", "librarycache");
            var games = new Dictionary<int, SteamGame>();

            foreach (var libraryDir in LibraryFolders(steamDir))
            {
                string[] manifests;
                try { manifests = Directory.GetFiles(Path.Combine(libraryDir, "steamapps"), "appmanifest_*.acf"); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }

                foreach (var file in manifests)
                {
                    var state = Vdf.Load(file)?.Node("AppState");
                    if (state is null || !int.TryParse(state["appid"], out var appId) || games.ContainsKey(appId)) continue;

                    var name = state["name"];
                    // StateFlags 4 = totalmente instalado (também vale durante uma atualização).
                    if (string.IsNullOrWhiteSpace(name) || (state.Long("StateFlags") & 4) == 0 || IsTool(appId, name)) continue;

                    usage.TryGetValue(appId, out var use);
                    long lastPlayed = use.LastPlayed > 0 ? use.LastPlayed : state.Long("LastPlayed");

                    games[appId] = new SteamGame(
                        appId,
                        name.Trim(),
                        lastPlayed > 0 ? DateTimeOffset.FromUnixTimeSeconds(lastPlayed).LocalDateTime : null,
                        use.PlaytimeMinutes,
                        FindArt(cacheDir, appId, "library_capsule", "library_600x900"),
                        FindArt(cacheDir, appId, "library_hero"),
                        FindArt(cacheDir, appId, "logo"),
                        FindArt(cacheDir, appId, "library_header", "header"));
                }
            }

            return games.Values
                .OrderByDescending(g => g.LastPlayed ?? DateTime.MinValue)
                .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return [];
        }
    }

    /// <summary>Inicia o jogo pelo Steam (que cuida de atualização, nuvem e anticheat), sempre sem elevação.</summary>
    public void Launch(SteamGame game)
    {
        ForegroundWindow.AllowNextProcessToTakeFocus();
        _steam.OpenSteamUrl($"steam://rungameid/{game.AppId}");
    }

    private static bool IsTool(int appId, string name) =>
        appId == SteamworksCommonAppId || ToolNamePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> LibraryFolders(string steamDir)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(steamDir) };
        yield return steamDir;

        var folders = Vdf.Load(Path.Combine(steamDir, "steamapps", "libraryfolders.vdf"))?.Node("libraryfolders");
        if (folders is null) yield break;

        foreach (var (_, folder) in folders.Children)
        {
            var path = folder["path"];
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;
            if (seen.Add(Path.GetFullPath(path))) yield return path;
        }
    }

    /// <summary>
    /// Última vez jogado (Unix) e minutos jogados por app, da conta que usou o Steam por último neste PC
    /// (a pasta de <c>userdata</c> com o <c>localconfig.vdf</c> mais recente).
    /// </summary>
    private static Dictionary<int, (long LastPlayed, int PlaytimeMinutes)> LoadUsage(string steamDir)
    {
        var result = new Dictionary<int, (long, int)>();
        var userdata = Path.Combine(steamDir, "userdata");
        if (!Directory.Exists(userdata)) return result;

        var config = Directory.GetDirectories(userdata)
            .Select(d => new FileInfo(Path.Combine(d, "config", "localconfig.vdf")))
            .Where(f => f.Exists)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        if (config is null) return result;

        var apps = Vdf.Load(config.FullName)?.Path("UserLocalConfigStore", "Software", "Valve", "Steam", "apps");
        if (apps is null) return result;

        foreach (var (key, app) in apps.Children)
        {
            if (int.TryParse(key, out var appId))
            {
                result[appId] = (app.Long("LastPlayed"), (int)Math.Min(int.MaxValue, app.Long("Playtime")));
            }
        }

        return result;
    }

    /// <summary>
    /// Procura uma arte no cache do Steam pelo primeiro nome da lista que existir. O lugar mudou entre versões
    /// do Steam e os três formatos convivem no mesmo PC: solta na raiz (<c>librarycache/&lt;appid&gt;_library_600x900.jpg</c>),
    /// numa pasta por app (<c>librarycache/&lt;appid&gt;/library_600x900.jpg</c>) e, desde 2024, em subpastas de
    /// nome aleatório (<c>librarycache/&lt;appid&gt;/&lt;hash&gt;/library_capsule.jpg</c>).
    /// </summary>
    private static string? FindArt(string cacheDir, int appId, params string[] names)
    {
        try
        {
            var appDir = Path.Combine(cacheDir, appId.ToString());
            List<FileInfo> files = Directory.Exists(appDir)
                ? Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories).Where(IsImage).Select(f => new FileInfo(f)).ToList()
                : [];

            foreach (var name in names)
            {
                var found = files
                    .Where(f => Path.GetFileNameWithoutExtension(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (found is not null) return found.FullName;

                foreach (var ext in new[] { ".jpg", ".png" })
                {
                    var legacy = Path.Combine(cacheDir, $"{appId}_{name}{ext}");
                    if (File.Exists(legacy)) return legacy;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return null;
    }

    private static bool IsImage(string path) =>
        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
}
