using OneRender.Forms;
using OneRender.Layout;
using OneRender.Style;

namespace OneRender.Painting;

// Form controls drawn the way Chrome 156 draws them by default, under the light and the dark color scheme (FormPalette). Every number here
// was read off screenshots of Chrome for Testing with the probe pages in testdata/forms-probes and the gallery in testdata/forms;
// docs/forms.md lists them with the evidence.
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

    private static bool IsDefaultBorderColor(ColorValue c, bool dark) =>
        dark
            ? c.A > 0.99f && (Near(c, 133, 133, 133) || Near(c, 107, 107, 107) || Near(c, 97, 97, 97) || Near(c, 98, 98, 98) || Near(c, 54, 54, 54) || Near(c, 74, 74, 74) || Near(c, 59, 59, 59))
            : (c.A > 0.99f && (Near(c, 118, 118, 118) || Near(c, 79, 79, 79) || Near(c, 70, 70, 70) || Near(c, 141, 141, 141) || Near(c, 226, 226, 226)))
              || (Math.Abs(c.A - 0.3f) < 0.02f && Near(c, 118, 118, 118));

    private static bool IsDefaultFill(ColorValue c, bool field, bool dark) =>
        dark
            ? c.A > 0.99f && (field ? Near(c, 59, 59, 59) || Near(c, 46, 46, 46) : Near(c, 107, 107, 107) || Near(c, 97, 97, 97) || Near(c, 54, 54, 54) || Near(c, 46, 46, 46))
            : field
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
        if (!square || v.Background.HasImages || !IsDefaultFill(v.BackgroundColor, field, v.DarkScheme))
        {
            return false;
        }

        return Math.Abs(v.BorderTopWidth - width) < 0.01 && Math.Abs(v.BorderRightWidth - width) < 0.01
            && Math.Abs(v.BorderBottomWidth - width) < 0.01 && Math.Abs(v.BorderLeftWidth - width) < 0.01
            && v.BorderTopStyle == style && v.BorderRightStyle == style && v.BorderBottomStyle == style && v.BorderLeftStyle == style
            && IsDefaultBorderColor(v.BorderTopColor, v.DarkScheme) && IsDefaultBorderColor(v.BorderRightColor, v.DarkScheme)
            && IsDefaultBorderColor(v.BorderBottomColor, v.DarkScheme) && IsDefaultBorderColor(v.BorderLeftColor, v.DarkScheme);
    }

    private ItemId ControlId(int f, int index) => Id2(f, ControlPart, index);

    private static PaintColor Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    // The color of the checked and filled parts: accent-color (CSS UI 4 §6.2), or the user agent's accent under the used scheme
    // (#0075ff light, #99c8ff dark); a disabled control ignores accent-color and is washed out (measured from Chrome).
    private static PaintColor AccentOf(in ComputedValues v, FormPalette pal)
    {
        if ((v.ControlState & ControlStates.Disabled) != 0)
        {
            return pal.DisabledAccent;
        }

        if (v.AccentColorAuto)
        {
            return pal.AutoAccent;
        }

        // CSS UI 4: accent-color takes currentcolor like any color, resolved against this element's own color.
        ColorValue a = v.AccentColor.Resolve(v.Color);
        return new PaintColor(a.R, a.G, a.B, a.A);
    }

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
        // Probe: an enabled drop-down's face is the field color (white, #3b3b3b dark) whatever its computed background (#efefef, #6b6b6b) says.
        if (v.Control == ControlKind.SelectMenu && v.BackgroundColor.A > 0.99f && (v.ControlState & ControlStates.Disabled) == 0)
        {
            fill = FormPalette.For(v.DarkScheme).Face;
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
            PaintResizer(f, r, FormPalette.For(v.DarkScheme));
        }
    }

    // Probe: the chevron is 8 px wide and 4.5 px tall with 2 px strokes, its tip 9 px in from the right edge.
    private void PaintSelectArrow(int f, in ComputedValues v, in RectF r, PaintColor cur)
    {
        float right = r.X + r.Width, cy = r.Y + (r.Height / 2);
        PaintColor c = ToColor(v.Color, cur);
        FillPolyline3(ControlId(f, 2), right - 13f, cy - 3.2f, right - 9f, cy + 1.6f, right - 5f, cy - 3.2f, 2f, c);
    }

    // Probe: two diagonal 1 px strokes in the bottom right corner, 7 px and 3 px long, 3.5 px in from the edges (#666 light, #171717 dark),
    // each followed by a lighter stroke one pixel to the right (white, #b0b0b0 dark).
    private void PaintResizer(int f, in RectF r, FormPalette pal)
    {
        float right = r.X + r.Width, bottom = r.Y + r.Height;
        FillLine(ControlId(f, 2), right - 3.5f, bottom - 9.5f, right - 9.5f, bottom - 3.5f, 1f, pal.ResizerDark);
        FillLine(ControlId(f, 3), right - 3.5f, bottom - 5.5f, right - 5.5f, bottom - 3.5f, 1f, pal.ResizerDark);
        FillLine(ControlId(f, 4), right - 2.5f, bottom - 9.5f, right - 8.5f, bottom - 3.5f, 1f, pal.ResizerLight);
        FillLine(ControlId(f, 5), right - 2.5f, bottom - 5.5f, right - 4.5f, bottom - 3.5f, 1f, pal.ResizerLight);
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

    // Probe: 13x13, 2 px corner radius; unchecked is the field color inside a 1 px border, checked is filled with the accent color with a mark
    // that is white or dark by the accent's contrast (an indeterminate box has a bar instead).
    private void PaintCheckbox(int f, in ComputedValues v, in RectF outer, bool disabled)
    {
        FormPalette pal = FormPalette.For(v.DarkScheme);
        // Probe: a checkbox stretched by a grid or flex container still draws 13 px square, centered in its box.
        float side13 = Math.Min(outer.Width, outer.Height);
        var r = new RectF(outer.X + ((outer.Width - side13) / 2), outer.Y + ((outer.Height - side13) / 2), side13, side13);
        bool on = (v.ControlState & (ControlStates.Checked | ControlStates.Indeterminate)) != 0;
        var radii = CornerRadii.Uniform(2f);
        if (on)
        {
            PaintColor accent = AccentOf(in v, pal);
            _list.FillRoundedRect(ControlId(f, 0), r, radii, accent);
            PaintColor mark = disabled ? pal.DisabledMark : pal.MarkOn(accent);
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

        _list.FillRoundedRect(ControlId(f, 0), r, radii, disabled ? pal.DisabledFace : pal.Face);
        var side = new BorderSide(SnapWidth(1), BorderStyle.Solid, disabled ? pal.DisabledBorder : pal.Border);
        _list.DrawBorder(ControlId(f, 1), r, radii, side, side, side, side);
    }

    // Probe: a 13 px circle; unchecked is the field color with a 1 px ring, checked is a 1 px accent ring, 2 px of the field color, then an accent
    // dot 7 px across.
    private void PaintRadio(int f, in ComputedValues v, in RectF r, bool disabled)
    {
        FormPalette pal = FormPalette.For(v.DarkScheme);
        float d = Math.Min(r.Width, r.Height);
        var box = new RectF(r.X + ((r.Width - d) / 2), r.Y + ((r.Height - d) / 2), d, d);
        var radii = CornerRadii.Uniform(d / 2);
        bool on = (v.ControlState & ControlStates.Checked) != 0;
        PaintColor ring = on ? AccentOf(in v, pal) : disabled ? pal.DisabledBorder : pal.Border;
        _list.FillRoundedRect(ControlId(f, 0), box, radii, disabled ? pal.DisabledFace : pal.Face);
        var side = new BorderSide(SnapWidth(1), BorderStyle.Solid, ring);
        _list.DrawBorder(ControlId(f, 1), box, radii, side, side, side, side);
        if (on)
        {
            float dot = d * 7f / 13f;
            var dr = new RectF(box.X + ((d - dot) / 2), box.Y + ((d - dot) / 2), dot, dot);
            _list.FillRoundedRect(ControlId(f, 2), dr, CornerRadii.Uniform(dot / 2), ring);
        }
    }

    // Probe: the track is 8 px tall, centered, inset 1 px at each end, a pill with a 1 px border; the part up to the thumb is the accent color;
    // the thumb is a 16 px accent circle that travels between 8 px in from each end. The track and the thumb are separate parts
    // (PaintRangeTrack, PaintRangeThumb) so that styling of the slider's pseudo-elements can replace either one.
    private void PaintRange(int f, in ComputedValues v, in RectF r, bool disabled)
    {
        FormPalette pal = FormPalette.For(v.DarkScheme);
        float cy = r.Y + (r.Height / 2);
        PaintColor accent = disabled ? pal.DisabledRangeAccent : AccentOf(in v, pal);
        float travel = Math.Max(0, r.Width - 16f);
        float t = (float)Math.Clamp(v.ControlFraction, 0, 1);
        float cx = r.X + 8f + (travel * t);
        PaintRangeTrack(f, in v, new RectF(r.X + 1f, cy - 4f, Math.Max(0, r.Width - 2f), 8f), accent, cx, disabled, pal);
        PaintRangeThumb(f, new RectF(cx - 8f, cy - 8f, 16f, 16f), accent);
    }

    private void PaintRangeTrack(int f, in ComputedValues v, in RectF track, PaintColor accent, float thumbCenterX, bool disabled, FormPalette pal)
    {
        PaintTrack(f, track, disabled ? pal.DisabledTrackFill : pal.TrackFillFor(accent), disabled ? pal.DisabledTrackBorder : pal.TrackBorderFor(accent), 0);
        PaintFilledPart(f, track, accent, thumbCenterX - track.X, 2, disabled ? PaintColor.Transparent : pal.FilledRing(accent));
    }

    private void PaintRangeThumb(int f, in RectF thumb, PaintColor accent) =>
        _list.FillRoundedRect(ControlId(f, 6), thumb, CornerRadii.Uniform(thumb.Height / 2), accent);

    // Probe: like the range track; the filled part reaches the border (the accent over the track, with the border's ring over it), with a square end.
    private void PaintProgress(int f, in ComputedValues v, in RectF r)
    {
        FormPalette pal = FormPalette.For(v.DarkScheme);
        float cy = r.Y + (r.Height / 2);
        var track = new RectF(r.X, cy - 4f, r.Width, 8f);
        PaintColor accent = AccentOf(in v, pal);
        PaintTrack(f, track, pal.TrackFillFor(accent), pal.TrackBorderFor(accent), 0);
        PaintColor ring = pal.FilledRing(accent);
        if ((v.ControlState & ControlStates.Indeterminate) != 0)
        {
            // Probe: an indeterminate bar shows a chunk a fifth of the track wide, starting a quarter of the way along.
            PaintFilledRange(f, track, accent, track.Width * 0.24f, track.Width * 0.44f, 2, ring);
            return;
        }

        PaintFilledPart(f, track, accent, track.Width * (float)Math.Clamp(v.ControlFraction, 0, 1), 2, ring);
    }

    // Probe: the track has a 1 px border and the value is a pill inside the border: green in the optimum region, yellow in the suboptimum one,
    // red in the even-less-good one (#107c10, #ffb900, #d83b01 light; #74b374, #f2c812, #e98f6d dark).
    private void PaintMeter(int f, in ComputedValues v, in RectF r)
    {
        FormPalette pal = FormPalette.For(v.DarkScheme);
        float cy = r.Y + (r.Height / 2);
        var track = new RectF(r.X, cy - 4f, r.Width, 8f);
        PaintTrack(f, track, pal.MeterFill, pal.MeterBorder, 0);
        PaintColor c = (v.ControlState & ControlStates.MeterBad) != 0 ? pal.MeterBad : (v.ControlState & ControlStates.MeterSuboptimum) != 0 ? pal.MeterSuboptimum : pal.MeterOptimum;
        var inner = new RectF(track.X + 1f, track.Y + 1f, Math.Max(0, track.Width - 2f), 6f);
        PaintFilledPart(f, inner, c, inner.Width * (float)Math.Clamp(v.ControlFraction, 0, 1), 2, PaintColor.Transparent);
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

    // The pill of `track` clipped to its first `width` px, with `ring` (when not transparent) as the track's 1 px border over it.
    private void PaintFilledPart(int f, in RectF track, PaintColor color, float width, int index, PaintColor ring) => PaintFilledRange(f, track, color, 0, width, index, ring);

    private void PaintFilledRange(int f, in RectF track, PaintColor color, float from, float to, int index, PaintColor ring)
    {
        float w = Math.Min(to, track.Width) - from;
        if (!(w > 0) || track.IsEmpty)
        {
            return;
        }

        var radii = CornerRadii.Uniform(track.Height / 2);
        _list.PushClipRect(ControlId(f, index), new RectF(track.X + from, track.Y, w, track.Height));
        _list.FillRoundedRect(ControlId(f, index + 1), track, radii, color);
        if (!ring.IsTransparent)
        {
            var side = new BorderSide(1f, BorderStyle.Solid, ring);
            _list.DrawBorder(ControlId(f, 8), track, radii, side, side, side, side);
        }

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
