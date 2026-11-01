using System.Text;
using Foldcraft.Core.Infrastructure;

namespace Foldcraft.Core.Ini;

/// <summary>The encoding a desktop.ini was found in.</summary>
public enum DesktopIniEncoding
{
    /// <summary>A new document with no source file.</summary>
    None,
    Utf16LittleEndianBom,
    Utf16BigEndianBom,
    Utf16LittleEndianNoBom,
    Utf8Bom,
    Utf8NoBom,
    Ansi,
}

/// <summary>The file cannot be understood without risking the loss of its content.</summary>
public sealed class DesktopIniFormatException(string message) : Exception(message);

/// <summary>
/// A lossless desktop.ini. The file is held as raw lines, including each line's exact
/// terminator. Only the lines a caller changes are rewritten, so every section, key,
/// comment and oddity Foldcraft does not own survives untouched and in order.
/// </summary>
public sealed class DesktopIni
{
    public const string ShellClassInfoSection = ".ShellClassInfo";
    public const string IconResourceKey = "IconResource";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16Le = new(false, false, true);
    private static readonly UnicodeEncoding StrictUtf16Be = new(true, false, true);
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];

    private readonly List<Line> _lines;

    private DesktopIni(List<Line> lines, DesktopIniEncoding encoding)
    {
        _lines = lines;
        SourceEncoding = encoding;
    }

    /// <summary>The encoding the document was read from. It is always written as UTF-16 LE.</summary>
    public DesktopIniEncoding SourceEncoding { get; }

    /// <summary>An empty document for a folder that has no desktop.ini yet.</summary>
    public static DesktopIni CreateEmpty() => new([], DesktopIniEncoding.None);

    /// <summary>Decodes raw file bytes.</summary>
    /// <param name="bytes">The whole file.</param>
    /// <param name="ansiCodePage">Overrides the system ANSI code page. Used by tests.</param>
    /// <exception cref="DesktopIniFormatException">
    /// The bytes cannot be decoded and re-encoded faithfully. Refusing is the safe outcome,
    /// because rewriting such a file would silently change it.
    /// </exception>
    public static DesktopIni Parse(byte[] bytes, int? ansiCodePage = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var (text, encoding) = Decode(bytes, ansiCodePage);
        return new DesktopIni(SplitLines(text), encoding);
    }

    /// <summary>Reads desktop.ini from disk.</summary>
    public static DesktopIni Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>The first value of a key, or null. Section and key names ignore case.</summary>
    public string? GetValue(string section, string key)
    {
        var index = FindKey(section, key);
        return index < 0 ? null : _lines[index].Value;
    }

    /// <summary>Sets a key, editing only that line, or adds it if it is missing.</summary>
    public void SetValue(string section, string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(section);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        if (value.Contains('\r') || value.Contains('\n'))
        {
            throw new ArgumentException("A value cannot span lines.", nameof(value));
        }

        var existing = FindKey(section, key);

        if (existing >= 0)
        {
            _lines[existing] = _lines[existing].WithValue(value);
            return;
        }

        var newline = DominantNewline();
        var header = FindSection(section);

        if (header < 0)
        {
            TerminateLastLine(newline);
            _lines.Add(Line.Classify($"[{section}]", newline));
            _lines.Add(Line.Classify($"{key}={value}", newline));
            return;
        }

        var insertAt = header + 1;

        for (var i = header + 1; i < _lines.Count && _lines[i].Kind != LineKind.Section; i++)
        {
            if (_lines[i].Kind != LineKind.Blank)
            {
                insertAt = i + 1;
            }
        }

        if (_lines[insertAt - 1].Terminator.Length == 0)
        {
            _lines[insertAt - 1] = _lines[insertAt - 1] with { Terminator = newline };
        }

        _lines.Insert(insertAt, Line.Classify($"{key}={value}", newline));
    }

    /// <summary>The document as text, identical to the decoded source apart from edits.</summary>
    public string ToText()
    {
        var builder = new StringBuilder();

        foreach (var line in _lines)
        {
            builder.Append(line.Text).Append(line.Terminator);
        }

        return builder.ToString();
    }

    /// <summary>The bytes Foldcraft writes: UTF-16 LE with a BOM, so non-ASCII paths survive.</summary>
    public byte[] ToBytes()
    {
        var body = StrictUtf16Le.GetBytes(ToText());
        var bytes = new byte[Utf16LeBom.Length + body.Length];
        Utf16LeBom.CopyTo(bytes, 0);
        body.CopyTo(bytes, Utf16LeBom.Length);
        return bytes;
    }

    /// <summary>
    /// Writes to <paramref name="path"/> atomically. An existing file keeps its attributes
    /// unless <paramref name="attributes"/> says otherwise.
    /// </summary>
    public void Save(string path, FileAttributes? attributes = null, FileOperations? operations = null)
    {
        var final = attributes
            ?? (File.Exists(path) ? File.GetAttributes(path) : FileAttributes.Hidden | FileAttributes.System);

        AtomicFile.Write(path, ToBytes(), final, operations ?? new FileOperations());
    }

    private int FindSection(string section)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Kind == LineKind.Section && section.Equals(_lines[i].Name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindKey(string section, string key)
    {
        var header = FindSection(section);

        if (header < 0)
        {
            return -1;
        }

        for (var i = header + 1; i < _lines.Count && _lines[i].Kind != LineKind.Section; i++)
        {
            if (_lines[i].Kind == LineKind.Key && key.Equals(_lines[i].Name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private string DominantNewline()
    {
        var counts = _lines
            .Where(l => l.Terminator.Length > 0)
            .GroupBy(l => l.Terminator)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return counts?.Key ?? "\r\n";
    }

    private void TerminateLastLine(string newline)
    {
        if (_lines.Count > 0 && _lines[^1].Terminator.Length == 0)
        {
            _lines[^1] = _lines[^1] with { Terminator = newline };
        }
    }

    private static (string Text, DesktopIniEncoding Encoding) Decode(byte[] bytes, int? ansiCodePage)
    {
        try
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            {
                return (StrictUtf16Le.GetString(bytes, 2, bytes.Length - 2), DesktopIniEncoding.Utf16LittleEndianBom);
            }

            if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            {
                return (StrictUtf16Be.GetString(bytes, 2, bytes.Length - 2), DesktopIniEncoding.Utf16BigEndianBom);
            }

            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            {
                return (StrictUtf8.GetString(bytes, 3, bytes.Length - 3), DesktopIniEncoding.Utf8Bom);
            }

            if (Array.IndexOf(bytes, (byte)0) >= 0)
            {
                return DecodeUtf16WithoutBom(bytes);
            }
        }
        catch (DecoderFallbackException)
        {
            throw new DesktopIniFormatException("desktop.ini declares an encoding its bytes do not match.");
        }

        var codePage = ansiCodePage ?? (int)NativeMethods.GetACP();

        // Windows reads a file with no BOM as ANSI. Double-byte text is easily valid UTF-8 by
        // accident, and that wrong guess would become permanent when the file is rewritten.
        if (!Ascii.IsValid(bytes) && codePage != Encoding.UTF8.CodePage && !AnsiEncoding(codePage).IsSingleByte)
        {
            return DecodeAnsi(bytes, codePage);
        }

        try
        {
            return (StrictUtf8.GetString(bytes), DesktopIniEncoding.Utf8NoBom);
        }
        catch (DecoderFallbackException)
        {
            // A system running UTF-8 as its ANSI code page cannot be the source of invalid UTF-8.
            return DecodeAnsi(bytes, codePage == Encoding.UTF8.CodePage ? 1252 : codePage);
        }
    }

    private static (string, DesktopIniEncoding) DecodeUtf16WithoutBom(byte[] bytes)
    {
        if (bytes.Length % 2 != 0)
        {
            throw new DesktopIniFormatException("desktop.ini contains binary data.");
        }

        var text = StrictUtf16Le.GetString(bytes);

        if (text.Contains('\0'))
        {
            throw new DesktopIniFormatException("desktop.ini contains binary data.");
        }

        return (text, DesktopIniEncoding.Utf16LittleEndianNoBom);
    }

    private static Encoding AnsiEncoding(int codePage)
    {
        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new DesktopIniFormatException($"Code page {codePage} is not available.");
        }
    }

    private static (string, DesktopIniEncoding) DecodeAnsi(byte[] bytes, int codePage)
    {
        var encoding = AnsiEncoding(codePage);
        var text = encoding.GetString(bytes);

        // Rewriting would corrupt bytes the code page cannot represent, so refuse those files.
        if (!encoding.GetBytes(text).AsSpan().SequenceEqual(bytes))
        {
            throw new DesktopIniFormatException("desktop.ini contains bytes that are invalid in the system code page.");
        }

        return (text, DesktopIniEncoding.Ansi);
    }

    private static List<Line> SplitLines(string text)
    {
        var lines = new List<Line>();
        var start = 0;

        while (start < text.Length)
        {
            var end = text.AsSpan(start).IndexOfAny('\r', '\n');

            if (end < 0)
            {
                lines.Add(Line.Classify(text[start..], string.Empty));
                break;
            }

            end += start;
            var length = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1;
            lines.Add(Line.Classify(text[start..end], text.Substring(end, length)));
            start = end + length;
        }

        return lines;
    }

    private enum LineKind
    {
        Blank,
        Comment,
        Section,
        Key,
        Other,
    }

    private sealed record Line(string Text, string Terminator, LineKind Kind, string? Name)
    {
        public string Value => Text[(Text.IndexOf('=') + 1)..].Trim();

        /// <summary>Keeps the original key spelling and indentation.</summary>
        public Line WithValue(string value) => this with { Text = Text[..(Text.IndexOf('=') + 1)] + value };

        public static Line Classify(string text, string terminator)
        {
            var trimmed = text.Trim();

            if (trimmed.Length == 0)
            {
                return new Line(text, terminator, LineKind.Blank, null);
            }

            if (trimmed[0] == ';')
            {
                return new Line(text, terminator, LineKind.Comment, null);
            }

            if (trimmed[0] == '[')
            {
                var close = trimmed.IndexOf(']');

                if (close > 0)
                {
                    return new Line(text, terminator, LineKind.Section, trimmed[1..close].Trim());
                }
            }

            var equals = text.IndexOf('=');

            return equals > 0
                ? new Line(text, terminator, LineKind.Key, text[..equals].Trim())
                : new Line(text, terminator, LineKind.Other, null);
        }
    }
}
