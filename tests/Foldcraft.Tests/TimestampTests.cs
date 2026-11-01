using System.Text;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Journal;

namespace Foldcraft.Tests;

/// <summary>
/// A look moves the folder's date on by one tiny step, too small for a date column to show, which is
/// what makes the shell stop using its saved picture, and undo puts the exact date back, which brings
/// that picture back. A plain folder must never be left on a date it had with a look on.
/// </summary>
public sealed class TimestampTests : IDisposable
{
    private static readonly DateTime Earlier = new(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2022, 8, 9, 10, 11, 12, DateTimeKind.Utc);
    private static readonly TimeSpan Step = TimeSpan.FromTicks(1);

    private static readonly byte[] ForeignIni = Encoding.ASCII.GetBytes("[.ShellClassInfo]\r\nInfoTip=Mine\r\n");

    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private string OldFolder(string name = "target")
    {
        var folder = _box.NewFolder(name);
        Directory.SetLastWriteTimeUtc(folder, Earlier);
        return folder;
    }

    [Fact]
    public void Applying_moves_the_folders_date_on_by_one_step()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.Equal(Earlier + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Changing_the_look_again_moves_the_folders_date_on_by_one_more_step()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        _box.Customizer().Apply(folder, Sandbox.GreenHeart);

        Assert.Equal(Earlier + Step + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_look_a_folder_already_has_leaves_its_date_alone()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var date = Directory.GetLastWriteTimeUtc(folder);

        Assert.Equal(ApplyOutcome.Unchanged, _box.Customizer().Apply(folder, Sandbox.BlueStar).Outcome);
        Assert.Equal(date, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Undoing_leaves_the_folders_date_as_it_was()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        _box.Restorer().UndoLast();

        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Undoing_several_changes_gives_the_exact_first_date_back()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        _box.Customizer().Apply(folder, Sandbox.GreenHeart);
        _box.Restorer().RestoreFolder(folder);

        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Undoing_only_the_newest_change_leaves_the_date_the_older_look_had()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        _box.Customizer().Apply(folder, Sandbox.GreenHeart);
        _box.Restorer().UndoLast();

        Assert.Equal(Earlier + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Restoring_everything_gives_the_exact_date_back()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        _box.Restorer().RestoreAll();

        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    /// <summary>
    /// The date stays, but a step on. The shell may have filed that exact date as having no picture
    /// while the folder had its look, and it would then show a plain folder for good.
    /// </summary>
    [Fact]
    public void Undoing_keeps_a_date_that_moved_for_a_real_reason()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Directory.SetLastWriteTimeUtc(folder, Later);
        _box.Restorer().UndoLast();

        Assert.Equal(Later + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Undoing_keeps_the_date_a_new_file_gave_the_folder()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        File.WriteAllText(Path.Combine(folder, "new.txt"), "new");
        var moved = Directory.GetLastWriteTimeUtc(folder);

        _box.Restorer().UndoLast();

        var left = Directory.GetLastWriteTimeUtc(folder);

        Assert.True(moved > Earlier);
        Assert.Equal(moved + Step, left);
    }

    [Fact]
    public void A_date_moved_between_two_changes_is_the_one_undo_keeps()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Directory.SetLastWriteTimeUtc(folder, Later);
        _box.Customizer().Apply(folder, Sandbox.GreenHeart);
        _box.Restorer().RestoreFolder(folder);

        // One step on is the date the second look had, so the shell may have saved "no picture" there.
        Assert.Equal(Later + Step + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Undoing_one_change_at_a_time_steps_over_a_date_a_look_already_undone_had()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Directory.SetLastWriteTimeUtc(folder, Later);
        _box.Customizer().Apply(folder, Sandbox.GreenHeart);

        _box.Restorer().UndoLast();
        Assert.Equal(Later, Directory.GetLastWriteTimeUtc(folder));

        _box.Restorer().UndoLast();
        Assert.Equal(Later + Step + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_date_put_back_by_a_program_is_not_moved_onto_the_date_the_look_had()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Directory.SetLastWriteTimeUtc(folder, Earlier);
        _box.Restorer().UndoLast();

        Assert.Equal(Earlier + Step + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Moving_on_steps_over_every_date_in_a_row_that_a_look_had()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        _box.Customizer().Apply(folder, Sandbox.GreenHeart);
        Directory.SetLastWriteTimeUtc(folder, Earlier);
        _box.Restorer().RestoreFolder(folder);

        Assert.Equal(Earlier + Step + Step + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Another_folders_dates_are_not_stepped_over()
    {
        var other = OldFolder("other");
        var folder = OldFolder();

        _box.Customizer().Apply(other, Sandbox.BlueStar);
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Directory.SetLastWriteTimeUtc(folder, Earlier + Step + Step);
        Directory.SetLastWriteTimeUtc(other, Earlier + Step + Step + Step);
        _box.Restorer().RestoreFolder(folder);

        Assert.Equal(Earlier + Step + Step + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_folder_that_was_renamed_keeps_clear_of_the_dates_it_had_under_its_old_name()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        _box.Restorer().UndoLast();

        var moved = folder + "-renamed";

        Directory.Move(folder, moved);
        Directory.SetLastWriteTimeUtc(moved, Later);
        _box.Customizer().Apply(moved, Sandbox.GreenHeart);
        Directory.SetLastWriteTimeUtc(moved, Earlier);
        _box.Restorer().UndoLast();

        Assert.Equal(Earlier + Step + Step, Directory.GetLastWriteTimeUtc(moved));
    }

    [Theory]
    [InlineData(100_000L)]
    [InlineData(20_000_000L)]
    public void A_coarse_file_system_also_steps_over_a_date_a_look_had(long ticksKept)
    {
        var folder = OldFolder();
        var ops = new RoundingOperations(ticksKept);

        Directory.SetLastWriteTimeUtc(folder, Later);
        _box.Customizer(ops).Apply(folder, Sandbox.BlueStar);
        Directory.SetLastWriteTimeUtc(folder, Later);
        _box.Restorer(ops).UndoLast();

        Assert.Equal(Later.AddTicks(ticksKept * 2), Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_retry_after_undo_failed_partway_leaves_the_exact_date()
    {
        var folder = OldFolder();
        var failures = 1;
        var ops = new FaultyOperations
        {
            FailSetAttributes = (path, _) => path.Equals(folder, StringComparison.OrdinalIgnoreCase) && failures-- > 0,
        };

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.Equal(RestoreOutcome.Failed, Assert.Single(_box.Restorer(ops).UndoLast().Results).Outcome);
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.NotEqual(Earlier + Step, Directory.GetLastWriteTimeUtc(folder));

        Assert.True(_box.Restorer(ops).UndoLast().AllRestored);
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    /// <summary>What a kill leaves after the marks came off too: the look is gone and the date is today.</summary>
    [Fact]
    public void A_retry_with_only_the_date_left_to_do_leaves_the_exact_date()
    {
        var folder = OldFolder();
        var ops = new FaultyOperations { FailSetTime = _ => true };

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var change = Assert.Single(_box.Journal.Read().Entries);

        _box.Journal.MarkUndoing(change.Id, new UndoPlan(Earlier, MoveOn: false));
        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        File.Delete(Sandbox.IniPath(folder));
        File.SetAttributes(folder, FileAttributes.Directory);

        Assert.NotEqual(Earlier + Step, Directory.GetLastWriteTimeUtc(folder));
        Assert.True(_box.Restorer().UndoLast().AllRestored);
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    /// <summary>What a kill leaves after the original settings file went back, before its date did.</summary>
    [Fact]
    public void A_retry_also_finishes_the_date_of_a_desktop_ini_that_was_there_before()
    {
        var folder = OldFolder();

        Sandbox.PlantIni(folder, ForeignIni);
        File.SetLastWriteTimeUtc(Sandbox.IniPath(folder), Later);
        File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
        Directory.SetLastWriteTimeUtc(folder, Earlier);

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        _box.Journal.MarkUndoing(Assert.Single(_box.Journal.Read().Entries).Id, new UndoPlan(Earlier, MoveOn: false));
        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        File.WriteAllBytes(Sandbox.IniPath(folder), ForeignIni);
        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Hidden | FileAttributes.System);

        Assert.True(_box.Restorer().UndoLast().AllRestored);
        Assert.Equal(Later, File.GetLastWriteTimeUtc(Sandbox.IniPath(folder)));
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    /// <summary>What a kill leaves after the temp file was written, before it was swapped in.</summary>
    [Fact]
    public void A_temp_file_left_behind_shows_the_undo_had_started()
    {
        var folder = OldFolder();

        Sandbox.PlantIni(folder, ForeignIni);
        Directory.SetLastWriteTimeUtc(folder, Earlier);

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        _box.Journal.MarkUndoing(Assert.Single(_box.Journal.Read().Entries).Id, new UndoPlan(Earlier, MoveOn: false));
        File.WriteAllBytes(Path.Combine(folder, $".foldcraft-{Guid.NewGuid():N}.tmp"), ForeignIni);

        Assert.True(_box.Restorer().UndoLast().AllRestored);
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_retry_keeps_the_moved_on_date_the_first_try_chose()
    {
        var folder = OldFolder();
        var failures = 1;
        var ops = new FaultyOperations
        {
            FailSetAttributes = (path, _) => path.Equals(folder, StringComparison.OrdinalIgnoreCase) && failures-- > 0,
        };

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Directory.SetLastWriteTimeUtc(folder, Later);

        Assert.Equal(RestoreOutcome.Failed, Assert.Single(_box.Restorer(ops).UndoLast().Results).Outcome);
        Assert.True(_box.Restorer(ops).UndoLast().AllRestored);
        Assert.Equal(Later + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    /// <summary>Nothing happened to the folder, so a file added since is a real change as usual.</summary>
    [Fact]
    public void An_undo_that_never_got_started_does_not_tie_a_later_one_to_its_date()
    {
        var folder = OldFolder();
        var ops = new FaultyOperations { FailDelete = path => path == Sandbox.IniPath(folder) };

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.Equal(RestoreOutcome.Failed, Assert.Single(_box.Restorer(ops).UndoLast().Results).Outcome);

        Directory.SetLastWriteTimeUtc(folder, Later);
        Assert.True(_box.Restorer().UndoLast().AllRestored);

        Assert.Equal(Later + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_plan_for_a_narrower_undo_is_not_used_for_a_wider_one()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var blueLook = File.ReadAllBytes(Sandbox.IniPath(folder));

        _box.Customizer().Apply(folder, Sandbox.GreenHeart);

        // What a kill leaves after undoing the newest look put the older one back, before the date.
        var newest = _box.Journal.Read().Active.Last();

        _box.Journal.MarkUndoing(newest.Id, new UndoPlan(Earlier + Step, MoveOn: false));
        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        File.WriteAllBytes(Sandbox.IniPath(folder), blueLook);

        Assert.True(_box.Restorer().RestoreFolder(folder, ConflictPolicy.Overwrite).AllRestored);

        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void The_date_to_leave_is_recorded_before_the_folder_is_touched()
    {
        var folder = OldFolder();
        UndoPlan? seen = null;
        var ops = new WatchingOperations(() => seen = Assert.Single(_box.Journal.Read().Entries).UndoPlan);

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        _box.Restorer(ops).UndoLast();

        Assert.Equal(new UndoPlan(Earlier, MoveOn: false), seen);
    }

    /// <summary>An interrupted second look took nothing away from what happened under the first.</summary>
    [Fact]
    public void An_interrupted_second_look_keeps_a_date_moved_before_it()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Directory.SetLastWriteTimeUtc(folder, Later);

        var newest = new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = folder,
            DesktopIniExisted = true,
            OriginalDesktopIni = File.ReadAllBytes(Sandbox.IniPath(folder)),
            OriginalDesktopIniAttributes = File.GetAttributes(Sandbox.IniPath(folder)),
            OriginalFolderAttributes = File.GetAttributes(folder),
            IconPath = @"C:
owherea.ico",
            WrittenSha256 = "not written yet",
            OriginalFolderLastWriteUtc = Later,
        };

        _box.Journal.RecordChange(newest);

        Assert.True(_box.Restorer().RestoreFolder(folder).AllRestored);
        Assert.Equal(Later + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_date_moved_on_a_second_time_is_not_mistaken_for_ours()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Directory.SetLastWriteTimeUtc(folder, Earlier + Step + Step);
        _box.Restorer().UndoLast();

        Assert.Equal(Earlier + Step + Step + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    /// <summary>What a crash leaves when undo finished but couldn't say so. The retry mustn't move the date.</summary>
    [Fact]
    public void Finishing_an_undo_that_was_cut_short_leaves_the_exact_date()
    {
        var folder = OldFolder();

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        File.Delete(Sandbox.IniPath(folder));
        File.SetAttributes(folder, FileAttributes.Directory);
        Directory.SetLastWriteTimeUtc(folder, Earlier);

        Assert.Equal(RestoreOutcome.AlreadyRestored, Assert.Single(_box.Restorer().UndoLast().Results).Outcome);
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Redrawing_an_icon_between_two_steps_still_gives_the_exact_date_back()
    {
        var folder = OldFolder();
        var customizer = _box.Customizer();

        customizer.Apply(folder, Sandbox.BlueStar);

        var icon = Assert.Single(_box.Journal.Read().Entries).Change.IconPath;
        File.Delete(icon);

        new FolderRepairer(_box.Safety, _box.Journal, _box.Icons, customizer, _box.Shell).RepairAll();

        Assert.Equal(Earlier + Step, Directory.GetLastWriteTimeUtc(folder));

        _box.Restorer().RestoreFolder(folder);

        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_folder_Foldcraft_made_that_stayed_keeps_a_moved_on_date()
    {
        var folder = Path.Combine(_box.NewFolder("apps"), "New app");
        _box.Customizer().CreateFolder(folder, [], Sandbox.BlueStar, Guid.NewGuid());

        File.WriteAllText(Path.Combine(folder, "mine.txt"), "mine");
        var moved = Directory.GetLastWriteTimeUtc(folder);

        Assert.True(_box.Restorer().UndoLast().AllRestored);
        Assert.Equal(moved + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Theory]
    [InlineData(100_000L)]
    [InlineData(20_000_000L)]
    public void A_coarse_file_system_gets_the_smallest_step_it_can_keep(long ticksKept)
    {
        var folder = OldFolder();
        var ops = new RoundingOperations(ticksKept);

        // Such a file system could never have stored an odd second to start with.
        Directory.SetLastWriteTimeUtc(folder, Later);

        Assert.Equal(ApplyOutcome.Applied, _box.Customizer(ops).Apply(folder, Sandbox.BlueStar).Outcome);
        Assert.Equal(Later.AddTicks(ticksKept), Directory.GetLastWriteTimeUtc(folder));

        Assert.True(_box.Restorer(ops).UndoLast().AllRestored);
        Assert.Equal(Later, Directory.GetLastWriteTimeUtc(folder));
    }

    [Theory]
    [InlineData(100_000L)]
    [InlineData(20_000_000L)]
    public void A_coarse_file_system_still_moves_on_a_date_that_moved_for_a_real_reason(long ticksKept)
    {
        var folder = OldFolder();
        var ops = new RoundingOperations(ticksKept);

        _box.Customizer(ops).Apply(folder, Sandbox.BlueStar);
        Directory.SetLastWriteTimeUtc(folder, Later);
        _box.Restorer(ops).UndoLast();

        Assert.Equal(Later.AddTicks(ticksKept), Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_date_that_could_not_be_moved_on_is_recorded_as_it_stands()
    {
        var folder = OldFolder();
        var failures = 1;
        var ops = new FaultyOperations { FailSetTime = _ => failures-- > 0 };

        Assert.Equal(ApplyOutcome.Applied, _box.Customizer(ops).Apply(folder, Sandbox.BlueStar).Outcome);

        var change = Assert.Single(_box.Journal.Read().Entries).Change;

        Assert.Equal(Directory.GetLastWriteTimeUtc(folder), change.AppliedFolderLastWriteUtc);

        _box.Restorer(ops).UndoLast();

        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Undoing_restores_the_date_of_a_desktop_ini_that_was_there_before()
    {
        var folder = OldFolder();
        Sandbox.PlantIni(folder, ForeignIni);
        File.SetLastWriteTimeUtc(Sandbox.IniPath(folder), Later);
        Directory.SetLastWriteTimeUtc(folder, Earlier);

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        Assert.NotEqual(Later, File.GetLastWriteTimeUtc(Sandbox.IniPath(folder)));

        _box.Restorer().UndoLast();

        Assert.Equal(Later, File.GetLastWriteTimeUtc(Sandbox.IniPath(folder)));
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Undoing_several_changes_restores_the_first_originals_date()
    {
        var folder = OldFolder();
        Sandbox.PlantIni(folder, ForeignIni);
        File.SetLastWriteTimeUtc(Sandbox.IniPath(folder), Later);

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        _box.Customizer().Apply(folder, Sandbox.GreenHeart);
        _box.Restorer().RestoreFolder(folder);

        Assert.Equal(Later, File.GetLastWriteTimeUtc(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void A_failed_apply_leaves_the_folders_date_as_it_was()
    {
        var folder = OldFolder();
        var ops = new FaultyOperations { FailCommit = path => path == Sandbox.IniPath(folder) };

        var result = _box.Customizer(ops).Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void An_apply_rolled_back_after_writing_leaves_the_folders_date_as_it_was()
    {
        var folder = OldFolder();
        var failures = 1;
        var ops = new FaultyOperations
        {
            FailSetAttributes = (path, _) => path.Equals(folder, StringComparison.OrdinalIgnoreCase) && failures-- > 0,
        };

        var result = _box.Customizer(ops).Apply(folder, Sandbox.BlueStar);

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_date_that_cannot_be_kept_does_not_fail_the_change()
    {
        var folder = OldFolder();
        var ops = new FaultyOperations { FailSetTime = _ => true };

        Assert.Equal(ApplyOutcome.Applied, _box.Customizer(ops).Apply(folder, Sandbox.BlueStar).Outcome);
        Assert.True(_box.Restorer(ops).UndoLast().AllRestored);
    }

    [Fact]
    public void An_interrupted_change_gets_its_recorded_date_back()
    {
        var folder = OldFolder();
        var path = Sandbox.IniPath(folder);

        // What a crash leaves: recorded, written and no outcome recorded.
        _box.Journal.RecordChange(new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = folder,
            DesktopIniExisted = false,
            OriginalFolderAttributes = File.GetAttributes(folder),
            IconPath = @"C:\nowhere\a.ico",
            WrittenSha256 = FolderState.Sha256Hex(ForeignIni),
            OriginalFolderLastWriteUtc = Earlier,
        });

        File.WriteAllBytes(path, ForeignIni);
        Assert.NotEqual(Earlier, Directory.GetLastWriteTimeUtc(folder));

        Assert.True(_box.Restorer().UndoLast().AllRestored);
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_record_from_an_older_version_still_undoes()
    {
        var folder = OldFolder();
        File.WriteAllBytes(Sandbox.IniPath(folder), ForeignIni);
        Directory.SetLastWriteTimeUtc(folder, Later);

        var id = _box.Journal.RecordChange(new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = folder,
            DesktopIniExisted = false,
            OriginalFolderAttributes = File.GetAttributes(folder),
            IconPath = @"C:\nowhere\a.ico",
            WrittenSha256 = FolderState.Sha256Hex(ForeignIni),
        });

        _box.Journal.MarkApplied(id);

        var entry = Assert.Single(_box.Journal.Read().Entries);
        Assert.Null(entry.Change.OriginalFolderLastWriteUtc);
        Assert.Null(entry.Change.SpecJson);

        Assert.True(_box.Restorer().UndoLast().AllRestored);
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.Equal(Later, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Dates_survive_a_trip_through_the_journal()
    {
        var folder = OldFolder();
        Sandbox.PlantIni(folder, ForeignIni);
        File.SetLastWriteTimeUtc(Sandbox.IniPath(folder), Later);
        Directory.SetLastWriteTimeUtc(folder, Earlier);

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var change = Assert.Single(_box.Journal.Read().Entries).Change;

        Assert.Equal(Earlier, change.OriginalFolderLastWriteUtc);
        Assert.Equal(Earlier + Step, change.AppliedFolderLastWriteUtc);
        Assert.Equal(Later, change.OriginalDesktopIniLastWriteUtc);
        Assert.Equal(DateTimeKind.Utc, change.OriginalFolderLastWriteUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, change.AppliedFolderLastWriteUtc!.Value.Kind);
    }

    [Fact]
    public void A_change_with_no_recorded_outcome_has_no_finished_date()
    {
        var id = _box.Journal.RecordChange(new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = _box.NewFolder(),
            DesktopIniExisted = false,
            OriginalFolderAttributes = FileAttributes.Directory,
            IconPath = @"C:\nowhere\a.ico",
            WrittenSha256 = "x",
        });

        Assert.Null(Assert.Single(_box.Journal.Read().Entries).Change.AppliedFolderLastWriteUtc);

        _box.Journal.MarkApplied(id, Later);
        _box.Journal.MarkUndone(id);

        var entry = Assert.Single(_box.Journal.Read().Entries);

        Assert.Equal(JournalEntryState.Undone, entry.State);
        Assert.Equal(Later, entry.Change.AppliedFolderLastWriteUtc);
    }

    /// <summary>Calls back at the first change to the folder's contents.</summary>
    private sealed class WatchingOperations(Action first) : FileOperations
    {
        private Action? _first = first;

        public override void DeleteFile(string path)
        {
            _first?.Invoke();
            _first = null;
            base.DeleteFile(path);
        }
    }

    /// <summary>Keeps dates only to a set precision, as exFAT and FAT do, without saying so.</summary>
    private sealed class RoundingOperations(long ticksKept) : FileOperations
    {
        public override void SetLastWriteTimeUtc(string path, DateTime lastWriteUtc) =>
            base.SetLastWriteTimeUtc(path, new DateTime(lastWriteUtc.Ticks - (lastWriteUtc.Ticks % ticksKept), DateTimeKind.Utc));
    }
}
