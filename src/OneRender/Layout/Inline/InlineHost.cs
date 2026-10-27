using OneRender.Tree;

namespace OneRender.Layout;

/// <summary>
/// What an inline layout needs from the block layout that runs it: lay out an atomic inline-level box (inline-block, replaced
/// element, inline-flex and the like) as a unit, and report its intrinsic contribution to the line. The block layout implements
/// this and hands itself to an <see cref="IAtomicInlineHostAware"/> inline layout.
/// </summary>
public interface IAtomicInlineHost
{
    /// <summary>
    /// Lays out an atomic inline-level box as the root of its own formatting context with shrink-to-fit width (CSS2 §10.3.9 and
    /// §10.3.5) and adds its fragment under <paramref name="parent"/>.
    /// </summary>
    /// <param name="box">The atomic inline-level box.</param>
    /// <param name="parent">The fragment to add to; the caller moves it later with <see cref="FragmentTree.Reparent"/>.</param>
    /// <param name="containingWidth">The width of the line's containing block, which percentages resolve against.</param>
    /// <param name="margins">Receives the used margins (top, right, bottom, left) in px.</param>
    /// <returns>The box's fragment (border box at the margin offset inside the parent), or the null fragment when nothing could be laid out.</returns>
    Fragment LayoutAtomic(Box box, Fragment parent, double containingWidth, out Insets margins);

    /// <summary>The min-content and max-content width of an atomic inline-level box including its margins, border and padding.</summary>
    /// <param name="box">The atomic inline-level box.</param>
    /// <param name="minContent">Receives the min-content contribution in px.</param>
    /// <param name="maxContent">Receives the max-content contribution in px.</param>
    void GetAtomicContribution(Box box, out double minContent, out double maxContent);

    /// <summary>
    /// Queues an absolutely or fixed positioned box found in inline content, with the place the box would have had in the flow
    /// (its static position). The box is laid out once the containing blocks are final.
    /// </summary>
    /// <param name="box">The positioned box.</param>
    /// <param name="containingFragment">The fragment of the nearest positioned inline box that contains it (CSS2 §10.1 item 4), or -1 to use the one of the block container.</param>
    /// <param name="staticStart">The hypothetical box's start margin edge, in the container fragment's border-box coordinates.</param>
    /// <param name="staticEnd">The end margin edge it would stretch to in the same coordinates.</param>
    /// <param name="staticTop">The top margin edge, in the same coordinates.</param>
    void QueueAbsolute(Box box, int containingFragment, double staticStart, double staticEnd, double staticTop);
}

/// <summary>An <see cref="IInlineLayout"/> that wants the block layout's atomic-inline services.</summary>
public interface IAtomicInlineHostAware
{
    /// <summary>Gives the inline layout the host it should lay out and measure atomic inlines with. Called when the block layout is created.</summary>
    /// <param name="host">The block layout.</param>
    void SetHost(IAtomicInlineHost host);
}
