using System.Threading;
using OneFont;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Tests.Support;

/// <summary>
/// A test-only layout that places positioned boxes and floats and draws unwrapped text, so paint order can be tested while the
/// real layouts for those are built. A deliberately small layout: block-level boxes stack top to bottom with their margins, borders, padding and specified sizes,
/// inline content becomes one unwrapped line per run, and positioned boxes go where <c>top</c>/<c>left</c>/<c>right</c>/<c>bottom</c>
/// in pixels say. It exists so the paint, image and harness plumbing can be exercised end to end; the real block, inline, float and
/// positioning layouts replace it behind <see cref="ILayoutEngine"/>.
/// </summary>
/// <remarks>
/// Simplifications, all on purpose: only adjacent block siblings collapse margins; floats are placed at the current position
/// and take no space; text is not wrapped, shaped or kerned (one glyph per code point, advances from the font); percentage heights
/// and <c>auto</c> offsets of absolutely positioned boxes use the static position. Nothing recurses on tree depth.
/// </remarks>
internal sealed class StackingLayout : ILayoutEngine
{
    private struct Frame
    {
        public Box Box;
        public Fragment Fragment;
        public Box NextChild;
        public double AbsX;
        public double AbsY;
        public double ContentLeft;
        public double ContentTop;
        public double ContentWidth;
        public double Cursor;
        public double PrevMarginBottom;
        public double MarginTop;
        public double MarginBottom;
        public double SpecifiedHeight;
        public bool Positioned;
        public double BorderBoxWidth;
        public double BottomExtra;
        public bool TopOpen;
    }

    private struct Item
    {
        public Fragment Fragment;
        public int Parent;
        public double X;
        public double Width;
        public double Ascent;
        public double Descent;
        public bool IsLeaf;
    }

    private Fragment _line;
    private Frame[] _frames = new Frame[32];
    private Item[] _items = new Item[32];
    private int _itemCount;
    private int[] _open = new int[16];
    private int[] _glyphs = new int[64];
    private double[] _xs = new double[64];
    private double[] _ys = new double[64];
    private int[] _clusters = new int[64];

    /// <inheritdoc />
    public void Layout(BoxTree boxes, in LayoutInput input, FragmentTree output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        ArgumentNullException.ThrowIfNull(output);
        if (!ReferenceEquals(output.Boxes, boxes))
        {
            throw new ArgumentException("The fragment tree belongs to another box tree.", nameof(output));
        }

        output.Clear();
        if (boxes.Root.IsNull)
        {
            return;
        }

        IFontProvider fonts = input.Fonts;
        double viewportW = input.AvailableWidth;
        double viewportH = double.IsNaN(input.AvailableHeight) ? 0 : input.AvailableHeight;
        int sp = 0;

        // The root box fills the initial containing block's width.
        Box root = boxes.Root;
        Fragment rootFrag = output.AddBox(default, root, new Rect(0, 0, viewportW, 0));
        sp = Push(sp, root, rootFrag, 0, 0, root.Values, viewportW, viewportW);
        int steps = 0;
        double rootHeight = 0;
        while (sp > 0)
        {
            if ((++steps & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int top = sp - 1;
            Box child = _frames[top].NextChild;
            if (child.IsNull)
            {
                double h = Finish(output, top, out double outer);
                sp--;
                if (sp == 0)
                {
                    rootHeight = h;
                    break;
                }

                // The parent advances past this child unless the child left the flow.
                if (!IsOutOfFlow(_frames[top].Box.Values))
                {
                    ref Frame parent = ref _frames[sp - 1];
                    parent.PrevMarginBottom = _frames[top].MarginBottom;
                    parent.Cursor += outer;
                }

                continue;
            }

            _frames[top].NextChild = child.NextSibling;
            switch (child.Kind)
            {
                case BoxKind.Container when child.Level == BoxLevel.Block || IsOutOfFlow(child.Values):
                case BoxKind.Replaced when child.Level == BoxLevel.Block || IsOutOfFlow(child.Values):
                    sp = EnterChild(output, sp, child);
                    break;
                case BoxKind.Marker:
                    break;
                default:
                    // A run of inline-level children (text, inline boxes, atomic inlines).
                    Box next = LayOutInlineRun(output, top, child, fonts);
                    _frames[top].NextChild = next;
                    break;
            }
        }

        output.SetScrollableOverflow(rootFrag, new Rect(0, 0, viewportW, Math.Max(rootHeight, viewportH)));
    }

    private static bool IsOutOfFlow(in ComputedValues v) =>
        v.Floating != FloatKind.None || v.Position is PositionKind.Absolute or PositionKind.Fixed;

    private int Push(int sp, Box box, Fragment fragment, double absX, double absY, in ComputedValues v, double borderBoxWidth, double availableWidth)
    {
        if (sp == _frames.Length)
        {
            Array.Resize(ref _frames, sp * 2);
        }

        double bl = Border(v.BorderLeftWidth, v.BorderLeftStyle);
        double bt = Border(v.BorderTopWidth, v.BorderTopStyle);
        double pl = Pad(v.PaddingLeft, availableWidth);
        double pr = Pad(v.PaddingRight, availableWidth);
        double br = Border(v.BorderRightWidth, v.BorderRightStyle);
        double pt = Pad(v.PaddingTop, availableWidth);
        _frames[sp] = new Frame
        {
            Box = box,
            Fragment = fragment,
            NextChild = box.FirstChild,
            AbsX = absX,
            AbsY = absY,
            ContentLeft = bl + pl,
            ContentTop = bt + pt,
            ContentWidth = Math.Max(0, borderBoxWidth - bl - br - pl - pr),
            BorderBoxWidth = borderBoxWidth,
            BottomExtra = Border(v.BorderBottomWidth, v.BorderBottomStyle) + Pad(v.PaddingBottom, availableWidth),
        };
        return sp + 1;
    }

    private static double Border(double width, BorderStyleKind style) => style is BorderStyleKind.None or BorderStyleKind.Hidden || !(width > 0) ? 0 : width;

    private static double Pad(Length l, double basis) => Math.Max(0, l.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc ? l.Resolve(basis) : 0);

    private static double Margin(Length l, double basis) => l.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc ? l.Resolve(basis) : 0;

    private static bool Definite(Length l) => l.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc;

    // CSS2 §8.3.1: adjoining margins combine to the largest positive plus the most negative.
    private static double Collapse(double a, double b) =>
        a >= 0 && b >= 0 ? Math.Max(a, b) : a < 0 && b < 0 ? Math.Min(a, b) : a + b;

    private int EnterChild(FragmentTree output, int sp, Box child)
    {
        ref Frame parent = ref _frames[sp - 1];
        ref readonly ComputedValues v = ref child.Values;
        double cw = parent.ContentWidth;
        bool outOfFlow = IsOutOfFlow(v);
        bool absolute = v.Position is PositionKind.Absolute or PositionKind.Fixed;

        // The block that absolute boxes are placed against: the nearest positioned ancestor's padding box, else the root's.
        int cb = 0;
        if (v.Position == PositionKind.Fixed)
        {
            cb = 0;
        }
        else if (absolute)
        {
            for (int k = sp - 1; k > 0; k--)
            {
                if (_frames[k].Positioned)
                {
                    cb = k;
                    break;
                }
            }
        }

        double basis = absolute ? _frames[cb].BorderBoxWidth : cw;
        double ml = Margin(v.MarginLeft, cw), mr = Margin(v.MarginRight, cw);
        double mt = Margin(v.MarginTop, cw), mb = Margin(v.MarginBottom, cw);
        double bl = Border(v.BorderLeftWidth, v.BorderLeftStyle), br = Border(v.BorderRightWidth, v.BorderRightStyle);
        double bt = Border(v.BorderTopWidth, v.BorderTopStyle), bb = Border(v.BorderBottomWidth, v.BorderBottomStyle);
        double pl = Pad(v.PaddingLeft, cw), pr = Pad(v.PaddingRight, cw);
        double pt = Pad(v.PaddingTop, cw), pb = Pad(v.PaddingBottom, cw);
        double extraW = bl + br + pl + pr;
        double extraH = bt + bb + pt + pb;

        double width;
        bool autoWidth = !Definite(v.Width);
        if (!autoWidth)
        {
            double spec = v.Width.Resolve(basis);
            width = v.BoxSizing == BoxSizingKind.BorderBox ? spec : spec + extraW;
        }
        else if (child.Kind == BoxKind.Replaced)
        {
            width = child.Node.NaturalWidth + extraW;
        }
        else if (absolute && Definite(v.Left) && Definite(v.Right))
        {
            width = basis - v.Left.Resolve(basis) - v.Right.Resolve(basis) - ml - mr;
        }
        else
        {
            width = cw - ml - mr;
        }

        if (Definite(v.MaxWidth))
        {
            double max = v.MaxWidth.Resolve(basis);
            width = Math.Min(width, v.BoxSizing == BoxSizingKind.BorderBox ? max : max + extraW);
        }

        if (Definite(v.MinWidth))
        {
            double min = v.MinWidth.Resolve(basis);
            width = Math.Max(width, v.BoxSizing == BoxSizingKind.BorderBox ? min : min + extraW);
        }

        width = Math.Max(width, extraW);

        // CSS2 §10.3.3: auto side margins absorb the leftover space when the width is specified.
        if (!autoWidth && !outOfFlow)
        {
            double free = cw - width - (v.MarginLeft.IsAuto ? 0 : ml) - (v.MarginRight.IsAuto ? 0 : mr);
            if (v.MarginLeft.IsAuto && v.MarginRight.IsAuto)
            {
                ml = mr = Math.Max(0, free / 2);
            }
            else if (v.MarginLeft.IsAuto)
            {
                ml = Math.Max(0, free);
            }
            else if (v.MarginRight.IsAuto)
            {
                mr = Math.Max(0, free);
            }
        }

        double specifiedHeight = -1;
        if (v.Height.Kind is LengthKind.Px)
        {
            specifiedHeight = Math.Max(0, v.BoxSizing == BoxSizingKind.BorderBox ? v.Height.Value - extraH : v.Height.Value);
        }
        else if (child.Kind == BoxKind.Replaced)
        {
            specifiedHeight = child.Node.NaturalHeight;
        }

        double x;
        double y;
        double collapsedTop = mt;
        if (absolute)
        {
            ref Frame cbf = ref _frames[cb];
            double cbX = cbf.AbsX + (cb == 0 ? 0 : Border(cbf.Box.Values.BorderLeftWidth, cbf.Box.Values.BorderLeftStyle));
            double cbY = cbf.AbsY + (cb == 0 ? 0 : Border(cbf.Box.Values.BorderTopWidth, cbf.Box.Values.BorderTopStyle));
            double absX;
            if (Definite(v.Left))
            {
                absX = cbX + v.Left.Resolve(basis) + ml;
            }
            else if (Definite(v.Right))
            {
                absX = cbX + basis - v.Right.Resolve(basis) - mr - width;
            }
            else
            {
                absX = parent.AbsX + parent.ContentLeft + ml;
            }

            double absY = Definite(v.Top) ? cbY + v.Top.Resolve(_frames[cb].BorderBoxWidth) + mt : parent.AbsY + parent.ContentTop + parent.Cursor + mt;
            x = absX - parent.AbsX;
            y = absY - parent.AbsY;
        }
        else if (v.Floating == FloatKind.Right)
        {
            x = parent.ContentLeft + cw - width - mr;
            y = parent.ContentTop + parent.Cursor + mt;
        }
        else
        {
            x = parent.ContentLeft + ml;
            if (!outOfFlow)
            {
                // Adjacent sibling margins collapse; the parent's cursor already holds the previous bottom margin's space.
                collapsedTop = Collapse(parent.PrevMarginBottom, mt);
                parent.Cursor -= parent.PrevMarginBottom;
                parent.PrevMarginBottom = 0;
                collapsedTop = CollapseThroughParents(output, sp, collapsedTop);
            }

            y = parent.ContentTop + parent.Cursor + collapsedTop;
        }

        Fragment frag = output.AddBox(parent.Fragment, child, new Rect(x, y, width, 0));
        output.SetEdges(frag, new Insets(bt, br, bb, bl), new Insets(pt, pr, pb, pl));
        double absLeft = parent.AbsX + x;
        double absTop = parent.AbsY + y;
        int next = Push(sp, child, frag, absLeft, absTop, in v, width, cw);
        ref Frame f = ref _frames[next - 1];
        f.Positioned = v.Position != PositionKind.Static;
        f.TopOpen = !outOfFlow && bt + pt == 0 && v.OverflowX == OverflowKind.Visible && v.OverflowY == OverflowKind.Visible;
        f.SpecifiedHeight = specifiedHeight;
        f.MarginTop = collapsedTop;
        f.MarginBottom = mb;
        // `outer` for the parent's cursor is computed in Finish: top margin as used, height, bottom margin.
        return next;
    }

    // CSS2 8.3.1: the top margin of a first in-flow child collapses with its parent's when nothing separates them (no border,
    // padding or earlier content, and the parent does not start a new formatting context). The chain of such parents shares one
    // margin, which belongs to the outermost of them; that box moves to where the merged margin puts it.
    private double CollapseThroughParents(FragmentTree output, int sp, double childTop)
    {
        int t = sp;
        while (t - 1 > 0 && _frames[t - 1].TopOpen && _frames[t - 1].Cursor == 0 && _frames[t - 1].PrevMarginBottom == 0)
        {
            t--;
        }

        if (t == sp)
        {
            return childTop;
        }

        ref Frame outer = ref _frames[t];
        double merged = Collapse(outer.MarginTop, childTop);
        double delta = merged - outer.MarginTop;
        if (delta != 0)
        {
            Rect r = outer.Fragment.BorderBox;
            output.SetBorderBox(outer.Fragment, new Rect(r.X, r.Y + delta, r.Width, r.Height));
            for (int k = t; k < sp; k++)
            {
                _frames[k].AbsY += delta;
            }
        }

        outer.MarginTop = merged;
        for (int k = t + 1; k < sp; k++)
        {
            _frames[k].MarginTop = 0;
        }

        return 0;
    }

    // Closes a frame: sets its height and returns the vertical space it takes in its parent (used top margin + border-box height +
    // bottom margin).
    private double Finish(FragmentTree output, int top, out double outer)
    {
        ref Frame f = ref _frames[top];
        ref readonly ComputedValues v = ref f.Box.Values;
        double bt = f.ContentTop;
        double bb = f.BottomExtra;
        double content = f.SpecifiedHeight >= 0 ? f.SpecifiedHeight : Math.Max(0, f.Cursor - f.PrevMarginBottom);
        if (Definite(v.MinHeight) && v.MinHeight.Kind == LengthKind.Px)
        {
            content = Math.Max(content, v.MinHeight.Value);
        }

        double height = bt + content + bb;
        Rect r = f.Fragment.BorderBox;
        double x = r.X;
        double y = r.Y;
        if (v.Position == PositionKind.Relative)
        {
            // CSS2 §9.4.3: a relative offset moves the box after layout without affecting siblings.
            double basis = f.BorderBoxWidth;
            if (Definite(v.Left))
            {
                x += v.Left.Resolve(basis);
            }
            else if (Definite(v.Right))
            {
                x -= v.Right.Resolve(basis);
            }

            if (Definite(v.Top))
            {
                y += v.Top.Resolve(basis);
            }
            else if (Definite(v.Bottom))
            {
                y -= v.Bottom.Resolve(basis);
            }
        }

        output.SetBorderBox(f.Fragment, new Rect(x, y, f.BorderBoxWidth, height));
        output.SetScrollableOverflow(f.Fragment, new Rect(0, 0, f.BorderBoxWidth, height));
        outer = f.MarginTop + height + f.MarginBottom;
        return height;
    }

    // ------------------------------------------------------------------ inline runs

    // Lays out the run of inline-level children that starts at `first` as one line (no wrapping) and returns the first box after it.
    private Box LayOutInlineRun(FragmentTree output, int top, Box first, IFontProvider fonts)
    {
        ref Frame frame = ref _frames[top];
        double cw = frame.ContentWidth;
        _itemCount = 0;
        int openCount = 0;
        double x = 0;
        bool pendingSpace = false;
        bool atStart = true;
        double maxAscent = 0;
        double maxDescent = 0;
        _line = output.AddLine(frame.Fragment, new Rect(frame.ContentLeft, frame.ContentTop + frame.Cursor, cw, 0), 0);

        Box cur = first;
        Box stop = default;
        while (!cur.IsNull)
        {
            bool blockLike = cur.Kind == BoxKind.Marker || (cur.Kind is BoxKind.Container or BoxKind.Replaced && (cur.Level == BoxLevel.Block || IsOutOfFlow(cur.Values)));
            if (openCount == 0 && blockLike)
            {
                stop = cur;
                break;
            }

            int parentItem = openCount > 0 ? _open[openCount - 1] : -1;
            Fragment parentFrag = parentItem >= 0 ? _items[parentItem].Fragment : _line;
            bool descend = false;
            switch (cur.Kind)
            {
                case BoxKind.Text:
                    x = AddText(output, cur, fonts, parentItem, parentFrag, x, ref pendingSpace, ref atStart, ref maxAscent, ref maxDescent);
                    break;
                case BoxKind.Inline:
                    {
                        ref readonly ComputedValues v = ref cur.Values;
                        Face(fonts, in v, out double asc, out double desc, out double lineHeight);
                        Fragment f = output.AddBox(parentFrag, cur, new Rect(0, 0, 0, 0));
                        output.SetEdges(f, EdgesOf(in v), default);
                        double start = x + Margin(v.MarginLeft, cw);
                        x = start + Border(v.BorderLeftWidth, v.BorderLeftStyle) + Pad(v.PaddingLeft, cw);
                        AddItem(f, parentItem, start, 0, asc, desc, false);
                        PushOpen(ref openCount, _itemCount - 1);
                        double leading = (lineHeight - (asc + desc)) / 2;
                        maxAscent = Math.Max(maxAscent, asc + leading);
                        maxDescent = Math.Max(maxDescent, desc + leading);
                        descend = !cur.FirstChild.IsNull;
                        if (!descend)
                        {
                            CloseInline(cur, ref openCount, ref x, cw);
                        }

                        break;
                    }

                default:
                    {
                        // Atomic inline: reserve the specified (or natural) size on the baseline; its content is not laid out here.
                        ref readonly ComputedValues v = ref cur.Values;
                        double w = Definite(v.Width) ? v.Width.Resolve(cw) : (cur.Kind == BoxKind.Replaced ? cur.Node.NaturalWidth : 0);
                        double h = v.Height.Kind == LengthKind.Px ? v.Height.Value : (cur.Kind == BoxKind.Replaced ? cur.Node.NaturalHeight : 0);
                        Fragment f = output.AddBox(parentFrag, cur, new Rect(0, 0, w, h));
                        output.SetEdges(f, EdgesOf(in v), default);
                        double ml = Margin(v.MarginLeft, cw);
                        AddItem(f, parentItem, x + ml, w, h, 0, true);
                        maxAscent = Math.Max(maxAscent, h);
                        x += ml + w + Margin(v.MarginRight, cw);
                        atStart = false;
                        pendingSpace = false;
                        break;
                    }
            }

            if (descend)
            {
                cur = cur.FirstChild;
                continue;
            }

            // Next box in tree order, closing the inline boxes the walk leaves.
            while (true)
            {
                if (!cur.NextSibling.IsNull)
                {
                    cur = cur.NextSibling;
                    break;
                }

                if (openCount == 0)
                {
                    cur = default;
                    break;
                }

                cur = cur.Parent;
                CloseInline(cur, ref openCount, ref x, cw);
            }
        }

        while (openCount > 0)
        {
            CloseInline(_items[_open[openCount - 1]].Fragment.Box, ref openCount, ref x, cw);
        }

        FinishLine(output, top, x, maxAscent, maxDescent);
        return stop;
    }

    private static Insets EdgesOf(in ComputedValues v) => new(
        Border(v.BorderTopWidth, v.BorderTopStyle),
        Border(v.BorderRightWidth, v.BorderRightStyle),
        Border(v.BorderBottomWidth, v.BorderBottomStyle),
        Border(v.BorderLeftWidth, v.BorderLeftStyle));

    private void PushOpen(ref int openCount, int item)
    {
        if (openCount == _open.Length)
        {
            Array.Resize(ref _open, openCount * 2);
        }

        _open[openCount++] = item;
    }

    private void CloseInline(Box box, ref int openCount, ref double x, double cw)
    {
        ref readonly ComputedValues v = ref box.Values;
        int idx = _open[--openCount];
        x += Border(v.BorderRightWidth, v.BorderRightStyle) + Pad(v.PaddingRight, cw);
        _items[idx].Width = x - _items[idx].X;
        x += Margin(v.MarginRight, cw);
    }

    private void AddItem(Fragment f, int parent, double x, double width, double ascent, double descent, bool leaf)
    {
        if (_itemCount == _items.Length)
        {
            Array.Resize(ref _items, _itemCount * 2);
        }

        _items[_itemCount++] = new Item { Fragment = f, Parent = parent, X = x, Width = width, Ascent = ascent, Descent = descent, IsLeaf = leaf };
    }

    private static void Face(IFontProvider fonts, in ComputedValues v, out double ascent, out double descent, out double lineHeight)
    {
        FontFace face = fonts.GetFallbackChain(in v).Faces[0];
        FontMetrics m = face.Metrics;
        double scale = v.FontSize / m.UnitsPerEm;
        ascent = m.Ascender * scale;
        descent = -m.Descender * scale;
        lineHeight = v.LineHeight.Kind switch
        {
            LengthKind.Px => v.LineHeight.Value,
            LengthKind.Number => v.LineHeight.Value * v.FontSize,
            _ => m.LineHeight * scale,
        };
    }

    private double AddText(FragmentTree output, Box textBox, IFontProvider fonts, int parentItem, Fragment parentFrag, double x, ref bool pendingSpace, ref bool atStart, ref double maxAscent, ref double maxDescent)
    {
        ref readonly ComputedValues v = ref textBox.Values;
        FontFace face = fonts.GetFallbackChain(in v).Faces[0];
        Face(fonts, in v, out double asc, out double desc, out double lineHeight);
        double scale = v.FontSize / face.Metrics.UnitsPerEm;
        bool collapse = v.WhiteSpaceCollapse is WhiteSpaceCollapseKind.Collapse;
        ReadOnlySpan<char> text = textBox.Text;

        int need = (text.Length * 2) + 2;
        if (_glyphs.Length < need)
        {
            _glyphs = new int[need];
            _xs = new double[need];
            _ys = new double[need];
            _clusters = new int[need];
        }

        // CSS Text 3 4.1.1: collapsible white space becomes one space, and none at the start of a line.
        int count = 0;
        double penX = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (collapse && ch is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                pendingSpace = true;
                continue;
            }

            if (!collapse && ch is '\n' or '\r')
            {
                continue;
            }

            if (pendingSpace)
            {
                pendingSpace = false;
                if (!atStart)
                {
                    int sg = face.GetGlyphIndex(' ');
                    _glyphs[count] = sg;
                    _xs[count] = penX;
                    _ys[count] = 0;
                    _clusters[count] = Math.Max(0, i - 1);
                    count++;
                    penX += face.GetAdvance(sg) * scale;
                }
            }

            int cp = ch;
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(ch, text[i + 1]);
            }

            int g = face.GetGlyphIndex(cp);
            _glyphs[count] = g;
            _xs[count] = penX;
            _ys[count] = 0;
            _clusters[count] = i;
            count++;
            penX += face.GetAdvance(g) * scale;
            atStart = false;
            if (cp > 0xFFFF)
            {
                i++;
            }
        }

        if (count == 0)
        {
            return x;
        }

        GlyphRunId run = output.AddGlyphRun(face, v.FontSize, 0, text.Length, false, _glyphs.AsSpan(0, count), _xs.AsSpan(0, count), _ys.AsSpan(0, count), _clusters.AsSpan(0, count));
        Fragment tf = output.AddText(parentFrag, textBox, 0, text.Length, new Rect(0, 0, penX, asc + desc), run);
        AddItem(tf, parentItem, x, penX, asc, desc, true);

        // CSS2 10.8.1: half-leading is added above and below the glyphs' ascent and descent.
        double leading = (lineHeight - (asc + desc)) / 2;
        maxAscent = Math.Max(maxAscent, asc + leading);
        maxDescent = Math.Max(maxDescent, desc + leading);
        return x + penX;
    }

    // The fragments of the run were created under the line before its size was known; now that the tallest ascent and descent are
    // known, they are placed on the common baseline.
    private void FinishLine(FragmentTree output, int top, double width, double maxAscent, double maxDescent)
    {
        ref Frame frame = ref _frames[top];
        double lineHeight = _itemCount == 0 ? 0 : maxAscent + maxDescent;
        double free = Math.Max(0, frame.ContentWidth - width);
        double shift = frame.Box.Values.TextAlign switch
        {
            TextAlignKind.Center => free / 2,
            TextAlignKind.Right or TextAlignKind.End => free,
            _ => 0,
        };

        output.SetBorderBox(_line, new Rect(frame.ContentLeft, frame.ContentTop + frame.Cursor, frame.ContentWidth, lineHeight));
        output.SetBaseline(_line, maxAscent);
        for (int i = 0; i < _itemCount; i++)
        {
            ref Item it = ref _items[i];
            double parentTop = it.Parent >= 0 ? maxAscent - _items[it.Parent].Ascent : 0;
            double parentX = it.Parent >= 0 ? _items[it.Parent].X : -shift;
            Rect r = new(it.X - parentX, (maxAscent - it.Ascent) - parentTop, it.Width, it.Ascent + it.Descent);
            output.SetBorderBox(it.Fragment, r);
        }

        frame.Cursor += lineHeight;
        frame.PrevMarginBottom = 0;
    }
}
