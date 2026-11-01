using System.Collections.ObjectModel;
using Foldcraft.App.Mvvm;
using Foldcraft.App.Services;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Settings;
using Foldcraft.Core.Shell;

namespace Foldcraft.App.ViewModels;

/// <summary>One choice for the app's colors.</summary>
public sealed record ThemeModeChoice(string Name, AppThemeMode Mode);

/// <summary>The Settings screen.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly IAppearance _appearance;
    private readonly ActivityViewModel _activity;
    private readonly HistoryViewModel _history;
    private AppSettings _settings;
    private bool _settingsUnreadable;
    private ThemeModeChoice _selectedThemeMode;
    private string? _rightClickMessage;
    private string? _whatRemainsText;

    public SettingsViewModel(
        AppServices services,
        IDialogs dialogs,
        IAppearance appearance,
        ActivityViewModel activity,
        HistoryViewModel history,
        Action<Exception> onError)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _history = history ?? throw new ArgumentNullException(nameof(history));

        var read = services.Settings.Read();

        _settings = read.Settings;
        _settingsUnreadable = read.Unreadable;

        ThemeModes =
        [
            new("Same as Windows", AppThemeMode.System), new("Light", AppThemeMode.Light), new("Dark", AppThemeMode.Dark),
        ];

        _selectedThemeMode = ThemeModes.First(choice => choice.Mode == _settings.ThemeMode);

        ToggleRightClickCommand = new RelayCommand(ToggleRightClick, () => CanChangeRightClick && !_activity.IsBusy);
        UpdateRightClickCommand = new RelayCommand(UpdateRightClick, () => CanChangeRightClick && RightClickIsOutdated && !_activity.IsBusy);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        RemoveAllCommand = new AsyncRelayCommand(RemoveAllAsync, onError, () => !_activity.IsBusy);

        _rightClickMessage = RightClickIsOutdated ? PlainEnglish.RightClickMoved : null;

        Updates = new UpdatesViewModel(
            services.Updates,
            dialogs,
            activity,
            services.Log,
            services.Time,
            () => _settings,
            () => _settingsUnreadable,
            ChangeSettings,
            services.CustomDataDirectory is { } dataDirectory ? ["--data-dir", dataDirectory] : [],
            onError);

        _activity.BusyChanged += (_, _) =>
        {
            ToggleRightClickCommand.RaiseCanExecuteChanged();
            UpdateRightClickCommand.RaiseCanExecuteChanged();
            RemoveAllCommand.RaiseCanExecuteChanged();
        };
    }

    public IReadOnlyList<ThemeModeChoice> ThemeModes { get; }

    public ObservableCollection<string> WhatRemains { get; } = [];

    /// <summary>The same lines as one text, so a screen reader announces them as one message.</summary>
    public string? WhatRemainsText
    {
        get => _whatRemainsText;
        private set => Set(ref _whatRemainsText, value);
    }

    public RelayCommand ToggleRightClickCommand { get; }

    /// <summary>Points the right-click entry at this copy of Foldcraft.</summary>
    public RelayCommand UpdateRightClickCommand { get; }

    public RelayCommand OpenDataFolderCommand { get; }

    public AsyncRelayCommand RemoveAllCommand { get; }

    public UpdatesViewModel Updates { get; }

    public string DataDirectory => _services.Paths.DataDirectory;

    public string RightClickExplanation =>
        "This adds \"Customize with Foldcraft\" to the menu you get when you right-click a folder. It adds one " +
        @"entry to the Windows registry for your account only, under HKCU\Software\Classes\Directory\shell. " +
        "No administrator rights are needed, and turning this off removes exactly that entry and nothing else.";

    public string RightClickWindows11Note =>
        "On Windows 11 it appears under \"Show more options\", not in the first short menu. Apps can only reach " +
        "that first menu with a kind of add-on Foldcraft doesn't install.";

    public string RemoveAllExplanation =>
        "This puts every folder back the way it was and removes the right-click entry. You choose what happens " +
        "to any folder something else has changed since. Afterward this screen lists what's left.";

    public string UninstallExplanation => PlainEnglish.WhatUninstallLeaves;

    public ThemeModeChoice SelectedThemeMode
    {
        get => _selectedThemeMode;
        set
        {
            if (value is null || !Set(ref _selectedThemeMode, value))
            {
                return;
            }

            _appearance.Apply(value.Mode);
            _settings = _settings with { ThemeMode = value.Mode };
            SaveSettings();
        }
    }

    /// <summary>True while the entry is on. This comes from the settings file, so seeing it reads no registry.</summary>
    public bool IsRightClickEnabled => _settings.RightClick is not null;

    public bool CanChangeRightClick => _services.RightClick is not null && _services.ExecutablePath is not null;

    /// <summary>True when the entry starts a copy of Foldcraft that isn't this one. Seeing this reads no registry either.</summary>
    public bool RightClickIsOutdated =>
        _settings.RightClick is { } record &&
        _services.ExecutablePath is { } exePath &&
        !record.ExePath.Equals(exePath, StringComparison.OrdinalIgnoreCase);

    public string? RightClickMessage
    {
        get => _rightClickMessage;
        private set => Set(ref _rightClickMessage, value);
    }

    /// <summary>Makes the app use the saved colors.</summary>
    public void ApplyStartupAppearance() => _appearance.Apply(_settings.ThemeMode);

    /// <summary>
    /// Keeps the entry true to an installed copy, whose program stays at one path through every
    /// update, so there's usually nothing to do. An entry pointing at a program that's gone is
    /// moved to this one. One that isn't there any more, as after an uninstall and a new install,
    /// goes back to off, because the entry is added only when someone turns it on. A copy that
    /// wasn't installed keeps the choice in Settings.
    /// </summary>
    public void RepairRightClick()
    {
        if (!_services.Updates.IsInstalled ||
            _services.RightClick is not { } menu ||
            _services.ExecutablePath is not { } exePath ||
            _settings.RightClick is not { } record)
        {
            return;
        }

        if (!record.ExePath.Equals(exePath, StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(record.ExePath))
            {
                UpdateRightClick();
            }
        }
        else if (!menu.IsInPlace(record.Command, exePath))
        {
            ChangeSettings(settings => settings with { RightClick = null });
            RightClickMessage = PlainEnglish.RightClickGone;
            RightClickChanged();
        }
    }

    private void ToggleRightClick()
    {
        if (_services.RightClick is not { } menu || _services.ExecutablePath is not { } exePath)
        {
            return;
        }

        if (_settings.RightClick is { } record)
        {
            TurnOff(menu, record);
        }
        else
        {
            TurnOn(menu, exePath);
        }

        RightClickChanged();
    }

    /// <summary>The old entry comes off first, using what was recorded about it, and then this copy's goes on.</summary>
    private void UpdateRightClick()
    {
        if (_services.RightClick is not { } menu || _services.ExecutablePath is not { } exePath || _settings.RightClick is not { } record)
        {
            return;
        }

        TurnOff(menu, record);

        // A message here means the old entry stayed, so there's no room for a new one.
        if (RightClickMessage is null)
        {
            TurnOn(menu, exePath);
        }

        RightClickChanged();
    }

    private void RightClickChanged()
    {
        OnPropertyChanged(nameof(IsRightClickEnabled));
        OnPropertyChanged(nameof(RightClickIsOutdated));
        UpdateRightClickCommand.RaiseCanExecuteChanged();
    }

    /// <summary>What's about to be added is written down first, so a crash can't leave it forgotten.</summary>
    private void TurnOn(RightClickMenu menu, string exePath)
    {
        var command = RightClickMenu.BuildCommand(exePath, _services.CustomDataDirectory);

        if (command is null)
        {
            RightClickMessage = PlainEnglish.RightClickMessage(RightClickOutcome.CannotQuote);
            return;
        }

        var before = _settings;

        _settings = _settings with { RightClick = new RightClickRecord(command, exePath) };
        SaveSettings();

        var result = menu.Enable(exePath, _services.CustomDataDirectory);

        if (result.Outcome != RightClickOutcome.Added)
        {
            LogRightClick(result);
            _settings = before;
            SaveSettings();
        }
        else if (result.AddedParents.Count > 0)
        {
            _settings = _settings with { RightClick = new RightClickRecord(command, exePath, result.AddedParents) };
            SaveSettings();
        }

        RightClickMessage = PlainEnglish.RightClickMessage(result.Outcome);
    }

    private void TurnOff(RightClickMenu menu, RightClickRecord record)
    {
        var result = menu.Disable(record.Command, record.ExePath, record.AddedParents);

        // Something else's entry stays where it is, and it stops being Foldcraft's to track.
        if (result.Outcome != RightClickOutcome.Failed)
        {
            _settings = _settings with { RightClick = null };
            SaveSettings();
        }
        else
        {
            LogRightClick(result);
        }

        RightClickMessage = PlainEnglish.RightClickMessage(result.Outcome);
    }

    private void LogRightClick(RightClickResult result) =>
        _services.Log.Write($"Right-click entry: {result.Outcome}. {result.Message}");

    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            _dialogs.OpenFolder(DataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _services.Log.Write("The data folder could not be opened", ex);
            _dialogs.ShowError($"Foldcraft couldn't open that folder. It's here:\n\n{DataDirectory}");
        }
    }

    private async Task RemoveAllAsync()
    {
        if (!_dialogs.Confirm(
                "Remove all Foldcraft changes?",
                "This puts every folder Foldcraft changed back the way it was and removes the right-click entry. " +
                "Your files aren't touched. " + PlainEnglish.CreatedFoldersGo + " Your saved themes, rules and history stay in Foldcraft's own folder.",
                "Remove All Changes"))
        {
            return;
        }

        WhatRemains.Clear();
        WhatRemainsText = null;

        var restored = await _history.RestoreEverythingAsync();
        var rightClick = RemoveRightClick();

        await Task.Run(PruneIcons);

        var kept = await Task.Run(_services.Restorer.ForgottenFoldersUsingIcons);

        foreach (var line in PlainEnglish.WhatRemains(restored, kept.Count, rightClick, DataDirectory))
        {
            WhatRemains.Add(line);
        }

        WhatRemainsText = string.Join('\n', WhatRemains);
    }

    private RightClickOutcome? RemoveRightClick()
    {
        if (_services.RightClick is not { } menu || _settings.RightClick is not { } record)
        {
            return null;
        }

        var result = menu.Disable(record.Command, record.ExePath, record.AddedParents);

        if (result.Outcome != RightClickOutcome.Failed)
        {
            _settings = _settings with { RightClick = null };
            SaveSettings();
            RightClickMessage = null;
            RightClickChanged();
        }
        else
        {
            LogRightClick(result);
        }

        return result.Outcome;
    }

    /// <summary>Tidying up is a bonus. A leftover icon does no harm, so this never fails the removal.</summary>
    private void PruneIcons()
    {
        try
        {
            _services.Icons.PruneUnused(_services.Journal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Log.Write("Unused icons could not be cleared", ex);
        }
    }

    private void ChangeSettings(Func<AppSettings, AppSettings> change)
    {
        _settings = change(_settings);
        SaveSettings();
    }

    /// <summary>A file that couldn't be read is moved aside first, so the first save can't destroy what's in it.</summary>
    private void SaveSettings()
    {
        try
        {
            if (_settingsUnreadable)
            {
                _services.Settings.SetAsideUnreadable();
                _settingsUnreadable = false;
            }

            _services.Settings.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Log.Write("The settings could not be saved", ex);
        }
    }
}
