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
// The provisional geometry entry point must survive trimming and AOT as well.
int boxes = GeometryProbe.LayOut("<p>x</p>", null, null, 800, 600).Count;

// Build a box tree from a small HTML string: parse, cascade, styled tree, box tree.
Document doc = Document.Parse("<!DOCTYPE html><body><p style=\"width:50%;margin:1em auto\">Hello <b>world</b></p><div style=\"display:flex\"><span>a</span></div><img src=x.png>");
DocumentStyles styles = DocumentStyles.Create(doc, new StyleContext(), new StyleSourceOptions { IncludeUserAgentSheet = true });
var styled = new StyledTree();
new HtmlFrontEnd(styled).Build(styles);
var boxTree = new BoxTree(styled);
boxTree.Build();
string dump = boxTree.Dump();
Console.WriteLine($"{styled.Count} styled nodes, {boxTree.Count} boxes");
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
var catalog = new FontCatalog(aliases: new SmokeAliases());
foreach (FontFaceInfo info in DefaultFonts.Faces)
{
    catalog.AddFont(info.ReadAllBytes(), info.Family, info.Weight, info.Slant == FontSlant.Italic ? FontStyleKind.Italic : FontStyleKind.Normal, info.Stretch);
}

IFontProvider fonts = new TextFontProvider(catalog);
var textTree = new StyledTree();
StyledNode textRoot = textTree.AddRoot("div", new StyleDeclaration().Set("font-family", "Calibri, sans-serif"));
ComputedValues style = textRoot.Values;
ShapedText run = fonts.Shape("Office fi AVA \u05D0\u05D1", in style);
bool shaped = run.GlyphCount > 5 && run.Width > 0 && run.Segments.Length == 2 && run.MissingGlyphCount == 0;
bool again = ReferenceEquals(run, fonts.Shape("Office fi AVA \u05D0\u05D1", in style));

using System.IO.Stream woff2 = typeof(SmokeAliases).Assembly.GetManifestResourceStream("hebrew.woff2")!;
var woff2Bytes = new byte[woff2.Length];
woff2.ReadExactly(woff2Bytes);
FontLoadResult decoded = FontLoader.Load(woff2Bytes);
bool woff2Ok = decoded.Status == FontLoadStatus.Ok && decoded.Face!.HasGlyph(0x05D0);
Console.WriteLine($"shaped {run.GlyphCount} glyphs, width {run.Width:F2}, cached {again}, woff2 {decoded.Status}");
return names.Length == 3 && sfnt && painted && boxes == 0 && shaped && again && woff2Ok ? 0 : 1;

internal sealed class SmokeAliases : IFontFamilyAliases
{
    public bool TryResolve(ReadOnlySpan<char> name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? family) => DefaultFonts.TryResolveFamily(name, out family);
}

