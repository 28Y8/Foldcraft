using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Foldcraft.Core.Apps;

/// <summary>What a .lnk file says.</summary>
/// <param name="Target">The program it starts.</param>
/// <param name="Arguments">Its command line arguments, if any.</param>
/// <param name="WorkingDirectory">Where it starts, if set.</param>
/// <param name="IconPath">Where its icon is, if it has its own.</param>
/// <param name="IconIndex">Which icon in that file.</param>
public sealed record ShortcutInfo(
    string Target,
    string? Arguments = null,
    string? WorkingDirectory = null,
    string? IconPath = null,
    int IconIndex = 0);

/// <summary>Reads and builds .lnk files. It's an interface so a failure can be simulated.</summary>
public interface IShortcuts
{
    /// <summary>What a shortcut says, or null if it can't be read. It's never followed or started.</summary>
    ShortcutInfo? Read(string path);

    /// <summary>The bytes of a new .lnk. Nothing is left behind on disk.</summary>
    /// <param name="shortcut">What it should say.</param>
    /// <param name="scratchDirectory">Where the shell can write it for a moment.</param>
    byte[] Build(ShortcutInfo shortcut, string scratchDirectory);
}

/// <summary>
/// The shell's own ShellLink object, which is the only supported way to handle .lnk files. It
/// loads and saves the file and nothing more: Resolve is never called, because that goes
/// looking across drives for a missing target and can rewrite the shortcut.
/// </summary>
public sealed class ShellShortcuts : IShortcuts
{
    private const int MaxPath = 32768;
    private const int ReadOnly = 0;

    public ShortcutInfo? Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        object? link = null;

        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(path, ReadOnly);

            var shell = (IShellLinkW)link;
            var target = new StringBuilder(MaxPath);
            var arguments = new StringBuilder(MaxPath);
            var folder = new StringBuilder(MaxPath);
            var icon = new StringBuilder(MaxPath);

            shell.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            shell.GetArguments(arguments, arguments.Capacity);
            shell.GetWorkingDirectory(folder, folder.Capacity);
            shell.GetIconLocation(icon, icon.Capacity, out var index);

            return target.Length == 0
                ? null
                : new ShortcutInfo(
                    Environment.ExpandEnvironmentVariables(target.ToString()),
                    arguments.Length == 0 ? null : arguments.ToString(),
                    folder.Length == 0 ? null : Environment.ExpandEnvironmentVariables(folder.ToString()),
                    icon.Length == 0 ? null : Environment.ExpandEnvironmentVariables(icon.ToString()),
                    index);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (link is not null)
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }

    public byte[] Build(ShortcutInfo shortcut, string scratchDirectory)
    {
        ArgumentNullException.ThrowIfNull(shortcut);

        Directory.CreateDirectory(scratchDirectory);

        var path = Path.Combine(scratchDirectory, $"{Guid.NewGuid():N}.lnk");
        var link = new ShellLink();

        try
        {
            var shell = (IShellLinkW)link;

            shell.SetPath(shortcut.Target);

            if (shortcut.Arguments is { } arguments)
            {
                shell.SetArguments(arguments);
            }

            if (shortcut.WorkingDirectory is { } folder)
            {
                shell.SetWorkingDirectory(folder);
            }

            if (shortcut.IconPath is { } icon)
            {
                shell.SetIconLocation(icon, shortcut.IconIndex);
            }

            ((IPersistFile)link).Save(path, fRemember: true);
            return File.ReadAllBytes(path);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
            File.Delete(path);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    /// <summary>The order of these methods is the COM interface's, so none may be moved or dropped.</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder folder, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string folder);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
