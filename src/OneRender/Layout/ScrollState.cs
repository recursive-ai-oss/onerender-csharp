using OneRender.Style;
using OneRender.Tree;

namespace OneRender.Layout;

/// <summary>The offsets a scroll container can be scrolled to, in px: scrolling right moves <c>X</c> up, scrolling down moves <c>Y</c> up.</summary>
public readonly struct ScrollRange : IEquatable<ScrollRange>
{
    /// <summary>Creates a range.</summary>
    /// <param name="minX">Smallest horizontal offset; 0, or negative for right-to-left containers whose content extends to the left.</param>
    /// <param name="maxX">Largest horizontal offset, at least <paramref name="minX"/>.</param>
    /// <param name="minY">Smallest vertical offset; 0.</param>
    /// <param name="maxY">Largest vertical offset, at least <paramref name="minY"/>.</param>
    public ScrollRange(double minX, double maxX, double minY, double maxY)
    {
        MinX = minX;
        MaxX = Math.Max(minX, maxX);
        MinY = minY;
        MaxY = Math.Max(minY, maxY);
    }

    /// <summary>Smallest horizontal offset.</summary>
    public double MinX { get; }

    /// <summary>Largest horizontal offset.</summary>
    public double MaxX { get; }

    /// <summary>Smallest vertical offset.</summary>
    public double MinY { get; }

    /// <summary>Largest vertical offset.</summary>
    public double MaxY { get; }

    /// <summary>True when the container can scroll on at least one axis.</summary>
    public bool CanScroll => MaxX > MinX || MaxY > MinY;

    /// <summary>Clamps a horizontal offset into the range; NaN becomes 0.</summary>
    /// <param name="x">The requested offset.</param>
    public double ClampX(double x) => double.IsNaN(x) ? Math.Clamp(0, MinX, MaxX) : Math.Clamp(x, MinX, MaxX);

    /// <summary>Clamps a vertical offset into the range; NaN becomes 0.</summary>
    /// <param name="y">The requested offset.</param>
    public double ClampY(double y) => double.IsNaN(y) ? Math.Clamp(0, MinY, MaxY) : Math.Clamp(y, MinY, MaxY);

    /// <inheritdoc />
    public bool Equals(ScrollRange other) => MinX.Equals(other.MinX) && MaxX.Equals(other.MaxX) && MinY.Equals(other.MinY) && MaxY.Equals(other.MaxY);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ScrollRange r && Equals(r);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(MinX, MaxX, MinY, MaxY);

    /// <summary>Equality.</summary>
    public static bool operator ==(ScrollRange left, ScrollRange right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(ScrollRange left, ScrollRange right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => FormattableString.Invariant($"x {MinX:0.##}..{MaxX:0.##}, y {MinY:0.##}..{MaxY:0.##}");

    /// <summary>
    /// The range of a fragment's box. The scrollport is the padding box (Overflow 3 §2.1); the scrollable overflow area was recorded
    /// by layout. Overflow that extends before the start edge of a left-to-right container cannot be reached (Overflow 3 §2.2).
    /// </summary>
    internal static ScrollRange Of(FragmentTree tree, int fragment, in ComputedValues v)
    {
        Rect bb = tree.BorderBoxOf(fragment);
        Insets border = tree.BorderOf(fragment);
        Rect ov = tree.OverflowOf(fragment);
        double maxX = 0, minX = 0, maxY = 0;
        if (v.OverflowX != OverflowKind.Visible && v.OverflowX != OverflowKind.Clip)
        {
            maxX = Math.Max(0, ov.Right - (bb.Width - border.Right));
            if (v.Direction == DirectionKind.Rtl)
            {
                minX = Math.Min(0, ov.X - border.Left);
                maxX = 0;
            }
        }

        if (v.OverflowY != OverflowKind.Visible && v.OverflowY != OverflowKind.Clip)
        {
            maxY = Math.Max(0, ov.Bottom - (bb.Height - border.Bottom));
        }

        return new ScrollRange(minX, maxX, 0, maxY);
    }
}

/// <summary>
/// The scroll positions the host has set: one for the viewport and one per scroll container (a box with <c>overflow</c> other than
/// <c>visible</c> or <c>clip</c>). Positions are plain input, like time: the engine never scrolls by itself. Layout does not read them;
/// the painter applies them as translations, so changing one never relays out and, through
/// <c>Painter.PatchScroll</c>, never repaints either.
/// </summary>
/// <remarks>
/// Offsets are stored as requested and clamped to the scrollable overflow when used, so a position set before layout, or kept
/// across a relayout that shrinks the content, is always valid. A container is keyed by its element (the styled node and the
/// generation of its slot), so its position follows it when boxes are made again around it, when siblings are inserted, removed or moved,
/// and is forgotten when the node is removed. The viewport's scrollable area is the root element's scrollable overflow.
/// </remarks>
public sealed class ScrollState
{
    private double[] _x = new double[16];
    private double[] _y = new double[16];
    // Generation of the node's slot plus one when the position was set; 0 for never set.
    private int[] _gen = new int[16];

    /// <summary>The requested horizontal viewport offset in px; 0 initially.</summary>
    public double ViewportX { get; private set; }

    /// <summary>The requested vertical viewport offset in px; 0 initially.</summary>
    public double ViewportY { get; private set; }

    /// <summary>Sets the viewport's scroll position; NaN counts as 0. Allocates nothing.</summary>
    /// <param name="x">Horizontal offset in px.</param>
    /// <param name="y">Vertical offset in px.</param>
    public void SetViewportOffset(double x, double y)
    {
        ViewportX = double.IsNaN(x) ? 0 : x;
        ViewportY = double.IsNaN(y) ? 0 : y;
    }

    /// <summary>Sets a scroll container's position; NaN counts as 0. Allocates nothing once the state has seen a box of that index.</summary>
    /// <param name="box">The scroll container's box.</param>
    /// <param name="x">Horizontal offset in px.</param>
    /// <param name="y">Vertical offset in px.</param>
    /// <exception cref="ArgumentException"><paramref name="box"/> is the null handle.</exception>
    public void SetOffset(Box box, double x, double y)
    {
        if (box.IsNull)
        {
            throw new ArgumentException("The box is the null handle.", nameof(box));
        }

        int node = box.Tree!.NodeIndexOf(box.Index);
        if (node < 0)
        {
            return; // an anonymous box is never a scroll container
        }

        if (node >= _x.Length)
        {
            int size = Math.Max(node + 1, _x.Length * 2);
            Array.Resize(ref _x, size);
            Array.Resize(ref _y, size);
            Array.Resize(ref _gen, size);
        }

        _x[node] = double.IsNaN(x) ? 0 : x;
        _y[node] = double.IsNaN(y) ? 0 : y;
        _gen[node] = box.Tree.Styled.GenerationOf(node) + 1;
    }

    // After the styled tree was built again (a restyle that changes which nodes exist), node indices are new: the container positions
    // move to the nodes made from the same source elements. Containers whose source element is gone are forgotten.
    internal void Remap(StyledTree before, StyledTree after)
    {
        double[] x = new double[Math.Max(16, after.Capacity)];
        double[] y = new double[x.Length];
        int[] gen = new int[x.Length];
        for (int i = 0; i < _gen.Length; i++)
        {
            if (_gen[i] == 0 || (_x[i] == 0 && _y[i] == 0) || i >= before.Capacity || before.IsFreeSlot(i) || before.GenerationOf(i) + 1 != _gen[i])
            {
                continue;
            }

            int source = before.SourceIndexOf(i);
            StyledNode moved = source < 0 ? default : after.FindBySource(source);
            if (!moved.IsNull)
            {
                x[moved.Index] = _x[i];
                y[moved.Index] = _y[i];
                gen[moved.Index] = after.GenerationOf(moved.Index) + 1;
            }
        }

        _x = x;
        _y = y;
        _gen = gen;
    }

    /// <summary>Forgets every position, including the viewport's.</summary>
    public void Clear()
    {
        Array.Clear(_x);
        Array.Clear(_y);
        Array.Clear(_gen);
        ViewportX = ViewportY = 0;
    }

    internal double RequestedX(int node, int generation) => (uint)node < (uint)_x.Length && _gen[node] == generation + 1 ? _x[node] : 0;

    internal double RequestedY(int node, int generation) => (uint)node < (uint)_y.Length && _gen[node] == generation + 1 ? _y[node] : 0;

    internal double RequestedX(BoxTree boxes, int box)
    {
        int node = boxes.NodeIndexOf(box);
        return node < 0 ? 0 : RequestedX(node, boxes.Styled.GenerationOf(node));
    }

    internal double RequestedY(BoxTree boxes, int box)
    {
        int node = boxes.NodeIndexOf(box);
        return node < 0 ? 0 : RequestedY(node, boxes.Styled.GenerationOf(node));
    }

    /// <summary>The offsets a scroll container can be scrolled to after the last layout.</summary>
    /// <param name="fragments">The laid-out tree.</param>
    /// <param name="box">The scroll container's box.</param>
    /// <returns>The range; all zero when the box has no fragment, is not a scroll container, or its content fits.</returns>
    public static ScrollRange GetRange(FragmentTree fragments, Box box)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        Fragment f = fragments.FragmentOf(box);
        if (f.IsNull)
        {
            return default;
        }

        ViewportOverflow.Resolve(fragments.Boxes, out _, out _, out int source);
        return box.Index == source ? default : ScrollRange.Of(fragments, f.Index, in box.Values);
    }

    /// <summary>The offsets the viewport can be scrolled to: the root's scrollable overflow beyond the viewport.</summary>
    /// <param name="fragments">The laid-out tree.</param>
    /// <param name="viewportWidth">Viewport width in px.</param>
    /// <param name="viewportHeight">Viewport height in px; 0 or less means unknown, which allows no scrolling.</param>
    public static ScrollRange GetViewportRange(FragmentTree fragments, double viewportWidth, double viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        if (fragments.Count == 0 || !(viewportWidth > 0) || !(viewportHeight > 0))
        {
            return default;
        }

        Rect ov = fragments.OverflowOf(0);
        return new ScrollRange(0, ov.Right - viewportWidth, 0, ov.Bottom - viewportHeight);
    }

    /// <summary>A scroll container's position after clamping to what it can reach.</summary>
    /// <param name="fragments">The laid-out tree.</param>
    /// <param name="box">The scroll container's box.</param>
    /// <returns>The offset in px.</returns>
    public (double X, double Y) GetOffset(FragmentTree fragments, Box box)
    {
        ScrollRange r = GetRange(fragments, box);
        return (r.ClampX(RequestedX(fragments.Boxes, box.Index)), r.ClampY(RequestedY(fragments.Boxes, box.Index)));
    }
}
