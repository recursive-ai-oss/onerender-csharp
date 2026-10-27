using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    /// <summary>
    /// Builds the grid of a table about to be opened and settles its border and padding (CSS2 §17.6.2: a collapsing table has half of
    /// the outer collapsed borders as its border and no padding). Returns null when tables nest too deeply; the box is then laid out as a block.
    /// </summary>
    private TableLevel? TablePrepare(Box table, double basis)
    {
        TableLevel? L = AcquireTable();
        if (L is null)
        {
            return null;
        }

        BuildTable(L, table);
        PrepareTableBorders(L, table, basis);
        return L;
    }

    /// <summary>Lays out the rows, cells and captions of the table whose frame is at <paramref name="ni"/> (CSS2 §17.5).</summary>
    private void TableOpen(int ni, Box table, TableLevel L, double basis)
    {
        try
        {
            TableLayoutAll(ni, table, L, basis);
        }
        finally
        {
            ReleaseTable();
        }
    }

    private void TableLayoutAll(int ni, Box table, TableLevel L, double basis)
    {
        ref readonly ComputedValues tv = ref table.Values;
        double borderW = _frames[ni].W;
        double contentW = _frames[ni].ContentWidth;
        double originX = _frames[ni].ContentLeft;
        double originY = _frames[ni].ContentTop;
        double specH = _frames[ni].SpecH;
        double minH = _frames[ni].MinH;
        int tableFrag = _frames[ni].Frag;
        var tableFragment = new Fragment(_out, tableFrag);
        bool fixedLayout = IsFixedLayout(in tv);
        int C = L.ColumnCount, R = L.RowCount;
        AnalyzeColumns(L, table, basis);
        AssignColumnWidths(L, contentW, fixedLayout);

        int visibleCols = 0;
        double gridW = 0;
        for (int j = 0; j < C; j++)
        {
            if (!L.Columns[j].Collapsed)
            {
                visibleCols++;
                gridW += L.Columns[j].Width;
            }
        }

        double x = originX + (visibleCols > 0 ? L.SpacingH : 0);
        double rowX = x;
        for (int j = 0; j < C; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            if (col.Collapsed)
            {
                col.X = x;
                continue;
            }

            col.X = x;
            x += col.Width + L.SpacingH;
        }

        double rowW = visibleCols > 0 ? gridW + (L.SpacingH * (visibleCols - 1)) : 0;

        // Top captions come first (CSS2 §17.4.1).
        double y = 0;
        for (int i = 0; i < L.CaptionCount; i++)
        {
            Box cap = _boxes.Make(L.Captions[i]);
            if (cap.Values.CaptionSide == CaptionSideKind.Top)
            {
                y = PlaceCaption(ni, tableFragment, cap, borderW, originY, y);
            }
        }

        double gridTop = y;
        int visibleRows = 0;
        for (int r = 0; r < R; r++)
        {
            if (!L.Rows[r].Collapsed)
            {
                visibleRows++;
            }
        }

        // The six layers (CSS2 §17.5.1) paint in tree order: column groups and columns first, then row groups, rows and cells.
        CreateColumnFragments(L, tableFragment);

        // Rows, cells: lay each cell out at its column width to learn its height.
        int curGroup = -2;
        Fragment groupFrag = default;
        for (int r = 0; r < R; r++)
        {
            if ((r & 63) == 63)
            {
                _token.ThrowIfCancellationRequested();
            }

            ref TableRow row = ref L.Rows[r];
            if (row.Collapsed)
            {
                row.Height = 0;
                continue;
            }

            if (row.Group != curGroup)
            {
                curGroup = row.Group;
                groupFrag = default;
                if (curGroup >= 0)
                {
                    groupFrag = _out.AddBox(tableFragment, _boxes.Make(L.Groups[curGroup].Box), new Rect(0, 0, 0, 0));
                    L.Groups[curGroup].Frag = groupFrag.Index;
                }
            }

            Fragment rowParent = groupFrag.IsNull ? tableFragment : groupFrag;
            Fragment rowFrag = _out.AddBox(rowParent, _boxes.Make(row.Box), new Rect(0, 0, 0, 0));
            row.Frag = rowFrag.Index;
            double h = 0, asc = 0, below = 0;
            for (int k = 0; k < row.CellCount; k++)
            {
                ref TableCell cell = ref L.Cells[row.FirstCell + k];
                if (cell.Skip)
                {
                    continue;
                }

                MeasureCell(L, ref cell, rowFrag, contentW);
                if (cell.Frag < 0)
                {
                    continue;
                }

                if (cell.RowSpan == 1)
                {
                    if (IsBaselineAligned(_boxes.Make(cell.Box).Values.VerticalAlign))
                    {
                        asc = Math.Max(asc, cell.Baseline);
                        below = Math.Max(below, cell.Height - cell.Baseline);
                    }
                    else
                    {
                        h = Math.Max(h, cell.Height);
                    }
                }
            }

            row.Ascent = asc;
            row.Height = Math.Max(Math.Max(double.IsNaN(row.Spec) ? 0 : row.Spec, h), asc + below);
        }

        // Cells that span rows raise the rows they cover when they are taller than them (CSS2 §17.5.3).
        for (int i = 0; i < L.CellCount; i++)
        {
            ref TableCell cell = ref L.Cells[i];
            if (cell.Skip || cell.Frag < 0 || cell.RowSpan == 1)
            {
                continue;
            }

            int n = 0;
            double sum = 0;
            for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
            {
                if (!L.Rows[r].Collapsed)
                {
                    n++;
                    sum += L.Rows[r].Height;
                }
            }

            if (n == 0)
            {
                continue;
            }

            double need = cell.Height - (sum + (L.SpacingV * (n - 1)));
            if (need > 0)
            {
                for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
                {
                    if (!L.Rows[r].Collapsed)
                    {
                        L.Rows[r].Height += need / n;
                    }
                }
            }
        }

        double rowsTotal = 0;
        for (int r = 0; r < R; r++)
        {
            if (!L.Rows[r].Collapsed)
            {
                rowsTotal += L.Rows[r].Height;
            }
        }

        rowsTotal += visibleRows > 0 ? L.SpacingV * (visibleRows + 1) : 0;

        // A specified height (or min-height) is a minimum for the grid: the extra goes to the rows in proportion to their heights.
        double target = rowsTotal;
        if (!double.IsNaN(specH))
        {
            target = Math.Max(target, specH);
        }

        target = Math.Max(target, minH);
        if (target > rowsTotal && visibleRows > 0)
        {
            double extra = target - rowsTotal;
            double heights = rowsTotal - (L.SpacingV * (visibleRows + 1));
            for (int r = 0; r < R; r++)
            {
                if (!L.Rows[r].Collapsed)
                {
                    L.Rows[r].Height += heights > 0 ? extra * L.Rows[r].Height / heights : extra / visibleRows;
                }
            }

            rowsTotal = target;
        }

        // Positions.
        double ry = gridTop + (visibleRows > 0 ? L.SpacingV : 0);
        for (int r = 0; r < R; r++)
        {
            ref TableRow row = ref L.Rows[r];
            row.Y = ry;
            if (!row.Collapsed)
            {
                ry += row.Height + L.SpacingV;
            }
        }

        double ovL = 0, ovT = 0, ovR = borderW, ovB = 0;
        double firstBaseline = double.NaN;
        for (int g = 0; g < L.GroupCount; g++)
        {
            ref TableGroup grp = ref L.Groups[g];
            if (grp.Frag < 0)
            {
                continue;
            }

            int first = -1, last = -1;
            for (int r = grp.FirstRow; r < grp.FirstRow + grp.RowCount; r++)
            {
                if (!L.Rows[r].Collapsed)
                {
                    if (first < 0)
                    {
                        first = r;
                    }

                    last = r;
                }
            }

            if (first < 0)
            {
                continue;
            }

            double gy = L.Rows[first].Y;
            double gh = L.Rows[last].Y + L.Rows[last].Height - gy;
            _out.SetBorderBox(new Fragment(_out, grp.Frag), new Rect(rowX, originY + gy, rowW, gh));
        }

        for (int r = 0; r < R; r++)
        {
            ref TableRow row = ref L.Rows[r];
            if (row.Collapsed || row.Frag < 0)
            {
                continue;
            }

            double px = 0, py = 0;
            if (row.Group >= 0 && L.Groups[row.Group].Frag >= 0)
            {
                Rect gb = _out.BorderBoxOf(L.Groups[row.Group].Frag);
                px = gb.X;
                py = gb.Y;
            }

            double rowAbsY = originY + row.Y;
            _out.SetBorderBox(new Fragment(_out, row.Frag), new Rect(rowX - px, rowAbsY - py, rowW, row.Height));
            for (int k = 0; k < row.CellCount; k++)
            {
                ref TableCell cell = ref L.Cells[row.FirstCell + k];
                if (cell.Skip || cell.Frag < 0)
                {
                    continue;
                }

                PlaceCell(L, ref cell, rowX, rowAbsY, originY, ref ovL, ref ovT, ref ovR, ref ovB);
                if (double.IsNaN(firstBaseline) && r == FirstVisibleRow(L))
                {
                    double bl = row.Ascent > 0 ? row.Ascent : cell.Baseline;
                    if (!double.IsNaN(bl))
                    {
                        firstBaseline = originY + row.Y + bl;
                    }
                }
            }
        }

        FinishColumnFragments(L, originY, gridTop, rowsTotal);

        double bottom = gridTop + rowsTotal;
        for (int i = 0; i < L.CaptionCount; i++)
        {
            Box cap = _boxes.Make(L.Captions[i]);
            if (cap.Values.CaptionSide == CaptionSideKind.Bottom)
            {
                bottom = PlaceCaption(ni, tableFragment, cap, borderW, originY, bottom);
            }
        }

        if (L.Collapse)
        {
            _out.SetTableBorders(tableFrag, CollapsedPaint(in L.Tt, in L.Tr, in L.Tb, in L.Tl));
        }

        ref Frame f = ref _frames[ni];
        f.Cursor = bottom;
        f.NextChild = -1;
        f.SpecH = double.NaN;
        f.MinH = 0;
        f.MaxH = double.PositiveInfinity;
        f.Flags |= FrameFlags.HasContent | FrameFlags.AnyChild;
        f.FirstBaseline = firstBaseline;
        f.OvL = Math.Min(f.OvL, ovL);
        f.OvT = Math.Min(f.OvT, ovT);
        f.OvR = Math.Max(f.OvR, ovR);
        f.OvB = Math.Max(f.OvB, ovB);
    }

    private TableBorderPaint CollapsedPaint(in TableEdge top, in TableEdge right, in TableEdge bottom, in TableEdge left)
    {
        var p = default(TableBorderPaint);
        p.StyleTop = EdgeStyle(in top);
        p.StyleRight = EdgeStyle(in right);
        p.StyleBottom = EdgeStyle(in bottom);
        p.StyleLeft = EdgeStyle(in left);
        p.ColorTop = EdgeColor(in top);
        p.ColorRight = EdgeColor(in right);
        p.ColorBottom = EdgeColor(in bottom);
        p.ColorLeft = EdgeColor(in left);
        return p;
    }

    private static BorderStyleKind EdgeStyle(in TableEdge e) => e.Prio == 0 ? BorderStyleKind.None : (BorderStyleKind)e.Style;

    private ColorValue EdgeColor(in TableEdge e)
    {
        if (e.Prio == 0)
        {
            return ColorValue.Transparent;
        }

        ref readonly ComputedValues v = ref _boxes.Make(e.Box).Values;
        ColorValue c = e.Side switch { 0 => v.BorderTopColor, 1 => v.BorderRightColor, 2 => v.BorderBottomColor, _ => v.BorderLeftColor };
        return c.Resolve(v.Color);
    }

    private static int FirstVisibleRow(TableLevel L)
    {
        for (int r = 0; r < L.RowCount; r++)
        {
            if (!L.Rows[r].Collapsed)
            {
                return r;
            }
        }

        return -1;
    }

    private static bool IsBaselineAligned(VerticalAlignKind k) => k is not (VerticalAlignKind.Top or VerticalAlignKind.Bottom or VerticalAlignKind.Middle);

    private double PlaceCaption(int ni, Fragment tableFragment, Box cap, double tableBorderW, double originY, double y)
    {
        Fragment cf = RunIndependent(cap, tableFragment, tableBorderW, double.NaN, out Insets m);
        if (cf.IsNull)
        {
            return y;
        }

        Rect r = _out.BorderBoxOf(cf.Index);
        y += m.Top;
        _out.SetBorderBox(cf, new Rect(m.Left, originY + y, r.Width, r.Height));
        Rect ov = _out.OverflowOf(cf.Index);
        ref Frame f = ref _frames[ni];
        f.OvL = Math.Min(f.OvL, m.Left + Math.Min(0, ov.X));
        f.OvR = Math.Max(f.OvR, m.Left + Math.Max(r.Width, ov.X + ov.Width));
        f.OvB = Math.Max(f.OvB, originY + y + Math.Max(r.Height, ov.Y + ov.Height));
        return y + r.Height + m.Bottom;
    }

    private void MeasureCell(TableLevel L, ref TableCell cell, Fragment rowFrag, double tableContentW)
    {
        double w = 0;
        int n = 0;
        for (int j = cell.Col; j < cell.Col + cell.ColSpan; j++)
        {
            if (!L.Columns[j].Collapsed)
            {
                w += L.Columns[j].Width;
                n++;
            }
        }

        if (n == 0)
        {
            cell.Frag = -1;
            return;
        }

        w += L.SpacingH * (n - 1);
        cell.Width = w;
        _tcActive = true;
        _tcT = cell.Bt;
        _tcR = cell.Br;
        _tcB = cell.Bb;
        _tcL = cell.Bl;
        _shrinkNext = false;
        _absForce = true;
        _absW = w;
        _absMl = 0;
        _absMr = 0;
        _absH = double.NaN;
        Box cb = _boxes.Make(cell.Box);
        int savedAbs = _absCount;
        Fragment f = RunIndependent(cb, rowFrag, tableContentW, double.NaN, out _);
        _absForce = false;
        _tcActive = false;
        _ = savedAbs;
        if (f.IsNull)
        {
            cell.Frag = -1;
            return;
        }

        cell.Frag = f.Index;
        Rect r = _out.BorderBoxOf(f.Index);
        cell.Height = r.Height;
        double bl = _out.BaselineOf(f.Index);
        if (double.IsNaN(bl))
        {
            // CSS2 §17.5.3: a cell without a line box has its baseline at the bottom of its content.
            bl = r.Height - cell.Bb - Pad(cb.Values.PaddingBottom, tableContentW);
        }

        cell.Baseline = bl;
    }

    private void PlaceCell(TableLevel L, ref TableCell cell, double rowX, double rowAbsY, double originY, ref double ovL, ref double ovT, ref double ovR, ref double ovB)
    {
        double h = 0;
        int n = 0;
        for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
        {
            if (!L.Rows[r].Collapsed)
            {
                h += L.Rows[r].Height;
                n++;
            }
        }

        h += L.SpacingV * Math.Max(0, n - 1);
        var frag = new Fragment(_out, cell.Frag);
        if (L.Collapse)
        {
            _out.SetTableBorders(cell.Frag, CollapsedPaint(in cell.Et, in cell.Er, in cell.Eb, in cell.El));
        }

        Rect old = _out.BorderBoxOf(cell.Frag);
        double cx = L.Columns[cell.Col].X;
        _out.SetBorderBox(frag, new Rect(cx - rowX, 0, cell.Width, h));

        // CSS2 §17.5.3: the cell's content moves within the cell according to vertical-align.
        ref readonly ComputedValues cv = ref _boxes.Make(cell.Box).Values;
        double dy = 0;
        double used = cell.Height;
        if (cv.VerticalAlign is VerticalAlignKind.Bottom or VerticalAlignKind.Middle)
        {
            // A cell's own height is a minimum (CSS2 §17.5.3): the content to align is what its children fill, plus the bottom padding and border.
            double inner = 0;
            for (int c = _out.FirstChildOf(cell.Frag); c >= 0; c = _out.NextSiblingOf(c))
            {
                Rect cb = _out.BorderBoxOf(c);
                inner = Math.Max(inner, cb.Y + cb.Height);
            }

            used = Math.Min(used, inner + cell.Bb + Pad(cv.PaddingBottom, cell.Width));
        }

        switch (cv.VerticalAlign)
        {
            case VerticalAlignKind.Top:
                break;
            case VerticalAlignKind.Bottom:
                dy = h - used;
                break;
            case VerticalAlignKind.Middle:
                dy = (h - used) / 2;
                break;
            default:
                if (cell.RowSpan == 1)
                {
                    dy = L.Rows[cell.Row].Ascent - cell.Baseline;
                }

                break;
        }

        if (dy > 0 && !double.IsNaN(dy))
        {
            for (int c = _out.FirstChildOf(cell.Frag); c >= 0; c = _out.NextSiblingOf(c))
            {
                Rect cb = _out.BorderBoxOf(c);
                _out.SetBorderBox(new Fragment(_out, c), new Rect(cb.X, cb.Y + dy, cb.Width, cb.Height));
            }

            double bl = _out.BaselineOf(cell.Frag);
            if (!double.IsNaN(bl))
            {
                _out.SetBaseline(frag, bl + dy);
            }
        }

        Rect ov = _out.OverflowOf(cell.Frag);
        double top = Math.Min(0, ov.Y);
        double bottom = Math.Max(h, ov.Y + ov.Height + (dy > 0 ? dy : 0));
        _out.SetScrollableOverflow(frag, new Rect(ov.X, top, ov.Width, bottom - top));
        _ = old;
        double ax = cx, ay = rowAbsY;
        ovL = Math.Min(ovL, ax + Math.Min(0, ov.X));
        ovT = Math.Min(ovT, ay + top);
        ovR = Math.Max(ovR, ax + Math.Max(cell.Width, ov.X + ov.Width));
        ovB = Math.Max(ovB, ay + bottom);
        _ = originY;
    }

    // ---- column and column group fragments, only for backgrounds (CSS2 §17.5.1 layers 2 and 3) ----

    private void CreateColumnFragments(TableLevel L, Fragment tableFragment)
    {
        int lastGroup = -1;
        Fragment groupFrag = default;
        for (int j = 0; j < L.ColumnCount; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            if (col.Collapsed)
            {
                continue;
            }

            if (col.GroupBox != lastGroup)
            {
                lastGroup = col.GroupBox;
                groupFrag = default;
                if (lastGroup >= 0 && HasBackground(_boxes.Make(lastGroup)))
                {
                    groupFrag = _out.AddBox(tableFragment, _boxes.Make(lastGroup), new Rect(0, 0, 0, 0));
                }
            }

            col.GroupFrag = groupFrag.IsNull ? -1 : groupFrag.Index;
            if (col.Box >= 0 && HasBackground(_boxes.Make(col.Box)))
            {
                col.Frag = _out.AddBox(groupFrag.IsNull ? tableFragment : groupFrag, _boxes.Make(col.Box), new Rect(0, 0, 0, 0)).Index;
            }
        }
    }

    private static bool HasBackground(Box b) => b.Values.BackgroundColor.A > 0 || b.Values.BackgroundColor.IsCurrentColor;

    private void FinishColumnFragments(TableLevel L, double originY, double gridTop, double rowsTotal)
    {
        double top = originY + gridTop;
        int j = 0;
        while (j < L.ColumnCount)
        {
            if (L.Columns[j].Collapsed)
            {
                j++;
                continue;
            }

            int groupFrag = L.Columns[j].GroupFrag;
            int groupBox = L.Columns[j].GroupBox;
            int k = j;
            double gx0 = L.Columns[j].X, gx1 = gx0;
            while (k < L.ColumnCount && L.Columns[k].GroupBox == groupBox)
            {
                if (!L.Columns[k].Collapsed)
                {
                    gx1 = L.Columns[k].X + L.Columns[k].Width;
                }

                k++;
            }

            if (groupFrag >= 0)
            {
                _out.SetBorderBox(new Fragment(_out, groupFrag), new Rect(gx0, top, gx1 - gx0, rowsTotal));
            }

            for (int m = j; m < k; m++)
            {
                ref TableColumn c2 = ref L.Columns[m];
                if (c2.Frag >= 0 && !c2.Collapsed)
                {
                    double px = groupFrag >= 0 ? gx0 : 0, py = groupFrag >= 0 ? top : 0;
                    _out.SetBorderBox(new Fragment(_out, c2.Frag), new Rect(c2.X - px, top - py, c2.Width, rowsTotal));
                }
            }

            j = k;
        }
    }
}
