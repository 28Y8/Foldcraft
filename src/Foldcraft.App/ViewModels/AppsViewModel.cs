using System.Collections.ObjectModel;
using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.Core.Apps;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Icons;

namespace Foldcraft.App.ViewModels;

/// <summary>Where an item's logo can sit.</summary>
public sealed record LogoAnchorChoice(string Name, LogoAnchor Anchor);

/// <summary>What can sit behind an item's logo. Null leaves it to the contrast check.</summary>
public sealed record LogoPlateChoice(string Name, LogoPlate? Plate);

/// <summary>What to do about a name that's taken.</summary>
public sealed record NameClashOption(string Name, NameClashChoice Choice);

/// <summary>What happened to one item in the last run.</summary>
public sealed record AppResultRow(string Name, string Message, StatusKind Kind);

/// <summary>
/// The Apps and Games screen. A scan only reads, and pictures stay in memory, so nothing is
/// written anywhere until the user presses the button that makes folders. Pressing it records
/// every folder first, and the whole run undoes as one step from History.
/// </summary>
public sealed class AppsViewModel : ObservableObject
{
    public const string AllSources = "All sources";

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly ActivityViewModel _activity;
    private readonly List<AppTileViewModel> _allTiles = [];
    private AppTileViewModel? _selectedTile;
    private string _searchText = string.Empty;
    private string _selectedSource = AllSources;
    private NameClashOption _selectedClash;
    private string? _destinationPath;
    private bool _themeInstallFolders;
    private bool _hasScanned;
    private string? _summary;

    public AppsViewModel(AppServices services, IDialogs dialogs, ActivityViewModel activity, Action<Exception> onError)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));

        ClashChoices =
        [
            new("Add a number, like Name (2)", NameClashChoice.AddNumber),
            new("Skip that item", NameClashChoice.Skip),
        ];

        _selectedClash = ClashChoices[0];

        ScanCommand = new AsyncRelayCommand(ScanAsync, onError, () => !_activity.IsBusy);
        SelectAllCommand = new RelayCommand(() => SetIncluded(true), () => Tiles.Any(tile => tile.IsReady));
        SelectNoneCommand = new RelayCommand(() => SetIncluded(false), () => _allTiles.Any(tile => tile.IsIncluded));
        PickDestinationCommand = new RelayCommand(PickDestination, () => !_activity.IsBusy);
        CreateCommand = new AsyncRelayCommand(CreateAsync, onError, () => !_activity.IsBusy && IncludedCount > 0);
        ThemeExistingCommand = new AsyncRelayCommand(ThemeExistingAsync, onError, () => !_activity.IsBusy && SelectedTile is { IsReady: true });
        ThemeFoldersInsideCommand = new AsyncRelayCommand(ThemeFoldersInsideAsync, onError, () => !_activity.IsBusy && SelectedTile is { IsReady: true });
        DismissResultsCommand = new RelayCommand(Results.Clear);

        _activity.BusyChanged += (_, _) => RefreshCommands();
    }

    /// <summary>Raised after folders changed, so the History screen can catch up.</summary>
    public event EventHandler? Applied;

    /// <summary>The items that fit the search and the filter.</summary>
    public ObservableCollection<AppTileViewModel> Tiles { get; } = [];

    public ObservableCollection<string> Sources { get; } = [AllSources];

    public ObservableCollection<AppResultRow> Results { get; } = [];

    public IReadOnlyList<NameClashOption> ClashChoices { get; }

    public IReadOnlyList<ShapeChoice> Shapes => AppTileViewModel.Shapes;

    public IReadOnlyList<LogoAnchorChoice> Anchors => AppTileViewModel.Anchors;

    public IReadOnlyList<LogoPlateChoice> Plates => AppTileViewModel.Plates;

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectNoneCommand { get; }

    public RelayCommand PickDestinationCommand { get; }

    /// <summary>Makes a folder for every checked item.</summary>
    public AsyncRelayCommand CreateCommand { get; }

    /// <summary>Gives the selected item's look to a folder that already exists.</summary>
    public AsyncRelayCommand ThemeExistingCommand { get; }

    /// <summary>Gives the selected item's look to every folder inside one, such as each version of an app.</summary>
    public AsyncRelayCommand ThemeFoldersInsideCommand { get; }

    public RelayCommand DismissResultsCommand { get; }

    public string Disclaimer => PlainEnglish.AppsDisclaimer;

    public string InstallFolderExplanation =>
        "Off unless you turn it on. Foldcraft normally leaves a game's own folder alone. This adds a small hidden settings file " +
        "inside it, which a game update or a file check can remove. " +
        (_services.Elevation.IsElevated
            ? "Foldcraft is running as administrator, so folders in Program Files can change. AppData stays blocked, so some games will be skipped."
            : "Program Files and AppData stay blocked, so many games will be skipped. Restarting as administrator from Settings opens up Program Files.");

    public AppTileViewModel? SelectedTile
    {
        get => _selectedTile;
        set
        {
            if (Set(ref _selectedTile, value))
            {
                ThemeExistingCommand.RaiseCanExecuteChanged();
                ThemeFoldersInsideCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ApplyHint));
            }
        }
    }

    /// <summary>The next step, beside the buttons that apply a look.</summary>
    public string ApplyHint =>
        SelectedTile is { } tile ? $"Give a folder you already have the {tile.Name} look."
        : HasScanned ? "Click an app to choose its look."
        : "Press Scan, click an app and give any of your folders its look.";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty))
            {
                ShowTiles();
            }
        }
    }

    public string SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (value is not null && Set(ref _selectedSource, value))
            {
                ShowTiles();
            }
        }
    }

    public NameClashOption SelectedClash
    {
        get => _selectedClash;
        set
        {
            if (value is not null)
            {
                Set(ref _selectedClash, value);
            }
        }
    }

    /// <summary>Where new folders go.</summary>
    public string? DestinationPath
    {
        get => _destinationPath;
        private set
        {
            if (Set(ref _destinationPath, value))
            {
                OnPropertyChanged(nameof(DestinationLabel));
            }
        }
    }

    public string DestinationLabel => _destinationPath ?? "No location chosen yet. For example, a folder called Games on any drive.";

    /// <summary>
    /// Also change each app's own folder. This stays off unless the user turns it on, and
    /// turning it on takes reading what it means first.
    /// </summary>
    public bool ThemeInstallFolders
    {
        get => _themeInstallFolders;
        set
        {
            if (value && !_themeInstallFolders &&
                !_dialogs.Confirm("Change install folders too?", InstallFolderExplanation, "Turn On"))
            {
                OnPropertyChanged();
                return;
            }

            Set(ref _themeInstallFolders, value);
        }
    }

    public bool HasScanned
    {
        get => _hasScanned;
        private set
        {
            if (Set(ref _hasScanned, value))
            {
                OnPropertyChanged(nameof(EmptyMessage));
                OnPropertyChanged(nameof(ApplyHint));
            }
        }
    }

    public bool HasNoTiles => Tiles.Count == 0;

    /// <summary>What the grid shows when it has nothing in it.</summary>
    public string EmptyMessage
    {
        get
        {
            if (!HasScanned)
            {
                return "Press Scan to find the apps and games on this PC. Scanning only reads, and it changes nothing.";
            }

            return _allTiles.Count == 0
                ? "Foldcraft didn't find any apps or games. It looks in Steam, Roblox, Epic Games and the Start Menu, for this Windows account and for all users. Try Scan again after installing something."
                : "Nothing fits that search. Try fewer words, or choose All sources.";
        }
    }

    public string? Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    public int IncludedCount => _allTiles.Count(tile => tile.IsIncluded);

    /// <summary>The History screen put folders back, so what the tiles say about them is out of date.</summary>
    public void OnFoldersRestored()
    {
        Results.Clear();
        Summary = null;

        foreach (var tile in _allTiles)
        {
            tile.ForgetResult();
        }
    }

    public string IncludedLabel => IncludedCount == 1 ? "1 selected" : $"{IncludedCount} selected";

    public string CreateLabel => IncludedCount == 1 ? "Create 1 Folder" : $"Create {IncludedCount} Folders";

    private async Task ScanAsync()
    {
        var cancellation = _activity.Begin("Looking for apps and games...", cancellable: true);
        var summary = "Something went wrong, and Foldcraft stopped looking.";

        ClearTiles();
        Results.Clear();

        try
        {
            var report = await Task.Run(() => _services.AppDiscovery.Scan(cancellation), CancellationToken.None);

            foreach (var problem in report.Problems)
            {
                _services.Log.Write($"Looking in {problem.Provider} failed: {problem.Message}");
            }

            var tiles = report.Apps.Select(app => new AppTileViewModel(app, _services.Renderer, _services.Log)).ToArray();

            foreach (var tile in tiles)
            {
                tile.IncludedChanged += (_, _) => OnIncludedChanged();
            }

            _allTiles.AddRange(tiles);
            ShowSources();
            ShowTiles();
            HasScanned = true;

            var (ready, unreadable) = await ReadIconsAsync(tiles, cancellation);

            summary = PlainEnglish.AppScanSummary(report, ready, unreadable, cancellation.IsCancellationRequested);
            Summary = summary;
        }
        finally
        {
            _activity.Finish(summary);
            OnPropertyChanged(nameof(EmptyMessage));
            RefreshCommands();
        }
    }

    /// <summary>One at a time off the UI thread, so tiles fill in as they're ready and cancel takes effect quickly.</summary>
    private async Task<(int Ready, int Unreadable)> ReadIconsAsync(IReadOnlyList<AppTileViewModel> tiles, CancellationToken cancellation)
    {
        if (tiles.Count == 0)
        {
            return (0, 0);
        }

        _activity.Restart("Reading icons...", tiles.Count);

        var (ready, unreadable) = (0, 0);

        foreach (var tile in tiles)
        {
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var prepared = await Task.Run(() => _services.AppLooks.Prepare(tile.App, cancellation), CancellationToken.None);
                await tile.AttachAsync(prepared);
                ready++;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Deliberately broad: one app's odd icon mustn't end the scan for the rest.
                _services.Log.Write($"The icon for {tile.Name} could not be read", ex);
                tile.ShowUnreadable();
                unreadable++;
            }

            _activity.Advance();
        }

        foreach (var tile in tiles.Where(tile => !tile.IsReady && tile.Status is null))
        {
            tile.ShowNotRead();
        }

        RefreshCommands();
        return (ready, unreadable);
    }

    private void PickDestination()
    {
        if (_dialogs.PickFolder("Choose where the new folders should go") is { } folder)
        {
            DestinationPath = folder;
        }
    }

    private async Task CreateAsync()
    {
        var chosen = _allTiles.Where(tile => tile.IsIncluded).ToArray();

        if (chosen.Length == 0)
        {
            return;
        }

        if (DestinationPath is null)
        {
            PickDestination();

            if (DestinationPath is null)
            {
                return;
            }
        }

        var cancellation = _activity.Begin($"Creating {PlainEnglish.Folders(chosen.Length)}...", chosen.Length, cancellable: true);
        var summary = "Something went wrong, and the remaining folders were left alone.";
        var finished = false;
        var progress = new Progress<AppFolderResult>(_ =>
        {
            if (!finished)
            {
                _activity.Advance();
            }
        });

        Results.Clear();

        try
        {
            var requests = chosen.Select(tile => tile.ToRequest()).ToArray();
            var options = new AppFolderOptions(DestinationPath, SelectedClash.Choice, ThemeInstallFolders);
            var batch = await Task.Run(() => _services.AppFolders.Create(requests, options, cancellation, progress), CancellationToken.None);
            finished = true;

            ShowResults(chosen, batch.Results);
            summary = PlainEnglish.AppFolderSummary(batch.Results.ToArray());
            Summary = summary;
        }
        finally
        {
            _activity.Finish(summary);
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ThemeExistingAsync()
    {
        if (SelectedTile is not { } tile || _dialogs.PickFolder($"Choose a folder to give the {tile.Name} look") is not { } folder)
        {
            return;
        }

        _activity.Begin($"Applying the {tile.Name} look...");

        var summary = "Something went wrong, and the folder was left alone.";

        try
        {
            var request = tile.ToRequest();
            var result = await Task.Run(() => _services.AppFolders.ThemeExisting(request, folder), CancellationToken.None);

            ShowResults([tile], [result]);
            summary = PlainEnglish.AppFolderResult(result);
            Summary = summary;
        }
        finally
        {
            _activity.Finish(summary);
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Everything goes through one question and one undo step, however many folders there are.</summary>
    private async Task ThemeFoldersInsideAsync()
    {
        if (SelectedTile is not { } tile ||
            _dialogs.PickFolder($"Choose the folder that holds the folders to give the {tile.Name} look") is not { } parent)
        {
            return;
        }

        var folders = await Task.Run(() => AppFolderCreator.FoldersInside(parent), CancellationToken.None);

        // A slow drive leaves time to start something else, and only one job can run.
        if (_activity.IsBusy)
        {
            Summary = "Foldcraft is busy, so nothing changed. Try again when the current work finishes.";
            return;
        }

        if (folders.Count == 0)
        {
            Summary = "That folder has no folders inside it for Foldcraft to change.";
            return;
        }

        if (!_dialogs.Confirm(
                $"Change {PlainEnglish.Folders(folders.Count)}?",
                $"Each folder directly inside {parent} gets the {tile.Name} look. Your files aren't touched, and you can undo all of it as one step from History.",
                "Apply",
                folders.Select(folder => Path.GetFileName(folder)).ToArray()))
        {
            return;
        }

        var cancellation = _activity.Begin($"Applying the {tile.Name} look...", folders.Count, cancellable: true);
        var summary = "Something went wrong, and the remaining folders were left alone.";
        var finished = false;
        var progress = new Progress<FolderApplyResult>(_ =>
        {
            if (!finished)
            {
                _activity.Advance();
            }
        });

        Results.Clear();

        try
        {
            var request = tile.ToRequest();
            var results = await Task.Run(() => _services.AppFolders.ThemeExisting(request, folders, cancellation, progress), CancellationToken.None);
            finished = true;

            for (var i = 0; i < results.Count; i++)
            {
                if (results[i].Outcome == AppFolderOutcome.Failed)
                {
                    _services.Log.Write($"The look for {tile.Name} failed on a folder: {results[i].Message}");
                }

                Results.Add(new AppResultRow(Path.GetFileName(folders[i]), PlainEnglish.AppFolderResult(results[i]), KindOf(results[i])));
            }

            summary = PlainEnglish.ThemedFoldersSummary(results);
            tile.ShowResult(summary, results.All(result => result.Outcome == AppFolderOutcome.Themed) ? StatusKind.Success : StatusKind.Warning);
            Summary = summary;
        }
        finally
        {
            _activity.Finish(summary);
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ShowResults(IReadOnlyList<AppTileViewModel> tiles, IReadOnlyList<AppFolderResult> results)
    {
        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var kind = KindOf(result);
            var message = PlainEnglish.AppFolderResult(result);

            if (result.Outcome == AppFolderOutcome.Failed)
            {
                _services.Log.Write($"The folder for {result.AppName} failed: {result.Message}");
            }

            tiles[i].ShowResult(message, kind);
            Results.Add(new AppResultRow(result.AppName, message, kind));
        }
    }

    private static StatusKind KindOf(AppFolderResult result) => result.Outcome switch
    {
        AppFolderOutcome.Created or AppFolderOutcome.Themed => result.InstallFolder is { Outcome: not (ApplyOutcome.Applied or ApplyOutcome.Unchanged) }
            ? StatusKind.Warning
            : StatusKind.Success,
        AppFolderOutcome.NameTaken or AppFolderOutcome.Canceled => StatusKind.Warning,
        _ => StatusKind.Error,
    };

    private void ClearTiles()
    {
        _allTiles.Clear();
        Tiles.Clear();
        SelectedTile = null;
        Summary = null;
        ShowSources();
        OnPropertyChanged(nameof(HasNoTiles));
        OnIncludedChanged();
    }

    private void ShowSources()
    {
        var names = _allTiles.Select(tile => tile.SourceName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        foreach (var stale in Sources.Skip(1).Where(name => !names.Contains(name)).ToArray())
        {
            Sources.Remove(stale);
        }

        foreach (var name in names.Where(name => !Sources.Contains(name)))
        {
            Sources.Add(name);
        }

        if (!Sources.Contains(SelectedSource))
        {
            SelectedSource = AllSources;
        }
    }

    private void ShowTiles()
    {
        var selected = SelectedTile;
        var words = _searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Tiles.Clear();

        foreach (var tile in _allTiles.Where(tile =>
                     (_selectedSource == AllSources || tile.SourceName == _selectedSource) &&
                     words.All(word => tile.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase))))
        {
            Tiles.Add(tile);
        }

        SelectedTile = selected is not null && Tiles.Contains(selected) ? selected : null;
        OnPropertyChanged(nameof(HasNoTiles));
        OnPropertyChanged(nameof(EmptyMessage));
        RefreshCommands();
    }

    /// <summary>Works on what's showing, so a search followed by Select All means what it looks like.</summary>
    private void SetIncluded(bool included)
    {
        var tiles = included ? Tiles.Where(tile => tile.IsReady) : _allTiles.AsEnumerable();

        foreach (var tile in tiles.ToArray())
        {
            tile.IsIncluded = included;
        }
    }

    private void OnIncludedChanged()
    {
        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(IncludedLabel));
        OnPropertyChanged(nameof(CreateLabel));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        ScanCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        SelectNoneCommand.RaiseCanExecuteChanged();
        PickDestinationCommand.RaiseCanExecuteChanged();
        CreateCommand.RaiseCanExecuteChanged();
        ThemeExistingCommand.RaiseCanExecuteChanged();
        ThemeFoldersInsideCommand.RaiseCanExecuteChanged();
    }
}
