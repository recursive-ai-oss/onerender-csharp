namespace OneRender.Style;

public partial struct ComputedValues
{
    private static void InitPaint(ref ComputedValues v)
    {
        v.Background = BackgroundSpec.Default;
        v.BorderImage = BorderImageSpec.Default;
        v.BoxShadow = ShadowList.None;
        v.TextShadow = ShadowList.None;
        v.OutlineWidth = 3;
        v.OutlineColor = ColorValue.CurrentColor;
        v.TextDecorationColor = ColorValue.CurrentColor;
        v.TextDecorationThickness = Length.Auto;
        v.TextUnderlineOffset = Length.Auto;
        v.ObjectPosition = PositionSpec.Center;
        v.ListStyleImage = ImageSpec.None;
    }

    internal static bool PaintEquals(in ComputedValues a, in ComputedValues b) =>
        a.Background.Equals(b.Background) &&
        a.BorderImage.Equals(b.BorderImage) &&
        a.BoxShadow.Equals(b.BoxShadow) &&
        a.TextShadow.Equals(b.TextShadow) &&
        a.OutlineWidth.Equals(b.OutlineWidth) &&
        a.OutlineStyle == b.OutlineStyle &&
        a.OutlineAuto == b.OutlineAuto &&
        a.OutlineColor.Equals(b.OutlineColor) &&
        a.OutlineOffset.Equals(b.OutlineOffset) &&
        a.TextDecorationLine == b.TextDecorationLine &&
        a.TextDecorationStyle == b.TextDecorationStyle &&
        a.TextDecorationColor.Equals(b.TextDecorationColor) &&
        a.TextDecorationThickness.Equals(b.TextDecorationThickness) &&
        a.TextUnderlineOffset.Equals(b.TextUnderlineOffset) &&
        a.TextUnderlinePosition == b.TextUnderlinePosition &&
        a.ObjectFit == b.ObjectFit &&
        a.ObjectPosition.Equals(b.ObjectPosition) &&
        a.ImageRendering == b.ImageRendering &&
        a.ListStyleImage.Equals(b.ListStyleImage);

    internal static void PaintHash(ref HashCode h, in ComputedValues v)
    {
        h.Add(v.Background);
        h.Add(v.BorderImage);
        h.Add(v.BoxShadow);
        h.Add(v.TextShadow);
        h.Add(v.OutlineWidth);
        h.Add(v.OutlineStyle);
        h.Add(v.OutlineColor);
        h.Add(v.TextDecorationLine);
        h.Add(v.TextDecorationStyle);
        h.Add(v.ObjectFit);
        h.Add(v.ImageRendering);
    }
}
