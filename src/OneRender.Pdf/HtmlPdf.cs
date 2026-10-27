using System.IO;
using OneRender.Image;
using OneRender.Layout;
using OneRender.Paged;
using OneRender.Painting;
using OneRender.Text;

namespace OneRender.Pdf;

/// <summary>Options for <see cref="HtmlPdf"/>.</summary>
public sealed class HtmlPdfOptions
{
    /// <summary>Page size and margins. Default <see cref="PdfPageSetup.A4"/>.</summary>
    public PdfPageSetup Page { get; init; } = PdfPageSetup.A4;

    /// <summary>
    /// How the file is written. When <see cref="PdfOutputOptions.Title"/> is null the document's <c>&lt;title&gt;</c> is used, and when
    /// <see cref="PdfOutputOptions.Language"/> is null the <c>lang</c> attribute of the root element.
    /// </summary>
    public PdfOutputOptions Output { get; init; } = new();

    /// <summary>The address the document is deemed to have, for resolving relative references. Default none.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Supplies the bytes of a style sheet, a font file or an image by absolute address, or null when the host does not have it. The
    /// renderer never fetches anything. Default none.
    /// </summary>
    public Func<string, byte[]?>? Resources { get; init; }

    /// <summary>The fonts to use; null uses the packaged fonts through <see cref="FontProviders.ForCurrentThread"/>.</summary>
    public IFontProvider? Fonts { get; init; }

    /// <summary>The layout to run; null uses the engine's default.</summary>
    public ILayoutEngine? Layout { get; init; }
}

/// <summary>What <see cref="HtmlPdf.ToPdf(string, Stream, HtmlPdfOptions, CancellationToken)"/> did.</summary>
/// <param name="PageCount">The number of pages written.</param>
/// <param name="Diagnostics">What the layout, painter and writer approximated or left out.</param>
public sealed record HtmlPdfResult(int PageCount, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// The happy path: HTML in, a tagged PDF out, in one call. It is built only on the public low level (<see cref="HtmlPage"/>,
/// <see cref="PageFragmentation"/>, <see cref="Painter"/>, <see cref="DisplayListPdfWriter"/>); a caller who needs control uses those.
/// </summary>
/// <remarks>
/// The document is laid out for print media and cut into pages as <see cref="PagedDocument"/> does: <c>@page</c> rules give the page size
/// and margins (<see cref="HtmlPdfOptions.Page"/> is the default where no rule does), page-margin boxes carry running headers, footers and
/// page numbers, and links to <c>#id</c> become links to the page where the element starts.
/// </remarks>
public static class HtmlPdf
{
    /// <summary>Renders <paramref name="html"/> to PDF, writing to <paramref name="output"/> page by page.</summary>
    /// <param name="html">The document source, decoded to text.</param>
    /// <param name="output">Where the PDF goes. Closed afterwards unless <see cref="PdfOutputOptions.LeaveOpen"/> is set.</param>
    /// <param name="options">Page, output and hooks; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the render; the output is then incomplete.</param>
    /// <returns>The page count and the diagnostics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> or <paramref name="output"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page setup or an option is out of range.</exception>
    /// <exception cref="PdfRenderException">A limit is reached or a requested profile's rules are broken.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static HtmlPdfResult ToPdf(string html, Stream output, HtmlPdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(output);
        options ??= new HtmlPdfOptions();
        PdfPageSetup setup = options.Page;
        setup.Validate();

        var images = options.Resources is null ? null : new ImageStore(options.Resources, options.BaseUrl);
        var pagedOptions = new PagedOptions(options.Fonts ?? FontProviders.ForCurrentThread())
        {
            DefaultPage = new PageGeometry(setup.Width, setup.Height, setup.MarginTop, setup.MarginRight, setup.MarginBottom, setup.MarginLeft),
            Images = images,
            Replaced = images,
            BaseUrl = options.BaseUrl,
            Resources = options.Resources,
            Layout = options.Layout,
            MaxPages = Math.Min(options.Output.MaxPages, 100_000),
        };
        PagedDocument doc = PagedDocument.Create(html, pagedOptions, cancellationToken);
        if (doc.Truncated)
        {
            throw new PdfRenderException("The document would have more than " + options.Output.MaxPages + " pages.");
        }

        var list = new DisplayList();
        var painter = new Painter();
        doc.PaintContent(list, painter, options.Output.Tagged, cancellationToken);

        PdfOutputOptions o = Complete(options.Output, doc.Content);
        var diagnostics = new List<Diagnostic>();
        diagnostics.AddRange(doc.Diagnostics);
        diagnostics.AddRange(painter.Diagnostics.Items);
        using var writer = new DisplayListPdfWriter(output, o, images);
        var names = new List<string>(doc.Destinations.Count);
        foreach (PageDestination d in doc.Destinations)
        {
            names.Add(d.Name);
        }

        writer.DeclareDestinations(names);
        int next = 0;
        for (int i = 0; i < doc.PageCount; i++)
        {
            PageInfo p = doc.GetPage(i);
            PageGeometry g = p.Geometry;
            var ps = new PdfPageSetup(g.Width, g.Height, g.MarginTop, g.MarginRight, g.MarginBottom, g.MarginLeft);
            writer.AddPage(list, ps, p.ContentRegion, doc.GetPageLayer(i, cancellationToken), cancellationToken);
            for (; next < doc.Destinations.Count && doc.Destinations[next].PageIndex == i; next++)
            {
                writer.AddDestination(doc.Destinations[next].Name, i, ps, doc.Destinations[next].Y);
            }
        }

        writer.Finish(cancellationToken);
        diagnostics.AddRange(writer.Diagnostics.Items);
        return new HtmlPdfResult(doc.PageCount, diagnostics);
    }

    /// <summary>Renders <paramref name="html"/> to PDF bytes.</summary>
    /// <param name="html">The document source, decoded to text.</param>
    /// <param name="options">Page, output and hooks; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>The file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page setup or an option is out of range.</exception>
    /// <exception cref="PdfRenderException">A limit is reached or a requested profile's rules are broken.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static byte[] ToPdf(string html, HtmlPdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        PdfOutputOptions o = options?.Output ?? new PdfOutputOptions();
        HtmlPdfOptions inner = options is null
            ? new HtmlPdfOptions { Output = o.WithLeaveOpen() }
            : new HtmlPdfOptions { Page = options.Page, Output = o.WithLeaveOpen(), BaseUrl = options.BaseUrl, Resources = options.Resources, Fonts = options.Fonts, Layout = options.Layout };
        ToPdf(html, ms, inner, cancellationToken);
        return ms.ToArray();
    }

    private static PdfOutputOptions Complete(PdfOutputOptions o, HtmlPage page)
    {
        string? title = o.Title;
        string? lang = o.Language;
        RecursiveAI.OneHtml.Node root = page.Document.DocumentElement;
        if (!root.IsNull)
        {
            if (title is null)
            {
                RecursiveAI.OneHtml.Node t = root.QuerySelector("title");
                string? text = t.IsNull ? null : t.TextContent?.Trim();
                title = string.IsNullOrEmpty(text) ? null : text;
            }

            if (lang is null)
            {
                string? l = root.GetAttribute("lang");
                lang = string.IsNullOrWhiteSpace(l) ? null : l.Trim();
            }
        }

        return o.With(title, lang);
    }
}
