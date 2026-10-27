using OneRender.Interaction;
using System;
using OneRender;
using OneRender.Fonts;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using OneRender.Image;
using OneRender.Painting;
using OneRender.Style;
using OneRender.Text;

Console.WriteLine(EngineInfo.Name);
FontFaceInfo face = DefaultFonts.GetFaces("Carlito")[0];
byte[] bytes = face.ReadAllBytes();
Console.WriteLine($"{face} {bytes.Length}");

Document doc = Document.Parse("<!DOCTYPE html><body><p>Hello <b>world</b></p><div style=\"display:flex\"><span>a</span></div>");
DocumentStyles styles = DocumentStyles.Create(doc, new StyleContext(), new StyleSourceOptions { IncludeUserAgentSheet = true, DeclarationValidator = OneRender.Style.StyleValidation.Validator });
var styled = new StyledTree();
new HtmlFrontEnd(styled).Build(styles);
var boxTree = new BoxTree(styled);
boxTree.Build();
Console.WriteLine($"{styled.Count} styled nodes, {boxTree.Count} boxes");
// Block layout over that tree: no reflection or dynamic code, so it must trim and run the same way.
var fragments = new OneRender.Layout.FragmentTree(boxTree);
new OneRender.Layout.BlockLayoutEngine().Layout(boxTree, new OneRender.Layout.LayoutInput(800, 600, FontProviders.CreateDefault()), fragments);
if (fragments.Root.IsNull || fragments.Root.BorderBox.Width != 800)
{
    throw new InvalidOperationException("block layout failed");
}

// Positioning, overflow and the in-place scroll patch too.
HtmlPage scrollPage = HtmlPage.Create(
    "<body style='margin:0'><div style='position:relative;overflow:auto;width:50px;height:20px'><div style='position:absolute;top:60px;width:10px;height:10px'></div><div style='height:100px'></div></div><div style='position:sticky;top:0;height:5px'></div><div style='height:200px'></div></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 50 });
var scrollPainter = new Painter();
var scrollList = new RetainedDisplayList();
scrollPainter.Paint(scrollPage.Fragments, scrollList, new PaintOptions(100, 50) { Scroll = scrollPage.Scroll });
scrollPage.Scroll.SetViewportOffset(0, 30);
Console.WriteLine($"scroll patch changed {scrollPainter.PatchScroll(scrollList)} items");

// Columns: balancing, a spanner and the rewrite of the fragment tree.
HtmlPage columnPage = HtmlPage.Create(
    "<body style='margin:0'><div style='columns:2;column-gap:0;width:100px'><div style='height:10px;break-inside:avoid'></div><div style='height:10px;break-inside:avoid'></div><div style='column-span:all;height:5px'></div><div style='height:10px'></div></div></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 50 });
Console.WriteLine($"columns page {columnPage.Fragments.Count} fragments");

// The rasterizer must trim and run on WebAssembly too.
DisplayList list = new();
list.FillRoundedRect(ItemId.Of(1), new RectF(2, 2, 28, 28), CornerRadii.Uniform(8), new PaintColor(1f, 0f, 0f, 1f));
RgbaImage image = DisplayListRasterizer.Render(list, 32, 32);
Console.WriteLine($"rasterized {image.Width}x{image.Height} red={image.Pixels[((16 * 32) + 16) * 4]}");

// The text service in the browser runtime: shape with fallback and decode a WOFF2 font (Brotli).
var textTree = new StyledTree();
StyledNode textRoot = textTree.AddRoot("div", new StyleDeclaration().Set("font-family", "Calibri"));
ComputedValues style = textRoot.Values;
ShapedText run = FontProviders.CreateDefault().Shape("Office fi AVA \u05D0", in style);
Console.WriteLine($"shaped {run.GlyphCount} glyphs, width {run.Width:F2}");
using System.IO.Stream woff2 = typeof(NoFonts).Assembly.GetManifestResourceStream("hebrew.woff2")!;
var woff2Bytes = new byte[woff2.Length];
woff2.ReadExactly(woff2Bytes);
Console.WriteLine($"woff2 {FontLoader.Load(woff2Bytes).Status}");

// The second paint area through the HTML path: parsers and painter must trim cleanly for WebAssembly.
HtmlPage paintPage = HtmlPage.Create(
    "<body style='margin:0'><div style='width:40px;height:20px;background:linear-gradient(red,blue),url(a.png) space;border:3px solid;border-image:linear-gradient(red,blue) 1;box-shadow:2px 2px 3px gray,inset 0 0 4px black;outline:2px auto;text-decoration:underline wavy red;text-shadow:1px 1px 2px black'>Hi</div><ul><li>x</li></ul></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 80, ViewportHeight = 80 });
var paintList = new DisplayList();
new Painter().Paint(paintPage.Fragments, paintList, new PaintOptions(80, 80));
// Tables: anonymous table objects, spans, collapsed borders and their painting must survive trimming and AOT as well.
HtmlPage tablePage = HtmlPage.Create(
    "<body style='margin:0'><table style='border-collapse:collapse;width:100px'><tr><td colspan=2 style='border:1px solid #000;height:10px'></td></tr><tr><td style='border:2px solid #f00'></td><td style='display:table-cell;border:1px solid #000'></td></tr></table><div style='display:table-cell;height:4px'></div></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 200, ViewportHeight = 100 });
var tableList = new DisplayList();
new Painter().Paint(tablePage.Fragments, tableList, new PaintOptions(200, 100));
Console.WriteLine($"paint items {paintList.Count}, table page {tablePage.Fragments.Count} fragments, {tableList.Count} table items");

// Interaction: a hit test, a dispatched pointer sequence with a listener, and a focus change.
InteractivePage ix = InteractivePage.Create(
    "<body style='margin:0'><div id=a style='width:40px;height:40px'><button id=b style='width:20px;height:20px'>x</button></div></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 100 });
Node ixButton = ix.Page.Document.GetElementById("b");
int ixClicks = 0;
ix.Events.AddEventListener(ixButton, "click", _ => ixClicks++);
bool ixHit = ix.ElementFromPoint(5, 5).Equals(ixButton);
ix.HandlePointer(new PointerInput { Action = PointerAction.Down, X = 5, Y = 5, Button = 0, Buttons = MouseButtons.Primary, Time = 1 });
ix.HandlePointer(new PointerInput { Action = PointerAction.Up, X = 5, Y = 5, Button = 0, Time = 2 });
Console.WriteLine($"interaction hit={ixHit} clicks={ixClicks} focused={ix.FocusedElement.Equals(ixButton)}");
if (!ixHit || ixClicks != 1)
{
    throw new InvalidOperationException("interaction failed");
}


// Text editing: type into a field, compose through the IME, undo and read the value back.
InteractivePage ed = InteractivePage.Create(
    "<body style='margin:0'><input id=t value=hello></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 300, ViewportHeight = 100 });
Node edText = ed.Page.Document.GetElementById("t");
ed.Focus(edText, FocusReason.Script);
ed.SetSelection(edText, 5, 5);
ed.HandleKey(new KeyInput { Key = "!", Down = true });
ed.CompositionStart();
ed.CompositionUpdate("ab", 1);
ed.CompositionEnd("ab");
string edValue = ed.GetValue(edText);
ed.Undo();
Console.WriteLine($"editing value={edValue} afterUndo={ed.GetValue(edText)}");
if (edValue != "hello!ab")
{
    throw new InvalidOperationException("editing failed");
}

// Animations: a keyframe animation and a transition, ticked with the host clock; the events and the animated value must come out.
InteractivePage mo = InteractivePage.Create(
    "<style>@keyframes f{from{opacity:0;transform:rotate(0deg)}to{opacity:1;transform:rotate(90deg)}}#a{width:20px;height:20px;animation:f 1s linear both}#b{width:10px;height:10px;opacity:0;transition:opacity 1s linear}#b:focus{opacity:1}</style><body style='margin:0'><div id=a></div><div id=b tabindex=0></div></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 100 });
int moStarts = 0;
mo.Events.AddEventListener(mo.Page.Document.GetElementById("a"), "animationstart", _ => moStarts++);
mo.Advance(0);
mo.Focus(mo.Page.Document.GetElementById("b"));
mo.Advance(500);
double moOpacity = mo.Page.Styled.FindBySource(mo.Page.Document.GetElementById("a").Index).Values.Opacity;
double moB = mo.Page.Styled.FindBySource(mo.Page.Document.GetElementById("b").Index).Values.Opacity;
Console.WriteLine($"animation starts={moStarts} opacity={moOpacity:F2} transition={moB:F2}");
if (moStarts != 1 || Math.Abs(moOpacity - 0.5) > 1e-6 || Math.Abs(moB - 0.5) > 1e-6)
{
    throw new InvalidOperationException("animation failed");
}

// Transforms, filters, clip paths, masks, blending and an inline svg on WebAssembly too.
RgbaImage fxPage = HtmlRender.ToImage(
    "<body style='margin:0'><div style='width:20px;height:20px;background:#f00;transform:rotate(10deg);filter:blur(1px);clip-path:circle(50%);mask-image:linear-gradient(#000,#000);mix-blend-mode:multiply'></div>"
    + "<svg width='20' height='20'><rect width='20' height='20' fill='#0a0'/></svg></body>",
    new HtmlRenderOptions { Width = 64, Height = 64 });
Console.WriteLine($"effects {fxPage.Pixels[((10 * 64) + 10) * 4]}");

// The rendered-facts producer on WebAssembly too.
OneRender.Accessibility.RenderedPage factsPage = OneRender.Accessibility.AccessibilityFacts.Render(
    "<body style='margin:0'><div style='background:#036;color:#fff'>Hi</div></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 50 });
Console.WriteLine($"facts {factsPage.Facts.BoxCount}");


// Incremental updates: edit a styled-node tree, query through the layout queries (which flush), restyle a state, and compare with the geometry.
var liveTree = new StyledTree();
StyledNode liveRoot = liveTree.AddRoot("div", new StyleDeclaration().Set("display", "block").Set("width", "100px"));
StyledNode liveKid = liveTree.AddElement(liveRoot, "div", new StyleDeclaration().Set("display", "block").Set("height", "10px"));
liveTree.SetStateStyle(liveKid, ElementStates.Hover, new StyleDeclaration().Set("height", "30px"));
var livePage = new LivePage(liveTree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = 200, ViewportHeight = 100 });
var liveQueries = new LayoutQueries(livePage, new HitTester(livePage.Fragments, livePage.Scroll, 200, 100));
liveTree.SetStates(liveKid, ElementStates.Hover);
int liveHeight = liveQueries.OffsetHeight(liveKid);
liveTree.AddText(liveTree.AddElement(liveRoot, "span"), "x");
liveTree.SetStates(liveKid, ElementStates.None);
FlushResult liveFlush = livePage.Flush();
bool liveOk = liveHeight == 30 && liveQueries.OffsetHeight(liveKid) == 10 && liveFlush.Repainted && livePage.Display.Current.Count > 0;

// Slot reuse: a removed subtree's slots serve the next insert, and the old handle is stale afterwards.
int liveSlots = liveTree.Count;
StyledNode liveGone = liveRoot.LastChild;
liveTree.Remove(liveGone);
livePage.Flush();
StyledNode liveNew = liveTree.AddElement(liveRoot, "p");
bool reuseOk = !liveGone.IsValid && liveNew.IsValid && liveNew.Index < liveSlots && liveTree.Count == liveSlots;
liveOk = liveOk && reuseOk;
Console.WriteLine($"incremental {liveOk}");
// The PDF backend: a tagged page through the PDF/UA identification pass must trim as well.
byte[] pdfBytes = OneRender.Pdf.HtmlPdf.ToPdf(
    "<!DOCTYPE html><html lang='en'><head><title>T</title></head><body><h1>Hi</h1><p style='background:linear-gradient(#f00,#00f);box-shadow:2px 2px 4px #888'>text</p></body></html>",
    new OneRender.Pdf.HtmlPdfOptions { Output = new OneRender.Pdf.PdfOutputOptions { Profile = OneRender.Pdf.PdfProfile.PdfUA1 } });
Console.WriteLine($"pdf {pdfBytes.Length} bytes");

internal sealed class NoFonts : OneRender.Layout.IFontProvider
{
    public OneRender.Text.FontCatalog Catalog => throw new NotSupportedException();

    public OneRender.Text.ResolvedFontList Resolve(in OneRender.Style.ComputedValues style) => throw new NotSupportedException();

    public OneRender.Text.ShapedText Shape(ReadOnlySpan<char> text, in OneRender.Style.ComputedValues style, bool rightToLeft = false) => throw new NotSupportedException();

    public OneRender.Text.FontMetricsInfo GetMetrics(in OneRender.Style.ComputedValues style) => throw new NotSupportedException();
}
