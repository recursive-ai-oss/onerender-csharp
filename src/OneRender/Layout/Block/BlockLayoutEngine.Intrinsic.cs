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
    /// do not contribute yet. Inline-level children are measured by <see cref="IInlineLayout.Measure"/>.
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
        c.Kind is BoxKind.Container or BoxKind.Replaced && (c.Flags & (BoxTraits.OutOfFlow | BoxTraits.OutsideMarker)) == 0;

    private void PushIntrinsic(Box b)
    {
        if (_isp == _istack.Length)
        {
            Array.Resize(ref _istack, _istack.Length * 2);
        }

        _istack[_isp++] = new IntrinsicFrame { Box = b.Index, Next = b.FirstChild.IsNull ? -1 : b.FirstChild.Index };
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
                if (!IsFlowChild(c))
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
                _istack[top].Min = Math.Max(_istack[top].Min, a);
                _istack[top].Max = Math.Max(_istack[top].Max, b);
                continue;
            }

            IntrinsicFrame done = _istack[top];
            _minContent[done.Box] = Math.Max(0, done.Min);
            _maxContent[done.Box] = Math.Max(0, done.Max);
            _isp--;
            if (_isp > baseSp)
            {
                Box d = _boxes.Make(done.Box);
                Contribution(d, out double a, out double b);
                int parent = _isp - 1;
                _istack[parent].Min = Math.Max(_istack[parent].Min, a);
                _istack[parent].Max = Math.Max(_istack[parent].Max, b);
            }
        }
    }

    private static bool NeedsDescent(Box b) => b.Kind == BoxKind.Container && b.ChildLayout == ChildLayout.Block;

    private void LeafIntrinsic(Box b)
    {
        double min = 0, max = 0;
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
