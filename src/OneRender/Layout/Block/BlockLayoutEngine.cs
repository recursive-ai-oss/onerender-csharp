using System.Threading;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

/// <summary>
/// Block layout: the block formatting context of CSS2 §9.4.1, with the widths, heights and margin collapsing of CSS2 §8.3.1,
/// §10.3, §10.4, §10.5, §10.6 and §10.7, and the intrinsic sizes of CSS Sizing 3 §5.
/// </summary>
/// <remarks>
/// <para>
/// Every box is laid out as a block-level box in normal flow. Inline content goes through <see cref="IInlineLayout"/>.
/// Floats, absolutely positioned boxes and out-of-flow boxes in general get no fragment yet, flex, grid and table containers are laid
/// out as plain block containers, and <c>clear</c> is ignored; those arrive with their own layout.
/// </para>
/// <para>
/// The engine keeps its working storage between runs, so laying out an unchanged tree again allocates nothing. An
/// instance is not thread-safe; use one per thread.
/// </para>
/// </remarks>
public sealed partial class BlockLayoutEngine : ILayoutEngine, IAtomicInlineHost
{
    private const int MaxDepth = 1 << 18;
    private const int MaxReentrancy = 32;

    private readonly IInlineLayout _inline;
    private Frame[] _frames = new Frame[64];
    private int _sp;
    private double _pos;
    private double _neg;
    private FragmentTree _out = null!;
    private BoxTree _boxes = null!;
    private IFontProvider _fonts = null!;
    private CancellationToken _token;
    private int _counter;
    private int _depth;
    private bool _depthReported;
    private bool _shrinkNext;

    /// <summary>Creates a block layout that uses <see cref="InlineLayoutEngine"/> for inline content.</summary>
    public BlockLayoutEngine()
        : this(new InlineLayoutEngine())
    {
    }

    /// <summary>Creates a block layout.</summary>
    /// <param name="inlineLayout">Lays out the inline-level children of block containers.</param>
    public BlockLayoutEngine(IInlineLayout inlineLayout)
    {
        ArgumentNullException.ThrowIfNull(inlineLayout);
        _inline = inlineLayout;
        if (inlineLayout is IAtomicInlineHostAware aware)
        {
            aware.SetHost(this);
        }
    }

    [Flags]
    private enum FrameFlags : byte
    {
        None = 0,
        TopPending = 1,
        Bfc = 2,
        HasContent = 4,
        Rtl = 8,
        AnyChild = 16,
        ScrollContainer = 32,
        Marker = 64,
    }

    private struct Frame
    {
        public int Box;
        public int Frag;
        public int NextChild;
        public FrameFlags Flags;

        // Border-box geometry relative to the parent fragment's border box. Y is final once TopPending is clear.
        public double X;
        public double Y;
        public double W;

        public double ContentLeft;
        public double ContentTop;
        public double BottomEdge;
        public double ContentWidth;
        public double MarginBottom;
        public double MarginTop;
        public double MarginLeft;
        public double MarginRight;

        // Content-box heights: specified (NaN when auto), minimum, maximum, and the height an aspect ratio asks for (NaN when none).
        public double SpecH;
        public double MinH;
        public double MaxH;
        public double RatioH;

        // Offset of the end of the last in-flow child's border box from the content-box top.
        public double Cursor;
        public double FirstBaseline;

        // Content-box origin in the block formatting context root's content box.
        public double BfcLeft;
        public double BfcTop;

        // Scrollable overflow so far, in this box's border-box coordinates.
        public double OvL;
        public double OvT;
        public double OvR;
        public double OvB;
    }

    /// <inheritdoc />
    public void Layout(BoxTree boxes, in LayoutInput input, FragmentTree output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input.Fonts);
        if (!ReferenceEquals(output.Boxes, boxes))
        {
            throw new ArgumentException("The fragment tree belongs to another box tree.", nameof(output));
        }

        _out = output;
        _boxes = boxes;
        _fonts = input.Fonts;
        _token = cancellationToken;
        _counter = 0;
        _depth = 0;
        _depthReported = false;
        _sp = 0;
        _pos = _neg = 0;
        PrepareIntrinsicCache(boxes.Count);
        output.Clear();
        if (boxes.Root.IsNull)
        {
            return;
        }

        _shrinkNext = false;
        RunIndependent(boxes.Root, default, input.AvailableWidth, input.AvailableHeight, out _);
    }

    /// <summary>
    /// Lays out one box and its subtree as the root of its own block formatting context and returns its fragment. Inline layout
    /// uses this for inline-block and other atomic inline-level boxes. The fragment is added under <paramref name="parent"/> at
    /// the position its margins give (x = left margin, y = top margin); the caller moves it with
    /// <see cref="FragmentTree.SetBorderBox"/>. May be called while a layout is running (it nests).
    /// </summary>
    /// <param name="box">A container or replaced box.</param>
    /// <param name="parent">The fragment to add to; the null fragment for the root.</param>
    /// <param name="containingWidth">The width in px percentages and <c>auto</c> resolve against.</param>
    /// <param name="containingHeight">The containing block's height in px for percentage heights, or NaN when it depends on content.</param>
    /// <param name="shrinkToFit">When true an <c>auto</c> width is shrink-to-fit (CSS2 §10.3.5) instead of filling the containing block.</param>
    /// <param name="margins">Receives the used margins (top, right, bottom, left) in px.</param>
    /// <returns>The box's fragment, or the null fragment when nothing was laid out (depth limit).</returns>
    public Fragment LayoutIndependent(Box box, Fragment parent, double containingWidth, double containingHeight, bool shrinkToFit, out Insets margins)
    {
        if (box.IsNull || !ReferenceEquals(box.Tree, _boxes))
        {
            throw new ArgumentException("The box does not belong to the box tree being laid out.", nameof(box));
        }

        _shrinkNext = shrinkToFit;
        return RunIndependent(box, parent, containingWidth, containingHeight, out margins);
    }

    private Fragment RunIndependent(Box box, Fragment parent, double containingWidth, double containingHeight, out Insets margins)
    {
        margins = default;
        if (_depth >= MaxReentrancy)
        {
            return default;
        }

        _depth++;
        double savedPos = _pos, savedNeg = _neg;
        _pos = _neg = 0;
        int baseSp = _sp;
        PushFrame(new Frame
        {
            Box = -1,
            Frag = parent.IsNull ? -1 : parent.Index,
            NextChild = -1,
            Flags = FrameFlags.Bfc,
            ContentWidth = containingWidth,
            SpecH = containingHeight,
            MinH = 0,
            MaxH = double.PositiveInfinity,
            RatioH = double.NaN,
            FirstBaseline = double.NaN,
        });
        _rootParent = parent;
        _lastFragment = default;
        _lastMargins = default;
        OpenChild(baseSp, box);
        while (_sp > baseSp + 1)
        {
            int top = _sp - 1;
            ref Frame f = ref _frames[top];
            int c = f.NextChild;
            if (c < 0)
            {
                CloseTop();
                continue;
            }

            if ((++_counter & 255) == 0)
            {
                _token.ThrowIfCancellationRequested();
            }

            Box child = _boxes.Make(c);
            f.NextChild = child.NextSibling.IsNull ? -1 : child.NextSibling.Index;
            OpenChild(top, child);
        }

        Fragment result = _lastFragment;
        margins = _lastMargins;
        _sp = baseSp;
        _pos = savedPos;
        _neg = savedNeg;
        _depth--;
        return result;
    }

    private Fragment _rootParent;
    private Fragment _lastFragment;
    private Insets _lastMargins;

    private void PushFrame(in Frame frame)
    {
        if (_sp == _frames.Length)
        {
            Array.Resize(ref _frames, _frames.Length * 2);
        }

        _frames[_sp++] = frame;
    }

    private void StrutAdd(double margin)
    {
        if (margin > _pos)
        {
            _pos = margin;
        }
        else if (margin < _neg)
        {
            _neg = margin;
        }
    }

    /// <summary>
    /// CSS2 §8.3.1: ends the run of adjoining margins and returns the offset it adds before the next border edge. When the
    /// current frame (and the frames above its first non-pending ancestor) were still waiting for their top margin to be settled,
    /// the margin moves them instead and the returned offset is 0: the margin of a box and of its first in-flow child are one margin.
    /// </summary>
    private double ResolveStrut()
    {
        double s = _pos + _neg;
        _pos = _neg = 0;
        int i = _sp - 1;
        if ((_frames[i].Flags & FrameFlags.TopPending) == 0)
        {
            return s;
        }

        while (i > 0 && (_frames[i].Flags & FrameFlags.TopPending) != 0)
        {
            _frames[i].Flags &= ~FrameFlags.TopPending;
            i--;
        }

        // Frame i is the nearest ancestor with a settled top; its child i + 1 starts the pending chain.
        for (int j = i + 1; j < _sp; j++)
        {
            ref Frame q = ref _frames[j];
            ref Frame pp = ref _frames[j - 1];
            q.Y = j == i + 1 ? pp.ContentTop + pp.Cursor + s : pp.ContentTop + pp.Cursor;
            if ((q.Flags & FrameFlags.Bfc) == 0)
            {
                q.BfcTop = pp.BfcTop + (q.Y - pp.ContentTop) + q.ContentTop;
                q.BfcLeft = pp.BfcLeft + (q.X - pp.ContentLeft) + q.ContentLeft;
            }
        }

        ref Frame outer = ref _frames[i + 1];
        _out.SetBorderBox(new Fragment(_out, outer.Frag), new Rect(outer.X, outer.Y, outer.W, 0));
        return 0;
    }
}
