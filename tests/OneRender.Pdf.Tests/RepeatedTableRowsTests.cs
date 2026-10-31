using System.Text;
using System.Text.RegularExpressions;
using OnePdf;
using OneRender.Image;
using OneRender.Paged;
using OneRender.Painting;
using OneRender.Pdf;

namespace OneRender.Pdf.Tests;

/// <summary>A table's thead and tfoot repeat on every page it spans, inside the page area (CSS 2 §17.5.2.2, CSS Tables 3 §9.1).</summary>
public class RepeatedTableRowsTests
{
    private const string Page = "@page{size:400px 200px;margin:0}";

    private static string Table() =>
        "<!DOCTYPE html><html lang='en'><head><title>t</title><style>" + Page + "html,body{margin:0}table{border-spacing:0;width:100%}td,th{padding:0;height:20px;font:bold 14px Liberation Sans;color:#000}" +
        "th{background:#fff}</style></head><body><table><thead><tr><th>HEADCELL</th></tr></thead><tbody>" +
        string.Concat(Enumerable.Range(0, 40).Select(i => "<tr><td>row" + i + "</td></tr>")) +
        "</tbody><tfoot><tr><td>FOOTCELL</td></tr></tfoot></table></body></html>";

    private static (List<(string Text, RectF Bounds)> Runs, int Pages, double Stride) PaintList(bool tags)
    {
        var options = new PagedOptions(FontProviders.CreateDefault());
        PagedDocument doc = PagedDocument.Create(Table(), options);
        var list = new DisplayList();
        doc.PaintContent(list, null, tags);
        var runs = new List<(string, RectF)>();
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem it = list[i];
            if (it.Kind == ItemKind.GlyphRun)
            {
                runs.Add((it.GlyphRun.Text.ToString(), it.RootBounds));
            }
        }

        return (runs, doc.PageCount, doc.Stride);
    }

    [Fact]
    public void RepeatedHeaderAndFooterSitInsideEveryPageArea()
    {
        var (runs, pages, stride) = PaintList(false);
        Assert.True(pages >= 4, "pages: " + pages);
        for (int p = 0; p < pages; p++)
        {
            var onPage = runs.Where(r => (int)(r.Bounds.X / stride + 0.01) == p).ToList();
            if (p > 0)
            {
                (string _, RectF head) = onPage.Single(r => r.Text == "HEADCELL");
                Assert.True(head.Y >= -0.5 && head.Y < 2, "header top on page " + (p + 1) + ": " + head.Y);
                // The first body row follows the header directly.
                (string _, RectF first) = onPage.Where(r => r.Text.StartsWith("row", StringComparison.Ordinal)).OrderBy(r => r.Bounds.Y).First();
                Assert.True(Math.Abs(first.Y - head.Bottom) <= 1.5, "first row " + first.Y + " header bottom " + head.Bottom);
            }

            if (p < pages - 1)
            {
                (string _, RectF foot) = onPage.Single(r => r.Text == "FOOTCELL");
                (string _, RectF last) = onPage.Where(r => r.Text.StartsWith("row", StringComparison.Ordinal)).OrderBy(r => r.Bounds.Y).Last();
                // A text box starts a pixel above its 20 px cell, so the cell ends at foot.Y + 19 and must lie inside the 200 px page.
                Assert.True(foot.Y + 19 <= 200.5, "footer " + foot + " on page " + (p + 1));
                Assert.True(Math.Abs(foot.Y - last.Bottom) <= 1.5, "footer " + foot.Y + " last row bottom " + last.Bottom);
            }
        }
    }

    [Fact]
    public void RasterOfLaterPagesShowsTheRepeatedRows()
    {
        var options = new PagedOptions(FontProviders.CreateDefault());
        PagedDocument doc = PagedDocument.Create(Table(), options);
        var list = new DisplayList();
        doc.PaintContent(list);
        Assert.True(doc.PageCount >= 3);
        int stride = (int)Math.Ceiling(doc.Stride);
        RgbaImage img = DisplayListRasterizer.Render(list, stride * doc.PageCount, 200, new RasterOptions(), default);
        for (int p = 1; p < doc.PageCount - 1; p++)
        {
            // Ink in the top 20 px (the header) and in the bottom 40 px (the footer follows the last row, within the page).
            Assert.True(Ink(img, p * stride, 0, 100, 20) > 20, "header ink on page " + (p + 1));
            Assert.True(Ink(img, p * stride, 150, 100, 50) > 20, "footer ink on page " + (p + 1));
        }
    }

    private static int Ink(RgbaImage img, int x0, int y0, int w, int h)
    {
        int n = 0;
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                int o = ((y * img.Width) + x) * 4;
                if (img.Pixels[o] < 128)
                {
                    n++;
                }
            }
        }

        return n;
    }

    [Fact]
    public void TaggedPdfDrawsTheRepeatedRowsAsArtifactsOnLaterPages()
    {
        byte[] pdf = HtmlPdf.ToPdf(
            Table(),
            new HtmlPdfOptions { Page = new PdfPageSetup(400, 200, 0, 0, 0, 0), Output = new PdfOutputOptions { Profile = PdfProfile.PdfUA1 } });
        using PdfDocument doc = PdfDocument.Open(pdf);
        Assert.True(doc.Pages.Count >= 4);
        Assert.Empty(doc.CheckPdfUA());
        for (int p = 0; p < doc.Pages.Count; p++)
        {
            string content = string.Concat(doc.Pages[p].ContentStreams.Select(c => Encoding.Latin1.GetString(c.GetDecodedBytes().Span)));
            // Each repeated row is one text object inside an /Artifact marked-content sequence; the table rows are tagged content.
            int artifactTexts = 0;
            foreach (Match m in Regex.Matches(content, @"/Artifact(?: <<[^>]*>>)? B(?:DC|MC)\n[\s\S]*?\nEMC"))
            {
                artifactTexts += Regex.Count(m.Value, @"\bTJ\b|\bTj\b");
            }

            int expected = (p > 0 ? 1 : 0) + (p < doc.Pages.Count - 1 ? 1 : 0);
            Assert.True(artifactTexts == expected, "page " + (p + 1) + ": " + artifactTexts + " artifact texts, expected " + expected);
        }
    }
}
