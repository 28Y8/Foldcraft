using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Icons;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Ini;
using Foldcraft.Core.Journal;

namespace Foldcraft.Tests;

public sealed class RepairTests : IDisposable
{
    private static readonly DateTime Earlier = new(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private FolderRepairer Repairer(FileOperations? ops = null)
    {
        var icons = ops is null ? _box.Icons : new IconLibrary(_box.Paths, operations: ops);
        var customizer = new FolderCustomizer(_box.Safety, _box.Journal, icons, _box.Shell);

        return new FolderRepairer(_box.Safety, _box.Journal, icons, customizer, _box.Shell);
    }

    /// <summary>Customizes a folder and returns it with its icon's path.</summary>
    private (string Folder, string Icon) Customized(FolderIconSpec? spec = null, string name = "target")
    {
        var folder = _box.NewFolder(name);
        _box.Customizer().Apply(folder, spec ?? Sandbox.BlueStar);
        return (folder, Assert.Single(_box.Journal.Read().Active, e => e.FolderPath == folder).Change.IconPath);
    }

    /// <summary>
    /// A folder as an older renderer left it: pointing at a healthy icon under an old name. The
    /// record is written by hand because a real older version isn't available here.
    /// </summary>
    private string OlderFolder(FolderIconSpec spec)
    {
        var folder = _box.NewFolder("older");
        var oldIcon = Path.Combine(_box.Paths.IconsDirectory, "0123456789abcdef0123456789abcdef.ico");

        Directory.CreateDirectory(_box.Paths.IconsDirectory);
        File.WriteAllBytes(oldIcon, new IconRenderer().RenderIco(spec));

        var document = DesktopIni.CreateEmpty();
        document.SetValue(DesktopIni.ShellClassInfoSection, DesktopIni.IconResourceKey, $"{oldIcon},0");

        var bytes = document.ToBytes();
        Sandbox.PlantIni(folder, bytes);
        File.SetAttributes(folder, FileAttributes.ReadOnly);

        var id = _box.Journal.RecordChange(new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = folder,
            DesktopIniExisted = false,
            OriginalFolderAttributes = FileAttributes.Directory,
            IconPath = oldIcon,
            WrittenSha256 = FolderState.Sha256Hex(bytes),
            SpecJson = IconSpecCodec.ToRecordedJson(spec, null),
        });

        _box.Journal.MarkApplied(id);
        return folder;
    }

    [Fact]
    public void A_healthy_folder_needs_nothing()
    {
        Customized();

        Assert.Equal(IconCondition.Healthy, Assert.Single(Repairer().Scan()).Condition);
        Assert.Empty(Repairer().RepairAll());
        Assert.Single(_box.Journal.Read().Entries);
    }

    [Fact]
    public void Each_apply_records_the_look_it_drew()
    {
        var (folder, _) = Customized(Sandbox.GreenHeart);
        var change = Assert.Single(_box.Journal.Read().Entries, e => e.FolderPath == folder).Change;

        Assert.True(IconSpecCodec.TryReadRecorded(change.SpecJson, out var spec, out _));
        Assert.Equal(Sandbox.GreenHeart, spec);
    }

    [Fact]
    public void A_missing_icon_is_found_and_drawn_again_under_the_same_name()
    {
        var (folder, icon) = Customized();
        var original = File.ReadAllBytes(icon);
        File.Delete(icon);

        Assert.Equal(IconCondition.Missing, Assert.Single(Repairer().Scan()).Condition);

        var result = Assert.Single(Repairer().RepairAll());

        Assert.Equal(RepairOutcome.Repaired, result.Outcome);
        Assert.Equal(folder, result.FolderPath);
        Assert.Equal(original, File.ReadAllBytes(icon));
    }

    [Fact]
    public void A_damaged_icon_is_found_and_drawn_again()
    {
        var (_, icon) = Customized();
        File.WriteAllBytes(icon, [1, 2, 3]);

        Assert.Equal(IconCondition.Damaged, Assert.Single(Repairer().Scan()).Condition);

        Repairer().RepairAll();

        Assert.Equal(IconFileState.Intact, IconLibrary.Inspect(icon));
    }

    [Fact]
    public void Redrawing_an_icon_changes_nothing_else()
    {
        var (folder, icon) = Customized();
        Directory.SetLastWriteTimeUtc(folder, Earlier);
        var iniBytes = File.ReadAllBytes(Sandbox.IniPath(folder));
        var iniTime = File.GetLastWriteTimeUtc(Sandbox.IniPath(folder));
        var attributes = File.GetAttributes(folder);
        File.Delete(icon);

        Repairer().RepairAll();

        Assert.Equal(iniBytes, File.ReadAllBytes(Sandbox.IniPath(folder)));
        Assert.Equal(iniTime, File.GetLastWriteTimeUtc(Sandbox.IniPath(folder)));
        Assert.Equal(attributes, File.GetAttributes(folder));
        Assert.Equal(Earlier, Directory.GetLastWriteTimeUtc(folder));
    }

    [Fact]
    public void Redrawing_an_icon_is_journaled()
    {
        var (folder, icon) = Customized();
        File.Delete(icon);

        Repairer().RepairAll();

        var entries = _box.Journal.Read().Entries;

        Assert.Equal(2, entries.Count);
        Assert.Equal(JournalEntryState.Applied, entries[1].State);
        Assert.True(entries[1].Change.IsRepair);
        Assert.True(entries[1].Change.LeftFolderAlone);
        Assert.Equal(folder, entries[1].FolderPath);
    }

    [Fact]
    public void A_redrawn_folder_still_restores_all_the_way_back()
    {
        var (folder, icon) = Customized();
        File.Delete(icon);
        Repairer().RepairAll();

        Assert.True(_box.Restorer().RestoreFolder(folder).AllRestored);

        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void An_icon_that_cannot_be_written_is_a_failure_and_undoes_cleanly()
    {
        var (folder, icon) = Customized();
        File.Delete(icon);

        var ops = new FaultyOperations { FailWrite = _ => true };
        var result = Assert.Single(Repairer(ops).RepairAll());

        Assert.Equal(RepairOutcome.Failed, result.Outcome);
        Assert.Equal(JournalEntryState.RolledBack, _box.Journal.Read().Entries[^1].State);
        Assert.True(File.Exists(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void An_out_of_date_icon_moves_to_a_new_file_through_a_journaled_change()
    {
        var folder = OlderFolder(Sandbox.GreenHeart);
        var before = File.ReadAllBytes(Sandbox.IniPath(folder));

        Assert.Equal(IconCondition.OutOfDate, Assert.Single(Repairer().Scan()).Condition);
        Assert.Equal(RepairOutcome.Repaired, Assert.Single(Repairer().RepairAll()).Outcome);

        var entries = _box.Journal.Read().Entries;
        var expected = _box.Icons.ExpectedPath(Sandbox.GreenHeart);

        Assert.Equal(2, entries.Count);
        Assert.True(entries[1].Change.IsRepair);
        Assert.False(entries[1].Change.LeftFolderAlone);
        Assert.Equal(expected, entries[1].Change.IconPath);
        Assert.True(File.Exists(expected));
        Assert.Contains(expected, DesktopIni.Load(Sandbox.IniPath(folder)).GetValue(DesktopIni.ShellClassInfoSection, DesktopIni.IconResourceKey));

        Assert.Equal(IconCondition.Healthy, Assert.Single(Repairer().Scan()).Condition);

        Assert.True(_box.Restorer().UndoBatch(entries[1].Change.BatchId).AllRestored);
        Assert.Equal(before, File.ReadAllBytes(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void A_record_from_before_looks_were_recorded_cannot_be_repaired_and_says_so()
    {
        var folder = _box.NewFolder();
        var missing = Path.Combine(_box.Paths.IconsDirectory, "gone.ico");
        var document = DesktopIni.CreateEmpty();
        document.SetValue(DesktopIni.ShellClassInfoSection, DesktopIni.IconResourceKey, $"{missing},0");
        var bytes = document.ToBytes();
        Sandbox.PlantIni(folder, bytes);

        var id = _box.Journal.RecordChange(new ChangeRecord
        {
            BatchId = Guid.NewGuid(),
            FolderPath = folder,
            DesktopIniExisted = false,
            OriginalFolderAttributes = File.GetAttributes(folder),
            IconPath = missing,
            WrittenSha256 = FolderState.Sha256Hex(bytes),
        });

        _box.Journal.MarkApplied(id);

        Assert.Equal(IconCondition.NoRecordedLook, Assert.Single(Repairer().Scan()).Condition);
        Assert.Equal(RepairOutcome.NotRepairable, Assert.Single(Repairer().RepairAll()).Outcome);
        Assert.False(File.Exists(missing));
    }

    private string WritePicture(Color color)
    {
        var path = Path.Combine(_box.Root, "picture.png");

        using var bitmap = new Bitmap(32, 32);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(color);
        }

        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    private static FolderIconSpec WithPicture(string path) =>
        new() { Fill = FolderFill.Solid(IconColor.Parse("#2F6FED")), CustomImagePath = path };

    [Fact]
    public void A_picture_that_is_still_there_can_be_drawn_again()
    {
        var (_, icon) = Customized(WithPicture(WritePicture(Color.Red)));
        File.Delete(icon);

        Assert.Equal(RepairOutcome.Repaired, Assert.Single(Repairer().RepairAll()).Outcome);
        Assert.True(File.Exists(icon));
    }

    [Fact]
    public void An_icon_whose_picture_has_gone_cannot_be_repaired()
    {
        var picture = WritePicture(Color.Red);
        var (_, icon) = Customized(WithPicture(picture));
        File.Delete(icon);
        File.Delete(picture);

        Assert.Equal(IconCondition.PictureGone, Assert.Single(Repairer().Scan()).Condition);
        Assert.Equal(RepairOutcome.NotRepairable, Assert.Single(Repairer().RepairAll()).Outcome);
    }

    [Fact]
    public void An_icon_whose_picture_has_changed_is_not_redrawn_with_the_new_picture()
    {
        var picture = WritePicture(Color.Red);
        var (_, icon) = Customized(WithPicture(picture));
        File.Delete(icon);
        WritePicture(Color.Lime);

        Assert.Equal(IconCondition.PictureGone, Assert.Single(Repairer().Scan()).Condition);
        Repairer().RepairAll();
        Assert.False(File.Exists(icon));
    }

    [Fact]
    public void A_folder_something_else_changed_is_left_alone()
    {
        var (folder, icon) = Customized();
        File.Delete(icon);
        AppHarness.EditIni(folder);

        var bytes = File.ReadAllBytes(Sandbox.IniPath(folder));

        Assert.Equal(IconCondition.LeftAlone, Assert.Single(Repairer().Scan()).Condition);
        Assert.Empty(Repairer().RepairAll());
        Assert.False(File.Exists(icon));
        Assert.Equal(bytes, File.ReadAllBytes(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void A_folder_that_is_gone_is_left_alone()
    {
        var (folder, icon) = Customized();
        File.Delete(icon);
        File.SetAttributes(folder, FileAttributes.Directory);
        Directory.Delete(folder, recursive: true);

        Assert.Equal(IconCondition.LeftAlone, Assert.Single(Repairer().Scan()).Condition);
        Assert.Empty(Repairer().RepairAll());
    }

    [Fact]
    public void An_undone_folder_is_not_looked_at()
    {
        var (_, icon) = Customized();
        _box.Restorer().UndoLast();
        File.Delete(icon);

        Assert.Empty(Repairer().Scan());
    }

    private sealed class Counted : IProgress<int>
    {
        public List<int> Reports { get; } = [];

        public void Report(int value) => Reports.Add(value);
    }

    [Fact]
    public void Every_folder_counts_toward_progress_healthy_or_not()
    {
        for (var i = 0; i < 5; i++)
        {
            Customized(name: $"folder {i}");
        }

        var counted = new Counted();

        Repairer().RepairAll(examined: counted);

        Assert.Equal([1, 2, 3, 4, 5], counted.Reports);
    }

    [Fact]
    public void A_run_says_how_each_folder_it_looked_at_turned_out()
    {
        Customized(name: "fine");
        var (missing, icon) = Customized(Sandbox.GreenHeart, "missing icon");
        var (gone, _) = Customized(Sandbox.GreenHeart, "moved away");

        File.Delete(icon);
        Directory.Move(gone, gone + " elsewhere");

        var report = Repairer().RepairAll();

        Assert.Equal(missing, Assert.Single(report).FolderPath);
        Assert.Equal(3, report.Total);
        Assert.Equal(1, report.Healthy);
        Assert.Equal(1, report.LeftAlone);
        Assert.Equal(0, report.NotLookedAt);
        Assert.False(report.WasCanceled);
    }

    [Fact]
    public void A_run_stopped_before_the_first_folder_says_nothing_was_looked_at()
    {
        var (_, icon) = Customized();
        File.Delete(icon);

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var report = Repairer().RepairAll(cancel.Token);

        Assert.Empty(report);
        Assert.True(report.WasCanceled);
        Assert.Equal(0, report.Healthy);
        Assert.Equal(1, report.NotLookedAt);
    }

    [Fact]
    public void A_scan_writes_nothing()
    {
        var (_, icon) = Customized();
        File.Delete(icon);
        var journal = File.ReadAllBytes(_box.Paths.JournalFile);

        Repairer().Scan();

        Assert.False(File.Exists(icon));
        Assert.Equal(journal, File.ReadAllBytes(_box.Paths.JournalFile));
    }

    [Fact]
    public void Text_in_a_recorded_look_that_is_not_json_counts_as_no_record()
    {
        Assert.False(IconSpecCodec.TryReadRecorded("not json", out _, out _));
        Assert.False(IconSpecCodec.TryReadRecorded(null, out _, out _));
        Assert.False(IconSpecCodec.TryReadRecorded("{\"fill\":{\"start\":\"#000000\"},\"glyph\":\"gone-now\"}", out _, out _));
        Assert.Equal("{\"fill\":{\"start\":\"#000000\"}}", Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(
            IconSpecCodec.ToRecordedJson(new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#000000")) }, null))));
    }
}
