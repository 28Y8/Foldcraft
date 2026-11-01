using System.Text;
using System.Text.Json;
using Foldcraft.Core.Icons;

namespace Foldcraft.Core.Themes;

/// <summary>
/// The theme file format: a small versioned JSON document, used for import, export and the
/// app's own saved themes alike. A file from outside either passes every check or nothing in
/// it is used.
/// <code>
/// { "format": "foldcraft-themes", "version": 1,
///   "themes": [ { "name": "Ocean", "mood": "Cool", "fill": { "start": "#0EA5E9" }, "glyph": "fish" } ] }
/// </code>
/// </summary>
public static class ThemeFile
{
    public const string FormatName = "foldcraft-themes";
    public const int CurrentVersion = 1;

    /// <summary>A hundred themes come to about 20 KB, so this only stops something that isn't a theme file.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    public const int MaxThemes = 500;
    public const int MaxNameLength = 60;
    public const int MaxMoodLength = 30;

    private const string SavedIdPrefix = "saved:";

    private static readonly JsonReaderOptions StrictReader = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8,
    };

    private static readonly JsonDocumentOptions StrictDocument = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8,
    };

    /// <summary>Turns themes into a file. Ids and pictures are left out unless asked for.</summary>
    /// <param name="themes">The themes to write.</param>
    /// <param name="forThisPc">
    /// True only for the app's own saved copy, where rules rely on a theme's id staying the same.
    /// A shared file never carries a picture's path: it would mean nothing on another PC, and
    /// it gives away the user name it contains.
    /// </param>
    public static byte[] Write(IEnumerable<FolderTheme> themes, bool forThisPc)
    {
        ArgumentNullException.ThrowIfNull(themes);

        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", FormatName);
            writer.WriteNumber("version", CurrentVersion);
            writer.WriteStartArray("themes");

            foreach (var theme in themes)
            {
                writer.WriteStartObject();

                if (forThisPc)
                {
                    writer.WriteString("id", theme.Id);
                }

                writer.WriteString("name", theme.Name);
                writer.WriteString("mood", theme.Mood);
                IconSpecCodec.WriteLook(writer, theme.Spec, forThisPc);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>Reads a theme file. A theme with no id gets a new one.</summary>
    /// <exception cref="LookFormatException">The file breaks a rule. Nothing in it should be used.</exception>
    public static IReadOnlyList<FolderTheme> Read(byte[] bytes) => Parse<IReadOnlyList<FolderTheme>>(bytes, root =>
    {
        var themes = ThemesIn(root);

        if (themes.GetArrayLength() > MaxThemes)
        {
            throw new LookFormatException($"This file has more than {MaxThemes} themes.");
        }

        return themes.EnumerateArray().Select(element => ReadTheme(element)).ToArray();
    });

    /// <summary>
    /// Reads the app's own saved copy, which is held to what <see cref="ThemeStore"/> lets in. A
    /// picture's path was chosen on this PC, so it isn't judged the way a downloaded file's is,
    /// and one theme that can't be read, such as one using a symbol that's gone, is counted
    /// rather than costing the rest.
    /// </summary>
    /// <exception cref="LookFormatException">The file as a whole isn't a theme file.</exception>
    internal static (List<FolderTheme> Themes, int Skipped) ReadSaved(byte[] bytes) => Parse(bytes, root =>
    {
        var themes = new List<FolderTheme>();
        var skipped = 0;

        foreach (var element in ThemesIn(root).EnumerateArray())
        {
            try
            {
                themes.Add(ReadTheme(element, trustImagePath: true));
            }
            catch (LookFormatException)
            {
                skipped++;
            }
        }

        return (themes, skipped);
    });

    private static T Parse<T>(byte[] bytes, Func<JsonElement, T> read)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length > MaxFileBytes)
        {
            throw new LookFormatException("This file is too big to be a theme file.");
        }

        var text = bytes.AsSpan();

        if (text.StartsWith(Encoding.UTF8.Preamble))
        {
            text = text[Encoding.UTF8.Preamble.Length..];
        }

        try
        {
            RequireReadableJson(text);

            using var document = JsonDocument.Parse(text.ToArray(), StrictDocument);
            return read(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or DecoderFallbackException or InvalidOperationException)
        {
            throw new LookFormatException("This isn't a Foldcraft theme file. It may be damaged.");
        }
    }

    /// <summary>Walks the raw tokens once, so bad encoding fails here with one clear answer.</summary>
    private static void RequireReadableJson(ReadOnlySpan<byte> text)
    {
        var reader = new Utf8JsonReader(text, StrictReader);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                reader.GetString();
            }
        }
    }

    /// <summary>Checks everything around the list of themes and hands back the list.</summary>
    private static JsonElement ThemesIn(JsonElement root)
    {
        IconSpecCodec.RequireProperties(root, "This theme file", ["format", "version", "themes"]);

        if (!root.TryGetProperty("format", out var format) ||
            format.ValueKind != JsonValueKind.String ||
            format.GetString() != FormatName)
        {
            throw new LookFormatException("This isn't a Foldcraft theme file.");
        }

        if (!root.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var number) ||
            number < 1)
        {
            throw new LookFormatException("This theme file has no version Foldcraft can read.");
        }

        if (number > CurrentVersion)
        {
            throw new LookFormatException("This theme file was made by a newer version of Foldcraft.");
        }

        if (!root.TryGetProperty("themes", out var themes) || themes.ValueKind != JsonValueKind.Array)
        {
            throw new LookFormatException("This theme file has no list of themes.");
        }

        return themes;
    }

    private static FolderTheme ReadTheme(JsonElement element, bool trustImagePath = false)
    {
        var spec = IconSpecCodec.ReadLook(element, ["id", "name", "mood"], trustImagePath);
        var name = RequireLabel(IconSpecCodec.ReadOptionalText(element, "name"), "name", MaxNameLength);
        var mood = IconSpecCodec.ReadOptionalText(element, "mood") is { } text
            ? RequireLabel(text, "mood", MaxMoodLength)
            : "Saved";
        var id = IconSpecCodec.ReadOptionalText(element, "id");

        if (id is not null && !IsSavedId(id))
        {
            throw new LookFormatException("A theme in this file has an id Foldcraft can't use.");
        }

        return new FolderTheme { Id = id ?? NewId(), Name = name, Mood = mood, Spec = spec };
    }

    internal static string NewId() => SavedIdPrefix + Guid.NewGuid().ToString("N");

    /// <summary>Built-in themes have ids of their own, and a file must not be able to pose as one.</summary>
    private static bool IsSavedId(string id) =>
        id.StartsWith(SavedIdPrefix, StringComparison.Ordinal) &&
        id.Length == SavedIdPrefix.Length + 32 &&
        id.AsSpan(SavedIdPrefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static string RequireLabel(string? text, string what, int maxLength)
    {
        var trimmed = text?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new LookFormatException($"A theme in this file has no {what}.");
        }

        if (trimmed.Length > maxLength || trimmed.Any(char.IsControl))
        {
            throw new LookFormatException($"A theme in this file has a {what} Foldcraft can't use.");
        }

        return trimmed;
    }
}
