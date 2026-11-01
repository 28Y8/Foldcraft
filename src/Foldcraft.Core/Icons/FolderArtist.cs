using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Foldcraft.Core.Icons;

/// <summary>
/// Draws the folder. Each size is drawn from scratch with its edges snapped to whole pixels,
/// because shrinking the 256 px frame is what makes small icons blurry. The folder stands at
/// the size and height of the stock Windows 11 folder, so the two line up in a File Explorer
/// window, and every size is lit the same way: from the top left, with a bright upper edge and
/// a darker rim along the bottom.
/// </summary>
internal static class FolderArtist
{
    /// <summary>At this size and below a glyph has about ten pixels, and uses its small drawing.</summary>
    private const int SmallGlyphSize = 20;

    /// <summary>At this size and below a logo takes over the whole folder, or it stops being recognizable.</summary>
    private const int SmallLogoSize = 24;

    /// <summary>A logo below this size has no room for a shadow to read as one.</summary>
    private const int LogoShadowSize = 48;

    /// <summary>A small source stops growing here. Past this it turns to mush, and a smaller sharp logo looks better.</summary>
    private const float MaxLogoEnlargement = 2f;

    /// <summary>A glyph below this size has no room for the soft edge that lifts it off the folder.</summary>
    private const int GlyphEdgeSize = 48;

    /// <summary>How the front of a classic folder is lit: bright at the top left and settling into the color itself.</summary>
    private static readonly Light ClassicLight = new(
        Angle: 40f,
        Stops: [(0f, 255, 80), (0.66f, 255, 0), (1f, 0, 12)],
        EdgeAlpha: 92);

    /// <summary>The same panel with the light directly above it and more shade underneath, for a rounder look.</summary>
    private static readonly Light SoftLight = new(
        Angle: 90f,
        Stops: [(0f, 255, 72), (0.5f, 255, 0), (1f, 0, 46)],
        EdgeAlpha: 96);

    public static Bitmap Draw(FolderIconSpec spec, int size, Bitmap? customImage)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // HighQuality blends with gamma correction, which turns a faint overlay into a
            // heavy wash and takes the fill away from the color the user picked.
            graphics.CompositingQuality = CompositingQuality.AssumeLinear;

            var layout = new Layout(size, spec.Shape);

            switch (spec.Shape)
            {
                case FolderShape.Flat:
                    DrawFlatShell(graphics, spec.Fill, layout);
                    break;
                case FolderShape.Outline:
                    DrawOutlineShell(graphics, spec.Fill, layout);
                    break;
                case FolderShape.Soft3D:
                    DrawSoftShell(graphics, spec.Fill, layout);
                    break;
                default:
                    DrawShell(graphics, spec.Fill, layout);
                    break;
            }

            if (customImage is not null && spec.Logo is { } logo)
            {
                DrawLogo(graphics, customImage, logo, layout);
            }
            else if (customImage is not null)
            {
                DrawImage(graphics, customImage, layout);
            }
            else if (spec.GlyphId is not null)
            {
                DrawGlyph(graphics, GlyphCatalog.Find(spec.GlyphId)!, InkFor(spec), layout, spec.Shape != FolderShape.Outline);
            }
            else if (spec.Emoji is not null)
            {
                DrawEmoji(graphics, spec.Emoji, InkFor(spec), layout);
            }

            if (spec.Badge is not null)
            {
                BadgeArtist.Draw(graphics, spec.Badge, size, layout.Front.Bottom);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>The two-tone folder: a darker back with the tab, and a lit front panel stepping down over it.</summary>
    private static void DrawShell(Graphics graphics, FolderFill fill, Layout layout)
    {
        using (var backPath = layout.BackPath())
        using (var backBrush = CreateBrush(fill, layout.BackBounds, BackTone))
        {
            graphics.FillPath(backBrush, backPath);
            DrawContactShadow(graphics, backPath, layout);
        }

        using var frontPath = layout.FrontPath();
        using var edgePath = layout.FrontTopEdge(-layout.RimHeight);

        DrawFront(graphics, fill, frontPath, edgePath, layout, ClassicLight);
    }

    /// <summary>The front casts a little shade on the tab behind it, which is what makes the panels read as two.</summary>
    private static void DrawContactShadow(Graphics graphics, GraphicsPath backPath, Layout layout)
    {
        if (layout.Size < 32)
        {
            return;
        }

        var band = Rectangle.FromLTRB(layout.Front.Left, layout.StepTop - layout.ShadeHeight, layout.StepEnd, layout.StepTop);
        var previousClip = graphics.Clip;

        using var brush = new LinearGradientBrush(Rectangle.Inflate(band, 0, 1), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(42, 0, 0, 0), 90f);

        graphics.SetClip(backPath, CombineMode.Intersect);
        graphics.FillRectangle(brush, band);
        graphics.Clip = previousClip;
    }

    /// <summary>One lit panel: the rim first, then the fill a little higher, then the light, then the bright edge.</summary>
    private static void DrawFront(Graphics graphics, FolderFill fill, GraphicsPath frontPath, GraphicsPath edgePath, Layout layout, Light light)
    {
        var previousClip = graphics.Clip;
        var bounds = layout.Front;
        var strength = LightStrength(fill);

        using (var rimBrush = CreateBrush(fill, bounds, RimTone))
        {
            graphics.FillPath(rimBrush, frontPath);
        }

        using var bodyPath = (GraphicsPath)frontPath.Clone();
        using var lift = new Matrix();
        lift.Translate(0f, -layout.RimHeight);
        bodyPath.Transform(lift);

        graphics.SetClip(frontPath, CombineMode.Intersect);

        using (var bodyBrush = CreateBrush(fill, bounds, c => c))
        {
            graphics.FillPath(bodyBrush, bodyPath);
        }

        graphics.SetClip(bodyPath, CombineMode.Intersect);

        using (var lightBrush = light.CreateBrush(bounds, strength))
        {
            graphics.FillPath(lightBrush, bodyPath);
        }

        // Half of this line falls outside the panel and is clipped, which keeps it inside the curve.
        using (var edge = new Pen(Color.FromArgb((int)(light.EdgeAlpha * strength), 255, 255, 255), layout.EdgeWidth * 2f))
        {
            graphics.DrawPath(edge, edgePath);
        }

        graphics.Clip = previousClip;
    }

    /// <summary>One silhouette in one fill, with nothing between the panels.</summary>
    private static void DrawFlatShell(Graphics graphics, FolderFill fill, Layout layout)
    {
        using var brush = CreateBrush(fill, layout.BackBounds, c => c);
        using var silhouette = layout.Silhouette(0f);

        graphics.FillPath(brush, silhouette);
    }

    /// <summary>
    /// Lines on a faint tint. The tint keeps a glyph readable on any background, and the line
    /// stays one whole pixel wide at small sizes, sitting on pixel centers so it doesn't blur.
    /// </summary>
    private static void DrawOutlineShell(Graphics graphics, FolderFill fill, Layout layout)
    {
        var width = Math.Max(1, (int)MathF.Round(layout.Size / 32f));
        var inset = width / 2f;

        using var lineBrush = CreateBrush(fill, layout.BackBounds, c => c);
        using var pen = new Pen(lineBrush, width) { LineJoin = LineJoin.Round };
        using var silhouette = layout.Silhouette(inset);
        using var tint = new SolidBrush(Color.FromArgb(46, ToColor(fill.Start)));

        graphics.FillPath(tint, silhouette);
        graphics.DrawPath(pen, silhouette);

        // The line closes off the tab. Past it the folder's own top edge does the same job.
        graphics.DrawLine(pen, layout.BackBounds.Left + inset, layout.Shelf + inset, layout.TabEnd - inset, layout.Shelf + inset);
    }

    /// <summary>Rounded panels lit from above, resting on a soft blurred shadow once there's room for one.</summary>
    private static void DrawSoftShell(Graphics graphics, FolderFill fill, Layout layout)
    {
        using var frontPath = layout.RoundFrontPath();
        using var edgePath = layout.RoundFrontTopEdge(-layout.RimHeight);

        if (layout.Size >= 32)
        {
            using var silhouette = layout.Silhouette(0f);
            var reach = Math.Max(1, (int)MathF.Round(layout.Size / 14f));

            SoftShadow.Draw(graphics, silhouette, layout.Size, reach, reach / 2, 84);
        }

        using (var backPath = layout.BackPath())
        using (var backBrush = CreateBrush(fill, layout.BackBounds, DeepTone))
        {
            graphics.FillPath(backBrush, backPath);
            DrawContactShadow(graphics, backPath, layout);
        }

        DrawFront(graphics, fill, frontPath, edgePath, layout, SoftLight);
    }

    private static void DrawGlyph(Graphics graphics, Glyph glyph, Color color, Layout layout, bool lifted)
    {
        var box = layout.GlyphBox();
        var layers = layout.Size <= SmallGlyphSize ? glyph.SmallLayers : glyph.Layers;
        var scale = box.Width / GlyphLayer.BoxSize;

        if (lifted && layout.Size >= GlyphEdgeSize)
        {
            // Light ink gets a faint shade under it, and dark ink a faint light edge, as if pressed in.
            var edge = color.GetBrightness() > 0.5f ? Color.FromArgb(56, 0, 0, 0) : Color.FromArgb(70, 255, 255, 255);
            var drop = new PointF(box.X, box.Y + Math.Max(1f, layout.Size / 128f));

            DrawLayers(graphics, layers, edge, drop, scale, 1f);
        }

        DrawLayers(graphics, layers, color, box.Location, scale, 1f);
    }

    /// <summary>Draws glyph layers. A stroke is never thinner than the given minimum in pixels.</summary>
    internal static void DrawLayers(Graphics graphics, IEnumerable<GlyphLayer> layers, Color color, PointF origin, float scale, float minimumStroke)
    {
        using var brush = new SolidBrush(color);
        using var transform = new Matrix();
        transform.Translate(origin.X, origin.Y);
        transform.Scale(scale, scale);

        foreach (var layer in layers)
        {
            using var path = layer.ToPath();
            path.Transform(transform);

            if (layer.IsStroke)
            {
                using var pen = new Pen(color, Math.Max(minimumStroke, layer.StrokeWidth * scale))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round,
                };

                graphics.DrawPath(pen, path);
            }
            else
            {
                graphics.FillPath(brush, path);
            }
        }
    }

    /// <summary>The emoji's own colors when Windows will draw them, or one color when it won't.</summary>
    private static void DrawEmoji(Graphics graphics, string emoji, Color ink, Layout layout)
    {
        var box = layout.GlyphBox();
        using var picture = EmojiArtist.TryDrawInColor(emoji, box.Width, ink);

        if (picture is not null)
        {
            graphics.DrawImage(picture, box);
            return;
        }

        EmojiArtist.DrawInOneColor(graphics, emoji, ink, box);
    }

    /// <summary>Letterboxes the image inside the front panel, keeping its aspect ratio.</summary>
    private static void DrawImage(Graphics graphics, Bitmap image, Layout layout)
    {
        var area = layout.ImageArea();
        var scale = Math.Min((float)area.Width / image.Width, (float)area.Height / image.Height);
        var width = Math.Max(1, (int)MathF.Round(image.Width * scale));
        var height = Math.Max(1, (int)MathF.Round(image.Height * scale));
        var destination = new Rectangle(
            area.Left + ((area.Width - width) / 2),
            area.Top + ((area.Height - height) / 2),
            width,
            height);

        using var clip = RoundedRectangle(destination, Math.Max(0f, layout.Radius - 1f));
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);

        var previousClip = graphics.Clip;
        graphics.SetClip(clip, CombineMode.Intersect);
        graphics.DrawImage(image, destination, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        graphics.Clip = previousClip;
    }

    /// <summary>The image as a product logo: whole, on any plate it needs, with a soft shadow once there's room.</summary>
    private static void DrawLogo(Graphics graphics, Bitmap image, LogoPlacement logo, Layout layout)
    {
        var destination = layout.LogoBox(image.Width, image.Height, logo);
        var radius = Math.Max(1f, destination.Height * 0.14f);

        if (logo.Plate != LogoPlate.None)
        {
            var margin = layout.Size <= SmallLogoSize ? 1 : Math.Max(2, destination.Height / 12);
            var plate = Rectangle.Intersect(Rectangle.Inflate(destination, margin, margin), layout.BackBounds);
            using var platePath = RoundedRectangle(plate, radius + margin);
            using var plateBrush = new SolidBrush(logo.Plate == LogoPlate.Light
                ? Color.FromArgb(238, 250, 250, 252)
                : Color.FromArgb(232, 28, 32, 40));

            graphics.FillPath(plateBrush, platePath);
        }
        else if (layout.Size >= LogoShadowSize)
        {
            DrawLogoShadow(graphics, image, destination, Math.Max(1, layout.Size / 96));
        }

        // A logo with its own shape already has soft edges. A plain rectangle gets them here.
        var isRectangular = image.GetPixel(0, 0).A > 200 && image.GetPixel(image.Width - 1, image.Height - 1).A > 200;
        var previousClip = graphics.Clip;
        using var clip = RoundedRectangle(destination, radius);

        if (isRectangular)
        {
            graphics.SetClip(clip, CombineMode.Intersect);
        }

        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(image, destination, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        graphics.Clip = previousClip;
    }

    /// <summary>The logo's own outline in faint black, a little lower, so it sits on the folder rather than in it.</summary>
    private static void DrawLogoShadow(Graphics graphics, Bitmap image, Rectangle destination, int offset)
    {
        var silhouette = new ColorMatrix(
        [
            [0f, 0f, 0f, 0f, 0f],
            [0f, 0f, 0f, 0f, 0f],
            [0f, 0f, 0f, 0f, 0f],
            [0f, 0f, 0f, 0.16f, 0f],
            [0f, 0f, 0f, 0f, 1f],
        ]);

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(silhouette);
        attributes.SetWrapMode(WrapMode.TileFlipXY);

        for (var i = 1; i <= 2; i++)
        {
            var shifted = destination with { Y = destination.Y + (offset * i) };
            graphics.DrawImage(image, shifted, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }
    }

    private static Brush CreateBrush(FolderFill fill, Rectangle bounds, Func<Color, Color> adjust)
    {
        var start = adjust(ToColor(fill.Start));

        if (fill.End is not { } end)
        {
            return new SolidBrush(start);
        }

        // Inflating keeps the outermost pixels from wrapping round to the opposite stop.
        return new LinearGradientBrush(Rectangle.Inflate(bounds, 1, 1), start, adjust(ToColor(end)), fill.AngleDegrees, isAngleScaleable: false);
    }

    /// <summary>An outline folder has nothing behind its glyph but a faint tint, so the glyph takes the folder's color.</summary>
    private static Color InkFor(FolderIconSpec spec) =>
        spec.Shape == FolderShape.Outline ? ToColor(spec.Fill.Start) : ContrastingInk(spec.Fill);

    private static Color ContrastingInk(FolderFill fill)
    {
        var end = fill.End ?? fill.Start;
        var (r, g, b) = ((fill.Start.R + end.R) / 2f, (fill.Start.G + end.G) / 2f, (fill.Start.B + end.B) / 2f);
        var luminance = ((0.2126f * r) + (0.7152f * g) + (0.0722f * b)) / 255f;

        return luminance > 0.62f ? Color.FromArgb(235, 28, 32, 40) : Color.FromArgb(245, 255, 255, 255);
    }

    private static Color ToColor(IconColor color) => Color.FromArgb(color.R, color.G, color.B);

    /// <summary>
    /// The back of the folder, a step darker than the front. Scaling lightness keeps the color
    /// vivid, where mixing in black would turn a yellow to mud. Near-black colors go the other
    /// way, or the tab would disappear on a dark background.
    /// </summary>
    private static Color BackTone(Color color) => IsNearBlack(color) ? Mix(color, Color.White, 0.2f) : Tone(color, 0.84f, 1f);

    private static Color DeepTone(Color color) => IsNearBlack(color) ? Mix(color, Color.White, 0.12f) : Tone(color, 0.7f, 1f);

    /// <summary>The bottom rim of the front panel.</summary>
    private static Color RimTone(Color color) => IsNearBlack(color) ? Mix(color, Color.White, 0.1f) : Tone(color, 0.76f, 0.9f);

    private static bool IsNearBlack(Color color) => color.GetBrightness() < 0.22f;

    /// <summary>White light on a dark color reads as gray haze, so the darker the fill, the less light it gets.</summary>
    private static float LightStrength(FolderFill fill)
    {
        var end = fill.End ?? fill.Start;
        var brightness = (ToColor(fill.Start).GetBrightness() + ToColor(end).GetBrightness()) / 2f;

        return Math.Clamp(0.45f + brightness, 0.5f, 1f);
    }

    private static Color Mix(Color color, Color other, float amount)
    {
        int Blend(int a, int b) => (int)MathF.Round(a + ((b - a) * amount));

        return Color.FromArgb(Blend(color.R, other.R), Blend(color.G, other.G), Blend(color.B, other.B));
    }

    /// <summary>The same hue with its lightness and saturation scaled.</summary>
    private static Color Tone(Color color, float lightness, float saturation)
    {
        var l = color.GetBrightness() * lightness;
        var s = color.GetSaturation() * saturation;
        var h = color.GetHue() / 60f;
        var chroma = (1f - MathF.Abs((2f * l) - 1f)) * s;
        var x = chroma * (1f - MathF.Abs((h % 2f) - 1f));
        var m = l - (chroma / 2f);

        var (r, g, b) = (int)h switch
        {
            0 => (chroma, x, 0f),
            1 => (x, chroma, 0f),
            2 => (0f, chroma, x),
            3 => (0f, x, chroma),
            4 => (x, 0f, chroma),
            _ => (chroma, 0f, x),
        };

        int Channel(float value) => Math.Clamp((int)MathF.Round((value + m) * 255f), 0, 255);

        return Color.FromArgb(Channel(r), Channel(g), Channel(b));
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, float radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = radius * 2f;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>A panel's lighting: stops of light or shade laid over its fill along one direction.</summary>
    private sealed record Light(float Angle, (float Position, int Shade, int Alpha)[] Stops, int EdgeAlpha)
    {
        public LinearGradientBrush CreateBrush(Rectangle bounds, float strength)
        {
            return new LinearGradientBrush(Rectangle.Inflate(bounds, 1, 1), Color.Transparent, Color.Transparent, Angle, isAngleScaleable: false)
            {
                InterpolationColors = new ColorBlend
                {
                    Colors = Stops.Select(stop => Color.FromArgb((int)(stop.Alpha * strength), stop.Shade, stop.Shade, stop.Shade)).ToArray(),
                    Positions = Stops.Select(stop => stop.Position).ToArray(),
                },
            };
        }
    }

    /// <summary>Folder geometry for one size, in whole pixels.</summary>
    private sealed class Layout
    {
        /// <summary>
        /// Left edge, top and bottom for the sizes in an .ico. These are set by hand rather than
        /// calculated, because at these sizes one pixel decides whether a colored folder sits
        /// level with the stock folder beside it.
        /// </summary>
        private static readonly (int Size, int Left, int Top, int Bottom)[] Frames =
        [
            (16, 0, 1, 14), (20, 1, 3, 17), (24, 1, 3, 20), (32, 2, 4, 26), (40, 3, 5, 33),
            (48, 3, 6, 39), (64, 4, 9, 53), (96, 6, 13, 79), (256, 16, 34, 210),
        ];

        private readonly int _left;
        private readonly int _right;
        private readonly int _top;
        private readonly int _bottom;

        public Layout(int size, FolderShape shape)
        {
            Size = size;
            (_left, _top, _bottom) = FrameFor(size);

            // A line needs a clear column on each side, and the full 16 px width leaves none.
            if (shape == FolderShape.Outline)
            {
                _left = Math.Max(1, _left);
            }

            _right = size - _left;

            var width = _right - _left;
            var height = _bottom - _top;

            Shelf = _top + Math.Max(2, Snap(height * 0.17f));
            TabEnd = _left + Snap(width * 0.455f);

            var frontTop = Shelf + Math.Max(1, Snap(size * 0.012f));

            Radius = shape == FolderShape.Soft3D
                ? Math.Max(2f, size * 0.075f)
                : size < 20 ? 1f : Math.Max(1f, size * 0.052f);

            Front = Rectangle.FromLTRB(_left, frontTop, _right, _bottom);
            StepTop = frontTop + Math.Max(1, Snap(size * 0.055f));
            StepEnd = TabEnd - (size < 32 ? 0 : Snap(width * 0.02f));
            RimHeight = Math.Max(1, Snap(size / 85f));
            EdgeWidth = Math.Max(1f, size / 128f);
            ShadeHeight = Math.Max(1, Snap(size * 0.04f));
        }

        public int Size { get; }
        public float Radius { get; }
        public Rectangle Front { get; }
        public Rectangle BackBounds => Rectangle.FromLTRB(_left, _top, _right, _bottom);

        /// <summary>The top of the folder's back, to the right of the tab.</summary>
        public int Shelf { get; }

        /// <summary>Where the tab has finished falling to the shelf.</summary>
        public int TabEnd { get; }

        /// <summary>The top of the front panel where it steps down at the left, below the tab.</summary>
        public int StepTop { get; }

        /// <summary>Where the front panel's step ends and it rises to its full height.</summary>
        public int StepEnd { get; }

        public int RimHeight { get; }
        public float EdgeWidth { get; }
        public int ShadeHeight { get; }

        /// <summary>Curves need room to be curves. Below this they are plain steps, which stay sharp.</summary>
        private bool HasCurves => Size >= 32;

        /// <summary>The rear panel: a rounded body with a tab rising at the top left.</summary>
        public GraphicsPath BackPath() => Silhouette(0f);

        /// <summary>The whole folder's outer edge, moved in by some distance so a line can sit on it.</summary>
        public GraphicsPath Silhouette(float inset)
        {
            var path = new GraphicsPath();
            var diameter = Radius * 2f;
            var (left, right, top, bottom) = (_left + inset, _right - inset, _top + inset, _bottom - inset);
            var shelf = Shelf + inset;
            var tabEnd = TabEnd - inset;

            path.AddArc(left, top, diameter, diameter, 180, 90);

            if (HasCurves)
            {
                // The tab's shoulder eases over and lands on the shelf without a corner.
                var run = (tabEnd - left) * 0.24f;

                path.AddLine(left + Radius, top, tabEnd - run, top);
                path.AddBezier(tabEnd - run, top, tabEnd - (run * 0.42f), top, tabEnd - (run * 0.44f), shelf, tabEnd, shelf);
            }
            else
            {
                path.AddLine(left + Radius, top, tabEnd - 1f, top);
                path.AddLine(tabEnd - 1f, top, tabEnd - 1f, shelf - 1f);
                path.AddLine(tabEnd - 1f, shelf - 1f, tabEnd, shelf - 1f);
                path.AddLine(tabEnd, shelf - 1f, tabEnd, shelf);
            }

            path.AddLine(tabEnd, shelf, right - Radius, shelf);
            path.AddArc(right - diameter, shelf, diameter, diameter, 270, 90);
            path.AddArc(right - diameter, bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(left, bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>The front panel, stepping down at the left so the tab shows above it.</summary>
        public GraphicsPath FrontPath()
        {
            var path = new GraphicsPath();
            var diameter = Radius * 2f;

            AddFrontTop(path, 0f);
            path.AddArc(_right - diameter, Front.Top, diameter, diameter, 270, 90);
            path.AddArc(_right - diameter, _bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(_left, _bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>The front panel as a plain rounded rectangle, for the rounder style.</summary>
        public GraphicsPath RoundFrontPath()
        {
            var path = new GraphicsPath();
            var diameter = Radius * 2f;

            path.AddArc(_left, Front.Top, diameter, diameter, 180, 90);
            path.AddArc(_right - diameter, Front.Top, diameter, diameter, 270, 90);
            path.AddArc(_right - diameter, _bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(_left, _bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>The top of the rounder panel as an open line, moved down or up by some distance.</summary>
        public GraphicsPath RoundFrontTopEdge(float shift)
        {
            var path = new GraphicsPath();
            var diameter = Radius * 2f;

            // Stopping partway round each corner lets the line taper away instead of ending in a tick.
            path.AddArc(_left, Front.Top + shift, diameter, diameter, 225, 45);
            path.AddArc(_right - diameter, Front.Top + shift, diameter, diameter, 270, 45);
            return path;
        }

        /// <summary>The top of the stepped panel as an open line, moved down or up by some distance.</summary>
        public GraphicsPath FrontTopEdge(float shift)
        {
            var path = new GraphicsPath();

            AddFrontTop(path, shift);
            path.AddLine(path.GetLastPoint(), new PointF(_right, Front.Top + shift));
            return path;
        }

        private void AddFrontTop(GraphicsPath path, float shift)
        {
            var diameter = Radius * 2f;
            var (step, top) = (StepTop + shift, Front.Top + shift);

            path.AddArc(_left, step, diameter, diameter, 180, 90);

            if (HasCurves)
            {
                var rise = (StepEnd - _left) * 0.2f;

                path.AddLine(_left + Radius, step, StepEnd - rise, step);
                path.AddBezier(StepEnd - rise, step, StepEnd - (rise * 0.35f), step, StepEnd - (rise * 0.1f), top, StepEnd + (rise * 0.5f), top);
            }
            else
            {
                path.AddLine(_left + Radius, step, StepEnd, step);
                path.AddLine(StepEnd, step, StepEnd, top);
            }
        }

        private static (int Left, int Top, int Bottom) FrameFor(int size)
        {
            foreach (var frame in Frames)
            {
                if (frame.Size == size)
                {
                    return (frame.Left, frame.Top, frame.Bottom);
                }
            }

            return (Snap(size * 0.0625f), Snap(size * 0.133f), Snap(size * 0.82f));
        }

        /// <summary>
        /// A square centered on the front panel. Small sizes use more of the panel, but at 16 px
        /// the symbol stops short of the bright top edge and the rim, or it would run into both.
        /// </summary>
        public Rectangle GlyphBox()
        {
            var fraction = Size <= 16 ? 0.8f : Size <= 24 ? 0.92f : 0.68f;
            var side = Math.Max(6, (int)MathF.Round(Math.Min(Front.Width, Front.Height) * fraction));

            return new Rectangle(
                Front.Left + ((Front.Width - side) / 2),
                Front.Top + ((Front.Height - side) / 2),
                side,
                side);
        }

        /// <summary>Where a logo goes, keeping its shape and never enlarging it past what it can take.</summary>
        public Rectangle LogoBox(int imageWidth, int imageHeight, LogoPlacement logo)
        {
            var small = Size <= SmallLogoSize;
            var area = small ? Rectangle.Inflate(BackBounds, 0, -1) : Front;
            var (widthShare, heightShare) = small ? (1f, 1f) : (0.82f * logo.Scale, 0.78f * logo.Scale);
            var fit = MathF.Min(area.Width * widthShare / imageWidth, area.Height * heightShare / imageHeight);

            if (!small)
            {
                fit = MathF.Min(fit, MaxLogoEnlargement);
            }

            var width = Math.Clamp((int)MathF.Round(imageWidth * fit), 1, area.Width);
            var height = Math.Clamp((int)MathF.Round(imageHeight * fit), 1, area.Height);
            var margin = Math.Max(1, Snap(Size * 0.04f));
            var anchor = small ? LogoAnchor.Center : logo.Anchor;

            var x = anchor switch
            {
                LogoAnchor.BottomLeft or LogoAnchor.TopLeft => area.Left + margin,
                LogoAnchor.BottomRight or LogoAnchor.TopRight => area.Right - margin - width,
                _ => area.Left + ((area.Width - width) / 2),
            };

            var y = anchor switch
            {
                LogoAnchor.TopLeft or LogoAnchor.TopRight => area.Top + margin,
                LogoAnchor.BottomLeft or LogoAnchor.BottomRight => area.Bottom - margin - height,
                _ => area.Top + ((area.Height - height) / 2),
            };

            return new Rectangle(x, y, width, height);
        }

        public Rectangle ImageArea()
        {
            var inset = Math.Max(1, Snap(Size * 0.035f));
            var area = Rectangle.Inflate(Front, -inset, -inset);
            return area.Width > 0 && area.Height > 0 ? area : Front;
        }

        private static int Snap(float value) => (int)MathF.Round(value);
    }
}
