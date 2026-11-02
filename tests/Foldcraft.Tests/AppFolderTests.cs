using System.Drawing;
using System.Text;
using Foldcraft.App.Services;
using Foldcraft.Core;
using Foldcraft.Core.Apps;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Safety;

namespace Foldcraft.Tests;

public sealed class FolderNamesTests
{
    [Theory]
    [InlineData("Plain Name", "Plain Name")]
    [InlineData("Half: The Sequel", "Half The Sequel")]
    [InlineData("a<b>c:d\"e/f\\g|h?i*j", "a b c d e f g h i j")]
    [InlineData("Trailing dots...", "Trailing dots")]
    [InlineData("Trailing spaces   ", "Trailing spaces")]
    [InlineData("Dots and spaces . . .", "Dots and spaces")]
    [InlineData("   Leading", "Leading")]
    [InlineData("Too    many   spaces", "Too many spaces")]
    [InlineData("Tabs\tand\nnew lines", "Tabsandnew lines")]
    [InlineData("Marks\u00AE and\u2122 more\u00A9", "Marks and more")]
    [InlineData("Caf\u00E9 \u6771\u4EAC", "Caf\u00E9 \u6771\u4EAC")]
    [InlineData("Emoji \U0001F3AE game", "Emoji \U0001F3AE game")]
    [InlineData("Zero\u200Bwidth", "Zerowidth")]
    public void Names_are_made_safe(string input, string expected)
    {
        Assert.Equal(expected, FolderNames.Sanitize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(". . .")]
    [InlineData("???")]
    [InlineData("\t\r\n")]
    public void A_name_with_nothing_left_still_gets_a_name(string? input)
    {
        Assert.Equal("App", FolderNames.Sanitize(input));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    [InlineData("COM\u00B9")]
    [InlineData("CONIN$")]
    public void Reserved_device_names_are_changed(string name)
    {
        var safe = FolderNames.Sanitize(name);

        Assert.NotEqual(name, safe, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith(name, safe, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("CON.txt", "CON folder.txt")]
    [InlineData("NUL.tar.gz", "NUL folder.tar.gz")]
    [InlineData("CON .txt", "CON folder .txt")]
    public void A_reserved_name_with_an_extension_is_changed_too(string name, string expected)
    {
        Assert.Equal(expected, FolderNames.Sanitize(name));
    }

    [Theory]
    [InlineData("Console")]
    [InlineData("COM10")]
    [InlineData("LPT")]
    [InlineData("Auxiliary")]
    public void Names_that_only_start_like_a_device_are_left_alone(string name)
    {
        Assert.Equal(name, FolderNames.Sanitize(name));
    }

    [Fact]
    public void A_long_name_is_cut_to_the_limit()
    {
        Assert.Equal(FolderNames.MaxLength, FolderNames.Sanitize(new string('x', 500)).Length);
    }

    [Fact]
    public void Cutting_never_splits_a_character_in_two()
    {
        var name = new string('x', FolderNames.MaxLength - 1) + "\U0001F3AE";

        var safe = FolderNames.Sanitize(name);

        Assert.False(char.IsHighSurrogate(safe[^1]));
        Assert.Equal(FolderNames.MaxLength - 1, safe.Length);
    }

    [Fact]
    public void Cutting_never_leaves_a_trailing_dot_or_space()
    {
        var name = new string('x', FolderNames.MaxLength - 1) + ". more";

        Assert.DoesNotContain(FolderNames.Sanitize(name)[^1], ". ");
    }

    [Fact]
    public void A_broken_surrogate_is_dropped()
    {
        Assert.Equal("ab", FolderNames.Sanitize("a\uD83Db"));
    }

    [Theory]
    [InlineData("Name", 2, "Name (2)")]
    [InlineData("Name", 12, "Name (12)")]
    public void A_number_goes_on_the_end(string name, int number, string expected)
    {
        Assert.Equal(expected, FolderNames.WithNumber(name, number));
    }

    [Fact]
    public void A_number_still_fits_the_limit()
    {
        Assert.True(FolderNames.WithNumber(new string('x', FolderNames.MaxLength), 99).Length <= FolderNames.MaxLength);
    }
}

public sealed class AppFolderCreatorTests : IDisposable
{
    private readonly Sandbox _box = new();
    private readonly ShellShortcuts _shortcuts = new();
    private readonly string _games;

    public AppFolderCreatorTests()
    {
        _games = _box.NewFolder("Games");
    }

    public void Dispose() => _box.Dispose();

    private AppFolderCreator Creator(FileOperations? ops = null, IShortcuts? shortcuts = null)
    {
        var customizer = _box.Customizer(ops);
        return new AppFolderCreator(_box.Paths, _box.Safety, customizer, _box.Icons, shortcuts ?? _shortcuts, new AppArtStore(_box.Paths));
    }

    private AppFolderBatch Create(params AppFolderRequest[] requests) =>
        Creator().Create(requests, new AppFolderOptions(_games, NameClashChoice.AddNumber));

    private AppFolderResult CreateOne(string name = "Sample App") =>
        Create(SampleApps.Request(SampleApps.Program(name))).Results[0];

    // Creating

    [Fact]
    public void A_new_folder_gets_its_look_and_a_shortcut_that_starts_the_app()
    {
        var result = CreateOne();

        Assert.Equal(AppFolderOutcome.Created, result.Outcome);
        Assert.Equal(Path.Combine(_games, "Sample App"), result.FolderPath);
        Assert.True(File.Exists(Sandbox.IniPath(result.FolderPath!)));

        var link = _shortcuts.Read(Path.Combine(result.FolderPath!, "Sample App.lnk"));

        Assert.Equal(@"C:\Apps\Sample\sample.exe", link?.Target);
        Assert.Equal("--start", link?.Arguments);
        Assert.Equal(@"C:\Apps\Sample", link?.WorkingDirectory);
    }

    [Fact]
    public void Explorer_is_told_to_read_the_new_folders_look()
    {
        var folder = CreateOne().FolderPath!;

        Assert.True((File.GetAttributes(folder) & FileAttributes.ReadOnly) != 0);
        Assert.True((File.GetAttributes(Sandbox.IniPath(folder)) & ChangeRecord.DesktopIniMarks) == ChangeRecord.DesktopIniMarks);
        Assert.Contains(folder, _box.Shell.Notified);
    }

    [Fact]
    public void A_program_with_many_icons_keeps_the_right_one()
    {
        var app = SampleApps.Program("Picker") with { Icons = [new IconCandidate(@"C:\Apps\icons.dll", 7)] };

        var folder = Create(SampleApps.Request(app)).Results[0].FolderPath!;
        var link = _shortcuts.Read(Path.Combine(folder, "Picker.lnk"));

        Assert.Equal(@"C:\Apps\icons.dll", link?.IconPath);
        Assert.Equal(7, link?.IconIndex);
    }

    [Fact]
    public void An_internet_shortcut_shows_the_games_own_icon_file()
    {
        var game = SampleApps.SteamGame() with { Icons = [new IconCandidate(@"C:\Launcher\games\abc.ico")] };

        var text = File.ReadAllText(Path.Combine(Create(SampleApps.Request(game)).Results[0].FolderPath!, "Sample Game.url"));

        Assert.Contains("IconFile=C:\\Launcher\\games\\abc.ico\r\n", text);
    }

    [Fact]
    public void An_icon_path_the_format_cannot_carry_is_left_out()
    {
        var game = SampleApps.SteamGame() with { Icons = [new IconCandidate("C:\\L\u00E4uncher\\abc.ico")] };

        var text = File.ReadAllText(Path.Combine(Create(SampleApps.Request(game)).Results[0].FolderPath!, "Sample Game.url"));

        Assert.DoesNotContain("IconFile", text);
    }

    [Fact]
    public void A_steam_game_gets_an_internet_shortcut_with_its_address()
    {
        var folder = Create(SampleApps.Request(SampleApps.SteamGame())).Results[0].FolderPath!;

        var text = File.ReadAllText(Path.Combine(folder, "Sample Game.url"));

        Assert.StartsWith("[InternetShortcut]\r\n", text);
        Assert.Contains("URL=steam://rungameid/4242\r\n", text);
    }

    [Fact]
    public void A_program_that_moves_on_update_starts_through_its_own_shortcut()
    {
        var app = SampleApps.Program("Mover") with { Launch = new AppLaunch(LaunchKind.OtherShortcut, @"C:\Menu\Mover.lnk") };

        var folder = Create(SampleApps.Request(app)).Results[0].FolderPath!;
        var link = _shortcuts.Read(Path.Combine(folder, "Mover.lnk"));

        Assert.Equal("explorer.exe", Path.GetFileName(link?.Target), ignoreCase: true);
        Assert.Equal("\"C:\\Menu\\Mover.lnk\"", link?.Arguments);
        Assert.EndsWith(".ico", link?.IconPath);
    }

    [Theory]
    [InlineData("steam://rungameid/1\r\nIconFile=\\\\evil\\share\\a.ico")]
    [InlineData("steam://rungameid/abc")]
    [InlineData("http://example.com/")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    public void An_address_that_is_not_a_launchers_own_form_is_never_written(string address)
    {
        var app = SampleApps.SteamGame() with { Launch = new AppLaunch(LaunchKind.Address, address) };

        var result = Create(SampleApps.Request(app)).Results[0];

        Assert.Equal(AppFolderOutcome.Failed, result.Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(_games));
    }

    [Fact]
    public void An_app_name_is_made_safe_before_it_becomes_a_folder()
    {
        var folder = CreateOne("Trouble: The <Game>...").FolderPath!;

        Assert.Equal("Trouble The Game", Path.GetFileName(folder));
        Assert.True(File.Exists(Path.Combine(folder, "Trouble The Game.lnk")));
    }

    [Fact]
    public void A_name_that_tries_to_climb_out_stays_inside_the_destination()
    {
        var folder = CreateOne(@"..\..\Elsewhere").FolderPath!;

        Assert.Equal(_games, Path.GetDirectoryName(folder));
    }

    [Fact]
    public void A_reserved_name_becomes_a_folder_that_really_exists()
    {
        var folder = CreateOne("CON").FolderPath!;

        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void Art_stays_in_the_data_folder_and_the_look_records_it()
    {
        CreateOne();

        var entry = _box.Journal.Read().Entries.Single();

        Assert.Single(Directory.GetFiles(_box.Paths.AppArtDirectory, "*.png"));
        Assert.Contains(_box.Paths.AppArtDirectory.Replace(@"\", @"\\"), entry.Change.SpecJson);
    }

    [Fact]
    public void The_scratch_folder_is_left_empty()
    {
        CreateOne();

        Assert.Empty(Directory.GetFileSystemEntries(_box.Paths.ScratchDirectory));
    }

    [Fact]
    public void The_creation_is_recorded_with_what_was_made()
    {
        var folder = CreateOne().FolderPath!;

        var entry = _box.Journal.Read().Entries.Single();
        var file = entry.Change.CreatedFiles.Single();

        Assert.True(entry.Change.CreatedFolder);
        Assert.Equal(JournalEntryState.Applied, entry.State);
        Assert.Equal("Sample App.lnk", file.Name);
        Assert.Equal(FolderState.Sha256Hex(File.ReadAllBytes(Path.Combine(folder, file.Name))), file.Sha256);
        Assert.Equal(FolderIdentity.Read(folder), entry.Change.FolderId);
    }

    [Fact]
    public void One_run_is_one_undo_step()
    {
        var batch = Create(SampleApps.Request(SampleApps.Program("One")), SampleApps.Request(SampleApps.Program("Two")));

        Assert.All(_box.Journal.Read().Entries, entry => Assert.Equal(batch.BatchId, entry.Change.BatchId));
    }

    [Fact]
    public void Canceling_leaves_the_rest_undone()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new SyncProgress<AppFolderResult>(_ => cancellation.Cancel());

        var batch = Creator().Create(
            [SampleApps.Request(SampleApps.Program("One")), SampleApps.Request(SampleApps.Program("Two"))],
            new AppFolderOptions(_games, NameClashChoice.AddNumber),
            cancellation.Token,
            progress);

        Assert.Equal(AppFolderOutcome.Created, batch.Results[0].Outcome);
        Assert.Equal(AppFolderOutcome.Canceled, batch.Results[1].Outcome);
        Assert.False(Directory.Exists(Path.Combine(_games, "Two")));
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    // Clashes

    [Fact]
    public void Skipping_a_taken_name_leaves_what_is_there_untouched()
    {
        var existing = Path.Combine(_games, "Sample App");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "mine.txt"), "mine");
        var date = Directory.GetLastWriteTimeUtc(existing);

        var batch = Creator().Create(
            [SampleApps.Request(SampleApps.Program())],
            new AppFolderOptions(_games, NameClashChoice.Skip));

        Assert.Equal(AppFolderOutcome.NameTaken, batch.Results[0].Outcome);
        Assert.Equal(["mine.txt"], Directory.GetFiles(existing).Select(Path.GetFileName));
        Assert.Equal(date, Directory.GetLastWriteTimeUtc(existing));
        Assert.Empty(_box.Journal.Read().Entries);
    }

    [Fact]
    public void Adding_a_number_makes_a_new_folder_beside_the_old_one()
    {
        var existing = Path.Combine(_games, "Sample App");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "mine.txt"), "mine");

        var result = CreateOne();

        Assert.Equal(Path.Combine(_games, "Sample App (2)"), result.FolderPath);
        Assert.Equal(["mine.txt"], Directory.GetFiles(existing).Select(Path.GetFileName));
    }

    [Fact]
    public void The_shortcut_inside_a_numbered_folder_keeps_the_plain_name()
    {
        Directory.CreateDirectory(Path.Combine(_games, "Sample App"));

        var result = CreateOne();

        Assert.True(File.Exists(Path.Combine(result.FolderPath!, "Sample App.lnk")));
    }

    [Fact]
    public void Numbers_keep_counting_past_names_that_are_taken()
    {
        Directory.CreateDirectory(Path.Combine(_games, "Sample App"));
        Directory.CreateDirectory(Path.Combine(_games, "Sample App (2)"));

        Assert.Equal("Sample App (3)", Path.GetFileName(CreateOne().FolderPath));
    }

    [Fact]
    public void A_file_with_that_name_counts_as_taken()
    {
        File.WriteAllText(Path.Combine(_games, "Sample App"), "a file, not a folder");

        Assert.Equal("Sample App (2)", Path.GetFileName(CreateOne().FolderPath));
        Assert.Equal("a file, not a folder", File.ReadAllText(Path.Combine(_games, "Sample App")));
    }

    [Fact]
    public void A_dangling_link_with_that_name_counts_as_taken()
    {
        var target = Path.Combine(_games, "gone");
        Directory.CreateDirectory(target);
        Sandbox.CreateJunction(Path.Combine(_games, "Sample App"), target);
        Directory.Delete(target);

        Assert.Equal("Sample App (2)", Path.GetFileName(CreateOne().FolderPath));
    }

    [Fact]
    public void Two_apps_with_one_name_in_a_run_both_get_folders()
    {
        var batch = Create(
            SampleApps.Request(SampleApps.Program("Twin", @"C:\One\twin.exe")),
            SampleApps.Request(SampleApps.Program("Twin", @"C:\Two\twin.exe")));

        Assert.Equal(["Twin", "Twin (2)"], batch.Results.Select(r => Path.GetFileName(r.FolderPath)));
    }

    [Fact]
    public void A_folder_that_appears_at_the_last_moment_is_not_taken_over()
    {
        var existing = Path.Combine(_games, "Sample App");
        var ops = new AppearingFolderOperations(existing);

        var result = Creator(ops).Create(
            [SampleApps.Request(SampleApps.Program())],
            new AppFolderOptions(_games, NameClashChoice.AddNumber)).Results[0];

        Assert.Equal(Path.Combine(_games, "Sample App (2)"), result.FolderPath);
        Assert.Equal(["theirs.txt"], Directory.GetFileSystemEntries(existing).Select(Path.GetFileName));
        Assert.Equal(JournalEntryState.RolledBack, _box.Journal.Read().Entries[0].State);
    }

    [Fact]
    public void A_failure_making_the_folder_never_cleans_up_someone_elses()
    {
        var existing = Path.Combine(_games, "Sample App");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "desktop.ini"), "theirs");

        var result = _box.Customizer(new FailingFolderOperations()).CreateFolder(existing, [], Sandbox.BlueStar, Guid.NewGuid());

        Assert.Equal(ApplyOutcome.Failed, result.Outcome);
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(existing, "desktop.ini")));
        Assert.Equal(JournalEntryState.RolledBack, _box.Journal.Read().Entries.Single().State);
    }

    private sealed class FailingFolderOperations : FileOperations
    {
        public override bool CreateNewDirectory(string path) => throw new UnauthorizedAccessException("Simulated folder failure.");
    }

    /// <summary>Another program makes the folder just after Foldcraft checked the name was free.</summary>
    private sealed class AppearingFolderOperations(string path) : FileOperations
    {
        public override bool CreateNewDirectory(string candidate)
        {
            if (candidate == path)
            {
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "theirs.txt"), "theirs");
            }

            return base.CreateNewDirectory(candidate);
        }
    }

    // Where folders may go

    [Fact]
    public void A_protected_destination_is_refused_and_nothing_is_made()
    {
        var batch = Creator().Create(
            [SampleApps.Request(SampleApps.Program())],
            new AppFolderOptions(_box.FakeAppData, NameClashChoice.AddNumber));

        Assert.Equal(AppFolderOutcome.Refused, batch.Results[0].Outcome);
        Assert.Equal(FolderRefusal.AppData, batch.Results[0].Refusal);
        Assert.Empty(Directory.GetFileSystemEntries(_box.FakeAppData));
        Assert.Empty(_box.Journal.Read().Entries);
    }

    [Fact]
    public void A_destination_reached_through_a_junction_is_refused()
    {
        var link = Sandbox.CreateJunction(Path.Combine(_box.Root, "linked"), _games);

        var batch = Creator().Create([SampleApps.Request(SampleApps.Program())], new AppFolderOptions(link, NameClashChoice.AddNumber));

        Assert.Equal(FolderRefusal.ReparsePoint, batch.Results[0].Refusal);
    }

    [Fact]
    public void A_destination_that_does_not_exist_is_refused()
    {
        var batch = Creator().Create(
            [SampleApps.Request(SampleApps.Program())],
            new AppFolderOptions(Path.Combine(_box.Root, "absent"), NameClashChoice.AddNumber));

        Assert.Equal(FolderRefusal.NotFound, batch.Results[0].Refusal);
    }

    [Fact]
    public void Foldcrafts_own_data_folder_is_refused()
    {
        var batch = Creator().Create([SampleApps.Request(SampleApps.Program())], new AppFolderOptions(_box.Paths.DataDirectory, NameClashChoice.AddNumber));

        Assert.Equal(FolderRefusal.FoldcraftData, batch.Results[0].Refusal);
    }

    [Fact]
    public void A_whole_drive_is_a_fine_place_for_new_folders_but_still_cannot_be_customized()
    {
        var drive = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.Equal(FolderRefusal.DriveRoot, _box.Safety.Check(drive).Refusal);
        Assert.True(_box.Safety.CheckDestination(drive).IsAllowed);
    }

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData("relative")]
    [InlineData("")]
    public void A_destination_that_is_not_a_local_folder_is_refused(string destination)
    {
        Assert.False(_box.Safety.CheckDestination(destination).IsAllowed);
    }

    [Fact]
    public void A_drive_letter_with_nothing_behind_it_is_refused()
    {
        var unused = Enumerable.Range('D', 23).Select(letter => $"{(char)letter}:\\").First(root => !Directory.Exists(root));

        Assert.False(_box.Safety.CheckDestination(unused).IsAllowed);
    }

    [Fact]
    public void A_destination_inside_a_new_folder_of_ours_is_just_another_folder()
    {
        var first = CreateOne("First").FolderPath!;

        var second = Creator().Create([SampleApps.Request(SampleApps.Program("Second"))], new AppFolderOptions(first, NameClashChoice.Skip));

        Assert.Equal(AppFolderOutcome.Created, second.Results[0].Outcome);
    }

    // Failures

    [Fact]
    public void A_failure_writing_the_shortcut_takes_the_folder_away_again()
    {
        var ops = new FaultyOperations { FailWrite = path => path.EndsWith(".lnk", StringComparison.Ordinal) };

        var result = Creator(ops).Create([SampleApps.Request(SampleApps.Program())], new AppFolderOptions(_games, NameClashChoice.AddNumber)).Results[0];

        Assert.Equal(AppFolderOutcome.Failed, result.Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(_games));
        Assert.Equal(JournalEntryState.RolledBack, _box.Journal.Read().Entries.Single().State);
    }

    [Fact]
    public void A_failure_writing_the_look_takes_everything_away_again()
    {
        var ops = new FaultyOperations { FailCommit = path => path.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase) };

        var result = Creator(ops).Create([SampleApps.Request(SampleApps.Program())], new AppFolderOptions(_games, NameClashChoice.AddNumber)).Results[0];

        Assert.Equal(AppFolderOutcome.Failed, result.Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(_games));
    }

    [Fact]
    public void A_failure_marking_the_folder_takes_everything_away_again()
    {
        var ops = new FaultyOperations
        {
            FailSetAttributes = (path, attributes) => (attributes & FileAttributes.ReadOnly) != 0 && Directory.Exists(path),
        };

        var result = Creator(ops).Create([SampleApps.Request(SampleApps.Program())], new AppFolderOptions(_games, NameClashChoice.AddNumber)).Results[0];

        Assert.Equal(AppFolderOutcome.Failed, result.Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(_games));
    }

    [Fact]
    public void A_rollback_that_also_fails_says_so_and_undo_can_finish_it()
    {
        var failDeletes = true;

        var ops = new FaultyOperations
        {
            FailCommit = path => path.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase),
            FailDelete = _ => failDeletes,
        };

        var result = Creator(ops).Create([SampleApps.Request(SampleApps.Program())], new AppFolderOptions(_games, NameClashChoice.AddNumber)).Results[0];

        Assert.Equal(AppFolderOutcome.LeftUnfinished, result.Outcome);
        Assert.Equal(JournalEntryState.Pending, _box.Journal.Read().Entries.Single().State);

        failDeletes = false;
        _box.Restorer().UndoLast();

        Assert.Empty(Directory.GetFileSystemEntries(_games));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void One_failing_app_does_not_stop_the_next()
    {
        var ops = new FaultyOperations { FailWrite = path => path.EndsWith("Bad.lnk", StringComparison.Ordinal) };

        var batch = Creator(ops).Create(
            [SampleApps.Request(SampleApps.Program("Bad")), SampleApps.Request(SampleApps.Program("Good"))],
            new AppFolderOptions(_games, NameClashChoice.AddNumber));

        Assert.Equal(AppFolderOutcome.Failed, batch.Results[0].Outcome);
        Assert.Equal(AppFolderOutcome.Created, batch.Results[1].Outcome);
    }

    [Fact]
    public void A_journal_that_cannot_be_written_means_nothing_is_made()
    {
        var blocked = new Sandbox();

        try
        {
            Directory.CreateDirectory(blocked.Paths.JournalFile);

            var creator = new AppFolderCreator(blocked.Paths, _box.Safety, new FolderCustomizer(_box.Safety, blocked.Journal, blocked.Icons, _box.Shell), blocked.Icons, _shortcuts, new AppArtStore(blocked.Paths));

            var result = creator.Create([SampleApps.Request(SampleApps.Program())], new AppFolderOptions(_games, NameClashChoice.AddNumber)).Results[0];

            Assert.Equal(AppFolderOutcome.Failed, result.Outcome);
            Assert.Empty(Directory.GetFileSystemEntries(_games));
        }
        finally
        {
            blocked.Dispose();
        }
    }

    [Fact]
    public void A_shortcut_that_cannot_be_built_makes_nothing()
    {
        var result = Creator(shortcuts: new FailingShortcuts()).Create(
            [SampleApps.Request(SampleApps.Program())],
            new AppFolderOptions(_games, NameClashChoice.AddNumber)).Results[0];

        Assert.Equal(AppFolderOutcome.Failed, result.Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(_games));
        Assert.Empty(_box.Journal.Read().Entries);
    }

    private sealed class FailingShortcuts : IShortcuts
    {
        public ShortcutInfo? Read(string path) => null;

        public byte[] Build(ShortcutInfo shortcut, string scratchDirectory) => throw new IOException("Simulated shortcut failure.");
    }

    // Undo

    [Fact]
    public void Undo_removes_exactly_what_was_made()
    {
        var folder = CreateOne().FolderPath!;

        var report = _box.Restorer().UndoLast();

        Assert.Equal(RestoreOutcome.Restored, report.Results[0].Outcome);
        Assert.False(report.Results[0].FolderKept);
        Assert.False(Directory.Exists(folder));
        Assert.Empty(Directory.GetFileSystemEntries(_games));
    }

    [Fact]
    public void Undo_takes_a_whole_run_in_one_step()
    {
        Create(SampleApps.Request(SampleApps.Program("One")), SampleApps.Request(SampleApps.Program("Two")), SampleApps.Request(SampleApps.SteamGame()));

        _box.Restorer().UndoLast();

        Assert.Empty(Directory.GetFileSystemEntries(_games));
    }

    [Fact]
    public void If_something_was_added_the_folder_stays_with_its_shortcut_and_only_the_look_goes()
    {
        var folder = CreateOne().FolderPath!;
        var added = Path.Combine(folder, "my notes.txt");

        File.SetAttributes(folder, FileAttributes.Directory);
        File.WriteAllText(added, "mine");

        var report = _box.Restorer().UndoLast();

        Assert.Equal(RestoreOutcome.Restored, report.Results[0].Outcome);
        Assert.True(report.Results[0].FolderKept);
        Assert.Contains("stayed", report.Results[0].Message);
        Assert.Equal("mine", File.ReadAllText(added));
        Assert.True(File.Exists(Path.Combine(folder, "Sample App.lnk")));
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.Equal(0, (int)(File.GetAttributes(folder) & FileAttributes.ReadOnly));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void An_added_subfolder_counts_too()
    {
        var folder = CreateOne().FolderPath!;

        Directory.CreateDirectory(Path.Combine(folder, "saves"));

        Assert.True(_box.Restorer().UndoLast().Results[0].FolderKept);
        Assert.True(Directory.Exists(Path.Combine(folder, "saves")));
    }

    [Fact]
    public void An_added_hidden_file_counts_too()
    {
        var folder = CreateOne().FolderPath!;
        var hidden = Path.Combine(folder, "secret");

        File.WriteAllText(hidden, "mine");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);

        Assert.True(_box.Restorer().UndoLast().Results[0].FolderKept);
        Assert.True(File.Exists(hidden));
    }

    [Fact]
    public void A_shortcut_someone_edited_is_not_removed()
    {
        var folder = CreateOne().FolderPath!;
        var link = Path.Combine(folder, "Sample App.lnk");

        File.AppendAllText(link, "edited");

        Assert.True(_box.Restorer().UndoLast().Results[0].FolderKept);
        Assert.True(File.Exists(link));
    }

    [Fact]
    public void A_shortcut_someone_renamed_keeps_the_folder()
    {
        var folder = CreateOne().FolderPath!;

        File.Move(Path.Combine(folder, "Sample App.lnk"), Path.Combine(folder, "Renamed.lnk"));

        Assert.True(_box.Restorer().UndoLast().Results[0].FolderKept);
        Assert.True(File.Exists(Path.Combine(folder, "Renamed.lnk")));
    }

    [Fact]
    public void A_shortcut_someone_deleted_does_not_stop_the_folder_going()
    {
        var folder = CreateOne().FolderPath!;

        File.Delete(Path.Combine(folder, "Sample App.lnk"));
        _box.Restorer().UndoLast();

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void A_folder_that_was_deleted_by_hand_is_reported_as_missing()
    {
        var folder = CreateOne().FolderPath!;

        File.SetAttributes(folder, FileAttributes.Directory);
        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        Directory.Delete(folder, recursive: true);

        Assert.Equal(RestoreOutcome.FolderMissing, _box.Restorer().UndoLast().Results[0].Outcome);
    }

    [Fact]
    public void A_different_folder_at_that_path_is_left_alone_even_when_empty()
    {
        var folder = CreateOne().FolderPath!;

        File.SetAttributes(folder, FileAttributes.Directory);
        File.SetAttributes(Sandbox.IniPath(folder), FileAttributes.Normal);
        Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        var result = _box.Restorer().UndoLast().Results[0];

        Assert.True(Directory.Exists(folder));
        Assert.True(result.Outcome is RestoreOutcome.Restored or RestoreOutcome.AlreadyRestored);
        Assert.True(result.FolderKept);
    }

    [Fact]
    public void A_look_changed_by_another_app_is_a_conflict_and_nothing_is_removed()
    {
        var folder = CreateOne().FolderPath!;

        AppHarness.EditIni(folder);

        var report = _box.Restorer().UndoLast();

        Assert.Equal(RestoreOutcome.Conflict, report.Results[0].Outcome);
        Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(Path.Combine(folder, "Sample App.lnk")));
    }

    [Fact]
    public void Choosing_to_overwrite_that_conflict_then_removes_the_folder()
    {
        var folder = CreateOne().FolderPath!;

        AppHarness.EditIni(folder);
        _box.Restorer().UndoLast(ConflictPolicy.Overwrite);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Choosing_to_keep_that_conflict_leaves_the_folder_and_stops_tracking()
    {
        var folder = CreateOne().FolderPath!;

        AppHarness.EditIni(folder);
        _box.Restorer().UndoLast(ConflictPolicy.Forget);

        Assert.True(Directory.Exists(folder));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void A_look_applied_on_top_undoes_first_and_the_folder_stays_until_its_own_undo()
    {
        var folder = CreateOne().FolderPath!;

        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        _box.Restorer().UndoLast();
        Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(Sandbox.IniPath(folder)));

        _box.Restorer().UndoLast();
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Undoing_the_creation_also_takes_a_look_applied_on_top()
    {
        var created = Create(SampleApps.Request(SampleApps.Program())).BatchId;
        var folder = Path.Combine(_games, "Sample App");

        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        _box.Restorer().UndoBatch(created);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Restore_all_removes_created_folders()
    {
        var folder = CreateOne().FolderPath!;

        Assert.True(_box.Restorer().RestoreAll().AllRestored);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Restoring_one_folder_by_path_removes_it()
    {
        var folder = CreateOne().FolderPath!;

        _box.Restorer().RestoreFolder(folder);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Undoing_again_is_harmless()
    {
        CreateOne();
        var restorer = _box.Restorer();

        restorer.UndoLast();

        Assert.Empty(restorer.UndoLast().Results);
    }

    [Fact]
    public void A_folder_that_was_moved_can_be_found_and_still_undone()
    {
        var folder = CreateOne().FolderPath!;
        var moved = Path.Combine(_games, "Moved");

        File.SetAttributes(folder, FileAttributes.Directory);
        Directory.Move(folder, moved);

        Assert.Equal(RestoreOutcome.FolderMissing, _box.Restorer().UndoLast().Results[0].Outcome);
        Assert.Equal(RestoreOutcome.Relocated, _box.Restorer().Relocate(folder, moved).Outcome);

        _box.Restorer().UndoLast();

        Assert.False(Directory.Exists(moved));
    }

    [Fact]
    public void A_journal_with_a_tampered_file_name_cannot_delete_anything_else()
    {
        var folder = _box.NewFolder("Precious");
        var precious = Path.Combine(_box.Root, "precious.txt");
        File.WriteAllText(precious, "keep me");

        var change = new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = folder,
            DesktopIniExisted = false,
            OriginalFolderAttributes = FileAttributes.Directory,
            IconPath = "x.ico",
            WrittenSha256 = "x",
            CreatedFolder = true,
            CreatedFiles = [new CreatedFile(@"..\..\precious.txt", FolderState.Sha256Hex(File.ReadAllBytes(precious)))],
        };

        File.WriteAllText(Path.Combine(folder, "other.txt"), "something else");

        Assert.Equal(CreatedContentFate.Kept, CreatedContent.Remove(change, new FileOperations()));
        Assert.True(File.Exists(precious));
    }

    // Crashes

    private ChangeRecord PendingChange(string folder) => new()
    {
        BatchId = Guid.NewGuid(),
        FolderPath = folder,
        DesktopIniExisted = false,
        OriginalFolderAttributes = FileAttributes.Directory,
        IconPath = Path.Combine(_box.Paths.IconsDirectory, "x.ico"),
        WrittenSha256 = "not-written",
        CreatedFolder = true,
        CreatedFiles = [new CreatedFile("App.lnk", FolderState.Sha256Hex(Encoding.ASCII.GetBytes("shortcut")))],
    };

    [Fact]
    public void A_crash_before_the_folder_was_made_undoes_to_nothing()
    {
        _box.Journal.RecordChange(PendingChange(Path.Combine(_games, "Never Made")));

        var report = _box.Restorer().UndoLast();

        Assert.Equal(RestoreOutcome.AlreadyRestored, report.Results[0].Outcome);
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void A_crash_after_the_folder_was_made_undoes_cleanly()
    {
        var folder = Path.Combine(_games, "Half Made");
        Directory.CreateDirectory(folder);
        _box.Journal.RecordChange(PendingChange(folder) with { FolderId = FolderIdentity.Read(folder) });

        _box.Restorer().UndoLast();

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void A_crash_before_the_folder_was_made_leaves_an_empty_folder_of_that_name_alone()
    {
        var folder = Path.Combine(_games, "Half Made");
        Directory.CreateDirectory(folder);
        _box.Journal.RecordChange(PendingChange(folder));

        var result = _box.Restorer().UndoLast().Results[0];

        Assert.True(Directory.Exists(folder));
        Assert.DoesNotContain("Removed", PlainEnglish.RestoreResult(result));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Theory]
    [InlineData(ConflictPolicy.Report)]
    [InlineData(ConflictPolicy.Overwrite)]
    public void A_crash_before_the_folder_was_made_never_costs_someones_settings_file(ConflictPolicy policy)
    {
        var folder = Path.Combine(_games, "Half Made");
        var theirs = Encoding.ASCII.GetBytes("[.ShellClassInfo]\r\nInfoTip=Theirs\r\n");

        Directory.CreateDirectory(folder);
        Sandbox.PlantIni(folder, theirs);
        _box.Journal.RecordChange(PendingChange(folder));

        _box.Restorer().UndoLast(policy);

        Assert.Equal(theirs, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void A_crash_then_a_look_on_someones_folder_of_that_name_still_undoes()
    {
        var folder = Path.Combine(_games, "Half Made");
        Directory.CreateDirectory(folder);
        _box.Journal.RecordChange(PendingChange(folder));
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.True(_box.Restorer().RestoreAll().AllRestored);

        Assert.True(Directory.Exists(folder));
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void With_no_id_on_record_the_settings_file_foldcraft_wrote_shows_the_folder_is_its_own()
    {
        var folder = Path.Combine(_games, "Half Made");
        var written = Encoding.ASCII.GetBytes("[.ShellClassInfo]\r\n");

        Directory.CreateDirectory(folder);
        Sandbox.PlantIni(folder, written);
        _box.Journal.RecordChange(PendingChange(folder) with { WrittenSha256 = FolderState.Sha256Hex(written) });

        _box.Restorer().UndoLast();

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void A_crash_after_the_shortcut_was_written_undoes_cleanly()
    {
        var folder = Path.Combine(_games, "Half Made");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "App.lnk"), "shortcut");
        _box.Journal.RecordChange(PendingChange(folder));

        _box.Restorer().UndoLast();

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void A_crash_that_left_a_temp_file_undoes_cleanly()
    {
        var folder = Path.Combine(_games, "Half Made");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $".foldcraft-{Guid.NewGuid():N}.tmp"), "half a file");
        _box.Journal.RecordChange(PendingChange(folder));

        _box.Restorer().UndoLast();

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void A_crash_then_someone_elses_file_in_the_folder_keeps_it()
    {
        var folder = Path.Combine(_games, "Half Made");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "theirs.txt"), "theirs");
        _box.Journal.RecordChange(PendingChange(folder));

        var result = _box.Restorer().UndoLast().Results[0];

        Assert.True(result.FolderKept);
        Assert.True(File.Exists(Path.Combine(folder, "theirs.txt")));
    }

    [Fact]
    public void Older_journal_records_read_as_ordinary_changes()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        var change = _box.Journal.Read().Entries.Single().Change;

        Assert.False(change.CreatedFolder);
        Assert.Empty(change.CreatedFiles);
        Assert.DoesNotContain("\"created\"", File.ReadAllText(_box.Paths.JournalFile));
    }

    // Existing folders

    [Fact]
    public void An_existing_folder_can_take_the_look()
    {
        var library = _box.NewFolder("Library");
        File.WriteAllText(Path.Combine(library, "game.dat"), "data");

        var result = Creator().ThemeExisting(SampleApps.Request(SampleApps.Program()), library);

        Assert.Equal(AppFolderOutcome.Themed, result.Outcome);
        Assert.True(File.Exists(Sandbox.IniPath(library)));
        Assert.True(File.Exists(Path.Combine(library, "game.dat")));
    }

    [Fact]
    public void Undoing_a_look_on_an_existing_folder_never_removes_the_folder()
    {
        var library = _box.NewFolder("Library");

        Creator().ThemeExisting(SampleApps.Request(SampleApps.Program()), library);
        _box.Restorer().UndoLast();

        Assert.True(Directory.Exists(library));
        Assert.False(File.Exists(Sandbox.IniPath(library)));
    }

    [Fact]
    public void Theming_an_existing_folder_still_goes_through_the_safety_check()
    {
        var protectedFolder = Path.Combine(_box.FakeAppData, "Launcher");
        Directory.CreateDirectory(protectedFolder);

        var result = Creator().ThemeExisting(SampleApps.Request(SampleApps.Program()), protectedFolder);

        Assert.Equal(AppFolderOutcome.Refused, result.Outcome);
        Assert.Equal(FolderRefusal.AppData, result.Refusal);
        Assert.Empty(Directory.GetFileSystemEntries(protectedFolder));
    }

    [Fact]
    public void Many_existing_folders_take_one_look_and_undo_as_one_step()
    {
        var versions = new[] { _box.NewFolder("Version 1"), _box.NewFolder("Version 2"), _box.NewFolder("Version 3") };

        var results = Creator().ThemeExisting(SampleApps.Request(SampleApps.Program()), versions);

        Assert.All(results, result => Assert.Equal(AppFolderOutcome.Themed, result.Outcome));
        Assert.All(versions, folder => Assert.True(File.Exists(Sandbox.IniPath(folder))));

        _box.Restorer().UndoLast();

        Assert.All(versions, folder => Assert.False(File.Exists(Sandbox.IniPath(folder))));
    }

    [Fact]
    public void A_refused_folder_does_not_stop_the_rest_of_the_run()
    {
        var version = _box.NewFolder("Version 1");
        var protectedFolder = Path.Combine(_box.FakeAppData, "Launcher");
        Directory.CreateDirectory(protectedFolder);

        var results = Creator().ThemeExisting(SampleApps.Request(SampleApps.Program()), [protectedFolder, version]);

        Assert.Equal(AppFolderOutcome.Refused, results[0].Outcome);
        Assert.Equal(AppFolderOutcome.Themed, results[1].Outcome);
    }

    [Fact]
    public void The_folders_inside_one_leave_out_files_and_hidden_folders()
    {
        var parent = _box.NewFolder("App");
        var second = Directory.CreateDirectory(Path.Combine(parent, "2.0")).FullName;
        var first = Directory.CreateDirectory(Path.Combine(parent, "1.0")).FullName;
        var hidden = Directory.CreateDirectory(Path.Combine(parent, "cache")).FullName;

        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        File.WriteAllText(Path.Combine(parent, "notes.txt"), "text");

        Assert.Equal([first, second], AppFolderCreator.FoldersInside(parent));
    }

    [Fact]
    public void A_folder_that_is_gone_has_nothing_inside_it()
    {
        Assert.Empty(AppFolderCreator.FoldersInside(Path.Combine(_box.Root, "gone")));
    }

    [Fact]
    public void An_install_folder_is_left_alone_unless_asked_for()
    {
        var install = _box.NewFolder("Install");

        var result = Create(SampleApps.Request(SampleApps.SteamGame(installFolder: install))).Results[0];

        Assert.Null(result.InstallFolder);
        Assert.Empty(Directory.GetFileSystemEntries(install));
    }

    [Fact]
    public void An_install_folder_gets_the_look_when_asked_for_and_undoes_with_the_rest()
    {
        var install = _box.NewFolder("Install");

        var batch = Creator().Create(
            [SampleApps.Request(SampleApps.SteamGame(installFolder: install))],
            new AppFolderOptions(_games, NameClashChoice.AddNumber, ThemeInstallFolders: true));

        Assert.Equal(ApplyOutcome.Applied, batch.Results[0].InstallFolder?.Outcome);
        Assert.True(File.Exists(Sandbox.IniPath(install)));

        _box.Restorer().UndoLast();

        Assert.True(Directory.Exists(install));
        Assert.False(File.Exists(Sandbox.IniPath(install)));
        Assert.Empty(Directory.GetFileSystemEntries(_games));
    }

    [Fact]
    public void A_protected_install_folder_is_refused_but_the_new_folder_is_still_made()
    {
        var install = Path.Combine(_box.FakeAppData, "Launcher", "Game");
        Directory.CreateDirectory(install);

        var batch = Creator().Create(
            [SampleApps.Request(SampleApps.SteamGame(installFolder: install))],
            new AppFolderOptions(_games, NameClashChoice.AddNumber, ThemeInstallFolders: true));

        Assert.Equal(AppFolderOutcome.Created, batch.Results[0].Outcome);
        Assert.Equal(ApplyOutcome.Refused, batch.Results[0].InstallFolder?.Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(install));
    }

    [Fact]
    public void An_install_folder_is_left_alone_when_its_new_folder_was_skipped()
    {
        var install = _box.NewFolder("Install");
        var game = SampleApps.SteamGame(installFolder: install);

        Directory.CreateDirectory(Path.Combine(_games, game.Name));

        var result = Creator().Create(
            [SampleApps.Request(game)],
            new AppFolderOptions(_games, NameClashChoice.Skip, ThemeInstallFolders: true)).Results[0];

        Assert.Equal(AppFolderOutcome.NameTaken, result.Outcome);
        Assert.Null(result.InstallFolder);
        Assert.Empty(Directory.GetFileSystemEntries(install));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void An_install_folder_is_left_alone_when_its_new_folder_failed()
    {
        var install = _box.NewFolder("Install");
        var ops = new FaultyOperations { FailWrite = path => path.EndsWith(".url", StringComparison.Ordinal) };

        var result = Creator(ops).Create(
            [SampleApps.Request(SampleApps.SteamGame(installFolder: install))],
            new AppFolderOptions(_games, NameClashChoice.AddNumber, ThemeInstallFolders: true)).Results[0];

        Assert.Equal(AppFolderOutcome.Failed, result.Outcome);
        Assert.Null(result.InstallFolder);
        Assert.Empty(Directory.GetFileSystemEntries(install));
    }

    // Repair

    [Fact]
    public void Repair_redraws_a_new_folders_icon_from_the_saved_art()
    {
        var folder = CreateOne().FolderPath!;
        var icon = _box.Journal.Read().Entries.Single().Change.IconPath;

        File.Delete(icon);

        var repairer = new FolderRepairer(_box.Safety, _box.Journal, _box.Icons, _box.Customizer(), _box.Shell);
        var results = repairer.RepairAll();

        Assert.Equal(RepairOutcome.Repaired, results.Single().Outcome);
        Assert.True(File.Exists(icon));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void Undo_still_removes_the_folder_after_a_repair()
    {
        var folder = CreateOne().FolderPath!;

        File.Delete(_box.Journal.Read().Entries.Single().Change.IconPath);
        new FolderRepairer(_box.Safety, _box.Journal, _box.Icons, _box.Customizer(), _box.Shell).RepairAll();

        _box.Restorer().RestoreAll();

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void A_created_folder_with_no_picture_uses_its_symbol()
    {
        var request = SampleApps.Request(SampleApps.Program()) with
        {
            ArtPng = null,
            Spec = Sandbox.BlueStar,
        };

        var result = Create(request).Results[0];

        Assert.Equal(AppFolderOutcome.Created, result.Outcome);
        Assert.False(Directory.Exists(_box.Paths.AppArtDirectory));
    }

    [Fact]
    public void Drawn_art_reaches_the_finished_icon()
    {
        var art = DrawnArt.Png(128, Color.FromArgb(240, 40, 40));
        var request = SampleApps.Request(SampleApps.Program()) with { ArtPng = art };

        Create(request);

        var bytes = File.ReadAllBytes(_box.Journal.Read().Entries.Single().Change.IconPath);
        var frames = Foldcraft.Core.Icons.IcoFile.TryReadFrames(bytes)!;
        var largest = frames.Single(frame => frame.IsPng);

        Assert.Equal(Foldcraft.Core.Icons.IconRenderer.Sizes.Count, frames.Count);
        Assert.True(DrawnArt.PixelsLike(bytes.AsSpan(largest.Offset, largest.Length).ToArray(), Color.FromArgb(240, 40, 40)) > 1000);
    }
}
