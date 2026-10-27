using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    /// <summary>
    /// Fills in each cell's borders (separate model) or collapses them (CSS2 §17.6), and the table's own border and padding.
    /// Returns the table's horizontal and vertical border plus padding through the level's fields.
    /// </summary>
    private void PrepareTableBorders(TableLevel L, Box table, double basis)
    {
        ref readonly ComputedValues tv = ref table.Values;
        if (L.Collapse && L.ColumnCount > 0 && L.RowCount > 0)
        {
            ResolveCollapsedBorders(L, table);
        }
        else
        {
            L.Collapse = false;
        }

        if (!L.Collapse)
        {
            BorderOf(in tv, 0, out _, out L.Bt);
            BorderOf(in tv, 1, out _, out L.Br);
            BorderOf(in tv, 2, out _, out L.Bb);
            BorderOf(in tv, 3, out _, out L.Bl);
            L.Pt = Pad(tv.PaddingTop, basis);
            L.Pr = Pad(tv.PaddingRight, basis);
            L.Pb = Pad(tv.PaddingBottom, basis);
            L.Pl = Pad(tv.PaddingLeft, basis);
            L.SpacingH = tv.BorderSpacingH;
            L.SpacingV = tv.BorderSpacingV;
            for (int i = 0; i < L.CellCount; i++)
            {
                ref TableCell cell = ref L.Cells[i];
                ref readonly ComputedValues cv = ref _boxes.Make(cell.Box).Values;
                BorderOf(in cv, 0, out _, out cell.Bt);
                BorderOf(in cv, 1, out _, out cell.Br);
                BorderOf(in cv, 2, out _, out cell.Bb);
                BorderOf(in cv, 3, out _, out cell.Bl);
            }
        }
    }

    private static bool IsFixedLayout(in ComputedValues v) =>
        v.TableLayout == TableLayoutKind.Fixed && v.Width.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc;

    /// <summary>
    /// Computes the min-content and max-content width of every column (CSS Tables 3 §3.6 / CSS2 §17.5.2.2) and the table grid's own
    /// min and max (<see cref="TableLevel.GridMin"/>, <see cref="TableLevel.GridMax"/>, content-box, spacing included).
    /// With <c>table-layout: fixed</c> the cells' content is not measured at all (CSS2 §17.5.2.1).
    /// </summary>
    private void AnalyzeColumns(TableLevel L, Box table, double basis)
    {
        int C = L.ColumnCount;
        bool fixedLayout = IsFixedLayout(in table.Values);
        for (int j = 0; j < C; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            col.Min = 0;
            col.Max = 0;
            col.HasFixed = !double.IsNaN(col.Fixed);
            col.Width = 0;
        }

        for (int i = 0; i < L.CellCount; i++)
        {
            if ((i & 255) == 255)
            {
                _token.ThrowIfCancellationRequested();
            }

            ref TableCell cell = ref L.Cells[i];
            ref readonly ComputedValues cv = ref _boxes.Make(cell.Box).Values;
            cell.Skip = L.Rows[cell.Row].Collapsed;
            if (!cell.Skip)
            {
                bool all = true;
                for (int j = cell.Col; j < cell.Col + cell.ColSpan; j++)
                {
                    if (!L.Columns[j].Collapsed)
                    {
                        all = false;
                        break;
                    }
                }

                cell.Skip = all;
            }

            cell.Fixed = double.NaN;
            cell.Percent = double.NaN;
            double ph = Pad(cv.PaddingLeft, basis) + Pad(cv.PaddingRight, basis) + cell.Bl + cell.Br;
            cell.Extra = ph;
            if (cell.Skip)
            {
                cell.Min = cell.Max = 0;
                continue;
            }

            double imin = 0, imax = 0;
            if (!fixedLayout)
            {
                GetIntrinsic(_boxes.Make(cell.Box), out imin, out imax);
            }

            cell.Min = imin + ph;
            cell.Max = imax + ph;
            if (cv.Width.Kind == LengthKind.Px)
            {
                double outer = cv.BoxSizing == BoxSizingKind.BorderBox ? cv.Width.Value : cv.Width.Value + ph;
                cell.Fixed = Math.Max(outer, ph);
                cell.Max = Math.Max(cell.Min, cell.Fixed);
                if (fixedLayout)
                {
                    cell.Min = cell.Fixed;
                }
            }
            else if (cv.Width.Kind == LengthKind.Percent)
            {
                cell.Percent = cv.Width.Value;
            }

            if (cv.MinWidth.Kind == LengthKind.Px)
            {
                double mo = cv.BoxSizing == BoxSizingKind.BorderBox ? cv.MinWidth.Value : cv.MinWidth.Value + ph;
                cell.Min = Math.Max(cell.Min, mo);
                cell.Max = Math.Max(cell.Max, cell.Min);
            }

            if (cell.ColSpan == 1)
            {
                ref TableColumn col = ref L.Columns[cell.Col];
                if (fixedLayout)
                {
                    // Only the first row decides a fixed layout.
                    if (cell.Row == 0 && !col.HasFixed && !double.IsNaN(cell.Fixed))
                    {
                        col.Fixed = cell.Fixed;
                        col.HasFixed = true;
                    }

                    if (cell.Row == 0 && double.IsNaN(col.Percent) && !double.IsNaN(cell.Percent))
                    {
                        col.Percent = cell.Percent;
                    }

                    continue;
                }

                col.Min = Math.Max(col.Min, cell.Min);
                col.Max = Math.Max(col.Max, cell.Max);
                if (!double.IsNaN(cell.Fixed))
                {
                    col.Fixed = double.IsNaN(col.Fixed) ? cell.Fixed : Math.Max(col.Fixed, cell.Fixed);
                    col.HasFixed = true;
                }

                if (!double.IsNaN(cell.Percent) && (double.IsNaN(col.Percent) || cell.Percent > col.Percent))
                {
                    col.Percent = cell.Percent;
                }
            }
        }

        if (fixedLayout)
        {
            AnalyzeFixed(L);
            return;
        }

        for (int j = 0; j < C; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            if (col.HasFixed)
            {
                // A column with a specified width is as wide as that, unless its content cannot shrink that far.
                col.Max = Math.Max(col.Min, col.Fixed);
            }
        }

        for (int i = 0; i < L.CellCount; i++)
        {
            ref TableCell cell = ref L.Cells[i];
            if (cell.Skip || cell.ColSpan == 1)
            {
                continue;
            }

            DistributeSpan(L, in cell);
        }

        int visible = 0;
        double sumMin = 0, sumMax = 0, nonPctMax = 0, pctSum = 0, t2 = 0;
        for (int j = 0; j < C; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            if (col.Collapsed)
            {
                col.Min = col.Max = 0;
                continue;
            }

            visible++;
            col.Max = Math.Max(col.Max, col.Min);
            sumMin += col.Min;
            sumMax += col.Max;
            double p = col.Percent;
            if (!double.IsNaN(p) && p > 0 && pctSum < 100)
            {
                p = Math.Min(p, 100 - pctSum);
                pctSum += p;
                t2 = Math.Max(t2, col.Max * 100 / p);
            }
            else
            {
                nonPctMax += col.Max;
            }
        }

        double spacing = visible > 0 ? L.SpacingH * (visible + 1) : 0;
        double maxCols = sumMax;
        if (pctSum > 0)
        {
            double t1 = pctSum < 100 ? nonPctMax * 100 / (100 - pctSum) : (nonPctMax > 0 ? sumMax : 0);
            maxCols = Math.Max(t2, Math.Max(t1, 0));
            if (pctSum >= 100 && nonPctMax > 0)
            {
                maxCols = Math.Max(maxCols, sumMax);
            }
        }

        L.GridMin = sumMin + spacing;
        L.GridMax = Math.Max(L.GridMin, maxCols + spacing);
    }

    /// <summary>CSS2 §17.5.2.1: widths from the column elements and the first row; the rest share what is left (done in <see cref="AssignColumnWidths"/>).</summary>
    private static void AnalyzeFixed(TableLevel L)
    {
        double sum = 0;
        int visible = 0;
        for (int j = 0; j < L.ColumnCount; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            if (col.Collapsed)
            {
                continue;
            }

            visible++;
            col.Min = col.HasFixed ? col.Fixed : 0;
            col.Max = col.Min;
            sum += col.Min;
        }

        double spacing = visible > 0 ? L.SpacingH * (visible + 1) : 0;
        L.GridMin = sum + spacing;
        L.GridMax = L.GridMin;
    }

    /// <summary>
    /// CSS Tables 3 §3.6: a cell that spans several columns raises the columns it covers until their sum (plus the spacing between them)
    /// reaches its own min-content and max-content width. The excess is shared in proportion to the columns' max-content widths,
    /// preferring columns without a fixed width.
    /// </summary>
    private static void DistributeSpan(TableLevel L, in TableCell cell)
    {
        int c0 = cell.Col, c1 = cell.Col + cell.ColSpan;
        int n = 0;
        double sumMin = 0, sumMax = 0, weight = 0, weightAll = 0;
        bool anyFlexible = false;
        for (int j = c0; j < c1; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            if (col.Collapsed)
            {
                continue;
            }

            n++;
            sumMin += col.Min;
            sumMax += col.Max;
            weightAll += col.Max;
            if (!col.HasFixed && double.IsNaN(col.Percent))
            {
                anyFlexible = true;
                weight += col.Max;
            }
        }

        if (n == 0)
        {
            return;
        }

        double inner = L.SpacingH * (n - 1);
        double exMin = cell.Min - (sumMin + inner);
        double exMax = cell.Max - (sumMax + inner);
        if (exMin > 0)
        {
            Spread(L, c0, c1, exMin, anyFlexible, anyFlexible ? weight : weightAll, true);
        }

        if (exMax > 0)
        {
            // Recompute the sum: raising the minima may already have raised the maxima.
            double s2 = 0;
            for (int j = c0; j < c1; j++)
            {
                if (!L.Columns[j].Collapsed)
                {
                    s2 += Math.Max(L.Columns[j].Max, L.Columns[j].Min);
                }
            }

            exMax = cell.Max - (s2 + inner);
            if (exMax > 0)
            {
                Spread(L, c0, c1, exMax, anyFlexible, anyFlexible ? weight : weightAll, false);
            }
        }
    }

    private static void Spread(TableLevel L, int c0, int c1, double excess, bool flexibleOnly, double weight, bool min)
    {
        int count = 0;
        for (int j = c0; j < c1; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            if (!col.Collapsed && (!flexibleOnly || (!col.HasFixed && double.IsNaN(col.Percent))))
            {
                count++;
            }
        }

        if (count == 0)
        {
            return;
        }

        for (int j = c0; j < c1; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            if (col.Collapsed || (flexibleOnly && (col.HasFixed || !double.IsNaN(col.Percent))))
            {
                continue;
            }

            double share = weight > 0 ? excess * col.Max / weight : excess / count;
            if (min)
            {
                col.Min += share;
                col.Max = Math.Max(col.Max, col.Min);
            }
            else
            {
                col.Max += share;
            }
        }
    }

    /// <summary>
    /// Gives each column its used width for a table content width of <paramref name="contentWidth"/> (CSS Tables 3 §3.6, CSS2 §17.5.2):
    /// at least the min-content widths; percentage columns first, then fixed ones, then auto ones grow towards their max-content widths;
    /// any room beyond that goes to the auto columns in proportion to their max-content widths.
    /// </summary>
    private static void AssignColumnWidths(TableLevel L, double contentWidth, bool fixedLayout)
    {
        int C = L.ColumnCount;
        int visible = 0;
        double sumMin = 0;
        for (int j = 0; j < C; j++)
        {
            if (!L.Columns[j].Collapsed)
            {
                visible++;
                sumMin += L.Columns[j].Min;
            }
            else
            {
                L.Columns[j].Width = 0;
            }
        }

        if (visible == 0)
        {
            return;
        }

        double avail = Math.Max(0, contentWidth - (L.SpacingH * (visible + 1)));
        if (fixedLayout)
        {
            AssignFixedWidths(L, avail, visible);
            return;
        }

        double pctSum = 0;
        for (int j = 0; j < C; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            if (col.Collapsed)
            {
                continue;
            }

            col.Width = col.Min;
            double p = col.Percent;
            if (!double.IsNaN(p) && p > 0 && pctSum < 100)
            {
                pctSum += Math.Min(p, 100 - pctSum);
            }
        }

        if (avail <= sumMin)
        {
            return;
        }

        double pctScale = pctSum > 100 ? 100 / pctSum : 1;
        double free = avail - sumMin;
        for (int tier = 0; tier < 3; tier++)
        {
            double need = 0;
            for (int j = 0; j < C; j++)
            {
                ref TableColumn col = ref L.Columns[j];
                if (!col.Collapsed && TierOf(in col) == tier)
                {
                    need += Math.Max(0, Desired(in col, avail, pctScale) - col.Min);
                }
            }

            if (need <= 0)
            {
                continue;
            }

            double give = Math.Min(free, need);
            for (int j = 0; j < C; j++)
            {
                ref TableColumn col = ref L.Columns[j];
                if (!col.Collapsed && TierOf(in col) == tier)
                {
                    col.Width += Math.Max(0, Desired(in col, avail, pctScale) - col.Min) * give / need;
                }
            }

            free -= give;
        }

        if (free > 1e-9)
        {
            DistributeExtra(L, free);
        }
    }

    private static int TierOf(in TableColumn col) => !double.IsNaN(col.Percent) && col.Percent > 0 ? 0 : col.HasFixed ? 1 : 2;

    private static double Desired(in TableColumn col, double avail, double pctScale) =>
        TierOf(in col) == 0 ? Math.Max(col.Min, col.Percent * pctScale * avail / 100) : col.Max;

    /// <summary>Room beyond every column's desired width goes to the auto columns, else the fixed ones, else the percentage ones.</summary>
    private static void DistributeExtra(TableLevel L, double extra)
    {
        for (int tier = 2; tier >= 0; tier--)
        {
            double weight = 0;
            int count = 0;
            for (int j = 0; j < L.ColumnCount; j++)
            {
                ref TableColumn col = ref L.Columns[j];
                if (!col.Collapsed && TierOf(in col) == tier)
                {
                    count++;
                    weight += tier == 0 ? col.Percent : col.Max;
                }
            }

            if (count == 0)
            {
                continue;
            }

            for (int j = 0; j < L.ColumnCount; j++)
            {
                ref TableColumn col = ref L.Columns[j];
                if (!col.Collapsed && TierOf(in col) == tier)
                {
                    col.Width += weight > 0 ? extra * (tier == 0 ? col.Percent : col.Max) / weight : extra / count;
                }
            }

            return;
        }
    }

    private static void AssignFixedWidths(TableLevel L, double avail, int visible)
    {
        double specified = 0;
        int open = 0;
        for (int j = 0; j < L.ColumnCount; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            if (col.Collapsed)
            {
                continue;
            }

            if (col.HasFixed)
            {
                col.Width = col.Fixed;
            }
            else if (!double.IsNaN(col.Percent))
            {
                col.Width = col.Percent * avail / 100;
            }
            else
            {
                col.Width = -1;
                open++;
                continue;
            }

            specified += col.Width;
        }

        double rest = Math.Max(0, avail - specified);
        for (int j = 0; j < L.ColumnCount; j++)
        {
            ref TableColumn col = ref L.Columns[j];
            if (!col.Collapsed && col.Width < 0)
            {
                col.Width = rest / open;
            }
        }

        if (open == 0 && avail > specified && specified > 0)
        {
            double scale = avail / specified;
            for (int j = 0; j < L.ColumnCount; j++)
            {
                if (!L.Columns[j].Collapsed)
                {
                    L.Columns[j].Width *= scale;
                }
            }
        }

        _ = visible;
    }

    /// <summary>The min-content and max-content widths of a table box (content box), asked by shrink-to-fit parents (CSS Sizing 3 §5).</summary>
    private bool TableIntrinsicSizes(Box table, out double min, out double max)
    {
        min = max = 0;
        TableLevel? L = AcquireTable();
        if (L is null)
        {
            return false;
        }

        try
        {
            BuildTable(L, table);
            PrepareTableBorders(L, table, double.NaN);
            AnalyzeColumns(L, table, double.NaN);
            min = L.GridMin;
            max = L.GridMax;
            double ph = L.Bl + L.Br + L.Pl + L.Pr;
            for (int i = 0; i < L.CaptionCount; i++)
            {
                Box cap = _boxes.Make(L.Captions[i]);
                GetIntrinsic(cap, out double cmin, out double cmax);
                ref readonly ComputedValues cv = ref cap.Values;
                double extra = FixedPadding(in cv) + (cv.MarginLeft.IsPx ? cv.MarginLeft.Value : 0) + (cv.MarginRight.IsPx ? cv.MarginRight.Value : 0);
                min = Math.Max(min, cmin + extra - ph);
                max = Math.Max(max, cmax + extra - ph);
            }

            max = Math.Max(min, max);
            return true;
        }
        finally
        {
            ReleaseTable();
        }
    }
}
