using System.Numerics;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Pdf.Tests;

/// <summary>The directly computed picture of a blurred box shadow against the exact Gaussian and against the raster backend.</summary>
public class ShadowPictureTests
{
    private static readonly RectF Box = new(40, 30, 220, 140);

    private static double Erf(double x)
    {
        // Abramowitz and Stegun 7.1.26, error under 1.5e-7.
        double t = 1 / (1 + (0.3275911 * Math.Abs(x)));
        double poly = ((((((((1.061405429 * t) - 1.453152027) * t) + 1.421413741) * t) - 0.284496736) * t) + 0.254829592) * t;
        double y = 1 - (poly * Math.Exp(-x * x));
        return x >= 0 ? y : -y;
    }

    private static double Cdf(double x, double sigma) => 0.5 * (1 + Erf(x / (sigma * Math.Sqrt(2))));

    private static (DisplayList, DisplayItem, RectF, int, int) Make(CornerRadii radii, float dx, float dy, float blur, float spread, float scale, bool inset)
    {
        var list = new DisplayList();
        list.DrawBoxShadow(ItemId.Of(1, 1, 1), Box, radii, new PaintColor(0f, 0f, 0f, 1f), dx, dy, blur, spread, inset);
        DisplayItem item = list[0];

        // An island with a margin of three deviations all round, as the writer's has for an outer shadow.
        RectF b = item.LocalBounds;
        float margin = (1.5f * blur) + 2f;
        var island = new RectF(b.X - margin, b.Y - margin, b.Width + (2f * margin), b.Height + (2f * margin));
        return (list, item, island, (int)Math.Ceiling(island.Width * scale), (int)Math.Ceiling(island.Height * scale));
    }

    [Theory]
    [InlineData(0f, 1f, 3f, 0f, 1f, false)]
    [InlineData(0f, 10f, 15f, -3f, 0.4f, false)]
    [InlineData(4f, 6f, 24f, 2f, 0.25f, false)]
    [InlineData(2f, 2f, 4f, 0f, 0.75f, true)]
    [InlineData(3f, 3f, 10f, 1f, 0.3f, true)]
    public void SquareShadowsAreTheExactGaussian(float dx, float dy, float blur, float spread, float scale, bool inset)
    {
        (_, DisplayItem item, RectF island, int pw, int ph) = Make(default, dx, dy, blur, spread, scale, inset);
        byte[] mine = ShadowPicture.Render(in item, island, pw, ph);
        double sigma = blur / 2;
        double sgn = inset ? -1 : 1;
        double l = Box.X + dx + (sgn * -spread), r = Box.Right + dx + (sgn * spread), t = Box.Y + dy + (sgn * -spread), bo = Box.Bottom + dy + (sgn * spread);
        double sx = pw / island.Width, sy = ph / island.Height;
        double worst = 0;
        int compared = 0;
        for (int j = 0; j < ph; j++)
        {
            for (int i = 0; i < pw; i++)
            {
                double x = island.X + ((i + 0.5) / sx), y = island.Y + ((j + 0.5) / sy);

                // The writer clips the picture at the box edge, so compare only what stays visible, a pixel back from the edge.
                double m = 1.5 / scale;
                bool inside = x > Box.X + m && x < Box.Right - m && y > Box.Y + m && y < Box.Bottom - m;
                bool outside = x < Box.X - m || x > Box.Right + m || y < Box.Y - m || y > Box.Bottom + m;
                if (inset ? !inside : !outside)
                {
                    continue;
                }

                double cover = (Cdf(x - l, sigma) - Cdf(x - r, sigma)) * (Cdf(y - t, sigma) - Cdf(y - bo, sigma));
                double truth = 255 * (inset ? 1 - cover : cover);
                worst = Math.Max(worst, Math.Abs(mine[(j * pw) + i] - truth));
                compared++;
            }
        }

        Assert.True(compared > 100);
        Assert.True(worst <= 5, "the largest alpha difference is " + worst + " of 255");
    }

    [Fact]
    public void RoundedShadowMatchesTheRasterBackend()
    {
        var radii = new CornerRadii(12, 12, 12, 12, 12, 12, 12, 12);
        (DisplayList list, DisplayItem item, RectF island, int pw, int ph) = Make(radii, 0, 4, 12, -2, 1f, false);
        byte[] mine = ShadowPicture.Render(in item, island, pw, ph);
        var sub = new DisplayList();
        sub.PushTransform(ItemId.Of(uint.MaxValue, ushort.MaxValue, ushort.MaxValue), Matrix3x2.CreateTranslation(-island.X, -island.Y));
        DisplayListCopier.Copy(list, 0, 0, sub);
        sub.PopTransform();
        RgbaImage reference = DisplayListRasterizer.Render(sub, pw, ph, new RasterOptions { Background = PaintColor.Transparent }, default);
        int worst = 0, compared = 0;
        for (int j = 0; j < ph; j++)
        {
            for (int i = 0; i < pw; i++)
            {
                float x = island.X + i + 0.5f, y = island.Y + j + 0.5f;
                if (x > Box.X - 2 && x < Box.Right + 2 && y > Box.Y - 2 && y < Box.Bottom + 2)
                {
                    continue;
                }

                int o = (((j * pw) + i) * 4) + 3;
                worst = Math.Max(worst, Math.Abs(mine[(j * pw) + i] - reference.Pixels[o]));
                compared++;
            }
        }

        Assert.True(compared > 100);
        Assert.True(worst <= 6, "the largest alpha difference is " + worst + " of 255");
    }

    [Fact]
    public void ABadPictureSizeOrIslandIsRefusedWithTheRasterizersException()
    {
        var list = new DisplayList();
        list.DrawBoxShadow(ItemId.Of(1, 1, 1), Box, default, new PaintColor(0f, 0f, 0f, 1f), 0, 0, 8, 0, false);
        DisplayItem item = list[0];
        Assert.Throws<RasterizationException>(() => ShadowPicture.Render(in item, new RectF(0, 0, 10, 10), 5000, 5000));
        Assert.Throws<RasterizationException>(() => ShadowPicture.Render(in item, new RectF(0, 0, float.NaN, 10), 10, 10));
        Assert.Throws<RasterizationException>(() => ShadowPicture.Render(in item, new RectF(0, 0, 10, 10), 0, 10));
    }

    [Fact]
    public void AHugeBlurStillRendersAPictureWithinBounds()
    {
        var list = new DisplayList();
        list.DrawBoxShadow(ItemId.Of(1, 1, 1), Box, default, new PaintColor(0f, 0f, 0f, 1f), 0, 0, 1e7f, 0, false);
        DisplayItem item = list[0];
        byte[] a = ShadowPicture.Render(in item, new RectF(0, 0, 300, 200), 30, 20);
        Assert.Equal(600, a.Length);
    }
}
