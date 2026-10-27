using OneRender.Interaction;
using OneRender.Painting;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Accessibility;

/// <summary>The facts of a page rendered by <see cref="AccessibilityFacts.Render(string, HtmlPageOptions, FactsOptions?, CancellationToken)"/>, with what they are keyed by.</summary>
/// <param name="Document">The document the facts are about; query the facts with its nodes.</param>
/// <param name="Page">The laid-out page.</param>
/// <param name="Facts">The rendered facts.</param>
public sealed record RenderedPage(Document Document, HtmlPage Page, RenderedFacts Facts);

/// <summary>Raised when the facts the engine wrote do not map onto the document, which means a defect in this package.</summary>
public sealed class RenderedFactsException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="refusal">What OneHtml.Accessibility refused and why.</param>
    public RenderedFactsException(SnapshotRefusal refusal)
        : base("The rendered facts do not map onto the document: " + (refusal ?? throw new ArgumentNullException(nameof(refusal))))
    {
        Refusal = refusal;
    }

    /// <summary>What the mapping refused, and where.</summary>
    public SnapshotRefusal Refusal { get; }
}

/// <summary>
/// Produces <see cref="RenderedFacts"/> from OneRender's own layout and paint, with the same meaning per fact as the facts
/// OneHtml.Accessibility reads from a browser's <c>DOMSnapshot.captureSnapshot</c>, so a consumer can switch sources without
/// changing its rules. Three levels, each built on the one below: <see cref="FromLayout"/> for a page the host already laid
/// out and painted, <see cref="FromPage"/> and <see cref="FromInteractive"/> for a page object, and
/// <see cref="Render(string, HtmlPageOptions, FactsOptions?, CancellationToken)"/> for HTML in, facts out.
/// </summary>
/// <remarks>
/// The facts describe the page as it is now: <see cref="FromInteractive"/> on a page with an element hovered or focused gives the
/// facts of that state. Time is not an input; the engine reads no clock.
/// </remarks>
public static class AccessibilityFacts
{
    /// <summary>
    /// Facts for a page that was laid out and painted by the host. <paramref name="trace"/> must have been attached to the
    /// painter that filled <paramref name="list"/> from this page's fragments, so that paint order and the colors behind text come
    /// from the very list the host draws.
    /// </summary>
    /// <param name="page">The laid-out page.</param>
    /// <param name="queries">CSSOM View queries for the page (offset, client and scroll rectangles).</param>
    /// <param name="list">The display list painted from <paramref name="page"/>.</param>
    /// <param name="trace">The paint trace recorded while painting <paramref name="list"/>.</param>
    /// <param name="options">Which computed styles to include and the bounds; null for the defaults.</param>
    /// <param name="context">The style context the page was styled under, recorded on the facts; null for none.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The facts, keyed by the nodes of <c>page.Document</c>.</returns>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="options"/> or <paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="trace"/> was not recorded from this page's fragments.</exception>
    /// <exception cref="DomSnapshotFormatException">The page is over a limit in <see cref="FactsOptions.Limits"/>.</exception>
    /// <exception cref="RenderedFactsException">The facts did not map onto the document.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static RenderedFacts FromLayout(HtmlPage page, LayoutQueries queries, DisplayList list, PaintTrace trace, FactsOptions? options = null, StyleContext? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(trace);
        if (trace.FragmentCount != page.Fragments.Count)
        {
            throw new ArgumentException("The paint trace was not recorded from this page's fragment tree.", nameof(trace));
        }

        cancellationToken.ThrowIfCancellationRequested();
        options ??= new FactsOptions();
        var writer = new SnapshotWriter(page, queries, list, trace, options);
        byte[] json = writer.Write(cancellationToken);
        DomSnapshot snapshot = DomSnapshot.Parse(json, writer.StyleNames, options.Limits);
        DomSnapshotMapResult mapped = DomSnapshotAdapter.Map(
            snapshot,
            page.Document,
            new DomSnapshotMapOptions
            {
                ViewportWidth = page.Options.ViewportWidth,
                ViewportHeight = page.Options.ViewportHeight,
                Context = context,
            },
            cancellationToken);
        return mapped.Facts ?? throw new RenderedFactsException(mapped.Refusal!);
    }

    /// <summary>Paints <paramref name="page"/> and returns its facts.</summary>
    /// <param name="page">The laid-out page.</param>
    /// <param name="options">Which computed styles to include and the bounds; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The facts, keyed by the nodes of <c>page.Document</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is null.</exception>
    /// <exception cref="DomSnapshotFormatException">The page is over a limit in <see cref="FactsOptions.Limits"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static RenderedFacts FromPage(HtmlPage page, FactsOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var hits = new HitTester(page.Fragments, page.Scroll, page.Options.ViewportWidth, page.Options.ViewportHeight);
        hits.Rebuild();
        return Paint(page, new LayoutQueries(page, hits), options, null, cancellationToken);
    }

    /// <summary>
    /// Facts for an interactive page in its present hover, active and focus state: the page is already styled and laid out for
    /// that state, so the facts show what a user would see.
    /// </summary>
    /// <param name="page">The interactive page.</param>
    /// <param name="options">Which computed styles to include and the bounds; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The facts, keyed by the nodes of <c>page.Page.Document</c>; their <see cref="RenderedFacts.Context"/> is the page's style context.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is null.</exception>
    /// <exception cref="DomSnapshotFormatException">The page is over a limit in <see cref="FactsOptions.Limits"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static RenderedFacts FromInteractive(InteractivePage page, FactsOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        return Paint(page.Page, page.Layout, options, page.StyleContext, cancellationToken);
    }

    /// <summary>Parses, lays out and paints <paramref name="html"/> and returns the facts about its document.</summary>
    /// <param name="html">The document source, decoded to text.</param>
    /// <param name="page">Viewport, fonts and resource hooks, as for <see cref="HtmlPage.Create(string, HtmlPageOptions, CancellationToken)"/>.</param>
    /// <param name="options">Which computed styles to include and the bounds; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The document, the page and the facts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> or <paramref name="page"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A viewport dimension is below 1.</exception>
    /// <exception cref="DomSnapshotFormatException">The page is over a limit in <see cref="FactsOptions.Limits"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static RenderedPage Render(string html, HtmlPageOptions page, FactsOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(page);
        HtmlPage laidOut = HtmlPage.Create(html, page, cancellationToken);
        return new RenderedPage(laidOut.Document, laidOut, FromPage(laidOut, options, cancellationToken));
    }

    /// <summary>Lays out and paints a document the caller has parsed and returns the facts about that document's nodes.</summary>
    /// <param name="document">The parsed document. It is read, never changed.</param>
    /// <param name="page">Viewport, fonts and resource hooks.</param>
    /// <param name="options">Which computed styles to include and the bounds; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The document, the page and the facts.</returns>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A viewport dimension is below 1.</exception>
    /// <exception cref="DomSnapshotFormatException">The page is over a limit in <see cref="FactsOptions.Limits"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static RenderedPage Render(Document document, HtmlPageOptions page, FactsOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(page);
        HtmlPage laidOut = HtmlPage.Create(document, page, cancellationToken);
        return new RenderedPage(document, laidOut, FromPage(laidOut, options, cancellationToken));
    }

    private static RenderedFacts Paint(HtmlPage page, LayoutQueries queries, FactsOptions? options, StyleContext? context, CancellationToken cancellationToken)
    {
        var trace = new PaintTrace();
        var list = new DisplayList();
        page.Paint(list, new Painter { Observer = trace }, cancellationToken);
        return FromLayout(page, queries, list, trace, options, context, cancellationToken);
    }
}
