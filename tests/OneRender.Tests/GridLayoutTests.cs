using System;
using OneFont;
using OneRender.Layout;
using OneRender.Style;
using OneRender.TestSupport;
using OneRender.Text;
using OneRender.Tests.Support;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Grid layout (CSS Grid 2): placement, track sizing, alignment, subgrid. Text-free boxes only, so sizes are exact.</summary>
public class GridLayoutTests
{
    private sealed class NoFonts : IFontProvider
    {
        public FontCatalog Catalog => throw new NotSupportedException();

        public ResolvedFontList Resolve(in ComputedValues style) => throw new NotSupportedException();

        public ShapedText Shape(ReadOnlySpan<char> text, in ComputedValues style, bool rightToLeft = false) => throw new NotSupportedException();

        public FontMetricsInfo GetMetrics(in ComputedValues style) => throw new NotSupportedException();
    }

    private sealed class FixedImages : IResourceResolver
    {
        public bool TryGetNaturalSize(ReplacedKind kind, ReadOnlySpan<char> source, out double width, out double height)
        {
            width = 20;
            height = 10;
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
    }

    private static Lay Run(string body, string css = "", double width = 800)
    {
        string html = "<!DOCTYPE html><html><head><style>body{margin:0}" + css + "</style></head><body>" + body + "</body></html>";
        Pipeline p = Pipeline.FromHtml(html, width, 600, resolver: new FixedImages());
        var f = new FragmentTree(p.Boxes);
        var engine = new BlockLayoutEngine();
        engine.Layout(p.Boxes, new LayoutInput(width, double.NaN, new NoFonts()), f);
        return new Lay { P = p, F = f, Engine = engine };
    }

    private static void Is(Lay l, string id, double x, double y, double w, double h)
    {
        Rect r = l[id];
        Assert.True(Math.Abs(r.X - x) < 0.01 && Math.Abs(r.Y - y) < 0.01 && Math.Abs(r.Width - w) < 0.01 && Math.Abs(r.Height - h) < 0.01,
            "#" + id + " expected " + x + "," + y + " " + w + "x" + h + " but was " + r.X + "," + r.Y + " " + r.Width + "x" + r.Height);
    }

    [Fact]
    public void ExplicitTracksAndFr()
    {
        Lay l = Run("<div id=g><div id=a></div><div id=b></div><div id=c></div></div>",
            "#g{display:grid;grid-template-columns:100px 1fr 2fr;grid-template-rows:50px 30px;width:720px;gap:10px}");
        Is(l, "g", 0, 0, 720, 90);
        Is(l, "a", 0, 0, 100, 50);
        Is(l, "b", 110, 0, 200, 50);
        Is(l, "c", 320, 0, 400, 50);
    }

    // ---- placement (Grid 2 §8) ----

    [Fact]
    public void AutoPlacementCreatesImplicitRows()
    {
        Lay l = Run("<div id=g><i id=a></i><i id=b></i><i id=c></i><i id=d></i><i id=e></i></div>",
            "#g{display:grid;grid-template-columns:repeat(2,50px);grid-auto-rows:20px;width:100px}i{display:block}");
        Is(l, "a", 0, 0, 50, 20);
        Is(l, "b", 50, 0, 50, 20);
        Is(l, "e", 0, 40, 50, 20);
        Is(l, "g", 0, 0, 100, 60);
    }

    [Fact]
    public void LineBasedPlacementSpansAndNegativeLines()
    {
        Lay l = Run("<div id=g><i id=a></i><i id=b></i><i id=c></i></div>",
            "#g{display:grid;grid-template-columns:repeat(4,25px);grid-template-rows:10px 10px;width:100px}i{display:block}"
            + "#a{grid-column:2 / span 2;grid-row:1}#b{grid-column:-2 / -1;grid-row:2}#c{grid-column:1 / 3;grid-row:2}");
        Is(l, "a", 25, 0, 50, 10);
        Is(l, "b", 75, 10, 25, 10);
        Is(l, "c", 0, 10, 50, 10);
    }

    [Fact]
    public void NamedAreasAndNamedLines()
    {
        Lay l = Run("<div id=g><i id=h></i><i id=s></i><i id=m></i></div>",
            "#g{display:grid;grid-template-columns:[a] 20px [b] 30px [c];grid-template-rows:10px 20px;width:50px;"
            + "grid-template-areas:'head head' 'side main'}i{display:block}"
            + "#h{grid-area:head}#s{grid-area:side}#m{grid-area:main}");
        Is(l, "h", 0, 0, 50, 10);
        Is(l, "s", 0, 10, 20, 20);
        Is(l, "m", 20, 10, 30, 20);
        Lay l2 = Run("<div id=g><i id=x></i></div>", "#g{display:grid;grid-template-columns:[a] 20px [b] 30px [c];width:50px}i{display:block;height:5px;grid-column:b / c}");
        Is(l2, "x", 20, 0, 30, 5);
    }

    [Fact]
    public void DenseFlowBackfills()
    {
        const string Css = "#g{display:grid;grid-template-columns:repeat(3,10px);grid-auto-rows:10px;width:30px}i{display:block}#a{grid-column:span 2}#b{grid-column:span 2}";
        Lay sparse = Run("<div id=g><i id=a></i><i id=b></i><i id=c></i></div>", Css);
        Is(sparse, "c", 20, 10, 10, 10);
        Lay dense = Run("<div id=g><i id=a></i><i id=b></i><i id=c></i></div>", Css + "#g{grid-auto-flow:row dense}");
        Is(dense, "c", 20, 0, 10, 10);
    }

    [Fact]
    public void ColumnFlow()
    {
        Lay l = Run("<div id=g><i id=a></i><i id=b></i><i id=c></i></div>",
            "#g{display:grid;grid-template-rows:repeat(2,10px);grid-auto-columns:15px;grid-auto-flow:column;width:100px}i{display:block}");
        Is(l, "b", 0, 10, 15, 10);
        Is(l, "c", 15, 0, 15, 10);
    }

    [Fact]
    public void OrderChangesPlacement()
    {
        Lay l = Run("<div id=g><i id=a></i><i id=b></i></div>", "#g{display:grid;grid-template-columns:10px 10px;width:20px}i{display:block;height:5px}#a{order:2}");
        Is(l, "b", 0, 0, 10, 5);
        Is(l, "a", 10, 0, 10, 5);
    }

    // ---- auto-repeat (Grid 2 §7.2.3.2) ----

    [Fact]
    public void AutoFillKeepsEmptyTracksAndAutoFitCollapsesThem()
    {
        const string Body = "<div id=g><i id=a></i><i id=b></i></div>";
        Lay fill = Run(Body, "#g{display:grid;grid-template-columns:repeat(auto-fill,minmax(100px,1fr));width:350px}i{display:block;height:5px}");
        Is(fill, "a", 0, 0, 350.0 / 3, 5);
        Is(fill, "b", 350.0 / 3, 0, 350.0 / 3, 5);
        Lay fit = Run(Body, "#g{display:grid;grid-template-columns:repeat(auto-fit,minmax(100px,1fr));width:350px}i{display:block;height:5px}");
        Is(fit, "a", 0, 0, 175, 5);
        Is(fit, "b", 175, 0, 175, 5);
        Lay fixedFill = Run(Body, "#g{display:grid;grid-template-columns:repeat(auto-fill,100px);width:350px;gap:10px}i{display:block;height:5px}");
        // 3 tracks fit: 3*100 + 2*10 = 320 <= 350, 4 would need 430.
        Is(fixedFill, "b", 110, 0, 100, 5);
    }

    // ---- track sizing (Grid 2 §12) ----

    [Fact]
    public void FlexibleTracksHonourIntrinsicMinimums()
    {
        Lay l = Run("<div id=g><i id=a></i><i id=b></i></div>", "#g{display:grid;grid-template-columns:1fr 1fr;width:400px}i{display:block;height:5px}#a{width:300px}");
        Is(l, "a", 0, 0, 300, 5);
        Is(l, "b", 300, 0, 100, 5);
    }

    [Fact]
    public void AutoTracksStretchAndMinmaxGrows()
    {
        // Grid 2 §12.8: auto tracks take the free space equally when justify-content is normal.
        Lay l = Run("<div id=g><i id=a></i><i id=b></i></div>", "#g{display:grid;grid-template-columns:auto auto;width:300px}i{display:block;height:5px}#a{width:50px}#b{width:100px}");
        Is(l, "b", 125, 0, 100, 5);
        Lay m = Run("<div id=g><i id=a></i><i id=b></i></div>", "#g{display:grid;grid-template-columns:minmax(50px,100px) 1fr;width:300px}i{display:block;height:5px}");
        Is(m, "b", 100, 0, 200, 5);
    }

    [Fact]
    public void SpanningItemGrowsFlexibleTracks()
    {
        // Grid 2 §12.5: an item spanning two flexible tracks raises them in proportion to their factors.
        Lay l = Run("<div id=g><i id=a></i><i id=b></i></div>", "#g{display:grid;grid-template-columns:1fr 2fr;float:left}i{display:block;height:5px}#a{grid-column:1 / 3;width:300px}");
        Is(l, "g", 0, 0, 300, 10);
        Is(l, "b", 0, 5, 100, 5);
    }

    [Fact]
    public void FitContentClampsTheTrack()
    {
        Lay l = Run("<div id=g><i id=a><u></u></i><i id=b></i></div>", "#g{display:grid;grid-template-columns:fit-content(80px) 1fr;width:300px}i{display:block;height:5px}#a{overflow:hidden}u{display:block;width:200px;height:1px}");
        Is(l, "b", 80, 0, 220, 5);
    }

    [Fact]
    public void PercentTracksAndGaps()
    {
        Lay l = Run("<div id=g><i id=a></i><i id=b></i></div>", "#g{display:grid;grid-template-columns:25% 1fr;column-gap:10%;width:200px}i{display:block;height:5px}");
        Is(l, "a", 0, 0, 50, 5);
        Is(l, "b", 70, 0, 130, 5);
    }

    // ---- alignment (Box Alignment 3, Grid 2 §11) ----

    [Fact]
    public void ItemAlignmentInsideTheArea()
    {
        const string Css = "#g{display:grid;grid-template-columns:100px;grid-template-rows:60px;width:100px}i{display:block;width:20px;height:10px}";
        Is(Run("<div id=g><i id=a></i></div>", Css + "#g{justify-items:center;align-items:end}"), "a", 40, 50, 20, 10);
        Is(Run("<div id=g><i id=a></i></div>", Css + "#a{justify-self:end;align-self:center}"), "a", 80, 25, 20, 10);
        Is(Run("<div id=g><i id=a></i></div>", Css + "#g{place-items:center}"), "a", 40, 25, 20, 10);
        Is(Run("<div id=g><i id=a></i></div>", Css + "#a{margin:auto}"), "a", 40, 25, 20, 10);
    }

    [Fact]
    public void ContentAlignmentDistributesFreeSpace()
    {
        const string Css = "#g{display:grid;grid-template-columns:20px 20px;grid-template-rows:10px 10px;width:100px;height:50px}i{display:block}";
        Lay between = Run("<div id=g><i id=a></i><i id=b></i><i id=c></i></div>", Css + "#g{justify-content:space-between;align-content:space-between}");
        Is(between, "b", 80, 0, 20, 10);
        Is(between, "c", 0, 40, 20, 10);
        Lay center = Run("<div id=g><i id=a></i></div>", Css + "#g{place-content:center}");
        Is(center, "a", 30, 15, 20, 10);
        Lay evenly = Run("<div id=g><i id=a></i><i id=b></i></div>", Css + "#g{justify-content:space-evenly}");
        Is(evenly, "b", 60, 0, 20, 10);
        Lay gap = Run("<div id=g><i id=a></i><i id=b></i></div>", Css + "#g{gap:4px 6px}");
        Is(gap, "b", 26, 0, 20, 10);
    }

    [Fact]
    public void StretchedItemsFillTheArea()
    {
        Lay l = Run("<div id=g><i id=a></i></div>", "#g{display:grid;grid-template-columns:100px;grid-template-rows:60px}i{display:block;margin:5px 10px}");
        Is(l, "a", 10, 5, 80, 50);
    }

    [Fact]
    public void ReplacedItemsDoNotStretchByDefault()
    {
        Lay l = Run("<div id=g><img id=a src=x.png></div>", "#g{display:grid;grid-template-columns:100px;grid-template-rows:60px}");
        Is(l, "a", 0, 0, 20, 10);
    }

    // ---- container sizing ----

    [Fact]
    public void ShrinkToFitUsesTheTrackSum()
    {
        Lay l = Run("<div id=g><i id=a></i><i id=b></i></div>", "#g{display:grid;float:left;grid-template-columns:auto auto;gap:10px}i{display:block;height:5px}#a{width:30px}#b{width:50px}");
        Is(l, "g", 0, 0, 90, 5);
        Lay m = Run("<div id=g><i id=a></i><i id=b></i></div>", "#g{display:grid;float:left;grid-template-columns:min-content 1fr;padding:3px}i{display:block;height:5px}#a{width:30px}#b{width:50px}");
        Is(m, "g", 0, 0, 86, 11);
    }

    // ---- absolutely positioned children (Grid 2 §10) ----

    [Fact]
    public void AbsoluteChildUsesTheGridAreaWhenTheGridIsTheContainingBlock()
    {
        Lay l = Run("<div id=g><i id=a></i><b id=p></b></div>",
            "#g{display:grid;position:relative;grid-template-columns:50px 60px;grid-template-rows:20px 30px;width:110px}"
            + "i{display:block}b{position:absolute;grid-column:2;grid-row:2;left:0;top:0;right:0;bottom:0}");
        Is(l, "p", 50, 20, 60, 30);
        Lay none = Run("<div id=g><b id=p></b></div>", "#g{display:grid;position:relative;grid-template-columns:50px 60px;width:110px;height:40px}b{position:absolute;left:0;top:0;right:0;bottom:0}");
        Is(none, "p", 0, 0, 110, 40);
    }

    // ---- subgrid (Grid 2 §9) ----

    [Fact]
    public void SubgridSharesParentColumns()
    {
        Lay l = Run("<div id=g><div id=s><i id=a></i><i id=b></i></div><i id=c></i></div>",
            "#g{display:grid;grid-template-columns:30px 40px 50px;width:120px;grid-auto-rows:10px}"
            + "#s{display:grid;grid-column:2 / 4;grid-template-columns:subgrid}i{display:block}");
        Is(l, "s", 30, 0, 90, 10);
        Is(l, "a", 30, 0, 40, 10);
        Is(l, "b", 70, 0, 50, 10);
    }

    [Fact]
    public void SubgridInBothAxesAndItsEdges()
    {
        Lay l = Run("<div id=g><div id=s><i id=a></i><i id=b></i><i id=c></i><i id=d></i></div></div>",
            "#g{display:grid;grid-template-columns:20px 30px;grid-template-rows:10px 15px;width:50px}"
            + "#s{display:grid;grid-template-columns:subgrid;grid-template-rows:subgrid;grid-column:1 / 3;grid-row:1 / 3;padding:2px}i{display:block}");
        Is(l, "s", 0, 0, 50, 25);
        Is(l, "a", 2, 2, 18, 8);
        Is(l, "d", 20, 10, 28, 13);
    }

    [Fact]
    public void SubgridGapsAndNestedSubgrids()
    {
        // Grid 2 §9: a subgrid's own gap replaces the parent's; the difference is split as extra margin on the items beside a gutter.
        Lay l = Run("<div id=g><div id=s><div id=t><i id=a></i><i id=b></i></div></div></div>",
            "#g{display:grid;grid-template-columns:repeat(3,20px);column-gap:10px;width:80px}"
            + "#s{display:grid;grid-column:1 / 4;grid-template-columns:subgrid}"
            + "#t{display:grid;grid-column:2 / 4;grid-template-columns:subgrid;column-gap:4px}i{display:block;height:5px}");
        Is(l, "a", 30, 0, 23, 5);
        Is(l, "b", 57, 0, 23, 5);
    }

    [Fact]
    public void SubgridLineNames()
    {
        Lay l = Run("<div id=g><div id=s><i id=a></i></div></div>",
            "#g{display:grid;grid-template-columns:[x] 10px [y] 20px [z] 30px [w];width:60px}"
            + "#s{display:grid;grid-column:2 / 4;grid-template-columns:subgrid [p] [q] [r]}i{display:block;height:5px;grid-column:q / r}");
        Is(l, "a", 30, 0, 30, 5);
    }

    // ---- baselines (Grid 2 §11.5, Box Alignment 3 §9) ----

    [Fact]
    public void BaselineAlignedItemsShareABaseline()
    {
        var fonts = OneRender.Image.FontProviders.CreateDefault();
        string html = "<!DOCTYPE html><html><head><style>body{margin:0}#g{display:grid;grid-template-columns:100px 100px;align-items:baseline;font-family:Liberation Sans}"
            + "#a{font-size:20px}#b{font-size:40px}</style></head><body><div id=g><div id=a>x</div><div id=b>x</div></div></body></html>";
        Pipeline p = Pipeline.FromHtml(html, 800, 600);
        var f = new FragmentTree(p.Boxes);
        new BlockLayoutEngine().Layout(p.Boxes, new LayoutInput(800, double.NaN, fonts), f);
        Fragment a = f.FragmentOf(p.Boxes.PrincipalBox(p.Styled.FindBySource(p.Document.GetElementById("a").Index)));
        Fragment b = f.FragmentOf(p.Boxes.PrincipalBox(p.Styled.FindBySource(p.Document.GetElementById("b").Index)));
        double baseA = f.AbsoluteBorderBox(a).Y + a.Baseline, baseB = f.AbsoluteBorderBox(b).Y + b.Baseline;
        Assert.True(Math.Abs(baseA - baseB) < 0.01, "baselines " + baseA + " and " + baseB);
        Assert.True(f.AbsoluteBorderBox(a).Y > 0.5, "the smaller item is pushed down");
    }

    // ---- limits and allocation ----

    private static string Items(int n)
    {
        var sb = new System.Text.StringBuilder("<div id=g>");
        for (int i = 0; i < n; i++)
        {
            sb.Append("<i></i>");
        }

        return sb.Append("</div>").ToString();
    }

    [Fact]
    public void RelayoutOfAnUnchangedGridAllocatesNothing()
    {
        var sb = new System.Text.StringBuilder("<div id=g>");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append(i % 7 == 0 ? "<i style='grid-column:span 2;height:7px'></i>" : i % 11 == 0 ? "<div class=s><i></i><i></i></div>" : "<i><u></u></i>");
        }

        sb.Append("</div>");
        Lay l = Run(sb.ToString(), "#g{display:grid;grid-template-columns:repeat(10,1fr) 30px;gap:2px;width:700px}i{display:block}u{display:block;height:4px}"
            + ".s{display:grid;grid-template-columns:subgrid;grid-column:span 2}");
        var input = new LayoutInput(800, 600, new NoFonts());
        for (int i = 0; i < 4; i++)
        {
            l.Engine.Layout(l.P.Boxes, input, l.F);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        l.Engine.Layout(l.P.Boxes, input, l.F);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void ATenThousandTrackRepeatIsClamped()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Lay l = Run(Items(50), "#g{display:grid;grid-template-columns:repeat(1000000,1px);grid-template-rows:repeat(auto-fill,1px);width:50000px;height:100px}i{display:block}");
        Assert.True(l.F.Count > 50);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), clock.Elapsed.ToString());
    }

    [Fact]
    public void ManyItemsStayBounded()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Lay l = Run(Items(100_000), "#g{display:grid;grid-template-columns:repeat(4,1fr);grid-auto-rows:1px;width:400px}i{display:block}");
        Assert.True(l.F.Count > 100_000);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), clock.Elapsed.ToString());
    }

    [Fact]
    public void HugeLineNumbersAreClamped()
    {
        Lay l = Run("<div id=g><i id=a></i><i id=b></i></div>",
            "#g{display:grid;grid-template-columns:10px 10px;width:20px}i{display:block;height:5px}#a{grid-column:99999999 / span 99999999;grid-row:-99999999}#b{grid-row:1 / 2000000000}");
        Assert.True(l.F.Count > 2);
    }

    [Fact]
    public void DeepSubgridNestingDoesNotRecurse()
    {
        var sb = new System.Text.StringBuilder();
        const int Depth = 300;
        for (int i = 0; i < Depth; i++)
        {
            sb.Append("<div class=s>");
        }

        sb.Append("<i id=leaf></i>");
        for (int i = 0; i < Depth; i++)
        {
            sb.Append("</div>");
        }

        Lay l = Run(sb.ToString(), ".s{display:grid;grid-template-columns:subgrid;grid-template-rows:subgrid;grid-column:1 / 3}body>.s{grid-template-columns:20px 30px;grid-template-rows:10px;display:grid}i{display:block}");
        Assert.True(l.F.Count > 1);
    }
}
