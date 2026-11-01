using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Foldcraft.App.Services;
using Foldcraft.App.ViewModels;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Rules;
using Foldcraft.Core.Settings;
using Foldcraft.Core.Shell;
using Foldcraft.Core.Themes;

namespace Foldcraft.Tests;

public sealed class DesignerAdditionsTests : IDisposable
{
    private readonly AppHarness _app = new();

    public void Dispose() => _app.Dispose();

    private DesignerViewModel Designer => _app.Designer;

    [Fact]
    public void Search_narrows_the_symbols_and_hides_the_plain_folder()
    {
        Designer.GlyphSearch = "pizza";

        Assert.Equal("pizza", Assert.Single(Designer.VisibleGlyphs).GlyphId);
        Assert.False(Designer.NoGlyphsFound);
    }

    [Fact]
    public void A_category_narrows_the_symbols()
    {
        Designer.SelectedCategory = Designer.GlyphCategories.Single(c => c.Category == GlyphCategory.Food);

        Assert.All(Designer.VisibleGlyphs, tile => Assert.Equal(GlyphCategory.Food, GlyphCatalog.Find(tile.GlyphId!)!.Category));
    }

    [Fact]
    public void Clearing_the_filters_shows_everything_again()
    {
        Designer.GlyphSearch = "pizza";
        Designer.GlyphSearch = string.Empty;

        Assert.Equal(Designer.Glyphs, Designer.VisibleGlyphs);
    }

    [Fact]
    public void A_search_with_no_results_says_so()
    {
        Designer.GlyphSearch = "zzzz";

        Assert.True(Designer.NoGlyphsFound);
    }

    [Fact]
    public void A_search_does_not_change_the_design()
    {
        Designer.SelectedGlyph = Designer.Glyphs.Single(t => t.GlyphId == "star");

        Designer.GlyphSearch = "pizza";

        Assert.Equal("star", Designer.Spec.GlyphId);
    }

    [Fact]
    public void Choosing_a_style_goes_into_the_design()
    {
        Designer.SelectedShape = Designer.Shapes.Single(s => s.Shape == FolderShape.Outline);

        Assert.Equal(FolderShape.Outline, Designer.Spec.Shape);
        Assert.Equal(4, Designer.Shapes.Count);
    }

    [Fact]
    public void Choosing_a_badge_goes_into_the_design()
    {
        Assert.False(Designer.HasBadge);

        Designer.SelectedBadge = Designer.Badges.Single(b => b.BadgeId == "done");
        Designer.SelectedCorner = Designer.Corners.Single(c => c.Corner == BadgeCorner.TopLeft);
        Designer.BadgeHexText = "#112233";

        Assert.True(Designer.HasBadge);
        Assert.Equal(new IconBadge("done", BadgeCorner.TopLeft, IconColor.Parse("#112233")), Designer.Spec.Badge);
    }

    [Fact]
    public void A_bad_badge_color_stops_apply_until_it_is_fixed()
    {
        Designer.SelectedBadge = Designer.Badges.Single(b => b.BadgeId == "done");

        Designer.BadgeHexText = "red";
        Assert.False(Designer.CanApply);
        Assert.NotNull(Designer.BadgeHexMessage);

        Designer.BadgeHexText = string.Empty;
        Assert.True(Designer.CanApply);
        Assert.Null(Designer.Spec.Badge!.Color);
    }

    [Fact]
    public void An_emoji_replaces_the_symbol()
    {
        Designer.SelectedGlyph = Designer.Glyphs.Single(t => t.GlyphId == "star");

        Designer.EmojiText = "\U0001F680";

        Assert.Equal("\U0001F680", Designer.Spec.Emoji);
        Assert.Null(Designer.Spec.GlyphId);
    }

    [Fact]
    public void Choosing_a_symbol_clears_the_emoji()
    {
        Designer.EmojiText = "\U0001F680";

        Designer.SelectedGlyph = Designer.Glyphs.Single(t => t.GlyphId == "star");

        Assert.Null(Designer.Spec.Emoji);
        Assert.Equal(string.Empty, Designer.EmojiText);
    }

    [Fact]
    public void Clearing_the_emoji_goes_back_to_a_plain_folder()
    {
        Designer.EmojiText = "\U0001F680";
        Designer.EmojiText = string.Empty;

        Assert.Null(Designer.Spec.Emoji);
        Assert.Equal(Designer.Glyphs[0], Designer.SelectedGlyph);
        Assert.True(Designer.CanApply);
    }

    [Fact]
    public void Text_that_is_not_an_emoji_stops_apply_and_explains()
    {
        Designer.EmojiText = "hello";

        Assert.False(Designer.CanApply);
        Assert.NotNull(Designer.EmojiMessage);
        Assert.Null(Designer.Spec.Emoji);
    }

    [Fact]
    public void Loading_a_look_shows_all_of_it()
    {
        var look = new FolderIconSpec
        {
            Fill = FolderFill.Gradient(IconColor.Parse("#112233"), IconColor.Parse("#445566"), 135f),
            Shape = FolderShape.Flat,
            GlyphId = "music",
            Badge = new IconBadge("waiting", BadgeCorner.TopRight, IconColor.Parse("#778899")),
        };

        Designer.LoadLook(look);

        Assert.Equal(look, Designer.Spec);
        Assert.True(Designer.IsGradient);
        Assert.Equal("#112233", Designer.HexText);
        Assert.Equal("#778899", Designer.BadgeHexText);
    }

    [Fact]
    public void Loading_a_plain_look_clears_what_was_there()
    {
        Designer.SelectedBadge = Designer.Badges[1];
        Designer.EmojiText = "\U0001F680";

        var look = new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#112233")) };

        Designer.LoadLook(look);

        Assert.Equal(look, Designer.Spec);
    }

    [Fact]
    public void A_loaded_emoji_look_comes_through()
    {
        var look = new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#112233")), Emoji = "\U0001F680" };

        Designer.LoadLook(look);

        Assert.Equal(look, Designer.Spec);
        Assert.Equal("\U0001F680", Designer.EmojiText);
    }
}

public sealed class ThemesViewModelTests : IDisposable
{
    private readonly AppHarness _app = new();

    public void Dispose() => _app.Dispose();

    private ThemesViewModel Themes => _app.Themes;

    private async Task<ThemeTileViewModel> Pick(int index = 0)
    {
        await Themes.RefreshAsync();
        Themes.SelectedTile = Themes.Themes[index];
        return Themes.SelectedTile;
    }

    [Fact]
    public async Task Built_in_themes_show_with_their_pictures()
    {
        await Themes.RefreshAsync();

        Assert.Equal(24, Themes.Themes.Count);
        Assert.All(Themes.Themes, tile => Assert.NotNull(tile.Image));
    }

    [Fact]
    public async Task The_mood_filter_narrows_the_list()
    {
        await Themes.RefreshAsync();

        Themes.SelectedMood = "Calm";

        Assert.Equal(3, Themes.Themes.Count);
    }

    [Fact]
    public async Task Opening_a_theme_in_the_designer_goes_there()
    {
        var tile = await Pick(3);
        _app.Shell.SelectedTab = ShellViewModel.ThemesTab;

        Themes.UseInDesignCommand.Execute(null);

        Assert.Equal(tile.Theme.Spec, _app.Designer.Spec);
        Assert.Equal(ShellViewModel.CustomizeTab, _app.Shell.SelectedTab);
    }

    [Fact]
    public async Task A_theme_applies_to_many_folders_as_one_undoable_step()
    {
        var tile = await Pick();
        var folders = new[] { _app.Box.NewFolder("a"), _app.Box.NewFolder("b"), _app.Box.NewFolder("c") };
        _app.Dialogs.FoldersToPick = folders;

        await Themes.ApplyToFoldersCommand.ExecuteAsync();

        Assert.All(folders, folder => Assert.True(File.Exists(Sandbox.IniPath(folder))));
        Assert.Single(_app.Services.Journal.Read().Entries.Select(e => e.Change.BatchId).Distinct());
        Assert.StartsWith("Changed 3 folders", _app.Shell.Activity.Message);

        _app.Services.Restorer.UndoLast();
        Assert.All(folders, folder => Assert.False(File.Exists(Sandbox.IniPath(folder))));
    }

    [Fact]
    public async Task Choosing_no_folders_does_nothing()
    {
        await Pick();

        await Themes.ApplyToFoldersCommand.ExecuteAsync();

        Assert.Empty(_app.Services.Journal.Read().Entries);
    }

    [Fact]
    public async Task Saving_the_current_look_adds_a_theme()
    {
        await Themes.RefreshAsync();
        _app.Dialogs.TextAnswer = "Mine";

        Themes.SaveCurrentCommand.Execute(null);

        for (var i = 0; i < 200 && Themes.Themes.Count == 24; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal("Mine", Themes.Themes[^1].Name);
    }

    [Fact]
    public async Task Canceling_the_name_saves_nothing()
    {
        await Themes.RefreshAsync();

        Themes.SaveCurrentCommand.Execute(null);

        Assert.False(File.Exists(_app.Box.Paths.ThemesFile));
    }

    [Fact]
    public async Task A_bad_name_is_explained()
    {
        await Themes.RefreshAsync();
        _app.Dialogs.TextAnswer = "   ";

        Themes.SaveCurrentCommand.Execute(null);

        Assert.Single(_app.Dialogs.Errors);
    }

    [Fact]
    public async Task A_built_in_theme_cannot_be_deleted_from_here()
    {
        await Pick();

        Assert.False(Themes.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_saved_theme_can_be_deleted()
    {
        _app.Services.Themes.Save("Mine", Sandbox.BlueStar);
        await Pick(24);

        await Themes.DeleteCommand.ExecuteAsync();

        Assert.Equal(24, Themes.Themes.Count);
    }

    [Fact]
    public async Task A_good_file_imports()
    {
        var path = Path.Combine(_app.Box.Root, "in.json");
        File.WriteAllText(path, """{ "format": "foldcraft-themes", "version": 1, "themes": [ { "name": "Ocean", "fill": { "start": "#0EA5E9" } } ] }""");
        _app.Dialogs.ThemeFileToPick = path;

        await Themes.ImportCommand.ExecuteAsync();

        Assert.Equal("Added 1 theme.", Themes.Notice);
        Assert.Contains(Themes.Themes, tile => tile.Name == "Ocean");
    }

    [Fact]
    public async Task A_bad_file_is_explained_and_changes_nothing()
    {
        var path = Path.Combine(_app.Box.Root, "in.json");
        File.WriteAllText(path, "not a theme file");
        _app.Dialogs.ThemeFileToPick = path;

        await Themes.ImportCommand.ExecuteAsync();

        Assert.StartsWith("Foldcraft didn't import this file.", Assert.Single(_app.Dialogs.Errors));
        Assert.False(File.Exists(_app.Box.Paths.ThemesFile));
    }

    [Fact]
    public async Task Export_writes_only_saved_themes()
    {
        _app.Services.Themes.Save("Mine", Sandbox.BlueStar);
        await Themes.RefreshAsync();
        var path = Path.Combine(_app.Box.Root, "out.json");
        _app.Dialogs.ThemeSaveLocation = path;

        Themes.ExportCommand.Execute(null);

        var theme = Assert.Single(ThemeFile.Read(File.ReadAllBytes(path)));
        Assert.Equal("Mine", theme.Name);
    }

    [Fact]
    public async Task Export_is_off_with_nothing_saved()
    {
        await Themes.RefreshAsync();

        Assert.False(Themes.ExportCommand.CanExecute(null));
    }

    /// <summary>
    /// A real network path can't be used here, because a test must not reach for a server. This
    /// spelling of a real picture isn't plainly local either, and without the gate it gets read.
    /// </summary>
    [Fact]
    public async Task Drawing_a_tile_never_opens_a_picture_that_is_not_plainly_local()
    {
        var picture = Path.Combine(_app.Box.Root, "picture.png");
        File.WriteAllBytes(picture, DrawnArt.Png(32, System.Drawing.Color.Teal));

        _app.Services.Themes.Save("Odd", new FolderIconSpec { Fill = Sandbox.BlueStar.Fill, CustomImagePath = picture + "::$DATA" });
        _app.Services.Themes.Save("Plain", new FolderIconSpec { Fill = Sandbox.BlueStar.Fill, CustomImagePath = picture });

        await Themes.RefreshAsync();

        Assert.Null(Themes.Themes.Single(tile => tile.Name == "Odd").Image);
        Assert.NotNull(Themes.Themes.Single(tile => tile.Name == "Plain").Image);
    }

    [Fact]
    public async Task A_new_theme_reaches_the_rules_screen()
    {
        _app.Services.Themes.Save("Mine", Sandbox.BlueStar);

        await Themes.RefreshAsync();

        Assert.Contains(_app.Rules.Themes, theme => theme.Name == "Mine");
    }
}

public sealed class RulesViewModelTests : IDisposable
{
    private readonly AppHarness _app = new();

    public void Dispose() => _app.Dispose();

    private RulesViewModel Rules => _app.Rules;

    private string Scope => Path.Combine(_app.Box.Root, "folders");

    private async Task<string> ReadyWithFolder(string name = "photos")
    {
        await _app.Shell.InitializeAsync();

        var folder = _app.Box.NewFolder(name);

        Rules.PatternText = "photos";
        Rules.AddRuleCommand.Execute(null);

        _app.Dialogs.FolderToPick = Scope;
        Rules.PickScopeCommand.Execute(null);

        return folder;
    }

    [Fact]
    public async Task A_rule_needs_something_to_look_for()
    {
        await _app.Shell.InitializeAsync();

        Rules.PatternText = "   ";
        Rules.AddRuleCommand.Execute(null);

        Assert.Empty(Rules.Rules);
        Assert.NotNull(Rules.PatternMessage);
    }

    [Fact]
    public async Task A_file_type_rule_needs_a_kind_it_knows()
    {
        await _app.Shell.InitializeAsync();

        Rules.SelectedCondition = Rules.Conditions.Single(c => c.Condition == RuleCondition.MostlyFileType);
        Rules.PatternText = "photos of cats";
        Rules.AddRuleCommand.Execute(null);

        Assert.Empty(Rules.Rules);
    }

    [Fact]
    public async Task Rules_are_saved_and_come_back()
    {
        await ReadyWithFolder();

        using var again = new AppHarnessOver(_app);

        again.Rules.Load();

        Assert.Single(again.Rules.Rules);
    }

    [Fact]
    public async Task A_rule_can_be_removed()
    {
        await ReadyWithFolder();

        Rules.Rules[0].RemoveCommand.Execute(null);

        Assert.Empty(Rules.Rules);
        Assert.Empty(_app.Services.Rules.Load());
    }

    [Fact]
    public async Task Rules_do_not_run_when_the_app_starts_or_a_rule_is_added()
    {
        var folder = await ReadyWithFolder();

        await _app.Shell.InitializeAsync();

        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.False(File.Exists(_app.Box.Paths.JournalFile));
        Assert.Empty(Rules.PreviewRows);
    }

    [Fact]
    public async Task Preview_needs_a_folder_and_a_rule()
    {
        await _app.Shell.InitializeAsync();

        Assert.False(Rules.PreviewCommand.CanExecute(null));
        Assert.False(Rules.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Preview_lists_what_would_change_and_changes_nothing()
    {
        var folder = await ReadyWithFolder();

        await Rules.PreviewCommand.ExecuteAsync();

        var row = Assert.Single(Rules.PreviewRows);

        Assert.Equal(folder, row.Path);
        Assert.True(row.IsIncluded);
        Assert.Contains("Will get", row.Message);
        Assert.Contains("1 folder would change", Rules.PreviewSummary);
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
    }

    [Fact]
    public async Task Apply_changes_only_the_checked_folders_and_can_be_undone()
    {
        var folder = await ReadyWithFolder();
        var other = _app.Box.NewFolder("more photos");
        await Rules.PreviewCommand.ExecuteAsync();

        Rules.PreviewRows.Single(row => row.Path == other).IsIncluded = false;
        await Rules.ApplyCommand.ExecuteAsync();

        Assert.True(File.Exists(Sandbox.IniPath(folder)));
        Assert.False(File.Exists(Sandbox.IniPath(other)));
        Assert.Empty(Rules.PreviewRows);

        _app.Services.Restorer.UndoLast();
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
    }

    [Fact]
    public async Task Skipped_folders_are_listed_with_a_reason_and_cannot_be_checked()
    {
        await ReadyWithFolder();
        Sandbox.CreateJunction(Path.Combine(Scope, "photos link"), _app.Box.NewFolder("elsewhere"));

        await Rules.PreviewCommand.ExecuteAsync();

        var skipped = Rules.PreviewRows.Single(row => row.Name == "photos link");

        Assert.False(skipped.CanInclude);
        Assert.False(skipped.IsIncluded);
        Assert.Equal(PlainEnglish.Refusal(Core.Safety.FolderRefusal.ReparsePoint), skipped.Message);
    }

    [Fact]
    public async Task What_will_change_comes_first()
    {
        await ReadyWithFolder();
        Sandbox.CreateJunction(Path.Combine(Scope, "a photos link"), _app.Box.NewFolder("elsewhere"));

        await Rules.PreviewCommand.ExecuteAsync();

        Assert.True(Rules.PreviewRows[0].CanInclude);
    }

    [Fact]
    public async Task Changing_anything_throws_the_old_preview_away()
    {
        await ReadyWithFolder();
        await Rules.PreviewCommand.ExecuteAsync();

        Rules.Depth = 3;

        Assert.Empty(Rules.PreviewRows);
        Assert.False(Rules.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_chosen_folder_that_is_off_limits_says_why()
    {
        await ReadyWithFolder();
        _app.Dialogs.FolderToPick = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Rules.PickScopeCommand.Execute(null);

        await Rules.PreviewCommand.ExecuteAsync();

        Assert.Equal(PlainEnglish.Refusal(Core.Safety.FolderRefusal.WindowsDirectory), Rules.PreviewSummary);
    }

    [Fact]
    public async Task A_rule_says_when_its_theme_has_gone()
    {
        await _app.Shell.InitializeAsync();

        var saved = _app.Services.Themes.Save("Mine", Sandbox.BlueStar);

        _app.Services.Rules.Save([new FolderRule { Id = Guid.NewGuid(), Condition = RuleCondition.NameContains, Pattern = "x", ThemeId = saved.Id }]);
        _app.Services.Themes.Delete(saved.Id);

        Rules.Load();

        Assert.Contains("no longer exists", Rules.Rules[0].Summary);
    }

    /// <summary>A second app over the same folders, as if it had been restarted.</summary>
    private sealed class AppHarnessOver(AppHarness first) : IDisposable
    {
        public RulesViewModel Rules { get; } =
            new ShellViewModel(new AppServices(first.Box.Paths, first.Box.Protected, first.Box.Shell, first.Log), first.Dialogs, TimeSpan.Zero).Rules;

        public void Dispose()
        {
        }
    }
}

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly AppHarness _app = new();

    public void Dispose() => _app.Dispose();

    private SettingsViewModel Settings => _app.Settings;

    [Fact]
    public void The_right_click_entry_is_off_by_default()
    {
        Assert.False(Settings.IsRightClickEnabled);
        Assert.Empty(_app.Registry.Keys);
    }

    [Fact]
    public async Task Starting_and_showing_settings_does_not_touch_the_registry()
    {
        await _app.Shell.InitializeAsync();

        _ = Settings.IsRightClickEnabled;
        _app.Shell.SelectedTab = ShellViewModel.SettingsTab;

        Assert.Equal(0, _app.Registry.Reads + _app.Registry.Writes);
    }

    [Fact]
    public void The_explanation_is_plain_and_honest()
    {
        Assert.Contains(@"HKCU\Software\Classes\Directory\shell", Settings.RightClickExplanation);
        Assert.Contains("one", Settings.RightClickExplanation);
        Assert.Contains("Show more options", Settings.RightClickWindows11Note);
    }

    [Fact]
    public void Turning_it_on_adds_the_entry_and_remembers_it()
    {
        Settings.ToggleRightClickCommand.Execute(null);

        Assert.True(Settings.IsRightClickEnabled);
        Assert.Equal(
            RightClickMenu.BuildCommand(AppHarness.FakeExecutable),
            _app.Registry.GetString(RightClickMenu.VerbKey + @"\command", null));
        Assert.NotNull(_app.Services.Settings.Load().RightClick);
    }

    [Fact]
    public void Turning_it_off_removes_the_entry()
    {
        Settings.ToggleRightClickCommand.Execute(null);
        Settings.ToggleRightClickCommand.Execute(null);

        Assert.False(Settings.IsRightClickEnabled);
        Assert.Empty(_app.Registry.Keys);
        Assert.Null(_app.Services.Settings.Load().RightClick);
    }

    [Fact]
    public void Another_apps_entry_under_that_name_is_explained_and_left()
    {
        _app.Registry.SetString(RightClickMenu.VerbKey + @"\command", null, "other.exe");

        Settings.ToggleRightClickCommand.Execute(null);

        Assert.False(Settings.IsRightClickEnabled);
        Assert.NotNull(Settings.RightClickMessage);
        Assert.Null(_app.Services.Settings.Load().RightClick);
        Assert.Equal("other.exe", _app.Registry.GetString(RightClickMenu.VerbKey + @"\command", null));
    }

    [Fact]
    public void A_registry_failure_is_explained_and_nothing_is_remembered()
    {
        _app.Registry.FailWrites = true;

        Settings.ToggleRightClickCommand.Execute(null);

        Assert.False(Settings.IsRightClickEnabled);
        Assert.NotNull(Settings.RightClickMessage);
    }

    [Fact]
    public void With_no_registry_the_setting_is_unavailable()
    {
        using var app = new AppHarness(hasRegistry: false);

        Assert.False(app.Settings.CanChangeRightClick);
        Assert.False(app.Settings.ToggleRightClickCommand.CanExecute(null));
    }

    [Fact]
    public void Changing_the_theme_mode_applies_and_saves_it()
    {
        Settings.SelectedThemeMode = Settings.ThemeModes.Single(m => m.Mode == AppThemeMode.Dark);

        Assert.Equal(AppThemeMode.Dark, _app.Appearance.Applied[^1]);
        Assert.Equal(AppThemeMode.Dark, _app.Services.Settings.Load().ThemeMode);
    }

    [Fact]
    public async Task The_saved_mode_is_used_at_startup()
    {
        Settings.SelectedThemeMode = Settings.ThemeModes.Single(m => m.Mode == AppThemeMode.Light);
        _app.Appearance.Applied.Clear();

        await _app.Shell.InitializeAsync();

        Assert.Equal([AppThemeMode.Light], _app.Appearance.Applied);
    }

    [Fact]
    public void Open_data_folder_opens_it()
    {
        Settings.OpenDataFolderCommand.Execute(null);

        Assert.Equal(_app.Box.Paths.DataDirectory, Assert.Single(_app.Dialogs.OpenedFolders));
    }

    [Fact]
    public async Task Remove_all_asks_first()
    {
        var folder = _app.Box.NewFolder();
        _app.Box.Customizer().Apply(folder, Sandbox.BlueStar);
        await _app.History.RefreshAsync();
        _app.Dialogs.ConfirmAnswer = false;

        await Settings.RemoveAllCommand.ExecuteAsync();

        Assert.True(File.Exists(Sandbox.IniPath(folder)));
        Assert.Empty(Settings.WhatRemains);
    }

    [Fact]
    public async Task Remove_all_restores_folders_removes_the_entry_and_says_what_remains()
    {
        var folder = _app.Box.NewFolder();
        var attributes = File.GetAttributes(folder);
        _app.Box.Customizer().Apply(folder, Sandbox.BlueStar);
        await _app.History.RefreshAsync();
        Settings.ToggleRightClickCommand.Execute(null);

        await Settings.RemoveAllCommand.ExecuteAsync();

        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.Equal(attributes, File.GetAttributes(folder));
        Assert.Empty(_app.Registry.Keys);
        Assert.False(Settings.IsRightClickEnabled);
        Assert.Empty(Directory.GetFiles(_app.Box.Paths.IconsDirectory));

        Assert.Contains(Settings.WhatRemains, line => line.Contains("right-click entry was removed", StringComparison.Ordinal));
        Assert.Contains(Settings.WhatRemains, line => line.Contains(_app.Box.Paths.DataDirectory, StringComparison.Ordinal));
        Assert.Contains(Settings.WhatRemains, line => line.Contains("you can delete it", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Remove_all_keeps_the_icons_a_stuck_folder_still_needs_and_says_so()
    {
        var folder = _app.Box.NewFolder();
        _app.Box.Customizer().Apply(folder, Sandbox.BlueStar);
        AppHarness.EditIni(folder);
        await _app.History.RefreshAsync();
        _app.Dialogs.ConflictAnswer = ConflictChoice.DecideLater;

        await Settings.RemoveAllCommand.ExecuteAsync();

        Assert.True(File.Exists(Sandbox.IniPath(folder)));
        Assert.Single(Directory.GetFiles(_app.Box.Paths.IconsDirectory));
        Assert.Contains(Settings.WhatRemains, line => line.Contains("still has its Foldcraft look", StringComparison.Ordinal));
        Assert.Contains(Settings.WhatRemains, line => line.Contains("Keep that folder", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Remove_all_with_the_entry_off_leaves_the_registry_alone()
    {
        await Settings.RemoveAllCommand.ExecuteAsync();

        Assert.Equal(0, _app.Registry.Reads + _app.Registry.Writes);
        Assert.Contains(Settings.WhatRemains, line => line.Contains("no right-click entry", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Remove_all_only_says_nothing_is_on_record_because_it_did_not_look()
    {
        await Settings.RemoveAllCommand.ExecuteAsync();

        Assert.Contains(Settings.WhatRemains, line => line.Contains("on record, so it removed nothing", StringComparison.Ordinal));
        Assert.DoesNotContain(Settings.WhatRemains, line => line.StartsWith("There was no", StringComparison.Ordinal));
    }

    private void AssertDataFolderIsNeeded()
    {
        Assert.Contains(Settings.WhatRemains, line => line.Contains("Keep that folder", StringComparison.Ordinal));
        Assert.DoesNotContain(Settings.WhatRemains, line => line.Contains("you can delete it", StringComparison.Ordinal));
    }

    private async Task<string> FolderKeptAsItIsAsync()
    {
        var folder = _app.Box.NewFolder();

        _app.Box.Customizer().Apply(folder, Sandbox.BlueStar);
        AppHarness.EditIni(folder);
        await _app.History.RefreshAsync();
        _app.Dialogs.ConflictAnswer = ConflictChoice.KeepCurrent;

        await Settings.RemoveAllCommand.ExecuteAsync();

        return folder;
    }

    [Fact]
    public async Task Remove_all_does_not_offer_deleting_icons_a_kept_folder_still_uses()
    {
        var folder = await FolderKeptAsItIsAsync();

        Assert.True(File.Exists(Sandbox.IniPath(folder)));
        AssertDataFolderIsNeeded();
        Assert.Contains(Settings.WhatRemains, line => line.Contains("1 folder you chose to keep", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_folder_kept_in_an_earlier_session_still_counts()
    {
        await FolderKeptAsItIsAsync();

        await Settings.RemoveAllCommand.ExecuteAsync();

        AssertDataFolderIsNeeded();
    }

    [Fact]
    public async Task A_kept_folder_that_no_longer_exists_needs_nothing()
    {
        var folder = await FolderKeptAsItIsAsync();

        File.SetAttributes(folder, FileAttributes.Directory);
        Directory.Delete(folder, recursive: true);

        await Settings.RemoveAllCommand.ExecuteAsync();

        Assert.Contains(Settings.WhatRemains, line => line.Contains("you can delete it", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_kept_folder_that_uses_another_icon_now_needs_nothing()
    {
        var folder = await FolderKeptAsItIsAsync();
        var ini = Sandbox.IniPath(folder);

        File.SetAttributes(ini, FileAttributes.Normal);
        File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=C:\\Elsewhere\\other.ico,0\r\n", Encoding.ASCII);

        await Settings.RemoveAllCommand.ExecuteAsync();

        Assert.Contains(Settings.WhatRemains, line => line.Contains("you can delete it", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Remove_all_does_not_offer_deleting_the_only_backup_behind_an_unreadable_record()
    {
        var folder = _app.Box.NewFolder();

        _app.Box.Customizer().Apply(folder, Sandbox.BlueStar);
        File.WriteAllText(_app.Box.Paths.JournalFile, "this record is damaged\n");
        await _app.History.RefreshAsync();

        await Settings.RemoveAllCommand.ExecuteAsync();

        AssertDataFolderIsNeeded();
        Assert.True(File.Exists(Sandbox.IniPath(folder)));
        Assert.Contains(Settings.WhatRemains, line => line.Contains("1 history record couldn't be read", StringComparison.Ordinal));
        Assert.DoesNotContain(Settings.WhatRemains, line => line.StartsWith("Nothing to put back", StringComparison.Ordinal));
    }

    private const string MovedExecutable = @"D:\Moved\Foldcraft.exe";

    /// <summary>The settings screen of a copy started from somewhere else, over the same data.</summary>
    private SettingsViewModel StartedFrom(string executable) => new ShellViewModel(
        new AppServices(_app.Box.Paths, _app.Box.Protected, _app.Box.Shell, _app.Log, registry: _app.Registry, executablePath: executable),
        _app.Dialogs,
        TimeSpan.Zero).Settings;

    [Fact]
    public void An_entry_for_a_copy_that_moved_is_pointed_out_without_reading_the_registry()
    {
        Settings.ToggleRightClickCommand.Execute(null);
        var reads = _app.Registry.Reads;

        var moved = StartedFrom(MovedExecutable);

        Assert.True(moved.RightClickIsOutdated);
        Assert.Equal(PlainEnglish.RightClickMoved, moved.RightClickMessage);
        Assert.True(moved.UpdateRightClickCommand.CanExecute(null));
        Assert.Equal(reads, _app.Registry.Reads);
    }

    [Fact]
    public void An_entry_for_this_copy_needs_no_update()
    {
        Settings.ToggleRightClickCommand.Execute(null);

        var same = StartedFrom(AppHarness.FakeExecutable.ToUpperInvariant());

        Assert.False(same.RightClickIsOutdated);
        Assert.Null(same.RightClickMessage);
        Assert.False(same.UpdateRightClickCommand.CanExecute(null));
    }

    [Fact]
    public void Updating_points_the_entry_at_this_copy()
    {
        Settings.ToggleRightClickCommand.Execute(null);
        var moved = StartedFrom(MovedExecutable);

        moved.UpdateRightClickCommand.Execute(null);

        Assert.False(moved.RightClickIsOutdated);
        Assert.Null(moved.RightClickMessage);
        Assert.True(moved.IsRightClickEnabled);
        Assert.Equal(RightClickMenu.BuildCommand(MovedExecutable), _app.Registry.GetString(RightClickMenu.VerbKey + @"\command", null));
        Assert.Equal(MovedExecutable, _app.Services.Settings.Load().RightClick?.ExePath);
    }

    [Fact]
    public void An_update_leaves_an_entry_something_else_changed()
    {
        Settings.ToggleRightClickCommand.Execute(null);
        _app.Registry.SetString(RightClickMenu.VerbKey + @"\command", null, "other.exe");
        var moved = StartedFrom(MovedExecutable);

        moved.UpdateRightClickCommand.Execute(null);

        Assert.False(moved.IsRightClickEnabled);
        Assert.NotNull(moved.RightClickMessage);
        Assert.Equal("other.exe", _app.Registry.GetString(RightClickMenu.VerbKey + @"\command", null));
    }

    [Fact]
    public async Task Removing_everything_clears_the_note_about_a_moved_copy()
    {
        Settings.ToggleRightClickCommand.Execute(null);
        var moved = StartedFrom(MovedExecutable);

        await moved.RemoveAllCommand.ExecuteAsync();

        Assert.False(moved.RightClickIsOutdated);
        Assert.Null(moved.RightClickMessage);
    }

    [Fact]
    public void Turning_it_off_takes_away_the_keys_made_for_it_even_after_a_restart()
    {
        _app.Registry.MakesParentKeys = true;
        Settings.ToggleRightClickCommand.Execute(null);

        Assert.NotEmpty(_app.Services.Settings.Load().RightClick?.AddedParents ?? []);

        StartedFrom(AppHarness.FakeExecutable).ToggleRightClickCommand.Execute(null);

        Assert.Empty(_app.Registry.Keys);
    }

    [Fact]
    public async Task Remove_all_is_blocked_while_other_work_runs()
    {
        await Task.CompletedTask;
        _app.Shell.Activity.Begin("Working");

        Assert.False(Settings.RemoveAllCommand.CanExecute(null));
        Assert.False(Settings.ToggleRightClickCommand.CanExecute(null));
    }
}

public sealed class HistoryRepairTests : IDisposable
{
    private readonly AppHarness _app = new();

    public void Dispose() => _app.Dispose();

    private async Task<string> CustomizedWithMissingIcon()
    {
        var folder = _app.Box.NewFolder();
        _app.Box.Customizer().Apply(folder, Sandbox.BlueStar);
        await _app.History.RefreshAsync();

        File.Delete(_app.Services.Icons.ExpectedPath(Sandbox.BlueStar));
        return folder;
    }

    [Fact]
    public void Repair_needs_something_to_look_at()
    {
        Assert.False(_app.History.RepairCommand.CanExecute(null));
    }

    [Fact]
    public async Task Repair_says_when_everything_is_fine()
    {
        _app.Box.Customizer().Apply(_app.Box.NewFolder(), Sandbox.BlueStar);
        await _app.History.RefreshAsync();

        await _app.History.RepairCommand.ExecuteAsync();

        Assert.Equal("All icons look fine.", _app.Shell.Activity.Message);
        Assert.Empty(_app.History.Results);
    }

    [Fact]
    public async Task Repair_redraws_and_shows_up_in_the_history_without_an_undo_that_does_nothing()
    {
        await CustomizedWithMissingIcon();

        await _app.History.RepairCommand.ExecuteAsync();

        Assert.True(File.Exists(_app.Services.Icons.ExpectedPath(Sandbox.BlueStar)));
        Assert.Equal("Repaired 1 icon.", _app.Shell.Activity.Message);
        Assert.Equal("Icon repaired.", Assert.Single(_app.History.Results).Message);

        var entry = _app.History.Entries[0];

        Assert.StartsWith("Repaired", entry.Title);
        Assert.False(entry.CanUndo);
    }

    [Fact]
    public async Task A_repair_does_not_count_as_a_newer_change_to_warn_about()
    {
        await CustomizedWithMissingIcon();
        await _app.History.RepairCommand.ExecuteAsync();

        Assert.Empty(_app.History.FoldersChangedSince(_app.History.Entries[1]));
    }

    [Fact]
    public async Task Everything_still_restores_after_a_repair()
    {
        var folder = await CustomizedWithMissingIcon();
        await _app.History.RepairCommand.ExecuteAsync();

        await _app.History.RestoreAllCommand.ExecuteAsync();

        Assert.False(File.Exists(Sandbox.IniPath(folder)));
    }

    [Fact]
    public async Task Repair_does_not_call_a_folder_it_skipped_fine()
    {
        var folder = await CustomizedWithMissingIcon();
        Directory.Move(folder, folder + " moved");

        await _app.History.RepairCommand.ExecuteAsync();

        Assert.DoesNotContain("look fine", _app.Shell.Activity.Message);
        Assert.Contains("1 folder was skipped", _app.Shell.Activity.Message);
    }

    [Fact]
    public void Repair_says_when_it_was_stopped()
    {
        Assert.Equal(
            "You stopped the check, so 3 folders weren't looked at.",
            PlainEnglish.RepairSummary(new RepairReport([], 3, 0, 0, wasCanceled: true)));

        Assert.Equal(
            "You stopped the check, so 1 folder wasn't looked at.",
            PlainEnglish.RepairSummary(new RepairReport([], 3, 2, 0, wasCanceled: true)));
    }

    [Fact]
    public void Repair_with_nothing_to_look_at_does_not_call_it_fine()
    {
        Assert.DoesNotContain("look fine", PlainEnglish.RepairSummary(new RepairReport([], 0, 0, 0, wasCanceled: false)));
    }

    [Fact]
    public void Repair_counts_a_mixed_run()
    {
        var summary = PlainEnglish.RepairSummary(new RepairReport(
            [new FolderRepairResult("a", RepairOutcome.Repaired), new FolderRepairResult("b", RepairOutcome.NotRepairable)],
            total: 5,
            healthy: 0,
            leftAlone: 3,
            wasCanceled: false));

        Assert.Equal("Repaired 1 icon. 1 folder couldn't be repaired. 3 folders were skipped, because they're missing or no longer as Foldcraft left them.", summary);
    }

    private static RulePreview PreviewOf(bool wasCanceled) => new()
    {
        ScopePath = "x",
        Rows = [],
        FoldersScanned = 300,
        HitLimit = false,
        WasCanceled = wasCanceled,
    };

    [Fact]
    public void A_stopped_preview_says_some_folders_were_not_looked_at()
    {
        Assert.Contains("You stopped early", PlainEnglish.RulePreviewSummary(PreviewOf(wasCanceled: true)));
        Assert.DoesNotContain("You stopped early", PlainEnglish.RulePreviewSummary(PreviewOf(wasCanceled: false)));
    }

    [Fact]
    public void Repair_wording_stays_plain()
    {
        foreach (var result in new[]
                 {
                     new FolderRepairResult("x", RepairOutcome.Repaired),
                     new FolderRepairResult("x", RepairOutcome.NotRepairable, "desktop.ini is a junction"),
                     new FolderRepairResult("x", RepairOutcome.Failed, "The undo journal could not be written"),
                 })
        {
            var text = PlainEnglish.RepairResult(result);

            Assert.DoesNotContain("desktop.ini", text);
            Assert.DoesNotContain("journal", text);
            Assert.DoesNotContain("junction", text);
        }
    }

    public static TheoryData<RulePreviewStatus> Statuses() => new(Enum.GetValues<RulePreviewStatus>());

    [Theory]
    [MemberData(nameof(Statuses))]
    public void Every_preview_line_has_plain_wording(RulePreviewStatus status)
    {
        var text = PlainEnglish.RulePreview(new RulePreviewRow { FolderPath = "x", Status = status, Reason = "journal junction" });

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("journal", text);
        Assert.DoesNotContain("junction", text);
    }

    public static TheoryData<RightClickOutcome> FailedOutcomes() =>
        new(RightClickOutcome.LeftAlone, RightClickOutcome.CannotQuote, RightClickOutcome.Failed);

    [Theory]
    [MemberData(nameof(FailedOutcomes))]
    public void Every_right_click_problem_has_wording(RightClickOutcome outcome)
    {
        Assert.False(string.IsNullOrWhiteSpace(PlainEnglish.RightClickMessage(outcome)));
    }
}

public sealed class SettingsStoreTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    [Fact]
    public void Nothing_saved_means_the_defaults()
    {
        var settings = new SettingsStore(_box.Paths).Load();

        Assert.Equal(AppThemeMode.System, settings.ThemeMode);
        Assert.Null(settings.RightClick);
    }

    [Fact]
    public void Settings_come_back()
    {
        var saved = new AppSettings { ThemeMode = AppThemeMode.Dark, RightClick = new RightClickRecord("cmd", "exe") };

        new SettingsStore(_box.Paths).Save(saved);

        Assert.Equal(saved, new SettingsStore(_box.Paths).Load());
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("""{ "ThemeMode": "Purple" }""")]
    [InlineData("""{ "ThemeMode": 99 }""")]
    public void A_bad_file_means_the_defaults(string text)
    {
        File.WriteAllText(_box.Paths.SettingsFile, text);

        Assert.Equal(new AppSettings(), new SettingsStore(_box.Paths).Load());
    }
}

public sealed class CommandLineTests : IDisposable
{
    private readonly AppHarness _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Customize_takes_a_path()
    {
        Assert.Equal(@"C:\Some Folder", AppOptions.Parse(["--customize", @"C:\Some Folder"]).CustomizePath);
    }

    [Fact]
    public void Both_options_work_in_either_order()
    {
        var one = AppOptions.Parse(["--customize", @"C:\A", "--data-dir", @"C:\B"]);
        var two = AppOptions.Parse(["--data-dir", @"C:\B", "--customize", @"C:\A"]);

        Assert.Equal(one, two);
        Assert.Equal(@"C:\A", one.CustomizePath);
        Assert.Equal(@"C:\B", one.DataDirectory);
    }

    [Theory]
    [InlineData("--customize")]
    [InlineData("--customize", "--data-dir", @"C:\B")]
    [InlineData("--customize", "   ")]
    public void A_missing_path_is_ignored(params string[] args)
    {
        Assert.Null(AppOptions.Parse(args).CustomizePath);
    }

    [Fact]
    public void A_path_cannot_smuggle_in_another_option()
    {
        var options = AppOptions.Parse(["--customize", "--data-dir", @"C:\Elsewhere"]);

        Assert.Null(options.CustomizePath);
        Assert.Equal(@"C:\Elsewhere", options.DataDirectory);
    }

    [Fact]
    public async Task A_folder_from_outside_lands_in_the_list_and_nothing_changes()
    {
        var folder = _app.Box.NewFolder();
        _app.Shell.SelectedTab = ShellViewModel.AboutTab;

        await _app.Shell.OpenFolderAsync(folder);

        Assert.Equal(ShellViewModel.CustomizeTab, _app.Shell.SelectedTab);
        Assert.Equal(folder, Assert.Single(_app.Customize.Folders).Path);
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.False(File.Exists(_app.Box.Paths.JournalFile));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"relative\path")]
    [InlineData(@"\\server\share")]
    [InlineData("C:\\bad\"name")]
    [InlineData("nul")]
    public async Task A_bad_path_from_outside_is_shown_as_refused(string path)
    {
        await _app.Shell.OpenFolderAsync(path);

        Assert.False(Assert.Single(_app.Customize.Folders).IsAllowed);
    }

    [Fact]
    public async Task No_path_only_brings_the_window_forward()
    {
        var raised = 0;
        _app.Shell.ActivationRequested += (_, _) => raised++;

        await _app.Shell.OpenFolderAsync(string.Empty);

        Assert.Equal(1, raised);
        Assert.Empty(_app.Customize.Folders);
    }
}

public sealed class SingleInstanceTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private static async Task<string?> Receive(TaskCompletionSource<string> received)
    {
        var finished = await Task.WhenAny(received.Task, Task.Delay(5000));
        return finished == received.Task ? received.Task.Result : null;
    }

    [Fact]
    public void The_first_start_is_first_and_the_second_is_not()
    {
        using var first = new SingleInstance(_box.Paths.DataDirectory);
        using var second = new SingleInstance(_box.Paths.DataDirectory);

        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
    }

    [Fact]
    public void Different_data_folders_do_not_share()
    {
        using var other = new Sandbox();
        using var first = new SingleInstance(_box.Paths.DataDirectory);
        using var second = new SingleInstance(other.Paths.DataDirectory);

        Assert.True(first.IsFirst);
        Assert.True(second.IsFirst);
    }

    [Fact]
    public async Task A_path_reaches_the_running_app()
    {
        using var first = new SingleInstance(_box.Paths.DataDirectory);
        using var second = new SingleInstance(_box.Paths.DataDirectory);
        var received = new TaskCompletionSource<string>();

        first.Listen(received.SetResult);

        var sent = await Task.Run(() => second.TrySend(@"C:\Some Folder \u00E9"));

        Assert.True(sent);
        Assert.Equal(@"C:\Some Folder \u00E9", await Receive(received));
    }

    /// <summary>
    /// The channel used to open on the thread pool, so a busy pool could keep it shut for longer
    /// than a second copy waits, and that copy's folder was lost.
    /// </summary>
    [Fact]
    public void The_channel_is_open_as_soon_as_listening_starts()
    {
        using var first = new SingleInstance(_box.Paths.DataDirectory);

        first.Listen(_ => { });

        var name = SingleInstance.MutexName(_box.Paths.DataDirectory)[@"Local\".Length..];

        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);

        pipe.Connect(0);

        Assert.True(pipe.IsConnected);
    }

    [Fact]
    public async Task Several_paths_arrive_one_after_another()
    {
        using var first = new SingleInstance(_box.Paths.DataDirectory);
        using var second = new SingleInstance(_box.Paths.DataDirectory);
        var seen = new List<string>();
        var done = new TaskCompletionSource<string>();

        first.Listen(path =>
        {
            seen.Add(path);

            if (seen.Count == 2)
            {
                done.SetResult(path);
            }
        });

        await Task.Run(() => second.TrySend("one"));
        await Task.Delay(50);
        await Task.Run(() => second.TrySend("two"));
        await Receive(done);

        Assert.Equal(["one", "two"], seen);
    }

    [Fact]
    public async Task Only_the_first_copy_listens()
    {
        using var first = new SingleInstance(_box.Paths.DataDirectory);
        using var second = new SingleInstance(_box.Paths.DataDirectory);
        var heard = 0;

        second.Listen(_ => heard++);

        Assert.False(await Task.Run(() => first.TrySend("anything")));
        Assert.Equal(0, heard);
    }

    [Fact]
    public async Task With_nobody_listening_a_send_says_so()
    {
        using var instance = new SingleInstance(_box.Paths.DataDirectory);

        Assert.False(await Task.Run(() => instance.TrySend("anything")));
    }

    [Fact]
    public async Task An_oversized_message_is_dropped_and_the_next_one_gets_through()
    {
        using var first = new SingleInstance(_box.Paths.DataDirectory);
        using var second = new SingleInstance(_box.Paths.DataDirectory);
        var received = new TaskCompletionSource<string>();

        first.Listen(received.SetResult);

        await Task.Run(() => second.TrySend(new string('x', 200_000)));
        await Task.Delay(50);
        await Task.Run(() => second.TrySend("after"));

        Assert.Equal("after", await Receive(received));
    }

    [Fact]
    public async Task A_copy_whose_marker_cannot_be_opened_runs_by_itself_and_does_not_crash()
    {
        var security = new MutexSecurity();

        security.AddAccessRule(new MutexAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), MutexRights.FullControl, AccessControlType.Allow));

        using var held = MutexAcl.Create(false, SingleInstance.MutexName(_box.Paths.DataDirectory), out _, security);
        using var instance = new SingleInstance(_box.Paths.DataDirectory);

        Assert.False(instance.IsFirst);
        Assert.False(await Task.Run(() => instance.TrySend("anything")));
    }
}

public sealed class FolderInboxTests
{
    [Fact]
    public void Folders_that_arrive_early_wait_for_the_window_and_keep_their_order()
    {
        var inbox = new FolderInbox();
        var opened = new List<string>();

        inbox.Receive("one");
        inbox.Receive("two");

        Assert.Empty(opened);

        inbox.Open(path =>
        {
            opened.Add(path);
            return Task.CompletedTask;
        });

        Assert.Equal(["one", "two"], opened);
    }

    [Fact]
    public void Once_the_window_is_ready_folders_go_straight_there_and_only_once()
    {
        var inbox = new FolderInbox();
        var opened = new List<string>();

        inbox.Receive("early");

        inbox.Open(path =>
        {
            opened.Add(path);
            return Task.CompletedTask;
        });

        inbox.Receive("late");

        Assert.Equal(["early", "late"], opened);
    }
}

/// <summary>Starts the real app with a throwaway data folder and closes it as soon as it has got that far.</summary>
public sealed class WorkingFolderTests : IDisposable
{
    private static readonly TimeSpan MaxStartTime = TimeSpan.FromSeconds(30);

    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private static bool WaitForMarker(string name, Process process)
    {
        var deadline = DateTime.UtcNow + MaxStartTime;

        while (DateTime.UtcNow < deadline && !process.HasExited)
        {
            if (Mutex.TryOpenExisting(name, out var marker))
            {
                marker.Dispose();
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    [Fact]
    [Trait("Category", TestCategories.Desktop)]
    public void The_folder_the_app_was_started_in_stays_free_to_rename()
    {
        var picked = Path.Combine(_box.NewFolder("parent"), "picked");
        Directory.CreateDirectory(picked);

        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Foldcraft.exe"))
        {
            WorkingDirectory = picked,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("--data-dir");
        start.ArgumentList.Add(_box.Paths.DataDirectory);

        using var process = Process.Start(start)!;

        try
        {
            Assert.True(WaitForMarker(SingleInstance.MutexName(_box.Paths.DataDirectory), process), "The app did not start.");

            Directory.Move(picked, picked + " renamed");
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
    }
}
