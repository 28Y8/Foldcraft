using System.Text.Json;
using System.Text.RegularExpressions;

namespace Foldcraft.Core.Apps;

/// <summary>
/// Finds Epic Games titles from the launcher's manifests in ProgramData. Starting a game through
/// the launcher is what the launcher's own shortcuts do, because many of these games refuse to
/// run without it. A program shortcut is the fallback if the manifest lacks what that needs.
/// </summary>
public sealed partial class EpicProvider(AppDiscoveryRoots roots) : IAppProvider
{
    private const int MaxManifests = 5000;
    private const long MaxManifestBytes = 1024 * 1024;

    private static readonly JsonDocumentOptions Tolerant = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 16,
    };

    public string Name => "Epic Games";

    public AppProviderResult Discover(CancellationToken cancellationToken)
    {
        var data = LocalSource.Normalize(roots.ProgramData);

        if (data is null)
        {
            return new AppProviderResult([]);
        }

        var folder = Path.Combine(data, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        var apps = new List<DetectedApp>();
        var skipped = 0;

        foreach (var manifest in LocalSource.FilesIn(folder, "*.item").Take(MaxManifests))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (Read(manifest) is { } app)
                {
                    apps.Add(app);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
            {
                skipped++;
            }
        }

        return new AppProviderResult(apps, skipped);
    }

    private static DetectedApp? Read(string path)
    {
        if (new FileInfo(path).Length > MaxManifestBytes)
        {
            throw new InvalidDataException("This is too large to be a manifest.");
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(path), Tolerant);

        var root = document.RootElement;
        var name = Text(root, "DisplayName")?.Trim();
        var appName = Text(root, "AppName");
        var folder = LocalSource.Normalize(Text(root, "InstallLocation"));

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(appName) || folder is null)
        {
            throw new InvalidDataException("This manifest is missing what it needs.");
        }

        // An unfinished download, or add-on content that belongs to another game.
        if (IsTrue(root, "bIsIncompleteInstall") ||
            (Text(root, "MainGameAppName") is { Length: > 0 } main && !main.Equals(appName, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var program = Program(folder, Text(root, "LaunchExecutable"));
        var address = LauncherAddress(Text(root, "CatalogNamespace"), Text(root, "CatalogItemId"), appName);

        if (address is null && program is null)
        {
            return null;
        }

        return new DetectedApp
        {
            Id = $"epic:{appName}",
            Name = name,
            Source = AppSource.EpicGames,
            Launch = address is not null
                ? new AppLaunch(LaunchKind.Address, address)
                : new AppLaunch(LaunchKind.Program, program!, WorkingDirectory: folder),
            Icons = program is null ? [] : [new IconCandidate(program)],
            InstallFolder = Directory.Exists(folder) ? folder : null,
            ProgramPath = program,
            IsGame = !IsTrue(root, "bIsApplication"),
        };
    }

    /// <summary>The manifest names the program, so one reaching outside the game's own folder is ignored.</summary>
    private static string? Program(string folder, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || LocalSource.Normalize(Path.Combine(folder, relative)) is not { } path)
        {
            return null;
        }

        return path.StartsWith(folder + '\\', StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    /// <summary>These parts land in a file Windows will follow, so only plain identifiers get through.</summary>
    private static string? LauncherAddress(string? space, string? item, string appName) =>
        space is not null && item is not null && Identifier().IsMatch(space) && Identifier().IsMatch(item) && Identifier().IsMatch(appName)
            ? $"com.epicgames.launcher://apps/{space}%3A{item}%3A{appName}?action=launch&silent=true"
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$")]
    private static partial Regex Identifier();
}
