using System;
using OneRender;
using OneRender.Fonts;
using OneRender.Text;

Console.WriteLine(EngineInfo.Name);
FontFaceInfo face = DefaultFonts.GetFaces("Carlito")[0];
byte[] bytes = face.ReadAllBytes();
Console.WriteLine($"{face} {bytes.Length}");

// The text service in the browser runtime: shape with fallback and decode a WOFF2 font (Brotli).
var catalog = new FontCatalog(aliases: new WasmAliases());
foreach (FontFaceInfo info in DefaultFonts.Faces)
{
    catalog.AddFont(info.ReadAllBytes(), info.Family, info.Weight, info.Slant == FontSlant.Italic ? FontStyleKind.Italic : FontStyleKind.Normal, info.Stretch);
}

GlyphRun run = new TextShaper(catalog).Shape("Office fi AVA \u05D0", new FontRequest([new FontFamilyName("Calibri")], 16));
Console.WriteLine($"shaped {run.GlyphCount} glyphs, width {run.Width:F2}");
using System.IO.Stream woff2 = typeof(WasmAliases).Assembly.GetManifestResourceStream("hebrew.woff2")!;
var woff2Bytes = new byte[woff2.Length];
woff2.ReadExactly(woff2Bytes);
Console.WriteLine($"woff2 {FontLoader.Load(woff2Bytes).Status}");

internal sealed class WasmAliases : IFontFamilyAliases
{
    public bool TryResolve(ReadOnlySpan<char> name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? family) => DefaultFonts.TryResolveFamily(name, out family);
}
