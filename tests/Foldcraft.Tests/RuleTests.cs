using Foldcraft.Core.Customization;
using Foldcraft.Core.Rules;
using Foldcraft.Core.Safety;
using Foldcraft.Core.Themes;

namespace Foldcraft.Tests;

public sealed class RuleMatcherTests
{
    private static FolderRule Rule(RuleCondition condition, string pattern) => new()
    {
        Id = Guid.NewGuid(),
        Condition = condition,
        Pattern = pattern,
        ThemeId = "t",
    };

    [Theory]
    [InlineData(RuleCondition.NameContains, "photo", "Holiday PHOTOS", true)]
    [InlineData(RuleCondition.NameContains, "photo", "Videos", false)]
    [InlineData(RuleCondition.NameEquals, "invoices", "Invoices", true)]
    [InlineData(RuleCondition.NameEquals, "invoices", "Invoices 2024", false)]
    [InlineData(RuleCondition.NameStartsWith, "2024", "2024 Taxes", true)]
    [InlineData(RuleCondition.NameStartsWith, "2024", "Taxes 2024", false)]
    [InlineData(RuleCondition.NameWildcard, "Project *", "project alpha", true)]
    [InlineData(RuleCondition.NameWildcard, "Project *", "Projects", false)]
    [InlineData(RuleCondition.MostlyFileType, "pictures", "Photos", false)]
    public void Name_rules_match_as_described(RuleCondition condition, string pattern, string name, bool expected)
    {
        Assert.Equal(expected, RuleMatcher.MatchesName(Rule(condition, pattern), name));
    }

    [Theory]
    [InlineData("*", "", true)]
    [InlineData("*", "anything", true)]
    [InlineData("", "", true)]
    [InlineData("", "a", false)]
    [InlineData("a?c", "abc", true)]
    [InlineData("a?c", "ac", false)]
    [InlineData("a*c", "abbbbc", true)]
    [InlineData("a*c", "abbbb", false)]
    [InlineData("*.bak", "file.bak", true)]
    [InlineData("**a**", "bab", true)]
    [InlineData("a*b*c", "a1b2c", true)]
    [InlineData("a*b*c", "a1c2b", false)]
    [InlineData("[a]", "a", false)]
    [InlineData("(a)+", "aa", false)]
    public void Wildcards_mean_star_and_question_mark_and_nothing_else(string pattern, string text, bool expected)
    {
        Assert.Equal(expected, RuleMatcher.MatchesWildcard(text, pattern));
    }

    [Fact]
    public void A_nasty_wildcard_cannot_take_long()
    {
        var pattern = string.Concat(Enumerable.Repeat("a*", 40)) + "b";
        var text = new string('a', 240);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(RuleMatcher.MatchesWildcard(text, pattern));
        Assert.True(clock.ElapsedMilliseconds < 1000);
    }

    [Theory]
    [InlineData("pictures", new[] { "a.jpg", "b.PNG", "c.txt" }, true)]
    [InlineData("pictures", new[] { "a.jpg", "c.txt" }, false)]
    [InlineData("pictures", new string[0], false)]
    [InlineData("music", new[] { "a.mp3" }, true)]
    [InlineData(".psd", new[] { "a.PSD", "b.psd", "c.png" }, true)]
    [InlineData(".psd", new[] { "a.png" }, false)]
    public void Mostly_means_more_than_half(string pattern, string[] files, bool expected)
    {
        Assert.Equal(expected, RuleMatcher.IsMostly(files, pattern));
    }

    [Theory]
    [InlineData(RuleCondition.NameContains, "photos", true)]
    [InlineData(RuleCondition.NameContains, "", false)]
    [InlineData(RuleCondition.NameContains, "   ", false)]
    [InlineData(RuleCondition.NameContains, "a\tb", false)]
    [InlineData(RuleCondition.MostlyFileType, "pictures", true)]
    [InlineData(RuleCondition.MostlyFileType, ".psd", true)]
    [InlineData(RuleCondition.MostlyFileType, "psd", false)]
    [InlineData(RuleCondition.MostlyFileType, ".a/b", false)]
    [InlineData((RuleCondition)99, "x", false)]
    public void Only_sensible_rules_are_usable(RuleCondition condition, string pattern, bool expected)
    {
        Assert.Equal(expected, RuleMatcher.IsUsable(condition, pattern));
    }
}

public sealed class RuleStoreTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private RuleStore NewStore() => new(_box.Paths);

    [Fact]
    public void Rules_keep_their_order_across_a_restart()
    {
        FolderRule[] rules =
        [
            new() { Id = Guid.NewGuid(), Condition = RuleCondition.NameWildcard, Pattern = "A*", ThemeId = "one" },
            new() { Id = Guid.NewGuid(), Condition = RuleCondition.MostlyFileType, Pattern = "music", ThemeId = "two" },
        ];

        NewStore().Save(rules);

        Assert.Equal(rules, NewStore().Load());
    }

    [Fact]
    public void No_file_means_no_rules()
    {
        Assert.Empty(NewStore().Load());
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("""{ "Version": 99, "Rules": [] }""")]
    [InlineData("""{ "Version": 1 }""")]
    public void A_bad_file_is_set_aside(string text)
    {
        File.WriteAllText(_box.Paths.RulesFile, text);

        var store = NewStore();

        Assert.Empty(store.Load());
        Assert.NotNull(store.LoadProblem);
        Assert.Single(Directory.GetFiles(_box.Paths.DataDirectory, "rules.json.*.unreadable"));
    }

    [Fact]
    public void A_rule_that_makes_no_sense_is_dropped_on_reading()
    {
        File.WriteAllText(_box.Paths.RulesFile, $$"""
            { "Version": 1, "Rules": [
              { "Id": "{{Guid.NewGuid()}}", "Condition": "NameContains", "Pattern": "", "ThemeId": "t" },
              { "Id": "{{Guid.NewGuid()}}", "Condition": "NameContains", "Pattern": "ok", "ThemeId": "t" } ] }
            """);

        Assert.Equal("ok", Assert.Single(NewStore().Load()).Pattern);
    }

    [Fact]
    public void A_missing_entry_is_dropped_on_reading_and_does_not_stop_the_rest()
    {
        File.WriteAllText(_box.Paths.RulesFile, $$"""
            { "Version": 1, "Rules": [ null,
              { "Id": "{{Guid.NewGuid()}}", "Condition": "NameContains", "Pattern": "ok", "ThemeId": "t" } ] }
            """);

        Assert.Equal("ok", Assert.Single(NewStore().Load()).Pattern);
    }

    [Fact]
    public void A_file_of_only_missing_entries_reads_as_no_rules()
    {
        File.WriteAllText(_box.Paths.RulesFile, """{ "Version": 1, "Rules": [ null ] }""");

        Assert.Empty(NewStore().Load());
    }
}

public sealed class RuleScannerTests : IDisposable
{
    private const string BlueId = "theme:blue";
    private const string GreenId = "theme:green";

    private static readonly FolderTheme Blue = new() { Id = BlueId, Name = "Blue", Mood = "Test", Spec = Sandbox.BlueStar };
    private static readonly FolderTheme Green = new() { Id = GreenId, Name = "Green", Mood = "Test", Spec = Sandbox.GreenHeart };

    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private string Scope => Path.Combine(_box.Root, "folders");

    private RuleScanner Scanner() => new(_box.Safety, _box.Customizer());

    private static FolderTheme? Find(string id) => id == BlueId ? Blue : id == GreenId ? Green : null;

    private static FolderRule Named(string pattern, string themeId = BlueId) => new()
    {
        Id = Guid.NewGuid(),
        Condition = RuleCondition.NameContains,
        Pattern = pattern,
        ThemeId = themeId,
    };

    private RulePreview Preview(int depth, params FolderRule[] rules) => Scanner().Preview(Scope, depth, rules, Find);

    private static string[] Lines(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };

        return Directory.EnumerateFileSystemEntries(root, "*", options)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => $"{p}|{File.GetAttributes(p)}|{Directory.GetLastWriteTimeUtc(p).Ticks}")
            .ToArray();
    }

    [Fact]
    public void A_preview_changes_nothing_anywhere()
    {
        _box.NewFolder("photos");
        _box.NewFolder("taxes");
        var before = Lines(_box.Root);

        var preview = Preview(2, Named("photos"));

        Assert.Single(preview.Changes);
        Assert.Equal(before, Lines(_box.Root));
        Assert.False(File.Exists(_box.Paths.JournalFile));
        Assert.False(Directory.Exists(_box.Paths.IconsDirectory));
    }

    [Fact]
    public void The_chosen_folder_itself_is_never_a_target()
    {
        _box.NewFolder("folder-child");

        var preview = Preview(1, Named("folder"));

        Assert.Equal(Path.GetFullPath(Path.Combine(Scope, "folder-child")), Assert.Single(preview.Changes).FolderPath);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void Depth_limits_how_far_down_it_looks(int depth, int expected)
    {
        _box.NewFolder(@"match1\match2\match3\match4");

        Assert.Equal(expected, Preview(depth, Named("match")).Changes.Count());
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(99, 5)]
    public void Depth_stays_within_its_limits(int depth, int expected)
    {
        _box.NewFolder(@"m\m\m\m\m\m\m");

        Assert.Equal(expected, Preview(depth, Named("m")).Changes.Count());
    }

    [Fact]
    public void A_junction_is_reported_and_never_entered()
    {
        var outside = Path.Combine(_box.Root, "outside");
        Directory.CreateDirectory(Path.Combine(outside, "match-inside"));
        Sandbox.CreateJunction(Path.Combine(Scope, "match-link"), outside);

        var preview = Preview(5, Named("match"));

        var row = Assert.Single(preview.Rows);

        Assert.Equal(RulePreviewStatus.Refused, row.Status);
        Assert.Equal(FolderRefusal.ReparsePoint, row.Refusal);
        Assert.Empty(preview.Changes);
    }

    [Fact]
    public void A_protected_folder_is_skipped_with_its_reason_and_not_entered()
    {
        var scanner = Scanner();
        Directory.CreateDirectory(Path.Combine(_box.FakeAppData, "match-inside"));

        var preview = scanner.Preview(_box.FakeProfile, 3, [Named("match")], Find);

        Assert.Null(preview.ScopeRefusal);

        var row = Assert.Single(preview.Rows);

        Assert.Equal(FolderRefusal.AppData, row.Refusal);
        Assert.Empty(preview.Changes);
    }

    [Fact]
    public void Foldcrafts_own_folder_is_skipped_with_its_reason()
    {
        var preview = Scanner().Preview(_box.Root, 1, [Named("data")], Find);

        Assert.Contains(preview.Rows, row => row.Refusal == FolderRefusal.FoldcraftData);
        Assert.Empty(preview.Changes);
    }

    [Fact]
    public void A_hidden_folder_is_left_alone()
    {
        var hidden = _box.NewFolder("match-hidden");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        _box.NewFolder(@"match-hidden\match-under");

        var preview = Preview(3, Named("match"));

        Assert.Equal(RulePreviewStatus.Hidden, Assert.Single(preview.Rows).Status);
    }

    [Theory]
    [InlineData(FolderRefusal.WindowsDirectory)]
    public void A_chosen_folder_that_is_off_limits_stops_the_scan(FolderRefusal refusal)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var preview = Scanner().Preview(windows, 1, [Named("a")], Find);

        Assert.Equal(refusal, preview.ScopeRefusal);
        Assert.Empty(preview.Rows);
    }

    [Fact]
    public void A_chosen_folder_that_is_a_junction_stops_the_scan()
    {
        var link = Sandbox.CreateJunction(Path.Combine(_box.Root, "link"), _box.NewFolder("target"));

        Assert.Equal(FolderRefusal.ReparsePoint, Scanner().Preview(link, 1, [Named("a")], Find).ScopeRefusal);
    }

    [Fact]
    public void The_first_rule_that_fits_wins()
    {
        _box.NewFolder("photos 2024");

        var row = Assert.Single(Preview(1, Named("2024", GreenId), Named("photos", BlueId)).Changes);

        Assert.Equal("Green", row.Theme!.Name);
    }

    [Fact]
    public void A_folder_already_showing_the_look_says_so()
    {
        var folder = _box.NewFolder("photos");
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var row = Assert.Single(Preview(1, Named("photos")).Rows);

        Assert.Equal(RulePreviewStatus.AlreadyHasLook, row.Status);
    }

    [Fact]
    public void A_folder_with_a_different_look_will_change()
    {
        var folder = _box.NewFolder("photos");
        _box.Customizer().Apply(folder, Sandbox.GreenHeart);

        Assert.Single(Preview(1, Named("photos")).Changes);
    }

    [Fact]
    public void A_rule_whose_theme_is_gone_changes_nothing_and_says_so()
    {
        _box.NewFolder("photos");

        var row = Assert.Single(Preview(1, Named("photos", "theme:gone")).Rows);

        Assert.Equal(RulePreviewStatus.ThemeMissing, row.Status);
    }

    [Fact]
    public void A_file_type_rule_counts_the_files_directly_inside()
    {
        var folder = _box.NewFolder("anything");

        foreach (var name in new[] { "a.jpg", "b.jpg", "c.txt" })
        {
            File.WriteAllText(Path.Combine(folder, name), "x");
        }

        var rule = new FolderRule { Id = Guid.NewGuid(), Condition = RuleCondition.MostlyFileType, Pattern = "pictures", ThemeId = BlueId };

        Assert.Single(Preview(1, rule).Changes);
    }

    [Fact]
    public void A_file_type_rule_does_not_count_the_settings_file()
    {
        var folder = _box.NewFolder("anything");
        File.WriteAllText(Path.Combine(folder, "a.txt"), "x");
        Sandbox.PlantIni(folder, [1, 2, 3]);

        var rule = new FolderRule { Id = Guid.NewGuid(), Condition = RuleCondition.MostlyFileType, Pattern = ".txt", ThemeId = BlueId };

        Assert.Single(Preview(1, rule).Changes);
    }

    [Fact]
    public void A_canceled_scan_stops_and_returns_what_it_has()
    {
        _box.NewFolder("photos");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Empty(Scanner().Preview(Scope, 1, [Named("photos")], Find, cancel.Token).Rows);
    }

    [Fact]
    public void A_canceled_scan_says_it_was_canceled()
    {
        _box.NewFolder("photos");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.True(Scanner().Preview(Scope, 1, [Named("photos")], Find, cancel.Token).WasCanceled);
    }

    [Fact]
    public void A_scan_canceled_partway_keeps_what_it_found_and_says_so()
    {
        _box.NewFolder("photos 1");
        _box.NewFolder("photos 2");
        using var cancel = new CancellationTokenSource();

        FolderTheme? CancelAfterFirst(string id)
        {
            cancel.Cancel();
            return Find(id);
        }

        var preview = Scanner().Preview(Scope, 1, [Named("photos")], CancelAfterFirst, cancel.Token);

        Assert.True(preview.WasCanceled);
        Assert.Single(preview.Rows);
        Assert.Equal(1, preview.FoldersScanned);
    }

    [Fact]
    public void A_scan_that_finished_was_not_canceled()
    {
        _box.NewFolder("photos");

        Assert.False(Preview(1, Named("photos")).WasCanceled);
    }

    [Fact]
    public void One_run_with_several_looks_undoes_as_one_step()
    {
        var a = _box.NewFolder("alpha");
        var b = _box.NewFolder("beta");

        var preview = Preview(1, Named("alpha", BlueId), Named("beta", GreenId));
        var looks = preview.Changes.Select(row => new FolderLook(row.FolderPath, row.Theme!.Spec)).ToArray();

        var batch = _box.Customizer().ApplyEach(looks);

        Assert.Equal(2, batch.AppliedCount);
        Assert.Single(_box.Journal.Read().Entries.Select(e => e.Change.BatchId).Distinct());

        Assert.True(_box.Restorer().UndoLast().AllRestored);
        Assert.False(File.Exists(Sandbox.IniPath(a)));
        Assert.False(File.Exists(Sandbox.IniPath(b)));
    }

    [Fact]
    public void A_look_that_cannot_be_drawn_only_fails_its_own_folders()
    {
        var good = _box.NewFolder("good");
        var bad = _box.NewFolder("bad");
        var broken = Sandbox.BlueStar with { GlyphId = "nonsense" };

        var batch = _box.Customizer().ApplyEach([new FolderLook(bad, broken), new FolderLook(good, Sandbox.BlueStar)]);

        Assert.Equal(ApplyOutcome.Failed, batch.Results[0].Outcome);
        Assert.Equal(ApplyOutcome.Applied, batch.Results[1].Outcome);
    }

    [Fact]
    public void A_folder_that_turned_into_a_junction_after_the_preview_is_still_refused()
    {
        var folder = _box.NewFolder("photos");
        var preview = Preview(1, Named("photos"));
        Directory.Delete(folder);
        Sandbox.CreateJunction(folder, _box.NewFolder("elsewhere"));

        var batch = _box.Customizer().ApplyEach(preview.Changes.Select(row => new FolderLook(row.FolderPath, row.Theme!.Spec)).ToArray());

        Assert.Equal(ApplyOutcome.Refused, Assert.Single(batch.Results).Outcome);
    }
}
