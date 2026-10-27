using System;
using System.IO;
using OnePdf;
using OneRender.Painting;
using OneRender.Pdf;

namespace OneRender.Fuzz;

/// <summary>
/// Builds a display list from the bytes (as the <c>displaylist</c> target does), takes a page size, margins, tagging and profile
/// from the first bytes (including hostile values), and writes the list as PDF. Only <see cref="PdfRenderException"/>,
/// <see cref="ArgumentException"/> and <see cref="DisplayListException"/> may escape; a file that was written must reopen with
/// the same number of pages. Anything else, a hang or a memory blow-up is a finding.
/// </summary>
internal static class PdfTarget
{
    private static readonly double[] Sizes = [0, -1, 1, 20, 200, 595, 794, 1123, 14400, 14401, 1e9, double.NaN, double.PositiveInfinity];

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
        {
            return;
        }

        double Pick(int b) => b < 240 ? 20 + (b * 4) : Sizes[(b - 240) % Sizes.Length];
        var page = new PdfPageSetup(Pick(data[0]), Pick(data[1]), data[2] % 5 * 8, data[3] % 5 * 8, data[4] % 5 * 8, data[5] % 5 * 8);
        var profile = (PdfProfile)(data[6] % 4);
        bool tagged = (data[6] & 8) == 0;
        DisplayList list;
        try
        {
            list = DisplayListTarget.BuildClosed(data[8..]);
        }
        catch (Exception ex) when (ex is DisplayListException or DisplayListLimitException or ArgumentException)
        {
            return;
        }

        using var ms = new MemoryStream();
        int pages = 0;
        try
        {
            var options = new PdfOutputOptions { Profile = profile, Tagged = tagged || profile != PdfProfile.None, LeaveOpen = true, RasterDpi = 24, MaxPages = 4 };
            using var writer = new DisplayListPdfWriter(ms, options);
            for (int i = 0; i < 2; i++)
            {
                writer.AddPage(list, page);
                pages++;
            }

            writer.Finish();
        }
        catch (Exception ex) when (ex is PdfRenderException or ArgumentException or DisplayListException or DisplayListLimitException)
        {
            return;
        }

        using PdfDocument doc = PdfDocument.Open(ms.ToArray());
        if (doc.Pages.Count != pages)
        {
            throw new InvalidOperationException("the file has " + doc.Pages.Count + " pages, expected " + pages);
        }
    }
}
