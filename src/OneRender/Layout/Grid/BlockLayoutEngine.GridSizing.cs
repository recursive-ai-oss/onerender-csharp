using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    // ---- columns ----

    private void SizeAllColumns(GridLevel L)
    {
        bool intrinsicOnly = _gridIntrinsicMode != 0;
        for (int k = 0; k < L.CtxCount; k++)
        {
            if ((++_counter & 63) == 0)
            {
                _token.ThrowIfCancellationRequested();
            }

            if (intrinsicOnly && k > 0)
            {
                break;
            }

            if (!L.Ctx[k].Sub0)
            {
                double avail = _gridIntrinsicMode != 0 ? double.NaN : L.Ctx[k].CW;
                SizeAxisFor(L, k, 0, avail, _gridIntrinsicMode, double.NaN, double.NaN);
            }

            if (intrinsicOnly)
            {
                break;
            }

            ref GCtx cx = ref L.Ctx[k];
            for (int i = 0; i < cx.ItemCount; i++)
            {
                ResolveInline(L, cx.FirstItem + i);
            }
        }
    }

    // ---- rows ----

    private void SizeAllRows(GridLevel L)
    {
        if (_gridIntrinsicMode != 0)
        {
            return;
        }

        for (int k = L.CtxCount - 1; k >= 0; k--)
        {
            if ((++_counter & 63) == 0)
            {
                _token.ThrowIfCancellationRequested();
            }

            if (L.Ctx[k].Sub1)
            {
                continue;
            }

            SizeRowsOf(L, k, definiteHeight: false);
        }
    }

    private void SizeRowsOf(GridLevel L, int k, bool definiteHeight)
    {
        ref GCtx cx = ref L.Ctx[k];
        Box b = _boxes.Make(cx.Box);
        ref readonly ComputedValues v = ref b.Values;
        if (k > 0 && !definiteHeight)
        {
            // A nested subgrid's own height: its box width is known, its height is `height` or its rows' total.
            double pv = cx.BpT + cx.BpB;
            ResolveHeights(in v, double.NaN, pv, 0, 0, cx.CW + cx.BpL + cx.BpR, cx.CW, out double specH, out double minH, out double maxH, out _);
            cx.CH = specH;
            cx.MinCH = minH;
            cx.MaxCH = maxH;
        }

        double min = cx.MinCH > 0 ? cx.MinCH : double.NaN;
        double max = double.IsPositiveInfinity(cx.MaxCH) ? double.NaN : cx.MaxCH;
        double avail = cx.CH;
        bool hasPercent = false;
        GridTrackList list = v.GridTemplateRows;
        if (double.IsNaN(avail))
        {
            hasPercent = HasPercentTrack(list, v.GridAutoRows);
        }

        int mode = double.IsNaN(avail) ? 2 : 0;
        SizeAxisFor(L, k, 1, avail, mode, min, max);
        GridAxis ax = L.AxisOf(k, 1);
        double total = ax.Count == 0 ? 0 : ax.End[ax.Count - 1];
        if (double.IsNaN(cx.CH))
        {
            double h = total;
            if (hasPercent)
            {
                // Percentages in row sizes were treated as auto to find the height; now they resolve against it (Grid 2 §7.2.1).
                h = Math.Max(min is var m && !double.IsNaN(m) ? m : 0, Math.Min(double.IsNaN(max) ? double.PositiveInfinity : max, h));
                SizeAxisFor(L, k, 1, h, 0, min, max);
                total = ax.Count == 0 ? 0 : ax.End[ax.Count - 1];
            }

            cx.SizedH = Math.Max(double.IsNaN(min) ? 0 : min, Math.Min(double.IsNaN(max) ? double.PositiveInfinity : max, total));
            cx.CH = cx.SizedH;
            if (!hasPercent && Math.Abs(cx.SizedH - total) > 1e-6)
            {
                // The container was clamped by min-/max-height: align the rows inside the clamped size.
                AlignTracks(ax, in v, 1, cx.SizedH, false);
            }
        }
        else
        {
            cx.SizedH = cx.CH;
        }
    }

    private static bool HasPercentTrack(GridTrackList list, GridTrackSize[] auto)
    {
        static bool P(in GridTrackSize t) =>
            t.Min.Kind == GridBreadthKind.Length && t.Min.Length.IsPercentage || t.Max.Kind == GridBreadthKind.Length && t.Max.Length.IsPercentage
            || t.Max.Kind == GridBreadthKind.FitContent && t.Max.Length.IsPercentage;

        foreach (GridTrackSize t in list.Head)
        {
            if (P(in t))
            {
                return true;
            }
        }

        foreach (GridTrackSize t in list.Auto)
        {
            if (P(in t))
            {
                return true;
            }
        }

        foreach (GridTrackSize t in list.Tail)
        {
            if (P(in t))
            {
                return true;
            }
        }

        foreach (GridTrackSize t in auto)
        {
            if (P(in t))
            {
                return true;
            }
        }

        return false;
    }

    // ---- one axis ----

    private void SizeAxisFor(GridLevel L, int k, int a, double available, int mode, double containerMin, double containerMax)
    {
        ref GCtx cx = ref L.Ctx[k];
        Box b = _boxes.Make(cx.Box);
        ref readonly ComputedValues v = ref b.Values;
        GridAxis ax = L.AxisOf(k, a);
        int n = a == 0 ? cx.Count0 : cx.Count1;
        ax.Count = n;
        ax.Ensure(n + 1);
        int leaves = GatherLeaves(L, k, a);
        BuildTracks(L, k, a, available, leaves);
        if (a == 1)
        {
            MeasureRowItems(L, k, leaves, n);
        }

        Span<SizeItem> items = L.Size.AsSpan(0, leaves);
        for (int i = 0; i < leaves; i++)
        {
            if ((i & 255) == 255)
            {
                _token.ThrowIfCancellationRequested();
            }

            ref SizeItem si = ref L.Size[i];
            int ii = L.SizeMap[i];
            if (a == 0)
            {
                ColumnContribution(L, ii, ref si, ax);
            }
            else
            {
                RowContribution(L, ii, ref si, ax);
            }
        }

        AlignKind jc = a == 0 ? v.JustifyContent : v.AlignContent;
        bool stretchAuto = jc is AlignKind.Normal or AlignKind.Stretch;
        _sizer.Size(ax, items, available, mode == 0 && double.IsNaN(available) ? 2 : mode, containerMin, containerMax, stretchAuto);
        if (_sizer.HitLimit)
        {
            GridDiagnostic("grid track sizing reached its work limit; some spanning items were ignored");
        }

        double inner = available;
        if (double.IsNaN(inner))
        {
            inner = double.NaN;
        }

        AlignTracks(ax, in v, a, inner, false);
        _ = containerMin;
    }

    /// <summary>
    /// Measures the block size of every item the rows must accommodate and finds the shims that line up baseline-aligned items
    /// in the same first row (Grid 2 §11.5, Box Alignment 3 §9).
    /// </summary>
    private void MeasureRowItems(GridLevel L, int k, int leaves, int tracks)
    {
        Box cbox = _boxes.Make(L.Ctx[k].Box);
        if (L.GroupBase.Length < tracks + 1)
        {
            L.GroupBase = new double[Math.Max(tracks + 1, L.GroupBase.Length * 2)];
        }

        bool anyBaseline = false;
        for (int i = 0; i < leaves; i++)
        {
            ref GItem it = ref L.Item[L.SizeMap[i]];
            Box b = _boxes.Make(it.Box);
            AlignKind al = SelfAlign(in b.Values, in cbox.Values, false, out AlignOverflowKind ao);
            it.A = al;
            it.AO = ao;
            it.Baseline = al == AlignKind.Baseline;
            anyBaseline |= it.Baseline;
            MeasureItem(L, L.SizeMap[i], it.Baseline);
        }

        if (!anyBaseline)
        {
            return;
        }

        Array.Fill(L.GroupBase, double.NegativeInfinity, 0, tracks + 1);
        for (int i = 0; i < leaves; i++)
        {
            ref GItem it = ref L.Item[L.SizeMap[i]];
            if (it.Baseline && it.O1 >= 0 && it.O1 < tracks)
            {
                double bl = it.ES1 + it.MT + (double.IsNaN(it.MeasBase) ? it.MeasH : it.MeasBase);
                L.GroupBase[it.O1] = Math.Max(L.GroupBase[it.O1], bl);
            }
        }

        for (int i = 0; i < leaves; i++)
        {
            ref GItem it = ref L.Item[L.SizeMap[i]];
            if (it.Baseline && it.O1 >= 0 && it.O1 < tracks)
            {
                double bl = it.ES1 + it.MT + (double.IsNaN(it.MeasBase) ? it.MeasH : it.MeasBase);
                it.Shim = L.GroupBase[it.O1] - bl;
            }
        }
    }

    /// <summary>Collects the items whose sizes the axis of grid <paramref name="k"/> must accommodate, subgridded items' children included (Grid 2 §12.1).</summary>
    private static int GatherLeaves(GridLevel L, int k, int a)
    {
        int n = 0;
        int sp = 0;
        if (L.Stack.Length < 4)
        {
            L.Stack = new int[16];
        }

        L.Stack[sp++] = k;
        while (sp > 0)
        {
            int c = L.Stack[--sp];
            ref GCtx cx = ref L.Ctx[c];
            for (int i = 0; i < cx.ItemCount; i++)
            {
                int ii = cx.FirstItem + i;
                ref GItem it = ref L.Item[ii];
                if (c != k)
                {
                    ComputeExtras(L, c, k, ii, a);
                }
                else if (a == 0)
                {
                    it.ES0 = it.EE0 = 0;
                }
                else
                {
                    it.ES1 = it.EE1 = 0;
                }

                if (it.Child >= 0 && (a == 0 ? L.Ctx[it.Child].Sub0 : L.Ctx[it.Child].Sub1))
                {
                    continue;
                }

                if (n >= L.Size.Length || n >= L.SizeMap.Length)
                {
                    // The two arrays are resized independently elsewhere (order sorting borrows the size array), so grow both.
                    int size = Math.Max(16, n * 2);
                    Array.Resize(ref L.Size, size);
                    Array.Resize(ref L.SizeMap, size);
                }

                int s = a == 0 ? it.O0 : it.O1;
                int span = a == 0 ? it.Span0 : it.Span1;
                L.Size[n] = new SizeItem { Start = s, End = s + span };
                L.SizeMap[n] = ii;
                n++;
            }

            for (int m = cx.FirstChild; m >= 0; m = L.Ctx[m].NextSibling)
            {
                if (a == 0 ? L.Ctx[m].Sub0 : L.Ctx[m].Sub1)
                {
                    if (sp == L.Stack.Length)
                    {
                        Array.Resize(ref L.Stack, sp * 2);
                    }

                    L.Stack[sp++] = m;
                }
            }
        }

        return n;
    }

    /// <summary>
    /// Grid 2 §9: the sum of the margin, border and padding of each enclosing subgrid at the edge an item touches, and half the
    /// difference between the subgrid's gutters and its parent's on the sides that are not at an edge, as extra margin.
    /// </summary>
    private static void ComputeExtras(GridLevel L, int c, int owner, int ii, int a)
    {
        ref GItem it = ref L.Item[ii];
        int s = a == 0 ? it.O0 : it.O1;
        int e = s + (a == 0 ? it.Span0 : it.Span1);
        double es = 0, ee = 0;
        for (int guard = 0, cc = c; cc != owner && cc >= 0 && guard < 4096; guard++, cc = L.Ctx[cc].Parent)
        {
            ref GCtx x = ref L.Ctx[cc];
            int lo = a == 0 ? x.Off0 : x.Off1;
            int hi = lo + (a == 0 ? x.Span0 : x.Span1);
            double eff = a == 0 ? x.Eff0 : x.Eff1;
            double peff = a == 0 ? L.Ctx[x.Parent].Eff0 : L.Ctx[x.Parent].Eff1;
            double half = (eff - peff) / 2;
            es += s == lo ? (a == 0 ? x.S0 : x.S1) : half;
            ee += e == hi ? (a == 0 ? x.E0 : x.E1) : half;
        }

        if (a == 0)
        {
            it.ES0 = es;
            it.EE0 = ee;
        }
        else
        {
            it.ES1 = es;
            it.EE1 = ee;
        }
    }

    private void BuildTracks(GridLevel L, int k, int a, double basis, int leaves)
    {
        ref GCtx cx = ref L.Ctx[k];
        Box b = _boxes.Make(cx.Box);
        ref readonly ComputedValues v = ref b.Values;
        GridAxis ax = L.AxisOf(k, a);
        int n = ax.Count;
        GridTrackSize[] pattern = a == 0 ? v.GridAutoColumns : v.GridAutoRows;
        int before = ax.Before;
        double gap = a == 0 ? cx.Eff0 : cx.Eff1;
        for (int i = 0; i < n; i++)
        {
            int idx = i - before;
            GridTrackSize ts;
            if (idx < 0)
            {
                int j = before - i;
                ts = pattern[pattern.Length - 1 - ((j - 1) % pattern.Length)];
            }
            else if (idx < ax.ExpCount)
            {
                ts = ax.Exp[idx];
            }
            else
            {
                ts = pattern[(idx - ax.ExpCount) % pattern.Length];
            }

            SetKind(ax, i, in ts, basis);
            ax.GapAfter[i] = i < n - 1 ? gap : 0;
            ax.Collapsed[i] = false;
        }

        if (ax.AutoFit && ax.AutoTo > ax.AutoFrom)
        {
            if (L.Used.Length < n)
            {
                L.Used = new bool[Math.Max(n, L.Used.Length * 2)];
            }

            Array.Clear(L.Used, 0, n);
            for (int i = 0; i < leaves; i++)
            {
                for (int t = Math.Max(0, L.Size[i].Start); t < Math.Min(n, L.Size[i].End); t++)
                {
                    L.Used[t] = true;
                }
            }

            for (int t = before + ax.AutoFrom; t < before + ax.AutoTo && t < n; t++)
            {
                ax.Collapsed[t] = !L.Used[t];
            }

            // Grid 2 §7.2.3.2: the gutters on both sides of a collapsed track coincide.
            for (int t = 0; t < n; t++)
            {
                if (ax.Collapsed[t])
                {
                    ax.GapAfter[t] = 0;
                    if (t == n - 1 && t > 0)
                    {
                        ax.GapAfter[t - 1] = 0;
                    }
                }
            }
        }
    }

    private static void SetKind(GridAxis ax, int i, in GridTrackSize ts, double basis)
    {
        GridBreadth mn = ts.Min, mx = ts.Max;
        switch (mn.Kind)
        {
            case GridBreadthKind.Length:
            {
                double px = mn.Length.Kind == LengthKind.Px ? mn.Length.Value : double.IsNaN(basis) ? double.NaN : mn.Length.Resolve(basis);
                if (double.IsNaN(px))
                {
                    ax.MinKind[i] = TrackKind.Auto;
                }
                else
                {
                    ax.MinKind[i] = TrackKind.Fixed;
                    ax.MinPx[i] = Math.Max(0, px);
                }

                break;
            }

            case GridBreadthKind.MinContent: ax.MinKind[i] = TrackKind.MinContent; break;
            case GridBreadthKind.MaxContent: ax.MinKind[i] = TrackKind.MaxContent; break;
            default: ax.MinKind[i] = TrackKind.Auto; break;
        }

        ax.Flex[i] = 0;
        switch (mx.Kind)
        {
            case GridBreadthKind.Length:
            {
                double px = mx.Length.Kind == LengthKind.Px ? mx.Length.Value : double.IsNaN(basis) ? double.NaN : mx.Length.Resolve(basis);
                if (double.IsNaN(px))
                {
                    ax.MaxKind[i] = TrackKind.Auto;
                }
                else
                {
                    ax.MaxKind[i] = TrackKind.Fixed;
                    ax.MaxPx[i] = Math.Max(0, px);
                }

                break;
            }

            case GridBreadthKind.MinContent: ax.MaxKind[i] = TrackKind.MinContent; break;
            case GridBreadthKind.MaxContent: ax.MaxKind[i] = TrackKind.MaxContent; break;
            case GridBreadthKind.Flex:
                ax.MaxKind[i] = TrackKind.Flex;
                ax.Flex[i] = mx.Flex;
                break;
            case GridBreadthKind.FitContent:
            {
                double px = mx.Length.Kind == LengthKind.Px ? mx.Length.Value : double.IsNaN(basis) ? double.NaN : mx.Length.Resolve(basis);
                if (double.IsNaN(px))
                {
                    ax.MaxKind[i] = TrackKind.Auto;
                }
                else
                {
                    ax.MaxKind[i] = TrackKind.FitContent;
                    ax.FitPx[i] = Math.Max(0, px);
                }

                break;
            }

            default: ax.MaxKind[i] = TrackKind.Auto; break;
        }

        // A fixed maximum below the minimum is floored by it (Grid 2 §7.2.1); the sizer's initialization does that.
    }

    // ---- contributions ----

    private static bool AutomaticMinimumApplies(GridAxis ax, in ComputedValues v, int axis, int start, int end)
    {
        OverflowKind o = axis == 0 ? v.OverflowX : v.OverflowY;
        if (o is not (OverflowKind.Visible or OverflowKind.Clip))
        {
            return false;
        }

        bool autoMin = false;
        bool flex = false;
        for (int t = start; t < end && t < ax.Count; t++)
        {
            autoMin |= ax.MinKind[t] == TrackKind.Auto;
            flex |= ax.MaxKind[t] == TrackKind.Flex;
        }

        return autoMin && (end - start == 1 || !flex);
    }

    private void ColumnContribution(GridLevel L, int ii, ref SizeItem si, GridAxis ax)
    {
        ref GItem it = ref L.Item[ii];
        Box b = _boxes.Make(it.Box);
        ref readonly ComputedValues v = ref b.Values;
        double basis = L.Ctx[it.Ctx].CW;
        if (double.IsNaN(basis))
        {
            basis = 0;
        }

        GetIntrinsic(b, out double imin, out double imax);
        Contribution(b, out double minC, out double maxC);
        double extra = it.ES0 + it.EE0;
        // Percent margins and paddings count as zero while the column is unsized (the same simplification as for block children).
        double edges = FixedPadding(in v) + (v.MarginLeft.IsPx ? v.MarginLeft.Value : 0) + (v.MarginRight.IsPx ? v.MarginRight.Value : 0);
        si.MinContent = minC + extra;
        si.MaxContent = maxC + extra;
        _ = basis;
        double ph = FixedPadding(in v);
        double mnVal;
        if (v.Width.Kind == LengthKind.Px)
        {
            si.MinContribution = si.MinContent;
            return;
        }

        if (v.MinWidth.Kind == LengthKind.Px)
        {
            mnVal = Math.Max(0, v.BoxSizing == BoxSizingKind.BorderBox ? v.MinWidth.Value - ph : v.MinWidth.Value);
        }
        else if (AutomaticMinimumApplies(ax, in v, 0, si.Start, si.End))
        {
            // Grid 2 §6.6: the content-based minimum size, clamped by a definite maximum size.
            mnVal = imin;
            if (v.MaxWidth.Kind == LengthKind.Px)
            {
                mnVal = Math.Min(mnVal, Math.Max(0, v.BoxSizing == BoxSizingKind.BorderBox ? v.MaxWidth.Value - ph : v.MaxWidth.Value));
            }

            // Spanning only fixed maximums, it is also limited to the room those tracks give it.
            bool allFixed = true;
            double room = ax.GapsBetween(si.Start, si.End);
            for (int t = si.Start; t < si.End && t < ax.Count; t++)
            {
                if (ax.MaxKind[t] != TrackKind.Fixed)
                {
                    allFixed = false;
                    break;
                }

                room += ax.MaxPx[t];
            }

            if (allFixed)
            {
                mnVal = Math.Min(mnVal, Math.Max(0, room - edges - extra));
            }
        }
        else
        {
            mnVal = 0;
        }

        si.MinContribution = Math.Min(si.MinContent, mnVal + edges + extra);
        if (v.MinWidth.Kind == LengthKind.Px)
        {
            si.MinContribution = mnVal + edges + extra;
        }
    }

    private void RowContribution(GridLevel L, int ii, ref SizeItem si, GridAxis ax)
    {
        ref GItem it = ref L.Item[ii];
        Box b = _boxes.Make(it.Box);
        ref readonly ComputedValues v = ref b.Values;
        double h = it.MeasH;
        double outer = h + it.MT + it.MB + it.ES1 + it.EE1 + it.Shim;
        si.MinContent = outer;
        si.MaxContent = outer;
        double pv = it.MT + it.MB;
        _ = pv;
        if (v.Height.Kind == LengthKind.Px)
        {
            si.MinContribution = outer;
            return;
        }

        double verticalPb = v.BorderTopWidth + v.BorderBottomWidth + Pad(v.PaddingTop, L.Ctx[it.Ctx].CW) + Pad(v.PaddingBottom, L.Ctx[it.Ctx].CW);
        if (v.MinHeight.Kind == LengthKind.Px)
        {
            double mh = Math.Max(0, v.BoxSizing == BoxSizingKind.BorderBox ? v.MinHeight.Value - verticalPb : v.MinHeight.Value);
            si.MinContribution = Math.Min(outer, mh + verticalPb + it.MT + it.MB + it.ES1 + it.EE1 + it.Shim);
            return;
        }

        si.MinContribution = AutomaticMinimumApplies(ax, in v, 1, si.Start, si.End)
            ? outer
            : Math.Min(outer, verticalPb + it.MT + it.MB + it.ES1 + it.EE1 + it.Shim);
    }

    // ---- alignment of the tracks (Box Alignment 3 §5, Grid 2 §11.5) ----

    private static void AlignTracks(GridAxis ax, in ComputedValues v, int a, double inner, bool rtl)
    {
        _ = rtl;
        int n = ax.Count;
        AlignKind kind = a == 0 ? v.JustifyContent : v.AlignContent;
        AlignOverflowKind ov = a == 0 ? v.JustifyContentOverflow : v.AlignContentOverflow;
        bool rtlAxis = a == 0 && v.Direction == DirectionKind.Rtl;
        double total = 0;
        int nonCollapsed = 0;
        for (int t = 0; t < n; t++)
        {
            total += ax.Base[t] + (t < n - 1 ? ax.GapAfter[t] : 0);
            if (!ax.Collapsed[t])
            {
                nonCollapsed++;
            }
        }

        double free = double.IsNaN(inner) ? 0 : inner - total;
        double lead = 0, extra = 0;
        switch (kind)
        {
            case AlignKind.End:
            case AlignKind.FlexEnd:
            case AlignKind.SelfEnd:
                lead = free;
                break;
            case AlignKind.Right:
                lead = rtlAxis ? 0 : free;
                break;
            case AlignKind.Left:
                lead = rtlAxis ? free : 0;
                break;
            case AlignKind.Center:
                lead = free / 2;
                break;
            case AlignKind.SpaceBetween:
                if (free > 0 && nonCollapsed > 1)
                {
                    extra = free / (nonCollapsed - 1);
                }

                break;
            case AlignKind.SpaceAround:
                if (free > 0 && nonCollapsed > 0)
                {
                    extra = free / nonCollapsed;
                    lead = extra / 2;
                }
                else
                {
                    lead = free > 0 ? free / 2 : 0;
                }

                break;
            case AlignKind.SpaceEvenly:
                if (free > 0 && nonCollapsed > 0)
                {
                    extra = free / (nonCollapsed + 1);
                    lead = extra;
                }
                else
                {
                    lead = free > 0 ? free / 2 : 0;
                }

                break;
        }

        if (ov == AlignOverflowKind.Safe && free < 0)
        {
            lead = 0;
        }

        double pos = lead;
        int lastNonCollapsed = -1;
        for (int t = n - 1; t >= 0; t--)
        {
            if (!ax.Collapsed[t])
            {
                lastNonCollapsed = t;
                break;
            }
        }

        for (int t = 0; t < n; t++)
        {
            ax.Start[t] = pos;
            double size = ax.Collapsed[t] ? 0 : ax.Base[t];
            ax.End[t] = pos + size;
            pos = ax.End[t] + (t < n - 1 ? ax.GapAfter[t] : 0);
            if (!ax.Collapsed[t] && t < lastNonCollapsed)
            {
                pos += extra;
            }
        }

        ax.Start[n] = pos;
        ax.End[n] = pos;
    }

    // ---- items: inline axis ----

    private static bool IsStretchLike(AlignKind j, bool replaced) => j == AlignKind.Stretch || (j == AlignKind.Normal && !replaced);

    private static AlignKind SelfAlign(in ComputedValues itemV, in ComputedValues containerV, bool inline, out AlignOverflowKind overflow)
    {
        AlignKind k = inline ? itemV.JustifySelf : itemV.AlignSelf;
        overflow = inline ? itemV.JustifySelfOverflow : itemV.AlignSelfOverflow;
        if (k == AlignKind.Auto)
        {
            k = inline ? containerV.JustifyItems : containerV.AlignItems;
            overflow = inline ? containerV.JustifyItemsOverflow : containerV.AlignItemsOverflow;
        }

        return k;
    }

    /// <summary>
    /// Sizes and places an item along the inline axis inside its grid area (Grid 2 §6.2, §11.2, §11.3): the used border-box width,
    /// the margins (auto margins absorb free space) and the offset in the owner's coordinates.
    /// </summary>
    private void ResolveInline(GridLevel L, int ii)
    {
        ref GItem it = ref L.Item[ii];
        ref GCtx cx = ref L.Ctx[it.Ctx];
        Box b = _boxes.Make(it.Box);
        ref readonly ComputedValues v = ref b.Values;
        Box cbox = _boxes.Make(cx.Box);
        GridAxis ax = L.AxisOf(cx.Owner0, 0);
        int s = it.O0, e = it.O0 + it.Span0;
        double areaStart = ax.Count == 0 ? 0 : ax.Start[Math.Min(s, ax.Count - 1)];
        double areaEnd = ax.Count == 0 ? 0 : ax.End[Math.Min(Math.Max(e, 1), ax.Count) - 1];
        double areaW = Math.Max(0, areaEnd - areaStart);
        bool rtl = cx.Rtl;
        double mlv = MarginPx(v.MarginLeft, areaW), mrv = MarginPx(v.MarginRight, areaW);
        bool mlAuto = v.MarginLeft.IsAuto, mrAuto = v.MarginRight.IsAuto;
        double pl = Pad(v.PaddingLeft, areaW), pr = Pad(v.PaddingRight, areaW);
        double ph = pl + pr + v.BorderLeftWidth + v.BorderRightWidth;
        bool isSubgridInline = it.Child >= 0 && L.Ctx[it.Child].Sub0;

        // Logical (start, end) margins.
        double ms = (rtl ? mrv : mlv) + it.ES0, me = (rtl ? mlv : mrv) + it.EE0;
        bool msAuto = rtl ? mrAuto : mlAuto, meAuto = rtl ? mlAuto : mrAuto;
        AlignKind j = SelfAlign(in v, in cbox.Values, true, out AlignOverflowKind jo);
        if (j == AlignKind.Baseline || j == AlignKind.LastBaseline)
        {
            j = AlignKind.Start; // Box Alignment 3 §9.3: baseline alignment in the inline axis falls back to start.
        }

        bool widthAuto = v.Width.Kind is LengthKind.Auto or LengthKind.Stretch;
        bool stretch = (isSubgridInline || IsStretchLike(j, it.Replaced) && widthAuto) && !msAuto && !meAuto;
        double availContent = Math.Max(0, areaW - ms - me - ph);
        double borderW;
        if (it.Replaced)
        {
            ReplacedContentSize(b, in v, areaW, double.NaN, ph, v.BorderTopWidth + v.BorderBottomWidth, availContent, out double rw, out double rh);
            if (stretch && j == AlignKind.Stretch)
            {
                double ratio = rh > 0 ? rw / rh : 0;
                rw = availContent;
                rh = ratio > 0 ? rw / ratio : rh;
            }

            borderW = rw + ph;
            it.MeasH = rh + v.BorderTopWidth + v.BorderBottomWidth + Pad(v.PaddingTop, areaW) + Pad(v.PaddingBottom, areaW);
        }
        else
        {
            double cwv = isSubgridInline ? availContent : ResolveWidthLength(b, v.Width, areaW, ph, v.BoxSizing, availContent, isMin: false);
            double minW = isSubgridInline ? 0 : ResolveWidthLength(b, v.MinWidth, areaW, ph, v.BoxSizing, availContent, isMin: true);
            double maxW = isSubgridInline ? double.NaN : ResolveWidthLength(b, v.MaxWidth, areaW, ph, v.BoxSizing, availContent, isMin: false);
            if (double.IsNaN(cwv))
            {
                if (stretch)
                {
                    cwv = availContent;
                }
                else
                {
                    GetIntrinsic(b, out double imin, out double imax);
                    cwv = Math.Min(imax, Math.Max(imin, availContent));
                }
            }

            if (!double.IsNaN(maxW))
            {
                cwv = Math.Min(cwv, maxW);
            }

            if (!double.IsNaN(minW))
            {
                cwv = Math.Max(cwv, minW);
            }

            borderW = Math.Max(0, cwv) + ph;
        }

        double free = areaW - (borderW + ms + me);
        double offset;
        if ((msAuto || meAuto) && free > 0)
        {
            if (msAuto && meAuto)
            {
                ms += free / 2;
                me += free / 2;
            }
            else if (msAuto)
            {
                ms += free;
            }
            else
            {
                me += free;
            }

            offset = ms;
        }
        else
        {
            double shift = 0;
            AlignKind eff = stretch ? AlignKind.Start : j;
            if (eff == AlignKind.Normal || eff == AlignKind.Stretch)
            {
                eff = AlignKind.Start;
            }

            switch (eff)
            {
                case AlignKind.End:
                case AlignKind.FlexEnd:
                    shift = free;
                    break;
                case AlignKind.SelfEnd:
                    shift = (v.Direction == DirectionKind.Rtl) == rtl ? free : 0;
                    break;
                case AlignKind.SelfStart:
                    shift = (v.Direction == DirectionKind.Rtl) == rtl ? 0 : free;
                    break;
                case AlignKind.Left:
                    shift = rtl ? free : 0;
                    break;
                case AlignKind.Right:
                    shift = rtl ? 0 : free;
                    break;
                case AlignKind.Center:
                    shift = free / 2;
                    break;
            }

            if (jo == AlignOverflowKind.Safe && free < 0)
            {
                shift = 0;
            }

            offset = ms + shift;
        }

        it.X = areaStart + offset;
        it.W = borderW;
        it.StretchW = stretch;
        it.J = j;
        it.JO = jo;
        // Physical used margins (own margins only; the extras are positional).
        double ownS = ms - it.ES0, ownE = me - it.EE0;
        if (msAuto && !(free > 0))
        {
            ownS = 0;
        }

        if (meAuto && !(free > 0))
        {
            ownE = 0;
        }

        it.ML = rtl ? ownE : ownS;
        it.MR = rtl ? ownS : ownE;

        if (it.Child >= 0)
        {
            ref GCtx child = ref L.Ctx[it.Child];
            child.CW = Math.Max(0, borderW - child.BpL - child.BpR);
            if (child.Sub0)
            {
                child.Org0 = it.X + (child.Rtl ? child.BpR : child.BpL);
            }
        }
    }

    // ---- items: measuring ----

    /// <summary>Lays an item out at its inline size to learn its block size and baseline, then discards the fragments.</summary>
    private void MeasureItem(GridLevel L, int ii, bool needBaseline)
    {
        ref GItem it = ref L.Item[ii];
        Box b = _boxes.Make(it.Box);
        ref readonly ComputedValues v = ref b.Values;
        ref GCtx cx = ref L.Ctx[it.Ctx];
        double areaW = cx.CW;
        double basisW = double.IsNaN(areaW) ? 0 : areaW;
        double mt = MarginPx(v.MarginTop, basisW), mb = MarginPx(v.MarginBottom, basisW);
        it.MT = mt;
        it.MB = mb;
        it.Shim = 0;
        if (it.Child >= 0)
        {
            ref GCtx child = ref L.Ctx[it.Child];
            if (!child.Sub1)
            {
                // A subgrid in columns only: its height is its own rows plus its edges.
                it.MeasH = child.CH + child.BpT + child.BpB;
                it.MeasBase = double.NaN;
                it.Measured = true;
                return;
            }

            it.MeasH = 0;
            it.MeasBase = double.NaN;
            it.Measured = true;
            return;
        }

        double pv = v.BorderTopWidth + v.BorderBottomWidth + Pad(v.PaddingTop, basisW) + Pad(v.PaddingBottom, basisW);
        if (it.Replaced)
        {
            // Computed with the width in ResolveInline.
            it.MeasBase = double.NaN;
            it.Measured = true;
            return;
        }

        if (v.Height.Kind == LengthKind.Px && !needBaseline)
        {
            ResolveHeights(in v, double.NaN, pv, mt, mb, it.W, Math.Max(0, it.W - FixedPadding(in v)), out double sh, out double mnh, out double mxh, out _);
            it.MeasH = (double.IsNaN(sh) ? 0 : Math.Max(mnh, Math.Min(mxh, sh))) + pv;
            it.MeasBase = double.NaN;
            it.Measured = true;
            return;
        }

        Fragment parent = new(_out, L.Ctx[0].Frag);
        FragmentMark mark = _out.Mark(parent);
        int savedAbs = _absCount;
        _shrinkNext = false;
        _absForce = true;
        _absW = it.W;
        double ml = it.ML, mr = it.MR;
        _absMl = ml;
        _absMr = mr;
        _absH = double.NaN;
        Fragment f = RunIndependent(b, parent, areaW, double.NaN, out _);
        _absForce = false;
        if (f.IsNull)
        {
            it.MeasH = pv;
            it.MeasBase = double.NaN;
        }
        else
        {
            Rect r = _out.BorderBoxOf(f.Index);
            it.MeasH = r.Height;
            it.MeasBase = _out.BaselineOf(f.Index);
        }

        _out.Truncate(mark);
        _absCount = savedAbs;
        it.Measured = true;
    }
}
