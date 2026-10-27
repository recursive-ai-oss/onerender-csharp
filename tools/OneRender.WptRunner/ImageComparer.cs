using OneRender.Image;

namespace OneRender.WptRunner;

/// <summary>Pixel comparison as WPT's reftests define it.</summary>
public static class ImageComparer
{
    /// <summary>
    /// Compares two images. The maximum difference is the largest per-channel (R, G, B) absolute difference over all pixels;
    /// the pixel count is how many pixels differ in any of those channels. Images of different sizes never match.
    /// </summary>
    public static (int MaxDifference, long DifferentPixels) Diff(RgbaImage a, RgbaImage b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
        {
            return (int.MaxValue, long.MaxValue);
        }

        ReadOnlySpan<byte> x = a.Pixels, y = b.Pixels;
        if (x.SequenceEqual(y))
        {
            return (0, 0);
        }

        int max = 0;
        long count = 0;
        for (int i = 0; i < x.Length; i += 4)
        {
            int d = Math.Max(Math.Abs(x[i] - y[i]), Math.Max(Math.Abs(x[i + 1] - y[i + 1]), Math.Abs(x[i + 2] - y[i + 2])));
            if (d > 0)
            {
                count++;
                if (d > max)
                {
                    max = d;
                }
            }
        }

        return (max, count);
    }

    /// <summary>
    /// True when the images count as identical: exactly equal with no fuzzy allowance, otherwise the measured difference
    /// lies inside both fuzzy ranges (inclusive).
    /// </summary>
    public static bool AreEqual(RgbaImage a, RgbaImage b, FuzzySpec? fuzzy, out string detail)
    {
        (int max, long count) = Diff(a, b);
        detail = max == int.MaxValue
            ? $"size {a.Width}x{a.Height} vs {b.Width}x{b.Height}"
            : $"max {max}, {count} px";
        if (fuzzy is null)
        {
            return max == 0;
        }

        return max != int.MaxValue && fuzzy.MaxDifference.Contains(max) && fuzzy.TotalPixels.Contains(count);
    }

    /// <summary>
    /// True when nothing was painted: every pixel is the same fully transparent colour or the same opaque white.
    /// A passing comparison between such images proves nothing about an engine.
    /// </summary>
    public static bool IsBlank(RgbaImage image)
    {
        ReadOnlySpan<byte> p = image.Pixels;
        byte r = p[0], g = p[1], b = p[2], a = p[3];
        bool white = r == 255 && g == 255 && b == 255 && a == 255;
        if (!white && a != 0)
        {
            return false;
        }

        for (int i = 4; i < p.Length; i += 4)
        {
            if (p[i] != r || p[i + 1] != g || p[i + 2] != b || p[i + 3] != a)
            {
                return false;
            }
        }

        return true;
    }
}
