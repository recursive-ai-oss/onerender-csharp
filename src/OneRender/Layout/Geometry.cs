namespace OneRender.Layout;

/// <summary>An axis-aligned rectangle in CSS px. Width and height are not negative for any rectangle the engine produces.</summary>
public readonly struct Rect : IEquatable<Rect>
{
    /// <summary>Creates a rectangle.</summary>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    public Rect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>The left edge.</summary>
    public double X { get; }

    /// <summary>The top edge.</summary>
    public double Y { get; }

    /// <summary>The width.</summary>
    public double Width { get; }

    /// <summary>The height.</summary>
    public double Height { get; }

    /// <summary>The right edge, <see cref="X"/> plus <see cref="Width"/>.</summary>
    public double Right => X + Width;

    /// <summary>The bottom edge, <see cref="Y"/> plus <see cref="Height"/>.</summary>
    public double Bottom => Y + Height;

    /// <summary>The smallest rectangle that contains both rectangles.</summary>
    /// <param name="other">The other rectangle.</param>
    public Rect Union(Rect other)
    {
        double x = Math.Min(X, other.X);
        double y = Math.Min(Y, other.Y);
        return new Rect(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
    }

    /// <summary>The rectangle moved by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    /// <param name="dx">Horizontal shift.</param>
    /// <param name="dy">Vertical shift.</param>
    public Rect Translate(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    /// <inheritdoc />
    public bool Equals(Rect other) => X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Rect r && Equals(r);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    /// <summary>Equality.</summary>
    public static bool operator ==(Rect left, Rect right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(Rect left, Rect right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => FormattableString.Invariant($"[{X:0.##}, {Y:0.##}, {Width:0.##} x {Height:0.##}]");
}

/// <summary>Four edge sizes in CSS px, in the order top, right, bottom, left.</summary>
public readonly struct Insets : IEquatable<Insets>
{
    /// <summary>Creates insets.</summary>
    /// <param name="top">Top edge.</param>
    /// <param name="right">Right edge.</param>
    /// <param name="bottom">Bottom edge.</param>
    /// <param name="left">Left edge.</param>
    public Insets(double top, double right, double bottom, double left)
    {
        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    /// <summary>The top edge.</summary>
    public double Top { get; }

    /// <summary>The right edge.</summary>
    public double Right { get; }

    /// <summary>The bottom edge.</summary>
    public double Bottom { get; }

    /// <summary>The left edge.</summary>
    public double Left { get; }

    /// <summary>Left plus right.</summary>
    public double Horizontal => Left + Right;

    /// <summary>Top plus bottom.</summary>
    public double Vertical => Top + Bottom;

    /// <inheritdoc />
    public bool Equals(Insets other) => Top.Equals(other.Top) && Right.Equals(other.Right) && Bottom.Equals(other.Bottom) && Left.Equals(other.Left);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Insets i && Equals(i);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Top, Right, Bottom, Left);

    /// <summary>Equality.</summary>
    public static bool operator ==(Insets left, Insets right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(Insets left, Insets right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => FormattableString.Invariant($"({Top:0.##} {Right:0.##} {Bottom:0.##} {Left:0.##})");
}
