using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Foldcraft.Core.Icons;

namespace Foldcraft.Core.Apps;

/// <summary>Gets an app's picture from wherever it might be.</summary>
public interface IAppIconReader
{
    /// <summary>The best picture among these places, or null if none gives one.</summary>
    AppArt? Read(IReadOnlyList<IconCandidate> candidates, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads icons from software on this PC, at run time. An icon's largest real frame is taken
/// directly, because asking Windows for 256 px hands back a small frame stretched to fit, with
/// no way to tell it from a sharp one. The shell is the fallback for what isn't a plain file.
/// <para>
/// Programs are opened as data only, so no code in them runs. Each candidate goes through
/// <see cref="LocalSource"/> first, and one that can't be read is simply passed over.
/// </para>
/// </summary>
public sealed class ShellIconReader : IAppIconReader
{
    /// <summary>A picture this big is good enough, so looking further would only cost time.</summary>
    private const int GoodEnough = 96;

    private static readonly string[] Pictures = [".png", ".jpg", ".jpeg", ".bmp"];
    private static readonly string[] Programs = [".exe", ".dll"];

    private readonly ImageLimits _limits;

    public ShellIconReader(ImageLimits? limits = null)
    {
        _limits = limits ?? ImageLimits.Default;
    }

    public AppArt? Read(IReadOnlyList<IconCandidate> candidates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        AppArt? best = null;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var art = ReadOne(candidate);

            if (art is not null && (best is null || Size(art) > Size(best)))
            {
                best = art;
            }

            if (best is not null && Size(best) >= GoodEnough)
            {
                break;
            }
        }

        return best;
    }

    private static int Size(AppArt art) => Math.Max(art.Width, art.Height);

    private AppArt? ReadOne(IconCandidate candidate)
    {
        if (LocalSource.Normalize(candidate.Path) is not { } path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var extension = Path.GetExtension(path);

            using var image =
                extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ? IconFrames.ReadLargest(File.ReadAllBytes(path), _limits)
                : Pictures.Contains(extension, StringComparer.OrdinalIgnoreCase) ? CustomImageLoader.Decode(CustomImageLoader.ReadBytes(path, _limits), _limits)
                : Programs.Contains(extension, StringComparer.OrdinalIgnoreCase) ? ProgramIcons.ReadLargest(path, candidate.Index, _limits)
                : ShellImage.Read(path);

            return image is null ? null : AppArtMaker.From(image, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IconRenderException or ArgumentException or ExternalException or OutOfMemoryException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>Picks and decodes the best frame of an icon.</summary>
internal static class IconFrames
{
    private const uint IconVersion = 0x00030000;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The largest frame of an .ico file. Among equal sizes, the most colors.</summary>
    public static Bitmap? ReadLargest(byte[] ico, ImageLimits limits)
    {
        var frames = IcoFile.TryReadFrames(ico);

        if (frames is null)
        {
            return null;
        }

        var best = frames.OrderByDescending(frame => frame.Width).ThenByDescending(frame => frame.BitsPerPixel).First();

        return Decode(ico.AsSpan(best.Offset, best.Length).ToArray(), best.Width, best.Height, limits);
    }

    /// <summary>
    /// Decodes one frame. GDI+ throws away the transparency of the older kind of frame when
    /// reading an icon, so those go through Windows' own icon loader instead.
    /// </summary>
    public static Bitmap? Decode(byte[] frame, int width, int height, ImageLimits limits)
    {
        if (frame.AsSpan().StartsWith(PngSignature))
        {
            return CustomImageLoader.Decode(frame, limits);
        }

        var handle = CreateIconFromResourceEx(frame, (uint)frame.Length, true, IconVersion, width, height, 0);

        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var icon = Icon.FromHandle(handle);
            using var bitmap = icon.ToBitmap();

            // ToBitmap hands back a shared format, so copy it into the one everything else expects.
            return bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppArgb);
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(
        byte[] resource,
        uint size,
        [MarshalAs(UnmanagedType.Bool)] bool isIcon,
        uint version,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}

/// <summary>Reads the icon resources stored in a program or library, without running it.</summary>
internal static class ProgramIcons
{
    private const uint LoadAsData = 0x00000002;
    private const uint LoadAsImageResource = 0x00000020;

    private const int IconResource = 3;
    private const int IconGroupResource = 14;

    private const int GroupHeaderSize = 6;
    private const int GroupEntrySize = 14;

    /// <summary>
    /// The largest real frame of one of a program's icons. A shortcut's icon index counts from
    /// the first icon, or names a resource when it's negative. If that icon isn't there,
    /// the first one stands in for it, as it does in Explorer.
    /// </summary>
    public static Bitmap? ReadLargest(string path, int index, ImageLimits limits)
    {
        var module = LoadLibraryEx(path, IntPtr.Zero, LoadAsData | LoadAsImageResource);

        if (module == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var groups = new List<IntPtr>();

            EnumResourceNames(module, IconGroupResource, (_, _, name, _) =>
            {
                groups.Add(IsId(name) ? name : Marshal.StringToHGlobalUni(Marshal.PtrToStringUni(name)));
                return true;
            }, IntPtr.Zero);

            try
            {
                if (groups.Count == 0)
                {
                    return null;
                }

                var chosen = index < 0
                    ? groups.FirstOrDefault(group => IsId(group) && group == -index)
                    : index < groups.Count ? groups[index] : IntPtr.Zero;

                return ReadGroup(module, chosen == IntPtr.Zero ? groups[0] : chosen, limits);
            }
            finally
            {
                foreach (var name in groups.Where(group => !IsId(group)))
                {
                    Marshal.FreeHGlobal(name);
                }
            }
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    /// <summary>A resource's name is either a small number or a pointer to text, told apart by size.</summary>
    private static bool IsId(IntPtr name) => (nint)name >> 16 == 0;

    private static Bitmap? ReadGroup(IntPtr module, IntPtr group, ImageLimits limits)
    {
        var directory = Resource(module, group, IconGroupResource);

        if (directory is null || directory.Length < GroupHeaderSize)
        {
            return null;
        }

        int count = BitConverter.ToUInt16(directory, 4);
        var entries = new List<(int Size, int Bits, ushort Id)>(count);

        for (var i = 0; i < count && GroupHeaderSize + ((i + 1) * GroupEntrySize) <= directory.Length; i++)
        {
            var offset = GroupHeaderSize + (i * GroupEntrySize);
            var width = directory[offset] == 0 ? 256 : directory[offset];
            entries.Add((width, BitConverter.ToUInt16(directory, offset + 6), BitConverter.ToUInt16(directory, offset + 12)));
        }

        foreach (var entry in entries.OrderByDescending(entry => entry.Size).ThenByDescending(entry => entry.Bits))
        {
            if (Resource(module, entry.Id, IconResource) is { } frame &&
                IconFrames.Decode(frame, entry.Size, entry.Size, limits) is { } bitmap)
            {
                return bitmap;
            }
        }

        return null;
    }

    private static byte[]? Resource(IntPtr module, IntPtr name, int type)
    {
        var found = FindResource(module, name, type);

        if (found == IntPtr.Zero)
        {
            return null;
        }

        var size = SizeofResource(module, found);
        var loaded = LoadResource(module, found);
        var pointer = loaded == IntPtr.Zero ? IntPtr.Zero : LockResource(loaded);

        if (pointer == IntPtr.Zero || size == 0)
        {
            return null;
        }

        var bytes = new byte[size];
        Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return bytes;
    }

    private delegate bool ResourceCallback(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadLibraryExW")]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "EnumResourceNamesW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumResourceNames(IntPtr module, nint type, ResourceCallback callback, IntPtr parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindResourceW")]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, nint type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr resource);
}

/// <summary>
/// Asks the shell for a file's picture, for anything that isn't a plain icon or program. Shell
/// objects expect a thread of their own kind, and a misbehaving handler can hang, so this
/// runs on its own thread and is abandoned if it takes too long.
/// </summary>
internal static class ShellImage
{
    private const int Size = 256;
    private const int IconOnly = 0x4;
    private const int TimeoutMilliseconds = 5000;

    private static readonly Guid ItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public static Bitmap? Read(string path)
    {
        Bitmap? result = null;

        var thread = new Thread(() => result = ReadOnThisThread(path)) { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return thread.Join(TimeoutMilliseconds) ? result : null;
    }

    private static Bitmap? ReadOnThisThread(string path)
    {
        object? item = null;
        var handle = IntPtr.Zero;

        try
        {
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ItemImageFactory, out item) != 0 ||
                ((IShellItemImageFactory)item).GetImage(new NativeSize(Size, Size), IconOnly, out handle) != 0)
            {
                return null;
            }

            return ToBitmap(handle);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or ExternalException)
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                DeleteObject(handle);
            }

            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }
        }
    }

    /// <summary>Copies the shell's picture, whose pixels come with their color already multiplied by their opacity.</summary>
    private static Bitmap ToBitmap(IntPtr handle)
    {
        using var source = Image.FromHbitmap(handle);
        var info = new BitmapHeader { Size = Marshal.SizeOf<BitmapHeader>(), Width = source.Width, Height = -source.Height, Planes = 1, BitCount = 32 };
        var pixels = new byte[source.Width * source.Height * 4];
        var context = CreateCompatibleDC(IntPtr.Zero);

        try
        {
            if (GetDIBits(context, handle, 0, (uint)source.Height, pixels, ref info, 0) == 0)
            {
                throw new ExternalException("The shell's picture could not be read.");
            }
        }
        finally
        {
            DeleteDC(context);
        }

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];

            if (alpha is > 0 and < 255)
            {
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / alpha);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / alpha);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / alpha);
            }
        }

        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);

        return bitmap;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeSize(int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        in Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object item);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr context);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr context);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr context, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapHeader info, uint usage);
}
