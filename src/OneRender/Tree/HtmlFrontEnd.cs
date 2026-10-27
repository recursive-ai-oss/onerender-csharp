using OneRender.Style;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Tree;

/// <summary>
/// Fills a <see cref="StyledTree"/> from a OneHtml document and its cascade (<see cref="DocumentStyles"/>): one styled
/// node per element and non-empty text node, plus <c>::before</c>, <c>::after</c> and <c>::marker</c> nodes where the
/// pseudo-element styles ask for them. Subtrees of <c>display: none</c> elements and of replaced elements are left out.
/// </summary>
/// <remarks>The front end keeps its work stack between builds, so repeated builds allocate nothing once warm. Not thread-safe.</remarks>
public sealed class HtmlFrontEnd
{
    private readonly StyledTree _tree;
    private readonly OneHtmlSource _source = new();
    private Frame[] _stack = new Frame[64];

    /// <summary>Creates a front end that writes into <paramref name="tree"/>.</summary>
    /// <param name="tree">The tree to fill; <see cref="Build"/> clears it first.</param>
    public HtmlFrontEnd(StyledTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        _tree = tree;
    }

    /// <summary>Clears the tree and builds it from <paramref name="styles"/>.</summary>
    /// <param name="styles">The cascade of a parsed document. Its <c>StyleContext</c> (media, hover and focus state) is whatever the caller made it.</param>
    /// <param name="cancellationToken">Checked every few hundred nodes.</param>
    /// <exception cref="OperationCanceledException">Cancelled; the tree is partly built.</exception>
    public void Build(DocumentStyles styles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(styles);
        Document doc = styles.Document;
        _tree.Clear();
        _tree.SetDocument(doc, doc.NodeCount);
        Node rootElement = doc.DocumentElement;
        if (rootElement.IsNull)
        {
            return;
        }

        int rootIdx = AddElement(styles, rootElement, -1);
        if (rootIdx < 0 || !ShouldDescend(rootIdx))
        {
            return;
        }

        int top = 0;
        _stack[0] = new Frame(rootElement.FirstChild, rootIdx, rootElement);
        int counter = 0;
        while (top >= 0)
        {
            if ((++counter & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ref Frame f = ref _stack[top];
            Node child = f.Cursor;
            if (child.IsNull)
            {
                // Children done: ::after comes last (CSS Pseudo-Elements 4 §4).
                AddPseudo(styles, f.Owner, f.StyledParent, PseudoElementKind.After);
                top--;
                continue;
            }

            f.Cursor = child.NextSibling;
            int parentIdx = f.StyledParent;
            switch (child.Kind)
            {
                case NodeKind.Text:
                    ReadOnlySpan<char> data = child.Data;
                    if (!data.IsEmpty)
                    {
                        _tree.AppendText(parentIdx, data);
                    }

                    break;
                case NodeKind.Element:
                {
                    int idx = AddElement(styles, child, parentIdx);
                    if (idx >= 0 && ShouldDescend(idx))
                    {
                        if (top + 1 == _stack.Length)
                        {
                            Array.Resize(ref _stack, _stack.Length * 2);
                        }

                        _stack[++top] = new Frame(child.FirstChild, idx, child);
                    }

                    break;
                }
            }
        }
    }

    private bool ShouldDescend(int idx) =>
        _tree.ReplacedOf(idx) == ReplacedKind.None && !_tree.Styles[_tree.StyleOf(idx)].Display.IsNone;

    private int AddElement(DocumentStyles styles, Node element, int parentIdx)
    {
        ComputedStyle cs = styles.GetStyle(element);
        int idx = Create(StyledNodeKind.Element, cs, parentIdx, element.Index);
        if (idx < 0)
        {
            return -1;
        }

        string tag = element.LocalName;
        ReplacedKind replaced = element.Namespace switch
        {
            ElementNamespace.Html => StyledTree.ReplacedKindOf(tag),
            ElementNamespace.Svg => tag.Equals("svg", StringComparison.Ordinal) ? ReplacedKind.Svg : ReplacedKind.None,
            _ => ReplacedKind.None,
        };
        _tree.SetElementInfo(idx, tag, replaced);
        if (replaced != ReplacedKind.None)
        {
            ReadOnlySpan<char> src = SourceAttribute(element, replaced);
            _tree.SetDefaultNaturalSize(idx, replaced, src);
            _tree.SetReplacedSource(idx, src);
            ApplySizeAttributes(element, idx, replaced);
            return idx;
        }

        StyleTable table = _tree.Styles;
        if (table[_tree.StyleOf(idx)].Display.IsNone)
        {
            return idx;
        }

        if (table[_tree.StyleOf(idx)].Display.IsListItem)
        {
            ComputedStyle? marker = styles.GetPseudoStyle(element, PseudoElementKind.Marker);
            if (marker is null)
            {
                _tree.AddMarker(idx, default);
            }
            else
            {
                CreatePseudoNode(StyledNodeKind.Marker, marker, idx);
            }
        }

        AddPseudo(styles, element, idx, PseudoElementKind.Before);
        return idx;
    }

    private void AddPseudo(DocumentStyles styles, Node owner, int ownerIdx, PseudoElementKind kind)
    {
        ComputedStyle? ps = styles.GetPseudoStyle(owner, kind);
        if (ps is not null)
        {
            CreatePseudoNode(kind == PseudoElementKind.Before ? StyledNodeKind.Before : StyledNodeKind.After, ps, ownerIdx);
        }
    }

    private void CreatePseudoNode(StyledNodeKind kind, ComputedStyle cs, int ownerIdx)
    {
        StyleId sid = Resolve(cs, _tree.StyleOf(ownerIdx), true, false);
        ref readonly ComputedValues v = ref _tree.Styles[sid];
        if (kind != StyledNodeKind.Marker && (v.Display.IsNone || v.Content is ContentKind.Normal or ContentKind.None))
        {
            return; // CSS Generated Content 3 §2: no content, no box.
        }

        int idx = _tree.NewNode(kind, ownerIdx, sid, -1);
        if (idx >= 0 && kind != StyledNodeKind.Marker && v.Content == ContentKind.Text)
        {
            _tree.SetText(idx, v.ContentText);
        }
    }

    private int Create(StyledNodeKind kind, ComputedStyle cs, int parentIdx, int sourceIndex)
    {
        StyleId parentStyle = parentIdx < 0 ? default : _tree.StyleOf(parentIdx);
        StyleId sid = Resolve(cs, parentStyle, parentIdx >= 0, parentIdx < 0);
        int idx = _tree.NewNode(kind, parentIdx, sid, sourceIndex);
        if (idx >= 0 && parentIdx < 0)
        {
            _tree.Styles.NoteRoot(sid);
        }

        return idx;
    }

    private StyleId Resolve(ComputedStyle cs, StyleId parentStyle, bool hasParent, bool isRoot)
    {
        StyleTable table = _tree.Styles;
        if (table.TryGetCached(cs, 0, parentStyle, out StyleId sid))
        {
            return sid;
        }

        _source.Reset(cs);
        return table.Compute(cs, 0, _source, parentStyle, hasParent, isRoot);
    }

    private static ReadOnlySpan<char> SourceAttribute(Node element, ReplacedKind kind)
    {
        string name = kind == ReplacedKind.Embedded && element.LocalName == "object" ? "data" : "src";
        foreach (Attr a in element.Attributes)
        {
            if (a.LocalName.Equals(name, StringComparison.Ordinal))
            {
                return a.Value;
            }
        }

        return default;
    }

    // HTML Standard §15.4.2 / §4.12.5: a canvas's size is its width and height attributes (default 300 by 150).
    private void ApplySizeAttributes(Node element, int idx, ReplacedKind kind)
    {
        if (kind != ReplacedKind.Canvas)
        {
            return;
        }

        double w = 300, h = 150;
        foreach (Attr a in element.Attributes)
        {
            if (a.LocalName.Equals("width", StringComparison.Ordinal) && TryParseDimension(a.Value, out double pw))
            {
                w = pw;
            }
            else if (a.LocalName.Equals("height", StringComparison.Ordinal) && TryParseDimension(a.Value, out double ph))
            {
                h = ph;
            }
        }

        _tree.SetNatural(idx, w, h);
    }

    private static bool TryParseDimension(ReadOnlySpan<char> text, out double value)
    {
        value = 0;
        text = text.Trim();
        if (text.IsEmpty || text.Length > 9)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            value = (value * 10) + (c - '0');
        }

        return true;
    }

    private struct Frame(Node cursor, int styledParent, Node owner)
    {
        public Node Cursor = cursor;
        public int StyledParent = styledParent;
        public Node Owner = owner;
    }
}
