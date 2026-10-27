using System.Numerics;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Painting;

/// <summary>What the painter needs to know besides the fragment tree.</summary>
public readonly struct PaintOptions
{
    /// <summary>
    /// The viewport size in CSS px. The canvas background (Backgrounds 3 §2.11.2) covers at least this area, so a page
    /// shorter than the viewport still gets its background everywhere. Zero means "only as big as the content".
    /// </summary>
    public PaintOptions(double viewportWidth, double viewportHeight)
    {
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    /// <summary>Viewport width in CSS px; 0 or less means unknown.</summary>
    public double ViewportWidth { get; }

    /// <summary>Viewport height in CSS px; 0 or less means unknown.</summary>
    public double ViewportHeight { get; }

    /// <summary>
    /// The host's scroll positions. When set, scroll containers that can scroll and the viewport get a translation item that
    /// <see cref="Painter.PatchScroll(RetainedDisplayList)"/> updates in place, sticky positioned boxes get theirs, and fixed boxes
    /// stay put in the viewport. Null paints everything at scroll position zero with no such items.
    /// </summary>
    public ScrollState? Scroll { get; init; }

    /// <summary>Resolves <c>url()</c> images and <c>img</c> sources to display-list handles. Null means every image is missing (a diagnostic each).</summary>
    public IImageSource? Images { get; init; }

    /// <summary>Shapes the text of list markers (<c>1.</c>, <c>a.</c>). Null draws only the marker shapes (disc, circle, square).</summary>
    public IFontProvider? Fonts { get; init; }

    /// <summary>
    /// Device pixels per CSS px, used only to snap box edges to whole device pixels (CSS px when 1). Default 1. The list itself stays in CSS px.
    /// </summary>
    public double DevicePixelRatio { get; init; } = 1;
}

/// <summary>
/// Turns a fragment tree into display-list items in CSS 2.2 Appendix E order. Reuse one painter for every frame: all of its
/// working storage is pooled, so painting an unchanged tree into a retained list allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// Item ids come from box identity (the box's index in the box tree, the kind of item, and the fragment's ordinal among the
/// fragments of its box), so painting the same tree twice gives the same ids and the list diff finds no change.
/// </para>
/// <para>
/// Known limits, each a documented simplification rather than silent drift: a box with <c>overflow</c> other than
/// <c>visible</c> that is not positioned paints as one unit (its floats and inline content are not interleaved with those
/// of the outside), and the clip of a positioned descendant is found by walking at most 4,096 containing blocks. Nesting
/// of clips and opacity groups beyond 240 levels is not applied, since the list allows 256 open scopes.
/// </para>
/// </remarks>
public sealed partial class Painter
{
    // Phases of one stacking context (or one atomic unit), in paint order. CSS2 Appendix E §E.2 steps 3 to 10.
    private const int PhaseNegative = 0; // step 3: child stacking contexts with negative z-index
    private const int PhaseBlocks = 1; // step 4: in-flow, non-inline-level, non-positioned descendants
    private const int PhaseFloats = 2; // step 5: non-positioned floats
    private const int PhaseInlines = 3; // step 7: in-flow inline-level descendants, text
    private const int PhaseZero = 4; // step 8: positioned descendants with z-index auto or 0
    private const int PhasePositive = 5; // step 9: child stacking contexts with positive z-index
    private const int PhaseCount = 6;

    /// <summary>
    /// Told where painting is in the paint order, for consumers that relate display list items to fragments; null (the default)
    /// means nobody is told and painting does no extra work.
    /// </summary>
    public IPaintObserver? Observer { get; set; }

    private const int MaxScopes = 240;
    private const int MaxClipSteps = 4096;
    private const int MaxDeferredClips = 64;

    private const byte FlagAtomic = 1;
    private const byte FlagStackingContext = 2;
    private const byte FlagClips = 4;
    private const byte FlagIsText = 8;

    private FragmentTree _tree = null!;
    private DisplayList _list = null!;
    private BoxTree _boxes = null!;

    private byte[] _flags = new byte[64];
    private int[] _ownerFor = new int[64];
    private int[] _contextFor = new int[64];
    private int[] _head = new int[64 * PhaseCount];
    private int[] _tail = new int[64 * PhaseCount];
    private int[] _nextEntry = new int[64];
    private int[] _outlineHead = new int[64];
    private int[] _outlineTail = new int[64];
    private int[] _nextOutline = new int[64];
    private int[] _listIndex = new int[64];
    private int[] _listCount = new int[64];
    private int[] _ordinal = new int[64];
    private int[] _boxOrdinal = new int[64];
    private double[] _absX = new double[64];
    private double[] _absY = new double[64];
    private double[] _baseY = new double[64];

    private long[] _sortKeys = new long[64];
    private int[] _sortFrags = new int[64];
    private int _sortCount;

    private Frame[] _stack = new Frame[32];
    private readonly int[] _clipChain = new int[MaxDeferredClips];

    private float[] _gx = new float[256];
    private float[] _gy = new float[256];
    private int[] _gc = new int[256];

    private int _skipBackground = -1;
    private int _scopes;

    // Scrolling and sticky positioning: the items that move with the host's scroll positions, patched in place.
    private const byte FlagScroller = 16;
    private const byte FlagScrollable = 32;
    private const byte FlagSticky = 64;
    private ScrollState? _scroll;
    private double _vw;
    private double _vh;
    private int _vpSource = -1;
    private ScrollRange _vpRange;
    private int _vpEntry;
    private int[] _scrollerFor = new int[64];
    private int[] _stickyAbove = new int[64];
    private int[] _scrollEntryOf = new int[64];
    private int[] _stickyEntryOf = new int[64];
    private PatchEntry[] _patch = new PatchEntry[16];
    private int _patchCount;

    private const byte PatchScrollKind = 0;
    private const byte PatchStickyKind = 1;
    private const byte PatchFixedKind = 2;
    private const byte PatchViewportKind = 3;

    private struct PatchEntry
    {
        public byte Kind;
        public int Box;
        public ulong Id;
        public float LastX;
        public float LastY;
        public ScrollRange Range;
        public int ParentEntry;
        public int ScrollerEntry;
        public Rect BorderBox;
        public Rect Cb;
        public Rect Port;
        public double MarginTop;
        public double MarginRight;
        public double MarginBottom;
        public double MarginLeft;
        public double Top;
        public double Right;
        public double Bottom;
        public double Left;
        public double TotalX;
        public double TotalY;
    }

    private struct Frame
    {
        public int Fragment;
        public int Phase;
        public int Cursor;
        public int PreClips;
        public bool Group;
        public bool Clip;
        public bool Outer;
        public bool Scroll;
        public byte FxOuter;
        public byte FxInner;
        public ulong PreMask;
    }

    /// <summary>Paints <paramref name="fragments"/> into <paramref name="list"/>, which is cleared first.</summary>
    /// <param name="fragments">The laid-out tree.</param>
    /// <param name="list">The list to fill. A caller that retains lists passes the back list of a <see cref="RetainedDisplayList"/>.</param>
    /// <param name="options">The viewport.</param>
    /// <param name="cancellationToken">Checked every few hundred fragments.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; <paramref name="list"/> is partly filled.</exception>
    public void Paint(FragmentTree fragments, DisplayList list, in PaintOptions options = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        ArgumentNullException.ThrowIfNull(list);
        list.Clear();
        if (fragments.Count == 0)
        {
            return;
        }

        _tree = fragments;
        _list = list;
        _boxes = fragments.Boxes;
        try
        {
            Run(options, cancellationToken);
        }
        finally
        {
            _tree = null!;
            _list = null!;
            _boxes = null!;
        }
    }

    /// <summary>Paints into the back list of <paramref name="retained"/> and commits it, so <see cref="RetainedDisplayList.Damage"/> holds what changed.</summary>
    /// <param name="fragments">The laid-out tree.</param>
    /// <param name="retained">The retained list.</param>
    /// <param name="options">The viewport.</param>
    /// <param name="cancellationToken">Checked every few hundred fragments.</param>
    public void Paint(FragmentTree fragments, RetainedDisplayList retained, in PaintOptions options = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(retained);
        Paint(fragments, retained.Begin(), options, cancellationToken);
        retained.Commit();
    }

    private void Run(in PaintOptions options, CancellationToken ct)
    {
        int n = _tree.Count;
        EnsureCapacity(n);
        Array.Fill(_head, -1, 0, n * PhaseCount);
        Array.Fill(_tail, -1, 0, n * PhaseCount);
        Array.Fill(_outlineHead, -1, 0, n);
        Array.Fill(_outlineTail, -1, 0, n);
        int boxCount = _boxes.Count;
        if (_boxOrdinal.Length < boxCount)
        {
            _boxOrdinal = new int[Math.Max(boxCount, _boxOrdinal.Length * 2)];
        }

        Array.Clear(_boxOrdinal, 0, boxCount);
        _sortCount = 0;
        _skipBackground = -1;
        _scopes = 0;
        _scroll = options.Scroll;
        _images = options.Images;
        _fonts = options.Fonts;
        _dpr = options.DevicePixelRatio > 0 && double.IsFinite(options.DevicePixelRatio) ? options.DevicePixelRatio : 1;
        Diagnostics.Clear();
        _vw = options.ViewportWidth;
        _vh = options.ViewportHeight;
        _patchCount = 0;
        _vpEntry = -1;
        ViewportOverflow.Resolve(_boxes, out _, out _, out _vpSource);
        _vpRange = _scroll is null ? default : ScrollState.GetViewportRange(_tree, _vw, _vh);

        Prepass(n, ct);
        PaintCanvas(options);
        LinkSorted();
        Execute(ct);
    }

    private void EnsureCapacity(int n)
    {
        if (_flags.Length >= n)
        {
            return;
        }

        int size = Math.Max(n, _flags.Length * 2);
        _flags = new byte[size];
        _ownerFor = new int[size];
        _contextFor = new int[size];
        _head = new int[size * PhaseCount];
        _tail = new int[size * PhaseCount];
        _nextEntry = new int[size];
        _outlineHead = new int[size];
        _outlineTail = new int[size];
        _nextOutline = new int[size];
        _listIndex = new int[size];
        _listCount = new int[size];
        _ordinal = new int[size];
        _absX = new double[size];
        _absY = new double[size];
        _baseY = new double[size];
        _scrollerFor = new int[size];
        _stickyAbove = new int[size];
        _scrollEntryOf = new int[size];
        _stickyEntryOf = new int[size];
    }

    // ------------------------------------------------------------------ classification

    private void Prepass(int n, CancellationToken ct)
    {
        int i = 0;
        int visited = 0;
        while (i >= 0)
        {
            if ((++visited & 511) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            Visit(i);
            int c = _tree.FirstChildOf(i);
            if (c >= 0)
            {
                i = c;
                continue;
            }

            while (i >= 0)
            {
                int next = _tree.NextSiblingOf(i);
                if (next >= 0)
                {
                    i = next;
                    break;
                }

                i = _tree.ParentOf(i);
            }
        }
    }

    private void Visit(int i)
    {
        VisitCore(i);
        if ((FragmentKind)_tree.KindOf(i) != FragmentKind.Box)
        {
            return;
        }

        Box b = _boxes.Make(_tree.BoxOf(i));
        ref readonly ComputedValues v = ref b.Values;
        if (v.OutlineWidth > 0 && (v.OutlineStyle != BorderStyleKind.None || v.OutlineAuto) && v.Visibility == VisibilityKind.Visible)
        {
            // CSS2 Appendix E step 10: outlines are painted over everything else of their paint unit, in tree order.
            int owner = _ownerFor[i];
            int t = _outlineTail[owner];
            if (t < 0)
            {
                _outlineHead[owner] = i;
            }
            else
            {
                _nextOutline[t] = i;
            }

            _outlineTail[owner] = i;
        }

        if (b.Level == BoxLevel.Block && v.Display.IsListItem && _ordinal[i] == 0)
        {
            int p = _tree.ParentOf(i);
            if (p >= 0)
            {
                _listIndex[i] = ++_listCount[p];
            }
        }
    }

    private void VisitCore(int i)
    {
        int parent = _tree.ParentOf(i);
        Rect bb = _tree.BorderBoxOf(i);
        if (parent < 0)
        {
            _absX[i] = bb.X;
            _absY[i] = bb.Y;
        }
        else
        {
            _absX[i] = _absX[parent] + bb.X;
            _absY[i] = _absY[parent] + bb.Y;
        }

        _flags[i] = 0;
        _nextEntry[i] = -1;
        _nextOutline[i] = -1;
        _listIndex[i] = 0;
        _listCount[i] = 0;
        FragmentKind kind = (FragmentKind)_tree.KindOf(i);
        int owner = parent < 0 ? i : _ownerFor[parent];
        int context = parent < 0 ? i : _contextFor[parent];
        _ownerFor[i] = owner;
        _contextFor[i] = context;
        _baseY[i] = parent < 0 ? _absY[i] : _baseY[parent];
        _scrollEntryOf[i] = -1;
        _stickyEntryOf[i] = -1;
        _scrollerFor[i] = parent < 0 ? -1 : ((_flags[parent] & FlagScroller) != 0 ? parent : _scrollerFor[parent]);
        _stickyAbove[i] = parent < 0 || (_flags[parent] & FlagScroller) != 0
            ? -1
            : ((_flags[parent] & FlagSticky) != 0 ? parent : _stickyAbove[parent]);

        if (kind == FragmentKind.Line)
        {
            _baseY[i] = _absY[i] + _tree.BaselineOf(i);
            return;
        }

        if (kind == FragmentKind.Column)
        {
            // A column holds no box: its content is painted by the multi-column container's stacking context in tree order.
            _ordinal[i] = 0;
            return;
        }

        int boxIndex = _tree.BoxOf(i);
        _ordinal[i] = boxIndex >= 0 ? _boxOrdinal[boxIndex]++ : 0;

        if (kind == FragmentKind.Text)
        {
            if (_tree.GlyphRunOf(i).IsValid)
            {
                _flags[i] = FlagIsText;
                AddEntry(owner, PhaseInlines, i);
            }

            return;
        }

        Box box = _boxes.Make(boxIndex);
        ref readonly ComputedValues v = ref box.Values;
        // CSS Overflow 3 §3.3: the box whose overflow went to the viewport neither clips nor scrolls.
        bool propagated = boxIndex == _vpSource;
        bool clips = !propagated && (v.OverflowX != OverflowKind.Visible || v.OverflowY != OverflowKind.Visible);
        if (clips)
        {
            _flags[i] |= FlagClips;
        }

        if (v.Position == PositionKind.Sticky)
        {
            _flags[i] |= FlagSticky;
        }

        if (!propagated && v.IsScrollContainer)
        {
            _flags[i] |= FlagScroller;
            if (_scroll is not null && ScrollRange.Of(_tree, i, in v).CanScroll)
            {
                _flags[i] |= FlagScrollable;
            }
        }

        if (parent < 0)
        {
            // The root element: a stacking context (CSS2 §9.9.1) painted first. Its background belongs to the canvas.
            _flags[i] |= FlagAtomic | FlagStackingContext;
            _ownerFor[i] = i;
            _contextFor[i] = i;
            return;
        }

        bool positioned = v.Position != PositionKind.Static;
        bool stacking = (positioned && (v.Position is PositionKind.Fixed or PositionKind.Sticky || !v.ZIndexAuto)) || v.CreatesEffectContext;
        if (stacking)
        {
            // Compositing 1 §3: opacity below 1 paints like a positioned element with z-index 0 in a stacking context of its own.
            int z = positioned && !v.ZIndexAuto ? v.ZIndex : 0;
            _flags[i] |= FlagAtomic | FlagStackingContext;
            _ownerFor[i] = i;
            _contextFor[i] = i;
            Defer(context, i, z);
            return;
        }

        if (positioned)
        {
            // CSS2 §E.2 step 8: z-index auto is painted as if it made a stacking context, but its positioned descendants
            // belong to the parent's; they are found by this same walk and linked to the enclosing context.
            _flags[i] |= FlagAtomic;
            _ownerFor[i] = i;
            Defer(context, i, 0);
            return;
        }

        if (v.Floating != FloatKind.None)
        {
            _flags[i] |= FlagAtomic;
            _ownerFor[i] = i;
            AddEntry(owner, PhaseFloats, i);
            return;
        }

        if (box.Level == BoxLevel.Inline)
        {
            if (box.IsAtomicInline)
            {
                _flags[i] |= FlagAtomic;
                _ownerFor[i] = i;
            }

            AddEntry(owner, PhaseInlines, i);
            return;
        }

        if (clips)
        {
            _flags[i] |= FlagAtomic;
            _ownerFor[i] = i;
        }

        AddEntry(owner, PhaseBlocks, i);
    }

    private void AddEntry(int owner, int phase, int fragment)
    {
        int slot = (owner * PhaseCount) + phase;
        int t = _tail[slot];
        if (t < 0)
        {
            _head[slot] = fragment;
        }
        else
        {
            _nextEntry[t] = fragment;
        }

        _tail[slot] = fragment;
    }

    // Entries painted by a stacking context at its z-order steps are sorted by (z-index, tree order) once, globally, then linked
    // into their context's lists in that order. The key is unique, so the sort is deterministic.
    private void Defer(int context, int fragment, int z)
    {
        if (_sortCount == _sortKeys.Length)
        {
            Array.Resize(ref _sortKeys, _sortKeys.Length * 2);
            Array.Resize(ref _sortFrags, _sortFrags.Length * 2);
        }

        _sortKeys[_sortCount] = (((long)z + 0x80000000L) << 32) | (uint)_sortCount;
        _sortFrags[_sortCount] = fragment;
        _sortCount++;
    }

    private void LinkSorted()
    {
        Array.Sort(_sortKeys, _sortFrags, 0, _sortCount);
        for (int k = 0; k < _sortCount; k++)
        {
            int f = _sortFrags[k];
            int z = (int)((_sortKeys[k] >> 32) - 0x80000000L);
            int context = ContextOf(f);
            int phase = z < 0 ? PhaseNegative : (z == 0 ? PhaseZero : PhasePositive);
            AddEntry(context, phase, f);
        }
    }

    // The stacking context a deferred fragment paints in: its parent's context.
    private int ContextOf(int f)
    {
        int p = _tree.ParentOf(f);
        return p < 0 ? f : _contextFor[p];
    }

    // ------------------------------------------------------------------ canvas

    private void PaintCanvas(in PaintOptions options)
    {
        int root = 0;
        Box rootBox = _boxes.Make(_tree.BoxOf(root));
        if (rootBox.IsNull)
        {
            return;
        }

        ref readonly ComputedValues rv = ref rootBox.Values;
        ColorValue bg = rv.BackgroundColor;
        ComputedValues bgValues = rv;
        // Backgrounds 3 §2.11.2: a transparent HTML root takes the background of its body child.
        if (bg.A <= 0f && !rv.Background.HasImages && string.Equals(rootBox.Node.TagName, "html", StringComparison.Ordinal))
        {
            for (int c = _tree.FirstChildOf(root); c >= 0; c = _tree.NextSiblingOf(c))
            {
                if ((FragmentKind)_tree.KindOf(c) != FragmentKind.Box)
                {
                    continue;
                }

                Box b = _boxes.Make(_tree.BoxOf(c));
                if (!b.IsAnonymous && b.Kind == BoxKind.Container && string.Equals(b.Node.TagName, "body", StringComparison.Ordinal))
                {
                    bgValues = b.Values;
                    bg = b.Values.BackgroundColor.Resolve(b.Values.Color);
                    _skipBackground = c;
                    break;
                }
            }
        }

        bool hasImages = bgValues.Background.HasImages;
        if (bg.A <= 0f && !hasImages)
        {
            return;
        }

        Rect rb = _tree.BorderBoxOf(root);
        Rect ov = _tree.OverflowOf(root);
        double right = Math.Max(Math.Max(rb.Right, ov.Right), options.ViewportWidth);
        double bottom = Math.Max(Math.Max(rb.Bottom, ov.Bottom), options.ViewportHeight);
        double left = Math.Min(Math.Min(rb.X, ov.X), 0);
        double top = Math.Min(Math.Min(rb.Y, ov.Y), 0);
        var canvas = new RectF(C(left), C(top), C(right - left), C(bottom - top));
        if (bg.A > 0f)
        {
            _list.FillRect(ItemId.Of(0, 5, 0), canvas, ToColor(bg, rv.Color));
        }

        if (hasImages)
        {
            // The layers are positioned against the root element's boxes and cover the whole canvas.
            BoxGeometry g = GeometryOf(root, in rv);
            PaintBackground(root, in bgValues, in g, new PaintColor(bgValues.Color.R, bgValues.Color.G, bgValues.Color.B, bgValues.Color.A), true, in canvas);
        }
    }

    // ------------------------------------------------------------------ execution

    private void Execute(CancellationToken ct)
    {
        int sp = 0;
        PushFrame(ref sp, 0, 0);
        int steps = 0;
        while (sp > 0)
        {
            if ((++steps & 511) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            int top = sp - 1;
            if (_stack[top].Phase < 0)
            {
                Enter(top);
                _stack[top].Phase = 0;
                _stack[top].Cursor = _head[(_stack[top].Fragment * PhaseCount) + 0];
                continue;
            }

            if (_stack[top].Phase >= PhaseCount)
            {
                Exit(top);
                sp--;
                continue;
            }

            int entry = _stack[top].Cursor;
            if (entry < 0)
            {
                int phase = ++_stack[top].Phase;
                if (phase < PhaseCount)
                {
                    _stack[top].Cursor = _head[(_stack[top].Fragment * PhaseCount) + phase];
                }

                continue;
            }

            int phaseNow = _stack[top].Phase;
            _stack[top].Cursor = _nextEntry[entry];
            if ((_flags[entry] & FlagAtomic) != 0)
            {
                // Stacking-context children come from further up the tree than their parent, so the clips of the
                // containing blocks between are applied around them.
                ulong mask = 0;
                int clips = phaseNow is PhaseNegative or PhaseZero or PhasePositive ? PushDeferredClips(entry, _stack[top].Fragment, out mask) : 0;
                PushFrame(ref sp, entry, clips);
                _stack[sp - 1].PreMask = mask;
            }
            else if ((_flags[entry] & FlagIsText) != 0)
            {
                PaintText(entry);
            }
            else
            {
                PaintBoxDecoration(entry);
            }
        }
    }

    private void PushFrame(ref int sp, int fragment, int preClips)
    {
        if (sp == _stack.Length)
        {
            Array.Resize(ref _stack, _stack.Length * 2);
        }

        _stack[sp++] = new Frame { Fragment = fragment, Phase = -1, Cursor = -1, PreClips = preClips };
    }

    private void Enter(int top)
    {
        int f = _stack[top].Fragment;
        Box box = _boxes.Make(_tree.BoxOf(f));
        ref readonly ComputedValues v = ref box.Values;
        if (Observer is { } observer)
        {
            bool stackingContext = (_flags[f] & FlagStackingContext) != 0;
            observer.EnterUnit(_tree.Make(f), stackingContext || v.Position != PositionKind.Static || (_flags[f] & FlagClips) != 0, stackingContext, _list.Count);
        }

        if (_scroll is not null && PushOuterTransform(f, in v))
        {
            _stack[top].Outer = true;
        }

        if (v.CreatesEffectContext && (_flags[f] & FlagStackingContext) != 0)
        {
            _stack[top].FxOuter = EnterTransform(f, in v);
            if (!v.BackdropFilter.IsNone && _scopes < MaxScopes - 1)
            {
                PaintBackdropFilter(f, in v);
            }
        }

        if ((v.Opacity < 1.0 || v.MixBlendMode != BlendMode.Normal || v.Isolate) && (_flags[f] & FlagStackingContext) != 0 && _scopes < MaxScopes)
        {
            _list.PushGroup(Id(f, 4), (float)Math.Clamp(v.Opacity, 0.0, 1.0), v.MixBlendMode);
            _scopes++;
            _stack[top].Group = true;
        }

        if (v.CreatesEffectContext && (_flags[f] & FlagStackingContext) != 0)
        {
            _stack[top].FxInner = EnterFx(f, in v, 0);
        }

        // The root's background went to the canvas (Backgrounds 3 §2.11.2); its border is its own.
        PaintBoxDecoration(f);

        if ((_flags[f] & FlagClips) != 0 && _scopes < MaxScopes)
        {
            PushOverflowClip(f, in v, Id(f, 5));
            _scopes++;
            _stack[top].Clip = true;
            if ((_flags[f] & FlagScrollable) != 0 && _scopes < MaxScopes)
            {
                // The content scrolls inside the clip, which stays put.
                PushScrollTransform(f, Id(f, 6), in v);
                _stack[top].Scroll = true;
            }
        }
    }

    private void Exit(int top)
    {
        int frag = _stack[top].Fragment;
        PaintOutlines(frag, false);
        if (_stack[top].Scroll)
        {
            _list.PopTransform();
            _scopes--;
        }

        if (_stack[top].Clip)
        {
            _list.PopClip();
            _scopes--;
        }

        PaintOutlines(frag, true);
        ExitFx(_stack[top].FxInner);
        if (_stack[top].Group)
        {
            _list.PopGroup();
            _scopes--;
        }

        ExitTransform(_stack[top].FxOuter);

        if (_stack[top].Outer)
        {
            _list.PopTransform();
            _scopes--;
        }

        ulong mask = _stack[top].PreMask;
        for (int k = _stack[top].PreClips; k > 0; k--)
        {
            if ((mask & (1UL << (k - 1))) != 0)
            {
                _list.PopTransform();
                _scopes--;
            }

            _list.PopClip();
            _scopes--;
        }

        Observer?.ExitUnit(_tree.Make(frag), _list.Count);
    }

    // ------------------------------------------------------------------ scroll and sticky patch items

    private int AddEntry(byte kind, int box, ulong id)
    {
        if (_patchCount == _patch.Length)
        {
            Array.Resize(ref _patch, _patch.Length * 2);
        }

        _patch[_patchCount] = new PatchEntry { Kind = kind, Box = box, Id = id, ParentEntry = -1, ScrollerEntry = -1 };
        return _patchCount++;
    }

    private void PushScrollTransform(int f, ItemId id, in ComputedValues v)
    {
        int k = AddEntry(PatchScrollKind, _tree.BoxOf(f), id.Value);
        _patch[k].Range = ScrollRange.Of(_tree, f, in v);
        if (_scrollEntryOf[f] < 0)
        {
            _scrollEntryOf[f] = k;
        }

        ComputeEntry(k, out float tx, out float ty);
        _patch[k].LastX = tx;
        _patch[k].LastY = ty;
        _list.PushTransform(id, Matrix3x2.CreateTranslation(tx, ty));
        _scopes++;
    }

    // The viewport's scroll (around everything), the counter-scroll of fixed boxes, and the shift of sticky boxes.
    private bool PushOuterTransform(int f, in ComputedValues v)
    {
        if (_scopes >= MaxScopes)
        {
            return false;
        }

        int k;
        ItemId id;
        if (f == 0)
        {
            if (!_vpRange.CanScroll)
            {
                return false;
            }

            id = ItemId.Of(0, 6, 0);
            k = AddEntry(PatchViewportKind, -1, id.Value);
            _vpEntry = k;
            _patch[k].Range = _vpRange;
        }
        else if (v.Position == PositionKind.Fixed && !HasTransformedAncestor(f))
        {
            if (!_vpRange.CanScroll)
            {
                return false;
            }

            id = Id(f, 8);
            k = AddEntry(PatchFixedKind, _tree.BoxOf(f), id.Value);
            _patch[k].Range = _vpRange;
        }
        else if (v.Position == PositionKind.Sticky)
        {
            k = AddSticky(f, in v, out id);
            if (k < 0)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        ComputeEntry(k, out float tx, out float ty);
        _patch[k].LastX = tx;
        _patch[k].LastY = ty;
        _list.PushTransform(id, Matrix3x2.CreateTranslation(tx, ty));
        _scopes++;
        return true;
    }

    // CSS Position 3 §3.4: everything the shift needs is recorded in document space now, so a scroll change is arithmetic only.
    private int AddSticky(int f, in ComputedValues v, out ItemId id)
    {
        id = default;
        int p = _tree.ParentOf(f);
        if (p < 0 || (FragmentKind)_tree.KindOf(p) != FragmentKind.Box)
        {
            return -1;
        }

        int s = _scrollerFor[f];
        Rect port;
        if (s < 0)
        {
            if (!(_vw > 0) || !(_vh > 0))
            {
                return -1;
            }

            port = new Rect(0, 0, _vw, _vh);
        }
        else
        {
            Rect sb = _tree.BorderBoxOf(s);
            Insets si = _tree.BorderOf(s);
            port = new Rect(_absX[s] + si.Left, _absY[s] + si.Top, Math.Max(0, sb.Width - si.Horizontal), Math.Max(0, sb.Height - si.Vertical));
        }

        double top = InsetOf(v.Top, port.Height), bottom = InsetOf(v.Bottom, port.Height);
        double left = InsetOf(v.Left, port.Width), right = InsetOf(v.Right, port.Width);
        if (double.IsNaN(top) && double.IsNaN(bottom) && double.IsNaN(left) && double.IsNaN(right))
        {
            return -1;
        }

        Rect pb = _tree.BorderBoxOf(p);
        Insets pBorder = _tree.BorderOf(p);
        Insets pPad = _tree.PaddingOf(p);
        double cbW = Math.Max(0, pb.Width - pBorder.Horizontal - pPad.Horizontal);
        var cb = new Rect(
            _absX[p] + pBorder.Left + pPad.Left,
            _absY[p] + pBorder.Top + pPad.Top,
            cbW,
            Math.Max(0, pb.Height - pBorder.Vertical - pPad.Vertical));
        Rect bb = _tree.BorderBoxOf(f);

        id = Id(f, 7);
        int k = AddEntry(PatchStickyKind, _tree.BoxOf(f), id.Value);
        ref PatchEntry e = ref _patch[k];
        e.BorderBox = new Rect(_absX[f], _absY[f], bb.Width, bb.Height);
        e.Cb = cb;
        e.Port = port;
        e.MarginTop = MarginOf(v.MarginTop, cbW);
        e.MarginRight = MarginOf(v.MarginRight, cbW);
        e.MarginBottom = MarginOf(v.MarginBottom, cbW);
        e.MarginLeft = MarginOf(v.MarginLeft, cbW);
        e.Top = top;
        e.Right = right;
        e.Bottom = bottom;
        e.Left = left;
        e.ScrollerEntry = s < 0 ? _vpEntry : _scrollEntryOf[s];
        int above = _stickyAbove[f];
        e.ParentEntry = above >= 0 ? _stickyEntryOf[above] : -1;
        _stickyEntryOf[f] = k;
        return k;
    }

    private static double InsetOf(Length l, double basis) =>
        l.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc ? l.Resolve(basis) : double.NaN;

    private static double MarginOf(Length l, double basis)
    {
        double m = l.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc ? l.Resolve(basis) : 0;
        return double.IsNaN(m) ? 0 : m;
    }

    private void OffsetOf(int entry, out double x, out double y)
    {
        x = y = 0;
        if (entry < 0 || _scroll is null)
        {
            return;
        }

        ref PatchEntry e = ref _patch[entry];
        if (e.Kind == PatchViewportKind)
        {
            x = e.Range.ClampX(_scroll.ViewportX);
            y = e.Range.ClampY(_scroll.ViewportY);
        }
        else
        {
            x = e.Range.ClampX(_scroll.RequestedX(e.Box));
            y = e.Range.ClampY(_scroll.RequestedY(e.Box));
        }
    }

    // The translation an entry's transform item should have for the host's current scroll positions.
    private void ComputeEntry(int k, out float tx, out float ty)
    {
        ref PatchEntry e = ref _patch[k];
        double x, y;
        switch (e.Kind)
        {
            case PatchStickyKind:
                double bx = 0, by = 0;
                if (e.ParentEntry >= 0)
                {
                    bx = _patch[e.ParentEntry].TotalX;
                    by = _patch[e.ParentEntry].TotalY;
                }

                OffsetOf(e.ScrollerEntry, out double sx, out double sy);
                StickyPositioning.Offset(
                    e.BorderBox.Translate(bx, by), e.MarginTop, e.MarginRight, e.MarginBottom, e.MarginLeft,
                    e.Cb.Translate(bx, by), e.Port, sx, sy, e.Top, e.Right, e.Bottom, e.Left, out x, out y);
                e.TotalX = bx + x;
                e.TotalY = by + y;
                break;
            case PatchFixedKind:
                x = e.Range.ClampX(_scroll!.ViewportX);
                y = e.Range.ClampY(_scroll.ViewportY);
                break;
            default:
                OffsetOf(k, out x, out y);
                x = 0.0 - x;
                y = 0.0 - y;
                break;
        }

        tx = (float)Math.Clamp(x, -5e8, 5e8);
        ty = (float)Math.Clamp(y, -5e8, 5e8);
    }

    /// <summary>
    /// Brings the scroll and sticky items of the last paint up to date with the host's scroll positions, in place: no relayout, no
    /// repaint, no allocation. Only the items whose translation changed are patched, so the damage covers just the scrolled areas.
    /// Call it after changing a position on the <see cref="ScrollState"/> the last paint used, with the list that paint produced.
    /// </summary>
    /// <param name="retained">The retained list holding the last painted frame.</param>
    /// <returns>The number of items that changed.</returns>
    public int PatchScroll(RetainedDisplayList retained)
    {
        ArgumentNullException.ThrowIfNull(retained);
        return PatchScroll(retained.Current, retained.Damage);
    }

    /// <summary>The same as <see cref="PatchScroll(RetainedDisplayList)"/> for a plain list.</summary>
    /// <param name="list">The list the last paint produced.</param>
    /// <param name="damage">Receives the changed areas; may be null.</param>
    /// <returns>The number of items that changed.</returns>
    public int PatchScroll(DisplayList list, DamageRegion? damage = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        int changed = 0;
        for (int k = 0; k < _patchCount; k++)
        {
            ComputeEntry(k, out float tx, out float ty);
            if (tx == _patch[k].LastX && ty == _patch[k].LastY)
            {
                continue;
            }

            list.PatchTransform(new ItemId(_patch[k].Id), Matrix3x2.CreateTranslation(tx, ty), damage);
            _patch[k].LastX = tx;
            _patch[k].LastY = ty;
            changed++;
        }

        return changed;
    }

    // The containing-block chain of a positioned (or opacity) fragment up to its stacking context: every ancestor on it that clips
    // its overflow clips this fragment too (CSS Overflow 3 §3; CSS2 §11.1.1).
    private int PushDeferredClips(int entry, int context, out ulong mask)
    {
        mask = 0;
        int count = 0;
        int cur = entry;
        for (int steps = 0; steps < MaxClipSteps && count < MaxDeferredClips; steps++)
        {
            int cb = ContainingBlockOf(cur);
            if (cb < 0 || cb == context)
            {
                break;
            }

            if ((_flags[cb] & FlagClips) != 0)
            {
                _clipChain[count++] = cb;
            }

            cur = cb;
        }

        int pushed = 0;
        for (int k = count - 1; k >= 0 && _scopes < MaxScopes; k--)
        {
            int a = _clipChain[k];
            Box box = _boxes.Make(_tree.BoxOf(a));
            ref readonly ComputedValues av = ref box.Values;
            ItemId id = ItemId.Of((uint)(_tree.BoxOf(entry) + 1), (ushort)(3 | (k << 4)), (ushort)_ordinal[entry]);
            PushOverflowClip(a, in av, id);
            _scopes++;
            int slot = pushed++;
            if ((_flags[a] & FlagScrollable) != 0 && _scopes < MaxScopes)
            {
                // The containing block scrolls, and so does this box with it, although it is painted outside that block's own scope.
                ItemId sid = ItemId.Of((uint)(_tree.BoxOf(entry) + 1), (ushort)(9 | (slot << 4)), (ushort)_ordinal[entry]);
                PushScrollTransform(a, sid, in av);
                mask |= 1UL << slot;
            }
        }

        return pushed;
    }

    // Transforms 1 §2: inside a transformed box a fixed box is positioned against that box and scrolls with it.
    private bool HasTransformedAncestor(int f)
    {
        for (int a = _tree.ParentOf(f); a >= 0; a = _tree.ParentOf(a))
        {
            if ((FragmentKind)_tree.KindOf(a) == FragmentKind.Box && _boxes.Make(_tree.BoxOf(a)).Values.CreatesTransformContext)
            {
                return true;
            }
        }

        return false;
    }

    private int ContainingBlockOf(int f)
    {
        Box box = _boxes.Make(_tree.BoxOf(f));
        PositionKind p = box.Values.Position;
        int a = _tree.ParentOf(f);
        if (p == PositionKind.Fixed && !HasTransformedAncestor(f))
        {
            return -1;
        }

        while (a >= 0)
        {
            if ((FragmentKind)_tree.KindOf(a) == FragmentKind.Box)
            {
                if (p == PositionKind.Fixed)
                {
                    if (_boxes.Make(_tree.BoxOf(a)).Values.CreatesTransformContext)
                    {
                        return a;
                    }
                }
                else if (p != PositionKind.Absolute || _boxes.Make(_tree.BoxOf(a)).Values.Position != PositionKind.Static
                    || _boxes.Make(_tree.BoxOf(a)).Values.CreatesTransformContext || a == 0)
                {
                    return a;
                }
            }

            a = _tree.ParentOf(a);
        }

        return -1;
    }

    private void PushOverflowClip(int f, in ComputedValues v, ItemId id)
    {
        Rect bb = _tree.BorderBoxOf(f);
        Insets border = _tree.BorderOf(f);
        double x = _absX[f] + border.Left;
        double y = _absY[f] + border.Top;
        double w = Math.Max(0, bb.Width - border.Horizontal);
        double h = Math.Max(0, bb.Height - border.Vertical);

        // Overflow 4 §3.2: on an axis with overflow: clip the clip edge can sit at another box edge and be pushed outward.
        bool clipX = v.OverflowX == OverflowKind.Clip, clipY = v.OverflowY == OverflowKind.Clip;
        bool marginApplies = (clipX || clipY) && (v.OverflowClipMarginPx > 0 || v.OverflowClipMarginBox != ClipMarginBoxKind.PaddingBox);
        if (marginApplies)
        {
            Insets pad = _tree.PaddingOf(f);
            double m = v.OverflowClipMarginPx;
            if (clipX)
            {
                switch (v.OverflowClipMarginBox)
                {
                    case ClipMarginBoxKind.BorderBox:
                        x = _absX[f];
                        w = bb.Width;
                        break;
                    case ClipMarginBoxKind.ContentBox:
                        x += pad.Left;
                        w = Math.Max(0, w - pad.Horizontal);
                        break;
                }

                x -= m;
                w += 2 * m;
            }

            if (clipY)
            {
                switch (v.OverflowClipMarginBox)
                {
                    case ClipMarginBoxKind.BorderBox:
                        y = _absY[f];
                        h = bb.Height;
                        break;
                    case ClipMarginBoxKind.ContentBox:
                        y += pad.Top;
                        h = Math.Max(0, h - pad.Vertical);
                        break;
                }

                y -= m;
                h += 2 * m;
            }
        }

        // Only the axes that clip are bounded; a visible axis stays open (Overflow 3 §3.1, overflow: clip with a visible other axis).
        if (v.OverflowX == OverflowKind.Visible)
        {
            x = -1e6;
            w = 2e6;
        }

        if (v.OverflowY == OverflowKind.Visible)
        {
            y = -1e6;
            h = 2e6;
        }

        RectF rect = SnapRect(x, y, w, h);
        CornerRadii outer = ResolveRadii(in v, bb.Width, bb.Height, 0);
        if (outer.IsZero || marginApplies)
        {
            _list.PushClipRect(id, rect);
            return;
        }

        // Backgrounds 3 §5.2: the padding edge's radii are the border-box radii reduced by the border widths.
        var inner = new CornerRadii(
            Shrink(outer.TopLeftX, border.Left), Shrink(outer.TopLeftY, border.Top),
            Shrink(outer.TopRightX, border.Right), Shrink(outer.TopRightY, border.Top),
            Shrink(outer.BottomRightX, border.Right), Shrink(outer.BottomRightY, border.Bottom),
            Shrink(outer.BottomLeftX, border.Left), Shrink(outer.BottomLeftY, border.Bottom));
        _list.PushClipRoundedRect(id, rect, inner);
    }

    // The list rejects coordinates beyond 1e9; hostile or overflowing geometry is pulled inside instead of failing the paint.
    private static float C(double v) => double.IsNaN(v) ? 0f : (float)Math.Clamp(v, -5e8, 5e8);

    private static float Shrink(float radius, double border) => (float)Math.Max(0, radius - border);

    // ------------------------------------------------------------------ box decoration: background-color and borders

    // CSS2 §8.5.3: a side whose style is none or hidden has a used width of zero.
    private static double SideWidth(double width, BorderStyleKind style) =>
        style is BorderStyleKind.None or BorderStyleKind.Hidden || !(width > 0) ? 0 : width;

    private static BorderStyle MapStyle(BorderStyleKind s) => s switch
    {
        BorderStyleKind.Hidden => BorderStyle.Hidden,
        BorderStyleKind.Dotted => BorderStyle.Dotted,
        BorderStyleKind.Dashed => BorderStyle.Dashed,
        BorderStyleKind.Solid => BorderStyle.Solid,
        BorderStyleKind.DoubleLine => BorderStyle.DoubleLine,
        BorderStyleKind.Groove => BorderStyle.Groove,
        BorderStyleKind.Ridge => BorderStyle.Ridge,
        BorderStyleKind.Inset => BorderStyle.Inset,
        BorderStyleKind.Outset => BorderStyle.Outset,
        _ => BorderStyle.None,
    };

    // Backgrounds 3 §5.1: percentages refer to the border box. slice bit 1: the start of the box continues from an earlier piece (left
    // corners square), bit 2: it continues in a later one (right corners square), as box-decoration-break: slice does (§6).
    private static CornerRadii ResolveRadii(in ComputedValues v, double width, double height, int slice)
    {
        float tlx = Radius(v.BorderTopLeftRadius.Horizontal, width);
        float tly = Radius(v.BorderTopLeftRadius.Vertical, height);
        float trx = Radius(v.BorderTopRightRadius.Horizontal, width);
        float tryy = Radius(v.BorderTopRightRadius.Vertical, height);
        float brx = Radius(v.BorderBottomRightRadius.Horizontal, width);
        float bry = Radius(v.BorderBottomRightRadius.Vertical, height);
        float blx = Radius(v.BorderBottomLeftRadius.Horizontal, width);
        float bly = Radius(v.BorderBottomLeftRadius.Vertical, height);
        if ((slice & 1) != 0)
        {
            tlx = tly = blx = bly = 0;
        }

        if ((slice & 2) != 0)
        {
            trx = tryy = brx = bry = 0;
        }

        return new CornerRadii(tlx, tly, trx, tryy, brx, bry, blx, bly);
    }

    private static float Radius(Length l, double basis)
    {
        double r = l.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc ? l.Resolve(basis) : 0;
        return r > 0 && r < 1e8 ? (float)r : 0f;
    }

    private static PaintColor ToColor(ColorValue c, ColorValue current)
    {
        ColorValue r = c.Resolve(current);
        return new PaintColor(r.R, r.G, r.B, r.A);
    }

    private static PaintColor ToColor(ColorValue c, PaintColor current) =>
        c.IsCurrentColor ? current : new PaintColor(c.R, c.G, c.B, c.A);

    // ------------------------------------------------------------------ ids

    // Owner: the box (index + 1, so zero stays "no id"); part: what is painted, plus the high bits of the fragment ordinal; sub: the
    // low 16 bits of the ordinal of the fragment among its box's fragments.
    private ItemId Id(int fragment, int kind)
    {
        int ordinal = _ordinal[fragment];
        return ItemId.Of((uint)(_tree.BoxOf(fragment) + 1), (ushort)(kind | ((ordinal >> 16) << 4)), (ushort)ordinal);
    }
}
