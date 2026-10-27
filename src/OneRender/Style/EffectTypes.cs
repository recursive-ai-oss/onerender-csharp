using System.Numerics;
using OneRender.Painting;

namespace OneRender.Style;

/// <summary>The transform functions of CSS Transforms 1 §12 and Transforms 2 §15, with their shorthands folded together.</summary>
internal enum TransformKind : byte
{
    /// <summary><c>translate()</c>, <c>translateX/Y/Z()</c>, <c>translate3d()</c>.</summary>
    Translate,

    /// <summary><c>scale()</c>, <c>scaleX/Y/Z()</c>, <c>scale3d()</c>.</summary>
    Scale,

    /// <summary><c>rotate()</c>, <c>rotateX/Y/Z()</c>, <c>rotate3d()</c>: an axis and an angle.</summary>
    Rotate,

    /// <summary><c>skew()</c>, <c>skewX()</c>, <c>skewY()</c>.</summary>
    Skew,

    /// <summary><c>perspective()</c>.</summary>
    Perspective,

    /// <summary><c>matrix()</c> and <c>matrix3d()</c>.</summary>
    Matrix,
}

/// <summary>One transform function with its arguments; lengths may still be percentages of the reference box.</summary>
internal sealed class TransformFunction
{
    public TransformKind Kind;

    /// <summary>Translate x and y.</summary>
    public Length X = Length.Zero;

    public Length Y = Length.Zero;

    /// <summary>Translate z (px) or the depth of <c>perspective()</c> (0 for none).</summary>
    public double Z;

    /// <summary>Scale factors, rotate axis (x, y, z), or skew angles (x, y in A, B).</summary>
    public double A = 1, B = 1, C = 1;

    /// <summary>Rotate angle in radians.</summary>
    public double Angle;

    /// <summary>The matrix of <c>matrix()</c> / <c>matrix3d()</c>, in the row-vector layout of <see cref="Matrix4x4"/>.</summary>
    public Matrix4x4 M = Matrix4x4.Identity;

    public Matrix4x4 ToMatrix(double refW, double refH)
    {
        switch (Kind)
        {
            case TransformKind.Translate:
                return Matrix4x4.CreateTranslation((float)X.ResolveOr(refW, 0), (float)Y.ResolveOr(refH, 0), (float)Z);
            case TransformKind.Scale:
                return Matrix4x4.CreateScale((float)A, (float)B, (float)C);
            case TransformKind.Skew:
                {
                    Matrix4x4 m = Matrix4x4.Identity;
                    m.M21 = (float)Math.Tan(A);
                    m.M12 = (float)Math.Tan(B);
                    return m;
                }

            case TransformKind.Perspective:
                {
                    Matrix4x4 m = Matrix4x4.Identity;
                    if (Z > 0)
                    {
                        m.M34 = (float)(-1.0 / Z);
                    }

                    return m;
                }

            case TransformKind.Matrix:
                return M;
            default:
                return RotateMatrix(A, B, C, Angle);
        }
    }

    /// <summary>Transforms 2 §15.4 (rotate3d): the matrix3d of a rotation around a vector, in listing order.</summary>
    public static Matrix4x4 RotateMatrix(double x, double y, double z, double angle)
    {
        double len = Math.Sqrt((x * x) + (y * y) + (z * z));
        if (len == 0 || !double.IsFinite(len))
        {
            return Matrix4x4.Identity; // a zero vector is no rotation
        }

        x /= len;
        y /= len;
        z /= len;
        double s = Math.Sin(angle / 2), c = Math.Cos(angle / 2);
        double sc = s * c, sq = s * s;
        return new Matrix4x4(
            (float)(1 - (2 * ((y * y) + (z * z)) * sq)), (float)(2 * ((x * y * sq) + (z * sc))), (float)(2 * ((x * z * sq) - (y * sc))), 0,
            (float)(2 * ((x * y * sq) - (z * sc))), (float)(1 - (2 * ((x * x) + (z * z)) * sq)), (float)(2 * ((y * z * sq) + (x * sc))), 0,
            (float)(2 * ((x * z * sq) + (y * sc))), (float)(2 * ((y * z * sq) - (x * sc))), (float)(1 - (2 * ((x * x) + (y * y)) * sq)), 0,
            0, 0, 0, 1);
    }
}

/// <summary>A computed <c>transform</c>, <c>translate</c>, <c>rotate</c> or <c>scale</c>: a list of functions. Immutable.</summary>
internal sealed class TransformSpec : IEquatable<TransformSpec>
{
    /// <summary>The most functions one value may hold; longer lists are invalid.</summary>
    public const int MaxFunctions = 64;

    public static readonly TransformSpec None = new([], "none");

    private string? _text;

    public TransformSpec(TransformFunction[] functions, string text)
    {
        Functions = functions;
        _text = text;
    }

    public TransformFunction[] Functions { get; }

    /// <summary>
    /// Canonical text; two specs are equal when it is. A spec an animation changes in place builds its text on demand, after
    /// <see cref="Touch"/> dropped the old one, so a running animation does not format a string every frame.
    /// </summary>
    public string Text => _text ??= Describe();

    /// <summary>Counts in-place changes made through <see cref="Touch"/>; part of equality so a changed working copy is a different value.</summary>
    public int Revision { get; private set; }

    /// <summary>Tells the spec its functions were changed in place.</summary>
    public void Touch()
    {
        _text = null;
        Revision++;
    }

    private string Describe()
    {
        if (Functions.Length == 0)
        {
            return "none";
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Functions.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            StyleComputer.DescribeTransform(Functions[i], sb);
        }

        return sb.ToString();
    }

    public bool IsNone => Functions.Length == 0;

    /// <summary>Multiplies the functions in order into one matrix: the first function is the outermost (Transforms 1 §13).</summary>
    public Matrix4x4 ToMatrix(double refW, double refH)
    {
        Matrix4x4 acc = Matrix4x4.Identity;
        for (int i = 0; i < Functions.Length; i++)
        {
            acc = Functions[i].ToMatrix(refW, refH) * acc;
        }

        return acc;
    }

    public bool Equals(TransformSpec? other) => other is not null && Revision == other.Revision && string.Equals(Text, other.Text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as TransformSpec);

    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);
}

/// <summary>The filter functions of Filter Effects 1 §5.</summary>
internal enum FilterKind : byte
{
    Blur,
    Brightness,
    Contrast,
    Grayscale,
    HueRotate,
    Invert,
    Opacity,
    Saturate,
    Sepia,
    DropShadow,

    /// <summary><c>url(#id)</c>: an SVG filter element (not rendered; a diagnostic is raised).</summary>
    Url,
}

/// <summary>One filter function with its computed argument.</summary>
internal readonly record struct FilterFunction(FilterKind Kind, double Amount, double OffsetX, double OffsetY, ColorValue Color, string? Url);

/// <summary>A computed <c>filter</c> or <c>backdrop-filter</c>. Immutable.</summary>
internal sealed class FilterSpec : IEquatable<FilterSpec>
{
    /// <summary>The most functions in one chain; a longer list is invalid.</summary>
    public const int MaxFunctions = 32;

    public static readonly FilterSpec None = new([], "none");

    public FilterSpec(FilterFunction[] functions, string text)
    {
        Functions = functions;
        Text = text;
    }

    public FilterFunction[] Functions { get; }

    public string Text { get; }

    public bool IsNone => Functions.Length == 0;

    public bool Equals(FilterSpec? other) => other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as FilterSpec);

    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);
}

/// <summary>The shapes of <c>clip-path</c> (CSS Shapes 1 §3.1 and Masking 1 §5.1).</summary>
internal enum ShapeKind : byte
{
    Inset,
    Circle,
    Ellipse,
    Polygon,
    Path,
}

/// <summary>The geometry boxes that give a shape its reference box (Masking 1 §5.1, Box 4).</summary>
internal enum GeometryBox : byte
{
    BorderBox,
    PaddingBox,
    ContentBox,
    MarginBox,
    FillBox,
    StrokeBox,
    ViewBox,
}

/// <summary>A computed <c>clip-path</c>. Immutable.</summary>
internal sealed class ClipPathSpec : IEquatable<ClipPathSpec>
{
    /// <summary>The most polygon points or path data bytes accepted.</summary>
    public const int MaxPoints = 4096;

    public static readonly ClipPathSpec None = new(ClipPathKind.None, "none");

    public ClipPathSpec(ClipPathKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public ClipPathKind Kind { get; }

    public string Text { get; }

    /// <summary>The reference box; border-box when none was given.</summary>
    public GeometryBox Box { get; init; } = GeometryBox.BorderBox;

    /// <summary>True when a shape was written (so the box is only its reference).</summary>
    public ShapeKind Shape { get; init; }

    /// <summary>Inset: four insets (top, right, bottom, left) as lengths; circle: radius in X; ellipse: rx, ry in X, Y.</summary>
    public Length[] Lengths { get; init; } = [];

    /// <summary>The <c>at</c> position of circle and ellipse.</summary>
    public PositionSpec Center { get; init; } = PositionSpec.Center;

    /// <summary>Circle/ellipse radius keywords: 0 length, 1 closest-side, 2 farthest-side.</summary>
    public byte RadiusKeyX { get; init; }

    public byte RadiusKeyY { get; init; }

    /// <summary>Inset corner radii (eight lengths: horizontal and vertical of each corner), or empty.</summary>
    public Length[] Radii { get; init; } = [];

    /// <summary>Polygon points as x, y pairs.</summary>
    public Length[] Points { get; init; } = [];

    /// <summary>True when the polygon uses even-odd.</summary>
    public bool EvenOdd { get; init; }

    /// <summary>The <c>path()</c> data.</summary>
    public string? PathData { get; init; }

    /// <summary>The fragment id of <c>url(#id)</c>.</summary>
    public string? Url { get; init; }

    public bool Equals(ClipPathSpec? other) => other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ClipPathSpec);

    public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);
}

/// <summary>Whether a <c>clip-path</c> is absent, a box, a shape or a reference.</summary>
internal enum ClipPathKind : byte
{
    None,
    Box,
    Shape,
    Reference,
}

/// <summary>Masking 1 §6: how a mask layer's pixels become coverage.</summary>
internal enum MaskModeKind : byte
{
    MatchSource,
    Alpha,
    Luminance,
}

/// <summary>Masking 1 §6.8: how a mask layer combines with the layers below it.</summary>
internal enum MaskCompositeKind : byte
{
    Add,
    Subtract,
    Intersect,
    Exclude,
}

/// <summary>The layered <c>mask-*</c> longhands. Layer <i>i</i> takes entry <c>i mod Length</c> of each list. Immutable.</summary>
internal sealed class MaskSpec : IEquatable<MaskSpec>
{
    public static readonly MaskSpec None = new();

    /// <summary>The most layers; more are ignored (the extra images are dropped with a diagnostic by the painter).</summary>
    public const int MaxLayers = 16;

    public ImageSpec[] Images { get; internal set; } = [ImageSpec.None];

    public MaskModeKind[] Modes { get; internal set; } = [MaskModeKind.MatchSource];

    public (BackgroundRepeatKind X, BackgroundRepeatKind Y)[] Repeats { get; internal set; } = [(BackgroundRepeatKind.Repeat, BackgroundRepeatKind.Repeat)];

    public PositionAxis[] PositionsX { get; internal set; } = [PositionAxis.Start];

    public PositionAxis[] PositionsY { get; internal set; } = [PositionAxis.Start];

    public BackgroundSizeSpec[] Sizes { get; internal set; } = [BackgroundSizeSpec.Auto];

    public BackgroundBoxKind[] Origins { get; internal set; } = [BackgroundBoxKind.BorderBox];

    public BackgroundBoxKind[] Clips { get; internal set; } = [BackgroundBoxKind.BorderBox];

    public MaskCompositeKind[] Composites { get; internal set; } = [MaskCompositeKind.Add];

    /// <summary>A copy to modify.</summary>
    public MaskSpec Copy()
    {
        var c = (MaskSpec)MemberwiseClone();
        c._background = null;
        return c;
    }

    private BackgroundSpec? _background;

    /// <summary>The layers as a <see cref="BackgroundSpec"/>, so a mask layer is positioned, sized and repeated like a background layer (Masking 1 §6.3 to §6.6). Built once.</summary>
    public BackgroundSpec AsBackground => _background ??= BackgroundSpec.Default.With(Images, Repeats, PositionsX, PositionsY, Sizes, Origins, Clips, null);

    /// <summary>True when at least one layer has an image.</summary>
    public bool HasImages
    {
        get
        {
            foreach (ImageSpec i in Images)
            {
                if (i.Kind != ImageSourceKind.None)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool Equals(MaskSpec? other) =>
        other is not null &&
        Images.AsSpan().SequenceEqual(other.Images) &&
        Modes.AsSpan().SequenceEqual(other.Modes) &&
        Repeats.AsSpan().SequenceEqual(other.Repeats) &&
        PositionsX.AsSpan().SequenceEqual(other.PositionsX) &&
        PositionsY.AsSpan().SequenceEqual(other.PositionsY) &&
        Sizes.AsSpan().SequenceEqual(other.Sizes) &&
        Origins.AsSpan().SequenceEqual(other.Origins) &&
        Clips.AsSpan().SequenceEqual(other.Clips) &&
        Composites.AsSpan().SequenceEqual(other.Composites);

    public override bool Equals(object? obj) => Equals(obj as MaskSpec);

    public override int GetHashCode() => HashCode.Combine(Images.Length, Modes.Length, PositionsX.Length);
}

/// <summary><c>transform-style</c> (Transforms 2 §10); only <c>flat</c> is rendered, <c>preserve-3d</c> flattens.</summary>
internal enum TransformStyleKind : byte
{
    Flat,
    Preserve3D,
}

/// <summary><c>transform-box</c> (Transforms 1 §6).</summary>
internal enum TransformBoxKind : byte
{
    ContentBox,
    BorderBox,
    FillBox,
    StrokeBox,
    ViewBox,
}
