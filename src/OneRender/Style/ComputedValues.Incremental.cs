namespace OneRender.Style;

public partial struct ComputedValues
{
    /// <summary>
    /// True when two styles differ at most in values that only paint reads (colors, backgrounds, shadows, outlines, text decoration,
    /// and opacity; transforms, filters and masks can make containing blocks or ink overflow, so they take the layout path), so a change from one to the other needs a repaint and no layout. Layout reads none of these; anything
    /// not listed here is assumed to matter to layout.
    /// </summary>
    internal static bool DiffersOnlyInPaint(in ComputedValues a, in ComputedValues b)
    {
        ComputedValues t = a;
        t.Color = b.Color;
        t.BackgroundColor = b.BackgroundColor;
        t.BorderTopColor = b.BorderTopColor;
        t.BorderRightColor = b.BorderRightColor;
        t.BorderBottomColor = b.BorderBottomColor;
        t.BorderLeftColor = b.BorderLeftColor;
        t.Background = b.Background;
        t.BoxShadow = b.BoxShadow;
        t.TextShadow = b.TextShadow;
        t.OutlineWidth = b.OutlineWidth;
        t.OutlineStyle = b.OutlineStyle;
        t.OutlineAuto = b.OutlineAuto;
        t.OutlineColor = b.OutlineColor;
        t.OutlineOffset = b.OutlineOffset;
        t.TextDecorationLine = b.TextDecorationLine;
        t.TextDecorationStyle = b.TextDecorationStyle;
        t.TextDecorationColor = b.TextDecorationColor;
        t.TextDecorationThickness = b.TextDecorationThickness;
        t.TextUnderlineOffset = b.TextUnderlineOffset;
        t.TextUnderlinePosition = b.TextUnderlinePosition;
        t.ObjectFit = b.ObjectFit;
        t.ObjectPosition = b.ObjectPosition;
        t.ImageRendering = b.ImageRendering;
        t.ListStyleImage = b.ListStyleImage;
        t.Opacity = b.Opacity;
        t.ControlState = b.ControlState;
        t.ControlFraction = b.ControlFraction;
        return ValueEquals(in t, in b);
    }
}
