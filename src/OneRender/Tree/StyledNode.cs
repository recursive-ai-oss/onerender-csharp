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

/// <summary>A handle to a node of a <see cref="StyledTree"/>. Cheap to copy; valid until the tree is cleared.</summary>
public readonly struct StyledNode : IEquatable<StyledNode>
{
    internal StyledNode(StyledTree tree, int index)
    {
        Tree = tree;
        Index = index;
    }

    /// <summary>The tree the node belongs to; null for the default value.</summary>
    public StyledTree? Tree { get; }

    /// <summary>The node's position in the tree's columns. Stable while the tree is not cleared.</summary>
    public int Index { get; }

    /// <summary>True for the default value, which refers to no node.</summary>
    public bool IsNull => Tree is null;

    /// <summary>What the node stands for.</summary>
    public StyledNodeKind Kind => (StyledNodeKind)Tree!.KindOf(Index);

    /// <summary>The parent, or the null handle for the root.</summary>
    public StyledNode Parent => Tree!.Make(Tree.ParentOf(Index));

    /// <summary>The first child, or the null handle.</summary>
    public StyledNode FirstChild => Tree!.Make(Tree.FirstChildOf(Index));

    /// <summary>The last child, or the null handle.</summary>
    public StyledNode LastChild => Tree!.Make(Tree.LastChildOf(Index));

    /// <summary>The next sibling, or the null handle.</summary>
    public StyledNode NextSibling => Tree!.Make(Tree.NextSiblingOf(Index));

    /// <summary>The style row of the node.</summary>
    public StyleId Style => Tree!.StyleOf(Index);

    /// <summary>The computed values of the node.</summary>
    public ref readonly ComputedValues Values => ref Tree!.Styles[Tree.StyleOf(Index)];

    /// <summary>The lower-case local name of an element (<c>div</c>); empty for other kinds.</summary>
    public string TagName => Tree!.TagNameOf(Index);

    /// <summary>The text of a <see cref="StyledNodeKind.Text"/>, <see cref="StyledNodeKind.Before"/> or <see cref="StyledNodeKind.After"/> node; empty otherwise. Valid until the tree is cleared or grows.</summary>
    public ReadOnlySpan<char> Text => Tree!.TextOf(Index);

    /// <summary>The address of replaced content (<c>src</c> of an image, <c>data</c> of an object) as written, or null when there is none.</summary>
    public string? ReplacedSource => Tree!.ReplacedSourceOf(Index);

    /// <summary>The kind of replaced content an element carries, or <see cref="ReplacedKind.None"/>.</summary>
    public ReplacedKind Replaced => Tree!.ReplacedOf(Index);

    /// <summary>The natural width of replaced content in px, or NaN when it has none.</summary>
    public double NaturalWidth => Tree!.NaturalWidthOf(Index);

    /// <summary>The natural height of replaced content in px, or NaN when it has none.</summary>
    public double NaturalHeight => Tree!.NaturalHeightOf(Index);

    /// <summary>
    /// The index of the OneHtml node this node was made from (<c>Node.Index</c>), or -1 for a builder node and for generated nodes.
    /// Pair it with the tree's <see cref="StyledTree.SourceDocument"/> to get the OneHtml node back.
    /// </summary>
    public int SourceIndex => Tree!.SourceIndexOf(Index);

    /// <inheritdoc />
    public bool Equals(StyledNode other) => ReferenceEquals(Tree, other.Tree) && Index == other.Index;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is StyledNode n && Equals(n);

    /// <inheritdoc />
    public override int GetHashCode() => Index;

    /// <summary>Equality.</summary>
    public static bool operator ==(StyledNode left, StyledNode right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(StyledNode left, StyledNode right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Tree is null ? "(null)" : Kind + "#" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
