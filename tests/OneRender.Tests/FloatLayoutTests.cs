using System;
using System.Linq;
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

/// <summary>Floats and clearance: CSS2 §9.5.1 placement, §10.3.5/§10.3.6 widths, §10.6.6/§10.6.7 heights, §9.5.2 clearance, boxes beside floats, and the exclusion query.</summary>
public class FloatLayoutTests
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

        public Fragment Frag(string id) => F.FragmentOf(P.Boxes.PrincipalBox(P.Styled.FindBySource(P.Document.GetElementById(id).Index)));

        /// <summary>The border box in the root's coordinates.</summary>
        public Rect this[string id] => F.AbsoluteBorderBox(Frag(id));

        /// <summary>The first line box of a block, in the root's coordinates.</summary>
        public Rect Line(string id)
        {
            Fragment f = Frag(id).FirstChild;
            while (!f.IsNull && f.Kind != FragmentKind.Line)
            {
                f = f.NextSibling;
            }

            Assert.False(f.IsNull, "no line in #" + id);
            return F.AbsoluteBorderBox(f);
        }
    }

    private static Lay Run(string body, string css = "", double width = 800, IResourceResolver? images = null)
    {
        string html = "<!DOCTYPE html><html><head><style>body{margin:0}" + css + "</style></head><body>" + body + "</body></html>";
        Pipeline p = Pipeline.FromHtml(html, width, 600, resolver: images);
        var f = new FragmentTree(p.Boxes);
        var engine = new BlockLayoutEngine();
        engine.Layout(p.Boxes, new LayoutInput(width, double.NaN, new NoFonts()), f);
        return new Lay { P = p, F = f, Engine = engine };
    }

    private static string Fl(string id, string side, double w, double h, string extra = "") =>
        "<div id=" + id + " style='float:" + side + ";width:" + w + "px;height:" + h + "px;" + extra + "'></div>";

    // ---- CSS2 §9.5.1: the placement rules ----

    [Fact]
    public void Rule1AFloatStaysInsideItsContainingBlock()
    {
        Lay l = Run(Fl("a", "left", 100, 50) + Fl("b", "right", 100, 50));
        Assert.Equal(new Rect(0, 0, 100, 50), l["a"]);
        Assert.Equal(new Rect(700, 0, 100, 50), l["b"]);
    }

    [Fact]
    public void Rule2AndRule9LeftFloatsGoSideBySideAndWrapBelowWhenTheyDoNotFit()
    {
        Lay l = Run(Fl("a", "left", 300, 50) + Fl("b", "left", 300, 40) + Fl("c", "left", 300, 10));
        Assert.Equal(new Rect(0, 0, 300, 50), l["a"]);
        Assert.Equal(new Rect(300, 0, 300, 40), l["b"]);
        // 900 > 800, so c cannot start at the top; it starts as soon as b ends (rule 8) and goes as far left as a allows (rule 9).
        Assert.Equal(new Rect(300, 40, 300, 10), l["c"]);
    }

    [Fact]
    public void Rule2RightFloatsStackLeftwards()
    {
        Lay l = Run(Fl("a", "right", 300, 50) + Fl("b", "right", 300, 40) + Fl("c", "right", 300, 10));
        Assert.Equal(new Rect(500, 0, 300, 50), l["a"]);
        Assert.Equal(new Rect(200, 0, 300, 40), l["b"]);
        Assert.Equal(new Rect(200, 40, 300, 10), l["c"]);
    }

    [Fact]
    public void Rule3ALeftFloatsRightEdgeMayNotPassARightFloatsLeftEdge()
    {
        Lay l = Run(Fl("a", "left", 500, 50) + Fl("b", "right", 400, 30));
        Assert.Equal(new Rect(0, 0, 500, 50), l["a"]);
        // 500 + 400 > 800, so the right float cannot sit next to the left one and goes below it.
        Assert.Equal(new Rect(400, 50, 400, 30), l["b"]);
    }

    [Fact]
    public void Rule4AFloatIsNotHigherThanItsContainingBlock()
    {
        Lay l = Run("<div style='height:100px'></div>" + Fl("a", "left", 10, 10)
            + "<div id=c style='margin-top:30px;padding-top:1px'>" + Fl("f", "left", 10, 10) + "</div>");
        Assert.Equal(new Rect(0, 100, 10, 10), l["a"]);
        // The container is below its collapsed margin from the float's sibling; the float starts at the container's content top.
        Rect c = l["c"];
        Rect f = l["f"];
        Assert.Equal(c.Y + 1, f.Y);
        Assert.Equal(c.X, f.X);
    }

    [Fact]
    public void Rule5AFloatIsNotHigherThanAnEarlierFloat()
    {
        // c would fit beside a at the top, but b was pushed down and c may not start above b.
        Lay l = Run(Fl("a", "left", 500, 50) + Fl("b", "left", 400, 50) + Fl("c", "left", 50, 10));
        Assert.Equal(new Rect(0, 50, 400, 50), l["b"]);
        Assert.Equal(new Rect(400, 50, 50, 10), l["c"]);
    }

    [Fact]
    public void Rule6AFloatInLineContentSitsAtTheTopOfTheLineAndShortensIt()
    {
        Lay l = Run("<div id=t>text" + Fl("f", "left", 100, 40) + "</div>");
        Assert.Equal(new Rect(0, 0, 100, 40), l["f"]);
        Rect line = l.Line("t");
        Assert.Equal(100, line.X);
        Assert.Equal(700, line.Width);
        Assert.Equal(0, line.Y);
    }

    [Fact]
    public void Rule7AFloatWiderThanItsContainingBlockSitsAgainstTheStartEdge()
    {
        Lay l = Run(Fl("a", "left", 1000, 10) + Fl("b", "right", 1000, 10));
        Assert.Equal(0, l["a"].X);
        Assert.Equal(-200, l["b"].X);
        Assert.Equal(10, l["b"].Y);
    }

    [Fact]
    public void Rule8AFloatGoesAsHighAsItFits()
    {
        // The left float a and the right float b leave 600 px at the top; c needs 650, so it waits for a to end at 50.
        Lay l = Run(Fl("a", "left", 100, 50) + Fl("b", "right", 100, 100) + Fl("c", "left", 650, 10));
        Assert.Equal(new Rect(0, 50, 650, 10), l["c"]);
    }

    [Fact]
    public void FloatsTakeTheMarginBoxIntoAccount()
    {
        Lay l = Run(Fl("a", "left", 100, 50, "margin:5px 7px 3px 9px") + Fl("b", "left", 100, 50, "margin-left:4px"));
        Assert.Equal(new Rect(9, 5, 100, 50), l["a"]);
        // a's margin box ends at 9 + 100 + 7 = 116, and b's left margin is 4.
        Assert.Equal(new Rect(120, 0, 100, 50), l["b"]);
    }

    [Fact]
    public void LogicalFloatAndClearValuesFollowTheDirection()
    {
        Lay l = Run("<div style='direction:rtl'>" + Fl("a", "inline-start", 100, 20) + Fl("b", "inline-end", 100, 20) + "</div>");
        Assert.Equal(700, l["a"].X);
        Assert.Equal(0, l["b"].X);
        Lay m = Run(Fl("f", "left", 100, 50) + "<div id=p style='clear:inline-start;height:5px'></div>");
        Assert.Equal(50, m["p"].Y);
    }

    [Fact]
    public void AFloatInsideTheFlowSitsAfterThePrecedingBlocksAndTheirMargins()
    {
        Lay l = Run("<div style='height:20px;margin-bottom:10px'></div>" + Fl("f", "left", 10, 10));
        Assert.Equal(30, l["f"].Y);
    }

    [Fact]
    public void AFloatThatStartsAMarginRunMovesWithTheMarginThatCollapsesThroughItsContainer()
    {
        // The float waits for the container's final position: the larger margin of the paragraph collapses through it.
        Lay l = Run("<div id=c style='margin-top:20px'>" + Fl("f", "left", 10, 10) + "<p id=p style='margin:50px 0 0;height:10px'></p></div>");
        Assert.Equal(50, l["c"].Y);
        Assert.Equal(50, l["p"].Y);
        Assert.Equal(50, l["f"].Y);
    }

    // ---- CSS2 §10.3.5, §10.3.6: widths ----

    [Fact]
    public void AutoWidthIsShrinkToFit()
    {
        Lay l = Run("<div id=f style='float:left'><div style='width:200px;height:10px'></div></div>"
            + "<div id=g style='float:left;padding:0 10px;border:2px solid'><div style='width:200px;height:10px'></div></div>");
        Assert.Equal(200, l["f"].Width);
        Assert.Equal(224, l["g"].Width);
        Assert.Equal(200, l["g"].X);
    }

[Fact]
    public void ShrinkToFitUsesTheMinimumWhenTheAvailableWidthIsSmaller()
    {
        Lay l = Run("<div style='width:150px'><div id=f style='float:left'><div style='width:300px;height:10px'></div></div></div>");
        Assert.Equal(300, l["f"].Width);
        Lay m = Run("<div style='width:150px'><div id=f style='float:left;width:50%;height:1px'></div></div>");
        Assert.Equal(75, m["f"].Width);
    }

    [Fact]
    public void AutoMarginsOfAFloatAreZero()
    {
        Lay l = Run("<div id=f style='float:left;width:100px;height:10px;margin:0 auto'></div>");
        Assert.Equal(new Rect(0, 0, 100, 10), l["f"]);
    }

    [Fact]
    public void AFloatedReplacedBoxUsesItsNaturalSize()
    {
        Lay l = Run("<img id=i src=x style='float:left'><img id=j src=x style='float:right;width:50%'>", images: new FixedImages(120, 80));
        Assert.Equal(new Rect(0, 0, 120, 80), l["i"]);
        Assert.Equal(new Rect(400, 0, 400, 266.6666666666667), l["j"]);
    }

    // ---- CSS2 §10.6.6, §10.6.7: heights ----

    [Fact]
    public void ABlockFormattingContextRootGrowsToItsFloatsAndOtherBlocksDoNot()
    {
        Lay l = Run("<div id=a style='overflow:hidden'>" + Fl("f", "left", 100, 70, "margin-bottom:5px") + "</div><div id=b>" + Fl("g", "left", 100, 70) + "</div>");
        Assert.Equal(75, l["a"].Height);
        Assert.Equal(0, l["b"].Height);
    }

    [Fact]
    public void AFloatContainsItsOwnFloats()
    {
        Lay l = Run("<div id=f style='float:left;width:100px'>" + Fl("g", "left", 50, 30) + "</div>");
        Assert.Equal(30, l["f"].Height);
    }

    [Fact]
    public void ABlockFormattingContextRootsFloatsAreInItsScrollableOverflow()
    {
        Lay l = Run("<div id=a style='overflow:hidden;display:flow-root;height:10px'>" + Fl("f", "left", 100, 70) + "</div>");
        Assert.Equal(10, l["a"].Height);
        Assert.Equal(70, l.Frag("a").ScrollableOverflow.Height);
    }

    // ---- CSS2 §9.5.2: clear ----

    [Fact]
    public void ClearMovesABlockBelowTheFloat()
    {
        Lay l = Run(Fl("f", "left", 100, 100) + "<div id=p style='clear:left;height:10px'></div><div id=q style='clear:right;height:10px'></div>");
        Assert.Equal(100, l["p"].Y);
        // q clears right floats only and there are none: it follows p.
        Assert.Equal(110, l["q"].Y);
    }

    [Fact]
    public void ClearBothClearsFloatsOnEitherSide()
    {
        Lay l = Run(Fl("f", "left", 100, 30) + Fl("g", "right", 100, 80) + "<div id=p style='clear:both;height:10px'></div>");
        Assert.Equal(80, l["p"].Y);
    }

    [Fact]
    public void ClearanceAbsorbsTheCollapsedMargins()
    {
        // The float starts after p1's margin, at 80, and ends at 180. p2's hypothetical position is 50 + max(30, 40) = 90, above
        // the float's bottom, so clearance puts its border edge at 180 (the clearance itself is negative: 180 - 40 - 80 = 60 is
        // measured from the margin edge, and the margin is not added on top).
        Lay l = Run("<div id=p1 style='height:50px;margin-bottom:30px'></div>" + Fl("f", "left", 100, 100)
            + "<div id=p2 style='clear:left;margin-top:40px;height:10px'></div><div id=p3 style='margin-top:7px;height:1px'></div>");
        Assert.Equal(80, l["f"].Y);
        Assert.Equal(180, l["p2"].Y);
        Assert.Equal(197, l["p3"].Y);
    }

    [Fact]
    public void NoClearanceIsIntroducedWhenTheHypotheticalPositionIsAlreadyPastTheFloat()
    {
        Lay l = Run("<div id=p1 style='height:50px;margin-bottom:30px'></div>" + Fl("f", "left", 100, 5)
            + "<div id=p2 style='clear:left;margin-top:40px;height:10px'></div>");
        // The margins collapse as they would without clear: 50 + max(30, 40).
        Assert.Equal(90, l["p2"].Y);
    }

    [Fact]
    public void AClearedFirstChildDoesNotCollapseItsMarginWithItsParents()
    {
        Lay l = Run("<div style='height:5px'></div>" + Fl("f", "left", 100, 100)
            + "<div id=par style='margin-top:10px'><div id=c style='clear:left;height:10px'></div></div>");
        // The parent's own margin still applies (5 + 10); the child goes to the float's bottom edge (5 + 100).
        Assert.Equal(15, l["par"].Y);
        Assert.Equal(105, l["c"].Y);
        Assert.Equal(100, l["par"].Height);
    }

    [Fact]
    public void AFloatWithClearGoesBelowEarlierFloatsOnThatSide()
    {
        Lay l = Run(Fl("a", "left", 100, 50) + Fl("b", "right", 100, 20) + Fl("c", "right", 100, 10, "clear:left"));
        Assert.Equal(new Rect(700, 50, 100, 10), l["c"]);
        Lay m = Run(Fl("a", "left", 100, 50) + Fl("b", "right", 100, 20, "clear:right"));
        Assert.Equal(0, m["b"].Y);
    }

    // ---- CSS2 §9.5: boxes beside floats ----

    [Fact]
    public void ABlockFormattingContextRootNarrowsBesideAFloat()
    {
        Lay l = Run(Fl("f", "left", 200, 100) + Fl("r", "right", 100, 100) + "<div id=b style='overflow:hidden;height:20px'></div>");
        Assert.Equal(new Rect(200, 0, 500, 20), l["b"]);
    }

    [Fact]
    public void ABlockFormattingContextRootThatDoesNotFitMovesBelowTheFloat()
    {
        Lay l = Run(Fl("f", "left", 200, 100) + "<div id=b style='overflow:hidden;width:700px;height:20px'></div><div id=c style='overflow:hidden;width:500px;height:20px'></div>");
        Assert.Equal(new Rect(0, 100, 700, 20), l["b"]);
        // c would have fit beside the float, but b is already below it.
        Assert.Equal(new Rect(0, 120, 500, 20), l["c"]);
    }

    [Fact]
    public void ABlockLevelReplacedBoxAvoidsFloats()
    {
        Lay l = Run(Fl("f", "left", 200, 100) + "<img id=i src=x style='display:block;width:300px;height:10px'><img id=j src=x style='display:block;width:700px;height:10px'>", images: new FixedImages(10, 10));
        Assert.Equal(new Rect(200, 0, 300, 10), l["i"]);
        Assert.Equal(new Rect(0, 100, 700, 10), l["j"]);
    }

    [Fact]
    public void AnOrdinaryBlockIgnoresFloatsButItsLinesDoNot()
    {
        Lay l = Run(Fl("f", "left", 200, 100) + "<div id=b>text</div>");
        Assert.Equal(new Rect(0, 0, 800, l["b"].Height), l["b"]);
        Rect line = l.Line("b");
        Assert.Equal(200, line.X);
        Assert.Equal(600, line.Width);
    }

    [Fact]
    public void ALineWithNoRoomGoesBelowTheFloat()
    {
        Lay l = Run(Fl("f", "left", 800, 50) + "<div id=b>text</div>");
        Rect line = l.Line("b");
        Assert.Equal(50, line.Y);
        Assert.Equal(800, line.Width);
        Assert.Equal(50 + 19.2, l["b"].Height, 6);
    }

    [Fact]
    public void FloatsInNestedBlocksShareOneBlockFormattingContext()
    {
        Lay l = Run("<div style='padding-left:30px'><div style='width:300px'>" + Fl("a", "left", 100, 60) + "</div></div>" + "<div style='padding-left:10px'><div id=t>text</div></div>");
        Assert.Equal(30, l["a"].X);
        // The second container's line starts at the container's own left edge (10) and clears a's right edge (130).
        Rect line = l.Line("t");
        Assert.Equal(130, line.X);
        Assert.Equal(670, line.Width);
    }

    // ---- the exclusion query ----

    private sealed class Probe : IInlineLayout
    {
        public double Start, End, Next, StartBelow, EndBelow;

        public InlineLayoutResult Layout(in InlineLayoutRequest request, FragmentTree output, Fragment parent, CancellationToken cancellationToken)
        {
            request.Exclusions.Query(request.BfcTop, 10, request.BfcLeft, request.BfcLeft + request.AvailableWidth, out Start, out End, out Next);
            request.Exclusions.Query(Next, 10, request.BfcLeft, request.BfcLeft + request.AvailableWidth, out StartBelow, out EndBelow, out _);
            return new InlineLayoutResult(0, 0, double.NaN, double.NaN);
        }

        public void Measure(Box container, IFontProvider fonts, out double minContent, out double maxContent)
        {
            minContent = maxContent = 0;
        }
    }

    [Fact]
    public void TheQueryReportsTheRoomAndTheNextEdge()
    {
        string html = "<!DOCTYPE html><html><head><style>body{margin:0}</style></head><body>" + Fl("a", "left", 100, 30) + Fl("b", "right", 50, 60) + "<div>x</div></body></html>";
        Pipeline p = Pipeline.FromHtml(html);
        var probe = new Probe();
        var f = new FragmentTree(p.Boxes);
        new BlockLayoutEngine(probe).Layout(p.Boxes, new LayoutInput(800, double.NaN, new NoFonts()), f);
        Assert.Equal(100, probe.Start);
        Assert.Equal(750, probe.End);
        Assert.Equal(30, probe.Next);
        Assert.Equal(0, probe.StartBelow);
        Assert.Equal(750, probe.EndBelow);
    }

    // ---- storage and limits ----

    [Fact]
    public void RelayoutOfAThousandBoxesWithTwoHundredFloatsAllocatesNothing()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            sb.Append("<div style='overflow:hidden;margin:").Append(i % 5).Append("px;padding:2px'>");
            sb.Append("<div style='float:left;width:").Append(30 + i).Append("px;height:").Append(10 + i % 9).Append("px;margin:1px'><div style='height:3px'></div></div>");
            sb.Append("<div style='float:right;width:40px;height:20px;clear:").Append(i % 3 == 0 ? "right" : "none").Append("'></div>");
            for (int j = 0; j < 3; j++)
            {
                sb.Append("<div style='margin:").Append(j).Append("px 3px;min-height:").Append(j * 2).Append("px;").Append(j == 1 ? "clear:left;" : string.Empty).Append("'><div style='height:3px'></div></div>");
            }

            sb.Append("<span>t</span></div>");
        }

        Lay l = Run(sb.ToString());
        Assert.True(l.P.Boxes.Count >= 1000, "box count " + l.P.Boxes.Count);
        var input = new LayoutInput(800, double.NaN, new NoFonts());
        string dump = l.F.Dump();
        Allocation.AssertZero(() => l.Engine.Layout(l.P.Boxes, input, l.F), "relayout of a 1,000-box tree with 200 floats");
        Assert.Equal(dump, l.F.Dump());
    }

    [Fact]
    public void TenThousandFloatsInOneContextLayOutInBoundedTime()
    {
        string body = string.Concat(Enumerable.Repeat("<div style='float:left;width:30px;height:20px'></div>", 10_000));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Lay l = Run(body);
        sw.Stop();
        Assert.DoesNotContain(l.P.Styled.Diagnostics.Items, d => d.Kind == DiagnosticKind.LimitExceeded);
        // 800 / 30 = 26 floats per row, 385 rows of 20 px.
        Assert.True(sw.ElapsedMilliseconds < 5000, "took " + sw.ElapsedMilliseconds + " ms");
        Fragment last = l.F.Root.LastChild;
        Assert.False(last.IsNull);
    }

    [Fact]
    public void FloatsThatAllStayInTheWayHitTheWorkCapWithADiagnostic()
    {
        // Every float is tall and narrow, so each new float has to look at all the earlier ones: quadratic without a cap.
        string body = string.Concat(Enumerable.Repeat("<div style='float:left;width:0.05px;height:100000px'></div>", 12_000));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Lay l = Run(body);
        sw.Stop();
        Assert.Contains(l.P.Styled.Diagnostics.Items, d => d.Kind == DiagnosticKind.LimitExceeded && d.Message.Contains("floats", StringComparison.Ordinal));
        Assert.True(sw.ElapsedMilliseconds < 10_000, "took " + sw.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void DeeplyNestedFloatsLayOutWithoutRecursion()
    {
        const int Depth = 3000;
        var sb = new StringBuilder();
        for (int i = 0; i < Depth; i++)
        {
            sb.Append("<div style='float:left'>");
        }

        sb.Append("<div style='width:10px;height:10px'></div>");
        for (int i = 0; i < Depth; i++)
        {
            sb.Append("</div>");
        }

        Lay l = Run(sb.ToString());
        Assert.True(l.P.Boxes.Count > Depth / 2);
    }

    [Fact]
    public void FloatsInInlineContentNestedDeeperThanTheLimitAreDropped()
    {
        const int Depth = 80;
        var sb = new StringBuilder();
        for (int i = 0; i < Depth; i++)
        {
            sb.Append("<div style='float:left'>t");
        }

        for (int i = 0; i < Depth; i++)
        {
            sb.Append("</div>");
        }

        Lay l = Run(sb.ToString());
        Assert.NotNull(l.F);
    }
}
