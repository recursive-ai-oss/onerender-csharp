using OneRender.Image;
using OneRender.WptRunner;
using Xunit;

namespace OneRender.WptRunner.Tests;

public sealed class PrintReftestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wptrunner-print-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // Each document's pages are named by <body data-pages="100,101,..."> : one flat 4x4 page per value, 255 = white (blank).
    private sealed class FakePaged(ReftestStore store) : IPagedReftestRenderer
    {
        public RgbaImage Render(string wptPath) => throw new InvalidOperationException("a print test must not be rendered as one image");

        public IReadOnlyList<RgbaImage> RenderPages(string wptPath)
        {
            string text = File.ReadAllText(store.FullPath(wptPath));
            int i = text.IndexOf("data-pages=\"", StringComparison.Ordinal) + 12;
            string list = text[i..text.IndexOf('"', i)];
            var pages = new List<RgbaImage>();
            foreach (string v in list.Split(','))
            {
                byte b = byte.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
                byte[] px = new byte[4 * 4 * 4];
                for (int p = 0; p < px.Length; p += 4)
                {
                    px[p] = b;
                    px[p + 1] = 255;
                    px[p + 2] = 255;
                    px[p + 3] = 255;
                }

                pages.Add(new RgbaImage(4, 4, px));
            }

            return pages;
        }
    }

    private void Write(string name, string head, string pages) =>
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(_root).FullName, name), $"<!doctype html><head>{head}</head><body data-pages=\"{pages}\">");

    private Verdict Run(string test)
    {
        var store = new ReftestStore(_root);
        return new ReftestEvaluator(store, new FakePaged(store)).Run(test);
    }

    [Theory]
    [InlineData("css/css-page/a-print.html", true)]
    [InlineData("css/css-page/print/a.html", true)]
    [InlineData("css/css-page/a-print.tentative.html", true)]
    [InlineData("css/css-page/a-print-001.html", false)]
    [InlineData("css/css-page/a-print.tentative.xht", true)]
    [InlineData("css/print/a.html", true)]
    [InlineData("print.html", false)]
    [InlineData("css/css-page/a.html", false)]
    public void PrintReftestsAreRecognisedByNameOrDirectory(string path, bool expected) =>
        Assert.Equal(expected, ReftestEvaluator.IsPrint(path));

    [Theory]
    [InlineData("-2,4,6,7,9-", "1 2 4 6 7 9 10")]
    [InlineData("3", "3")]
    [InlineData("2-3", "2 3")]
    [InlineData("8-", "8 9 10")]
    [InlineData("11,x,4-2", "")]
    [InlineData(null, "1 2 3 4 5 6 7 8 9 10")]
    public void PageRangesSelectPagesFromOne(string? spec, string expected)
    {
        int[] all = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        Assert.Equal(expected, string.Join(' ', PageRanges.Select(all, spec)));
    }

    [Fact]
    public void ParserKeysReftestPagesByDocument()
    {
        ReftestFile f = ReftestParser.Parse("x/t-print.html", "<link rel=match href=r.html><meta name=reftest-pages content=\"2-3\"><meta name=reftest-pages content=\"r.html:1\">");
        Assert.Equal("2-3", f.RangesFor("x/t-print.html"));
        Assert.Equal("1", f.RangesFor("x/r.html"));
        Assert.Null(f.RangesFor("x/other.html"));
    }

    [Fact]
    public void EveryPageMustMatchAndCountsMustAgree()
    {
        Write("t-print.html", "<link rel=match href=r.html>", "100,101");
        Write("r.html", string.Empty, "100,101");
        Assert.Equal(Outcome.Pass, Run("t-print.html").Outcome);

        Write("r.html", string.Empty, "100,102");
        Verdict v = Run("t-print.html");
        Assert.Equal(Outcome.Fail, v.Outcome);
        Assert.Contains("page 2", v.Reason, StringComparison.Ordinal);

        Write("r.html", string.Empty, "100");
        Verdict count = Run("t-print.html");
        Assert.Equal(Outcome.Fail, count.Outcome);
        Assert.Contains("2 pages vs 1", count.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RangeSelectsPagesOfTheDocumentThatDeclaresItOnly()
    {
        // The test's range does not apply to the reference: page 2 of the test is compared with every page of the reference.
        Write("t-print.html", "<link rel=match href=r.html><meta name=reftest-pages content=\"2\">", "100,101,103");
        Write("r.html", string.Empty, "101");
        Assert.Equal(Outcome.Pass, Run("t-print.html").Outcome);

        Write("r.html", string.Empty, "7,101,8");
        Assert.Equal(Outcome.Fail, Run("t-print.html").Outcome);

        // A prefixed range selects pages of that reference.
        Write("t-print.html", "<link rel=match href=r.html><meta name=reftest-pages content=\"2\"><meta name=reftest-pages content=\"r.html:2\">", "100,101,103");
        Assert.Equal(Outcome.Pass, Run("t-print.html").Outcome);
    }

    [Fact]
    public void FuzzyAppliesToEachPageSeparately()
    {
        // 3 levels apart on each of two pages: a pixel allowance of 16 per page is enough, 15 is not.
        Write("t-print.html", "<link rel=match href=r.html><meta name=fuzzy content=\"3;16\">", "100,100");
        Write("r.html", string.Empty, "103,103");
        Assert.Equal(Outcome.Pass, Run("t-print.html").Outcome);

        Write("t-print.html", "<link rel=match href=r.html><meta name=fuzzy content=\"3;15\">", "100,100");
        Assert.Equal(Outcome.Fail, Run("t-print.html").Outcome);
    }

    [Fact]
    public void MismatchHoldsWhenAnyPageDiffers()
    {
        Write("t-print.html", "<link rel=mismatch href=r.html>", "100,101");
        Write("r.html", string.Empty, "100,102");
        Assert.Equal(Outcome.Pass, Run("t-print.html").Outcome);

        Write("r.html", string.Empty, "100,101");
        Assert.Equal(Outcome.Fail, Run("t-print.html").Outcome);
    }

    [Fact]
    public void AllBlankPagesAreBlankNotPass()
    {
        Write("t-print.html", "<link rel=match href=r.html>", "255,255");
        Write("r.html", string.Empty, "255,255");
        Assert.Equal(Outcome.Blank, Run("t-print.html").Outcome);
    }

    [Fact]
    public void RendererWithoutPagesSkipsPrintTests()
    {
        Write("t-print.html", "<link rel=match href=r.html>", "100");
        Write("r.html", string.Empty, "100");
        var store = new ReftestStore(_root);
        Assert.Equal(Outcome.Skipped, new ReftestEvaluator(store, new OnlyImage()).Run("t-print.html").Outcome);
    }

    [Fact]
    public void CompositorBlendsOverWhiteAndClips()
    {
        RgbaImage page = PageCompositor.White(4, 4);
        byte[] px = new byte[2 * 2 * 4];
        for (int p = 0; p < px.Length; p += 4)
        {
            px[p + 3] = 255;
        }

        px[3] = 0; // first pixel transparent: leaves the page white
        PageCompositor.Over(page, new RgbaImage(2, 2, px), 0, 0, 2, 2, 3, 3);
        Assert.Equal(255, page.Pixels[(3 * 4 + 3) * 4]);
        PageCompositor.Over(page, new RgbaImage(2, 2, px), 1, 1, 1, 1, 0, 0);
        Assert.Equal(0, page.Pixels[0]);
    }

    private sealed class OnlyImage : IReftestRenderer
    {
        public RgbaImage Render(string wptPath) => new(1, 1, [0, 0, 0, 255]);
    }
}
