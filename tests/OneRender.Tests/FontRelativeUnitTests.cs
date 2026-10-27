using OneRender.Image;
using OneRender.Layout;
using Xunit;

namespace OneRender.Tests;

/// <summary>CSS Values 4 \u00a76.1.1: ch and ex come from the first available font, not from a fixed half em.</summary>
public sealed class FontRelativeUnitTests
{
    private static string Dump(string body)
    {
        HtmlPage page = HtmlPage.Create("<!DOCTYPE html><style>body{margin:0}</style>" + body, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 800, ViewportHeight = 600 });
        return page.Fragments.Dump();
    }

    [Fact]
    public void ChIsTheAdvanceOfZeroInTheUsedFont()
    {
        // Liberation Sans: the advance of "0" is 1139/2048 em, so 10ch at 20px is 111.2px (the half-em fallback would give 100).
        string d = Dump("<div style='font:20px Liberation Sans;width:10ch;height:10px'></div>");
        Assert.Contains("box div [0, 0, 111.2", d, StringComparison.Ordinal);
    }

    [Fact]
    public void ChOfAMonospaceFaceIsItsGlyphAdvance()
    {
        // Liberation Mono advances 1229/2048 em = 0.6001 em; 10ch at 20px is 120px.
        string d = Dump("<div style='font:20px Liberation Mono;width:10ch;height:10px'></div>");
        Assert.Contains("box div [0, 0, 120", d, StringComparison.Ordinal);
    }

    [Fact]
    public void ExIsTheXHeightOfTheUsedFont()
    {
        string d = Dump("<div style='font:20px Liberation Sans;width:10px;height:10ex'></div>");
        Assert.DoesNotContain("x 100]", d, StringComparison.Ordinal);
        Assert.Contains("x 105", d, StringComparison.Ordinal); // x-height 1082/2048 em = 10.56px per ex
    }

    [Fact]
    public void UnitsInTheElementsOwnFontSizeUseItsOwnFont()
    {
        string d = Dump("<div style='font-family:Liberation Sans;font-size:10px'><div style='font-size:20px;width:10ch;height:10px'></div></div>");
        Assert.Contains("box div [0, 0, 111.2", d, StringComparison.Ordinal);
    }

    [Fact]
    public void FontSizeUsesTheParentsFontForItsOwnExAndCh()
    {
        // The child's font-size is 1ch + 1em computed in the parent's font (20px Liberation Sans: ch = 11.12px), so 31.12px; the child's
        // own font does not enter into it.
        string d = Dump("<div style='font:20px Liberation Sans'><div style='font-size:calc(1ch + 1em);width:1em;height:10px'></div></div>");
        Assert.Contains("box div [0, 0, 31.1", d, StringComparison.Ordinal);
    }

    [Fact]
    public void AFontWithoutASpaceIsNotTheFirstAvailableFont()
    {
        // CSS Fonts 4 §5.1: a face limited by unicode-range to "a" can't be the first available font, so ex comes from the next family.
        // Liberation Serif's x-height (940/2048 em) differs from Liberation Sans's (1082/2048 em = 10.56px at 20px), so the two readings are told apart.
        string css = "@font-face{font-family:A;src:local('Liberation Serif');unicode-range:U+0061}";
        string d = Dump("<style>" + css + "</style><div style='font:20px A, Liberation Sans;width:10px;height:10ex'></div>");
        Assert.Contains("x 105", d, StringComparison.Ordinal);
        string serif = Dump("<div style='font:20px Liberation Serif;width:10px;height:10ex'></div>");
        Assert.DoesNotContain("x 105", serif, StringComparison.Ordinal);
    }
}
