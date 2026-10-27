using Xunit;
namespace OneRender.Tests;
public class InsetAndClipMarginTests
{
    [Theory]
    [InlineData("inset:10px 20px 30px 40px", 40, 10)]
    [InlineData("inset:7px", 7, 7)]
    [InlineData("inset-inline-start:9px;inset-block-start:4px", 9, 4)]
    [InlineData("inset-inline:5px 6px;inset-block:2px", 5, 2)]
    public void InsetShorthandsAndLogicalInsetsReachTheComputedValues(string decl, double left, double top)
    {
        var l = PositioningTests.Run("<div id=cb style='position:relative;width:300px;height:200px'><div id=a style='position:absolute;width:1px;height:1px;" + decl + "'></div></div>");
        Assert.Equal(left, l["a"].X);
        Assert.Equal(top, l["a"].Y);
    }

    [Fact]
    public void OverflowClipMarginParses()
    {
        var l = PositioningTests.Run("<div id=a style='overflow:clip;overflow-clip-margin:content-box 5px'></div>");
        var b = l.P.Boxes.PrincipalBox(l.P.Styled.FindBySource(l.P.Document.GetElementById("a").Index));
        Assert.Equal(5, b.Values.OverflowClipMarginPx);
        Assert.Equal(OneRender.Style.ClipMarginBoxKind.ContentBox, b.Values.OverflowClipMarginBox);
    }
}
