using System.Collections.Generic;
using InvoiceApp.Components;
using OneRender.Pdf;

namespace InvoiceApp;

/// <summary>The sample invoice's data.</summary>
public static class Sample
{
    /// <summary>PDF options for the invoice: an accessible (PDF/UA-1) file. The title and language come from the component's markup.</summary>
    public static HtmlPdfOptions PdfOptions { get; } = new() { Output = new PdfOutputOptions { Profile = PdfProfile.PdfUA1 } };

    /// <summary>The parameters passed to the <see cref="Invoice"/> component.</summary>
    public static IReadOnlyDictionary<string, object?> Parameters { get; } = new Dictionary<string, object?>
    {
        ["Number"] = "INV-2026-0418",
        ["Customer"] = "Fabrikam Logistics LLC",
        ["Lines"] = new List<InvoiceLine>
        {
            new("Pallet wrap, 20 in x 1500 ft", 40, 18.50m),
            new("Corrugated box, 24 x 18 x 12", 250, 2.15m),
            new("Strapping, polyester, 1/2 in", 12, 64.00m),
            new("Label printer ribbons", 6, 27.75m),
        },
    };
}
