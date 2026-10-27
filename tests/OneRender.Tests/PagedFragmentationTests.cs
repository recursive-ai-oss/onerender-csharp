using OneRender.Image;
using System.Linq;
using OneRender.Paged;

namespace OneRender.Tests;

/// <summary>Flex containers, grids and table rows split between items and rows across pages (CSS Fragmentation 3 §4.4, Flexbox §12).</summary>
public class PagedFragmentationTests
{
    // 400x300 with 20 px margins: a 260 px page area.
    private static readonly PageGeometry Small = new(400, 300, 20, 20, 20, 20);

    private static PagedDocument Doc(string body) =>
        PagedDocument.Create("<!DOCTYPE html><html><head><style>@page{size:400px 300px;margin:20px}html,body{margin:0}.i{height:100px;break-inside:avoid}</style></head><body>" + body,
            new PagedOptions(FontProviders.CreateDefault()) { DefaultPage = Small });

    private static string Items(int n) => string.Concat(Enumerable.Range(0, n).Select(_ => "<div class='i'></div>"));

    [Fact]
    public void AColumnFlexContainerSplitsBetweenItems()
    {
        // 500 px of items against a 260 px page area: two items fit per page, so three pages.
        Assert.Equal(3, Doc("<div style='display:flex;flex-direction:column'>" + Items(5) + "</div>").PageCount);
    }

    [Fact]
    public void AGridSplitsBetweenRows()
    {
        Assert.Equal(3, Doc("<div style='display:grid'>" + Items(5) + "</div>").PageCount);
    }

    [Fact]
    public void ATableSplitsBetweenRows()
    {
        string rows = string.Concat(Enumerable.Range(0, 5).Select(_ => "<tr><td style='height:100px;padding:0;break-inside:avoid'></td></tr>"));
        Assert.Equal(3, Doc("<table style='border-spacing:0'>" + rows + "</table>").PageCount);
    }

    [Fact]
    public void ASingleRowOfFlexItemsStaysTogetherWhenItFits()
    {
        Assert.Equal(1, Doc("<div style='display:flex'>" + Items(3) + "</div>").PageCount);
    }

    [Fact]
    public void AWrappingFlexContainerBreaksBetweenLines()
    {
        // Short items in a wrapping row container: the lines are what break, never an item across a line.
        string items = string.Concat(Enumerable.Range(0, 6).Select(_ => "<div style='width:100%;height:100px'></div>"));
        Assert.Equal(3, Doc("<div style='display:flex;flex-wrap:wrap'>" + items + "</div>").PageCount);
    }

    [Fact]
    public void ARowThatFitsTheNextPageMovesWholeEvenWithEmptyCells()
    {
        // 30 px rows with an empty cell: a page holds eight rows (240 of 260 px), and the ninth moves instead of being cut.
        string rows = string.Concat(Enumerable.Range(0, 100).Select(_ => "<tr><td style='height:30px;padding:0'></td><td style='padding:0'>x</td></tr>"));
        Assert.Equal(13, Doc("<table style='border-spacing:0'>" + rows + "</table>").PageCount);
    }
}
