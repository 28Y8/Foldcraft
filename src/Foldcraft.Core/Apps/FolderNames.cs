using System.Text;

namespace Foldcraft.Core.Apps;

/// <summary>
/// Turns an app's name into a safe folder name. The names come from other people's files, so
/// they can hold anything, and this always gives something Windows will treat as written.
/// </summary>
public static class FolderNames
{
    /// <summary>Long enough for any real name, and short enough to leave room for the path around it.</summary>
    public const int MaxLength = 64;

    private const string Fallback = "App";

    private static readonly HashSet<string> ReservedDevices = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>Makes a name safe to use as a folder name. The result is never empty.</summary>
    public static string Sanitize(string? name)
    {
        var builder = new StringBuilder(name?.Length ?? 0);

        foreach (var rune in (name ?? string.Empty).EnumerateRunes())
        {
            if (Rune.IsControl(rune) || rune.Value is '\uFEFF' or '\u200B' or '\u200E' or '\u200F' or '\uFFFD')
            {
                continue;
            }

            if (rune.Value is '®' or '™' or '©')
            {
                continue;
            }

            builder.Append(rune.Value is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' ? new Rune(' ') : rune);
        }

        var text = CollapseSpaces(builder.ToString());

        if (text.Length > MaxLength)
        {
            text = Clip(text, MaxLength);
        }

        text = text.Trim().TrimEnd('.', ' ');

        if (text.Length == 0)
        {
            return Fallback;
        }

        // Windows treats CON.txt as the device CON too, so what counts is the part before the first dot.
        var stem = text.Split('.')[0].TrimEnd(' ');

        return ReservedDevices.Contains(stem) ? $"{stem} folder{text[stem.Length..]}" : text;
    }

    /// <summary>The name to try next after a clash: "Name (2)", then "Name (3)".</summary>
    public static string WithNumber(string name, int number)
    {
        ArgumentNullException.ThrowIfNull(name);

        var ending = $" ({number})";
        return Clip(name, MaxLength - ending.Length).TrimEnd('.', ' ') + ending;
    }

    private static string CollapseSpaces(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString();
    }

    /// <summary>Cuts to a length without splitting a character that takes two code units.</summary>
    private static string Clip(string text, int length)
    {
        if (text.Length <= length)
        {
            return text;
        }

        return char.IsLowSurrogate(text[length]) ? text[..(length - 1)] : text[..length];
    }
}
