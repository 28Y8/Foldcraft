using System.Diagnostics;
using System.IO;
using System.Windows;
using Foldcraft.App.Views;
using Microsoft.Win32;

namespace Foldcraft.App.Services;

/// <summary>The real dialogs.</summary>
public sealed class WpfDialogs : IDialogs
{
    private const string ImageFilter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
    private const string ThemeFilter = "Foldcraft themes (*.json)|*.json";

    public IReadOnlyList<string> PickFolders()
    {
        var dialog = new OpenFolderDialog { Title = "Choose folders to customize", Multiselect = true };
        return Show(dialog) ? dialog.FolderNames : [];
    }

    public string? PickImage()
    {
        var dialog = new OpenFileDialog { Title = "Choose a picture", Filter = ImageFilter };
        return Show(dialog) ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message, string confirmLabel, IReadOnlyList<string>? items = null)
    {
        var confirm = new PromptButton(confirmLabel, IsPrimary: true);
        return Ask(title, message, items, [confirm, new PromptButton("Cancel", IsCancel: true)]) == confirm;
    }

    public ConflictChoice AskAboutConflicts(IReadOnlyList<string> folders)
    {
        // Enter and Esc both land on the choice that changes nothing.
        var keep = new PromptButton("Keep Current Version");
        var restore = new PromptButton("Restore Anyway");
        var later = new PromptButton("Decide Later", IsPrimary: true, IsCancel: true);

        var choice = Ask(
            "Something else changed these folders",
            "After Foldcraft changed these folders, another app or Windows changed their settings too.\n\n" +
            "Keep the current version to leave them exactly as they are, and Foldcraft will stop tracking them. " +
            "Restore anyway to put the earlier look back, which replaces that other change.",
            folders,
            [keep, restore, later]);

        return choice == keep ? ConflictChoice.KeepCurrent
            : choice == restore ? ConflictChoice.RestoreAnyway
            : ConflictChoice.DecideLater;
    }

    public MissingChoice AskAboutMissing(IReadOnlyList<string> folders)
    {
        // Enter and Esc both land on the choice that changes nothing.
        var find = new PromptButton("Find Folder...");
        var forget = new PromptButton("Stop Tracking");
        var keep = new PromptButton("Keep in History", IsPrimary: true, IsCancel: true);

        var choice = Ask(
            "These folders can't be found",
            "They may have been renamed, moved or deleted, or be on a drive that isn't connected. Another folder may " +
            "even have taken their place.\n\n" +
            "If you renamed or moved one, find it and Foldcraft can still undo the change. If they might come back, " +
            "keep them in the history and try again later. Stop tracking only if they're gone for good, because " +
            "Foldcraft can't undo those changes afterward.",
            folders,
            [find, forget, keep]);

        return choice == find ? MissingChoice.FindFolder
            : choice == forget ? MissingChoice.StopTracking
            : MissingChoice.KeepInHistory;
    }

    public string? PickMovedFolder(string recordedPath)
    {
        var dialog = new OpenFolderDialog { Title = $"Where is {Path.GetFileName(recordedPath)} now?" };
        return Show(dialog) ? dialog.FolderName : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return Show(dialog) ? dialog.FolderName : null;
    }

    public string? PickThemeFile()
    {
        var dialog = new OpenFileDialog { Title = "Choose a theme file", Filter = ThemeFilter };
        return Show(dialog) ? dialog.FileName : null;
    }

    public string? PickThemeSaveLocation()
    {
        var dialog = new SaveFileDialog { Title = "Export themes", Filter = ThemeFilter, FileName = "Foldcraft themes.json" };
        return Show(dialog) ? dialog.FileName : null;
    }

    public string? AskForText(string title, string message, string confirmLabel)
    {
        var confirm = new PromptButton(confirmLabel, IsPrimary: true);
        var prompt = NewPrompt(title, message, null, [confirm, new PromptButton("Cancel", IsCancel: true)], askForText: true);

        prompt.ShowDialog();
        return prompt.Choice == confirm ? prompt.TypedText : null;
    }

    public void ShowError(string message) =>
        Ask("Foldcraft", message, null, [new PromptButton("OK", IsPrimary: true, IsCancel: true)]);

    public void OpenLink(string url) => Process.Start(StartInfoForLink(url, new WindowsElevation().IsElevated))?.Dispose();

    public void OpenFolder(string path) => Process.Start(StartInfoFor(path))?.Dispose();

    /// <summary>
    /// A browser started directly would inherit administrator rights. Explorer hands a link to the
    /// shell that's already running without them.
    /// </summary>
    public static ProcessStartInfo StartInfoForLink(string url, bool elevated)
    {
        var start = StartInfoFor(url);

        if (elevated)
        {
            start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            start.ArgumentList.Add(url);
        }

        return start;
    }

    /// <summary>
    /// A new process inherits the working folder, and Foldcraft's own is the app folder. A browser
    /// that kept it would hold that folder open, and the updater couldn't then replace it.
    /// </summary>
    public static ProcessStartInfo StartInfoFor(string target) => new(target)
    {
        UseShellExecute = true,
        WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
    };

    private static Window? Owner
    {
        get
        {
            var main = Application.Current?.MainWindow;
            return main is { IsLoaded: true } ? main : null;
        }
    }

    /// <summary>A file dialog can leave the working folder wherever it last was, which would lock that folder.</summary>
    private static bool Show(CommonDialog dialog)
    {
        try
        {
            return dialog.ShowDialog(Owner) == true;
        }
        finally
        {
            WorkingFolder.MoveToAppFolder();
        }
    }

    private static PromptButton? Ask(string title, string message, IReadOnlyList<string>? items, IReadOnlyList<PromptButton> buttons)
    {
        var prompt = NewPrompt(title, message, items, buttons, askForText: false);

        prompt.ShowDialog();
        return prompt.Choice;
    }

    private static PromptWindow NewPrompt(
        string title,
        string message,
        IReadOnlyList<string>? items,
        IReadOnlyList<PromptButton> buttons,
        bool askForText)
    {
        var prompt = new PromptWindow(title, message, items, buttons, askForText) { Owner = Owner };

        if (prompt.Owner is null)
        {
            prompt.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return prompt;
    }
}
