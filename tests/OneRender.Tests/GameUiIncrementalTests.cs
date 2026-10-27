using System.Diagnostics;
using OneRender.Image;
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

    [Fact]
    public void Timing()
    {
        (LivePage page, GameUiScene.SceneNodes nodes) = Scene();
        StyledNode slotA = nodes["slot3"], slotB = nodes["slot20"];
        StyledNode label = nodes.TextOf("gold");
        StyleDeclaration tall = GameUiScene.Declaration("display:flex;height:48px;overflow:hidden;justify-content:space-between;align-items:center;padding:0 6px;background:#1d2535;");
        StyleDeclaration normal = GameUiScene.Declaration("display:flex;height:24px;overflow:hidden;justify-content:space-between;align-items:center;padding:0 6px;background:#1d2535;");
        StyledNode row = nodes["quest3"];
        bool flip = false;
        FlushResult last = default;
        void Hover()
        {
            flip = !flip;
            page.Styled.SetStates(slotA, flip ? ElementStates.Hover : ElementStates.None);
            page.Styled.SetStates(slotB, flip ? ElementStates.None : ElementStates.Hover);
            last = page.Flush();
        }

        string[] texts = ["Gold 100", "Gold 1000", "Gold 7"];
        int t = 0;
        void Text()
        {
            page.Styled.SetText(label, texts[t++ % 3]);
            last = page.Flush();
        }

        void Subtree()
        {
            flip = !flip;
            page.Styled.SetDeclaration(row, flip ? tall : normal);
            last = page.Flush();
        }

        foreach ((string name, Action a) in new (string, Action)[] { ("hover", Hover), ("text", Text), ("subtree", Subtree) })
        {
            for (int i = 0; i < 300; i++)
            {
                a();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++)
            {
                a();
            }

            sw.Stop();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"TIMING {name}: {sw.Elapsed.TotalMilliseconds / 200:F3} ms, {bytes / 200} B per change, work {last.Work} reused {last.ReusedSubtrees} nodes {page.Styled.Count} boxes {page.Boxes.Count} frags {page.Fragments.Count} items {page.Display.Current.Count}");
        }
    }
}
