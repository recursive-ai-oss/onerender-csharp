using OneRender.Layout;
using OneRender.Painting;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender;

/// <summary>What <see cref="HtmlPage.Create"/> needs to turn HTML into a laid-out page.</summary>
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

    /// <summary>The layout to run; null uses <see cref="StackingLayoutEngine"/>.</summary>
    public ILayoutEngine? Layout { get; init; }

    /// <summary>Whether the user agent style sheet applies. Default true.</summary>
    public bool UserAgentStyles { get; init; } = true;
}

/// <summary>
/// HTML in, fragment tree and display list out: the engine's whole pipeline behind one object, built only from the public
/// low-level pieces (OneHtml parse and cascade, <see cref="HtmlFrontEnd"/>, <see cref="BoxTree"/>, <see cref="ILayoutEngine"/>,
/// <see cref="Painter"/>). The images and PDF backends and the test harnesses are all built on it.
/// </summary>
public sealed class HtmlPage
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

    /// <summary>Parses <paramref name="html"/>, cascades, builds the box tree and lays it out.</summary>
    /// <param name="html">The document source, decoded to text.</param>
    /// <param name="options">Viewport, fonts, resource hooks and layout.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The laid-out page.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A viewport dimension is below 1.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static HtmlPage Create(string html, HtmlPageOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ViewportWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ViewportHeight, 1);
        cancellationToken.ThrowIfCancellationRequested();

        Document document = Document.Parse(html, null, cancellationToken);
        var context = new StyleContext { Media = new MediaFeatures { Width = options.ViewportWidth, Height = options.ViewportHeight } };
        Func<string, byte[]?>? bytes = options.StylesheetBytes;
        var sourceOptions = new StyleSourceOptions
        {
            IncludeUserAgentSheet = options.UserAgentStyles,
            BaseUrl = options.BaseUrl,
            Resolver = bytes is null ? null : url =>
            {
                byte[]? b = bytes(url);
                return b is null ? null : new StylesheetResource(b);
            },
        };
        DocumentStyles styles = DocumentStyles.Create(document, context, sourceOptions, cancellationToken);

        var table = new StyleTable();
        table.SetViewport(options.ViewportWidth, options.ViewportHeight);
        var styled = new StyledTree(table) { Resolver = options.Replaced };
        new HtmlFrontEnd(styled).Build(styles, cancellationToken);
        var boxes = new BoxTree(styled);
        boxes.Build(cancellationToken);
        var fragments = new FragmentTree(boxes);
        ILayoutEngine engine = options.Layout ?? new StackingLayoutEngine();
        engine.Layout(boxes, new LayoutInput(options.ViewportWidth, options.ViewportHeight, options.Fonts), fragments, cancellationToken);
        return new HtmlPage(document, styled, boxes, fragments, options);
    }

    /// <summary>Paints the page into <paramref name="list"/>, which is cleared first.</summary>
    /// <param name="list">The list to fill.</param>
    /// <param name="painter">A painter to reuse; null makes a new one.</param>
    /// <param name="cancellationToken">Cancels the painting.</param>
    public void Paint(DisplayList list, Painter? painter = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        (painter ?? new Painter()).Paint(Fragments, list, new PaintOptions(Options.ViewportWidth, Options.ViewportHeight), cancellationToken);
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
