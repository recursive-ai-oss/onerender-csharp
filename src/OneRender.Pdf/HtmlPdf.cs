using System.IO;
using OneRender.Image;
using OneRender.Layout;
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
/// The document is laid out once at the page's content width and cut into pages by the block fragmenter, which honours
/// <c>break-before</c>, <c>break-after</c>, <c>break-inside</c>, <c>orphans</c> and <c>widows</c>. <c>@page</c> rules, margin boxes
/// and page counters are not applied.
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
        var pageOptions = new HtmlPageOptions(options.Fonts ?? FontProviders.ForCurrentThread())
        {
            Images = images,
            Replaced = images,
            DevicePixelRatio = 1,
            ViewportWidth = setup.ContentWidth,
            ViewportHeight = setup.ContentHeight,
            BaseUrl = options.BaseUrl,
            StylesheetBytes = options.Resources,
            Layout = options.Layout,
        };
        HtmlPage page = HtmlPage.Create(html, pageOptions, cancellationToken);
        int pages = PageFragmentation.Paginate(page.Fragments, setup.ContentWidth, setup.ContentHeight, cancellationToken);
        var list = new DisplayList();
        var painter = new Painter();
        painter.Paint(
            page.Fragments,
            list,
            new PaintOptions(setup.ContentWidth, setup.ContentHeight)
            {
                Scroll = page.Scroll,
                Images = images,
                Fonts = page.Options.Fonts,
                DevicePixelRatio = 1,
                Tags = options.Output.Tagged,
                BaseUrl = options.BaseUrl,
            },
            cancellationToken);

        PdfOutputOptions o = Complete(options.Output, page);
        double stride = PageFragmentation.PageStride(setup.ContentWidth);
        var diagnostics = new List<Diagnostic>();
        diagnostics.AddRange(page.Styled.Diagnostics.Items);
        diagnostics.AddRange(painter.Diagnostics.Items);
        using var writer = new DisplayListPdfWriter(output, o, images);
        for (int i = 0; i < pages; i++)
        {
            writer.AddPage(list, setup, new RectF((float)(i * stride), 0, (float)setup.ContentWidth, (float)setup.ContentHeight), cancellationToken);
        }

        writer.Finish(cancellationToken);
        diagnostics.AddRange(writer.Diagnostics.Items);
        return new HtmlPdfResult(pages, diagnostics);
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
