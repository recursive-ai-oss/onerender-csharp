using OneRender.Image;
using OneRender.Layout;
using Xunit;

namespace OneRender.Tests;

/// <summary>Fragmentation over the real inline layout: line boxes are the class B break points, orphans and widows count them.</summary>
public sealed class MulticolInlineTests
{
    private static string Dump(string body)
    {
        HtmlPage page = HtmlPage.Create("<!DOCTYPE html><style>body{margin:0}</style>" + body, new HtmlPageOptions(FontProviders.CreateDefault()) { ViewportWidth = 800, ViewportHeight = 600 });
        return page.Fragments.Dump();
    }

    private static int Count(string dump, string needle)
    {
        int n = 0;
        for (int i = dump.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = dump.IndexOf(needle, i + 1, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }

    [Fact]
    public void LinesDirectlyInTheContainerAreBalancedIntoColumns()
    {
        string d = Dump("<div style='columns:2;column-gap:0;width:100px'>X<br>X<br>X<br>X</div>");
        Assert.Equal(2, Count(d, "column "));
        Assert.Equal(4, Count(d, "line "));
        Assert.Contains("box div [0, 0, 100 x 36]", d, StringComparison.Ordinal);
    }

    [Fact]
    public void ParagraphsBalanceByLinesAndKeepEveryLineOnce()
    {
        string d = Dump("<div style='columns:2;column-gap:0;width:100px;line-height:10px'><p style='margin:0'>X<br>X<br>X<br>X<br>X<br>X</p></div>");
        Assert.Equal(6, Count(d, "line "));
        Assert.Contains("box div [0, 0, 100 x 30]", d, StringComparison.Ordinal);
    }

    [Fact]
    public void WidowsMoveLinesToTheNextColumn()
    {
        // Five 10px lines in 30px columns: three fit in the first, widows 3 leaves only two lines for the second, so the break comes after two.
        string d = Dump("<div style='columns:2;column-gap:0;width:100px;height:30px;column-fill:auto;line-height:10px;widows:3;orphans:1'><p style='margin:0'>X<br>X<br>X<br>X<br>X</p></div>");
        int c1 = d.IndexOf("column 1", StringComparison.Ordinal);
        Assert.True(c1 > 0);
        Assert.Equal(2, Count(d[..c1], "line "));
        Assert.Equal(3, Count(d[c1..], "line "));
    }
}
