using System.Drawing;
using Foldcraft.Core.Apps;
using Foldcraft.Core.Icons;

namespace Foldcraft.Tests;

/// <summary>Reading icons from files. Every picture here is drawn by the test.</summary>
public sealed class AppIconReaderTests : IDisposable
{
    private readonly Sandbox _box = new();
    private readonly ShellIconReader _reader = new();

    public void Dispose() => _box.Dispose();

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_box.Root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private AppArt? Read(params string[] paths) => _reader.Read(paths.Select(path => new IconCandidate(path)).ToArray());

    [Fact]
    public void The_largest_real_frame_of_an_icon_file_is_used()
    {
        var art = Read(Write("a.ico", DrawnArt.Ico(Color.Teal, 16, 32, 48, 256, 64)));

        Assert.Equal(256, art?.Width);
    }

    [Fact]
    public void A_frame_stored_the_older_way_keeps_its_transparency()
    {
        using var frame = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(frame))
        using (var brush = new SolidBrush(Color.Teal))
        {
            graphics.FillEllipse(brush, 16, 16, 32, 32);
        }

        var art = Read(Write("round.ico", IcoFile.Write([frame])));

        // Cropped to the circle, so its corners are empty and its middle is solid.
        Assert.NotNull(art);
        Assert.InRange(art.Width, 30, 34);
        Assert.Equal(0, DrawnArt.PixelAt(art.Png, 0, 0).A);
        Assert.Equal(255, DrawnArt.PixelAt(art.Png, art.Width / 2, art.Height / 2).A);
    }

    [Fact]
    public void A_frame_stored_as_png_keeps_its_transparency()
    {
        using var frame = DrawnArt.Bitmap(256, Color.Teal, margin: 40);

        var art = Read(Write("png.ico", IcoFile.Write([frame])));

        Assert.Equal(176, art?.Width);
    }

    [Fact]
    public void A_png_or_jpeg_or_bitmap_is_read_directly()
    {
        using var image = DrawnArt.Bitmap(80, Color.Teal);
        var jpeg = Path.Combine(_box.Root, "a.jpg");
        var bitmap = Path.Combine(_box.Root, "a.bmp");

        image.Save(jpeg, System.Drawing.Imaging.ImageFormat.Jpeg);
        image.Save(bitmap, System.Drawing.Imaging.ImageFormat.Bmp);

        Assert.Equal(80, Read(Write("a.png", DrawnArt.Png(80, Color.Teal)))?.Width);
        Assert.Equal(80, Read(jpeg)?.Width);
        Assert.Equal(80, Read(bitmap)?.Width);
    }

    [Fact]
    public void A_programs_largest_icon_resource_is_read_without_running_it()
    {
        // Foldcraft's own program, which holds Foldcraft's own icon.
        var program = Path.Combine(AppContext.BaseDirectory, "Foldcraft.exe");
        Assert.True(File.Exists(program));

        var art = Read(program);

        Assert.NotNull(art);
        Assert.True(Math.Max(art.Width, art.Height) >= 128, $"Only {art.Width} x {art.Height} was read.");
    }

    [Fact]
    public void An_icon_index_past_the_end_falls_back_to_the_first_icon()
    {
        var program = Path.Combine(AppContext.BaseDirectory, "Foldcraft.exe");

        Assert.NotNull(_reader.Read([new IconCandidate(program, 999)]));
        Assert.NotNull(_reader.Read([new IconCandidate(program, -999)]));
    }

    [Fact]
    public void A_file_with_no_icon_of_its_own_falls_back_to_the_shell()
    {
        // Whatever Windows shows for a text file on this PC, it comes back cropped and not empty.
        var art = Read(Write("note.txt", [65]));

        Assert.NotNull(art);
        Assert.True(art.Width <= AppArtMaker.MaxSize);
    }

    [Fact]
    public void A_program_with_no_icon_gives_nothing()
    {
        Assert.Null(Read(Write("empty.exe", [])));
    }

    [Fact]
    public void A_damaged_file_is_passed_over_for_the_next_candidate()
    {
        var art = Read(
            Write("broken.ico", [1, 2, 3]),
            Write("broken.png", [9, 9, 9, 9]),
            Write("fine.png", DrawnArt.Png(128, Color.Teal)));

        Assert.Equal("fine.png", Path.GetFileName(art?.SourcePath));
    }

    [Fact]
    public void A_missing_file_is_passed_over()
    {
        Assert.Null(Read(Path.Combine(_box.Root, "absent.ico")));
    }

    [Theory]
    [InlineData(@"\\server\share\app.ico")]
    [InlineData("//server/share/app.ico")]
    [InlineData(@"\\?\UNC\server\share\app.ico")]
    [InlineData(@"\\.\pipe\something")]
    [InlineData("relative\\app.ico")]
    [InlineData("C:\\file.ico:stream")]
    [InlineData("")]
    public void Anything_but_a_plain_local_path_is_never_opened(string path)
    {
        Assert.Null(LocalSource.Normalize(path));
        Assert.Null(_reader.Read([new IconCandidate(path)]));
    }

    [Fact]
    public void The_search_stops_at_the_first_picture_big_enough()
    {
        var art = Read(
            Write("first.png", DrawnArt.Png(128, Color.Teal)),
            Write("second.png", DrawnArt.Png(256, Color.Teal)));

        Assert.Equal("first.png", Path.GetFileName(art?.SourcePath));
    }

    [Fact]
    public void When_all_are_small_the_largest_wins()
    {
        var art = Read(
            Write("small.png", DrawnArt.Png(32, Color.Teal)),
            Write("bigger.png", DrawnArt.Png(48, Color.Teal)),
            Write("smallest.png", DrawnArt.Png(16, Color.Teal)));

        Assert.Equal(48, art?.Width);
    }

    [Fact]
    public void A_picture_with_nothing_visible_counts_as_no_picture()
    {
        using var empty = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        Assert.Null(AppArtMaker.From(empty, "empty"));
    }

    [Fact]
    public void Empty_margins_are_cropped_so_logos_sit_the_same_size()
    {
        using var padded = DrawnArt.Bitmap(200, Color.Teal, margin: 50);

        var art = AppArtMaker.From(padded, "padded");

        Assert.Equal(100, art?.Width);
        Assert.Equal(100, art?.Height);
    }

    [Fact]
    public void A_huge_picture_is_brought_down_to_the_largest_icon_size()
    {
        using var huge = DrawnArt.Bitmap(900, Color.Teal);

        Assert.Equal(AppArtMaker.MaxSize, AppArtMaker.From(huge, "huge")?.Width);
    }

    [Fact]
    public void A_small_picture_is_never_enlarged()
    {
        using var small = DrawnArt.Bitmap(32, Color.Teal);

        Assert.Equal(32, AppArtMaker.From(small, "small")?.Width);
    }

    [Theory]
    [InlineData(32, ArtQuality.Low)]
    [InlineData(63, ArtQuality.Low)]
    [InlineData(64, ArtQuality.Good)]
    [InlineData(256, ArtQuality.Good)]
    public void Small_sources_are_marked_low_quality(int size, ArtQuality expected)
    {
        using var image = DrawnArt.Bitmap(size, Color.Teal);

        Assert.Equal(expected, AppArtMaker.From(image, "image")?.Quality);
    }

    [Fact]
    public void Saved_art_stays_in_the_data_folder_and_a_repeat_is_one_file()
    {
        var store = new AppArtStore(_box.Paths);
        var png = DrawnArt.Png(64, Color.Teal);

        var first = store.Save(png);
        var second = store.Save(png);

        Assert.Equal(first, second);
        Assert.StartsWith(_box.Paths.AppArtDirectory, first);
        Assert.Single(Directory.GetFiles(_box.Paths.AppArtDirectory));
    }
}

public sealed class ColorExtractionTests
{
    private static LogoSample Solid(Color color, int count = 400) =>
        new(Enumerable.Repeat(new IconColor(color.R, color.G, color.B), count).ToArray());

    private static LogoSample Mixed(params (Color Color, int Count)[] parts) => new(parts
        .SelectMany(part => Enumerable.Repeat(new IconColor(part.Color.R, part.Color.G, part.Color.B), part.Count))
        .ToArray());

    private static double HueOf(IconColor color) => ColorMath.ToHsl(color).Hue;

    [Fact]
    public void A_red_logo_gives_a_red_folder()
    {
        var palette = IconPalette.Extract(Solid(Color.FromArgb(220, 30, 40)));

        Assert.False(palette.IsNeutral);
        Assert.True(ColorMath.HueDistance(HueOf(palette.Dominant), 0) < 10);
    }

    [Fact]
    public void The_biggest_color_is_dominant_and_the_next_is_its_companion()
    {
        var palette = IconPalette.Extract(Mixed((Color.FromArgb(30, 90, 220), 300), (Color.FromArgb(230, 120, 20), 100)));

        Assert.True(ColorMath.HueDistance(HueOf(palette.Dominant), 220) < 15);
        Assert.True(ColorMath.HueDistance(HueOf(palette.Companion), 30) < 15);
    }

    [Fact]
    public void Near_white_near_black_and_gray_pixels_do_not_count()
    {
        var palette = IconPalette.Extract(Mixed(
            (Color.White, 500),
            (Color.FromArgb(240, 240, 245), 500),
            (Color.Black, 500),
            (Color.FromArgb(10, 12, 14), 500),
            (Color.FromArgb(128, 128, 130), 500),
            (Color.FromArgb(40, 180, 90), 300)));

        Assert.False(palette.IsNeutral);
        Assert.True(ColorMath.HueDistance(HueOf(palette.Dominant), 142) < 15);
    }

    [Fact]
    public void A_white_with_a_slight_tint_is_still_white()
    {
        // Saturation alone can't catch these: colors this pale or this dark measure as very saturated.
        var pale = Color.FromArgb(255, 238, 232);

        Assert.True(ColorMath.ToHsl(new IconColor(pale.R, pale.G, pale.B)).Saturation > 0.5);
        Assert.True(IconPalette.Extract(Solid(pale)).IsNeutral);
    }

    [Fact]
    public void A_black_with_a_slight_tint_is_still_black()
    {
        var dark = Color.FromArgb(34, 14, 12);

        Assert.True(ColorMath.ToHsl(new IconColor(dark.R, dark.G, dark.B)).Saturation > 0.4);
        Assert.True(IconPalette.Extract(Solid(dark)).IsNeutral);
    }

    [Fact]
    public void A_gray_logo_gets_a_sensible_neutral()
    {
        var palette = IconPalette.Extract(Mixed((Color.White, 200), (Color.FromArgb(150, 150, 150), 200)));

        Assert.True(palette.IsNeutral);
        Assert.True(ColorMath.ToHsl(palette.Dominant).Saturation < 0.2);
    }

    [Fact]
    public void A_black_logo_gets_a_pale_neutral_and_a_white_one_gets_a_dark_neutral()
    {
        var onBlack = IconPalette.Extract(Solid(Color.FromArgb(12, 12, 12)));
        var onWhite = IconPalette.Extract(Solid(Color.White));

        Assert.True(ColorMath.Luminance(onBlack.Dominant) > 0.5);
        Assert.True(ColorMath.Luminance(onWhite.Dominant) < 0.25);
    }

    [Fact]
    public void A_stray_colored_pixel_does_not_make_a_gray_logo_colorful()
    {
        var palette = IconPalette.Extract(Mixed((Color.FromArgb(120, 120, 120), 998), (Color.Red, 2)));

        Assert.True(palette.IsNeutral);
    }

    [Fact]
    public void No_pixels_at_all_gives_a_neutral()
    {
        Assert.True(IconPalette.Extract(new LogoSample([])).IsNeutral);
    }

    [Fact]
    public void Transparent_pixels_are_left_out_of_the_sample()
    {
        using var image = DrawnArt.Bitmap(96, Color.FromArgb(200, 30, 40), margin: 24);

        var sample = LogoSample.From(DrawnArt.Encode(image));

        Assert.All(sample.Pixels, pixel => Assert.Equal(new IconColor(200, 30, 40), pixel));
        Assert.InRange(sample.Pixels.Count, 500, 600);
    }

    [Fact]
    public void Shades_of_one_color_count_as_one_color()
    {
        var palette = IconPalette.Extract(Mixed(
            (Color.FromArgb(200, 30, 40), 100),
            (Color.FromArgb(220, 40, 50), 100),
            (Color.FromArgb(180, 25, 35), 100),
            (Color.FromArgb(240, 60, 70), 100),
            (Color.FromArgb(30, 200, 90), 150)));

        Assert.True(ColorMath.HueDistance(HueOf(palette.Dominant), 0) < 12);
    }

    [Fact]
    public void The_same_logo_always_gives_the_same_colors()
    {
        var sample = Mixed((Color.FromArgb(30, 90, 220), 100), (Color.FromArgb(230, 120, 20), 100), (Color.FromArgb(40, 180, 90), 100));

        Assert.Equal(IconPalette.Extract(sample), IconPalette.Extract(sample));
    }

    [Fact]
    public void The_fill_is_a_two_color_gradient_that_runs_darker()
    {
        var fill = IconPalette.Extract(Solid(Color.FromArgb(30, 90, 220))).ToFill();

        Assert.NotNull(fill.End);
        Assert.True(ColorMath.Luminance(fill.Start) > ColorMath.Luminance(fill.End.Value));
    }

    [Theory]
    [InlineData(255, 0, 255)]
    [InlineData(0, 255, 0)]
    [InlineData(255, 255, 0)]
    [InlineData(60, 0, 0)]
    [InlineData(0, 20, 90)]
    public void Neon_or_nearly_black_brand_colors_are_brought_into_a_tasteful_range(int r, int g, int b)
    {
        var fill = IconPalette.Extract(Solid(Color.FromArgb(r, g, b))).ToFill();
        var (_, saturation, lightness) = ColorMath.ToHsl(fill.Start);

        Assert.InRange(saturation, 0.3, 0.9);
        Assert.InRange(lightness, 0.3, 0.7);
    }

    [Fact]
    public void A_clashing_companion_only_tints_the_shade()
    {
        var clash = new IconPalette(IconColor.Parse("#2060E0"), IconColor.Parse("#E06020")).ToFill();
        var alone = new IconPalette(IconColor.Parse("#2060E0"), IconColor.Parse("#2060E0")).ToFill();

        var drift = ColorMath.HueDistance(HueOf(clash.End!.Value), HueOf(alone.End!.Value));

        Assert.True(drift < 45, $"The gradient drifted {drift:0} degrees.");
    }

    [Fact]
    public void A_neutral_palette_stays_neutral_on_the_folder()
    {
        var palette = IconPalette.Extract(Solid(Color.White));

        Assert.Equal(palette.Dominant, palette.ToFill().Start);
        Assert.Equal(palette.Companion, palette.ToFill().End);
    }

    [Theory]
    [InlineData("#000000", 0.0)]
    [InlineData("#FFFFFF", 0.0)]
    [InlineData("#FF0000", 0.0)]
    [InlineData("#00FF00", 120.0)]
    [InlineData("#0000FF", 240.0)]
    [InlineData("#3C78B4", 210.0)]
    public void Colors_survive_the_trip_through_hue_and_back(string hex, double hue)
    {
        var color = IconColor.Parse(hex);
        var (h, s, l) = ColorMath.ToHsl(color);

        Assert.Equal(hue, h, 1);
        Assert.Equal(color, ColorMath.FromHsl(h, s, l));
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF", 21.0)]
    [InlineData("#FFFFFF", "#FFFFFF", 1.0)]
    [InlineData("#777777", "#FFFFFF", 4.48)]
    public void Contrast_is_the_standard_ratio(string a, string b, double expected)
    {
        Assert.Equal(expected, ColorMath.Contrast(IconColor.Parse(a), IconColor.Parse(b)), 1);
    }

    [Theory]
    [InlineData(10.0, 350.0, 20.0)]
    [InlineData(0.0, 180.0, 180.0)]
    [InlineData(90.0, 100.0, 10.0)]
    public void Hue_distance_goes_the_short_way_round(double a, double b, double expected)
    {
        Assert.Equal(expected, ColorMath.HueDistance(a, b));
    }
}

/// <summary>Putting a logo on a folder, and keeping it visible there.</summary>
public sealed class LogoCompositionTests
{
    private static readonly IconRenderer Renderer = new();

    private static PreparedApp Prepared(byte[] png, DetectedApp? app = null)
    {
        var (width, height) = DrawnArt.SizeOf(png);
        var sample = LogoSample.From(png);

        return new PreparedApp(app ?? SampleApps.Program(), new AppArt(png, width, height, "test"), sample, IconPalette.Extract(sample));
    }

    private static PreparedApp NoArt(DetectedApp app) => new(app, null, new LogoSample([]), IconPalette.Extract(new LogoSample([])));

    private static double Visible(PreparedApp prepared, AppLook look) =>
        prepared.Sample.VisibleShare(IconPalette.MiddleOf(look.Spec.Fill));

    private static byte[] Render(PreparedApp prepared, AppLook look, int size) =>
        Renderer.RenderPreviewPng(look.Spec, size, prepared.Art!.Png);

    [Fact]
    public void A_logo_in_two_colors_stands_out_with_nothing_added()
    {
        // Mostly white, so most of it stands out against a folder in its blue.
        var prepared = Prepared(DrawnArt.TwoColorPng(128, Color.FromArgb(30, 90, 220), Color.White, 0.3f));

        var look = AppLookComposer.Compose(prepared);

        Assert.Equal(ContrastFix.None, look.Fix);
        Assert.Equal(LogoPlate.None, look.Spec.Logo?.Plate);
    }

    [Fact]
    public void A_one_color_logo_would_vanish_on_its_own_color_so_the_folder_shifts()
    {
        var prepared = Prepared(DrawnArt.Png(128, Color.FromArgb(40, 90, 200)));

        var look = AppLookComposer.Compose(prepared);

        Assert.NotEqual(ContrastFix.None, look.Fix);
        Assert.True(
            Visible(prepared, look) >= AppLookComposer.EnoughVisible || look.Spec.Logo?.Plate != LogoPlate.None,
            "The logo would still disappear.");
    }

    [Fact]
    public void Shifting_keeps_the_brands_hue()
    {
        var prepared = Prepared(DrawnArt.Png(128, Color.FromArgb(40, 90, 200)));
        var automatic = prepared.Palette.ToFill();

        var look = AppLookComposer.Compose(prepared);

        Assert.Equal(ContrastFix.ShiftedColor, look.Fix);
        Assert.True(ColorMath.HueDistance(ColorMath.ToHsl(look.Spec.Fill.Start).Hue, ColorMath.ToHsl(automatic.Start).Hue) < 5);
        Assert.True(Visible(prepared, look) >= AppLookComposer.EnoughVisible);
    }

    [Fact]
    public void When_shifting_cannot_save_it_the_logo_gets_a_plate()
    {
        // One tone above the folder and one below. A lighter or darker folder only trades one for the other.
        var prepared = Prepared(DrawnArt.TwoColorPng(128, Color.FromArgb(0xC9, 0xAF, 0x62), Color.FromArgb(0x5D, 0x4C, 0x1D), 0.5f));

        var look = AppLookComposer.Compose(prepared with { Palette = new IconPalette(IconColor.Parse("#9A7A30"), IconColor.Parse("#9A7A30")) });

        Assert.Equal(ContrastFix.AddedPlate, look.Fix);
        Assert.NotEqual(LogoPlate.None, look.Spec.Logo?.Plate);
    }

    [Fact]
    public void A_pale_logo_gets_a_dark_plate_and_a_dark_logo_a_pale_one()
    {
        var chosen = new AppLookChoices { Start = IconColor.Parse("#808890") };
        var pale = Prepared(DrawnArt.Png(128, Color.FromArgb(135, 145, 155)));
        var paleLook = AppLookComposer.Compose(pale, chosen);

        Assert.Equal(LogoPlate.Dark, paleLook.Spec.Logo?.Plate);

        var dark = Prepared(DrawnArt.TwoColorPng(128, Color.FromArgb(70, 75, 80), Color.FromArgb(120, 128, 134), 0.5f));
        var darkLook = AppLookComposer.Compose(dark, chosen);

        Assert.Equal(LogoPlate.Light, darkLook.Spec.Logo?.Plate);
    }

    [Fact]
    public void A_color_the_user_chose_is_never_shifted()
    {
        var prepared = Prepared(DrawnArt.Png(128, Color.FromArgb(40, 90, 200)));

        var look = AppLookComposer.Compose(prepared, new AppLookChoices { Start = IconColor.Parse("#285AC8") });

        Assert.Equal(IconColor.Parse("#285AC8"), look.Spec.Fill.Start);
        Assert.Equal(ContrastFix.AddedPlate, look.Fix);
    }

    [Fact]
    public void A_plate_the_user_chose_is_kept_even_when_it_is_none()
    {
        var prepared = Prepared(DrawnArt.Png(128, Color.FromArgb(40, 90, 200)));

        var look = AppLookComposer.Compose(prepared, new AppLookChoices { Start = IconColor.Parse("#285AC8"), Plate = LogoPlate.None });

        Assert.Equal(LogoPlate.None, look.Spec.Logo?.Plate);
        Assert.Equal(ContrastFix.None, look.Fix);
    }

    [Fact]
    public void An_outline_folder_gets_no_plate()
    {
        var prepared = Prepared(DrawnArt.Png(128, Color.FromArgb(40, 90, 200)));

        var look = AppLookComposer.Compose(prepared, new AppLookChoices { Shape = FolderShape.Outline });

        Assert.Equal(LogoPlate.None, look.Spec.Logo?.Plate);
        Assert.Equal(FolderShape.Outline, look.Spec.Shape);
    }

    [Fact]
    public void With_no_picture_a_game_gets_a_game_symbol_and_an_app_gets_a_plain_one()
    {
        Assert.Equal("game", AppLookComposer.Compose(NoArt(SampleApps.SteamGame())).Spec.GlyphId);
        Assert.Equal("sparkle", AppLookComposer.Compose(NoArt(SampleApps.Program())).Spec.GlyphId);
    }

    [Fact]
    public void Every_shape_and_position_can_be_drawn()
    {
        var prepared = Prepared(DrawnArt.Png(128, Color.Teal));

        foreach (var shape in Enum.GetValues<FolderShape>())
        {
            foreach (var anchor in Enum.GetValues<LogoAnchor>())
            {
                foreach (var plate in Enum.GetValues<LogoPlate>())
                {
                    var look = AppLookComposer.Compose(prepared, new AppLookChoices { Shape = shape, Anchor = anchor, Plate = plate });

                    Assert.NotEmpty(Render(prepared, look, 96));
                }
            }
        }
    }

    [Fact]
    public void A_scale_outside_the_range_is_brought_back_into_it()
    {
        var prepared = Prepared(DrawnArt.Png(128, Color.Teal));

        Assert.Equal(LogoPlacement.MaxScale, AppLookComposer.Compose(prepared, new AppLookChoices { Scale = 9f }).Spec.Logo?.Scale);
        Assert.Equal(LogoPlacement.MinScale, AppLookComposer.Compose(prepared, new AppLookChoices { Scale = 0f }).Spec.Logo?.Scale);
    }

    [Fact]
    public void The_logo_sits_in_the_middle_of_the_folder_front()
    {
        var prepared = Prepared(DrawnArt.Png(128, Color.FromArgb(240, 40, 40)));
        var look = AppLookComposer.Compose(prepared, new AppLookChoices { Start = IconColor.Parse("#203040"), Plate = LogoPlate.None });

        var png = Render(prepared, look, 256);

        // The front panel runs from about 66 to 210 down. Its middle is about 138.
        Assert.Equal(Color.FromArgb(255, 240, 40, 40), DrawnArt.PixelAt(png, 128, 138));

        var red = Enumerable.Range(0, 256).Where(y => DrawnArt.PixelAt(png, 128, y) == Color.FromArgb(255, 240, 40, 40)).ToArray();
        var horizontal = Enumerable.Range(0, 256).Where(x => DrawnArt.PixelAt(png, x, 138) == Color.FromArgb(255, 240, 40, 40)).ToArray();

        Assert.InRange((red.Min() + red.Max()) / 2, 134, 142);
        Assert.InRange((horizontal.Min() + horizontal.Max()) / 2, 125, 131);
    }

    [Fact]
    public void A_low_resolution_logo_is_not_stretched_into_mush()
    {
        var png = DrawnArt.Png(32, Color.FromArgb(240, 40, 40));
        var prepared = Prepared(png);
        var look = AppLookComposer.Compose(prepared, new AppLookChoices { Start = IconColor.Parse("#203040"), Plate = LogoPlate.None });

        var rendered = Render(prepared, look, 256);

        var wide = Enumerable.Range(0, 256).Count(x => DrawnArt.PixelAt(rendered, x, 149).R > 200);

        Assert.InRange(wide, 50, 70);
    }

    [Fact]
    public void A_sharp_logo_fills_the_room_it_has()
    {
        var png = DrawnArt.Png(256, Color.FromArgb(240, 40, 40));
        var prepared = Prepared(png);
        var look = AppLookComposer.Compose(prepared, new AppLookChoices { Start = IconColor.Parse("#203040"), Plate = LogoPlate.None });

        var rendered = Render(prepared, look, 256);

        var wide = Enumerable.Range(0, 256).Count(x => DrawnArt.PixelAt(rendered, x, 149).R > 200);

        Assert.InRange(wide, 110, 135);
    }

    [Fact]
    public void A_bigger_scale_draws_a_bigger_logo()
    {
        var prepared = Prepared(DrawnArt.Png(256, Color.FromArgb(240, 40, 40)));
        var color = new AppLookChoices { Start = IconColor.Parse("#203040"), Plate = LogoPlate.None };

        var small = Render(prepared, AppLookComposer.Compose(prepared, color with { Scale = 0.6f }), 256);
        var large = Render(prepared, AppLookComposer.Compose(prepared, color with { Scale = 1.2f }), 256);

        Assert.True(
            DrawnArt.PixelsLike(large, Color.FromArgb(240, 40, 40)) > DrawnArt.PixelsLike(small, Color.FromArgb(240, 40, 40)) * 2);
    }

    [Theory]
    [InlineData(LogoAnchor.TopLeft, -1, -1)]
    [InlineData(LogoAnchor.TopRight, 1, -1)]
    [InlineData(LogoAnchor.BottomLeft, -1, 1)]
    [InlineData(LogoAnchor.BottomRight, 1, 1)]
    public void Positions_move_the_logo_toward_their_corner(LogoAnchor anchor, int horizontal, int vertical)
    {
        var prepared = Prepared(DrawnArt.Png(256, Color.FromArgb(240, 40, 40)));
        var look = AppLookComposer.Compose(prepared, new AppLookChoices
        {
            Start = IconColor.Parse("#203040"),
            Plate = LogoPlate.None,
            Anchor = anchor,
            Scale = 0.6f,
        });

        var png = Render(prepared, look, 256);
        var red = Color.FromArgb(255, 240, 40, 40);
        var points = from y in Enumerable.Range(0, 256) from x in Enumerable.Range(0, 256) where DrawnArt.PixelAt(png, x, y) == red select (x, y);
        var found = points.ToArray();

        Assert.Equal(horizontal, Math.Sign(found.Average(p => p.x) - 128));
        Assert.Equal(vertical, Math.Sign(found.Average(p => p.y) - 149));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void At_small_sizes_the_logo_takes_over_so_it_stays_recognizable(int size)
    {
        var red = Color.FromArgb(240, 40, 40);
        var prepared = Prepared(DrawnArt.Png(256, red));
        var look = AppLookComposer.Compose(prepared, new AppLookChoices { Start = IconColor.Parse("#203040"), Plate = LogoPlate.None });

        var png = Render(prepared, look, size);

        // A quarter of the icon. Sitting on the front panel as at larger sizes, it would get about a sixth.
        Assert.True(DrawnArt.PixelsLike(png, red) >= size * size / 4, $"Only {DrawnArt.PixelsLike(png, red)} logo pixels at {size} px.");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void Two_different_logos_can_be_told_apart_at_small_sizes(int size)
    {
        var color = new AppLookChoices { Start = IconColor.Parse("#203040"), Plate = LogoPlate.None };
        var one = Prepared(DrawnArt.TwoColorPng(128, Color.Red, Color.Yellow, 0.5f));
        var two = Prepared(DrawnArt.TwoColorPng(128, Color.Cyan, Color.Magenta, 0.5f));

        var difference = DrawnArt.Difference(
            Render(one, AppLookComposer.Compose(one, color), size),
            Render(two, AppLookComposer.Compose(two, color), size));

        Assert.True(difference >= size * 3, $"Only {difference} pixels differ at {size} px.");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(256)]
    public void A_plate_shows_up_at_every_size(int size)
    {
        var prepared = Prepared(DrawnArt.Png(128, Color.FromArgb(240, 40, 40), margin: 40));
        var without = AppLookComposer.Compose(prepared, new AppLookChoices { Start = IconColor.Parse("#203040"), Plate = LogoPlate.None });
        var with = AppLookComposer.Compose(prepared, new AppLookChoices { Start = IconColor.Parse("#203040"), Plate = LogoPlate.Light });

        Assert.True(DrawnArt.Difference(Render(prepared, without, size), Render(prepared, with, size)) > 0);
    }

    [Fact]
    public void A_logo_needs_a_picture()
    {
        var spec = new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#203040")), Logo = new LogoPlacement() };

        Assert.Throws<IconRenderException>(spec.Validate);
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(5f)]
    [InlineData(float.NaN)]
    public void A_logo_size_out_of_range_is_refused(float scale)
    {
        var spec = new FolderIconSpec
        {
            Fill = FolderFill.Solid(IconColor.Parse("#203040")),
            CustomImagePath = "art.png",
            Logo = new LogoPlacement(scale),
        };

        Assert.Throws<IconRenderException>(spec.Validate);
    }

    [Fact]
    public void A_recorded_logo_comes_back_exactly()
    {
        var spec = new FolderIconSpec
        {
            Fill = FolderFill.Gradient(IconColor.Parse("#203040"), IconColor.Parse("#102030"), 115f),
            CustomImagePath = @"C:\Data\AppArt\art.png",
            Logo = new LogoPlacement(0.85f, LogoAnchor.TopRight, LogoPlate.Dark),
        };

        var json = IconSpecCodec.ToRecordedJson(spec, "abc");

        Assert.True(IconSpecCodec.TryReadRecorded(json, out var read, out var hash));
        Assert.Equal(spec.Logo, read?.Logo);
        Assert.Equal(spec.CustomImagePath, read?.CustomImagePath);
        Assert.Equal("abc", hash);
    }

    [Fact]
    public void A_recorded_logo_with_a_bad_value_reads_as_no_record()
    {
        const string Json = """{"fill":{"start":"#203040"},"image":"a.png","logo":{"scale":99,"anchor":"center","plate":"none"}}""";

        Assert.False(IconSpecCodec.TryReadRecorded(Json, out _, out _));
    }

    [Fact]
    public void A_look_with_no_logo_keeps_its_icon_file_name()
    {
        // Existing folders refer to their icons by this name, so it must never move.
        var spec = new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#2F6FED")), GlyphId = "star" };
        var paths = new Foldcraft.Core.FoldcraftPaths(Path.Combine(Path.GetTempPath(), "foldcraft-key-test"));
        var library = new IconLibrary(paths);

        Assert.DoesNotContain("logo", spec.CanonicalKey(null));
        Assert.EndsWith(".ico", library.ExpectedPath(spec));
    }

    [Fact]
    public void Different_logo_settings_are_different_icons()
    {
        var plain = new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#203040")), CustomImagePath = "a.png" };
        var logo = plain with { Logo = new LogoPlacement() };
        var moved = plain with { Logo = new LogoPlacement(Anchor: LogoAnchor.TopLeft) };

        Assert.NotEqual(plain.CanonicalKey("x"), logo.CanonicalKey("x"));
        Assert.NotEqual(logo.CanonicalKey("x"), moved.CanonicalKey("x"));
    }

    [Fact]
    public void A_theme_file_still_refuses_anything_it_does_not_know()
    {
        const string File = """
            { "format": "foldcraft-themes", "version": 1,
              "themes": [ { "name": "Sneaky", "fill": { "start": "#203040" }, "logo": { "scale": 1 } } ] }
            """;

        Assert.Throws<LookFormatException>(() => Foldcraft.Core.Themes.ThemeFile.Read(System.Text.Encoding.UTF8.GetBytes(File)));
    }
}
