using System.IO;
using OneRender.Style;
using System.Linq;
using System.Text;
using OneFont;
using OneRender.Text;

namespace OneRender.Tests.Text;

// @font-face descriptors, src fallback, unicode-range, local() and the font loader's formats.
public class FontFaceTests
{
    private static readonly byte[] Sans = TestFonts.Liberation("LiberationSans-Regular.ttf");
    private static readonly byte[] Serif = TestFonts.Liberation("LiberationSerif-Regular.ttf");

    private static FontFaceRule Parse(params (string Name, string Value)[] d) =>
        FontFaceRule.Create(d.Select(x => new KeyValuePair<string, string>(x.Name, x.Value)), null)!;

    [Fact]
    public void DescriptorsParse()
    {
        FontFaceRule r = Parse(
            ("font-family", "\"My Face\""),
            ("src", "url(a.woff2) format(\"woff2\") tech(variations), local(Foo Bar), url('b.ttf')"),
            ("font-weight", "300 700"),
            ("font-style", "oblique 10deg 20deg"),
            ("font-stretch", "75% 125%"),
            ("unicode-range", "U+0025-00FF, u+4??, U+1F600"),
            ("font-display", "swap"),
            ("size-adjust", "110%"),
            ("ascent-override", "90%"),
            ("descent-override", "normal"));
        Assert.Equal("My Face", r.Family);
        Assert.Equal(3, r.Sources.Count);
        Assert.Equal((FontSourceKind.Url, "a.woff2", "woff2"), (r.Sources[0].Kind, r.Sources[0].Value, r.Sources[0].Format));
        Assert.Equal(["variations"], r.Sources[0].Tech);
        Assert.Equal((FontSourceKind.Local, "Foo Bar"), (r.Sources[1].Kind, r.Sources[1].Value));
        Assert.Equal("b.ttf", r.Sources[2].Value);
        Assert.Equal((300f, 700f), (r.WeightMin, r.WeightMax));
        Assert.Equal(FontStyleKind.Oblique, r.Style);
        Assert.Equal((10f, 20f), (r.ObliqueMin, r.ObliqueMax));
        Assert.Equal((75f, 125f), (r.StretchMin, r.StretchMax));
        Assert.Equal([new CodePointRange(0x25, 0xFF), new CodePointRange(0x400, 0x4FF), new CodePointRange(0x1F600, 0x1F600)], r.UnicodeRanges);
        Assert.Equal(FontDisplay.Swap, r.Display);
        Assert.Equal(1.1f, r.SizeAdjust, 3);
        Assert.Equal(0.9f, r.AscentOverride, 3);
        Assert.True(float.IsNaN(r.DescentOverride));
    }

    [Fact]
    public void DefaultsMatchTheInitialValues()
    {
        FontFaceRule r = Parse(("font-family", "F"), ("src", "url(f.ttf)"));
        Assert.Equal((400f, 400f, 100f, 100f), (r.WeightMin, r.WeightMax, r.StretchMin, r.StretchMax));
        Assert.Equal(FontStyleKind.Normal, r.Style);
        Assert.Empty(r.UnicodeRanges);
        Assert.Equal(FontDisplay.Auto, r.Display);
        Assert.Equal(1f, r.SizeAdjust);
    }

    [Fact]
    public void ReversedRangesAreSwappedAndKeywordsAccepted()
    {
        FontFaceRule r = Parse(("font-family", "F"), ("src", "url(f.ttf)"), ("font-weight", "900 100"), ("font-stretch", "condensed"), ("font-style", "italic"));
        Assert.Equal((100f, 900f), (r.WeightMin, r.WeightMax));
        Assert.Equal((75f, 75f), (r.StretchMin, r.StretchMax));
        Assert.Equal(FontStyleKind.Italic, r.Style);
        Assert.Equal((200f, 200f), (Parse(("font-family", "F"), ("src", "url(f)"), ("font-width", "ultra-expanded")).StretchMin, 200f));
    }

    [Theory]
    [InlineData("font-weight", "bolder")]
    [InlineData("font-weight", "0")]
    [InlineData("font-weight", "1001")]
    [InlineData("font-weight", "100 200 300")]
    [InlineData("font-style", "oblique 91deg")]
    [InlineData("font-style", "slanted")]
    [InlineData("font-stretch", "-5%")]
    [InlineData("font-stretch", "wide")]
    [InlineData("unicode-range", "U+110000")]
    [InlineData("unicode-range", "U+5-1")]
    [InlineData("unicode-range", "U+4?5")]
    [InlineData("unicode-range", "U+1234567")]
    [InlineData("unicode-range", "U+0025,")]
    [InlineData("unicode-range", "12")]
    [InlineData("font-display", "later")]
    [InlineData("size-adjust", "-1%")]
    [InlineData("size-adjust", "1")]
    public void InvalidDescriptorsAreIgnoredIndividually(string name, string value)
    {
        var diagnostics = new TextDiagnostics();
        FontFaceRule? r = FontFaceRule.Create([new("font-family", "F"), new("src", "url(f.ttf)"), new(name, value)], null, diagnostics);
        Assert.NotNull(r);
        Assert.Contains(diagnostics.Items, d => d.Kind == TextDiagnosticKind.InvalidDescriptor && d.Subject == name);
        Assert.Equal((400f, 400f, 100f, 100f), (r!.WeightMin, r.WeightMax, r.StretchMin, r.StretchMax));
        Assert.Empty(r.UnicodeRanges);
        Assert.Equal(1f, r.SizeAdjust);
    }

    [Theory]
    [InlineData("serif")]
    [InlineData("inherit")]
    [InlineData("")]
    [InlineData("a, b")]
    public void BadFamilyDropsTheRule(string family)
    {
        var diagnostics = new TextDiagnostics();
        Assert.Null(FontFaceRule.Create([new("font-family", family), new("src", "url(f.ttf)")], null, diagnostics));
        Assert.Contains(diagnostics.Items, d => d.Kind == TextDiagnosticKind.InvalidFontFaceRule);
    }

    [Theory]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("url(")]
    [InlineData("format(woff)")]
    [InlineData("local()")]
    public void RuleWithoutUsableSourceIsDropped(string src) =>
        Assert.Null(FontFaceRule.Create([new("font-family", "F"), new("src", src)]));

    [Fact]
    public void BadSourceEntriesAreSkippedAlone()
    {
        FontFaceRule r = Parse(("font-family", "F"), ("src", "bogus, url(a.ttf) format(woff2) format(woff), url(b.ttf), local(\"X\")"));
        Assert.Equal(["b.ttf", "X"], r.Sources.Select(s => s.Value).ToArray());
    }

    [Fact]
    public void UnicodeRangeWildcardsExpand()
    {
        FontFaceRule r = Parse(("font-family", "F"), ("src", "url(f)"), ("unicode-range", "U+???, U+0??, U+10FFFF, U+?????"));
        Assert.Equal(new CodePointRange(0, 0xFFF), r.UnicodeRanges[0]);
        Assert.Equal(new CodePointRange(0, 0xFF), r.UnicodeRanges[1]);
        Assert.Equal(new CodePointRange(0x10FFFF, 0x10FFFF), r.UnicodeRanges[2]);
        Assert.Equal(new CodePointRange(0, 0xFFFFF), r.UnicodeRanges[3]);
    }

    [Fact]
    public void FamilyListParsing()
    {
        Assert.True(FontCss.TryParseFamilyList("  \"Helvetica Neue\" , Arial,  sans-serif , Times  New   Roman", out FontFamilyName[] f));
        Assert.Equal(["Helvetica Neue", "Arial", "sans-serif", "Times New Roman"], f.Select(x => x.Name).ToArray());
        Assert.Equal([GenericFamily.None, GenericFamily.None, GenericFamily.SansSerif, GenericFamily.None], f.Select(x => x.Generic).ToArray());
        Assert.True(FontCss.TryParseFamilyList("'serif', SYSTEM-UI", out f));
        Assert.Equal([GenericFamily.None, GenericFamily.SystemUi], f.Select(x => x.Generic).ToArray()); // a quoted keyword is a name
        Assert.False(FontCss.TryParseFamilyList("a,,b", out _));
        Assert.False(FontCss.TryParseFamilyList("", out _));
        Assert.False(FontCss.TryParseFamilyList("inherit", out _));
        Assert.False(FontCss.TryParseFamilyList("\"unterminated", out _));
        Assert.False(FontCss.TryParseFamilyList("a 12", out _));
        Assert.Equal("café", ((FontCss.TryParseFamilyList("caf\\E9", out f) ? f : [])[0]).Name); // CSS escape
    }

    [Fact]
    public void OneHtmlRulesAdapt()
    {
        var sheet = RecursiveAI.OneHtml.Css.Stylesheet.Parse("@font-face { font-family: Foo; src: url(foo.woff2) format('woff2'); font-weight: 600 }", "https://x.test/a.css");
        RecursiveAI.OneHtml.Css.CssNode node = sheet.Rules.First();
        FontFaceRule? r = FontFaceRule.FromNode(node, sheet.Location);
        Assert.NotNull(r);
        Assert.Equal("Foo", r!.Family);
        Assert.Equal("foo.woff2", r.Sources[0].Value);
        Assert.Equal(600f, r.WeightMin);
        Assert.Equal("https://x.test/a.css", r.BaseUrl);
    }

    // ---- loading ---------------------------------------------------------------------------------------------------

    [Fact]
    public void SrcEntriesAreTriedInOrderAndLoadingIsLazy()
    {
        var resolver = new MapResolver().Add("bad.ttf", [1, 2, 3, 4, 5, 6, 7, 8]).Add("good.ttf", Sans).Add("later.ttf", Serif);
        FontCatalog c = TestFonts.Defaults(resolver);
        c.AddFontFace(TestFonts.Rule("W", "url(gone.woff2) format(\"woff2\"), url(bad.ttf), url(good.ttf), url(later.ttf)"));
        Assert.Empty(resolver.Requests); // nothing is read until text needs it

        var shaper = new TextShaper(c);
        ShapedText run = shaper.Shape("abc", TestFonts.Request("W"));
        Assert.Equal(["gone.woff2", "bad.ttf", "good.ttf"], resolver.Requests.Select(r => r.Url).ToArray());
        Assert.All(resolver.Requests, r => Assert.Equal("https://example.test/css/site.css", r.Base));
        Assert.Equal("W", run.Segments[0].Face.FamilyName);
        Assert.Equal("Liberation Sans", run.Segments[0].Face.Face.FamilyName);
        Assert.True(run.Segments[0].Face.IsWebFont);

        Assert.Contains(c.Diagnostics.Items, d => d.Kind == TextDiagnosticKind.FontResourceMissing && d.Subject == "gone.woff2");
        Assert.Contains(c.Diagnostics.Items, d => d.Kind == TextDiagnosticKind.FontLoadFailed && d.Subject == "bad.ttf");

        // Shaping again does not read the files again.
        resolver.Requests.Clear();
        shaper.Shape("abd", TestFonts.Request("W"));
        Assert.Empty(resolver.Requests);
    }

    [Fact]
    public void UnicodeRangeSelectsTheFacePerCharacterAndLoadsOnlyWhatIsUsed()
    {
        var resolver = new MapResolver().Add("latin.ttf", Sans).Add("hebrew.woff2", TestFonts.Data("NotoSansHebrew-Subset-Regular.woff2"));
        var c = new FontCatalog(resolver, new DefaultFontAliases());
        c.AddFont(FontFace.Load(Serif), "Liberation Serif", 400, FontStyleKind.Normal);
        c.AddFontFace(TestFonts.Rule("Split", "url(latin.ttf)", ("unicode-range", "U+0-24F")));
        c.AddFontFace(TestFonts.Rule("Split", "url(hebrew.woff2) format(woff2)", ("unicode-range", "U+590-5FF")));
        var shaper = new TextShaper(c);

        ShapedText latinOnly = shaper.Shape("abc", TestFonts.Request("Split"));
        Assert.Equal(["latin.ttf"], resolver.Requests.Select(r => r.Url).ToArray());
        Assert.Single(latinOnly.Segments.ToArray());

        ShapedText mixed = shaper.Shape("ab \u05D0\u05D1 cd", TestFonts.Request("Split"));
        ShapedTextSegment[] seg = mixed.Segments.ToArray();
        Assert.Equal(["Liberation Sans", "Noto Sans Hebrew", "Liberation Sans"], seg.Select(s => s.Face.Face.FamilyName).ToArray());
        Assert.Equal([0, 3, 5], seg.Select(s => s.TextStart).ToArray());
        Assert.Equal(["latin.ttf", "hebrew.woff2"], resolver.Requests.Select(r => r.Url).ToArray());
        Assert.Equal(0, mixed.MissingGlyphCount);
    }

    [Fact]
    public void LaterFaceWinsWhereRangesOverlap()
    {
        var resolver = new MapResolver().Add("a.ttf", Sans).Add("b.ttf", Serif);
        FontCatalog c = TestFonts.Defaults(resolver);
        c.AddFontFace(TestFonts.Rule("O", "url(a.ttf)"));
        c.AddFontFace(TestFonts.Rule("O", "url(b.ttf)", ("unicode-range", "U+61-7A")));
        var shaper = new TextShaper(c);
        ShapedText run = shaper.Shape("aB", TestFonts.Request("O"));
        Assert.Equal(["Liberation Serif", "Liberation Sans"], run.Segments.ToArray().Select(s => s.Face.Face.FamilyName).ToArray());
    }

    [Fact]
    public void LocalSourceNeedsAProvider()
    {
        var resolver = new MapResolver().Add("f.ttf", Serif);
        FontCatalog c = TestFonts.Defaults(resolver);
        c.AddFontFace(TestFonts.Rule("L", "local(\"Liberation Sans\"), url(f.ttf)"));
        ShapedText run = new TextShaper(c).Shape("x", TestFonts.Request("L"));
        // No provider: local() is skipped, even though the packaged font has that name; the url is used.
        Assert.Equal("Liberation Serif", run.Segments[0].Face.Face.FamilyName);
        Assert.Contains(c.Diagnostics.Items, d => d.Kind == TextDiagnosticKind.SourceSkipped && d.Subject == "Liberation Sans");
    }

    [Fact]
    public void LocalSourceUsesTheProviderWhenGiven()
    {
        var system = new FakeSystemFonts();
        system.Faces.Add(new SystemFontFace("Host Sans", 400, FontStyleKind.Normal, 100, "Host Sans Regular", "HostSans-Regular", () => Serif));
        FontCatalog c = TestFonts.Defaults(null, system);
        c.AddFontFace(TestFonts.Rule("L", "local(\"Host Sans Regular\")"));
        ShapedText run = new TextShaper(c).Shape("x", TestFonts.Request("L"));
        Assert.Equal("Liberation Serif", run.Segments[0].Face.Face.FamilyName);

        // The provider's families are also reachable by name, only when a provider is set.
        Assert.Equal("Host Sans", new TextShaper(c).Shape("x", TestFonts.Request("Host Sans")).Segments[0].Face.FamilyName);
        FontCatalog without = TestFonts.Defaults();
        Assert.Equal("Liberation Sans", new TextShaper(without).Shape("x", TestFonts.Request("Host Sans")).Segments[0].Face.FamilyName);
    }

    [Fact]
    public void MissingResourceIsADiagnosticAndTextStillShapes()
    {
        FontCatalog c = TestFonts.Defaults(new MapResolver());
        c.AddFontFace(TestFonts.Rule("Gone", "url(nope.woff2)"));
        ShapedText run = new TextShaper(c).Shape("hello", TestFonts.Request("Gone, serif"));
        Assert.Equal(5, run.GlyphCount);
        Assert.Equal("Liberation Serif", run.Segments[0].Face.Face.FamilyName);
        TextDiagnostic d = c.Diagnostics.Items.First(x => x.Kind == TextDiagnosticKind.FontResourceMissing);
        Assert.Equal("nope.woff2", d.Subject);
        Assert.Contains("nope.woff2", d.Message, StringComparison.Ordinal);

        // With no resolver at all the diagnostic says so.
        var bare = new FontCatalog();
        bare.AddFont(FontFace.Load(Sans), "Liberation Sans", 400, FontStyleKind.Normal);
        bare.AddFontFace(TestFonts.Rule("Gone", "url(nope.woff2)"));
        new TextShaper(bare).Shape("x", TestFonts.Request("Gone"));
        Assert.Contains(bare.Diagnostics.Items, x => x.Detail == "No resource resolver is set.");
    }

    [Fact]
    public void UnsupportedFormatAndTechnologySourcesAreSkipped()
    {
        var resolver = new MapResolver().Add("a.eot", Sans).Add("b.ttf", Serif).Add("c.ttf", Sans);
        FontCatalog c = TestFonts.Defaults(resolver);
        c.AddFontFace(TestFonts.Rule("U", "url(a.eot) format(\"embedded-opentype\"), url(b.ttf) tech(color-colrv1), url(c.ttf) format(\"truetype\") tech(features-opentype)"));
        ShapedText run = new TextShaper(c).Shape("x", TestFonts.Request("U"));
        Assert.Equal(["c.ttf"], resolver.Requests.Select(r => r.Url).ToArray());
        Assert.Equal("Liberation Sans", run.Segments[0].Face.Face.FamilyName);
    }

    [Fact]
    public void DataUrlsNeedNoResolver()
    {
        string b64 = Convert.ToBase64String(TestFonts.Data("NotoSansHebrew-Subset-Regular.woff2"));
        var c = new FontCatalog();
        c.AddFont(FontFace.Load(Sans), "Liberation Sans", 400, FontStyleKind.Normal);
        c.AddFontFace(TestFonts.Rule("D", "url(\"data:font/woff2;base64," + b64 + "\") format(woff2)"));
        ShapedText run = new TextShaper(c).Shape("\u05D0", TestFonts.Request("D"));
        Assert.Equal("Noto Sans Hebrew", run.Segments[0].Face.Face.FamilyName);
        Assert.Equal(0, run.MissingGlyphCount);
    }

    [Fact]
    public void FontDisplayIsRecorded()
    {
        var resolver = new MapResolver().Add("f.ttf", Sans);
        FontCatalog c = TestFonts.Defaults(resolver);
        c.AddFontFace(TestFonts.Rule("Fd", "url(f.ttf)", ("font-display", "optional")));
        Assert.Equal(FontDisplay.Optional, new TextShaper(c).Shape("x", TestFonts.Request("Fd")).Segments[0].Face.Display);
    }

    [Fact]
    public void SizeAdjustAndOverridesApplyToMetricsAndAdvances()
    {
        var resolver = new MapResolver().Add("f.ttf", Sans);
        FontCatalog c = TestFonts.Defaults(resolver);
        c.AddFontFace(TestFonts.Rule("Adj", "url(f.ttf)", ("size-adjust", "50%"), ("ascent-override", "100%"), ("descent-override", "20%"), ("line-gap-override", "0%")));
        var shaper = new TextShaper(c);
        ShapedText adj = shaper.Shape("Hello", TestFonts.Request("Adj", 20));
        ShapedText plain = shaper.Shape("Hello", TestFonts.Request("Liberation Sans", 20));
        Assert.Equal(plain.Width / 2, adj.Width, 3);
        FontMetricsInfo m = adj.Segments[0].Face.GetMetrics(20);
        Assert.Equal(20f, m.Ascent, 3);
        Assert.Equal(4f, m.Descent, 3);
        Assert.Equal(0f, m.LineGap, 3);
    }
}

// The loader's formats and its refusal of bad data.
public class FontLoaderTests
{
    private static readonly byte[] Ttf = TestFonts.Data("NotoSansHebrew-Subset-Regular.ttf");
    private static readonly byte[] Woff = TestFonts.Data("NotoSansHebrew-Subset-Regular.woff");
    private static readonly byte[] Woff2 = TestFonts.Data("NotoSansHebrew-Subset-Regular.woff2");

    private sealed class Recorder : IOutlineSink
    {
        public readonly StringBuilder Text = new();

        public void MoveTo(float x, float y) => Text.Append('M').Append(x).Append(',').Append(y);

        public void LineTo(float x, float y) => Text.Append('L').Append(x).Append(',').Append(y);

        public void QuadTo(float cx, float cy, float x, float y) => Text.Append('Q').Append(cx).Append(',').Append(cy).Append(',').Append(x).Append(',').Append(y);

        public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y) => Text.Append('C').Append(c1x).Append(',').Append(c1y).Append(',').Append(c2x).Append(',').Append(c2y).Append(',').Append(x).Append(',').Append(y);

        public void Close() => Text.Append('Z');
    }

    private static void AssertSameFont(FontFace expected, FontFace actual)
    {
        Assert.Equal(expected.GlyphCount, actual.GlyphCount);
        Assert.Equal(expected.UnitsPerEm, actual.UnitsPerEm);
        Assert.Equal(expected.FamilyName, actual.FamilyName);
        Assert.Equal(expected.Metrics.Ascender, actual.Metrics.Ascender);
        for (int g = 0; g < expected.GlyphCount; g++)
        {
            Assert.Equal(expected.GetAdvance(g), actual.GetAdvance(g));
            Assert.Equal(expected.GetLeftSideBearing(g), actual.GetLeftSideBearing(g));
            var a = new Recorder();
            var b = new Recorder();
            expected.GetOutline(g).Write(a);
            actual.GetOutline(g).Write(b);
            Assert.Equal(a.Text.ToString(), b.Text.ToString());
        }

        for (int cp = 0x20; cp < 0x700; cp++)
        {
            Assert.Equal(expected.GetGlyphIndex(cp), actual.GetGlyphIndex(cp));
        }
    }

    [Fact]
    public void PlainSfntLoads()
    {
        FontLoadResult r = FontLoader.Load(Ttf);
        Assert.Equal(FontLoadStatus.Ok, r.Status);
        Assert.NotNull(r.Face);
    }

    [Fact]
    public void WoffDecodesToTheSameFont()
    {
        FontLoadResult r = FontLoader.Load(Woff);
        Assert.Equal(FontLoadStatus.Ok, r.Status);
        AssertSameFont(FontFace.Load(Ttf), r.Face!);
    }

    [Fact]
    public void Woff2DecodesToTheSameFontWithTransformedGlyfAndHmtx()
    {
        Assert.True(Woff2.Length < Ttf.Length / 2);
        FontLoadResult r = FontLoader.Load(Woff2);
        Assert.True(r.Status == FontLoadStatus.Ok, r.Detail);
        AssertSameFont(FontFace.Load(Ttf), r.Face!);
    }

    [Fact]
    public void EmptyAndUnknownDataAreRefused()
    {
        Assert.Equal(FontLoadStatus.Empty, FontLoader.Load(null).Status);
        Assert.Equal(FontLoadStatus.Empty, FontLoader.Load([]).Status);
        Assert.Equal(FontLoadStatus.UnsupportedFormat, FontLoader.Load(Encoding.ASCII.GetBytes("<html>not a font</html>")).Status);
        byte[] ttc = new byte[64];
        Encoding.ASCII.GetBytes("ttcf").CopyTo(ttc, 0);
        Assert.Equal(FontLoadStatus.UnsupportedFormat, FontLoader.Load(ttc).Status);
    }

    [Fact]
    public void InputLargerThanTheLimitIsRefusedBeforeParsing()
    {
        var limits = new FontLimits { MaxInputBytes = 1000 };
        Assert.Equal(FontLoadStatus.InputTooLarge, FontLoader.Load(Ttf, limits).Status);
    }

    [Fact]
    public void CorruptZlibDataInAWoffTableFailsAsAStatus()
    {
        // Found by the font-data fuzz target: a WOFF whose compressed table made the inflater throw an IOException.
        // The first byte is the fuzz target's mode byte; the font follows.
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "testdata", "fuzz", "woff-inflate-error.bin"));
        FontLoadResult r = FontLoader.Load(input.AsSpan(1).ToArray());
        Assert.NotEqual(FontLoadStatus.Ok, r.Status);
        Assert.Null(r.Face);
    }

    [Fact]
    public void ACorruptCmapMarksTheFaceFailedInsteadOfThrowing()
    {
        // Found by the font-data fuzz target: the font loads, then its cmap throws on the first lookup. The first byte is the
        // fuzz target's mode byte; the font follows.
        byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "testdata", "fuzz", "damaged-cmap.bin"));
        FontCatalog catalog = TestFonts.Defaults();
        string url = "data:font/ttf;base64," + Convert.ToBase64String(input.AsSpan(1));
        FontFaceRule rule = FontFaceRule.Create([new("font-family", "D"), new("src", "url(\"" + url + "\")")], null, catalog.Diagnostics)!;
        Assert.True(catalog.AddFontFace(rule));
        var shaper = new TextShaper(catalog);
        ShapedText run = shaper.Shape("Hello", TestFonts.Request("D, Liberation Sans"));
        Assert.True(run.GlyphCount > 0);
        Assert.Contains(catalog.Diagnostics.Items, d => d.Kind == TextDiagnosticKind.FontLoadFailed);
    }

    [Fact]
    public void TruncatedFilesFailCleanly()
    {
        foreach (byte[] file in new[] { Ttf, Woff, Woff2 })
        {
            for (int len = 0; len < file.Length; len += Math.Max(1, file.Length / 97))
            {
                FontLoadResult r = FontLoader.Load(file.AsSpan(0, len).ToArray());
                if (r.Status == FontLoadStatus.Ok)
                {
                    Assert.NotNull(r.Face);
                }
                else
                {
                    Assert.Null(r.Face);
                    Assert.NotNull(r.Detail);
                }
            }
        }
    }

    [Fact]
    public void CorruptedWebFontsFailCleanly()
    {
        // Deterministic byte flips over both containers: only a status may come back, never an exception.
        var rng = new Random(12345);
        foreach (byte[] file in new[] { Woff, Woff2 })
        {
            for (int i = 0; i < 400; i++)
            {
                byte[] copy = (byte[])file.Clone();
                int flips = 1 + rng.Next(4);
                for (int k = 0; k < flips; k++)
                {
                    copy[rng.Next(copy.Length)] = (byte)rng.Next(256);
                }

                FontLoadResult r = FontLoader.Load(copy);
                if (r.Face is not null)
                {
                    // A font that loads must also be usable: shaping must not throw anything but a typed error.
                    try
                    {
                        TextShaping.ShapeRun("\u05D0\u05D1", r.Face);
                    }
                    catch (FontFormatException)
                    {
                    }
                }
            }
        }
    }

    [Fact]
    public void WoffLengthsAreCheckedBeforeAllocating()
    {
        // A WOFF whose table claims 4 GiB of output: refused for size, without trying to allocate it.
        byte[] w = (byte[])Woff.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(w.AsSpan(44 + 12), 0xFFFFFFF0);
        FontLoadResult r = FontLoader.Load(w);
        Assert.NotEqual(FontLoadStatus.Ok, r.Status);

        var tight = new FontLimits { MaxDecodedBytes = 4096 };
        Assert.Equal(FontLoadStatus.LimitExceeded, FontLoader.Load(Woff, tight).Status);
        Assert.Equal(FontLoadStatus.LimitExceeded, FontLoader.Load(Woff2, tight).Status);
        Assert.Equal(FontLoadStatus.LimitExceeded, FontLoader.Load(Woff, new FontLimits { MaxTables = 3 }).Status);
        Assert.Equal(FontLoadStatus.LimitExceeded, FontLoader.Load(Woff2, new FontLimits { MaxTables = 3 }).Status);
    }

    [Fact]
    public void Woff2TableLengthsAreCheckedBeforeAllocating()
    {
        byte[] crafted = new byte[48 + 16 + 8];
        System.Text.Encoding.ASCII.GetBytes("wOF2").CopyTo(crafted, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crafted.AsSpan(4), 0x00010000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crafted.AsSpan(8), (uint)crafted.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(crafted.AsSpan(12), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crafted.AsSpan(20), 8);
        // flags: known tag 'cmap' (0), version 0; origLength = 0xFFFFFFFF as UIntBase128 (FF FF FF FF 0F).
        byte[] dir = [0x00, 0x8F, 0xFF, 0xFF, 0xFF, 0x7F];
        dir.CopyTo(crafted, 48);
        FontLoadResult r = FontLoader.Load(crafted);
        Assert.Equal(FontLoadStatus.LimitExceeded, r.Status);

        // The same bytes with a malformed UIntBase128 (leading 0x80) are an invalid container.
        crafted[49] = 0x80;
        Assert.Equal(FontLoadStatus.InvalidContainer, FontLoader.Load(crafted).Status);
    }
}
