using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    private struct AbsEntry
    {
        public int Box;
        public int Parent; // fragment the box is a child of (its parent box's fragment)
        public int Frag; // the placeholder fragment, created in tree order so paint order sees the box where it sits in the document
        public int Cb; // containing block fragment, -1 for the initial containing block
        public bool Fixed;
        public bool ParentRtl;
        public double StaticStart; // parent-relative border-box coordinates of the hypothetical box's margin edges
        public double StaticEnd;
        public double StaticTop;

        // Set for a child of a flex container (Flexbox §4.1): the container's content box and where in it the box sits when its insets are auto.
        public bool HasArea;
        public double AreaX;
        public double AreaY;
        public double AreaW;
        public double AreaH;
        public double FracX;
        public double FracY;
    }

    private const int MaxAbsolute = 1 << 22;

    private AbsEntry[] _abs = new AbsEntry[16];
    private int _absCount;
    private int _absPre = -1;
    private bool _absForce;
    private double _absW;
    private double _absMl;
    private double _absMr;
    private double _absH;
    private int _vpSource = -1;
    private double _icbW;
    private double _icbH;
    private double[] _ox = new double[64];
    private double[] _oy = new double[64];
    private int[] _cbOf = new int[64];
    private byte[] _dirty = new byte[64];
    private int[] _stamp = new int[64];
    private int _stampNo;
    private int[] _walk = new int[64];
    private int[] _post = new int[64];

    private void BeginPositioning(BoxTree boxes, in LayoutInput input)
    {
        _absCount = 0;
        _absPre = -1;
        _absForce = false;
        ViewportOverflow.Resolve(boxes, out _, out _, out _vpSource);
        _icbW = input.AvailableWidth;
        _icbH = input.AvailableHeight;
    }

    private Fragment NewFragment(int preCreated, in Frame parent, Box c, Rect borderBox)
    {
        if (preCreated >= 0)
        {
            var f = new Fragment(_out, preCreated);
            _out.SetBorderBox(f, borderBox);
            return f;
        }

        return _out.AddBox(FragmentAt(in parent), c, borderBox);
    }

    private static double InsetPx(Length l, double basis) =>
        l.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc ? l.Resolve(basis) : double.NaN;

    /// <summary>
    /// CSS2 §9.4.3: the shift of a relatively positioned box. With both insets given, <c>left</c> wins over <c>right</c> in
    /// left-to-right containers and <c>top</c> over <c>bottom</c> always; one given inset moves the box by it, and the other
    /// one moves it the opposite way. Percentages of <c>top</c>/<c>bottom</c> against a height that depends on content are <c>auto</c>.
    /// </summary>
    internal static void RelativeOffset(in ComputedValues v, double cbWidth, double cbHeight, bool rtl, out double dx, out double dy)
    {
        dx = dy = 0;
        if (v.Position != PositionKind.Relative)
        {
            return;
        }

        double l = InsetPx(v.Left, cbWidth), r = InsetPx(v.Right, cbWidth);
        if (double.IsNaN(l))
        {
            dx = double.IsNaN(r) ? 0 : -r;
        }
        else if (double.IsNaN(r))
        {
            dx = l;
        }
        else
        {
            dx = rtl ? -r : l;
        }

        double t = InsetPx(v.Top, cbHeight), b = InsetPx(v.Bottom, cbHeight);
        if (double.IsNaN(t))
        {
            dy = double.IsNaN(b) ? 0 : -b;
        }
        else
        {
            dy = t;
        }
    }

    // ---- queueing ----

    /// <summary>
    /// An absolutely positioned child met while placing the in-flow children of frame <paramref name="pi"/>: its static position
    /// is where it would have been in the flow (CSS2 §10.6.4: the top margin edge of the hypothetical box, after the margins
    /// that precede it). Its containing block is not known until that block's size is, so the box waits in a queue.
    /// </summary>
    private void QueueAbsolute(int pi, Box c)
    {
        ref Frame f = ref _frames[pi];
        if (f.Box < 0 || _absCount >= MaxAbsolute)
        {
            return;
        }

        bool pending = (f.Flags & FrameFlags.TopPending) != 0;
        double top = f.ContentTop + (pending ? 0 : f.Cursor + _pos + _neg);
        Enqueue(in f, c, f.ContentLeft, f.ContentLeft + f.ContentWidth, top);
    }

    /// <summary>
    /// Absolutely positioned boxes inside the inline content of a block container. Until the inline layout reports where it met
    /// them, their static position is the start of the container's content box. This is the seam for it: an inline layout that
    /// knows the position of the hypothetical box can call the same queue with that position instead.
    /// </summary>
    private void QueueInlineAbsolutes(int ni)
    {
        Box root = _boxes.Make(_frames[ni].Box);
        Box b = root.FirstChild;
        while (!b.IsNull)
        {
            if ((b.Flags & BoxTraits.OutOfFlow) != 0 && b.Values.Position is PositionKind.Absolute or PositionKind.Fixed)
            {
                ref Frame f = ref _frames[ni];
                if (_absCount < MaxAbsolute)
                {
                    Enqueue(in f, b, f.ContentLeft, f.ContentLeft + f.ContentWidth, f.ContentTop);
                }
            }
            else if (b.Kind == BoxKind.Inline && !b.FirstChild.IsNull)
            {
                b = b.FirstChild;
                continue;
            }

            while (true)
            {
                Box n = b.NextSibling;
                if (!n.IsNull)
                {
                    b = n;
                    break;
                }

                b = b.Parent;
                if (b.IsNull || b.Index == root.Index)
                {
                    b = default;
                    break;
                }
            }
        }
    }

    private void EnqueueRoot(Box root)
    {
        Fragment placeholder = _out.AddBox(default, root, default);
        bool fixedBox = root.Values.Position == PositionKind.Fixed;
        _abs[_absCount++] = new AbsEntry
        {
            Box = root.Index,
            Parent = -1,
            Frag = placeholder.Index,
            Cb = -1,
            Fixed = fixedBox,
            StaticEnd = _icbW,
        };
    }

    private void Enqueue(in Frame f, Box c, double staticStart, double staticEnd, double staticTop)
    {
        Fragment placeholder = _out.AddBox(new Fragment(_out, f.Frag), c, default);
        if (_absCount == _abs.Length)
        {
            Array.Resize(ref _abs, _abs.Length * 2);
        }

        bool fixedBox = c.Values.Position == PositionKind.Fixed;
        _abs[_absCount++] = new AbsEntry
        {
            Box = c.Index,
            Parent = f.Frag,
            Frag = placeholder.Index,
            Cb = fixedBox ? -1 : f.CbFrag,
            Fixed = fixedBox,
            ParentRtl = (f.Flags & FrameFlags.Rtl) != 0,
            StaticStart = staticStart,
            StaticEnd = staticEnd,
            StaticTop = staticTop,
        };
    }

    // ---- resolving ----

    private void EnsureOrigins(int count)
    {
        if (_ox.Length >= count)
        {
            return;
        }

        int size = Math.Max(count, _ox.Length * 2);
        Array.Resize(ref _ox, size);
        Array.Resize(ref _oy, size);
        Array.Resize(ref _cbOf, size);
        Array.Resize(ref _dirty, size);
        Array.Resize(ref _stamp, size);
    }

    /// <summary>
    /// Document-space origins of the fragments [from, to). A parent usually has a lower index than its children, but not always:
    /// inline layout moves an atomic inline's fragment under the line it lands on (<see cref="FragmentTree.Reparent"/>), and that
    /// line is created after it. So a fragment whose parent is not done yet waits for it: the chain up to the first finished
    /// ancestor is walked and filled from the top down, each fragment once.
    /// </summary>
    private void FillOrigins(int from, int to)
    {
        _stampNo++;
        for (int i = from; i < to; i++)
        {
            _cbOf[i] = -1;
            _dirty[i] = 0;
        }

        for (int i = from; i < to; i++)
        {
            if (_stamp[i] == _stampNo)
            {
                continue;
            }

            int n = 0;
            for (int j = i; j >= from && j < to && _stamp[j] != _stampNo; j = _out.ParentOf(j))
            {
                if (n == _walk.Length)
                {
                    Array.Resize(ref _walk, n * 2);
                }

                _walk[n++] = j;
            }

            for (int k = n - 1; k >= 0; k--)
            {
                int f = _walk[k];
                Rect r = _out.BorderBoxOf(f);
                int p = _out.ParentOf(f);
                _ox[f] = (p < 0 ? 0 : _ox[p]) + r.X;
                _oy[f] = (p < 0 ? 0 : _oy[p]) + r.Y;
                _stamp[f] = _stampNo;
            }
        }
    }

    /// <summary>
    /// Lays out the queued absolutely positioned boxes once everything they can be positioned against is final (CSS2 §10.1,
    /// CSS Position 3 §2). Boxes found while laying out a queued box join the end of the queue, so a box is always resolved
    /// after the box that contains it. Positions are computed from document-space origins kept in a column, so each box costs
    /// O(1) however deep its containing block is.
    /// </summary>
    private void ResolveAbsolutes()
    {
        if (_absCount == 0 || _out.Count == 0)
        {
            return;
        }

        int n = _out.Count;
        EnsureOrigins(n);
        FillOrigins(0, n);
        if (double.IsNaN(_icbH))
        {
            _icbH = _out.BorderBoxOf(0).Height;
        }

        for (int e = 0; e < _absCount; e++)
        {
            if ((e & 63) == 0)
            {
                _token.ThrowIfCancellationRequested();
            }

            ResolveOne(e);
        }

        PropagateAbsoluteOverflow();
    }

    private void ResolveOne(int e)
    {
        AbsEntry a = _abs[e];
        Box c = _boxes.Make(a.Box);
        ref readonly ComputedValues v = ref c.Values;
        double px = a.Parent < 0 ? 0 : _ox[a.Parent], py = a.Parent < 0 ? 0 : _oy[a.Parent];

        // The containing block is the padding box of the nearest positioned ancestor, or the initial containing block (CSS2 §10.1).
        double cbx = 0, cby = 0, cbw = _icbW, cbh = _icbH;
        bool cbRtl = _boxes.Root.Values.Direction == DirectionKind.Rtl;
        if (a.Cb >= 0)
        {
            Rect cbb = _out.BorderBoxOf(a.Cb);
            Insets cbBorder = _out.BorderOf(a.Cb);
            cbx = _ox[a.Cb] + cbBorder.Left;
            cby = _oy[a.Cb] + cbBorder.Top;
            cbw = Math.Max(0, cbb.Width - cbBorder.Horizontal);
            cbh = Math.Max(0, cbb.Height - cbBorder.Vertical);
            cbRtl = _boxes.Make(_out.BoxOf(a.Cb)).Values.Direction == DirectionKind.Rtl;
        }

        double pl = Pad(v.PaddingLeft, cbw), pr = Pad(v.PaddingRight, cbw), pt = Pad(v.PaddingTop, cbw), pb = Pad(v.PaddingBottom, cbw);
        double ph = pl + pr + v.BorderLeftWidth + v.BorderRightWidth;
        double pv = pt + pb + v.BorderTopWidth + v.BorderBottomWidth;
        double mlv = v.MarginLeft.IsAuto ? double.NaN : MarginPx(v.MarginLeft, cbw);
        double mrv = v.MarginRight.IsAuto ? double.NaN : MarginPx(v.MarginRight, cbw);
        double mtv = v.MarginTop.IsAuto ? double.NaN : MarginPx(v.MarginTop, cbw);
        double mbv = v.MarginBottom.IsAuto ? double.NaN : MarginPx(v.MarginBottom, cbw);
        double left = InsetPx(v.Left, cbw), right = InsetPx(v.Right, cbw);
        double top = InsetPx(v.Top, cbh), bottom = InsetPx(v.Bottom, cbh);
        bool replaced = c.Kind == BoxKind.Replaced;

        double availContent = Math.Max(0, cbw - ph - (double.IsNaN(mlv) ? 0 : mlv) - (double.IsNaN(mrv) ? 0 : mrv));
        double sizeW, minW = 0, maxW = double.PositiveInfinity, fitMin = 0, fitMax = 0, replacedH = double.NaN;
        if (replaced)
        {
            // CSS2 §10.3.8 / §10.6.5: a replaced box has its used width and height before the insets are looked at.
            ReplacedContentSize(c, in v, cbw, cbh, ph, pv, availContent, out sizeW, out replacedH);
        }
        else
        {
            if (v.Width.Kind == LengthKind.Stretch)
            {
                sizeW = Math.Max(0, cbw - (double.IsNaN(left) ? 0 : left) - (double.IsNaN(right) ? 0 : right) - (double.IsNaN(mlv) ? 0 : mlv) - (double.IsNaN(mrv) ? 0 : mrv) - ph);
            }
            else
            {
                sizeW = ResolveWidthLength(c, v.Width, cbw, ph, v.BoxSizing, availContent, isMin: false);
            }

            minW = ResolveWidthLength(c, v.MinWidth, cbw, ph, v.BoxSizing, availContent, isMin: true);
            maxW = ResolveWidthLength(c, v.MaxWidth, cbw, ph, v.BoxSizing, availContent, isMin: false);
            if (double.IsNaN(minW))
            {
                minW = 0;
            }

            if (double.IsNaN(maxW))
            {
                maxW = double.PositiveInfinity;
            }

            if (double.IsNaN(sizeW))
            {
                GetIntrinsic(c, out fitMin, out fitMax);
            }
        }

        SolveAbsoluteAxis(
            cbw, left, right, sizeW, mlv, mrv, ph,
            px + a.StaticStart - cbx, (cbx + cbw) - (px + a.StaticEnd),
            staticFromStart: !a.ParentRtl, startWins: !cbRtl, negativeCenter: false,
            fitMin, fitMax, minW, maxW,
            out double usedLeft, out double usedW, out double ml, out double mr);

        // Vertical: the height can come from the box, from an aspect ratio, from the insets, or from its content.
        double sizeH = replacedH, minH = 0, maxH = double.PositiveInfinity;
        if (!replaced)
        {
            ResolveHeights(in v, cbh, pv, double.IsNaN(mtv) ? 0 : mtv, double.IsNaN(mbv) ? 0 : mbv, usedW + ph, usedW, out double specH, out minH, out maxH, out double ratioH);
            sizeH = !double.IsNaN(specH) ? specH : (!double.IsNaN(ratioH) && (double.IsNaN(top) || double.IsNaN(bottom)) ? ratioH : double.NaN);
        }

        double staticTop = py + a.StaticTop - cby;
        double usedTop = 0, usedH = double.NaN, mt = 0;
        bool heightKnown = !double.IsNaN(sizeH) || (!double.IsNaN(top) && !double.IsNaN(bottom));
        if (heightKnown)
        {
            SolveAbsoluteAxis(
                cbh, top, bottom, sizeH, mtv, mbv, pv,
                staticTop, 0,
                staticFromStart: true, startWins: true, negativeCenter: true,
                0, 0, minH, maxH,
                out usedTop, out usedH, out mt, out _);
        }

        int before = _out.Count;
        Fragment parent = a.Parent < 0 ? default : new Fragment(_out, a.Parent);
        _absPre = a.Frag;
        if (!replaced)
        {
            _absForce = true;
            _absW = usedW + ph;
            _absMl = ml;
            _absMr = mr;
            _absH = usedH;
        }

        RunIndependent(c, parent, cbw, cbh, out _);
        _absPre = -1;
        _absForce = false;

        var frag = new Fragment(_out, a.Frag);
        Rect bb = _out.BorderBoxOf(a.Frag);
        if (!heightKnown)
        {
            double contentH = Math.Max(0, bb.Height - pv);
            SolveAbsoluteAxis(
                cbh, top, bottom, double.NaN, mtv, mbv, pv,
                staticTop, 0,
                staticFromStart: true, startWins: true, negativeCenter: true,
                contentH, contentH, 0, double.PositiveInfinity,
                out usedTop, out _, out mt, out _);
        }

        double x = cbx + usedLeft + ml - px;
        double y = cby + usedTop + mt - py;
        if (a.HasArea)
        {
            // Flexbox §4.1: with auto insets the box is aligned in the container's content box as the only flex item.
            if (double.IsNaN(left) && double.IsNaN(right))
            {
                x = a.AreaX + ((a.AreaW - bb.Width - ml - mr) * a.FracX) + ml;
            }

            if (double.IsNaN(top) && double.IsNaN(bottom))
            {
                y = a.AreaY + ((a.AreaH - bb.Height - mt - (double.IsNaN(mbv) ? 0 : mbv)) * a.FracY) + mt;
            }
        }

        _out.SetBorderBox(frag, new Rect(x, y, bb.Width, bb.Height));
        EnsureOrigins(_out.Count);
        _ox[a.Frag] = px + x;
        _oy[a.Frag] = py + y;
        FillOrigins(Math.Max(before, 0), _out.Count);
        _cbOf[a.Frag] = a.Fixed ? -2 : (a.Cb >= 0 ? a.Cb : 0);
    }

    /// <summary>
    /// CSS2 §10.3.7 and §10.6.4 (and §10.3.8 / §10.6.5 for replaced boxes, which arrive with a used size): solves the equation
    /// <c>start + margin-start + size + margin-end + end + extra = containing size</c> on one axis, given which of the insets,
    /// the size and the margins are <c>auto</c> (NaN).
    /// </summary>
    /// <param name="cb">Containing block size on this axis.</param>
    /// <param name="start">Inset at the start edge (left or top), NaN for auto.</param>
    /// <param name="end">Inset at the end edge (right or bottom), NaN for auto.</param>
    /// <param name="size">Content size, NaN for auto.</param>
    /// <param name="marginStart">Start margin, NaN for auto.</param>
    /// <param name="marginEnd">End margin, NaN for auto.</param>
    /// <param name="extra">Padding plus border on this axis.</param>
    /// <param name="staticStart">Distance from the containing block's start edge to the hypothetical box's start margin edge.</param>
    /// <param name="staticEnd">Distance from the containing block's end edge to the hypothetical box's end margin edge.</param>
    /// <param name="staticFromStart">When both insets are auto: use the static start (left-to-right) or the static end.</param>
    /// <param name="startWins">When over-constrained: ignore the end inset (true) or the start inset.</param>
    /// <param name="negativeCenter">Whether two auto margins share a negative remainder equally (the vertical rule) or the end margin takes it (the horizontal rule).</param>
    /// <param name="fitMin">Preferred minimum size, for an auto size (shrink-to-fit).</param>
    /// <param name="fitMax">Preferred size, for an auto size.</param>
    /// <param name="minSize">Minimum content size (§10.4, §10.7).</param>
    /// <param name="maxSize">Maximum content size.</param>
    /// <param name="usedStart">The start inset used.</param>
    /// <param name="usedSize">The content size used.</param>
    /// <param name="usedMarginStart">The start margin used.</param>
    /// <param name="usedMarginEnd">The end margin used.</param>
    internal static void SolveAbsoluteAxis(
        double cb, double start, double end, double size, double marginStart, double marginEnd, double extra,
        double staticStart, double staticEnd, bool staticFromStart, bool startWins, bool negativeCenter,
        double fitMin, double fitMax, double minSize, double maxSize,
        out double usedStart, out double usedSize, out double usedMarginStart, out double usedMarginEnd)
    {
        SolveCore(cb, start, end, size, marginStart, marginEnd, extra, staticStart, staticEnd, staticFromStart, startWins, negativeCenter, fitMin, fitMax, out usedStart, out usedSize, out usedMarginStart, out usedMarginEnd);
        // CSS2 §10.4: a violated maximum, then a violated minimum, repeats the solution with that size as if it were specified.
        if (usedSize > maxSize)
        {
            SolveCore(cb, start, end, maxSize, marginStart, marginEnd, extra, staticStart, staticEnd, staticFromStart, startWins, negativeCenter, fitMin, fitMax, out usedStart, out usedSize, out usedMarginStart, out usedMarginEnd);
        }

        if (usedSize < minSize)
        {
            SolveCore(cb, start, end, minSize, marginStart, marginEnd, extra, staticStart, staticEnd, staticFromStart, startWins, negativeCenter, fitMin, fitMax, out usedStart, out usedSize, out usedMarginStart, out usedMarginEnd);
        }
    }

    private static void SolveCore(
        double cb, double start, double end, double size, double ms, double me, double extra,
        double staticStart, double staticEnd, bool staticFromStart, bool startWins, bool negativeCenter,
        double fitMin, double fitMax,
        out double usedStart, out double usedSize, out double usedMs, out double usedMe)
    {
        bool sAuto = double.IsNaN(start), eAuto = double.IsNaN(end), zAuto = double.IsNaN(size);
        if (!sAuto && !eAuto && !zAuto)
        {
            double free = cb - start - end - size - extra;
            if (double.IsNaN(ms) && double.IsNaN(me))
            {
                if (free < 0 && !negativeCenter)
                {
                    // §10.3.7: the margin at the start of the line becomes 0 and the other one absorbs the negative remainder.
                    if (startWins)
                    {
                        ms = 0;
                        me = free;
                    }
                    else
                    {
                        me = 0;
                        ms = free;
                    }
                }
                else
                {
                    ms = me = free / 2;
                }
            }
            else if (double.IsNaN(ms))
            {
                ms = free - me;
            }
            else if (double.IsNaN(me))
            {
                me = free - ms;
            }
            else if (!startWins)
            {
                // Over-constrained: the inset at the end of the line is ignored, which for rtl is the start one.
                start = cb - end - size - extra - ms - me;
            }

            usedStart = start;
            usedSize = size;
            usedMs = ms;
            usedMe = me;
            return;
        }

        if (double.IsNaN(ms))
        {
            ms = 0;
        }

        if (double.IsNaN(me))
        {
            me = 0;
        }

        double m = ms + me + extra;
        if (sAuto && eAuto && zAuto)
        {
            if (staticFromStart)
            {
                start = staticStart;
                sAuto = false;
            }
            else
            {
                end = staticEnd;
                eAuto = false;
            }
        }

        if (zAuto)
        {
            if (sAuto)
            {
                size = Shrink(fitMin, fitMax, cb - end - m);
                start = cb - end - size - m;
            }
            else if (eAuto)
            {
                size = Shrink(fitMin, fitMax, cb - start - m);
            }
            else
            {
                size = Math.Max(0, cb - start - end - m);
            }
        }
        else if (sAuto)
        {
            start = eAuto ? (staticFromStart ? staticStart : cb - staticEnd - size - m) : cb - end - size - m;
        }

        usedStart = start;
        usedSize = size;
        usedMs = ms;
        usedMe = me;
    }

    // CSS2 §10.3.5: min(max(preferred minimum width, available width), preferred width).
    private static double Shrink(double min, double max, double available) => Math.Min(Math.Max(min, Math.Max(0, available)), max);

    // ---- overflow of positioned boxes ----

    /// <summary>
    /// Overflow 3 §2.2: an absolutely positioned box adds its scrollable overflow to the scroll containers and root that contain it
    /// through its containing block, not through its parent. Fixed boxes add nothing. One pass from the last fragment to the
    /// first carries every contribution up, so each fragment is touched once.
    /// </summary>
    private int PostOrder()
    {
        int n = _out.Count;
        if (_post.Length < n)
        {
            Array.Resize(ref _post, Math.Max(n, _post.Length * 2));
        }

        int k = 0;
        int cur = 0;
        if (n == 0)
        {
            return 0;
        }

        while (true)
        {
            for (int c = _out.FirstChildOf(cur); c >= 0; c = _out.FirstChildOf(cur))
            {
                cur = c;
            }

            while (true)
            {
                _post[k++] = cur;
                int next = _out.NextSiblingOf(cur);
                if (next >= 0)
                {
                    cur = next;
                    break;
                }

                cur = _out.ParentOf(cur);
                if (cur < 0)
                {
                    return k;
                }
            }
        }
    }

    private void PropagateAbsoluteOverflow()
    {
        // Descendants before ancestors. Fragment indices do not guarantee that (see FillOrigins), so walk the tree in post-order.
        int count = PostOrder();
        for (int q = 0; q < count; q++)
        {
            int i = _post[q];
            if (i == 0)
            {
                continue;
            }

            int target = _cbOf[i];
            bool abs = target != -1;
            if (target == -2 || (!abs && _dirty[i] == 0))
            {
                continue;
            }

            Rect ov = _out.OverflowOf(i);
            Rect bb = _out.BorderBoxOf(i);
            Box box = _boxes.Make(_out.BoxOf(i));
            ref readonly ComputedValues v = ref box.Values;
            if (box.Index != _vpSource)
            {
                if (v.OverflowX != OverflowKind.Visible)
                {
                    ov = new Rect(0, ov.Y, bb.Width, ov.Height);
                }

                if (v.OverflowY != OverflowKind.Visible)
                {
                    ov = new Rect(ov.X, 0, ov.Width, bb.Height);
                }
            }

            // A box's border box is always inside its own scrollable overflow (replaced boxes record none).
            double ul = Math.Min(ov.X, 0), ut = Math.Min(ov.Y, 0);
            ov = new Rect(ul, ut, Math.Max(ov.Right, bb.Width) - ul, Math.Max(ov.Bottom, bb.Height) - ut);

            double dx, dy;
            if (abs)
            {
                dx = _ox[i] - _ox[target];
                dy = _oy[i] - _oy[target];
            }
            else
            {
                target = _out.ParentOf(i);
                dx = bb.X;
                dy = bb.Y;
                while (target >= 0 && (FragmentKind)_out.KindOf(target) == FragmentKind.Line)
                {
                    Rect lr = _out.BorderBoxOf(target);
                    dx += lr.X;
                    dy += lr.Y;
                    target = _out.ParentOf(target);
                }

                if (target < 0)
                {
                    continue;
                }
            }

            Rect t = _out.OverflowOf(target);
            double l = Math.Min(t.X, ov.X + dx), tp = Math.Min(t.Y, ov.Y + dy);
            double r = Math.Max(t.Right, ov.Right + dx), b = Math.Max(t.Bottom, ov.Bottom + dy);
            _out.SetScrollableOverflow(new Fragment(_out, target), new Rect(l, tp, r - l, b - tp));
            _dirty[target] = 1;
        }
    }
}
