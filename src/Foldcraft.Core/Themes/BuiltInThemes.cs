using Foldcraft.Core.Icons;

namespace Foldcraft.Core.Themes;

/// <summary>The themes that come with the app, grouped by mood.</summary>
public static class BuiltInThemes
{
    private const string Calm = "Calm";
    private const string Bright = "Bright";
    private const string Warm = "Warm";
    private const string Cool = "Cool";
    private const string Earthy = "Earthy";
    private const string Night = "Night";
    private const string Pastel = "Pastel";
    private const string Bold = "Bold";

    private static readonly FolderTheme[] Themes =
    [
        Make("still-water", "Still Water", Calm, Gradient("#7DD3FC", "#38BDF8"), FolderShape.Soft3D, "drop"),
        Make("sage", "Sage", Calm, Solid("#84A98C"), FolderShape.Flat, "leaf"),
        Make("mist", "Mist", Calm, Gradient("#CBD5E1", "#94A3B8", 135f), FolderShape.Classic, "cloud"),

        Make("sunny", "Sunny", Bright, Gradient("#FDE047", "#F59E0B"), FolderShape.Soft3D, "sun"),
        Make("citrus", "Citrus", Bright, Solid("#A3E635"), FolderShape.Flat, "bolt"),
        Make("pop", "Pop", Bright, Gradient("#F472B6", "#A855F7", 45f), FolderShape.Classic, "sparkle"),

        Make("ember", "Ember", Warm, Gradient("#F97316", "#DC2626"), FolderShape.Soft3D, "fire"),
        Make("terracotta", "Terracotta", Warm, Solid("#C2703D"), FolderShape.Classic, "home"),
        Make("honey", "Honey", Warm, Gradient("#FBBF24", "#B45309", 135f), FolderShape.Flat, "star"),

        Make("glacier", "Glacier", Cool, Gradient("#BAE6FD", "#3B82F6"), FolderShape.Soft3D, "snowflake"),
        Make("deep-sea", "Deep Sea", Cool, Gradient("#0E7490", "#1E3A8A", 135f), FolderShape.Classic, "fish"),
        Make("indigo-ink", "Indigo Ink", Cool, Solid("#4F46E5"), FolderShape.Outline, "pencil"),

        Make("forest-floor", "Forest Floor", Earthy, Gradient("#4D7C0F", "#365314"), FolderShape.Classic, "tree"),
        Make("clay", "Clay", Earthy, Solid("#A16207"), FolderShape.Flat, "mountain"),
        Make("driftwood", "Driftwood", Earthy, Gradient("#D6B370", "#92400E", 120f), FolderShape.Soft3D, "compass"),

        Make("midnight", "Midnight", Night, Gradient("#312E81", "#0F172A"), FolderShape.Soft3D, "moon"),
        Make("graphite", "Graphite", Night, Solid("#1F2937"), FolderShape.Flat, "terminal"),
        Make("aurora", "Aurora", Night, Gradient("#10B981", "#6D28D9", 60f), FolderShape.Classic, "star"),

        Make("blush", "Blush", Pastel, Gradient("#FBCFE8", "#F9A8D4"), FolderShape.Soft3D, "heart"),
        Make("mint", "Mint", Pastel, Solid("#A7F3D0"), FolderShape.Flat, "flower"),
        Make("lilac", "Lilac", Pastel, Gradient("#DDD6FE", "#C4B5FD", 135f), FolderShape.Outline, "bookmark"),

        Make("signal", "Signal", Bold, Solid("#DC2626"), FolderShape.Flat, "flag"),
        Make("electric", "Electric", Bold, Gradient("#2563EB", "#DB2777", 45f), FolderShape.Soft3D, "bolt"),
        Make("high-vis", "High Vis", Bold, Solid("#FACC15"), FolderShape.Classic, "target"),
    ];

    /// <summary>All built-in themes in display order.</summary>
    public static IReadOnlyList<FolderTheme> All => Themes;

    private static FolderFill Solid(string color) => FolderFill.Solid(IconColor.Parse(color));

    private static FolderFill Gradient(string start, string end, float angle = 90f) =>
        FolderFill.Gradient(IconColor.Parse(start), IconColor.Parse(end), angle);

    private static FolderTheme Make(string id, string name, string mood, FolderFill fill, FolderShape shape, string glyph) => new()
    {
        Id = "builtin:" + id,
        Name = name,
        Mood = mood,
        Spec = new FolderIconSpec { Fill = fill, Shape = shape, GlyphId = glyph },
        IsBuiltIn = true,
    };
}
