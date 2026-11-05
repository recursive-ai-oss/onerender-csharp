using System.Text;
using System.Text.RegularExpressions;
using OnePdf;
using OneRender.Pdf;

namespace OneRender.FontPackageTests;

/// <summary>Colour emoji in PDF output: the invisible text layer under the bitmaps must not leak into later text (PDF 32000-1 8.4.2, 9.3.6).</summary>
public class PdfEmojiTests
{
    public PdfEmojiTests() => AllPackages.RegisterAll();

    private static string Content(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(pdf);
        var sb = new StringBuilder();
        foreach (PdfPage page in doc.Pages)
        {
            foreach (PdfStream s in page.ContentStreams)
            {
                sb.Append(Encoding.Latin1.GetString(s.GetDecodedBytes().Span)).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static string AllText(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(pdf);
        var sb = new StringBuilder();
        foreach (PdfPage p in doc.Pages)
        {
            sb.Append(p.ExtractText().Text).Append('\n');
        }

        return sb.ToString();
    }

    private static byte[] Pdf(string html) => HtmlPdf.ToPdf(html, new HtmlPdfOptions { Output = new PdfOutputOptions { Profile = PdfProfile.None } });

    /// <summary>The text render mode each text object (BT..ET) is written with. The builder writes one Tr into every object, so this is what a viewer applies.</summary>
    private static List<int> RenderModes(string content)
    {
        var modes = new List<int>();
        foreach (Match m in Regex.Matches(content, @"BT
(?:(?!ET)[\s\S])*?(\d) Tr"))
        {
            modes.Add(m.Groups[1].Value[0] - '0');
        }

        return modes;
    }

    /// <summary>The text layer as the extractor reads it glyph by glyph, before any reading-order or bidi processing.</summary>
    private static string GlyphText(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(pdf);
        var sb = new StringBuilder();
        foreach (PdfPage p in doc.Pages)
        {
            foreach (var g in p.ExtractText().Glyphs)
            {
                sb.Append(g.Text);
            }
        }

        return sb.ToString();
    }

    [Fact]
    public void TextAfterAColourEmojiIsNotInvisible()
    {
        byte[] pdf = Pdf("<p>before 😀 after</p><h2>heading</h2>");
        string content = Content(pdf);
        List<int> modes = RenderModes(content);
        // Only the emoji's own text layer is invisible; the text before and after it and the heading are filled.
        Assert.Equal(1, modes.Count(m => m == 3));
        Assert.True(modes.Count(m => m == 0) >= 3, string.Join(",", modes));
        Assert.Equal(modes.Count - 1, modes.Count(m => m == 0));
        string text = AllText(pdf);
        Assert.Contains("before", text, StringComparison.Ordinal);
        Assert.Contains("after", text, StringComparison.Ordinal);
        Assert.Contains("heading", text, StringComparison.Ordinal);
        Assert.Contains("😀", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroWidthJoinersStayInTheTextLayerWithTheEmojiFont()
    {
        const string family = "👨‍👩‍👧";
        Assert.Contains(family, GlyphText(Pdf("<p>a " + family + " b</p>")), StringComparison.Ordinal);
    }
}
