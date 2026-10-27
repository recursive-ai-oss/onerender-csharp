namespace OneRender.Style;

/// <summary>Serializes computed values the way <c>getComputedStyle</c> does, for tests and debug dumps.</summary>
internal static partial class ComputedValueText
{
    private static readonly string[] BorderStyles = ["none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset", "outset"];
    private static readonly string[] Overflows = ["visible", "hidden", "clip", "scroll", "auto"];
    private static readonly string[] ClipMarginBoxes = ["padding-box", "content-box", "border-box"];
    private static readonly string[] VerticalAligns = ["baseline", "sub", "super", "text-top", "text-bottom", "middle", "top", "bottom"];
    private static readonly string[] Collapses = ["collapse", "preserve", "preserve-breaks", "preserve-spaces", "break-spaces"];
    private static readonly string[] TextTransforms = ["none", "capitalize", "uppercase", "lowercase"];
    private static readonly string[] WordBreaks = ["normal", "break-all", "keep-all", "break-word"];
    private static readonly string[] OverflowWraps = ["normal", "break-word", "anywhere"];
    private static readonly string[] LineBreaks = ["auto", "loose", "normal", "strict", "anywhere"];
    private static readonly string[] HyphenWords = ["manual", "none", "auto"];
    private static readonly string[] AlignLasts = ["auto", "start", "end", "left", "right", "center", "justify", "match-parent"];
    private static readonly string[] Justifies = ["auto", "none", "inter-word", "inter-character"];
    private static readonly string[] Aligns = ["start", "end", "left", "right", "center", "justify", "match-parent", "justify-all"];
    private static readonly string[] Bidis = ["normal", "embed", "isolate", "bidi-override", "isolate-override", "plaintext"];

    /// <summary>The serialization of one property of a computed style.</summary>
    public static string Get(PropertyId id, in ComputedValues v)
    {
        switch (id)
        {
            case PropertyId.FontSize: return CssText.Px(v.FontSize);
            case PropertyId.Display: return v.Display.ToCss();
            case PropertyId.Position: return v.Position.ToString().ToLowerInvariant();
            case PropertyId.Float: return v.Floating.ToString().ToLowerInvariant();
            case PropertyId.Clear: return v.Clear.ToString().ToLowerInvariant();
            case PropertyId.BoxSizing: return v.BoxSizing == BoxSizingKind.BorderBox ? "border-box" : "content-box";
            case PropertyId.Top: return v.Top.ToCss();
            case PropertyId.Right: return v.Right.ToCss();
            case PropertyId.Bottom: return v.Bottom.ToCss();
            case PropertyId.Left: return v.Left.ToCss();
            case PropertyId.Width: return v.Width.ToCss();
            case PropertyId.Height: return v.Height.ToCss();
            case PropertyId.MinWidth: return v.MinWidth.ToCss();
            case PropertyId.MinHeight: return v.MinHeight.ToCss();
            case PropertyId.MaxWidth: return v.MaxWidth.ToCss();
            case PropertyId.MaxHeight: return v.MaxHeight.ToCss();
            case PropertyId.AspectRatio: return AspectRatioText(in v);
            case PropertyId.MarginTop: return v.MarginTop.ToCss();
            case PropertyId.MarginRight: return v.MarginRight.ToCss();
            case PropertyId.MarginBottom: return v.MarginBottom.ToCss();
            case PropertyId.MarginLeft: return v.MarginLeft.ToCss();
            case PropertyId.PaddingTop: return v.PaddingTop.ToCss();
            case PropertyId.PaddingRight: return v.PaddingRight.ToCss();
            case PropertyId.PaddingBottom: return v.PaddingBottom.ToCss();
            case PropertyId.PaddingLeft: return v.PaddingLeft.ToCss();
            case PropertyId.BorderTopWidth: return CssText.Px(v.BorderTopWidth);
            case PropertyId.BorderRightWidth: return CssText.Px(v.BorderRightWidth);
            case PropertyId.BorderBottomWidth: return CssText.Px(v.BorderBottomWidth);
            case PropertyId.BorderLeftWidth: return CssText.Px(v.BorderLeftWidth);
            case PropertyId.BorderTopStyle: return BorderStyles[(int)v.BorderTopStyle];
            case PropertyId.BorderRightStyle: return BorderStyles[(int)v.BorderRightStyle];
            case PropertyId.BorderBottomStyle: return BorderStyles[(int)v.BorderBottomStyle];
            case PropertyId.BorderLeftStyle: return BorderStyles[(int)v.BorderLeftStyle];
            case PropertyId.BorderTopColor: return v.BorderTopColor.ToCss();
            case PropertyId.BorderRightColor: return v.BorderRightColor.ToCss();
            case PropertyId.BorderBottomColor: return v.BorderBottomColor.ToCss();
            case PropertyId.BorderLeftColor: return v.BorderLeftColor.ToCss();
            case PropertyId.BorderTopLeftRadius: return RadiusText(v.BorderTopLeftRadius);
            case PropertyId.BorderTopRightRadius: return RadiusText(v.BorderTopRightRadius);
            case PropertyId.BorderBottomRightRadius: return RadiusText(v.BorderBottomRightRadius);
            case PropertyId.BorderBottomLeftRadius: return RadiusText(v.BorderBottomLeftRadius);
            case PropertyId.OverflowX: return Overflows[(int)v.OverflowX];
            case PropertyId.OverflowY: return Overflows[(int)v.OverflowY];
            case PropertyId.OverflowClipMargin:
                return ClipMarginBoxes[(int)v.OverflowClipMarginBox] + " " + CssText.Number(v.OverflowClipMarginPx) + "px";
            case PropertyId.ZIndex: return v.ZIndexAuto ? "auto" : v.ZIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case PropertyId.Visibility: return v.Visibility.ToString().ToLowerInvariant();
            case PropertyId.FontFamily: return v.FontFamily.ToCss();
            case PropertyId.FontWeight: return CssText.Number(v.FontWeight);
            case PropertyId.FontStyle:
                return v.FontStyle switch
                {
                    FontStyleKind.Italic => "italic",
                    FontStyleKind.Oblique => v.ObliqueAngle == 14 ? "oblique" : "oblique " + CssText.Number(v.ObliqueAngle) + "deg",
                    _ => "normal",
                };
            case PropertyId.FontStretch: return CssText.Number(v.FontStretch) + "%";
            case PropertyId.LineHeight: return v.LineHeight.ToCss();
            case PropertyId.VerticalAlign: return v.VerticalAlign == VerticalAlignKind.Length ? v.VerticalAlignOffset.ToCss() : VerticalAligns[(int)v.VerticalAlign];
            case PropertyId.WhiteSpaceCollapse: return Collapses[(int)v.WhiteSpaceCollapse];
            case PropertyId.TextWrapMode: return v.TextWrapMode == TextWrapModeKind.NoWrap ? "nowrap" : "wrap";
            case PropertyId.TextAlign: return Aligns[(int)v.TextAlign];
            case PropertyId.TextIndent:
                return v.TextIndent.ToCss() + (v.TextIndentHanging ? " hanging" : string.Empty) + (v.TextIndentEachLine ? " each-line" : string.Empty);
            case PropertyId.LetterSpacing: return v.LetterSpacing.ToCss();
            case PropertyId.WordSpacing: return v.WordSpacing.ToCss();
            case PropertyId.Direction: return v.Direction == DirectionKind.Rtl ? "rtl" : "ltr";
            case PropertyId.UnicodeBidi: return Bidis[(int)v.UnicodeBidi];
            case PropertyId.Color: return v.Color.ToCss();
            case PropertyId.Opacity: return CssText.Number(v.Opacity);
            case PropertyId.BackgroundColor: return v.BackgroundColor.ToCss();
            case PropertyId.Content:
                return v.Content switch
                {
                    ContentKind.None => "none",
                    ContentKind.Text => "\"" + v.ContentText.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\a ", StringComparison.Ordinal) + "\"",
                    ContentKind.Other => v.ContentText,
                    _ => "normal",
                };
            case PropertyId.ListStyleType:
                return v.ListStyleTypeIsString ? "\"" + v.ListStyleType.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : v.ListStyleType;
            case PropertyId.ListStylePosition: return v.ListStylePosition == ListStylePositionKind.Inside ? "inside" : "outside";
            case PropertyId.TextTransform: return TextTransforms[(int)v.TextTransform];
            case PropertyId.WordBreak: return WordBreaks[(int)v.WordBreak];
            case PropertyId.OverflowWrap: return OverflowWraps[(int)v.OverflowWrap];
            case PropertyId.LineBreak: return LineBreaks[(int)v.LineBreak];
            case PropertyId.Hyphens: return HyphenWords[(int)v.Hyphens];
            case PropertyId.TabSize: return v.TabSize.ToCss();
            case PropertyId.TextAlignLast: return AlignLasts[(int)v.TextAlignLast];
            case PropertyId.TextJustify: return Justifies[(int)v.TextJustify];
            case PropertyId.FlexDirection: return v.FlexDirection switch { FlexDirectionKind.RowReverse => "row-reverse", FlexDirectionKind.Column => "column", FlexDirectionKind.ColumnReverse => "column-reverse", _ => "row" };
            case PropertyId.FlexWrap: return v.FlexWrap switch { FlexWrapKind.Wrap => "wrap", FlexWrapKind.WrapReverse => "wrap-reverse", _ => "nowrap" };
            case PropertyId.FlexGrow: return CssText.Number(v.FlexGrow);
            case PropertyId.FlexShrink: return CssText.Number(v.FlexShrink);
            case PropertyId.FlexBasis: return v.FlexBasisContent ? "content" : v.FlexBasis.ToCss();
            case PropertyId.Order: return v.Order.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case PropertyId.JustifyContent: return AlignText(v.JustifyContent, v.JustifyContentOverflow);
            case PropertyId.AlignContent: return AlignText(v.AlignContent, v.AlignContentOverflow);
            case PropertyId.AlignItems: return AlignText(v.AlignItems, v.AlignItemsOverflow);
            case PropertyId.AlignSelf: return AlignText(v.AlignSelf, v.AlignSelfOverflow);
            case PropertyId.RowGap: return v.RowGap.ToCss();
            case PropertyId.ColumnGap: return v.ColumnGap.ToCss();
            case PropertyId.BreakBefore: return BreakText(v.BreakBefore);
            case PropertyId.BreakAfter: return BreakText(v.BreakAfter);
            case PropertyId.BreakInside: return v.BreakInside switch { BreakInsideKind.Avoid => "avoid", BreakInsideKind.AvoidPage => "avoid-page", BreakInsideKind.AvoidColumn => "avoid-column", BreakInsideKind.AvoidRegion => "avoid-region", _ => "auto" };
            case PropertyId.Orphans: return v.Orphans.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case PropertyId.Widows: return v.Widows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case PropertyId.BoxDecorationBreak: return v.BoxDecorationBreak == BoxDecorationBreakKind.Clone ? "clone" : "slice";
            case PropertyId.ColumnCount: return v.ColumnCount == 0 ? "auto" : v.ColumnCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case PropertyId.ColumnWidth: return v.ColumnWidth.ToCss();
            case PropertyId.ColumnFill: return v.ColumnFill == ColumnFillKind.Auto ? "auto" : "balance";
            case PropertyId.ColumnSpan: return v.ColumnSpan == ColumnSpanKind.All ? "all" : "none";
            case PropertyId.ColumnRuleWidth: return CssText.Px(v.ColumnRuleWidth);
            case PropertyId.ColumnRuleStyle: return BorderStyles[(int)v.ColumnRuleStyle];
            case PropertyId.ColumnRuleColor: return v.ColumnRuleColor.ToCss();
            case PropertyId.GridTemplateColumns: return v.GridTemplateColumns.ToString();
            case PropertyId.GridTemplateRows: return v.GridTemplateRows.ToString();
            case PropertyId.GridTemplateAreas: return v.GridTemplateAreas.Text;
            case PropertyId.GridAutoColumns: return string.Join(' ', Array.ConvertAll(v.GridAutoColumns, t => t.ToCss()));
            case PropertyId.GridAutoRows: return string.Join(' ', Array.ConvertAll(v.GridAutoRows, t => t.ToCss()));
            case PropertyId.GridAutoFlow: return (v.GridAutoFlow == GridAutoFlowKind.Column ? "column" : "row") + (v.GridAutoFlowDense ? " dense" : string.Empty);
            case PropertyId.GridRowStart: return v.GridRowStart.ToCss();
            case PropertyId.GridRowEnd: return v.GridRowEnd.ToCss();
            case PropertyId.GridColumnStart: return v.GridColumnStart.ToCss();
            case PropertyId.GridColumnEnd: return v.GridColumnEnd.ToCss();
            case PropertyId.JustifyItems: return AlignText(v.JustifyItems, v.JustifyItemsOverflow);
            case PropertyId.JustifySelf: return AlignText(v.JustifySelf, v.JustifySelfOverflow);
            case PropertyId.BorderCollapse: return v.BorderCollapse == BorderCollapseKind.Collapse ? "collapse" : "separate";
            case PropertyId.BorderSpacing: return CssText.Px(v.BorderSpacingH) + " " + CssText.Px(v.BorderSpacingV);
            case PropertyId.EmptyCells: return v.EmptyCells == EmptyCellsKind.Hide ? "hide" : "show";
            case PropertyId.CaptionSide: return v.CaptionSide == CaptionSideKind.Bottom ? "bottom" : "top";
            case PropertyId.TableLayout: return v.TableLayout == TableLayoutKind.Fixed ? "fixed" : "auto";
            case PropertyId.Page: return v.PageName ?? "auto";
            case PropertyId.PointerEvents: return v.PointerEvents == PointerEventsKind.None ? "none" : "auto";
            case PropertyId.Appearance: return v.Appearance == AppearanceKind.None ? "none" : "auto";
            case PropertyId.CaretColor: return v.CaretColorAuto ? "auto" : v.CaretColor.ToCss();
            case PropertyId.AccentColor: return v.AccentColorAuto ? "auto" : v.AccentColor.ToCss();
            default: return (id >= PropertyId.TransitionProperty && id <= PropertyId.AnimationPlayState ? MotionText(id, in v) : PaintText(id, in v)) ?? string.Empty;
        }
    }

    private static string BreakText(BreakKind k) => k switch
    {
        BreakKind.Avoid => "avoid", BreakKind.AvoidPage => "avoid-page", BreakKind.AvoidColumn => "avoid-column", BreakKind.AvoidRegion => "avoid-region",
        BreakKind.Page => "page", BreakKind.Left => "left", BreakKind.Right => "right", BreakKind.Recto => "recto", BreakKind.Verso => "verso",
        BreakKind.Column => "column", BreakKind.Region => "region", _ => "auto",
    };

    private static string AlignText(AlignKind k, AlignOverflowKind o)
    {
        string w = k switch
        {
            AlignKind.Auto => "auto", AlignKind.Normal => "normal", AlignKind.Stretch => "stretch", AlignKind.FlexStart => "flex-start",
            AlignKind.FlexEnd => "flex-end", AlignKind.Start => "start", AlignKind.End => "end", AlignKind.SelfStart => "self-start",
            AlignKind.SelfEnd => "self-end", AlignKind.Center => "center", AlignKind.Baseline => "baseline", AlignKind.LastBaseline => "last baseline",
            AlignKind.Left => "left", AlignKind.Right => "right", AlignKind.SpaceBetween => "space-between", AlignKind.SpaceAround => "space-around",
            _ => "space-evenly",
        };
        return o == AlignOverflowKind.Safe ? "safe " + w : o == AlignOverflowKind.Unsafe ? "unsafe " + w : w;
    }

    /// <summary>The serialization by property name; null for a property the engine does not compute.</summary>
    public static string? Get(string property, in ComputedValues v) => Properties.TryGet(property, out PropertyId id) ? Get(id, in v) : null;

    /// <summary>Lists the properties whose value differs from the initial value, as <c>name: value</c> separated by <c>; </c>.</summary>
    public static string NonInitial(in ComputedValues v)
    {
        ComputedValues initial = ComputedValues.Initial;
        var parts = new List<string>();
        for (int i = 0; i < Properties.Count; i++)
        {
            var id = (PropertyId)i;
            string now = Get(id, in v);
            if (now != Get(id, in initial))
            {
                parts.Add(Properties.NameOf(id) + ": " + now);
            }
        }

        return string.Join("; ", parts);
    }

    private static string RadiusText(CornerRadius r) =>
        r.Horizontal.Equals(r.Vertical) ? r.Horizontal.ToCss() : r.Horizontal.ToCss() + " " + r.Vertical.ToCss();

    private static string AspectRatioText(in ComputedValues v)
    {
        if (v.AspectRatioWidth <= 0 && v.AspectRatioHeight <= 0)
        {
            return "auto";
        }

        string ratio = CssText.Number(v.AspectRatioWidth) + (v.AspectRatioHeight == 1 ? string.Empty : " / " + CssText.Number(v.AspectRatioHeight));
        return v.AspectRatioAuto ? "auto " + ratio : ratio;
    }
}
