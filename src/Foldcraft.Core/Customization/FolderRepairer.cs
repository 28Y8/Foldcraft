using System.Collections.ObjectModel;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Customization;

/// <summary>What a look over a customized folder found.</summary>
public enum IconCondition
{
    /// <summary>The icon is there and up to date.</summary>
    Healthy,

    /// <summary>The icon file is gone, so Explorer shows a plain folder.</summary>
    Missing,

    /// <summary>The icon file is there but can't be read as an icon.</summary>
    Damaged,

    /// <summary>The icon is fine, but an older version of Foldcraft drew it.</summary>
    OutOfDate,

    /// <summary>The icon needs repair, but this change was made before looks were recorded.</summary>
    NoRecordedLook,

    /// <summary>The icon needs repair, but the picture it was made from has gone or changed.</summary>
    PictureGone,

    /// <summary>The folder is gone, refused, half finished or edited since, so Repair has no business there.</summary>
    LeftAlone,
}

public enum RepairOutcome
{
    /// <summary>The icon was drawn again and the change is in the history.</summary>
    Repaired,

    /// <summary>Repair couldn't do anything for this folder. The message says why.</summary>
    NotRepairable,

    /// <summary>Repairing failed, and the folder is as it was.</summary>
    Failed,
}

/// <summary>One customized folder and the state of its icon.</summary>
public sealed record RepairFinding(string FolderPath, string IconPath, IconCondition Condition, string? Message = null)
{
    /// <summary>True when Repair can do something about this.</summary>
    public bool CanRepair => Condition is IconCondition.Missing or IconCondition.Damaged or IconCondition.OutOfDate;

    /// <summary>True when something is wrong, whether or not Repair can help.</summary>
    public bool NeedsAttention => Condition is not (IconCondition.Healthy or IconCondition.LeftAlone);
}

public sealed record FolderRepairResult(string FolderPath, RepairOutcome Outcome, string? Message = null);

/// <summary>
/// What a repair run did, as a list of the folders worth showing with counts for the rest. An
/// empty list alone says nothing: it could mean every icon is fine or nothing was looked at.
/// </summary>
/// <param name="results">The folders Repair acted on or can't help.</param>
/// <param name="total">How many folders there were to look at.</param>
/// <param name="healthy">How many were looked at and found fine.</param>
/// <param name="leftAlone">How many were left alone, because they're gone, changed or unfinished.</param>
/// <param name="wasCanceled">True when the run stopped before the last folder.</param>
public sealed class RepairReport(IList<FolderRepairResult> results, int total, int healthy, int leftAlone, bool wasCanceled)
    : ReadOnlyCollection<FolderRepairResult>(results)
{
    public int Total { get; } = total;

    public int Healthy { get; } = healthy;

    public int LeftAlone { get; } = leftAlone;

    public bool WasCanceled { get; } = wasCanceled;

    /// <summary>The folders nobody looked at.</summary>
    public int NotLookedAt => Total - Healthy - LeftAlone - Count;
}

/// <summary>
/// Finds customized folders whose icon has gone missing, been damaged or fallen behind, and draws
/// it again from the look recorded when it was applied. Nothing else about the folder changes.
/// <para>
/// An icon that's missing or damaged goes back under the same name, so the folder isn't touched.
/// An out-of-date icon needs a new name, so its folder is updated through the ordinary apply,
/// and undoing that returns it to the old icon. Both are journaled, so a repair never happens
/// off the record. Repair only acts on a folder still exactly as Foldcraft left it.
/// </para>
/// </summary>
public sealed class FolderRepairer
{
    private readonly FolderSafety _safety;
    private readonly ChangeJournal _journal;
    private readonly IconLibrary _icons;
    private readonly FolderCustomizer _customizer;
    private readonly IShellNotifier _shell;

    public FolderRepairer(
        FolderSafety safety,
        ChangeJournal journal,
        IconLibrary icons,
        FolderCustomizer customizer,
        IShellNotifier? shell = null)
    {
        _safety = safety ?? throw new ArgumentNullException(nameof(safety));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _customizer = customizer ?? throw new ArgumentNullException(nameof(customizer));
        _shell = shell ?? new ShellNotifier();
    }

    /// <summary>Looks at every folder Foldcraft has changed. This only reads.</summary>
    public IReadOnlyList<RepairFinding> Scan() => Newest(_journal.Read()).Select(Examine).ToArray();

    /// <summary>
    /// Repairs what can be repaired, as one change in the history. Folders that need attention but
    /// can't be helped are reported too. A healthy folder doesn't appear.
    /// </summary>
    /// <param name="cancellationToken">Stops before the next folder.</param>
    /// <param name="examined">Told how many folders have been looked at, after each one, healthy or not.</param>
    public RepairReport RepairAll(CancellationToken cancellationToken = default, IProgress<int>? examined = null)
    {
        var batchId = Guid.NewGuid();
        var results = new List<FolderRepairResult>();
        var entries = Newest(_journal.Read()).ToArray();
        var (healthy, leftAlone, done) = (0, 0, 0);
        var canceled = false;

        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            var finding = Examine(entry);

            if (finding.CanRepair)
            {
                results.Add(Repair(entry, finding, batchId));
            }
            else if (finding.NeedsAttention)
            {
                results.Add(new FolderRepairResult(finding.FolderPath, RepairOutcome.NotRepairable, finding.Message));
            }
            else if (finding.Condition == IconCondition.Healthy)
            {
                healthy++;
            }
            else
            {
                leftAlone++;
            }

            examined?.Report(++done);
        }

        return new RepairReport(results, entries.Length, healthy, leftAlone, canceled);
    }

    /// <summary>What's on a folder now is whatever its newest change put there.</summary>
    private static IEnumerable<JournalEntry> Newest(JournalSnapshot snapshot) => snapshot.Active
        .GroupBy(e => e.FolderPath, StringComparer.Ordinal)
        .Select(group => group.MaxBy(e => e.Sequence)!);

    private RepairFinding Examine(JournalEntry entry)
    {
        var folder = entry.FolderPath;
        var iconPath = entry.Change.IconPath;

        RepairFinding Found(IconCondition condition, string? message = null) => new(folder, iconPath, condition, message);

        if (entry.State == JournalEntryState.Pending)
        {
            return Found(IconCondition.LeftAlone, "This change didn't finish. Undo it from the history first.");
        }

        var verdict = _safety.Check(folder);

        if (!verdict.IsAllowed)
        {
            return Found(IconCondition.LeftAlone, verdict.Reason);
        }

        try
        {
            if (FolderState.Capture(folder).DesktopIniSha256 != entry.Change.WrittenSha256)
            {
                return Found(IconCondition.LeftAlone, "Something else changed this folder's look since, so it was left alone.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnusableDesktopIniException)
        {
            return Found(IconCondition.LeftAlone, ex.Message);
        }

        var fileState = IconLibrary.Inspect(iconPath);
        var condition = fileState == IconFileState.Damaged ? IconCondition.Damaged : IconCondition.Missing;

        if (!IconSpecCodec.TryReadRecorded(entry.Change.SpecJson, out var spec, out var imageHash))
        {
            return fileState == IconFileState.Intact
                ? Found(IconCondition.Healthy)
                : Found(IconCondition.NoRecordedLook, "This change was made by an earlier version, which didn't record the look.");
        }

        string expected;

        try
        {
            if (spec!.CustomImagePath is not null && _icons.ImageHash(spec) != imageHash)
            {
                throw new IconRenderException("The picture has changed since this look was made.");
            }

            expected = _icons.ExpectedPath(spec);
        }
        catch (IconRenderException ex)
        {
            return fileState == IconFileState.Intact
                ? Found(IconCondition.Healthy)
                : Found(IconCondition.PictureGone, ex.Message);
        }

        if (!expected.Equals(iconPath, StringComparison.OrdinalIgnoreCase))
        {
            return Found(IconCondition.OutOfDate);
        }

        return fileState == IconFileState.Intact ? Found(IconCondition.Healthy) : Found(condition);
    }

    private FolderRepairResult Repair(JournalEntry entry, RepairFinding finding, Guid batchId)
    {
        IconSpecCodec.TryReadRecorded(entry.Change.SpecJson, out var spec, out _);

        return finding.Condition == IconCondition.OutOfDate
            ? Reapply(entry.FolderPath, spec!, batchId)
            : Redraw(entry, spec!, batchId);
    }

    /// <summary>A new file name means a new settings file, which is an ordinary change to a folder.</summary>
    private FolderRepairResult Reapply(string folder, FolderIconSpec spec, Guid batchId)
    {
        var result = _customizer.Run([new FolderLook(folder, spec)], batchId, isRepair: true).Results[0];

        return result.Outcome == ApplyOutcome.Applied
            ? new FolderRepairResult(folder, RepairOutcome.Repaired)
            : new FolderRepairResult(folder, RepairOutcome.Failed, result.Message);
    }

    /// <summary>
    /// Writes the icon back under its own name. The folder isn't touched, so the record's "before"
    /// and "after" match, and it joins the stack as a step that reverses to nothing.
    /// </summary>
    private FolderRepairResult Redraw(JournalEntry entry, FolderIconSpec spec, Guid batchId)
    {
        var folder = entry.FolderPath;
        FolderState state;
        Guid recordId;

        try
        {
            state = FolderState.Capture(folder);

            recordId = _journal.RecordChange(new ChangeRecord
            {
                BatchId = batchId,
                FolderPath = folder,
                DesktopIniExisted = state.DesktopIniExists,
                OriginalDesktopIni = state.DesktopIniBytes,
                OriginalDesktopIniAttributes = state.DesktopIniAttributes,
                OriginalFolderAttributes = state.FolderAttributes,
                IconPath = entry.Change.IconPath,
                WrittenSha256 = entry.Change.WrittenSha256,
                FolderId = FolderIdentity.Read(folder),
                OriginalFolderLastWriteUtc = state.FolderLastWriteUtc,
                OriginalDesktopIniLastWriteUtc = state.DesktopIniLastWriteUtc,
                SpecJson = entry.Change.SpecJson,
                IsRepair = true,
            });
        }
        catch (Exception ex)
        {
            return new FolderRepairResult(folder, RepairOutcome.Failed, $"The undo journal could not be written, so nothing was changed: {ex.Message}");
        }

        try
        {
            _icons.Ensure(spec);
            _journal.MarkApplied(recordId, state.FolderLastWriteUtc);
        }
        catch (Exception ex)
        {
            TryMarkRolledBack(recordId);
            return new FolderRepairResult(folder, RepairOutcome.Failed, ex.Message);
        }

        try
        {
            _shell.FolderChanged(folder);
        }
        catch (Exception)
        {
            // The icon is correct on disk; Explorer will catch up on its next refresh.
        }

        return new FolderRepairResult(folder, RepairOutcome.Repaired);
    }

    private void TryMarkRolledBack(Guid recordId)
    {
        try
        {
            _journal.MarkRolledBack(recordId);
        }
        catch (Exception)
        {
            // The record stays pending, and undoing it later finds the folder already matches.
        }
    }
}
