using System.Text.RegularExpressions;

namespace Foldcraft.Core.Infrastructure;

/// <summary>
/// Replaces a file without ever exposing a partial version of it: the new content is
/// written to a temp file in the same folder, flushed, then swapped in. If any step fails
/// the original file keeps its bytes and its attributes.
/// </summary>
internal static partial class AtomicFile
{
    private const string TempPrefix = ".foldcraft-";
    private const string TempSuffix = ".tmp";

    public static void Write(string path, byte[] bytes, FileAttributes finalAttributes, FileOperations ops)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The path has no parent directory.", nameof(path));
        var tempPath = Path.Combine(directory, $"{TempPrefix}{Guid.NewGuid():N}{TempSuffix}");

        try
        {
            ops.WriteNewFile(tempPath, bytes);

            var exists = File.Exists(path);
            var original = exists ? File.GetAttributes(path) : (FileAttributes?)null;

            // ReplaceFile refuses a read-only destination. Hidden and System do not stop it.
            var loosened = original is { } attrs && attrs.HasFlag(FileAttributes.ReadOnly);

            if (loosened)
            {
                ops.SetAttributes(path, original!.Value & ~FileAttributes.ReadOnly);
            }

            try
            {
                ops.CommitReplace(tempPath, path, exists);
            }
            catch
            {
                if (loosened)
                {
                    TryRestoreAttributes(path, original!.Value, ops);
                }

                throw;
            }

            ops.SetAttributes(path, finalAttributes);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// Removes temp files a crash left behind. Only names this class generates match, so
    /// no user file can be selected.
    /// </summary>
    public static void SweepStaleTemps(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, TempPrefix + "*" + TempSuffix))
        {
            if (TempName().IsMatch(Path.GetFileName(file)))
            {
                TryDelete(file);
            }
        }
    }

    /// <summary>True when a crash left one of this class's temp files here, which nothing else can name.</summary>
    public static bool HasStaleTemps(string directory) =>
        Directory.EnumerateFiles(directory, TempPrefix + "*" + TempSuffix).Any(file => TempName().IsMatch(Path.GetFileName(file)));

    private static void TryRestoreAttributes(string path, FileAttributes attributes, FileOperations ops)
    {
        try
        {
            if (File.Exists(path))
            {
                ops.SetAttributes(path, attributes);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The caller is already failing with the original error, which matters more.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is harmless, and restoring the folder sweeps it.
        }
    }

    [GeneratedRegex(@"^\.foldcraft-[0-9a-f]{32}\.tmp$")]
    private static partial Regex TempName();
}
