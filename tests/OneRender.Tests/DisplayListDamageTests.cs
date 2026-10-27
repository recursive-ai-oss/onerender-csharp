using System.Numerics;
using OneRender.Painting;
using OneRender.TestSupport;

namespace OneRender.Tests;

public class DisplayListDamageTests
{
    private static ItemId Id(uint n) => ItemId.Of(n);

    private static PaintColor Red => PaintColor.FromBytes(255, 0, 0);

    private static PaintColor Blue => PaintColor.FromBytes(0, 0, 255);

    private static DisplayList Base(PaintColor c1 = default, RectF? r1 = null, bool second = true)
    {
        DisplayList l = new();
        l.FillRect(Id(1), r1 ?? new RectF(10, 10, 20, 20), c1.A == 0 ? Red : c1);
        if (second)
        {
            l.FillRect(Id(2), new RectF(50, 10, 20, 20), Blue);
        }

        return l;
    }

    private static DamageRegion Diff(DisplayList a, DisplayList b)
    {
        DamageRegion d = new();
        DisplayList.ComputeDamage(a, b, d);
        return d;
    }

    [Fact]
    public void IdenticalListsHaveNoDamage()
    {
        Assert.True(Diff(Base(), Base()).IsEmpty);
    }

    [Fact]
    public void ChangedColorDamagesExactlyTheItem()
    {
        DamageRegion d = Diff(Base(), Base(Blue));
        Assert.Equal(1, d.Count);
        Assert.Equal(new RectF(10, 10, 20, 20), d.Bounds);
    }

    [Fact]
    public void MovedItemDamagesOldAndNewPositions()
    {
        DamageRegion d = Diff(Base(), Base(r1: new RectF(100, 100, 20, 20)));
        Assert.Equal(2, d.Count);
        Assert.Equal(new RectF(10, 10, 20, 20), d[0]);
        Assert.Equal(new RectF(100, 100, 20, 20), d[1]);
        Assert.Equal(new RectF(10, 10, 110, 110), d.Bounds);
    }

    [Fact]
    public void OverlappingOldAndNewMergeIntoOneRect()
    {
        DamageRegion d = Diff(Base(), Base(r1: new RectF(20, 10, 20, 20)));
        Assert.Equal(1, d.Count);
        Assert.Equal(new RectF(10, 10, 30, 20), d[0]);
    }

    [Fact]
    public void AddedItemDamagesItsBounds()
    {
        DisplayList b = Base();
        b.FillRect(Id(3), new RectF(5, 60, 10, 10), Red);
        DamageRegion d = Diff(Base(), b);
        Assert.Equal(1, d.Count);
        Assert.Equal(new RectF(5, 60, 10, 10), d[0]);
    }

    [Fact]
    public void RemovedItemDamagesItsOldBounds()
    {
        DamageRegion d = Diff(Base(), Base(second: false));
        Assert.Equal(1, d.Count);
        Assert.Equal(new RectF(50, 10, 20, 20), d[0]);
    }

    [Fact]
    public void DamageIsClippedAndTransformed()
    {
        // The rectangle is 100 wide, but only the part inside the clip, moved by the translation, can change.
        DisplayList Make(PaintColor c)
        {
            DisplayList l = new();
            l.PushTransform(Id(1), Matrix3x2.CreateTranslation(100, 200));
            l.PushClipRect(Id(2), new RectF(0, 0, 30, 30));
            l.FillRect(Id(3), new RectF(-10, 5, 100, 10), c);
            l.PopClip();
            l.PopTransform();
            return l;
        }

        DamageRegion d = Diff(Make(Red), Make(Blue));
        Assert.Equal(new RectF(100, 205, 30, 10), d.Bounds);
    }

    [Fact]
    public void ReorderingKeptItemsDamagesThem()
    {
        DisplayList a = new();
        a.FillRect(Id(1), new RectF(0, 0, 20, 20), Red);
        a.FillRect(Id(2), new RectF(10, 0, 20, 20), Blue);
        DisplayList b = new();
        b.FillRect(Id(2), new RectF(10, 0, 20, 20), Blue);
        b.FillRect(Id(1), new RectF(0, 0, 20, 20), Red);
        // Item 1 now paints over item 2, which changes the overlap; the overlap lies inside item 1's rectangle.
        Assert.Equal(new RectF(0, 0, 20, 20), Diff(a, b).Bounds);
    }

    [Fact]
    public void ChangingATagAloneCausesNoDamage()
    {
        DisplayList Make(string alt)
        {
            DisplayList l = new();
            l.BeginTag(Id(1), TagRole.Figure, altText: alt);
            l.FillRect(Id(2), new RectF(0, 0, 10, 10), Red);
            l.EndTag();
            return l;
        }

        Assert.True(Diff(Make("a"), Make("b")).IsEmpty);
    }

    [Fact]
    public void RetainedListAccumulatesDamageUntilCleared()
    {
        RetainedDisplayList r = new();
        DisplayList l = r.Begin();
        l.FillRect(Id(1), new RectF(10, 10, 20, 20), Red);
        r.Commit();
        Assert.Equal(new RectF(10, 10, 20, 20), r.Damage.Bounds);
        r.ClearDamage();

        l = r.Begin();
        l.FillRect(Id(1), new RectF(10, 10, 20, 20), Red);
        r.Commit();
        Assert.True(r.Damage.IsEmpty);

        l = r.Begin();
        l.FillRect(Id(1), new RectF(10, 10, 20, 20), Blue);
        r.Commit();
        Assert.Equal(new RectF(10, 10, 20, 20), r.Damage.Bounds);
        Assert.Equal(1, r.Current.Count);
    }

    [Fact]
    public void DamageRegionMergesAndCollapses()
    {
        DamageRegion d = new();
        for (int i = 0; i < DisplayListLimits.MaxDamageRects + 5; i++)
        {
            d.Add(new RectF(i * 100, 0, 10, 10));
        }

        Assert.True(d.Count <= DisplayListLimits.MaxDamageRects);
        Assert.Equal(new RectF(0, 0, ((DisplayListLimits.MaxDamageRects + 4) * 100) + 10, 10), d.Bounds);
        RectI px = d.ToDevicePixels(1.5f);
        Assert.Equal(0, px.X);
        Assert.Equal(15, px.Height);
    }

    // ---- patching

    private static DisplayList Animated(Matrix3x2 m, float opacity)
    {
        DisplayList l = new();
        l.FillRect(Id(1), new RectF(0, 0, 200, 200), PaintColor.White);
        l.PushTransform(Id(2), m);
        l.PushGroup(Id(3), opacity);
        l.FillRect(Id(4), new RectF(10, 10, 20, 20), Red);
        l.PushTransform(Id(5), Matrix3x2.CreateScale(2));
        l.FillRect(Id(6), new RectF(0, 0, 5, 5), Blue);
        l.PopTransform();
        l.PopGroup();
        l.PopTransform();
        return l;
    }

    [Fact]
    public void PatchedTransformGivesExactDamageAndEqualsRebuild()
    {
        DisplayList l = Animated(Matrix3x2.CreateTranslation(0, 0), 1f);
        DamageRegion d = new();
        l.PatchTransform(Id(2), Matrix3x2.CreateTranslation(30, 0), d);
        // Rect 4 moves from x=10..30 to 40..60; the scaled blue square from 0..10 to 30..40.
        // The old and new areas of the two items touch and merge into one rectangle.
        Assert.Equal(1, d.Count);
        Assert.Equal(new RectF(0, 0, 60, 30), d.Bounds);
        Assert.True(l.TryFind(Id(4), out int i4));
        Assert.Equal(new RectF(40, 10, 20, 20), l[i4].RootBounds);

        // A list built from scratch with the new value is identical to the patched one.
        Assert.True(Diff(l, Animated(Matrix3x2.CreateTranslation(30, 0), 1f)).IsEmpty);
        Assert.True(Diff(Animated(Matrix3x2.CreateTranslation(30, 0), 1f), l).IsEmpty);
    }

    [Fact]
    public void PatchedOpacityDamagesTheGroupContentAndEqualsRebuild()
    {
        DisplayList l = Animated(Matrix3x2.CreateTranslation(5, 5), 1f);
        DamageRegion d = new();
        l.PatchOpacity(Id(3), 0.25f, d);
        Assert.Equal(new RectF(5, 5, 30, 30), d.Bounds);
        Assert.True(Diff(l, Animated(Matrix3x2.CreateTranslation(5, 5), 0.25f)).IsEmpty);
        // A different opacity is a different list.
        Assert.False(Diff(l, Animated(Matrix3x2.CreateTranslation(5, 5), 0.5f)).IsEmpty);
        Assert.Equal(0.25f, l[l.TryFind(Id(3), out int ix) ? ix : 0].Opacity);
    }

    [Fact]
    public void PatchedOpacityDamageIsExactlyTheContent()
    {
        DisplayList l = new();
        l.PushGroup(Id(1), 1f);
        l.FillRect(Id(2), new RectF(10, 20, 30, 40), Red);
        l.PopGroup();
        DamageRegion d = new();
        l.PatchOpacity(Id(1), 0.5f, d);
        Assert.Equal(1, d.Count);
        Assert.Equal(new RectF(10, 20, 30, 40), d[0]);
    }

    [Fact]
    public void PatchRejectsWrongTargets()
    {
        DisplayList l = Animated(Matrix3x2.Identity, 1f);
        Assert.Throws<ArgumentException>(() => l.PatchTransform(Id(3), Matrix3x2.Identity));
        Assert.Throws<ArgumentException>(() => l.PatchOpacity(Id(99), 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => l.PatchOpacity(Id(3), 2f));
    }

    // ---- allocation

    private static void BuildThousand(DisplayList l, int variant)
    {
        PaintColor c = variant == 0 ? Red : Blue;
        int[] glyphs = s_glyphs;
        float[] xs = s_xs;
        int[] clusters = s_clusters;
        PathBuffer p = s_path;
        for (uint i = 0; i < 125; i++)
        {
            uint b = (i * 8) + 1;
            float x = (i % 25) * 10, y = (i / 25) * 10;
            l.FillRect(Id(b), new RectF(x, y, 9, 9), c);
            l.FillRoundedRect(Id(b + 1), new RectF(x, y, 9, 9), CornerRadii.Uniform(3), c);
            l.DrawBorder(Id(b + 2), new RectF(x, y, 9, 9), CornerRadii.Zero, new BorderSide(1, BorderStyle.Solid, c), new BorderSide(1, BorderStyle.Dashed, c), new BorderSide(1, BorderStyle.Solid, c), new BorderSide(1, BorderStyle.Solid, c));
            l.DrawBoxShadow(Id(b + 3), new RectF(x, y, 9, 9), CornerRadii.Zero, c, 1, 1, 2, 0, false);
            l.PushClipRect(Id(b + 4), new RectF(x, y, 9, 9));
            l.FillPath(Id(b + 5), p, c);
            l.PopClip();
            l.DrawGlyphRun(Id(b + 6), Scenes.Sans, 10, x, y + 8, c, glyphs, xs, default, clusters, "ABC");
            l.PushTransform(Id(b + 7), Matrix3x2.CreateTranslation(1, 1));
            l.PopTransform();
        }

        // 125 * 8 items + pushes/pops make well over 1000 list entries.
    }

    private static readonly int[] s_glyphs = [36, 37, 38];
    private static readonly float[] s_xs = [0, 10, 20];
    private static readonly int[] s_clusters = [0, 1, 2];
    private static readonly PathBuffer s_path = MakePath();

    private static PathBuffer MakePath()
    {
        PathBuffer p = new();
        p.MoveTo(0, 0);
        p.LineTo(5, 0);
        p.QuadTo(8, 4, 5, 8);
        p.Close();
        return p;
    }

    [Fact]
    public void RebuildingASameShapedThousandItemListAllocatesNothing()
    {
        RetainedDisplayList r = new();
        int variant = 0;
        Action frame = () =>
        {
            DisplayList l = r.Begin();
            BuildThousand(l, variant);
            r.Commit();
            r.ClearDamage();
            variant ^= 1;
        };
        frame();
        Assert.True(r.Current.Count >= 1000, "scene has " + r.Current.Count + " entries");
        Allocation.AssertZero(frame, "rebuild of a 1,000-item list into the retained list", warmup: 4, iterations: 20);
    }

    [Fact]
    public void PatchingAllocatesNothing()
    {
        RetainedDisplayList r = new();
        DisplayList l = r.Begin();
        BuildThousand(l, 0);
        l.PushTransform(Id(100000), Matrix3x2.Identity);
        l.PushGroup(Id(100001), 1f);
        for (uint i = 0; i < 100; i++)
        {
            l.FillRect(Id(200000 + i), new RectF(i, 0, 5, 5), Red);
        }

        l.PopGroup();
        l.PopTransform();
        r.Commit();
        float t = 0;
        Action patch = () =>
        {
            t += 0.5f;
            r.PatchTransform(Id(100000), Matrix3x2.CreateTranslation(t, 0));
            r.PatchOpacity(Id(100001), 0.5f);
            r.ClearDamage();
        };
        Allocation.AssertZero(patch, "transform and opacity patch", warmup: 4, iterations: 50);
    }
}
