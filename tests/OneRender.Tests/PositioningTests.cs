using System;
using OneFont;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tests.Support;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Positioning (CSS2 §9.3, §9.4.3, §10.3.7, §10.6.4, CSS Position 3) and overflow (CSS Overflow 3) in block layout.</summary>
public class PositioningTests
{
    private sealed class NoFonts : IFontProvider
    {
        public FontFallbackChain GetFallbackChain(in ComputedValues style) => throw new NotSupportedException();
    }

    private sealed class FixedImages(double w, double h) : IResourceResolver
    {
        public bool TryGetNaturalSize(ReplacedKind kind, ReadOnlySpan<char> source, out double width, out double height)
        {
            width = w;
            height = h;
            return true;
        }
    }

    internal sealed class Lay
    {
        public Pipeline P = null!;
        public FragmentTree F = null!;
        public BlockLayoutEngine Engine = null!;

        public Rect this[string id]
        {
            get
            {
                Assert.True(F.TryGetBorderBox(P.Document.GetElementById(id), out Rect r), "no box for #" + id);
                return r;
            }
        }

        public Fragment Frag(string id) => F.FragmentOf(P.Boxes.PrincipalBox(P.Styled.FindBySource(P.Document.GetElementById(id).Index)));
    }

    internal static Lay Run(string body, string css = "", double width = 800, double height = 600, IResourceResolver? images = null)
    {
        string html = "<!DOCTYPE html><html><head><style>body{margin:0}" + css + "</style></head><body>" + body + "</body></html>";
        Pipeline p = Pipeline.FromHtml(html, width, height, resolver: images);
        var f = new FragmentTree(p.Boxes);
        var engine = new BlockLayoutEngine();
        engine.Layout(p.Boxes, new LayoutInput(width, height, new NoFonts()), f);
        return new Lay { P = p, F = f, Engine = engine };
    }

    private const string Cb = "<div id=cb style='position:relative;margin-left:30px;margin-top:20px;border:5px solid;padding:10px;width:300px;height:200px'>";

    // ---- relative positioning, CSS2 §9.4.3 ----

    [Fact]
    public void RelativeShiftsTheBoxButNotItsSiblings()
    {
        Lay l = Run("<div id=a style='position:relative;left:10px;top:5px;height:20px'></div><div id=b style='height:20px'></div>");
        Assert.Equal(new Rect(10, 5, 800, 20), l["a"]);
        Assert.Equal(new Rect(0, 20, 800, 20), l["b"]);
    }

    [Fact]
    public void RelativeWithOneInsetMovesAwayFromTheOtherEdge()
    {
        Lay l = Run("<div id=a style='position:relative;right:15px;bottom:4px;height:20px'></div>");
        Assert.Equal(new Rect(-15, -4, 800, 20), l["a"]);
    }

    [Fact]
    public void RelativeWithBothInsetsLeftWinsInLtrAndRightInRtl()
    {
        Lay l = Run("<div id=a style='position:relative;left:10px;right:30px;top:3px;bottom:50px;height:20px'></div><div style='direction:rtl'><div id=b style='position:relative;left:10px;right:30px;height:20px'></div></div>");
        Assert.Equal(new Rect(10, 3, 800, 20), l["a"]);
        Assert.Equal(-30, l["b"].X);
    }

    [Fact]
    public void RelativePercentagesUseTheContainingBlockAndAutoHeightMakesVerticalOnesAuto()
    {
        Lay l = Run("<div style='width:400px'><div id=a style='position:relative;left:10%;top:50%;height:20px'></div></div>");
        Assert.Equal(40, l["a"].X);
        Assert.Equal(0, l["a"].Y);
        Lay m = Run("<div style='width:400px;height:200px'><div id=a style='position:relative;top:50%;height:20px'></div></div>");
        Assert.Equal(100, m["a"].Y);
    }

    [Fact]
    public void RelativeShiftOfAParentMovesItsChildren()
    {
        Lay l = Run("<div style='position:relative;left:7px;top:9px'><div id=c style='height:10px'></div></div>");
        Assert.Equal(new Rect(7, 9, 800, 10), l["c"]);
    }

    // ---- containing blocks, CSS2 §10.1 ----

    [Fact]
    public void AbsoluteBoxWithoutPositionedAncestorUsesTheInitialContainingBlock()
    {
        Lay l = Run("<div style='margin:50px'><div id=a style='position:absolute;left:10px;top:20px;width:100px;height:50px'></div></div>");
        Assert.Equal(new Rect(10, 20, 100, 50), l["a"]);
    }

    [Fact]
    public void AbsoluteBoxUsesThePaddingBoxOfThePositionedAncestor()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;left:0;top:0;width:10px;height:10px'></div></div>");
        // margin 30/20 + border 5: the padding box starts at (35, 25)
        Assert.Equal(new Rect(35, 25, 10, 10), l["a"]);
    }

    [Fact]
    public void AbsoluteBoxInsideAnotherAbsoluteBoxIsPlacedAgainstIt()
    {
        Lay l = Run("<div id=a style='position:absolute;left:100px;top:100px;width:200px;height:100px'><div id=b style='position:absolute;right:0;bottom:0;width:20px;height:10px'></div></div>");
        Assert.Equal(new Rect(280, 190, 20, 10), l["b"]);
    }

    [Fact]
    public void AStaticAncestorBetweenDoesNotChangeTheContainingBlock()
    {
        Lay l = Run(Cb + "<div style='margin:40px;padding:3px'><div id=a style='position:absolute;left:1px;top:2px;width:10px;height:10px'></div></div></div>");
        Assert.Equal(new Rect(36, 27, 10, 10), l["a"]);
    }

    [Fact]
    public void FixedBoxesAreRelativeToTheViewportWhateverTheAncestors()
    {
        Lay l = Run("<div style='position:relative;margin:30px;height:2000px'><div id=a style='position:fixed;right:10px;bottom:5px;width:50px;height:40px'></div></div>");
        Assert.Equal(new Rect(740, 555, 50, 40), l["a"]);
    }

    // ---- CSS2 §10.3.7 widths and horizontal position ----

    [Fact]
    public void LeftAndRightWithAutoWidthStretch()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;left:10px;right:20px;height:5px;margin:0 3px 0 4px'></div></div>");
        Assert.Equal(new Rect(35 + 10 + 4, 35, 300 + 20 - 10 - 20 - 7, 5), l["a"]);
    }

    [Fact]
    public void RightAndWidthSolveLeft()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;right:20px;width:100px;height:5px'></div></div>");
        Assert.Equal(35 + 320 - 20 - 100, l["a"].X);
    }

    [Fact]
    public void ShrinkToFitWithLeftOnly()
    {
        Lay l = Run("<div id=a style='position:absolute;left:10px;top:0'><div style='width:80px;height:5px'></div></div>");
        Assert.Equal(new Rect(10, 0, 80, 5), l["a"]);
    }

    [Fact]
    public void ShrinkToFitIsLimitedByTheAvailableWidth()
    {
        // preferred minimum 50, preferred 500, available 300 - left 100: min(max(50, 200), 500)
        Lay l = Run(Cb + "<div id=a style='position:absolute;left:100px;top:0'><div style='width:500px;height:5px'></div></div></div>", css: "");
        Assert.Equal(500, l["a"].Width); // the child has a definite width, so min = max = 500
        Lay m = Run("<div id=a style='position:absolute;left:100px;top:0;width:fit-content'><div style='width:50%;height:5px'></div></div>");
        Assert.True(m["a"].Width <= 700);
    }

    [Fact]
    public void AutoMarginsCenterAWidthSpecifiedBox()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;left:0;right:0;width:100px;height:5px;margin:0 auto'></div></div>");
        Assert.Equal(35 + 110, l["a"].X);
    }

    [Fact]
    public void OneAutoMarginTakesTheRemainder()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;left:10px;right:20px;width:100px;height:5px;margin-left:auto;margin-right:7px'></div></div>");
        Assert.Equal(35 + 10 + (320 - 10 - 20 - 100 - 7), l["a"].X);
    }

    [Fact]
    public void OverConstrainedLtrIgnoresRight()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;left:10px;right:10px;width:100px;height:5px'></div></div>");
        Assert.Equal(new Rect(45, 35, 100, 5), l["a"]); // top is auto: static position
    }

    [Fact]
    public void OverConstrainedRtlIgnoresLeft()
    {
        Lay l = Run("<div style='direction:rtl;position:relative;width:300px;height:50px'><div id=a style='position:absolute;left:10px;right:10px;width:100px;height:5px'></div></div>");
        Assert.Equal(300 - 10 - 100, l["a"].X);
    }

    [Fact]
    public void NegativeFreeSpaceWithBothMarginsAutoGivesTheEndMarginTheRemainder()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;left:0;right:0;width:400px;height:5px;margin:0 auto'></div></div>");
        Assert.Equal(35, l["a"].X);
        Lay r = Run("<div style='direction:rtl;position:relative;width:300px;height:50px'><div id=a style='position:absolute;left:0;right:0;width:400px;height:5px;margin:0 auto'></div></div>");
        Assert.Equal(-100, r["a"].X);
    }

    [Fact]
    public void MinAndMaxWidthApplyAfterTheEquation()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;left:0;right:0;max-width:100px;height:5px'></div><div id=b style='position:absolute;left:0;width:10px;min-width:60px;height:5px'></div></div>");
        Assert.Equal(100, l["a"].Width);
        Assert.Equal(60, l["b"].Width);
    }

    [Fact]
    public void PercentagesResolveAgainstThePaddingBox()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;left:10%;top:50%;width:50%;height:25%'></div></div>");
        Assert.Equal(new Rect(35 + 32, 25 + 110, 160, 55), l["a"]);
    }

    // ---- static position ----

    [Fact]
    public void StaticPositionIsWhereTheBoxWouldHaveBeenInFlow()
    {
        Lay l = Run("<div style='height:30px'></div><div style='padding:7px 0 0 9px'><div id=a style='position:absolute;width:10px;height:5px'></div></div>");
        Assert.Equal(new Rect(9, 37, 10, 5), l["a"]);
    }

    [Fact]
    public void StaticPositionFollowsPrecedingSiblingsAndTheirMargins()
    {
        Lay l = Run("<div style='height:30px;margin-bottom:12px'></div><div id=a style='position:absolute;width:10px;height:5px;margin-top:3px'></div><div id=b style='height:10px'></div>");
        Assert.Equal(new Rect(0, 45, 10, 5), l["a"]);
        // The absolute box takes no part in margin collapsing and no space.
        Assert.Equal(42, l["b"].Y);
    }

    [Fact]
    public void StaticTopOfAFirstChildStaysInsideItsParentWhenMarginsCollapseThrough()
    {
        Lay l = Run("<div id=p style='margin-top:25px'><div id=a style='position:absolute;width:10px;height:5px'></div><div style='height:10px'></div></div>");
        Assert.Equal(25, l["a"].Y);
        Assert.Equal(25, l["p"].Y);
    }

    [Fact]
    public void StaticPositionInInlineContentIsTheStartOfTheContent()
    {
        // Placeholder inline layout: until real inline layout reports positions, the start of the container's content box stands in.
        Lay l = Run("<p style='margin:0;padding:4px 0 0 6px'>text <span id=a style='position:absolute;width:10px;height:5px'></span></p>");
        Assert.Equal(new Rect(6, 4, 10, 5), l["a"]);
    }

    [Fact]
    public void RtlStaticPositionIsMeasuredFromTheRightEdge()
    {
        Lay l = Run("<div style='direction:rtl;width:300px'><div id=a style='position:absolute;width:100px;height:5px'></div></div>");
        Assert.Equal(200, l["a"].X);
    }

    // ---- CSS2 §10.6.4 heights and vertical position ----

    [Fact]
    public void TopAndBottomWithAutoHeightStretch()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;top:10px;bottom:20px;width:5px'></div></div>");
        Assert.Equal(new Rect(45, 35, 5, 220 - 30), l["a"]); // auto left: the static position is the parent's content edge, 10px of padding in
    }

    [Fact]
    public void BottomWithContentHeightSolvesTop()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;bottom:10px;width:50px'><div style='height:40px'></div></div></div>");
        Assert.Equal(new Rect(45, 25 + 220 - 10 - 40, 50, 40), l["a"]);
    }

    [Fact]
    public void TopAndHeightSolveBottomAndOverConstrainedIgnoresBottom()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;top:10px;bottom:10px;height:50px;width:5px'></div></div>");
        Assert.Equal(new Rect(45, 35, 5, 50), l["a"]);
    }

    [Fact]
    public void AutoVerticalMarginsCenter()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;top:0;bottom:0;height:50px;width:5px;margin:auto 0'></div></div>");
        Assert.Equal(25 + 85, l["a"].Y);
    }

    [Fact]
    public void AutoVerticalMarginsWithNegativeRemainderCenterToo()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;top:0;bottom:0;height:260px;width:5px;margin:auto 0'></div></div>");
        Assert.Equal(25 - 20, l["a"].Y);
    }

    [Fact]
    public void MinAndMaxHeightApply()
    {
        Lay l = Run(Cb + "<div id=a style='position:absolute;top:0;bottom:0;max-height:40px;width:5px'></div><div id=b style='position:absolute;top:0;min-height:60px;width:5px'></div></div>");
        Assert.Equal(40, l["a"].Height);
        Assert.Equal(60, l["b"].Height);
    }

    [Fact]
    public void AspectRatioGivesAnAutoHeight()
    {
        Lay l = Run("<div id=a style='position:absolute;left:0;top:0;width:200px;aspect-ratio:2/1'></div>");
        Assert.Equal(new Rect(0, 0, 200, 100), l["a"]);
    }

    // ---- replaced elements, §10.3.8 and §10.6.5 ----

    [Fact]
    public void ReplacedBoxKeepsItsIntrinsicSizeBetweenLeftAndRight()
    {
        Lay l = Run(Cb + "<img id=a src=x style='position:absolute;left:0;right:0;top:0'></div>", images: new FixedImages(60, 30));
        Assert.Equal(new Rect(35, 25, 60, 30), l["a"]);
    }

    [Fact]
    public void ReplacedBoxWithBothMarginsAutoIsCentered()
    {
        Lay l = Run(Cb + "<img id=a src=x style='position:absolute;left:0;right:0;top:0;bottom:0;margin:auto'></div>", images: new FixedImages(60, 30));
        Assert.Equal(new Rect(35 + 130, 25 + 95, 60, 30), l["a"]);
    }

    [Fact]
    public void ReplacedBoxWithAutoInsetsUsesTheStaticPosition()
    {
        Lay l = Run("<div style='height:40px'></div><img id=a src=x style='position:absolute;width:20px'>", images: new FixedImages(60, 30));
        Assert.Equal(new Rect(0, 40, 20, 10), l["a"]);
    }

    // ---- paint-order relevant: fragments appear in document order ----

    [Fact]
    public void AbsoluteFragmentsSitUnderTheirParentInDocumentOrder()
    {
        Lay l = Run("<div id=p><div id=x style='height:5px'></div><div id=a style='position:absolute'></div><div id=y style='height:5px'></div></div>");
        Fragment p = l.Frag("p");
        Assert.Equal(l.Frag("x"), p.FirstChild);
        Assert.Equal(l.Frag("a"), p.FirstChild.NextSibling);
        Assert.Equal(l.Frag("y"), p.FirstChild.NextSibling.NextSibling);
    }

    // ---- overflow ----

    [Fact]
    public void ScrollContainerRecordsScrollableOverflowOfItsContent()
    {
        Lay l = Run("<div id=s style='overflow:auto;width:100px;height:50px;padding:5px;border:2px solid'><div style='width:300px;height:400px'></div></div>");
        Rect ov = l.Frag("s").ScrollableOverflow;
        // padding box starts at 2; the child sits at padding-left 5 in the content box: 2 + 5 + 300 = 307, then end padding 5
        Assert.Equal(2, ov.X);
        Assert.Equal(312, ov.Right);
        Assert.Equal(2, ov.Y);
        Assert.Equal(2 + 5 + 400 + 5, ov.Bottom);
    }

    [Fact]
    public void OverflowOfAClippedBoxDoesNotReachItsParent()
    {
        Lay l = Run("<div id=p><div id=s style='overflow:hidden;width:100px;height:50px'><div style='width:300px;height:400px'></div></div></div>");
        Rect ov = l.Frag("p").ScrollableOverflow;
        Assert.Equal(800, ov.Right);
        Assert.Equal(50, ov.Bottom);
    }

    [Fact]
    public void OverflowClipAlsoCutsPropagationButVisibleDoesNot()
    {
        Lay l = Run("<div id=p><div style='overflow:clip;width:100px;height:50px'><div style='width:300px;height:400px'></div></div></div><div id=q><div style='width:100px;height:50px'><div style='width:300px;height:400px'></div></div></div>");
        Assert.Equal(50, l.Frag("p").ScrollableOverflow.Bottom);
        Assert.Equal(400, l.Frag("q").ScrollableOverflow.Bottom);
    }

    [Fact]
    public void OverflowClipIsNotAScrollContainerButHiddenIs()
    {
        Lay l = Run("<div id=c style='overflow:clip;width:100px;height:50px'><div style='height:400px'></div></div><div id=h style='overflow:hidden;width:100px;height:50px'><div style='height:400px'></div></div>");
        ScrollRange rc = ScrollState.GetRange(l.F, l.P.Boxes.PrincipalBox(l.P.Styled.FindBySource(l.P.Document.GetElementById("c").Index)));
        ScrollRange rh = ScrollState.GetRange(l.F, l.P.Boxes.PrincipalBox(l.P.Styled.FindBySource(l.P.Document.GetElementById("h").Index)));
        Assert.False(rc.CanScroll);
        Assert.Equal(350, rh.MaxY);
        Assert.Equal(0, rh.MaxX);
    }

    [Fact]
    public void ComputedOverflowMakesVisibleAutoWhenTheOtherAxisScrolls()
    {
        Lay l = Run("<div id=a style='overflow-x:hidden'></div><div id=b style='overflow-x:clip;overflow-y:scroll'></div>");
        Box a = l.P.Boxes.PrincipalBox(l.P.Styled.FindBySource(l.P.Document.GetElementById("a").Index));
        Box b = l.P.Boxes.PrincipalBox(l.P.Styled.FindBySource(l.P.Document.GetElementById("b").Index));
        Assert.Equal(OverflowKind.Auto, a.Values.OverflowY);
        Assert.Equal(OverflowKind.Hidden, b.Values.OverflowX);
    }

    [Fact]
    public void AbsoluteBoxAddsOverflowToItsContainingBlockNotItsParent()
    {
        Lay l = Run("<div id=s style='overflow:auto;width:100px;height:50px'><div id=p style='height:10px'><div id=a style='position:absolute;left:0;top:0;width:10px;height:200px'></div></div></div><div id=s2 style='overflow:auto;position:relative;width:100px;height:50px'><div id=p2><div style='position:absolute;left:0;top:0;width:10px;height:200px'></div></div></div>");
        // The first absolute box is positioned against the initial containing block: the scroller does not scroll for it.
        Assert.Equal(50, l.Frag("s").ScrollableOverflow.Bottom);
        // The second one is positioned against the scroller itself.
        Assert.Equal(200, l.Frag("s2").ScrollableOverflow.Bottom);
    }

    [Fact]
    public void AbsoluteBoxInTheInitialContainingBlockExtendsTheRootOverflow()
    {
        Lay l = Run("<div id=a style='position:absolute;left:0;top:900px;width:1200px;height:10px'></div>");
        Rect ov = l.F.Root.ScrollableOverflow;
        Assert.Equal(1200, ov.Right);
        Assert.Equal(910, ov.Bottom);
    }

    [Fact]
    public void FixedBoxesDoNotExtendTheScrollableOverflow()
    {
        Lay l = Run("<div style='height:10px'></div><div style='position:fixed;left:0;top:900px;width:1200px;height:10px'></div>");
        Assert.Equal(800, l.F.Root.ScrollableOverflow.Right);
    }

    [Fact]
    public void RelativeOffsetCountsAsOverflowWhereTheBoxIsDrawn()
    {
        Lay l = Run("<div id=p style='height:10px'><div style='position:relative;left:50px;top:70px;width:10px;height:10px'></div></div>");
        Assert.Equal(80, l.Frag("p").ScrollableOverflow.Bottom);
    }

    // ---- propagation to the viewport, Overflow 3 §3.3 ----

    [Fact]
    public void BodyOverflowPropagatesToTheViewportWhenTheRootIsVisible()
    {
        Lay l = Run("<div style='height:2000px'></div>", css: "body{overflow:hidden;height:100px}");
        Box body = l.P.Boxes.Root.FirstChild;
        while (body.Node.TagName != "body")
        {
            body = body.NextSibling;
        }

        ViewportOverflow.Resolve(l.P.Boxes, out OverflowKind x, out OverflowKind y, out int source);
        Assert.Equal(body.Index, source);
        Assert.Equal(OverflowKind.Hidden, x);
        // The body behaves as visible: it neither clips nor becomes a scroll container, so its content overflows into the root.
        Assert.False(ScrollState.GetRange(l.F, body).CanScroll);
        Assert.Equal(2000, l.F.Root.ScrollableOverflow.Bottom);
    }

    [Fact]
    public void RootOverflowWinsOverTheBodys()
    {
        Lay l = Run("<div></div>", css: "html{overflow:scroll}body{overflow:hidden}");
        ViewportOverflow.Resolve(l.P.Boxes, out OverflowKind x, out _, out int source);
        Assert.Equal(l.P.Boxes.Root.Index, source);
        Assert.Equal(OverflowKind.Scroll, x);
    }

    [Fact]
    public void ThePositionedRootElementIsPlacedInTheInitialContainingBlock()
    {
        Pipeline p = Pipeline.FromHtml("<!DOCTYPE html><html style='position:absolute;left:100px;top:100px;width:100px;height:100px;border:10px solid'><body style='margin:0'><div id=a style='height:5px'></div></body></html>", 800, 600);
        var f = new FragmentTree(p.Boxes);
        new BlockLayoutEngine().Layout(p.Boxes, new LayoutInput(800, 600, new NoFonts()), f);
        Assert.Equal(new Rect(100, 100, 120, 120), f.Root.BorderBox);
        Assert.True(f.TryGetBorderBox(p.Document.GetElementById("a"), out Rect a));
        Assert.Equal(new Rect(110, 110, 100, 5), a);
    }

    // ---- bounds and determinism ----

    [Fact]
    public void ManyAbsoluteBoxesInDeepNestingAreLinear()
    {
        var sb = new System.Text.StringBuilder();
        const int Depth = 3000;
        for (int i = 0; i < Depth; i++)
        {
            sb.Append("<div style='position:relative'><span style='position:absolute;left:1px;top:1px;width:1px;height:1px'></span>");
        }

        for (int i = 0; i < Depth; i++)
        {
            sb.Append("</div>");
        }

        Lay l = Run(sb.ToString());
        Assert.True(l.F.Count > Depth);
    }

    [Fact]
    public void LayoutOfAnUnchangedTreeWithPositionedBoxesAllocatesNothing()
    {
        Lay l = Run(Cb + "<div style='position:absolute;left:1px;top:2px;width:30px;height:10px'><div style='position:absolute;right:0;bottom:0;width:3px;height:3px'></div></div><div style='position:relative;left:3px;overflow:auto;height:20px'><div style='height:90px'></div></div></div>");
        var input = new LayoutInput(800, 600, new NoFonts());
        for (int i = 0; i < 3; i++)
        {
            l.Engine.Layout(l.P.Boxes, input, l.F);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        l.Engine.Layout(l.P.Boxes, input, l.F);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }
}
