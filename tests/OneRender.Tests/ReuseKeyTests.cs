using OneRender.Image;
using OneRender.TestSupport;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// A subtree is taken from the earlier layout only when everything it was laid out against is the same. These cases change one
/// input of a clean subtree (the height its percentages resolve against, the width, the viewport) and require the fresh result.
/// </summary>
public class ReuseKeyTests
{
    private static (LivePage Page, StyledNode Root, StyledNode Mid, StyledNode Leaf, StyledNode Sibling) Build(string rootCss, string midCss, string siblingCss = "height:20px;")
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", GameUiScene.Declaration("display:block;" + rootCss));
        StyledNode mid = tree.AddElement(root, "div", GameUiScene.Declaration(midCss));
        StyledNode leaf = tree.AddElement(mid, "div", GameUiScene.Declaration("display:block;height:50%;background:#123;"));
        StyledNode p = tree.AddElement(mid, "p", GameUiScene.Declaration("display:block;margin:0;"));
        tree.AddText(p, "Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor");
        StyledNode sibling = tree.AddElement(root, "div", GameUiScene.Declaration("display:block;" + siblingCss));
        var page = new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = 800, ViewportHeight = 600 });
        return (page, root, mid, leaf, sibling);
    }

    private static void AssertSameAsFresh(LivePage live, string rootCss, string midCss, string siblingCss = "height:20px;")
    {
        var fresh = Build(rootCss, midCss, siblingCss).Page;
        Assert.Equal(EditSession.Snapshot(fresh), EditSession.Snapshot(live));
    }

    [Fact]
    public void ThePercentageBasisOfAReusableBoxIsPartOfTheKey()
    {
        const string Mid = "display:flow-root;height:50%;";
        var s = Build("height:200px;width:400px;", Mid);
        s.Page.Styled.SetDeclaration(s.Root, GameUiScene.Declaration("height:300px;width:400px;"));
        s.Page.Flush();
        AssertSameAsFresh(s.Page, "height:300px;width:400px;", Mid);
        s.Page.Styled.SetDeclaration(s.Root, GameUiScene.Declaration("height:120px;width:400px;"));
        s.Page.Flush();
        AssertSameAsFresh(s.Page, "height:120px;width:400px;", Mid);
    }

    [Fact]
    public void TheWidthOfAReusableBoxIsPartOfTheKey()
    {
        const string Mid = "display:flow-root;";
        var s = Build("width:400px;", Mid);
        s.Page.Styled.SetDeclaration(s.Root, GameUiScene.Declaration("width:150px;"));
        s.Page.Flush();
        AssertSameAsFresh(s.Page, "width:150px;", Mid);
    }

    [Fact]
    public void ACleanSubtreeIsReusedWhenOnlyASiblingChanges()
    {
        const string Mid = "display:flow-root;height:100px;";
        var s = Build("width:400px;", Mid);
        s.Page.Styled.SetDeclaration(s.Sibling, GameUiScene.Declaration("display:block;height:30px;"));
        s.Page.Flush();
        s.Page.Styled.SetDeclaration(s.Sibling, GameUiScene.Declaration("display:block;height:35px;"));
        FlushResult r = s.Page.Flush();
        Assert.True(r.ReusedSubtrees > 0, "the clean subtree should have been taken from the earlier layout");
        AssertSameAsFresh(s.Page, "width:400px;", Mid, "height:35px;");
    }

    [Fact]
    public void AResizeDoesNotReuseAnythingLaidOutAtAnotherViewport()
    {
        const string Mid = "display:flow-root;height:20vh;";
        var s = Build("width:50vw;", Mid);
        s.Page.Resize(500, 300);
        s.Page.Flush();
        var fresh = Build("width:50vw;", Mid).Page;
        fresh.Resize(500, 300);
        fresh.Flush();
        Assert.Equal(EditSession.Snapshot(fresh), EditSession.Snapshot(s.Page));
    }
}
