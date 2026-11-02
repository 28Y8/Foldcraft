using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Foldcraft.App.Services;
using Foldcraft.App.Services.Updates;
using Foldcraft.App.ViewModels;
using Foldcraft.Core.Apps;
using Foldcraft.Core.Shell;

namespace Foldcraft.App;

/// <summary>
/// Starts the app: finds the data directory, hands off to a running copy if there is one,
/// installs the crash guard, opens the window.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    private SingleInstance? _instance;
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A relative data folder has to be pinned down before the working folder moves away.
        var options = AppOptions.Parse(e.Args).WithFullPaths();
        var paths = options.ResolvePaths();

        WorkingFolder.MoveToAppFolder();
        WaitForClose(options.ReplacesProcess);

        _instance = new SingleInstance(paths.DataDirectory);

        if (!_instance.IsFirst && _instance.TrySend(options.CustomizePath))
        {
            Shutdown(0);
            return;
        }

        var log = new FileLog(paths.LogFile);
        var dialogs = new WpfDialogs();
        var guard = new CrashGuard(log, dialogs.ShowError);

        guard.Install(this, Dispatcher);

        // This starts ahead of everything slow, so a second start on a cold disk still gets through.
        var inbox = new FolderInbox();

        _instance.Listen(path => Dispatcher.InvokeAsync(() => inbox.Receive(path)));

        try
        {
            var registry = new CurrentUserRegistry();

            var services = new AppServices(
                paths,
                log: log,
                registry: registry,
                executablePath: Environment.ProcessPath,
                customDataDirectory: options.DataDirectory,
                appRoots: AppDiscoveryRoots.Detect(registry),
                updates: VelopackUpdateService.ForThisApp(log),
                elevation: new WindowsElevation());

            _services = services;
            services.PreviewWatch.Start();

            var shell = new ShellViewModel(services, dialogs, appearance: new WpfAppearance());
            var window = new MainWindow { DataContext = shell };

            MainWindow = window;
            window.Show();

            _ = StartAsync(shell, options.CustomizePath, guard);

            inbox.Open(path => OpenFolder(shell, path, guard));
        }
        catch (Exception ex)
        {
            guard.Report("Foldcraft could not start", ex);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.PreviewWatch.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// A restart as administrator begins before the old copy has closed, and that copy would
    /// otherwise count as already running. Waiting is all that's done with the number, so a wrong
    /// one costs a moment at most.
    /// </summary>
    private static void WaitForClose(int? processId)
    {
        if (processId is not { } id)
        {
            return;
        }

        try
        {
            using var previous = Process.GetProcessById(id);

            previous.WaitForExit(CloseTimeout);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // It has already gone, which is what this was waiting for.
        }
    }

    private static async Task StartAsync(ShellViewModel shell, string? customizePath, CrashGuard guard)
    {
        try
        {
            await shell.InitializeAsync();
        }
        catch (Exception ex)
        {
            // Nothing awaits this task, so without this a failure here would vanish without a word.
            guard.Report("Foldcraft could not finish starting", ex);
        }

        // Nothing waits for this. It runs off the UI thread and it can't fail.
        _ = shell.Updates.CheckAtStartupAsync();

        await OpenFolder(shell, customizePath, guard);
    }

    private static async Task OpenFolder(ShellViewModel shell, string? path, CrashGuard guard)
    {
        try
        {
            await shell.OpenFolderAsync(path);
        }
        catch (Exception ex)
        {
            guard.Report("A folder from outside the app could not be opened", ex);
        }
    }
}
