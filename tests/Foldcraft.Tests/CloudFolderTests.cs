using Foldcraft.Core.Customization;
using Foldcraft.Core.Infrastructure;
using Foldcraft.Core.Safety;

namespace Foldcraft.Tests;

/// <summary>
/// A real cloud folder needs a sync provider registered with Windows, which can't be done in
/// temp. A real junction stands in for one here, with its tag reader swapped, because the
/// junction carries the reparse attribute the code has to get past.
/// </summary>
public sealed class CloudFolderTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private string NewLink()
    {
        var target = _box.NewFolder("target");
        return Sandbox.CreateJunction(Path.Combine(_box.Root, "folders", "link"), target);
    }

    private FolderSafety SafetySeeing(uint? tag) => new(_box.Paths, _box.Protected) { ReadReparseTag = _ => tag };

    [Theory]
    [InlineData(0x9000001Au)]
    [InlineData(0x9000101Au)]
    [InlineData(0x9000701Au)]
    [InlineData(0x9000F01Au)]
    public void Every_cloud_tag_variant_is_allowed(uint tag)
    {
        var result = SafetySeeing(tag).Check(NewLink());

        Assert.True(result.IsAllowed, result.Reason);
    }

    [Theory]
    [InlineData(ReparseTags.MountPoint)]
    [InlineData(ReparseTags.SymbolicLink)]
    [InlineData(0x80000023u)] // AF_UNIX
    [InlineData(0x8000001Bu)] // App execution alias
    [InlineData(0x80000021u)] // OneDrive's older tag, which isn't a Cloud Files tag
    [InlineData(0x9000001Bu)] // One bit away from the cloud tag
    [InlineData(0x9001001Au)] // A bit outside the variant mask
    [InlineData(0x00000000u)]
    public void Every_other_tag_is_refused(uint tag)
    {
        Assert.Equal(FolderRefusal.ReparsePoint, SafetySeeing(tag).Check(NewLink()).Refusal);
    }

    [Fact]
    public void A_tag_that_cannot_be_read_is_refused()
    {
        Assert.Equal(FolderRefusal.ReparsePoint, SafetySeeing(null).Check(NewLink()).Refusal);
    }

    [Fact]
    public void The_tag_of_a_real_junction_is_read_from_the_file_system()
    {
        var link = NewLink();

        Assert.Equal(ReparseTags.MountPoint, ReparseTags.Read(link));
        Assert.Equal(FolderRefusal.ReparsePoint, _box.Safety.Check(link).Refusal);
    }

    [Fact]
    public void An_ordinary_folder_reads_as_no_tag()
    {
        Assert.Equal(0u, ReparseTags.Read(_box.NewFolder()));
    }

    [Fact]
    public void A_missing_path_has_no_tag()
    {
        Assert.Null(ReparseTags.Read(Path.Combine(_box.Root, "not-here")));
    }

    [Fact]
    public void A_cloud_tag_is_not_guessed_from_the_path()
    {
        var target = _box.NewFolder("target");
        var link = Sandbox.CreateJunction(Path.Combine(_box.Root, "folders", "OneDrive"), target);

        Assert.Equal(FolderRefusal.ReparsePoint, _box.Safety.Check(link).Refusal);
    }

    [Fact]
    public void A_folder_inside_a_cloud_folder_is_allowed()
    {
        var link = NewLink();
        var nested = Path.Combine(link, "nested");
        Directory.CreateDirectory(nested);

        var seesCloudOnlyOnLink = new FolderSafety(_box.Paths, _box.Protected)
        {
            ReadReparseTag = path => path.Equals(link, StringComparison.OrdinalIgnoreCase) ? ReparseTags.Cloud : ReparseTags.MountPoint,
        };

        Assert.True(seesCloudOnlyOnLink.Check(nested).IsAllowed);
    }

    [Fact]
    public void A_junction_under_a_cloud_folder_is_still_refused()
    {
        var link = NewLink();
        var inner = Sandbox.CreateJunction(Path.Combine(link, "inner"), _box.NewFolder("other"));

        var seesCloudOnlyOnLink = new FolderSafety(_box.Paths, _box.Protected)
        {
            ReadReparseTag = path => path.Equals(link, StringComparison.OrdinalIgnoreCase) ? ReparseTags.Cloud : ReparseTags.MountPoint,
        };

        Assert.Equal(FolderRefusal.ReparsePoint, seesCloudOnlyOnLink.Check(inner).Refusal);
    }

    [Fact]
    public void A_cloud_folder_inside_a_protected_place_is_still_refused()
    {
        var target = _box.NewFolder("target");
        var link = Sandbox.CreateJunction(Path.Combine(_box.FakeAppData, "synced"), target);

        Assert.Equal(FolderRefusal.AppData, SafetySeeing(ReparseTags.Cloud).Check(link).Refusal);
    }

    [Fact]
    public void A_cloud_folder_can_be_changed_and_undone()
    {
        var link = NewLink();
        var safety = SafetySeeing(ReparseTags.Cloud);
        var customizer = new FolderCustomizer(safety, _box.Journal, _box.Icons, _box.Shell);
        var restorer = new FolderRestorer(safety, _box.Journal, _box.Shell);

        Assert.Equal(ApplyOutcome.Applied, customizer.Apply(link, Sandbox.BlueStar).Outcome);
        Assert.True(File.Exists(Sandbox.IniPath(link)));

        Assert.True(restorer.UndoLast().AllRestored);
        Assert.False(File.Exists(Sandbox.IniPath(link)));
    }

    /// <summary>Setting a date reads nothing in the folder, so such a folder gets the same treatment as any other.</summary>
    [Fact]
    public void A_cloud_folder_gets_its_new_look_and_its_date_back_like_any_other()
    {
        var link = NewLink();
        var date = new DateTime(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var safety = SafetySeeing(ReparseTags.Cloud);
        var customizer = new FolderCustomizer(safety, _box.Journal, _box.Icons, _box.Shell);
        var restorer = new FolderRestorer(safety, _box.Journal, _box.Shell);

        Directory.SetLastWriteTimeUtc(link, date);

        customizer.Apply(link, Sandbox.BlueStar);
        Assert.Equal(date.AddTicks(1), Directory.GetLastWriteTimeUtc(link));

        restorer.UndoLast();
        Assert.Equal(date, Directory.GetLastWriteTimeUtc(link));
    }
}
