using System.Text.RegularExpressions;

namespace Foldcraft.Core.Updates;

/// <summary>Where releases live, and how the installed app is laid out on disk.</summary>
public static partial class ReleaseSite
{
    /// <summary>
    /// The installer's id, which is also the name of the folder the app installs to under
    /// %LOCALAPPDATA%. Uninstalling empties that folder and an update replaces what's in it, so
    /// this must never be "Foldcraft": that's the data folder, which holds the icons customized
    /// folders depend on and the undo history. Directory.Build.props holds the same value for
    /// the packaging script, and a test keeps the two together.
    /// </summary>
    public const string PackageId = "FoldcraftApp";

    /// <summary>Always HTTPS.</summary>
    public const string RepositoryUrl = "https://github.com/28Y8/Foldcraft";

    public const string ReleasesPage = RepositoryUrl + "/releases";

    /// <summary>The page for one release. Null when the text isn't a plain version, so a bad feed can't choose the link.</summary>
    public static string? ReleaseNotesPage(string version) =>
        PlainVersion().IsMatch(version) ? $"{ReleasesPage}/tag/v{version}" : null;

    /// <summary>The folder the installer uses for this user.</summary>
    /// <param name="localAppData">%LOCALAPPDATA%.</param>
    public static string InstallRoot(string localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);
        return Path.Combine(localAppData, PackageId);
    }

    [GeneratedRegex(@"^\d{1,6}\.\d{1,6}\.\d{1,6}$")]
    private static partial Regex PlainVersion();
}
