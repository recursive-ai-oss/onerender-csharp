using OneImage;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Tests;

public class RasterGoldenTests
{
    private static RgbaImage Render(DisplayList l, int w, int h, float dpr = 1f, bool transparent = false)
    {
        RasterOptions o = new() { DevicePixelRatio = dpr, Images = new Scenes.OneImageResolver() };
        if (transparent)
        {
            o.Background = PaintColor.Transparent;
        }

        return DisplayListRasterizer.Render(l, w, h, o);
    }

    private static void Near(Rgba32 p, int r, int g, int b, int tol = 3)
    {
        Assert.True(Math.Abs(p.R - r) <= tol && Math.Abs(p.G - g) <= tol && Math.Abs(p.B - b) <= tol, $"expected ({r},{g},{b}) got ({p.R},{p.G},{p.B})");
    }

    [Fact]
    public void Fills()
    {
        RgbaImage img = Render(Scenes.Fills(), 200, 140);
        Golden.Check("fills", img);
        Near(Golden.Pixel(img, 30, 30), 200, 40, 40);
        Near(Golden.Pixel(img, 80, 10), 255, 255, 255); // outside the 30px elliptical corner
        Near(Golden.Pixel(img, 5, 5), 255, 255, 255);
    }

    [Fact]
    public void Borders()
    {
        RgbaImage img = Render(Scenes.Borders(), 240, 190);
        Golden.Check("borders", img);
        Near(Golden.Pixel(img, 12, 30), 60, 110, 190); // solid left side
        Near(Golden.Pixel(img, 32, 32), 255, 255, 255); // inside the border box
    }

    [Fact]
    public void BordersAtDeviceScale2()
    {
        RgbaImage img = Render(Scenes.Borders(), 480, 380, 2f);
        Golden.Check("borders-2x", img);
    }

    [Fact]
    public void Gradients()
    {
        RgbaImage img = Render(Scenes.Gradients(), 232, 192);
        Golden.Check("gradients", img);
        // Linear: ends take the end colors, the middle is the middle stop.
        Near(Golden.Pixel(img, 43, 33), 250, 220, 40, 8);
        // Hard stops: bands of exactly the stop colors.
        Near(Golden.Pixel(img, 90, 30), 220, 40, 40);
        Near(Golden.Pixel(img, 120, 30), 30, 170, 80);
        Near(Golden.Pixel(img, 150, 30), 40, 60, 220);
        // Radial center is the first stop.
        Near(Golden.Pixel(img, 121, 91), 250, 220, 40, 8);
        // Conic: just right of "up" is the first stop, a quarter turn is the second.
        Near(Golden.Pixel(img, 44, 128), 220, 40, 40, 20);
        Near(Golden.Pixel(img, 74, 154), 250, 220, 40, 20);
    }

    [Fact]
    public void Shadows()
    {
        RgbaImage img = Render(Scenes.Shadows(), 240, 200);
        Golden.Check("shadows", img);
        // The sharp outer shadow is not drawn under the box, and is drawn beside it.
        Near(Golden.Pixel(img, 40, 30), 255, 255, 255);
        Rgba32 side = Golden.Pixel(img, 69, 40);
        Assert.True(side.R < 120, "sharp outer shadow should be dark at the offset edge");
    }

    [Fact]
    public void Clips()
    {
        RgbaImage img = Render(Scenes.Clips(), 220, 130);
        Golden.Check("clips", img);
        Near(Golden.Pixel(img, 30, 30), 200, 40, 40);
        Near(Golden.Pixel(img, 70, 30), 235, 238, 242); // left of the nested clip
    }

    [Fact]
    public void Transforms()
    {
        RgbaImage img = Render(Scenes.Transforms(), 220, 130);
        Golden.Check("transforms", img);
        Near(Golden.Pixel(img, 30, 25), 200, 40, 40);
    }

    [Fact]
    public void Groups()
    {
        RgbaImage img = Render(Scenes.Groups(), 240, 190);
        Golden.Check("groups", img);
        // In a group the overlap shows only the top color (blue), not a mix with red.
        Rgba32 overlapGroup = Golden.Pixel(img, 45, 35);
        Rgba32 overlapNoGroup = Golden.Pixel(img, 137, 35);
        Assert.True(overlapGroup.B > overlapGroup.R + 20);
        Assert.True(overlapNoGroup.B > overlapNoGroup.R + 20);
        // Multiply of (250,200,60) and (60,120,220) is (59, 94, 52) in the overlap.
        Near(Golden.Pixel(img, 8 + 12, 100), 59, 94, 52, 4);
    }

    [Fact]
    public void Text()
    {
        RgbaImage img = Render(Scenes.Text(), 240, 140);
        Golden.Check("text", img);
        // Some dark ink exists in the first line.
        int dark = 0;
        for (int y = 10; y < 40; y++)
        {
            for (int x = 8; x < 150; x++)
            {
                if (Golden.Pixel(img, x, y).R < 100)
                {
                    dark++;
                }
            }
        }

        Assert.True(dark > 150);
    }

    [Fact]
    public void Images()
    {
        RgbaImage img = Render(Scenes.Images(), 200, 120);
        Golden.Check("images", img);
        // Pixelated: the top-left source pixel (255,0,0) covers the first 20 pixels of the 80 px destination.
        Near(Golden.Pixel(img, 110, 18), 255, 0, 0);
        // The transparent source pixel shows the backdrop.
        Near(Golden.Pixel(img, 170, 18), 235, 238, 242);
    }

    [Fact]
    public void TagsAreInvisibleAndContentInsideThemDraws()
    {
        DisplayList withTags = Scenes.Structure();
        RgbaImage a = Render(withTags, 100, 60);
        Golden.Check("structure", a);
        Near(Golden.Pixel(a, 20, 44), 0, 0, 200);
        Near(Golden.Pixel(a, 64, 44), 200, 0, 0);
    }

    [Fact]
    public void TransparentBackgroundKeepsAlpha()
    {
        DisplayList l = new();
        l.FillRect(Scenes.Id(1), new RectF(2, 2, 4, 4), PaintColor.Black);
        RgbaImage img = Render(l, 8, 8, transparent: true);
        Assert.Equal(0, Golden.Pixel(img, 0, 0).A);
        Assert.Equal(255, Golden.Pixel(img, 3, 3).A);
    }

    [Fact]
    public void OutputIsDeterministic()
    {
        foreach (Func<DisplayList> scene in new Func<DisplayList>[] { Scenes.Fills, Scenes.Borders, Scenes.Gradients, Scenes.Shadows, Scenes.Clips, Scenes.Transforms, Scenes.Groups, Scenes.Text, Scenes.Images })
        {
            DisplayList l = scene();
            RgbaImage a = Render(l, 240, 200);
            RgbaImage b = Render(l, 240, 200);
            Assert.True(a.Pixels.AsSpan().SequenceEqual(b.Pixels), "two renders of the same list differ");
        }
    }

    [Fact]
    public void RegionRedrawEqualsFullRedraw()
    {
        DisplayList l = Scenes.Groups();
        RgbaImage full = Render(l, 240, 190);
        RgbaImage partial = Render(Scenes.Fills(), 240, 190);
        DisplayListRasterizer.RenderRegion(l, partial, new RectI(0, 0, 240, 190), new RasterOptions { Images = new Scenes.OneImageResolver() });
        Assert.True(full.Pixels.AsSpan().SequenceEqual(partial.Pixels));

        // Redrawing a sub-region leaves the pixels outside it alone.
        RgbaImage other = Render(Scenes.Fills(), 240, 190);
        RgbaImage before = new(240, 190, (byte[])other.Pixels.Clone());
        DisplayListRasterizer.RenderRegion(l, other, new RectI(100, 80, 60, 50), new RasterOptions());
        for (int y = 0; y < 190; y++)
        {
            for (int x = 0; x < 240; x++)
            {
                bool inside = x >= 100 && x < 160 && y >= 80 && y < 130;
                Rgba32 o = Golden.Pixel(other, x, y);
                Rgba32 expect = inside ? Golden.Pixel(full, x, y) : Golden.Pixel(before, x, y);
                Assert.True(o == expect, $"pixel {x},{y} (inside={inside})");
            }
        }
    }
}
