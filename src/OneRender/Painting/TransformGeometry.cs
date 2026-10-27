using System.Numerics;
using OneRender.Style;

namespace OneRender.Painting;

/// <summary>
/// The matrix of a box's transform properties and of its parent's perspective, computed from style and geometry only, so the
/// painter and hit testing agree on where transformed content is. Matrices use the row-vector layout of <see cref="Matrix4x4"/>
/// (a point is multiplied on the left) and live in the coordinates of the box's containing space, with the transform origin applied.
/// </summary>
internal static class TransformGeometry
{
    /// <summary>
    /// Transforms 1 §13 and Transforms 2 §9: <c>translate</c>, <c>rotate</c>, <c>scale</c>, then <c>transform</c>, around <c>transform-origin</c>.
    /// </summary>
    /// <param name="v">The box's computed values.</param>
    /// <param name="border">The border box (x, y, width, height) in the containing space.</param>
    /// <param name="content">The content box in the same space, used for <c>transform-box: content-box</c>.</param>
    /// <param name="m">The matrix, identity when <see cref="ComputedValues.HasTransform"/> is false.</param>
    /// <returns>True when the box has a transform.</returns>
    public static bool Compute(in ComputedValues v, in RectF border, in RectF content, out Matrix4x4 m)
    {
        m = Matrix4x4.Identity;
        if (!v.HasTransform)
        {
            return false;
        }

        RectF r = v.TransformBox == TransformBoxKind.ContentBox ? content : border;
        double w = r.Width, h = r.Height;
        Matrix4x4 acc = v.Transform.ToMatrix(w, h) * v.Scale.ToMatrix(w, h) * v.Rotate.ToMatrix(w, h) * v.Translate.ToMatrix(w, h);
        float ox = (float)(r.X + v.TransformOrigin.X.Resolve(w));
        float oy = (float)(r.Y + v.TransformOrigin.Y.Resolve(h));
        float oz = (float)v.TransformOriginZ;
        m = Matrix4x4.CreateTranslation(-ox, -oy, -oz) * acc * Matrix4x4.CreateTranslation(ox, oy, oz);
        return true;
    }

    /// <summary>
    /// Transforms 2 §10: the matrix a parent's <c>perspective</c> applies to its children, around <c>perspective-origin</c>.
    /// </summary>
    /// <param name="parent">The parent's computed values.</param>
    /// <param name="parentBorder">The parent's border box in the same space as the child's.</param>
    /// <param name="m">The matrix, identity when there is no perspective.</param>
    /// <returns>True when the parent has a perspective.</returns>
    public static bool Perspective(in ComputedValues parent, in RectF parentBorder, out Matrix4x4 m)
    {
        m = Matrix4x4.Identity;
        if (!(parent.Perspective > 0))
        {
            return false;
        }

        float ox = (float)(parentBorder.X + parent.PerspectiveOrigin.X.Resolve(parentBorder.Width));
        float oy = (float)(parentBorder.Y + parent.PerspectiveOrigin.Y.Resolve(parentBorder.Height));
        Matrix4x4 p = Matrix4x4.Identity;
        p.M34 = (float)(-1.0 / parent.Perspective);
        m = Matrix4x4.CreateTranslation(-ox, -oy, 0) * p * Matrix4x4.CreateTranslation(ox, oy, 0);
        return true;
    }

    /// <summary>
    /// Maps a point of the box's own space through the matrix to the containing space, with the perspective divide (z = 0 plane).
    /// </summary>
    /// <param name="m">The matrix.</param>
    /// <param name="x">The point's x.</param>
    /// <param name="y">The point's y.</param>
    /// <param name="ox">The mapped x.</param>
    /// <param name="oy">The mapped y.</param>
    /// <returns>False when the point maps behind the viewer (w ≤ 0).</returns>
    public static bool MapPoint(in Matrix4x4 m, float x, float y, out float ox, out float oy)
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

    /// <summary>Inverse of <see cref="MapPoint"/> for the z = 0 plane: the point of the box's own space that lands on (x, y).</summary>
    /// <param name="m">The matrix.</param>
    /// <param name="x">The containing-space x.</param>
    /// <param name="y">The containing-space y.</param>
    /// <param name="ox">The box-space x.</param>
    /// <param name="oy">The box-space y.</param>
    /// <returns>False when the plane is edge-on or the point is not on the visible side.</returns>
    public static bool UnmapPoint(in Matrix4x4 m, float x, float y, out float ox, out float oy)
    {
        ox = oy = 0f;
        var h = new Matrix3x3Lite(m.M11, m.M12, m.M14, m.M21, m.M22, m.M24, m.M41, m.M42, m.M44);
        if (!h.TryInvert(out Matrix3x3Lite inv))
        {
            return false;
        }

        float w = (x * inv.C) + (y * inv.F) + inv.I;
        if (!(MathF.Abs(w) > 1e-9f))
        {
            return false;
        }

        ox = ((x * inv.A) + (y * inv.D) + inv.G) / w;
        oy = ((x * inv.B) + (y * inv.E) + inv.H) / w;
        return MapPoint(in m, ox, oy, out _, out _) && float.IsFinite(ox) && float.IsFinite(oy);
    }

    /// <summary>Signed area test: true when the box's plane faces away from the viewer after the matrix (CSS Transforms 2 §11, backface-visibility).</summary>
    public static bool FacesAway(in Matrix4x4 m, in RectF box)
    {
        Span<float> xs = stackalloc float[4], ys = stackalloc float[4];
        float[] cx = [box.X, box.Right, box.Right, box.X], cy = [box.Y, box.Y, box.Bottom, box.Bottom];
        for (int i = 0; i < 4; i++)
        {
            if (!MapPoint(in m, cx[i], cy[i], out xs[i], out ys[i]))
            {
                return false;
            }
        }

        float area = 0f;
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) & 3;
            area += (xs[i] * ys[j]) - (xs[j] * ys[i]);
        }

        return area < 0f;
    }

    // Row-major 3x3 for the homography of the z = 0 plane: rows are (A B C), (D E F), (G H I).
    private readonly struct Matrix3x3Lite(float a, float b, float c, float d, float e, float f, float g, float h, float i)
    {
        public float A { get; } = a;

        public float B { get; } = b;

        public float C { get; } = c;

        public float D { get; } = d;

        public float E { get; } = e;

        public float F { get; } = f;

        public float G { get; } = g;

        public float H { get; } = h;

        public float I { get; } = i;

        public bool TryInvert(out Matrix3x3Lite inv)
        {
            double det = (A * ((E * I) - (F * H))) - (B * ((D * I) - (F * G))) + (C * ((D * H) - (E * G)));
            inv = default;
            if (Math.Abs(det) < 1e-12 || !double.IsFinite(det))
            {
                return false;
            }

            double k = 1.0 / det;
            inv = new Matrix3x3Lite(
                (float)(((E * I) - (F * H)) * k), (float)(((C * H) - (B * I)) * k), (float)(((B * F) - (C * E)) * k),
                (float)(((F * G) - (D * I)) * k), (float)(((A * I) - (C * G)) * k), (float)(((C * D) - (A * F)) * k),
                (float)(((D * H) - (E * G)) * k), (float)(((B * G) - (A * H)) * k), (float)(((A * E) - (B * D)) * k));
            return true;
        }
    }
}
