using OneRender.Image;
using OneRender.Layout;
using Xunit;

namespace OneRender.Tests;

/// <summary>CSS Values 4 §6.1.2: rex, rcap, rch and ric use the root element's font; plus the shorthands OneHtml expands.</summary>
public sealed class RootFontUnitTests
{
    private static string Dump(string html)
    {
        HtmlPage page = HtmlPage.Create("<!DOCTYPE html>" + html, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 800, ViewportHeight = 600 });
        return page.Fragments.Dump();
    }

    private const string MonoRootSansBody = "<style>html{font:20px Liberation Mono}body{margin:0;font-family:Liberation Sans}</style>";

    [Fact]
    public void RchIsTheRootFontsZeroAdvanceNotTheElements()
    {
        // The root is 20px Liberation Mono (advance 0.6001 em = 12px); the div is Liberation Sans, whose ch would give 111.2 for 10ch.
        string d = Dump(MonoRootSansBody + "<div style='width:10rch;height:10px'></div>");
        Assert.Contains("box div [0, 0, 120", d, StringComparison.Ordinal);
        Assert.DoesNotContain("111.2", d, StringComparison.Ordinal);
    }

    [Fact]
    public void RexUsesTheRootFont()
    {
        // Liberation Serif's x-height (940/2048 em) is smaller than Liberation Sans's (1082/2048 em).
        const string SerifRoot = "<style>html{font:20px Liberation Serif}body{margin:0;font-family:Liberation Sans}</style>";
        string viaRoot = Dump(SerifRoot + "<div style='width:10px;height:10rex'></div>");
        string viaElement = Dump(SerifRoot + "<div style='width:10px;height:10ex'></div>");
        Assert.Contains("x 91.8", viaRoot, StringComparison.Ordinal);
        Assert.Contains("x 105.6", viaElement, StringComparison.Ordinal);

        // On an element in the root's own font, rex and ex agree.
        string sameFontEx = Dump("<style>html{font:20px Liberation Mono}body{margin:0}</style><div style='width:10px;height:10ex'></div>");
        string sameFontRex = Dump("<style>html{font:20px Liberation Mono}body{margin:0}</style><div style='width:10px;height:10rex'></div>");
        Assert.Equal(sameFontEx, sameFontRex);
    }

    [Fact]
    public void RcapAndRicFollowTheRootFont()
    {
        string serifRoot = Dump("<style>html{font:20px Liberation Serif}body{margin:0;font-family:Liberation Sans}</style><div style='width:10px;height:10rcap'></div>");
        string sansRoot = Dump("<style>html{font:20px Liberation Sans}body{margin:0;font-family:Liberation Sans}</style><div style='width:10px;height:10rcap'></div>");
        string ownFontCap = Dump("<style>html{font:20px Liberation Serif}body{margin:0;font-family:Liberation Sans}</style><div style='width:10px;height:10cap'></div>");
        Assert.NotEqual(serifRoot, sansRoot);
        Assert.NotEqual(serifRoot, ownFontCap);
        string ric = Dump(MonoRootSansBody + "<div style='width:10ric;height:10px'></div>");
        Assert.Contains("box div", ric, StringComparison.Ordinal);
    }

    [Fact]
    public void WhiteSpaceInheritsThroughItsLonghands()
    {
        // CSS Text 4 §3.1: white-space-collapse and text-wrap-mode inherit, so a child of white-space:pre keeps the line break.
        string d = Dump("<style>body{margin:0;font:16px/20px Liberation Mono}</style><div style='white-space:pre'><span>a\nb</span></div>");
        Assert.Contains("box div [0, 0, 800 x 40]", d, StringComparison.Ordinal);
    }

    [Fact]
    public void BorderInlineAndBlockExpandToBothSides()
    {
        string d = Dump("<style>body{margin:0}</style><div style='border-inline:4px solid;border-block:2px solid;width:100px;height:10px'></div>");
        Assert.Contains("box div [0, 0, 108 x 14]", d, StringComparison.Ordinal);
    }
}
