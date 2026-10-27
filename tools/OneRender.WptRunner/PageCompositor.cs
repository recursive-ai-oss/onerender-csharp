using OneRender.Image;

namespace OneRender.WptRunner;

/// <summary>Assembles page images from the pieces a paged document is drawn in.</summary>
public static class PageCompositor
{
    /// <summary>A new opaque white page image.</summary>
    public static RgbaImage White(int width, int height)
    {
        byte[] px = new byte[checked(width * height * 4)];
        Array.Fill(px, (byte)255);
        return new RgbaImage(width, height, px);
    }

    /// <summary>
    /// Draws the rectangle (<paramref name="sx"/>, <paramref name="sy"/>, <paramref name="w"/>, <paramref name="h"/>) of <paramref name="src"/>
    /// over <paramref name="dst"/> with its top-left at (<paramref name="dx"/>, <paramref name="dy"/>), blending by the source's straight alpha.
    /// Parts outside either image are skipped.
    /// </summary>
    public static void Over(RgbaImage dst, RgbaImage src, int sx, int sy, int w, int h, int dx, int dy)
    {
        for (int y = 0; y < h; y++)
        {
            int syy = sy + y, dyy = dy + y;
            if (syy < 0 || syy >= src.Height || dyy < 0 || dyy >= dst.Height)
            {
                continue;
            }

            for (int x = 0; x < w; x++)
            {
                int sxx = sx + x, dxx = dx + x;
                if (sxx < 0 || sxx >= src.Width || dxx < 0 || dxx >= dst.Width)
                {
                    continue;
                }

                int s = (syy * src.Width + sxx) * 4, d = (dyy * dst.Width + dxx) * 4;
                int a = src.Pixels[s + 3];
                if (a == 0)
                {
                    continue;
                }

                for (int c = 0; c < 3; c++)
                {
                    dst.Pixels[d + c] = (byte)((src.Pixels[s + c] * a + dst.Pixels[d + c] * (255 - a) + 127) / 255);
                }

                dst.Pixels[d + 3] = (byte)Math.Min(255, a + dst.Pixels[d + 3] * (255 - a) / 255);
            }
        }
    }
}
