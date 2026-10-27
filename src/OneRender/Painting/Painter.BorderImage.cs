using System.Numerics;
using OneRender.Style;

namespace OneRender.Painting;

// border-image (CSS Backgrounds 3 §6): nine slices of the source drawn into the border area.
public sealed partial class Painter
{
    private bool PaintBorderImage(int f, in ComputedValues v, in BoxGeometry g, double top, double right, double bottom, double left, PaintColor cur)
    {
        BorderImageSpec bi = v.BorderImage;
        ImageSpec image = bi.Source;
        ImageInfo info = default;
        bool isUrl = image.Kind == ImageSourceKind.Url;
        if (isUrl && !TryImage(image.Url!, out info, v.ImageOrientationNone))
        {
            return false;
        }

        if (_scopes + 4 >= MaxScopes)
        {
            return false;
        }

        // §6.2 border-image-outset: the image area grows outward from the border box.
        double ot = Outset(bi.Outset[0], top), orr = Outset(bi.Outset[1], right), ob = Outset(bi.Outset[2], bottom), ol = Outset(bi.Outset[3], left);
        RectF bx = g.Border;
        double ax = bx.X - ol, ay = bx.Y - ot, aw = bx.Width + ol + orr, ah = bx.Height + ot + ob;
        if (!(aw > 0) || !(ah > 0))
        {
            return false;
        }

        // The image's own size: a gradient has none and takes the border image area (Images 3 §5.3).
        double iw = isUrl && info.Width > 0 ? info.Width : aw, ih = isUrl && info.Height > 0 ? info.Height : ah;

        // §6.1 border-image-slice
        double st = Slice(bi.Slice[0], ih), sr = Slice(bi.Slice[1], iw), sb = Slice(bi.Slice[2], ih), sl = Slice(bi.Slice[3], iw);

        // §6.3 border-image-width
        double wt = BiWidth(bi.Width[0], top, ah, st), wr = BiWidth(bi.Width[1], right, aw, sr), wb = BiWidth(bi.Width[2], bottom, ah, sb), wl = BiWidth(bi.Width[3], left, aw, sl);
        double fx = wl + wr > aw ? aw / (wl + wr) : 1, fy = wt + wb > ah ? ah / (wt + wb) : 1;
        double fac = Math.Min(fx, fy);
        if (fac < 1)
        {
            wt *= fac;
            wr *= fac;
            wb *= fac;
            wl *= fac;
        }

        ImageSampling sampling = SamplingOf(in v);
        int n = 0;
        double mw = aw - wl - wr, mh = ah - wt - wb;
        double smw = iw - sl - sr, smh = ih - st - sb;

        // corners
        Piece(f, n++, image, info, iw, ih, cur, sampling, 0, 0, sl, st, ax, ay, wl, wt);
        Piece(f, n++, image, info, iw, ih, cur, sampling, iw - sr, 0, sr, st, ax + aw - wr, ay, wr, wt);
        Piece(f, n++, image, info, iw, ih, cur, sampling, 0, ih - sb, sl, sb, ax, ay + ah - wb, wl, wb);
        Piece(f, n++, image, info, iw, ih, cur, sampling, iw - sr, ih - sb, sr, sb, ax + aw - wr, ay + ah - wb, wr, wb);

        // edges: tiled along their length by the scale that fits the slice to the border width (§6.5)
        Edge(f, n++, image, info, iw, ih, cur, sampling, sl, 0, smw, st, ax + wl, ay, mw, wt, true, bi.RepeatX);
        Edge(f, n++, image, info, iw, ih, cur, sampling, sl, ih - sb, smw, sb, ax + wl, ay + ah - wb, mw, wb, true, bi.RepeatX);
        Edge(f, n++, image, info, iw, ih, cur, sampling, 0, st, sl, smh, ax, ay + wt, wl, mh, false, bi.RepeatY);
        Edge(f, n++, image, info, iw, ih, cur, sampling, iw - sr, st, sr, smh, ax + aw - wr, ay + wt, wr, mh, false, bi.RepeatY);

        if (bi.Fill)
        {
            Piece(f, n, image, info, iw, ih, cur, sampling, sl, st, smw, smh, ax + wl, ay + wt, mw, mh);
        }

        return true;
    }

    private static double Outset(Length l, double borderWidth) => l.Kind == LengthKind.Number ? l.Value * borderWidth : Math.Max(0, l.ResolveOr(0, 0));

    private static double Slice(Length l, double dim) =>
        Math.Clamp(l.Kind == LengthKind.Percent ? l.Value / 100 * dim : l.Value, 0, dim);

    private static double BiWidth(Length l, double borderWidth, double basis, double sliceSize) => l.Kind switch
    {
        LengthKind.Number => l.Value * borderWidth,
        LengthKind.Auto => sliceSize,
        _ => Math.Max(0, l.ResolveOr(basis, 0)),
    };

    // Draws source rectangle (sx, sy, sw, sh) of the image scaled into the destination rectangle.
    private void Piece(int f, int index, ImageSpec image, ImageInfo info, double iw, double ih, PaintColor cur, ImageSampling sampling, double sx, double sy, double sw, double sh, double dx, double dy, double dw, double dh)
    {
        if (!(sw > 0) || !(sh > 0) || !(dw > 0) || !(dh > 0))
        {
            return;
        }

        PieceTile(f, index << 10, image, info, iw, ih, cur, sampling, sx, sy, sw, sh, dx, dy, dw, dh);
    }

    private void PieceTile(int f, int idx, ImageSpec image, ImageInfo info, double iw, double ih, PaintColor cur, ImageSampling sampling, double sx, double sy, double sw, double sh, double dx, double dy, double dw, double dh)
    {
        var dest = new RectF(C(dx), C(dy), C(dw), C(dh));
        if (image.Kind == ImageSourceKind.Url)
        {
            _list.DrawImage(Id2(f, 12, idx), info.Handle, new RectF(C(sx), C(sy), C(sw), C(sh)), dest, sampling);
            return;
        }

        // A gradient is drawn in image space and mapped: scale the source rectangle onto the destination.
        double kx = dw / sw, ky = dh / sh;
        double tx = dx - (sx * kx), ty = dy - (sy * ky);
        if (!double.IsFinite(kx + ky + tx + ty) || Math.Abs(kx) > 1e6 || Math.Abs(ky) > 1e6 || Math.Abs(tx) > 1e8 || Math.Abs(ty) > 1e8)
        {
            return;
        }

        var m = new Matrix3x2((float)kx, 0, 0, (float)ky, (float)tx, (float)ty);
        var srcRect = new RectF(C(sx), C(sy), C(sw), C(sh));
        _list.PushTransform(Id2(f, 13, idx), m);
        _list.PushClipRect(Id2(f, 14, idx), srcRect);
        _scopes += 2;
        Paint paint = MakeGradient(image.Gradient!, 0, 0, iw, ih, cur);
        _list.FillRect(Id2(f, 12, idx), srcRect, paint);
        _list.PopClip();
        _list.PopTransform();
        _scopes -= 2;
    }

    private void Edge(int f, int index, ImageSpec image, ImageInfo info, double iw, double ih, PaintColor cur, ImageSampling sampling, double sx, double sy, double sw, double sh, double dx, double dy, double dw, double dh, bool horizontal, BorderImageRepeatKind mode)
    {
        if (!(sw > 0) || !(sh > 0) || !(dw > 0) || !(dh > 0))
        {
            return;
        }

        // Scale factor from slice thickness to border thickness; the tile length follows from it.
        double scale = horizontal ? dh / sh : dw / sw;
        double tile = (horizontal ? sw : sh) * scale;
        double len = horizontal ? dw : dh;
        if (mode == BorderImageRepeatKind.Stretch || !(tile > 0))
        {
            PieceTile(f, (index << 10), image, info, iw, ih, cur, sampling, sx, sy, sw, sh, dx, dy, dw, dh);
            return;
        }

        double start = 0, step = tile, size = tile;
        int count;
        switch (mode)
        {
            case BorderImageRepeatKind.Round:
                count = (int)Math.Max(1, Math.Round(len / tile));
                size = step = len / count;
                break;
            case BorderImageRepeatKind.Space:
                count = (int)Math.Floor((len / tile) + 1e-9);
                if (count < 1)
                {
                    return;
                }

                step = tile + ((len - (count * tile)) / (count + 1));
                start = (step - tile);
                break;
            default:
                // repeat: one tile centered, the others around it, clipped at the ends.
                double first = (len - tile) / 2;
                first -= Math.Ceiling(first / tile) * tile;
                start = first;
                count = (int)Math.Min(MaxEdgeTiles, Math.Ceiling((len - first) / tile));
                break;
        }

        count = Math.Min(count, MaxEdgeTiles);
        bool clip = mode == BorderImageRepeatKind.Repeat && _scopes < MaxScopes;
        if (clip)
        {
            _list.PushClipRect(Id2(f, 15, index), new RectF(C(dx), C(dy), C(dw), C(dh)));
            _scopes++;
        }

        for (int i = 0; i < count; i++)
        {
            double off = start + (i * step);
            if (horizontal)
            {
                PieceTile(f, (index << 10) | (i + 1), image, info, iw, ih, cur, sampling, sx, sy, sw, sh, dx + off, dy, size, dh);
            }
            else
            {
                PieceTile(f, (index << 10) | (i + 1), image, info, iw, ih, cur, sampling, sx, sy, sw, sh, dx, dy + off, dw, size);
            }
        }

        if (clip)
        {
            _list.PopClip();
            _scopes--;
        }
    }
}
