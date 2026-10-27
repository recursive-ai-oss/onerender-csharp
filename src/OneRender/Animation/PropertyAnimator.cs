using OneRender.Style;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Animation;

/// <summary>What interpolation needs to know about the element besides the two values (CSS Values 4 §"Interpolation").</summary>
internal readonly struct InterpolationContext
{
    public InterpolationContext(double referenceWidth, double referenceHeight, ColorValue currentColor)
    {
        ReferenceWidth = referenceWidth;
        ReferenceHeight = referenceHeight;
        CurrentColor = currentColor;
    }

    /// <summary>The width percentages in a transform resolve against, when two transform lists have to be interpolated as matrices.</summary>
    public double ReferenceWidth { get; }

    /// <summary>The height percentages in a transform resolve against.</summary>
    public double ReferenceHeight { get; }

    /// <summary>The element's <c>color</c>, which <c>currentcolor</c> resolves to when it meets a real color.</summary>
    public ColorValue CurrentColor { get; }
}

/// <summary>
/// Working objects an animation reuses from tick to tick so interpolating transforms allocates nothing once the first frame has run.
/// </summary>
internal sealed class InterpolationScratch
{
    /// <summary>The interpolated <c>transform</c>, <c>translate</c>, <c>rotate</c> and <c>scale</c>, indexed 0 to 3.</summary>
    public TransformSpec?[] Working { get; } = new TransformSpec?[4];
}

/// <summary>
/// Per-property animation behavior: which properties animate, whether a pair of values can be interpolated, and the interpolation
/// itself, following each property's "Animation type" line (CSS Values 4 §"Combining values", Web Animations 1 §5.3) and the module that
/// defines it.
/// </summary>
internal static partial class PropertyAnimator
{
    private enum Kind : byte
    {
        None,
        Discrete,
        Number,
        Integer,
        Length,
        Px,
        Color,
        Radius,
        Position,
        Shadow,
        Filter,
        ClipPath,
        Transform,
        Visibility,
        LineHeight,
        ZIndex,
        VerticalAlign,
    }

    private static readonly Kind[] Kinds = BuildKinds();

    private static Kind[] BuildKinds()
    {
        var k = new Kind[Properties.Count];
        for (int i = 0; i < k.Length; i++)
        {
            k[i] = Kind.Discrete;
        }

        // Not animatable at all (CSS Transitions 1 §2.1, CSS Animations 1 §3): the transition and animation longhands themselves, direction and
        // unicode-bidi (CSS Writing Modes 4 §2.1 and §2.2: "Animation type: not animatable").
        for (int i = (int)PropertyId.TransitionProperty; i <= (int)PropertyId.AnimationPlayState; i++)
        {
            k[i] = Kind.None;
        }

        k[(int)PropertyId.Direction] = Kind.None;
        k[(int)PropertyId.UnicodeBidi] = Kind.None;

        void Set(Kind kind, params PropertyId[] ids)
        {
            foreach (PropertyId id in ids)
            {
                k[(int)id] = kind;
            }
        }

        Set(Kind.Number, PropertyId.Opacity, PropertyId.FlexGrow, PropertyId.FlexShrink, PropertyId.FontWeight, PropertyId.FontStretch);
        Set(Kind.Integer, PropertyId.Order, PropertyId.ColumnCount, PropertyId.Orphans, PropertyId.Widows);
        Set(Kind.ZIndex, PropertyId.ZIndex);
        Set(Kind.Length, PropertyId.Top, PropertyId.Right, PropertyId.Bottom, PropertyId.Left, PropertyId.Width, PropertyId.Height,
            PropertyId.MinWidth, PropertyId.MinHeight, PropertyId.MaxWidth, PropertyId.MaxHeight,
            PropertyId.MarginTop, PropertyId.MarginRight, PropertyId.MarginBottom, PropertyId.MarginLeft,
            PropertyId.PaddingTop, PropertyId.PaddingRight, PropertyId.PaddingBottom, PropertyId.PaddingLeft,
            PropertyId.TextIndent, PropertyId.LetterSpacing, PropertyId.WordSpacing, PropertyId.FlexBasis, PropertyId.ColumnWidth,
            PropertyId.TextDecorationThickness, PropertyId.TextUnderlineOffset);
        Set(Kind.Px, PropertyId.FontSize, PropertyId.BorderTopWidth, PropertyId.BorderRightWidth, PropertyId.BorderBottomWidth, PropertyId.BorderLeftWidth,
            PropertyId.OutlineWidth, PropertyId.OutlineOffset, PropertyId.ColumnRuleWidth, PropertyId.OverflowClipMargin, PropertyId.Perspective);
        Set(Kind.Color, PropertyId.Color, PropertyId.BackgroundColor, PropertyId.BorderTopColor, PropertyId.BorderRightColor, PropertyId.BorderBottomColor,
            PropertyId.BorderLeftColor, PropertyId.OutlineColor, PropertyId.TextDecorationColor, PropertyId.ColumnRuleColor);
        Set(Kind.Radius, PropertyId.BorderTopLeftRadius, PropertyId.BorderTopRightRadius, PropertyId.BorderBottomRightRadius, PropertyId.BorderBottomLeftRadius);
        Set(Kind.Position, PropertyId.TransformOrigin, PropertyId.PerspectiveOrigin, PropertyId.ObjectPosition);
        Set(Kind.Shadow, PropertyId.BoxShadow, PropertyId.TextShadow);
        Set(Kind.Filter, PropertyId.Filter, PropertyId.BackdropFilter);
        Set(Kind.ClipPath, PropertyId.ClipPath);
        Set(Kind.Transform, PropertyId.Transform, PropertyId.Translate, PropertyId.Rotate, PropertyId.Scale);
        Set(Kind.Visibility, PropertyId.Visibility);
        Set(Kind.LineHeight, PropertyId.LineHeight);
        Set(Kind.VerticalAlign, PropertyId.VerticalAlign);

        // Row and column gap are keywords or lengths; `normal` is not interpolable, so the pair rule below decides.
        Set(Kind.Length, PropertyId.RowGap, PropertyId.ColumnGap);
        return k;
    }

    /// <summary>True when the property may appear in keyframes and be animated (discrete properties flip at the midpoint).</summary>
    public static bool IsAnimatable(PropertyId id) => Kinds[(int)id] != Kind.None;

    /// <summary>
    /// True when the property has an interpolation of its own (a transition on it starts without <c>allow-discrete</c> when the two
    /// values can be interpolated). Discrete-only properties return false.
    /// </summary>
    public static bool IsInterpolable(PropertyId id) => Kinds[(int)id] > Kind.Discrete;

    /// <summary>True for the properties a compositor can animate without relayout: transform, the individual transform properties and opacity.</summary>
    public static bool IsCompositorProperty(PropertyId id) => id is PropertyId.Transform or PropertyId.Translate or PropertyId.Rotate or PropertyId.Scale or PropertyId.Opacity;

    // ---------------------------------------------------------------------------------------------------------------- accessors

    private static Length GetLength(in ComputedValues v, PropertyId id) => id switch
    {
        PropertyId.Top => v.Top,
        PropertyId.Right => v.Right,
        PropertyId.Bottom => v.Bottom,
        PropertyId.Left => v.Left,
        PropertyId.Width => v.Width,
        PropertyId.Height => v.Height,
        PropertyId.MinWidth => v.MinWidth,
        PropertyId.MinHeight => v.MinHeight,
        PropertyId.MaxWidth => v.MaxWidth,
        PropertyId.MaxHeight => v.MaxHeight,
        PropertyId.MarginTop => v.MarginTop,
        PropertyId.MarginRight => v.MarginRight,
        PropertyId.MarginBottom => v.MarginBottom,
        PropertyId.MarginLeft => v.MarginLeft,
        PropertyId.PaddingTop => v.PaddingTop,
        PropertyId.PaddingRight => v.PaddingRight,
        PropertyId.PaddingBottom => v.PaddingBottom,
        PropertyId.PaddingLeft => v.PaddingLeft,
        PropertyId.TextIndent => v.TextIndent,
        PropertyId.LetterSpacing => v.LetterSpacing,
        PropertyId.WordSpacing => v.WordSpacing,
        PropertyId.FlexBasis => v.FlexBasis,
        PropertyId.ColumnWidth => v.ColumnWidth,
        PropertyId.TextDecorationThickness => v.TextDecorationThickness,
        PropertyId.TextUnderlineOffset => v.TextUnderlineOffset,
        PropertyId.RowGap => v.RowGap,
        PropertyId.ColumnGap => v.ColumnGap,
        _ => Length.Auto,
    };

    private static void SetLength(ref ComputedValues v, PropertyId id, Length value)
    {
        switch (id)
        {
            case PropertyId.Top: v.Top = value; break;
            case PropertyId.Right: v.Right = value; break;
            case PropertyId.Bottom: v.Bottom = value; break;
            case PropertyId.Left: v.Left = value; break;
            case PropertyId.Width: v.Width = value; break;
            case PropertyId.Height: v.Height = value; break;
            case PropertyId.MinWidth: v.MinWidth = value; break;
            case PropertyId.MinHeight: v.MinHeight = value; break;
            case PropertyId.MaxWidth: v.MaxWidth = value; break;
            case PropertyId.MaxHeight: v.MaxHeight = value; break;
            case PropertyId.MarginTop: v.MarginTop = value; break;
            case PropertyId.MarginRight: v.MarginRight = value; break;
            case PropertyId.MarginBottom: v.MarginBottom = value; break;
            case PropertyId.MarginLeft: v.MarginLeft = value; break;
            case PropertyId.PaddingTop: v.PaddingTop = value; break;
            case PropertyId.PaddingRight: v.PaddingRight = value; break;
            case PropertyId.PaddingBottom: v.PaddingBottom = value; break;
            case PropertyId.PaddingLeft: v.PaddingLeft = value; break;
            case PropertyId.TextIndent: v.TextIndent = value; break;
            case PropertyId.LetterSpacing: v.LetterSpacing = value; break;
            case PropertyId.WordSpacing: v.WordSpacing = value; break;
            case PropertyId.FlexBasis: v.FlexBasis = value; break;
            case PropertyId.ColumnWidth: v.ColumnWidth = value; break;
            case PropertyId.TextDecorationThickness: v.TextDecorationThickness = value; break;
            case PropertyId.TextUnderlineOffset: v.TextUnderlineOffset = value; break;
            case PropertyId.RowGap: v.RowGap = value; break;
            case PropertyId.ColumnGap: v.ColumnGap = value; break;
        }
    }

    // Lengths whose range is [0, infinity): the interpolated value is clamped to it (CSS Values 4 §10.? "Range restrictions" via the property's
    // "Percentages / value range"); the others (margins, offsets, letter-spacing, text-indent, vertical-align) may be negative.
    private static bool NonNegative(PropertyId id) => id is PropertyId.Width or PropertyId.Height or PropertyId.MinWidth or PropertyId.MinHeight
        or PropertyId.MaxWidth or PropertyId.MaxHeight or PropertyId.PaddingTop or PropertyId.PaddingRight or PropertyId.PaddingBottom
        or PropertyId.PaddingLeft or PropertyId.ColumnWidth or PropertyId.TextDecorationThickness or PropertyId.FlexBasis or PropertyId.RowGap
        or PropertyId.ColumnGap;

    private static double GetPx(in ComputedValues v, PropertyId id) => id switch
    {
        PropertyId.FontSize => v.FontSize,
        PropertyId.BorderTopWidth => v.BorderTopWidth,
        PropertyId.BorderRightWidth => v.BorderRightWidth,
        PropertyId.BorderBottomWidth => v.BorderBottomWidth,
        PropertyId.BorderLeftWidth => v.BorderLeftWidth,
        PropertyId.OutlineWidth => v.OutlineWidth,
        PropertyId.OutlineOffset => v.OutlineOffset,
        PropertyId.ColumnRuleWidth => v.ColumnRuleWidth,
        PropertyId.OverflowClipMargin => v.OverflowClipMarginPx,
        PropertyId.Perspective => v.Perspective,
        _ => 0,
    };

    private static void SetPx(ref ComputedValues v, PropertyId id, double value)
    {
        switch (id)
        {
            case PropertyId.FontSize: v.FontSize = value; v.FontSizeKeyword = 0; break;
            case PropertyId.BorderTopWidth: v.BorderTopWidth = value; break;
            case PropertyId.BorderRightWidth: v.BorderRightWidth = value; break;
            case PropertyId.BorderBottomWidth: v.BorderBottomWidth = value; break;
            case PropertyId.BorderLeftWidth: v.BorderLeftWidth = value; break;
            case PropertyId.OutlineWidth: v.OutlineWidth = value; break;
            case PropertyId.OutlineOffset: v.OutlineOffset = value; break;
            case PropertyId.ColumnRuleWidth: v.ColumnRuleWidth = value; break;
            case PropertyId.OverflowClipMargin: v.OverflowClipMarginPx = value; break;
            case PropertyId.Perspective: v.Perspective = value; break;
        }
    }

    private static bool PxNonNegative(PropertyId id) => id != PropertyId.OutlineOffset;

    private static double GetNumber(in ComputedValues v, PropertyId id) => id switch
    {
        PropertyId.Opacity => v.Opacity,
        PropertyId.FlexGrow => v.FlexGrow,
        PropertyId.FlexShrink => v.FlexShrink,
        PropertyId.FontWeight => v.FontWeight,
        PropertyId.FontStretch => v.FontStretch,
        PropertyId.Order => v.Order,
        PropertyId.ColumnCount => v.ColumnCount,
        PropertyId.Orphans => v.Orphans,
        PropertyId.Widows => v.Widows,
        _ => 0,
    };

    private static void SetNumber(ref ComputedValues v, PropertyId id, double value)
    {
        switch (id)
        {
            case PropertyId.Opacity: v.Opacity = Math.Clamp(value, 0, 1); break;
            case PropertyId.FlexGrow: v.FlexGrow = Math.Max(0, value); break;
            case PropertyId.FlexShrink: v.FlexShrink = Math.Max(0, value); break;
            case PropertyId.FontWeight: v.FontWeight = Math.Clamp(value, 1, 1000); break;
            case PropertyId.FontStretch: v.FontStretch = Math.Max(0, value); break;
            case PropertyId.Order: v.Order = ToInt(value); break;
            case PropertyId.ColumnCount: v.ColumnCount = Math.Max(1, ToInt(value)); break;
            case PropertyId.Orphans: v.Orphans = Math.Max(1, ToInt(value)); break;
            case PropertyId.Widows: v.Widows = Math.Max(1, ToInt(value)); break;
        }
    }

    // Web Animations 1 §5.3.? "integer": round to the nearest integer, ties toward positive infinity.
    private static int ToInt(double value)
    {
        double r = Math.Floor(value + 0.5);
        return (int)Math.Clamp(r, int.MinValue / 2, int.MaxValue / 2);
    }

    private static ColorValue GetColor(in ComputedValues v, PropertyId id) => id switch
    {
        PropertyId.Color => v.Color,
        PropertyId.BackgroundColor => v.BackgroundColor,
        PropertyId.BorderTopColor => v.BorderTopColor,
        PropertyId.BorderRightColor => v.BorderRightColor,
        PropertyId.BorderBottomColor => v.BorderBottomColor,
        PropertyId.BorderLeftColor => v.BorderLeftColor,
        PropertyId.OutlineColor => v.OutlineColor,
        PropertyId.TextDecorationColor => v.TextDecorationColor,
        PropertyId.ColumnRuleColor => v.ColumnRuleColor,
        _ => ColorValue.Black,
    };

    private static void SetColor(ref ComputedValues v, PropertyId id, ColorValue c)
    {
        switch (id)
        {
            case PropertyId.Color: v.Color = c; break;
            case PropertyId.BackgroundColor: v.BackgroundColor = c; break;
            case PropertyId.BorderTopColor: v.BorderTopColor = c; break;
            case PropertyId.BorderRightColor: v.BorderRightColor = c; break;
            case PropertyId.BorderBottomColor: v.BorderBottomColor = c; break;
            case PropertyId.BorderLeftColor: v.BorderLeftColor = c; break;
            case PropertyId.OutlineColor: v.OutlineColor = c; break;
            case PropertyId.TextDecorationColor: v.TextDecorationColor = c; break;
            case PropertyId.ColumnRuleColor: v.ColumnRuleColor = c; break;
        }
    }

    private static CornerRadius GetRadius(in ComputedValues v, PropertyId id) => id switch
    {
        PropertyId.BorderTopLeftRadius => v.BorderTopLeftRadius,
        PropertyId.BorderTopRightRadius => v.BorderTopRightRadius,
        PropertyId.BorderBottomRightRadius => v.BorderBottomRightRadius,
        _ => v.BorderBottomLeftRadius,
    };

    private static void SetRadius(ref ComputedValues v, PropertyId id, CornerRadius r)
    {
        switch (id)
        {
            case PropertyId.BorderTopLeftRadius: v.BorderTopLeftRadius = r; break;
            case PropertyId.BorderTopRightRadius: v.BorderTopRightRadius = r; break;
            case PropertyId.BorderBottomRightRadius: v.BorderBottomRightRadius = r; break;
            default: v.BorderBottomLeftRadius = r; break;
        }
    }

    private static PositionSpec GetPosition(in ComputedValues v, PropertyId id) => id switch
    {
        PropertyId.TransformOrigin => v.TransformOrigin,
        PropertyId.PerspectiveOrigin => v.PerspectiveOrigin,
        _ => v.ObjectPosition,
    };

    private static void SetPosition(ref ComputedValues v, PropertyId id, PositionSpec p)
    {
        switch (id)
        {
            case PropertyId.TransformOrigin: v.TransformOrigin = p; break;
            case PropertyId.PerspectiveOrigin: v.PerspectiveOrigin = p; break;
            default: v.ObjectPosition = p; break;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- comparison

    /// <summary>
    /// Whether two computed values of the property are the same value. Typed properties compare without allocating; the rest compare
    /// by copying the property into a copy of one style and comparing the styles.
    /// </summary>
    public static bool ValuesEqual(StyleTable table, PropertyId id, in ComputedValues a, in ComputedValues b)
    {
        switch (Kinds[(int)id])
        {
            case Kind.Number:
            case Kind.Integer:
                return GetNumber(in a, id).Equals(GetNumber(in b, id));
            case Kind.Px:
                return GetPx(in a, id).Equals(GetPx(in b, id));
            case Kind.Length:
                return GetLength(in a, id).Equals(GetLength(in b, id)) && (id != PropertyId.FlexBasis || a.FlexBasisContent == b.FlexBasisContent);
            case Kind.Color:
                return GetColor(in a, id).Equals(GetColor(in b, id));
            case Kind.Transform:
                return TransformOf(in a, id).Equals(TransformOf(in b, id));
            case Kind.None:
                return true;
            default:
            {
                ComputedValues x = a;
                table.CopyProperty(id, ref x, in b);
                return ComputedValues.ValueEquals(in x, in a);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- pair rules

    /// <summary>
    /// Whether the two values of a property can be interpolated (CSS Values 4 §"Interpolation", Web Animations 1 §5.3). A pair that cannot, such as
    /// <c>auto</c> and a length, is animated discretely.
    /// </summary>
    public static bool CanInterpolate(PropertyId id, in ComputedValues a, in ComputedValues b)
    {
        switch (Kinds[(int)id])
        {
            case Kind.None:
            case Kind.Discrete:
                return false;
            case Kind.Number:
                return true;
            case Kind.Integer:
                // column-count: auto (0) is a keyword.
                return id != PropertyId.ColumnCount || (a.ColumnCount > 0 && b.ColumnCount > 0);
            case Kind.ZIndex:
                return !a.ZIndexAuto && !b.ZIndexAuto;
            case Kind.Px:
                return id != PropertyId.Perspective || (a.Perspective > 0 && b.Perspective > 0);
            case Kind.Length:
            {
                if (id == PropertyId.FlexBasis && (a.FlexBasisContent || b.FlexBasisContent))
                {
                    return false;
                }

                if (id == PropertyId.TextIndent && (a.TextIndentHanging != b.TextIndentHanging || a.TextIndentEachLine != b.TextIndentEachLine))
                {
                    return false;
                }

                Length la = GetLength(in a, id), lb = GetLength(in b, id);
                bool zeroOk = id is PropertyId.LetterSpacing or PropertyId.WordSpacing; // `normal` is 0 for the purpose of interpolation
                return LengthLike(la, zeroOk) && LengthLike(lb, zeroOk);
            }

            case Kind.LineHeight:
                return (a.LineHeight.Kind == LengthKind.Number && b.LineHeight.Kind == LengthKind.Number) || (LengthLike(a.LineHeight, false) && LengthLike(b.LineHeight, false));
            case Kind.VerticalAlign:
                return a.VerticalAlign == VerticalAlignKind.Length && b.VerticalAlign == VerticalAlignKind.Length && LengthLike(a.VerticalAlignOffset, false) && LengthLike(b.VerticalAlignOffset, false);
            case Kind.Color:
                return true;
            case Kind.Radius:
            {
                CornerRadius ra = GetRadius(in a, id), rb = GetRadius(in b, id);
                return LengthLike(ra.Horizontal, false) && LengthLike(ra.Vertical, false) && LengthLike(rb.Horizontal, false) && LengthLike(rb.Vertical, false);
            }

            case Kind.Position:
            {
                PositionSpec pa = GetPosition(in a, id), pb = GetPosition(in b, id);
                return pa.X.FromEnd == pb.X.FromEnd && pa.Y.FromEnd == pb.Y.FromEnd && LengthLike(pa.X.Offset, false) && LengthLike(pb.X.Offset, false)
                    && LengthLike(pa.Y.Offset, false) && LengthLike(pb.Y.Offset, false);
            }

            case Kind.Shadow:
                return ShadowsInterpolable(id == PropertyId.BoxShadow ? a.BoxShadow : a.TextShadow, id == PropertyId.BoxShadow ? b.BoxShadow : b.TextShadow);
            case Kind.Filter:
                return FiltersInterpolable(id == PropertyId.Filter ? a.Filter : a.BackdropFilter, id == PropertyId.Filter ? b.Filter : b.BackdropFilter);
            case Kind.ClipPath:
                return ClipPathsInterpolable(a.ClipPath, b.ClipPath);
            case Kind.Transform:
                return true;
            case Kind.Visibility:
                return a.Visibility == VisibilityKind.Visible || b.Visibility == VisibilityKind.Visible;
            default:
                return false;
        }
    }

    private static bool LengthLike(Length l, bool zeroForNormal) => l.Kind is LengthKind.Px or LengthKind.Percent or LengthKind.Calc || (zeroForNormal && l.Kind == LengthKind.Normal);

    // ---------------------------------------------------------------------------------------------------------------- interpolation

    /// <summary>
    /// Sets property <paramref name="id"/> of <paramref name="result"/> to the value <paramref name="progress"/> of the way from
    /// <paramref name="from"/> to <paramref name="to"/>. Progress may be outside 0 to 1 (easing overshoot). A pair that cannot be interpolated
    /// is animated discretely: the first value below progress 0.5, the second from there on.
    /// </summary>
    /// <returns>True when the value was interpolated, false when the discrete rule applied.</returns>
    public static bool Interpolate(StyleTable table, PropertyId id, in ComputedValues from, in ComputedValues to, double progress, ref ComputedValues result,
        in InterpolationContext context, InterpolationScratch? scratch)
    {
        if (!double.IsFinite(progress))
        {
            progress = double.IsNaN(progress) ? 0 : progress > 0 ? 1 : 0;
        }

        Kind kind = Kinds[(int)id];
        if (kind == Kind.Visibility)
        {
            result.Visibility = InterpolateVisibility(from.Visibility, to.Visibility, progress);
            return CanInterpolate(id, in from, in to);
        }

        if (!CanInterpolate(id, in from, in to))
        {
            CopyDiscrete(table, id, in from, in to, progress, ref result);
            return false;
        }

        switch (kind)
        {
            case Kind.Number:
                SetNumber(ref result, id, Lerp(GetNumber(in from, id), GetNumber(in to, id), progress));
                return true;
            case Kind.Integer:
                SetNumber(ref result, id, Lerp(GetNumber(in from, id), GetNumber(in to, id), progress));
                return true;
            case Kind.ZIndex:
                result.ZIndexAuto = false;
                result.ZIndex = ToInt(Lerp(from.ZIndex, to.ZIndex, progress));
                return true;
            case Kind.Px:
            {
                double v = Lerp(GetPx(in from, id), GetPx(in to, id), progress);
                SetPx(ref result, id, PxNonNegative(id) ? Math.Max(0, v) : v);
                return true;
            }

            case Kind.Length:
            {
                Length la = GetLength(in from, id), lb = GetLength(in to, id);
                SetLength(ref result, id, LerpLength(la.Kind == LengthKind.Normal ? Length.Zero : la, lb.Kind == LengthKind.Normal ? Length.Zero : lb, progress, NonNegative(id)));
                if (id == PropertyId.FlexBasis)
                {
                    result.FlexBasisContent = false;
                }

                return true;
            }

            case Kind.LineHeight:
                result.LineHeight = from.LineHeight.Kind == LengthKind.Number
                    ? new Length(LengthKind.Number, Math.Max(0, Lerp(from.LineHeight.Value, to.LineHeight.Value, progress)))
                    : LerpLength(from.LineHeight, to.LineHeight, progress, true);
                return true;
            case Kind.VerticalAlign:
                result.VerticalAlign = VerticalAlignKind.Length;
                result.VerticalAlignOffset = LerpLength(from.VerticalAlignOffset, to.VerticalAlignOffset, progress, false);
                return true;
            case Kind.Color:
            {
                ColorValue a = GetColor(in from, id), b = GetColor(in to, id);
                if (a.IsCurrentColor && b.IsCurrentColor)
                {
                    SetColor(ref result, id, a);
                }
                else
                {
                    SetColor(ref result, id, LerpColor(a.Resolve(context.CurrentColor), b.Resolve(context.CurrentColor), progress));
                }

                return true;
            }

            case Kind.Radius:
            {
                CornerRadius ra = GetRadius(in from, id), rb = GetRadius(in to, id);
                SetRadius(ref result, id, new CornerRadius(LerpLength(ra.Horizontal, rb.Horizontal, progress, true), LerpLength(ra.Vertical, rb.Vertical, progress, true)));
                return true;
            }

            case Kind.Position:
            {
                PositionSpec pa = GetPosition(in from, id), pb = GetPosition(in to, id);
                SetPosition(ref result, id, new PositionSpec(
                    new PositionAxis(LerpLength(pa.X.Offset, pb.X.Offset, progress, false), pa.X.FromEnd),
                    new PositionAxis(LerpLength(pa.Y.Offset, pb.Y.Offset, progress, false), pa.Y.FromEnd)));
                if (id == PropertyId.TransformOrigin)
                {
                    result.TransformOriginZ = Lerp(from.TransformOriginZ, to.TransformOriginZ, progress);
                }

                return true;
            }

            case Kind.Shadow:
                if (id == PropertyId.BoxShadow)
                {
                    result.BoxShadow = LerpShadows(from.BoxShadow, to.BoxShadow, progress);
                }
                else
                {
                    result.TextShadow = LerpShadows(from.TextShadow, to.TextShadow, progress);
                }

                return true;
            case Kind.Filter:
                if (id == PropertyId.Filter)
                {
                    result.Filter = LerpFilters(from.Filter, to.Filter, progress);
                }
                else
                {
                    result.BackdropFilter = LerpFilters(from.BackdropFilter, to.BackdropFilter, progress);
                }

                return true;
            case Kind.ClipPath:
                result.ClipPath = LerpClipPaths(from.ClipPath, to.ClipPath, progress);
                return true;
            case Kind.Transform:
                SetTransform(ref result, id, InterpolateTransform(id, TransformOf(in from, id), TransformOf(in to, id), progress, in context, scratch));
                return true;
            default:
                CopyDiscrete(table, id, in from, in to, progress, ref result);
                return false;
        }
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    // Web Animations 1 §5.3 "discrete": the first value for progress below 0.5, the second from there on.
    private static void CopyDiscrete(StyleTable table, PropertyId id, in ComputedValues from, in ComputedValues to, double progress, ref ComputedValues result)
    {
        if (progress < 0.5)
        {
            table.CopyProperty(id, ref result, in from);
        }
        else
        {
            table.CopyProperty(id, ref result, in to);
        }
    }

    // CSS Color 4 §12.3: interpolate premultiplied. All colors are sRGB here (see docs/animations.md for the reason).
    private static ColorValue LerpColor(ColorValue a, ColorValue b, double t)
    {
        double aa = a.A, ab = b.A;
        double alpha = Math.Clamp(Lerp(aa, ab, t), 0, 1);
        if (alpha <= 0)
        {
            return new ColorValue(0, 0, 0, 0);
        }

        float r = Chan(Lerp(a.R * aa, b.R * ab, t) / alpha);
        float g = Chan(Lerp(a.G * aa, b.G * ab, t) / alpha);
        float bl = Chan(Lerp(a.B * aa, b.B * ab, t) / alpha);
        return new ColorValue(r, g, bl, (float)alpha);
    }

    private static float Chan(double v) => (float)Math.Clamp(v, 0, 1);

    private static VisibilityKind InterpolateVisibility(VisibilityKind a, VisibilityKind b, double t)
    {
        // CSS Transitions 1 / Web Animations: visible when either end is visible and progress is strictly between 0 and 1.
        if (a == VisibilityKind.Visible || b == VisibilityKind.Visible)
        {
            if (t <= 0)
            {
                return a;
            }

            if (t >= 1)
            {
                return b;
            }

            return VisibilityKind.Visible;
        }

        return t < 0.5 ? a : b;
    }

    /// <summary>Linear interpolation of two lengths; a px and a percentage mix becomes a <c>calc()</c> (CSS Values 4 §10.? addition of lengths).</summary>
    internal static Length LerpLength(Length a, Length b, double t, bool nonNegative)
    {
        if (a.Kind == LengthKind.Px && b.Kind == LengthKind.Px)
        {
            double v = Lerp(a.Value, b.Value, t);
            return Length.FromPx(nonNegative ? Math.Max(0, v) : v);
        }

        if (a.Kind == LengthKind.Percent && b.Kind == LengthKind.Percent)
        {
            double v = Lerp(a.Value, b.Value, t);
            return Length.FromPercent(nonNegative ? Math.Max(0, v) : v);
        }

        if (a.Kind == LengthKind.Number && b.Kind == LengthKind.Number)
        {
            return new Length(LengthKind.Number, Lerp(a.Value, b.Value, t));
        }

        string text = "calc(" + a.ToCss() + " * " + CssText.Number(1 - t) + " + " + b.ToCss() + " * " + CssText.Number(t) + ")";
        if (CssQuantity.TryParse(CssTree.ParseComponentValueList(text).Values, QuantityKinds.LengthPercentage, out CssQuantity q))
        {
            return new Length(LengthKind.Calc, 0, new CalcValue(q, LengthContext.Default));
        }

        return t < 0.5 ? a : b;
    }
}
