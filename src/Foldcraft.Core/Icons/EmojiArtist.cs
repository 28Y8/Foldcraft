using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Foldcraft.Core.Icons;

/// <summary>
/// Draws an emoji with the Segoe UI Emoji font that ships with Windows, on the person's own
/// machine. The result stays in their icon library, and no emoji art is stored in this project.
/// <para>
/// GDI+ can only fill a font's outlines in one color. Color needs Direct2D, reached through a
/// few raw COM calls so Core needs no extra package. Any failure there is ordinary and
/// expected on some machines, so it falls back to one color instead of surfacing.
/// </para>
/// </summary>
internal static partial class EmojiArtist
{
    private const string FontName = "Segoe UI Emoji";

    /// <summary>Rendering big and shrinking evens out the font's metrics and gives clean edges.</summary>
    private const int Oversample = 2;

    private static readonly Guid Id2D1Factory = new("06152247-6f50-465a-9245-118bfd3b6007");
    private static readonly Guid IdWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
    private static readonly Guid WicPixelFormat32bppPbgra = new("6fddc324-4e03-4bfe-b185-3d77768dc910");

    /// <summary>
    /// The emoji in color, or null if Windows can't do that here. A result that's all one
    /// color came from the font's plain outlines, which <see cref="DrawInOneColor"/> does better.
    /// </summary>
    public static Bitmap? TryDrawInColor(string emoji, int side, Color ink)
    {
        try
        {
            var canvas = side * Oversample * 2;
            var pixels = RenderWithDirect2D(emoji, canvas, emSize: side * Oversample * 1.1f, ink);

            return pixels is not null && IsMulticolored(pixels) ? Shrink(pixels, canvas, side) : null;
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Fills the font's outlines in one color, fitted to the box.</summary>
    /// <exception cref="IconRenderException">The font has nothing to draw for this emoji.</exception>
    public static void DrawInOneColor(Graphics graphics, string emoji, Color ink, Rectangle box)
    {
        using var family = FindFamily();
        using var path = new GraphicsPath(FillMode.Winding);
        using var format = StringFormat.GenericTypographic;

        path.AddString(emoji, family, (int)FontStyle.Regular, box.Width, PointF.Empty, format);

        var bounds = path.GetBounds();

        if (bounds.Width <= 0f || bounds.Height <= 0f)
        {
            throw new IconRenderException("This emoji can't be drawn.");
        }

        var scale = Math.Min(box.Width / bounds.Width, box.Height / bounds.Height);

        using var transform = new Matrix();
        transform.Translate(
            box.Left + ((box.Width - (bounds.Width * scale)) / 2f),
            box.Top + ((box.Height - (bounds.Height * scale)) / 2f));
        transform.Scale(scale, scale);
        transform.Translate(-bounds.Left, -bounds.Top);
        path.Transform(transform);

        using var brush = new SolidBrush(ink);
        graphics.FillPath(brush, path);
    }

    private static FontFamily FindFamily()
    {
        using var installed = new InstalledFontCollection();

        return installed.Families.Any(f => f.Name.Equals(FontName, StringComparison.OrdinalIgnoreCase))
            ? new FontFamily(FontName)
            : new FontFamily(GenericFontFamilies.SansSerif);
    }

    private static bool IsMulticolored(byte[] pixels)
    {
        (int B, int G, int R)? first = null;

        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            // Partly covered edge pixels are blends, so only solid ones can tell colors apart.
            if (pixels[i + 3] != 255)
            {
                continue;
            }

            var pixel = (B: (int)pixels[i], G: (int)pixels[i + 1], R: (int)pixels[i + 2]);

            first ??= pixel;

            if (first != pixel)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Crops to the drawn pixels, then fits them to the box. The pixels are premultiplied.</summary>
    private static Bitmap Shrink(byte[] pixels, int canvas, int side)
    {
        var (left, top, right, bottom) = (canvas, canvas, -1, -1);

        for (var y = 0; y < canvas; y++)
        {
            for (var x = 0; x < canvas; x++)
            {
                if (pixels[(((y * canvas) + x) * 4) + 3] != 0)
                {
                    (left, right) = (Math.Min(left, x), Math.Max(right, x));
                    (top, bottom) = (Math.Min(top, y), Math.Max(bottom, y));
                }
            }
        }

        using var source = new Bitmap(canvas, canvas, PixelFormat.Format32bppPArgb);
        var data = source.LockBits(new Rectangle(0, 0, canvas, canvas), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);

        try
        {
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            source.UnlockBits(data);
        }

        var (width, height) = (right - left + 1, bottom - top + 1);
        var scale = Math.Min((float)side / width, (float)side / height);
        var (fitWidth, fitHeight) = (width * scale, height * scale);
        var result = new Bitmap(side, side, PixelFormat.Format32bppArgb);

        try
        {
            using var graphics = Graphics.FromImage(result);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(
                source,
                new RectangleF((side - fitWidth) / 2f, (side - fitHeight) / 2f, fitWidth, fitHeight),
                new RectangleF(left, top, width, height),
                GraphicsUnit.Pixel);

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>Draws into a software render target, and returns its premultiplied BGRA pixels.</summary>
    private static byte[]? RenderWithDirect2D(string emoji, int canvas, float emSize, Color ink)
    {
        var (d2d, dwrite, wic, bitmap, target, format, brush) = (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        try
        {
            var options = IntPtr.Zero;

            Check(D2D1CreateFactory(0, Id2D1Factory, ref options, out d2d));
            Check(DWriteCreateFactory(0, IdWriteFactory, out dwrite));
            Check(WICCreateImagingFactory_Proxy(WicSdkVersion, out wic));

            Check(Com<CreateBitmapCall>(wic, 17)(wic, (uint)canvas, (uint)canvas, WicPixelFormat32bppPbgra, CacheOnDemand, out bitmap));

            var properties = new RenderTargetProperties
            {
                Type = RenderTargetSoftware,
                Format = PixelFormatB8G8R8A8,
                AlphaMode = AlphaPremultiplied,
                DpiX = 96f,
                DpiY = 96f,
            };

            Check(Com<CreateWicTargetCall>(d2d, 13)(d2d, bitmap, properties, out target));

            Check(Com<CreateTextFormatCall>(dwrite, 15)(dwrite, FontName, IntPtr.Zero, 400, 0, 5, emSize, "en-us", out format));
            Check(Com<SetAlignmentCall>(format, 3)(format, AlignCenter));
            Check(Com<SetAlignmentCall>(format, 4)(format, AlignCenter));

            var color = new ColorF(ink.R / 255f, ink.G / 255f, ink.B / 255f, ink.A / 255f);
            Check(Com<CreateBrushCall>(target, 8)(target, color, IntPtr.Zero, out brush));

            var layout = new RectF(0f, 0f, canvas, canvas);

            Com<BeginDrawCall>(target, 48)(target);
            Com<DrawTextCall>(target, 27)(target, emoji, (uint)emoji.Length, format, layout, brush, EnableColorFont, 0);
            Check(Com<EndDrawCall>(target, 49)(target, IntPtr.Zero, IntPtr.Zero));

            var pixels = new byte[canvas * canvas * 4];
            Check(Com<CopyPixelsCall>(bitmap, 7)(bitmap, IntPtr.Zero, (uint)canvas * 4, (uint)pixels.Length, pixels));

            return pixels.Where((_, i) => i % 4 == 3).Any(alpha => alpha != 0) ? pixels : null;
        }
        finally
        {
            foreach (var item in new[] { brush, format, target, bitmap, wic, dwrite, d2d })
            {
                if (item != IntPtr.Zero)
                {
                    Marshal.Release(item);
                }
            }
        }
    }

    private static void Check(int result)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"Drawing failed with code {result:x8}.");
        }
    }

    /// <summary>Fetches a method from a COM object's table, at its position in the interface.</summary>
    private static T Com<T>(IntPtr instance, int slot)
        where T : Delegate
    {
        var table = Marshal.ReadIntPtr(instance);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, slot * IntPtr.Size));
    }

    private const uint WicSdkVersion = 0x0237;
    private const int CacheOnDemand = 1;
    private const int RenderTargetSoftware = 1;
    private const int PixelFormatB8G8R8A8 = 87;
    private const int AlphaPremultiplied = 1;
    private const int AlignCenter = 2;
    private const int EnableColorFont = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct RenderTargetProperties
    {
        public int Type;
        public int Format;
        public int AlphaMode;
        public float DpiX;
        public float DpiY;
        public int Usage;
        public int MinLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ColorF(float R, float G, float B, float A);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct RectF(float Left, float Top, float Right, float Bottom);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateBitmapCall(IntPtr self, uint width, uint height, in Guid format, int cache, out IntPtr bitmap);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateWicTargetCall(IntPtr self, IntPtr bitmap, in RenderTargetProperties properties, out IntPtr target);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int CreateTextFormatCall(
        IntPtr self, string family, IntPtr collection, int weight, int style, int stretch, float size, string locale, out IntPtr format);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetAlignmentCall(IntPtr self, int alignment);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateBrushCall(IntPtr self, in ColorF color, IntPtr properties, out IntPtr brush);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BeginDrawCall(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate void DrawTextCall(
        IntPtr self, string text, uint length, IntPtr format, in RectF layout, IntPtr brush, int options, int measuring);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EndDrawCall(IntPtr self, IntPtr tag1, IntPtr tag2);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CopyPixelsCall(IntPtr self, IntPtr rectangle, uint stride, uint size, [Out] byte[] buffer);

    [DllImport("d2d1.dll", ExactSpelling = true)]
    private static extern int D2D1CreateFactory(int type, in Guid iid, ref IntPtr options, out IntPtr factory);

    [DllImport("dwrite.dll", ExactSpelling = true)]
    private static extern int DWriteCreateFactory(int type, in Guid iid, out IntPtr factory);

    [DllImport("windowscodecs.dll", ExactSpelling = true)]
    private static extern int WICCreateImagingFactory_Proxy(uint version, out IntPtr factory);
}
