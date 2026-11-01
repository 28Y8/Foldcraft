using Foldcraft.Core.Customization;
using Foldcraft.Core.Themes;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Rules;

/// <summary>What would happen to one folder if the rules ran.</summary>
public enum RulePreviewStatus
{
    /// <summary>A rule matched, and the folder would get a new look.</summary>
    WillChange,

    /// <summary>A rule matched, and the folder already has that look.</summary>
    AlreadyHasLook,

    /// <summary>Foldcraft won't touch this folder.</summary>
    Refused,

    /// <summary>A hidden or system folder, which rules leave alone.</summary>
    Hidden,

    /// <summary>The folder couldn't be looked into.</summary>
    Unreadable,

    /// <summary>A rule matched, but its theme no longer exists.</summary>
    ThemeMissing,
}

/// <summary>One line of a preview.</summary>
public sealed record RulePreviewRow
{
    public required string FolderPath { get; init; }
    public required RulePreviewStatus Status { get; init; }

    /// <summary>The rule that matched, when one did.</summary>
    public FolderRule? Rule { get; init; }

    /// <summary>The theme it would get, when there is one.</summary>
    public FolderTheme? Theme { get; init; }

    public FolderRefusal? Refusal { get; init; }

    /// <summary>A technical reason, for the log. The app words its own.</summary>
    public string? Reason { get; init; }
}

/// <summary>What a preview found. Building one changes nothing anywhere.</summary>
public sealed record RulePreview
{
    public required string ScopePath { get; init; }
    public required IReadOnlyList<RulePreviewRow> Rows { get; init; }

    /// <summary>How many folders were looked at.</summary>
    public required int FoldersScanned { get; init; }

    /// <summary>True when the scan stopped at the limit, so some folders weren't looked at.</summary>
    public required bool HitLimit { get; init; }

    /// <summary>True when the scan was stopped early, so what's here is only part of the answer.</summary>
    public bool WasCanceled { get; init; }

    /// <summary>Set when the chosen folder itself can't be scanned.</summary>
    public FolderRefusal? ScopeRefusal { get; init; }

    public string? ScopeProblem { get; init; }

    public IEnumerable<RulePreviewRow> Changes => Rows.Where(row => row.Status == RulePreviewStatus.WillChange);
}

/// <summary>
/// Works out what rules would do, without doing any of it. It only runs when asked, walks down
/// a limited number of levels and never enters a link or junction. It reads names, and for a
/// folder a rule matches, that folder's own desktop.ini to see whether it already has the look.
/// No other file in any folder is ever opened.
/// </summary>
public sealed class RuleScanner(FolderSafety safety, FolderCustomizer customizer)
{
    public const int MinDepth = 1;
    public const int MaxDepth = 5;

    /// <summary>Enough for any folder someone would really point this at, and a stop for a whole drive.</summary>
    public const int MaxFolders = 20_000;

    /// <summary>A folder with more files than this is judged on the first ones.</summary>
    private const int MaxFilesCounted = 5_000;

    private const string SettingsFileName = "desktop.ini";

    private static readonly EnumerationOptions ReadOneLevel = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// Refusals that are about changing the folder itself. Its contents may still be fine, as with
    /// a whole drive, or a user folder with Documents inside it. Every other refusal means stay out.
    /// </summary>
    private static readonly FolderRefusal[] MayLookInside =
        [FolderRefusal.DriveRoot, FolderRefusal.UserProfileRoot, FolderRefusal.ContainsProtectedFolder];

    private readonly FolderSafety _safety = safety ?? throw new ArgumentNullException(nameof(safety));
    private readonly FolderCustomizer _customizer = customizer ?? throw new ArgumentNullException(nameof(customizer));

    /// <summary>Scans the folders inside a chosen folder. The chosen folder itself is never changed.</summary>
    /// <param name="scope">The folder to look inside.</param>
    /// <param name="depth">How many levels to go down, from <see cref="MinDepth"/> to <see cref="MaxDepth"/>.</param>
    /// <param name="rules">The rules, in order. The first match wins.</param>
    /// <param name="findTheme">Finds a theme by id.</param>
    /// <param name="cancellationToken">Stops the scan early. What was found so far is returned.</param>
    public RulePreview Preview(
        string scope,
        int depth,
        IReadOnlyList<FolderRule> rules,
        Func<string, FolderTheme?> findTheme,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(findTheme);

        var verdict = _safety.Check(scope);
        var start = verdict.ResolvedPath ?? verdict.NormalizedPath;

        if (start is null || (verdict.Refusal is { } refusal && !MayLookInside.Contains(refusal)))
        {
            return new RulePreview
            {
                ScopePath = scope,
                Rows = [],
                FoldersScanned = 0,
                HitLimit = false,
                ScopeRefusal = verdict.Refusal,
                ScopeProblem = verdict.Reason,
            };
        }

        var levels = Math.Clamp(depth, MinDepth, MaxDepth);
        var rows = new List<RulePreviewRow>();
        var pending = new Queue<(string Path, int Level)>();
        var scanned = 0;
        var hitLimit = false;
        var canceled = false;

        pending.Enqueue((start, 0));

        while (pending.TryDequeue(out var current))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            foreach (var child in SubfoldersOf(current.Path, rows))
            {
                if (scanned >= MaxFolders)
                {
                    hitLimit = true;
                    break;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                scanned++;

                if (Examine(child, rules, findTheme, rows) is { } enter && current.Level + 1 < levels)
                {
                    pending.Enqueue((enter, current.Level + 1));
                }
            }
        }

        return new RulePreview { ScopePath = start, Rows = rows, FoldersScanned = scanned, HitLimit = hitLimit, WasCanceled = canceled };
    }

    /// <summary>Adds a folder's row, if it needs one. Returns the path to look inside, or null to stay out.</summary>
    private string? Examine(
        DirectoryInfo child,
        IReadOnlyList<FolderRule> rules,
        Func<string, FolderTheme?> findTheme,
        List<RulePreviewRow> rows)
    {
        var path = child.FullName;

        if ((child.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
        {
            rows.Add(new RulePreviewRow { FolderPath = path, Status = RulePreviewStatus.Hidden });
            return null;
        }

        var verdict = _safety.Check(path);

        if (!verdict.IsAllowed)
        {
            rows.Add(new RulePreviewRow
            {
                FolderPath = path,
                Status = RulePreviewStatus.Refused,
                Refusal = verdict.Refusal,
                Reason = verdict.Reason,
            });

            return MayLookInside.Contains(verdict.Refusal!.Value) ? verdict.NormalizedPath : null;
        }

        var folder = verdict.ResolvedPath!;

        if (FirstMatch(folder, rules) is { } rule)
        {
            var theme = findTheme(rule.ThemeId);

            rows.Add(new RulePreviewRow
            {
                FolderPath = folder,
                Rule = rule,
                Theme = theme,
                Status = theme is null ? RulePreviewStatus.ThemeMissing
                    : _customizer.AlreadyShows(folder, theme.Spec) ? RulePreviewStatus.AlreadyHasLook
                    : RulePreviewStatus.WillChange,
            });
        }

        return folder;
    }

    private static FolderRule? FirstMatch(string folder, IReadOnlyList<FolderRule> rules)
    {
        var name = Path.GetFileName(folder);
        string[]? files = null;

        foreach (var rule in rules)
        {
            if (rule.Condition == RuleCondition.MostlyFileType)
            {
                files ??= FileNamesIn(folder);

                if (RuleMatcher.IsMostly(files, rule.Pattern))
                {
                    return rule;
                }
            }
            else if (RuleMatcher.MatchesName(rule, name))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>Names only. Foldcraft's own settings file isn't part of what a folder holds.</summary>
    private static string[] FileNamesIn(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", ReadOneLevel)
                .Select(file => Path.GetFileName(file)!)
                .Where(name => !name.Equals(SettingsFileName, StringComparison.OrdinalIgnoreCase))
                .Take(MaxFilesCounted)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static DirectoryInfo[] SubfoldersOf(string folder, List<RulePreviewRow> rows)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateDirectories("*", ReadOneLevel).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            rows.Add(new RulePreviewRow { FolderPath = folder, Status = RulePreviewStatus.Unreadable, Reason = ex.Message });
            return [];
        }
    }
}
