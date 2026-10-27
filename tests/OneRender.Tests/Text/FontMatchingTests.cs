using OneRender.TestSupport;
using OneRender.Style;
using System.Linq;
using OneFont;
using OneRender.Fonts;
using OneRender.Text;

namespace OneRender.Tests.Text;

// CSS Fonts 4 §5.2 font matching: the order in which width, style and weight narrow a family, synthesis, and the generic and alias names.
public class FontMatchingTests
{
    private static readonly byte[] Sans = TestFonts.Liberation("LiberationSans-Regular.ttf");

    // A family of one font file registered at several descriptor values, so only the descriptors differ.
    private static FontCatalog Family(string name, params (float Weight, FontStyleKind Style, float Stretch)[] faces)
    {
        var catalog = new FontCatalog(aliases: new DefaultFontAliases());
        FontFace face = FontFace.Load(Sans);
        foreach ((float w, FontStyleKind s, float st) in faces)
        {
            catalog.AddFont(face, name, w, s, st);
        }

        catalog.AddFont(FontFace.Load(Sans), "Liberation Sans", 400, FontStyleKind.Normal);
        return catalog;
    }

    private static ResolvedFace Pick(FontCatalog c, FontRequest r) => c.Resolve(r).PrimaryFace!;

    [Theory]
    [InlineData(400, 300)] // 400-500: weights above up to 500 first, then below descending
    [InlineData(500, 300)]
    [InlineData(450, 300)]
    [InlineData(700, 800)] // above 500: ascending from the target
    [InlineData(650, 800)]
    [InlineData(1000, 800)] // nothing above: descending
    [InlineData(200, 100)] // below 400: descending from the target
    [InlineData(250, 100)]
    [InlineData(50, 100)] // nothing at or below: ascending
    [InlineData(600, 600)] // exact
    public void WeightRules(int desired, int expected)
    {
        FontCatalog c = Family("W", (100, FontStyleKind.Normal, 100), (300, FontStyleKind.Normal, 100), (600, FontStyleKind.Normal, 100), (800, FontStyleKind.Normal, 100));
        // 400 is absent, so 300 is next below; 500 falls to 300 too, and 450 as well.
        ResolvedFace f = Pick(c, TestFonts.Request("W", weight: desired));
        Assert.Equal(expected, (int)f.WeightRange.Min);
    }

    [Fact]
    public void WeightBetween400And500PrefersUpToFiveHundredBeforeGoingLighter()
    {
        FontCatalog c = Family("W", (300, FontStyleKind.Normal, 100), (500, FontStyleKind.Normal, 100), (900, FontStyleKind.Normal, 100));
        Assert.Equal(500, (int)Pick(c, TestFonts.Request("W", weight: 400)).WeightRange.Min);
        c = Family("W", (300, FontStyleKind.Normal, 100), (900, FontStyleKind.Normal, 100));
        Assert.Equal(300, (int)Pick(c, TestFonts.Request("W", weight: 400)).WeightRange.Min);
    }

    [Fact]
    public void WeightRangesContainTheDesiredValue()
    {
        var c = new FontCatalog();
        FontFace face = FontFace.Load(Sans);
        c.AddFontFace(TestFonts.Rule("R", "url(a.ttf)", ("font-weight", "100 400")));
        c.AddFontFace(TestFonts.Rule("R", "url(b.ttf)", ("font-weight", "500 900")));
        c.Resolver = new MapResolver().Add("a.ttf", Sans).Add("b.ttf", Sans);
        Assert.Equal(500, (int)Pick(c, TestFonts.Request("R", weight: 700)).WeightRange.Min);
        Assert.Equal(100, (int)Pick(c, TestFonts.Request("R", weight: 300)).WeightRange.Min);
        _ = face;
    }

    [Theory]
    [InlineData(90, 75)] // at or below 100%: narrower first, descending
    [InlineData(60, 50)]
    [InlineData(30, 50)] // nothing narrower: wider ascending
    [InlineData(110, 125)] // above 100%: wider first, ascending
    [InlineData(200, 125)] // nothing wider: narrower descending
    [InlineData(100, 100)]
    public void StretchRules(int desired, int expectedStretch)
    {
        var c = new FontCatalog();
        FontFace face = FontFace.Load(Sans);
        foreach (float s in new[] { 50f, 75f, 100f, 125f })
        {
            c.AddFont(face, "S", 400, FontStyleKind.Normal, s);
        }

        // The tie between faces is broken by the stretch alone, so identify the pick through the face id order.
        ResolvedFace f = Pick(c, TestFonts.Request("S", stretch: desired));
        float[] order = [50, 75, 100, 125];
        Assert.Equal(expectedStretch, (int)order[f.FaceId]);
    }

    [Fact]
    public void StretchIsNarrowedBeforeWeightAndStyle()
    {
        // The narrower face has the wrong weight, but width is tried first (§5.2).
        var c = new FontCatalog();
        FontFace face = FontFace.Load(Sans);
        c.AddFont(face, "S", 300, FontStyleKind.Normal, 75); // id 0
        c.AddFont(face, "S", 700, FontStyleKind.Normal, 100); // id 1
        Assert.Equal(0, Pick(c, TestFonts.Request("S", weight: 700, stretch: 75)).FaceId);
    }

    [Fact]
    public void ItalicPrefersItalicThenObliqueThenUpright()
    {
        var c = new FontCatalog();
        FontFace face = FontFace.Load(Sans);
        c.AddFont(face, "I", 400, FontStyleKind.Normal); // 0
        c.AddFont(face, "I", 400, FontStyleKind.Italic); // 1
        Assert.Equal(1, Pick(c, TestFonts.Request("I", style: FontStyleKind.Italic)).FaceId);
        Assert.Equal(0, Pick(c, TestFonts.Request("I")).FaceId);

        // Without a real italic, an oblique face stands in; without either, the upright face is slanted.
        c = new FontCatalog();
        c.Resolver = new MapResolver().Add("o.ttf", Sans).Add("n.ttf", Sans);
        c.AddFontFace(TestFonts.Rule("I", "url(n.ttf)"));
        c.AddFontFace(TestFonts.Rule("I", "url(o.ttf)", ("font-style", "oblique 10deg")));
        ResolvedFace pick = Pick(c, TestFonts.Request("I", style: FontStyleKind.Italic));
        Assert.Equal(1, pick.FaceId);
        Assert.False(pick.SyntheticItalic);
    }

    [Fact]
    public void ObliqueRequestUsesTheNearestAngleAsFontsFourSays()
    {
        FontCatalog Make(params string[] styles)
        {
            var c = new FontCatalog { Resolver = new MapResolver().Add("f.ttf", Sans) };
            foreach (string s in styles)
            {
                c.AddFontFace(s == "normal" ? TestFonts.Rule("O", "url(f.ttf)") : TestFonts.Rule("O", "url(f.ttf)", ("font-style", s)));
            }

            return c;
        }

        // oblique 20deg (>= 11): angles above ascending, then below descending down to 0.
        var r20 = new FontRequest([new FontFamilyName("O")], 16, 400, FontStyleKind.Oblique, 20);
        Assert.Equal(1, Pick(Make("normal", "oblique 30deg", "oblique 5deg"), r20).FaceId);
        Assert.Equal(1, Pick(Make("normal", "oblique 5deg"), r20).FaceId);

        // oblique 8deg (< 11): angles below descending first, then above.
        var r8 = new FontRequest([new FontFamilyName("O")], 16, 400, FontStyleKind.Oblique, 8);
        Assert.Equal(2, Pick(Make("normal", "oblique 30deg", "oblique 5deg"), r8).FaceId);
        Assert.Equal(1, Pick(Make("normal", "oblique 30deg"), r8).FaceId);

        // A range that contains the angle matches directly.
        Assert.Equal(1, Pick(Make("normal", "oblique 10deg 40deg"), r20).FaceId);
    }

    [Fact]
    public void SyntheticBoldAndItalicAreRecordedForPaint()
    {
        var c = new FontCatalog();
        c.AddFont(FontFace.Load(Sans), "Solo", 400, FontStyleKind.Normal);
        ResolvedFace plain = Pick(c, TestFonts.Request("Solo"));
        Assert.False(plain.SyntheticBold);
        Assert.False(plain.SyntheticItalic);

        ResolvedFace bold = Pick(c, TestFonts.Request("Solo", weight: 700));
        Assert.True(bold.SyntheticBold);
        Assert.False(bold.SyntheticItalic);

        ResolvedFace italic = Pick(c, TestFonts.Request("Solo", style: FontStyleKind.Italic));
        Assert.True(italic.SyntheticItalic);
        Assert.Equal(14, italic.SyntheticSlant);

        var oblique = new FontRequest([new FontFamilyName("Solo")], 16, 400, FontStyleKind.Oblique, 20);
        Assert.Equal(20, Pick(c, oblique).SyntheticSlant);

        // font-synthesis can forbid either.
        Assert.False(Pick(c, TestFonts.Request("Solo", weight: 700, synthesis: FontSynthesis.Style)).SyntheticBold);
        Assert.False(Pick(c, TestFonts.Request("Solo", style: FontStyleKind.Italic, synthesis: FontSynthesis.Weight)).SyntheticItalic);
        Assert.False(Pick(c, TestFonts.Request("Solo", weight: 700, style: FontStyleKind.Italic, synthesis: FontSynthesis.None)).SyntheticBold);

        // A real bold face is not faked.
        FontCatalog defaults = TestFonts.Defaults();
        Assert.False(Pick(defaults, TestFonts.Request("Liberation Sans", weight: 700)).SyntheticBold);
        Assert.False(Pick(defaults, TestFonts.Request("Liberation Sans", style: FontStyleKind.Italic)).SyntheticItalic);
    }

    [Fact]
    public void PackagedFamiliesPickTheirBoldAndItalicFiles()
    {
        FontCatalog c = TestFonts.Defaults();
        Assert.Equal("Liberation Sans", TestFonts.FamilyOf(Pick(c, TestFonts.Request("Liberation Sans"))));
        ResolvedFace bi = Pick(c, TestFonts.Request("Carlito", weight: 700, style: FontStyleKind.Italic));
        Assert.True(bi.Face.IsBold);
        Assert.True(bi.Face.IsItalic);
        Assert.Equal("Carlito", bi.Face.FamilyName);
    }

    [Theory]
    [InlineData("sans-serif", "Liberation Sans")]
    [InlineData("system-ui", "Liberation Sans")]
    [InlineData("ui-sans-serif", "Liberation Sans")]
    [InlineData("serif", "Liberation Serif")]
    [InlineData("ui-serif", "Liberation Serif")]
    [InlineData("monospace", "Liberation Mono")]
    [InlineData("ui-monospace", "Liberation Mono")]
    [InlineData("cursive", "Liberation Serif")]
    [InlineData("Arial", "Liberation Sans")]
    [InlineData("ARIAL", "Liberation Sans")]
    [InlineData("Helvetica", "Liberation Sans")]
    [InlineData("'Times New Roman'", "Liberation Serif")]
    [InlineData("\"Courier New\"", "Liberation Mono")]
    [InlineData("Calibri", "Carlito")]
    [InlineData("Cambria", "Caladea")]
    [InlineData("liberation mono", "Liberation Mono")]
    [InlineData("No Such Family, serif", "Liberation Serif")]
    [InlineData("No Such Family", "Liberation Sans")]
    public void GenericAndAliasNamesMapThroughTheFontsPackage(string families, string expected)
    {
        FontCatalog c = TestFonts.Defaults();
        Assert.Equal(expected, TestFonts.FamilyOf(Pick(c, TestFonts.Request(families))));
    }

    [Fact]
    public void WithoutAliasesGenericsFallBackToTheFirstRegisteredFamily()
    {
        var c = new FontCatalog();
        c.AddFont(FontFace.Load(Sans), "Only", 400, FontStyleKind.Normal);
        Assert.Equal("Liberation Sans", TestFonts.FamilyOf(Pick(c, TestFonts.Request("monospace"))));
        c.SetGenericFamily(GenericFamily.Monospace, "Only");
        Assert.Equal("Only", Pick(c, TestFonts.Request("monospace")).FamilyName);
    }

    [Fact]
    public void FontFaceFamiliesShadowRegisteredOnesOfTheSameName()
    {
        // §5.2: a family defined by @font-face is never replaced by an installed font of the same name, even when its source fails.
        FontCatalog c = TestFonts.Defaults();
        c.AddFontFace(TestFonts.Rule("Arial", "url(missing.ttf)"));
        ResolvedFontList list = c.Resolve(TestFonts.Request("Arial"));
        // The failed face is skipped when text needs it; the default faces still draw.
        ResolvedFace? f = list.PrimaryFace;
        Assert.NotNull(f);
        Assert.Equal("Liberation Sans", f!.Face.FamilyName); // reached through the default faces, not through "Arial"
        Assert.Contains(c.Diagnostics.Items, d => d.Kind == TextDiagnosticKind.FontResourceMissing);
    }

    [Fact]
    public void ResolveReturnsTheSameListForEqualRequestsWithoutAllocating()
    {
        FontCatalog c = TestFonts.Defaults();
        FontRequest a = TestFonts.Request("Arial, sans-serif", 12);
        FontRequest b = TestFonts.Request("arial, sans-serif", 30);
        ResolvedFontList la = c.Resolve(a);
        Assert.Same(la, c.Resolve(b)); // size and name case do not matter
        Assert.NotSame(la, c.Resolve(TestFonts.Request("Arial, sans-serif", weight: 700)));
        Allocation.AssertZero(() => c.Resolve(b), "Resolve of a known request");
    }

    [Fact]
    public void CatalogRefusesBeyondItsFaceLimit()
    {
        var c = new FontCatalog();
        FontFace face = FontFace.Load(Sans);
        for (int i = 0; i < FontCatalog.MaxFaces; i++)
        {
            Assert.True(c.AddFont(face, "F" + (i % 7), 400, FontStyleKind.Normal));
        }

        Assert.False(c.AddFont(face, "Extra", 400, FontStyleKind.Normal));
        Assert.Equal(FontCatalog.MaxFaces, c.FaceCount);
    }

    [Fact]
    public void RequestsValidateTheirArguments()
    {
        FontFamilyName[] none = [];
        Assert.Throws<ArgumentException>(() => new FontRequest(none, 0));
        Assert.Throws<ArgumentException>(() => new FontRequest(none, float.NaN));
        Assert.Throws<ArgumentException>(() => new FontRequest(none, 10, weight: 0));
        Assert.Throws<ArgumentException>(() => new FontRequest(none, 10, weight: 1001));
        Assert.Throws<ArgumentException>(() => new FontRequest(none, 10, stretch: 0));
        Assert.Throws<ArgumentException>(() => new FontRequest(new FontFamilyName[FontRequest.MaxFamilies + 1], 10));
    }
}
