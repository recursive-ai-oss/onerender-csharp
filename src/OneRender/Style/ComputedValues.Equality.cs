namespace OneRender.Style;

public partial struct ComputedValues
{
    internal static bool ValueEquals(in ComputedValues a, in ComputedValues b) =>
        a.Display.Equals(b.Display) &&
        a.Position == b.Position &&
        a.Floating == b.Floating &&
        a.Clear == b.Clear &&
        a.BoxSizing == b.BoxSizing &&
        a.Top.Equals(b.Top) &&
        a.Right.Equals(b.Right) &&
        a.Bottom.Equals(b.Bottom) &&
        a.Left.Equals(b.Left) &&
        a.Width.Equals(b.Width) &&
        a.Height.Equals(b.Height) &&
        a.MinWidth.Equals(b.MinWidth) &&
        a.MinHeight.Equals(b.MinHeight) &&
        a.MaxWidth.Equals(b.MaxWidth) &&
        a.MaxHeight.Equals(b.MaxHeight) &&
        a.AspectRatioAuto.Equals(b.AspectRatioAuto) &&
        a.AspectRatioWidth.Equals(b.AspectRatioWidth) &&
        a.AspectRatioHeight.Equals(b.AspectRatioHeight) &&
        a.MarginTop.Equals(b.MarginTop) &&
        a.MarginRight.Equals(b.MarginRight) &&
        a.MarginBottom.Equals(b.MarginBottom) &&
        a.MarginLeft.Equals(b.MarginLeft) &&
        a.PaddingTop.Equals(b.PaddingTop) &&
        a.PaddingRight.Equals(b.PaddingRight) &&
        a.PaddingBottom.Equals(b.PaddingBottom) &&
        a.PaddingLeft.Equals(b.PaddingLeft) &&
        a.BorderTopWidth.Equals(b.BorderTopWidth) &&
        a.BorderRightWidth.Equals(b.BorderRightWidth) &&
        a.BorderBottomWidth.Equals(b.BorderBottomWidth) &&
        a.BorderLeftWidth.Equals(b.BorderLeftWidth) &&
        a.BorderTopStyle == b.BorderTopStyle &&
        a.BorderRightStyle == b.BorderRightStyle &&
        a.BorderBottomStyle == b.BorderBottomStyle &&
        a.BorderLeftStyle == b.BorderLeftStyle &&
        a.BorderTopColor.Equals(b.BorderTopColor) &&
        a.BorderRightColor.Equals(b.BorderRightColor) &&
        a.BorderBottomColor.Equals(b.BorderBottomColor) &&
        a.BorderLeftColor.Equals(b.BorderLeftColor) &&
        a.BorderTopLeftRadius.Horizontal.Equals(b.BorderTopLeftRadius.Horizontal) && a.BorderTopLeftRadius.Vertical.Equals(b.BorderTopLeftRadius.Vertical) &&
        a.BorderTopRightRadius.Horizontal.Equals(b.BorderTopRightRadius.Horizontal) && a.BorderTopRightRadius.Vertical.Equals(b.BorderTopRightRadius.Vertical) &&
        a.BorderBottomRightRadius.Horizontal.Equals(b.BorderBottomRightRadius.Horizontal) && a.BorderBottomRightRadius.Vertical.Equals(b.BorderBottomRightRadius.Vertical) &&
        a.BorderBottomLeftRadius.Horizontal.Equals(b.BorderBottomLeftRadius.Horizontal) && a.BorderBottomLeftRadius.Vertical.Equals(b.BorderBottomLeftRadius.Vertical) &&
        a.OverflowX == b.OverflowX &&
        a.OverflowY == b.OverflowY &&
        a.ZIndexAuto.Equals(b.ZIndexAuto) &&
        a.ZIndex.Equals(b.ZIndex) &&
        a.Visibility == b.Visibility &&
        a.FontFamily.Equals(b.FontFamily) &&
        a.FontSize.Equals(b.FontSize) &&
        a.FontWeight.Equals(b.FontWeight) &&
        a.FontStyle == b.FontStyle &&
        a.ObliqueAngle.Equals(b.ObliqueAngle) &&
        a.FontStretch.Equals(b.FontStretch) &&
        a.LineHeight.Equals(b.LineHeight) &&
        a.VerticalAlign == b.VerticalAlign &&
        a.VerticalAlignOffset.Equals(b.VerticalAlignOffset) &&
        a.WhiteSpaceCollapse == b.WhiteSpaceCollapse &&
        a.TextWrapMode == b.TextWrapMode &&
        a.TextAlign == b.TextAlign &&
        a.TextIndent.Equals(b.TextIndent) &&
        a.TextIndentHanging.Equals(b.TextIndentHanging) &&
        a.TextIndentEachLine.Equals(b.TextIndentEachLine) &&
        a.LetterSpacing.Equals(b.LetterSpacing) &&
        a.WordSpacing.Equals(b.WordSpacing) &&
        a.Direction == b.Direction &&
        a.UnicodeBidi == b.UnicodeBidi &&
        a.Color.Equals(b.Color) &&
        a.Opacity.Equals(b.Opacity) &&
        a.BackgroundColor.Equals(b.BackgroundColor) &&
        a.Content == b.Content &&
        string.Equals(a.ContentText, b.ContentText, StringComparison.Ordinal) &&
        string.Equals(a.ListStyleType, b.ListStyleType, StringComparison.Ordinal) &&
        a.ListStyleTypeIsString.Equals(b.ListStyleTypeIsString) &&
        a.ListStylePosition == b.ListStylePosition &&
        FlexEquals(in a, in b);

    internal static int ValueHash(in ComputedValues v)
    {
        var h = new HashCode();
        h.Add(v.Display);
        h.Add(v.Position);
        h.Add(v.Floating);
        h.Add(v.Clear);
        h.Add(v.BoxSizing);
        h.Add(v.Top);
        h.Add(v.Right);
        h.Add(v.Bottom);
        h.Add(v.Left);
        h.Add(v.Width);
        h.Add(v.Height);
        h.Add(v.MinWidth);
        h.Add(v.MinHeight);
        h.Add(v.MaxWidth);
        h.Add(v.MaxHeight);
        h.Add(v.AspectRatioAuto);
        h.Add(v.AspectRatioWidth);
        h.Add(v.AspectRatioHeight);
        h.Add(v.MarginTop);
        h.Add(v.MarginRight);
        h.Add(v.MarginBottom);
        h.Add(v.MarginLeft);
        h.Add(v.PaddingTop);
        h.Add(v.PaddingRight);
        h.Add(v.PaddingBottom);
        h.Add(v.PaddingLeft);
        h.Add(v.BorderTopWidth);
        h.Add(v.BorderRightWidth);
        h.Add(v.BorderBottomWidth);
        h.Add(v.BorderLeftWidth);
        h.Add(v.BorderTopStyle);
        h.Add(v.BorderRightStyle);
        h.Add(v.BorderBottomStyle);
        h.Add(v.BorderLeftStyle);
        h.Add(v.BorderTopColor);
        h.Add(v.BorderRightColor);
        h.Add(v.BorderBottomColor);
        h.Add(v.BorderLeftColor);
        h.Add(v.BorderTopLeftRadius.Horizontal); h.Add(v.BorderTopLeftRadius.Vertical);
        h.Add(v.BorderTopRightRadius.Horizontal); h.Add(v.BorderTopRightRadius.Vertical);
        h.Add(v.BorderBottomRightRadius.Horizontal); h.Add(v.BorderBottomRightRadius.Vertical);
        h.Add(v.BorderBottomLeftRadius.Horizontal); h.Add(v.BorderBottomLeftRadius.Vertical);
        h.Add(v.OverflowX);
        h.Add(v.OverflowY);
        h.Add(v.ZIndexAuto);
        h.Add(v.ZIndex);
        h.Add(v.Visibility);
        h.Add(v.FontFamily);
        h.Add(v.FontSize);
        h.Add(v.FontWeight);
        h.Add(v.FontStyle);
        h.Add(v.ObliqueAngle);
        h.Add(v.FontStretch);
        h.Add(v.LineHeight);
        h.Add(v.VerticalAlign);
        h.Add(v.VerticalAlignOffset);
        h.Add(v.WhiteSpaceCollapse);
        h.Add(v.TextWrapMode);
        h.Add(v.TextAlign);
        h.Add(v.TextIndent);
        h.Add(v.TextIndentHanging);
        h.Add(v.TextIndentEachLine);
        h.Add(v.LetterSpacing);
        h.Add(v.WordSpacing);
        h.Add(v.Direction);
        h.Add(v.UnicodeBidi);
        h.Add(v.Color);
        h.Add(v.Opacity);
        h.Add(v.BackgroundColor);
        h.Add(v.Content);
        h.Add(v.ContentText, StringComparer.Ordinal);
        h.Add(v.ListStyleType, StringComparer.Ordinal);
        h.Add(v.ListStyleTypeIsString);
        h.Add(v.ListStylePosition);
        FlexHash(ref h, in v);
        return h.ToHashCode();
    }
}
