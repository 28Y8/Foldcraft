using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Apps;

/// <summary>
/// The one gate every path from a launcher's file or a shortcut goes through before Foldcraft
/// opens it. Those files are outside Foldcraft's control, and a network path is the dangerous
/// one: opening it would send this PC's sign-in to whoever runs that server.
/// </summary>
internal static class LocalSource
{
    /// <summary>The path in plain form if it's a full path on a local drive, otherwise null.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl))
        {
            return null;
        }

        var normalized = PathResolver.Normalize(path, out _, out _);

        if (normalized is null || PathResolver.IsNetworkPath(normalized))
        {
            return null;
        }

        return NativeMethods.GetDriveType(Path.GetPathRoot(normalized)!) is NativeMethods.DRIVE_REMOTE or NativeMethods.DRIVE_NO_ROOT_DIR
            ? null
            : normalized;
    }

    /// <summary>Lists one folder level without following anything, and shrugs off what can't be read.</summary>
    public static IEnumerable<string> FilesIn(string folder, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(folder, pattern, new EnumerationOptions { IgnoreInaccessible = true }).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>The real subfolders of one folder. Junctions and links are never entered.</summary>
    public static IEnumerable<string> FoldersIn(string folder)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            return Directory.EnumerateDirectories(folder, "*", options).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}
