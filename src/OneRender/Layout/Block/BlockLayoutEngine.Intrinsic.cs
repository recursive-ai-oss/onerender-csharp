using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    private double[] _minContent = [];
    private double[] _maxContent = [];
    private IntrinsicFrame[] _istack = new IntrinsicFrame[16];
    private int _isp;

    private struct IntrinsicFrame
    {
        public int Box;
        public int Next;
        public double Min;
        public double Max;

        // Sum of the max-content contributions of the floats since the last clear.
        public double FloatRun;

        // Flex containers (Flexbox §9.9): 0 for a block container, else the kind of flex container, with the gap and the item count.
        public byte Flex;
        public int Count;
        public double Gap;
    }

    private int _atomicMeasureDepth;

    /// <inheritdoc />
    Fragment IAtomicInlineHost.LayoutAtomic(Box box, Fragment parent, double containingWidth, out Insets margins) =>
        LayoutIndependent(box, parent, containingWidth, double.NaN, true, out margins);

    /// <inheritdoc />
    void IAtomicInlineHost.GetAtomicContribution(Box box, out double minContent, out double maxContent)
    {
        // Atomic boxes nest (an inline-block holding text holding an inline-block ...); the measuring recursion is cut at a fixed
        // depth so a hostile tree cannot reach the call stack. Past it an atomic box contributes nothing.
        if (_atomicMeasureDepth >= MaxReentrancy || box.IsNull || !ReferenceEquals(box.Tree, _boxes))
        {
            minContent = maxContent = 0;
            return;
        }

        _atomicMeasureDepth++;
        try
        {
            GetIntrinsic(box, out _, out _);
            Contribution(box, out minContent, out maxContent);
        }
        finally
        {
            _atomicMeasureDepth--;
        }
    }

    /// <inheritdoc />
    void IAtomicInlineHost.QueueAbsolute(Box box, int containingFragment, double staticStart, double staticEnd, double staticTop)
    {
        if (_sp == 0 || _absCount >= MaxAbsolute || box.IsNull)
        {
            return;
        }

        Enqueue(in _frames[_sp - 1], box, staticStart, staticEnd, staticTop);
        if (containingFragment >= 0 && box.Values.Position != PositionKind.Fixed)
        {
            _abs[_absCount - 1].Cb = containingFragment;
        }
    }

    private void PrepareIntrinsicCache(int boxCount)
    {
        if (_minContent.Length < boxCount)
        {
            int size = Math.Max(boxCount, _minContent.Length * 2);
            _minContent = new double[size];
            _maxContent = new double[size];
        }

        Array.Fill(_minContent, double.NaN, 0, boxCount);
        Array.Fill(_maxContent, double.NaN, 0, boxCount);
        _isp = 0;
    }

    /// <summary>
    /// The intrinsic inline sizes of a box's content (CSS Sizing 3 §5): the content-box width it needs when it breaks at every
    /// opportunity (min-content) and when it never breaks (max-content). The sizes are cached per box until the next
    /// <see cref="Layout"/>, so shrink-to-fit, flex and grid can ask repeatedly. Percentages inside the box count as auto; floats
    /// contribute as CSS Sizing 3 §5.2 describes: the min-content size is at least that of the widest float, and consecutive floats (not separated by a <c>clear</c>) add up in the max-content size. Inline-level children are measured by <see cref="IInlineLayout.Measure"/>.
    /// </summary>
    /// <param name="box">A box of the tree last laid out.</param>
    /// <param name="minContent">Receives the min-content width in px.</param>
    /// <param name="maxContent">Receives the max-content width in px.</param>
    public void GetIntrinsicSizes(Box box, out double minContent, out double maxContent)
    {
        if (box.IsNull || !ReferenceEquals(box.Tree, _boxes))
        {
            throw new ArgumentException("The box does not belong to the box tree last laid out.", nameof(box));
        }

        GetIntrinsic(box, out minContent, out maxContent);
    }

    private void GetIntrinsic(Box box, out double min, out double max)
    {
        if (double.IsNaN(_minContent[box.Index]))
        {
            ComputeIntrinsic(box);
        }

        min = _minContent[box.Index];
        max = _maxContent[box.Index];
    }

    private static bool IsFlowChild(Box c) =>
        c.Kind is BoxKind.Container or BoxKind.Replaced && ((c.Flags & (BoxTraits.OutOfFlow | BoxTraits.OutsideMarker)) == 0 || IsFloat(c));

    private static bool IsFloat(Box c) =>
        c.Kind is BoxKind.Container or BoxKind.Replaced && (c.Flags & BoxTraits.OutOfFlow) != 0 && c.Values.Floating != FloatKind.None;

    /// <summary>Adds a child's contribution to its parent's running sizes (CSS Sizing 3 §5.1 for blocks, §5.2 for floats).</summary>
    private static void Merge(ref IntrinsicFrame parent, Box child, double minC, double maxC)
    {
        if (child.Values.Clear != ClearKind.None)
        {
            parent.Max = Math.Max(parent.Max, parent.FloatRun);
            parent.FloatRun = 0;
        }

        parent.Min = Math.Max(parent.Min, minC);
        if (IsFloat(child))
        {
            parent.FloatRun += maxC;
        }
        else
        {
            parent.Max = Math.Max(parent.Max, maxC);
        }
    }

    private void PushIntrinsic(Box b)
    {
        if (_isp == _istack.Length)
        {
            Array.Resize(ref _istack, _istack.Length * 2);
        }

        _istack[_isp++] = new IntrinsicFrame { Box = b.Index, Next = b.FirstChild.IsNull ? -1 : b.FirstChild.Index, Flex = FlexIntrinsicKind(b), Gap = FlexIntrinsicGap(b) };
    }

    /// <summary>Post-order walk with an explicit stack, so the depth of the tree never reaches the call stack.</summary>
    private void ComputeIntrinsic(Box root)
    {
        int baseSp = _isp;
        if (!NeedsDescent(root))
        {
            LeafIntrinsic(root);
            return;
        }

        PushIntrinsic(root);
        while (_isp > baseSp)
        {
            if ((++_counter & 255) == 0)
            {
                _token.ThrowIfCancellationRequested();
            }

            int top = _isp - 1;
            int ci = _istack[top].Next;
            if (ci >= 0)
            {
                Box c = _boxes.Make(ci);
                _istack[top].Next = c.NextSibling.IsNull ? -1 : c.NextSibling.Index;
                bool flexParent = _istack[top].Flex != 0;
                if (!(flexParent ? IsFlexItemBox(c) : IsFlowChild(c)))
                {
                    continue;
                }

                if (double.IsNaN(_minContent[ci]))
                {
                    if (NeedsDescent(c))
                    {
                        PushIntrinsic(c);
                        continue;
                    }

                    LeafIntrinsic(c);
                }

                Contribution(c, out double a, out double b);
                if (flexParent)
                {
                    MergeFlex(ref _istack[top], c, a, b);
                }
                else
                {
                    Merge(ref _istack[top], c, a, b);
                }

                continue;
            }

            IntrinsicFrame done = _istack[top];
            if (done.Flex != 0)
            {
                FinishFlexIntrinsic(ref done);
            }

            double dmin = Math.Max(0, done.Min), dmax = Math.Max(0, Math.Max(done.Max, done.FloatRun));
            MulticolIntrinsic(_boxes.Make(done.Box), ref dmin, ref dmax);
            _minContent[done.Box] = dmin;
            _maxContent[done.Box] = dmax;
            _isp--;
            if (_isp > baseSp)
            {
                Box d = _boxes.Make(done.Box);
                Contribution(d, out double a, out double b);
                if (_istack[_isp - 1].Flex != 0)
                {
                    MergeFlex(ref _istack[_isp - 1], d, a, b);
                }
                else
                {
                    Merge(ref _istack[_isp - 1], d, a, b);
                }
            }
        }
    }

    private static bool NeedsDescent(Box b) => b.Kind == BoxKind.Container && b.ChildLayout == ChildLayout.Block && b.Context != FormattingContext.Grid;

    private void LeafIntrinsic(Box b)
    {
        double min = 0, max = 0;
        if (b.Kind == BoxKind.Container && b.Context == FormattingContext.Grid && GridIntrinsicSizes(b, out min, out max))
        {
            _minContent[b.Index] = min;
            _maxContent[b.Index] = max;
            return;
        }

        if (b.Kind == BoxKind.Replaced)
        {
            ref readonly ComputedValues v = ref b.Values;
            double ph = FixedPadding(in v);
            ReplacedContentSize(b, in v, double.NaN, double.NaN, ph, 0, double.NaN, out double w, out _);
            min = max = w;
            if (v.Width.IsPercentage)
            {
                // CSS Sizing 3 §5.2.2: a percentage width on a replaced box is zero for its min-content contribution.
                min = 0;
            }
        }
        else if (b.Kind == BoxKind.Container && b.ChildLayout == ChildLayout.Inline)
        {
            _inline.Measure(b, _fonts, out min, out max);
            if (double.IsNaN(min) || min < 0)
            {
                min = 0;
            }

            if (double.IsNaN(max) || max < min)
            {
                max = min;
            }
        }

        MulticolIntrinsic(b, ref min, ref max);
        _minContent[b.Index] = min;
        _maxContent[b.Index] = max;
    }

    private static double FixedPadding(in ComputedValues v) =>
        (v.PaddingLeft.IsPx ? Math.Max(0, v.PaddingLeft.Value) : 0)
        + (v.PaddingRight.IsPx ? Math.Max(0, v.PaddingRight.Value) : 0)
        + v.BorderLeftWidth + v.BorderRightWidth;

    /// <summary>
    /// CSS Sizing 3 §5.1: a block-level child contributes its own intrinsic size, or its definite width, plus its margin, border
    /// and padding. Percent margins and paddings and auto margins count as zero.
    /// </summary>
    private void Contribution(Box c, out double minC, out double maxC)
    {
        ref readonly ComputedValues v = ref c.Values;
        double a = _minContent[c.Index], b = _maxContent[c.Index];
        double ph = FixedPadding(in v);
        bool borderBox = v.BoxSizing == BoxSizingKind.BorderBox;
        Length w = v.Width;
        switch (w.Kind)
        {
            case LengthKind.Px:
                a = b = Math.Max(0, borderBox ? w.Value - ph : w.Value);
                break;
            case LengthKind.MinContent:
                b = a;
                break;
            case LengthKind.MaxContent:
                a = b;
                break;
        }

        if (v.MaxWidth.IsPx)
        {
            double mx = Math.Max(0, borderBox ? v.MaxWidth.Value - ph : v.MaxWidth.Value);
            a = Math.Min(a, mx);
            b = Math.Min(b, mx);
        }

        if (v.MinWidth.IsPx)
        {
            double mn = Math.Max(0, borderBox ? v.MinWidth.Value - ph : v.MinWidth.Value);
            a = Math.Max(a, mn);
            b = Math.Max(b, mn);
        }

        double edges = ph + (v.MarginLeft.IsPx ? v.MarginLeft.Value : 0) + (v.MarginRight.IsPx ? v.MarginRight.Value : 0);
        minC = a + edges;
        maxC = b + edges;
    }
}
