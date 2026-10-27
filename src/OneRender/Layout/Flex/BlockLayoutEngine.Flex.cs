using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

// Flex layout (CSS Flexible Box Layout 1). A flex container is a frame on the same explicit stack as block boxes: its items are
// opened one at a time at the sizes the flex algorithm chose, so nesting depth never reaches the call stack. See docs/architecture.md.
public sealed partial class BlockLayoutEngine
{
    private const byte ModeIdle = 0;
    private const byte ModeTrial = 1;
    private const byte ModeFinal = 2;

    private const int PhaseInit = 0;
    private const int PhaseBase = 1;
    private const int PhaseCross = 2;
    private const int PhaseFinal = 3;
    private const int PhaseClose = 4;

    // Item flags.
    private const int FrozenFlag = 1;
    private const int MinViolationFlag = 2;
    private const int StretchFlag = 4;
    private const int BaselineFlag = 8;
    private const int LastBaselineFlag = 16;
    private const int NeedBaseTrial = 32;
    private const int NeedMinTrial = 64;
    private const int NeedCrossTrial = 128;
    private const int ScrollFlag = 256;
    private const int ReplacedFlag = 512;

    private const int MaxResolveRounds = 256;
    private const long FlexBudgetBase = 1_000_000;
    private const long FlexBudgetPerBox = 64;

    /// <summary>One flex container being laid out.</summary>
    private struct FlexFrame
    {
        public int FrameIdx;
        public int BoxIdx;
        public int Phase;
        public int ItemStart;
        public int ItemCount;
        public int LineStart;
        public int LineCount;
        public int Cursor;
        public int CurItem;
        public int AbsStart;
        public int AbsCount;
        public byte Mode;
        public bool Pending;
        public bool Trial;
        public bool Row;
        public bool MainRev;
        public bool CrossRev;
        public bool DirRev;
        public bool Wrap;
        public bool WrapRev;
        public bool Rtl;

        // Size the next item is opened at: border-box width and height; NaN leaves the style in charge, a negative height means content height.
        public double ForceW;
        public double ForceH;

        public double CW;
        public double SpecH;
        public double MinH;
        public double MaxH;
        public double MainSize;
        public double CrossSize;
        public double MinCross;
        public double MaxCross;
        public double MainGap;
        public double CrossGap;
        public double AvailMain;
        public double Baseline;
        public FragmentMark Mark;
    }

    /// <summary>One flex item. Margins and sizes are in flow coordinates: "main" and "cross" run from the start edge of the axis.</summary>
    private struct FlexItem
    {
        public int Box;
        public int Order;
        public int Flags;
        public byte AutoMask;
        public AlignKind Align;
        public AlignOverflowKind AlignOv;
        public double MS, ME, CS, CE;
        public double PBMain, PBCross;
        public double Grow, Shrink;
        public double MainSpec;
        public double Base;
        public double Hypo;
        public double MinM, MaxM;
        public double Target;
        public double CrossStyle;
        public double MinC, MaxC;
        public double HypoCross;
        public double Cross;
        public double TrialH;
        public double TrialBase;
        public double TrialBaseLast;
        public double FinalBase;
        public double FinalLastBase;
        public double PosMain, PosCross;
        public double X, Y;
        public double FinalW, FinalH;
        public double BaseAbove, BaseBelow;
    }

    private struct FlexLine
    {
        public int First;
        public int Count;
        public double Cross;
        public double CrossPos;
        public double MaxAbove;
        public double MaxBelow;
        public double MaxAboveLast;
        public double MaxBelowLast;
        public double MainUsed;
    }

    private struct FlexMemo
    {
        public int Gen;
        public int Box;
        public double W, H, CW, CH;
        public double ResH, ResBase;
    }

    private FlexFrame[] _fx = new FlexFrame[8];
    private int _fxCount;
    private FlexItem[] _items = new FlexItem[64];
    private FlexItem[] _itemScratch = [];
    private int _itemTop;
    private FlexLine[] _lines = new FlexLine[16];
    private int _lineTop;
    private int _trialNesting;
    private long _flexWork;
    private long _flexBudget;
    private bool _budgetReported;
    private FlexMemo[] _memo = new FlexMemo[256];
    private int _memoGen;
    private int _memoCount;
    private int[] _absBoxes = new int[16];
    private int _absTop;

    private void PrepareFlex(int boxCount)
    {
        _fxCount = 0;
        _itemTop = 0;
        _lineTop = 0;
        _absTop = 0;
        _trialNesting = 0;
        _flexWork = 0;
        _flexBudget = FlexBudgetBase + FlexBudgetPerBox * boxCount;
        _budgetReported = false;
        if (++_memoGen == int.MaxValue)
        {
            Array.Clear(_memo);
            _memoGen = 1;
        }

        _memoCount = 0;
        if (_staticIdx.Length < boxCount)
        {
            _staticIdx = new int[Math.Max(boxCount, _staticIdx.Length * 2)];
        }

        Array.Fill(_staticIdx, -1, 0, boxCount);
        _staticCount = 0;
    }

    /// <summary>Turns the frame just pushed for a flex container into a flex frame; <see cref="FlexStep"/> drives it from then on.</summary>
    private void FlexOpen(int frameIdx, Box c)
    {
        if (_fxCount == _fx.Length)
        {
            Array.Resize(ref _fx, _fx.Length * 2);
        }

        int fi = _fxCount++;
        ref Frame fr = ref _frames[frameIdx];
        fr.Flags |= FrameFlags.Flex;
        fr.FlexIndex = fi;
        fr.NextChild = -1;
        _fx[fi] = new FlexFrame
        {
            FrameIdx = frameIdx,
            BoxIdx = c.Index,
            Phase = PhaseInit,
            ForceW = double.NaN,
            ForceH = double.NaN,
            Trial = _trialNesting > 0,
            Baseline = double.NaN,
        };
    }

    private void FlexStep(int top)
    {
        int fi = _frames[top].FlexIndex;
        while (true)
        {
            switch (_fx[fi].Phase)
            {
                case PhaseInit:
                    FlexInit(fi);
                    continue;
                case PhaseBase:
                    if (!FlexRunTrials(top, fi, baseKind: true))
                    {
                        return;
                    }

                    FlexResolve(fi);
                    _fx[fi].Phase = PhaseCross;
                    _fx[fi].Cursor = 0;
                    continue;
                case PhaseCross:
                    if (!FlexRunTrials(top, fi, baseKind: false))
                    {
                        return;
                    }

                    FlexCrossAndPlace(fi);
                    _fx[fi].Phase = _fx[fi].Trial ? PhaseClose : PhaseFinal;
                    _fx[fi].Cursor = 0;
                    continue;
                case PhaseFinal:
                    if (!FlexRunFinals(top, fi))
                    {
                        return;
                    }

                    _fx[fi].Phase = PhaseClose;
                    continue;
                default:
                    FlexClose(top, fi);
                    return;
            }
        }
    }

    /// <summary>
    /// Runs the item layouts of one phase that need a layout: the content height of column items (flex base size and automatic minimum
    /// size) or the hypothetical cross size of row items. Returns false when an item's frames are on the stack and the container must wait.
    /// </summary>
    private bool FlexRunTrials(int top, int fi, bool baseKind)
    {
        while (_fx[fi].Cursor < _fx[fi].ItemCount)
        {
            int idx = _fx[fi].ItemStart + _fx[fi].Cursor++;
            ref FlexItem it = ref _items[idx];
            if (baseKind)
            {
                if ((it.Flags & (NeedBaseTrial | NeedMinTrial)) == 0)
                {
                    continue;
                }

                // Flexbox §9.2 step 3: the content height at the item's cross size; its own height, min-height and max-height do not apply.
                if (!FlexLayoutItem(top, fi, idx, true, it.Cross, -1))
                {
                    return false;
                }
            }
            else
            {
                if ((it.Flags & NeedCrossTrial) == 0)
                {
                    continue;
                }

                if (!FlexLayoutItem(top, fi, idx, true, it.Target + it.PBMain, double.NaN))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool FlexRunFinals(int top, int fi)
    {
        while (_fx[fi].Cursor < _fx[fi].ItemCount)
        {
            int idx = _fx[fi].ItemStart + _fx[fi].Cursor++;
            ref FlexItem it = ref _items[idx];
            if (!FlexLayoutItem(top, fi, idx, false, it.FinalW, it.FinalH))
            {
                return false;
            }
        }

        return true;
    }

    private bool FlexLayoutItem(int top, int fi, int idx, bool trial, double forceW, double forceH)
    {
        Box box = _boxes.Make(_items[idx].Box);
        if (trial)
        {
            ref FlexFrame f0 = ref _fx[fi];
            if (MemoLookup(box.Index, forceW, forceH, f0.CW, f0.SpecH, out double rh, out double rb))
            {
                _items[idx].TrialH = rh;
                _items[idx].TrialBase = rb;
                _items[idx].TrialBaseLast = rb;
                return true;
            }

            if (++_flexWork > _flexBudget)
            {
                // A tree built to make trial layouts multiply: stop measuring, size from the style alone, and say so once.
                if (!_budgetReported)
                {
                    _budgetReported = true;
                    _boxes.Styled.Diagnostics.Add(DiagnosticKind.LimitExceeded, "flex layout measured too many items; the remaining items are sized from their styles");
                }

                _items[idx].TrialH = 0;
                _items[idx].TrialBase = double.NaN;
                _items[idx].TrialBaseLast = double.NaN;
                return true;
            }
        }

        ref FlexFrame fx = ref _fx[fi];
        fx.ForceW = forceW;
        fx.ForceH = forceH;
        fx.CurItem = idx;
        fx.Mode = trial ? ModeTrial : ModeFinal;
        fx.Pending = true;
        if (trial)
        {
            fx.Mark = _out.Mark(new Fragment(_out, _frames[top].Frag));
            _trialNesting++;
        }

        int sp = _sp;
        OpenChild(top, box);
        fx = ref _fx[fi];
        if (!fx.Pending)
        {
            return true;
        }

        if (_sp > sp)
        {
            return false;
        }

        // Nothing was laid out for the item (a depth limit): it counts as empty.
        FlexItemDone(top, 0, 0, 0, 0, double.NaN, 0, 0, 0, 0, false);
        return true;
    }

    /// <summary>An item finished (called from <c>Attach</c>): a measurement is memoized and undone, a real layout is placed.</summary>
    private void FlexItemDone(int pi, double x, double y, double w, double h, double baseline, double ovL, double ovT, double ovR, double ovB, bool scroll)
    {
        _ = x;
        _ = y;
        int fi = _frames[pi].FlexIndex;
        ref FlexFrame fx = ref _fx[fi];
        if (!fx.Pending)
        {
            return;
        }

        fx.Pending = false;
        ref FlexItem it = ref _items[fx.CurItem];
        if (fx.Mode == ModeTrial)
        {
            it.TrialH = h;
            it.TrialBase = baseline;
            it.TrialBaseLast = baseline;
            MemoStore(it.Box, fx.ForceW, fx.ForceH, fx.CW, fx.SpecH, h, baseline);
            _out.Truncate(fx.Mark);
            _trialNesting--;
        }
        else
        {
            it.FinalBase = baseline;
            Fragment frag = _out.FragmentOf(_boxes.Make(it.Box));
            if (!frag.IsNull)
            {
                ref Frame p = ref _frames[pi];
                double px = p.ContentLeft + it.X, py = p.ContentTop + it.Y;
                _out.SetBorderBox(frag, new Rect(px, py, w, h));
                if (scroll)
                {
                    ovL = 0;
                    ovT = 0;
                    ovR = w;
                    ovB = h;
                }

                p.OvL = Math.Min(p.OvL, px + ovL);
                p.OvT = Math.Min(p.OvT, py + ovT);
                p.OvR = Math.Max(p.OvR, px + ovR);
                p.OvB = Math.Max(p.OvB, py + ovB);
                it.FinalLastBase = LastBaselineFromFragment(frag, h);
            }
        }

        fx.Mode = ModeIdle;
    }

    /// <summary>The last baseline of a laid-out box: the baseline of its last line box, found down the chain of last children; NaN when it has none.</summary>
    private static double LastBaselineFromFragment(Fragment frag, double height)
    {
        _ = height;
        double offset = 0;
        Fragment f = frag;
        for (int guard = 0; guard < 4096 && !f.IsNull; guard++)
        {
            Fragment last = f.LastChild;
            if (last.IsNull)
            {
                return double.NaN;
            }

            Rect r = last.BorderBox;
            offset += r.Y;
            if (last.Kind == FragmentKind.Line)
            {
                return offset + last.Baseline;
            }

            if (last.Kind != FragmentKind.Box)
            {
                return double.NaN;
            }

            f = last;
        }

        return double.NaN;
    }

    private void FlexClose(int top, int fi)
    {
        ref FlexFrame fx = ref _fx[fi];
        FlexComputeBaseline(fi);
        FlexRecordStatics(fi);
        double contentH = fx.Row ? fx.CrossSize : fx.MainSize;
        if (double.IsNaN(contentH))
        {
            contentH = 0;
        }

        double baseline = fx.Baseline;
        ref Frame t = ref _frames[top];
        t.SpecH = contentH;
        t.MinH = 0;
        t.MaxH = double.PositiveInfinity;
        t.RatioH = double.NaN;
        t.FirstBaseline = double.IsNaN(baseline) ? double.NaN : t.ContentTop + baseline;
        t.Flags &= ~FrameFlags.Flex;
        t.Flags |= FrameFlags.HasContent | FrameFlags.AnyChild;
        _itemTop = fx.ItemStart;
        _lineTop = fx.LineStart;
        _absTop = fx.AbsStart;
        _fxCount = fi;
        CloseTop();
    }

    // ---- memo of measurements ----

    private static long Bits(double d) => double.IsNaN(d) ? 0x7FF8000000000000L : BitConverter.DoubleToInt64Bits(d);

    private static int MemoHash(int box, double w, double h, double cw, double ch)
    {
        unchecked
        {
            ulong x = (ulong)box * 0x9E3779B97F4A7C15UL;
            x ^= (ulong)Bits(w) * 0xC2B2AE3D27D4EB4FUL;
            x = (x << 29) | (x >> 35);
            x ^= (ulong)Bits(h) * 0x165667B19E3779F9UL;
            x = (x << 31) | (x >> 33);
            x ^= (ulong)Bits(cw) * 0x27D4EB2F165667C5UL;
            x ^= (ulong)Bits(ch) * 0x9E3779B185EBCA87UL;
            x ^= x >> 32;
            return (int)x;
        }
    }

    private bool MemoLookup(int box, double w, double h, double cw, double ch, out double resH, out double resBase)
    {
        resH = 0;
        resBase = double.NaN;
        int mask = _memo.Length - 1;
        int i = MemoHash(box, w, h, cw, ch) & mask;
        while (true)
        {
            ref FlexMemo m = ref _memo[i];
            if (m.Gen != _memoGen)
            {
                return false;
            }

            if (m.Box == box && Bits(m.W) == Bits(w) && Bits(m.H) == Bits(h) && Bits(m.CW) == Bits(cw) && Bits(m.CH) == Bits(ch))
            {
                resH = m.ResH;
                resBase = m.ResBase;
                return true;
            }

            i = (i + 1) & mask;
        }
    }

    private void MemoStore(int box, double w, double h, double cw, double ch, double resH, double resBase)
    {
        if ((_memoCount + 1) * 2 > _memo.Length)
        {
            MemoGrow();
        }

        int mask = _memo.Length - 1;
        int i = MemoHash(box, w, h, cw, ch) & mask;
        while (_memo[i].Gen == _memoGen)
        {
            i = (i + 1) & mask;
        }

        _memo[i] = new FlexMemo { Gen = _memoGen, Box = box, W = w, H = h, CW = cw, CH = ch, ResH = resH, ResBase = resBase };
        _memoCount++;
    }

    private void MemoGrow()
    {
        FlexMemo[] old = _memo;
        _memo = new FlexMemo[old.Length * 2];
        int mask = _memo.Length - 1;
        foreach (FlexMemo m in old)
        {
            if (m.Gen != _memoGen)
            {
                continue;
            }

            int i = MemoHash(m.Box, m.W, m.H, m.CW, m.CH) & mask;
            while (_memo[i].Gen == _memoGen)
            {
                i = (i + 1) & mask;
            }

            _memo[i] = m;
        }
    }

    /// <summary>The border-box size of a replaced item that the flex algorithm has resized (the ratio carries the other side).</summary>
    private static void ForceReplaced(double forceW, double forceH, double ph, double pv, ref double w, ref double h)
    {
        if (!double.IsNaN(forceW))
        {
            double nw = Math.Max(0, forceW - ph);
            if (!double.IsNaN(forceH) && forceH >= 0)
            {
                h = Math.Max(0, forceH - pv);
            }
            else if (w > 0 && nw != w)
            {
                h = h * nw / w;
            }

            w = nw;
        }
        else if (!double.IsNaN(forceH) && forceH >= 0)
        {
            double nh = Math.Max(0, forceH - pv);
            if (h > 0 && nh != h)
            {
                w = w * nh / h;
            }

            h = nh;
        }
    }
}
