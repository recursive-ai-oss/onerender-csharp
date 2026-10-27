using OneRender.Style;
using System.Linq;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using OneRender.Fonts;
using OneRender.Text;

namespace OneRender.Tests.Text;

/// <summary>The packaged default fonts' alias table, as a host would hand it to the catalog.</summary>
internal sealed class DefaultFontAliases : IFontFamilyAliases
{
    public bool TryResolve(ReadOnlySpan<char> name, [NotNullWhen(true)] out string? family) => DefaultFonts.TryResolveFamily(name, out family);
}

/// <summary>A resolver over an in-memory table that records what was asked for.</summary>
internal sealed class MapResolver : IFontResourceResolver
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public List<(string Url, string? Base)> Requests { get; } = [];

    public MapResolver Add(string url, byte[] data)
    {
        _files[url] = data;
        return this;
    }

    public byte[]? LoadFont(string url, string? baseUrl)
    {
        Requests.Add((url, baseUrl));
        return _files.TryGetValue(url, out byte[]? d) ? d : null;
    }
}

internal sealed class FakeSystemFonts : ISystemFontProvider
{
    public List<SystemFontFace> Faces { get; } = [];

    public IReadOnlyList<SystemFontFace> GetFaces(string family) => Faces.FindAll(f => string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase));

    public SystemFontFace? FindByName(string name) => Faces.Find(f => string.Equals(f.FullName, name, StringComparison.OrdinalIgnoreCase) || string.Equals(f.PostScriptName, name, StringComparison.OrdinalIgnoreCase));

    public SystemFontFace? FindFallback(int codePoint) => Faces.Find(f => f.Family == "Fallback Arabic" && codePoint >= 0x600 && codePoint <= 0x6FF);
}

internal static class TestFonts
{
    public static byte[] Data(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "testdata", "fonts", name));

    /// <summary>A catalog holding the 20 packaged faces, with their alias table.</summary>
    public static FontCatalog Defaults(IFontResourceResolver? resolver = null, ISystemFontProvider? system = null)
    {
        var catalog = new FontCatalog(resolver, new DefaultFontAliases(), system);
        foreach (FontFaceInfo info in DefaultFonts.Faces)
        {
            Assert.True(catalog.AddFont(info.ReadAllBytes(), info.Family, info.Weight, info.Slant == FontSlant.Italic ? FontStyleKind.Italic : FontStyleKind.Normal, info.Stretch));
        }

        return catalog;
    }

    public static byte[] Liberation(string file) => DefaultFonts.Faces.First(f => f.FileName == file).ReadAllBytes();

    public static FontRequest Request(string families, float size = 16, float weight = 400, FontStyleKind style = FontStyleKind.Normal, float stretch = 100, FontSynthesis synthesis = FontSynthesis.All, FontFeatureSettings? features = null)
    {
        Assert.True(FontCss.TryParseFamilyList(families, out FontFamilyName[] list));
        return new FontRequest(list, size, weight, style, 14, stretch, synthesis, features);
    }

    public static FontFaceRule Rule(string family, string src, params (string Name, string Value)[] more)
    {
        var d = new List<KeyValuePair<string, string>> { new("font-family", family), new("src", src) };
        foreach ((string n, string v) in more)
        {
            d.Add(new(n, v));
        }

        return FontFaceRule.Create(d, "https://example.test/css/site.css")!;
    }

    public static string FamilyOf(ResolvedFace face) => face.Face.FamilyName;
}
