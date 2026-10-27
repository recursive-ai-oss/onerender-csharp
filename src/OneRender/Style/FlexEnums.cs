namespace OneRender.Style;

/// <summary>The <c>flex-direction</c> property (CSS Flexible Box 1 §5.1).</summary>
public enum FlexDirectionKind : byte
{
    /// <summary><c>row</c> (initial): the main axis is the inline axis.</summary>
    Row,

    /// <summary><c>row-reverse</c>.</summary>
    RowReverse,

    /// <summary><c>column</c>: the main axis is the block axis.</summary>
    Column,

    /// <summary><c>column-reverse</c>.</summary>
    ColumnReverse,
}

/// <summary>The <c>flex-wrap</c> property (CSS Flexible Box 1 §5.2).</summary>
public enum FlexWrapKind : byte
{
    /// <summary><c>nowrap</c> (initial): a single line.</summary>
    NoWrap,

    /// <summary><c>wrap</c>.</summary>
    Wrap,

    /// <summary><c>wrap-reverse</c>: lines stack from the cross end to the cross start.</summary>
    WrapReverse,
}

/// <summary>
/// The keyword of <c>justify-content</c>, <c>align-content</c>, <c>align-items</c> and <c>align-self</c> (CSS Box Alignment 3 §4 and §5).
/// Which keywords a property accepts is checked when it is parsed.
/// </summary>
public enum AlignKind : byte
{
    /// <summary><c>auto</c> (initial of <c>align-self</c>): use the container's <c>align-items</c>.</summary>
    Auto,

    /// <summary><c>normal</c> (initial of the other properties): behaves as <c>stretch</c> for items and <c>flex-start</c> for content in a flex container.</summary>
    Normal,

    /// <summary><c>stretch</c>.</summary>
    Stretch,

    /// <summary><c>flex-start</c>.</summary>
    FlexStart,

    /// <summary><c>flex-end</c>.</summary>
    FlexEnd,

    /// <summary><c>start</c>.</summary>
    Start,

    /// <summary><c>end</c>.</summary>
    End,

    /// <summary><c>self-start</c>.</summary>
    SelfStart,

    /// <summary><c>self-end</c>.</summary>
    SelfEnd,

    /// <summary><c>center</c>.</summary>
    Center,

    /// <summary><c>baseline</c> or <c>first baseline</c>.</summary>
    Baseline,

    /// <summary><c>last baseline</c>.</summary>
    LastBaseline,

    /// <summary><c>left</c>.</summary>
    Left,

    /// <summary><c>right</c>.</summary>
    Right,

    /// <summary><c>space-between</c>.</summary>
    SpaceBetween,

    /// <summary><c>space-around</c>.</summary>
    SpaceAround,

    /// <summary><c>space-evenly</c>.</summary>
    SpaceEvenly,
}

/// <summary>The overflow-position keyword in front of an alignment value (CSS Box Alignment 3 §4.4).</summary>
public enum AlignOverflowKind : byte
{
    /// <summary>No keyword: <c>unsafe</c> for content distribution, and the safe-ish default of the flex algorithm for items.</summary>
    Default,

    /// <summary><c>safe</c>.</summary>
    Safe,

    /// <summary><c>unsafe</c>.</summary>
    Unsafe,
}
