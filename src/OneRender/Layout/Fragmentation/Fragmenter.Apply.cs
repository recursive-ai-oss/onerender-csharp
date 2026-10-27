using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

internal sealed partial class Fragmenter
{
    private struct AFrame
    {
        public int Src;
        public int Next;
        public int KStart;
        public int KEnd;
        public int PiecesBase;
        public double SrcTop;
        public double XShift;
        public double AccBottom;

        /// <summary>The box's own left offset in its parent (source coordinates), for placing continuations of monolithic overflow.</summary>
        public double BoxX;
    }

    private AFrame[] _af = new AFrame[32];
    private int _asp;
    private int[] _pieces = new int[64];
    private int _plTop;
    private double[] _pcTop = new double[64];
    private double[] _pcContent = new double[64];
    private double[] _accL = new double[64];
    private double[] _accT = new double[64];
    private double[] _accR = new double[64];
    private double[] _accB = new double[64];
    private int _pieceBudget;
    private bool _pieceLimitHit;

    // The row being applied.
    private int _rFk;
    private int _rCount;
    private int _rColBase;
    private double _rH;

    /// <summary>True when the piece budget ran out during <see cref="Apply"/> and a box was kept whole in one column.</summary>
    internal bool PieceLimitHit => _pieceLimitHit;

    internal void ClearLimitFlag() => _pieceLimitHit = false;

    private void EnsureFragments(int n)
    {
        if (_pcTop.Length >= n)
        {
            return;
        }

        int size = Math.Max(n, _pcTop.Length * 2);
        Array.Resize(ref _pcTop, size);
        Array.Resize(ref _pcContent, size);
        Array.Resize(ref _accL, size);
        Array.Resize(ref _accT, size);
        Array.Resize(ref _accR, size);
        Array.Resize(ref _accB, size);
    }

    // The first fragmentainer of the row whose content ends after y; the last one takes everything beyond.
    private int FindK(double y, int lo, int hi)
    {
        int a = lo, b = hi;
        while (a < b)
        {
            int mid = (a + b) >> 1;
            if (y < _pYe[_rFk + mid])
            {
                b = mid;
            }
            else
            {
                a = mid + 1;
            }
        }

        return Math.Min(a, hi);
    }

    // The fragmentainer where a box that ends at y ends.
    private int FindKEnd(double y, int lo, int hi)
    {
        int a = lo, b = hi;
        while (a < b)
        {
            int mid = (a + b) >> 1;
            if (y <= _pYe[_rFk + mid] + Eps)
            {
                b = mid;
            }
            else
            {
                a = mid + 1;
            }
        }

        return Math.Min(a, hi);
    }

    /// <summary>
    /// Rewrites the container's fragment as <see cref="Plan"/> decided: its children become columns (and the full-width
    /// <c>column-span: all</c> boxes between them), and each box that crosses a break is split into one fragment per column. The
    /// first fragment of a box keeps its identity, so fragment handles taken before stay valid. Box fragments of one box stay
    /// chained with <see cref="Fragment.NextOfBox"/> in column order.
    /// </summary>
    internal void Apply(FragmentTree tree, int planIndex, CancellationToken ct)
    {
        _t = tree;
        _boxes = tree.Boxes;
        PlanRecord plan = _plans[planIndex];
        int root = plan.Frag;
        EnsureFragments(tree.Count + 16);
        _pieceBudget = MaxPieces;
        int child = tree.DetachChildren(root);
        int row = 0;
        int spanNo = 0;
        StartRow(plan, row, root);
        int steps = 0;
        while (child >= 0)
        {
            int c = child;
            child = tree.NextSiblingOf(c);
            if (spanNo < plan.SpanCount && _spans[plan.SpanStart + spanNo].Frag == c)
            {
                SpannerRecord sp = _spans[plan.SpanStart + spanNo];
                Rect r = tree.BorderBoxOf(c);
                tree.SetBorderBox(new Fragment(tree, c), new Rect(r.X, plan.ContentTop + sp.OutY, r.Width, r.Height));
                tree.AppendChild(root, c);
                spanNo++;
                row++;
                StartRow(plan, row, root);
                continue;
            }

            if (_rCount == 0)
            {
                tree.AppendChild(root, c);
                continue;
            }

            PlaceTop(c, plan);
            if ((++steps & 255) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
        }

        // Scrollable overflow of the container: its own area and the columns, which may stick out sideways (Multicol 1 §8).
        Rect ov = tree.OverflowOf(root);
        double l = ov.X, t = ov.Y, rr = ov.Right, bb = ov.Bottom;
        for (int c = tree.FirstChildOf(root); c >= 0; c = tree.NextSiblingOf(c))
        {
            Rect r = tree.BorderBoxOf(c);
            Rect o = (FragmentKind)tree.KindOf(c) == FragmentKind.Column ? new Rect(_accL[c], _accT[c], _accR[c] - _accL[c], _accB[c] - _accT[c]) : tree.OverflowOf(c);
            l = Math.Min(l, r.X + o.X);
            t = Math.Min(t, r.Y + o.Y);
            rr = Math.Max(rr, r.X + o.Right);
            bb = Math.Max(bb, r.Y + o.Bottom);
        }

        tree.SetOverflowRaw(root, new Rect(l, t, rr - l, bb - t));
        if (_repeats.Count > 0)
        {
            ApplyRepeats(tree);
        }
    }

    // Copies each repeating table's header group to the top of its pieces after the first, and its footer group to the bottom of its
    // pieces before the last.
    private void ApplyRepeats(FragmentTree tree)
    {
        var pieces = new List<int>();
        foreach (RepeatRecord rec in _repeats)
        {
            pieces.Clear();
            Box tb = _boxes.Make(rec.Box);
            for (Fragment f = tree.FragmentOf(tb); !f.IsNull && pieces.Count < MaxFragmentainers; f = f.NextOfBox)
            {
                pieces.Add(f.Index);
            }

            if (pieces.Count < 2)
            {
                continue;
            }

            Rect hr = rec.Header >= 0 ? tree.BorderBoxOf(rec.Header) : default;
            Rect fr = rec.Footer >= 0 ? tree.BorderBoxOf(rec.Footer) : default;
            for (int k = 0; k < pieces.Count; k++)
            {
                int piece = pieces[k];
                int firstBody = -1, lastBody = -1;
                for (int c = tree.FirstChildOf(piece); c >= 0; c = tree.NextSiblingOf(c))
                {
                    if ((FragmentKind)tree.KindOf(c) == FragmentKind.Box && _boxes.Make(tree.BoxOf(c)).Context == FormattingContext.TableRowGroup && (tree.TraitsOf(c) & (byte)FragmentTraits.Repeated) == 0)
                    {
                        if (firstBody < 0)
                        {
                            firstBody = c;
                        }

                        lastBody = c;
                    }
                }

                if (k > 0 && rec.Header >= 0 && firstBody >= 0)
                {
                    tree.CloneSubtree(rec.Header, piece, new Rect(hr.X, tree.BorderBoxOf(firstBody).Y - rec.HeaderReserve, hr.Width, hr.Height));
                }

                if (k < pieces.Count - 1 && rec.Footer >= 0 && lastBody >= 0)
                {
                    Rect lb = tree.BorderBoxOf(lastBody);
                    tree.CloneSubtree(rec.Footer, piece, new Rect(fr.X, lb.Bottom + (rec.FooterReserve - fr.Height), fr.Width, fr.Height));
                }
            }
        }
    }

    private void StartRow(in PlanRecord plan, int row, int root)
    {
        RowRecord rec = _rows[plan.RowStart + row];
        _rFk = rec.FkStart;
        _rCount = rec.FkCount;
        _rColBase = rec.ColBase;
        _rH = rec.H;
        _plTop = 0;
        if (_rCount == 0)
        {
            return;
        }

        if (_pieces.Length < _rCount)
        {
            Array.Resize(ref _pieces, Math.Max(_rCount, _pieces.Length * 2));
        }

        for (int k = 0; k < _rCount; k++)
        {
            double x = plan.Rtl ? plan.ContentLeft + plan.ContentW - plan.ColW - (k * plan.Stride) : plan.ContentLeft + (k * plan.Stride);
            int col = _t.AddColumnFragment(root, new Rect(x, plan.ContentTop + rec.OutY, plan.ColW, rec.H), _rColBase + k);
            EnsureFragments(_t.Count + 16);
            _pieces[k] = col;
            _pcTop[col] = 0;
            _pcContent[col] = 0;
            _accL[col] = 0;
            _accT[col] = 0;
            _accR[col] = plan.ColW;
            _accB[col] = rec.H;
        }

        _plTop = _rCount;
    }

    private void PlaceTop(int c, in PlanRecord plan)
    {
        _asp = 0;
        PushFrame(new AFrame { Src = -1, Next = -1, KStart = 0, KEnd = _rCount - 1, PiecesBase = 0, SrcTop = -plan.ContentTop, XShift = -plan.ContentLeft });
        PlaceChild(0, c);
        while (_asp > 1)
        {
            int top = _asp - 1;
            int next = _af[top].Next;
            if (next < 0)
            {
                FinishFrame(top);
                _asp--;
                continue;
            }

            _af[top].Next = _t.NextSiblingOf(next);
            PlaceChild(top, next);
        }
    }

    private void PushFrame(in AFrame f)
    {
        if (_asp == _af.Length)
        {
            Array.Resize(ref _af, _af.Length * 2);
        }

        _af[_asp++] = f;
    }

    private bool IsLeaf(int c, out Box box)
    {
        box = default;
        if ((FragmentKind)_t.KindOf(c) != FragmentKind.Box)
        {
            return true;
        }

        box = _boxes.Make(_t.BoxOf(c));
        return (box.Flags & BoxTraits.OutOfFlow) != 0 || IsMono(c, box);
    }

    // The top of the first line box among the in-flow descendants that follow a marker, in the coordinates of the marker's parent
    // (whose top is parentTop). Gives up on boxes that don't fragment and after a fixed number of fragments, so it stays linear.
    private bool FirstLineTop(int first, double parentTop, out double top)
    {
        top = 0;
        int cur = first;
        double off = parentTop;
        int depth = 0;
        for (int steps = 0; cur >= 0 && steps < 64; steps++)
        {
            FragmentKind kind = (FragmentKind)_t.KindOf(cur);
            if (kind == FragmentKind.Line)
            {
                top = off + _t.BorderBoxOf(cur).Y;
                return true;
            }

            if (kind == FragmentKind.Box)
            {
                Box b = _boxes.Make(_t.BoxOf(cur));
                if ((b.Flags & (BoxTraits.OutOfFlow | BoxTraits.OutsideMarker)) == 0)
                {
                    if (IsMono(cur, b))
                    {
                        return false;
                    }

                    int child = _t.FirstChildOf(cur);
                    if (child >= 0)
                    {
                        off += _t.BorderBoxOf(cur).Y;
                        depth++;
                        cur = child;
                        continue;
                    }
                }
            }

            while (_t.NextSiblingOf(cur) < 0)
            {
                if (depth == 0)
                {
                    return false;
                }

                cur = _t.ParentOf(cur);
                off -= _t.BorderBoxOf(cur).Y;
                depth--;
            }

            cur = _t.NextSiblingOf(cur);
        }

        return false;
    }

    private void PlaceChild(int pi, int c)
    {
        AFrame p = _af[pi];
        Rect r = _t.BorderBoxOf(c);
        double cTop = p.SrcTop + r.Y, cBot = cTop + r.Height;
        if (IsLeaf(c, out Box box))
        {
            if (!box.IsNull && box.Kind == BoxKind.Marker && (box.Flags & BoxTraits.OutsideMarker) != 0 && FirstLineTop(p.Next, p.SrcTop, out double lineTop) && lineTop > cTop)
            {
                // CSS Lists 3 §3: an outside marker sits beside the item's first line box, so it follows that line into the column it lands in.
                r = new Rect(r.X, r.Y + (lineTop - cTop), r.Width, r.Height);
                cTop = lineTop;
                cBot = cTop + r.Height;
            }

            int k = FindK(cTop, p.KStart, p.KEnd);
            int parentPiece = _pieces[p.PiecesBase + (k - p.KStart)];
            double colY = cTop - _pYs[_rFk + k] + _pT[_rFk + k];
            double newY = colY - _pcTop[parentPiece];
            var nr = new Rect(r.X + p.XShift, newY, r.Width, r.Height);
            _t.SetBorderBox(new Fragment(_t, c), nr);
            _t.AppendChild(parentPiece, c);
            SetIndexSubtree(c, _rColBase + k);
            Rect ov = _t.OverflowOf(c);
            if (!box.IsNull)
            {
                ref readonly ComputedValues v = ref box.Values;
                if (v.OverflowX != OverflowKind.Visible)
                {
                    ov = new Rect(0, ov.Y, r.Width, ov.Height);
                }

                if (v.OverflowY != OverflowKind.Visible)
                {
                    ov = new Rect(ov.X, 0, ov.Width, r.Height);
                }
            }

            Contribute(parentPiece, nr, ov);
            if (_kind == FragmentationKind.Page && !box.IsNull && box.Kind != BoxKind.Marker && ((box.Flags & BoxTraits.OutOfFlow) == 0 || box.Values.Floating != FloatKind.None) && cBot > cTop + Eps)
            {
                ContinueMonolithic(pi, c, in box, r, cBot, k);
            }

            return;
        }

        int kS = FindK(cTop, p.KStart, p.KEnd);
        int kE = Math.Max(kS, FindKEnd(cBot, p.KStart, p.KEnd));
        int count = kE - kS + 1;
        if (count > 1 && _pieceBudget < count)
        {
            // The piece budget is spent: keep the rest of this box in the fragmentainer where it starts.
            _pieceLimitHit = true;
            kE = kS;
            count = 1;
        }

        _pieceBudget -= count - 1;
        if (_plTop + count > _pieces.Length)
        {
            Array.Resize(ref _pieces, Math.Max(_plTop + count, _pieces.Length * 2));
        }

        int pbase = _plTop;
        _plTop += count;
        Insets border = _t.BorderOf(c), pad = _t.PaddingOf(c);
        bool clone = box.Values.BoxDecorationBreak == BoxDecorationBreakKind.Clone;
        int firstChild = _t.DetachChildren(c);
        int prev = c;
        for (int j = 0; j < count; j++)
        {
            int k = kS + j;
            int parentPiece = _pieces[p.PiecesBase + (k - p.KStart)];
            double topCol = j == 0 ? cTop - _pYs[_rFk + k] + _pT[_rFk + k] : _pcContent[parentPiece];
            double botCol = k == kE ? cBot - _pYs[_rFk + k] + _pT[_rFk + k] : _rH - p.AccBottom;
            double h = Math.Max(0, botCol - topCol);
            bool first = j == 0, lastPiece = j == count - 1;
            var b2 = new Insets(first || clone ? border.Top : 0, border.Right, lastPiece || clone ? border.Bottom : 0, border.Left);
            var p2 = new Insets(first || clone ? pad.Top : 0, pad.Right, lastPiece || clone ? pad.Bottom : 0, pad.Left);
            var rect = new Rect(r.X + p.XShift, topCol - _pcTop[parentPiece], r.Width, h);
            int piece;
            if (first)
            {
                piece = c;
            }
            else
            {
                piece = _t.NewPiece(c, prev, rect);
                EnsureFragments(_t.Count + 16);
            }

            _t.SetRaw(piece, rect, b2, p2);
            _t.AppendChild(parentPiece, piece);
            _t.SetFragmentainer(piece, _rColBase + k);
            _pcTop[piece] = topCol;
            _pcContent[piece] = topCol + b2.Top + p2.Top;
            _accL[piece] = 0;
            _accT[piece] = 0;
            _accR[piece] = r.Width;
            _accB[piece] = h;
            _pieces[pbase + j] = piece;
            prev = piece;
        }

        PushFrame(new AFrame
        {
            Src = c,
            Next = firstChild,
            KStart = kS,
            KEnd = kE,
            PiecesBase = pbase,
            SrcTop = cTop,
            XShift = 0,
            AccBottom = p.AccBottom + (clone ? border.Bottom + pad.Bottom : 0),
            BoxX = r.X,
        });
    }

    // Monolithic overflow (CSS Break 4 §4.1): a box that cannot break and is taller than the page stays whole on the page it starts
    // on; its background and borders carry on in the pages the overflow reaches, as pieces without content, attached to the nearest
    // ancestor that has a piece on that page.
    private void ContinueMonolithic(int pi, int c, in Box box, Rect r, double cBot, int k)
    {
        int rootEnd = _af[0].KEnd;
        int kE = Math.Max(k, FindKEnd(cBot, k, rootEnd));
        if (kE <= k)
        {
            return;
        }

        int count = kE - k;
        if (_pieceBudget < count)
        {
            _pieceLimitHit = true;
            return;
        }

        _pieceBudget -= count;
        Insets border = _t.BorderOf(c), pad = _t.PaddingOf(c);
        int prev = c;
        for (int j = k + 1; j <= kE; j++)
        {
            int fi = pi;
            while (fi > 0 && (j < _af[fi].KStart || j > _af[fi].KEnd))
            {
                fi--;
            }

            double x = r.X + _af[0].XShift * (fi == 0 ? 1 : 0);
            for (int i = fi + 1; i <= pi; i++)
            {
                x += _af[i].BoxX;
            }

            int parentPiece = _pieces[_af[fi].PiecesBase + (j - _af[fi].KStart)];
            double top = _pT[_rFk + j];
            double bottom = j == kE ? cBot - _pYs[_rFk + j] + _pT[_rFk + j] : _pYe[_rFk + j] - _pYs[_rFk + j] + _pT[_rFk + j];
            var rect = new Rect(x, top - _pcTop[parentPiece], r.Width, Math.Max(0, bottom - top));
            int piece = _t.NewPiece(c, prev, rect);
            EnsureFragments(_t.Count + 16);
            var b2 = new Insets(0, border.Right, j == kE ? border.Bottom : 0, border.Left);
            var p2 = new Insets(0, pad.Right, j == kE ? pad.Bottom : 0, pad.Left);
            _t.SetRaw(piece, rect, b2, p2);
            _t.AppendChild(parentPiece, piece);
            _t.SetFragmentainer(piece, _rColBase + j);
            _pcTop[piece] = top;
            _pcContent[piece] = top;
            _accL[piece] = 0;
            _accT[piece] = 0;
            _accR[piece] = r.Width;
            _accB[piece] = rect.Height;
            Contribute(parentPiece, rect, new Rect(0, 0, rect.Width, rect.Height));
            prev = piece;
        }
    }

    private void Contribute(int parentPiece, Rect rect, Rect overflow)
    {
        _accL[parentPiece] = Math.Min(_accL[parentPiece], rect.X + overflow.X);
        _accT[parentPiece] = Math.Min(_accT[parentPiece], rect.Y + overflow.Y);
        _accR[parentPiece] = Math.Max(_accR[parentPiece], rect.X + overflow.Right);
        _accB[parentPiece] = Math.Max(_accB[parentPiece], rect.Y + overflow.Bottom);
    }

    private void FinishFrame(int top)
    {
        AFrame f = _af[top];
        AFrame parent = _af[top - 1];
        int count = f.KEnd - f.KStart + 1;
        for (int j = 0; j < count; j++)
        {
            int piece = _pieces[f.PiecesBase + j];
            var ov = new Rect(_accL[piece], _accT[piece], _accR[piece] - _accL[piece], _accB[piece] - _accT[piece]);
            _t.SetOverflowRaw(piece, ov);
            int parentPiece = _pieces[parent.PiecesBase + (f.KStart + j - parent.KStart)];
            Contribute(parentPiece, _t.BorderBoxOf(piece), ov);
        }

        _plTop = f.PiecesBase;
    }

    private void SetIndexSubtree(int c, int index)
    {
        int i = c;
        while (true)
        {
            _t.SetFragmentainer(i, index);
            int f = _t.FirstChildOf(i);
            if (f >= 0)
            {
                i = f;
                continue;
            }

            while (i != c && _t.NextSiblingOf(i) < 0)
            {
                i = _t.ParentOf(i);
            }

            if (i == c)
            {
                return;
            }

            i = _t.NextSiblingOf(i);
        }
    }
}
