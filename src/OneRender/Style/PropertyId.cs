namespace OneRender.Style;

/// <summary>The longhand properties the engine computes. The order is the order they are computed in: <c>font-size</c> first, because other lengths use it.</summary>
internal enum PropertyId : byte
{
    FontSize,
    Display,
    Position,
    Float,
    Clear,
    BoxSizing,
    Top,
    Right,
    Bottom,
    Left,
    Width,
    Height,
    MinWidth,
    MinHeight,
    MaxWidth,
    MaxHeight,
    AspectRatio,
    MarginTop,
    MarginRight,
    MarginBottom,
    MarginLeft,
    PaddingTop,
    PaddingRight,
    PaddingBottom,
    PaddingLeft,
    BorderTopWidth,
    BorderRightWidth,
    BorderBottomWidth,
    BorderLeftWidth,
    BorderTopStyle,
    BorderRightStyle,
    BorderBottomStyle,
    BorderLeftStyle,
    BorderTopColor,
    BorderRightColor,
    BorderBottomColor,
    BorderLeftColor,
    BorderTopLeftRadius,
    BorderTopRightRadius,
    BorderBottomRightRadius,
    BorderBottomLeftRadius,
    OverflowX,
    OverflowY,
    ZIndex,
    Visibility,
    FontFamily,
    FontWeight,
    FontStyle,
    FontStretch,
    LineHeight,
    VerticalAlign,
    WhiteSpaceCollapse,
    TextWrapMode,
    TextAlign,
    TextIndent,
    LetterSpacing,
    WordSpacing,
    Direction,
    UnicodeBidi,
    Color,
    Opacity,
    BackgroundColor,
    Content,
    ListStyleType,
    ListStylePosition,
    WordBreak,
    OverflowWrap,
    LineBreak,
    Hyphens,
    TabSize,
    TextAlignLast,
    TextJustify,
    OverflowClipMargin,
    FlexDirection,
    FlexWrap,
    FlexGrow,
    FlexShrink,
    FlexBasis,
    Order,
    JustifyContent,
    AlignContent,
    AlignItems,
    AlignSelf,
    RowGap,
    ColumnGap,
}

/// <summary>Names and inheritance of <see cref="PropertyId"/> values.</summary>
internal static class Properties
{
    public const int Count = (int)PropertyId.ColumnGap + 1;

    private static readonly string[] Names =
    [
        "font-size", "display", "position", "float", "clear", "box-sizing", "top", "right", "bottom", "left",
        "width", "height", "min-width", "min-height", "max-width", "max-height", "aspect-ratio",
        "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding-top", "padding-right", "padding-bottom", "padding-left",
        "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
        "border-top-style", "border-right-style", "border-bottom-style", "border-left-style",
        "border-top-color", "border-right-color", "border-bottom-color", "border-left-color",
        "border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius",
        "overflow-x", "overflow-y", "z-index", "visibility",
        "font-family", "font-weight", "font-style", "font-stretch", "line-height", "vertical-align",
        "white-space-collapse", "text-wrap-mode", "text-align", "text-indent", "letter-spacing", "word-spacing",
        "direction", "unicode-bidi", "color", "opacity", "background-color", "content", "list-style-type", "list-style-position",
        "word-break", "overflow-wrap", "line-break", "hyphens", "tab-size", "text-align-last", "text-justify", "overflow-clip-margin",
        "flex-direction", "flex-wrap", "flex-grow", "flex-shrink", "flex-basis", "order",
        "justify-content", "align-content", "align-items", "align-self", "row-gap", "column-gap",
    ];

    private static readonly Dictionary<string, PropertyId> ByName = BuildByName();

    private static readonly Dictionary<string, PropertyId>.AlternateLookup<ReadOnlySpan<char>> BySpan = ByName.GetAlternateLookup<ReadOnlySpan<char>>();

    public static string NameOf(PropertyId id) => Names[(int)id];

    public static bool TryGet(ReadOnlySpan<char> name, out PropertyId id) => BySpan.TryGetValue(name, out id);

    /// <summary>Whether the property inherits by default (the "Inherited: yes" line of its definition).</summary>
    public static bool IsInherited(PropertyId id) => id switch
    {
        PropertyId.Visibility or PropertyId.FontSize or PropertyId.FontFamily or PropertyId.FontWeight or PropertyId.FontStyle
            or PropertyId.FontStretch or PropertyId.LineHeight or PropertyId.WhiteSpaceCollapse or PropertyId.TextWrapMode
            or PropertyId.TextAlign or PropertyId.TextIndent or PropertyId.LetterSpacing or PropertyId.WordSpacing
            or PropertyId.Direction or PropertyId.Color or PropertyId.ListStyleType or PropertyId.ListStylePosition
            or PropertyId.WordBreak or PropertyId.OverflowWrap or PropertyId.LineBreak or PropertyId.Hyphens or PropertyId.TabSize
            or PropertyId.TextAlignLast or PropertyId.TextJustify => true,
        _ => false,
    };

    private static Dictionary<string, PropertyId> BuildByName()
    {
        var map = new Dictionary<string, PropertyId>(Names.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Names.Length; i++)
        {
            map[Names[i]] = (PropertyId)i;
        }

        // CSS Text 3 §5.5: word-wrap is a legacy alias of overflow-wrap.
        map["word-wrap"] = PropertyId.OverflowWrap;

        return map;
    }
}
