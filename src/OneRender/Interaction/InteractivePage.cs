using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using HtmlStateChange = RecursiveAI.OneHtml.Css.ElementStateChange;

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
    private HtmlPage _page;
    private HitTester _hits;
    private LayoutQueries _queries;
    private DocumentStyles _view;

    // The cascade that moves in place once an interaction or form state changes (hover, focus, a ticked box). Until then, and after an
    // edit makes a new snapshot, it is null and `_view` is the cascade; while it exists, `_view` is rebuilt from it when a snapshot is needed.
    private LiveDocumentStyles? _live;
    private bool _viewStale;
    private int _generation;
    private IStateChangeHandler? _handler;

    // Tests set it to compare the in-place restyle with building the page again.
    internal bool DisableInPlaceRestyle { get; set; }

    private InteractivePage(HtmlPage page, DocumentStyles styles, HtmlPageOptions options, InteractionOptions io)
    {
        _page = page;
        _view = styles;
        _options = options;
        _io = io;
        _events = new EventDispatcher(page.Document);
        _hits = NewHitTester(page);
        _queries = new LayoutQueries(page, _hits);
        InitEvents();
    }

    /// <summary>Parses, styles and lays out <paramref name="html"/> and makes it interactive.</summary>
    /// <param name="html">The document source.</param>
    /// <param name="options">Viewport, fonts and resource hooks, as for <see cref="HtmlPage.Create(string, HtmlPageOptions, CancellationToken)"/>.</param>
    /// <param name="interaction">Double-click timing, scroll step sizes and tab wrapping; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The interactive page.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static InteractivePage Create(string html, HtmlPageOptions options, InteractionOptions? interaction = null, CancellationToken cancellationToken = default)
    {
        interaction ??= new InteractionOptions();
        HtmlPage page = HtmlPage.CreateWithStyles(html, options, out DocumentStyles styles, cancellationToken, incremental: true, animate: true, startTime: interaction.AnimationStartTime);
        var created = new InteractivePage(page, styles, options, interaction);
        created._motionNow = interaction.AnimationStartTime;
        // Events of the first frame (animationstart, transitionrun) stay queued until the first Advance, so listeners added after Create see them.
        return created;
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
    public StyleContext StyleContext => _live is null ? _view.Context : _live.GetContext();

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

    // The same element in the document the cascade is made for (node indices are the same in every snapshot of a page).
    private Node InSnapshot(Node n) => n.IsNull || _snapshot is null ? n : _snapshot.GetNode(n.Index);

    // The cascade as an immutable view of what the page shows now (for a rebuild, an edit or a handler).
    private DocumentStyles CurrentView()
    {
        if (_live is not null && _viewStale)
        {
            _view = _live.ToSnapshot();
            _viewStale = false;
        }

        return _view;
    }

    // Starts the cascade that moves in place; `_view` must be current.
    private LiveDocumentStyles EnsureLive() => _live ??= LiveDocumentStyles.Create(_view);

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

        // The cascade belongs to the latest edited snapshot (see SetTextContent), so it names that snapshot's nodes. Only the parts that
        // moved are named; the live cascade restyles from them without allocating.
        FocusSource source = _focusVisible || _focusReason == FocusReason.Keyboard ? FocusSource.Keyboard : FocusSource.PointingDevice;
        HtmlStateChange change = default;
        if (!_appliedHover.Equals(_hovered))
        {
            change = change.WithHover(InSnapshot(_hovered));
        }

        if (!_appliedActive.Equals(_active))
        {
            change = change.WithActive(InSnapshot(_active));
        }

        if (!_appliedFocus.Equals(_focused) || _appliedFocusVisible != _focusVisible)
        {
            change = change.WithFocus(InSnapshot(_focused), source);
        }

        _appliedHover = _hovered;
        _appliedActive = _active;
        _appliedFocus = _focused;
        _appliedFocusVisible = _focusVisible;
        LiveDocumentStyles live = EnsureLive();
        live.ApplyState(change);
        _viewStale = true;
        InputOutcome outcome = InputOutcome.StatesChanged;
        if (_changeCount == 0)
        {
            return outcome;
        }

        if (_handler is not null && _handler.ElementStatesChanged(this, LastStateChanges, live.GetContext()))
        {
            return outcome;
        }

        if (live.ChangedElementCount > 0 || live.HasInteractivePseudoElementRules)
        {
            outcome |= ShowStyles(new StyleSource(live), live.ChangedElementIndices, false, false, -1, null, ct);
        }

        return outcome;
    }

    // Brings the page up to date with a new view of the cascade, in place when the change allows it, else by building the page again.
    // `textElement` (a node index, or -1) names an element whose single text child takes `text` in the same pass.
    private InputOutcome ShowStyles(DocumentStyles styles, bool all, bool edit, int textElement, string? text, CancellationToken ct, bool rebuild = false) =>
        ShowStyles(new StyleSource(styles), all ? [] : [.. styles.ChangedElements], all, edit, textElement, text, ct, rebuild);

    private InputOutcome ShowStyles(StyleSource styles, ReadOnlySpan<int> changed, bool all, bool edit, int textElement, string? text, CancellationToken ct, bool rebuild = false)
    {
        _motionNow = Math.Max(_motionNow, _lastTime);
        if (!rebuild && !DisableInPlaceRestyle && _page.CanRestyleInPlace && _page.TryRestyle(styles, changed, all, ct, _motionNow, edit, textElement, text))
        {
            // The page object stays; its trees were updated in place, so the hit tester reads the new fragments.
            _hits.Rebuild();
        }
        else
        {
            HtmlPage before = _page;
            _page = _page.Rebuild(_live is not null ? CurrentView() : styles.ToSnapshot(), ct, _motionNow);
            if (edit)
            {
                // Scroll positions follow their elements into the new tree.
                _page.Scroll.Remap(before.Styled, _page.Styled);
            }

            _hits = NewHitTester(_page);
            _queries = new LayoutQueries(_page, _hits);
        }

        _generation++;
        if (_page.Motion is { } motion)
        {
            DispatchMotionEvents(motion);
        }

        return InputOutcome.Relaid;
    }

    // A text-only change: the page shows `text` in the element without a restyle (the cascade does not read text), or is built again.
    private InputOutcome ShowText(int sourceIndex, string text, CancellationToken ct)
    {
        _motionNow = Math.Max(_motionNow, _lastTime);
        if (!DisableInPlaceRestyle && _page.CanRestyleInPlace && _page.TryApplyText(sourceIndex, text, ct))
        {
            _hits.Rebuild();
        }
        else
        {
            HtmlPage before = _page;
            _page = _page.Rebuild(CurrentView(), ct, _motionNow);
            _page.Scroll.Remap(before.Styled, _page.Styled);
            _hits = NewHitTester(_page);
            _queries = new LayoutQueries(_page, _hits);
        }

        _generation++;
        return InputOutcome.Relaid;
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
