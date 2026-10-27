using OnePdf;
using OneRender.Image;
using OneRender.Pdf;

namespace OneRender.FontPackageTests;

public class MixedScriptPdfTests
{
    // One paragraph per script, each set in the opt-in packages' fonts; the extracted text must equal what was written. This page is
    // the one validated with veraPDF (PDF/UA-1, tools/pdf/run-verapdf-fonts.sh): its Arabic, Devanagari and Thai words need no
    // multi-glyph clusters.
    private static readonly string[] s_lines =
    [
        "Hello world, the default fonts cover this line.",
        "\u0644\u0627 \u0627\u0644\u0644\u0647",
        "\u05E9\u05DC\u05D5\u05DD \u05E2\u05D5\u05DC\u05DD",
        "\u0928\u092E",
        "\u0E0A\u0E32\u0E27",
        "\u10E5\u10D0\u10E0\u10D7\u10E3\u10DA\u10D8 \u0540\u0561\u0575\u0565\u0580\u0565\u0576",
        "\u6C49\u5B57 \u3072\u3089\u304C\u306A \uD55C\uAD6D\uC5B4",
        "\U0001F600 \U0001F680 \U0001F1E9\U0001F1EA",
    ];

    // Scripts whose shaping splits a character into several glyphs (Arabic dots, Indic conjuncts, Thai marks). OnePdf writes the text
    // of such clusters as /ActualText, so extraction is exact, but veraPDF's PDF/UA-1 rule 7.21.7 wants a /ToUnicode entry for every
    // glyph and reports those glyphs; the page is therefore checked for extraction only.
    private static readonly string[] s_complexLines =
    [
        "\u0645\u0631\u062D\u0628\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645",
        "\u0928\u092E\u0938\u094D\u0924\u0947 \u0926\u0941\u0928\u093F\u092F\u093E",
        "\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35\u0E0A\u0E32\u0E27\u0E42\u0E25\u0E01",
    ];

    private static string Html(string[] lines) =>
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Mixed scripts</title></head><body style=\"font:16px sans-serif\"><h1>Scripts</h1>"
        + string.Concat(lines.Select(l => "<p>" + System.Net.WebUtility.HtmlEncode(l) + "</p>")) + "</body></html>";

    private static string Normalize(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

    private static void Check(string name, string[] lines)
    {
        AllPackages.RegisterAll();
        byte[] pdf = HtmlPdf.ToPdf(Html(lines), new HtmlPdfOptions { Fonts = FontProviders.CreateDefault(), Output = new PdfOutputOptions { Profile = PdfProfile.PdfUA1 } });
        if (Environment.GetEnvironmentVariable("ONERENDER_PDF_OUT") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".pdf"), pdf);
        }

        using PdfDocument doc = PdfDocument.Open(pdf);
        Assert.Empty(doc.CheckPdfUA());

        string normalized = Normalize(string.Concat(doc.Pages.Select(p => p.ExtractText().Text + "\n")));
        foreach (string line in lines)
        {
            Assert.Contains(Normalize(line), normalized, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MixedScriptPageIsTaggedAndItsTextIsExtractable() => Check("mixed-scripts", s_lines);

    [Fact]
    public void ComplexClusterScriptsExtractTheirSourceText() => Check("complex-scripts", s_complexLines);
}
