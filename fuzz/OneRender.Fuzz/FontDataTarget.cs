using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OneRender.Style;
using OneFont;
using OneRender.Text;

namespace OneRender.Fuzz;

/// <summary>
/// Hostile font files and <c>@font-face</c> descriptors through the text service: the loader (TrueType, OpenType, WOFF, WOFF2),
/// the descriptor and font-property parsers, matching, fallback and shaping.
///
/// Input layout: byte 0 selects the mode (low two bits): 0 the rest is a font file; 1 a u16 length, then descriptor text
/// (name, value, name, value ... separated by NUL bytes), then a font file served for every URL; 2 CSS text for the property parsers
/// (families, weight, stretch, feature settings, split by NUL); 3 a data: URL source built from the rest. Bits 2-3 pick the text
/// that is shaped, bit 4 right to left, bit 5 a language.
/// Nothing may escape the library but an <see cref="ArgumentException"/> for text longer than the shaper's limit; any other exception
/// or a broken invariant is a finding.
/// </summary>
internal static class FontDataTarget
{
    private static readonly string[] Texts =
    [
        "Hello, world! fi AVA",
        "\u05D0\u05D1\u05D2 abc \u0627\u0644\u0633\u0644\u0627\u0645",
        "e\u0301 \U0001F600 \u4E2D\u6587 \uE000 \u0928\u092E\u0938\u094D\u0924\u0947 \u200D\u200C",
        "\uD800 lone \uDC00 surrogates \u0000 and controls \u0007\n\t",
    ];

    private sealed class ServeAll(byte[] data) : IFontResourceResolver
    {
        public byte[]? LoadFont(string url, string? baseUrl) => url.Length > 0 && url[0] == '!' ? null : data;
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        int flags = data[0];
        ReadOnlySpan<byte> rest = data[1..];
        string text = Texts[(flags >> 2) & 3];
        bool rtl = (flags & 0x10) != 0;
        string? language = (flags & 0x20) != 0 ? "ar" : null;
        var catalog = new FontCatalog(limits: new FontLimits { MaxInputBytes = 1 << 20, MaxDecodedBytes = 4 << 20 });
        catalog.AddFont(Seed.Value, "Seed", 400, FontStyleKind.Normal);

        switch (flags & 3)
        {
            case 0:
                FontLoadResult r = FontLoader.Load(rest.ToArray(), catalog.Limits);
                Check(r.Face is null == (r.Status != FontLoadStatus.Ok), "face and status disagree");
                if (r.Face is not null)
                {
                    catalog.AddFont(r.Face, "Fuzz", 400, FontStyleKind.Normal);
                    ShapeWith(catalog, "Fuzz, Seed", text, rtl, language);
                }

                break;
            case 1:
                {
                    if (rest.Length < 2)
                    {
                        return;
                    }

                    int length = Math.Min((rest[0] << 8) | rest[1], rest.Length - 2);
                    string descriptors = Encoding.UTF8.GetString(rest.Slice(2, length));
                    byte[] font = rest[(2 + length)..].ToArray();
                    catalog.Resolver = new ServeAll(font);
                    string[] parts = descriptors.Split('\0');
                    var list = new List<KeyValuePair<string, string>>();
                    for (int i = 0; i + 1 < parts.Length && list.Count < 64; i += 2)
                    {
                        list.Add(new KeyValuePair<string, string>(parts[i], parts[i + 1]));
                    }

                    FontFaceRule? rule = FontFaceRule.Create(list, "https://fuzz.test/a.css", catalog.Diagnostics);
                    if (rule is not null)
                    {
                        catalog.AddFontFace(rule);
                        ShapeWith(catalog, "\"" + rule.Family.Replace("\"", string.Empty, StringComparison.Ordinal) + "\", Seed", text, rtl, language);
                    }

                    break;
                }

            case 2:
                {
                    string[] parts = Encoding.UTF8.GetString(rest).Split('\0');
                    string P(int i) => i < parts.Length ? parts[i] : string.Empty;
                    if (FontCss.TryParseFamilyList(P(0), out FontFamilyName[] families))
                    {
                        Check(families.Length > 0 && families.Length <= FontRequest.MaxFamilies, "family list size");
                    }

                    _ = FontCss.TryParseWeight(P(1), out _);
                    _ = FontCss.TryParseStretch(P(2), out _);
                    FontFeatureSettings features = FontFeatureSettings.FromCss(P(3), P(4), P(5), P(6), P(7), P(8), P(9));
                    var req = new FontRequest(families.Length > 0 ? families : [new FontFamilyName("Seed")], 14, features: features, language: language);
                    Shape(catalog, req, text, rtl);
                    break;
                }

            default:
                {
                    string url = "data:font/woff2;base64," + Convert.ToBase64String(rest);
                    if (rest.Length < 64)
                    {
                        url = "data:," + Encoding.Latin1.GetString(rest);
                    }

                    FontFaceRule? rule = FontFaceRule.Create([new("font-family", "D"), new("src", "url(\"" + url.Replace("\"", "%22", StringComparison.Ordinal).Replace("\\", "%5C", StringComparison.Ordinal) + "\")")], null, catalog.Diagnostics);
                    if (rule is not null)
                    {
                        catalog.AddFontFace(rule);
                        ShapeWith(catalog, "D, Seed", text, rtl, language);
                    }

                    break;
                }
        }
    }

    private static readonly Lazy<byte[]> Seed = new(() =>
    {
        string dir = Environment.GetEnvironmentVariable("ONERENDER_FUZZ_FONTS") ?? Path.Combine(AppContext.BaseDirectory, "fonts");
        return File.ReadAllBytes(Path.Combine(dir, "NotoSansHebrew-Subset-Regular.ttf"));
    });

    private static void ShapeWith(FontCatalog catalog, string families, string text, bool rtl, string? language)
    {
        _ = FontCss.TryParseFamilyList(families, out FontFamilyName[] list);
        Shape(catalog, new FontRequest(list, 16, 700, FontStyleKind.Italic, language: language), text, rtl);
    }

    private static void Shape(FontCatalog catalog, FontRequest request, string text, bool rtl)
    {
        var shaper = new TextShaper(catalog, 64);
        var run = new ShapedText();
        shaper.ShapeInto(run, text, request, rtl);
        CheckRun(run, text);
        ShapedText cached = shaper.Shape(text, request, rtl);
        Check(cached.GlyphCount == run.GlyphCount, "cached and fresh runs differ");
        FontMetricsInfo m = catalog.Resolve(request).GetMetrics(16);
        Check(!float.IsNaN(m.Ascent) && !float.IsNaN(m.Descent), "metrics are NaN");
    }

    // The clusters must tile the text exactly once, the segments tile the glyphs, and positions must be finite.
    private static void CheckRun(ShapedText run, string text)
    {
        Check(run.Text.SequenceEqual(text), "run text differs");
        if (run.GlyphCount == 0 && run.Clusters.Length == 0)
        {
            return;
        }

        var seen = new bool[text.Length];
        int glyphs = 0;
        foreach (GlyphCluster c in run.Clusters)
        {
            Check(c.TextStart >= 0 && c.TextLength >= 0 && c.TextStart + c.TextLength <= text.Length, "cluster outside the text");
            for (int i = c.TextStart; i < c.TextStart + c.TextLength; i++)
            {
                Check(!seen[i], "clusters overlap");
                seen[i] = true;
            }

            Check(c.GlyphStart >= 0 && c.GlyphStart + c.GlyphCount <= run.GlyphCount, "cluster glyphs outside the run");
            glyphs += c.GlyphCount;
        }

        Check(seen.All(b => b), "clusters leave a gap");
        Check(glyphs == run.GlyphCount, "glyphs not covered once");
        int g = 0;
        foreach (ShapedTextSegment s in run.Segments)
        {
            Check(s.GlyphStart == g, "segments do not tile the glyphs");
            g += s.GlyphCount;
        }

        Check(g == run.GlyphCount, "segments miss glyphs");
        foreach (float v in run.X)
        {
            Check(float.IsFinite(v), "position is not finite");
        }

        Check(float.IsFinite(run.Width), "width is not finite");
    }

    private static void Check(bool ok, string what)
    {
        if (!ok)
        {
            throw new InvalidOperationException("Invariant broken: " + what);
        }
    }

    public static int WriteSeeds(string outDir, string fontDir)
    {
        Directory.CreateDirectory(outDir);
        int n = 0;

        void Put(string name, params byte[][] parts)
        {
            File.WriteAllBytes(Path.Combine(outDir, name), parts.SelectMany(p => p).ToArray());
            n++;
        }

        foreach (string f in Directory.EnumerateFiles(fontDir).Where(x => x.EndsWith(".woff", StringComparison.Ordinal) || x.EndsWith(".woff2", StringComparison.Ordinal) || x.EndsWith("Hebrew-Subset-Regular.ttf", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal))
        {
            byte[] font = File.ReadAllBytes(f);
            string stem = Path.GetFileName(f);
            Put("font-" + stem + ".bin", [0x00], font);
            Put("font-rtl-" + stem + ".bin", [0x14], font);

            byte[] desc = Encoding.UTF8.GetBytes("font-family\0Face\0src\0url(f." + (stem.EndsWith(".woff2", StringComparison.Ordinal) ? "woff2) format(woff2)" : "ttf)") + "\0font-weight\0300 700\0unicode-range\0U+0-5FF,U+4??");
            Put("rule-" + stem + ".bin", [0x01, (byte)(desc.Length >> 8), (byte)desc.Length], desc, font);
            Put("data-" + stem + ".bin", [0x03], font);
        }

        Put("css-a.bin", [0x02], Encoding.UTF8.GetBytes("\"Face\", serif, 'x'\0bold\0condensed\0none\0common-ligatures\0small-caps\0oldstyle-nums\0full-width\0super\0\"liga\" 0, \"smcp\", \"ss01\" 2"));
        Put("css-b.bin", [0x06], Encoding.UTF8.GetBytes("a, b c, \"d\"\0700\0125%\0\0\0\0\0\0\0\"kern\" off"));
        Put("rule-text.bin", [0x01, 0x00, 0x60], Encoding.UTF8.GetBytes("font-family\0F\0src\0local(\"X\"), url(a.ttf) tech(variations), url(\"b\")\0font-style\0oblique 5deg 20deg\0size-adjust\0120%"));
        return n == 0 ? 1 : 0;
    }
}
