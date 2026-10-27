using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    /// <summary>Grid containers nested inside grid items this deep are laid out as plain blocks (a diagnostic says so).</summary>
    private const int MaxGridDepth = 24;

    /// <summary>Subgrids nested this deep still share their parent's tracks; deeper ones become independent grids.</summary>
    private const int MaxSubgridDepth = 32;

    /// <summary>Grid items one container lays out; later children are not laid out (a diagnostic says so).</summary>
    private const int MaxGridItems = 1 << 20;

    private readonly GridPlacer _placer = new();
    private readonly GridTrackSizer _sizer = new();
    private GridLevel[] _gridLevels = new GridLevel[4];
    private int _gridDepth;
    private bool _gridReplacedForce;
    private double _absRepW;
    private double _absRepH;
    private long _gridWork;

    private struct GCtx
    {
        public int Box;
        public int Frag;
        public int Parent;
        public int FirstItem;
        public int ItemCount;
        public int FirstChild;
        public int NextSibling;
        public int AbsFirst;
        public int AbsCount;
        public bool Sub0;
        public bool Sub1;
        public bool Rtl;
        public int Owner0;
        public int Owner1;
        public int Off0;
        public int Off1;
        public int Span0;
        public int Span1;
        public int Count0;
        public int Count1;

        // Own gaps in px (NaN: normal, the parent's) and the effective gaps the lines actually have.
        public double Gap0;
        public double Gap1;
        public double Eff0;
        public double Eff1;

        // Margin + border + padding of the box on the start and end side of each axis.
        public double S0;
        public double E0;
        public double S1;
        public double E1;

        // Content box size and its offset inside the box's fragment.
        public double CW;
        public double CH;
        public double PadL;
        public double PadT;

        // For a subgrid: where its content box starts in the owner's coordinates, per axis.
        public double Org0;
        public double Org1;
        public double SizedH;
        public double MinCH;
        public double MaxCH;
    }

    private struct GItem
    {
        public int Box;
        public int Ctx;
        public int Child;
        public int Pos0;
        public int Span0;
        public int Pos1;
        public int Span1;
        public int O0;
        public int O1;

        // Extra margin from enclosing subgrids on each side of each axis.
        public double ES0;
        public double EE0;
        public double ES1;
        public double EE1;

        // Layout results: logical offset and border-box size in the owner's coordinates.
        public double X;
        public double Y;
        public double W;
        public double H;
        public double ML;
        public double MR;
        public double MT;
        public double MB;
        public double MeasH;
        public double MeasBase;
        public double Shim;
        public AlignKind J;
        public AlignKind A;
        public AlignOverflowKind JO;
        public AlignOverflowKind AO;
        public bool StretchW;
        public bool StretchH;
        public bool Baseline;
        public bool Replaced;
        public bool Measured;
    }

    private sealed class GridLevel
    {
        public GCtx[] Ctx = new GCtx[4];
        public int CtxCount;
        public GItem[] Item = new GItem[16];
        public GridPlaceItem[] Place = new GridPlaceItem[16];
        public int ItemCount;
        public GridAxis[] Axis = new GridAxis[8];
        public SizeItem[] Size = new SizeItem[16];
        public int[] SizeMap = new int[16];
        public int[] Stack = new int[16];
        public int[] Abs = new int[16];
        public int AbsCount;
        public long[] OrderKeys = new long[16];
        public int[] Group = new int[16];
        public double[] GroupBase = new double[16];
        public bool[] Used = new bool[16];

        public void Reset()
        {
            CtxCount = 0;
            ItemCount = 0;
            AbsCount = 0;
        }

        public GridAxis AxisOf(int ctx, int axis)
        {
            int i = 2 * ctx + axis;
            if (i >= Axis.Length)
            {
                Array.Resize(ref Axis, Math.Max(i + 1, Axis.Length * 2));
            }

            return Axis[i] ??= new GridAxis();
        }

        public int AddCtx()
        {
            if (CtxCount == Ctx.Length)
            {
                Array.Resize(ref Ctx, Ctx.Length * 2);
            }

            return CtxCount++;
        }

        public int AddItem()
        {
            if (ItemCount == Item.Length)
            {
                Array.Resize(ref Item, Item.Length * 2);
                Array.Resize(ref Place, Place.Length * 2);
            }

            return ItemCount++;
        }

        public void AddAbs(int box)
        {
            if (AbsCount == Abs.Length)
            {
                Array.Resize(ref Abs, Abs.Length * 2);
            }

            Abs[AbsCount++] = box;
        }
    }

    private GridLevel LevelAt(int depth)
    {
        if (depth >= _gridLevels.Length)
        {
            Array.Resize(ref _gridLevels, Math.Max(depth + 1, _gridLevels.Length * 2));
        }

        return _gridLevels[depth] ??= new GridLevel();
    }

    private void GridDiagnostic(string message) => _boxes.Styled.Diagnostics.Add(DiagnosticKind.LimitExceeded, message);

    // ---- entry ----

    /// <summary>
    /// Lays out the children of the grid container whose frame is <paramref name="ni"/> (CSS Grid 2 §12): places the items, sizes the
    /// columns, then the rows, and lays each item out in its grid area. Returns false when the grid is nested too deeply; the
    /// container is then laid out as a block.
    /// </summary>
    private bool GridOpen(int ni, Box c)
    {
        if (_gridDepth >= MaxGridDepth)
        {
            GridDiagnostic("grid containers nested deeper than " + MaxGridDepth.ToString(System.Globalization.CultureInfo.InvariantCulture) + " levels are laid out as blocks");
            return false;
        }

        GridLevel L = LevelAt(_gridDepth);
        _gridDepth++;
        try
        {
            L.Reset();
            double cw = _frames[ni].ContentWidth;
            double specH = _frames[ni].SpecH;
            int root = L.AddCtx();
            ref GCtx rc = ref L.Ctx[root];
            rc = default;
            rc.Box = c.Index;
            rc.Frag = _frames[ni].Frag;
            rc.Parent = -1;
            rc.FirstChild = -1;
            rc.NextSibling = -1;
            rc.Owner0 = rc.Owner1 = root;
            rc.CW = cw;
            rc.CH = specH;
            rc.MinCH = _frames[ni].MinH;
            rc.MaxCH = _frames[ni].MaxH;
            rc.Rtl = c.Values.Direction == DirectionKind.Rtl;
            rc.PadL = _frames[ni].ContentLeft;
            rc.PadT = _frames[ni].ContentTop;
            rc.Gap0 = rc.Gap1 = double.NaN;
            rc.Eff0 = ResolveGap(c.Values.ColumnGap, cw);
            rc.Eff1 = ResolveGap(c.Values.RowGap, specH);
            BuildGrid(L);
            SizeAllColumns(L);
            SizeAllRows(L);
            LayoutGridItems(L, ni);
            return true;
        }
        finally
        {
            _gridDepth--;
        }
    }

    private static double ResolveGap(Length g, double basis)
    {
        if (g.Kind is not (LengthKind.Px or LengthKind.Percent or LengthKind.Calc))
        {
            return 0;
        }

        double v = g.Kind == LengthKind.Px ? g.Value : double.IsNaN(basis) ? 0 : g.Resolve(basis);
        return double.IsNaN(v) || v < 0 ? 0 : v;
    }

    // ---- building: items, templates, placement ----

    private void BuildGrid(GridLevel L)
    {
        for (int k = 0; k < L.CtxCount; k++)
        {
            if ((++_counter & 63) == 0)
            {
                _token.ThrowIfCancellationRequested();
            }

            GatherItems(L, k);
            ExpandTemplates(L, k);
            PlaceItems(L, k);
            CreateSubgrids(L, k);
        }
    }

    private void GatherItems(GridLevel L, int k)
    {
        Box root = _boxes.Make(L.Ctx[k].Box);
        int first = L.ItemCount;
        int absFirst = L.AbsCount;
        bool anyOrder = false;
        for (Box ch = root.FirstChild; !ch.IsNull; ch = ch.NextSibling)
        {
            if (ch.Kind is not (BoxKind.Container or BoxKind.Replaced))
            {
                continue;
            }

            if ((ch.Flags & BoxTraits.OutOfFlow) != 0 && ch.Values.Position is PositionKind.Absolute or PositionKind.Fixed)
            {
                L.AddAbs(ch.Index);
                continue;
            }

            if (L.ItemCount - first >= MaxGridItems)
            {
                GridDiagnostic("a grid container has more than " + MaxGridItems.ToString(System.Globalization.CultureInfo.InvariantCulture) + " items; the rest are not laid out");
                break;
            }

            int i = L.AddItem();
            ref GItem it = ref L.Item[i];
            it = default;
            it.Box = ch.Index;
            it.Ctx = k;
            it.Child = -1;
            it.Replaced = ch.Kind == BoxKind.Replaced;
            anyOrder |= ch.Values.Order != 0;
        }

        ref GCtx cx = ref L.Ctx[k];
        cx.FirstItem = first;
        cx.ItemCount = L.ItemCount - first;
        cx.AbsFirst = absFirst;
        cx.AbsCount = L.AbsCount - absFirst;
        if (anyOrder && cx.ItemCount > 1)
        {
            // Grid 2 §6.3: items are placed and painted in order-modified document order (a stable sort by `order`).
            if (L.OrderKeys.Length < cx.ItemCount)
            {
                L.OrderKeys = new long[Math.Max(cx.ItemCount, L.OrderKeys.Length * 2)];
            }

            for (int i = 0; i < cx.ItemCount; i++)
            {
                L.OrderKeys[i] = ((long)_boxes.Make(L.Item[first + i].Box).Values.Order << 32) | (uint)i;
            }

            Array.Sort(L.OrderKeys, 0, cx.ItemCount);
            if (L.Size.Length < cx.ItemCount)
            {
                L.Size = new SizeItem[Math.Max(cx.ItemCount, L.Size.Length * 2)];
            }

            for (int i = 0; i < cx.ItemCount; i++)
            {
                // Reuse the size array's slots as a scratch copy of the boxes in sorted order.
                L.Size[i].Start = L.Item[first + (int)(L.OrderKeys[i] & 0xFFFFFFFF)].Box;
            }

            for (int i = 0; i < cx.ItemCount; i++)
            {
                L.Item[first + i].Box = L.Size[i].Start;
                L.Item[first + i].Replaced = _boxes.Make(L.Size[i].Start).Kind == BoxKind.Replaced;
            }
        }
    }

    private void ExpandTemplates(GridLevel L, int k)
    {
        ref GCtx cx = ref L.Ctx[k];
        Box b = _boxes.Make(cx.Box);
        ref readonly ComputedValues v = ref b.Values;
        for (int a = 0; a < 2; a++)
        {
            GridAxis ax = L.AxisOf(k, a);
            ax.Names.Clear();
            ax.ExpCount = 0;
            ax.AutoFrom = ax.AutoTo = 0;
            ax.AutoFit = false;
            GridTrackList list = a == 0 ? v.GridTemplateColumns : v.GridTemplateRows;
            bool sub = a == 0 ? cx.Sub0 : cx.Sub1;
            GridAreas areas = v.GridTemplateAreas;
            int areaCount = a == 0 ? areas.Columns : areas.Rows;
            if (sub)
            {
                int span = a == 0 ? cx.Span0 : cx.Span1;
                ax.Explicit = span;
                AddSubgridNames(L, k, a, ax, list, span);
                continue;
            }

            double size = a == 0 ? cx.CW : cx.CH;
            double max = a == 0 ? double.NaN : cx.MaxCH;
            double min = a == 0 ? double.NaN : cx.MinCH;
            if (a == 1 && double.IsNaN(size) && !double.IsInfinity(max) && !double.IsNaN(max))
            {
                size = max;
            }

            double gap = a == 0 ? cx.Eff0 : cx.Eff1;
            ExpandExplicit(list, ax, size, gap, a == 1 ? min : double.NaN);
            ax.Explicit = Math.Max(ax.ExpCount, Math.Min(areaCount, GridTrackList.MaxTracks));
            for (int ar = 0; ar < areas.Names.Length; ar++)
            {
                int s = a == 0 ? areas.ColumnStart[ar] - 1 : areas.RowStart[ar] - 1;
                int e = a == 0 ? areas.ColumnEnd[ar] - 1 : areas.RowEnd[ar] - 1;
                ax.Names.AddArea(areas.Names[ar], s, e);
            }
        }
    }

    /// <summary>Expands <c>repeat()</c> of a track list into the explicit tracks and line names of an axis (Grid 2 §7.2.3).</summary>
    private static void ExpandExplicit(GridTrackList list, GridAxis ax, double size, double gap, double minSize)
    {
        int h = list.Head.Length, kk = list.Auto.Length, t = list.Tail.Length;
        int reps = 0;
        if (kk > 0)
        {
            reps = AutoRepetitions(list, size, gap, minSize);
            reps = Math.Min(reps, Math.Max(1, (GridTrackList.MaxTracks - h - t) / kk));
        }

        int total = h + reps * kk + t;
        total = Math.Min(total, GridTrackList.MaxTracks);
        ax.EnsureExp(total + 1);
        int line = 0;

        void AddN(string[]? names)
        {
            if (names is null)
            {
                return;
            }

            foreach (string n in names)
            {
                ax.Names.Add(n, line);
            }
        }

        for (int i = 0; i < h && line < total; i++)
        {
            AddN(list.HeadNames[i]);
            ax.Exp[line++] = list.Head[i];
        }

        AddN(list.HeadNames[Math.Min(h, list.HeadNames.Length - 1)]);
        ax.AutoFrom = line;
        for (int j = 0; j < reps; j++)
        {
            AddN(list.AutoNames[0]);
            for (int m = 0; m < kk && line < total; m++)
            {
                ax.Exp[line++] = list.Auto[m];
                AddN(list.AutoNames[m + 1]);
            }
        }

        ax.AutoTo = line;
        ax.AutoFit = list.AutoFit && reps > 0;
        if (t > 0 || reps > 0)
        {
            AddN(list.TailNames[0]);
        }

        for (int m = 0; m < t && line < total; m++)
        {
            ax.Exp[line++] = list.Tail[m];
            AddN(list.TailNames[m + 1]);
        }

        ax.ExpCount = line;
    }

    /// <summary>Grid 2 §7.2.3.2: how many times an auto-fill / auto-fit repeat repeats.</summary>
    private static int AutoRepetitions(GridTrackList list, double size, double gap, double minSize)
    {
        int h = list.Head.Length, kk = list.Auto.Length, t = list.Tail.Length;
        double fixedSum = 0;
        for (int i = 0; i < h; i++)
        {
            fixedSum += RepeatTrackSize(in list.Head[i]);
        }

        for (int i = 0; i < t; i++)
        {
            fixedSum += RepeatTrackSize(in list.Tail[i]);
        }

        double autoSum = 0;
        for (int i = 0; i < kk; i++)
        {
            autoSum += Math.Max(1, RepeatTrackSize(in list.Auto[i]));
        }

        double per = Math.Max(1, autoSum + kk * gap);
        double avail = double.IsNaN(size) ? minSize : size;
        if (double.IsNaN(avail))
        {
            return 1;
        }

        double rest = avail - fixedSum - (h + t - 1) * gap;
        double r = double.IsNaN(size) ? Math.Ceiling(rest / per) : Math.Floor(rest / per);
        if (double.IsNaN(r) || r < 1)
        {
            return 1;
        }

        return (int)Math.Min(r, GridTrackList.MaxTracks);
    }

    // Each repeated track counts as its max track sizing function if that is definite, else its min; percentages are not definite here.
    private static double RepeatTrackSize(in GridTrackSize t)
    {
        double max = t.Max.Kind == GridBreadthKind.Length && t.Max.Length.Kind == LengthKind.Px ? t.Max.Length.Value : double.NaN;
        double min = t.Min.Kind == GridBreadthKind.Length && t.Min.Length.Kind == LengthKind.Px ? t.Min.Length.Value : double.NaN;
        if (!double.IsNaN(max))
        {
            return double.IsNaN(min) ? max : Math.Max(max, min);
        }

        return double.IsNaN(min) ? 0 : min;
    }

    /// <summary>Line names of a subgrid axis: the subgrid's own names, then the parent's names on the spanned lines (Grid 2 §9).</summary>
    private void AddSubgridNames(GridLevel L, int k, int a, GridAxis ax, GridTrackList list, int span)
    {
        // Own names: one set per line, starting with line 1; an auto-fill repeat of names fills up to the span.
        int h = list.HeadNames.Length, t = list.TailNames.Length, ac = list.AutoNames.Length;
        int lines = span + 1;
        int line = 0;
        for (int i = 0; i < h && line < lines; i++, line++)
        {
            AddNameSet(ax, list.HeadNames[i], line);
        }

        if (list.HasAutoRepeat && ac > 0)
        {
            int fill = Math.Max(0, lines - h - t);
            int reps = fill / ac;
            for (int j = 0; j < reps; j++)
            {
                for (int m = 0; m < ac; m++, line++)
                {
                    AddNameSet(ax, list.AutoNames[m], line);
                }
            }
        }

        int tailStart = Math.Max(line, Math.Max(0, lines - t));
        line = tailStart;
        for (int i = 0; i < t && line < lines; i++, line++)
        {
            AddNameSet(ax, list.TailNames[i], line);
        }

        int parent = L.Ctx[k].Parent;
        if (parent < 0)
        {
            return;
        }

        int off = (a == 0 ? L.Ctx[k].Off0 : L.Ctx[k].Off1) - (a == 0 ? L.Ctx[parent].Off0 : L.Ctx[parent].Off1);
        ref GCtx pc = ref L.Ctx[parent];
        GridAxis pax = L.AxisOf((a == 0 ? pc.Owner0 : pc.Owner1), a);
        int pOff = a == 0 ? pc.Off0 : pc.Off1;
        _ = pOff;
        // The parent's names, in its own line numbers, restricted to the lines this subgrid covers.
        GridNameIndex src = ParentNames(L, parent, a);
        int itemStartInParent = off;
        _ = pax;
        src.CopyRange(ax.Names, itemStartInParent, itemStartInParent + span);
    }

    private GridNameIndex ParentNames(GridLevel L, int parent, int a) => L.AxisOf(parent, a).Names;

    private static void AddNameSet(GridAxis ax, string[]? names, int line)
    {
        if (names is null)
        {
            return;
        }

        foreach (string n in names)
        {
            ax.Names.Add(n, line);
        }
    }

    private void PlaceItems(GridLevel L, int k)
    {
        ref GCtx cx = ref L.Ctx[k];
        Box cb = _boxes.Make(cx.Box);
        ref readonly ComputedValues cv = ref cb.Values;
        int n = cx.ItemCount;
        for (int i = 0; i < n; i++)
        {
            int ii = cx.FirstItem + i;
            Box ib = _boxes.Make(L.Item[ii].Box);
            ref readonly ComputedValues iv = ref ib.Values;
            ref GridPlaceItem pl = ref L.Place[ii];
            pl = default;
            pl.ColStart = iv.GridColumnStart;
            pl.ColEnd = iv.GridColumnEnd;
            pl.RowStart = iv.GridRowStart;
            pl.RowEnd = iv.GridRowEnd;
            if (ib.Kind == BoxKind.Container && ib.Context == FormattingContext.Grid && SubgridDepth(L, k) < MaxSubgridDepth)
            {
                // Grid 2 §9: a subgrid with an automatic span spans as many tracks as its line-name list names, at least one.
                if (iv.GridTemplateColumns.IsSubgrid)
                {
                    AutoSubgridSpan(ref pl.ColStart, ref pl.ColEnd, iv.GridTemplateColumns);
                }

                if (iv.GridTemplateRows.IsSubgrid)
                {
                    AutoSubgridSpan(ref pl.RowStart, ref pl.RowEnd, iv.GridTemplateRows);
                }
            }
        }

        GridAxis a0 = L.AxisOf(k, 0), a1 = L.AxisOf(k, 1);
        var p = new GridPlaceParams
        {
            Explicit0 = a0.Explicit,
            Explicit1 = a1.Explicit,
            Sub0 = cx.Sub0,
            Sub1 = cx.Sub1,
            ColumnFlow = cv.GridAutoFlow == GridAutoFlowKind.Column,
            Dense = cv.GridAutoFlowDense,
        };
        _placer.Place(L.Place.AsSpan(cx.FirstItem, n), in p, a0.Names, a1.Names);
        if (_placer.HitLimit)
        {
            GridDiagnostic("grid auto-placement reached its work or size limit; the remaining items are stacked");
        }

        cx.Count0 = cx.Sub0 ? cx.Span0 : _placer.Count0;
        cx.Count1 = cx.Sub1 ? cx.Span1 : _placer.Count1;
        a0.Before = cx.Sub0 ? 0 : _placer.Before0;
        a1.Before = cx.Sub1 ? 0 : _placer.Before1;
        for (int i = 0; i < n; i++)
        {
            int ii = cx.FirstItem + i;
            ref GItem it = ref L.Item[ii];
            ref GridPlaceItem pl = ref L.Place[ii];
            it.Pos0 = pl.Pos0;
            it.Span0 = pl.Span0;
            it.Pos1 = pl.Pos1;
            it.Span1 = pl.Span1;
            it.O0 = (cx.Sub0 ? cx.Off0 : 0) + it.Pos0;
            it.O1 = (cx.Sub1 ? cx.Off1 : 0) + it.Pos1;
        }
    }

    private static void AutoSubgridSpan(ref GridLine start, ref GridLine end, GridTrackList list)
    {
        if (start.Kind == GridLineKind.Span || end.Kind == GridLineKind.Span || (start.Kind != GridLineKind.Auto && end.Kind != GridLineKind.Auto))
        {
            return;
        }

        int lines = list.HeadNames.Length + (list.HasAutoRepeat ? 0 : list.TailNames.Length);
        int span = Math.Max(1, lines - 1);
        if (start.Kind == GridLineKind.Auto)
        {
            start = new GridLine(GridLineKind.Span, span, null);
        }
        else
        {
            end = new GridLine(GridLineKind.Span, span, null);
        }
    }

    private int SubgridDepth(GridLevel L, int k)
    {
        int d = 0;
        for (int c = k; c > 0 && d <= MaxSubgridDepth; c = L.Ctx[c].Parent)
        {
            d++;
        }

        return d;
    }

    private void CreateSubgrids(GridLevel L, int k)
    {
        ref GCtx pc = ref L.Ctx[k];
        int first = pc.FirstItem, n = pc.ItemCount;
        int lastChild = -1;
        for (int i = 0; i < n; i++)
        {
            int ii = first + i;
            Box ib = _boxes.Make(L.Item[ii].Box);
            if (ib.Kind != BoxKind.Container || ib.Context != FormattingContext.Grid || SubgridDepth(L, k) >= MaxSubgridDepth)
            {
                continue;
            }

            ref readonly ComputedValues iv = ref ib.Values;
            bool s0 = iv.GridTemplateColumns.IsSubgrid, s1 = iv.GridTemplateRows.IsSubgrid;
            if (!s0 && !s1)
            {
                continue;
            }

            int m = L.AddCtx();
            ref GCtx parent = ref L.Ctx[k];
            ref GCtx cx = ref L.Ctx[m];
            cx = default;
            ref GItem it = ref L.Item[ii];
            it.Child = m;
            cx.Box = ib.Index;
            cx.Parent = k;
            cx.FirstChild = -1;
            cx.NextSibling = -1;
            if (lastChild < 0)
            {
                parent.FirstChild = m;
            }
            else
            {
                L.Ctx[lastChild].NextSibling = m;
            }

            lastChild = m;
            cx.Sub0 = s0;
            cx.Sub1 = s1;
            cx.Rtl = iv.Direction == DirectionKind.Rtl;
            cx.Owner0 = s0 ? parent.Owner0 : m;
            cx.Owner1 = s1 ? parent.Owner1 : m;
            cx.Off0 = s0 ? it.O0 : 0;
            cx.Off1 = s1 ? it.O1 : 0;
            cx.Span0 = s0 ? it.Span0 : 0;
            cx.Span1 = s1 ? it.Span1 : 0;
            cx.Gap0 = iv.ColumnGap.Kind == LengthKind.Normal ? double.NaN : ResolveGap(iv.ColumnGap, parent.CW);
            cx.Gap1 = iv.RowGap.Kind == LengthKind.Normal ? double.NaN : ResolveGap(iv.RowGap, parent.CH);
            cx.Eff0 = s0 ? (double.IsNaN(cx.Gap0) ? parent.Eff0 : cx.Gap0) : ResolveGap(iv.ColumnGap, double.NaN);
            cx.Eff1 = s1 ? (double.IsNaN(cx.Gap1) ? parent.Eff1 : cx.Gap1) : ResolveGap(iv.RowGap, double.NaN);
            double basis = parent.CW;
            cx.S0 = MarginPx(iv.MarginLeft, basis) + iv.BorderLeftWidth + Pad(iv.PaddingLeft, basis);
            cx.E0 = MarginPx(iv.MarginRight, basis) + iv.BorderRightWidth + Pad(iv.PaddingRight, basis);
            cx.S1 = MarginPx(iv.MarginTop, basis) + iv.BorderTopWidth + Pad(iv.PaddingTop, basis);
            cx.E1 = MarginPx(iv.MarginBottom, basis) + iv.BorderBottomWidth + Pad(iv.PaddingBottom, basis);
            cx.PadL = iv.BorderLeftWidth + Pad(iv.PaddingLeft, basis);
            cx.PadT = iv.BorderTopWidth + Pad(iv.PaddingTop, basis);
            cx.CW = double.NaN;
            cx.CH = double.NaN;
            cx.MinCH = 0;
            cx.MaxCH = double.PositiveInfinity;
        }
    }
}
