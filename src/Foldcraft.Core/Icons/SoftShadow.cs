using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Foldcraft.Core.Icons;

/// <summary>
/// A real blurred shadow. Stacking outlines of growing size leaves visible steps at large icon
/// sizes, so this draws the shape once and blurs its alpha.
/// </summary>
internal static class SoftShadow
{
    /// <summary>Three box blurs in a row are close enough to a gaussian that the eye can't tell.</summary>
    private const int Passes = 3;

    /// <param name="graphics">Where the shadow lands.</param>
    /// <param name="shape">The outline casting it, in the same coordinates.</param>
    /// <param name="size">The icon's size in pixels.</param>
    /// <param name="blur">How far the shadow's edge fades, in pixels.</param>
    /// <param name="drop">How far below the shape it sits, in pixels.</param>
    /// <param name="alpha">How dark it is under the shape's middle, from 0 to 255.</param>
    public static void Draw(Graphics graphics, GraphicsPath shape, int size, int blur, int drop, int alpha)
    {
        using var mask = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (var maskGraphics = Graphics.FromImage(mask))
        using (var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
        {
            maskGraphics.SmoothingMode = SmoothingMode.AntiAlias;
            maskGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            maskGraphics.TranslateTransform(0f, drop);
            maskGraphics.FillPath(brush, shape);
        }

        BlurAlpha(mask, Math.Max(1, blur / Passes));
        graphics.DrawImage(mask, 0, 0, size, size);
    }

    private static void BlurAlpha(Bitmap bitmap, int radius)
    {
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        try
        {
            var (width, height) = (bitmap.Width, bitmap.Height);
            var pixels = new int[width * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            var alpha = pixels.Select(pixel => (pixel >> 24) & 255).ToArray();
            var scratch = new int[alpha.Length];

            for (var pass = 0; pass < Passes; pass++)
            {
                BoxBlur(alpha, scratch, width, height, radius, 1, width);
                BoxBlur(scratch, alpha, height, width, radius, width, 1);
            }

            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = alpha[i] << 24;
            }

            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>A running average along each line. The steps choose whether lines run across or down.</summary>
    private static void BoxBlur(int[] source, int[] target, int length, int lines, int radius, int alongStep, int lineStep)
    {
        var window = (radius * 2) + 1;

        for (var line = 0; line < lines; line++)
        {
            var origin = line * lineStep;
            var total = 0;

            for (var i = -radius; i <= radius; i++)
            {
                total += source[origin + (Math.Clamp(i, 0, length - 1) * alongStep)];
            }

            for (var i = 0; i < length; i++)
            {
                target[origin + (i * alongStep)] = total / window;

                var entering = Math.Min(length - 1, i + radius + 1);
                var leaving = Math.Max(0, i - radius);

                total += source[origin + (entering * alongStep)] - source[origin + (leaving * alongStep)];
            }
        }
    }
}
