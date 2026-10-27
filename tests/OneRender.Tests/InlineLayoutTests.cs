using OneRender.Image;
using OneRender.Layout;
using OneRender.Tests.Support;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Inline layout: white space, line breaking, inline boxes, line height, alignment, bidi and intrinsic sizes.</summary>
public class InlineLayoutTests
{
    private sealed class Lay
    {
        public Pipeline P = null!;
        public FragmentTree F = null!;
        public BlockLayoutEngine Engine = null!;

        public Rect this[string id]
        {
            get
            {
                Assert.True(F.TryGetBorderBox(P.Document.GetElementById(id), out Rect r), "no box for #" + id);
                return r;
            }
        }

        public bool Has(string id) => F.TryGetBorderBox(P.Document.GetElementById(id), out _);
    }

    private static Lay Run(string body, string css = "", double width = 800, double height = 600)
    {
        string html = "<!DOCTYPE html><html><head><style>body{margin:0;font:16px/20px 'Liberation Sans'}" + css + "</style></head><body>" + body + "</body></html>";
        Pipeline p = Pipeline.FromHtml(html, width, height);
        var f = new FragmentTree(p.Boxes);
        var engine = new BlockLayoutEngine();
        engine.Layout(p.Boxes, new LayoutInput(width, double.NaN, FontProviders.ForCurrentThread()), f);
        return new Lay { P = p, F = f, Engine = engine };
    }

    [Fact]
    public void Smoke()
    {
        Lay l = Run("<p id=a>Line one<br>Line two<br><br>Line four<br></p>", "p{width:200px;margin:0}");
        System.IO.File.WriteAllText(System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("TMPDIR") ?? ".", "boxes.txt"), l.P.Boxes.Dump());
        System.IO.File.WriteAllText(System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("TMPDIR") ?? ".", "smoke.txt"), l.F.Dump());
        Assert.True(l["a"].Height >= 60);
    }
}
