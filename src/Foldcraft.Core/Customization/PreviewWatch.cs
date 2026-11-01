using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Ini;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Customization;

/// <summary>
/// Takes a second look at folders that just lost a look. A process that drew such a folder keeps
/// treating it as customized for up to a minute. If it asks for a picture in that time and none
/// is saved for the folder's date, the shell saves the failure, and the folder stays plain until
/// its date changes. So once that time is over, a folder with a saved failure gets one more
/// date step, which the shell treats as new. Only a failure is acted on: a folder that kept or
/// never needed its picture is not touched, so its date stays exactly as undo left it.
/// <para>
/// The work is queued in the journal, so it isn't lost when Foldcraft closes. Whatever is left
/// is finished the next time it starts.
/// </para>
/// </summary>
public sealed class PreviewWatch : IDisposable
{
    /// <summary>
    /// How long after a look goes to check. Measured at 30 to 60 seconds, and at 75 no process
    /// failed. This has to be long enough, because a date step taken too soon can fail the same way.
    /// </summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(75);

    /// <summary>A date step can fail like the first, so it gets checked too, up to this many checks.</summary>
    private const int MostAttempts = 3;

    /// <summary>The other key that can name a folder's icon, used by older programs.</summary>
    private const string IconFileKey = "IconFile";

    private readonly FolderSafety _safety;
    private readonly ChangeJournal _journal;
    private readonly IShellNotifier _shell;
    private readonly IShellPreviews _previews;
    private readonly FileOperations _ops;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Lock _running = new();
    private Timer? _timer;

    public PreviewWatch(
        FolderSafety safety,
        ChangeJournal journal,
        IShellNotifier? shell = null,
        IShellPreviews? previews = null,
        FileOperations? operations = null,
        TimeProvider? time = null)
    {
        _safety = safety ?? throw new ArgumentNullException(nameof(safety));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _shell = shell ?? new ShellNotifier();
        _previews = previews ?? new ShellPreviews();
        _ops = operations ?? new FileOperations();
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Finishes what an earlier run left, then keeps up with new undos until disposed.</summary>
    public void Start()
    {
        lock (_gate)
        {
            _timer ??= new Timer(_ => Tick());
        }

        _journal.PreviewCheckQueued += ScheduleNext;
        ScheduleNext();
    }

    public void Dispose()
    {
        _journal.PreviewCheckQueued -= ScheduleNext;

        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Runs every check whose time has come.</summary>
    /// <returns>How long until the next one, or null if none is left.</returns>
    public TimeSpan? RunDue()
    {
        // One at a time: a check queued from here wakes the timer, and no check may run twice.
        lock (_running)
        {
            return RunDueAlone();
        }
    }

    private TimeSpan? RunDueAlone()
    {
        var snapshot = _journal.Read();
        var now = _time.GetUtcNow();

        foreach (var check in snapshot.PreviewChecks.Where(c => now - c.Queued >= Wait))
        {
            var stepped = check.Folders.Where(folder => Recover(folder, snapshot)).ToArray();

            // In this order: a crash between the two only means looking at a folder once more.
            if (stepped.Length > 0 && check.Attempt < MostAttempts)
            {
                _journal.QueuePreviewCheck(stepped, check.Attempt + 1);
            }

            _journal.MarkPreviewChecked(check.Id);
        }

        return TimeUntilNext(_journal.Read());
    }

    private TimeSpan? TimeUntilNext(JournalSnapshot snapshot) =>
        snapshot.PreviewChecks.Count == 0
            ? null
            : snapshot.PreviewChecks.Min(c => c.Queued + Wait - _time.GetUtcNow());

    /// <returns>True when the folder got a new date.</returns>
    private bool Recover(string folder, JournalSnapshot snapshot)
    {
        try
        {
            var verdict = _safety.Check(folder);

            if (!verdict.IsAllowed)
            {
                return false;
            }

            var path = verdict.ResolvedPath!;

            if (snapshot.Active.Any(e => e.FolderPath.Equals(path, StringComparison.OrdinalIgnoreCase)) ||
                !HasContents(path) ||
                NamesAnIcon(path) ||
                !_previews.HasSavedFailure(path) ||
                _ops.TryReadLastWriteTimeUtc(path) is not { } date)
            {
                return false;
            }

            _ops.TryMoveDateOn(path, date, snapshot.DatesWithALook(path, FolderIdentity.Read(path)));
            _shell.FolderChanged(path);

            return true;
        }
        catch (Exception)
        {
            // Deliberately broad: this is only ever a second look, and the folder is right on disk either way.
            return false;
        }
    }

    /// <summary>A folder holding nothing the shell would show has nothing to draw, so a saved failure is simply correct there.</summary>
    private static bool HasContents(string folder) =>
        new DirectoryInfo(folder).EnumerateFileSystemInfos().Any(entry =>
            (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 &&
            !entry.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase));

    /// <summary>A folder with an icon of its own shows that and no picture, so a failure is correct there too.</summary>
    private static bool NamesAnIcon(string folder)
    {
        try
        {
            var state = FolderState.Capture(folder);

            if (!state.DesktopIniExists)
            {
                return false;
            }

            var ini = DesktopIni.Parse(state.DesktopIniBytes!);

            return ini.GetValue(DesktopIni.ShellClassInfoSection, DesktopIni.IconResourceKey) is not null ||
                ini.GetValue(DesktopIni.ShellClassInfoSection, IconFileKey) is not null;
        }
        catch (Exception ex) when (ex is DesktopIniFormatException or UnusableDesktopIniException)
        {
            return true;
        }
    }

    private void ScheduleNext()
    {
        try
        {
            if (TimeUntilNext(_journal.Read()) is { } wait)
            {
                Schedule(wait);
            }
        }
        catch (Exception)
        {
            // Deliberately broad, because nothing above this on a timer's thread would catch it.
            // The queue is in the journal, so the next start picks it up.
        }
    }

    private void Tick()
    {
        try
        {
            if (RunDue() is { } wait)
            {
                Schedule(wait);
            }
        }
        catch (Exception)
        {
            // As above.
        }
    }

    private void Schedule(TimeSpan wait)
    {
        lock (_gate)
        {
            _timer?.Change(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, Timeout.InfiniteTimeSpan);
        }
    }
}
