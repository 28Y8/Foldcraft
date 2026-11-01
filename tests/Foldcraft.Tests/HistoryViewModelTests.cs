using Foldcraft.App.Services;
using Foldcraft.App.ViewModels;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Journal;

namespace Foldcraft.Tests;

public sealed class HistoryViewModelTests : IDisposable
{
    private readonly AppHarness _app = new();

    public void Dispose() => _app.Dispose();

    private HistoryViewModel History => _app.History;

    private static bool HasIcon(string folder) => File.Exists(Sandbox.IniPath(folder));

    /// <summary>Changes the design and applies it to one folder, as a separate change.</summary>
    private async Task ReapplyAsync(string folder, int swatch)
    {
        _app.Customize.ClearCommand.Execute(null);
        _app.Designer.SelectedSwatch = _app.Designer.Swatches[swatch];
        await _app.Customize.AddPathsAsync([folder]);
        await _app.Customize.ApplyCommand.ExecuteAsync();
        await History.RefreshAsync();
    }

    [Fact]
    public async Task An_empty_journal_shows_the_empty_state()
    {
        await History.RefreshAsync();

        Assert.True(History.IsEmpty);
        Assert.False(History.RestoreAllCommand.CanExecute(null));
        Assert.Null(History.Notice);
    }

    [Fact]
    public async Task A_refresh_does_not_create_the_data_on_disk()
    {
        await History.RefreshAsync();

        Assert.False(File.Exists(_app.Box.Paths.JournalFile));
    }

    [Fact]
    public async Task Changes_are_listed_newest_first_and_batches_stay_together()
    {
        var older = await _app.ApplyToNewFoldersAsync("a", "b");
        await ReapplyAsync(_app.Box.NewFolder("c"), 3);

        Assert.Equal(2, History.Entries.Count);
        Assert.Equal("Changed c", History.Entries[0].Title);
        Assert.Equal("Changed 2 folders", History.Entries[1].Title);
        Assert.Equal("a, b", History.Entries[1].Detail);
        Assert.Equal(older, History.Entries[1].Folders.Select(f => f.Path));
        Assert.False(History.IsEmpty);
    }

    [Fact]
    public async Task A_long_batch_summary_is_cut_short()
    {
        await _app.ApplyToNewFoldersAsync("a", "b", "c", "d");

        Assert.Equal("a, b, c and more", History.Entries[0].Detail);
    }

    [Fact]
    public async Task A_change_shows_its_time_and_icon()
    {
        await _app.ApplyToNewFoldersAsync("a");

        var entry = Assert.Single(History.Entries);

        Assert.StartsWith("Today at ", entry.When);
        Assert.Equal(string.Empty, entry.State);
        Assert.True(File.Exists(entry.IconFile));
        Assert.StartsWith(_app.Box.Paths.IconsDirectory, entry.IconFile);
    }

    [Fact]
    public async Task A_change_rolled_back_during_apply_is_not_history()
    {
        var folder = _app.Box.NewFolder();
        var id = _app.Services.Journal.RecordChange(new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = folder,
            DesktopIniExisted = false,
            OriginalFolderAttributes = FileAttributes.Directory,
            IconPath = "x.ico",
            WrittenSha256 = "x",
        });
        _app.Services.Journal.MarkRolledBack(id);

        await History.RefreshAsync();

        Assert.True(History.IsEmpty);
    }

    [Fact]
    public async Task An_interrupted_change_says_so_and_can_be_undone()
    {
        var folder = _app.Box.NewFolder();
        _app.Services.Journal.RecordChange(new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = folder,
            DesktopIniExisted = false,
            OriginalFolderAttributes = File.GetAttributes(folder),
            IconPath = "x.ico",
            WrittenSha256 = "x",
        });

        await History.RefreshAsync();

        var entry = Assert.Single(History.Entries);
        Assert.Contains("Interrupted", entry.State);
        Assert.True(entry.CanUndo);

        await entry.UndoCommand.ExecuteAsync();

        Assert.False(Assert.Single(History.Entries).CanUndo);
    }

    [Fact]
    public async Task Unreadable_records_are_mentioned_not_fatal()
    {
        await _app.ApplyToNewFoldersAsync("a");
        File.AppendAllText(_app.Box.Paths.JournalFile, "garbage line\ngarbage line\n");

        await History.RefreshAsync();

        Assert.Single(History.Entries);
        Assert.Contains("2 history records", History.Notice);
        Assert.DoesNotContain("journal", History.Notice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Undo_puts_the_whole_change_back_and_says_so()
    {
        var folders = await _app.ApplyToNewFoldersAsync("a", "b");

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.All(folders, f => Assert.False(HasIcon(f)));
        Assert.Equal("Undone", History.Entries[0].State);
        Assert.False(History.Entries[0].CanUndo);
        Assert.Equal("Put back 2 folders.", _app.Shell.Activity.Message);
        Assert.Equal(2, History.Results.Count);
        Assert.All(History.Results, r => Assert.Equal(StatusKind.Success, r.Kind));
        Assert.False(History.RestoreAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task Undoing_the_newest_change_asks_nothing()
    {
        await _app.ApplyToNewFoldersAsync("a");

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Empty(_app.Dialogs.Confirmations);
    }

    [Fact]
    public async Task Undoing_from_the_history_leaves_the_folder_for_a_second_look()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Equal(folder, Assert.Single(Assert.Single(_app.Services.Journal.Read().PreviewChecks).Folders));
    }

    [Fact]
    public async Task Undoing_the_newer_of_two_changes_leaves_the_older_look()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        var firstLook = File.ReadAllBytes(Sandbox.IniPath(folder));
        await ReapplyAsync(folder, 5);

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Equal(firstLook, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.Equal("Undone", History.Entries[0].State);
        Assert.True(History.Entries[1].CanUndo);
    }

    [Fact]
    public async Task Undoing_an_older_change_warns_that_newer_ones_go_too()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        await ReapplyAsync(folder, 5);

        Assert.Equal([folder], History.FoldersChangedSince(History.Entries[1]));
        Assert.Empty(History.FoldersChangedSince(History.Entries[0]));

        await History.Entries[1].UndoCommand.ExecuteAsync();

        Assert.Single(_app.Dialogs.Confirmations);
        Assert.False(HasIcon(folder));
        Assert.All(History.Entries, e => Assert.False(e.CanUndo));
    }

    [Fact]
    public async Task Declining_that_warning_changes_nothing()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        await ReapplyAsync(folder, 5);
        _app.Dialogs.ConfirmAnswer = false;
        var before = File.ReadAllBytes(Sandbox.IniPath(folder));

        await History.Entries[1].UndoCommand.ExecuteAsync();

        Assert.Equal(before, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.All(History.Entries, e => Assert.True(e.CanUndo));
        Assert.False(_app.Shell.Activity.IsBusy);
    }

    [Fact]
    public async Task Restoring_one_folder_leaves_the_rest_of_the_change_in_place()
    {
        var folders = await _app.ApplyToNewFoldersAsync("a", "b");

        await History.Entries[0].Folders[0].RestoreCommand.ExecuteAsync();

        Assert.False(HasIcon(folders[0]));
        Assert.True(HasIcon(folders[1]));
        Assert.Equal("Partly undone", History.Entries[0].State);
        Assert.False(History.Entries[0].Folders[0].IsActive);
        Assert.Equal("Undone", History.Entries[0].Folders[0].State);
        Assert.True(History.Entries[0].CanUndo);
    }

    [Fact]
    public async Task Restoring_one_folder_goes_back_past_every_change_to_the_original()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        await ReapplyAsync(folder, 5);

        await History.Entries[0].Folders[0].RestoreCommand.ExecuteAsync();

        Assert.False(HasIcon(folder));
        Assert.All(History.Entries, e => Assert.False(e.CanUndo));
    }

    [Fact]
    public async Task Restore_everything_asks_first_and_declining_changes_nothing()
    {
        var folders = await _app.ApplyToNewFoldersAsync("a", "b");
        _app.Dialogs.ConfirmAnswer = false;

        await History.RestoreAllCommand.ExecuteAsync();

        Assert.Equal("Restore everything?", Assert.Single(_app.Dialogs.Confirmations));
        Assert.All(folders, f => Assert.True(HasIcon(f)));
    }

    [Fact]
    public async Task Restore_everything_puts_every_folder_back()
    {
        var folders = await _app.ApplyToNewFoldersAsync("a", "b");
        var more = _app.Box.NewFolder("c");
        await ReapplyAsync(more, 5);
        await ReapplyAsync(folders[0], 6);

        await History.RestoreAllCommand.ExecuteAsync();

        Assert.All(folders.Append(more), f => Assert.False(HasIcon(f)));
        Assert.Equal("Put back 3 folders.", _app.Shell.Activity.Message);
        Assert.False(History.RestoreAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_conflict_is_asked_about_in_plain_words_and_nothing_changes_by_default()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        AppHarness.EditIni(folder);
        var edited = File.ReadAllBytes(Sandbox.IniPath(folder));

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Equal([folder], Assert.Single(_app.Dialogs.ConflictPrompts));
        Assert.Equal(edited, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.True(History.Entries[0].CanUndo);

        var result = Assert.Single(History.Results);
        Assert.Equal(StatusKind.Warning, result.Kind);
        Assert.DoesNotContain("desktop.ini", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("desktop.ini", _app.Shell.Activity.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Keeping_the_current_version_leaves_the_folder_and_stops_tracking_it()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        AppHarness.EditIni(folder);
        var edited = File.ReadAllBytes(Sandbox.IniPath(folder));
        _app.Dialogs.ConflictAnswer = ConflictChoice.KeepCurrent;

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Equal(edited, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.False(History.Entries[0].CanUndo);
        Assert.Equal("No longer tracked", History.Entries[0].State);
    }

    [Fact]
    public async Task Restoring_anyway_replaces_the_other_change()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        AppHarness.EditIni(folder);
        _app.Dialogs.ConflictAnswer = ConflictChoice.RestoreAnyway;

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.False(HasIcon(folder));
        Assert.Equal("Undone", History.Entries[0].State);
    }

    [Fact]
    public async Task A_decision_about_one_conflict_never_touches_the_other_folders_results()
    {
        var folders = await _app.ApplyToNewFoldersAsync("clean", "edited");
        AppHarness.EditIni(folders[1]);
        _app.Dialogs.ConflictAnswer = ConflictChoice.KeepCurrent;

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Equal([folders[1]], Assert.Single(_app.Dialogs.ConflictPrompts));
        Assert.False(HasIcon(folders[0]));
        Assert.True(HasIcon(folders[1]));
        Assert.Equal(2, History.Results.Count);
    }

    [Fact]
    public async Task Restore_everything_resolves_conflicts_folder_by_folder()
    {
        var folders = await _app.ApplyToNewFoldersAsync("clean", "edited");
        AppHarness.EditIni(folders[1]);
        _app.Dialogs.ConflictAnswer = ConflictChoice.RestoreAnyway;

        await History.RestoreAllCommand.ExecuteAsync();

        Assert.All(folders, f => Assert.False(HasIcon(f)));
    }

    [Fact]
    public async Task A_missing_folder_is_kept_for_later_unless_the_user_lets_it_go()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        ForceDelete(folder);

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Equal([folder], Assert.Single(_app.Dialogs.MissingPrompts));
        Assert.True(History.Entries[0].CanUndo);
        Assert.Contains("try again", Assert.Single(History.Results).Message);
    }

    [Fact]
    public async Task A_missing_folder_can_be_let_go_so_it_cannot_block_anything()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        ForceDelete(folder);
        _app.Dialogs.ForgetMissingAnswer = true;

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.False(History.Entries[0].CanUndo);
        Assert.Equal("No longer tracked", History.Entries[0].State);
    }

    [Fact]
    public async Task A_renamed_folder_can_be_found_during_an_undo()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        var renamed = folder + "-renamed";
        Directory.Move(folder, renamed);
        _app.Dialogs.MissingAnswer = MissingChoice.FindFolder;
        _app.Dialogs.MovedFolderAnswer = _ => renamed;

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.False(HasIcon(renamed));
        Assert.False(File.GetAttributes(renamed).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal("Undone", History.Entries[0].State);
        Assert.Equal("Undone", History.Entries[0].Folders[0].State);
        Assert.Equal("Put back 1 folder.", _app.Shell.Activity.Message);
        Assert.Equal(renamed, Assert.Single(History.Results).Path, ignoreCase: true);
    }

    [Fact]
    public async Task Restoring_one_renamed_folder_follows_it_to_the_new_location()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        var renamed = folder + "-renamed";
        Directory.Move(folder, renamed);
        _app.Dialogs.MissingAnswer = MissingChoice.FindFolder;
        _app.Dialogs.MovedFolderAnswer = _ => renamed;

        await History.Entries[0].Folders[0].RestoreCommand.ExecuteAsync();

        Assert.False(HasIcon(renamed));
        Assert.Equal("Put back 1 folder.", _app.Shell.Activity.Message);
    }

    [Fact]
    public async Task Restore_everything_follows_a_renamed_folder_and_finishes_the_rest()
    {
        var folders = await _app.ApplyToNewFoldersAsync("a", "b");
        var renamed = folders[0] + "-renamed";
        Directory.Move(folders[0], renamed);
        _app.Dialogs.MissingAnswer = MissingChoice.FindFolder;
        _app.Dialogs.MovedFolderAnswer = _ => renamed;

        await History.RestoreAllCommand.ExecuteAsync();

        Assert.False(HasIcon(renamed));
        Assert.False(HasIcon(folders[1]));
        Assert.Equal("Put back 2 folders.", _app.Shell.Activity.Message);
        Assert.False(History.RestoreAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_moved_folder_that_was_also_edited_still_gets_the_conflict_question()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        var renamed = folder + "-renamed";
        Directory.Move(folder, renamed);
        AppHarness.EditIni(renamed);
        _app.Dialogs.MissingAnswer = MissingChoice.FindFolder;
        _app.Dialogs.MovedFolderAnswer = _ => renamed;
        var edited = File.ReadAllBytes(Sandbox.IniPath(renamed));

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Single(_app.Dialogs.ConflictPrompts);
        Assert.Equal(edited, File.ReadAllBytes(Sandbox.IniPath(renamed)));
    }

    [Fact]
    public async Task Declining_to_pick_a_folder_keeps_it_in_the_history()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        Directory.Move(folder, folder + "-renamed");
        _app.Dialogs.MissingAnswer = MissingChoice.FindFolder;

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.True(History.Entries[0].CanUndo);
        Assert.Contains("try again", Assert.Single(History.Results).Message);
    }

    [Fact]
    public async Task Picking_the_wrong_folder_says_so_and_changes_nothing()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        var unrelated = _app.Box.NewFolder("unrelated");
        File.WriteAllText(Path.Combine(unrelated, "note.txt"), "mine");
        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        File.Delete(Sandbox.IniPath(folder));
        File.SetAttributes(folder, FileAttributes.Directory);
        Directory.Delete(folder);
        _app.Dialogs.MissingAnswer = MissingChoice.FindFolder;
        _app.Dialogs.MovedFolderAnswer = _ => unrelated;

        await History.Entries[0].UndoCommand.ExecuteAsync();

        var result = Assert.Single(History.Results);

        Assert.True(History.Entries[0].CanUndo);
        Assert.Equal(StatusKind.Warning, result.Kind);
        Assert.Contains("chosen folder", result.Message);
        Assert.Single(Directory.GetFileSystemEntries(unrelated));
    }

    [Fact]
    public async Task A_different_folder_at_the_path_is_asked_about_and_can_be_let_go()
    {
        var folder = (await _app.ApplyToNewFoldersAsync("a"))[0];
        ForceDelete(folder);
        Directory.CreateDirectory(folder);
        File.SetAttributes(folder, FileAttributes.ReadOnly);
        _app.Dialogs.ForgetMissingAnswer = true;

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Equal([folder], Assert.Single(_app.Dialogs.MissingPrompts));
        Assert.False(History.Entries[0].CanUndo);
        Assert.True(File.GetAttributes(folder).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public async Task Each_undo_asks_for_focus_on_its_own_entry()
    {
        await _app.ApplyToNewFoldersAsync("a");
        var batch = History.Entries[0].BatchId;
        Guid? requested = Guid.Empty;
        History.FocusRequested += (_, id) => requested = id;

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Equal(batch, requested);
    }

    [Fact]
    public async Task Dismissing_results_asks_for_focus_to_move()
    {
        await _app.ApplyToNewFoldersAsync("a");
        await History.Entries[0].UndoCommand.ExecuteAsync();
        Guid? requested = Guid.Empty;
        History.FocusRequested += (_, id) => requested = id;

        History.DismissResultsCommand.Execute(null);

        Assert.Null(requested);
    }

    [Fact]
    public async Task Progress_never_runs_past_its_total_when_conflicts_are_retried()
    {
        var folders = await _app.ApplyToNewFoldersAsync("a", "b");
        AppHarness.EditIni(folders[0]);
        AppHarness.EditIni(folders[1]);
        _app.Dialogs.ConflictAnswer = ConflictChoice.RestoreAnyway;

        var seen = new System.Collections.Concurrent.ConcurrentQueue<string>();

        _app.Shell.Activity.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActivityViewModel.ProgressText))
            {
                seen.Enqueue(_app.Shell.Activity.ProgressText);
            }
        };

        await History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.NotEmpty(seen);
        Assert.DoesNotContain(seen, text => text.Length > 0 && int.Parse(text.Split(' ')[0]) > int.Parse(text.Split(' ')[2]));
    }

    [Fact]
    public async Task An_interrupted_change_is_mentioned_at_startup()
    {
        _app.Services.Journal.RecordChange(new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = _app.Box.NewFolder(),
            DesktopIniExisted = false,
            OriginalFolderAttributes = FileAttributes.Directory,
            IconPath = "x.ico",
            WrittenSha256 = "x",
        });

        await _app.Shell.InitializeAsync();

        Assert.Contains("interrupted", _app.Shell.Activity.Message);
        Assert.Contains("interrupted", History.Notice);
    }

    [Fact]
    public async Task Startup_says_nothing_extra_when_nothing_was_interrupted()
    {
        await _app.ApplyToNewFoldersAsync("a");

        await _app.Shell.InitializeAsync();

        Assert.DoesNotContain("interrupted", _app.Shell.Activity.Message);
        Assert.Null(History.Notice);
    }

    [Fact]
    public async Task Results_can_be_dismissed()
    {
        await _app.ApplyToNewFoldersAsync("a");
        await History.Entries[0].UndoCommand.ExecuteAsync();

        History.DismissResultsCommand.Execute(null);

        Assert.Empty(History.Results);
    }

    [Fact]
    public async Task While_work_runs_undo_and_restore_are_disabled()
    {
        await _app.ApplyToNewFoldersAsync("a");

        var changes = 0;
        History.Entries[0].UndoCommand.CanExecuteChanged += (_, _) => changes++;

        _app.Shell.Activity.Begin("Busy");

        Assert.True(changes > 0);
        Assert.False(History.Entries[0].UndoCommand.CanExecute(null));
        Assert.False(History.Entries[0].Folders[0].RestoreCommand.CanExecute(null));
        Assert.False(History.RestoreAllCommand.CanExecute(null));

        _app.Shell.Activity.Finish("Done");

        Assert.True(History.Entries[0].UndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refreshing_keeps_expanded_entries_expanded()
    {
        await _app.ApplyToNewFoldersAsync("a");
        History.Entries[0].IsExpanded = true;

        await History.RefreshAsync();

        Assert.True(History.Entries[0].IsExpanded);
    }

    [Fact]
    public async Task A_history_that_cannot_be_read_says_so()
    {
        Directory.CreateDirectory(_app.Box.Paths.JournalFile);

        await History.RefreshAsync();

        Assert.Contains("couldn't read its history", History.Notice);
        Assert.False(_app.Dialogs.Errors.Count > 0);
        Assert.True(File.Exists(_app.Box.Paths.LogFile));
    }

    /// <summary>The folder is read-only once customized, which blocks an ordinary delete.</summary>
    private static void ForceDelete(string folder)
    {
        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        File.Delete(Sandbox.IniPath(folder));
        File.SetAttributes(folder, FileAttributes.Directory);
        Directory.Delete(folder);
    }
}
