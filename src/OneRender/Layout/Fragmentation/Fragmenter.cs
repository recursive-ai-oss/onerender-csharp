using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

/// <summary>The kind of fragmentainer a slicer fills; it decides which break values force or discourage a break (CSS Fragmentation 3 §3, §4.3).</summary>
internal enum FragmentationKind : byte
{
    /// <summary>Columns of a multi-column container: <c>column</c> forces a break, <c>avoid</c> and <c>avoid-column</c> discourage one.</summary>
    Column,

    /// <summary>Pages: <c>page</c>, <c>left</c>, <c>right</c>, <c>recto</c> and <c>verso</c> force a break, <c>avoid</c> and <c>avoid-page</c> discourage one.</summary>
    Page,
}

/// <summary>The break values that apply at one break point, combined (§4.3: all forced types are honoured).</summary>
[Flags]
internal enum BreakBits : ushort
{
    None = 0,
    ForceColumn = 1,
    ForcePage = 2,
    Avoid = 4,
    AvoidColumn = 8,
    AvoidPage = 16,
    SideLeft = 32,
    SideRight = 64,
    SideRecto = 128,
    SideVerso = 256,
}

/// <summary>A place where the flow may be cut, in strip coordinates (block offsets from the top of the unfragmented flow).</summary>
internal struct BreakCandidate
{
    /// <summary>Where the content above the break ends (bottom of the earlier box or line).</summary>
    public double Ye;

    /// <summary>Where the content below the break resumes in the next fragmentainer (top of the later box or line); the gap between <see cref="Ye"/> and this is the truncated margin.</summary>
    public double Ys;

    /// <summary>Top border and padding that boxes with <c>box-decoration-break: clone</c> repeat at the start of the next fragmentainer.</summary>
    public double CloneTop;

    /// <summary>Bottom border and padding that cloned boxes add at the end of the fragmentainer before the break.</summary>
    public double CloneBottom;

    public int Seq;

    /// <summary>0: allowed by every rule; 1: breaks the orphans/widows rule only (§4.4 rule 3); 2: breaks an avoid rule (rules 1, 2 or 4).</summary>
    public byte Level;

    public bool Forced;
    public bool IsLine;
    public BreakBits Bits;
}

/// <summary>What one pass of slicing a row of the flow into fragmentainers produced.</summary>
internal struct SliceResult
{
    public int Count;
    public int MaxLevel;
    public bool Overflow;
    public bool Truncated;
    public double MaxExtent;
}

/// <summary>A flow cut into fragmentainers: where each one starts and ends in strip coordinates.</summary>
internal struct RowRecord
{
    public int FkStart;
    public int FkCount;
    public double H;
    public double OutY;
    public int ColBase;
}

internal struct SpannerRecord
{
    public int Frag;
    public double Top;
    public double Bottom;
    public double OutY;
}

internal struct PlanRecord
{
    public int Frag;
    public double ContentLeft;
    public double ContentTop;
    public double ColW;
    public double Stride;
    public double ContentW;
    public bool Rtl;
    public int RowStart;
    public int RowCount;
    public int SpanStart;
    public int SpanCount;
    public double OutH;
    public int Columns;
}

/// <summary>
/// Block fragmentation (CSS Fragmentation 3) over a laid-out flow. The flow is first laid out as one tall strip at the
/// fragmentainers' inline size; this class finds the break points in it (§4.1), chooses breaks by the rules of §4.3 to §4.5,
/// and then rewrites the fragment tree into one fragment per fragmentainer, splitting the boxes that cross a break. Multi-column
/// layout uses it with column fragmentainers; paged media can use it with page fragmentainers of any sizes.
/// </summary>
internal sealed partial class Fragmenter
{
    internal const double Eps = 0.001;
    internal const int MaxFragmentainers = 100_000;
    internal const int MaxPieces = 4_000_000;

    private FragmentTree _t = null!;
    private BoxTree _boxes = null!;
    private FragmentationKind _kind;

    internal static bool Monolithic(Box b)
    {
        // §4.1: replaced content and scroll containers have no break points inside; flex, grid and table containers and nested
        // multi-column containers are treated the same until they learn to fragment.
        if (b.Kind != BoxKind.Container)
        {
            return true;
        }

        // §4.1: rows of a table row group are block-level siblings for breaking, so a table and its row groups fragment between
        // rows. A row stays whole (its cells are not split), so a row taller than a fragmentainer overflows it.
        // A header or footer group stays whole: a break inside it would leave a table page with half a header, and the group is
        // the thing a break repeats (CSS 2 §17.5.2.2).
        if (b.Context is FormattingContext.TableHeaderGroup or FormattingContext.TableFooterGroup)
        {
            return true;
        }

        if (b.Context is FormattingContext.Table or FormattingContext.TableRowGroup)
        {
            return b.Values.IsScrollContainer || b.Values.IsMulticol;
        }

        if (b.Context is not (FormattingContext.Block or FormattingContext.FlowRoot))
        {
            return true;
        }

        ref readonly ComputedValues v = ref b.Values;
        return v.IsScrollContainer || v.IsMulticol;
    }

    private static BreakBits Map(BreakKind k) => k switch
    {
        BreakKind.Avoid => BreakBits.Avoid,
        BreakKind.AvoidPage => BreakBits.AvoidPage,
        BreakKind.AvoidColumn => BreakBits.AvoidColumn,
        BreakKind.Page => BreakBits.ForcePage,
        BreakKind.Left => BreakBits.ForcePage | BreakBits.SideLeft,
        BreakKind.Right => BreakBits.ForcePage | BreakBits.SideRight,
        BreakKind.Recto => BreakBits.ForcePage | BreakBits.SideRecto,
        BreakKind.Verso => BreakBits.ForcePage | BreakBits.SideVerso,
        BreakKind.Column => BreakBits.ForceColumn,
        _ => BreakBits.None,
    };

    private BreakBits ForcedMask => _kind == FragmentationKind.Column ? BreakBits.ForceColumn : BreakBits.ForcePage;

    private BreakBits AvoidMask => BreakBits.Avoid | (_kind == FragmentationKind.Column ? BreakBits.AvoidColumn : BreakBits.AvoidPage);

    private bool AvoidInside(BreakInsideKind k) =>
        k == BreakInsideKind.Avoid || (_kind == FragmentationKind.Column ? k == BreakInsideKind.AvoidColumn : k == BreakInsideKind.AvoidPage);

    private static T[] Grown<T>(T[] a, int needed)
    {
        if (a.Length >= needed)
        {
            return a;
        }

        Array.Resize(ref a, Math.Max(needed, Math.Max(16, a.Length * 2)));
        return a;
    }
}
