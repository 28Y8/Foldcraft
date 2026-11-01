namespace Foldcraft.Core.Apps;

/// <summary>
/// Finds Roblox in its per-user install. It keeps each version in a folder of its own, and its
/// updater replaces those folders, so a shortcut straight to the program would break within
/// days. Its own Start Menu shortcuts are kept current, so those are what gets started.
/// </summary>
public sealed class RobloxProvider(AppDiscoveryRoots roots, IShortcuts shortcuts) : IAppProvider
{
    private static readonly (string Id, string Name, string Program, string[] Links, bool IsGame)[] Products =
    [
        ("roblox:player", "Roblox", "RobloxPlayerBeta.exe", ["Roblox Player.lnk", "Roblox.lnk"], true),
        ("roblox:studio", "Roblox Studio", "RobloxStudioBeta.exe", ["Roblox Studio.lnk"], false),
    ];

    public string Name => "Roblox";

    public AppProviderResult Discover(CancellationToken cancellationToken)
    {
        var versions = LocalSource.Normalize(roots.LocalAppData) is { } local ? Path.Combine(local, "Roblox", "Versions") : null;

        if (versions is null || !Directory.Exists(versions))
        {
            return new AppProviderResult([]);
        }

        var folders = LocalSource.FoldersIn(versions)
            .Where(folder => Path.GetFileName(folder).StartsWith("version-", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var apps = new List<DetectedApp>();

        foreach (var product in Products)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var program = folders
                .Select(folder => Path.Combine(folder, product.Program))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (program is null)
            {
                continue;
            }

            var link = FindLink(product.Links);

            apps.Add(new DetectedApp
            {
                Id = product.Id,
                Name = product.Name,
                Source = AppSource.Roblox,
                Launch = link is null
                    ? new AppLaunch(LaunchKind.Program, program, WorkingDirectory: Path.GetDirectoryName(program))
                    : new AppLaunch(LaunchKind.OtherShortcut, link),
                Icons = [new IconCandidate(program)],
                ProgramPath = program,
                IsGame = product.IsGame,
            });
        }

        return new AppProviderResult(apps);
    }

    /// <summary>A shortcut that works today, wherever the installer put it.</summary>
    private string? FindLink(string[] names)
    {
        foreach (var menu in roots.StartMenuFolders)
        {
            foreach (var name in names)
            {
                foreach (var folder in new[] { Path.Combine(menu, "Roblox"), menu })
                {
                    var link = Path.Combine(folder, name);

                    if (File.Exists(link) && shortcuts.Read(link) is { } info &&
                        LocalSource.Normalize(info.Target) is { } target && File.Exists(target))
                    {
                        return link;
                    }
                }
            }
        }

        return null;
    }
}
