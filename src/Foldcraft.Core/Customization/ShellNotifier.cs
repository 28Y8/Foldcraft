using Foldcraft.Core.Infrastructure;

namespace Foldcraft.Core.Customization;

/// <summary>Tells the shell that a folder's appearance changed.</summary>
public interface IShellNotifier
{
    void FolderChanged(string folderPath);
}

/// <summary>
/// Sends SHCNE_UPDATEITEM for the folder and SHCNE_UPDATEDIR for its parent so an open Explorer
/// window redraws. It does not restart Explorer, and it never waits for the shell.
/// </summary>
public sealed class ShellNotifier : IShellNotifier
{
    public void FolderChanged(string folderPath)
    {
        const uint flags = NativeMethods.SHCNF_PATHW | NativeMethods.SHCNF_FLUSHNOWAIT;

        // In this order: a parent notice just after the item's can swallow it, and the view skips the folder.
        var parent = Path.GetDirectoryName(folderPath);

        if (parent is not null)
        {
            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_UPDATEDIR, flags, parent, IntPtr.Zero);
        }

        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_UPDATEITEM, flags, folderPath, IntPtr.Zero);
    }
}