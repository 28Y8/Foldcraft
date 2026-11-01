using Foldcraft.App.Services.Updates;
using Foldcraft.Core;
using Foldcraft.Core.Settings;
using Foldcraft.Core.Shell;
using Foldcraft.Core.Updates;
using Velopack;

namespace Foldcraft.App;

/// <summary>
/// Where the process starts. The installer's hooks go first, so a hook run does its job and ends
/// before WPF, the single-instance check or any window exist. Everything after that is a normal
/// start, and <see cref="App"/> reads --customize and --data-dir as it always has.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var isHookRun = UpdateHooks.IsHookRun(args);

        // A source build or the plain zip has no updater, so it leaves Velopack and its log alone.
        if (isHookRun || UpdateHooks.IsPackaged(AppContext.BaseDirectory))
        {
            try
            {
                // Auto-apply stays off. It would install a downloaded update at the next start,
                // and an update installs only after the person has agreed to the restart.
                VelopackApp.Build()
                    .SetArgs(args)
                    .SetAutoApplyOnStartup(false)
                    .OnBeforeUninstallFastCallback(_ => RemoveRightClickEntry())
                    .Run();
            }
            catch (Exception)
            {
                // Updates are a convenience, so a problem here is no reason not to start.
            }
        }

        if (isHookRun)
        {
            return 0;
        }

        var app = new App();

        app.InitializeComponent();
        return app.Run();
    }

    private static void RemoveRightClickEntry()
    {
        if (Environment.ProcessPath is { } exePath)
        {
            UninstallCleanup.RemoveRightClick(new CurrentUserRegistry(), new SettingsStore(FoldcraftPaths.Default()), exePath);
        }
    }
}
