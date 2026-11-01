using System.Text;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Journal;

namespace Foldcraft.Tests;

/// <summary>
/// A process that drew a folder with a look can leave a saved failure under the date undo left, and
/// the folder then stays plain. These prove the second look finds that, cures it with a date step and
/// leaves every other folder alone.
/// </summary>
public sealed class PreviewWatchTests : IDisposable
{
    private static readonly DateTime Earlier = new(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2022, 8, 9, 10, 11, 12, DateTimeKind.Utc);
    private static readonly TimeSpan Step = TimeSpan.FromTicks(1);

    private readonly Sandbox _box = new();
    private readonly Clock _clock = new();
    private readonly ChangeJournal _journal;
    private readonly FakePreviews _previews = new();

    public PreviewWatchTests() => _journal = new ChangeJournal(_box.Paths.JournalFile, _clock);

    public void Dispose() => _box.Dispose();

    private FolderCustomizer Customizer() => new(_box.Safety, _journal, _box.Icons, _box.Shell);

    private FolderRestorer Restorer() => new(_box.Safety, _journal, _box.Shell);

    private PreviewWatch Watch() => new(_box.Safety, _journal, _box.Shell, _previews, time: _clock);

    /// <summary>A folder with something in it, so the shell would have something to draw.</summary>
    private string OldFolder(string name = "target")
    {
        var folder = _box.NewFolder(name);
        File.WriteAllText(Path.Combine(folder, "photo.jpg"), "picture");
        Directory.SetLastWriteTimeUtc(folder, Earlier);
        return folder;
    }

    private string UndoneFolder()
    {
        var folder = OldFolder();

        Customizer().Apply(folder, Sandbox.BlueStar);
        Restorer().UndoLast();
        _box.Shell.Notified.Clear();

        return folder;
    }

    private IReadOnlyList<PreviewCheck> Checks() => _journal.Read().PreviewChecks;

    [Fact]
    public void An_undo_queues_a_check_of_the_folder()
    {
        var folder = UndoneFolder();

        Assert.Equal(folder, Assert.Single(Assert.Single(Checks()).Folders));
    }

    [Fact]
    public void An_undo_of_a_whole_batch_queues_one_check()
    {
        var folders = new[] { OldFolder("one"), OldFolder("two"), OldFolder("three") };

        Customizer().Apply(folders, Sandbox.BlueStar);
        Restorer().UndoLast();

        Assert.Equal(3, Assert.Single(Checks()).Folders.Count);
    }

    [Fact]
    public void Restoring_a_folder_queues_a_check()
    {
        var folder = OldFolder();

        Customizer().Apply(folder, Sandbox.BlueStar);
        Restorer().RestoreFolder(folder);

        Assert.Single(Checks());
    }

    [Fact]
    public void Restoring_everything_queues_a_check()
    {
        var folder = OldFolder();

        Customizer().Apply(folder, Sandbox.BlueStar);
        Restorer().RestoreAll();

        Assert.Single(Checks());
    }

    [Fact]
    public void A_folder_that_could_not_be_restored_is_not_checked()
    {
        var folder = OldFolder();

        Customizer().Apply(folder, Sandbox.BlueStar);
        File.AppendAllText(Sandbox.IniPath(folder), "Edited=yes\r\n");

        Assert.Equal(RestoreOutcome.Conflict, Assert.Single(Restorer().UndoLast().Results).Outcome);
        Assert.Empty(Checks());
    }

    [Fact]
    public void A_folder_undo_took_away_is_not_checked()
    {
        var folder = Path.Combine(_box.NewFolder("apps"), "New app");

        Customizer().CreateFolder(folder, [], Sandbox.BlueStar, Guid.NewGuid());
        Restorer().UndoLast();

        Assert.False(Directory.Exists(folder));
        Assert.Empty(Checks());
    }

    [Fact]
    public void Having_nothing_to_undo_queues_nothing()
    {
        Restorer().RestoreFolder(OldFolder());

        Assert.Empty(Checks());
    }

    [Fact]
    public void Nothing_happens_before_the_shell_has_forgotten_the_look()
    {
        var folder = UndoneFolder();

        _previews.Failing.Add(folder);
        _clock.Advance(PreviewWatch.Wait - TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(1), Watch().RunDue());
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
        Assert.Empty(_previews.Asked);
        Assert.Single(Checks());
    }

    [Fact]
    public void The_wait_is_longer_than_the_longest_time_measured()
    {
        Assert.True(PreviewWatch.Wait > TimeSpan.FromSeconds(60));
    }

    /// <summary>The date right after is the one the look used, which may hold the failure too.</summary>
    [Fact]
    public void A_saved_failure_gets_a_date_no_look_used_and_a_notice()
    {
        var folder = UndoneFolder();

        _previews.Failing.Add(folder);
        _clock.Advance(PreviewWatch.Wait);
        Watch().RunDue();

        Assert.Equal(Earlier + Step + Step, Directory.GetLastWriteTimeUtc(folder));
        Assert.Equal([folder], _box.Shell.Notified);
    }

    [Fact]
    public void A_folder_with_a_saved_picture_keeps_its_exact_date()
    {
        var folder = UndoneFolder();

        _clock.Advance(PreviewWatch.Wait);

        Assert.Null(Watch().RunDue());
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
        Assert.Empty(_box.Shell.Notified);
        Assert.Empty(Checks());
    }

    [Fact]
    public void A_date_step_is_checked_again_because_it_can_fail_the_same_way()
    {
        var folder = UndoneFolder();

        _previews.Failing.Add(folder);
        _clock.Advance(PreviewWatch.Wait);

        Assert.Equal(PreviewWatch.Wait, Watch().RunDue());

        _previews.Failing.Clear();
        _clock.Advance(PreviewWatch.Wait);

        Assert.Null(Watch().RunDue());
        Assert.Equal(Earlier + Step + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_failure_that_never_clears_stops_being_chased()
    {
        var folder = UndoneFolder();

        _previews.Failing.Add(folder);

        for (var i = 0; i < 10; i++)
        {
            _clock.Advance(PreviewWatch.Wait);
            Watch().RunDue();
        }

        Assert.Equal(Earlier + (Step * 4), Directory.GetLastWriteTimeUtc(folder));
        Assert.Empty(Checks());
    }

    [Fact]
    public void Only_the_folder_with_a_failure_is_touched()
    {
        var hurt = OldFolder("hurt");
        var fine = OldFolder("fine");

        Customizer().Apply([hurt, fine], Sandbox.BlueStar);
        Restorer().UndoLast();
        _previews.Failing.Add(hurt);
        _clock.Advance(PreviewWatch.Wait);
        Watch().RunDue();

        Assert.Equal(Earlier + Step + Step, Directory.GetLastWriteTimeUtc(hurt));
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(fine));
    }

    [Fact]
    public void A_folder_given_a_look_again_shows_that_and_is_left_alone()
    {
        var folder = UndoneFolder();

        Customizer().Apply(folder, Sandbox.GreenHeart);
        var date = Directory.GetLastWriteTimeUtc(folder);

        _previews.Failing.Add(folder);
        _clock.Advance(PreviewWatch.Wait);
        Watch().RunDue();

        Assert.Equal(date, Directory.GetLastWriteTimeUtc(folder));
        Assert.Empty(Checks());
    }

    [Fact]
    public void A_folder_of_only_hidden_items_has_nothing_to_draw_so_its_failure_is_correct()
    {
        var folder = _box.NewFolder("hidden only");
        var hidden = Path.Combine(folder, ".keep");
        File.WriteAllText(hidden, "hidden");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        Directory.SetLastWriteTimeUtc(folder, Earlier);

        Customizer().Apply(folder, Sandbox.BlueStar);
        Restorer().UndoLast();
        _previews.Failing.Add(folder);
        _clock.Advance(PreviewWatch.Wait);
        Watch().RunDue();

        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void An_empty_folder_has_nothing_to_draw_so_its_failure_is_correct()
    {
        var folder = _box.NewFolder("empty");
        Directory.SetLastWriteTimeUtc(folder, Earlier);

        Customizer().Apply(folder, Sandbox.BlueStar);
        Restorer().UndoLast();
        _previews.Failing.Add(folder);
        _clock.Advance(PreviewWatch.Wait);
        Watch().RunDue();

        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_settings_file_alone_does_not_count_as_something_to_draw()
    {
        var folder = _box.NewFolder("only-settings");
        Sandbox.PlantIni(folder, Encoding.ASCII.GetBytes("[.ShellClassInfo]\r\nInfoTip=Mine\r\n"));
        Directory.SetLastWriteTimeUtc(folder, Earlier);

        Customizer().Apply(folder, Sandbox.BlueStar);
        Restorer().UndoLast();
        _previews.Failing.Add(folder);
        _clock.Advance(PreviewWatch.Wait);
        Watch().RunDue();

        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Theory]
    [InlineData("IconResource=C:\\Mine\\own.ico,0")]
    [InlineData("IconFile=C:\\Mine\\own.ico")]
    public void A_folder_with_an_icon_of_its_own_keeps_its_exact_date(string line)
    {
        var folder = OldFolder();

        Sandbox.PlantIni(folder, Encoding.ASCII.GetBytes($"[.ShellClassInfo]\r\n{line}\r\n"));
        File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
        Directory.SetLastWriteTimeUtc(folder, Earlier);

        Customizer().Apply(folder, Sandbox.BlueStar);
        Restorer().UndoLast();
        _previews.Failing.Add(folder);
        _clock.Advance(PreviewWatch.Wait);
        Watch().RunDue();

        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_folder_that_has_gone_closes_its_check()
    {
        var folder = UndoneFolder();

        Directory.Delete(folder, recursive: true);
        _clock.Advance(PreviewWatch.Wait);

        Assert.Null(Watch().RunDue());
        Assert.Empty(Checks());
    }

    [Fact]
    public void A_folder_that_became_unsafe_is_left_alone()
    {
        var folder = UndoneFolder();
        var elsewhere = OldFolder("elsewhere");

        Directory.Delete(folder, recursive: true);
        Sandbox.CreateJunction(folder, elsewhere);
        _previews.Failing.Add(folder);
        _previews.Failing.Add(elsewhere);
        _clock.Advance(PreviewWatch.Wait);
        Watch().RunDue();

        Assert.Empty(_previews.Asked);
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(elsewhere));
    }

    [Fact]
    public void A_date_the_folder_moved_to_since_is_moved_on_from()
    {
        var folder = UndoneFolder();

        Directory.SetLastWriteTimeUtc(folder, Later);
        _previews.Failing.Add(folder);
        _clock.Advance(PreviewWatch.Wait);
        Watch().RunDue();

        Assert.Equal(Later + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_shell_with_no_answer_changes_nothing()
    {
        var folder = UndoneFolder();
        var watch = new PreviewWatch(_box.Safety, _journal, _box.Shell, new ThrowingPreviews(), time: _clock);

        _clock.Advance(PreviewWatch.Wait);

        Assert.Null(watch.RunDue());
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_later_start_finishes_what_a_closed_app_left()
    {
        var folder = UndoneFolder();

        _previews.Failing.Add(folder);
        _clock.Advance(TimeSpan.FromDays(3));

        var reopened = new ChangeJournal(_box.Paths.JournalFile, _clock);

        Assert.Single(reopened.Read().PreviewChecks);

        new PreviewWatch(_box.Safety, reopened, _box.Shell, _previews, time: _clock).RunDue();

        Assert.Equal(Earlier + Step + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public async Task Starting_runs_overdue_work_on_its_own()
    {
        var folder = UndoneFolder();

        _previews.Failing.Add(folder);
        _clock.Advance(PreviewWatch.Wait);

        using var watch = Watch();

        watch.Start();

        await WaitUntil(() => Checks() is [{ Attempt: 2 }]);

        Assert.Equal(Earlier + Step + Step, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public async Task An_undo_after_starting_wakes_the_watch()
    {
        var folder = OldFolder();

        _previews.Failing.Add(folder);

        // A clock already past the wait, so the timer the undo sets fires at once.
        var later = new Clock();

        later.Advance(PreviewWatch.Wait);

        using var watch = new PreviewWatch(_box.Safety, _journal, _box.Shell, _previews, time: later);

        watch.Start();
        Customizer().Apply(folder, Sandbox.BlueStar);
        Restorer().UndoLast();

        await WaitUntil(() => Checks().Count == 0);

        // That clock stays past the wait, so all three checks ran and the failure never cleared.
        Assert.Equal(Earlier + (Step * 4), Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void A_disposed_watch_stops_listening()
    {
        var folder = OldFolder();
        var watch = Watch();

        watch.Start();
        watch.Dispose();

        Customizer().Apply(folder, Sandbox.BlueStar);
        Restorer().UndoLast();

        Assert.Single(Checks());
        Assert.Empty(_previews.Asked);
    }

    [Fact]
    public void Checks_survive_a_trip_through_the_journal()
    {
        _journal.QueuePreviewCheck([@"C:\a", @"C:\b"], attempt: 2);

        var snapshot = new ChangeJournal(_box.Paths.JournalFile).Read();
        var check = Assert.Single(snapshot.PreviewChecks);

        Assert.Equal([@"C:\a", @"C:\b"], check.Folders);
        Assert.Equal(2, check.Attempt);
        Assert.Equal(0, snapshot.CorruptRecords);

        _journal.MarkPreviewChecked(check.Id);

        snapshot = _journal.Read();

        Assert.Empty(snapshot.PreviewChecks);
        Assert.Equal(0, snapshot.CorruptRecords);
    }

    /// <summary>The real thing, read-only: a folder the shell has never seen has no failure saved.</summary>
    [Fact]
    public void The_real_shell_reports_no_failure_for_a_new_folder()
    {
        Assert.False(new ShellPreviews().HasSavedFailure(OldFolder()));
    }

    [Fact]
    public void The_real_shell_reports_no_failure_for_a_missing_folder()
    {
        Assert.False(new ShellPreviews().HasSavedFailure(Path.Combine(_box.Root, "nothing-here")));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FakePreviews : IShellPreviews
    {
        public HashSet<string> Failing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Asked { get; } = [];

        public bool HasSavedFailure(string folderPath)
        {
            Asked.Add(folderPath);
            return Failing.Contains(folderPath);
        }
    }

    private sealed class ThrowingPreviews : IShellPreviews
    {
        public bool HasSavedFailure(string folderPath) => throw new IOException("Simulated shell failure.");
    }
}
