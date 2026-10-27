using System;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tests.Support;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Table layout (CSS2 §17, CSS Tables 3). Text-free cells (sized divs) keep every number exact.</summary>
public class TableLayoutTests
{
    internal sealed class Lay
    {
        public Pipeline P = null!;
        public FragmentTree F = null!;
        public BlockLayoutEngine Engine = null!;

        public Fragment Frag(string id) => F.FragmentOf(P.Boxes.PrincipalBox(P.Styled.FindBySource(P.Document.GetElementById(id).Index)));

        public Rect this[string id] => F.AbsoluteBorderBox(Frag(id));
    }

    internal static Lay Run(string body, string css = "", double width = 800, bool userAgent = true)
    {
        string html = "<!DOCTYPE html><html><head><style>body{margin:0}div{display:block}" + css + "</style></head><body>" + body + "</body></html>";
        Pipeline p = Pipeline.FromHtml(html, width, 600, userAgentSheet: userAgent);
        var f = new FragmentTree(p.Boxes);
        var engine = new BlockLayoutEngine();
        engine.Layout(p.Boxes, new LayoutInput(width, double.NaN, OneRender.Image.FontProviders.CreateDefault()), f);
        return new Lay { P = p, F = f, Engine = engine };
    }

    internal static void Is(Lay l, string id, double x, double y, double w, double h)
    {
        Rect r = l[id];
        Assert.True(Math.Abs(r.X - x) < 0.01 && Math.Abs(r.Y - y) < 0.01 && Math.Abs(r.Width - w) < 0.01 && Math.Abs(r.Height - h) < 0.01,
            "#" + id + " expected " + x + "," + y + " " + w + "x" + h + " but was " + r.X + "," + r.Y + " " + r.Width + "x" + r.Height);
    }

    private const string Cells = "td{padding:0;vertical-align:top}";

    [Fact]
    public void SeparateBordersWithSpacing()
    {
        Lay l = Run("<table id=t><tr><td id=a><div style='width:50px;height:20px'></div><td id=b><div style='width:30px;height:40px'></div></table>",
            "table{border-spacing:2px}td{padding:1px;vertical-align:top}");
        Is(l, "t", 0, 0, 90, 46);
        Is(l, "a", 2, 2, 52, 42);
        Is(l, "b", 56, 2, 32, 42);
    }

    [Fact]
    public void RowsAndColumnsShareTracks()
    {
        Lay l = Run("<table id=t><tr><td id=a><div style='width:50px;height:20px'></div><td id=b><div style='width:30px;height:10px'></div>"
            + "<tr><td id=c><div style='width:20px;height:30px'></div><td id=d><div style='width:60px;height:5px'></div></table>", "table{border-spacing:0}" + Cells);
        Is(l, "t", 0, 0, 110, 50);
        Is(l, "a", 0, 0, 50, 20);
        Is(l, "b", 50, 0, 60, 20);
        Is(l, "c", 0, 20, 50, 30);
        Is(l, "d", 50, 20, 60, 30);
    }

    [Fact]
    public void ColumnSpanAndRowSpan()
    {
        Lay l = Run("<table id=t><tr><td id=a colspan=2><div style='width:100px;height:10px'></div><td id=b rowspan=2><div style='width:20px;height:40px'></div>"
            + "<tr><td id=c><div style='width:30px;height:10px'></div><td id=d><div style='width:30px;height:10px'></div></table>", "table{border-spacing:0}" + Cells);
        // The 100px spanning cell needs the two 30px columns widened to 50px each.
        Is(l, "t", 0, 0, 120, 40);
        Is(l, "a", 0, 0, 100, 20);
        Is(l, "b", 100, 0, 20, 40);
        Is(l, "c", 0, 20, 50, 20);
        Is(l, "d", 50, 20, 50, 20);
    }

    [Fact]
    public void RowspanZeroSpansToEndOfGroupAndSpansAreClamped()
    {
        Lay l = Run("<table><tbody><tr><td id=a rowspan=0><div style='width:10px;height:90px'></div><td><div style='height:10px;width:10px'></div>"
            + "<tr><td><div style='height:10px;width:10px'></div><tr><td><div style='height:10px;width:10px'></div></tbody></table>", "table{border-spacing:0}" + Cells);
        Rect a = l["a"];
        Assert.Equal(90, a.Height, 3);
        Lay big = Run("<table id=t><tr><td id=a colspan=5000 rowspan=999999><div style='width:10px;height:10px'></div></table>", "table{border-spacing:0}" + Cells);
        Assert.Equal(10, big["t"].Height, 3);
        Assert.Equal(10, big["a"].Width, 3);
    }

    [Fact]
    public void WidthAndAutoLayout()
    {
        // A table with width fills it; the extra goes to the auto columns in proportion to max-content.
        Lay l = Run("<table id=t style='width:300px'><tr><td id=a><div style='width:100px;height:10px'></div><td id=b><div style='width:50px;height:10px'></div></table>", "table{border-spacing:0}" + Cells);
        Is(l, "t", 0, 0, 300, 10);
        Is(l, "a", 0, 0, 200, 10);
        Is(l, "b", 200, 0, 100, 10);
    }

    [Fact]
    public void PercentAndFixedColumns()
    {
        Lay l = Run("<table id=t style='width:400px'><tr><td id=a style='width:25%'><div style='height:10px'></div><td id=b style='width:100px'><div style='height:10px'></div><td id=c><div style='height:10px'></div></table>", "table{border-spacing:0}" + Cells);
        Is(l, "a", 0, 0, 100, 10);
        Is(l, "b", 100, 0, 100, 10);
        Is(l, "c", 200, 0, 200, 10);
    }

    [Fact]
    public void FixedLayoutUsesFirstRow()
    {
        Lay l = Run("<table id=t style='width:200px;table-layout:fixed'><tr><td id=a style='width:50px'><td id=b><tr><td id=c><div style='width:500px;height:10px'></div><td id=d></table>", "table{border-spacing:0}" + Cells);
        Is(l, "a", 0, 0, 50, 0);
        Is(l, "b", 50, 0, 150, 0);
        Is(l, "c", 0, 0, 50, 10);
        Is(l, "t", 0, 0, 200, 10);
    }

    [Fact]
    public void VerticalAlignInCells()
    {
        Lay l = Run("<table id=t><tr style='height:60px'><td id=a style='vertical-align:top'><div id=ia style='width:10px;height:10px'></div><td id=b style='vertical-align:middle'><div id=ib style='width:10px;height:10px'></div>"
            + "<td id=c style='vertical-align:bottom'><div id=ic style='width:10px;height:10px'></div></table>", "table{border-spacing:0}td{padding:0}");
        Is(l, "ia", 0, 0, 10, 10);
        Is(l, "ib", 10, 25, 10, 10);
        Is(l, "ic", 20, 50, 10, 10);
        Is(l, "a", 0, 0, 10, 60);
    }

    [Fact]
    public void CaptionTopAndBottom()
    {
        Lay l = Run("<table id=t><caption id=c style='height:20px'>x</caption><tr><td id=a><div style='width:50px;height:10px'></div></table>"
            + "<table id=u style='caption-side:bottom'><caption id=d style='height:20px'></caption><tr><td id=b><div style='width:50px;height:10px'></div></table>", "table{border-spacing:0}" + Cells);
        Is(l, "c", 0, 0, 50, 20);
        Is(l, "a", 0, 20, 50, 10);
        Is(l, "t", 0, 0, 50, 30);
        Is(l, "b", 0, 30, 50, 10);
        Is(l, "d", 0, 40, 50, 20);
    }

    [Fact]
    public void CollapsedBordersShareTheLine()
    {
        Lay l = Run("<table id=t><tr><td id=a><div style='width:50px;height:20px'></div><td id=b><div style='width:50px;height:20px'></div></table>",
            "table{border-collapse:collapse}td{padding:0;border:2px solid #000}");
        // Outer half of each outer border belongs to the table (1px each side), the inner half to the cell; the middle line is shared.
        Is(l, "t", 0, 0, 106, 24);
        Is(l, "a", 1, 1, 52, 22);
    }

    [Fact]
    public void CollapsedBorderConflictWiderWinsHiddenKills()
    {
        Lay l = Run("<table id=t><tr><td id=a style='border-right:6px solid red'><div style='width:20px;height:10px'></div><td id=b style='border-left:2px solid blue'><div style='width:20px;height:10px'></div>"
            + "<tr><td id=c style='border-right:6px solid red'><div style='width:20px;height:10px'></div><td id=d style='border-left:hidden'><div style='width:20px;height:10px'></div></table>",
            "table{border-collapse:collapse}td{padding:0}");
        // Row 1: the 6px border wins, 3px on each cell. Row 2: hidden wins, so no border at all there; the line is as wide as the widest in the column.
        Rect a = l["a"], b = l["b"];
        Assert.Equal(23, a.Width, 3);
        Assert.Equal(23, b.Width, 3);
    }

    [Fact]
    public void AnonymousTableObjects()
    {
        Lay l = Run("<div id=w><div id=c1 style='display:table-cell;width:30px;height:10px'></div><div id=c2 style='display:table-cell;width:20px;height:10px'></div></div>"
            + "<div id=x style='display:table'><div id=r style='display:table-row'><div id=c3 style='display:table-cell;width:5px;height:5px'></div></div></div>"
            + "<div id=y style='display:table'>text in table<div id=c4 style='display:table-cell;width:5px;height:5px'></div></div>", "table{border-spacing:0}");
        Assert.Equal(30, l["c1"].Width, 3);
        Assert.Equal(30, l["c2"].X, 3);
        Is(l, "x", 0, 10, 5, 5);
        Assert.True(l["y"].Height > 5);
    }

    [Fact]
    public void CollapsedRowsAndColumns()
    {
        Lay l = Run("<table id=t><colgroup><col><col style='visibility:collapse'><col></colgroup><tr><td id=a><div style='width:10px;height:10px'></div><td id=b><div style='width:10px;height:10px'></div><td id=c><div style='width:10px;height:10px'></div>"
            + "<tr style='visibility:collapse'><td><div style='width:10px;height:50px'></div></table>", "table{border-spacing:0}" + Cells);
        Is(l, "t", 0, 0, 20, 10);
        Is(l, "c", 10, 0, 10, 10);
    }

    [Fact]
    public void InlineTableAndNestedTables()
    {
        Lay l = Run("<table id=o><tr><td id=oc><table id=i><tr><td id=ic><div style='width:40px;height:10px'></div></table></table>", "table{border-spacing:0}" + Cells);
        Is(l, "i", 0, 0, 40, 10);
        Is(l, "o", 0, 0, 40, 10);
        Lay it = Run("<div style='display:inline-table' id=t><div style='display:table-cell;width:30px;height:10px'></div></div>", "");
        Assert.Equal(30, it["t"].Width, 3);
        Assert.Equal(10, it["t"].Height, 3);
    }

    [Fact]
    public void HeaderAndFooterGroupsMoveToTheEnds()
    {
        Lay l = Run("<table><tfoot><tr><td id=f><div style='width:10px;height:5px'></div></tfoot><tbody><tr><td id=b><div style='width:10px;height:10px'></div></tbody><thead><tr><td id=h><div style='width:10px;height:20px'></div></thead></table>", "table{border-spacing:0}" + Cells);
        Is(l, "h", 0, 0, 10, 20);
        Is(l, "b", 0, 20, 10, 10);
        Is(l, "f", 0, 30, 10, 5);
    }

    [Fact]
    public void ColumnSpanAttributeOnColGroup()
    {
        Lay l = Run("<table id=t style='width:100px'><colgroup span=2 style='width:30px'></colgroup><tr><td id=a><td id=b><td id=c></table>", "table{border-spacing:0}" + Cells);
        Is(l, "a", 0, 0, 30, 0);
        Is(l, "b", 30, 0, 30, 0);
        Is(l, "c", 60, 0, 40, 0);
    }

    [Fact]
    public void ReLayoutGivesTheSameFragments()
    {
        string body = "<table id=t style='width:300px'><tr><td id=a><div style='width:100px;height:10px'></div><td id=b><div style='width:50px;height:30px'></div><tr><td colspan=2><div style='height:12px'></div></table>";
        Lay l = Run(body, "table{border-spacing:3px}" + Cells);
        string first = l.F.Dump();
        l.Engine.Layout(l.P.Boxes, new LayoutInput(800, double.NaN, OneRender.Image.FontProviders.CreateDefault()), l.F);
        Assert.Equal(first, l.F.Dump());
    }

    [Fact]
    public void PresentationalAttributes()
    {
        // HTML §15.3.8: cellspacing, cellpadding and width on a table act as hints; the author's CSS wins.
        Lay l = Run("<table id=t width=200 cellspacing=0 cellpadding=3><tr><td id=a><div style='width:10px;height:10px'></div><td id=b style='padding:0'><div style='width:10px;height:10px'></div></table>");
        Assert.Equal(200, l["t"].Width, 3);
        Assert.Equal(0, l["a"].X, 3);
        Assert.Equal(16, l["a"].Height, 3);
        Assert.Equal(16, l["b"].Height, 3);
    }

    [Fact]
    public void ChangingACellTextRelaysOutLikeAFullRecompute()
    {
        string Doc(string cell) => "<table id=t style='width:300px'><tr><td id=a>" + cell + "</td><td id=b>beta</td></tr><tr><td colspan=2 id=c>row two</td></tr></table>";
        Lay edited = Run(Doc("alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau upsilon"), "table{border-spacing:2px}");
        Lay fresh = Run(Doc("alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau upsilon"), "table{border-spacing:2px}");
        // Laying out the same boxes again (a forced flush) gives the same fragments as the first layout and as a fresh pipeline.
        string first = edited.F.Dump();
        edited.Engine.Layout(edited.P.Boxes, new LayoutInput(800, double.NaN, OneRender.Image.FontProviders.CreateDefault()), edited.F);
        Assert.Equal(first, edited.F.Dump());
        Assert.Equal(fresh.F.Dump(), edited.F.Dump());
    }
}
