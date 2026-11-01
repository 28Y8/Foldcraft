using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.App.Services.Updates;
using Foldcraft.Core.Settings;
using Foldcraft.Core.Updates;

namespace Foldcraft.App.ViewModels;

/// <summary>How far along an update is.</summary>
public enum UpdateStage
{
    /// <summary>Nothing to show.</summary>
    Idle,

    /// <summary>A newer version exists. Nothing has been downloaded.</summary>
    Available,

    /// <summary>Downloading, because the person pressed Update.</summary>
    Downloading,

    /// <summary>
    /// Downloaded, with each package checked against the release. Only a confirmed restart installs
    /// the app, though the finished download has already refreshed the updater (Update.exe).
    /// </summary>
    ReadyToRestart,
}

/// <summary>
/// Checking for a new version, the banner that offers it and the Settings card. A check by itself
/// never says anything unless it found something. Nothing downloads until Update is pressed. A
/// finished download also refreshes the updater (Update.exe) straight away, and the app itself
/// changes only after a confirmed restart, which is refused while folders are changing or while
/// another copy is running from the same install.
/// </summary>
public sealed class UpdatesViewModel : ObservableObject
{
    private readonly IUpdateService _service;
    private readonly IDialogs _dialogs;
    private readonly ActivityViewModel _activity;
    private readonly FileLog _log;
    private readonly TimeProvider _time;
    private readonly Func<AppSettings> _settings;
    private readonly Func<bool> _settingsUnreadable;
    private readonly Action<Func<AppSettings, AppSettings>> _changeSettings;
    private readonly IReadOnlyList<string> _restartArguments;

    private AvailableUpdate? _update;
    private CancellationTokenSource? _download;
    private UpdateStage _stage;
    private bool _isChecking;
    private bool _dismissed;
    private int _progress;
    private string? _note;
    private string? _checkMessage;

    /// <param name="service">Where updates come from.</param>
    /// <param name="dialogs">How to ask and how to open a link.</param>
    /// <param name="activity">The work that holds a restart back.</param>
    /// <param name="log">Where failures go, as their kind and no details.</param>
    /// <param name="time">The clock.</param>
    /// <param name="settings">The current settings.</param>
    /// <param name="settingsUnreadable">True while the settings are only defaults standing in for a file that couldn't be read.</param>
    /// <param name="changeSettings">Changes and saves the settings.</param>
    /// <param name="restartArguments">The command line for the new version, so a custom data folder carries over.</param>
    /// <param name="onError">Where an unexpected command failure goes.</param>
    public UpdatesViewModel(
        IUpdateService service,
        IDialogs dialogs,
        ActivityViewModel activity,
        FileLog log,
        TimeProvider time,
        Func<AppSettings> settings,
        Func<bool> settingsUnreadable,
        Action<Func<AppSettings, AppSettings>> changeSettings,
        IReadOnlyList<string> restartArguments,
        Action<Exception> onError)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsUnreadable = settingsUnreadable ?? throw new ArgumentNullException(nameof(settingsUnreadable));
        _changeSettings = changeSettings ?? throw new ArgumentNullException(nameof(changeSettings));
        _restartArguments = restartArguments ?? throw new ArgumentNullException(nameof(restartArguments));

        CheckCommand = new AsyncRelayCommand(() => CheckAsync(manual: true), onError, () => CanUpdate && !_isChecking && Stage != UpdateStage.Downloading);
        UpdateCommand = new AsyncRelayCommand(DownloadAsync, onError, () => Stage == UpdateStage.Available && !_isChecking);
        CancelCommand = new RelayCommand(() => _download?.Cancel(), () => Stage == UpdateStage.Downloading);
        RestartCommand = new RelayCommand(Restart, () => Stage == UpdateStage.ReadyToRestart && !_activity.IsBusy);
        LaterCommand = new RelayCommand(Dismiss, () => Stage != UpdateStage.Downloading);
        ReleaseNotesCommand = new RelayCommand(OpenReleaseNotes);
        ReleasesPageCommand = new RelayCommand(() => _dialogs.OpenLink(ReleaseSite.ReleasesPage));

        _activity.BusyChanged += (_, _) =>
        {
            RestartCommand.RaiseCanExecuteChanged();

            // Only this stage's wording depends on the work, and an unchanged line isn't news.
            if (Stage == UpdateStage.ReadyToRestart)
            {
                OnPropertyChanged(nameof(BannerText));
            }
        };
    }

    /// <summary>The window closes for this, once the updater is waiting for it to.</summary>
    public event EventHandler? RestartRequested;

    public AsyncRelayCommand CheckCommand { get; }

    /// <summary>Starts the download.</summary>
    public AsyncRelayCommand UpdateCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand RestartCommand { get; }

    /// <summary>Hides the banner until the next check finds something or the app starts again.</summary>
    public RelayCommand LaterCommand { get; }

    public RelayCommand ReleaseNotesCommand { get; }

    public RelayCommand ReleasesPageCommand { get; }

    /// <summary>False for a copy the installer didn't put in place, which offers the Releases page instead.</summary>
    public bool CanUpdate => _service.IsInstalled;

    public bool CannotUpdate => !CanUpdate;

    public string NotInstalledMessage => PlainEnglish.UpdatesNeedInstaller;

    public string PrivacyNote => PlainEnglish.UpdatePrivacy;

    public string CurrentVersion => $"Version {_service.CurrentVersion}";

    public string LastChecked => _settings().LastUpdateCheck is { } moment
        ? $"Last checked: {PlainEnglish.When(moment, _time.GetUtcNow(), _time.LocalTimeZone)}"
        : "Last checked: not yet";

    public bool CheckAutomatically
    {
        get => _settings().CheckForUpdatesAutomatically;
        set
        {
            if (value != CheckAutomatically)
            {
                _changeSettings(settings => settings with { CheckForUpdatesAutomatically = value });
                OnPropertyChanged();
            }
        }
    }

    public UpdateStage Stage
    {
        get => _stage;
        private set
        {
            if (Set(ref _stage, value))
            {
                OnBannerChanged();
                UpdateCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
                RestartCommand.RaiseCanExecuteChanged();
                LaterCommand.RaiseCanExecuteChanged();
                CheckCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBannerVisible => Stage != UpdateStage.Idle && !_dismissed;

    public bool ShowUpdate => Stage == UpdateStage.Available;

    public bool IsDownloading => Stage == UpdateStage.Downloading;

    public bool ShowRestart => Stage == UpdateStage.ReadyToRestart;

    public bool ShowLater => Stage is UpdateStage.Available or UpdateStage.ReadyToRestart;

    public string BannerText => (Stage, _update?.Version) switch
    {
        (UpdateStage.Available, { } version) => $"Foldcraft {version} is available.",
        (UpdateStage.Downloading, { } version) => $"Downloading Foldcraft {version}...",
        (UpdateStage.ReadyToRestart, { } version) when _activity.IsBusy => $"Foldcraft {version} is ready. You can restart when the current work finishes.",
        (UpdateStage.ReadyToRestart, { } version) => $"Foldcraft {version} is ready to install.",
        _ => string.Empty,
    };

    /// <summary>What's happened to the update, when something went wrong or was stopped.</summary>
    public string? Note
    {
        get => _note;
        private set => Set(ref _note, value);
    }

    public int Progress
    {
        get => _progress;
        private set
        {
            if (Set(ref _progress, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public string ProgressText => $"{Progress}%";

    /// <summary>The answer to Check for Updates. A check that ran by itself leaves this empty. It also says why a check was skipped because of a bad settings file.</summary>
    public string? CheckMessage
    {
        get => _checkMessage;
        private set => Set(ref _checkMessage, value);
    }

    /// <summary>
    /// What happens at each start. It finishes when the check does, but nothing waits for it. It
    /// never throws, and any failure stays in the log.
    /// </summary>
    public async Task CheckAtStartupAsync()
    {
        if (!CanUpdate)
        {
            return;
        }

        try
        {
            if (await Task.Run(_service.FindDownloaded) is { } downloaded)
            {
                _update = downloaded;
                Stage = UpdateStage.ReadyToRestart;
            }
        }
        catch (Exception ex)
        {
            LogFailure("A downloaded update could not be read", ex);
        }

        // These settings are only defaults, so they say nothing of whether the person opted out.
        if (_settingsUnreadable())
        {
            _log.Write("The automatic check for updates was skipped, because the settings file could not be read");
            CheckMessage = PlainEnglish.UpdateCheckSkipped;
            return;
        }

        if (UpdateSchedule.IsDue(_settings(), _time.GetUtcNow()))
        {
            await CheckAsync(manual: false);
        }
    }

    private async Task CheckAsync(bool manual)
    {
        if (_isChecking || Stage == UpdateStage.Downloading)
        {
            return;
        }

        SetChecking(true);
        CheckMessage = manual ? "Checking..." : null;
        Remember(settings => settings with { LastUpdateAttempt = _time.GetUtcNow() });

        try
        {
            var found = await Task.Run(() => _service.CheckAsync(CancellationToken.None));

            Remember(settings => settings with { LastUpdateCheck = _time.GetUtcNow() });
            Offer(found);

            if (manual)
            {
                CheckMessage = found is null ? PlainEnglish.NoUpdateFound(_service.CurrentVersion) : $"Foldcraft {found.Version} is available.";
            }
        }
        catch (Exception ex)
        {
            LogFailure("The check for updates failed", ex);

            if (manual)
            {
                CheckMessage = PlainEnglish.UpdateCheckFailed(UpdateFailures.Classify(ex));
            }
        }
        finally
        {
            SetChecking(false);
        }
    }

    private void Offer(AvailableUpdate? found)
    {
        // A download keeps going whatever a check says, and its banner has to stay with it.
        if (found is null || Stage == UpdateStage.Downloading)
        {
            return;
        }

        var alreadyDownloaded = Stage == UpdateStage.ReadyToRestart && found.Version == _update?.Version;

        _dismissed = false;
        Note = null;

        if (!alreadyDownloaded)
        {
            _update = found;
            Stage = UpdateStage.Available;
        }

        OnBannerChanged();
    }

    private async Task DownloadAsync()
    {
        if (_update is not { } update)
        {
            return;
        }

        using var download = new CancellationTokenSource();

        _download = download;
        Note = null;
        Progress = 0;
        Stage = UpdateStage.Downloading;

        try
        {
            await Task.Run(() => _service.DownloadAsync(update, value => Progress = Math.Clamp(value, 0, 100), download.Token), download.Token);
            Stage = UpdateStage.ReadyToRestart;
        }
        catch (OperationCanceledException) when (download.IsCancellationRequested)
        {
            Note = PlainEnglish.UpdateDownloadCanceled;
            Stage = UpdateStage.Available;
        }
        catch (Exception ex)
        {
            LogFailure("An update could not be downloaded", ex);
            Note = PlainEnglish.UpdateDownloadFailed(UpdateFailures.Classify(ex));
            Stage = UpdateStage.Available;
        }
        finally
        {
            _download = null;
        }
    }

    private void Restart()
    {
        if (_update is not { } update)
        {
            return;
        }

        if (!_dialogs.Confirm("Restart Foldcraft to update?", PlainEnglish.UpdateRestartMessage(update.Version), "Restart Now"))
        {
            return;
        }

        // Work can start from outside while the question is up, such as a folder sent from the right-click menu.
        if (_activity.IsBusy)
        {
            Note = PlainEnglish.UpdateWaitsForWork;
            return;
        }

        try
        {
            if (_service.AnotherCopyIsRunning())
            {
                Note = PlainEnglish.UpdateWaitsForOtherCopy;
                return;
            }

            _service.InstallWhenThisCloses(update, _restartArguments);
        }
        catch (Exception ex)
        {
            LogFailure("An update could not be started", ex);
            Note = PlainEnglish.UpdateInstallFailed;
            return;
        }

        RestartRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Dismiss()
    {
        _dismissed = true;
        OnPropertyChanged(nameof(IsBannerVisible));
    }

    private void OpenReleaseNotes() =>
        _dialogs.OpenLink((_update is { } update ? ReleaseSite.ReleaseNotesPage(update.Version) : null) ?? ReleaseSite.ReleasesPage);

    private void SetChecking(bool value)
    {
        _isChecking = value;
        CheckCommand.RaiseCanExecuteChanged();
        UpdateCommand.RaiseCanExecuteChanged();
    }

    /// <summary>A date isn't worth a write while the file on disk can't be read and might still come back.</summary>
    private void Remember(Func<AppSettings, AppSettings> change)
    {
        if (_settingsUnreadable())
        {
            return;
        }

        _changeSettings(change);
        OnPropertyChanged(nameof(LastChecked));
    }

    private void LogFailure(string what, Exception exception) =>
        _log.Write($"{what}: {UpdateFailures.ForLog(exception)}");

    private void OnBannerChanged()
    {
        OnPropertyChanged(nameof(IsBannerVisible));
        OnPropertyChanged(nameof(BannerText));
        OnPropertyChanged(nameof(ShowUpdate));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(ShowRestart));
        OnPropertyChanged(nameof(ShowLater));
    }
}
