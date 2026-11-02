using Foldcraft.App.Services;
using Foldcraft.Core.Safety;

namespace Foldcraft.Tests;

/// <summary>Stands in for Windows, so a test can pretend to have the rights and never shows a prompt.</summary>
internal sealed class FakeElevation(bool isElevated = false) : IElevation
{
    public bool IsElevated { get; } = isElevated;

    public List<string?> Restarts { get; } = [];

    public bool TryRestartElevated(string? dataDirectory)
    {
        Restarts.Add(dataDirectory);
        return true;
    }
}

public sealed class ElevationSafetyTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private ProtectedFolders Protecting(string programFiles, string programData) =>
        _box.Protected with { ProgramFiles = [programFiles], ProgramData = [programData] };

    [Fact]
    public void Folders_inside_program_files_and_program_data_need_administrator_rights()
    {
        var programFiles = _box.NewFolder("Program Files");
        var programData = _box.NewFolder("ProgramData");
        var app = Directory.CreateDirectory(Path.Combine(programFiles, "App")).FullName;
        var shared = Directory.CreateDirectory(Path.Combine(programData, "App")).FullName;
        var folders = Protecting(programFiles, programData);

        var normal = new FolderSafety(_box.Paths, folders);
        var elevated = new FolderSafety(_box.Paths, folders, elevated: true);

        Assert.Equal(FolderRefusal.ProgramFiles, normal.Check(app).Refusal);
        Assert.Equal(FolderRefusal.ProgramData, normal.Check(shared).Refusal);
        Assert.True(elevated.Check(app).IsAllowed);
        Assert.True(elevated.Check(shared).IsAllowed);
    }

    [Fact]
    public void The_top_folders_stay_refused_with_administrator_rights()
    {
        var programFiles = _box.NewFolder("Program Files");
        var programData = _box.NewFolder("ProgramData");
        var elevated = new FolderSafety(_box.Paths, Protecting(programFiles, programData), elevated: true);

        Assert.Equal(FolderRefusal.ProgramFiles, elevated.Check(programFiles).Refusal);
        Assert.Equal(FolderRefusal.ProgramData, elevated.Check(programData).Refusal);
    }

    [Fact]
    public void Every_other_protected_place_stays_refused_with_administrator_rights()
    {
        var windows = _box.NewFolder("Windows");
        var system = Directory.CreateDirectory(Path.Combine(windows, "System32")).FullName;
        var launcher = Directory.CreateDirectory(Path.Combine(_box.FakeAppData, "Launcher")).FullName;
        var elevated = new FolderSafety(_box.Paths, _box.Protected with { Windows = [windows] }, elevated: true);

        Assert.Equal(FolderRefusal.WindowsDirectory, elevated.Check(system).Refusal);
        Assert.Equal(FolderRefusal.AppData, elevated.Check(launcher).Refusal);
        Assert.Equal(FolderRefusal.FoldcraftData, elevated.Check(_box.Paths.DataDirectory).Refusal);
    }
}

public sealed class ElevationAccountTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    [Fact]
    public void Another_account_is_refused_only_with_administrator_rights()
    {
        var other = _box.NewFolder("Other Account");
        var documents = Directory.CreateDirectory(Path.Combine(other, "Documents")).FullName;
        var folders = _box.Protected with { OtherProfiles = [other] };

        var elevated = new FolderSafety(_box.Paths, folders, elevated: true);
        var normal = new FolderSafety(_box.Paths, folders);

        Assert.Equal(FolderRefusal.OtherAccount, elevated.Check(other).Refusal);
        Assert.Equal(FolderRefusal.OtherAccount, elevated.Check(documents).Refusal);
        Assert.True(normal.Check(documents).IsAllowed);
    }

    [Fact]
    public void The_list_of_other_accounts_leaves_out_this_one()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.DoesNotContain(profile, ProtectedFolders.Detect().OtherProfiles, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ElevationLinkTests
{
    private const string Link = "https://github.com/28Y8/Foldcraft";

    [Fact]
    public void A_link_opens_as_usual_without_administrator_rights()
    {
        Assert.Equal(Link, WpfDialogs.StartInfoForLink(Link, elevated: false).FileName);
    }

    [Fact]
    public void A_link_goes_through_explorer_with_administrator_rights()
    {
        var start = WpfDialogs.StartInfoForLink(Link, elevated: true);

        Assert.EndsWith("explorer.exe", start.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([Link], start.ArgumentList);
    }
}

public sealed class ElevationViewModelTests
{
    private sealed class RefusedElevation : IElevation
    {
        public bool IsElevated => false;

        public bool TryRestartElevated(string? dataDirectory) =>
            throw new System.ComponentModel.Win32Exception(5);
    }

    [Fact]
    public void A_restart_Windows_refuses_says_so()
    {
        using var app = new AppHarness(elevation: new RefusedElevation());

        app.Settings.RestartAsAdministratorCommand.Execute(null);

        Assert.Single(app.Dialogs.Errors);
    }

    [Fact]
    public void The_install_folder_note_fits_a_copy_with_the_rights()
    {
        using var app = new AppHarness(elevation: new FakeElevation(isElevated: true));

        Assert.Contains("running as administrator", app.Shell.Apps.InstallFolderExplanation);
        Assert.DoesNotContain("Restarting as administrator", app.Shell.Apps.InstallFolderExplanation);
    }

    [Fact]
    public void A_restart_only_happens_after_the_person_agrees()
    {
        var elevation = new FakeElevation();
        using var app = new AppHarness(elevation: elevation);

        app.Dialogs.ConfirmAnswer = false;
        app.Settings.RestartAsAdministratorCommand.Execute(null);

        Assert.Empty(elevation.Restarts);
    }

    [Fact]
    public void A_restart_keeps_the_data_folder_it_was_started_with()
    {
        var elevation = new FakeElevation();
        using var app = new AppHarness(customDataDirectory: @"D:\Trial", elevation: elevation);

        app.Settings.RestartAsAdministratorCommand.Execute(null);

        Assert.Equal([@"D:\Trial"], elevation.Restarts);
    }

    [Fact]
    public void A_copy_with_the_rights_has_nothing_to_restart_for()
    {
        using var app = new AppHarness(elevation: new FakeElevation(isElevated: true));

        Assert.True(app.Settings.IsRunningAsAdministrator);
        Assert.False(app.Settings.RestartAsAdministratorCommand.CanExecute(null));
    }

    [Fact]
    public void Settings_says_the_rights_are_not_required()
    {
        using var app = new AppHarness();

        Assert.False(app.Settings.IsRunningAsAdministrator);
        Assert.Contains("doesn't require administrator rights", app.Settings.AdministratorExplanation);
    }
}

public sealed class ReplacesOptionTests
{
    [Fact]
    public void The_copy_to_wait_for_is_read_from_the_command_line()
    {
        var options = AppOptions.Parse(["--replaces", "1234", "--data-dir", @"D:\Trial"]);

        Assert.Equal(1234, options.ReplacesProcess);
        Assert.Equal(@"D:\Trial", options.DataDirectory);
    }

    [Fact]
    public void Anything_that_is_not_a_process_number_is_ignored()
    {
        Assert.Null(AppOptions.Parse(["--replaces", "soon"]).ReplacesProcess);
    }
}
