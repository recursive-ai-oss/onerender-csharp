using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using OneRender.Fonts;
using OneRender.Fonts.Cjk;
using OneRender.Fonts.Emoji;
using OneRender.Image;
using OneRender.Style;
using OneRender.Text;

namespace OneRender.FontPackageTests;

public class OptionalFontTests
{
    private const string CjkId = "RecursiveAI.OneRender.Fonts.Cjk";
    private const string EmojiId = "RecursiveAI.OneRender.Fonts.Emoji";

    public OptionalFontTests() => AllPackages.RegisterAll();

    public static TheoryData<string> ScriptIds()
    {
        var d = new TheoryData<string>();
        foreach ((string id, _, _) in AllPackages.Scripts)
        {
            d.Add(id);
        }

        return d;
    }

    public static TheoryData<string> EveryPackageId()
    {
        TheoryData<string> d = ScriptIds();
        d.Add(CjkId);
        d.Add(EmojiId);
        return d;
    }

    private static OptionalFontPackage Package(string id) =>
        id == CjkId ? CjkFonts.Package : id == EmojiId ? EmojiFonts.Package : AllPackages.Scripts.First(s => s.Id == id).Package;

    private static string Hash(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    private static byte[] ReadAll(Stream s)
    {
        using Stream x = s;
        using var ms = new MemoryStream();
        x.CopyTo(ms);
        return ms.ToArray();
    }

    // The default fonts alone: what an application sees with no optional package referenced.
    private sealed class DefaultAliases : IFontFamilyAliases
    {
        public bool TryResolve(ReadOnlySpan<char> name, [NotNullWhen(true)] out string? family) => DefaultFonts.TryResolveFamily(name, out family);
    }

    private static TextFontProvider DefaultsOnly()
    {
        var catalog = new FontCatalog(null, new DefaultAliases());
        foreach (FontFaceInfo info in DefaultFonts.Faces)
        {
            catalog.AddFont(info.ReadAllBytes(), info.Family, info.Weight, info.Slant == FontSlant.Italic ? FontStyleKind.Italic : FontStyleKind.Normal, info.Stretch);
        }

        return new TextFontProvider(catalog);
    }

    private static RgbaImage Render(TextFontProvider fonts, string text, string style = "", int width = 400, int height = 120) =>
        HtmlRender.ToImage(
            "<html><body style=\"margin:8px;font:32px sans-serif;" + style + "\"><p style=\"margin:0\">" + System.Net.WebUtility.HtmlEncode(text) + "</p></body></html>",
            new HtmlRenderOptions { Width = width, Height = height, Fonts = fonts });

    private static int Ink(RgbaImage img)
    {
        int n = 0;
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            if (img.Pixels[i] < 250 || img.Pixels[i + 1] < 250 || img.Pixels[i + 2] < 250)
            {
                n++;
            }
        }

        return n;
    }

    private static bool Missing(TextFontProvider fonts, out string?[] packages)
    {
        packages = fonts.Catalog.Diagnostics.Items.Where(d => d.Kind == TextDiagnosticKind.MissingGlyph).Select(d => d.Package).Distinct().ToArray();
        return packages.Length > 0;
    }

    [Theory]
    [MemberData(nameof(EveryPackageId))]
    public void EmbeddedBytesMatchTheManifest(string id)
    {
        OptionalFontPackage package = Package(id);
        Assert.Equal(id, package.PackageId);
        Dictionary<string, string> hashes = ReadManifest(package);

        Assert.NotEmpty(package.Faces);
        foreach (FontFaceInfo face in package.Faces)
        {
            byte[] bytes = face.ReadAllBytes();
            Assert.Equal(face.Length, bytes.Length);
            Assert.Equal(hashes[face.FileName], Hash(bytes));
        }

        byte[] licence = ReadAll(package.OpenLicense());
        Assert.Equal(hashes["OFL.txt"], Hash(licence));
        string text = Encoding.UTF8.GetString(licence);
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Copyright", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryPackageId))]
    public void FacesMatchWhatTheFontsDeclare(string id)
    {
        foreach (FontFaceInfo info in Package(id).Faces)
        {
            OneFont.FontFace face = OneFont.FontFace.Load(info.ReadAllBytes(), info.FaceIndex);
            Assert.True(face.GlyphCount > 100, info.FileName);
            Assert.Equal(info.Family, face.FamilyName.Replace("Symbols2", "Symbols 2", StringComparison.Ordinal));
            Assert.Equal(info.Weight == 700, face.Weight >= 600);
            Assert.StartsWith("fonts/", "fonts/" + info.FileName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PackageNamesMatchTheMissingGlyphDiagnosticTable()
    {
        var named = new SortedSet<string>(StringComparer.Ordinal);
        for (int cp = 0; cp <= 0x3FFFF; cp++)
        {
            if (FontPackages.ForCodePoint(cp) is { } p)
            {
                named.Add(p);
            }
        }

        var shipped = new SortedSet<string>(AllPackages.Scripts.Select(s => s.Id).Append(CjkId).Append(EmojiId), StringComparer.Ordinal);
        Assert.Equal(shipped, named);
    }

    [Theory]
    [MemberData(nameof(ScriptIds))]
    public void ScriptSampleNamesItsPackageWhenAbsentAndRendersWhenPresent(string id)
    {
        string sample = AllPackages.Scripts.First(s => s.Id == id).Sample;

        TextFontProvider without = DefaultsOnly();
        RgbaImage before = Render(without, sample);
        Assert.True(Missing(without, out string?[] named), "expected a missing glyph for " + id);
        Assert.Contains(id, named);

        TextFontProvider with = FontProviders.CreateDefault();
        RgbaImage after = Render(with, sample);
        Assert.False(Missing(with, out string?[] still), id + " still missing: " + string.Join(",", still));
        Assert.True(Ink(after) > 100, "no glyphs drawn for " + id);
        Assert.NotEqual(before.Pixels, after.Pixels);
    }

    [Fact]
    public void ArabicShapesIntoJoinedForms()
    {
        // Isolated and joined letters differ, so the same letters spelled with a break between them look different.
        TextFontProvider fonts = FontProviders.CreateDefault();
        RgbaImage joined = Render(fonts, "\u0628\u0628\u0628");
        RgbaImage apart = Render(fonts, "\u0628\u200C\u0628\u200C\u0628");
        Assert.NotEqual(joined.Pixels, apart.Pixels);
    }

    [Fact]
    public void ChineseJapaneseAndKoreanSamplesShape()
    {
        Assert.Equal(10, CjkFonts.Package.Faces.Count);
        string[] samples = ["\u6C49\u5B57\u7B80\u4F53\u4E2D\u6587", "\u3072\u3089\u304C\u306A\u30AB\u30BF\u30AB\u30CA\u65E5\u672C\u8A9E", "\uD55C\uAD6D\uC5B4 \uC870\uC120\uAE00"];
        foreach (string text in samples)
        {
            TextFontProvider without = DefaultsOnly();
            Render(without, text);
            Assert.True(Missing(without, out string?[] named));
            Assert.Contains(CjkId, named);

            TextFontProvider with = FontProviders.CreateDefault();
            RgbaImage img = Render(with, text);
            Assert.False(Missing(with, out string?[] still), string.Join(",", still));
            Assert.True(Ink(img) > 500);
        }
    }

    [Fact]
    public void RegionFacesGiveRegionalGlyphForms()
    {
        // U+9AA8 is drawn differently in the Japanese and Simplified Chinese faces of the same collection.
        TextFontProvider fonts = FontProviders.CreateDefault();
        RgbaImage jp = Render(fonts, "\u9AA8", "font-family:'Noto Sans CJK JP'");
        RgbaImage sc = Render(fonts, "\u9AA8", "font-family:'Noto Sans CJK SC'");
        RgbaImage kr = Render(fonts, "\u9AA8", "font-family:'Noto Sans CJK KR'");
        Assert.True(Ink(jp) > 200 && Ink(sc) > 200);
        Assert.NotEqual(jp.Pixels, sc.Pixels);
        Assert.NotEqual(sc.Pixels, kr.Pixels);
    }

    [Fact]
    public void ColorEmojiRendersWithColorPixels()
    {
        TextFontProvider without = DefaultsOnly();
        Render(without, "\U0001F600");
        Assert.True(Missing(without, out string?[] named));
        Assert.Contains(EmojiId, named);

        TextFontProvider with = FontProviders.CreateDefault();
        foreach (string emoji in new[] { "\U0001F600", "\U0001F680\U0001F34E", "\U0001F469\u200D\U0001F680", "\U0001F1E9\U0001F1EA" })
        {
            RgbaImage img = Render(with, emoji);
            Assert.False(Missing(with, out string?[] still), emoji + " " + string.Join(",", still));
            int colored = 0;
            for (int i = 0; i < img.Pixels.Length; i += 4)
            {
                int max = Math.Max(img.Pixels[i], Math.Max(img.Pixels[i + 1], img.Pixels[i + 2]));
                int min = Math.Min(img.Pixels[i], Math.Min(img.Pixels[i + 1], img.Pixels[i + 2]));
                if (max - min > 60)
                {
                    colored++;
                }
            }

            Assert.True(colored > 300, emoji + ": colored pixels " + colored);
        }
    }

    [Fact]
    public void RegistrationIsIdempotentAndOrderIndependent()
    {
        int version = OptionalFonts.Version;
        AllPackages.RegisterAll();
        Assert.Equal(version, OptionalFonts.Version);
        IReadOnlyList<OptionalFontPackage> all = OptionalFonts.Packages;
        Assert.Equal(all.OrderBy(p => p.Priority).ThenBy(p => p.PackageId, StringComparer.Ordinal).Select(p => p.PackageId), all.Select(p => p.PackageId));
        Assert.Equal(EmojiId, all[^1].PackageId);
    }

    private static Dictionary<string, string> ReadManifest(OptionalFontPackage package)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in Encoding.UTF8.GetString(ReadAll(package.OpenManifest())).Split('\n'))
        {
            if (line.StartsWith("file\t", StringComparison.Ordinal))
            {
                string[] c = line.Split('\t');
                d[c[1]] = c[4];
            }
        }

        return d;
    }
}
