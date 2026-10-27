using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using OneRender.Pdf;

namespace OneRender.Benchmarks.Documents;

/// <summary>HTML to tagged PDF with default options, per document: the in-process time and the bytes allocated by one call.</summary>
[MemoryDiagnoser]
public class DocumentBenchmarks : IDisposable
{
    private string _html = "";
    private readonly MemoryStream _output = new();

    /// <summary>The document, by file name without extension, in the folder <c>ONERENDER_DOCS</c>.</summary>
    [Params("invoice", "report", "table10k", "tailwind")]
    public string Document { get; set; } = "invoice";

    [GlobalSetup]
    public void Setup()
    {
        string dir = Environment.GetEnvironmentVariable("ONERENDER_DOCS") ?? throw new InvalidOperationException("ONERENDER_DOCS is not set");
        _html = File.ReadAllText(Path.Combine(dir, Document + ".html"));
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public int ToPdf()
    {
        _output.SetLength(0);
        return HtmlPdf.ToPdf(_html, _output, new HtmlPdfOptions { Output = new PdfOutputOptions { LeaveOpen = true } }).PageCount;
    }
}
