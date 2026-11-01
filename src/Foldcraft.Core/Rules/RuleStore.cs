using System.Text.Json;
using System.Text.Json.Serialization;
using Foldcraft.Core.Infrastructure;

namespace Foldcraft.Core.Rules;

/// <summary>
/// The rules set up on this PC, in order, because the first match wins. A file that can't be
/// read is set aside rather than overwritten. Rules are only data: nothing here runs them.
/// </summary>
public sealed class RuleStore
{
    private const int CurrentVersion = 1;
    private const int MaxFileBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<RuleCondition>() },
    };

    private readonly FoldcraftPaths _paths;
    private readonly FileOperations _ops;
    private readonly Lock _gate = new();

    public RuleStore(FoldcraftPaths paths, FileOperations? operations = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _ops = operations ?? new FileOperations();
    }

    /// <summary>A note about the saved rules, when something went wrong reading them.</summary>
    public string? LoadProblem { get; private set; }

    /// <summary>Reads the saved rules. Anything that isn't a sound rule file reads as none.</summary>
    public IReadOnlyList<FolderRule> Load()
    {
        lock (_gate)
        {
            var path = _paths.RulesFile;

            if (!File.Exists(path))
            {
                return [];
            }

            try
            {
                if (new FileInfo(path).Length <= MaxFileBytes &&
                    JsonSerializer.Deserialize<RuleDocument>(File.ReadAllBytes(path), JsonOptions) is { Version: CurrentVersion, Rules: not null } document)
                {
                    return document.Rules
                        .Where(rule => rule is not null && RuleMatcher.IsUsable(rule.Condition, rule.Pattern) && !string.IsNullOrEmpty(rule.ThemeId))
                        .ToArray();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Falls through to setting the file aside.
            }

            LoadProblem = "Your saved rules couldn't be read, so that file was set aside and left in the data folder.";
            SetAside(path);
            return [];
        }
    }

    public void Save(IEnumerable<FolderRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(new RuleDocument(CurrentVersion, rules.ToArray()), JsonOptions);

        lock (_gate)
        {
            Directory.CreateDirectory(_paths.DataDirectory);
            AtomicFile.Write(_paths.RulesFile, bytes, FileAttributes.Normal, _ops);
        }
    }

    private static void SetAside(string path)
    {
        try
        {
            File.Move(path, $"{path}.{DateTime.UtcNow:yyyyMMddHHmmss}.unreadable");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If it can't be moved, the next save can't replace it either, and that fails visibly.
        }
    }

    private sealed record RuleDocument(int Version, FolderRule[]? Rules);
}
