using OneRender.Layout;
using OneRender.Painting;
using OneRender.Style;
using OneRender.Text;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender;

/// <summary>What <see cref="HtmlPage.Create(string, HtmlPageOptions, CancellationToken)"/> needs to turn HTML into a laid-out page.</summary>
public sealed class HtmlPageOptions
{
    /// <summary>Creates options with the required font provider.</summary>
    /// <param name="fonts">Chooses the faces text is shaped with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fonts"/> is null.</exception>
    public HtmlPageOptions(IFontProvider fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        Fonts = fonts;
    }

    /// <summary>Chooses the faces text is shaped with.</summary>
    public IFontProvider Fonts { get; }

    /// <summary>Layout viewport width in CSS px, at least 1. Default 800.</summary>
    public double ViewportWidth { get; init; } = 800;

    /// <summary>Layout viewport height in CSS px, at least 1. Default 600. Media queries and viewport units see it.</summary>
    public double ViewportHeight { get; init; } = 600;

    /// <summary>The address the document is deemed to have; relative references in the document resolve against it. Null for none.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Returns the bytes of a linked or imported style sheet for an absolute address, or null when the host does not have it.
    /// The engine never fetches anything. Null means no sheet is available.
    /// </summary>
    public Func<string, byte[]?>? StylesheetBytes { get; init; }

    /// <summary>Tells the engine the natural size of replaced content such as images; null means unknown.</summary>
    public IResourceResolver? Replaced { get; init; }

    /// <summary>Supplies the images that <c>url()</c> values and <c>img</c> elements refer to when painting; null means every image is missing (a diagnostic each).</summary>
    public IImageSource? Images { get; init; }

    /// <summary>Device pixels per CSS px, used to snap box edges to whole device pixels when painting. Default 1.</summary>
    public double DevicePixelRatio { get; init; } = 1;

    /// <summary>The layout to run; null uses <see cref="BlockLayoutEngine"/>.</summary>
    public ILayoutEngine? Layout { get; init; }

    /// <summary>Whether the user agent style sheet applies. Default true.</summary>
    public bool UserAgentStyles { get; init; } = true;
}

/// <summary>
/// HTML in, fragment tree and display list out: the engine's whole pipeline behind one object, built only from the public
/// low-level pieces (OneHtml parse and cascade, <see cref="HtmlFrontEnd"/>, <see cref="BoxTree"/>, <see cref="ILayoutEngine"/>,
/// <see cref="Painter"/>). The images and PDF backends and the test harnesses are all built on it.
/// </summary>
public sealed partial class HtmlPage
{
    private HtmlPage(Document document, StyledTree styled, BoxTree boxes, FragmentTree fragments, HtmlPageOptions options)
    {
        Document = document;
        Styled = styled;
        Boxes = boxes;
        Fragments = fragments;
        Options = options;
    }

    /// <summary>The parsed document.</summary>
    public Document Document { get; }

    /// <summary>The styled-node tree.</summary>
    public StyledTree Styled { get; }

    /// <summary>The box tree.</summary>
    public BoxTree Boxes { get; }

    /// <summary>The fragment tree produced by layout.</summary>
    public FragmentTree Fragments { get; }

    /// <summary>The options the page was created with.</summary>
    public HtmlPageOptions Options { get; }

    /// <summary>
    /// The scroll positions of the viewport and of scroll containers. <see cref="Paint"/> applies them; change them and call
    /// <see cref="Painter.PatchScroll(RetainedDisplayList)"/> to update a painted list without relayout.
    /// </summary>
    public ScrollState Scroll { get; private set; } = new();

    /// <summary>
    /// Defaults the HTML rendering section asks for that the family's user-agent sheet does not carry as rules. It is applied as a
    /// user-origin sheet, so author rules and presentational hints override it and the user-agent sheet does not.
    /// <para>HTML §15.3.2 (The page): when no <c>marginwidth</c>/<c>leftmargin</c>/<c>marginheight</c>/<c>topmargin</c> attribute gives
    /// the body's margins, "a default value of 8px is expected to be used".</para>
    /// </summary>
    internal const string UserAgentSupplement = "body { margin: 8px; }";

    /// <summary>Parses <paramref name="html"/>, cascades, builds the box tree and lays it out.</summary>
    /// <param name="html">The document source, decoded to text.</param>
    /// <param name="options">Viewport, fonts, resource hooks and layout.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The laid-out page.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A viewport dimension is below 1.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static HtmlPage Create(string html, HtmlPageOptions options, CancellationToken cancellationToken = default) =>
        CreateWithStyles(html, options, out _, cancellationToken);

    // Create, also handing back the cascade so an interactive page can restyle it for a new hover, focus or active element.
    internal static HtmlPage CreateWithStyles(string html, HtmlPageOptions options, out DocumentStyles styles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ViewportWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ViewportHeight, 1);
        cancellationToken.ThrowIfCancellationRequested();

        return CreateWithStyles(Document.Parse(html, null, cancellationToken), options, out styles, cancellationToken);
    }

    /// <summary>Cascades, builds the box tree and lays out a document the host has already parsed.</summary>
    /// <param name="document">The parsed document. The page keeps it and never changes it.</param>
    /// <param name="options">Viewport, fonts, resource hooks and layout.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The laid-out page; <see cref="Document"/> is <paramref name="document"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A viewport dimension is below 1.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static HtmlPage Create(Document document, HtmlPageOptions options, CancellationToken cancellationToken = default) =>
        CreateWithStyles(document, options, out _, cancellationToken);

    internal static HtmlPage CreateWithStyles(Document document, HtmlPageOptions options, out DocumentStyles styles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ViewportWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ViewportHeight, 1);
        cancellationToken.ThrowIfCancellationRequested();
        var context = new StyleContext { Media = new MediaFeatures { Width = options.ViewportWidth, Height = options.ViewportHeight } };
        Func<string, byte[]?>? bytes = options.StylesheetBytes;
        var sourceOptions = new StyleSourceOptions
        {
            IncludeUserAgentSheet = options.UserAgentStyles,
            UserSheets = options.UserAgentStyles ? [Stylesheet.Parse(UserAgentSupplement, null, null, cancellationToken)] : null,
            BaseUrl = options.BaseUrl,
            DeclarationValidator = StyleValidation.Validator,
            Resolver = bytes is null ? null : url =>
            {
                byte[]? b = bytes(url);
                return b is null ? null : new StylesheetResource(b);
            },
        };
        styles = DocumentStyles.Create(document, context, sourceOptions, cancellationToken);

        // @font-face rules of the document's sheets belong to this document only; the files come through the same host lookup as sheets.
        FontCatalog catalog = options.Fonts.Catalog;
        IFontResourceResolver? savedResolver = catalog.Resolver;
        catalog.ClearFontFaces();
        if (bytes is not null)
        {
            catalog.Resolver = new PageFontResolver(bytes, options.BaseUrl);
        }

        try
        {
            AddFontFaces(styles, catalog, context.Media, cancellationToken);
            return Build(document, styles, options, null, cancellationToken);
        }
        finally
        {
            catalog.Resolver = savedResolver;
        }
    }

    private static HtmlPage Build(Document document, DocumentStyles styles, HtmlPageOptions options, ScrollState? scroll, CancellationToken cancellationToken)
    {
        var table = new StyleTable();
        table.SetViewport(options.ViewportWidth, options.ViewportHeight);
        table.Fonts = options.Fonts;
        var styled = new StyledTree(table) { Resolver = options.Replaced };
        new HtmlFrontEnd(styled).Build(styles, cancellationToken);
        var boxes = new BoxTree(styled);
        boxes.Build(cancellationToken);
        var fragments = new FragmentTree(boxes);
        ILayoutEngine engine = options.Layout ?? new BlockLayoutEngine();
        engine.Layout(boxes, new LayoutInput(options.ViewportWidth, options.ViewportHeight, options.Fonts), fragments, cancellationToken);
        var page = new HtmlPage(document, styled, boxes, fragments, options);
        if (scroll is not null)
        {
            page.Scroll = scroll;
        }

        return page;
    }

    private const int MaxFontFaceRules = 1024;
    private const int MaxRuleDepth = 32;

    // CSS Fonts 4 §4.1: @font-face may sit at the top level or inside conditional group rules. Groups whose condition fails are skipped.
    private static void AddFontFaces(DocumentStyles styles, FontCatalog catalog, MediaFeatures media, CancellationToken cancellationToken)
    {
        int added = 0;
        var stack = new Stack<(IEnumerator<CssNode> Rules, string? BaseUrl)>();
        foreach (StyleSheetInfo info in styles.Sheets)
        {
            stack.Push((((IEnumerable<CssNode>)info.Sheet.Rules).GetEnumerator(), info.Url));
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (IEnumerator<CssNode> rules, string? baseUrl) = stack.Peek();
                if (!rules.MoveNext())
                {
                    rules.Dispose();
                    stack.Pop();
                    continue;
                }

                CssNode node = rules.Current;
                if (node.Kind != CssNodeKind.AtRule)
                {
                    continue;
                }

                switch (node.AtRuleKind)
                {
                    case AtRuleKind.FontFace:
                        if (added < MaxFontFaceRules && FontFaceRule.FromNode(node, baseUrl, catalog.Diagnostics) is { } rule && catalog.AddFontFace(rule))
                        {
                            added++;
                        }

                        break;
                    case AtRuleKind.Media:
                        if (node.HasBlock && stack.Count < MaxRuleDepth && MediaQueryList.Parse(node.Prelude.Trimmed()).Matches(media))
                        {
                            stack.Push((node.Rules.GetEnumerator(), baseUrl));
                        }

                        break;
                    case AtRuleKind.Layer:
                    case AtRuleKind.Supports:
                        if (node.HasBlock && stack.Count < MaxRuleDepth)
                        {
                            stack.Push((node.Rules.GetEnumerator(), baseUrl));
                        }

                        break;
                }
            }
        }
    }

    private sealed class PageFontResolver(Func<string, byte[]?> bytes, string? documentBase) : IFontResourceResolver
    {
        public byte[]? LoadFont(string url, string? baseUrl) => bytes(ResourceAddress.Resolve(baseUrl ?? documentBase, url));
    }

    /// <summary>Paints the page into <paramref name="list"/>, which is cleared first.</summary>
    /// <param name="list">The list to fill.</param>
    /// <param name="painter">A painter to reuse; null makes a new one.</param>
    /// <param name="cancellationToken">Cancels the painting.</param>
    public void Paint(DisplayList list, Painter? painter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        (painter ?? new Painter()).Paint(Fragments, list, new PaintOptions(Options.ViewportWidth, Options.ViewportHeight) { Scroll = Scroll, Images = Options.Images, Fonts = Options.Fonts, DevicePixelRatio = Options.DevicePixelRatio }, cancellationToken);
    }

    /// <summary>
    /// The border box of every element that generated one, keyed by element path (see <see cref="ElementBox"/>), in document order.
    /// </summary>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The boxes.</returns>
    public IReadOnlyList<ElementBox> ElementBoxes(CancellationToken cancellationToken = default)
    {
        var result = new List<ElementBox>();
        Node root = Document.DocumentElement;
        if (root.IsNull)
        {
            return result;
        }

        // Depth-first over elements with an explicit stack of (node, its path).
        var stack = new Stack<(Node Node, string Path)>();
        stack.Push((root, "0"));
        int visited = 0;
        while (stack.Count > 0)
        {
            if ((++visited & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            (Node node, string path) = stack.Pop();
            if (Fragments.TryGetBorderBox(node, out Rect r))
            {
                result.Add(new ElementBox(path, node.LocalName, r.X, r.Y, r.Width, r.Height));
            }

            // Children are pushed in reverse so they pop in document order.
            int count = 0;
            for (Node c = node.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.IsElement)
                {
                    count++;
                }
            }

            var kids = new (Node, string)[count];
            int k = 0;
            for (Node c = node.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.IsElement)
                {
                    kids[k] = (c, path + "/" + k.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    k++;
                }
            }

            for (int j = kids.Length - 1; j >= 0; j--)
            {
                stack.Push(kids[j]);
            }
        }

        return result;
    }
}
