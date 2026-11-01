using System.Globalization;

namespace Foldcraft.Core.Icons;

/// <summary>An opaque RGB color.</summary>
public readonly record struct IconColor(byte R, byte G, byte B)
{
    /// <summary>Parses #RRGGBB or RRGGBB.</summary>
    public static IconColor Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var hex = text.TrimStart('#');

        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"'{text}' is not a #RRGGBB color.");
        }

        return new IconColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>A solid color, or a two-stop linear gradient when <see cref="End"/> is set.</summary>
public sealed record FolderFill(IconColor Start, IconColor? End = null, float AngleDegrees = 90f)
{
    public static FolderFill Solid(IconColor color) => new(color);

    public static FolderFill Gradient(IconColor start, IconColor end, float angleDegrees = 90f) => new(start, end, angleDegrees);

    internal string CanonicalKey()
    {
        if (End is null)
        {
            return $"solid:{Start.ToHex()}";
        }

        var angle = ((AngleDegrees % 360f) + 360f) % 360f;
        return string.Create(CultureInfo.InvariantCulture, $"gradient:{Start.ToHex()}:{End.Value.ToHex()}:{angle:0.###}");
    }
}

/// <summary>The style of the folder itself. The names are part of icon identity, so they must not change.</summary>
public enum FolderShape
{
    /// <summary>The two-tone folder of Windows 11.</summary>
    Classic,

    /// <summary>One flat silhouette with no shading.</summary>
    Flat,

    /// <summary>A line drawing that lets the background show through.</summary>
    Outline,

    /// <summary>Rounded and softly lit, with a shadow under it.</summary>
    Soft3D,
}

/// <summary>Where a badge sits. The names are part of icon identity, so they must not change.</summary>
public enum BadgeCorner
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
}

/// <summary>A small mark in a corner of the icon.</summary>
/// <param name="Kind">An id from <see cref="BadgeCatalog"/>.</param>
/// <param name="Corner">Which corner it sits in.</param>
/// <param name="Color">The plate's color. Null uses the badge's own.</param>
public sealed record IconBadge(string Kind, BadgeCorner Corner = BadgeCorner.BottomRight, IconColor? Color = null)
{
    internal string CanonicalKey() => $"{Kind.ToLowerInvariant()}:{Corner}:{Color?.ToHex()}";
}

/// <summary>Where a logo sits on the folder. The names are part of icon identity, so they must not change.</summary>
public enum LogoAnchor
{
    Center,
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
}

/// <summary>What sits behind a logo. The names are part of icon identity, so they must not change.</summary>
public enum LogoPlate
{
    None,

    /// <summary>A pale rounded plate, for a dark logo.</summary>
    Light,

    /// <summary>A dark rounded plate, for a pale logo.</summary>
    Dark,
}

/// <summary>
/// Shows the custom image as a product logo sitting on the folder, instead of a picture filling
/// its front. Small sizes ignore the scale and the corner, because there a logo has to fill
/// all the room there is to stay recognizable.
/// </summary>
/// <param name="Scale">Size relative to the normal logo, from <see cref="MinScale"/> to <see cref="MaxScale"/>.</param>
/// <param name="Anchor">Where it sits.</param>
/// <param name="Plate">What sits behind it.</param>
public sealed record LogoPlacement(float Scale = 1f, LogoAnchor Anchor = LogoAnchor.Center, LogoPlate Plate = LogoPlate.None)
{
    public const float MinScale = 0.5f;
    public const float MaxScale = 1.25f;

    internal string CanonicalKey() => string.Create(CultureInfo.InvariantCulture, $"{Scale:0.##}:{Anchor}:{Plate}");
}

/// <summary>
/// Everything that determines how an icon looks. A folder shows a glyph, an emoji or a custom
/// image, never more than one, or none. The record is designed to grow: a new optional property
/// joins <see cref="CanonicalKey"/> only when it's set, so existing icons keep their file names.
/// </summary>
public sealed record FolderIconSpec
{
    public required FolderFill Fill { get; init; }

    /// <summary>An id from <see cref="GlyphCatalog"/>.</summary>
    public string? GlyphId { get; init; }

    /// <summary>One emoji, as text. See <see cref="EmojiText"/>.</summary>
    public string? Emoji { get; init; }

    /// <summary>A PNG, JPEG, BMP or GIF on disk. Decoded with limits; see <see cref="ImageLimits"/>.</summary>
    public string? CustomImagePath { get; init; }

    public FolderShape Shape { get; init; } = FolderShape.Classic;

    public IconBadge? Badge { get; init; }

    /// <summary>Set to show <see cref="CustomImagePath"/> as a logo. Null shows it as a picture.</summary>
    public LogoPlacement? Logo { get; init; }

    /// <exception cref="IconRenderException">The spec cannot be rendered.</exception>
    public void Validate()
    {
        if (Fill is null)
        {
            throw new IconRenderException("An icon needs a fill.");
        }

        if (Fill.End is not null && !float.IsFinite(Fill.AngleDegrees))
        {
            throw new IconRenderException("The gradient angle is not a number.");
        }

        if (new[] { GlyphId, Emoji, CustomImagePath }.Count(choice => choice is not null) > 1)
        {
            throw new IconRenderException("An icon can show a glyph, an emoji or a custom image, but only one of them.");
        }

        if (GlyphId is not null && GlyphCatalog.Find(GlyphId) is null)
        {
            throw new IconRenderException($"There is no glyph named '{GlyphId}'.");
        }

        if (Emoji is not null && !EmojiText.IsValid(Emoji))
        {
            throw new IconRenderException("That isn't a single emoji.");
        }

        if (!Enum.IsDefined(Shape))
        {
            throw new IconRenderException("That folder style doesn't exist.");
        }

        if (Badge is not null && BadgeCatalog.Find(Badge.Kind) is null)
        {
            throw new IconRenderException($"There is no badge named '{Badge.Kind}'.");
        }

        if (Badge is not null && !Enum.IsDefined(Badge.Corner))
        {
            throw new IconRenderException("That badge corner doesn't exist.");
        }

        if (Logo is { } logo)
        {
            if (CustomImagePath is null)
            {
                throw new IconRenderException("A logo needs a picture.");
            }

            if (!float.IsFinite(logo.Scale) || logo.Scale < LogoPlacement.MinScale || logo.Scale > LogoPlacement.MaxScale)
            {
                throw new IconRenderException("That logo size is out of range.");
            }

            if (!Enum.IsDefined(logo.Anchor) || !Enum.IsDefined(logo.Plate))
            {
                throw new IconRenderException("That logo position doesn't exist.");
            }
        }
    }

    /// <summary>The identity of the rendered result. A custom image counts by content, not by path.</summary>
    internal string CanonicalKey(string? imageSha256)
    {
        var parts = new List<string>
        {
            $"renderer={IconRenderer.Version}",
            $"fill={Fill.CanonicalKey()}",
            $"glyph={GlyphId?.ToLowerInvariant()}",
            $"image={imageSha256}",
        };

        if (Emoji is not null)
        {
            parts.Add($"emoji={Emoji}");
        }

        if (Shape != FolderShape.Classic)
        {
            parts.Add($"shape={Shape}");
        }

        if (Badge is not null)
        {
            parts.Add($"badge={Badge.CanonicalKey()}");
        }

        if (Logo is not null)
        {
            parts.Add($"logo={Logo.CanonicalKey()}");
        }

        return string.Join('|', parts);
    }
}
