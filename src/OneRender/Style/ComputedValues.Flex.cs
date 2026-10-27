namespace OneRender.Style;

public partial struct ComputedValues
{
    private static void InitFlex(ref ComputedValues v)
    {
        v.FlexShrink = 1;
        v.FlexBasis = Length.Auto;
        v.JustifyContent = AlignKind.Normal;
        v.AlignContent = AlignKind.Normal;
        v.AlignItems = AlignKind.Normal;
        v.AlignSelf = AlignKind.Auto;
        v.RowGap = Length.Normal;
        v.ColumnGap = Length.Normal;
    }

    internal static bool FlexEquals(in ComputedValues a, in ComputedValues b) =>
        a.FlexDirection == b.FlexDirection &&
        a.FlexWrap == b.FlexWrap &&
        a.FlexGrow.Equals(b.FlexGrow) &&
        a.FlexShrink.Equals(b.FlexShrink) &&
        a.FlexBasis.Equals(b.FlexBasis) &&
        a.FlexBasisContent == b.FlexBasisContent &&
        a.Order == b.Order &&
        a.JustifyContent == b.JustifyContent &&
        a.JustifyContentOverflow == b.JustifyContentOverflow &&
        a.AlignContent == b.AlignContent &&
        a.AlignContentOverflow == b.AlignContentOverflow &&
        a.AlignItems == b.AlignItems &&
        a.AlignItemsOverflow == b.AlignItemsOverflow &&
        a.AlignSelf == b.AlignSelf &&
        a.AlignSelfOverflow == b.AlignSelfOverflow &&
        a.RowGap.Equals(b.RowGap) &&
        a.ColumnGap.Equals(b.ColumnGap);

    internal static void FlexHash(ref HashCode h, in ComputedValues v)
    {
        h.Add(v.FlexDirection);
        h.Add(v.FlexWrap);
        h.Add(v.FlexGrow);
        h.Add(v.FlexShrink);
        h.Add(v.FlexBasis);
        h.Add(v.FlexBasisContent);
        h.Add(v.Order);
        h.Add(v.JustifyContent);
        h.Add(v.JustifyContentOverflow);
        h.Add(v.AlignContent);
        h.Add(v.AlignContentOverflow);
        h.Add(v.AlignItems);
        h.Add(v.AlignItemsOverflow);
        h.Add(v.AlignSelf);
        h.Add(v.AlignSelfOverflow);
        h.Add(v.RowGap);
        h.Add(v.ColumnGap);
    }
}
