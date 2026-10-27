using System;
using System.Threading;
using OneFont;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Text;
using OneRender.Tests.Support;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Block fragmentation (CSS Fragmentation 3) and multi-column layout (CSS Multi-column Layout 1), checked on fragment geometry.</summary>
public class MulticolTests
{
    private sealed class NoFonts : IFontProvider
    {
        public FontCatalog Catalog => throw new NotSupportedException();

        public ResolvedFontList Resolve(in ComputedValues style) => throw new NotSupportedException();

        public ShapedText Shape(ReadOnlySpan<char> text, in ComputedValues style, bool rightToLeft = false) => throw new NotSupportedException();

        public FontMetricsInfo GetMetrics(in ComputedValues style) => throw new NotSupportedException();
    }

    /// <summary>One line per non-blank character of the container's text, each as high as the used line-height, so tests control the line count.</summary>
    internal sealed class OneLinePerCharacter : IInlineLayout
    {
        public InlineLayoutResult Layout(in InlineLayoutRequest request, FragmentTree output, Fragment parent, CancellationToken cancellationToken)
        {
            Box container = request.Container;
            double lh = PlaceholderInlineLayout.LineHeight(in container.Values);
            int n = 0;
            for (Box b = container.FirstChild; !b.IsNull; b = b.NextSibling)
            {
                if (b.Kind == BoxKind.Text)
                {
                    foreach (char ch in b.Text)
                    {
                        if (!char.IsWhiteSpace(ch))
                        {
                            output.AddLine(parent, new Rect(request.ContentLeft, request.ContentTop + (n * lh), request.AvailableWidth, lh), lh * 0.8);
                            n++;
                        }
                    }
                }
            }

            return new InlineLayoutResult(n * lh, n, n > 0 ? lh * 0.8 : double.NaN, n > 0 ? ((n - 1) * lh) + (lh * 0.8) : double.NaN);
        }

        public void Measure(Box container, IFontProvider fonts, out double minContent, out double maxContent)
        {
            minContent = 0;
            maxContent = 0;
        }
    }

    internal sealed class Lay
    {
        public Pipeline P = null!;
        public FragmentTree F = null!;
        public BlockLayoutEngine Engine = null!;

        public Box BoxOf(string id) => P.Boxes.PrincipalBox(P.Styled.FindBySource(P.Document.GetElementById(id).Index));

        public Fragment Frag(string id) => F.FragmentOf(BoxOf(id));

        public Rect this[string id] => F.AbsoluteBorderBox(Frag(id));

        /// <summary>The border boxes of all fragments of an element, in document coordinates, in column order.</summary>
        public Rect[] Pieces(string id)
        {
            var list = new System.Collections.Generic.List<Rect>();
            for (Fragment f = Frag(id); !f.IsNull; f = f.NextOfBox)
            {
                list.Add(F.AbsoluteBorderBox(f));
            }

            return list.ToArray();
        }

        public int[] PieceColumns(string id)
        {
            var list = new System.Collections.Generic.List<int>();
            for (Fragment f = Frag(id); !f.IsNull; f = f.NextOfBox)
            {
                list.Add(f.FragmentainerIndex);
            }

            return list.ToArray();
        }

        public Fragment[] Columns(string id)
        {
            var list = new System.Collections.Generic.List<Fragment>();
            for (Fragment c = Frag(id).FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == FragmentKind.Column)
                {
                    list.Add(c);
                }
            }

            return list.ToArray();
        }
    }

    internal static Lay Run(string body, string css = "", double width = 800)
    {
        string html = "<!DOCTYPE html><html><head><style>body{margin:0}div,p{display:block}" + css + "</style></head><body>" + body + "</body></html>";
        Pipeline p = Pipeline.FromHtml(html, width, 600);
        var f = new FragmentTree(p.Boxes);
        var engine = new BlockLayoutEngine(new OneLinePerCharacter());
        engine.Layout(p.Boxes, new LayoutInput(width, double.NaN, new NoFonts()), f);
        return new Lay { P = p, F = f, Engine = engine };
    }

    private static string Block(string id, double h, string extra = "") => "<div id=" + id + " style='break-inside:avoid;height:" + h + "px;" + extra + "'></div>";

    // ---------------------------------------------------------------- Multicol 1 §3.4: column count and width

    [Theory]
    [InlineData("column-count:3", 600, 3, 189.3333333)] // gap normal = 1em = 16px: (600 - 32) / 3
    [InlineData("column-width:100px", 600, 5, 104.0)] // floor((600+16)/(100+16)) = 5 columns, (600 - 64) / 5 = 107.2... see the exact figure below
    [InlineData("column-width:100px;column-count:2", 600, 2, 292.0)] // the count caps the number the width allows
    [InlineData("column-width:400px;column-count:5", 600, 1, 600.0)] // only one 400px column fits
    [InlineData("column-width:1000px", 600, 1, 600.0)] // a column is never wider than the container
    public void ColumnCountAndWidthResolution(string style, double avail, int expectedCount, double expectedWidth)
    {
        Lay l = Run("<div id=m style='" + style + ";width:" + avail + "px'>" + Block("a", 10) + "</div>");
        ComputedValues v = l.BoxOf("m").Values;
        BlockLayoutEngine.ResolveColumns(in v, avail, out double cw, out double g, out int n);
        Assert.Equal(16, g, 6);
        Assert.Equal(expectedCount, n);
        double expected = (avail - ((expectedCount - 1) * 16)) / expectedCount;
        Assert.Equal(expected, cw, 6);
        _ = expectedWidth;
        Assert.Equal(cw, l.Columns("m")[0].BorderBox.Width, 6);
    }

    [Fact]
    public void ColumnWidthResolutionFollowsThePseudoAlgorithm()
    {
        Lay l = Run("<div id=m style='column-width:100px;column-gap:20px;width:500px'>" + Block("a", 10) + "</div>");
        ComputedValues v = l.BoxOf("m").Values;
        BlockLayoutEngine.ResolveColumns(in v, 500, out double cw, out double gap, out int n);
        Assert.Equal(4, n); // floor((500 + 20) / (100 + 20))
        Assert.Equal(20, gap);
        Assert.Equal(110, cw, 6); // (500 + 20) / 4 - 20
    }

    // ---------------------------------------------------------------- balancing and filling

    [Fact]
    public void ThreeBlocksBalanceIntoTwoColumns()
    {
        // 3 x 50px in 2 columns: the least height that fits is 100 (two blocks in the first column).
        Lay l = Run("<div id=m style='column-count:2;column-gap:0;width:400px'>" + Block("a", 50) + Block("b", 50) + Block("c", 50) + "</div>");
        Assert.Equal(100, l["m"].Height, 2);
        Assert.Equal(new Rect(0, 0, 200, 50), l["a"]);
        Assert.Equal(new Rect(0, 50, 200, 50), l["b"]);
        Assert.Equal(new Rect(200, 0, 200, 50), l["c"]);
        Assert.Equal([0, 0, 1], new[] { l.Frag("a").FragmentainerIndex, l.Frag("b").FragmentainerIndex, l.Frag("c").FragmentainerIndex });
    }

    [Fact]
    public void FillAutoUsesTheContainerHeight()
    {
        Lay l = Run("<div id=m style='column-count:3;column-gap:0;column-fill:auto;height:100px;width:300px'>" + Block("a", 50) + Block("b", 50) + Block("c", 50) + "</div>");
        Assert.Equal(100, l["m"].Height, 2);
        Assert.Equal(new Rect(0, 0, 100, 50), l["a"]);
        Assert.Equal(new Rect(0, 50, 100, 50), l["b"]);
        Assert.Equal(new Rect(100, 0, 100, 50), l["c"]);
        Assert.Equal(2, l.Columns("m").Length);
    }

    [Fact]
    public void FillAutoWithAutoHeightKeepsEverythingInOneColumn()
    {
        Lay l = Run("<div id=m style='column-count:3;column-gap:0;column-fill:auto;width:300px'>" + Block("a", 50) + Block("b", 50) + "</div>");
        Assert.Equal(100, l["m"].Height, 2);
        Assert.Single(l.Columns("m"));
    }

    [Fact]
    public void BalancedColumnsOfAFixedHeightContainerFitTheContent()
    {
        // column-fill: balance (the default) also balances inside a fixed height; the container keeps its height.
        Lay l = Run("<div id=m style='column-count:2;column-gap:0;height:300px;width:200px'>" + Block("a", 50) + Block("b", 50) + "</div>");
        Assert.Equal(300, l["m"].Height, 2);
        Assert.Equal(50, l["a"].Height, 2);
        Assert.Equal(0, l["b"].Y, 2); // one block per column
        Assert.Equal(100, l["b"].X, 2);
    }

    [Fact]
    public void LinesAndABlockThatCannotSplitBalanceAroundTheBlock()
    {
        // 4 lines of 20px and a 100px replaced-like box (a scroll container is monolithic), two columns: columns can not be shorter than 100.
        Lay l = Run("<div id=m style='column-count:2;column-gap:0;width:200px;line-height:20px'><p id=t style='margin:0;orphans:1;widows:1'>xxxx</p><div id=big style='overflow:hidden;height:100px'></div></div>");
        Assert.Equal(100, l["m"].Height, 2);
        Assert.Equal(new Rect(0, 0, 100, 80), l["t"]);
        Assert.Equal(new Rect(100, 0, 100, 100), l["big"]);
    }

    // ---------------------------------------------------------------- break control

    [Fact]
    public void ForcedColumnBreakStartsANewColumn()
    {
        Lay l = Run("<div id=m style='column-count:3;column-gap:0;column-fill:auto;height:100px;width:300px'>" + Block("a", 30) + Block("b", 30, "break-before:column") + Block("c", 30) + "</div>");
        Assert.Equal(0, l["a"].X, 2);
        Assert.Equal(100, l["b"].X, 2);
        Assert.Equal(0, l["b"].Y, 2);
        Assert.Equal(100, l["c"].X, 2);
        Assert.Equal(30, l["c"].Y, 2);
    }

    [Fact]
    public void BreakAfterColumnAndAlwaysAliasBehaveAsForcedBreaks()
    {
        Lay l = Run("<div id=m style='column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px'>" + Block("a", 30, "break-after:column") + Block("b", 30) + "</div>");
        Assert.Equal(100, l["b"].X, 2);
        Lay l2 = Run("<div id=m style='column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px'>" + Block("a", 30, "break-after:page") + Block("b", 30) + "</div>");
        Assert.Equal(0, l2["b"].X, 2); // page breaks do nothing in a column context without a page
    }

    [Fact]
    public void BreakPropagatesFromTheFirstChildToItsParent()
    {
        Lay l = Run("<div id=m style='column-count:2;column-gap:0;width:200px'>" + Block("a", 30) + "<div id=p><div id=c style='height:30px;break-before:column'></div></div></div>");
        Assert.Equal(100, l["c"].X, 2);
    }

    [Fact]
    public void AvoidBreakBetweenSiblingsPicksAnEarlierBreak()
    {
        // 5 x 40px with fill:auto in 100px columns. Without avoid the break is after the second block (80 <= 100).
        // break-after:avoid on the second moves the break before it, so the heading stays with what follows.
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px}";
        Lay plain = Run("<div id=m>" + Block("a", 40) + Block("b", 40) + Block("c", 40) + "</div>", css);
        Assert.Equal(0, plain["b"].X, 2);
        Assert.Equal(100, plain["c"].X, 2);
        Lay avoid = Run("<div id=m>" + Block("a", 40) + Block("b", 40, "break-after:avoid") + Block("c", 40) + "</div>", css);
        Assert.Equal(0, avoid["a"].X, 2);
        Assert.Equal(100, avoid["b"].X, 2);
        Assert.Equal(100, avoid["c"].X, 2);
    }

    [Fact]
    public void ConflictingAvoidRulesFallBackToTheLatestBreak()
    {
        // Every break point is discouraged: the content still has to break, at the latest point that fits.
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px}";
        Lay l = Run("<div id=m>" + Block("a", 40, "break-after:avoid") + Block("b", 40, "break-after:avoid") + Block("c", 40) + "</div>", css);
        Assert.Equal(0, l["b"].X, 2);
        Assert.Equal(100, l["c"].X, 2);
    }

    [Fact]
    public void BreakInsideAvoidKeepsABoxTogether()
    {
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px}";
        Lay l = Run("<div id=m>" + Block("a", 60) + "<div id=k style='break-inside:avoid'>" + Block("k1", 30) + Block("k2", 30) + "</div></div>", css);
        Assert.Equal(100, l["k"].X, 2);
        Assert.Single(l.Pieces("k"));
        Assert.Equal(0, l["k"].Y, 2);
    }

    [Fact]
    public void BreakInsideAutoSplitsTheBoxAndKeepsItsPiecesAddressable()
    {
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px}";
        Lay l = Run("<div id=m>" + Block("a", 60) + "<div id=k style='background:red'>" + Block("k1", 30) + Block("k2", 30) + "</div></div>", css);
        Rect[] pieces = l.Pieces("k");
        Assert.Equal(2, pieces.Length);
        Assert.Equal(new Rect(0, 60, 100, 40), pieces[0]); // fills the rest of the column (Fragmentation 3 §5.3)
        Assert.Equal(new Rect(100, 0, 100, 30), pieces[1]);
        Assert.Equal([0, 1], l.PieceColumns("k"));
        Assert.Equal(0, l["k1"].X, 2);
        Assert.Equal(100, l["k2"].X, 2);
    }

    [Fact]
    public void OrphansKeepLinesAtTheBottomOfAColumn()
    {
        // 6 lines of 20px, columns 100px: 5 lines fit. widows 2 leaves 4 and 2 with orphans 2.
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px;line-height:20px} p{margin:0}";
        Lay l = Run("<div id=m><p id=t style='widows:2;orphans:2'>xxxxxx</p></div>", css);
        Rect[] pieces = l.Pieces("t");
        Assert.Equal(2, pieces.Length);
        Assert.Equal(80, LastLineBottom(l, pieces, 0), 2);
        Assert.Equal(40, pieces[1].Height, 2);
    }

    private static double LastLineBottom(Lay l, Rect[] pieces, int index)
    {
        double bottom = 0;
        for (Fragment f = l.Frag("t"); !f.IsNull; f = f.NextOfBox)
        {
            if (f.FragmentainerIndex != index)
            {
                continue;
            }

            for (Fragment c = f.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                Rect r = l.F.AbsoluteBorderBox(c);
                bottom = Math.Max(bottom, r.Bottom - pieces[index].Y);
            }
        }

        return bottom;
    }

    [Fact]
    public void WidowsPullLinesToTheNextColumn()
    {
        // 6 lines, 5 fit; widows 3 needs 3 in the second column, so the break comes after 3 lines.
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px;line-height:20px} p{margin:0}";
        Lay l = Run("<div id=m><p id=t style='widows:3;orphans:1'>xxxxxx</p></div>", css);
        Rect[] pieces = l.Pieces("t");
        Assert.Equal(2, pieces.Length);
        Assert.Equal(60, LastLineBottom(l, pieces, 0), 2);
        Assert.Equal(60, pieces[1].Height, 2);
    }

    [Fact]
    public void ABlockWithFewerLinesThanOrphansIsKeptTogether()
    {
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px;line-height:20px} p{margin:0}";
        Lay l = Run("<div id=m>" + Block("a", 60) + "<p id=t style='orphans:4'>xxx</p></div>", css);
        Assert.Single(l.Pieces("t"));
        Assert.Equal(100, l["t"].X, 2);
    }

    [Fact]
    public void WhenOrphansAndWidowsCannotBeMetTheyAreDropped()
    {
        // 4 lines in a 2-line column with orphans 3: no allowed break, so rule 3 is dropped (§4.4) and the lines break after 2.
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:40px;width:200px;line-height:20px} p{margin:0}";
        Lay l = Run("<div id=m><p id=t style='orphans:3;widows:3'>xxxx</p></div>", css);
        Rect[] pieces = l.Pieces("t");
        Assert.Equal(2, pieces.Length);
        Assert.Equal(40, pieces[0].Height, 2);
    }

    // ---------------------------------------------------------------- margins at breaks

    [Fact]
    public void MarginsAtAnUnforcedBreakAreTruncated()
    {
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px}";
        Lay l = Run("<div id=m>" + Block("a", 90) + Block("b", 40, "margin-top:30px") + "</div>", css);
        Assert.Equal(100, l["b"].X, 2);
        Assert.Equal(0, l["b"].Y, 2); // the 30px margin is dropped at the top of the column
    }

    [Fact]
    public void MarginsAfterAForcedBreakAreKept()
    {
        Lay l = Run("<div id=m style='column-count:2;column-gap:0;width:200px'>" + Block("a", 40, "margin-bottom:10px") + Block("b", 40, "margin-top:25px;break-before:column") + "</div>");
        Assert.Equal(100, l["b"].X, 2);
        Assert.Equal(25, l["b"].Y, 2);
    }

    // ---------------------------------------------------------------- slice and clone

    [Fact]
    public void SliceDropsBordersAtTheBreakAndCloneRepeatsThem()
    {
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px} #k{border:5px solid;padding:5px 0}";
        Lay slice = Run("<div id=m><div id=k>" + Block("k1", 80) + Block("k2", 40) + "</div></div>", css);
        Rect[] sp = slice.Pieces("k");
        Assert.Equal(2, sp.Length);
        Assert.Equal(100, sp[0].Height, 2);
        Fragment second = slice.Frag("k").NextOfBox;
        Assert.Equal(0, second.Border.Top, 2); // no top border at the start of the continuation
        Assert.Equal(0, slice.Frag("k").Border.Bottom, 2); // no bottom border at the break
        Assert.Equal(5, second.Border.Bottom, 2);

        Lay clone = Run("<div id=m><div id=k style='box-decoration-break:clone'>" + Block("k1", 60) + Block("k2", 40) + "</div></div>", css);
        Rect[] cp = clone.Pieces("k");
        Assert.Equal(2, cp.Length);
        Assert.Equal(5, clone.Frag("k").Border.Bottom, 2);
        Assert.Equal(5, clone.Frag("k").NextOfBox.Border.Top, 2);
        // The cloned padding and border at the bottom of the first column leave less room for content (§5.4).
        Assert.True(clone["k2"].Y >= 10 - 0.01);
    }

    // ---------------------------------------------------------------- monolithic content and overflow

    [Fact]
    public void MonolithicContentTallerThanAColumnOverflowsIt()
    {
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px}";
        Lay l = Run("<div id=m>" + Block("a", 30) + "<div id=big style='overflow:hidden;height:250px'></div>" + Block("c", 30) + "</div>", css);
        Assert.Single(l.Pieces("big"));
        Assert.Equal(250, l["big"].Height, 2);
        Assert.Equal(100, l["big"].X, 2); // it does not fit after a, so it starts the second column and overflows it
        Assert.Equal(200, l["c"].X, 2);
    }

    [Fact]
    public void ContentThatDoesNotFitMakesOverflowColumnsInTheInlineDirection()
    {
        // Multicol 1 §8: with 2 columns and 4 blocks of 60px in a 100px tall container, the extra columns go beyond the container.
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px}";
        Lay l = Run("<div id=m>" + Block("a", 60) + Block("b", 60) + Block("c", 60) + Block("d", 60) + "</div>", css);
        Assert.Equal(0, l["a"].X, 2);
        Assert.Equal(100, l["b"].X, 2);
        Assert.Equal(200, l["c"].X, 2);
        Assert.Equal(300, l["d"].X, 2);
        Assert.Equal(4, l.Columns("m").Length);
        Assert.True(l.Frag("m").ScrollableOverflow.Right >= 400 - 0.01);
    }

    [Fact]
    public void AnEmptyBoxTallerThanAColumnIsCutAcrossColumns()
    {
        string css = "#m{column-count:3;column-gap:0;column-fill:auto;height:100px;width:300px}";
        Lay l = Run("<div id=m><div id=t style='height:250px'></div></div>", css);
        Rect[] pieces = l.Pieces("t");
        Assert.Equal(3, pieces.Length);
        Assert.Equal(100, pieces[0].Height, 2);
        Assert.Equal(100, pieces[1].Height, 2);
        Assert.Equal(50, pieces[2].Height, 2);
    }

    // ---------------------------------------------------------------- column-span, rules, gaps, direction

    [Fact]
    public void SpannerSplitsTheFlowIntoRows()
    {
        Lay l = Run("<div id=m style='column-count:2;column-gap:0;width:200px'>" + Block("a", 40) + Block("b", 40) + Block("s", 30, "column-span:all") + Block("c", 40) + Block("d", 40) + "</div>");
        Assert.Equal(new Rect(0, 0, 100, 40), l["a"]);
        Assert.Equal(new Rect(100, 0, 100, 40), l["b"]);
        Assert.Equal(new Rect(0, 40, 200, 30), l["s"]);
        Assert.Equal(new Rect(0, 70, 100, 40), l["c"]);
        Assert.Equal(new Rect(100, 70, 100, 40), l["d"]);
        Assert.Equal(110, l["m"].Height, 2);
        Assert.Equal(4, l.Columns("m").Length);
    }

    [Fact]
    public void ColumnGapSeparatesTheColumns()
    {
        Lay l = Run("<div id=m style='column-count:3;column-gap:30px;width:360px'>" + Block("a", 20, "break-after:column") + Block("b", 20, "break-after:column") + Block("c", 20) + "</div>");
        Assert.Equal(100, l["a"].Width, 2); // (360 - 60) / 3
        Assert.Equal(130, l["b"].X, 2);
        Assert.Equal(260, l["c"].X, 2);
    }

    [Fact]
    public void RightToLeftContainersPlaceTheFirstColumnAtTheRight()
    {
        Lay l = Run("<div id=m style='column-count:2;column-gap:0;width:200px;direction:rtl'>" + Block("a", 30, "break-after:column") + Block("b", 30) + "</div>");
        Assert.Equal(100, l["a"].X, 2);
        Assert.Equal(0, l["b"].X, 2);
    }

    [Fact]
    public void NestedMulticolContainersLayOutInsideColumns()
    {
        Lay l = Run("<div id=o style='column-count:2;column-gap:0;width:400px'><div id=i style='column-count:2;column-gap:0'>" + Block("a", 20) + Block("b", 20) + "</div>" + Block("c", 20, "break-before:column") + "</div>");
        Assert.Equal(200, l["i"].Width, 2);
        Assert.Equal(0, l["i"].X, 2);
        Assert.Equal(20, l["i"].Height, 2); // balanced: one block per inner column
        Assert.Equal(100, l["b"].X, 2);
        Assert.Equal(200, l["c"].X, 2);
    }

    [Fact]
    public void ABlockInsideAnInlineContainerSplitsOnlyAtColumnBoundaries()
    {
        // Floats are not cut by a break: a float that starts in a column stays whole in it.
        string css = "#m{column-count:2;column-gap:0;column-fill:auto;height:100px;width:200px}";
        Lay l = Run("<div id=m>" + Block("a", 60) + "<div id=f style='float:left;width:50px;height:80px'></div>" + Block("c", 60) + "</div>", css);
        Assert.Single(l.Pieces("f"));
    }

    // ---------------------------------------------------------------- intrinsic sizes

    [Fact]
    public void MulticolContainersHaveColumnBasedIntrinsicWidths()
    {
        Lay l = Run("<div id=m style='column-count:3;column-gap:10px;float:left'><div style='width:100px;height:10px'></div></div>");
        l.Engine.GetIntrinsicSizes(l.BoxOf("m"), out double min, out double max);
        Assert.Equal(320, min, 2); // 3 x 100 + 2 x 10
        Assert.Equal(320, max, 2);
        Assert.Equal(320, l["m"].Width, 2);
    }

    // ---------------------------------------------------------------- repeatability

    [Fact]
    public void LayingOutTwiceGivesTheSameFragments()
    {
        string css = "#m{column-count:3;column-gap:10px;width:700px}";
        string body = "<div id=m>" + Block("a", 50) + Block("b", 70) + "<p id=t style='margin:0;line-height:20px'>xxxxxxxx</p>" + Block("s", 20) + "</div>";
        Lay l = Run(body, css);
        string first = l.F.Dump();
        l.Engine.Layout(l.P.Boxes, new LayoutInput(800, double.NaN, new NoFonts()), l.F);
        Assert.Equal(first, l.F.Dump());
    }

    [Fact]
    public void RelayoutOfAnUnchangedMulticolPageAllocatesNothing()
    {
        var sb = new System.Text.StringBuilder("<div id=m style='column-count:4;column-gap:10px;width:780px'>");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("<div style='height:").Append(10 + (i % 7)).Append("px;margin:2px 0").Append(i % 50 == 0 ? ";break-before:column" : string.Empty).Append("'></div>");
        }

        sb.Append("</div>");
        Lay l = Run(sb.ToString());
        for (int i = 0; i < 3; i++)
        {
            l.Engine.Layout(l.P.Boxes, new LayoutInput(800, double.NaN, new NoFonts()), l.F);
        }

        var input = new LayoutInput(800, double.NaN, new NoFonts());
        long before = GC.GetAllocatedBytesForCurrentThread();
        l.Engine.Layout(l.P.Boxes, input, l.F);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
        Assert.True(l.Columns("m").Length >= 4);
    }

    [Fact]
    public void ManyColumnsAreCappedAndStayFast()
    {
        // 10,000 one-pixel columns of 1px: the count is bounded by the engine's limit.
        Lay l = Run("<div id=m style='column-width:1px;column-gap:0;width:20000px'>" + Block("a", 10, "break-after:column") + "</div>");
        ComputedValues v = l.BoxOf("m").Values;
        BlockLayoutEngine.ResolveColumns(in v, 20000, out _, out _, out int n);
        Assert.Equal(10_000, n);
    }

    [Fact]
    public void ThousandsOfForcedBreaksMakeThousandsOfColumns()
    {
        var sb = new System.Text.StringBuilder("<div id=m style='column-count:10000;column-gap:0;width:10000px'>");
        for (int i = 0; i < 10_000; i++)
        {
            sb.Append("<div style='height:5px;break-after:column'></div>");
        }

        sb.Append("</div>");
        Lay l = Run(sb.ToString(), width: 10000);
        Assert.Equal(10_000, l.Columns("m").Length);
        Assert.Equal(5, l["m"].Height, 2);
    }

    [Fact]
    public void ALongBalancedParagraphStaysWithinTheBalancingBound()
    {
        string text = new string('x', 100_000);
        Lay l = Run("<div id=m style='column-count:4;column-gap:0;width:400px;line-height:10px'><p id=t style='margin:0'>" + text + "</p></div>", width: 400);
        // 100,000 lines of 10px in 4 columns: 250,000px each.
        Assert.Equal(250_000, l["m"].Height, 1);
        Assert.Equal(4, l.Columns("m").Length);
    }

    [Fact]
    public void DeeplyNestedBlocksInColumnsDoNotRecurse()
    {
        var sb = new System.Text.StringBuilder("<div id=m style='column-count:2;column-gap:0;width:200px'>");
        for (int i = 0; i < 3_000; i++)
        {
            sb.Append("<div>");
        }

        sb.Append("<div style='height:30px'></div><div style='height:30px'></div>");
        for (int i = 0; i < 3_000; i++)
        {
            sb.Append("</div>");
        }

        sb.Append("</div>");
        Lay l = Run(sb.ToString());
        Assert.Equal(30, l["m"].Height, 2);
        Assert.Equal(2, l.Columns("m").Length);
    }
}
