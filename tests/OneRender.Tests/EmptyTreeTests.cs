using OneRender.Image;
using OneRender.Style;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>A page over a styled tree with no nodes is an empty page, and grows normally once nodes arrive.</summary>
public class EmptyTreeTests
{
    [Fact]
    public void LivePageOverAnEmptyTreePaintsNothingAndAcceptsALaterRoot()
    {
        var tree = new StyledTree();
        var page = new LivePage(tree, new LivePageOptions(FontProviders.CreateDefault()) { ViewportWidth = 100, ViewportHeight = 100 });
        Assert.Equal(0, page.Display.Current.Count);

        StyledNode root = tree.AddRoot("div", new StyleDeclaration().Set("display", "block").Set("height", "10px"));
        Assert.True(root.IsValid);
        FlushResult result = page.Flush();
        Assert.True(result.Repainted);
        Assert.True(page.Fragments.Count > 0);
    }

    [Fact]
    public void InitialValuesKeepTheirSharedObjectsAcrossCollections()
    {
        // The initial values must not be a large static struct (see ComputedValues.Initial).
        GC.Collect();
        GC.Collect();
        Assert.Same(MaskSpec.None, ComputedValues.Initial.Mask);
    }
}
