using System.Numerics;
using OneRender.Painting;
using Brush = OneImage.Drawing.Brush;

namespace OneRender.Image;

// Brushes that give oneimage the paints a display list needs and its own brushes do not have: CSS gradients (conic included),
// clamped image sampling, and a coverage mask. A oneimage brush is asked for a row of pixel colors at a time, in the pixel grid
// of the canvas; these map each pixel center back to the item's own space with the inverse of the current transform.

/// <summary>CSS Images 3/4 gradients: linear, radial and conic, plain and repeating, interpolated in premultiplied sRGB.</summary>
internal sealed class GradientBrush : Brush
{
    private readonly PaintKind _kind;
    private readonly bool _repeating;
    private readonly double _g0, _g1, _g2, _g3, _from;
    private readonly double _ia, _ib, _ic, _id, _ie, _if;
    private readonly float[] _off;
    private readonly float[] _pr, _pg, _pb, _pa;
    private readonly bool _degenerate;
    private readonly double _lenSq;

    public GradientBrush(in GradientView g, in Matrix3x2 localToDevice)
    {
        _kind = g.Kind;
        _repeating = g.Repeating;
        _g0 = g.X0;
        _g1 = g.Y0;
        _g2 = g.X1;
        _g3 = g.Y1;
        _from = g.FromAngle;
        if (!Matrix3x2.Invert(localToDevice, out Matrix3x2 inv))
        {
            throw new ArgumentException("The transform is not invertible.", nameof(localToDevice));
        }

        _ia = inv.M11;
        _ib = inv.M12;
        _ic = inv.M21;
        _id = inv.M22;
        _ie = inv.M31;
        _if = inv.M32;
        int n = g.StopCount;
        _off = new float[n];
        _pr = new float[n];
        _pg = new float[n];
        _pb = new float[n];
        _pa = new float[n];
        for (int i = 0; i < n; i++)
        {
            GradientStop s = g.GetStop(i);
            _off[i] = s.Offset;
            _pr[i] = s.Color.R * s.Color.A;
            _pg[i] = s.Color.G * s.Color.A;
            _pb[i] = s.Color.B * s.Color.A;
            _pa[i] = s.Color.A;
        }

        switch (_kind)
        {
            case PaintKind.Linear:
                _lenSq = ((_g2 - _g0) * (_g2 - _g0)) + ((_g3 - _g1) * (_g3 - _g1));
                _degenerate = !(_lenSq > 0.0);
                break;
            case PaintKind.Radial:
                _degenerate = !(_g2 > 0.0) || !(_g3 > 0.0);
                break;
            default:
                _degenerate = false;
                break;
        }
    }

    public override void FillRow(int x, int y, Span<float> rgba)
    {
        int count = rgba.Length / 4;
        double py = y + 0.5;
        for (int i = 0; i < count; i++)
        {
            double px = x + i + 0.5;
            double lx = (px * _ia) + (py * _ic) + _ie;
            double ly = (px * _ib) + (py * _id) + _if;
            double t = Position(lx, ly);
            Color(t, rgba.Slice(i * 4, 4));
        }
    }

    private double Position(double lx, double ly)
    {
        switch (_kind)
        {
            case PaintKind.Linear:
                {
                    // CSS Images 3 §3.1.1: the position is the projection of the point on the gradient line, in line lengths.
                    if (_degenerate)
                    {
                        return double.PositiveInfinity;
                    }

                    double dx = _g2 - _g0, dy = _g3 - _g1;
                    return (((lx - _g0) * dx) + ((ly - _g1) * dy)) / _lenSq;
                }

            case PaintKind.Radial:
                {
                    // CSS Images 3 §3.2.1: the position is the distance from the center, in units of the ending shape's radii.
                    if (_degenerate)
                    {
                        return double.PositiveInfinity;
                    }

                    double ex = (lx - _g0) / _g2, ey = (ly - _g1) / _g3;
                    return Math.Sqrt((ex * ex) + (ey * ey));
                }

            default:
                {
                    // CSS Images 4 §3.3: angle clockwise from "up", in turns, after the start angle.
                    double ang = Math.Atan2(lx - _g0, -(ly - _g1)) - _from;
                    double turns = ang / (2.0 * Math.PI);
                    return turns - Math.Floor(turns);
                }
        }
    }

    private void Color(double t, Span<float> o)
    {
        int n = _off.Length;
        double first = _off[0], last = _off[n - 1];
        if (double.IsPositiveInfinity(t))
        {
            // A zero-length gradient line or a zero radius paints the last color (CSS Images 3 §3.1 and §3.2).
            Put(n - 1, o);
            return;
        }

        if (_repeating)
        {
            double span = last - first;
            if (!(span > 0.0))
            {
                // CSS Images 3 §3.5: a repeating gradient whose stops all coincide paints the average of the stop colors.
                double r = 0, g = 0, b = 0, a = 0;
                for (int k = 0; k < n; k++)
                {
                    r += _pr[k];
                    g += _pg[k];
                    b += _pb[k];
                    a += _pa[k];
                }

                Emit(o, (float)(r / n), (float)(g / n), (float)(b / n), (float)(a / n));
                return;
            }

            double u = (t - first) / span;
            t = first + ((u - Math.Floor(u)) * span);
        }

        if (t <= first)
        {
            Put(0, o);
            return;
        }

        if (t >= last)
        {
            Put(n - 1, o);
            return;
        }

        int hi = 1;
        while (hi < n - 1 && _off[hi] <= t)
        {
            hi++;
        }

        int lo = hi - 1;
        double d = _off[hi] - _off[lo];
        float f = d > 0.0 ? (float)((t - _off[lo]) / d) : 1f;
        Emit(
            o,
            _pr[lo] + ((_pr[hi] - _pr[lo]) * f),
            _pg[lo] + ((_pg[hi] - _pg[lo]) * f),
            _pb[lo] + ((_pb[hi] - _pb[lo]) * f),
            _pa[lo] + ((_pa[hi] - _pa[lo]) * f));
    }

    private void Put(int i, Span<float> o) => Emit(o, _pr[i], _pg[i], _pb[i], _pa[i]);

    // Interpolation happens on premultiplied values (CSS Images 3 §3.5.2); oneimage wants straight colors back.
    private static void Emit(Span<float> o, float pr, float pg, float pb, float pa)
    {
        if (pa <= 0f)
        {
            o[0] = 0f;
            o[1] = 0f;
            o[2] = 0f;
            o[3] = 0f;
            return;
        }

        o[0] = Math.Min(pr / pa, 1f);
        o[1] = Math.Min(pg / pa, 1f);
        o[2] = Math.Min(pb / pa, 1f);
        o[3] = Math.Min(pa, 1f);
    }
}

/// <summary>Samples an RGBA image through an affine map, clamping at the edges of the source rectangle.</summary>
internal sealed class ImageSamplerBrush : Brush
{
    private readonly RgbaImage _image;
    private readonly double _ia, _ib, _ic, _id, _ie, _if;
    private readonly int _sx, _sy, _sw, _sh;
    private readonly double _fx, _fy, _fw, _fh;
    private readonly bool _smooth;

    // Tiling (format 1.1): device -> local space map, the first tile in local space and the repeat period.
    private readonly bool _tiled;
    private readonly double _la, _lb, _lc, _ld, _le, _lf;
    private readonly double _tx, _ty, _tw, _th, _px, _py, _kx, _ky;

    public ImageSamplerBrush(RgbaImage image, in RectF source, in Matrix3x2 sourceToDevice, bool smooth)
    {
        _image = image;
        _smooth = smooth;
        if (!Matrix3x2.Invert(sourceToDevice, out Matrix3x2 inv))
        {
            throw new ArgumentException("The transform is not invertible.", nameof(sourceToDevice));
        }

        _ia = inv.M11;
        _ib = inv.M12;
        _ic = inv.M21;
        _id = inv.M22;
        _ie = inv.M31;
        _if = inv.M32;
        // The source rectangle is snapped outward to whole source pixels; an empty one means the whole image.
        if (source.IsEmpty)
        {
            _sx = 0;
            _sy = 0;
            _sw = image.Width;
            _sh = image.Height;
        }
        else
        {
            int x0 = Math.Clamp((int)Math.Floor(source.X), 0, image.Width - 1);
            int y0 = Math.Clamp((int)Math.Floor(source.Y), 0, image.Height - 1);
            int x1 = Math.Clamp((int)Math.Ceiling(source.Right), x0 + 1, image.Width);
            int y1 = Math.Clamp((int)Math.Ceiling(source.Bottom), y0 + 1, image.Height);
            _sx = x0;
            _sy = y0;
            _sw = x1 - x0;
            _sh = y1 - y0;
        }

        _fx = _sx;
        _fy = _sy;
        _fw = _sw;
        _fh = _sh;
    }

    /// <summary>A tiled image: <paramref name="tile"/> is one copy in local space, repeated with the given gaps; <paramref name="localToDevice"/> maps local space to the device.</summary>
    public ImageSamplerBrush(RgbaImage image, in RectF source, in RectF tile, float gapX, float gapY, in Matrix3x2 localToDevice, bool smooth)
        : this(image, source, Matrix3x2.Identity * localToDevice, smooth)
    {
        _tiled = true;
        if (!Matrix3x2.Invert(localToDevice, out Matrix3x2 inv))
        {
            throw new ArgumentException("The transform is not invertible.", nameof(localToDevice));
        }

        _la = inv.M11;
        _lb = inv.M12;
        _lc = inv.M21;
        _ld = inv.M22;
        _le = inv.M31;
        _lf = inv.M32;
        _tx = tile.X;
        _ty = tile.Y;
        _tw = tile.Width;
        _th = tile.Height;
        _px = tile.Width + gapX;
        _py = tile.Height + gapY;
        _kx = tile.Width / _fw;
        _ky = tile.Height / _fh;
    }

    public override void FillRow(int x, int y, Span<float> rgba)
    {
        int count = rgba.Length / 4;
        double py = y + 0.5;
        for (int i = 0; i < count; i++)
        {
            double px = x + i + 0.5;
            double u = (px * _ia) + (py * _ic) + _ie;
            double v = (px * _ib) + (py * _id) + _if;
            Span<float> o = rgba.Slice(i * 4, 4);
            if (_tiled)
            {
                double lx = (px * _la) + (py * _lc) + _le - _tx;
                double ly = (px * _lb) + (py * _ld) + _lf - _ty;
                lx -= Math.Floor(lx / _px) * _px;
                ly -= Math.Floor(ly / _py) * _py;
                if (lx >= _tw || ly >= _th)
                {
                    o[0] = o[1] = o[2] = o[3] = 0f;
                    continue;
                }

                u = _fx + (lx / _kx);
                v = _fy + (ly / _ky);
            }

            if (_smooth)
            {
                Bilinear(u, v, o);
            }
            else
            {
                int ix = Math.Clamp((int)Math.Floor(u), _sx, _sx + _sw - 1);
                int iy = Math.Clamp((int)Math.Floor(v), _sy, _sy + _sh - 1);
                int p = ((iy * _image.Width) + ix) * 4;
                byte[] d = _image.Pixels;
                o[0] = d[p] / 255f;
                o[1] = d[p + 1] / 255f;
                o[2] = d[p + 2] / 255f;
                o[3] = d[p + 3] / 255f;
            }
        }
    }

    private void Bilinear(double u, double v, Span<float> o)
    {
        // Sample positions are clamped half a pixel inside the source rectangle, so edges repeat instead of fading.
        double uc = Math.Clamp(u, _fx + 0.5, _fx + _fw - 0.5) - 0.5;
        double vc = Math.Clamp(v, _fy + 0.5, _fy + _fh - 0.5) - 0.5;
        int u0 = (int)Math.Floor(uc), v0 = (int)Math.Floor(vc);
        float fu = (float)(uc - u0), fv = (float)(vc - v0);
        int u1 = Math.Min(u0 + 1, _sx + _sw - 1), v1 = Math.Min(v0 + 1, _sy + _sh - 1);
        u0 = Math.Max(u0, _sx);
        v0 = Math.Max(v0, _sy);
        float r = 0, g = 0, b = 0, a = 0;
        Tap(u0, v0, (1 - fu) * (1 - fv), ref r, ref g, ref b, ref a);
        Tap(u1, v0, fu * (1 - fv), ref r, ref g, ref b, ref a);
        Tap(u0, v1, (1 - fu) * fv, ref r, ref g, ref b, ref a);
        Tap(u1, v1, fu * fv, ref r, ref g, ref b, ref a);
        if (a <= 0f)
        {
            o[0] = 0f;
            o[1] = 0f;
            o[2] = 0f;
            o[3] = 0f;
            return;
        }

        o[0] = Math.Min(r / a, 1f);
        o[1] = Math.Min(g / a, 1f);
        o[2] = Math.Min(b / a, 1f);
        o[3] = Math.Min(a, 1f);
    }

    private void Tap(int px, int py, float w, ref float r, ref float g, ref float b, ref float a)
    {
        int p = ((py * _image.Width) + px) * 4;
        byte[] d = _image.Pixels;
        float al = d[p + 3] / 255f * w;
        r += d[p] / 255f * al;
        g += d[p + 1] / 255f * al;
        b += d[p + 2] / 255f * al;
        a += al;
    }
}

/// <summary>A solid color whose alpha is an 8-bit coverage mask placed at a pixel offset; pixels outside the mask are transparent.</summary>
internal sealed class MaskBrush : Brush
{
    private readonly PaintColor _color;
    private readonly byte[] _mask;
    private readonly int _mx, _my, _mw, _mh;

    public MaskBrush(PaintColor color, byte[] mask, int x, int y, int width, int height)
    {
        _color = color;
        _mask = mask;
        _mx = x;
        _my = y;
        _mw = width;
        _mh = height;
    }

    public override void FillRow(int x, int y, Span<float> rgba)
    {
        int count = rgba.Length / 4;
        int row = y - _my;
        for (int i = 0; i < count; i++)
        {
            int col = x + i - _mx;
            float cov = (uint)row < (uint)_mh && (uint)col < (uint)_mw ? _mask[(row * _mw) + col] / 255f : 0f;
            int p = i * 4;
            rgba[p] = _color.R;
            rgba[p + 1] = _color.G;
            rgba[p + 2] = _color.B;
            rgba[p + 3] = _color.A * cov;
        }
    }
}
