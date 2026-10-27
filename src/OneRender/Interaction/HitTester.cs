using OneRender.Painting;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;

namespace OneRender.Interaction;

/// <summary>What a hit test found at a point.</summary>
public readonly struct HitResult
{
    internal HitResult(StyledNode element, Fragment fragment, double localX, double localY, StyledNode textNode, int textOffset)
    {
        Element = element;
        Fragment = fragment;
        LocalX = localX;
        LocalY = localY;
        TextNode = textNode;
        TextOffset = textOffset;
    }

    /// <summary>The element that is the target: the element of the box hit, or the parent element when the point is on text, a marker or generated content.</summary>
    public StyledNode Element { get; }

    /// <summary>The fragment that was hit: a box fragment, or a text fragment.</summary>
    public Fragment Fragment { get; }

    /// <summary>The point in the fragment's border-box space: after undoing scroll offsets and transforms of the fragment and its ancestors, relative to the fragment's top-left corner.</summary>
    public double LocalX { get; }

    /// <summary>See <see cref="LocalX"/>.</summary>
    public double LocalY { get; }

    /// <summary>The text node under the point, or the null handle when the hit is not on text.</summary>
    public StyledNode TextNode { get; }

    /// <summary>
    /// When the hit is on text: the UTF-16 offset into <see cref="TextNode"/>'s text where a caret placed at the point would sit
    /// (the nearer cluster edge); -1 otherwise.
    /// </summary>
    public int TextOffset { get; }

    /// <summary>True when the point is on a run of text.</summary>
    public bool IsText => TextOffset >= 0;

    /// <summary>The OneHtml element behind <see cref="Element"/>, or the null node for a node built in C#.</summary>
    public Node Node
    {
        get
        {
            if (Element.IsNull)
            {
                return default;
            }

            Document? doc = Element.Tree!.SourceDocument;
            int src = Element.SourceIndex;
            return doc is null || src < 0 ? default : doc.GetNode(src);
        }
    }
}

/// <summary>
/// Finds what is under a point, in CSS 2.2 Appendix E paint order, and maps between layout space and viewport space. It reads the
/// fragment tree, the box styles and the host's <see cref="ScrollState"/>; it never changes them.
/// </summary>
/// <remarks>
/// <para>
/// Fragments are put in the same stacking contexts and phases as the painter puts them (negative z-index, blocks, floats, inline
/// content, positioned with z-index auto or 0, positive z-index), and the point is tried against them in the reverse of paint
/// order, so the first fragment hit is the topmost. Tests honour <c>visibility</c>, <c>pointer-events</c>, rounded border edges,
/// overflow clips (with their radii), scroll offsets and 2D transforms, including those of ancestors. A box that is out of its
/// containing block's clip because it is positioned is clipped by that block and not by the ancestors in between (CSS Overflow 3 §3).
/// </para>
/// <para>
/// The tree is indexed once by <see cref="Rebuild"/>; queries allocate nothing and use explicit stacks, never recursion over tree
/// depth. At most <see cref="MaxEffectChain"/> clipping, scrolling or transformed ancestors are applied to one fragment; a fragment
/// under more is not hit. Not thread-safe.
/// </para>
/// <para>
/// Coordinates are viewport (client) coordinates: the scroll offset of the viewport is applied inside.
/// </para>
/// </remarks>
public sealed class HitTester
{
    /// <summary>The most clipping, scrolling or transformed ancestors applied to one fragment. 1,024.</summary>
    public const int MaxEffectChain = 1024;

    private const int PhaseNegative = 0;
    private const int PhaseBlocks = 1;
    private const int PhaseFloats = 2;
    private const int PhaseInlines = 3;
    private const int PhaseZero = 4;
    private const int PhasePositive = 5;
    private const int PhaseCount = 6;

    private const ushort FlagAtomic = 1;
    private const ushort FlagStacking = 2;
    private const ushort FlagClips = 4;
    private const ushort FlagText = 8;
    private const ushort FlagScroller = 16;
    private const ushort FlagScrollable = 32;
    private const ushort FlagTransform = 64;
    private const ushort FlagFixed = 128;

    private readonly FragmentTree _tree;
    private readonly BoxTree _boxes;
    private readonly ScrollState? _scroll;
    private readonly double _vw;
    private readonly double _vh;

    private int _built = -1;
    private int _builtVersion = -1;
    private ushort[] _flags = new ushort[64];
    private int[] _owner = new int[64];
    private int[] _context = new int[64];
    private int[] _containing = new int[64];
    private int[] _effectParent = new int[64];
    private double[] _absX = new double[64];
    private double[] _absY = new double[64];
    private int[] _head = new int[64 * PhaseCount];
    private int[] _tail = new int[64 * PhaseCount];
    private int[] _prev = new int[64];
    private ScrollRange[] _range = new ScrollRange[64];
    private long[] _sortKeys = new long[64];
    private int[] _sortFrags = new int[64];
    private int _sortCount;
    private int _vpSource = -1;
    private ScrollRange _vpRange;

    // Traversal stack of units (stacking contexts and other atomic fragments).
    private int[] _unit = new int[32];
    private int[] _unitPhase = new int[32];
    private int[] _unitCur = new int[32];

    private readonly int[] _chain = new int[MaxEffectChain + 2];
    private readonly HitResult[] _one = new HitResult[1];

    /// <summary>Creates a hit tester for a laid-out tree.</summary>
    /// <param name="tree">The fragment tree. Call <see cref="Rebuild"/> again after layout changes it.</param>
    /// <param name="scroll">The host's scroll positions; null means everything is at scroll position zero.</param>
    /// <param name="viewportWidth">The viewport width in CSS px; 0 or less means unknown, which turns off the viewport bounds and viewport scrolling.</param>
    /// <param name="viewportHeight">The viewport height in CSS px.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is null.</exception>
    public HitTester(FragmentTree tree, ScrollState? scroll = null, double viewportWidth = 0, double viewportHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(tree);
        _tree = tree;
        _boxes = tree.Boxes;
        _scroll = scroll;
        _vw = viewportWidth;
        _vh = viewportHeight;
    }

    /// <summary>The fragment tree this tester reads.</summary>
    public FragmentTree Tree => _tree;

    /// <summary>The viewport width given at creation.</summary>
    public double ViewportWidth => _vw;

    /// <summary>The viewport height given at creation.</summary>
    public double ViewportHeight => _vh;

    /// <summary>Indexes the fragment tree. Call it after each layout; queries also call it when the fragment count changed.</summary>
    public void Rebuild()
    {
        int n = _tree.Count;
        _built = n;
        _builtVersion = _tree.Version;
        if (n == 0)
        {
            return;
        }

        EnsureCapacity(n);
        Array.Fill(_head, -1, 0, n * PhaseCount);
        Array.Fill(_tail, -1, 0, n * PhaseCount);
        _sortCount = 0;
        ViewportOverflow.Resolve(_boxes, out _, out _, out _vpSource);
        _vpRange = _scroll is null ? default : ScrollState.GetViewportRange(_tree, _vw, _vh);

        int i = 0;
        while (i >= 0)
        {
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

        // Entries a stacking context paints at its z-order steps are ordered by (z-index, tree order) once, then linked in that order.
        Array.Sort(_sortKeys, _sortFrags, 0, _sortCount);
        for (int k = 0; k < _sortCount; k++)
        {
            int f = _sortFrags[k];
            int z = (int)((_sortKeys[k] >> 32) - 0x80000000L);
            int p = _tree.ParentOf(f);
            int context = p < 0 ? f : _context[p];
            AddEntry(context, z < 0 ? PhaseNegative : (z == 0 ? PhaseZero : PhasePositive), f);
        }
    }

    private void EnsureCapacity(int n)
    {
        if (_flags.Length >= n)
        {
            return;
        }

        int size = Math.Max(n, _flags.Length * 2);
        _flags = new ushort[size];
        _owner = new int[size];
        _context = new int[size];
        _containing = new int[size];
        _effectParent = new int[size];
        _absX = new double[size];
        _absY = new double[size];
        _head = new int[size * PhaseCount];
        _tail = new int[size * PhaseCount];
        _prev = new int[size];
        _range = new ScrollRange[size];
        _sortKeys = new long[size];
        _sortFrags = new int[size];
    }

    private void Visit(int i)
    {
        int parent = _tree.ParentOf(i);
        Rect bb = _tree.BorderBoxOf(i);
        _absX[i] = parent < 0 ? bb.X : _absX[parent] + bb.X;
        _absY[i] = parent < 0 ? bb.Y : _absY[parent] + bb.Y;
        _flags[i] = 0;
        _prev[i] = -1;
        FragmentKind kind = (FragmentKind)_tree.KindOf(i);
        int owner = parent < 0 ? i : _owner[parent];
        int context = parent < 0 ? i : _context[parent];
        _owner[i] = owner;
        _context[i] = context;
        _containing[i] = parent;
        _effectParent[i] = parent < 0 ? -1 : (IsEffect(parent) ? parent : _effectParent[parent]);

        if (kind is FragmentKind.Line or FragmentKind.Column)
        {
            return;
        }

        int boxIndex = _tree.BoxOf(i);
        if (kind == FragmentKind.Text)
        {
            if (_tree.GlyphRunOf(i).IsValid)
            {
                _flags[i] = FlagText;
                AddEntry(owner, PhaseInlines, i);
            }

            return;
        }

        Box box = _boxes.Make(boxIndex);
        ref readonly ComputedValues v = ref box.Values;
        bool propagated = boxIndex == _vpSource;
        bool clips = !propagated && (v.OverflowX != OverflowKind.Visible || v.OverflowY != OverflowKind.Visible);
        if (clips)
        {
            _flags[i] |= FlagClips;
        }

        if (!propagated && v.IsScrollContainer)
        {
            _flags[i] |= FlagScroller;
            ScrollRange r = ScrollRange.Of(_tree, i, in v);
            _range[i] = r;
            if (_scroll is not null && r.CanScroll)
            {
                _flags[i] |= FlagScrollable;
            }
        }

        if (v.HasTransform)
        {
            _flags[i] |= FlagTransform;
        }

        if (v.Position == PositionKind.Fixed)
        {
            _flags[i] |= FlagFixed;
        }

        // Positioned boxes belong to the containing-block chain, not to the tree parent, for clips and scrolling (CSS2 §10.1).
        if (parent >= 0 && v.Position is PositionKind.Absolute or PositionKind.Fixed)
        {
            _containing[i] = ContainingBlockOf(i, v.Position);
            _effectParent[i] = _containing[i] < 0 ? -1 : (IsEffect(_containing[i]) ? _containing[i] : _effectParent[_containing[i]]);
        }

        if (parent < 0)
        {
            _flags[i] |= FlagAtomic | FlagStacking;
            _owner[i] = i;
            _context[i] = i;
            return;
        }

        bool positioned = v.Position != PositionKind.Static;
        bool stacking = (positioned && (v.Position is PositionKind.Fixed or PositionKind.Sticky || !v.ZIndexAuto)) || v.Opacity < 1.0 || v.HasTransform;
        if (stacking)
        {
            int z = positioned && !v.ZIndexAuto ? v.ZIndex : 0;
            _flags[i] |= FlagAtomic | FlagStacking;
            _owner[i] = i;
            _context[i] = i;
            Defer(i, z);
            return;
        }

        if (positioned)
        {
            _flags[i] |= FlagAtomic;
            _owner[i] = i;
            Defer(i, 0);
            return;
        }

        if (v.Floating != FloatKind.None)
        {
            _flags[i] |= FlagAtomic;
            _owner[i] = i;
            AddEntry(owner, PhaseFloats, i);
            return;
        }

        if (box.Level == BoxLevel.Inline)
        {
            if (box.IsAtomicInline)
            {
                _flags[i] |= FlagAtomic;
                _owner[i] = i;
            }

            AddEntry(owner, PhaseInlines, i);
            return;
        }

        if (clips)
        {
            _flags[i] |= FlagAtomic;
            _owner[i] = i;
        }

        AddEntry(owner, PhaseBlocks, i);
    }

    // The nearest ancestor box that contains an absolutely positioned box: the nearest positioned one, or the root. Fixed boxes
    // are contained by the viewport, which neither clips nor scrolls them.
    private int ContainingBlockOf(int f, PositionKind position)
    {
        if (position == PositionKind.Fixed)
        {
            return -1;
        }

        for (int a = _tree.ParentOf(f); a >= 0; a = _tree.ParentOf(a))
        {
            if ((FragmentKind)_tree.KindOf(a) == FragmentKind.Box && (a == 0 || _boxes.Make(_tree.BoxOf(a)).Values.Position != PositionKind.Static))
            {
                return a;
            }
        }

        return -1;
    }

    private bool IsEffect(int f) =>
        (_flags[f] & (FlagClips | FlagScrollable | FlagTransform)) != 0 || (f == 0 && _vpRange.CanScroll);

    private void Defer(int fragment, int z)
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

    private void AddEntry(int owner, int phase, int fragment)
    {
        int slot = (owner * PhaseCount) + phase;
        _prev[fragment] = _tail[slot];
        if (_tail[slot] < 0)
        {
            _head[slot] = fragment;
        }

        _tail[slot] = fragment;
    }

    // ------------------------------------------------------------------ queries

    /// <summary>Finds the topmost target at a point.</summary>
    /// <param name="x">Viewport x in CSS px.</param>
    /// <param name="y">Viewport y in CSS px.</param>
    /// <param name="result">What was hit.</param>
    /// <returns>False when the point is outside the viewport, or nothing is there (an empty tree).</returns>
    public bool HitTest(double x, double y, out HitResult result)
    {
        int n = Collect(x, y, _one);
        result = n > 0 ? _one[0] : default;
        _one[0] = default;
        return n > 0;
    }

    /// <summary>
    /// Finds every element under a point, topmost first (<c>elementsFromPoint</c>): one result per element, each from its topmost
    /// fragment, ending with the root element, which covers the whole canvas.
    /// </summary>
    /// <param name="x">Viewport x in CSS px.</param>
    /// <param name="y">Viewport y in CSS px.</param>
    /// <param name="results">Receives the results; extra hits beyond its length are dropped.</param>
    /// <returns>The number of results written.</returns>
    public int HitTestAll(double x, double y, Span<HitResult> results) => Collect(x, y, results);

    private int Collect(double x, double y, Span<HitResult> results)
    {
        if (results.IsEmpty || double.IsNaN(x) || double.IsNaN(y))
        {
            return 0;
        }

        if (_tree.Count != _built || _tree.Version != _builtVersion)
        {
            Rebuild();
        }

        if (_tree.Count == 0)
        {
            return 0;
        }

        // The point must lie in the viewport (CSSOM View: elementFromPoint returns null outside it).
        if ((_vw > 0 && (x < 0 || x >= _vw)) || (_vh > 0 && (y < 0 || y >= _vh)))
        {
            return 0;
        }

        int found = 0;
        int sp = 0;
        PushUnit(ref sp, 0);
        while (sp > 0 && found < results.Length)
        {
            int top = sp - 1;
            int u = _unit[top];
            int phase = _unitPhase[top];
            if (phase < PhaseNegative)
            {
                // Lowest layer: the unit's own box.
                sp--;
                TestFragment(u, x, y, results, ref found);
                continue;
            }

            int cur = _unitCur[top];
            cur = cur == -2 ? _tail[(u * PhaseCount) + phase] : _prev[cur];
            if (cur < 0)
            {
                _unitPhase[top] = phase - 1;
                _unitCur[top] = -2;
                continue;
            }

            _unitCur[top] = cur;
            if ((_flags[cur] & FlagAtomic) != 0 && cur != u)
            {
                PushUnit(ref sp, cur);
            }
            else
            {
                TestFragment(cur, x, y, results, ref found);
            }
        }

        // The root element stands for the canvas: whatever else is under the point, it is too (CSS Backgrounds 3 §2.11.2).
        if (found < results.Length)
        {
            StyledNode rootElement = ElementOf(0);
            if (!rootElement.IsNull && !Contains(results, found, rootElement) && _boxes.Make(_tree.BoxOf(0)).Values.PointerEvents != PointerEventsKind.None)
            {
                results[found++] = new HitResult(rootElement, _tree.Root, x, y, default, -1);
            }
        }

        return found;
    }

    private void PushUnit(ref int sp, int fragment)
    {
        if (sp == _unit.Length)
        {
            Array.Resize(ref _unit, sp * 2);
            Array.Resize(ref _unitPhase, sp * 2);
            Array.Resize(ref _unitCur, sp * 2);
        }

        _unit[sp] = fragment;
        _unitPhase[sp] = PhasePositive;
        _unitCur[sp] = -2;
        sp++;
    }

    private static bool Contains(Span<HitResult> results, int count, StyledNode element)
    {
        for (int i = 0; i < count; i++)
        {
            if (results[i].Element == element)
            {
                return true;
            }
        }

        return false;
    }

    // Tests one fragment's own area and, when it hits, adds it unless the element is already listed.
    private void TestFragment(int f, double px, double py, Span<HitResult> results, ref int found)
    {
        FragmentKind kind = (FragmentKind)_tree.KindOf(f);
        int boxIndex = _tree.BoxOf(f);
        if (boxIndex < 0)
        {
            return;
        }

        Box box = _boxes.Make(boxIndex);
        ref readonly ComputedValues v = ref box.Values;
        if (v.Visibility != VisibilityKind.Visible || v.PointerEvents == PointerEventsKind.None)
        {
            return;
        }

        if (!MapToFragment(f, px, py, out double qx, out double qy))
        {
            return;
        }

        Rect bb = _tree.BorderBoxOf(f);
        double lx = qx - _absX[f];
        double ly = qy - _absY[f];
        if (!(lx >= 0 && ly >= 0 && lx < bb.Width && ly < bb.Height))
        {
            return;
        }

        StyledNode textNode = default;
        int textOffset = -1;
        if (kind == FragmentKind.Text)
        {
            textNode = box.Node;
            textOffset = TextOffsetAt(f, box, lx);
        }
        else
        {
            FragmentTraits traits = (FragmentTraits)_tree.TraitsOf(f);
            if (!InRoundedBox(in v, bb.Width, bb.Height, lx, ly, (traits & FragmentTraits.ContinuesBefore) != 0, (traits & FragmentTraits.ContinuesAfter) != 0))
            {
                return;
            }
        }

        StyledNode element = ElementOf(f);
        if (element.IsNull || Contains(results, found, element))
        {
            return;
        }

        results[found++] = new HitResult(element, _tree.Make(f), lx, ly, textNode, textOffset);
    }

    /// <summary>The element a fragment belongs to: its box's element, or the parent element for text, markers and generated content.</summary>
    internal StyledNode ElementOf(int f)
    {
        int bi = _tree.BoxOf(f);
        if (bi < 0)
        {
            return default;
        }

        Box box = _boxes.Make(bi);
        while (!box.IsNull && box.Node.IsNull)
        {
            box = box.Parent;
        }

        if (box.IsNull)
        {
            return default;
        }

        StyledNode n = box.Node;
        while (!n.IsNull && n.Kind != StyledNodeKind.Element)
        {
            n = n.Parent;
        }

        return n;
    }

    // Applies, from the outermost to the fragment itself, the inverse of every scroll, transform and clip on the way, leaving the
    // point in the fragment's layout (absolute, untransformed) space. False when a clip excludes it or a transform cannot be inverted.
    private bool MapToFragment(int f, double px, double py, out double qx, out double qy)
    {
        qx = px;
        qy = py;
        int count = GatherChain(f, out bool selfFirst);
        if (count < 0)
        {
            return false;
        }

        for (int k = count - 1; k >= 0; k--)
        {
            int a = _chain[k];
            bool self = selfFirst && k == 0;
            if (a == 0 && _vpRange.CanScroll)
            {
                qx += _vpRange.ClampX(_scroll!.ViewportX);
                qy += _vpRange.ClampY(_scroll.ViewportY);
            }

            if ((_flags[a] & FlagTransform) != 0 && !InverseTransform(a, ref qx, ref qy))
            {
                return false;
            }

            if (self)
            {
                continue;
            }

            if ((_flags[a] & FlagClips) != 0 && !InsideClip(a, qx, qy))
            {
                return false;
            }

            if ((_flags[a] & FlagScrollable) != 0)
            {
                ScrollOffset(a, out double sx, out double sy);
                qx += sx;
                qy += sy;
            }
        }

        return true;
    }

    // Collects the effectful fragments that apply to f: itself first when it is one, then the chain of effectful containing blocks.
    private int GatherChain(int f, out bool selfFirst)
    {
        int count = 0;
        selfFirst = false;
        if (IsEffect(f))
        {
            _chain[count++] = f;
            selfFirst = true;
        }

        for (int a = _effectParent[f]; a >= 0; a = _effectParent[a])
        {
            if (count >= MaxEffectChain)
            {
                return -1;
            }

            _chain[count++] = a;
        }

        return count;
    }

    private void ScrollOffset(int a, out double x, out double y)
    {
        ScrollRange r = _range[a];
        int bi = _tree.BoxOf(a);
        x = r.ClampX(_scroll!.RequestedX(bi));
        y = r.ClampY(_scroll.RequestedY(bi));
    }

    private bool TransformOf(int a, out System.Numerics.Matrix4x4 m)
    {
        ref readonly ComputedValues v = ref _boxes.Make(_tree.BoxOf(a)).Values;
        Rect bb = _tree.BorderBoxOf(a);
        Insets bd = _tree.BorderOf(a);
        Insets pd = _tree.PaddingOf(a);
        var border = new RectF((float)_absX[a], (float)_absY[a], (float)bb.Width, (float)bb.Height);
        var content = new RectF((float)(_absX[a] + bd.Left + pd.Left), (float)(_absY[a] + bd.Top + pd.Top),
            (float)Math.Max(0, bb.Width - bd.Horizontal - pd.Horizontal), (float)Math.Max(0, bb.Height - bd.Vertical - pd.Vertical));
        return TransformGeometry.Compute(in v, in border, in content, out m);
    }

    private bool InverseTransform(int a, ref double qx, ref double qy)
    {
        if (!TransformOf(a, out System.Numerics.Matrix4x4 m) || !TransformGeometry.UnmapPoint(in m, (float)qx, (float)qy, out float ox, out float oy))
        {
            return false;
        }

        qx = ox;
        qy = oy;
        return true;
    }

    // CSS Overflow 3 §3: content is clipped to the padding box (with the rounded inner edge), on the axes that clip.
    private bool InsideClip(int a, double qx, double qy)
    {
        Box box = _boxes.Make(_tree.BoxOf(a));
        ref readonly ComputedValues v = ref box.Values;
        Rect bb = _tree.BorderBoxOf(a);
        Insets border = _tree.BorderOf(a);
        double x = _absX[a] + border.Left;
        double y = _absY[a] + border.Top;
        double w = Math.Max(0, bb.Width - border.Horizontal);
        double h = Math.Max(0, bb.Height - border.Vertical);
        bool clipX = v.OverflowX == OverflowKind.Clip, clipY = v.OverflowY == OverflowKind.Clip;
        if ((clipX || clipY) && (v.OverflowClipMarginPx > 0 || v.OverflowClipMarginBox != ClipMarginBoxKind.PaddingBox))
        {
            // Overflow 4 §3.2: overflow-clip-margin moves the clip edge outward from the chosen box.
            Insets pad = _tree.PaddingOf(a);
            double m = v.OverflowClipMarginPx;
            if (clipX)
            {
                if (v.OverflowClipMarginBox == ClipMarginBoxKind.BorderBox)
                {
                    x = _absX[a];
                    w = bb.Width;
                }
                else if (v.OverflowClipMarginBox == ClipMarginBoxKind.ContentBox)
                {
                    x += pad.Left;
                    w = Math.Max(0, w - pad.Horizontal);
                }

                x -= m;
                w += 2 * m;
            }

            if (clipY)
            {
                if (v.OverflowClipMarginBox == ClipMarginBoxKind.BorderBox)
                {
                    y = _absY[a];
                    h = bb.Height;
                }
                else if (v.OverflowClipMarginBox == ClipMarginBoxKind.ContentBox)
                {
                    y += pad.Top;
                    h = Math.Max(0, h - pad.Vertical);
                }

                y -= m;
                h += 2 * m;
            }

            return InAxis(qx, v.OverflowX, x, w) && InAxis(qy, v.OverflowY, y, h);
        }

        if (!InAxis(qx, v.OverflowX, x, w) || !InAxis(qy, v.OverflowY, y, h))
        {
            return false;
        }

        // Backgrounds 3 §5.2: the padding edge's radii are the border-box radii reduced by the border widths.
        if (v.OverflowX != OverflowKind.Visible && v.OverflowY != OverflowKind.Visible)
        {
            Radii outer = ResolveRadii(in v, bb.Width, bb.Height, false, false);
            if (!outer.IsZero)
            {
                var inner = new Radii(
                    Math.Max(0, outer.Tlx - border.Left), Math.Max(0, outer.Tly - border.Top),
                    Math.Max(0, outer.Trx - border.Right), Math.Max(0, outer.Try - border.Top),
                    Math.Max(0, outer.Brx - border.Right), Math.Max(0, outer.Bry - border.Bottom),
                    Math.Max(0, outer.Blx - border.Left), Math.Max(0, outer.Bly - border.Bottom));
                return InRounded(qx - x, qy - y, w, h, inner);
            }
        }

        return true;
    }

    private static bool InAxis(double q, OverflowKind kind, double start, double length) =>
        kind == OverflowKind.Visible || (q >= start && q < start + length);

    // ------------------------------------------------------------------ rounded boxes

    private readonly struct Radii
    {
        public Radii(double tlx, double tly, double trx, double tryy, double brx, double bry, double blx, double bly)
        {
            Tlx = tlx;
            Tly = tly;
            Trx = trx;
            Try = tryy;
            Brx = brx;
            Bry = bry;
            Blx = blx;
            Bly = bly;
        }

        public double Tlx { get; }

        public double Tly { get; }

        public double Trx { get; }

        public double Try { get; }

        public double Brx { get; }

        public double Bry { get; }

        public double Blx { get; }

        public double Bly { get; }

        public bool IsZero => Tlx <= 0 && Tly <= 0 && Trx <= 0 && Try <= 0 && Brx <= 0 && Bry <= 0 && Blx <= 0 && Bly <= 0;
    }

    private static double Radius(Length l, double basis)
    {
        double r = l.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc ? l.Resolve(basis) : 0;
        return r > 0 && r < 1e8 ? r : 0;
    }

    // Backgrounds 3 §5.1 and §5.5: percentages refer to the border box, and radii that overlap are scaled down together.
    // A box that continues from or into another fragment has square corners on that side (box-decoration-break: slice, §6).
    private static Radii ResolveRadii(in ComputedValues v, double w, double h, bool sliceStart, bool sliceEnd)
    {
        double tlx = Radius(v.BorderTopLeftRadius.Horizontal, w), tly = Radius(v.BorderTopLeftRadius.Vertical, h);
        double trx = Radius(v.BorderTopRightRadius.Horizontal, w), tryy = Radius(v.BorderTopRightRadius.Vertical, h);
        double brx = Radius(v.BorderBottomRightRadius.Horizontal, w), bry = Radius(v.BorderBottomRightRadius.Vertical, h);
        double blx = Radius(v.BorderBottomLeftRadius.Horizontal, w), bly = Radius(v.BorderBottomLeftRadius.Vertical, h);
        if (sliceStart)
        {
            tlx = tly = blx = bly = 0;
        }

        if (sliceEnd)
        {
            trx = tryy = brx = bry = 0;
        }

        double f = 1;
        f = ScaleFor(f, w, tlx + trx);
        f = ScaleFor(f, w, blx + brx);
        f = ScaleFor(f, h, tly + bly);
        f = ScaleFor(f, h, tryy + bry);
        return f < 1
            ? new Radii(tlx * f, tly * f, trx * f, tryy * f, brx * f, bry * f, blx * f, bly * f)
            : new Radii(tlx, tly, trx, tryy, brx, bry, blx, bly);
    }

    private static double ScaleFor(double f, double length, double sum) => sum > length && sum > 0 ? Math.Min(f, length / sum) : f;

    private static bool InRoundedBox(in ComputedValues v, double w, double h, double x, double y, bool sliceStart, bool sliceEnd)
    {
        Radii r = ResolveRadii(in v, w, h, sliceStart, sliceEnd);
        return r.IsZero || InRounded(x, y, w, h, r);
    }

    // x and y are relative to the rectangle's top-left corner.
    private static bool InRounded(double x, double y, double w, double h, Radii r)
    {
        if (!(x >= 0 && y >= 0 && x < w && y < h))
        {
            return false;
        }

        return InCorner(x, y, r.Tlx, r.Tly, r.Tlx, r.Tly)
            && InCorner(w - x, y, r.Trx, r.Try, r.Trx, r.Try)
            && InCorner(w - x, h - y, r.Brx, r.Bry, r.Brx, r.Bry)
            && InCorner(x, h - y, r.Blx, r.Bly, r.Blx, r.Bly);
    }

    // The point is measured from the corner; inside the corner square it must lie within the quarter ellipse.
    private static bool InCorner(double dx, double dy, double rx, double ry, double cx, double cy)
    {
        if (rx <= 0 || ry <= 0 || dx >= cx || dy >= cy)
        {
            return true;
        }

        double ex = (cx - dx) / rx;
        double ey = (cy - dy) / ry;
        return (ex * ex) + (ey * ey) <= 1;
    }

    // ------------------------------------------------------------------ text positions

    // The caret offset nearest to a point on a text fragment: the edge of the glyph cluster under it that the point is closer to.
    private int TextOffsetAt(int f, Box box, double localX)
    {
        GlyphRunId id = _tree.GlyphRunOf(f);
        int start = _tree.TextStartOf(f);
        int end = start + _tree.TextLengthOf(f);
        if (!id.IsValid)
        {
            return start;
        }

        GlyphRun run = _tree.GetGlyphRun(id);
        ReadOnlySpan<double> xs = _tree.GlyphX(in run);
        ReadOnlySpan<int> clusters = _tree.GlyphClusters(in run);
        int count = xs.Length;
        if (count == 0)
        {
            return start;
        }

        double width = _tree.BorderBoxOf(f).Width;

        // Glyphs are in visual order, so glyph k covers [x[k], x[k+1]) on screen (the last to the fragment's end).
        int hit = count - 1;
        for (int k = 0; k < count; k++)
        {
            double right = k + 1 < count ? xs[k + 1] : width;
            if (localX < right)
            {
                hit = k;
                break;
            }
        }

        double left = xs[hit];
        double rightEdge = hit + 1 < count ? xs[hit + 1] : width;
        bool nearerStart = localX < left + ((rightEdge - left) / 2);
        int cluster = Math.Clamp(clusters[hit], start, end);

        // The cluster spans from its own offset to the next larger cluster offset in the run (or the end of the text shown).
        int next = end;
        for (int k = 0; k < count; k++)
        {
            int c = clusters[k];
            if (c > cluster && c < next)
            {
                next = c;
            }
        }

        bool leadingIsStart = !run.RightToLeft;
        return nearerStart == leadingIsStart ? cluster : next;
    }

    // ------------------------------------------------------------------ layout space to viewport space

    /// <summary>
    /// Maps a rectangle given in a fragment's absolute layout space to its bounding box in the viewport: scroll offsets of the
    /// viewport and of scrolled ancestors are subtracted and 2D transforms applied (CSSOM View: the geometry
    /// <c>getBoundingClientRect</c> reports).
    /// </summary>
    /// <param name="fragment">The fragment whose chain of ancestors applies.</param>
    /// <param name="rect">A rectangle in absolute layout space, usually the fragment's border box.</param>
    /// <returns>The bounding box in viewport space.</returns>
    /// <exception cref="ArgumentException">The fragment is not in this tester's tree.</exception>
    public Rect ToViewport(Fragment fragment, Rect rect)
    {
        if (fragment.IsNull || !ReferenceEquals(fragment.Tree, _tree))
        {
            throw new ArgumentException("The fragment is not in this tree.", nameof(fragment));
        }

        if (_tree.Count != _built || _tree.Version != _builtVersion)
        {
            Rebuild();
        }

        int f = fragment.Index;
        int count = GatherChain(f, out bool selfFirst);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        for (int corner = 0; corner < 4; corner++)
        {
            double x = (corner & 1) == 0 ? rect.X : rect.Right;
            double y = (corner & 2) == 0 ? rect.Y : rect.Bottom;
            for (int k = 0; k < count; k++)
            {
                int a = _chain[k];
                bool self = selfFirst && k == 0;
                if (!self && (_flags[a] & FlagScrollable) != 0)
                {
                    ScrollOffset(a, out double sx, out double sy);
                    x -= sx;
                    y -= sy;
                }

                if ((_flags[a] & FlagTransform) != 0)
                {
                    ForwardTransform(a, ref x, ref y);
                }

                if (a == 0 && _vpRange.CanScroll)
                {
                    x -= _vpRange.ClampX(_scroll!.ViewportX);
                    y -= _vpRange.ClampY(_scroll.ViewportY);
                }
            }

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private void ForwardTransform(int a, ref double x, ref double y)
    {
        if (TransformOf(a, out System.Numerics.Matrix4x4 m) && TransformGeometry.MapPoint(in m, (float)x, (float)y, out float ox, out float oy))
        {
            x = ox;
            y = oy;
        }
    }

    // ------------------------------------------------------------------ scrolling support

    /// <summary>The fragment whose clip and scroll offset apply to <paramref name="fragment"/>: its containing block (the parent, or the positioned ancestor for absolutely positioned boxes); -1 for the viewport.</summary>
    internal int ContainingOf(int fragment) => _containing[fragment];

    internal bool IsScrollContainer(int fragment) => (_flags[fragment] & FlagScroller) != 0;

    internal ScrollRange RangeOf(int fragment) => _range[fragment];

    internal ScrollRange ViewportRange => _vpRange;

    internal Rect AbsoluteBox(int fragment)
    {
        Rect bb = _tree.BorderBoxOf(fragment);
        return new Rect(_absX[fragment], _absY[fragment], bb.Width, bb.Height);
    }

    internal void EnsureBuilt()
    {
        if (_tree.Count != _built || _tree.Version != _builtVersion)
        {
            Rebuild();
        }
    }
}
