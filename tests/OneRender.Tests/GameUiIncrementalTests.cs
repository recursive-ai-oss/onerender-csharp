using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using OneRender.Image;
using OneRender.Layout;
using OneRender.Interaction;
using OneRender.Painting;
using OneRender.Style;
using OneRender.TestSupport;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>The 1,000-node game-UI page through the retained pipeline: correctness against a fresh build, zero bytes, and time.</summary>
public class GameUiIncrementalTests
{
    internal static (LivePage Page, GameUiScene.SceneNodes Nodes) Scene()
    {
        var tree = new StyledTree();
        GameUiScene.SceneNodes nodes = GameUiScene.Populate(tree);
        var page = new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height });
        return (page, nodes);
    }

    [Fact]
    public void TheSceneHasAboutAThousandNodes()
    {
        (LivePage page, _) = Scene();
        Assert.InRange(page.Styled.Count, 1000, 1700);
        Assert.True(page.Display.Current.Count > 500);
    }

    // The three changes the interactive bar names, as actions on one page.
    private sealed class Changes
    {
        private readonly LivePage _page;
        private readonly StyledNode _slotA, _slotB, _label, _row;
        private readonly StyleDeclaration _tall = GameUiScene.Declaration("display:flex;height:48px;overflow:hidden;justify-content:space-between;align-items:center;padding:0 6px;background:#1d2535;");
        private readonly StyleDeclaration _normal = GameUiScene.Declaration("display:flex;height:24px;overflow:hidden;justify-content:space-between;align-items:center;padding:0 6px;background:#1d2535;");
        private readonly string[] _texts = ["Gold 100", "Gold 1000", "Gold 7"];
        private bool _flip;
        private int _t;

        public Changes(LivePage page, GameUiScene.SceneNodes nodes)
        {
            _page = page;
            _slotA = nodes["slot3"];
            _slotB = nodes["slot20"];
            _label = nodes.TextOf("gold");
            _row = nodes["quest3"];
            Hover = DoHover;
            Text = DoText;
            Subtree = DoSubtree;
        }

        public FlushResult Last;
        public readonly Action Hover, Text, Subtree;

        private void DoHover()
        {
            _flip = !_flip;
            _page.Styled.SetStates(_slotA, _flip ? ElementStates.Hover : ElementStates.None);
            _page.Styled.SetStates(_slotB, _flip ? ElementStates.None : ElementStates.Hover);
            Last = _page.Flush();
        }

        private void DoText()
        {
            _page.Styled.SetText(_label, _texts[_t++ % 3]);
            Last = _page.Flush();
        }

        private void DoSubtree()
        {
            _flip = !_flip;
            _page.Styled.SetDeclaration(_row, _flip ? _tall : _normal);
            Last = _page.Flush();
        }
    }

    [Fact]
    public void HudFileMatchesTheGenerator() => CheckHud();

    private static void CheckHud([CallerFilePath] string here = "")
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata", "game-ui", "hud.html"));
        string html = GameUiScene.Html();
        if (Environment.GetEnvironmentVariable("ONERENDER_REGEN_TESTDATA") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, html);
        }

        Assert.Equal(html, File.ReadAllText(path));
    }

    [Fact]
    public void HoverTextAndSubtreeChangesAllocateNothing()
    {
        (LivePage page, GameUiScene.SceneNodes nodes) = Scene();
        var c = new Changes(page, nodes);
        Allocation.AssertZero(c.Hover, "hover restyle", warmup: 300, iterations: 100);
        Allocation.AssertZero(c.Text, "text change", warmup: 300, iterations: 100);
        Allocation.AssertZero(c.Subtree, "one-subtree relayout", warmup: 300, iterations: 100);
    }

    [Fact]
    public void ChangesStayEqualToAFreshBuild()
    {
        (LivePage page, GameUiScene.SceneNodes nodes) = Scene();
        var c = new Changes(page, nodes);
        for (int i = 0; i < 6; i++)
        {
            c.Hover();
            c.Text();
            c.Subtree();
        }

        // A fresh page from the same tree state must produce the same snapshot.
        var tree = new StyledTree();
        GameUiScene.SceneNodes n2 = GameUiScene.Populate(tree);
        var fresh = new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height });
        var c2 = new Changes(fresh, n2);
        for (int i = 0; i < 6; i++)
        {
            c2.Hover();
            c2.Text();
            c2.Subtree();
        }

        Assert.Equal(EditSession.Snapshot(fresh), EditSession.Snapshot(page));
    }

    // The time of these changes (the 1 ms bar) is a benchmark row on the benchmark host (IncrementalBenchmarks). What a unit test can state
    // without a clock is how much work each change does: none of them generates the boxes again.
    [Fact]
    public void WorkPerChange()
    {
        (LivePage page, GameUiScene.SceneNodes nodes) = Scene();
        var c = new Changes(page, nodes);
        foreach ((string name, Action a) in new (string, Action)[] { ("hover", c.Hover), ("text", c.Text), ("subtree", c.Subtree) })
        {
            for (int i = 0; i < 20; i++)
            {
                a();
                Assert.Equal(FlushWork.None, c.Last.Work & FlushWork.BoxesRebuilt);
            }

            Console.WriteLine($"WORK {name}: {c.Last.Work}, reused {c.Last.ReusedSubtrees}, nodes {page.Styled.Count}, boxes {page.Boxes.Count}, fragments {page.Fragments.Count}, items {page.Display.Current.Count}");
        }
    }

    [Fact]
    public void LayoutQueriesFlushPendingEdits()
    {
        (LivePage page, GameUiScene.SceneNodes nodes) = Scene();
        var queries = new LayoutQueries(page, new HitTester(page.Fragments, page.Scroll, GameUiScene.Width, GameUiScene.Height));
        StyledNode row = nodes["quest3"];
        int before = queries.OffsetHeight(row);
        page.Styled.SetDeclaration(row, GameUiScene.Declaration("display:flex;height:77px;overflow:hidden;"));
        Assert.True(page.NeedsFlush);
        Assert.Equal(77, queries.OffsetHeight(row));
        Assert.False(page.NeedsFlush);
        Assert.NotEqual(77, before);
        page.Styled.SetDeclaration(row, GameUiScene.Declaration("display:flex;height:31px;overflow:hidden;"));
        Assert.Equal(31, queries.GetBoundingClientRect(row).Height);
        Assert.Equal(GameUiScene.Width, queries.ClientWidth(page.Styled.Root));
    }

    // Information only: what a hover costs through the HTML front end today (OneHtml's cascade allocates per state change).
    [Fact]
    public void HtmlHoverCost()
    {
        (LivePage builder, GameUiScene.SceneNodes nodes) = Scene();
        var q = new LayoutQueries(builder, new HitTester(builder.Fragments, builder.Scroll, GameUiScene.Width, GameUiScene.Height));
        Rect a = q.GetBoundingClientRect(nodes["slot3"]), b = q.GetBoundingClientRect(nodes["slot20"]);
        var options = new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height };
        InteractivePage page = InteractivePage.Create(GameUiScene.Html(), options);
        bool flip = false;
        double t = 0;
        void Hover()
        {
            flip = !flip;
            Rect r = flip ? a : b;
            t += 16;
            page.HandlePointer(Ix.Move(r.X + (r.Width / 2), r.Y + (r.Height / 2), t));
        }

        for (int i = 0; i < 200; i++)
        {
            Hover();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            Hover();
        }

        long bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 200;
        Console.WriteLine($"html hover (pointer move through InteractivePage): {bytes} B per change");
    }

    // Typing one character into a text field on the 1,000-node HUD page: zero bytes once warm, .
    [Fact]
    public void TypingIntoAFieldOnTheHudPageAllocatesNothing()
    {
        string html = GameUiScene.Html().Replace("<body>", "<body><input id=tf value='hello' style='position:absolute;left:4px;top:4px;width:160px'>", StringComparison.Ordinal);
        var options = new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = GameUiScene.Width, ViewportHeight = GameUiScene.Height };
        InteractivePage page = InteractivePage.Create(html, options);
        RecursiveAI.OneHtml.Node field = page.Page.Document.GetElementById("tf");
        page.Focus(field, FocusReason.Script);
        page.SetSelection(field, 5, 5);
        KeyInput add = Ix.Key("a"), back = Ix.Key("Backspace");
        bool flip = false;
        void Step()
        {
            flip = !flip;
            page.HandleKey(flip ? add : back);
        }

        Allocation.AssertZero(Step, "typing a character on the HUD page", warmup: 300, iterations: 100);
    }
}
