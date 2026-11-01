using System.Drawing;
using System.Drawing.Imaging;
using Foldcraft.App.Services;
using Foldcraft.App.ViewModels;
using Foldcraft.Core.Icons;

namespace Foldcraft.Tests;

public sealed class DesignerViewModelTests : IDisposable
{
    private readonly Sandbox _box = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly IconRenderer _renderer = new();
    private readonly DesignerViewModel _designer;

    public DesignerViewModelTests()
    {
        _designer = new DesignerViewModel(_renderer, _dialogs, new FileLog(_box.Paths.LogFile), TimeSpan.Zero);
    }

    public void Dispose() => _box.Dispose();

    private string WritePicture()
    {
        var path = Path.Combine(_box.Root, "picture.png");

        using var bitmap = new Bitmap(32, 32);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    private void PickPicture(string path)
    {
        _dialogs.ImageToPick = path;
        _designer.PickImageCommand.Execute(null);
    }

    [Theory]
    [InlineData(1.25, 40, 20)]
    [InlineData(1.5, 48, 24)]
    [InlineData(2.0, 64, 32)]
    public async Task Small_previews_are_drawn_at_the_display_real_size(double scale, int medium, int small)
    {
        _designer.DisplayScale = scale;
        await _designer.PreviewTask;

        Assert.Equal(_renderer.RenderPreviewPng(_designer.Spec, medium), _designer.MediumPreview);
        Assert.Equal(_renderer.RenderPreviewPng(_designer.Spec, small), _designer.SmallPreview);
    }

    [Fact]
    public async Task A_huge_display_scale_stays_within_what_the_renderer_accepts()
    {
        _designer.DisplayScale = 20;
        await _designer.PreviewTask;

        Assert.Null(_designer.PreviewError);
        Assert.Equal(_renderer.RenderPreviewPng(_designer.Spec, 256), _designer.MediumPreview);
    }

    [Fact]
    public void Starts_as_a_plain_solid_folder_that_can_be_applied()
    {
        var spec = _designer.Spec;

        Assert.Null(spec.Fill.End);
        Assert.Null(spec.GlyphId);
        Assert.Null(spec.CustomImagePath);
        Assert.True(_designer.CanApply);
        Assert.Equal(_designer.Swatches[0], _designer.SelectedSwatch);
    }

    [Fact]
    public void The_palette_has_about_sixteen_distinct_named_colors()
    {
        Assert.Equal(16, _designer.Swatches.Count);
        Assert.Equal(16, _designer.Swatches.Select(s => s.Color).Distinct().Count());
        Assert.Equal(16, _designer.Swatches.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void The_glyph_picker_offers_a_plain_folder_and_every_glyph()
    {
        Assert.Null(_designer.Glyphs[0].GlyphId);
        Assert.Equal(GlyphCatalog.All.Select(g => g.Id), _designer.Glyphs.Skip(1).Select(t => t.GlyphId));
    }

    [Fact]
    public void Picking_a_swatch_sets_the_color_and_the_box()
    {
        var red = _designer.Swatches.Single(s => s.Name == "Red");

        _designer.SelectedSwatch = red;

        Assert.Equal(red.Color, _designer.Spec.Fill.Start);
        Assert.Equal("#DC2626", _designer.HexText);
        Assert.Equal("#DC2626", _designer.ActiveHex);
    }

    [Theory]
    [InlineData("#123ABC")]
    [InlineData("123abc")]
    [InlineData("  #123abc  ")]
    public void A_typed_color_is_used_and_left_as_typed(string typed)
    {
        _designer.HexText = typed;

        Assert.Equal(IconColor.Parse("123ABC"), _designer.Spec.Fill.Start);
        Assert.Equal(typed, _designer.HexText);
        Assert.Null(_designer.HexMessage);
        Assert.True(_designer.CanApply);
        Assert.Null(_designer.SelectedSwatch);
    }

    [Fact]
    public void Typing_a_palette_color_selects_its_swatch()
    {
        _designer.HexText = "#dc2626";

        Assert.Equal("Red", _designer.SelectedSwatch?.Name);
    }

    [Theory]
    [InlineData("#12", "Keep typing")]
    [InlineData("", "Keep typing")]
    [InlineData("#12345G", "isn't a color")]
    [InlineData("#1234567", "isn't a color")]
    [InlineData("not a color", "isn't a color")]
    public void Unfinished_or_wrong_text_blocks_apply_and_keeps_the_last_color(string typed, string expected)
    {
        var before = _designer.Spec;

        _designer.HexText = typed;

        Assert.Contains(expected, _designer.HexMessage);
        Assert.False(_designer.CanApply);
        Assert.Equal(before, _designer.Spec);
    }

    [Fact]
    public void Picking_a_swatch_clears_a_bad_color_box()
    {
        _designer.HexText = "nope";

        _designer.SelectedSwatch = _designer.Swatches[3];

        Assert.Null(_designer.HexMessage);
        Assert.True(_designer.CanApply);
        Assert.Equal(_designer.Swatches[3].Hex, _designer.HexText);
    }

    [Fact]
    public void Gradient_uses_two_colors_and_the_chosen_direction()
    {
        _designer.IsGradient = true;
        _designer.Angle = 135;

        var fill = _designer.Spec.Fill;

        Assert.Equal(_designer.Swatches[0].Color, fill.Start);
        Assert.Equal(_designer.Swatches[1].Color, fill.End);
        Assert.Equal(135f, fill.AngleDegrees);
        Assert.Equal("135°", _designer.AngleText);
    }

    [Fact]
    public void The_default_second_color_is_never_the_same_as_the_first()
    {
        _designer.SelectedSwatch = _designer.Swatches[^1];

        _designer.IsGradient = true;

        Assert.NotEqual(_designer.Spec.Fill.Start, _designer.Spec.Fill.End);
    }

    [Fact]
    public void The_palette_edits_whichever_gradient_color_is_chosen()
    {
        var red = _designer.Swatches.Single(s => s.Name == "Red");
        var start = _designer.Spec.Fill.Start;

        _designer.IsGradient = true;
        _designer.EditingEnd = true;
        _designer.SelectedSwatch = red;

        Assert.Equal(start, _designer.Spec.Fill.Start);
        Assert.Equal(red.Color, _designer.Spec.Fill.End);
        Assert.False(_designer.EditingStart);

        _designer.EditingStart = true;

        Assert.Equal(_designer.Swatches[0], _designer.SelectedSwatch);
        Assert.Equal(_designer.Swatches[0].Hex, _designer.HexText);
    }

    [Fact]
    public void A_chosen_second_color_survives_turning_the_gradient_off_and_on()
    {
        var red = _designer.Swatches.Single(s => s.Name == "Red");

        _designer.IsGradient = true;
        _designer.EditingEnd = true;
        _designer.SelectedSwatch = red;
        _designer.IsGradient = false;

        Assert.Null(_designer.Spec.Fill.End);
        Assert.True(_designer.EditingStart);

        _designer.IsGradient = true;

        Assert.Equal(red.Color, _designer.Spec.Fill.End);
    }

    [Fact]
    public void A_glyph_replaces_a_picture_and_a_picture_replaces_a_glyph()
    {
        var picture = WritePicture();

        _designer.SelectedGlyph = _designer.Glyphs[1];
        Assert.Equal(_designer.Glyphs[1].GlyphId, _designer.Spec.GlyphId);

        PickPicture(picture);
        Assert.Null(_designer.Spec.GlyphId);
        Assert.Equal(picture, _designer.Spec.CustomImagePath);
        Assert.Null(_designer.SelectedGlyph);
        Assert.Equal("picture.png", _designer.ImageName);
        _designer.Spec.Validate();

        _designer.SelectedGlyph = _designer.Glyphs[2];
        Assert.Null(_designer.Spec.CustomImagePath);
        Assert.False(_designer.HasImage);
    }

    [Fact]
    public void The_list_clearing_its_selection_is_not_a_choice()
    {
        PickPicture(WritePicture());

        _designer.SelectedGlyph = null;

        Assert.True(_designer.HasImage);
    }

    [Fact]
    public void Removing_a_picture_returns_to_a_plain_folder()
    {
        PickPicture(WritePicture());
        Assert.True(_designer.ClearImageCommand.CanExecute(null));

        _designer.ClearImageCommand.Execute(null);

        Assert.False(_designer.HasImage);
        Assert.Equal(_designer.Glyphs[0], _designer.SelectedGlyph);
        Assert.False(_designer.ClearImageCommand.CanExecute(null));
    }

    [Fact]
    public void Canceling_the_picture_dialog_changes_nothing()
    {
        var before = _designer.Spec;

        _designer.PickImageCommand.Execute(null);

        Assert.Equal(before, _designer.Spec);
    }

    [Fact]
    public async Task The_preview_is_exactly_what_the_renderer_draws()
    {
        _designer.IsGradient = true;
        _designer.SelectedGlyph = _designer.Glyphs[3];
        await _designer.PreviewTask;

        var spec = _designer.Spec;

        Assert.Equal(_renderer.RenderPreviewPng(spec, 256), _designer.LargePreview);
        Assert.Equal(_renderer.RenderPreviewPng(spec, 32), _designer.MediumPreview);
        Assert.Equal(_renderer.RenderPreviewPng(spec, 16), _designer.SmallPreview);
    }

    [Fact]
    public async Task The_newest_design_wins_however_many_came_before()
    {
        foreach (var swatch in _designer.Swatches)
        {
            _designer.SelectedSwatch = swatch;
        }

        await _designer.PreviewTask;

        Assert.Equal(_renderer.RenderPreviewPng(_designer.Spec, 256), _designer.LargePreview);
    }

    [Fact]
    public async Task A_superseded_debounced_render_never_runs()
    {
        var slow = new DesignerViewModel(_renderer, _dialogs, new FileLog(_box.Paths.LogFile), TimeSpan.FromMilliseconds(40));

        slow.SelectedSwatch = slow.Swatches[1];
        var superseded = slow.PreviewTask;
        slow.SelectedSwatch = slow.Swatches[2];

        await Task.WhenAll(superseded, slow.PreviewTask);

        Assert.Equal(_renderer.RenderPreviewPng(slow.Spec, 256), slow.LargePreview);
    }

    [Fact]
    public async Task The_glyph_picker_is_drawn_in_the_chosen_colors()
    {
        await _designer.InitializeAsync();
        var before = _designer.Glyphs[1].Image;
        Assert.NotNull(before);

        _designer.SelectedSwatch = _designer.Swatches[7];
        await _designer.PreviewTask;

        Assert.NotEqual(before, _designer.Glyphs[1].Image);
        Assert.All(_designer.Glyphs, tile => Assert.NotNull(tile.Image));
    }

    [Fact]
    public async Task A_bad_picture_explains_itself_and_blocks_apply()
    {
        var path = Path.Combine(_box.Root, "not-a-picture.png");
        File.WriteAllText(path, "this is not an image");

        PickPicture(path);
        await _designer.PreviewTask;

        Assert.Equal("This file could not be read as an image.", _designer.PreviewError);
        Assert.False(_designer.CanApply);

        _designer.ClearImageCommand.Execute(null);
        await _designer.PreviewTask;

        Assert.Null(_designer.PreviewError);
        Assert.True(_designer.CanApply);
    }

    [Fact]
    public async Task A_picture_that_disappeared_explains_itself()
    {
        PickPicture(Path.Combine(_box.Root, "gone.png"));
        await _designer.PreviewTask;

        Assert.Equal("The image file could not be found.", _designer.PreviewError);
    }

    [Fact]
    public void Changes_are_announced_so_the_apply_button_can_follow()
    {
        var raised = 0;
        _designer.DesignChanged += (_, _) => raised++;

        _designer.SelectedSwatch = _designer.Swatches[2];

        Assert.True(raised > 0);
    }
}
