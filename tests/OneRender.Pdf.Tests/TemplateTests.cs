using OnePdf;
using OneRender.Pdf;

namespace OneRender.Pdf.Tests;

public class TemplateTests
{
    public static TheoryData<string> Names => new() { "invoice", "invoice-spanned", "report", "statement", "receipt", "certificate", "email" };

    public static byte[] Render(string name, PdfProfile profile)
    {
        return HtmlPdf.ToPdf(
            Support.Template(name),
            new HtmlPdfOptions
            {
                Page = name == "certificate" ? new PdfPageSetup(1122.5196850, 793.7007874, 24, 24, 24, 24) : PdfPageSetup.A4,
                BaseUrl = "https://templates.invalid/" + name + "/index.html",
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

    [Fact]
    public void SpannedInvoiceStructureCarriesTheSpans()
    {
        byte[] pdf = Render("invoice-spanned", PdfProfile.None);
        using PdfDocument doc = PdfDocument.Open(pdf);
        var spans = new List<(string Type, long Col, long Row)>();
        var stack = new Stack<PdfObject>();
        if (doc.StructureTreeRoot?.Get("K") is { } root)
        {
            stack.Push(root);
        }

        while (stack.Count > 0)
        {
            PdfObject o = stack.Pop();
            if (o is PdfArray arr)
            {
                for (int i = arr.Count - 1; i >= 0; i--)
                {
                    if (arr[i] is { } item)
                    {
                        stack.Push(item);
                    }
                }
            }
            else if (o is PdfDictionary d)
            {
                string? type = d.GetName("S");
                if (type is "TH" or "TD")
                {
                    long col = 1, row = 1;
                    PdfObject? a = d.Get("A");
                    var attrs = new List<PdfDictionary>();
                    if (a is PdfDictionary one)
                    {
                        attrs.Add(one);
                    }
                    else if (a is PdfArray many)
                    {
                        for (int i = 0; i < many.Count; i++)
                        {
                            if (many[i] is PdfDictionary m)
                            {
                                attrs.Add(m);
                            }
                        }
                    }

                    foreach (PdfDictionary at in attrs)
                    {
                        col = at.GetInt("ColSpan", col);
                        row = at.GetInt("RowSpan", row);
                    }

                    spans.Add((type, col, row));
                }

                if (d.Get("K") is { } kids)
                {
                    stack.Push(kids);
                }
            }
        }

        Assert.Equal(4, spans.Count(s => s is ("TH", 4, 1)));
        Assert.Single(spans, s => s is ("TD", 1, 2));
    }
}
