using System.Numerics;
using OneFont;

namespace OneRender.Painting;

/// <summary>A gradient created by <see cref="DisplayList.LinearGradient"/>, <see cref="DisplayList.RadialGradient"/> or <see cref="DisplayList.ConicGradient"/>, read back.</summary>
public readonly struct GradientView
{
    private readonly DisplayList _list;
    private readonly int _index;

    internal GradientView(DisplayList list, int index)
    {
        _list = list;
        _index = index;
    }

    private ref readonly DisplayList.GradientRecord Rec => ref _list._gradients[_index];

    /// <summary>Linear, radial or conic.</summary>
    public PaintKind Kind => Rec.Kind;

    /// <summary>True for the <c>repeating-*</c> forms.</summary>
    public bool Repeating => Rec.Repeating;

    /// <summary>Linear: start x. Radial and conic: center x.</summary>
    public float X0 => Rec.G0;

    /// <summary>Linear: start y. Radial and conic: center y.</summary>
    public float Y0 => Rec.G1;

    /// <summary>Linear: end x. Radial: horizontal radius. Conic: unused (0).</summary>
    public float X1 => Rec.G2;

    /// <summary>Linear: end y. Radial: vertical radius. Conic: unused (0).</summary>
    public float Y1 => Rec.G3;

    /// <summary>Conic: the start angle in radians, clockwise from up. Otherwise 0.</summary>
    public float FromAngle => Rec.G4;

    /// <summary>Number of color stops, at least 1.</summary>
    public int StopCount => Rec.StopCount;

    /// <summary>The stop at <paramref name="index"/>; offsets never decrease along the stops.</summary>
    /// <param name="index">0 to <see cref="StopCount"/> - 1.</param>
    /// <returns>The stop.</returns>
    public GradientStop GetStop(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Rec.StopCount);
        int p = Rec.StopStart + (index * 5);
        float[] s = _list._stops;
        return new GradientStop(s[p], new PaintColor(s[p + 1], s[p + 2], s[p + 3], s[p + 4]));
    }
}

/// <summary>A glyph run read back from a list.</summary>
public readonly struct GlyphRunView
{
    private readonly DisplayList _list;
    private readonly int _index;

    internal GlyphRunView(DisplayList list, int index)
    {
        _list = list;
        _index = index;
    }

    private ref readonly DisplayList.ItemRecord Rec => ref _list._items[_index];

    /// <summary>The OneFont face the glyph ids belong to.</summary>
    public FontFace Face => _list._faces[Rec.A];

    /// <summary>Index of the face in the list's face table (stable within one list).</summary>
    public int FaceIndex => Rec.A;

    /// <summary>
    /// A key of the face that is the same in every list, frame and process for the same font file (format 1.3), so a host glyph atlas can
    /// key on (face key, size × device-pixel ratio, glyph id). <see cref="FaceIndex"/> is only an index into one list's table.
    /// </summary>
    public ulong FaceKey => _list._faceHashes[Rec.A];

    /// <summary>Font size in CSS pixels.</summary>
    public float FontSize => _list._num[Rec.Num + 2];

    /// <summary>X of the run origin.</summary>
    public float OriginX => _list._num[Rec.Num];

    /// <summary>Y of the run origin (the baseline).</summary>
    public float OriginY => _list._num[Rec.Num + 1];

    /// <summary>Text color.</summary>
    public PaintColor Color
    {
        get
        {
            float[] n = _list._num;
            int p = Rec.Num + 3;
            return new PaintColor(n[p], n[p + 1], n[p + 2], n[p + 3]);
        }
    }

    /// <summary>Blur of the run (format 1.1): the glyphs are blurred with a Gaussian of standard deviation <c>Blur / 2</c>; 0 for sharp glyphs.</summary>
    public float Blur => _list._num[Rec.Num + 7];

    /// <summary>True when the run's direction is right to left.</summary>
    public bool RightToLeft => (Rec.Flags & 1) != 0;

    /// <summary>Glyph ids in the face, in visual order.</summary>
    public ReadOnlySpan<int> GlyphIds => new(_list._glyphIds, Rec.B, Rec.C);

    /// <summary>Per-glyph pen x relative to the origin.</summary>
    public ReadOnlySpan<float> X => new(_list._glyphX, Rec.B, Rec.C);

    /// <summary>Per-glyph y offset from the baseline.</summary>
    public ReadOnlySpan<float> Y => new(_list._glyphY, Rec.B, Rec.C);

    /// <summary>Per-glyph index into <see cref="Text"/> of the first UTF-16 unit of the glyph's cluster.</summary>
    public ReadOnlySpan<int> Clusters => new(_list._glyphClusters, Rec.B, Rec.C);

    /// <summary>The source text, in logical order.</summary>
    public ReadOnlySpan<char> Text => new(_list._text, Rec.D, Rec.E);
}

/// <summary>
/// A read-only view of one item of a <see cref="DisplayList"/>. Properties documented for a kind return default values for
/// other kinds. A view is valid until the list is cleared, patched or refilled.
/// </summary>
public readonly partial struct DisplayItem
{
    private readonly DisplayList _list;
    private readonly int _index;

    internal DisplayItem(DisplayList list, int index)
    {
        _list = list;
        _index = index;
    }

    private ref readonly DisplayList.ItemRecord Rec => ref _list._items[_index];

    private ReadOnlySpan<float> Num => new(_list._num, Rec.Num, _list._numCount - Rec.Num);

    /// <summary>Position in paint order.</summary>
    public int Index => _index;

    /// <summary>What the item is.</summary>
    public ItemKind Kind => Rec.Kind;

    /// <summary>The painter's id; <see cref="ItemId.None"/> for pop items, which have none.</summary>
    public ItemId Id => new(Rec.Id);

    /// <summary>
    /// The area the item can touch, in the space it is painted in (inside any enclosing transform, before clipping).
    /// Empty for items without pixels of their own (pushes, pops, tags).
    /// </summary>
    public RectF LocalBounds => Rec.Local;

    /// <summary>
    /// <see cref="LocalBounds"/> mapped through all enclosing transforms and intersected with all enclosing clips, in root space.
    /// Empty when the item is fully clipped away. This is the rectangle damage is made of.
    /// </summary>
    public RectF RootBounds => Rec.Device;

    /// <summary>
    /// A 64-bit hash of everything that decides what this item paints by itself (its kind and every field documented for it,
    /// including the content of paths, gradients, glyphs and text). Equal hashes on one id mean the item is unchanged
    /// (up to a 2^-64 collision chance), so an adapter can keep a GPU resource per id and rebuild it only when this changes.
    /// The value is stable within one process run; it is not a file format.
    /// </summary>
    public ulong ContentHash => Rec.ContentHash;

    /// <summary>
    /// A 64-bit hash of the enclosing transforms, clips and groups of the item (their kinds and parameters). A cached
    /// resource for the item is valid while <see cref="ContentHash"/> and this value are both unchanged and, for anything that
    /// depends on position, <see cref="RootBounds"/> is unchanged.
    /// </summary>
    public ulong ContextHash => Rec.CtxHash;

    /// <summary>Index of the innermost enclosing push item, or -1 at top level.</summary>
    public int ParentIndex => Rec.Parent;

    /// <summary>For a push item the index of its pop; for a pop item the index of its push; otherwise -1.</summary>
    public int MatchIndex => Rec.Match;

    /// <summary>
    /// The rectangle of <see cref="ItemKind.FillRect"/>, <see cref="ItemKind.FillRoundedRect"/>, <see cref="ItemKind.Border"/>
    /// (border box), <see cref="ItemKind.BoxShadow"/> (border or padding box), <see cref="ItemKind.PushClipRect"/> and
    /// <see cref="ItemKind.PushClipRoundedRect"/>.
    /// </summary>
    public RectF Rect => HasRect(Rec.Kind) ? ReadRect(0) : default;

    /// <summary>The corner radii of rounded fills, borders, shadows and rounded clips (already constrained to the box).</summary>
    public CornerRadii Radii => HasRadii(Rec.Kind) ? ReadRadii(4) : default;

    /// <summary>The paint of fill items.</summary>
    public Paint Paint
    {
        get
        {
            ItemKind k = Rec.Kind;
            if (k != ItemKind.FillRect && k != ItemKind.FillRoundedRect && k != ItemKind.FillPath)
            {
                return default;
            }

            PaintKind pk = (PaintKind)Rec.A;
            int colorAt = k == ItemKind.FillRect ? 4 : k == ItemKind.FillRoundedRect ? 12 : 0;
            ReadOnlySpan<float> n = Num;
            PaintColor c = pk == PaintKind.Solid ? new PaintColor(n[colorAt], n[colorAt + 1], n[colorAt + 2], n[colorAt + 3]) : default;
            return new Paint(pk, c, Rec.B);
        }
    }

    /// <summary>The gradient of a fill item whose <see cref="Paint"/> is not solid.</summary>
    public GradientView Gradient => new(_list, Rec.B);

    /// <summary>The inside rule of <see cref="ItemKind.FillPath"/> and <see cref="ItemKind.PushClipPath"/>.</summary>
    public FillRule FillRule => (Rec.Flags & 1) != 0 ? FillRule.EvenOdd : FillRule.NonZero;

    /// <summary>Verbs of <see cref="ItemKind.FillPath"/> and <see cref="ItemKind.PushClipPath"/> as <see cref="PathVerb"/> values.</summary>
    public ReadOnlySpan<byte> PathVerbs => HasPath(Rec.Kind) ? new(_list._verbs, _list._paths[Rec.C * 4], _list._paths[(Rec.C * 4) + 1]) : default;

    /// <summary>Coordinates of the path, in verb order.</summary>
    public ReadOnlySpan<float> PathCoords => HasPath(Rec.Kind) ? new(_list._coords, _list._paths[(Rec.C * 4) + 2], _list._paths[(Rec.C * 4) + 3]) : default;

    /// <summary>The top side of a <see cref="ItemKind.Border"/>.</summary>
    public BorderSide BorderTop => Side(0);

    /// <summary>The right side of a border.</summary>
    public BorderSide BorderRight => Side(1);

    /// <summary>The bottom side of a border.</summary>
    public BorderSide BorderBottom => Side(2);

    /// <summary>The left side of a border.</summary>
    public BorderSide BorderLeft => Side(3);

    /// <summary>Shadow color of a <see cref="ItemKind.BoxShadow"/>.</summary>
    public PaintColor ShadowColor => Rec.Kind == ItemKind.BoxShadow ? ReadColor(12) : default;

    /// <summary>Shadow horizontal offset.</summary>
    public float ShadowOffsetX => Rec.Kind == ItemKind.BoxShadow ? Num[16] : 0f;

    /// <summary>Shadow vertical offset.</summary>
    public float ShadowOffsetY => Rec.Kind == ItemKind.BoxShadow ? Num[17] : 0f;

    /// <summary>Shadow blur radius (the Gaussian's standard deviation is half of it).</summary>
    public float ShadowBlur => Rec.Kind == ItemKind.BoxShadow ? Num[18] : 0f;

    /// <summary>Shadow spread distance.</summary>
    public float ShadowSpread => Rec.Kind == ItemKind.BoxShadow ? Num[19] : 0f;

    /// <summary>True for an inset shadow.</summary>
    public bool ShadowInset => Rec.Kind == ItemKind.BoxShadow && (Rec.Flags & 1) != 0;

    /// <summary>The host's handle of an <see cref="ItemKind.Image"/> item.</summary>
    public ImageHandle Image => Rec.Kind == ItemKind.Image ? new ImageHandle(Rec.A) : default;

    /// <summary>The source rectangle of an image item in image pixels; empty means the whole image.</summary>
    public RectF ImageSource => Rec.Kind == ItemKind.Image ? ReadRect(0) : default;

    /// <summary>The destination rectangle of an image item.</summary>
    public RectF ImageDestination => Rec.Kind == ItemKind.Image ? ReadRect(4) : default;

    /// <summary>The sampling of an image item.</summary>
    public ImageSampling Sampling => Rec.Kind == ItemKind.Image ? (ImageSampling)(Rec.Flags & 1) : ImageSampling.Smooth;

    /// <summary>True for an <see cref="ItemKind.Image"/> item made by <c>DrawImageTiled</c>: <see cref="ImageDestination"/> is then one tile.</summary>
    public bool IsTiledImage => Rec.Kind == ItemKind.Image && (Rec.Flags & 2) != 0;

    /// <summary>The area a tiled image is limited to.</summary>
    public RectF ImageTileArea => IsTiledImage ? ReadRect(8) : default;

    /// <summary>Horizontal space between the copies of a tiled image.</summary>
    public float ImageTileGapX => IsTiledImage ? Num[12] : 0f;

    /// <summary>Vertical space between the copies of a tiled image.</summary>
    public float ImageTileGapY => IsTiledImage ? Num[13] : 0f;

    /// <summary>The glyph run of a <see cref="ItemKind.GlyphRun"/> item.</summary>
    public GlyphRunView GlyphRun => new(_list, _index);

    /// <summary>The matrix of a <see cref="ItemKind.PushTransform"/>.</summary>
    public Matrix3x2 Matrix
    {
        get
        {
            if (Rec.Kind != ItemKind.PushTransform)
            {
                return Matrix3x2.Identity;
            }

            ReadOnlySpan<float> n = Num;
            return new Matrix3x2(n[0], n[1], n[2], n[3], n[4], n[5]);
        }
    }

    /// <summary>The opacity of a <see cref="ItemKind.PushGroup"/>; 1 for other kinds.</summary>
    public float Opacity => Rec.Kind == ItemKind.PushGroup ? Num[0] : 1f;

    /// <summary>The blend mode of a <see cref="ItemKind.PushGroup"/>.</summary>
    public BlendMode Blend => Rec.Kind == ItemKind.PushGroup ? (BlendMode)Rec.A : BlendMode.Normal;

    /// <summary>The role of a <see cref="ItemKind.BeginTag"/>.</summary>
    public TagRole Role => Rec.Kind == ItemKind.BeginTag ? (TagRole)Rec.A : TagRole.Artifact;

    /// <summary>Alternative text of a tag, or null.</summary>
    public string? AltText => Rec.Kind == ItemKind.BeginTag ? _list.StringAt(Rec.B) : null;

    /// <summary>Link target of a tag, or null.</summary>
    public string? LinkTarget => Rec.Kind == ItemKind.BeginTag ? _list.StringAt(Rec.C) : null;

    /// <summary>The number of columns a table cell's <see cref="ItemKind.BeginTag"/> covers; 1 otherwise.</summary>
    public int ColumnSpan => Rec.Kind == ItemKind.BeginTag && Rec.E != 0 ? Rec.E & 0x7FF : 1;

    /// <summary>The number of rows a table cell's <see cref="ItemKind.BeginTag"/> covers; 1 otherwise.</summary>
    public int RowSpan => Rec.Kind == ItemKind.BeginTag && Rec.E != 0 ? Rec.E >> 11 : 1;

    /// <summary>Language of a tag, or null.</summary>
    public string? Language => Rec.Kind == ItemKind.BeginTag ? _list.StringAt(Rec.D) : null;

    private static bool HasRect(ItemKind k) =>
        k is ItemKind.FillRect or ItemKind.FillRoundedRect or ItemKind.Border or ItemKind.BoxShadow or ItemKind.PushClipRect or ItemKind.PushClipRoundedRect
            or ItemKind.SvgImage or ItemKind.BackdropFilter;

    private static bool HasRadii(ItemKind k) =>
        k is ItemKind.FillRoundedRect or ItemKind.Border or ItemKind.BoxShadow or ItemKind.PushClipRoundedRect or ItemKind.BackdropFilter;

    private static bool HasPath(ItemKind k) => k is ItemKind.FillPath or ItemKind.PushClipPath;

    private RectF ReadRect(int at)
    {
        ReadOnlySpan<float> n = Num;
        return new RectF(n[at], n[at + 1], n[at + 2], n[at + 3]);
    }

    private PaintColor ReadColor(int at)
    {
        ReadOnlySpan<float> n = Num;
        return new PaintColor(n[at], n[at + 1], n[at + 2], n[at + 3]);
    }

    private CornerRadii ReadRadii(int at)
    {
        ReadOnlySpan<float> n = Num;
        return new CornerRadii(n[at], n[at + 1], n[at + 2], n[at + 3], n[at + 4], n[at + 5], n[at + 6], n[at + 7]);
    }

    private BorderSide Side(int side)
    {
        if (Rec.Kind != ItemKind.Border)
        {
            return default;
        }

        return new BorderSide(Num[12 + side], (BorderStyle)((Rec.A >> (side * 8)) & 0xFF), ReadColor(16 + (side * 4)));
    }
}
