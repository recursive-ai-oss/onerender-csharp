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
DocumentStyles styles = DocumentStyles.Create(doc, new StyleContext(), new StyleSourceOptions { IncludeUserAgentSheet = true });
var styled = new StyledTree();
new HtmlFrontEnd(styled).Build(styles);
var boxTree = new BoxTree(styled);
boxTree.Build();
Console.WriteLine($"{styled.Count} styled nodes, {boxTree.Count} boxes");
// The rasterizer must trim and run on WebAssembly too.
DisplayList list = new();
list.FillRoundedRect(ItemId.Of(1), new RectF(2, 2, 28, 28), CornerRadii.Uniform(8), new PaintColor(1f, 0f, 0f, 1f));
RgbaImage image = DisplayListRasterizer.Render(list, 32, 32);
Console.WriteLine($"rasterized {image.Width}x{image.Height} red={image.Pixels[((16 * 32) + 16) * 4]}");

// The text service in the browser runtime: shape with fallback and decode a WOFF2 font (Brotli).
var catalog = new FontCatalog(aliases: new WasmAliases());
foreach (FontFaceInfo info in DefaultFonts.Faces)
{
    catalog.AddFont(info.ReadAllBytes(), info.Family, info.Weight, info.Slant == FontSlant.Italic ? FontStyleKind.Italic : FontStyleKind.Normal, info.Stretch);
}

var textTree = new StyledTree();
StyledNode textRoot = textTree.AddRoot("div", new StyleDeclaration().Set("font-family", "Calibri"));
ComputedValues style = textRoot.Values;
ShapedText run = new TextFontProvider(catalog).Shape("Office fi AVA \u05D0", in style);
Console.WriteLine($"shaped {run.GlyphCount} glyphs, width {run.Width:F2}");
using System.IO.Stream woff2 = typeof(WasmAliases).Assembly.GetManifestResourceStream("hebrew.woff2")!;
var woff2Bytes = new byte[woff2.Length];
woff2.ReadExactly(woff2Bytes);
Console.WriteLine($"woff2 {FontLoader.Load(woff2Bytes).Status}");

internal sealed class WasmAliases : IFontFamilyAliases
{
    public bool TryResolve(ReadOnlySpan<char> name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? family) => DefaultFonts.TryResolveFamily(name, out family);
}
