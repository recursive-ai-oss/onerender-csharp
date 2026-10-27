using OneRender.Forms;
using OneRender.Layout;
using OneRender.Style;

namespace OneRender.Painting;

// Form controls drawn the way Chrome 156 draws them by default (light scheme). Every number here was read off screenshots of Chrome for
// Testing with the probe pages in testdata/forms-probes and the gallery in testdata/forms; docs/forms.md lists them with the evidence.
// A text field, textarea, select or button keeps the native look only while its border and background are still the defaults the
// native look expects; once the author changes one the box is drawn from its CSS, as browsers do (CSS UI 4 §6.1, `appearance: auto`).
public sealed partial class Painter
{
    private const int ControlPart = 30;

    private enum NativeLook : byte
    {
        None,

        // The border and background are drawn by the native look, instead of from CSS.
        Replace,

        // The CSS background and border are drawn as usual and the native parts go on top.
        Overlay,
    }

    private static bool Near(ColorValue c, int r, int g, int b) =>
        Math.Abs((c.R * 255f) - r) < 1.5f && Math.Abs((c.G * 255f) - g) < 1.5f && Math.Abs((c.B * 255f) - b) < 1.5f;

    private static bool IsDefaultBorderColor(ColorValue c) =>
        (c.A > 0.99f && (Near(c, 118, 118, 118) || Near(c, 79, 79, 79) || Near(c, 70, 70, 70)))
        || (Math.Abs(c.A - 0.3f) < 0.02f && Near(c, 118, 118, 118));

    private static bool IsDefaultFill(ColorValue c, bool field) =>
        field
            ? (c.A > 0.99f && Near(c, 255, 255, 255)) || (Math.Abs(c.A - 0.3f) < 0.02f && Near(c, 239, 239, 239))
            : (c.A > 0.99f && (Near(c, 239, 239, 239) || Near(c, 229, 229, 229) || Near(c, 245, 245, 245))) || (Math.Abs(c.A - 0.3f) < 0.02f && Near(c, 239, 239, 239));

    // Whether a control's own border and background are the ones its native look replaces.
    private static NativeLook LookOf(in ComputedValues v, bool square)
    {
        switch (v.Control)
        {
            case ControlKind.Checkbox:
            case ControlKind.Radio:
            case ControlKind.Range:
            case ControlKind.Progress:
            case ControlKind.Meter:
                return NativeLook.Overlay;
            case ControlKind.TextField:
            case ControlKind.Password:
                return IsDefaultFrame(in v, square, 2, BorderStyleKind.Inset, field: true) ? NativeLook.Replace : NativeLook.None;
            case ControlKind.TextArea:
            case ControlKind.SelectList:
                return IsDefaultFrame(in v, square, 1, BorderStyleKind.Solid, field: true) ? NativeLook.Replace : NativeLook.None;
            case ControlKind.SelectMenu:
                return IsDefaultFrame(in v, square, 1, BorderStyleKind.Solid, field: false) ? NativeLook.Replace : NativeLook.None;
            case ControlKind.Button:
            case ControlKind.ButtonInput:
                return IsDefaultFrame(in v, square, 2, BorderStyleKind.Outset, field: false) ? NativeLook.Replace : NativeLook.None;
            default:
                return NativeLook.None;
        }
    }

    private static bool IsDefaultFrame(in ComputedValues v, bool square, double width, BorderStyleKind style, bool field)
    {
        if (!square || v.Background.HasImages || !IsDefaultFill(v.BackgroundColor, field))
        {
            return false;
        }

        return Math.Abs(v.BorderTopWidth - width) < 0.01 && Math.Abs(v.BorderRightWidth - width) < 0.01
            && Math.Abs(v.BorderBottomWidth - width) < 0.01 && Math.Abs(v.BorderLeftWidth - width) < 0.01
            && v.BorderTopStyle == style && v.BorderRightStyle == style && v.BorderBottomStyle == style && v.BorderLeftStyle == style
            && IsDefaultBorderColor(v.BorderTopColor) && IsDefaultBorderColor(v.BorderRightColor)
            && IsDefaultBorderColor(v.BorderBottomColor) && IsDefaultBorderColor(v.BorderLeftColor);
    }

    private ItemId ControlId(int f, int index) => Id2(f, ControlPart, index);

    private static PaintColor Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    // The color of the checked and filled parts: accent-color, or Chrome's default blue (#0075ff); washed out when disabled.
    private static PaintColor AccentOf(in ComputedValues v)
    {
        if ((v.ControlState & ControlStates.Disabled) != 0)
        {
            return Rgb(209, 209, 209);
        }

        if (v.AccentColorAuto)
        {
            return Rgb(0, 117, 255);
        }

        // CSS UI 4: accent-color takes currentcolor like any color, resolved against this element's own color.
        ColorValue a = v.AccentColor.Resolve(v.Color);
        return new PaintColor(a.R, a.G, a.B, a.A);
    }

    // Probe: the mark on an accent-colored box is white, and black when the accent color is light.
    private static PaintColor OnAccent(PaintColor accent, bool disabled) =>
        disabled ? Rgb(237, 237, 237) : ((0.299f * accent.R) + (0.587f * accent.G) + (0.114f * accent.B)) > 0.7f ? PaintColor.Black : PaintColor.White;

    // ------------------------------------------------------------------ border and background replaced by the native look

    private void PaintNativeFrame(int f, in ComputedValues v, in BoxGeometry g)
    {
        RectF r = g.Border;
        if (r.IsEmpty)
        {
            return;
        }

        bool button = v.Control is ControlKind.Button or ControlKind.ButtonInput;
        float radius = button ? 3f : 2f;
        var radii = CornerRadii.Uniform(radius);
        PaintColor cur = new(v.Color.R, v.Color.G, v.Color.B, v.Color.A);
        PaintColor fill = ToColor(v.BackgroundColor, cur);
        // Probe: a drop-down's face is white whatever its computed background (#efefef) says.
        if (v.Control == ControlKind.SelectMenu && v.BackgroundColor.A > 0.99f)
        {
            fill = PaintColor.White;
        }

        if (!fill.IsTransparent)
        {
            _list.FillRoundedRect(ControlId(f, 0), r, radii, fill);
        }

        PaintColor line = ToColor(v.BorderTopColor, cur);
        float w = SnapWidth(1);
        var side = new BorderSide(w, BorderStyle.Solid, line);
        _list.DrawBorder(ControlId(f, 1), r, radii, side, side, side, side);
        if (v.Control == ControlKind.SelectMenu)
        {
            PaintSelectArrow(f, in v, r, cur);
        }
        else if (v.Control == ControlKind.TextArea && v.OverflowX is OverflowKind.Visible or OverflowKind.Auto or OverflowKind.Scroll or OverflowKind.Clip)
        {
            PaintResizer(f, r);
        }
    }

    // Probe: the chevron is 8 px wide and 4.5 px tall with 2 px strokes, its tip 9 px in from the right edge.
    private void PaintSelectArrow(int f, in ComputedValues v, in RectF r, PaintColor cur)
    {
        float right = r.X + r.Width, cy = r.Y + (r.Height / 2);
        PaintColor c = ToColor(v.Color, cur);
        FillPolyline3(ControlId(f, 2), right - 13f, cy - 3.2f, right - 9f, cy + 1.6f, right - 5f, cy - 3.2f, 2f, c);
    }

    // Probe: two diagonal 1 px strokes (#666) in the bottom right corner, 7 px and 3 px long, 3.5 px in from the edges.
    private void PaintResizer(int f, in RectF r)
    {
        float right = r.X + r.Width, bottom = r.Y + r.Height;
        PaintColor c = Rgb(102, 102, 102);
        FillLine(ControlId(f, 2), right - 3.5f, bottom - 9.5f, right - 9.5f, bottom - 3.5f, 1f, c);
        FillLine(ControlId(f, 3), right - 3.5f, bottom - 5.5f, right - 5.5f, bottom - 3.5f, 1f, c);
    }

    // ------------------------------------------------------------------ native parts over the CSS box

    private void PaintNativeParts(int f, in ComputedValues v, in BoxGeometry g)
    {
        RectF r = g.Border;
        if (r.IsEmpty)
        {
            return;
        }

        bool disabled = (v.ControlState & ControlStates.Disabled) != 0;
        switch (v.Control)
        {
            case ControlKind.Checkbox:
                PaintCheckbox(f, in v, r, disabled);
                break;
            case ControlKind.Radio:
                PaintRadio(f, in v, r, disabled);
                break;
            case ControlKind.Range:
                PaintRange(f, in v, r, disabled);
                break;
            case ControlKind.Progress:
                PaintProgress(f, in v, r);
                break;
            case ControlKind.Meter:
                PaintMeter(f, in v, r);
                break;
        }
    }

    // Probe: 13x13, 2 px corner radius; unchecked is white inside a 1 px #767676 border, checked is filled with the accent color with a white
    // check (an indeterminate box has a white bar instead).
    private void PaintCheckbox(int f, in ComputedValues v, in RectF outer, bool disabled)
    {
        // Probe: a checkbox stretched by a grid or flex container still draws 13 px square, centered in its box.
        float side13 = Math.Min(outer.Width, outer.Height);
        var r = new RectF(outer.X + ((outer.Width - side13) / 2), outer.Y + ((outer.Height - side13) / 2), side13, side13);
        bool on = (v.ControlState & (ControlStates.Checked | ControlStates.Indeterminate)) != 0;
        var radii = CornerRadii.Uniform(2f);
        if (on)
        {
            PaintColor accent = AccentOf(in v);
            _list.FillRoundedRect(ControlId(f, 0), r, radii, accent);
            PaintColor mark = OnAccent(accent, disabled);
            if ((v.ControlState & ControlStates.Indeterminate) != 0)
            {
                _list.FillRect(ControlId(f, 1), new RectF(r.X + 3f, r.Y + (r.Height / 2) - 1f, r.Width - 6f, 2f), mark);
            }
            else
            {
                float sx = r.Width / 13f, sy = r.Height / 13f;
                FillPolyline3(ControlId(f, 1), r.X + (3.2f * sx), r.Y + (6.4f * sy), r.X + (5.5f * sx), r.Y + (8.7f * sy), r.X + (9.7f * sx), r.Y + (2.8f * sy), 1.9f, mark);
            }

            return;
        }

        _list.FillRoundedRect(ControlId(f, 0), r, radii, disabled ? Rgb(248, 248, 248) : PaintColor.White);
        var side = new BorderSide(SnapWidth(1), BorderStyle.Solid, disabled ? Rgb(209, 209, 209) : Rgb(118, 118, 118));
        _list.DrawBorder(ControlId(f, 1), r, radii, side, side, side, side);
    }

    // Probe: a 13 px circle; unchecked is white with a 1 px #767676 ring, checked is a 1 px accent ring, 2 px of white, then an accent dot 7 px across.
    private void PaintRadio(int f, in ComputedValues v, in RectF r, bool disabled)
    {
        float d = Math.Min(r.Width, r.Height);
        var box = new RectF(r.X + ((r.Width - d) / 2), r.Y + ((r.Height - d) / 2), d, d);
        var radii = CornerRadii.Uniform(d / 2);
        bool on = (v.ControlState & ControlStates.Checked) != 0;
        PaintColor ring = on ? AccentOf(in v) : disabled ? Rgb(209, 209, 209) : Rgb(118, 118, 118);
        _list.FillRoundedRect(ControlId(f, 0), box, radii, disabled ? Rgb(248, 248, 248) : PaintColor.White);
        var side = new BorderSide(SnapWidth(1), BorderStyle.Solid, ring);
        _list.DrawBorder(ControlId(f, 1), box, radii, side, side, side, side);
        if (on)
        {
            float dot = d * 7f / 13f;
            var dr = new RectF(box.X + ((d - dot) / 2), box.Y + ((d - dot) / 2), dot, dot);
            _list.FillRoundedRect(ControlId(f, 2), dr, CornerRadii.Uniform(dot / 2), ring);
        }
    }

    // Probe: the track is 8 px tall, centered, inset 1 px at each end, a pill with a 1 px #b2b2b2 border and #efefef inside; the part up to the
    // thumb is the accent color; the thumb is a 16 px accent circle that travels between 8 px in from each end.
    private void PaintRange(int f, in ComputedValues v, in RectF r, bool disabled)
    {
        float cy = r.Y + (r.Height / 2);
        var track = new RectF(r.X + 1f, cy - 4f, Math.Max(0, r.Width - 2f), 8f);
        PaintColor accent = disabled ? Rgb(203, 203, 203) : AccentOf(in v);
        PaintTrack(f, track, disabled ? Rgb(250, 250, 250) : Rgb(239, 239, 239), disabled ? Rgb(210, 210, 210) : Rgb(178, 178, 178), 0);
        float travel = Math.Max(0, r.Width - 16f);
        float t = (float)Math.Clamp(v.ControlFraction, 0, 1);
        float cx = r.X + 8f + (travel * t);
        PaintFilledPart(f, track, accent, cx - track.X, 2);
        _list.FillRoundedRect(ControlId(f, 4), new RectF(cx - 8f, cy - 8f, 16f, 16f), CornerRadii.Uniform(8f), accent);
    }

    // Probe: like the range track; the filled part reaches the border (blue over the gray), with a square end.
    private void PaintProgress(int f, in ComputedValues v, in RectF r)
    {
        float cy = r.Y + (r.Height / 2);
        var track = new RectF(r.X, cy - 4f, r.Width, 8f);
        PaintTrack(f, track, Rgb(239, 239, 239), Rgb(178, 178, 178), 0);
        PaintColor accent = AccentOf(in v);
        if ((v.ControlState & ControlStates.Indeterminate) != 0)
        {
            // Probe: an indeterminate bar shows a chunk a fifth of the track wide, starting a quarter of the way along.
            PaintFilledRange(f, track, accent, track.Width * 0.24f, track.Width * 0.44f, 2);
            return;
        }

        PaintFilledPart(f, track, accent, track.Width * (float)Math.Clamp(v.ControlFraction, 0, 1), 2);
    }

    // Probe: the track has a 1 px #cbcbcb border and #efefef inside, and the value is a pill inside the border: green in the optimum
    // region (#107c10), yellow in the suboptimum one, red (#d83b01) in the even-less-good one.
    private void PaintMeter(int f, in ComputedValues v, in RectF r)
    {
        float cy = r.Y + (r.Height / 2);
        var track = new RectF(r.X, cy - 4f, r.Width, 8f);
        PaintTrack(f, track, Rgb(239, 239, 239), Rgb(203, 203, 203), 0);
        PaintColor c = (v.ControlState & ControlStates.MeterBad) != 0 ? Rgb(216, 59, 1) : (v.ControlState & ControlStates.MeterSuboptimum) != 0 ? Rgb(255, 185, 0) : Rgb(16, 124, 16);
        var inner = new RectF(track.X + 1f, track.Y + 1f, Math.Max(0, track.Width - 2f), 6f);
        PaintFilledPart(f, inner, c, inner.Width * (float)Math.Clamp(v.ControlFraction, 0, 1), 2);
    }

    private void PaintTrack(int f, in RectF track, PaintColor fill, PaintColor border, int index)
    {
        if (track.IsEmpty)
        {
            return;
        }

        var radii = CornerRadii.Uniform(track.Height / 2);
        _list.FillRoundedRect(ControlId(f, index), track, radii, fill);
        var side = new BorderSide(1f, BorderStyle.Solid, border);
        _list.DrawBorder(ControlId(f, index + 1), track, radii, side, side, side, side);
    }

    // The pill of `track` clipped to its first `width` px.
    private void PaintFilledPart(int f, in RectF track, PaintColor color, float width, int index) => PaintFilledRange(f, track, color, 0, width, index);

    private void PaintFilledRange(int f, in RectF track, PaintColor color, float from, float to, int index)
    {
        float w = Math.Min(to, track.Width) - from;
        if (!(w > 0) || track.IsEmpty)
        {
            return;
        }

        _list.PushClipRect(ControlId(f, index), new RectF(track.X + from, track.Y, w, track.Height));
        _list.FillRoundedRect(ControlId(f, index + 1), track, CornerRadii.Uniform(track.Height / 2), color);
        _list.PopClip();
    }

    // ------------------------------------------------------------------ small shapes

    // A line of the given width as a filled quad.
    private void FillLine(ItemId id, float x0, float y0, float x1, float y1, float width, PaintColor color)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = MathF.Sqrt((dx * dx) + (dy * dy));
        if (!(len > 0))
        {
            return;
        }

        float nx = -dy / len * (width / 2), ny = dx / len * (width / 2);
        ReadOnlySpan<byte> verbs = [(byte)PathVerb.Move, (byte)PathVerb.Line, (byte)PathVerb.Line, (byte)PathVerb.Line, (byte)PathVerb.Close];
        ReadOnlySpan<float> coords = [x0 + nx, y0 + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, x0 - nx, y0 - ny];
        _list.FillPath(id, verbs, coords, color);
    }

    // Two segments p0-p1-p2 of the given width, joined with a miter, as one filled polygon.
    private void FillPolyline3(ItemId id, float x0, float y0, float x1, float y1, float x2, float y2, float width, PaintColor color)
    {
        float h = width / 2;
        Normal(x0, y0, x1, y1, out float ax, out float ay);
        Normal(x1, y1, x2, y2, out float bx, out float by);
        float k = 1f + ((ax * bx) + (ay * by));
        if (!(k > 0.05f))
        {
            return;
        }

        float mx = (ax + bx) / k * h, my = (ay + by) / k * h;
        ReadOnlySpan<byte> verbs = [(byte)PathVerb.Move, (byte)PathVerb.Line, (byte)PathVerb.Line, (byte)PathVerb.Line, (byte)PathVerb.Line, (byte)PathVerb.Line, (byte)PathVerb.Close];
        ReadOnlySpan<float> coords =
        [
            x0 + (ax * h), y0 + (ay * h),
            x1 + mx, y1 + my,
            x2 + (bx * h), y2 + (by * h),
            x2 - (bx * h), y2 - (by * h),
            x1 - mx, y1 - my,
            x0 - (ax * h), y0 - (ay * h),
        ];
        _list.FillPath(id, verbs, coords, color);
    }

    private static void Normal(float x0, float y0, float x1, float y1, out float nx, out float ny)
    {
        float dx = x1 - x0, dy = y1 - y0;
        float len = MathF.Sqrt((dx * dx) + (dy * dy));
        if (!(len > 0))
        {
            nx = 0;
            ny = 0;
            return;
        }

        nx = -dy / len;
        ny = dx / len;
    }
}

// ---------------------------------------------------------------------- fieldset

public sealed partial class Painter
{
    // HTML §15.3.13 (fieldset and legend): the fieldset's top border is drawn through the middle of the rendered legend, and the part of it
    // behind the legend is left out. Probe: Chrome 156 draws a 2 px groove top border at legend top + (legend height - 2) / 2, with the gap
    // exactly as wide as the legend's border box.
    private bool FindLegend(int fieldset, out Rect legend)
    {
        for (int c = _tree.FirstChildOf(fieldset); c >= 0; c = _tree.NextSiblingOf(c))
        {
            // Fragments of fragmentation (columns, pages) carry no box.
            int bx = _tree.BoxOf(c);
            if (bx >= 0 && _boxes.Make(bx).Values.Control == ControlKind.Legend)
            {
                Rect bb = _tree.BorderBoxOf(c);
                legend = new Rect(_absX[c], _absY[c], bb.Width, bb.Height);
                return true;
            }
        }

        legend = default;
        return false;
    }
}
