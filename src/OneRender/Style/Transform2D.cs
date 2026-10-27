using System.Globalization;

namespace OneRender.Style;

/// <summary>
/// A computed <c>transform</c> list reduced to one 2D affine matrix (CSS Transforms 1 §13). A <c>translate</c> percentage refers to
/// the reference box, which is only known during layout, so the translation part is kept as a constant plus a multiple of the box's
/// width and of its height; composing translations with rotations and scales keeps it linear in those two sizes.
/// </summary>
/// <remarks>Only the 2D functions are understood: 3D functions and <c>perspective()</c> make the declaration invalid.</remarks>
public readonly struct Transform2D : IEquatable<Transform2D>
{
    private readonly double _a;
    private readonly double _b;
    private readonly double _c;
    private readonly double _d;
    private readonly double _e0;
    private readonly double _ew;
    private readonly double _eh;
    private readonly double _f0;
    private readonly double _fw;
    private readonly double _fh;
    private readonly bool _set;

    private Transform2D(double a, double b, double c, double d, double e0, double ew, double eh, double f0, double fw, double fh)
    {
        _a = a;
        _b = b;
        _c = c;
        _d = d;
        _e0 = e0;
        _ew = ew;
        _eh = eh;
        _f0 = f0;
        _fw = fw;
        _fh = fh;
        _set = true;
    }

    /// <summary>The identity, the value of <c>transform: none</c>.</summary>
    public static Transform2D Identity => default;

    /// <summary>True for <c>none</c> and for a list that composes to the identity.</summary>
    public bool IsIdentity => !_set || (_a == 1 && _b == 0 && _c == 0 && _d == 1 && _e0 == 0 && _ew == 0 && _eh == 0 && _f0 == 0 && _fw == 0 && _fh == 0);

    /// <summary>A matrix as <c>matrix(a, b, c, d, e, f)</c> writes it: x' = a·x + c·y + e, y' = b·x + d·y + f.</summary>
    public static Transform2D FromMatrix(double a, double b, double c, double d, double e, double f) => new(a, b, c, d, e, 0, 0, f, 0, 0);

    /// <summary>A translation by a length plus fractions of the reference box (1 = 100%).</summary>
    public static Transform2D FromTranslation(double x, double xWidthFraction, double y, double yHeightFraction) =>
        new(1, 0, 0, 1, x, xWidthFraction, 0, y, 0, yHeightFraction);

    /// <summary>The matrix that applies <paramref name="inner"/> first and then this one (CSS Transforms 1 §13: lists multiply left to right).</summary>
    public Transform2D Then(Transform2D inner)
    {
        if (!_set)
        {
            return inner;
        }

        if (!inner._set)
        {
            return this;
        }

        return new Transform2D(
            (_a * inner._a) + (_c * inner._b),
            (_b * inner._a) + (_d * inner._b),
            (_a * inner._c) + (_c * inner._d),
            (_b * inner._c) + (_d * inner._d),
            (_a * inner._e0) + (_c * inner._f0) + _e0,
            (_a * inner._ew) + (_c * inner._fw) + _ew,
            (_a * inner._eh) + (_c * inner._fh) + _eh,
            (_b * inner._e0) + (_d * inner._f0) + _f0,
            (_b * inner._ew) + (_d * inner._fw) + _fw,
            (_b * inner._eh) + (_d * inner._fh) + _fh);
    }

    /// <summary>Resolves the percentages against a reference box.</summary>
    /// <param name="width">Reference box width in px.</param>
    /// <param name="height">Reference box height in px.</param>
    /// <param name="a">Matrix element a.</param>
    /// <param name="b">Matrix element b.</param>
    /// <param name="c">Matrix element c.</param>
    /// <param name="d">Matrix element d.</param>
    /// <param name="e">Horizontal translation.</param>
    /// <param name="f">Vertical translation.</param>
    public void Resolve(double width, double height, out double a, out double b, out double c, out double d, out double e, out double f)
    {
        if (!_set)
        {
            a = 1;
            b = 0;
            c = 0;
            d = 1;
            e = 0;
            f = 0;
            return;
        }

        a = _a;
        b = _b;
        c = _c;
        d = _d;
        e = _e0 + (_ew * width) + (_eh * height);
        f = _f0 + (_fw * width) + (_fh * height);
    }

    /// <inheritdoc />
    public bool Equals(Transform2D other) =>
        IsIdentity == other.IsIdentity
        && (IsIdentity || (_a == other._a && _b == other._b && _c == other._c && _d == other._d && _e0 == other._e0 && _ew == other._ew
            && _eh == other._eh && _f0 == other._f0 && _fw == other._fw && _fh == other._fh));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Transform2D t && Equals(t);

    /// <inheritdoc />
    public override int GetHashCode() => IsIdentity ? 0 : HashCode.Combine(_a, _b, _c, _d, HashCode.Combine(_e0, _ew, _eh), HashCode.Combine(_f0, _fw, _fh));

    /// <summary>Equality.</summary>
    public static bool operator ==(Transform2D left, Transform2D right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(Transform2D left, Transform2D right) => !left.Equals(right);

    /// <summary>The value as a <c>matrix()</c> (or <c>none</c>); percentages stay symbolic.</summary>
    public string ToCss()
    {
        if (IsIdentity)
        {
            return "none";
        }

        static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
        static string T(double c, double w, double h) =>
            w == 0 && h == 0 ? N(c) + "px" : "(" + N(c) + "px + " + N(w * 100) + "% of width + " + N(h * 100) + "% of height)";
        return "matrix(" + N(_a) + ", " + N(_b) + ", " + N(_c) + ", " + N(_d) + ", " + T(_e0, _ew, _eh) + ", " + T(_f0, _fw, _fh) + ")";
    }
}
