using Foldcraft.Core;

namespace Foldcraft.App.Services;

/// <summary>Command line options.</summary>
/// <param name="DataDirectory">Where to keep Foldcraft's data, or null for the default.</param>
/// <param name="CustomizePath">
/// A folder to open with. This comes from outside the app, so it's only ever a suggestion: the
/// path goes through the same safety check as a folder added by hand, and nothing is applied.
/// </param>
/// <param name="ReplacesProcess">A copy this start waits for, because it's closing to make way for this one.</param>
public sealed record AppOptions(string? DataDirectory, string? CustomizePath = null, int? ReplacesProcess = null)
{
    public const string DataDirectoryOption = "--data-dir";
    public const string ReplacesOption = "--replaces";
    private const string CustomizeOption = "--customize";

    /// <summary>
    /// Understands <c>--data-dir &lt;path&gt;</c>, <c>--customize &lt;path&gt;</c> and
    /// <c>--replaces &lt;process id&gt;</c>. Anything else is ignored.
    /// </summary>
    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? dataDirectory = null;
        string? customizePath = null;
        int? replacesProcess = null;

        for (var i = 0; i < args.Count - 1; i++)
        {
            var value = args[i + 1];

            // An option's value can't look like another option, or a missing value would swallow it.
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(args[i], DataDirectoryOption, StringComparison.OrdinalIgnoreCase))
            {
                dataDirectory ??= value;
                i++;
            }
            else if (string.Equals(args[i], CustomizeOption, StringComparison.OrdinalIgnoreCase))
            {
                customizePath ??= value;
                i++;
            }
            else if (string.Equals(args[i], ReplacesOption, StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var processId))
            {
                replacesProcess ??= processId;
                i++;
            }
        }

        return new AppOptions(dataDirectory, customizePath, replacesProcess);
    }

    /// <summary>
    /// Pins a relative data folder to where the app was started. A relative folder to customize
    /// stays as it is, because the safety check refuses it, as it would one added by hand.
    /// </summary>
    public AppOptions WithFullPaths() =>
        DataDirectory is null ? this : this with { DataDirectory = Path.GetFullPath(DataDirectory) };

    public FoldcraftPaths ResolvePaths() =>
        DataDirectory is null ? FoldcraftPaths.Default() : new FoldcraftPaths(DataDirectory);
}
