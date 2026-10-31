using OnePdf;
using OneRender.Pdf;

namespace OneRender.Pdf.Tests;

/// <summary>
/// Every template, rendered with the default options, converts to PDF/A-2b with OnePDF's converter, which refuses a file that shows
/// .notdef (ISO 19005-2 6.2.11.8) or breaks another rule it can check. The converted files are kept when ONERENDER_PDF_OUT is set so
/// tools/pdf/run-verapdf-pdfa.sh can validate them with veraPDF.
/// </summary>
public class PdfAConversionTests
{
    public static TheoryData<string> Names => new() { "invoice", "invoice-spanned", "report", "statement", "receipt", "certificate", "email", "paged" };

    [Theory]
    [MemberData(nameof(Names))]
    public void DefaultOutputConvertsToPdfA(string name)
    {
        byte[] pdf = HtmlPdf.ToPdf(
            Support.Template(name),
            new HtmlPdfOptions
            {
                Page = name == "certificate" ? new PdfPageSetup(1122.5196850, 793.7007874, 24, 24, 24, 24) : PdfPageSetup.A4,
                BaseUrl = "https://templates.invalid/" + name + "/index.html",
                Resources = Support.TemplateResources(name),
            });
        using var input = new MemoryStream(pdf);
        using var output = new MemoryStream();
        PdfAConversionResult result = PdfAConverter.Convert(input, output);
        Assert.NotNull(result);
        Assert.True(output.Length > 0);
        Support.Keep(name + ".pdfa.pdf", output.ToArray());
    }
}
