using OneFont;
using OneRender.Style;
using OneRender.Text;
using OneRender.Tree;

namespace OneRender.Layout;

internal sealed partial class InlineWorker
{
    private enum PK : byte
    {
        Text,
        Open,
        Close,
        Atomic,
        Tab,
        Hyphen,
        Br,
        Abs,
        Dead,
    }

    private struct Piece
    {
        public PK Kind;
        public byte Level;
        public byte VMode;     // atomic: 0 baseline-relative, 1 top, 2 bottom
        public int Elem;
        public int Ca;         // clusters [Ca, Cb) of the element, logical order
        public int Cb;
        public int Owner;      // box index of the innermost inline box (the container's own when none)
        public int Seps;
        public int Seg;
        public double W;
        public double X;
        public double Shift;   // atomic: baseline shift relative to its owner's baseline
        public double Base;    // atomic: its baseline y in line coordinates, set once the line height is known
    }

    private struct Seg
    {
        public int Box;
        public int Parent;
        public int Depth;
        public double MinX;
        public double MaxX;
        public double OriginX;
        public double OriginY;
        public bool HasOpen;
        public bool HasClose;
    }

    private struct BM
    {
        public int Stamp;
        public int Parent;
        public byte Mode;      // 0 aligned to the baseline chain, 1 top, 2 bottom
        public bool Excl;      // inside a top- or bottom-aligned subtree: does not stretch the line
        public double A;
        public double D;
        public double HL;
        public double HB;
        public double XH;
        public double Size;
        public double Shift;
        public double Abs;     // baseline offset from the root inline box's baseline, y down
        public double Base;    // baseline y in line coordinates
    }

    private Piece[] _pc = new Piece[32];
    private int _pn;
    private int[] _order = new int[32];
    private byte[] _plv = new byte[32];
    private Seg[] _sg = new Seg[16];
    private int _sgn;
    private BM[] _bm = [];
    private int _stamp;
    private int[] _touched = new int[16];
    private int _tn;
    private int[] _chain = new int[16];
    private int[] _path = new int[16];

    // Glyph scratch for building one text fragment.
    private int[] _gid = new int[64];
    private double[] _gx = new double[64];
    private double[] _gy = new double[64];
    private int[] _gc = new int[64];
    private double[] _clx = new double[64];

    // Font metrics cache, direct-mapped by style row.
    private readonly int[] _mcStyle = new int[256];
    private readonly int[] _mcVersion = new int[256];
    private readonly double[] _mcA = new double[256];
    private readonly double[] _mcD = new double[256];
    private readonly double[] _mcGap = new double[256];
    private readonly double[] _mcXh = new double[256];

    private double _firstBaseline;
    private double _lastBaseline;
    private double _maxRight;

    // Extent of everything the lines occupy, in the container fragment's border-box coordinates (scrollable overflow).
    private double _ovL, _ovT, _ovR, _ovB;
    private double _lineX, _lineY;

    private void AddOverflow(double x, double y, double w, double h)
    {
        // Line-local to container coordinates.
        _ovL = Math.Min(_ovL, _lineX + x);
        _ovT = Math.Min(_ovT, _lineY + y);
        _ovR = Math.Max(_ovR, _lineX + x + w);
        _ovB = Math.Max(_ovB, _lineY + y + h);
    }

    private static void Ensure<T>(ref T[] a, int n)
    {
        if (a.Length < n)
        {
            Array.Resize(ref a, Math.Max(n, a.Length * 2));
        }
    }

    // ------------------------------------------------------------------ entry points

    internal InlineLayoutResult Layout(in InlineLayoutRequest request, FragmentTree output, Fragment parent, IAtomicInlineHost? host, CancellationToken ct)
    {
        ResetState(request.Container, request.Fonts, host, true, ct);
        _req = request;
        _out = output;
        _parent = parent;
        _availW = Math.Max(0, request.AvailableWidth);
        Collect();
        if (_rawN == 0)
        {
            return new InlineLayoutResult(0, 0, double.NaN, double.NaN);
        }

        Prepare();
        return LayoutLines(in request);
    }

    internal void Measure(Box container, IFontProvider fonts, IAtomicInlineHost? host, out double minContent, out double maxContent)
    {
        ResetState(container, fonts, host, false, default);
        _availW = 0;
        minContent = 0;
        maxContent = 0;
        Collect();
        if (_rawN == 0)
        {
            return;
        }

        Prepare();
        double indentBase = Edge(container.Values.TextIndent, 0);
        for (int pass = 1; pass <= 2; pass++)
        {
            _measureWhich = pass;
            _measureMin = pass == 1;
            double avail = pass == 1 ? 0 : double.PositiveInfinity;
            double widest = 0;
            int e = 0, c = 0;
            bool first = true;
            while (e < _elN)
            {
                ScanLine(e, c, avail, IndentFor(in container.Values, first, first || _lastForced, indentBase));
                widest = Math.Max(widest, _lineW);
                _lastForced = _endForced;
                first = false;
                if (_endE == e && _endC == c)
                {
                    // No progress: take the element whole (cannot happen with a line that places something).
                    _endE = e + 1;
                    _endC = 0;
                }

                e = _endE;
                c = _endC;
            }

            if (pass == 1)
            {
                minContent = widest;
            }
            else
            {
                maxContent = widest;
            }
        }

        _measureWhich = 0;
        _measureMin = false;
        if (maxContent < minContent)
        {
            maxContent = minContent;
        }
    }

    private bool _lastForced;

    // CSS Text 3 §7.2: text-indent indents the first formatted line, and with each-line the lines after a forced break; hanging
    // inverts which lines are indented.
    private static double IndentFor(in ComputedValues v, bool firstLine, bool afterForced, double indent)
    {
        bool affected = firstLine || (v.TextIndentEachLine && afterForced);
        return (v.TextIndentHanging ? !affected : affected) ? indent : 0;
    }

    // ------------------------------------------------------------------ lines

    private InlineLayoutResult LayoutLines(in InlineLayoutRequest request)
    {
        ref readonly ComputedValues cv = ref _container.Values;
        double indentBase = Edge(cv.TextIndent, _availW);
        _measureWhich = 0;
        _measureMin = false;
        _firstBaseline = double.NaN;
        _lastBaseline = double.NaN;
        _maxRight = 0;
        _ovL = _ovT = double.PositiveInfinity;
        _ovR = _ovB = double.NegativeInfinity;
        IExclusionSpace ex = request.Exclusions;
        double y = 0;
        int lines = 0;
        int e = 0, c = 0;
        bool first = true;
        bool afterForced = true;
        Ensure(ref _bm, _boxes.Count);
        double strut = StrutHeight();
        while (e < _elN)
        {
            _ct.ThrowIfCancellationRequested();
            double indent = IndentFor(in cv, first, afterForced, indentBase);
            double top = y;
            double xOff = 0, avail = _availW;
            int ee = e, ec = c;
            double band = strut;
            bool hasContent = false;
            for (int guard = 0; guard < MaxLineAttempts; guard++)
            {
                // CSS2 §9.4.2: the line box is as wide as the room between the floats that meet its whole height.
                ex.Query(request.BfcTop + top, band, request.BfcLeft, request.BfcLeft + _availW, out double start, out double end, out double next);
                xOff = start - request.BfcLeft;
                avail = end - start;
                if (avail <= 0 && !double.IsPositiveInfinity(next))
                {
                    top = next - request.BfcTop;
                    continue;
                }

                ScanLine(e, c, avail, indent);
                ee = _endE;
                ec = _endC;
                // Out-of-flow boxes right after the line's last item stay with it: they take no room and have no break of their own.
                while (ec == 0 && !_endForced && ee < _elN && _el[ee].Kind == EK.Float && (_el[ee].Flags & EF.Abs) != 0)
                {
                    ee++;
                }

                if (PlaceLineFloats(in request, ex, e, c, ee, ec, avail, top))
                {
                    continue; // a float joined this line: it is narrower now
                }

                // A line too narrow for its first unbreakable piece moves down to where there is more room (§9.5).
                if (_lineW > avail + Eps && avail < _availW - Eps && !double.IsPositiveInfinity(next))
                {
                    top = next - request.BfcTop;
                    continue;
                }

                if (ee == e && ec == c)
                {
                    // A line always consumes something; this only guards against a bug turning into an endless loop.
                    ee = e + 1;
                    ec = 0;
                }

                hasContent = BuildPieces(e, c, ee, ec, _endHyphen, indent);
                if (hasContent)
                {
                    // The line is as tall as its content needs; floats that start lower than the strut but inside that height narrow it too.
                    VerticalMetrics(out _, out double h);
                    if (Math.Abs(h - band) > Eps)
                    {
                        band = h;
                        ex.Query(request.BfcTop + top, band, request.BfcLeft, request.BfcLeft + _availW, out double s2, out double e2, out _);
                        if (Math.Abs(s2 - start) > Eps || Math.Abs(e2 - end) > Eps)
                        {
                            continue;
                        }
                    }
                }

                break;
            }

            double lineHeight = 0;
            if (hasContent)
            {
                lineHeight = EmitLine(in request, top, xOff, avail, indent, _endForced || ee >= _elN, out double baseline);
                if (lines == 0)
                {
                    _firstBaseline = top + baseline;
                }

                _lastBaseline = top + baseline;
                lines++;
                first = false;
            }

            if (!hasContent && _host is not null)
            {
                // Out-of-flow boxes on a line that has nothing else: they sit at the start of the empty line.
                for (int k = e; k < ee && k < _elN; k++)
                {
                    if (_el[k].Kind == EK.Float && (_el[k].Flags & EF.Abs) != 0)
                    {
                        QueueAbs(in request, k, request.ContentLeft + xOff, request.ContentTop + top, -1);
                    }
                }
            }

            y = top + lineHeight;
            PlaceDeferredFloats(in request, ex, y);
            afterForced = _endForced;
            e = ee;
            c = ec;
        }

        return new InlineLayoutResult(y, lines, _firstBaseline, _lastBaseline, _ovL, _ovT, _ovR, _ovB, _host is not null);
    }

    private const int MaxLineAttempts = 1 << 16;
    private int[] _deferred = new int[8];
    private int _deferredN;

    private double StrutHeight()
    {
        ref readonly ComputedValues cv = ref _container.Values;
        FontBox(in cv, _container.Style, out double a, out double d, out double gap, out _);
        return Math.Max(0, LineHeightOf(in cv, a, d, gap));
    }

    /// <summary>
    /// CSS2 §9.5.1 rule 6: floats met on this line go at its top if they fit beside what is on the line, and below it otherwise.
    /// Floats that fit are placed now (the caller scans the line again, since they narrow it); the others wait for the line's height.
    /// </summary>
    /// <returns>True when a float was placed on this line.</returns>
    private bool PlaceLineFloats(in InlineLayoutRequest request, IExclusionSpace ex, int e0, int c0, int e1, int c1, double avail, double top)
    {
        _deferredN = 0;
        bool placed = false;
        for (int k = e0; k < _elN && (k < e1 || (k == e1 && c1 > 0)); k++)
        {
            ref Elem el = ref _el[k];
            if (el.Kind != EK.Float || el.Frag >= 0 || (el.Flags & EF.Abs) != 0)
            {
                continue;
            }

            double fw = 0;
            if (_host is not null)
            {
                _host.GetAtomicContribution(_boxes.Make(el.Box), out double min, out double max);
                fw = Math.Min(Math.Max(min, _availW), max); // shrink-to-fit, CSS2 §10.3.5
            }

            if (el.W2 <= Eps || el.W2 + fw <= avail + Eps)
            {
                Fragment f = ex.PlaceFloat(in request, _parent, _boxes.Make(el.Box), request.BfcTop + top, out _);
                el.Frag = f.IsNull ? int.MaxValue : f.Index;
                placed = true;
            }
            else
            {
                if (_deferredN == _deferred.Length)
                {
                    Array.Resize(ref _deferred, _deferredN * 2);
                }

                _deferred[_deferredN++] = k;
            }
        }

        return placed;
    }

    /// <summary>
    /// Hands an absolutely positioned box in the content to the block layout. Its static position (CSS2 §10.3.7, §10.6.4) is where the
    /// box would have been as an inline-level box: at the inline position reached on the line, at the top of the line box.
    /// </summary>
    private void QueueAbs(in InlineLayoutRequest request, int elem, double x, double lineTop, int containingFrag, bool inkBefore = false)
    {
        Box b = _boxes.Make(_el[elem].Box);
        double end = request.ContentLeft + _availW;
        bool rtl = _container.Values.Direction == DirectionKind.Rtl;
        if (!inkBefore)
        {
            // Nothing but out-of-flow boxes before it on the line: the box sits at the container's start edge, not after the floats.
            _host!.QueueAbsolute(b, containingFrag, request.ContentLeft, end, lineTop);
            return;
        }

        _host!.QueueAbsolute(b, containingFrag, rtl ? request.ContentLeft : x, rtl ? x : end, lineTop);
    }

    private void PlaceDeferredFloats(in InlineLayoutRequest request, IExclusionSpace ex, double lineBottom)
    {
        for (int i = 0; i < _deferredN; i++)
        {
            ref Elem el = ref _el[_deferred[i]];
            if (el.Frag >= 0)
            {
                continue;
            }

            Fragment f = ex.PlaceFloat(in request, _parent, _boxes.Make(el.Box), request.BfcTop + lineBottom, out _);
            el.Frag = f.IsNull ? int.MaxValue : f.Index;
        }

        _deferredN = 0;
    }

    private double TextWidth(int e, int ca, int cb, out int seps)
    {
        ref Elem el = ref _el[e];
        ReadOnlySpan<GlyphCluster> cls = _shaped[e]!.Clusters;
        int k = cls.Length;
        bool rtl = (el.Flags & EF.Rtl) != 0;
        double w = 0;
        seps = 0;
        for (int i = ca; i < cb; i++)
        {
            GlyphCluster cl = cls[rtl ? k - 1 - i : i];
            byte fl = _cf[el.Start + cl.TextStart];
            w += ClusterAdvance(in el, in cl, fl);
            if ((fl & CfWordSep) != 0)
            {
                seps++;
            }
        }

        return w;
    }

    private byte ClusterFlags(int e, int i)
    {
        ref Elem el = ref _el[e];
        ReadOnlySpan<GlyphCluster> cls = _shaped[e]!.Clusters;
        int k = cls.Length;
        return _cf[el.Start + cls[(el.Flags & EF.Rtl) != 0 ? k - 1 - i : i].TextStart];
    }

    private void AddPiece(PK kind, int e, int ca, int cb, double w, int owner, byte level)
    {
        if (_pn == _pc.Length)
        {
            Array.Resize(ref _pc, _pc.Length * 2);
        }

        ref Piece p = ref _pc[_pn++];
        p = default;
        p.Kind = kind;
        p.Elem = e;
        p.Ca = ca;
        p.Cb = cb;
        p.W = w;
        p.Owner = owner;
        p.Level = level;
    }

    /// <summary>
    /// Turns the elements of one line, [(<paramref name="e0"/>, <paramref name="c0"/>), (<paramref name="e1"/>, <paramref name="c1"/>)), into
    /// pieces in logical order: leading collapsible spaces are skipped and trailing ones removed (CSS Text 3 §4.1.2).
    /// </summary>
    /// <returns>True when the line has anything that gives it height.</returns>
    private bool BuildPieces(int e0, int c0, int e1, int c1, bool hyphen, double indent)
    {
        _pn = 0;
        bool ink = false;
        double x = indent;
        for (int e = e0; e < _elN && (e < e1 || (e == e1 && c1 > 0)); e++)
        {
            ref Elem el = ref _el[e];
            switch (el.Kind)
            {
                case EK.Open:
                    AddPiece(PK.Open, e, 0, 0, el.W, el.Owner, el.Level);
                    x += el.W;
                    break;
                case EK.Close:
                    AddPiece(PK.Close, e, 0, 0, el.W, el.Owner, el.Level);
                    x += el.W;
                    break;
                case EK.Atomic:
                    AddPiece(PK.Atomic, e, 0, 0, el.W, el.Owner, el.Level);
                    x += el.W;
                    ink = true;
                    break;
                case EK.Float:
                    if ((el.Flags & EF.Abs) != 0)
                    {
                        AddPiece(PK.Abs, e, 0, 0, 0, el.Owner, el.Level);
                    }

                    break;
                case EK.Tab:
                {
                    double tw = TabAdvance(in el, x);
                    AddPiece(PK.Tab, e, 0, 0, tw, el.Owner, el.Level);
                    x += tw;
                    ink = true;
                    break;
                }

                case EK.Newline:
                    if ((el.Flags & EF.Br) != 0)
                    {
                        AddPiece(PK.Br, e, 0, 0, 0, el.Owner, el.Level);
                    }

                    ink = true;
                    break;
                default:
                {
                    int k = _shaped[e]!.Clusters.Length;
                    int cs = e == e0 ? c0 : 0;
                    int ce = e == e1 ? c1 : k;
                    if (!ink)
                    {
                        while (cs < ce && (ClusterFlags(e, cs) & CfCollapsible) != 0)
                        {
                            cs++;
                        }
                    }

                    if (cs >= ce)
                    {
                        break;
                    }

                    double tw = TextWidth(e, cs, ce, out int seps);
                    AddPiece(PK.Text, e, cs, ce, tw, el.Owner, el.Level);
                    _pc[_pn - 1].Seps = seps;
                    ink = true;
                    x += tw;
                    break;
                }
            }
        }

        if (hyphen)
        {
            for (int j = _pn - 1; j >= 0; j--)
            {
                if (_pc[j].Kind == PK.Text)
                {
                    ref Elem pe = ref _el[_pc[j].Elem];
                    AddPiece(PK.Hyphen, _pc[j].Elem, 0, 0, HyphenWidth(in pe), pe.Owner, pe.Level);
                    // Ca of a hyphen piece is the source offset of the soft hyphen, right after the last character before the break.
                    ReadOnlySpan<GlyphCluster> hc = _shaped[_pc[j].Elem]!.Clusters;
                    GlyphCluster last = hc[(pe.Flags & EF.Rtl) != 0 ? hc.Length - _pc[j].Cb : _pc[j].Cb - 1];
                    int lastPos = pe.Start + last.TextStart + Math.Max(1, last.TextLength) - 1;
                    _pc[_pn - 1].Ca = _src[lastPos] + 1;
                    break;
                }
            }
        }

        // Trailing collapsible spaces are removed, also those before the end edges of inline boxes (§4.1.2).
        int t = _pn - 1;
        while (t >= 0)
        {
            ref Piece p = ref _pc[t];
            if (p.Kind is PK.Close or PK.Abs)
            {
                t--;
                continue;
            }

            if (p.Kind == PK.Text)
            {
                while (p.Cb > p.Ca && (ClusterFlags(p.Elem, p.Cb - 1) & CfCollapsible) != 0)
                {
                    p.Cb--;
                }

                if (p.Cb == p.Ca)
                {
                    p.Kind = PK.Dead;
                    p.W = 0;
                    t--;
                    continue;
                }

                p.W = TextWidth(p.Elem, p.Ca, p.Cb, out p.Seps);
            }

            break;
        }

        bool content = false;
        int m = 0;
        for (int j = 0; j < _pn; j++)
        {
            if (_pc[j].Kind == PK.Dead)
            {
                continue;
            }

            if (m != j)
            {
                _pc[m] = _pc[j];
            }

            PK kind = _pc[m].Kind;
            if (kind is PK.Text or PK.Atomic or PK.Tab or PK.Br or PK.Hyphen || (kind is PK.Open or PK.Close && _pc[m].W > 0))
            {
                content = true;
            }

            m++;
        }

        _pn = m;
        return content;
    }
}
