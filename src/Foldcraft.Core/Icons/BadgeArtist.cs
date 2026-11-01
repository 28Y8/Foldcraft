using System.Drawing;
using System.Drawing.Drawing2D;

namespace Foldcraft.Core.Icons;

/// <summary>
/// Draws a badge over a finished folder. A plate in the badge's color sits on a white rim, which
/// keeps it apart from a folder of the same color. Sizes come from a table rather than a
/// formula, because at 16 and 32 px a single pixel decides whether the symbol still reads.
/// </summary>
internal static class BadgeArtist
{
    /// <summary>At this plate size and below, the badge uses its tiny drawing.</summary>
    private const int TinyPlate = 10;

    /// <summary>Icon size and the badge's full width including its rim, for the sizes in an .ico.</summary>
    private static readonly (int Size, int Width)[] Widths =
    [
        (16, 10), (20, 11), (24, 13), (32, 16), (40, 19), (48, 22), (64, 28), (96, 40), (256, 88),
    ];

    /// <summary>Below this size a shadow would only smudge the rim.</summary>
    private const int ShadowSize = 48;

    /// <summary>At this size and below a badge needs its whole corner, so it stays at the frame's edge.</summary>
    private const int FrameEdgeSize = 20;

    /// <summary>The symbol's share of the plate. Small plates need all the room they can get.</summary>
    private static float SymbolShare(int plate) => plate <= 16 ? 0.72f : 0.62f;

    /// <param name="graphics">The finished folder.</param>
    /// <param name="spec">The badge.</param>
    /// <param name="size">The icon's size in pixels.</param>
    /// <param name="folderBottom">Where the folder ends. A bottom badge rises toward it so it sits on the folder's corner.</param>
    public static void Draw(Graphics graphics, IconBadge spec, int size, int folderBottom)
    {
        var badge = BadgeCatalog.Find(spec.Kind)!;
        var outer = OuterWidth(size);
        var rim = RimWidth(size);
        var plate = outer - (rim * 2);
        var bounds = Place(spec.Corner, size, outer, folderBottom);
        var inner = Rectangle.Inflate(bounds, -rim, -rim);

        var color = spec.Color ?? badge.DefaultColor;
        var ink = ReadableOn(color);

        using (var rimBrush = new SolidBrush(Color.White))
        using (var rimPath = PlatePath(badge.Plate, bounds))
        {
            if (size >= ShadowSize)
            {
                SoftShadow.Draw(graphics, rimPath, size, size / 24, size / 96, 96);
            }

            graphics.FillPath(rimBrush, rimPath);
        }

        using (var fill = new SolidBrush(Color.FromArgb(color.R, color.G, color.B)))
        using (var platePath = PlatePath(badge.Plate, inner))
        {
            graphics.FillPath(fill, platePath);
        }

        var side = plate * SymbolShare(plate);
        var offsetY = badge.Plate == BadgePlate.Triangle ? plate * 0.1f : 0f;
        var origin = new PointF(inner.Left + ((plate - side) / 2f), inner.Top + ((plate - side) / 2f) + offsetY);
        var layers = plate <= TinyPlate ? badge.TinySymbol : badge.Symbol;

        FolderArtist.DrawLayers(graphics, layers, ink, origin, side / GlyphLayer.BoxSize, 1f);
    }

    /// <summary>Black or white, whichever reads better. One of them always reaches 4.5:1 on any color.</summary>
    internal static Color ReadableOn(IconColor color)
    {
        var luminance = Luminance(color);
        var onWhite = 1.05 / (luminance + 0.05);
        var onBlack = (luminance + 0.05) / 0.05;

        return onBlack >= onWhite ? Color.Black : Color.White;
    }

    /// <summary>The WCAG contrast ratio between two opaque colors.</summary>
    internal static double Contrast(IconColor a, IconColor b)
    {
        var (first, second) = (Luminance(a), Luminance(b));
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    internal static int OuterWidth(int size)
    {
        foreach (var (known, width) in Widths)
        {
            if (known >= size)
            {
                return known == size ? width : Math.Max(10, (int)MathF.Round(size * width / (float)known));
            }
        }

        return (int)MathF.Round(size * 0.39f);
    }

    private static int RimWidth(int size) => Math.Max(1, size / 32);

    private static Rectangle Place(BadgeCorner corner, int size, int outer, int folderBottom)
    {
        var bottom = size - outer - (size <= FrameEdgeSize ? 0 : (size - folderBottom) / 2);

        return corner switch
        {
            BadgeCorner.BottomLeft => new Rectangle(0, bottom, outer, outer),
            BadgeCorner.TopRight => new Rectangle(size - outer, 0, outer, outer),
            BadgeCorner.TopLeft => new Rectangle(0, 0, outer, outer),
            _ => new Rectangle(size - outer, bottom, outer, outer),
        };
    }

    private static GraphicsPath PlatePath(BadgePlate plate, Rectangle bounds)
    {
        var path = new GraphicsPath();

        if (plate == BadgePlate.Triangle)
        {
            path.AddPolygon(new PointF[]
            {
                new(bounds.Left + (bounds.Width / 2f), bounds.Top),
                new(bounds.Right, bounds.Bottom),
                new(bounds.Left, bounds.Bottom),
            });
        }
        else
        {
            path.AddEllipse(bounds);
        }

        return path;
    }

    private static double Luminance(IconColor color) =>
        (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

    private static double Channel(byte value)
    {
        var scaled = value / 255.0;
        return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
    }
}
