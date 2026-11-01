using Foldcraft.Core;
using Foldcraft.Core.Safety;

namespace Foldcraft.Tests;

public sealed class FolderSafetyTests : IDisposable
{
    private readonly Sandbox _box = new();
    private readonly FolderSafety _real;

    private static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string SystemDrive = Path.GetPathRoot(WindowsDir)!;

    public FolderSafetyTests()
    {
        // Real system locations, read-only: Check never writes anything.
        _real = new FolderSafety(_box.Paths);
    }

    public void Dispose() => _box.Dispose();

    private static void AssertRefused(FolderSafetyResult result, FolderRefusal expected)
    {
        Assert.False(result.IsAllowed, $"'{result.RequestedPath}' was allowed.");
        Assert.Equal(expected, result.Refusal);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Null(result.ResolvedPath);
    }

    [Fact]
    public void An_ordinary_folder_is_allowed_and_resolved()
    {
        var folder = _box.NewFolder("plain");

        var result = _box.Safety.Check(folder);

        Assert.True(result.IsAllowed, result.Reason);
        Assert.Equal(folder, result.ResolvedPath, ignoreCase: true);
    }

    [Fact]
    public void Spelling_variations_of_an_allowed_folder_resolve_to_one_path()
    {
        var folder = _box.NewFolder("Spelled");
        var canonical = _box.Safety.Check(folder).ResolvedPath;

        Assert.Equal(canonical, _box.Safety.Check(folder + "\\").ResolvedPath);
        Assert.Equal(canonical, _box.Safety.Check(folder.ToUpperInvariant()).ResolvedPath);
        Assert.Equal(canonical, _box.Safety.Check(@"\\?\" + folder).ResolvedPath);
        Assert.Equal(canonical, _box.Safety.Check(Path.Combine(folder, "..", "Spelled")).ResolvedPath);
        Assert.Equal(canonical, _box.Safety.Check(folder.Replace('\\', '/')).ResolvedPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"\")]
    [InlineData("/")]
    [InlineData(@"\\?\")]
    public void Drive_root_spellings_are_refused(string suffix)
    {
        var drive = SystemDrive.TrimEnd('\\');

        var spelling = suffix switch
        {
            "" => drive + @"\",
            @"\\?\" => @"\\?\" + drive + @"\",
            _ => drive + suffix,
        };

        AssertRefused(_real.Check(spelling), FolderRefusal.DriveRoot);
    }

    [Fact]
    public void A_drive_root_reached_through_dot_dot_is_refused()
    {
        AssertRefused(_real.Check(WindowsDir + @"\.."), FolderRefusal.DriveRoot);
        AssertRefused(_real.Check(@"\\?\" + SystemDrive), FolderRefusal.DriveRoot);
        AssertRefused(_real.Check(@"\\.\" + SystemDrive.TrimEnd('\\')), FolderRefusal.DriveRoot);
    }

    public static TheoryData<string> WindowsSpellings()
    {
        var name = Path.GetFileName(WindowsDir);
        var drive = SystemDrive.TrimEnd('\\');

        return new TheoryData<string>
        {
            WindowsDir,
            WindowsDir + @"\",
            WindowsDir + @"\\",
            WindowsDir.ToUpperInvariant(),
            WindowsDir.ToLowerInvariant(),
            WindowsDir.Replace('\\', '/'),
            WindowsDir + @"\.",
            WindowsDir + @"\System32\..",
            $@"{drive}\{name}\..\{name}\.",
            WindowsDir + ".",
            WindowsDir + " ",
            WindowsDir + "...  ",
            @"\\?\" + WindowsDir,
            @"\\?\" + WindowsDir + @"\",
            @"\\?\" + WindowsDir.ToUpperInvariant(),
            @"\\?\" + WindowsDir + @"\System32\..",
            @"\\.\" + WindowsDir,
            "//?/" + WindowsDir.Replace('\\', '/'),
            Sandbox.ShortPath(WindowsDir),
        };
    }

    [Theory]
    [MemberData(nameof(WindowsSpellings))]
    public void Windows_directory_is_refused_however_it_is_spelled(string spelling)
    {
        AssertRefused(_real.Check(spelling), FolderRefusal.WindowsDirectory);
    }

    [Fact]
    public void Everything_inside_Windows_is_refused()
    {
        AssertRefused(_real.Check(Path.Combine(WindowsDir, "System32")), FolderRefusal.WindowsDirectory);
        AssertRefused(_real.Check(Path.Combine(WindowsDir, "System32", "drivers")), FolderRefusal.WindowsDirectory);
    }

    [Fact]
    public void Program_files_are_refused()
    {
        AssertRefused(_real.Check(ProgramFiles), FolderRefusal.ProgramFiles);
        AssertRefused(_real.Check(ProgramFiles.ToUpperInvariant() + @"\"), FolderRefusal.ProgramFiles);
        AssertRefused(_real.Check(@"\\?\" + ProgramFiles), FolderRefusal.ProgramFiles);
        AssertRefused(_real.Check(Path.Combine(ProgramFiles, "Common Files")), FolderRefusal.ProgramFiles);

        var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        AssertRefused(_real.Check(x86), FolderRefusal.ProgramFiles);
    }

    [Fact]
    public void An_eight_dot_three_name_cannot_bypass_the_check()
    {
        var shortName = Sandbox.ShortPath(ProgramFiles);

        // On a volume with 8.3 names disabled this degrades to the plain spelling, which is
        // still refused; on this project's dev machine it is C:\PROGRA~1.
        AssertRefused(_real.Check(shortName), FolderRefusal.ProgramFiles);
        AssertRefused(_real.Check(@"\\?\" + shortName), FolderRefusal.ProgramFiles);
        AssertRefused(_real.Check(shortName.ToLowerInvariant() + @"\"), FolderRefusal.ProgramFiles);
        AssertRefused(_real.Check(Path.Combine(shortName, "Common Files")), FolderRefusal.ProgramFiles);
    }

    [Fact]
    public void ProgramData_is_refused()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        AssertRefused(_real.Check(path), FolderRefusal.ProgramData);
        AssertRefused(_real.Check(path.ToLowerInvariant() + @"\"), FolderRefusal.ProgramData);
    }

    [Fact]
    public void The_real_user_profile_root_is_refused_but_not_as_a_subtree()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AssertRefused(_real.Check(profile), FolderRefusal.UserProfileRoot);
        AssertRefused(_real.Check(profile.ToUpperInvariant() + @"\"), FolderRefusal.UserProfileRoot);
        AssertRefused(_real.Check(@"\\?\" + profile), FolderRefusal.UserProfileRoot);
    }

    [Fact]
    public void A_folder_inside_the_profile_is_allowed()
    {
        var documents = Path.Combine(_box.FakeProfile, "Documents");
        Directory.CreateDirectory(documents);

        Assert.True(_box.Safety.Check(documents).IsAllowed);
        AssertRefused(_box.Safety.Check(_box.FakeProfile), FolderRefusal.UserProfileRoot);
    }

    [Fact]
    public void Real_AppData_and_everything_in_it_is_refused()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AssertRefused(_real.Check(Path.Combine(profile, "AppData")), FolderRefusal.AppData);
        AssertRefused(_real.Check(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)), FolderRefusal.AppData);
        AssertRefused(_real.Check(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)), FolderRefusal.AppData);

        // The sandbox itself lives in AppData\Local\Temp on this machine.
        var insideAppData = Path.GetFullPath(Path.GetTempPath()).StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase);

        if (insideAppData)
        {
            AssertRefused(_real.Check(_box.NewFolder("in-real-appdata")), FolderRefusal.AppData);
        }
    }

    [Fact]
    public void Fake_AppData_subtree_is_refused()
    {
        var nested = Path.Combine(_box.FakeAppData, "Roaming", "App");
        Directory.CreateDirectory(nested);

        AssertRefused(_box.Safety.Check(_box.FakeAppData), FolderRefusal.AppData);
        AssertRefused(_box.Safety.Check(nested), FolderRefusal.AppData);
    }

    [Fact]
    public void The_Foldcraft_data_directory_is_refused()
    {
        Directory.CreateDirectory(_box.Paths.IconsDirectory);
        var data = _box.Paths.DataDirectory;

        AssertRefused(_box.Safety.Check(data), FolderRefusal.FoldcraftData);
        AssertRefused(_box.Safety.Check(data + @"\"), FolderRefusal.FoldcraftData);
        AssertRefused(_box.Safety.Check(data.ToUpperInvariant()), FolderRefusal.FoldcraftData);
        AssertRefused(_box.Safety.Check(@"\\?\" + data), FolderRefusal.FoldcraftData);
        AssertRefused(_box.Safety.Check(Path.Combine(data, "Icons", "..")), FolderRefusal.FoldcraftData);
        AssertRefused(_box.Safety.Check(_box.Paths.IconsDirectory), FolderRefusal.FoldcraftData);
    }

    [Fact]
    public void A_folder_that_contains_a_protected_folder_is_refused()
    {
        AssertRefused(_box.Safety.Check(_box.Root), FolderRefusal.ContainsProtectedFolder);

        var usersRoot = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!;
        AssertRefused(_real.Check(usersRoot), FolderRefusal.ContainsProtectedFolder);
    }

    [Fact]
    public void Volume_level_system_folders_are_refused()
    {
        AssertRefused(_real.Check(SystemDrive + "$Recycle.Bin"), FolderRefusal.SystemVolumeFolder);
        AssertRefused(_real.Check(SystemDrive + "System Volume Information"), FolderRefusal.SystemVolumeFolder);
    }

    /// <summary>
    /// A SUBST drive or 8.3 name leads to a Windows folder without ever spelling it. Pointing
    /// one at a real Windows folder would break the temp-only rule for tests, so this stands
    /// in for what Windows reports at the end of that route.
    /// </summary>
    [Theory]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21-1000")]
    [InlineData(@"C:\System Volume Information\store")]
    [InlineData(@"D:\Recovery\WindowsRE")]
    [InlineData(@"C:\$WinREAgent")]
    public void A_volume_system_folder_is_refused_however_the_path_got_there(string finalPath)
    {
        var aliased = new FolderSafety(_box.Paths, _box.Protected) { ResolveFinalPath = _ => finalPath };
        var folder = _box.NewFolder("alias");

        Assert.True(_box.Safety.Check(folder).IsAllowed);
        AssertRefused(aliased.Check(folder), FolderRefusal.SystemVolumeFolder);
    }

    [Fact]
    public void A_junction_is_refused_even_when_its_target_is_allowed()
    {
        var target = _box.NewFolder("junction-target");
        var link = Sandbox.CreateJunction(Path.Combine(_box.Root, "folders", "junction"), target);

        Assert.True(_box.Safety.Check(target).IsAllowed);
        AssertRefused(_box.Safety.Check(link), FolderRefusal.ReparsePoint);
        AssertRefused(_box.Safety.Check(link + @"\"), FolderRefusal.ReparsePoint);
        AssertRefused(_box.Safety.Check(@"\\?\" + link), FolderRefusal.ReparsePoint);
    }

    [Fact]
    public void A_folder_reached_through_a_junction_is_refused()
    {
        var target = _box.NewFolder("through-target");
        Directory.CreateDirectory(Path.Combine(target, "child"));
        var link = Sandbox.CreateJunction(Path.Combine(_box.Root, "folders", "through"), target);

        AssertRefused(_box.Safety.Check(Path.Combine(link, "child")), FolderRefusal.ReparsePoint);
    }

    [Fact]
    public void A_junction_into_a_protected_folder_is_refused()
    {
        var link = Sandbox.CreateJunction(Path.Combine(_box.Root, "folders", "to-windows"), WindowsDir);

        AssertRefused(_box.Safety.Check(link), FolderRefusal.ReparsePoint);
        AssertRefused(_box.Safety.Check(Path.Combine(link, "System32")), FolderRefusal.ReparsePoint);
    }

    [Fact]
    public void A_dangling_junction_is_refused()
    {
        var target = _box.NewFolder("dangling-target");
        var link = Sandbox.CreateJunction(Path.Combine(_box.Root, "folders", "dangling"), target);
        Directory.Delete(target);

        AssertRefused(_box.Safety.Check(link), FolderRefusal.ReparsePoint);
    }

    [Fact]
    public void A_directory_symlink_is_refused_when_the_OS_lets_us_make_one()
    {
        var target = _box.NewFolder("symlink-target");
        var link = Path.Combine(_box.Root, "folders", "symlink");

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Needs Developer Mode or a privilege. Junctions above cover the same code path.
            return;
        }

        AssertRefused(_box.Safety.Check(link), FolderRefusal.ReparsePoint);
    }

    [Theory]
    [InlineData(@"\\server\share\folder")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\SERVER\SHARE\Folder\")]
    [InlineData(@"\\127.0.0.1\c$\Windows")]
    [InlineData(@"\\?\UNC\server\share\folder")]
    [InlineData(@"\\?\unc\server\share\folder")]
    [InlineData(@"//server/share/folder")]
    [InlineData(@"\\.\UNC\server\share\folder")]
    public void Network_paths_are_refused_by_default(string path)
    {
        AssertRefused(_real.Check(path), FolderRefusal.NetworkPath);
    }

    [Fact]
    public void Allowing_the_network_never_allows_an_administrative_share()
    {
        var permissive = new FolderSafety(_box.Paths, _box.Protected, allowNetworkPaths: true);

        AssertRefused(permissive.Check(@"\\localhost\C$\Windows"), FolderRefusal.NetworkPath);
    }

    /// <summary>Makes a real folder with a name only the literal \\?\ form can create.</summary>
    private static string NewLiteralFolder(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        Directory.CreateDirectory(@"\\?\" + path);
        return path;
    }

    private static void DeleteLiteralFolder(string path) => Directory.Delete(@"\\?\" + path, recursive: true);

    [Theory]
    [InlineData("Report.")]
    [InlineData("Sp ")]
    [InlineData("Dots...")]
    public void A_name_ending_in_a_dot_or_space_never_resolves_to_its_twin(string name)
    {
        var parent = Path.Combine(_box.Root, "folders");
        var twin = _box.NewFolder(name.TrimEnd('.', ' '));
        var literal = NewLiteralFolder(parent, name);

        try
        {
            AssertRefused(_box.Safety.Check(literal), FolderRefusal.UnsupportedPathForm);
            AssertRefused(_box.Safety.Check(@"\\?\" + literal), FolderRefusal.UnsupportedPathForm);

            var result = _box.Customizer().Apply(literal, Sandbox.BlueStar);

            Assert.Equal(Foldcraft.Core.Customization.ApplyOutcome.Refused, result.Outcome);
            Assert.Empty(Directory.GetFileSystemEntries(twin));
            Assert.Empty(_box.Journal.Read().Entries);
        }
        finally
        {
            DeleteLiteralFolder(literal);
        }
    }

    [Fact]
    public void A_folder_below_a_name_ending_in_a_dot_never_resolves_to_its_twin()
    {
        var parent = Path.Combine(_box.Root, "folders");
        var twinChild = Path.Combine(_box.NewFolder("Report"), "sub");
        Directory.CreateDirectory(twinChild);
        var literal = NewLiteralFolder(parent, "Report.");
        Directory.CreateDirectory(@"\\?\" + Path.Combine(literal, "sub"));

        try
        {
            AssertRefused(_box.Safety.Check(Path.Combine(literal, "sub")), FolderRefusal.UnsupportedPathForm);
        }
        finally
        {
            DeleteLiteralFolder(literal);
        }
    }

    [Fact]
    public void A_name_ending_in_a_dot_with_no_twin_is_explained_not_called_missing()
    {
        var literal = NewLiteralFolder(_box.NewFolder("alone"), "Lonely.");

        try
        {
            AssertRefused(_box.Safety.Check(literal), FolderRefusal.UnsupportedPathForm);
        }
        finally
        {
            DeleteLiteralFolder(literal);
        }
    }

    [Fact]
    public void A_short_name_cannot_reach_a_folder_whose_real_name_ends_in_a_dot()
    {
        var parent = Path.Combine(_box.Root, "folders");
        _box.NewFolder("Long Report Name");
        var literal = NewLiteralFolder(parent, "Long Report Name.");

        try
        {
            var shortPath = Sandbox.ShortPath(@"\\?\" + literal);

            if (shortPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                shortPath = shortPath[4..];
            }

            // Where 8.3 names are off the short path is the literal one, which is refused anyway.
            Assert.False(_box.Safety.Check(shortPath).IsAllowed);
        }
        finally
        {
            DeleteLiteralFolder(literal);
        }
    }

    [Fact]
    public void A_restore_by_a_trimmed_spelling_cannot_reach_the_twin()
    {
        var twin = _box.NewFolder("Report");
        _box.Customizer().Apply(twin, Sandbox.BlueStar);

        var report = _box.Restorer().RestoreFolder(twin + ".");

        Assert.NotEqual(Foldcraft.Core.Customization.RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.True(File.Exists(Sandbox.IniPath(twin)));
    }

    [Fact]
    public void A_missing_folder_is_refused()
    {
        var path = Path.Combine(_box.Root, "folders", "never-created");

        var result = _box.Safety.Check(path);

        AssertRefused(result, FolderRefusal.NotFound);
        Assert.Equal(path, result.NormalizedPath, ignoreCase: true);
    }

    [Fact]
    public void A_path_on_a_missing_parent_is_refused()
    {
        AssertRefused(
            _box.Safety.Check(Path.Combine(_box.Root, "no", "such", "parent")),
            FolderRefusal.NotFound);
    }

    [Fact]
    public void A_file_is_refused()
    {
        var file = Path.Combine(_box.NewFolder("has-file"), "note.txt");
        File.WriteAllText(file, "hi");

        AssertRefused(_box.Safety.Check(file), FolderRefusal.NotADirectory);
    }

    [Fact]
    public void A_path_going_through_a_file_is_refused()
    {
        var file = Path.Combine(_box.NewFolder("through-file"), "note.txt");
        File.WriteAllText(file, "hi");

        AssertRefused(_box.Safety.Check(Path.Combine(file, "child")), FolderRefusal.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative\\folder")]
    [InlineData("..\\..\\Windows")]
    [InlineData("C:relative")]
    [InlineData("\\rooted\\no\\drive")]
    [InlineData("C:\\bad\0name")]
    [InlineData("C:\\Windows:stream")]
    public void Malformed_paths_are_refused(string path)
    {
        AssertRefused(_real.Check(path), FolderRefusal.InvalidPath);
    }

    [Fact]
    public void A_null_path_is_refused_not_thrown()
    {
        AssertRefused(_real.Check(null!), FolderRefusal.InvalidPath);
    }

    [Theory]
    [InlineData(@"\??\C:\Windows")]
    [InlineData(@"\\?\Volume{00000000-0000-0000-0000-000000000000}\Windows")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1\Windows")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\.\pipe\something")]
    public void Device_and_volume_paths_are_refused(string path)
    {
        AssertRefused(_real.Check(path), FolderRefusal.UnsupportedPathForm);
    }

    [Fact]
    public void Detected_folders_come_from_the_system()
    {
        var detected = ProtectedFolders.Detect();

        Assert.Contains(WindowsDir, detected.Windows, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(ProgramFiles, detected.ProgramFiles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), detected.UserProfiles, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), detected.AppData, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_default_data_directory_is_under_local_app_data()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(local, "Foldcraft"), FoldcraftPaths.Default().DataDirectory);
    }
}
