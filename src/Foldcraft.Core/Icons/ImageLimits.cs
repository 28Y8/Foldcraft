namespace Foldcraft.Core.Icons;

/// <summary>What Foldcraft will accept as a custom image.</summary>
public sealed record ImageLimits
{
    public long MaxFileBytes { get; init; } = 32 * 1024 * 1024;
    public int MaxDimension { get; init; } = 12_000;
    public long MaxPixels { get; init; } = 64_000_000;

    public static ImageLimits Default { get; } = new();
}
