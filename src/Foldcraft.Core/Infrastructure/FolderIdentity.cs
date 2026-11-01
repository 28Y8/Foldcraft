using System.Runtime.InteropServices;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Infrastructure;

/// <summary>
/// Which folder a path leads to right now: the volume's serial number plus the file system's
/// ID for the folder. Unlike a path it stays the same across a rename, and it changes when a
/// folder is deleted and another is created in its place.
/// </summary>
internal static class FolderIdentity
{
    private const int FileIdInfoClass = 18;

    /// <summary>The identity, or null where the file system doesn't offer one.</summary>
    public static string? Read(string folderPath)
    {
        using var handle = NativeMethods.CreateFile(
            PathResolver.ToExtendedPath(folderPath),
            NativeMethods.FILE_READ_ATTRIBUTES,
            NativeMethods.FILE_SHARE_ALL,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS | NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid ||
            !NativeMethods.GetFileInformationByHandleEx(
                handle, FileIdInfoClass, out NativeMethods.FileIdInfo info, (uint)Marshal.SizeOf<NativeMethods.FileIdInfo>()))
        {
            return null;
        }

        return $"{info.VolumeSerialNumber:x16}-{info.FileIdHigh:x16}{info.FileIdLow:x16}";
    }
}
