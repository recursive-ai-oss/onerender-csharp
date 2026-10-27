using System.Numerics;
using OneRender.Painting;
using AlphaComposition = OneImage.AlphaComposition;
using Brush = OneImage.Drawing.Brush;
using Canvas = OneImage.Canvas;
using ImageRect = OneImage.ImageRect;
using IntersectionRule = OneImage.Drawing.IntersectionRule;
using OBlend = OneImage.BlendMode;
using Path = OneImage.Drawing.Path;
using PixelFormat = OneImage.PixelFormat;
using Rgba32 = OneImage.Rgba32;

namespace OneRender.Image;

// Layers: groups, filters, masks, projections. A layer is a canvas the size of the bounds of its content (clipped to the
// surface), not of the whole surface, so a small translucent box costs a small canvas.
internal sealed partial class Rasterizer
{
    private const long MaxProjectedPixels = 1L << 24;

    // Fills in the layer part of the saved frame and swaps in a fresh canvas. Returns false when the content has no pixels to show.
    private bool BeginLayer(int index, in DisplayItem it, ItemKind kind, bool projection, out int skipTo)
    {
        skipTo = it.MatchIndex;
        if (_clip.IsEmpty || _groups >= MaxOpenGroups)
        {
            if (_groups >= MaxOpenGroups && !_clip.IsEmpty)
            {
                throw new RasterizationException("Groups, filters and masks are nested more than " + MaxOpenGroups + " deep.");
            }

            return false;
        }

        RectI region = RegionOf(index, it.MatchIndex, projection);
        if (region.IsEmpty)
        {
            return false;
        }

        long px = (long)region.Width * region.Height;
        if (_layerPixels + px > MaxLayerPixels)
        {
            throw new RasterizationException("Layers would use more than " + MaxLayerPixels + " pixels.");
        }

        Save(kind);
        ref Frame f = ref _stack[_sp - 1];
        f.Region = region;
        f.Item = index;
        _groups++;
        _layerPixels += px;
        if (projection)
        {
            // The content is drawn flat in its own plane at device scale, then warped.
            _off = (region.X, region.Y);
            _ctm = Matrix3x2.CreateScale(_dpr) * Matrix3x2.CreateTranslation(-region.X, -region.Y);
            _in3D = true;
        }
        else
        {
            _off = (_off.X + region.X, _off.Y + region.Y);
            _ctm *= Matrix3x2.CreateTranslation(-region.X, -region.Y);
        }

        _target = new Canvas(region.Width, region.Height, PixelFormat.Rgba8);
        _width = region.Width;
        _height = region.Height;
        _visible = new RectF(_off.X / _dpr, _off.Y / _dpr, _width / _dpr, _height / _dpr);

        // The enclosing clip applies once, when the finished layer is composited; inside only new clips count.
        _clip = default;
        return true;
    }

    // The pixels (in the current target) that the items of [index + 1, match) can touch.
    private RectI RegionOf(int index, int match, bool projection)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int j = index + 1; j < match; j++)
        {
            RectF b = _list[j].LayerBounds;
            if (b.IsEmpty)
            {
                continue;
            }

            x0 = Math.Min(x0, b.X);
            y0 = Math.Min(y0, b.Y);
            x1 = Math.Max(x1, b.Right);
            y1 = Math.Max(y1, b.Bottom);
        }

        if (x1 <= x0 || y1 <= y0)
        {
            return default;
        }

        // To device pixels of the base space (root space, or the content plane inside a projection).
        long dx0 = (long)Math.Floor(Math.Max(x0 * _dpr, -1e8f)), dy0 = (long)Math.Floor(Math.Max(y0 * _dpr, -1e8f));
        long dx1 = (long)Math.Ceiling(Math.Min(x1 * _dpr, 1e8f)), dy1 = (long)Math.Ceiling(Math.Min(y1 * _dpr, 1e8f));
        if (projection)
        {
            dx1 = Math.Min(dx1, dx0 + 8192);
            dy1 = Math.Min(dy1, dy0 + 8192);
            return dx1 <= dx0 || dy1 <= dy0 || (dx1 - dx0) * (dy1 - dy0) > MaxProjectedPixels ? default : new RectI((int)dx0, (int)dy0, (int)(dx1 - dx0), (int)(dy1 - dy0));
        }

        // Into the current target's pixels, limited to what can be seen.
        dx0 -= _off.X;
        dy0 -= _off.Y;
        dx1 -= _off.X;
        dy1 -= _off.Y;
        long vx0 = (long)Math.Floor(_visible.X * _dpr) - _off.X, vy0 = (long)Math.Floor(_visible.Y * _dpr) - _off.Y;
        long vx1 = (long)Math.Ceiling(_visible.Right * _dpr) - _off.X, vy1 = (long)Math.Ceiling(_visible.Bottom * _dpr) - _off.Y;
        dx0 = Math.Max(Math.Max(dx0, vx0), 0);
        dy0 = Math.Max(Math.Max(dy0, vy0), 0);
        dx1 = Math.Min(Math.Min(dx1, vx1), _width);
        dy1 = Math.Min(Math.Min(dy1, vy1), _height);
        return dx1 <= dx0 || dy1 <= dy0 ? default : new RectI((int)dx0, (int)dy0, (int)(dx1 - dx0), (int)(dy1 - dy0));
    }

    // Closes the innermost layer frame and returns it with its canvas; the target is the parent's again.
    private Frame EndLayer(out Canvas layer)
    {
        Frame f = _stack[--_sp];
        layer = _target;
        _target = f.Target;
        _ctm = f.Ctm;
        _clip = f.Clip;
        _off = f.Off;
        _width = f.W;
        _height = f.H;
        _visible = f.Visible;
        _in3D = f.In3D;
        _groups--;
        _layerPixels -= (long)f.Region.Width * f.Region.Height;
        return f;
    }

    // Composites a finished layer canvas, placed at region (in the parent's pixels), onto the parent.
    private void Composite(Canvas layer, in RectI region, float opacity, OBlend blend, bool plus)
    {
        if (_clip.IsEmpty)
        {
            return;
        }

        OneImage.Image snapshot = layer.Snapshot();
        Brush brush = Brush.FromImage(snapshot, OneImage.Drawing.ImageSampling.Nearest, null, region.X, region.Y, null, false);
        Path area = Path.Rectangle(region.X, region.Y, region.Width, region.Height);
        _target.Fill(brush, area, false, IntersectionRule.NonZero, opacity, blend, plus ? AlphaComposition.Plus : AlphaComposition.SrcOver, false, null, _clip.Path, IntersectionRule.NonZero);
    }

    private void PushGroup(int index, in DisplayItem it)
    {
        if (!BeginLayer(index, it, ItemKind.PushGroup, false, out _skip))
        {
            _next = _skip;
            return;
        }

        ref Frame f = ref _stack[_sp - 1];
        f.Opacity = it.Opacity;
        (f.Blend, f.Plus) = MapBlend(it.Blend);
    }

    private void PopGroup()
    {
        Frame f = EndLayer(out Canvas layer);
        try
        {
            Composite(layer, f.Region, f.Opacity, f.Blend, f.Plus);
        }
        finally
        {
            layer.Dispose();
        }
    }

    // ---------------------------------------------------------------- filters

    private void PushFilter(int index, in DisplayItem it)
    {
        if (!BeginLayer(index, it, ItemKind.PushFilter, false, out _skip))
        {
            _next = _skip;
            return;
        }

        // A scale of lengths from CSS px to device pixels, at the point where the filter starts.
        _stack[_sp - 1].Scale = ScaleOf(_stack[_sp - 1].Ctm);
    }

    private static float ScaleOf(in Matrix3x2 m) => MathF.Sqrt(MathF.Abs((m.M11 * m.M22) - (m.M12 * m.M21)));

    private void PopFilter()
    {
        Frame f = EndLayer(out Canvas layer);
        try
        {
            DisplayItem push = _list[f.Item];
            int n = push.FilterOpCount;
            float[] px = Read(layer, f.Region.Width, f.Region.Height);
            for (int i = 0; i < n; i++)
            {
                FilterOp op = push.GetFilterOp(i);
                px = ApplyOp(px, f.Region.Width, f.Region.Height, op, f.Scale);
            }

            Write(layer, px, f.Region.Width, f.Region.Height);
            Composite(layer, f.Region, 1f, OBlend.Normal, false);
        }
        finally
        {
            layer.Dispose();
        }
    }

    private void BackdropFilter(in DisplayItem it)
    {
        if (_clip.IsEmpty)
        {
            return;
        }

        int n = it.FilterOpCount;
        float scale = ScaleOf(_ctm);
        float margin = 0f;
        for (int i = 0; i < n; i++)
        {
            FilterOp op = it.GetFilterOp(i);
            margin += op.Kind == FilterOpKind.Blur ? 3f * op.A * scale : op.Kind == FilterOpKind.DropShadow ? 3f * op.C * scale : 0f;
        }

        RectF dev = it.Rect.Transform(_ctm);
        int m = (int)Math.Ceiling(Math.Min(margin, 4096f));
        long x0 = Math.Max((long)Math.Floor(dev.X) - m, 0), y0 = Math.Max((long)Math.Floor(dev.Y) - m, 0);
        long x1 = Math.Min((long)Math.Ceiling(dev.Right) + m, _width), y1 = Math.Min((long)Math.Ceiling(dev.Bottom) + m, _height);
        if (x1 <= x0 || y1 <= y0 || (x1 - x0) * (y1 - y0) > MaxLayerPixels)
        {
            return;
        }

        var region = new RectI((int)x0, (int)y0, (int)(x1 - x0), (int)(y1 - y0));
        var rect = new ImageRect(region.X, region.Y, region.Width, region.Height);
        Rgba32[] src = new Rgba32[region.Width * region.Height];
        _target.CopyTo<Rgba32>(src, rect);
        float[] px = ToFloat(src);
        for (int i = 0; i < n; i++)
        {
            px = ApplyOp(px, region.Width, region.Height, it.GetFilterOp(i), scale);
        }

        using Canvas tmp = new(region.Width, region.Height, PixelFormat.Rgba8);
        Write(tmp, px, region.Width, region.Height);
        Brush brush = Brush.FromImage(tmp.Snapshot(), OneImage.Drawing.ImageSampling.Nearest, null, region.X, region.Y, null, false);
        Path shape = Shapes.RoundedRect(it.Rect, it.Radii);
        _target.Fill(brush, shape, true, IntersectionRule.NonZero, 1f, OBlend.Normal, AlphaComposition.Src, false, _ctm, _clip.Path, IntersectionRule.NonZero);
    }

    // ---------------------------------------------------------------- masks

    private void PushMask(int index, in DisplayItem it)
    {
        if (!BeginLayer(index, it, ItemKind.PushMask, false, out _skip))
        {
            _next = _skip;
            return;
        }

        ref Frame f = ref _stack[_sp - 1];
        f.Coverage = new float[f.Region.Width * f.Region.Height];
    }

    private void PushMaskLayer(int index, in DisplayItem it)
    {
        // The layer canvas has the size of the masked content's layer, so coverage maps one to one.
        if (_sp == 0 || _stack[_sp - 1].Kind != ItemKind.PushMask)
        {
            _next = it.MatchIndex;
            return;
        }

        Save(ItemKind.PushMaskLayer);
        ref Frame f = ref _stack[_sp - 1];
        f.Region = new RectI(0, 0, _width, _height);
        f.Item = index;
        _target = new Canvas(_width, _height, PixelFormat.Rgba8);
        _clip = default;
        _groups++;
        _layerPixels += (long)_width * _height;
    }

    private void PopMaskLayer()
    {
        Frame f = _stack[--_sp];
        Canvas layer = _target;
        _target = f.Target;
        _ctm = f.Ctm;
        _clip = f.Clip;
        _groups--;
        _layerPixels -= (long)f.Region.Width * f.Region.Height;
        try
        {
            DisplayItem push = _list[f.Item];
            ref Frame mask = ref _stack[_sp - 1];
            float[] src = Read(layer, f.Region.Width, f.Region.Height);
            float[] acc = mask.Coverage!;
            bool lum = push.MaskMode == MaskLayerMode.Luminance;
            MaskCompositeOp op = push.MaskComposite;
            for (int i = 0; i < acc.Length; i++)
            {
                float a = src[(i * 4) + 3];
                float s = a;
                if (lum && a > 0f)
                {
                    // Straight sRGB luminance times alpha (Masking 1 §6.2).
                    s = ((0.2126f * src[i * 4]) + (0.7152f * src[(i * 4) + 1]) + (0.0722f * src[(i * 4) + 2]));
                }

                float d = acc[i];
                acc[i] = op switch
                {
                    MaskCompositeOp.Subtract => s * (1f - d),
                    MaskCompositeOp.Intersect => s * d,
                    MaskCompositeOp.Exclude => (s * (1f - d)) + (d * (1f - s)),
                    _ => s + (d * (1f - s)),
                };
            }
        }
        finally
        {
            layer.Dispose();
        }
    }

    private void PopMask()
    {
        Frame f = EndLayer(out Canvas layer);
        try
        {
            float[] px = Read(layer, f.Region.Width, f.Region.Height);
            float[] cov = f.Coverage!;
            for (int i = 0; i < cov.Length; i++)
            {
                float c = Math.Clamp(cov[i], 0f, 1f);
                px[i * 4] *= c;
                px[(i * 4) + 1] *= c;
                px[(i * 4) + 2] *= c;
                px[(i * 4) + 3] *= c;
            }

            Write(layer, px, f.Region.Width, f.Region.Height);
            Composite(layer, f.Region, 1f, OBlend.Normal, false);
        }
        finally
        {
            layer.Dispose();
        }
    }

    // ---------------------------------------------------------------- perspective

    private void PushProjection(int index, in DisplayItem it)
    {
        if (!BeginLayer(index, it, ItemKind.PushTransform3D, true, out _skip))
        {
            _next = _skip;
            return;
        }

        _stack[_sp - 1].Proj = it.Matrix4;
    }

    private void PopProjection()
    {
        Frame f = EndLayer(out Canvas layer);
        try
        {
            if (_clip.IsEmpty)
            {
                return;
            }

            Matrix4x4 m = f.Proj;
            Matrix3x2 parent = f.Ctm;
            if (!Matrix3x2.Invert(parent, out Matrix3x2 inv))
            {
                return;
            }

            // Destination bounds: the layer's corners through the projection and the parent's matrix.
            float lx0 = f.Region.X / _dpr, ly0 = f.Region.Y / _dpr, lx1 = f.Region.Right / _dpr, ly1 = f.Region.Bottom / _dpr;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool unbounded = false;
            for (int i = 0; i < 4; i++)
            {
                float cx = (i & 1) == 0 ? lx0 : lx1, cy = (i & 2) == 0 ? ly0 : ly1;
                if (!TransformGeometry3.Map(in m, cx, cy, out float px, out float py))
                {
                    unbounded = true;
                    break;
                }

                Vector2 d = Vector2.Transform(new Vector2(px, py), parent);
                minX = Math.Min(minX, d.X);
                maxX = Math.Max(maxX, d.X);
                minY = Math.Min(minY, d.Y);
                maxY = Math.Max(maxY, d.Y);
            }

            long x0 = unbounded ? 0 : Math.Max((long)Math.Floor(minX), 0), y0 = unbounded ? 0 : Math.Max((long)Math.Floor(minY), 0);
            long x1 = unbounded ? _width : Math.Min((long)Math.Ceiling(maxX), _width), y1 = unbounded ? _height : Math.Min((long)Math.Ceiling(maxY), _height);
            if (_clip.IsRect)
            {
                x0 = Math.Max(x0, (long)Math.Floor(_clip.Rect.X));
                y0 = Math.Max(y0, (long)Math.Floor(_clip.Rect.Y));
                x1 = Math.Min(x1, (long)Math.Ceiling(_clip.Rect.Right));
                y1 = Math.Min(y1, (long)Math.Ceiling(_clip.Rect.Bottom));
            }

            if (x1 <= x0 || y1 <= y0 || (x1 - x0) * (y1 - y0) > MaxProjectedPixels)
            {
                return;
            }

            int w = (int)(x1 - x0), h = (int)(y1 - y0);
            Rgba32[] src = new Rgba32[f.Region.Width * f.Region.Height];
            layer.CopyTo<Rgba32>(src, new ImageRect(0, 0, f.Region.Width, f.Region.Height));
            Rgba32[] dst = new Rgba32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Destination pixel center, back through the parent's matrix, then the plane's homography, then to layer pixels.
                    Vector2 p = Vector2.Transform(new Vector2((float)x0 + x + 0.5f, (float)y0 + y + 0.5f), inv);
                    if (!TransformGeometry3.Unmap(in m, p.X, p.Y, out float u, out float v))
                    {
                        continue;
                    }

                    dst[(y * w) + x] = Sample(src, f.Region.Width, f.Region.Height, (u * _dpr) - f.Region.X, (v * _dpr) - f.Region.Y);
                }
            }

            using Canvas warped = new(w, h, PixelFormat.Rgba8);
            warped.CopyFrom<Rgba32>(dst, new ImageRect(0, 0, w, h));
            Composite(warped, new RectI((int)x0, (int)y0, w, h), 1f, OBlend.Normal, false);
        }
        finally
        {
            layer.Dispose();
        }
    }

    // Bilinear sample of straight 8-bit RGBA at a continuous position (pixel centers at +0.5), edges transparent.
    private static Rgba32 Sample(Rgba32[] src, int w, int h, float x, float y)
    {
        float fx = x - 0.5f, fy = y - 0.5f;
        int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
        float tx = fx - ix, ty = fy - iy;
        float r = 0, g = 0, b = 0, a = 0;
        for (int j = 0; j < 2; j++)
        {
            for (int i = 0; i < 2; i++)
            {
                int px = ix + i, py = iy + j;
                float wgt = (i == 0 ? 1 - tx : tx) * (j == 0 ? 1 - ty : ty);
                if (px < 0 || py < 0 || px >= w || py >= h || wgt <= 0f)
                {
                    continue;
                }

                Rgba32 s = src[(py * w) + px];
                float sa = s.A / 255f * wgt;
                r += s.R * sa;
                g += s.G * sa;
                b += s.B * sa;
                a += sa;
            }
        }

        if (a <= 0f)
        {
            return default;
        }

        return new Rgba32((byte)Math.Clamp(MathF.Round(r / a), 0, 255), (byte)Math.Clamp(MathF.Round(g / a), 0, 255), (byte)Math.Clamp(MathF.Round(b / a), 0, 255), (byte)Math.Clamp(MathF.Round(a * 255f), 0, 255));
    }

    // ---------------------------------------------------------------- pixel buffers (premultiplied float RGBA)

    private static float[] Read(Canvas c, int w, int h)
    {
        Rgba32[] px = new Rgba32[w * h];
        c.CopyTo<Rgba32>(px, new ImageRect(0, 0, w, h));
        return ToFloat(px);
    }

    private static float[] ToFloat(Rgba32[] px)
    {
        float[] f = new float[px.Length * 4];
        for (int i = 0; i < px.Length; i++)
        {
            float a = px[i].A / 255f;
            f[i * 4] = px[i].R / 255f * a;
            f[(i * 4) + 1] = px[i].G / 255f * a;
            f[(i * 4) + 2] = px[i].B / 255f * a;
            f[(i * 4) + 3] = a;
        }

        return f;
    }

    private static void Write(Canvas c, float[] f, int w, int h)
    {
        Rgba32[] px = new Rgba32[w * h];
        for (int i = 0; i < px.Length; i++)
        {
            float a = Math.Clamp(f[(i * 4) + 3], 0f, 1f);
            if (a <= 0f)
            {
                continue;
            }

            px[i] = new Rgba32(
                (byte)Math.Clamp(MathF.Round(f[i * 4] / a * 255f), 0f, 255f),
                (byte)Math.Clamp(MathF.Round(f[(i * 4) + 1] / a * 255f), 0f, 255f),
                (byte)Math.Clamp(MathF.Round(f[(i * 4) + 2] / a * 255f), 0f, 255f),
                (byte)Math.Clamp(MathF.Round(a * 255f), 0f, 255f));
        }

        c.CopyFrom<Rgba32>(px, new ImageRect(0, 0, w, h));
    }

    // ---------------------------------------------------------------- filter functions (Filter Effects 1 §5; sRGB)

    private static float[] ApplyOp(float[] px, int w, int h, in FilterOp op, float scale)
    {
        switch (op.Kind)
        {
            case FilterOpKind.Blur:
                return Blur(px, w, h, op.A * scale);
            case FilterOpKind.DropShadow:
                return DropShadow(px, w, h, op, scale);
            case FilterOpKind.Brightness:
                Scale(px, op.A);
                return px;
            case FilterOpKind.Opacity:
                {
                    float k = Math.Clamp(op.A, 0f, 1f);
                    for (int i = 0; i < px.Length; i++)
                    {
                        px[i] *= k;
                    }

                    return px;
                }
            case FilterOpKind.Contrast:
                {
                    // Per straight channel c' = (c − ½)·k + ½, which on premultiplied values is p' = k·p + a·(½ − ½k).
                    float k = op.A;
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        float a = px[i + 3];
                        for (int c = 0; c < 3; c++)
                        {
                            px[i + c] = Math.Clamp((k * px[i + c]) + (a * (0.5f - (0.5f * k))), 0f, a);
                        }
                    }

                    return px;
                }

            case FilterOpKind.Invert:
                {
                    float k = Math.Clamp(op.A, 0f, 1f);
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        float a = px[i + 3];
                        for (int c = 0; c < 3; c++)
                        {
                            px[i + c] = Math.Clamp(((1f - (2f * k)) * px[i + c]) + (k * a), 0f, a);
                        }
                    }

                    return px;
                }

            case FilterOpKind.Grayscale:
                {
                    float a = 1f - Math.Clamp(op.A, 0f, 1f);
                    return Matrix(px, 0.2126f + (0.7874f * a), 0.7152f - (0.7152f * a), 0.0722f - (0.0722f * a), 0.2126f - (0.2126f * a), 0.7152f + (0.2848f * a), 0.0722f - (0.0722f * a), 0.2126f - (0.2126f * a), 0.7152f - (0.7152f * a), 0.0722f + (0.9278f * a));
                }

            case FilterOpKind.Sepia:
                {
                    float a = 1f - Math.Clamp(op.A, 0f, 1f);
                    return Matrix(px, 0.393f + (0.607f * a), 0.769f - (0.769f * a), 0.189f - (0.189f * a), 0.349f - (0.349f * a), 0.686f + (0.314f * a), 0.168f - (0.168f * a), 0.272f - (0.272f * a), 0.534f - (0.534f * a), 0.131f + (0.869f * a));
                }

            case FilterOpKind.Saturate:
                {
                    float s = op.A;
                    return Matrix(px, 0.213f + (0.787f * s), 0.715f - (0.715f * s), 0.072f - (0.072f * s), 0.213f - (0.213f * s), 0.715f + (0.285f * s), 0.072f - (0.072f * s), 0.213f - (0.213f * s), 0.715f - (0.715f * s), 0.072f + (0.928f * s));
                }

            default:
                {
                    float cs = MathF.Cos(op.A), sn = MathF.Sin(op.A);
                    return Matrix(
                        px,
                        0.213f + (cs * 0.787f) - (sn * 0.213f), 0.715f - (cs * 0.715f) - (sn * 0.715f), 0.072f - (cs * 0.072f) + (sn * 0.928f),
                        0.213f - (cs * 0.213f) + (sn * 0.143f), 0.715f + (cs * 0.285f) + (sn * 0.140f), 0.072f - (cs * 0.072f) - (sn * 0.283f),
                        0.213f - (cs * 0.213f) - (sn * 0.787f), 0.715f - (cs * 0.715f) + (sn * 0.715f), 0.072f + (cs * 0.928f) + (sn * 0.072f));
                }
        }
    }

    private static void Scale(float[] px, float k)
    {
        for (int i = 0; i < px.Length; i += 4)
        {
            float a = px[i + 3];
            px[i] = Math.Clamp(px[i] * k, 0f, a);
            px[i + 1] = Math.Clamp(px[i + 1] * k, 0f, a);
            px[i + 2] = Math.Clamp(px[i + 2] * k, 0f, a);
        }
    }

    private static float[] Matrix(float[] px, float a, float b, float c, float d, float e, float f, float g, float h, float i2)
    {
        for (int i = 0; i < px.Length; i += 4)
        {
            float r = px[i], gg = px[i + 1], bb = px[i + 2], al = px[i + 3];
            px[i] = Math.Clamp((a * r) + (b * gg) + (c * bb), 0f, al);
            px[i + 1] = Math.Clamp((d * r) + (e * gg) + (f * bb), 0f, al);
            px[i + 2] = Math.Clamp((g * r) + (h * gg) + (i2 * bb), 0f, al);
        }

        return px;
    }

    private static float[] DropShadow(float[] px, int w, int h, in FilterOp op, float scale)
    {
        int dx = (int)MathF.Round(op.A * scale), dy = (int)MathF.Round(op.B * scale);
        float[] sh = new float[px.Length];
        for (int y = 0; y < h; y++)
        {
            int sy = y - dy;
            if (sy < 0 || sy >= h)
            {
                continue;
            }

            for (int x = 0; x < w; x++)
            {
                int sx = x - dx;
                if (sx < 0 || sx >= w)
                {
                    continue;
                }

                float a = px[(((sy * w) + sx) * 4) + 3] * op.Color.A;
                int o = ((y * w) + x) * 4;
                sh[o] = op.Color.R * a;
                sh[o + 1] = op.Color.G * a;
                sh[o + 2] = op.Color.B * a;
                sh[o + 3] = a;
            }
        }

        sh = Blur(sh, w, h, op.C * scale);
        for (int i = 0; i < px.Length; i += 4)
        {
            float inv = 1f - px[i + 3];
            sh[i] = px[i] + (sh[i] * inv);
            sh[i + 1] = px[i + 1] + (sh[i + 1] * inv);
            sh[i + 2] = px[i + 2] + (sh[i + 2] * inv);
            sh[i + 3] = px[i + 3] + (sh[i + 3] * inv);
        }

        return sh;
    }

    // Gaussian blur of premultiplied data (Filter Effects 1 §5.1: standard deviation sigma; Gaussian for small sigma, three box blurs
    // of the size the SVG filter spec gives for large ones). Pixels outside the layer count as transparent.
    private static float[] Blur(float[] px, int w, int h, float sigma)
    {
        sigma = Math.Min(sigma, MaxSigma);
        if (!(sigma > 0.05f))
        {
            return px;
        }

        float[] tmp = new float[px.Length];
        if (sigma <= 8f)
        {
            int r = (int)MathF.Ceiling(sigma * 3f);
            float[] k = new float[(2 * r) + 1];
            float sum = 0f;
            for (int i = -r; i <= r; i++)
            {
                k[i + r] = MathF.Exp(-(i * i) / (2f * sigma * sigma));
                sum += k[i + r];
            }

            for (int i = 0; i < k.Length; i++)
            {
                k[i] /= sum;
            }

            Convolve(px, tmp, w, h, k, r, true);
            Convolve(tmp, px, w, h, k, r, false);
            return px;
        }

        int d = (int)MathF.Floor((sigma * 3f * MathF.Sqrt(2f * MathF.PI) / 4f) + 0.5f);
        for (int pass = 0; pass < 3; pass++)
        {
            Box(px, tmp, w, h, d, true);
            Box(tmp, px, w, h, d, false);
        }

        return px;
    }

    private static void Convolve(float[] src, float[] dst, int w, int h, float[] k, int r, bool horizontal)
    {
        int len = horizontal ? w : h, lines = horizontal ? h : w;
        int step = horizontal ? 4 : w * 4, lineStep = horizontal ? w * 4 : 4;
        for (int l = 0; l < lines; l++)
        {
            int baseOff = l * lineStep;
            for (int p = 0; p < len; p++)
            {
                float a0 = 0, a1 = 0, a2 = 0, a3 = 0;
                int from = Math.Max(0, p - r), to = Math.Min(len - 1, p + r);
                for (int q = from; q <= to; q++)
                {
                    float wgt = k[q - p + r];
                    int o = baseOff + (q * step);
                    a0 += src[o] * wgt;
                    a1 += src[o + 1] * wgt;
                    a2 += src[o + 2] * wgt;
                    a3 += src[o + 3] * wgt;
                }

                int od = baseOff + (p * step);
                dst[od] = a0;
                dst[od + 1] = a1;
                dst[od + 2] = a2;
                dst[od + 3] = a3;
            }
        }
    }

    // A box blur of width d centered on the pixel (for even d, the SVG rule's offset alternation is not applied: the box is centered at a half pixel).
    private static void Box(float[] src, float[] dst, int w, int h, int d, bool horizontal)
    {
        if (d <= 1)
        {
            Array.Copy(src, dst, src.Length);
            return;
        }

        int len = horizontal ? w : h, lines = horizontal ? h : w;
        int step = horizontal ? 4 : w * 4, lineStep = horizontal ? w * 4 : 4;
        int left = d / 2, right = d - left - 1;
        float inv = 1f / d;
        for (int l = 0; l < lines; l++)
        {
            int baseOff = l * lineStep;
            for (int c = 0; c < 4; c++)
            {
                float sum = 0f;
                for (int q = 0; q <= Math.Min(right, len - 1); q++)
                {
                    sum += src[baseOff + (q * step) + c];
                }

                for (int p = 0; p < len; p++)
                {
                    dst[baseOff + (p * step) + c] = sum * inv;
                    int add = p + right + 1, sub = p - left;
                    if (add < len)
                    {
                        sum += src[baseOff + (add * step) + c];
                    }

                    if (sub >= 0)
                    {
                        sum -= src[baseOff + (sub * step) + c];
                    }
                }
            }
        }
    }
}

// The same plane mapping as the engine's transform geometry, kept here so the raster backend does not depend on engine internals.
internal static class TransformGeometry3
{
    public static bool Map(in Matrix4x4 m, float x, float y, out float ox, out float oy)
    {
        float w = (x * m.M14) + (y * m.M24) + m.M44;
        ox = oy = 0f;
        if (!(w > 1e-6f))
        {
            return false;
        }

        ox = ((x * m.M11) + (y * m.M21) + m.M41) / w;
        oy = ((x * m.M12) + (y * m.M22) + m.M42) / w;
        return float.IsFinite(ox) && float.IsFinite(oy);
    }

    public static bool Unmap(in Matrix4x4 m, float x, float y, out float ox, out float oy)
    {
        ox = oy = 0f;
        double a = m.M11, b = m.M12, c = m.M14, d = m.M21, e = m.M22, f = m.M24, g = m.M41, h = m.M42, i = m.M44;
        double det = (a * ((e * i) - (f * h))) - (b * ((d * i) - (f * g))) + (c * ((d * h) - (e * g)));
        if (Math.Abs(det) < 1e-12 || !double.IsFinite(det))
        {
            return false;
        }

        double k = 1.0 / det;
        double ia = ((e * i) - (f * h)) * k, ib = ((c * h) - (b * i)) * k, ic = ((b * f) - (c * e)) * k;
        double id = ((f * g) - (d * i)) * k, ie = ((a * i) - (c * g)) * k, iff = ((c * d) - (a * f)) * k;
        double ig = ((d * h) - (e * g)) * k, ih = ((b * g) - (a * h)) * k, ii = ((a * e) - (b * d)) * k;
        double w = (x * ic) + (y * iff) + ii;
        if (Math.Abs(w) < 1e-12)
        {
            return false;
        }

        double ux = ((x * ia) + (y * id) + ig) / w, uy = ((x * ib) + (y * ie) + ih) / w;
        ox = (float)ux;
        oy = (float)uy;
        // The point must project in front of the viewer.
        return Map(in m, ox, oy, out _, out _);
    }
}
