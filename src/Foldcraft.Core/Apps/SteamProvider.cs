using System.Text.RegularExpressions;

namespace Foldcraft.Core.Apps;

/// <summary>
/// Finds Steam and its games: the install folder from the registry, its libraries from
/// libraryfolders.vdf and each game from its appmanifest. Files that can't be understood are
/// counted and skipped. Nothing here assumes a game is still where its manifest says.
/// </summary>
public sealed partial class SteamProvider(AppDiscoveryRoots roots) : IAppProvider
{
    private const string RegistryKey = @"Software\Valve\Steam";
    private const int MaxLibraries = 64;
    private const int MaxManifestsPerLibrary = 5000;

    /// <summary>Steam's shared installers, which appear as an app but aren't anything to launch.</summary>
    private const string RedistributablesAppId = "228980";

    private static readonly string[] ToolNamePrefixes = ["Proton ", "Steam Linux Runtime", "Steamworks "];
    private static readonly string[] NotTheGame = ["unins", "setup", "crash", "redist", "vcredist", "dxsetup", "dotnet", "report", "helper"];

    public string Name => "Steam";

    public AppProviderResult Discover(CancellationToken cancellationToken)
    {
        var steam = LocalSource.Normalize(roots.Registry?.GetString(RegistryKey, "SteamPath"));

        if (steam is null || !Directory.Exists(steam))
        {
            return new AppProviderResult([]);
        }

        var apps = new List<DetectedApp>();
        var skipped = 0;
        var client = Path.Combine(steam, "steam.exe");

        if (File.Exists(client))
        {
            apps.Add(new DetectedApp
            {
                Id = "steam:client",
                Name = "Steam",
                Source = AppSource.Steam,
                Launch = new AppLaunch(LaunchKind.Program, client, WorkingDirectory: steam),
                Icons = [new IconCandidate(client)],
                ProgramPath = client,
            });
        }

        foreach (var library in Libraries(steam, ref skipped))
        {
            var manifests = LocalSource.FilesIn(Path.Combine(library, "steamapps"), "appmanifest_*.acf").Take(MaxManifestsPerLibrary);

            foreach (var manifest in manifests)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var game = ReadManifest(manifest, library, steam, out var unreadable);

                if (game is not null)
                {
                    apps.Add(game);
                }

                skipped += unreadable ? 1 : 0;
            }
        }

        return new AppProviderResult(apps, skipped);
    }

    /// <summary>Every library Steam knows, starting with its own folder, which is always one.</summary>
    private static List<string> Libraries(string steam, ref int skipped)
    {
        var libraries = new List<string> { steam };
        var path = Path.Combine(steam, "steamapps", "libraryfolders.vdf");

        if (!File.Exists(path))
        {
            return libraries;
        }

        var root = VdfParser.TryParseFile(path)?.Child("libraryfolders");

        if (root is null)
        {
            skipped++;
            return libraries;
        }

        foreach (var entry in root.Children.Take(MaxLibraries))
        {
            // The current format nests the path. The older one holds it directly.
            var candidate = entry.Value ?? entry.Text("path");

            if (LocalSource.Normalize(candidate) is { } library &&
                !libraries.Contains(library, StringComparer.OrdinalIgnoreCase) &&
                Directory.Exists(library))
            {
                libraries.Add(library);
            }
        }

        return libraries;
    }

    private static DetectedApp? ReadManifest(string manifest, string library, string steam, out bool unreadable)
    {
        unreadable = false;

        var state = VdfParser.TryParseFile(manifest)?.Child("AppState");
        var appId = state?.Text("appid");
        var name = state?.Text("name")?.Trim();

        if (appId is null || string.IsNullOrEmpty(name) || !AppId().IsMatch(appId))
        {
            unreadable = true;
            return null;
        }

        if (appId == RedistributablesAppId || ToolNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var installFolder = InstallFolder(library, state!.Text("installdir"));

        return new DetectedApp
        {
            Id = $"steam:{appId}",
            Name = name,
            Source = AppSource.Steam,
            Launch = new AppLaunch(LaunchKind.Address, $"steam://rungameid/{appId}"),
            Icons = IconCandidates(steam, appId, installFolder),
            InstallFolder = installFolder,
            IsGame = true,
        };
    }

    /// <summary>A manifest names the folder, so a name with any path in it is refused rather than followed.</summary>
    private static string? InstallFolder(string library, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || name is "." or "..")
        {
            return null;
        }

        var folder = Path.Combine(library, "steamapps", "common", name);
        return Directory.Exists(folder) ? folder : null;
    }

    /// <summary>
    /// Best first. Steam keeps a full icon in its games folder, named after the small picture
    /// in the library cache. The cache has two layouts, flat with the app's number in each name
    /// and one folder per app, so both are looked at.
    /// </summary>
    private static List<IconCandidate> IconCandidates(string steam, string appId, string? installFolder)
    {
        var candidates = new List<string>();
        var cache = Path.Combine(steam, "appcache", "librarycache");
        var appCache = Path.Combine(cache, appId);
        var pictures = LocalSource.FilesIn(appCache, "*.jpg").Where(IsHashNamed).ToArray();
        var nested = LocalSource.FoldersIn(appCache).Where(IsHashNamed).ToArray();

        candidates.AddRange(pictures.Select(file => Path.Combine(steam, "steam", "games", Path.GetFileNameWithoutExtension(file) + ".ico")));
        candidates.Add(Path.Combine(appCache, "logo.png"));
        candidates.AddRange(nested.Select(folder => Path.Combine(folder, "logo.png")));
        candidates.Add(Path.Combine(cache, $"{appId}_logo.png"));

        if (installFolder is not null && LikelyProgram(installFolder) is { } program)
        {
            candidates.Add(program);
        }

        candidates.AddRange(pictures);
        candidates.Add(Path.Combine(cache, $"{appId}_icon.jpg"));
        candidates.Add(Path.Combine(appCache, "header.jpg"));
        candidates.AddRange(nested.Select(folder => Path.Combine(folder, "library_header.jpg")));
        candidates.Add(Path.Combine(cache, $"{appId}_header.jpg"));

        return candidates.Where(File.Exists).Select(path => new IconCandidate(path)).ToList();
    }

    private static bool IsHashNamed(string path) => HashName().IsMatch(Path.GetFileNameWithoutExtension(path));

    /// <summary>A game's main program is nearly always the biggest one at the top of its folder.</summary>
    private static string? LikelyProgram(string folder) => LocalSource.FilesIn(folder, "*.exe")
        .Where(file => !NotTheGame.Any(word => Path.GetFileName(file).Contains(word, StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(file => new FileInfo(file).Length)
        .FirstOrDefault();

    [GeneratedRegex("^[0-9]{1,12}$")]
    private static partial Regex AppId();

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.IgnoreCase)]
    private static partial Regex HashName();
}
