using OneRender.Layout;
using OneRender.Style;
using OneRender.Text;
using OneRender.Tree;

namespace OneRender.Painting;

// Text paint: shadows, decorations, list markers and replaced images.
public sealed partial class Painter
{
    private const int MaxDecorators = 16;
    private static readonly string[] RomanSymbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
    private readonly int[] _decorators = new int[MaxDecorators];
    private byte[] _wavVerbs = new byte[64];
    private float[] _wavCoords = new float[256];

    private void PaintText(int f)
    {
        Observer?.Content(_tree.Make(f), _list.Count);
        Box box = _boxes.Make(_tree.BoxOf(f));
        ref readonly ComputedValues v = ref box.Values;
        if (v.Visibility != VisibilityKind.Visible)
        {
            return;
        }

        GlyphRun run = _tree.GetGlyphRun(_tree.GlyphRunOf(f));
        int count = run.GlyphCount;
        if (count == 0)
        {
            return;
        }

        if (_gx.Length < count)
        {
            int size = Math.Max(count, _gx.Length * 2);
            _gx = new float[size];
            _gy = new float[size];
            _gc = new int[size];
        }

        ReadOnlySpan<double> xs = _tree.GlyphX(in run);
        ReadOnlySpan<double> ys = _tree.GlyphY(in run);
        ReadOnlySpan<int> clusters = _tree.GlyphClusters(in run);
        ReadOnlySpan<char> all = box.Text;
        int start = Math.Clamp(_tree.TextStartOf(f), 0, all.Length);
        int length = Math.Clamp(_tree.TextLengthOf(f), 0, all.Length - start);
        ReadOnlySpan<char> text = all.Slice(start, length);
        for (int k = 0; k < count; k++)
        {
            _gx[k] = C(xs[k]);
            _gy[k] = C(ys[k]);
            // Clusters are offsets into the box's text; the list wants them relative to the run's text.
            _gc[k] = Math.Clamp(clusters[k] - start, 0, length);
        }

        PaintColor color = new(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
        float ox = C(_absX[f]), oy = C(_baseY[f]);

        // Text Decoration 4 §4: shadows first (the first one on top), then underline and overline, the text, then line-through.
        ShadowList shadows = v.TextShadow;
        for (int s = shadows.Count - 1; s >= 0; s--)
        {
            ShadowSpec sh = shadows.Items[s];
            PaintColor sc = ToColor(sh.Color, color);
            if (sc.IsTransparent)
            {
                continue;
            }

            DrawRun(Id2(f, 7, s), in run, ox + C(sh.OffsetX), oy + C(sh.OffsetY), sc, count, text, C(Math.Max(0, sh.Blur)));
        }

        int decorators = CollectDecorators(f);
        if (decorators > 0)
        {
            PaintDecorations(f, in run, decorators, color, true);
        }

        DrawRun(Id(f, 2), in run, ox, oy, color, count, text, 0f);
        if (decorators > 0)
        {
            PaintDecorations(f, in run, decorators, color, false);
        }
    }

    private void DrawRun(ItemId id, in GlyphRun run, float ox, float oy, PaintColor color, int count, ReadOnlySpan<char> text, float blur)
    {
        _list.DrawGlyphRun(
            id,
            run.Face,
            (float)run.FontSize,
            ox,
            oy,
            color,
            _tree.GlyphIds(in run),
            _gx.AsSpan(0, count),
            _gy.AsSpan(0, count),
            _gc.AsSpan(0, count),
            text,
            run.RightToLeft,
            blur);
    }

    // Text Decoration 4 §2.1: a decoration is drawn by the box that declares it across all of its in-flow inline text, and by no
    // box further out; it does not reach into inline-blocks, floats or absolutely positioned boxes, which become decorating boxes
    // of their own only through their own style. Walking up from the text, every box up to and including the first block
    // container (anonymous wrappers excepted) contributes.
    private int CollectDecorators(int f)
    {
        int n = 0;
        int p = _tree.ParentOf(f);
        for (int steps = 0; p >= 0 && steps < 4096 && n < MaxDecorators; steps++, p = _tree.ParentOf(p))
        {
            if ((FragmentKind)_tree.KindOf(p) != FragmentKind.Box)
            {
                continue;
            }

            Box b = _boxes.Make(_tree.BoxOf(p));
            if (b.Values.TextDecorationLine != TextDecorationLines.None)
            {
                _decorators[n++] = p;
            }

            if (b.Kind != BoxKind.Inline && !b.IsAnonymous)
            {
                break;
            }
        }

        return n;
    }

    private void PaintDecorations(int f, in GlyphRun run, int decorators, PaintColor textColor, bool under)
    {
        FontFaceMetrics(run, out double upem, out double asc, out double ulPos, out double ulThick, out double stPos, out double stThick);
        double fs = run.FontSize;
        double scale = fs / upem;
        double x = _absX[f];
        double w = _tree.BorderBoxOf(f).Width;
        double baseline = _baseY[f];
        if (!(w > 0))
        {
            return;
        }

        for (int d = 0; d < decorators; d++)
        {
            Box b = _boxes.Make(_tree.BoxOf(_decorators[d]));
            ref readonly ComputedValues dv = ref b.Values;
            PaintColor color = ToColor(dv.TextDecorationColor, new PaintColor(dv.Color.R, dv.Color.G, dv.Color.B, dv.Color.A));
            if (color.IsTransparent)
            {
                continue;
            }

            TextDecorationLines lines = dv.TextDecorationLine;
            double thick = dv.TextDecorationThickness.Kind switch
            {
                LengthKind.Px => dv.TextDecorationThickness.Value,
                LengthKind.Percent => dv.TextDecorationThickness.Value / 100 * fs,
                _ => 0,
            };
            if (!(thick > 0))
            {
                // auto and from-font: the font's own thickness, at least a device pixel (Text Decoration 4 §2.3).
                thick = Math.Max(ulThick * scale, 1.0 / _dpr);
            }

            if (under && (lines & TextDecorationLines.Underline) != 0)
            {
                double top;
                Length off = dv.TextUnderlineOffset;
                if (off.Kind is LengthKind.Px or LengthKind.Percent)
                {
                    top = baseline + (off.Kind == LengthKind.Px ? off.Value : off.Value / 100 * fs);
                }
                else if (dv.TextUnderlinePosition == TextUnderlinePositionKind.Under)
                {
                    top = baseline + (asc > 0 ? Math.Abs(fs * 0.2) : 0);
                }
                else
                {
                    top = baseline - (ulPos * scale) - (thick / 2);
                }

                DrawDecorationLine(f, d * 3, x, top, w, thick, dv.TextDecorationStyle, color);
            }

            if (under && (lines & TextDecorationLines.Overline) != 0)
            {
                DrawDecorationLine(f, (d * 3) + 1, x, baseline - (asc * scale), w, thick, dv.TextDecorationStyle, color);
            }

            if (!under && (lines & TextDecorationLines.LineThrough) != 0)
            {
                double st = dv.TextDecorationThickness.Kind is LengthKind.Px or LengthKind.Percent ? thick : Math.Max(stThick * scale, 1.0 / _dpr);
                double top = baseline - (stPos * scale) - (st / 2);
                DrawDecorationLine(f, (d * 3) + 2, x, top, w, st, dv.TextDecorationStyle, color);
            }
        }

        _ = textColor;
    }

    private static void FontFaceMetrics(in GlyphRun run, out double upem, out double ascent, out double ulPos, out double ulThick, out double stPos, out double stThick)
    {
        OneFont.FontMetrics m = run.Face.Metrics;
        upem = Math.Max(1, m.UnitsPerEm);
        ascent = m.Ascender;
        // Measured against Chrome 156 with the packaged fonts (tools/snap/probe.py, sizes 10 to 48 px): the underline's center sits half
        // the descent below the baseline (a negative number here), not where the post table's underlinePosition says. The OS/2 table
        // gives the top of the strike.
        ulPos = m.Descender != 0 ? -Math.Abs(m.Descender) / 2.0 : -upem * 0.1;
        ulThick = m.UnderlineThickness > 0 ? m.UnderlineThickness : upem / 14;
        stPos = m.StrikeoutPosition > 0 ? m.StrikeoutPosition - (m.StrikeoutSize / 2.0) : upem * 0.3;
        stThick = m.StrikeoutSize > 0 ? m.StrikeoutSize : ulThick;
    }

    private void DrawDecorationLine(int f, int index, double x, double top, double w, double thick, TextDecorationStyleKind style, PaintColor color)
    {
        switch (style)
        {
            case TextDecorationStyleKind.DoubleLine:
            {
                RectF a = SnapRect(x, top, w, thick);
                RectF b = SnapRect(x, top + (2 * thick), w, thick);
                _list.FillRect(Id2(f, 8, index * 4), a, color);
                _list.FillRect(Id2(f, 8, (index * 4) + 1), b, color);
                break;
            }

            case TextDecorationStyleKind.Dotted:
            case TextDecorationStyleKind.Dashed:
            {
                RectF r = SnapRect(x, top, w, thick);
                if (r.IsEmpty)
                {
                    break;
                }

                BorderStyle bs = style == TextDecorationStyleKind.Dotted ? BorderStyle.Dotted : BorderStyle.Dashed;
                float t = r.Height;
                _list.DrawBorder(Id2(f, 8, index * 4), r, CornerRadii.Zero, new BorderSide(t, bs, color), default, default, default);
                break;
            }

            case TextDecorationStyleKind.Wavy:
                DrawWave(f, index, x, top, w, thick, color);
                break;
            default:
            {
                RectF r = SnapRect(x, top, w, thick);
                if (!r.IsEmpty)
                {
                    _list.FillRect(Id2(f, 8, index * 4), r, color);
                }

                break;
            }
        }
    }

    // A zigzag band of the line's thickness: the wave amplitude and half-period follow the thickness (a design choice; Text
    // Decoration 4 §2.4 does not fix the wave's shape).
    private void DrawWave(int f, int index, double x, double top, double w, double thick, PaintColor color)
    {
        double step = Math.Max(2.0, 2 * thick);
        int n = (int)Math.Min(1024, Math.Ceiling(w / step) + 1);
        if (n < 1)
        {
            return;
        }

        int points = (n + 1) * 2;
        if (_wavVerbs.Length < points + 1)
        {
            _wavVerbs = new byte[points + 1];
            _wavCoords = new float[(points + 1) * 2];
        }

        double amp = Math.Max(thick, 1.0);
        int c = 0;
        for (int i = 0; i <= n; i++)
        {
            double px = Math.Min(x + w, x + (i * step));
            double py = top + ((i & 1) == 0 ? 0 : amp);
            _wavVerbs[c / 2] = (byte)(i == 0 ? PathVerb.Move : PathVerb.Line);
            _wavCoords[c++] = C(px);
            _wavCoords[c++] = C(py);
        }

        int v = c / 2;
        for (int i = n; i >= 0; i--)
        {
            double px = Math.Min(x + w, x + (i * step));
            double py = top + ((i & 1) == 0 ? 0 : amp) + thick;
            _wavVerbs[v++] = (byte)PathVerb.Line;
            _wavCoords[c++] = C(px);
            _wavCoords[c++] = C(py);
        }

        _wavVerbs[v++] = (byte)PathVerb.Close;
        _list.FillPath(Id2(f, 8, index * 4), _wavVerbs.AsSpan(0, v), _wavCoords.AsSpan(0, c), color);
    }

    // ------------------------------------------------------------------ list markers

    // CSS Lists 3 §3: the marker is painted inside the box layout reserved for it. Shapes for the symbolic styles, text for the others.
    private void PaintMarker(int f, Box box, in ComputedValues v, in BoxGeometry g)
    {
        RectF r = g.Border;
        int parent = _tree.ParentOf(f);
        int index = parent >= 0 ? Math.Max(1, _listIndex[parent]) : 1;
        double fs = v.FontSize;
        double asc = fs * 0.8, desc = fs * 0.2;
        if (_fonts is not null)
        {
            FontMetricsInfo mi = _fonts.GetMetrics(in v);
            asc = mi.Ascent;
            desc = mi.Descent;
        }

        double baseline = r.Y + ((r.Height - (asc + desc)) / 2) + asc;
        PaintColor color = new(v.Color.R, v.Color.G, v.Color.B, v.Color.A);

        ImageSpec li = v.ListStyleImage;
        if (li.Kind == ImageSourceKind.Url && TryImage(li.Url!, out ImageInfo info) && info.Width > 0 && info.Height > 0)
        {
            double iw = info.Width, ih = info.Height;
            double ix = v.ListStylePosition == ListStylePositionKind.Outside ? r.Right - iw - (fs * 0.25) : r.X;
            _list.DrawImage(Id2(f, 9, 0), info.Handle, default, new RectF(C(ix), C(baseline - ih), C(iw), C(ih)), SamplingOf(in v));
            return;
        }

        if (v.ListStyleTypeIsString)
        {
            DrawMarkerText(f, in v, v.ListStyleType, r, baseline, color);
            return;
        }

        string type = v.ListStyleType;
        // Chrome draws the three symbols as shapes sized from the font: a disc of about a third of the ascent.
        double d = Math.Max(1.0, Math.Round(fs * 0.3125));
        double cy = baseline - (fs * 0.3);
        double cx = v.ListStylePosition == ListStylePositionKind.Outside ? r.Right - (fs * 0.5) - (d / 2) : r.X + (d / 2);
        if (type.Equals("disc", StringComparison.OrdinalIgnoreCase))
        {
            _list.FillRoundedRect(Id2(f, 9, 0), new RectF(C(cx - (d / 2)), C(cy - (d / 2)), C(d), C(d)), CornerRadii.Uniform(C(d / 2)), color);
            return;
        }

        if (type.Equals("circle", StringComparison.OrdinalIgnoreCase))
        {
            float bw = (float)Math.Max(1.0, Math.Round(d / 8));
            var side = new BorderSide(bw, BorderStyle.Solid, color);
            _list.DrawBorder(Id2(f, 9, 0), new RectF(C(cx - (d / 2)), C(cy - (d / 2)), C(d), C(d)), CornerRadii.Uniform(C(d / 2)), side, side, side, side);
            return;
        }

        if (type.Equals("square", StringComparison.OrdinalIgnoreCase))
        {
            _list.FillRect(Id2(f, 9, 0), new RectF(C(cx - (d / 2)), C(cy - (d / 2)), C(d), C(d)), color);
            return;
        }

        Span<char> buf = stackalloc char[40];
        int n = FormatCounter(type, index, buf);
        buf[n++] = '.';
        buf[n++] = ' ';
        DrawMarkerText(f, in v, string.Empty, r, baseline, color, buf.Slice(0, n));
        _ = box;
    }

    private void DrawMarkerText(int f, in ComputedValues v, string literal, RectF r, double baseline, PaintColor color, ReadOnlySpan<char> text = default)
    {
        if (_fonts is null)
        {
            return;
        }

        ReadOnlySpan<char> t = text.IsEmpty ? literal.AsSpan() : text;
        if (t.IsEmpty)
        {
            return;
        }

        ShapedText st = _fonts.Shape(t, in v, false);
        double originX = v.ListStylePosition == ListStylePositionKind.Outside ? r.Right - st.Width : r.X;
        ReadOnlySpan<ShapedTextSegment> segs = st.Segments;
        for (int s = 0; s < segs.Length; s++)
        {
            ShapedTextSegment seg = segs[s];
            int count = seg.GlyphCount;
            if (count == 0)
            {
                continue;
            }

            if (_gx.Length < count)
            {
                int size = Math.Max(count, _gx.Length * 2);
                _gx = new float[size];
                _gy = new float[size];
                _gc = new int[size];
            }

            ReadOnlySpan<float> sx = st.X, sy = st.Y;
            ReadOnlySpan<int> cl = st.GlyphClusters;
            float x0 = sx[seg.GlyphStart];
            for (int k = 0; k < count; k++)
            {
                _gx[k] = sx[seg.GlyphStart + k] - x0;
                _gy[k] = sy[seg.GlyphStart + k];
                _gc[k] = Math.Clamp(cl[seg.GlyphStart + k] - seg.TextStart, 0, seg.TextLength);
            }

            _list.DrawGlyphRun(
                Id2(f, 9, s),
                seg.Face.Face,
                st.Size,
                C(originX + x0),
                C(baseline),
                color,
                st.GlyphIds.Slice(seg.GlyphStart, count),
                _gx.AsSpan(0, count),
                _gy.AsSpan(0, count),
                _gc.AsSpan(0, count),
                st.Text.Slice(seg.TextStart, seg.TextLength),
                st.RightToLeft);
        }
    }

    // CSS Counter Styles 3 §6: the predefined styles the markers need. Anything else falls back to decimal (§3.1).
    private static int FormatCounter(string type, int value, Span<char> buf)
    {
        if (type.Equals("decimal-leading-zero", StringComparison.OrdinalIgnoreCase))
        {
            return value is >= 0 and < 10 ? WriteInt(value, buf, 2) : WriteInt(value, buf, 1);
        }

        bool lower = type.Equals("lower-alpha", StringComparison.OrdinalIgnoreCase) || type.Equals("lower-latin", StringComparison.OrdinalIgnoreCase);
        bool upper = type.Equals("upper-alpha", StringComparison.OrdinalIgnoreCase) || type.Equals("upper-latin", StringComparison.OrdinalIgnoreCase);
        if ((lower || upper) && value > 0)
        {
            // Bijective base 26: a, b, ..., z, aa, ab, ...
            Span<char> tmp = stackalloc char[16];
            int len = 0;
            int x = value;
            while (x > 0 && len < tmp.Length)
            {
                x--;
                tmp[len++] = (char)((lower ? 'a' : 'A') + (x % 26));
                x /= 26;
            }

            for (int i = 0; i < len; i++)
            {
                buf[i] = tmp[len - 1 - i];
            }

            return len;
        }

        bool lroman = type.Equals("lower-roman", StringComparison.OrdinalIgnoreCase);
        if ((lroman || type.Equals("upper-roman", StringComparison.OrdinalIgnoreCase)) && value is > 0 and < 4000)
        {
            ReadOnlySpan<int> vals = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
            int len = 0;
            int x = value;
            for (int i = 0; i < vals.Length; i++)
            {
                while (x >= vals[i])
                {
                    foreach (char ch in RomanSymbols[i])
                    {
                        buf[len++] = lroman ? char.ToLowerInvariant(ch) : ch;
                    }

                    x -= vals[i];
                }
            }

            return len;
        }

        if (type.Equals("lower-greek", StringComparison.OrdinalIgnoreCase) && value is > 0 and <= 24)
        {
            // alpha .. omega without the final sigma.
            string greek = "\u03b1\u03b2\u03b3\u03b4\u03b5\u03b6\u03b7\u03b8\u03b9\u03ba\u03bb\u03bc\u03bd\u03be\u03bf\u03c0\u03c1\u03c3\u03c4\u03c5\u03c6\u03c7\u03c8\u03c9";
            buf[0] = greek[value - 1];
            return 1;
        }

        return WriteInt(value, buf, 1);
    }

    private static int WriteInt(int value, Span<char> buf, int minDigits)
    {
        Span<char> tmp = stackalloc char[12];
        int len = 0;
        bool neg = value < 0;
        long x = Math.Abs((long)value);
        do
        {
            tmp[len++] = (char)('0' + (x % 10));
            x /= 10;
        }
        while (x > 0);
        while (len < minDigits)
        {
            tmp[len++] = '0';
        }

        int n = 0;
        if (neg)
        {
            buf[n++] = '-';
        }

        for (int i = len - 1; i >= 0; i--)
        {
            buf[n++] = tmp[i];
        }

        return n;
    }

    // ------------------------------------------------------------------ replaced content

    // Images 3 §5.5 and §5.6: object-fit sizes the image within the content box and object-position places it.
    private void PaintReplaced(int f, Box box, in ComputedValues v, in BoxGeometry g)
    {
        if (box.Node.Replaced != ReplacedKind.Image)
        {
            return;
        }

        string? src = box.Node.ReplacedSource;
        RectF cr = g.Content;
        if (string.IsNullOrEmpty(src) || cr.IsEmpty || !TryImage(src, out ImageInfo info))
        {
            return;
        }

        double nw = info.Width > 0 ? info.Width : cr.Width, nh = info.Height > 0 ? info.Height : cr.Height;
        double dw, dh;
        double sx = cr.Width / nw, sy = cr.Height / nh;
        switch (v.ObjectFit)
        {
            case ObjectFitKind.Contain:
                dw = nw * Math.Min(sx, sy);
                dh = nh * Math.Min(sx, sy);
                break;
            case ObjectFitKind.Cover:
                dw = nw * Math.Max(sx, sy);
                dh = nh * Math.Max(sx, sy);
                break;
            case ObjectFitKind.None:
                dw = nw;
                dh = nh;
                break;
            case ObjectFitKind.ScaleDown:
                double k = Math.Min(1.0, Math.Min(sx, sy));
                dw = nw * k;
                dh = nh * k;
                break;
            default:
                dw = cr.Width;
                dh = cr.Height;
                break;
        }

        double dx = cr.X + v.ObjectPosition.X.Resolve(cr.Width - dw);
        double dy = cr.Y + v.ObjectPosition.Y.Resolve(cr.Height - dh);
        var dest = new RectF(C(dx), C(dy), C(dw), C(dh));
        bool inside = dest.X >= cr.X - 0.001f && dest.Y >= cr.Y - 0.001f && dest.Right <= cr.Right + 0.001f && dest.Bottom <= cr.Bottom + 0.001f;
        bool clip = (!inside || !g.ContentRadii.IsZero) && _scopes < MaxScopes;
        if (clip)
        {
            if (g.ContentRadii.IsZero)
            {
                _list.PushClipRect(Id2(f, 10, 0), cr);
            }
            else
            {
                _list.PushClipRoundedRect(Id2(f, 10, 0), cr, g.ContentRadii);
            }

            _scopes++;
        }

        _list.DrawImage(Id2(f, 11, 0), info.Handle, default, dest, SamplingOf(in v));
        if (clip)
        {
            _list.PopClip();
            _scopes--;
        }
    }
}
