using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    /// <summary>0 while laying out; 1 / 2 while measuring a grid container's min-content / max-content width.</summary>
    private int _gridIntrinsicMode;

    // ---- final layout: rows, alignment, fragments ----

    /// <summary>
    /// Places every item in its grid area (Grid 2 §11): the block-axis size and alignment, then the item's own layout at the used
    /// size. Contexts are visited parents first, so a subgrid's fragment exists before its items are added to it.
    /// </summary>
    private void LayoutGridItems(GridLevel L, int ni)
    {
        for (int k = 0; k < L.CtxCount; k++)
        {
            ref GCtx cx = ref L.Ctx[k];
            cx.OvL = cx.OvT = double.PositiveInfinity;
            cx.OvR = cx.OvB = double.NegativeInfinity;
            if (k == 0)
            {
                cx.CbFrag = _frames[ni].CbFrag;
            }
        }

        for (int k = 0; k < L.CtxCount; k++)
        {
            if ((++_counter & 63) == 0)
            {
                _token.ThrowIfCancellationRequested();
            }

            int first = L.Ctx[k].FirstItem, n = L.Ctx[k].ItemCount;
            for (int i = 0; i < n; i++)
            {
                if ((i & 255) == 255)
                {
                    _token.ThrowIfCancellationRequested();
                }

                PlaceGridItem(L, first + i);
            }

            QueueGridAbsolutes(L, k, ni);
        }

        // Scrollable overflow flows from the deepest subgrid up to the container's frame.
        for (int k = L.CtxCount - 1; k >= 1; k--)
        {
            ref GCtx cx = ref L.Ctx[k];
            var frag = new Fragment(_out, cx.Frag);
            Rect bb = _out.BorderBoxOf(cx.Frag);
            double l = Math.Min(0, cx.OvL), t = Math.Min(0, cx.OvT);
            double r = Math.Max(bb.Width, cx.OvR), b = Math.Max(bb.Height, cx.OvB);
            _out.SetScrollableOverflow(frag, new Rect(l, t, r - l, b - t));
            ref GCtx p = ref L.Ctx[cx.Parent];
            Box cbx = _boxes.Make(cx.Box);
            bool clipX = cbx.Values.OverflowX != OverflowKind.Visible, clipY = cbx.Values.OverflowY != OverflowKind.Visible;
            p.OvL = Math.Min(p.OvL, bb.X + (clipX ? 0 : l));
            p.OvT = Math.Min(p.OvT, bb.Y + (clipY ? 0 : t));
            p.OvR = Math.Max(p.OvR, bb.X + (clipX ? bb.Width : r));
            p.OvB = Math.Max(p.OvB, bb.Y + (clipY ? bb.Height : b));
        }

        ref GCtx root = ref L.Ctx[0];
        ref Frame f = ref _frames[ni];
        if (root.OvR >= root.OvL)
        {
            f.OvL = Math.Min(f.OvL, root.OvL);
            f.OvT = Math.Min(f.OvT, root.OvT);
            f.OvR = Math.Max(f.OvR, root.OvR);
            f.OvB = Math.Max(f.OvB, root.OvB);
        }

        f.Cursor = double.IsNaN(root.CH) ? 0 : root.CH;
        f.NextChild = -1;
        f.Flags |= FrameFlags.HasContent | FrameFlags.AnyChild;
        f.FirstBaseline = GridFirstBaseline(L);
    }

    /// <summary>Grid 2 §11.6: the first baseline is that of the first item in the first row (preferring baseline-aligned ones).</summary>
    private double GridFirstBaseline(GridLevel L)
    {
        ref GCtx cx = ref L.Ctx[0];
        int best = -1;
        for (int i = 0; i < cx.ItemCount; i++)
        {
            ref GItem it = ref L.Item[cx.FirstItem + i];
            if (it.O1 != 0 || it.Child >= 0 || it.Frag < 0)
            {
                continue;
            }

            if (best < 0 || (it.Baseline && !L.Item[best].Baseline))
            {
                best = cx.FirstItem + i;
            }
        }

        if (best < 0)
        {
            return double.NaN;
        }

        ref GItem bi = ref L.Item[best];
        double baseline = _out.BaselineOf(bi.Frag);
        if (double.IsNaN(baseline))
        {
            return double.NaN;
        }

        Rect bb = _out.BorderBoxOf(bi.Frag);
        return bb.Y + baseline;
    }

    private void PlaceGridItem(GridLevel L, int ii)
    {
        ref GItem it = ref L.Item[ii];
        ref GCtx cx = ref L.Ctx[it.Ctx];
        Box b = _boxes.Make(it.Box);
        ref readonly ComputedValues v = ref b.Values;
        GridAxis ax0 = L.AxisOf(cx.Owner0, 0);
        GridAxis ax1 = L.AxisOf(cx.Owner1, 1);
        int s0 = Math.Min(it.O0, Math.Max(0, ax0.Count - 1)), e0 = Math.Min(Math.Max(it.O0 + it.Span0, 1), ax0.Count);
        double aw = ax0.Count == 0 ? 0 : Math.Max(0, ax0.End[Math.Max(e0 - 1, 0)] - ax0.Start[s0]);
        int s1 = Math.Min(it.O1, Math.Max(0, ax1.Count - 1)), e1 = Math.Min(Math.Max(it.O1 + it.Span1, 1), ax1.Count);
        double areaTop = ax1.Count == 0 ? 0 : ax1.Start[s1];
        double areaH = ax1.Count == 0 ? 0 : Math.Max(0, ax1.End[Math.Max(e1 - 1, 0)] - areaTop);
        double padV = v.BorderTopWidth + v.BorderBottomWidth + Pad(v.PaddingTop, aw) + Pad(v.PaddingBottom, aw);
        double padH = v.BorderLeftWidth + v.BorderRightWidth + Pad(v.PaddingLeft, aw) + Pad(v.PaddingRight, aw);
        bool sub1 = it.Child >= 0 && L.Ctx[it.Child].Sub1;
        double mt = MarginPx(v.MarginTop, aw), mb = MarginPx(v.MarginBottom, aw);
        bool mtAuto = v.MarginTop.IsAuto, mbAuto = v.MarginBottom.IsAuto;
        double basisH = Math.Max(0, areaH - it.ES1 - it.EE1);
        double avail = Math.Max(0, basisH - mt - mb);
        AlignKind a = SelfAlign(in v, in _boxes.Make(cx.Box).Values, false, out AlignOverflowKind ao);
        it.A = a;
        it.AO = ao;
        bool baselineItem = a == AlignKind.Baseline && it.Baseline;
        double h;
        if (sub1)
        {
            h = avail;
        }
        else
        {
            ResolveHeights(in v, basisH, padV, mt, mb, it.W, Math.Max(0, it.W - padH), out double specH, out double minH, out double maxH, out _);
            bool heightAuto = v.Height.Kind is LengthKind.Auto or LengthKind.Stretch;
            bool stretch = heightAuto && !mtAuto && !mbAuto && (it.Replaced ? a == AlignKind.Stretch : a is AlignKind.Normal or AlignKind.Stretch);
            if (!it.Replaced && !double.IsNaN(specH))
            {
                h = Math.Max(minH, Math.Min(maxH, specH)) + padV;
            }
            else if (stretch)
            {
                h = Math.Max(minH, Math.Min(maxH, Math.Max(0, avail - padV))) + padV;
            }
            else
            {
                h = it.MeasH;
            }
        }

        it.H = h;
        double free = basisH - (h + mt + mb);
        double shift = 0;
        double ownT = mt;
        if ((mtAuto || mbAuto) && free > 0 && !sub1)
        {
            if (mtAuto && mbAuto)
            {
                ownT += free / 2;
            }
            else if (mtAuto)
            {
                ownT += free;
            }
        }
        else if (!sub1)
        {
            switch (a)
            {
                case AlignKind.End:
                case AlignKind.FlexEnd:
                case AlignKind.SelfEnd:
                case AlignKind.LastBaseline:
                    shift = free;
                    break;
                case AlignKind.Center:
                    shift = free / 2;
                    break;
            }

            if (ao == AlignOverflowKind.Safe && free < 0)
            {
                shift = 0;
            }
        }

        it.Y = areaTop + it.ES1 + ownT + shift + (baselineItem ? it.Shim : 0);

        double ox0 = cx.Sub0 ? cx.Org0 : 0, oy1 = cx.Sub1 ? cx.Org1 : 0;
        double lx = it.X - ox0;
        double px = cx.Rtl ? cx.CW - lx - it.W : lx;
        double localX = cx.PadL + px;
        double localY = cx.PadT + (it.Y - oy1);
        RelativeOffset(in v, aw, basisH, cx.Rtl, out double relDx, out double relDy);
        var parent = new Fragment(_out, cx.Frag);
        Fragment frag;
        if (it.Child >= 0)
        {
            frag = _out.AddBox(parent, b, new Rect(localX + relDx, localY + relDy, it.W, h));
            _out.SetEdges(frag, new Insets(v.BorderTopWidth, v.BorderRightWidth, v.BorderBottomWidth, v.BorderLeftWidth),
                new Insets(Pad(v.PaddingTop, aw), Pad(v.PaddingRight, aw), Pad(v.PaddingBottom, aw), Pad(v.PaddingLeft, aw)));
            _out.SetBaseline(frag, double.NaN);
            ref GCtx child = ref L.Ctx[it.Child];
            child.Frag = frag.Index;
            child.CbFrag = v.Position != PositionKind.Static ? frag.Index : cx.CbFrag;
            if (child.Sub1)
            {
                child.CH = Math.Max(0, h - child.BpT - child.BpB);
                child.Org1 = it.Y + child.BpT;
            }
        }
        else
        {
            frag = LayoutGridLeaf(L, ii, b, in v, aw, areaH, h, padH, padV);
            if (frag.IsNull)
            {
                it.Frag = -1;
                return;
            }

            _out.SetBorderBox(frag, new Rect(localX + relDx, localY + relDy, it.W, h));
            Rect ov = _out.OverflowOf(frag.Index);
            cx.OvL = Math.Min(cx.OvL, localX + relDx + Math.Min(0, ov.X));
            cx.OvT = Math.Min(cx.OvT, localY + relDy + Math.Min(0, ov.Y));
            cx.OvR = Math.Max(cx.OvR, localX + relDx + Math.Max(it.W, ov.X + ov.Width));
            cx.OvB = Math.Max(cx.OvB, localY + relDy + Math.Max(h, ov.Y + ov.Height));
        }

        it.Frag = frag.Index;
    }

    private Fragment LayoutGridLeaf(GridLevel L, int ii, Box b, in ComputedValues v, double areaW, double areaH, double h, double padH, double padV)
    {
        ref GItem it = ref L.Item[ii];
        var parent = new Fragment(_out, L.Ctx[it.Ctx].Frag);
        _shrinkNext = false;
        if (it.Replaced)
        {
            _gridReplacedForce = true;
            _absRepW = Math.Max(0, it.W - padH);
            _absRepH = Math.Max(0, h - padV);
        }
        else
        {
            _absForce = true;
            _absW = it.W;
            _absMl = it.ML;
            _absMr = it.MR;
            _absH = Math.Max(0, h - padV);
        }

        Fragment f = RunIndependent(b, parent, areaW, areaH - it.ES1 - it.EE1, out _);
        _absForce = false;
        _gridReplacedForce = false;
        _ = v;
        return f;
    }

    // ---- absolutely positioned children (Grid 2 §10) ----

    private void QueueGridAbsolutes(GridLevel L, int k, int ni)
    {
        ref GCtx cx = ref L.Ctx[k];
        if (cx.AbsCount == 0)
        {
            return;
        }

        Frame tf = _frames[ni];
        tf.Frag = cx.Frag;
        tf.CbFrag = cx.CbFrag;
        tf.Flags = cx.Rtl ? FrameFlags.Rtl : FrameFlags.None;
        tf.ContentLeft = cx.PadL;
        tf.ContentTop = cx.PadT;
        tf.ContentWidth = double.IsNaN(cx.CW) ? 0 : cx.CW;
        bool areaCb = cx.CbFrag == cx.Frag;
        Insets border = _out.BorderOf(cx.Frag);
        Insets padding = _out.PaddingOf(cx.Frag);
        for (int i = 0; i < cx.AbsCount; i++)
        {
            if (_absCount >= MaxAbsolute)
            {
                return;
            }

            Box c = _boxes.Make(L.Abs[cx.AbsFirst + i]);
            int before = _absCount;
            Enqueue(in tf, c, tf.ContentLeft, tf.ContentLeft + tf.ContentWidth, tf.ContentTop);
            if (!areaCb || _absCount == before)
            {
                continue;
            }

            ref AbsEntry e = ref _abs[_absCount - 1];
            e.HasArea = true;
            GridAxis ax0 = L.AxisOf(cx.Owner0, 0), ax1 = L.AxisOf(cx.Owner1, 1);
            AbsLines(c.Values.GridColumnStart, c.Values.GridColumnEnd, ax0, cx.Owner0 == k ? cx : L.Ctx[cx.Owner0], 0, out double l0, out double l1);
            AbsLines(c.Values.GridRowStart, c.Values.GridRowEnd, ax1, cx.Owner1 == k ? cx : L.Ctx[cx.Owner1], 1, out double t0, out double t1);
            double padL = cx.PadL - border.Left, padT = cx.PadT - border.Top;
            _ = padding;
            if (!double.IsNaN(l0) || !double.IsNaN(l1))
            {
                double w = double.IsNaN(cx.CW) ? 0 : cx.CW;
                double a = double.IsNaN(l0) ? double.NaN : l0 - (cx.Sub0 ? cx.Org0 : 0);
                double z = double.IsNaN(l1) ? double.NaN : l1 - (cx.Sub0 ? cx.Org0 : 0);
                if (cx.Rtl)
                {
                    (a, z) = (double.IsNaN(z) ? double.NaN : w - z, double.IsNaN(a) ? double.NaN : w - a);
                }

                e.AreaL = double.IsNaN(a) ? double.NaN : padL + a;
                e.AreaR = double.IsNaN(z) ? double.NaN : padL + z;
            }
            else
            {
                e.AreaL = e.AreaR = double.NaN;
            }

            e.AreaT = double.IsNaN(t0) ? double.NaN : padT + t0 - (cx.Sub1 ? cx.Org1 : 0);
            e.AreaB = double.IsNaN(t1) ? double.NaN : padT + t1 - (cx.Sub1 ? cx.Org1 : 0);
        }
    }

    /// <summary>Track-space positions of an absolutely positioned child's start and end lines; NaN for <c>auto</c> or a line outside the grid (the padding edge).</summary>
    private static void AbsLines(GridLine start, GridLine end, GridAxis ax, in GCtx owner, int axis, out double p0, out double p1)
    {
        _ = owner;
        _ = axis;
        p0 = p1 = double.NaN;
        int n = ax.Count;
        if (n == 0)
        {
            return;
        }

        if (start.Kind == GridLineKind.Line)
        {
            int idx = GridPlacer.LineIndex(start, ax.Explicit, ax.Names, false) + ax.Before;
            if (idx >= 0 && idx <= n)
            {
                p0 = idx == n ? ax.End[n - 1] : ax.Start[idx];
            }
        }

        if (end.Kind == GridLineKind.Line)
        {
            int idx = GridPlacer.LineIndex(end, ax.Explicit, ax.Names, true) + ax.Before;
            if (idx >= 0 && idx <= n)
            {
                p1 = idx == n ? ax.End[n - 1] : ax.Start[idx];
            }
        }
    }

    // ---- intrinsic sizes ----

    /// <summary>The min-content and max-content widths of a grid container's content (Grid 2 §11.1); false when grids nest too deeply.</summary>
    private bool GridIntrinsicSizes(Box b, out double min, out double max)
    {
        min = max = 0;
        if (_gridDepth >= MaxGridDepth)
        {
            return false;
        }

        GridLevel L = LevelAt(_gridDepth);
        _gridDepth++;
        int savedMode = _gridIntrinsicMode;
        try
        {
            L.Reset();
            int root = L.AddCtx();
            ref GCtx rc = ref L.Ctx[root];
            rc = default;
            rc.Box = b.Index;
            rc.Frag = -1;
            rc.Parent = -1;
            rc.FirstChild = -1;
            rc.NextSibling = -1;
            rc.Owner0 = rc.Owner1 = root;
            rc.CW = double.NaN;
            rc.CH = double.NaN;
            rc.MinCH = 0;
            rc.MaxCH = double.PositiveInfinity;
            rc.Rtl = b.Values.Direction == DirectionKind.Rtl;
            rc.Gap0 = rc.Gap1 = double.NaN;
            rc.Eff0 = ResolveGap(b.Values.ColumnGap, double.NaN);
            rc.Eff1 = ResolveGap(b.Values.RowGap, double.NaN);
            BuildGrid(L);
            GridAxis ax = L.AxisOf(0, 0);
            _gridIntrinsicMode = 1;
            SizeAllColumns(L);
            min = ax.Count == 0 ? 0 : ax.End[ax.Count - 1];
            _gridIntrinsicMode = 2;
            SizeAllColumns(L);
            max = ax.Count == 0 ? 0 : ax.End[ax.Count - 1];
            min = Math.Max(0, min);
            max = Math.Max(min, max);
            return true;
        }
        finally
        {
            _gridIntrinsicMode = savedMode;
            _gridDepth--;
        }
    }
}
