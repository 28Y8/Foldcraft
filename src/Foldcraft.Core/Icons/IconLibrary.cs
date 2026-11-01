using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Ini;
using Foldcraft.Core.Journal;

namespace Foldcraft.Core.Icons;

/// <summary>The condition of a rendered icon file.</summary>
public enum IconFileState
{
    Intact,
    Missing,
    Damaged,
}

/// <summary>
/// The fixed folder of rendered icons. A file's name is the hash of what it depicts, so an
/// icon is never edited: it exists or it does not, and writes are atomic replaces.
/// </summary>
public sealed partial class IconLibrary
{
    private readonly FoldcraftPaths _paths;
    private readonly IconRenderer _renderer;
    private readonly FileOperations _ops;

    public IconLibrary(FoldcraftPaths paths, IconRenderer? renderer = null, FileOperations? operations = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _renderer = renderer ?? new IconRenderer();
        _ops = operations ?? new FileOperations();
    }

    /// <summary>Returns the path of the icon for a spec, rendering it if needed.</summary>
    /// <exception cref="IconRenderException">The spec or its image cannot be used.</exception>
    public string Ensure(FolderIconSpec spec) => EnsureRecorded(spec).Path;

    /// <summary>
    /// Where the icon for a spec belongs, without drawing or writing anything. The answer changes
    /// when the renderer's version does, which is how an older icon is recognized as out of date.
    /// </summary>
    /// <exception cref="IconRenderException">The spec or its image cannot be used.</exception>
    public string ExpectedPath(FolderIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return PathForKey(_renderer.KeyFor(spec).Key);
    }

    /// <summary>The hash of a spec's custom image as it is now, or null if the spec has none.</summary>
    /// <exception cref="IconRenderException">The image can't be read.</exception>
    public string? ImageHash(FolderIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return _renderer.KeyFor(spec).ImageSha256;
    }

    /// <summary>What's wrong with an icon file, if anything.</summary>
    public static IconFileState Inspect(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            return IconFileState.Missing;
        }

        return IsIntact(path) ? IconFileState.Intact : IconFileState.Damaged;
    }

    /// <summary>As <see cref="Ensure"/>, also giving the hash of any custom image used, for the record.</summary>
    /// <exception cref="IconRenderException">The spec or its image cannot be used.</exception>
    internal (string Path, string? ImageSha256) EnsureRecorded(FolderIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var (key, ico, imageHash) = _renderer.RenderWithKey(spec);
        var path = PathForKey(key);

        Directory.CreateDirectory(_paths.IconsDirectory);

        if (!IsIntact(path))
        {
            AtomicFile.Write(path, ico, FileAttributes.Normal, _ops);
        }

        return (path, imageHash);
    }

    /// <summary>
    /// Deletes icons nothing refers to, and is the only code that deletes from the library.
    /// An icon stays if any journal entry that may still be on a folder names it, including
    /// inside a recorded original desktop.ini. If the journal has unreadable records, nothing
    /// is deleted, because an unreadable record could be the one that names an icon.
    /// </summary>
    /// <returns>How many icons were deleted.</returns>
    public int PruneUnused(ChangeJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        if (!Directory.Exists(_paths.IconsDirectory))
        {
            return 0;
        }

        var snapshot = journal.Read();

        if (snapshot.CorruptRecords > 0)
        {
            return 0;
        }

        var inUse = ReferencedIcons(snapshot);
        var deleted = 0;

        foreach (var file in Directory.EnumerateFiles(_paths.IconsDirectory, "*.ico"))
        {
            var name = Path.GetFileName(file);

            if (IconName().IsMatch(name) && !inUse.Contains(name))
            {
                _ops.DeleteFile(file);
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>
    /// File names only: the name is the content hash, so a match still holds after the
    /// profile, and with it the library's directory, has moved.
    /// </summary>
    private static HashSet<string> ReferencedIcons(JournalSnapshot snapshot)
    {
        var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in snapshot.Entries)
        {
            if (entry.State is JournalEntryState.Undone or JournalEntryState.RolledBack)
            {
                continue;
            }

            inUse.Add(Path.GetFileName(entry.Change.IconPath));

            if (entry.Change.OriginalDesktopIni is { } original && IconInDesktopIni(original) is { } previous)
            {
                inUse.Add(Path.GetFileName(previous));
            }
        }

        return inUse;
    }

    private static string? IconInDesktopIni(byte[] bytes)
    {
        try
        {
            var value = DesktopIni.Parse(bytes).GetValue(DesktopIni.ShellClassInfoSection, DesktopIni.IconResourceKey);
            var comma = value?.LastIndexOf(',') ?? -1;
            return comma > 0 ? value![..comma] : value;
        }
        catch (DesktopIniFormatException)
        {
            return null;
        }
    }

    private string PathForKey(string key)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_paths.IconsDirectory, $"{hash[..32]}.ico");
    }

    private static bool IsIntact(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return IcoFile.TryReadFrames(File.ReadAllBytes(path))?.Count == IconRenderer.Sizes.Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked icon is probably being read by Explorer, so leave it alone.
            return true;
        }
    }

    [GeneratedRegex("^[0-9a-f]{32}\\.ico$")]
    private static partial Regex IconName();
}
