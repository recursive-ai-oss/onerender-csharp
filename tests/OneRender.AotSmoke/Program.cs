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
DocumentStyles styles = DocumentStyles.Create(doc, new StyleContext(), new StyleSourceOptions { IncludeUserAgentSheet = true });
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


return names.Length == 3 && sfnt && painted && pageOk && boxes >= 3 && scrollOk && shaped && again && woff2Ok ? 0 : 1;

internal sealed class NoFonts : OneRender.Layout.IFontProvider
{
    public OneRender.Text.FontCatalog Catalog => throw new NotSupportedException();

    public OneRender.Text.ResolvedFontList Resolve(in OneRender.Style.ComputedValues style) => throw new NotSupportedException();

    public OneRender.Text.ShapedText Shape(ReadOnlySpan<char> text, in OneRender.Style.ComputedValues style, bool rightToLeft = false) => throw new NotSupportedException();

    public OneRender.Text.FontMetricsInfo GetMetrics(in OneRender.Style.ComputedValues style) => throw new NotSupportedException();
}
