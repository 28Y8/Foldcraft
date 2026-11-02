using Foldcraft.App.Services.Updates;
using Foldcraft.Core;
using Foldcraft.Core.Apps;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Rules;
using Foldcraft.Core.Safety;
using Foldcraft.Core.Settings;
using Foldcraft.Core.Shell;
using Foldcraft.Core.Themes;

namespace Foldcraft.App.Services;

/// <summary>The app's composition root. Tests build one pointed at a sandbox.</summary>
public sealed class AppServices
{
    /// <param name="paths">Where Foldcraft keeps its data.</param>
    /// <param name="protectedFolders">The folders to refuse. Tests swap these.</param>
    /// <param name="shell">How to tell Explorer about a change.</param>
    /// <param name="log">Where problems are recorded.</param>
    /// <param name="time">The clock.</param>
    /// <param name="registry">
    /// The registry for the right-click entry. With none, that setting is simply unavailable,
    /// so only the real app, which passes one on purpose, can ever reach the real registry.
    /// </param>
    /// <param name="executablePath">Foldcraft.exe, for the right-click entry to start.</param>
    /// <param name="customDataDirectory">The data folder given on the command line, if any.</param>
    /// <param name="appRoots">
    /// Where to look for installed apps. With none a scan finds nothing, so only the real app,
    /// which passes these on purpose, can ever look at the real PC.
    /// </param>
    /// <param name="iconReader">How to read an app's icon. Tests swap this to simulate a failure.</param>
    /// <param name="updates">
    /// Where updates come from. With none the app says it can't update itself, so only the real
    /// app, which passes one on purpose, can ever reach the network.
    /// </param>
    /// <param name="elevation">
    /// Administrator rights. With none the app runs without them, so only the real app, which
    /// passes one on purpose, can ever show a Windows prompt.
    /// </param>
    public AppServices(
        FoldcraftPaths paths,
        ProtectedFolders? protectedFolders = null,
        IShellNotifier? shell = null,
        FileLog? log = null,
        TimeProvider? time = null,
        IUserRegistry? registry = null,
        string? executablePath = null,
        string? customDataDirectory = null,
        AppDiscoveryRoots? appRoots = null,
        IAppIconReader? iconReader = null,
        IUpdateService? updates = null,
        IElevation? elevation = null)
    {
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        Log = log ?? new FileLog(paths.LogFile);
        Time = time ?? TimeProvider.System;
        Elevation = elevation ?? new NoElevation();
        Safety = new FolderSafety(paths, protectedFolders, elevated: Elevation.IsElevated);
        Journal = new ChangeJournal(paths.JournalFile);
        Renderer = new IconRenderer();
        Icons = new IconLibrary(paths, Renderer);
        Customizer = new FolderCustomizer(Safety, Journal, Icons, shell);
        Restorer = new FolderRestorer(Safety, Journal, shell);
        PreviewWatch = new PreviewWatch(Safety, Journal, shell);
        Repairer = new FolderRepairer(Safety, Journal, Icons, Customizer, shell);
        Themes = new ThemeStore(paths);
        Rules = new RuleStore(paths);
        Scanner = new RuleScanner(Safety, Customizer);
        Settings = new SettingsStore(paths);
        RightClick = registry is null ? null : new RightClickMenu(registry);
        ExecutablePath = executablePath;
        CustomDataDirectory = customDataDirectory;
        Updates = updates ?? new NoUpdateService();

        var shortcuts = new ShellShortcuts();

        AppDiscovery = AppDiscovery.For(appRoots ?? AppDiscoveryRoots.None, shortcuts);
        AppLooks = new AppLookBuilder(iconReader ?? new ShellIconReader());
        AppFolders = new AppFolderCreator(paths, Safety, Customizer, Icons, shortcuts, new AppArtStore(paths));
    }

    public FoldcraftPaths Paths { get; }
    public FileLog Log { get; }
    public TimeProvider Time { get; }
    public FolderSafety Safety { get; }
    public ChangeJournal Journal { get; }
    public IconRenderer Renderer { get; }
    public IconLibrary Icons { get; }
    public FolderCustomizer Customizer { get; }
    public FolderRestorer Restorer { get; }

    /// <summary>Only the real app starts this, so no test ever runs on its timer.</summary>
    public PreviewWatch PreviewWatch { get; }

    public FolderRepairer Repairer { get; }
    public ThemeStore Themes { get; }
    public RuleStore Rules { get; }
    public RuleScanner Scanner { get; }
    public SettingsStore Settings { get; }
    public AppDiscovery AppDiscovery { get; }
    public AppLookBuilder AppLooks { get; }
    public AppFolderCreator AppFolders { get; }

    /// <summary>Null when no registry was provided.</summary>
    public RightClickMenu? RightClick { get; }

    public string? ExecutablePath { get; }
    public string? CustomDataDirectory { get; }
    public IUpdateService Updates { get; }
    public IElevation Elevation { get; }
}
