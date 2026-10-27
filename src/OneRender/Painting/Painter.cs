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
public sealed class Painter
{
    // Phases of one stacking context (or one atomic unit), in paint order. CSS2 Appendix E §E.2 steps 3 to 10.
    private const int PhaseNegative = 0; // step 3: child stacking contexts with negative z-index
    private const int PhaseBlocks = 1; // step 4: in-flow, non-inline-level, non-positioned descendants
    private const int PhaseFloats = 2; // step 5: non-positioned floats
    private const int PhaseInlines = 3; // step 7: in-flow inline-level descendants, text
    private const int PhaseZero = 4; // step 8: positioned descendants with z-index auto or 0
    private const int PhasePositive = 5; // step 9: child stacking contexts with positive z-index
    private const int PhaseCount = 6;

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

    private struct Frame
    {
        public int Fragment;
        public int Phase;
        public int Cursor;
        public int PreClips;
        public bool Group;
        public bool Clip;
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
        int boxCount = _boxes.Count;
        if (_boxOrdinal.Length < boxCount)
        {
            _boxOrdinal = new int[Math.Max(boxCount, _boxOrdinal.Length * 2)];
        }

        Array.Clear(_boxOrdinal, 0, boxCount);
        _sortCount = 0;
        _skipBackground = -1;
        _scopes = 0;

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
        _ordinal = new int[size];
        _absX = new double[size];
        _absY = new double[size];
        _baseY = new double[size];
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
        FragmentKind kind = (FragmentKind)_tree.KindOf(i);
        int owner = parent < 0 ? i : _ownerFor[parent];
        int context = parent < 0 ? i : _contextFor[parent];
        _ownerFor[i] = owner;
        _contextFor[i] = context;
        _baseY[i] = parent < 0 ? _absY[i] : _baseY[parent];

        if (kind == FragmentKind.Line)
        {
            _baseY[i] = _absY[i] + _tree.BaselineOf(i);
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
        bool clips = v.OverflowX != OverflowKind.Visible || v.OverflowY != OverflowKind.Visible;
        if (clips)
        {
            _flags[i] |= FlagClips;
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
        bool stacking = (positioned && (v.Position is PositionKind.Fixed or PositionKind.Sticky || !v.ZIndexAuto)) || v.Opacity < 1.0;
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
        // Backgrounds 3 §2.11.2: a transparent HTML root takes the background of its body child.
        if (bg.A <= 0f && string.Equals(rootBox.Node.TagName, "html", StringComparison.Ordinal))
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
                    bg = b.Values.BackgroundColor.Resolve(b.Values.Color);
                    _skipBackground = c;
                    break;
                }
            }
        }

        if (bg.A <= 0f)
        {
            return;
        }

        Rect rb = _tree.BorderBoxOf(root);
        Rect ov = _tree.OverflowOf(root);
        double right = Math.Max(Math.Max(rb.Right, ov.Right), options.ViewportWidth);
        double bottom = Math.Max(Math.Max(rb.Bottom, ov.Bottom), options.ViewportHeight);
        double left = Math.Min(Math.Min(rb.X, ov.X), 0);
        double top = Math.Min(Math.Min(rb.Y, ov.Y), 0);
        _list.FillRect(ItemId.Of(0, 5, 0), new RectF(C(left), C(top), C(right - left), C(bottom - top)), ToColor(bg, rv.Color));
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
                int clips = phaseNow is PhaseNegative or PhaseZero or PhasePositive ? PushDeferredClips(entry, _stack[top].Fragment) : 0;
                PushFrame(ref sp, entry, clips);
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
        if (v.Opacity < 1.0 && (_flags[f] & FlagStackingContext) != 0 && _scopes < MaxScopes)
        {
            _list.PushGroup(Id(f, 4), (float)Math.Clamp(v.Opacity, 0.0, 1.0));
            _scopes++;
            _stack[top].Group = true;
        }

        if (f != 0)
        {
            PaintBoxDecoration(f);
        }

        if ((_flags[f] & FlagClips) != 0 && _scopes < MaxScopes)
        {
            PushOverflowClip(f, in v, Id(f, 5));
            _scopes++;
            _stack[top].Clip = true;
        }
    }

    private void Exit(int top)
    {
        if (_stack[top].Clip)
        {
            _list.PopClip();
            _scopes--;
        }

        if (_stack[top].Group)
        {
            _list.PopGroup();
            _scopes--;
        }

        for (int k = _stack[top].PreClips; k > 0; k--)
        {
            _list.PopClip();
            _scopes--;
        }
    }

    // The containing-block chain of a positioned (or opacity) fragment up to its stacking context: every ancestor on it that clips
    // its overflow clips this fragment too (CSS Overflow 3 §3; CSS2 §11.1.1).
    private int PushDeferredClips(int entry, int context)
    {
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
            pushed++;
        }

        return pushed;
    }

    private int ContainingBlockOf(int f)
    {
        Box box = _boxes.Make(_tree.BoxOf(f));
        PositionKind p = box.Values.Position;
        int a = _tree.ParentOf(f);
        if (p == PositionKind.Fixed)
        {
            return -1;
        }

        while (a >= 0)
        {
            if ((FragmentKind)_tree.KindOf(a) == FragmentKind.Box)
            {
                if (p != PositionKind.Absolute || _boxes.Make(_tree.BoxOf(a)).Values.Position != PositionKind.Static || a == 0)
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

        var rect = new RectF(C(x), C(y), C(w), C(h));
        CornerRadii outer = ResolveRadii(in v, bb.Width, bb.Height, 0);
        if (outer.IsZero)
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

    private void PaintBoxDecoration(int f)
    {
        Box box = _boxes.Make(_tree.BoxOf(f));
        ref readonly ComputedValues v = ref box.Values;
        if (v.Visibility != VisibilityKind.Visible)
        {
            return;
        }

        Rect bb = _tree.BorderBoxOf(f);
        if (!(bb.Width > 0) || !(bb.Height > 0))
        {
            return;
        }

        FragmentTraits traits = (FragmentTraits)_tree.TraitsOf(f);
        bool sliceStart = (traits & FragmentTraits.ContinuesBefore) != 0;
        bool sliceEnd = (traits & FragmentTraits.ContinuesAfter) != 0;
        var rect = new RectF(C(_absX[f]), C(_absY[f]), C(bb.Width), C(bb.Height));
        CornerRadii radii = ResolveRadii(in v, bb.Width, bb.Height, (sliceStart ? 1 : 0) | (sliceEnd ? 2 : 0));

        if (f != _skipBackground && v.BackgroundColor.A > 0f)
        {
            PaintColor c = ToColor(v.BackgroundColor, v.Color);
            if (radii.IsZero)
            {
                _list.FillRect(Id(f, 0), rect, c);
            }
            else
            {
                _list.FillRoundedRect(Id(f, 0), rect, radii, c);
            }
        }

        Insets border = _tree.BorderOf(f);
        double top = SideWidth(border.Top, v.BorderTopStyle);
        double right = sliceEnd ? 0 : SideWidth(border.Right, v.BorderRightStyle);
        double bottom = SideWidth(border.Bottom, v.BorderBottomStyle);
        double left = sliceStart ? 0 : SideWidth(border.Left, v.BorderLeftStyle);
        if (top > 0 || right > 0 || bottom > 0 || left > 0)
        {
            PaintColor cur = ToColor(v.Color, v.Color);
            _list.DrawBorder(
                Id(f, 1),
                rect,
                radii,
                new BorderSide(C(top), MapStyle(v.BorderTopStyle), ToColor(v.BorderTopColor, cur)),
                new BorderSide(C(right), MapStyle(v.BorderRightStyle), ToColor(v.BorderRightColor, cur)),
                new BorderSide(C(bottom), MapStyle(v.BorderBottomStyle), ToColor(v.BorderBottomColor, cur)),
                new BorderSide(C(left), MapStyle(v.BorderLeftStyle), ToColor(v.BorderLeftColor, cur)));
        }
    }

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

    // ------------------------------------------------------------------ text

    private void PaintText(int f)
    {
        Box box = _boxes.Make(_tree.BoxOf(f));
        ref readonly ComputedValues v = ref box.Values;
        if (v.Visibility != VisibilityKind.Visible)
        {
            return;
        }

        GlyphRun run = _tree.GetGlyphRun(_tree.GlyphRunOf(f));
        int count = run.GlyphCount;
        if (count == 0)
        {
            return;
        }

        if (_gx.Length < count)
        {
            int size = Math.Max(count, _gx.Length * 2);
            _gx = new float[size];
            _gy = new float[size];
            _gc = new int[size];
        }

        ReadOnlySpan<double> xs = _tree.GlyphX(in run);
        ReadOnlySpan<double> ys = _tree.GlyphY(in run);
        ReadOnlySpan<int> clusters = _tree.GlyphClusters(in run);
        ReadOnlySpan<char> all = box.Text;
        int start = Math.Clamp(_tree.TextStartOf(f), 0, all.Length);
        int length = Math.Clamp(_tree.TextLengthOf(f), 0, all.Length - start);
        ReadOnlySpan<char> text = all.Slice(start, length);
        for (int k = 0; k < count; k++)
        {
            _gx[k] = C(xs[k]);
            _gy[k] = C(ys[k]);
            // Clusters are offsets into the box's text; the list wants them relative to the run's text.
            _gc[k] = Math.Clamp(clusters[k] - start, 0, length);
        }

        _list.DrawGlyphRun(
            Id(f, 2),
            run.Face,
            (float)run.FontSize,
            C(_absX[f]),
            C(_baseY[f]),
            new PaintColor(v.Color.R, v.Color.G, v.Color.B, v.Color.A),
            _tree.GlyphIds(in run),
            _gx.AsSpan(0, count),
            _gy.AsSpan(0, count),
            _gc.AsSpan(0, count),
            text,
            run.RightToLeft);
    }

    // ------------------------------------------------------------------ ids

    // Owner: the box (index + 1, so zero stays "no id"); part: what is painted, plus the high bits of the fragment ordinal; sub: the
    // low 16 bits of the ordinal of the fragment among its box's fragments.
    private ItemId Id(int fragment, int kind)
    {
        int ordinal = _ordinal[fragment];
        return ItemId.Of((uint)(_tree.BoxOf(fragment) + 1), (ushort)(kind | ((ordinal >> 16) << 4)), (ushort)ordinal);
    }
}
