using Foldcraft.Core.Icons;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Ini;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Safety;

namespace Foldcraft.Core.Customization;

/// <summary>
/// Applies an icon to folders: validate, journal, write, mark, notify.
/// <para>
/// This writes desktop.ini itself rather than calling SHGetSetFolderCustomSettings. That API
/// documents no guarantee that foreign content survives byte for byte, or that the write is
/// atomic, so nothing shows it could meet the preservation guarantees; Foldcraft's own
/// writer is tested against them. Failures never escape per-folder handling: each folder
/// reports its own result, deliberately whatever the exception type.
/// </para>
/// The only things changed in a target folder are desktop.ini and the folder's own
/// attributes and date. Sibling files and subfolders are never opened.
/// </summary>
public sealed class FolderCustomizer
{
    private readonly FolderSafety _safety;
    private readonly ChangeJournal _journal;
    private readonly IconLibrary _icons;
    private readonly IShellNotifier _shell;
    private readonly FileOperations _ops;

    public FolderCustomizer(
        FolderSafety safety,
        ChangeJournal journal,
        IconLibrary icons,
        IShellNotifier? shell = null,
        FileOperations? operations = null)
    {
        _safety = safety ?? throw new ArgumentNullException(nameof(safety));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _shell = shell ?? new ShellNotifier();
        _ops = operations ?? new FileOperations();
    }

    /// <summary>
    /// Applies a spec to every folder and reports each result. A failure on one folder never
    /// stops the others, and a spec that cannot be rendered fails every folder cleanly,
    /// before any is touched. This does not throw for per-folder problems.
    /// </summary>
    /// <param name="folders">The folders to change.</param>
    /// <param name="spec">The icon to apply.</param>
    /// <param name="cancellationToken">Stops before the next folder. A folder already started finishes.</param>
    /// <param name="progress">Told about each folder's result as it happens.</param>
    public BatchApplyResult Apply(
        IEnumerable<string> folders,
        FolderIconSpec spec,
        CancellationToken cancellationToken = default,
        IProgress<FolderApplyResult>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(spec);

        return ApplyEach(folders.Select(folder => new FolderLook(folder, spec)).ToArray(), cancellationToken, progress);
    }

    /// <summary>Applies a spec to a single folder.</summary>
    public FolderApplyResult Apply(string folder, FolderIconSpec spec) => Apply([folder], spec).Results[0];

    /// <summary>
    /// Gives each folder its own look, all as one undoable step. Behaves like
    /// <see cref="Apply(IEnumerable{string}, FolderIconSpec, CancellationToken, IProgress{FolderApplyResult})"/>,
    /// except that a look that can't be drawn fails only the folders meant to have it.
    /// </summary>
    public BatchApplyResult ApplyEach(
        IReadOnlyList<FolderLook> looks,
        CancellationToken cancellationToken = default,
        IProgress<FolderApplyResult>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(looks);
        return Run(looks, Guid.NewGuid(), isRepair: false, cancellationToken, progress);
    }

    /// <summary>
    /// True when a folder's settings already point at the icon for this look. Nothing is drawn or
    /// written, which is what lets a preview answer this. Anything unclear counts as false.
    /// </summary>
    public bool AlreadyShows(string resolvedFolder, FolderIconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(resolvedFolder);
        ArgumentNullException.ThrowIfNull(spec);

        try
        {
            var state = FolderState.Capture(resolvedFolder);

            return state.DesktopIniExists &&
                DesktopIni.Parse(state.DesktopIniBytes!).GetValue(DesktopIni.ShellClassInfoSection, DesktopIni.IconResourceKey) ==
                    $"{_icons.ExpectedPath(spec)},0";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DesktopIniFormatException or
            IconRenderException or UnusableDesktopIniException)
        {
            return false;
        }
    }

    /// <summary>
    /// Makes a new folder with its files and its look, as one recorded change. The record is
    /// written first, as with any change, so a crash at any step can be undone. Nothing that
    /// already exists is touched: a name that turns out to be taken just comes back as such.
    /// </summary>
    /// <param name="folder">The new folder's path. Its parent has already passed the safety check.</param>
    /// <param name="files">What goes inside.</param>
    /// <param name="spec">Its look.</param>
    /// <param name="batchId">The undo step this belongs to.</param>
    internal FolderApplyResult CreateFolder(string folder, IReadOnlyList<NewFolderFile> files, FolderIconSpec spec, Guid batchId)
    {
        var icon = Prepare(spec);

        if (icon.Problem is not null)
        {
            return new FolderApplyResult { RequestedPath = folder, Outcome = ApplyOutcome.Failed, Message = icon.Problem };
        }

        var iniBytes = NewDesktopIni(icon.Path);

        var change = new ChangeRecord
        {
            BatchId = batchId,
            FolderPath = folder,
            DesktopIniExisted = false,
            OriginalFolderAttributes = FileAttributes.Directory,
            IconPath = icon.Path,
            WrittenSha256 = FolderState.Sha256Hex(iniBytes),
            SpecJson = icon.SpecJson,
            CreatedFolder = true,
            CreatedFiles = files.Select(file => new CreatedFile(file.Name, FolderState.Sha256Hex(file.Bytes))).ToArray(),
        };

        Guid entryId;

        try
        {
            entryId = _journal.RecordChange(change);
        }
        catch (Exception ex)
        {
            return Result(folder, folder, ApplyOutcome.Failed, $"The undo journal could not be written, so nothing was created: {ex.Message}");
        }

        // Until this is true the folder at this path isn't Foldcraft's, so nothing may clean it up.
        var made = false;

        try
        {
            if (!_ops.CreateNewDirectory(folder))
            {
                TryMarkRolledBack(entryId);
                return new FolderApplyResult { RequestedPath = folder, Outcome = ApplyOutcome.Unchanged, NameTaken = true };
            }

            made = true;

            var verdict = _safety.Check(folder);

            if (!verdict.IsAllowed)
            {
                _ops.DeleteEmptyDirectory(folder);
                _journal.MarkRolledBack(entryId);

                return new FolderApplyResult
                {
                    RequestedPath = folder,
                    Outcome = ApplyOutcome.Refused,
                    Refusal = verdict.Refusal,
                    Message = verdict.Reason,
                };
            }

            // Its ID can't be known until it exists. This is what tells it from a later namesake.
            if (FolderIdentity.Read(folder) is { } identity)
            {
                _journal.MarkMoved(entryId, folder, identity);
                change = change with { FolderId = identity };
            }

            foreach (var file in files)
            {
                _ops.WriteNewFile(Path.Combine(folder, file.Name), file.Bytes);
            }

            AtomicFile.Write(FolderState.DesktopIniPath(folder), iniBytes, ChangeRecord.DesktopIniMarks, _ops);
            _ops.AddAttributes(folder, ChangeRecord.FolderMarkNeeded(change.OriginalFolderAttributes));
            _journal.MarkApplied(entryId, _ops.TryReadLastWriteTimeUtc(folder));
        }
        catch (Exception ex) when (made)
        {
            return RollBackCreation(folder, entryId, change, ex);
        }
        catch (Exception ex)
        {
            TryMarkRolledBack(entryId);
            return Result(folder, folder, ApplyOutcome.Failed, ex.Message);
        }

        Notify(folder);
        return Result(folder, folder, ApplyOutcome.Applied, null);
    }

    /// <summary>Applies looks on Repair's behalf, into a batch Repair already opened.</summary>
    internal BatchApplyResult Run(
        IReadOnlyList<FolderLook> looks,
        Guid batchId,
        bool isRepair,
        CancellationToken cancellationToken = default,
        IProgress<FolderApplyResult>? progress = null)
    {
        var results = new List<FolderApplyResult>(looks.Count);
        var prepared = new Dictionary<FolderIconSpec, PreparedIcon>();

        foreach (var spec in looks.Select(look => look.Spec).Distinct())
        {
            prepared[spec] = Prepare(spec);
        }

        void Record(FolderApplyResult result)
        {
            results.Add(result);
            progress?.Report(result);
        }

        // Resolved paths carry the true spelling, so names that differ only by case are two folders.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var look in looks)
        {
            var icon = prepared[look.Spec];

            if (icon.Problem is not null)
            {
                Record(new FolderApplyResult { RequestedPath = look.Folder, Outcome = ApplyOutcome.Failed, Message = icon.Problem });
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                Record(new FolderApplyResult { RequestedPath = look.Folder, Outcome = ApplyOutcome.Canceled });
            }
            else
            {
                Record(ApplyToFolder(look.Folder, icon, batchId, seen, isRepair));
            }
        }

        return new BatchApplyResult(batchId, results);
    }

    private PreparedIcon Prepare(FolderIconSpec spec)
    {
        try
        {
            var (iconPath, imageHash) = _icons.EnsureRecorded(spec);

            // The shell expands variables in IconResource, so such a path would quietly show
            // the default icon. It can't be escaped, so the data folder has to move.
            if (Environment.ExpandEnvironmentVariables(iconPath) != iconPath)
            {
                throw new InvalidOperationException(
                    "Windows would misread where Foldcraft keeps its icons, because that location has a name between percent signs. Use a data folder without one.");
            }

            return new PreparedIcon(iconPath, IconSpecCodec.ToRecordedJson(spec, imageHash), null);
        }
        catch (Exception ex)
        {
            return new PreparedIcon(string.Empty, string.Empty, ex.Message);
        }
    }

    private FolderApplyResult ApplyToFolder(string requestedPath, PreparedIcon icon, Guid batchId, HashSet<string> seen, bool isRepair)
    {
        FolderSafetyResult verdict;

        try
        {
            verdict = _safety.Check(requestedPath);
        }
        catch (Exception ex)
        {
            return new FolderApplyResult { RequestedPath = requestedPath, Outcome = ApplyOutcome.Failed, Message = ex.Message };
        }

        if (!verdict.IsAllowed)
        {
            return new FolderApplyResult
            {
                RequestedPath = requestedPath,
                Outcome = ApplyOutcome.Refused,
                Refusal = verdict.Refusal,
                Message = verdict.Reason,
            };
        }

        var folder = verdict.ResolvedPath!;

        // Two spellings of one folder in a batch must not stack two journal entries.
        if (!seen.Add(folder))
        {
            return Result(requestedPath, folder, ApplyOutcome.Unchanged, "This folder was already in the batch.");
        }

        FolderState before;
        ChangeRecord change;
        byte[] newBytes;
        FileAttributes newIniAttributes;
        FileAttributes folderMark;

        // Nothing has been changed in this block, so a failure here only needs reporting.
        try
        {
            before = FolderState.Capture(folder);
            var document = before.DesktopIniExists ? DesktopIni.Parse(before.DesktopIniBytes!) : DesktopIni.CreateEmpty();
            var iconResource = $"{icon.Path},0";

            newIniAttributes = (before.DesktopIniAttributes ?? 0) | ChangeRecord.DesktopIniMarks;
            folderMark = ChangeRecord.FolderMarkNeeded(before.FolderAttributes);

            if (document.GetValue(DesktopIni.ShellClassInfoSection, DesktopIni.IconResourceKey) == iconResource &&
                folderMark == 0 &&
                newIniAttributes == before.DesktopIniAttributes)
            {
                return Result(requestedPath, folder, ApplyOutcome.Unchanged, "The folder already shows this icon.");
            }

            document.SetValue(DesktopIni.ShellClassInfoSection, DesktopIni.IconResourceKey, iconResource);
            newBytes = document.ToBytes();

            // Rewriting as UTF-16 can double a file, and one past the limit could never be
            // backed up again.
            if (newBytes.Length > FolderState.MaxDesktopIniBytes)
            {
                return Result(requestedPath, folder, ApplyOutcome.Failed, "This folder's settings file is too large to change safely.");
            }

            change = new ChangeRecord
            {
                BatchId = batchId,
                FolderPath = folder,
                DesktopIniExisted = before.DesktopIniExists,
                OriginalDesktopIni = before.DesktopIniBytes,
                OriginalDesktopIniAttributes = before.DesktopIniAttributes,
                OriginalFolderAttributes = before.FolderAttributes,
                IconPath = icon.Path,
                WrittenSha256 = FolderState.Sha256Hex(newBytes),
                FolderId = FolderIdentity.Read(folder),
                OriginalFolderLastWriteUtc = before.FolderLastWriteUtc,
                OriginalDesktopIniLastWriteUtc = before.DesktopIniLastWriteUtc,
                SpecJson = icon.SpecJson,
                IsRepair = isRepair,
            };
        }
        catch (UnusableDesktopIniException)
        {
            return Result(requestedPath, folder, ApplyOutcome.Failed, "This folder's settings file can't be backed up, so the folder was left alone.");
        }
        catch (Exception ex)
        {
            return Result(requestedPath, folder, ApplyOutcome.Failed, ex.Message);
        }

        Guid entryId;

        try
        {
            entryId = _journal.RecordChange(change);
        }
        catch (Exception ex)
        {
            return Result(requestedPath, folder, ApplyOutcome.Failed, $"The undo journal could not be written, so the folder was left alone: {ex.Message}");
        }

        try
        {
            AtomicFile.Write(FolderState.DesktopIniPath(folder), newBytes, newIniAttributes, _ops);

            if (folderMark != 0)
            {
                _ops.AddAttributes(folder, folderMark);
            }

            // Writing the settings file moved the folder's date. It goes back to one tiny step past
            // where it was, far too small for a date column to show. That step is what makes the shell
            // stop using its saved picture of the folder. Undo puts the old date back, which brings the picture back.
            _journal.MarkApplied(entryId, _ops.TryMoveDateOn(folder, before.FolderLastWriteUtc));
        }
        catch (Exception ex)
        {
            return RollBack(requestedPath, entryId, change, ex);
        }

        Notify(folder);
        return Result(requestedPath, folder, ApplyOutcome.Applied, null);
    }

    private FolderApplyResult RollBack(string requestedPath, Guid entryId, ChangeRecord change, Exception cause)
    {
        try
        {
            // A step that failed early changed nothing, and repeating writes could fail the same way.
            if (CaptureIfUsable(change.FolderPath)?.FullyMatches(change) != true)
            {
                FolderState.RestoreTo(change, _ops, FolderDate.Exactly(change.OriginalFolderLastWriteUtc));
            }
            else
            {
                // Even a temp file that came and went moves the folder's date.
                _ops.TryKeepLastWriteTimeUtc(change.FolderPath, change.OriginalFolderLastWriteUtc);
            }
        }
        catch (Exception ex)
        {
            return Result(requestedPath, change.FolderPath, ApplyOutcome.FailedRollbackIncomplete,
                $"{cause.Message} Rolling back also failed ({ex.Message}); restore this folder to finish.");
        }

        try
        {
            _journal.MarkRolledBack(entryId);
        }
        catch (Exception)
        {
            // The entry stays pending. A later restore finds the folder already matches, so this is harmless.
        }

        Notify(change.FolderPath);
        return Result(requestedPath, change.FolderPath, ApplyOutcome.Failed, $"{cause.Message} The folder was put back as it was.");
    }

    /// <summary>Takes back whatever got made. The entry stays pending if that fails, so undo can finish it.</summary>
    private FolderApplyResult RollBackCreation(string folder, Guid entryId, ChangeRecord change, Exception cause)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                AtomicFile.SweepStaleTemps(folder);
                FolderState.RestoreTo(change, _ops, null);

                if (CreatedContent.Remove(change, _ops) != CreatedContentFate.Removed)
                {
                    throw new IOException("The new folder could not be removed.");
                }
            }

            _journal.MarkRolledBack(entryId);
        }
        catch (Exception ex)
        {
            return Result(folder, folder, ApplyOutcome.FailedRollbackIncomplete,
                $"{cause.Message} Taking the new folder away also failed ({ex.Message}); undo it from the history to finish.");
        }

        Notify(folder);
        return Result(folder, folder, ApplyOutcome.Failed, $"{cause.Message} The new folder was taken away again.");
    }

    private void TryMarkRolledBack(Guid entryId)
    {
        try
        {
            _journal.MarkRolledBack(entryId);
        }
        catch (Exception)
        {
            // The entry stays pending. Undoing it finds nothing of Foldcraft's, so this is harmless.
        }
    }

    private static byte[] NewDesktopIni(string iconPath)
    {
        var document = DesktopIni.CreateEmpty();
        document.SetValue(DesktopIni.ShellClassInfoSection, DesktopIni.IconResourceKey, $"{iconPath},0");
        return document.ToBytes();
    }

    /// <summary>A file that has outgrown the limit is no reason to leave a half-made change in place.</summary>
    private static FolderState? CaptureIfUsable(string folder)
    {
        try
        {
            return FolderState.Capture(folder);
        }
        catch (UnusableDesktopIniException)
        {
            return null;
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

    private static FolderApplyResult Result(string requested, string folder, ApplyOutcome outcome, string? message) => new()
    {
        RequestedPath = requested,
        ResolvedPath = folder,
        Outcome = outcome,
        Message = message,
    };

    /// <summary>A look that's ready to use, or why it isn't.</summary>
    private sealed record PreparedIcon(string Path, string SpecJson, string? Problem);
}
