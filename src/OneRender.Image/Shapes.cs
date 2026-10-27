using OneRender.Painting;
using Path = OneImage.Drawing.Path;
using PathBuilder = OneImage.Drawing.PathBuilder;

namespace OneRender.Image;

/// <summary>Path construction for display-list shapes, in the item's own coordinates.</summary>
internal static class Shapes
{
    // Control-point distance for a cubic Bézier quarter ellipse: 4/3 * (sqrt(2) - 1).
    private const float Kappa = 0.5522847498f;

    public static Path Rect(in RectF r) => Path.Rectangle(r.X, r.Y, Math.Max(r.Width, 0f), Math.Max(r.Height, 0f));

    public static Path RoundedRect(in RectF r, in CornerRadii radii) =>
        RoundedRect(r.X, r.Y, r.Width, r.Height, radii.TopLeftX, radii.TopLeftY, radii.TopRightX, radii.TopRightY, radii.BottomRightX, radii.BottomRightY, radii.BottomLeftX, radii.BottomLeftY);

    public static Path RoundedRect(float x, float y, float w, float h, float tlx, float tly, float trx, float try_, float brx, float bry, float blx, float bly)
    {
        w = Math.Max(w, 0f);
        h = Math.Max(h, 0f);
        if (tlx <= 0f || tly <= 0f)
        {
            tlx = 0f;
            tly = 0f;
        }

        if (trx <= 0f || try_ <= 0f)
        {
            trx = 0f;
            try_ = 0f;
        }

        if (brx <= 0f || bry <= 0f)
        {
            brx = 0f;
            bry = 0f;
        }

        if (blx <= 0f || bly <= 0f)
        {
            blx = 0f;
            bly = 0f;
        }

        if (tlx == 0f && trx == 0f && brx == 0f && blx == 0f)
        {
            return Path.Rectangle(x, y, w, h);
        }

        float k = Kappa;
        PathBuilder b = new();
        b.MoveTo(x + tlx, y);
        b.LineTo(x + w - trx, y);
        if (trx > 0f)
        {
            b.CubicBezierTo(x + w - trx + (k * trx), y, x + w, y + try_ - (k * try_), x + w, y + try_);
        }

        b.LineTo(x + w, y + h - bry);
        if (brx > 0f)
        {
            b.CubicBezierTo(x + w, y + h - bry + (k * bry), x + w - brx + (k * brx), y + h, x + w - brx, y + h);
        }

        b.LineTo(x + blx, y + h);
        if (blx > 0f)
        {
            b.CubicBezierTo(x + blx - (k * blx), y + h, x, y + h - bly + (k * bly), x, y + h - bly);
        }

        b.LineTo(x, y + tly);
        if (tlx > 0f)
        {
            b.CubicBezierTo(x, y + tly - (k * tly), x + tlx - (k * tlx), y, x + tlx, y);
        }

        b.Close();
        return b.Build();
    }

    /// <summary>
    /// The rounded rectangle <paramref name="r"/> pulled in by a distance per side, each radius reduced by the adjacent distances
    /// and floored at zero (CSS Backgrounds 3 §5.2). The result is empty-sized (zero width or height) when the insets meet.
    /// </summary>
    public static Path Inset(in RectF r, in CornerRadii radii, float top, float right, float bottom, float left, out bool empty)
    {
        float w = r.Width - left - right, h = r.Height - top - bottom;
        empty = !(w > 0f && h > 0f);
        if (empty)
        {
            return Path.Empty;
        }

        return RoundedRect(
            r.X + left,
            r.Y + top,
            w,
            h,
            Math.Max(0f, radii.TopLeftX - left),
            Math.Max(0f, radii.TopLeftY - top),
            Math.Max(0f, radii.TopRightX - right),
            Math.Max(0f, radii.TopRightY - top),
            Math.Max(0f, radii.BottomRightX - right),
            Math.Max(0f, radii.BottomRightY - bottom),
            Math.Max(0f, radii.BottomLeftX - left),
            Math.Max(0f, radii.BottomLeftY - bottom));
    }

    public static Path FromItem(in DisplayItem it)
    {
        ReadOnlySpan<byte> verbs = it.PathVerbs;
        ReadOnlySpan<float> c = it.PathCoords;
        PathBuilder b = new();
        int p = 0;
        bool open = false;
        for (int i = 0; i < verbs.Length; i++)
        {
            switch ((PathVerb)verbs[i])
            {
                case PathVerb.Move:
                    b.MoveTo(c[p], c[p + 1]);
                    p += 2;
                    open = true;
                    break;
                case PathVerb.Line:
                    if (!open)
                    {
                        b.MoveTo(c[p], c[p + 1]);
                        open = true;
                    }
                    else
                    {
                        b.LineTo(c[p], c[p + 1]);
                    }

                    p += 2;
                    break;
                case PathVerb.Quad:
                    if (open)
                    {
                        b.QuadraticBezierTo(c[p], c[p + 1], c[p + 2], c[p + 3]);
                    }

                    p += 4;
                    break;
                case PathVerb.Cubic:
                    if (open)
                    {
                        b.CubicBezierTo(c[p], c[p + 1], c[p + 2], c[p + 3], c[p + 4], c[p + 5]);
                    }

                    p += 6;
                    break;
                default:
                    if (open)
                    {
                        b.Close();
                        open = false;
                    }

                    break;
            }
        }

        return b.Build();
    }
}
