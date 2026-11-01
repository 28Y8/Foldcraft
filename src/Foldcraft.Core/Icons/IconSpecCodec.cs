using System.Globalization;
using System.Text;
using System.Text.Json;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Icons;

/// <summary>A theme or recorded look doesn't follow the rules. The message is safe to show.</summary>
public sealed class LookFormatException(string message) : Exception(message);

/// <summary>
/// Reads and writes a look as JSON, for theme files and for the journal. Reading is strict on
/// purpose: an unknown name, a repeated name or a value of the wrong type stops the read,
/// because the input may have come from anywhere.
/// </summary>
public static class IconSpecCodec
{
    private const int MaxImagePathLength = 1024;

    private static readonly string[] LookProperties = ["fill", "shape", "glyph", "emoji", "image", "badge"];
    private static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    private static readonly Dictionary<string, FolderShape> ShapeNames = new()
    {
        ["classic"] = FolderShape.Classic,
        ["flat"] = FolderShape.Flat,
        ["outline"] = FolderShape.Outline,
        ["soft3d"] = FolderShape.Soft3D,
    };

    private static readonly Dictionary<string, LogoAnchor> AnchorNames = new()
    {
        ["center"] = LogoAnchor.Center,
        ["bottomRight"] = LogoAnchor.BottomRight,
        ["bottomLeft"] = LogoAnchor.BottomLeft,
        ["topRight"] = LogoAnchor.TopRight,
        ["topLeft"] = LogoAnchor.TopLeft,
    };

    private static readonly Dictionary<string, LogoPlate> PlateNames = new()
    {
        ["none"] = LogoPlate.None,
        ["light"] = LogoPlate.Light,
        ["dark"] = LogoPlate.Dark,
    };

    private static readonly Dictionary<string, BadgeCorner> CornerNames = new()
    {
        ["bottomRight"] = BadgeCorner.BottomRight,
        ["bottomLeft"] = BadgeCorner.BottomLeft,
        ["topRight"] = BadgeCorner.TopRight,
        ["topLeft"] = BadgeCorner.TopLeft,
    };

    /// <summary>A look as one line of JSON, for the journal. Reads back with <see cref="TryReadRecorded"/>.</summary>
    public static string ToRecordedJson(FolderIconSpec spec, string? imageSha256)
    {
        ArgumentNullException.ThrowIfNull(spec);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteLook(writer, spec, includeImage: true);

            if (spec.Logo is { } logo)
            {
                writer.WriteStartObject("logo");
                writer.WriteNumber("scale", logo.Scale);
                writer.WriteString("anchor", AnchorNames.First(pair => pair.Value == logo.Anchor).Key);
                writer.WriteString("plate", PlateNames.First(pair => pair.Value == logo.Plate).Key);
                writer.WriteEndObject();
            }

            if (imageSha256 is not null)
            {
                writer.WriteString("imageSha256", imageSha256);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads a look from the journal. A failure means "no usable record", never an error: an
    /// older build's glyph could be gone, and that only stops Repair for that one folder. The
    /// picture path isn't judged here, because the person chose it and its bytes are hash-checked.
    /// </summary>
    public static bool TryReadRecorded(string? json, out FolderIconSpec? spec, out string? imageSha256)
    {
        spec = null;
        imageSha256 = null;

        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            spec = ReadLook(document.RootElement, ["imageSha256", "logo"], trustImagePath: true);

            // Only the journal knows "logo". A theme file stays exactly the format it was.
            if (document.RootElement.TryGetProperty("logo", out var logo))
            {
                spec = spec with { Logo = ReadLogo(logo) };
                spec.Validate();
            }

            if (document.RootElement.TryGetProperty("imageSha256", out var hash))
            {
                imageSha256 = hash.GetString();
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or LookFormatException or InvalidOperationException or IconRenderException)
        {
            spec = null;
            return false;
        }
    }

    /// <summary>Writes a look's properties into the object already open on the writer.</summary>
    internal static void WriteLook(Utf8JsonWriter writer, FolderIconSpec spec, bool includeImage)
    {
        writer.WriteStartObject("fill");
        writer.WriteString("start", spec.Fill.Start.ToHex());

        if (spec.Fill.End is { } end)
        {
            writer.WriteString("end", end.ToHex());
            writer.WriteNumber("angle", spec.Fill.AngleDegrees);
        }

        writer.WriteEndObject();

        if (spec.Shape != FolderShape.Classic)
        {
            writer.WriteString("shape", ShapeNames.First(pair => pair.Value == spec.Shape).Key);
        }

        if (spec.GlyphId is not null)
        {
            writer.WriteString("glyph", spec.GlyphId);
        }

        if (spec.Emoji is not null)
        {
            writer.WriteString("emoji", spec.Emoji);
        }

        if (includeImage && spec.CustomImagePath is not null)
        {
            writer.WriteString("image", spec.CustomImagePath);
        }

        if (spec.Badge is { } badge)
        {
            writer.WriteStartObject("badge");
            writer.WriteString("kind", badge.Kind);

            if (badge.Corner != BadgeCorner.BottomRight)
            {
                writer.WriteString("corner", CornerNames.First(pair => pair.Value == badge.Corner).Key);
            }

            if (badge.Color is { } color)
            {
                writer.WriteString("color", color.ToHex());
            }

            writer.WriteEndObject();
        }
    }

    /// <summary>Reads a look from an object, which may also hold the named extra properties.</summary>
    /// <exception cref="LookFormatException">The look breaks a rule.</exception>
    internal static FolderIconSpec ReadLook(JsonElement element, IReadOnlyCollection<string> extraProperties, bool trustImagePath = false)
    {
        RequireProperties(element, "A look", [.. LookProperties, .. extraProperties]);

        if (!element.TryGetProperty("fill", out var fillElement))
        {
            throw new LookFormatException("A look has no fill color.");
        }

        var spec = new FolderIconSpec
        {
            Fill = ReadFill(fillElement),
            Shape = ReadChoice(element, "shape", ShapeNames, FolderShape.Classic),
            GlyphId = ReadOptionalText(element, "glyph"),
            Emoji = ReadOptionalText(element, "emoji"),
            CustomImagePath = ReadOptionalText(element, "image"),
            Badge = element.TryGetProperty("badge", out var badge) ? ReadBadge(badge) : null,
        };

        if (spec.CustomImagePath is { } image && !trustImagePath)
        {
            RequireSafePicturePath(image);
        }

        try
        {
            spec.Validate();
        }
        catch (IconRenderException ex)
        {
            throw new LookFormatException(ex.Message);
        }

        return spec;
    }

    /// <summary>Refuses anything but an object made only of these names, each named once.</summary>
    internal static void RequireProperties(JsonElement element, string what, IReadOnlyCollection<string> allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new LookFormatException($"{what} isn't written the right way.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new LookFormatException($"{what} has a setting Foldcraft doesn't know: {Clip(property.Name)}.");
            }

            if (!seen.Add(property.Name))
            {
                throw new LookFormatException($"{what} sets {Clip(property.Name)} twice.");
            }
        }
    }

    internal static string? ReadOptionalText(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new LookFormatException($"{name} should be text.");
    }

    /// <summary>
    /// True when opening a picture can't reach another computer. Anything that isn't a plain full
    /// path counts as not local. Whatever opens a picture with no click to ask for it checks this.
    /// </summary>
    public static bool IsLocalPicture(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return PathResolver.Normalize(path, out _, out _) is { } normalized && IsLocal(normalized);
    }

    /// <summary>
    /// Only a full path on a local drive to a picture file. This is what stands between a
    /// downloaded theme and the disk: a network path would send this PC's sign-in to whoever
    /// runs that server the moment Windows opened it, so anything but a plain local path stops
    /// here. The decision is made on the normalized path, because Windows reads "/\" and "\/"
    /// the same as two backslashes. Only a file from outside comes through here, so a picture of
    /// the user's own on a drive that's unplugged today is never refused for it.
    /// </summary>
    /// <exception cref="LookFormatException">The path isn't one Foldcraft will open.</exception>
    internal static void RequireSafePicturePath(string path)
    {
        const string Refused = "This look points to a picture in a place Foldcraft won't open.";

        if (path.Length > MaxImagePathLength ||
            path.Any(char.IsControl) ||
            (path.Length >= 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/') ||
            path.Contains('%') ||
            path.Contains('*') ||
            path.Contains('?') ||
            path.Split('\\', '/').Contains(".."))
        {
            throw new LookFormatException(Refused);
        }

        var normalized = PathResolver.Normalize(path, out _, out _);

        if (normalized is null ||
            !PictureExtensions.Contains(Path.GetExtension(normalized), StringComparer.OrdinalIgnoreCase) ||
            !IsLocal(normalized) ||
            NativeMethods.GetDriveType(Path.GetPathRoot(normalized)!) == NativeMethods.DRIVE_NO_ROOT_DIR)
        {
            throw new LookFormatException(Refused);
        }
    }

    /// <summary>The network test comes first, because asking Windows about a server's root would already reach it.</summary>
    private static bool IsLocal(string normalized) =>
        !PathResolver.IsNetworkPath(normalized) &&
        NativeMethods.GetDriveType(Path.GetPathRoot(normalized)!) != NativeMethods.DRIVE_REMOTE;

    private static FolderFill ReadFill(JsonElement element)
    {
        RequireProperties(element, "A look's fill", ["start", "end", "angle"]);

        var start = ReadColor(element, "start") ?? throw new LookFormatException("A look's fill has no start color.");
        var end = ReadColor(element, "end");
        var angle = 90f;

        if (element.TryGetProperty("angle", out var angleElement))
        {
            if (end is null)
            {
                throw new LookFormatException("A solid color can't have a gradient angle.");
            }

            if (angleElement.ValueKind != JsonValueKind.Number ||
                !angleElement.TryGetSingle(out angle) ||
                !float.IsFinite(angle) ||
                angle < 0f ||
                angle > 360f)
            {
                throw new LookFormatException("A gradient angle should be a number from 0 to 360.");
            }
        }

        return new FolderFill(start, end, angle);
    }

    private static LogoPlacement ReadLogo(JsonElement element)
    {
        RequireProperties(element, "A logo", ["scale", "anchor", "plate"]);

        if (!element.TryGetProperty("scale", out var scale) || scale.ValueKind != JsonValueKind.Number || !scale.TryGetSingle(out var value))
        {
            throw new LookFormatException("A logo's size should be a number.");
        }

        return new LogoPlacement(
            value,
            ReadChoice(element, "anchor", AnchorNames, LogoAnchor.Center),
            ReadChoice(element, "plate", PlateNames, LogoPlate.None));
    }

    private static IconBadge ReadBadge(JsonElement element)
    {
        RequireProperties(element, "A badge", ["kind", "corner", "color"]);

        var kind = ReadOptionalText(element, "kind") ?? throw new LookFormatException("A badge has no kind.");

        return new IconBadge(kind, ReadChoice(element, "corner", CornerNames, BadgeCorner.BottomRight), ReadColor(element, "color"));
    }

    /// <summary>Exactly #RRGGBB, stricter than <see cref="IconColor.Parse"/> because this text wasn't typed here.</summary>
    private static IconColor? ReadColor(JsonElement element, string name)
    {
        if (ReadOptionalText(element, name) is not { } text)
        {
            return null;
        }

        if (text.Length != 7 || text[0] != '#' || !text.Skip(1).All(char.IsAsciiHexDigit))
        {
            throw new LookFormatException($"{name} should be a color like #2F6FED.");
        }

        return IconColor.Parse(text);
    }

    private static T ReadChoice<T>(JsonElement element, string name, Dictionary<string, T> choices, T fallback)
    {
        if (ReadOptionalText(element, name) is not { } text)
        {
            return fallback;
        }

        return choices.TryGetValue(text, out var value)
            ? value
            : throw new LookFormatException($"{Clip(text)} isn't a {name} Foldcraft knows.");
    }

    /// <summary>Text from a file that's being refused is quoted back short, and on one line.</summary>
    private static string Clip(string text)
    {
        var flat = new string(text.Where(c => !char.IsControl(c)).Take(40).ToArray());
        return string.Create(CultureInfo.InvariantCulture, $"\"{flat}\"");
    }
}
