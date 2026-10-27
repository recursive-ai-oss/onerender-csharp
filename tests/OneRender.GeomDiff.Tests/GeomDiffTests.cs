using System.Text.Json;

namespace OneRender.GeomDiff.Tests;

public class GeomDiffTests
{
    private static readonly Tolerance Tol = new(1, 0.5);

    [Theory]
    [InlineData(100, 100, 50, 20, 100.9, 100, 50, 20, false, "Match")]
    [InlineData(100, 100, 50, 20, 102, 100, 50, 20, false, "PositionOnly")]
    [InlineData(100, 100, 50, 20, 100, 100, 53, 20, false, "SizeOnly")]
    [InlineData(100, 100, 50, 20, 105, 100, 53, 20, false, "PositionAndSize")]
    [InlineData(0, 0, 1000, 20, 4, 0, 1004, 20, false, "Match")] // 0.5% of 1000 px is 5 px
    [InlineData(0, 0, 1000, 20, 6, 0, 1000, 20, false, "PositionOnly")]
    [InlineData(10, 10, 80, 18, 10, 10, 96, 18, true, "TextRun")]
    [InlineData(10, 10, 80, 18, 40, 10, 80, 18, true, "PositionOnly")] // text only matters for size differences
    public void ClassifiesByAxisAndTolerance(double cx, double cy, double cw, double ch, double ox, double oy, double ow, double oh, bool text, string expected)
    {
        Assert.Equal(Enum.Parse<Verdict>(expected), Comparer.Classify([cx, cy, cw, ch], [ox, oy, ow, oh], text, Tol));
    }

    [Fact]
    public void MissingBoxesAreClassifiedBySide()
    {
        Assert.Equal(Verdict.MissingOurs, Comparer.Classify([0, 0, 1, 1], null, false, Tol));
        Assert.Equal(Verdict.MissingChrome, Comparer.Classify(null, [0, 0, 1, 1], false, Tol));
        Assert.Equal(Verdict.Match, Comparer.Classify(null, null, false, Tol));
    }

    // A hand-made DOMSnapshot: #document > html > (head, body > (div "text", div > span)), with a shadow root and a
    // pseudo-element that must not take part in the element paths.
    private const string Snapshot = """
        {"strings":["#document","HTML","HEAD","BODY","DIV","SPAN","#text","hello","  ","INPUT","#document-fragment","::before","block","inline"],
         "documents":[{
          "nodes":{
            "parentIndex":[-1,0,1,1,3,4,3,6,3,8,9],
            "nodeType":[9,1,1,1,1,3,1,1,1,11,1],
            "nodeName":[0,1,2,3,4,6,4,5,9,10,4],
            "nodeValue":[-1,-1,-1,-1,-1,7,-1,-1,-1,-1,-1],
            "shadowRootType":{"index":[9],"value":[0]},
            "pseudoType":{"index":[],"value":[]}
          },
          "layout":{
            "nodeIndex":[1,3,4,6,7,8],
            "bounds":[[0,0,800,100],[8,8,784,84],[8,8,784,20],[8,28,784,40],[8,28,30,18],[8,70,150,22]],
            "styles":[[12],[12],[12],[12],[13],[12]]
          }}]}
        """;

    [Fact]
    public void ExtractsElementsKeyedByParsedTreePath()
    {
        using JsonDocument doc = JsonDocument.Parse(Snapshot);
        List<ElementRecord> els = Capture.ExtractElements(doc.RootElement);
        string[] paths = [.. els.Select(static e => e.Path)];
        // html, head, body, div (with text), div, span, input; the shadow root's child div is not an element of the tree.
        Assert.Equal(["0", "0/0", "0/1", "0/1/0", "0/1/1", "0/1/1/0", "0/1/2"], paths);
        Assert.Equal(["html", "head", "body", "div", "div", "span", "input"], els.Select(static e => e.Tag));
        Assert.True(els[3].Text);
        Assert.False(els[4].Text);
        Assert.Null(els[1].Box); // head generates no box
        Assert.Equal("8 8 784 20", string.Join(" ", els[3].Box!));
        Assert.Equal("inline", els[5].Display);
    }

    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "geomdiff-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    private static void WriteSyntheticCapture(string root)
    {
        GeometryCapture cap = new() { Source = "chrome", Page = "p1", Viewport = new ViewportSize { Width = 1280, Height = 800 } };
        for (int i = 0; i < 400; i++)
        {
            bool text = i % 3 == 0;
            cap.Elements.Add(new ElementRecord
            {
                Path = "0/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Tag = text ? "p" : "div",
                Display = text ? "block" : "flex",
                Text = text,
                Box = [i, i * 2, 100 + i, 20],
            });
        }
        cap.Elements.Add(new ElementRecord { Path = "0/400", Tag = "script", Display = "none" });
        Store.Write(Path.Combine(root, "p1", "1280x800.json.gz"), cap, Ctx.Default.GeometryCapture);
    }

    [Fact]
    public void PerturbedCaptureIsClassifiedExactlyAsPerturbed()
    {
        string dir = TempDir();
        try
        {
            WriteSyntheticCapture(Path.Combine(dir, "chrome"));
            Assert.Equal(0, Perturb.Run(Path.Combine(dir, "chrome"), Path.Combine(dir, "bad"), 1, Tol, Path.Combine(dir, "expect.json")));
            Expectation exp = Store.Read(Path.Combine(dir, "expect.json"), Ctx.Default.Expectation);
            Assert.True(exp.Classes.PositionOnly > 0 && exp.Classes.SizeOnly > 0 && exp.Classes.PositionAndSize > 0 && exp.Classes.TextRun > 0 && exp.Classes.MissingOurs > 0);
            int rc = Comparer.Run(new Comparer.Options
            {
                ChromeDir = Path.Combine(dir, "chrome"),
                OursDir = Path.Combine(dir, "bad"),
                Set = "t",
                Label = "perturbed",
                OutDir = Path.Combine(dir, "out"),
                ExpectFile = Path.Combine(dir, "expect.json"),
            }, CancellationToken.None);
            Assert.Equal(0, rc);
            SummaryFile sum = Store.Read(Path.Combine(dir, "out", "summary.json"), Ctx.Default.SummaryFile);
            Assert.False(sum.GateMet);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IdenticalCapturesAgreeCompletely()
    {
        string dir = TempDir();
        try
        {
            WriteSyntheticCapture(Path.Combine(dir, "a"));
            WriteSyntheticCapture(Path.Combine(dir, "b"));
            Comparer.Run(new Comparer.Options { ChromeDir = Path.Combine(dir, "a"), OursDir = Path.Combine(dir, "b"), Set = "t", Label = "same", OutDir = Path.Combine(dir, "out") }, CancellationToken.None);
            SummaryFile sum = Store.Read(Path.Combine(dir, "out", "summary.json"), Ctx.Default.SummaryFile);
            Assert.Equal(400, sum.Total.Elements); // the script element has no box on either side and is not counted
            Assert.Equal(1.0, sum.Total.Rate);
            Assert.True(sum.GateMet);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheEngineSideLaysOutTheSnapshotAndTheRestIsMissing()
    {
        string dir = TempDir();
        try
        {
            WriteSyntheticCapture(Path.Combine(dir, "chrome"));
            string page = Path.Combine(dir, "src", "p1");
            Directory.CreateDirectory(page);
            File.WriteAllText(Path.Combine(page, "index.html"), "<!DOCTYPE html><title>t</title><p>x</p>");
            Assert.Equal(1, Snapshots.BuildStatic(Path.Combine(dir, "src"), Path.Combine(dir, "snap"), ""));
            Comparer.Run(new Comparer.Options { ChromeDir = Path.Combine(dir, "chrome"), UseEngine = true, SnapshotDir = Path.Combine(dir, "snap"), Set = "t", Label = "engine", OutDir = Path.Combine(dir, "out") }, CancellationToken.None);
            SummaryFile sum = Store.Read(Path.Combine(dir, "out", "summary.json"), Ctx.Default.SummaryFile);
            // The page has three elements with boxes; the synthetic Chrome capture has 400, so most are missing on our side.
            Assert.True(sum.Total.Classes.MissingOurs > 300);
            Assert.True(sum.Total.Rate < 0.05);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReplayServesTheFrozenPageAndNothingElse()
    {
        SnapshotPage page = new() { Id = "x", Url = "https://pages.test/x/index.html" };
        Dictionary<string, (byte[] Body, string Type)> res = new() { [Snapshots.UrlKey("https://pages.test/x/a.css")] = ([1, 2], "text/css") };
        Router r = Snapshots.Replay(page, "<p>", res);
        Assert.IsType<Fulfill>(r("https://pages.test/x/index.html", "Document", true, "GET"));
        Assert.Equal(2, ((Fulfill)r("https://pages.test/x/a.css", "Stylesheet", false, "GET")).Body.Length);
        Assert.Empty(((Fulfill)r("https://pages.test/x/missing.css", "Stylesheet", false, "GET")).Body);
        Assert.IsType<Fail>(r("https://pages.test/x/logo.png", "Image", false, "GET"));
        Assert.IsType<Fail>(r("https://elsewhere.test/script.js", "Script", false, "GET"));
    }

    [Fact]
    public void UrlKeyIgnoresCaseDefaultPortAndFragment()
    {
        Assert.Equal(Snapshots.UrlKey("https://Example.test:443/a?b=1#frag"), Snapshots.UrlKey("https://example.test/a?b=1"));
        Assert.NotEqual(Snapshots.UrlKey("https://example.test/a"), Snapshots.UrlKey("https://example.test/A"));
    }

    [Fact]
    public void ProbeReportsElementBoxesByPath()
    {
        var fonts = new OneRender.Image.DefaultFontProvider();
        IReadOnlyList<ElementBox> boxes = GeometryProbe.LayOut("<body style='margin:0'><div style='height:10px'></div><div style='height:20px'><p style='margin:0;height:5px'></p></div></body>", null, null, 1280, 800, fonts);
        Assert.Contains(boxes, b => b.Path == "0" && b.Tag == "html");
        Assert.Contains(boxes, b => b.Path == "0/1" && b.Tag == "body");
        ElementBox second = Assert.Single(boxes, b => b.Path == "0/1/1");
        Assert.Equal(10, second.Y);
        Assert.Equal(20, second.Height);
        Assert.Equal(1280, second.Width);
        Assert.Contains(boxes, b => b.Path == "0/1/1/0" && b.Tag == "p" && b.Y == 10);
        Assert.DoesNotContain(boxes, b => b.Tag == "head" && b.Width > 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryProbe.LayOut("", null, null, 0, 800, fonts));
    }
}
