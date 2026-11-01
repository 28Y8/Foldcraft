using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Foldcraft.Core.Icons;

namespace Foldcraft.Tests;

/// <summary>Glyphs, badges and folder styles at the sizes people actually see them.</summary>
public sealed class ArtTests
{
    private static readonly IconColor Blue = IconColor.Parse("#2F6FED");

    private static FolderIconSpec Plain(FolderShape shape = FolderShape.Classic) =>
        new() { Fill = FolderFill.Solid(Blue), Shape = shape };

    private static Bitmap Frame(FolderIconSpec spec, int size) =>
        new(new MemoryStream(new IconRenderer().RenderPreviewPng(spec, size)));

    private static Color[] Pixels(Bitmap bitmap)
    {
        var pixels = new Color[bitmap.Width * bitmap.Height];

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                pixels[(y * bitmap.Width) + x] = bitmap.GetPixel(x, y);
            }
        }

        return pixels;
    }

    private static int DifferentPixels(FolderIconSpec a, FolderIconSpec b, int size)
    {
        using var first = Frame(a, size);
        using var second = Frame(b, size);

        return Pixels(first).Zip(Pixels(second)).Count(pair => pair.First != pair.Second);
    }

    public static TheoryData<string> GlyphIds() => new(GlyphCatalog.All.Select(g => g.Id));

    public static TheoryData<string> BadgeIds() => new(BadgeCatalog.All.Select(b => b.Id));

    public static TheoryData<FolderShape> Shapes() => new(Enum.GetValues<FolderShape>());

    [Fact]
    public void Every_category_has_glyphs()
    {
        Assert.Equal(10, GlyphCatalog.Categories.Count);

        foreach (var category in GlyphCatalog.Categories)
        {
            Assert.True(GlyphCatalog.Search(null, category).Count >= 6, $"{category} is thin.");
        }
    }

    [Theory]
    [MemberData(nameof(GlyphIds))]
    public void A_glyph_has_a_name_and_words_to_find_it_by(string id)
    {
        var glyph = GlyphCatalog.Find(id)!;

        Assert.NotEmpty(glyph.Keywords);
        Assert.Contains(glyph, GlyphCatalog.Search(glyph.DisplayName));
        Assert.Contains(glyph, GlyphCatalog.Search(glyph.Keywords[0]));
    }

    /// <summary>
    /// The glyph sits in about eight pixels at 16 px. Filling nearly all of them is a blob, and
    /// the two drawings must also be different where a small one exists at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(GlyphIds))]
    public void A_glyph_at_16_px_is_more_than_a_blob(string id)
    {
        var spec = Plain() with { GlyphId = id };

        var changed = DifferentPixels(Plain(), spec, 16);

        Assert.True(changed >= 6, $"{id} barely shows: {changed} pixels.");
        Assert.True(changed <= 56, $"{id} is a blob: {changed} pixels.");
    }

    [Fact]
    public void Glyphs_with_fine_detail_have_a_simpler_drawing_for_small_sizes()
    {
        var simplified = GlyphCatalog.All.Count(g => g.HasSmallVariant);

        Assert.True(simplified >= 60, $"Only {simplified} have a small drawing.");
        Assert.True(GlyphCatalog.Find("game")!.HasSmallVariant);
        Assert.False(GlyphCatalog.Find("heart")!.HasSmallVariant);
    }

    [Fact]
    public void The_small_drawing_is_used_at_16_and_20_px_and_not_above()
    {
        var glyph = GlyphCatalog.Find("calendar")!;
        var layers = glyph.SmallLayers;

        Assert.NotSame(glyph.Layers, layers);
        Assert.NotEqual(0, DifferentPixels(Plain() with { GlyphId = "calendar" }, Plain(), 20));
    }

    [Fact]
    public void Glyph_ids_stay_lower_case_words_because_they_are_stored()
    {
        Assert.All(GlyphCatalog.All, g => Assert.Matches("^[a-z0-9-]+$", g.Id));
    }

    [Fact]
    public void Search_ignores_case_and_every_word_has_to_fit()
    {
        Assert.Contains(GlyphCatalog.Search("MUSIC"), g => g.Id == "music");
        Assert.Contains(GlyphCatalog.Search("  camping  "), g => g.Id == "tent");
        Assert.Contains(GlyphCatalog.Search("road trip"), g => g.Id == "car");
        Assert.Empty(GlyphCatalog.Search("zzz nothing"));
        Assert.Equal(GlyphCatalog.All.Count, GlyphCatalog.Search(string.Empty).Count);
        Assert.All(GlyphCatalog.Search(null, GlyphCategory.Food), g => Assert.Equal(GlyphCategory.Food, g.Category));
    }

    [Fact]
    public void There_are_twelve_badges_with_the_planned_set()
    {
        string[] expected =
        [
            "done", "important", "in-progress", "favorite", "archived", "shared",
            "private", "waiting", "new", "warning", "idea", "review",
        ];

        Assert.Equal(expected, BadgeCatalog.All.Select(b => b.Id));
    }

    [Theory]
    [MemberData(nameof(BadgeIds))]
    public void A_badge_shows_at_every_size(string id)
    {
        foreach (var size in IconRenderer.Sizes)
        {
            Assert.True(DifferentPixels(Plain(), Plain() with { Badge = new IconBadge(id) }, size) > 0);
        }
    }

    [Theory]
    [MemberData(nameof(BadgeIds))]
    public void A_badges_symbol_stands_out_from_its_plate(string id)
    {
        var badge = BadgeCatalog.Find(id)!;
        var ink = BadgeArtist.ReadableOn(badge.DefaultColor);
        var inkColor = new IconColor(ink.R, ink.G, ink.B);

        Assert.True(BadgeArtist.Contrast(inkColor, badge.DefaultColor) >= 4.5, $"{id} has low contrast.");
    }

    /// <summary>
    /// Legibility here means the symbol still has both ink and plate pixels to show its form.
    /// Thin strokes are antialiased at this size, so a pixel counts as whichever it's closer to.
    /// </summary>
    [Theory]
    [MemberData(nameof(BadgeIds))]
    public void A_badge_at_32_px_keeps_its_symbol_and_its_plate(string id)
    {
        var badge = BadgeCatalog.Find(id)!;
        var plate = Color.FromArgb(badge.DefaultColor.R, badge.DefaultColor.G, badge.DefaultColor.B);
        var ink = BadgeArtist.ReadableOn(badge.DefaultColor);

        static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

        using var frame = Frame(Plain() with { Badge = new IconBadge(id) }, 32);

        // Inside the rim, so white and the folder's own blue aren't counted as either.
        var pixels = Enumerable.Range(19, 10)
            .SelectMany(y => Enumerable.Range(19, 10).Select(x => frame.GetPixel(x, y)))
            .ToArray();

        var plateCount = pixels.Count(p => Distance(p, plate) < Distance(p, ink));
        var inkCount = pixels.Count(p => Distance(p, ink) < Distance(p, plate));

        Assert.True(plateCount >= 15, $"{id}: {plateCount} plate pixels.");
        Assert.True(inkCount >= 8, $"{id}: {inkCount} ink pixels.");
    }

    [Theory]
    [InlineData(16, 3)]
    [InlineData(32, 6)]
    public void Every_badge_looks_different_from_every_other(int size, int minimumDifference)
    {
        var specs = BadgeCatalog.All.Select(b => Plain() with { Badge = new IconBadge(b.Id, Color: Blue) }).ToArray();

        for (var i = 0; i < specs.Length; i++)
        {
            for (var j = i + 1; j < specs.Length; j++)
            {
                var difference = DifferentPixels(specs[i], specs[j], size);

                Assert.True(
                    difference >= minimumDifference,
                    $"{specs[i].Badge!.Kind} and {specs[j].Badge!.Kind} differ by {difference} pixels at {size} px.");
            }
        }
    }

    [Fact]
    public void A_badge_at_16_px_stays_in_its_corner()
    {
        Assert.Equal(10, BadgeArtist.OuterWidth(16));
        Assert.Equal(16, BadgeArtist.OuterWidth(32));

        var change = Plain() with { Badge = new IconBadge("done") };

        using var plain = Frame(Plain(), 16);
        using var badged = Frame(change, 16);

        for (var y = 0; y < 6; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                Assert.Equal(plain.GetPixel(x, y), badged.GetPixel(x, y));
            }
        }
    }

    [Theory]
    [InlineData(BadgeCorner.BottomRight, 24, 24)]
    [InlineData(BadgeCorner.BottomLeft, 7, 24)]
    [InlineData(BadgeCorner.TopRight, 24, 7)]
    [InlineData(BadgeCorner.TopLeft, 7, 7)]
    public void A_badge_goes_in_the_corner_asked_for(BadgeCorner corner, int x, int y)
    {
        using var plain = Frame(Plain(), 32);
        using var badged = Frame(Plain() with { Badge = new IconBadge("done", corner) }, 32);

        Assert.NotEqual(plain.GetPixel(x, y), badged.GetPixel(x, y));
    }

    [Fact]
    public void A_custom_badge_color_is_used()
    {
        var red = IconColor.Parse("#FF0000");

        using var frame = Frame(Plain() with { Badge = new IconBadge("done", Color: red) }, 256);
        var plate = Pixels(frame).Count(p => p.ToArgb() == Color.FromArgb(255, 0, 0).ToArgb());

        Assert.True(plate > 1000, $"Only {plate} pixels are the chosen color.");
    }

    [Fact]
    public void Corner_and_color_are_part_of_an_icons_identity()
    {
        using var box = new Sandbox();
        var plain = Plain() with { Badge = new IconBadge("done") };

        Assert.NotEqual(box.Icons.ExpectedPath(plain), box.Icons.ExpectedPath(Plain()));
        Assert.NotEqual(
            box.Icons.ExpectedPath(plain),
            box.Icons.ExpectedPath(plain with { Badge = new IconBadge("done", BadgeCorner.TopLeft) }));
        Assert.NotEqual(
            box.Icons.ExpectedPath(plain),
            box.Icons.ExpectedPath(plain with { Badge = new IconBadge("done", Color: Blue) }));
    }

    [Fact]
    public void A_look_with_nothing_new_keeps_the_same_identity_as_before_those_options_existed()
    {
        var spec = new FolderIconSpec { Fill = FolderFill.Solid(Blue), GlyphId = "star" };

        Assert.Equal($"renderer={IconRenderer.Version}|fill=solid:#2F6FED|glyph=star|image=", spec.CanonicalKey(null));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Every_style_differs_from_every_other_at_each_common_size(FolderShape shape)
    {
        foreach (var other in Enum.GetValues<FolderShape>().Where(s => s != shape))
        {
            foreach (var size in new[] { 16, 32, 256 })
            {
                Assert.True(
                    DifferentPixels(Plain(shape), Plain(other), size) >= size / 2,
                    $"{shape} and {other} look alike at {size} px.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Every_style_stays_inside_its_frame_with_a_transparent_corner(FolderShape shape)
    {
        foreach (var size in IconRenderer.Sizes)
        {
            using var frame = Frame(Plain(shape), size);

            Assert.Equal(0, frame.GetPixel(size - 1, 0).A);
            Assert.Equal(0, frame.GetPixel(0, size - 1).A);
        }
    }

    [Fact]
    public void An_outline_folder_lets_the_background_show_through()
    {
        using var frame = Frame(Plain(FolderShape.Outline), 64);

        Assert.InRange((int)frame.GetPixel(32, 45).A, 1, 90);
    }

    [Fact]
    public void An_outline_folder_has_crisp_lines_at_16_px()
    {
        using var frame = Frame(Plain(FolderShape.Outline), 16);
        var column = Enumerable.Range(0, 16).Select(x => frame.GetPixel(x, 11).A).ToArray();

        Assert.Equal(255, column[1]);
        Assert.Equal(255, column[14]);
    }

    [Fact]
    public void A_glyph_on_an_outline_folder_is_drawn_in_the_folders_color()
    {
        using var frame = Frame(Plain(FolderShape.Outline) with { GlyphId = "shield" }, 64);
        var ink = frame.GetPixel(32, 42);

        Assert.Equal(Blue.B, ink.B);
    }

    [Fact]
    public void An_unknown_style_is_refused()
    {
        Assert.Throws<IconRenderException>(() => Plain((FolderShape)99).Validate());
    }

    [Fact]
    public void A_soft_folder_has_a_shadow_under_it()
    {
        using var soft = Frame(Plain(FolderShape.Soft3D), 256);
        using var flat = Frame(Plain(FolderShape.Flat), 256);
        var below = 256 - 27;

        Assert.True(soft.GetPixel(128, below).A > flat.GetPixel(128, below).A);
    }

    /// <summary>
    /// Where the stock Windows 11 folder sits in each frame, measured from the frames Windows
    /// ships. Only these numbers are kept here: a colored folder beside a stock one should line
    /// up with it, and no part of the stock art is in this repository.
    /// </summary>
    [Theory]
    [InlineData(16, 0, 1, 15, 13)]
    [InlineData(20, 1, 3, 18, 16)]
    [InlineData(24, 1, 3, 22, 19)]
    [InlineData(32, 2, 4, 29, 25)]
    [InlineData(48, 3, 6, 44, 38)]
    [InlineData(64, 4, 9, 59, 52)]
    [InlineData(256, 16, 34, 239, 209)]
    public void A_classic_folder_stands_where_the_stock_folder_stands(int size, int left, int top, int right, int bottom)
    {
        using var frame = Frame(Plain(), size);

        var solid = Enumerable.Range(0, size)
            .SelectMany(y => Enumerable.Range(0, size).Select(x => (X: x, Y: y)))
            .Where(p => frame.GetPixel(p.X, p.Y).A > 40)
            .ToArray();

        Assert.Equal(left, solid.Min(p => p.X));
        Assert.Equal(right, solid.Max(p => p.X));
        Assert.Equal(top, solid.Min(p => p.Y));
        Assert.Equal(bottom, solid.Max(p => p.Y));
    }

    /// <summary>The stock folder has its light at every size, so a flat small frame would look out of place beside it.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(256)]
    public void A_classic_folder_is_lit_from_the_top_left_at_every_size(int size)
    {
        using var frame = Frame(Plain(), size);

        var lit = frame.GetPixel(size / 8, size * 45 / 100);
        var shaded = frame.GetPixel(size * 7 / 8, size * 72 / 100);

        Assert.True(lit.GetBrightness() > shaded.GetBrightness(), $"{lit} is not brighter than {shaded}.");
    }

    [Fact]
    public void The_back_of_a_folder_stays_vivid_instead_of_going_muddy()
    {
        var yellow = IconColor.Parse("#FFCC3F");

        using var frame = Frame(new FolderIconSpec { Fill = FolderFill.Solid(yellow) }, 256);
        var tab = frame.GetPixel(50, 44);

        Assert.True(tab.GetSaturation() > 0.9f, $"The tab is {tab}.");
        Assert.True(tab.GetBrightness() < Color.FromArgb(yellow.R, yellow.G, yellow.B).GetBrightness());
    }

    [Fact]
    public void A_near_black_folder_keeps_a_tab_you_can_see()
    {
        using var frame = Frame(new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#1F2937")) }, 256);

        Assert.True(frame.GetPixel(50, 44).GetBrightness() > frame.GetPixel(200, 180).GetBrightness());
    }

    [Fact]
    public void A_soft_shadow_fades_out_instead_of_stopping_in_steps()
    {
        using var frame = Frame(Plain(FolderShape.Soft3D), 256);

        var alphas = Enumerable.Range(212, 26).Select(y => (int)frame.GetPixel(128, y).A).ToArray();

        Assert.Equal(alphas.OrderDescending(), alphas);
        Assert.True(alphas.Distinct().Count() >= 10, $"Only {alphas.Distinct().Count()} levels in [{string.Join(',', alphas)}].");
    }

    [Fact]
    public void A_bottom_badge_sits_on_the_corner_of_the_folder()
    {
        using var frame = Frame(Plain() with { Badge = new IconBadge("done") }, 256);

        var lowest = Enumerable.Range(0, 256).Last(y => frame.GetPixel(210, y).A > 200);

        Assert.InRange(lowest, 210, 236);
    }

    /// <summary>Writes pictures of the art to look at, when FOLDCRAFT_ART_DIR is set. Nothing else reads them.</summary>
    [Fact]
    public void Draw_contact_sheets_when_asked()
    {
        var directory = Environment.GetEnvironmentVariable("FOLDCRAFT_ART_DIR");

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);

        foreach (var (name, spec) in new[]
                 {
                     ("light", new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#F9D66B")) }),
                     ("dark", new FolderIconSpec { Fill = FolderFill.Solid(Blue) }),
                 })
        {
            Sheet(Path.Combine(directory, $"glyphs-{name}.png"), GlyphCatalog.All.Select(g => (g.Id, spec with { GlyphId = g.Id })).ToArray());
        }

        var plain = new FolderIconSpec { Fill = FolderFill.Solid(Blue) };

        Sheet(Path.Combine(directory, "badges.png"), BadgeCatalog.All.Select(b => (b.Id, plain with { Badge = new IconBadge(b.Id) })).ToArray());

        Sheet(Path.Combine(directory, "emoji.png"), new[] { "\U0001F680", "❤️", "\U0001F44D\U0001F3FF", "\U0001F468‍\U0001F469‍\U0001F467", "\U0001F355", "1️⃣" }
            .Select(emoji => (emoji, plain with { Emoji = emoji }))
            .ToArray());

        Sheet(Path.Combine(directory, "shapes.png"), Enum.GetValues<FolderShape>()
            .SelectMany(shape => new[]
            {
                (shape.ToString(), plain with { Shape = shape }),
                (shape + " star", plain with { Shape = shape, GlyphId = "star" }),
                (shape + " badge", plain with { Shape = shape, GlyphId = "music", Badge = new IconBadge("done") }),
            })
            .ToArray());

        string[] colors = ["#2F6FED", "#F59E0B", "#16A34A", "#DC2626", "#7C3AED", "#1F2937", "#CBD5E1"];
        string[] glyphs = ["star", "music", "code", "image", "heart"];
        string[] badges = ["done", "important", "favorite", "waiting", "new"];

        Sheet(Path.Combine(directory, "folders.png"), Enum.GetValues<FolderShape>()
            .SelectMany(shape => colors.Select((color, i) =>
            {
                var look = new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse(color)), Shape = shape, GlyphId = i % 2 == 0 ? glyphs[i % glyphs.Length] : null };

                return ($"{shape} {color}", i % 3 == 0 ? look : look with { Badge = new IconBadge(badges[i % badges.Length]) });
            }))
            .ToArray());
    }

    /// <summary>Each look at 16, 32 and 48 px, blown up without smoothing so single pixels show, and at 256 px as it is.</summary>
    private static void Sheet(string path, IReadOnlyList<(string Name, FolderIconSpec Spec)> looks)
    {
        const int Zoom = 2;
        const int Columns = 6;
        const int CellWidth = 480;
        const int CellHeight = 280;

        var rows = (looks.Count + Columns - 1) / Columns;

        using var sheet = new Bitmap(CellWidth * Columns, CellHeight * rows);
        using var graphics = Graphics.FromImage(sheet);
        using var font = new Font("Segoe UI", 9f);

        graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        for (var i = 0; i < looks.Count; i++)
        {
            var (x, y) = ((i % Columns) * CellWidth, (i / Columns) * CellHeight);
            var offset = 4;

            graphics.DrawString(looks[i].Name, font, Brushes.Black, x + 2, y);

            foreach (var size in new[] { 16, 32, 48 })
            {
                using var frame = Frame(looks[i].Spec, size);
                graphics.DrawImage(frame, new Rectangle(x + offset, y + 20, size * Zoom, size * Zoom));
                offset += (size * Zoom) + 8;
            }

            using var large = Frame(looks[i].Spec, 256);
            graphics.DrawImage(large, new Rectangle(x + offset, y + 20, 256, 256));
        }

        sheet.Save(path, ImageFormat.Png);
    }
}
