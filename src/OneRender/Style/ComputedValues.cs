using OneRender.Painting;

namespace OneRender.Style;

/// <summary>An immutable <c>font-family</c> list: family names in order, generic keywords lower-cased (CSS Fonts 4 §2.1).</summary>
public sealed class FontFamilyList : IEquatable<FontFamilyList>
{
    private readonly string[] _names;
    private readonly bool[] _generic;

    internal FontFamilyList(string[] names, bool[] generic)
    {
        _names = names;
        _generic = generic;
    }

    /// <summary>The initial value: <c>serif</c>, the user agent's default family.</summary>
    public static FontFamilyList Default { get; } = new(["serif"], [true]);

    /// <summary>The number of entries; at least 1.</summary>
    public int Count => _names.Length;

    /// <summary>The family name or generic keyword at <paramref name="index"/>.</summary>
    /// <param name="index">0 up to <see cref="Count"/> exclusive.</param>
    public string this[int index] => _names[index];

    /// <summary>Whether the entry at <paramref name="index"/> is a generic family keyword.</summary>
    /// <param name="index">0 up to <see cref="Count"/> exclusive.</param>
    public bool IsGeneric(int index) => _generic[index];

    /// <summary>The list as CSS text.</summary>
    public string ToCss()
    {
        var parts = new string[_names.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = _generic[i] || IsPlainIdentifierRun(_names[i]) ? _names[i] : "\"" + _names[i].Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        return string.Join(", ", parts);
    }

    private static bool IsPlainIdentifierRun(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        foreach (string word in name.Split(' '))
        {
            if (word.Length == 0 || char.IsAsciiDigit(word[0]) || (word[0] == '-' && (word.Length == 1 || char.IsAsciiDigit(word[1]))))
            {
                return false;
            }

            foreach (char c in word)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || c > 0x7F))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <inheritdoc />
    public bool Equals(FontFamilyList? other)
    {
        if (other is null || other._names.Length != _names.Length)
        {
            return false;
        }

        for (int i = 0; i < _names.Length; i++)
        {
            if (!string.Equals(_names[i], other._names[i], StringComparison.Ordinal) || _generic[i] != other._generic[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FontFamilyList);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (string n in _names)
        {
            h.Add(n, StringComparer.Ordinal);
        }

        return h.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => ToCss();
}

/// <summary>
/// The computed values of every property the engine reads, for one distinct style (CSS Cascade 5 §4.4 "computed value").
/// Lengths are CSS px; percentages, <c>auto</c> and calculations with percentages stay unresolved. Instances live in a
/// <see cref="StyleTable"/>, shared by every node with the same style; read them through <c>ref readonly</c>.
/// </summary>
public partial struct ComputedValues
{
    /// <summary>The initial values of all properties (the style of a node with no declarations and no parent).</summary>
    public static readonly ComputedValues Initial = CreateInitial();

    /// <summary><c>display</c>.</summary>
    public DisplayType Display { get; internal set; }

    /// <summary><c>position</c>.</summary>
    public PositionKind Position { get; internal set; }

    /// <summary><c>float</c>.</summary>
    public FloatKind Floating { get; internal set; }

    /// <summary><c>clear</c>.</summary>
    public ClearKind Clear { get; internal set; }

    /// <summary><c>box-sizing</c>.</summary>
    public BoxSizingKind BoxSizing { get; internal set; }

    /// <summary><c>top</c>.</summary>
    public Length Top { get; internal set; }

    /// <summary><c>right</c>.</summary>
    public Length Right { get; internal set; }

    /// <summary><c>bottom</c>.</summary>
    public Length Bottom { get; internal set; }

    /// <summary><c>left</c>.</summary>
    public Length Left { get; internal set; }

    /// <summary><c>width</c>.</summary>
    public Length Width { get; internal set; }

    /// <summary><c>height</c>.</summary>
    public Length Height { get; internal set; }

    /// <summary><c>min-width</c> (<c>auto</c> initially; layout resolves it by the box's formatting context).</summary>
    public Length MinWidth { get; internal set; }

    /// <summary><c>min-height</c>.</summary>
    public Length MinHeight { get; internal set; }

    /// <summary><c>max-width</c> (<c>none</c> initially).</summary>
    public Length MaxWidth { get; internal set; }

    /// <summary><c>max-height</c>.</summary>
    public Length MaxHeight { get; internal set; }

    /// <summary>True when <c>aspect-ratio</c> contains the keyword <c>auto</c>.</summary>
    public bool AspectRatioAuto { get; internal set; }

    /// <summary>The first number of the <c>aspect-ratio</c> ratio; 0 when the property is <c>auto</c> alone.</summary>
    public double AspectRatioWidth { get; internal set; }

    /// <summary>The second number of the <c>aspect-ratio</c> ratio (1 when only one number was written); 0 when the property is <c>auto</c> alone.</summary>
    public double AspectRatioHeight { get; internal set; }

    /// <summary>The preferred ratio of <c>aspect-ratio</c> as width divided by height; 0 when there is none (<c>auto</c> alone, or a ratio with a zero term, which is degenerate and counts as none).</summary>
    public readonly double AspectRatio => AspectRatioWidth > 0 && AspectRatioHeight > 0 ? AspectRatioWidth / AspectRatioHeight : 0;

    /// <summary><c>margin-top</c>.</summary>
    public Length MarginTop { get; internal set; }

    /// <summary><c>margin-right</c>.</summary>
    public Length MarginRight { get; internal set; }

    /// <summary><c>margin-bottom</c>.</summary>
    public Length MarginBottom { get; internal set; }

    /// <summary><c>margin-left</c>.</summary>
    public Length MarginLeft { get; internal set; }

    /// <summary><c>padding-top</c>.</summary>
    public Length PaddingTop { get; internal set; }

    /// <summary><c>padding-right</c>.</summary>
    public Length PaddingRight { get; internal set; }

    /// <summary><c>padding-bottom</c>.</summary>
    public Length PaddingBottom { get; internal set; }

    /// <summary><c>padding-left</c>.</summary>
    public Length PaddingLeft { get; internal set; }

    /// <summary><c>border-top-width</c> in px; 0 when the style is <c>none</c> or <c>hidden</c> (CSS Backgrounds 3 §4.1).</summary>
    public double BorderTopWidth { get; internal set; }

    /// <summary><c>border-right-width</c> in px.</summary>
    public double BorderRightWidth { get; internal set; }

    /// <summary><c>border-bottom-width</c> in px.</summary>
    public double BorderBottomWidth { get; internal set; }

    /// <summary><c>border-left-width</c> in px.</summary>
    public double BorderLeftWidth { get; internal set; }

    /// <summary><c>border-top-style</c>.</summary>
    public BorderStyleKind BorderTopStyle { get; internal set; }

    /// <summary><c>border-right-style</c>.</summary>
    public BorderStyleKind BorderRightStyle { get; internal set; }

    /// <summary><c>border-bottom-style</c>.</summary>
    public BorderStyleKind BorderBottomStyle { get; internal set; }

    /// <summary><c>border-left-style</c>.</summary>
    public BorderStyleKind BorderLeftStyle { get; internal set; }

    /// <summary><c>border-top-color</c> (initially <c>currentcolor</c>).</summary>
    public ColorValue BorderTopColor { get; internal set; }

    /// <summary><c>border-right-color</c>.</summary>
    public ColorValue BorderRightColor { get; internal set; }

    /// <summary><c>border-bottom-color</c>.</summary>
    public ColorValue BorderBottomColor { get; internal set; }

    /// <summary><c>border-left-color</c>.</summary>
    public ColorValue BorderLeftColor { get; internal set; }

    /// <summary><c>border-top-left-radius</c>.</summary>
    public CornerRadius BorderTopLeftRadius { get; internal set; }

    /// <summary><c>border-top-right-radius</c>.</summary>
    public CornerRadius BorderTopRightRadius { get; internal set; }

    /// <summary><c>border-bottom-right-radius</c>.</summary>
    public CornerRadius BorderBottomRightRadius { get; internal set; }

    /// <summary><c>border-bottom-left-radius</c>.</summary>
    public CornerRadius BorderBottomLeftRadius { get; internal set; }

    /// <summary><c>overflow-x</c>, after the visible/clip adjustment of CSS Overflow 3 §3.1.</summary>
    public OverflowKind OverflowX { get; internal set; }

    /// <summary><c>overflow-y</c>.</summary>
    public OverflowKind OverflowY { get; internal set; }

    /// <summary>The length of <c>overflow-clip-margin</c> in px (CSS Overflow 4 §3.2); 0 initially.</summary>
    public double OverflowClipMarginPx { get; internal set; }

    /// <summary>The reference box of <c>overflow-clip-margin</c>; padding-box when the property omits it.</summary>
    public ClipMarginBoxKind OverflowClipMarginBox { get; internal set; }

    /// <summary>True when <c>z-index</c> is <c>auto</c>.</summary>
    public bool ZIndexAuto { get; internal set; }

    /// <summary>The integer <c>z-index</c>; 0 when <see cref="ZIndexAuto"/>.</summary>
    public int ZIndex { get; internal set; }

    /// <summary><c>visibility</c>.</summary>
    public VisibilityKind Visibility { get; internal set; }

    /// <summary><c>font-family</c>.</summary>
    public FontFamilyList FontFamily { get; internal set; }

    /// <summary><c>font-size</c> in px.</summary>
    public double FontSize { get; internal set; }

    /// <summary>
    /// The absolute-size keyword the font size came from, 1 (<c>xx-small</c>) to 8 (<c>xxx-large</c>), or 0 when it did not come from
    /// one. It inherits, and decides whether an element that switches into or out of a bare <c>monospace</c> family list takes a
    /// different size (<see cref="StyleComputer.MonospaceScale"/>).
    /// </summary>
    internal byte FontSizeKeyword { get; set; }

    /// <summary><c>font-weight</c> as a number from 1 to 1000.</summary>
    public double FontWeight { get; internal set; }

    /// <summary><c>font-style</c>.</summary>
    public FontStyleKind FontStyle { get; internal set; }

    /// <summary>The slant angle in degrees when <see cref="FontStyle"/> is <see cref="FontStyleKind.Oblique"/> (14 when the keyword was written alone).</summary>
    public double ObliqueAngle { get; internal set; }

    /// <summary><c>font-stretch</c> as a percentage (<c>condensed</c> is 75, <c>normal</c> 100, <c>expanded</c> 125).</summary>
    public double FontStretch { get; internal set; }

    /// <summary><c>line-height</c>: <see cref="LengthKind.Normal"/>, a <see cref="LengthKind.Number"/> factor, or an absolute <see cref="LengthKind.Px"/> length (percentages are resolved against the font size).</summary>
    public Length LineHeight { get; internal set; }

    /// <summary>The keyword of <c>vertical-align</c>, or <see cref="VerticalAlignKind.Length"/>.</summary>
    public VerticalAlignKind VerticalAlign { get; internal set; }

    /// <summary>The length or percentage of <c>vertical-align</c> when it is not a keyword.</summary>
    public Length VerticalAlignOffset { get; internal set; }

    /// <summary><c>white-space-collapse</c>.</summary>
    public WhiteSpaceCollapseKind WhiteSpaceCollapse { get; internal set; }

    /// <summary><c>text-wrap-mode</c>.</summary>
    public TextWrapModeKind TextWrapMode { get; internal set; }

    /// <summary><c>text-align</c>.</summary>
    public TextAlignKind TextAlign { get; internal set; }

    /// <summary><c>text-indent</c> length or percentage.</summary>
    public Length TextIndent { get; internal set; }

    /// <summary>True when <c>text-indent</c> has <c>hanging</c>.</summary>
    public bool TextIndentHanging { get; internal set; }

    /// <summary>True when <c>text-indent</c> has <c>each-line</c>.</summary>
    public bool TextIndentEachLine { get; internal set; }

    /// <summary><c>letter-spacing</c>: <see cref="LengthKind.Normal"/> or an absolute length.</summary>
    public Length LetterSpacing { get; internal set; }

    /// <summary><c>word-spacing</c>: <see cref="LengthKind.Normal"/>, an absolute length or a percentage.</summary>
    public Length WordSpacing { get; internal set; }

    /// <summary><c>direction</c>.</summary>
    public DirectionKind Direction { get; internal set; }

    /// <summary><c>unicode-bidi</c>.</summary>
    public UnicodeBidiKind UnicodeBidi { get; internal set; }

    /// <summary><c>color</c>; never <c>currentcolor</c> (it inherits).</summary>
    public ColorValue Color { get; internal set; }

    /// <summary>
    /// True when the computed <c>display</c> was inline-level before positioning made it block-level. An absolutely positioned box
    /// like that has the static position it would have had on a line, so it follows <c>text-align</c> (CSS Position 3 §4).
    /// </summary>
    public bool DisplayWasInlineLevel { get; internal set; }

    /// <summary><c>pointer-events</c>; inherited.</summary>
    public PointerEventsKind PointerEvents { get; internal set; }

    /// <summary><c>opacity</c>, clamped to 0 to 1.</summary>
    public double Opacity { get; internal set; }

    /// <summary><c>background-color</c>.</summary>
    public ColorValue BackgroundColor { get; internal set; }

    /// <summary>How <c>content</c> generates boxes.</summary>
    public ContentKind Content { get; internal set; }

    /// <summary>The concatenated strings of <c>content</c> when <see cref="Content"/> is <see cref="ContentKind.Text"/>; the value as written when it is <see cref="ContentKind.Other"/>; otherwise empty.</summary>
    public string ContentText { get; internal set; }

    /// <summary><c>list-style-type</c>: a counter-style name, <c>none</c>, or the text of a string value when <see cref="ListStyleTypeIsString"/>.</summary>
    public string ListStyleType { get; internal set; }

    /// <summary>True when <see cref="ListStyleType"/> holds the text of a string value rather than a keyword.</summary>
    public bool ListStyleTypeIsString { get; internal set; }

    /// <summary><c>list-style-position</c>.</summary>
    public ListStylePositionKind ListStylePosition { get; internal set; }

    /// <summary><c>text-transform</c>; inherited.</summary>
    public TextTransformKind TextTransform { get; internal set; }

    /// <summary><c>word-break</c>.</summary>
    public WordBreakKind WordBreak { get; internal set; }

    /// <summary><c>overflow-wrap</c> (and its alias <c>word-wrap</c>).</summary>
    public OverflowWrapKind OverflowWrap { get; internal set; }

    /// <summary><c>line-break</c>.</summary>
    public LineBreakKind LineBreak { get; internal set; }

    /// <summary><c>hyphens</c>.</summary>
    public HyphensKind Hyphens { get; internal set; }

    /// <summary><c>tab-size</c>: a <see cref="LengthKind.Number"/> of space widths or an absolute <see cref="LengthKind.Px"/> length.</summary>
    public Length TabSize { get; internal set; }

    /// <summary><c>text-align-last</c>.</summary>
    public TextAlignLastKind TextAlignLast { get; internal set; }

    /// <summary><c>text-justify</c>.</summary>
    public TextJustifyKind TextJustify { get; internal set; }
    /// <summary><c>grid-template-columns</c>.</summary>
    public GridTrackList GridTemplateColumns { get; internal set; }

    /// <summary><c>grid-template-rows</c>.</summary>
    public GridTrackList GridTemplateRows { get; internal set; }

    /// <summary><c>grid-template-areas</c>.</summary>
    public GridAreas GridTemplateAreas { get; internal set; }

    /// <summary><c>grid-auto-columns</c>: the pattern of implicit column sizes; never empty.</summary>
    public GridTrackSize[] GridAutoColumns { get; internal set; }

    /// <summary><c>grid-auto-rows</c>: the pattern of implicit row sizes; never empty.</summary>
    public GridTrackSize[] GridAutoRows { get; internal set; }

    /// <summary>The <c>row</c> or <c>column</c> part of <c>grid-auto-flow</c>.</summary>
    public GridAutoFlowKind GridAutoFlow { get; internal set; }

    /// <summary>True when <c>grid-auto-flow</c> has <c>dense</c>.</summary>
    public bool GridAutoFlowDense { get; internal set; }

    /// <summary><c>grid-row-start</c>.</summary>
    public GridLine GridRowStart { get; internal set; }

    /// <summary><c>grid-row-end</c>.</summary>
    public GridLine GridRowEnd { get; internal set; }

    /// <summary><c>grid-column-start</c>.</summary>
    public GridLine GridColumnStart { get; internal set; }

    /// <summary><c>grid-column-end</c>.</summary>
    public GridLine GridColumnEnd { get; internal set; }

    /// <summary><c>justify-items</c> (<c>legacy</c> computes to <c>normal</c>).</summary>
    public AlignKind JustifyItems { get; internal set; }

    /// <summary>The <c>safe</c> or <c>unsafe</c> keyword of <c>justify-items</c>.</summary>
    public AlignOverflowKind JustifyItemsOverflow { get; internal set; }

    /// <summary><c>justify-self</c>.</summary>
    public AlignKind JustifySelf { get; internal set; }

    /// <summary>The <c>safe</c> or <c>unsafe</c> keyword of <c>justify-self</c>.</summary>
    public AlignOverflowKind JustifySelfOverflow { get; internal set; }

    /// <summary>The left and right border widths added.</summary>
    public readonly double BorderHorizontal => BorderLeftWidth + BorderRightWidth;

    /// <summary>The top and bottom border widths added.</summary>
    public readonly double BorderVertical => BorderTopWidth + BorderBottomWidth;

    /// <summary>True when the style establishes a scroll container on either axis (CSS Overflow 3 §3).</summary>
    public readonly bool IsScrollContainer => OverflowX is not (OverflowKind.Visible or OverflowKind.Clip) || OverflowY is not (OverflowKind.Visible or OverflowKind.Clip);

    /// <summary>True when the box is out of flow: floated, absolutely or fixed positioned (CSS2 §9.3.1 and §9.5).</summary>
    public readonly bool IsOutOfFlow => Floating != FloatKind.None || Position is PositionKind.Absolute or PositionKind.Fixed;

    /// <summary><c>flex-direction</c>.</summary>
    public FlexDirectionKind FlexDirection { get; internal set; }

    /// <summary><c>flex-wrap</c>.</summary>
    public FlexWrapKind FlexWrap { get; internal set; }

    /// <summary><c>flex-grow</c>: a non-negative number, 0 initially.</summary>
    public double FlexGrow { get; internal set; }

    /// <summary><c>flex-shrink</c>: a non-negative number, 1 initially.</summary>
    public double FlexShrink { get; internal set; }

    /// <summary><c>flex-basis</c> when it is not <c>content</c>: <c>auto</c>, a length or percentage, or a sizing keyword.</summary>
    public Length FlexBasis { get; internal set; }

    /// <summary>True when <c>flex-basis</c> is <c>content</c> (CSS Flexible Box 1 §7.1.1).</summary>
    public bool FlexBasisContent { get; internal set; }

    /// <summary><c>order</c> (CSS Flexible Box 1 §5.4), 0 initially.</summary>
    public int Order { get; internal set; }

    /// <summary><c>justify-content</c>.</summary>
    public AlignKind JustifyContent { get; internal set; }

    /// <summary>The <c>safe</c> or <c>unsafe</c> keyword of <c>justify-content</c>.</summary>
    public AlignOverflowKind JustifyContentOverflow { get; internal set; }

    /// <summary><c>align-content</c>.</summary>
    public AlignKind AlignContent { get; internal set; }

    /// <summary>The <c>safe</c> or <c>unsafe</c> keyword of <c>align-content</c>.</summary>
    public AlignOverflowKind AlignContentOverflow { get; internal set; }

    /// <summary><c>align-items</c>.</summary>
    public AlignKind AlignItems { get; internal set; }

    /// <summary>The <c>safe</c> or <c>unsafe</c> keyword of <c>align-items</c>.</summary>
    public AlignOverflowKind AlignItemsOverflow { get; internal set; }

    /// <summary><c>align-self</c>.</summary>
    public AlignKind AlignSelf { get; internal set; }

    /// <summary>The <c>safe</c> or <c>unsafe</c> keyword of <c>align-self</c>.</summary>
    public AlignOverflowKind AlignSelfOverflow { get; internal set; }

    /// <summary><c>row-gap</c>: <c>normal</c> (0 in flex containers), a length or a percentage.</summary>
    public Length RowGap { get; internal set; }

    /// <summary><c>column-gap</c>.</summary>
    public Length ColumnGap { get; internal set; }

    /// <summary>The layered <c>background-*</c> longhands other than the color.</summary>
    public BackgroundSpec Background { get; internal set; }

    /// <summary>The <c>border-image-*</c> longhands.</summary>
    public BorderImageSpec BorderImage { get; internal set; }

    /// <summary><c>box-shadow</c>.</summary>
    public ShadowList BoxShadow { get; internal set; }

    /// <summary><c>text-shadow</c> (inherited).</summary>
    public ShadowList TextShadow { get; internal set; }

    /// <summary><c>outline-width</c> in px; 0 when the style is <c>none</c> (CSS UI 4 §4.1).</summary>
    public double OutlineWidth { get; internal set; }

    /// <summary><c>outline-style</c> other than <c>auto</c>; see <see cref="OutlineAuto"/>.</summary>
    public BorderStyleKind OutlineStyle { get; internal set; }

    /// <summary>True for <c>outline-style: auto</c> (a focus-ring look).</summary>
    public bool OutlineAuto { get; internal set; }

    /// <summary><c>outline-color</c>; <c>invert</c> computes to <c>currentcolor</c>.</summary>
    public ColorValue OutlineColor { get; internal set; }

    /// <summary><c>outline-offset</c> in px.</summary>
    public double OutlineOffset { get; internal set; }

    /// <summary><c>text-decoration-line</c>. Not inherited, but it decorates descendant text (Text Decoration 4 §2.1).</summary>
    public TextDecorationLines TextDecorationLine { get; internal set; }

    /// <summary><c>text-decoration-style</c>.</summary>
    public TextDecorationStyleKind TextDecorationStyle { get; internal set; }

    /// <summary><c>text-decoration-color</c>.</summary>
    public ColorValue TextDecorationColor { get; internal set; }

    /// <summary><c>text-decoration-thickness</c>: <see cref="Length.Auto"/>, <see cref="Length.Normal"/> for <c>from-font</c>, or a length or percentage of 1em.</summary>
    public Length TextDecorationThickness { get; internal set; }

    /// <summary><c>text-underline-offset</c>: <see cref="Length.Auto"/> or a length or percentage of 1em.</summary>
    public Length TextUnderlineOffset { get; internal set; }

    /// <summary><c>text-underline-position</c>.</summary>
    public TextUnderlinePositionKind TextUnderlinePosition { get; internal set; }

    /// <summary><c>object-fit</c>.</summary>
    public ObjectFitKind ObjectFit { get; internal set; }

    /// <summary><c>object-position</c>.</summary>
    public PositionSpec ObjectPosition { get; internal set; }

    /// <summary><c>image-rendering</c> (inherited).</summary>
    public ImageRenderingKind ImageRendering { get; internal set; }

    /// <summary><c>list-style-image</c> (inherited).</summary>
    public ImageSpec ListStyleImage { get; internal set; }

    /// <summary><c>transform</c>.</summary>
    internal TransformSpec Transform { get; set; }

    /// <summary>The individual <c>translate</c> property.</summary>
    internal TransformSpec Translate { get; set; }

    /// <summary>The individual <c>rotate</c> property.</summary>
    internal TransformSpec Rotate { get; set; }

    /// <summary>The individual <c>scale</c> property.</summary>
    internal TransformSpec Scale { get; set; }

    /// <summary><c>transform-origin</c> x and y; percentages are of the reference box.</summary>
    internal PositionSpec TransformOrigin { get; set; }

    /// <summary>The z offset of <c>transform-origin</c> in px.</summary>
    internal double TransformOriginZ { get; set; }

    /// <summary><c>transform-box</c>.</summary>
    internal TransformBoxKind TransformBox { get; set; }

    /// <summary><c>perspective</c> in px; 0 for <c>none</c>.</summary>
    internal double Perspective { get; set; }

    /// <summary><c>perspective-origin</c>.</summary>
    internal PositionSpec PerspectiveOrigin { get; set; }

    /// <summary><c>transform-style</c>.</summary>
    internal TransformStyleKind TransformStyle { get; set; }

    /// <summary>True for <c>backface-visibility: hidden</c>.</summary>
    internal bool BackfaceHidden { get; set; }

    /// <summary><c>filter</c>.</summary>
    internal FilterSpec Filter { get; set; }

    /// <summary><c>backdrop-filter</c>.</summary>
    internal FilterSpec BackdropFilter { get; set; }

    /// <summary><c>clip-path</c>.</summary>
    internal ClipPathSpec ClipPath { get; set; }

    /// <summary>The <c>mask-*</c> layer longhands.</summary>
    internal MaskSpec Mask { get; set; }

    /// <summary><c>mix-blend-mode</c>.</summary>
    internal BlendMode MixBlendMode { get; set; }

    /// <summary>True for <c>isolation: isolate</c>.</summary>
    internal bool Isolate { get; set; }

    /// <summary><c>background-blend-mode</c> list; layer <i>i</i> takes entry <c>i mod Length</c>.</summary>
    internal BlendMode[] BackgroundBlendModes { get; set; }

    /// <summary>True for <c>image-orientation: none</c>: images are drawn with the pixel layout stored in the file, ignoring EXIF orientation. Inherited.</summary>
    internal bool ImageOrientationNone { get; set; }

    /// <summary><c>break-before</c>.</summary>
    public BreakKind BreakBefore { get; internal set; }

    /// <summary><c>break-after</c>.</summary>
    public BreakKind BreakAfter { get; internal set; }

    /// <summary><c>break-inside</c>.</summary>
    public BreakInsideKind BreakInside { get; internal set; }

    /// <summary><c>orphans</c>: the fewest lines left before a break (inherited, at least 1, initially 2).</summary>
    public int Orphans { get; internal set; }

    /// <summary><c>widows</c>: the fewest lines carried over a break (inherited, at least 1, initially 2).</summary>
    public int Widows { get; internal set; }

    /// <summary>The page name <c>page</c> gives the box (Paged Media 3 §7.3), or null for <c>auto</c> all the way up. Inherited, because <c>auto</c> means the parent's name.</summary>
    public string? PageName { get; internal set; }

    /// <summary><c>box-decoration-break</c>.</summary>
    public BoxDecorationBreakKind BoxDecorationBreak { get; internal set; }

    /// <summary><c>column-count</c>: 0 stands for <c>auto</c>.</summary>
    public int ColumnCount { get; internal set; }

    /// <summary><c>column-width</c>: <c>auto</c> or a length in px.</summary>
    public Length ColumnWidth { get; internal set; }

    /// <summary><c>column-fill</c>.</summary>
    public ColumnFillKind ColumnFill { get; internal set; }

    /// <summary><c>column-span</c>.</summary>
    public ColumnSpanKind ColumnSpan { get; internal set; }

    /// <summary><c>border-collapse</c> (inherited).</summary>
    public BorderCollapseKind BorderCollapse { get; internal set; }

    /// <summary>The horizontal component of <c>border-spacing</c> in px (inherited; initial 0).</summary>
    public double BorderSpacingH { get; internal set; }

    /// <summary>The vertical component of <c>border-spacing</c> in px (inherited; initial 0).</summary>
    public double BorderSpacingV { get; internal set; }

    /// <summary><c>empty-cells</c> (inherited).</summary>
    public EmptyCellsKind EmptyCells { get; internal set; }

    /// <summary><c>caption-side</c> (inherited).</summary>
    public CaptionSideKind CaptionSide { get; internal set; }

    /// <summary><c>table-layout</c>.</summary>
    public TableLayoutKind TableLayout { get; internal set; }

    /// <summary><c>column-rule-width</c> in px; 0 when the rule style is <c>none</c> or <c>hidden</c>.</summary>
    public double ColumnRuleWidth { get; internal set; }

    /// <summary><c>column-rule-style</c>.</summary>
    public BorderStyleKind ColumnRuleStyle { get; internal set; }

    /// <summary><c>column-rule-color</c>; <c>currentcolor</c> by default.</summary>
    public ColorValue ColumnRuleColor { get; internal set; }

    private static ComputedValues CreateInitial()
    {
        var v = default(ComputedValues);
        v.Display = DisplayType.Inline;
        v.Top = v.Right = v.Bottom = v.Left = Length.Auto;
        v.Width = v.Height = v.MinWidth = v.MinHeight = Length.Auto;
        v.MaxWidth = v.MaxHeight = Length.None;
        v.MarginTop = v.MarginRight = v.MarginBottom = v.MarginLeft = Length.Zero;
        v.PaddingTop = v.PaddingRight = v.PaddingBottom = v.PaddingLeft = Length.Zero;
        v.BorderTopColor = v.BorderRightColor = v.BorderBottomColor = v.BorderLeftColor = ColorValue.CurrentColor;
        var zero = new CornerRadius(Length.Zero, Length.Zero);
        v.BorderTopLeftRadius = v.BorderTopRightRadius = v.BorderBottomRightRadius = v.BorderBottomLeftRadius = zero;
        v.ZIndexAuto = true;
        v.FontFamily = FontFamilyList.Default;
        v.FontSize = 16;
        v.FontSizeKeyword = StyleComputer.MediumKeyword;
        v.FontWeight = 400;
        v.ObliqueAngle = 14;
        v.FontStretch = 100;
        v.LineHeight = Length.Normal;
        v.VerticalAlignOffset = Length.Zero;
        v.TextIndent = Length.Zero;
        v.LetterSpacing = Length.Normal;
        v.WordSpacing = Length.Normal;
        v.Color = ColorValue.Black;
        v.Opacity = 1;
        v.BackgroundColor = ColorValue.Transparent;
        v.ContentText = string.Empty;
        v.ListStyleType = "disc";
        v.TabSize = new Length(LengthKind.Number, 8);
        InitFlex(ref v);
        InitFragmentation(ref v);
        InitGrid(ref v);
        InitPaint(ref v);
        InitInteraction(ref v);
        return v;
    }
}
