using OneImage;

namespace OneRender.Image;

/// <summary>
/// Decodes the PNG bitmaps of color bitmap fonts (the <c>CBDT</c> glyph images the text service returns as
/// <c>ColorBitmapGlyph</c>) to straight-alpha RGBA pixels. Backends other than the raster one use it to draw those glyphs.
/// </summary>
public static class ColorGlyphBitmaps
{
    /// <summary>Decodes a PNG glyph bitmap.</summary>
    /// <param name="png">The PNG bytes of one glyph image.</param>
    /// <returns>The pixels, 8 bits per channel with straight alpha; <see langword="null"/> when the data is not a decodable image, in which case the glyph is not drawn.</returns>
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
