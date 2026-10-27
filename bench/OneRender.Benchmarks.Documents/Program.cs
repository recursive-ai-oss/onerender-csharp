using System;
using System.IO;
using System.Diagnostics;
using BenchmarkDotNet.Running;
using OneRender.Pdf;

// Usage: render <in.html> <out.pdf> [repeat]   renders the file; prints pages, bytes and the milliseconds of each render (the first one is cold)
//        bdn [BenchmarkDotNet arguments]       in-process time and allocation of the four documents; ONERENDER_DOCS names the folder
if (args.Length > 0 && args[0] == "render")
{
    string html = File.ReadAllText(args[1]);
    int repeat = args.Length > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 1;
    for (int i = 0; i < repeat; i++)
    {
        var sw = Stopwatch.StartNew();
        HtmlPdfResult r;
        using (FileStream fs = File.Create(args[2]))
        {
            r = HtmlPdf.ToPdf(html, fs);
        }

        sw.Stop();
        Console.WriteLine("pages={0} bytes={1} ms={2:F1} diagnostics={3}", r.PageCount, new FileInfo(args[2]).Length, sw.Elapsed.TotalMilliseconds, r.Diagnostics.Count);
    }

    return 0;
}

if (args.Length > 0 && args[0] == "bdn")
{
    BenchmarkSwitcher.FromAssembly(typeof(OneRender.Benchmarks.Documents.DocumentBenchmarks).Assembly).Run(args[1..]);
    return 0;
}

Console.Error.WriteLine("usage: render <in.html> <out.pdf> [repeat] | bdn [BenchmarkDotNet arguments]");
return 2;
