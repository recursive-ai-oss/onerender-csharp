namespace OneRender.Style;

/// <summary>What a CSS <c>&lt;image&gt;</c> value is.</summary>
public enum ImageSourceKind : byte
{
    /// <summary><c>none</c>, or an image the engine does not understand.</summary>
    None,

    /// <summary>A <c>url()</c> the host resolves.</summary>
    Url,

    /// <summary>A linear, radial or conic gradient, repeating or not.</summary>
    Gradient,
}

/// <summary>The shape of a gradient (CSS Images 3 §3).</summary>
public enum GradientKind : byte
{
    /// <summary><c>linear-gradient()</c>.</summary>
    Linear,

    /// <summary><c>radial-gradient()</c>.</summary>
    Radial,

    /// <summary><c>conic-gradient()</c>.</summary>
    Conic,
}

/// <summary>The ending-shape size keyword of a radial gradient (CSS Images 3 §3.2).</summary>
public enum RadialExtent : byte
{
    /// <summary><c>farthest-corner</c> (initial).</summary>
    FarthestCorner,

    /// <summary><c>closest-corner</c>.</summary>
    ClosestCorner,

    /// <summary><c>farthest-side</c>.</summary>
    FarthestSide,

    /// <summary><c>closest-side</c>.</summary>
    ClosestSide,

    /// <summary>Explicit radii in <see cref="GradientSpec.RadiusX"/> and <see cref="GradientSpec.RadiusY"/>.</summary>
    Explicit,
}

/// <summary>One color stop or transition hint of a gradient, as written (positions are resolved when painting).</summary>
public readonly struct GradientStopSpec : IEquatable<GradientStopSpec>
{
    internal GradientStopSpec(ColorValue color, Length position, bool isHint)
    {
        Color = color;
        Position = position;
        IsHint = isHint;
    }

    /// <summary>The stop color; unused for a hint.</summary>
    public ColorValue Color { get; }

    /// <summary>A length or percentage (an angle in radians stored as <see cref="LengthKind.Number"/> for conic gradients, a percentage as <see cref="LengthKind.Percent"/>), or <see cref="Length.Auto"/> when omitted.</summary>
    public Length Position { get; }

    /// <summary>True for a color hint (a position with no color).</summary>
    public bool IsHint { get; }

    /// <inheritdoc/>
    public bool Equals(GradientStopSpec other) => Color.Equals(other.Color) && Position.Equals(other.Position) && IsHint == other.IsHint;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is GradientStopSpec s && Equals(s);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Color, Position, IsHint);

    /// <summary>Equality.</summary>
    public static bool operator ==(GradientStopSpec left, GradientStopSpec right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(GradientStopSpec left, GradientStopSpec right) => !left.Equals(right);
}

/// <summary>A gradient as computed: shape, direction or center, and stops. Immutable.</summary>
public sealed class GradientSpec : IEquatable<GradientSpec>
{
    internal GradientSpec(GradientKind kind, bool repeating, GradientStopSpec[] stops)
    {
        Kind = kind;
        Repeating = repeating;
        Stops = stops;
    }

    /// <summary>Linear, radial or conic.</summary>
    public GradientKind Kind { get; }

    /// <summary>Whether the gradient repeats (<c>repeating-*-gradient()</c>).</summary>
    public bool Repeating { get; }

    /// <summary>The stops and hints in order.</summary>
    public GradientStopSpec[] Stops { get; }

    /// <summary>Linear: the angle in radians clockwise from "up" (valid when <see cref="CornerKeyword"/> is false). Conic: the <c>from</c> angle.</summary>
    public double Angle { get; internal set; }

    /// <summary>Linear <c>to &lt;corner&gt;</c>: the corner is the direction signs <see cref="CornerX"/>, <see cref="CornerY"/>, and the angle depends on the box (CSS Images 3 §3.1.1).</summary>
    public bool CornerKeyword { get; internal set; }

    /// <summary>With <see cref="CornerKeyword"/>: -1 for left, 1 for right.</summary>
    public int CornerX { get; internal set; }

    /// <summary>With <see cref="CornerKeyword"/>: -1 for top, 1 for bottom.</summary>
    public int CornerY { get; internal set; }

    /// <summary>Radial: true for a circle, false for an ellipse.</summary>
    public bool Circle { get; internal set; }

    /// <summary>Radial: the size keyword or explicit radii.</summary>
    public RadialExtent Extent { get; internal set; }

    /// <summary>Radial with explicit size: the horizontal radius (a circle's radius, or a percentage of the box width).</summary>
    public Length RadiusX { get; internal set; }

    /// <summary>Radial with explicit size: the vertical radius (percentage of the box height).</summary>
    public Length RadiusY { get; internal set; }

    /// <summary>Radial and conic: the center, horizontal part. A percentage refers to the gradient box width; resolve with the box width as the free space.</summary>
    public PositionAxis CenterX { get; internal set; } = new(Length.FromPercent(50), false);

    /// <summary>Radial and conic: the center, vertical part.</summary>
    public PositionAxis CenterY { get; internal set; } = new(Length.FromPercent(50), false);

    /// <inheritdoc/>
    public bool Equals(GradientSpec? other)
    {
        if (other is null || Kind != other.Kind || Repeating != other.Repeating || Stops.Length != other.Stops.Length
            || !Angle.Equals(other.Angle) || CornerKeyword != other.CornerKeyword || CornerX != other.CornerX || CornerY != other.CornerY
            || Circle != other.Circle || Extent != other.Extent || !RadiusX.Equals(other.RadiusX) || !RadiusY.Equals(other.RadiusY)
            || !CenterX.Equals(other.CenterX) || !CenterY.Equals(other.CenterY))
        {
            return false;
        }

        for (int i = 0; i < Stops.Length; i++)
        {
            if (!Stops[i].Equals(other.Stops[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as GradientSpec);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, Repeating, Stops.Length, Angle, Circle, Extent);
}

/// <summary>A computed <c>&lt;image&gt;</c>: nothing, a host-resolved address or a gradient. Immutable.</summary>
public sealed class ImageSpec : IEquatable<ImageSpec>
{
    private ImageSpec(ImageSourceKind kind, string? url, GradientSpec? gradient)
    {
        Kind = kind;
        Url = url;
        Gradient = gradient;
    }

    /// <summary><c>none</c>.</summary>
    public static ImageSpec None { get; } = new(ImageSourceKind.None, null, null);

    /// <summary>What this image is.</summary>
    public ImageSourceKind Kind { get; }

    /// <summary>The address as written in <c>url()</c> (already resolved against the style sheet's base when the cascade supplied it), or null.</summary>
    public string? Url { get; }

    /// <summary>The gradient, or null.</summary>
    public GradientSpec? Gradient { get; }

    internal static ImageSpec FromUrl(string url) => new(ImageSourceKind.Url, url, null);

    internal static ImageSpec FromGradient(GradientSpec g) => new(ImageSourceKind.Gradient, null, g);

    /// <inheritdoc/>
    public bool Equals(ImageSpec? other) =>
        other is not null && Kind == other.Kind && string.Equals(Url, other.Url, StringComparison.Ordinal) && Equals(Gradient, other.Gradient);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ImageSpec);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, Url, Gradient);
}

/// <summary><c>background-repeat</c> for one axis (Backgrounds 3 §3.4).</summary>
public enum BackgroundRepeatKind : byte
{
    /// <summary><c>repeat</c> (initial).</summary>
    Repeat,

    /// <summary><c>no-repeat</c>.</summary>
    NoRepeat,

    /// <summary><c>space</c>.</summary>
    Space,

    /// <summary><c>round</c>.</summary>
    Round,
}

/// <summary>A box edge for <c>background-origin</c> and <c>background-clip</c>.</summary>
public enum BackgroundBoxKind : byte
{
    /// <summary><c>border-box</c>.</summary>
    BorderBox,

    /// <summary><c>padding-box</c>.</summary>
    PaddingBox,

    /// <summary><c>content-box</c>.</summary>
    ContentBox,

    /// <summary><c>text</c> (clip only).</summary>
    Text,

    /// <summary><c>border-area</c> (clip only): the border itself, without the padding box.</summary>
    BorderArea,
}

/// <summary><c>background-attachment</c> (Backgrounds 3 §3.8).</summary>
public enum BackgroundAttachmentKind : byte
{
    /// <summary><c>scroll</c> (initial).</summary>
    Scroll,

    /// <summary><c>fixed</c>: relative to the viewport.</summary>
    Fixed,

    /// <summary><c>local</c>: scrolls with the element's content.</summary>
    Local,
}

/// <summary>How a <c>background-size</c> layer sizes its image.</summary>
public enum BackgroundSizeKind : byte
{
    /// <summary><c>auto auto</c> (initial) or explicit values in <see cref="BackgroundSizeSpec.Width"/> and <see cref="BackgroundSizeSpec.Height"/>.</summary>
    Explicit,

    /// <summary><c>cover</c>.</summary>
    Cover,

    /// <summary><c>contain</c>.</summary>
    Contain,
}

/// <summary>A computed <c>background-size</c> layer.</summary>
public readonly record struct BackgroundSizeSpec(BackgroundSizeKind Kind, Length Width, Length Height)
{
    /// <summary><c>auto</c>.</summary>
    public static BackgroundSizeSpec Auto { get; } = new(BackgroundSizeKind.Explicit, Length.Auto, Length.Auto);
}

/// <summary>
/// One axis of a <c>background-position</c> or <c>object-position</c>: an offset from the start edge or, when
/// <see cref="FromEnd"/>, from the end edge. A percentage refers to the free space (container minus image), CSS Backgrounds 3 §3.6.
/// </summary>
public readonly record struct PositionAxis(Length Offset, bool FromEnd)
{
    /// <summary>The initial value for the two axes of <c>background-position</c>: 0%.</summary>
    public static PositionAxis Start { get; } = new(Length.FromPercent(0), false);

    /// <summary>The offset in px for a free space of <paramref name="free"/> px.</summary>
    /// <param name="free">Container size minus image size.</param>
    public double Resolve(double free)
    {
        double o = Offset.ResolveOr(free, 0);
        return FromEnd ? free - o : o;
    }
}

/// <summary>A computed two-axis position.</summary>
public readonly record struct PositionSpec(PositionAxis X, PositionAxis Y)
{
    /// <summary><c>0% 0%</c>.</summary>
    public static PositionSpec Origin { get; } = new(PositionAxis.Start, PositionAxis.Start);

    /// <summary><c>50% 50%</c>.</summary>
    public static PositionSpec Center { get; } = new(new PositionAxis(Length.FromPercent(50), false), new PositionAxis(Length.FromPercent(50), false));
}

/// <summary>The layered longhands of <c>background</c> other than the color. Layer <i>i</i> takes entry <c>i mod Length</c> of each list (Backgrounds 3 §3.1). Immutable.</summary>
public sealed class BackgroundSpec : IEquatable<BackgroundSpec>
{
    internal BackgroundSpec(
        ImageSpec[] images,
        (BackgroundRepeatKind X, BackgroundRepeatKind Y)[] repeats,
        PositionAxis[] positionsX,
        PositionAxis[] positionsY,
        BackgroundSizeSpec[] sizes,
        BackgroundBoxKind[] origins,
        BackgroundBoxKind[] clips,
        BackgroundAttachmentKind[] attachments)
    {
        Images = images;
        Repeats = repeats;
        PositionsX = positionsX;
        PositionsY = positionsY;
        Sizes = sizes;
        Origins = origins;
        Clips = clips;
        Attachments = attachments;
    }

    /// <summary>The initial value: one layer with no image.</summary>
    public static BackgroundSpec Default { get; } = new(
        [ImageSpec.None],
        [(BackgroundRepeatKind.Repeat, BackgroundRepeatKind.Repeat)],
        [PositionAxis.Start],
        [PositionAxis.Start],
        [BackgroundSizeSpec.Auto],
        [BackgroundBoxKind.PaddingBox],
        [BackgroundBoxKind.BorderBox],
        [BackgroundAttachmentKind.Scroll]);

    /// <summary><c>background-image</c> per layer; its length is the number of layers.</summary>
    public ImageSpec[] Images { get; }

    /// <summary><c>background-repeat</c> (x, y).</summary>
    public (BackgroundRepeatKind X, BackgroundRepeatKind Y)[] Repeats { get; }

    /// <summary><c>background-position-x</c>.</summary>
    public PositionAxis[] PositionsX { get; }

    /// <summary><c>background-position-y</c>.</summary>
    public PositionAxis[] PositionsY { get; }

    /// <summary><c>background-size</c>.</summary>
    public BackgroundSizeSpec[] Sizes { get; }

    /// <summary><c>background-origin</c>.</summary>
    public BackgroundBoxKind[] Origins { get; }

    /// <summary><c>background-clip</c>.</summary>
    public BackgroundBoxKind[] Clips { get; }

    /// <summary><c>background-attachment</c>.</summary>
    public BackgroundAttachmentKind[] Attachments { get; }

    /// <summary>The number of layers.</summary>
    public int LayerCount => Images.Length;

    /// <summary>True when some layer has an image.</summary>
    public bool HasImages
    {
        get
        {
            foreach (ImageSpec i in Images)
            {
                if (i.Kind != ImageSourceKind.None)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal BackgroundSpec With(
        ImageSpec[]? images = null,
        (BackgroundRepeatKind X, BackgroundRepeatKind Y)[]? repeats = null,
        PositionAxis[]? positionsX = null,
        PositionAxis[]? positionsY = null,
        BackgroundSizeSpec[]? sizes = null,
        BackgroundBoxKind[]? origins = null,
        BackgroundBoxKind[]? clips = null,
        BackgroundAttachmentKind[]? attachments = null) =>
        new(images ?? Images, repeats ?? Repeats, positionsX ?? PositionsX, positionsY ?? PositionsY, sizes ?? Sizes, origins ?? Origins, clips ?? Clips, attachments ?? Attachments);

    /// <inheritdoc/>
    public bool Equals(BackgroundSpec? other) =>
        other is not null && (ReferenceEquals(this, other)
        || (Same(Images, other.Images) && Same(Repeats, other.Repeats) && Same(PositionsX, other.PositionsX) && Same(PositionsY, other.PositionsY) && Same(Sizes, other.Sizes)
            && Same(Origins, other.Origins) && Same(Clips, other.Clips) && Same(Attachments, other.Attachments)));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as BackgroundSpec);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Images.Length, Repeats.Length, PositionsX.Length, Sizes.Length, Images[0]);

    internal static bool Same<T>(T[] a, T[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>The edge-repeat mode of <c>border-image-repeat</c> (Backgrounds 3 §6.5).</summary>
public enum BorderImageRepeatKind : byte
{
    /// <summary><c>stretch</c> (initial).</summary>
    Stretch,

    /// <summary><c>repeat</c>.</summary>
    Repeat,

    /// <summary><c>round</c>.</summary>
    Round,

    /// <summary><c>space</c>.</summary>
    Space,
}

/// <summary>One side of <c>border-image-width</c> or <c>-outset</c>, or of <c>-slice</c>: a number (multiplier or image pixels), a length or percentage, or auto.</summary>
public readonly record struct BorderImageValue(Length Value);

/// <summary>The border-image longhands, computed. Immutable.</summary>
public sealed class BorderImageSpec : IEquatable<BorderImageSpec>
{
    internal BorderImageSpec(ImageSpec source, Length[] slice, bool fill, Length[] width, Length[] outset, BorderImageRepeatKind repeatX, BorderImageRepeatKind repeatY)
    {
        Source = source;
        Slice = slice;
        Fill = fill;
        Width = width;
        Outset = outset;
        RepeatX = repeatX;
        RepeatY = repeatY;
    }

    /// <summary>The initial value: no source, slice 100%, width 1, outset 0, stretch.</summary>
    public static BorderImageSpec Default { get; } = new(
        ImageSpec.None,
        [Length.FromPercent(100), Length.FromPercent(100), Length.FromPercent(100), Length.FromPercent(100)],
        false,
        [new Length(LengthKind.Number, 1), new Length(LengthKind.Number, 1), new Length(LengthKind.Number, 1), new Length(LengthKind.Number, 1)],
        [new Length(LengthKind.Number, 0), new Length(LengthKind.Number, 0), new Length(LengthKind.Number, 0), new Length(LengthKind.Number, 0)],
        BorderImageRepeatKind.Stretch,
        BorderImageRepeatKind.Stretch);

    /// <summary><c>border-image-source</c>.</summary>
    public ImageSpec Source { get; }

    /// <summary><c>border-image-slice</c> top, right, bottom, left: <see cref="LengthKind.Number"/> (image pixels) or <see cref="LengthKind.Percent"/>.</summary>
    public Length[] Slice { get; }

    /// <summary>The <c>fill</c> keyword of <c>border-image-slice</c>.</summary>
    public bool Fill { get; }

    /// <summary><c>border-image-width</c> top, right, bottom, left: a number (times the border width), a length, a percentage or auto.</summary>
    public Length[] Width { get; }

    /// <summary><c>border-image-outset</c> top, right, bottom, left: a number (times the border width) or a length.</summary>
    public Length[] Outset { get; }

    /// <summary>Horizontal repeat mode.</summary>
    public BorderImageRepeatKind RepeatX { get; }

    /// <summary>Vertical repeat mode.</summary>
    public BorderImageRepeatKind RepeatY { get; }

    internal BorderImageSpec With(ImageSpec? source = null, Length[]? slice = null, bool? fill = null, Length[]? width = null, Length[]? outset = null, BorderImageRepeatKind? rx = null, BorderImageRepeatKind? ry = null) =>
        new(source ?? Source, slice ?? Slice, fill ?? Fill, width ?? Width, outset ?? Outset, rx ?? RepeatX, ry ?? RepeatY);

    /// <inheritdoc/>
    public bool Equals(BorderImageSpec? other) =>
        other is not null && Source.Equals(other.Source) && Fill == other.Fill && RepeatX == other.RepeatX && RepeatY == other.RepeatY
        && BackgroundSpec.Same(Slice, other.Slice) && BackgroundSpec.Same(Width, other.Width) && BackgroundSpec.Same(Outset, other.Outset);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as BorderImageSpec);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Source, Fill, RepeatX, RepeatY);
}

/// <summary>One <c>box-shadow</c> or <c>text-shadow</c> entry; lengths are px.</summary>
public readonly record struct ShadowSpec(ColorValue Color, double OffsetX, double OffsetY, double Blur, double Spread, bool Inset);

/// <summary>An immutable list of shadows, first on top.</summary>
public sealed class ShadowList : IEquatable<ShadowList>
{
    internal ShadowList(ShadowSpec[] items) => Items = items;

    /// <summary>The empty list (<c>none</c>).</summary>
    public static ShadowList None { get; } = new([]);

    /// <summary>The shadows, first on top.</summary>
    public ShadowSpec[] Items { get; }

    /// <summary>The number of shadows.</summary>
    public int Count => Items.Length;

    /// <inheritdoc/>
    public bool Equals(ShadowList? other) => other is not null && BackgroundSpec.Same(Items, other.Items);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ShadowList);

    /// <inheritdoc/>
    public override int GetHashCode() => Items.Length;
}

/// <summary>The line types of <c>text-decoration-line</c>.</summary>
[Flags]
public enum TextDecorationLines : byte
{
    /// <summary><c>none</c>.</summary>
    None = 0,

    /// <summary><c>underline</c>.</summary>
    Underline = 1,

    /// <summary><c>overline</c>.</summary>
    Overline = 2,

    /// <summary><c>line-through</c>.</summary>
    LineThrough = 4,
}

/// <summary><c>text-decoration-style</c> (Text Decoration 4 §2.3).</summary>
public enum TextDecorationStyleKind : byte
{
    /// <summary><c>solid</c> (initial).</summary>
    Solid,

    /// <summary><c>double</c>.</summary>
    DoubleLine,

    /// <summary><c>dotted</c>.</summary>
    Dotted,

    /// <summary><c>dashed</c>.</summary>
    Dashed,

    /// <summary><c>wavy</c>.</summary>
    Wavy,
}

/// <summary><c>text-underline-position</c> keywords (Text Decoration 4 §3.2), the horizontal ones.</summary>
public enum TextUnderlinePositionKind : byte
{
    /// <summary><c>auto</c> (initial).</summary>
    Auto,

    /// <summary><c>from-font</c>.</summary>
    FromFont,

    /// <summary><c>under</c>.</summary>
    Under,
}

/// <summary><c>object-fit</c> (Images 3 §5.5).</summary>
public enum ObjectFitKind : byte
{
    /// <summary><c>fill</c> (initial).</summary>
    Fill,

    /// <summary><c>contain</c>.</summary>
    Contain,

    /// <summary><c>cover</c>.</summary>
    Cover,

    /// <summary><c>none</c>.</summary>
    None,

    /// <summary><c>scale-down</c>.</summary>
    ScaleDown,
}

/// <summary><c>image-rendering</c> (Images 3 §5.3).</summary>
public enum ImageRenderingKind : byte
{
    /// <summary><c>auto</c> (initial): smooth.</summary>
    Auto,

    /// <summary><c>smooth</c> / <c>high-quality</c>.</summary>
    Smooth,

    /// <summary><c>crisp-edges</c>.</summary>
    CrispEdges,

    /// <summary><c>pixelated</c>.</summary>
    Pixelated,
}
