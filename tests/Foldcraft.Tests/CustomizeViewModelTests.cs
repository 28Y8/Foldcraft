using Foldcraft.App.ViewModels;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Safety;

namespace Foldcraft.Tests;

public sealed class CustomizeViewModelTests : IDisposable
{
    private readonly AppHarness _app = new();

    public void Dispose() => _app.Dispose();

    private CustomizeViewModel Customize => _app.Customize;

    private static bool HasIcon(string folder) => File.Exists(Sandbox.IniPath(folder));

    [Fact]
    public async Task Starts_empty_with_nothing_to_apply()
    {
        Assert.False(Customize.HasFolders);
        Assert.Equal("Apply", Customize.ApplyLabel);
        Assert.Equal("Add a folder to get started.", Customize.ApplyHint);
        Assert.False(Customize.ApplyCommand.CanExecute(null));
        Assert.False(Customize.ClearCommand.CanExecute(null));

        await Customize.ApplyCommand.ExecuteAsync();
    }

    [Fact]
    public async Task Add_Folders_uses_the_dialog_and_shows_each_folder()
    {
        var folder = _app.Box.NewFolder("Photos");
        _app.Dialogs.FoldersToPick = [folder];

        await Customize.AddFoldersCommand.ExecuteAsync();

        var row = Assert.Single(Customize.Folders);
        Assert.Equal("Photos", row.Name);
        Assert.Equal("Ready.", row.Status);
        Assert.True(row.IsAllowed);
        Assert.Equal("Apply to 1 folder", Customize.ApplyLabel);
        Assert.True(Customize.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Canceling_the_folder_dialog_adds_nothing()
    {
        await Customize.AddFoldersCommand.ExecuteAsync();

        Assert.Empty(Customize.Folders);
    }

    [Fact]
    public async Task Dropped_folders_are_added()
    {
        await Customize.DropCommand.ExecuteAsync(new[] { _app.Box.NewFolder("a"), _app.Box.NewFolder("b") });

        Assert.Equal(2, Customize.Folders.Count);
        Assert.Equal("Apply to 2 folders", Customize.ApplyLabel);
    }

    [Fact]
    public async Task The_same_folder_is_only_listed_once_however_it_is_spelled()
    {
        var folder = _app.Box.NewFolder();

        await Customize.AddPathsAsync([folder, folder + "\\", folder.ToUpperInvariant()]);
        await Customize.AddPathsAsync([folder]);

        Assert.Single(Customize.Folders);
    }

    [Fact]
    public async Task A_refused_folder_stays_listed_with_a_plain_reason_and_is_not_counted()
    {
        var good = _app.Box.NewFolder("good");

        await Customize.AddPathsAsync([good, _app.Box.FakeProfile]);

        var refused = Customize.Folders.Single(f => !f.IsAllowed);
        Assert.Contains("main user folder", refused.Status);
        Assert.Equal(StatusKind.Error, refused.Kind);
        Assert.Equal("Apply to 1 folder", Customize.ApplyLabel);
        Assert.Equal("1 folder in the list can't be changed.", Customize.ApplyHint);
    }

    [Fact]
    public async Task A_dropped_file_is_explained()
    {
        var file = Path.Combine(_app.Box.Root, "notes.txt");
        File.WriteAllText(file, "hello");

        await Customize.AddPathsAsync([file]);

        Assert.Equal("This is a file, not a folder.", Assert.Single(Customize.Folders).Status);
        Assert.False(Customize.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_folder_that_does_not_exist_is_explained()
    {
        await Customize.AddPathsAsync([Path.Combine(_app.Box.Root, "missing")]);

        Assert.Contains("can't be found", Assert.Single(Customize.Folders).Status);
    }

    [Fact]
    public async Task A_row_explains_when_windows_may_not_show_the_change()
    {
        var segment = new string('x', 100);
        var deep = Path.Combine(_app.Box.Root, segment, segment, segment);
        Directory.CreateDirectory(deep);

        await Customize.AddPathsAsync([deep]);

        var row = Assert.Single(Customize.Folders);
        Assert.Equal(StatusKind.Warning, row.Kind);
        Assert.Contains("very long", row.Status);
        Assert.True(row.IsAllowed);
    }

    [Fact]
    public async Task Rows_can_be_removed_without_touching_the_folders()
    {
        var a = _app.Box.NewFolder("a");
        var b = _app.Box.NewFolder("b");
        await Customize.AddPathsAsync([a, b]);

        Assert.False(Customize.RemoveSelectedCommand.CanExecute(null));

        var changes = 0;
        Customize.RemoveSelectedCommand.CanExecuteChanged += (_, _) => changes++;
        Customize.Folders[0].IsSelected = true;

        Assert.True(changes > 0);
        Assert.True(Customize.RemoveSelectedCommand.CanExecute(null));

        Customize.RemoveSelectedCommand.Execute(null);

        Assert.Equal("b", Assert.Single(Customize.Folders).Name);
        Assert.True(Directory.Exists(a));
        Assert.Equal("Apply to 1 folder", Customize.ApplyLabel);

        Customize.ClearCommand.Execute(null);

        Assert.Empty(Customize.Folders);
        Assert.False(Customize.HasFolders);
    }

    [Fact]
    public async Task Apply_changes_folders_and_reports_each_one()
    {
        var folders = await _app.ApplyToNewFoldersAsync("a", "b");

        Assert.All(folders, f => Assert.True(HasIcon(f)));
        Assert.All(Customize.Folders, row =>
        {
            Assert.Equal("Done.", row.Status);
            Assert.Equal(StatusKind.Success, row.Kind);
            Assert.False(row.IsWorking);
        });

        Assert.Contains("Changed 2 folders.", _app.Shell.Activity.Message);
        Assert.False(_app.Shell.Activity.IsBusy);
    }

    [Fact]
    public async Task A_location_too_long_for_Explorer_keeps_its_warning_after_apply()
    {
        var path = _app.Box.NewFolder("deep");

        while (path.Length < 250)
        {
            path = Path.Combine(path, new string('L', Math.Min(40, 250 - path.Length + 1)));
        }

        Directory.CreateDirectory(path);
        await Customize.AddPathsAsync([path]);
        await Customize.ApplyCommand.ExecuteAsync();

        var row = Assert.Single(Customize.Folders);

        Assert.True(HasIcon(path));
        Assert.Equal(StatusKind.Warning, row.Kind);
        Assert.Contains("can't show", row.Status);
        Assert.Contains("too long", _app.Shell.Activity.Message);
    }

    [Fact]
    public async Task Apply_makes_one_undoable_change_however_many_folders()
    {
        await _app.ApplyToNewFoldersAsync("a", "b", "c");

        Assert.Single(_app.History.Entries);
    }

    [Fact]
    public async Task A_refused_folder_is_never_touched_by_apply()
    {
        var safe = _app.Box.NewFolder("safe");
        var protectedFolder = Path.Combine(_app.Box.FakeAppData, "Roaming");
        Directory.CreateDirectory(protectedFolder);

        await Customize.AddPathsAsync([safe, protectedFolder]);
        await Customize.ApplyCommand.ExecuteAsync();

        Assert.True(HasIcon(safe));
        Assert.False(HasIcon(protectedFolder));
    }

    [Fact]
    public async Task After_applying_there_is_nothing_left_to_do_until_the_design_changes()
    {
        await _app.ApplyToNewFoldersAsync("a");

        Assert.Equal("Already applied", Customize.ApplyLabel);
        Assert.False(Customize.ApplyCommand.CanExecute(null));

        _app.Designer.SelectedSwatch = _app.Designer.Swatches[4];

        Assert.Equal("Apply to 1 folder", Customize.ApplyLabel);
        Assert.Equal("Ready.", Customize.Folders[0].Status);
        Assert.True(Customize.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Only_folders_that_would_change_are_counted()
    {
        await _app.ApplyToNewFoldersAsync("a");

        await Customize.AddPathsAsync([_app.Box.NewFolder("b")]);

        Assert.Equal("Apply to 1 folder", Customize.ApplyLabel);
    }

    [Fact]
    public async Task A_folder_that_disappears_before_apply_is_reported_and_the_rest_continue()
    {
        var gone = _app.Box.NewFolder("gone");
        var kept = _app.Box.NewFolder("kept");
        await Customize.AddPathsAsync([gone, kept]);
        Directory.Delete(gone);

        await Customize.ApplyCommand.ExecuteAsync();

        Assert.Contains("can't be found", Customize.Folders.Single(f => f.Name == "gone").Status);
        Assert.Equal("Done.", Customize.Folders.Single(f => f.Name == "kept").Status);
        Assert.Contains("1 couldn't be changed.", _app.Shell.Activity.Message);
    }

    [Fact]
    public async Task A_broken_design_does_not_touch_any_folder_and_says_why()
    {
        var folder = _app.Box.NewFolder();
        _app.Dialogs.ImageToPick = Path.Combine(_app.Box.Root, "gone.png");
        await Customize.AddPathsAsync([folder]);

        _app.Designer.PickImageCommand.Execute(null);
        await Customize.ApplyCommand.ExecuteAsync();

        Assert.False(HasIcon(folder));
        Assert.Equal("The image file could not be found.", Customize.Folders[0].Status);
        Assert.Equal(StatusKind.Error, Customize.Folders[0].Kind);
    }

    [Fact]
    public async Task An_unfinished_color_blocks_apply()
    {
        await Customize.AddPathsAsync([_app.Box.NewFolder()]);

        _app.Designer.HexText = "#12";

        Assert.False(Customize.ApplyCommand.CanExecute(null));

        _app.Designer.HexText = "#123456";

        Assert.True(Customize.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_bad_picture_blocks_apply_once_the_preview_has_failed()
    {
        var path = Path.Combine(_app.Box.Root, "bad.png");
        File.WriteAllText(path, "not an image");
        _app.Dialogs.ImageToPick = path;
        await Customize.AddPathsAsync([_app.Box.NewFolder()]);

        _app.Designer.PickImageCommand.Execute(null);
        await _app.Designer.PreviewTask;

        Assert.False(Customize.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task While_work_runs_nothing_else_can_start()
    {
        await Customize.AddPathsAsync([_app.Box.NewFolder()]);
        Customize.Folders[0].IsSelected = true;

        _app.Shell.Activity.Begin("Busy");

        Assert.False(Customize.ApplyCommand.CanExecute(null));
        Assert.False(Customize.AddFoldersCommand.CanExecute(null));
        Assert.False(Customize.DropCommand.CanExecute(null));
        Assert.False(Customize.RemoveSelectedCommand.CanExecute(null));
        Assert.False(Customize.ClearCommand.CanExecute(null));

        _app.Shell.Activity.Finish("Done");

        Assert.True(Customize.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Canceling_stops_at_the_next_folder_and_marks_the_rest_skipped()
    {
        AppHarness? app = null;

        // The first folder to finish cancels the batch, so the outcome doesn't depend on timing.
        using var harness = app = new AppHarness(new CallbackShell(_ => app!.Shell.Activity.Cancel()));

        var folders = new[] { harness.Box.NewFolder("a"), harness.Box.NewFolder("b"), harness.Box.NewFolder("c") };
        await harness.Customize.AddPathsAsync(folders);

        await harness.Customize.ApplyCommand.ExecuteAsync();

        Assert.True(HasIcon(folders[0]));
        Assert.False(HasIcon(folders[1]));
        Assert.False(HasIcon(folders[2]));

        var rows = harness.Customize.Folders;
        Assert.Equal("Done.", rows[0].Status);
        Assert.Equal("Skipped because you canceled.", rows[1].Status);
        Assert.Contains("2 skipped.", harness.Shell.Activity.Message);
        Assert.Equal("Apply to 2 folders", harness.Customize.ApplyLabel);
    }

    [Fact]
    public async Task A_folder_refused_at_apply_time_is_no_longer_offered()
    {
        var folder = _app.Box.NewFolder("real");
        var target = _app.Box.NewFolder("elsewhere");
        await Customize.AddPathsAsync([folder]);

        Directory.Delete(folder);
        Sandbox.CreateJunction(folder, target);
        await Customize.ApplyCommand.ExecuteAsync();

        var row = Assert.Single(Customize.Folders);
        Assert.False(row.IsAllowed);
        Assert.Contains("link to another location", row.Status);
        Assert.False(HasIcon(target));
        Assert.Equal("Apply", Customize.ApplyLabel);
    }

    [Fact]
    public async Task Undoing_elsewhere_makes_finished_rows_ready_again()
    {
        await _app.ApplyToNewFoldersAsync("a");

        await _app.History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Equal("Ready.", Customize.Folders[0].Status);
        Assert.Equal("Apply to 1 folder", Customize.ApplyLabel);
    }

    [Fact]
    public void A_row_screen_reader_label_includes_its_status()
    {
        var row = new FolderRowViewModel(new FolderSafetyResult
        {
            RequestedPath = @"C:\Users\me\Photos",
            NormalizedPath = @"C:\Users\me\Photos",
            ResolvedPath = @"C:\Users\me\Photos",
        });

        Assert.Equal(@"Photos. C:\Users\me\Photos. Ready.", row.AccessibleText);

        row.ShowResult(new FolderApplyResult { RequestedPath = row.Path, Outcome = ApplyOutcome.Canceled }, Sandbox.BlueStar);

        Assert.EndsWith("Skipped because you canceled.", row.AccessibleText);
    }

    [Fact]
    public void A_row_for_an_unparseable_path_still_has_a_name()
    {
        var row = new FolderRowViewModel(new FolderSafetyResult
        {
            RequestedPath = "\\\\?\\Volume{x}",
            Refusal = FolderRefusal.UnsupportedPathForm,
            Reason = "raw",
        });

        Assert.False(string.IsNullOrEmpty(row.Name));
        Assert.Equal("Foldcraft can't work with this kind of location.", row.Status);
    }
}
