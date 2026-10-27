using System.Globalization;

namespace OneRender.Painting;

/// <summary>
/// Parser for SVG path data (SVG 2 §9.3), used for the CSS <c>path()</c> shape and for the shapes of an inline
/// <c>clipPath</c>. Arcs become cubic curves (SVG 2 implementation notes F.6). The parser is iterative and stops at
/// <see cref="MaxSegments"/>, so hostile data costs a bounded amount of work.
/// </summary>
internal static class PathDataParser
{
    /// <summary>The most path segments one path may hold.</summary>
    public const int MaxSegments = 8192;

    /// <summary>True when the whole string is valid path data (CSS Shapes 1: an error makes the shape invalid).</summary>
    public static bool IsValid(string data) => Parse(data, null, 0, 0, out _);

    /// <summary>Parses <paramref name="data"/> into <paramref name="dst"/> shifted by (<paramref name="ox"/>, <paramref name="oy"/>).</summary>
    /// <param name="data">The path data.</param>
    /// <param name="dst">Receives the figures; null to only validate.</param>
    /// <param name="ox">X shift.</param>
    /// <param name="oy">Y shift.</param>
    /// <param name="segments">How many segments were read.</param>
    /// <returns>False when the data has a syntax error or too many segments; <paramref name="dst"/> then holds what was read before it.</returns>
    public static bool Parse(string data, PathBuffer? dst, float ox, float oy, out int segments)
    {
        segments = 0;
        int i = 0, n = data.Length;
        double cx = 0, cy = 0, sx = 0, sy = 0, lcx = 0, lcy = 0;
        char cmd = '\0', prev = '\0';
        bool open = false, first = true;
        while (true)
        {
            SkipWs(data, ref i);
            if (i >= n)
            {
                return !first;
            }

            char c = data[i];
            if (char.IsAsciiLetter(c))
            {
                if ("MmLlHhVvCcSsQqTtAaZz".IndexOf(c, StringComparison.Ordinal) < 0)
                {
                    return false;
                }

                if (first && c is not ('M' or 'm'))
                {
                    return false;
                }

                cmd = c;
                i++;
                if (c is 'Z' or 'z')
                {
                    if (open)
                    {
                        dst?.Close();
                    }

                    open = false;
                    cx = sx;
                    cy = sy;
                    prev = 'Z';
                    first = false;
                    if (++segments > MaxSegments)
                    {
                        return false;
                    }

                    continue;
                }
            }
            else if (cmd == '\0' || cmd is 'Z' or 'z')
            {
                return false;
            }
            else if (!(c == '-' || c == '+' || c == '.' || char.IsAsciiDigit(c)))
            {
                return false;
            }

            if (++segments > MaxSegments)
            {
                return false;
            }

            bool rel = char.IsAsciiLetterLower(cmd);
            char up = char.ToUpperInvariant(cmd);
            double a, b, d, e, f, g;
            switch (up)
            {
                case 'M':
                    if (!Num(data, ref i, out a) || !Num(data, ref i, out b))
                    {
                        return false;
                    }

                    if (rel)
                    {
                        a += cx;
                        b += cy;
                    }

                    cx = sx = a;
                    cy = sy = b;
                    dst?.MoveTo((float)(a + ox), (float)(b + oy));
                    open = true;
                    cmd = rel ? 'l' : 'L'; // further pairs are implicit line-tos
                    prev = 'M';
                    break;
                case 'L':
                    if (!Num(data, ref i, out a) || !Num(data, ref i, out b))
                    {
                        return false;
                    }

                    if (rel)
                    {
                        a += cx;
                        b += cy;
                    }

                    Ensure(dst, ref open, cx, cy, sx, sy, ox, oy, prev);
                    dst?.LineTo((float)(a + ox), (float)(b + oy));
                    cx = a;
                    cy = b;
                    prev = 'L';
                    break;
                case 'H':
                    if (!Num(data, ref i, out a))
                    {
                        return false;
                    }

                    a = rel ? a + cx : a;
                    Ensure(dst, ref open, cx, cy, sx, sy, ox, oy, prev);
                    dst?.LineTo((float)(a + ox), (float)(cy + oy));
                    cx = a;
                    prev = 'L';
                    break;
                case 'V':
                    if (!Num(data, ref i, out a))
                    {
                        return false;
                    }

                    a = rel ? a + cy : a;
                    Ensure(dst, ref open, cx, cy, sx, sy, ox, oy, prev);
                    dst?.LineTo((float)(cx + ox), (float)(a + oy));
                    cy = a;
                    prev = 'L';
                    break;
                case 'C':
                    if (!Num(data, ref i, out a) || !Num(data, ref i, out b) || !Num(data, ref i, out d) || !Num(data, ref i, out e) || !Num(data, ref i, out f) || !Num(data, ref i, out g))
                    {
                        return false;
                    }

                    if (rel)
                    {
                        a += cx;
                        b += cy;
                        d += cx;
                        e += cy;
                        f += cx;
                        g += cy;
                    }

                    Ensure(dst, ref open, cx, cy, sx, sy, ox, oy, prev);
                    dst?.CubicTo((float)(a + ox), (float)(b + oy), (float)(d + ox), (float)(e + oy), (float)(f + ox), (float)(g + oy));
                    lcx = d;
                    lcy = e;
                    cx = f;
                    cy = g;
                    prev = 'C';
                    break;
                case 'S':
                    if (!Num(data, ref i, out d) || !Num(data, ref i, out e) || !Num(data, ref i, out f) || !Num(data, ref i, out g))
                    {
                        return false;
                    }

                    if (rel)
                    {
                        d += cx;
                        e += cy;
                        f += cx;
                        g += cy;
                    }

                    a = prev == 'C' ? (2 * cx) - lcx : cx;
                    b = prev == 'C' ? (2 * cy) - lcy : cy;
                    Ensure(dst, ref open, cx, cy, sx, sy, ox, oy, prev);
                    dst?.CubicTo((float)(a + ox), (float)(b + oy), (float)(d + ox), (float)(e + oy), (float)(f + ox), (float)(g + oy));
                    lcx = d;
                    lcy = e;
                    cx = f;
                    cy = g;
                    prev = 'C';
                    break;
                case 'Q':
                    if (!Num(data, ref i, out a) || !Num(data, ref i, out b) || !Num(data, ref i, out d) || !Num(data, ref i, out e))
                    {
                        return false;
                    }

                    if (rel)
                    {
                        a += cx;
                        b += cy;
                        d += cx;
                        e += cy;
                    }

                    Ensure(dst, ref open, cx, cy, sx, sy, ox, oy, prev);
                    dst?.QuadTo((float)(a + ox), (float)(b + oy), (float)(d + ox), (float)(e + oy));
                    lcx = a;
                    lcy = b;
                    cx = d;
                    cy = e;
                    prev = 'Q';
                    break;
                case 'T':
                    if (!Num(data, ref i, out d) || !Num(data, ref i, out e))
                    {
                        return false;
                    }

                    if (rel)
                    {
                        d += cx;
                        e += cy;
                    }

                    a = prev == 'Q' ? (2 * cx) - lcx : cx;
                    b = prev == 'Q' ? (2 * cy) - lcy : cy;
                    Ensure(dst, ref open, cx, cy, sx, sy, ox, oy, prev);
                    dst?.QuadTo((float)(a + ox), (float)(b + oy), (float)(d + ox), (float)(e + oy));
                    lcx = a;
                    lcy = b;
                    cx = d;
                    cy = e;
                    prev = 'Q';
                    break;
                default: // 'A'
                    {
                        if (!Num(data, ref i, out double rx) || !Num(data, ref i, out double ry) || !Num(data, ref i, out double rot)
                            || !Flag(data, ref i, out bool large) || !Flag(data, ref i, out bool sweep) || !Num(data, ref i, out d) || !Num(data, ref i, out e))
                        {
                            return false;
                        }

                        if (rel)
                        {
                            d += cx;
                            e += cy;
                        }

                        Ensure(dst, ref open, cx, cy, sx, sy, ox, oy, prev);
                        if (dst is not null)
                        {
                            Arc(dst, cx, cy, rx, ry, rot, large, sweep, d, e, ox, oy);
                        }

                        cx = d;
                        cy = e;
                        prev = 'A';
                        break;
                    }
            }

            first = false;
        }
    }

    // After a close the next drawing command starts a new figure at the previous start point (SVG 2 §9.3.4).
    private static void Ensure(PathBuffer? dst, ref bool open, double cx, double cy, double sx, double sy, float ox, float oy, char prev)
    {
        if (!open)
        {
            dst?.MoveTo((float)(sx + ox), (float)(sy + oy));
            open = true;
        }
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && (s[i] is ' ' or '\t' or '\n' or '\r' or '\f' or ','))
        {
            i++;
        }
    }

    private static bool Flag(string s, ref int i, out bool v)
    {
        SkipWs(s, ref i);
        v = false;
        if (i >= s.Length || (s[i] != '0' && s[i] != '1'))
        {
            return false;
        }

        v = s[i++] == '1';
        return true;
    }

    private static bool Num(string s, ref int i, out double v)
    {
        SkipWs(s, ref i);
        v = 0;
        int start = i, n = s.Length;
        if (i < n && (s[i] == '-' || s[i] == '+'))
        {
            i++;
        }

        int digits = 0;
        while (i < n && char.IsAsciiDigit(s[i]))
        {
            i++;
            digits++;
        }

        if (i < n && s[i] == '.')
        {
            i++;
            while (i < n && char.IsAsciiDigit(s[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return false;
        }

        if (i < n && (s[i] == 'e' || s[i] == 'E'))
        {
            int save = i++;
            if (i < n && (s[i] == '-' || s[i] == '+'))
            {
                i++;
            }

            int ed = 0;
            while (i < n && char.IsAsciiDigit(s[i]))
            {
                i++;
                ed++;
            }

            if (ed == 0)
            {
                i = save;
            }
        }

        if (!double.TryParse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || !double.IsFinite(v) || Math.Abs(v) > 1e9)
        {
            return false;
        }

        return true;
    }

    // SVG 2 Appendix B.2.4: endpoint to center parameterization, then up to four cubic pieces.
    private static void Arc(PathBuffer dst, double x1, double y1, double rx, double ry, double rotDeg, bool large, bool sweep, double x2, double y2, float ox, float oy)
    {
        if (x1 == x2 && y1 == y2)
        {
            return;
        }

        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx == 0 || ry == 0)
        {
            dst.LineTo((float)(x2 + ox), (float)(y2 + oy));
            return;
        }

        double phi = rotDeg * Math.PI / 180, cp = Math.Cos(phi), sp = Math.Sin(phi);
        double dx = (x1 - x2) / 2, dy = (y1 - y2) / 2;
        double x1p = (cp * dx) + (sp * dy), y1p = (-sp * dx) + (cp * dy);
        double lambda = (x1p * x1p / (rx * rx)) + (y1p * y1p / (ry * ry));
        if (lambda > 1)
        {
            double sq = Math.Sqrt(lambda);
            rx *= sq;
            ry *= sq;
        }

        double num = (rx * rx * ry * ry) - (rx * rx * y1p * y1p) - (ry * ry * x1p * x1p);
        double den = (rx * rx * y1p * y1p) + (ry * ry * x1p * x1p);
        double coef = den == 0 ? 0 : Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
        double cxp = coef * rx * y1p / ry, cyp = -coef * ry * x1p / rx;
        double cx = (cp * cxp) - (sp * cyp) + ((x1 + x2) / 2), cy = (sp * cxp) + (cp * cyp) + ((y1 + y2) / 2);
        double th1 = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        double th2 = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
        double dth = th2 - th1;
        if (sweep && dth < 0)
        {
            dth += 2 * Math.PI;
        }
        else if (!sweep && dth > 0)
        {
            dth -= 2 * Math.PI;
        }

        int pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(dth) / (Math.PI / 2) - 1e-9));
        double step = dth / pieces;
        double t = 4.0 / 3.0 * Math.Tan(step / 4);
        double a = th1;
        for (int k = 0; k < pieces; k++)
        {
            double b = a + step;
            double ca = Math.Cos(a), sa = Math.Sin(a), cb = Math.Cos(b), sb = Math.Sin(b);
            Pt(rx, ry, cp, sp, cx, cy, ca - (t * sa), sa + (t * ca), out double p1x, out double p1y);
            Pt(rx, ry, cp, sp, cx, cy, cb + (t * sb), sb - (t * cb), out double p2x, out double p2y);
            double ex = k == pieces - 1 ? x2 : 0, ey = k == pieces - 1 ? y2 : 0;
            if (k != pieces - 1)
            {
                Pt(rx, ry, cp, sp, cx, cy, cb, sb, out ex, out ey);
            }

            dst.CubicTo((float)(p1x + ox), (float)(p1y + oy), (float)(p2x + ox), (float)(p2y + oy), (float)(ex + ox), (float)(ey + oy));
            a = b;
        }
    }

    private static void Pt(double rx, double ry, double cp, double sp, double cx, double cy, double u, double v, out double x, out double y)
    {
        x = (cp * rx * u) - (sp * ry * v) + cx;
        y = (sp * rx * u) + (cp * ry * v) + cy;
    }
}
