using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Apps;

/// <summary>
/// Finds everything else from Start Menu shortcuts, for this user and for all users. There's no
/// list of known products here on purpose: anything installed the normal way gets found.
/// </summary>
public sealed class StartMenuProvider(AppDiscoveryRoots roots, IShortcuts shortcuts) : IAppProvider
{
    private const int MaxDepth = 6;
    private const int MaxShortcuts = 5000;

    public string Name => "Start Menu";

    public AppProviderResult Discover(CancellationToken cancellationToken)
    {
        var apps = new List<DetectedApp>();
        var skipped = 0;
        var seen = 0;

        foreach (var menu in roots.StartMenuFolders)
        {
            if (LocalSource.Normalize(menu) is not { } root || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var link in Links(root, 0))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (++seen > MaxShortcuts)
                {
                    return new AppProviderResult(Preferred(apps), skipped);
                }

                var info = shortcuts.Read(link);

                if (info is null)
                {
                    skipped++;
                }
                else if (ToApp(link, info) is { } app)
                {
                    apps.Add(app);
                }
            }
        }

        return new AppProviderResult(Preferred(apps), skipped);
    }

    private IEnumerable<string> Links(string folder, int depth)
    {
        foreach (var link in LocalSource.FilesIn(folder, "*.lnk"))
        {
            yield return link;
        }

        if (depth >= MaxDepth)
        {
            yield break;
        }

        foreach (var child in LocalSource.FoldersIn(folder).Where(child => !ShortcutFilter.IsWindowsFolder(Path.GetFileName(child))))
        {
            foreach (var link in Links(child, depth + 1))
            {
                yield return link;
            }
        }
    }

    private DetectedApp? ToApp(string link, ShortcutInfo info)
    {
        var name = Path.GetFileNameWithoutExtension(link).Trim();

        if (name.Length == 0 || ShortcutFilter.IsNotAnApp(name))
        {
            return null;
        }

        var target = LocalSource.Normalize(info.Target);

        if (target is null)
        {
            return null;
        }

        var windows = LocalSource.Normalize(roots.WindowsDirectory);
        var iconSource = LocalSource.Normalize(info.IconPath);

        // A Windows Installer shortcut points at a stored icon. Windows knows what it really starts.
        if (windows is not null && PathResolver.IsStrictlyUnder(target, Path.Combine(windows, "Installer")))
        {
            return new DetectedApp
            {
                Id = IdFor(link),
                Name = name,
                Source = AppSource.StartMenu,
                Launch = new AppLaunch(LaunchKind.OtherShortcut, link),
                Icons = iconSource is null ? [new IconCandidate(link)] : [new IconCandidate(iconSource, info.IconIndex), new IconCandidate(link)],
            };
        }

        if (windows is not null && PathResolver.IsSameOrUnder(target, windows))
        {
            return null;
        }

        if (!File.Exists(target) || ShortcutFilter.IsNotAProgram(target))
        {
            return null;
        }

        var icons = new List<IconCandidate>();

        if (iconSource is not null && File.Exists(iconSource))
        {
            icons.Add(new IconCandidate(iconSource, info.IconIndex));
        }

        icons.Add(new IconCandidate(target));

        return new DetectedApp
        {
            Id = IdFor(link),
            Name = name,
            Source = AppSource.StartMenu,
            Launch = new AppLaunch(LaunchKind.Program, target, info.Arguments, LocalSource.Normalize(info.WorkingDirectory)),
            Icons = icons,
            ProgramPath = target,
        };
    }

    /// <summary>
    /// Several shortcuts can start one program, such as a browser's private window. The one
    /// with the fewest extras is the app itself, so it goes first and wins.
    /// </summary>
    private static List<DetectedApp> Preferred(IEnumerable<DetectedApp> apps) =>
        apps.OrderBy(app => app.Launch.Arguments?.Length ?? 0).ToList();

    /// <summary>Stable for one shortcut, without keeping its path, which holds the user's name.</summary>
    private static string IdFor(string link) =>
        "menu:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(link.ToUpperInvariant())))[..16];
}

/// <summary>Tells apps from everything else a Start Menu holds.</summary>
public static partial class ShortcutFilter
{
    private static readonly HashSet<string> WindowsFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accessibility", "Accessories", "Administrative Tools", "Maintenance", "Startup", "System Tools",
        "Windows Accessories", "Windows Administrative Tools", "Windows Ease of Access", "Windows PowerShell",
        "Windows System", "Windows Tools",
    };

    private static readonly HashSet<string> ProgramExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe" };

    /// <summary>True for a Start Menu folder that only holds parts of Windows.</summary>
    public static bool IsWindowsFolder(string name) => WindowsFolders.Contains(name);

    /// <summary>True for an uninstaller, documentation or the like, going by the shortcut's name.</summary>
    public static bool IsNotAnApp(string name) => NotAnAppName().IsMatch(name);

    /// <summary>True when the target isn't a program, or is an installer or a part of Windows.</summary>
    public static bool IsNotAProgram(string target) =>
        !ProgramExtensions.Contains(Path.GetExtension(target)) ||
        NotAProgramFile().IsMatch(Path.GetFileName(target)) ||
        PartOfWindows().IsMatch(target);

    [GeneratedRegex(
        @"\b(uninstall(er)?|uninst|deinstall|remove|repair|modify|readme|read me|help|documentation|manual|guide|licen[cs]e|eula|changelog|change log|release notes|revision history|what'?s new|what is new|faq|website|web site|homepage|home page|support|report a problem|command prompt|example scripts?)\b|^(install|setup|update|updater)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex NotAnAppName();

    /// <summary>Update.exe isn't listed: some apps really do start through a launcher with that name.</summary>
    [GeneratedRegex(@"^(unins.*|uninstall.*|setup|install|installer|msiexec|bugreporter|crashreporter)\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex NotAProgramFile();

    [GeneratedRegex(@"\\Program Files( \(x86\))?\\(Windows |Internet Explorer\\)", RegexOptions.IgnoreCase)]
    private static partial Regex PartOfWindows();
}
