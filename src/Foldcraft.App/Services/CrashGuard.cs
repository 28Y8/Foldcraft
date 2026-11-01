using System.Windows;
using System.Windows.Threading;

namespace Foldcraft.App.Services;

/// <summary>
/// The last line of defense: anything no other code handled ends up here, is logged and
/// becomes a friendly message rather than a crash. Only a failure on a raw background thread
/// can still end the process, because .NET allows nothing else, and that is logged first.
/// </summary>
public sealed class CrashGuard(FileLog log, Action<string> showMessage)
{
    private int _showing;

    /// <summary>The wording shown to the user. It never contains exception text.</summary>
    public string FriendlyMessage =>
        $"Foldcraft ran into an unexpected problem and carried on. Details were saved to:\n\n{log.Path}";

    public void Install(Application application, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(dispatcher);

        application.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            Report("Unhandled error on the UI thread", e.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            dispatcher.BeginInvoke(() => Report("Unobserved task failure", e.Exception));
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Write($"Fatal error (terminating: {e.IsTerminating})", e.ExceptionObject as Exception);
    }

    /// <summary>Logs a failure and tells the user, unless a message is already on screen.</summary>
    public void Report(string source, Exception exception)
    {
        log.Write(source, exception);

        if (Interlocked.Exchange(ref _showing, 1) == 1)
        {
            return;
        }

        try
        {
            showMessage(FriendlyMessage);
        }
        catch (Exception ex)
        {
            log.Write("The error message itself could not be shown", ex);
        }
        finally
        {
            Volatile.Write(ref _showing, 0);
        }
    }
}
