using static Foldcraft.Core.Icons.GlyphLayer;

namespace Foldcraft.Core.Icons;

/// <summary>The plate a badge's symbol sits on.</summary>
internal enum BadgePlate
{
    Disc,
    Triangle,
}

/// <summary>
/// A small mark for a corner of the icon. The tiny drawing is used when the plate has about
/// nine pixels to work with, which leaves room for one or two bold strokes at most.
/// </summary>
public sealed class Badge
{
    internal Badge(string id, string displayName, string color, GlyphLayer[] symbol, GlyphLayer[]? tiny = null, BadgePlate plate = BadgePlate.Disc)
    {
        Id = id;
        DisplayName = displayName;
        DefaultColor = IconColor.Parse(color);
        Symbol = symbol;
        TinySymbol = tiny ?? symbol;
        Plate = plate;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public IconColor DefaultColor { get; }

    internal IReadOnlyList<GlyphLayer> Symbol { get; }
    internal IReadOnlyList<GlyphLayer> TinySymbol { get; }
    internal BadgePlate Plate { get; }
}

/// <summary>
/// The built-in badges. Like the glyphs, every shape here is plain geometry written for
/// Foldcraft. A stored spec names a badge by id, so ids must not change.
/// </summary>
public static class BadgeCatalog
{
    private static readonly Badge[] Badges =
    [
        new("done", "Done", "#16A34A",
            [Stroke("M3.5 13 L9.5 19 L20.5 6", 4.4f)],
            [Stroke("M2 13 L9 20 L22 5", 6f)]),

        new("important", "Important", "#DC2626",
            [Stroke("M12 3.5 V13.5", 4.8f), Dot(12f, 20f, 3f)],
            [Stroke("M12 1.5 V12", 6f), Dot(12f, 20.5f, 3.6f)]),

        new("in-progress", "In Progress", "#2563EB",
            [Ring(12f, 12f, 9.5f, 3.4f), Fill("M12 5.5 C15.6 5.5 18.5 8.4 18.5 12 C18.5 15.6 15.6 18.5 12 18.5 Z")],
            [Fill("M12 0.5 C18.4 0.5 23.5 5.6 23.5 12 C23.5 18.4 18.4 23.5 12 23.5 Z"), Stroke("M12 3 C3 3 3 21 12 21", 3.6f)]),

        new("favorite", "Favorite", "#EAB308",
            [Fill("M12 0.5 L15.6 8.2 L24 9.2 L17.8 15 L19.4 23.4 L12 19.2 L4.6 23.4 L6.2 15 L0 9.2 L8.4 8.2 Z")]),

        new("archived", "Archived", "#64748B",
            [Fill("M1.5 3.5 H22.5 V10 H1.5 Z"), Fill("M3.5 12 H20.5 V21.5 H3.5 Z M8.5 14 V16.6 H15.5 V14 Z")],
            [Fill("M0.5 2.5 H23.5 V10 H0.5 Z"), Fill("M3 13 H21 V22 H3 Z")]),

        new("shared", "Shared", "#0284C7",
            [Stroke("M5 19 L18.5 5.5", 4.4f), Stroke("M8.5 4.5 H19.5 V15.5", 4.4f)],
            [Stroke("M3 21 L19 5", 6f), Stroke("M7 3.5 H20.5 V17", 6f)]),

        new("private", "Private", "#334155",
            [Stroke("M7.4 11 V8 C7.4 1.5 16.6 1.5 16.6 8 V11", 3.6f), Fill("M3.5 10.5 H20.5 V22.5 H3.5 Z")],
            [Stroke("M7 11 V7.5 C7 0 17 0 17 7.5 V11", 4.6f), Fill("M3 11 H21 V23 H3 Z")]),

        new("waiting", "Waiting", "#EA580C",
            [Ring(12f, 12f, 9.8f, 3.2f), Stroke("M12 6.5 V12.5 L16.2 15.2", 3.2f)],
            [Stroke("M12 2 V12.5 L20 17", 6f)]),

        new("new", "New", "#9333EA",
            [Fill("M12 0.5 L14.8 9.2 L23.5 12 L14.8 14.8 L12 23.5 L9.2 14.8 L0.5 12 L9.2 9.2 Z")],
            [Stroke("M12 1.5 V22.5 M1.5 12 H22.5", 6.4f)]),

        new("warning", "Warning", "#F59E0B",
            [Stroke("M12 5 V13.5", 4.4f), Dot(12f, 19.5f, 2.8f)],
            [Stroke("M12 3 V12.5", 6f), Dot(12f, 20f, 3.2f)],
            BadgePlate.Triangle),

        new("idea", "Idea", "#FACC15",
            [Fill("M12 0.5 C6.8 0.5 3.5 4.3 3.5 9 C3.5 12.5 5.5 14.5 7 16 V17.5 H17 V16 C18.5 14.5 20.5 12.5 20.5 9 C20.5 4.3 17.2 0.5 12 0.5 Z"), Fill("M8 19.5 H16 V23.5 H8 Z")]),

        new("review", "Review", "#0D9488",
            [Ring(9.6f, 9.6f, 6.8f, 3.8f), Stroke("M15 15 L21.5 21.5", 4.6f)],
            [Ring(9f, 9f, 6.4f, 5f), Stroke("M14 14 L22 22", 6f)]),
    ];

    /// <summary>All built-in badges in display order.</summary>
    public static IReadOnlyList<Badge> All => Badges;

    /// <summary>Finds a badge by id, ignoring case. Returns null if there is none.</summary>
    public static Badge? Find(string id) =>
        Array.Find(Badges, badge => badge.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
