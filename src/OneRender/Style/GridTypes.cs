namespace OneRender.Style;

/// <summary>What a <see cref="GridBreadth"/> is (CSS Grid 2 §7.2.1).</summary>
public enum GridBreadthKind : byte
{
    /// <summary>A <c>&lt;length-percentage&gt;</c> held in <see cref="GridBreadth.Length"/>.</summary>
    Length,

    /// <summary><c>auto</c>.</summary>
    Auto,

    /// <summary><c>min-content</c>.</summary>
    MinContent,

    /// <summary><c>max-content</c>.</summary>
    MaxContent,

    /// <summary>A flexible length; the factor is <see cref="GridBreadth.Flex"/>.</summary>
    Flex,

    /// <summary><c>fit-content(&lt;length-percentage&gt;)</c>; the limit is <see cref="GridBreadth.Length"/>. Only valid as a maximum.</summary>
    FitContent,
}

/// <summary>One side of a track sizing function: a length, a keyword, a flex factor or a fit-content limit.</summary>
public readonly struct GridBreadth : IEquatable<GridBreadth>
{
    internal GridBreadth(GridBreadthKind kind, Length length, double flex)
    {
        Kind = kind;
        Length = length;
        Flex = flex;
    }

    /// <summary>What it is.</summary>
    public GridBreadthKind Kind { get; }

    /// <summary>The length or percentage for <see cref="GridBreadthKind.Length"/> and the limit of <see cref="GridBreadthKind.FitContent"/>.</summary>
    public Length Length { get; }

    /// <summary>The factor of a <see cref="GridBreadthKind.Flex"/> breadth (the number before <c>fr</c>).</summary>
    public double Flex { get; }

    /// <summary><c>auto</c>.</summary>
    public static GridBreadth Auto => new(GridBreadthKind.Auto, default, 0);

    /// <summary>Whether the breadth is a length that does not depend on a percentage basis the grid lacks, or a keyword that never needs one.</summary>
    internal bool IsFixedKind => Kind == GridBreadthKind.Length;

    /// <inheritdoc />
    public bool Equals(GridBreadth other) => Kind == other.Kind && Length.Equals(other.Length) && Flex.Equals(other.Flex);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GridBreadth b && Equals(b);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Length, Flex);

    /// <summary>Equality.</summary>
    public static bool operator ==(GridBreadth left, GridBreadth right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(GridBreadth left, GridBreadth right) => !left.Equals(right);

    /// <summary>The value as CSS text.</summary>
    public string ToCss() => Kind switch
    {
        GridBreadthKind.Length => Length.ToCss(),
        GridBreadthKind.Auto => "auto",
        GridBreadthKind.MinContent => "min-content",
        GridBreadthKind.MaxContent => "max-content",
        GridBreadthKind.Flex => CssText.Number(Flex) + "fr",
        _ => "fit-content(" + Length.ToCss() + ")",
    };
}

/// <summary>A track sizing function: its min and max track sizing functions (CSS Grid 2 §12.2).</summary>
public readonly struct GridTrackSize : IEquatable<GridTrackSize>
{
    internal GridTrackSize(GridBreadth min, GridBreadth max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>The min track sizing function. <c>auto</c> for a bare flexible length or <c>fit-content()</c>.</summary>
    public GridBreadth Min { get; }

    /// <summary>The max track sizing function.</summary>
    public GridBreadth Max { get; }

    /// <summary><c>auto</c>, the initial value of <c>grid-auto-rows</c> and <c>grid-auto-columns</c>.</summary>
    public static GridTrackSize Auto => new(GridBreadth.Auto, GridBreadth.Auto);

    /// <inheritdoc />
    public bool Equals(GridTrackSize other) => Min.Equals(other.Min) && Max.Equals(other.Max);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GridTrackSize t && Equals(t);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Min, Max);

    /// <summary>Equality.</summary>
    public static bool operator ==(GridTrackSize left, GridTrackSize right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(GridTrackSize left, GridTrackSize right) => !left.Equals(right);

    /// <summary>The value as CSS text.</summary>
    public string ToCss()
    {
        if (Max.Kind == GridBreadthKind.FitContent)
        {
            return Max.ToCss();
        }

        if (Min.Kind == GridBreadthKind.Auto && Max.Kind == GridBreadthKind.Flex)
        {
            return Max.ToCss();
        }

        if (Min.Equals(Max))
        {
            return Min.ToCss();
        }

        return "minmax(" + Min.ToCss() + ", " + Max.ToCss() + ")";
    }
}

/// <summary>
/// A computed <c>grid-template-columns</c> or <c>grid-template-rows</c> value (CSS Grid 2 §7.2.5): <c>none</c>, a track list or
/// <c>subgrid</c> with line names. Integer <c>repeat()</c>s are expanded when the value is parsed (clamped to <see cref="MaxTracks"/>
/// tracks in all); an <c>auto-fill</c> or <c>auto-fit</c> repeat is kept as its tracks and expanded when layout knows the room.
/// </summary>
public sealed class GridTrackList : IEquatable<GridTrackList>
{
    /// <summary>The most tracks one axis of one grid may have (CSS Grid 2 §5.4: a UA-defined limit that accommodates lines in [-10000, 10000]). Tracks and repeats past it are dropped.</summary>
    public const int MaxTracks = 10000;

    internal static readonly string[]?[] NoNames = [null];

    internal GridTrackList(bool isSubgrid, GridTrackSize[] head, string[]?[] headNames, GridTrackSize[] auto, string[]?[] autoNames, bool autoFit, GridTrackSize[] tail, string[]?[] tailNames, bool hasAuto = false)
    {
        HasAutoRepeat = hasAuto;
        IsSubgrid = isSubgrid;
        Head = head;
        HeadNames = headNames;
        Auto = auto;
        AutoNames = autoNames;
        AutoFit = autoFit;
        Tail = tail;
        TailNames = tailNames;
    }

    /// <summary>The value <c>none</c>.</summary>
    public static GridTrackList None { get; } = new(false, [], NoNames, [], NoNames, false, [], NoNames);

    /// <summary>True for <c>subgrid</c>; the line-name lists then hold the names of the subgrid's lines and the size arrays are empty.</summary>
    public bool IsSubgrid { get; }

    /// <summary>True when there are no tracks and no line names (<c>none</c>, or a subgrid with no names).</summary>
    public bool IsEmpty => Head.Length == 0 && Auto.Length == 0 && Tail.Length == 0 && !IsSubgrid;

    /// <summary>The tracks before an auto-repeat (or all tracks when there is none).</summary>
    internal GridTrackSize[] Head { get; }

    /// <summary>Line names around the head tracks: <c>Head.Length + 1</c> sets (for a subgrid, one set per line).</summary>
    internal string[]?[] HeadNames { get; }

    /// <summary>The tracks of the <c>auto-fill</c> / <c>auto-fit</c> repeat; empty when there is none.</summary>
    internal GridTrackSize[] Auto { get; }

    /// <summary>Line names around the repeated tracks: <c>Auto.Length + 1</c> sets.</summary>
    internal string[]?[] AutoNames { get; }

    /// <summary>True for <c>auto-fit</c>.</summary>
    internal bool AutoFit { get; }

    /// <summary>The tracks after the auto-repeat.</summary>
    internal GridTrackSize[] Tail { get; }

    /// <summary>Line names around the tail tracks.</summary>
    internal string[]?[] TailNames { get; }

    /// <summary>True when the list has an <c>auto-fill</c> / <c>auto-fit</c> repeat (for a subgrid: a <c>repeat(auto-fill, ...)</c> of names).</summary>
    public bool HasAutoRepeat { get; }

    /// <summary>The number of tracks written, with an auto-repeat counted once.</summary>
    public int WrittenTrackCount => Head.Length + Auto.Length + Tail.Length;

    /// <inheritdoc />
    public bool Equals(GridTrackList? other) =>
        other is not null && IsSubgrid == other.IsSubgrid && AutoFit == other.AutoFit
        && Same(Head, other.Head) && Same(Auto, other.Auto) && Same(Tail, other.Tail)
        && Same(HeadNames, other.HeadNames) && Same(AutoNames, other.AutoNames) && Same(TailNames, other.TailNames);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as GridTrackList);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(IsSubgrid, Head.Length, Auto.Length, Tail.Length, HeadNames.Length);

    /// <summary>The value as CSS text.</summary>
    public override string ToString()
    {
        if (IsEmpty)
        {
            return "none";
        }

        var sb = new System.Text.StringBuilder();
        if (IsSubgrid)
        {
            sb.Append("subgrid");
            Names(sb, HeadNames, 0, HeadNames.Length);
            if (HasAutoRepeat)
            {
                sb.Append(" repeat(auto-fill,");
                Names(sb, AutoNames, 0, AutoNames.Length - 1);
                sb.Append(')');
            }

            Names(sb, TailNames, 0, TailNames.Length);
            return sb.ToString();
        }

        Section(sb, Head, HeadNames);
        if (Auto.Length > 0)
        {
            sb.Append(sb.Length > 0 ? " " : string.Empty).Append("repeat(").Append(AutoFit ? "auto-fit, " : "auto-fill, ");
            var inner = new System.Text.StringBuilder();
            Section(inner, Auto, AutoNames);
            sb.Append(inner).Append(')');
        }

        Section(sb, Tail, TailNames);
        return sb.ToString();
    }

    private static void Section(System.Text.StringBuilder sb, GridTrackSize[] sizes, string[]?[] names)
    {
        for (int i = 0; i <= sizes.Length; i++)
        {
            Names(sb, names, i, i + 1);
            if (i < sizes.Length)
            {
                sb.Append(sb.Length > 0 ? " " : string.Empty).Append(sizes[i].ToCss());
            }
        }
    }

    private static void Names(System.Text.StringBuilder sb, string[]?[] names, int from, int to)
    {
        for (int i = from; i < to && i < names.Length; i++)
        {
            string[]? n = names[i];
            if (n is null || n.Length == 0)
            {
                if (to - from > 1 || sb.Length > 0 && sb[^1] == 'd')
                {
                    sb.Append(" []");
                }

                continue;
            }

            sb.Append(sb.Length > 0 ? " [" : "[").Append(string.Join(' ', n)).Append(']');
        }
    }

    private static bool Same(GridTrackSize[] a, GridTrackSize[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Same(string[]?[] a, string[]?[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            string[]? x = a[i], y = b[i];
            int nx = x?.Length ?? 0, ny = y?.Length ?? 0;
            if (nx != ny)
            {
                return false;
            }

            for (int j = 0; j < nx; j++)
            {
                if (!string.Equals(x![j], y![j], StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>What a <see cref="GridLine"/> contributes to a placement (CSS Grid 2 §8.3).</summary>
public enum GridLineKind : byte
{
    /// <summary><c>auto</c>: nothing.</summary>
    Auto,

    /// <summary>A line: an index (positive or negative), a name, or both. An index of 0 means "the name only".</summary>
    Line,

    /// <summary><c>span</c> with a count and an optional name.</summary>
    Span,
}

/// <summary>A computed <c>grid-row-start</c>, <c>grid-row-end</c>, <c>grid-column-start</c> or <c>grid-column-end</c> value.</summary>
public readonly struct GridLine : IEquatable<GridLine>
{
    internal GridLine(GridLineKind kind, int number, string? name)
    {
        Kind = kind;
        Number = number;
        Name = name;
    }

    /// <summary>What it is.</summary>
    public GridLineKind Kind { get; }

    /// <summary>The line index (never 0 unless only a name was written) or the span count (at least 1).</summary>
    public int Number { get; }

    /// <summary>The custom identifier, or null.</summary>
    public string? Name { get; }

    /// <summary><c>auto</c>.</summary>
    public static GridLine Auto => default;

    /// <inheritdoc />
    public bool Equals(GridLine other) => Kind == other.Kind && Number == other.Number && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GridLine l && Equals(l);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Number);

    /// <summary>Equality.</summary>
    public static bool operator ==(GridLine left, GridLine right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(GridLine left, GridLine right) => !left.Equals(right);

    /// <summary>The value as CSS text.</summary>
    public string ToCss() => Kind switch
    {
        GridLineKind.Auto => "auto",
        GridLineKind.Span => "span" + (Name is null || Number != 1 ? " " + Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty) + (Name is null ? string.Empty : " " + Name),
        _ => Number == 0 ? Name ?? "auto" : Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + (Name is null ? string.Empty : " " + Name),
    };
}

/// <summary>The named areas of <c>grid-template-areas</c> (CSS Grid 2 §7.3): a rectangle of cells per name.</summary>
public sealed class GridAreas : IEquatable<GridAreas>
{
    internal GridAreas(int rows, int columns, string[] names, int[] rowStart, int[] rowEnd, int[] columnStart, int[] columnEnd, string text)
    {
        Rows = rows;
        Columns = columns;
        Names = names;
        RowStart = rowStart;
        RowEnd = rowEnd;
        ColumnStart = columnStart;
        ColumnEnd = columnEnd;
        Text = text;
    }

    /// <summary>No areas.</summary>
    public static GridAreas None { get; } = new(0, 0, [], [], [], [], [], "none");

    /// <summary>The number of rows the template defines.</summary>
    public int Rows { get; }

    /// <summary>The number of columns the template defines.</summary>
    public int Columns { get; }

    /// <summary>Distinct area names in order of first appearance.</summary>
    internal string[] Names { get; }

    internal int[] RowStart { get; }

    internal int[] RowEnd { get; }

    internal int[] ColumnStart { get; }

    internal int[] ColumnEnd { get; }

    /// <summary>The serialized value (cells joined by one space, rows by quoted strings).</summary>
    public string Text { get; }

    /// <inheritdoc />
    public bool Equals(GridAreas? other) => other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as GridAreas);

    /// <inheritdoc />
    public override int GetHashCode() => Text.Length;

    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>The <c>grid-auto-flow</c> keyword for the main direction.</summary>
public enum GridAutoFlowKind : byte
{
    /// <summary><c>row</c>.</summary>
    Row,

    /// <summary><c>column</c>.</summary>
    Column,
}
