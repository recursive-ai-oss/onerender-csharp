using System.Text;
using OnePdf;
using OneRender.Pdf;

namespace OneRender.Pdf.Tests;

/// <summary>Hostile or oversized input fails cleanly with a typed exception, or is bounded, and never hangs.</summary>
public class LimitTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, -5)]
    [InlineData(PdfPageSetup.MaxEdge + 1, 100)]
    [InlineData(double.NaN, 100)]
    [InlineData(double.PositiveInfinity, 100)]
    public void PageSizesOutsideTheLimitsAreRefused(double w, double h)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlPdf.ToPdf("<p>x</p>", new HtmlPdfOptions { Page = new PdfPageSetup(w, h) }));
    }

    [Fact]
    public void MarginsThatLeaveNoContentAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HtmlPdf.ToPdf("<p>x</p>", new HtmlPdfOptions { Page = new PdfPageSetup(100, 100, 60, 60, 60, 60) }));
    }

    [Fact]
    public void MaxPagesStopsALongDocument()
    {
        var sb = new StringBuilder("<!DOCTYPE html><html><body>");
        for (int i = 0; i < 400; i++)
        {
            sb.Append("<p style='break-after:page'>page ").Append(i).Append("</p>");
        }

        var ex = Assert.Throws<PdfRenderException>(() => HtmlPdf.ToPdf(sb.Append("</body></html>").ToString(), new HtmlPdfOptions { Output = new PdfOutputOptions { MaxPages = 5 } }));
        Assert.Contains("pages", ex.Message);
    }

    [Fact]
    public void DeepNestingWithStructureTagsIsBounded()
    {
        var sb = new StringBuilder("<!DOCTYPE html><html lang='en'><head><title>Deep</title></head><body>");
        const int Depth = 4000;
        for (int i = 0; i < Depth; i++)
        {
            sb.Append(i % 2 == 0 ? "<div>" : "<section>");
        }

        sb.Append("<p>core text</p>");
        for (int i = Depth - 1; i >= 0; i--)
        {
            sb.Append(i % 2 == 0 ? "</div>" : "</section>");
        }

        byte[] pdf = HtmlPdf.ToPdf(sb.Append("</body></html>").ToString());
        Assert.Contains("core text", Support.AllText(pdf));
    }

    [Fact]
    public void ADeepListUnderPdfUaStillSatisfiesTheProfileOrFailsCleanly()
    {
        var sb = new StringBuilder("<!DOCTYPE html><html lang='en'><head><title>Lists</title></head><body>");
        for (int i = 0; i < 300; i++)
        {
            sb.Append("<ul><li>item ").Append(i);
        }

        for (int i = 0; i < 300; i++)
        {
            sb.Append("</li></ul>");
        }

        try
        {
            byte[] pdf = HtmlPdf.ToPdf(sb.Append("</body></html>").ToString(), new HtmlPdfOptions { Output = new PdfOutputOptions { Profile = PdfProfile.PdfUA1 } });
            using PdfDocument doc = PdfDocument.Open(pdf);
            Assert.True(doc.Pages.Count >= 1);
        }
        catch (PdfRenderException)
        {
            // A typed refusal is the other acceptable outcome.
        }
    }

    [Fact]
    public void AHugeUnbrokenTextRunIsWritten()
    {
        // One line of 1.2 million glyphs must not hit the text writer's per-call glyph limit.
        string text = new('a', 1_200_000);
        string html = "<!DOCTYPE html><html><body><p style='white-space:nowrap'>" + text + "</p></body></html>";
        try
        {
            byte[] pdf = HtmlPdf.ToPdf(html);
            using PdfDocument doc = PdfDocument.Open(pdf);
            Assert.True(doc.Pages.Count >= 1);
        }
        catch (PdfRenderException e)
        {
            Assert.Fail("refused: " + e.Message);
        }
    }

    [Fact]
    public void ManyRasterIslandsAreCappedAndReported()
    {
        var sb = new StringBuilder("<!DOCTYPE html><html><body>");
        for (int i = 0; i < 60; i++)
        {
            sb.Append("<div style='width:700px;height:900px;filter:blur(3px);background:#e33'>x</div>");
        }

        HtmlPdfResult r = HtmlPdf.ToPdf(sb.Append("</body></html>").ToString(), Stream.Null, new HtmlPdfOptions { Output = new PdfOutputOptions { RasterDpi = 96, LeaveOpen = true } });
        Assert.True(r.PageCount >= 1);
        Assert.NotEmpty(r.Diagnostics);
    }

    [Fact]
    public void AShadowBeyondTheRasterizersCoordinateRangeIsNotDrawnAndIsReported()
    {
        // An inset blurred shadow is drawn as a picture; geometry the rasterizer refuses leaves a hole and a diagnostic, not an exception.
        var list = new OneRender.Painting.DisplayList();
        list.DrawBoxShadow(
            OneRender.Painting.ItemId.Of(1, 0),
            new OneRender.Painting.RectF(36, 35.25f, 4.5f, 1e9f),
            default,
            new OneRender.Painting.PaintColor(0, 0, 0, 1),
            0,
            0,
            42.75f,
            0,
            true);
        list.Validate();
        using var stream = new MemoryStream();
        using var writer = new DisplayListPdfWriter(stream);
        writer.AddPage(list, PdfPageSetup.A4);
        Assert.Equal(1, writer.PageCount);
        Assert.Contains(writer.Diagnostics.Items, d => d.Message.Contains("could not be rasterized", StringComparison.Ordinal));
    }
}
