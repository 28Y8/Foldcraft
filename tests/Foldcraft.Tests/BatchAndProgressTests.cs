using Foldcraft.Core.Customization;
using Foldcraft.Core.Journal;
using Foldcraft.Core.Safety;

namespace Foldcraft.Tests;

/// <summary>The Core additions the app's progress and per-entry undo rely on.</summary>
public sealed class BatchAndProgressTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    /// <summary>Reports on the calling thread, unlike Progress&lt;T&gt;.</summary>
    private sealed class ListProgress<T> : IProgress<T>
    {
        public List<T> Items { get; } = [];

        public void Report(T value) => Items.Add(value);
    }

    [Fact]
    public void Apply_reports_every_folder_in_order_as_it_goes()
    {
        var folders = new[] { _box.NewFolder("a"), _box.NewFolder("b") };
        var progress = new ListProgress<FolderApplyResult>();

        var batch = _box.Customizer().Apply(folders, Sandbox.BlueStar, default, progress);

        Assert.Equal(batch.Results, progress.Items);
    }

    [Fact]
    public void Apply_reports_folders_skipped_by_canceling()
    {
        var folders = new[] { _box.NewFolder("a"), _box.NewFolder("b") };
        var progress = new ListProgress<FolderApplyResult>();

        var batch = _box.Customizer().Apply(folders, Sandbox.BlueStar, new CancellationToken(true), progress);

        Assert.All(batch.Results, r => Assert.Equal(ApplyOutcome.Canceled, r.Outcome));
        Assert.Equal(2, progress.Items.Count);
    }

    [Fact]
    public void Apply_reports_every_folder_when_the_icon_cannot_be_made()
    {
        var folders = new[] { _box.NewFolder("a"), _box.NewFolder("b") };
        var progress = new ListProgress<FolderApplyResult>();
        var spec = Sandbox.BlueStar with { GlyphId = null, CustomImagePath = Path.Combine(_box.Root, "missing.png") };

        var batch = _box.Customizer().Apply(folders, spec, default, progress);

        Assert.Equal(batch.Results, progress.Items);
        Assert.All(progress.Items, r => Assert.Equal(ApplyOutcome.Failed, r.Outcome));
    }

    [Fact]
    public void UndoBatch_reverses_an_older_batch_and_leaves_other_folders_alone()
    {
        var customizer = _box.Customizer();
        var first = _box.NewFolder("first");
        var second = _box.NewFolder("second");

        var older = customizer.Apply([first], Sandbox.BlueStar);
        customizer.Apply([second], Sandbox.GreenHeart);

        var report = _box.Restorer().UndoBatch(older.BatchId);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.False(File.Exists(Sandbox.IniPath(first)));
        Assert.True(File.Exists(Sandbox.IniPath(second)));
    }

    [Fact]
    public void UndoBatch_returns_a_folder_to_just_before_that_batch()
    {
        var customizer = _box.Customizer();
        var folder = _box.NewFolder();

        customizer.Apply([folder], Sandbox.BlueStar);
        var afterFirst = File.ReadAllBytes(Sandbox.IniPath(folder));
        var second = customizer.Apply([folder], Sandbox.GreenHeart);

        _box.Restorer().UndoBatch(second.BatchId);

        Assert.Equal(afterFirst, File.ReadAllBytes(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void UndoBatch_of_an_older_change_also_reverses_newer_changes_to_that_folder()
    {
        var customizer = _box.Customizer();
        var folder = _box.NewFolder();
        var attributes = File.GetAttributes(folder);

        var older = customizer.Apply([folder], Sandbox.BlueStar);
        customizer.Apply([folder], Sandbox.GreenHeart);

        var report = _box.Restorer().UndoBatch(older.BatchId);

        Assert.Equal(RestoreOutcome.Restored, Assert.Single(report.Results).Outcome);
        Assert.False(File.Exists(Sandbox.IniPath(folder)));
        Assert.Equal(attributes, File.GetAttributes(folder));
        Assert.Empty(_box.Journal.Read().Active);
    }

    [Fact]
    public void UndoBatch_with_a_folder_filter_touches_only_those_folders()
    {
        var a = _box.NewFolder("a");
        var b = _box.NewFolder("b");
        var batch = _box.Customizer().Apply([a, b], Sandbox.BlueStar);

        var report = _box.Restorer().UndoBatch(batch.BatchId, onlyFolders: [a.ToUpperInvariant()]);

        Assert.Single(report.Results);
        Assert.False(File.Exists(Sandbox.IniPath(a)));
        Assert.True(File.Exists(Sandbox.IniPath(b)));
    }

    [Fact]
    public void UndoBatch_of_an_unknown_batch_does_nothing()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);

        Assert.Empty(_box.Restorer().UndoBatch(Guid.NewGuid()).Results);
        Assert.True(File.Exists(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void UndoBatch_reports_conflicts_and_touches_nothing_by_default()
    {
        var folder = _box.NewFolder();
        var batch = _box.Customizer().Apply([folder], Sandbox.BlueStar);
        AppHarness.EditIni(folder);
        var edited = File.ReadAllBytes(Sandbox.IniPath(folder));

        var report = _box.Restorer().UndoBatch(batch.BatchId);

        Assert.Equal(RestoreOutcome.Conflict, Assert.Single(report.Results).Outcome);
        Assert.Equal(edited, File.ReadAllBytes(Sandbox.IniPath(folder)));
    }

    [Fact]
    public void Undo_and_restore_all_report_progress()
    {
        var a = _box.NewFolder("a");
        var b = _box.NewFolder("b");
        var batch = _box.Customizer().Apply([a, b], Sandbox.BlueStar);

        var undo = new ListProgress<FolderRestoreResult>();
        var undone = _box.Restorer().UndoBatch(batch.BatchId, progress: undo);
        Assert.Equal(undone.Results, undo.Items);

        _box.Customizer().Apply([a, b], Sandbox.GreenHeart);

        var all = new ListProgress<FolderRestoreResult>();
        var restored = _box.Restorer().RestoreAll(progress: all);
        Assert.Equal(restored.Results, all.Items);
        Assert.Equal(2, all.Items.Count);
    }

    [Fact]
    public void A_restore_refused_by_safety_says_why()
    {
        var folder = _box.NewFolder();
        var target = _box.NewFolder("elsewhere");
        var batch = _box.Customizer().Apply([folder], Sandbox.BlueStar);

        // The folder is replaced by a junction, which must be refused rather than followed.
        var moved = folder + "-moved";
        Directory.Move(folder, moved);
        Sandbox.CreateJunction(folder, target);

        var result = Assert.Single(_box.Restorer().UndoBatch(batch.BatchId).Results);

        Assert.Equal(RestoreOutcome.Refused, result.Outcome);
        Assert.Equal(FolderRefusal.ReparsePoint, result.Refusal);
        Assert.Contains(_box.Journal.Read().Entries, e => e.State == JournalEntryState.Applied);
    }
}
