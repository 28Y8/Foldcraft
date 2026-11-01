using System.ComponentModel;
using Foldcraft.Core.Infrastructure;

namespace Foldcraft.Core.Safety;

/// <summary>
/// Turns path text into one canonical spelling, so that safety decisions cannot be dodged
/// by writing the same location differently.
/// </summary>
internal static class PathResolver
{
    /// <summary>
    /// Normalizes spelling only and touches no disk: forward slashes, \\?\ and \\.\ prefixes,
    /// ".." segments, trailing separators, dots and spaces. Returns null with a refusal when
    /// the text is not a plain absolute folder path. Trimming a dot or space can land on a
    /// different folder, so anything that acts on a path also checks <see cref="HasTrimmedName"/>.
    /// </summary>
    public static string? Normalize(string? input, out FolderRefusal? refusal, out string? reason)
    {
        refusal = null;
        reason = null;

        if (string.IsNullOrWhiteSpace(input) || input.Contains('\0'))
        {
            return Refuse(FolderRefusal.InvalidPath, "The path is empty or contains invalid characters.", out refusal, out reason);
        }

        var path = input.Replace('/', '\\');

        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            return Refuse(FolderRefusal.UnsupportedPathForm, "NT object paths are not supported.", out refusal, out reason);
        }

        while (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            var rest = path[4..];

            if (rest.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
            {
                path = @"\\" + rest[4..];
            }
            else if (rest.Length >= 2 && char.IsAsciiLetter(rest[0]) && rest[1] == ':')
            {
                path = rest.Length == 2 ? rest + '\\' : rest;
            }
            else if (rest.StartsWith(@"\\?\", StringComparison.Ordinal) || rest.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                path = rest;
            }
            else
            {
                return Refuse(FolderRefusal.UnsupportedPathForm, "Volume GUID and device paths are not supported.", out refusal, out reason);
            }
        }

        if (!Path.IsPathFullyQualified(path))
        {
            return Refuse(FolderRefusal.InvalidPath, "The path must be absolute.", out refusal, out reason);
        }

        // Anything past the drive colon would name an alternate data stream.
        if (path.IndexOf(':', path.StartsWith(@"\\", StringComparison.Ordinal) ? 0 : 2) >= 0)
        {
            return Refuse(FolderRefusal.InvalidPath, "The path contains an alternate data stream.", out refusal, out reason);
        }

        try
        {
            return TrimEnd(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Refuse(FolderRefusal.InvalidPath, $"The path is not valid: {ex.Message}", out refusal, out reason);
        }
    }

    /// <summary>
    /// True when a name in the path ends in a dot or space. NTFS allows such names, but Windows
    /// removes the ending almost everywhere, so "Report." would quietly mean "Report".
    /// </summary>
    public static bool HasTrimmedName(string path)
    {
        foreach (var segment in path.Split('\\', '/'))
        {
            if (segment is not ("" or "." or "..") && segment[^1] is '.' or ' ')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Asks Windows for the true path of an existing folder, which expands 8.3 short names,
    /// corrects case and resolves SUBST drives. Reparse points are opened, not followed.
    /// </summary>
    public static string ResolveFinalPath(string normalizedPath)
    {
        using var handle = NativeMethods.CreateFile(
            ToExtendedPath(normalizedPath),
            NativeMethods.FILE_READ_ATTRIBUTES,
            NativeMethods.FILE_SHARE_ALL,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS | NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception();
        }

        var buffer = new char[1024];

        while (true)
        {
            var length = NativeMethods.GetFinalPathNameByHandle(
                handle, buffer, (uint)buffer.Length, NativeMethods.VOLUME_NAME_DOS);

            if (length == 0)
            {
                throw new Win32Exception();
            }

            if (length < buffer.Length)
            {
                return TrimEnd(StripExtendedPrefix(new string(buffer, 0, (int)length)));
            }

            buffer = new char[length + 1];
        }
    }

    /// <summary>True when <paramref name="path"/> equals <paramref name="root"/> or lies inside it.</summary>
    public static bool IsSameOrUnder(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) || IsStrictlyUnder(path, root);

    /// <summary>True when <paramref name="path"/> lies inside <paramref name="root"/>.</summary>
    public static bool IsStrictlyUnder(string path, string root)
    {
        var prefix = root.EndsWith('\\') ? root : root + '\\';
        return path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNetworkPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);

    public static bool IsRoot(string path) =>
        string.Equals(Path.GetPathRoot(path)?.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public static string ToExtendedPath(string path) =>
        IsNetworkPath(path) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;

    private static string StripExtendedPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }

        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    private static string TrimEnd(string path)
    {
        var root = Path.GetPathRoot(path);
        return string.IsNullOrEmpty(root) || path.Length <= root.Length ? path : path.TrimEnd('\\');
    }

    private static string? Refuse(FolderRefusal code, string message, out FolderRefusal? refusal, out string? reason)
    {
        refusal = code;
        reason = message;
        return null;
    }
}
