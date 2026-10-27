namespace OneRender.Painting;

/// <summary>
/// The hard limits of a display list. Every one is checked before the storage it guards grows, so hostile input fails
/// with a <see cref="DisplayListLimitException"/> instead of exhausting memory.
/// </summary>
public static class DisplayListLimits
{
    /// <summary>Items in one list, structural pushes and pops included.</summary>
    public const int MaxItems = 1 << 20;

    /// <summary>Nesting depth of open clips, transforms, groups and tags together.</summary>
    public const int MaxNestingDepth = 256;

    /// <summary>Verbs in one path.</summary>
    public const int MaxPathVerbsPerPath = 1 << 20;

    /// <summary>Path verbs over all paths of a list.</summary>
    public const int MaxPathVerbs = 1 << 23;

    /// <summary>Glyphs over all glyph runs of a list.</summary>
    public const int MaxGlyphs = 1 << 22;

    /// <summary>UTF-16 code units of source text over all glyph runs of a list.</summary>
    public const int MaxTextChars = 1 << 23;

    /// <summary>Stops of one gradient.</summary>
    public const int MaxStopsPerGradient = 1024;

    /// <summary>Gradients in a list.</summary>
    public const int MaxGradients = 1 << 18;

    /// <summary>Distinct font faces referenced by one list.</summary>
    public const int MaxFaces = 4096;

    /// <summary>Strings (alternative texts, link targets, languages) in one list.</summary>
    public const int MaxStrings = 1 << 20;

    /// <summary>Rectangles a <see cref="DamageRegion"/> keeps before it merges them all into one.</summary>
    public const int MaxDamageRects = 16;
}

/// <summary>A display list was asked to hold more than a documented limit.</summary>
public sealed class DisplayListLimitException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public DisplayListLimitException()
    {
    }

    /// <summary>Creates the exception with a message naming the limit.</summary>
    /// <param name="message">What was exceeded.</param>
    public DisplayListLimitException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">What was exceeded.</param>
    /// <param name="innerException">The cause.</param>
    public DisplayListLimitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A display list was built wrongly: unbalanced pushes and pops, a duplicate or zero id, or a value that is not finite.</summary>
public sealed class DisplayListException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public DisplayListException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What is wrong.</param>
    public DisplayListException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">What is wrong.</param>
    /// <param name="innerException">The cause.</param>
    public DisplayListException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The screen area that changed between two versions of a list, in the list's root coordinate space (CSS pixels, before any
/// device-pixel ratio). It keeps up to <see cref="DisplayListLimits.MaxDamageRects"/> rectangles; rectangles that overlap or
/// touch are merged, and past the maximum everything collapses into one bounding rectangle. A region is reusable and
/// allocates only while it grows to its first working size.
/// </summary>
public sealed class DamageRegion
{
    private readonly RectF[] _rects = new RectF[DisplayListLimits.MaxDamageRects];
    private int _count;

    /// <summary>Number of rectangles.</summary>
    public int Count => _count;

    /// <summary>True when nothing changed.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>The rectangle at <paramref name="index"/>.</summary>
    /// <param name="index">0 to <see cref="Count"/> - 1.</param>
    /// <returns>A non-empty rectangle.</returns>
    public RectF this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count);
            return _rects[index];
        }
    }

    /// <summary>The bounding rectangle of everything in the region; empty when the region is empty.</summary>
    public RectF Bounds
    {
        get
        {
            RectF b = RectF.Empty;
            for (int i = 0; i < _count; i++)
            {
                b = RectF.Union(b, _rects[i]);
            }

            return b;
        }
    }

    /// <summary>Empties the region.</summary>
    public void Clear() => _count = 0;

    /// <summary>
    /// Adds a rectangle, merging it with every rectangle it overlaps or touches. Empty rectangles are ignored.
    /// </summary>
    /// <param name="rect">The changed area in root coordinates.</param>
    public void Add(in RectF rect)
    {
        if (rect.IsEmpty)
        {
            return;
        }

        RectF r = rect;
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < _count; i++)
            {
                if (_rects[i].Touches(r))
                {
                    r = RectF.Union(r, _rects[i]);
                    _rects[i] = _rects[--_count];
                    merged = true;
                    break;
                }
            }
        }

        if (_count == _rects.Length)
        {
            RectF all = r;
            for (int i = 0; i < _count; i++)
            {
                all = RectF.Union(all, _rects[i]);
            }

            _rects[0] = all;
            _count = 1;
            return;
        }

        _rects[_count++] = r;
    }

    /// <summary>Adds every rectangle of another region.</summary>
    /// <param name="other">The region to merge in.</param>
    public void Add(DamageRegion other)
    {
        ArgumentNullException.ThrowIfNull(other);
        for (int i = 0; i < other._count; i++)
        {
            Add(other._rects[i]);
        }
    }

    /// <summary>The bounding rectangle rounded outward to whole device pixels at a scale.</summary>
    /// <param name="devicePixelRatio">Device pixels per CSS pixel, greater than 0.</param>
    /// <returns>An integer rectangle covering <see cref="Bounds"/>; empty when the region is empty.</returns>
    public RectI ToDevicePixels(float devicePixelRatio)
    {
        RectF b = Bounds;
        if (b.IsEmpty || !(devicePixelRatio > 0f))
        {
            return default;
        }

        int x0 = (int)Math.Floor(Math.Max(b.X * devicePixelRatio, -1e9f));
        int y0 = (int)Math.Floor(Math.Max(b.Y * devicePixelRatio, -1e9f));
        int x1 = (int)Math.Ceiling(Math.Min(b.Right * devicePixelRatio, 1e9f));
        int y1 = (int)Math.Ceiling(Math.Min(b.Bottom * devicePixelRatio, 1e9f));
        return new RectI(x0, y0, x1 - x0, y1 - y0);
    }
}
