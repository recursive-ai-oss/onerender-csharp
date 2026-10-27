using System.Numerics;

namespace OneRender.Animation;

/// <summary>
/// Matrix decomposition for interpolating transforms that cannot be matched function by function (CSS Transforms 2 §"Interpolation of
/// matrices"). A matrix is 16 numbers in the order CSS writes <c>matrix3d()</c>: index <c>column * 4 + row</c>, so the translation is
/// at 12 to 14 and the perspective row at 3, 7, 11, 15. All arithmetic is in <c>double</c> and allocation-free.
/// </summary>
internal static class TransformMath
{
    /// <summary>The decomposed parts of a 4x4 matrix.</summary>
    internal ref struct Decomposed
    {
        public Span<double> Translate; // 3
        public Span<double> Scale; // 3
        public Span<double> Skew; // 3: xy, xz, yz
        public Span<double> Perspective; // 4
        public Span<double> Quaternion; // 4: x, y, z, w
    }

    /// <summary>Loads a .NET matrix (row-vector layout, the same numbers CSS writes column by column) into the 16-number form.</summary>
    public static void Load(in Matrix4x4 m, Span<double> d)
    {
        d[0] = m.M11; d[1] = m.M12; d[2] = m.M13; d[3] = m.M14;
        d[4] = m.M21; d[5] = m.M22; d[6] = m.M23; d[7] = m.M24;
        d[8] = m.M31; d[9] = m.M32; d[10] = m.M33; d[11] = m.M34;
        d[12] = m.M41; d[13] = m.M42; d[14] = m.M43; d[15] = m.M44;
    }

    /// <summary>The reverse of <see cref="Load"/>; non-finite numbers become 0 so a matrix never poisons the painter.</summary>
    public static Matrix4x4 Store(ReadOnlySpan<double> d) => new(
        F(d[0]), F(d[1]), F(d[2]), F(d[3]),
        F(d[4]), F(d[5]), F(d[6]), F(d[7]),
        F(d[8]), F(d[9]), F(d[10]), F(d[11]),
        F(d[12]), F(d[13]), F(d[14]), F(d[15]));

    private static float F(double v) => double.IsFinite(v) ? (float)Math.Clamp(v, -1e30, 1e30) : 0f;

    /// <summary>Decomposes <paramref name="m"/>. Returns false for a matrix that cannot be (m44 = 0 or a singular perspective part).</summary>
    public static bool Decompose(ReadOnlySpan<double> m, ref Decomposed r)
    {
        Span<double> a = stackalloc double[16];
        m.CopyTo(a);
        if (a[15] == 0 || !double.IsFinite(a[15]))
        {
            return false;
        }

        double inv = 1 / a[15];
        for (int i = 0; i < 16; i++)
        {
            a[i] *= inv;
        }

        // The matrix with its perspective part removed must be invertible.
        Span<double> pm = stackalloc double[16];
        a.CopyTo(pm);
        pm[3] = pm[7] = pm[11] = 0;
        pm[15] = 1;
        double det = Determinant(pm);
        if (det == 0 || !double.IsFinite(det))
        {
            return false;
        }

        if (a[3] != 0 || a[7] != 0 || a[11] != 0)
        {
            // perspective = transpose(inverse(pm)) * rhs
            Span<double> invm = stackalloc double[16];
            if (!Invert(pm, invm))
            {
                return false;
            }

            double r0 = a[3], r1 = a[7], r2 = a[11], r3 = a[15];
            // transpose(inv) * rhs: component i is sum over j of inv[i*4 + j] (since transposed, the (j,i) entry of inv in column form).
            for (int i = 0; i < 4; i++)
            {
                r.Perspective[i] = (invm[(i * 4) + 0] * r0) + (invm[(i * 4) + 1] * r1) + (invm[(i * 4) + 2] * r2) + (invm[(i * 4) + 3] * r3);
            }
        }
        else
        {
            r.Perspective[0] = r.Perspective[1] = r.Perspective[2] = 0;
            r.Perspective[3] = 1;
        }

        r.Translate[0] = a[12];
        r.Translate[1] = a[13];
        r.Translate[2] = a[14];

        // Rows of the 3x3 part: the three column vectors.
        Span<double> row0 = stackalloc double[3] { a[0], a[1], a[2] };
        Span<double> row1 = stackalloc double[3] { a[4], a[5], a[6] };
        Span<double> row2 = stackalloc double[3] { a[8], a[9], a[10] };

        r.Scale[0] = Length(row0);
        Normalize(row0);
        r.Skew[0] = Dot(row0, row1);
        Combine(row1, row0, 1, -r.Skew[0]);
        r.Scale[1] = Length(row1);
        Normalize(row1);
        r.Skew[0] = r.Scale[1] != 0 ? r.Skew[0] / r.Scale[1] : 0;

        r.Skew[1] = Dot(row0, row2);
        Combine(row2, row0, 1, -r.Skew[1]);
        r.Skew[2] = Dot(row1, row2);
        Combine(row2, row1, 1, -r.Skew[2]);
        r.Scale[2] = Length(row2);
        Normalize(row2);
        r.Skew[1] = r.Scale[2] != 0 ? r.Skew[1] / r.Scale[2] : 0;
        r.Skew[2] = r.Scale[2] != 0 ? r.Skew[2] / r.Scale[2] : 0;

        // A left-handed coordinate system means a negative determinant: flip.
        Span<double> cross = stackalloc double[3];
        Cross(row1, row2, cross);
        if (Dot(row0, cross) < 0)
        {
            for (int i = 0; i < 3; i++)
            {
                r.Scale[i] = -r.Scale[i];
                row0[i] = -row0[i];
                row1[i] = -row1[i];
                row2[i] = -row2[i];
            }
        }

        double qx = 0.5 * Math.Sqrt(Math.Max(1 + row0[0] - row1[1] - row2[2], 0));
        double qy = 0.5 * Math.Sqrt(Math.Max(1 - row0[0] + row1[1] - row2[2], 0));
        double qz = 0.5 * Math.Sqrt(Math.Max(1 - row0[0] - row1[1] + row2[2], 0));
        double qw = 0.5 * Math.Sqrt(Math.Max(1 + row0[0] + row1[1] + row2[2], 0));
        if (row2[1] > row1[2])
        {
            qx = -qx;
        }

        if (row0[2] > row2[0])
        {
            qy = -qy;
        }

        if (row1[0] > row0[1])
        {
            qz = -qz;
        }

        r.Quaternion[0] = qx;
        r.Quaternion[1] = qy;
        r.Quaternion[2] = qz;
        r.Quaternion[3] = qw;
        return true;
    }

    /// <summary>Builds the matrix back from its parts (translate, rotate, skew, scale, in that order).</summary>
    public static void Recompose(in Decomposed p, Span<double> m)
    {
        Identity(m);
        for (int i = 0; i < 4; i++)
        {
            m[(i * 4) + 3] = p.Perspective[i];
        }

        // Translate: multiply on the right by a translation.
        Span<double> t = stackalloc double[16];
        Identity(t);
        t[12] = p.Translate[0];
        t[13] = p.Translate[1];
        t[14] = p.Translate[2];
        Span<double> tmp = stackalloc double[16];
        Multiply(m, t, tmp);
        tmp.CopyTo(m);

        // Rotate.
        double x = p.Quaternion[0], y = p.Quaternion[1], z = p.Quaternion[2], w = p.Quaternion[3];
        Span<double> rot = stackalloc double[16];
        Identity(rot);
        rot[0] = 1 - (2 * ((y * y) + (z * z)));
        rot[1] = 2 * ((x * y) + (z * w));
        rot[2] = 2 * ((x * z) - (y * w));
        rot[4] = 2 * ((x * y) - (z * w));
        rot[5] = 1 - (2 * ((x * x) + (z * z)));
        rot[6] = 2 * ((y * z) + (x * w));
        rot[8] = 2 * ((x * z) + (y * w));
        rot[9] = 2 * ((y * z) - (x * w));
        rot[10] = 1 - (2 * ((x * x) + (y * y)));
        Multiply(m, rot, tmp);
        tmp.CopyTo(m);

        // Skew: yz, then xz, then xy.
        Span<double> sk = stackalloc double[16];
        if (p.Skew[2] != 0)
        {
            Identity(sk);
            sk[9] = p.Skew[2];
            Multiply(m, sk, tmp);
            tmp.CopyTo(m);
        }

        if (p.Skew[1] != 0)
        {
            Identity(sk);
            sk[8] = p.Skew[1];
            Multiply(m, sk, tmp);
            tmp.CopyTo(m);
        }

        if (p.Skew[0] != 0)
        {
            Identity(sk);
            sk[4] = p.Skew[0];
            Multiply(m, sk, tmp);
            tmp.CopyTo(m);
        }

        // Scale.
        for (int c = 0; c < 3; c++)
        {
            for (int rr = 0; rr < 3; rr++)
            {
                m[(c * 4) + rr] *= p.Scale[c];
            }
        }
    }

    /// <summary>Interpolates two decomposed matrices into <paramref name="result"/> (a 16-number buffer); false when either cannot be decomposed.</summary>
    public static bool InterpolateMatrices(ReadOnlySpan<double> from, ReadOnlySpan<double> to, double progress, Span<double> result)
    {
        Span<double> buf = stackalloc double[34];
        var a = new Decomposed { Translate = buf.Slice(0, 3), Scale = buf.Slice(3, 3), Skew = buf.Slice(6, 3), Perspective = buf.Slice(9, 4), Quaternion = buf.Slice(13, 4) };
        var b = new Decomposed { Translate = buf.Slice(17, 3), Scale = buf.Slice(20, 3), Skew = buf.Slice(23, 3), Perspective = buf.Slice(26, 4), Quaternion = buf.Slice(30, 4) };
        if (!Decompose(from, ref a) || !Decompose(to, ref b))
        {
            return false;
        }

        // Matrices with a different scale sign pattern are interpolated by the spec's rule: flip nothing, the quaternion path takes the short way.
        for (int i = 0; i < 3; i++)
        {
            a.Translate[i] += (b.Translate[i] - a.Translate[i]) * progress;
            a.Scale[i] += (b.Scale[i] - a.Scale[i]) * progress;
            a.Skew[i] += (b.Skew[i] - a.Skew[i]) * progress;
        }

        for (int i = 0; i < 4; i++)
        {
            a.Perspective[i] += (b.Perspective[i] - a.Perspective[i]) * progress;
        }

        Slerp(a.Quaternion, b.Quaternion, progress);
        Recompose(a, result);
        return true;
    }

    // Spherical interpolation of unit quaternions along the shorter arc; the result replaces a.
    private static void Slerp(Span<double> a, ReadOnlySpan<double> b, double t)
    {
        double dot = (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]) + (a[3] * b[3]);
        Span<double> bb = stackalloc double[4] { b[0], b[1], b[2], b[3] };
        if (dot < 0)
        {
            dot = -dot;
            for (int i = 0; i < 4; i++)
            {
                bb[i] = -bb[i];
            }
        }

        dot = Math.Clamp(dot, -1, 1);
        double s0, s1;
        if (dot > 0.9995)
        {
            s0 = 1 - t;
            s1 = t;
        }
        else
        {
            double theta = Math.Acos(dot);
            double sin = Math.Sin(theta);
            s0 = Math.Sin((1 - t) * theta) / sin;
            s1 = Math.Sin(t * theta) / sin;
        }

        for (int i = 0; i < 4; i++)
        {
            a[i] = (a[i] * s0) + (bb[i] * s1);
        }
    }

    /// <summary>Sets <paramref name="m"/> to the identity.</summary>
    public static void Identity(Span<double> m)
    {
        m.Clear();
        m[0] = m[5] = m[10] = m[15] = 1;
    }

    /// <summary>The product <c>a * b</c> in column-vector convention (apply b first).</summary>
    public static void Multiply(ReadOnlySpan<double> a, ReadOnlySpan<double> b, Span<double> result)
    {
        for (int c = 0; c < 4; c++)
        {
            for (int r = 0; r < 4; r++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    sum += a[(k * 4) + r] * b[(c * 4) + k];
                }

                result[(c * 4) + r] = sum;
            }
        }
    }

    private static double Dot(ReadOnlySpan<double> a, ReadOnlySpan<double> b) => (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]);

    private static double Length(ReadOnlySpan<double> a) => Math.Sqrt(Dot(a, a));

    private static void Normalize(Span<double> a)
    {
        double l = Length(a);
        if (l == 0)
        {
            return;
        }

        a[0] /= l;
        a[1] /= l;
        a[2] /= l;
    }

    private static void Combine(Span<double> a, ReadOnlySpan<double> b, double ascl, double bscl)
    {
        a[0] = (ascl * a[0]) + (bscl * b[0]);
        a[1] = (ascl * a[1]) + (bscl * b[1]);
        a[2] = (ascl * a[2]) + (bscl * b[2]);
    }

    private static void Cross(ReadOnlySpan<double> a, ReadOnlySpan<double> b, Span<double> r)
    {
        r[0] = (a[1] * b[2]) - (a[2] * b[1]);
        r[1] = (a[2] * b[0]) - (a[0] * b[2]);
        r[2] = (a[0] * b[1]) - (a[1] * b[0]);
    }

    private static double Determinant(ReadOnlySpan<double> m)
    {
        // Laplace expansion along the first row of the row-major view; the determinant is the same for the transpose.
        double a = m[0], b = m[1], c = m[2], d = m[3];
        return (a * Det3(m, 1, 2, 3, 1, 2, 3)) - (b * Det3(m, 0, 2, 3, 1, 2, 3)) + (c * Det3(m, 0, 1, 3, 1, 2, 3)) - (d * Det3(m, 0, 1, 2, 1, 2, 3));
    }

    // The 3x3 minor of the row-major view using columns c0..c2 of rows r0..r2 where m[row * 4 + col].
    private static double Det3(ReadOnlySpan<double> m, int c0, int c1, int c2, int r0, int r1, int r2)
    {
        double a = m[(r0 * 4) + c0], b = m[(r0 * 4) + c1], c = m[(r0 * 4) + c2];
        double d = m[(r1 * 4) + c0], e = m[(r1 * 4) + c1], f = m[(r1 * 4) + c2];
        double g = m[(r2 * 4) + c0], h = m[(r2 * 4) + c1], i = m[(r2 * 4) + c2];
        return (a * ((e * i) - (f * h))) - (b * ((d * i) - (f * g))) + (c * ((d * h) - (e * g)));
    }

    private static bool Invert(ReadOnlySpan<double> m, Span<double> inv)
    {
        // Gauss-Jordan elimination with partial pivoting on a 4x8 augmented matrix.
        Span<double> aug = stackalloc double[32];
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                aug[(r * 8) + c] = m[(r * 4) + c];
                aug[(r * 8) + 4 + c] = r == c ? 1 : 0;
            }
        }

        for (int col = 0; col < 4; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < 4; r++)
            {
                if (Math.Abs(aug[(r * 8) + col]) > Math.Abs(aug[(pivot * 8) + col]))
                {
                    pivot = r;
                }
            }

            if (aug[(pivot * 8) + col] == 0)
            {
                return false;
            }

            if (pivot != col)
            {
                for (int c = 0; c < 8; c++)
                {
                    (aug[(col * 8) + c], aug[(pivot * 8) + c]) = (aug[(pivot * 8) + c], aug[(col * 8) + c]);
                }
            }

            double div = aug[(col * 8) + col];
            for (int c = 0; c < 8; c++)
            {
                aug[(col * 8) + c] /= div;
            }

            for (int r = 0; r < 4; r++)
            {
                if (r == col)
                {
                    continue;
                }

                double f = aug[(r * 8) + col];
                if (f == 0)
                {
                    continue;
                }

                for (int c = 0; c < 8; c++)
                {
                    aug[(r * 8) + c] -= f * aug[(col * 8) + c];
                }
            }
        }

        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                inv[(r * 4) + c] = aug[(r * 8) + 4 + c];
            }
        }

        return true;
    }
}
