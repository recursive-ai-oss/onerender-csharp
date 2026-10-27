using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Interaction;

/// <summary>
/// A laid-out page that takes input: hit testing, DOM events, focus, hover and active states, wheel and keyboard scrolling and the
/// CSSOM View queries, over an <see cref="HtmlPage"/>. The host feeds it pointer, wheel and key inputs with its own timestamps;
/// listeners attach through <see cref="Events"/>, the same listener model a script runtime uses.
/// </summary>
/// <remarks>
/// <para>
/// After an input changed hover, active or focus the page restyles and lays out again as a whole, unless an
/// <see cref="IStateChangeHandler"/> takes over; <see cref="Generation"/> then counts up and <see cref="Page"/> is a new object, so the host
/// repaints. Inputs that change no state (pointer moves inside one element, events nobody listens to) allocate nothing.
/// </para>
/// <para>
/// The scroll positions live in <see cref="HtmlPage.Scroll"/> (kept across restyles); after an input reports
/// <see cref="InputOutcome.Scrolled"/> the host patches its display list with <c>Painter.PatchScroll</c>. Not thread-safe.
/// </para>
/// </remarks>
public sealed partial class InteractivePage
{
    private readonly HtmlPageOptions _options;
    private readonly InteractionOptions _io;
    private readonly EventDispatcher _events;
    private readonly DocumentStyles _baseStyles;
    private HtmlPage _page;
    private HitTester _hits;
    private LayoutQueries _queries;
    private StyleContext _context;
    private DocumentStyles _view;
    private int _viewDepth = 1;
    private int _generation;
    private IStateChangeHandler? _handler;

    // Tests set it to compare the in-place restyle with building the page again.
    internal bool DisableInPlaceRestyle { get; set; }

    private InteractivePage(HtmlPage page, DocumentStyles styles, HtmlPageOptions options, InteractionOptions io)
    {
        _page = page;
        _baseStyles = styles;
        _view = styles;
        _context = styles.Context;
        _options = options;
        _io = io;
        _events = new EventDispatcher(page.Document);
        _hits = NewHitTester(page);
        _queries = new LayoutQueries(page, _hits);
        InitEvents();
    }

    /// <summary>Parses, styles and lays out <paramref name="html"/> and makes it interactive.</summary>
    /// <param name="html">The document source.</param>
    /// <param name="options">Viewport, fonts and resource hooks, as for <see cref="HtmlPage.Create"/>.</param>
    /// <param name="interaction">Double-click timing, scroll step sizes and tab wrapping; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The interactive page.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static InteractivePage Create(string html, HtmlPageOptions options, InteractionOptions? interaction = null, CancellationToken cancellationToken = default)
    {
        HtmlPage page = HtmlPage.CreateWithStyles(html, options, out DocumentStyles styles, cancellationToken, incremental: true);
        return new InteractivePage(page, styles, options, interaction ?? new InteractionOptions());
    }

    /// <summary>The current page. It is replaced when a state change restyles the whole document, so read it again after an input reports <see cref="InputOutcome.Relaid"/>.</summary>
    public HtmlPage Page => _page;

    /// <summary>Counts the times the page was restyled and laid out again; starts at 0.</summary>
    public int Generation => _generation;

    /// <summary>The listener registry; its dispatcher is the one that delivers the engine's own input events.</summary>
    public EventDispatcher Events => _events;

    /// <summary>The hit tester for the current page.</summary>
    public HitTester Hits => _hits;

    /// <summary>CSSOM View queries for the current page.</summary>
    public LayoutQueries Layout => _queries;

    /// <summary>The scroll positions (shared with <see cref="Page"/>, and kept when it is replaced).</summary>
    public ScrollState Scroll => _page.Scroll;

    /// <summary>The document node as an event target.</summary>
    public EventTarget DocumentTarget => EventTarget.Of(_page.Document.Root);

    /// <summary>The handler that may take over restyling after a state change; null (the default) restyles the whole page.</summary>
    public IStateChangeHandler? StateChangeHandler
    {
        get => _handler;
        set => _handler = value;
    }

    /// <summary>The elements whose states changed in the last input that changed any, valid until the next input.</summary>
    public ReadOnlySpan<ElementStateChange> LastStateChanges => new(_changes, 0, _changeCount);

    /// <summary>The style context the document is currently styled under.</summary>
    public StyleContext StyleContext => _context;

    /// <summary>The element under the pointer, or the null node.</summary>
    public Node HoveredElement => _hovered;

    /// <summary>The element being pressed, or the null node.</summary>
    public Node ActiveElement => _active;

    /// <summary>The focused element, or the null node.</summary>
    public Node FocusedElement => _focused;

    /// <summary>The topmost element at a viewport point (<c>document.elementFromPoint</c>).</summary>
    /// <param name="x">Viewport x in CSS px.</param>
    /// <param name="y">Viewport y in CSS px.</param>
    /// <returns>The element, or the null node when the point is outside the viewport.</returns>
    public Node ElementFromPoint(double x, double y) => _hits.HitTest(x, y, out HitResult r) ? r.Node : default;

    /// <summary>All elements at a viewport point, topmost first (<c>document.elementsFromPoint</c>).</summary>
    /// <param name="x">Viewport x in CSS px.</param>
    /// <param name="y">Viewport y in CSS px.</param>
    /// <param name="results">Receives the elements.</param>
    /// <returns>How many were written.</returns>
    public int ElementsFromPoint(double x, double y, Span<Node> results)
    {
        HitResult[] buffer = _hitBuffer;
        int n = _hits.HitTestAll(x, y, buffer.AsSpan(0, Math.Min(buffer.Length, results.Length)));
        for (int i = 0; i < n; i++)
        {
            results[i] = buffer[i].Node;
            buffer[i] = default;
        }

        return n;
    }

    private readonly HitResult[] _hitBuffer = new HitResult[HitBufferSize];
    private const int HitBufferSize = 256;
    private const int MaxViewDepth = 8;

    private HitTester NewHitTester(HtmlPage page)
    {
        var h = new HitTester(page.Fragments, page.Scroll, _options.ViewportWidth, _options.ViewportHeight);
        h.Rebuild();
        return h;
    }

    // ------------------------------------------------------------------ states and the restyle seam

    private Node _hovered;
    private Node _active;
    private Node _focused;
    private FocusReason _focusReason;
    private bool _focusVisible;
    private bool _lastInputWasPointer;

    // The states as they were when the page was last brought up to date.
    private Node _appliedHover;
    private Node _appliedActive;
    private Node _appliedFocus;
    private bool _appliedFocusVisible;
    private bool _statesDirty;

    private ElementStateChange[] _changes = new ElementStateChange[16];
    private int _changeCount;
    private int[] _mark = new int[64];
    private int _markStamp;

    private void MarkDirty() => _statesDirty = true;

    private static ElementStates StatesOf(Node e, Node hover, Node active, Node focus, bool focusVisible)
    {
        ElementStates s = ElementStates.None;
        if (IsInclusiveAncestor(e, hover))
        {
            s |= ElementStates.Hover;
        }

        if (IsInclusiveAncestor(e, active))
        {
            s |= ElementStates.Active;
        }

        if (!focus.IsNull && e.Equals(focus))
        {
            s |= ElementStates.Focus;
            if (focusVisible)
            {
                s |= ElementStates.FocusVisible;
            }
        }

        if (IsInclusiveAncestor(e, focus))
        {
            s |= ElementStates.FocusWithin;
        }

        return s;
    }

    private static bool IsInclusiveAncestor(Node ancestor, Node node)
    {
        for (Node n = node; !n.IsNull; n = n.Parent)
        {
            if (n.Equals(ancestor))
            {
                return true;
            }
        }

        return false;
    }

    // Brings the document up to date with the hover, active and focus the inputs left behind. Returns what changed on the page.
    private InputOutcome FlushStates(CancellationToken ct = default)
    {
        if (!_statesDirty)
        {
            return InputOutcome.None;
        }

        _statesDirty = false;
        _changeCount = 0;
        if (_appliedHover.Equals(_hovered) && _appliedActive.Equals(_active) && _appliedFocus.Equals(_focused) && _appliedFocusVisible == _focusVisible)
        {
            return InputOutcome.None;
        }

        if (_mark.Length < _page.Document.NodeCount)
        {
            _mark = new int[Math.Max(_page.Document.NodeCount, _mark.Length * 2)];
        }

        _markStamp++;
        CollectChanges(_appliedHover);
        CollectChanges(_hovered);
        CollectChanges(_appliedActive);
        CollectChanges(_active);
        CollectChanges(_appliedFocus);
        CollectChanges(_focused);

        // The context differs only in these four fields; a record `with` allocates, which is fine once per state change.
        FocusSource source = _focusVisible || _focusReason == FocusReason.Keyboard ? FocusSource.Keyboard : FocusSource.PointingDevice;
        StyleContext next = _context with { Hover = _hovered, Active = _active, Focus = _focused, FocusSource = source };
        _appliedHover = _hovered;
        _appliedActive = _active;
        _appliedFocus = _focused;
        _appliedFocusVisible = _focusVisible;
        InputOutcome outcome = InputOutcome.StatesChanged;
        if (_changeCount == 0)
        {
            _context = next;
            return outcome;
        }

        if (_handler is not null && _handler.ElementStatesChanged(this, LastStateChanges, next))
        {
            _context = next;
            return outcome;
        }

        // Whole-document restyle. Each view is derived from the previous one so that ChangedElements says exactly what differs from
        // the page as it is now; after a few such steps the chain is rebased on the interaction-free styles so lookups stay short.
        bool rebase = _viewDepth >= MaxViewDepth;
        DocumentStyles styles = (rebase ? _baseStyles : _view).WithContext(next, ct);
        bool differs = rebase || styles.ChangedElements.Count > 0 || styles.HasInteractivePseudoElementRules;
        _context = next;
        _view = styles;
        _viewDepth = rebase ? 1 : _viewDepth + 1;
        if (differs)
        {
            if (!DisableInPlaceRestyle && _page.CanRestyleInPlace && _page.TryRestyle(styles, rebase, ct))
            {
                // The page object stays; its trees were updated in place, so the hit tester reads the new fragments.
                _hits.Rebuild();
            }
            else
            {
                _page = _page.Rebuild(styles, ct);
                _hits = NewHitTester(_page);
                _queries = new LayoutQueries(_page, _hits);
            }

            _generation++;
            outcome |= InputOutcome.Relaid;
        }

        return outcome;
    }

    private void CollectChanges(Node start)
    {
        for (Node e = start; e.IsElement; e = e.Parent)
        {
            int idx = e.Index;
            if (_mark[idx] == _markStamp)
            {
                // Already visited, so every ancestor above is too.
                return;
            }

            _mark[idx] = _markStamp;
            ElementStates before = StatesOf(e, _appliedHover, _appliedActive, _appliedFocus, _appliedFocusVisible);
            ElementStates after = StatesOf(e, _hovered, _active, _focused, _focusVisible);
            if (before != after)
            {
                if (_changeCount == _changes.Length)
                {
                    Array.Resize(ref _changes, _changes.Length * 2);
                }

                _changes[_changeCount++] = new ElementStateChange(e, before, after);
            }
        }
    }

    /// <summary>
    /// Replaces the page after the document's styles, layout or scroll content changed outside the engine's own state handling (for
    /// instance a handler that relaid out the page itself). The hit tester and layout queries are rebuilt for it.
    /// </summary>
    /// <param name="page">The new page; it must be built from the same document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is null.</exception>
    /// <exception cref="ArgumentException">The page is for another document.</exception>
    public void Adopt(HtmlPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!ReferenceEquals(page.Document, _page.Document))
        {
            throw new ArgumentException("The page is built from another document.", nameof(page));
        }

        _page = page;
        _hits = NewHitTester(page);
        _queries = new LayoutQueries(page, _hits);
        _generation++;
    }
}
