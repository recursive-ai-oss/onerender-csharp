using System.Diagnostics;
using System.Linq;
using System.Threading;
using OneRender.Accessibility;
using OneRender.Image;
using OneRender.Interaction;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using Xunit;

namespace OneRender.Tests;

/// <summary>Rendered facts from the engine's own layout and paint, one family of facts at a time.</summary>
public class AccessibilityFactsTests
{
    private const string Head = "<!DOCTYPE html><html><head><style>html,body{margin:0}body{font:16px/20px Arial}div,p,span{box-sizing:border-box}</style></head><body>";

    private static RenderedPage Render(string body, int w = 400, int h = 300, FactsOptions? options = null) =>
        AccessibilityFacts.Render(Head + body + "</body></html>", new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = w, ViewportHeight = h }, options);

    private static NodeFacts Of(RenderedPage r, string id) => r.Facts[r.Document.GetElementById(id)];

    // ------------------------------------------------------------------ geometry

    [Fact]
    public void BorderBoxesAreInDocumentCoordinates()
    {
        RenderedPage r = Render("<div style='height:50px'></div><div id=a style='margin:10px 0 0 20px;width:100px;height:40px;border:2px solid #000'></div>");
        BoxFacts box = Of(r, "a").Layout;
        Assert.Equal(new LayoutRect(20, 60, 100, 40), box.BorderBox);
        Assert.Equal(new LayoutRect(0, 0, 400, 300), r.Facts[r.Document.Root].Layout.BorderBox);
    }

    [Fact]
    public void ScrolledPagesKeepDocumentCoordinates()
    {
        RenderedPage r = Render("<div style='height:900px'></div><div id=a style='height:10px'></div>");
        Assert.Equal(900, Of(r, "a").Layout.BorderBox.Y);
        Assert.Equal(910, Of(r, "a").Layout.BorderBox.Y + 10);
    }

    [Fact]
    public void OffsetClientAndScrollRectsFollowCssomView()
    {
        RenderedPage r = Render("<div id=outer style='position:relative;margin:7px;width:100px;height:60px;border:3px solid #000;overflow:auto'><div id=inner style='position:absolute;left:5px;top:6px;width:200px;height:150px'></div></div>");
        BoxFacts outer = Of(r, "outer").Layout;
        Assert.Equal(new LayoutRect(7, 7, 100, 60), outer.OffsetRect);
        Assert.Equal(new LayoutRect(3, 3, 94, 54), outer.ClientRect);
        Assert.Equal(new LayoutRect(0, 0, 205, 156), outer.ScrollRect);
        Assert.Equal(new LayoutRect(5, 6, 200, 150), Of(r, "inner").Layout.OffsetRect);
    }

    // ------------------------------------------------------------------ visibility

    [Fact]
    public void NoBoxForDisplayNoneOrContentsButOneForHiddenAndTransparent()
    {
        RenderedPage r = Render(
            "<div id=none style='display:none'><span id=kid>x</span></div>" +
            "<div id=contents style='display:contents'><span id=inside>y</span></div>" +
            "<div id=hidden style='visibility:hidden;height:10px'>z</div>" +
            "<div id=clear style='opacity:0;height:10px'>w</div>" +
            "<div id=zero style='width:0;height:0'></div>");
        Assert.False(Of(r, "none").HasLayout);
        Assert.False(Of(r, "kid").HasLayout);
        Assert.False(Of(r, "contents").HasLayout);
        Assert.True(Of(r, "inside").HasLayout);
        Assert.True(Of(r, "hidden").HasLayout);
        Assert.True(Of(r, "clear").HasLayout);
        Assert.True(Of(r, "zero").HasLayout);
        Assert.Equal(0, Of(r, "zero").Layout.BorderBox.Width);
    }

    [Fact]
    public void TextOfHiddenBoxesHasNoPaintedBackgroundFact()
    {
        RenderedPage r = Render("<div id=hidden style='visibility:hidden'>secret</div><div id=shown>open</div>");
        Assert.Null(Of(r, "hidden").Layout.BlendedBackgroundColor);
        Assert.Equal("rgb(255, 255, 255)", Of(r, "shown").Layout.BlendedBackgroundColor);
    }

    // ------------------------------------------------------------------ paint order and stacking contexts

    [Fact]
    public void PaintOrderFollowsAppendixEAndSharesValuesInsideALayer()
    {
        RenderedPage r = Render(
            "<div id=stage style='position:relative;height:200px'>" +
            "<div id=a style='position:absolute;z-index:1;width:20px;height:20px'></div>" +
            "<div id=b style='position:absolute;z-index:2;width:20px;height:20px'></div>" +
            "<div id=c style='position:absolute;width:20px;height:20px'></div>" +
            "<div id=n style='position:absolute;z-index:-1;width:20px;height:20px'></div>" +
            "</div><div id=plain style='height:10px'></div><p id=text>hello</p>");
        int doc = Of(r, "a").Layout.PaintOrder!.Value;
        int html = r.Facts[r.Document.DocumentElement].Layout.PaintOrder!.Value;
        int Po(string id) => Of(r, id).Layout.PaintOrder!.Value;
        Assert.Equal(1, html);
        Assert.Equal(html, Po("plain"));
        Assert.Equal(html, Po("text"));
        // negative z first, then the in-flow stage and the auto layer in tree order, then z 1, then z 2
        Assert.True(Po("n") < Po("stage"));
        Assert.True(Po("stage") < Po("c"));
        Assert.True(Po("c") < Po("a"));
        Assert.True(Po("a") < Po("b"));
        Assert.Equal(doc, Po("a"));
        Assert.Equal(0, r.Facts[r.Document.Root].Layout.PaintOrder);
        // a text node is painted with its parent
        Assert.Equal(Po("text"), r.Facts[r.Document.GetElementById("text").FirstChild].Layout.PaintOrder);
    }

    [Fact]
    public void StackingContextsAreTheBoxesThatBeginOne()
    {
        RenderedPage r = Render(
            "<div id=auto style='position:relative'></div><div id=z style='position:relative;z-index:0'></div>" +
            "<div id=op style='opacity:.5'></div><div id=fixed style='position:fixed'></div><div id=plain></div>");
        Assert.True(r.Facts[r.Document.Root].IsStackingContext);
        Assert.True(r.Facts[r.Document.DocumentElement].IsStackingContext);
        Assert.False(Of(r, "auto").IsStackingContext);
        Assert.True(Of(r, "z").IsStackingContext);
        Assert.True(Of(r, "op").IsStackingContext);
        Assert.True(Of(r, "fixed").IsStackingContext);
        Assert.False(Of(r, "plain").IsStackingContext);
        Node z = r.Document.GetElementById("z");
        Assert.Equal(z.Index, Of(r, "z").StackingContextNodeIndex);
        Assert.Equal(r.Document.DocumentElement.Index, Of(r, "plain").StackingContextNodeIndex);
    }

    // ------------------------------------------------------------------ text

    [Fact]
    public void TextBoxesGiveUtf16RangesIntoTheLayoutText()
    {
        RenderedPage r = Render("<p id=p style='width:90px;margin:0'>one two three four five</p>");
        Node text = r.Document.GetElementById("p").FirstChild;
        BoxFacts box = r.Facts[text].Layout;
        Assert.Equal("one two three four five", box.LayoutText);
        Assert.True(box.TextBoxes.Length >= 3);
        int end = 0;
        foreach (TextBox tb in box.TextBoxes)
        {
            Assert.True(tb.Start >= end);
            Assert.True(tb.Length > 0 && tb.Start + tb.Length <= box.LayoutText!.Length);
            Assert.True(tb.Bounds.Width > 0);
            end = tb.Start + tb.Length;
        }

        Assert.StartsWith("one", box.LayoutText![box.TextBoxes[0].Start..]);
        // the text boxes sit inside the paragraph
        LayoutRect p = Of(r, "p").Layout.BorderBox;
        Assert.All(box.TextBoxes.ToArray(), tb => Assert.True(tb.Bounds.X >= p.X && tb.Bounds.Right <= p.Right + 0.01));
    }

    [Fact]
    public void SurrogatePairsAreCountedInUtf16Units()
    {
        RenderedPage r = Render("<p id=p style='margin:0'>a\U0001F600b</p>");
        BoxFacts box = r.Facts[r.Document.GetElementById("p").FirstChild].Layout;
        Assert.Equal("a\U0001F600b", box.LayoutText);
        Assert.Equal(4, box.TextBoxes.ToArray().Sum(tb => tb.Length));
    }

    // ------------------------------------------------------------------ colors behind text

    [Fact]
    public void BlendedBackgroundCompositesLayersOverWhite()
    {
        RenderedPage r = Render(
            "<div id=solid style='background:#003366;height:30px'>a</div>" +
            "<div id=half style='background:rgba(0,0,0,.5);height:30px'>b</div>" +
            "<div id=none style='height:30px'>c</div>" +
            "<div style='position:relative;height:60px;background:#003366'><div id=over style='position:absolute;left:0;top:0;width:100px;height:30px;background:rgba(255,255,255,.8)'>d</div></div>" +
            "<div id=nested style='background:#ff0000'><div style='background:rgba(0,0,255,.5);height:30px'><span id=deep>e</span></div></div>");
        Assert.Equal("rgb(0, 51, 102)", Of(r, "solid").Layout.BlendedBackgroundColor);
        Assert.Equal("rgb(127, 127, 127)", Of(r, "half").Layout.BlendedBackgroundColor);
        Assert.Equal("rgb(255, 255, 255)", Of(r, "none").Layout.BlendedBackgroundColor);
        Assert.Equal("rgb(204, 214, 224)", Of(r, "over").Layout.BlendedBackgroundColor);
        // red under 50 % blue: 255*128/255 -> 127 red... (0,0,255 at alpha 128 over 255,0,0)
        Assert.Equal("rgb(127, 0, 128)", Of(r, "deep").Layout.BlendedBackgroundColor);
    }

    [Fact]
    public void FiltersAndBlendModesMakeTheColorBehindTextUnknownOnlyWhenTheyAreFlattenedUnderIt()
    {
        RenderedPage r = Render(
            "<div style='position:relative;height:40px'><div style='position:absolute;left:0;top:0;width:200px;height:40px;background:#336699;filter:blur(2px)'></div>" +
            "<div id=over style='position:absolute;left:0;top:0'>x</div></div>" +
            "<div style='position:relative;height:40px'><div style='position:absolute;left:0;top:0;width:200px;height:40px;background:#336699;mix-blend-mode:multiply'></div>" +
            "<div id=over2 style='position:absolute;left:0;top:0'>y</div></div>" +
            "<div id=inside style='background:#336699;filter:blur(1px);height:30px'>z</div>");
        Assert.Null(Of(r, "over").Layout.BlendedBackgroundColor);
        Assert.Null(Of(r, "over2").Layout.BlendedBackgroundColor);
        Assert.Equal("rgb(51, 102, 153)", Of(r, "inside").Layout.BlendedBackgroundColor);
    }

    [Fact]
    public void ElementsWithoutTextOfTheirOwnHaveNoBlendedColor()
    {
        RenderedPage r = Render("<div id=box style='background:#336699;height:30px'><span id=s>text</span></div>");
        Assert.Null(Of(r, "box").Layout.BlendedBackgroundColor);
        Assert.Equal("rgb(51, 102, 153)", Of(r, "s").Layout.BlendedBackgroundColor);
    }

    private sealed class OneImage : OneRender.Painting.IImageSource
    {
        public bool TryGetImage(string address, out OneRender.Painting.ImageInfo image)
        {
            image = new OneRender.Painting.ImageInfo(new OneRender.Painting.ImageHandle(1), 8, 8);
            return true;
        }
    }

    [Fact]
    public void ImagesBehindTextMakeTheColorUnknownAndGradientsCountAsTheirFirstStop()
    {
        var options = new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 300, ViewportHeight = 100, Images = new OneImage() };
        RenderedPage r = AccessibilityFacts.Render(
            Head + "<div id=img style='height:30px;background:url(a.png) #123456'>t</div><div id=grad style='height:30px;background:linear-gradient(90deg,#102030,#fff)'>g</div></body></html>", options);
        Assert.Null(Of(r, "img").Layout.BlendedBackgroundColor);
        Assert.Equal("rgb(16, 32, 48)", Of(r, "grad").Layout.BlendedBackgroundColor);
    }

    [Fact]
    public void TextColorOpacityIsTheOpacityTheTextIsPaintedWith()
    {
        RenderedPage r = Render(
            "<div id=outer style='opacity:.5;background:#000;height:40px'><p id=inner style='margin:0;opacity:.5'>t</p></div><p id=plain>u</p>");
        Assert.Equal(1, Of(r, "plain").Layout.TextColorOpacity);
        Assert.Equal(0.25, Of(r, "inner").Layout.TextColorOpacity!.Value, 6);
        // a background inside the group the text is painted in belongs to the same group
        Assert.Equal("rgb(0, 0, 0)", Of(r, "inner").Layout.BlendedBackgroundColor);
    }

    // ------------------------------------------------------------------ computed styles, generated boxes, form state

    [Fact]
    public void ComputedStylesAreSerializedForTheListedProperties()
    {
        RenderedPage r = Render(
            "<div id=a style='width:120px;height:30px;padding:4px;color:#102030;background-color:rgba(1,2,3,.5);display:flex;position:relative;z-index:3'>x</div><span id=s>y</span>",
            options: new FactsOptions { ComputedStyles = ["display", "color", "background-color", "position", "z-index", "width", "height", "no-such-property"] });
        NodeFacts a = Of(r, "a");
        Assert.Equal("flex", a.GetStyle("display"));
        Assert.Equal("rgb(16, 32, 48)", a.GetStyle("color"));
        Assert.Equal("rgba(1, 2, 3, 0.5)", a.GetStyle("background-color"));
        Assert.Equal("relative", a.GetStyle("position"));
        Assert.Equal("3", a.GetStyle("z-index"));
        Assert.Equal("120px", a.GetStyle("width"));
        Assert.Equal("30px", a.GetStyle("height"));
        Assert.Null(a.GetStyle("no-such-property"));
        Assert.Equal("inline", Of(r, "s").GetStyle("display"));
        Assert.Equal("auto", Of(r, "s").GetStyle("width"));
    }

    [Fact]
    public void GeneratedBoxesAreListedOnTheirHost()
    {
        RenderedPage r = Render(
            "<style>#g::before{content:'>> ';color:#c00}#g::after{content:''}ul{margin:0}</style><p id=g style='margin:0'>body</p><ul><li id=li>item</li></ul>");
        NodeFacts g = Of(r, "g");
        GeneratedBox before = Assert.Single(g.GeneratedBoxes, b => b.PseudoType == "before");
        Assert.Equal("before", before.PseudoType);
        Assert.True(before.HasLayout);
        Assert.Equal(">> ", before.Text);
        Assert.Contains(Of(r, "li").GeneratedBoxes, b => b.PseudoType == "marker" && b.HasLayout);
        // the host's own text starts after the generated text
        LayoutRect hostText = r.Facts[r.Document.GetElementById("g").FirstChild].Layout.BorderBox;
        Assert.True(hostText.X >= before.Layout.BorderBox.Right - 0.01);
    }

    [Fact]
    public void FormStateComesFromTheDocument()
    {
        RenderedPage r = Render("<input id=t value='Ada'><input id=c type=checkbox checked><textarea id=a>one\ntwo</textarea><select><option id=o selected>x</option></select>");
        Assert.Equal("Ada", Of(r, "t").InputValue);
        Assert.True(Of(r, "c").InputChecked);
        Assert.False(Of(r, "t").InputChecked);
        Assert.Equal("one\ntwo", Of(r, "a").TextValue);
        Assert.True(Of(r, "o").OptionSelected);
    }

    [Fact]
    public void ASelectWithoutASelectedOptionSelectsTheFirstAndEveryInputHasAValue()
    {
        RenderedPage r = Render("<input id=e><input id=cb type=checkbox><select><option id=a>a</option><option id=b>b</option></select><select size=3><option id=c>c</option></select><select multiple><option id=d selected>d</option><option id=f selected>f</option></select>");
        Assert.Equal(string.Empty, Of(r, "e").InputValue);
        Assert.Equal("on", Of(r, "cb").InputValue);
        Assert.True(Of(r, "a").OptionSelected);
        Assert.False(Of(r, "b").OptionSelected);
        Assert.False(Of(r, "c").OptionSelected);
        Assert.True(Of(r, "d").OptionSelected);
        Assert.True(Of(r, "f").OptionSelected);
    }

    [Fact]
    public void TextTransformIsAppliedToTheLayoutTextAndBreaksAreTheirOwnTextBoxes()
    {
        RenderedPage r = Render("<p id=u style='text-transform:uppercase;margin:0'>day walks</p><pre id=pre style='margin:0'>ab\ncd\n</pre>");
        Assert.Equal("DAY WALKS", r.Facts[r.Document.GetElementById("u").FirstChild].Layout.LayoutText);
        BoxFacts pre = r.Facts[r.Document.GetElementById("pre").FirstChild].Layout;
        Assert.Equal(new[] { (0, 2), (2, 1), (3, 2), (5, 1) }, pre.TextBoxes.ToArray().Select(t => (t.Start, t.Length)).ToArray());
        Assert.Equal(0, pre.TextBoxes[1].Bounds.Width);
    }

    // ------------------------------------------------------------------ levels

    [Fact]
    public void InteractiveFactsShowTheHoverState()
    {
        const string html = Head + "<style>#b{height:30px;background:#eee}#b:hover{background:#036}</style><div id=b>hover me</div></body></html>";
        InteractivePage page = InteractivePage.Create(html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 300, ViewportHeight = 100 });
        var options = new FactsOptions { ComputedStyles = ["background-color"] };
        RenderedFacts before = AccessibilityFacts.FromInteractive(page, options);
        Node b = page.Page.Document.GetElementById("b");
        Assert.Equal("rgb(238, 238, 238)", before[b].GetStyle("background-color"));
        Assert.Equal("rgb(238, 238, 238)", before[b].Layout.BlendedBackgroundColor);
        page.HandlePointer(Ix.Move(5, 5));
        RenderedFacts after = AccessibilityFacts.FromInteractive(page, options);
        Assert.Equal("rgb(0, 51, 102)", after[page.Page.Document.GetElementById("b")].GetStyle("background-color"));
        Assert.Equal("rgb(0, 51, 102)", after[page.Page.Document.GetElementById("b")].Layout.BlendedBackgroundColor);
        Assert.Equal(page.StyleContext.Hover, after.Context!.Hover);
    }

    [Fact]
    public void ADocumentTheCallerParsedKeysTheFacts()
    {
        Document doc = Document.Parse(Head + "<div id=a style='height:20px'>x</div></body></html>");
        RenderedPage r = AccessibilityFacts.Render(doc, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 200, ViewportHeight = 100 });
        Assert.Same(doc, r.Document);
        Assert.Equal(20, r.Facts[doc.GetElementById("a")].Layout.BorderBox.Height);
    }

    [Fact]
    public void LowLevelUsesTheListTheHostDrew()
    {
        var page = HtmlPage.Create(Head + "<div id=a style='background:#808080;height:30px'>t</div></body></html>", new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 200, ViewportHeight = 100 });
        var trace = new PaintTrace();
        var list = new OneRender.Painting.DisplayList();
        page.Paint(list, new OneRender.Painting.Painter { Observer = trace });
        var hits = new HitTester(page.Fragments, page.Scroll, 200, 100);
        hits.Rebuild();
        RenderedFacts facts = AccessibilityFacts.FromLayout(page, new LayoutQueries(page, hits), list, trace);
        Assert.Equal("rgb(128, 128, 128)", facts[page.Document.GetElementById("a")].Layout.BlendedBackgroundColor);
        Assert.True(trace.LayerCount >= 1);
        Assert.Throws<ArgumentException>(() => AccessibilityFacts.FromLayout(page, new LayoutQueries(page, hits), list, new PaintTrace()));
    }

    // ------------------------------------------------------------------ bounds

    [Fact]
    public void LimitsAreHonoured()
    {
        var limits = new RenderedFactsLimits { MaxBoxes = 20 };
        string many = string.Concat(Enumerable.Repeat("<p>x</p>", 50));
        Assert.Throws<RenderedFactsLimitException>(() => Render(many, options: new FactsOptions { Limits = limits }));
        Assert.Throws<OperationCanceledException>(() =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            AccessibilityFacts.Render("<p>x</p>", new HtmlPageOptions(FontProviders.CreateDefault()), null, cts.Token);
        });
    }

    [Fact]
    public void FactsGrowLinearlyWithThePage()
    {
        static string Page(int rows) => string.Concat(Enumerable.Range(0, rows).Select(i => "<div style='background:#" + (i % 2 == 0 ? "eee" : "ddd") + ";height:20px'><span>row " + i + "</span></div>"));
        var options = new FactsOptions { ComputedStyles = FactsOptions.StandardStyleList };
        Render(Page(50), options: options);
        var sw = Stopwatch.StartNew();
        RenderedPage small = Render(Page(1000), 400, 300, options);
        double smallMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        RenderedPage large = Render(Page(4000), 400, 300, options);
        double largeMs = sw.Elapsed.TotalMilliseconds;
        Assert.True(large.Facts.BoxCount > 3 * small.Facts.BoxCount);
        // four times the page may take about four times as long; a quadratic pass would take sixteen
        Assert.True(largeMs < smallMs * 9, $"1000 rows {smallMs:0} ms, 4000 rows {largeMs:0} ms");
    }
}
