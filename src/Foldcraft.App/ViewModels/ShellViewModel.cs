using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;

namespace Foldcraft.App.ViewModels;

/// <summary>The window: its screens, and the status bar they share.</summary>
public sealed class ShellViewModel : ObservableObject
{
    public const int CustomizeTab = 0;
    public const int HistoryTab = 1;
    public const int AppsTab = 2;
    public const int ThemesTab = 3;
    public const int RulesTab = 4;
    public const int SettingsTab = 5;
    public const int AboutTab = 6;

    private readonly IDialogs _dialogs;
    private readonly FileLog _log;
    private int _selectedTab;
    private bool _closeWhenIdle;

    public ShellViewModel(AppServices services, IDialogs dialogs, TimeSpan? previewDebounce = null, IAppearance? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _log = services.Log;

        Activity = new ActivityViewModel();
        var designer = new DesignerViewModel(services.Renderer, dialogs, services.Log, previewDebounce);
        Customize = new CustomizeViewModel(services, dialogs, Activity, designer, ReportFailure);
        History = new HistoryViewModel(services, dialogs, Activity, ReportFailure);
        Themes = new ThemesViewModel(services, dialogs, Activity, designer, ReportFailure);
        Rules = new RulesViewModel(services, dialogs, Activity, ReportFailure);
        Apps = new AppsViewModel(services, dialogs, Activity, ReportFailure);
        Settings = new SettingsViewModel(services, dialogs, appearance ?? new NoAppearance(), Activity, History, ReportFailure);
        About = new AboutViewModel(dialogs, services.Paths.DataDirectory);

        Customize.Applied += (_, _) => _ = History.RefreshAsync();
        Themes.Applied += (_, _) => OnFoldersChangedElsewhere();
        Rules.Applied += (_, _) => OnFoldersChangedElsewhere();
        Apps.Applied += (_, _) => OnFoldersChangedElsewhere();
        Themes.ThemesChanged += (_, _) => Rules.ThemesChanged();
        Themes.DesignRequested += (_, _) => SelectedTab = CustomizeTab;
        History.FoldersRestored += (_, _) => Customize.OnFoldersChangedElsewhere();
        History.FoldersRestored += (_, _) => Apps.OnFoldersRestored();
        Updates.RestartRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        Activity.BusyChanged += (_, _) =>
        {
            if (_closeWhenIdle && !Activity.IsBusy)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>The work that delayed closing has finished, so the window can go.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>A folder arrived from outside, so the window should come forward.</summary>
    public event EventHandler? ActivationRequested;

    public ActivityViewModel Activity { get; }

    public CustomizeViewModel Customize { get; }

    public HistoryViewModel History { get; }

    public ThemesViewModel Themes { get; }

    public RulesViewModel Rules { get; }

    public AppsViewModel Apps { get; }

    public SettingsViewModel Settings { get; }

    public AboutViewModel About { get; }

    public UpdatesViewModel Updates => Settings.Updates;

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (Set(ref _selectedTab, value) && value == HistoryTab)
            {
                _ = History.RefreshAsync();
            }
        }
    }

    public async Task InitializeAsync()
    {
        Settings.ApplyStartupAppearance();
        Settings.RepairRightClick();
        Rules.Load();

        await Task.WhenAll(Customize.Designer.InitializeAsync(), History.RefreshAsync(), Themes.RefreshAsync());

        if (History.HasInterruptedChanges)
        {
            Activity.ShowMessage("A change was interrupted before it finished. Open History to finish cleaning up.");
        }
    }

    /// <summary>
    /// Loads a folder that came from the command line or a right-click. The path is untrusted:
    /// it goes through the same check as one added by hand, a bad one shows up in the list with
    /// its reason and nothing is changed until the user presses Apply.
    /// </summary>
    public async Task OpenFolderAsync(string? path)
    {
        ActivationRequested?.Invoke(this, EventArgs.Empty);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SelectedTab = CustomizeTab;
        await Customize.AddPathsAsync([path]);
    }

    /// <summary>
    /// True if the window may close now. Otherwise the work in progress is asked to stop, and
    /// <see cref="CloseRequested"/> fires when it has, so no folder is left mid-change.
    /// </summary>
    public bool TryClose()
    {
        if (!Activity.IsBusy)
        {
            return true;
        }

        _closeWhenIdle = true;
        Activity.Cancel();
        return false;
    }

    /// <summary>A screen other than Customize changed folders, so what Customize remembers may be out of date.</summary>
    private void OnFoldersChangedElsewhere()
    {
        _ = History.RefreshAsync();
        Customize.OnFoldersChangedElsewhere();
    }

    private void ReportFailure(Exception exception)
    {
        _log.Write("A command failed", exception);
        _dialogs.ShowError($"Something went wrong, and Foldcraft stopped what it was doing. Details were saved to:\n\n{_log.Path}");
    }

    /// <summary>Tests have no application whose colors could change.</summary>
    private sealed class NoAppearance : IAppearance
    {
        public void Apply(Core.Settings.AppThemeMode mode)
        {
        }
    }
}
