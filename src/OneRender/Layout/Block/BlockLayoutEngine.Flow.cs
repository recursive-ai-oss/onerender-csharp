using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    private static double Pad(Length l, double basis)
    {
        if (l.Kind is not (LengthKind.Px or LengthKind.Percent or LengthKind.Calc))
        {
            return 0;
        }

        double v = l.Resolve(basis);
        return double.IsNaN(v) || v < 0 ? 0 : v;
    }

    private static double MarginPx(Length l, double basis)
    {
        if (l.Kind is not (LengthKind.Px or LengthKind.Percent or LengthKind.Calc))
        {
            return 0;
        }

        double v = l.Resolve(basis);
        return double.IsNaN(v) ? 0 : v;
    }

    /// <summary>CSS2 §9.4.1 and CSS Display 3 §2.1: whether a box is the root of a new block formatting context.</summary>
    private bool EstablishesBfc(Box c, in ComputedValues v, bool parentIsRoot) =>
        parentIsRoot
        || c.Context != FormattingContext.Block
        || v.Floating != FloatKind.None
        || v.Position is PositionKind.Absolute or PositionKind.Fixed
        || (v.IsScrollContainer && c.Index != _vpSource);

    private Fragment FragmentAt(in Frame f) => f.Frag < 0 ? _rootParent : new Fragment(_out, f.Frag);

    private void RecordTop(int pi, Fragment frag, double mt, double mr, double mb, double ml)
    {
        if (_frames[pi].Box < 0)
        {
            _lastFragment = frag;
            _lastMargins = new Insets(mt, mr, mb, ml);
        }
    }

    /// <summary>CSS Lists 3 §3: a stand-in for an outside marker, one em wide and one line high, just outside the list item's start edge.</summary>
    private void PlaceOutsideMarker(int pi, Box marker)
    {
        ref Frame f = ref _frames[pi];
        if (f.Box < 0)
        {
            return;
        }

        ref readonly ComputedValues v = ref marker.Values;
        double size = v.FontSize;
        Fragment mf = _out.AddBox(new Fragment(_out, f.Frag), marker, new Rect(-size, f.ContentTop, size, PlaceholderInlineLayout.LineHeight(in v)));
        _ = mf;
    }

    /// <summary>
    /// Adds a finished in-flow child to its parent's running state: the cursor moves to the child's border-box bottom, its
    /// bottom margin joins the margin run, its overflow joins the parent's and its baseline may become the parent's first baseline.
    /// </summary>
    private void Attach(int pi, double x, double y, double w, double h, double mb, double baseline, double ovL, double ovT, double ovR, double ovB, bool clipX, bool clipY, double dx, double dy)
    {
        ref Frame p = ref _frames[pi];
        if ((p.Flags & FrameFlags.Flex) != 0)
        {
            FlexItemDone(pi, w, h, baseline, ovL, ovT, ovR, ovB, clipX, clipY, dx, dy);
            return;
        }

        p.Cursor = (y - p.ContentTop) + h;
        p.Flags |= FrameFlags.HasContent | FrameFlags.AnyChild;
        StrutAdd(mb);
        if (double.IsNaN(p.FirstBaseline) && !double.IsNaN(baseline))
        {
            p.FirstBaseline = y + baseline;
        }

        if (clipX)
        {
            ovL = 0;
            ovR = w;
        }

        if (clipY)
        {
            ovT = 0;
            ovB = h;
        }

        // A relatively positioned box overflows where it is drawn, not where it sat in the flow (Overflow 3 §2.2).
        p.OvL = Math.Min(p.OvL, x + dx + ovL);
        p.OvT = Math.Min(p.OvT, y + dy + ovT);
        p.OvR = Math.Max(p.OvR, x + dx + ovR);
        p.OvB = Math.Max(p.OvB, y + dy + ovB);
    }

    private void CloseTop()
    {
        int ti = _sp - 1;
        int pi = ti - 1;
        ref Frame t = ref _frames[ti];
        FrameFlags fl = t.Flags;
        bool emptyClearing = (fl & (FrameFlags.Cleared | FrameFlags.ClearsFloats)) != 0 && (fl & FrameFlags.Bfc) == 0
            && (double.IsNaN(t.SpecH) || (t.SpecH == 0 && (fl & FrameFlags.AnyChild) == 0))
            && t.BottomEdge == 0 && t.MinH == 0 && double.IsNaN(t.RatioH);

        if ((fl & FrameFlags.TopPending) != 0)
        {
            // CSS2 §8.3.1: a box with no height, padding, border or in-flow content has its top and bottom margins adjoining
            // ("collapsing through"), so they join the margin run and the box sits where a zero-height box with a bottom border would.
            // Not when the box has a `clear` that names floats of its formatting context: see the empty-clearing-box rule after Attach below.
            bool eligible = (double.IsNaN(t.SpecH) || (t.SpecH == 0 && (fl & FrameFlags.AnyChild) == 0))
                && (fl & (FrameFlags.Cleared | FrameFlags.ClearsFloats)) == 0 && t.BottomEdge == 0 && t.MinH == 0 && double.IsNaN(t.RatioH);
            if (eligible)
            {
                ref Frame pp = ref _frames[pi];
                double y = (pp.Flags & FrameFlags.TopPending) != 0 ? pp.ContentTop + pp.Cursor : pp.ContentTop + pp.Cursor + (_pos + _neg);
                t.Y = y;
                var fragment = new Fragment(_out, t.Frag);
                _out.SetBorderBox(fragment, new Rect(t.X + t.RelDx, y + t.RelDy, t.W, 0));
                _out.SetBaseline(fragment, double.NaN);
                _out.SetScrollableOverflow(fragment, new Rect(0, 0, t.W, 0));
                StrutAdd(t.MarginBottom);
                RecordTop(pi, fragment, t.MarginTop, t.MarginRight, t.MarginBottom, t.MarginLeft);
                if (HasDeferred && (pp.Flags & FrameFlags.TopPending) == 0)
                {
                    // Floats inside the box that collapsed through sit where the box sits.
                    FlushDeferred(pp.BfcTop + (y - pp.ContentTop));
                }

                _sp--;
                return;
            }

            ResolveStrut();
            t = ref _frames[ti];
        }

        // CSS2 §10.6.3: an auto height reaches the bottom margin edge of the last in-flow child, or its border edge when that
        // margin collapses with this box's own bottom margin (§8.3.1: no bottom padding or border, auto height, not a BFC root).
        double contentH;
        double cursor = t.Cursor;
        bool scroll = (fl & FrameFlags.ScrollContainer) != 0;
        if (!double.IsNaN(t.SpecH))
        {
            contentH = t.SpecH;
            _pos = _neg = 0;
        }
        else
        {
            bool collapsible = t.BottomEdge == 0 && (fl & FrameFlags.Bfc) == 0 && double.IsNaN(t.RatioH) && t.MinH <= cursor && cursor <= t.MaxH;
            if (collapsible)
            {
                contentH = cursor;
            }
            else
            {
                double h = cursor + _pos + _neg;
                _pos = _neg = 0;
                if ((fl & FrameFlags.Bfc) != 0)
                {
                    // CSS2 §10.6.7: the auto height of a block formatting context root reaches the bottom margin edge of its lowest float.
                    h = Math.Max(h, _bfcs[_bsp - 1].MaxBottom);
                }
                if (!double.IsNaN(t.RatioH))
                {
                    // CSS Sizing 4 §5.1: the automatic minimum size of a box with a preferred aspect ratio is its content size.
                    h = scroll ? t.RatioH : Math.Max(t.RatioH, h);
                }

                contentH = Math.Max(t.MinH, Math.Min(t.MaxH, h));
            }
        }

        double bh = t.ContentTop + contentH + t.BottomEdge;
        if ((fl & FrameFlags.Bfc) != 0)
        {
            // Float border boxes are scrollable overflow of the box that contains them; they are tracked per formatting context.
            ref BfcState s = ref _bfcs[_bsp - 1];
            if (s.OvR >= s.OvL)
            {
                t.OvL = Math.Min(t.OvL, s.OvL + t.ContentLeft);
                t.OvT = Math.Min(t.OvT, s.OvT + t.ContentTop);
                t.OvR = Math.Max(t.OvR, s.OvR + t.ContentLeft);
                t.OvB = Math.Max(t.OvB, s.OvB + t.ContentTop);
            }

            PopBfc();
        }

        var frag = new Fragment(_out, t.Frag);
        _out.SetBorderBox(frag, new Rect(t.X + t.RelDx, t.Y + t.RelDy, t.W, bh));
        _out.SetBaseline(frag, t.FirstBaseline);
        double ovB, ovR = t.OvR;
        if (scroll)
        {
            // Overflow 3 §2.2: a scroll container's scrollable overflow is its padding box plus its content, and content that
            // overflows at the end edge also brings the end padding into the scrollable area.
            Insets border = _out.BorderOf(t.Frag);
            Insets padding = _out.PaddingOf(t.Frag);
            if (ovR > t.W - border.Right - padding.Right)
            {
                ovR += padding.Right;
            }

            ovR = Math.Max(t.W - border.Right, ovR);
            ovB = t.OvB;
            if (ovB > bh - t.BottomEdge)
            {
                ovB += padding.Bottom;
            }

            ovB = Math.Max(bh - border.Bottom, ovB);
        }
        else
        {
            ovB = Math.Max(t.OvB, bh);
        }

        _out.SetScrollableOverflow(frag, new Rect(t.OvL, t.OvT, ovR - t.OvL, ovB - t.OvT));
        double x = t.X, y2 = t.Y, wd = t.W, mb = t.MarginBottom, baseline = t.FirstBaseline;
        double ovL = t.OvL, ovT = t.OvT;
        bool clipX = (fl & FrameFlags.ClipX) != 0, clipY = (fl & FrameFlags.ClipY) != 0;
        double rdx = t.RelDx, rdy = t.RelDy;
        if ((fl & FrameFlags.Float) != 0)
        {
            // A float takes no part in the flow: the margin run of the flow it sat in resumes, and it is positioned by CSS2 §9.5.1.
            _pos = t.SavedPos;
            _neg = t.SavedNeg;
            double fmt = t.MarginTop, fmr = t.MarginRight, fmb = t.MarginBottom, fml = t.MarginLeft;
            int fragIndex = t.Frag;
            Box floatBox = _boxes.Make(t.Box);
            _sp--;
            FinishFloat(pi, fragIndex, wd, bh, fmt, fmr, fmb, fml, in floatBox.Values, rdx, rdy);
            return;
        }

        double marginTop = t.MarginTop;
        RecordTop(pi, frag, t.MarginTop, t.MarginRight, t.MarginBottom, t.MarginLeft);
        Attach(pi, x, y2, wd, bh, mb, baseline, ovL, ovT, ovR, ovB, clipX, clipY, rdx, rdy);
        if (emptyClearing && _frames[pi].NextChild >= 0)
        {
            // An empty block with `clear` is placed at its hypothetical position or below the floats, as CSS2 §9.5.2 says, but the margins
            // after it are measured from its top margin edge: its margins collapse together and with the next box's, and the run starts
            // where the box's own top margin began (at y - margin-top), not at its border edge. Chrome's output has next = y - mt +
            // max(mt, mb, next margin-top) whether or not the floats reach it (WPT clear-clearance-calculation-003 needs this for the
            // case where they do not, and a clearing block between paragraphs shows the margin before it never merging with the one
            // after it). With margin-top 0 this is the usual "margin before + margin after". As the last child of its parent it keeps the
            // plain rule (the parent reaches its border edge; WPT margin-collapse-018 and -027).
            _frames[pi].Cursor -= Math.Max(0, marginTop);
            StrutAdd(marginTop);
        }

        _sp--;
    }

    /// <summary>CSS2 §10.5, §10.6.3, §10.7 and CSS Sizing 4 §5: the specified, minimum and maximum content heights and the height an aspect ratio asks for.</summary>
    private static void ResolveHeights(in ComputedValues v, double basis, double pv, double mt, double mb, double borderW, double contentW, out double specH, out double minH, out double maxH, out double ratioH)
    {
        bool definite = !double.IsNaN(basis);
        bool borderBox = v.BoxSizing == BoxSizingKind.BorderBox;
        double val = double.NaN;
        Length hl = v.Height;
        switch (hl.Kind)
        {
            case LengthKind.Px:
                val = hl.Value;
                break;
            case LengthKind.Percent or LengthKind.Calc:
                // CSS2 §10.5: a percentage height against a containing block whose height depends on content computes to auto.
                val = definite ? hl.Resolve(basis) : double.NaN;
                break;
            case LengthKind.Stretch:
                val = definite ? basis - mt - mb : double.NaN;
                if (!double.IsNaN(val) && !borderBox)
                {
                    val -= pv;
                }

                break;
        }

        if (!double.IsNaN(val))
        {
            if (borderBox && hl.Kind != LengthKind.Stretch)
            {
                val -= pv;
            }

            val = Math.Max(0, val);
        }

        minH = 0;
        Length mnl = v.MinHeight;
        if (mnl.Kind is LengthKind.Px || (mnl.Kind is LengthKind.Percent or LengthKind.Calc && definite))
        {
            minH = Math.Max(0, mnl.Resolve(basis) - (borderBox ? pv : 0));
        }

        maxH = double.PositiveInfinity;
        Length mxl = v.MaxHeight;
        if (mxl.Kind is LengthKind.Px || (mxl.Kind is LengthKind.Percent or LengthKind.Calc && definite))
        {
            maxH = Math.Max(0, mxl.Resolve(basis) - (borderBox ? pv : 0));
        }

        specH = double.IsNaN(val) ? double.NaN : Math.Max(minH, Math.Min(maxH, val));
        ratioH = double.NaN;
        double ratio = v.AspectRatio;
        if (double.IsNaN(specH) && ratio > 0)
        {
            // CSS Sizing 4 §5: with a definite inline size, an auto block size follows from the preferred aspect ratio of the box-sizing box.
            double r = borderBox ? borderW / ratio - pv : contentW / ratio;
            ratioH = Math.Max(minH, Math.Min(maxH, Math.Max(0, r)));
        }
    }

    /// <summary>
    /// CSS2 §10.3.3 (block-level, normal flow) and §10.3.4 (replaced): with a definite content width, solves the margins so the
    /// widths of the containing block add up. <paramref name="contentWidth"/> NaN means <c>width: auto</c>.
    /// </summary>
    private static void SolveInline(double containing, double ph, double contentWidth, bool mlAuto, bool mrAuto, double mlv, double mrv, bool rtl, bool exact, out double borderW, out double ml, out double mr)
    {
        if (double.IsNaN(contentWidth))
        {
            // auto width: auto margins are 0 and the box fills what the margins leave.
            ml = mlAuto ? 0 : mlv;
            mr = mrAuto ? 0 : mrv;
            borderW = Math.Max(ph, containing - ml - mr);
            return;
        }

        borderW = contentWidth + ph;
        if (exact)
        {
            // CSS2 §10.3.5 and §10.3.9: a shrink-to-fit box keeps the margins it was given; there is nothing to distribute.
            ml = mlv;
            mr = mrv;
            return;
        }

        double rem = containing - borderW;
        if (mlAuto && mrAuto)
        {
            if (rem < 0)
            {
                // Over-constrained with both margins auto: the start margin is 0 and the end margin takes the (negative) rest.
                if (rtl)
                {
                    mr = 0;
                    ml = rem;
                }
                else
                {
                    ml = 0;
                    mr = rem;
                }
            }
            else
            {
                ml = mr = rem / 2;
            }
        }
        else if (mlAuto)
        {
            ml = rem - mrv;
            mr = mrv;
        }
        else if (mrAuto)
        {
            mr = rem - mlv;
            ml = mlv;
        }
        else if (rtl)
        {
            // §10.3.3: when over-constrained, the margin at the end of the line is ignored: left for rtl.
            mr = mrv;
            ml = rem - mrv;
        }
        else
        {
            ml = mlv;
            mr = rem - mlv;
        }
    }

    /// <summary>
    /// A width, min-width or max-width as a content-box width in px, or NaN when it behaves as <c>auto</c> (or <c>none</c>).
    /// Intrinsic keywords come from CSS Sizing 3 §5.
    /// </summary>
    private double ResolveWidthLength(Box c, Length l, double containing, double ph, BoxSizingKind sizing, double available, bool isMin)
    {
        _ = isMin;
        double v;
        switch (l.Kind)
        {
            case LengthKind.Px:
            case LengthKind.Percent:
            case LengthKind.Calc:
                v = l.Resolve(containing);
                if (double.IsNaN(v))
                {
                    return double.NaN;
                }

                return Math.Max(0, sizing == BoxSizingKind.BorderBox ? v - ph : v);
            case LengthKind.MinContent:
                GetIntrinsic(c, out v, out _);
                return v;
            case LengthKind.MaxContent:
                GetIntrinsic(c, out _, out v);
                return v;
            case LengthKind.FitContent:
                GetIntrinsic(c, out double a, out double b);
                return Math.Min(b, Math.Max(a, available));
            case LengthKind.FitContentPx:
            case LengthKind.FitContentPercent:
                GetIntrinsic(c, out double a2, out double b2);
                double arg = l.Kind == LengthKind.FitContentPx ? l.Value : l.Value / 100 * containing;
                if (sizing == BoxSizingKind.BorderBox)
                {
                    arg -= ph;
                }

                return Math.Min(b2, Math.Max(a2, Math.Max(0, arg)));
            case LengthKind.Stretch:
                return available;
            default:
                return double.NaN;
        }
    }

    /// <summary>
    /// CSS2 §10.3.2 and §10.6.2 with the constraint table of §10.4, and CSS Sizing 4 §5 for the preferred aspect ratio: the used
    /// content size of a replaced box. <paramref name="containing"/> may be NaN when only intrinsic sizes are wanted.
    /// </summary>
    private static void ReplacedContentSize(Box c, in ComputedValues v, double containing, double basisH, double ph, double pv, double available, out double width, out double height)
    {
        StyledNode node = c.Node;
        double nw = node.NaturalWidth, nh = node.NaturalHeight;
        if (nw <= 0 || double.IsInfinity(nw))
        {
            nw = double.NaN;
        }

        if (nh <= 0 || double.IsInfinity(nh))
        {
            nh = double.NaN;
        }

        double ratio = 0;
        if (v.AspectRatio > 0 && !(v.AspectRatioAuto && !double.IsNaN(nw) && !double.IsNaN(nh)))
        {
            ratio = v.AspectRatio;
        }
        else if (!double.IsNaN(nw) && !double.IsNaN(nh))
        {
            ratio = nw / nh;
        }

        bool borderBox = v.BoxSizing == BoxSizingKind.BorderBox;
        double sw = ReplacedLength(v.Width, containing, ph, borderBox, available);
        double sh = ReplacedLengthH(v.Height, basisH, pv, borderBox);
        double minW = ReplacedLength(v.MinWidth, containing, ph, borderBox, available);
        double maxW = ReplacedLength(v.MaxWidth, containing, ph, borderBox, available);
        double minH = ReplacedLengthH(v.MinHeight, basisH, pv, borderBox);
        double maxH = ReplacedLengthH(v.MaxHeight, basisH, pv, borderBox);
        if (double.IsNaN(minW))
        {
            minW = 0;
        }

        if (double.IsNaN(minH))
        {
            minH = 0;
        }

        if (double.IsNaN(maxW))
        {
            maxW = double.PositiveInfinity;
        }

        if (double.IsNaN(maxH))
        {
            maxH = double.PositiveInfinity;
        }

        double w, h;
        if (!double.IsNaN(sw) && !double.IsNaN(sh))
        {
            width = Math.Max(minW, Math.Min(maxW, sw));
            height = Math.Max(minH, Math.Min(maxH, sh));
            return;
        }

        if (!double.IsNaN(sw))
        {
            w = Math.Max(minW, Math.Min(maxW, sw));
            h = ratio > 0 ? w / ratio : (double.IsNaN(nh) ? 150 : nh);
            width = w;
            height = Math.Max(minH, Math.Min(maxH, h));
            return;
        }

        if (!double.IsNaN(sh))
        {
            h = Math.Max(minH, Math.Min(maxH, sh));
            w = ratio > 0 ? h * ratio : (double.IsNaN(nw) ? 300 : nw);
            height = h;
            width = Math.Max(minW, Math.Min(maxW, w));
            return;
        }

        // Neither is specified: natural size, else ratio, else the 300 x 150 default (CSS2 §10.3.2).
        if (!double.IsNaN(nw) && !double.IsNaN(nh))
        {
            w = nw;
            h = nh;
        }
        else if (!double.IsNaN(nw))
        {
            w = nw;
            h = ratio > 0 ? nw / ratio : 150;
        }
        else if (!double.IsNaN(nh))
        {
            h = nh;
            w = ratio > 0 ? nh * ratio : 300;
        }
        else if (ratio > 0)
        {
            w = double.IsNaN(available) ? 0 : available;
            h = w / ratio;
        }
        else
        {
            w = 300;
            h = 150;
        }

        // CSS2 §10.4: the nine cases of the constraint table.
        if (w > maxW && h > maxH)
        {
            if (maxW / w <= maxH / h)
            {
                width = maxW;
                height = Math.Max(minH, maxW * h / w);
            }
            else
            {
                width = Math.Max(minW, maxH * w / h);
                height = maxH;
            }
        }
        else if (w < minW && h < minH)
        {
            if (minW / w <= minH / h)
            {
                width = Math.Min(maxW, minH * w / h);
                height = minH;
            }
            else
            {
                width = minW;
                height = Math.Min(maxH, minW * h / w);
            }
        }
        else if (w < minW && h > maxH)
        {
            width = minW;
            height = maxH;
        }
        else if (w > maxW && h < minH)
        {
            width = maxW;
            height = minH;
        }
        else if (w > maxW)
        {
            width = maxW;
            height = Math.Max(maxW * h / w, minH);
        }
        else if (w < minW)
        {
            width = minW;
            height = Math.Min(minW * h / w, maxH);
        }
        else if (h > maxH)
        {
            width = Math.Max(maxH * w / h, minW);
            height = maxH;
        }
        else if (h < minH)
        {
            width = Math.Min(minH * w / h, maxW);
            height = minH;
        }
        else
        {
            width = w;
            height = h;
        }
    }

    private static double ReplacedLength(Length l, double containing, double ph, bool borderBox, double available)
    {
        switch (l.Kind)
        {
            case LengthKind.Px:
            case LengthKind.Percent:
            case LengthKind.Calc:
                double v = l.Resolve(containing);
                return double.IsNaN(v) ? double.NaN : Math.Max(0, borderBox ? v - ph : v);
            case LengthKind.Stretch:
                return available;
            default:
                return double.NaN;
        }
    }

    private static double ReplacedLengthH(Length l, double basis, double pv, bool borderBox)
    {
        if (l.Kind == LengthKind.Px || (l.Kind is LengthKind.Percent or LengthKind.Calc && !double.IsNaN(basis)))
        {
            double v = l.Resolve(basis);
            return Math.Max(0, borderBox ? v - pv : v);
        }

        return double.NaN;
    }
}
