using OneRender.Tree;
using OneFont;

namespace OneRender.Layout;

/// <summary>What a <see cref="Fragment"/> stands for.</summary>
public enum FragmentKind : byte
{
    /// <summary>A piece of a box: its border box and decoration. One box can have several (an inline box broken across lines, a block broken across pages).</summary>
    Box,

    /// <summary>A line box of an inline formatting context (CSS2 §9.4.2). Its children are the inline-level fragments on the line, in visual order of the line's own coordinates.</summary>
    Line,

    /// <summary>A run of text on one line: a range of a text box's text with its shaped glyphs.</summary>
    Text,
}

/// <summary>Facts about how a fragment relates to the other pieces of its box.</summary>
[Flags]
public enum FragmentTraits : byte
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The box continues in a later fragment (broken after this one by a line or page break).</summary>
    ContinuesAfter = 1,

    /// <summary>The box continues from an earlier fragment (this one starts after a break).</summary>
    ContinuesBefore = 2,
}

/// <summary>Identifies a glyph run stored in a <see cref="FragmentTree"/>; the default value is "no run".</summary>
public readonly struct GlyphRunId : IEquatable<GlyphRunId>
{
    internal GlyphRunId(int index) => Index = index;

    /// <summary>The run's position in the tree, 0-based; -1 for "no run".</summary>
    public int Index { get; }

    /// <summary>True when this refers to a run.</summary>
    public bool IsValid => Index >= 0;

    /// <summary>The value that refers to no run.</summary>
    public static GlyphRunId None => new(-1);

    /// <inheritdoc />
    public bool Equals(GlyphRunId other) => Index == other.Index;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GlyphRunId g && Equals(g);

    /// <inheritdoc />
    public override int GetHashCode() => Index;

    /// <summary>Equality.</summary>
    public static bool operator ==(GlyphRunId left, GlyphRunId right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(GlyphRunId left, GlyphRunId right) => !left.Equals(right);
}

/// <summary>
/// A run of shaped glyphs in one face and size, as paint and the PDF backend consume it: the face, the glyph ids with
/// positions, and the source text it came from (for text extraction and accessibility).
/// </summary>
public readonly struct GlyphRun
{
    internal GlyphRun(FontFace face, double fontSize, int glyphStart, int glyphCount, int textStart, int textLength, bool rightToLeft)
    {
        Face = face;
        FontSize = fontSize;
        GlyphStart = glyphStart;
        GlyphCount = glyphCount;
        TextStart = textStart;
        TextLength = textLength;
        RightToLeft = rightToLeft;
    }

    /// <summary>The OneFont face the glyph ids belong to.</summary>
    public FontFace Face { get; }

    /// <summary>The font size in CSS px.</summary>
    public double FontSize { get; }

    /// <summary>The index of the first glyph in the tree's glyph columns.</summary>
    public int GlyphStart { get; }

    /// <summary>The number of glyphs.</summary>
    public int GlyphCount { get; }

    /// <summary>The start of the source text, as a UTF-16 offset into the text box's text.</summary>
    public int TextStart { get; }

    /// <summary>The length of the source text in UTF-16 units.</summary>
    public int TextLength { get; }

    /// <summary>True when the run is right-to-left (an odd bidi level); glyphs are still stored in visual order.</summary>
    public bool RightToLeft { get; }
}

/// <summary>A handle to a fragment of a <see cref="FragmentTree"/>. Cheap to copy; valid until the tree is cleared.</summary>
public readonly struct Fragment : IEquatable<Fragment>
{
    internal Fragment(FragmentTree tree, int index)
    {
        Tree = tree;
        Index = index;
    }

    /// <summary>The tree the fragment belongs to; null for the default value.</summary>
    public FragmentTree? Tree { get; }

    /// <summary>The fragment's position in the tree's columns.</summary>
    public int Index { get; }

    /// <summary>True for the default value, which refers to no fragment.</summary>
    public bool IsNull => Tree is null;

    /// <summary>What the fragment stands for.</summary>
    public FragmentKind Kind => (FragmentKind)Tree!.KindOf(Index);

    /// <summary>The box this fragment is a piece of; the null handle for line fragments.</summary>
    public Box Box => Tree!.Boxes.Make(Tree.BoxOf(Index));

    /// <summary>The parent, or the null handle for the root.</summary>
    public Fragment Parent => Tree!.Make(Tree.ParentOf(Index));

    /// <summary>The first child, or the null handle.</summary>
    public Fragment FirstChild => Tree!.Make(Tree.FirstChildOf(Index));

    /// <summary>The last child, or the null handle.</summary>
    public Fragment LastChild => Tree!.Make(Tree.LastChildOf(Index));

    /// <summary>The next sibling, or the null handle.</summary>
    public Fragment NextSibling => Tree!.Make(Tree.NextSiblingOf(Index));

    /// <summary>The next fragment of the same box (the box's next piece), or the null handle.</summary>
    public Fragment NextOfBox => Tree!.Make(Tree.NextOfBoxOf(Index));

    /// <summary>The border box in the parent's coordinates: its top-left corner is relative to the top-left corner of the parent fragment's border box.</summary>
    public Rect BorderBox => Tree!.BorderBoxOf(Index);

    /// <summary>Resolved border widths in px.</summary>
    public Insets Border => Tree!.BorderOf(Index);

    /// <summary>Resolved padding in px.</summary>
    public Insets Padding => Tree!.PaddingOf(Index);

    /// <summary>
    /// The scrollable overflow area (CSS Overflow 3 §2.2) in this fragment's own coordinates (origin at the top-left of its border box):
    /// the border box plus the boxes of descendants that extend beyond it. Equals the border box at its own origin until layout widens it.
    /// </summary>
    public Rect ScrollableOverflow => Tree!.OverflowOf(Index);

    /// <summary>For line fragments, the baseline's distance from the top of the line in px; 0 for other kinds.</summary>
    public double Baseline => Tree!.BaselineOf(Index);

    /// <summary>For text fragments, the first UTF-16 offset of the box's text this fragment shows.</summary>
    public int TextStart => Tree!.TextStartOf(Index);

    /// <summary>For text fragments, the number of UTF-16 units of the box's text this fragment shows.</summary>
    public int TextLength => Tree!.TextLengthOf(Index);

    /// <summary>For text fragments, the shaped glyphs; <see cref="GlyphRunId.None"/> if the fragment is not shaped.</summary>
    public GlyphRunId GlyphRun => Tree!.GlyphRunOf(Index);

    /// <summary>Relation to the other fragments of the same box.</summary>
    public FragmentTraits Traits => (FragmentTraits)Tree!.TraitsOf(Index);

    /// <inheritdoc />
    public bool Equals(Fragment other) => ReferenceEquals(Tree, other.Tree) && Index == other.Index;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Fragment f && Equals(f);

    /// <inheritdoc />
    public override int GetHashCode() => Index;

    /// <summary>Equality.</summary>
    public static bool operator ==(Fragment left, Fragment right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(Fragment left, Fragment right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Tree is null ? "(null)" : Kind + "#" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
