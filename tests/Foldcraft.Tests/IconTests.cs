using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Foldcraft.Core.Customization;
using Foldcraft.Core.Icons;

namespace Foldcraft.Tests;

public sealed class IconTests : IDisposable
{
    private static readonly int[] RequiredSizes = [16, 20, 24, 32, 40, 48, 64, 96, 256];

    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private static FolderIconSpec Blue(string? glyph = null) => new()
    {
        Fill = FolderFill.Solid(IconColor.Parse("#2F6FED")),
        GlyphId = glyph,
    };

    /// <summary>GDI+ keeps reading the stream, so it must outlive the bitmap.</summary>
    private static Bitmap Frame(FolderIconSpec spec, int size) =>
        new(new MemoryStream(new IconRenderer().RenderPreviewPng(spec, size)));

    private static byte[] Pixels(Bitmap bitmap)
    {
        using var copy = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppArgb);
        var data = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            copy.UnlockBits(data);
        }
    }

    private string WriteImage(string name, int width, int height, Color color, ImageFormat? format = null)
    {
        var path = Path.Combine(_box.Root, name);
        using var bitmap = new Bitmap(width, height);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(color);
        }

        bitmap.Save(path, format ?? ImageFormat.Png);
        return path;
    }

    [Fact]
    public void The_directory_lists_every_required_size_as_a_valid_ico()
    {
        var bytes = new IconRenderer().RenderIco(Blue("star"));

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        Assert.Equal(RequiredSizes.Length, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)));

        var frames = IcoFile.TryReadFrames(bytes)!;

        Assert.Equal(RequiredSizes, frames.Select(f => f.Width).Order());
        Assert.All(frames, f => Assert.Equal(f.Width, f.Height));
        Assert.All(frames, f => Assert.Equal(32, f.BitsPerPixel));

        for (var i = 0; i < frames.Count; i++)
        {
            var entry = bytes.AsSpan(6 + (16 * i), 16);
            Assert.Equal(frames[i].Width == 256 ? 0 : frames[i].Width, entry[0]);
            Assert.Equal(0, entry[2]);
            Assert.Equal(0, entry[3]);
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]));
        }

        var ordered = frames.OrderBy(f => f.Offset).ToArray();
        Assert.Equal(6 + (16 * RequiredSizes.Length), ordered[0].Offset);

        for (var i = 1; i < ordered.Length; i++)
        {
            Assert.Equal(ordered[i - 1].Offset + ordered[i - 1].Length, ordered[i].Offset);
        }

        Assert.Equal(bytes.Length, ordered[^1].Offset + ordered[^1].Length);
    }

    [Fact]
    public void Only_the_256_frame_is_png()
    {
        var frames = IcoFile.TryReadFrames(new IconRenderer().RenderIco(Blue("star")))!;

        Assert.All(frames, f => Assert.Equal(f.Width == 256, f.IsPng));
    }

    [Fact]
    public void Small_frames_are_standard_32bpp_dibs_with_an_and_mask()
    {
        var bytes = new IconRenderer().RenderIco(Blue("star"));

        foreach (var frame in IcoFile.TryReadFrames(bytes)!.Where(f => !f.IsPng))
        {
            var dib = bytes.AsSpan(frame.Offset, frame.Length);
            var maskStride = ((frame.Width + 31) / 32) * 4;
            var expected = 40 + (frame.Width * frame.Height * 4) + (maskStride * frame.Height);

            Assert.Equal(expected, frame.Length);
            Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(dib));
            Assert.Equal(frame.Width, BinaryPrimitives.ReadInt32LittleEndian(dib[4..]));
            Assert.Equal(frame.Height * 2, BinaryPrimitives.ReadInt32LittleEndian(dib[8..]));
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(dib[12..]));
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]));
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(dib[16..]));

            // The top-left corner is transparent. Rows are bottom-up, so that is the last mask row.
            var lastMaskRow = dib[^maskStride..];
            Assert.Equal(0x80, lastMaskRow[0] & 0x80);
        }
    }

    [Fact]
    public void Windows_itself_decodes_every_frame()
    {
        var bytes = new IconRenderer().RenderIco(Blue("star"));

        foreach (var frame in IcoFile.TryReadFrames(bytes)!)
        {
            var image = bytes.AsSpan(frame.Offset, frame.Length).ToArray();
            var icon = CreateIconFromResourceEx(image, image.Length, true, 0x00030000, frame.Width, frame.Height, 0);

            Assert.True(icon != IntPtr.Zero, $"Windows rejected the {frame.Width} px frame.");
            DestroyIcon(icon);
        }
    }

    [Fact]
    public void The_Windows_file_loader_finds_every_size_in_the_finished_file()
    {
        var path = _box.Icons.Ensure(Blue("star"));

        foreach (var size in RequiredSizes)
        {
            var handle = LoadImage(IntPtr.Zero, path, ImageIcon, size, size, LoadFromFile);
            Assert.True(handle != IntPtr.Zero, $"LoadImage failed at {size} px.");

            try
            {
                using var icon = Icon.FromHandle(handle);
                Assert.Equal(size, icon.Width);
                Assert.Equal(size, icon.Height);
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
    }

    [Fact]
    public void Every_frame_decodes_to_pixels_with_a_transparent_corner()
    {
        var bytes = new IconRenderer().RenderIco(Blue("star"));

        foreach (var frame in IcoFile.TryReadFrames(bytes)!)
        {
            using var bitmap = frame.IsPng ? DecodePng(bytes, frame) : DecodeDib(bytes, frame);

            Assert.Equal(frame.Width, bitmap.Width);
            Assert.Equal(frame.Height, bitmap.Height);
            Assert.Equal(0, bitmap.GetPixel(0, 0).A);
            Assert.Equal(255, bitmap.GetPixel(frame.Width / 2, frame.Height / 2).A);
        }
    }

    private static Bitmap DecodePng(byte[] bytes, IcoFrame frame) =>
        new(new MemoryStream(bytes, frame.Offset, frame.Length));

    /// <summary>System.Drawing.Icon cannot select PNG frames, but reads a DIB frame from its own file.</summary>
    private static Bitmap DecodeDib(byte[] bytes, IcoFrame frame)
    {
        using var single = new MemoryStream();
        using var writer = new BinaryWriter(single);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(bytes, 6 + (16 * Array.IndexOf(IcoFile.TryReadFrames(bytes)!.ToArray(), frame)), 12);
        writer.Write(22);
        writer.Write(bytes, frame.Offset, frame.Length);
        single.Position = 0;

        using var icon = new Icon(single, new Size(frame.Width, frame.Height));
        return icon.ToBitmap();
    }

    [Fact]
    public void A_corrupt_directory_is_not_accepted_as_an_icon()
    {
        Assert.Null(IcoFile.TryReadFrames(new byte[] { 0, 0, 2, 0, 1, 0 }));
        Assert.Null(IcoFile.TryReadFrames(new byte[] { 0, 0, 1, 0, 0, 0 }));
        Assert.Null(IcoFile.TryReadFrames(new byte[] { 0, 0, 1, 0, 1, 0, 16, 16 }));

        var bytes = new IconRenderer().RenderIco(Blue());
        Assert.Null(IcoFile.TryReadFrames(bytes[..(bytes.Length / 2)]));
    }

    [Fact]
    public void Each_size_is_drawn_separately_with_a_transparent_background()
    {
        foreach (var size in RequiredSizes)
        {
            using var bitmap = Frame(Blue(), size);

            Assert.Equal(0, bitmap.GetPixel(0, 0).A);
            Assert.Equal(0, bitmap.GetPixel(size - 1, 0).A);
            Assert.Equal(0, bitmap.GetPixel(size - 1, size - 1).A);
            Assert.Equal(255, bitmap.GetPixel(size / 2, size / 2).A);
        }
    }

    [Fact]
    public void Small_frames_have_crisp_vertical_edges()
    {
        using var bitmap = Frame(Blue(), 16);
        var y = 10;

        // The body's edge lands on a pixel boundary: no half-transparent column beside it.
        var alphas = Enumerable.Range(0, 16).Select(x => bitmap.GetPixel(x, y).A).ToArray();

        Assert.All(alphas, a => Assert.True(a is 0 or 255, $"Soft edge with alpha {a} in [{string.Join(',', alphas)}]"));
    }

    [Fact]
    public void The_catalog_keeps_every_original_glyph_and_has_at_least_sixty_unique_ones()
    {
        string[] original =
        [
            "star", "heart", "code", "music", "image", "video", "document", "game",
            "work", "school", "money", "download", "lock", "cloud", "tools", "flag",
        ];

        var ids = GlyphCatalog.All.Select(g => g.Id).ToArray();

        Assert.Subset(ids.ToHashSet(), original.ToHashSet());
        Assert.True(ids.Length >= 60, $"Only {ids.Length} glyphs.");
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(GlyphCatalog.All, g => Assert.False(string.IsNullOrWhiteSpace(g.DisplayName)));
    }

    public static TheoryData<string> GlyphIds()
    {
        var data = new TheoryData<string>();

        foreach (var glyph in GlyphCatalog.All)
        {
            data.Add(glyph.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(GlyphIds))]
    public void Each_glyph_is_visible_at_every_size(string id)
    {
        foreach (var size in RequiredSizes)
        {
            using var plain = Frame(Blue(), size);
            using var withGlyph = Frame(Blue(id), size);

            Assert.NotEqual(Pixels(plain), Pixels(withGlyph));
        }
    }

    [Fact]
    public void Every_glyph_looks_different_from_every_other()
    {
        var renders = GlyphCatalog.All.Select(g =>
        {
            using var bitmap = Frame(Blue(g.Id), 64);
            return Convert.ToHexString(Pixels(bitmap));
        }).ToArray();

        Assert.Equal(renders.Length, renders.Distinct().Count());
    }

    [Fact]
    public void Glyph_ids_ignore_case()
    {
        Assert.NotNull(GlyphCatalog.Find("STAR"));
        Assert.Null(GlyphCatalog.Find("nonsense"));
    }

    [Fact]
    public void A_gradient_renders_and_its_angle_matters()
    {
        var (start, end) = (IconColor.Parse("#FF0000"), IconColor.Parse("#0000FF"));

        using var solid = Frame(new FolderIconSpec { Fill = FolderFill.Solid(start) }, 64);
        using var down = Frame(new FolderIconSpec { Fill = FolderFill.Gradient(start, end, 90f) }, 64);
        using var across = Frame(new FolderIconSpec { Fill = FolderFill.Gradient(start, end, 0f) }, 64);

        Assert.NotEqual(Pixels(solid), Pixels(down));
        Assert.NotEqual(Pixels(down), Pixels(across));

        Assert.True(across.GetPixel(10, 40).R > across.GetPixel(54, 40).R);
        Assert.True(down.GetPixel(32, 24).R > down.GetPixel(32, 54).R);
    }

    [Theory]
    [InlineData("#2F6FED", 0x2F, 0x6F, 0xED)]
    [InlineData("000000", 0, 0, 0)]
    public void Colors_parse(string text, byte r, byte g, byte b)
    {
        Assert.Equal(new IconColor(r, g, b), IconColor.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void Bad_colors_are_rejected(string text)
    {
        Assert.Throws<FormatException>(() => IconColor.Parse(text));
    }

    [Fact]
    public void Icon_names_are_a_stable_hash_of_the_spec()
    {
        var same = _box.Icons.Ensure(Blue("star"));
        var again = _box.Icons.Ensure(Blue("star"));
        var differentGlyph = _box.Icons.Ensure(Blue("heart"));
        var differentColor = _box.Icons.Ensure(new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#FF0000")), GlyphId = "star" });

        Assert.Equal(same, again);
        Assert.NotEqual(same, differentGlyph);
        Assert.NotEqual(same, differentColor);
        Assert.Equal(_box.Paths.IconsDirectory, Path.GetDirectoryName(same));
        Assert.Matches("^[0-9a-f]{32}\\.ico$", Path.GetFileName(same));
    }

    [Fact]
    public void Equivalent_gradient_angles_share_an_icon()
    {
        var (start, end) = (IconColor.Parse("#FF0000"), IconColor.Parse("#0000FF"));

        Assert.Equal(
            _box.Icons.Ensure(new FolderIconSpec { Fill = FolderFill.Gradient(start, end, 45f) }),
            _box.Icons.Ensure(new FolderIconSpec { Fill = FolderFill.Gradient(start, end, 405f) }));
    }

    [Fact]
    public void An_existing_icon_is_not_rewritten()
    {
        var path = _box.Icons.Ensure(Blue("star"));
        var first = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, first);

        _box.Icons.Ensure(Blue("star"));

        Assert.Equal(first, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_damaged_icon_is_replaced_atomically()
    {
        var path = _box.Icons.Ensure(Blue("star"));
        File.WriteAllBytes(path, [1, 2, 3]);

        var again = _box.Icons.Ensure(Blue("star"));

        Assert.Equal(path, again);
        Assert.Equal(RequiredSizes.Length, IcoFile.TryReadFrames(File.ReadAllBytes(path))!.Count);
        Assert.Single(Directory.GetFiles(_box.Paths.IconsDirectory));
    }

    [Fact]
    public void A_custom_image_counts_by_content_not_by_path()
    {
        var first = WriteImage("one.png", 32, 32, Color.Red);
        var sameContent = Path.Combine(_box.Root, "two.png");
        File.Copy(first, sameContent);
        var fill = FolderFill.Solid(IconColor.Parse("#222222"));

        var a = _box.Icons.Ensure(new FolderIconSpec { Fill = fill, CustomImagePath = first });
        var b = _box.Icons.Ensure(new FolderIconSpec { Fill = fill, CustomImagePath = sameContent });

        WriteImage("one.png", 32, 32, Color.Lime);
        var c = _box.Icons.Ensure(new FolderIconSpec { Fill = fill, CustomImagePath = first });

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void A_wide_custom_image_is_letterboxed_inside_the_folder()
    {
        var image = WriteImage("wide.png", 400, 100, Color.Red);
        var spec = new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#00FF00")), CustomImagePath = image };

        using var bitmap = Frame(spec, 256);

        var center = bitmap.GetPixel(128, 138);
        Assert.True(center.R > 200 && center.G < 60, $"Image missing at center: {center}");

        // Above and below the image the folder shows through, so the aspect ratio was kept.
        var above = bitmap.GetPixel(128, 80);
        Assert.True(above.G > 200 && above.R < 60, $"Expected folder color, got {above}");
        var below = bitmap.GetPixel(128, 200);
        Assert.True(below.G > 180 && below.R < 80, $"Expected folder color, got {below}");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(256)]
    public void An_extreme_aspect_ratio_still_renders(int size)
    {
        var image = WriteImage("sliver.png", 800, 2, Color.Red);
        var spec = new FolderIconSpec { Fill = FolderFill.Solid(IconColor.Parse("#00FF00")), CustomImagePath = image };

        using var bitmap = Frame(spec, size);

        Assert.Equal(size, bitmap.Width);
    }

    [Theory]
    [InlineData(nameof(ImageFormat.Jpeg))]
    [InlineData(nameof(ImageFormat.Bmp))]
    [InlineData(nameof(ImageFormat.Gif))]
    public void Common_image_formats_decode(string format)
    {
        var imageFormat = format switch
        {
            nameof(ImageFormat.Jpeg) => ImageFormat.Jpeg,
            nameof(ImageFormat.Bmp) => ImageFormat.Bmp,
            _ => ImageFormat.Gif,
        };

        var path = WriteImage("picture." + format, 64, 64, Color.Red, imageFormat);

        var bytes = new IconRenderer().RenderIco(new FolderIconSpec { Fill = FolderFill.Solid(new IconColor(0, 0, 0)), CustomImagePath = path });

        Assert.Equal(RequiredSizes.Length, IcoFile.TryReadFrames(bytes)!.Count);
    }

    [Fact]
    public void A_file_that_is_not_an_image_is_a_clean_error()
    {
        var path = Path.Combine(_box.Root, "fake.png");
        File.WriteAllText(path, "this is not an image");

        AssertImageError(path);
    }

    [Fact]
    public void A_truncated_image_is_a_clean_error_or_a_render_never_a_crash()
    {
        var good = WriteImage("whole.png", 200, 200, Color.Red);
        var bytes = File.ReadAllBytes(good);
        var path = Path.Combine(_box.Root, "truncated.png");
        File.WriteAllBytes(path, bytes[..(bytes.Length / 3)]);

        var spec = new FolderIconSpec { Fill = FolderFill.Solid(new IconColor(0, 0, 0)), CustomImagePath = path };

        try
        {
            new IconRenderer().RenderIco(spec);
        }
        catch (IconRenderException)
        {
            // Also acceptable: GDI+ tolerates some truncation. Anything else fails the test.
        }
    }

    [Fact]
    public void A_missing_image_is_a_clean_error()
    {
        AssertImageError(Path.Combine(_box.Root, "does-not-exist.png"));
    }

    [Fact]
    public void An_empty_image_is_a_clean_error()
    {
        var path = Path.Combine(_box.Root, "empty.png");
        File.WriteAllBytes(path, []);

        AssertImageError(path);
    }

    [Fact]
    public void A_directory_as_an_image_is_a_clean_error()
    {
        AssertImageError(_box.Root);
    }

    [Fact]
    public void An_oversized_file_is_refused_before_it_is_read()
    {
        var path = WriteImage("big-file.png", 64, 64, Color.Red);

        AssertImageError(path, new ImageLimits { MaxFileBytes = 10 });
    }

    [Fact]
    public void Oversized_dimensions_are_refused()
    {
        var path = WriteImage("big-dimensions.png", 300, 20, Color.Red);

        AssertImageError(path, new ImageLimits { MaxDimension = 200 });
    }

    [Fact]
    public void Too_many_total_pixels_are_refused()
    {
        var path = WriteImage("big-area.png", 100, 100, Color.Red);

        AssertImageError(path, new ImageLimits { MaxPixels = 9_999 });
    }

    private static void AssertImageError(string path, ImageLimits? limits = null)
    {
        var spec = new FolderIconSpec { Fill = FolderFill.Solid(new IconColor(0, 0, 0)), CustomImagePath = path };

        var error = Assert.Throws<IconRenderException>(() => new IconRenderer(limits).RenderIco(spec));

        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void A_spec_with_both_glyph_and_image_is_rejected()
    {
        var spec = Blue("star") with { CustomImagePath = "x.png" };

        Assert.Throws<IconRenderException>(() => new IconRenderer().RenderIco(spec));
    }

    [Fact]
    public void An_unknown_glyph_is_rejected()
    {
        Assert.Throws<IconRenderException>(() => new IconRenderer().RenderIco(Blue("nonsense")));
    }

    [Fact]
    public void An_unknown_badge_is_refused_rather_than_silently_ignored()
    {
        var spec = Blue() with { Badge = new IconBadge("nonsense") };

        Assert.Throws<IconRenderException>(() => new IconRenderer().RenderIco(spec));
    }

    [Fact]
    public void The_preview_writes_nothing_to_disk()
    {
        var bytes = new IconRenderer().RenderPreviewPng(Blue("star"), 128);

        Assert.True(bytes.Length > 0);
        Assert.False(Directory.Exists(_box.Paths.IconsDirectory));
    }

    [Fact]
    public void Pruning_never_deletes_an_icon_in_use_by_an_active_folder()
    {
        var active = _box.NewFolder("active");
        var reverted = _box.NewFolder("reverted");
        var customizer = _box.Customizer();

        customizer.Apply(active, Sandbox.BlueStar);
        customizer.Apply(reverted, Sandbox.GreenHeart);
        _box.Restorer().UndoLast();

        var activeIcon = Directory.GetFiles(_box.Paths.IconsDirectory).Single(f => File.ReadAllBytes(f).SequenceEqual(new IconRenderer().RenderIco(Sandbox.BlueStar)));
        var orphan = _box.Icons.Ensure(Blue("cloud"));

        var deleted = _box.Icons.PruneUnused(_box.Journal);

        Assert.Equal(2, deleted);
        Assert.True(File.Exists(activeIcon));
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public void Pruning_keeps_an_icon_named_by_a_recorded_original()
    {
        var folder = _box.NewFolder();
        var customizer = _box.Customizer();

        // The first change is forgotten, so only the second entry's backup still names icon A.
        customizer.Apply(folder, Sandbox.BlueStar);
        var iconA = DesktopIniIcon(folder);
        _box.Journal.MarkForgotten(_box.Journal.Read().Entries[0].Id);
        customizer.Apply(folder, Sandbox.GreenHeart);
        File.Delete(iconA);
        _box.Icons.Ensure(Sandbox.BlueStar);

        Assert.Equal(0, _box.Icons.PruneUnused(_box.Journal));
        Assert.True(File.Exists(iconA));
    }

    [Fact]
    public void Pruning_recognizes_an_icon_recorded_under_an_older_data_path()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var icon = Assert.Single(Directory.GetFiles(_box.Paths.IconsDirectory));

        // A moved profile changes the directory, never the content-hash file name.
        var moved = new Foldcraft.Core.Journal.ChangeJournal(Path.Combine(_box.Root, "moved.fcj"));
        moved.RecordChange(_box.Journal.Read().Entries[0].Change with
        {
            IconPath = Path.Combine(@"E:\OldProfile\Icons", Path.GetFileName(icon)),
        });

        Assert.Equal(0, _box.Icons.PruneUnused(moved));
        Assert.True(File.Exists(icon));
    }

    private static string DesktopIniIcon(string folder)
    {
        var value = Foldcraft.Core.Ini.DesktopIni.Load(Sandbox.IniPath(folder)).GetValue(".ShellClassInfo", "IconResource")!;
        return value[..value.LastIndexOf(',')];
    }

    [Fact]
    public void Pruning_deletes_nothing_while_the_journal_has_unreadable_records()
    {
        var orphan = _box.Icons.Ensure(Blue("cloud"));
        _box.Customizer().Apply(_box.NewFolder(), Sandbox.BlueStar);
        File.AppendAllText(_box.Paths.JournalFile, "torn record");

        Assert.Equal(0, _box.Icons.PruneUnused(_box.Journal));
        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public void Pruning_leaves_files_it_does_not_recognize()
    {
        Directory.CreateDirectory(_box.Paths.IconsDirectory);
        var stranger = Path.Combine(_box.Paths.IconsDirectory, "custom.ico");
        File.WriteAllBytes(stranger, [1]);

        Assert.Equal(0, _box.Icons.PruneUnused(_box.Journal));
        Assert.True(File.Exists(stranger));
    }

    [Fact]
    public void Undoing_a_change_leaves_the_library_alone()
    {
        var folder = _box.NewFolder();
        _box.Customizer().Apply(folder, Sandbox.BlueStar);
        var icon = Assert.Single(Directory.GetFiles(_box.Paths.IconsDirectory));

        Assert.True(_box.Restorer().RestoreAll().AllRestored);

        Assert.True(File.Exists(icon));
    }

    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(byte[] bits, int size, bool isIcon, int version, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW", SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
