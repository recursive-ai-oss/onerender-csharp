using OneRender.Style;
using RecursiveAI.OneHtml;

namespace OneRender.Tree;

/// <summary>
/// The styled-node tree: elements, text and generated content with their computed styles, in struct-of-arrays columns.
/// It is the engine's front-end-neutral input. <see cref="HtmlFrontEnd"/> fills it from a OneHtml document and cascade;
/// the <c>Add…</c> methods fill it from C# with no HTML involved. Both go through the same computed-value code.
/// </summary>
/// <remarks>
/// The tree is retained: <see cref="Clear"/> keeps the columns, the style table and the interned names, so building the
/// same content again allocates nothing. Not thread-safe.
/// </remarks>
public sealed partial class StyledTree
{
    private static readonly string[] ReplacedTags = ["img", "video", "canvas", "iframe", "embed", "object", "svg"];

    private readonly StyleDeclaration.Source _builderSource = new();
    private readonly Dictionary<string, int> _tagIds = new(StringComparer.Ordinal);
    private readonly List<string> _tags = ["" ];
    private byte[] _kind;
    private int[] _parent;
    private int[] _first;
    private int[] _last;
    private int[] _next;
    private int[] _depth;
    private int[] _style;
    private int[] _source;
    private int[] _tag;
    private int[] _textStart;
    private int[] _textLength;
    private byte[] _replaced;
    private double[] _naturalW;
    private double[] _naturalH;
    private int[] _span;
    private char[] _text;
    private int[] _sourceToNode = [];
    private int _count;
    private int _textCount;
    private bool _limitReported;
    private bool _textLimitReported;
    private Document? _document;

    /// <summary>Creates an empty tree.</summary>
    /// <param name="styles">The table to compute styles into; a new one when null. Trees may share a table.</param>
    /// <param name="limits">Size limits; <see cref="TreeLimits.Default"/> when null.</param>
    public StyledTree(StyleTable? styles = null, TreeLimits? limits = null)
    {
        Styles = styles ?? new StyleTable();
        Limits = limits ?? TreeLimits.Default;
        _kind = new byte[64];
        _parent = new int[64];
        _first = new int[64];
        _last = new int[64];
        _next = new int[64];
        _depth = new int[64];
        _style = new int[64];
        _source = new int[64];
        _tag = new int[64];
        _textStart = new int[64];
        _textLength = new int[64];
        _replaced = new byte[64];
        _naturalW = new double[64];
        _naturalH = new double[64];
        _span = new int[64];
        _text = new char[256];
        _tagIds[""] = 0;
    }

    /// <summary>The computed styles of the nodes.</summary>
    public StyleTable Styles { get; }

    /// <summary>The size limits in force.</summary>
    public TreeLimits Limits { get; }

    /// <summary>What was dropped or ignored while building: limits hit, invalid values met. Cleared by <see cref="Clear"/>.</summary>
    public DiagnosticBag Diagnostics => Styles.Diagnostics;

    /// <summary>The number of nodes.</summary>
    public int Count => _count;

    /// <summary>The host's resource resolver, used for the natural size of replaced content. Null means no sizes are known.</summary>
    public IResourceResolver? Resolver { get; set; }

    /// <summary>The OneHtml document the tree was built from, or null for a builder tree.</summary>
    public Document? SourceDocument => _document;

    /// <summary>The root, or the null handle when the tree is empty.</summary>
    public StyledNode Root => _count == 0 ? default : new StyledNode(this, 0);

    /// <summary>Removes every node and diagnostic; keeps all storage and cached styles.</summary>
    public void Clear()
    {
        _count = 0;
        _textCount = 0;
        _limitReported = false;
        _textLimitReported = false;
        _document = null;
        Styles.Diagnostics.Clear();
    }

    // ---- builder front end ----

    /// <summary>Adds the root element. The tree must be empty.</summary>
    /// <param name="tag">The element's local name, for example <c>div</c>. ASCII letters are lower-cased by the caller; names are compared ordinally.</param>
    /// <param name="style">Declared values; null for none.</param>
    /// <returns>The new node.</returns>
    /// <exception cref="InvalidOperationException">The tree already has a root.</exception>
    public StyledNode AddRoot(string tag, StyleDeclaration? style = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (_count != 0)
        {
            throw new InvalidOperationException("The tree already has a root; call Clear first.");
        }

        return AddBuilderElement(-1, tag, style);
    }

    /// <summary>Adds an element as the last child of <paramref name="parent"/>.</summary>
    /// <param name="parent">An element of this tree. A null handle or a node dropped by a limit adds nothing.</param>
    /// <param name="tag">The element's local name. <c>img</c>, <c>video</c>, <c>canvas</c>, <c>iframe</c>, <c>embed</c>, <c>object</c> and <c>svg</c> are replaced elements and take no children.</param>
    /// <param name="style">Declared values; null for none.</param>
    /// <returns>The new node, or the null handle when a limit stopped it (a diagnostic says which).</returns>
    /// <exception cref="ArgumentException"><paramref name="parent"/> belongs to another tree, or is a text node.</exception>
    public StyledNode AddElement(StyledNode parent, string tag, StyleDeclaration? style = null)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (parent.IsNull)
        {
            return default;
        }

        CheckParent(parent);
        return AddBuilderElement(parent.Index, tag, style);
    }

    /// <summary>Adds text as the last child of <paramref name="parent"/>. The text takes its parent's style.</summary>
    /// <param name="parent">An element of this tree.</param>
    /// <param name="text">The text, unprocessed: white space is collapsed by layout according to <c>white-space</c>. Empty text adds nothing.</param>
    /// <returns>The new node, or the null handle when nothing was added.</returns>
    /// <exception cref="ArgumentException"><paramref name="parent"/> belongs to another tree, or is a text node.</exception>
    public StyledNode AddText(StyledNode parent, ReadOnlySpan<char> text)
    {
        if (parent.IsNull || text.IsEmpty)
        {
            return default;
        }

        CheckParent(parent);
        int idx = AppendText(parent.Index, text);
        return Make(idx);
    }

    /// <summary>Sets the natural size of replaced content of a builder element.</summary>
    /// <param name="node">A replaced element of this tree.</param>
    /// <param name="width">Width in px; NaN for none.</param>
    /// <param name="height">Height in px; NaN for none.</param>
    public void SetNaturalSize(StyledNode node, double width, double height)
    {
        if (node.IsNull || !ReferenceEquals(node.Tree, this))
        {
            throw new ArgumentException("The node does not belong to this tree.", nameof(node));
        }

        _naturalW[node.Index] = width < 0 ? double.NaN : width;
        _naturalH[node.Index] = height < 0 ? double.NaN : height;
    }

    private void CheckParent(StyledNode parent)
    {
        if (!ReferenceEquals(parent.Tree, this))
        {
            throw new ArgumentException("The parent belongs to another tree.", nameof(parent));
        }

        if (_kind[parent.Index] != (byte)StyledNodeKind.Element)
        {
            throw new ArgumentException("Only elements can have children.", nameof(parent));
        }
    }

    private StyledNode AddBuilderElement(int parent, string tag, StyleDeclaration? declaration)
    {
        StyleId parentStyle = parent < 0 ? default : new StyleId(_style[parent]);
        if (parent >= 0 && _replaced[parent] != (byte)ReplacedKind.None)
        {
            return default; // replaced elements take no children
        }

        StyleId style;
        object key = (object?)declaration ?? NoDeclaration;
        int version = declaration?.Version ?? 0;
        if (!Styles.TryGetCached(key, version, parentStyle, out style))
        {
            _builderSource.Declaration = declaration;
            style = Styles.Compute(key, version, _builderSource, parentStyle, parent >= 0, parent < 0);
        }

        if (parent < 0)
        {
            Styles.NoteRoot(style);
        }

        int idx = NewNode(StyledNodeKind.Element, parent, style, -1);
        if (idx < 0)
        {
            return default;
        }

        int tagId = InternTag(tag);
        _tag[idx] = tagId;
        ReplacedKind rk = ReplacedKindOf(tag);
        _replaced[idx] = (byte)rk;
        if (rk != ReplacedKind.None)
        {
            SetDefaultNaturalSize(idx, rk, default);
        }

        if (Styles[style].Display.IsListItem)
        {
            AddMarker(idx, default);
        }

        return Make(idx);
    }

    private static readonly object NoDeclaration = new();

    // ---- primitives shared with the front ends ----

    internal StyledNode Make(int index) => index < 0 ? default : new StyledNode(this, index);

    internal byte KindOf(int i) => _kind[i];

    internal int ParentOf(int i) => _parent[i];

    internal int FirstChildOf(int i) => _first[i];

    internal int LastChildOf(int i) => _last[i];

    internal int NextSiblingOf(int i) => _next[i];

    internal StyleId StyleOf(int i) => new(_style[i]);

    internal string TagNameOf(int i) => _tags[_tag[i]];

    internal ReadOnlySpan<char> TextOf(int i) => new(_text, _textStart[i], _textLength[i]);

    internal ReplacedKind ReplacedOf(int i) => (ReplacedKind)_replaced[i];

    internal double NaturalWidthOf(int i) => _naturalW[i];

    private const int DefaultSpan = 1 | (1 << 11);

    /// <summary>The most columns a <c>colspan</c> or <c>span</c> may cover (HTML Standard §4.9.11 clamps to 1000).</summary>
    public const int MaxColumnSpan = 1000;

    /// <summary>The most rows a <c>rowspan</c> may cover (HTML Standard §4.9.11 clamps to 65534).</summary>
    public const int MaxRowSpan = 65534;

    /// <summary>
    /// Sets the table spans of a cell (<c>colspan</c>, <c>rowspan</c>) or the <c>span</c> of a column or column group. The values are
    /// clamped to the HTML limits; a column span below 1 counts as 1. A row span of 0 means "to the end of the row group".
    /// </summary>
    /// <param name="node">The element.</param>
    /// <param name="columns">The column span (or the <c>span</c> of a column), at least 1.</param>
    /// <param name="rows">The row span, 0 for the rest of the row group.</param>
    public void SetTableSpans(StyledNode node, int columns, int rows)
    {
        if (node.IsNull || !ReferenceEquals(node.Tree, this))
        {
            throw new ArgumentException("The node does not belong to this tree.", nameof(node));
        }

        SetSpans(node.Index, columns, rows);
    }

    internal void SetSpans(int i, int columns, int rows)
    {
        int c = Math.Clamp(columns, 1, MaxColumnSpan);
        int r = Math.Clamp(rows, 0, MaxRowSpan);
        _span[i] = c | (r << 11);
    }

    internal void ReplaceStyle(int i, StyleId style) => _style[i] = style.Index;

    internal int ColumnSpanOf(int i) => _span[i] & 0x7FF;

    internal int RowSpanOf(int i) => _span[i] >> 11;

    internal double NaturalHeightOf(int i) => _naturalH[i];

    internal int SourceIndexOf(int i) => _source[i];

    internal int DepthOf(int i) => _depth[i];

    internal void SetDocument(Document? document, int sourceNodeCount)
    {
        _document = document;
        if (_sourceToNode.Length < sourceNodeCount)
        {
            _sourceToNode = new int[Math.Max(sourceNodeCount, _sourceToNode.Length * 2)];
        }

        Array.Fill(_sourceToNode, -1, 0, sourceNodeCount);
    }

    /// <summary>The styled node made from the OneHtml node with the given <c>Node.Index</c>, or the null handle.</summary>
    /// <param name="sourceIndex">A <c>RecursiveAI.OneHtml.Node.Index</c> of <see cref="SourceDocument"/>.</param>
    public StyledNode FindBySource(int sourceIndex) =>
        (uint)sourceIndex < (uint)_sourceToNode.Length && sourceIndex >= 0 ? Make(_sourceToNode[sourceIndex]) : default;

    internal int NewNode(StyledNodeKind kind, int parent, StyleId style, int sourceIndex)
    {
        if (_count >= Limits.MaxNodes)
        {
            ReportLimit("node count limit of " + Limits.MaxNodes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " reached; later nodes are dropped");
            return -1;
        }

        int depth = parent < 0 ? 0 : _depth[parent] + 1;
        if (depth > Limits.MaxDepth)
        {
            ReportLimit("depth limit of " + Limits.MaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture) + " reached; deeper nodes are dropped");
            return -1;
        }

        if (_count == _kind.Length)
        {
            Grow();
        }

        int i = _count++;
        _kind[i] = (byte)kind;
        _parent[i] = parent;
        _first[i] = -1;
        _last[i] = -1;
        _next[i] = -1;
        _depth[i] = depth;
        _style[i] = style.Index;
        _source[i] = sourceIndex;
        _tag[i] = 0;
        _textStart[i] = 0;
        _textLength[i] = 0;
        _replaced[i] = 0;
        _naturalW[i] = double.NaN;
        _naturalH[i] = double.NaN;
        _span[i] = DefaultSpan;
        if (parent >= 0)
        {
            if (_last[parent] >= 0)
            {
                _next[_last[parent]] = i;
            }
            else
            {
                _first[parent] = i;
            }

            _last[parent] = i;
        }

        if (sourceIndex >= 0 && sourceIndex < _sourceToNode.Length)
        {
            _sourceToNode[sourceIndex] = i;
        }

        return i;
    }

    internal int AppendText(int parent, ReadOnlySpan<char> text)
    {
        if (text.Length > Limits.MaxTextLength - _textCount)
        {
            if (!_textLimitReported)
            {
                _textLimitReported = true;
                Styles.Diagnostics.Add(DiagnosticKind.LimitExceeded, "text limit of " + Limits.MaxTextLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + " characters reached; later text is dropped");
            }

            return -1;
        }

        int idx = NewNode(StyledNodeKind.Text, parent, new StyleId(_style[parent]), -1);
        if (idx < 0)
        {
            return -1;
        }

        SetText(idx, text);
        return idx;
    }

    internal void SetText(int idx, ReadOnlySpan<char> text)
    {
        if (_textCount + text.Length > _text.Length)
        {
            Array.Resize(ref _text, (int)Math.Min(Array.MaxLength, Math.Max((long)_text.Length * 2, (long)_textCount + text.Length)));
        }

        text.CopyTo(_text.AsSpan(_textCount));
        _textStart[idx] = _textCount;
        _textLength[idx] = text.Length;
        _textCount += text.Length;
    }

    internal void SetElementInfo(int idx, string tag, ReplacedKind replaced)
    {
        _tag[idx] = InternTag(tag);
        _replaced[idx] = (byte)replaced;
    }

    internal void SetNatural(int idx, double w, double h)
    {
        _naturalW[idx] = w;
        _naturalH[idx] = h;
    }

    /// <summary>Gives replaced content its natural size: the host's answer, else the HTML defaults.</summary>
    internal void SetDefaultNaturalSize(int idx, ReplacedKind kind, ReadOnlySpan<char> source)
    {
        double w = double.NaN, h = double.NaN;
        if (Resolver is not null && Resolver.TryGetNaturalSize(kind, source, out double rw, out double rh) && rw >= 0 && rh >= 0)
        {
            w = rw;
            h = rh;
        }
        else if (kind != ReplacedKind.Image)
        {
            // HTML Standard §15.4: canvas, video, iframe, embed and object without a known size are 300 by 150.
            w = 300;
            h = 150;
        }

        _naturalW[idx] = w;
        _naturalH[idx] = h;
    }

    internal int AddMarker(int listItem, StyleId markerStyle)
    {
        StyleId style = markerStyle.IsNone ? Styles.GetAnonymous(new StyleId(_style[listItem]), DisplayType.Inline) : markerStyle;
        return NewNode(StyledNodeKind.Marker, listItem, style, -1);
    }

    internal static ReplacedKind ReplacedKindOf(ReadOnlySpan<char> tag)
    {
        for (int i = 0; i < ReplacedTags.Length; i++)
        {
            if (tag.Equals(ReplacedTags[i], StringComparison.Ordinal))
            {
                return i switch
                {
                    0 => ReplacedKind.Image,
                    1 => ReplacedKind.Video,
                    2 => ReplacedKind.Canvas,
                    6 => ReplacedKind.Svg,
                    _ => ReplacedKind.Embedded,
                };
            }
        }

        return ReplacedKind.None;
    }

    internal int InternTag(string tag)
    {
        if (_tagIds.TryGetValue(tag, out int id))
        {
            return id;
        }

        if (_tags.Count >= 1 << 16)
        {
            return 0; // hostile input with endless distinct tag names: later ones are anonymous
        }

        id = _tags.Count;
        _tags.Add(tag);
        _tagIds[tag] = id;
        return id;
    }

    internal void ReportLimit(string message)
    {
        if (!_limitReported)
        {
            _limitReported = true;
            Styles.Diagnostics.Add(DiagnosticKind.LimitExceeded, message);
        }
    }

    private void Grow()
    {
        int n = _kind.Length * 2;
        Array.Resize(ref _kind, n);
        Array.Resize(ref _parent, n);
        Array.Resize(ref _first, n);
        Array.Resize(ref _last, n);
        Array.Resize(ref _next, n);
        Array.Resize(ref _depth, n);
        Array.Resize(ref _style, n);
        Array.Resize(ref _source, n);
        Array.Resize(ref _tag, n);
        Array.Resize(ref _textStart, n);
        Array.Resize(ref _textLength, n);
        Array.Resize(ref _replaced, n);
        Array.Resize(ref _naturalW, n);
        Array.Resize(ref _span, n);
        Array.Resize(ref _naturalH, n);
    }
}
