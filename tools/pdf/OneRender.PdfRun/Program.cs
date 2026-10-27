using System.Diagnostics;
using System.Text;
using OneRender.Pdf;

// Usage: OneRender.PdfRun report <sections> <out.pdf> [ua]   a generated report (headings, paragraphs, small tables)
//        OneRender.PdfRun rows <rows> <out.pdf> [ua]         one table of <rows> rows
// Prints pages, file size, elapsed milliseconds, bytes allocated, peak managed heap (sampled) and peak working set.
string kind = args[0];
int n = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
string path = args[2];
bool ua = args.Length > 3 && args[3] == "ua";

string html = kind == "rows" ? Rows(n) : Report(n);
long peak = 0;
var sampler = new Thread(() =>
{
    while (true)
    {
        peak = Math.Max(peak, GC.GetTotalMemory(false));
        Thread.Sleep(10);
    }
}) { IsBackground = true };
sampler.Start();
long before = GC.GetTotalAllocatedBytes(true);
var sw = Stopwatch.StartNew();
int pages;
int diagnostics;
using (FileStream fs = File.Create(path))
{
    HtmlPdfResult r = HtmlPdf.ToPdf(html, fs, new HtmlPdfOptions { Output = new PdfOutputOptions { Profile = ua ? PdfProfile.PdfUA1 : PdfProfile.None } });
    pages = r.PageCount;
    diagnostics = r.Diagnostics.Count;
}

sw.Stop();
long allocated = GC.GetTotalAllocatedBytes(false) - before;
Console.WriteLine($"{kind} {n}: pages {pages}, {new FileInfo(path).Length / 1024} KiB, {sw.ElapsedMilliseconds} ms, allocated {allocated >> 20} MiB, peak managed {peak >> 20} MiB, peak working set {Process.GetCurrentProcess().PeakWorkingSet64 >> 20} MiB, diagnostics {diagnostics}, html {html.Length >> 10} KiB");

static string Rows(int rows)
{
    var sb = new StringBuilder("<!DOCTYPE html><html lang='en'><head><title>Rows</title><style>body{font:12px Liberation Sans}table{border-collapse:collapse;width:100%}th,td{border:1px solid #999;padding:3px 6px;text-align:left}th{background:#eee}</style></head><body><h1>Ledger</h1><table><thead><tr><th>No</th><th>Account</th><th>Description</th><th>Amount</th></tr></thead><tbody>");
    for (int i = 0; i < rows; i++)
    {
        sb.Append("<tr><td>").Append(i).Append("</td><td>AC-").Append(i * 7919 % 100000).Append("</td><td>Entry number ").Append(i).Append(" for the period</td><td>").Append(i * 37 % 10000).Append('.').Append(i % 100).Append("</td></tr>");
    }

    return sb.Append("</tbody></table></body></html>").ToString();
}

static string Report(int sections)
{
    var sb = new StringBuilder("<!DOCTYPE html><html lang='en'><head><title>Report</title><style>body{font:11pt Liberation Serif;line-height:1.4}h2{break-after:avoid;color:#234}table{border-collapse:collapse}td,th{border:1px solid #bbb;padding:2px 8px}</style></head><body><h1>Annual report</h1>");
    string words = "revenue quarter margin forecast customer account policy balance support delivery schedule contract premium ";
    for (int s = 0; s < sections; s++)
    {
        sb.Append("<h2>Section ").Append(s + 1).Append("</h2>");
        for (int p = 0; p < 3; p++)
        {
            sb.Append("<p>");
            for (int w = 0; w < 4; w++)
            {
                sb.Append(words);
            }

            sb.Append("</p>");
        }

        sb.Append("<table><tr><th>Item</th><th>Value</th></tr>");
        for (int r = 0; r < 4; r++)
        {
            sb.Append("<tr><td>Line ").Append(r).Append("</td><td>").Append(s * 10 + r).Append("</td></tr>");
        }

        sb.Append("</table>");
    }

    return sb.Append("</body></html>").ToString();
}
