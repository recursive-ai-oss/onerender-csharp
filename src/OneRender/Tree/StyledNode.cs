using OneRender.Style;

namespace OneRender.Tree;

/// <summary>What a <see cref="StyledNode"/> stands for.</summary>
public enum StyledNodeKind : byte
{
    /// <summary>An element.</summary>
    Element,

    /// <summary>A run of text. Its style is its parent's.</summary>
    Text,

    /// <summary>The <c>::marker</c> of a list item (CSS Lists 3 §3). A list item's first child; the box tree turns it into a marker placeholder.</summary>
    Marker,

    /// <summary>The <c>::before</c> of an element (CSS Pseudo-Elements 4 §4); only present when its <c>content</c> generates a box. Its text is the generated string.</summary>
    Before,

    /// <summary>The <c>::after</c> of an element; see <see cref="Before"/>.</summary>
    After,

    /// <summary>The box that holds the track of an <c>input type=range</c> (its shadow container): a flex container that centers the track in the input.</summary>
    SliderContainer,

    /// <summary>The track of a range (<c>::slider-track</c>, CSS Forms 1): a child of the <see cref="SliderContainer"/>, styled from the cascade.</summary>
    SliderTrack,

    /// <summary>The thumb of a range (<c>::slider-thumb</c>): the track's only child, styled from the cascade.</summary>
    SliderThumb,
}

/// <summary>Size limits for building a tree from untrusted input. Checked before anything is stored.</summary>
public sealed class TreeLimits
{
    /// <summary>The defaults: 2 million nodes, depth 1,048,576, 32 million characters of text.</summary>
    public static TreeLimits Default { get; } = new();

    /// <summary>The most nodes in one tree; later nodes are dropped with a diagnostic. Default 2,000,000.</summary>
    public int MaxNodes { get; init; } = 2_000_000;

    /// <summary>The deepest nesting; deeper nodes are dropped with a diagnostic. Default 1,048,576. Nothing in the engine recurses on tree depth.</summary>
    public int MaxDepth { get; init; } = 1 << 20;

    /// <summary>The most UTF-16 units of text in one tree. Default 33,554,432.</summary>
    public int MaxTextLength { get; init; } = 32 * 1024 * 1024;
}

/// <summary>
/// Lets the host tell the engine what it cannot know itself: the natural size of replaced content such as images.
/// The engine never loads, decodes or fetches anything.
/// </summary>
public interface IResourceResolver
{
    /// <summary>Looks up the natural size of replaced content.</summary>
    /// <param name="kind">What kind of element asks.</param>
    /// <param name="source">The element's source attribute (<c>src</c>, or <c>data</c> for <c>object</c>); empty when it has none.</param>
    /// <param name="width">The natural width in CSS px, 0 or more.</param>
    /// <param name="height">The natural height in CSS px, 0 or more.</param>
    /// <returns>False when the size is unknown; the engine then uses the defaults of HTML Standard §15.4 (300 by 150 for canvas, video, iframe and embedded objects, no natural size for an image).</returns>
    bool TryGetNaturalSize(ReplacedKind kind, ReadOnlySpan<char> source, out double width, out double height);
}

/// <summary>A handle to a node of a <see cref="StyledTree"/>. Cheap to copy; valid until the node is removed and its slot reclaimed, or the tree is cleared (see <see cref="IsValid"/>).</summary>
public readonly struct StyledNode : IEquatable<StyledNode>
{
    internal StyledNode(StyledTree tree, int index)
    {
        Tree = tree;
        Index = index;
        Generation = tree.GenerationOf(index);
    }

    /// <summary>The tree the node belongs to; null for the default value.</summary>
    public StyledTree? Tree { get; }

    /// <summary>The node's position in the tree's columns. Stable for as long as the node exists; the position of a removed node is reused by later nodes, so check <see cref="IsValid"/> before trusting an old handle.</summary>
    public int Index { get; }

    /// <summary>Counts how often the slot at <see cref="Index"/> has been given back; a handle whose count differs from the slot's refers to a node that no longer exists.</summary>
    internal int Generation { get; }

    /// <summary>
    /// True while the handle refers to the node it was made for. It becomes false once the node was removed and the tree reclaimed its
    /// slot (at the next flush of the page that owns the tree, or at the next insert of a tree no page tracks), and after <see cref="StyledTree.Clear"/>.
    /// The null handle is not valid. Every other member throws <see cref="InvalidOperationException"/> on an invalid handle.
    /// </summary>
    public bool IsValid => Tree is not null && Tree.GenerationOf(Index) == Generation;

    private StyledTree T
    {
        get
        {
            StyledTree t = Tree!;
            if (t.GenerationOf(Index) != Generation)
            {
                throw new InvalidOperationException("The node was removed from the tree; its slot may belong to another node now.");
            }

            return t;
        }
    }

    /// <summary>True for the default value, which refers to no node.</summary>
    public bool IsNull => Tree is null;

    /// <summary>What the node stands for.</summary>
    public StyledNodeKind Kind => (StyledNodeKind)T.KindOf(Index);

    /// <summary>The parent, or the null handle for the root.</summary>
    public StyledNode Parent => T.Make(T.ParentOf(Index));

    /// <summary>The first child, or the null handle.</summary>
    public StyledNode FirstChild => T.Make(T.FirstChildOf(Index));

    /// <summary>The last child, or the null handle.</summary>
    public StyledNode LastChild => T.Make(T.LastChildOf(Index));

    /// <summary>The next sibling, or the null handle.</summary>
    public StyledNode NextSibling => T.Make(T.NextSiblingOf(Index));

    /// <summary>The style row of the node.</summary>
    public StyleId Style => T.StyleOf(Index);

    /// <summary>The computed values of the node.</summary>
    public ref readonly ComputedValues Values => ref T.Styles[T.StyleOf(Index)];

    /// <summary>The lower-case local name of an element (<c>div</c>); empty for other kinds.</summary>
    public string TagName => T.TagNameOf(Index);

    /// <summary>The text of a <see cref="StyledNodeKind.Text"/>, <see cref="StyledNodeKind.Before"/> or <see cref="StyledNodeKind.After"/> node; empty otherwise. Valid until the tree is cleared or grows.</summary>
    public ReadOnlySpan<char> Text => T.TextOf(Index);

    /// <summary>The address of replaced content (<c>src</c> of an image, <c>data</c> of an object) as written, or null when there is none.</summary>
    public string? ReplacedSource => T.ReplacedSourceOf(Index);

    /// <summary>The kind of replaced content an element carries, or <see cref="ReplacedKind.None"/>.</summary>
    public ReplacedKind Replaced => T.ReplacedOf(Index);

    /// <summary>The natural width of replaced content in px, or NaN when it has none.</summary>
    public double NaturalWidth => T.NaturalWidthOf(Index);

    /// <summary>The natural height of replaced content in px, or NaN when it has none.</summary>
    public double NaturalHeight => T.NaturalHeightOf(Index);

    /// <summary>The natural aspect ratio (width over height) of replaced content that has a ratio but no size (an <c>svg</c> with only a <c>viewBox</c>), or 0.</summary>
    public double NaturalRatio => T.NaturalRatioOf(Index);

    /// <summary>
    /// The index of the OneHtml node this node was made from (<c>Node.Index</c>), or -1 for a builder node and for generated nodes.
    /// Pair it with the tree's <see cref="StyledTree.SourceDocument"/> to get the OneHtml node back.
    /// </summary>
    public int SourceIndex => T.SourceIndexOf(Index);

    /// <inheritdoc />
    public bool Equals(StyledNode other) => ReferenceEquals(Tree, other.Tree) && Index == other.Index && Generation == other.Generation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StyledNode n && Equals(n);

    /// <inheritdoc />
    public override int GetHashCode() => Index;

    /// <summary>Equality.</summary>
    public static bool operator ==(StyledNode left, StyledNode right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(StyledNode left, StyledNode right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Tree is null ? "(null)" : (IsValid ? Kind.ToString() : "Stale") + "#" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
