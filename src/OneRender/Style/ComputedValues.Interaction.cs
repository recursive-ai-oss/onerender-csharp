namespace OneRender.Style;

/// <summary>The <c>pointer-events</c> property as it applies to HTML content (CSS UI 4 §6.3).</summary>
public enum PointerEventsKind : byte
{
    /// <summary><c>auto</c>, and the SVG-only painted/fill/stroke keywords, which behave as <c>auto</c> outside SVG.</summary>
    Auto,

    /// <summary><c>none</c>: the element is never the target of a pointer event; its descendants still can be.</summary>
    None,
}

/// <summary>The <c>appearance</c> property (CSS UI 4 §6.1).</summary>
public enum AppearanceKind : byte
{
    /// <summary><c>auto</c> and the compatibility keywords: the control is drawn natively and sized by its widget rules.</summary>
    Auto,

    /// <summary><c>none</c>: the element is an ordinary box with its CSS border and background.</summary>
    None,
}

public partial struct ComputedValues
{
    private static void InitInteraction(ref ComputedValues v)
    {
        v.PointerEvents = PointerEventsKind.Auto;
        v.Control = Forms.ControlKind.None;
        v.ControlState = Forms.ControlStates.None;
        v.ControlFraction = 0;
        v.ThumbFraction = 0;
        v.InnerBaseline = double.NaN;
        v.ControlWidth = double.NaN;
        v.WidthIsIntrinsic = false;
        v.HeightIsIntrinsic = false;
        v.ContentCenteredByPadding = false;
        v.LegendGap = 0;
        v.ControlInnerPadding = 0;
        v.Appearance = AppearanceKind.Auto;
        v.AccentColorAuto = true;
        v.AccentColor = ColorValue.Black;
        v.CaretColorAuto = true;
        v.CaretColor = ColorValue.Black;
        v.ColorScheme = ColorSchemes.Normal;
        v.DarkScheme = false;
        v.HostScope = 0;
        v.HasHostScope = false;
    }

    internal static bool InteractionEquals(in ComputedValues a, in ComputedValues b) =>
        a.PointerEvents == b.PointerEvents && a.Control == b.Control && a.ControlState == b.ControlState && a.ControlFraction.Equals(b.ControlFraction) && a.ThumbFraction.Equals(b.ThumbFraction) && a.DisplayWasInlineLevel == b.DisplayWasInlineLevel
        && a.BaselineAtBorderEdge == b.BaselineAtBorderEdge && a.InnerBaseline.Equals(b.InnerBaseline) && a.ControlWidth.Equals(b.ControlWidth) && a.WidthIsIntrinsic == b.WidthIsIntrinsic && a.HeightIsIntrinsic == b.HeightIsIntrinsic && a.ContentCenteredByPadding == b.ContentCenteredByPadding && a.LegendGap.Equals(b.LegendGap) && a.ControlInnerPadding.Equals(b.ControlInnerPadding) && a.Appearance == b.Appearance && a.AccentColorAuto == b.AccentColorAuto && a.AccentColor.Equals(b.AccentColor) && a.CaretColorAuto == b.CaretColorAuto && a.CaretColor.Equals(b.CaretColor) && a.ColorScheme == b.ColorScheme && a.DarkScheme == b.DarkScheme && a.HostScope == b.HostScope && a.HasHostScope == b.HasHostScope;

    internal static void InteractionHash(ref HashCode h, in ComputedValues v)
    {
        h.Add(v.PointerEvents);
        h.Add(v.Control);
        h.Add(v.ControlState);
        h.Add(v.ControlFraction);
        h.Add(v.ThumbFraction);
        h.Add(v.DisplayWasInlineLevel);
        h.Add(v.BaselineAtBorderEdge);
        h.Add(v.InnerBaseline);
        h.Add(v.ControlWidth);
        h.Add(v.WidthIsIntrinsic);
        h.Add(v.HeightIsIntrinsic);
        h.Add(v.ContentCenteredByPadding);
        h.Add(v.LegendGap);
        h.Add(v.ControlInnerPadding);
        h.Add(v.Appearance);
        h.Add(v.AccentColorAuto);
        h.Add(v.AccentColor);
        h.Add(v.CaretColorAuto);
        h.Add(v.CaretColor);
        h.Add(v.ColorScheme);
        h.Add(v.DarkScheme);
        h.Add(v.HostScope);
        h.Add(v.HasHostScope);
    }
}
