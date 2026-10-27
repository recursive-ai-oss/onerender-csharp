using System;
using System.IO;
using OneFont;
using OneRender;
using OneRender.Fonts;
using OneRender.Image;
using OneRender.Pdf;
using OneRender.Text;

string[] names = [EngineInfo.Name, ImageBackendInfo.Name, PdfBackendInfo.Name];
Console.WriteLine(string.Join(' ', names));
// The provisional geometry entry point must survive trimming and AOT as well.
int boxes = GeometryProbe.LayOut("<p>x</p>", null, null, 800, 600).Count;

// Load one embedded face and check it is a TrueType file ('true' or 0x00010000 sfnt version) of the declared size.
if (!DefaultFonts.TryResolveFamily("Arial", out string? family))
{
    return 2;
}

FontFaceInfo face = DefaultFonts.GetFaces(family)[0];
byte[] bytes = face.ReadAllBytes();
Console.WriteLine($"{face} {bytes.Length} bytes");
bool sfnt = bytes.Length == face.Length && bytes[0] == 0 && bytes[1] == 1 && bytes[2] == 0 && bytes[3] == 0;
// The text service: match, fall back, shape (ligature, kerning, Hebrew fallback) and decode a WOFF2 font (Brotli).
var catalog = new FontCatalog(aliases: new SmokeAliases());
foreach (FontFaceInfo info in DefaultFonts.Faces)
{
    catalog.AddFont(info.ReadAllBytes(), info.Family, info.Weight, info.Slant == FontSlant.Italic ? FontStyleKind.Italic : FontStyleKind.Normal, info.Stretch);
}

var shaper = new TextShaper(catalog);
FontFamilyName[] families = [new FontFamilyName("Calibri"), FontFamilyName.OfGeneric(GenericFamily.SansSerif)];
GlyphRun run = shaper.Shape("Office fi AVA \u05D0\u05D1", new FontRequest(families, 16));
bool shaped = run.GlyphCount > 5 && run.Width > 0 && run.Segments.Length == 2 && run.MissingGlyphCount == 0;
bool again = ReferenceEquals(run, shaper.Shape("Office fi AVA \u05D0\u05D1", new FontRequest(families, 16)));

using System.IO.Stream woff2 = typeof(SmokeAliases).Assembly.GetManifestResourceStream("hebrew.woff2")!;
var woff2Bytes = new byte[woff2.Length];
woff2.ReadExactly(woff2Bytes);
FontLoadResult decoded = FontLoader.Load(woff2Bytes);
bool woff2Ok = decoded.Status == FontLoadStatus.Ok && decoded.Face!.HasGlyph(0x05D0);
Console.WriteLine($"shaped {run.GlyphCount} glyphs, width {run.Width:F2}, cached {again}, woff2 {decoded.Status}");
return names.Length == 3 && sfnt && boxes == 0 && shaped && again && woff2Ok ? 0 : 1;

internal sealed class SmokeAliases : IFontFamilyAliases
{
    public bool TryResolve(ReadOnlySpan<char> name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? family) => DefaultFonts.TryResolveFamily(name, out family);
}
