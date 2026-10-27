using BenchmarkDotNet.Attributes;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Style;
using OneRender.TestSupport;
using OneRender.Tree;

namespace OneRender.Benchmarks;

/// <summary>
/// The interactive bar on the 1,000-node game-UI page: each benchmark makes one change and flushes (restyle, box patch, layout of
/// what changed, display-list update). Expected: well under 1 ms and 0 B per change.
/// </summary>
[MemoryDiagnoser]
public class IncrementalBenchmarks
{
    private LivePage _page = null!;
    private StyledNode _slotA, _slotB, _label, _row;
    private StyleDeclaration _tall = null!, _normal = null!;
    private readonly string[] _texts = ["Gold 100", "Gold 1000", "Gold 7"];
    private bool _flip;
    private int _t;
    public int BoxCount => _page.Boxes.Count;

    [GlobalSetup]
    public void Setup()
    {
        var tree = new StyledTree();
        GameUiScene.SceneNodes nodes = GameUiScene.Populate(tree);
        _page = new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height });
        _slotA = nodes["slot3"];
        _slotB = nodes["slot20"];
        _label = nodes.TextOf("gold");
        _row = nodes["quest3"];
        _tall = GameUiScene.Declaration("display:flex;height:48px;overflow:hidden;justify-content:space-between;align-items:center;padding:0 6px;background:#1d2535;");
        _normal = GameUiScene.Declaration("display:flex;height:24px;overflow:hidden;justify-content:space-between;align-items:center;padding:0 6px;background:#1d2535;");

        // Warm the code paths (and let the JIT promote them) so a measurement sees steady state, as a running game does.
        for (int i = 0; i < 200; i++)
        {
            HoverRestyle();
            TextChange();
            SubtreeRelayout();
            IdleFrame();
        }
    }

    /// <summary>A frame in which nothing changed: the steady state of a UI at rest. Expected: next to nothing and 0 B.</summary>
    [Benchmark]
    public FlushResult IdleFrame() => _page.Flush();

    /// <summary>Moves the pointer from one slot to another: two hover flips, restyle and repaint.</summary>
    [Benchmark]
    public FlushResult HoverRestyle()
    {
        _flip = !_flip;
        _page.Styled.SetStates(_slotA, _flip ? ElementStates.Hover : ElementStates.None);
        _page.Styled.SetStates(_slotB, _flip ? ElementStates.None : ElementStates.Hover);
        return _page.Flush();
    }

    /// <summary>Changes a label's text: its line is shaped again and the subtrees around it are reused.</summary>
    [Benchmark]
    public FlushResult TextChange()
    {
        _page.Styled.SetText(_label, _texts[_t++ % 3]);
        return _page.Flush();
    }

    /// <summary>Resizes one row (a declaration swap): that subtree and what moves with it lay out again.</summary>
    [Benchmark]
    public FlushResult SubtreeRelayout()
    {
        _flip = !_flip;
        _page.Styled.SetDeclaration(_row, _flip ? _tall : _normal);
        return _page.Flush();
    }
}
public static class StageProbe
{
    public static void Loop(string which)
    {
        var b = new IncrementalBenchmarks(); b.Setup();
        long end = System.Diagnostics.Stopwatch.GetTimestamp() + 12 * System.Diagnostics.Stopwatch.Frequency;
        while (System.Diagnostics.Stopwatch.GetTimestamp() < end)
            for (int i = 0; i < 200; i++) { if (which == "text") b.TextChange(); else if (which == "hover") b.HoverRestyle(); else b.SubtreeRelayout(); }
    }

    public static void Run()
    {
        var b = new IncrementalBenchmarks(); b.Setup();
        System.Console.WriteLine($"text reused {b.TextChange().ReusedSubtrees} sub {b.SubtreeRelayout().ReusedSubtrees} hover {b.HoverRestyle().ReusedSubtrees} boxes {b.BoxCount}");
        foreach (var (name, act) in new (string, System.Action)[]{("hover",()=>b.HoverRestyle()),("text",()=>b.TextChange()),("subtree",()=>b.SubtreeRelayout())})
        {
            for (int i=0;i<20000;i++) act();
            LivePage.TRestyle=LivePage.TLayout=LivePage.TPaint=LivePage.TCount=0;
            for (int i=0;i<20000;i++) act();
            double f=1e6/System.Diagnostics.Stopwatch.Frequency/LivePage.TCount;
            System.Console.WriteLine("why: " + string.Join(",", OneRender.Painting.Painter.Why));
            System.Array.Clear(OneRender.Painting.Painter.Why);
            System.Console.WriteLine($"{name}: restyle {LivePage.TRestyle*f:F1}us layout {LivePage.TLayout*f:F1}us paint {LivePage.TPaint*f:F1}us flushes/iter {LivePage.TCount/20000.0}");
        }
    }
}
