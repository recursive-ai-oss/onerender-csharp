using OnePdf;
using OneRender.Painting;

namespace OneRender.Pdf;

/// <summary>Path construction shared by the leaf painters. All coordinates are CSS px in the current user space.</summary>
internal static class Geo
{
    // Control-point distance of a cubic that approximates a quarter ellipse (4/3 · (√2 − 1)).
    private const double Kappa = 0.5522847498307936;

    public static void RoundedRect(PdfContentBuilder b, double x, double y, double w, double h, in CornerRadii r)
    {
        if (r.IsZero)
        {
            b.Rectangle(x, y, w, h);
            return;
        }

        double tlx = Math.Max(0, r.TopLeftX), tly = Math.Max(0, r.TopLeftY);
        double trx = Math.Max(0, r.TopRightX), try_ = Math.Max(0, r.TopRightY);
        double brx = Math.Max(0, r.BottomRightX), bry = Math.Max(0, r.BottomRightY);
        double blx = Math.Max(0, r.BottomLeftX), bly = Math.Max(0, r.BottomLeftY);
        double right = x + w, bottom = y + h;
        b.MoveTo(x + tlx, y);
        b.LineTo(right - trx, y);
        if (trx > 0 && try_ > 0)
        {
            b.CurveTo(right - trx + (Kappa * trx), y, right, y + try_ - (Kappa * try_), right, y + try_);
        }

        b.LineTo(right, bottom - bry);
        if (brx > 0 && bry > 0)
        {
            b.CurveTo(right, bottom - bry + (Kappa * bry), right - brx + (Kappa * brx), bottom, right - brx, bottom);
        }

        b.LineTo(x + blx, bottom);
        if (blx > 0 && bly > 0)
        {
            b.CurveTo(x + blx - (Kappa * blx), bottom, x, bottom - bly + (Kappa * bly), x, bottom - bly);
        }

        b.LineTo(x, y + tly);
        if (tlx > 0 && tly > 0)
        {
            b.CurveTo(x, y + tly - (Kappa * tly), x + tlx - (Kappa * tlx), y, x + tlx, y);
        }

        b.ClosePath();
    }

    /// <summary>The inner radii of a border (CSS Backgrounds 3 §5.2): each outer radius minus the width of the adjacent sides, not below zero.</summary>
    public static CornerRadii Inner(in CornerRadii r, double top, double right, double bottom, double left) => new(
        (float)Math.Max(0, r.TopLeftX - left), (float)Math.Max(0, r.TopLeftY - top),
        (float)Math.Max(0, r.TopRightX - right), (float)Math.Max(0, r.TopRightY - top),
        (float)Math.Max(0, r.BottomRightX - right), (float)Math.Max(0, r.BottomRightY - bottom),
        (float)Math.Max(0, r.BottomLeftX - left), (float)Math.Max(0, r.BottomLeftY - bottom));

    /// <summary>Writes the verbs of a path; quadratic segments are raised to cubic (a PDF path has no quadratic operator).</summary>
    public static void Verbs(PdfContentBuilder b, ReadOnlySpan<byte> verbs, ReadOnlySpan<float> c)
    {
        int p = 0;
        double cx = 0, cy = 0, sx = 0, sy = 0;
        foreach (byte v in verbs)
        {
            switch ((PathVerb)v)
            {
                case PathVerb.Move:
                    cx = sx = c[p];
                    cy = sy = c[p + 1];
                    b.MoveTo(cx, cy);
                    p += 2;
                    break;
                case PathVerb.Line:
                    cx = c[p];
                    cy = c[p + 1];
                    b.LineTo(cx, cy);
                    p += 2;
                    break;
                case PathVerb.Quad:
                    {
                        double qx = c[p], qy = c[p + 1], ex = c[p + 2], ey = c[p + 3];
                        b.CurveTo(cx + ((2.0 / 3.0) * (qx - cx)), cy + ((2.0 / 3.0) * (qy - cy)), ex + ((2.0 / 3.0) * (qx - ex)), ey + ((2.0 / 3.0) * (qy - ey)), ex, ey);
                        cx = ex;
                        cy = ey;
                        p += 4;
                        break;
                    }

                case PathVerb.Cubic:
                    b.CurveTo(c[p], c[p + 1], c[p + 2], c[p + 3], c[p + 4], c[p + 5]);
                    cx = c[p + 4];
                    cy = c[p + 5];
                    p += 6;
                    break;
                case PathVerb.Close:
                    b.ClosePath();
                    cx = sx;
                    cy = sy;
                    break;
            }
        }
    }

    /// <summary>The 0..1 channel of a color as a PDF number, premultiplication not applied.</summary>
    public static void SetFill(PdfContentBuilder b, in PaintColor c) => b.SetFillRgb(c.R, c.G, c.B);

    public static void SetStroke(PdfContentBuilder b, in PaintColor c) => b.SetStrokeRgb(c.R, c.G, c.B);

    public static PdfBlendMode Blend(BlendMode m, out bool exact)
    {
        exact = true;
        switch (m)
        {
            case BlendMode.Multiply: return PdfBlendMode.Multiply;
            case BlendMode.Screen: return PdfBlendMode.Screen;
            case BlendMode.Overlay: return PdfBlendMode.Overlay;
            case BlendMode.Darken: return PdfBlendMode.Darken;
            case BlendMode.Lighten: return PdfBlendMode.Lighten;
            case BlendMode.ColorDodge: return PdfBlendMode.ColorDodge;
            case BlendMode.ColorBurn: return PdfBlendMode.ColorBurn;
            case BlendMode.HardLight: return PdfBlendMode.HardLight;
            case BlendMode.SoftLight: return PdfBlendMode.SoftLight;
            case BlendMode.Difference: return PdfBlendMode.Difference;
            case BlendMode.Exclusion: return PdfBlendMode.Exclusion;
            case BlendMode.Normal: return PdfBlendMode.Normal;
            default:
                exact = false; // Hue, Saturation, Color, Luminosity and PlusLighter have no operator in OnePdf's writer.
                return PdfBlendMode.Normal;
        }
    }
}
