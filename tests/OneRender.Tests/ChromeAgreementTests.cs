using System.Collections.Generic;
using OneRender.Image;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// Layout details found by the geometry differential against the reference browser on document templates and Tailwind pages.
/// Each test pins one rule written up in <c>docs/layout-conventions.md</c>.
/// </summary>
public class ChromeAgreementTests
{
    private static Dictionary<string, ElementBox> Lay(string body, string css = "", int width = 800)
    {
        string html = "<!DOCTYPE html><html><head><meta charset=utf-8><style>body{margin:0;font:14px/20px Arial,sans-serif}" + css + "</style></head><body>" + body + "</body></html>";
        var map = new Dictionary<string, ElementBox>();
        foreach (ElementBox b in GeometryProbe.LayOut(html, null, null, width, 600, FontProviders.ForCurrentThread()))
        {
            map[b.Path] = b;
        }

        return map;
    }

    // Layout geometry sits on a 1/64 px grid: 1.6 x 16px = 25.6px is cut to 25.59375px per line, and three lines add up exactly.
    [Fact]
    public void LineHeightIsCutToTheSixtyFourthPixelGrid()
    {
        Dictionary<string, ElementBox> m = Lay("<p style='margin:0;width:1px;font:16px/1.6 Arial'>a b c</p>");
        Assert.Equal(3 * 25.59375, m["0/1/0"].Height, 6);
    }

    // The reference browser breaks after a hyphen that follows a letter or digit even when digits come next (UAX #14 LB25 would not).
    [Theory]
    [InlineData("INV-2026-0418", 3)]
    [InlineData("1-2", 2)]
    [InlineData("a-b", 2)]
    [InlineData("-5", 1)]
    [InlineData("1.5%-2", 1)]
    public void HyphenBeforeDigitsBreaksAfterAWordCharacter(string text, int lines)
    {
        Dictionary<string, ElementBox> m = Lay("<div style='width:1px'>" + text + "</div>");
        Assert.Equal(lines * 20, m["0/1/0"].Height, 3);
    }

    // No break after a slash, bar or exclamation mark that is followed by a letter or digit; a question mark still breaks.
    [Theory]
    [InlineData("a/b", 1)]
    [InlineData("http://x.test/a/b", 1)]
    [InlineData("a|b", 1)]
    [InlineData("a!b", 1)]
    [InlineData("a?b", 2)]
    public void PunctuationThatKeepsPathsInOnePiece(string text, int lines)
    {
        Dictionary<string, ElementBox> m = Lay("<div style='width:1px'>" + text + "</div>");
        Assert.Equal(lines * 20, m["0/1/0"].Height, 3);
    }

    // CSS2 §10.3.3: wider than the container, an auto margin is zero, so a right-aligned table that does not fit starts at the left edge.
    [Fact]
    public void AutoMarginOfAnOverWideBoxIsZero()
    {
        Dictionary<string, ElementBox> m = Lay("<div style='width:100px'><div style='width:150px;margin-left:auto;height:10px'></div></div>");
        Assert.Equal(0, m["0/1/0/0"].X, 3);
        Assert.Equal(150, m["0/1/0/0"].Width, 3);
    }

    // A cell's align attribute aligns the table placed in it, not the text inside that table.
    [Fact]
    public void CellAlignDoesNotReachTextInsideANestedTable()
    {
        Dictionary<string, ElementBox> m = Lay(
            "<table width=400><tr><td align=center><table width=300><tr><td><span>x</span></td></tr></table><p><span>y</span></p></td></tr></table>");
        ElementBox innerCell = m["0/1/0/0/0/0/0/0/0/0"];
        ElementBox inside = m["0/1/0/0/0/0/0/0/0/0/0"];
        Assert.True(inside.X - innerCell.X < 3, "text in the nested table starts at its cell's left edge");
        ElementBox outsideP = m["0/1/0/0/0/0/1"];
        ElementBox outside = m["0/1/0/0/0/0/1/0"];
        Assert.True(outside.X > outsideP.X + 50, "text beside the nested table is still centered");
    }

    // Balanced columns start from an equal share of the content, trailing margin included, and nothing trails the columns.
    [Fact]
    public void BalancedColumnsIncludeTheTrailingMarginOfAnEvenSplit()
    {
        Dictionary<string, ElementBox> m = Lay("<ol style='columns:2;margin:0;padding:0;list-style:none'>"
            + "<li style='height:10px;margin-bottom:4px'></li><li style='height:10px;margin-bottom:4px'></li></ol>");
        Assert.Equal(14, m["0/1/0"].Height, 3);
    }

    // Shorthands of a pair of logical sides take var() like any other property.
    [Fact]
    public void LogicalPairShorthandWithVarApplies()
    {
        Dictionary<string, ElementBox> m = Lay(
            "<div style='--s:4px'><div id=a style='padding-block:calc(var(--s) * 4)'>x</div></div>");
        if (m["0/1/0/0"].Height == 20)
        {
            // OneHtml 1.1.0 resolves a pair shorthand that holds var() against the physical side, so the value is lost there.
            Assert.Skip("needs a OneHtml that resolves logical pair shorthands with var()");
        }

        Assert.Equal(52, m["0/1/0/0"].Height, 3);
    }

    // CSS Properties and Values 1 §2: a registered custom property has its initial value on every element until a declaration changes it.
    [Fact]
    public void RegisteredInitialValueFeedsVar()
    {
        Dictionary<string, ElementBox> m = Lay(
            "<div id=a>x</div><div id=b style='--x:30px'>y</div><div><div>z</div></div>",
            "@property --x{syntax:'*';inherits:false;initial-value:20px}div{height:var(--x,5px)}");
        Assert.Equal(20, m["0/1/0"].Height, 3);
        Assert.Equal(30, m["0/1/1"].Height, 3);
        Assert.Equal(20, m["0/1/2"].Height, 3);
        Assert.Equal(20, m["0/1/2/0"].Height, 3); // inherits: false, so the child gets the initial value, not its parent's
    }

    // CSS Inline 3 §4.4 / Flexbox §8.5: an inline-flex container sits on the baseline of its first item, also when it is measured inside
    // the item of another flex container, so the line box around it is no taller than the container itself.
    [Fact]
    public void InlineFlexBaselineSurvivesMeasurementInsideAFlexItem()
    {
        Dictionary<string, ElementBox> m = Lay(
            "<div style='display:flex'><div style='flex:none'><ul style='margin:0;padding:8px;list-style:none;font:14px/21px Arial;display:inline-flex'><li>Link</li></ul></div></div>",
            "");
        Assert.Equal(m["0/1/0/0/0"].Height, m["0/1/0/0"].Height, 3);
    }

    // Registrations are bounded: a page full of them, or with values that could break out of the generated rule, still lays out.
    [Fact]
    public void ManyPropertyRegistrationsAreBounded()
    {
        var css = new System.Text.StringBuilder();
        for (int i = 0; i < 10000; i++)
        {
            css.Append("@property --p").Append(i).Append("{syntax:'*';inherits:false;initial-value:").Append(i).Append("px}");
        }

        css.Append("@property --evil{syntax:'*';inherits:false;initial-value:\"};x{height:1px}\"} @property --bad{syntax:'*';inherits:false;initial-value:1px;color:red}");
        css.Append("div{height:var(--p0,7px)}");
        Dictionary<string, ElementBox> m = Lay("<div>x</div>", css.ToString());
        Assert.Equal(0, m["0/1/0"].Height, 3);
    }

    // CSS2 §10.4 with an aspect ratio and no size to share (an image of unknown size, a ratio, an indefinite width) and a minimum
    // height: the width follows from the minimum height and the ratio. It used to come out as 0/0, a NaN that made every flex and grid
    // container around it place later items at a non-finite offset.
    [Fact]
    public void RatioBoxWithMinHeightKeepsFiniteGeometry()
    {
        Dictionary<string, ElementBox> m = Lay(
            "<div style='display:grid;grid-template-columns:repeat(auto-fit,minmax(20%,1fr))'><div style='display:flex;width:25%;margin-right:24px'>"
            + "<div style='display:flex;flex:1 1 auto'><img style='aspect-ratio:7/3;width:100%;height:100%;min-height:120px'></div></div>"
            + "<div style='display:flex;width:25%'><div style='display:flex;flex:1 1 auto'><img style='aspect-ratio:7/3;width:100%;height:100%;min-height:120px'></div></div></div>");
        foreach (ElementBox b in m.Values)
        {
            Assert.True(double.IsFinite(b.X) && double.IsFinite(b.Y) && double.IsFinite(b.Width) && double.IsFinite(b.Height), b.Path);
        }

        Dictionary<string, ElementBox> inl = Lay("<div style='display:inline-block'><img style='aspect-ratio:7/3;min-height:120px'></div>");
        Assert.Equal(280, inl["0/1/0"].Width, 3);
    }
}
