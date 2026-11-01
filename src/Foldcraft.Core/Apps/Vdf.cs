using System.Text;

namespace Foldcraft.Core.Apps;

/// <summary>One entry in a Valve data file: a value, or a set of children.</summary>
public sealed class VdfNode(string key, string? value = null)
{
    public string Key { get; } = key;

    /// <summary>Null when this entry holds children instead.</summary>
    public string? Value { get; } = value;

    public List<VdfNode> Children { get; } = [];

    /// <summary>The first child with this name. Valve's own readers ignore case.</summary>
    public VdfNode? Child(string key) =>
        Children.FirstOrDefault(child => child.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The text of the first child with this name, or null.</summary>
    public string? Text(string key) => Child(key)?.Value;
}

/// <summary>
/// Reads Valve's KeyValues text format, as used by libraryfolders.vdf and appmanifest files. It
/// is written to survive anything: these files can be half written or edited by hand, so it
/// keeps what it can read, never throws and can't be driven into deep recursion.
/// </summary>
public static class VdfParser
{
    /// <summary>The largest file worth reading. A real library file is a few KB.</summary>
    public const int MaxFileBytes = 8 * 1024 * 1024;

    private const int MaxDepth = 32;

    /// <summary>Everything in the text, under a nameless root.</summary>
    public static VdfNode Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var root = new VdfNode(string.Empty);
        var stack = new Stack<VdfNode>();
        var position = 0;
        string? pendingKey = null;
        var ignoredDepth = 0;

        stack.Push(root);

        while (NextToken(text, ref position, out var token, out var isBrace))
        {
            if (isBrace && token == "{")
            {
                if (ignoredDepth > 0 || stack.Count > MaxDepth)
                {
                    ignoredDepth++;
                }
                else
                {
                    var child = new VdfNode(pendingKey ?? string.Empty);

                    stack.Peek().Children.Add(child);
                    stack.Push(child);
                }

                pendingKey = null;
            }
            else if (isBrace)
            {
                if (ignoredDepth > 0)
                {
                    ignoredDepth--;
                }
                else if (stack.Count > 1)
                {
                    stack.Pop();
                }

                pendingKey = null;
            }
            else if (pendingKey is null)
            {
                pendingKey = token;
            }
            else
            {
                if (ignoredDepth == 0)
                {
                    stack.Peek().Children.Add(new VdfNode(pendingKey, token));
                }

                pendingKey = null;
            }
        }

        return root;
    }

    /// <summary>Reads and parses a file, or returns null if it can't be read or is far too big.</summary>
    public static VdfNode? TryParseFile(string path)
    {
        try
        {
            var info = new FileInfo(path);

            return !info.Exists || info.Length > MaxFileBytes ? null : Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool NextToken(string text, ref int position, out string token, out bool isBrace)
    {
        token = string.Empty;
        isBrace = false;

        while (position < text.Length)
        {
            var c = text[position];

            // A NUL means binary junk, which is no reason to stop reading what's around it.
            if (char.IsWhiteSpace(c) || c is '﻿' or '\0')
            {
                position++;
            }
            else if (c == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                SkipLine(text, ref position);
            }
            else if (c == '[')
            {
                // A platform condition such as [$WIN32], which belongs to the value before it.
                SkipPast(text, ref position, ']');
            }
            else if (c is '{' or '}')
            {
                token = c.ToString();
                isBrace = true;
                position++;
                return true;
            }
            else if (c == '"')
            {
                position++;
                return ReadQuoted(text, ref position, out token);
            }
            else
            {
                token = ReadBare(text, ref position);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A quote that never closes loses that one token. A backslash only means something before
    /// a character the format escapes, because older files hold bare Windows paths.
    /// </summary>
    private static bool ReadQuoted(string text, ref int position, out string token)
    {
        var builder = new StringBuilder();

        while (position < text.Length)
        {
            var c = text[position++];

            if (c == '"')
            {
                token = builder.ToString();
                return true;
            }

            if (c == '\\' && position < text.Length && text[position] is '\\' or '"' or 'n' or 't')
            {
                builder.Append(text[position++] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    var escaped => escaped,
                });
            }
            else
            {
                builder.Append(c);
            }
        }

        token = string.Empty;
        return false;
    }

    private static string ReadBare(string text, ref int position)
    {
        var start = position;

        while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}' or '"'))
        {
            position++;
        }

        return text[start..position];
    }

    private static void SkipLine(string text, ref int position)
    {
        while (position < text.Length && text[position] != '\n')
        {
            position++;
        }
    }

    private static void SkipPast(string text, ref int position, char end)
    {
        while (position < text.Length && text[position] != end && text[position] != '\n')
        {
            position++;
        }

        position = Math.Min(position + 1, text.Length);
    }
}
