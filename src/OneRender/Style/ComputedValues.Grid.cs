namespace OneRender.Style;

public partial struct ComputedValues
{
    private static readonly GridTrackSize[] AutoTrackPattern = [GridTrackSize.Auto];

    private static void InitGrid(ref ComputedValues v)
    {
        v.GridTemplateColumns = GridTrackList.None;
        v.GridTemplateRows = GridTrackList.None;
        v.GridTemplateAreas = GridAreas.None;
        v.GridAutoColumns = AutoTrackPattern;
        v.GridAutoRows = AutoTrackPattern;
        v.JustifyItems = AlignKind.Normal;
        v.JustifySelf = AlignKind.Auto;
    }

    internal static bool GridEquals(in ComputedValues a, in ComputedValues b)
    {
        if (a.GridAutoFlow != b.GridAutoFlow || a.GridAutoFlowDense != b.GridAutoFlowDense
            || !a.GridRowStart.Equals(b.GridRowStart) || !a.GridRowEnd.Equals(b.GridRowEnd)
            || !a.GridColumnStart.Equals(b.GridColumnStart) || !a.GridColumnEnd.Equals(b.GridColumnEnd)
            || a.JustifyItems != b.JustifyItems || a.JustifyItemsOverflow != b.JustifyItemsOverflow
            || a.JustifySelf != b.JustifySelf || a.JustifySelfOverflow != b.JustifySelfOverflow
            || !a.GridTemplateColumns.Equals(b.GridTemplateColumns) || !a.GridTemplateRows.Equals(b.GridTemplateRows)
            || !a.GridTemplateAreas.Equals(b.GridTemplateAreas) || a.GridAutoColumns.Length != b.GridAutoColumns.Length || a.GridAutoRows.Length != b.GridAutoRows.Length)
        {
            return false;
        }

        for (int i = 0; i < a.GridAutoColumns.Length; i++)
        {
            if (!a.GridAutoColumns[i].Equals(b.GridAutoColumns[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < a.GridAutoRows.Length; i++)
        {
            if (!a.GridAutoRows[i].Equals(b.GridAutoRows[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal static void GridHash(ref HashCode h, in ComputedValues v)
    {
        h.Add(v.GridAutoFlow);
        h.Add(v.GridAutoFlowDense);
        h.Add(v.GridRowStart);
        h.Add(v.GridRowEnd);
        h.Add(v.GridColumnStart);
        h.Add(v.GridColumnEnd);
        h.Add(v.JustifyItems);
        h.Add(v.JustifySelf);
        h.Add(v.GridTemplateColumns);
        h.Add(v.GridTemplateRows);
        h.Add(v.GridTemplateAreas);
    }
}
