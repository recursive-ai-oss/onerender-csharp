using System.Text;
using BenchmarkDotNet.Attributes;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;
using OneRender.TestSupport;

namespace OneRender.Benchmarks;

/// <summary>
/// Layout of very large trees, and hit testing: the rows behind the unit tests that check these cases for bounded work. The unit tests
/// count work and bytes; the time belongs here. Run with <c>--filter '*StressBenchmarks*'</c>.
/// </summary>
[MemoryDiagnoser]
public class StressBenchmarks
{
    private BoxTree _flexNoWrap = null!, _flexWrap = null!, _floats = null!, _grid = null!;
    private FragmentTree _flexNoWrapF = null!, _flexWrapF = null!, _floatsF = null!, _gridF = null!;
    private InteractivePage _ui = null!;
    private readonly BlockLayoutEngine _engine = new();
    private IFontProvider _fonts = null!;
    private ChurnSoak _moving = null!;
    private int _i;

    [GlobalSetup]
    public void Setup()
    {
        _fonts = FontProviders.CreateDefault();
        _moving = new ChurnSoak(_fonts, false, moving: true);
        for (int i = 0; i < 5000; i++)
        {
            _moving.Frame();
        }

        (_flexNoWrap, _flexNoWrapF) = Flex(100_000, "nowrap");
        (_flexWrap, _flexWrapF) = Flex(100_000, "wrap");
        (_floats, _floatsF) = Floats(10_000);
        (_grid, _gridF) = Grid(100_000);
        var sb = new StringBuilder("<div id=app style='width:780px'>");
        for (int i = 0; i < 250; i++)
        {
            sb.Append("<div class=row id=r").Append(i).Append(" style='height:24px;overflow:hidden'><span class=lbl>Item ").Append(i)
                .Append("</span><button id=b").Append(i).Append(" style='width:60px;margin-left:8px'>Go</button><i style='display:inline-block;width:10px'></i></div>");
        }

        sb.Append("</div>");
        _ui = InteractivePage.Create(sb.ToString(), new HtmlPageOptions(_fonts) { ViewportWidth = 800, ViewportHeight = 600 });
    }

    private static (BoxTree, FragmentTree) Build(StyledTree styled)
    {
        var boxes = new BoxTree(styled);
        boxes.Build();
        return (boxes, new FragmentTree(boxes));
    }

    private static (BoxTree, FragmentTree) Flex(int items, string wrap)
    {
        var styled = new StyledTree();
        StyledNode root = styled.AddRoot("div", new StyleDeclaration().Set("display", "flex").Set("flex-wrap", wrap));
        StyleDeclaration item = new StyleDeclaration().Set("width", "10px").Set("height", "5px").Set("flex-grow", "1");
        for (int i = 0; i < items; i++)
        {
            styled.AddElement(root, "div", item);
        }

        return Build(styled);
    }

    private static (BoxTree, FragmentTree) Floats(int count)
    {
        var styled = new StyledTree();
        StyledNode root = styled.AddRoot("div", new StyleDeclaration().Set("display", "block"));
        StyleDeclaration f = new StyleDeclaration().Set("float", "left").Set("width", "30px").Set("height", "20px");
        for (int i = 0; i < count; i++)
        {
            styled.AddElement(root, "div", f);
        }

        return Build(styled);
    }

    private static (BoxTree, FragmentTree) Grid(int items)
    {
        var styled = new StyledTree();
        StyledNode root = styled.AddRoot("div", new StyleDeclaration().Set("display", "grid").Set("grid-template-columns", "repeat(4, 1fr)").Set("grid-auto-rows", "1px").Set("width", "400px"));
        StyleDeclaration cell = new StyleDeclaration().Set("display", "block");
        for (int i = 0; i < items; i++)
        {
            styled.AddElement(root, "i", cell);
        }

        return Build(styled);
    }

    private void Lay(BoxTree boxes, FragmentTree f) => _engine.Layout(boxes, new LayoutInput(1000, double.NaN, _fonts), f);

    /// <summary>100,000 flex items in one line.</summary>
    [Benchmark]
    public void FlexNoWrap100k() => Lay(_flexNoWrap, _flexNoWrapF);

    /// <summary>100,000 flex items that wrap into lines.</summary>
    [Benchmark]
    public void FlexWrap100k() => Lay(_flexWrap, _flexWrapF);

    /// <summary>10,000 floats in one block formatting context.</summary>
    [Benchmark]
    public void Floats10k() => Lay(_floats, _floatsF);

    /// <summary>100,000 grid items in four columns.</summary>
    [Benchmark]
    public void Grid100k() => Lay(_grid, _gridF);

    /// <summary>One hit test on a page of about a thousand nodes. Expected: microseconds and 0 B.</summary>
    [Benchmark]
    public bool HitTest()
    {
        int i = ++_i;
        return _ui.Hits.HitTest(10 + (i % 700), 5 + ((i * 7) % 560), out _);
    }

    /// <summary>One frame of the HUD churn in which one element's left and one label's width take values never used before (the style table reclaims the rows). The B column includes the host's own work of parsing the two new values; the engine's share is 0 B (see the unit test).</summary>
    [Benchmark]
    public void NewStyleValuesEveryFrame() => _moving.Frame();
}
