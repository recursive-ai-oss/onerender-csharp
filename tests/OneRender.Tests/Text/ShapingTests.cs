using OneRender.TestSupport;
using OneRender.Style;
using System.Linq;
using OneFont;
using OneRender.Text;

namespace OneRender.Tests.Text;

// Shaping goes through OneFont; these tests check that the service hands its output over unchanged (glyph ids, clusters, scaled
// advances), in display order, and that features and the cache behave. OneFont's own tests are the oracle for the glyph values.
public class ShapingTests
{
    private static readonly byte[] SansBytes = TestFonts.Liberation("LiberationSans-Regular.ttf");

    private static FontCatalog Bidi()
    {
        var c = new FontCatalog(aliases: new DefaultFontAliases());
        Assert.True(c.AddFont(TestFonts.Data("NotoSans-Bidi-Subset.ttf"), "Bidi", 400, FontStyleKind.Normal));
        return c;
    }

    // OneFont's own result for a run, flattened to glyph records in logical order.
    private static List<(int Glyph, int Advance, int XOff, int YOff, int Cluster)> Direct(string text, FontFace face, bool rtl, ShapingOptions? options = null)
    {
        var run = new ShapedRun();
        TextShaping.ShapeRun(text, face, run, rtl, options);
        var list = new List<(int, int, int, int, int)>();
        foreach (ShapedClusterRange c in run.Clusters)
        {
            foreach (ShapedGlyph g in run.GlyphsOf(c))
            {
                list.Add((g.GlyphId, g.XAdvance, g.XOffset, g.YOffset, c.Start));
            }
        }

        return list;
    }

    private static void AssertMatchesOneFont(ShapedText run, string text, FontFace face, float size, bool rtl = false, ShapingOptions? options = null)
    {
        var expected = Direct(text, face, rtl, options);
        if (rtl)
        {
            expected.Reverse(); // the run is in display order
        }

        Assert.Equal(expected.Count, run.GlyphCount);
        float scale = size / face.UnitsPerEm;
        float pen = 0;
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Glyph, run.GlyphIds[i]);
            Assert.Equal(expected[i].Advance * scale, run.Advances[i]);
            Assert.Equal(expected[i].Cluster, run.GlyphClusters[i]);
            Assert.Equal(pen + (expected[i].XOff * scale), run.X[i], 4);
            Assert.Equal(-expected[i].YOff * scale, run.Y[i], 4);
            pen += expected[i].Advance * scale;
        }

        Assert.Equal(pen, run.Width, 3);
    }

    [Fact]
    public void LatinMatchesOneFontIncludingKerning()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        FontFace face = FontFace.Load(SansBytes);
        const string text = "AVATAR To Wa. office";
        ShapedText run = shaper.Shape(text, TestFonts.Request("Liberation Sans", 16));
        AssertMatchesOneFont(run, text, face, 16);

        // Kerning is on by default: "AV" is tighter than the two nominal advances.
        ShapedText av = shaper.Shape("AV", TestFonts.Request("Liberation Sans", 100));
        float nominal = (face.GetAdvance(face.GetGlyphIndex('A')) + face.GetAdvance(face.GetGlyphIndex('V'))) * 100f / face.UnitsPerEm;
        Assert.True(av.Width < nominal - 1f, $"{av.Width} vs {nominal}");

        ShapedText off = shaper.Shape("AV", TestFonts.Request("Liberation Sans", 100, features: FontFeatureSettings.FromCss(kerning: "none")));
        Assert.Equal(nominal, off.Width, 2);
    }

    [Fact]
    public void LigaturesAreOnByDefaultAndSwitchOff()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        FontFace face = FontFace.Load(TestFonts.Liberation("LiberationSans-Regular.ttf"));
        // Carlito's liga feature joins f and i; Liberation Sans only has a discretionary one.
        FontFace carlito = FontFace.Load(DefaultFontsBytes("Carlito-Regular.ttf"));
        ShapedText on = shaper.Shape("fi", TestFonts.Request("Carlito"));
        Assert.Equal(1, on.GlyphCount);
        GlyphCluster c = on.Clusters[0];
        Assert.Equal((0, 2, 1), (c.TextStart, c.TextLength, c.GlyphCount));
        AssertMatchesOneFont(on, "fi", carlito, 16);

        Assert.Equal(2, shaper.Shape("fi", TestFonts.Request("Carlito", features: FontFeatureSettings.FromCss(ligatures: "none"))).GlyphCount);
        Assert.Equal(2, shaper.Shape("fi", TestFonts.Request("Carlito", features: FontFeatureSettings.FromCss(featureSettings: "\"liga\" 0, \"clig\" off"))).GlyphCount);
        Assert.Equal(1, shaper.Shape("fi", TestFonts.Request("Carlito", features: FontFeatureSettings.FromCss(ligatures: "no-common-ligatures", featureSettings: "\"liga\", \"clig\" 1"))).GlyphCount); // font-feature-settings applies after the variant properties

        // A feature outside the defaults, switched on: Carlito's old-style figures replace the lining ones.
        ShapedText lining = shaper.Shape("1234", TestFonts.Request("Carlito"));
        ShapedText oldstyle = shaper.Shape("1234", TestFonts.Request("Carlito", features: FontFeatureSettings.FromCss(numeric: "oldstyle-nums")));
        Assert.Equal(lining.GlyphCount, oldstyle.GlyphCount);
        Assert.False(lining.GlyphIds.SequenceEqual(oldstyle.GlyphIds));
        _ = face;
    }

    private static byte[] DefaultFontsBytes(string file) => TestFonts.Liberation(file);

    [Fact]
    public void FeatureCssParsing()
    {
        Assert.Same(FontFeatureSettings.Default, FontFeatureSettings.FromCss());
        Assert.Same(FontFeatureSettings.Default, FontFeatureSettings.FromCss("auto", "normal", "normal", "normal", "normal", "normal", "normal"));
        FontFeatureSettings f = FontFeatureSettings.FromCss(caps: "all-small-caps", numeric: "oldstyle-nums tabular-nums", position: "super", featureSettings: "\"ss01\" 2, \"kern\" off");
        Assert.Equal(["smcp=1", "c2sc=1", "onum=1", "tnum=1", "sups=1", "ss01=2", "kern=0"], f.Features.ToArray().Select(x => x.ToString()).ToArray());
        // Invalid values are ignored whole.
        Assert.Same(FontFeatureSettings.Default, FontFeatureSettings.FromCss(ligatures: "bogus", caps: "small-caps nonsense", featureSettings: "\"toolong\" 1"));
        Assert.Equal(KerningMode.None, FontFeatureSettings.FromCss(kerning: "none").Kerning);
        Assert.Equal(FontFeatureSettings.FromCss(caps: "small-caps"), FontFeatureSettings.FromCss(featureSettings: "\"smcp\""));
    }

    [Fact]
    public void ArabicShapesJoiningFormsAndLigaturesInDisplayOrder()
    {
        FontCatalog c = Bidi();
        var shaper = new TextShaper(c);
        FontFace face = c.Resolve(TestFonts.Request("Bidi")).PrimaryFace!.Face;
        const string text = "\u0627\u0644\u0633\u0644\u0627\u0645"; // al-salam: contains the lam-alef pair

        ShapedText run = shaper.Shape(text, TestFonts.Request("Bidi", 24), rightToLeft: true);
        AssertMatchesOneFont(run, text, face, 24, rtl: true);
        Assert.True(run.RightToLeft);
        Assert.Equal(0, run.MissingGlyphCount);

        // Joining happened: some glyph is not the nominal one for its character.
        int nominalMatches = 0;
        for (int i = 0; i < run.GlyphCount; i++)
        {
            if (run.GlyphIds[i] == face.GetGlyphIndex(text[run.GlyphClusters[i]]))
            {
                nominalMatches++;
            }
        }

        Assert.True(nominalMatches < run.GlyphCount);

        // Display order: clusters run from the end of the text to its start, glyph ranges ascend and tile the run.
        GlyphCluster[] clusters = run.Clusters.ToArray();
        Assert.Equal(text.Length, clusters.Sum(k => k.TextLength));
        for (int i = 1; i < clusters.Length; i++)
        {
            Assert.True(clusters[i].TextStart < clusters[i - 1].TextStart);
            Assert.Equal(clusters[i - 1].GlyphStart + clusters[i - 1].GlyphCount, clusters[i].GlyphStart);
        }

        // Positions increase left to right.
        for (int i = 1; i < run.GlyphCount; i++)
        {
            Assert.True(run.X[i] + 1e-3f >= run.X[i - 1] - 40f);
        }

        // The cluster map covers every glyph exactly once.
        Assert.Equal(run.GlyphCount, clusters.Sum(k => k.GlyphCount));
        Assert.Equal(run.Width, clusters.Sum(k => k.Advance), 3);
    }

    [Fact]
    public void LamAlefIsOneGlyphCoveringTwoCharacters()
    {
        FontCatalog c = Bidi();
        ShapedText run = new TextShaper(c).Shape("\u0644\u0627", TestFonts.Request("Bidi"), rightToLeft: true);
        Assert.Equal(1, run.GlyphCount);
        Assert.Equal((0, 2), (run.Clusters[0].TextStart, run.Clusters[0].TextLength));
    }

    [Fact]
    public void MixedDirectionParagraphShapesRunByRun()
    {
        FontCatalog c = Bidi();
        var shaper = new TextShaper(c);
        FontFace face = c.Resolve(TestFonts.Request("Bidi")).PrimaryFace!.Face;
        const string text = "abc \u0627\u0644\u0633\u0644\u0627\u0645 def 123";

        var runs = new List<BidiRun>();
        byte[]? levels = null;
        TextAnalysis.GetBidiRuns(text, TextDirection.LeftToRight, runs, ref levels, out int para);
        Assert.Equal(0, para);
        Assert.Equal(3, runs.Count);
        Assert.Equal([(0, 4, 0), (4, 6, 1), (10, 8, 0)], runs.Select(r => (r.Start, r.Length, r.Level)).ToArray());
        Assert.False(runs[0].RightToLeft);
        Assert.True(runs[1].RightToLeft);

        foreach (BidiRun r in runs)
        {
            string piece = text.Substring(r.Start, r.Length);
            ShapedText g = shaper.Shape(piece, TestFonts.Request("Bidi", 20), r.RightToLeft);
            AssertMatchesOneFont(g, piece, face, 20, r.RightToLeft);
            Assert.Equal(r.RightToLeft, g.RightToLeft);
        }

        // Line order of the runs: a left-to-right paragraph keeps them in place; a right-to-left one mirrors them.
        int[] order = new int[runs.Count];
        TextAnalysis.GetVisualOrder(runs.ToArray(), order, new byte[runs.Count]);
        Assert.Equal([0, 1, 2], order);

        var rtlRuns = new List<BidiRun>();
        TextAnalysis.GetBidiRuns("\u0627\u0644 abc \u0633", TextDirection.RightToLeft, rtlRuns, ref levels, out para);
        Assert.Equal(1, para);
        TextAnalysis.GetVisualOrder(rtlRuns.ToArray(), order, new byte[3]);
        Assert.Equal([2, 1, 0], order);

        TextAnalysis.GetBidiRuns("\u05D0 abc", TextDirection.Auto, rtlRuns = [], ref levels, out para);
        Assert.Equal(1, para); // the first strong character decides
    }

    [Fact]
    public void LineBreaksAndGraphemesComeFromOneFont()
    {
        const string text = "Hello wor-ld\ne\u0301!";
        var br = new LineBreakOpportunity[text.Length + 1];
        TextAnalysis.GetLineBreakOpportunities(text, br);
        Assert.Equal(LineBreakOpportunity.Allowed, br[6]); // after the space
        Assert.Equal(LineBreakOpportunity.Allowed, br[10]); // after the hyphen
        Assert.Equal(LineBreakOpportunity.None, br[3]);
        Assert.Equal(LineBreakOpportunity.Mandatory, br[13]); // after the line feed

        byte[] starts = new byte[text.Length + 1];
        TextAnalysis.GetGraphemeStarts(text, starts);
        Assert.Equal(1, starts[13]);
        Assert.Equal(0, starts[14]); // inside "e" + combining acute
        Assert.Equal(1, starts[15]);
        Assert.Equal(1, starts[text.Length]);
        Assert.Throws<ArgumentException>(() => TextAnalysis.GetGraphemeStarts(text, new byte[3]));
    }

    [Fact]
    public void SameInputGivesTheSameRunInAnyShaper()
    {
        FontCatalog c1 = TestFonts.Defaults();
        FontCatalog c2 = TestFonts.Defaults();
        FontRequest req = TestFonts.Request("Carlito, Liberation Serif", 13.5f, 700);
        const string text = "Déjà vu \u05D0\u05D1 fi ffl AVA";
        ShapedText a = new TextShaper(c1).Shape(text, req);
        ShapedText b = new TextShaper(c2, 8).Shape(text, req);
        Assert.True(a.GlyphIds.SequenceEqual(b.GlyphIds));
        Assert.True(a.X.SequenceEqual(b.X));
        Assert.True(a.Advances.SequenceEqual(b.Advances));
        Assert.True(a.GlyphClusters.SequenceEqual(b.GlyphClusters));
        Assert.Equal(a.Width, b.Width);
    }

    [Fact]
    public void SegmentsClustersAndGlyphsTileTheRun()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        foreach (bool rtl in new[] { false, true })
        {
            ShapedText run = shaper.Shape("abc \u05D0\u05D1\u05D2 \u03B1\u03B2 e\u0301 \U0001F600 end", TestFonts.Request("Carlito"), rtl);
            int g = 0;
            int k = 0;
            int textTotal = 0;
            foreach (ShapedTextSegment s in run.Segments)
            {
                Assert.Equal(g, s.GlyphStart);
                Assert.Equal(k, s.ClusterStart);
                g += s.GlyphCount;
                k += s.ClusterCount;
                textTotal += s.TextLength;
                float adv = 0;
                for (int i = s.GlyphStart; i < s.GlyphStart + s.GlyphCount; i++)
                {
                    adv += run.Advances[i];
                }

                Assert.Equal(adv, s.Advance, 3);
            }

            Assert.Equal(run.GlyphCount, g);
            Assert.Equal(run.Clusters.Length, k);
            Assert.Equal(run.Text.Length, textTotal);
            Assert.Equal(run.Text.Length, run.Clusters.ToArray().Sum(c => c.TextLength));
        }
    }

    [Fact]
    public void MetricsMatchTheFontsOwnTables()
    {
        FontCatalog c = TestFonts.Defaults();
        FontFace face = FontFace.Load(SansBytes);
        ResolvedFontList list = c.Resolve(TestFonts.Request("Liberation Sans", 20));
        FontMetricsInfo m = list.GetMetrics(20);
        float s = 20f / face.UnitsPerEm;
        Assert.Equal(face.Metrics.Ascender * s, m.Ascent, 3);
        Assert.Equal(-face.Metrics.Descender * s, m.Descent, 3);
        Assert.Equal(face.Metrics.LineGap * s, m.LineGap, 3);
        Assert.Equal(m.Ascent + m.Descent + m.LineGap, m.NormalLineHeight, 3);
        Assert.InRange(m.Ascent / 20, 0.88f, 0.93f);
        Assert.InRange(m.Descent / 20, 0.19f, 0.23f);
        Assert.InRange(m.XHeight / 20, 0.45f, 0.56f);
        Assert.InRange(m.CapHeight / 20, 0.62f, 0.75f);
        Assert.True(m.UnderlinePosition > 0 && m.UnderlineThickness > 0);
        Assert.True(m.StrikeoutPosition > 0 && m.StrikeoutThickness > 0);
        Assert.Equal(face.GetAdvance(face.GetGlyphIndex('0')) * s, m.ZeroAdvance, 3);
        Assert.Equal(0f, m.IdeographicAdvance); // no water ideograph in the packaged fonts
        Assert.Equal(0f, default(FontMetricsInfo).Ascent);
    }

    [Fact]
    public void FontSizeAdjustInputs()
    {
        FontCatalog c = TestFonts.Defaults();
        FontMetricsInfo m = c.Resolve(TestFonts.Request("Liberation Sans", 16)).GetMetrics(16);
        float aspect = m.XHeight / 16;
        Assert.Equal(16 * 0.5f / aspect, m.AdjustedSize(FontSizeAdjustMetric.ExHeight, 0.5f, 16), 3);
        // A face without the measurement leaves the size alone.
        Assert.Equal(16f, m.AdjustedSize(FontSizeAdjustMetric.IcWidth, 0.5f, 16));
        Assert.Equal(m.CapHeight, m.Measure(FontSizeAdjustMetric.CapHeight));
    }
}

public class ShapedRunCacheTests
{
    private static string[] Words(int n) => Enumerable.Range(0, n).Select(i => "word" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x").ToArray();

    [Fact]
    public void EqualInputsShareOneRunAndAnyDifferenceMakesANewOne()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        FontRequest r = TestFonts.Request("Arial", 16);
        ShapedText a = shaper.Shape("Hello", r);
        Assert.Same(a, shaper.Shape("Hello", r));
        Assert.Same(a, shaper.Shape("Hello", TestFonts.Request("arial", 16))); // an equal request
        Assert.NotSame(a, shaper.Shape("Hello", r.WithSize(17)));
        Assert.NotSame(a, shaper.Shape("Hellp", r));
        Assert.NotSame(a, shaper.Shape("Hello", r, rightToLeft: true));
        Assert.NotSame(a, shaper.Shape("Hello", TestFonts.Request("Arial", 16, 700)));
        Assert.NotSame(a, shaper.Shape("Hello", TestFonts.Request("Arial", 16, features: FontFeatureSettings.FromCss(kerning: "none"))));
        Assert.Equal(6, shaper.CachedRuns);
    }

    [Fact]
    public void ACacheHitAllocatesNothing()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        FontRequest r = TestFonts.Request("Arial, sans-serif", 14);
        string[] words = Words(1000);
        foreach (string w in words)
        {
            shaper.Shape(w, r);
        }

        Assert.Equal(1000, shaper.CachedRuns);
        float total = 0;
        Action hit = () =>
        {
            for (int i = 0; i < words.Length; i++)
            {
                total += shaper.Shape(words[i], r).Width;
            }
        };
        Allocation.AssertZero(hit, "a shaped-run cache hit", 2, 5);
        Assert.True(total > 0);
    }

    [Fact]
    public void ReshapingIntoRetainedBuffersAllocatesNothing()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        string[] words = Words(1000);
        FontRequest r = TestFonts.Request("Arial", 14);
        FontRequest withFeatures = TestFonts.Request("Carlito, serif", 14, 700, features: FontFeatureSettings.FromCss(ligatures: "none", numeric: "oldstyle-nums"));
        var runs = new ShapedText[words.Length];
        for (int i = 0; i < runs.Length; i++)
        {
            runs[i] = new ShapedText();
        }

        Action reshape = () =>
        {
            for (int i = 0; i < words.Length; i++)
            {
                shaper.ShapeInto(runs[i], words[i], (i & 1) == 0 ? r : withFeatures);
            }
        };
        Allocation.AssertZero(reshape, "reshaping 1000 runs into retained buffers", 3, 5);

        // The same through a fallback mix (Hebrew and Greek need other faces) and a right-to-left run.
        string[] mixed = Enumerable.Range(0, 1000).Select(i => "ab\u05D0\u05D1" + i + "\u03B1").ToArray();
        Action reshapeMixed = () =>
        {
            for (int i = 0; i < mixed.Length; i++)
            {
                shaper.ShapeInto(runs[i], mixed[i], withFeatures, rightToLeft: (i & 2) != 0);
            }
        };
        Allocation.AssertZero(reshapeMixed, "reshaping mixed-script runs with fallback", 3, 5);
    }

    [Fact]
    public void MissingGlyphsDoNotAllocateOnceReported()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        var run = new ShapedText();
        FontRequest r = TestFonts.Request("serif", 14);
        Action a = () => shaper.ShapeInto(run, "x\u4E2D\U0001F600y", r);
        Allocation.AssertZero(a, "reshaping text with missing glyphs", 3, 20);
        Assert.Equal(2, run.MissingGlyphCount);
    }

    [Fact]
    public void TheCacheIsBounded()
    {
        var shaper = new TextShaper(TestFonts.Defaults(), cacheCapacity: 16);
        Assert.Equal(16, shaper.CacheCapacity);
        FontRequest r = TestFonts.Request("serif");
        for (int i = 0; i < 500; i++)
        {
            ShapedText run = shaper.Shape("n" + i, r);
            Assert.Equal(1 + (i.ToString(System.Globalization.CultureInfo.InvariantCulture).Length), run.Clusters.Length);
        }

        Assert.True(shaper.CachedRuns <= 16);
        shaper.ClearCache();
        Assert.Equal(0, shaper.CachedRuns);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextShaper(TestFonts.Defaults(), 0));
    }

    [Fact]
    public void LongRunsAreShapedButNotCached()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        string text = new('a', TextShaper.MaxCachedLength + 1);
        ShapedText run = shaper.Shape(text, TestFonts.Request("serif"));
        Assert.Equal(text.Length, run.GlyphCount);
        Assert.Equal(0, shaper.CachedRuns);
        Assert.Throws<ArgumentException>(() => shaper.ShapeInto(run, new string('a', TextShaper.MaxRunLength + 1), TestFonts.Request("serif")));
    }

    [Fact]
    public void ChangingTheFontsInvalidatesCachedRuns()
    {
        FontCatalog c = TestFonts.Defaults();
        var shaper = new TextShaper(c);
        FontRequest r = TestFonts.Request("Custom, serif");
        ShapedText before = shaper.Shape("abc", r);
        Assert.Equal("Liberation Serif", before.Segments[0].Face.Face.FamilyName);
        c.AddFont(FontFace.Load(TestFonts.Liberation("LiberationMono-Regular.ttf")), "Custom", 400, FontStyleKind.Normal);
        ShapedText after = shaper.Shape("abc", r);
        Assert.NotSame(before, after);
        Assert.Equal("Liberation Mono", after.Segments[0].Face.Face.FamilyName);
    }

    [Fact]
    public void EmptyTextGivesAnEmptyRun()
    {
        var shaper = new TextShaper(TestFonts.Defaults());
        ShapedText run = shaper.Shape("", TestFonts.Request("serif"));
        Assert.Equal(0, run.GlyphCount);
        Assert.Equal(0f, run.Width);
        Assert.True(run.Segments.IsEmpty);
    }
}
