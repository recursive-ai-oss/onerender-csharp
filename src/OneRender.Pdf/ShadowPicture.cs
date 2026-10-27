using System.Buffers;
using System.Numerics;
using OneRender.Image;
using OneRender.Painting;

namespace OneRender.Pdf;

/// <summary>
/// The picture of a blurred box shadow (CSS Backgrounds 3 §7), computed directly: the coverage of the shadow shape, blurred with a
/// Gaussian whose standard deviation is half the blur radius (§7.2), in the shadow colour. Shadow pictures are small and soft, so a
/// pass over a few thousand floats replaces drawing the shape on a canvas and running the general image graph over it.
/// </summary>
internal static class ShadowPicture
{
    // A kernel reaches three deviations; beyond that a Gaussian is under 0.2% of its peak.
    private const float KernelReach = 3f;

    private const long MaxPixels = 16_000_000;

    private static bool IsFinite(in DisplayItem it)
    {
        RectF r = it.Rect;
        CornerRadii c = it.Radii;
        return float.IsFinite(r.X) && float.IsFinite(r.Y) && float.IsFinite(r.Width) && float.IsFinite(r.Height)
            && float.IsFinite(it.ShadowOffsetX) && float.IsFinite(it.ShadowOffsetY) && float.IsFinite(it.ShadowSpread) && float.IsFinite(it.ShadowBlur)
            && float.IsFinite(c.TopLeftX + c.TopLeftY + c.TopRightX + c.TopRightY + c.BottomRightX + c.BottomRightY + c.BottomLeftX + c.BottomLeftY);
    }

    /// <summary>
    /// Renders the shadow of <paramref name="it"/> into a <paramref name="width"/>×<paramref name="height"/> picture that covers
    /// <paramref name="island"/>. The box's own area is not cut out of an outer shadow, nor the outside of an inset one: the writer
    /// clips the picture with the box edge as a vector (§7.1, §7.2).
    /// </summary>
    public static byte[] Render(in DisplayItem it, RectF island, int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > MaxPixels || !float.IsFinite(island.X) || !float.IsFinite(island.Y) || !(island.Width > 0f) || !(island.Height > 0f)
            || !float.IsFinite(island.Width) || !float.IsFinite(island.Height) || !IsFinite(in it))
        {
            throw new RasterizationException("The shadow's geometry is not finite or its picture would be larger than " + MaxPixels + " pixels.");
        }

        float sx = width / island.Width, sy = height / island.Height;
        float sigma = it.ShadowBlur * 0.5f * MathF.Sqrt(sx * sy);
        RectF box = it.Rect;
        CornerRadii radii = it.Radii;
        float spread = it.ShadowSpread;
        bool inset = it.ShadowInset;

        RectF shape;
        CornerRadii shapeRadii;
        bool hasShape = true;
        if (!inset)
        {
            // The border box moved by the offset and grown by the spread; radii grow with it, a square corner stays square (§7.1).
            shape = new RectF(box.X + it.ShadowOffsetX - spread, box.Y + it.ShadowOffsetY - spread, box.Width + (2f * spread), box.Height + (2f * spread));
            shapeRadii = Grow(radii, spread);
        }
        else
        {
            // What is left of the padding box after cutting out the offset, shrunk box (§7.2).
            shape = new RectF(box.X + it.ShadowOffsetX + spread, box.Y + it.ShadowOffsetY + spread, box.Width - (2f * spread), box.Height - (2f * spread));
            shapeRadii = Grow(radii, -spread);
            hasShape = !(shape.Width <= 0f || shape.Height <= 0f);
        }

        int count = width * height;
        float[] a = ArrayPool<float>.Shared.Rent(count);
        try
        {
            Span<float> cover = a.AsSpan(0, count);
            cover.Clear();
            if (hasShape)
            {
                Coverage(cover, width, height, island, sx, sy, shape, shapeRadii);
            }

            if (inset)
            {
                for (int i = 0; i < cover.Length; i++)
                {
                    cover[i] = 1f - cover[i];
                }
            }

            Blur(cover, width, height, sigma);

            // The colour's own opacity scales the coverage; its channels are constant and come from the item when the image is embedded.
            float opacity = it.ShadowColor.A;
            byte[] alpha = new byte[count];
            for (int i = 0; i < alpha.Length; i++)
            {
                alpha[i] = Byte(cover[i] * opacity);
            }

            return alpha;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(a);
        }
    }

    private static byte Byte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static float G(float v, float by) => v > 0f ? Math.Max(0f, v + by) : 0f;

    private static CornerRadii Grow(in CornerRadii r, float by) => new(
        G(r.TopLeftX, by), G(r.TopLeftY, by), G(r.TopRightX, by), G(r.TopRightY, by),
        G(r.BottomRightX, by), G(r.BottomRightY, by), G(r.BottomLeftX, by), G(r.BottomLeftY, by));

    // The fraction of each pixel inside the rounded rectangle, from the signed distance of the pixel centre to its outline: a linear
    // ramp across one pixel, which is the exact area for a straight edge.
    private static void Coverage(Span<float> dst, int w, int h, RectF island, float sx, float sy, RectF s, CornerRadii r)
    {
        float left = s.X, top = s.Y, right = s.X + s.Width, bottom = s.Y + s.Height;
        float tlx = r.TopLeftY > 0f ? r.TopLeftX : 0f, tly = r.TopLeftX > 0f ? r.TopLeftY : 0f;
        float trx = r.TopRightY > 0f ? r.TopRightX : 0f, try_ = r.TopRightX > 0f ? r.TopRightY : 0f;
        float brx = r.BottomRightY > 0f ? r.BottomRightX : 0f, bry = r.BottomRightX > 0f ? r.BottomRightY : 0f;
        float blx = r.BottomLeftY > 0f ? r.BottomLeftX : 0f, bly = r.BottomLeftX > 0f ? r.BottomLeftY : 0f;
        float px = MathF.Sqrt(sx * sy);
        for (int j = 0; j < h; j++)
        {
            float y = island.Y + ((j + 0.5f) / sy);
            for (int i = 0; i < w; i++)
            {
                float x = island.X + ((i + 0.5f) / sx);
                float d;
                if (x < left + tlx && y < top + tly && tlx > 0f)
                {
                    d = Corner(x - (left + tlx), y - (top + tly), tlx, tly);
                }
                else if (x > right - trx && y < top + try_ && trx > 0f)
                {
                    d = Corner(x - (right - trx), y - (top + try_), trx, try_);
                }
                else if (x > right - brx && y > bottom - bry && brx > 0f)
                {
                    d = Corner(x - (right - brx), y - (bottom - bry), brx, bry);
                }
                else if (x < left + blx && y > bottom - bly && blx > 0f)
                {
                    d = Corner(x - (left + blx), y - (bottom - bly), blx, bly);
                }
                else
                {
                    float dx = MathF.Max(left - x, x - right), dy = MathF.Max(top - y, y - bottom);
                    d = dx > 0f && dy > 0f ? MathF.Sqrt((dx * dx) + (dy * dy)) : MathF.Max(dx, dy);
                }

                dst[(j * w) + i] = Math.Clamp(0.5f - (d * px), 0f, 1f);
            }
        }
    }

    // Distance from (x, y), measured from the centre of a corner ellipse, to the ellipse with radii rx and ry (positive outside):
    // the implicit function divided by the length of its gradient, which is exact for a circle.
    private static float Corner(float x, float y, float rx, float ry)
    {
        float qx = x / rx, qy = y / ry;
        float q = MathF.Sqrt((qx * qx) + (qy * qy));
        if (q < 1e-6f)
        {
            return -MathF.Min(rx, ry);
        }

        float gx = x / (rx * rx), gy = y / (ry * ry);
        float g = MathF.Sqrt((gx * gx) + (gy * gy));
        return (q - 1f) * q / g;
    }

    // e^x for x <= 0 from field operations only (a Taylor series on x/64, then six squarings), so the picture is the same bytes on every
    // platform; the library's exponential is allowed to differ in the last bit.
    private static float Exp(float x)
    {
        double t = x / 64.0;
        double term = 1, sum = 1;
        for (int n = 1; n <= 8; n++)
        {
            term *= t / n;
            sum += term;
        }

        for (int n = 0; n < 6; n++)
        {
            sum *= sum;
        }

        return (float)sum;
    }

    // A separable Gaussian, edges extended by their nearest pixel.
    private static void Blur(Span<float> a, int w, int h, float sigma)
    {
        if (!(sigma > 0.05f))
        {
            return;
        }

        int reach = Math.Clamp((int)MathF.Ceiling(KernelReach * sigma), 1, 64);
        float[] k = new float[(2 * reach) + 1];
        float sum = 0f;
        for (int i = -reach; i <= reach; i++)
        {
            float v = Exp(-(i * i) / (2f * sigma * sigma));
            k[i + reach] = v;
            sum += v;
        }

        for (int i = 0; i < k.Length; i++)
        {
            k[i] /= sum;
        }

        // Each pass adds shifted copies of whole rows scaled by one tap, which the vector unit does a row at a time. Edges repeat their
        // nearest pixel.
        float[] tmpArray = ArrayPool<float>.Shared.Rent(a.Length);
        float[] padArray = ArrayPool<float>.Shared.Rent(w + (2 * reach));
        try
        {
            Span<float> tmp = tmpArray.AsSpan(0, a.Length);
            Span<float> pad = padArray.AsSpan(0, w + (2 * reach));
            for (int j = 0; j < h; j++)
            {
                ReadOnlySpan<float> row = a.Slice(j * w, w);
                pad.Slice(0, reach).Fill(row[0]);
                row.CopyTo(pad.Slice(reach));
                pad.Slice(reach + w, reach).Fill(row[w - 1]);
                Span<float> outRow = tmp.Slice(j * w, w);
                outRow.Clear();
                for (int t = 0; t < k.Length; t++)
                {
                    Axpy(outRow, pad.Slice(t, w), k[t]);
                }
            }

            for (int j = 0; j < h; j++)
            {
                Span<float> outRow = a.Slice(j * w, w);
                outRow.Clear();
                for (int t = 0; t < k.Length; t++)
                {
                    int src = Math.Clamp(j - reach + t, 0, h - 1);
                    Axpy(outRow, tmp.Slice(src * w, w), k[t]);
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tmpArray);
            ArrayPool<float>.Shared.Return(padArray);
        }
    }

    // dst += k * src, element by element in the same order whatever the vector width, so the picture does not depend on the hardware.
    private static void Axpy(Span<float> dst, ReadOnlySpan<float> src, float k)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var vk = new Vector<float>(k);
            int n = Vector<float>.Count;
            for (; i <= dst.Length - n; i += n)
            {
                (new Vector<float>(dst.Slice(i)) + (vk * new Vector<float>(src.Slice(i)))).CopyTo(dst.Slice(i));
            }
        }

        for (; i < dst.Length; i++)
        {
            dst[i] += k * src[i];
        }
    }
}
