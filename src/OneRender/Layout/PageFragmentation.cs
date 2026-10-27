namespace OneRender.Layout;

/// <summary>
/// Cuts a laid-out flow into pages. The document is laid out once as a single tall strip at the page's content width (a viewport of
/// that width), and <see cref="Paginate"/> then rewrites the fragment tree: the root's children become one page fragment per
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
        int pages = fragmenter.PlanPages(tree, 0, pageWidth, pageHeight, PageStride(pageWidth), cancellationToken);
        if (pages > 0)
        {
            fragmenter.Apply(tree, 0, cancellationToken);
        }

        return Math.Max(1, pages);
    }

    /// <summary>The distance between the left edges of two neighbouring pages in the tree's space: the page width plus half again, so content that overflows a page sideways does not reach the next page's region.</summary>
    /// <param name="pageWidth">The content width in px.</param>
    /// <returns>The stride in px.</returns>
    public static double PageStride(double pageWidth) => pageWidth * 1.5;
}
