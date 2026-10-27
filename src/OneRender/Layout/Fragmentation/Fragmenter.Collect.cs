using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

internal sealed partial class Fragmenter
{
    private struct Walk
    {
        public int Frag;
        public int Next;
        public double Top;
        public int PrevFrag;
        public double PrevBottom;
        public int Lines;
        public int LineNo;
        public double PrevLineBottom;
        public bool AvoidUp;
        public double CloneTop;
        public double CloneBottom;
        public int Orphans;
        public int Widows;
        public double ContentW;
        public bool IsRoot;
    }

    private struct Filler
    {
        public double Top;
        public double Bottom;
        public double CloneTop;
        public double CloneBottom;
    }

    private struct Span2
    {
        public double Top;
        public double Bottom;
    }

    private BreakCandidate[] _c = new BreakCandidate[64];
    private int _cn;
    private int[] _last0 = new int[64];
    private int[] _last1 = new int[64];
    private int[] _nextForced = new int[65];
    private Filler[] _fillers = new Filler[16];
    private int _fn;
    private Span2[] _floats = new Span2[16];
    private int _flN;
    private Walk[] _walk = new Walk[32];
    private int _wsp;
    private int _steps;

    // Per row of the container (rows are separated by column-span: all boxes): extents of the children that belong to it.
    private double[] _rowFirst = new double[8];
    private double[] _rowLast = new double[8];
    private bool[] _rowHas = new bool[8];
    private SpannerRecord[] _spanTmp = new SpannerRecord[4];
    private int _spanTmpN;
    private bool _spannersOn;

    internal int CandidateCount => _cn;

    /// <summary>
    /// Walks the in-flow children of <paramref name="root"/> (a container whose content was laid out as one tall strip) and collects
    /// the possible break points (§4.1 classes A and B; class C points are not used), the boxes with room to break inside (empty
    /// boxes with a height), floats, and, when asked, the top-level <c>column-span: all</c> boxes that split the flow into rows.
    /// </summary>
    /// <param name="tree">The fragment tree.</param>
    /// <param name="root">The fragment whose children are the strip.</param>
    /// <param name="contentTop">The offset of the strip's top inside the root's border box; strip coordinates start at 0 there.</param>
    /// <param name="kind">Which break values force and discourage breaks.</param>
    /// <param name="spanners">Whether top-level <c>column-span: all</c> children end a row.</param>
    /// <param name="ct">Checked every few hundred fragments.</param>
    internal void Collect(FragmentTree tree, int root, double contentTop, FragmentationKind kind, bool spanners, CancellationToken ct)
    {
        _t = tree;
        _boxes = tree.Boxes;
        _kind = kind;
        _cn = 0;
        _fn = 0;
        _flN = 0;
        _spanTmpN = 0;
        _spannersOn = spanners;
        _steps = 0;
        ResetRow(0);
        _wsp = 0;
        PushWalk(root, -contentTop, true, default, tree.FirstChildOf(root));
        while (_wsp > 0)
        {
            int top = _wsp - 1;
            int c = _walk[top].Next;
            if (c < 0)
            {
                _wsp--;
                continue;
            }

            _walk[top].Next = tree.NextSiblingOf(c);
            if ((++_steps & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            Visit(top, c);
        }
    }

    private void ResetRow(int r)
    {
        _rowFirst = Grown(_rowFirst, r + 1);
        _rowLast = Grown(_rowLast, r + 1);
        _rowHas = Grown(_rowHas, r + 1);
        _rowFirst[r] = 0;
        _rowLast[r] = 0;
        _rowHas[r] = false;
    }

    internal bool RowHasChildren(int r) => _rowHas[r];

    internal double RowFirst(int r) => _rowFirst[r];

    internal double RowLast(int r) => _rowLast[r];

    internal int SpannerCount => _spanTmpN;

    internal SpannerRecord SpannerAt(int i) => _spanTmp[i];

    private void NoteRowChild(double top, double bottom)
    {
        int r = _spanTmpN;
        if (!_rowHas[r])
        {
            _rowHas[r] = true;
            _rowFirst[r] = top;
            _rowLast[r] = Math.Max(top, bottom);
        }
        else
        {
            _rowFirst[r] = Math.Min(_rowFirst[r], top);
            _rowLast[r] = Math.Max(_rowLast[r], bottom);
        }
    }

    private void PushWalk(int frag, double top, bool isRoot, in Walk parent, int firstChild)
    {
        if (_wsp == _walk.Length)
        {
            Array.Resize(ref _walk, _walk.Length * 2);
        }

        Box box = _boxes.Make(_t.BoxOf(frag));
        ref readonly ComputedValues v = ref box.Values;
        Rect bb = _t.BorderBoxOf(frag);
        Insets border = _t.BorderOf(frag), pad = _t.PaddingOf(frag);
        int lines = 0;
        for (int c = firstChild; c >= 0; c = _t.NextSiblingOf(c))
        {
            if ((FragmentKind)_t.KindOf(c) == FragmentKind.Line)
            {
                lines++;
            }
        }

        var w = new Walk
        {
            Frag = frag,
            Next = firstChild,
            Top = top,
            PrevFrag = -1,
            Lines = lines,
            Orphans = v.Orphans,
            Widows = v.Widows,
            ContentW = Math.Max(0, bb.Width - border.Horizontal - pad.Horizontal),
            IsRoot = isRoot,
        };
        if (!isRoot)
        {
            bool clone = v.BoxDecorationBreak == BoxDecorationBreakKind.Clone;
            w.AvoidUp = parent.AvoidUp || AvoidInside(v.BreakInside);
            w.CloneTop = parent.CloneTop + (clone ? border.Top + pad.Top : 0);
            w.CloneBottom = parent.CloneBottom + (clone ? border.Bottom + pad.Bottom : 0);
        }

        _walk[_wsp++] = w;
    }

    private void Visit(int wi, int c)
    {
        FragmentKind k = (FragmentKind)_t.KindOf(c);
        Rect r = _t.BorderBoxOf(c);
        double cTop = _walk[wi].Top + r.Y, cBot = cTop + r.Height;
        if (k == FragmentKind.Line)
        {
            ref Walk lw = ref _walk[wi];
            if (lw.LineNo > 0)
            {
                int level = 0;
                int above = lw.LineNo, below = lw.Lines - above;
                if (above < lw.Orphans || below < lw.Widows)
                {
                    level = 1;
                }

                if (lw.AvoidUp)
                {
                    level = 2;
                }

                Add(Math.Min(lw.PrevLineBottom, cTop), cTop, in lw, level, false, true, BreakBits.None);
            }

            lw.LineNo++;
            lw.PrevLineBottom = cBot;
            return;
        }

        if (k != FragmentKind.Box)
        {
            return;
        }

        Box box = _boxes.Make(_t.BoxOf(c));
        ref readonly ComputedValues v = ref box.Values;
        bool isRoot = _walk[wi].IsRoot;
        if ((box.Flags & BoxTraits.OutOfFlow) != 0)
        {
            bool isFloat = v.Floating != FloatKind.None;
            if (isRoot)
            {
                NoteRowChild(cTop, isFloat ? cBot : cTop);
            }

            if (isFloat)
            {
                if (_flN == _floats.Length)
                {
                    Array.Resize(ref _floats, _floats.Length * 2);
                }

                _floats[_flN++] = new Span2 { Top = cTop, Bottom = cBot };
            }

            return;
        }

        if (box.Kind == BoxKind.Marker)
        {
            if (isRoot)
            {
                NoteRowChild(cTop, cTop);
            }

            return;
        }

        if (isRoot && _spannersOn && v.ColumnSpan == ColumnSpanKind.All && box.Level == BoxLevel.Block)
        {
            if (_spanTmpN == _spanTmp.Length)
            {
                Array.Resize(ref _spanTmp, _spanTmp.Length * 2);
            }

            _spanTmp[_spanTmpN++] = new SpannerRecord { Frag = c, Top = cTop, Bottom = cBot };
            ResetRow(_spanTmpN);
            _walk[wi].PrevFrag = -1;
            return;
        }

        if (isRoot)
        {
            NoteRowChild(cTop, cBot);
        }

        if (_walk[wi].PrevFrag >= 0)
        {
            ClassA(wi, c, cTop, in v);
        }

        _walk[wi].PrevFrag = c;
        _walk[wi].PrevBottom = cBot;
        if (Monolithic(box))
        {
            return;
        }

        bool flow = false;
        for (int g = _t.FirstChildOf(c); g >= 0; g = _t.NextSiblingOf(g))
        {
            if ((FragmentKind)_t.KindOf(g) == FragmentKind.Line || InFlowBox(g))
            {
                flow = true;
                break;
            }
        }

        if (flow)
        {
            Walk parent = _walk[wi];
            PushWalk(c, cTop, false, in parent, _t.FirstChildOf(c));
        }
        else if (r.Height > Eps)
        {
            // An empty box with a height has no break points of its own but can be cut anywhere (the "break anywhere" last resort of
            // §4.4, applied only when the box is the thing that does not fit).
            ref Walk w = ref _walk[wi];
            bool clone = v.BoxDecorationBreak == BoxDecorationBreakKind.Clone;
            if (!w.AvoidUp && !AvoidInside(v.BreakInside))
            {
                if (_fn == _fillers.Length)
                {
                    Array.Resize(ref _fillers, _fillers.Length * 2);
                }

                Insets border = _t.BorderOf(c), pad = _t.PaddingOf(c);
                _fillers[_fn++] = new Filler
                {
                    Top = cTop,
                    Bottom = cBot,
                    CloneTop = w.CloneTop + (clone ? border.Top + pad.Top : 0),
                    CloneBottom = w.CloneBottom + (clone ? border.Bottom + pad.Bottom : 0),
                };
            }
        }
    }

    private bool InFlowBox(int g)
    {
        if ((FragmentKind)_t.KindOf(g) != FragmentKind.Box)
        {
            return false;
        }

        Box b = _boxes.Make(_t.BoxOf(g));
        return (b.Flags & BoxTraits.OutOfFlow) == 0 && b.Kind != BoxKind.Marker;
    }

    // §3.1.1: a break-before on the first in-flow child counts for its parent, a break-after on the last for its parent, all the way up.
    private BreakBits ChainBits(int frag, bool first)
    {
        BreakBits bits = BreakBits.None;
        int x = frag;
        for (int n = 0; x >= 0 && n < 1_000_000; n++)
        {
            Box box = _boxes.Make(_t.BoxOf(x));
            bits |= Map(first ? box.Values.BreakBefore : box.Values.BreakAfter);
            if (Monolithic(box))
            {
                break;
            }

            int next = -1;
            for (int g = _t.FirstChildOf(x); g >= 0; g = _t.NextSiblingOf(g))
            {
                if ((FragmentKind)_t.KindOf(g) == FragmentKind.Line)
                {
                    next = -1;
                    if (first)
                    {
                        break;
                    }

                    continue;
                }

                if (InFlowBox(g))
                {
                    next = g;
                    if (first)
                    {
                        break;
                    }
                }
            }

            x = next;
        }

        return bits;
    }

    private void ClassA(int wi, int c, double cTop, in ComputedValues cv)
    {
        ref Walk w = ref _walk[wi];
        BreakBits bits = ChainBits(w.PrevFrag, false) | ChainBits(c, true);
        double ye = Math.Min(w.PrevBottom, cTop), ys = cTop;
        bool forced = (bits & ForcedMask) != 0;
        int level = 0;
        if (forced)
        {
            // §5.2: the margin before a forced break is truncated, the margin after it is kept.
            Length ml = cv.MarginTop;
            double m = ml.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc ? ml.Resolve(w.ContentW) : 0;
            if (double.IsNaN(m))
            {
                m = 0;
            }

            ys = cTop - Math.Clamp(m, 0, Math.Max(0, ys - ye));
        }
        else if ((bits & AvoidMask) != 0 || w.AvoidUp)
        {
            level = 2;
        }

        Add(ye, ys, in w, level, forced, false, bits);
    }

    private void Add(double ye, double ys, in Walk w, int level, bool forced, bool isLine, BreakBits bits)
    {
        if (_cn == _c.Length)
        {
            Array.Resize(ref _c, _c.Length * 2);
        }

        _c[_cn] = new BreakCandidate
        {
            Ye = ye,
            Ys = ys,
            CloneTop = w.CloneTop,
            CloneBottom = w.CloneBottom,
            Seq = _cn,
            Level = (byte)level,
            Forced = forced,
            IsLine = isLine,
            Bits = bits,
        };
        _cn++;
    }

    private static readonly Comparison<BreakCandidate> ByPosition = static (a, b) =>
    {
        int d = a.Ye.CompareTo(b.Ye);
        return d != 0 ? d : a.Seq.CompareTo(b.Seq);
    };

    /// <summary>Orders the candidates by position and builds the lookup tables slicing uses. Call once after <see cref="Collect"/>.</summary>
    internal void Finish()
    {
        bool sorted = true;
        for (int i = 1; i < _cn; i++)
        {
            if (_c[i - 1].Ye > _c[i].Ye)
            {
                sorted = false;
                break;
            }
        }

        if (!sorted)
        {
            new Span<BreakCandidate>(_c, 0, _cn).Sort(ByPosition);
        }

        // A break that would cut through a float is a last resort: the float stays whole in the earlier fragmentainer.
        if (_flN > 0)
        {
            new Span<Span2>(_floats, 0, _flN).Sort(static (a, b) => a.Top.CompareTo(b.Top));
            int m = 0;
            for (int i = 1; i < _flN; i++)
            {
                if (_floats[i].Top <= _floats[m].Bottom)
                {
                    _floats[m].Bottom = Math.Max(_floats[m].Bottom, _floats[i].Bottom);
                }
                else
                {
                    _floats[++m] = _floats[i];
                }
            }

            int fp = 0;
            for (int i = 0; i < _cn; i++)
            {
                while (fp <= m && _floats[fp].Bottom <= _c[i].Ye + Eps)
                {
                    fp++;
                }

                if (fp > m)
                {
                    break;
                }

                if (!_c[i].Forced && _c[i].Level < 2 && _floats[fp].Top + Eps < _c[i].Ye && _c[i].Ye < _floats[fp].Bottom - Eps)
                {
                    _c[i].Level = 2;
                }
            }
        }

        if (_fn > 1)
        {
            new Span<Filler>(_fillers, 0, _fn).Sort(static (a, b) => a.Top.CompareTo(b.Top));
        }

        _last0 = Grown(_last0, _cn + 1);
        _last1 = Grown(_last1, _cn + 1);
        _nextForced = Grown(_nextForced, _cn + 2);
        int l0 = -1, l1 = -1;
        for (int i = 0; i < _cn; i++)
        {
            if (_c[i].Level == 0)
            {
                l0 = i;
            }

            if (_c[i].Level <= 1)
            {
                l1 = i;
            }

            _last0[i] = l0;
            _last1[i] = l1;
        }

        _nextForced[_cn] = _cn;
        for (int i = _cn - 1; i >= 0; i--)
        {
            _nextForced[i] = _c[i].Forced ? i : _nextForced[i + 1];
        }
    }

    /// <summary>The first candidate whose end position is at or after <paramref name="y"/>, or <see cref="CandidateCount"/>.</summary>
    internal int LowerBound(double y)
    {
        int lo = 0, hi = _cn;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_c[mid].Ye < y)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
