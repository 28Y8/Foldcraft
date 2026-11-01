namespace Foldcraft.Tests;

public sealed class ShellViewModelTests : IDisposable
{
    private readonly AppHarness _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Initializing_draws_the_preview_and_touches_no_folders()
    {
        await _app.Shell.InitializeAsync();

        Assert.NotNull(_app.Designer.LargePreview);
        Assert.False(File.Exists(_app.Box.Paths.JournalFile));
        Assert.False(Directory.Exists(_app.Box.Paths.IconsDirectory));
    }

    [Fact]
    public void Closing_while_idle_is_allowed()
    {
        Assert.True(_app.Shell.TryClose());
    }

    [Fact]
    public void Closing_during_work_cancels_it_and_closes_when_it_stops()
    {
        var closed = 0;
        _app.Shell.CloseRequested += (_, _) => closed++;
        var token = _app.Shell.Activity.Begin("Working", 2, cancellable: true);

        Assert.False(_app.Shell.TryClose());
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(0, closed);

        _app.Shell.Activity.Finish("Stopped");

        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task Opening_the_history_screen_refreshes_it()
    {
        var folder = _app.Box.NewFolder();
        _app.Box.Customizer().Apply(folder, Sandbox.BlueStar);
        await _app.History.RefreshAsync();
        Assert.Single(_app.History.Entries);

        _app.Box.Customizer().Apply(_app.Box.NewFolder("other"), Sandbox.BlueStar);

        _app.Shell.SelectedTab = 1;

        for (var i = 0; i < 200 && _app.History.Entries.Count < 2; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(2, _app.History.Entries.Count);
    }

    [Fact]
    public async Task An_apply_updates_the_history_without_being_asked()
    {
        await _app.Customize.AddPathsAsync([_app.Box.NewFolder()]);

        await _app.Customize.ApplyCommand.ExecuteAsync();

        for (var i = 0; i < 200 && _app.History.Entries.Count < 1; i++)
        {
            await Task.Delay(10);
        }

        Assert.Single(_app.History.Entries);
    }

    [Fact]
    public async Task A_theme_applied_from_its_own_screen_lets_customize_apply_again()
    {
        var folder = _app.Box.NewFolder();

        await _app.Customize.AddPathsAsync([folder]);
        await _app.Customize.ApplyCommand.ExecuteAsync();

        Assert.Equal("Already applied", _app.Customize.ApplyLabel);

        await _app.Themes.RefreshAsync();
        _app.Themes.SelectedTile = _app.Themes.Themes[0];
        _app.Dialogs.FoldersToPick = [folder];
        await _app.Themes.ApplyToFoldersCommand.ExecuteAsync();

        Assert.Equal("Apply to 1 folder", _app.Customize.ApplyLabel);
        Assert.True(_app.Customize.ApplyCommand.CanExecute(null));
        Assert.Equal("Ready.", Assert.Single(_app.Customize.Folders).Status);
    }

    [Fact]
    public async Task A_rule_applied_from_its_own_screen_lets_customize_apply_again()
    {
        await _app.Shell.InitializeAsync();

        var folder = _app.Box.NewFolder("photos");

        await _app.Customize.AddPathsAsync([folder]);
        await _app.Customize.ApplyCommand.ExecuteAsync();

        _app.Rules.PatternText = "photos";
        _app.Rules.AddRuleCommand.Execute(null);
        _app.Dialogs.FolderToPick = Path.GetDirectoryName(folder);
        _app.Rules.PickScopeCommand.Execute(null);
        await _app.Rules.PreviewCommand.ExecuteAsync();
        await _app.Rules.ApplyCommand.ExecuteAsync();

        Assert.Equal("Apply to 1 folder", _app.Customize.ApplyLabel);
    }

    [Fact]
    public async Task A_look_from_the_apps_screen_lets_customize_apply_again()
    {
        var folder = _app.Box.NewFolder();

        await _app.Customize.AddPathsAsync([folder]);
        await _app.Customize.ApplyCommand.ExecuteAsync();

        _app.Pc.AddApp("Paper Boat", System.Drawing.Color.Teal);
        await _app.Shell.Apps.ScanCommand.ExecuteAsync();
        _app.Shell.Apps.SelectedTile = Assert.Single(_app.Shell.Apps.Tiles);
        _app.Dialogs.FolderToPick = folder;
        await _app.Shell.Apps.ThemeExistingCommand.ExecuteAsync();

        Assert.Equal("Apply to 1 folder", _app.Customize.ApplyLabel);
    }

    [Fact]
    public async Task A_failing_command_is_logged_and_explained_kindly()
    {
        await _app.Customize.AddPathsAsync([_app.Box.NewFolder()]);

        // A null path can't come from the real dialog, which is what makes it a clean way to fail.
        _app.Dialogs.FoldersToPick = [null!];
        await _app.Customize.AddFoldersCommand.ExecuteAsync();

        var message = Assert.Single(_app.Dialogs.Errors);
        Assert.Contains(_app.Box.Paths.LogFile, message);
        Assert.DoesNotContain("Exception", message);
        Assert.Contains("A command failed", File.ReadAllText(_app.Box.Paths.LogFile));
    }
}
