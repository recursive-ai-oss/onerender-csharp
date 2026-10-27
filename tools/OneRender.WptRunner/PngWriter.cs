using System.IO.Compression;
using OneRender.Image;

namespace OneRender.WptRunner;

/// <summary>Writes RGBA images as PNG files, for looking at what a failing test actually rendered.</summary>
public static class PngWriter
{
    private static readonly uint[] Crc = BuildTable();

    /// <summary>Saves <paramref name="image"/> to <paramref name="path"/> as an 8-bit RGBA PNG.</summary>
    public static void Save(string path, RgbaImage image)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            int stride = image.Width * 4;
            for (int y = 0; y < image.Height; y++)
            {
                z.WriteByte(0);
                z.Write(image.Pixels, y * stride, stride);
            }
        }

        using var fs = File.Create(path);
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] ihdr = new byte[13];
        WriteBe(ihdr, 0, (uint)image.Width);
        WriteBe(ihdr, 4, (uint)image.Height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(fs, "IHDR", ihdr);
        Chunk(fs, "IDAT", raw.ToArray());
        Chunk(fs, "IEND", []);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        byte[] head = new byte[8];
        WriteBe(head, 0, (uint)data.Length);
        for (int i = 0; i < 4; i++)
        {
            head[4 + i] = (byte)type[i];
        }

        s.Write(head);
        s.Write(data);
        uint c = 0xFFFFFFFF;
        foreach (byte b in head.AsSpan(4))
        {
            c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        foreach (byte b in data)
        {
            c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        byte[] tail = new byte[4];
        WriteBe(tail, 0, c ^ 0xFFFFFFFF);
        s.Write(tail);
    }

    private static void WriteBe(byte[] b, int at, uint v)
    {
        b[at] = (byte)(v >> 24);
        b[at + 1] = (byte)(v >> 16);
        b[at + 2] = (byte)(v >> 8);
        b[at + 3] = (byte)v;
    }

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            t[n] = c;
        }

        return t;
    }
}

/// <summary>Saves every image it renders into a directory, named after the document path.</summary>
internal sealed class DumpingRenderer : IReftestRenderer
{
    private readonly IReftestRenderer _inner;
    private readonly string _dir;

    public DumpingRenderer(IReftestRenderer inner, string dir)
    {
        _inner = inner;
        _dir = dir;
        Directory.CreateDirectory(dir);
    }

    public RgbaImage Render(string wptPath)
    {
        RgbaImage img = _inner.Render(wptPath);
        PngWriter.Save(Path.Combine(_dir, wptPath.Replace('/', '_') + ".png"), img);
        return img;
    }
}
