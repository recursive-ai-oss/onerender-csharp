using System.Text;
using OnePdf;
using OnePdf.Text;
using OneRender.Image;
using OneRender.Paged;
using OneRender.Painting;

namespace OneRender.Pdf.Tests;

/// <summary>A table row taller than the rest of the page continues on the next page without losing or clipping text (CSS Fragmentation 3 §4.1, CSS Tables 3 §17).</summary>
public class TableRowBreakTests
{
    private const string Head = "<!DOCTYPE html><html lang='en'><head><title>t</title><style>@page{size:400px 300px;margin:40px}html,body{margin:0}" +
        "table{border-spacing:0;width:100%}td{padding:2px;vertical-align:top;font:14px/18px Liberation Sans}p{margin:0;white-space:pre-wrap}</style></head><body>";

    private static string Lines(int n, string word) => string.Join("\n", Enumerable.Range(0, n).Select(i => word + i));

    private static int Glyphs(string s) => s.Count(c => !char.IsWhiteSpace(c));

    private static (int Pages, int Extracted) Convert(string html)
    {
        byte[] pdf = HtmlPdf.ToPdf(html, new HtmlPdfOptions { Page = new PdfPageSetup(400, 300, 40, 40, 40, 40) });
        using PdfDocument doc = PdfDocument.Open(pdf);
        int n = 0;
        for (int i = 0; i < doc.Pages.Count; i++)
        {
            n += Glyphs(doc.Pages[i].ExtractText().Text);
        }

        return (doc.Pages.Count, n);
    }

    [Fact]
    public void LongRowKeepsEveryLineAcrossPages()
    {
        string a = Lines(60, "alpha"), b = Lines(45, "beta");
        string html = Head + "<table><tr><td><p>" + a + "</p></td><td><p>" + b + "</p></td></tr></table></body></html>";
        var (pages, got) = Convert(html);
        Assert.True(pages >= 4, "pages " + pages);
        Assert.Equal(Glyphs(a) + Glyphs(b), got);
    }

    [Fact]
    public void WideUnbreakableWordInACellIsNotCutAtThePageEdge()
    {
        // The table cannot be narrower than its widest word, so it spills into the right margin; the page shows it all.
        string word = new string('n', 22);
        string html = Head + "<table><tr><td><p>" + word + "\n" + Lines(25, "x") + "</p></td><td><p>" + word + "</p></td></tr></table></body></html>";
        var (_, got) = Convert(html);
        Assert.Equal(Glyphs(word) * 2 + Glyphs(Lines(25, "x")), got);
    }

    [Fact]
    public void RowThatFitsAFreshPageAndAvoidsBreaksMovesWhole()
    {
        string filler = Lines(10, "fill");
        string row = Lines(8, "keep");
        string html = Head + "<p>" + filler + "</p><table><tr><td>a</td></tr><tr style='break-inside:avoid'><td><p>" + row + "</p></td></tr></table></body></html>";
        var options = new PagedOptions(FontProviders.CreateDefault()) { DefaultPage = new PageGeometry(400, 300, 40, 40, 40, 40) };
        PagedDocument doc = PagedDocument.Create(html, options);
        var list = new DisplayList();
        doc.PaintContent(list, null, false);
        var pageOf = new SortedSet<int>();
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem it = list[i];
            if (it.Kind == ItemKind.GlyphRun && it.GlyphRun.Text.ToString().StartsWith("keep", StringComparison.Ordinal))
            {
                pageOf.Add((int)(it.RootBounds.X / doc.Stride + 0.01));
            }
        }

        Assert.True(doc.PageCount >= 2);
        Assert.Single(pageOf);
    }

    private static List<int> TaggedOrder(string html)
    {
        byte[] pdf = HtmlPdf.ToPdf(html, new HtmlPdfOptions { Page = new PdfPageSetup(400, 300, 40, 40, 40, 40), Output = new PdfOutputOptions { Profile = PdfProfile.PdfUA1 } });
        using PdfDocument doc = PdfDocument.Open(pdf);
        Assert.True(doc.Pages.Count >= 3);
        string all = string.Join(" ", doc.ExtractText(new PdfTextOptions { UseStructureTree = true }).Select(p => p.Text));
        return System.Text.RegularExpressions.Regex.Matches(all, @"w(\d+)\b").Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
    }

    private static string Words(int n) => string.Join(" ", Enumerable.Range(0, n).Select(i => "w" + i));

    [Fact]
    public void ParagraphContinuedAcrossPagesReadsInSourceOrderInTheStructureTree()
    {
        // Several lengths so that the break leaves a different number of lines on the last page (a lone widow line among them).
        for (int n = 600; n < 640; n += 3)
        {
            var order = TaggedOrder(Head + "<h1>Title</h1><p>" + Words(n) + "</p><p>tail</p></body></html>");
            Assert.True(Enumerable.Range(0, n).SequenceEqual(order), "words " + n);
        }
    }

    [Fact]
    public void ParagraphInATableCellContinuedAcrossPagesReadsInSourceOrder()
    {
        var order = TaggedOrder(Head + "<table><tr><td><p>" + Words(900) + "</p></td></tr></table></body></html>");
        Assert.Equal(Enumerable.Range(0, 900), order);
    }

    [Fact]
    public void MarginsWiderThanHalfTheContentWidthNeverShowANeighbouringPage()
    {
        // Content width 160 px, side margins 120 px: the page-to-page gap in the list is 80 px, less than a margin.
        string head = "<!DOCTYPE html><html lang='en'><head><title>t</title><style>@page{size:400px 300px;margin:40px 120px}html,body{margin:0}p{margin:0;font:14px/18px Liberation Sans}</style></head><body>";
        string text = string.Join("", Enumerable.Range(0, 120).Select(i => "<p>line" + i + "</p>"));
        byte[] pdf = HtmlPdf.ToPdf(head + text + "</body></html>", new HtmlPdfOptions { Page = new PdfPageSetup(400, 300, 40, 120, 40, 120) });
        using PdfDocument doc = PdfDocument.Open(pdf);
        Assert.True(doc.Pages.Count >= 4, "pages " + doc.Pages.Count);
        var seen = new List<int>();
        for (int i = 0; i < doc.Pages.Count; i++)
        {
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(doc.Pages[i].ExtractText().Text, @"line(\d+)"))
            {
                seen.Add(int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        Assert.Equal(Enumerable.Range(0, 120), seen);
    }
}
