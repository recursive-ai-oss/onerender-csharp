using OneRender.Tree;

namespace OneRender.Layout;

/// <summary>
/// The floats of the block formatting context an inline layout is running in (CSS2 §9.5), as lines see them. Coordinates are
/// in the content box of the block formatting context root: x grows right, y grows down, and
/// <see cref="InlineLayoutRequest.BfcLeft"/> / <see cref="InlineLayoutRequest.BfcTop"/> give the container's own origin there.
/// </summary>
/// <remarks>
/// An inline layout asks for the room beside floats with <see cref="Query"/> for each line it tries, and hands floats it meets in
/// inline content to <see cref="PlaceFloat"/>. Both are valid only while the layout call that received the request is running.
/// </remarks>
public interface IExclusionSpace
{
    /// <summary>
    /// The inline range a line box occupying a band can use (CSS2 §9.4.2: a line box is shortened by floats that intersect it).
    /// A float intersects the band when its margin box overlaps it by any amount; with <paramref name="height"/> 0 the band is the
    /// single line at <paramref name="bfcTop"/>. Floats with an empty margin box never intersect.
    /// </summary>
    /// <param name="bfcTop">Top of the band in block-formatting-context coordinates.</param>
    /// <param name="height">Height of the band in px; 0 or more.</param>
    /// <param name="bfcLeft">Left edge of the containing block's content box in block-formatting-context coordinates.</param>
    /// <param name="bfcRight">Right edge of the containing block's content box in block-formatting-context coordinates.</param>
    /// <param name="rangeStart">Receives the left end of the usable range, at least <paramref name="bfcLeft"/>.</param>
    /// <param name="rangeEnd">Receives the right end of the usable range, at most <paramref name="bfcRight"/> and not below <paramref name="rangeStart"/>; equal to <paramref name="rangeStart"/> when floats leave no room.</param>
    /// <param name="nextEdge">Receives the y of the smallest bottom margin edge, below <paramref name="bfcTop"/>, among the floats that intersect the band: the next place where more room may appear. <see cref="double.PositiveInfinity"/> when no float intersects.</param>
    void Query(double bfcTop, double height, double bfcLeft, double bfcRight, out double rangeStart, out double rangeEnd, out double nextEdge);

    /// <summary>
    /// Lays out and positions a float met in inline content (CSS2 §9.5.1 rules 6 and 7): its top is not above
    /// <paramref name="minBfcTop"/>, which the inline layout sets to the top of the current line when the float fits on that
    /// line and to the bottom of the line otherwise. The float's fragment is added to <paramref name="container"/>.
    /// </summary>
    /// <param name="request">The request the inline layout is running for.</param>
    /// <param name="container">The fragment of <see cref="InlineLayoutRequest.Container"/>.</param>
    /// <param name="floatBox">A box whose computed <c>float</c> is not <c>none</c>, a descendant of the container.</param>
    /// <param name="minBfcTop">The highest y the float's margin box may start at, in block-formatting-context coordinates.</param>
    /// <param name="marginBox">Receives the float's margin box in block-formatting-context coordinates; empty when nothing was placed.</param>
    /// <returns>The float's fragment, or the null fragment when it was not laid out (nesting limit).</returns>
    /// <exception cref="ArgumentException"><paramref name="floatBox"/> is not a float of the box tree being laid out.</exception>
    Fragment PlaceFloat(in InlineLayoutRequest request, Fragment container, Box floatBox, double minBfcTop, out Rect marginBox);
}

/// <summary>An exclusion space with no floats, for layouts run without a block formatting context.</summary>
public sealed class NoExclusions : IExclusionSpace
{
    /// <summary>The shared instance.</summary>
    public static NoExclusions Instance { get; } = new();

    private NoExclusions()
    {
    }

    /// <inheritdoc />
    public void Query(double bfcTop, double height, double bfcLeft, double bfcRight, out double rangeStart, out double rangeEnd, out double nextEdge)
    {
        rangeStart = bfcLeft;
        rangeEnd = Math.Max(bfcLeft, bfcRight);
        nextEdge = double.PositiveInfinity;
    }

    /// <inheritdoc />
    public Fragment PlaceFloat(in InlineLayoutRequest request, Fragment container, Box floatBox, double minBfcTop, out Rect marginBox)
    {
        marginBox = default;
        return default;
    }
}
