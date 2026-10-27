using OneRender.Interaction;
using System;
using System.IO;
using OneRender;
using OneRender.Fonts;
using OneRender.Image;
using OneRender.Painting;
using OneRender.Layout;
using OneRender.Pdf;
using OneRender.Style;
using OneRender.Text;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

string[] names = [EngineInfo.Name, ImageBackendInfo.Name, PdfBackendInfo.Name];
Console.WriteLine(string.Join(' ', names));
// The geometry entry point and the HTML-to-pixels path must survive trimming and AOT as well.
int boxes = GeometryProbe.LayOut("<p>x</p>", null, null, 800, 600, FontProviders.CreateDefault()).Count;
RgbaImage page = HtmlRender.ToImage("<body style='margin:0'><div style='width:20px;height:10px;background:#f00'></div></body>", new HtmlRenderOptions { Width = 32, Height = 32 });
bool pageOk = page.Pixels[((5 * 32) + 5) * 4] == 255 && page.Pixels[((5 * 32) + 5) * 4 + 1] == 0 && page.Pixels[((20 * 32) + 5) * 4 + 1] == 255;

// Build a box tree from a small HTML string: parse, cascade, styled tree, box tree.
Document doc = Document.Parse("<!DOCTYPE html><body><p style=\"width:50%;margin:1em auto\">Hello <b>world</b></p><div style=\"display:flex\"><span>a</span></div><img src=x.png>");
DocumentStyles styles = DocumentStyles.Create(doc, new StyleContext(), new StyleSourceOptions { IncludeUserAgentSheet = true, DeclarationValidator = OneRender.Style.StyleValidation.Validator });
var styled = new StyledTree();
new HtmlFrontEnd(styled).Build(styles);
var boxTree = new BoxTree(styled);
boxTree.Build();
string dump = boxTree.Dump();
// Block layout over that tree: no reflection or dynamic code, so it must trim and run the same way.
var fragments = new OneRender.Layout.FragmentTree(boxTree);
new OneRender.Layout.BlockLayoutEngine().Layout(boxTree, new OneRender.Layout.LayoutInput(800, 600, FontProviders.CreateDefault()), fragments);
if (fragments.Root.IsNull || fragments.Root.BorderBox.Width != 800)
{
    return 4;
}

// Positioned boxes, overflow, sticky positioning and the in-place scroll patch must survive trimming and AOT as well.
HtmlPage scrollPage = HtmlPage.Create(
    "<body style='margin:0'><div style='position:relative;overflow:auto;width:50px;height:20px'><div style='position:absolute;left:5px;top:60px;width:10px;height:10px;background:#00f'></div><div style='height:100px'></div></div><div style='position:sticky;top:0;height:5px;background:#f00'></div><div style='height:200px'></div></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 50 });
var scrollPainter = new Painter();
var scrollList = new RetainedDisplayList();
scrollPainter.Paint(scrollPage.Fragments, scrollList, new PaintOptions(100, 50) { Scroll = scrollPage.Scroll });
scrollPage.Scroll.SetViewportOffset(0, 30);
bool scrollOk = scrollPainter.PatchScroll(scrollList) > 0;

// Inline layout: wrapping, a nested inline box, an atomic inline and a bidi run through the real fonts.
HtmlPage textPage = HtmlPage.Create(
    "<body style='margin:0;font:16px/20px Arial'><p style='width:90px;margin:0'>one two <b>three <span style='padding:0 2px'>four</span></b> <i style='display:inline-block;width:10px;height:5px'></i> \u05d0\u05d1\u05d2 five</p></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 200, ViewportHeight = 100 });
int textLines = 0;
OneRender.Layout.Fragment walk = textPage.Fragments.Root;
while (!walk.IsNull)
{
    if (walk.Kind == OneRender.Layout.FragmentKind.Line)
    {
        textLines++;
    }

    if (!walk.FirstChild.IsNull)
    {
        walk = walk.FirstChild;
        continue;
    }

    while (!walk.IsNull && walk.NextSibling.IsNull)
    {
        walk = walk.Parent;
    }

    walk = walk.IsNull ? walk : walk.NextSibling;
}

if (textLines < 3)
{
    return 5;
}

// A wrapping flex container with grow and gap: the flex algorithm must survive trimming and AOT.
HtmlPage flexPage = HtmlPage.Create(
    "<body style='margin:0'><div style='display:flex;flex-wrap:wrap;gap:10px;width:100px'><div style='flex:1 1 40px;height:10px'></div><div style='flex:1 1 40px;height:10px'></div><div style='flex:1 1 40px;height:10px'></div></div></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 50 });
if (flexPage.Fragments.Root.IsNull || flexPage.Fragments.Root.BorderBox.Width != 100)
{
    return 6;
}

// Columns: balancing, a spanner, a forced break and the rewrite of the fragment tree must survive trimming and AOT as well.
HtmlPage columnPage = HtmlPage.Create(
    "<body style='margin:0'><div id=m style='columns:2;column-gap:0;column-rule:2px solid;width:100px'><div style='height:10px;break-inside:avoid'></div><div style='height:10px;break-inside:avoid'></div><div style='column-span:all;height:5px'></div><div style='height:10px;break-after:column'></div><div style='height:10px'></div></div></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 50 });
var columnPainter = new Painter();
var columnList = new RetainedDisplayList();
columnPainter.Paint(columnPage.Fragments, columnList, new PaintOptions(100, 50));
int columnCount = 0;
for (OneRender.Layout.Fragment cf = columnPage.Fragments.Root.FirstChild.FirstChild.FirstChild; !cf.IsNull; cf = cf.NextSibling)
{
    if (cf.Kind == OneRender.Layout.FragmentKind.Column)
    {
        columnCount++;
    }
}

if (columnCount != 4)
{
    return 6;
}

// A grid with fr tracks, auto-fill, a subgrid and a named line: placement, track sizing and subgrid must survive trimming and AOT.
HtmlPage gridPage = HtmlPage.Create(
    "<body style='margin:0'><div style='display:grid;grid-template-columns:[a] 20px repeat(auto-fill,minmax(10px,1fr));gap:2px;width:100px'><div style='grid-column:a / span 2;display:grid;grid-template-columns:subgrid'><i style='display:block;height:5px'></i></div><i style='display:block;height:7px'></i></div></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 50 });
if (gridPage.Fragments.Root.IsNull || gridPage.Fragments.Root.BorderBox.Width != 100)
{
    return 7;
}

// The second paint area (layered backgrounds, gradients, shadows, border-image, outlines, decoration, text-shadow, markers) draws
// through the same style parsers and painter code that must survive trimming and AOT.
RgbaImage paintPage = HtmlRender.ToImage(
    "<body style='margin:0;font:14px Arial'><div style='width:40px;height:20px;background:linear-gradient(#f00,#f00) 0 0/10px 10px space,radial-gradient(circle,#fff,#00f);border:3px solid;border-image:linear-gradient(#0f0,#00f) 1;box-shadow:2px 2px 3px #888,inset 0 0 4px #000;outline:2px auto'></div><p style='text-decoration:underline wavy #f00;text-shadow:1px 1px 2px #000;margin:2px'>Hi</p><ul><li>x</li></ul></body>",
    new HtmlRenderOptions { Width = 80, Height = 80 });
int paintAt = ((12 * 80) + 20) * 4;
bool paintOk = paintPage.Pixels[paintAt] > 200 && paintPage.Pixels[paintAt + 1] < 40 && paintPage.Pixels[paintAt + 2] < 40;
if (!paintOk)
{
    return 7;
}

// The third paint area: a transform, a filter, a clip-path, a mask, a blend mode and an inline svg go through the same parsers, painter
// and rasterizer (including OneImage's SVG renderer) that must survive trimming and AOT.
RgbaImage fxPage = HtmlRender.ToImage(
    "<body style='margin:0'><svg width='0' height='0' style='position:absolute'><clipPath id='c'><circle cx='10' cy='10' r='8'/></clipPath></svg>"
    + "<div style='position:absolute;left:0;top:0;width:20px;height:20px;background:#f00;transform:translate(20px,0) rotate(10deg);filter:blur(1px) saturate(2)'></div>"
    + "<div style='position:absolute;left:0;top:30px;width:20px;height:20px;background:#00f;clip-path:url(#c);mask-image:linear-gradient(#000,#000);mix-blend-mode:multiply'></div>"
    + "<svg style='position:absolute;left:30px;top:30px' width='20' height='20'><rect width='20' height='20' fill='#0a0'/></svg></body>",
    new HtmlRenderOptions { Width = 64, Height = 64 });
int fxBlue = ((40 * 64) + 10) * 4, fxSvg = ((40 * 64) + 40) * 4;
if (fxPage.Pixels[fxBlue + 2] < 200 || fxPage.Pixels[fxSvg + 1] < 120 || fxPage.Pixels[fxSvg] > 60)
{
    return 8;
}

Console.WriteLine($"{styled.Count} styled nodes, {boxTree.Count} boxes, scroll patch {scrollOk}");
if (boxTree.Count < 8 || !dump.Contains("flex", StringComparison.Ordinal))
{
    return 3;
}

// Load one embedded face and check it is a TrueType file ('true' or 0x00010000 sfnt version) of the declared size.
if (!DefaultFonts.TryResolveFamily("Arial", out string? family))
{
    return 2;
}

FontFaceInfo face = DefaultFonts.GetFaces(family)[0];
byte[] bytes = face.ReadAllBytes();
Console.WriteLine($"{face} {bytes.Length} bytes");
bool sfnt = bytes.Length == face.Length && bytes[0] == 0 && bytes[1] == 1 && bytes[2] == 0 && bytes[3] == 0;

// Rasterize a small display list through oneimage: a rounded rectangle, a gradient, a glyph run and a group.
DisplayList list = new();
list.FillRoundedRect(ItemId.Of(1), new RectF(2, 2, 28, 28), CornerRadii.Uniform(8), new PaintColor(1f, 0f, 0f, 1f));
Paint gradient = list.ConicGradient(16, 16, 0f, [new GradientStop(0f, new PaintColor(0f, 0f, 1f, 1f)), new GradientStop(1f, new PaintColor(0f, 1f, 0f, 1f))]);
list.PushGroup(ItemId.Of(2), 0.5f);
list.FillRect(ItemId.Of(3), new RectF(8, 8, 16, 16), gradient);
list.PopGroup();
OneFont.FontFace font = OneFont.FontFace.Load(bytes);
int glyph = font.GetGlyphIndex('A');
list.DrawGlyphRun(ItemId.Of(4), font, 12f, 4f, 14f, new PaintColor(0f, 0f, 0f, 1f), [glyph], [0f], default, [0], "A");
RgbaImage image = DisplayListRasterizer.Render(list, 32, 32);
bool painted = image.Pixels[((16 * 32) + 16) * 4 + 3] == 255 && image.Pixels[(((5 * 32) + 10) * 4) + 1] < 50;
Console.WriteLine($"rasterized {image.Width}x{image.Height} painted={painted}");
// The text service: match, fall back, shape (ligature, kerning, Hebrew fallback) and decode a WOFF2 font (Brotli).
IFontProvider fonts = FontProviders.CreateDefault();
var textTree = new StyledTree();
StyledNode textRoot = textTree.AddRoot("div", new StyleDeclaration().Set("font-family", "Calibri, sans-serif"));
ComputedValues style = textRoot.Values;
ShapedText run = fonts.Shape("Office fi AVA \u05D0\u05D1", in style);
bool shaped = run.GlyphCount > 5 && run.Width > 0 && run.Segments.Length == 2 && run.MissingGlyphCount == 0;
bool again = ReferenceEquals(run, fonts.Shape("Office fi AVA \u05D0\u05D1", in style));

using System.IO.Stream woff2 = typeof(NoFonts).Assembly.GetManifestResourceStream("hebrew.woff2")!;
var woff2Bytes = new byte[woff2.Length];
woff2.ReadExactly(woff2Bytes);
FontLoadResult decoded = FontLoader.Load(woff2Bytes);
bool woff2Ok = decoded.Status == FontLoadStatus.Ok && decoded.Face!.HasGlyph(0x05D0);
Console.WriteLine($"shaped {run.GlyphCount} glyphs, width {run.Width:F2}, cached {again}, woff2 {decoded.Status}");


// Tables: anonymous table objects, spans, collapsed borders and their painting must survive trimming and AOT as well.
HtmlPage tablePage = HtmlPage.Create(
    "<body style='margin:0'><table style='border-collapse:collapse;width:100px'><tr><td colspan=2 style='border:1px solid #000;height:10px'></td></tr><tr><td style='border:2px solid #f00'></td><td style='display:table-cell;border:1px solid #000'></td></tr></table><div style='display:table-cell;height:4px'></div></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 200, ViewportHeight = 100 });
var tableList = new DisplayList();
new Painter().Paint(tablePage.Fragments, tableList, new PaintOptions(200, 100));
bool tableOk = tablePage.Fragments.Count > 8 && tableList.Count > 0;

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
bool ixOk = ixHit && ixClicks == 1 && ix.FocusedElement.Equals(ixButton);


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
bool moOk = moStarts == 1 && Math.Abs(moOpacity - 0.5) < 1e-6 && Math.Abs(moB - 0.5) < 1e-6;

// The rendered-facts producer: facts for a small page through the snapshot route, with colors behind text and form state.
OneRender.Accessibility.RenderedPage factsPage = OneRender.Accessibility.AccessibilityFacts.Render(
    "<body style='margin:0'><div id=a style='background:#036;color:#fff'>Hi</div><input id=i value=x></body>",
    new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 50 });
bool factsOk = factsPage.Facts.BoxCount > 3 && OneRender.Accessibility.AccessibilityInfo.Name.Length > 0;


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

// The PDF backend: a tagged one-page file with a gradient and a rasterized island, through the PDF/UA identification pass.
byte[] pdfBytes = HtmlPdf.ToPdf(
    "<!DOCTYPE html><html lang='en'><head><title>T</title></head><body><h1>Hi</h1><p style='background:linear-gradient(#f00,#00f);box-shadow:2px 2px 4px #888'>text <a href='https://example.invalid/'>link</a></p></body></html>",
    new HtmlPdfOptions { Output = new PdfOutputOptions { Profile = PdfProfile.PdfUA1 } });
bool pdfOk = pdfBytes.Length > 500 && pdfBytes[0] == (byte)'%' && pdfBytes[1] == (byte)'P' && pdfBytes[2] == (byte)'D' && pdfBytes[3] == (byte)'F';

// Paged media: @page with margin boxes, page counters, a target counter and a leader, over several pages.
OneRender.Paged.PagedDocument paged = OneRender.Paged.PagedDocument.Create(
    "<!DOCTYPE html><html><head><style>@page{size:200px 120px;margin:20px;@bottom-center{content:'Page ' counter(page) ' of ' counter(pages)}}"
    + "a::after{content:leader('.') target-counter(attr(href url),page)}div{height:60px;break-inside:avoid}</style></head><body><p><a href='#t'>Go</a></p><div>a</div><div>b</div><div id='t'>c</div></body></html>",
    new OneRender.Paged.PagedOptions(FontProviders.CreateDefault()));
bool pagedOk = paged.PageCount >= 3 && paged.Destinations.Count == 1 && paged.GetPageLayer(0) is not null;

return moOk && liveOk && pdfOk && pagedOk && factsOk && ixOk && names.Length == 3 && tableOk && sfnt && painted && pageOk && boxes >= 3 && scrollOk && shaped && again && woff2Ok ? 0 : 1;

internal sealed class NoFonts : OneRender.Layout.IFontProvider
{
    public OneRender.Text.FontCatalog Catalog => throw new NotSupportedException();

    public OneRender.Text.ResolvedFontList Resolve(in OneRender.Style.ComputedValues style) => throw new NotSupportedException();

    public OneRender.Text.ShapedText Shape(ReadOnlySpan<char> text, in OneRender.Style.ComputedValues style, bool rightToLeft = false) => throw new NotSupportedException();

    public OneRender.Text.FontMetricsInfo GetMetrics(in OneRender.Style.ComputedValues style) => throw new NotSupportedException();
}
