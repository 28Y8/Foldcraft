using Foldcraft.Core.Customization;

namespace Foldcraft.Core.Journal;

/// <summary>Where a recorded change stands, derived by replaying the journal.</summary>
public enum JournalEntryState
{
    /// <summary>Recorded before the folder was touched; no outcome was recorded. Usually a crash.</summary>
    Pending,

    /// <summary>The change completed.</summary>
    Applied,

    /// <summary>The change failed partway and the folder was put back.</summary>
    RolledBack,

    /// <summary>The change was reversed by undo or restore.</summary>
    Undone,

    /// <summary>The caller chose to keep the folder as it was and stop tracking this change.</summary>
    Forgotten,
}

/// <summary>A file Foldcraft made in a folder it created, and how to recognize it unchanged.</summary>
public sealed record CreatedFile(string Name, string Sha256);

/// <summary>What Foldcraft is about to do to a folder, captured before it is touched.</summary>
public sealed record ChangeRecord
{
    /// <summary>The marks desktop.ini needs before Explorer treats it as a settings file.</summary>
    public const FileAttributes DesktopIniMarks = FileAttributes.Hidden | FileAttributes.System;

    public required Guid BatchId { get; init; }
    public required string FolderPath { get; init; }
    public required bool DesktopIniExisted { get; init; }

    /// <summary>The exact original file. Null only when no desktop.ini existed.</summary>
    public byte[]? OriginalDesktopIni { get; init; }

    public FileAttributes? OriginalDesktopIniAttributes { get; init; }
    public required FileAttributes OriginalFolderAttributes { get; init; }

    /// <summary>The icon this change references.</summary>
    public required string IconPath { get; init; }

    /// <summary>SHA-256 of what Foldcraft will write, used to notice later edits by anything else.</summary>
    public required string WrittenSha256 { get; init; }

    /// <summary>
    /// Which folder this was, from <see cref="Infrastructure.FolderIdentity"/>. Null where the
    /// file system has no such ID, and then no decision depends on it.
    /// </summary>
    public string? FolderId { get; init; }

    /// <summary>The folder's date before this change. Null in records from older versions.</summary>
    public DateTime? OriginalFolderLastWriteUtc { get; init; }

    /// <summary>
    /// The folder's date once this change finished. Still there at undo, it means nothing else
    /// has touched the folder since. Null until the change finishes, and in records from older versions.
    /// </summary>
    public DateTime? AppliedFolderLastWriteUtc { get; init; }

    /// <summary>The original desktop.ini's date. Null if there wasn't one, or in records from older versions.</summary>
    public DateTime? OriginalDesktopIniLastWriteUtc { get; init; }

    /// <summary>
    /// The look this change drew, from <see cref="Icons.IconSpecCodec"/>, so Repair can draw it
    /// again. Null in records from older versions, which only kept the icon's file name.
    /// </summary>
    public string? SpecJson { get; init; }

    /// <summary>True when Repair made this change rather than the person using the app.</summary>
    public bool IsRepair { get; init; }

    /// <summary>
    /// True when this change made the folder as well as its look. Undo then takes the folder
    /// away too, but only while it holds nothing except what Foldcraft put there.
    /// </summary>
    public bool CreatedFolder { get; init; }

    /// <summary>What Foldcraft put in a folder it created. Empty for every other change.</summary>
    public IReadOnlyList<CreatedFile> CreatedFiles { get; init; } = [];

    /// <summary>
    /// True when only the icon library was written, so there is nothing in the folder to undo.
    /// Its record still takes a place in the stack, which reverses cleanly as a change to nothing.
    /// </summary>
    public bool LeftFolderAlone =>
        OriginalDesktopIni is { } original && FolderState.Sha256Hex(original) == WrittenSha256;

    /// <summary>The only bit Foldcraft adds to a folder, and only when it has neither mark Explorer honors.</summary>
    public FileAttributes AddedFolderMark => FolderMarkNeeded(OriginalFolderAttributes);

    /// <summary>The bits Foldcraft added to an existing desktop.ini. Undo takes off these and nothing else.</summary>
    public FileAttributes AddedDesktopIniMarks => DesktopIniMarks & ~(OriginalDesktopIniAttributes ?? 0);

    /// <summary>Explorer only reads desktop.ini in a folder marked ReadOnly or System.</summary>
    public static FileAttributes FolderMarkNeeded(FileAttributes folder) =>
        (folder & (FileAttributes.ReadOnly | FileAttributes.System)) == 0 ? FileAttributes.ReadOnly : 0;
}

/// <summary>A change as replayed from the journal.</summary>
public sealed record JournalEntry
{
    public required Guid Id { get; init; }

    /// <summary>Position in the journal. Later changes have higher numbers.</summary>
    public required long Sequence { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
    public required ChangeRecord Change { get; init; }
    public required JournalEntryState State { get; init; }

    public string FolderPath => Change.FolderPath;

    /// <summary>
    /// The date an undo reaching back to this change decided to leave, recorded before it touched
    /// the folder. Undoing moves the date, so a retry after a crash can't work this out again.
    /// </summary>
    public UndoPlan? UndoPlan { get; init; }

    /// <summary>True while the change is still in effect, or might be.</summary>
    public bool IsActive => State is JournalEntryState.Pending or JournalEntryState.Applied;
}

/// <summary>The date an undo will leave on a folder.</summary>
/// <param name="FolderDate">The date.</param>
/// <param name="MoveOn">True to leave it one step past that.</param>
public sealed record UndoPlan(DateTime FolderDate, bool MoveOn);

/// <summary>Folders to look at again once the shell has had time to forget their look.</summary>
public sealed record PreviewCheck(Guid Id, DateTimeOffset Queued, IReadOnlyList<string> Folders, int Attempt);

/// <summary>Everything readable from the journal at one moment.</summary>
public sealed record JournalSnapshot(IReadOnlyList<JournalEntry> Entries, int CorruptRecords)
{
    public IEnumerable<JournalEntry> Active => Entries.Where(e => e.IsActive);

    /// <summary>The preview checks that haven't finished.</summary>
    public IReadOnlyList<PreviewCheck> PreviewChecks { get; init; } = [];

    /// <summary>
    /// Every date a look of Foldcraft's left on a folder, including looks since undone. The shell
    /// may have saved "no picture" under any of them, so a plain folder must not be left on one.
    /// </summary>
    public IReadOnlySet<DateTime> DatesWithALook(string folder, string? folderId) => Entries
        .Where(e => e.FolderPath.Equals(folder, StringComparison.OrdinalIgnoreCase) || (folderId is not null && e.Change.FolderId == folderId))
        .Select(e => e.Change.AppliedFolderLastWriteUtc)
        .OfType<DateTime>()
        .ToHashSet();
}
