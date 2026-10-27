using System.Buffers.Binary;
using System.IO.Compression;

namespace OneRender.WptRunner.Chrome;

/// <summary>A decoded screenshot: tightly packed RGBA, rows top to bottom.</summary>
public sealed record Screenshot(int Width, int Height, byte[] Rgba);

/// <summary>A small PNG reader for the 8-bit, non-interlaced RGB and RGBA files Chrome produces.</summary>
public static class PngReader
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Decodes <paramref name="png"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not a supported PNG.</exception>
    public static Screenshot Decode(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG file.");
        }

        int width = 0, height = 0, channels = 0;
        using var idat = new MemoryStream();
        int pos = 8;
        while (pos + 12 <= png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos, 4));
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            int data = pos + 8;
            if (len < 0 || data + len + 4 > png.Length)
            {
                throw new InvalidDataException("Truncated PNG chunk.");
            }

            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(data, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(data + 4, 4));
                byte depth = png[data + 8], colorType = png[data + 9], interlace = png[data + 12];
                if (depth != 8 || interlace != 0 || (colorType != 2 && colorType != 6))
                {
                    throw new InvalidDataException("Unsupported PNG format.");
                }

                channels = colorType == 6 ? 4 : 3;
            }
            else if (type == "IDAT")
            {
                idat.Write(png, data, len);
            }
            else if (type == "IEND")
            {
                break;
            }

            pos = data + len + 4;
        }

        if (width < 1 || height < 1 || (long)width * height > 64_000_000)
        {
            throw new InvalidDataException("Bad PNG dimensions.");
        }

        int stride = width * channels;
        byte[] raw = new byte[(stride + 1) * height];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            z.ReadExactly(raw);
        }

        byte[] rgba = new byte[width * height * 4];
        byte[] prev = new byte[stride];
        byte[] cur = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            Array.Copy(raw, y * (stride + 1) + 1, cur, 0, stride);
            Unfilter(filter, cur, prev, channels);
            int o = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                rgba[o++] = cur[x * channels];
                rgba[o++] = cur[(x * channels) + 1];
                rgba[o++] = cur[(x * channels) + 2];
                rgba[o++] = channels == 4 ? cur[(x * channels) + 3] : (byte)255;
            }

            (prev, cur) = (cur, prev);
        }

        return new Screenshot(width, height, rgba);
    }

    private static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
    {
        for (int i = 0; i < cur.Length; i++)
        {
            int a = i >= bpp ? cur[i - bpp] : 0;
            int b = prev[i];
            int c = i >= bpp ? prev[i - bpp] : 0;
            int add = filter switch
            {
                0 => 0,
                1 => a,
                2 => b,
                3 => (a + b) >> 1,
                4 => Paeth(a, b, c),
                _ => throw new InvalidDataException("Bad PNG filter."),
            };
            cur[i] = (byte)(cur[i] + add);
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
