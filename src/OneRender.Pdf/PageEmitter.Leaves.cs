using System.Numerics;
using OnePdf;
using OneRender.Painting;
using OneRender.Text;

namespace OneRender.Pdf;

internal sealed partial class PageEmitter
{
    /// <summary>A rectangle, rounded rectangle or path, to be written and then filled or clipped.</summary>
    private readonly ref struct Shape
    {
        private readonly byte _kind;
        private readonly RectF _rect;
        private readonly CornerRadii _radii;
        private readonly ReadOnlySpan<byte> _verbs;
        private readonly ReadOnlySpan<float> _coords;

        public Shape(RectF rect)
        {
            _kind = 0;
            _rect = rect;
            _radii = default;
            _verbs = default;
            _coords = default;
            EvenOdd = false;
        }

        public Shape(RectF rect, CornerRadii radii)
        {
            _kind = 1;
            _rect = rect;
            _radii = radii;
            _verbs = default;
            _coords = default;
            EvenOdd = false;
        }

        public Shape(ReadOnlySpan<byte> verbs, ReadOnlySpan<float> coords, bool evenOdd)
        {
            _kind = 2;
            _rect = default;
            _radii = default;
            _verbs = verbs;
            _coords = coords;
            EvenOdd = evenOdd;
        }

        public bool EvenOdd { get; }

        public void Write(PdfContentBuilder b)
        {
            switch (_kind)
            {
                case 0:
                    b.Rectangle(_rect.X, _rect.Y, _rect.Width, _rect.Height);
                    break;
                case 1:
                    Geo.RoundedRect(b, _rect.X, _rect.Y, _rect.Width, _rect.Height, _radii);
                    break;
                default:
                    Geo.Verbs(b, _verbs, _coords);
                    break;
            }
        }
    }

    private void FillRectItem(in DisplayItem it)
    {
        RectF r = it.Rect;
        if (r.IsEmpty)
        {
            return;
        }

        FillShape(new Shape(r), it.Paint, in it);
    }

    private void FillShape(in Shape shape, in Paint paint, in DisplayItem it)
    {
        if (paint.Kind == PaintKind.Solid)
        {
            if (paint.Color.IsTransparent)
            {
                // Structure anchors (empty table cells) are transparent rectangles: they make the elements around them exist.
                EnsureStructure();
                return;
            }

            BeginLeaf(false, it.RootBounds);
            bool saved = BeginAlpha(paint.Color.A);
            Geo.SetFill(_b, paint.Color);
            shape.Write(_b);
            _b.Fill(shape.EvenOdd);
            EndAlpha(saved);
            return;
        }

        BeginLeaf(false, it.RootBounds);
        _b.Save();
        shape.Write(_b);
        _b.Clip(shape.EvenOdd);
        PaintGradient(it.Gradient, it.LocalBounds);
        _b.Restore();
    }

    // ------------------------------------------------------------------ borders

    private void BorderItem(in DisplayItem it)
    {
        RectF r = it.Rect;
        if (r.IsEmpty)
        {
            return;
        }

        BorderSide top = it.BorderTop, right = it.BorderRight, bottom = it.BorderBottom, left = it.BorderLeft;
        double wt = Math.Max(0, top.Width), wr = Math.Max(0, right.Width), wb = Math.Max(0, bottom.Width), wl = Math.Max(0, left.Width);
        if (wt + wr + wb + wl <= 0)
        {
            return;
        }

        // CSS Backgrounds 3 §5.3: where opposite widths exceed the box the inner corners are brought together in proportion.
        double sx = wl + wr > r.Width && wl + wr > 0 ? r.Width / (wl + wr) : 1;
        double sy = wt + wb > r.Height && wt + wb > 0 ? r.Height / (wt + wb) : 1;
        var geo = new BorderGeometry(r, it.Radii, wt * sy, wr * sx, wb * sy, wl * sx);

        BeginLeaf(false, it.RootBounds);
        if (IsPlainSolid(top, wt) && IsPlainSolid(right, wr) && IsPlainSolid(bottom, wb) && IsPlainSolid(left, wl) && top.Color == right.Color && top.Color == bottom.Color && top.Color == left.Color)
        {
            bool saved = BeginAlpha(top.Color.A);
            Geo.SetFill(_b, top.Color);
            geo.Ring(_b, 0, 1);
            _b.Fill(true);
            EndAlpha(saved);
            return;
        }

        DrawSide(in geo, 0, top, wt);
        DrawSide(in geo, 1, right, wr);
        DrawSide(in geo, 2, bottom, wb);
        DrawSide(in geo, 3, left, wl);
    }

    private static bool IsPlainSolid(in BorderSide s, double width) => width > 0 && s.Style == BorderStyle.Solid && !s.Color.IsTransparent;

    private readonly struct BorderGeometry
    {
        public readonly RectF Rect;
        public readonly CornerRadii Radii;
        public readonly double Top, Right, Bottom, Left;

        public BorderGeometry(RectF rect, CornerRadii radii, double top, double right, double bottom, double left)
        {
            Rect = rect;
            Radii = radii;
            Top = top;
            Right = right;
            Bottom = bottom;
            Left = left;
        }

        /// <summary>The area between the outline inset by <paramref name="from"/> of the widths and the one inset by <paramref name="to"/>, as two subpaths (fill even-odd).</summary>
        public void Ring(PdfContentBuilder b, double from, double to)
        {
            Edge(b, from);
            Edge(b, to);
        }

        private void Edge(PdfContentBuilder b, double f)
        {
            double t = Top * f, rt = Right * f, bt = Bottom * f, l = Left * f;
            double w = Math.Max(0, Rect.Width - l - rt), h = Math.Max(0, Rect.Height - t - bt);
            Geo.RoundedRect(b, Rect.X + l, Rect.Y + t, w, h, Geo.Inner(Radii, t, rt, bt, l));
        }

        /// <summary>The part of the ring that belongs to a side: bounded by the diagonals from the outer to the inner corners (§5.3).</summary>
        public void SideQuad(PdfContentBuilder b, int side)
        {
            double x = Rect.X, y = Rect.Y, r = Rect.Right, bo = Rect.Bottom;
            double ix0 = x + Left, iy0 = y + Top, ix1 = Math.Max(ix0, r - Right), iy1 = Math.Max(iy0, bo - Bottom);
            switch (side)
            {
                case 0:
                    b.MoveTo(x, y);
                    b.LineTo(r, y);
                    b.LineTo(ix1, iy0);
                    b.LineTo(ix0, iy0);
                    break;
                case 1:
                    b.MoveTo(r, y);
                    b.LineTo(r, bo);
                    b.LineTo(ix1, iy1);
                    b.LineTo(ix1, iy0);
                    break;
                case 2:
                    b.MoveTo(r, bo);
                    b.LineTo(x, bo);
                    b.LineTo(ix0, iy1);
                    b.LineTo(ix1, iy1);
                    break;
                default:
                    b.MoveTo(x, bo);
                    b.LineTo(x, y);
                    b.LineTo(ix0, iy0);
                    b.LineTo(ix0, iy1);
                    break;
            }

            b.ClosePath();
        }

        /// <summary>The middle line of the ring of a side, as a closed outline (for dashed and dotted sides).</summary>
        public void MiddleLine(PdfContentBuilder b)
        {
            double t = Top * 0.5, rt = Right * 0.5, bt = Bottom * 0.5, l = Left * 0.5;
            double w = Math.Max(0, Rect.Width - l - rt), h = Math.Max(0, Rect.Height - t - bt);
            Geo.RoundedRect(b, Rect.X + l, Rect.Y + t, w, h, Geo.Inner(Radii, t, rt, bt, l));
        }
    }

    private static PaintColor Dark(in PaintColor c) => new(c.R * (2f / 3f), c.G * (2f / 3f), c.B * (2f / 3f), c.A);

    private void DrawSide(in BorderGeometry g, int side, in BorderSide s, double width)
    {
        if (!(width > 0) || s.Style is BorderStyle.None or BorderStyle.Hidden || s.Color.IsTransparent)
        {
            return;
        }

        bool topLeft = side is 0 or 3;
        _b.Save();
        g.SideQuad(_b, side);
        _b.Clip();
        switch (s.Style)
        {
            case BorderStyle.Dashed:
            case BorderStyle.Dotted:
                {
                    bool saved = BeginAlpha(s.Color.A);
                    Geo.SetStroke(_b, s.Color);
                    _b.SetLineWidth(width);
                    if (s.Style == BorderStyle.Dashed)
                    {
                        _b.SetDash([2 * width, width]);
                    }
                    else
                    {
                        _b.SetLineCap(PdfLineCap.Round);
                        _b.SetDash([0, 2 * width]);
                    }

                    g.MiddleLine(_b);
                    _b.Stroke();
                    EndAlpha(saved);
                    break;
                }

            case BorderStyle.DoubleLine when width >= 3:
                FillRing(in g, 0, 1.0 / 3, s.Color);
                FillRing(in g, 2.0 / 3, 1, s.Color);
                break;
            case BorderStyle.Groove:
                FillRing(in g, 0, 0.5, topLeft ? Dark(s.Color) : s.Color);
                FillRing(in g, 0.5, 1, topLeft ? s.Color : Dark(s.Color));
                break;
            case BorderStyle.Ridge:
                FillRing(in g, 0, 0.5, topLeft ? s.Color : Dark(s.Color));
                FillRing(in g, 0.5, 1, topLeft ? Dark(s.Color) : s.Color);
                break;
            case BorderStyle.Inset:
                FillRing(in g, 0, 1, topLeft ? Dark(s.Color) : s.Color);
                break;
            case BorderStyle.Outset:
                FillRing(in g, 0, 1, topLeft ? s.Color : Dark(s.Color));
                break;
            default:
                FillRing(in g, 0, 1, s.Color);
                break;
        }

        _b.Restore();
    }

    private void FillRing(in BorderGeometry g, double from, double to, in PaintColor color)
    {
        bool saved = BeginAlpha(color.A);
        Geo.SetFill(_b, color);
        g.Ring(_b, from, to);
        _b.Fill(true);
        EndAlpha(saved);
    }

    // ------------------------------------------------------------------ shadows

    private void ShadowItem(in DisplayItem it, int index)
    {
        PaintColor c = it.ShadowColor;
        RectF box = it.Rect;
        if (c.IsTransparent || (box.IsEmpty && !it.ShadowInset))
        {
            return;
        }

        if (it.ShadowBlur > 0)
        {
            // PDF has no blur. The soft edge is a small image with an alpha channel, drawn at the raster resolution.
            Island(index, index, "blurred shadow", report: false);
            return;
        }

        float spread = it.ShadowSpread;
        CornerRadii radii = it.Radii;
        BeginLeaf(false, it.RootBounds);
        if (!it.ShadowInset)
        {
            // CSS Backgrounds 3 §7.1: the shape is the border box moved and grown by the spread, drawn only outside the border box.
            double x = box.X + it.ShadowOffsetX - spread, y = box.Y + it.ShadowOffsetY - spread;
            double w = box.Width + (2 * spread), h = box.Height + (2 * spread);
            if (!(w > 0 && h > 0))
            {
                return;
            }

            _b.Save();
            _b.Rectangle(x - 1, y - 1, w + 2, h + 2);
            Geo.RoundedRect(_b, box.X, box.Y, box.Width, box.Height, radii);
            _b.Clip(true);
            bool saved = BeginAlpha(c.A);
            Geo.SetFill(_b, c);
            Geo.RoundedRect(_b, x, y, w, h, Grow(radii, spread));
            _b.Fill();
            EndAlpha(saved);
            _b.Restore();
            return;
        }

        // §7.2: the padding box minus the offset, shrunk hole is shadow.
        _b.Save();
        Geo.RoundedRect(_b, box.X, box.Y, box.Width, box.Height, radii);
        _b.Clip();
        bool sv = BeginAlpha(c.A);
        Geo.SetFill(_b, c);
        _b.Rectangle(box.X - 1, box.Y - 1, box.Width + 2, box.Height + 2);
        double hx = box.X + it.ShadowOffsetX + spread, hy = box.Y + it.ShadowOffsetY + spread;
        double hw = box.Width - (2 * spread), hh = box.Height - (2 * spread);
        if (hw > 0 && hh > 0)
        {
            Geo.RoundedRect(_b, hx, hy, hw, hh, Grow(radii, -spread));
        }

        _b.Fill(true);
        EndAlpha(sv);
        _b.Restore();
    }

    // A non-zero radius changes by the spread and a square corner stays square (§7.1).
    private static CornerRadii Grow(in CornerRadii r, float d)
    {
        static float G(float v, float d) => v > 0 ? Math.Max(0, v + d) : 0;
        return new CornerRadii(G(r.TopLeftX, d), G(r.TopLeftY, d), G(r.TopRightX, d), G(r.TopRightY, d), G(r.BottomRightX, d), G(r.BottomRightY, d), G(r.BottomLeftX, d), G(r.BottomLeftY, d));
    }

    // ------------------------------------------------------------------ images

    private void ImageItem(in DisplayItem it)
    {
        RectF dest = it.ImageDestination;
        if (dest.IsEmpty)
        {
            return;
        }

        PdfCreatedImage? img = _s.ImageFor(it.Image);
        if (img is null)
        {
            return;
        }

        RectF src = it.ImageSource;
        double sx = 0, sy = 0, sw = img.Width, sh = img.Height;
        if (!src.IsEmpty)
        {
            (sx, sy, sw, sh) = (src.X, src.Y, src.Width, src.Height);
        }

        double kx = dest.Width / sw, ky = dest.Height / sh;
        double fx = dest.X - (sx * kx), fy = dest.Y - (sy * ky), fw = img.Width * kx, fh = img.Height * ky;
        bool partial = sx > 0 || sy > 0 || sw < img.Width || sh < img.Height;
        BeginLeaf(true, it.RootBounds);
        bool saved = BeginAlpha(1f);
        if (!it.IsTiledImage)
        {
            if (partial)
            {
                _b.Save();
                _b.Rectangle(dest.X, dest.Y, dest.Width, dest.Height);
                _b.Clip();
            }

            _b.DrawImage(img, fx, fy + fh, fw, -fh);
            if (partial)
            {
                _b.Restore();
            }

            EndAlpha(saved);
            return;
        }

        RectF area = it.ImageTileArea;
        double stepX = dest.Width + it.ImageTileGapX, stepY = dest.Height + it.ImageTileGapY;
        if (area.IsEmpty || !(stepX > 0) || !(stepY > 0))
        {
            EndAlpha(saved);
            return;
        }

        int i0 = (int)Math.Floor((area.X - dest.X) / stepX), i1 = (int)Math.Ceiling((area.Right - dest.X) / stepX);
        int j0 = (int)Math.Floor((area.Y - dest.Y) / stepY), j1 = (int)Math.Ceiling((area.Bottom - dest.Y) / stepY);
        long tiles = (long)(i1 - i0 + 1) * (j1 - j0 + 1);
        if (tiles > 4096)
        {
            _s.Diagnostics.Add(DiagnosticKind.LimitExceeded, "a tiled image repeats more than 4,096 times; the rest are not drawn");
            i1 = i0 + 63;
            j1 = j0 + 63;
        }

        _b.Save();
        _b.Rectangle(area.X, area.Y, area.Width, area.Height);
        _b.Clip();
        for (int j = j0; j <= j1; j++)
        {
            for (int i = i0; i <= i1; i++)
            {
                double ox = i * stepX, oy = j * stepY;
                if (partial)
                {
                    _b.Save();
                    _b.Rectangle(dest.X + ox, dest.Y + oy, dest.Width, dest.Height);
                    _b.Clip();
                }

                _b.DrawImage(img, fx + ox, fy + oy + fh, fw, -fh);
                if (partial)
                {
                    _b.Restore();
                }
            }
        }

        _b.Restore();
        EndAlpha(saved);
    }

    // ------------------------------------------------------------------ text

    private void GlyphRunItem(in DisplayItem it, int index)
    {
        GlyphRunView run = it.GlyphRun;
        ReadOnlySpan<int> ids = run.GlyphIds;
        PaintColor color = run.Color;
        float size = run.FontSize;
        if (ids.IsEmpty || color.IsTransparent || !(size > 0))
        {
            return;
        }

        if (run.Blur > 0)
        {
            Island(index, index, "text shadow", report: false);
            return;
        }

        PdfCreatedFont? font = _s.FontFor(run.Face);
        if (font is null)
        {
            return;
        }

        ReadOnlySpan<float> xs = run.X, ys = run.Y;
        ReadOnlySpan<int> clusters = run.Clusters;
        ReadOnlySpan<char> text = run.Text;
        int glyphCount = run.Face.GlyphCount;
        string source = text.IsEmpty ? " " : text.ToString();
        var glyphs = new PdfShapedGlyph[ids.Length];
        double perEm = 1000.0 / size;
        float baseY = ys[0];
        int spaceGlyph = run.Face.GetGlyphIndex(32);
        bool notdefSeen = false;
        for (int i = 0; i < ids.Length; i++)
        {
            double advance;
            if (i + 1 < ids.Length)
            {
                advance = (xs[i + 1] - xs[i]) * perEm;
            }
            else
            {
                advance = run.Face.GetAdvance(ids[i]) * 1000.0 / Math.Max(1, run.Face.UnitsPerEm);
            }

            int gid = (uint)ids[i] < (uint)glyphCount ? ids[i] : 0;
            if (gid == 0 && _s.Options.Profile != PdfProfile.None)
            {
                // ISO 14289-1 7.21.8 and ISO 19005-2 6.2.11.8 forbid showing .notdef: a character the face lacks keeps its advance
                // but draws nothing (the text is still in the structure through /ActualText).
                gid = spaceGlyph;
                notdefSeen = true;
            }

            int cl = Math.Clamp(clusters[i], 0, source.Length);
            glyphs[i] = new PdfShapedGlyph(gid, cl, advance, 0, -(ys[i] - baseY) * perEm);
        }

        if (notdefSeen && spaceGlyph == 0)
        {
            return;
        }

        BeginLeaf(true, it.RootBounds, text);
        bool saved = BeginAlpha(color.A);
        _b.Save();
        Geo.SetFill(_b, color);
        // The page space is y down; text is drawn upright in a locally flipped space with its origin on the baseline.
        _b.Translate(run.OriginX + xs[0], run.OriginY + baseY);
        _b.Scale(1, -1);
        _b.SetFont(font, size);
        bool bitmaps = ColorBitmapGlyphs.HasBitmaps(run.Face);
        if (bitmaps)
        {
            // A color bitmap font has no outlines to paint: the text is written invisibly so it stays selectable and extractable,
            // and each glyph's bitmap is drawn over it below.
            _b.SetTextRenderMode(PdfTextRenderMode.Invisible);
        }

        _b.DrawGlyphRun(0, 0, glyphs, source);
        _b.Restore();
        if (bitmaps)
        {
            ColorBitmapGlyphItems(in run, ids);
        }

        EndAlpha(saved);
    }

    // Each glyph of a color bitmap font is its PNG, scaled from the strike's pixels per em to the run's font size and placed by the
    // glyph's bearings: the same placement the raster backend uses.
    private void ColorBitmapGlyphItems(in GlyphRunView run, ReadOnlySpan<int> ids)
    {
        ReadOnlySpan<float> xs = run.X, ys = run.Y;
        for (int i = 0; i < ids.Length; i++)
        {
            if (!ColorBitmapGlyphs.TryGet(run.Face, ids[i], out ColorBitmapGlyph g))
            {
                continue;
            }

            PdfCreatedImage? img = _s.GlyphImageFor(run.Face, ids[i], in g);
            if (img is null)
            {
                continue;
            }

            double k = run.FontSize / (double)g.Ppem;
            double left = run.OriginX + xs[i] + (g.BearingX * k);
            double top = run.OriginY + ys[i] - (g.BearingY * k);
            double w = g.Width * k, h = g.Height * k;
            _b.DrawImage(img, left, top + h, w, -h);
        }
    }
}
