using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using OneRender.Image;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Scroll offsets, sticky positioning and fixed boxes: translation items patched in place, with no relayout and no allocation.</summary>
public class ScrollTests
{
    private sealed class CountingLayout : ILayoutEngine
    {
        private readonly BlockLayoutEngine _inner = new();

        public int Runs { get; private set; }

        public void Layout(BoxTree boxes, in LayoutInput input, FragmentTree output, CancellationToken cancellationToken = default)
        {
            Runs++;
            _inner.Layout(boxes, input, output, cancellationToken);
        }
    }

    private const string Head = "<!DOCTYPE html><html><head><style>body{margin:0}div{box-sizing:border-box}</style></head><body>";

    private static HtmlPage Page(string body, out CountingLayout layout, int w = 200, int h = 100)
    {
        layout = new CountingLayout();
        return HtmlPage.Create(Head + body, new HtmlPageOptions(new DefaultFontProvider()) { ViewportWidth = w, ViewportHeight = h, Layout = layout });
    }

    private static Box BoxOf(HtmlPage page, string id) =>
        page.Boxes.PrincipalBox(page.Styled.FindBySource(page.Document.GetElementById(id).Index));

    private static PaintOptions Options(HtmlPage page) => new(page.Options.ViewportWidth, page.Options.ViewportHeight) { Scroll = page.Scroll };

    private static DisplayItem Find(DisplayList list, ItemKind kind, Func<DisplayItem, bool> pick)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == kind && pick(list[i]))
            {
                return list[i];
            }
        }

        throw new InvalidOperationException("no such item");
    }

    private static RectF FillWithColor(DisplayList list, float r, float g, float b)
    {
        DisplayItem it = Find(list, ItemKind.FillRect, i => Math.Abs(i.Paint.Color.R - r) < 0.02f && Math.Abs(i.Paint.Color.G - g) < 0.02f && Math.Abs(i.Paint.Color.B - b) < 0.02f);
        return it.RootBounds;
    }

    private const string Scroller =
        "<div id=s style='overflow:auto;width:100px;height:50px'><div style='height:20px;background:#ff0000'></div><div style='height:300px;background:#0000ff'></div></div>";

    [Fact]
    public void ScrollOffsetMovesTheContentUnderAFixedClip()
    {
        HtmlPage page = Page(Scroller, out _);
        var painter = new Painter();
        var list = new DisplayList();
        painter.Paint(page.Fragments, list, Options(page));
        Assert.Equal(new RectF(0, 0, 100, 20), FillWithColor(list, 1, 0, 0));
        page.Scroll.SetOffset(BoxOf(page, "s"), 0, 30);
        painter.Paint(page.Fragments, list, Options(page));
        // The red block (0..20) scrolled to -30..-10, above the 50px window.
        Assert.True(FillWithColor(list, 1, 0, 0).IsEmpty);
        Assert.Equal(new RectF(0, 0, 100, 50), FillWithColor(list, 0, 0, 1));
    }

    [Fact]
    public void OffsetsAreClampedToTheScrollableOverflow()
    {
        HtmlPage page = Page(Scroller, out _);
        Box s = BoxOf(page, "s");
        page.Scroll.SetOffset(s, -50, 100000);
        (double x, double y) = page.Scroll.GetOffset(page.Fragments, s);
        Assert.Equal(0, x);
        Assert.Equal(320 - 50, y);
        ScrollRange r = ScrollState.GetRange(page.Fragments, s);
        Assert.Equal(270, r.MaxY);
        Assert.Equal(0, r.MaxX);
    }

    [Fact]
    public void PatchedListEqualsAFreshPaintAndNothingRelaysOut()
    {
        HtmlPage page = Page(Scroller + "<div style='height:500px;background:#00ff00'></div>", out CountingLayout layout);
        Box s = BoxOf(page, "s");
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(page.Fragments, retained, Options(page));
        retained.ClearDamage();
        Assert.Equal(1, layout.Runs);

        page.Scroll.SetOffset(s, 0, 25);
        page.Scroll.SetViewportOffset(0, 40);
        int changed = painter.PatchScroll(retained);
        Assert.Equal(2, changed);
        Assert.Equal(1, layout.Runs);

        var fresh = new DisplayList();
        new Painter().Paint(page.Fragments, fresh, Options(page));
        DisplayList patched = retained.Current;
        Assert.Equal(fresh.Count, patched.Count);
        for (int i = 0; i < fresh.Count; i++)
        {
            Assert.Equal(fresh[i].Id, patched[i].Id);
            Assert.Equal(fresh[i].RootBounds, patched[i].RootBounds);
            Assert.Equal(fresh[i].ContentHash, patched[i].ContentHash);
            Assert.Equal(fresh[i].ContextHash, patched[i].ContextHash);
        }
    }

    [Fact]
    public void ScrollingDamagesOnlyTheScrolledRegion()
    {
        HtmlPage page = Page("<div style='height:10px;background:#00ff00'></div>" + Scroller, out _, w: 300, h: 200);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(page.Fragments, retained, Options(page));
        retained.ClearDamage();
        page.Scroll.SetOffset(BoxOf(page, "s"), 0, 60);
        Assert.Equal(1, painter.PatchScroll(retained));
        Assert.False(retained.Damage.IsEmpty);
        RectF b = retained.Damage.Bounds;
        // The scroller is at y 10..60, x 0..100; nothing outside it may be damaged.
        Assert.True(b.X >= 0 && b.Right <= 100 && b.Y >= 10 && b.Bottom <= 60, b.ToString());
    }

    [Fact]
    public void ScrollingAThousandBoxPageAllocatesNothingAndDoesNotRelayout()
    {
        var sb = new StringBuilder("<div id=s style='overflow:auto;width:200px;height:100px'>");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("<div style='height:10px;background:#").Append((i % 9) + 1).Append("00'></div>");
        }

        sb.Append("</div><div id=h style='position:sticky;top:0;height:5px'></div>");
        HtmlPage page = Page(sb.ToString(), out CountingLayout layout);
        Box s = BoxOf(page, "s");
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(page.Fragments, retained, Options(page));
        for (int i = 0; i < 4; i++)
        {
            page.Scroll.SetOffset(s, 0, i * 7);
            painter.PatchScroll(retained);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int patched = 0;
        for (int i = 0; i < 200; i++)
        {
            page.Scroll.SetOffset(s, 0, 100 + ((i * 37) % 5000));
            patched += painter.PatchScroll(retained);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
        Assert.Equal(200, patched);
        Assert.Equal(1, layout.Runs);
    }

    // ---- fixed boxes and the viewport ----

    [Fact]
    public void FixedBoxStaysPutWhileTheViewportScrolls()
    {
        HtmlPage page = Page("<div style='height:1000px;background:#00ff00'></div><div id=f style='position:fixed;left:10px;top:20px;width:30px;height:15px;background:#ff0000'></div>", out _);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(page.Fragments, retained, Options(page));
        Assert.Equal(new RectF(10, 20, 30, 15), FillWithColor(retained.Current, 1, 0, 0));
        Assert.Equal(new RectF(0, 0, 200, 1000), FillWithColor(retained.Current, 0, 1, 0));
        page.Scroll.SetViewportOffset(0, 300);
        Assert.Equal(2, painter.PatchScroll(retained));
        Assert.Equal(new RectF(10, 20, 30, 15), FillWithColor(retained.Current, 1, 0, 0));
        // The green block is 1000px tall and now starts at -300.
        Assert.Equal(new RectF(0, -300, 200, 1000), FillWithColor(retained.Current, 0, 1, 0));
        Assert.Equal(900, ScrollState.GetViewportRange(page.Fragments, 200, 100).MaxY);
    }

    [Fact]
    public void ViewportRangeIsTheRootsScrollableOverflowBeyondTheViewport()
    {
        HtmlPage page = Page("<div style='height:150px;width:260px'></div>", out _);
        ScrollRange r = ScrollState.GetViewportRange(page.Fragments, 200, 100);
        Assert.Equal(60, r.MaxX);
        Assert.Equal(50, r.MaxY);
    }

    // ---- a positioned box in a scroller that is not its parent ----

    [Fact]
    public void AbsoluteBoxPositionedAgainstAScrollerScrollsWithIt()
    {
        HtmlPage page = Page(
            "<div id=s style='position:relative;overflow:auto;width:100px;height:50px'><div style='height:200px'><div style='position:absolute;left:10px;top:80px;width:20px;height:5px;background:#ff0000'></div></div></div>",
            out _);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(page.Fragments, retained, Options(page));
        // Below the 50px window, clipped away.
        Assert.True(FillWithColor(retained.Current, 1, 0, 0).IsEmpty);
        page.Scroll.SetOffset(BoxOf(page, "s"), 0, 60);
        // The scroller's own item and the copy that carries the box, painted outside the scroller's scope, move.
        Assert.Equal(2, painter.PatchScroll(retained));
        Assert.Equal(new RectF(10, 20, 20, 5), FillWithColor(retained.Current, 1, 0, 0));
    }

    [Fact]
    public void AbsoluteBoxPositionedAgainstTheRootIsNotScrolledByAScrollerAroundIt()
    {
        HtmlPage page = Page(
            "<div id=s style='overflow:auto;width:100px;height:50px'><div style='height:200px'><div style='position:absolute;left:10px;top:20px;width:20px;height:5px;background:#ff0000'></div></div></div>",
            out _);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(page.Fragments, retained, Options(page));
        page.Scroll.SetOffset(BoxOf(page, "s"), 0, 60);
        painter.PatchScroll(retained);
        Assert.Equal(new RectF(10, 20, 20, 5), FillWithColor(retained.Current, 1, 0, 0));
    }

    // ---- sticky ----

    private static readonly Rect Port = new(0, 0, 100, 50);
    private static readonly Rect ContainingBlock = new(0, 0, 100, 300);

    private static double StickyDy(double scrollY, double top = 10, double bottom = double.NaN, Rect? box = null)
    {
        StickyPositioning.Offset(box ?? new Rect(0, 40, 100, 20), 0, 0, 0, 0, ContainingBlock, Port, 0, scrollY, top, double.NaN, bottom, double.NaN, out _, out double dy);
        return dy;
    }

    [Theory]
    [InlineData(0, 0)] // still below the inset line: stays
    [InlineData(30, 0)] // exactly at it
    [InlineData(45, 15)] // scrolled 15px past the line: pushed to stay 10px below the top of the window
    [InlineData(100, 70)]
    [InlineData(250, 220)] // the margin box reaches the bottom of the containing block (300 - 60): it stops there
    [InlineData(1000, 240)]
    public void StickyTopTracksTheScrollportAndStopsAtTheContainingBlock(double scrollY, double expected) =>
        Assert.Equal(expected, StickyDy(scrollY));

    [Fact]
    public void StickyBottomPullsTheBoxUpToTheBottomEdgeAndStopsAtTheContainingBlockStart()
    {
        var box = new Rect(0, 100, 100, 20);
        Assert.Equal(-80, StickyDy(0, double.NaN, 10, box)); // window bottom at 50, box bottom 120 must be at 40
        Assert.Equal(-50, StickyDy(30, double.NaN, 10, box));
        Assert.Equal(0, StickyDy(100, double.NaN, 10, box));
        Assert.Equal(-100, StickyDy(-1000, double.NaN, 10, new Rect(0, 100, 100, 20))); // cannot leave the containing block
    }

    [Fact]
    public void StickyMarginsCountInTheContainingBlockLimit()
    {
        StickyPositioning.Offset(new Rect(0, 40, 100, 20), 0, 0, 12, 0, new Rect(0, 0, 100, 100), Port, 0, 500, 0, double.NaN, double.NaN, double.NaN, out _, out double dy);
        Assert.Equal(100 - (60 + 12), dy);
    }

    [Fact]
    public void StickyHorizontalWorksTheSameWay()
    {
        StickyPositioning.Offset(new Rect(20, 0, 30, 10), 0, 0, 0, 0, new Rect(0, 0, 300, 10), Port, 70, 0, double.NaN, double.NaN, double.NaN, 5, out double dx, out _);
        Assert.Equal(55, dx);
    }

    [Fact]
    public void StickyHeaderInAScrollerFollowsThePatchedScrollPosition()
    {
        HtmlPage page = Page(
            "<div id=s style='overflow:auto;width:100px;height:50px'><div id=sec style='height:300px'><div style='height:40px'></div><div id=h style='position:sticky;top:10px;height:20px;background:#ff0000'></div></div></div>",
            out CountingLayout layout);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(page.Fragments, retained, Options(page));
        Assert.Equal(new RectF(0, 40, 100, 10), FillWithColor(retained.Current, 1, 0, 0)); // clipped by the 50px window
        foreach ((double scroll, float top) in new[] { (30.0, 10f), (45.0, 10f), (100.0, 10f) })
        {
            page.Scroll.SetOffset(BoxOf(page, "s"), 0, scroll);
            painter.PatchScroll(retained);
            RectF r = FillWithColor(retained.Current, 1, 0, 0);
            Assert.Equal(top, r.Y);
        }

        Assert.Equal(1, layout.Runs);
        // Fresh paint agrees with the patched list at the last position.
        var fresh = new DisplayList();
        new Painter().Paint(page.Fragments, fresh, Options(page));
        Assert.Equal(FillWithColor(fresh, 1, 0, 0), FillWithColor(retained.Current, 1, 0, 0));
    }

    [Fact]
    public void StickyAgainstTheViewportUsesTheViewportScroll()
    {
        HtmlPage page = Page("<div style='height:60px'></div><div id=h style='position:sticky;top:0;height:20px;background:#ff0000'></div><div style='height:500px'></div>", out _);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(page.Fragments, retained, Options(page));
        Assert.Equal(60f, FillWithColor(retained.Current, 1, 0, 0).Y);
        page.Scroll.SetViewportOffset(0, 200);
        painter.PatchScroll(retained);
        Assert.Equal(0f, FillWithColor(retained.Current, 1, 0, 0).Y);
    }

    [Fact]
    public void StickyPatchAllocatesNothingAndDamagesOnlyTheBox()
    {
        HtmlPage page = Page("<div style='height:60px'></div><div id=h style='position:sticky;top:0;height:20px;background:#ff0000'></div><div style='height:500px'></div>", out CountingLayout layout);
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(page.Fragments, retained, Options(page));
        for (int i = 0; i < 3; i++)
        {
            page.Scroll.SetViewportOffset(0, 100 + i);
            painter.PatchScroll(retained);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            page.Scroll.SetViewportOffset(0, 70 + i);
            painter.PatchScroll(retained);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(1, layout.Runs);
    }

    // ---- overflow-clip-margin ----

    [Fact]
    public void OverflowClipMarginPushesTheClipOutward()
    {
        HtmlPage page = Page("<div style='margin:20px;overflow:clip;overflow-clip-margin:content-box 5px;padding:10px;width:60px;height:40px'><div style='margin:-30px;width:200px;height:200px;background:#ff0000'></div></div>", out _);
        var list = new DisplayList();
        new Painter().Paint(page.Fragments, list, Options(page));
        DisplayItem clip = Find(list, ItemKind.PushClipRect, _ => true);
        // content box of the 60x40 border box (padding 10): x 30..., outset 5.
        Assert.Equal(new RectF(25, 25, 50, 30), clip.Rect);
    }
}
