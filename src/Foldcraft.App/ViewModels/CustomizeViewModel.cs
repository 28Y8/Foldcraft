using System.Collections.ObjectModel;
using System.ComponentModel;
using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.Core.Customization;

namespace Foldcraft.App.ViewModels;

/// <summary>The main screen: the folder list, the designer and the Apply button.</summary>
public sealed class CustomizeViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly ActivityViewModel _activity;

    public CustomizeViewModel(
        AppServices services,
        IDialogs dialogs,
        ActivityViewModel activity,
        DesignerViewModel designer,
        Action<Exception> onError)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        Designer = designer ?? throw new ArgumentNullException(nameof(designer));

        AddFoldersCommand = new AsyncRelayCommand(() => AddPathsAsync(_dialogs.PickFolders()), onError, () => !_activity.IsBusy);
        DropCommand = new AsyncRelayCommand(
            parameter => AddPathsAsync(parameter as IReadOnlyList<string> ?? []),
            onError,
            () => !_activity.IsBusy);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => !_activity.IsBusy && Folders.Any(f => f.IsSelected));
        ClearCommand = new RelayCommand(Clear, () => !_activity.IsBusy && Folders.Count > 0);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, onError, () => CanApply);

        Designer.DesignChanged += (_, _) => OnDesignChanged();
        _activity.BusyChanged += (_, _) => RefreshCommands();
    }

    /// <summary>Raised after an apply, so the History screen can catch up.</summary>
    public event EventHandler? Applied;

    public DesignerViewModel Designer { get; }

    public ObservableCollection<FolderRowViewModel> Folders { get; } = [];

    public AsyncRelayCommand AddFoldersCommand { get; }

    /// <summary>Takes the paths of dropped items.</summary>
    public AsyncRelayCommand DropCommand { get; }

    public RelayCommand RemoveSelectedCommand { get; }

    public RelayCommand ClearCommand { get; }

    public AsyncRelayCommand ApplyCommand { get; }

    public bool HasFolders => Folders.Count > 0;

    private int ChangeCount
    {
        get
        {
            var spec = Designer.Spec;
            return Folders.Count(f => f.WillChange(spec));
        }
    }

    private bool CanApply => !_activity.IsBusy && Designer.CanApply && ChangeCount > 0;

    /// <summary>The Apply button's wording, which says how many folders will change.</summary>
    public string ApplyLabel
    {
        get
        {
            var count = ChangeCount;

            if (count > 0)
            {
                return $"Apply to {PlainEnglish.Folders(count)}";
            }

            return Folders.Any(f => f.IsAllowed) ? "Already applied" : "Apply";
        }
    }

    /// <summary>A note under the Apply button, or nothing when there's nothing to say.</summary>
    public string? ApplyHint
    {
        get
        {
            if (Folders.Count == 0)
            {
                return "Add a folder to get started.";
            }

            var refused = Folders.Count(f => !f.IsAllowed);

            return refused switch
            {
                0 => null,
                1 => "1 folder in the list can't be changed.",
                _ => $"{refused} folders in the list can't be changed.",
            };
        }
    }

    /// <summary>Checks and adds paths. Duplicates are skipped; refused folders stay, with the reason.</summary>
    public async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
        {
            return;
        }

        // Checking touches the disk, and a sleeping drive can take seconds to answer.
        var verdicts = await Task.Run(() => paths.Select(_services.Safety.Check).ToArray());
        // Paths are in their true spelling, so names that differ only by case are two folders.
        var known = new HashSet<string>(Folders.Select(f => f.Path), StringComparer.Ordinal);

        foreach (var row in verdicts.Select(v => new FolderRowViewModel(v)))
        {
            if (known.Add(row.Path))
            {
                row.PropertyChanged += OnRowChanged;
                Folders.Add(row);
            }
        }

        ListChanged();
    }

    /// <summary>Another screen changed or put back folders, so finished rows are out of date.</summary>
    public void OnFoldersChangedElsewhere()
    {
        foreach (var row in Folders)
        {
            row.ForgetAppliedLook();
        }

        ListChanged();
    }

    private void RemoveSelected()
    {
        foreach (var row in Folders.Where(f => f.IsSelected).ToArray())
        {
            row.PropertyChanged -= OnRowChanged;
            Folders.Remove(row);
        }

        ListChanged();
    }

    private void Clear()
    {
        foreach (var row in Folders)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Folders.Clear();
        ListChanged();
    }

    private async Task ApplyAsync()
    {
        var spec = Designer.Spec;
        var targets = Folders.Where(f => f.WillChange(spec)).ToArray();

        if (targets.Length == 0)
        {
            return;
        }

        var cancellation = _activity.Begin($"Applying to {PlainEnglish.Folders(targets.Length)}...", targets.Length, cancellable: true);
        var rows = targets.ToDictionary(row => row.Path, StringComparer.Ordinal);
        var summary = "Something went wrong, and the remaining folders were left alone.";

        foreach (var row in targets)
        {
            row.BeginWork();
        }

        void Show(FolderApplyResult result)
        {
            if (rows.TryGetValue(result.RequestedPath, out var row))
            {
                row.ShowResult(result, spec);
            }
        }

        var finished = false;

        var progress = new Progress<FolderApplyResult>(result =>
        {
            if (!finished)
            {
                Show(result);
                _activity.Advance();
            }
        });

        try
        {
            var paths = targets.Select(row => row.Path).ToArray();
            var batch = await Task.Run(() => _services.Customizer.Apply(paths, spec, cancellation, progress), CancellationToken.None);
            finished = true;

            // The final report is the truth. Progress arrives separately and may lag behind it.
            foreach (var result in batch.Results)
            {
                Show(result);
                LogProblem(result);
            }

            summary = PlainEnglish.ApplySummary(batch.Results.ToArray());
        }
        finally
        {
            foreach (var row in targets.Where(row => row.IsWorking))
            {
                row.ShowResult(new FolderApplyResult { RequestedPath = row.Path, Outcome = ApplyOutcome.Failed }, spec);
            }

            _activity.Finish(summary);
            ListChanged();
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }

    private void LogProblem(FolderApplyResult result)
    {
        if (result.Outcome is ApplyOutcome.Failed or ApplyOutcome.FailedRollbackIncomplete)
        {
            _services.Log.Write($"Apply {result.Outcome} for {result.RequestedPath}: {result.Message}");
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderRowViewModel.IsSelected))
        {
            RemoveSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnDesignChanged()
    {
        var spec = Designer.Spec;

        foreach (var row in Folders)
        {
            row.DesignChanged(spec);
        }

        ListChanged();
    }

    private void ListChanged()
    {
        OnPropertyChanged(nameof(HasFolders));
        OnPropertyChanged(nameof(ApplyLabel));
        OnPropertyChanged(nameof(ApplyHint));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        AddFoldersCommand.RaiseCanExecuteChanged();
        DropCommand.RaiseCanExecuteChanged();
        RemoveSelectedCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
    }
}
