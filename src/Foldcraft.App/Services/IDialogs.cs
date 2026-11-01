namespace Foldcraft.App.Services;

/// <summary>What to do about folders that something else changed after Foldcraft did.</summary>
public enum ConflictChoice
{
    /// <summary>Leave them alone for now and ask again next time.</summary>
    DecideLater,

    /// <summary>Keep the folders as they are and stop tracking the change.</summary>
    KeepCurrent,

    /// <summary>Put the earlier look back, replacing the other change.</summary>
    RestoreAnyway,
}

/// <summary>What to do about folders that aren't where Foldcraft left them.</summary>
public enum MissingChoice
{
    /// <summary>Leave them in the history and try again later.</summary>
    KeepInHistory,

    /// <summary>Show Foldcraft where they are now, so their changes can still be undone.</summary>
    FindFolder,

    /// <summary>Forget about them. Their changes can no longer be undone.</summary>
    StopTracking,
}

/// <summary>Everything a view-model needs from the user or the desktop, so tests can stand in.</summary>
public interface IDialogs
{
    IReadOnlyList<string> PickFolders();

    string? PickImage();

    bool Confirm(string title, string message, string confirmLabel, IReadOnlyList<string>? items = null);

    ConflictChoice AskAboutConflicts(IReadOnlyList<string> folders);

    /// <summary>Asks what to do about folders that are no longer where they were.</summary>
    MissingChoice AskAboutMissing(IReadOnlyList<string> folders);

    /// <summary>Asks where a folder went. Null if the user doesn't know or cancels.</summary>
    string? PickMovedFolder(string recordedPath);

    /// <summary>Asks for one folder. Null if the user cancels.</summary>
    string? PickFolder(string title);

    /// <summary>Asks for a theme file to read. Null if the user cancels.</summary>
    string? PickThemeFile();

    /// <summary>Asks where to save a theme file. Null if the user cancels.</summary>
    string? PickThemeSaveLocation();

    /// <summary>Asks for a line of text. Null if the user cancels.</summary>
    string? AskForText(string title, string message, string confirmLabel);

    void ShowError(string message);

    void OpenLink(string url);

    /// <summary>Shows a folder in File Explorer.</summary>
    void OpenFolder(string path);
}
