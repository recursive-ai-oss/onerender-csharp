namespace OneRender.Style;

/// <summary>A computed <c>display</c> value in the two-value form of CSS Display 3 §2 (<c>block flow</c>, <c>inline flow-root</c>, <c>block flow list-item</c>, ...).</summary>
public readonly struct DisplayType : IEquatable<DisplayType>
{
    /// <summary>Creates a display value.</summary>
    /// <param name="outside">The outer type.</param>
    /// <param name="inside">The inner type.</param>
    /// <param name="isListItem">True for <c>list-item</c>.</param>
    public DisplayType(OuterDisplay outside, InnerDisplay inside, bool isListItem = false)
    {
        Outside = outside;
        Inside = inside;
        IsListItem = isListItem;
    }

    /// <summary>The outer display type.</summary>
    public OuterDisplay Outside { get; }

    /// <summary>The inner display type, or the table role for <see cref="OuterDisplay.TableInternal"/>.</summary>
    public InnerDisplay Inside { get; }

    /// <summary>True when the box also generates a marker.</summary>
    public bool IsListItem { get; }

    /// <summary><c>inline flow</c>, the initial value.</summary>
    public static DisplayType Inline => new(OuterDisplay.Inline, InnerDisplay.Flow);

    /// <summary><c>block flow</c>.</summary>
    public static DisplayType Block => new(OuterDisplay.Block, InnerDisplay.Flow);

    /// <summary><c>none</c>.</summary>
    public static DisplayType None => new(OuterDisplay.None, InnerDisplay.Flow);

    /// <summary>True for <c>display: none</c>.</summary>
    public bool IsNone => Outside == OuterDisplay.None;

    /// <summary>True for <c>display: contents</c>.</summary>
    public bool IsContents => Outside == OuterDisplay.Contents;

    /// <summary>True for block-level display types.</summary>
    public bool IsBlockLevel => Outside == OuterDisplay.Block;

    /// <summary>True for inline-level display types.</summary>
    public bool IsInlineLevel => Outside == OuterDisplay.Inline;

    /// <summary>
    /// The blockified value (CSS Display 3 §2.7): inline-level types become block-level with the same inner type;
    /// plain <c>inline</c> becomes <c>block flow</c>; table-internal types become <c>block flow</c>; <c>none</c> and <c>contents</c> stay.
    /// </summary>
    public DisplayType Blockified() => Outside switch
    {
        OuterDisplay.Inline => new DisplayType(OuterDisplay.Block, Inside, IsListItem),
        OuterDisplay.TableInternal => Block,
        _ => this,
    };

    /// <summary>The value as CSS text, in the shortest form CSSOM serializes (<c>block</c>, <c>inline-block</c>, <c>list-item</c>, <c>flex</c>, ...).</summary>
    public string ToCss()
    {
        switch (Outside)
        {
            case OuterDisplay.None:
                return "none";
            case OuterDisplay.Contents:
                return "contents";
            case OuterDisplay.TableInternal:
                return Inside switch
                {
                    InnerDisplay.TableRowGroup => "table-row-group",
                    InnerDisplay.TableHeaderGroup => "table-header-group",
                    InnerDisplay.TableFooterGroup => "table-footer-group",
                    InnerDisplay.TableRow => "table-row",
                    InnerDisplay.TableCell => "table-cell",
                    InnerDisplay.TableColumnGroup => "table-column-group",
                    InnerDisplay.TableColumn => "table-column",
                    _ => "table-caption",
                };
        }

        bool block = Outside == OuterDisplay.Block;
        if (IsListItem)
        {
            return Inside == InnerDisplay.Flow ? (block ? "list-item" : "inline list-item") : (block ? string.Empty : "inline ") + InsideName(Inside) + " list-item";
        }

        return Inside switch
        {
            InnerDisplay.Flow => block ? "block" : "inline",
            InnerDisplay.FlowRoot => block ? "flow-root" : "inline-block",
            InnerDisplay.Table => block ? "table" : "inline-table",
            InnerDisplay.Flex => block ? "flex" : "inline-flex",
            InnerDisplay.Grid => block ? "grid" : "inline-grid",
            _ => block ? "block ruby" : "ruby",
        };
    }

    private static string InsideName(InnerDisplay inside) => inside switch
    {
        InnerDisplay.FlowRoot => "flow-root",
        InnerDisplay.Table => "table",
        InnerDisplay.Flex => "flex",
        InnerDisplay.Grid => "grid",
        _ => "flow",
    };

    /// <inheritdoc />
    public bool Equals(DisplayType other) => Outside == other.Outside && Inside == other.Inside && IsListItem == other.IsListItem;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DisplayType d && Equals(d);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Outside, Inside, IsListItem);

    /// <summary>Equality.</summary>
    public static bool operator ==(DisplayType left, DisplayType right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(DisplayType left, DisplayType right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => ToCss();
}

/// <summary>A computed color in gamma-encoded sRGB, or the keyword <c>currentcolor</c> (CSS Color 4 §4.5: it stays a keyword in the computed value).</summary>
public readonly struct ColorValue : IEquatable<ColorValue>
{
    /// <summary>Creates a color.</summary>
    /// <param name="r">Red, 0 to 1.</param>
    /// <param name="g">Green, 0 to 1.</param>
    /// <param name="b">Blue, 0 to 1.</param>
    /// <param name="a">Alpha, 0 to 1.</param>
    /// <param name="isCurrentColor">True for <c>currentcolor</c>; the components are then ignored.</param>
    public ColorValue(float r, float g, float b, float a, bool isCurrentColor = false)
    {
        R = r;
        G = g;
        B = b;
        A = a;
        IsCurrentColor = isCurrentColor;
    }

    /// <summary>Red, 0 to 1.</summary>
    public float R { get; }

    /// <summary>Green, 0 to 1.</summary>
    public float G { get; }

    /// <summary>Blue, 0 to 1.</summary>
    public float B { get; }

    /// <summary>Alpha, 0 to 1.</summary>
    public float A { get; }

    /// <summary>True for the keyword <c>currentcolor</c>.</summary>
    public bool IsCurrentColor { get; }

    /// <summary>Opaque black.</summary>
    public static ColorValue Black => new(0, 0, 0, 1);

    /// <summary>Transparent black.</summary>
    public static ColorValue Transparent => new(0, 0, 0, 0);

    /// <summary>The keyword <c>currentcolor</c>.</summary>
    public static ColorValue CurrentColor => new(0, 0, 0, 1, true);

    /// <summary>The color with <c>currentcolor</c> replaced by <paramref name="current"/>.</summary>
    /// <param name="current">The element's <c>color</c>.</param>
    public ColorValue Resolve(ColorValue current) => IsCurrentColor ? current : this;

    /// <summary>The color in the form <c>getComputedStyle</c> serializes: <c>rgb(r, g, b)</c> or <c>rgba(r, g, b, a)</c>, or <c>currentcolor</c>.</summary>
    public string ToCss()
    {
        if (IsCurrentColor)
        {
            return "currentcolor";
        }

        string rgb = Channel(R) + ", " + Channel(G) + ", " + Channel(B);
        return A >= 1 ? "rgb(" + rgb + ")" : "rgba(" + rgb + ", " + CssText.Number(Math.Round(A, 2)) + ")";
    }

    private static string Channel(float v) => ((int)Math.Round(Math.Clamp(v, 0f, 1f) * 255)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public bool Equals(ColorValue other) => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A) && IsCurrentColor == other.IsCurrentColor;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ColorValue c && Equals(c);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(R, G, B, A, IsCurrentColor);

    /// <summary>Equality.</summary>
    public static bool operator ==(ColorValue left, ColorValue right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(ColorValue left, ColorValue right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => ToCss();
}

/// <summary>A computed border radius for one corner: horizontal and vertical radii, each a non-negative length-percentage (CSS Backgrounds 3 §5.1).</summary>
public readonly struct CornerRadius
{
    internal CornerRadius(Length horizontal, Length vertical)
    {
        Horizontal = horizontal;
        Vertical = vertical;
    }

    /// <summary>The horizontal radius.</summary>
    public Length Horizontal { get; }

    /// <summary>The vertical radius.</summary>
    public Length Vertical { get; }
}
