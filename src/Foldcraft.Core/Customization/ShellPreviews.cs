using System.Runtime.InteropServices;

namespace Foldcraft.Core.Customization;

/// <summary>What the shell has saved about a folder's picture.</summary>
public interface IShellPreviews
{
    /// <summary>True when the shell tried to draw this folder at its current date, failed and saved that.</summary>
    bool HasSavedFailure(string folderPath);
}

/// <summary>
/// Asks the shell's thumbnail cache with WTS_INCACHEONLY, the documented way to look without
/// drawing. Nothing is drawn or saved and no file in the folder is read, so a cloud-synced
/// folder has no reason to download anything.
/// </summary>
public sealed class ShellPreviews : IShellPreviews
{
    private const uint WTS_INCACHEONLY = 0x2;
    private const int WTS_E_FAILEDEXTRACTION = unchecked((int)0x8004B200);

    /// <summary>A failure is kept for the folder, not per size, so any size gets the same answer.</summary>
    private const uint AnySize = 256;

    private static readonly TimeSpan MostWaited = TimeSpan.FromSeconds(10);
    private static readonly Guid LocalThumbnailCache = new("50EF4544-AC9F-4A8E-B21B-8A26180DB13F");
    private static readonly Guid ShellItemId = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    /// <summary>When in doubt the answer is no, so a folder is only ever touched on a clear answer.</summary>
    public bool HasSavedFailure(string folderPath)
    {
        var failed = false;

        // Shell objects expect a thread of this kind, and one that hangs must not hold anything up.
        var thread = new Thread(() => failed = Ask(folderPath)) { IsBackground = true, Name = "Foldcraft preview check" };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return thread.Join(MostWaited) && failed;
    }

    private static bool Ask(string folderPath)
    {
        object? item = null;
        object? cache = null;
        var bitmap = IntPtr.Zero;

        try
        {
            if (SHCreateItemFromParsingName(folderPath, IntPtr.Zero, ShellItemId, out item) != 0)
            {
                return false;
            }

            cache = Activator.CreateInstance(Type.GetTypeFromCLSID(LocalThumbnailCache, throwOnError: true)!);

            return ((IThumbnailCache)cache!).GetThumbnail((IShellItem)item, AnySize, WTS_INCACHEONLY, out bitmap, out _, IntPtr.Zero) == WTS_E_FAILEDEXTRACTION;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            return false;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                Marshal.Release(bitmap);
            }

            if (cache is not null)
            {
                Marshal.ReleaseComObject(cache);
            }

            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        in Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object item);

    /// <summary>Only ever passed along, so none of its methods are needed.</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    private interface IShellItem;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("F676C15D-596A-4ce2-8234-33996F445DB1")]
    private interface IThumbnailCache
    {
        [PreserveSig]
        int GetThumbnail(
            IShellItem item,
            uint size,
            uint flags,
            out IntPtr bitmap,
            out uint cacheFlags,
            IntPtr thumbnailId);
    }
}
