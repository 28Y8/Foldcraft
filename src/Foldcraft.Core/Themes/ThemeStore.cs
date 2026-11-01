using Foldcraft.Core.Icons;
using Foldcraft.Core.Infrastructure;

namespace Foldcraft.Core.Themes;

/// <summary>What an export left out.</summary>
public sealed record ThemeExportResult(int Exported, int PicturesLeftOut);

/// <summary>
/// The themes saved on this PC, in one file in the data folder, written atomically. If that file
/// can't be read it's set aside rather than overwritten, so a saved theme is never lost to a
/// bad read. Everything this class writes reads back: a file from outside is judged by
/// <see cref="ThemeFile.Read"/> on the way in, and after that it's the app's own.
/// This holds looks, not folders: nothing here ever touches a folder.
/// </summary>
public sealed class ThemeStore
{
    private static readonly string TooMany = $"Foldcraft can keep up to {ThemeFile.MaxThemes} saved themes.";

    private readonly FoldcraftPaths _paths;
    private readonly FileOperations _ops;
    private readonly Lock _gate = new();
    private List<FolderTheme>? _saved;

    public ThemeStore(FoldcraftPaths paths, FileOperations? operations = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _ops = operations ?? new FileOperations();
    }

    /// <summary>A note about the saved themes, when something went wrong reading them.</summary>
    public string? LoadProblem { get; private set; }

    /// <summary>The built-in themes, then the saved ones.</summary>
    public IReadOnlyList<FolderTheme> All
    {
        get
        {
            lock (_gate)
            {
                return [.. BuiltInThemes.All, .. Saved()];
            }
        }
    }

    public FolderTheme? Find(string id) => All.FirstOrDefault(theme => theme.Id == id);

    /// <summary>Saves a look under a name. The same name replaces the look saved under it.</summary>
    /// <exception cref="LookFormatException">The name or the look can't be used.</exception>
    public FolderTheme Save(string name, FolderIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var theme = Checked(name, spec);

        lock (_gate)
        {
            var saved = Saved().ToList();
            var index = saved.FindIndex(t => t.Name.Equals(theme.Name, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                theme = theme with { Id = saved[index].Id };
                saved[index] = theme;
            }
            else if (saved.Count >= ThemeFile.MaxThemes)
            {
                throw new LookFormatException(TooMany);
            }
            else
            {
                saved.Add(theme);
            }

            Persist(saved);
            _saved = saved;
            return theme;
        }
    }

    /// <summary>Deletes a saved theme. Built-in themes stay.</summary>
    public bool Delete(string id)
    {
        lock (_gate)
        {
            var saved = Saved();

            if (saved.RemoveAll(theme => theme.Id == id) == 0)
            {
                return false;
            }

            Persist(saved);
            return true;
        }
    }

    /// <summary>Adds every theme in a file, or none of them.</summary>
    /// <exception cref="LookFormatException">The file can't be used. The message says why.</exception>
    public IReadOnlyList<FolderTheme> Import(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var imported = ThemeFile.Read(ReadLimited(path));

        lock (_gate)
        {
            var saved = Saved().ToList();

            if (saved.Count + imported.Count > ThemeFile.MaxThemes)
            {
                throw new LookFormatException(TooMany);
            }

            var added = new List<FolderTheme>(imported.Count);

            // Names are made unique against each other too, and an id from a file is never kept,
            // because it could be the id of a theme a rule already uses.
            foreach (var theme in imported)
            {
                var unique = theme with { Id = ThemeFile.NewId(), Name = UniqueName(theme.Name, saved) };
                saved.Add(unique);
                added.Add(unique);
            }

            Persist(saved);
            _saved = saved;
            return added;
        }
    }

    /// <summary>Writes themes to a file to share. Their pictures stay behind, and the result says how many.</summary>
    public ThemeExportResult Export(IReadOnlyCollection<FolderTheme> themes, string path)
    {
        ArgumentNullException.ThrowIfNull(themes);
        ArgumentNullException.ThrowIfNull(path);

        AtomicFile.Write(Path.GetFullPath(path), ThemeFile.Write(themes, forThisPc: false), FileAttributes.Normal, _ops);

        return new ThemeExportResult(themes.Count, themes.Count(theme => theme.Spec.CustomImagePath is not null));
    }

    private static FolderTheme Checked(string name, FolderIconSpec spec)
    {
        var trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length == 0 || trimmed.Length > ThemeFile.MaxNameLength || trimmed.Any(char.IsControl))
        {
            throw new LookFormatException($"A theme needs a name of up to {ThemeFile.MaxNameLength} characters.");
        }

        try
        {
            spec.Validate();
        }
        catch (IconRenderException ex)
        {
            throw new LookFormatException(ex.Message);
        }

        return new FolderTheme { Id = ThemeFile.NewId(), Name = trimmed, Mood = "Saved", Spec = spec };
    }

    private static string UniqueName(string name, List<FolderTheme> saved)
    {
        bool Taken(string candidate) => saved.Any(t => t.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase));

        if (!Taken(name))
        {
            return name;
        }

        for (var i = 2; ; i++)
        {
            var suffix = $" {i}";
            var candidate = name[..Math.Min(name.Length, ThemeFile.MaxNameLength - suffix.Length)] + suffix;

            if (!Taken(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>The size is checked from the open file, so a huge one is never read at all.</summary>
    private static byte[] ReadLimited(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            if (stream.Length > ThemeFile.MaxFileBytes)
            {
                throw new LookFormatException("This file is too big to be a theme file.");
            }

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new LookFormatException("This file couldn't be read.");
        }
    }

    private List<FolderTheme> Saved()
    {
        if (_saved is not null)
        {
            return _saved;
        }

        _saved = [];
        var path = _paths.ThemesFile;

        if (!File.Exists(path))
        {
            return _saved;
        }

        try
        {
            var (themes, skipped) = ThemeFile.ReadSaved(File.ReadAllBytes(path));

            _saved = themes;

            if (skipped > 0)
            {
                LoadProblem = skipped == 1
                    ? "1 saved theme couldn't be read, so it was left out. A copy of the file as it was is in the data folder."
                    : $"{skipped} saved themes couldn't be read, so they were left out. A copy of the file as it was is in the data folder.";

                KeepCopyWithoutSkipped(path);
            }
        }
        catch (Exception ex) when (ex is LookFormatException or IOException or UnauthorizedAccessException)
        {
            LoadProblem = "Your saved themes couldn't be read, so that file was set aside and left in the data folder.";
            SetAside(path);
        }

        return _saved;
    }

    private static string AsidePath(string path) => $"{path}.{DateTime.UtcNow:yyyyMMddHHmmss}.unreadable";

    private static void SetAside(string path)
    {
        try
        {
            File.Move(path, AsidePath(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If it can't be moved, the next save can't replace it either, and that fails visibly.
        }
    }

    /// <summary>The next save would drop what was skipped, so a copy goes aside before anything replaces the file.</summary>
    private void KeepCopyWithoutSkipped(string path)
    {
        try
        {
            File.Copy(path, AsidePath(path));
            Persist(_saved!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file stays as it is, so the next start finds the same themes and tries again.
        }
    }

    /// <exception cref="LookFormatException">The result would be too big to read back.</exception>
    private void Persist(List<FolderTheme> saved)
    {
        var bytes = ThemeFile.Write(saved, forThisPc: true);

        if (bytes.Length > ThemeFile.MaxFileBytes)
        {
            throw new LookFormatException("Your saved themes have grown too big to keep. Delete a few and try again.");
        }

        Directory.CreateDirectory(_paths.DataDirectory);
        AtomicFile.Write(_paths.ThemesFile, bytes, FileAttributes.Normal, _ops);
    }
}
