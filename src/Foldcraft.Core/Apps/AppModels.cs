namespace Foldcraft.Core.Apps;

/// <summary>Where an app was found.</summary>
public enum AppSource
{
    Steam,
    Roblox,
    EpicGames,
    StartMenu,
}

/// <summary>How a generated folder's shortcut starts an app.</summary>
public enum LaunchKind
{
    /// <summary>A .lnk straight to a program.</summary>
    Program,

    /// <summary>A .url with a launcher's own address, such as steam://rungameid/.</summary>
    Address,

    /// <summary>
    /// A .lnk that opens another shortcut through the shell. This is for software whose program
    /// moves on every update, or that has no fixed program at all. Its own Start Menu shortcut
    /// is kept current by its installer, so that's the thing to point at.
    /// </summary>
    OtherShortcut,
}

/// <summary>What a shortcut has to start.</summary>
/// <param name="Kind">How.</param>
/// <param name="Target">A program, an address or a shortcut, depending on the kind.</param>
/// <param name="Arguments">Command line arguments, if any.</param>
/// <param name="WorkingDirectory">Where it starts, if that matters.</param>
public sealed record AppLaunch(LaunchKind Kind, string Target, string? Arguments = null, string? WorkingDirectory = null);

/// <summary>A place an app's icon might be. Candidates are tried in order.</summary>
/// <param name="Path">A program, library, icon or picture on this PC.</param>
/// <param name="Index">Which icon in a program or library. Negative means a resource ID, as in a shortcut.</param>
public sealed record IconCandidate(string Path, int Index = 0);

/// <summary>Something installed that could get a folder of its own.</summary>
public sealed record DetectedApp
{
    /// <summary>Stable across scans, such as steam:440.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required AppSource Source { get; init; }

    public required AppLaunch Launch { get; init; }

    /// <summary>Where to look for its icon, best first.</summary>
    public IReadOnlyList<IconCandidate> Icons { get; init; } = [];

    /// <summary>The app's own folder, if known. It's only ever customized when the user asks.</summary>
    public string? InstallFolder { get; init; }

    /// <summary>The program behind it, used to notice one app found two ways.</summary>
    public string? ProgramPath { get; init; }

    /// <summary>True for a game, which only affects the symbol used if no icon can be read.</summary>
    public bool IsGame { get; init; }
}

/// <summary>A source of apps that couldn't be read, for the log and the summary.</summary>
public sealed record AppProviderProblem(string Provider, string Message);

/// <summary>What one provider found, and how many of its files it had to skip.</summary>
public sealed record AppProviderResult(IReadOnlyList<DetectedApp> Apps, int SkippedFiles = 0);

/// <summary>What a whole scan found.</summary>
public sealed record AppScanReport
{
    public required IReadOnlyList<DetectedApp> Apps { get; init; }

    /// <summary>Providers that failed. The others still ran.</summary>
    public IReadOnlyList<AppProviderProblem> Problems { get; init; } = [];

    /// <summary>Files that couldn't be understood and were passed over.</summary>
    public int SkippedFiles { get; init; }

    public bool WasCanceled { get; init; }

    /// <summary>True when more was found than one screen can sensibly hold.</summary>
    public bool HitLimit { get; init; }
}

/// <summary>One way of finding apps. Each is read-only and knows nothing of the others.</summary>
public interface IAppProvider
{
    /// <summary>A short name for the log.</summary>
    string Name { get; }

    /// <summary>
    /// Looks for apps. A malformed file is counted and skipped. Anything worse may throw:
    /// <see cref="AppDiscovery"/> keeps one provider's failure from reaching the others.
    /// </summary>
    AppProviderResult Discover(CancellationToken cancellationToken);
}
