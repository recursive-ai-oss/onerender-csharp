using System.Buffers.Binary;
using OneFont;

namespace OneRender.Text;

/// <summary>
/// One color glyph stored as an embedded PNG in a font's <c>CBDT</c>/<c>CBLC</c> tables (the format of Noto Color Emoji), with the
/// metrics that place it. All lengths are in bitmap pixels of the strike it came from; multiply by <c>fontSize / Ppem</c> to get CSS px.
/// </summary>
/// <param name="Png">The PNG file for the glyph. A view into the font's table; valid as long as the font data is.</param>
/// <param name="Width">The bitmap width in pixels.</param>
/// <param name="Height">The bitmap height in pixels.</param>
/// <param name="BearingX">The distance from the pen position to the bitmap's left edge, in bitmap pixels (positive right).</param>
/// <param name="BearingY">The distance from the baseline to the bitmap's top edge, in bitmap pixels (positive up).</param>
/// <param name="Ppem">The em size of the strike in pixels (the bitmap pixels per em).</param>
public readonly record struct ColorBitmapGlyph(ReadOnlyMemory<byte> Png, int Width, int Height, int BearingX, int BearingY, int Ppem);

/// <summary>
/// Reads color bitmap glyphs (OpenType <c>CBLC</c> locations and <c>CBDT</c> data, image formats 17 and 18 with PNG data) out of a
/// font. Fonts without those tables, and glyphs without a bitmap, simply have none. Every offset is range-checked; a damaged table
/// yields no glyph, never an exception.
/// </summary>
public static class ColorBitmapGlyphs
{
    private const int MaxStrikes = 64;
    private const int MaxSubtables = 1 << 16;

    /// <summary>Tells whether the font has color bitmap tables, so a text backend can skip the per-glyph lookup for ordinary fonts.</summary>
    /// <param name="face">The font face.</param>
    /// <returns>True when both <c>CBDT</c> and <c>CBLC</c> are present.</returns>
    public static bool HasBitmaps(FontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        return face.HasTable("CBLC") && face.HasTable("CBDT");
    }

    /// <summary>Finds the bitmap for a glyph in the largest strike that has one.</summary>
    /// <param name="face">The font face.</param>
    /// <param name="glyphId">The glyph id.</param>
    /// <param name="glyph">The bitmap and its metrics when found.</param>
    /// <returns>True when the font has a PNG bitmap for the glyph.</returns>
    public static bool TryGet(FontFace face, int glyphId, out ColorBitmapGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(face);
        return TryGet(face.GetTable("CBLC"), face.GetTable("CBDT"), glyphId, out glyph);
    }

    /// <summary>Finds the bitmap for a glyph in the largest strike that has one, from the raw bytes of the two tables.</summary>
    /// <param name="cblc">The <c>CBLC</c> table (bitmap locations).</param>
    /// <param name="cbdt">The <c>CBDT</c> table (bitmap data).</param>
    /// <param name="glyphId">The glyph id.</param>
    /// <param name="glyph">The bitmap and its metrics when found; its <see cref="ColorBitmapGlyph.Png"/> is a view of <paramref name="cbdt"/>.</param>
    /// <returns>True when the tables hold a PNG bitmap for the glyph. Damaged tables never throw; they just have no glyph.</returns>
    public static bool TryGet(ReadOnlyMemory<byte> cblc, ReadOnlyMemory<byte> cbdt, int glyphId, out ColorBitmapGlyph glyph)
    {
        glyph = default;
        if (glyphId is < 0 or > 0xFFFF)
        {
            return false;
        }

        ReadOnlyMemory<byte> dataMem = cbdt;
        ReadOnlySpan<byte> loc = cblc.Span;
        ReadOnlySpan<byte> data = dataMem.Span;
        if (loc.Length < 8 || data.Length < 4)
        {
            return false;
        }

        uint numSizes = BinaryPrimitives.ReadUInt32BigEndian(loc[4..]);
        if (numSizes > MaxStrikes)
        {
            numSizes = MaxStrikes;
        }

        // The strike with the most pixels per em that holds the glyph, so scaling only ever shrinks the bitmap.
        int bestPpem = -1;
        for (int s = 0; s < numSizes; s++)
        {
            int rec = 8 + (s * 48);
            if (rec + 48 > loc.Length)
            {
                break;
            }

            int ppem = loc[rec + 44];
            int first = BinaryPrimitives.ReadUInt16BigEndian(loc[(rec + 40)..]);
            int last = BinaryPrimitives.ReadUInt16BigEndian(loc[(rec + 42)..]);
            if (ppem <= bestPpem || glyphId < first || glyphId > last)
            {
                continue;
            }

            if (TryStrike(loc, data, dataMem, rec, glyphId, ppem, out ColorBitmapGlyph g))
            {
                glyph = g;
                bestPpem = ppem;
            }
        }

        return bestPpem >= 0;
    }

    private static bool TryStrike(ReadOnlySpan<byte> loc, ReadOnlySpan<byte> data, ReadOnlyMemory<byte> dataMem, int rec, int glyphId, int ppem, out ColorBitmapGlyph glyph)
    {
        glyph = default;
        long arrayOffset = BinaryPrimitives.ReadUInt32BigEndian(loc[rec..]);
        uint count = BinaryPrimitives.ReadUInt32BigEndian(loc[(rec + 8)..]);
        if (count > MaxSubtables)
        {
            return false;
        }

        for (uint i = 0; i < count; i++)
        {
            long e = arrayOffset + (i * 8L);
            if (e + 8 > loc.Length)
            {
                return false;
            }

            int first = BinaryPrimitives.ReadUInt16BigEndian(loc[(int)e..]);
            int last = BinaryPrimitives.ReadUInt16BigEndian(loc[(int)(e + 2)..]);
            if (glyphId < first || glyphId > last)
            {
                continue;
            }

            long sub = arrayOffset + BinaryPrimitives.ReadUInt32BigEndian(loc[(int)(e + 4)..]);
            if (sub + 8 > loc.Length)
            {
                return false;
            }

            int indexFormat = BinaryPrimitives.ReadUInt16BigEndian(loc[(int)sub..]);
            int imageFormat = BinaryPrimitives.ReadUInt16BigEndian(loc[(int)(sub + 2)..]);
            long imageBase = BinaryPrimitives.ReadUInt32BigEndian(loc[(int)(sub + 4)..]);
            int n = glyphId - first;
            long start;
            long end;
            switch (indexFormat)
            {
                case 1: // 32-bit offsets
                    {
                        long o = sub + 8 + (n * 4L);
                        if (o + 8 > loc.Length)
                        {
                            return false;
                        }

                        start = BinaryPrimitives.ReadUInt32BigEndian(loc[(int)o..]);
                        end = BinaryPrimitives.ReadUInt32BigEndian(loc[(int)(o + 4)..]);
                        break;
                    }

                case 3: // 16-bit offsets
                    {
                        long o = sub + 8 + (n * 2L);
                        if (o + 4 > loc.Length)
                        {
                            return false;
                        }

                        start = BinaryPrimitives.ReadUInt16BigEndian(loc[(int)o..]);
                        end = BinaryPrimitives.ReadUInt16BigEndian(loc[(int)(o + 2)..]);
                        break;
                    }

                default:
                    return false;
            }

            if (end <= start)
            {
                return false; // an empty slot: this glyph has no bitmap here
            }

            long at = imageBase + start;
            long length = end - start;
            if (at < 0 || at + length > data.Length)
            {
                return false;
            }

            return ReadImage(data, dataMem, (int)at, (int)length, imageFormat, ppem, out glyph);
        }

        return false;
    }

    private static bool ReadImage(ReadOnlySpan<byte> data, ReadOnlyMemory<byte> dataMem, int at, int length, int imageFormat, int ppem, out ColorBitmapGlyph glyph)
    {
        glyph = default;
        int metrics;
        int height;
        int width;
        int bearingX;
        int bearingY;
        switch (imageFormat)
        {
            case 17: // smallGlyphMetrics (5 bytes) then the PNG length
                metrics = 5;
                if (length < metrics + 4)
                {
                    return false;
                }

                height = data[at];
                width = data[at + 1];
                bearingX = (sbyte)data[at + 2];
                bearingY = (sbyte)data[at + 3];
                break;
            case 18: // bigGlyphMetrics (8 bytes) then the PNG length
                metrics = 8;
                if (length < metrics + 4)
                {
                    return false;
                }

                height = data[at];
                width = data[at + 1];
                bearingX = (sbyte)data[at + 2];
                bearingY = (sbyte)data[at + 3];
                break;
            default:
                return false;
        }

        uint pngLength = BinaryPrimitives.ReadUInt32BigEndian(data[(at + metrics)..]);
        int pngAt = at + metrics + 4;
        if (pngLength == 0 || pngLength > length - metrics - 4 || width == 0 || height == 0 || ppem == 0)
        {
            return false;
        }

        glyph = new ColorBitmapGlyph(dataMem.Slice(pngAt, (int)pngLength), width, height, bearingX, bearingY, ppem);
        return true;
    }
}
