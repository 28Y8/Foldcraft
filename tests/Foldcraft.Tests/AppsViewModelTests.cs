using System.Drawing;
using Foldcraft.App.Services;
using Foldcraft.App.ViewModels;
using Foldcraft.Core.Apps;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Safety;

namespace Foldcraft.Tests;

public sealed class AppsViewModelTests : IDisposable
{
    private readonly AppHarness _app = new();

    public void Dispose() => _app.Dispose();

    private AppsViewModel Apps => _app.Shell.Apps;

    private FakePc Pc => _app.Pc;

    private async Task ScanAsync() => await Apps.ScanCommand.ExecuteAsync();

    private async Task<AppTileViewModel> ScanOneAsync(string name = "Paper Boat", Color? color = null, int iconSize = 256)
    {
        Pc.AddApp(name, color ?? Color.Teal, iconSize);
        await ScanAsync();
        return Assert.Single(Apps.Tiles);
    }

    private string Destination()
    {
        var folder = _app.Box.NewFolder("Games");
        _app.Dialogs.FolderToPick = folder;
        Apps.PickDestinationCommand.Execute(null);
        return folder;
    }

    [Fact]
    public void Before_a_scan_the_screen_says_what_to_do()
    {
        Assert.True(Apps.HasNoTiles);
        Assert.Contains("Press Scan", Apps.EmptyMessage);
    }

    [Fact]
    public async Task Finding_nothing_shows_a_helpful_message()
    {
        await ScanAsync();

        Assert.True(Apps.HasNoTiles);
        Assert.Contains("didn't find any", Apps.EmptyMessage);
        Assert.Contains("Start Menu", Apps.EmptyMessage);
        Assert.Contains("No apps or games found", Apps.Summary);
    }

    [Fact]
    public void Starting_the_app_does_not_scan_or_touch_the_registry()
    {
        Assert.Equal(0, _app.Registry.Reads);
        Assert.Equal(0, _app.Registry.Writes);
        Assert.Empty(Apps.Tiles);
    }

    [Fact]
    public async Task A_scan_fills_the_grid_with_previews()
    {
        var tile = await ScanOneAsync();

        Assert.True(tile.IsReady);
        Assert.NotNull(tile.Preview);
        Assert.NotNull(tile.MediumPreview);
        Assert.NotNull(tile.SmallPreview);
        Assert.Equal("Start Menu", tile.SourceName);
    }

    [Fact]
    public async Task A_scan_writes_nothing_at_all()
    {
        await ScanOneAsync();

        Assert.False(File.Exists(_app.Box.Paths.JournalFile));
        Assert.False(Directory.Exists(_app.Box.Paths.AppArtDirectory));
        Assert.False(Directory.Exists(_app.Box.Paths.IconsDirectory));
        Assert.False(Directory.Exists(_app.Box.Paths.ScratchDirectory));
    }

    [Fact]
    public async Task A_scan_never_writes_to_the_registry()
    {
        Pc.InstallSteam(_app.Registry);
        var writes = _app.Registry.Writes;

        await ScanAsync();

        Assert.Equal(writes, _app.Registry.Writes);
    }

    [Fact]
    public async Task Nothing_is_selected_until_the_user_says_so()
    {
        await ScanOneAsync();

        Assert.Equal(0, Apps.IncludedCount);
        Assert.False(Apps.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Search_narrows_the_grid_and_clearing_brings_everything_back()
    {
        Pc.AddApp("Paper Boat", Color.Teal);
        Pc.AddApp("Quiet Studio", Color.Orange);
        await ScanAsync();

        Apps.SearchText = "quiet";
        Assert.Equal(["Quiet Studio"], Apps.Tiles.Select(tile => tile.Name));

        Apps.SearchText = string.Empty;
        Assert.Equal(2, Apps.Tiles.Count);
    }

    [Fact]
    public async Task Search_matches_every_word_in_any_order()
    {
        Pc.AddApp("Paper Boat", Color.Teal);
        Pc.AddApp("Paper Plane", Color.Orange);
        await ScanAsync();

        Apps.SearchText = "boat paper";

        Assert.Equal(["Paper Boat"], Apps.Tiles.Select(tile => tile.Name));
    }

    [Fact]
    public async Task A_search_with_no_match_says_so()
    {
        await ScanOneAsync();

        Apps.SearchText = "nothing like this";

        Assert.True(Apps.HasNoTiles);
        Assert.Contains("Nothing fits", Apps.EmptyMessage);
    }

    [Fact]
    public async Task The_filter_shows_one_source()
    {
        Pc.InstallSteam(_app.Registry);
        Pc.AddSteamGame(Pc.Steam, "100", "Lantern Keep");
        Pc.AddApp("Paper Boat", Color.Teal);
        await ScanAsync();

        Assert.Contains("Steam", Apps.Sources);
        Assert.Contains("Start Menu", Apps.Sources);

        Apps.SelectedSource = "Start Menu";

        Assert.Equal(["Paper Boat"], Apps.Tiles.Select(tile => tile.Name));
    }

    [Fact]
    public async Task A_source_with_nothing_left_after_a_new_scan_drops_off_the_filter()
    {
        Pc.AddApp("Paper Boat", Color.Teal);
        await ScanAsync();
        Apps.SelectedSource = "Start Menu";

        foreach (var link in Directory.GetFiles(Pc.UserMenu))
        {
            File.Delete(link);
        }

        await ScanAsync();

        Assert.Equal([AppsViewModel.AllSources], Apps.Sources);
        Assert.Equal(AppsViewModel.AllSources, Apps.SelectedSource);
    }

    [Fact]
    public async Task Select_all_takes_what_is_showing_and_select_none_clears_everything()
    {
        Pc.AddApp("Paper Boat", Color.Teal);
        Pc.AddApp("Quiet Studio", Color.Orange);
        await ScanAsync();

        Apps.SearchText = "paper";
        Apps.SelectAllCommand.Execute(null);

        Assert.Equal(1, Apps.IncludedCount);
        Assert.Equal("1 selected", Apps.IncludedLabel);
        Assert.Equal("Create 1 Folder", Apps.CreateLabel);

        Apps.SearchText = string.Empty;
        Apps.SelectAllCommand.Execute(null);

        Assert.Equal(2, Apps.IncludedCount);
        Assert.Equal("Create 2 Folders", Apps.CreateLabel);

        Apps.SelectNoneCommand.Execute(null);

        Assert.Equal(0, Apps.IncludedCount);
    }

    [Fact]
    public async Task Selecting_survives_a_search()
    {
        var tile = await ScanOneAsync();
        Apps.SelectedTile = tile;

        Apps.SearchText = "paper";
        Assert.Same(tile, Apps.SelectedTile);

        Apps.SearchText = "something else";
        Assert.Null(Apps.SelectedTile);
    }

    [Fact]
    public async Task A_low_resolution_icon_shows_a_warning()
    {
        var tile = await ScanOneAsync(iconSize: 32);

        Assert.Contains("small", tile.QualityNote);
        Assert.Equal(StatusKind.Warning, tile.Kind);
        Assert.Equal(tile.QualityNote, tile.Status);
    }

    [Fact]
    public async Task A_sharp_icon_shows_no_warning()
    {
        var tile = await ScanOneAsync();

        Assert.Empty(tile.QualityNote);
        Assert.Null(tile.Status);
    }

    [Fact]
    public async Task An_app_with_no_readable_icon_uses_a_symbol_and_says_so()
    {
        Pc.AddLink(Pc.UserMenu, "No Icon.lnk", Pc.AddProgram("noicon.exe"));
        await ScanAsync();

        var tile = Assert.Single(Apps.Tiles);

        Assert.True(tile.IsReady);
        Assert.False(tile.HasLogo);
        Assert.Contains("No icon", tile.QualityNote);
        Assert.NotNull(tile.Preview);
    }

    [Fact]
    public async Task The_automatic_colors_come_from_the_icon()
    {
        var tile = await ScanOneAsync(color: Color.FromArgb(200, 30, 40));

        var (hue, _, _) = ColorMath.ToHsl(tile.ToRequest().Spec.Fill.Start);

        Assert.True(ColorMath.HueDistance(hue, 356) < 15);
    }

    [Fact]
    public async Task A_logo_lost_on_its_own_color_is_explained()
    {
        var tile = await ScanOneAsync(color: Color.FromArgb(40, 90, 200));

        Assert.Contains("getting lost", tile.ContrastNote);
    }

    [Fact]
    public async Task Tweaks_change_the_look_and_redraw_the_preview()
    {
        var tile = await ScanOneAsync();
        var before = tile.Preview;

        tile.StartColor = "#112233";
        tile.EndColor = "#445566";
        tile.SelectedShape = AppTileViewModel.Shapes[3];
        tile.SelectedAnchor = AppTileViewModel.Anchors[1];
        tile.SelectedPlate = AppTileViewModel.Plates[2];
        tile.Scale = 0.75;
        await tile.PreviewTask;

        var spec = tile.ToRequest().Spec;

        Assert.Equal(IconColor.Parse("#112233"), spec.Fill.Start);
        Assert.Equal(IconColor.Parse("#445566"), spec.Fill.End);
        Assert.Equal(FolderShape.Soft3D, spec.Shape);
        Assert.Equal(LogoAnchor.BottomRight, spec.Logo?.Anchor);
        Assert.Equal(LogoPlate.Light, spec.Logo?.Plate);
        Assert.Equal(0.75f, spec.Logo?.Scale);
        Assert.NotEqual(before, tile.Preview);
    }

    [Fact]
    public async Task A_second_color_alone_does_nothing()
    {
        var tile = await ScanOneAsync();
        var automatic = tile.ToRequest().Spec.Fill;

        tile.EndColor = "#445566";

        Assert.Equal(automatic, tile.ToRequest().Spec.Fill);
    }

    [Fact]
    public async Task One_color_makes_a_solid_folder()
    {
        var tile = await ScanOneAsync();

        tile.StartColor = "#112233";

        Assert.Null(tile.ToRequest().Spec.Fill.End);
    }

    [Theory]
    [InlineData("#12")]
    [InlineData("blue")]
    [InlineData("#GGHHII")]
    public async Task A_bad_color_is_explained_and_the_automatic_one_stays(string text)
    {
        var tile = await ScanOneAsync();
        var automatic = tile.ToRequest().Spec.Fill;

        tile.StartColor = text;

        Assert.Equal("Colors look like #2F6FED.", tile.ColorMessage);
        Assert.Equal(automatic, tile.ToRequest().Spec.Fill);

        tile.StartColor = string.Empty;

        Assert.Null(tile.ColorMessage);
    }

    [Fact]
    public async Task Reset_goes_back_to_what_foldcraft_chose()
    {
        var tile = await ScanOneAsync();
        var automatic = tile.ToRequest().Spec;

        tile.StartColor = "#112233";
        tile.SelectedShape = AppTileViewModel.Shapes[1];
        tile.Scale = 0.6;
        tile.ResetCommand.Execute(null);

        Assert.Equal(automatic, tile.ToRequest().Spec);
        Assert.Empty(tile.StartColor);
    }

    [Fact]
    public async Task The_scale_stays_in_range()
    {
        var tile = await ScanOneAsync();

        tile.Scale = 99;

        Assert.Equal(tile.MaxScale, tile.Scale);
    }

    [Fact]
    public async Task Creating_makes_every_checked_item_and_reports_each_one()
    {
        Pc.AddApp("Paper Boat", Color.Teal);
        Pc.AddApp("Quiet Studio", Color.Orange);
        await ScanAsync();

        var games = Destination();

        Apps.SelectAllCommand.Execute(null);
        await Apps.CreateCommand.ExecuteAsync();

        Assert.True(Directory.Exists(Path.Combine(games, "Paper Boat")));
        Assert.True(Directory.Exists(Path.Combine(games, "Quiet Studio")));
        Assert.Equal(2, Apps.Results.Count);
        Assert.All(Apps.Results, row => Assert.Equal(StatusKind.Success, row.Kind));
        Assert.All(Apps.Tiles, tile => Assert.Equal("Created.", tile.Status));
        Assert.Contains("Created 2 folders", Apps.Summary);
        Assert.Contains("History", Apps.Summary);
    }

    [Fact]
    public async Task With_no_location_chosen_creating_asks_for_one()
    {
        var tile = await ScanOneAsync();
        var games = _app.Box.NewFolder("Games");

        tile.IsIncluded = true;
        _app.Dialogs.FolderToPick = games;
        await Apps.CreateCommand.ExecuteAsync();

        Assert.True(Directory.Exists(Path.Combine(games, "Paper Boat")));
    }

    [Fact]
    public async Task Backing_out_of_the_location_creates_nothing()
    {
        var tile = await ScanOneAsync();

        tile.IsIncluded = true;
        _app.Dialogs.FolderToPick = null;
        await Apps.CreateCommand.ExecuteAsync();

        Assert.False(File.Exists(_app.Box.Paths.JournalFile));
        Assert.False(_app.Shell.Activity.IsBusy);
    }

    [Fact]
    public async Task The_renamed_folder_uses_the_new_name()
    {
        var tile = await ScanOneAsync();
        var games = Destination();

        tile.FolderName = "My Boat";
        tile.IsIncluded = true;
        await Apps.CreateCommand.ExecuteAsync();

        Assert.True(File.Exists(Path.Combine(games, "My Boat", "My Boat.lnk")));
    }

    [Fact]
    public async Task A_tweaked_look_is_what_gets_made()
    {
        var tile = await ScanOneAsync();

        Destination();
        tile.StartColor = "#112233";
        tile.IsIncluded = true;
        await Apps.CreateCommand.ExecuteAsync();

        Assert.Contains("#112233", _app.Box.Journal.Read().Entries.Single().Change.SpecJson);
    }

    [Fact]
    public async Task A_refused_destination_shows_why_on_each_item()
    {
        var tile = await ScanOneAsync();

        _app.Dialogs.FolderToPick = _app.Box.FakeAppData;
        Apps.PickDestinationCommand.Execute(null);
        tile.IsIncluded = true;
        await Apps.CreateCommand.ExecuteAsync();

        Assert.Equal(PlainEnglish.Refusal(FolderRefusal.AppData), tile.Status);
        Assert.Equal(StatusKind.Error, tile.Kind);
        Assert.Contains("couldn't be made", Apps.Summary);
    }

    [Fact]
    public async Task Skipping_a_taken_name_is_a_choice_the_user_makes()
    {
        var tile = await ScanOneAsync();
        var games = Destination();

        Directory.CreateDirectory(Path.Combine(games, "Paper Boat"));
        Apps.SelectedClash = Apps.ClashChoices.Single(choice => choice.Choice == NameClashChoice.Skip);
        tile.IsIncluded = true;
        await Apps.CreateCommand.ExecuteAsync();

        Assert.Contains("already has this name", tile.Status);
        Assert.Equal(StatusKind.Warning, tile.Kind);
        Assert.False(File.Exists(_app.Box.Paths.JournalFile));
    }

    [Fact]
    public async Task By_default_a_taken_name_gets_a_number()
    {
        var tile = await ScanOneAsync();
        var games = Destination();

        Directory.CreateDirectory(Path.Combine(games, "Paper Boat"));
        tile.IsIncluded = true;
        await Apps.CreateCommand.ExecuteAsync();

        Assert.True(Directory.Exists(Path.Combine(games, "Paper Boat (2)")));
    }

    [Fact]
    public void Turning_on_install_folders_asks_first_and_saying_no_leaves_it_off()
    {
        _app.Dialogs.ConfirmAnswer = false;

        Apps.ThemeInstallFolders = true;

        Assert.False(Apps.ThemeInstallFolders);
        Assert.Contains("Change install folders too?", _app.Dialogs.Confirmations);

        _app.Dialogs.ConfirmAnswer = true;
        Apps.ThemeInstallFolders = true;

        Assert.True(Apps.ThemeInstallFolders);
    }

    [Fact]
    public void Turning_install_folders_off_does_not_ask()
    {
        Apps.ThemeInstallFolders = true;
        _app.Dialogs.Confirmations.Clear();

        Apps.ThemeInstallFolders = false;

        Assert.False(Apps.ThemeInstallFolders);
        Assert.Empty(_app.Dialogs.Confirmations);
    }

    [Fact]
    public async Task After_an_undo_a_tile_stops_saying_it_was_created()
    {
        var tile = await ScanOneAsync();

        Destination();
        tile.IsIncluded = true;
        await Apps.CreateCommand.ExecuteAsync();
        await _app.History.RefreshAsync();

        Assert.Equal("Created.", tile.Status);

        await _app.History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Null(tile.Status);
        Assert.Empty(Apps.Results);
        Assert.Null(Apps.Summary);
    }

    [Fact]
    public async Task Undoing_keeps_the_low_quality_warning()
    {
        var tile = await ScanOneAsync(iconSize: 32);

        Destination();
        tile.IsIncluded = true;
        await Apps.CreateCommand.ExecuteAsync();
        await _app.History.RefreshAsync();
        await _app.History.Entries[0].UndoCommand.ExecuteAsync();

        Assert.Equal(tile.QualityNote, tile.Status);
        Assert.Equal(StatusKind.Warning, tile.Kind);
    }

    [Fact]
    public async Task Restoring_everything_warns_about_folders_foldcraft_made()
    {
        var tile = await ScanOneAsync();

        Destination();
        tile.IsIncluded = true;
        await Apps.CreateCommand.ExecuteAsync();
        await _app.History.RefreshAsync();

        _app.Dialogs.ConfirmAnswer = false;
        await _app.History.RestoreAllCommand.ExecuteAsync();

        Assert.Contains(PlainEnglish.CreatedFoldersGo, _app.Dialogs.ConfirmationMessages[^1]);
    }

    [Fact]
    public async Task Restoring_everything_says_nothing_about_made_folders_when_there_are_none()
    {
        await _app.ApplyToNewFoldersAsync("Ordinary");

        _app.Dialogs.ConfirmAnswer = false;
        await _app.History.RestoreAllCommand.ExecuteAsync();

        Assert.DoesNotContain(PlainEnglish.CreatedFoldersGo, _app.Dialogs.ConfirmationMessages[^1]);
    }

    [Fact]
    public void An_unfinished_folder_never_claims_nothing_was_left()
    {
        var result = new AppFolderResult { AppId = "a", AppName = "A", Outcome = AppFolderOutcome.LeftUnfinished, Message = "desktop.ini failed" };

        var text = PlainEnglish.AppFolderResult(result);

        Assert.Contains("History", text);
        Assert.DoesNotContain("Nothing was left", text);
        Assert.Contains("couldn't be made", PlainEnglish.AppFolderSummary([result]));
    }

    [Fact]
    public void Install_folders_are_left_alone_by_default_and_the_reason_is_explained()
    {
        Assert.False(Apps.ThemeInstallFolders);
        Assert.Contains("Off unless you turn it on", Apps.InstallFolderExplanation);
        Assert.Contains("Program Files and AppData stay blocked", Apps.InstallFolderExplanation);
    }

    [Fact]
    public async Task Turning_on_install_folders_changes_them_and_says_how_each_went()
    {
        Pc.InstallSteam(_app.Registry);
        Pc.AddSteamGame(Pc.Steam, "100", "Lantern Keep");
        await ScanAsync();

        var install = Path.Combine(Pc.Steam, "steamapps", "common", "Game Folder");
        var tile = Apps.Tiles.Single(item => item.Name == "Lantern Keep");

        Destination();
        Apps.ThemeInstallFolders = true;
        tile.IsIncluded = true;
        await Apps.CreateCommand.ExecuteAsync();

        Assert.True(File.Exists(Sandbox.IniPath(install)));
        Assert.Contains("Install folder", tile.Status);
    }

    [Fact]
    public async Task A_folder_that_exists_can_take_the_look()
    {
        var tile = await ScanOneAsync();
        var library = _app.Box.NewFolder("Library");

        Apps.SelectedTile = tile;
        _app.Dialogs.FolderToPick = library;
        await Apps.ThemeExistingCommand.ExecuteAsync();

        Assert.True(File.Exists(Sandbox.IniPath(library)));
        Assert.Equal("Done.", tile.Status);
    }

    [Fact]
    public async Task Using_a_look_on_a_protected_folder_is_refused_with_the_reason()
    {
        var tile = await ScanOneAsync();

        Apps.SelectedTile = tile;
        _app.Dialogs.FolderToPick = _app.Box.FakeAppData;
        await Apps.ThemeExistingCommand.ExecuteAsync();

        Assert.Equal(PlainEnglish.Refusal(FolderRefusal.AppData), tile.Status);
    }

    [Fact]
    public async Task The_hint_follows_the_selected_item()
    {
        Assert.Contains("Press Scan", Apps.ApplyHint);

        var tile = await ScanOneAsync();

        Assert.Contains("Click an app", Apps.ApplyHint);

        Apps.SelectedTile = tile;

        Assert.Contains(tile.Name, Apps.ApplyHint);
    }

    [Fact]
    public async Task Every_folder_inside_one_can_take_the_look_in_one_go()
    {
        var tile = await ScanOneAsync();
        var parent = _app.Box.NewFolder("Versions");
        var first = Directory.CreateDirectory(Path.Combine(parent, "1.0")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(parent, "2.0")).FullName;

        Apps.SelectedTile = tile;
        _app.Dialogs.FolderToPick = parent;
        await Apps.ThemeFoldersInsideCommand.ExecuteAsync();

        Assert.True(File.Exists(Sandbox.IniPath(first)));
        Assert.True(File.Exists(Sandbox.IniPath(second)));
        Assert.False(File.Exists(Sandbox.IniPath(parent)));
        Assert.Equal(["1.0", "2.0"], Apps.Results.Select(row => row.Name));
        Assert.Contains("Change 2 folders?", _app.Dialogs.Confirmations);
        Assert.Contains("Gave 2 folders this look", Apps.Summary);
    }

    [Fact]
    public async Task Folders_that_already_have_the_look_are_not_claimed_as_changed()
    {
        var tile = await ScanOneAsync();
        var parent = _app.Box.NewFolder("Versions");
        Directory.CreateDirectory(Path.Combine(parent, "1.0"));

        Apps.SelectedTile = tile;
        _app.Dialogs.FolderToPick = parent;
        await Apps.ThemeFoldersInsideCommand.ExecuteAsync();
        await Apps.ThemeFoldersInsideCommand.ExecuteAsync();

        Assert.Equal("1 folder already had it.", Apps.Summary);
        Assert.Equal("Already has this look.", Apps.Results[0].Message);
    }

    [Fact]
    public void A_folder_left_half_finished_says_where_to_put_it_right()
    {
        var result = new AppFolderResult
        {
            AppId = "test", AppName = "Paper Boat", Outcome = AppFolderOutcome.LeftUnfinished, FolderPath = @"D:\Apps\1.0",
        };

        Assert.Contains("History", PlainEnglish.AppFolderResult(result));
        Assert.Contains("1 couldn't be changed", PlainEnglish.ThemedFoldersSummary([result]));
    }

    [Fact]
    public void A_failure_on_an_existing_folder_does_not_say_it_was_making_one()
    {
        var result = new AppFolderResult
        {
            AppId = "test", AppName = "Paper Boat", Outcome = AppFolderOutcome.Failed, FolderPath = @"D:\Apps\1.0",
            Message = @"Access to the path 'D:\Apps\1.0' is denied.",
        };

        Assert.DoesNotContain("make this folder", PlainEnglish.AppFolderResult(result));
    }

    [Fact]
    public void A_folder_Windows_locked_says_how_to_get_past_it()
    {
        var failed = new FolderApplyResult
        {
            RequestedPath = @"D:\Apps\Tool", Outcome = ApplyOutcome.Failed, Message = @"Access to the path 'D:\Apps\Tool\x.tmp' is denied. The folder was put back as it was.",
        };
        var restore = new FolderRestoreResult(@"D:\Apps\Tool", RestoreOutcome.Failed, "Access is denied.");

        Assert.Equal(PlainEnglish.NoPermission, PlainEnglish.ApplyResult(failed));
        Assert.Equal(PlainEnglish.NoPermission, PlainEnglish.RestoreResult(restore));
    }

    [Fact]
    public void A_folder_put_back_with_a_note_is_not_called_a_failure()
    {
        var restored = new FolderRestoreResult(@"D:\Apps\Tool", RestoreOutcome.Restored, "Access is denied.");

        Assert.NotEqual(PlainEnglish.NoPermission, PlainEnglish.RestoreResult(restored));
    }

    [Fact]
    public async Task Saying_no_to_the_question_changes_no_folders()
    {
        var tile = await ScanOneAsync();
        var parent = _app.Box.NewFolder("Versions");
        var first = Directory.CreateDirectory(Path.Combine(parent, "1.0")).FullName;

        Apps.SelectedTile = tile;
        _app.Dialogs.FolderToPick = parent;
        _app.Dialogs.ConfirmAnswer = false;
        await Apps.ThemeFoldersInsideCommand.ExecuteAsync();

        Assert.False(File.Exists(Sandbox.IniPath(first)));
    }

    [Fact]
    public async Task A_folder_with_nothing_inside_says_so_without_asking()
    {
        var tile = await ScanOneAsync();

        Apps.SelectedTile = tile;
        _app.Dialogs.FolderToPick = _app.Box.NewFolder("Empty");
        await Apps.ThemeFoldersInsideCommand.ExecuteAsync();

        Assert.Empty(_app.Dialogs.Confirmations);
        Assert.Contains("no folders inside it", Apps.Summary);
    }

    [Fact]
    public async Task Created_folders_appear_in_history_as_created_and_undo_says_what_stayed()
    {
        Pc.AddApp("Paper Boat", Color.Teal);
        Pc.AddApp("Quiet Studio", Color.Orange);
        await ScanAsync();

        var games = Destination();

        Apps.SelectAllCommand.Execute(null);
        await Apps.CreateCommand.ExecuteAsync();
        await _app.History.RefreshAsync();

        var entry = Assert.Single(_app.History.Entries);

        Assert.Equal("Created 2 folders", entry.Title);

        var folder = Path.Combine(games, "Paper Boat");
        File.SetAttributes(folder, FileAttributes.Directory);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");

        await entry.UndoCommand.ExecuteAsync();

        Assert.True(Directory.Exists(folder));
        Assert.False(Directory.Exists(Path.Combine(games, "Quiet Studio")));
        Assert.Contains(_app.History.Results, row => row.Message.Contains("stayed", StringComparison.Ordinal));
        Assert.Contains(_app.History.Results, row => row.Message.Contains("Removed the folder", StringComparison.Ordinal));
        Assert.Contains("stayed", _app.Shell.Activity.Message);
    }

    [Fact]
    public async Task Canceling_a_scan_keeps_what_is_ready_and_says_so()
    {
        AppHarness? harness = null;
        var reads = 0;

        var reader = new ScriptedIconReader((_, _) =>
        {
            if (++reads == 2)
            {
                harness!.Shell.Activity.Cancel();
            }

            return null;
        });

        using var run = new AppHarness(iconReader: reader);

        harness = run;
        run.Pc.AddApp("One", Color.Teal);
        run.Pc.AddApp("Two", Color.Orange);
        run.Pc.AddApp("Three", Color.Purple);

        await run.Shell.Apps.ScanCommand.ExecuteAsync();

        Assert.Equal(3, run.Shell.Apps.Tiles.Count);
        Assert.Contains(run.Shell.Apps.Tiles, tile => tile.IsReady);
        Assert.Contains(run.Shell.Apps.Tiles, tile => !tile.IsReady);
        Assert.Contains("stopped the scan", run.Shell.Apps.Summary);
        Assert.False(run.Shell.Activity.IsBusy);

        var notRead = Assert.Single(run.Shell.Apps.Tiles, tile => !tile.IsReady);

        Assert.Equal("Not read yet. Scan again to use this.", notRead.Status);
        Assert.Equal(StatusKind.Warning, notRead.Kind);
        Assert.Contains("before 1 icon was read", run.Shell.Apps.Summary);

        // Putting folders back says nothing new about a picture that was never read.
        run.Shell.Apps.OnFoldersRestored();

        Assert.Equal("Not read yet. Scan again to use this.", notRead.Status);
    }

    [Fact]
    public async Task One_icon_that_cannot_be_read_does_not_end_the_scan()
    {
        using var harness = new AppHarness(iconReader: new ScriptedIconReader((candidates, _) =>
            candidates.Any(icon => icon.Path.Contains("Bad", StringComparison.Ordinal))
                ? throw new InvalidOperationException("An odd icon.")
                : null));

        harness.Pc.AddApp("Bad App", Color.Teal);
        harness.Pc.AddApp("Good App", Color.Orange);

        await harness.Shell.Apps.ScanCommand.ExecuteAsync();

        var bad = harness.Shell.Apps.Tiles.Single(tile => tile.Name == "Bad App");
        var good = harness.Shell.Apps.Tiles.Single(tile => tile.Name == "Good App");

        Assert.False(bad.IsReady);
        Assert.Equal(StatusKind.Error, bad.Kind);
        Assert.True(good.IsReady);

        // Nobody stopped this scan, so nothing should say somebody did.
        Assert.DoesNotContain("stopped", harness.Shell.Apps.Summary);
        Assert.Contains("1 icon couldn't be read", harness.Shell.Apps.Summary);

        // Something that can't be used can't be checked, either.
        bad.IsIncluded = true;

        Assert.False(bad.IsIncluded);
    }

    [Fact]
    public async Task A_new_scan_starts_clean()
    {
        var tile = await ScanOneAsync();

        tile.IsIncluded = true;
        await ScanAsync();

        Assert.Equal(0, Apps.IncludedCount);
        Assert.NotSame(tile, Apps.Tiles[0]);
    }

    [Fact]
    public async Task Nothing_can_start_while_a_scan_runs()
    {
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        using var harness = new AppHarness(iconReader: new ScriptedIconReader((_, _) =>
        {
            entered.TrySetResult();
            release.Task.Wait();
            return null;
        }));

        harness.Pc.AddApp("One", Color.Teal);

        var scan = harness.Shell.Apps.ScanCommand.ExecuteAsync();
        await entered.Task;

        Assert.False(harness.Shell.Apps.ScanCommand.CanExecute(null));
        Assert.False(harness.Shell.Apps.CreateCommand.CanExecute(null));
        Assert.True(harness.Shell.Activity.IsBusy);
        Assert.True(harness.Shell.Activity.CanCancel);

        release.SetResult();
        await scan;

        Assert.True(harness.Shell.Apps.ScanCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_history_screen_catches_up_after_creating()
    {
        var tile = await ScanOneAsync();

        Destination();
        tile.IsIncluded = true;
        await Apps.CreateCommand.ExecuteAsync();

        for (var i = 0; i < 200 && _app.History.Entries.Count == 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.Single(_app.History.Entries);
    }

    [Fact]
    public void The_notice_appears_on_the_screen_and_in_about()
    {
        Assert.Contains("not affiliated with these companies", Apps.Disclaimer);
        Assert.Contains("never uploaded or redistributed", Apps.Disclaimer);
        Assert.Equal(Apps.Disclaimer, _app.Shell.About.AppsNotice);
    }

    [Fact]
    public void Repair_and_restore_wording_covers_what_was_added()
    {
        var folderKept = new FolderRestoreResult(@"C:\Games\Boat", RestoreOutcome.Restored, "The look was removed.") { FolderKept = true };

        Assert.Equal("The look was removed.", PlainEnglish.RestoreResult(folderKept));
        Assert.Contains("stayed", PlainEnglish.RestoreSummary([folderKept]));
        Assert.Equal("Put back.", PlainEnglish.RestoreResult(new FolderRestoreResult(@"C:\A", RestoreOutcome.Restored)));
    }

    [Fact]
    public void A_folder_too_deep_for_windows_to_show_says_so()
    {
        var result = new AppFolderResult
        {
            AppId = "a",
            AppName = "A",
            Outcome = AppFolderOutcome.Created,
            FolderPath = @"C:\" + new string('x', 300),
        };

        Assert.Contains("can't show", PlainEnglish.AppFolderResult(result));
    }

    [Theory]
    [InlineData(AppFolderOutcome.Created)]
    [InlineData(AppFolderOutcome.Themed)]
    [InlineData(AppFolderOutcome.NameTaken)]
    [InlineData(AppFolderOutcome.Refused)]
    [InlineData(AppFolderOutcome.Failed)]
    [InlineData(AppFolderOutcome.Canceled)]
    public void Every_outcome_has_friendly_words(AppFolderOutcome outcome)
    {
        var result = new AppFolderResult { AppId = "a", AppName = "A", Outcome = outcome, Message = "Access to the path was denied" };

        var text = PlainEnglish.AppFolderResult(result);

        Assert.NotEmpty(text);
        Assert.DoesNotContain("Access to the path", text);
    }

    [Theory]
    [InlineData(AppSource.Steam, "Steam")]
    [InlineData(AppSource.Roblox, "Roblox")]
    [InlineData(AppSource.EpicGames, "Epic Games")]
    [InlineData(AppSource.StartMenu, "Start Menu")]
    public void Sources_have_friendly_names(AppSource source, string expected)
    {
        Assert.Equal(expected, PlainEnglish.AppSourceName(source));
    }

    [Fact]
    public void A_provider_that_failed_is_mentioned_without_stopping_the_summary()
    {
        var report = new AppScanReport
        {
            Apps = [SampleApps.Program()],
            Problems = [new AppProviderProblem("Steam", "broken")],
        };

        var summary = PlainEnglish.AppScanSummary(report, 1);

        Assert.Contains("Found 1 app or game", summary);
        Assert.Contains("Steam couldn't be read", summary);
    }
}
