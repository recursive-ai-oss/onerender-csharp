using RecursiveAI.OneHtml.Css;

namespace OneRender.Style;

/// <summary>What a <see cref="Length"/> holds.</summary>
public enum LengthKind : byte
{
    /// <summary>The keyword <c>auto</c>. Layout resolves it by the rules of the property.</summary>
    Auto,

    /// <summary>An absolute length in CSS px. Every absolute, font-relative and viewport-relative unit computes to this.</summary>
    Px,

    /// <summary>A percentage; <see cref="Length.Value"/> is the number as written (50 for <c>50%</c>). Layout supplies the basis.</summary>
    Percent,

    /// <summary>A <c>calc()</c>, <c>min()</c>, <c>max()</c> or <c>clamp()</c> that still contains a percentage; resolve with <see cref="Length.Resolve(double)"/>.</summary>
    Calc,

    /// <summary>A plain number (<c>line-height: 1.5</c>); <see cref="Length.Value"/> is the factor.</summary>
    Number,

    /// <summary>The keyword <c>normal</c> (<c>line-height</c>, <c>letter-spacing</c>, <c>word-spacing</c>).</summary>
    Normal,

    /// <summary>The keyword <c>none</c> (<c>max-width</c>, <c>max-height</c>).</summary>
    None,

    /// <summary>The keyword <c>min-content</c>.</summary>
    MinContent,

    /// <summary>The keyword <c>max-content</c>.</summary>
    MaxContent,

    /// <summary>The keyword <c>fit-content</c>.</summary>
    FitContent,

    /// <summary><c>fit-content(&lt;length&gt;)</c> with an absolute argument in <see cref="Length.Value"/> (px).</summary>
    FitContentPx,

    /// <summary><c>fit-content(&lt;percentage&gt;)</c> with the percentage in <see cref="Length.Value"/>.</summary>
    FitContentPercent,

    /// <summary>The keyword <c>stretch</c> (also written <c>-webkit-fill-available</c>).</summary>
    Stretch,

    /// <summary>The keyword <c>content</c> (<c>flex-basis</c>); reserved for the flex layout.</summary>
    Content,
}

/// <summary>
/// A computed <c>&lt;length-percentage&gt;</c> or sizing keyword. Lengths are in CSS px; percentages and calculations
/// that contain percentages stay unresolved because their basis is only known during layout (CSS Values 4 §5.1.2).
/// </summary>
public readonly struct Length : IEquatable<Length>
{
    private readonly CalcValue? _calc;

    internal Length(LengthKind kind, double value, CalcValue? calc = null)
    {
        Kind = kind;
        Value = value;
        _calc = calc;
    }

    /// <summary>What the value is.</summary>
    public LengthKind Kind { get; }

    /// <summary>The number: px for <see cref="LengthKind.Px"/>, the percentage for <see cref="LengthKind.Percent"/>, the factor for <see cref="LengthKind.Number"/>; 0 for keywords.</summary>
    public double Value { get; }

    /// <summary>The keyword <c>auto</c>.</summary>
    public static Length Auto => new(LengthKind.Auto, 0);

    /// <summary>Zero px.</summary>
    public static Length Zero => new(LengthKind.Px, 0);

    /// <summary>The keyword <c>none</c>.</summary>
    public static Length None => new(LengthKind.None, 0);

    /// <summary>The keyword <c>normal</c>.</summary>
    public static Length Normal => new(LengthKind.Normal, 0);

    /// <summary>A length in px.</summary>
    /// <param name="px">The size; NaN becomes 0.</param>
    public static Length FromPx(double px) => new(LengthKind.Px, double.IsNaN(px) ? 0 : px);

    /// <summary>A percentage.</summary>
    /// <param name="percent">The number as written (50 for <c>50%</c>); NaN becomes 0.</param>
    public static Length FromPercent(double percent) => new(LengthKind.Percent, double.IsNaN(percent) ? 0 : percent);

    /// <summary>True for <see cref="LengthKind.Auto"/>.</summary>
    public bool IsAuto => Kind == LengthKind.Auto;

    /// <summary>True for an absolute length.</summary>
    public bool IsPx => Kind == LengthKind.Px;

    /// <summary>True when the value needs a percentage basis: a percentage, a calculation with one, or <c>fit-content(&lt;percentage&gt;)</c>.</summary>
    public bool IsPercentage => Kind is LengthKind.Percent or LengthKind.Calc or LengthKind.FitContentPercent;

    /// <summary>True for the intrinsic sizing keywords (<c>min-content</c>, <c>max-content</c>, <c>fit-content</c> in any form, <c>stretch</c>).</summary>
    public bool IsIntrinsicKeyword => Kind is LengthKind.MinContent or LengthKind.MaxContent or LengthKind.FitContent or LengthKind.FitContentPx or LengthKind.FitContentPercent or LengthKind.Stretch;

    /// <summary>Resolves a <see cref="LengthKind.Px"/>, <see cref="LengthKind.Percent"/> or <see cref="LengthKind.Calc"/> value.</summary>
    /// <param name="percentBasis">What 100% is, in px. Ignored for absolute lengths.</param>
    /// <returns>The size in px; for any other kind 0.</returns>
    public double Resolve(double percentBasis) => Kind switch
    {
        LengthKind.Px => Value,
        LengthKind.Percent => Value / 100 * percentBasis,
        LengthKind.Calc => _calc!.Resolve(percentBasis),
        _ => 0,
    };

    /// <summary>Resolves the value like <see cref="Resolve(double)"/>, or returns <paramref name="fallback"/> for keywords.</summary>
    /// <param name="percentBasis">What 100% is, in px.</param>
    /// <param name="fallback">The result for <c>auto</c>, <c>none</c>, <c>normal</c> and the sizing keywords.</param>
    public double ResolveOr(double percentBasis, double fallback) =>
        Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc ? Resolve(percentBasis) : fallback;

    /// <summary>The value as CSS text in the form a computed style serializes.</summary>
    public string ToCss() => Kind switch
    {
        LengthKind.Auto => "auto",
        LengthKind.Px => CssText.Px(Value),
        LengthKind.Percent => CssText.Number(Value) + "%",
        LengthKind.Calc => _calc!.Text,
        LengthKind.Number => CssText.Number(Value),
        LengthKind.Normal => "normal",
        LengthKind.None => "none",
        LengthKind.MinContent => "min-content",
        LengthKind.MaxContent => "max-content",
        LengthKind.FitContent => "fit-content",
        LengthKind.FitContentPx => "fit-content(" + CssText.Px(Value) + ")",
        LengthKind.FitContentPercent => "fit-content(" + CssText.Number(Value) + "%)",
        LengthKind.Stretch => "stretch",
        _ => "content",
    };

    /// <inheritdoc />
    public bool Equals(Length other) => Kind == other.Kind && Value.Equals(other.Value) && (ReferenceEquals(_calc, other._calc) || (_calc is not null && _calc.Equals(other._calc)));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Length l && Equals(l);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Value);

    /// <summary>Equality.</summary>
    public static bool operator ==(Length left, Length right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(Length left, Length right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => ToCss();
}

/// <summary>A calculation that contains a percentage, kept as parsed with the font and viewport context it was written in.</summary>
internal sealed class CalcValue(CssQuantity quantity, in LengthContext context) : IEquatable<CalcValue>
{
    private readonly CssQuantity _quantity = quantity;
    private readonly LengthContext _context = context;

    public double Resolve(double percentBasis)
    {
        double v = _quantity.Resolve(in _context, percentBasis);
        return double.IsNaN(v) ? 0 : v;
    }

    public string Text => _quantity.ToCss();

    /// <summary>Two calculations are the same when they print the same and were written in the same font and viewport context.</summary>
    public bool Equals(CalcValue? other) => other is not null && _context.Equals(other._context) && string.Equals(Text, other.Text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as CalcValue);

    public override int GetHashCode() => _context.FontSize.GetHashCode();
}

/// <summary>Number formatting for serialized values: invariant, shortest round-trip, no exponent for ordinary sizes.</summary>
internal static class CssText
{
    public static string Number(double v)
    {
        if (double.IsNaN(v))
        {
            return "0";
        }

        if (double.IsPositiveInfinity(v))
        {
            return "infinity";
        }

        if (double.IsNegativeInfinity(v))
        {
            return "-infinity";
        }

        double r = Math.Round(v, 6);
        return r.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string Px(double v) => Number(v) + "px";
}
