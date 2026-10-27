using OneRender.Tree;

namespace OneRender.Layout;

internal sealed partial class Fragmenter
{
    private bool[] _pBlank = new bool[16];

    /// <summary>Whether page <paramref name="index"/> of the last <see cref="PlanPages"/> is a blank page inserted for a side break.</summary>
    internal bool IsBlankPage(int index) => _pBlank[index];

    /// <summary>The offset in the strip where page <paramref name="index"/> of the last <see cref="PlanPages"/> starts.</summary>
    internal double PageStart(int index) => _pYs[index];

    /// <summary>The offset in the strip where page <paramref name="index"/> of the last <see cref="PlanPages"/> ends.</summary>
    internal double PageEnd(int index) => _pYe[index];

    /// <summary>
    /// Finds the break points of a continuous flow: <paramref name="root"/> is the root fragment, whose children were laid out as one tall
    /// strip at the page content width. Call once, then <see cref="PlanPages"/> any number of times.
    /// </summary>
    internal void BeginPages(FragmentTree tree, int root, CancellationToken ct)
    {
        Collect(tree, root, 0, FragmentationKind.Page, false, ct);
        Finish();
    }

    /// <summary>
    /// Plans the pages of the flow collected by <see cref="BeginPages"/>. The strip is cut into pages by the rules of CSS Fragmentation 3
    /// §4.1 to §4.5 (forced breaks first, then the latest allowed break, orphans and widows, <c>break-inside</c>), and the plan is
    /// recorded for <see cref="Apply"/>, which lays the pages side by side <paramref name="stride"/> apart.
    /// </summary>
    /// <param name="contentWidth">The page content width in px.</param>
    /// <param name="stride">The distance between the left edges of neighbouring pages in the tree.</param>
    /// <param name="sizes">The content height of page 0, page 1 and so on. With <paramref name="cyclic"/> the last two entries then alternate for every later page.</param>
    /// <param name="cyclic">Whether the last two sizes repeat alternately (the left/right pattern) instead of the last one alone.</param>
    /// <param name="firstPageRight">Whether page 0 is a right-hand page (a left-to-right document); it decides blank pages for side breaks.</param>
    /// <param name="maxPages">The most pages to plan; the last one then takes all the remaining content.</param>
    /// <param name="truncated">Set when <paramref name="maxPages"/> was reached.</param>
    /// <returns>The number of pages.</returns>
    internal int PlanPages(double contentWidth, ReadOnlySpan<double> sizes, bool cyclic, bool firstPageRight, int maxPages, double stride, out bool truncated)
    {
        ClearPlans();
        _cyclic = cyclic;
        _sidesOn = true;
        _firstRight = firstPageRight;
        _cap = Math.Clamp(maxPages, 1, MaxFragmentainers);
        if (_planN == _plans.Length)
        {
            Array.Resize(ref _plans, _planN * 2);
        }

        double maxH = 1;
        for (int i = 0; i < sizes.Length; i++)
        {
            maxH = Math.Max(maxH, sizes[i]);
        }

        var plan = new PlanRecord
        {
            Frag = 0,
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
        var row = new RowRecord { FkStart = _pN, ColBase = 0, H = maxH, OutY = 0 };
        truncated = false;
        if (RowHasChildren(0))
        {
            double end = Math.Max(0, RowLast(0));
            ClearRecord();
            // The first page starts at the top of the strip, so the margin above the first box is on the page.
            SliceResult sr = Slice(0, CandidateCount, 0, end, sizes, int.MaxValue, true);
            truncated = sr.Truncated;
            if (_pBlank.Length < _pN + _fkN)
            {
                Array.Resize(ref _pBlank, Math.Max(_pN + _fkN, _pBlank.Length * 2));
            }

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

                if (_pBlank.Length <= _pN)
                {
                    Array.Resize(ref _pBlank, _pN * 2);
                }

                _pYs[_pN] = _fkYs[i];
                _pYe[_pN] = _fkYe[i];
                _pT[_pN] = _fkT[i];
                _pB[_pN] = _fkB[i];
                _pBlank[_pN] = _fkBlank[i];
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
        plan.OutH = maxH;
        plan.Columns = row.FkCount;
        _plans[_planN++] = plan;
        _sidesOn = false;
        _cyclic = false;
        _cap = MaxFragmentainers;
        return row.FkCount;
    }

    /// <summary>
    /// The page name (Paged Media 3 §7.3) of the first in-flow content at or after strip offset <paramref name="y"/>: the used name of the
    /// innermost box reached by always taking the first child that extends past <paramref name="y"/>. Null for <c>auto</c>.
    /// </summary>
    internal string? PageNameAt(FragmentTree tree, int root, double y)
    {
        _t = tree;
        _boxes = tree.Boxes;
        int node = root;
        double off = 0;
        string? name = null;
        for (int depth = 0; depth < 100_000; depth++)
        {
            int next = -1;
            double nextOff = off;
            for (int c = tree.FirstChildOf(node); c >= 0; c = tree.NextSiblingOf(c))
            {
                if ((FragmentKind)tree.KindOf(c) != FragmentKind.Box || !InFlowBox(c))
                {
                    continue;
                }

                Rect r = tree.BorderBoxOf(c);
                if (off + r.Bottom > y + Eps)
                {
                    next = c;
                    nextOff = off + r.Y;
                    break;
                }
            }

            if (next < 0)
            {
                return name;
            }

            Box b = _boxes.Make(tree.BoxOf(next));
            name = b.Values.PageName;
            if (IsMono(next, b))
            {
                return name;
            }

            node = next;
            off = nextOff;
        }

        return name;
    }
}
