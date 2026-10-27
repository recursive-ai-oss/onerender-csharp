using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    /// <summary>The most columns a multi-column container uses in one row (Multicol 1 §3.4 lets a user agent cap the count).</summary>
    internal const int MaxColumns = 10_000;

    private readonly Fragmenter _frag = new();

    /// <summary>
    /// Multicol 1 §3.4: the used column count and width for a container whose content box is <paramref name="available"/> wide.
    /// <c>column-gap: normal</c> is 1em (§4.1).
    /// </summary>
    internal static void ResolveColumns(in ComputedValues v, double available, out double colWidth, out double gap, out int count)
    {
        Length g = v.ColumnGap;
        gap = g.Kind switch
        {
            LengthKind.Normal => v.FontSize,
            LengthKind.Px or LengthKind.Percent or LengthKind.Calc => g.Resolve(available),
            _ => 0,
        };
        if (double.IsNaN(gap) || gap < 0)
        {
            gap = 0;
        }

        double w = Math.Max(0, available);
        int n;
        if (v.ColumnWidth.Kind != LengthKind.Auto)
        {
            double cw = Math.Max(0, v.ColumnWidth.Value);
            double denom = cw + gap;
            double fit = denom > 0 ? Math.Floor((w + gap) / denom) : MaxColumns;
            n = (int)Math.Clamp(fit, 1, MaxColumns);
            if (v.ColumnCount > 0)
            {
                n = Math.Min(n, v.ColumnCount);
            }
        }
        else
        {
            n = Math.Clamp(v.ColumnCount, 1, MaxColumns);
        }

        count = n;
        colWidth = Math.Max(0, (w - ((n - 1) * gap)) / n);
    }

    private static void MulticolOpen(ref Frame nf, in ComputedValues v, double contentW)
    {
        ResolveColumns(in v, contentW, out double colW, out double gap, out int count);
        nf.Flags |= FrameFlags.Multicol;
        nf.McFullW = contentW;
        nf.McColW = colW;
        nf.McGap = gap;
        nf.McCount = count;
        nf.ContentWidth = colW;
    }

    /// <summary>
    /// Closes a multi-column container: plans its columns from the strip laid out at column width, and returns the content height.
    /// The fragment tree is rewritten after the whole layout is done (see <see cref="ApplyColumns"/>).
    /// </summary>
    private double MulticolClose(int ti)
    {
        ref Frame t = ref _frames[ti];
        double strip = t.Cursor + _pos + _neg;
        _pos = _neg = 0;
        if (_bsp > 0)
        {
            strip = Math.Max(strip, _bfcs[_bsp - 1].MaxBottom);
        }

        strip = Math.Max(0, strip);
        ref readonly ComputedValues v = ref _boxes.Make(t.Box).Values;
        double height = t.SpecH;
        if (double.IsNaN(height) && !double.IsInfinity(t.MaxH))
        {
            height = t.MaxH;
        }

        bool rtl = (t.Flags & FrameFlags.Rtl) != 0;
        var input = new PlanInput
        {
            Frag = t.Frag,
            ContentLeft = t.ContentLeft,
            ContentTop = t.ContentTop,
            ColW = t.McColW,
            Stride = t.McColW + t.McGap,
            ContentW = t.McFullW,
            Rtl = rtl,
            Columns = t.McCount,
            Balance = v.ColumnFill == ColumnFillKind.Balance,
            Height = height,
            StripH = strip,
        };
        double outH = _frag.Plan(_out, in input, _token);
        PlanRecord plan = _frag.PlanAt(_frag.PlanCount - 1);
        double contentH = !double.IsNaN(t.SpecH) ? t.SpecH : Math.Max(t.MinH, Math.Min(t.MaxH, outH));

        // The strip is as tall as all the columns together; the container's overflow is its columns, which can stick out sideways.
        t.OvT = 0;
        t.OvB = 0;
        t.OvL = 0;
        t.OvR = t.W;
        int cols = Math.Max(plan.Columns, 1);
        if (rtl)
        {
            t.OvL = Math.Min(0, t.ContentLeft + t.McFullW - t.McColW - ((cols - 1) * (t.McColW + t.McGap)));
        }
        else
        {
            t.OvR = Math.Max(t.W, t.ContentLeft + ((cols - 1) * (t.McColW + t.McGap)) + t.McColW);
        }

        return contentH;
    }

    private void ApplyColumns()
    {
        for (int i = 0; i < _frag.PlanCount; i++)
        {
            _frag.Apply(_out, i, _token);
        }

        if (_frag.PieceLimitHit)
        {
            _boxes.Styled.Diagnostics.Add(DiagnosticKind.LimitExceeded, "too many column fragments; some boxes are kept whole in one column");
            _frag.ClearLimitFlag();
        }
    }

    /// <summary>
    /// Multicol 1 sizes (no section covers them in level 1): the content of a multi-column container is laid out in columns, so its
    /// min-content width is the number of columns times the content's min-content width plus the gaps, and its max-content width
    /// the same with the max-content width. Without a column count the container is one column wide at its narrowest, and without
    /// a column width a column is as wide as the content needs.
    /// </summary>
    private static void MulticolIntrinsic(Box b, ref double min, ref double max)
    {
        ref readonly ComputedValues v = ref b.Values;
        if (!v.IsMulticol || b.Context is not (FormattingContext.Block or FormattingContext.FlowRoot))
        {
            return;
        }

        double gap = v.ColumnGap.Kind switch
        {
            LengthKind.Normal => v.FontSize,
            LengthKind.Px => v.ColumnGap.Value,
            _ => 0,
        };
        gap = Math.Max(0, gap);
        double cw = v.ColumnWidth.Kind == LengthKind.Auto ? 0 : Math.Max(0, v.ColumnWidth.Value);
        int n = Math.Max(1, v.ColumnCount);
        double colMin = Math.Max(min, cw);
        double colMax = Math.Max(max, cw);
        min = (n * colMin) + ((n - 1) * gap);
        max = (n * colMax) + ((n - 1) * gap);
    }
}
