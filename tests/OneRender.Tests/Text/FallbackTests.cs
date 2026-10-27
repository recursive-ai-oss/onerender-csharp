using OneRender.Style;
using System.Linq;
using OneFont;
using OneRender.Text;

namespace OneRender.Tests.Text;

// Per-character fallback (CSS Fonts 4 §5.3, §5.4) and the missing-glyph diagnostics.
public class FallbackTests
{
    private static string[] Families(ShapedText run) => run.Segments.ToArray().Select(s => s.Face.Face.FamilyName).ToArray();

    private static string[] Slices(ShapedText run) =>
        run.Segments.ToArray().Select(s => run.Text.Slice(s.TextStart, s.TextLength).ToString()).ToArray();

    [Fact]
    public void MixedScriptsFallThroughTheFamilyListThenTheDefaults()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        // Carlito has Latin and Greek but no Hebrew; Liberation Sans (the default sans) has all three.
        ShapedText run = shaper.Shape("abc \u03B1\u03B2 \u05D0\u05D1 def", TestFonts.Request("Carlito"));
        Assert.Equal(["Carlito", "Liberation Sans", "Carlito"], Families(run));
        Assert.Equal(["abc \u03B1\u03B2 ", "\u05D0\u05D1", " def"], Slices(run));
        Assert.Equal(0, run.MissingGlyphCount);
        Assert.Null(run.FirstMissingGlyph);
    }

    [Fact]
    public void NamedFamiliesComeBeforeTheDefaultFaces()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        // Caladea has no Greek: the next listed family (Carlito) takes it, not the default sans.
        ShapedText run = shaper.Shape("a\u03B1", TestFonts.Request("Caladea, Carlito"));
        Assert.Equal(["Caladea", "Carlito"], Families(run));
    }

    [Fact]
    public void ClustersStayTogetherWhenOneFaceSupportsThemWhole()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        // Carlito has "e" but not the combining acute; Liberation Sans has both, so the whole cluster goes there (§5.3 step 1).
        ShapedText run = shaper.Shape("xe\u0301y", TestFonts.Request("Carlito"));
        Assert.Equal(["Carlito", "Liberation Sans", "Carlito"], Families(run));
        Assert.Equal(["x", "e\u0301", "y"], Slices(run));
    }

    [Fact]
    public void AMarkNoFaceSupportsWithItsBaseIsSplitOff()
    {
        // Two faces, each lacking the other's character: the base stays with the face that has it, the mark goes alone.
        var c = new FontCatalog();
        var resolver = new MapResolver().Add("s.ttf", TestFonts.Liberation("LiberationSans-Regular.ttf"));
        c.Resolver = resolver;
        c.AddFontFace(TestFonts.Rule("A", "url(s.ttf)", ("unicode-range", "U+0-2FF")));
        c.AddFontFace(TestFonts.Rule("B", "url(s.ttf)", ("unicode-range", "U+300-36F")));
        ShapedText run = new TextShaper(c).Shape("e\u0301", TestFonts.Request("A, B"));
        Assert.Equal(["e", "\u0301"], Slices(run));
        Assert.Equal(["A", "B"], run.Segments.ToArray().Select(s => s.Face.FamilyName).ToArray());
    }

    [Fact]
    public void InvisibleCharactersFollowTheFaceBeforeThem()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        ShapedText run = shaper.Shape("a\u05D0\n\u200Db\t", TestFonts.Request("Carlito"));
        Assert.Equal(["a", "\u05D0\n\u200D", "b\t"], Slices(run));
        Assert.Equal(0, run.MissingGlyphCount);
    }

    [Fact]
    public void EmojiProducesADiagnosticNamingTheEmojiPackage()
    {
        FontCatalog c = TestFonts.Defaults();
        var shaper = new TextShaper(c);
        ShapedText run = shaper.Shape("a\U0001F600b", TestFonts.Request("sans-serif"));
        Assert.Equal(1, run.MissingGlyphCount);
        TextDiagnostic d = run.FirstMissingGlyph!.Value;
        Assert.Equal(TextDiagnosticKind.MissingGlyph, d.Kind);
        Assert.Equal(0x1F600, d.CodePoint);
        Assert.Equal("RecursiveAI.OneRender.Fonts.Emoji", d.Package);
        Assert.Contains("RecursiveAI.OneRender.Fonts.Emoji", d.Message, StringComparison.Ordinal);
        Assert.Contains("U+1F600", d.Message, StringComparison.Ordinal);
        Assert.Contains(d, c.Diagnostics.Items);

        // The cluster still occupies its text range and draws the missing-glyph shape (glyph 0) with the first available face.
        GlyphCluster[] clusters = run.Clusters.ToArray();
        Assert.Equal(3, clusters.Length);
        Assert.True(clusters[1].HasMissingGlyph);
        Assert.Equal((1, 2), (clusters[1].TextStart, clusters[1].TextLength));
        Assert.Equal(0, run.GlyphIds[clusters[1].GlyphStart]);
        Assert.Equal("Liberation Sans", Families(run)[0]);
    }

    [Theory]
    [InlineData("\u0645\u0631\u062D\u0628\u0627", 0x0645, "RecursiveAI.OneRender.Fonts.Noto.Arabic")]
    [InlineData("\u4E2D\u6587", 0x4E2D, "RecursiveAI.OneRender.Fonts.Cjk")]
    [InlineData("\u3053\u3093\u306B\u3061\u306F", 0x3053, "RecursiveAI.OneRender.Fonts.Cjk")]
    [InlineData("\uD55C\uAD6D", 0xD55C, "RecursiveAI.OneRender.Fonts.Cjk")]
    [InlineData("\u0928\u092E\u0938\u094D\u0924\u0947", 0x0928, "RecursiveAI.OneRender.Fonts.Noto.Devanagari")]
    [InlineData("\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35", 0x0E2A, "RecursiveAI.OneRender.Fonts.Noto.Thai")]
    [InlineData("\u0995", 0x0995, "RecursiveAI.OneRender.Fonts.Noto.Bengali")]
    [InlineData("\u2603", 0x2603, "RecursiveAI.OneRender.Fonts.Emoji")]
    [InlineData("\u0F40", 0x0F40, "RecursiveAI.OneRender.Fonts.Noto.Tibetan")]
    public void MissingScriptsNameTheirPackage(string text, int first, string package)
    {
        FontCatalog c = TestFonts.Defaults();
        ShapedText run = new TextShaper(c).Shape(text, TestFonts.Request("serif"));
        Assert.True(run.MissingGlyphCount > 0);
        Assert.Equal(first, run.FirstMissingGlyph!.Value.CodePoint);
        Assert.Equal(package, run.FirstMissingGlyph.Value.Package);
    }

    [Fact]
    public void ACharacterNoPackageCoversHasANullPackage()
    {
        FontCatalog c = TestFonts.Defaults();
        ShapedText run = new TextShaper(c).Shape("\uE000", TestFonts.Request("serif")); // private use
        Assert.Equal(1, run.MissingGlyphCount);
        Assert.Null(run.FirstMissingGlyph!.Value.Package);
        Assert.Contains("No optional fonts package", run.FirstMissingGlyph.Value.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsAreRecordedOnceAndBounded()
    {
        FontCatalog c = TestFonts.Defaults();
        var shaper = new TextShaper(c);
        for (int i = 0; i < 5; i++)
        {
            shaper.ShapeInto(new ShapedText(), "\u4E2D\u4E2D\u4E2D", TestFonts.Request("serif"));
        }

        Assert.Single(c.Diagnostics.Items.Where(d => d.Kind == TextDiagnosticKind.MissingGlyph).ToArray());

        // Thousands of distinct missing characters cannot grow the list without bound.
        var sb = new System.Text.StringBuilder();
        for (int cp = 0x4E00; cp < 0x4E00 + 2000; cp++)
        {
            sb.Append((char)cp);
        }

        shaper.ShapeInto(new ShapedText(), sb.ToString(), TestFonts.Request("serif"));
        Assert.True(c.Diagnostics.Items.Count <= TextDiagnostics.Capacity);
        Assert.True(c.Diagnostics.Dropped > 0);
    }

    [Fact]
    public void AnEmptyCatalogReportsNoFontsInsteadOfThrowing()
    {
        var c = new FontCatalog();
        ShapedText run = new TextShaper(c).Shape("hello", TestFonts.Request("serif"));
        Assert.Equal(0, run.GlyphCount);
        Assert.Contains(c.Diagnostics.Items, d => d.Kind == TextDiagnosticKind.NoFonts);
    }

    [Fact]
    public void PrivateUseCharactersIgnoreGenericAndDefaultFaces()
    {
        // §5.4: a private-use character is matched only against named families of the list.
        var c = new FontCatalog(aliases: new DefaultFontAliases());
        c.AddFont(OneFont.FontFace.Load(TestFonts.Liberation("LiberationSans-Regular.ttf")), "Liberation Sans", 400, FontStyleKind.Normal);
        ShapedText run = new TextShaper(c).Shape("\uE000", TestFonts.Request("sans-serif"));
        Assert.Equal(1, run.MissingGlyphCount);
    }

    [Fact]
    public void ProviderFontsSupplyCharactersNothingElseHas()
    {
        var system = new FakeSystemFonts();
        system.Faces.Add(new SystemFontFace("Fallback Arabic", 400, FontStyleKind.Normal, 100, null, null, () => TestFonts.Data("NotoSansArabic-Subset-Regular.ttf")));
        FontCatalog c = TestFonts.Defaults(null, system);
        ShapedText run = new TextShaper(c).Shape("a \u0627\u0644", TestFonts.Request("serif"));
        Assert.Equal(0, run.MissingGlyphCount);
        ShapedTextSegment[] seg = run.Segments.ToArray();
        Assert.Equal(2, seg.Length);
        Assert.Equal("Liberation Serif", seg[0].Face.Face.FamilyName);
        Assert.StartsWith("Noto Sans Arabic", seg[1].Face.Face.FamilyName, StringComparison.Ordinal);

        // Without the provider the same text reports Arabic as missing, naming the package.
        FontCatalog plain = TestFonts.Defaults();
        ShapedText missing = new TextShaper(plain).Shape("a \u0627\u0644", TestFonts.Request("serif"));
        Assert.Equal("RecursiveAI.OneRender.Fonts.Noto.Arabic", missing.FirstMissingGlyph!.Value.Package);
    }

    [Theory]
    [InlineData(0x41, null)]
    [InlineData(0x5D0, "RecursiveAI.OneRender.Fonts.Noto.Hebrew")]
    [InlineData(0x627, "RecursiveAI.OneRender.Fonts.Noto.Arabic")]
    [InlineData(0xFE8D, "RecursiveAI.OneRender.Fonts.Noto.Arabic")]
    [InlineData(0x10D0, "RecursiveAI.OneRender.Fonts.Noto.Georgian")]
    [InlineData(0x1100, "RecursiveAI.OneRender.Fonts.Cjk")]
    [InlineData(0x20000, "RecursiveAI.OneRender.Fonts.Cjk")]
    [InlineData(0x1F9D1, "RecursiveAI.OneRender.Fonts.Emoji")]
    [InlineData(0x2211, "RecursiveAI.OneRender.Fonts.Noto.Math")]
    [InlineData(0xE000, null)]
    [InlineData(0x10FFFF, null)]
    public void PackageTable(int codePoint, string? package) => Assert.Equal(package, FontPackages.ForCodePoint(codePoint));
}
