using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.Core.Journal;

namespace Foldcraft.App.ViewModels;

/// <summary>One folder within a history entry.</summary>
public sealed class HistoryFolderViewModel
{
    internal HistoryFolderViewModel(JournalEntry entry, AsyncRelayCommand restoreCommand)
    {
        Path = entry.FolderPath;
        IsActive = entry.IsActive;
        RestoreCommand = restoreCommand;
        State = entry.State switch
        {
            JournalEntryState.Applied => "Changed",
            JournalEntryState.Pending => "Interrupted",
            JournalEntryState.Undone => "Undone",
            JournalEntryState.Forgotten => "No longer tracked",
            _ => string.Empty,
        };
    }

    public string Path { get; }

    public string State { get; }

    public bool IsActive { get; }

    /// <summary>Returns this folder to how it was before Foldcraft first changed it.</summary>
    public AsyncRelayCommand RestoreCommand { get; }

    public string RestoreName => $"Restore original look of {Path}";
}

/// <summary>One change in the history: everything applied together, which undoes together.</summary>
public sealed class HistoryEntryViewModel : ObservableObject
{
    private bool _isExpanded;

    internal HistoryEntryViewModel(
        Guid batchId,
        IReadOnlyList<JournalEntry> entries,
        string when,
        string iconFile,
        AsyncRelayCommand undoCommand,
        Func<JournalEntry, AsyncRelayCommand> restoreCommand)
    {
        BatchId = batchId;
        When = when;
        IconFile = iconFile;
        UndoCommand = undoCommand;
        Folders = entries.Select(e => new HistoryFolderViewModel(e, restoreCommand(e))).ToArray();
        LastSequence = entries.Max(e => e.Sequence);

        var names = entries.Select(e => FolderName(e.FolderPath)).ToArray();

        var verb = entries.All(e => e.Change.IsRepair) ? "Repaired"
            : entries.All(e => e.Change.CreatedFolder) ? "Created"
            : "Changed";

        Title = entries.Count == 1 ? $"{verb} {names[0]}" : $"{verb} {PlainEnglish.Folders(entries.Count)}";
        Detail = entries.Count == 1
            ? entries[0].FolderPath
            : string.Join(", ", names.Take(3)) + (names.Length > 3 ? " and more" : string.Empty);

        var active = entries.Count(e => e.IsActive);

        // A redrawn icon changed nothing in its folder, so an Undo button would do nothing.
        CanUndo = active > 0 && entries.Any(e => !e.Change.LeftFolderAlone);
        State = DescribeState(entries, active);
    }

    public Guid BatchId { get; }

    public string Title { get; }

    public string Detail { get; }

    public string When { get; }

    /// <summary>Only set when the change is no longer simply in place.</summary>
    public string State { get; }

    public bool CanUndo { get; }

    /// <summary>The icon this change applied, shown as the row's picture.</summary>
    public string IconFile { get; }

    public AsyncRelayCommand UndoCommand { get; }

    public IReadOnlyList<HistoryFolderViewModel> Folders { get; }

    public string UndoName => $"Undo: {Title}, {When}";

    public long LastSequence { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    private static string FolderName(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static string DescribeState(IReadOnlyList<JournalEntry> entries, int active)
    {
        if (entries.Any(e => e.State == JournalEntryState.Pending))
        {
            return "Interrupted. Undo to finish cleaning up.";
        }

        if (active == entries.Count)
        {
            return string.Empty;
        }

        if (active > 0)
        {
            return "Partly undone";
        }

        return entries.All(e => e.State == JournalEntryState.Forgotten) ? "No longer tracked" : "Undone";
    }
}

/// <summary>What happened to one folder in the last undo or restore.</summary>
public sealed record RestoreResultRow(string Path, string Message, StatusKind Kind)
{
    public string Name => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;
}
