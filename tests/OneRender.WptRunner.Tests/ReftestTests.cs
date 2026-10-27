using OneRender.Image;

namespace OneRender.WptRunner.Tests;

public sealed class ReftestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wptrunner-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // The fake renderer paints each document as one flat colour named by its <body bgcolor=...> (no colour = blank page).
    private sealed class ColourRenderer(ReftestStore store) : IReftestRenderer
    {
        public RgbaImage Render(string wptPath)
        {
            string text = File.ReadAllText(store.FullPath(wptPath));
            int i = text.IndexOf("bgcolor=", StringComparison.Ordinal);
            byte v = i < 0 ? (byte)255 : byte.Parse(text.AsSpan(i + 8, 3), System.Globalization.CultureInfo.InvariantCulture);
            byte[] px = new byte[4 * 4 * 4];
            for (int p = 0; p < px.Length; p += 4)
            {
                px[p] = v;
                px[p + 1] = 255;
                px[p + 2] = 255;
                px[p + 3] = 255;
            }

            return new RgbaImage(4, 4, px);
        }
    }

    private void Write(string name, string head, string body = "") =>
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(_root).FullName, name), $"<!doctype html><head>{head}</head><body {body}>");

    private Verdict Run(string test)
    {
        var store = new ReftestStore(_root);
        return new ReftestEvaluator(store, new ColourRenderer(store)).Run(test);
    }

    [Theory]
    [InlineData("10;4", 10, 10, 4, 4)]
    [InlineData("maxDifference=1-3;totalPixels=0-20", 1, 3, 0, 20)]
    public void FuzzyMetaParses(string content, int d0, int d1, int p0, int p1)
    {
        Assert.True(ReftestParser.TryParseFuzzy("a/t.html", content, out FuzzySpec spec));
        Assert.Null(spec.Reference);
        Assert.Equal(new IntRange(d0, d1), spec.MaxDifference);
        Assert.Equal(new IntRange(p0, p1), spec.TotalPixels);
    }

    [Fact]
    public void FuzzyMetaWithReferencePrefixResolvesRelativeToTest()
    {
        Assert.True(ReftestParser.TryParseFuzzy("a/b/t.html", "../r.html:2;3", out FuzzySpec spec));
        Assert.Equal("a/r.html", spec.Reference);
    }

    [Fact]
    public void ParserFindsLinksIgnoringComments()
    {
        ReftestFile f = ReftestParser.Parse(
            "x/t.html",
            "<!-- <link rel=match href=no.html> --><link rel=\"match\" href=\"ref.html\"><LINK REL=mismatch HREF='/y/z.html?q'>"
            + "<html class=\"a reftest-wait\">");
        Assert.Equal(new ReferenceLink("x/ref.html", false), f.References[0]);
        Assert.Equal(new ReferenceLink("y/z.html", true), f.References[1]);
        Assert.True(f.ReftestWait);
    }

    [Fact]
    public void MatchPassesWhenImagesEqualAndPaintedElseBlank()
    {
        Write("t.html", "<link rel=match href=r.html>", "bgcolor=100");
        Write("r.html", string.Empty, "bgcolor=100");
        Assert.Equal(Outcome.Pass, Run("t.html").Outcome);

        Write("t2.html", "<link rel=match href=r2.html>");
        Write("r2.html", string.Empty);
        Assert.Equal(Outcome.Blank, Run("t2.html").Outcome);
    }

    [Fact]
    public void MismatchOfBlankPagesFails()
    {
        Write("t.html", "<link rel=mismatch href=r.html>");
        Write("r.html", string.Empty);
        Assert.Equal(Outcome.Fail, Run("t.html").Outcome);
    }

    [Fact]
    public void AnyOneMatchSufficesButEveryMismatchMustDiffer()
    {
        Write("t.html", "<link rel=match href=bad.html><link rel=match href=good.html><link rel=mismatch href=other.html>", "bgcolor=100");
        Write("bad.html", string.Empty, "bgcolor=101");
        Write("good.html", string.Empty, "bgcolor=100");
        Write("other.html", string.Empty, "bgcolor=102");
        Assert.Equal(Outcome.Pass, Run("t.html").Outcome);

        Write("other.html", string.Empty, "bgcolor=100");
        Assert.Equal(Outcome.Fail, Run("t.html").Outcome);
    }

    [Fact]
    public void ReferenceChainMustAlsoHold()
    {
        Write("t.html", "<link rel=match href=a.html>", "bgcolor=100");
        Write("a.html", "<link rel=match href=b.html>", "bgcolor=100");
        Write("b.html", string.Empty, "bgcolor=100");
        Assert.Equal(Outcome.Pass, Run("t.html").Outcome);

        Write("b.html", string.Empty, "bgcolor=007");
        Assert.Equal(Outcome.Fail, Run("t.html").Outcome);
    }

    [Fact]
    public void MismatchPairsLinkingBackToEachOtherDoNotLoop()
    {
        Write("a.html", "<link rel=mismatch href=b.html>", "bgcolor=011");
        Write("b.html", "<link rel=mismatch href=a.html>", "bgcolor=022");
        Assert.Equal(Outcome.Pass, Run("a.html").Outcome);
    }

    [Fact]
    public void FuzzyAllowsSmallDifferenceAndRequiresRange()
    {
        Write("t.html", "<link rel=match href=r.html><meta name=fuzzy content=\"5;16\">", "bgcolor=100");
        Write("r.html", string.Empty, "bgcolor=103");
        Assert.Equal(Fuzzy("3;16"), Run("t.html").Outcome);
        Assert.Equal(Outcome.Fail, Fuzzy("2;16"));

        Outcome Fuzzy(string content)
        {
            Write("t.html", $"<link rel=match href=r.html><meta name=fuzzy content=\"{content}\">", "bgcolor=100");
            return Run("t.html").Outcome;
        }
    }

    [Fact]
    public void UnrunnableTestsAreSkippedWithReasons()
    {
        Write("w.html", "<link rel=match href=r.html>", "bgcolor=1");
        File.WriteAllText(Path.Combine(_root, "w.html"), "<html class=reftest-wait><link rel=match href=r.html>");
        Write("r.html", string.Empty);
        Write("s.html", "<link rel=match href=r.html><script>x()</script>");
        Write("p-print.html", "<link rel=match href=r.html>");
        Write("h.html", "<script src=/resources/testharness.js></script>");
        Assert.Contains("reftest-wait", Run("w.html").Reason, StringComparison.Ordinal);
        Assert.Equal(Outcome.Skipped, Run("s.html").Outcome);
        Assert.Equal(Outcome.Skipped, Run("p-print.html").Outcome);
        Assert.Equal("testharness.js test", Run("h.html").Reason);
    }

    [Fact]
    public void PngReaderRoundTripsAFilteredImage()
    {
        // 2x1 RGBA image, one scanline using the Sub filter, built by hand.
        byte[] raw = [1, 10, 20, 30, 255, 5, 5, 5, 0];
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            z.Write(raw);
        }

        byte[] idat = ms.ToArray();
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", [0, 0, 0, 2, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
        Chunk(png, "IDAT", idat);
        Chunk(png, "IEND", []);
        var shot = OneRender.WptRunner.Chrome.PngReader.Decode(png.ToArray());
        Assert.Equal([10, 20, 30, 255, 15, 25, 35, 255], shot.Rgba);
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        // The reader does not verify CRCs, so they are written as zero.
        s.Write([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length]);
        s.Write(System.Text.Encoding.ASCII.GetBytes(type));
        s.Write(data);
        s.Write([0, 0, 0, 0]);
    }
}
