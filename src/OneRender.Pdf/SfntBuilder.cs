using System.Buffers.Binary;
using OneFont;

namespace OneRender.Pdf;

/// <summary>
/// Reassembles a single-face font file from the tables of a loaded <see cref="FontFace"/>. The engine keeps faces, not the files they
/// came from (a WOFF2 face has been decoded, a collection has been cut down to one face), and PDF embedding wants a font file.
/// The tables are copied unchanged; only the directory is written again. The shipped font files are never modified.
/// </summary>
internal static class SfntBuilder
{
    // The tables a PDF font program needs (ISO 32000-2 9.9, OpenType 1.9): outlines, metrics, naming, the character map the
    // embedder reads to find fsType, and the TrueType hinting programs. Layout tables are left out: the shaper has already run.
    private static readonly string[] s_tags =
    [
        "CFF ", "OS/2", "cmap", "cvt ", "fpgm", "gasp", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "name", "post", "prep",
    ];

    /// <summary>Builds the font file bytes, or null when the face lacks the tables of an embeddable font.</summary>
    public static byte[]? Build(FontFace face)
    {
        var tables = new List<(string Tag, ReadOnlyMemory<byte> Data)>(s_tags.Length);
        foreach (string tag in s_tags)
        {
            ReadOnlyMemory<byte> data = face.GetTable(tag);
            if (!data.IsEmpty)
            {
                tables.Add((tag, data));
            }
        }

        // Tags are sorted ascending as bytes, which the directory requires for binary search (OpenType: Table Directory).
        tables.Sort(static (a, b) => string.CompareOrdinal(a.Tag, b.Tag));
        bool hasHead = false, hasMaxp = false, hasOutlines = false;
        foreach ((string tag, _) in tables)
        {
            hasHead |= tag == "head";
            hasMaxp |= tag == "maxp";
            hasOutlines |= tag == "glyf" || tag == "CFF ";
        }

        if (!hasHead || !hasMaxp || !hasOutlines)
        {
            return null;
        }

        int n = tables.Count;
        long total = 12 + (16L * n);
        foreach ((_, ReadOnlyMemory<byte> d) in tables)
        {
            total += (d.Length + 3L) & ~3L;
        }

        if (total > int.MaxValue / 2)
        {
            return null;
        }

        byte[] file = new byte[total];
        Span<byte> f = file;
        BinaryPrimitives.WriteUInt32BigEndian(f, face.IsCff ? 0x4F54544Fu : 0x00010000u);
        int entrySelectorPow = 1, entrySelector = 0;
        while (entrySelectorPow * 2 <= n)
        {
            entrySelectorPow *= 2;
            entrySelector++;
        }

        BinaryPrimitives.WriteUInt16BigEndian(f[4..], (ushort)n);
        BinaryPrimitives.WriteUInt16BigEndian(f[6..], (ushort)(entrySelectorPow * 16));
        BinaryPrimitives.WriteUInt16BigEndian(f[8..], (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(f[10..], (ushort)((n * 16) - (entrySelectorPow * 16)));
        int offset = 12 + (16 * n);
        int headAt = -1;
        for (int i = 0; i < n; i++)
        {
            (string tag, ReadOnlyMemory<byte> data) = tables[i];
            Span<byte> entry = f.Slice(12 + (16 * i), 16);
            for (int c = 0; c < 4; c++)
            {
                entry[c] = (byte)tag[c];
            }

            data.Span.CopyTo(f[offset..]);
            if (tag == "head" && data.Length >= 12)
            {
                headAt = offset;
                BinaryPrimitives.WriteUInt32BigEndian(f[(offset + 8)..], 0); // checkSumAdjustment is computed over the whole file below
            }

            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], Checksum(f.Slice(offset, (data.Length + 3) & ~3)));
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(entry[12..], (uint)data.Length);
            offset += (data.Length + 3) & ~3;
        }

        if (headAt >= 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(f[(headAt + 8)..], unchecked(0xB1B0AFBAu - Checksum(f)));
        }

        return file;
    }

    private static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 4 <= bytes.Length; i += 4)
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(bytes[i..]));
        }

        if (i < bytes.Length)
        {
            uint last = 0;
            for (int k = 0; i + k < bytes.Length; k++)
            {
                last |= (uint)bytes[i + k] << (24 - (8 * k));
            }

            sum = unchecked(sum + last);
        }

        return sum;
    }
}
