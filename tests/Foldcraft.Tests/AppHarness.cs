using System.Text;
using Foldcraft.App.Services;
using Foldcraft.App.Services.Updates;
using Foldcraft.App.ViewModels;
using Foldcraft.Core.Apps;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Settings;
using Foldcraft.Core.Shell;

namespace Foldcraft.Tests;

/// <summary>Answers every question the app asks, and remembers what was asked.</summary>
internal sealed class FakeDialogs : IDialogs
{
    public IReadOnlyList<string> FoldersToPick { get; set; } = [];
    public string? ImageToPick { get; set; }
    public string? FolderToPick { get; set; }
    public string? ThemeFileToPick { get; set; }
    public string? ThemeSaveLocation { get; set; }
    public string? TextAnswer { get; set; }
    public bool ConfirmAnswer { get; set; } = true;
    public ConflictChoice ConflictAnswer { get; set; } = ConflictChoice.DecideLater;
    public MissingChoice MissingAnswer { get; set; }
    public Func<string, string?> MovedFolderAnswer { get; set; } = _ => null;

    /// <summary>Runs while a question is on screen, to do what could happen behind it.</summary>
    public Action? WhileConfirming { get; set; }

    public bool ForgetMissingAnswer
    {
        set => MissingAnswer = value ? MissingChoice.StopTracking : MissingChoice.KeepInHistory;
    }

    public List<string> Confirmations { get; } = [];
    public List<string> ConfirmationMessages { get; } = [];
    public List<IReadOnlyList<string>> ConflictPrompts { get; } = [];
    public List<IReadOnlyList<string>> MissingPrompts { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Links { get; } = [];
    public List<string> OpenedFolders { get; } = [];

    public IReadOnlyList<string> PickFolders() => FoldersToPick;

    public string? PickImage() => ImageToPick;

    public bool Confirm(string title, string message, string confirmLabel, IReadOnlyList<string>? items = null)
    {
        Confirmations.Add(title);
        ConfirmationMessages.Add(message);
        WhileConfirming?.Invoke();
        return ConfirmAnswer;
    }

    public ConflictChoice AskAboutConflicts(IReadOnlyList<string> folders)
    {
        ConflictPrompts.Add(folders);
        return ConflictAnswer;
    }

    public MissingChoice AskAboutMissing(IReadOnlyList<string> folders)
    {
        MissingPrompts.Add(folders);
        return MissingAnswer;
    }

    public string? PickMovedFolder(string recordedPath) => MovedFolderAnswer(recordedPath);

    public string? PickFolder(string title) => FolderToPick;

    public string? PickThemeFile() => ThemeFileToPick;

    public string? PickThemeSaveLocation() => ThemeSaveLocation;

    public string? AskForText(string title, string message, string confirmLabel) => TextAnswer;

    public void ShowError(string message) => Errors.Add(message);

    public void OpenLink(string url) => Links.Add(url);

    public void OpenFolder(string path) => OpenedFolders.Add(path);
}

/// <summary>Remembers the mode it was asked for. Tests have no application to recolor.</summary>
internal sealed class FakeAppearance : IAppearance
{
    public List<AppThemeMode> Applied { get; } = [];

    public void Apply(AppThemeMode mode) => Applied.Add(mode);
}

/// <summary>
/// A registry in a dictionary. Every test that reaches the right-click code uses this, so
/// the real one on the machine running the tests is never touched.
/// </summary>
internal sealed class FakeRegistry : IUserRegistry
{
    private readonly Dictionary<string, Dictionary<string, string>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public bool FailWrites { get; set; }

    /// <summary>How many values can be set before the next one fails.</summary>
    public int WritesBeforeFailure { get; set; } = int.MaxValue;

    /// <summary>Makes any missing keys above one being set, as the real registry does. Software\Classes counts as always there.</summary>
    public bool MakesParentKeys { get; set; }

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public IReadOnlyCollection<string> Keys => _keys.Keys;

    public string? GetString(string subKey, string? name)
    {
        Reads++;
        return _keys.TryGetValue(subKey, out var values) && values.TryGetValue(name ?? string.Empty, out var value) ? value : null;
    }

    public void SetString(string subKey, string? name, string value)
    {
        Writes++;

        if (FailWrites || WritesBeforeFailure-- <= 0)
        {
            throw new UnauthorizedAccessException("Simulated registry failure.");
        }

        if (!_keys.TryGetValue(subKey, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _keys[subKey] = values;
        }

        values[name ?? string.Empty] = value;

        for (var key = subKey; MakesParentKeys && key.Length > @"Software\Classes".Length; key = key[..key.LastIndexOf('\\')])
        {
            _keys.TryAdd(key, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    public void DeleteValue(string subKey, string? name)
    {
        Writes++;

        if (_keys.TryGetValue(subKey, out var values))
        {
            values.Remove(name ?? string.Empty);
        }
    }

    /// <summary>Like the real registry, a key with a subkey exists even if nothing was set on it directly.</summary>
    public bool KeyExists(string subKey)
    {
        Reads++;
        return _keys.Keys.Any(key => key.Equals(subKey, StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith(subKey + "\\", StringComparison.OrdinalIgnoreCase));
    }

    public (IReadOnlyList<string> Values, IReadOnlyList<string> SubKeys) Contents(string subKey)
    {
        Reads++;

        var prefix = subKey + "\\";

        return (
            _keys.TryGetValue(subKey, out var values) ? values.Keys.ToArray() : [],
            _keys.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    public void DeleteEmptyKey(string subKey)
    {
        Writes++;
        Assert.Empty(Contents(subKey).Values);
        Assert.Empty(Contents(subKey).SubKeys);
        _keys.Remove(subKey);
    }
}

/// <summary>Runs a callback whenever a folder finishes, to act in the middle of a batch.</summary>
internal sealed class CallbackShell(Action<string> callback) : IShellNotifier
{
    public void FolderChanged(string folderPath) => callback(folderPath);
}

/// <summary>The whole app minus its window, inside a sandbox.</summary>
internal sealed class AppHarness : IDisposable
{
    public const string FakeExecutable = @"C:\Apps\Foldcraft\Foldcraft.exe";

    public AppHarness(
        IShellNotifier? shell = null,
        TimeProvider? time = null,
        bool hasRegistry = true,
        IAppIconReader? iconReader = null,
        IUpdateService? updates = null,
        string? customDataDirectory = null,
        Action<Sandbox>? prepare = null,
        IElevation? elevation = null)
    {
        Box = new Sandbox();
        prepare?.Invoke(Box);
        Dialogs = new FakeDialogs();
        Log = new FileLog(Box.Paths.LogFile);
        Registry = new FakeRegistry();
        Appearance = new FakeAppearance();
        Pc = new FakePc(Path.Combine(Box.Root, "pc"), hasRegistry ? Registry : null);
        Services = new AppServices(
            Box.Paths,
            Box.Protected,
            shell ?? Box.Shell,
            Log,
            time,
            hasRegistry ? Registry : null,
            FakeExecutable,
            customDataDirectory,
            appRoots: Pc.Roots,
            iconReader: iconReader,
            updates: updates,
            elevation: elevation);

        Shell = new ShellViewModel(Services, Dialogs, TimeSpan.Zero, Appearance);
    }

    public Sandbox Box { get; }
    public FakeDialogs Dialogs { get; }
    public FileLog Log { get; }
    public FakeRegistry Registry { get; }
    public FakeAppearance Appearance { get; }

    /// <summary>A pretend PC for the Apps and Games screen to scan. The real one is never looked at.</summary>
    public FakePc Pc { get; }

    public AppServices Services { get; }
    public ShellViewModel Shell { get; }

    public CustomizeViewModel Customize => Shell.Customize;
    public DesignerViewModel Designer => Shell.Customize.Designer;
    public HistoryViewModel History => Shell.History;
    public ThemesViewModel Themes => Shell.Themes;
    public RulesViewModel Rules => Shell.Rules;
    public SettingsViewModel Settings => Shell.Settings;
    public UpdatesViewModel Updates => Shell.Updates;

    /// <summary>Adds sandbox folders to the list and applies the current design.</summary>
    public async Task<string[]> ApplyToNewFoldersAsync(params string[] names)
    {
        var folders = names.Select(Box.NewFolder).ToArray();
        await Customize.AddPathsAsync(folders);
        await Customize.ApplyCommand.ExecuteAsync();
        await History.RefreshAsync();
        return folders;
    }

    /// <summary>Changes a folder's look behind Foldcraft's back, as another app might.</summary>
    public static void EditIni(string folder)
    {
        var path = Sandbox.IniPath(folder);
        var attributes = File.GetAttributes(path);

        File.SetAttributes(path, FileAttributes.Normal);
        File.AppendAllText(path, "[Other]\r\nKey=Value\r\n", Encoding.ASCII);
        File.SetAttributes(path, attributes);
    }

    public void Dispose() => Box.Dispose();
}
