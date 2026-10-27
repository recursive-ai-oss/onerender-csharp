using System;
using System.IO;
using OneRender;
using OneRender.Fonts;
using OneRender.Image;
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
return names.Length == 3 && sfnt && boxes == 0 ? 0 : 1;
