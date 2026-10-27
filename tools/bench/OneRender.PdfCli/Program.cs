using System;
using System.IO;
using OneRender.Pdf;

// Usage: OneRender.PdfCli <in.html> <out.pdf>   prints the page count
if (args.Length != 2)
{
    Console.Error.WriteLine("usage: OneRender.PdfCli <in.html> <out.pdf>");
    return 2;
}

string html = File.ReadAllText(args[0]);
HtmlPdfResult r;
using (FileStream fs = File.Create(args[1]))
{
    r = HtmlPdf.ToPdf(html, fs);
}

Console.WriteLine("pages={0}", r.PageCount);
return 0;
