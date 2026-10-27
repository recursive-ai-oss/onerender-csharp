namespace OneRender.Layout;

internal sealed partial class Fragmenter
{
    // Fragmentainer records of the pass being recorded: where each starts and ends in strip coordinates.
    private double[] _fkYs = new double[16];
    private double[] _fkYe = new double[16];
    private double[] _fkT = new double[16];
    private double[] _fkB = new double[16];
    private bool[] _fkBlank = new bool[16];
    private int _fkN;

    // Page planning: the block sizes of the first fragmentainers repeat their last two entries alternately when set, so a left/right
    // pattern continues past the explicit entries; and a forced break to a left or right page may insert a blank page.
    private bool _cyclic;
    private bool _sidesOn;
    private bool _firstRight = true;
    private int _cap = MaxFragmentainers;
    private readonly double[] _one = new double[1];

    private void Record(double ys, double ye, double t, double b)
    {
        if (_fkN == _fkYs.Length)
        {
            int n = _fkN * 2;
            Array.Resize(ref _fkYs, n);
            Array.Resize(ref _fkYe, n);
            Array.Resize(ref _fkT, n);
            Array.Resize(ref _fkB, n);
            Array.Resize(ref _fkBlank, n);
        }

        _fkYs[_fkN] = ys;
        _fkYe[_fkN] = ye;
        _fkT[_fkN] = t;
        _fkB[_fkN] = b;
        _fkBlank[_fkN] = false;
        _fkN++;
    }

    private int UpperBound(double y, int lo, int hi)
    {
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_c[mid].Ye <= y)
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

    // A filler (an empty box with a height) that the fragmentainer's end falls inside: the cut position, or NaN.
    private bool FindFiller(double limit, double start, out double y, out double cloneTop, out double cloneBottom)
    {
        y = 0;
        cloneTop = cloneBottom = 0;
        int lo = 0, hi = _fn;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_fillers[mid].Top < limit - Eps)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        if (lo == 0)
        {
            return false;
        }

        // Fillers are disjoint in block order, but several can start above the limit; the last one is the one that can contain it.
        ref Filler f = ref _fillers[lo - 1];
        double cut = limit - f.CloneBottom;
        if (cut > f.Top + Eps && cut < f.Bottom - Eps && cut > start + Eps)
        {
            y = cut;
            cloneTop = f.CloneTop;
            cloneBottom = f.CloneBottom;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Cuts the strip between <paramref name="rowStart"/> and <paramref name="rowEnd"/> into fragmentainers of the given block sizes by the
    /// rules of CSS Fragmentation 3 §4.4: forced breaks first, then the latest break the rules allow, dropping orphans and widows and
    /// then the avoid rules when nothing else fits, and as a last resort the first break after the point where content no longer
    /// fits (the content overflows). Fragmentainers are at least 1px high (§4).
    /// </summary>
    /// <param name="c0">First candidate of the row.</param>
    /// <param name="c1">End of the row's candidates.</param>
    /// <param name="rowStart">Strip offset where the row's content starts.</param>
    /// <param name="rowEnd">Strip offset where it ends.</param>
    /// <param name="sizes">The block size of the first fragmentainer, the second and so on; the last entry repeats.</param>
    /// <param name="stopAbove">Give up (reporting <c>stopAbove + 1</c> fragmentainers) when the row needs more than this many.</param>
    /// <param name="record">Whether to append the fragmentainers to the record arrays (see <see cref="ClearRecord"/>).</param>
    internal SliceResult Slice(int c0, int c1, double rowStart, double rowEnd, ReadOnlySpan<double> sizes, int stopAbove, bool record)
    {
        var res = new SliceResult();
        double s = rowStart, t = 0;
        int p = c0;
        int k = 0;
        int cap = _cap;
        while (true)
        {
            double h = _cyclic && sizes.Length >= 2 && k >= sizes.Length ? sizes[sizes.Length - 2 + ((k - sizes.Length) & 1)] : sizes[Math.Min(k, sizes.Length - 1)];
            if (!(h >= 1))
            {
                h = 1;
            }

            double limit = s + h - t;
            while (p < c1 && _c[p].Ye < s - Eps)
            {
                p++;
            }

            int cut = -1;
            bool filler = false;
            double fy = 0, fT = 0, fB = 0;
            bool final = false;
            int f = p < c1 ? Math.Min(_nextForced[p], c1) : c1;
            bool capped = k + 1 >= cap;
            if (f < c1 && _c[f].Ye <= limit + Eps && !capped)
            {
                cut = f;
            }
            else if (rowEnd <= limit + Eps)
            {
                final = true;
                if (capped && f < c1 && _c[f].Ye <= limit + Eps)
                {
                    res.Truncated = true; // a forced break past the page limit is not taken
                }
            }
            else if (k + 1 >= cap)
            {
                final = true;
                res.Truncated = true;
            }
            else
            {
                int e = UpperBound(limit + Eps, p, c1) - 1;
                while (e >= p && _c[e].Ye + _c[e].CloneBottom > limit + Eps)
                {
                    e--;
                }

                // Progress: a break at the very top of the fragmentainer would leave it empty (§4.4).
                int lo = UpperBound(s + Eps, p, c1);
                if (e >= lo)
                {
                    int i0 = _last0[e];
                    if (i0 >= lo)
                    {
                        cut = i0;
                    }
                    else
                    {
                        int i1 = _last1[e];
                        cut = i1 >= lo ? i1 : e;
                    }
                }

                if (FindFiller(limit, s, out fy, out fT, out fB) && (cut < 0 || _c[cut].Level > 0 || fy > _c[cut].Ye))
                {
                    filler = true;
                }

                if (cut < 0 && !filler)
                {
                    int n = Math.Max(e + 1, lo);
                    if (n < c1)
                    {
                        cut = n;
                        res.Overflow = true;
                    }
                    else
                    {
                        final = true;
                    }
                }
            }

            if (final)
            {
                if (rowEnd > limit + Eps)
                {
                    res.Overflow = true;
                }

                res.Count++;
                res.MaxExtent = Math.Max(res.MaxExtent, rowEnd - s + t);
                if (record)
                {
                    Record(s, rowEnd, t, 0);
                }

                return res;
            }

            double ye, ys, nt, nb;
            if (filler)
            {
                ye = ys = fy;
                nt = fT;
                nb = fB;
            }
            else
            {
                BreakCandidate cc = _c[cut];
                ye = cc.Ye;
                ys = cc.Ys;
                nt = cc.CloneTop;
                nb = cc.CloneBottom;
                if (!cc.Forced && cc.Level > res.MaxLevel)
                {
                    res.MaxLevel = cc.Level;
                }

                p = cut + 1;
            }

            res.Count++;
            res.MaxExtent = Math.Max(res.MaxExtent, ye - s + t + nb);
            if (record)
            {
                Record(s, ye, t, nb);
            }

            if (res.Count > stopAbove)
            {
                return res;
            }

            s = ys;
            t = nt;
            k++;
            if (_sidesOn && !filler && (_c[cut].Bits & SideMask) != 0 && NeedsBlank(_c[cut].Bits, k) && k + 1 < cap)
            {
                // Paged Media 3 §7.1 / Fragmentation 3 §3.1: break-*: left | right | recto | verso make the next page that side by
                // leaving one blank page when the natural next page is the other side.
                res.Count++;
                if (record)
                {
                    Record(s, s, 0, 0);
                    _fkBlank[_fkN - 1] = true;
                }

                k++;
                if (res.Count > stopAbove)
                {
                    return res;
                }
            }
        }
    }

    /// <summary>Slices with one block size for every fragmentainer.</summary>
    internal SliceResult Slice(int c0, int c1, double rowStart, double rowEnd, double size, int stopAbove, bool record)
    {
        _one[0] = size;
        return Slice(c0, c1, rowStart, rowEnd, _one, stopAbove, record);
    }

    private const BreakBits SideMask = BreakBits.SideLeft | BreakBits.SideRight | BreakBits.SideRecto | BreakBits.SideVerso;

    // Whether page index k is a right-hand page: the first page is on the right in a left-to-right document and on the left otherwise.
    private bool IsRightPage(int k) => ((k & 1) == 0) == _firstRight;

    private bool NeedsBlank(BreakBits bits, int k)
    {
        bool right = IsRightPage(k);
        // Several side values on one break: the first in the order left, right, recto, verso decides.
        if ((bits & BreakBits.SideLeft) != 0)
        {
            return right;
        }

        if ((bits & BreakBits.SideRight) != 0)
        {
            return !right;
        }

        // recto is the right page of a left-to-right document and the left page of a right-to-left one; verso the other.
        bool recto = (bits & BreakBits.SideRecto) != 0;
        bool wantRight = recto == _firstRight;
        return right != wantRight;
    }

    /// <summary>Forgets the recorded fragmentainers.</summary>
    internal void ClearRecord() => _fkN = 0;

    internal int RecordCount => _fkN;

    /// <summary>
    /// Finds the smallest block size (to 1/64 px) at which the row fits in <paramref name="columns"/> fragmentainers without breaking
    /// a rule and without overflow; failing that, one that fits ignoring the rules; failing that, <paramref name="upper"/>.
    /// At most 2 × 40 trial slices, each of which stops as soon as it needs more than <paramref name="columns"/> fragmentainers.
    /// </summary>
    internal double Balance(int c0, int c1, double rowStart, double rowEnd, int columns, double upper)
    {
        double total = rowEnd - rowStart;
        if (!(total > 0))
        {
            return 0;
        }

        double hi = Math.Min(upper, total);
        for (int strict = 1; strict >= 0; strict--)
        {
            long loU = 63;
            long hiU = (long)Math.Ceiling(hi * 64);
            if (loU >= hiU)
            {
                loU = hiU - 1;
            }

            if (!Fits(c0, c1, rowStart, rowEnd, hiU / 64.0, columns, strict == 1))
            {
                continue;
            }

            for (int it = 0; it < 40 && hiU - loU > 1; it++)
            {
                long mid = loU + ((hiU - loU) >> 1);
                if (Fits(c0, c1, rowStart, rowEnd, mid / 64.0, columns, strict == 1))
                {
                    hiU = mid;
                }
                else
                {
                    loU = mid;
                }
            }

            return hiU / 64.0;
        }

        return hi;
    }

    private bool Fits(int c0, int c1, double rowStart, double rowEnd, double h, int columns, bool strict)
    {
        SliceResult r = Slice(c0, c1, rowStart, rowEnd, h, columns, false);
        return r.Count <= columns && !r.Overflow && (!strict || r.MaxLevel == 0);
    }
}
