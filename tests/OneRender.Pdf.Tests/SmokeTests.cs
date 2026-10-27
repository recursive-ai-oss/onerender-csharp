using OnePdf;
using OneRender.Pdf;

namespace OneRender.Pdf.Tests;

public class SmokeTests
{
    [Fact]
    public void OnePageWithTextAndBox()
    {
        byte[] pdf = HtmlPdf.ToPdf("<!DOCTYPE html><html lang='en'><head><title>Hello</title></head><body><h1>Hello world</h1><p style='background:#eef;border:2px solid #44a;padding:4px'>Some text that is long enough.</p></body></html>");
        Support.Keep("smoke1.pdf", pdf);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 8));
        using PdfDocument doc = PdfDocument.Open(pdf);
        Assert.Single(doc.Pages);
        string text = Support.AllText(pdf);
        Assert.Contains("Hello world", text);
        Assert.Contains("Some text that is long enough.", text);
    }
}
