namespace OneRender.Style;

/// <summary>A value of <c>break-before</c> or <c>break-after</c> (CSS Fragmentation 3 §3.1). <c>always</c> is accepted as <see cref="Page"/>, the way the legacy <c>page-break-*</c> properties map it (§3.4).</summary>
public enum BreakKind : byte
{
    /// <summary><c>auto</c>: neither force nor forbid a break.</summary>
    Auto,

    /// <summary><c>avoid</c>: avoid a break of any type.</summary>
    Avoid,

    /// <summary><c>avoid-page</c>.</summary>
    AvoidPage,

    /// <summary><c>avoid-column</c>.</summary>
    AvoidColumn,

    /// <summary><c>avoid-region</c>; regions are not implemented, so it has no effect.</summary>
    AvoidRegion,

    /// <summary><c>page</c>: force a page break.</summary>
    Page,

    /// <summary><c>left</c>: force page breaks so the next page is a left page.</summary>
    Left,

    /// <summary><c>right</c>: force page breaks so the next page is a right page.</summary>
    Right,

    /// <summary><c>recto</c>.</summary>
    Recto,

    /// <summary><c>verso</c>.</summary>
    Verso,

    /// <summary><c>column</c>: force a column break.</summary>
    Column,

    /// <summary><c>region</c>; regions are not implemented, so it has no effect.</summary>
    Region,
}

/// <summary>A value of <c>break-inside</c> (CSS Fragmentation 3 §3.2).</summary>
public enum BreakInsideKind : byte
{
    /// <summary><c>auto</c>.</summary>
    Auto,

    /// <summary><c>avoid</c>.</summary>
    Avoid,

    /// <summary><c>avoid-page</c>.</summary>
    AvoidPage,

    /// <summary><c>avoid-column</c>.</summary>
    AvoidColumn,

    /// <summary><c>avoid-region</c>.</summary>
    AvoidRegion,
}

/// <summary><c>box-decoration-break</c> (CSS Fragmentation 3 §5.4).</summary>
public enum BoxDecorationBreakKind : byte
{
    /// <summary><c>slice</c>: the box is laid out unfragmented and cut; borders and padding appear only at the real start and end.</summary>
    Slice,

    /// <summary><c>clone</c>: every fragment gets its own border and padding at the break.</summary>
    Clone,
}

/// <summary><c>column-fill</c> (Multicol 1 §7.1).</summary>
public enum ColumnFillKind : byte
{
    /// <summary><c>balance</c>: columns get as equal a height as the content allows.</summary>
    Balance,

    /// <summary><c>auto</c>: columns fill one after the other up to the container's height.</summary>
    Auto,
}

/// <summary><c>column-span</c> (Multicol 1 §6.1).</summary>
public enum ColumnSpanKind : byte
{
    /// <summary><c>none</c>.</summary>
    None,

    /// <summary><c>all</c>: the box spans every column of its multicol container.</summary>
    All,
}

/// <summary>The parts of <c>contain</c> (CSS Containment 2 §3) the engine tracks.</summary>
[Flags]
public enum ContainParts : byte
{
    /// <summary><c>contain: none</c>.</summary>
    None = 0,

    /// <summary>Size containment (also from <c>strict</c>): the box is sized as if it had no content, and it does not fragment (CSS Break 4 §4.1 treats it as monolithic).</summary>
    Size = 1,

    /// <summary>Layout containment (also from <c>strict</c> and <c>content</c>).</summary>
    Layout = 2,

    /// <summary>Paint containment (also from <c>strict</c> and <c>content</c>).</summary>
    Paint = 4,

    /// <summary>Style containment (also from <c>content</c>).</summary>
    Style = 8,

    /// <summary>Inline-size containment.</summary>
    InlineSize = 16,
}
