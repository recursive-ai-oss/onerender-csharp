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
        _cellPadding.Clear();
        _alignHint.Clear();
        _tableBorder.Clear();
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
        if (element.Namespace == ElementNamespace.Html && tag.Length is 2 or 3 or 5 or 8 && IsSpanTag(tag))
        {
            ApplySpanAttributes(element, idx, tag);
        }

        if (element.Namespace == ElementNamespace.Html && (tag.Equals("table", StringComparison.Ordinal) || tag.Equals("tr", StringComparison.Ordinal) || tag.Equals("td", StringComparison.Ordinal) || tag.Equals("th", StringComparison.Ordinal)))
        {
            ApplyTableHints(element, idx, tag);
        }
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

    // HTML Standard §15.3.8 (tables): the width, height, cellspacing and cellpadding attributes act as presentational hints, below any author
    // rule. The cascade here does not carry hints, so a hint applies where the computed value is still the user agent's default.
    private void ApplyTableHints(Node element, int idx, string tag)
    {
        bool isTable = tag.Length == 5;
        ref readonly ComputedValues v0 = ref _tree.Styles[_tree.StyleOf(idx)];
        ComputedValues v = v0;
        bool changed = false;
        foreach (Attr a in element.Attributes)
        {
            ReadOnlySpan<char> name = a.LocalName;
            if (name.Equals("width", StringComparison.Ordinal) && v.Width.Kind == LengthKind.Auto && TryParseHintDimension(a.Value, out Length w))
            {
                v.Width = w;
                changed = true;
            }
            else if (name.Equals("height", StringComparison.Ordinal) && v.Height.Kind == LengthKind.Auto && TryParseHintDimension(a.Value, out Length h))
            {
                v.Height = h;
                changed = true;
            }
            else if (isTable && name.Equals("cellspacing", StringComparison.Ordinal) && v.BorderSpacingH == 2 && v.BorderSpacingV == 2 && TryParseHintDimension(a.Value, out Length s) && s.Kind == LengthKind.Px)
            {
                v.BorderSpacingH = v.BorderSpacingV = s.Value;
                changed = true;
            }
            else if (isTable && name.Equals("border", StringComparison.Ordinal))
            {
                ApplyBorderHint(a.Value, idx, ref v, ref changed);
            }
            else if (name.Equals("align", StringComparison.Ordinal))
            {
                ApplyAlignHint(a.Value, idx, isTable, ref v, ref changed);
            }
            else if (isTable && name.Equals("cellpadding", StringComparison.Ordinal) && TryParseHintDimension(a.Value, out Length cp) && cp.Kind == LengthKind.Px)
            {
                _cellPadding[idx] = cp.Value;
            }
        }

        // The parent cell's align="center" or "right" also places a table child, the way a centered block child sits in a cell.
        // The cell's align attribute aligns the table's box, not the text inside the table: a table directly in such a cell starts over
        // with the initial text-align (evidence: docs/layout-conventions.md, align on a cell around a table).
        if (isTable && _alignHint.TryGetValue(_tree.ParentOf(idx), out TextAlignKind hinted) && v.TextAlign == hinted)
        {
            v.TextAlign = TextAlignKind.Start;
            changed = true;
        }

        if (isTable && _alignHint.TryGetValue(_tree.ParentOf(idx), out TextAlignKind parentAlign) && v.MarginLeft.Kind == LengthKind.Px && v.MarginLeft.Value == 0
            && v.MarginRight.Kind == LengthKind.Px && v.MarginRight.Value == 0)
        {
            v.MarginLeft = Length.Auto;
            v.MarginRight = parentAlign == TextAlignKind.Center ? Length.Auto : Length.Zero;
            changed = true;
        }

        if (!isTable && tag.Length == 2 && tag[1] != 'r' && NearestTable(idx) is int owner and >= 0 && _tableBorder.TryGetValue(owner, out int tb) && tb == 0)
        {
            // The user agent sheet gives cells an inset border "only if the table's border is not equivalent to zero". Sides the author
            // styled keep their own border.
            if (v.BorderTopStyle == BorderStyleKind.Inset)
            {
                v.BorderTopStyle = BorderStyleKind.None;
                v.BorderTopWidth = 0;
                changed = true;
            }

            if (v.BorderRightStyle == BorderStyleKind.Inset)
            {
                v.BorderRightStyle = BorderStyleKind.None;
                v.BorderRightWidth = 0;
                changed = true;
            }

            if (v.BorderBottomStyle == BorderStyleKind.Inset)
            {
                v.BorderBottomStyle = BorderStyleKind.None;
                v.BorderBottomWidth = 0;
                changed = true;
            }

            if (v.BorderLeftStyle == BorderStyleKind.Inset)
            {
                v.BorderLeftStyle = BorderStyleKind.None;
                v.BorderLeftWidth = 0;
                changed = true;
            }
        }

        if (!isTable && IsUserAgentPadding(in v))
        {
            double pad = TablePaddingHint(idx);
            if (pad >= 0)
            {
                v.PaddingTop = v.PaddingRight = v.PaddingBottom = v.PaddingLeft = new Length(LengthKind.Px, pad);
                changed = true;
            }
        }

        if (changed)
        {
            _tree.ReplaceStyle(idx, _tree.Styles.Derive(_tree.StyleOf(idx), in v));
        }
    }

    // HTML Standard §15.3.8: a table's border attribute is its border width in pixels; zero turns the user agent's outset border off, and
    // a value that does not parse counts as one.
    private void ApplyBorderHint(ReadOnlySpan<char> value, int idx, ref ComputedValues v, ref bool changed)
    {
        ReadOnlySpan<char> t = value.Trim();
        int n = 1;
        if (!t.IsEmpty && t.Length <= 6 && int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
        {
            n = parsed;
        }

        _tableBorder[idx] = n;
        if (v.BorderTopStyle != BorderStyleKind.Outset)
        {
            return;
        }

        if (n == 0)
        {
            v.BorderTopStyle = v.BorderRightStyle = v.BorderBottomStyle = v.BorderLeftStyle = BorderStyleKind.None;
            v.BorderTopWidth = v.BorderRightWidth = v.BorderBottomWidth = v.BorderLeftWidth = 0;
        }
        else
        {
            v.BorderTopWidth = v.BorderRightWidth = v.BorderBottomWidth = v.BorderLeftWidth = n;
        }

        changed = true;
    }

    private int NearestTable(int idx)
    {
        for (int p = _tree.ParentOf(idx); p >= 0; p = _tree.ParentOf(p))
        {
            if (_tree.KindOf(p) == (byte)StyledNodeKind.Element && _tree.TagNameOf(p).Equals("table", StringComparison.Ordinal))
            {
                return p;
            }
        }

        return -1;
    }

    // HTML Standard §15.3.8: align on a table floats it left or right or centers it with auto margins; on a row or cell it sets text-align.
    private void ApplyAlignHint(ReadOnlySpan<char> value, int idx, bool isTable, ref ComputedValues v, ref bool changed)
    {
        TextAlignKind kind;
        if (value.Equals("center", StringComparison.OrdinalIgnoreCase) || value.Equals("middle", StringComparison.OrdinalIgnoreCase))
        {
            kind = TextAlignKind.Center;
        }
        else if (value.Equals("left", StringComparison.OrdinalIgnoreCase))
        {
            kind = TextAlignKind.Left;
        }
        else if (value.Equals("right", StringComparison.OrdinalIgnoreCase))
        {
            kind = TextAlignKind.Right;
        }
        else
        {
            return;
        }

        if (isTable)
        {
            if (kind == TextAlignKind.Center)
            {
                v.MarginLeft = v.MarginRight = Length.Auto;
            }
            else
            {
                v.Floating = kind == TextAlignKind.Left ? FloatKind.Left : FloatKind.Right;
            }
        }
        else
        {
            v.TextAlign = kind;
            _alignHint[idx] = kind;
        }

        changed = true;
    }

    private static bool IsUserAgentPadding(in ComputedValues v) =>
        v.PaddingTop.Kind == LengthKind.Px && v.PaddingTop.Value == 1 && v.PaddingRight.Kind == LengthKind.Px && v.PaddingRight.Value == 1
        && v.PaddingBottom.Kind == LengthKind.Px && v.PaddingBottom.Value == 1 && v.PaddingLeft.Kind == LengthKind.Px && v.PaddingLeft.Value == 1;

    // The cellpadding of the nearest table ancestor, or -1.
    private double TablePaddingHint(int idx)
    {
        for (int p = _tree.ParentOf(idx); p >= 0; p = _tree.ParentOf(p))
        {
            if (_tree.KindOf(p) == (byte)StyledNodeKind.Element && _tree.TagNameOf(p).Equals("table", StringComparison.Ordinal))
            {
                return _cellPadding.TryGetValue(p, out double pad) ? pad : -1;
            }
        }

        return -1;
    }

    private static bool TryParseHintDimension(ReadOnlySpan<char> text, out Length value)
    {
        value = default;
        text = text.Trim();
        bool percent = !text.IsEmpty && text[^1] == '%';
        if (percent)
        {
            text = text[..^1];
        }

        if (text.IsEmpty || text.Length > 9 || !double.TryParse(text, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out double n) || n < 0)
        {
            return false;
        }

        value = new Length(percent ? LengthKind.Percent : LengthKind.Px, n);
        return true;
    }

    private readonly Dictionary<int, double> _cellPadding = [];
    private readonly Dictionary<int, TextAlignKind> _alignHint = [];
    private readonly Dictionary<int, int> _tableBorder = [];

    private static bool IsSpanTag(string tag) =>
        tag.Equals("td", StringComparison.Ordinal) || tag.Equals("th", StringComparison.Ordinal)
        || tag.Equals("col", StringComparison.Ordinal) || tag.Equals("colgroup", StringComparison.Ordinal);

    // HTML Standard §4.9.11 (processing model of tables): colspan and rowspan are non-negative integers; colspan is clamped to 1..1000,
    // rowspan to 0..65534. A column's or column group's span attribute follows the same rules as colspan.
    private void ApplySpanAttributes(Node element, int idx, string tag)
    {
        bool cell = tag.Length == 2 && tag[0] == 't';
        int cols = 1, rows = 1;
        foreach (Attr a in element.Attributes)
        {
            ReadOnlySpan<char> name = a.LocalName;
            if (cell && name.Equals("colspan", StringComparison.Ordinal))
            {
                cols = ParseNonNegative(a.Value, 1);
            }
            else if (cell && name.Equals("rowspan", StringComparison.Ordinal))
            {
                rows = ParseNonNegative(a.Value, 1);
            }
            else if (!cell && name.Equals("span", StringComparison.Ordinal))
            {
                cols = ParseNonNegative(a.Value, 1);
            }
        }

        _tree.SetSpans(idx, cols == 0 ? 1 : cols, rows);
    }

    // HTML Standard §2.3.4.1 rules for parsing non-negative integers, saturating at a large value.
    private static int ParseNonNegative(ReadOnlySpan<char> text, int fallback)
    {
        int i = 0;
        while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\f' or '\r')
        {
            i++;
        }

        if (i < text.Length && text[i] == '+')
        {
            i++;
        }

        if (i >= text.Length || !char.IsAsciiDigit(text[i]))
        {
            return fallback;
        }

        long v = 0;
        for (; i < text.Length && char.IsAsciiDigit(text[i]); i++)
        {
            v = Math.Min(1_000_000, (v * 10) + (text[i] - '0'));
        }

        return (int)v;
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
