using OnePdf;
using OneRender.Pdf;

namespace OneRender.Pdf.Tests;

public class TemplateTests
{
    public static TheoryData<string> Names => new() { "invoice", "report", "statement", "receipt", "certificate", "email" };

    public static byte[] Render(string name, PdfProfile profile)
    {
        return HtmlPdf.ToPdf(
            Support.Template(name),
            new HtmlPdfOptions
            {
                Page = PdfPageSetup.A4,
                Resources = Support.TemplateResources(name),
                Output = new PdfOutputOptions { Profile = profile },
            });
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void TaggedOutputPassesPdfUaChecks(string name)
    {
        byte[] pdf = Render(name, PdfProfile.PdfUA1);
        Support.Keep(name + ".pdf", pdf);
        using PdfDocument doc = PdfDocument.Open(pdf);
        Assert.Empty(doc.CheckPdfUA());
    }
}
