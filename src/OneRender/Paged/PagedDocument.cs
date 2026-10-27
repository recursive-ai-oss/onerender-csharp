using System.Globalization;
using System.Text;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.Text;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Paged;

/// <summary>What <see cref="PagedDocument.Create"/> needs to turn HTML into pages.</summary>
public sealed class PagedOptions
{
    /// <summary>Creates options with the required font provider.</summary>
    /// <param name="fonts">Chooses the faces text is shaped with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fonts"/> is null.</exception>
    public PagedOptions(IFontProvider fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        Fonts = fonts;
    }

    /// <summary>Chooses the faces text is shaped with.</summary>
    public IFontProvider Fonts { get; }

    /// <summary>The page size and margins where no <c>@page</c> rule says otherwise. Default <see cref="PageGeometry.A4"/>.</summary>
    public PageGeometry DefaultPage { get; init; } = PageGeometry.A4;

    /// <summary>The address the document is deemed to have; relative references resolve against it. Null for none.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>Returns the bytes of a style sheet, font file or image for an absolute address, or null. The engine never fetches anything.</summary>
    public Func<string, byte[]?>? Resources { get; init; }

    /// <summary>Tells the engine the natural size of replaced content such as images; null means unknown.</summary>
    public IResourceResolver? Replaced { get; init; }

    /// <summary>Supplies the images when painting; null means every image is missing.</summary>
    public IImageSource? Images { get; init; }

    /// <summary>The layout to run; null uses the engine's default.</summary>
    public ILayoutEngine? Layout { get; init; }

    /// <summary>The most pages produced, 1 to 100,000. Past it the rest of the content stays on the last page and <see cref="PagedDocument.Truncated"/> is set. Default 100,000.</summary>
    public int MaxPages { get; init; } = 100_000;
}

/// <summary>One page of a <see cref="PagedDocument"/>.</summary>
/// <param name="Number">The 1-based page number (blank pages count).</param>
/// <param name="Geometry">The page size and margins that apply to it.</param>
/// <param name="IsBlank">True for a page inserted so a <c>left</c>/<c>right</c>/<c>recto</c>/<c>verso</c> break lands on the right side.</param>
/// <param name="Name">The page name from the <c>page</c> property, or null.</param>
/// <param name="IsRight">True for a right-hand page.</param>
/// <param name="ContentRegion">The part of the content display list that is this page's page area, in CSS px.</param>
public readonly record struct PageInfo(int Number, PageGeometry Geometry, bool IsBlank, string? Name, bool IsRight, RectF ContentRegion);

/// <summary>Where an element that a link can point to starts.</summary>
/// <param name="Name">The element's <c>id</c>.</param>
/// <param name="PageIndex">The 0-based page it starts on.</param>
/// <param name="Y">Its distance below the top of the page area in CSS px.</param>
public readonly record struct PageDestination(string Name, int PageIndex, double Y);

/// <summary>
/// A document laid out as pages (CSS Paged Media 3, CSS Fragmentation 3, CSS GCPM 3). The content is laid out once at the page area's
/// width and cut into pages by the block fragmenter; <c>@page</c> rules give each page its size and margins and the page-margin boxes
/// their content, which is drawn as a separate layer per page. Page numbers, <c>string()</c> and <c>target-counter()</c> are resolved by
/// laying the document out again with the numbers the first layout found, at most three times.
/// </summary>
/// <remarks>
/// The content display list holds every page side by side (page <c>i</c> starts at x = <c>i</c> · <see cref="Stride"/>); a backend draws
/// each <see cref="PageInfo.ContentRegion"/> inside the page's margins and then, or before, the page layer from
/// <see cref="GetPageLayer"/>. Not thread-safe.
/// </remarks>
public sealed partial class PagedDocument
{
    private readonly PagedOptions _options;
    private readonly PageRuleSet _rules;
    private readonly PageInfo[] _pages;
    private readonly List<PageDestination> _destinations = [];
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly Dictionary<string, List<StringEntry>> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ContentList> _contentCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DisplayList> _layerCache = new(StringComparer.Ordinal);
    private readonly Dictionary<PageStyle, int> _styleIds = new(ReferenceEqualityComparer.Instance);
    private readonly DocumentStyles _styles;
    private readonly string _fontFaceCss;
    private readonly bool _firstRight;
    private readonly double _contentWidth;

    private struct StringEntry
    {
        public int Page;
        public bool AtTop;
        public string Value;
    }

    private PagedDocument(PagedOptions options, HtmlPage content, DocumentStyles styles, PageRuleSet rules, PageInfo[] pages, bool truncated, bool firstRight, double contentWidth)
    {
        _options = options;
        Content = content;
        _styles = styles;
        _rules = rules;
        _pages = pages;
        Truncated = truncated;
        _firstRight = firstRight;
        _contentWidth = contentWidth;
        _fontFaceCss = CollectFontFaces(styles);
    }

    /// <summary>The laid-out content: its fragment tree has one page fragment per page, side by side.</summary>
    public HtmlPage Content { get; }

    /// <summary>The number of pages, at least 1.</summary>
    public int PageCount => _pages.Length;

    /// <summary>The distance between the left edges of neighbouring pages in the content's space, in CSS px.</summary>
    public double Stride => PageFragmentation.PageStride(_contentWidth);

    /// <summary>True when <see cref="PagedOptions.MaxPages"/> was reached; the last page then holds all the remaining content.</summary>
    public bool Truncated { get; }

    /// <summary>The elements that a link in the document points to with <c>#id</c>, and where they start.</summary>
    public IReadOnlyList<PageDestination> Destinations => _destinations;

    /// <summary>What the layout, the page rules and the page layers approximated or left out.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>The page at the 0-based <paramref name="index"/>.</summary>
    /// <param name="index">0 to <see cref="PageCount"/> − 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public PageInfo GetPage(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _pages.Length);
        return _pages[index];
    }

    /// <summary>
    /// Parses <paramref name="html"/>, cascades it for print media, lays it out and cuts it into pages.
    /// </summary>
    /// <param name="html">The document source, decoded to text.</param>
    /// <param name="options">Fonts, page defaults and resource hooks.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The paged document.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The default page or <see cref="PagedOptions.MaxPages"/> is out of range.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static PagedDocument Create(string html, PagedOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(options);
        PageGeometry def = options.DefaultPage;
        if (!(def.ContentWidth >= 1) || !(def.ContentHeight >= 1) || !double.IsFinite(def.Width) || !double.IsFinite(def.Height) || def.Width > 14400 || def.Height > 14400)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The default page must leave a page area of at least 1 px and be at most 14,400 px.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxPages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxPages, 100_000);
        Document doc = Document.Parse(html, null, cancellationToken);
        HtmlPageOptions Layout(double w, double h) => new(options.Fonts)
        {
            ViewportWidth = w,
            ViewportHeight = h,
            BaseUrl = options.BaseUrl,
            StylesheetBytes = options.Resources,
            Replaced = options.Replaced,
            Images = options.Images,
            DevicePixelRatio = 1,
            Layout = options.Layout,
            PrintMedia = true,
        };

        HtmlPageOptions layoutOptions = Layout(def.ContentWidth, def.ContentHeight);
        DocumentStyles styles = HtmlPage.CreateStyles(doc, layoutOptions, cancellationToken);
        Node root = doc.DocumentElement;
        double em = 16;
        bool rtl = false;
        if (!root.IsNull)
        {
            ComputedStyle rs = styles.GetStyle(root);
            string fs = rs.GetPropertyValue("font-size");
            if (fs.EndsWith("px", StringComparison.Ordinal) && double.TryParse(fs.AsSpan(0, fs.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out double fpx) && fpx > 0)
            {
                em = fpx;
            }

            rtl = rs.GetPropertyValue("direction").Equals("rtl", StringComparison.OrdinalIgnoreCase);
        }

        PageRuleSet rules = PageRuleSet.Parse(styles, styles.Context.Media, def, em, cancellationToken);
        bool firstRight = !rtl;
        PageStyle first = rules.Resolve(null, 0, firstRight, false);
        double cw = first.Geometry.ContentWidth, ch = first.Geometry.ContentHeight;
        if (Math.Abs(cw - def.ContentWidth) > 0.01 || Math.Abs(ch - def.ContentHeight) > 0.01)
        {
            // Media queries and viewport units see the page area of the first page.
            layoutOptions = Layout(cw, ch);
            styles = HtmlPage.CreateStyles(doc, layoutOptions, cancellationToken);
            rules = PageRuleSet.Parse(styles, styles.Context.Media, def, em, cancellationToken);
        }

        var refs = new ReferenceTable();
        var sizer = new RuleSizer(rules, firstRight);
        HtmlPage page = null!;
        PaginationResult pr = null!;
        double stride = PageFragmentation.PageStride(cw);
        bool unstable = false;
        for (int pass = 0; pass < 3; pass++)
        {
            refs.NeedsPages = false;
            page = HtmlPage.BuildFromStyles(doc, styles, layoutOptions, refs, cancellationToken);
            pr = PageFragmentation.Paginate(page.Fragments, cw, sizer, firstRight, options.MaxPages, cancellationToken);
            bool depends = page.UsedPageReferences || refs.NeedsPages;
            if (!depends)
            {
                break;
            }

            bool changed = UpdateTargets(refs, page, pr.PageCount, stride);
            if (pass > 0 && !changed)
            {
                unstable = false;
                break;
            }

            unstable = pass == 2 && changed;
        }

        var pages = new PageInfo[pr.PageCount];
        for (int i = 0; i < pages.Length; i++)
        {
            bool right = IsRightPage(i, firstRight);
            PageStyle ps = rules.Resolve(pr.Names[i], i, right, pr.Blank[i]);
            pages[i] = new PageInfo(i + 1, ps.Geometry, pr.Blank[i], pr.Names[i], right, new RectF((float)(i * stride), 0, (float)cw, (float)ps.Geometry.ContentHeight));
        }

        var result = new PagedDocument(options, page, styles, rules, pages, pr.Truncated, firstRight, cw);
        result.Finish(refs, stride, unstable, cancellationToken);
        return result;
    }

    private static bool IsRightPage(int index, bool firstRight) => ((index & 1) == 0) == firstRight;

    private sealed class RuleSizer(PageRuleSet rules, bool firstRight) : IPageSizer
    {
        public double ContentHeight(int index, string? name, bool blank) =>
            rules.Resolve(name, index, IsRightPage(index, firstRight), blank).Geometry.ContentHeight;
    }

    // The page each id'd element starts on (1-based), for target-counter(); true when any of them, or the page count, changed.
    private static bool UpdateTargets(ReferenceTable refs, HtmlPage page, int pageCount, double stride)
    {
        bool changed = refs.Pages != pageCount;
        refs.Pages = pageCount;
        foreach (ReferenceTable.Target t in refs.Targets.Values)
        {
            int p = 0;
            if (page.Fragments.TryGetFirstBorderBox(t.Element, out Rect r))
            {
                p = Math.Clamp((int)Math.Floor((r.X + 0.5) / stride), 0, pageCount - 1) + 1;
            }

            if (p != t.Page)
            {
                t.Page = p;
                changed = true;
            }
        }

        return changed;
    }

    private void Finish(ReferenceTable refs, double stride, bool unstable, CancellationToken ct)
    {
        _diagnostics.AddRange(Content.Styled.Diagnostics.Items);
        _diagnostics.AddRange(_rules.Diagnostics.Items);
        if (Truncated)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticKind.LimitExceeded, "the document needs more than " + _options.MaxPages + " pages; the rest of the content is on the last page"));
        }

        if (unstable)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticKind.Unsupported, "page numbers in the content did not settle after three layouts; the last ones are used"));
        }

        for (int i = 0; i < _pages.Length; i++)
        {
            if (Math.Abs(_pages[i].Geometry.ContentWidth - _contentWidth) > 0.5)
            {
                _diagnostics.Add(new Diagnostic(DiagnosticKind.Unsupported, "page " + (i + 1) + " has a page area " + CssNumber(_pages[i].Geometry.ContentWidth) + " px wide but the content is laid out at " + CssNumber(_contentWidth) + " px; it is cut or padded to fit"));
                break;
            }
        }

        // string-set assignments by the page they land on.
        foreach (StringSetEvent e in refs.StringSets)
        {
            if (!Content.Fragments.TryGetFirstBorderBox(e.Element, out Rect r))
            {
                continue;
            }

            int p = Math.Clamp((int)Math.Floor((r.X + 0.5) / stride), 0, _pages.Length - 1);
            if (!_strings.TryGetValue(e.Name, out List<StringEntry>? list))
            {
                list = [];
                _strings[e.Name] = list;
            }

            list.Add(new StringEntry { Page = p, AtTop = r.Y < 1, Value = e.Value });
        }

        CollectDestinations(stride, ct);
    }

    // Elements that "#id" links point to, with the page and offset where each starts.
    private void CollectDestinations(double stride, CancellationToken ct)
    {
        Document doc = Content.Document;
        Node root = doc.DocumentElement;
        if (root.IsNull)
        {
            return;
        }

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<Node>();
        stack.Push(root);
        int visited = 0;
        while (stack.Count > 0)
        {
            if ((++visited & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            Node n = stack.Pop();
            if (n.IsElement && n.LocalName is "a" or "area")
            {
                string? href = n.GetAttribute("href");
                if (href is { Length: > 1 } && href[0] == '#' && wanted.Count < 100_000)
                {
                    wanted.Add(Uri.UnescapeDataString(href[1..]));
                }
            }

            for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.IsElement)
                {
                    stack.Push(c);
                }
            }
        }

        foreach (string id in wanted)
        {
            Node target = doc.GetElementById(id);
            if (target.IsNull || !Content.Fragments.TryGetFirstBorderBox(target, out Rect r))
            {
                continue;
            }

            int p = Math.Clamp((int)Math.Floor((r.X + 0.5) / stride), 0, _pages.Length - 1);
            _destinations.Add(new PageDestination(id, p, Math.Max(0, r.Y)));
        }

        _destinations.Sort(static (a, b) =>
        {
            int d = a.PageIndex.CompareTo(b.PageIndex);
            return d != 0 ? d : string.CompareOrdinal(a.Name, b.Name);
        });
    }

    private static string CssNumber(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Paints the content of every page into <paramref name="list"/>, which is cleared first.</summary>
    /// <param name="list">The list to fill; page <c>i</c> lies in <see cref="PageInfo.ContentRegion"/>.</param>
    /// <param name="painter">A painter to reuse; null makes a new one.</param>
    /// <param name="tags">Whether the list carries semantic tag scopes (for tagged PDF).</param>
    /// <param name="cancellationToken">Cancels the painting.</param>
    /// <exception cref="ArgumentNullException"><paramref name="list"/> is null.</exception>
    public void PaintContent(DisplayList list, Painter? painter = null, bool tags = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        (painter ?? new Painter()).Paint(
            Content.Fragments,
            list,
            new PaintOptions(_contentWidth, _pages[0].Geometry.ContentHeight)
            {
                Scroll = Content.Scroll,
                Images = _options.Images,
                Fonts = _options.Fonts,
                DevicePixelRatio = 1,
                Tags = tags,
                BaseUrl = _options.BaseUrl,
            },
            cancellationToken);
    }

    // @font-face rules of the document, as text, so the page layers can use the same web fonts.
    private static string CollectFontFaces(DocumentStyles styles)
    {
        var sb = new StringBuilder();
        var stack = new Stack<IEnumerator<CssNode>>();
        foreach (StyleSheetInfo info in styles.Sheets)
        {
            if (info.Origin == StyleOrigin.UserAgent)
            {
                continue;
            }

            stack.Push(((IEnumerable<CssNode>)info.Sheet.Rules).GetEnumerator());
            while (stack.Count > 0)
            {
                IEnumerator<CssNode> rules = stack.Peek();
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

                if (node.AtRuleKind == AtRuleKind.FontFace && sb.Length < (1 << 18))
                {
                    sb.Append(node.Raw).Append('\n');
                }
                else if (node.AtRuleKind is AtRuleKind.Layer or AtRuleKind.Supports && node.HasBlock && stack.Count < 8)
                {
                    stack.Push(node.Rules.GetEnumerator());
                }
            }
        }

        return sb.ToString();
    }
}
