using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    // Bounds on hostile input. A block formatting context that needs more than MaxFloatWork float visits to place floats and
    // answer line queries, or holds more than MaxFloats floats, stops searching: later floats are stacked below everything
    // placed so far and lines ignore floats. A diagnostic says so.
    private const int MaxFloats = 1 << 18;
    private const long MaxFloatWork = 50_000_000;
    private const double FitEpsilon = 1e-6;

    /// <summary>A placed float's margin box in the coordinates of its block formatting context root's content box.</summary>
    private struct FloatRect
    {
        public double Left;
        public double Top;
        public double Right;
        public double Bottom;
        public bool IsLeft;
    }

    /// <summary>Per block formatting context state; the floats are the tail of <see cref="_floats"/> from <see cref="FloatBase"/>.</summary>
    private struct BfcState
    {
        public int FloatBase;
        public int LiveBase;
        public int DefBase;

        // CSS2 §9.5.1 rule 5: no float starts higher than the highest-starting (lowest) float placed so far.
        public double TopFloor;
        public double MaxBottomLeft;
        public double MaxBottomRight;
        public double MaxBottom;

        // Union of the float border boxes, for the scrollable overflow of the root.
        public double OvL;
        public double OvT;
        public double OvR;
        public double OvB;

        public long Work;
        public bool Capped;
    }

    /// <summary>A laid-out float waiting for a position.</summary>
    private struct PendingFloat
    {
        public int Frag;
        public double Bw;
        public double Bh;
        public double Mt;
        public double Mr;
        public double Mb;
        public double Ml;
        public bool Left;
        public ClearKind Clear;
        public double OwnerBfcLeft;
        public double OwnerWidth;
        public double OwnerContentLeft;
        public double OwnerContentTop;
    }

    private FloatRect[] _floats = new FloatRect[64];
    private int _floatCount;

    // Indices of the floats that can still affect a float placed from now on (those whose bottom is below the lowest float top).
    private int[] _live = new int[64];
    private int _liveCount;

    private BfcState[] _bfcs = new BfcState[16];
    private int _bsp;

    private PendingFloat[] _deferred = new PendingFloat[8];
    private int _defCount;

    private void PushBfc()
    {
        if (_bsp == _bfcs.Length)
        {
            Array.Resize(ref _bfcs, _bfcs.Length * 2);
        }

        _bfcs[_bsp++] = new BfcState
        {
            FloatBase = _floatCount,
            LiveBase = _liveCount,
            DefBase = _defCount,
            TopFloor = double.NegativeInfinity,
            MaxBottomLeft = double.NegativeInfinity,
            MaxBottomRight = double.NegativeInfinity,
            MaxBottom = double.NegativeInfinity,
            OvL = double.PositiveInfinity,
            OvT = double.PositiveInfinity,
            OvR = double.NegativeInfinity,
            OvB = double.NegativeInfinity,
        };
    }

    private void PopBfc()
    {
        ref BfcState s = ref _bfcs[--_bsp];
        _floatCount = s.FloatBase;
        _liveCount = s.LiveBase;
        _defCount = s.DefBase;
    }

    private static double ClearEdge(in BfcState s, ClearKind clear) => clear switch
    {
        ClearKind.Left => s.MaxBottomLeft,
        ClearKind.Right => s.MaxBottomRight,
        ClearKind.Both => Math.Max(s.MaxBottomLeft, s.MaxBottomRight),
        _ => double.NegativeInfinity,
    };

    private void Cap(ref BfcState s, string why)
    {
        if (!s.Capped)
        {
            s.Capped = true;
            _boxes.Styled.Diagnostics.Add(DiagnosticKind.LimitExceeded, why);
        }
    }

    /// <summary>
    /// The room between the floats that intersect the band [y, y + h) inside [cbL, cbR], and the smallest bottom edge among them.
    /// The floats are sorted by top (rule 5 of CSS2 §9.5.1), so the scan stops at the first one that starts below the band.
    /// </summary>
    private void BandAll(ref BfcState s, double y, double h, double cbL, double cbR, out double left, out double right, out double next)
    {
        left = cbL;
        right = cbR;
        next = double.PositiveInfinity;
        if (s.Capped)
        {
            return;
        }

        double yEnd = y + h;
        int i = s.FloatBase;
        int end = _floatCount;
        for (; i < end; i++)
        {
            ref FloatRect f = ref _floats[i];
            if (h > 0 ? f.Top >= yEnd : f.Top > y)
            {
                break;
            }

            if (f.Bottom <= y)
            {
                continue;
            }

            if (f.IsLeft)
            {
                if (f.Right > left)
                {
                    left = f.Right;
                }
            }
            else if (f.Left < right)
            {
                right = f.Left;
            }

            if (f.Bottom < next)
            {
                next = f.Bottom;
            }
        }

        s.Work += i - s.FloatBase;
        if (s.Work > MaxFloatWork)
        {
            Cap(ref s, "floats in one block formatting context need too much work to lay out; later floats are stacked and lines ignore floats");
        }
    }

    /// <summary>
    /// As <see cref="BandAll"/>, over the floats that can still matter to a float placed now, and also reporting the raw edges: the
    /// right edge of the rightmost left float and the left edge of the leftmost right float in the band, not limited to the
    /// containing block (negative and positive infinity when there are none). Rules 3 and 7 of CSS2 §9.5.1 need the difference.
    /// </summary>
    private void BandLive(ref BfcState s, double y, double h, double cbL, double cbR, out double left, out double right, out double next, out double rawLeft, out double rawRight)
    {
        left = cbL;
        right = cbR;
        next = double.PositiveInfinity;
        rawLeft = double.NegativeInfinity;
        rawRight = double.PositiveInfinity;
        if (s.Capped)
        {
            return;
        }

        double yEnd = y + h;
        int end = _liveCount;
        int i = s.LiveBase;
        for (; i < end; i++)
        {
            ref FloatRect f = ref _floats[_live[i]];
            if (h > 0 ? f.Top >= yEnd : f.Top > y)
            {
                break;
            }

            if (f.Bottom <= y)
            {
                continue;
            }

            if (f.IsLeft)
            {
                if (f.Right > rawLeft)
                {
                    rawLeft = f.Right;
                }
            }
            else if (f.Left < rawRight)
            {
                rawRight = f.Left;
            }

            if (f.Bottom < next)
            {
                next = f.Bottom;
            }
        }

        if (rawLeft > left)
        {
            left = rawLeft;
        }

        if (rawRight < right)
        {
            right = rawRight;
        }

        s.Work += i - s.LiveBase;
        if (s.Work > MaxFloatWork)
        {
            Cap(ref s, "floats in one block formatting context need too much work to lay out; later floats are stacked and lines ignore floats");
        }
    }

    /// <summary>Drops the floats that end at or above <paramref name="y"/> from the live list: no float placed from y downward can meet them.</summary>
    private void PruneLive(ref BfcState s, double y)
    {
        int write = s.LiveBase;
        for (int i = s.LiveBase; i < _liveCount; i++)
        {
            int idx = _live[i];
            if (_floats[idx].Bottom > y)
            {
                _live[write++] = idx;
            }
        }

        _liveCount = write;
    }

    /// <summary>
    /// CSS2 §9.5.1: positions a laid-out float in the block formatting context on top of the stack and sets its border box in its
    /// containing block's fragment. Returns its margin box in block-formatting-context coordinates.
    /// </summary>
    private Rect PlaceFloatRecord(in PendingFloat r, double ownerBfcTop, double minTop)
    {
        ref BfcState s = ref _bfcs[_bsp - 1];
        double w = Math.Max(0, r.Ml + r.Bw + r.Mr);
        double h = Math.Max(0, r.Mt + r.Bh + r.Mb);
        double cbL = r.OwnerBfcLeft, cbR = cbL + r.OwnerWidth;

        // Rule 4 and 5: not above the top of the containing block (minTop) nor above any earlier float. CSS2 §9.5.2: clear.
        double y = Math.Max(minTop, s.TopFloor);
        if (r.Clear != ClearKind.None)
        {
            y = Math.Max(y, ClearEdge(in s, r.Clear));
        }

        double x = r.Left ? cbL : cbR - w;
        if (!s.Capped)
        {
            PruneLive(ref s, y);
            while (true)
            {
                BandLive(ref s, y, h, cbL, cbR, out double bandLeft, out double bandRight, out double next, out double rawLeft, out double rawRight);
                if (s.Capped)
                {
                    break;
                }

                // Rules 2, 3, 7 and 9: as far left (right) as the earlier floats allow; rule 8: as high as possible.
                // Rule 3 is measured against the other side's floats wherever they are; rule 7 applies only when another float on
                // the same side is in the way, wherever it is (a float with no other float on its side may stick out of its containing block).
                bool fits = r.Left
                    ? bandLeft + w <= rawRight + FitEpsilon && (double.IsNegativeInfinity(rawLeft) || bandLeft + w <= cbR + FitEpsilon)
                    : bandRight - w >= rawLeft - FitEpsilon && (double.IsPositiveInfinity(rawRight) || bandRight - w >= cbL - FitEpsilon);
                if (fits || double.IsPositiveInfinity(next))
                {
                    // With nothing left to wait for, a float wider than its containing block sits against its edge (rules 1 and 7).
                    x = r.Left ? bandLeft : bandRight - w;
                    break;
                }

                y = next;
            }
        }

        if (s.Capped)
        {
            if (s.MaxBottom > y)
            {
                y = s.MaxBottom;
            }

            x = r.Left ? cbL : cbR - w;
        }

        double bottom = y + h;
        if (y > s.TopFloor)
        {
            s.TopFloor = y;
        }

        if (bottom > s.MaxBottom)
        {
            s.MaxBottom = bottom;
        }

        if (r.Left)
        {
            if (bottom > s.MaxBottomLeft)
            {
                s.MaxBottomLeft = bottom;
            }
        }
        else if (bottom > s.MaxBottomRight)
        {
            s.MaxBottomRight = bottom;
        }

        if (w > 0 && h > 0 && !s.Capped)
        {
            if (_floatCount - s.FloatBase >= MaxFloats)
            {
                Cap(ref s, "more than " + MaxFloats.ToString(System.Globalization.CultureInfo.InvariantCulture) + " floats in one block formatting context; later floats are stacked and lines ignore floats");
            }
            else
            {
                if (_floatCount == _floats.Length)
                {
                    Array.Resize(ref _floats, _floats.Length * 2);
                }

                if (_liveCount == _live.Length)
                {
                    Array.Resize(ref _live, _live.Length * 2);
                }

                _floats[_floatCount] = new FloatRect { Left = x, Top = y, Right = x + w, Bottom = bottom, IsLeft = r.Left };
                _live[_liveCount++] = _floatCount++;
            }
        }

        double borderLeft = x + r.Ml, borderTop = y + r.Mt;
        s.OvL = Math.Min(s.OvL, borderLeft);
        s.OvT = Math.Min(s.OvT, borderTop);
        s.OvR = Math.Max(s.OvR, borderLeft + r.Bw);
        s.OvB = Math.Max(s.OvB, borderTop + r.Bh);
        _out.SetBorderBox(new Fragment(_out, r.Frag), new Rect(r.OwnerContentLeft + (borderLeft - r.OwnerBfcLeft), r.OwnerContentTop + (borderTop - ownerBfcTop), r.Bw, r.Bh));
        return new Rect(x, y, w, h);
    }

    /// <summary>
    /// A float of the block flow has been laid out (its fragment exists, its size is known): positions it at the current flow
    /// position of its parent, or, while the parent's top margin is still pending, holds it until that position is settled.
    /// </summary>
    private void FinishFloat(int pi, int frag, double bw, double bh, double mt, double mr, double mb, double ml, in ComputedValues v)
    {
        ref Frame p = ref _frames[pi];
        var rec = new PendingFloat
        {
            Frag = frag,
            Bw = bw,
            Bh = bh,
            Mt = mt,
            Mr = mr,
            Mb = mb,
            Ml = ml,
            Left = v.Floating == FloatKind.Left,
            Clear = v.Clear,
            OwnerBfcLeft = p.BfcLeft,
            OwnerWidth = p.ContentWidth,
            OwnerContentLeft = p.ContentLeft,
            OwnerContentTop = p.ContentTop,
        };
        if ((p.Flags & FrameFlags.TopPending) != 0)
        {
            // CSS2 §8.3.1 and §9.5.1 rule 4: the float sits where the box that holds it ends up, which margins still to come may move.
            if (_defCount == _deferred.Length)
            {
                Array.Resize(ref _deferred, _deferred.Length * 2);
            }

            rec.OwnerContentTop = 0;
            _deferred[_defCount++] = rec;
            return;
        }

        // The float goes where a block would: after the margins collected so far (rule 5 keeps it below earlier floats).
        double minTop = p.BfcTop + p.Cursor + _pos + _neg;
        PlaceFloatRecord(in rec, p.BfcTop, minTop);
    }

    /// <summary>Whether the block formatting context being laid out holds floats that wait for their position.</summary>
    private bool HasDeferred => _bsp > 0 && _defCount > _bfcs[_bsp - 1].DefBase;

    /// <summary>Whether a <c>clear</c> value clears one of the floats waiting for their position.</summary>
    private bool DeferredClearedBy(ClearKind clear)
    {
        for (int i = _bfcs[_bsp - 1].DefBase; i < _defCount; i++)
        {
            if (clear == ClearKind.Both || (clear == ClearKind.Left) == _deferred[i].Left)
            {
                return true;
            }
        }

        return false;
    }

    private void FlushDeferred(double chainTop)
    {
        int first = _bfcs[_bsp - 1].DefBase;
        int n = _defCount;
        _defCount = first;
        for (int i = first; i < n; i++)
        {
            PlaceFloatRecord(in _deferred[i], chainTop, chainTop);
        }
    }

    // ---- IExclusionSpace ----

    void IExclusionSpace.Query(double bfcTop, double height, double bfcLeft, double bfcRight, out double rangeStart, out double rangeEnd, out double nextEdge)
    {
        if (_bsp == 0)
        {
            rangeStart = bfcLeft;
            rangeEnd = Math.Max(bfcLeft, bfcRight);
            nextEdge = double.PositiveInfinity;
            return;
        }

        BandAll(ref _bfcs[_bsp - 1], bfcTop, Math.Max(0, height), bfcLeft, bfcRight, out rangeStart, out double right, out nextEdge);
        rangeEnd = Math.Max(rangeStart, right);
    }

    Fragment IExclusionSpace.PlaceFloat(in InlineLayoutRequest request, Fragment container, Box floatBox, double minBfcTop, out Rect marginBox)
    {
        marginBox = default;
        if (floatBox.IsNull || !ReferenceEquals(floatBox.Tree, _boxes) || floatBox.Values.Floating == FloatKind.None || (floatBox.Flags & BoxTraits.OutOfFlow) == 0)
        {
            throw new ArgumentException("The box is not a float of the box tree being laid out.", nameof(floatBox));
        }

        if (_bsp == 0)
        {
            return default;
        }

        Fragment frag = LayoutIndependent(floatBox, container, request.AvailableWidth, double.NaN, true, out Insets m);
        if (frag.IsNull)
        {
            _boxes.Styled.Diagnostics.Add(DiagnosticKind.LimitExceeded, "floats nested in inline content deeper than " + MaxReentrancy.ToString(System.Globalization.CultureInfo.InvariantCulture) + " levels are not laid out");
            return default;
        }

        Rect bb = frag.BorderBox;
        var rec = new PendingFloat
        {
            Frag = frag.Index,
            Bw = bb.Width,
            Bh = bb.Height,
            Mt = m.Top,
            Mr = m.Right,
            Mb = m.Bottom,
            Ml = m.Left,
            Left = floatBox.Values.Floating == FloatKind.Left,
            Clear = floatBox.Values.Clear,
            OwnerBfcLeft = request.BfcLeft,
            OwnerWidth = request.AvailableWidth,
            OwnerContentLeft = request.ContentLeft,
            OwnerContentTop = request.ContentTop,
        };
        marginBox = PlaceFloatRecord(in rec, request.BfcTop, minBfcTop);
        return frag;
    }
}
