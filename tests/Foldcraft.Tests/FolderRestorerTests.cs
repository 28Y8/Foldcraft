using System.Text;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Ini;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Safety;

namespace Foldcraft.Tests;

public sealed class FolderRestorerTests : IDisposable
{
    private const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;

    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private static string Icon(string folder) =>
        DesktopIni.Load(Sandbox.IniPath(folder)).GetValue(".ShellClassInfo", "IconResource")!;

    public static TheoryData<byte[], FileAttributes> Originals()
    {
        byte[] utf8 = [.. Encoding.UTF8.GetBytes("[.ShellClassInfo]\nInfoTip=é\n")];
        byte[] utf8Bom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("[.ShellClassInfo]\r\nIconResource=x.ico,1\r\n")];
        byte[] utf16 = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("[ViewState]\r\nMode=\r\n")];
        byte[] ansi = [(byte)'[', (byte)'A', (byte)']', 0x0D, 0x0A, (byte)'k', (byte)'=', 0xE9, 0xFC];
        byte[] noTrailingNewline = [.. Encoding.ASCII.GetBytes("[A]\r\nk=v")];

        return new TheoryData<byte[], FileAttributes>
        {
            { utf8, HiddenSystem },
            { utf8Bom, FileAttributes.Hidden },
            { utf16, HiddenSystem | FileAttributes.ReadOnly },
            { ansi, FileAttributes.Archive },
            { noTrailingNewline, FileAttributes.ReadOnly | FileAttributes.Archive },
        };
    }

    [Theory]
    [MemberData(nameof(Originals))]
    public void Undo_restores_bytes_and_attributes_exactly(byte[] original, FileAttributes attributes)
    {
        var folder = _box.NewFolder();
        Sandbox.PlantIni(folder, original, attributes);
        File.SetAttributes(folder, FileAttributes.Directory | FileAttributes.Hidden);
        var folderAttributes = File.GetAttributes(folder);

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Assert.NotEqual(original, File.ReadAllBytes(Sandbox.IniPath(folder)));

        var report = _box.Restorer().UndoLast();

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.Equal(attributes, File.GetAttributes(Sandbox.IniPath(folder)));
        Assert.Equal(folderAttributes, File.GetAttributes(folder));
    }

    [Fact]
    public void Undo_deletes_desktop_ini_when_there_was_none()
    {
        var folder = _box.NewFolder();
        var before = File.GetAttributes(folder);

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var report = _box.Restorer().UndoLast();

        Assert.True(report.AllRestored);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
        Assert.Equal(before, File.GetAttributes(folder));
    }

    [Fact]
    public void Undo_goes_back_one_change_at_a_time()
    {
        var folder = _box.NewFolder();
        var customizer = _box.Customizer();
        var restorer = _box.Restorer();

        customizer.Apply(folder, Sandbox.BlueStar);
        var blue = Icon(folder);
        customizer.Apply(folder, Sandbox.GreenHeart);
        Assert.NotEqual(blue, Icon(folder));

        restorer.UndoLast();
        Assert.Equal(blue, Icon(folder));

        restorer.UndoLast();
        Assert.Empty(Directory.GetFileSystemEntries(folder));

        Assert.Empty(restorer.UndoLast().Results);
    }

    [Fact]
    public void Undo_treats_a_batch_as_one_change()
    {
        var a = _box.NewFolder("a");
        var b = _box.NewFolder("b");
        var earlier = _box.NewFolder("earlier");
        var customizer = _box.Customizer();

        customizer.Apply(earlier, Sandbox.GreenHeart);
        customizer.Apply([a, b], Sandbox.BlueStar);

        var report = _box.Restorer().UndoLast();

        Assert.Equal(2, report.Results.Count);
        Assert.False(File.Exists(Sandbox.IniPath(a)));
        Assert.False(File.Exists(Sandbox.IniPath(b)));
        Assert.True(File.Exists(Sandbox.IniPath(earlier)));
    }

    [Fact]
    public void Restore_folder_returns_to_the_state_before_the_first_change()
    {
        var folder = _box.NewFolder();
        var other = _box.NewFolder("other");
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);
        var customizer = _box.Customizer();

        customizer.Apply(folder, Sandbox.BlueStar);
        customizer.Apply(other, Sandbox.BlueStar);
        customizer.Apply(folder, Sandbox.GreenHeart);

        var report = _box.Restorer().RestoreFolder(folder);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.False(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
        Assert.True(File.Exists(Sandbox.IniPath(other)));
        Assert.Single(_box.Journal.Read().Active);
    }

    [Fact]
    public void Restore_folder_accepts_any_spelling()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var report = _box.Restorer().RestoreFolder(@"\\?\" + folder.ToUpperInvariant() + @"\");

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
    }

    [Fact]
    public void Restore_all_restores_every_folder_past_any_number_of_changes()
    {
        var a = _box.NewFolder("a");
        var b = _box.NewFolder("b");
        var customizer = _box.Customizer();

        customizer.Apply(a, Sandbox.BlueStar);
        customizer.Apply(a, Sandbox.GreenHeart);
        customizer.Apply(b, Sandbox.BlueStar);

        var report = _box.Restorer().RestoreAll();

        Assert.Equal(2, report.Results.Count);
        Assert.True(report.AllRestored);
        Assert.Empty(Directory.GetFileSystemEntries(a));
        Assert.Empty(Directory.GetFileSystemEntries(b));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void A_deleted_folder_is_reported_and_does_not_stop_restore_all()
    {
        var kept = _box.NewFolder("kept");
        var deleted = _box.NewFolder("deleted");
        var alsoKept = _box.NewFolder("also-kept");
        _box.Customizer().Apply([kept, deleted, alsoKept], Sandbox.BlueStar);

        File.SetAttributes(deleted, FileAttributes.Normal);
        File.SetAttributes(Sandbox.IniPath(deleted), FileAttributes.Normal);
        Directory.Delete(deleted, recursive: true);

        var report = _box.Restorer().RestoreAll();

        var missing = Assert.Single(report.Missing);
        Assert.Equal(deleted, missing.FolderPath, ignoreCase: true);
        Assert.Equal(3, report.Results.Count);
        Assert.False(report.AllRestored);
        Assert.Empty(Directory.GetFileSystemEntries(kept));
        Assert.Empty(Directory.GetFileSystemEntries(alsoKept));
    }

    [Fact]
    public void A_missing_folder_stays_recorded_and_restores_if_it_returns()
    {
        var folder = _box.NewFolder();
        var away = folder + "-away";
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Directory.Move(folder, away);
        Assert.Single(_box.Restorer().RestoreAll().Missing);
        Assert.Single(_box.Restorer().RestoreFolder(folder).Missing);

        Directory.Move(away, folder);
        var report = _box.Restorer().RestoreAll();

        Assert.True(report.AllRestored);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void A_missing_folder_can_be_forgotten()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        File.SetAttributes(folder, FileAttributes.Normal);
        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        Directory.Delete(folder, recursive: true);

        var report = _box.Restorer().RestoreAll(ConflictPolicy.Forget);

        Assert.Equal(RestoreOutcome.Forgotten, Assert.Single(report.Results).Outcome);
        Assert.Empty(_box.Journal.Read().Active);
    }

    private (string Folder, byte[] Edited) AppliedThenEditedElsewhere()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var path = Sandbox.IniPath(folder);
        File.SetAttributes(path, FileAttributes.Normal);
        var edited = Encoding.UTF8.GetBytes("[.ShellClassInfo]\r\nIconResource=someone-else.ico,0\r\n");
        File.WriteAllBytes(path, edited);
        File.SetAttributes(path, HiddenSystem);

        return (folder, edited);
    }

    [Fact]
    public void A_conflict_is_reported_and_the_other_edit_survives()
    {
        var (folder, edited) = AppliedThenEditedElsewhere();

        var report = _box.Restorer().UndoLast();

        Assert.Single(report.Conflicts);
        Assert.False(report.AllRestored);
        Assert.Equal(edited, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.True(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
        Assert.Single(_box.Journal.Read().Active);
    }

    [Fact]
    public void Every_restore_path_detects_a_conflict()
    {
        var (folder, _) = AppliedThenEditedElsewhere();
        var restorer = _box.Restorer();

        Assert.Single(restorer.RestoreFolder(folder).Conflicts);
        Assert.Single(restorer.RestoreAll().Conflicts);
    }

    [Fact]
    public void The_caller_can_overwrite_a_conflict()
    {
        var (folder, _) = AppliedThenEditedElsewhere();

        var report = _box.Restorer().UndoLast(ConflictPolicy.Overwrite);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void The_caller_can_keep_the_other_edit_and_stop_tracking()
    {
        var (folder, edited) = AppliedThenEditedElsewhere();

        var report = _box.Restorer().UndoLast(ConflictPolicy.Forget);

        Assert.Equal(RestoreOutcome.Forgotten, Assert.Single(report.Results).Outcome);
        Assert.Equal(edited, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void A_desktop_ini_deleted_elsewhere_is_a_conflict_when_there_was_one_before()
    {
        var folder = _box.NewFolder();
        Sandbox.PlantIni(folder, Encoding.UTF8.GetBytes("[A]\nb=1\n"));
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        File.Delete(Sandbox.IniPath(folder));

        Assert.Single(_box.Restorer().RestoreFolder(folder).Conflicts);
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void A_desktop_ini_deleted_elsewhere_is_no_conflict_when_there_was_none_before()
    {
        var folder = _box.NewFolder();
        var attributes = File.GetAttributes(folder);
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        File.Delete(Sandbox.IniPath(folder));

        var report = _box.Restorer().RestoreFolder(folder);

        Assert.True(report.AllRestored);
        Assert.Equal(attributes, File.GetAttributes(folder));
    }

    [Fact]
    public void Attribute_changes_by_others_are_not_a_conflict()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Hidden);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(_box.Restorer().UndoLast().Results).Outcome);
    }

    [Fact]
    public void A_conflict_in_a_batch_does_not_stop_the_other_folders()
    {
        var clean = _box.NewFolder("clean");
        var edited = _box.NewFolder("edited");
        _box.Customizer().Apply([clean, edited], Sandbox.BlueStar);

        var path = Sandbox.IniPath(edited);
        File.SetAttributes(path, FileAttributes.Normal);
        File.AppendAllText(path, "; touched");

        var report = _box.Restorer().RestoreAll();

        Assert.Single(report.Conflicts);
        Assert.Empty(Directory.GetFileSystemEntries(clean));
    }

    [Fact]
    public void Restoring_twice_is_harmless()
    {
        var folder = _box.NewFolder();
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var restorer = _box.Restorer();

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(restorer.RestoreFolder(folder).Results).Outcome);

        Assert.Equal(RestoreOutcome.NothingToRestore, Assert.Single(restorer.RestoreFolder(folder).Results).Outcome);
        Assert.Empty(restorer.RestoreAll().Results);
        Assert.Empty(restorer.UndoLast().Results);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void A_crash_between_restoring_and_marking_is_finished_by_the_next_run()
    {
        var folder = _box.NewFolder();
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var journalBefore = File.ReadAllBytes(_box.Paths.JournalFile);

        _box.Restorer().RestoreAll();

        // Forget the completion, as if the process died right after putting the folder back.
        File.WriteAllBytes(_box.Paths.JournalFile, journalBefore);

        var report = _box.Restorer().RestoreAll();

        Assert.Equal(RestoreOutcome.AlreadyRestored, Assert.Single(report.Results).Outcome);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void A_crash_between_marking_a_stack_of_changes_is_not_mistaken_for_a_conflict()
    {
        var folder = _box.NewFolder();
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);
        var customizer = _box.Customizer();
        customizer.Apply(folder, Sandbox.BlueStar);
        customizer.Apply(folder, Sandbox.GreenHeart);

        Assert.True(_box.Restorer().RestoreFolder(folder).AllRestored);

        // Lose the final "undone" record and the preview check queued after it, as if the process
        // died between the two marks.
        var lines = File.ReadAllBytes(_box.Paths.JournalFile);
        var checkLineStart = Array.LastIndexOf(lines, (byte)'\n', lines.Length - 2);
        var lastLineStart = Array.LastIndexOf(lines, (byte)'\n', checkLineStart - 1) + 1;
        File.WriteAllBytes(_box.Paths.JournalFile, lines[..lastLineStart]);
        Assert.Single(_box.Journal.Read().Active);

        var report = _box.Restorer().RestoreFolder(folder);

        Assert.Equal(RestoreOutcome.AlreadyRestored, Assert.Single(report.Results).Outcome);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void A_crash_right_after_journaling_is_not_mistaken_for_a_conflict()
    {
        var folder = _box.NewFolder();
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);

        _box.Journal.RecordChange(new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = _box.Safety.Check(folder).ResolvedPath!,
            DesktopIniExisted = true,
            OriginalDesktopIni = original,
            OriginalDesktopIniAttributes = File.GetAttributes(Sandbox.IniPath(folder)),
            OriginalFolderAttributes = File.GetAttributes(folder),
            IconPath = "never-used.ico",
            WrittenSha256 = "never-written",
        });

        var report = _box.Restorer().RestoreAll();

        Assert.Equal(RestoreOutcome.AlreadyRestored, Assert.Single(report.Results).Outcome);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void A_crash_after_writing_but_before_marking_applied_is_undone_normally()
    {
        var folder = _box.NewFolder();
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        // Drop the final "applied" record, leaving what a crash at that moment would leave.
        var lines = File.ReadAllBytes(_box.Paths.JournalFile);
        var lastLineStart = Array.LastIndexOf(lines, (byte)'\n', lines.Length - 2) + 1;
        File.WriteAllBytes(_box.Paths.JournalFile, lines[..lastLineStart]);
        Assert.Equal(JournalEntryState.Pending, Assert.Single(_box.Journal.Read().Entries).State);

        var report = _box.Restorer().RestoreAll();

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void A_truncated_journal_tail_does_not_block_restoring_earlier_folders()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        File.AppendAllText(_box.Paths.JournalFile, "0123456789abcdef\t{\"k\":\"Cha");

        var report = _box.Restorer().RestoreAll();

        Assert.True(report.AllRestored);
        Assert.Equal(1, report.CorruptJournalRecords);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public void A_failed_restore_stays_active_and_can_be_retried()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var ops = new FaultyOperations { FailDelete = _ => true };

        var failed = _box.Restorer(ops).RestoreAll();

        Assert.Equal(RestoreOutcome.Failed, Assert.Single(failed.Results).Outcome);
        Assert.Single(_box.Journal.Read().Active);

        Assert.True(_box.Restorer().RestoreAll().AllRestored);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public void A_folder_replaced_by_a_junction_is_refused_and_its_target_is_untouched()
    {
        var folder = _box.NewFolder("swapped");
        var bystander = _box.NewFolder("bystander");
        var bytes = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(bystander, bytes);
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        File.SetAttributes(folder, FileAttributes.Normal);
        Directory.Delete(folder, recursive: true);
        Sandbox.CreateJunction(folder, bystander);

        var report = _box.Restorer().RestoreAll();

        Assert.Equal(RestoreOutcome.Refused, Assert.Single(report.Results).Outcome);
        Assert.Equal(bytes, File.ReadAllBytes(Sandbox.IniPath(bystander)));
    }

    [Fact]
    public void A_stale_temp_file_of_ours_is_swept_and_lookalikes_are_not()
    {
        var folder = _box.NewFolder();
        var ours = Path.Combine(folder, ".foldcraft-" + Guid.NewGuid().ToString("N") + ".tmp");
        var lookalike = Path.Combine(folder, ".foldcraft-my-notes.tmp");
        File.WriteAllText(lookalike, "user file");
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        File.WriteAllText(ours, "crash leftover");

        _box.Restorer().RestoreAll();

        Assert.False(File.Exists(ours));
        Assert.Equal("user file", File.ReadAllText(lookalike));
    }

    [Fact]
    public void Restoring_notifies_the_shell()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        _box.Shell.Notified.Clear();

        _box.Restorer().RestoreAll();

        Assert.Contains(folder, _box.Shell.Notified, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_shell_that_throws_cannot_fail_a_restore()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var restorer = new FolderRestorer(_box.Safety, _box.Journal, new ThrowingShell(), new FileOperations());

        Assert.True(restorer.RestoreAll().AllRestored);
    }

    private sealed class ThrowingShell : IShellNotifier
    {
        public void FolderChanged(string folderPath) => throw new InvalidOperationException("Explorer is unavailable.");
    }
}
