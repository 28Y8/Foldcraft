using System.Drawing;
using System.Drawing.Imaging;
using Foldcraft.Core.Icons;

namespace Foldcraft.Tests;

public sealed class EmojiTests
{
    private const string Rocket = "\U0001F680";
    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
    private const string ThumbsUpDark = "\U0001F44D\U0001F3FF";
    private const string Keycap = "1\uFE0F\u20E3";
    private const string Heart = "\u2764\uFE0F";

    private static FolderIconSpec With(string emoji) =>
        new() { Fill = FolderFill.Solid(IconColor.Parse("#2F6FED")), Emoji = emoji };

    private static Bitmap Frame(FolderIconSpec spec, int size) =>
        new(new MemoryStream(new IconRenderer().RenderPreviewPng(spec, size)));

    [Theory]
    [InlineData(Rocket)]
    [InlineData(Family)]
    [InlineData(ThumbsUpDark)]
    [InlineData(Keycap)]
    [InlineData(Heart)]
    public void One_emoji_is_accepted(string text)
    {
        Assert.True(EmojiText.IsValid(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("hello")]
    [InlineData("1")]
    [InlineData("#")]
    [InlineData(Rocket + Rocket)]
    [InlineData(Rocket + " ")]
    [InlineData("a" + Rocket)]
    [InlineData(@"C:\Windows")]
    [InlineData("\u200D")]
    [InlineData("\uFE0F")]
    [InlineData("\uD83D")]
    public void Anything_else_is_refused(string? text)
    {
        Assert.False(EmojiText.IsValid(text));
    }

    [Fact]
    public void A_very_long_sequence_is_refused()
    {
        Assert.False(EmojiText.IsValid(string.Concat(Enumerable.Repeat("\U0001F600\u200D", 40))));
    }

    [Fact]
    public void A_spec_with_something_that_is_not_an_emoji_cannot_be_rendered()
    {
        Assert.Throws<IconRenderException>(() => new IconRenderer().RenderIco(With("not an emoji")));
    }

    [Fact]
    public void An_emoji_cannot_be_combined_with_a_glyph_or_a_picture()
    {
        Assert.Throws<IconRenderException>(() => (With(Rocket) with { GlyphId = "star" }).Validate());
        Assert.Throws<IconRenderException>(() => (With(Rocket) with { CustomImagePath = "x.png" }).Validate());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(256)]
    public void An_emoji_shows_on_the_folder_at_each_size(int size)
    {
        using var plain = Frame(new FolderIconSpec { Fill = With(Rocket).Fill }, size);
        using var withEmoji = Frame(With(Rocket), size);

        var changed = 0;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (plain.GetPixel(x, y) != withEmoji.GetPixel(x, y))
                {
                    changed++;
                }
            }
        }

        Assert.True(changed > size / 2, $"Only {changed} pixels changed at {size} px.");
    }

    [Fact]
    public void An_emoji_is_part_of_the_icons_identity()
    {
        using var box = new Sandbox();

        Assert.NotEqual(box.Icons.ExpectedPath(With(Rocket)), box.Icons.ExpectedPath(With(Heart)));
    }

    [Fact]
    public void A_complete_icon_can_be_written_with_an_emoji()
    {
        var bytes = new IconRenderer().RenderIco(With(Rocket));

        Assert.Equal(IconRenderer.Sizes.Count, IcoFile.TryReadFrames(bytes)?.Count);
    }

    [Fact]
    public void The_one_color_fallback_draws_into_the_box_it_was_given()
    {
        using var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        EmojiArtist.DrawInOneColor(graphics, Rocket, Color.Black, new Rectangle(16, 16, 32, 32));

        var inside = 0;

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                if (bitmap.GetPixel(x, y).A > 0)
                {
                    Assert.InRange(x, 15, 48);
                    Assert.InRange(y, 15, 48);
                    inside++;
                }
            }
        }

        Assert.True(inside > 50);
    }

    /// <summary>
    /// The color route is allowed to come back empty, where Windows can't do it. When it does
    /// answer, the answer has to be a real picture of the size asked for, in more than one color.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(128)]
    public void The_color_route_gives_a_picture_or_nothing(int side)
    {
        using var picture = EmojiArtist.TryDrawInColor(Rocket, side, Color.White);

        if (picture is null)
        {
            return;
        }

        Assert.Equal(side, picture.Width);
        Assert.Equal(side, picture.Height);

        var colors = new HashSet<int>();

        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var pixel = picture.GetPixel(x, y);

                if (pixel.A == 255)
                {
                    colors.Add(pixel.ToArgb());
                }
            }
        }

        Assert.True(colors.Count > 1 || side < 32, "A color picture came out in one color.");
    }

    [Fact]
    public void The_color_route_does_not_mind_which_thread_it_runs_on()
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                EmojiArtist.TryDrawInColor(Rocket, 32, Color.White)?.Dispose();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }
}
