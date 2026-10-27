namespace OneRender.GeomDiff;

/// <summary>
/// Structural similarity (Wang et al. 2004) of two equally sized RGBA images, on Rec. 601 luma. Uniform 8x8 windows moved four pixels
/// at a time, K1 = 0.01, K2 = 0.03, dynamic range 255. The score is the mean over windows, 1 for identical images.
/// </summary>
internal static class Ssim
{
    private const int Window = 8;
    private const int Stride = 4;
    private const double C1 = (0.01 * 255) * (0.01 * 255);
    private const double C2 = (0.03 * 255) * (0.03 * 255);

    /// <summary>Mean SSIM of the two images, and the fraction of pixels whose RGB differs at all.</summary>
    public static (double Ssim, double DifferingPixels) Compare(byte[] a, byte[] b, int width, int height)
    {
        if (a.Length != b.Length || a.Length != width * height * 4)
        {
            throw new ArgumentException("images differ in size");
        }
        float[] la = Luma(a);
        float[] lb = Luma(b);
        double sum = 0;
        int count = 0;
        for (int y = 0; y + Window <= height; y += Stride)
        {
            for (int x = 0; x + Window <= width; x += Stride)
            {
                double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
                for (int j = 0; j < Window; j++)
                {
                    int row = (y + j) * width + x;
                    for (int i = 0; i < Window; i++)
                    {
                        double p = la[row + i];
                        double q = lb[row + i];
                        sa += p;
                        sb += q;
                        saa += p * p;
                        sbb += q * q;
                        sab += p * q;
                    }
                }
                const double n = Window * Window;
                double ma = sa / n, mb = sb / n;
                double va = saa / n - ma * ma, vb = sbb / n - mb * mb, cov = sab / n - ma * mb;
                sum += (2 * ma * mb + C1) * (2 * cov + C2) / ((ma * ma + mb * mb + C1) * (va + vb + C2));
                count++;
            }
        }
        long differing = 0;
        for (int i = 0; i < a.Length; i += 4)
        {
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2])
            {
                differing++;
            }
        }
        // An image smaller than one window has no windows: identical pixels then score 1, anything else 0.
        double score = count == 0 ? (differing == 0 ? 1 : 0) : sum / count;
        return (score, (double)differing / ((long)width * height));
    }

    private static float[] Luma(byte[] rgba)
    {
        float[] l = new float[rgba.Length / 4];
        for (int i = 0; i < l.Length; i++)
        {
            l[i] = 0.299f * rgba[i * 4] + 0.587f * rgba[i * 4 + 1] + 0.114f * rgba[i * 4 + 2];
        }
        return l;
    }
}
