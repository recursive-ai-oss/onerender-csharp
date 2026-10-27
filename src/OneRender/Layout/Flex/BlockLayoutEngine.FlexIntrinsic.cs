using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

public sealed partial class BlockLayoutEngine
{
    private static bool IsFlexItemBox(Box c) =>
        c.Kind is BoxKind.Container or BoxKind.Replaced
        && (c.Flags & BoxTraits.OutsideMarker) == 0
        && c.Values.Position is not (PositionKind.Absolute or PositionKind.Fixed);

    /// <summary>0 for a block container; 1 for a single-line row, 2 for a wrapping row and 3 for a column flex container.</summary>
    private static byte FlexIntrinsicKind(Box b)
    {
        if (b.Kind != BoxKind.Container || b.Context != FormattingContext.Flex)
        {
            return 0;
        }

        ref readonly ComputedValues v = ref b.Values;
        if (v.FlexDirection is FlexDirectionKind.Column or FlexDirectionKind.ColumnReverse)
        {
            return 3;
        }

        return v.FlexWrap == FlexWrapKind.NoWrap ? (byte)1 : (byte)2;
    }

    // Percentage gaps count as zero in intrinsic sizes (Box Alignment 3 §8.3).
    private static double FlexIntrinsicGap(Box b)
    {
        if (b.Kind != BoxKind.Container || b.Context != FormattingContext.Flex)
        {
            return 0;
        }

        ref readonly ComputedValues v = ref b.Values;
        bool row = v.FlexDirection is FlexDirectionKind.Row or FlexDirectionKind.RowReverse;
        Length g = row ? v.ColumnGap : v.RowGap;
        return g.Kind == LengthKind.Px ? Math.Max(0, g.Value) : 0;
    }

    /// <summary>
    /// Flexbox §9.9: an item's contribution is its outer size, held to its flex base size from above when it cannot grow and from below
    /// when it cannot shrink; a single-line row adds the contributions, a wrapping row breaks everywhere for min-content and never for
    /// max-content, and a column takes the widest item.
    /// </summary>
    private static void MergeFlex(ref IntrinsicFrame parent, Box child, double minC, double maxC)
    {
        ref readonly ComputedValues v = ref child.Values;
        double baseOuter = maxC;
        if (parent.Flex != 3 && !v.FlexBasisContent && v.FlexBasis.IsPx)
        {
            double ph = FixedPadding(in v);
            double edges = ph + (v.MarginLeft.IsPx ? v.MarginLeft.Value : 0) + (v.MarginRight.IsPx ? v.MarginRight.Value : 0);
            double inner = v.BoxSizing == BoxSizingKind.BorderBox ? v.FlexBasis.Value - ph : v.FlexBasis.Value;
            baseOuter = Math.Max(0, inner) + edges;
        }

        if (v.FlexShrink == 0)
        {
            minC = Math.Max(minC, baseOuter);
        }

        if (v.FlexGrow == 0)
        {
            maxC = Math.Min(maxC, baseOuter);
        }

        minC = Math.Min(minC, maxC);
        parent.Count++;
        switch (parent.Flex)
        {
            case 1:
                parent.Min += minC;
                parent.Max += maxC;
                break;
            case 2:
                parent.Min = Math.Max(parent.Min, minC);
                parent.Max += maxC;
                break;
            default:
                parent.Min = Math.Max(parent.Min, minC);
                parent.Max = Math.Max(parent.Max, maxC);
                break;
        }
    }

    private static void FinishFlexIntrinsic(ref IntrinsicFrame f)
    {
        if (f.Count > 1 && f.Flex != 3)
        {
            double gaps = f.Gap * (f.Count - 1);
            f.Max += gaps;
            if (f.Flex == 1)
            {
                f.Min += gaps;
            }
        }
    }
}
