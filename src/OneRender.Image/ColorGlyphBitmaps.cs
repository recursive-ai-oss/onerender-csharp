using OneImage;

namespace OneRender.Image;

/// <summary>Decodes the PNG bitmaps of color bitmap fonts (see <c>ColorBitmapGlyphs</c>) to pixels.</summary>
internal static class ColorGlyphBitmaps
{
    /// <summary>Decodes a PNG; null when it cannot be decoded (the glyph is then not drawn).</summary>
    public static RgbaImage? Decode(ReadOnlySpan<byte> png)
    {
        try
        {
            OneImage.Image img = OneImage.Image.Load(png.ToArray());
            Rgba32[] px = img.ToArray<Rgba32>();
            byte[] raw = new byte[px.Length * 4];
            for (int i = 0; i < px.Length; i++)
            {
                raw[i * 4] = px[i].R;
                raw[(i * 4) + 1] = px[i].G;
                raw[(i * 4) + 2] = px[i].B;
                raw[(i * 4) + 3] = px[i].A;
            }

            return new RgbaImage(img.Width, img.Height, raw);
        }
        catch (ImageException)
        {
            return null;
        }
    }
}
