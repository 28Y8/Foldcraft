using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Foldcraft.Core.Icons;

/// <summary>
/// Reads an untrusted image file. The file is read once into memory, so the hash and the
/// pixels always describe the same bytes; limits are checked before any pixels are decoded;
/// and every decoder failure comes out as <see cref="IconRenderException"/>.
/// </summary>
internal static class CustomImageLoader
{
    /// <summary>The master is at most this big. Icons top out at 256 px, so this keeps memory bounded.</summary>
    private const int MasterSize = 1024;

    private const int ExifOrientation = 0x0112;

    public static byte[] ReadBytes(string path, ImageLimits limits)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists)
            {
                throw new IconRenderException("The image file could not be found.");
            }

            if (info.Length > limits.MaxFileBytes)
            {
                throw new IconRenderException($"The image is larger than the {limits.MaxFileBytes / (1024 * 1024)} MB limit.");
            }

            if (info.Length == 0)
            {
                throw new IconRenderException("The image file is empty.");
            }

            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new IconRenderException($"The image could not be read: {ex.Message}", ex);
        }
    }

    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Decodes to a bounded master bitmap, upright and 32-bit.</summary>
    public static Bitmap Decode(byte[] bytes, ImageLimits limits)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);

            // validateImageData: false reads the header only, so the limits run before pixel decoding.
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);

            if (image.Width <= 0 || image.Height <= 0)
            {
                throw new IconRenderException("The image has no pixels.");
            }

            if (image.Width > limits.MaxDimension || image.Height > limits.MaxDimension ||
                (long)image.Width * image.Height > limits.MaxPixels)
            {
                throw new IconRenderException($"The image is too large ({image.Width} x {image.Height}).");
            }

            ApplyOrientation(image);

            var scale = Math.Min(1f, (float)MasterSize / Math.Max(image.Width, image.Height));
            var width = Math.Max(1, (int)MathF.Round(image.Width * scale));
            var height = Math.Max(1, (int)MathF.Round(image.Height * scale));
            var master = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            try
            {
                using var graphics = Graphics.FromImage(master);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(image, new Rectangle(0, 0, width, height));
                return master;
            }
            catch
            {
                master.Dispose();
                throw;
            }
        }
        catch (IconRenderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException or InvalidOperationException)
        {
            // GDI+ reports a corrupt or unknown image in several unhelpful ways, OutOfMemory among them.
            throw new IconRenderException("This file could not be read as an image.", ex);
        }
    }

    private static void ApplyOrientation(Image image)
    {
        if (Array.IndexOf(image.PropertyIdList, ExifOrientation) < 0)
        {
            return;
        }

        var value = image.GetPropertyItem(ExifOrientation)?.Value;

        if (value is not { Length: >= 2 })
        {
            return;
        }

        var flip = BitConverter.ToUInt16(value, 0) switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        };

        if (flip != RotateFlipType.RotateNoneFlipNone)
        {
            image.RotateFlip(flip);
        }
    }
}
