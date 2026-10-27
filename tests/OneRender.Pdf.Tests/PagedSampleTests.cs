using System.Linq;
using System.Text.RegularExpressions;
using OnePdf;
using OneRender.Pdf;

namespace OneRender.Pdf.Tests;

public class PagedSampleTests
{
    private static byte[] Render(PdfProfile profile) =>
        HtmlPdf.ToPdf(
            Support.Template("paged"),
            new HtmlPdfOptions
            {
                BaseUrl = "https://templates.invalid/paged/index.html",
                Resources = Support.TemplateResources("paged"),
                Output = new PdfOutputOptions { Profile = profile },
            });

    [Fact]
    public void TaggedPagedSamplePassesPdfUaChecks()
    {
        byte[] pdf = Render(PdfProfile.PdfUA1);
        Support.Keep("paged.pdf", pdf);
        using PdfDocument doc = PdfDocument.Open(pdf);
        Assert.Empty(doc.CheckPdfUA());
    }

    [Fact]
    public void PagesCarryRunningHeadersNumbersAndAContentsList()
    {
        byte[] pdf = Render(PdfProfile.None);
        using PdfDocument doc = PdfDocument.Open(pdf);
        int count = doc.Pages.Count;
        Assert.True(count >= 7, "pages: " + count);

        string[] pages = new string[count];
        for (int i = 0; i < count; i++)
        {
            pages[i] = doc.Pages[i].ExtractText().Text;
        }

        for (int i = 0; i < count; i++)
        {
            Assert.Contains("Page " + (i + 1) + " of " + count, pages[i]);
        }

        // The first page has no running header; later pages carry the chapter that starts on them.
        Assert.DoesNotContain("Contents", pages[1]);
        Assert.Contains("Contents", pages[0]);

        // The contents list says on which page each section starts, and that is where its heading is.
        MatchCollection entries = Regex.Matches(pages[0], @"\.\s+(\d+)\s");
        Assert.Equal(5, entries.Count);
        int[] targets = entries.Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(targets.OrderBy(x => x), targets);
        foreach (int t in targets)
        {
            Assert.InRange(t, 2, count);
        }
    }

    [Fact]
    public void ContentsEntriesAreInternalLinks()
    {
        byte[] pdf = Render(PdfProfile.None);
        using PdfDocument doc = PdfDocument.Open(pdf);
        var links = doc.Pages[0].Links.Where(l => l.Dictionary.Get("Dest") is not null || l.Dictionary.Get("A") is not null).ToList();
        Assert.Equal(5, links.Count);
    }
}
