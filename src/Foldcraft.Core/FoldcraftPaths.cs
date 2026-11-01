namespace Foldcraft.Core;

/// <summary>
/// Where Foldcraft keeps its own data. Every component takes this as a dependency so
/// that tests can point the whole app at a throwaway directory.
/// </summary>
public sealed class FoldcraftPaths
{
    public FoldcraftPaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory);
    }

    public string DataDirectory { get; }

    /// <summary>The fixed icon library. desktop.ini files reference icons here.</summary>
    public string IconsDirectory => Path.Combine(DataDirectory, "Icons");

    /// <summary>The append-only undo history.</summary>
    public string JournalFile => Path.Combine(DataDirectory, "journal.fcj");

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>A settings file that couldn't be read, kept when a change would otherwise write over it.</summary>
    public string UnreadableSettingsFile => Path.Combine(DataDirectory, "settings.unreadable.json");

    /// <summary>The looks saved on this PC.</summary>
    public string ThemesFile => Path.Combine(DataDirectory, "themes.json");

    /// <summary>The rules set up on this PC.</summary>
    public string RulesFile => Path.Combine(DataDirectory, "rules.json");

    /// <summary>Pictures read from software on this PC for generated folders. They stay on this PC.</summary>
    public string AppArtDirectory => Path.Combine(DataDirectory, "AppArt");

    /// <summary>Where a shortcut is built before it's copied into a new folder.</summary>
    public string ScratchDirectory => Path.Combine(DataDirectory, "Scratch");

    /// <summary>Where the app records problems it recovered from.</summary>
    public string LogFile => Path.Combine(DataDirectory, "foldcraft.log");

    /// <summary>%LOCALAPPDATA%\Foldcraft for the current user.</summary>
    public static FoldcraftPaths Default() => new(Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify),
        "Foldcraft"));
}
