using System.Numerics;
using OneFont;

namespace OneRender.Painting;

/// <summary>One side of a border: width, style and color (CSS Backgrounds 3 §4).</summary>
/// <param name="Width">Border width in CSS pixels, zero or more. The painter passes zero for <see cref="BorderStyle.None"/> and <see cref="BorderStyle.Hidden"/>.</param>
/// <param name="Style">The line style.</param>
/// <param name="Color">The color of this side.</param>
public readonly record struct BorderSide(float Width, BorderStyle Style, PaintColor Color);

/// <summary>
/// A retained, pooled list of paint items for one frame or page, in paint order. A painter fills it with the <c>Fill*</c>,
/// <c>Draw*</c>, <c>Push*</c> and <c>Pop*</c> methods; backends read it item by item through <see cref="this[int]"/>.
/// <para>
/// All coordinates are CSS pixels in the list's root space (x right, y down); a backend multiplies by the device-pixel ratio
/// when it draws. All storage is kept between <see cref="Clear"/> calls, so refilling a list of the same shape allocates nothing.
/// A list is not thread-safe.
/// </para>
/// </summary>
public sealed partial class DisplayList
{
    /// <summary>Major version of the format. A reader of major N must refuse lists of another major.</summary>
    public const int FormatMajor = 1;

    /// <summary>Minor version of the format this library writes. Higher minors only add item kinds and fields.</summary>
    public const int FormatMinor = 2;

    private const int FlagInset = 1;
    private const int FlagTiled = 2;
    private const int FlagEvenOdd = 1;
    private const int FlagRtl = 1;

    internal ItemRecord[] _items = new ItemRecord[64];
    private int _count;

    internal float[] _num = new float[512];
    internal int _numCount;

    internal byte[] _verbs = new byte[256];
    internal float[] _coords = new float[1024];
    private int _verbCount;
    private int _coordCount;
    internal int[] _paths = new int[64];
    private int _pathCount;

    internal GradientRecord[] _gradients = new GradientRecord[8];
    private int _gradientCount;
    internal float[] _stops = new float[64];
    private int _stopCount;

    internal int[] _glyphIds = new int[256];
    internal float[] _glyphX = new float[256];
    internal float[] _glyphY = new float[256];
    internal int[] _glyphClusters = new int[256];
    private int _glyphCount;
    internal char[] _text = new char[256];
    private int _textCount;

    internal FontFace[] _faces = new FontFace[8];
    private ulong[] _faceHashes = new ulong[8];
    private int _faceCount;

    internal string?[] _strings = new string?[16];
    private int _stringCount;

    private PushState[] _states = new PushState[16];
    private int _stateCount;

    private int[] _table = new int[128];
    private byte[] _seen = new byte[64];

    private int _requiredMinor;
    private int _top = -1;
    private int _depth;
    private ulong _hash;

    /// <summary>Number of items, structural pushes and pops included.</summary>
    public int Count => _count;

    /// <summary>The lowest minor version of the format a reader needs for every item in this list. Always 0 in format 1.0.</summary>
    public int RequiredMinor => _requiredMinor;

    /// <summary>Number of currently open pushes (clips, transforms, groups, tags). Zero in a finished list.</summary>
    public int OpenDepth => _depth;

    /// <summary>The item at <paramref name="index"/>.</summary>
    /// <param name="index">0 to <see cref="Count"/> - 1, in paint order.</param>
    /// <returns>A read-only view of the item, valid until the list is cleared or changed.</returns>
    public DisplayItem this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_count);
            return new DisplayItem(this, index);
        }
    }

    /// <summary>Removes every item and keeps all storage for reuse.</summary>
    public void Clear()
    {
        _count = 0;
        _numCount = 0;
        _verbCount = 0;
        _coordCount = 0;
        _pathCount = 0;
        _gradientCount = 0;
        _stopCount = 0;
        _glyphCount = 0;
        _textCount = 0;
        _stateCount = 0;
        Array.Clear(_faces, 0, _faceCount);
        _faceCount = 0;
        Array.Clear(_strings, 0, _stringCount);
        _stringCount = 0;
        Array.Clear(_table);
        _top = -1;
        _depth = 0;
        _requiredMinor = 0;
    }

    /// <summary>Throws <see cref="DisplayListException"/> unless every push has been closed.</summary>
    public void Validate()
    {
        if (_depth != 0)
        {
            throw new DisplayListException("The list has " + _depth + " unclosed push item(s).");
        }
    }

    /// <summary>Finds an item by id.</summary>
    /// <param name="id">The id the painter gave the item.</param>
    /// <param name="index">Receives the item's position in paint order.</param>
    /// <returns>True when the list has the id.</returns>
    public bool TryFind(ItemId id, out int index)
    {
        index = Find(id.Value);
        return index >= 0;
    }

    /// <summary>The font face a glyph run refers to.</summary>
    /// <param name="faceIndex">The index a run reports.</param>
    /// <returns>The face.</returns>
    public FontFace GetFace(int faceIndex)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)faceIndex, (uint)_faceCount);
        return _faces[faceIndex];
    }

    // ---------------------------------------------------------------- gradients

    /// <summary>
    /// Creates a linear gradient paint (CSS Images 3 §3.1). The gradient line runs from (x0, y0) to (x1, y1) in the same
    /// coordinates as the item that uses it; offset 0 is at the start point and 1 at the end point.
    /// </summary>
    /// <param name="x0">Start x.</param>
    /// <param name="y0">Start y.</param>
    /// <param name="x1">End x.</param>
    /// <param name="y1">End y.</param>
    /// <param name="stops">At least one stop; offsets should not decrease (smaller ones are raised).</param>
    /// <param name="repeating">True for <c>repeating-linear-gradient</c>: the pattern between the first and last stop repeats.</param>
    /// <returns>A paint for fill items of this list.</returns>
    public Paint LinearGradient(float x0, float y0, float x1, float y1, ReadOnlySpan<GradientStop> stops, bool repeating = false) =>
        AddGradient(PaintKind.Linear, repeating, x0, y0, x1, y1, 0f, stops);

    /// <summary>
    /// Creates a radial gradient paint (CSS Images 3 §3.2): an ellipse (a circle when the radii are equal) centered at
    /// (cx, cy); offset 0 is the center and 1 is the ellipse edge.
    /// </summary>
    /// <param name="cx">Center x.</param>
    /// <param name="cy">Center y.</param>
    /// <param name="radiusX">Horizontal radius of the ending shape, 0 or more.</param>
    /// <param name="radiusY">Vertical radius of the ending shape, 0 or more.</param>
    /// <param name="stops">At least one stop.</param>
    /// <param name="repeating">True for <c>repeating-radial-gradient</c>.</param>
    /// <returns>A paint for fill items of this list.</returns>
    public Paint RadialGradient(float cx, float cy, float radiusX, float radiusY, ReadOnlySpan<GradientStop> stops, bool repeating = false) =>
        AddGradient(PaintKind.Radial, repeating, cx, cy, radiusX, radiusY, 0f, stops);

    /// <summary>
    /// Creates a conic gradient paint (CSS Images 4 §3.3): colors sweep clockwise around (cx, cy). Offset 0 is at
    /// <paramref name="fromAngle"/> and offset 1 is one full turn later.
    /// </summary>
    /// <param name="cx">Center x.</param>
    /// <param name="cy">Center y.</param>
    /// <param name="fromAngle">Start angle in radians, measured clockwise from "up" (CSS <c>from</c>).</param>
    /// <param name="stops">At least one stop; offsets are in turns.</param>
    /// <param name="repeating">True for <c>repeating-conic-gradient</c>.</param>
    /// <returns>A paint for fill items of this list.</returns>
    public Paint ConicGradient(float cx, float cy, float fromAngle, ReadOnlySpan<GradientStop> stops, bool repeating = false) =>
        AddGradient(PaintKind.Conic, repeating, cx, cy, 0f, 0f, fromAngle, stops);

    private Paint AddGradient(PaintKind kind, bool repeating, float g0, float g1, float g2, float g3, float g4, ReadOnlySpan<GradientStop> stops)
    {
        Coord(g0);
        Coord(g1);
        Coord(g2);
        Coord(g3);
        Coord(g4);
        if (stops.Length == 0 || stops.Length > DisplayListLimits.MaxStopsPerGradient)
        {
            throw new DisplayListLimitException("A gradient needs 1 to " + DisplayListLimits.MaxStopsPerGradient + " stops.");
        }

        if (_gradientCount >= DisplayListLimits.MaxGradients || _stopCount + stops.Length > DisplayListLimits.MaxGradients * 4)
        {
            throw new DisplayListLimitException("Too many gradients in one list.");
        }

        if ((kind == PaintKind.Radial) && (g2 < 0f || g3 < 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(stops), "Radial gradient radii must not be negative.");
        }

        for (int i = 0; i < stops.Length; i++)
        {
            Coord(stops[i].Offset);
        }

        Grow(ref _gradients, _gradientCount + 1);
        Grow(ref _stops, _stopCount + (stops.Length * 5));
        ulong h = Mix(0x6772616400000000UL + (ulong)kind, repeating ? 1UL : 0UL);
        h = Mix(h, Bits(g0));
        h = Mix(h, Bits(g1));
        h = Mix(h, Bits(g2));
        h = Mix(h, Bits(g3));
        h = Mix(h, Bits(g4));
        int start = _stopCount;
        float prev = float.NegativeInfinity;
        for (int i = 0; i < stops.Length; i++)
        {
            float off = Math.Max(stops[i].Offset, prev);
            prev = off;
            PaintColor c = ClampColor(stops[i].Color);
            _stops[_stopCount++] = off;
            _stops[_stopCount++] = c.R;
            _stops[_stopCount++] = c.G;
            _stops[_stopCount++] = c.B;
            _stops[_stopCount++] = c.A;
            h = Mix(h, Bits(off));
            h = Mix(h, Bits(c.R));
            h = Mix(h, Bits(c.G));
            h = Mix(h, Bits(c.B));
            h = Mix(h, Bits(c.A));
        }

        _gradients[_gradientCount] = new GradientRecord
        {
            Kind = kind,
            Repeating = repeating,
            G0 = g0,
            G1 = g1,
            G2 = g2,
            G3 = g3,
            G4 = g4,
            StopStart = start,
            StopCount = stops.Length,
            Hash = h,
        };
        return new Paint(kind, default, _gradientCount++);
    }

    // ---------------------------------------------------------------- fills

    /// <summary>Fills an axis-aligned rectangle.</summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="rect">The rectangle; an empty one paints nothing but still takes the id.</param>
    /// <param name="paint">A color or a gradient made by this list.</param>
    public void FillRect(ItemId id, in RectF rect, in Paint paint)
    {
        CheckRect(rect);
        CheckPaint(paint);
        Begin(ItemKind.FillRect, id, rect, 0, 8);
        PutRect(rect);
        PutPaint(paint);
        Finish(_count - 1);
    }

    /// <summary>Fills a rectangle with elliptical corners. The radii are constrained to the box (CSS Backgrounds 3 §5.5) when stored.</summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="rect">The rectangle.</param>
    /// <param name="radii">Corner radii, one horizontal and one vertical per corner.</param>
    /// <param name="paint">A color or a gradient made by this list.</param>
    public void FillRoundedRect(ItemId id, in RectF rect, in CornerRadii radii, in Paint paint)
    {
        CheckRect(rect);
        CheckPaint(paint);
        CornerRadii r = ConstrainRadii(rect, radii);
        Begin(ItemKind.FillRoundedRect, id, rect, 0, 16);
        PutRect(rect);
        PutRadii(r);
        PutPaint(paint);
        Finish(_count - 1);
    }

    /// <summary>Fills a path.</summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="path">The path; its contents are copied.</param>
    /// <param name="paint">A color or a gradient made by this list.</param>
    /// <param name="rule">The inside rule.</param>
    public void FillPath(ItemId id, PathBuffer path, in Paint paint, FillRule rule = FillRule.NonZero)
    {
        ArgumentNullException.ThrowIfNull(path);
        FillPath(id, path.Verbs, path.Coords, paint, rule);
    }

    /// <summary>Fills a path given as verbs and coordinates.</summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="verbs"><see cref="PathVerb"/> values.</param>
    /// <param name="coords">Coordinates for the verbs, in order; the count must match exactly.</param>
    /// <param name="paint">A color or a gradient made by this list.</param>
    /// <param name="rule">The inside rule.</param>
    public void FillPath(ItemId id, ReadOnlySpan<byte> verbs, ReadOnlySpan<float> coords, in Paint paint, FillRule rule = FillRule.NonZero)
    {
        CheckPaint(paint);
        RectF bounds = CheckPath(verbs, coords);
        ReservePath(verbs.Length, coords.Length);
        Begin(ItemKind.FillPath, id, bounds, rule == FillRule.EvenOdd ? FlagEvenOdd : 0, 8);
        int pathIndex = StorePath(verbs, coords);
        _items[_count - 1].C = pathIndex;
        PutPaint(paint);
        H(rule == FillRule.EvenOdd ? 1UL : 0UL);
        Finish(_count - 1);
    }

    // ---------------------------------------------------------------- border, shadow, image

    /// <summary>
    /// Draws a border (CSS Backgrounds 3 §4 and §5). <paramref name="rect"/> is the border box, whose rounded outline is the
    /// border's outer edge; the inner edge is that outline pulled in by the side widths, with each radius reduced by the
    /// adjacent widths (§5.2).
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="rect">The border box.</param>
    /// <param name="radii">Outer corner radii.</param>
    /// <param name="top">The top side.</param>
    /// <param name="right">The right side.</param>
    /// <param name="bottom">The bottom side.</param>
    /// <param name="left">The left side.</param>
    public void DrawBorder(ItemId id, in RectF rect, in CornerRadii radii, in BorderSide top, in BorderSide right, in BorderSide bottom, in BorderSide left)
    {
        CheckRect(rect);
        CheckSide(top);
        CheckSide(right);
        CheckSide(bottom);
        CheckSide(left);
        CornerRadii r = ConstrainRadii(rect, radii);
        Begin(ItemKind.Border, id, rect, 0, 32);
        PutRect(rect);
        PutRadii(r);
        PutF(top.Width);
        PutF(right.Width);
        PutF(bottom.Width);
        PutF(left.Width);
        PutColor(ClampColor(top.Color));
        PutColor(ClampColor(right.Color));
        PutColor(ClampColor(bottom.Color));
        PutColor(ClampColor(left.Color));
        _items[_count - 1].A = (int)top.Style | ((int)right.Style << 8) | ((int)bottom.Style << 16) | ((int)left.Style << 24);
        H((ulong)(uint)_items[_count - 1].A);
        Finish(_count - 1);
    }

    /// <summary>
    /// Draws a box shadow (CSS Backgrounds 3 §7). An outer shadow is the border box offset, grown by the spread and blurred
    /// with a Gaussian of standard deviation <c>blur / 2</c>, and it is not drawn inside the border box. An inset shadow is
    /// drawn inside the padding box: the area outside the offset, shrunk padding box, blurred, clipped to the padding box.
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="rect">The border box for an outer shadow, the padding box for an inset one.</param>
    /// <param name="radii">Corner radii of that box (outer radii for an outer shadow, padding-edge radii for an inset one).</param>
    /// <param name="color">Shadow color.</param>
    /// <param name="offsetX">Horizontal offset.</param>
    /// <param name="offsetY">Vertical offset.</param>
    /// <param name="blur">Blur radius, 0 or more; 0 is a sharp edge.</param>
    /// <param name="spread">Spread distance; positive grows an outer shadow (and shrinks an inset one), negative does the opposite.</param>
    /// <param name="inset">True for <c>inset</c>.</param>
    public void DrawBoxShadow(ItemId id, in RectF rect, in CornerRadii radii, in PaintColor color, float offsetX, float offsetY, float blur, float spread, bool inset)
    {
        CheckRect(rect);
        Coord(offsetX);
        Coord(offsetY);
        Coord(blur);
        Coord(spread);
        if (blur < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(blur), "Blur must not be negative.");
        }

        CornerRadii r = ConstrainRadii(rect, radii);
        _ = ClampColor(color);
        RectF bounds = rect;
        if (!inset)
        {
            float grow = Math.Max(spread, 0f) + (1.5f * blur);
            bounds = new RectF(rect.X + offsetX - grow, rect.Y + offsetY - grow, rect.Width + (2f * grow), rect.Height + (2f * grow));
            if (rect.Width + (2f * spread) <= 0f || rect.Height + (2f * spread) <= 0f)
            {
                bounds = RectF.Empty;
            }
        }

        Begin(ItemKind.BoxShadow, id, bounds, inset ? FlagInset : 0, 20);
        PutRect(rect);
        PutRadii(r);
        PutColor(ClampColor(color));
        PutF(offsetX);
        PutF(offsetY);
        PutF(blur);
        PutF(spread);
        H(inset ? 1UL : 0UL);
        Finish(_count - 1);
    }

    /// <summary>Draws part of a host-resolved image into a destination rectangle, scaled to fit it exactly.</summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="image">The host's handle for the image.</param>
    /// <param name="source">The part of the image to draw, in the image's own pixels; an empty rectangle means the whole image.</param>
    /// <param name="destination">Where to draw it.</param>
    /// <param name="sampling">How to filter when scaling.</param>
    public void DrawImage(ItemId id, ImageHandle image, in RectF source, in RectF destination, ImageSampling sampling = ImageSampling.Smooth)
    {
        CheckRect(source);
        CheckRect(destination);
        Begin(ItemKind.Image, id, destination, (int)sampling, 8);
        PutRect(source);
        PutRect(destination);
        _items[_count - 1].A = image.Value;
        H((ulong)(uint)image.Value);
        H((ulong)sampling);
        Finish(_count - 1);
    }

    /// <summary>
    /// Draws an image repeated over an area, as <c>background-repeat</c> does (format 1.1). The cell at <paramref name="tile"/> is one copy of the
    /// image; copies repeat every <c>tile.Width + gapX</c> horizontally and <c>tile.Height + gapY</c> vertically in both directions, and only
    /// the part inside <paramref name="area"/> is drawn. A gap is empty space (<c>background-repeat: space</c>).
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="image">The host's handle for the image.</param>
    /// <param name="source">The part of the image to draw, in the image's pixels; empty for the whole image.</param>
    /// <param name="tile">Where one copy is drawn; it need not lie inside the area.</param>
    /// <param name="area">The rectangle the repetition is limited to.</param>
    /// <param name="gapX">Space between copies horizontally, 0 or more.</param>
    /// <param name="gapY">Space between copies vertically, 0 or more.</param>
    /// <param name="sampling">How to filter when scaling.</param>
    public void DrawImageTiled(ItemId id, ImageHandle image, in RectF source, in RectF tile, in RectF area, float gapX, float gapY, ImageSampling sampling = ImageSampling.Smooth)
    {
        CheckRect(source);
        CheckRect(tile);
        CheckRect(area);
        if (!(gapX >= 0) || !(gapY >= 0) || gapX > 1e9f || gapY > 1e9f)
        {
            throw new ArgumentOutOfRangeException(nameof(gapX));
        }

        Begin(ItemKind.Image, id, area, (int)sampling | FlagTiled, 14);
        PutRect(source);
        PutRect(tile);
        PutRect(area);
        PutF(gapX);
        PutF(gapY);
        _items[_count - 1].A = image.Value;
        _requiredMinor = Math.Max(_requiredMinor, 1);
        H((ulong)(uint)image.Value);
        H((ulong)sampling | 4UL);
        Finish(_count - 1);
    }

    // ---------------------------------------------------------------- glyph runs

    /// <summary>
    /// Draws a run of glyphs of one face. Glyph i is drawn with its origin (the pen position on the baseline) at
    /// <c>(originX + x[i], originY + y[i])</c>. The glyph outline is the face's outline scaled by <c>fontSize / UnitsPerEm</c>, with y up
    /// in font units and y down on screen.
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="face">The OneFont face; the list keeps a reference until it is cleared.</param>
    /// <param name="fontSize">Font size in CSS pixels, greater than 0.</param>
    /// <param name="originX">X of the run's origin.</param>
    /// <param name="originY">Y of the run's baseline origin.</param>
    /// <param name="color">Text color.</param>
    /// <param name="glyphIds">Glyph ids in the face, in visual (left to right on screen) order.</param>
    /// <param name="x">Per-glyph x of the pen position, relative to the origin; same length as <paramref name="glyphIds"/>.</param>
    /// <param name="y">Per-glyph y offset from the baseline; either empty (all zero) or the same length as <paramref name="glyphIds"/>.</param>
    /// <param name="clusters">
    /// Per-glyph index into <paramref name="text"/> of the first UTF-16 unit of the glyph's cluster; same length as
    /// <paramref name="glyphIds"/>. Glyphs of one cluster share a value. This is what a PDF writer needs for
    /// <c>/ToUnicode</c> and <c>/ActualText</c>.
    /// </param>
    /// <param name="text">The source text of the run, in logical order.</param>
    /// <param name="rightToLeft">True when the run's text direction is right to left, so cluster values decrease along the glyphs.</param>
    /// <param name="blur">
    /// Format 1.1: when above 0 the glyphs are blurred with a Gaussian of standard deviation <c>blur / 2</c> before they are painted, as
    /// a <c>text-shadow</c> needs. 0 draws sharp glyphs, as in format 1.0.
    /// </param>
    public void DrawGlyphRun(
        ItemId id,
        FontFace face,
        float fontSize,
        float originX,
        float originY,
        in PaintColor color,
        ReadOnlySpan<int> glyphIds,
        ReadOnlySpan<float> x,
        ReadOnlySpan<float> y,
        ReadOnlySpan<int> clusters,
        ReadOnlySpan<char> text,
        bool rightToLeft = false,
        float blur = 0f)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (!(blur >= 0f) || blur > 1e6f)
        {
            throw new ArgumentOutOfRangeException(nameof(blur));
        }

        Coord(originX);
        Coord(originY);
        Coord(fontSize);
        if (!(fontSize > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), "Font size must be greater than zero.");
        }

        if (x.Length != glyphIds.Length || clusters.Length != glyphIds.Length || (y.Length != 0 && y.Length != glyphIds.Length))
        {
            throw new ArgumentException("Glyph arrays must have the same length.");
        }

        if (_glyphCount + glyphIds.Length > DisplayListLimits.MaxGlyphs || _textCount + text.Length > DisplayListLimits.MaxTextChars)
        {
            throw new DisplayListLimitException("Too many glyphs or text characters in one list.");
        }

        for (int i = 0; i < glyphIds.Length; i++)
        {
            Coord(x[i]);
            if (y.Length != 0)
            {
                Coord(y[i]);
            }

            if ((uint)clusters[i] > (uint)text.Length || glyphIds[i] < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(clusters), "A cluster index or glyph id is out of range.");
            }
        }

        _ = ClampColor(color);
        int faceIndex = FaceIndex(face);
        RectF bounds = GlyphBounds(face, fontSize, originX, originY, glyphIds, x, y);
        if (blur > 0f)
        {
            bounds = bounds.Inflate((1.5f * blur) + 1f);
            _requiredMinor = Math.Max(_requiredMinor, 1);
        }

        Begin(ItemKind.GlyphRun, id, bounds, rightToLeft ? FlagRtl : 0, 8);
        int idx = _count - 1;
        PutF(originX);
        PutF(originY);
        PutF(fontSize);
        PutColor(ClampColor(color));
        PutF(blur);
        H(Bits(blur));
        ref ItemRecord rec = ref _items[idx];
        rec.A = faceIndex;
        rec.B = _glyphCount;
        rec.C = glyphIds.Length;
        rec.D = _textCount;
        rec.E = text.Length;
        Grow(ref _glyphIds, _glyphCount + glyphIds.Length);
        Grow(ref _glyphX, _glyphCount + glyphIds.Length);
        Grow(ref _glyphY, _glyphCount + glyphIds.Length);
        Grow(ref _glyphClusters, _glyphCount + glyphIds.Length);
        Grow(ref _text, _textCount + text.Length);
        H(_faceHashes[faceIndex]);
        H(rightToLeft ? 1UL : 0UL);
        H((ulong)glyphIds.Length);
        for (int i = 0; i < glyphIds.Length; i++)
        {
            int g = _glyphCount + i;
            _glyphIds[g] = glyphIds[i];
            _glyphX[g] = x[i];
            _glyphY[g] = y.Length == 0 ? 0f : y[i];
            _glyphClusters[g] = clusters[i];
            H((ulong)(uint)glyphIds[i]);
            H(Bits(x[i]));
            H(Bits(_glyphY[g]));
            H((ulong)(uint)clusters[i]);
        }

        text.CopyTo(_text.AsSpan(_textCount, text.Length));
        H((ulong)text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            H(text[i]);
        }

        _glyphCount += glyphIds.Length;
        _textCount += text.Length;
        Finish(idx);
    }

    private static RectF GlyphBounds(FontFace face, float size, float ox, float oy, ReadOnlySpan<int> ids, ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        if (ids.Length == 0)
        {
            return RectF.Empty;
        }

        float upem = Math.Max(1, face.UnitsPerEm);
        float scale = size / upem;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < ids.Length; i++)
        {
            float gx = x[i];
            float gy = y.Length == 0 ? 0f : y[i];
            float adv = face.GetAdvance(ids[i]) * scale;
            minX = Math.Min(minX, gx);
            maxX = Math.Max(maxX, gx + Math.Max(adv, 0f));
            minY = Math.Min(minY, gy);
            maxY = Math.Max(maxY, gy);
        }

        // Conservative ink box: the face's ascent and descent (at least one em above and half an em below, since accents and
        // descenders may exceed the nominal metrics) plus a quarter em of side overhang for italics and wide marks.
        FontMetrics m = face.Metrics;
        float up = Math.Max(m.Ascender, upem) * scale;
        float down = Math.Max(-m.Descender, upem * 0.5f) * scale;
        float side = size * 0.25f;
        float left = ox + minX - side;
        float top = oy + minY - up;
        return new RectF(left, top, (ox + maxX + side) - left, (oy + maxY + down) - top);
    }

    private int FaceIndex(FontFace face)
    {
        for (int i = _faceCount - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_faces[i], face))
            {
                return i;
            }
        }

        if (_faceCount >= DisplayListLimits.MaxFaces)
        {
            throw new DisplayListLimitException("More than " + DisplayListLimits.MaxFaces + " faces in one list.");
        }

        Grow(ref _faces, _faceCount + 1);
        Grow(ref _faceHashes, _faceCount + 1);
        ulong h = 0x66616365UL;
        foreach (char c in face.FamilyName)
        {
            h = Mix(h, c);
        }

        foreach (char c in face.SubfamilyName)
        {
            h = Mix(h, c);
        }

        h = Mix(h, (ulong)face.UnitsPerEm);
        h = Mix(h, (ulong)face.GlyphCount);
        _faces[_faceCount] = face;
        _faceHashes[_faceCount] = h;
        return _faceCount++;
    }

    // ---------------------------------------------------------------- clips, transforms, groups, tags

    /// <summary>Clips everything until the matching <see cref="PopClip"/> to a rectangle (intersected with any outer clip).</summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="rect">The clip rectangle in the current (transformed) space.</param>
    public void PushClipRect(ItemId id, in RectF rect)
    {
        CheckRect(rect);
        Begin(ItemKind.PushClipRect, id, rect, 0, 4);
        PutRect(rect);
        FinishPush(_count - 1);
    }

    /// <summary>Clips to a rounded rectangle; the radii are constrained when stored.</summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="rect">The clip rectangle.</param>
    /// <param name="radii">Corner radii.</param>
    public void PushClipRoundedRect(ItemId id, in RectF rect, in CornerRadii radii)
    {
        CheckRect(rect);
        CornerRadii r = ConstrainRadii(rect, radii);
        Begin(ItemKind.PushClipRoundedRect, id, rect, 0, 12);
        PutRect(rect);
        PutRadii(r);
        FinishPush(_count - 1);
    }

    /// <summary>Clips to a path.</summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="path">The clip path; copied.</param>
    /// <param name="rule">The inside rule.</param>
    public void PushClipPath(ItemId id, PathBuffer path, FillRule rule = FillRule.NonZero)
    {
        ArgumentNullException.ThrowIfNull(path);
        PushClipPath(id, path.Verbs, path.Coords, rule);
    }

    /// <summary>Clips to a path given as verbs and coordinates.</summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="verbs"><see cref="PathVerb"/> values.</param>
    /// <param name="coords">Coordinates for the verbs.</param>
    /// <param name="rule">The inside rule.</param>
    public void PushClipPath(ItemId id, ReadOnlySpan<byte> verbs, ReadOnlySpan<float> coords, FillRule rule = FillRule.NonZero)
    {
        RectF bounds = CheckPath(verbs, coords);
        ReservePath(verbs.Length, coords.Length);
        Begin(ItemKind.PushClipPath, id, bounds, rule == FillRule.EvenOdd ? FlagEvenOdd : 0, 0);
        int pathIndex = StorePath(verbs, coords);
        _items[_count - 1].C = pathIndex;
        H(rule == FillRule.EvenOdd ? 1UL : 0UL);
        FinishPush(_count - 1);
    }

    /// <summary>Ends the innermost clip.</summary>
    /// <exception cref="DisplayListException">The innermost open push is not a clip.</exception>
    public void PopClip() => Pop(ItemKind.PopClip, ItemKind.PushClipRect);

    /// <summary>
    /// Applies a 2D affine transform to everything until the matching <see cref="PopTransform"/>, composed after any outer
    /// transform. A point maps as <c>x' = x*M11 + y*M21 + M31</c>, <c>y' = x*M12 + y*M22 + M32</c>.
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="matrix">The transform, in CSS pixels. <c>transform-origin</c> is already folded in by the painter.</param>
    public void PushTransform(ItemId id, in Matrix3x2 matrix)
    {
        CheckMatrix(matrix);
        Begin(ItemKind.PushTransform, id, RectF.Empty, 0, 6);
        PutMatrix(matrix);
        FinishPush(_count - 1);
    }

    /// <summary>
    /// Pushes a 3D transform. A matrix with no perspective (<c>M14</c> and <c>M24</c> zero) maps the z = 0 plane affinely, so it is stored
    /// as an ordinary 2D transform: the 2D part of the matrix divided by <c>M44</c>. A matrix with perspective is stored whole
    /// (<see cref="ItemKind.PushTransform3D"/>, format 1.2): the content is drawn flat, then projected with
    /// <c>x' = (x*M11 + y*M21 + M41) / w</c>, <c>y' = (x*M12 + y*M22 + M42) / w</c>, <c>w = x*M14 + y*M24 + M44</c>.
    /// Inside another projection the perspective is dropped (the list does not nest projections).
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="matrix">The 3D transform in CSS pixels (row-vector convention, as <see cref="Matrix4x4"/>).</param>
    public void PushTransform3D(ItemId id, in Matrix4x4 matrix)
    {
        float w = matrix.M44;
        if ((matrix.M14 == 0f && matrix.M24 == 0f) || InsideProjection())
        {
            if (!float.IsFinite(w) || w <= 0f)
            {
                throw new ArgumentException("A matrix without perspective needs a finite, positive M44.", nameof(matrix));
            }

            PushTransform(id, new Matrix3x2(matrix.M11 / w, matrix.M12 / w, matrix.M21 / w, matrix.M22 / w, matrix.M41 / w, matrix.M42 / w));
            return;
        }

        PushTransform3DCore(id, matrix);
    }

    /// <summary>Ends the innermost transform.</summary>
    /// <exception cref="DisplayListException">The innermost open push is not a transform.</exception>
    public void PopTransform() => Pop(ItemKind.PopTransform, ItemKind.PushTransform);

    /// <summary>
    /// Starts an isolated group (Compositing and Blending 1 §3 and §4): everything until the matching <see cref="PopGroup"/> is
    /// painted into a transparent layer, which is then composited onto what is behind it with the opacity and blend mode.
    /// Use it for <c>opacity</c> below 1, <c>mix-blend-mode</c>, and <c>isolation: isolate</c> (opacity 1, normal).
    /// </summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="opacity">0 (invisible) to 1 (opaque).</param>
    /// <param name="blend">How the finished layer blends with the backdrop.</param>
    public void PushGroup(ItemId id, float opacity, BlendMode blend = BlendMode.Normal)
    {
        CheckOpacity(opacity);
        if ((int)blend > (int)BlendMode.PlusLighter)
        {
            throw new ArgumentOutOfRangeException(nameof(blend));
        }

        Begin(ItemKind.PushGroup, id, RectF.Empty, 0, 1);
        PutF(opacity);
        _items[_count - 1].A = (int)blend;
        H((ulong)blend);
        FinishPush(_count - 1);
    }

    /// <summary>Ends the innermost group and composites it.</summary>
    /// <exception cref="DisplayListException">The innermost open push is not a group.</exception>
    public void PopGroup() => Pop(ItemKind.PopGroup, ItemKind.PushGroup);

    /// <summary>Starts a semantic element: everything painted until <see cref="EndTag"/> belongs to it.</summary>
    /// <param name="id">The item's stable id.</param>
    /// <param name="role">The element's role.</param>
    /// <param name="altText">Alternative text (<c>alt</c>, <c>aria-label</c>), or null.</param>
    /// <param name="linkTarget">The destination of a <see cref="TagRole.Link"/>: a URL or a <c>#fragment</c>; null otherwise.</param>
    /// <param name="language">A BCP 47 language tag when it differs from the parent's, or null.</param>
    public void BeginTag(ItemId id, TagRole role, string? altText = null, string? linkTarget = null, string? language = null)
    {
        if ((int)role > (int)TagRole.Form)
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        int needed = (altText is null ? 0 : 1) + (linkTarget is null ? 0 : 1) + (language is null ? 0 : 1);
        if (_stringCount + needed > DisplayListLimits.MaxStrings)
        {
            throw new DisplayListLimitException("Too many strings in one list.");
        }

        Begin(ItemKind.BeginTag, id, RectF.Empty, 0, 0);
        ref ItemRecord rec = ref _items[_count - 1];
        rec.A = (int)role;
        H((ulong)role);
        rec.B = StoreString(altText);
        rec.C = StoreString(linkTarget);
        rec.D = StoreString(language);
        FinishPush(_count - 1);
    }

    /// <summary>Ends the innermost semantic element.</summary>
    /// <exception cref="DisplayListException">The innermost open push is not a tag.</exception>
    public void EndTag() => Pop(ItemKind.EndTag, ItemKind.BeginTag);

    private int StoreString(string? s)
    {
        H(0x73UL);
        if (s is null)
        {
            return 0;
        }

        foreach (char c in s)
        {
            H(c);
        }

        H((ulong)s.Length);
        Grow(ref _strings, _stringCount + 1);
        _strings[_stringCount++] = s;
        return _stringCount;
    }

    // ---------------------------------------------------------------- validation helpers

    private static void Coord(float v)
    {
        if (!float.IsFinite(v) || Math.Abs(v) > 1e9f)
        {
            throw new ArgumentOutOfRangeException(nameof(v), "Coordinates must be finite and within ±1e9.");
        }
    }

    private static void CheckRect(in RectF r)
    {
        Coord(r.X);
        Coord(r.Y);
        Coord(r.Width);
        Coord(r.Height);
    }

    private static void CheckMatrix(in Matrix3x2 m)
    {
        Coord(m.M11);
        Coord(m.M12);
        Coord(m.M21);
        Coord(m.M22);
        Coord(m.M31);
        Coord(m.M32);
    }

    private static void CheckOpacity(float opacity)
    {
        if (!(opacity >= 0f && opacity <= 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), "Opacity must be between 0 and 1.");
        }
    }

    private static void CheckSide(in BorderSide s)
    {
        Coord(s.Width);
        _ = ClampColor(s.Color);
        if (s.Width < 0f || (int)s.Style > (int)BorderStyle.Outset)
        {
            throw new ArgumentOutOfRangeException(nameof(s), "Border width must not be negative and the style must be defined.");
        }
    }

    private void CheckPaint(in Paint p)
    {
        if (p.Kind == PaintKind.Solid)
        {
            _ = ClampColor(p.Color);
            return;
        }

        if ((uint)p.Gradient >= (uint)_gradientCount)
        {
            throw new ArgumentException("The gradient paint does not belong to this list.", nameof(p));
        }
    }

    private static CornerRadii ConstrainRadii(in RectF rect, in CornerRadii radii)
    {
        Coord(radii.TopLeftX);
        Coord(radii.TopLeftY);
        Coord(radii.TopRightX);
        Coord(radii.TopRightY);
        Coord(radii.BottomRightX);
        Coord(radii.BottomRightY);
        Coord(radii.BottomLeftX);
        Coord(radii.BottomLeftY);
        return radii.Constrained(rect.Width, rect.Height);
    }

    private static PaintColor ClampColor(in PaintColor c)
    {
        if (!float.IsFinite(c.R) || !float.IsFinite(c.G) || !float.IsFinite(c.B) || !float.IsFinite(c.A))
        {
            throw new ArgumentOutOfRangeException(nameof(c), "Color channels must be finite.");
        }

        return new PaintColor(Math.Clamp(c.R, 0f, 1f), Math.Clamp(c.G, 0f, 1f), Math.Clamp(c.B, 0f, 1f), Math.Clamp(c.A, 0f, 1f));
    }

    private static RectF CheckPath(ReadOnlySpan<byte> verbs, ReadOnlySpan<float> coords)
    {
        if (verbs.Length > DisplayListLimits.MaxPathVerbsPerPath)
        {
            throw new DisplayListLimitException("A path has more than " + DisplayListLimits.MaxPathVerbsPerPath + " segments.");
        }

        int need = 0;
        for (int i = 0; i < verbs.Length; i++)
        {
            switch ((PathVerb)verbs[i])
            {
                case PathVerb.Move:
                case PathVerb.Line:
                    need += 2;
                    break;
                case PathVerb.Quad:
                    need += 4;
                    break;
                case PathVerb.Cubic:
                    need += 6;
                    break;
                case PathVerb.Close:
                    break;
                default:
                    throw new ArgumentException("Unknown path verb " + verbs[i] + ".", nameof(verbs));
            }
        }

        if (need != coords.Length)
        {
            throw new ArgumentException("The coordinate count does not match the verbs.", nameof(coords));
        }

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < coords.Length; i += 2)
        {
            Coord(coords[i]);
            Coord(coords[i + 1]);
            minX = Math.Min(minX, coords[i]);
            maxX = Math.Max(maxX, coords[i]);
            minY = Math.Min(minY, coords[i + 1]);
            maxY = Math.Max(maxY, coords[i + 1]);
        }

        return coords.Length == 0 ? RectF.Empty : new RectF(minX, minY, maxX - minX, maxY - minY);
    }

    private void ReservePath(int verbs, int coords)
    {
        if (_verbCount + verbs > DisplayListLimits.MaxPathVerbs || _coordCount + coords > DisplayListLimits.MaxPathVerbs * 6)
        {
            throw new DisplayListLimitException("Too many path segments in one list.");
        }
    }

    private int StorePath(ReadOnlySpan<byte> verbs, ReadOnlySpan<float> coords)
    {
        Grow(ref _verbs, _verbCount + verbs.Length);
        Grow(ref _coords, _coordCount + coords.Length);
        Grow(ref _paths, (_pathCount + 1) * 4);
        int p = _pathCount * 4;
        _paths[p] = _verbCount;
        _paths[p + 1] = verbs.Length;
        _paths[p + 2] = _coordCount;
        _paths[p + 3] = coords.Length;
        verbs.CopyTo(_verbs.AsSpan(_verbCount));
        coords.CopyTo(_coords.AsSpan(_coordCount));
        H((ulong)verbs.Length);
        for (int i = 0; i < verbs.Length; i++)
        {
            H(verbs[i]);
        }

        for (int i = 0; i < coords.Length; i++)
        {
            H(Bits(coords[i]));
        }

        _verbCount += verbs.Length;
        _coordCount += coords.Length;
        return _pathCount++;
    }

    // ---------------------------------------------------------------- storage plumbing

    private static void Grow<T>(ref T[] array, int needed)
    {
        if (needed <= array.Length)
        {
            return;
        }

        int size = array.Length;
        while (size < needed)
        {
            size = size > (int.MaxValue / 2) ? needed : size * 2;
        }

        Array.Resize(ref array, size);
    }

    private static ulong Bits(float v) => BitConverter.SingleToUInt32Bits(v == 0f ? 0f : v);

    private static ulong Mix(ulong h, ulong v)
    {
        unchecked
        {
            h ^= v + 0x9E3779B97F4A7C15UL + (h << 6) + (h >> 2);
            h *= 0xFF51AFD7ED558CCDUL;
            return h ^ (h >> 33);
        }
    }

    private void H(ulong v) => _hash = Mix(_hash, v);

    private void PutF(float v)
    {
        _num[_numCount++] = v;
        H(Bits(v));
    }

    private void PutRect(in RectF r)
    {
        PutF(r.X);
        PutF(r.Y);
        PutF(r.Width);
        PutF(r.Height);
    }

    private void PutColor(in PaintColor c)
    {
        PutF(c.R);
        PutF(c.G);
        PutF(c.B);
        PutF(c.A);
    }

    private void PutMatrix(in Matrix3x2 m)
    {
        PutF(m.M11);
        PutF(m.M12);
        PutF(m.M21);
        PutF(m.M22);
        PutF(m.M31);
        PutF(m.M32);
    }

    private void PutRadii(in CornerRadii r)
    {
        PutF(r.TopLeftX);
        PutF(r.TopLeftY);
        PutF(r.TopRightX);
        PutF(r.TopRightY);
        PutF(r.BottomRightX);
        PutF(r.BottomRightY);
        PutF(r.BottomLeftX);
        PutF(r.BottomLeftY);
    }

    private void PutPaint(in Paint p)
    {
        ref ItemRecord rec = ref _items[_count - 1];
        rec.A = (int)p.Kind;
        rec.B = p.Gradient;
        H((ulong)p.Kind);
        if (p.Kind == PaintKind.Solid)
        {
            PutColor(ClampColor(p.Color));
        }
        else
        {
            PutColor(default);
            H(_gradients[p.Gradient].Hash);
        }
    }

    internal struct ItemRecord
    {
        public ItemKind Kind;
        public int Flags;
        public ulong Id;
        public ulong ContentHash;
        public ulong CtxHash;
        public RectF Local;
        public RectF Device;
        public RectF Rel;
        public int Num;
        public int A;
        public int B;
        public int C;
        public int D;
        public int E;
        public int Parent;
        public int Match;
        public int State;
    }

    internal struct PushState
    {
        public Matrix3x2 Ctm;
        public RectF Clip;
        public ulong Ctx;
        public Matrix4x4 Outer;
        public bool Has3D;
        public float Expand;
    }

    internal struct GradientRecord
    {
        public PaintKind Kind;
        public bool Repeating;
        public float G0;
        public float G1;
        public float G2;
        public float G3;
        public float G4;
        public int StopStart;
        public int StopCount;
        public ulong Hash;
    }
}
