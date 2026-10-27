using System.Numerics;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Tests;

/// <summary>The effect item kinds of display list format 1.2: limits, nesting, in-place patching and damage.</summary>
public class DisplayListEffectsTests
{
    private static ItemId Id(uint n) => ItemId.Of(n);

    private static PaintColor Red => PaintColor.FromBytes(255, 0, 0);

    [Fact]
    public void FilterChainsAndBlursAreBounded()
    {
        DisplayList l = new();
        FilterOp[] tooMany = new FilterOp[DisplayList.MaxFilterOps + 1];
        Array.Fill(tooMany, new FilterOp(FilterOpKind.Invert, 1f));
        Assert.Throws<DisplayListLimitException>(() => l.PushFilter(Id(1), tooMany));
        Assert.Throws<DisplayListLimitException>(() => l.PushFilter(Id(1), ReadOnlySpan<FilterOp>.Empty));
        Assert.Throws<DisplayListLimitException>(() => l.PushFilter(Id(1), [new FilterOp(FilterOpKind.Blur, DisplayList.MaxBlur + 1)]));
        l.PushFilter(Id(2), tooMany.AsSpan(0, DisplayList.MaxFilterOps));
        l.PopFilter();
        l.Validate();
        Assert.Equal(2, l.RequiredMinor);
    }

    [Fact]
    public void MaskLayersAreBounded()
    {
        DisplayList l = new();
        l.PushMask(Id(1));
        for (uint i = 0; i < DisplayList.MaxMaskLayers; i++)
        {
            l.PushMaskLayer(Id(10 + i), MaskLayerMode.Alpha, MaskCompositeOp.Add);
            l.FillRect(Id(100 + i), new RectF(0, 0, 4, 4), Red);
            l.PopMaskLayer();
        }

        Assert.Throws<DisplayListLimitException>(() => l.PushMaskLayer(Id(999), MaskLayerMode.Alpha, MaskCompositeOp.Add));
        l.PopMask();
        l.Validate();
    }

    [Fact]
    public void OversizedSvgIsRefused()
    {
        DisplayList l = new();
        Assert.Throws<DisplayListLimitException>(() => l.SvgImage(Id(1), new RectF(0, 0, 10, 10), new string('x', DisplayList.MaxSvgChars + 1), Red));
    }

    [Fact]
    public void EffectScopesNestToTheDepthLimitAndCloseInOrder()
    {
        DisplayList l = new();
        FilterOp[] blur = [new FilterOp(FilterOpKind.Blur, 1f)];
        int opened = 0;
        for (uint i = 1; i <= DisplayListLimits.MaxNestingDepth; i++)
        {
            switch (i % 3)
            {
                case 0: l.PushFilter(Id(i), blur); break;
                case 1: l.PushTransform3D(Id(i), Matrix4x4.CreateRotationY(0.1f)); break;
                default: l.PushMask(Id(i)); break;
            }

            opened++;
        }

        Assert.Equal(DisplayListLimits.MaxNestingDepth, l.OpenDepth);
        Assert.Throws<DisplayListLimitException>(() => l.PushFilter(Id(9999), blur));
        Assert.Throws<DisplayListException>(l.PopFilter);
        for (int i = opened; i >= 1; i--)
        {
            switch (i % 3)
            {
                case 0: l.PopFilter(); break;
                case 1: l.PopTransform(); break;
                default: l.PopMask(); break;
            }
        }

        l.Validate();
    }

    [Fact]
    public void ADeepEffectStackRastersWithoutRecursionOrHugeLayers()
    {
        DisplayList l = new();
        DisplayList ok = Groups(30);
        RgbaImage img = DisplayListRasterizer.Render(ok, 32, 32);
        Assert.True(img.Pixels[((10 * 32) + 10) * 4] > 200);
        // More open layers than the rasterizer allows is a typed failure, not a crash or an unbounded allocation.
        Assert.Throws<RasterizationException>(() => DisplayListRasterizer.Render(Groups(100), 32, 32));
    }

    [Fact]
    public void PatchingA3DTransformMovesTheDamageAndNothingElse()
    {
        DisplayList a = Build(Persp(10));
        DisplayList b = Build(Persp(30));
        DamageRegion d = new();
        DisplayList.ComputeDamage(a, b, d);
        Assert.False(d.IsEmpty);
        Assert.True(d.Bounds.X <= 10 && d.Bounds.Right >= 45);

        DamageRegion patched = new();
        a.PatchTransform3D(Id(1), Persp(30), patched);
        Assert.False(patched.IsEmpty);
        DamageRegion none = new();
        DisplayList.ComputeDamage(a, b, none);
        Assert.True(none.IsEmpty);
        Assert.ThrowsAny<ArgumentException>(() => a.PatchTransform3D(Id(1), new Matrix4x4(float.NaN, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)));
    }

    [Fact]
    public void AFilterOutsetWidensTheDamageOfItsContent()
    {
        DisplayList a = new();
        a.PushFilter(Id(1), [new FilterOp(FilterOpKind.Blur, 5f)]);
        a.FillRect(Id(2), new RectF(20, 20, 10, 10), Red);
        a.PopFilter();
        DisplayList b = new();
        b.PushFilter(Id(1), [new FilterOp(FilterOpKind.Blur, 5f)]);
        b.FillRect(Id(2), new RectF(20, 20, 10, 10), PaintColor.FromBytes(0, 0, 255));
        b.PopFilter();
        DamageRegion d = new();
        DisplayList.ComputeDamage(a, b, d);
        Assert.True(d.Bounds.X < 12 && d.Bounds.Right > 38, "blurred ink reaches 3 sigma past the box: " + d.Bounds);
    }

    private static DisplayList Groups(int n)
    {
        DisplayList l = new();
        for (uint i = 1; i <= n; i++)
        {
            l.PushGroup(Id(i), 0.99f);
        }

        l.FillRect(Id(500), new RectF(0, 0, 20, 20), Red);
        for (int i = 0; i < n; i++)
        {
            l.PopGroup();
        }

        return l;
    }

    private static Matrix4x4 Persp(float tx)
    {
        Matrix4x4 m = Matrix4x4.CreateTranslation(tx, 0, 0);
        m.M14 = 0.001f;
        return m;
    }

    private static DisplayList Build(Matrix4x4 m)
    {
        DisplayList l = new();
        l.PushTransform3D(Id(1), m);
        l.FillRect(Id(2), new RectF(0, 0, 20, 20), Red);
        l.PopTransform();
        return l;
    }
}
