using System.Linq;
using OneRender.Image;
using OneRender.Paged;
using OneRender.Painting;

namespace OneRender.Tests;

public class PagedMediaTests
{
    private static readonly PageGeometry Small = new(400, 300, 20, 20, 20, 20);

    private static PagedDocument Doc(string html, PageGeometry? page = null) =>
        PagedDocument.Create("<!DOCTYPE html><html><head><style>html,body{margin:0}" + html, new PagedOptions(FontProviders.CreateDefault()) { DefaultPage = page ?? Small });

    private static string Block(int count, int height = 100, string cls = "b") =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"<div class='{cls}' id='b{i}' style='height:{height}px'>block {i}</div>"));

    [Fact]
    public void PageSizeAndMarginsComeFromTheRule()
    {
        PagedDocument d = Doc("@page{size:200px 150px;margin:10px 20px 30px 40px}</style></head><body>" + Block(3, 50));
        PageGeometry g = d.GetPage(0).Geometry;
        Assert.Equal(200, g.Width);
        Assert.Equal(150, g.Height);
        Assert.Equal((10, 20, 30, 40), (g.MarginTop, g.MarginRight, g.MarginBottom, g.MarginLeft));
        // 110 px of page area: two 50 px blocks fit, the third goes on a second page.
        Assert.Equal(2, d.PageCount);
    }

    [Theory]
    [InlineData("A4", 793.7007874, 1122.5196850)]
    [InlineData("letter landscape", 1056, 816)]
    [InlineData("landscape", 400, 300)]
    [InlineData("10cm", 377.9527559, 377.9527559)]
    [InlineData("A5 portrait", 559.3700787, 793.7007874)]
    public void SizeKeywordsAndOrientation(string size, double w, double h)
    {
        PagedDocument d = Doc("@page{size:" + size + ";margin:0}</style></head><body>" + Block(1, 10));
        Assert.Equal(w, d.GetPage(0).Geometry.Width, 3);
        Assert.Equal(h, d.GetPage(0).Geometry.Height, 3);
    }

    [Fact]
    public void LandscapeTurnsTheDefaultSize()
    {
        PagedDocument d = Doc("@page{size:landscape;margin:0}</style></head><body>" + Block(1, 10), new PageGeometry(300, 400, 0, 0, 0, 0));
        Assert.Equal(400, d.GetPage(0).Geometry.Width);
        Assert.Equal(300, d.GetPage(0).Geometry.Height);
    }

    [Fact]
    public void PageSelectorsCascadeBySpecificity()
    {
        // :first beats the plain rule, :left beats :right on a left page, a name beats a pseudo-class.
        PagedDocument d = Doc("@page{margin:10px}@page:first{margin-top:50px}@page:left{margin-left:70px}@page:right{margin-right:60px}@page wide{size:600px 300px;margin:5px}div.s{page:wide}</style></head><body>"
            + Block(2, 240) + Block(2, 240));
        Assert.Equal(50, d.GetPage(0).Geometry.MarginTop);
        Assert.Equal(60, d.GetPage(0).Geometry.MarginRight);
        Assert.Equal(10, d.GetPage(0).Geometry.MarginLeft);
        Assert.Equal(70, d.GetPage(1).Geometry.MarginLeft);
        Assert.Equal(10, d.GetPage(1).Geometry.MarginTop);
    }

    [Fact]
    public void ANamedPageForcesBreaksAndTakesItsOwnSize()
    {
        PagedDocument d = Doc("@page{size:300px 200px;margin:0}@page wide{size:500px 200px;margin:0}.w{page:wide;height:20px}</style></head><body><div style='height:20px'>a</div><div class='w'>b</div><div class='w'>c</div><div style='height:20px'>d</div>");
        Assert.Equal(3, d.PageCount);
        Assert.Null(d.GetPage(0).Name);
        Assert.Equal("wide", d.GetPage(1).Name);
        Assert.Equal(500, d.GetPage(1).Geometry.Width);
        Assert.Null(d.GetPage(2).Name);
    }

    [Fact]
    public void BreakToLeftInsertsABlankPage()
    {
        // Page 1 is a right page, so a forced break to a left page keeps page 2 and a break to a right page leaves one blank.
        PagedDocument left = Doc("@page{margin:0}</style></head><body><div style='height:20px'>a</div><div style='break-before:left;height:20px'>b</div>");
        Assert.Equal(2, left.PageCount);
        Assert.False(left.GetPage(1).IsBlank);
        Assert.False(left.GetPage(1).IsRight);

        PagedDocument right = Doc("@page{margin:0}</style></head><body><div style='height:20px'>a</div><div style='break-before:right;height:20px'>b</div>");
        Assert.Equal(3, right.PageCount);
        Assert.True(right.GetPage(1).IsBlank);
        Assert.False(right.GetPage(2).IsBlank);
        Assert.True(right.GetPage(2).IsRight);

        PagedDocument verso = Doc("@page{margin:0}</style></head><body><div style='height:20px'>a</div><div style='break-before:verso;height:20px'>b</div>");
        Assert.Equal(2, verso.PageCount);
        PagedDocument recto = Doc("@page{margin:0}</style></head><body><div style='height:20px'>a</div><div style='break-before:recto;height:20px'>b</div>");
        Assert.Equal(3, recto.PageCount);
    }

    [Fact]
    public void RightToLeftDocumentsStartOnALeftPage()
    {
        PagedDocument d = Doc("@page{margin:0}html{direction:rtl}</style></head><body><div style='height:20px'>a</div><div style='break-before:verso;height:20px'>b</div>");
        Assert.False(d.GetPage(0).IsRight);
        Assert.Equal(2, d.PageCount);
        Assert.True(d.GetPage(1).IsRight);

        // recto is the left page of a right-to-left document, so the second page cannot be it without a blank one.
        PagedDocument r = Doc("@page{margin:0}html{direction:rtl}</style></head><body><div style='height:20px'>a</div><div style='break-before:recto;height:20px'>b</div>");
        Assert.Equal(3, r.PageCount);
    }

    [Fact]
    public void MarginBoxesAreDrawnOnEveryPage()
    {
        PagedDocument d = Doc("@page{size:400px 300px;margin:40px;@top-center{content:'Header'}@bottom-right{content:'Page ' counter(page) ' of ' counter(pages)}}</style></head><body>" + Block(4, 100));
        Assert.Equal(2, d.PageCount);
        DisplayList l0 = d.GetPageLayer(0)!;
        DisplayList l1 = d.GetPageLayer(1)!;
        Assert.NotNull(l0);
        Assert.NotSame(l0, l1);
        string t0 = RunText(l0), t1 = RunText(l1);
        Assert.Contains("Header", t0);
        Assert.Contains("Page 1 of 2", t0);
        Assert.Contains("Page 2 of 2", t1);
    }

    [Fact]
    public void MarginBoxGeometryFollowsTheSpecAlgorithm()
    {
        // top-left, top-center and top-right share the page width; the centre box stays centred.
        var gen = new[] { true, true, true };
        var w = new double[3];
        PagedDocument.Distribute(300, gen, [50, 100, 30], [null, null, null], w);
        Assert.Equal(300, w[0] + w[1] + w[2], 6);
        Assert.Equal(w[0], w[2], 6);
        Assert.True(w[1] > 100);

        // Not enough room: they shrink in proportion.
        PagedDocument.Distribute(100, gen, [100, 200, 100], [null, null, null], w);
        Assert.Equal(100, w[0] + w[1] + w[2], 6);
        Assert.Equal(w[0], w[2], 6);
        Assert.Equal(50, w[1], 6);

        // A lone centre box takes the whole side; a lone start box too.
        PagedDocument.Distribute(300, [false, true, false], [0, 40, 0], [null, null, null], w);
        Assert.Equal(300, w[1], 6);
        PagedDocument.Distribute(300, [true, false, false], [40, 0, 0], [null, null, null], w);
        Assert.Equal(300, w[0], 6);

        // Start and end share in proportion to their content.
        PagedDocument.Distribute(300, [true, false, true], [60, 0, 120], [null, null, null], w);
        Assert.Equal(100, w[0], 6);
        Assert.Equal(200, w[2], 6);

        // An authored extent is kept; the other box takes the rest.
        PagedDocument.Distribute(300, [true, false, true], [60, 0, 120], [80, null, null], w);
        Assert.Equal(80, w[0], 6);
        Assert.Equal(220, w[2], 6);
    }

    [Fact]
    public void MarginBoxesLandInTheirMargins()
    {
        PagedDocument d = Doc("@page{size:400px 300px;margin:50px;@top-left-corner{content:'TLC'}@bottom-center{content:'BC'}@left-middle{content:'LM'}}</style></head><body>" + Block(1, 10));
        DisplayList l = d.GetPageLayer(0)!;
        var runs = Runs(l);
        // Corner: inside the top-left 50x50 square; bottom centre: in the bottom margin; left middle: in the left margin.
        (RectF b, string t) tlc = runs.Single(r => r.Text == "TLC");
        Assert.True(tlc.b.X >= 0 && tlc.b.Right <= 50.5f && tlc.b.Y >= 0 && tlc.b.Bottom <= 50.5f, tlc.b.ToString());
        (RectF b, string t) bc = runs.Single(r => r.Text == "BC");
        Assert.True(bc.b.Y >= 250 && bc.b.Bottom <= 300.5f, bc.b.ToString());
        Assert.InRange(bc.b.X + (bc.b.Width / 2), 195, 205);
        (RectF b, string t) lm = runs.Single(r => r.Text == "LM");
        Assert.True(lm.b.X >= 0 && lm.b.Right <= 50.5f, lm.b.ToString());
        Assert.InRange(lm.b.Y + (lm.b.Height / 2), 145, 155);
    }

    [Fact]
    public void StringSetFeedsRunningHeaders()
    {
        string css = "@page{size:400px 200px;margin:40px;@top-left{content:string(chapter)}@top-right{content:string(chapter,last)}}h1{string-set:chapter content(text);height:60px;margin:0;font-size:16px}";
        PagedDocument d = Doc(css + "</style></head><body><h1>One</h1><h1>Two</h1><h1>Three</h1><h1>Four</h1>");
        Assert.Equal(2, d.PageCount);
        // 120 px of page area, 60 px per heading: One and Two on page 1, Three and Four on page 2.
        string p1 = RunText(d.GetPageLayer(0)!), p2 = RunText(d.GetPageLayer(1)!);
        Assert.Contains("One", p1);
        Assert.Contains("Two", p1);
        Assert.Contains("Three", p2);
        Assert.Contains("Four", p2);
    }

    [Fact]
    public void CountersNumberHeadings()
    {
        PagedDocument d = Doc("body{counter-reset:ch}h2{counter-increment:ch;margin:0;height:20px}h2::before{content:'Chapter ' counter(ch) '. '}</style></head><body><h2>A</h2><h2>B</h2><h2>C</h2>");
        string text = ContentText(d);
        Assert.Contains("Chapter 1. ", text);
        Assert.Contains("Chapter 2. ", text);
        Assert.Contains("Chapter 3. ", text);
    }

    [Fact]
    public void NestedCountersJoin()
    {
        PagedDocument d = Doc("ol{counter-reset:item;margin:0;padding:0;list-style:none}li{counter-increment:item}li::before{content:counters(item,'.') ' '}</style></head><body><ol><li>a<ol><li>b</li><li>c</li></ol></li><li>d</li></ol>");
        string text = ContentText(d);
        Assert.Contains("1 ", text);
        Assert.Contains("1.1 ", text);
        Assert.Contains("1.2 ", text);
        Assert.Contains("2 ", text);
    }

    [Fact]
    public void TargetCounterFindsThePageOfAnElement()
    {
        string css = "a::after{content:' p.' target-counter(attr(href url),page)}div.s{height:100px;break-inside:avoid}";
        PagedDocument d = Doc("@page{size:400px 300px;margin:20px}" + css + "</style></head><body><p><a href='#x1'>one</a> <a href='#x2'>two</a></p>" + "<div class='s'></div><div class='s'></div><div class='s' id='x1'>target</div><div class='s'></div><div class='s' id='x2'>target</div>");
        string text = ContentText(d);
        Assert.Equal(3, d.PageCount);
        Assert.Contains("p.2", text);
        Assert.Contains("p.3", text);
        // Both links are destinations.
        Assert.Equal(2, d.Destinations.Count);
        Assert.Equal(1, d.Destinations.Single(x => x.Name == "x1").PageIndex);
    }

    [Fact]
    public void TargetCounterOfACounterAndTargetText()
    {
        string css = "body{counter-reset:s}h2{counter-increment:s;margin:0;height:20px}a.t::after{content:' (section ' target-counter(attr(href url),s) ': ' target-text(attr(href url)) ')'}";
        PagedDocument d = Doc(css + "</style></head><body><p><a class='t' href='#late'>go</a></p><h2>First</h2><h2 id='late'>Late  heading</h2>");
        string text = ContentText(d);
        Assert.Contains("(section 2: Late heading)", text);
    }

    [Fact]
    public void BoxDecorationBreakCloneRepeatsBordersOnEveryPage()
    {
        PagedDocument d = Doc("@page{size:200px 100px;margin:0}.c{box-decoration-break:clone;border:5px solid #000;padding:5px;background:#eee}.c div{height:50px}</style></head><body><div class='c'><div>a</div><div>b</div><div>c</div></div>");
        Assert.True(d.PageCount >= 2);
        var list = new DisplayList();
        d.PaintContent(list);
        int borders = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kind == ItemKind.Border)
            {
                borders++;
            }
        }

        Assert.True(borders >= d.PageCount, "one border per page expected, found " + borders);
    }

    [Fact]
    public void PageLimitStopsTheLayoutAndSaysSo()
    {
        PagedDocument d = PagedDocument.Create("<html><body>" + Block(50, 100) + "</body></html>", new PagedOptions(FontProviders.CreateDefault()) { DefaultPage = Small, MaxPages = 3 });
        Assert.Equal(3, d.PageCount);
        Assert.True(d.Truncated);
        Assert.Contains(d.Diagnostics, x => x.Kind == DiagnosticKind.LimitExceeded);
    }

    [Fact]
    public void HostileMarginBoxContentIsBounded()
    {
        string many = string.Concat(Enumerable.Repeat("counter(page) ", 5000));
        PagedDocument d = Doc("@page{margin:40px;@top-center{content:" + many[..20000] + "}@bottom-center{content:string(" + new string((char)97, 20000) + ")}}</style></head><body>" + Block(1, 10));
        Assert.NotNull(d.GetPageLayer(0));

        // A rule longer than the limit is dropped and said so.
        PagedDocument big = Doc("@page{margin:40px;@top-center{content:" + many + "}}</style></head><body>" + Block(1, 10));
        Assert.Null(big.GetPageLayer(0));
        Assert.Contains(big.Diagnostics, x => x.Kind == DiagnosticKind.LimitExceeded);
    }

    private static string ContentText(PagedDocument d)
    {
        var list = new DisplayList();
        d.PaintContent(list);
        return RunText(list);
    }

    private static string RunText(DisplayList l) => string.Join(" ", Runs(l).Select(r => r.Text));

    private static List<(RectF Bounds, string Text)> Runs(DisplayList l)
    {
        var runs = new List<(RectF, string)>();
        for (int i = 0; i < l.Count; i++)
        {
            DisplayItem it = l[i];
            if (it.Kind == ItemKind.GlyphRun)
            {
                runs.Add((it.RootBounds, it.GlyphRun.Text.ToString()));
            }
        }

        return runs;
    }
}
