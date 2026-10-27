using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    private void OpenChild(int pi, Box c)
    {
        // Items of a flex container are opened like the root of a block formatting context of their own, at the size flex layout chose.
        bool parentIsFlex = (_frames[pi].Flags & FrameFlags.Flex) != 0;
        bool parentIsRoot = _frames[pi].Box < 0 || parentIsFlex;
        bool shrink = _shrinkNext && parentIsRoot;
        _shrinkNext = false;
        int preFragment = _absPre;
        _absPre = -1;
        bool forced = _absForce;
        _absForce = false;
        bool outOfFlow = !parentIsRoot && (c.Flags & BoxTraits.OutOfFlow) != 0;
        if (outOfFlow && c.Values.Floating == FloatKind.None)
        {
            // Absolutely positioned boxes wait until their containing block is final (CSS2 §10.1); until then they only hold a place.
            if (c.Values.Position is PositionKind.Absolute or PositionKind.Fixed)
            {
                QueueAbsolute(pi, c);
            }

            return;
        }

        if (c.Kind == BoxKind.Marker)
        {
            if ((c.Flags & BoxTraits.OutsideMarker) != 0)
            {
                PlaceOutsideMarker(pi, c);
            }

            return;
        }

        if (c.Kind is not (BoxKind.Container or BoxKind.Replaced))
        {
            return;
        }

        if (_sp >= MaxDepth)
        {
            if (!_depthReported)
            {
                _depthReported = true;
                _boxes.Styled.Diagnostics.Add(DiagnosticKind.LimitExceeded, "block nesting deeper than " + MaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture) + " levels is not laid out");
            }

            return;
        }

        ref readonly ComputedValues v = ref c.Values;
        ref Frame f = ref _frames[pi];
        double w = f.ContentWidth;
        bool rtl = (f.Flags & FrameFlags.Rtl) != 0;
        bool isFloat = outOfFlow;

        // CSS2 §10.3.5 and §10.3.9: a float or a shrink-to-fit box (inline-block) never has auto margins; they compute to 0.
        if (isFloat)
        {
            shrink = true;
        }

        double pl = Pad(v.PaddingLeft, w), pr = Pad(v.PaddingRight, w), pt = Pad(v.PaddingTop, w), pb = Pad(v.PaddingBottom, w);
        double bl = v.BorderLeftWidth, br = v.BorderRightWidth, bt = v.BorderTopWidth, bb = v.BorderBottomWidth;
        double ph = pl + pr + bl + br, pv = pt + pb + bt + bb;
        double forceW = double.NaN, forceH = double.NaN;
        if (parentIsFlex)
        {
            forceW = _fx[f.FlexIndex].ForceW;
            forceH = _fx[f.FlexIndex].ForceH;
        }
        double mt = MarginPx(v.MarginTop, w), mb = MarginPx(v.MarginBottom, w);
        double mlv = MarginPx(v.MarginLeft, w), mrv = MarginPx(v.MarginRight, w);
        bool mlAuto = v.MarginLeft.IsAuto && !shrink, mrAuto = v.MarginRight.IsAuto && !shrink;
        double mlFixed = mlAuto ? 0 : mlv, mrFixed = mrAuto ? 0 : mrv;
        double availContent = Math.Max(0, w - mlFixed - mrFixed - ph);

        bool replaced = c.Kind == BoxKind.Replaced;
        double replacedH = double.NaN, replacedW = 0;
        bool bfc;
        if (replaced)
        {
            bfc = true;
            ReplacedContentSize(c, in v, w, f.SpecH, ph, pv, availContent, out replacedW, out replacedH);
            if (_gridReplacedForce)
            {
                // A grid item's used size was settled by the grid algorithm.
                _gridReplacedForce = false;
                replacedW = _absRepW;
                replacedH = _absRepH;
            }
            else if (parentIsFlex)
            {
                ForceReplaced(forceW, forceH, ph, pv, ref replacedW, ref replacedH);
            }
        }
        else
        {
            bfc = EstablishesBfc(c, in v, parentIsRoot);
        }

        double borderW, ml, mr, contentW;
        if (!double.IsNaN(forceW))
        {
            // Flexbox §9.7: the flex algorithm has fixed the item's main or cross size; margins were resolved by the container.
            borderW = forceW;
            contentW = Math.Max(0, forceW - ph);
            ml = mlAuto ? 0 : mlv;
            mr = mrAuto ? 0 : mrv;
        }
        else
        {
            ResolveBoxWidth(c, in v, replaced, replacedW, w, w, ph, shrink, rtl, mlAuto, mrAuto, mlv, mrv, out borderW, out ml, out mr, out contentW);
        }

        if (forced && !isFloat)
        {
            // An absolutely positioned box whose size CSS2 §10.3.7 / §10.6.4 already solved.
            if (!replaced)
            {
                borderW = _absW;
                ml = _absMl;
                mr = _absMr;
                contentW = Math.Max(0, borderW - ph);
            }
        }

        double x = 0, y = 0;
        bool pending = false;
        bool clearedPending = false;
        double clearedEdge = double.NegativeInfinity;
        if (!isFloat)
        {
            // CSS2 §9.5.2: a box that clears floats. When its hypothetical position (with its top margin collapsed into the run of
            // adjoining margins) is above the bottom of the floats, or a float is adjoining it so that margin collapsing would pull the
            // float down with it, clearance is introduced: the margins before the box end there, and its border edge goes to the
            // bottom outer edge of the floats whatever its own margin is.
            bool clearing = false;
            double clearEdge = double.NegativeInfinity;
            if (v.Clear != ClearKind.None && !parentIsRoot && _bsp > 0)
            {
                bool certain = false;
                if (HasDeferred && DeferredClearedBy(v.Clear))
                {
                    certain = true;
                }
                else if (ClearEdge(in _bfcs[_bsp - 1], v.Clear) > double.NegativeInfinity)
                {
                    int j = SettledOf(pi);
                    certain = _frames[j].BfcTop + _frames[j].Cursor + CollapsedTopMargin(c, w, mt) < ClearEdge(in _bfcs[_bsp - 1], v.Clear);
                }

                if (certain)
                {
                    double before = ResolveStrut();
                    f = ref _frames[pi];
                    f.Cursor += before;
                    clearEdge = ClearEdge(in _bfcs[_bsp - 1], v.Clear);
                    clearing = true;
                }
            }

            if (!clearing && bfc && HasDeferred && CollapsedTopMargin(c, w, mt) >= _pos + _neg)
            {
                // A box that starts a formatting context is separated from the floats that adjoin it: a margin above it that
                // would pull them down does not; they are positioned first. A margin that pulls them up still counts.
                double before = ResolveStrut();
                f = ref _frames[pi];
                f.Cursor += before;
            }

            StrutAdd(mt);
            pending = !bfc && bt == 0 && pt == 0;
            clearedPending = clearing && pending;
            clearedEdge = clearEdge;
            x = f.ContentLeft + ml;
            if (!pending)
            {
                double off = ResolveStrut();
                f = ref _frames[pi];
                y = f.ContentTop + f.Cursor + off;
                if (clearing)
                {
                    y = Math.Max(f.ContentTop + f.Cursor, f.ContentTop + (clearEdge - f.BfcTop));
                }

                if (bfc && !parentIsRoot && _bsp > 0 && _floatCount > _bfcs[_bsp - 1].FloatBase)
                {
                    AvoidFloats(pi, c, in v, replaced, replacedW, ph, rtl, mlAuto, mrAuto, mlv, mrv, ref y, ref x, ref borderW, ref ml, ref mr, ref contentW);
                    f = ref _frames[pi];
                }
            }
        }

        double specH, minH, maxH, ratioH = double.NaN;
        if (replaced)
        {
            specH = replacedH;
            minH = 0;
            maxH = double.PositiveInfinity;
        }
        else
        {
            ResolveHeights(in v, f.SpecH, pv, mt, mb, borderW, contentW, out specH, out minH, out maxH, out ratioH);
            if (parentIsFlex && !double.IsNaN(forceH))
            {
                // A negative forced height asks for the content height: height, min-height and max-height are ignored (Flexbox §4.5 content size suggestion).
                if (forceH < 0)
                {
                    specH = double.NaN;
                    minH = 0;
                    maxH = double.PositiveInfinity;
                    ratioH = double.NaN;
                }
                else
                {
                    specH = Math.Max(0, forceH - pv);
                    minH = 0;
                    maxH = double.PositiveInfinity;
                    ratioH = double.NaN;
                }
            }

            if (forced && !double.IsNaN(_absH))
            {
                specH = _absH;
                minH = 0;
                maxH = double.PositiveInfinity;
                ratioH = double.NaN;
            }
        }

        RelativeOffset(in v, w, f.SpecH, rtl, out double relDx, out double relDy);
        if (replaced)
        {
            double bh = replacedH + pv;
            Fragment rf = NewFragment(preFragment, in f, c, new Rect(x, y, borderW, bh));
            _out.SetEdges(rf, new Insets(bt, br, bb, bl), new Insets(pt, pr, pb, pl));
            _out.SetBaseline(rf, double.NaN);
            if (isFloat)
            {
                FinishFloat(pi, rf.Index, borderW, bh, mt, mr, mb, ml, in v, relDx, relDy);
                return;
            }

            if (relDx != 0 || relDy != 0)
            {
                _out.SetBorderBox(rf, new Rect(x + relDx, y + relDy, borderW, bh));
            }

            Attach(pi, x, y, borderW, bh, mb, double.NaN, 0, 0, borderW, bh, false, false, relDx, relDy);
            RecordTop(pi, rf, mt, mr, mb, ml);
            return;
        }

        f = ref _frames[pi];
        Fragment frag = NewFragment(preFragment, in f, c, new Rect(x, y, borderW, 0));
        _out.SetEdges(frag, new Insets(bt, br, bb, bl), new Insets(pt, pr, pb, pl));
        FrameFlags flags = FrameFlags.None;
        if (pending)
        {
            flags |= FrameFlags.TopPending;
        }

        if (bfc)
        {
            flags |= FrameFlags.Bfc;
        }

        if (isFloat)
        {
            flags |= FrameFlags.Float;
        }

        if (clearedPending)
        {
            flags |= FrameFlags.Cleared;
        }

        if (v.Direction == DirectionKind.Rtl)
        {
            flags |= FrameFlags.Rtl;
        }

        bool scrollC = v.IsScrollContainer && c.Index != _vpSource;
        if (scrollC)
        {
            flags |= FrameFlags.ScrollContainer;
        }

        // CSS Overflow 3 §3: each axis that is not visible clips, and a clipped box adds only its own border box to its parent's overflow.
        if (c.Index != _vpSource)
        {
            if (v.OverflowX != OverflowKind.Visible)
            {
                flags |= FrameFlags.ClipX;
            }

            if (v.OverflowY != OverflowKind.Visible)
            {
                flags |= FrameFlags.ClipY;
            }
        }

        // The horizontal origin is known as soon as the box is opened; the vertical one only once its top margin is settled.
        double bfcLeft = 0, bfcTop = 0;
        if (!bfc)
        {
            bfcLeft = f.BfcLeft + (x - f.ContentLeft) + bl + pl;
            if (!pending)
            {
                bfcTop = f.BfcTop + (y - f.ContentTop) + bt + pt;
            }
        }

        int settled = pending ? SettledOf(pi) : 0;

        // A float lays out its content in a block formatting context of its own, away from the margin run of the flow it sits in.
        double savedPos = 0, savedNeg = 0;
        if (isFloat)
        {
            savedPos = _pos;
            savedNeg = _neg;
            _pos = _neg = 0;
        }

        // Percentage heights of children resolve against this box's height when it is specified (CSS2 §10.5).
        var nf = new Frame
        {
            Box = c.Index,
            Frag = frag.Index,
            NextChild = c.FirstChild.IsNull ? -1 : c.FirstChild.Index,
            Flags = flags,
            X = x,
            Y = y,
            W = borderW,
            ContentLeft = bl + pl,
            ContentTop = bt + pt,
            BottomEdge = bb + pb,
            ContentWidth = contentW,
            MarginTop = mt,
            MarginBottom = mb,
            MarginLeft = ml,
            MarginRight = mr,
            SpecH = specH,
            MinH = minH,
            MaxH = maxH,
            RatioH = ratioH,
            Cursor = 0,
            FirstBaseline = double.NaN,
            BfcLeft = bfcLeft,
            BfcTop = bfcTop,
            OvL = scrollC ? bl : 0,
            OvT = scrollC ? bt : 0,
            OvR = scrollC ? double.NegativeInfinity : borderW,
            OvB = scrollC ? double.NegativeInfinity : 0,
            CbFrag = v.Position != PositionKind.Static ? frag.Index : f.CbFrag,
            RelDx = relDx,
            RelDy = relDy,
            Settled = settled,
            SavedPos = savedPos,
            SavedNeg = savedNeg,
            ClearBfc = clearedEdge,
        };
        PushFrame(in nf);
        int ni = _sp - 1;
        if (bfc)
        {
            PushBfc();
        }

        if (c.Context == FormattingContext.Flex)
        {
            FlexOpen(ni, c);
            return;
        }

        if (c.Context == FormattingContext.Grid && !replaced && GridOpen(ni, c))
        {
            return;
        }

        if (c.ChildLayout == ChildLayout.Inline && c.FirstChild.Kind == BoxKind.Marker && (c.FirstChild.Flags & BoxTraits.OutsideMarker) != 0)
        {
            // The block loop never visits the children of a container with inline content, so the marker is placed here.
            PlaceOutsideMarker(ni, c.FirstChild);
        }

        if (c.ChildLayout == ChildLayout.Inline)
        {
            // The container has line boxes, so it has content: its margins stop collapsing through it.
            ResolveStrut();
            ref Frame t = ref _frames[ni];
            var req = new InlineLayoutRequest(c, contentW, t.ContentLeft, t.ContentTop, t.BfcLeft, t.BfcTop, _fonts, this);
            InlineLayoutResult r = _inline.Layout(in req, _out, new Fragment(_out, t.Frag), _token);
            t = ref _frames[ni];
            t.Cursor = r.Height;
            if (r.LineCount > 0)
            {
                t.Flags |= FrameFlags.HasContent;
                t.FirstBaseline = t.ContentTop + r.FirstBaseline;
            }

            t.NextChild = -1;
            t.Flags |= FrameFlags.AnyChild;
            QueueInlineAbsolutes(ni);
        }
    }

    /// <summary>
    /// CSS2 §10.3.3 (block-level, normal flow), §10.3.4 (replaced), §10.3.5 and §10.3.6 (floats, shrink-to-fit): the used border-box
    /// width and horizontal margins of a box. <paramref name="containing"/> is the containing block's width, the basis for
    /// percentages; <paramref name="fill"/> is the room an <c>auto</c> width fills, which is narrower than the containing block
    /// beside floats (CSS2 §9.5).
    /// </summary>
    private void ResolveBoxWidth(Box c, in ComputedValues v, bool replaced, double replacedWidth, double containing, double fill, double ph, bool shrink, bool rtl, bool mlAuto, bool mrAuto, double mlv, double mrv, out double borderW, out double ml, out double mr, out double contentW)
    {
        if (replaced)
        {
            contentW = replacedWidth;
            SolveInline(fill, ph, contentW, mlAuto, mrAuto, mlv, mrv, rtl, shrink, out borderW, out ml, out mr);
            return;
        }

        double mlFixed = mlAuto ? 0 : mlv, mrFixed = mrAuto ? 0 : mrv;
        double availContent = Math.Max(0, fill - mlFixed - mrFixed - ph);
        double cw = ResolveWidthLength(c, v.Width, containing, ph, v.BoxSizing, availContent, isMin: false);
        double minW = ResolveWidthLength(c, v.MinWidth, containing, ph, v.BoxSizing, availContent, isMin: true);
        double maxW = ResolveWidthLength(c, v.MaxWidth, containing, ph, v.BoxSizing, availContent, isMin: false);
        if (double.IsNaN(minW))
        {
            minW = 0;
        }

        if (double.IsNaN(maxW))
        {
            maxW = double.PositiveInfinity;
        }

        if (double.IsNaN(cw) && shrink && v.Width.Kind is LengthKind.Auto)
        {
            // CSS2 §10.3.5: shrink-to-fit is min(max(preferred minimum, available), preferred).
            GetIntrinsic(c, out double imin, out double imax);
            cw = Math.Min(imax, Math.Max(imin, availContent));
        }

        SolveInline(fill, ph, cw, mlAuto, mrAuto, mlv, mrv, rtl, shrink, out borderW, out ml, out mr);
        if (borderW - ph > maxW)
        {
            SolveInline(fill, ph, maxW, mlAuto, mrAuto, mlv, mrv, rtl, shrink, out borderW, out ml, out mr);
        }

        if (borderW - ph < minW)
        {
            SolveInline(fill, ph, minW, mlAuto, mrAuto, mlv, mrv, rtl, shrink, out borderW, out ml, out mr);
        }

        contentW = Math.Max(0, borderW - ph);
    }

    /// <summary>
    /// CSS2 §9.5: the border box of a block-level replaced box or of a box that establishes a block formatting context must not
    /// overlap the margin box of a float in the same block formatting context. The box goes beside the floats when it fits there
    /// (an <c>auto</c> width shrinks to the room between them, but not below its min-content width) and otherwise moves down to
    /// the next float edge. The room is measured at the box's top edge.
    /// </summary>
    private void AvoidFloats(int pi, Box c, in ComputedValues v, bool replaced, double replacedWidth, double ph, bool rtl, bool mlAuto, bool mrAuto, double mlv, double mrv, ref double y, ref double x, ref double borderW, ref double ml, ref double mr, ref double contentW)
    {
        ref Frame fr = ref _frames[pi];
        double containing = fr.ContentWidth;
        double cbL = fr.BfcLeft, cbR = cbL + containing;
        double mlFixed = mlAuto ? 0 : mlv, mrFixed = mrAuto ? 0 : mrv;
        double bandLeft = cbL;
        int limit = _floatCount + 2;
        for (int guard = 0; guard < limit; guard++)
        {
            ref BfcState s = ref _bfcs[_bsp - 1];
            double by = fr.BfcTop + (y - fr.ContentTop);
            BandAll(ref s, by, 0, cbL, cbR, out bandLeft, out double bandRight, out double next);
            bool intrudes = bandLeft != cbL || bandRight != cbR;
            double fill = intrudes ? Math.Max(0, bandRight - bandLeft) : containing;
            ResolveBoxWidth(c, in v, replaced, replacedWidth, containing, fill, ph, false, rtl, mlAuto, mrAuto, mlv, mrv, out borderW, out ml, out mr, out contentW);
            if (!intrudes || s.Capped)
            {
                break;
            }

            bool fits;
            if (replaced || v.Width.Kind != LengthKind.Auto)
            {
                // CSS2 §10.3.3: when the width is over-constrained the margin at the end of the line is dropped, so it does not count.
                fits = borderW + (rtl ? mrFixed + Math.Min(0, mlFixed) : mlFixed + Math.Min(0, mrFixed)) <= fill + FitEpsilon;
            }
            else
            {
                GetIntrinsic(c, out double imin, out _);
                fits = fill > 0 && contentW + FitEpsilon >= imin;
            }

            if (fits || double.IsPositiveInfinity(next))
            {
                break;
            }

            y += next - by;
        }

        x = fr.ContentLeft + (bandLeft - cbL) + ml;
    }

    private int SettledOf(int i) => (_frames[i].Flags & FrameFlags.TopPending) != 0 ? _frames[i].Settled : i;

    /// <summary>
    /// The sum of the run of adjoining margins if the top margin of <paramref name="c"/> joined it, together with the top margins
    /// of the chain of first children that adjoin it (CSS2 §8.3.1: no top border or padding, no formatting context). The chain
    /// is followed at most <see cref="MaxMarginChain"/> boxes deep.
    /// </summary>
    private double CollapsedTopMargin(Box c, double basis, double margin)
    {
        double p = _pos, n = _neg;
        for (int depth = 0; ; depth++)
        {
            if (margin > p)
            {
                p = margin;
            }
            else if (margin < n)
            {
                n = margin;
            }

            ref readonly ComputedValues v = ref c.Values;
            if (depth >= MaxMarginChain || c.ChildLayout != ChildLayout.Block || EstablishesBfc(c, in v, false) || v.BorderTopWidth != 0 || Pad(v.PaddingTop, basis) != 0)
            {
                break;
            }

            Box k = c.FirstChild;
            while (!k.IsNull && (k.Kind != BoxKind.Container && k.Kind != BoxKind.Replaced || (k.Flags & (BoxTraits.OutOfFlow | BoxTraits.OutsideMarker)) != 0))
            {
                k = k.NextSibling;
            }

            if (k.IsNull || k.Kind != BoxKind.Container || EstablishesBfc(k, in k.Values, false))
            {
                break;
            }

            c = k;
            margin = MarginPx(k.Values.MarginTop, basis);
        }

        return p + n;
    }

    private const int MaxMarginChain = 64;
}
