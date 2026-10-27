using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    /// <summary>Tables nested deeper than this are laid out as plain blocks.</summary>
    private const int MaxTableDepth = 16;

    /// <summary>Most columns one table has; cells that would start beyond are dropped (HTML caps a column span at 1000 as well).</summary>
    private const int MaxTableColumns = 1000;

    /// <summary>Most grid slots (rows times columns) a collapsed-border table resolves; larger tables lay out with separate borders.</summary>
    private const int MaxCollapsedSlots = 1 << 22;

    private TableLevel[] _tableLevels = new TableLevel[4];
    private int _tableDepth;
    private bool _tableDepthReported;

    // The cell currently being laid out: its borders override the style's, and its margins are zero (CSS2 §17.5: cells have no margins).
    private bool _tcActive;
    private double _tcT, _tcR, _tcB, _tcL;

    private TableLevel? AcquireTable()
    {
        if (_tableDepth >= MaxTableDepth)
        {
            if (!_tableDepthReported)
            {
                _tableDepthReported = true;
                _boxes.Styled.Diagnostics.Add(DiagnosticKind.LimitExceeded, "tables nested deeper than " + MaxTableDepth.ToString(System.Globalization.CultureInfo.InvariantCulture) + " levels are laid out as blocks");
            }

            return null;
        }

        if (_tableDepth == _tableLevels.Length)
        {
            Array.Resize(ref _tableLevels, _tableLevels.Length * 2);
        }

        TableLevel level = _tableLevels[_tableDepth] ??= new TableLevel();
        _tableDepth++;
        level.Reset();
        return level;
    }

    private void ReleaseTable() => _tableDepth--;

    /// <summary>Collects captions, columns and rows of a table box and places its cells on the grid (HTML Standard §4.9.12, CSS2 §17.5).</summary>
    private void BuildTable(TableLevel L, Box table)
    {
        ref readonly ComputedValues tv = ref table.Values;
        L.BoxIndex = table.Index;
        L.Collapse = tv.BorderCollapse == BorderCollapseKind.Collapse;
        L.SpacingH = L.Collapse ? 0 : tv.BorderSpacingH;
        L.SpacingV = L.Collapse ? 0 : tv.BorderSpacingV;
        for (Box ch = table.FirstChild; !ch.IsNull; ch = ch.NextSibling)
        {
            if (ch.Kind != BoxKind.Container)
            {
                continue;
            }

            switch (ch.Context)
            {
                case FormattingContext.TableCaption:
                    L.AddCaption(ch.Index);
                    break;
                case FormattingContext.TableColumnGroup:
                    AddColumnGroup(L, ch);
                    break;
                case FormattingContext.TableColumn:
                    AddColumns(L, ch, -1);
                    break;
                case FormattingContext.TableRowGroup:
                case FormattingContext.TableHeaderGroup:
                case FormattingContext.TableFooterGroup:
                {
                    ref TableUnit u = ref L.AddUnit();
                    u.Box = ch.Index;
                    u.IsGroup = true;
                    u.Order = ch.Context == FormattingContext.TableHeaderGroup ? 0 : ch.Context == FormattingContext.TableFooterGroup ? 2 : 1;
                    break;
                }

                case FormattingContext.TableRow:
                {
                    ref TableUnit u = ref L.AddUnit();
                    u.Box = ch.Index;
                    u.IsGroup = false;
                    u.Order = 1;
                    break;
                }
            }
        }

        // CSS2 §17.5.1: the header group comes before all other rows and the footer group after them, wherever they are in the source.
        for (int order = 0; order < 3; order++)
        {
            for (int i = 0; i < L.UnitCount; i++)
            {
                if (L.Units[i].Order != order)
                {
                    continue;
                }

                if ((++_counter & 255) == 0)
                {
                    _token.ThrowIfCancellationRequested();
                }

                Box ub = _boxes.Make(L.Units[i].Box);
                if (L.Units[i].IsGroup)
                {
                    PlaceGroup(L, ub);
                }
                else
                {
                    PlaceRow(L, ub, -1, int.MaxValue);
                }
            }
        }

        // A row span that was unclipped (rows outside a row group, or span 0) now stops at the last row.
        for (int i = 0; i < L.CellCount; i++)
        {
            ref TableCell c = ref L.Cells[i];
            int limit = c.Limit == int.MaxValue ? L.RowCount : c.Limit;
            c.RowSpan = Math.Max(1, Math.Min(c.RowSpan <= 0 ? limit - c.Row : c.RowSpan, limit - c.Row));
        }
    }

    private void AddColumnGroup(TableLevel L, Box group)
    {
        bool any = false;
        for (Box ch = group.FirstChild; !ch.IsNull; ch = ch.NextSibling)
        {
            if (ch.Kind == BoxKind.Container && ch.Context == FormattingContext.TableColumn)
            {
                AddColumns(L, ch, group.Index);
                any = true;
            }
        }

        if (!any)
        {
            // A column group without column children stands for as many columns as its span says.
            int span = group.Node.IsNull ? 1 : _boxes.Styled.ColumnSpanOf(group.Node.Index);
            for (int i = 0; i < span && L.ColumnCount < MaxTableColumns; i++)
            {
                ref TableColumn col = ref L.AddColumn();
                col.GroupBox = group.Index;
                col.Collapsed = group.Values.Visibility == VisibilityKind.Collapse;
                ApplyColumnWidth(ref col, group.Values);
            }
        }
    }

    private void AddColumns(TableLevel L, Box colBox, int groupBox)
    {
        int span = colBox.Node.IsNull ? 1 : _boxes.Styled.ColumnSpanOf(colBox.Node.Index);
        bool collapsed = colBox.Values.Visibility == VisibilityKind.Collapse
            || (groupBox >= 0 && _boxes.Make(groupBox).Values.Visibility == VisibilityKind.Collapse);
        for (int i = 0; i < span && L.ColumnCount < MaxTableColumns; i++)
        {
            ref TableColumn col = ref L.AddColumn();
            col.Box = colBox.Index;
            col.GroupBox = groupBox;
            col.Collapsed = collapsed;
            ApplyColumnWidth(ref col, colBox.Values);
        }
    }

    private static void ApplyColumnWidth(ref TableColumn col, in ComputedValues v)
    {
        if (v.Width.Kind == LengthKind.Px)
        {
            col.Fixed = v.Width.Value;
        }
        else if (v.Width.Kind == LengthKind.Percent)
        {
            col.Percent = v.Width.Value;
        }
    }

    private void PlaceGroup(TableLevel L, Box group)
    {
        int rows = 0;
        for (Box ch = group.FirstChild; !ch.IsNull; ch = ch.NextSibling)
        {
            if (ch.Kind == BoxKind.Container && ch.Context == FormattingContext.TableRow)
            {
                rows++;
            }
        }

        int gi = L.GroupCount;
        ref TableGroup g = ref L.AddGroup();
        g.Box = group.Index;
        g.FirstRow = L.RowCount;
        g.Collapsed = group.Values.Visibility == VisibilityKind.Collapse;
        int end = L.RowCount + rows;
        for (Box ch = group.FirstChild; !ch.IsNull; ch = ch.NextSibling)
        {
            if (ch.Kind == BoxKind.Container && ch.Context == FormattingContext.TableRow)
            {
                PlaceRow(L, ch, gi, end);
            }
        }

        L.Groups[gi].RowCount = L.RowCount - L.Groups[gi].FirstRow;
    }

    private void PlaceRow(TableLevel L, Box rowBox, int group, int groupEnd)
    {
        int r = L.RowCount;
        ref TableRow row = ref L.AddRow();
        row.Box = rowBox.Index;
        row.Group = group;
        row.FirstCell = L.CellCount;
        row.Spec = rowBox.Values.Height.Kind == LengthKind.Px ? rowBox.Values.Height.Value : double.NaN;
        row.Collapsed = rowBox.Values.Visibility == VisibilityKind.Collapse || (group >= 0 && L.Groups[group].Collapsed);
        int col = 0;
        for (Box ch = rowBox.FirstChild; !ch.IsNull; ch = ch.NextSibling)
        {
            if (ch.Kind != BoxKind.Container || ch.Context != FormattingContext.TableCell || (ch.Flags & BoxTraits.OutOfFlow) != 0)
            {
                continue;
            }

            while (col < MaxTableColumns && L.Busy[col] > r)
            {
                col++;
            }

            if (col >= MaxTableColumns)
            {
                ReportTableLimit(L);
                break;
            }

            int cs = ch.Node.IsNull ? 1 : _boxes.Styled.ColumnSpanOf(ch.Node.Index);
            int rs = ch.Node.IsNull ? 1 : _boxes.Styled.RowSpanOf(ch.Node.Index);
            if (col + cs > MaxTableColumns)
            {
                cs = MaxTableColumns - col;
                ReportTableLimit(L);
            }

            int limit = group >= 0 ? groupEnd : int.MaxValue;
            int busyTo = rs == 0 ? limit : (int)Math.Min(limit, (long)r + rs);
            for (int k = col; k < col + cs; k++)
            {
                L.Busy[k] = busyTo;
            }

            ref TableCell cell = ref L.AddCell();
            cell.Box = ch.Index;
            cell.Row = r;
            cell.Col = col;
            cell.ColSpan = cs;
            cell.RowSpan = rs;
            cell.Limit = limit;
            col += cs;
            while (L.ColumnCount < col)
            {
                L.AddColumn();
            }
        }

        L.Rows[r].CellCount = L.CellCount - L.Rows[r].FirstCell;
    }

    private void ReportTableLimit(TableLevel L)
    {
        if (!L.LimitReported)
        {
            L.LimitReported = true;
            _boxes.Styled.Diagnostics.Add(DiagnosticKind.LimitExceeded, "table cells beyond " + MaxTableColumns.ToString(System.Globalization.CultureInfo.InvariantCulture) + " columns are dropped");
        }
    }

    // ---- collapsing borders (CSS2 §17.6.2) ----

    private static void BorderOf(in ComputedValues v, int side, out BorderStyleKind style, out double width)
    {
        switch (side)
        {
            case 0: style = v.BorderTopStyle; width = v.BorderTopWidth; break;
            case 1: style = v.BorderRightStyle; width = v.BorderRightWidth; break;
            case 2: style = v.BorderBottomStyle; width = v.BorderBottomWidth; break;
            default: style = v.BorderLeftStyle; width = v.BorderLeftWidth; break;
        }

        if (style == BorderStyleKind.None)
        {
            width = 0;
        }
    }

    /// <summary>
    /// CSS2 §17.6.2.1: <c>hidden</c> wins over everything, <c>none</c> loses to everything; otherwise the wider border wins, then the
    /// style (double, solid, dashed, dotted, ridge, outset, groove, inset), then the element (cell, row, row group, column, column group,
    /// table). Equal borders keep the one offered first, which is the left-most and then top-most.
    /// </summary>
    private static void OfferEdge(ref TableEdge e, in ComputedValues v, int side, byte prio, int boxIndex)
    {
        BorderOf(in v, side, out BorderStyleKind style, out double width);
        if (style == BorderStyleKind.None && e.Prio != 0)
        {
            return;
        }

        bool take;
        if (e.Prio == 0)
        {
            take = true;
        }
        else if (style == BorderStyleKind.Hidden)
        {
            take = (BorderStyleKind)e.Style != BorderStyleKind.Hidden;
        }
        else if ((BorderStyleKind)e.Style == BorderStyleKind.Hidden)
        {
            take = false;
        }
        else if ((BorderStyleKind)e.Style == BorderStyleKind.None)
        {
            take = true;
        }
        else if (width != e.Width)
        {
            take = width > e.Width;
        }
        else if (style != (BorderStyleKind)e.Style)
        {
            take = StyleRank(style) > StyleRank((BorderStyleKind)e.Style);
        }
        else
        {
            take = prio > e.Prio;
        }

        if (take)
        {
            e.Width = style == BorderStyleKind.Hidden ? 0 : width;
            e.Box = boxIndex;
            e.Side = (byte)side;
            e.Style = (byte)style;
            e.Prio = prio;
        }
    }

    private static int StyleRank(BorderStyleKind s) => s switch
    {
        BorderStyleKind.DoubleLine => 8,
        BorderStyleKind.Solid => 7,
        BorderStyleKind.Dashed => 6,
        BorderStyleKind.Dotted => 5,
        BorderStyleKind.Ridge => 4,
        BorderStyleKind.Outset => 3,
        BorderStyleKind.Groove => 2,
        BorderStyleKind.Inset => 1,
        _ => 0,
    };

    /// <summary>The part of a collapsed border of width <paramref name="w"/> that lies before the grid line (towards the top or left).</summary>
    private static double HalfBefore(double w) => w == Math.Floor(w) ? Math.Floor(w / 2) : w / 2;

    private void ResolveCollapsedBorders(TableLevel L, Box table)
    {
        int R = L.RowCount, C = L.ColumnCount;
        if ((long)R * C > MaxCollapsedSlots)
        {
            L.Collapse = false;
            L.SpacingH = table.Values.BorderSpacingH;
            L.SpacingV = table.Values.BorderSpacingV;
            _boxes.Styled.Diagnostics.Add(DiagnosticKind.LimitExceeded, "table with more than " + MaxCollapsedSlots.ToString(System.Globalization.CultureInfo.InvariantCulture) + " grid slots uses separate borders");
            return;
        }

        L.PrepareEdges();
        int V = C + 1;
        ref readonly ComputedValues tv = ref table.Values;
        for (int c = 0; c < C; c++)
        {
            OfferEdge(ref L.Horizontal[c], in tv, 0, 1, table.Index);
            OfferEdge(ref L.Horizontal[(R * C) + c], in tv, 2, 1, table.Index);
        }

        for (int r = 0; r < R; r++)
        {
            OfferEdge(ref L.Vertical[r * V], in tv, 3, 1, table.Index);
            OfferEdge(ref L.Vertical[(r * V) + C], in tv, 1, 1, table.Index);
        }

        for (int c = 0; c < C; c++)
        {
            ref TableColumn col = ref L.Columns[c];
            if (col.GroupBox >= 0)
            {
                ref readonly ComputedValues gv = ref _boxes.Make(col.GroupBox).Values;
                for (int r = 0; r < R; r++)
                {
                    OfferEdge(ref L.Vertical[(r * V) + c], in gv, 3, 2, col.GroupBox);
                    OfferEdge(ref L.Vertical[(r * V) + c + 1], in gv, 1, 2, col.GroupBox);
                }
            }

            if (col.Box >= 0)
            {
                ref readonly ComputedValues cv = ref _boxes.Make(col.Box).Values;
                for (int r = 0; r < R; r++)
                {
                    OfferEdge(ref L.Vertical[(r * V) + c], in cv, 3, 3, col.Box);
                    OfferEdge(ref L.Vertical[(r * V) + c + 1], in cv, 1, 3, col.Box);
                }
            }
        }

        for (int g = 0; g < L.GroupCount; g++)
        {
            ref TableGroup grp = ref L.Groups[g];
            if (grp.RowCount == 0)
            {
                continue;
            }

            ref readonly ComputedValues gv = ref _boxes.Make(grp.Box).Values;
            for (int c = 0; c < C; c++)
            {
                OfferEdge(ref L.Horizontal[(grp.FirstRow * C) + c], in gv, 0, 4, grp.Box);
                OfferEdge(ref L.Horizontal[((grp.FirstRow + grp.RowCount) * C) + c], in gv, 2, 4, grp.Box);
            }

            for (int r = grp.FirstRow; r < grp.FirstRow + grp.RowCount; r++)
            {
                OfferEdge(ref L.Vertical[r * V], in gv, 3, 4, grp.Box);
                OfferEdge(ref L.Vertical[(r * V) + C], in gv, 1, 4, grp.Box);
            }
        }

        for (int r = 0; r < R; r++)
        {
            ref readonly ComputedValues rv = ref _boxes.Make(L.Rows[r].Box).Values;
            for (int c = 0; c < C; c++)
            {
                OfferEdge(ref L.Horizontal[(r * C) + c], in rv, 0, 5, L.Rows[r].Box);
                OfferEdge(ref L.Horizontal[((r + 1) * C) + c], in rv, 2, 5, L.Rows[r].Box);
            }

            OfferEdge(ref L.Vertical[r * V], in rv, 3, 5, L.Rows[r].Box);
            OfferEdge(ref L.Vertical[(r * V) + C], in rv, 1, 5, L.Rows[r].Box);
        }

        for (int i = 0; i < L.CellCount; i++)
        {
            ref TableCell cell = ref L.Cells[i];
            ref readonly ComputedValues cv = ref _boxes.Make(cell.Box).Values;
            for (int c = cell.Col; c < cell.Col + cell.ColSpan; c++)
            {
                OfferEdge(ref L.Horizontal[(cell.Row * C) + c], in cv, 0, 6, cell.Box);
                OfferEdge(ref L.Horizontal[((cell.Row + cell.RowSpan) * C) + c], in cv, 2, 6, cell.Box);
            }

            for (int r = cell.Row; r < cell.Row + cell.RowSpan; r++)
            {
                OfferEdge(ref L.Vertical[(r * V) + cell.Col], in cv, 3, 6, cell.Box);
                OfferEdge(ref L.Vertical[(r * V) + cell.Col + cell.ColSpan], in cv, 1, 6, cell.Box);
            }
        }

        // Each side of a cell takes the strongest segment along it; the cell owns the half after (right or below) or before the grid line.
        for (int i = 0; i < L.CellCount; i++)
        {
            ref TableCell cell = ref L.Cells[i];
            cell.Et = StrongestH(L, cell.Row, cell.Col, cell.ColSpan);
            cell.Eb = StrongestH(L, cell.Row + cell.RowSpan, cell.Col, cell.ColSpan);
            cell.El = StrongestV(L, cell.Row, cell.RowSpan, cell.Col);
            cell.Er = StrongestV(L, cell.Row, cell.RowSpan, cell.Col + cell.ColSpan);
            cell.Bt = cell.Et.Width - HalfBefore(cell.Et.Width);
            cell.Bb = HalfBefore(cell.Eb.Width);
            cell.Bl = cell.El.Width - HalfBefore(cell.El.Width);
            cell.Br = HalfBefore(cell.Er.Width);
        }

        L.Tt = StrongestH(L, 0, 0, C);
        L.Tb = StrongestH(L, R, 0, C);
        L.Tl = StrongestV(L, 0, R, 0);
        L.Tr = StrongestV(L, 0, R, C);
        L.Bt = HalfBefore(L.Tt.Width);
        L.Bb = L.Tb.Width - HalfBefore(L.Tb.Width);
        L.Bl = HalfBefore(L.Tl.Width);
        L.Br = L.Tr.Width - HalfBefore(L.Tr.Width);
        L.Pt = L.Pr = L.Pb = L.Pl = 0;
    }

    private static TableEdge StrongestH(TableLevel L, int line, int col, int span)
    {
        TableEdge best = default;
        for (int c = col; c < col + span; c++)
        {
            Merge(ref best, in L.Horizontal[(line * L.ColumnCount) + c]);
        }

        return best;
    }

    private static TableEdge StrongestV(TableLevel L, int row, int span, int line)
    {
        TableEdge best = default;
        int V = L.ColumnCount + 1;
        for (int r = row; r < row + span; r++)
        {
            Merge(ref best, in L.Vertical[(r * V) + line]);
        }

        return best;
    }

    private static void Merge(ref TableEdge best, in TableEdge e)
    {
        if (e.Prio == 0)
        {
            return;
        }

        if (best.Prio == 0)
        {
            best = e;
            return;
        }

        bool eHidden = (BorderStyleKind)e.Style == BorderStyleKind.Hidden, bHidden = (BorderStyleKind)best.Style == BorderStyleKind.Hidden;
        bool take;
        if (eHidden != bHidden)
        {
            take = eHidden;
        }
        else if (e.Width != best.Width)
        {
            take = e.Width > best.Width;
        }
        else
        {
            take = StyleRank((BorderStyleKind)e.Style) > StyleRank((BorderStyleKind)best.Style);
        }

        if (take)
        {
            best = e;
        }
    }
}
