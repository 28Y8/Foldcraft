namespace Foldcraft.Core.Rules;

/// <summary>How a rule decides which folders it's about. The names are stored, so they must not change.</summary>
public enum RuleCondition
{
    NameContains,
    NameEquals,
    NameStartsWith,

    /// <summary>A name pattern where * stands for any text and ? for any one character.</summary>
    NameWildcard,

    /// <summary>More than half of the files directly inside are of one kind.</summary>
    MostlyFileType,
}

/// <summary>"If a folder matches this, use that look." Rules only ever run when the user presses a button.</summary>
public sealed record FolderRule
{
    public required Guid Id { get; init; }
    public required RuleCondition Condition { get; init; }

    /// <summary>The text to match, or for <see cref="RuleCondition.MostlyFileType"/>, an id from <see cref="FileKinds"/> or an extension.</summary>
    public required string Pattern { get; init; }

    /// <summary>The theme to use, by id.</summary>
    public required string ThemeId { get; init; }
}

/// <summary>The groups of file types a rule can ask about.</summary>
public static class FileKinds
{
    private static readonly (string Id, string Name, string[] Extensions)[] Kinds =
    [
        ("pictures", "Pictures", [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".raw", ".svg"]),
        ("music", "Music", [".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".opus"]),
        ("videos", "Videos", [".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v"]),
        ("documents", "Documents", [".pdf", ".doc", ".docx", ".txt", ".rtf", ".odt", ".md", ".xls", ".xlsx", ".ppt", ".pptx", ".csv"]),
        ("code", "Code", [".cs", ".js", ".ts", ".py", ".java", ".c", ".cpp", ".h", ".go", ".rs", ".rb", ".php", ".html", ".css", ".json", ".xml"]),
        ("archives", "Archives", [".zip", ".7z", ".rar", ".tar", ".gz", ".iso"]),
    ];

    /// <summary>Every group's id and name.</summary>
    public static IReadOnlyList<(string Id, string Name)> All { get; } = Kinds.Select(kind => (kind.Id, kind.Name)).ToArray();

    /// <summary>True when the pattern names a group or looks like one extension, such as ".psd".</summary>
    public static bool IsValidPattern(string pattern) =>
        Array.Exists(Kinds, kind => kind.Id.Equals(pattern, StringComparison.OrdinalIgnoreCase)) ||
        (pattern.Length is > 1 and <= 16 && pattern[0] == '.' && pattern.Skip(1).All(char.IsAsciiLetterOrDigit));

    /// <summary>True when a file name is of the kind a pattern asks for.</summary>
    public static bool Matches(string fileName, string pattern)
    {
        var extension = Path.GetExtension(fileName);

        foreach (var kind in Kinds)
        {
            if (kind.Id.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return kind.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
            }
        }

        return extension.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The matching itself, on plain values, so none of it needs a disk.</summary>
public static class RuleMatcher
{
    public const int MaxPatternLength = 100;

    /// <summary>True when a rule could ever match anything sensible. This also guards rules read from a file.</summary>
    public static bool IsUsable(RuleCondition condition, string? pattern)
    {
        if (!Enum.IsDefined(condition) || string.IsNullOrWhiteSpace(pattern) ||
            pattern.Length > MaxPatternLength || pattern.Any(char.IsControl))
        {
            return false;
        }

        return condition != RuleCondition.MostlyFileType || FileKinds.IsValidPattern(pattern);
    }

    /// <summary>True when a folder's name fits a rule. A file-type rule isn't about the name, so it's false here.</summary>
    public static bool MatchesName(FolderRule rule, string folderName)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(folderName);

        return rule.Condition switch
        {
            RuleCondition.NameContains => folderName.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase),
            RuleCondition.NameEquals => folderName.Equals(rule.Pattern, StringComparison.OrdinalIgnoreCase),
            RuleCondition.NameStartsWith => folderName.StartsWith(rule.Pattern, StringComparison.OrdinalIgnoreCase),
            RuleCondition.NameWildcard => MatchesWildcard(folderName, rule.Pattern),
            _ => false,
        };
    }

    /// <summary>True when more than half of the files are of the kind asked for. No files is never "mostly".</summary>
    public static bool IsMostly(IReadOnlyCollection<string> fileNames, string pattern)
    {
        ArgumentNullException.ThrowIfNull(fileNames);

        return fileNames.Count > 0 && fileNames.Count(name => FileKinds.Matches(name, pattern)) * 2 > fileNames.Count;
    }

    /// <summary>
    /// A hand-written matcher rather than a regular expression, because this pattern is typed
    /// text. It backtracks only to the last star, so no pattern can make it take long.
    /// </summary>
    public static bool MatchesWildcard(string text, string pattern)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pattern);

        var (t, p, star, resume) = (0, 0, -1, 0);

        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = t;
            }
            else if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])))
            {
                p++;
                t++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++resume;
            }
            else
            {
                return false;
            }
        }

        return pattern.AsSpan(p).TrimStart('*').IsEmpty;
    }
}
