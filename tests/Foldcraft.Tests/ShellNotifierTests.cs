using Foldcraft.Core.Customization;

namespace Foldcraft.Tests;

/// <summary>
/// Explorer learns about a folder from one notice, sent when the folder is finished. These prove
/// that every path sends it. Only the last few talk to the real shell, on sandbox folders.
/// </summary>
public sealed class ShellNotifierTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    /// <summary>A view told any earlier could look the folder up under the wrong date.</summary>
    [Fact]
    public void The_shell_hears_about_a_folder_only_once_it_is_finished_and_its_date_is_set()
    {
        var folder = _box.NewFolder();
        var original = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(folder, original);

        DateTime? dateAtNotice = null;
        var iniAtNotice = false;

        var shell = new CallbackShell(path =>
        {
            dateAtNotice = Directory.GetLastWriteTimeUtc(path);
            iniAtNotice = File.Exists(Sandbox.IniPath(path));
        });

        var customizer = new FolderCustomizer(_box.Safety, _box.Journal, _box.Icons, shell);
        customizer.Apply(folder, Sandbox.BlueStar);

        Assert.Equal(original.AddTicks(1), dateAtNotice);
        Assert.True(iniAtNotice);

        new FolderRestorer(_box.Safety, _box.Journal, shell).UndoLast();

        Assert.Equal(original, dateAtNotice);
        Assert.False(iniAtNotice);
    }

    [Fact]
    public void Apply_undo_and_restore_all_tell_the_shell()
    {
        var customizer = _box.Customizer();
        var restorer = _box.Restorer();
        var folder = _box.NewFolder();

        customizer.Apply(folder, Sandbox.BlueStar);
        Assert.Equal([folder], _box.Shell.Notified);

        restorer.UndoLast();
        Assert.Equal([folder, folder], _box.Shell.Notified);

        customizer.Apply(folder, Sandbox.GreenHeart);
        restorer.RestoreFolder(folder);
        Assert.Equal([folder, folder, folder, folder], _box.Shell.Notified);

        customizer.Apply(folder, Sandbox.BlueStar);
        restorer.RestoreAll();
        Assert.Equal(6, _box.Shell.Notified.Count);
    }

    [Fact]
    public void A_repair_tells_the_shell()
    {
        var customizer = _box.Customizer();
        var repairer = new FolderRepairer(_box.Safety, _box.Journal, _box.Icons, customizer, _box.Shell);
        var folder = _box.NewFolder();

        customizer.Apply(folder, Sandbox.BlueStar);

        File.Delete(Assert.Single(_box.Journal.Read().Entries).Change.IconPath);
        _box.Shell.Notified.Clear();

        repairer.RepairAll();

        Assert.Equal([folder], _box.Shell.Notified);
    }

    [Fact]
    public void A_rolled_back_apply_tells_the_shell()
    {
        var folder = _box.NewFolder();
        var customizer = _box.Customizer(new FaultyOperations { FailCommit = _ => true });

        Assert.Equal(ApplyOutcome.Failed, customizer.Apply(folder, Sandbox.BlueStar).Outcome);
        Assert.Equal([folder], _box.Shell.Notified);
    }

    [Fact]
    public void A_folder_made_for_an_app_is_announced_too()
    {
        var folder = Path.Combine(_box.NewFolder("apps"), "New app");

        _box.Customizer().CreateFolder(folder, [], Sandbox.BlueStar, Guid.NewGuid());

        Assert.Equal([folder], _box.Shell.Notified);
    }

    [Fact]
    public void The_real_notice_leaves_a_folder_and_its_files_exactly_as_they_were()
    {
        var folder = _box.NewFolder("real");
        var file = Path.Combine(folder, "note.txt");
        var date = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        File.WriteAllText(file, "keep");
        Directory.SetLastWriteTimeUtc(folder, date);

        var written = File.GetLastWriteTimeUtc(file);

        new ShellNotifier().FolderChanged(folder);

        Assert.Equal(date, Directory.GetLastWriteTimeUtc(folder));
        Assert.Equal("keep", File.ReadAllText(file));
        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
        Assert.Equal(["note.txt"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    public void The_real_notice_shrugs_off_a_folder_that_is_not_there()
    {
        new ShellNotifier().FolderChanged(Path.Combine(_box.Root, "not-here"));
    }
}
