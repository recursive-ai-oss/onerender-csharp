using OneFont;
using OneRender.Style;
using OneRender.Text;
using OneRender.Tree;

namespace OneRender.Layout;

internal sealed partial class InlineWorker
{
    // ------------------------------------------------------------------ font metrics and line-height

    private void FontBox(in ComputedValues v, StyleId style, out double a, out double d, out double gap, out double xh)
    {
        int slot = style.Index & 255;
        int version = _fonts.Catalog.Version;
        if (_mcStyle[slot] == style.Index && _mcVersion[slot] == version && style.Index != 0)
        {
            a = _mcA[slot]; d = _mcD[slot]; gap = _mcGap[slot]; xh = _mcXh[slot];
            return;
        }

        FontMetricsInfo m = _fonts.GetMetrics(in v);
        // The reference browser works with whole-pixel ascent, descent and line gap; matching it keeps line boxes on its pixel grid
        // (evidence: the inline lab differential).
        a = Math.Round(m.Ascent, MidpointRounding.AwayFromZero);
        d = Math.Round(m.Descent, MidpointRounding.AwayFromZero);
        gap = Math.Round(m.LineGap, MidpointRounding.AwayFromZero);
        xh = m.XHeight > 0 ? m.XHeight : v.FontSize * 0.5;
        _mcStyle[slot] = style.Index;
        _mcVersion[slot] = version;
        _mcA[slot] = a; _mcD[slot] = d; _mcGap[slot] = gap; _mcXh[slot] = xh;
    }

    private static double LineHeightOf(in ComputedValues v, double a, double d, double gap)
    {
        Length lh = v.LineHeight;
        return lh.Kind switch
        {
            LengthKind.Number => lh.Value * v.FontSize,
            LengthKind.Px => lh.Value,
            LengthKind.Percent => lh.Value / 100 * v.FontSize,
            LengthKind.Calc => lh.Resolve(v.FontSize),
            _ => a + d + gap, // normal
        };
    }

    private void InitBoxMetrics(int b, bool isRoot)
    {
        Box bx = _boxes.Make(b);
        ref readonly ComputedValues v = ref bx.Values;
        FontBox(in v, bx.Style, out double a, out double d, out double gap, out double xh);
        double l = LineHeightOf(in v, a, d, gap);
        ref BM m = ref _bm[b];
        m.Stamp = _stamp;
        m.A = a;
        m.D = d;
        // Half-leading: the top half is rounded down to a whole pixel and the bottom half takes the rest.
        double leading = l - (a + d);
        m.HL = Math.Floor(leading / 2);
        m.HB = leading - m.HL;
        m.XH = xh;
        m.Size = v.FontSize;
        m.Mode = 0;
        m.Excl = false;
        m.Shift = 0;
        m.Abs = 0;
        m.Parent = -1;
        if (_tn == _touched.Length)
        {
            Array.Resize(ref _touched, _tn * 2);
        }

        _touched[_tn++] = b;
        if (isRoot)
        {
            return;
        }

        int parent = bx.Parent.Index;
        m.Parent = parent;
        ref BM p = ref _bm[parent];
        AlignShift(in v, l, m.A, m.D, m.HL, m.HB, ref p, out m.Mode, out m.Shift);
        m.Excl = m.Mode != 0 || p.Excl;
        m.Abs = p.Abs + m.Shift;
    }

    /// <summary>
    /// CSS2 §10.8.1 <c>vertical-align</c>: where this box's baseline sits relative to its parent's baseline, y down. Boxes aligned to the
    /// line's top or bottom are placed once the line height is known. The shifts for <c>sub</c> and <c>super</c> are the usual
    /// browser values (the spec leaves them to the user agent).
    /// </summary>
    private static void AlignShift(in ComputedValues v, double lineHeight, double a, double d, double hl, double hb, ref BM parent, out byte mode, out double shift)
    {
        mode = 0;
        shift = 0;
        switch (v.VerticalAlign)
        {
            case VerticalAlignKind.Sub:
                shift = parent.Size / 5 + 1;
                break;
            case VerticalAlignKind.Super:
                shift = -(parent.Size / 3 + 1);
                break;
            case VerticalAlignKind.TextTop:
                shift = -parent.A + a + hl;
                break;
            case VerticalAlignKind.TextBottom:
                shift = parent.D - (d + hb);
                break;
            case VerticalAlignKind.Middle:
                shift = -parent.XH / 2 - (d - a) / 2;
                break;
            case VerticalAlignKind.Top:
                mode = 1;
                break;
            case VerticalAlignKind.Bottom:
                mode = 2;
                break;
            case VerticalAlignKind.Length:
                shift = -v.VerticalAlignOffset.Resolve(lineHeight);
                break;
            default:
                break;
        }
    }

    private void EnsureBox(int b)
    {
        if (_bm[b].Stamp == _stamp)
        {
            return;
        }

        int root = _container.Index;
        int cnt = 0;
        int cur = b;
        while (cur != root && _bm[cur].Stamp != _stamp)
        {
            if (cnt == _chain.Length)
            {
                Array.Resize(ref _chain, cnt * 2);
            }

            _chain[cnt++] = cur;
            cur = _boxes.Make(cur).Parent.Index;
        }

        if (cur == root && _bm[root].Stamp != _stamp)
        {
            InitBoxMetrics(root, true);
        }

        while (cnt > 0)
        {
            InitBoxMetrics(_chain[--cnt], false);
        }
    }

    // ------------------------------------------------------------------ one line

    private int Lca(int a, int b)
    {
        while (a != b)
        {
            if (_bdepth[a] > _bdepth[b])
            {
                a = _boxes.Make(a).Parent.Index;
            }
            else if (_bdepth[b] > _bdepth[a])
            {
                b = _boxes.Make(b).Parent.Index;
            }
            else
            {
                a = _boxes.Make(a).Parent.Index;
                b = _boxes.Make(b).Parent.Index;
            }
        }

        return a;
    }

    // A finished segment widens its parent: an inline box's fragment spans the fragments of the boxes inside it.
    private int CloseSeg(int seg)
    {
        int parent = _sg[seg].Parent;
        if (parent >= 0)
        {
            _sg[parent].MinX = Math.Min(_sg[parent].MinX, _sg[seg].MinX);
            _sg[parent].MaxX = Math.Max(_sg[parent].MaxX, _sg[seg].MaxX);
        }

        return parent;
    }

    private int PushSeg(int parent, int box, int depth)
    {
        Ensure(ref _sg, _sgn + 1);
        ref Seg s = ref _sg[_sgn];
        s = default;
        s.Box = box;
        s.Parent = parent;
        s.Depth = depth;
        s.MinX = double.PositiveInfinity;
        s.MaxX = double.NegativeInfinity;
        return _sgn++;
    }

    private static int AlignMode(TextAlignKind k, bool rtl) => k switch
    {
        TextAlignKind.Left => 0,
        TextAlignKind.Right => 1,
        TextAlignKind.Center => 2,
        TextAlignKind.Justify or TextAlignKind.JustifyAll => 3,
        TextAlignKind.End => rtl ? 0 : 1,
        _ => rtl ? 1 : 0, // start, match-parent (resolved when computed)
    };

    /// <summary>
    /// Vertical metrics of the line in <c>_pc</c> (CSS2 §10.8, CSS Inline 3 §4): the box and atomic baselines, the line box height
    /// and the baseline's distance from the line top.
    /// </summary>
    private void VerticalMetrics(out double rb, out double lineH)
    {
        int root = _container.Index;
        _stamp++;
        _tn = 0;
        EnsureBox(root);

        // ---- vertical metrics (CSS2 §10.8, CSS Inline 3 §4)
        double minTop = double.PositiveInfinity, maxBottom = double.NegativeInfinity, tallAligned = 0;
        for (int i = 0; i < _pn; i++)
        {
            ref Piece p = ref _pc[i];
            EnsureBox(p.Owner);
            if (p.Kind != PK.Atomic)
            {
                continue;
            }

            ref Elem el = ref _el[p.Elem];
            Box ab = _boxes.Make(el.Box);
            ref readonly ComputedValues av = ref ab.Values;
            ref BM ob = ref _bm[p.Owner];
            double a = el.Base, d = el.H - el.Base;
            double l = LineHeightOf(in av, a, d, 0);
            AlignShift(in av, l, a, d, 0, 0, ref ob, out byte mode, out double shift);
            p.VMode = mode;
            p.Shift = shift;
            if (mode != 0)
            {
                tallAligned = Math.Max(tallAligned, el.H);
            }
            else if (!ob.Excl)
            {
                double bs = ob.Abs + shift;
                minTop = Math.Min(minTop, bs - a);
                maxBottom = Math.Max(maxBottom, bs + d);
            }
        }

        for (int i = 0; i < _tn; i++)
        {
            ref BM m = ref _bm[_touched[i]];
            if (m.Mode != 0)
            {
                tallAligned = Math.Max(tallAligned, m.A + m.D + m.HL + m.HB);
            }
            else if (!m.Excl)
            {
                minTop = Math.Min(minTop, m.Abs - (m.A + m.HL));
                maxBottom = Math.Max(maxBottom, m.Abs + m.D + m.HB);
            }
        }

        rb = -minTop;
        lineH = Math.Max(maxBottom - minTop, tallAligned);
        for (int i = 0; i < _tn; i++)
        {
            ref BM m = ref _bm[_touched[i]];
            if (m.Parent >= 0)
            {
                ref BM par = ref _bm[m.Parent];
                m.Abs = m.Mode switch
                {
                    1 => m.A + m.HL - rb,
                    2 => lineH - rb - (m.D + m.HB),
                    _ => par.Abs + m.Shift,
                };
            }

            m.Base = rb + m.Abs;
        }

        for (int i = 0; i < _pn; i++)
        {
            ref Piece p = ref _pc[i];
            if (p.Kind != PK.Atomic)
            {
                continue;
            }

            ref Elem el = ref _el[p.Elem];
            double d = el.H - el.Base;
            p.Base = p.VMode switch
            {
                1 => el.Base,
                2 => lineH - d,
                _ => _bm[p.Owner].Base + p.Shift,
            };
        }

    }

    /// <summary>Places the pieces built by <see cref="BuildPieces"/> as one line box and adds its fragments.</summary>
    /// <returns>The line box height.</returns>
    private double EmitLine(in InlineLayoutRequest request, double y, double xOff, double lineW, double indent, bool lastLine, out double baseline)
    {
        ref readonly ComputedValues cv = ref _container.Values;
        bool baseRtl = (_paraLevel & 1) != 0;
        int root = _container.Index;
        VerticalMetrics(out double rb, out double lineH);

        // ---- alignment and justification (CSS Text 3 §7)
        double total = 0, hang = 0;
        int seps = 0;
        for (int i = 0; i < _pn; i++)
        {
            total += _pc[i].W;
            seps += _pc[i].Seps;
        }

        // Preserved spaces at the end of a wrapping line hang: they stay but do not count when aligning.
        for (int i = _pn - 1; i >= 0; i--)
        {
            ref Piece p = ref _pc[i];
            if (p.Kind == PK.Close)
            {
                continue;
            }

            if (p.Kind == PK.Text && (_el[p.Elem].Flags & EF.Wrap) != 0)
            {
                ref Elem el = ref _el[p.Elem];
                ReadOnlySpan<GlyphCluster> cls = _shaped[p.Elem]!.Clusters;
                int k = cls.Length;
                bool rtlRun = (el.Flags & EF.Rtl) != 0;
                for (int c = p.Cb - 1; c >= p.Ca; c--)
                {
                    GlyphCluster cl = cls[rtlRun ? k - 1 - c : c];
                    byte fl = _cf[el.Start + cl.TextStart];
                    if ((fl & CfPreserved) == 0 || (fl & CfBreakSpace) != 0)
                    {
                        break;
                    }

                    hang += ClusterAdvance(in el, in cl, fl);
                    seps--;
                }
            }

            break;
        }

        int ta;
        if (!lastLine || cv.TextAlign == TextAlignKind.JustifyAll)
        {
            ta = AlignMode(cv.TextAlign, baseRtl);
        }
        else
        {
            ta = cv.TextAlignLast == TextAlignLastKind.Auto
                ? (cv.TextAlign == TextAlignKind.Justify ? AlignMode(TextAlignKind.Start, baseRtl) : AlignMode(cv.TextAlign, baseRtl))
                : cv.TextAlignLast switch
                {
                    TextAlignLastKind.Left => 0,
                    TextAlignLastKind.Right => 1,
                    TextAlignLastKind.Center => 2,
                    TextAlignLastKind.Justify => 3,
                    TextAlignLastKind.End => baseRtl ? 0 : 1,
                    _ => baseRtl ? 1 : 0,
                };
        }

        if (ta == 3 && cv.TextJustify == TextJustifyKind.None)
        {
            ta = baseRtl ? 1 : 0;
        }

        double regionLeft = baseRtl ? 0 : indent;
        double regionW = lineW - indent;
        double content = total - hang;
        double free = regionW - content;
        double extra = 0;
        double x0;
        if (free < 0)
        {
            // CSS Text 3 §7.1: content that does not fit is start-aligned.
            x0 = baseRtl ? regionLeft + free : regionLeft;
        }
        else if (ta == 3)
        {
            if (seps > 0)
            {
                extra = free / seps;
            }

            x0 = regionLeft;
        }
        else
        {
            x0 = regionLeft + (ta == 1 ? free : ta == 2 ? free / 2 : 0);
        }

        if (extra > 0)
        {
            for (int i = 0; i < _pn; i++)
            {
                _pc[i].W += _pc[i].Seps * extra;
            }
        }

        // ---- bidi reordering of the pieces (UAX #9 rule L2)
        Ensure(ref _order, _pn);
        Ensure(ref _plv, _pn);
        for (int i = 0; i < _pn; i++)
        {
            _plv[i] = _pc[i].Level;
        }

        TextShaping.GetVisualOrder(_plv.AsSpan(0, _pn), _order.AsSpan(0, _pn));
        double x = x0;
        for (int k = 0; k < _pn; k++)
        {
            ref Piece p = ref _pc[_order[k]];
            p.X = x;
            x += p.W;
        }

        _maxRight = Math.Max(_maxRight, x);

        // ---- fragment segments: one box fragment per run of consecutive pieces of an inline box
        _sgn = 0;
        int top = -1;
        for (int k = 0; k < _pn; k++)
        {
            ref Piece p = ref _pc[_order[k]];
            int owner = p.Owner;
            int curBox = top >= 0 ? _sg[top].Box : root;
            if (curBox != owner)
            {
                int lca = Lca(curBox, owner);
                int lcaDepth = _bdepth[lca];
                while (top >= 0 && _sg[top].Depth > lcaDepth)
                {
                    top = CloseSeg(top);
                }

                int cnt = 0;
                for (int b = owner; b != lca; b = _boxes.Make(b).Parent.Index)
                {
                    Ensure(ref _path, cnt + 1);
                    _path[cnt++] = b;
                }

                for (int q = cnt - 1; q >= 0; q--)
                {
                    top = PushSeg(top, _path[q], _bdepth[_path[q]]);
                }
            }

            p.Seg = top;
            if (top < 0)
            {
                continue;
            }

            ref Seg s = ref _sg[top];
            double lo = p.X, hi = p.X + p.W;
            if (p.Kind == PK.Open)
            {
                s.HasOpen = (_el[p.Elem].Flags & EF.NoEdge) == 0;
                if (V(s.Box).Direction == DirectionKind.Ltr) { lo += _el[p.Elem].Outer; } else { hi -= _el[p.Elem].Outer; }
            }
            else if (p.Kind == PK.Close)
            {
                s.HasClose = (_el[p.Elem].Flags & EF.NoEdge) == 0;
                if (V(s.Box).Direction == DirectionKind.Ltr) { hi -= _el[p.Elem].Outer; } else { lo += _el[p.Elem].Outer; }
            }

            s.MinX = Math.Min(s.MinX, lo);
            s.MaxX = Math.Max(s.MaxX, hi);
        }

        while (top >= 0)
        {
            top = CloseSeg(top);
        }

        // ---- fragments
        Fragment line = _out.AddLine(_parent, new Rect(request.ContentLeft + xOff, request.ContentTop + y, lineW, lineH), rb);
        _lineX = request.ContentLeft + xOff;
        _lineY = request.ContentTop + y;
        AddOverflow(0, 0, lineW, lineH);
        AddOverflow(x0, 0, x - x0, lineH); // text that runs past the line end
        int cur = -1;
        Fragment curFrag = line;
        double ox = 0, oy = 0;
        bool inkSeen = false;
        for (int k = 0; k < _pn; k++)
        {
            ref Piece p = ref _pc[_order[k]];
            int target = p.Seg;
            if (cur != target)
            {
                int a = cur, b = target, cnt = 0;
                while (a != b)
                {
                    int da = a < 0 ? -1 : _sg[a].Depth;
                    int db = b < 0 ? -1 : _sg[b].Depth;
                    if (da > db)
                    {
                        a = _sg[a].Parent;
                    }
                    else
                    {
                        Ensure(ref _path, cnt + 1);
                        _path[cnt++] = b;
                        b = _sg[b].Parent;
                    }
                }

                cur = a;
                curFrag = cur >= 0 ? FragmentOfSeg(cur) : line;
                for (int q = cnt - 1; q >= 0; q--)
                {
                    curFrag = CreateSegFragment(_path[q], cur, curFrag);
                    cur = _path[q];
                }

                ox = cur >= 0 ? _sg[cur].OriginX : 0;
                oy = cur >= 0 ? _sg[cur].OriginY : 0;
            }

            switch (p.Kind)
            {
                case PK.Text:
                    EmitText(in p, curFrag, ox, oy, false);
                    break;
                case PK.Hyphen:
                    EmitText(in p, curFrag, ox, oy, true);
                    break;
                case PK.Atomic:
                {
                    ref Elem el = ref _el[p.Elem];
                    if (el.Frag >= 0)
                    {
                        Fragment af = _out.Make(el.Frag);
                        Rect bb = af.BorderBox;
                        _out.Reparent(af, curFrag);
                        _out.SetBorderBox(af, new Rect(p.X + el.Ml - ox, p.Base - el.Base + el.Mt - oy, bb.Width, bb.Height));
                        AddOverflow(p.X + el.Ml, p.Base - el.Base + el.Mt, bb.Width, bb.Height);
                    }

                    break;
                }

                case PK.Abs:
                {
                    // CSS2 §10.1 item 4: a positioned inline box is the containing block of the boxes inside it.
                    int cbf = -1;
                    for (int sIdx = p.Seg; sIdx >= 0; sIdx = _sg[sIdx].Parent)
                    {
                        if (V(_sg[sIdx].Box).Position != PositionKind.Static)
                        {
                            cbf = _segFragIndex[sIdx];
                            break;
                        }
                    }

                    QueueAbs(in request, p.Elem, _lineX + p.X, _lineY, cbf, inkSeen);
                    break;
                }

                case PK.Br:
                {
                    ref Elem el = ref _el[p.Elem];
                    ref BM ob = ref _bm[p.Owner];
                    _out.AddBox(curFrag, _boxes.Make(el.Box), new Rect(p.X - ox, ob.Base - ob.A - oy, 0, ob.A + ob.D));
                    break;
                }

                default:
                    break;
            }

            if (p.Kind != PK.Abs && p.W > Eps)
            {
                inkSeen = true;
            }
        }

        baseline = rb;
        return lineH;
    }

    private int[] _segFragIndex = new int[16];

    private Fragment FragmentOfSeg(int seg) => _out.Make(_segFragIndex[seg]);

    private Fragment CreateSegFragment(int seg, int parentSeg, Fragment parentFrag)
    {
        ref Seg s = ref _sg[seg];
        Box bx = _boxes.Make(s.Box);
        ref readonly ComputedValues v = ref bx.Values;
        ref BM m = ref _bm[s.Box];
        double bw = _availW;
        bool rtl = v.Direction == DirectionKind.Rtl;
        double bt = v.BorderTopWidth, bb = v.BorderBottomWidth;
        double pt = Math.Max(0, Edge(v.PaddingTop, bw)), pb = Math.Max(0, Edge(v.PaddingBottom, bw));
        double pl = Math.Max(0, Edge(v.PaddingLeft, bw)), pr = Math.Max(0, Edge(v.PaddingRight, bw));
        double blw = v.BorderLeftWidth, brw = v.BorderRightWidth;
        bool hasStart = s.HasOpen, hasEnd = s.HasClose;
        double leftB = rtl ? (hasEnd ? blw : 0) : (hasStart ? blw : 0);
        double rightB = rtl ? (hasStart ? brw : 0) : (hasEnd ? brw : 0);
        double leftP = rtl ? (hasEnd ? pl : 0) : (hasStart ? pl : 0);
        double rightP = rtl ? (hasStart ? pr : 0) : (hasEnd ? pr : 0);
        double top = m.Base - m.A - bt - pt;
        double h = m.A + m.D + bt + bb + pt + pb;
        double px = parentSeg >= 0 ? _sg[parentSeg].OriginX : 0;
        double py = parentSeg >= 0 ? _sg[parentSeg].OriginY : 0;
        double minX = double.IsInfinity(s.MinX) ? 0 : s.MinX;
        double maxX = double.IsInfinity(s.MaxX) ? minX : s.MaxX;
        s.OriginX = minX;
        s.OriginY = top;
        AddOverflow(minX, top, maxX - minX, h);
        // CSS2 §9.4.3: a relatively positioned inline box is drawn shifted; its content moves with it and the line does not change.
        BlockLayoutEngine.RelativeOffset(in v, bw, double.NaN, _container.Values.Direction == DirectionKind.Rtl, out double relDx, out double relDy);
        Fragment f = _out.AddBox(parentFrag, bx, new Rect(minX - px + relDx, top - py + relDy, maxX - minX, h));
        _out.SetEdges(f, new Insets(bt, rightB, bb, leftB), new Insets(pt, rightP, pb, leftP));
        _out.SetBaseline(f, bt + pt + m.A);
        FragmentTraits tr = FragmentTraits.None;
        if (!s.HasOpen)
        {
            tr |= FragmentTraits.ContinuesBefore;
        }

        if (!s.HasClose)
        {
            tr |= FragmentTraits.ContinuesAfter;
        }

        _out.SetTraits(f, tr);
        Ensure(ref _segFragIndex, seg + 1);
        _segFragIndex[seg] = f.Index;
        return f;
    }

    // ------------------------------------------------------------------ glyph runs

    /// <summary>
    /// Adds the text fragments of one piece: one per font face the piece's clusters use. Glyph positions are the shaper's, plus
    /// letter-spacing, word-spacing and justification, and the piece's baseline shift. Everything is in the fragment's own space.
    /// </summary>
    private void EmitText(in Piece p, Fragment parent, double ox, double oy, bool hyphen)
    {
        ref Elem el = ref _el[p.Elem];
        ref BM ob = ref _bm[p.Owner];
        Box tb = _boxes.Make(el.Box);
        ShapedText st = hyphen ? _fonts.Shape("-", in tb.Values, (el.Flags & EF.Rtl) != 0) : _shaped[p.Elem]!;
        ReadOnlySpan<GlyphCluster> cls = st.Clusters;
        ReadOnlySpan<ShapedTextSegment> segs = st.Segments;
        ReadOnlySpan<int> ids = st.GlyphIds;
        ReadOnlySpan<float> gxs = st.X;
        ReadOnlySpan<float> gys = st.Y;
        int k = cls.Length;
        bool rtl = (el.Flags & EF.Rtl) != 0;
        int ca = hyphen ? 0 : p.Ca;
        int cb = hyphen ? k : p.Cb;
        int va = rtl ? k - cb : ca;
        int vb = rtl ? k - ca : cb;
        if (va >= vb)
        {
            return;
        }

        double sepExtra = 0;
        if (!hyphen && p.Seps > 0)
        {
            // The width the piece gained from justification, shared among its separators.
            double natural = TextWidth(p.Elem, p.Ca, p.Cb, out _);
            sepExtra = (p.W - natural) / p.Seps;
        }

        double pen0 = 0;
        for (int v = 0; v < va; v++)
        {
            pen0 += cls[v].Advance;
        }

        int g0 = cls[va].GlyphStart;
        int g1 = cls[vb - 1].GlyphStart + cls[vb - 1].GlyphCount;
        int gn = g1 - g0;
        Ensure(ref _gid, gn);
        Ensure(ref _gx, gn);
        Ensure(ref _gy, gn);
        Ensure(ref _gc, gn);
        Ensure(ref _clx, vb - va + 1);
        double cx = 0;
        double pen = pen0;
        double yShift = ob.Abs;
        for (int v = va; v < vb; v++)
        {
            GlyphCluster cl = cls[v];
            byte fl = hyphen ? (byte)0 : _cf[el.Start + cl.TextStart];
            double ex = el.Ls;
            if ((fl & CfWordSep) != 0)
            {
                ex += (el.Flags & EF.WsPercent) != 0 ? el.Ws * cl.Advance : el.Ws;
                ex += sepExtra;
            }

            _clx[v - va] = cx;
            double shiftRtl = rtl ? ex : 0;
            int srcOff = hyphen ? 0 : _src[el.Start + cl.TextStart];
            for (int g = cl.GlyphStart; g < cl.GlyphStart + cl.GlyphCount; g++)
            {
                int j = g - g0;
                _gid[j] = ids[g];
                _gx[j] = cx + shiftRtl + (gxs[g] - pen);
                _gy[j] = gys[g] + yShift;
                _gc[j] = srcOff;
            }

            cx += cl.Advance + ex;
            pen += cl.Advance;
        }

        _clx[vb - va] = cx;
        for (int s = 0; s < segs.Length; s++)
        {
            ShapedTextSegment sg = segs[s];
            int lo = Math.Max(va, sg.ClusterStart);
            int hi = Math.Min(vb, sg.ClusterStart + sg.ClusterCount);
            if (lo >= hi)
            {
                continue;
            }

            int gs = cls[lo].GlyphStart - g0;
            int ge = cls[hi - 1].GlyphStart + cls[hi - 1].GlyphCount - g0;
            double x0 = _clx[lo - va];
            double x1 = _clx[hi - va];
            for (int j = gs; j < ge; j++)
            {
                _gx[j] -= x0;
            }

            int tmin = int.MaxValue, tmax = 0;
            if (hyphen)
            {
                tmin = p.Ca;
                tmax = p.Ca + 1;
            }
            else
            {
                for (int v = lo; v < hi; v++)
                {
                    GlyphCluster cl = cls[v];
                    int a = _src[el.Start + cl.TextStart];
                    int b = _src[el.Start + cl.TextStart + Math.Max(1, cl.TextLength) - 1] + 1;
                    tmin = Math.Min(tmin, a);
                    tmax = Math.Max(tmax, b);
                }
            }

            if (hyphen)
            {
                for (int j = gs; j < ge; j++)
                {
                    _gc[j] = tmin;
                }
            }

            GlyphRunId run = _out.AddGlyphRun(
                sg.Face.Face,
                st.Size,
                tmin,
                tmax - tmin,
                rtl,
                _gid.AsSpan(gs, ge - gs),
                _gx.AsSpan(gs, ge - gs),
                _gy.AsSpan(gs, ge - gs),
                _gc.AsSpan(gs, ge - gs));
            double ty = ob.Base - ob.A - oy;
            _out.AddText(parent, tb, tmin, tmax - tmin, new Rect(p.X + x0 - ox, ty, x1 - x0, ob.A + ob.D), run);
        }
    }
}
