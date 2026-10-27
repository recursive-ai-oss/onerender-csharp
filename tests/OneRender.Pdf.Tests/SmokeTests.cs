using System.IO;
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

    [Fact]
    public void WideBlurredShadowsAreSmallPicturesAndKeepTheBoxClear()
    {
        // An outer and an inset shadow with a wide blur become low-resolution pictures clipped by the box; the page still reads back whole.
        string html = "<!DOCTYPE html><html lang='en'><head><title>Shadows</title></head><body style='margin:0'>"
            + "<div style='margin:90px 90px 40px;width:400px;height:200px;border-radius:12px;box-shadow:0 20px 80px rgba(0,0,0,.5)'>Outer shadow</div>"
            + "<div style='margin:0 90px;width:400px;height:200px;box-shadow:inset 0 0 60px #036'>Inset shadow</div></body></html>";
        HtmlPdfResult result;
        using var ms = new MemoryStream();
        result = HtmlPdf.ToPdf(html, ms, new HtmlPdfOptions { Output = new PdfOutputOptions { LeaveOpen = true } });
        Assert.Equal(1, result.PageCount);
        byte[] pdf = ms.ToArray();
        using PdfDocument doc = PdfDocument.Open(pdf);
        Assert.Single(doc.Pages);
        string text = Support.AllText(pdf);
        Assert.Contains("Outer shadow", text);
        Assert.Contains("Inset shadow", text);
        Assert.True(pdf.Length < 60_000, "the shadow pictures should be small, the file is " + pdf.Length + " bytes");
    }

    [Fact]
    public void HostScopesDoNotChangeThePdf()
    {
        // A scope names a pass for a host that draws the list itself; the writer draws its content as if it were not there.
        static byte[] Make(string extra) => HtmlPdf.ToPdf("<!DOCTYPE html><html lang='en'><head><title>Scopes</title></head><body><div style='background:#eef;padding:6px;" + extra + "'><h1>Scoped</h1><p style='opacity:.7'>Inside the scope.</p></div></body></html>");
        byte[] plain = Make("isolation:isolate");
        byte[] scoped = Make("--host-scope:12");
        Assert.Equal(plain, scoped);
        Assert.Contains("Inside the scope.", Support.AllText(scoped));
    }
}
