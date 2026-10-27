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
        Pipeline p = Pipeline.FromHtml(html, width, 600);
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
            "#g{display:grid;grid-template-columns:100px 1fr 2fr;grid-template-rows:50px 30px;width:720px;gap:10px}div div{}");
        Is(l, "g", 0, 0, 720, 90);
        Is(l, "a", 0, 0, 100, 50);
        Is(l, "b", 110, 0, 200, 50);
        Is(l, "c", 320, 0, 400, 50);
    }
}
