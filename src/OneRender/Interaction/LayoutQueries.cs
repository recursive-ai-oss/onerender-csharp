using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

/// <summary>
/// The geometry queries of CSSOM View for the elements of a laid-out page, answered from the fragment tree. A query over a <see cref="LivePage"/> first
/// flushes the page's pending edits (a forced layout, as in a browser); an <see cref="HtmlPage"/> applies its changes as they happen. Lengths in CSS px; the integer members round the way the specification says
/// (CSSOM View §6: the value is rounded to the nearest integer, halves away from zero).
/// </summary>
/// <remarks>
/// Rectangles from <see cref="GetBoundingClientRect(Node)"/> and <see cref="GetClientRects(Node, Span{Rect})"/> are in viewport
/// coordinates: they follow scrolling and transforms. The <c>Offset*</c>, <c>Client*</c> and <c>Scroll*</c> sizes are layout values
/// that ignore transforms. A node that generated no box (for example <c>display: none</c>) answers zero, empty or null.
/// </remarks>
public sealed class LayoutQueries
{
    private readonly HtmlPage? _html;
    private readonly LivePage? _live;
    private readonly StyledTree _styled;
    private readonly BoxTree _boxes;
    private readonly FragmentTree _fragments;
    private readonly ScrollState _scroll;
    private readonly HitTester _hits;

    /// <summary>Creates the queries for a page.</summary>
    /// <param name="page">The laid-out page.</param>
    /// <param name="hits">A hit tester for the page's fragment tree, used for the scroll and transform mapping.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The hit tester is for another fragment tree.</exception>
    public LayoutQueries(HtmlPage page, HitTester hits)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(hits);
        if (!ReferenceEquals(page.Fragments, hits.Tree))
        {
            throw new ArgumentException("The hit tester is for another page.", nameof(hits));
        }

        _html = page;
        _styled = page.Styled;
        _boxes = page.Boxes;
        _fragments = page.Fragments;
        _scroll = page.Scroll;
        _hits = hits;
    }

    /// <summary>Creates the queries for a page built from a styled-node tree; every query first flushes the page's pending edits.</summary>
    /// <param name="page">The live page.</param>
    /// <param name="hits">A hit tester for the page's fragment tree, used for the scroll and transform mapping.</param>
    /// <remarks>Only the members that take a <see cref="StyledNode"/> answer for such a page; the <see cref="Node"/> members return their "no box" answers because there is no document.</remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The hit tester is for another fragment tree.</exception>
    public LayoutQueries(LivePage page, HitTester hits)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(hits);
        if (!ReferenceEquals(page.Fragments, hits.Tree))
        {
            throw new ArgumentException("The hit tester is for another page.", nameof(hits));
        }

        _live = page;
        _styled = page.Styled;
        _boxes = page.Boxes;
        _fragments = page.Fragments;
        _scroll = page.Scroll;
        _hits = hits;
    }

    // CSSOM View: a geometry query sees the effect of every edit made before it. The edits are applied here, and only what they
    // touched is laid out again.
    private void Sync()
    {
        if (_live is { NeedsFlush: true } live)
        {
            live.Flush();
        }

        _hits.EnsureBuilt();
    }

    private double ViewportWidth => _html?.Options.ViewportWidth ?? _live!.ViewportWidth;

    private double ViewportHeight => _html?.Options.ViewportHeight ?? _live!.ViewportHeight;

    private StyledNode StyledOf(Node element)
    {
        Sync();
        return _html is null || element.IsNull || !ReferenceEquals(element.Document, _html.Document) ? default : _styled.FindBySource(element.Index);
    }

    private Box BoxOf(StyledNode s) => s.IsNull ? default : _boxes.PrincipalBox(s);

    private Fragment FirstFragment(StyledNode s) => _fragments.FragmentOf(BoxOf(s));

    private bool IsRoot(StyledNode s) => s.Equals(_styled.Root);

    private static int Round(double v) => double.IsNaN(v) ? 0 : (int)Math.Clamp(Math.Round(v, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);

    // ------------------------------------------------------------------ rectangles

    /// <summary><c>getClientRects()</c>: the border box of each fragment of the element in the viewport, in tree order; an inline box split across lines gives one per piece.</summary>
    /// <param name="element">The element.</param>
    /// <param name="rects">Receives the rectangles; extra ones are dropped.</param>
    /// <returns>The number written.</returns>
    public int GetClientRects(Node element, Span<Rect> rects) => GetClientRects(StyledOf(element), rects);

    /// <summary><see cref="GetClientRects(Node, Span{Rect})"/> for a styled node.</summary>
    /// <param name="element">The styled element.</param>
    /// <param name="rects">Receives the rectangles.</param>
    /// <returns>The number written.</returns>
    public int GetClientRects(StyledNode element, Span<Rect> rects)
    {
        Sync();
        int n = 0;
        FragmentTree tree = _fragments;
        for (Box box = BoxOf(element); !box.IsNull && n < rects.Length; box = box.NextPiece)
        {
            for (Fragment f = tree.FragmentOf(box); !f.IsNull && n < rects.Length; f = f.NextOfBox)
            {
                rects[n++] = _hits.ToViewport(f, _hits.AbsoluteBox(f.Index));
            }
        }

        return n;
    }

    /// <summary>
    /// <c>getBoundingClientRect()</c>: the smallest rectangle in the viewport that contains the element's client rects, ignoring empty
    /// ones unless all are empty; all zero when the element has none.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The rectangle.</returns>
    public Rect GetBoundingClientRect(Node element) => GetBoundingClientRect(StyledOf(element));

    /// <summary><see cref="GetBoundingClientRect(Node)"/> for a styled node.</summary>
    /// <param name="element">The styled element.</param>
    /// <returns>The rectangle.</returns>
    public Rect GetBoundingClientRect(StyledNode element)
    {
        Sync();
        bool any = false, anyNonEmpty = false;
        Rect all = default, nonEmpty = default;
        FragmentTree tree = _fragments;
        for (Box box = BoxOf(element); !box.IsNull; box = box.NextPiece)
        {
            for (Fragment f = tree.FragmentOf(box); !f.IsNull; f = f.NextOfBox)
            {
                Rect r = _hits.ToViewport(f, _hits.AbsoluteBox(f.Index));
                all = any ? all.Union(r) : r;
                any = true;
                if (r.Width > 0 || r.Height > 0)
                {
                    nonEmpty = anyNonEmpty ? nonEmpty.Union(r) : r;
                    anyNonEmpty = true;
                }
            }
        }

        return anyNonEmpty ? nonEmpty : (any ? all : default);
    }

    internal Rect ViewportRectOf(Node element) => GetBoundingClientRect(StyledOf(element));

    // ------------------------------------------------------------------ offset*

    // The border boxes of all fragments of the principal box, in layout space, ignoring transforms and scrolling.
    private bool LayoutBox(StyledNode s, out Rect union)
    {
        union = default;
        bool any = false;
        FragmentTree tree = _fragments;
        for (Box box = BoxOf(s); !box.IsNull; box = box.NextPiece)
        {
            for (Fragment f = tree.FragmentOf(box); !f.IsNull; f = f.NextOfBox)
            {
                Rect r = _hits.AbsoluteBox(f.Index);
                union = any ? union.Union(r) : r;
                any = true;
            }
        }

        return any;
    }

    /// <summary><c>offsetWidth</c>: the width of the border box in px, rounded; 0 without a box.</summary>
    /// <param name="element">The element.</param>
    public int OffsetWidth(Node element) => LayoutBox(StyledOf(element), out Rect r) ? Round(r.Width) : 0;

    /// <summary><see cref="OffsetWidth(Node)"/> for a styled node.</summary>
    /// <param name="element">The styled element.</param>
    public int OffsetWidth(StyledNode element)
    {
        Sync();
        return LayoutBox(element, out Rect r) ? Round(r.Width) : 0;
    }

    /// <summary><see cref="OffsetHeight(Node)"/> for a styled node.</summary>
    /// <param name="element">The styled element.</param>
    public int OffsetHeight(StyledNode element)
    {
        Sync();
        return LayoutBox(element, out Rect r) ? Round(r.Height) : 0;
    }

    /// <summary><see cref="ClientWidth(Node)"/> for a styled node (the viewport width for the root).</summary>
    /// <param name="element">The styled element.</param>
    public int ClientWidth(StyledNode element)
    {
        Sync();
        if (!element.IsNull && IsRoot(element))
        {
            return Round(ViewportWidth);
        }

        return HasClientBox(element, out Fragment f) ? Round(Math.Max(0, f.BorderBox.Width - f.Border.Horizontal)) : 0;
    }

    /// <summary><see cref="ClientHeight(Node)"/> for a styled node (the viewport height for the root).</summary>
    /// <param name="element">The styled element.</param>
    public int ClientHeight(StyledNode element)
    {
        Sync();
        if (!element.IsNull && IsRoot(element))
        {
            return Round(ViewportHeight);
        }

        return HasClientBox(element, out Fragment f) ? Round(Math.Max(0, f.BorderBox.Height - f.Border.Vertical)) : 0;
    }

    /// <summary><c>offsetHeight</c>: the height of the border box in px, rounded; 0 without a box.</summary>
    /// <param name="element">The element.</param>
    public int OffsetHeight(Node element) => LayoutBox(StyledOf(element), out Rect r) ? Round(r.Height) : 0;

    /// <summary>
    /// <c>offsetParent</c>: the nearest ancestor that is positioned, is the <c>body</c>, or (for a statically positioned element) is a
    /// <c>td</c>, <c>th</c> or <c>table</c>; null for the root element, the body, fixed positioned elements and elements without a box.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The ancestor, or the null node.</returns>
    public Node OffsetParent(Node element)
    {
        StyledNode s = StyledOf(element);
        if (s.IsNull || FirstFragment(s).IsNull || IsRoot(s) || FocusRules.IsHtml(element, "body") || s.Values.Position == PositionKind.Fixed)
        {
            return default;
        }

        bool isStatic = s.Values.Position == PositionKind.Static;
        for (Node a = element.Parent; a.IsElement; a = a.Parent)
        {
            StyledNode sa = StyledOf(a);
            if ((!sa.IsNull && sa.Values.Position != PositionKind.Static) || FocusRules.IsHtml(a, "body")
                || (isStatic && (FocusRules.IsHtml(a, "td") || FocusRules.IsHtml(a, "th") || FocusRules.IsHtml(a, "table"))))
            {
                return a;
            }
        }

        return default;
    }

    /// <summary><c>offsetLeft</c>: the distance from the padding edge of <see cref="OffsetParent"/> (or from the page origin when it is null) to the left border edge of the element's first box, rounded; 0 for the body and elements without a box.</summary>
    /// <param name="element">The element.</param>
    public int OffsetLeft(Node element) => Offset(element, vertical: false);

    /// <summary><c>offsetTop</c>: like <see cref="OffsetLeft(Node)"/> for the top edge.</summary>
    /// <param name="element">The element.</param>
    public int OffsetTop(Node element) => Offset(element, vertical: true);

    private int Offset(Node element, bool vertical)
    {
        StyledNode s = StyledOf(element);
        Fragment f = FirstFragment(s);
        if (f.IsNull || FocusRules.IsHtml(element, "body"))
        {
            return 0;
        }

        Rect r = _hits.AbsoluteBox(f.Index);
        double v = vertical ? r.Y : r.X;
        Node parent = OffsetParent(element);
        // CSSOM View offsetLeft: a null or body offsetParent leaves the value relative to the initial containing block origin.
        if (!parent.IsNull && !FocusRules.IsHtml(parent, "body"))
        {
            Fragment pf = FirstFragment(StyledOf(parent));
            if (!pf.IsNull)
            {
                Rect pr = _hits.AbsoluteBox(pf.Index);
                v -= vertical ? pr.Y + pf.Border.Top : pr.X + pf.Border.Left;
            }
        }

        return Round(v);
    }

    // ------------------------------------------------------------------ client*

    private bool HasClientBox(StyledNode s, out Fragment f)
    {
        f = FirstFragment(s);
        if (f.IsNull)
        {
            return false;
        }

        // CSSOM View: no CSS layout box, or an inline box that is not replaced, gives zero.
        Box box = f.Box;
        return !(box.Kind == BoxKind.Inline);
    }

    /// <summary><c>clientTop</c>: the top border width, rounded; 0 for inline boxes and elements without a box.</summary>
    /// <param name="element">The element.</param>
    public int ClientTop(Node element) => HasClientBox(StyledOf(element), out Fragment f) ? Round(f.Border.Top) : 0;

    /// <summary><c>clientLeft</c>: the left border width, rounded.</summary>
    /// <param name="element">The element.</param>
    public int ClientLeft(Node element) => HasClientBox(StyledOf(element), out Fragment f) ? Round(f.Border.Left) : 0;

    /// <summary>
    /// <c>clientWidth</c>: the width of the padding box (no scroll bars are drawn), rounded; the viewport width for the root element
    /// and, in quirks mode, for the body; 0 for inline boxes and elements without a box.
    /// </summary>
    /// <param name="element">The element.</param>
    public int ClientWidth(Node element)
    {
        StyledNode s = StyledOf(element);
        if (IsViewportElement(element, s))
        {
            return Round(ViewportWidth);
        }

        return HasClientBox(s, out Fragment f) ? Round(Math.Max(0, f.BorderBox.Width - f.Border.Horizontal)) : 0;
    }

    /// <summary><c>clientHeight</c>: see <see cref="ClientWidth(Node)"/>.</summary>
    /// <param name="element">The element.</param>
    public int ClientHeight(Node element)
    {
        StyledNode s = StyledOf(element);
        if (IsViewportElement(element, s))
        {
            return Round(ViewportHeight);
        }

        return HasClientBox(s, out Fragment f) ? Round(Math.Max(0, f.BorderBox.Height - f.Border.Vertical)) : 0;
    }

    private bool IsViewportElement(Node element, StyledNode s) =>
        !s.IsNull && (IsRoot(s) || (_html is not null && _html.Document.Mode == QuirksMode.Quirks && FocusRules.IsHtml(element, "body")));

    // ------------------------------------------------------------------ scroll*

    /// <summary>
    /// <c>scrollWidth</c>: the width of the scrollable overflow area measured from the padding edge, at least <see cref="ClientWidth(Node)"/>, rounded.
    /// For the root element it is at least the viewport width.
    /// </summary>
    /// <param name="element">The element.</param>
    public int ScrollWidth(Node element)
    {
        StyledNode s = StyledOf(element);
        Fragment f = FirstFragment(s);
        if (f.IsNull)
        {
            return 0;
        }

        int client = ClientWidth(element);
        if (IsRoot(s))
        {
            return Math.Max(client, Round(_fragments.OverflowOf(0).Right));
        }

        // The overflow rect always includes the border box; only content past it extends the padding-edge-based scroll area.
        return f.ScrollableOverflow.Right > f.BorderBox.Width ? Math.Max(client, Round(f.ScrollableOverflow.Right - f.Border.Left)) : client;
    }

    /// <summary><c>scrollHeight</c>: see <see cref="ScrollWidth(Node)"/>.</summary>
    /// <param name="element">The element.</param>
    public int ScrollHeight(Node element)
    {
        StyledNode s = StyledOf(element);
        Fragment f = FirstFragment(s);
        if (f.IsNull)
        {
            return 0;
        }

        int client = ClientHeight(element);
        if (IsRoot(s))
        {
            return Math.Max(client, Round(_fragments.OverflowOf(0).Bottom));
        }

        return f.ScrollableOverflow.Bottom > f.BorderBox.Height ? Math.Max(client, Round(f.ScrollableOverflow.Bottom - f.Border.Top)) : client;
    }

    /// <summary><c>scrollLeft</c>: the horizontal scroll offset of a scroll container, or of the viewport for the root element; 0 otherwise.</summary>
    /// <param name="element">The element.</param>
    public double ScrollLeft(Node element) => ScrollOffset(element).X;

    /// <summary><c>scrollTop</c>: the vertical scroll offset; see <see cref="ScrollLeft(Node)"/>.</summary>
    /// <param name="element">The element.</param>
    public double ScrollTop(Node element) => ScrollOffset(element).Y;

    private (double X, double Y) ScrollOffset(Node element)
    {
        StyledNode s = StyledOf(element);
        if (s.IsNull)
        {
            return default;
        }

        if (IsRoot(s))
        {
            ScrollRange r = _hits.ViewportRange;
            return (r.ClampX(_scroll.ViewportX), r.ClampY(_scroll.ViewportY));
        }

        Box box = BoxOf(s);
        Fragment f = FirstFragment(s);
        if (f.IsNull)
        {
            return default;
        }

        _hits.EnsureBuilt();
        return _hits.IsScrollContainer(f.Index) ? _scroll.GetOffset(_fragments, box) : default;
    }
}
