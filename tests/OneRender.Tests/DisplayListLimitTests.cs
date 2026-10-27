using System.Numerics;
using System.Threading;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Tests;

public class DisplayListLimitTests
{
    private static ItemId Id(uint n) => ItemId.Of(n);

    private static PaintColor Red => PaintColor.FromBytes(255, 0, 0);

    [Fact]
    public void NestingStopsAtTheLimitWithoutRecursion()
    {
        DisplayList l = new();
        for (uint i = 1; i <= DisplayListLimits.MaxNestingDepth; i++)
        {
            switch (i % 4)
            {
                case 0: l.PushClipRect(Id(i), new RectF(0, 0, 1e6f, 1e6f)); break;
                case 1: l.PushTransform(Id(i), Matrix3x2.Identity); break;
                case 2: l.PushGroup(Id(i), 1f); break;
                default: l.BeginTag(Id(i), TagRole.Div); break;
            }
        }

        Assert.Equal(DisplayListLimits.MaxNestingDepth, l.OpenDepth);
        Assert.Throws<DisplayListLimitException>(() => l.PushClipRect(Id(9999), new RectF(0, 0, 1, 1)));
        Assert.Throws<DisplayListException>(l.Validate);
        // Closing in the right order works.
        for (int i = DisplayListLimits.MaxNestingDepth; i >= 1; i--)
        {
            switch (i % 4)
            {
                case 0: l.PopClip(); break;
                case 1: l.PopTransform(); break;
                case 2: l.PopGroup(); break;
                default: l.EndTag(); break;
            }
        }

        l.Validate();
    }

    [Fact]
    public void ItemCountIsLimited()
    {
        DisplayList l = new();
        RectF r = new(0, 0, 1, 1);
        for (uint i = 1; i <= DisplayListLimits.MaxItems; i++)
        {
            l.FillRect(Id(i), r, Red);
        }

        Assert.Equal(DisplayListLimits.MaxItems, l.Count);
        Assert.Throws<DisplayListLimitException>(() => l.FillRect(Id(uint.MaxValue), r, Red));
        // The list is still usable after a refused add.
        Assert.Equal(DisplayListLimits.MaxItems, l.Count);
        l.Clear();
        l.FillRect(Id(1), r, Red);
        Assert.Equal(1, l.Count);
    }

    [Fact]
    public void MismatchedAndDuplicatePushesAreRejected()
    {
        DisplayList l = new();
        Assert.Throws<DisplayListException>(l.PopClip);
        l.PushGroup(Id(1), 0.5f);
        Assert.Throws<DisplayListException>(l.PopClip);
        Assert.Throws<DisplayListException>(l.EndTag);
        Assert.Throws<DisplayListException>(() => l.FillRect(Id(1), new RectF(0, 0, 1, 1), Red));
        Assert.Throws<DisplayListException>(() => l.FillRect(ItemId.None, new RectF(0, 0, 1, 1), Red));
        l.PopGroup();
        l.Validate();
        Assert.Equal(2, l.Count);
    }

    [Fact]
    public void NonFiniteAndHugeValuesAreRejectedAndLeaveTheListIntact()
    {
        DisplayList l = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => l.FillRect(Id(1), new RectF(float.NaN, 0, 1, 1), Red));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.FillRect(Id(1), new RectF(0, 0, float.PositiveInfinity, 1), Red));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.FillRect(Id(1), new RectF(2e9f, 0, 1, 1), Red));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.FillRect(Id(1), new RectF(0, 0, 1, 1), new PaintColor(float.NaN, 0, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.PushGroup(Id(1), float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.DrawBoxShadow(Id(1), new RectF(0, 0, 1, 1), CornerRadii.Zero, Red, 0, 0, -1, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.PushTransform(Id(1), new Matrix3x2(float.NaN, 0, 0, 1, 0, 0)));
        Assert.Equal(0, l.Count);
        l.FillRect(Id(1), new RectF(0, 0, 1, 1), Red);
        Assert.Equal(1, l.Count);
    }

    [Fact]
    public void GradientAndGlyphAndPathLimitsAreChecked()
    {
        DisplayList l = new();
        GradientStop[] many = new GradientStop[DisplayListLimits.MaxStopsPerGradient + 1];
        Assert.Throws<DisplayListLimitException>(() => l.LinearGradient(0, 0, 1, 1, many));
        Assert.Throws<DisplayListLimitException>(() => l.LinearGradient(0, 0, 1, 1, ReadOnlySpan<GradientStop>.Empty));
        Paint other = new DisplayList().LinearGradient(0, 0, 1, 1, [new GradientStop(0, Red)]);
        Assert.Throws<ArgumentException>(() => l.FillRect(Id(1), new RectF(0, 0, 1, 1), other));

        Assert.Throws<ArgumentException>(() => l.FillPath(Id(2), [(byte)PathVerb.Line], [1f], Red));
        Assert.Throws<ArgumentException>(() => l.FillPath(Id(2), [(byte)PathVerb.Move], [1f, 2f, 3f], Red));
        Assert.Throws<ArgumentException>(() => l.FillPath(Id(2), [(byte)99], [], Red));

        Assert.Throws<ArgumentException>(() => l.DrawGlyphRun(Id(3), Scenes.Sans, 10, 0, 0, Red, [1, 2], [0f], default, [0, 0], "ab"));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.DrawGlyphRun(Id(3), Scenes.Sans, 10, 0, 0, Red, [1], [0f], default, [5], "ab"));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.DrawGlyphRun(Id(3), Scenes.Sans, 0, 0, 0, Red, [1], [0f], default, [0], "ab"));
        Assert.Equal(0, l.Count);
    }

    [Fact]
    public void ADeepPathRingAndHugeCoordinatesRasterizeWithoutCrashing()
    {
        DisplayList l = new();
        l.FillRect(Id(1), new RectF(-5e8f, -5e8f, 1e9f, 1e9f), Red);
        l.FillRoundedRect(Id(2), new RectF(-1e8f, -1e8f, 2e8f, 2e8f), CornerRadii.Uniform(1e8f), PaintColor.Black);
        l.DrawBoxShadow(Id(3), new RectF(5, 5, 10, 10), CornerRadii.Uniform(4), PaintColor.Black, 0, 0, 1e6f, 1e6f, false);
        l.DrawBoxShadow(Id(4), new RectF(5, 5, 10, 10), CornerRadii.Uniform(4), PaintColor.Black, 1e8f, 1e8f, 50, 0, true);
        l.PushTransform(Id(5), new Matrix3x2(100, 0, 0, 100, 0, 0));
        l.DrawBorder(Id(6), new RectF(0, 0, 100, 100), CornerRadii.Uniform(30), new BorderSide(3, BorderStyle.Dotted, Red), new BorderSide(3, BorderStyle.Dashed, Red), new BorderSide(3, BorderStyle.Groove, Red), new BorderSide(3, BorderStyle.DoubleLine, Red));
        l.PopTransform();
        l.PushTransform(Id(7), new Matrix3x2(0, 0, 0, 0, 5, 5)); // a singular matrix paints nothing
        l.FillRect(Id(8), new RectF(0, 0, 10, 10), Red);
        l.PopTransform();
        RgbaImage img = DisplayListRasterizer.Render(l, 32, 32);
        Assert.Equal(32, img.Width);

        // Items that need path booleans accept device coordinates within +-2^25; beyond that the render fails with the typed exception.
        DisplayList far = new();
        far.PushTransform(Id(1), new Matrix3x2(1e6f, 0, 0, 1e6f, 0, 0));
        far.DrawBorder(Id(2), new RectF(0, 0, 100, 100), CornerRadii.Uniform(30), new BorderSide(3, BorderStyle.Dashed, Red), new BorderSide(3, BorderStyle.Dashed, Red), new BorderSide(3, BorderStyle.Dashed, Red), new BorderSide(3, BorderStyle.Dashed, Red));
        far.PopTransform();
        Assert.Throws<RasterizationException>(() => DisplayListRasterizer.Render(far, 32, 32));
    }

    [Fact]
    public void ASingularTransformDrawsNothingEvenWithGradientsAndGroupsInside()
    {
        // Found by fuzzing: a gradient under a transform that cannot be inverted used to throw from the rasterizer.
        DisplayList l = new();
        l.PushTransform(Id(1), new Matrix3x2(1e-30f, 0, 0, 1e-30f, 3, 3));
        Paint g = l.ConicGradient(0, 0, 0, [new GradientStop(0, Red), new GradientStop(1, PaintColor.Black)]);
        l.FillRect(Id(2), new RectF(0, 0, 1e9f, 1e9f), g);
        l.PushGroup(Id(3), 0.5f);
        l.FillRect(Id(4), new RectF(0, 0, 10, 10), g);
        l.PopGroup();
        l.PopTransform();
        RgbaImage img = DisplayListRasterizer.Render(l, 8, 8);
        Assert.All(img.Pixels, b => Assert.Equal(255, b));
    }

    [Fact]
    public void GroupsDeeperThanTheRasterizerAllowsFailCleanly()
    {
        DisplayList l = new();
        for (uint i = 1; i <= 40; i++)
        {
            l.PushGroup(Id(i), 0.9f);
        }

        l.FillRect(Id(100), new RectF(0, 0, 4, 4), Red);
        for (int i = 0; i < 40; i++)
        {
            l.PopGroup();
        }

        Assert.Throws<RasterizationException>(() => DisplayListRasterizer.Render(l, 16, 16));
    }

    [Fact]
    public void RasterizerChecksItsArguments()
    {
        DisplayList l = new();
        Assert.Throws<ArgumentNullException>(() => DisplayListRasterizer.Render(null!, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayListRasterizer.Render(l, 0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayListRasterizer.Render(l, 16385, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayListRasterizer.Render(l, 16384, 16384));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisplayListRasterizer.Render(l, 5, 5, new RasterOptions { DevicePixelRatio = float.NaN }));
        l.PushGroup(Id(1), 1f);
        Assert.Throws<DisplayListException>(() => DisplayListRasterizer.Render(l, 5, 5));
        l.PopGroup();
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => DisplayListRasterizer.Render(l, 5, 5, null, cts.Token));
    }

    [Fact]
    public void SeamStillReturnsBlankWhite()
    {
        RgbaImage img = HtmlRenderSeam.Render("<p>hi</p>", "", 20, 10);
        Assert.Equal(20, img.Width);
        Assert.Equal(10, img.Height);
        Assert.All(img.Pixels, b => Assert.Equal(255, b));
    }
}
