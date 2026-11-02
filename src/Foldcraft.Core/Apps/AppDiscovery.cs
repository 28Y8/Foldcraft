using Foldcraft.Core.Shell;

namespace Foldcraft.Core.Apps;

/// <summary>
/// Where to look for installed software. Tests fill this with fixture folders and the app
/// fills it with <see cref="Detect"/>, so only the real app ever looks at the real PC.
/// </summary>
public sealed record AppDiscoveryRoots
{
    /// <summary>Only ever read, to find Steam. Null means no Steam.</summary>
    public IUserRegistry? Registry { get; init; }

    /// <summary>The per-user and all-users Start Menu program folders.</summary>
    public IReadOnlyList<string> StartMenuFolders { get; init; } = [];

    public string? LocalAppData { get; init; }

    public string? ProgramData { get; init; }

    /// <summary>The Windows folder. Shortcuts into it are parts of Windows, not apps.</summary>
    public string? WindowsDirectory { get; init; }

    /// <summary>Nowhere at all, so a scan finds nothing.</summary>
    public static AppDiscoveryRoots None { get; } = new();

    public static AppDiscoveryRoots Detect(IUserRegistry registry) => new()
    {
        Registry = registry,
        StartMenuFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolderOption.DoNotVerify),
        }.Where(path => path.Length > 0).ToArray(),
        LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        WindowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows, Environment.SpecialFolderOption.DoNotVerify),
    };
}

/// <summary>
/// Runs every provider and merges what they found. This only reads: it writes nothing and
/// starts nothing. One provider failing costs that provider's results and no more.
/// </summary>
public sealed class AppDiscovery
{
    /// <summary>A grid of more than this would be slower than it is useful.</summary>
    public const int MaxApps = 1000;

    private readonly IReadOnlyList<IAppProvider> _providers;

    /// <param name="providers">Earlier providers win when two find the same app.</param>
    public AppDiscovery(IReadOnlyList<IAppProvider> providers)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    /// <summary>The usual providers, most specific first, so a game is known as a game before it's seen as a shortcut.</summary>
    public static AppDiscovery For(AppDiscoveryRoots roots, IShortcuts shortcuts)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(shortcuts);

        return new AppDiscovery(
        [
            new SteamProvider(roots),
            new RobloxProvider(roots, shortcuts),
            new EpicProvider(roots),
            new StartMenuProvider(roots, shortcuts),
        ]);
    }

    /// <summary>Scans. This never throws: canceling or failing gives a report saying so.</summary>
    public AppScanReport Scan(CancellationToken cancellationToken = default)
    {
        var found = new List<DetectedApp>();
        var problems = new List<AppProviderProblem>();
        var skipped = 0;
        var canceled = false;

        foreach (var provider in _providers)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = provider.Discover(cancellationToken);

                found.AddRange(result.Apps);
                skipped += result.SkippedFiles;
            }
            catch (OperationCanceledException)
            {
                canceled = true;
                break;
            }
            catch (Exception ex)
            {
                // Deliberately broad: whatever a launcher's files do, the other sources still run.
                problems.Add(new AppProviderProblem(provider.Name, ex.Message));
            }
        }

        var unique = RemoveDuplicates(found);

        unique.AddRange(AppCompanies.For(unique).ToArray());

        return new AppScanReport
        {
            Apps = unique.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxApps).ToArray(),
            Problems = problems,
            SkippedFiles = skipped,
            WasCanceled = canceled,
            HitLimit = unique.Count > MaxApps,
        };
    }

    /// <summary>Two finds are one app if they run the same program or carry the same name. The first one stays.</summary>
    private static List<DetectedApp> RemoveDuplicates(IEnumerable<DetectedApp> apps)
    {
        var programs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<DetectedApp>();

        foreach (var app in apps)
        {
            var name = app.Name.Trim();

            if (app.ProgramPath is { } known && programs.Contains(known))
            {
                continue;
            }

            if (!names.Add(name) || !ids.Add(app.Id))
            {
                continue;
            }

            if (app.ProgramPath is { } program)
            {
                programs.Add(program);
            }

            unique.Add(app);
        }

        return unique;
    }
}
