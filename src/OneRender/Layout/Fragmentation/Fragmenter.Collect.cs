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

        /// <summary>A flex container: its children may sit side by side, so a break needs every earlier child to end above it.</summary>
        public bool Overlapping;

        /// <summary>Inside a table row: breaks here split the cells, so they rank with the avoid-rule breaks (level 2) but fillers stay usable.</summary>
        public bool RowUp;

        /// <summary>The candidate added for the start of the current flex line, or -1.</summary>
        public int LineCand;

        /// <summary>When the children are walked in block order instead of tree order (reversed flex containers): the next and the end position in the order pool.</summary>
        public int OrderPos;
        public int OrderEnd;
    }

    private struct Filler
    {
        public double Top;
        public double Bottom;
        public double CloneTop;
        public double CloneBottom;

        /// <summary>The box has size containment: it is only cut when it is taller than a fragmentainer.</summary>
        public bool Mono;
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
    private int[] _fo = new int[16];
    private int _foN;
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

    // A table whose header or footer group a page break repeats (CSS 2 §17.5.2.2, CSS Tables 3 §9.1): the group is copied to the top
    // (header) or bottom (footer) of every page the table continues on, so its height is reserved there like a cloned border.
    private struct RepeatRecord
    {
        public int Box;
        public int Header;
        public double HeaderReserve;
        public int Footer;
        public double FooterReserve;
    }

    private readonly List<RepeatRecord> _repeats = [];

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
        _repeats.Clear();
        _spannersOn = spanners;
        _steps = 0;
        ResetRow(0);
        _wsp = 0;
        _foN = 0;
        PushWalk(root, -contentTop, true, default, tree.FirstChildOf(root));
        while (_wsp > 0)
        {
            int top = _wsp - 1;
            int c = _walk[top].Next;
            if (c < 0)
            {
                EndGap(top);
                _wsp--;
                continue;
            }

            if (_walk[top].OrderEnd > 0)
            {
                _walk[top].Next = ++_walk[top].OrderPos < _walk[top].OrderEnd ? _fo[_walk[top].OrderPos] : -1;
            }
            else
            {
                _walk[top].Next = tree.NextSiblingOf(c);
            }

            if ((++_steps & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            Visit(top, c);
        }
    }

    // §4.1 class C: the room between the last child and the end of the box's content (a fixed height, bottom padding) has no content
    // in it, so it can be cut anywhere, like an empty box.
    private void EndGap(int wi)
    {
        ref Walk w = ref _walk[wi];
        if (w.IsRoot || w.AvoidUp)
        {
            return;
        }

        Box box = _boxes.Make(_t.BoxOf(w.Frag));
        if (AvoidInside(box.Values.BreakInside))
        {
            return;
        }

        double last = double.NegativeInfinity;
        if (w.PrevFrag >= 0)
        {
            last = w.PrevBottom;
        }

        if (w.LineNo > 0)
        {
            last = Math.Max(last, w.PrevLineBottom);
        }

        Rect bb = _t.BorderBoxOf(w.Frag);
        Insets border = _t.BorderOf(w.Frag), pad = _t.PaddingOf(w.Frag);
        double end = w.Top + bb.Height - border.Bottom - pad.Bottom;
        if (double.IsNegativeInfinity(last) || end - last <= Eps)
        {
            return;
        }

        if (_fn == _fillers.Length)
        {
            Array.Resize(ref _fillers, _fillers.Length * 2);
        }

        _fillers[_fn++] = new Filler { Top = last, Bottom = end, CloneTop = w.CloneTop, CloneBottom = w.CloneBottom };
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
            Overlapping = box.Context is FormattingContext.Flex or FormattingContext.Grid or FormattingContext.TableRow,
            LineCand = -1,
        };
        if (w.Overlapping)
        {
            OrderChildren(ref w, firstChild);
        }

        if (!isRoot)
        {
            bool clone = v.BoxDecorationBreak == BoxDecorationBreakKind.Clone;
            w.AvoidUp = parent.AvoidUp || AvoidInside(v.BreakInside);
            w.RowUp = parent.RowUp || box.Context == FormattingContext.TableRow;
            w.CloneTop = parent.CloneTop + (clone ? border.Top + pad.Top : 0);
            w.CloneBottom = parent.CloneBottom + (clone ? border.Bottom + pad.Bottom : 0);
        }

        if (!isRoot && _kind == FragmentationKind.Page && box.Context == FormattingContext.Table && !v.IsScrollContainer && !v.IsMulticol)
        {
            NoteRepeats(frag, box, ref w);
        }

        _walk[_wsp++] = w;
    }

    // Items of a reversed flex container (column-reverse, wrap-reverse) come after one another in the tree but not in block order, and
    // a break has to be offered between them in block order. The children are put in block order (stable) in a pool when they are not.
    private void OrderChildren(ref Walk w, int firstChild)
    {
        int n = 0;
        bool sorted = true;
        double prev = double.NegativeInfinity;
        for (int c = firstChild; c >= 0; c = _t.NextSiblingOf(c))
        {
            double y = _t.BorderBoxOf(c).Y;
            if (y < prev - Eps)
            {
                sorted = false;
            }

            prev = Math.Max(prev, y);
            n++;
        }

        if (sorted || n < 2)
        {
            return;
        }

        if (_foN + n > _fo.Length)
        {
            Array.Resize(ref _fo, Math.Max(_fo.Length * 2, _foN + n));
        }

        int start = _foN, i = start;
        for (int c = firstChild; c >= 0; c = _t.NextSiblingOf(c))
        {
            _fo[i++] = c;
        }

        _foN += n;
        // Insertion order is the tie-break: sort a copy of (y, position) pairs.
        var keys = new double[n];
        var pos = new int[n];
        for (int k = 0; k < n; k++)
        {
            keys[k] = _t.BorderBoxOf(_fo[start + k]).Y;
            pos[k] = k;
        }

        Array.Sort(pos, (a, b) =>
        {
            int cmp = keys[a].CompareTo(keys[b]);
            return cmp != 0 ? cmp : a.CompareTo(b);
        });
        var tmp = new int[n];
        for (int k = 0; k < n; k++)
        {
            tmp[k] = _fo[start + pos[k]];
        }

        Array.Copy(tmp, 0, _fo, start, n);
        w.Next = tmp[0];
        w.OrderPos = start;
        w.OrderEnd = start + n;
    }

    private void NoteRepeats(int table, Box box, ref Walk w)
    {
        int header = -1, footer = -1;
        double headerReserve = 0, footerReserve = 0;
        double prevBottom = double.NaN;
        for (int g = _t.FirstChildOf(table); g >= 0; g = _t.NextSiblingOf(g))
        {
            if ((FragmentKind)_t.KindOf(g) != FragmentKind.Box)
            {
                continue;
            }

            Box gb = _boxes.Make(_t.BoxOf(g));
            Rect r = _t.BorderBoxOf(g);
            if (gb.Context == FormattingContext.TableHeaderGroup && header < 0)
            {
                header = g;
                headerReserve = r.Height;
                for (int n = _t.NextSiblingOf(g); n >= 0; n = _t.NextSiblingOf(n))
                {
                    if ((FragmentKind)_t.KindOf(n) == FragmentKind.Box && _boxes.Make(_t.BoxOf(n)).Context is FormattingContext.TableRowGroup or FormattingContext.TableFooterGroup)
                    {
                        headerReserve = Math.Max(headerReserve, _t.BorderBoxOf(n).Y - r.Y);
                        break;
                    }
                }
            }
            else if (gb.Context == FormattingContext.TableFooterGroup && footer < 0)
            {
                footer = g;
                footerReserve = r.Height + (double.IsNaN(prevBottom) ? 0 : Math.Max(0, r.Y - prevBottom));
            }

            if (gb.Context is FormattingContext.TableRowGroup or FormattingContext.TableHeaderGroup)
            {
                prevBottom = r.Bottom;
            }
        }

        if (header < 0 && footer < 0)
        {
            return;
        }

        w.CloneTop += headerReserve;
        w.CloneBottom += footerReserve;
        _repeats.Add(new RepeatRecord { Box = box.Index, Header = header, HeaderReserve = headerReserve, Footer = footer, FooterReserve = footerReserve });
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

                if (lw.AvoidUp || lw.RowUp)
                {
                    level = 2;
                }

                Add(Math.Min(lw.PrevLineBottom, cTop), cTop, in lw, level, false, true, BreakBits.None);
            }

            if (lw.IsRoot)
            {
                // Lines directly in the fragmented container (inline content with no block children) are its content.
                NoteRowChild(cTop, cBot);
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
                AddMonoFiller(wi, cTop, cBot, in v);
                if (_flN == _floats.Length)
                {
                    Array.Resize(ref _floats, _floats.Length * 2);
                }

                _floats[_flN++] = new Span2 { Top = cTop, Bottom = cBot };
            }

            return;
        }

        if (box.Context is FormattingContext.TableColumn or FormattingContext.TableColumnGroup)
        {
            // Column boxes span the whole table height and are not part of the row flow.
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
            if (!_walk[wi].Overlapping || cTop >= _walk[wi].PrevBottom - Eps)
            {
                ClassA(wi, c, cTop, in v);
                _walk[wi].LineCand = _cn - 1;
            }
            else if (_walk[wi].LineCand >= 0)
            {
                // Flexbox §12.1: a forced break before any item of a flex line is a forced break before the line.
                BreakBits own = ChainBits(c, true, out _);
                ref BreakCandidate lc = ref _c[_walk[wi].LineCand];
                if ((own & ForcedMask) != 0 && lc.Ye <= cTop + Eps)
                {
                    lc.Bits |= own;
                    lc.Forced = true;
                    lc.Level = 0;
                }
            }
        }

        _walk[wi].PrevBottom = _walk[wi].Overlapping && _walk[wi].PrevFrag >= 0 ? Math.Max(_walk[wi].PrevBottom, cBot) : cBot;
        _walk[wi].PrevFrag = c;
        if (IsMono(c, box))
        {
            if (box.Kind != BoxKind.Marker)
            {
                AddMonoFiller(wi, cTop, cBot, in v);
            }

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
                    Mono = v.HasSizeContainment,
                };
            }
        }
    }

    // Monolithic content (and a float) taller than a page overflows it, and on pages the overflow carries on in the pages after it:
    // the box is a filler that only the page planner may cut, and only when it does not fit a page whole (CSS Break 4 §4.1).
    private void AddMonoFiller(int wi, double top, double bottom, in ComputedValues v)
    {
        ref Walk w = ref _walk[wi];
        if (_kind != FragmentationKind.Page || bottom - top <= Eps || w.AvoidUp || AvoidInside(v.BreakInside))
        {
            return;
        }

        if (_fn == _fillers.Length)
        {
            Array.Resize(ref _fillers, _fillers.Length * 2);
        }

        _fillers[_fn++] = new Filler { Top = top, Bottom = bottom, CloneTop = w.CloneTop, CloneBottom = w.CloneBottom, Mono = true };
    }

    private bool InFlowBox(int g)
    {
        if ((FragmentKind)_t.KindOf(g) != FragmentKind.Box)
        {
            return false;
        }

        Box b = _boxes.Make(_t.BoxOf(g));
        return (b.Flags & BoxTraits.OutOfFlow) == 0 && b.Kind != BoxKind.Marker && b.Context is not (FormattingContext.TableColumn or FormattingContext.TableColumnGroup);
    }

    // §3.1.1: a break-before on the first in-flow child counts for its parent, a break-after on the last for its parent, all the way up.
    private BreakBits ChainBits(int frag, bool first, out string? pageName)
    {
        BreakBits bits = BreakBits.None;
        pageName = null;
        bool nameFrozen = false;
        int x = frag;
        for (int n = 0; x >= 0 && n < 1_000_000; n++)
        {
            Box box = _boxes.Make(_t.BoxOf(x));
            bits |= Map(first ? box.Values.BreakBefore : box.Values.BreakAfter);
            if (!nameFrozen)
            {
                pageName = box.Values.PageName;
            }

            // Paged Media 3 §7.3: the page property applies to in-flow block-level boxes, not to flex items, so a name inside a flex
            // container does not leak out to the container's neighbours.
            nameFrozen |= box.Context is FormattingContext.Flex or FormattingContext.Grid;
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
        BreakBits bits = ChainBits(w.PrevFrag, false, out string? prevName) | ChainBits(c, true, out string? nextName);
        if (_kind == FragmentationKind.Page && !w.Overlapping && !string.Equals(prevName, nextName, StringComparison.Ordinal))
        {
            // Paged Media 3 §7.2: a change of used page name between consecutive boxes forces a page break.
            bits |= BreakBits.ForcePage;
        }

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
        else if ((bits & AvoidMask) != 0 || w.AvoidUp || w.RowUp || (_kind == FragmentationKind.Page && _boxes.Make(_t.BoxOf(w.PrevFrag)).Context == FormattingContext.TableHeaderGroup))
        {
            level = 2; // a break right after a repeated header would leave a page of header and nothing else
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
