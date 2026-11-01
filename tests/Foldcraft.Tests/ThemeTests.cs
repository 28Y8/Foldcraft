using System.Text;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Themes;

namespace Foldcraft.Tests;

public sealed class ThemeFileTests
{
    private static string File(string themeBody) =>
        $$"""{ "format": "foldcraft-themes", "version": 1, "themes": [ { {{themeBody}} } ] }""";

    private const string Good = """ "name": "Ocean", "fill": { "start": "#0EA5E9" } """;

    private static IReadOnlyList<FolderTheme> Read(string text) => ThemeFile.Read(Encoding.UTF8.GetBytes(text));

    private static FolderTheme Theme(FolderIconSpec spec) =>
        new() { Id = ThemeFile.NewId(), Name = "Test", Mood = "Saved", Spec = spec };

    [Fact]
    public void A_simple_file_reads()
    {
        var theme = Assert.Single(Read(File(Good)));

        Assert.Equal("Ocean", theme.Name);
        Assert.Equal("Saved", theme.Mood);
        Assert.Equal(IconColor.Parse("#0EA5E9"), theme.Spec.Fill.Start);
        Assert.False(theme.IsBuiltIn);
    }

    [Fact]
    public void A_file_with_a_byte_order_mark_reads()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(File(Good))).ToArray();

        Assert.Single(ThemeFile.Read(bytes));
    }

    [Fact]
    public void Every_part_of_a_look_survives_the_trip()
    {
        var spec = new FolderIconSpec
        {
            Fill = FolderFill.Gradient(IconColor.Parse("#112233"), IconColor.Parse("#445566"), 135f),
            Shape = FolderShape.Soft3D,
            GlyphId = "music",
            Badge = new IconBadge("done", BadgeCorner.TopLeft, IconColor.Parse("#778899")),
        };

        var back = Assert.Single(ThemeFile.Read(ThemeFile.Write([Theme(spec)], forThisPc: false)));

        Assert.Equal(spec, back.Spec);
    }

    [Fact]
    public void An_emoji_survives_the_trip()
    {
        var spec = new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#112233")), Emoji = "\U0001F680" };

        Assert.Equal(spec, Assert.Single(ThemeFile.Read(ThemeFile.Write([Theme(spec)], forThisPc: false))).Spec);
    }

    [Fact]
    public void A_shared_file_carries_no_id_and_no_picture_path()
    {
        var spec = new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#112233")), CustomImagePath = @"C:\Users\someone\secret.png" };
        var text = Encoding.UTF8.GetString(ThemeFile.Write([Theme(spec)], forThisPc: false));

        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("someone", text);
        Assert.DoesNotContain("\"id\"", text);
    }

    public static TheoryData<string> Broken() => new()
    {
        string.Empty,
        "not json",
        "[]",
        "{}",
        "null",
        """{ "format": "something-else", "version": 1, "themes": [] }""",
        """{ "version": 1, "themes": [] }""",
        """{ "format": "foldcraft-themes", "themes": [] }""",
        """{ "format": "foldcraft-themes", "version": "1", "themes": [] }""",
        """{ "format": "foldcraft-themes", "version": 0, "themes": [] }""",
        """{ "format": "foldcraft-themes", "version": 2, "themes": [] }""",
        """{ "format": "foldcraft-themes", "version": 1 }""",
        """{ "format": "foldcraft-themes", "version": 1, "themes": {} }""",
        """{ "format": "foldcraft-themes", "version": 1, "themes": [], "extra": 1 }""",
        """{ "format": "foldcraft-themes", "format": "foldcraft-themes", "version": 1, "themes": [] }""",
        """{ "format": "foldcraft-themes", "version": 1, "themes": [ 5 ] }""",
        """{ "format": "foldcraft-themes", "version": 1, "themes": [], }""",
        """{ "format": "foldcraft-themes", /* note */ "version": 1, "themes": [] }""",
        File(""" "fill": { "start": "#0EA5E9" } """),
        File(""" "name": "", "fill": { "start": "#0EA5E9" } """),
        File(""" "name": "   ", "fill": { "start": "#0EA5E9" } """),
        File(""" "name": 5, "fill": { "start": "#0EA5E9" } """),
        File(""" "name": "Line\nbreak", "fill": { "start": "#0EA5E9" } """),
        File($$""" "name": "{{new string('x', 61)}}", "fill": { "start": "#0EA5E9" } """),
        File(""" "name": "A" """),
        File(""" "name": "A", "fill": "#0EA5E9" """),
        File(""" "name": "A", "fill": { } """),
        File(""" "name": "A", "fill": { "start": "0EA5E9" } """),
        File(""" "name": "A", "fill": { "start": "#0EA5E" } """),
        File(""" "name": "A", "fill": { "start": "#GGGGGG" } """),
        File(""" "name": "A", "fill": { "start": "#0EA5E9", "angle": 90 } """),
        File(""" "name": "A", "fill": { "start": "#000000", "end": "#FFFFFF", "angle": 400 } """),
        File(""" "name": "A", "fill": { "start": "#000000", "end": "#FFFFFF", "angle": -1 } """),
        File(""" "name": "A", "fill": { "start": "#000000", "end": "#FFFFFF", "angle": "up" } """),
        File(""" "name": "A", "fill": { "start": "#000000", "extra": 1 } """),
        File(""" "name": "A", "fill": { "start": "#000000" }, "script": "x" """),
        File(""" "name": "A", "name": "B", "fill": { "start": "#000000" } """),
        File(""" "name": "A", "fill": { "start": "#000000" }, "shape": "round" """),
        File(""" "name": "A", "fill": { "start": "#000000" }, "glyph": "nonsense" """),
        File(""" "name": "A", "fill": { "start": "#000000" }, "emoji": "not one" """),
        File(""" "name": "A", "fill": { "start": "#000000" }, "glyph": "star", "emoji": "\u2764" """),
        File(""" "name": "A", "fill": { "start": "#000000" }, "badge": "done" """),
        File(""" "name": "A", "fill": { "start": "#000000" }, "badge": { } """),
        File(""" "name": "A", "fill": { "start": "#000000" }, "badge": { "kind": "nonsense" } """),
        File(""" "name": "A", "fill": { "start": "#000000" }, "badge": { "kind": "done", "corner": "middle" } """),
        File(""" "name": "A", "fill": { "start": "#000000" }, "badge": { "kind": "done", "color": "red" } """),
        File(""" "id": "builtin:ember", "name": "A", "fill": { "start": "#000000" } """),
        File(""" "id": "saved:../../evil", "name": "A", "fill": { "start": "#000000" } """),
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void A_broken_file_fails_cleanly(string text)
    {
        Assert.Throws<LookFormatException>(() => Read(text));
    }

    [Fact]
    public void Bytes_that_are_not_text_fail_cleanly()
    {
        Assert.Throws<LookFormatException>(() => ThemeFile.Read([0xFF, 0xFE, 0x00, 0x7B, 0x80]));
        Assert.Throws<LookFormatException>(() => ThemeFile.Read(Encoding.UTF8.GetBytes("{ \"format\": \"\u00E9").Concat(new byte[] { 0xC3 }).ToArray()));
    }

    [Fact]
    public void Deep_nesting_fails_cleanly()
    {
        var text = string.Concat(Enumerable.Repeat("[", 5000));

        Assert.Throws<LookFormatException>(() => Read(text));
    }

    [Fact]
    public void An_oversized_file_fails_without_being_parsed()
    {
        Assert.Throws<LookFormatException>(() => ThemeFile.Read(new byte[ThemeFile.MaxFileBytes + 1]));
    }

    [Fact]
    public void Too_many_themes_fail_cleanly()
    {
        var themes = string.Join(',', Enumerable.Repeat($"{{{Good}}}", ThemeFile.MaxThemes + 1));

        Assert.Throws<LookFormatException>(() => Read($$"""{ "format": "foldcraft-themes", "version": 1, "themes": [ {{themes}} ] }"""));
    }

    [Fact]
    public void A_newer_file_says_so()
    {
        var problem = Assert.Throws<LookFormatException>(() => Read("""{ "format": "foldcraft-themes", "version": 9, "themes": [] }"""));

        Assert.Contains("newer version", problem.Message);
    }

    [Theory]
    [InlineData(@"\\\\server\\share\\a.png")]
    [InlineData("//server/share/a.png")]
    [InlineData(@"\\\\?\\C:\\a.png")]
    [InlineData(@"\\\\.\\C:\\a.png")]
    [InlineData(@"\\\\?\\UNC\\server\\share\\a.png")]
    [InlineData("a.png")]
    [InlineData(@"..\\a.png")]
    [InlineData(@"C:\\Pictures\\..\\Windows\\a.png")]
    [InlineData(@"C:a.png")]
    [InlineData(@"C:\\Pictures\\a.exe")]
    [InlineData(@"C:\\Pictures\\a")]
    [InlineData(@"C:\\Pictures\\a.png:hidden")]
    [InlineData(@"%USERPROFILE%\\a.png")]
    [InlineData(@"C:\\%TEMP%\\a.png")]
    [InlineData(@"C:\\Pictures\\*.png")]
    [InlineData(@"C:\\Pictures\\a?.png")]
    [InlineData(@"C:\\Pic\ntures\\a.png")]
    [InlineData(@"/\\host.invalid\\share\\a.png")]
    [InlineData(@"\\/host.invalid\\share\\a.png")]
    [InlineData(@"/\\host.invalid/share/a.png")]
    [InlineData(@"\\/host.invalid/share/a.png")]
    [InlineData(@"/\\.\\C:\\a.png")]
    public void A_theme_cannot_point_at_a_path_that_fails_validation(string json)
    {
        var text = File($$""" "name": "A", "fill": { "start": "#000000" }, "image": "{{json}}" """);

        Assert.Throws<LookFormatException>(() => Read(text));
    }

    [Fact]
    public void A_theme_can_name_a_plain_picture_on_a_local_drive()
    {
        var theme = Assert.Single(Read(File(""" "name": "A", "fill": { "start": "#000000" }, "image": "C:\\Pictures\\a.png" """)));

        Assert.Equal(@"C:\Pictures\a.png", theme.Spec.CustomImagePath);
    }

    [Fact]
    public void A_theme_cannot_name_a_picture_on_a_drive_that_is_not_there()
    {
        var mounted = DriveInfo.GetDrives().Select(drive => drive.Name[0]).ToArray();
        var letter = "ZYXWVUTSRQPONMLKJIHGFED".First(c => !mounted.Contains(c));

        var text = File($$""" "name": "A", "fill": { "start": "#000000" }, "image": "{{letter}}:\\a.png" """);

        Assert.Throws<LookFormatException>(() => Read(text));
    }

    [Theory]
    [InlineData(@"\\host.invalid\share\a.png")]
    [InlineData(@"/\host.invalid\share\a.png")]
    [InlineData(@"\/host.invalid\share\a.png")]
    [InlineData("//host.invalid/share/a.png")]
    [InlineData(@"\\?\UNC\host.invalid\share\a.png")]
    [InlineData("a.png")]
    public void Only_a_plain_path_on_this_pc_counts_as_a_local_picture(string path)
    {
        Assert.False(IconSpecCodec.IsLocalPicture(path));
    }

    [Fact]
    public void A_picture_on_a_local_drive_is_local()
    {
        Assert.True(IconSpecCodec.IsLocalPicture(Path.Combine(Path.GetTempPath(), "a.png")));
    }
}

public sealed class BuiltInThemeTests
{
    [Fact]
    public void There_are_twenty_four_across_several_moods()
    {
        Assert.Equal(24, BuiltInThemes.All.Count);
        Assert.True(BuiltInThemes.All.Select(t => t.Mood).Distinct().Count() >= 6);
    }

    [Fact]
    public void Each_has_its_own_id_name_and_look()
    {
        Assert.Equal(24, BuiltInThemes.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal(24, BuiltInThemes.All.Select(t => t.Name).Distinct().Count());
        Assert.Equal(24, BuiltInThemes.All.Select(t => t.Spec).Distinct().Count());
    }

    [Fact]
    public void Each_can_be_rendered_at_every_size()
    {
        var renderer = new IconRenderer();

        Assert.All(BuiltInThemes.All, theme =>
        {
            Assert.True(theme.IsBuiltIn);
            Assert.Equal(IconRenderer.Sizes.Count, IcoFile.TryReadFrames(renderer.RenderIco(theme.Spec))?.Count);
        });
    }

    [Fact]
    public void Each_passes_the_rules_for_a_file()
    {
        var back = ThemeFile.Read(ThemeFile.Write(BuiltInThemes.All, forThisPc: false));

        Assert.Equal(BuiltInThemes.All.Select(t => t.Spec), back.Select(t => t.Spec));
    }
}

public sealed class ThemeStoreTests : IDisposable
{
    private static readonly FolderIconSpec Look = Sandbox.BlueStar;

    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private ThemeStore NewStore() => new(_box.Paths);

    private string Temp(string name) => Path.Combine(_box.Root, name);

    private string WriteFile(string text)
    {
        var path = Temp("incoming.json");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Built_in_themes_are_there_with_nothing_saved()
    {
        Assert.Equal(BuiltInThemes.All.Count, NewStore().All.Count);
        Assert.False(File.Exists(_box.Paths.ThemesFile));
    }

    [Fact]
    public void A_saved_theme_comes_back_after_a_restart_with_the_same_id()
    {
        var saved = NewStore().Save("Mine", Look);

        var found = NewStore().Find(saved.Id);

        Assert.NotNull(found);
        Assert.Equal("Mine", found.Name);
        Assert.Equal(Look, found.Spec);
    }

    [Fact]
    public void A_saved_theme_keeps_its_picture_on_this_pc()
    {
        var spec = new FolderIconSpec { Fill = Look.Fill, CustomImagePath = @"C:\Pictures\me.png" };
        var saved = NewStore().Save("Mine", spec);

        Assert.Equal(spec, NewStore().Find(saved.Id)!.Spec);
    }

    [Fact]
    public void Saving_under_the_same_name_replaces_the_look_and_keeps_the_id()
    {
        var store = NewStore();
        var first = store.Save("Mine", Look);
        var second = store.Save("mine", Sandbox.GreenHeart);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(BuiltInThemes.All.Count + 1, store.All.Count);
        Assert.Equal(Sandbox.GreenHeart, store.Find(first.Id)!.Spec);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nname")]
    public void A_bad_name_is_refused(string name)
    {
        Assert.Throws<LookFormatException>(() => NewStore().Save(name, Look));
    }

    [Fact]
    public void A_long_name_is_refused()
    {
        Assert.Throws<LookFormatException>(() => NewStore().Save(new string('x', ThemeFile.MaxNameLength + 1), Look));
    }

    [Fact]
    public void A_built_in_theme_cannot_be_deleted()
    {
        var store = NewStore();

        Assert.False(store.Delete(BuiltInThemes.All[0].Id));
        Assert.Equal(BuiltInThemes.All.Count, store.All.Count);
    }

    [Fact]
    public void A_saved_theme_can_be_deleted()
    {
        var store = NewStore();
        var saved = store.Save("Mine", Look);

        Assert.True(store.Delete(saved.Id));
        Assert.Null(NewStore().Find(saved.Id));
    }

    [Fact]
    public void Export_then_import_moves_themes_to_another_pc()
    {
        var mine = NewStore();
        mine.Save("One", Look);
        mine.Save("Two", Sandbox.GreenHeart);

        var path = Temp("out.json");
        var result = mine.Export(mine.All.Where(t => !t.IsBuiltIn).ToArray(), path);

        Assert.Equal(new ThemeExportResult(2, 0), result);

        using var other = new Sandbox();
        var theirs = new ThemeStore(other.Paths);

        Assert.Equal(["One", "Two"], theirs.Import(path).Select(t => t.Name));
        Assert.Equal(BuiltInThemes.All.Count + 2, new ThemeStore(other.Paths).All.Count);
    }

    [Fact]
    public void An_export_counts_the_pictures_it_left_behind()
    {
        var store = NewStore();
        var theme = store.Save("Mine", new FolderIconSpec { Fill = Look.Fill, CustomImagePath = @"C:\Pictures\me.png" });

        var result = store.Export([theme], Temp("out.json"));

        Assert.Equal(1, result.PicturesLeftOut);
        Assert.DoesNotContain("me.png", File.ReadAllText(Temp("out.json")));
    }

    [Fact]
    public void An_import_with_one_bad_theme_adds_nothing()
    {
        var path = WriteFile("""
            { "format": "foldcraft-themes", "version": 1, "themes": [
              { "name": "Fine", "fill": { "start": "#000000" } },
              { "name": "Bad", "fill": { "start": "#000000" }, "glyph": "nonsense" } ] }
            """);

        var store = NewStore();

        Assert.Throws<LookFormatException>(() => store.Import(path));
        Assert.Equal(BuiltInThemes.All.Count, store.All.Count);
        Assert.False(File.Exists(_box.Paths.ThemesFile));
    }

    [Fact]
    public void An_import_with_a_taken_name_keeps_both()
    {
        var store = NewStore();
        store.Save("Mine", Look);

        var path = WriteFile("""{ "format": "foldcraft-themes", "version": 1, "themes": [ { "name": "Mine", "fill": { "start": "#000000" } }, { "name": "Mine", "fill": { "start": "#111111" } } ] }""");

        Assert.Equal(["Mine 2", "Mine 3"], store.Import(path).Select(t => t.Name));
    }

    [Fact]
    public void An_imported_id_is_never_kept()
    {
        var store = NewStore();
        var mine = store.Save("Mine", Look);
        var path = WriteFile($$"""{ "format": "foldcraft-themes", "version": 1, "themes": [ { "id": "{{mine.Id}}", "name": "Pretender", "fill": { "start": "#000000" } } ] }""");

        var imported = Assert.Single(store.Import(path));

        Assert.NotEqual(mine.Id, imported.Id);
        Assert.Equal(Look, store.Find(mine.Id)!.Spec);
    }

    [Fact]
    public void A_missing_file_fails_cleanly()
    {
        Assert.Throws<LookFormatException>(() => NewStore().Import(Temp("not-here.json")));
    }

    [Fact]
    public void A_huge_file_is_refused_without_being_read()
    {
        var path = Temp("huge.json");

        using (var file = File.Create(path))
        {
            file.SetLength(ThemeFile.MaxFileBytes + 1);
        }

        Assert.Throws<LookFormatException>(() => NewStore().Import(path));
    }

    [Fact]
    public void A_damaged_saved_file_is_set_aside_not_overwritten()
    {
        Directory.CreateDirectory(_box.Paths.DataDirectory);
        File.WriteAllText(_box.Paths.ThemesFile, "garbage");

        var store = NewStore();

        Assert.Equal(BuiltInThemes.All.Count, store.All.Count);
        Assert.NotNull(store.LoadProblem);

        store.Save("Mine", Look);

        var aside = Assert.Single(Directory.GetFiles(_box.Paths.DataDirectory, "themes.json.*.unreadable"));
        Assert.Equal("garbage", File.ReadAllText(aside));
    }

    private string[] SetAside() => Directory.GetFiles(_box.Paths.DataDirectory, "themes.json.*.unreadable");

    private void WriteSavedThemes(int count) => File.WriteAllBytes(
        _box.Paths.ThemesFile,
        ThemeFile.Write(
            Enumerable.Range(0, count).Select(i => new FolderTheme { Id = ThemeFile.NewId(), Name = $"Theme {i}", Mood = "Saved", Spec = Look }),
            forThisPc: true));

    public static TheoryData<string> PicturesFromTheDesigner() => new()
    {
        @"C:\Pics\100% done\a.png",
        @"\\host.invalid\photos\a.png",
        @"C:\Pics\a.webp",
        @"C:\Pics\" + new string('x', 1100) + ".png",
    };

    [Theory]
    [MemberData(nameof(PicturesFromTheDesigner))]
    public void A_picture_that_saved_fine_costs_no_theme_at_the_next_start(string picture)
    {
        var store = NewStore();
        var mine = store.Save("Mine", new FolderIconSpec { Fill = Look.Fill, CustomImagePath = picture });
        var other = store.Save("Other", Sandbox.GreenHeart);

        var reopened = NewStore();

        Assert.Equal(picture, reopened.Find(mine.Id)?.Spec.CustomImagePath);
        Assert.NotNull(reopened.Find(other.Id));
        Assert.Null(reopened.LoadProblem);
        Assert.Empty(SetAside());
    }

    [Fact]
    public void A_file_that_got_past_the_limit_still_opens()
    {
        WriteSavedThemes(ThemeFile.MaxThemes + 1);

        var store = NewStore();

        Assert.Equal(BuiltInThemes.All.Count + ThemeFile.MaxThemes + 1, store.All.Count);
        Assert.Null(store.LoadProblem);
        Assert.Empty(SetAside());
    }

    [Fact]
    public void Saving_stops_at_the_limit_but_a_saved_look_can_still_be_replaced()
    {
        WriteSavedThemes(ThemeFile.MaxThemes);

        var store = NewStore();

        Assert.Throws<LookFormatException>(() => store.Save("One more", Look));
        Assert.Equal(Sandbox.GreenHeart, store.Save("Theme 0", Sandbox.GreenHeart).Spec);
        Assert.Equal(ThemeFile.MaxThemes, ThemeFile.Read(File.ReadAllBytes(_box.Paths.ThemesFile)).Count);
    }

    [Fact]
    public void A_save_too_big_to_read_back_is_refused_and_changes_nothing()
    {
        var store = NewStore();
        var mine = store.Save("Mine", Look);
        var huge = new FolderIconSpec { Fill = Look.Fill, CustomImagePath = @"C:\" + new string('x', ThemeFile.MaxFileBytes) + ".png" };

        Assert.Throws<LookFormatException>(() => store.Save("Huge", huge));

        Assert.Equal(BuiltInThemes.All.Count + 1, store.All.Count);
        Assert.NotNull(NewStore().Find(mine.Id));
    }

    [Fact]
    public void One_theme_that_cannot_be_read_costs_only_itself()
    {
        var store = NewStore();
        var good = store.Save("Good", Sandbox.GreenHeart);
        store.Save("Bad", Look);

        var text = File.ReadAllText(_box.Paths.ThemesFile);
        File.WriteAllText(_box.Paths.ThemesFile, text.Replace("\"star\"", "\"gone-now\"", StringComparison.Ordinal));

        var reopened = NewStore();

        Assert.NotNull(reopened.Find(good.Id));
        Assert.Equal(BuiltInThemes.All.Count + 1, reopened.All.Count);
        Assert.Contains("1 saved theme", reopened.LoadProblem);
        Assert.Contains("gone-now", File.ReadAllText(Assert.Single(SetAside())));

        var third = NewStore();

        Assert.NotNull(third.Find(good.Id));
        Assert.Null(third.LoadProblem);
    }

    [Theory]
    [InlineData(@"/\\host.invalid\\share\\a.png")]
    [InlineData(@"\\/host.invalid\\share\\a.png")]
    public void An_import_is_held_to_the_strict_rules_saved_themes_are_not(string json)
    {
        var path = WriteFile($$"""{ "format": "foldcraft-themes", "version": 1, "themes": [ { "name": "Sneaky", "fill": { "start": "#000000" }, "image": "{{json}}" } ] }""");

        Assert.Throws<LookFormatException>(() => NewStore().Import(path));
        Assert.False(File.Exists(_box.Paths.ThemesFile));
    }
}
