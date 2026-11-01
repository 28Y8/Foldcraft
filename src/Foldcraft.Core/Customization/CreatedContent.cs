using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Journal;

namespace Foldcraft.Core.Customization;

/// <summary>A file to put in a new folder.</summary>
/// <param name="Name">A plain file name.</param>
/// <param name="Bytes">Its exact content.</param>
public sealed record NewFolderFile(string Name, byte[] Bytes);

/// <summary>What became of a folder Foldcraft made.</summary>
internal enum CreatedContentFate
{
    /// <summary>It was still exactly what Foldcraft made, so it's gone.</summary>
    Removed,

    /// <summary>Something in it was added or changed, so everything in it stayed.</summary>
    Kept,

    /// <summary>Another folder has taken its place, so nothing was touched.</summary>
    DifferentFolder,

    /// <summary>It was unchanged, but Windows refused. What's left is harmless.</summary>
    CouldNotRemove,
}

/// <summary>
/// Takes away a folder Foldcraft made, under the strictest rule in the app: all of it or none of
/// it. The folder goes only if it holds nothing but the recorded files, byte for byte. One
/// extra file, or one edited shortcut, means nothing in it is touched, shortcut included,
/// because at that point the folder belongs to the person using it.
/// </summary>
internal static class CreatedContent
{
    /// <summary>A shortcut is a few KB, so anything bigger isn't the one Foldcraft wrote.</summary>
    private const long MaxRecordedFileBytes = 1024 * 1024;

    private static readonly EnumerationOptions EveryEntry = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// True when this really is the folder that was made. The record gets the folder's ID as soon
    /// as the folder exists, so a record without one may date from before the folder was made, or
    /// from a file system that has no IDs. Then only something Foldcraft put there can show it.
    /// An empty folder of that name proves nothing, and neither does one holding someone else's files.
    /// </summary>
    public static bool IsShownToBeOurs(ChangeRecord change)
    {
        if (change.FolderId is not null)
        {
            return true;
        }

        try
        {
            return AtomicFile.HasStaleTemps(change.FolderPath) ||
                Directory.EnumerateFiles(change.FolderPath, "*", EveryEntry).Any(file => IsRecordedAndUnchanged(file, change)) ||
                FolderState.Capture(change.FolderPath).DesktopIniSha256 == change.WrittenSha256;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnusableDesktopIniException)
        {
            return false;
        }
    }

    /// <summary>
    /// Call this after the look has come off, so desktop.ini is out of the way, and only for a
    /// folder that <see cref="IsShownToBeOurs"/> or that this run just made.
    /// </summary>
    public static CreatedContentFate Remove(ChangeRecord change, FileOperations ops)
    {
        var folder = change.FolderPath;

        if (!Directory.Exists(folder))
        {
            return CreatedContentFate.Removed;
        }

        if (change.FolderId is { } recorded && FolderIdentity.Read(folder) is { } current && current != recorded)
        {
            return CreatedContentFate.DifferentFolder;
        }

        try
        {
            AtomicFile.SweepStaleTemps(folder);

            var ours = new List<string>();

            foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", EveryEntry))
            {
                if (!IsRecordedAndUnchanged(entry, change))
                {
                    return CreatedContentFate.Kept;
                }

                ours.Add(entry);
            }

            foreach (var file in ours)
            {
                ops.SetAttributes(file, FileAttributes.Normal);
                ops.DeleteFile(file);
            }

            ops.DeleteEmptyDirectory(folder);
            return CreatedContentFate.Removed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CreatedContentFate.CouldNotRemove;
        }
    }

    private static bool IsRecordedAndUnchanged(string path, ChangeRecord change)
    {
        // This starts from what's really in the folder, so a recorded name can't reach outside it.
        var name = Path.GetFileName(path);
        var recorded = change.CreatedFiles.FirstOrDefault(file => file.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (recorded is null)
        {
            return false;
        }

        var info = new FileInfo(path);

        return info.Exists &&
            (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0 &&
            info.Length <= MaxRecordedFileBytes &&
            FolderState.Sha256Hex(File.ReadAllBytes(path)) == recorded.Sha256;
    }
}
