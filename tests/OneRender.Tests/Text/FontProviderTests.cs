using OneRender.Layout;
using OneRender.Style;
using OneRender.TestSupport;
using OneRender.Text;
using OneRender.Tree;

namespace OneRender.Tests.Text;

public class FontProviderTests
{
    private static ComputedValues Style(string css)
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", new StyleDeclaration().Set("font-family", css));
        return root.Values;
    }

    private static TextFontProvider Provider() => new(TestFonts.Defaults());

    [Fact]
    public void ComputedValuesMatchThroughTheCatalogAndGenericsMapToTheDefaultFaces()
    {
        IFontProvider fonts = Provider();
        ComputedValues sans = Style("Arial, sans-serif");
        ComputedValues serif = Style("serif");
        Assert.Equal("Liberation Sans", fonts.Resolve(in sans).PrimaryFace!.Face.FamilyName);
        Assert.Equal("Liberation Serif", fonts.Resolve(in serif).PrimaryFace!.Face.FamilyName);
        Assert.Same(fonts.Resolve(in sans), fonts.Resolve(in sans));
    }

    [Fact]
    public void WeightStyleAndSizeReachTheRequest()
    {
        var provider = Provider();
        var tree = new StyledTree();
        StyledNode n = tree.AddRoot("div", new StyleDeclaration().Set("font-family", "Carlito").Set("font-weight", "700").Set("font-style", "italic").Set("font-size", "20px"));
        ComputedValues v = n.Values;
        FontRequest r = provider.GetRequest(in v);
        Assert.Equal(700, r.Weight);
        Assert.Equal(FontStyleKind.Italic, r.Style);
        Assert.Equal(20, r.Size);
        Assert.Same(r, provider.GetRequest(in v));
        Assert.True(provider.GetMetrics(in v).Ascent > 0);
    }

    [Fact]
    public void ZeroFontSizeStillYieldsARequest()
    {
        var provider = Provider();
        var tree = new StyledTree();
        ComputedValues v = tree.AddRoot("div", new StyleDeclaration().Set("font-size", "0px")).Values;
        Assert.True(provider.GetRequest(in v).Size > 0);
        Assert.Equal(0, provider.Shape("abc", in v).Width, 1);
    }

    [Fact]
    public void WarmedProviderCallsAllocateNothing()
    {
        IFontProvider fonts = Provider();
        ComputedValues v = Style("Calibri, sans-serif");
        ShapedText first = fonts.Shape("Office fi", in v);
        Assert.True(first.GlyphCount > 0);
        Allocation.AssertZero(() =>
        {
            _ = fonts.Resolve(in v);
            _ = fonts.GetMetrics(in v);
            _ = fonts.Shape("Office fi", in v);
        }, "provider calls on warmed styles");
    }
}
