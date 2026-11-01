using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Foldcraft.Core.Icons;

/// <summary>One image inside an .ico file.</summary>
public sealed record IcoFrame(int Width, int Height, int BitsPerPixel, bool IsPng, int Offset, int Length);

/// <summary>
/// Reads and writes the .ico container by hand. Windows expects frames up to 64 px as
/// 32 bpp DIBs with an AND mask, and the 256 px frame as PNG to keep the file small.
/// </summary>
public static class IcoFile
{
    private const int DirectoryHeaderSize = 6;
    private const int DirectoryEntrySize = 16;
    private const int BitmapInfoHeaderSize = 40;
    private const int FirstPngSize = 256;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Encodes square bitmaps as an .ico. Each must be 32 bpp ARGB.</summary>
    public static byte[] Write(IReadOnlyList<Bitmap> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var encoded = frames.Select(frame => frame.Width >= FirstPngSize ? EncodePng(frame) : EncodeDib(frame)).ToArray();
        var headerSize = DirectoryHeaderSize + (DirectoryEntrySize * frames.Count);
        var bytes = new byte[headerSize + encoded.Sum(e => e.Length)];
        var span = bytes.AsSpan();

        BinaryPrimitives.WriteUInt16LittleEndian(span[0..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)frames.Count);

        var offset = headerSize;

        for (var i = 0; i < frames.Count; i++)
        {
            var entry = span.Slice(DirectoryHeaderSize + (DirectoryEntrySize * i), DirectoryEntrySize);

            // 256 does not fit in a byte; the format stores it as 0.
            entry[0] = (byte)(frames[i].Width >= FirstPngSize ? 0 : frames[i].Width);
            entry[1] = (byte)(frames[i].Height >= FirstPngSize ? 0 : frames[i].Height);
            entry[2] = 0;
            entry[3] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], encoded[i].Length);
            BinaryPrimitives.WriteInt32LittleEndian(entry[12..], offset);

            encoded[i].CopyTo(span[offset..]);
            offset += encoded[i].Length;
        }

        return bytes;
    }

    /// <summary>Parses the directory. Returns null if the bytes are not a well-formed icon.</summary>
    public static IReadOnlyList<IcoFrame>? TryReadFrames(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < DirectoryHeaderSize ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]) != 1)
        {
            return null;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);

        if (count == 0 || bytes.Length < DirectoryHeaderSize + (DirectoryEntrySize * count))
        {
            return null;
        }

        var frames = new List<IcoFrame>(count);

        for (var i = 0; i < count; i++)
        {
            var entry = bytes.Slice(DirectoryHeaderSize + (DirectoryEntrySize * i), DirectoryEntrySize);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);

            if (length <= 0 || offset < 0 || (long)offset + length > bytes.Length)
            {
                return null;
            }

            frames.Add(new IcoFrame(
                entry[0] == 0 ? FirstPngSize : entry[0],
                entry[1] == 0 ? FirstPngSize : entry[1],
                BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]),
                bytes.Slice(offset, length).StartsWith(PngSignature),
                offset,
                length));
        }

        return frames;
    }

    private static byte[] EncodePng(Bitmap frame)
    {
        using var stream = new MemoryStream();
        frame.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>A BITMAPINFOHEADER, then bottom-up BGRA rows, then a bottom-up 1 bpp AND mask.</summary>
    private static byte[] EncodeDib(Bitmap frame)
    {
        var (width, height) = (frame.Width, frame.Height);
        var colorSize = width * height * 4;
        var maskStride = ((width + 31) / 32) * 4;
        var maskSize = maskStride * height;
        var bytes = new byte[BitmapInfoHeaderSize + colorSize + maskSize];
        var span = bytes.AsSpan();

        BinaryPrimitives.WriteInt32LittleEndian(span[0..], BitmapInfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], height * 2); // Color plus mask, by convention.
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], colorSize + maskSize);

        var data = frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var row = new byte[width * 4];

            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, row.Length);

                var flipped = height - 1 - y;
                row.CopyTo(span.Slice(BitmapInfoHeaderSize + (flipped * width * 4), row.Length));

                var mask = span.Slice(BitmapInfoHeaderSize + colorSize + (flipped * maskStride), maskStride);

                for (var x = 0; x < width; x++)
                {
                    if (row[(x * 4) + 3] == 0)
                    {
                        mask[x / 8] |= (byte)(0x80 >> (x % 8));
                    }
                }
            }
        }
        finally
        {
            frame.UnlockBits(data);
        }

        return bytes;
    }
}
