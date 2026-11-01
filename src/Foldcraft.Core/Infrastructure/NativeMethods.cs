using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Foldcraft.Core.Infrastructure;

internal static class NativeMethods
{
    internal const uint FILE_READ_ATTRIBUTES = 0x0080;
    internal const uint FILE_SHARE_ALL = 0x1 | 0x2 | 0x4;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    internal const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    internal const uint VOLUME_NAME_DOS = 0;

    internal const int ERROR_ALREADY_EXISTS = 183;

    internal const uint DRIVE_NO_ROOT_DIR = 1;
    internal const uint DRIVE_REMOTE = 4;

    internal const int SHCNE_UPDATEDIR = 0x00001000;
    internal const int SHCNE_UPDATEITEM = 0x00002000;
    internal const uint SHCNF_PATHW = 0x0005;
    internal const uint SHCNF_FLUSHNOWAIT = 0x3000;


    internal static readonly Guid FOLDERID_ProgramFilesX64 = new("6D809377-6AF0-444b-8957-A3773F02200E");

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    internal static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        [Out] char[] filePath,
        uint filePathLength,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int informationClass,
        out FileIdInfo information,
        uint bufferSize);

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int informationClass,
        out FileAttributeTagInfo information,
        uint bufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateDirectoryW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateDirectory(string pathName, IntPtr securityAttributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDriveTypeW")]
    internal static extern uint GetDriveType(string rootPathName);

    [DllImport("kernel32.dll")]
    internal static extern uint GetACP();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

    [DllImport("shell32.dll", PreserveSig = true)]
    internal static extern int SHGetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
