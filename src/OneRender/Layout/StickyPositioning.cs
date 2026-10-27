namespace OneRender.Layout;

/// <summary>
/// CSS Position 3 §3.4: where a sticky positioned box sits for a given scroll position. The result is a pure function of
/// geometry layout already fixed and the host's scroll offset, so scrolling never needs a relayout.
/// </summary>
public static class StickyPositioning
{
    /// <summary>
    /// Computes how far a sticky box is shifted, as relative positioning would. On each axis the box is moved inward of the
    /// scrollport edge by its inset when it would otherwise be beyond that edge, but never so far that its margin box leaves its
    /// containing block. With both insets of an axis given and a box too large for both, the start inset (top, left) wins.
    /// </summary>
    /// <param name="borderBox">The box's border box where layout put it, in the scroll container's unscrolled space.</param>
    /// <param name="marginTop">Top margin in px.</param>
    /// <param name="marginRight">Right margin in px.</param>
    /// <param name="marginBottom">Bottom margin in px.</param>
    /// <param name="marginLeft">Left margin in px.</param>
    /// <param name="containingBlock">The box's containing block (its parent's content box) in the same space.</param>
    /// <param name="scrollport">The scroll container's scrollport (padding box) in the same space, as laid out, before scrolling.</param>
    /// <param name="scrollX">The scroll container's horizontal offset in px.</param>
    /// <param name="scrollY">The scroll container's vertical offset in px.</param>
    /// <param name="top">Resolved <c>top</c> in px, NaN for <c>auto</c>.</param>
    /// <param name="right">Resolved <c>right</c> in px, NaN for <c>auto</c>.</param>
    /// <param name="bottom">Resolved <c>bottom</c> in px, NaN for <c>auto</c>.</param>
    /// <param name="left">Resolved <c>left</c> in px, NaN for <c>auto</c>.</param>
    /// <param name="dx">Receives the horizontal shift in px.</param>
    /// <param name="dy">Receives the vertical shift in px.</param>
    public static void Offset(
        Rect borderBox, double marginTop, double marginRight, double marginBottom, double marginLeft,
        Rect containingBlock, Rect scrollport, double scrollX, double scrollY,
        double top, double right, double bottom, double left,
        out double dx, out double dy)
    {
        dy = Axis(
            borderBox.Y, borderBox.Bottom, marginTop, marginBottom,
            containingBlock.Y, containingBlock.Bottom,
            scrollport.Y + scrollY, scrollport.Bottom + scrollY, top, bottom);
        dx = Axis(
            borderBox.X, borderBox.Right, marginLeft, marginRight,
            containingBlock.X, containingBlock.Right,
            scrollport.X + scrollX, scrollport.Right + scrollX, left, right);
    }

    private static double Axis(double start, double end, double marginStart, double marginEnd, double cbStart, double cbEnd, double winStart, double winEnd, double insetStart, double insetEnd)
    {
        double d = 0;
        if (!double.IsNaN(insetEnd))
        {
            double over = end - (winEnd - insetEnd);
            if (over > 0)
            {
                d = -over;
            }
        }

        if (!double.IsNaN(insetStart))
        {
            double under = (winStart + insetStart) - (start + d);
            if (under > 0)
            {
                d += under;
            }
        }

        // The margin box stays inside the containing block (the "position box" of §3.4).
        double lo = cbStart - (start - marginStart);
        double hi = cbEnd - (end + marginEnd);
        return Math.Min(Math.Max(d, lo), hi);
    }
}
