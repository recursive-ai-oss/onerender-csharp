using System.Text;
using System.Text.RegularExpressions;
using OnePdf;
using OneRender.Fonts;
using OneRender.Image;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.Pdf;

namespace OneRender.Pdf.Tests;

/// <summary>
/// A character that has no glyph never shows the font's missing-glyph shape (glyph 0): the base fonts package ships a symbol face
/// for the common symbols, and for anything still missing the text keeps its advance and its extractable text but draws nothing
/// (ISO 14289-1 7.21.8, ISO 19005-2 6.2.11.8).
/// </summary>
public class MissingGlyphTests
{
    private const string Certificate = "certificate";

    /// <summary>Every glyph run in the display list of a document, as (text, glyph ids).</summary>
    private static List<(string Text, int[] Ids)> GlyphRuns(string html, string baseUrl, Func<string, byte[]?> resources, double width, double height)
    {
        var images = new ImageStore(resources, baseUrl);
        var options = new HtmlPageOptions(FontProviders.ForCurrentThread())
        {
            Images = images,
            Replaced = images,
            DevicePixelRatio = 1,
            ViewportWidth = width,
            ViewportHeight = height,
            BaseUrl = baseUrl,
            StylesheetBytes = resources,
        };
        HtmlPage page = HtmlPage.Create(html, options, default);
        var list = new DisplayList();
        new Painter().Paint(
            page.Fragments,
            list,
            new PaintOptions(width, height) { Scroll = page.Scroll, Images = images, Fonts = options.Fonts, DevicePixelRatio = 1, BaseUrl = baseUrl },
            default);
        var runs = new List<(string, int[])>();
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem it = list[i];
            if (it.Kind == ItemKind.GlyphRun)
            {
                GlyphRunView run = it.GlyphRun;
                runs.Add((run.Text.ToString(), run.GlyphIds.ToArray()));
            }
        }

        return runs;
    }

    [Fact]
    public void TheBasePackageAddsItsSymbolFaceOnce()
    {
        // No opt-in fonts package is registered in this test project: twenty default faces and the fallback-only symbol face.
        Assert.Equal(DefaultFonts.Faces.Count + DefaultFonts.FallbackFaces.Count, FontProviders.CreateDefault().Catalog.FaceCount);
    }

    [Fact]
    public void CertificateStarUsesARealGlyphFromTheFallbackFace()
    {
        PdfPageSetup setup = new(1122.5196850, 793.7007874, 24, 24, 24, 24);
        var runs = GlyphRuns(
            Support.Template(Certificate),
            "https://templates.invalid/certificate/index.html",
            Support.TemplateResources(Certificate),
            setup.ContentWidth,
            setup.ContentHeight);
        var star = runs.Where(r => r.Text.Contains('\u2605')).ToList();
        Assert.NotEmpty(star);
        foreach (var r in star)
        {
            Assert.DoesNotContain(0, r.Ids);
        }

        // The raster draws the same glyph: the seal has ink where the star is, so the page differs from one with the star removed.
        string html = Support.Template(Certificate);
        var opts = new HtmlRenderOptions { Width = 1122, Height = 794, BaseUrl = "https://templates.invalid/certificate/index.html", Resources = Support.TemplateResources(Certificate) };
        byte[] with = HtmlRender.ToImage(html, opts).Pixels;
        byte[] without = HtmlRender.ToImage(html.Replace("&#9733;", "&#160;", StringComparison.Ordinal), opts).Pixels;
        Assert.False(with.AsSpan().SequenceEqual(without));
    }

    [Fact]
    public void CertificateStarInThePdfIsNotGlyphZero()
    {
        byte[] pdf = TemplateTests.Render(Certificate, PdfProfile.None);
        Assert.Contains('\u2605', Support.AllText(pdf));
        // The seal's text shows the star's own glyph: no show-text operand in the page has glyph 0 for it.
        Assert.False(ShowsGlyphZero(pdf), "the content stream shows glyph 0");
    }

    [Fact]
    public void UnsupportedCharacterDrawsNothingAndStaysExtractableUnderNoProfile()
    {
        // Tamil has no glyph in the default fonts (it needs the opt-in Noto Tamil package).
        const string html = "<p style=\"font-size:20px\">before \u0B85 after</p>";
        byte[] pdf = HtmlPdf.ToPdf(html, new HtmlPdfOptions { Output = new PdfOutputOptions { Profile = PdfProfile.None } });
        Assert.False(ShowsGlyphZero(pdf), "the content stream shows glyph 0");
        string text = Support.AllText(pdf);
        Assert.Contains("before", text, StringComparison.Ordinal);
        Assert.Contains("after", text, StringComparison.Ordinal);
        Assert.Contains('\u0B85', text);
    }

    [Fact]
    public void UnsupportedCharacterDrawsNoMissingGlyphBoxInTheRaster()
    {
        var opts = new HtmlRenderOptions { Width = 120, Height = 40 };
        const string with = "<body style=\"margin:0\"><span style=\"font-size:30px\">அஅ</span>";
        // Blank except for the page color: every pixel is white.
        byte[] px = HtmlRender.ToImage(with, opts).Pixels;
        for (int i = 0; i < px.Length; i++)
        {
            Assert.Equal(255, px[i]);
        }
    }

    [Fact]
    public void ResultNamesThePackageThatCoversAMissingCharacter()
    {
        // No opt-in package is registered in this test project, so an emoji and a Tamil letter have no glyph.
        HtmlPdfResult result;
        using (var ms = new MemoryStream())
        {
            result = HtmlPdf.ToPdf("<p>\U0001F600 \U0001F680 \u0B85 \u0B85</p>", ms);
        }

        var missing = result.Diagnostics.Where(d => d.Kind == DiagnosticKind.MissingGlyph).ToList();
        // One line per package, however many characters it would cover.
        Assert.Single(missing, d => d.Message.Contains("RecursiveAI.OneRender.Fonts.Emoji", StringComparison.Ordinal));
        Assert.Single(missing, d => d.Message.Contains("RecursiveAI.OneRender.Fonts.Noto.Tamil", StringComparison.Ordinal));
        Assert.Equal(2, missing.Count);

        using var clean = new MemoryStream();
        Assert.DoesNotContain(HtmlPdf.ToPdf("<p>plain text</p>", clean).Diagnostics, d => d.Kind == DiagnosticKind.MissingGlyph);
    }

    [Fact]
    public void ZeroWidthJoinersStayInTheTextLayerWithoutAnEmojiFont()
    {
        const string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
        byte[] pdf = HtmlPdf.ToPdf("<p>a " + family + " b</p>", new HtmlPdfOptions { Output = new PdfOutputOptions { Profile = PdfProfile.None } });
        using PdfDocument doc = PdfDocument.Open(pdf);
        var sb = new StringBuilder();
        foreach (PdfPage p in doc.Pages)
        {
            foreach (var g in p.ExtractText().Glyphs)
            {
                sb.Append(g.Text);
            }
        }

        Assert.Contains(family, sb.ToString(), StringComparison.Ordinal);
    }

    /// <summary>True when any show-text operand of any page, decoded as 2-byte glyph codes, holds code 0 (the codes are glyph indexes, Identity-H).</summary>
    private static bool ShowsGlyphZero(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(pdf);
        foreach (PdfPage page in doc.Pages)
        {
            foreach (PdfStream s in page.ContentStreams)
            {
                string content = Encoding.Latin1.GetString(s.GetDecodedBytes().Span);
                foreach (Match m in Regex.Matches(content, "<([0-9A-Fa-f]+)>"))
                {
                    string hex = m.Groups[1].Value;
                    for (int i = 0; i + 4 <= hex.Length; i += 4)
                    {
                        if (hex.AsSpan(i, 4).SequenceEqual("0000"))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }
}
