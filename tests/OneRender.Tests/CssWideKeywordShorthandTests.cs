using OneRender.Style;
using OneRender.Tests.Support;
using OneRender.Tree;
using Xunit;

namespace OneRender.Tests;

/// <summary>
/// A CSS-wide keyword on a shorthand sets every longhand to it (CSS Cascade 4 §7.3, CSS Values 4 §3), also for the shorthands
/// OneHtml keeps whole because it has no grammar for them.
/// </summary>
public class CssWideKeywordShorthandTests
{
    // The nth element with this tag name (document order).
    private static string Compute(string css, string body, string tag, string property, int nth = 0)
    {
        Pipeline p = Pipeline.FromHtml("<style>" + css + "</style>" + body, userAgentSheet: true);
        for (int i = 0; i < p.Styled.Count; i++)
        {
            var n = new StyledNode(p.Styled, i);
            if (n.Kind == StyledNodeKind.Element && n.TagName == tag && nth-- == 0)
            {
                Assert.True(Properties.TryGet(property, out PropertyId pid));
                return ComputedValueText.Get(pid, in n.Values);
            }
        }

        throw new InvalidOperationException("no <" + tag + ">");
    }

    // The user-agent rule `tr, td, th { vertical-align: inherit }` gives a cell its row's value.
    [Fact]
    public void VerticalAlignInheritReachesTableCells() =>
        Assert.Equal("top", Compute("", "<table><tr style='vertical-align:top'><td>x</td></tr></table>", "td", "vertical-align"));

    // An explicit `initial` is baseline, not the parent's value.
    [Fact]
    public void VerticalAlignInitialIsBaselineBelowAParentThatIsNot()
    {
        const string Body = "<div style='vertical-align:top'><span style='vertical-align:initial'>x</span><b style='vertical-align:inherit'>y</b></div>";
        Assert.Equal("baseline", Compute("", Body, "span", "vertical-align"));
        Assert.Equal("top", Compute("", Body, "b", "vertical-align"));
    }

    [Theory]
    [InlineData("flex:3 1 0", "flex:inherit", "flex-grow")]
    [InlineData("border-radius:7px", "border-radius:inherit", "border-top-left-radius")]
    [InlineData("white-space:pre", "white-space:inherit", "white-space-collapse")]
    [InlineData("place-items:end", "place-items:inherit", "align-items")]
    public void InheritOnAShorthandSetsItsLonghands(string parentRule, string childRule, string longhand)
    {
        string body = "<section style='" + parentRule + "'><article style='" + childRule + "'>x</article></section>";
        string parent = Compute("", body, "section", longhand);
        Assert.NotEqual(Compute("", "<article>x</article>", "article", longhand), parent);
        Assert.Equal(parent, Compute("", body, "article", longhand));
    }

    [Theory]
    [InlineData("flex:5", "flex", "flex-grow")]
    [InlineData("border-radius:9px", "border-radius", "border-top-left-radius")]
    [InlineData("white-space:pre", "white-space", "white-space-collapse")]
    [InlineData("place-items:end", "place-items", "align-items")]
    public void InitialOnAShorthandResetsItsLonghands(string set, string shorthand, string longhand)
    {
        string plain = Compute("", "<article>x</article>", "article", longhand);
        Assert.NotEqual(plain, Compute("", "<article style='" + set + "'>x</article>", "article", longhand));
        Assert.Equal(plain, Compute("", "<article style='" + set + ";" + shorthand + ":initial'>x</article>", "article", longhand));
    }
}
