using System;
using System.IO;
using OneRender;
using OneRender.Fonts;
using OneRender.Image;
using OneRender.Painting;
using OneRender.Pdf;
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
// Block layout over that tree: no reflection or dynamic code, so it must trim and run the same way.
var fragments = new OneRender.Layout.FragmentTree(boxTree);
new OneRender.Layout.BlockLayoutEngine().Layout(boxTree, new OneRender.Layout.LayoutInput(800, 600, new NoFonts()), fragments);
if (fragments.Root.IsNull || fragments.Root.BorderBox.Width != 800)
{
    return 4;
}

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
return names.Length == 3 && sfnt && painted && boxes == 0 ? 0 : 1;

internal sealed class NoFonts : OneRender.Layout.IFontProvider
{
    public OneFont.FontFallbackChain GetFallbackChain(in OneRender.Style.ComputedValues style) => throw new NotSupportedException();
}
