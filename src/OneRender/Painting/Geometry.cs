using System.Numerics;

namespace OneRender.Painting;

/// <summary>
/// An axis-aligned rectangle in CSS pixels. A rectangle whose width or height is not positive is empty.
/// </summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge (y grows downward).</param>
/// <param name="Width">Width; zero or negative means empty.</param>
/// <param name="Height">Height; zero or negative means empty.</param>
public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    /// <summary>The empty rectangle at the origin.</summary>
    public static RectF Empty => default;

    /// <summary>
    /// A rectangle larger than any real content, the clip a list starts with. Its numbers are far inside the finite range so
    /// intersections stay exact.
    /// </summary>
    public static RectF Unbounded => new(-1e9f, -1e9f, 2e9f, 2e9f);

    /// <summary>The right edge, <c>X + Width</c>.</summary>
    public float Right => X + Width;

    /// <summary>The bottom edge, <c>Y + Height</c>.</summary>
    public float Bottom => Y + Height;

    /// <summary>True when the rectangle covers no area (width or height is zero, negative or NaN).</summary>
    public bool IsEmpty => !(Width > 0f && Height > 0f);

    /// <summary>The smallest rectangle containing both; an empty operand is ignored.</summary>
    /// <param name="a">First rectangle.</param>
    /// <param name="b">Second rectangle.</param>
    /// <returns>The bounding rectangle, or <see cref="Empty"/> when both are empty.</returns>
    public static RectF Union(in RectF a, in RectF b)
    {
        if (a.IsEmpty)
        {
            return b.IsEmpty ? Empty : b;
        }

        if (b.IsEmpty)
        {
            return a;
        }

        float x0 = Math.Min(a.X, b.X);
        float y0 = Math.Min(a.Y, b.Y);
        float x1 = Math.Max(a.Right, b.Right);
        float y1 = Math.Max(a.Bottom, b.Bottom);
        return new RectF(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>The overlap of two rectangles, or <see cref="Empty"/> when they share no area.</summary>
    /// <param name="a">First rectangle.</param>
    /// <param name="b">Second rectangle.</param>
    /// <returns>The intersection.</returns>
    public static RectF Intersect(in RectF a, in RectF b)
    {
        // When one contains the other, return it untouched so values are not perturbed by the subtraction below.
        if (a.X >= b.X && a.Y >= b.Y && a.Right <= b.Right && a.Bottom <= b.Bottom)
        {
            return a.IsEmpty ? Empty : a;
        }

        if (b.X >= a.X && b.Y >= a.Y && b.Right <= a.Right && b.Bottom <= a.Bottom)
        {
            return b.IsEmpty ? Empty : b;
        }

        float x0 = Math.Max(a.X, b.X);
        float y0 = Math.Max(a.Y, b.Y);
        float x1 = Math.Min(a.Right, b.Right);
        float y1 = Math.Min(a.Bottom, b.Bottom);
        return x1 > x0 && y1 > y0 ? new RectF(x0, y0, x1 - x0, y1 - y0) : Empty;
    }

    /// <summary>True when the rectangles overlap or share an edge (both must be non-empty).</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>Whether the closed rectangles touch.</returns>
    public bool Touches(in RectF other) =>
        !IsEmpty && !other.IsEmpty && X <= other.Right && other.X <= Right && Y <= other.Bottom && other.Y <= Bottom;

    /// <summary>The bounding box of this rectangle's four corners mapped through <paramref name="m"/>.</summary>
    /// <param name="m">An affine transform in row-vector form: <c>x' = x*M11 + y*M21 + M31</c>, <c>y' = x*M12 + y*M22 + M32</c>.</param>
    /// <returns>The transformed bounds; empty when this rectangle is empty.</returns>
    public RectF Transform(in Matrix3x2 m)
    {
        if (IsEmpty)
        {
            return Empty;
        }

        float x0 = X, y0 = Y, x1 = Right, y1 = Bottom;
        float ax = (x0 * m.M11) + (y0 * m.M21) + m.M31, ay = (x0 * m.M12) + (y0 * m.M22) + m.M32;
        float bx = (x1 * m.M11) + (y0 * m.M21) + m.M31, by = (x1 * m.M12) + (y0 * m.M22) + m.M32;
        float cx = (x1 * m.M11) + (y1 * m.M21) + m.M31, cy = (x1 * m.M12) + (y1 * m.M22) + m.M32;
        float dx = (x0 * m.M11) + (y1 * m.M21) + m.M31, dy = (x0 * m.M12) + (y1 * m.M22) + m.M32;
        float minX = Math.Min(Math.Min(ax, bx), Math.Min(cx, dx));
        float maxX = Math.Max(Math.Max(ax, bx), Math.Max(cx, dx));
        float minY = Math.Min(Math.Min(ay, by), Math.Min(cy, dy));
        float maxY = Math.Max(Math.Max(ay, by), Math.Max(cy, dy));
        return new RectF(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>This rectangle grown by <paramref name="amount"/> on every side (shrunk when negative).</summary>
    /// <param name="amount">Distance in CSS pixels.</param>
    /// <returns>The grown rectangle; empty if shrinking removes all area.</returns>
    public RectF Inflate(float amount)
    {
        RectF r = new(X - amount, Y - amount, Width + (2f * amount), Height + (2f * amount));
        return r.IsEmpty ? Empty : r;
    }
}

/// <summary>An integer pixel rectangle (device pixels), used to report damage rounded outward to whole pixels.</summary>
/// <param name="X">Left pixel column.</param>
/// <param name="Y">Top pixel row.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct RectI(int X, int Y, int Width, int Height)
{
    /// <summary>Right edge, exclusive.</summary>
    public int Right => X + Width;

    /// <summary>Bottom edge, exclusive.</summary>
    public int Bottom => Y + Height;

    /// <summary>True when the rectangle has no pixels.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// A color in sRGB with straight (not premultiplied) alpha, each channel in 0 to 1. Colors from wider gamuts or other spaces are
/// resolved to sRGB by the painter before they enter the list.
/// </summary>
/// <param name="R">Red, 0 to 1.</param>
/// <param name="G">Green, 0 to 1.</param>
/// <param name="B">Blue, 0 to 1.</param>
/// <param name="A">Alpha, 0 (transparent) to 1 (opaque).</param>
public readonly record struct PaintColor(float R, float G, float B, float A)
{
    /// <summary>Fully transparent black.</summary>
    public static PaintColor Transparent => default;

    /// <summary>Opaque black.</summary>
    public static PaintColor Black => new(0f, 0f, 0f, 1f);

    /// <summary>Opaque white.</summary>
    public static PaintColor White => new(1f, 1f, 1f, 1f);

    /// <summary>True when alpha is zero, so painting the color changes nothing.</summary>
    public bool IsTransparent => !(A > 0f);

    /// <summary>Builds a color from 8-bit channels.</summary>
    /// <param name="r">Red, 0 to 255.</param>
    /// <param name="g">Green, 0 to 255.</param>
    /// <param name="b">Blue, 0 to 255.</param>
    /// <param name="a">Alpha, 0 to 255.</param>
    /// <returns>The color with each channel divided by 255.</returns>
    public static PaintColor FromBytes(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>The 8-bit value of a 0 to 1 channel, rounded half up and clamped.</summary>
    /// <param name="channel">A channel value.</param>
    /// <returns>0 to 255.</returns>
    public static byte ToByte(float channel) => (byte)Math.Clamp((int)((channel * 255f) + 0.5f), 0, 255);
}

/// <summary>
/// The eight radii of a rounded rectangle, each corner with a horizontal and a vertical radius (CSS Backgrounds 3 §5.1).
/// </summary>
/// <param name="TopLeftX">Horizontal radius of the top-left corner.</param>
/// <param name="TopLeftY">Vertical radius of the top-left corner.</param>
/// <param name="TopRightX">Horizontal radius of the top-right corner.</param>
/// <param name="TopRightY">Vertical radius of the top-right corner.</param>
/// <param name="BottomRightX">Horizontal radius of the bottom-right corner.</param>
/// <param name="BottomRightY">Vertical radius of the bottom-right corner.</param>
/// <param name="BottomLeftX">Horizontal radius of the bottom-left corner.</param>
/// <param name="BottomLeftY">Vertical radius of the bottom-left corner.</param>
public readonly record struct CornerRadii(
    float TopLeftX,
    float TopLeftY,
    float TopRightX,
    float TopRightY,
    float BottomRightX,
    float BottomRightY,
    float BottomLeftX,
    float BottomLeftY)
{
    /// <summary>Square corners.</summary>
    public static CornerRadii Zero => default;

    /// <summary>True when every radius is zero or less.</summary>
    public bool IsZero =>
        !(TopLeftX > 0f) && !(TopLeftY > 0f) && !(TopRightX > 0f) && !(TopRightY > 0f) &&
        !(BottomRightX > 0f) && !(BottomRightY > 0f) && !(BottomLeftX > 0f) && !(BottomLeftY > 0f);

    /// <summary>The same circular radius on all four corners.</summary>
    /// <param name="radius">The radius in CSS pixels.</param>
    /// <returns>The radii.</returns>
    public static CornerRadii Uniform(float radius) => new(radius, radius, radius, radius, radius, radius, radius, radius);

    /// <summary>
    /// The radii after the "overlapping curves" rule of CSS Backgrounds 3 §5.5: negative radii become zero, and if the radii on
    /// any side add up to more than that side's length, all eight are multiplied by the one factor that makes the worst side fit.
    /// The display list stores radii in this form, so consumers never repeat the rule.
    /// </summary>
    /// <param name="width">Width of the box the radii belong to.</param>
    /// <param name="height">Height of the box.</param>
    /// <returns>The constrained radii.</returns>
    public CornerRadii Constrained(float width, float height)
    {
        float tlx = Math.Max(0f, TopLeftX), tly = Math.Max(0f, TopLeftY);
        float trx = Math.Max(0f, TopRightX), try_ = Math.Max(0f, TopRightY);
        float brx = Math.Max(0f, BottomRightX), bry = Math.Max(0f, BottomRightY);
        float blx = Math.Max(0f, BottomLeftX), bly = Math.Max(0f, BottomLeftY);
        float w = Math.Max(0f, width), h = Math.Max(0f, height);
        float f = 1f;
        f = Fit(f, w, tlx + trx);
        f = Fit(f, w, blx + brx);
        f = Fit(f, h, tly + bly);
        f = Fit(f, h, try_ + bry);
        return f >= 1f
            ? new CornerRadii(tlx, tly, trx, try_, brx, bry, blx, bly)
            : new CornerRadii(tlx * f, tly * f, trx * f, try_ * f, brx * f, bry * f, blx * f, bly * f);
    }

    private static float Fit(float factor, float length, float sum) =>
        sum > length && sum > 0f ? Math.Min(factor, length / sum) : factor;
}
