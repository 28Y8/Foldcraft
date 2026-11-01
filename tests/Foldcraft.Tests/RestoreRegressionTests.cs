using System.Diagnostics;
using System.Text;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Journal;

namespace Foldcraft.Tests;

/// <summary>
/// Things a restore must never do: lose someone else's edit, take attributes it didn't add,
/// act on a folder that isn't the one it changed or strand a folder with no way back.
/// </summary>
public sealed class RestoreRegressionTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private static void AddForeignSection(string folder)
    {
        var path = Sandbox.IniPath(folder);
        var attributes = File.GetAttributes(path);

        File.SetAttributes(path, FileAttributes.Normal);
        File.AppendAllText(path, "[UserSection]\r\nmine=precious\r\n", Encoding.Unicode);
        File.SetAttributes(path, attributes);
    }

    private static bool HasForeignSection(string folder) =>
        File.Exists(Sandbox.IniPath(folder)) &&
        File.ReadAllText(Sandbox.IniPath(folder), Encoding.Unicode).Contains("mine=precious", StringComparison.Ordinal);

    /// <summary>The edit sits between two of Foldcraft's changes, so the top of the stack still matches.</summary>
    private (string Folder, Guid OlderBatch) AppliedEditedAppliedAgain()
    {
        var folder = _box.NewFolder();
        var customizer = _box.Customizer();

        var older = customizer.Apply([folder], Sandbox.BlueStar);
        AddForeignSection(folder);
        customizer.Apply([folder], Sandbox.GreenHeart);

        Assert.True(HasForeignSection(folder));
        return (folder, older.BatchId);
    }

    private static void ForceDelete(string folder)
    {
        foreach (var file in Directory.GetFiles(folder))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        File.SetAttributes(folder, FileAttributes.Directory);
        Directory.Delete(folder);
    }

    private static void Attrib(string flag, string path)
    {
        var info = new ProcessStartInfo("attrib.exe", [flag, path, "/D"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(info)!;
        Assert.True(process.WaitForExit(15_000), "attrib timed out");
    }

    public static TheoryData<string, int> CloudBits() => new()
    {
        { "+P", 0x80000 },
        { "+U", 0x100000 },
        { "+O", 0x1000 },
    };

    // A foreign edit below the top of the stack

    [Fact]
    public void Restore_folder_reports_a_foreign_edit_below_the_newest_change()
    {
        var (folder, _) = AppliedEditedAppliedAgain();

        var report = _box.Restorer().RestoreFolder(folder);

        Assert.Equal(RestoreOutcome.Conflict, Assert.Single(report.Results).Outcome);
        Assert.True(HasForeignSection(folder));
        Assert.Equal(2, _box.Journal.Read().Active.Count());
    }

    [Fact]
    public void Undoing_the_older_batch_reports_a_foreign_edit_below_the_newest_change()
    {
        var (folder, olderBatch) = AppliedEditedAppliedAgain();

        var report = _box.Restorer().UndoBatch(olderBatch);

        Assert.Equal(RestoreOutcome.Conflict, Assert.Single(report.Results).Outcome);
        Assert.True(HasForeignSection(folder));
    }

    [Fact]
    public void Restore_all_reports_a_foreign_edit_below_the_newest_change()
    {
        var (folder, _) = AppliedEditedAppliedAgain();

        var report = _box.Restorer().RestoreAll();

        Assert.Single(report.Conflicts);
        Assert.True(HasForeignSection(folder));
    }

    [Fact]
    public void Undoing_only_the_newest_change_keeps_a_foreign_edit_below_it()
    {
        var (folder, _) = AppliedEditedAppliedAgain();

        var report = _box.Restorer().UndoLast();

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.True(HasForeignSection(folder));
    }

    [Fact]
    public void The_caller_can_still_decide_about_a_foreign_edit_below_the_newest_change()
    {
        var (folder, _) = AppliedEditedAppliedAgain();
        var restorer = _box.Restorer();

        Assert.Equal(RestoreOutcome.Forgotten, Assert.Single(restorer.RestoreFolder(folder, ConflictPolicy.Forget).Results).Outcome);
        Assert.True(HasForeignSection(folder));
        Assert.Empty(_box.Journal.Read().Active);

        var (other, _) = (_box.NewFolder("other"), 0);
        var customizer = _box.Customizer();
        customizer.Apply(other, Sandbox.BlueStar);
        AddForeignSection(other);
        customizer.Apply(other, Sandbox.GreenHeart);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(restorer.RestoreFolder(other, ConflictPolicy.Overwrite).Results).Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(other));
    }

    [Fact]
    public void An_interrupted_change_that_never_wrote_is_not_a_foreign_edit()
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

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var report = _box.Restorer().RestoreFolder(folder);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(folder)));
    }

    // Attributes that aren't Foldcraft's

    [Fact]
    public void A_folder_hidden_after_apply_stays_hidden_after_undo()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.Hidden);

        var report = _box.Restorer().UndoLast();

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.Equal(FileAttributes.Directory | FileAttributes.Hidden, File.GetAttributes(folder));
    }

    [Fact]
    public void A_read_only_mark_the_folder_already_had_is_left_alone()
    {
        var folder = _box.NewFolder();
        File.SetAttributes(folder, FileAttributes.ReadOnly);
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.True(_box.Restorer().UndoLast().AllRestored);
        Assert.True(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void Attributes_added_to_desktop_ini_after_apply_survive_undo()
    {
        var folder = _box.NewFolder();
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original, FileAttributes.Hidden);

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var path = Sandbox.IniPath(folder);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

        Assert.True(_box.Restorer().UndoLast().AllRestored);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(FileAttributes.Hidden | FileAttributes.ReadOnly, File.GetAttributes(path));
    }

    [Fact]
    public void A_different_folder_at_the_same_path_is_left_alone()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        ForceDelete(folder);

        Directory.CreateDirectory(folder);
        const FileAttributes marks = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly;
        File.SetAttributes(folder, marks);
        var attributes = File.GetAttributes(folder);

        var report = _box.Restorer().UndoLast();

        Assert.Equal(RestoreOutcome.DifferentFolder, Assert.Single(report.Results).Outcome);
        Assert.Equal(attributes, File.GetAttributes(folder));
        Assert.Single(_box.Journal.Read().Active);
    }

    private string AppliedThenReplacedByAnotherFolder()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        ForceDelete(folder);

        Directory.CreateDirectory(folder);
        File.SetAttributes(folder, FileAttributes.ReadOnly);
        Sandbox.PlantIni(folder, Encoding.UTF8.GetBytes("[Theirs]\nkeep=me\n"));

        return folder;
    }

    [Fact]
    public void Restoring_anyway_still_never_touches_a_different_folder()
    {
        var folder = AppliedThenReplacedByAnotherFolder();
        var before = File.ReadAllBytes(Sandbox.IniPath(folder));

        var report = _box.Restorer().RestoreFolder(folder, ConflictPolicy.Overwrite);

        Assert.Equal(RestoreOutcome.DifferentFolder, Assert.Single(report.Results).Outcome);
        Assert.Equal(before, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.True(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void A_different_folder_can_be_let_go()
    {
        var folder = AppliedThenReplacedByAnotherFolder();

        var report = _box.Restorer().RestoreFolder(folder, ConflictPolicy.Forget);

        Assert.Equal(RestoreOutcome.Forgotten, Assert.Single(report.Results).Outcome);
        Assert.True(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void The_same_look_restored_from_a_backup_counts_as_the_same_folder()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var written = File.ReadAllBytes(Sandbox.IniPath(folder));
        ForceDelete(folder);

        Directory.CreateDirectory(folder);
        File.SetAttributes(folder, FileAttributes.ReadOnly);
        Sandbox.PlantIni(folder, written);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(_box.Restorer().UndoLast().Results).Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
        Assert.False(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
    }

    // Renamed and moved folders

    [Fact]
    public void A_renamed_folder_can_be_found_and_undone()
    {
        var folder = _box.NewFolder("orig");
        var renamed = folder + "-renamed";
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Directory.Move(folder, renamed);
        var restorer = _box.Restorer();

        Assert.Single(restorer.UndoLast().Missing);

        var moved = restorer.Relocate(_box.Journal.Read().Entries[0].FolderPath, renamed);

        Assert.Equal(RestoreOutcome.Relocated, moved.Outcome);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(restorer.UndoLast().Results).Outcome);
        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(renamed)));
        Assert.False(File.GetAttributes(renamed).HasFlag(FileAttributes.ReadOnly));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void Relocating_changes_nothing_in_the_folder()
    {
        var folder = _box.NewFolder("orig");
        var renamed = folder + "-renamed";
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var written = File.ReadAllBytes(Sandbox.IniPath(folder));
        Directory.Move(folder, renamed);

        _box.Restorer().Relocate(_box.Journal.Read().Entries[0].FolderPath, renamed);

        Assert.Equal(written, File.ReadAllBytes(Sandbox.IniPath(renamed)));
        Assert.True(File.GetAttributes(renamed).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal(_box.Safety.Check(renamed).ResolvedPath, Assert.Single(_box.Journal.Read().Active).FolderPath);
    }

    [Fact]
    public void A_copy_with_the_same_look_is_accepted_as_a_folder_moved_between_drives()
    {
        var folder = _box.NewFolder("orig");
        var copy = _box.NewFolder("copy");
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var written = File.ReadAllBytes(Sandbox.IniPath(folder));
        var recorded = _box.Journal.Read().Entries[0].FolderPath;
        Sandbox.PlantIni(copy, written);
        File.SetAttributes(copy, FileAttributes.ReadOnly);
        ForceDelete(folder);

        Assert.Equal(RestoreOutcome.Relocated, _box.Restorer().Relocate(recorded, copy).Outcome);
        Assert.Equal(RestoreOutcome.Restored, Assert.Single(_box.Restorer().UndoLast().Results).Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(copy));
    }

    [Fact]
    public void An_unrelated_folder_is_not_accepted_as_the_new_location()
    {
        var folder = _box.NewFolder("orig");
        var unrelated = _box.NewFolder("unrelated");
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var recorded = _box.Journal.Read().Entries[0].FolderPath;
        ForceDelete(folder);

        var moved = _box.Restorer().Relocate(recorded, unrelated);

        Assert.Equal(RestoreOutcome.DifferentFolder, moved.Outcome);
        Assert.Equal(recorded, Assert.Single(_box.Journal.Read().Active).FolderPath);
        Assert.Empty(Directory.GetFileSystemEntries(unrelated));
    }

    [Fact]
    public void A_folder_still_in_place_cannot_hand_its_history_to_a_copy()
    {
        var folder = _box.NewFolder("orig");
        var copy = _box.NewFolder("copy");
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Sandbox.PlantIni(copy, File.ReadAllBytes(Sandbox.IniPath(folder)));
        var recorded = _box.Journal.Read().Entries[0].FolderPath;

        Assert.Equal(RestoreOutcome.DifferentFolder, _box.Restorer().Relocate(recorded, copy).Outcome);
        Assert.Equal(recorded, Assert.Single(_box.Journal.Read().Active).FolderPath);
    }

    [Fact]
    public void A_refused_location_is_not_accepted()
    {
        var folder = _box.NewFolder("orig");
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var recorded = _box.Journal.Read().Entries[0].FolderPath;
        ForceDelete(folder);

        var moved = _box.Restorer().Relocate(recorded, _box.FakeAppData);

        Assert.Equal(RestoreOutcome.Refused, moved.Outcome);
        Assert.NotNull(moved.Refusal);
    }

    [Fact]
    public void A_rename_then_a_new_look_joins_up_into_one_stack()
    {
        var folder = _box.NewFolder("orig");
        var renamed = folder + "-renamed";
        var customizer = _box.Customizer();
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original);

        customizer.Apply(folder, Sandbox.BlueStar);
        var recorded = _box.Journal.Read().Entries[0].FolderPath;
        Directory.Move(folder, renamed);
        customizer.Apply(renamed, Sandbox.GreenHeart);

        var restorer = _box.Restorer();

        Assert.Equal(RestoreOutcome.Relocated, restorer.Relocate(recorded, renamed).Outcome);
        Assert.Equal(RestoreOutcome.Restored, Assert.Single(restorer.RestoreFolder(renamed).Results).Outcome);

        Assert.Equal(original, File.ReadAllBytes(Sandbox.IniPath(renamed)));
        Assert.False(File.GetAttributes(renamed).HasFlag(FileAttributes.ReadOnly));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void A_moved_folder_keeps_its_place_in_a_journal_read_from_disk()
    {
        var folder = _box.NewFolder("orig");
        var renamed = folder + "-renamed";
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var recorded = _box.Journal.Read().Entries[0];
        Directory.Move(folder, renamed);

        _box.Restorer().Relocate(recorded.FolderPath, renamed);

        var entry = Assert.Single(new ChangeJournal(_box.Paths.JournalFile).Read().Entries);

        Assert.Equal(recorded.Id, entry.Id);
        Assert.Equal(JournalEntryState.Applied, entry.State);
        Assert.Equal(recorded.Change.WrittenSha256, entry.Change.WrittenSha256);
    }

    [Theory]
    [MemberData(nameof(CloudBits))]
    public void Cloud_and_offline_bits_on_a_folder_survive_apply_and_restore(string flag, int bit)
    {
        var folder = _box.NewFolder();
        var attribute = (FileAttributes)bit;
        Attrib(flag, folder);

        Assert.True(File.GetAttributes(folder).HasFlag(attribute), $"attrib {flag} did not take here.");

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Assert.True(File.GetAttributes(folder).HasFlag(attribute), "Apply cleared the bit.");

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(_box.Restorer().RestoreFolder(folder).Results).Outcome);
        Assert.Equal(FileAttributes.Directory | attribute, File.GetAttributes(folder));
    }

    [Theory]
    [MemberData(nameof(CloudBits))]
    public void Cloud_and_offline_bits_on_desktop_ini_survive_apply_and_restore(string flag, int bit)
    {
        var folder = _box.NewFolder();
        var path = Sandbox.IniPath(folder);
        var attribute = (FileAttributes)bit;
        var original = Encoding.UTF8.GetBytes("[A]\nb=1\n");
        Sandbox.PlantIni(folder, original, FileAttributes.Normal);
        Attrib(flag, path);

        Assert.True(File.GetAttributes(path).HasFlag(attribute), $"attrib {flag} did not take here.");

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Assert.True(File.GetAttributes(path).HasFlag(attribute), "Apply cleared the bit.");

        Assert.True(_box.Restorer().RestoreFolder(folder).AllRestored);
        Assert.True(File.GetAttributes(path).HasFlag(attribute), "Restore cleared the bit.");
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    // desktop.ini that can't be backed up

    private string AppliedThenGrownPastTheLimit()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var path = Sandbox.IniPath(folder);
        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllBytes(path, new byte[2 * 1024 * 1024]);

        return folder;
    }

    private string AppliedThenReplacedByADirectory(bool withContent)
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var path = Sandbox.IniPath(folder);
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
        Directory.CreateDirectory(path);

        if (withContent)
        {
            File.WriteAllText(Path.Combine(path, "keep.txt"), "keep");
        }

        return folder;
    }

    [Fact]
    public void An_oversized_desktop_ini_is_a_conflict_not_a_dead_end()
    {
        var folder = AppliedThenGrownPastTheLimit();

        Assert.Equal(RestoreOutcome.Conflict, Assert.Single(_box.Restorer().RestoreFolder(folder).Results).Outcome);
        Assert.Equal(2 * 1024 * 1024, new FileInfo(Sandbox.IniPath(folder)).Length);
    }

    [Fact]
    public void An_oversized_desktop_ini_can_be_kept()
    {
        var folder = AppliedThenGrownPastTheLimit();

        var report = _box.Restorer().RestoreFolder(folder, ConflictPolicy.Forget);

        Assert.Equal(RestoreOutcome.Forgotten, Assert.Single(report.Results).Outcome);
        Assert.Equal(2 * 1024 * 1024, new FileInfo(Sandbox.IniPath(folder)).Length);
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void An_oversized_desktop_ini_can_be_overwritten()
    {
        var folder = AppliedThenGrownPastTheLimit();

        var report = _box.Restorer().RestoreFolder(folder, ConflictPolicy.Overwrite);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
        Assert.Equal(FileAttributes.Directory, File.GetAttributes(folder));
    }

    [Fact]
    public void A_directory_in_place_of_desktop_ini_is_a_conflict_that_can_be_kept()
    {
        var folder = AppliedThenReplacedByADirectory(withContent: true);
        var restorer = _box.Restorer();

        Assert.Equal(RestoreOutcome.Conflict, Assert.Single(restorer.RestoreFolder(folder).Results).Outcome);
        Assert.Equal(RestoreOutcome.Forgotten, Assert.Single(restorer.RestoreFolder(folder, ConflictPolicy.Forget).Results).Outcome);
        Assert.True(File.Exists(Path.Combine(Sandbox.IniPath(folder), "keep.txt")));
    }

    [Fact]
    public void An_empty_directory_in_place_of_desktop_ini_can_be_overwritten()
    {
        var folder = AppliedThenReplacedByADirectory(withContent: false);

        var report = _box.Restorer().RestoreFolder(folder, ConflictPolicy.Overwrite);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public void A_directory_with_content_is_never_deleted_to_make_room()
    {
        var folder = AppliedThenReplacedByADirectory(withContent: true);

        var report = _box.Restorer().RestoreFolder(folder, ConflictPolicy.Overwrite);

        Assert.Equal(RestoreOutcome.Failed, Assert.Single(report.Results).Outcome);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(Sandbox.IniPath(folder), "keep.txt")));
        Assert.Single(_box.Journal.Read().Active);
    }

    [Fact]
    public void A_junction_in_place_of_desktop_ini_is_unlinked_and_its_target_is_untouched()
    {
        var folder = _box.NewFolder();
        var bystander = _box.NewFolder("bystander");
        File.WriteAllText(Path.Combine(bystander, "keep.txt"), "keep");
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var path = Sandbox.IniPath(folder);
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
        Sandbox.CreateJunction(path, bystander);

        var restorer = _box.Restorer();

        Assert.Equal(RestoreOutcome.Conflict, Assert.Single(restorer.RestoreFolder(folder).Results).Outcome);
        Assert.Equal(RestoreOutcome.Restored, Assert.Single(restorer.RestoreFolder(folder, ConflictPolicy.Overwrite).Results).Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(folder));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(bystander, "keep.txt")));
    }

    // Folders whose names differ only in ways a case-insensitive comparer can't see

    [Fact]
    public void Folders_that_differ_only_by_case_folding_keep_separate_undo_records()
    {
        var first = _box.NewFolder("\u10D0");
        var second = _box.NewFolder("\u1C90");
        var customizer = _box.Customizer();
        customizer.Apply(first, Sandbox.BlueStar);
        customizer.Apply(second, Sandbox.BlueStar);
        var restorer = _box.Restorer();

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(restorer.RestoreFolder(first).Results).Outcome);

        Assert.False(File.Exists(Sandbox.IniPath(first)));
        Assert.True(File.Exists(Sandbox.IniPath(second)));
        Assert.Single(_box.Journal.Read().Active);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(restorer.RestoreFolder(second).Results).Outcome);
        Assert.False(File.Exists(Sandbox.IniPath(second)));
    }

    [Fact]
    public void Restore_all_treats_case_folded_twins_as_two_folders()
    {
        var first = _box.NewFolder("\u10D0");
        var second = _box.NewFolder("\u1C90");
        var customizer = _box.Customizer();
        customizer.Apply(first, Sandbox.BlueStar);
        customizer.Apply(second, Sandbox.GreenHeart);

        var report = _box.Restorer().RestoreAll();

        Assert.Equal(2, report.Results.Count);
        Assert.True(report.AllRestored);
        Assert.False(File.Exists(Sandbox.IniPath(first)));
        Assert.False(File.Exists(Sandbox.IniPath(second)));
    }

    [Fact]
    public void A_batch_treats_case_folded_twins_as_two_folders()
    {
        var first = _box.NewFolder("\u10D0");
        var second = _box.NewFolder("\u1C90");

        var batch = _box.Customizer().Apply([first, second], Sandbox.BlueStar);

        Assert.Equal([ApplyOutcome.Applied, ApplyOutcome.Applied], batch.Results.Select(r => r.Outcome));

        var report = _box.Restorer().UndoBatch(batch.BatchId);

        Assert.Equal(2, report.Results.Count);
        Assert.False(File.Exists(Sandbox.IniPath(first)));
        Assert.False(File.Exists(Sandbox.IniPath(second)));
    }

    [Fact]
    public void Folders_in_a_case_sensitive_directory_keep_separate_undo_records()
    {
        var parent = _box.NewFolder("case-sensitive");

        var info = new ProcessStartInfo("fsutil.exe", ["file", "setCaseSensitiveInfo", parent, "enable"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using (var process = Process.Start(info)!)
        {
            Assert.True(process.WaitForExit(15_000), "fsutil timed out");

            if (process.ExitCode != 0)
            {
                // Needs the Windows Subsystem for Linux feature or similar. Nothing to check here.
                return;
            }
        }

        var lower = Path.Combine(parent, "foo");
        var upper = Path.Combine(parent, "FOO");
        Directory.CreateDirectory(lower);
        Directory.CreateDirectory(upper);

        var batch = _box.Customizer().Apply([lower, upper], Sandbox.BlueStar);

        Assert.Equal([ApplyOutcome.Applied, ApplyOutcome.Applied], batch.Results.Select(r => r.Outcome));

        Assert.True(_box.Restorer().RestoreFolder(lower).AllRestored);
        Assert.False(File.Exists(Sandbox.IniPath(lower)));
        Assert.True(File.Exists(Sandbox.IniPath(upper)));

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(_box.Restorer().RestoreFolder(upper).Results).Outcome);
        Assert.False(File.Exists(Sandbox.IniPath(upper)));
    }

    // Temp files a crash left behind

    private static string PlantStaleTemp(string folder)
    {
        var temp = Path.Combine(folder, ".foldcraft-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temp, "crash leftover");
        return temp;
    }

    [Fact]
    public void A_crash_leftover_is_swept_when_the_folder_already_matches_its_original()
    {
        var folder = _box.NewFolder();

        _box.Journal.RecordChange(new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = _box.Safety.Check(folder).ResolvedPath!,
            DesktopIniExisted = false,
            OriginalFolderAttributes = File.GetAttributes(folder),
            IconPath = "never-used.ico",
            WrittenSha256 = "never-written",
        });

        var temp = PlantStaleTemp(folder);

        var report = _box.Restorer().UndoLast();

        Assert.Equal(RestoreOutcome.AlreadyRestored, Assert.Single(report.Results).Outcome);
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void A_crash_leftover_is_swept_when_the_change_is_forgotten()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        AddForeignSection(folder);
        var temp = PlantStaleTemp(folder);

        var report = _box.Restorer().UndoLast(ConflictPolicy.Forget);

        Assert.Equal(RestoreOutcome.Forgotten, Assert.Single(report.Results).Outcome);
        Assert.False(File.Exists(temp));
    }
}
