using OneFont;
using OneRender.Style;
using OneRender.Text;

namespace OneRender.Layout;

internal sealed partial class InlineWorker
{
    private const double Eps = 1e-5;

    // What ScanLine reports.
    private int _endE;
    private int _endC;
    private bool _endForced;     // the line ends at a forced break or the end of the paragraph
    private bool _endHyphen;     // the line ends at a soft hyphen: a hyphen is drawn after its last character
    private double _lineW;       // width of the line's content without trailing collapsible or hanging spaces
    private int _measureWhich;   // 0 layout; 1 min-content (atomic W); 2 max-content (atomic W2)
    private bool _measureMin;

    private double AtomicWidth(in Elem e) => _measureWhich == 2 ? e.W2 : e.W;

    private static double TabAdvance(in Elem e, double x)
    {
        double unit = e.W;
        if (unit <= 0)
        {
            return 0;
        }

        double stops = Math.Floor((x + Eps) / unit) + 1;
        double w = stops * unit - x;
        return w <= Eps ? unit : w;
    }

    private static double ClusterAdvance(in Elem e, in GlyphCluster cl, byte flags)
    {
        double adv = cl.Advance + e.Ls;
        if ((flags & CfWordSep) != 0)
        {
            adv += (e.Flags & EF.WsPercent) != 0 ? e.Ws * cl.Advance : e.Ws;
        }

        return adv;
    }

    /// <summary>
    /// Finds where the line that starts at (<paramref name="e0"/>, <paramref name="c0"/>) ends: greedy breaking at the last opportunity
    /// that fits (CSS Text 3 §5), with trailing collapsible spaces and hanging preserved spaces not counted, and an emergency break
    /// inside a word only when the line has no other opportunity (<c>overflow-wrap</c>). Elements are counted in whole clusters.
    /// </summary>
    /// <param name="e0">The first element of the line.</param>
    /// <param name="c0">The first cluster of that element (logical order).</param>
    /// <param name="avail">The width the line may fill, including the indent.</param>
    /// <param name="indent">The part of the width taken by <c>text-indent</c>.</param>
    private void ScanLine(int e0, int c0, double avail, double indent)
    {
        int e = e0;
        int c = c0;
        double w = indent;     // everything placed so far
        double wTrim = indent; // w without the trailing hanging spaces
        bool ink = false;
        int placed = 0;
        int candE = -1, candC = 0, candPos = -1;
        double candW = 0;
        bool candHy = false;
        while (true)
        {
            if ((++_tick & 1023) == 0)
            {
                _ct.ThrowIfCancellationRequested();
            }

            if (e >= _elN)
            {
                SetEnd(e, 0, true, false, wTrim);
                return;
            }

            ref Elem el = ref _el[e];
            switch (el.Kind)
            {
                case EK.Open:
                    if (ink && placed > 0 && _opp[el.Start] != OppNone && el.Start != candPos)
                    {
                        candE = e; candC = 0; candPos = el.Start; candW = wTrim; candHy = false;
                    }

                    w += el.W;
                    wTrim = w;
                    e++;
                    c = 0;
                    break;
                case EK.Close:
                    w += el.W;
                    wTrim += el.W;
                    e++;
                    c = 0;
                    break;
                case EK.Atomic:
                {
                    if (placed > 0 && _opp[el.Start] != OppNone && el.Start != candPos)
                    {
                        candE = e; candC = 0; candPos = el.Start; candW = wTrim; candHy = false;
                    }

                    double aw = AtomicWidth(in el);
                    if (placed > 0 && w + aw > avail + Eps && candE >= 0)
                    {
                        SetEnd(candE, candC, false, candHy, candW);
                        return;
                    }

                    w += aw;
                    wTrim = w;
                    ink = true;
                    placed++;
                    e++;
                    c = 0;
                    break;
                }

                case EK.Float:
                    el.W2 = wTrim; // how much of the line is before the float, for the "does it fit here" test
                    e++;
                    c = 0;
                    break;
                case EK.Tab:
                    w += TabAdvance(in el, w);
                    wTrim = w;
                    ink = true;
                    placed++;
                    e++;
                    c = 0;
                    break;
                case EK.Newline:
                    SetEnd(e + 1, 0, true, false, wTrim);
                    return;
                default:
                {
                    ShapedText st = _shaped[e]!;
                    ReadOnlySpan<GlyphCluster> cls = st.Clusters;
                    int k = cls.Length;
                    bool rtl = (el.Flags & EF.Rtl) != 0;
                    bool wrap = (el.Flags & EF.Wrap) != 0;
                    bool emergency = (el.Flags & (_measureMin ? EF.EmergencyMeasure : EF.Emergency)) != 0;
                    for (int i = c; i < k; i++)
                    {
                        GlyphCluster cl = cls[rtl ? k - 1 - i : i];
                        int pos = el.Start + cl.TextStart;
                        byte fl = _cf[pos];
                        bool collapsible = (fl & CfCollapsible) != 0;
                        if (!ink && collapsible)
                        {
                            continue; // CSS Text 3 §4.1.2: collapsible spaces at the start of a line are removed
                        }

                        if (placed > 0 && wrap && _opp[pos] != OppNone && pos != candPos)
                        {
                            bool hy = pos > 0 && (_cf[pos - 1] & CfShy) != 0 && V(el.Box).Hyphens != HyphensKind.None && (_opp[pos] == OppAllowed);
                            double hyW = hy ? HyphenWidth(in el) : 0;
                            if (!hy || wTrim + hyW <= avail + Eps)
                            {
                                candE = e; candC = i; candPos = pos; candW = wTrim + hyW; candHy = hy;
                            }
                        }

                        double adv = ClusterAdvance(in el, in cl, fl);
                        bool hang = collapsible || ((fl & CfPreserved) != 0 && wrap && (fl & CfBreakSpace) == 0);
                        if (hang)
                        {
                            w += adv;
                            if (!collapsible)
                            {
                                ink = true;
                                placed++;
                            }

                            continue;
                        }

                        if (placed > 0 && w + adv > avail + Eps)
                        {
                            if (candE >= 0)
                            {
                                SetEnd(candE, candC, false, candHy, candW);
                                return;
                            }

                            if (emergency)
                            {
                                SetEnd(e, i, false, false, wTrim);
                                return;
                            }
                        }

                        w += adv;
                        wTrim = w;
                        ink = true;
                        placed++;
                    }

                    e++;
                    c = 0;
                    break;
                }
            }
        }
    }

    private void SetEnd(int e, int c, bool forced, bool hyphen, double lineW)
    {
        _endE = e;
        _endC = c;
        _endForced = forced;
        _endHyphen = hyphen;
        _lineW = lineW;
    }

    private double HyphenWidth(in Elem el)
    {
        ref readonly ComputedValues v = ref V(el.Box);
        ShapedText h = _fonts.Shape("-", in v, (el.Flags & EF.Rtl) != 0);
        return h.Width + el.Ls;
    }
}
