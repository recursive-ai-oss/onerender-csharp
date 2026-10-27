using OneRender.Image;
using OneRender.Layout;
using OneRender.Style;
using OneRender.TestSupport;
using OneRender.Tests.Support;

namespace OneRender.Tests;

/// <summary>Columns through the whole pipeline: column rules, backgrounds that continue across columns, and the shorthands.</summary>
public class MulticolPaintTests
{
    private const string Reset = "<!DOCTYPE html><html><body style='margin:0'>";

    private static RgbaImage Render(string body, int size = 64) =>
        HtmlRender.ToImage(Reset + body, new HtmlRenderOptions { Width = size, Height = size });

    private static (byte R, byte G, byte B) Px(RgbaImage img, int x, int y)
    {
        int o = ((y * img.Width) + x) * 4;
        return (img.Pixels[o], img.Pixels[o + 1], img.Pixels[o + 2]);
    }

    private static readonly (byte, byte, byte) Red = (255, 0, 0);
    private static readonly (byte, byte, byte) Green = (0, 128, 0);
    private static readonly (byte, byte, byte) Blue = (0, 0, 255);
    private static readonly (byte, byte, byte) White = (255, 255, 255);

    [Fact]
    public void ColumnRuleIsDrawnInTheMiddleOfTheGap()
    {
        RgbaImage img = Render("<div style='column-count:2;column-gap:20px;column-rule:4px solid green;width:60px'>"
            + "<div style='height:20px;background:red;break-after:column;break-inside:avoid'></div>"
            + "<div style='height:20px;background:blue;break-inside:avoid'></div></div>");
        Assert.Equal(Red, Px(img, 10, 10));
        Assert.Equal(Blue, Px(img, 50, 10));
        Assert.Equal(Green, Px(img, 30, 10)); // 28..32
        Assert.Equal(White, Px(img, 24, 10));
        Assert.Equal(White, Px(img, 36, 10));
        Assert.Equal(White, Px(img, 30, 30)); // below the columns
    }

    [Fact]
    public void NoRuleIsDrawnWithoutAStyleOrWithOnlyOneColumn()
    {
        RgbaImage none = Render("<div style='column-count:2;column-gap:20px;column-rule-width:4px;column-rule-color:green;width:60px'><div style='height:20px;background:red;break-after:column'></div><div style='height:20px;background:blue'></div></div>");
        Assert.Equal(White, Px(none, 30, 10));
        RgbaImage single = Render("<div style='column-count:2;column-gap:20px;column-rule:4px solid green;width:60px'><div style='height:20px;background:red;break-inside:avoid'></div></div>");
        Assert.Equal(White, Px(single, 30, 10));
    }

    [Fact]
    public void ABackgroundContinuesIntoTheNextColumn()
    {
        RgbaImage img = Render("<div style='column-count:2;column-gap:0;column-fill:auto;height:40px;width:40px'><div style='height:60px;background:red'></div></div>");
        Assert.Equal(Red, Px(img, 10, 30));
        Assert.Equal(Red, Px(img, 30, 10));
        Assert.Equal(White, Px(img, 30, 30));
    }

    [Fact]
    public void ColumnShorthandsExpandToTheirLonghands()
    {
        var p = Pipeline.FromHtml("<!DOCTYPE html><html><body><div id=m style='columns:3 100px;column-rule:2px dashed red'></div></body></html>");
        ComputedValues v = p.Boxes.PrincipalBox(p.Styled.FindBySource(p.Document.GetElementById("m").Index)).Values;
        Assert.Equal(3, v.ColumnCount);
        Assert.Equal(100, v.ColumnWidth.Value);
        Assert.Equal(2, v.ColumnRuleWidth);
        Assert.Equal(BorderStyleKind.Dashed, v.ColumnRuleStyle);
        Assert.Equal(1f, v.ColumnRuleColor.R);
    }

    [Fact]
    public void BreakPropertiesComputeAndInherit()
    {
        var p = Pipeline.FromHtml("<!DOCTYPE html><html><body><div id=m style='break-before:column;break-after:avoid-page;break-inside:avoid;orphans:3;widows:4;box-decoration-break:clone'><p id=c></p></div></body></html>");
        ComputedValues v = p.Boxes.PrincipalBox(p.Styled.FindBySource(p.Document.GetElementById("m").Index)).Values;
        Assert.Equal(BreakKind.Column, v.BreakBefore);
        Assert.Equal(BreakKind.AvoidPage, v.BreakAfter);
        Assert.Equal(BreakInsideKind.Avoid, v.BreakInside);
        Assert.Equal(BoxDecorationBreakKind.Clone, v.BoxDecorationBreak);
        ComputedValues c = p.Boxes.PrincipalBox(p.Styled.FindBySource(p.Document.GetElementById("c").Index)).Values;
        Assert.Equal(3, c.Orphans); // inherited
        Assert.Equal(4, c.Widows);
        Assert.Equal(BreakKind.Auto, c.BreakBefore); // not inherited
        Assert.Equal(BoxDecorationBreakKind.Slice, c.BoxDecorationBreak);
    }

    private static bool AnyDark(RgbaImage img, int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                (byte r, byte g, byte b) = Px(img, x, y);
                if (r < 128 && g < 128 && b < 128)
                {
                    return true;
                }
            }
        }

        return false;
    }

    [Fact]
    public void AnOutsideMarkerFollowsTheFirstLineIntoTheNextColumn()
    {
        // Lists 3 §3: the marker belongs to the item's first line box. Here that line is pushed into column 2, after an
        // empty block that stays at the bottom of column 1, so the bullet must not be left behind in column 1.
        RgbaImage img = Render("<div style='font-size:20px;line-height:50px;columns:2;column-fill:auto;column-gap:0;width:200px;height:100px;margin-left:50px'>"
            + "<div style='height:80px'></div><div style='display:list-item'><div style='height:10px'></div><div>x</div></div></div>", 300);
        Assert.True(AnyDark(img, 130, 0, 150, 50), "bullet beside the first line, at the top of column 2");
        Assert.False(AnyDark(img, 100, 80, 150, 100), "no bullet left at the bottom of column 1");
    }

    [Fact]
    public void ColumnRuleColorInheritTakesTheParentsValueAndCurrentColorIsTheDefault()
    {
        const string Child = "column-count:2;column-gap:10px;column-rule-width:10px;column-rule-style:solid;width:50px;font:10px/10px sans-serif;";
        const string Text = "<div style='height:10px;break-inside:avoid'>a</div><div style='height:10px;break-inside:avoid'>b</div>";
        RgbaImage inherited = Render("<div style='column-rule-color:green;color:red'><div style='column-rule-color:inherit;" + Child + "'>" + Text + "</div></div>");
        Assert.Equal(Green, Px(inherited, 25, 5));
        RgbaImage current = Render("<div style='color:blue'><div style='" + Child + "'>" + Text + "</div></div>");
        Assert.Equal(Blue, Px(current, 25, 5));
    }

    [Fact]
    public void ColumnRulesOfAMulticolInsideAnotherMulticolGetDistinctItemIds()
    {
        // Every column of the inner container carries the outer column's fragmentainer index, so ids keyed on that index collided and
        // the display list refused the second rule.
        RgbaImage img = Render("<div style='column-count:2;column-gap:0;width:120px'><div style='column-count:3;column-gap:10px;column-rule:10px solid green;font:10px/10px sans-serif'>"
            + "<div style='height:10px;break-inside:avoid'>a</div><div style='height:10px;break-inside:avoid'>b</div><div style='height:10px;break-inside:avoid'>c</div></div></div>", 128);
        bool any = false;
        for (int x = 0; x < 128 && !any; x++)
        {
            any = Px(img, x, 2) == Green;
        }

        Assert.True(any, "a green rule is drawn");
    }
}
