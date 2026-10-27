using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    /// <summary>
    /// CSS Box Alignment 3 §5 and §6 inside a flex container: the space before the first item and between neighbours for
    /// <paramref name="n"/> items in <paramref name="free"/> px of free space (negative when they overflow).
    /// </summary>
    private static void Distribute(AlignKind k, AlignOverflowKind ov, double free, int n, bool startFlip, bool horizontal, bool leftIsStart, out double lead, out double between)
    {
        between = 0;
        AlignKind e;
        switch (k)
        {
            case AlignKind.Start:
            case AlignKind.SelfStart:
                e = startFlip ? AlignKind.FlexEnd : AlignKind.FlexStart;
                break;
            case AlignKind.End:
            case AlignKind.SelfEnd:
                e = startFlip ? AlignKind.FlexStart : AlignKind.FlexEnd;
                break;
            case AlignKind.Left:
                e = horizontal ? (leftIsStart ? AlignKind.FlexStart : AlignKind.FlexEnd) : (startFlip ? AlignKind.FlexEnd : AlignKind.FlexStart);
                break;
            case AlignKind.Right:
                e = horizontal ? (leftIsStart ? AlignKind.FlexEnd : AlignKind.FlexStart) : (startFlip ? AlignKind.FlexStart : AlignKind.FlexEnd);
                break;
            case AlignKind.Center:
            case AlignKind.FlexEnd:
            case AlignKind.SpaceBetween:
            case AlignKind.SpaceAround:
            case AlignKind.SpaceEvenly:
                e = k;
                break;
            default:
                e = AlignKind.FlexStart;
                break;
        }

        // Box Alignment 3 §4.4: safe alignment falls back to the start edge instead of overflowing it.
        bool safe = ov == AlignOverflowKind.Safe && free < 0;
        switch (e)
        {
            case AlignKind.FlexEnd:
                lead = safe ? 0 : free;
                break;
            case AlignKind.Center:
                lead = safe ? 0 : free / 2;
                break;
            case AlignKind.SpaceBetween:
                lead = 0;
                if (n > 1 && free > 0)
                {
                    between = free / (n - 1);
                }

                break;
            case AlignKind.SpaceAround:
                if (free > 0)
                {
                    between = free / n;
                    lead = between / 2;
                }
                else
                {
                    // Flexbox §8.2: with no positive free space the value is identical to (safe) center.
                    lead = ov == AlignOverflowKind.Unsafe ? free / 2 : 0;
                }

                break;
            case AlignKind.SpaceEvenly:
                if (free > 0)
                {
                    between = free / (n + 1);
                    lead = between;
                }
                else
                {
                    lead = ov == AlignOverflowKind.Unsafe ? free / 2 : 0;
                }

                break;
            default:
                lead = 0;
                break;
        }
    }

    /// <summary>Flexbox §9.4 to §9.6: cross sizes, line positions, alignment of every item, and the container's final size.</summary>
    private void FlexCrossAndPlace(int fi)
    {
        ref FlexFrame fx = ref _fx[fi];
        Box c = _boxes.Make(fx.BoxIdx);
        ref readonly ComputedValues v = ref c.Values;
        int s = fx.ItemStart, n = fx.ItemCount;
        bool row = fx.Row;
        for (int i = s; i < s + n; i++)
        {
            ref FlexItem it = ref _items[i];
            if ((it.Flags & NeedCrossTrial) != 0)
            {
                it.HypoCross = Math.Max(it.PBCross, it.TrialH);
            }
        }

        // §9.4 steps 8 and 9: the cross size of each line.
        double sumCross = 0;
        for (int l = 0; l < fx.LineCount; l++)
        {
            ref FlexLine line = ref _lines[fx.LineStart + l];
            line.MaxAbove = line.MaxBelow = line.MaxAboveLast = line.MaxBelowLast = 0;
            double maxOuter = 0;
            for (int i = line.First; i < line.First + line.Count; i++)
            {
                ref FlexItem it = ref _items[i];
                double outer = it.HypoCross + it.CS + it.CE;
                if ((it.Flags & BaselineFlag) == 0)
                {
                    if (it.Align != AlignKind.Stretch || !double.IsNaN(it.CrossStyle) || (it.AutoMask & 12) != 0 || !(row && !fx.Wrap && !double.IsNaN(fx.CrossSize)))
                    {
                        maxOuter = Math.Max(maxOuter, outer);
                    }

                    continue;
                }

                if ((it.Flags & LastBaselineFlag) == 0)
                {
                    double bt = double.IsNaN(it.TrialBase) ? it.HypoCross : it.TrialBase;
                    it.BaseAbove = it.CS + bt;
                    it.BaseBelow = outer - it.BaseAbove;
                    line.MaxAbove = Math.Max(line.MaxAbove, it.BaseAbove);
                    line.MaxBelow = Math.Max(line.MaxBelow, it.BaseBelow);
                }
                else
                {
                    double bl = double.IsNaN(it.TrialBaseLast) ? it.HypoCross : it.TrialBaseLast;
                    it.BaseBelow = it.CE + (it.HypoCross - bl);
                    it.BaseAbove = outer - it.BaseBelow;
                    line.MaxAboveLast = Math.Max(line.MaxAboveLast, it.BaseAbove);
                    line.MaxBelowLast = Math.Max(line.MaxBelowLast, it.BaseBelow);
                }
            }

            if (!fx.Wrap && !double.IsNaN(fx.CrossSize))
            {
                line.Cross = fx.CrossSize;
            }
            else
            {
                line.Cross = Math.Max(maxOuter, Math.Max(line.MaxAbove + line.MaxBelow, line.MaxAboveLast + line.MaxBelowLast));
                if (!fx.Wrap)
                {
                    line.Cross = Clamp(line.Cross, fx.MinCross, fx.MaxCross);
                }
            }

            sumCross += line.Cross;
        }

        sumCross += fx.CrossGap * Math.Max(0, fx.LineCount - 1);
        if (double.IsNaN(fx.CrossSize))
        {
            fx.CrossSize = Clamp(sumCross, fx.MinCross, fx.MaxCross);
        }

        // §9.4 step 9 and §9.6: align-content for a multi-line container.
        double lead = 0, between = 0;
        if (fx.Wrap && fx.LineCount > 0)
        {
            double free = fx.CrossSize - sumCross;
            AlignKind ac = v.AlignContent;
            if (ac is AlignKind.Normal or AlignKind.Stretch)
            {
                if (free > 0)
                {
                    double share = free / fx.LineCount;
                    for (int l = 0; l < fx.LineCount; l++)
                    {
                        _lines[fx.LineStart + l].Cross += share;
                    }
                }
            }
            else
            {
                Distribute(ac, v.AlignContentOverflow, free, fx.LineCount, fx.WrapRev, !row, !fx.CrossRev, out lead, out between);
            }
        }

        double linePos = lead;
        for (int l = 0; l < fx.LineCount; l++)
        {
            ref FlexLine line = ref _lines[fx.LineStart + l];
            line.CrossPos = linePos;
            linePos += line.Cross + fx.CrossGap + between;
            FlexPlaceLine(ref fx, in line, in v);
        }

        FlexFinishPlacement(fi);
    }

    private void FlexPlaceLine(ref FlexFrame fx, in FlexLine line, in ComputedValues v)
    {
        int a = line.First, b = line.First + line.Count;
        bool row = fx.Row;
        double used = fx.MainGap * Math.Max(0, line.Count - 1);
        int autos = 0;
        for (int i = a; i < b; i++)
        {
            ref FlexItem it = ref _items[i];
            used += it.Target + it.PBMain + it.MS + it.ME;
            autos += (it.AutoMask & 1) + ((it.AutoMask >> 1) & 1);
        }

        double free = fx.MainSize - used;
        double lead = 0, between = 0;
        if (free > 0 && autos > 0)
        {
            // Flexbox §8.1: auto margins take the free space before any justification.
            double share = free / autos;
            for (int i = a; i < b; i++)
            {
                ref FlexItem it = ref _items[i];
                if ((it.AutoMask & 1) != 0)
                {
                    it.MS = share;
                }

                if ((it.AutoMask & 2) != 0)
                {
                    it.ME = share;
                }
            }
        }
        else
        {
            Distribute(v.JustifyContent, v.JustifyContentOverflow, free, line.Count, fx.DirRev, row, !fx.MainRev, out lead, out between);
        }

        double pos = lead;
        for (int i = a; i < b; i++)
        {
            ref FlexItem it = ref _items[i];
            it.PosMain = pos;
            pos += it.Target + it.PBMain + it.MS + it.ME + fx.MainGap + between;

            // Cross axis.
            double lineC = line.Cross;
            bool crossAuto = double.IsNaN(it.CrossStyle);
            bool autoStart = (it.AutoMask & 4) != 0, autoEnd = (it.AutoMask & 8) != 0;
            if (it.Align == AlignKind.Stretch && crossAuto && !autoStart && !autoEnd)
            {
                double inner = Clamp(lineC - it.CS - it.CE - it.PBCross, it.MinC, it.MaxC);
                it.Cross = inner + it.PBCross;
                it.Flags |= StretchFlag;
            }
            else
            {
                it.Cross = it.HypoCross;
                it.Flags &= ~StretchFlag;
            }

            double outer = it.Cross + it.CS + it.CE;
            double freeC = lineC - outer;
            double off;
            if (autoStart || autoEnd)
            {
                // Flexbox §8.1: auto margins in the cross axis absorb the free space; align-self is ignored.
                off = 0;
                if (freeC > 0)
                {
                    if (autoStart && autoEnd)
                    {
                        it.CS = freeC / 2;
                        it.CE = freeC / 2;
                    }
                    else if (autoStart)
                    {
                        it.CS = freeC;
                    }
                    else
                    {
                        it.CE = freeC;
                    }
                }
            }
            else if ((it.Flags & BaselineFlag) != 0)
            {
                off = (it.Flags & LastBaselineFlag) == 0 ? line.MaxAbove - it.BaseAbove : lineC - (line.MaxBelowLast - it.BaseBelow) - outer;
            }
            else
            {
                Distribute(it.Align, it.AlignOv, freeC, 1, fx.WrapRev, !row, !fx.CrossRev, out off, out _);
            }

            it.PosCross = line.CrossPos + off;
        }
    }

    /// <summary>Turns flow positions into the physical position of each item's border box and the sizes the real layout is opened at.</summary>
    private void FlexFinishPlacement(int fi)
    {
        ref FlexFrame fx = ref _fx[fi];
        for (int i = fx.ItemStart; i < fx.ItemStart + fx.ItemCount; i++)
        {
            ref FlexItem it = ref _items[i];
            double mainBB = it.Target + it.PBMain;
            double mpos = it.PosMain + it.MS;
            double physMain = fx.MainRev ? fx.MainSize - (mpos + mainBB) : mpos;
            double cpos = it.PosCross + it.CS;
            double physCross = fx.CrossRev ? fx.CrossSize - (cpos + it.Cross) : cpos;
            if (fx.Row)
            {
                it.X = physMain;
                it.Y = physCross;
                it.FinalW = mainBB;
                it.FinalH = (it.Flags & StretchFlag) != 0 ? it.Cross : double.NaN;
            }
            else
            {
                it.X = physCross;
                it.Y = physMain;
                it.FinalW = it.Cross;
                it.FinalH = mainBB;
            }
        }
    }

    /// <summary>Flexbox §8.5: the container's first baseline, relative to its content box (NaN when it has no items).</summary>
    private void FlexComputeBaseline(int fi)
    {
        ref FlexFrame fx = ref _fx[fi];
        fx.Baseline = double.NaN;
        if (fx.ItemCount == 0 || fx.LineCount == 0)
        {
            return;
        }

        ref FlexLine line = ref _lines[fx.LineStart];
        int pick = line.First;
        if (fx.Row)
        {
            for (int i = line.First; i < line.First + line.Count; i++)
            {
                if ((_items[i].Flags & BaselineFlag) != 0)
                {
                    pick = i;
                    break;
                }
            }
        }

        ref FlexItem it = ref _items[pick];
        double b = fx.Trial || double.IsNaN(it.FinalBase) ? it.TrialBase : it.FinalBase;
        if (double.IsNaN(b))
        {
            // Flexbox §8.5: an item without a baseline contributes one synthesized from its border box.
            b = it.Cross;
            if (!fx.Row)
            {
                b = it.Target + it.PBMain;
            }
        }

        fx.Baseline = it.Y + b;
    }

    /// <summary>Gives the absolutely positioned children of a container the alignment of a sole flex item (Flexbox §4.1).</summary>
    private void FlexPatchAbsolute(int fi)
    {
        ref FlexFrame fx = ref _fx[fi];
        if (fx.AbsCount == 0)
        {
            return;
        }

        ref Frame t = ref _frames[fx.FrameIdx];
        Box c = _boxes.Make(fx.BoxIdx);
        double h = fx.Row ? fx.CrossSize : fx.MainSize;
        for (int i = fx.AbsStart; i < fx.AbsStart + fx.AbsCount; i++)
        {
            ref AbsEntry e = ref _abs[_absBoxes[i]];
            Box k = _boxes.Make(e.Box);
            ref readonly ComputedValues kv = ref k.Values;
            AlignKind cross = kv.AlignSelf == AlignKind.Auto ? c.Values.AlignItems : kv.AlignSelf;
            AlignOverflowKind crossOv = kv.AlignSelf == AlignKind.Auto ? c.Values.AlignItemsOverflow : kv.AlignSelfOverflow;
            Distribute(c.Values.JustifyContent, c.Values.JustifyContentOverflow, 1, 1, fx.DirRev, fx.Row, !fx.MainRev, out double mf, out _);
            Distribute(cross, crossOv, 1, 1, fx.WrapRev, !fx.Row, !fx.CrossRev, out double cf, out _);
            mf = Math.Clamp(mf, 0, 1);
            cf = Math.Clamp(cf, 0, 1);
            if (fx.MainRev)
            {
                mf = 1 - mf;
            }

            if (fx.CrossRev)
            {
                cf = 1 - cf;
            }

            e.HasArea = true;
            e.AreaX = t.ContentLeft;
            e.AreaY = t.ContentTop;
            e.AreaW = fx.CW;
            e.AreaH = double.IsNaN(h) ? 0 : h;
            e.FracX = fx.Row ? mf : cf;
            e.FracY = fx.Row ? cf : mf;
        }
    }
}
