using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Infrastructure;

namespace Foldcraft.Core.Apps;

/// <summary>How well a picture will hold up at the largest icon size.</summary>
public enum ArtQuality
{
    /// <summary>Enough pixels to look sharp.</summary>
    Good,

    /// <summary>Too few pixels for a large folder. It's drawn smaller, and the user is told.</summary>
    Low,
}

/// <summary>A picture read from software on this PC, held in memory until a folder needs it.</summary>
/// <param name="Png">The picture, cropped to its visible part.</param>
/// <param name="Width">Its real width in pixels. It's never been enlarged.</param>
/// <param name="Height">Its real height in pixels.</param>
/// <param name="SourcePath">Where it came from.</param>
public sealed record AppArt(byte[] Png, int Width, int Height, string SourcePath)
{
    /// <summary>Below this a logo can't fill the folder without going soft.</summary>
    public const int LowQualitySize = 64;

    public ArtQuality Quality => Math.Max(Width, Height) < LowQualitySize ? ArtQuality.Low : ArtQuality.Good;
}

/// <summary>Turns a decoded image into <see cref="AppArt"/>.</summary>
public static class AppArtMaker
{
    /// <summary>The largest size stored. Icons stop at 256 px, so more would only cost disk.</summary>
    public const int MaxSize = 256;

    private const byte VisibleAlpha = 12;

    /// <summary>
    /// Crops the empty margin, so every logo sits the same size however its icon was padded, and
    /// shrinks anything huge. It never enlarges. Null if nothing in the image is visible.
    /// </summary>
    public static AppArt? From(Bitmap image, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (VisibleBounds(image) is not { } bounds)
        {
            return null;
        }

        var fit = Math.Min(1f, (float)MaxSize / Math.Max(bounds.Width, bounds.Height));
        var width = Math.Max(1, (int)MathF.Round(bounds.Width * fit));
        var height = Math.Max(1, (int)MathF.Round(bounds.Height * fit));

        using var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(result))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(image, new Rectangle(0, 0, width, height), bounds, GraphicsUnit.Pixel);
        }

        using var stream = new MemoryStream();
        result.Save(stream, ImageFormat.Png);

        return new AppArt(stream.ToArray(), width, height, sourcePath);
    }

    private static Rectangle? VisibleBounds(Bitmap image)
    {
        var (left, top, right, bottom) = (image.Width, image.Height, -1, -1);
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var row = new int[image.Width];

            for (var y = 0; y < image.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, row.Length);

                for (var x = 0; x < row.Length; x++)
                {
                    if ((byte)(row[x] >> 24) >= VisibleAlpha)
                    {
                        (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
                    }
                }
            }
        }
        finally
        {
            image.UnlockBits(data);
        }

        return right < 0 ? null : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
}

/// <summary>
/// Keeps art for generated folders in the data folder, and nowhere else. A file's name is the
/// hash of its content, so saving the same picture twice is one file, and the look recorded in
/// the journal can always be drawn again by Repair. Nothing from here is ever uploaded.
/// </summary>
public sealed class AppArtStore
{
    private readonly FoldcraftPaths _paths;
    private readonly FileOperations _ops;

    public AppArtStore(FoldcraftPaths paths, FileOperations? operations = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _ops = operations ?? new FileOperations();
    }

    /// <summary>Saves a picture and returns its path.</summary>
    public string Save(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);

        Directory.CreateDirectory(_paths.AppArtDirectory);

        var path = Path.Combine(_paths.AppArtDirectory, $"{Convert.ToHexStringLower(SHA256.HashData(png))[..32]}.png");

        if (!File.Exists(path))
        {
            AtomicFile.Write(path, png, FileAttributes.Normal, _ops);
        }

        return path;
    }
}
