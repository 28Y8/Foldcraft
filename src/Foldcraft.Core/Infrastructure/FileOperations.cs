using System.ComponentModel;
using System.Runtime.InteropServices;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Infrastructure;

/// <summary>
/// The file-system mutations Foldcraft performs, gathered in one overridable place so
/// tests can inject a failure at any step and prove that nothing is left damaged.
/// </summary>
public class FileOperations
{
    /// <summary>
    /// Bits the file system owns, which SetFileAttributes cannot change. Everything else is
    /// passed through, because a value handed to SetFileAttributes replaces the whole set:
    /// leaving a bit out clears it, which is how Offline, Pinned and Unpinned used to get lost.
    /// </summary>
    private const FileAttributes OwnedByTheFileSystem =
        FileAttributes.Directory | FileAttributes.Device | FileAttributes.SparseFile |
        FileAttributes.ReparsePoint | FileAttributes.Compressed | FileAttributes.Encrypted |
        FileAttributes.IntegrityStream | Virtual | RecallOnOpen | RecallOnDataAccess;

    private const FileAttributes Virtual = (FileAttributes)0x00010000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary>
    /// How far <see cref="TryMoveDateOn"/> moves a date, smallest first. NTFS keeps the first, which
    /// is all the shell needs. exFAT only keeps the second and FAT only keeps the third.
    /// </summary>
    private static readonly TimeSpan[] DateSteps = [TimeSpan.FromTicks(1), TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(2)];

    /// <summary>Creates a new file and forces its bytes to disk. Fails if the file exists.</summary>
    public virtual void WriteNewFile(string path, byte[] bytes)
    {
        using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Moves a fully written temp file over its destination as a single step. An existing
    /// destination goes through ReplaceFile, which keeps its ACL and alternate data streams.
    /// </summary>
    public virtual void CommitReplace(string tempPath, string destinationPath, bool destinationExists)
    {
        if (destinationExists)
        {
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, destinationPath, overwrite: false);
        }
    }

    /// <summary>
    /// Sets a file's or folder's whole attribute value. To change one bit, prefer
    /// <see cref="AddAttributes"/> or <see cref="RemoveAttributes"/>, which keep the rest.
    /// </summary>
    public virtual void SetAttributes(string path, FileAttributes attributes)
    {
        var settable = attributes & ~OwnedByTheFileSystem;
        File.SetAttributes(path, settable == 0 ? FileAttributes.Normal : settable);
    }

    /// <summary>Turns bits on and leaves every other attribute as it is right now.</summary>
    public void AddAttributes(string path, FileAttributes bits) =>
        SetAttributes(path, File.GetAttributes(path) | bits);

    /// <summary>Turns bits off and leaves every other attribute as it is right now.</summary>
    public void RemoveAttributes(string path, FileAttributes bits) =>
        SetAttributes(path, File.GetAttributes(path) & ~bits);

    /// <summary>Sets when a file or folder was last written, which is what Explorer sorts by date on.</summary>
    public virtual void SetLastWriteTimeUtc(string path, DateTime lastWriteUtc)
    {
        if (Directory.Exists(path))
        {
            Directory.SetLastWriteTimeUtc(path, lastWriteUtc);
        }
        else
        {
            File.SetLastWriteTimeUtc(path, lastWriteUtc);
        }
    }

    /// <summary>
    /// Sets a date and shrugs off a failure. A date is only ever cosmetic, so it must never turn
    /// a correct change into a failed one, or a finished restore into a retry.
    /// </summary>
    public void TryKeepLastWriteTimeUtc(string path, DateTime? lastWriteUtc)
    {
        if (lastWriteUtc is not { } time)
        {
            return;
        }

        try
        {
            SetLastWriteTimeUtc(path, time);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Permissions can allow changing a file and still forbid changing its dates.
        }
    }

    /// <summary>
    /// Leaves a folder's date the smallest step past the one given that its file system can keep.
    /// The shell files its saved picture of a folder under the exact date, so the folder gets a
    /// new picture and the one under the old date stays for later. Like
    /// <see cref="TryKeepLastWriteTimeUtc"/>, this shrugs off a failure.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="lastWriteUtc">The date to move on from.</param>
    /// <param name="used">Dates to step over, because the folder had a look on them.</param>
    /// <returns>The date the folder has now, or null if that can't be read.</returns>
    public DateTime? TryMoveDateOn(string folder, DateTime lastWriteUtc, IReadOnlySet<DateTime>? used = null)
    {
        try
        {
            foreach (var step in DateSteps)
            {
                var next = lastWriteUtc;

                for (var tries = 0; tries <= (used?.Count ?? 0); tries++)
                {
                    SetLastWriteTimeUtc(folder, next + step);

                    // Asked, because a coarse file system rounds a small step away without saying so.
                    if (TryReadLastWriteTimeUtc(folder) is not { } kept || kept <= lastWriteUtc)
                    {
                        break;
                    }

                    if (used?.Contains(kept) != true)
                    {
                        return kept;
                    }

                    next = kept;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // As above. A date that stayed where a write left it has still moved on.
        }

        return TryReadLastWriteTimeUtc(folder);
    }

    /// <summary>A folder's date, or null if it can't be read. A date is never worth failing a change for.</summary>
    public DateTime? TryReadLastWriteTimeUtc(string folder)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes a folder, and only if nothing is there yet. Directory.CreateDirectory can't do this:
    /// it quietly succeeds on a folder that exists, so someone else's folder would pass as new.
    /// </summary>
    /// <returns>False when something already has that name.</returns>
    public virtual bool CreateNewDirectory(string path)
    {
        if (NativeMethods.CreateDirectory(PathResolver.ToExtendedPath(path), IntPtr.Zero))
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();

        return error == NativeMethods.ERROR_ALREADY_EXISTS ? false : throw new Win32Exception(error);
    }

    public virtual void DeleteFile(string path) => File.Delete(path);

    /// <summary>Removes an empty directory or unlinks a junction. Fails if it has content.</summary>
    public virtual void DeleteEmptyDirectory(string path) => Directory.Delete(path, recursive: false);
}
