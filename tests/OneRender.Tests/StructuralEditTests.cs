using OneRender.Image;
using OneRender.Layout;
using OneRender.Style;
using OneRender.TestSupport;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Inserting, removing and moving nodes patches the boxes in place; scroll positions follow their elements.</summary>
public class StructuralEditTests
{
    private static StyleDeclaration D(string css) => GameUiScene.Declaration(css);

    private sealed class Scene
    {
        public readonly StyledTree Tree = new();
        public readonly StyledNode Root, Scroller, First, Second;
        public readonly LivePage Page;

        public Scene()
        {
            Root = Tree.AddRoot("div", D("display:block;width:300px;"));
            First = Tree.AddElement(Root, "div", D("display:block;height:20px;"));
            Scroller = Tree.AddElement(Root, "div", D("display:block;overflow:auto;height:50px;width:100px;"));
            Tree.AddElement(Scroller, "div", D("display:block;height:400px;"));
            Second = Tree.AddElement(Root, "div", D("display:block;height:20px;"));
            Page = new LivePage(Tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = 300, ViewportHeight = 300 });
        }

        public Box BoxOf(StyledNode n) => Page.Boxes.PrincipalBox(n);
    }

    [Fact]
    public void InsertsAndRemovalsPatchTheBoxesAndKeepTheBoxesOfUntouchedSiblings()
    {
        var s = new Scene();
        int scroller = s.BoxOf(s.Scroller).Index, second = s.BoxOf(s.Second).Index;

        StyledNode added = s.Tree.InsertElement(s.Root, s.Scroller, "div", D("display:block;height:10px;"));
        FlushResult r = s.Page.Flush();
        Assert.True((r.Work & FlushWork.BoxesRebuilt) == 0, "an insert must not generate every box again");
        Assert.True((r.Work & FlushWork.BoxesPatched) != 0);
        Assert.Equal(scroller, s.BoxOf(s.Scroller).Index);
        Assert.Equal(second, s.BoxOf(s.Second).Index);
        Assert.False(s.BoxOf(added).IsNull);
        Assert.Equal(30, s.Page.Fragments.FragmentOf(s.BoxOf(s.Scroller)).BorderBox.Y);

        s.Tree.Remove(added);
        r = s.Page.Flush();
        Assert.True((r.Work & FlushWork.BoxesRebuilt) == 0);
        Assert.Equal(20, s.Page.Fragments.FragmentOf(s.BoxOf(s.Scroller)).BorderBox.Y);
        Assert.Equal(s.Page.Boxes.ReachableCount(), s.Page.Boxes.LiveBoxes);
    }

    [Fact]
    public void ScrollOffsetsFollowTheirElementWhenSiblingsAreInsertedRemovedAndMoved()
    {
        var s = new Scene();
        s.Page.Scroll.SetOffset(s.BoxOf(s.Scroller), 0, 40);

        // Siblings before the scroller take box numbers around it.
        StyledNode a = s.Tree.InsertElement(s.Root, s.First, "div", D("display:block;height:5px;overflow:auto;"));
        StyledNode b = s.Tree.InsertElement(s.Root, s.Scroller, "div", D("display:block;height:5px;overflow:auto;"));
        s.Page.Flush();
        Assert.Equal((0.0, 40.0), s.Page.Scroll.GetOffset(s.Page.Fragments, s.BoxOf(s.Scroller)));
        Assert.Equal((0.0, 0.0), s.Page.Scroll.GetOffset(s.Page.Fragments, s.BoxOf(a)));
        Assert.Equal((0.0, 0.0), s.Page.Scroll.GetOffset(s.Page.Fragments, s.BoxOf(b)));

        s.Tree.Remove(a);
        s.Tree.Move(s.Second, s.Root, s.First);
        s.Page.Flush();
        Assert.Equal((0.0, 40.0), s.Page.Scroll.GetOffset(s.Page.Fragments, s.BoxOf(s.Scroller)));

        // A moved scroller keeps its position too.
        s.Tree.Move(s.Scroller, s.Root, s.First);
        s.Page.Flush();
        Assert.Equal((0.0, 40.0), s.Page.Scroll.GetOffset(s.Page.Fragments, s.BoxOf(s.Scroller)));

        // A removed element's position does not pass to a node that takes its slot.
        s.Page.Scroll.SetOffset(s.BoxOf(b), 0, 3);
        s.Tree.Remove(b);
        s.Page.Flush();
        StyledNode c = s.Tree.InsertElement(s.Root, default, "div", D("display:block;height:5px;overflow:auto;"));
        s.Page.Flush();
        Assert.Equal((0.0, 0.0), s.Page.Scroll.GetOffset(s.Page.Fragments, s.BoxOf(c)));
    }

    [Fact]
    public void StructuralEditsOnAWarmPageAllocateNothing()
    {
        var soak = new ChurnSoak(FontProviders.CreateDefault(), false);
        for (int i = 0; i < 500; i++)
        {
            soak.Frame();
        }

        soak.InsertLayer();
        for (int i = 0; i < 20; i++)
        {
            soak.MoveLayer();
            soak.RemoveLayer();
            soak.InsertLayer();
        }

        long best = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
            {
                soak.MoveLayer();
                soak.RemoveLayer();
                soak.InsertLayer();
            }

            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, best);
    }
}
