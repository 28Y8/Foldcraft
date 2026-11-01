using System.Drawing;
using System.Drawing.Imaging;

namespace Foldcraft.Core.Icons;

/// <summary>Turns a <see cref="FolderIconSpec"/> into pixels and .ico files.</summary>
public sealed class IconRenderer(ImageLimits? limits = null)
{
    /// <summary>
    /// Raise this when the artwork changes, so new renders get new file names rather than
    /// disagreeing with icons already referenced by folders. Version 3 gave glyphs their own
    /// drawings at 16 and 20 px. Version 4 redrew the folder at the proportions of the stock
    /// Windows 11 folder, lit at every size. Repair can bring older folders up to date.
    /// </summary>
    internal const int Version = 4;

    /// <summary>
    /// Every size in a finished icon, largest first. 20, 40 and 96 are the 16, 32 and 48 px
    /// slots at 125% and 200% display scaling, which Windows would otherwise resample.
    /// </summary>
    public static IReadOnlyList<int> Sizes { get; } = [256, 96, 64, 48, 40, 32, 24, 20, 16];

    public ImageLimits Limits { get; } = limits ?? ImageLimits.Default;

    /// <summary>Renders one size to PNG for the live preview. Nothing is written to disk.</summary>
    /// <exception cref="IconRenderException">The spec or its image cannot be used.</exception>
    public byte[] RenderPreviewPng(FolderIconSpec spec, int size)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 256);
        spec.Validate();

        using var image = LoadImage(spec);
        using var frame = FolderArtist.Draw(spec, size, image);
        using var stream = new MemoryStream();
        frame.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>
    /// Renders one size around an image held in memory, for art that hasn't been saved. Nothing
    /// is read from or written to disk, so the spec's picture path only has to be present.
    /// </summary>
    /// <exception cref="IconRenderException">The spec or its image cannot be used.</exception>
    public byte[] RenderPreviewPng(FolderIconSpec spec, int size, byte[] image)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 256);
        spec.Validate();

        using var decoded = CustomImageLoader.Decode(image, Limits);
        using var frame = FolderArtist.Draw(spec, size, decoded);
        using var stream = new MemoryStream();
        frame.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>Renders a complete .ico with every size in <see cref="Sizes"/>.</summary>
    /// <exception cref="IconRenderException">The spec or its image cannot be used.</exception>
    public byte[] RenderIco(FolderIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        spec.Validate();

        using var image = LoadImage(spec);
        return RenderIco(spec, image);
    }

    /// <summary>The icon's identity and bytes, from a single read of any custom image.</summary>
    internal (string Key, byte[] Ico, string? ImageSha256) RenderWithKey(FolderIconSpec spec)
    {
        spec.Validate();

        if (spec.CustomImagePath is null)
        {
            return (spec.CanonicalKey(null), RenderIco(spec, null), null);
        }

        var bytes = CustomImageLoader.ReadBytes(spec.CustomImagePath, Limits);
        using var image = CustomImageLoader.Decode(bytes, Limits);
        var hash = CustomImageLoader.Hash(bytes);
        return (spec.CanonicalKey(hash), RenderIco(spec, image), hash);
    }

    /// <summary>The icon's identity, without drawing anything. A custom image is still read, for its hash.</summary>
    internal (string Key, string? ImageSha256) KeyFor(FolderIconSpec spec)
    {
        spec.Validate();

        if (spec.CustomImagePath is null)
        {
            return (spec.CanonicalKey(null), null);
        }

        var hash = CustomImageLoader.Hash(CustomImageLoader.ReadBytes(spec.CustomImagePath, Limits));
        return (spec.CanonicalKey(hash), hash);
    }

    private static byte[] RenderIco(FolderIconSpec spec, Bitmap? image)
    {
        var frames = new List<Bitmap>(Sizes.Count);

        try
        {
            foreach (var size in Sizes)
            {
                frames.Add(FolderArtist.Draw(spec, size, image));
            }

            return IcoFile.Write(frames);
        }
        finally
        {
            frames.ForEach(frame => frame.Dispose());
        }
    }

    private Bitmap? LoadImage(FolderIconSpec spec) => spec.CustomImagePath is null
        ? null
        : CustomImageLoader.Decode(CustomImageLoader.ReadBytes(spec.CustomImagePath, Limits), Limits);
}
