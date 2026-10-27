namespace OneRender.Painting;

/// <summary>
/// The stable identity of a display-list item. The painter that emits an item owns its id: repainting an unchanged box emits the
/// same id, which is how damage and patching find the item again. Ids are unique within one list; zero is never valid.
/// </summary>
/// <remarks>
/// A convenient scheme is one owner number per box (or text run) and one part number per thing the box paints, see <see cref="Of"/>.
/// </remarks>
/// <param name="Value">The 64-bit id. Zero is reserved for "no id".</param>
public readonly record struct ItemId(ulong Value)
{
    /// <summary>The reserved "no id" value; adding an item with it throws.</summary>
    public static ItemId None => default;

    /// <summary>True when this is not <see cref="None"/>.</summary>
    public bool IsValid => Value != 0;

    /// <summary>Builds an id from an owner number and two part numbers: <c>owner &lt;&lt; 32 | part &lt;&lt; 16 | sub</c>.</summary>
    /// <param name="owner">The owning box or run (a node number, for example); must be non-zero for the id to be valid.</param>
    /// <param name="part">Which thing the owner paints (background, border, shadow, text...), 0 to 65535.</param>
    /// <param name="sub">A repeat index within the part (the n-th background layer, the n-th line fragment), 0 to 65535.</param>
    /// <returns>The packed id.</returns>
    public static ItemId Of(uint owner, ushort part = 0, ushort sub = 0) =>
        new(((ulong)owner << 32) | ((ulong)part << 16) | sub);

    /// <summary>The owner number of an id made with <see cref="Of"/>.</summary>
    public uint Owner => (uint)(Value >> 32);

    /// <summary>The part number of an id made with <see cref="Of"/>.</summary>
    public ushort Part => (ushort)(Value >> 16);

    /// <summary>The sub-index of an id made with <see cref="Of"/>.</summary>
    public ushort Sub => (ushort)Value;
}

/// <summary>
/// A host's name for an image it will supply at draw time. The engine never loads images: the host resolves the handle
/// to pixels (or a texture) in its own table.
/// </summary>
/// <param name="Value">Any host-chosen number. Zero is allowed.</param>
public readonly record struct ImageHandle(int Value);

/// <summary>One color stop of a gradient, after CSS Images 3 §3.4.3 stop fixup.</summary>
/// <param name="Offset">
/// Position along the gradient line (linear), from the center to the ending shape (radial) or around the circle in turns (conic),
/// where 0 is the start and 1 the end. May lie outside 0 to 1. Offsets must not decrease; a smaller one is raised to the
/// previous stop's when added.
/// </param>
/// <param name="Color">The stop color (sRGB, straight alpha). Interpolation is in premultiplied space, CSS Images 3 §3.5.2.</param>
public readonly record struct GradientStop(float Offset, PaintColor Color);

/// <summary>
/// What a fill item paints with: one color or a gradient created on the same list with
/// <see cref="DisplayList.LinearGradient"/>, <see cref="DisplayList.RadialGradient"/> or <see cref="DisplayList.ConicGradient"/>.
/// A gradient paint is only valid for the list that made it, until that list is cleared.
/// </summary>
public readonly record struct Paint
{
    internal Paint(PaintKind kind, PaintColor color, int gradient)
    {
        Kind = kind;
        Color = color;
        Gradient = gradient;
    }

    /// <summary>The kind of paint.</summary>
    public PaintKind Kind { get; }

    /// <summary>The color of a solid paint; transparent for gradients.</summary>
    public PaintColor Color { get; }

    /// <summary>Index of the gradient in its list; -1 for solid paints.</summary>
    internal int Gradient { get; }

    /// <summary>A solid color paint.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The paint.</returns>
    public static Paint FromColor(PaintColor color) => new(PaintKind.Solid, color, -1);

    /// <summary>Implicit conversion so a color can be passed where a paint is expected.</summary>
    /// <param name="color">The color.</param>
    /// <returns>A solid paint.</returns>
    public static implicit operator Paint(PaintColor color) => FromColor(color);

    /// <summary>The same conversion by name, for languages without implicit operators.</summary>
    /// <param name="color">The color.</param>
    /// <returns>A solid paint.</returns>
    public static Paint ToPaint(PaintColor color) => FromColor(color);
}

/// <summary>
/// A reusable, growable path: verbs and coordinates in CSS pixels. Fill and clip items copy it, so one buffer can be refilled
/// for every path of a frame without allocating once it has grown.
/// </summary>
public sealed class PathBuffer
{
    private byte[] _verbs = new byte[16];
    private float[] _coords = new float[64];
    private int _verbCount;
    private int _coordCount;

    /// <summary>Number of verbs.</summary>
    public int VerbCount => _verbCount;

    /// <summary>Number of coordinates (x and y count separately).</summary>
    public int CoordCount => _coordCount;

    /// <summary>The verbs, as <see cref="PathVerb"/> values.</summary>
    public ReadOnlySpan<byte> Verbs => new(_verbs, 0, _verbCount);

    /// <summary>The coordinates in verb order.</summary>
    public ReadOnlySpan<float> Coords => new(_coords, 0, _coordCount);

    /// <summary>Empties the path, keeping its storage.</summary>
    public void Clear()
    {
        _verbCount = 0;
        _coordCount = 0;
    }

    /// <summary>Starts a new figure.</summary>
    /// <param name="x">X in CSS pixels.</param>
    /// <param name="y">Y in CSS pixels.</param>
    public void MoveTo(float x, float y) => Add(PathVerb.Move, x, y, 0, 0, 0, 0, 2);

    /// <summary>Adds a straight segment.</summary>
    /// <param name="x">End x.</param>
    /// <param name="y">End y.</param>
    public void LineTo(float x, float y) => Add(PathVerb.Line, x, y, 0, 0, 0, 0, 2);

    /// <summary>Adds a quadratic Bézier segment.</summary>
    /// <param name="cx">Control x.</param>
    /// <param name="cy">Control y.</param>
    /// <param name="x">End x.</param>
    /// <param name="y">End y.</param>
    public void QuadTo(float cx, float cy, float x, float y) => Add(PathVerb.Quad, cx, cy, x, y, 0, 0, 4);

    /// <summary>Adds a cubic Bézier segment.</summary>
    /// <param name="c1x">First control x.</param>
    /// <param name="c1y">First control y.</param>
    /// <param name="c2x">Second control x.</param>
    /// <param name="c2y">Second control y.</param>
    /// <param name="x">End x.</param>
    /// <param name="y">End y.</param>
    public void CubicTo(float c1x, float c1y, float c2x, float c2y, float x, float y) => Add(PathVerb.Cubic, c1x, c1y, c2x, c2y, x, y, 6);

    /// <summary>Closes the current figure.</summary>
    public void Close() => Add(PathVerb.Close, 0, 0, 0, 0, 0, 0, 0);

    private void Add(PathVerb verb, float a, float b, float c, float d, float e, float f, int n)
    {
        if (_verbCount >= DisplayListLimits.MaxPathVerbsPerPath || _coordCount + n > DisplayListLimits.MaxPathVerbsPerPath * 6)
        {
            throw new DisplayListLimitException("A path has more than " + DisplayListLimits.MaxPathVerbsPerPath + " segments.");
        }

        if (_verbCount == _verbs.Length)
        {
            Array.Resize(ref _verbs, _verbs.Length * 2);
        }

        if (_coordCount + n > _coords.Length)
        {
            Array.Resize(ref _coords, Math.Max(_coords.Length * 2, _coordCount + n));
        }

        _verbs[_verbCount++] = (byte)verb;
        float[] c0 = _coords;
        int p = _coordCount;
        if (n >= 2)
        {
            c0[p] = a;
            c0[p + 1] = b;
        }

        if (n >= 4)
        {
            c0[p + 2] = c;
            c0[p + 3] = d;
        }

        if (n >= 6)
        {
            c0[p + 4] = e;
            c0[p + 5] = f;
        }

        _coordCount += n;
    }
}
