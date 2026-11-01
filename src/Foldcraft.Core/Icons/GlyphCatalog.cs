using static Foldcraft.Core.Icons.GlyphCategory;
using static Foldcraft.Core.Icons.GlyphLayer;

namespace Foldcraft.Core.Icons;

/// <summary>
/// The built-in glyphs. Every shape here was written for Foldcraft as plain geometry, so
/// the set carries no third-party artwork and ships under the project's MIT license. Add new
/// ones at the end of their group: a stored spec names a glyph by id, so ids must not change.
/// </summary>
public static class GlyphCatalog
{
    private static readonly Glyph[] Glyphs =
    [
        // Work
        new Glyph("work", "Work", Work,
            Stroke("M8.5 8 V4.6 H15.5 V8", 2f),
            Fill("M2 7.6 H22 V12.8 H14 V11.4 H10 V12.8 H2 Z"),
            Fill("M2 14.4 H10 V15.8 H14 V14.4 H22 V21 H2 Z"))
            .Small(Stroke("M8 8 V4 H16 V8", 3f), Fill("M1.5 7.5 H22.5 V21.5 H1.5 Z"))
            .Also("briefcase", "job", "office", "business"),

        new Glyph("document", "Document", Work,
            Fill("M5 2 H14.5 L19.5 7 V22 H5 Z M8 11 V12.8 H16.5 V11 Z M8 15.4 V17.2 H16.5 V15.4 Z"))
            .Small(Fill("M4 1 H15 L20 6 V23 H4 Z M7.5 10 V13 H16.5 V10 Z"))
            .Also("file", "paper", "page", "text"),

        new Glyph("calendar", "Calendar", Work,
            Fill("M3 5 H21 V22 H3 Z M5.2 10 V19.8 H18.8 V10 Z"),
            Stroke("M8 2.5 V6.5", 2.2f),
            Stroke("M16 2.5 V6.5", 2.2f),
            Fill("M7.5 12.2 H11 V15.6 H7.5 Z M13 12.2 H16.5 V15.6 H13 Z"))
            .Small(Fill("M2 3 H22 V22 H2 Z M6 10 V18.5 H18 V10 Z"))
            .Also("date", "schedule", "plan", "event"),

        new Glyph("clipboard", "Clipboard", Work,
            Fill("M5 4 H9 V6.5 H15 V4 H19 V22 H5 Z M8 10.5 V12.5 H16 V10.5 Z M8 15 V17 H16 V15 Z"),
            Fill("M9.6 2 H14.4 V5.5 H9.6 Z"))
            .Small(Fill("M4 3.5 H20 V23 H4 Z M7.5 10 V13 H16.5 V10 Z M7.5 15.5 V18.5 H16.5 V15.5 Z"), Fill("M8 1 H16 V5 H8 Z"))
            .Also("tasks", "checklist", "notes"),

        new Glyph("mail", "Mail", Work,
            Fill("M2 5 H22 L12 13 Z"),
            Fill("M2 7.4 L12 15.4 L22 7.4 V19 H2 Z"))
            .Small(Fill("M1 4 H23 L12 12.5 Z"), Fill("M1 7.5 L12 16 L23 7.5 V20 H1 Z"))
            .Also("email", "envelope", "letter", "message"),

        new Glyph("chart", "Chart", Work,
            Fill("M3 13 H8 V21 H3 Z M9.5 8 H14.5 V21 H9.5 Z M16 3 H21 V21 H16 Z"))
            .Small(Fill("M1.5 13 H7.5 V22 H1.5 Z M9 7.5 H15 V22 H9 Z M16.5 2 H22.5 V22 H16.5 Z"))
            .Also("graph", "bars", "stats", "report"),

        new Glyph("pin", "Pin", Work,
            Fill("M8.5 2.5 H15.5 V4.5 L14.5 5.5 V10.5 L18 14.5 V16 H13 V22 L12 23 L11 22 V16 H6 V14.5 L9.5 10.5 V5.5 L8.5 4.5 Z"))
            .Small(Fill("M7.5 1 H16.5 V4 L15 5.5 V10 L19 14.5 V17 H13.5 V23 H10.5 V17 H5 V14.5 L9 10 V5.5 L7.5 4 Z"))
            .Also("pushpin", "important", "keep"),

        new Glyph("box", "Box", Work,
            Fill("M2.5 4 H21.5 V9 H2.5 Z"),
            Fill("M4 10.6 H20 V21 H4 Z M9 13 V15 H15 V13 Z"))
            .Small(Fill("M1.5 3 H22.5 V9.5 H1.5 Z"), Fill("M3 12 H21 V22 H3 Z"))
            .Also("archive", "storage", "package", "old"),

        new Glyph("printer", "Printer", Work,
            Fill("M7 2.5 H17 V7.5 H7 Z"),
            Fill("M2.5 9 H21.5 V18 H17.5 V14.5 H6.5 V18 H2.5 Z"),
            Fill("M8 16 H16 V22 H8 Z"))
            .Small(Fill("M6 1.5 H18 V7 H6 Z"), Fill("M1.5 9 H22.5 V18 H1.5 Z"), Fill("M6.5 15 H17.5 V22.5 H6.5 Z"))
            .Also("print", "paper", "scan"),

        // Media
        new Glyph("music", "Music", Media,
            Stroke("M9 18 V5.6 L20 3.2 V15.6", 2.2f),
            Dot(6.4f, 18f, 3.2f),
            Dot(17.4f, 15.6f, 3.2f))
            .Small(Stroke("M9 18 V5 L20.5 2.5 V15.5", 3f), Dot(5.8f, 18.5f, 4f), Dot(17.2f, 16f, 4f))
            .Also("song", "audio", "note", "sound"),

        new Glyph("image", "Image", Media,
            Fill("M2.5 4 H21.5 V20 H2.5 Z M4.6 6.1 V17.9 H19.4 V6.1 Z"),
            Dot(8.6f, 9.6f, 1.9f),
            Fill("M5.6 17 L10.2 11.6 L13.4 15 L15.8 12.6 L18.4 17 Z"))
            .Small(Fill("M1.5 3 H22.5 V21 H1.5 Z M4.5 6 V18 H19.5 V6 Z"), Fill("M6 18 L11 11 L15 15.5 L17 13.5 L19.5 18 Z"))
            .Also("photo", "picture", "pictures", "gallery"),

        new Glyph("video", "Video", Media,
            Fill("M2 6 H15.6 V18 H2 Z"),
            Fill("M17 10.6 L22 7 V17 L17 13.4 Z"))
            .Also("movie", "camera", "clip", "recording"),

        new Glyph("camera", "Camera", Media,
            Fill("M8.5 4.5 H15.5 L17.5 7 H22 V20 H2 V7 H6.5 Z", (12f, 13f, 4.4f)),
            Dot(12f, 13f, 2.4f))
            .Small(Fill("M8 3 H16 L18 6 H23 V21 H1 V6 H6 Z", (12f, 13f, 4.5f)))
            .Also("photo", "photography", "shots"),

        new Glyph("headphones", "Headphones", Media,
            Stroke("M3.6 15 V12.5 C3.6 1.6 20.4 1.6 20.4 12.5 V15"),
            Fill("M2 13.5 H7 V21 H2 Z"),
            Fill("M17 13.5 H22 V21 H17 Z"))
            .Small(Stroke("M3.5 15 V12 C3.5 0.5 20.5 0.5 20.5 12 V15", 3f), Fill("M1 13 H7.5 V22 H1 Z"), Fill("M16.5 13 H23 V22 H16.5 Z"))
            .Also("audio", "music", "listen", "podcast"),

        new Glyph("microphone", "Microphone", Media,
            Fill("M8 3.5 C8 0.6 16 0.6 16 3.5 V11.5 C16 14.4 8 14.4 8 11.5 Z"),
            Stroke("M4.5 11 C4.5 20 19.5 20 19.5 11", 2.2f),
            Stroke("M12 17.5 V22", 2.2f),
            Stroke("M8 22 H16", 2.2f))
            .Small(Fill("M7.5 3.5 C7.5 0 16.5 0 16.5 3.5 V10.5 C16.5 14 7.5 14 7.5 10.5 Z"), Stroke("M3.5 10.5 C3.5 20.5 20.5 20.5 20.5 10.5", 3f), Stroke("M12 18 V23", 3f))
            .Also("voice", "recording", "podcast", "sing"),

        new Glyph("film", "Film", Media,
            Fill("M4 2.5 H20 V21.5 H4 Z M6 5 V8 H8.4 V5 Z M15.6 5 V8 H18 V5 Z M6 10.5 V13.5 H8.4 V10.5 Z M15.6 10.5 V13.5 H18 V10.5 Z M6 16 V19 H8.4 V16 Z M15.6 16 V19 H18 V16 Z"))
            .Small(Fill("M3 1 H21 V23 H3 Z M6 5 V9.5 H9 V5 Z M15 5 V9.5 H18 V5 Z M6 14.5 V19 H9 V14.5 Z M15 14.5 V19 H18 V14.5 Z"))
            .Also("movie", "cinema", "reel", "strip"),

        new Glyph("tv", "TV", Media,
            Fill("M2.5 6 H21.5 V19 H2.5 Z M4.6 8.1 V16.9 H19.4 V8.1 Z"),
            Stroke("M8 2.5 L12 6 L16 2.5", 2f),
            Stroke("M7 21.5 H17", 2.2f))
            .Small(Fill("M1.5 5 H22.5 V19.5 H1.5 Z M5 8.5 V16 H19 V8.5 Z"), Stroke("M7 22 H17", 3f))
            .Also("television", "show", "series", "screen"),

        new Glyph("play", "Play", Media,
            Fill("M6 3 L21 12 L6 21 Z"))
            .Also("start", "video", "stream"),

        // School
        new Glyph("school", "School", School,
            Fill("M12 3.5 L23.5 9.4 L12 15.3 L0.5 9.4 Z"),
            Fill("M5.5 14 V18 C8 20.6 16 20.6 18.5 18 V14 L12 17.3 Z"),
            Stroke("M21.6 10.4 V17", 1.8f))
            .Small(Fill("M12 2.5 L24 9 L12 15.5 L0 9 Z"), Fill("M5 15 V19.5 C8 22 16 22 19 19.5 V15 L12 18.8 Z"))
            .Also("graduation", "study", "class", "university"),

        new Glyph("book", "Book", School,
            Fill("M2 4.5 C5.6 3.6 9 4 11.2 6 V21 C9 19.2 5.6 18.8 2 19.6 Z"),
            Fill("M22 4.5 C18.4 3.6 15 4 12.8 6 V21 C15 19.2 18.4 18.8 22 19.6 Z"))
            .Small(Fill("M1 3.5 C5 2.5 8.5 3 10.8 5 V21.5 C8.5 19.5 5 19 1 20 Z"), Fill("M23 3.5 C19 2.5 15.5 3 13.2 5 V21.5 C15.5 19.5 19 19 23 20 Z"))
            .Also("read", "reading", "study", "library"),

        new Glyph("pencil", "Pencil", School,
            Fill("M17 2 L22 7 L19.4 9.6 L14.4 4.6 Z"),
            Fill("M13 6 L18 11 L7.8 21.2 L1.8 22.2 L2.8 16.2 Z"))
            .Small(Fill("M16.5 1 L23 7.5 L8 22.5 L1 23 L1.5 16 Z"))
            .Also("write", "draw", "edit", "notes"),

        new Glyph("ruler", "Ruler", School,
            Fill("M2 7 H22 V17 H2 Z M6 7 V11.5 H7.4 V7 Z M10 7 V13 H11.4 V7 Z M14 7 V11.5 H15.4 V7 Z M18 7 V13 H19.4 V7 Z"))
            .Small(Fill("M1 6 H23 V18 H1 Z M6 6 V11.5 H8.5 V6 Z M15.5 6 V11.5 H18 V6 Z"))
            .Also("measure", "math", "geometry"),

        new Glyph("flask", "Flask", School,
            Fill("M9 2.5 H15 V4.6 H14 V9.5 L20.4 19.4 C21.2 20.8 20.4 22 18.8 22 H5.2 C3.6 22 2.8 20.8 3.6 19.4 L10 9.5 V4.6 H9 Z M11.6 6 V10 L9 14 H15 L12.4 10 V6 Z"))
            .Small(Fill("M8 1 H16 V4 H14.5 V9 L22 20 C23 21.5 22 23 20 23 H4 C2 23 1 21.5 2 20 L9.5 9 V4 H8 Z"))
            .Also("science", "chemistry", "lab", "experiment"),

        new Glyph("atom", "Atom", School,
            Orbit(12f, 12f, 10.4f, 4f, 0f, 1.8f),
            Orbit(12f, 12f, 10.4f, 4f, 60f, 1.8f),
            Orbit(12f, 12f, 10.4f, 4f, 120f, 1.8f),
            Dot(12f, 12f, 2.2f))
            .Small(Orbit(12f, 12f, 11f, 5f, 45f, 3f), Orbit(12f, 12f, 11f, 5f, 135f, 3f))
            .Also("science", "physics", "research"),

        new Glyph("calculator", "Calculator", School,
            Fill("M4.5 2 H19.5 V22 H4.5 Z M7 4.5 V9 H17 V4.5 Z M7 11.5 V14 H9.5 V11.5 Z M10.75 11.5 V14 H13.25 V11.5 Z M14.5 11.5 V14 H17 V11.5 Z M7 16 V18.5 H9.5 V16 Z M10.75 16 V18.5 H13.25 V16 Z M14.5 16 V18.5 H17 V16 Z"))
            .Small(Fill("M3.5 1 H20.5 V23 H3.5 Z M7 4.5 V9.5 H17 V4.5 Z M7 13 V16 H10 V13 Z M14 13 V16 H17 V13 Z"))
            .Also("math", "numbers", "sums", "accounting"),

        new Glyph("backpack", "Backpack", School,
            Fill("M9 3.5 C9 1.5 15 1.5 15 3.5 V4.5 C18.2 5.2 20 7.6 20 10.5 V22 H4 V10.5 C4 7.6 5.8 5.2 9 4.5 Z M10.6 3.2 V4.3 H13.4 V3.2 Z M7 14 V19 H17 V14 Z"))
            .Small(Fill("M12 1 C16.5 3 20.5 6 20.5 11 V23 H3.5 V11 C3.5 6 7.5 3 12 1 Z M7 14 V19.5 H17 V14 Z"))
            .Also("bag", "school", "hiking", "pack"),

        new Glyph("globe", "Globe", School,
            Ring(12f, 12f, 9.6f, 2.2f),
            Stroke("M2.6 12 H21.4", 2f),
            Stroke("M12 2.4 C6.5 7 6.5 17 12 21.6", 2f),
            Stroke("M12 2.4 C17.5 7 17.5 17 12 21.6", 2f))
            .Small(Ring(12f, 12f, 10f, 3f), Stroke("M2 12 H22", 3f), Stroke("M12 2 V22", 3f))
            .Also("world", "earth", "languages", "geography", "web", "internet"),

        // Games
        new Glyph("game", "Game", Games,
            Fill(
                "M7 6.5 H17 C20.4 6.5 22.5 10 22.8 14.2 C23 17.2 21.4 18.6 19.8 18.6 C18.2 18.6 17 16.8 15.4 15.2 H8.6 C7 16.8 5.8 18.6 4.2 18.6 C2.6 18.6 1 17.2 1.2 14.2 C1.5 10 3.6 6.5 7 6.5 Z " +
                "M6.8 9 V10.6 H5.2 V12.4 H6.8 V14 H8.6 V12.4 H10.2 V10.6 H8.6 V9 Z",
                (16f, 10f, 1.4f), (18.4f, 12.8f, 1.4f)))
            .Small(Fill(
                "M6.5 5.5 H17.5 C21 5.5 23.5 10 23.8 14.5 C24 18 22.5 19.5 20.5 19.5 C18.5 19.5 17 17.5 15.5 16 H8.5 C7 17.5 5.5 19.5 3.5 19.5 C1.5 19.5 0 18 0.2 14.5 C0.5 10 3 5.5 6.5 5.5 Z",
                (7f, 11f, 2.4f), (17f, 11f, 2.4f)))
            .Also("gaming", "controller", "play", "gamepad"),

        new Glyph("dice", "Dice", Games,
            Fill("M6 3 H18 Q21 3 21 6 V18 Q21 21 18 21 H6 Q3 21 3 18 V6 Q3 3 6 3 Z",
                (8f, 8f, 1.9f), (16f, 8f, 1.9f), (12f, 12f, 1.9f), (8f, 16f, 1.9f), (16f, 16f, 1.9f)))
            .Small(Fill("M5 3 H19 Q21 3 21 5 V19 Q21 21 19 21 H5 Q3 21 3 19 V5 Q3 3 5 3 Z",
                (8f, 8f, 3f), (16f, 16f, 3f)))
            .Also("tabletop", "board", "roll", "luck"),

        new Glyph("trophy", "Trophy", Games,
            Fill("M6.5 2.5 H17.5 V9 C17.5 13 14.8 15 12 15 C9.2 15 6.5 13 6.5 9 Z"),
            Stroke("M6.5 4.6 H3 V7 C3 10 5 11.2 7 11.4", 2f),
            Stroke("M17.5 4.6 H21 V7 C21 10 19 11.2 17 11.4", 2f),
            Fill("M10.8 14.5 H13.2 V18.5 H10.8 Z"),
            Fill("M7 18.5 H17 V21.5 H7 Z"))
            .Small(Fill("M5.5 1.5 H18.5 V9 C18.5 13.5 15.5 16 12 16 C8.5 16 5.5 13.5 5.5 9 Z"), Fill("M10.5 15 H13.5 V19 H10.5 Z"), Fill("M6 19 H18 V23 H6 Z"))
            .Also("award", "win", "prize", "cup", "best"),

        new Glyph("puzzle", "Puzzle", Games,
            Fill("M3 8 H8.5 C7.5 6 7.8 3 10.5 3 C13.2 3 13.5 6 12.5 8 H18 V12.5 C20 11.5 22.5 11.8 22.5 14.5 C22.5 17.2 20 17.5 18 16.5 V21.5 H3 Z"))
            .Also("piece", "jigsaw", "plugin", "extension"),

        new Glyph("sword", "Sword", Games,
            Fill("M20.5 2 L22 3.5 L11 15.2 L8.8 13 Z"),
            Stroke("M5 10.5 L13.5 19"),
            Stroke("M8 16 L3.5 20.5", 3f))
            .Small(Fill("M19.5 1 L23 4.5 L11 16.5 L7.5 13 Z"), Stroke("M4 10 L14 20", 3f), Stroke("M8 16 L3 21", 3.2f))
            .Also("rpg", "fantasy", "adventure", "battle"),

        new Glyph("joystick", "Joystick", Games,
            Dot(12f, 6f, 4f),
            Fill("M11 9 H13 V17 H11 Z"),
            Fill("M3.5 16 H20.5 V21.5 H3.5 Z"))
            .Small(Dot(12f, 6f, 5f), Fill("M10.5 9 H13.5 V16 H10.5 Z"), Fill("M2.5 15.5 H21.5 V22.5 H2.5 Z"))
            .Also("arcade", "retro", "emulator"),

        new Glyph("crown", "Crown", Games,
            Fill("M2.5 7 L7.5 12 L12 4 L16.5 12 L21.5 7 L19.5 19.5 H4.5 Z"))
            .Small(Fill("M1 5 L7 11 L12 2.5 L17 11 L23 5 L20.5 21 H3.5 Z"))
            .Also("king", "queen", "royal", "best", "premium"),

        new Glyph("target", "Target", Games,
            Ring(12f, 12f, 9.6f),
            Ring(12f, 12f, 5f),
            Dot(12f, 12f, 1.9f))
            .Small(Ring(12f, 12f, 9.8f, 3f), Dot(12f, 12f, 3.6f))
            .Also("goal", "aim", "focus", "bullseye"),

        new Glyph("rook", "Chess Piece", Games,
            Fill("M5 3 H8 V6 H10.5 V3 H13.5 V6 H16 V3 H19 V9.5 L16.5 11.5 V17 H7.5 V11.5 L5 9.5 Z"),
            Fill("M5 18.5 H19 V22 H5 Z"))
            .Small(Fill("M4 1.5 H8 V5 H10 V1.5 H14 V5 H16 V1.5 H20 V10 L17 12.5 V17 H7 V12.5 L4 10 Z"), Fill("M4 19 H20 V23 H4 Z"))
            .Also("chess", "rook", "strategy", "board"),

        // Nature
        new Glyph("tree", "Tree", Nature,
            Fill("M12 1.5 L18.5 9.5 H15.8 L20.5 16 H13.6 V22 H10.4 V16 H3.5 L8.2 9.5 H5.5 Z"))
            .Also("forest", "pine", "garden", "outdoors"),

        new Glyph("leaf", "Leaf", Nature,
            Fill("M20.5 3 C21.5 11 19 20 9 20 C7.7 20 6.5 19.7 5.5 19.2 L3 22 L1.8 20.8 L4.4 18.2 C1.5 12 6 3.5 20.5 3 Z"))
            .Also("plant", "green", "eco", "garden"),

        new Glyph("flower", "Flower", Nature,
            Dot(12f, 5.8f, 3.6f),
            Dot(17.9f, 10.1f, 3.6f),
            Dot(15.6f, 17f, 3.6f),
            Dot(8.4f, 17f, 3.6f),
            Dot(6.1f, 10.1f, 3.6f),
            Dot(12f, 12f, 2.2f))
            .Small(Dot(12f, 5f, 4.6f), Dot(19f, 12f, 4.6f), Dot(12f, 19f, 4.6f), Dot(5f, 12f, 4.6f))
            .Also("bloom", "garden", "spring", "plant"),

        new Glyph("sun", "Sun", Nature,
            Dot(12f, 12f, 5f),
            Stroke("M12 1.5 V4 M12 20 V22.5 M1.5 12 H4 M20 12 H22.5 M4.6 4.6 L6.3 6.3 M17.7 17.7 L19.4 19.4 M4.6 19.4 L6.3 17.7 M17.7 6.3 L19.4 4.6", 2.2f))
            .Small(Dot(12f, 12f, 6f), Stroke("M12 0.5 V3 M12 21 V23.5 M0.5 12 H3 M21 12 H23.5", 3f))
            .Also("summer", "day", "bright", "weather", "light"),

        new Glyph("moon", "Moon", Nature,
            Fill("M20.5 15.5 C16 17 9 14.5 8.5 7 C8.4 5.4 8.8 3.8 9.5 2.5 C4.8 3.8 1.8 8 2.2 12.8 C2.7 18.5 7.5 22 12.4 21.8 C16.2 21.6 19 19 20.5 15.5 Z"))
            .Also("night", "dark", "sleep", "evening"),

        new Glyph("mountain", "Mountain", Nature,
            Fill("M1.5 21 L9 6 L12.6 13 L15.5 9 L22.5 21 Z"))
            .Small(Fill("M0.5 22 L9 4 L13 13 L16 8.5 L23.5 22 Z"))
            .Also("hiking", "landscape", "peak", "outdoors"),

        new Glyph("paw", "Paw", Nature,
            Dot(12.2f, 15.8f, 4.6f),
            Dot(5.2f, 11.2f, 2.2f),
            Dot(9f, 6.6f, 2.4f),
            Dot(15f, 6.6f, 2.4f),
            Dot(18.8f, 11.2f, 2.2f))
            .Small(Dot(12f, 16f, 5.5f), Dot(4.5f, 10f, 3f), Dot(12f, 5.5f, 3f), Dot(19.5f, 10f, 3f))
            .Also("pet", "pets", "dog", "cat", "animal"),

        new Glyph("drop", "Drop", Nature,
            Fill("M12 2 C12 2 4.5 10.5 4.5 15 C4.5 19.4 7.8 22 12 22 C16.2 22 19.5 19.4 19.5 15 C19.5 10.5 12 2 12 2 Z"))
            .Also("water", "rain", "liquid", "ocean"),

        new Glyph("fire", "Fire", Nature,
            Fill("M12.5 1.5 C13.5 5.5 19.5 9 19.5 15 C19.5 19.4 16 22.5 12 22.5 C8 22.5 4.5 19.4 4.5 15 C4.5 11.5 6.5 9 8.5 7.2 C8.6 9.2 9.4 10.6 10.6 11 C10.2 7.5 10.4 4 12.5 1.5 Z M12 20 C14 20 15.4 18.6 15.4 16.8 C15.4 14.8 13.4 13.6 12.4 11.6 C11.4 13.6 8.6 14.8 8.6 16.8 C8.6 18.6 10 20 12 20 Z"))
            .Small(Fill("M12.5 0.5 C14 5 20.5 9 20.5 15 C20.5 20 16.5 23.5 12 23.5 C7.5 23.5 3.5 20 3.5 15 C3.5 11 6 8.5 8 6.5 C8.5 8.5 9.5 10 10.5 10.5 C10 7 10.5 3.5 12.5 0.5 Z"))
            .Also("flame", "hot", "urgent", "campfire"),

        new Glyph("snowflake", "Snowflake", Nature,
            Stroke("M12 2 V22 M3.3 7 L20.7 17 M3.3 17 L20.7 7", 2.2f),
            Stroke("M9 3.5 L12 6 L15 3.5 M9 20.5 L12 18 L15 20.5", 1.8f))
            .Small(Stroke("M12 1 V23 M2.5 6.5 L21.5 17.5 M2.5 17.5 L21.5 6.5", 3f))
            .Also("winter", "cold", "ice", "snow", "frozen"),

        // Travel
        new Glyph("plane", "Plane", Travel,
            Fill("M21 2.5 C22 3.5 21.8 5 20.6 6.2 L16.6 10.2 L19 20 L17 22 L12.6 14.2 L8.4 18.4 L9 21.2 L7.6 22.6 L5.6 18.4 L1.4 16.4 L2.8 15 L5.6 15.6 L9.8 11.4 L2 7 L4 5 L13.8 7.4 L17.8 3.4 C19 2.2 20 1.5 21 2.5 Z"))
            .Also("flight", "trip", "holiday", "vacation", "airport"),

        new Glyph("car", "Car", Travel,
            Fill("M6.5 5 H17.5 L20.5 11 C21.6 11.4 22.5 12.2 22.5 13.5 V19 H19.5 V21 H16.5 V19 H7.5 V21 H4.5 V19 H1.5 V13.5 C1.5 12.2 2.4 11.4 3.5 11 Z M7.6 7 L5.8 11 H18.2 L16.4 7 Z",
                (6f, 15f, 1.7f), (18f, 15f, 1.7f)))
            .Small(Fill("M6 3.5 H18 L21 10.5 C22.5 11 23.5 12 23.5 13.5 V21.5 H18.5 V19 H5.5 V21.5 H0.5 V13.5 C0.5 12 1.5 11 3 10.5 Z M7.5 6.5 L6 10.5 H18 L16.5 6.5 Z"))
            .Also("drive", "road", "vehicle", "road trip"),

        new Glyph("place", "Place", Travel,
            Fill("M12 1.5 C7.4 1.5 4 5 4 9.5 C4 15 12 22.5 12 22.5 C12 22.5 20 15 20 9.5 C20 5 16.6 1.5 12 1.5 Z", (12f, 9.5f, 3f)))
            .Small(Fill("M12 0.5 C6.8 0.5 3 4.5 3 9.5 C3 15.5 12 23.5 12 23.5 C12 23.5 21 15.5 21 9.5 C21 4.5 17.2 0.5 12 0.5 Z", (12f, 9.5f, 3.8f)))
            .Also("location", "map pin", "address", "marker"),

        new Glyph("compass", "Compass", Travel,
            Ring(12f, 12f, 9.6f, 2.2f),
            Fill("M16.8 7.2 L13.6 13.6 L7.2 16.8 L10.4 10.4 Z"))
            .Small(Ring(12f, 12f, 10f, 3f), Fill("M18 6 L14 14 L6 18 L10 10 Z"))
            .Also("direction", "navigate", "explore", "north"),

        new Glyph("suitcase", "Suitcase", Travel,
            Stroke("M9 6 V3.4 H15 V6", 2f),
            Fill("M4 6.4 H20 V21 H4 Z M7.2 9 V18.4 H8.8 V9 Z M15.2 9 V18.4 H16.8 V9 Z"))
            .Small(Stroke("M8.5 7 V2.5 H15.5 V7", 3f), Fill("M3 6 H21 V22.5 H3 Z"))
            .Also("luggage", "trip", "travel", "packing"),

        new Glyph("tent", "Tent", Travel,
            Fill("M12 3 L22.5 21 H15.5 L12 14 L8.5 21 H1.5 Z"))
            .Small(Fill("M12 1.5 L24 22.5 H15.5 L12 15.5 L8.5 22.5 H0 Z"))
            .Also("camping", "outdoors", "festival"),

        new Glyph("boat", "Boat", Travel,
            Fill("M11 2.5 V14.5 H3.5 Z"),
            Fill("M13 5 L20.5 14.5 H13 Z"),
            Fill("M2 16.5 H22 L19 21.5 H5 Z"))
            .Small(Fill("M10.5 1 V14 H2.5 Z"), Fill("M13.5 4 L22 14 H13.5 Z"), Fill("M0.5 16 H23.5 L19.5 23 H4.5 Z"))
            .Also("sail", "sailing", "sea", "cruise", "ship"),

        new Glyph("train", "Train", Travel,
            Fill("M6 2.5 H18 Q20 2.5 20 4.5 V16 Q20 18 18 18 H6 Q4 18 4 16 V4.5 Q4 2.5 6 2.5 Z M6.4 5.5 V10.5 H17.6 V5.5 Z", (8f, 14f, 1.5f), (16f, 14f, 1.5f)),
            Stroke("M8 18.5 L5.5 22", 2.2f),
            Stroke("M16 18.5 L18.5 22", 2.2f))
            .Small(Fill("M5 1 H19 Q21.5 1 21.5 3.5 V16 Q21.5 18.5 19 18.5 H5 Q2.5 18.5 2.5 16 V3.5 Q2.5 1 5 1 Z M6 5 V10.5 H18 V5 Z"), Stroke("M7.5 19 L5 23", 3f), Stroke("M16.5 19 L19 23", 3f))
            .Also("rail", "subway", "commute", "metro"),

        new Glyph("ticket", "Ticket", Travel,
            Fill("M2 6 H22 V10 C20.4 10 19.4 11 19.4 12 C19.4 13 20.4 14 22 14 V18 H2 V14 C3.6 14 4.6 13 4.6 12 C4.6 11 3.6 10 2 10 Z"))
            .Also("event", "pass", "booking", "concert"),

        new Glyph("map", "Map", Travel,
            Fill("M2 5 L8.5 3 V19 L2 21 Z"),
            Fill("M10 3 L14 5 V21 L10 19 Z"),
            Fill("M15.5 5 L22 3 V19 L15.5 21 Z"))
            .Small(Fill("M1 4.5 L7.5 2 V19.5 L1 22 Z"), Fill("M9.5 2 L14.5 4.5 V22 L9.5 19.5 Z"), Fill("M16.5 4.5 L23 2 V19.5 L16.5 22 Z"))
            .Also("atlas", "route", "guide", "plan"),

        // Food
        new Glyph("coffee", "Coffee", Food,
            Fill("M3 8 H18 V15 C18 18.5 15.5 21 12.2 21 H8.8 C5.5 21 3 18.5 3 15 Z"),
            Stroke("M18 10 H19.4 C22 10 22 15.4 19.4 15.4 H17.6", 2f),
            Stroke("M8 2.5 C6.8 3.8 9 4.6 8 6", 1.8f),
            Stroke("M13 2.5 C11.8 3.8 14 4.6 13 6", 1.8f))
            .Small(Fill("M2 5 H18.5 V15 C18.5 19.5 15.5 22.5 11.5 22.5 H9 C5 22.5 2 19.5 2 15 Z"), Stroke("M18.5 8 H20 C23.5 8 23.5 15.5 20 15.5 H18", 3f))
            .Also("tea", "drink", "cafe", "cup", "mug"),

        new Glyph("apple", "Apple", Food,
            Fill("M12 7.5 C14 5.8 20.5 5.5 20.8 12.5 C21 17.2 17.4 22 15.2 22 C13.8 22 13.2 21.2 12 21.2 C10.8 21.2 10.2 22 8.8 22 C6.6 22 3 17.2 3.2 12.5 C3.5 5.5 10 5.8 12 7.5 Z"),
            Stroke("M12 7 C12 4.6 13.2 2.8 15.6 2", 2f))
            .Small(Fill("M12 7 C14 5 21.5 4.5 22 12 C22.3 18 18 23.5 15.5 23.5 C14 23.5 13.2 22.5 12 22.5 C10.8 22.5 10 23.5 8.5 23.5 C6 23.5 1.7 18 2 12 C2.5 4.5 10 5 12 7 Z"), Stroke("M12 6.5 C12 4 13 2 15.5 1", 3f))
            .Also("fruit", "healthy", "food", "teacher"),

        new Glyph("pizza", "Pizza", Food,
            Fill("M12 22.5 L2 6 C8 2 16 2 22 6 Z", (9f, 9f, 1.7f), (15f, 9.5f, 1.7f), (12f, 15f, 1.7f)))
            .Small(Fill("M12 24 L1 5.5 C7.5 1 16.5 1 23 5.5 Z", (9f, 9f, 2.4f), (15f, 12f, 2.4f)))
            .Also("slice", "food", "takeout", "dinner"),

        new Glyph("cutlery", "Fork and Knife", Food,
            Stroke("M6 2.5 V8 C6 11 10 11 10 8 V2.5", 2f),
            Stroke("M8 2.5 V22"),
            Fill("M15 2.5 C19 3 20 8 20 13 H17.4 V22 H15 Z"))
            .Small(Stroke("M3.5 2 V8 C3.5 12.5 10.5 12.5 10.5 8 V2", 3f), Stroke("M7 11 V23", 3f), Fill("M14.5 1 C19.5 1.5 21 7 21 13.5 H17.5 V23 H14.5 Z"))
            .Also("food", "recipes", "restaurant", "cooking", "meal"),

        new Glyph("cake", "Cake", Food,
            Stroke("M12 3 V7", 2f),
            Fill("M4 8.6 H20 V13 C18 14.6 16.5 14.6 14.7 13.2 C13 14.6 11 14.6 9.3 13.2 C7.5 14.6 6 14.6 4 13 Z"),
            Fill("M4 15 C6 16.2 7.5 16.2 9.3 15 C11 16.2 13 16.2 14.7 15 C16.5 16.2 18 16.2 20 15 V21.5 H4 Z"))
            .Small(Stroke("M12 1 V6.5", 3f), Fill("M2.5 7 H21.5 V12.5 H2.5 Z"), Fill("M2.5 15.5 H21.5 V23 H2.5 Z"))
            .Also("birthday", "party", "dessert", "celebrate"),

        new Glyph("carrot", "Carrot", Food,
            Fill("M14.5 7 C12 5.8 8.8 7 7.2 9.4 L2 22 L14.6 16.8 C17 15.2 18.2 12 17 9.5 Z"),
            Stroke("M16 8 L21 3", 2f),
            Stroke("M17.2 6 C18.4 3.6 17 2.4 15.4 2.4", 1.8f),
            Stroke("M18 6.8 C20.4 5.6 21.6 7 21.6 8.6", 1.8f))
            .Small(Fill("M15 6 C12 4.5 8 6 6.5 9 L1 23 L15 17.5 C18 16 19.5 12 18 9 Z"), Stroke("M16 8 L22 2", 3f))
            .Also("vegetable", "garden", "healthy", "diet"),

        new Glyph("fish", "Fish", Food,
            Fill("M2 12 C5 6.5 12 5 17 9.5 L22.5 5.5 V18.5 L17 14.5 C12 19 5 17.5 2 12 Z", (8f, 10.8f, 1.4f)))
            .Small(Fill("M0.5 12 C4 5 12 3.5 17 8.5 L23.5 4 V20 L17 15.5 C12 20.5 4 19 0.5 12 Z"))
            .Also("seafood", "aquarium", "sea", "fishing"),

        new Glyph("wine", "Wine Glass", Food,
            Fill("M6 2.5 H18 V8 C18 11.5 15.4 14 12 14 C8.6 14 6 11.5 6 8 Z"),
            Fill("M11 13.5 H13 V20 H11 Z"),
            Fill("M7 20 H17 V22 H7 Z"))
            .Small(Fill("M5 1 H19 V8 C19 12.5 16 15 12 15 C8 15 5 12.5 5 8 Z"), Fill("M10.5 14 H13.5 V20 H10.5 Z"), Fill("M6 19.5 H18 V23 H6 Z"))
            .Also("drink", "bar", "party", "toast"),

        // Tech
        new Glyph("code", "Code", Tech,
            Stroke("M8 6 L2.5 12 L8 18"),
            Stroke("M16 6 L21.5 12 L16 18"),
            Stroke("M13.6 4 L10.4 20", 2.2f))
            .Small(Stroke("M9 5 L2 12 L9 19", 3.2f), Stroke("M15 5 L22 12 L15 19", 3.2f))
            .Also("programming", "developer", "software", "project"),

        new Glyph("cloud", "Cloud", Tech,
            Fill("M6.6 19.5 C3.4 19.5 1 17.2 1 14.4 C1 11.8 2.9 9.8 5.4 9.4 C6.2 6.2 8.9 4 12.2 4 C16 4 18.9 6.7 19.3 10.2 C21.5 10.6 23 12.5 23 14.8 C23 17.4 20.9 19.5 18.2 19.5 Z"))
            .Also("online", "backup", "sync", "weather"),

        new Glyph("download", "Download", Tech,
            Stroke("M12 2.5 V15"),
            Stroke("M5.8 9.4 L12 15.6 L18.2 9.4"),
            Stroke("M3.5 21 H20.5"))
            .Small(Stroke("M12 1 V15", 3.2f), Stroke("M5 8.5 L12 15.5 L19 8.5", 3.2f), Stroke("M3 22 H21", 3.2f))
            .Also("downloads", "save", "arrow", "incoming"),

        new Glyph("tools", "Tools", Tech,
            Fill("M21.8 6.2 L17.7 10.3 L13.8 6.4 L17.9 2.3 C14.9 1.3 11.4 2.5 9.9 5.6 C9.2 7.1 9.1 8.8 9.7 10.4 L2.6 17.5 C1.4 18.7 1.4 20.6 2.6 21.6 C3.7 22.7 5.4 22.7 6.6 21.5 L13.7 14.4 C15.3 15 17 14.9 18.5 14.2 C21.6 12.7 22.8 9.2 21.8 6.2 Z"))
            .Also("wrench", "settings", "repair", "fix", "utilities"),

        new Glyph("terminal", "Terminal", Tech,
            Fill("M2 4 H22 V20 H2 Z M4.2 6.2 V17.8 H19.8 V6.2 Z"),
            Stroke("M7 9 L10.4 12 L7 15", 2f),
            Stroke("M12.4 15.4 H17", 2f))
            .Small(Fill("M1 3 H23 V21 H1 Z M4 6 V18 H20 V6 Z"), Stroke("M7 8.5 L11 12 L7 15.5", 3f))
            .Also("console", "command line", "shell", "scripts"),

        new Glyph("chip", "Chip", Tech,
            Fill("M7 7 H17 V17 H7 Z"),
            Stroke("M9.5 2.5 V6.5 M14.5 2.5 V6.5 M9.5 17.5 V21.5 M14.5 17.5 V21.5 M2.5 9.5 H6.5 M2.5 14.5 H6.5 M17.5 9.5 H21.5 M17.5 14.5 H21.5", 2f))
            .Small(Fill("M6 6 H18 V18 H6 Z"), Stroke("M12 1 V5 M12 19 V23 M1 12 H5 M19 12 H23", 3f))
            .Also("processor", "hardware", "electronics", "cpu"),

        new Glyph("phone", "Phone", Tech,
            Fill("M7 1.5 H17 Q19 1.5 19 3.5 V20.5 Q19 22.5 17 22.5 H7 Q5 22.5 5 20.5 V3.5 Q5 1.5 7 1.5 Z M7.2 4.5 V18.5 H16.8 V4.5 Z", (12f, 20.4f, 1.1f)))
            .Small(Fill("M6.5 0.5 H17.5 Q20 0.5 20 3 V21 Q20 23.5 17.5 23.5 H6.5 Q4 23.5 4 21 V3 Q4 0.5 6.5 0.5 Z M7 4 V19 H17 V4 Z"))
            .Also("mobile", "smartphone", "app", "cell"),

        new Glyph("laptop", "Laptop", Tech,
            Fill("M4.5 4 H19.5 Q20.5 4 20.5 5 V15.5 H3.5 V5 Q3.5 4 4.5 4 Z M5.8 6.2 V13.4 H18.2 V6.2 Z"),
            Fill("M1 17 H23 L21.5 20 H2.5 Z"))
            .Small(Fill("M3 3 H21 V16 H3 Z M6 6 V13 H18 V6 Z"), Fill("M0 17.5 H24 L22 21.5 H2 Z"))
            .Also("computer", "notebook", "pc", "device"),

        new Glyph("wifi", "Wireless", Tech,
            Stroke("M2 9.5 C7.8 4 16.2 4 22 9.5", 2.6f),
            Stroke("M5.8 13.6 C9.4 10.2 14.6 10.2 18.2 13.6", 2.6f),
            Stroke("M9.4 17.2 C11 15.8 13 15.8 14.6 17.2", 2.6f),
            Dot(12f, 20.6f, 1.8f))
            .Small(Stroke("M1.5 9 C7.5 2.5 16.5 2.5 22.5 9", 3.2f), Stroke("M6 14.5 C9.5 11 14.5 11 18 14.5", 3.2f), Dot(12f, 20f, 3f))
            .Also("network", "internet", "signal", "connection"),

        new Glyph("database", "Database", Tech,
            Fill("M12 2 C17.5 2 21 3.6 21 5.6 C21 7.6 17.5 9.2 12 9.2 C6.5 9.2 3 7.6 3 5.6 C3 3.6 6.5 2 12 2 Z"),
            Fill("M3 8.4 C4.8 10.2 8 11 12 11 C16 11 19.2 10.2 21 8.4 V12 C21 14 17.5 15.4 12 15.4 C6.5 15.4 3 14 3 12 Z"),
            Fill("M3 14.8 C4.8 16.4 8 17.2 12 17.2 C16 17.2 19.2 16.4 21 14.8 V18.4 C21 20.4 17.5 22 12 22 C6.5 22 3 20.4 3 18.4 Z"))
            .Small(Fill("M3 1.5 H21 V6.5 H3 Z"), Fill("M3 9.5 H21 V14.5 H3 Z"), Fill("M3 17.5 H21 V22.5 H3 Z"))
            .Also("data", "storage", "sql", "server"),

        new Glyph("bug", "Bug", Tech,
            Fill("M12 6 C15.6 6 17.6 9 17.6 13.5 C17.6 18.5 15 21.5 12 21.5 C9 21.5 6.4 18.5 6.4 13.5 C6.4 9 8.4 6 12 6 Z"),
            Stroke("M8.5 7.5 L6 3.5 M15.5 7.5 L18 3.5 M6 11.5 L2 9.5 M18 11.5 L22 9.5 M6 15.5 H1.8 M18 15.5 H22.2 M7 19.5 L3 22 M17 19.5 L21 22", 2f))
            .Small(Fill("M12 4 C16.5 4 18.5 8 18.5 13 C18.5 19 15.5 23 12 23 C8.5 23 5.5 19 5.5 13 C5.5 8 7.5 4 12 4 Z"), Stroke("M6 10 L1 7 M18 10 L23 7 M6 16 H1 M18 16 H23", 3f))
            .Also("issue", "insect", "debug", "testing"),

        new Glyph("gear", "Gear", Tech,
            Gear(12f, 12f, 8, 11f, 8f, 3.4f))
            .Small(Gear(12f, 12f, 6, 12f, 8.6f, 4f))
            .Also("settings", "config", "preferences", "cog"),

        new Glyph("battery", "Battery", Tech,
            Fill("M2 7 H20 V17 H2 Z M4 9 V15 H18 V9 Z"),
            Fill("M5.5 10.4 H12 V13.6 H5.5 Z"),
            Fill("M21 10 H23 V14 H21 Z"))
            .Small(Fill("M0.5 5.5 H20 V18.5 H0.5 Z M3.5 8.5 V15.5 H17 V8.5 Z"), Fill("M21 9.5 H24 V14.5 H21 Z"))
            .Also("power", "energy", "charge", "charging"),

        // Finance
        new Glyph("money", "Money", Finance,
            Fill("M1.5 5.5 H22.5 V18.5 H1.5 Z", (12f, 12f, 3.6f), (5.6f, 12f, 1.2f), (18.4f, 12f, 1.2f)))
            .Small(Fill("M0.5 4.5 H23.5 V19.5 H0.5 Z", (12f, 12f, 4.4f)))
            .Also("cash", "bill", "budget", "salary", "bank note"),

        new Glyph("coin", "Coin", Finance,
            Ring(12f, 12f, 9.8f),
            Stroke("M15.6 9 C15 7.6 13.6 7 12 7 C10 7 8.6 8 8.6 9.6 C8.6 13 15.6 11 15.6 14.6 C15.6 16.2 14 17 12 17 C10.2 17 8.8 16.2 8.2 15 M12 5 V19", 1.8f))
            .Small(Ring(12f, 12f, 10f, 3f), Stroke("M12 7 V17", 3.2f))
            .Also("money", "dollar", "currency", "savings", "price"),

        new Glyph("wallet", "Wallet", Finance,
            Fill("M2.5 6 C2.5 4.6 3.6 4 5 4 H19 V7 H2.5 Z"),
            Fill("M2.5 8.6 H21.5 V12 H16 C14 12 13 13.2 13 14.5 C13 15.8 14 17 16 17 H21.5 V20.5 H2.5 Z"),
            Fill("M16 13.4 H22.5 V15.6 H16 Z"))
            .Small(Fill("M1 3 H20 V7 H1 Z"), Fill("M1 9 H23 V12 H15.5 V17 H23 V22 H1 Z"))
            .Also("money", "purse", "spending", "pay"),

        new Glyph("card", "Credit Card", Finance,
            Fill("M1.5 5 H22.5 V19 H1.5 Z M1.5 8 V11 H22.5 V8 Z M4.5 14 V16 H10 V14 Z"))
            .Small(Fill("M0.5 4 H23.5 V20 H0.5 Z M0.5 8 V11.5 H23.5 V8 Z"))
            .Also("payment", "bank", "purchase", "subscriptions"),

        new Glyph("trend", "Trend", Finance,
            Stroke("M2.5 18 L9 11 L13.5 15 L21 6.5", 2.6f),
            Stroke("M15.5 6 H21.5 V12", 2.6f))
            .Small(Stroke("M1.5 19 L9 11 L14 16 L22 6", 3.2f), Stroke("M15 5 H22.5 V12.5", 3.2f))
            .Also("growth", "stocks", "investing", "profit", "up"),

        new Glyph("bank", "Bank", Finance,
            Fill("M12 1.5 L23 7.5 V9.5 H1 V7.5 Z"),
            Fill("M3.5 11 H6.5 V18 H3.5 Z M10.5 11 H13.5 V18 H10.5 Z M17.5 11 H20.5 V18 H17.5 Z"),
            Fill("M1.5 19.5 H22.5 V22.5 H1.5 Z"))
            .Small(Fill("M12 0.5 L24 7.5 V10 H0 V7.5 Z"), Fill("M3 12 H7 V18.5 H3 Z M10 12 H14 V18.5 H10 Z M17 12 H21 V18.5 H17 Z"), Fill("M0.5 20 H23.5 V23.5 H0.5 Z"))
            .Also("finance", "account", "institution", "loan"),

        new Glyph("receipt", "Receipt", Finance,
            Fill("M4 2 H20 V22 L17.3 20 L14.7 22 L12 20 L9.3 22 L6.7 20 L4 22 Z M7.5 6.5 V8.5 H16.5 V6.5 Z M7.5 11 V13 H16.5 V11 Z"))
            .Small(Fill("M3 1 H21 V23.5 L12 19.5 L3 23.5 Z M7 5.5 V9 H17 V5.5 Z"))
            .Also("invoice", "expenses", "bills", "tax"),

        new Glyph("piggy", "Piggy Bank", Finance,
            Fill("M4 11 C4 7 8 5 12 5 C13.8 5 15.5 5.4 16.8 6.2 L19.5 4.5 L19.4 8.2 C20.4 9.2 21 10.2 21.3 11.4 H23 V15.5 H20.8 C20.2 16.8 19.2 17.8 18 18.6 V21.5 H14.5 V19.8 H9.5 V21.5 H6 V18.6 C4.8 17.5 4 15 4 11 Z", (16.6f, 10.6f, 1.2f)))
            .Small(Fill("M2.5 11 C2.5 6.5 7 3.5 12 3.5 C14 3.5 15.8 4 17.2 4.8 L20.5 2.5 V8 C21.5 9 22.2 10.2 22.5 11.5 H24 V16 H22 C21.2 17.5 20 19 19 19.5 V23 H14.5 V21 H9.5 V23 H5 V19.5 C3.5 18 2.5 15 2.5 11 Z"))
            .Also("savings", "save", "money", "budget"),

        new Glyph("percent", "Percent", Finance,
            Stroke("M19 5 L5 19", 2.6f),
            Ring(7f, 7f, 3f, 2.2f),
            Ring(17f, 17f, 3f, 2.2f))
            .Small(Stroke("M20 4 L4 20", 3.2f), Dot(6f, 6f, 4.2f), Dot(18f, 18f, 4.2f))
            .Also("tax", "discount", "sale", "rate"),

        // Symbols
        new Glyph("star", "Star", Symbols,
            Fill("M12 2 L15 8.6 L22 9.4 L16.8 14.2 L18.2 21.2 L12 17.7 L5.8 21.2 L7.2 14.2 L2 9.4 L9 8.6 Z"))
            .Also("favorite", "best", "top", "rating"),

        new Glyph("heart", "Heart", Symbols,
            Fill("M12 21 C5 16 2 12.4 2 8.6 C2 5.4 4.4 3.2 7.2 3.2 C9.2 3.2 11 4.3 12 6 C13 4.3 14.8 3.2 16.8 3.2 C19.6 3.2 22 5.4 22 8.6 C22 12.4 19 16 12 21 Z"))
            .Also("love", "favorite", "family", "like"),

        new Glyph("flag", "Flag", Symbols,
            Stroke("M4.8 2.5 V22"),
            Fill("M6 3.2 H21.5 L17.4 8.8 L21.5 14.4 H6 Z"))
            .Small(Stroke("M4 1 V23", 3f), Fill("M4 2 H23 L18.5 8.5 L23 15 H4 Z"))
            .Also("mark", "priority", "report", "country"),

        new Glyph("lock", "Lock", Symbols,
            Stroke("M7.6 11 V7.4 C7.6 1.8 16.4 1.8 16.4 7.4 V11"),
            Fill("M4 10.4 H20 V22 H4 Z", (12f, 16.2f, 2.2f)))
            .Small(Stroke("M7 11 V7 C7 0.5 17 0.5 17 7 V11", 3f), Fill("M3 10 H21 V23 H3 Z"))
            .Also("private", "secure", "password", "secret"),

        new Glyph("check", "Check", Symbols,
            Stroke("M3.5 12.5 L9.5 18.5 L20.5 6", 3.4f))
            .Also("done", "complete", "finished", "ok", "tick"),

        new Glyph("cross", "Cross", Symbols,
            Stroke("M5 5 L19 19 M19 5 L5 19", 3.4f))
            .Also("close", "cancel", "no", "delete", "x"),

        new Glyph("bolt", "Bolt", Symbols,
            Fill("M13.5 1.5 L4 13.5 H11 L9.5 22.5 L20 9.5 H12.8 Z"))
            .Also("lightning", "fast", "power", "energy", "electric"),

        new Glyph("key", "Key", Symbols,
            Ring(7.6f, 8f, 4.4f, 3f),
            Stroke("M11 11.4 L21 21.4", 3f),
            Stroke("M16 16.4 L18.6 13.8", 2.6f),
            Stroke("M19 19.4 L21.6 16.8", 2.6f))
            .Small(Ring(7.5f, 7.5f, 5f, 3.4f), Stroke("M11.5 11.5 L22 22", 3.4f), Stroke("M17.5 17.5 L21 14", 3f))
            .Also("access", "password", "license", "secret"),

        new Glyph("bell", "Bell", Symbols,
            Fill("M12 2 C8 2 5.5 5 5.5 9 V14 L3 18 H21 L18.5 14 V9 C18.5 5 16 2 12 2 Z"),
            Fill("M9.4 19.6 H14.6 C14.4 21.2 13.4 22 12 22 C10.6 22 9.6 21.2 9.4 19.6 Z"))
            .Small(Fill("M12 0.5 C7.5 0.5 4.5 4 4.5 8.5 V13.5 L1.5 18.5 H22.5 L19.5 13.5 V8.5 C19.5 4 16.5 0.5 12 0.5 Z"), Dot(12f, 21.5f, 2.2f))
            .Also("alert", "notification", "reminder", "alarm"),

        new Glyph("shield", "Shield", Symbols,
            Fill("M12 1.5 L21 5 V11.5 C21 17 17 21 12 22.5 C7 21 3 17 3 11.5 V5 Z"))
            .Also("security", "safe", "protect", "backup"),

        new Glyph("question", "Question Mark", Symbols,
            Stroke("M6.5 8 C6.5 1.5 17.5 1.5 17.5 8 C17.5 12 12 12.6 12 16.5", 3f),
            Dot(12f, 21f, 1.9f))
            .Small(Stroke("M6 8 C6 0.5 18 0.5 18 8 C18 12 12 12.6 12 16", 3.4f), Dot(12f, 21.5f, 2.2f))
            .Also("help", "unknown", "faq", "support"),

        new Glyph("eye", "Eye", Symbols,
            Fill("M1 12 C5 5 19 5 23 12 C19 19 5 19 1 12 Z", (12f, 12f, 4f)),
            Dot(12f, 12f, 2f))
            .Small(Fill("M0 12 C4.5 3.5 19.5 3.5 24 12 C19.5 20.5 4.5 20.5 0 12 Z", (12f, 12f, 4.4f)))
            .Also("view", "watch", "visible", "review"),

        new Glyph("bookmark", "Bookmark", Symbols,
            Fill("M6 2 H18 V22 L12 17 L6 22 Z"))
            .Also("saved", "later", "reading", "keep"),

        new Glyph("home", "Home", Symbols,
            Fill("M12 2.5 L23 12 H19.5 V21.5 H14.5 V14.5 H9.5 V21.5 H4.5 V12 H1 Z"))
            .Small(Fill("M12 1 L24 12 H20.5 V23 H14.5 V15 H9.5 V23 H3.5 V12 H0 Z"))
            .Also("house", "family", "personal", "main"),

        new Glyph("tag", "Tag", Symbols,
            Fill("M2 3 H12 L22 13 L13 22 L2 11 Z", (7f, 8f, 1.8f)))
            .Small(Fill("M1 2 H12.5 L23 12.5 L12.5 23 L1 11.5 Z", (7f, 7.5f, 2.6f)))
            .Also("label", "price", "category", "sale"),

        new Glyph("user", "Person", Symbols,
            Dot(12f, 7f, 4.8f),
            Fill("M3 22 C3 16 7 13.4 12 13.4 C17 13.4 21 16 21 22 Z"))
            .Small(Dot(12f, 6.5f, 5.5f), Fill("M2 23.5 C2 17 6.5 14.5 12 14.5 C17.5 14.5 22 17 22 23.5 Z"))
            .Also("profile", "account", "me", "people", "contact"),

        new Glyph("sparkle", "Sparkle", Symbols,
            Fill("M12 1.5 L14.4 9.6 L22.5 12 L14.4 14.4 L12 22.5 L9.6 14.4 L1.5 12 L9.6 9.6 Z"))
            .Also("new", "magic", "shine", "special"),

        new Glyph("idea", "Light Bulb", Symbols,
            Fill("M12 1.5 C7.6 1.5 4.5 4.8 4.5 9 C4.5 12 6.2 13.8 7.5 15.2 C8.4 16.2 8.8 17 8.8 17.6 H15.2 C15.2 17 15.6 16.2 16.5 15.2 C17.8 13.8 19.5 12 19.5 9 C19.5 4.8 16.4 1.5 12 1.5 Z"),
            Fill("M9 19 H15 V20.4 H9 Z"),
            Fill("M10 21.2 H14 V22.6 H10 Z"))
            .Small(Fill("M12 0.5 C7 0.5 3.5 4.3 3.5 9 C3.5 12.5 5.5 14.5 7 16 V17.5 H17 V16 C18.5 14.5 20.5 12.5 20.5 9 C20.5 4.3 17 0.5 12 0.5 Z"), Fill("M8 19.5 H16 V23.5 H8 Z"))
            .Also("bulb", "lamp", "thoughts", "inspiration"),
    ];

    private static readonly IReadOnlyList<GlyphCategory> CategoryList = Enum.GetValues<GlyphCategory>();

    /// <summary>All built-in glyphs in display order.</summary>
    public static IReadOnlyList<Glyph> All => Glyphs;

    /// <summary>Every category, in display order.</summary>
    public static IReadOnlyList<GlyphCategory> Categories => CategoryList;

    /// <summary>Finds a glyph by id, ignoring case. Returns null if there is none.</summary>
    public static Glyph? Find(string id) =>
        Array.Find(Glyphs, glyph => glyph.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The glyphs matching some words and a category, either of which can be left out.</summary>
    public static IReadOnlyList<Glyph> Search(string? text, GlyphCategory? category = null)
    {
        var words = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Glyphs
            .Where(glyph => category is null || glyph.Category == category)
            .Where(glyph => words.All(glyph.Matches))
            .ToArray();
    }
}
