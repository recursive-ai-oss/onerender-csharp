namespace OneRender.Layout;

/// <summary>Kinds of track sizing function the sizing algorithm distinguishes (CSS Grid 2 §12.1).</summary>
internal enum TrackKind : byte
{
    /// <summary>A length or resolvable percentage.</summary>
    Fixed,

    /// <summary><c>auto</c>.</summary>
    Auto,

    /// <summary><c>min-content</c>.</summary>
    MinContent,

    /// <summary><c>max-content</c>.</summary>
    MaxContent,

    /// <summary>A flexible length (maximum only).</summary>
    Flex,

    /// <summary><c>fit-content()</c> (maximum only).</summary>
    FitContent,
}

/// <summary>The tracks of one axis of a grid with their sizing functions and the sizes the algorithm computes.</summary>
internal sealed class GridAxis
{
    public int Count;
    public TrackKind[] MinKind = new TrackKind[16];
    public TrackKind[] MaxKind = new TrackKind[16];
    public double[] MinPx = new double[16];
    public double[] MaxPx = new double[16];
    public double[] Flex = new double[16];
    public double[] FitPx = new double[16];
    public double[] Base = new double[16];
    public double[] Limit = new double[16];
    public double[] GapAfter = new double[16];
    public bool[] Collapsed = new bool[16];

    /// <summary>Line positions after alignment: <c>Count + 1</c> entries, the start edge of each track and the end of the last (relative to the content box).</summary>
    public double[] Start = new double[17];

    public double[] End = new double[17];

    /// <summary>The explicit tracks of the template with <c>repeat()</c> expanded, and the line names (indexes are explicit line numbers).</summary>
    public OneRender.Style.GridTrackSize[] Exp = new OneRender.Style.GridTrackSize[16];

    public int ExpCount;
    public int Explicit;
    public int Before;
    public int AutoFrom;
    public int AutoTo;
    public bool AutoFit;
    public GridNameIndex Names = new();

    public void EnsureExp(int n)
    {
        if (Exp.Length < n)
        {
            Array.Resize(ref Exp, Math.Max(n, Exp.Length * 2));
        }
    }

    public void Ensure(int n)
    {
        if (MinKind.Length >= n)
        {
            return;
        }

        int size = Math.Max(n, MinKind.Length * 2);
        Array.Resize(ref MinKind, size);
        Array.Resize(ref MaxKind, size);
        Array.Resize(ref MinPx, size);
        Array.Resize(ref MaxPx, size);
        Array.Resize(ref Flex, size);
        Array.Resize(ref FitPx, size);
        Array.Resize(ref Base, size);
        Array.Resize(ref Limit, size);
        Array.Resize(ref GapAfter, size);
        Array.Resize(ref Collapsed, size);
        Array.Resize(ref Start, size + 1);
        Array.Resize(ref End, size + 1);
    }

    /// <summary>Sum of gaps strictly between tracks <paramref name="from"/> and <paramref name="to"/> (exclusive end).</summary>
    public double GapsBetween(int from, int to)
    {
        double g = 0;
        for (int i = from; i < to - 1; i++)
        {
            g += GapAfter[i];
        }

        return g;
    }
}

/// <summary>An item as the track sizing algorithm sees it: the tracks it spans and its outer size contributions along the axis.</summary>
internal struct SizeItem
{
    public int Start;
    public int End;

    /// <summary>Min-content contribution, max-content contribution and minimum contribution (CSS Grid 2 §12.5), outer sizes in px.</summary>
    public double MinContent;
    public double MaxContent;
    public double MinContribution;
}

/// <summary>
/// The track sizing algorithm of CSS Grid 2 §12.3 to §12.8. It sees only tracks and items with precomputed contributions, so it is
/// independent of the layout engine; the caller resolves percentages and measures items.
/// </summary>
internal sealed class GridTrackSizer
{
    /// <summary>Track visits one run may spend on spanning items before the rest are ignored.</summary>
    public const long MaxWork = 100_000_000;

    private readonly double[] _empty = [];
    private double[] _planned = [];
    private double[] _inc = [];
    private bool[] _infGrow = [];
    private bool[] _frozen = [];
    private long[] _order = [];
    private long _work;

    /// <summary>True when the work limit was reached.</summary>
    public bool HitLimit;

    /// <summary>The flex fraction the last run used.</summary>
    public double FlexFraction;

    /// <summary>
    /// Sizes the tracks. <paramref name="available"/> is the available grid space (NaN when indefinite);
    /// <paramref name="mode"/> is 0 for a definite container, 1 for a min-content and 2 for a max-content constraint. The container's
    /// minimum and maximum inner size (NaN when none) bound the flex fraction and the stretch step.
    /// </summary>
    public void Size(GridAxis ax, Span<SizeItem> items, double available, int mode, double containerMin, double containerMax, bool stretchAuto)
    {
        _ = _empty;
        int n = ax.Count;
        HitLimit = false;
        _work = 0;
        if (_planned.Length < n)
        {
            int size = Math.Max(n, _planned.Length * 2);
            _planned = new double[size];
            _inc = new double[size];
            _infGrow = new bool[size];
            _frozen = new bool[size];
        }

        if (items.Length > _order.Length)
        {
            _order = new long[Math.Max(items.Length, _order.Length * 2)];
        }

        // §12.4: initialize.
        for (int t = 0; t < n; t++)
        {
            double b = ax.MinKind[t] == TrackKind.Fixed ? ax.MinPx[t] : 0;
            double l = ax.MaxKind[t] == TrackKind.Fixed ? ax.MaxPx[t] : double.PositiveInfinity;
            if (ax.Collapsed[t])
            {
                b = 0;
                l = 0;
            }

            ax.Base[t] = b;
            ax.Limit[t] = Math.Max(l, b);
            _infGrow[t] = false;
        }

        ResolveIntrinsic(ax, items, mode);
        Maximize(ax, available, mode, containerMin, containerMax);
        ExpandFlex(ax, items, available, mode, containerMin, containerMax);
        if (stretchAuto)
        {
            StretchAuto(ax, available, containerMin);
        }
    }

    // ---- §12.5 ----

    private static bool IsIntrinsicMin(TrackKind k) => k is TrackKind.Auto or TrackKind.MinContent or TrackKind.MaxContent;

    private static bool IsIntrinsicMax(TrackKind k) => k is TrackKind.Auto or TrackKind.MinContent or TrackKind.MaxContent or TrackKind.FitContent;

    private static bool SpansFlex(GridAxis ax, in SizeItem it)
    {
        for (int t = it.Start; t < it.End; t++)
        {
            if (ax.MaxKind[t] == TrackKind.Flex)
            {
                return true;
            }
        }

        return false;
    }

    private void ResolveIntrinsic(GridAxis ax, Span<SizeItem> items, int mode)
    {
        int count = 0;
        int maxSpan = 1;
        for (int i = 0; i < items.Length; i++)
        {
            ref SizeItem it = ref items[i];
            if (it.End <= it.Start || it.Start < 0 || it.End > ax.Count)
            {
                continue;
            }

            _order[count++] = ((long)(it.End - it.Start) << 32) | (uint)i;
            maxSpan = Math.Max(maxSpan, it.End - it.Start);
        }

        if (count == 0)
        {
            FinishInfinite(ax);
            return;
        }

        Array.Sort(_order, 0, count);

        // Items spanning one track.
        int p = 0;
        while (p < count && (_order[p] >> 32) == 1)
        {
            ref SizeItem it = ref items[(int)(_order[p] & 0xFFFFFFFF)];
            p++;
            int t = it.Start;
            if (ax.MaxKind[t] == TrackKind.Flex || ax.Collapsed[t] || ax.MinKind[t] == TrackKind.Fixed && ax.MaxKind[t] == TrackKind.Fixed)
            {
                continue;
            }

            switch (ax.MinKind[t])
            {
                case TrackKind.MinContent:
                    ax.Base[t] = Math.Max(ax.Base[t], Math.Max(0, it.MinContent));
                    break;
                case TrackKind.MaxContent:
                    ax.Base[t] = Math.Max(ax.Base[t], Math.Max(0, it.MaxContent));
                    break;
                case TrackKind.Auto:
                    ax.Base[t] = Math.Max(ax.Base[t], Math.Max(0, mode != 0 ? Limited(ax, in it, it.MinContent, 0) : it.MinContribution));
                    break;
            }

            switch (ax.MaxKind[t])
            {
                case TrackKind.MinContent:
                    SetLimit(ax, t, it.MinContent);
                    break;
                case TrackKind.MaxContent:
                case TrackKind.Auto:
                    SetLimit(ax, t, it.MaxContent);
                    break;
                case TrackKind.FitContent:
                    SetLimit(ax, t, Math.Min(it.MaxContent, ax.FitPx[t]));
                    break;
            }

            if (ax.Limit[t] < ax.Base[t])
            {
                ax.Limit[t] = ax.Base[t];
            }
        }

        // Items spanning several tracks, smallest spans first, not crossing a flexible track.
        while (p < count)
        {
            long span = _order[p] >> 32;
            int q = p;
            while (q < count && (_order[q] >> 32) == span)
            {
                q++;
            }

            DistributeGroup(ax, items, p, q, mode, flexOnly: false);
            p = q;
        }

        // Items crossing flexible tracks, together.
        DistributeGroup(ax, items, 0, count, mode, flexOnly: true);
        FinishInfinite(ax);
    }

    private static void SetLimit(GridAxis ax, int t, double v)
    {
        // The first item replaces the infinite initial limit; later ones raise it.
        double cur = ax.Limit[t];
        ax.Limit[t] = double.IsPositiveInfinity(cur) ? Math.Max(0, v) : Math.Max(cur, Math.Max(0, v));
    }

    private static void FinishInfinite(GridAxis ax)
    {
        for (int t = 0; t < ax.Count; t++)
        {
            if (double.IsPositiveInfinity(ax.Limit[t]))
            {
                ax.Limit[t] = ax.Base[t];
            }
        }
    }

    /// <summary>
    /// The limited min- or max-content contribution of §12.5: limited by the sum of the fixed max track sizing functions when the
    /// item spans only such tracks, and floored by the minimum contribution.
    /// </summary>
    private static double Limited(GridAxis ax, in SizeItem it, double contribution, int unused)
    {
        _ = unused;
        double cap = 0;
        for (int t = it.Start; t < it.End; t++)
        {
            TrackKind k = ax.MaxKind[t];
            if (k == TrackKind.Fixed)
            {
                cap += ax.MaxPx[t];
            }
            else if (k == TrackKind.FitContent)
            {
                cap += ax.FitPx[t];
            }
            else
            {
                return Math.Max(contribution, it.MinContribution);
            }
        }

        cap += ax.GapsBetween(it.Start, it.End);
        return Math.Max(Math.Min(contribution, cap), it.MinContribution);
    }

    private enum Phase
    {
        IntrinsicMinimums,
        ContentMinimums,
        MaxContentMinimums,
        IntrinsicMaximums,
        MaxContentMaximums,
    }

    private void DistributeGroup(GridAxis ax, Span<SizeItem> items, int from, int to, int mode, bool flexOnly)
    {
        int n = ax.Count;
        if (!flexOnly)
        {
            DoPhase(ax, items, from, to, Phase.IntrinsicMinimums, mode, false);
            DoPhase(ax, items, from, to, Phase.ContentMinimums, mode, false);
            if (mode == 2)
            {
                DoPhase(ax, items, from, to, Phase.MaxContentMinimums, mode, false);
            }

            DoPhase(ax, items, from, to, Phase.MaxContentMinimums, 3, false);
            for (int t = 0; t < n; t++)
            {
                if (ax.Limit[t] < ax.Base[t])
                {
                    ax.Limit[t] = ax.Base[t];
                }
            }

            DoPhase(ax, items, from, to, Phase.IntrinsicMaximums, mode, false);
            DoPhase(ax, items, from, to, Phase.MaxContentMaximums, mode, false);
        }
        else
        {
            DoFlexPhase(ax, items, from, to, mode);
        }
    }

    private void DoPhase(GridAxis ax, Span<SizeItem> items, int from, int to, Phase phase, int mode, bool flexOnly)
    {
        _ = flexOnly;
        int n = ax.Count;
        Array.Clear(_planned, 0, n);
        bool any = false;
        bool limits = phase is Phase.IntrinsicMaximums or Phase.MaxContentMaximums;
        for (int k = from; k < to; k++)
        {
            ref SizeItem it = ref items[(int)(_order[k] & 0xFFFFFFFF)];
            if (SpansFlex(ax, in it))
            {
                continue;
            }

            if (_work > MaxWork)
            {
                HitLimit = true;
                return;
            }

            double contribution;
            switch (phase)
            {
                case Phase.IntrinsicMinimums:
                    contribution = mode != 0 ? Limited(ax, in it, it.MinContent, 0) : it.MinContribution;
                    break;
                case Phase.ContentMinimums:
                    contribution = it.MinContent;
                    break;
                case Phase.MaxContentMinimums:
                    // mode 2 (max-content constraint): limited max-content into auto / max-content minimums;
                    // mode 3 marks the unconditional pass for max-content minimums with the plain max-content contribution.
                    contribution = mode == 3 ? it.MaxContent : Limited(ax, in it, it.MaxContent, 0);
                    break;
                case Phase.IntrinsicMaximums:
                    contribution = it.MinContent;
                    break;
                default:
                    contribution = it.MaxContent;
                    break;
            }

            if (contribution <= 0 || double.IsNaN(contribution))
            {
                continue;
            }

            if (DistributeItem(ax, in it, contribution, phase, mode))
            {
                any = true;
            }
        }

        if (!any)
        {
            return;
        }

        for (int t = 0; t < n; t++)
        {
            double inc = _planned[t];
            if (inc <= 0)
            {
                continue;
            }

            if (limits)
            {
                double cur = ax.Limit[t];
                bool wasInf = double.IsPositiveInfinity(cur);
                ax.Limit[t] = wasInf ? ax.Base[t] + inc : cur + inc;
                if (wasInf && phase == Phase.IntrinsicMaximums)
                {
                    _infGrow[t] = true;
                }
            }
            else
            {
                ax.Base[t] += inc;
            }
        }
    }

    private static bool Affected(GridAxis ax, int t, Phase phase, int mode) => phase switch
    {
        Phase.IntrinsicMinimums => IsIntrinsicMin(ax.MinKind[t]),
        Phase.ContentMinimums => ax.MinKind[t] is TrackKind.MinContent or TrackKind.MaxContent,
        Phase.MaxContentMinimums => mode == 3 ? ax.MinKind[t] == TrackKind.MaxContent : ax.MinKind[t] is TrackKind.Auto or TrackKind.MaxContent,
        Phase.IntrinsicMaximums => IsIntrinsicMax(ax.MaxKind[t]),
        _ => ax.MaxKind[t] is TrackKind.MaxContent or TrackKind.Auto or TrackKind.FitContent,
    };

    /// <summary>Distributes one item's contribution over the tracks it spans (§12.5.1). Returns whether anything was planned.</summary>
    private bool DistributeItem(GridAxis ax, in SizeItem it, double contribution, Phase phase, int mode)
    {
        bool limits = phase is Phase.IntrinsicMaximums or Phase.MaxContentMaximums;
        int s = it.Start, e = it.End;
        double sum = ax.GapsBetween(s, e);
        int affectedCount = 0;
        for (int t = s; t < e; t++)
        {
            _work++;
            double size = limits ? (double.IsPositiveInfinity(ax.Limit[t]) ? ax.Base[t] : ax.Limit[t]) : ax.Base[t];
            sum += size;
            if (!ax.Collapsed[t] && Affected(ax, t, phase, mode))
            {
                affectedCount++;
            }
        }

        double space = contribution - sum;
        if (space <= 0 || affectedCount == 0)
        {
            return false;
        }

        for (int t = s; t < e; t++)
        {
            _inc[t] = 0;
        }

        // Up to the limits: affected tracks first, then the others.
        space = FillEqually(ax, s, e, space, phase, mode, limits, affected: true);
        if (space > 1e-9 && affectedCount < e - s)
        {
            space = FillEqually(ax, s, e, space, phase, mode, limits, affected: false);
        }

        if (space > 1e-9)
        {
            // Beyond the limits: tracks with the matching max function, or all affected tracks when none match.
            int count = 0;
            for (int t = s; t < e; t++)
            {
                _work++;
                if (BeyondTarget(ax, t, phase, mode, strict: true))
                {
                    count++;
                }
            }

            bool strict = count > 0;
            if (!strict)
            {
                for (int t = s; t < e; t++)
                {
                    if (!ax.Collapsed[t] && Affected(ax, t, phase, mode))
                    {
                        count++;
                    }
                }
            }

            if (count > 0)
            {
                double each = space / count;
                for (int t = s; t < e; t++)
                {
                    if (strict ? BeyondTarget(ax, t, phase, mode, strict: true) : !ax.Collapsed[t] && Affected(ax, t, phase, mode))
                    {
                        _inc[t] += each;
                    }
                }
            }
        }

        bool anyPlanned = false;
        for (int t = s; t < e; t++)
        {
            if (_inc[t] > _planned[t])
            {
                _planned[t] = _inc[t];
            }

            anyPlanned |= _inc[t] > 0;
        }

        return anyPlanned;
    }

    private bool BeyondTarget(GridAxis ax, int t, Phase phase, int mode, bool strict)
    {
        _ = strict;
        if (ax.Collapsed[t] || !Affected(ax, t, phase, mode))
        {
            return false;
        }

        switch (phase)
        {
            case Phase.IntrinsicMinimums:
            case Phase.ContentMinimums:
            case Phase.IntrinsicMaximums:
                // Only tracks with an intrinsic max track sizing function; a fit-content() track stops at its limit.
                return IsIntrinsicMax(ax.MaxKind[t]) && !(ax.MaxKind[t] == TrackKind.FitContent && Size(ax, t, phase) >= ax.FitPx[t]);
            default:
                return ax.MaxKind[t] is TrackKind.MaxContent or TrackKind.Auto or TrackKind.FitContent && !(ax.MaxKind[t] == TrackKind.FitContent && Size(ax, t, phase) >= ax.FitPx[t]);
        }
    }

    private double Size(GridAxis ax, int t, Phase phase)
    {
        bool limits = phase is Phase.IntrinsicMaximums or Phase.MaxContentMaximums;
        double b = limits ? (double.IsPositiveInfinity(ax.Limit[t]) ? ax.Base[t] : ax.Limit[t]) : ax.Base[t];
        return b + _inc[t];
    }

    /// <summary>Water-filling: distributes space equally, freezing a track as its size reaches its limit. Returns the space left.</summary>
    private double FillEqually(GridAxis ax, int s, int e, double space, Phase phase, int mode, bool limits, bool affected)
    {
        int active = 0;
        for (int t = s; t < e; t++)
        {
            _work++;
            bool ok = !ax.Collapsed[t] && Affected(ax, t, phase, mode) == affected;
            _frozen[t] = !ok;
            if (ok)
            {
                active++;
            }
        }

        while (active > 0 && space > 1e-9)
        {
            double share = space / active;
            double used = 0;
            for (int t = s; t < e; t++)
            {
                _work++;
                if (_frozen[t])
                {
                    continue;
                }

                double room = Room(ax, t, limits);
                double take = Math.Min(share, room - _inc[t]);
                if (take > 0)
                {
                    _inc[t] += take;
                    used += take;
                }

                if (_inc[t] >= room - 1e-9)
                {
                    _frozen[t] = true;
                    active--;
                }
            }

            if (used <= 1e-12)
            {
                break;
            }

            space -= used;
        }

        return space;
    }

    /// <summary>How much a track's affected size may grow in total: up to its growth limit for base sizes, and for limits only when infinite or infinitely growable.</summary>
    private double Room(GridAxis ax, int t, bool limits)
    {
        if (!limits)
        {
            double lim = ax.Limit[t];
            if (ax.MaxKind[t] == TrackKind.FitContent)
            {
                lim = Math.Min(lim, ax.FitPx[t]);
            }

            return double.IsPositiveInfinity(lim) ? double.PositiveInfinity : Math.Max(0, lim - ax.Base[t]);
        }

        if (double.IsPositiveInfinity(ax.Limit[t]) || _infGrow[t])
        {
            if (ax.MaxKind[t] == TrackKind.FitContent)
            {
                double basis = double.IsPositiveInfinity(ax.Limit[t]) ? ax.Base[t] : ax.Limit[t];
                return Math.Max(0, ax.FitPx[t] - basis);
            }

            return double.PositiveInfinity;
        }

        return 0;
    }

    private void DoFlexPhase(GridAxis ax, Span<SizeItem> items, int from, int to, int mode)
    {
        int n = ax.Count;
        Array.Clear(_planned, 0, n);
        bool any = false;
        for (int k = from; k < to; k++)
        {
            ref SizeItem it = ref items[(int)(_order[k] & 0xFFFFFFFF)];
            if (!SpansFlex(ax, in it))
            {
                continue;
            }

            if (_work > MaxWork)
            {
                HitLimit = true;
                return;
            }

            // The minimum contribution is accommodated by the base sizes of the flexible tracks; with a max-content constraint
            // the limited max-content contribution is, too (§12.5: the flexible-track repetition of the span steps).
            double contribution = mode == 2 ? Limited(ax, in it, it.MaxContent, 0) : mode == 1 ? Limited(ax, in it, it.MinContent, 0) : it.MinContribution;
            if (contribution <= 0 || double.IsNaN(contribution))
            {
                continue;
            }

            double sum = ax.GapsBetween(it.Start, it.End);
            double flexSum = 0;
            int flexCount = 0;
            for (int t = it.Start; t < it.End; t++)
            {
                _work++;
                sum += ax.Base[t];
                if (ax.MaxKind[t] == TrackKind.Flex)
                {
                    flexSum += ax.Flex[t];
                    flexCount++;
                }
            }

            double space = contribution - sum;
            if (space <= 0)
            {
                continue;
            }

            for (int t = it.Start; t < it.End; t++)
            {
                if (ax.MaxKind[t] != TrackKind.Flex)
                {
                    continue;
                }

                double inc;
                if (flexSum >= 1)
                {
                    inc = space * ax.Flex[t] / flexSum;
                }
                else
                {
                    // Less than one fr in total: that proportion by ratio, the rest equally.
                    inc = flexSum > 0 ? space * flexSum * ax.Flex[t] / flexSum : 0;
                    inc += space * (1 - flexSum) / flexCount;
                }

                if (inc > _planned[t])
                {
                    _planned[t] = inc;
                    any = true;
                }
            }
        }

        if (!any)
        {
            return;
        }

        for (int t = 0; t < n; t++)
        {
            ax.Base[t] += _planned[t];
            if (ax.Limit[t] < ax.Base[t])
            {
                ax.Limit[t] = ax.Base[t];
            }
        }
    }

    // ---- §12.6 ----

    private void Maximize(GridAxis ax, double available, int mode, double containerMin, double containerMax)
    {
        _ = containerMin;
        int n = ax.Count;
        double free;
        if (mode == 2 || double.IsNaN(available) && mode != 1)
        {
            free = double.PositiveInfinity;
        }
        else if (mode == 1)
        {
            free = 0;
        }
        else
        {
            free = available - TotalBase(ax);
        }

        if (!(free > 0))
        {
            return;
        }

        if (double.IsPositiveInfinity(free))
        {
            // Under a max-content constraint every track grows to its growth limit; the container's maximum size bounds the total.
            double total = 0;
            for (int t = 0; t < n; t++)
            {
                total += ax.Limit[t];
            }

            total += ax.GapsBetween(0, n);
            if (!double.IsNaN(containerMax) && total > containerMax)
            {
                free = Math.Max(0, containerMax - TotalBase(ax));
            }
        }

        FillBases(ax, free);
    }

    private void FillBases(GridAxis ax, double free)
    {
        int n = ax.Count;
        int active = 0;
        for (int t = 0; t < n; t++)
        {
            _frozen[t] = ax.Collapsed[t] || ax.Base[t] >= ax.Limit[t];
            if (!_frozen[t])
            {
                active++;
            }
        }

        while (active > 0 && free > 1e-9)
        {
            double share = double.IsPositiveInfinity(free) ? double.PositiveInfinity : free / active;
            double used = 0;
            for (int t = 0; t < n; t++)
            {
                if (_frozen[t])
                {
                    continue;
                }

                double room = ax.Limit[t] - ax.Base[t];
                double take = Math.Min(share, room);
                ax.Base[t] += take;
                used += take;
                if (ax.Base[t] >= ax.Limit[t] - 1e-9)
                {
                    _frozen[t] = true;
                    active--;
                }
            }

            if (used <= 1e-12)
            {
                break;
            }

            free -= used;
        }
    }

    private static double TotalBase(GridAxis ax)
    {
        double s = 0;
        for (int t = 0; t < ax.Count; t++)
        {
            s += ax.Base[t];
        }

        return s + ax.GapsBetween(0, ax.Count);
    }

    // ---- §12.7 ----

    private void ExpandFlex(GridAxis ax, Span<SizeItem> items, double available, int mode, double containerMin, double containerMax)
    {
        int n = ax.Count;
        bool anyFlex = false;
        for (int t = 0; t < n; t++)
        {
            if (ax.MaxKind[t] == TrackKind.Flex && !ax.Collapsed[t])
            {
                anyFlex = true;
                break;
            }
        }

        FlexFraction = 0;
        if (!anyFlex)
        {
            return;
        }

        bool definite = !double.IsNaN(available) && mode == 0;
        double fr;
        if (mode == 1)
        {
            fr = 0;
        }
        else if (definite)
        {
            // Free space of zero or less: the used flex fraction is zero (§12.7).
            fr = available - TotalBase(ax) <= 0 ? 0 : FindFr(ax, 0, n, available, whole: true);
        }
        else
        {
            fr = 0;
            for (int t = 0; t < n; t++)
            {
                if (ax.MaxKind[t] == TrackKind.Flex && !ax.Collapsed[t])
                {
                    double v = ax.Flex[t] > 1 ? ax.Base[t] / ax.Flex[t] : ax.Base[t];
                    fr = Math.Max(fr, v);
                }
            }

            for (int i = 0; i < items.Length; i++)
            {
                ref SizeItem it = ref items[i];
                if (it.End <= it.Start || it.Start < 0 || it.End > n || !SpansFlex(ax, in it))
                {
                    continue;
                }

                if (_work++ > MaxWork)
                {
                    HitLimit = true;
                    break;
                }

                fr = Math.Max(fr, FindFr(ax, it.Start, it.End, it.MaxContent, whole: false));
            }

            // If this makes the grid smaller than the container's minimum or larger than its maximum, redo with that size.
            double total = 0;
            for (int t = 0; t < n; t++)
            {
                total += ax.MaxKind[t] == TrackKind.Flex && !ax.Collapsed[t] ? Math.Max(ax.Base[t], fr * ax.Flex[t]) : ax.Base[t];
            }

            total += ax.GapsBetween(0, n);
            if (!double.IsNaN(containerMin) && total < containerMin)
            {
                fr = FindFr(ax, 0, n, containerMin, whole: true);
            }
            else if (!double.IsNaN(containerMax) && total > containerMax)
            {
                fr = FindFr(ax, 0, n, containerMax, whole: true);
            }
        }

        FlexFraction = fr;
        for (int t = 0; t < n; t++)
        {
            if (ax.MaxKind[t] == TrackKind.Flex && !ax.Collapsed[t])
            {
                double v = fr * ax.Flex[t];
                if (v > ax.Base[t])
                {
                    ax.Base[t] = v;
                }

                if (ax.Limit[t] < ax.Base[t])
                {
                    ax.Limit[t] = ax.Base[t];
                }
            }
        }
    }

    /// <summary>§12.7.1: the largest size of an fr that does not exceed <paramref name="space"/> across tracks [from, to).</summary>
    private double FindFr(GridAxis ax, int from, int to, double space, bool whole)
    {
        _ = whole;
        for (int t = from; t < to; t++)
        {
            _frozen[t] = ax.MaxKind[t] != TrackKind.Flex || ax.Collapsed[t];
        }

        double gaps = ax.GapsBetween(from, to);
        for (int guard = 0; guard <= to - from + 1; guard++)
        {
            double leftover = space - gaps;
            double flexSum = 0;
            for (int t = from; t < to; t++)
            {
                if (_frozen[t])
                {
                    leftover -= ax.Base[t];
                }
                else
                {
                    flexSum += ax.Flex[t];
                }
            }

            if (flexSum < 1)
            {
                flexSum = 1;
            }

            double hyp = Math.Max(0, leftover) / flexSum;
            bool restart = false;
            for (int t = from; t < to; t++)
            {
                if (!_frozen[t] && hyp * ax.Flex[t] < ax.Base[t])
                {
                    _frozen[t] = true;
                    restart = true;
                }
            }

            if (!restart)
            {
                return hyp;
            }
        }

        return 0;
    }

    // ---- §12.8 ----

    internal static void StretchAuto(GridAxis ax, double available, double containerMin)
    {
        int n = ax.Count;
        double space = double.IsNaN(available) ? containerMin : available;
        if (double.IsNaN(space))
        {
            return;
        }

        double free = space - TotalBase(ax);
        if (!(free > 1e-9))
        {
            return;
        }

        int count = 0;
        for (int t = 0; t < n; t++)
        {
            if (ax.MaxKind[t] == TrackKind.Auto && !ax.Collapsed[t])
            {
                count++;
            }
        }

        if (count == 0)
        {
            return;
        }

        double each = free / count;
        for (int t = 0; t < n; t++)
        {
            if (ax.MaxKind[t] == TrackKind.Auto && !ax.Collapsed[t])
            {
                ax.Base[t] += each;
            }
        }
    }
}
