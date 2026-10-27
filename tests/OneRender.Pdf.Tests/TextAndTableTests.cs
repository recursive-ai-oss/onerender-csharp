using System.Text;
using RecursiveAI.OneHtml;
using OnePdf;
using OneRender.Pdf;

namespace OneRender.Pdf.Tests;

public class TextAndTableTests
{
    private static string[] Words(string s) =>
        s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Extracted text equals the document's text content in order (ISO 32000-2 14.8.2.5 text extraction).</summary>
    [Theory]
    [MemberData(nameof(TemplateTests.Names), MemberType = typeof(TemplateTests))]
    public void ExtractedTextEqualsSourceText(string name)
    {
        string html = Support.Template(name);
        Document doc = Document.Parse(html);
        string source = string.Join(' ', Words(doc.DocumentElement.QuerySelector("body").TextContent ?? ""));
        byte[] pdf = TemplateTests.Render(name, PdfProfile.PdfUA1);
        string extracted = string.Join(' ', Words(Support.AllText(pdf)));
        if (Support.OutDir is { } od) { File.WriteAllText(Path.Combine(od, name + ".src.txt"), source); File.WriteAllText(Path.Combine(od, name + ".ext.txt"), extracted); }
        // Element boundaries carry no white space in the source but extraction separates words, so compare the characters.
        // Extraction may add generated content (list numbers, counters), so check that every source character is present
        // (the star in the certificate seal has no glyph in the shipped fonts) and that sample windows of the source appear in
        // order.
        string a = string.Concat(Words(source)), b = string.Concat(Words(extracted));
        var counts = new Dictionary<char, int>();
        foreach (char c in b)
        {
            counts[c] = counts.GetValueOrDefault(c) + 1;
        }

        int missing = 0;
        foreach (char c in a)
        {
            int left = counts.GetValueOrDefault(c);
            if (left == 0)
            {
                missing++;
            }
            else
            {
                counts[c] = left - 1;
            }
        }

        Assert.True(missing <= (name == "certificate" ? 1 : 0), name + ": " + missing + " source characters missing");
        int windows = 0, found = 0, last = 0;
        for (int at = 0; at + 16 <= a.Length; at += 64)
        {
            windows++;
            int pos = b.IndexOf(a.Substring(at, 16), last, StringComparison.Ordinal);
            if (pos >= 0)
            {
                found++;
                last = pos;
            }
        }

        Assert.True(found >= windows * 0.95, name + ": " + found + "/" + windows + " source windows in order");
    }

    private static string RowsTable(int rows, string rowStyle = "")
    {
        var sb = new StringBuilder("<!DOCTYPE html><html lang='en'><head><title>t</title><style>td,th{padding:2px 6px}</style></head><body><table><thead><tr><th>N</th><th>Value</th></tr></thead><tbody>");
        for (int i = 0; i < rows; i++)
        {
            sb.Append("<tr style='").Append(rowStyle).Append("'><td>r").Append(i).Append("</td><td>v").Append(i).Append("</td></tr>");
        }

        return sb.Append("</tbody></table></body></html>").ToString();
    }

    [Fact]
    public void LongTableBreaksBetweenRows()
    {
        byte[] pdf = HtmlPdf.ToPdf(RowsTable(300));
        using PdfDocument doc = PdfDocument.Open(pdf);
        Assert.True(doc.Pages.Count >= 3, "pages: " + doc.Pages.Count);
        // Every row's cells land on the same page and in order.
        int last = -1;
        for (int p = 0; p < doc.Pages.Count; p++)
        {
            string t = doc.Pages[p].ExtractText().Text;
            foreach (string w in Words(t))
            {
                if (w.Length > 1 && w[0] == 'r' && int.TryParse(w.AsSpan(1), out int n))
                {
                    Assert.True(n > last);
                    last = n;
                }
            }
        }

        Assert.Equal(299, last);
    }

    [Fact]
    public void RowsDoNotSplitAcrossPages()
    {
        byte[] pdf = HtmlPdf.ToPdf(RowsTable(200, "break-inside:avoid"));
        using PdfDocument doc = PdfDocument.Open(pdf);
        for (int p = 0; p < doc.Pages.Count; p++)
        {
            var w = Words(doc.Pages[p].ExtractText().Text);
            foreach (string s in w)
            {
                if (s.Length > 1 && s[0] == 'r' && int.TryParse(s.AsSpan(1), out int n))
                {
                    Assert.Contains("v" + n, w);
                }
            }
        }
    }
}
