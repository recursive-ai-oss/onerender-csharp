using OneRender.Image;
using OneRender.Style;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Node slots of removed nodes are reused, and handles to removed nodes are detectably stale.</summary>
public class SlotReuseTests
{
    private static LivePage Page(StyledTree tree) => new(tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = 400, ViewportHeight = 300 });

    private static StyleDeclaration Block() => new StyleDeclaration().Set("display", "block");

    [Fact]
    public void ARemovedNodesSlotGoesToTheNextInsertAfterTheFlush()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", Block());
        StyledNode a = tree.AddElement(root, "div", Block());
        StyledNode text = tree.AddText(a, "hello");
        LivePage page = Page(tree);
        int slots = tree.Count;

        tree.Remove(a);
        // Until the flush the change set still refers to the nodes: they keep their slots and handles.
        Assert.True(a.IsValid);
        Assert.Equal(slots, tree.Count);
        StyledNode during = tree.AddElement(root, "div", Block());
        Assert.NotEqual(a.Index, during.Index);
        Assert.NotEqual(text.Index, during.Index);

        page.Flush();
        Assert.False(a.IsValid);
        Assert.False(text.IsValid);
        Assert.Equal(2, tree.FreeSlots);

        StyledNode reused = tree.AddElement(root, "div", Block());
        Assert.True(reused.Index == a.Index || reused.Index == text.Index);
        Assert.Equal(slots + 1, tree.Count); // `during` took one new slot; `reused` took a free one
        Assert.True(reused.IsValid);
        Assert.False(reused == a);
        Assert.False(a.IsValid);
    }

    [Fact]
    public void AStaleHandleNeverAddressesTheNodeThatReusesItsSlot()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", Block());
        StyledNode a = tree.AddElement(root, "span", Block());
        LivePage page = Page(tree);
        tree.Remove(a);
        page.Flush();
        StyledNode b = tree.AddElement(root, "p", Block());
        Assert.Equal(a.Index, b.Index);

        Assert.False(a.IsValid);
        Assert.NotEqual(a, b);
        Assert.Throws<InvalidOperationException>(() => a.TagName);
        Assert.Equal("p", b.TagName);
        Assert.Throws<ArgumentException>(() => tree.SetDeclaration(a, null));
        Assert.Throws<ArgumentException>(() => tree.Remove(a));
        Assert.Throws<ArgumentException>(() => tree.AddElement(a, "i"));
        Assert.Throws<ArgumentException>(() => tree.AddText(a, "x"));
        Assert.Throws<ArgumentException>(() => tree.Move(a, root));
        Assert.True(page.Boxes.PrincipalBox(a).IsNull);
        Assert.Null(ComputedStyleSerializer.Get(a, "color"));
        Assert.Contains("Stale", a.ToString(), StringComparison.Ordinal);
        Assert.False(default(StyledNode).IsValid);
    }

    [Fact]
    public void RemovingADescendantOfARemovedSubtreeKeepsItOutOfTheSubtreesReclaim()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", Block());
        StyledNode parent = tree.AddElement(root, "div", Block());
        StyledNode child = tree.AddElement(parent, "div", Block());
        StyledNode grandchild = tree.AddElement(child, "div", Block());
        LivePage page = Page(tree);
        tree.Remove(parent);
        tree.Remove(child); // still allowed before the flush; it leaves the removed subtree
        page.Flush();
        Assert.Equal(1, tree.LiveCount);
        Assert.Equal(3, tree.FreeSlots);
        Assert.False(grandchild.IsValid);

        // Every slot comes back exactly once.
        var seen = new HashSet<int>();
        for (int i = 0; i < 3; i++)
        {
            Assert.True(seen.Add(tree.AddElement(root, "div", Block()).Index));
        }

        Assert.Equal(4, tree.Count);
    }

    [Fact]
    public void ClearInvalidatesEveryHandle()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", Block());
        StyledNode a = tree.AddElement(root, "div", Block());
        tree.Clear();
        Assert.False(root.IsValid);
        Assert.False(a.IsValid);
        StyledNode newRoot = tree.AddRoot("div", Block());
        Assert.Equal(root.Index, newRoot.Index);
        Assert.NotEqual(root, newRoot);
        Assert.False(root.IsValid);
    }

    [Fact]
    public void TheNodeLimitBoundsTheNodesInTheTreeNotTheNodesEverMade()
    {
        var tree = new StyledTree(limits: new TreeLimits { MaxNodes = 70 });
        StyledNode root = tree.AddRoot("div", Block());
        LivePage page = Page(tree);
        StyledNode previous = default;
        for (int frame = 0; frame < 500; frame++)
        {
            if (!previous.IsNull)
            {
                tree.Remove(previous);
            }

            previous = tree.AddElement(root, "div", Block());
            for (int i = 0; i < 30; i++)
            {
                Assert.False(tree.AddElement(previous, "span", Block()).IsNull, $"frame {frame} child {i}");
            }

            page.Flush();
        }

        Assert.True(tree.Count <= 70);
        Assert.DoesNotContain(tree.Diagnostics.Items, d => d.Kind == DiagnosticKind.LimitExceeded);
    }

    [Fact]
    public void ATreeNoPageWatchesReusesSlotsAtOnce()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", Block());
        for (int round = 0; round < 1000; round++)
        {
            StyledNode n = tree.AddElement(root, "div", Block());
            tree.AddText(n, "text " + round);
            tree.Remove(n);
        }

        Assert.True(tree.Count <= 5, "slots: " + tree.Count);
    }

    [Fact]
    public void TheTextArenaStaysTheSizeOfTheLiveText()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", Block());
        LivePage page = Page(tree);
        string text = new string('x', 200);
        StyledNode previous = default;
        int length = 0;
        for (int frame = 0; frame < 3000; frame++)
        {
            if (!previous.IsNull)
            {
                tree.Remove(previous);
            }

            previous = tree.AddElement(root, "div", Block());
            for (int i = 0; i < 10; i++)
            {
                tree.AddText(tree.AddElement(previous, "span", Block()), text);
            }

            page.Flush();
            if (frame == 100)
            {
                length = tree.TextCapacity;
            }
        }

        Assert.Equal(length, tree.TextCapacity);
    }
}
