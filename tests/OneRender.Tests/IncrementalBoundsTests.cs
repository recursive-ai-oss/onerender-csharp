using System.Threading;
using OneRender.Image;
using OneRender.TestSupport;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Hostile or huge edit streams: bounded work, a clean fallback to a full rebuild, and recovery after cancellation.</summary>
public class IncrementalBoundsTests
{
    private static LivePage NewPage(StyledTree tree) =>
        new(tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = 800, ViewportHeight = 600 });

    private static (StyledTree Tree, StyledNode[] Kids) Wide(int n, Func<int, string> css)
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", GameUiScene.Declaration("display:block;"));
        var kids = new StyledNode[n];
        for (int i = 0; i < n; i++)
        {
            kids[i] = tree.AddElement(root, "div", GameUiScene.Declaration(css(i)));
        }

        return (tree, kids);
    }

    [Fact]
    public void MoreChangedNodesThanTheLogHoldsFallBackToAFullRebuild()
    {
        const int N = StyledTree.MaxPendingChanges + 904;
        (StyledTree tree, StyledNode[] kids) = Wide(N, i => "display:block;height:10px;");
        LivePage page = NewPage(tree);
        for (int i = 0; i < N; i++)
        {
            tree.SetDeclaration(kids[i], GameUiScene.Declaration("display:block;height:" + (5 + (i % 7)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "px;background:#abc;"));
        }

        Assert.True(tree.PendingOverflowed);
        Assert.True(tree.PendingCount <= StyledTree.MaxPendingChanges);
        page.Flush();
        Assert.False(tree.PendingOverflowed);
        Assert.Equal(0, tree.PendingCount);

        (StyledTree freshTree, _) = Wide(N, i => "display:block;height:" + (5 + (i % 7)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "px;background:#abc;");
        Assert.Equal(EditSession.Snapshot(NewPage(freshTree)), EditSession.Snapshot(page));

        // The page keeps working incrementally afterwards.
        tree.SetDeclaration(kids[3], GameUiScene.Declaration("display:block;height:40px;"));
        FlushResult r = page.Flush();
        Assert.NotEqual(FlushWork.None, r.Work);
    }

    [Fact]
    public void AHugeInsertionIsOneCleanFlush()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", GameUiScene.Declaration("display:block;"));
        LivePage page = NewPage(tree);
        StyledNode box = tree.InsertElement(root, default, "div", GameUiScene.Declaration("display:block;"));
        for (int i = 0; i < 6000; i++)
        {
            StyledNode kid = tree.InsertElement(box, default, "div", GameUiScene.Declaration("display:block;height:3px;"));
            tree.InsertText(kid, default, "x");
        }

        page.Flush();
        var freshTree = new StyledTree();
        StyledNode fr = freshTree.AddRoot("div", GameUiScene.Declaration("display:block;"));
        StyledNode fb = freshTree.AddElement(fr, "div", GameUiScene.Declaration("display:block;"));
        for (int i = 0; i < 6000; i++)
        {
            StyledNode kid = freshTree.AddElement(fb, "div", GameUiScene.Declaration("display:block;height:3px;"));
            freshTree.AddText(kid, "x");
        }

        Assert.Equal(EditSession.Snapshot(NewPage(freshTree)), EditSession.Snapshot(page));
    }

    [Fact]
    public void ADepthLimitStopsInsertionsWithoutBreakingTheFlush()
    {
        var tree = new StyledTree(limits: new TreeLimits { MaxDepth = 50 });
        StyledNode cur = tree.AddRoot("div", GameUiScene.Declaration("display:block;"));
        LivePage page = NewPage(tree);
        int added = 0;
        for (int i = 0; i < 500; i++)
        {
            StyledNode n = tree.InsertElement(cur, default, "div", GameUiScene.Declaration("display:block;padding:1px;"));
            if (n.IsNull)
            {
                break;
            }

            cur = n;
            added++;
        }

        Assert.InRange(added, 40, 60);
        page.Flush();
        Assert.False(page.NeedsFlush);
    }

    [Fact]
    public void ACancelledFlushLeavesThePageRecoverable()
    {
        (StyledTree tree, StyledNode[] kids) = Wide(300, i => "display:block;height:10px;");
        LivePage page = NewPage(tree);
        for (int i = 0; i < 300; i += 3)
        {
            tree.SetDeclaration(kids[i], GameUiScene.Declaration("display:block;height:25px;margin:2px;"));
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => { page.Flush(cts.Token); });
        Assert.True(page.NeedsFlush);
        page.Flush();
        Assert.False(page.NeedsFlush);

        (StyledTree freshTree, _) = Wide(300, i => i % 3 == 0 ? "display:block;height:25px;margin:2px;" : "display:block;height:10px;");
        Assert.Equal(EditSession.Snapshot(NewPage(freshTree)), EditSession.Snapshot(page));
    }

    [Fact]
    public void EditsAreStillAcceptedAfterACancelledFlush()
    {
        (StyledTree tree, StyledNode[] kids) = Wide(50, i => "display:block;height:10px;");
        LivePage page = NewPage(tree);
        tree.SetDeclaration(kids[1], GameUiScene.Declaration("display:block;height:99px;"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => { page.Flush(cts.Token); });
        tree.SetDeclaration(kids[2], GameUiScene.Declaration("display:block;height:77px;"));
        page.Flush();
        (StyledTree freshTree, _) = Wide(50, i => i == 1 ? "display:block;height:99px;" : i == 2 ? "display:block;height:77px;" : "display:block;height:10px;");
        Assert.Equal(EditSession.Snapshot(NewPage(freshTree)), EditSession.Snapshot(page));
    }
}
