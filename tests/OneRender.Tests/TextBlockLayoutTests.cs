using System.Collections.Generic;
using OneRender.Image;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// Layout details that plain text-and-block pages depend on and that show up as geometry differences against a browser:
/// the default body margin, the monospace default size, inheritance of relative font sizes and of <c>white-space</c>, and
/// floats cleared by empty blocks. Pages go through the whole front half (<see cref="GeometryProbe"/>), so the user-agent
/// rules apply.
/// </summary>
public class TextBlockLayoutTests
{
    private static Dictionary<string, ElementBox> Lay(string body, string css = "", int width = 800)
    {
        string html = "<!DOCTYPE html><html><head><meta charset=utf-8><style>" + css + "</style></head><body>" + body + "</body></html>";
        var map = new Dictionary<string, ElementBox>();
        foreach (ElementBox b in GeometryProbe.LayOut(html, null, null, width, 600, FontProviders.ForCurrentThread()))
        {
            map[b.Path] = b;
        }

        return map;
    }

    // HTML §15.3.2 (The page): the body's margins default to 8px.
    [Fact]
    public void BodyHasAnEightPixelMargin()
    {
        Dictionary<string, ElementBox> m = Lay("<div style='height:10px'></div>");
        ElementBox body = m["0/1"];
        Assert.Equal(8, body.X, 3);
        Assert.Equal(8, body.Y, 3);
        Assert.Equal(784, body.Width, 3);
    }

    [Fact]
    public void AuthorMarginOverridesTheDefaultBodyMargin()
    {
        Dictionary<string, ElementBox> m = Lay("<div style='height:10px'></div>", "body{margin:0}");
        Assert.Equal(0, m["0/1"].X, 3);
        Assert.Equal(800, m["0/1"].Width, 3);
    }

    // The size of `monospace` alone is 13px where medium is 16px; any other list keeps 16px.
    [Fact]
    public void GenericMonospaceAloneUsesTheSmallerDefaultSize()
    {
        Dictionary<string, ElementBox> m = Lay("<p><span>a</span><code>a</code><span style='font-family:monospace,monospace'>a</span><span style='font-family:\"Courier New\",monospace'>a</span></p>");
        double bare = m["0/1/0/1"].Height;
        Assert.True(bare < m["0/1/0/2"].Height, "a doubled generic keyword is a list, so it keeps the full size");
        Assert.True(bare < m["0/1/0/3"].Height, "a named family before monospace keeps the full size");
    }

    // A size that came from a keyword (the default medium included) follows the family list; a size given as a length does not.
    [Fact]
    public void KeywordSizeFollowsTheFamilyListAndLengthSizeDoesNot()
    {
        string digits = "0000000000";
        Dictionary<string, ElementBox> m = Lay(
            "<div style='font-size:20px'><span style='font-family:monospace'>" + digits + "</span><span style='font-family:monospace,monospace'>" + digits + "</span></div>"
            + "<div style='font-size:x-large'><span style='font-family:monospace'>" + digits + "</span><span style='font-family:monospace,monospace'>" + digits + "</span></div>"
            + "<div style='font-family:monospace'><span style='font-family:serif'>0</span></div>"
            + "<div><span>0</span></div>");
        Assert.Equal(m["0/1/0/1"].Width, m["0/1/0/0"].Width, 2); // 20px given as a length: no scaling
        Assert.Equal(m["0/1/1/1"].Width * 13 / 16, m["0/1/1/0"].Width, 2); // x-large is a keyword: scaled in a bare monospace list
        Assert.Equal(m["0/1/3/0"].Width, m["0/1/2/0"].Width, 2); // back to a proportional family: the keyword's own size again
    }

    // CSS Fonts 4 §2.5: em refers to the parent's computed size, so a nested element that only inherits must not apply it again.
    [Fact]
    public void RelativeFontSizeIsNotAppliedAgainByDescendants()
    {
        Dictionary<string, ElementBox> m = Lay("<div style='font-size:2em;line-height:1'><p style='margin:0'>x</p></div>", "body{font-size:10px}");
        Assert.Equal(20, m["0/1/0/0"].Height, 3);
    }

    [Fact]
    public void BolderIsNotAppliedAgainByDescendants()
    {
        double once = Lay("<div style='font-weight:bolder'><span>wide words here</span></div>", "body{font-weight:400}")["0/1/0/0"].Width;
        double twice = Lay("<div style='font-weight:700'><span>wide words here</span></div>")["0/1/0/0"].Width;
        Assert.Equal(twice, once, 3);
    }

    // CSS Text 4 §3: white-space-collapse and text-wrap-mode inherit, so a `white-space: pre` ancestor keeps the line breaks of an inline element inside.
    [Fact]
    public void NewlinesInsideAnInlineElementOfPreBreakLines()
    {
        Dictionary<string, ElementBox> m = Lay("<pre id=p><code>one\ntwo\nthree</code></pre>", "pre{margin:0}");
        double line = Lay("<pre><code>one</code></pre>", "pre{margin:0}")["0/1/0"].Height;
        Assert.Equal(3 * line, m["0/1/0"].Height, 2);
    }

    [Fact]
    public void WhiteSpaceShorthandOnAnAncestorReachesDescendants()
    {
        Dictionary<string, ElementBox> m = Lay("<div style='white-space:pre'><span>a\nb\nc</span></div><div style='white-space:pre'><span>a b c</span></div>");
        Assert.Equal(m["0/1/1"].Height * 3, m["0/1/0"].Height, 2);
    }

    // CSS2 §8.3.1 / §9.5.2: an empty block with `clear` that names floats of its context keeps the margin before it apart from the margin after it.
    [Fact]
    public void EmptyClearingBlockSeparatesTheMarginsAroundIt()
    {
        string body = "<div style='float:left;width:10px;height:10px'></div><p style='margin:17px 0;height:50px'></p><div style='clear:both'></div><div style='margin-top:20px;height:5px'></div>";
        Dictionary<string, ElementBox> m = Lay(body, "body{margin:0}");
        ElementBox p = m["0/1/1"];
        ElementBox clear = m["0/1/2"];
        ElementBox next = m["0/1/3"];
        Assert.Equal(p.Y + p.Height + 17, clear.Y, 3);
        Assert.Equal(clear.Y + 20, next.Y, 3);
    }

    // The margins after an empty `clear` block run from its top margin edge: next = y - margin-top + max(margin-top, margin-bottom, next margin-top).
    // Chrome's numbers for a float above the block (no clearance needed) and for one that reaches it (clearance).
    [Fact]
    public void EmptyClearingBlockMarginsStartAtItsTopMarginEdge()
    {
        string flow = "<div style='float:left;width:20px;height:20px;margin:0'></div><div style='height:20px;margin-bottom:17px'></div>"
            + "<div style='clear:both;margin:10px 0'></div><div style='margin-top:20px;height:5px'></div>";
        Dictionary<string, ElementBox> m = Lay(flow, "body{margin:0}");
        Assert.Equal(37, m["0/1/2"].Y, 3);
        Assert.Equal(47, m["0/1/3"].Y, 3);

        string cleared = "<div style='height:20px;margin-bottom:17px'></div><div style='float:left;width:20px;height:20px;margin:0 0 8px'></div>"
            + "<div style='clear:both;margin:40px 0 50px'></div><div style='margin-top:20px;height:5px'></div>";
        m = Lay(cleared, "body{margin:0}");
        Assert.Equal(65, m["0/1/2"].Y, 3);
        Assert.Equal(75, m["0/1/3"].Y, 3);
    }

    [Fact]
    public void EmptyBlockWithoutClearCollapsesThrough()
    {
        string body = "<p style='margin:17px 0;height:50px'></p><div></div><div style='margin-top:20px;height:5px'></div>";
        Dictionary<string, ElementBox> m = Lay(body, "body{margin:0}");
        ElementBox p = m["0/1/0"];
        Assert.Equal(p.Y + p.Height + 20, m["0/1/2"].Y, 3);
    }

    private static double SpanWidth(string span) => Lay(span)["0/1/0"].Width;

    // CSS Text 3 §2.1: text-transform changes the case of what is laid out, so widths follow the transformed text.
    [Fact]
    public void TextTransformChangesTheLaidOutText()
    {
        Assert.Equal(SpanWidth("<span>HELLO WORLD</span>"), SpanWidth("<span style='text-transform:uppercase'>Hello world</span>"), 3);
        Assert.Equal(SpanWidth("<span>hello world</span>"), SpanWidth("<span style='text-transform:lowercase'>HeLLo WORLD</span>"), 3);
        Assert.Equal(SpanWidth("<span>Hello Big World</span>"), SpanWidth("<span style='text-transform:capitalize'>hello big world</span>"), 3);
        Assert.Equal(SpanWidth("<span>Don't Stop</span>"), SpanWidth("<span style='text-transform:capitalize'>don't stop</span>"), 3);
        Assert.Equal(SpanWidth("<span>STRASSE</span>"), SpanWidth("<span style='text-transform:uppercase'>stra\u00dfe</span>"), 3);
    }

    [Fact]
    public void TextTransformInheritsAndContinuesAcrossInlineBoundaries()
    {
        Assert.Equal(SpanWidth("<span>ABC</span>"), Lay("<div style='text-transform:uppercase'><span>abc</span></div>")["0/1/0/0"].Width, 3);
        Assert.Equal(SpanWidth("<span>Foo</span>"), SpanWidth("<span style='text-transform:capitalize'><span>f</span>oo</span>"), 3);
    }

    // CSS Text 3 §4.1.3: a preserved segment break always ends the line, so two in a row leave an empty line between them.
    [Fact]
    public void ConsecutivePreservedNewlinesLeaveAnEmptyLine()
    {
        double line = Lay("<div style='white-space:pre-line'>a</div>")["0/1/0"].Height;
        Assert.Equal(3 * line, Lay("<div style='white-space:pre-line'>a\n\nb</div>")["0/1/0"].Height, 3);
        Assert.Equal(3 * line, Lay("<div style='white-space:pre'>a\n\nb</div>")["0/1/0"].Height, 3);
        Assert.Equal(3 * line, Lay("<div>a<br><br>b</div>")["0/1/0"].Height, 3);
    }

    // CSS2 §10.3.5: a float's width is min(max(preferred minimum, available), preferred), so it wraps at the space when the container is narrower than its text.
    [Fact]
    public void FloatShrinkToFitWrapsInANarrowContainer()
    {
        Dictionary<string, ElementBox> m = Lay("<div style='width:82px;overflow:hidden'><a style='float:left'>\u2190 Installation</a></div>");
        ElementBox a = m["0/1/0/0"];
        double one = Lay("<span>\u2190 Installation</span>")["0/1/0"].Width;
        Assert.True(one > 82, "the text alone is wider than the container");
        Assert.Equal(82, a.Width, 1);
        Assert.True(a.Height > 30, "two lines");
    }
}
