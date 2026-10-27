using System;
using System.Text;
using System.Threading;
using OneFont;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tests.Support;
using OneRender.Tree;
using OneRender.TestSupport;
using Xunit;

namespace OneRender.Tests;

/// <summary>Block layout: widths, heights, margin collapsing, replaced boxes, intrinsic sizes and the retained-storage rules.</summary>
public class BlockLayoutTests
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

    private sealed class Lay
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
    }

    private static Lay Run(string body, string css = "", double width = 800, double height = double.NaN, IResourceResolver? images = null)
    {
        string html = "<!DOCTYPE html><html><head><style>body{margin:0}" + css + "</style></head><body>" + body + "</body></html>";
        Pipeline p = Pipeline.FromHtml(html, width, double.IsNaN(height) ? 600 : height, resolver: images);
        var f = new FragmentTree(p.Boxes);
        var engine = new BlockLayoutEngine();
        engine.Layout(p.Boxes, new LayoutInput(width, height, new NoFonts()), f);
        return new Lay { P = p, F = f, Engine = engine };
    }

    // ---- CSS2 §10.3.3 widths ----

    [Fact]
    public void AutoWidthFillsTheContainingBlockMinusMarginsBordersAndPadding()
    {
        Lay l = Run("<div id=a style='margin:0 10px;padding:5px;border:2px solid;height:10px'></div>");
        Assert.Equal(new Rect(10, 0, 780, 24), l["a"]);
        Assert.Equal(new Insets(2, 2, 2, 2), l.F.FragmentOf(l.P.Boxes.PrincipalBox(l.P.Styled.FindBySource(l.P.Document.GetElementById("a").Index))).Border);
    }

    [Fact]
    public void AutoMarginsCenterAndOneAutoMarginTakesTheRest()
    {
        Lay l = Run("<div id=a style='width:200px;margin:0 auto;height:1px'></div><div id=b style='width:200px;margin-left:auto;margin-right:50px;height:1px'></div><div id=c style='width:200px;margin-right:auto;margin-left:10px;height:1px'></div>");
        Assert.Equal(300, l["a"].X);
        Assert.Equal(550, l["b"].X);
        Assert.Equal(10, l["c"].X);
    }

    [Fact]
    public void AnOverConstrainedBoxIgnoresTheEndMargin()
    {
        Lay l = Run("<div id=a style='width:200px;margin:0 10px;height:1px'></div>");
        Assert.Equal(new Rect(10, 0, 200, 1), l["a"]);

        // direction: rtl ignores the left margin instead (CSS2 §10.3.3)
        Lay r = Run("<div style='direction:rtl;width:400px'><div id=a style='width:100px;margin:0 10px;height:1px'></div></div>");
        Assert.Equal(new Rect(290, 0, 100, 1), r["a"]);
    }

    [Fact]
    public void NegativeAutoMarginSpaceGoesToTheEndMargin()
    {
        Lay l = Run("<div style='width:100px'><div id=a style='width:200px;margin:0 auto;height:1px'></div></div>");
        Assert.Equal(0, l["a"].X);
        Assert.Equal(200, l["a"].Width);
    }

    [Fact]
    public void PercentagesResolveAgainstTheContainingBlockWidth()
    {
        Lay l = Run("<div style='width:400px'><div id=a style='width:50%;margin-left:10%;padding:5%;height:1px'></div></div>");
        // content 200 + padding 2 * 20; margin-left 40
        Assert.Equal(new Rect(40, 0, 240, 41), l["a"]);
    }

    [Fact]
    public void MinAndMaxWidthClampInTheOrderOfCss2Section104()
    {
        Lay l = Run("<div id=a style='width:50%;max-width:300px;height:1px'></div><div id=b style='width:100px;min-width:500px;height:1px'></div><div id=c style='width:900px;max-width:100px;min-width:200px;height:1px'></div>");
        Assert.Equal(300, l["a"].Width);
        Assert.Equal(500, l["b"].Width);
        Assert.Equal(200, l["c"].Width); // min-width wins over max-width
    }

    [Fact]
    public void BoxSizingChangesWhatWidthMeans()
    {
        Lay l = Run("<div id=a style='width:200px;padding:10px;border:5px solid;height:1px'></div><div id=b style='box-sizing:border-box;width:200px;padding:10px;border:5px solid;height:1px'></div><div id=c style='box-sizing:border-box;width:10px;padding:10px;border:5px solid;height:1px'></div>");
        Assert.Equal(230, l["a"].Width);
        Assert.Equal(200, l["b"].Width);
        Assert.Equal(30, l["c"].Width); // the content box cannot go below zero
    }

    [Fact]
    public void IntrinsicWidthKeywordsUseTheContentSizes()
    {
        Lay l = Run(
            "<div id=a style='width:max-content'><div style='width:100px;height:1px'></div><div style='width:50px;padding:10px;margin-left:5px;height:1px'></div></div>" +
            "<div id=b style='width:fit-content'><div style='width:1000px;height:1px'></div></div>" +
            "<div id=c style='width:min-content'><div style='width:70px;height:1px'></div></div>");
        Assert.Equal(100, l["a"].Width);
        Assert.Equal(1000, l["b"].Width); // fit-content never goes below min-content, even past the available width
        Assert.Equal(70, l["c"].Width);
    }

    // ---- heights ----

    [Fact]
    public void AutoHeightSumsTheChildrenAndSpecifiedHeightWins()
    {
        Lay l = Run("<div id=a><div style='height:30px'></div><div style='height:40px'></div></div><div id=b style='height:10px'><div style='height:99px'></div></div><div id=c style='padding:3px;border-top:2px solid'><div style='height:5px'></div></div>");
        Assert.Equal(70, l["a"].Height);
        Assert.Equal(10, l["b"].Height);
        Assert.Equal(70, l["b"].Y);
        Assert.Equal(13, l["c"].Height);
    }

    [Fact]
    public void PercentageHeightsNeedADefiniteContainingBlockHeight()
    {
        Lay l = Run("<div id=a><div id=a1 style='height:50%'></div></div><div id=b style='height:200px'><div id=b1 style='height:50%'></div></div>");
        Assert.Equal(0, l["a1"].Height); // auto: the parent's height depends on its content (CSS2 §10.5)
        Assert.Equal(100, l["b1"].Height);
    }

    [Fact]
    public void ThePercentageHeightOfTheRootChainResolvesAgainstTheViewport()
    {
        Lay l = Run("<div id=a style='height:50%'></div>", "html,body{height:100%}", 800, 600);
        Assert.Equal(600, l["a"].Height + 300);
        Assert.Equal(300, l["a"].Height);
        Assert.Equal(600, l.F.Root.BorderBox.Height);
    }

    [Fact]
    public void MinAndMaxHeightClampAutoAndSpecifiedHeights()
    {
        Lay l = Run("<div id=a style='min-height:50px'></div><div id=b style='height:100px;max-height:40px'></div><div id=c style='height:10px;min-height:30px'></div><div id=d style='max-height:5px'><div style='height:20px'></div></div>");
        Assert.Equal(50, l["a"].Height);
        Assert.Equal(40, l["b"].Height);
        Assert.Equal(30, l["c"].Height);
        Assert.Equal(5, l["d"].Height);
    }

    [Fact]
    public void AspectRatioGivesAnAutoHeight()
    {
        Lay l = Run("<div id=a style='aspect-ratio:2/1'></div><div id=b style='aspect-ratio:1/1;width:100px;box-sizing:border-box;padding:10px'></div><div id=c style='aspect-ratio:2/1;height:30px'></div>");
        Assert.Equal(new Rect(0, 0, 800, 400), l["a"]);
        Assert.Equal(100, l["b"].Height);
        Assert.Equal(30, l["c"].Height);
    }

    // ---- margin collapsing, CSS2 §8.3.1 ----

    [Fact]
    public void AdjoiningSiblingMarginsCollapseToTheLargest()
    {
        Lay l = Run("<div id=a style='height:10px;margin-bottom:20px'></div><div id=b style='height:10px;margin-top:30px'></div>");
        Assert.Equal(40, l["b"].Y);
    }

    [Fact]
    public void NegativeMarginsAddTheMostNegativeToTheLargestPositive()
    {
        Lay l = Run(
            "<div id=a style='height:10px;margin-bottom:20px'></div><div id=b style='height:10px;margin-top:-5px'></div>" +
            "<div id=c style='height:10px;margin-top:-10px;margin-bottom:-20px'></div><div id=d style='height:10px'></div>" +
            "<div id=e style='height:10px;margin-bottom:30px'></div><div id=f style='height:10px;margin-top:-10px;margin-bottom:-4px'></div><div id=g style='height:10px;margin-top:8px'></div>");
        Assert.Equal(25, l["b"].Y);                       // 20 + (-5) = 15 below a's bottom
        Assert.Equal(25, l["c"].Y);                       // b's bottom margin 0, c's top margin -10
        Assert.Equal(15, l["d"].Y);                       // c's bottom margin -20 below c's bottom
        Assert.Equal(l["d"].Bottom, l["e"].Y);
        Assert.Equal(l["e"].Bottom + 20, l["f"].Y);       // 30 + (-10)
        Assert.Equal(l["f"].Bottom + 4, l["g"].Y);        // 8 + (-4)
    }

    [Fact]
    public void ParentAndFirstChildTopMarginsCollapse()
    {
        Lay l = Run("<div style='height:10px'></div><div id=p style='margin-top:15px'><div id=c style='margin-top:20px;height:10px'></div></div>");
        Assert.Equal(30, l["p"].Y);
        Assert.Equal(30, l["c"].Y);
        Assert.Equal(10, l["p"].Height);
    }

    [Fact]
    public void PaddingOrBorderStopsParentChildCollapsing()
    {
        Lay l = Run("<div id=p style='padding-top:5px'><div id=c style='margin-top:20px;height:10px'></div></div><div id=q style='border-top:1px solid'><div id=d style='margin-top:20px;height:10px'></div></div>");
        Assert.Equal(0, l["p"].Y);
        Assert.Equal(25, l["c"].Y);
        Assert.Equal(35, l["p"].Height);
        Assert.Equal(35, l["q"].Y);
        Assert.Equal(56, l["d"].Y);
    }

    [Fact]
    public void LastChildBottomMarginCollapsesWithAnAutoHeightParent()
    {
        Lay l = Run("<div id=p style='margin-bottom:10px'><div style='height:10px;margin-bottom:30px'></div></div><div id=n style='height:5px'></div>");
        Assert.Equal(10, l["p"].Height);
        Assert.Equal(40, l["n"].Y);
    }

    [Fact]
    public void ASpecifiedHeightMinHeightOrBottomPaddingKeepsTheChildMarginInside()
    {
        Lay l = Run(
            "<div id=p style='height:20px'><div style='height:10px;margin-bottom:30px'></div></div><div id=n style='height:5px'></div>" +
            "<div id=q style='min-height:50px'><div style='height:10px;margin-bottom:30px'></div></div><div id=r style='height:5px'></div>" +
            "<div id=s style='padding-bottom:1px'><div style='height:10px;margin-bottom:30px'></div></div><div id=t style='height:5px'></div>");
        Assert.Equal(20, l["n"].Y);
        Assert.Equal(50, l["q"].Height);
        Assert.Equal(l["q"].Bottom, l["r"].Y);
        Assert.Equal(41, l["s"].Height);
        Assert.Equal(l["s"].Bottom, l["t"].Y);
    }

    [Fact]
    public void EmptyBlocksCollapseTheirMarginsThrough()
    {
        Lay l = Run("<div style='height:10px'></div><div id=e style='margin-top:20px;margin-bottom:30px'></div><div id=n style='height:5px;margin-top:5px'></div>");
        Assert.Equal(40, l["n"].Y);
        Assert.Equal(0, l["e"].Height);
        Assert.Equal(30, l["e"].Y); // where the border edge would be with a non-zero bottom border: after the 20px top margin

        Lay m = Run("<div style='margin:10px 0'><div style='margin:20px 0'></div></div><div id=n style='height:5px'></div>");
        Assert.Equal(20, m["n"].Y);
    }

    [Fact]
    public void NestedFirstChildMarginsCollapseIntoOneAndMoveTheWholeChain()
    {
        Lay l = Run("<div id=a style='margin-top:10px'><div id=b style='margin-top:20px'><div id=c style='margin-top:25px;height:10px'></div></div></div>");
        Assert.Equal(25, l["a"].Y);
        Assert.Equal(25, l["b"].Y);
        Assert.Equal(25, l["c"].Y);
        Assert.Equal(10, l["a"].Height);
    }

    [Fact]
    public void AnEmptyFirstChildStillLetsLaterMarginsJoinTheChain()
    {
        Lay l = Run("<div id=a><div style='margin:10px 0'></div><div id=c style='margin-top:20px;height:10px'></div></div>");
        Assert.Equal(20, l["a"].Y);
        Assert.Equal(20, l["c"].Y);
    }

    [Fact]
    public void ABlockFormattingContextRootDoesNotCollapseWithItsChildren()
    {
        Lay l = Run(
            "<div id=p style='overflow:hidden'><div id=c style='margin:20px 0 30px;height:10px'></div></div><div id=n style='height:5px'></div>" +
            "<div id=f style='display:flow-root'><div id=g style='margin-top:7px;height:10px'></div></div>");
        Assert.Equal(0, l["p"].Y);
        Assert.Equal(20, l["c"].Y);
        Assert.Equal(60, l["p"].Height);
        Assert.Equal(60, l["n"].Y);
        Assert.Equal(7, l["g"].Y - l["f"].Y);
    }

    [Fact]
    public void TheRootElementMarginDoesNotCollapseWithItsChildren()
    {
        Lay l = Run("<div id=a style='margin-top:20px;height:10px'></div>", "html{margin-top:5px}");
        Assert.Equal(5, l.F.Root.BorderBox.Y);
        Assert.Equal(25, l["a"].Y);
    }

    [Fact]
    public void MarginsOfAReplacedBoxCollapseLikeAnyBlock()
    {
        Lay l = Run("<div style='height:10px;margin-bottom:20px'></div><img id=i style='display:block;margin-top:30px'>", images: new FixedImages(40, 20));
        Assert.Equal(40, l["i"].Y);
    }

    // ---- replaced boxes, CSS2 §10.3.2, §10.4 and §10.6.2 ----

    [Fact]
    public void ReplacedBoxesUseNaturalSizeAndTheRatio()
    {
        var images = new FixedImages(200, 100);
        Lay l = Run(
            "<img id=a style='display:block'><img id=b style='display:block;width:100px'><img id=c style='display:block;height:25px'>" +
            "<img id=d style='display:block;max-width:100px'><img id=e style='display:block;min-width:400px'><img id=f style='display:block;width:60px;height:60px'>" +
            "<img id=g style='display:block;margin:0 auto;width:100px'>", images: images);
        Assert.Equal(new Size(200, 100), Sz(l["a"]));
        Assert.Equal(new Size(100, 50), Sz(l["b"]));
        Assert.Equal(new Size(50, 25), Sz(l["c"]));
        Assert.Equal(new Size(100, 50), Sz(l["d"]));
        Assert.Equal(new Size(400, 200), Sz(l["e"]));
        Assert.Equal(new Size(60, 60), Sz(l["f"]));
        Assert.Equal(350, l["g"].X);
    }

    [Fact]
    public void ReplacedConstraintTableOfCss2Section104()
    {
        var images = new FixedImages(200, 100);
        // w > max-w and h > max-h, max-w / w <= max-h / h: width max-w, height max(min-h, max-w * h / w)
        Lay l = Run("<img id=a style='display:block;max-width:100px;max-height:80px'><img id=b style='display:block;max-width:150px;max-height:20px'><img id=c style='display:block;min-width:300px;min-height:10px;max-height:50px'>", images: images);
        Assert.Equal(new Size(100, 50), Sz(l["a"]));
        Assert.Equal(new Size(40, 20), Sz(l["b"]));
        Assert.Equal(new Size(300, 50), Sz(l["c"])); // w < min-w and h > max-h: (min-w, max-h)
    }

    [Fact]
    public void AnImageWithoutANaturalSizeIs300By150()
    {
        Lay l = Run("<img id=a style='display:block'>");
        Assert.Equal(new Size(300, 150), Sz(l["a"]));
    }

    private static Size Sz(Rect r) => new(r.Width, r.Height);

    private readonly record struct Size(double W, double H);

    // ---- intrinsic sizes ----

    [Fact]
    public void IntrinsicSizesAreCachedPerBoxAndFollowTheChildContributions()
    {
        Lay l = Run("<div id=p><div style='width:100px'></div><div style='width:50px;padding:10px;margin-left:5px'></div><div style='max-width:30px'><div style='width:80px'></div></div></div>");
        Box p = l.P.Boxes.PrincipalBox(l.P.Styled.FindBySource(l.P.Document.GetElementById("p").Index));
        l.Engine.GetIntrinsicSizes(p, out double min, out double max);
        Assert.Equal(100, min);
        Assert.Equal(100, max);
        l.Engine.GetIntrinsicSizes(p, out double min2, out double max2);
        Assert.Equal((min, max), (min2, max2));
    }

    // ---- list items ----

    [Fact]
    public void AnOutsideMarkerIsAFragmentJustBeforeTheItem()
    {
        Lay l = Run("<ul style='margin:0;padding:0 0 0 40px'><li id=a style='height:20px;font-size:10px'>x</li></ul>");
        Fragment li = l.F.FragmentOf(l.P.Boxes.PrincipalBox(l.P.Styled.FindBySource(l.P.Document.GetElementById("a").Index)));
        Fragment marker = li.FirstChild;
        Assert.Equal(BoxKind.Marker, marker.Box.Kind);
        Assert.Equal(-10, marker.BorderBox.X);
        Assert.Equal(40, l["a"].X);
    }

    // ---- dumps are stable and deterministic ----

    [Fact]
    public void LayingOutTwiceGivesTheSameFragments()
    {
        Lay l = Run("<div style='margin:7px'><div style='margin:3px auto;width:50%;padding:2px'></div><p>text</p></div>");
        string first = l.F.Dump();
        l.Engine.Layout(l.P.Boxes, new LayoutInput(800, double.NaN, new NoFonts()), l.F);
        Assert.Equal(first, l.F.Dump());
    }

    [Fact]
    public void CancellationIsHonoured()
    {
        Lay l = Run(string.Concat(System.Linq.Enumerable.Repeat("<div style='height:1px'></div>", 2000)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => l.Engine.Layout(l.P.Boxes, new LayoutInput(800, double.NaN, new NoFonts()), l.F, cts.Token));
    }

    // ---- retained storage and hostile shapes ----

    [Fact]
    public void RelayoutOfAThousandBoxesAllocatesNothing()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            sb.Append("<div style='margin:").Append(i % 7).Append("px auto;padding:2px;width:").Append(300 + i).Append("px'>");
            for (int j = 0; j < 4; j++)
            {
                sb.Append("<div style='margin:").Append(j).Append("px 3px;min-height:").Append(j * 2).Append("px'><div style='height:3px'></div></div>");
            }

            sb.Append("<img style='display:block;width:20px'><span>t</span></div>");
        }

        Lay l = Run(sb.ToString(), images: new FixedImages(10, 10));
        Assert.True(l.P.Boxes.Count >= 1000, "box count " + l.P.Boxes.Count);
        var input = new LayoutInput(800, double.NaN, new NoFonts());
        string dump = l.F.Dump();
        Allocation.AssertZero(() => l.Engine.Layout(l.P.Boxes, input, l.F), "relayout of an unchanged 1,000-box block tree");
        Assert.Equal(dump, l.F.Dump());
    }

    [Fact]
    public void ADeepBlockNestingLaysOutWithoutRecursion()
    {
        const int Depth = 100_000;
        var styled = new StyledTree();
        var boxes = new BoxTree(styled);
        StyleDeclaration block = new StyleDeclaration().Set("display", "block").Set("padding-left", "1px");
        StyledNode n = styled.AddRoot("div", block);
        for (int i = 1; i < Depth; i++)
        {
            n = styled.AddElement(n, "div", block);
        }

        styled.AddElement(n, "div", new StyleDeclaration().Set("display", "block").Set("height", "10px"));
        boxes.Build();
        var f = new FragmentTree(boxes);
        new BlockLayoutEngine().Layout(boxes, new LayoutInput(1_000_000, double.NaN, new NoFonts()), f);
        Assert.Equal(Depth + 1, f.Count);
        Assert.Equal(10, f.Root.BorderBox.Height);
        Assert.Equal(1_000_000, f.Root.BorderBox.Width);
    }

    [Fact]
    public void AWideBlockStackLaysOut()
    {
        var styled = new StyledTree();
        var boxes = new BoxTree(styled);
        StyledNode root = styled.AddRoot("div", new StyleDeclaration().Set("display", "block"));
        StyleDeclaration item = new StyleDeclaration().Set("display", "block").Set("height", "1px").Set("margin", "1px 0");
        for (int i = 0; i < 100_000; i++)
        {
            styled.AddElement(root, "div", item);
        }

        boxes.Build();
        var f = new FragmentTree(boxes);
        new BlockLayoutEngine().Layout(boxes, new LayoutInput(100, double.NaN, new NoFonts()), f);
        Assert.Equal(200_001, f.Root.BorderBox.Height); // the root keeps its first and last child margins inside
        Assert.Equal(200_000, f.Root.LastChild.BorderBox.Bottom);
    }

    // ---- a web-platform-tests reftest checked by geometry ----

    private static Rect[] Boxes(string file, string tag)
    {
        string html = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "testdata", "wpt-block", "css", "CSS2", "normal-flow", file));
        Pipeline p = Pipeline.FromHtml(html, 800, 600);
        var f = new FragmentTree(p.Boxes);
        new BlockLayoutEngine().Layout(p.Boxes, new LayoutInput(800, 600, new NoFonts()), f);
        var rects = new System.Collections.Generic.List<Rect>();
        foreach (RecursiveAI.OneHtml.Node n in p.Document.Root.GetElementsByTagName(tag))
        {
            Assert.True(f.TryGetBorderBox(n, out Rect r));
            rects.Add(r);
        }

        return rects.ToArray();
    }

    [Fact]
    public void NegativeMarginReftestBoxesEqualTheReferenceBoxes()
    {
        // css/CSS2/normal-flow/negative-margin-001.html: LTR, RTL, and block formatting context roots all place the
        // negative-margin box at the same place, which is what the reference draws four times.
        Rect[] test = Boxes("negative-margin-001.html", "inner");
        Rect[] reference = Boxes("negative-margin-001-ref.html", "inner");
        Assert.Equal(4, test.Length);
        Assert.Equal(reference, test);
        Assert.Equal(new Rect(10 + 10 - 20, 10, 170, 30), test[0]); // html margin 10, outer border 10, inner margin -20; 100 + 20 + 50 wide
    }
}
