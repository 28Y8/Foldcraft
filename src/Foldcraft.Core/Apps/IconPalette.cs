using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Foldcraft.Core.Icons;

namespace Foldcraft.Core.Apps;

/// <summary>Color arithmetic for choosing a folder to suit a logo.</summary>
public static class ColorMath
{
    /// <summary>The WCAG contrast ratio of two colors, from 1 (the same) to 21 (black on white).</summary>
    public static double Contrast(IconColor a, IconColor b)
    {
        var (first, second) = (Luminance(a), Luminance(b));
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    public static double Luminance(IconColor color) =>
        (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

    /// <summary>Hue from 0 to 360, saturation and lightness from 0 to 1.</summary>
    public static (double Hue, double Saturation, double Lightness) ToHsl(IconColor color)
    {
        var (r, g, b) = (color.R / 255.0, color.G / 255.0, color.B / 255.0);
        var (max, min) = (Math.Max(r, Math.Max(g, b)), Math.Min(r, Math.Min(g, b)));
        var lightness = (max + min) / 2;
        var delta = max - min;

        if (delta < 1e-9)
        {
            return (0, 0, lightness);
        }

        var saturation = delta / (1 - Math.Abs((2 * lightness) - 1));
        var hue = max == r ? ((g - b) / delta) + (g < b ? 6 : 0)
            : max == g ? ((b - r) / delta) + 2
            : ((r - g) / delta) + 4;

        return (hue * 60, saturation, lightness);
    }

    public static IconColor FromHsl(double hue, double saturation, double lightness)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        lightness = Math.Clamp(lightness, 0, 1);

        var chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var m = lightness - (chroma / 2);

        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };

        return new IconColor(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    /// <summary>How far apart two hues are around the color wheel, from 0 to 180.</summary>
    public static double HueDistance(double a, double b)
    {
        var distance = Math.Abs(a - b) % 360;
        return distance > 180 ? 360 - distance : distance;
    }

    public static IconColor Mix(IconColor a, IconColor b, double share) => new(
        ToByte(((a.R * (1 - share)) + (b.R * share)) / 255),
        ToByte(((a.G * (1 - share)) + (b.G * share)) / 255),
        ToByte(((a.B * (1 - share)) + (b.B * share)) / 255));

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

    private static double Channel(byte value)
    {
        var scaled = value / 255.0;
        return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
    }
}

/// <summary>A small sample of a logo's visible pixels, for choosing colors and judging contrast.</summary>
public sealed class LogoSample
{
    /// <summary>A contrast below this and that part of the logo melts into whatever is behind it.</summary>
    public const double VisibleContrast = 2.2;

    private const int SampleSize = 48;
    private const byte OpaqueEnough = 128;

    public LogoSample(IReadOnlyList<IconColor> visiblePixels)
    {
        Pixels = visiblePixels ?? throw new ArgumentNullException(nameof(visiblePixels));
    }

    public IReadOnlyList<IconColor> Pixels { get; }

    /// <summary>Reads a sample from a picture. Nearest pixels are kept, so no color is invented at an edge.</summary>
    public static LogoSample From(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);

        using var stream = new MemoryStream(png, writable: false);
        using var image = new Bitmap(stream);
        using var small = new Bitmap(SampleSize, SampleSize, PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(small))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(image, new Rectangle(0, 0, SampleSize, SampleSize));
        }

        var data = small.LockBits(new Rectangle(0, 0, SampleSize, SampleSize), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var pixels = new List<IconColor>(SampleSize * SampleSize);
            var row = new int[SampleSize];

            for (var y = 0; y < SampleSize; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, SampleSize);

                foreach (var pixel in row)
                {
                    if ((byte)(pixel >> 24) >= OpaqueEnough)
                    {
                        pixels.Add(new IconColor((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel));
                    }
                }
            }

            return new LogoSample(pixels);
        }
        finally
        {
            small.UnlockBits(data);
        }
    }

    /// <summary>How much of the logo stands out against a color, from 0 to 1.</summary>
    public double VisibleShare(IconColor background) => Pixels.Count == 0
        ? 0
        : (double)Pixels.Count(pixel => ColorMath.Contrast(pixel, background) >= VisibleContrast) / Pixels.Count;
}

/// <summary>A folder's colors, picked from a logo.</summary>
/// <param name="Dominant">The main brand color.</param>
/// <param name="Companion">A second color to go with it.</param>
/// <param name="IsNeutral">True when the logo has no real color, so this is a tasteful gray.</param>
public sealed record IconPalette(IconColor Dominant, IconColor Companion, bool IsNeutral = false)
{
    /// <summary>A pixel lighter than this in every channel is background, not brand.</summary>
    private const byte NearWhite = 232;

    /// <summary>A pixel darker than this in every channel is outline, not brand.</summary>
    private const byte NearBlack = 36;

    private const double MinimumSaturation = 0.22;

    /// <summary>With less of the logo in real color than this, it's a grayscale logo with a stray pixel.</summary>
    private const double MinimumColorShare = 0.06;

    /// <summary>Hues closer than this are one color.</summary>
    private const double SameHue = 20;

    private const int ChannelBits = 4;

    private static readonly IconColor DarkNeutralStart = IconColor.Parse("#566072");
    private static readonly IconColor DarkNeutralEnd = IconColor.Parse("#343B49");
    private static readonly IconColor LightNeutralStart = IconColor.Parse("#DCE0E7");
    private static readonly IconColor LightNeutralEnd = IconColor.Parse("#B4BBC8");

    /// <summary>
    /// Picks colors the way a designer would look at a logo: ignore the white, the black and the
    /// grays, group what's left into a small set of colors and take the biggest. A vivid
    /// color counts for more than a dull one of the same size, because that's what the eye sees.
    /// </summary>
    public static IconPalette Extract(LogoSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var groups = new Dictionary<int, ColorGroup>();
        var colored = 0;

        foreach (var pixel in sample.Pixels)
        {
            if (Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) >= NearWhite ||
                Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) <= NearBlack)
            {
                continue;
            }

            var (_, saturation, _) = ColorMath.ToHsl(pixel);

            if (saturation < MinimumSaturation)
            {
                continue;
            }

            colored++;

            var key = (pixel.R >> ChannelBits << 8) | (pixel.G >> ChannelBits << 4) | (pixel.B >> ChannelBits);

            if (!groups.TryGetValue(key, out var group))
            {
                group = new ColorGroup();
                groups[key] = group;
            }

            group.Add(pixel, 1 + (1.5 * saturation));
        }

        if (sample.Pixels.Count == 0 || (double)colored / sample.Pixels.Count < MinimumColorShare)
        {
            return Neutral(sample);
        }

        var ranked = groups.Values
            .OrderByDescending(group => group.Weight)
            .ThenBy(group => group.Mean.ToHex(), StringComparer.Ordinal)
            .ToArray();

        var clusters = Cluster(ranked);
        var dominant = clusters[0];
        var (dominantHue, _, _) = ColorMath.ToHsl(dominant.Mean);

        var companion = clusters.Skip(1).FirstOrDefault(cluster =>
            cluster.Weight >= dominant.Weight * 0.08 &&
            ColorMath.HueDistance(dominantHue, ColorMath.ToHsl(cluster.Mean).Hue) >= SameHue);

        return new IconPalette(dominant.Mean, (companion ?? dominant).Mean);
    }

    /// <summary>
    /// The gradient for a folder. Brand colors can be neon or nearly black, so they're brought
    /// into a range that looks right on a Windows 11 folder, and a clashing companion only
    /// tints the shade instead of fighting the main color.
    /// </summary>
    public FolderFill ToFill()
    {
        if (IsNeutral)
        {
            return FolderFill.Gradient(Dominant, Companion, 115f);
        }

        var (hue, saturation, lightness) = ColorMath.ToHsl(Dominant);
        var (companionHue, _, _) = ColorMath.ToHsl(Companion);

        saturation = Math.Clamp(saturation, 0.38, 0.82);
        lightness = Math.Clamp(lightness, 0.36, 0.58);

        var start = ColorMath.FromHsl(hue, saturation, lightness + 0.05);
        var shade = ColorMath.FromHsl(hue + 6, saturation, lightness - 0.13);
        var distance = ColorMath.HueDistance(hue, companionHue);

        var end = distance >= SameHue
            ? ColorMath.Mix(shade, ColorMath.FromHsl(companionHue, saturation, lightness - 0.13), distance <= 70 ? 0.6 : 0.2)
            : shade;

        return FolderFill.Gradient(start, end, 115f);
    }

    /// <summary>The color a fill looks like from a distance, for judging contrast.</summary>
    public static IconColor MiddleOf(FolderFill fill) =>
        fill.End is { } end ? ColorMath.Mix(fill.Start, end, 0.5) : fill.Start;

    /// <summary>A pale folder for a dark logo and a dark one for anything else.</summary>
    private static IconPalette Neutral(LogoSample sample)
    {
        var isDark = sample.Pixels.Count > 0 && sample.Pixels.Average(ColorMath.Luminance) < 0.25;

        return isDark
            ? new IconPalette(LightNeutralStart, LightNeutralEnd, IsNeutral: true)
            : new IconPalette(DarkNeutralStart, DarkNeutralEnd, IsNeutral: true);
    }

    /// <summary>Folds near-identical shades together, so one color in a gradient isn't split into many.</summary>
    private static List<ColorGroup> Cluster(IEnumerable<ColorGroup> ranked)
    {
        var clusters = new List<ColorGroup>();

        foreach (var group in ranked)
        {
            var (hue, _, lightness) = ColorMath.ToHsl(group.Mean);

            var home = clusters.FirstOrDefault(cluster =>
            {
                var (clusterHue, _, clusterLightness) = ColorMath.ToHsl(cluster.Mean);
                return ColorMath.HueDistance(hue, clusterHue) < SameHue && Math.Abs(lightness - clusterLightness) < 0.3;
            });

            if (home is null)
            {
                home = new ColorGroup();
                clusters.Add(home);
            }

            home.Merge(group);
        }

        return clusters.OrderByDescending(cluster => cluster.Weight).ToList();
    }

    private sealed class ColorGroup
    {
        private double _red;
        private double _green;
        private double _blue;

        public double Weight { get; private set; }

        public IconColor Mean => Weight <= 0
            ? default
            : new IconColor((byte)Math.Round(_red / Weight), (byte)Math.Round(_green / Weight), (byte)Math.Round(_blue / Weight));

        public void Add(IconColor pixel, double weight)
        {
            _red += pixel.R * weight;
            _green += pixel.G * weight;
            _blue += pixel.B * weight;
            Weight += weight;
        }

        public void Merge(ColorGroup other)
        {
            _red += other._red;
            _green += other._green;
            _blue += other._blue;
            Weight += other.Weight;
        }
    }
}
