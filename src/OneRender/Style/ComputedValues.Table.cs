namespace OneRender.Style;

public partial struct ComputedValues
{
    internal static bool TableEquals(in ComputedValues a, in ComputedValues b) =>
        a.BorderCollapse == b.BorderCollapse &&
        a.BorderSpacingH.Equals(b.BorderSpacingH) &&
        a.BorderSpacingV.Equals(b.BorderSpacingV) &&
        a.EmptyCells == b.EmptyCells &&
        a.CaptionSide == b.CaptionSide &&
        a.TableLayout == b.TableLayout;

    internal static void TableHash(ref HashCode h, in ComputedValues v)
    {
        h.Add(v.BorderCollapse);
        h.Add(v.BorderSpacingH);
        h.Add(v.BorderSpacingV);
        h.Add(v.EmptyCells);
        h.Add(v.CaptionSide);
        h.Add(v.TableLayout);
    }
}
