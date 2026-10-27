using System;
using System.Globalization;
using System.IO;
using BenchmarkDotNet.Attributes;
using OneRender.Image;
using OneRender.Interaction;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Benchmarks;

/// <summary>
/// The like-for-like row of the interactive comparison: the flex-only page that <see cref="OneGameUiBenchmarks"/> builds with OneGame.UI,
/// built through the engine's builder front end (<see cref="FlexHudScene"/>: styled nodes and declarations, no HTML) and driven the way a
/// game host drives it. The same changes as the OneGame.UI side, with a frame after each: hover (<c>HoverRestyle</c> sets the element
/// states directly; <c>HoverWithHitTest</c> also finds the target under the pointer first, as OneGame.UI's pointer dispatch does), text
/// change and one-row resize. Setup refuses to run unless the two trees agree (see <see cref="VerifyEquivalence"/>).
/// </summary>
[MemoryDiagnoser]
public class FlexHudBenchmarks
{
    private LivePage _page = null!;
    private HitTester _hits = null!;
    private FlexHudScene _scene = null!;
    private StyledNode _slotA, _slotB, _label, _row;
    private StyleDeclaration _tall = null!, _normal = null!;
    private readonly string[] _texts = ["Gold 100", "Gold 1000", "Gold 7"];
    private bool _flip;
    private int _t;
    private double _ax, _ay, _bx, _by;

    [GlobalSetup]
    public void Setup()
    {
        Init();
        VerifyEquivalence(this, null);
        for (int i = 0; i < 200; i++)
        {
            HoverRestyle();
            HoverWithHitTest();
            TextChange();
            SubtreeRelayout();
            IdleFrame();
        }
    }

    private void Init()
    {
        var tree = new StyledTree();
        _scene = FlexHudScene.Populate(tree);
        _page = new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = FlexHudScene.Width, ViewportHeight = FlexHudScene.Height });
        _slotA = _scene["slot3"];
        _slotB = _scene["slot20"];
        _label = _scene.TextOf("gold");
        _row = _scene["quest3"];
        _tall = FlexHudScene.Declaration(FlexHudScene.QuestRowTall);
        _normal = FlexHudScene.Declaration(FlexHudScene.QuestRowNormal);
        _page.Flush();
        _hits = new HitTester(_page.Fragments, _page.Scroll, FlexHudScene.Width, FlexHudScene.Height);
        var q = new LayoutQueries(_page, _hits);
        var a = q.GetBoundingClientRect(_slotA);
        var b = q.GetBoundingClientRect(_slotB);
        (_ax, _ay) = (a.X + (a.Width / 2), a.Y + (a.Height / 2));
        (_bx, _by) = (b.X + (b.Width / 2), b.Y + (b.Height / 2));
    }

    /// <summary>A frame in which nothing changed.</summary>
    [Benchmark]
    public FlushResult IdleFrame() => _page.Flush();

    /// <summary>Moves hover from one inventory slot to the other (element states set directly), then builds the frame.</summary>
    [Benchmark]
    public FlushResult HoverRestyle()
    {
        _flip = !_flip;
        _page.Styled.SetStates(_slotA, _flip ? ElementStates.Hover : ElementStates.None);
        _page.Styled.SetStates(_slotB, _flip ? ElementStates.None : ElementStates.Hover);
        return _page.Flush();
    }

    /// <summary>Hit-tests the pointer position, then moves hover from one slot to the other and builds the frame.</summary>
    [Benchmark]
    public FlushResult HoverWithHitTest()
    {
        _flip = !_flip;
        _hits.HitTest(_flip ? _ax : _bx, _flip ? _ay : _by, out _);
        _page.Styled.SetStates(_slotA, _flip ? ElementStates.Hover : ElementStates.None);
        _page.Styled.SetStates(_slotB, _flip ? ElementStates.None : ElementStates.Hover);
        return _page.Flush();
    }

    /// <summary>Changes a label's text, then builds the frame.</summary>
    [Benchmark]
    public FlushResult TextChange()
    {
        _page.Styled.SetText(_label, _texts[_t++ % 3]);
        return _page.Flush();
    }

    /// <summary>Resizes one row, then builds the frame.</summary>
    [Benchmark]
    public FlushResult SubtreeRelayout()
    {
        _flip = !_flip;
        _page.Styled.SetDeclaration(_row, _flip ? _tall : _normal);
        return _page.Flush();
    }

    /// <summary>
    /// Builds both pages and checks they are the same: the same number of elements, in the same order and nesting, and the same border boxes
    /// to within half a pixel, after the first layout and again after the text change and the resize. Throws when they are not.
    /// </summary>
    /// <param name="log">Receives one line per check; null for silence.</param>
    public static void VerifyEquivalence(TextWriter? log) => VerifyEquivalence(new FlexHudBenchmarks(), log, init: true);

    private static void VerifyEquivalence(FlexHudBenchmarks me, TextWriter? log, bool init = false)
    {
        if (init)
        {
            me.Init();
        }

        OneGame.UI.UiTree ui = OneGameUiBenchmarks.CreateTree();
        ui.Frame();
        var queries = new LayoutQueries(me._page, me._hits);
        StyledNode root = me._page.Styled.Root;
        (int elements, int labels, double worst) = Compare(ui.Root.ChildAt(0), root, queries, "initial");
        log?.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"initial: {elements} elements ({labels} labels, {elements + labels} styled nodes) on both sides, worst border-box difference {worst:0.###} px"));

        // The same two edits on both sides.
        ui.Find("gold")!.Text = "Gold 1000";
        me._page.Styled.SetText(me._label, "Gold 1000");
        ui.Find("quest3")!.Style(s => s.Height(48));
        me._page.Styled.SetDeclaration(me._row, me._tall);
        ui.Frame();
        me._page.Flush();
        (_, _, double worstEdited) = Compare(ui.Root.ChildAt(0), root, queries, "after text change and resize");
        log?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"after text change and resize: worst border-box difference {worstEdited:0.###} px"));
        ui.Find("quest3")!.Style(s => s.Height(24));
        me._page.Styled.SetDeclaration(me._row, me._normal);
        ui.Frame();
        me._page.Flush();

        // The pointer over a slot reaches that slot (or something inside it) in the engine.
        if (!me._hits.HitTest(me._ax, me._ay, out HitResult hit) || !IsInside(hit.Element, me._slotA))
        {
            throw new InvalidOperationException("the pointer position over slot3 does not hit slot3");
        }
    }

    private static bool IsInside(StyledNode node, StyledNode ancestor)
    {
        for (StyledNode n = node; !n.IsNull; n = n.Parent)
        {
            if (n == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    // Walks the two trees together with an explicit stack. Children of the engine's tree that are text runs are not elements of the other
    // library's tree (its labels hold their text themselves), so they are skipped and counted.
    private static (int Elements, int Labels, double Worst) Compare(OneGame.UI.Element gameRoot, StyledNode engineRoot, LayoutQueries queries, string phase)
    {
        var stack = new System.Collections.Generic.Stack<(OneGame.UI.Element G, StyledNode E)>();
        stack.Push((gameRoot, engineRoot));
        int elements = 0, labels = 0;
        double worst = 0;
        while (stack.Count > 0)
        {
            (OneGame.UI.Element g, StyledNode e) = stack.Pop();
            elements++;
            var gb = g.AbsoluteBounds;
            var eb = queries.GetBoundingClientRect(e);
            double d = Math.Max(Math.Max(Math.Abs(gb.X - eb.X), Math.Abs(gb.Y - eb.Y)), Math.Max(Math.Abs(gb.Width - eb.Width), Math.Abs(gb.Height - eb.Height)));
            if (d > worst)
            {
                worst = d;
            }

            if (d > 0.5)
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"{phase}: node #{elements} ({g.Kind} {g.Id}) differs: OneGame.UI ({gb.X}, {gb.Y}, {gb.Width} x {gb.Height}), engine ({eb.X}, {eb.Y}, {eb.Width} x {eb.Height})"));
            }

            StyledNode ec = e.FirstChild;
            while (!ec.IsNull && ec.Kind != StyledNodeKind.Element)
            {
                if (ec.Kind == StyledNodeKind.Text)
                {
                    labels++;
                }

                ec = ec.NextSibling;
            }

            var pairs = new System.Collections.Generic.List<(OneGame.UI.Element, StyledNode)>();
            for (int i = 0; i < g.ChildCount; i++)
            {
                if (ec.IsNull)
                {
                    throw new InvalidOperationException(phase + ": the engine tree has fewer children than OneGame.UI under " + g.Kind);
                }

                pairs.Add((g.ChildAt(i), ec));
                ec = ec.NextSibling;
                while (!ec.IsNull && ec.Kind != StyledNodeKind.Element)
                {
                    ec = ec.NextSibling;
                }
            }

            if (!ec.IsNull)
            {
                throw new InvalidOperationException(phase + ": the engine tree has more children than OneGame.UI under " + g.Kind);
            }

            for (int i = pairs.Count - 1; i >= 0; i--)
            {
                stack.Push(pairs[i]);
            }
        }

        return (elements, labels, worst);
    }
}
