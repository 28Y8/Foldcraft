using System.Collections.ObjectModel;
using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Journal;

namespace Foldcraft.App.ViewModels;

/// <summary>
/// The History screen. It shows the journal newest first and drives undo and restore,
/// including the conversation when a folder can't simply be put back.
/// </summary>
public sealed class HistoryViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly ActivityViewModel _activity;
    private readonly Action<Exception> _onError;
    private readonly Lock _refreshGate = new();
    private Task _lastRefresh = Task.CompletedTask;
    private JournalSnapshot _snapshot = new([], 0);
    private string? _notice;

    public HistoryViewModel(AppServices services, IDialogs dialogs, ActivityViewModel activity, Action<Exception> onError)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _onError = onError ?? throw new ArgumentNullException(nameof(onError));

        RestoreAllCommand = new AsyncRelayCommand(RestoreAllAsync, onError, () => !_activity.IsBusy && ActiveFolderCount > 0);
        RepairCommand = new AsyncRelayCommand(RepairAsync, onError, () => !_activity.IsBusy && ActiveFolderCount > 0);
        DismissResultsCommand = new RelayCommand(DismissResults);
        _activity.BusyChanged += (_, _) => RefreshCommands();
    }

    /// <summary>Runs one pass of a restore. A null folder list means every folder involved.</summary>
    private delegate RestoreReport RestorePass(
        ConflictPolicy policy,
        IReadOnlyCollection<string>? folders,
        IProgress<FolderRestoreResult> progress);

    /// <summary>Raised after folders were put back, so the Customize screen can catch up.</summary>
    public event EventHandler? FoldersRestored;

    /// <summary>
    /// Raised when the control that had focus is gone. The value is the batch to go back to,
    /// or null for wherever makes sense next.
    /// </summary>
    public event EventHandler<Guid?>? FocusRequested;

    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];

    public ObservableCollection<RestoreResultRow> Results { get; } = [];

    public AsyncRelayCommand RestoreAllCommand { get; }

    /// <summary>Finds icons that went missing or fell behind, and draws them again.</summary>
    public AsyncRelayCommand RepairCommand { get; }

    public RelayCommand DismissResultsCommand { get; }

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>A note about the history itself, such as records that couldn't be read.</summary>
    public string? Notice
    {
        get => _notice;
        private set => Set(ref _notice, value);
    }

    /// <summary>True if a change has no recorded outcome, so a crash may have left it half done.</summary>
    public bool HasInterruptedChanges => InterruptedCount > 0;

    private int InterruptedCount => _snapshot.Active.Count(e => e.State == JournalEntryState.Pending);

    private int ActiveFolderCount => _snapshot.Active
        .Select(e => e.FolderPath)
        .Distinct(StringComparer.Ordinal)
        .Count();

    /// <summary>
    /// Re-reads the journal. Refreshes run strictly one after another, so a slow read that
    /// started before a change can never overwrite a newer one. This never fails.
    /// </summary>
    public Task RefreshAsync()
    {
        lock (_refreshGate)
        {
            _lastRefresh = RefreshAfterAsync(_lastRefresh);
            return _lastRefresh;
        }
    }

    private async Task RefreshAfterAsync(Task previous)
    {
        await previous;

        try
        {
            var snapshot = await Task.Run(_services.Journal.Read);
            Show(snapshot);

            var unreadable = snapshot.CorruptRecords switch
            {
                0 => null,
                1 => "1 history record couldn't be read and was skipped. Everything else still works.",
                var count => $"{count} history records couldn't be read and were skipped. Everything else still works.",
            };

            var interrupted = InterruptedCount switch
            {
                0 => null,
                1 => "1 change was interrupted before it finished. Undo it to finish cleaning up.",
                var count => $"{count} changes were interrupted before they finished. Undo them to finish cleaning up.",
            };

            Notice = unreadable is null && interrupted is null ? null : string.Join(' ', new[] { unreadable, interrupted }.OfType<string>());
        }
        catch (Exception ex)
        {
            _services.Log.Write("The history could not be read", ex);
            Notice = "Foldcraft couldn't read its history just now. Try opening this screen again.";
        }
    }

    /// <summary>The folders in an entry that have been changed again since.</summary>
    public IReadOnlyList<string> FoldersChangedSince(HistoryEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var mine = new HashSet<string>(
            entry.Folders.Where(f => f.IsActive).Select(f => f.Path),
            StringComparer.Ordinal);

        return _snapshot.Active
            .Where(e => e.Change.BatchId != entry.BatchId && e.Sequence > entry.LastSequence && mine.Contains(e.FolderPath))
            .Where(e => !e.Change.LeftFolderAlone)
            .Select(e => e.FolderPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private void Show(JournalSnapshot snapshot)
    {
        _snapshot = snapshot;

        var expanded = Entries.Where(e => e.IsExpanded).Select(e => e.BatchId).ToHashSet();
        var now = _services.Time.GetUtcNow();

        var batches = snapshot.Entries
            .Where(e => e.State != JournalEntryState.RolledBack)
            .GroupBy(e => e.Change.BatchId)
            .OrderByDescending(batch => batch.Max(e => e.Sequence));

        Entries.Clear();

        foreach (var batch in batches)
        {
            var entries = batch.OrderBy(e => e.Sequence).ToArray();
            HistoryEntryViewModel? self = null;

            self = new HistoryEntryViewModel(
                batch.Key,
                entries,
                PlainEnglish.When(entries[0].Timestamp, now),
                Path.Combine(_services.Paths.IconsDirectory, Path.GetFileName(entries[0].Change.IconPath)),
                new AsyncRelayCommand(() => UndoAsync(self!), _onError, () => !_activity.IsBusy),
                entry => new AsyncRelayCommand(() => RestoreFolderAsync(batch.Key, entry.FolderPath), _onError, () => !_activity.IsBusy))
            {
                IsExpanded = expanded.Contains(batch.Key),
            };

            Entries.Add(self);
        }

        OnPropertyChanged(nameof(IsEmpty));
        RestoreAllCommand.RaiseCanExecuteChanged();
        RepairCommand.RaiseCanExecuteChanged();
    }

    private async Task UndoAsync(HistoryEntryViewModel entry)
    {
        var newer = FoldersChangedSince(entry);

        if (newer.Count > 0 && !_dialogs.Confirm(
                "Undo newer changes too?",
                "These folders were changed again later. Undoing this change also removes those newer looks.",
                "Undo",
                newer))
        {
            return;
        }

        await RunAsync(
            "Undoing...",
            entry.Folders.Count(f => f.IsActive),
            (policy, folders, progress) => _services.Restorer.UndoBatch(entry.BatchId, policy, folders, progress),
            entry.BatchId);
    }

    private Task RestoreFolderAsync(Guid batchId, string folder) => RunAsync(
        "Restoring...",
        1,
        (policy, folders, progress) => RestoreFolders(folders ?? [folder], policy, progress),
        batchId);

    private async Task RestoreAllAsync()
    {
        var count = ActiveFolderCount;

        if (!_dialogs.Confirm(
                "Restore everything?",
                $"This puts {PlainEnglish.Folders(count)} back to how they were before Foldcraft changed them. Your files aren't touched either way." +
                (_snapshot.Active.Any(e => e.Change.CreatedFolder) ? " " + PlainEnglish.CreatedFoldersGo : string.Empty),
                "Restore everything"))
        {
            return;
        }

        await RestoreEverythingAsync();
    }

    /// <summary>
    /// Restores every folder, including the conversation about any that need a decision. The
    /// caller has already asked. Returns what happened to each one and how many records
    /// couldn't be read, because those are changes nothing here could put back.
    /// </summary>
    public Task<RestoreReport> RestoreEverythingAsync() => RunAsync(
        "Restoring everything...",
        ActiveFolderCount,
        (policy, folders, progress) => folders is null
            ? _services.Restorer.RestoreAll(policy, progress)
            : RestoreFolders(folders, policy, progress),
        focusBatch: null);

    private async Task RepairAsync()
    {
        var cancellation = _activity.Begin("Checking your icons...", ActiveFolderCount, cancellable: true);
        var summary = "Something went wrong, and Foldcraft stopped.";
        var finished = false;
        var progress = new Progress<int>(_ =>
        {
            if (!finished)
            {
                _activity.Advance();
            }
        });

        try
        {
            var report = await Task.Run(() => _services.Repairer.RepairAll(cancellation, progress), CancellationToken.None);
            finished = true;

            Results.Clear();

            foreach (var result in report)
            {
                if (result.Outcome == RepairOutcome.Failed)
                {
                    _services.Log.Write($"Repair failed for {result.FolderPath}: {result.Message}");
                }

                Results.Add(new RestoreResultRow(
                    result.FolderPath,
                    PlainEnglish.RepairResult(result),
                    result.Outcome == RepairOutcome.Repaired ? StatusKind.Success : StatusKind.Warning));
            }

            summary = PlainEnglish.RepairSummary(report);
        }
        finally
        {
            await RefreshAsync();
            _activity.Finish(summary);
        }
    }

    private RestoreReport RestoreFolders(IReadOnlyCollection<string> folders, ConflictPolicy policy, IProgress<FolderRestoreResult> progress)
    {
        var results = new List<FolderRestoreResult>(folders.Count);
        var corrupt = 0;

        foreach (var folder in folders)
        {
            var report = _services.Restorer.RestoreFolder(folder, policy);
            corrupt = Math.Max(corrupt, report.CorruptJournalRecords);

            foreach (var result in report.Results)
            {
                results.Add(result);
                progress.Report(result);
            }
        }

        return new RestoreReport(results, corrupt);
    }

    /// <summary>
    /// Runs a restore, then asks about anything that needs a decision and repeats the pass for
    /// exactly those folders. The first pass never overwrites anything.
    /// </summary>
    private async Task<RestoreReport> RunAsync(string message, int total, RestorePass pass, Guid? focusBatch)
    {
        _activity.Begin(message, total);

        var results = new Dictionary<string, FolderRestoreResult>(StringComparer.Ordinal);
        var summary = "Something went wrong, and Foldcraft stopped.";
        var currentPass = 0;
        var corrupt = 0;

        async Task Run(ConflictPolicy policy, IReadOnlyCollection<string>? folders)
        {
            var thisPass = ++currentPass;

            // A retry counts its own folders, not ones already counted. A late report from an
            // earlier pass must not land on the new count.
            if (folders is not null)
            {
                _activity.Restart(message, folders.Count);
            }

            var progress = new Progress<FolderRestoreResult>(_ =>
            {
                if (thisPass == currentPass)
                {
                    _activity.Advance();
                }
            });

            var report = await Task.Run(() => pass(policy, folders, progress));

            corrupt = Math.Max(corrupt, report.CorruptJournalRecords);

            foreach (var result in report.Results)
            {
                results[result.FolderPath] = result;
            }
        }

        try
        {
            await Run(ConflictPolicy.Report, null);

            var missing = Pick(results, RestoreOutcome.FolderMissing, RestoreOutcome.DifferentFolder);

            if (missing.Length > 0)
            {
                switch (_dialogs.AskAboutMissing(missing))
                {
                    case MissingChoice.StopTracking:
                        await Run(ConflictPolicy.Forget, missing);
                        break;
                    case MissingChoice.FindFolder:
                        var found = await RelocateAsync(missing, results);

                        if (found.Count > 0)
                        {
                            await Run(ConflictPolicy.Report, found);
                        }

                        break;
                }
            }

            var conflicts = Pick(results, RestoreOutcome.Conflict);

            if (conflicts.Length > 0)
            {
                var choice = _dialogs.AskAboutConflicts(conflicts);

                if (choice != ConflictChoice.DecideLater)
                {
                    await Run(choice == ConflictChoice.RestoreAnyway ? ConflictPolicy.Overwrite : ConflictPolicy.Forget, conflicts);
                }
            }

            summary = PlainEnglish.RestoreSummary(results.Values);
        }
        finally
        {
            ShowResults(results.Values);
            await RefreshAsync();
            _activity.Finish(summary);
            FoldersRestored?.Invoke(this, EventArgs.Empty);
            FocusRequested?.Invoke(this, focusBatch);
        }

        return new RestoreReport(results.Values.ToArray(), corrupt);
    }

    /// <summary>
    /// Asks where each folder went and points its history there. A folder that turns out not to
    /// be the one recorded keeps its history where it was. Returns the new locations.
    /// </summary>
    private async Task<IReadOnlyList<string>> RelocateAsync(
        IReadOnlyList<string> folders,
        Dictionary<string, FolderRestoreResult> results)
    {
        var found = new List<string>();

        foreach (var folder in folders)
        {
            if (_dialogs.PickMovedFolder(folder) is not { } location)
            {
                continue;
            }

            var moved = await Task.Run(() => _services.Restorer.Relocate(folder, location));

            if (moved.Outcome == RestoreOutcome.Relocated)
            {
                results.Remove(folder);
                found.Add(moved.FolderPath);
            }
            else
            {
                results[folder] = moved;
            }
        }

        return found;
    }

    private void DismissResults()
    {
        Results.Clear();
        FocusRequested?.Invoke(this, null);
    }

    private static string[] Pick(Dictionary<string, FolderRestoreResult> results, params RestoreOutcome[] outcomes) =>
        results.Values.Where(r => outcomes.Contains(r.Outcome)).Select(r => r.FolderPath).ToArray();

    private void ShowResults(IEnumerable<FolderRestoreResult> results)
    {
        Results.Clear();

        foreach (var result in results)
        {
            if (result.Outcome == RestoreOutcome.Failed)
            {
                _services.Log.Write($"Restore failed for {result.FolderPath}: {result.Message}");
            }

            Results.Add(new RestoreResultRow(result.FolderPath, PlainEnglish.RestoreResult(result), KindOf(result.Outcome)));
        }
    }

    private static StatusKind KindOf(RestoreOutcome outcome) => outcome switch
    {
        RestoreOutcome.Restored or RestoreOutcome.AlreadyRestored => StatusKind.Success,
        RestoreOutcome.Conflict or RestoreOutcome.FolderMissing or RestoreOutcome.DifferentFolder => StatusKind.Warning,
        RestoreOutcome.Refused or RestoreOutcome.Failed => StatusKind.Error,
        _ => StatusKind.Neutral,
    };

    private void RefreshCommands()
    {
        RestoreAllCommand.RaiseCanExecuteChanged();
        RepairCommand.RaiseCanExecuteChanged();

        foreach (var entry in Entries)
        {
            entry.UndoCommand.RaiseCanExecuteChanged();

            foreach (var folder in entry.Folders)
            {
                folder.RestoreCommand.RaiseCanExecuteChanged();
            }
        }
    }
}
