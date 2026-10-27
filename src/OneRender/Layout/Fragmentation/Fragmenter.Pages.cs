namespace OneRender.Layout;

internal sealed partial class Fragmenter
{
    /// <summary>
    /// Plans the pages of a continuous flow: <paramref name="root"/> is the root fragment, whose children were laid out as one tall strip
    /// at the page content width. The strip is cut into pages of <paramref name="pageHeight"/> by the rules of CSS Fragmentation 3
    /// §4.1 to §4.5 (forced breaks first, then the latest allowed break, orphans and widows, <c>break-inside</c>), and the plan is
    /// recorded for <see cref="Apply"/>, which lays the pages side by side <paramref name="stride"/> apart.
    /// </summary>
    /// <returns>The number of pages.</returns>
    internal int PlanPages(FragmentTree tree, int root, double contentWidth, double pageHeight, double stride, CancellationToken ct)
    {
        ClearPlans();
        Collect(tree, root, 0, FragmentationKind.Page, false, ct);
        Finish();
        if (_planN == _plans.Length)
        {
            Array.Resize(ref _plans, _planN * 2);
        }

        var plan = new PlanRecord
        {
            Frag = root,
            ContentLeft = 0,
            ContentTop = 0,
            ColW = contentWidth,
            Stride = stride,
            ContentW = contentWidth,
            Rtl = false,
            RowStart = _rowN,
            RowCount = 1,
            SpanStart = _spanN,
            SpanCount = 0,
        };
        var row = new RowRecord { FkStart = _pN, ColBase = 0, H = pageHeight, OutY = 0 };
        if (RowHasChildren(0))
        {
            double end = Math.Max(0, RowLast(0));
            ClearRecord();
            // The first page starts at the top of the strip, so the margin above the first box is on the page.
            SliceResult sr = Slice(0, CandidateCount, 0, end, pageHeight, int.MaxValue, true);
            for (int i = 0; i < _fkN; i++)
            {
                if (_pN == _pYs.Length)
                {
                    int n = _pN * 2;
                    Array.Resize(ref _pYs, n);
                    Array.Resize(ref _pYe, n);
                    Array.Resize(ref _pT, n);
                    Array.Resize(ref _pB, n);
                }

                _pYs[_pN] = _fkYs[i];
                _pYe[_pN] = _fkYe[i];
                _pT[_pN] = _fkT[i];
                _pB[_pN] = _fkB[i];
                _pN++;
            }

            ClearRecord();
            row.FkCount = sr.Count;
        }

        if (_rowN == _rows.Length)
        {
            Array.Resize(ref _rows, _rowN * 2);
        }

        _rows[_rowN++] = row;
        plan.OutH = pageHeight;
        plan.Columns = row.FkCount;
        _plans[_planN++] = plan;
        return row.FkCount;
    }
}
