namespace OneRender.Layout;

/// <summary>What the planner needs to know about a multi-column container whose content has been laid out as one strip.</summary>
internal struct PlanInput
{
    public int Frag;
    public double ContentLeft;
    public double ContentTop;
    public double ColW;
    public double Stride;
    public double ContentW;
    public bool Rtl;
    public int Columns;
    public bool Balance;
    public double Height;
    public double StripH;
}

internal sealed partial class Fragmenter
{
    private PlanRecord[] _plans = new PlanRecord[4];
    private int _planN;
    private RowRecord[] _rows = new RowRecord[8];
    private int _rowN;
    private SpannerRecord[] _spans = new SpannerRecord[4];
    private int _spanN;
    private double[] _pYs = new double[16];
    private double[] _pYe = new double[16];
    private double[] _pT = new double[16];
    private double[] _pB = new double[16];
    private int _pN;

    internal int PlanCount => _planN;

    internal PlanRecord PlanAt(int i) => _plans[i];

    /// <summary>Forgets every plan; storage is kept.</summary>
    internal void ClearPlans()
    {
        _planN = 0;
        _rowN = 0;
        _spanN = 0;
        _pN = 0;
    }

    /// <summary>Drops the plans of containers whose fragments were discarded by a trial layout (index at or after <paramref name="fragmentCount"/>).</summary>
    internal void DropPlansFrom(int fragmentCount)
    {
        while (_planN > 0 && _plans[_planN - 1].Frag >= fragmentCount)
        {
            _planN--;
            _rowN = _plans[_planN].RowStart;
            _spanN = _plans[_planN].SpanStart;
            _pN = _rowN > 0 ? _rows[_rowN - 1].FkStart + _rows[_rowN - 1].FkCount : 0;
        }
    }

    /// <summary>
    /// Plans the columns of a multi-column container (Multicol 1 §5 to §8): splits the strip into rows at <c>column-span: all</c>
    /// children, picks each row's column height (balanced, or filled up to the container's height for the last row), and cuts the
    /// row into columns. The tree is not changed until <see cref="Apply"/>.
    /// </summary>
    /// <returns>The container's content height when its own height is <c>auto</c>.</returns>
    internal double Plan(FragmentTree tree, in PlanInput inp, CancellationToken ct)
    {
        Collect(tree, inp.Frag, inp.ContentTop, FragmentationKind.Column, true, ct);
        Finish();
        int spans = SpannerCount;
        if (_planN == _plans.Length)
        {
            Array.Resize(ref _plans, _planN * 2);
        }

        var plan = new PlanRecord
        {
            Frag = inp.Frag,
            ContentLeft = inp.ContentLeft,
            ContentTop = inp.ContentTop,
            ColW = inp.ColW,
            Stride = inp.Stride,
            ContentW = inp.ContentW,
            Rtl = inp.Rtl,
            RowStart = _rowN,
            RowCount = spans + 1,
            SpanStart = _spanN,
            SpanCount = spans,
        };
        double stripPos = 0, cursor = 0;
        int colBase = 0;
        for (int r = 0; r <= spans; r++)
        {
            bool last = r == spans;
            var row = new RowRecord { FkStart = _pN, ColBase = colBase };
            if (RowHasChildren(r))
            {
                double cs = RowFirst(r), ce = Math.Max(cs, RowLast(r));
                cursor += cs - stripPos;
                int c0 = r == 0 ? 0 : LowerBound(SpannerAt(r - 1).Bottom - Eps);
                int c1 = last ? CandidateCount : LowerBound(SpannerAt(r).Top - Eps);
                if (c1 < c0)
                {
                    c1 = c0;
                }

                double avail = double.PositiveInfinity;
                if (last && !double.IsNaN(inp.Height) && !double.IsInfinity(inp.Height))
                {
                    avail = Math.Max(0, inp.Height - cursor - Math.Max(0, inp.StripH - ce));
                }

                bool balance = inp.Balance || !last;
                double h;
                bool balancedLast = false;
                if (balance)
                {
                    h = Balance(c0, c1, cs, ce, inp.Columns, avail);
                    if (last && inp.Columns > 0)
                    {
                        // Balancing starts from an equal share of everything, trailing margin included, and only grows from there; the
                        // columns end at that height and nothing trails them (evidence: docs/layout-conventions.md, balanced columns).
                        double ideal = Math.Ceiling((inp.StripH - cs) / inp.Columns * 64) / 64;
                        if (h < ideal && ideal <= avail)
                        {
                            h = ideal;
                        }

                        balancedLast = true;
                    }
                }
                else
                {
                    h = double.IsInfinity(avail) ? 1e30 : avail;
                }

                ClearRecord();
                SliceResult sr = Slice(c0, c1, cs, ce, h, int.MaxValue, true);
                if (h >= 1e29 || (!last && h < sr.MaxExtent))
                {
                    h = sr.MaxExtent;
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

                    _pYs[_pN] = _fkYs[i];
                    _pYe[_pN] = _fkYe[i];
                    _pT[_pN] = _fkT[i];
                    _pB[_pN] = _fkB[i];
                    _pN++;
                }

                ClearRecord();
                row.FkCount = sr.Count;
                row.H = h;
                row.OutY = cursor;
                colBase += sr.Count;
                cursor += h;
                stripPos = balancedLast ? inp.StripH : ce;
            }

            if (_rowN == _rows.Length)
            {
                Array.Resize(ref _rows, _rowN * 2);
            }

            _rows[_rowN++] = row;
            if (!last)
            {
                SpannerRecord sp = SpannerAt(r);
                cursor += sp.Top - stripPos;
                sp.OutY = cursor;
                cursor += sp.Bottom - sp.Top;
                stripPos = sp.Bottom;
                if (_spanN == _spans.Length)
                {
                    Array.Resize(ref _spans, _spanN * 2);
                }

                _spans[_spanN++] = sp;
            }
        }

        cursor += inp.StripH - stripPos;
        plan.OutH = cursor;
        plan.Columns = colBase;
        _plans[_planN++] = plan;
        return cursor;
    }
}
