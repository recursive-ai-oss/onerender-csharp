using OnePdf;
using OneRender.Painting;

namespace OneRender.Pdf;

internal sealed partial class PageEmitter
{
    private const int MaxRepeats = 96;
    private const int ConicWedges = 180;

    private struct Stop
    {
        public double T;
        public double R, G, B, A;
    }

    /// <summary>
    /// Paints a gradient over the current clip. Linear and radial gradients are PDF axial and radial shadings; a gradient with
    /// translucent stops gets a luminosity soft mask carrying the alpha. A conic gradient has no PDF shading type in the writer
    /// and is drawn as 180 wedges of one color each.
    /// </summary>
    private void PaintGradient(GradientView g, in RectF area)
    {
        int n = g.StopCount;
        if (n == 0 || area.IsEmpty)
        {
            return;
        }

        if (_alpha < 0.9995f)
        {
            _b.SetFillOpacity(_alpha);
            _b.SetStrokeOpacity(_alpha);
        }

        if (_blend != BlendMode.Normal)
        {
            _b.SetBlendMode(Geo.Blend(_blend, out _));
        }

        var stops = new List<Stop>(n);
        for (int i = 0; i < n; i++)
        {
            GradientStop s = g.GetStop(i);
            stops.Add(new Stop { T = s.Offset, R = s.Color.R, G = s.Color.G, B = s.Color.B, A = s.Color.A });
        }

        switch (g.Kind)
        {
            case PaintKind.Linear:
                PaintLinear(g, stops, in area);
                break;
            case PaintKind.Radial:
                PaintRadial(g, stops, in area);
                break;
            default:
                PaintConic(g, stops, in area);
                break;
        }
    }

    private void SolidArea(in Stop s, in RectF area)
    {
        if (s.A <= 0)
        {
            return;
        }

        _b.Save();
        _b.SetFillOpacity(Math.Clamp(s.A * _alpha, 0, 1));
        _b.SetFillRgb(s.R, s.G, s.B);
        _b.Rectangle(area.X - 1, area.Y - 1, area.Width + 2, area.Height + 2);
        _b.Fill();
        _b.Restore();
    }

    private static Stop Mix(in Stop a, in Stop b, double f)
    {
        // CSS Images 3 §3.5.2: premultiplied interpolation.
        double al = a.A + ((b.A - a.A) * f);
        double pr = (a.R * a.A) + (((b.R * b.A) - (a.R * a.A)) * f);
        double pg = (a.G * a.A) + (((b.G * b.A) - (a.G * a.A)) * f);
        double pb = (a.B * a.A) + (((b.B * b.A) - (a.B * a.A)) * f);
        return al <= 1e-9
            ? new Stop { A = 0, R = a.R, G = a.G, B = a.B }
            : new Stop { A = al, R = pr / al, G = pg / al, B = pb / al };
    }

    private static Stop ColorAt(List<Stop> s, double t)
    {
        if (t <= s[0].T)
        {
            return s[0];
        }

        for (int i = 1; i < s.Count; i++)
        {
            if (t < s[i].T)
            {
                double span = s[i].T - s[i - 1].T;
                return span <= 0 ? s[i] : Mix(s[i - 1], s[i], (t - s[i - 1].T) / span);
            }
        }

        return s[^1];
    }

    // Expands a repeating gradient over [tmin, tmax]; returns false when it repeats too often to write (the caller paints the average).
    private static bool Expand(List<Stop> s, double tmin, double tmax, out List<Stop> result)
    {
        double first = s[0].T, span = s[^1].T - first;
        result = s;
        if (!(span > 1e-9))
        {
            return false;
        }

        int k0 = (int)Math.Floor((tmin - first) / span), k1 = (int)Math.Ceiling((tmax - first) / span);
        if (k1 <= k0)
        {
            k1 = k0 + 1;
        }

        if (k1 - k0 > MaxRepeats || (long)(k1 - k0) * s.Count > 4096)
        {
            return false;
        }

        var list = new List<Stop>((k1 - k0) * s.Count);
        for (int k = k0; k < k1; k++)
        {
            foreach (Stop st in s)
            {
                Stop c = st;
                c.T = st.T + (k * span);
                list.Add(c);
            }
        }

        result = list;
        return true;
    }

    private static Stop Average(List<Stop> s)
    {
        double r = 0, g = 0, b = 0, a = 0;
        foreach (Stop st in s)
        {
            r += st.R;
            g += st.G;
            b += st.B;
            a += st.A;
        }

        int n = s.Count;
        return new Stop { R = r / n, G = g / n, B = b / n, A = a / n };
    }

    /// <summary>Drops the part of the stop list below <paramref name="t0"/>, inserting the interpolated stop at the boundary.</summary>
    private static List<Stop> ClipLow(List<Stop> s, double t0)
    {
        if (s[0].T >= t0)
        {
            return s;
        }

        var r = new List<Stop>(s.Count);
        Stop at = ColorAt(s, t0);
        at.T = t0;
        r.Add(at);
        foreach (Stop st in s)
        {
            if (st.T > t0)
            {
                r.Add(st);
            }
        }

        if (r.Count == 1)
        {
            r.Add(new Stop { T = t0 + 1e-6, R = at.R, G = at.G, B = at.B, A = at.A });
        }

        return r;
    }

    private static PdfGradient? Build(List<Stop> s, bool alphaOnly, Func<List<PdfGradientStop>, PdfGradient> make)
    {
        double lo = s[0].T, hi = s[^1].T;
        if (!(hi - lo > 1e-9))
        {
            return null;
        }

        var stops = new List<PdfGradientStop>(s.Count + 2);
        for (int i = 0; i < s.Count; i++)
        {
            Stop st = s[i];
            double r = st.R, g = st.G, b = st.B;
            if (st.A <= 0)
            {
                // A transparent stop has no color of its own; premultiplied interpolation fades the neighbour's color, so borrow it.
                int j = i + 1;
                while (j < s.Count && s[j].A <= 0)
                {
                    j++;
                }

                if (j == s.Count)
                {
                    j = i - 1;
                    while (j >= 0 && s[j].A <= 0)
                    {
                        j--;
                    }
                }

                if (j >= 0 && j < s.Count)
                {
                    (r, g, b) = (s[j].R, s[j].G, s[j].B);
                }
            }

            double pos = Math.Clamp((st.T - lo) / (hi - lo), 0, 1);
            if (stops.Count > 0 && pos < stops[^1].Position)
            {
                pos = stops[^1].Position;
            }

            PdfGradientColor color = alphaOnly ? PdfGradientColor.Gray(Math.Clamp(st.A, 0, 1)) : PdfGradientColor.Rgb(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
            stops.Add(new PdfGradientStop(pos, color));
        }

        try
        {
            return make(stops);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool Translucent(List<Stop> s)
    {
        foreach (Stop st in s)
        {
            if (st.A < 0.999)
            {
                return true;
            }
        }

        return false;
    }

    private void PaintLinear(GradientView g, List<Stop> stops, in RectF area)
    {
        double x0 = g.X0, y0 = g.Y0, dx = g.X1 - g.X0, dy = g.Y1 - g.Y0;
        double len2 = (dx * dx) + (dy * dy);
        if (!(len2 > 1e-12))
        {
            SolidArea(stops[^1], in area); // CSS Images 3 §3.1: a zero-length gradient line paints the last color
            return;
        }

        if (g.Repeating)
        {
            double tmin = double.MaxValue, tmax = double.MinValue;
            foreach ((double cx, double cy) in Corners(in area))
            {
                double t = (((cx - x0) * dx) + ((cy - y0) * dy)) / len2;
                tmin = Math.Min(tmin, t);
                tmax = Math.Max(tmax, t);
            }

            if (!Expand(stops, tmin, tmax, out List<Stop> expanded))
            {
                SolidArea(Average(stops), in area);
                return;
            }

            stops = expanded;
        }

        double lo = stops[0].T, hi = stops[^1].T;
        PdfGradient? colors = Build(stops, false, st => PdfGradient.Axial(x0 + (dx * lo), y0 + (dy * lo), x0 + (dx * hi), y0 + (dy * hi), st.ToArray()));
        if (colors is null)
        {
            SolidArea(stops[^1], in area);
            return;
        }

        if (Translucent(stops))
        {
            PdfGradient? alpha = Build(stops, true, st => PdfGradient.Axial(x0 + (dx * lo), y0 + (dy * lo), x0 + (dx * hi), y0 + (dy * hi), st.ToArray()));
            SetAlphaMask(alpha, in area);
        }

        _b.PaintGradient(colors);
    }

    private void PaintRadial(GradientView g, List<Stop> stops, in RectF area)
    {
        double cx = g.X0, cy = g.Y0, rx = g.X1, ry = g.Y1;
        if (!(rx > 1e-9) || !(ry > 1e-9))
        {
            SolidArea(stops[^1], in area);
            return;
        }

        if (g.Repeating)
        {
            double tmax = 0;
            foreach ((double px, double py) in Corners(in area))
            {
                double ex = (px - cx) / rx, ey = (py - cy) / ry;
                tmax = Math.Max(tmax, Math.Sqrt((ex * ex) + (ey * ey)));
            }

            if (!Expand(stops, 0, tmax, out List<Stop> expanded))
            {
                SolidArea(Average(stops), in area);
                return;
            }

            stops = expanded;
        }

        stops = ClipLow(stops, 0);
        double lo = stops[0].T, hi = stops[^1].T;
        PdfGradient Make(List<PdfGradientStop> st) => PdfGradient.Radial(0, 0, lo * rx, 0, 0, hi * rx, st.ToArray());
        PdfGradient? colors = Build(stops, false, Make);
        if (colors is null)
        {
            SolidArea(stops[^1], in area);
            return;
        }

        // The ellipse is a circle of radius rx squeezed in y: the shading is written in a space that maps back to the ellipse.
        _b.Save();
        _b.Translate(cx, cy);
        _b.Scale(1, ry / rx);
        if (Translucent(stops))
        {
            SetAlphaMask(Build(stops, true, Make), new RectF((float)(area.X - cx), (float)((area.Y - cy) * rx / ry), area.Width, (float)(area.Height * rx / ry)));
        }

        _b.PaintGradient(colors);
        _b.Restore();
    }

    private void SetAlphaMask(PdfGradient? alpha, in RectF area)
    {
        if (alpha is null)
        {
            return;
        }

        PdfFormContentBuilder fb = _s.Writer.BeginForm(new PdfRectangle(area.X - 2, area.Y - 2, area.Right + 2, area.Bottom + 2));
        fb.SetTransparencyGroup(true);
        fb.PaintGradient(alpha);
        PdfFormXObject mask = fb.Commit();
        _b.SetSoftMask(mask, PdfSoftMaskKind.Luminosity);
    }

    private void PaintConic(GradientView g, List<Stop> stops, in RectF area)
    {
        double cx = g.X0, cy = g.Y0, from = g.FromAngle;
        double first = stops[0].T, last = stops[^1].T, span = last - first;
        double far = 0;
        foreach ((double px, double py) in Corners(in area))
        {
            far = Math.Max(far, Math.Sqrt(((px - cx) * (px - cx)) + ((py - cy) * (py - cy))));
        }

        double step = 2 * Math.PI / ConicWedges;
        double radius = ((far + 2) * 1.02) / Math.Cos(step * 0.75);
        for (int k = 0; k < ConicWedges; k++)
        {
            double mid = (k + 0.5) * step; // clockwise from up, before the from-angle
            double t = mid / (2 * Math.PI);
            // CSS Images 4 §3.3: position is the angle minus the from-angle, in turns. Wedges are laid out in absolute angle, so shift back.
            double turns = t - (from / (2 * Math.PI));
            turns -= Math.Floor(turns);
            double pos = g.Repeating && span > 1e-9 ? first + ((((turns - first) / span) - Math.Floor((turns - first) / span)) * span) : turns;
            Stop c = ColorAt(stops, pos);
            if (c.A <= 0)
            {
                continue;
            }

            double a0 = (k * step) - (step * 0.25), a1 = ((k + 1) * step) + (step * 0.25);
            _b.Save();
            if (c.A * _alpha < 0.9995)
            {
                _b.SetFillOpacity(Math.Clamp(c.A * _alpha, 0, 1));
            }

            _b.SetFillRgb(c.R, c.G, c.B);
            _b.MoveTo(cx, cy);
            _b.LineTo(cx + (radius * Math.Sin(a0)), cy - (radius * Math.Cos(a0)));
            _b.LineTo(cx + (radius * Math.Sin(a1)), cy - (radius * Math.Cos(a1)));
            _b.ClosePath();
            _b.Fill();
            _b.Restore();
        }
    }

    private static IEnumerable<(double X, double Y)> Corners(in RectF a)
    {
        return
        [
            (a.X, a.Y),
            (a.Right, a.Y),
            (a.Right, a.Bottom),
            (a.X, a.Bottom),
        ];
    }
}
