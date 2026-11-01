using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Foldcraft.Core.Icons;

/// <summary>The groups the glyph picker filters by.</summary>
public enum GlyphCategory
{
    Work,
    Media,
    School,
    Games,
    Nature,
    Travel,
    Food,
    Tech,
    Finance,
    Symbols,
}

/// <summary>
/// A built-in folder glyph. The small variant is what's drawn at 16 and 20 px, where the glyph
/// has only eight to ten pixels and the detailed form would fill in.
/// </summary>
public sealed class Glyph
{
    internal Glyph(string id, string displayName, GlyphCategory category, params GlyphLayer[] layers)
    {
        Id = id;
        DisplayName = displayName;
        Category = category;
        Layers = layers;
        SmallLayers = layers;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public GlyphCategory Category { get; }

    /// <summary>Other words someone might search for.</summary>
    public IReadOnlyList<string> Keywords { get; private init; } = [];

    /// <summary>True when the smallest sizes have their own drawing.</summary>
    public bool HasSmallVariant => !ReferenceEquals(Layers, SmallLayers);

    internal IReadOnlyList<GlyphLayer> Layers { get; }
    internal IReadOnlyList<GlyphLayer> SmallLayers { get; private init; }

    internal Glyph Small(params GlyphLayer[] layers) => new(this, layers, Keywords);

    internal Glyph Also(params string[] keywords) => new(this, SmallLayers, keywords);

    private Glyph(Glyph source, IReadOnlyList<GlyphLayer> small, IReadOnlyList<string> keywords)
    {
        Id = source.Id;
        DisplayName = source.DisplayName;
        Category = source.Category;
        Layers = source.Layers;
        SmallLayers = small;
        Keywords = keywords;
    }

    internal bool Matches(string text) =>
        Id.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        Category.ToString().Contains(text, StringComparison.OrdinalIgnoreCase) ||
        Keywords.Any(word => word.Contains(text, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One shape on a 24-unit square. Filled layers use even-odd winding, so a sub-path or
/// <see cref="Holes"/> circle inside a shape cuts through it.
/// </summary>
internal sealed partial record GlyphLayer(string PathData, bool IsStroke, float StrokeWidth, (float X, float Y, float R)[] Holes)
{
    internal const float BoxSize = 24f;

    /// <summary>How far a Bezier control point sits from a quarter circle's end, for a radius of one.</summary>
    private const float CircleControl = 0.5523f;

    public static GlyphLayer Fill(string pathData, params (float X, float Y, float R)[] holes) => new(pathData, false, 0f, holes);

    public static GlyphLayer Stroke(string pathData, float width = 2.4f) => new(pathData, true, width, []);

    /// <summary>A filled circle, drawn as a zero-length round-capped stroke.</summary>
    public static GlyphLayer Dot(float x, float y, float r) =>
        new(string.Create(CultureInfo.InvariantCulture, $"M{x} {y} H{x + 0.01f}"), true, r * 2, []);

    /// <summary>A circle's outline.</summary>
    public static GlyphLayer Ring(float x, float y, float r, float width = 2.4f) => Orbit(x, y, r, r, 0f, width);

    /// <summary>An ellipse's outline, turned by some degrees.</summary>
    public static GlyphLayer Orbit(float x, float y, float rx, float ry, float degrees, float width = 2f)
    {
        var (sin, cos) = MathF.SinCos(degrees * MathF.PI / 180f);
        var (kx, ky) = (rx * CircleControl, ry * CircleControl);

        string At(float dx, float dy) => string.Create(
            CultureInfo.InvariantCulture,
            $"{x + (dx * cos) - (dy * sin):0.###} {y + (dx * sin) + (dy * cos):0.###}");

        var path = new StringBuilder($"M{At(rx, 0)} ");
        path.Append($"C{At(rx, ky)} {At(kx, ry)} {At(0, ry)} ");
        path.Append($"C{At(-kx, ry)} {At(-rx, ky)} {At(-rx, 0)} ");
        path.Append($"C{At(-rx, -ky)} {At(-kx, -ry)} {At(0, -ry)} ");
        path.Append($"C{At(kx, -ry)} {At(rx, -ky)} {At(rx, 0)} Z");

        return Stroke(path.ToString(), width);
    }

    /// <summary>A round gear with square teeth and a hole in the middle.</summary>
    public static GlyphLayer Gear(float x, float y, int teeth, float outer, float inner, float hole)
    {
        var path = new StringBuilder();
        var step = 360f / (teeth * 4);

        for (var i = 0; i < teeth * 4; i++)
        {
            var radius = (i % 4) is 1 or 2 ? outer : inner;
            var (sin, cos) = MathF.SinCos(((i * step) - 90f) * MathF.PI / 180f);

            path.Append(i == 0 ? 'M' : 'L');
            path.Append(string.Create(CultureInfo.InvariantCulture, $"{x + (radius * cos):0.###} {y + (radius * sin):0.###} "));
        }

        path.Append('Z');
        return Fill(path.ToString(), (x, y, hole));
    }

    /// <summary>Builds the shape. Supports absolute M, L, H, V, C, Q and Z.</summary>
    public GraphicsPath ToPath()
    {
        var path = new GraphicsPath(FillMode.Alternate);
        var tokens = Tokens().Matches(PathData).Select(m => m.Value).ToArray();
        var index = 0;
        var current = PointF.Empty;
        var figureStart = PointF.Empty;
        var command = 'M';

        float Next() => float.Parse(tokens[index++], CultureInfo.InvariantCulture);

        while (index < tokens.Length)
        {
            if (char.IsAsciiLetter(tokens[index][0]))
            {
                command = tokens[index++][0];
            }

            switch (command)
            {
                case 'M':
                    path.StartFigure();
                    current = new PointF(Next(), Next());
                    figureStart = current;
                    command = 'L';
                    break;
                case 'L':
                    current = LineTo(path, current, new PointF(Next(), Next()));
                    break;
                case 'H':
                    current = LineTo(path, current, new PointF(Next(), current.Y));
                    break;
                case 'V':
                    current = LineTo(path, current, new PointF(current.X, Next()));
                    break;
                case 'C':
                {
                    var (a, b, end) = (new PointF(Next(), Next()), new PointF(Next(), Next()), new PointF(Next(), Next()));
                    path.AddBezier(current, a, b, end);
                    current = end;
                    break;
                }

                case 'Q':
                {
                    var (control, end) = (new PointF(Next(), Next()), new PointF(Next(), Next()));
                    path.AddBezier(current, Lerp(current, control), Lerp(end, control), end);
                    current = end;
                    break;
                }

                case 'Z':
                    path.CloseFigure();
                    current = figureStart;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown path command '{command}'.");
            }
        }

        foreach (var (x, y, r) in Holes)
        {
            path.AddEllipse(x - r, y - r, r * 2, r * 2);
        }

        return path;
    }

    private static PointF LineTo(GraphicsPath path, PointF from, PointF to)
    {
        path.AddLine(from, to);
        return to;
    }

    private static PointF Lerp(PointF from, PointF control) =>
        new(from.X + ((control.X - from.X) * 2f / 3f), from.Y + ((control.Y - from.Y) * 2f / 3f));

    [GeneratedRegex(@"[MLHVCQZ]|-?\d*\.?\d+")]
    private static partial Regex Tokens();
}
