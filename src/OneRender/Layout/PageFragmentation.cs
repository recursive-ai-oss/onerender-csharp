namespace OneRender.Layout;

/// <summary>
/// Cuts a laid-out flow into pages. The document is laid out once as a single tall strip at the page's content width (a viewport of
/// that width), and <see cref="Paginate(FragmentTree, double, double, CancellationToken)"/> then rewrites the fragment tree: the root's children become one page fragment per
/// page, each holding the pieces of the boxes that fall on it. Pages sit side by side in the tree's coordinate space, so one
/// <see cref="Painting.DisplayList"/> holds the whole document and a backend draws one region per page.
/// </summary>
public static class PageFragmentation
{
    /// <summary>
    /// Fragments the flow of <paramref name="tree"/> into pages of <paramref name="pageHeight"/> px.
    /// </summary>
    /// <param name="tree">A laid-out tree whose root was laid out at the page content width. It is rewritten in place and must not be paginated twice.</param>
    /// <param name="pageWidth">The content width in px (the width the document was laid out at).</param>
    /// <param name="pageHeight">The content height of a page in px, at least 1.</param>
    /// <param name="cancellationToken">Checked every few hundred fragments.</param>
    /// <returns>The number of pages, at least 1. Page <c>i</c> occupies the region starting at x = <see cref="PageStride"/>(<paramref name="pageWidth"/>) · <c>i</c>, y = 0, of size <paramref name="pageWidth"/> × <paramref name="pageHeight"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size is not finite or below 1.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static int Paginate(FragmentTree tree, double pageWidth, double pageHeight, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (!(pageWidth >= 1) || !double.IsFinite(pageWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(pageWidth));
        }

        if (!(pageHeight >= 1) || !double.IsFinite(pageHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(pageHeight));
        }

        if (tree.Count == 0)
        {
            return 1;
        }

        var fragmenter = new Fragmenter();
        fragmenter.BeginPages(tree, 0, cancellationToken);
        double[] sizes = [pageHeight];
        int pages = fragmenter.PlanPages(pageWidth, sizes, false, true, Fragmenter.MaxFragmentainers, PageStride(pageWidth), out _);
        if (pages > 0)
        {
            fragmenter.Apply(tree, 0, cancellationToken);
        }

        return Math.Max(1, pages);
    }

    /// <summary>
    /// Cuts the flow into pages whose sizes depend on the page's position (first, left, right), name and blankness, as <paramref name="sizer"/>
    /// says. Page names can change a page's height, and the page names depend on where the pages break, so the plan is repeated (at most
    /// four times) until every page has the height its own name asks for; a flow that does not settle keeps the last plan.
    /// </summary>
    internal static PaginationResult Paginate(FragmentTree tree, double contentWidth, IPageSizer sizer, bool firstPageRight, int maxPages, CancellationToken cancellationToken)
    {
        var fragmenter = new Fragmenter();
        if (tree.Count == 0)
        {
            return new PaginationResult(1, [false], [null], false);
        }

        fragmenter.BeginPages(tree, 0, cancellationToken);
        double stride = PageStride(contentWidth);
        string?[] names = [];
        bool[] blank = [];
        int pages = 0;
        bool truncated = false;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            // Explicit sizes for the pages known so far, then the first/left/right pattern repeats from the last two.
            int n = Math.Max(3, pages);
            double[] sizes = new double[n];
            for (int i = 0; i < n; i++)
            {
                sizes[i] = sizer.ContentHeight(i, i < names.Length ? names[i] : null, i < blank.Length && blank[i]);
            }

            pages = fragmenter.PlanPages(contentWidth, sizes, true, firstPageRight, maxPages, stride, out truncated);
            names = new string?[pages];
            blank = new bool[pages];
            bool settled = true;
            for (int i = 0; i < pages; i++)
            {
                blank[i] = fragmenter.IsBlankPage(i);
                names[i] = blank[i] ? null : fragmenter.PageNameAt(tree, 0, fragmenter.PageStart(i));
            }

            for (int i = 0; i < pages && settled; i++)
            {
                double want = sizer.ContentHeight(i, names[i], blank[i]);
                double used = i < n ? sizes[i] : sizes[n - 2 + ((i - n) & 1)];
                settled = Math.Abs(want - used) < 0.01;
            }

            if (settled)
            {
                break;
            }
        }

        if (pages > 0)
        {
            fragmenter.Apply(tree, 0, cancellationToken);
        }

        return new PaginationResult(Math.Max(1, pages), blank, names, truncated);
    }

    /// <summary>The distance between the left edges of two neighbouring pages in the tree's space: the page width plus half again, so content that overflows a page sideways does not reach the next page's region.</summary>
    /// <param name="pageWidth">The content width in px.</param>
    /// <returns>The stride in px.</returns>
    public static double PageStride(double pageWidth) => pageWidth * 1.5;
}


/// <summary>Answers how tall the content area of a page is, for the page planner.</summary>
internal interface IPageSizer
{
    /// <summary>The content height in px of page <paramref name="index"/> (0-based), given its page name (null for none) and whether it is a blank page inserted for a side break.</summary>
    double ContentHeight(int index, string? name, bool blank);
}

/// <summary>What <see cref="PageFragmentation.Paginate(FragmentTree, double, IPageSizer, bool, int, CancellationToken)"/> decided.</summary>
internal sealed class PaginationResult
{
    public PaginationResult(int count, bool[] blank, string?[] names, bool truncated)
    {
        PageCount = count;
        Blank = blank;
        Names = names;
        Truncated = truncated;
    }

    /// <summary>The number of pages, at least 1.</summary>
    public int PageCount { get; }

    /// <summary>Per page: a blank page inserted so a <c>left</c>/<c>right</c>/<c>recto</c>/<c>verso</c> break lands on the right side.</summary>
    public bool[] Blank { get; }

    /// <summary>Per page: the page name of its first content, or null.</summary>
    public string?[] Names { get; }

    /// <summary>True when the page limit stopped the planning; the last page then holds all the remaining content.</summary>
    public bool Truncated { get; }
}
