using System;
using OneFont;
using OneRender.Layout;
using OneRender.Text;
using OneRender.Style;
using OneRender.Tests.Support;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>Flex layout (CSS Flexible Box Layout 1): the algorithm of §9, automatic minimum sizes, alignment, gaps, nesting and limits.</summary>
public class FlexLayoutTests
{
    private sealed class NoFonts : IFontProvider
    {
        public FontCatalog Catalog => throw new NotSupportedException();

        public ResolvedFontList Resolve(in ComputedValues style) => throw new NotSupportedException();

        public ShapedText Shape(ReadOnlySpan<char> text, in ComputedValues style, bool rightToLeft = false) => throw new NotSupportedException();

        public FontMetricsInfo GetMetrics(in ComputedValues style) => throw new NotSupportedException();
    }

    private sealed class FixedImages(double w, double h) : IResourceResolver
    {
        public bool TryGetNaturalSize(ReplacedKind kind, ReadOnlySpan<char> source, out double width, out double height)
        {
            width = w;
            height = h;
            return true;
        }
    }

    internal sealed class Lay
    {
        public Pipeline P = null!;
        public FragmentTree F = null!;
        public BlockLayoutEngine Engine = null!;

        public Fragment Frag(string id) => F.FragmentOf(P.Boxes.PrincipalBox(P.Styled.FindBySource(P.Document.GetElementById(id).Index)));

        public Rect this[string id] => F.AbsoluteBorderBox(Frag(id));

        public Box BoxOf(string id) => P.Boxes.PrincipalBox(P.Styled.FindBySource(P.Document.GetElementById(id).Index));
    }

    internal static Lay Run(string body, string css = "", double width = 800, IResourceResolver? images = null)
    {
        string html = "<!DOCTYPE html><html><head><style>body{margin:0}" + css + "</style></head><body>" + body + "</body></html>";
        Pipeline p = Pipeline.FromHtml(html, width, 600, resolver: images);
        var f = new FragmentTree(p.Boxes);
        var engine = new BlockLayoutEngine();
        engine.Layout(p.Boxes, new LayoutInput(width, double.NaN, new NoFonts()), f);
        return new Lay { P = p, F = f, Engine = engine };
    }

    private static string Item(string id, string style) => "<div id=" + id + " style='" + style + "'></div>";

    private static string Flex(string style, params string[] items) => "<div id=c style='display:flex;" + style + "'>" + string.Concat(items) + "</div>";

    // ---- §9.7 resolving flexible lengths ----

    [Fact]
    public void ItemsWithAutoWidthTakeTheirContentWidthAndStartAtTheMainStart()
    {
        Lay l = Run(Flex("", Item("a", "width:100px;height:20px"), Item("b", "width:50px;height:30px")));
        Assert.Equal(new Rect(0, 0, 100, 20), l["a"]);
        Assert.Equal(new Rect(100, 0, 50, 30), l["b"]);
        Assert.Equal(new Rect(0, 0, 800, 30), l["c"]);
    }

    [Fact]
    public void GrowDistributesFreeSpaceInProportionToTheFactors()
    {
        Lay l = Run(Flex("width:400px", Item("a", "width:100px;height:10px;flex-grow:1"), Item("b", "width:100px;height:10px;flex-grow:3")));
        Assert.Equal(150, l["a"].Width, 6);
        Assert.Equal(250, l["b"].Width, 6);
        Assert.Equal(150, l["b"].X, 6);
    }

    [Fact]
    public void ShrinkIsWeightedByTheFlexBaseSize()
    {
        // 300 + 100 = 400 into 300: a loses 75, b loses 25.
        Lay l = Run(Flex("width:300px", Item("a", "width:300px;height:10px"), Item("b", "width:100px;height:10px")));
        Assert.Equal(225, l["a"].Width, 6);
        Assert.Equal(75, l["b"].Width, 6);
    }

    [Fact]
    public void AMaxWidthViolationFreezesTheItemAndTheRestShareWhatIsLeft()
    {
        // Flexbox §9.7 step 4: a is clamped to 100, so b and c share the other 300.
        Lay l = Run(Flex("width:400px", Item("a", "flex:1 1 0;max-width:100px;height:10px"), Item("b", "flex:1 1 0;height:10px"), Item("c3", "flex:1 1 0;height:10px")));
        Assert.Equal(100, l["a"].Width, 6);
        Assert.Equal(150, l["b"].Width, 6);
        Assert.Equal(150, l["c3"].Width, 6);
    }

    [Fact]
    public void AMinWidthViolationOnShrinkFreezesTheItem()
    {
        Lay l = Run(Flex("width:300px", Item("a", "width:200px;min-width:180px;height:10px"), Item("b", "width:200px;height:10px")));
        Assert.Equal(180, l["a"].Width, 6);
        Assert.Equal(120, l["b"].Width, 6);
    }

    [Fact]
    public void FlexGrowBelowOneUsesOnlyThatFractionOfTheFreeSpace()
    {
        Lay l = Run(Flex("width:400px", Item("a", "width:100px;height:10px;flex-grow:.5")));
        Assert.Equal(250, l["a"].Width, 6);
    }

    [Fact]
    public void FlexBasisPercentAndContentAreUsed()
    {
        Lay l = Run(Flex("width:400px", Item("a", "flex-basis:25%;height:10px;width:10px"), Item("b", "flex-basis:content;width:60px;height:10px")));
        Assert.Equal(100, l["a"].Width, 6);
        Assert.Equal(0, l["b"].Width, 6);
    }

    [Fact]
    public void BoxSizingAndPaddingStayOutsideTheFlexibleSize()
    {
        Lay l = Run(Flex("width:300px", Item("a", "flex:1 1 0;padding:0 10px;height:10px"), Item("b", "flex:1 1 0;height:10px;box-sizing:border-box;padding:0 10px")));
        Assert.Equal(150, l["a"].Width, 6);
        Assert.Equal(150, l["b"].Width, 6);
    }

    // ---- §5 directions, §9.3 lines ----

    [Fact]
    public void RowReversePacksFromTheRight()
    {
        Lay l = Run(Flex("flex-direction:row-reverse;width:300px", Item("a", "width:50px;height:10px"), Item("b", "width:70px;height:10px")));
        Assert.Equal(250, l["a"].X, 6);
        Assert.Equal(180, l["b"].X, 6);
    }

    [Fact]
    public void RtlRowStartsAtTheRightEdge()
    {
        Lay l = Run(Flex("direction:rtl;width:300px", Item("a", "width:50px;height:10px"), Item("b", "width:70px;height:10px")));
        Assert.Equal(250, l["a"].X, 6);
        Assert.Equal(180, l["b"].X, 6);
    }

    [Fact]
    public void ColumnStacksAndTheContainerHeightIsTheSum()
    {
        Lay l = Run(Flex("flex-direction:column", Item("a", "height:30px"), Item("b", "height:20px;width:100px")));
        Assert.Equal(new Rect(0, 0, 800, 30), l["a"]);
        Assert.Equal(new Rect(0, 30, 100, 20), l["b"]);
        Assert.Equal(50, l["c"].Height, 6);
    }

    [Fact]
    public void ColumnReverseStartsAtTheBottom()
    {
        Lay l = Run(Flex("flex-direction:column-reverse;height:100px", Item("a", "height:30px;width:10px"), Item("b", "height:20px;width:10px")));
        Assert.Equal(70, l["a"].Y, 6);
        Assert.Equal(50, l["b"].Y, 6);
    }

    [Fact]
    public void OrderReordersTheItemsStably()
    {
        Lay l = Run(Flex("width:300px", Item("a", "width:50px;height:10px;order:2"), Item("b", "width:50px;height:10px"), Item("c2", "width:50px;height:10px;order:-1"), Item("d", "width:50px;height:10px")));
        Assert.Equal(0, l["c2"].X, 6);
        Assert.Equal(50, l["b"].X, 6);
        Assert.Equal(100, l["d"].X, 6);
        Assert.Equal(150, l["a"].X, 6);
    }

    [Fact]
    public void WrapBreaksIntoLinesAndLineHeightsFollowTheTallestItem()
    {
        Lay l = Run(Flex("flex-wrap:wrap;width:250px", Item("a", "width:100px;height:10px"), Item("b", "width:100px;height:30px"), Item("c2", "width:100px;height:20px")));
        Assert.Equal(new Rect(0, 0, 100, 10), l["a"]);
        Assert.Equal(new Rect(100, 0, 100, 30), l["b"]);
        Assert.Equal(new Rect(0, 30, 100, 20), l["c2"]);
        Assert.Equal(50, l["c"].Height, 6);
    }

    [Fact]
    public void WrapReverseStacksLinesFromTheCrossEnd()
    {
        Lay l = Run(Flex("flex-wrap:wrap-reverse;width:150px;height:100px;align-content:flex-start", Item("a", "width:100px;height:10px"), Item("b", "width:100px;height:30px")));
        Assert.Equal(90, l["a"].Y, 6);
        Assert.Equal(60, l["b"].Y, 6);
    }

    [Fact]
    public void AlignContentStretchSharesTheFreeCrossSpaceBetweenLines()
    {
        Lay l = Run(Flex("flex-wrap:wrap;width:150px;height:100px", Item("a", "width:100px;height:10px"), Item("b", "width:100px;height:10px")));
        // Two lines of 10 in 100: each grows to 50; the items stretch only when they have no height.
        Assert.Equal(0, l["a"].Y, 6);
        Assert.Equal(50, l["b"].Y, 6);
    }

    [Fact]
    public void GapsSeparateItemsAndLines()
    {
        Lay l = Run(Flex("flex-wrap:wrap;width:230px;column-gap:20px;row-gap:5px", Item("a", "width:100px;height:10px"), Item("b", "width:100px;height:10px"), Item("c2", "width:100px;height:10px")));
        Assert.Equal(120, l["b"].X, 6);
        Assert.Equal(0, l["c2"].X, 6);
        Assert.Equal(15, l["c2"].Y, 6);
    }

    // ---- §8 alignment ----

    [Theory]
    [InlineData("flex-start", 0, 10)]
    [InlineData("flex-end", 280, 290)]
    [InlineData("center", 140, 150)]
    [InlineData("space-between", 0, 290)]
    [InlineData("space-around", 70, 220)]
    [InlineData("space-evenly", 93.333333333, 196.666666667)]
    public void JustifyContent(string value, double ax, double bx)
    {
        Lay l = Run(Flex("width:300px;justify-content:" + value, Item("a", "width:10px;height:10px"), Item("b", "width:10px;height:10px")));
        Assert.Equal(ax, l["a"].X, 5);
        Assert.Equal(bx, l["b"].X, 5);
    }

    [Fact]
    public void SpaceBetweenFallsBackToStartWithNegativeFreeSpaceAndSafeCenterDoesNotOverflowTheStart()
    {
        Lay l = Run(Flex("width:100px;justify-content:space-between", Item("a", "width:80px;height:10px;flex-shrink:0"), Item("b", "width:80px;height:10px;flex-shrink:0")));
        Assert.Equal(0, l["a"].X, 6);
        Assert.Equal(80, l["b"].X, 6);
        Lay u = Run(Flex("width:100px;justify-content:center", Item("a", "width:200px;height:10px;flex-shrink:0")));
        Assert.Equal(-50, u["a"].X, 6);
        Lay s = Run(Flex("width:100px;justify-content:safe center", Item("a", "width:200px;height:10px;flex-shrink:0")));
        Assert.Equal(0, s["a"].X, 6);
    }

    [Theory]
    [InlineData("stretch", 0, 40)]
    [InlineData("flex-start", 0, 10)]
    [InlineData("flex-end", 30, 10)]
    [InlineData("center", 15, 10)]
    public void AlignItems(string value, double y, double h)
    {
        Lay l = Run(Flex("height:40px;align-items:" + value, "<div id=a style='width:10px" + (value == "stretch" ? "" : ";height:10px") + "'></div>"));
        Assert.Equal(y, l["a"].Y, 6);
        Assert.Equal(h, l["a"].Height, 6);
    }

    [Fact]
    public void AlignSelfOverridesAlignItems()
    {
        Lay l = Run(Flex("height:40px;align-items:flex-end", Item("a", "width:10px;height:10px"), Item("b", "width:10px;height:10px;align-self:flex-start")));
        Assert.Equal(30, l["a"].Y, 6);
        Assert.Equal(0, l["b"].Y, 6);
    }

    [Fact]
    public void AutoMarginsAbsorbFreeSpaceBeforeJustifyContent()
    {
        Lay l = Run(Flex("width:300px;justify-content:flex-end", Item("a", "width:50px;height:10px;margin-right:auto"), Item("b", "width:50px;height:10px")));
        Assert.Equal(0, l["a"].X, 6);
        Assert.Equal(250, l["b"].X, 6);
        Lay m = Run(Flex("width:300px;height:100px", Item("a", "width:50px;height:10px;margin:auto")));
        Assert.Equal(125, m["a"].X, 6);
        Assert.Equal(45, m["a"].Y, 6);
    }

    // ---- §4.5 automatic minimum size ----

    [Fact]
    public void AReplacedItemCannotShrinkBelowItsNaturalWidth()
    {
        Lay l = Run(Flex("width:100px", "<img id=a src=x style='height:20px'>", Item("b", "width:100px;height:10px")), images: new FixedImages(80, 20));
        Assert.True(l["a"].Width >= 80 - 1e-6, "min-width:auto keeps the natural width, got " + l["a"].Width);
    }

    [Fact]
    public void ScrollContainersHaveNoContentMinimum()
    {
        Lay l = Run(Flex("width:100px", "<div id=a style='overflow:hidden;width:200px;height:10px'></div>"));
        Assert.Equal(100, l["a"].Width, 6);
    }

    [Fact]
    public void ADefiniteWidthCapsTheAutomaticMinimum()
    {
        // Flexbox §4.5: min(content size suggestion, specified size suggestion).
        Lay l = Run(Flex("width:50px", "<div id=a style='width:200px;height:10px'><div style='width:30px;height:5px'></div></div>"));
        Assert.Equal(50, l["a"].Width, 6);
        Lay m = Run(Flex("width:50px", "<div id=a style='width:200px;height:10px'><div style='width:80px;height:5px'></div></div>"));
        Assert.Equal(80, m["a"].Width, 6);
    }

    // ---- column containers and cross sizes ----

    [Fact]
    public void ColumnFlexGrowFillsADefiniteHeight()
    {
        Lay l = Run(Flex("flex-direction:column;height:100px", Item("a", "flex:1;width:10px"), Item("b", "height:20px;width:10px")));
        Assert.Equal(80, l["a"].Height, 6);
        Assert.Equal(80, l["b"].Y, 6);
    }

    [Fact]
    public void ColumnItemsWithoutWidthStretchAcrossTheContainer()
    {
        Lay l = Run(Flex("flex-direction:column;width:300px", Item("a", "height:10px"), Item("b", "height:10px;align-self:center;width:50px")));
        Assert.Equal(300, l["a"].Width, 6);
        Assert.Equal(125, l["b"].X, 6);
    }

    [Fact]
    public void PercentageHeightsOfItemsResolveAgainstADefiniteContainerHeight()
    {
        Lay l = Run(Flex("height:200px", "<div id=a style='width:10px'><div id=in style='height:50%'></div></div>"));
        Assert.Equal(200, l["a"].Height, 6);
        Assert.Equal(100, l["in"].Height, 6);
    }

    // ---- nesting, inline-flex, the container's own box ----

    [Fact]
    public void NestedFlexContainersLayOutAsItems()
    {
        Lay l = Run("<div id=c style='display:flex;width:400px'><div id=in style='display:flex;flex:1'><div id=a style='width:50px;height:20px'></div><div id=b style='flex:1;height:10px'></div></div><div id=z style='width:100px;height:5px'></div></div>");
        Assert.Equal(300, l["in"].Width, 6);
        Assert.Equal(250, l["b"].Width, 6);
        Assert.Equal(20, l["in"].Height, 6);
        Assert.Equal(20, l["c"].Height, 6);
    }

    [Fact]
    public void AnInlineFlexContainerShrinkWrapsItsItems()
    {
        Lay l = Run("<div id=o style='width:500px'><div id=c style='display:flex;float:left'><div style='width:30px;height:10px'></div><div style='width:40px;height:10px'></div></div></div>");
        Assert.Equal(70, l["c"].Width, 6);
    }

    [Fact]
    public void AFlexContainerEstablishesAFormattingContextAndBlockifiesItsChildren()
    {
        Lay l = Run("<div id=c style='display:flex;width:300px'><span id=a style='width:50px;height:10px'></span><span id=b style='float:left;width:60px;height:10px'></span></div>");
        Assert.Equal(new Rect(0, 0, 50, 10), l["a"]);
        Assert.Equal(new Rect(50, 0, 60, 10), l["b"]);
    }

    [Fact]
    public void ContainerPaddingAndBorderOffsetTheItems()
    {
        Lay l = Run(Flex("width:200px;padding:5px;border:2px solid;justify-content:flex-end", Item("a", "width:50px;height:10px")));
        Assert.Equal(157, l["a"].X, 6);
        Assert.Equal(7, l["a"].Y, 6);
        Assert.Equal(24, l["c"].Height, 6);
    }

    [Fact]
    public void BaselineAlignmentLinesUpFirstBaselinesOfBlocksWithoutText()
    {
        // Boxes with no line boxes synthesize the baseline from their bottom border edge.
        Lay l = Run(Flex("align-items:baseline", Item("a", "width:10px;height:10px"), Item("b", "width:10px;height:30px")));
        Assert.Equal(20, l["a"].Y, 6);
        Assert.Equal(0, l["b"].Y, 6);
    }

    // ---- intrinsic sizes (§9.9) ----

    [Fact]
    public void IntrinsicSizesOfRowAndColumnContainers()
    {
        Lay l = Run("<div id=f style='float:left'><div id=r style='display:flex'><div style='width:30px;height:5px'></div><div style='width:40px;height:5px;margin-left:10px'></div></div></div>"
            + "<div id=g style='float:left'><div style='display:flex;flex-direction:column'><div style='width:30px;height:5px'></div><div style='width:70px;height:5px'></div></div></div>");
        Assert.Equal(80, l["f"].Width, 6);
        Assert.Equal(70, l["g"].Width, 6);
    }

    [Fact]
    public void GapsCountInTheIntrinsicWidth()
    {
        Lay l = Run("<div id=f style='float:left'><div style='display:flex;column-gap:7px'><div style='width:30px;height:5px'></div><div style='width:40px;height:5px'></div></div></div>");
        Assert.Equal(77, l["f"].Width, 6);
    }

    // ---- absolutely positioned children (Flexbox §4.1) ----

    [Fact]
    public void AbsolutelyPositionedChildrenAreAlignedAsTheOnlyFlexItem()
    {
        Lay l = Run("<div id=c style='display:flex;position:relative;width:200px;height:100px;justify-content:center;align-items:flex-end'><div id=p style='position:absolute;width:10px;height:20px'></div></div>");
        Assert.Equal(new Rect(95, 80, 10, 20), l["p"]);
    }

    [Fact]
    public void AbsolutelyPositionedChildrenTakeNoSpaceAmongTheItems()
    {
        Lay l = Run(Flex("position:relative;width:300px", Item("a", "width:50px;height:10px"), Item("p", "position:absolute;width:70px;height:5px"), Item("b", "width:50px;height:10px")));
        Assert.Equal(50, l["b"].X, 6);
        Assert.Equal(0, l["p"].X, 6);
    }

    [Fact]
    public void InsetsOverrideTheStaticPosition()
    {
        Lay l = Run("<div id=c style='display:flex;position:relative;width:200px;height:100px;justify-content:center'><div id=p style='position:absolute;left:5px;top:7px;width:10px;height:20px'></div></div>");
        Assert.Equal(new Rect(5, 7, 10, 20), l["p"]);
    }
}
