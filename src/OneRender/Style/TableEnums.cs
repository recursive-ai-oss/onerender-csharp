namespace OneRender.Style;

/// <summary>The <c>border-collapse</c> property (CSS2 §17.6).</summary>
public enum BorderCollapseKind : byte
{
    /// <summary><c>separate</c>, the initial value: every cell has its own borders and the table has <c>border-spacing</c> between them.</summary>
    Separate,

    /// <summary><c>collapse</c>: adjacent borders merge into one by the conflict resolution of CSS2 §17.6.2.1.</summary>
    Collapse,
}

/// <summary>The <c>empty-cells</c> property (CSS2 §17.6.1.1).</summary>
public enum EmptyCellsKind : byte
{
    /// <summary><c>show</c>, the initial value.</summary>
    Show,

    /// <summary><c>hide</c>: a cell with no visible content paints no background or border (separate borders only).</summary>
    Hide,
}

/// <summary>The <c>caption-side</c> property (CSS2 §17.4.1).</summary>
public enum CaptionSideKind : byte
{
    /// <summary><c>top</c>, the initial value.</summary>
    Top,

    /// <summary><c>bottom</c>.</summary>
    Bottom,
}

/// <summary>The <c>table-layout</c> property (CSS2 §17.5.2).</summary>
public enum TableLayoutKind : byte
{
    /// <summary><c>auto</c>, the initial value: columns are sized from the content of every cell.</summary>
    Auto,

    /// <summary><c>fixed</c>: columns are sized from the first row and the column elements; the table's width must not be <c>auto</c>.</summary>
    Fixed,
}
