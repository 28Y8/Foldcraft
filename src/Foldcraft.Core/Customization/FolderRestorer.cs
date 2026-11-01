using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Ini;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Customization;

/// <summary>What to do when desktop.ini is not what Foldcraft last wrote.</summary>
public enum ConflictPolicy
{
    /// <summary>Leave the folder alone and report the conflict. The default.</summary>
    Report,

    /// <summary>Put the recorded original back, discarding the other edit.</summary>
    Overwrite,

    /// <summary>Keep the folder as it is now and stop tracking the change.</summary>
    Forget,
}

public enum RestoreOutcome
{
    /// <summary>The folder was put back.</summary>
    Restored,

    /// <summary>The folder was already in the recorded state. Nothing needed doing.</summary>
    AlreadyRestored,

    /// <summary>The folder has no active Foldcraft changes.</summary>
    NothingToRestore,

    /// <summary>desktop.ini changed since Foldcraft wrote it. Nothing was touched.</summary>
    Conflict,

    /// <summary>The folder is gone. The change stays active in case it comes back.</summary>
    FolderMissing,

    /// <summary>The folder is no longer safe to touch, for example it became a junction.</summary>
    Refused,

    /// <summary>Restoring failed. The change stays active so it can be retried.</summary>
    Failed,

    /// <summary>The caller chose to keep the current state. The change is no longer tracked.</summary>
    Forgotten,

    /// <summary>
    /// The path now leads to some other folder, so nothing was touched, whatever the policy.
    /// The change stays active. It can be forgotten, or relocated to the real folder.
    /// </summary>
    DifferentFolder,

    /// <summary>The history now follows the folder to its new location. The folder was not touched.</summary>
    Relocated,
}

public sealed record FolderRestoreResult(
    string FolderPath,
    RestoreOutcome Outcome,
    string? Message = null,
    FolderRefusal? Refusal = null)
{
    /// <summary>True when Foldcraft made this folder, and it stayed because it's no longer only Foldcraft's.</summary>
    public bool FolderKept { get; init; }
}

public sealed record RestoreReport(IReadOnlyList<FolderRestoreResult> Results, int CorruptJournalRecords)
{
    public IEnumerable<FolderRestoreResult> Conflicts => Results.Where(r => r.Outcome == RestoreOutcome.Conflict);
    public IEnumerable<FolderRestoreResult> Missing => Results.Where(r => r.Outcome == RestoreOutcome.FolderMissing);

    public bool AllRestored => Results.All(r => r.Outcome is
        RestoreOutcome.Restored or RestoreOutcome.AlreadyRestored or
        RestoreOutcome.NothingToRestore or RestoreOutcome.Forgotten);
}

/// <summary>
/// Reverses recorded changes. Every operation is safe to repeat: the journal is only marked
/// after the folder is back, and a folder already in the target state is left alone, so a
/// retry after a crash at any point finishes the job. Problems are reported per folder and
/// never stop the rest of a batch.
/// <para>
/// Paths are compared exactly, after asking Windows for each folder's true spelling. Two
/// names that a case-insensitive comparer calls equal can be two different folders.
/// </para>
/// </summary>
public sealed class FolderRestorer
{
    private const string NotShownToBeOurs = "Nothing shows that Foldcraft made this folder, so it was left as it is.";

    private readonly FolderSafety _safety;
    private readonly ChangeJournal _journal;
    private readonly IShellNotifier _shell;
    private readonly FileOperations _ops;

    public FolderRestorer(
        FolderSafety safety,
        ChangeJournal journal,
        IShellNotifier? shell = null,
        FileOperations? operations = null)
    {
        _safety = safety ?? throw new ArgumentNullException(nameof(safety));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _shell = shell ?? new ShellNotifier();
        _ops = operations ?? new FileOperations();
    }

    /// <summary>
    /// Undoes the most recent operation. A batch counts as one operation, so undoing an apply
    /// to many folders puts all of them back. A folder that cannot be undone stays active,
    /// and calling this again retries it.
    /// </summary>
    public RestoreReport UndoLast(ConflictPolicy policy = ConflictPolicy.Report)
    {
        var snapshot = _journal.Read();
        var active = snapshot.Active.ToArray();

        return active.Length == 0
            ? new RestoreReport([], snapshot.CorruptRecords)
            : UndoBatch(snapshot, active[^1].Change.BatchId, policy, null, null);
    }

    /// <summary>
    /// Puts the folders of any recorded batch back to how they were just before it. A folder
    /// changed again since has those later changes reversed too, because nothing can be taken
    /// out of the middle of a stack of changes.
    /// </summary>
    /// <param name="batchId">The batch to reverse.</param>
    /// <param name="policy">How to treat a conflict.</param>
    /// <param name="onlyFolders">When set, only these folders of the batch are reversed.</param>
    /// <param name="progress">Told about each folder's result as it happens.</param>
    public RestoreReport UndoBatch(
        Guid batchId,
        ConflictPolicy policy = ConflictPolicy.Report,
        IReadOnlyCollection<string>? onlyFolders = null,
        IProgress<FolderRestoreResult>? progress = null) =>
        UndoBatch(_journal.Read(), batchId, policy, onlyFolders, progress);

    private RestoreReport UndoBatch(
        JournalSnapshot snapshot,
        Guid batchId,
        ConflictPolicy policy,
        IReadOnlyCollection<string>? onlyFolders,
        IProgress<FolderRestoreResult>? progress)
    {
        var active = snapshot.Active.ToArray();
        var key = FolderKeys();
        var only = onlyFolders?.Select(key).ToHashSet(StringComparer.Ordinal);

        var results = active
            .Where(e => e.Change.BatchId == batchId && (only is null || only.Contains(key(e.FolderPath))))
            .GroupBy(e => key(e.FolderPath), StringComparer.Ordinal)
            .OrderByDescending(group => group.Max(e => e.Sequence))
            .Select(group =>
            {
                var target = group.MinBy(e => e.Sequence)!;
                var stack = active.Where(e => key(e.FolderPath) == group.Key && e.Sequence >= target.Sequence).ToArray();
                return Reported(RestoreOne(target, stack, policy, snapshot), progress);
            })
            .ToArray();

        WatchPreviews(results);

        return new RestoreReport(results, snapshot.CorruptRecords);
    }

    /// <summary>Returns a folder to its state before Foldcraft's first change to it.</summary>
    public RestoreReport RestoreFolder(string folder, ConflictPolicy policy = ConflictPolicy.Report)
    {
        var verdict = _safety.Check(folder);
        var requested = verdict.ResolvedPath ?? verdict.NormalizedPath ?? folder;
        var snapshot = _journal.Read();
        var key = FolderKeys();

        var stack = snapshot.Active.Where(e => key(e.FolderPath) == requested).ToArray();

        // A folder that's gone has no true spelling to ask for, so the text is all there is.
        if (stack.Length == 0 && verdict.ResolvedPath is null)
        {
            var similar = snapshot.Active.FirstOrDefault(e => requested.Equals(e.FolderPath, StringComparison.OrdinalIgnoreCase));
            stack = snapshot.Active.Where(e => e.FolderPath == similar?.FolderPath).ToArray();
        }

        if (stack.Length == 0)
        {
            return new RestoreReport([new FolderRestoreResult(requested, RestoreOutcome.NothingToRestore)], snapshot.CorruptRecords);
        }

        var result = RestoreOne(stack[0], stack, policy, snapshot);

        WatchPreviews([result]);

        return new RestoreReport([result], snapshot.CorruptRecords);
    }

    /// <summary>Returns every folder Foldcraft has changed to its original state.</summary>
    public RestoreReport RestoreAll(
        ConflictPolicy policy = ConflictPolicy.Report,
        IProgress<FolderRestoreResult>? progress = null)
    {
        var snapshot = _journal.Read();
        var key = FolderKeys();

        var results = snapshot.Active
            .GroupBy(e => key(e.FolderPath), StringComparer.Ordinal)
            .OrderByDescending(group => group.Max(e => e.Sequence))
            .Select(group => Reported(RestoreOne(group.First(), group.ToArray(), policy, snapshot), progress))
            .ToArray();

        WatchPreviews(results);

        return new RestoreReport(results, snapshot.CorruptRecords);
    }

    /// <summary>
    /// The folders Foldcraft stopped tracking that still point at one of its icons, so they'd
    /// lose their look if the data folder went. This only reads.
    /// </summary>
    public IReadOnlyList<string> ForgottenFoldersUsingIcons() => _journal.Read().Entries
        .Where(e => e.State == JournalEntryState.Forgotten)
        .GroupBy(e => e.FolderPath, StringComparer.Ordinal)
        .Where(group => StillUsesIcon(group.Key, group))
        .Select(group => group.Key)
        .ToArray();

    /// <summary>
    /// Points the history of a folder that was renamed or moved at its new location, so its
    /// changes can be undone there. Nothing in any folder changes. The new location has to
    /// be provably the same folder: its ID matches, or its desktop.ini is exactly what
    /// Foldcraft last wrote, as it would be for a copy or a move between drives.
    /// </summary>
    /// <param name="recordedFolder">The location the history has now.</param>
    /// <param name="newLocation">Where the folder is now.</param>
    public FolderRestoreResult Relocate(string recordedFolder, string newLocation)
    {
        ArgumentNullException.ThrowIfNull(recordedFolder);
        ArgumentNullException.ThrowIfNull(newLocation);

        try
        {
            var snapshot = _journal.Read();
            var moving = snapshot.Active.Where(e => e.FolderPath == recordedFolder).ToArray();

            if (moving.Length == 0)
            {
                return new FolderRestoreResult(recordedFolder, RestoreOutcome.NothingToRestore);
            }

            var verdict = _safety.Check(newLocation);

            if (!verdict.IsAllowed)
            {
                return new FolderRestoreResult(recordedFolder, RestoreOutcome.Refused, verdict.Reason, verdict.Refusal);
            }

            var destination = verdict.ResolvedPath!;

            if (destination == recordedFolder)
            {
                return new FolderRestoreResult(recordedFolder, RestoreOutcome.DifferentFolder, "That is the location already recorded.");
            }

            // The original can't be left behind with no record, or a copy would take its history.
            if (moving[^1].Change.FolderId is { } recorded && FolderIdentity.Read(recordedFolder) == recorded)
            {
                return new FolderRestoreResult(recordedFolder, RestoreOutcome.DifferentFolder, "This folder is still at its recorded location.");
            }

            var identity = FolderIdentity.Read(destination);
            var stack = snapshot.Active
                .Where(e => e.FolderPath == recordedFolder || e.FolderPath == destination)
                .OrderBy(e => e.Sequence)
                .ToArray();

            var sameFolder = identity is not null && identity == moving[^1].Change.FolderId;

            if (!sameFolder && !CarriesOurLook(destination, stack))
            {
                return new FolderRestoreResult(recordedFolder, RestoreOutcome.DifferentFolder, "The chosen folder doesn't have the look Foldcraft gave this one.");
            }

            foreach (var entry in moving)
            {
                _journal.MarkMoved(entry.Id, destination, identity);
            }

            return new FolderRestoreResult(destination, RestoreOutcome.Relocated);
        }
        catch (Exception ex)
        {
            // Deliberately broad, as in RestoreOne: the answer is a result, never a crash.
            return new FolderRestoreResult(recordedFolder, RestoreOutcome.Failed, ex.Message);
        }
    }

    /// <summary>A folder that's gone needs nothing, and one that can't be read counts as still needing it.</summary>
    private static bool StillUsesIcon(string folder, IEnumerable<JournalEntry> entries)
    {
        if (!Directory.Exists(folder))
        {
            return false;
        }

        try
        {
            var state = FolderState.Capture(folder);

            return state.DesktopIniExists &&
                DesktopIni.Parse(state.DesktopIniBytes!).GetValue(DesktopIni.ShellClassInfoSection, DesktopIni.IconResourceKey) is { } icon &&
                entries.Any(e => icon.Contains(Path.GetFileName(e.Change.IconPath), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DesktopIniFormatException or UnusableDesktopIniException)
        {
            return true;
        }
    }

    /// <summary>
    /// Queues the folders that lost a look for <see cref="PreviewWatch"/>. This is best effort:
    /// the folders are already right on disk, so it must never turn a good restore into a failure.
    /// </summary>
    private void WatchPreviews(IReadOnlyCollection<FolderRestoreResult> results)
    {
        var folders = results
            .Where(r => r.Outcome is RestoreOutcome.Restored or RestoreOutcome.AlreadyRestored && Directory.Exists(r.FolderPath))
            .Select(r => r.FolderPath)
            .ToArray();

        if (folders.Length == 0)
        {
            return;
        }

        try
        {
            _journal.QueuePreviewCheck(folders);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // All that's lost is a second look at a folder that is very likely fine.
        }
    }

    private static FolderRestoreResult Reported(FolderRestoreResult result, IProgress<FolderRestoreResult>? progress)
    {
        progress?.Report(result);
        return result;
    }

    /// <summary>
    /// A lookup from any recorded path to the folder's true spelling, for grouping. It asks
    /// Windows once per distinct path, so a later rename that only changes case still groups.
    /// </summary>
    private Func<string, string> FolderKeys()
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal);

        return path =>
        {
            if (!known.TryGetValue(path, out var key))
            {
                key = _safety.Check(path).ResolvedPath ?? path;
                known[path] = key;
            }

            return key;
        };
    }

    /// <param name="target">The oldest change to reverse: its original state is the destination.</param>
    /// <param name="stack">Every active change being reversed, oldest first.</param>
    /// <param name="policy">How to treat a conflict.</param>
    /// <param name="snapshot">The journal, for the dates the folder has had.</param>
    private FolderRestoreResult RestoreOne(JournalEntry target, JournalEntry[] stack, ConflictPolicy policy, JournalSnapshot snapshot)
    {
        var folder = target.FolderPath;

        try
        {
            var verdict = _safety.Check(folder);

            if (verdict.Refusal == FolderRefusal.NotFound)
            {
                if (policy == ConflictPolicy.Forget)
                {
                    return Forget(folder, stack, "The folder no longer exists.");
                }

                // A crash before the folder was made leaves nothing to find, and nothing to do.
                if (target.Change.CreatedFolder && target.State == JournalEntryState.Pending)
                {
                    MarkUndone(stack);
                    return new FolderRestoreResult(folder, RestoreOutcome.AlreadyRestored);
                }

                return new FolderRestoreResult(folder, RestoreOutcome.FolderMissing, "The folder no longer exists.");
            }

            if (!verdict.IsAllowed)
            {
                return new FolderRestoreResult(folder, RestoreOutcome.Refused, verdict.Reason, verdict.Refusal);
            }

            // Whatever is at this path may have nothing to do with that record, so neither the
            // folder nor any settings file in it is Foldcraft's to take away. Newer changes still count.
            if (target.Change.CreatedFolder && !CreatedContent.IsShownToBeOurs(target.Change))
            {
                _journal.MarkUndone(target.Id);

                return stack.Length > 1
                    ? RestoreOne(stack[1], stack[1..], policy, snapshot)
                    : new FolderRestoreResult(folder, RestoreOutcome.AlreadyRestored, NotShownToBeOurs) { FolderKept = true };
            }

            FolderState? current = null;
            string? unusable = null;

            try
            {
                current = FolderState.Capture(folder);
            }
            catch (UnusableDesktopIniException ex)
            {
                unusable = ex.Message;
            }

            var used = snapshot.DatesWithALook(folder, stack[^1].Change.FolderId);
            var resuming = current?.DesktopIniSha256 != stack[^1].Change.WrittenSha256 || AtomicFile.HasStaleTemps(folder);
            var folderDate = DateToLeave(folder, stack, used, resuming);

            if (current is not null && current.FullyMatches(target.Change))
            {
                SweepTemps(folder);

                if (resuming && stack.Any(e => e.UndoPlan is not null))
                {
                    _ops.TryKeepLastWriteTimeUtc(FolderState.DesktopIniPath(folder), target.Change.OriginalDesktopIniLastWriteUtc);
                    folderDate.LeaveOn(folder, _ops);
                }
                else
                {
                    // The look went some other way, so nothing here was filed under this date while it had one.
                    _ops.TryKeepLastWriteTimeUtc(folder, folderDate.Value);
                }

                var settled = TakeAwayCreatedFolder(target.Change, folderDate);

                MarkUndone(stack);
                return settled is null
                    ? new FolderRestoreResult(folder, RestoreOutcome.AlreadyRestored)
                    : settled with { Outcome = RestoreOutcome.AlreadyRestored };
            }

            if (IsDifferentFolder(folder, current, stack[^1]))
            {
                return policy == ConflictPolicy.Forget
                    ? Forget(folder, stack, "Kept the folder as it is now.")
                    : new FolderRestoreResult(folder, RestoreOutcome.DifferentFolder, "A different folder is at this location now, so it was left alone.");
            }

            SweepTemps(folder);

            // Reaching the original content by another route is not a conflict: only the
            // marks are left to take off.
            if (current is null || (!current.ContentMatches(target.Change) && IsConflict(current, stack)))
            {
                switch (policy)
                {
                    case ConflictPolicy.Report:
                        return new FolderRestoreResult(folder, RestoreOutcome.Conflict,
                            unusable ?? "desktop.ini has changed since Foldcraft wrote it.");
                    case ConflictPolicy.Forget:
                        return Forget(folder, stack, "Kept the folder as it is now.");
                }
            }

            if (current is null)
            {
                FolderState.ClearUnusableDesktopIni(folder, _ops);
            }

            _journal.MarkUndoing(target.Id, new UndoPlan(folderDate.Value, folderDate.MoveOn));
            FolderState.RestoreTo(target.Change, _ops, folderDate);

            var outcome = TakeAwayCreatedFolder(target.Change, folderDate);

            MarkUndone(stack);
            Notify(folder);

            return outcome ?? new FolderRestoreResult(folder, RestoreOutcome.Restored);
        }
        catch (Exception ex)
        {
            // Deliberately broad: one folder must never be able to stop a restore-all.
            return new FolderRestoreResult(folder, RestoreOutcome.Failed, ex.Message);
        }
    }

    /// <summary>
    /// After a change that made its folder, the look is gone and this decides about the folder.
    /// It goes only if it's still exactly what Foldcraft made. Null means this change made none.
    /// </summary>
    private FolderRestoreResult? TakeAwayCreatedFolder(ChangeRecord change, FolderDate folderDate)
    {
        if (!change.CreatedFolder)
        {
            return null;
        }

        var fate = CreatedContent.Remove(change, _ops);

        switch (fate)
        {
            case CreatedContentFate.Removed:
                return new FolderRestoreResult(change.FolderPath, RestoreOutcome.Restored, "Removed the folder Foldcraft created.");
            case CreatedContentFate.DifferentFolder:
                return new FolderRestoreResult(
                    change.FolderPath,
                    RestoreOutcome.Restored,
                    "A different folder is at this location now, so it was left alone.") { FolderKept = true };
            case CreatedContentFate.CouldNotRemove:
                return new FolderRestoreResult(
                    change.FolderPath,
                    RestoreOutcome.Restored,
                    "The look was removed, but Windows wouldn't let Foldcraft remove the folder. You can delete it yourself.") { FolderKept = true };
            default:
                folderDate.LeaveOn(change.FolderPath, _ops);

                return new FolderRestoreResult(
                    change.FolderPath,
                    RestoreOutcome.Restored,
                    "The look was removed. The folder stayed, because something in it was added or changed after Foldcraft made it.") { FolderKept = true };
        }
    }

    /// <summary>
    /// The folder's date to leave behind. Each change recorded where it left the date, so walking
    /// back from the newest shows whether anything else moved it. If nothing did, the original
    /// date comes back exactly, and the shell's saved picture with it. If something did, someone
    /// really changed the folder, so that date stays, moved on as <see cref="FolderDate"/> explains.
    /// A change with no recorded outcome may have crashed before setting the date, so that one
    /// uses the record, and a record from an older version has nothing to compare, so the date stays.
    /// <para>
    /// An undo that was cut short moved the date itself, which this must not mistake for someone
    /// else's change, so its plan counts as where it got to and the walk goes on from there. While
    /// desktop.ini is still what the newest change wrote and no temp file was left behind, that undo
    /// never started, and whatever has happened to the folder since counts as usual.
    /// </para>
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="stack">The changes being reversed, oldest first.</param>
    /// <param name="used">Every date the folder had with a look on, to keep clear of when moving on.</param>
    /// <param name="resuming">True when an earlier undo got as far as changing the folder.</param>
    private static FolderDate DateToLeave(string folder, JournalEntry[] stack, IReadOnlySet<DateTime> used, bool resuming)
    {
        var date = Directory.GetLastWriteTimeUtc(folder);
        var planned = resuming ? Array.FindLastIndex(stack, e => e.UndoPlan is not null) : -1;

        if (planned >= 0)
        {
            var plan = stack[planned].UndoPlan!;

            if (plan.MoveOn || planned == 0)
            {
                return new FolderDate(plan.FolderDate, plan.MoveOn, used);
            }

            date = plan.FolderDate;
            stack = stack[..planned];
        }

        foreach (var entry in stack.Reverse())
        {
            if (entry.State == JournalEntryState.Pending && entry.Change.OriginalFolderLastWriteUtc is { } recorded)
            {
                date = recorded;
                continue;
            }

            if (entry.Change.AppliedFolderLastWriteUtc is not { } left)
            {
                return new FolderDate(date);
            }

            if (left != date)
            {
                return new FolderDate(date, MoveOn: true, used);
            }

            if (entry.Change.OriginalFolderLastWriteUtc is not { } before)
            {
                return new FolderDate(date);
            }

            date = before;
        }

        return new FolderDate(date);
    }

    /// <summary>
    /// True when the path now leads to another folder, so nothing recorded applies to it.
    /// An ID that changed but a desktop.ini that's exactly ours means the same folder came
    /// back another way, such as from a backup.
    /// </summary>
    private static bool IsDifferentFolder(string folder, FolderState? current, JournalEntry newest) =>
        newest.Change.FolderId is { } recorded &&
        FolderIdentity.Read(folder) is { } now &&
        recorded != now &&
        current?.DesktopIniSha256 != newest.Change.WrittenSha256;

    /// <summary>True when desktop.ini is exactly what the newest change wrote, with nothing edited on the way.</summary>
    private static bool CarriesOurLook(string folder, JournalEntry[] stack)
    {
        try
        {
            return FolderState.Capture(folder).DesktopIniSha256 == stack[^1].Change.WrittenSha256 && !WasEditedBetween(stack);
        }
        catch (UnusableDesktopIniException)
        {
            return false;
        }
    }

    /// <summary>
    /// A folder is in conflict unless desktop.ini is what the newest change wrote, and every
    /// older change was followed directly by the next. A restore across a stack would otherwise
    /// discard an edit that a later apply carried forward, even though the top looks untouched.
    /// </summary>
    private static bool IsConflict(FolderState current, JournalEntry[] stack) =>
        WasEditedBetween(stack) || !LooksAsNewestLeftIt(current, stack[^1]);

    /// <summary>A change with no recorded outcome may have crashed before writing, so its original also counts.</summary>
    private static bool LooksAsNewestLeftIt(FolderState current, JournalEntry newest) =>
        current.DesktopIniSha256 == newest.Change.WrittenSha256 ||
        (newest.State == JournalEntryState.Pending && current.ContentMatches(newest.Change));

    /// <summary>True when something else changed desktop.ini between two of Foldcraft's changes.</summary>
    private static bool WasEditedBetween(JournalEntry[] stack)
    {
        for (var i = 0; i + 1 < stack.Length; i++)
        {
            var (earlier, later) = (stack[i], stack[i + 1]);
            var found = OriginalSha256(later.Change);

            var unbroken = found == earlier.Change.WrittenSha256 ||
                (earlier.State == JournalEntryState.Pending && found == OriginalSha256(earlier.Change));

            if (!unbroken)
            {
                return true;
            }
        }

        return false;
    }

    private static string? OriginalSha256(ChangeRecord change) =>
        change.OriginalDesktopIni is { } bytes ? FolderState.Sha256Hex(bytes) : null;

    private FolderRestoreResult Forget(string folder, JournalEntry[] stack, string message)
    {
        foreach (var entry in stack.Reverse())
        {
            _journal.MarkForgotten(entry.Id);
        }

        return new FolderRestoreResult(folder, RestoreOutcome.Forgotten, message);
    }

    /// <summary>
    /// Marks newest first. A crash partway then leaves the oldest change active, whose
    /// original is the state the folder is already in, so the retry finishes cleanly.
    /// </summary>
    private void MarkUndone(JournalEntry[] stack)
    {
        foreach (var entry in stack.Reverse())
        {
            _journal.MarkUndone(entry.Id);
        }
    }

    /// <summary>Best effort: a leftover temp file must never turn a good restore into a failure.</summary>
    private static void SweepTemps(string folder)
    {
        try
        {
            AtomicFile.SweepStaleTemps(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file is harmless, and only ever one this class's names can match.
        }
    }

    private void Notify(string folder)
    {
        try
        {
            _shell.FolderChanged(folder);
        }
        catch (Exception)
        {
            // The folder is correct on disk; Explorer will catch up on its next refresh.
        }
    }
}
