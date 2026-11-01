using System.Drawing;
using Foldcraft.Core.Apps;

namespace Foldcraft.Tests;

public sealed class AppDiscoveryTests : IDisposable
{
    private readonly Sandbox _box = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakePc _pc;

    public AppDiscoveryTests()
    {
        _pc = new FakePc(Path.Combine(_box.Root, "pc"), _registry);
    }

    public void Dispose() => _box.Dispose();

    private DetectedApp Find(string name) => Assert.Single(_pc.Discovery.Scan().Apps, app => app.Name == name);

    private IEnumerable<string> FoundNames() => _pc.Discovery.Scan().Apps.Select(app => app.Name);

    private string LibraryFile(params string[] paths) =>
        "\"libraryfolders\"\n{\n" + string.Concat(paths.Select((path, i) =>
            $"\t\"{i}\"\n\t{{\n\t\t\"path\"\t\t\"{path.Replace(@"\", @"\\")}\"\n\t\t\"label\"\t\t\"\"\n\t}}\n")) + "}\n";

    [Fact]
    public void Nowhere_to_look_finds_nothing()
    {
        var report = AppDiscovery.For(AppDiscoveryRoots.None, new ShellShortcuts()).Scan();

        Assert.Empty(report.Apps);
        Assert.Empty(report.Problems);
    }

    [Fact]
    public void A_scan_only_reads_the_registry()
    {
        _pc.InstallSteam(_registry);
        var writes = _registry.Writes;

        _pc.Discovery.Scan();

        Assert.Equal(writes, _registry.Writes);
        Assert.True(_registry.Reads > 0);
    }

    [Fact]
    public void A_scan_changes_nothing_on_disk()
    {
        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep");
        _pc.AddApp("Paper Boat", Color.Teal);

        var before = Snapshot(_pc.Root);

        _pc.Discovery.Scan();

        Assert.Equal(before, Snapshot(_pc.Root));
    }

    private static string[] Snapshot(string root) => Directory
        .EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => $"{path}|{File.GetLastWriteTimeUtc(path):O}")
        .ToArray();

    // Steam

    [Fact]
    public void Steam_games_are_found_in_every_library()
    {
        var second = Path.Combine(_pc.Root, "SecondDrive", "Library");

        _pc.InstallSteam(_registry, LibraryFile(_pc.Steam, second));
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep");
        _pc.AddSteamGame(second, "200", "Salt Harbor");

        Assert.Contains("Lantern Keep", FoundNames());
        Assert.Contains("Salt Harbor", FoundNames());
    }

    [Fact]
    public void The_older_library_format_with_bare_paths_also_works()
    {
        var second = Path.Combine(_pc.Root, "Old", "Library");

        _pc.InstallSteam(_registry, "\"LibraryFolders\"\n{\n\t\"TimeNextStatsReport\"\t\"1\"\n\t\"1\"\t\"" + second.Replace(@"\", @"\\") + "\"\n}\n");
        _pc.AddSteamGame(second, "300", "Old Tower");

        Assert.Contains("Old Tower", FoundNames());
    }

    [Fact]
    public void The_steam_client_is_found_too()
    {
        _pc.InstallSteam(_registry);

        var client = Find("Steam");

        Assert.Equal(LaunchKind.Program, client.Launch.Kind);
        Assert.False(client.IsGame);
    }

    [Fact]
    public void A_game_starts_through_a_steam_address()
    {
        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep");

        var game = Find("Lantern Keep");

        Assert.Equal(LaunchKind.Address, game.Launch.Kind);
        Assert.Equal("steam://rungameid/100", game.Launch.Target);
        Assert.True(game.IsGame);
    }

    [Fact]
    public void Steams_shared_installers_are_not_shown()
    {
        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "228980", "Steamworks Common Redistributables");
        _pc.AddSteamGame(_pc.Steam, "400", "Proton 9.0");

        Assert.Equal(["Steam"], FoundNames());
    }

    [Fact]
    public void No_steam_in_the_registry_means_no_steam_results()
    {
        Assert.Empty(new SteamProvider(_pc.Roots).Discover(default).Apps);
    }

    [Fact]
    public void A_steam_path_that_does_not_exist_finds_nothing()
    {
        _registry.SetString(@"Software\Valve\Steam", "SteamPath", Path.Combine(_pc.Root, "gone"));

        Assert.Empty(new SteamProvider(_pc.Roots).Discover(default).Apps);
    }

    [Fact]
    public void A_malformed_manifest_is_skipped_and_counted()
    {
        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep");
        File.WriteAllText(Path.Combine(_pc.Steam, "steamapps", "appmanifest_999.acf"), "this isn't a manifest {{{");
        File.WriteAllBytes(Path.Combine(_pc.Steam, "steamapps", "appmanifest_998.acf"), [0, 1, 2, 0xFF, 0xFE]);

        var report = _pc.Discovery.Scan();

        Assert.Contains(report.Apps, app => app.Name == "Lantern Keep");
        Assert.Equal(2, report.SkippedFiles);
    }

    [Theory]
    [InlineData("12a4")]
    [InlineData("1/../2")]
    [InlineData("")]
    public void An_app_number_that_is_not_a_number_is_refused(string appId)
    {
        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "1", "Good Game");

        var path = Path.Combine(_pc.Steam, "steamapps", "appmanifest_2.acf");
        File.WriteAllText(path, $"\"AppState\" {{ \"appid\" \"{appId}\" \"name\" \"Bad Game\" }}");

        Assert.DoesNotContain("Bad Game", FoundNames());
    }

    [Fact]
    public void A_broken_library_file_still_leaves_the_main_library()
    {
        _pc.InstallSteam(_registry, "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\"");
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep");

        Assert.Contains("Lantern Keep", FoundNames());
    }

    [Fact]
    public void A_network_library_is_never_opened()
    {
        _pc.InstallSteam(_registry, LibraryFile(@"\\server\share\Library"));
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep");

        var report = _pc.Discovery.Scan();

        Assert.Null(LocalSource.Normalize(@"\\server\share\Library"));
        Assert.Contains(report.Apps, app => app.Name == "Lantern Keep");
        Assert.Empty(report.Problems);
    }

    [Fact]
    public void A_manifest_cannot_point_its_install_folder_outside_the_library()
    {
        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep", installDir: null);

        var path = Path.Combine(_pc.Steam, "steamapps", "appmanifest_100.acf");
        File.WriteAllText(path, "\"AppState\" { \"appid\" \"100\" \"name\" \"Lantern Keep\" \"installdir\" \"..\\\\..\\\\..\" }");

        Assert.Null(Find("Lantern Keep").InstallFolder);
    }

    [Fact]
    public void A_game_that_was_removed_has_no_install_folder()
    {
        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep", installDir: null);

        var path = Path.Combine(_pc.Steam, "steamapps", "appmanifest_100.acf");
        File.WriteAllText(path, "\"AppState\" { \"appid\" \"100\" \"name\" \"Lantern Keep\" \"installdir\" \"Gone\" }");

        Assert.Null(Find("Lantern Keep").InstallFolder);
    }

    [Fact]
    public void The_full_icon_named_after_the_cache_picture_comes_first()
    {
        const string Hash = "0123456789abcdef0123456789abcdef01234567";

        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep");
        _pc.WriteFile($@"Launchers\Steam\appcache\librarycache\100\{Hash}.jpg", [1]);
        _pc.WriteFile(@"Launchers\Steam\appcache\librarycache\100\logo.png", [1]);
        _pc.WriteFile(@"Launchers\Steam\appcache\librarycache\100\header.jpg", [1]);
        var full = _pc.WriteFile($@"Launchers\Steam\steam\games\{Hash}.ico", [1]);

        var icons = Find("Lantern Keep").Icons.Select(icon => Path.GetFileName(icon.Path)).ToArray();

        Assert.Equal(Path.GetFileName(full), icons[0]);
        Assert.Equal("logo.png", icons[1]);
        Assert.Equal($"{Hash}.jpg", icons[2]);
        Assert.Equal("header.jpg", icons[3]);
    }

    [Fact]
    public void A_logo_in_a_hashed_folder_of_the_newer_cache_is_found()
    {
        const string Hash = "fedcba9876543210fedcba9876543210fedcba98";

        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep");
        var logo = _pc.WriteFile($@"Launchers\Steam\appcache\librarycache\100\{Hash}\logo.png", [1]);

        Assert.Contains(Find("Lantern Keep").Icons, icon => icon.Path.Equals(logo, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_flat_layout_of_the_older_cache_is_found()
    {
        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep");
        var logo = _pc.WriteFile(@"Launchers\Steam\appcache\librarycache\100_logo.png", [1]);
        var icon = _pc.WriteFile(@"Launchers\Steam\appcache\librarycache\100_icon.jpg", [1]);

        var found = Find("Lantern Keep").Icons.Select(candidate => candidate.Path).ToArray();

        Assert.Contains(found, path => path.Equals(logo, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(found, path => path.Equals(icon, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_games_own_program_is_a_candidate_but_its_uninstaller_is_not()
    {
        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep", "Keep");

        var folder = Path.Combine(_pc.Steam, "steamapps", "common", "Keep");
        File.WriteAllBytes(Path.Combine(folder, "keep.exe"), new byte[100]);
        File.WriteAllBytes(Path.Combine(folder, "unins000.exe"), new byte[500]);

        var names = Find("Lantern Keep").Icons.Select(icon => Path.GetFileName(icon.Path)).ToArray();

        Assert.Contains("keep.exe", names);
        Assert.DoesNotContain("unins000.exe", names);
    }

    // Roblox

    [Fact]
    public void Roblox_is_found_in_its_per_user_install()
    {
        _pc.AddRobloxVersion("version-aaa", "RobloxPlayerBeta.exe");

        Assert.Contains("Roblox", FoundNames());
    }

    [Fact]
    public void The_newest_version_wins()
    {
        var old = _pc.AddRobloxVersion("version-old", "RobloxPlayerBeta.exe");
        var current = _pc.AddRobloxVersion("version-new", "RobloxPlayerBeta.exe");

        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));

        Assert.Equal(current, Find("Roblox").ProgramPath);
    }

    [Fact]
    public void Roblox_starts_through_its_own_shortcut_because_its_program_moves_on_every_update()
    {
        var program = _pc.AddRobloxVersion("version-aaa", "RobloxPlayerBeta.exe");
        var link = _pc.AddLink(_pc.UserMenu, @"Roblox\Roblox Player.lnk", program);

        var app = Find("Roblox");

        Assert.Equal(LaunchKind.OtherShortcut, app.Launch.Kind);
        Assert.Equal(link, app.Launch.Target);
    }

    [Fact]
    public void Without_a_shortcut_roblox_falls_back_to_the_program()
    {
        var program = _pc.AddRobloxVersion("version-aaa", "RobloxPlayerBeta.exe");

        var app = Find("Roblox");

        Assert.Equal(LaunchKind.Program, app.Launch.Kind);
        Assert.Equal(program, app.Launch.Target);
    }

    private sealed class ShortcutTo(string target) : IShortcuts
    {
        public ShortcutInfo? Read(string path) => new(target);

        public byte[] Build(ShortcutInfo shortcut, string scratchDirectory) => [];
    }

    /// <summary>
    /// A real network path can't be used here, because a test must not reach for a server. This
    /// spelling is one the gate refuses just the same, and without the gate the file is found.
    /// </summary>
    [Fact]
    public void A_shortcut_is_only_believed_after_the_local_path_gate()
    {
        var program = _pc.AddRobloxVersion("version-aaa", "RobloxPlayerBeta.exe");

        _pc.WriteFile(@"UserMenu\Roblox\Roblox Player.lnk", []);

        var provider = new RobloxProvider(_pc.Roots, new ShortcutTo(program + "::$DATA"));
        var app = Assert.Single(provider.Discover(CancellationToken.None).Apps);

        Assert.Equal(LaunchKind.Program, app.Launch.Kind);
        Assert.Equal(program, app.Launch.Target);
    }

    [Fact]
    public void A_shortcut_left_over_from_an_old_version_is_not_used()
    {
        var gone = _pc.AddRobloxVersion("version-old", "RobloxPlayerBeta.exe");
        var program = _pc.AddRobloxVersion("version-new", "RobloxPlayerBeta.exe");

        _pc.AddLink(_pc.UserMenu, @"Roblox\Roblox Player.lnk", gone);
        File.Delete(gone);

        var app = Find("Roblox");

        Assert.Equal(LaunchKind.Program, app.Launch.Kind);
        Assert.Equal(program, app.Launch.Target);
    }

    [Fact]
    public void Roblox_studio_is_its_own_item()
    {
        _pc.AddRobloxVersion("version-aaa", "RobloxPlayerBeta.exe");
        _pc.AddRobloxVersion("version-bbb", "RobloxStudioBeta.exe");

        Assert.Contains("Roblox", FoundNames());
        Assert.Contains("Roblox Studio", FoundNames());
    }

    [Fact]
    public void A_start_menu_entry_for_roblox_does_not_appear_twice()
    {
        var program = _pc.AddRobloxVersion("version-aaa", "RobloxPlayerBeta.exe");

        _pc.AddLink(_pc.UserMenu, @"Roblox\Roblox Player.lnk", program);
        _pc.AddLink(_pc.UserMenu, "Roblox.lnk", program);

        Assert.Single(FoundNames());
    }

    [Fact]
    public void No_install_means_no_roblox_even_with_a_leftover_folder()
    {
        Directory.CreateDirectory(Path.Combine(_pc.LocalAppData, "Roblox", "Versions", "version-empty"));

        Assert.Empty(FoundNames());
    }

    // Epic Games

    private string EpicJson(string name = "Copper Sky", string install = "", string extra = "")
    {
        var folder = install.Length > 0 ? install : Path.Combine(_pc.Root, "Epic Games", "CopperSky");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Copper.exe"), []);

        return $$"""
            {
              "DisplayName": "{{name}}",
              "AppName": "CopperSkyApp",
              "CatalogNamespace": "abc123",
              "CatalogItemId": "def456",
              "InstallLocation": "{{folder.Replace(@"\", @"\\")}}",
              "LaunchExecutable": "Copper.exe"{{extra}}
            }
            """;
    }

    [Fact]
    public void Epic_games_are_found_from_their_manifests()
    {
        _pc.AddEpicManifest("one.item", EpicJson());

        var game = Find("Copper Sky");

        Assert.Equal(AppSource.EpicGames, game.Source);
        Assert.True(game.IsGame);
        Assert.Equal("com.epicgames.launcher://apps/abc123%3Adef456%3ACopperSkyApp?action=launch&silent=true", game.Launch.Target);
        Assert.Equal("Copper.exe", Path.GetFileName(game.Icons.Single().Path));
    }

    [Fact]
    public void An_unfinished_download_is_not_shown()
    {
        _pc.AddEpicManifest("one.item", EpicJson(extra: ",\n\"bIsIncompleteInstall\": true"));

        Assert.Empty(FoundNames());
    }

    [Fact]
    public void Add_on_content_is_not_shown_as_a_game()
    {
        _pc.AddEpicManifest("one.item", EpicJson(extra: ",\n\"MainGameAppName\": \"SomethingElse\""));

        Assert.Empty(FoundNames());
    }

    [Fact]
    public void An_app_from_epic_is_not_called_a_game()
    {
        _pc.AddEpicManifest("one.item", EpicJson(extra: ",\n\"bIsApplication\": true"));

        Assert.False(Find("Copper Sky").IsGame);
    }

    [Fact]
    public void A_malformed_epic_manifest_is_skipped_and_counted()
    {
        _pc.AddEpicManifest("good.item", EpicJson());
        _pc.AddEpicManifest("bad.item", "{ this is not json");
        _pc.AddEpicManifest("thin.item", "{ \"DisplayName\": 12 }");
        _pc.AddEpicManifest("list.item", "[1, 2, 3]");

        var report = _pc.Discovery.Scan();

        Assert.Single(report.Apps);
        Assert.Equal(3, report.SkippedFiles);
    }

    [Fact]
    public void Odd_identifiers_fall_back_to_the_program_rather_than_into_a_file_windows_follows()
    {
        var json = EpicJson().Replace("abc123", "abc\\r\\nURL=http://elsewhere");
        _pc.AddEpicManifest("one.item", json);

        var game = Find("Copper Sky");

        Assert.Equal(LaunchKind.Program, game.Launch.Kind);
        Assert.EndsWith("Copper.exe", game.Launch.Target);
    }

    [Fact]
    public void A_manifest_cannot_name_a_program_outside_its_own_folder()
    {
        var outside = _pc.AddProgram("outside.exe");
        var json = EpicJson().Replace("\"Copper.exe\"", "\"..\\\\..\\\\Programs\\\\outside.exe\"");

        Assert.True(File.Exists(outside));
        _pc.AddEpicManifest("one.item", json);

        Assert.Empty(Find("Copper Sky").Icons);
    }

    // Start Menu

    [Fact]
    public void Shortcuts_from_both_start_menus_are_found_with_no_list_of_products()
    {
        _pc.AddLink(_pc.UserMenu, "Paper Boat.lnk", _pc.AddProgram(@"Boat\boat.exe"));
        _pc.AddLink(_pc.AllUsersMenu, @"Vendor\Quiet Studio 2030.lnk", _pc.AddProgram(@"Vendor\studio.exe"));

        Assert.Contains("Paper Boat", FoundNames());
        Assert.Contains("Quiet Studio 2030", FoundNames());
    }

    [Theory]
    [InlineData("Uninstall Paper Boat")]
    [InlineData("Paper Boat Uninstaller")]
    [InlineData("Remove Paper Boat")]
    [InlineData("Paper Boat Help")]
    [InlineData("Paper Boat Documentation")]
    [InlineData("Paper Boat Manual")]
    [InlineData("Paper Boat User Guide")]
    [InlineData("Paper Boat License")]
    [InlineData("Paper Boat Release Notes")]
    [InlineData("Paper Boat Website")]
    [InlineData("Read Me")]
    [InlineData("Paper Boat FAQ")]
    [InlineData("What's New in Paper Boat")]
    [InlineData("Report a Problem with Paper Boat")]
    [InlineData("Install Extra Tools")]
    public void Things_that_are_not_the_app_are_filtered_out_by_name(string name)
    {
        Assert.True(ShortcutFilter.IsNotAnApp(name));
    }

    [Theory]
    [InlineData("Paper Boat")]
    [InlineData("Inno Setup Compiler")]
    [InlineData("Quick Docs")]
    [InlineData("Helpful Otter")]
    [InlineData("Guidebook")]
    public void Real_apps_are_not_filtered_out_by_a_word_in_their_name(string name)
    {
        Assert.False(ShortcutFilter.IsNotAnApp(name));
    }

    [Theory]
    [InlineData(@"C:\Apps\Boat\unins000.exe")]
    [InlineData(@"C:\Apps\Boat\uninstall.exe")]
    [InlineData(@"C:\Apps\Boat\setup.exe")]
    [InlineData(@"C:\Apps\Boat\readme.txt")]
    [InlineData(@"C:\Apps\Boat\manual.pdf")]
    [InlineData(@"C:\Apps\Boat\help.chm")]
    [InlineData(@"C:\Apps\Boat\index.html")]
    [InlineData(@"C:\Apps\Boat\tools.bat")]
    [InlineData(@"C:\Program Files\Windows Media Player\wmplayer.exe")]
    public void Targets_that_are_not_the_app_are_filtered_out(string target)
    {
        Assert.True(ShortcutFilter.IsNotAProgram(target));
    }

    [Theory]
    [InlineData(@"C:\Apps\Boat\boat.exe")]
    [InlineData(@"C:\Users\Someone\AppData\Local\Chatty\Update.exe")]
    public void Real_programs_are_kept(string target)
    {
        Assert.False(ShortcutFilter.IsNotAProgram(target));
    }

    [Fact]
    public void A_shortcut_into_the_windows_folder_is_part_of_windows()
    {
        var tool = Path.Combine(_pc.Windows, "System32", "tool.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(tool)!);
        File.WriteAllBytes(tool, []);

        _pc.AddLink(_pc.UserMenu, "System Thing.lnk", tool);

        Assert.Empty(FoundNames());
    }

    [Fact]
    public void Windows_own_start_menu_folders_are_not_entered()
    {
        _pc.AddLink(_pc.UserMenu, @"Administrative Tools\Thing.lnk", _pc.AddProgram("thing.exe"));
        _pc.AddLink(_pc.UserMenu, @"Startup\Starts Itself.lnk", _pc.AddProgram("starts.exe"));

        Assert.Empty(FoundNames());
    }

    [Fact]
    public void A_shortcut_whose_target_is_gone_is_left_out()
    {
        var program = _pc.AddProgram("gone.exe");

        _pc.AddLink(_pc.UserMenu, "Gone App.lnk", program);
        File.Delete(program);

        Assert.Empty(FoundNames());
    }

    [Fact]
    public void A_broken_shortcut_is_skipped_and_counted()
    {
        _pc.AddLink(_pc.UserMenu, "Good.lnk", _pc.AddProgram("good.exe"));
        File.WriteAllBytes(Path.Combine(_pc.UserMenu, "Broken.lnk"), [1, 2, 3, 4]);

        var report = _pc.Discovery.Scan();

        Assert.Single(report.Apps);
        Assert.Equal(1, report.SkippedFiles);
    }

    [Fact]
    public void One_app_with_a_shortcut_in_both_menus_appears_once()
    {
        var program = _pc.AddProgram("boat.exe");

        _pc.AddLink(_pc.UserMenu, "Paper Boat.lnk", program);
        _pc.AddLink(_pc.AllUsersMenu, "Paper Boat.lnk", program);

        Assert.Single(FoundNames());
    }

    [Fact]
    public void Two_shortcuts_to_one_program_keep_the_plain_one()
    {
        var program = _pc.AddProgram("browser.exe");

        _pc.AddLink(_pc.UserMenu, "A Browser Private.lnk", program, "--private");
        _pc.AddLink(_pc.UserMenu, "Z Browser.lnk", program);

        Assert.Equal(["Z Browser"], FoundNames());
    }

    [Fact]
    public void A_program_that_starts_through_arguments_keeps_them()
    {
        _pc.AddLink(_pc.UserMenu, "Chatty.lnk", _pc.AddProgram(@"Chatty\Update.exe"), "--processStart chatty.exe");

        Assert.Equal("--processStart chatty.exe", Find("Chatty").Launch.Arguments);
    }

    [Fact]
    public void The_shortcuts_own_icon_comes_before_the_program()
    {
        var program = _pc.AddProgram("boat.exe");
        var icon = _pc.WriteFile("boat-icon.ico", DrawnArt.Ico(Color.Teal, 64));

        _pc.AddLink(_pc.UserMenu, "Paper Boat.lnk", program, icon: icon, iconIndex: 3);

        var candidates = Find("Paper Boat").Icons;

        Assert.Equal(new IconCandidate(icon, 3), candidates[0]);
        Assert.Equal(new IconCandidate(program), candidates[1]);
    }

    [Fact]
    public void A_shortcut_with_an_icon_on_a_network_share_never_reaches_for_it()
    {
        _pc.AddLink(_pc.UserMenu, "Paper Boat.lnk", _pc.AddProgram("boat.exe"), icon: @"\\server\share\boat.ico");

        Assert.DoesNotContain(Find("Paper Boat").Icons, icon => icon.Path.StartsWith(@"\\", StringComparison.Ordinal));
    }

    [Fact]
    public void A_junction_in_the_start_menu_is_never_entered()
    {
        var elsewhere = Path.Combine(_pc.Root, "Elsewhere");
        _pc.AddLink(elsewhere, "Hidden Away.lnk", _pc.AddProgram("hidden.exe"));
        Sandbox.CreateJunction(Path.Combine(_pc.UserMenu, "Linked"), elsewhere);

        Assert.Empty(FoundNames());
    }

    [Fact]
    public void A_windows_installer_shortcut_starts_through_itself()
    {
        var stored = Path.Combine(_pc.Windows, "Installer", "{GUID}", "icon.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(stored)!);
        File.WriteAllBytes(stored, []);

        var link = _pc.AddLink(_pc.UserMenu, "Office Thing.lnk", stored);
        var app = Find("Office Thing");

        Assert.Equal(LaunchKind.OtherShortcut, app.Launch.Kind);
        Assert.Equal(link, app.Launch.Target);
    }

    // Everything together

    [Fact]
    public void One_provider_failing_never_stops_the_others()
    {
        var discovery = new AppDiscovery(
        [
            new ScriptedProvider(_ => new AppProviderResult([SampleApps.Program("First")])),
            new ScriptedProvider(_ => throw new InvalidOperationException("A launcher did something odd.")),
            new ScriptedProvider(_ => new AppProviderResult([SampleApps.Program("Third")])),
        ]);

        var report = discovery.Scan();

        Assert.Equal(["First", "Third"], report.Apps.Select(app => app.Name));
        Assert.Equal("A launcher did something odd.", Assert.Single(report.Problems).Message);
    }

    [Fact]
    public void Canceling_stops_the_scan_and_says_so()
    {
        using var cancellation = new CancellationTokenSource();

        var discovery = new AppDiscovery(
        [
            new ScriptedProvider(_ =>
            {
                cancellation.Cancel();
                return new AppProviderResult([SampleApps.Program("First")]);
            }),
            new ScriptedProvider(_ => throw new InvalidOperationException("This should never run.")),
        ]);

        var report = discovery.Scan(cancellation.Token);

        Assert.True(report.WasCanceled);
        Assert.Empty(report.Problems);
        Assert.Single(report.Apps);
    }

    [Fact]
    public void Canceling_inside_a_provider_is_a_cancel_and_not_a_failure()
    {
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();
        _pc.InstallSteam(_registry);

        var report = _pc.Discovery.Scan(cancellation.Token);

        Assert.True(report.WasCanceled);
        Assert.Empty(report.Problems);
    }

    [Fact]
    public void A_game_a_launcher_knows_about_beats_a_start_menu_shortcut_of_the_same_name()
    {
        _pc.InstallSteam(_registry);
        _pc.AddSteamGame(_pc.Steam, "100", "Lantern Keep");
        _pc.AddLink(_pc.UserMenu, "Lantern Keep.lnk", _pc.AddProgram("keep.exe"));

        Assert.Equal(AppSource.Steam, Find("Lantern Keep").Source);
    }

    [Fact]
    public void Steams_own_start_menu_shortcut_does_not_appear_twice()
    {
        _pc.InstallSteam(_registry);
        _pc.AddLink(_pc.UserMenu, @"Steam\Steam.lnk", Path.Combine(_pc.Steam, "steam.exe"));

        Assert.Equal(AppSource.Steam, Find("Steam").Source);
    }

    [Fact]
    public void Results_come_back_in_alphabetical_order()
    {
        _pc.AddLink(_pc.UserMenu, "Zebra.lnk", _pc.AddProgram("z.exe"));
        _pc.AddLink(_pc.UserMenu, "apple.lnk", _pc.AddProgram("a.exe"));

        Assert.Equal(["apple", "Zebra"], FoundNames());
    }

    [Fact]
    public void Past_the_limit_the_report_says_so()
    {
        var apps = Enumerable.Range(0, AppDiscovery.MaxApps + 5).Select(i => SampleApps.Program($"App {i}", $@"C:\Apps\{i}.exe")).ToArray();

        var report = new AppDiscovery([new ScriptedProvider(_ => new AppProviderResult(apps))]).Scan();

        Assert.Equal(AppDiscovery.MaxApps, report.Apps.Count);
        Assert.True(report.HitLimit);
    }
}
