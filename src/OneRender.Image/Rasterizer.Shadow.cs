using System.Numerics;
using OneRender.Painting;
using Brush = OneImage.Drawing.Brush;
using Canvas = OneImage.Canvas;
using IntersectionRule = OneImage.Drawing.IntersectionRule;
using Path = OneImage.Drawing.Path;
using PathBoolean = OneImage.Drawing.PathBoolean;
using PixelFormat = OneImage.PixelFormat;
using Rgba32 = OneImage.Rgba32;

namespace OneRender.Image;

internal sealed partial class Rasterizer
{
    private const float MaxSigma = 256f;

    // CSS Backgrounds 3 §7. The shadow shape is drawn as an opaque mask into a small layer, blurred with a Gaussian whose
    // standard deviation is half the blur radius (§7.2), and the result is laid down in the shadow color. The layer holds
    // only a coverage value, so a colored shadow never fringes.
    private void DrawShadow(in DisplayItem it)
    {
        PaintColor color = it.ShadowColor;
        if (color.IsTransparent || _clip.IsEmpty)
        {
            return;
        }

        RectF box = it.Rect;
        CornerRadii radii = it.Radii;
        float dx = it.ShadowOffsetX, dy = it.ShadowOffsetY, spread = it.ShadowSpread;
        float scale = MathF.Sqrt(MathF.Abs(_ctm.GetDeterminant()));
        float sigma = Math.Min(it.ShadowBlur * 0.5f * scale, MaxSigma);
        bool blurred = sigma > 0.05f;
        int margin = blurred ? (int)MathF.Ceiling(3f * sigma) : 0;

        if (it.ShadowInset)
        {
            DrawInsetShadow(color, box, radii, dx, dy, spread, sigma, blurred, margin);
        }
        else
        {
            DrawOuterShadow(color, box, radii, dx, dy, spread, sigma, blurred, margin);
        }
    }

    private void DrawOuterShadow(in PaintColor color, in RectF box, in CornerRadii radii, float dx, float dy, float spread, float sigma, bool blurred, int margin)
    {
        // The shadow shape is the border box moved by the offset and grown by the spread; radii grow with it but a square
        // corner stays square (§7.1).
        RectF s = new(box.X + dx - spread, box.Y + dy - spread, box.Width + (2f * spread), box.Height + (2f * spread));
        if (s.IsEmpty)
        {
            return;
        }

        Path shape = Shapes.RoundedRect(s, GrowRadii(radii, spread));
        Path boxDevice = Shapes.RoundedRect(box, radii).Transform(_ctm);
        RectF dev = s.Transform(_ctm);
        RectI region = LayerRegion(dev, margin);
        if (region.IsEmpty)
        {
            return;
        }

        // Only the area outside the border box is shadowed (§7.1: the shadow is clipped inside the border-box edge).
        Path basis = _clip.Path ?? Path.Rectangle(region.X, region.Y, region.Width, region.Height);
        Path clip = PathBoolean.Subtract(basis, boxDevice);
        if (clip.IsEmpty)
        {
            return;
        }

        if (!blurred)
        {
            Brush solid = Brush.Solid(ToRgba(color));
            _target.Fill(solid, shape, true, IntersectionRule.NonZero, 1f, OneImage.BlendMode.Normal, OneImage.AlphaComposition.SrcOver, false, _ctm, clip, IntersectionRule.NonZero);
            return;
        }

        byte[] mask = BlurredMask(region, sigma, white: false, shape, null, _ctm);
        FillMask(color, mask, region, clip);
    }

    private void DrawInsetShadow(in PaintColor color, in RectF box, in CornerRadii radii, float dx, float dy, float spread, float sigma, bool blurred, int margin)
    {
        // The inset shadow is what is left of the padding box after cutting out the offset, shrunk box (§7.1).
        RectF hole = new(box.X + dx + spread, box.Y + dy + spread, box.Width - (2f * spread), box.Height - (2f * spread));
        Path padDevice = Shapes.RoundedRect(box, radii).Transform(_ctm);
        RectF padDev = box.Transform(_ctm);
        RectI region = LayerRegion(padDev, margin);
        if (region.IsEmpty)
        {
            return;
        }

        Path basis = _clip.Path ?? Path.Rectangle(region.X, region.Y, region.Width, region.Height);
        Path clip = PathBoolean.Intersect(basis, padDevice);
        if (clip.IsEmpty)
        {
            return;
        }

        Path? holePath = hole.IsEmpty ? null : Shapes.RoundedRect(hole, GrowRadii(radii, -spread));
        if (!blurred)
        {
            Brush solid = Brush.Solid(ToRgba(color));
            if (holePath is null)
            {
                _target.Fill(solid, Shapes.Rect(box), true, IntersectionRule.NonZero, 1f, OneImage.BlendMode.Normal, OneImage.AlphaComposition.SrcOver, false, _ctm, clip, IntersectionRule.NonZero);
            }
            else
            {
                float grow = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(spread) + 1f;
                Path frame = Path.Combine(Shapes.Rect(box.Inflate(grow)), holePath);
                _target.Fill(solid, frame, true, IntersectionRule.EvenOdd, 1f, OneImage.BlendMode.Normal, OneImage.AlphaComposition.SrcOver, false, _ctm, clip, IntersectionRule.NonZero);
            }

            return;
        }

        byte[] mask = BlurredMask(region, sigma, white: true, holePath, null, _ctm);
        FillMask(color, mask, region, clip);
    }

    // A spread moves every edge by the same distance, so radii change by it too; a corner with no radius stays square.
    private static CornerRadii GrowRadii(in CornerRadii r, float by) => new(
        G(r.TopLeftX, by), G(r.TopLeftY, by), G(r.TopRightX, by), G(r.TopRightY, by),
        G(r.BottomRightX, by), G(r.BottomRightY, by), G(r.BottomLeftX, by), G(r.BottomLeftY, by));

    private static float G(float v, float by) => v > 0f ? Math.Max(0f, v + by) : 0f;

    // The layer rectangle: the shape's device bounds plus the blur margin, limited to the surface plus the margin so that
    // pixels inside the surface are never closer than three sigma to an artificial layer edge.
    private RectI LayerRegion(in RectF dev, int margin)
    {
        if (dev.IsEmpty)
        {
            return default;
        }

        long x0 = (long)Math.Floor(Math.Max(dev.X, -1e8f)) - margin, y0 = (long)Math.Floor(Math.Max(dev.Y, -1e8f)) - margin;
        long x1 = (long)Math.Ceiling(Math.Min(dev.Right, 1e8f)) + margin, y1 = (long)Math.Ceiling(Math.Min(dev.Bottom, 1e8f)) + margin;
        x0 = Math.Max(x0, -margin);
        y0 = Math.Max(y0, -margin);
        x1 = Math.Min(x1, _width + margin);
        y1 = Math.Min(y1, _height + margin);
        if (x1 <= x0 || y1 <= y0)
        {
            return default;
        }

        if ((x1 - x0) * (y1 - y0) > MaxPixels)
        {
            throw new RasterizationException("A shadow layer would be larger than " + MaxPixels + " pixels.");
        }

        return new RectI((int)x0, (int)y0, (int)(x1 - x0), (int)(y1 - y0));
    }

    private const long MaxPixels = 1L << 26;

    // Renders the shape as 255 on black (or, when white is true, the complement: 255 everywhere except inside the shape), blurs it
    // and returns the 8-bit coverage.
    private static byte[] BlurredMask(in RectI region, float sigma, bool white, Path? shape, Path? unused, in Matrix3x2 ctm)
    {
        _ = unused;
        using Canvas layer = new(region.Width, region.Height, PixelFormat.Rgba8);
        Rgba32 hi = new(255, 255, 255, 255), lo = new(0, 0, 0, 255);
        layer.Fill(white ? hi : lo, 1f, OneImage.BlendMode.Normal, OneImage.AlphaComposition.Src);
        if (shape is not null)
        {
            Matrix3x2 m = ctm * Matrix3x2.CreateTranslation(-region.X, -region.Y);
            layer.Fill(white ? lo : hi, shape, true, IntersectionRule.NonZero, 1f, OneImage.BlendMode.Normal, OneImage.AlphaComposition.Src, false, m);
        }

        // oneimage's wide-sigma blur works on a reduced copy and never terminates when the layer is only a few pixels across
        // relative to sigma, so each axis' deviation is limited to 12 times the layer's extent along it. Beyond that the
        // blur is flat across the layer anyway.
        double sx = Math.Min(sigma, 12.0 * region.Width), sy = Math.Min(sigma, 12.0 * region.Height);
        OneImage.Image blurredImage = layer.Snapshot().GaussianBlur(sx, sy, false);
        Rgba32[] px = blurredImage.ToArray<Rgba32>();
        byte[] mask = new byte[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            mask[i] = px[i].R;
        }

        return mask;
    }

    private void FillMask(in PaintColor color, byte[] mask, in RectI region, Path clip)
    {
        Brush brush = new MaskBrush(color, mask, region.X, region.Y, region.Width, region.Height);
        int x0 = Math.Max(region.X, 0), y0 = Math.Max(region.Y, 0);
        int x1 = Math.Min(region.Right, _width), y1 = Math.Min(region.Bottom, _height);
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }

        _target.Fill(brush, Path.Rectangle(x0, y0, x1 - x0, y1 - y0), true, IntersectionRule.NonZero, 1f, OneImage.BlendMode.Normal, OneImage.AlphaComposition.SrcOver, false, null, clip, IntersectionRule.NonZero);
    }
}
