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

/// <summary>
/// Structural edits on the same page: a floating layer of about fifty nodes inserted, removed and moved, and one frame of the churn
/// soak (a layer in, the last one out, a chat line in, the oldest out). Each row is one flush with its setup outside the timing,
/// so the number is the cost of that edit alone. Expected: well under 1 ms and 0 B.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Engines.RunStrategy.Monitoring, launchCount: 1, warmupCount: 300, iterationCount: 2000)]
public class StructuralBenchmarks
{
    private ChurnSoak _soak = null!;

    [GlobalSetup]
    public void Setup()
    {
        _soak = new ChurnSoak(FontProviders.CreateDefault(), fresh: false);
        for (int i = 0; i < 500; i++)
        {
            _soak.Frame();
        }
    }

    [IterationSetup(Target = nameof(InsertSubtree))]
    public void BeforeInsert() => _soak.RemoveLayer();

    [IterationSetup(Targets = [nameof(RemoveSubtree), nameof(MoveSubtree)])]
    public void BeforeRemove() => _soak.InsertLayer();

    /// <summary>Inserts a subtree of about fifty nodes.</summary>
    [Benchmark]
    public void InsertSubtree() => _soak.InsertLayer();

    /// <summary>Removes it again.</summary>
    [Benchmark]
    public void RemoveSubtree() => _soak.RemoveLayer();

    /// <summary>Moves it under another parent.</summary>
    [Benchmark]
    public void MoveSubtree() => _soak.MoveLayer();

    /// <summary>One frame of the soak: fifty nodes in, fifty out, one chat line in and one out.</summary>
    [Benchmark]
    public void ChurnFrame() => _soak.Frame();
}
