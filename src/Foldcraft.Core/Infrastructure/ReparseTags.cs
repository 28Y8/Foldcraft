using System.Runtime.InteropServices;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Infrastructure;

/// <summary>
/// Which kind of reparse point something is. The attribute alone can't tell a junction from
/// a OneDrive folder, and the path says nothing reliable either, so the tag is read from the
/// file system. Anything that can't be read counts as unknown, and unknown is refused.
/// </summary>
internal static class ReparseTags
{
    internal const uint MountPoint = 0xA0000003;
    internal const uint SymbolicLink = 0xA000000C;

    /// <summary>IO_REPARSE_TAG_CLOUD. Its variants, _1 to _F, differ only in the bits of <see cref="CloudVariantMask"/>.</summary>
    internal const uint Cloud = 0x9000001A;

    /// <summary>IO_REPARSE_TAG_CLOUD_MASK.</summary>
    internal const uint CloudVariantMask = 0x0000F000;

    private const int FileAttributeTagInfoClass = 9;

    /// <summary>
    /// True for a Cloud Files tag. Such an item stays where it is: the tag marks it as managed
    /// by a sync provider, and unlike a junction it never leads anywhere else.
    /// </summary>
    public static bool IsCloud(uint tag) => (tag & ~CloudVariantMask) == Cloud;

    /// <summary>
    /// The tag of an item, opened without following it and without asking a sync provider to
    /// download anything. Null if it can't be read. An item that isn't a reparse point reads as 0.
    /// </summary>
    public static uint? Read(string path)
    {
        using var handle = NativeMethods.CreateFile(
            PathResolver.ToExtendedPath(path),
            NativeMethods.FILE_READ_ATTRIBUTES,
            NativeMethods.FILE_SHARE_ALL,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS | NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (handle.IsInvalid ||
            !NativeMethods.GetFileInformationByHandleEx(
                handle, FileAttributeTagInfoClass, out NativeMethods.FileAttributeTagInfo info, (uint)Marshal.SizeOf<NativeMethods.FileAttributeTagInfo>()))
        {
            return null;
        }

        return ((FileAttributes)info.FileAttributes & FileAttributes.ReparsePoint) == 0 ? 0 : info.ReparseTag;
    }
}
