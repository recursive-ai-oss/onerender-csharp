using System.Numerics;
using OneRender.Painting;
using Brush = OneImage.Drawing.Brush;
using IntersectionRule = OneImage.Drawing.IntersectionRule;
using OBlend = OneImage.BlendMode;
using Path = OneImage.Drawing.Path;
using PathBoolean = OneImage.Drawing.PathBoolean;
using Pen = OneImage.Drawing.Pen;
using StrokeCap = OneImage.Drawing.StrokeCap;
using StrokeJoin = OneImage.Drawing.StrokeJoin;

namespace OneRender.Image;

internal sealed partial class Rasterizer
{
    // CSS Backgrounds 3 §4: a border is the ring between the outer (border) edge and the inner (padding) edge. Each side owns
    // the part of the ring on its side of the diagonals that join the outer corners to the inner corners (§5.3). Styles are
    // drawn per side into that part.
    private void DrawBorder(in DisplayItem it)
    {
        RectF rect = it.Rect;
        CornerRadii radii = it.Radii;
        BorderSide top = it.BorderTop, right = it.BorderRight, bottom = it.BorderBottom, left = it.BorderLeft;
        if (_clip.IsEmpty)
        {
            return;
        }

        float wT = top.Width, wR = right.Width, wB = bottom.Width, wL = left.Width;

        // Fast path: four solid sides of one color are one ring, which also avoids anti-aliasing seams along the diagonals.
        if (top.Style == BorderStyle.Solid && right.Style == BorderStyle.Solid && bottom.Style == BorderStyle.Solid && left.Style == BorderStyle.Solid &&
            top.Color == right.Color && top.Color == bottom.Color && top.Color == left.Color)
        {
            if (!top.Color.IsTransparent)
            {
                Brush brush = Brush.Solid(ToRgba(top.Color));
                FillRing(brush, rect, radii, 0, 0, 0, 0, wT, wR, wB, wL);
            }

            return;
        }

        for (int side = 0; side < 4; side++)
        {
            BorderSide s = side switch { 0 => top, 1 => right, 2 => bottom, _ => left };
            if (s.Width <= 0f || s.Style is BorderStyle.None or BorderStyle.Hidden || s.Color.IsTransparent)
            {
                continue;
            }

            Path wedge = Wedge(rect, side, wT, wR, wB, wL);
            PaintSide(rect, radii, side, s, wT, wR, wB, wL, wedge);
        }
    }

    private void PaintSide(in RectF rect, in CornerRadii radii, int side, in BorderSide s, float wT, float wR, float wB, float wL, Path wedge)
    {
        float w = s.Width;
        BorderStyle style = s.Style;
        if (style == BorderStyle.DoubleLine && w < 3f)
        {
            // Too thin to show a gap (CSS Backgrounds 3 §4.2 asks for thirds; below 3px they cannot be told apart).
            style = BorderStyle.Solid;
        }

        switch (style)
        {
            case BorderStyle.Solid:
                {
                    Brush brush = Brush.Solid(ToRgba(s.Color));
                    FillRing(brush, rect, radii, 0, 0, 0, 0, wT, wR, wB, wL, wedge);
                    break;
                }

            case BorderStyle.DoubleLine:
                {
                    Brush brush = Brush.Solid(ToRgba(s.Color));
                    float t = 1f / 3f;
                    FillRing(brush, rect, radii, 0, 0, 0, 0, wT * t, wR * t, wB * t, wL * t, wedge);
                    FillRing(brush, rect, radii, wT * 2 * t, wR * 2 * t, wB * 2 * t, wL * 2 * t, wT, wR, wB, wL, wedge);
                    break;
                }

            case BorderStyle.Groove:
            case BorderStyle.Ridge:
                {
                    // Two half-width rings, one dark and one light; which is outer depends on the side and on groove/ridge.
                    bool topLeft = side is 0 or 3;
                    bool outerDark = (style == BorderStyle.Groove) == topLeft;
                    Brush outer = Brush.Solid(ToRgba(outerDark ? Dark(s.Color) : s.Color));
                    Brush inner = Brush.Solid(ToRgba(outerDark ? s.Color : Dark(s.Color)));
                    FillRing(outer, rect, radii, 0, 0, 0, 0, wT / 2, wR / 2, wB / 2, wL / 2, wedge);
                    FillRing(inner, rect, radii, wT / 2, wR / 2, wB / 2, wL / 2, wT, wR, wB, wL, wedge);
                    break;
                }

            case BorderStyle.Inset:
            case BorderStyle.Outset:
                {
                    bool topLeft = side is 0 or 3;
                    bool dark = (style == BorderStyle.Inset) == topLeft;
                    Brush brush = Brush.Solid(ToRgba(dark ? Dark(s.Color) : s.Color));
                    FillRing(brush, rect, radii, 0, 0, 0, 0, wT, wR, wB, wL, wedge);
                    break;
                }

            case BorderStyle.Dashed:
            case BorderStyle.Dotted:
                StrokeSide(rect, radii, s, wT, wR, wB, wL, wedge);
                break;
            default:
                break;
        }
    }

    // The darker color of groove, ridge, inset and outset: two thirds of each channel. Chrome-style special handling of black is
    // not done, so a black inset border looks like a solid one.
    private static PaintColor Dark(in PaintColor c) => new(c.R * (2f / 3f), c.G * (2f / 3f), c.B * (2f / 3f), c.A);

    private void StrokeSide(in RectF rect, in CornerRadii radii, in BorderSide s, float wT, float wR, float wB, float wL, Path wedge)
    {
        // Dashes follow the middle line of the ring and are cut to this side's part of it. Dashed: dash 2w, gap w. Dotted:
        // round dots of diameter w with a gap of w. Patterns start at the top-left corner and are not fitted to the sides.
        float w = s.Width;
        Path mid = Shapes.Inset(rect, radii, wT / 2, wR / 2, wB / 2, wL / 2, out bool empty);
        if (empty)
        {
            return;
        }

        Path ring = RingPath(rect, radii, 0, 0, 0, 0, wT, wR, wB, wL, out IntersectionRule ringRule);
        Path part = PathBoolean.Intersect(ring.Transform(_ctm), wedge.Transform(_ctm), ringRule, IntersectionRule.NonZero);
        Path clip = _clip.Path is null ? part : PathBoolean.Intersect(_clip.Path, part);
        if (clip.IsEmpty)
        {
            return;
        }

        Pen pen = s.Style == BorderStyle.Dashed
            ? new Pen(Brush.Solid(ToRgba(s.Color)), w, StrokeJoin.Miter, StrokeCap.Butt, 4f, new[] { 2f * w, w })
            : new Pen(Brush.Solid(ToRgba(s.Color)), w, StrokeJoin.Round, StrokeCap.Round, 4f, new[] { 0.01f, (2f * w) - 0.01f });
        _target.Draw(pen, mid, true, 1f, OBlend.Normal, OneImage.AlphaComposition.SrcOver, false, _ctm, clip, IntersectionRule.NonZero);
    }

    // The ring between the rounded rectangle inset by (a*) and the one inset by (b*). Returns the path and the rule to fill it with.
    private static Path RingPath(in RectF rect, in CornerRadii radii, float aT, float aR, float aB, float aL, float bT, float bR, float bB, float bL, out IntersectionRule rule)
    {
        Path outer = Shapes.Inset(rect, radii, aT, aR, aB, aL, out bool outerEmpty);
        Path inner = Shapes.Inset(rect, radii, bT, bR, bB, bL, out bool innerEmpty);
        rule = IntersectionRule.NonZero;
        if (outerEmpty)
        {
            return Path.Empty;
        }

        if (innerEmpty)
        {
            return outer;
        }

        rule = IntersectionRule.EvenOdd;
        return Path.Combine(outer, inner);
    }

    private void FillRing(Brush brush, in RectF rect, in CornerRadii radii, float aT, float aR, float aB, float aL, float bT, float bR, float bB, float bL, Path? wedge = null)
    {
        Path ring = RingPath(rect, radii, aT, aR, aB, aL, bT, bR, bB, bL, out IntersectionRule rule);
        if (ring.IsEmpty)
        {
            return;
        }

        if (wedge is null)
        {
            DrawPath(brush, ring, rule);
            return;
        }

        Path clip = wedge.Transform(_ctm);
        if (_clip.Path is not null)
        {
            clip = PathBoolean.Intersect(_clip.Path, clip);
        }

        if (clip.IsEmpty)
        {
            return;
        }

        _target.Fill(brush, ring, true, rule, 1f, OBlend.Normal, OneImage.AlphaComposition.SrcOver, false, _ctm, clip, IntersectionRule.NonZero);
    }

    // The quadrilateral from an outer corner pair to the matching inner corner pair (CSS Backgrounds 3 §5.3). Where the widths of
    // opposite sides exceed the box, the inner corners are squeezed together in proportion to the widths.
    private static Path Wedge(in RectF r, int side, float wT, float wR, float wB, float wL)
    {
        float x0 = r.X, y0 = r.Y, x1 = r.Right, y1 = r.Bottom;
        float ix0 = x0 + wL, ix1 = x1 - wR, iy0 = y0 + wT, iy1 = y1 - wB;
        if (ix0 > ix1)
        {
            float f = (wL + wR) > 0f ? wL / (wL + wR) : 0.5f;
            ix0 = ix1 = x0 + (r.Width * f);
        }

        if (iy0 > iy1)
        {
            float f = (wT + wB) > 0f ? wT / (wT + wB) : 0.5f;
            iy0 = iy1 = y0 + (r.Height * f);
        }

        Vector2[] p = new Vector2[4];
        switch (side)
        {
            case 0:
                p[0] = new Vector2(x0, y0);
                p[1] = new Vector2(x1, y0);
                p[2] = new Vector2(ix1, iy0);
                p[3] = new Vector2(ix0, iy0);
                break;
            case 1:
                p[0] = new Vector2(x1, y0);
                p[1] = new Vector2(x1, y1);
                p[2] = new Vector2(ix1, iy1);
                p[3] = new Vector2(ix1, iy0);
                break;
            case 2:
                p[0] = new Vector2(x1, y1);
                p[1] = new Vector2(x0, y1);
                p[2] = new Vector2(ix0, iy1);
                p[3] = new Vector2(ix1, iy1);
                break;
            default:
                p[0] = new Vector2(x0, y1);
                p[1] = new Vector2(x0, y0);
                p[2] = new Vector2(ix0, iy0);
                p[3] = new Vector2(ix0, iy1);
                break;
        }

        return Path.Polygon(p);
    }
}
