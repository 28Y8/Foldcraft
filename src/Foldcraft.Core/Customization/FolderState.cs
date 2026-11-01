using System.Security.Cryptography;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Journal;

namespace Foldcraft.Core.Customization;

/// <summary>desktop.ini is something other than a small ordinary file, so it can't be backed up.</summary>
internal sealed class UnusableDesktopIniException(string message) : InvalidOperationException(message);

/// <summary>The date to leave on a folder that's being put back.</summary>
/// <param name="Value">The date.</param>
/// <param name="MoveOn">
/// True to leave it one step past that. Windows may have filed a date as having no picture while the
/// folder had its look, and it would keep to that once the look was gone, so a plain folder
/// would lose its preview for good.
/// </param>
/// <param name="Used">
/// The dates the folder had while it had a look, which moving on steps over for the same reason.
/// </param>
internal readonly record struct FolderDate(DateTime Value, bool MoveOn = false, IReadOnlySet<DateTime>? Used = null)
{
    public static FolderDate? Exactly(DateTime? date) => date is { } value ? new FolderDate(value) : null;

    public void LeaveOn(string folder, FileOperations ops)
    {
        if (MoveOn)
        {
            ops.TryMoveDateOn(folder, Value, Used);
        }
        else
        {
            ops.TryKeepLastWriteTimeUtc(folder, Value);
        }
    }
}

/// <summary>
/// What is on disk for one folder right now, and the one routine that puts a folder back
/// to a recorded state. Apply-rollback and undo share it, so they cannot drift apart.
/// <para>
/// Putting back never assigns recorded attributes. Foldcraft adds one or two known bits, and
/// undo takes off exactly those, so anything else that changed the folder keeps its change.
/// </para>
/// </summary>
internal sealed record FolderState(
    string FolderPath,
    FileAttributes FolderAttributes,
    bool DesktopIniExists,
    byte[]? DesktopIniBytes,
    FileAttributes? DesktopIniAttributes,
    DateTime FolderLastWriteUtc,
    DateTime? DesktopIniLastWriteUtc)
{
    /// <summary>desktop.ini is a few hundred bytes. Anything huge is not one we should back up.</summary>
    internal const int MaxDesktopIniBytes = 1024 * 1024;

    public string? DesktopIniSha256 => DesktopIniBytes is null ? null : Sha256Hex(DesktopIniBytes);

    public static string DesktopIniPath(string folderPath) => Path.Combine(folderPath, "desktop.ini");

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <param name="folderPath">The folder.</param>
    /// <param name="readReparseTag">Reads a reparse tag. Tests swap this.</param>
    /// <exception cref="UnusableDesktopIniException">desktop.ini is not an ordinary file we can back up.</exception>
    public static FolderState Capture(string folderPath, Func<string, uint?>? readReparseTag = null)
    {
        var folderAttributes = File.GetAttributes(folderPath);
        var folderTime = Directory.GetLastWriteTimeUtc(folderPath);
        var iniPath = DesktopIniPath(folderPath);
        var info = new FileInfo(iniPath);

        // Exists is false for a directory, and a dangling link has no target, so test attributes.
        if ((int)info.Attributes == -1)
        {
            return new FolderState(folderPath, folderAttributes, false, null, null, folderTime, null);
        }

        if ((info.Attributes & FileAttributes.Directory) != 0)
        {
            throw new UnusableDesktopIniException("desktop.ini in this folder is a directory.");
        }

        // Every file in a cloud-synced folder carries a cloud tag, and it's still a plain file.
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 && !IsCloudTag(readReparseTag, iniPath))
        {
            throw new UnusableDesktopIniException("desktop.ini in this folder is a link, which Foldcraft will not follow.");
        }

        if (info.Length > MaxDesktopIniBytes)
        {
            throw new UnusableDesktopIniException("desktop.ini in this folder is too large to back up safely.");
        }

        return new FolderState(folderPath, folderAttributes, true, File.ReadAllBytes(iniPath), info.Attributes, folderTime, info.LastWriteTimeUtc);
    }

    /// <summary>True when desktop.ini already matches the recorded original.</summary>
    public bool ContentMatches(ChangeRecord target) =>
        DesktopIniExists == target.DesktopIniExisted &&
        (!DesktopIniExists || DesktopIniBytes.AsSpan().SequenceEqual(target.OriginalDesktopIni));

    /// <summary>True when the content matches and none of the marks Foldcraft added remain.</summary>
    public bool FullyMatches(ChangeRecord target) =>
        ContentMatches(target) &&
        (FolderAttributes & target.AddedFolderMark) == 0 &&
        (!DesktopIniExists || (DesktopIniAttributes!.Value & target.AddedDesktopIniMarks) == 0);

    /// <summary>
    /// Puts a folder back as recorded. The folder's mark comes off last, because a read-only
    /// folder is what tells Explorer to read desktop.ini at all.
    /// </summary>
    /// <param name="target">The recorded state.</param>
    /// <param name="ops">The file operations to use.</param>
    /// <param name="folderDate">
    /// The folder's date to leave behind. It isn't always the recorded one: a file added
    /// since then moved the date on for a real reason, and undo has no business hiding that.
    /// </param>
    public static void RestoreTo(ChangeRecord target, FileOperations ops, FolderDate? folderDate)
    {
        var iniPath = DesktopIniPath(target.FolderPath);

        if (target.DesktopIniExisted)
        {
            var attributes = File.Exists(iniPath)
                ? File.GetAttributes(iniPath) & ~target.AddedDesktopIniMarks
                : target.OriginalDesktopIniAttributes!.Value;

            AtomicFile.Write(iniPath, target.OriginalDesktopIni!, attributes, ops);
            ops.TryKeepLastWriteTimeUtc(iniPath, target.OriginalDesktopIniLastWriteUtc);
        }
        else if (File.Exists(iniPath))
        {
            ops.SetAttributes(iniPath, FileAttributes.Normal);
            ops.DeleteFile(iniPath);
        }

        if (target.AddedFolderMark != 0)
        {
            ops.RemoveAttributes(target.FolderPath, target.AddedFolderMark);
        }

        folderDate?.LeaveOn(target.FolderPath, ops);
    }

    private static bool IsCloudTag(Func<string, uint?>? readReparseTag, string path) =>
        (readReparseTag ?? ReparseTags.Read)(path) is { } tag && ReparseTags.IsCloud(tag);

    /// <summary>
    /// Makes room for the recorded original when desktop.ini is a directory or a link. A link
    /// is removed, never followed. A directory with anything in it stays, because it isn't
    /// Foldcraft's to throw away, and that surfaces as a failure the caller can report.
    /// </summary>
    public static void ClearUnusableDesktopIni(string folderPath, FileOperations ops)
    {
        var iniPath = DesktopIniPath(folderPath);
        var attributes = new FileInfo(iniPath).Attributes;

        if ((int)attributes == -1)
        {
            return;
        }

        if ((attributes & FileAttributes.Directory) != 0)
        {
            try
            {
                ops.DeleteEmptyDirectory(iniPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Something else is stored where this folder keeps its settings, so it was left alone.", ex);
            }
        }
        else if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            ops.DeleteFile(iniPath);
        }
    }
}
