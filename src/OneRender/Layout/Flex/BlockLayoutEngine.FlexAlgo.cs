using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    private static double GapPx(Length g, double basis)
    {
        switch (g.Kind)
        {
            case LengthKind.Px:
                return Math.Max(0, g.Value);
            case LengthKind.Percent:
            case LengthKind.Calc:
                if (double.IsNaN(basis))
                {
                    return 0;
                }

                double r = g.Resolve(basis);
                return double.IsNaN(r) ? 0 : Math.Max(0, r);
            default:
                return 0;
        }
    }

    private static double HeightLen(Length l, double basis, double pb, bool borderBox)
    {
        if (l.Kind == LengthKind.Px || (l.Kind is LengthKind.Percent or LengthKind.Calc && !double.IsNaN(basis)))
        {
            double v = l.Resolve(basis);
            if (double.IsNaN(v))
            {
                return double.NaN;
            }

            return Math.Max(0, borderBox ? v - pb : v);
        }

        return double.NaN;
    }

    private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

    /// <summary>Flexbox §9.1 to §9.2: collects the items and settles everything about them that needs no layout.</summary>
    private void FlexInit(int fi)
    {
        ref FlexFrame fx = ref _fx[fi];
        int top = fx.FrameIdx;
        Box c = _boxes.Make(fx.BoxIdx);
        ref readonly ComputedValues v = ref c.Values;
        ref Frame fr = ref _frames[top];
        double cw = fr.ContentWidth;
        fx.CW = cw;
        fx.SpecH = fr.SpecH;
        fx.MinH = fr.MinH;
        fx.MaxH = fr.MaxH;
        bool rtl = v.Direction == DirectionKind.Rtl;
        FlexDirectionKind dir = v.FlexDirection;
        bool row = dir is FlexDirectionKind.Row or FlexDirectionKind.RowReverse;
        fx.Row = row;
        fx.Rtl = rtl;
        fx.DirRev = dir is FlexDirectionKind.RowReverse or FlexDirectionKind.ColumnReverse;
        fx.MainRev = row ? fx.DirRev ^ rtl : fx.DirRev;
        fx.Wrap = v.FlexWrap != FlexWrapKind.NoWrap;
        fx.WrapRev = v.FlexWrap == FlexWrapKind.WrapReverse;
        fx.CrossRev = row ? fx.WrapRev : rtl ^ fx.WrapRev;
        double columnGap = GapPx(v.ColumnGap, cw);
        double rowGap = GapPx(v.RowGap, fx.SpecH);
        fx.MainGap = row ? columnGap : rowGap;
        fx.CrossGap = row ? rowGap : columnGap;
        if (row)
        {
            fx.MainSize = cw;
            fx.CrossSize = fx.SpecH;
            fx.MinCross = fx.MinH;
            fx.MaxCross = fx.MaxH;
            fx.AvailMain = cw;
        }
        else
        {
            fx.MainSize = fx.SpecH;
            fx.CrossSize = cw;
            fx.MinCross = cw;
            fx.MaxCross = cw;
            fx.AvailMain = double.IsNaN(fx.SpecH) ? fx.MaxH : fx.SpecH;
        }

        int start = _itemTop;
        int n = 0;
        fx.AbsStart = _absTop;
        for (Box k = c.FirstChild; !k.IsNull; k = k.NextSibling)
        {
            if (k.Kind is not (BoxKind.Container or BoxKind.Replaced) || (k.Flags & BoxTraits.OutsideMarker) != 0)
            {
                continue;
            }

            if (k.Values.Position is PositionKind.Absolute or PositionKind.Fixed)
            {
                // Flexbox §4.1: laid out later, with the container's content box as its static-position rectangle. A trial
                // layout is thrown away, so it queues nothing.
                if (!fx.Trial && _absCount < MaxAbsolute)
                {
                    Enqueue(in _frames[top], k, fr.ContentLeft, fr.ContentLeft + cw, fr.ContentTop);
                    if (_absTop == _absBoxes.Length)
                    {
                        Array.Resize(ref _absBoxes, _absBoxes.Length * 2);
                    }

                    _absBoxes[_absTop++] = _absCount - 1;
                }

                continue;
            }

            if (start + n == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }

            FlexPrepItem(ref fx, k, ref _items[start + n], in v);
            n++;
        }

        fx = ref _fx[fi];
        fx.AbsCount = _absTop - fx.AbsStart;
        fx.ItemStart = start;
        fx.ItemCount = n;
        _itemTop = start + n;
        FlexSortByOrder(start, n);
        fx.LineStart = _lineTop;
        fx.Phase = PhaseBase;
        fx.Cursor = 0;
    }

    /// <summary>Flexbox §5.4: a stable sort of the items by <c>order</c>; already-sorted input (the common case) is left alone.</summary>
    private void FlexSortByOrder(int start, int n)
    {
        bool sorted = true;
        for (int i = 1; i < n; i++)
        {
            if (_items[start + i].Order < _items[start + i - 1].Order)
            {
                sorted = false;
                break;
            }
        }

        if (sorted)
        {
            return;
        }

        if (_itemScratch.Length < n)
        {
            _itemScratch = new FlexItem[Math.Max(n, _itemScratch.Length * 2)];
        }

        // Bottom-up merge sort: stable, no recursion.
        FlexItem[] src = _items;
        FlexItem[] dst = _itemScratch;
        int srcOff = start, dstOff = 0;
        for (int width = 1; width < n; width *= 2)
        {
            for (int lo = 0; lo < n; lo += 2 * width)
            {
                int mid = Math.Min(lo + width, n), hi = Math.Min(lo + 2 * width, n);
                int a = lo, b = mid, o = lo;
                while (a < mid && b < hi)
                {
                    dst[dstOff + o++] = src[srcOff + b].Order < src[srcOff + a].Order ? src[srcOff + b++] : src[srcOff + a++];
                }

                while (a < mid)
                {
                    dst[dstOff + o++] = src[srcOff + a++];
                }

                while (b < hi)
                {
                    dst[dstOff + o++] = src[srcOff + b++];
                }
            }

            (src, dst) = (dst, src);
            (srcOff, dstOff) = (dstOff, srcOff);
        }

        if (!ReferenceEquals(src, _items))
        {
            Array.Copy(src, srcOff, _items, start, n);
        }
    }

    private void FlexPrepItem(ref FlexFrame fx, Box k, ref FlexItem it, in ComputedValues cv)
    {
        it = default;
        it.TrialBase = it.TrialBaseLast = it.FinalBase = it.FinalLastBase = double.NaN;
        ref readonly ComputedValues iv = ref k.Values;
        double cw = fx.CW;
        bool row = fx.Row;
        it.Box = k.Index;
        it.Order = iv.Order;
        it.Grow = iv.FlexGrow;
        it.Shrink = iv.FlexShrink;
        bool borderBox = iv.BoxSizing == BoxSizingKind.BorderBox;
        double pl = Pad(iv.PaddingLeft, cw), pr = Pad(iv.PaddingRight, cw), pt = Pad(iv.PaddingTop, cw), pb = Pad(iv.PaddingBottom, cw);
        double ph = pl + pr + iv.BorderLeftWidth + iv.BorderRightWidth;
        double pv = pt + pb + iv.BorderTopWidth + iv.BorderBottomWidth;
        bool mlA = iv.MarginLeft.IsAuto, mrA = iv.MarginRight.IsAuto, mtA = iv.MarginTop.IsAuto, mbA = iv.MarginBottom.IsAuto;
        double mL = mlA ? 0 : MarginPx(iv.MarginLeft, cw), mR = mrA ? 0 : MarginPx(iv.MarginRight, cw);
        double mT = mtA ? 0 : MarginPx(iv.MarginTop, cw), mB = mbA ? 0 : MarginPx(iv.MarginBottom, cw);
        int mask = 0;
        if (row)
        {
            // Physical to flow: with a reversed main axis the right margin sits at the main start.
            if (fx.MainRev)
            {
                it.MS = mR;
                it.ME = mL;
                mask |= (mrA ? 1 : 0) | (mlA ? 2 : 0);
            }
            else
            {
                it.MS = mL;
                it.ME = mR;
                mask |= (mlA ? 1 : 0) | (mrA ? 2 : 0);
            }

            if (fx.CrossRev)
            {
                it.CS = mB;
                it.CE = mT;
                mask |= (mbA ? 4 : 0) | (mtA ? 8 : 0);
            }
            else
            {
                it.CS = mT;
                it.CE = mB;
                mask |= (mtA ? 4 : 0) | (mbA ? 8 : 0);
            }

            it.PBMain = ph;
            it.PBCross = pv;
        }
        else
        {
            if (fx.MainRev)
            {
                it.MS = mB;
                it.ME = mT;
                mask |= (mbA ? 1 : 0) | (mtA ? 2 : 0);
            }
            else
            {
                it.MS = mT;
                it.ME = mB;
                mask |= (mtA ? 1 : 0) | (mbA ? 2 : 0);
            }

            if (fx.CrossRev)
            {
                it.CS = mR;
                it.CE = mL;
                mask |= (mrA ? 4 : 0) | (mlA ? 8 : 0);
            }
            else
            {
                it.CS = mL;
                it.CE = mR;
                mask |= (mlA ? 4 : 0) | (mrA ? 8 : 0);
            }

            it.PBMain = pv;
            it.PBCross = ph;
        }

        it.AutoMask = (byte)mask;
        AlignKind a = iv.AlignSelf == AlignKind.Auto ? cv.AlignItems : iv.AlignSelf;
        it.AlignOv = iv.AlignSelf == AlignKind.Auto ? cv.AlignItemsOverflow : iv.AlignSelfOverflow;
        if (a == AlignKind.Normal)
        {
            a = AlignKind.Stretch;
        }

        if (!row && a is AlignKind.Baseline or AlignKind.LastBaseline)
        {
            // Baselines are only shared along the cross axis of a row container; a column falls back to flex-start (Flexbox §8.3).
            a = AlignKind.FlexStart;
        }

        it.Align = a;
        if (a == AlignKind.Baseline)
        {
            it.Flags |= BaselineFlag;
        }
        else if (a == AlignKind.LastBaseline)
        {
            it.Flags |= BaselineFlag | LastBaselineFlag;
        }

        bool replaced = k.Kind == BoxKind.Replaced;
        if (replaced)
        {
            it.Flags |= ReplacedFlag;
        }

        bool scroll = iv.IsScrollContainer;
        if (scroll)
        {
            it.Flags |= ScrollFlag;
        }

        double availInner = Math.Max(0, cw - mL - mR - ph);
        double wS = ResolveWidthLength(k, iv.Width, cw, ph, iv.BoxSizing, availInner, false);
        double wMin = ResolveWidthLength(k, iv.MinWidth, cw, ph, iv.BoxSizing, availInner, true);
        double wMax = ResolveWidthLength(k, iv.MaxWidth, cw, ph, iv.BoxSizing, availInner, false);
        double hS = HeightLen(iv.Height, fx.SpecH, pv, borderBox);
        double hMin = HeightLen(iv.MinHeight, fx.SpecH, pv, borderBox);
        double hMax = HeightLen(iv.MaxHeight, fx.SpecH, pv, borderBox);
        double mainS = row ? wS : hS;
        double crossS = row ? hS : wS;
        double minMain = row ? wMin : hMin, maxMain = row ? wMax : hMax;
        double minCross = row ? hMin : wMin, maxCross = row ? hMax : wMax;
        bool minAuto = row ? iv.MinWidth.Kind == LengthKind.Auto : iv.MinHeight.Kind == LengthKind.Auto;
        it.MainSpec = mainS;
        it.CrossStyle = crossS;
        it.MinC = double.IsNaN(minCross) ? 0 : minCross;
        it.MaxC = double.IsNaN(maxCross) ? double.PositiveInfinity : maxCross;
        it.MaxM = double.IsNaN(maxMain) ? double.PositiveInfinity : maxMain;

        double rw = 0, rh = 0;
        if (replaced)
        {
            ReplacedContentSize(k, in iv, cw, fx.SpecH, ph, pv, availInner, out rw, out rh);
        }

        // Flexbox §9.2 step 3: the flex base size.
        double basis = double.NaN;
        bool contentBasis = iv.FlexBasisContent;
        if (!contentBasis)
        {
            Length fb = iv.FlexBasis;
            if (fb.Kind == LengthKind.Auto)
            {
                basis = mainS;
            }
            else if (row)
            {
                basis = ResolveWidthLength(k, fb, cw, ph, iv.BoxSizing, availInner, false);
            }
            else
            {
                basis = HeightLen(fb, fx.SpecH, pv, borderBox);
            }
        }

        double ratio = iv.AspectRatio;
        bool needBaseTrial = false;
        double baseSize;
        if (!double.IsNaN(basis))
        {
            baseSize = basis;
        }
        else if (replaced)
        {
            baseSize = row ? rw : rh;
        }
        else if (ratio > 0 && !double.IsNaN(crossS))
        {
            // Flexbox §9.2 step 3 B: an item with a preferred aspect ratio and a definite cross size takes its main size from the ratio.
            double crossBox = borderBox ? crossS + it.PBCross : crossS;
            double mainBox = row ? crossBox * ratio : crossBox / ratio;
            baseSize = Math.Max(0, borderBox ? mainBox - it.PBMain : mainBox);
        }
        else if (row)
        {
            GetIntrinsic(k, out _, out double imax);
            baseSize = imax;
        }
        else
        {
            baseSize = 0;
            needBaseTrial = true;
        }

        it.Base = Math.Max(0, baseSize);

        // Flexbox §4.5: the automatic minimum size.
        if (minAuto)
        {
            if (scroll)
            {
                it.MinM = 0;
            }
            else if (row)
            {
                GetIntrinsic(k, out double imin, out _);
                if (replaced)
                {
                    imin = rw;
                }

                double spec = double.IsNaN(mainS) ? double.PositiveInfinity : mainS;
                it.MinM = Math.Min(Math.Min(spec, imin), it.MaxM);
            }
            else if (replaced)
            {
                double spec = double.IsNaN(mainS) ? double.PositiveInfinity : mainS;
                it.MinM = Math.Min(Math.Min(spec, rh), it.MaxM);
            }
            else
            {
                it.Flags |= NeedMinTrial;
            }
        }
        else
        {
            it.MinM = double.IsNaN(minMain) ? 0 : minMain;
        }

        if (needBaseTrial)
        {
            it.Flags |= NeedBaseTrial;
        }

        // The cross size of a column item is settled now: its width decides the content height (Flexbox §9.2 step 3 C).
        if (!row)
        {
            double inner;
            bool stretchOk = it.Align == AlignKind.Stretch && double.IsNaN(crossS) && (mask & 12) == 0;
            if (!double.IsNaN(crossS))
            {
                inner = Clamp(crossS, it.MinC, it.MaxC);
            }
            else if (stretchOk)
            {
                inner = Clamp(availInner, it.MinC, it.MaxC);
            }
            else if (replaced)
            {
                inner = Clamp(rw, it.MinC, it.MaxC);
            }
            else
            {
                GetIntrinsic(k, out double imin, out double imax);
                inner = Clamp(Math.Min(imax, Math.Max(imin, availInner)), it.MinC, it.MaxC);
            }

            it.Cross = inner + it.PBCross;
            it.HypoCross = it.Cross;
        }
    }

    /// <summary>Flexbox §9.3 to §9.7: hypothetical main sizes, flex lines and flexible lengths.</summary>
    private void FlexResolve(int fi)
    {
        ref FlexFrame fx = ref _fx[fi];
        int s = fx.ItemStart, n = fx.ItemCount;
        for (int i = s; i < s + n; i++)
        {
            ref FlexItem it = ref _items[i];
            if ((it.Flags & (NeedBaseTrial | NeedMinTrial)) != 0)
            {
                double content = Math.Max(0, it.TrialH - it.PBMain);
                if ((it.Flags & NeedBaseTrial) != 0)
                {
                    it.Base = content;
                }

                if ((it.Flags & NeedMinTrial) != 0)
                {
                    double spec = double.IsNaN(it.MainSpec) ? double.PositiveInfinity : it.MainSpec;
                    it.MinM = Math.Min(Math.Min(spec, content), it.MaxM);
                }
            }

            it.Hypo = Clamp(it.Base, it.MinM, it.MaxM);
            it.Flags &= ~(FrozenFlag | MinViolationFlag);
        }

        // §9.3: flex lines.
        double avail = fx.AvailMain;
        int lineStart = fx.LineStart;
        int lines = 0;
        int first = s;
        double sum = 0;
        double maxLine = 0;
        for (int i = s; i < s + n; i++)
        {
            ref FlexItem it = ref _items[i];
            double outer = it.Hypo + it.PBMain + it.MS + it.ME;
            bool breakBefore = fx.Wrap && i > first && sum + fx.MainGap + outer > avail + 1e-6;
            if (breakBefore)
            {
                AddLine(lineStart + lines++, first, i - first, sum);
                maxLine = Math.Max(maxLine, sum);
                first = i;
                sum = 0;
            }

            sum += (i > first ? fx.MainGap : 0) + outer;
        }

        if (n > 0)
        {
            AddLine(lineStart + lines++, first, s + n - first, sum);
            maxLine = Math.Max(maxLine, sum);
        }

        fx.LineCount = lines;
        _lineTop = lineStart + lines;

        // §9.2 step 9: with no definite main size the container is as large as its content wants.
        if (double.IsNaN(fx.MainSize))
        {
            fx.MainSize = Clamp(maxLine, fx.MinH, fx.MaxH);
        }

        for (int l = 0; l < lines; l++)
        {
            ResolveFlexibleLengths(ref fx, lineStart + l);
        }

        // Which items still need a layout to learn their cross size (row containers only; a column item's width is known).
        for (int i = s; i < s + n; i++)
        {
            ref FlexItem it = ref _items[i];
            it.Flags &= ~NeedCrossTrial;
            if (!fx.Row)
            {
                continue;
            }

            bool baseline = (it.Flags & BaselineFlag) != 0;
            bool crossAuto = double.IsNaN(it.CrossStyle);
            bool stretches = it.Align == AlignKind.Stretch && crossAuto && (it.AutoMask & 12) == 0;
            bool lineIsContainer = !fx.Wrap && !double.IsNaN(fx.CrossSize);
            if (baseline || (crossAuto && !(stretches && lineIsContainer)))
            {
                it.Flags |= NeedCrossTrial;
            }
            else if (!crossAuto)
            {
                it.HypoCross = Clamp(it.CrossStyle, it.MinC, it.MaxC) + it.PBCross;
            }
        }
    }

    private void AddLine(int index, int first, int count, double used)
    {
        if (index >= _lines.Length)
        {
            Array.Resize(ref _lines, Math.Max(_lines.Length * 2, index + 1));
        }

        _lines[index] = new FlexLine { First = first, Count = count, MainUsed = used };
    }

    /// <summary>Flexbox §9.7: resolving flexible lengths for one line.</summary>
    private void ResolveFlexibleLengths(ref FlexFrame fx, int lineIdx)
    {
        ref FlexLine line = ref _lines[lineIdx];
        int a = line.First, b = line.First + line.Count;
        double gaps = fx.MainGap * Math.Max(0, line.Count - 1);
        bool grow = line.MainUsed < fx.MainSize;
        double initialFree = 0;
        int unfrozen = 0;
        for (int i = a; i < b; i++)
        {
            ref FlexItem it = ref _items[i];
            double factor = grow ? it.Grow : it.Shrink;
            bool frozen = factor == 0 || (grow ? it.Base > it.Hypo : it.Base < it.Hypo);
            if (frozen)
            {
                it.Flags |= FrozenFlag;
                it.Target = it.Hypo;
            }
            else
            {
                it.Flags &= ~FrozenFlag;
                it.Target = it.Base;
                unfrozen++;
            }
        }

        initialFree = FreeSpace(a, b, fx.MainSize, gaps);
        for (int round = 0; unfrozen > 0 && round < MaxResolveRounds; round++)
        {
            double remaining = FreeSpace(a, b, fx.MainSize, gaps);
            double sumFactor = 0, scaledSum = 0;
            for (int i = a; i < b; i++)
            {
                ref FlexItem it = ref _items[i];
                if ((it.Flags & FrozenFlag) == 0)
                {
                    double f = grow ? it.Grow : it.Shrink;
                    sumFactor += f;
                    scaledSum += it.Shrink * it.Base;
                }
            }

            if (sumFactor < 1)
            {
                double scaled = initialFree * sumFactor;
                if (Math.Abs(scaled) < Math.Abs(remaining))
                {
                    remaining = scaled;
                }
            }

            double total = 0;
            for (int i = a; i < b; i++)
            {
                ref FlexItem it = ref _items[i];
                if ((it.Flags & FrozenFlag) != 0)
                {
                    continue;
                }

                double t = it.Base;
                if (grow)
                {
                    if (sumFactor > 0)
                    {
                        t = it.Base + remaining * it.Grow / sumFactor;
                    }
                }
                else if (scaledSum > 0)
                {
                    t = it.Base + remaining * (it.Shrink * it.Base) / scaledSum;
                }

                double clamped = Clamp(t, it.MinM, it.MaxM);
                if (clamped < 0)
                {
                    clamped = 0;
                }

                total += clamped - t;
                it.Target = t;
                it.BaseAbove = clamped;
            }

            // §9.7 step 4e: freeze on the violations; with none, freeze everything.
            for (int i = a; i < b; i++)
            {
                ref FlexItem it = ref _items[i];
                if ((it.Flags & FrozenFlag) != 0)
                {
                    continue;
                }

                double clamped = it.BaseAbove;
                double unclamped = it.Target;
                it.Target = clamped;
                bool freeze = total == 0 || (total > 0 && clamped > unclamped) || (total < 0 && clamped < unclamped);
                if (freeze)
                {
                    it.Flags |= FrozenFlag;
                    unfrozen--;
                }
            }
        }

        // Rounds are capped (hostile min/max mixes); what is left keeps its clamped value.
        for (int i = a; i < b; i++)
        {
            ref FlexItem it = ref _items[i];
            it.Flags &= ~FrozenFlag;
            it.BaseAbove = 0;
        }
    }

    private double FreeSpace(int a, int b, double mainSize, double gaps)
    {
        double used = gaps;
        for (int i = a; i < b; i++)
        {
            ref FlexItem it = ref _items[i];
            double size = (it.Flags & FrozenFlag) != 0 ? it.Target : it.Base;
            used += size + it.PBMain + it.MS + it.ME;
        }

        return mainSize - used;
    }
}
