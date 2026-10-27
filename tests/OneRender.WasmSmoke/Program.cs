using System;
using OneRender;
using OneRender.Fonts;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using OneRender.Image;
using OneRender.Painting;

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
// Block layout over that tree: no reflection or dynamic code, so it must trim and run the same way.
var fragments = new OneRender.Layout.FragmentTree(boxTree);
new OneRender.Layout.BlockLayoutEngine().Layout(boxTree, new OneRender.Layout.LayoutInput(800, 600, new NoFonts()), fragments);
if (fragments.Root.IsNull || fragments.Root.BorderBox.Width != 800)
{
    throw new InvalidOperationException("block layout failed");
}

// Positioning, overflow and the in-place scroll patch too.
HtmlPage scrollPage = HtmlPage.Create(
    "<body style='margin:0'><div style='position:relative;overflow:auto;width:50px;height:20px'><div style='position:absolute;top:60px;width:10px;height:10px'></div><div style='height:100px'></div></div><div style='position:sticky;top:0;height:5px'></div><div style='height:200px'></div></body>",
    new HtmlPageOptions(new DefaultFontProvider()) { ViewportWidth = 100, ViewportHeight = 50 });
var scrollPainter = new Painter();
var scrollList = new RetainedDisplayList();
scrollPainter.Paint(scrollPage.Fragments, scrollList, new PaintOptions(100, 50) { Scroll = scrollPage.Scroll });
scrollPage.Scroll.SetViewportOffset(0, 30);
Console.WriteLine($"scroll patch changed {scrollPainter.PatchScroll(scrollList)} items");

// The rasterizer must trim and run on WebAssembly too.
DisplayList list = new();
list.FillRoundedRect(ItemId.Of(1), new RectF(2, 2, 28, 28), CornerRadii.Uniform(8), new PaintColor(1f, 0f, 0f, 1f));
RgbaImage image = DisplayListRasterizer.Render(list, 32, 32);
Console.WriteLine($"rasterized {image.Width}x{image.Height} red={image.Pixels[((16 * 32) + 16) * 4]}");

internal sealed class NoFonts : OneRender.Layout.IFontProvider
{
    public OneFont.FontFallbackChain GetFallbackChain(in OneRender.Style.ComputedValues style) => throw new NotSupportedException();
}
