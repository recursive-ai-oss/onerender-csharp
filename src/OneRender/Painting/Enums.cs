namespace OneRender.Painting;

/// <summary>
/// What an item in a <see cref="DisplayList"/> is. The numeric values are part of the format and never change; new kinds only
/// get new numbers (see docs/display-list.md, "Versioning").
/// </summary>
public enum ItemKind : byte
{
    /// <summary>A filled axis-aligned rectangle.</summary>
    FillRect = 1,

    /// <summary>A filled rectangle with elliptical corner radii.</summary>
    FillRoundedRect = 2,

    /// <summary>A filled path.</summary>
    FillPath = 3,

    /// <summary>A CSS border: per-side widths, styles and colors inside an outer rounded rectangle.</summary>
    Border = 4,

    /// <summary>A CSS box shadow, outer or inset.</summary>
    BoxShadow = 5,

    /// <summary>A host-resolved image drawn from a source rectangle into a destination rectangle.</summary>
    Image = 6,

    /// <summary>A run of positioned glyphs of one face, size and color, with its source text.</summary>
    GlyphRun = 7,

    /// <summary>An inline SVG document drawn into a rectangle (format 1.2).</summary>
    SvgImage = 8,

    /// <summary>Starts clipping to a rectangle.</summary>
    PushClipRect = 16,

    /// <summary>Starts clipping to a rounded rectangle.</summary>
    PushClipRoundedRect = 17,

    /// <summary>Starts clipping to a path.</summary>
    PushClipPath = 18,

    /// <summary>Ends the innermost clip.</summary>
    PopClip = 19,

    /// <summary>Starts a 2D affine transform that applies to everything until the matching pop.</summary>
    PushTransform = 20,

    /// <summary>Ends the innermost transform.</summary>
    PopTransform = 21,

    /// <summary>Starts an isolated group composited with an opacity and a blend mode when it ends.</summary>
    PushGroup = 22,

    /// <summary>Ends the innermost group and composites it.</summary>
    PopGroup = 23,

    /// <summary>Starts a semantic structure element (for tagged PDF and accessibility trees).</summary>
    BeginTag = 24,

    /// <summary>Ends the innermost semantic element.</summary>
    EndTag = 25,

    /// <summary>Starts a filter chain applied to the content until the matching <see cref="PopFilter"/> (format 1.2).</summary>
    PushFilter = 32,

    /// <summary>Ends the innermost filter scope.</summary>
    PopFilter = 33,

    /// <summary>Starts a masked scope: the mask layers come first, then the masked content (format 1.2).</summary>
    PushMask = 34,

    /// <summary>Ends the innermost masked scope and applies the mask.</summary>
    PopMask = 35,

    /// <summary>Starts one layer of a mask; only valid directly inside a <see cref="PushMask"/> and before its content (format 1.2).</summary>
    PushMaskLayer = 36,

    /// <summary>Ends a mask layer.</summary>
    PopMaskLayer = 37,

    /// <summary>Filters what has already been painted under a shape, <c>backdrop-filter</c> (format 1.2).</summary>
    BackdropFilter = 38,

    /// <summary>Starts a 3D transform with perspective; the content is flattened into its plane and projected (format 1.2).</summary>
    PushTransform3D = 40,

    // 39 and 41 to 63 stay reserved. Format 1.2 draws clip-path shapes with PushClipPath and PushClipRoundedRect, so no separate clip-shape kind exists.
}

/// <summary>The kind of paint a fill item uses.</summary>
public enum PaintKind : byte
{
    /// <summary>One color.</summary>
    Solid = 0,

    /// <summary>A CSS linear gradient.</summary>
    Linear = 1,

    /// <summary>A CSS radial gradient (circle or ellipse, set by the two radii).</summary>
    Radial = 2,

    /// <summary>A CSS conic gradient.</summary>
    Conic = 3,
}

/// <summary>Inside rule for a filled or clipping path.</summary>
public enum FillRule : byte
{
    /// <summary>Non-zero winding (SVG and CSS <c>nonzero</c>).</summary>
    NonZero = 0,

    /// <summary>Even-odd (<c>evenodd</c>).</summary>
    EvenOdd = 1,
}

/// <summary>A CSS <c>border-style</c> value (CSS Backgrounds 3 §4.2).</summary>
public enum BorderStyle : byte
{
    /// <summary>No border is drawn. The painter passes width zero.</summary>
    None = 0,

    /// <summary>Like <see cref="None"/>; distinct only for table border conflict resolution, which the painter handles.</summary>
    Hidden = 1,

    /// <summary>A solid line.</summary>
    Solid = 2,

    /// <summary>Square-ish dashes.</summary>
    Dashed = 3,

    /// <summary>Round dots.</summary>
    Dotted = 4,

    /// <summary>Two solid lines with a gap, each a third of the width.</summary>
    DoubleLine = 5,

    /// <summary>Looks carved into the page.</summary>
    Groove = 6,

    /// <summary>Looks raised from the page.</summary>
    Ridge = 7,

    /// <summary>Looks like the box is embedded in the page.</summary>
    Inset = 8,

    /// <summary>Looks like the box comes out of the page.</summary>
    Outset = 9,
}

/// <summary>Blend modes of Compositing and Blending Level 1 §10, plus <see cref="PlusLighter"/> (Compositing 2).</summary>
public enum BlendMode : byte
{
    /// <summary><c>normal</c>.</summary>
    Normal = 0,

    /// <summary><c>multiply</c>.</summary>
    Multiply = 1,

    /// <summary><c>screen</c>.</summary>
    Screen = 2,

    /// <summary><c>overlay</c>.</summary>
    Overlay = 3,

    /// <summary><c>darken</c>.</summary>
    Darken = 4,

    /// <summary><c>lighten</c>.</summary>
    Lighten = 5,

    /// <summary><c>color-dodge</c>.</summary>
    ColorDodge = 6,

    /// <summary><c>color-burn</c>.</summary>
    ColorBurn = 7,

    /// <summary><c>hard-light</c>.</summary>
    HardLight = 8,

    /// <summary><c>soft-light</c>.</summary>
    SoftLight = 9,

    /// <summary><c>difference</c>.</summary>
    Difference = 10,

    /// <summary><c>exclusion</c>.</summary>
    Exclusion = 11,

    /// <summary><c>hue</c>.</summary>
    Hue = 12,

    /// <summary><c>saturation</c>.</summary>
    Saturation = 13,

    /// <summary><c>color</c>.</summary>
    Color = 14,

    /// <summary><c>luminosity</c>.</summary>
    Luminosity = 15,

    /// <summary><c>plus-lighter</c>: the sum of the premultiplied colors, clamped.</summary>
    PlusLighter = 16,
}

/// <summary>How an image is sampled when scaled.</summary>
public enum ImageSampling : byte
{
    /// <summary>Smooth (bilinear or better): <c>image-rendering: auto</c>.</summary>
    Smooth = 0,

    /// <summary>Nearest neighbour: <c>image-rendering: pixelated</c> and <c>crisp-edges</c>.</summary>
    Pixelated = 1,
}

/// <summary>Path segment kinds in a <see cref="PathBuffer"/> or a path item.</summary>
public enum PathVerb : byte
{
    /// <summary>Starts a figure; 2 coordinates (x, y).</summary>
    Move = 0,

    /// <summary>Straight line; 2 coordinates.</summary>
    Line = 1,

    /// <summary>Quadratic Bézier; 4 coordinates (control x, y, end x, y).</summary>
    Quad = 2,

    /// <summary>Cubic Bézier; 6 coordinates (control 1, control 2, end).</summary>
    Cubic = 3,

    /// <summary>Closes the figure; no coordinates.</summary>
    Close = 4,
}

/// <summary>The semantic role of a tagged element. Names follow the PDF structure types a tagged-PDF writer maps them to.</summary>
public enum TagRole : byte
{
    /// <summary>Content that is not part of the document structure (backgrounds, rules, decoration); PDF "Artifact".</summary>
    Artifact = 0,

    /// <summary>Root document element.</summary>
    Document = 1,

    /// <summary>A part of a document (<c>main</c>, <c>article</c>).</summary>
    Part = 2,

    /// <summary>A section (<c>section</c>, <c>nav</c>, <c>aside</c>).</summary>
    Section = 3,

    /// <summary>A generic block container (<c>div</c>).</summary>
    Div = 4,

    /// <summary>A paragraph.</summary>
    Paragraph = 5,

    /// <summary>Inline text container (<c>span</c>).</summary>
    Span = 6,

    /// <summary>Heading level 1.</summary>
    Heading1 = 7,

    /// <summary>Heading level 2.</summary>
    Heading2 = 8,

    /// <summary>Heading level 3.</summary>
    Heading3 = 9,

    /// <summary>Heading level 4.</summary>
    Heading4 = 10,

    /// <summary>Heading level 5.</summary>
    Heading5 = 11,

    /// <summary>Heading level 6.</summary>
    Heading6 = 12,

    /// <summary>A list (<c>ul</c>, <c>ol</c>).</summary>
    List = 13,

    /// <summary>A list item (<c>li</c>).</summary>
    ListItem = 14,

    /// <summary>A list item label (the marker).</summary>
    ListLabel = 15,

    /// <summary>A list item body.</summary>
    ListBody = 16,

    /// <summary>A table.</summary>
    Table = 17,

    /// <summary>A table row.</summary>
    TableRow = 18,

    /// <summary>A table header cell.</summary>
    TableHeaderCell = 19,

    /// <summary>A table data cell.</summary>
    TableDataCell = 20,

    /// <summary>A table header row group.</summary>
    TableHead = 21,

    /// <summary>A table body row group.</summary>
    TableBody = 22,

    /// <summary>A table footer row group.</summary>
    TableFoot = 23,

    /// <summary>An illustration (<c>img</c> with alternative text, <c>svg</c>).</summary>
    Figure = 24,

    /// <summary>A caption (<c>figcaption</c>, <c>caption</c>).</summary>
    Caption = 25,

    /// <summary>A hyperlink; the target goes in the tag's link target.</summary>
    Link = 26,

    /// <summary>A block quotation.</summary>
    BlockQuote = 27,

    /// <summary>Inline code.</summary>
    Code = 28,

    /// <summary>A form field or form container.</summary>
    Form = 29,
}
