using OnePdf;
using OnePdf.Text;
using OneRender.Pdf;

namespace OneRender.Pdf.Tests;

internal static class Support
{
    /// <summary>Folder for files a test wants to keep for inspection (set by ONERENDER_PDF_OUT; null when not set).</summary>
    public static string? OutDir => Environment.GetEnvironmentVariable("ONERENDER_PDF_OUT");

    public static void Keep(string name, byte[] pdf)
    {
        if (OutDir is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), pdf);
        }
    }

    public static string TemplatePath(string name) => Path.Combine(AppContext.BaseDirectory, "testdata", "templates", name);

    public static string Template(string name) => File.ReadAllText(Path.Combine(TemplatePath(name), "index.html"));

    /// <summary>Resolves the template's stylesheet and other files from its folder.</summary>
    public static Func<string, byte[]?> TemplateResources(string name) => url =>
    {
        string file = url;
        int slash = file.LastIndexOf('/');
        if (slash >= 0)
        {
            file = file[(slash + 1)..];
        }

        string path = Path.Combine(TemplatePath(name), file);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    };

    public static string AllText(byte[] pdf)
    {
        using PdfDocument doc = PdfDocument.Open(pdf);
        var sb = new System.Text.StringBuilder();
        foreach (PdfPage p in doc.Pages)
        {
            sb.Append(p.ExtractText().Text).Append('\n');
        }

        return sb.ToString();
    }
}
