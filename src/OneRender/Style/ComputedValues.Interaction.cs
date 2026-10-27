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
        v.InnerBaseline = double.NaN;
        v.ControlWidth = double.NaN;
        v.WidthIsIntrinsic = false;
        v.LegendGap = 0;
        v.ControlInnerPadding = 0;
        v.Appearance = AppearanceKind.Auto;
        v.AccentColorAuto = true;
        v.AccentColor = ColorValue.Black;
    }

    internal static bool InteractionEquals(in ComputedValues a, in ComputedValues b) =>
        a.PointerEvents == b.PointerEvents && a.DisplayWasInlineLevel == b.DisplayWasInlineLevel
        && a.BaselineAtBorderEdge == b.BaselineAtBorderEdge && a.InnerBaseline.Equals(b.InnerBaseline) && a.ControlWidth.Equals(b.ControlWidth) && a.WidthIsIntrinsic == b.WidthIsIntrinsic && a.LegendGap.Equals(b.LegendGap) && a.ControlInnerPadding.Equals(b.ControlInnerPadding) && a.Appearance == b.Appearance && a.AccentColorAuto == b.AccentColorAuto && a.AccentColor.Equals(b.AccentColor);

    internal static void InteractionHash(ref HashCode h, in ComputedValues v)
    {
        h.Add(v.PointerEvents);
        h.Add(v.DisplayWasInlineLevel);
        h.Add(v.BaselineAtBorderEdge);
        h.Add(v.InnerBaseline);
        h.Add(v.ControlWidth);
        h.Add(v.WidthIsIntrinsic);
        h.Add(v.LegendGap);
        h.Add(v.ControlInnerPadding);
        h.Add(v.Appearance);
        h.Add(v.AccentColorAuto);
        h.Add(v.AccentColor);
    }
}
