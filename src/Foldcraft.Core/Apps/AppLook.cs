using Foldcraft.Core.Icons;

namespace Foldcraft.Core.Apps;

/// <summary>An app's picture and the colors read from it, ready to be turned into looks.</summary>
/// <param name="App">The app.</param>
/// <param name="Art">Its picture, or null if none could be read.</param>
/// <param name="Sample">A sample of the picture.</param>
/// <param name="Palette">Colors picked from the picture.</param>
public sealed record PreparedApp(DetectedApp App, AppArt? Art, LogoSample Sample, IconPalette Palette);

/// <summary>What the user changed for one item. Anything left null is worked out automatically.</summary>
public sealed record AppLookChoices
{
    public IconColor? Start { get; init; }
    public IconColor? End { get; init; }
    public FolderShape Shape { get; init; } = FolderShape.Classic;
    public LogoAnchor Anchor { get; init; } = LogoAnchor.Center;
    public float Scale { get; init; } = 1f;

    /// <summary>Null lets the contrast check decide.</summary>
    public LogoPlate? Plate { get; init; }
}

/// <summary>What the contrast check had to do.</summary>
public enum ContrastFix
{
    /// <summary>The logo stood out as it was.</summary>
    None,

    /// <summary>The folder's color moved away from the logo's.</summary>
    ShiftedColor,

    /// <summary>The logo got a plate of its own.</summary>
    AddedPlate,
}

/// <summary>A finished look, and how it got there.</summary>
public sealed record AppLook(FolderIconSpec Spec, ContrastFix Fix);

/// <summary>Reads an app's picture and works out its colors. This only reads.</summary>
public sealed class AppLookBuilder(IAppIconReader reader)
{
    private static readonly LogoSample NoSample = new([]);

    public PreparedApp Prepare(DetectedApp app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        var art = reader.Read(app.Icons, cancellationToken);
        var sample = art is null ? NoSample : LogoSample.From(art.Png);

        return new PreparedApp(app, art, sample, IconPalette.Extract(sample));
    }
}

/// <summary>
/// Puts a logo on a folder. The folder's color comes from the logo, so a logo melting into its
/// own folder is the normal case to guard against, not a rare one. The check tries the gentle
/// fix first: a darker or lighter folder keeps the brand color and needs nothing added. Only
/// when that isn't enough does the logo get a plate.
/// </summary>
public static class AppLookComposer
{
    /// <summary>The share of a logo that has to stand out for it to read as itself.</summary>
    public const double EnoughVisible = 0.6;

    /// <summary>How far the folder's lightness may move and still be the brand's color.</summary>
    private const double MaxLightnessShift = 0.24;

    private const double LightnessStep = 0.06;

    /// <summary>The placeholder path for art that's in memory until a folder needs it.</summary>
    public const string ArtNotSavedYet = "unsaved-art.png";

    private const string GameSymbol = "game";
    private const string AppSymbol = "sparkle";

    public static AppLook Compose(PreparedApp prepared, AppLookChoices? choices = null)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        choices ??= new AppLookChoices();

        var automatic = prepared.Palette.ToFill();
        var chosenColor = choices.Start is not null;
        var fill = chosenColor ? new FolderFill(choices.Start!.Value, choices.End, automatic.AngleDegrees) : automatic;

        if (prepared.Art is null)
        {
            return new AppLook(
                new FolderIconSpec
                {
                    Fill = fill,
                    Shape = choices.Shape,
                    GlyphId = prepared.App.IsGame ? GameSymbol : AppSymbol,
                },
                ContrastFix.None);
        }

        var (finalFill, plate, fix) = EnsureVisible(prepared, fill, choices, mayShift: !chosenColor);

        return new AppLook(
            new FolderIconSpec
            {
                Fill = finalFill,
                Shape = choices.Shape,
                CustomImagePath = ArtNotSavedYet,
                Logo = new LogoPlacement(MathF.Round(Math.Clamp(choices.Scale, LogoPlacement.MinScale, LogoPlacement.MaxScale), 2), choices.Anchor, plate),
            },
            fix);
    }

    private static (FolderFill Fill, LogoPlate Plate, ContrastFix Fix) EnsureVisible(
        PreparedApp prepared,
        FolderFill fill,
        AppLookChoices choices,
        bool mayShift)
    {
        var sample = prepared.Sample;

        if (choices.Plate is { } chosen)
        {
            return (fill, chosen, ContrastFix.None);
        }

        // An outline folder has no fill to compare against, and a plate would hide what it shows.
        if (choices.Shape == FolderShape.Outline || sample.Pixels.Count == 0 ||
            sample.VisibleShare(IconPalette.MiddleOf(fill)) >= EnoughVisible)
        {
            return (fill, LogoPlate.None, ContrastFix.None);
        }

        if (mayShift && TryShift(sample, fill) is { } shifted)
        {
            return (shifted, LogoPlate.None, ContrastFix.ShiftedColor);
        }

        return (fill, BetterPlate(sample), ContrastFix.AddedPlate);
    }

    /// <summary>Walks the folder away from the logo, one step at a time, in whichever direction helps first.</summary>
    private static FolderFill? TryShift(LogoSample sample, FolderFill fill)
    {
        for (var shift = LightnessStep; shift <= MaxLightnessShift + 1e-9; shift += LightnessStep)
        {
            foreach (var direction in new[] { -1.0, 1.0 })
            {
                var moved = Shifted(fill, shift * direction);

                if (sample.VisibleShare(IconPalette.MiddleOf(moved)) >= EnoughVisible)
                {
                    return moved;
                }
            }
        }

        return null;
    }

    private static FolderFill Shifted(FolderFill fill, double amount)
    {
        IconColor Move(IconColor color)
        {
            var (hue, saturation, lightness) = ColorMath.ToHsl(color);
            return ColorMath.FromHsl(hue, saturation, lightness + amount);
        }

        return fill with { Start = Move(fill.Start), End = fill.End is { } end ? Move(end) : null };
    }

    /// <summary>
    /// The plate the logo stands out on most. Average contrast decides, not the visible share,
    /// because a mid-tone logo is visible on both and would otherwise tie.
    /// </summary>
    private static LogoPlate BetterPlate(LogoSample sample)
    {
        var onLight = sample.Pixels.Average(pixel => ColorMath.Contrast(pixel, new IconColor(250, 250, 252)));
        var onDark = sample.Pixels.Average(pixel => ColorMath.Contrast(pixel, new IconColor(28, 32, 40)));

        return onLight >= onDark ? LogoPlate.Light : LogoPlate.Dark;
    }
}
