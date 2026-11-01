using System.Collections.ObjectModel;
using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Themes;

namespace Foldcraft.App.ViewModels;

/// <summary>One theme in the list, with its picture.</summary>
public sealed class ThemeTileViewModel(FolderTheme theme) : ObservableObject
{
    private byte[]? _image;

    public FolderTheme Theme { get; } = theme;

    public string Name => Theme.Name;

    public string Mood => Theme.Mood;

    public byte[]? Image
    {
        get => _image;
        set => Set(ref _image, value);
    }
}

/// <summary>The Themes screen: browse named looks, save the current one, share them and use them.</summary>
public sealed class ThemesViewModel : ObservableObject
{
    private const string AllMoods = "All moods";
    private const int TileSize = 96;

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly ActivityViewModel _activity;
    private readonly DesignerViewModel _designer;
    private readonly List<ThemeTileViewModel> _allTiles = [];
    private ThemeTileViewModel? _selectedTile;
    private string _selectedMood = AllMoods;
    private string? _notice;

    public ThemesViewModel(
        AppServices services,
        IDialogs dialogs,
        ActivityViewModel activity,
        DesignerViewModel designer,
        Action<Exception> onError)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _designer = designer ?? throw new ArgumentNullException(nameof(designer));

        UseInDesignCommand = new RelayCommand(UseInDesign, () => SelectedTile is not null);
        ApplyToFoldersCommand = new AsyncRelayCommand(ApplyToFoldersAsync, onError, () => SelectedTile is not null && !_activity.IsBusy);
        SaveCurrentCommand = new RelayCommand(SaveCurrent, () => _designer.CanApply);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, onError, () => SelectedTile is { Theme.IsBuiltIn: false });
        ImportCommand = new AsyncRelayCommand(ImportAsync, onError);
        ExportCommand = new RelayCommand(Export, () => _allTiles.Any(tile => !tile.Theme.IsBuiltIn));

        _designer.DesignChanged += (_, _) => SaveCurrentCommand.RaiseCanExecuteChanged();
        _activity.BusyChanged += (_, _) => ApplyToFoldersCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Raised when a theme was put in the designer, so the window can go there.</summary>
    public event EventHandler? DesignRequested;

    /// <summary>Raised after folders changed, so the History screen can catch up.</summary>
    public event EventHandler? Applied;

    /// <summary>Raised when the list of themes changed, so the Rules screen can catch up.</summary>
    public event EventHandler? ThemesChanged;

    /// <summary>The themes that fit the mood filter.</summary>
    public ObservableCollection<ThemeTileViewModel> Themes { get; } = [];

    public ObservableCollection<string> Moods { get; } = [AllMoods];

    public RelayCommand UseInDesignCommand { get; }

    public AsyncRelayCommand ApplyToFoldersCommand { get; }

    public RelayCommand SaveCurrentCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }

    public AsyncRelayCommand ImportCommand { get; }

    public RelayCommand ExportCommand { get; }

    public ThemeTileViewModel? SelectedTile
    {
        get => _selectedTile;
        set
        {
            if (Set(ref _selectedTile, value))
            {
                UseInDesignCommand.RaiseCanExecuteChanged();
                ApplyToFoldersCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedMood
    {
        get => _selectedMood;
        set
        {
            if (value is not null && Set(ref _selectedMood, value))
            {
                ShowTiles();
            }
        }
    }

    /// <summary>The result of the last import or export, or a problem with the saved themes.</summary>
    public string? Notice
    {
        get => _notice;
        private set => Set(ref _notice, value);
    }

    /// <summary>Reads the themes and draws their pictures, off the UI thread.</summary>
    public async Task RefreshAsync()
    {
        var themes = await Task.Run(() => _services.Themes.All);
        var selected = SelectedTile?.Theme.Id;

        _allTiles.Clear();
        _allTiles.AddRange(themes.Select(theme => new ThemeTileViewModel(theme)));

        foreach (var mood in themes.Select(theme => theme.Mood).Distinct(StringComparer.Ordinal).Where(mood => !Moods.Contains(mood)).ToArray())
        {
            Moods.Add(mood);
        }

        if (_services.Themes.LoadProblem is { } problem)
        {
            Notice = problem;
        }

        ShowTiles();
        SelectedTile = Themes.FirstOrDefault(tile => tile.Theme.Id == selected);
        ExportCommand.RaiseCanExecuteChanged();

        var tiles = _allTiles.ToArray();
        var images = await Task.Run(() => tiles.Select(tile => Draw(tile.Theme)).ToArray());

        for (var i = 0; i < tiles.Length; i++)
        {
            tiles[i].Image = images[i];
        }

        ThemesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A theme whose picture has gone just shows no image here. Using it reports the problem.
    /// This runs at every start with no click to ask for it, so it stays off the network whatever
    /// the saved themes say.
    /// </summary>
    private byte[]? Draw(FolderTheme theme)
    {
        if (theme.Spec.CustomImagePath is { } picture && !IconSpecCodec.IsLocalPicture(picture))
        {
            return null;
        }

        try
        {
            return _services.Renderer.RenderPreviewPng(theme.Spec, TileSize);
        }
        catch (IconRenderException)
        {
            return null;
        }
    }

    private void ShowTiles()
    {
        Themes.Clear();

        foreach (var tile in _allTiles.Where(tile => _selectedMood == AllMoods || tile.Mood == _selectedMood))
        {
            Themes.Add(tile);
        }
    }

    private void UseInDesign()
    {
        if (SelectedTile is { } tile)
        {
            _designer.LoadLook(tile.Theme.Spec);
            DesignRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ApplyToFoldersAsync()
    {
        if (SelectedTile is not { } tile)
        {
            return;
        }

        var folders = _dialogs.PickFolders();

        if (folders.Count == 0)
        {
            return;
        }

        var cancellation = _activity.Begin(
            $"Applying {tile.Name} to {PlainEnglish.Folders(folders.Count)}...", folders.Count, cancellable: true);

        var summary = "Something went wrong, and the remaining folders were left alone.";
        var finished = false;
        var progress = new Progress<FolderApplyResult>(_ =>
        {
            if (!finished)
            {
                _activity.Advance();
            }
        });

        try
        {
            var batch = await Task.Run(() => _services.Customizer.Apply(folders, tile.Theme.Spec, cancellation, progress), CancellationToken.None);
            finished = true;

            foreach (var result in batch.Results.Where(r => r.Outcome is ApplyOutcome.Failed or ApplyOutcome.FailedRollbackIncomplete))
            {
                _services.Log.Write($"Theme apply {result.Outcome} for {result.RequestedPath}: {result.Message}");
            }

            summary = PlainEnglish.ApplySummary(batch.Results.ToArray());
        }
        finally
        {
            _activity.Finish(summary);
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SaveCurrent()
    {
        var name = _dialogs.AskForText("Save this look", "Give this look a name to find it again later.", "Save");

        if (name is null)
        {
            return;
        }

        try
        {
            _services.Themes.Save(name, _designer.Spec);
            Notice = null;
            _ = RefreshAsync();
        }
        catch (LookFormatException ex)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    private async Task DeleteAsync()
    {
        if (SelectedTile is { Theme.IsBuiltIn: false } tile &&
            _dialogs.Confirm("Delete this theme?", $"This deletes {tile.Name}. Folders using this look keep it.", "Delete"))
        {
            _services.Themes.Delete(tile.Theme.Id);
            await RefreshAsync();
        }
    }

    /// <summary>Nothing in a file is used unless all of it passes, so a bad file changes nothing here.</summary>
    private async Task ImportAsync()
    {
        if (_dialogs.PickThemeFile() is not { } path)
        {
            return;
        }

        try
        {
            var added = await Task.Run(() => _services.Themes.Import(path));

            Notice = added.Count == 1 ? "Added 1 theme." : $"Added {added.Count} themes.";
            await RefreshAsync();
        }
        catch (LookFormatException ex)
        {
            _dialogs.ShowError($"Foldcraft didn't import this file. {ex.Message}");
        }
    }

    private void Export()
    {
        var saved = _allTiles.Select(tile => tile.Theme).Where(theme => !theme.IsBuiltIn).ToArray();

        if (saved.Length == 0 || _dialogs.PickThemeSaveLocation() is not { } path)
        {
            return;
        }

        try
        {
            var result = _services.Themes.Export(saved, path);

            Notice = result.PicturesLeftOut == 0
                ? $"Exported {result.Exported} saved themes."
                : $"Exported {result.Exported} saved themes. Your own pictures stay on this PC, so {result.PicturesLeftOut} of them will show a plain folder elsewhere.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Log.Write("Themes could not be exported", ex);
            _dialogs.ShowError("Foldcraft couldn't save the file there. Try another folder.");
        }
    }
}
