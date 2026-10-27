namespace OneRender.Style;

public partial struct ComputedValues
{
    /// <summary>True when the box is a multi-column container: <c>column-count</c> or <c>column-width</c> is not <c>auto</c> (Multicol 1 §2).</summary>
    public readonly bool IsMulticol => ColumnCount > 0 || ColumnWidth.Kind != LengthKind.Auto;

    private static void InitFragmentation(ref ComputedValues v)
    {
        v.Orphans = 2;
        v.Widows = 2;
        v.ColumnWidth = Length.Auto;
        v.ColumnRuleWidth = 3;
        v.ColumnRuleColor = ColorValue.CurrentColor;
    }

    internal static bool FragmentationEquals(in ComputedValues a, in ComputedValues b) =>
        a.BreakBefore == b.BreakBefore &&
        a.BreakAfter == b.BreakAfter &&
        a.BreakInside == b.BreakInside &&
        a.Orphans == b.Orphans &&
        a.Widows == b.Widows &&
        string.Equals(a.PageName, b.PageName, StringComparison.Ordinal) &&
        a.BoxDecorationBreak == b.BoxDecorationBreak &&
        a.ColumnCount == b.ColumnCount &&
        a.ColumnWidth.Equals(b.ColumnWidth) &&
        a.ColumnFill == b.ColumnFill &&
        a.ColumnSpan == b.ColumnSpan &&
        a.ColumnRuleWidth.Equals(b.ColumnRuleWidth) &&
        a.ColumnRuleStyle == b.ColumnRuleStyle &&
        a.ColumnRuleColor.Equals(b.ColumnRuleColor);

    internal static void FragmentationHash(ref HashCode h, in ComputedValues v)
    {
        h.Add(v.BreakBefore);
        h.Add(v.BreakAfter);
        h.Add(v.BreakInside);
        h.Add(v.Orphans);
        h.Add(v.Widows);
        h.Add(v.PageName, StringComparer.Ordinal);
        h.Add(v.BoxDecorationBreak);
        h.Add(v.ColumnCount);
        h.Add(v.ColumnWidth);
        h.Add(v.ColumnFill);
        h.Add(v.ColumnSpan);
        h.Add(v.ColumnRuleWidth);
        h.Add(v.ColumnRuleStyle);
        h.Add(v.ColumnRuleColor);
    }
}
