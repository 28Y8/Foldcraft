using Foldcraft.Core.Icons;

namespace Foldcraft.Core.Themes;

/// <summary>A named look.</summary>
public sealed record FolderTheme
{
    /// <summary>A stable id, which rules use to name their look.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The group it's shown under, such as Calm or Bright.</summary>
    public required string Mood { get; init; }

    public required FolderIconSpec Spec { get; init; }

    /// <summary>Built-in themes come with the app and can't be deleted.</summary>
    public bool IsBuiltIn { get; init; }
}
