using System.Buffers;
using System.Globalization;
using System.Text.Json;
using OneRender.Interaction;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Accessibility.Rendered;

namespace OneRender.Accessibility;

/// <summary>
/// Writes a laid-out and painted page as a <c>DOMSnapshot.captureSnapshot</c> result held in memory. OneHtml.Accessibility reads
/// facts through that shape only, so this is the one way in; nothing here is specific to a browser except the field names.
/// </summary>
internal sealed class SnapshotWriter
{
    private readonly HtmlPage _page;
    private readonly LayoutQueries _queries;
    private readonly PaintTrace _trace;
    private readonly BackgroundBlender _blender;
    private readonly FactsOptions _options;
    private readonly DomSnapshotLimits _limits;
    private readonly Document _doc;
    private readonly StyledTree _styled;
    private readonly FragmentTree _fragments;
    private readonly string[] _styleNames;

    // The string table.
    private readonly List<string> _strings = [];
    private readonly Dictionary<string, int> _stringIndex = new(StringComparer.Ordinal);

    // Fragment geometry in document coordinates, and the text fragments of every styled node, in layout order (line by line,
    // left to right), which is the order a browser lists text boxes in.
    private double[] _absX = [];
    private double[] _absY = [];
    private int[] _textFirst = [];
    private int[] _textCount = [];
    private Fragment[] _textFragments = [];

    // The node table.
    private readonly List<int> _parent = [];
    private readonly List<int> _type = [];
    private readonly List<int> _name = [];
    private readonly List<int> _value = [];
    private readonly List<int> _attrStart = [];
    private readonly List<int> _attrs = [];
    private readonly List<int> _pseudoNode = [];
    private readonly List<int> _pseudoType = [];
    private readonly List<int> _inputValueNode = [];
    private readonly List<int> _inputValue = [];
    private readonly List<int> _textValueNode = [];
    private readonly List<int> _textValue = [];
    private readonly List<int> _checkedNode = [];
    private readonly List<int> _selectedNode = [];
    private readonly HashSet<int> _selectedOptions = [];

    // The layout table.
    private readonly List<int> _lNode = [];
    private readonly List<double> _lBounds = [];
    private readonly List<int> _lText = [];
    private readonly List<int> _lStyleAt = [];
    private readonly List<int> _lStyles = [];
    private readonly List<int> _lPaint = [];
    private readonly List<int> _lBlended = [];
    private readonly List<double> _lOpacity = [];
    private readonly List<int> _lRectAt = [];
    private readonly List<int> _lRects = [];
    private readonly List<int> _stacking = [];
    private readonly List<int> _tbLayout = [];
    private readonly List<double> _tbBounds = [];
    private readonly List<int> _tbStart = [];
    private readonly List<int> _tbLength = [];

    private struct Frame
    {
        public Node Next;
        public int ParentIndex;
        public StyledNode Styled;
        public StyledNode Cursor;
    }

    public SnapshotWriter(HtmlPage page, LayoutQueries queries, DisplayList list, PaintTrace trace, FactsOptions options)
    {
        _page = page;
        _queries = queries;
        _trace = trace;
        _options = options;
        _limits = options.Limits ?? DomSnapshotLimits.Default;
        _doc = page.Document;
        _styled = page.Styled;
        _fragments = page.Fragments;
        _blender = new BackgroundBlender(list, Math.Max(1, options.BackgroundScanLimit));
        _styleNames = [.. options.ComputedStyles];
    }

    public IReadOnlyList<string> StyleNames => _styleNames;

    private int Str(string s)
    {
        if (_stringIndex.TryGetValue(s, out int i))
        {
            return i;
        }

        i = _strings.Count;
        _strings.Add(s);
        _stringIndex[s] = i;
        return i;
    }

    private static DomSnapshotFormatException Limit(string name, long limit, long actual) =>
        new("The page is over the limit " + name + " (" + limit.ToString(CultureInfo.InvariantCulture) + "): " + actual.ToString(CultureInfo.InvariantCulture) + ".");

    // ------------------------------------------------------------------ fragments

    private void IndexFragments()
    {
        int n = _fragments.Count;
        _absX = new double[n];
        _absY = new double[n];
        _textFirst = new int[_styled.Count + 1];
        _textCount = new int[_styled.Count + 1];
        if (n == 0)
        {
            return;
        }

        // Absolute origins, and the number of text fragments per styled node, by an explicit pre-order walk.
        Fragment root = _fragments.Root;
        Rect rb = root.BorderBox;
        _absX[root.Index] = rb.X;
        _absY[root.Index] = rb.Y;
        int total = 0;
        for (Fragment f = root; !f.IsNull;)
        {
            if (f.Kind == FragmentKind.Text && f.TextLength > 0 && TextOwner(f) is int owner and >= 0)
            {
                _textCount[owner]++;
                total++;
            }

            Fragment c = f.FirstChild;
            if (!c.IsNull)
            {
                SetAbs(c, f);
                f = c;
                continue;
            }

            while (!f.IsNull)
            {
                Fragment next = f.NextSibling;
                if (!next.IsNull)
                {
                    SetAbs(next, f.Parent);
                    f = next;
                    break;
                }

                f = f.Parent;
            }
        }

        int sum = 0;
        for (int i = 0; i < _textFirst.Length; i++)
        {
            _textFirst[i] = sum;
            sum += _textCount[i];
            _textCount[i] = 0;
        }

        _textFragments = new Fragment[total];
        for (Fragment f = root; !f.IsNull;)
        {
            if (f.Kind == FragmentKind.Text && f.TextLength > 0 && TextOwner(f) is int owner and >= 0)
            {
                _textFragments[_textFirst[owner] + _textCount[owner]++] = f;
            }

            Fragment c = f.FirstChild;
            if (!c.IsNull)
            {
                f = c;
                continue;
            }

            while (!f.IsNull)
            {
                Fragment next = f.NextSibling;
                if (!next.IsNull)
                {
                    f = next;
                    break;
                }

                f = f.Parent;
            }
        }
    }

    private void SetAbs(Fragment child, Fragment parent)
    {
        Rect r = child.BorderBox;
        _absX[child.Index] = _absX[parent.Index] + r.X;
        _absY[child.Index] = _absY[parent.Index] + r.Y;
    }

    private static int TextOwner(Fragment f)
    {
        Box box = f.Box;
        return box.IsNull || box.Kind != BoxKind.Text ? -1 : box.Node.Index;
    }

    private Rect Absolute(Fragment f)
    {
        Rect r = f.BorderBox;
        return new Rect(_absX[f.Index], _absY[f.Index], r.Width, r.Height);
    }

    // The union of the border boxes of every fragment of a node's box and its pieces, and the first fragment.
    private bool BoxBounds(StyledNode s, out Rect bounds, out Fragment first)
    {
        bounds = default;
        first = default;
        if (s.IsNull)
        {
            return false;
        }

        bool any = false;
        for (Box box = _page.Boxes.PrincipalBox(s); !box.IsNull; box = box.NextPiece)
        {
            for (Fragment f = _fragments.FragmentOf(box); !f.IsNull; f = f.NextOfBox)
            {
                Rect r = Absolute(f);
                if (!any)
                {
                    first = f;
                    bounds = r;
                    any = true;
                }
                else
                {
                    bounds = bounds.Union(r);
                }
            }
        }

        return any;
    }

    // ------------------------------------------------------------------ the node table

    private int AddNode(int parent, int type, string name, string? value)
    {
        if (_parent.Count >= _limits.MaxNodes)
        {
            throw Limit(nameof(DomSnapshotLimits.MaxNodes), _limits.MaxNodes, _parent.Count + 1L);
        }

        int index = _parent.Count;
        _parent.Add(parent);
        _type.Add(type);
        _name.Add(Str(name));
        _value.Add(value is null ? -1 : Str(value));
        _attrStart.Add(_attrs.Count);
        return index;
    }

    private void AddAttributes(Node element)
    {
        foreach (Attr a in element.Attributes)
        {
            _attrs.Add(Str(a.QualifiedName));
            _attrs.Add(Str(a.Value.ToString()));
        }
    }

    private void AddFormState(Node element, int index)
    {
        if (element.Namespace != ElementNamespace.Html)
        {
            return;
        }

        string tag = element.LocalName;
        if (string.Equals(tag, "input", StringComparison.Ordinal))
        {
            // A browser lists a value for every input: the value attribute, the default "on" of a check box or radio button, or empty.
            string type = (element.GetAttribute("type") ?? string.Empty).ToLowerInvariant();
            bool toggle = type is "checkbox" or "radio";
            _inputValueNode.Add(index);
            _inputValue.Add(Str(element.GetAttribute("value") ?? (toggle ? "on" : string.Empty)));
            if (toggle && element.HasAttribute("checked"))
            {
                _checkedNode.Add(index);
            }
        }
        else if (string.Equals(tag, "textarea", StringComparison.Ordinal))
        {
            // HTML §4.10.11: the raw value is the text content, without a newline that directly follows the start tag.
            string text = element.FirstChild.IsNull ? string.Empty : TextContent(element);
            _textValueNode.Add(index);
            _textValue.Add(Str(text));
        }
        else if (string.Equals(tag, "select", StringComparison.Ordinal))
        {
            MarkSelectedOptions(element);
        }
        else if (string.Equals(tag, "option", StringComparison.Ordinal) && _selectedOptions.Contains(element.Index))
        {
            _selectedNode.Add(index);
        }
    }

    // HTML §4.10.7 selectedness: options with the selected attribute are selected; a select that shows one row and is not
    // multiple has exactly one selected option, the last one with the attribute or else the first that is not disabled.
    private void MarkSelectedOptions(Node select)
    {
        bool multiple = select.HasAttribute("multiple");
        bool oneRow = !multiple && !(int.TryParse(select.GetAttribute("size"), NumberStyles.None, CultureInfo.InvariantCulture, out int size) && size > 1);
        Node lastSelected = default, firstEnabled = default;
        for (Node n = select.FirstChild; !n.IsNull;)
        {
            if (n.IsElement && n.Namespace == ElementNamespace.Html && string.Equals(n.LocalName, "option", StringComparison.Ordinal))
            {
                if (n.HasAttribute("selected"))
                {
                    lastSelected = n;
                    if (multiple)
                    {
                        _selectedOptions.Add(n.Index);
                    }
                }

                if (firstEnabled.IsNull && !n.HasAttribute("disabled"))
                {
                    firstEnabled = n;
                }
            }

            if (!n.FirstChild.IsNull)
            {
                n = n.FirstChild;
                continue;
            }

            while (!n.IsNull && n.NextSibling.IsNull)
            {
                n = n.Parent;
                if (n.Equals(select))
                {
                    n = default;
                }
            }

            if (!n.IsNull)
            {
                n = n.NextSibling;
            }
        }

        if (oneRow)
        {
            Node chosen = lastSelected.IsNull ? firstEnabled : lastSelected;
            if (!chosen.IsNull)
            {
                _selectedOptions.Add(chosen.Index);
            }
        }
    }

    private static string TextContent(Node element)
    {
        var sb = new System.Text.StringBuilder();
        for (Node c = element.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == NodeKind.Text)
            {
                sb.Append(c.Data);
            }
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------ the walk

    private void Walk(CancellationToken ct)
    {
        int docIndex = AddNode(-1, 9, "#document", null);
        AddDocumentItem(docIndex);
        var stack = new Frame[32];
        int top = 0;
        stack[0] = new Frame { Next = _doc.Root.FirstChild, ParentIndex = docIndex };
        int counter = 0;
        while (top >= 0)
        {
            if ((++counter & 255) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            ref Frame f = ref stack[top];
            Node child = f.Next;
            if (child.IsNull)
            {
                if (!f.Styled.IsNull)
                {
                    StyledNode last = f.Styled.LastChild;
                    if (!last.IsNull && last.Kind == StyledNodeKind.After)
                    {
                        AddPseudo(last, f.ParentIndex);
                    }
                }

                top--;
                continue;
            }

            f.Next = child.NextSibling;
            int parentIndex = f.ParentIndex;
            switch (child.Kind)
            {
                case NodeKind.Element:
                {
                    StyledNode s = _styled.FindBySource(child.Index);
                    int index = AddElement(child, parentIndex, s);
                    for (StyledNode p = s.IsNull ? default : s.FirstChild; !p.IsNull && p.Kind is StyledNodeKind.Marker or StyledNodeKind.Before; p = p.NextSibling)
                    {
                        AddPseudo(p, index);
                    }

                    if (top + 1 == stack.Length)
                    {
                        Array.Resize(ref stack, stack.Length * 2);
                    }

                    stack[++top] = new Frame { Next = child.FirstChild, ParentIndex = index, Styled = s, Cursor = s.IsNull ? default : s.FirstChild };
                    break;
                }

                case NodeKind.Text:
                    AddText(child, parentIndex, ref f);
                    break;
                case NodeKind.Comment:
                    AddNode(parentIndex, 8, "#comment", child.Data.ToString());
                    break;
                case NodeKind.ProcessingInstruction:
                    AddNode(parentIndex, 7, child.LocalName, child.Data.ToString());
                    break;
                case NodeKind.DocumentType:
                    AddNode(parentIndex, 10, child.LocalName, null);
                    break;
            }
        }
    }

    private int AddElement(Node element, int parentIndex, StyledNode s)
    {
        string local = element.LocalName;
        int index = AddNode(parentIndex, 1, element.Namespace == ElementNamespace.Html ? local.ToUpperInvariant() : local, null);
        AddAttributes(element);
        AddFormState(element, index);
        if (BoxBounds(s, out Rect bounds, out Fragment first))
        {
            AddElementItem(element, index, s, first, bounds);
        }

        return index;
    }

    private void AddText(Node text, int parentIndex, ref Frame frame)
    {
        ReadOnlySpan<char> data = text.Data;
        int index = AddNode(parentIndex, 3, "#text", data.ToString());
        if (data.IsEmpty)
        {
            return;
        }

        // The styled tree has one text node for each non-empty text node, in order, among the other styled children.
        StyledNode c = frame.Cursor;
        while (!c.IsNull && c.Kind != StyledNodeKind.Text)
        {
            c = c.NextSibling;
        }

        if (c.IsNull)
        {
            return;
        }

        frame.Cursor = c.NextSibling;
        StyledNode owner = frame.Styled;
        BoxBounds(owner, out _, out Fragment ownerFragment);
        EmitText(index, c, TextTransforms.Apply(data.ToString(), c.Values.TextTransform), owner, ownerFragment);
    }

    private void AddPseudo(StyledNode p, int hostIndex)
    {
        string type = p.Kind switch { StyledNodeKind.Marker => "marker", StyledNodeKind.Before => "before", _ => "after" };
        int index = AddNode(hostIndex, 1, "::" + type, null);
        _pseudoNode.Add(index);
        _pseudoType.Add(Str(type));
        if (BoxBounds(p, out Rect bounds, out Fragment first))
        {
            AddBoxItem(index, p, first, bounds, -1, false);
            if (p.Kind != StyledNodeKind.Marker)
            {
                // The generated text is laid out by an anonymous box of its own, listed as a second item of the same node.
                EmitText(index, p, p.Text.ToString(), p, first);
            }
        }
    }

    // ------------------------------------------------------------------ layout items

    private void AddDocumentItem(int node)
    {
        _lNode.Add(node);
        AddBounds(new Rect(0, 0, _page.Options.ViewportWidth, _page.Options.ViewportHeight));
        _lText.Add(-1);
        _lStyleAt.Add(-1);
        _lPaint.Add(0);
        _lBlended.Add(-1);
        _lOpacity.Add(1);
        _lRectAt.Add(-1);
        _stacking.Add(0);
    }

    private void AddBounds(Rect r)
    {
        _lBounds.Add(Finite(r.X));
        _lBounds.Add(Finite(r.Y));
        _lBounds.Add(Finite(r.Width));
        _lBounds.Add(Finite(r.Height));
    }

    private static double Finite(double v) => double.IsFinite(v) ? v : 0;

    private void AddElementItem(Node element, int node, StyledNode s, Fragment first, Rect bounds)
    {
        int rects = _lRects.Count;
        if (element.Namespace == ElementNamespace.Html && element.LocalName is "br" or "wbr")
        {
            // A line break has no box of its own to measure in CSSOM View.
            for (int k = 0; k < 12; k++)
            {
                _lRects.Add(0);
            }

            AddBoxItem(node, s, first, bounds, rects, false);
            return;
        }

        _lRects.Add(_queries.OffsetLeft(element));
        _lRects.Add(_queries.OffsetTop(element));
        _lRects.Add(_queries.OffsetWidth(element));
        _lRects.Add(_queries.OffsetHeight(element));
        _lRects.Add(_queries.ClientLeft(element));
        _lRects.Add(_queries.ClientTop(element));
        _lRects.Add(_queries.ClientWidth(element));
        _lRects.Add(_queries.ClientHeight(element));
        _lRects.Add((int)Math.Round(_queries.ScrollLeft(element), MidpointRounding.AwayFromZero));
        _lRects.Add((int)Math.Round(_queries.ScrollTop(element), MidpointRounding.AwayFromZero));
        _lRects.Add(_queries.ScrollWidth(element));
        _lRects.Add(_queries.ScrollHeight(element));
        AddBoxItem(node, s, first, bounds, rects, true);
    }

    private void AddBoxItem(int node, StyledNode s, Fragment first, Rect bounds, int rects, bool ownText)
    {
        _lNode.Add(node);
        AddBounds(bounds);
        _lText.Add(-1);
        AddStyles(s, first);
        _lPaint.Add(LayerOf(first));
        _lRectAt.Add(rects);
        if (_trace.IsStackingContext(first.Index))
        {
            _stacking.Add(_lNode.Count - 1);
        }

        if (ownText && TextBackground(s, out int blended, out double opacity))
        {
            _lBlended.Add(blended);
            _lOpacity.Add(opacity);
        }
        else
        {
            _lBlended.Add(-1);
            _lOpacity.Add(1);
        }
    }

    private void AddStyles(StyledNode s, Fragment fragment)
    {
        if (_styleNames.Length == 0)
        {
            _lStyleAt.Add(-1);
            return;
        }

        _lStyleAt.Add(_lStyles.Count);
        foreach (string name in _styleNames)
        {
            string? value = ComputedStyleSerializer.Get(s, name, fragment);
            _lStyles.Add(value is null ? -1 : Str(value));
        }
    }

    private int LayerOf(Fragment f)
    {
        for (; !f.IsNull; f = f.Parent)
        {
            int layer = _trace.LayerOf(f.Index);
            if (layer != 0)
            {
                return layer;
            }
        }

        return 1;
    }

    // One layout item for the laid-out text of a text node (or of generated content), and its text boxes.
    private void EmitText(int node, StyledNode textNode, string layoutText, StyledNode styleOwner, Fragment styleFragment)
    {
        int count = _textCount[textNode.Index];
        if (count == 0)
        {
            return;
        }

        int at = _textFirst[textNode.Index];
        int item = _lNode.Count;
        bool any = false;
        Rect union = default;
        WhiteSpaceCollapseKind collapse = textNode.Values.WhiteSpaceCollapse;
        bool keepBreaks = collapse is WhiteSpaceCollapseKind.Preserve or WhiteSpaceCollapseKind.PreserveBreaks or WhiteSpaceCollapseKind.BreakSpaces;
        for (int k = 0; k < count; k++)
        {
            Fragment f = _textFragments[at + k];
            int start = f.TextStart;
            int length = f.TextLength;
            if (start < 0 || length <= 0 || start + length > layoutText.Length)
            {
                continue;
            }

            Rect r = Absolute(f);
            union = any ? union.Union(r) : r;
            any = true;
            AddTextBox(item, r, start, length);
            // A forced break is a text box of its own, of no width, at the end of the line it ends.
            int end = start + length;
            if (keepBreaks && end < layoutText.Length && layoutText[end] == '\n' && !(k + 1 < count && _textFragments[at + k + 1].TextStart == end))
            {
                AddTextBox(item, new Rect(r.Right, r.Y, 0, r.Height), end, 1);
            }
        }

        if (!any)
        {
            return;
        }

        _lNode.Add(node);
        AddBounds(union);
        _lText.Add(Str(layoutText));
        AddStyles(styleOwner, styleFragment);
        _lPaint.Add(LayerOf(_textFragments[at]));
        _lRectAt.Add(-1);
        _lBlended.Add(-1);
        _lOpacity.Add(1);
    }

    private void AddTextBox(int item, Rect r, int start, int length)
    {
        _tbLayout.Add(item);
        _tbBounds.Add(Finite(r.X));
        _tbBounds.Add(Finite(r.Y));
        _tbBounds.Add(Finite(r.Width));
        _tbBounds.Add(Finite(r.Height));
        _tbStart.Add(start);
        _tbLength.Add(length);
    }

    // The color behind an element's own text and the opacity it is painted with, when it has text that is painted: a character
    // that is not white space, in a box that is visible.
    private bool TextBackground(StyledNode s, out int blended, out double opacity)
    {
        blended = -1;
        opacity = 1;
        // A browser works this out only for an element whose children are all text (and generated content).
        for (StyledNode c = s.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == StyledNodeKind.Element)
            {
                return false;
            }
        }

        for (StyledNode c = s.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind != StyledNodeKind.Text || c.Values.Visibility != VisibilityKind.Visible)
            {
                continue;
            }

            ReadOnlySpan<char> text = c.Text;
            int at = _textFirst[c.Index];
            for (int k = 0; k < _textCount[c.Index]; k++)
            {
                Fragment f = _textFragments[at + k];
                int start = f.TextStart;
                int end = Math.Min(text.Length, start + f.TextLength);
                if (start < 0 || !HasNonSpace(text[start..Math.Max(start, end)]))
                {
                    continue;
                }

                int item = _trace.FirstItemOf(f.Index);
                if (item < 0)
                {
                    continue;
                }

                Rect r = Absolute(f);
                string? color = _blender.BlendedAt(item, (float)(r.X + (r.Width / 2)), (float)(r.Y + (r.Height / 2)));
                blended = color is null ? -1 : Str(color);
                opacity = _blender.OpacityAt(item);
                return true;
            }
        }

        return false;
    }

    private static bool HasNonSpace(ReadOnlySpan<char> text)
    {
        foreach (char ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------ output

    public byte[] Write(CancellationToken ct)
    {
        IndexFragments();
        Walk(ct);
        var buffer = new ArrayBufferWriter<byte>(1 << 16);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("schema", "onerender-facts/1");
            w.WriteStartObject("format");
            w.WriteString("domain", "DOMSnapshot");
            w.WriteString("method", "captureSnapshot");
            w.WriteString("protocolVersion", DomSnapshot.PinnedProtocolVersion);
            w.WriteString("product", OneRender.EngineInfo.Name);
            w.WriteStartObject("parameters");
            w.WriteStartArray("computedStyles");
            foreach (string name in _styleNames)
            {
                w.WriteStringValue(name);
            }

            w.WriteEndArray();
            w.WriteBoolean("includePaintOrder", true);
            w.WriteBoolean("includeDOMRects", true);
            w.WriteBoolean("includeBlendedBackgroundColors", true);
            w.WriteBoolean("includeTextColorOpacities", true);
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteStartObject("snapshot");
            w.WriteStartArray("documents");
            WriteDocument(w);
            w.WriteEndArray();
            WriteStrings(w);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        if (buffer.WrittenCount > _limits.MaxInputBytes)
        {
            throw Limit(nameof(DomSnapshotLimits.MaxInputBytes), _limits.MaxInputBytes, buffer.WrittenCount);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private void WriteStrings(Utf8JsonWriter w)
    {
        w.WriteStartArray("strings");
        foreach (string s in _strings)
        {
            w.WriteStringValue(s);
        }

        w.WriteEndArray();
    }

    private void WriteDocument(Utf8JsonWriter w)
    {
        Node rootElement = _doc.DocumentElement;
        w.WriteStartObject();
        w.WriteNumber("documentURL", Str(_page.Options.BaseUrl ?? string.Empty));
        w.WriteNumber("baseURL", Str(_page.Options.BaseUrl ?? string.Empty));
        w.WriteNumber("encodingName", Str("UTF-8"));
        w.WriteNumber("frameId", Str("main"));
        WriteNodes(w);
        WriteLayout(w);
        w.WriteStartObject("textBoxes");
        WriteInts(w, "layoutIndex", _tbLayout);
        w.WriteStartArray("bounds");
        for (int i = 0; i < _tbLayout.Count; i++)
        {
            WriteRect(w, _tbBounds, i * 4);
        }

        w.WriteEndArray();
        WriteInts(w, "start", _tbStart);
        WriteInts(w, "length", _tbLength);
        w.WriteEndObject();
        w.WriteNumber("scrollOffsetX", Finite(_page.Scroll.ViewportX));
        w.WriteNumber("scrollOffsetY", Finite(_page.Scroll.ViewportY));
        w.WriteNumber("contentWidth", rootElement.IsNull ? _page.Options.ViewportWidth : _queries.ScrollWidth(rootElement));
        w.WriteNumber("contentHeight", rootElement.IsNull ? _page.Options.ViewportHeight : _queries.ScrollHeight(rootElement));
        w.WriteEndObject();
    }

    private void WriteNodes(Utf8JsonWriter w)
    {
        int n = _parent.Count;
        w.WriteStartObject("nodes");
        WriteInts(w, "parentIndex", _parent);
        WriteInts(w, "nodeType", _type);
        WriteInts(w, "nodeName", _name);
        WriteInts(w, "nodeValue", _value);
        w.WriteStartArray("backendNodeId");
        for (int i = 0; i < n; i++)
        {
            w.WriteNumberValue(i + 1);
        }

        w.WriteEndArray();
        w.WriteStartArray("attributes");
        for (int i = 0; i < n; i++)
        {
            int from = _attrStart[i];
            int to = i + 1 < n ? _attrStart[i + 1] : _attrs.Count;
            w.WriteStartArray();
            for (int k = from; k < to; k++)
            {
                w.WriteNumberValue(_attrs[k]);
            }

            w.WriteEndArray();
        }

        w.WriteEndArray();
        WriteSparse(w, "pseudoType", _pseudoNode, _pseudoType);
        WriteSparse(w, "inputValue", _inputValueNode, _inputValue);
        WriteSparse(w, "textValue", _textValueNode, _textValue);
        w.WriteStartObject("inputChecked");
        WriteInts(w, "index", _checkedNode);
        w.WriteEndObject();
        w.WriteStartObject("optionSelected");
        WriteInts(w, "index", _selectedNode);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteSparse(Utf8JsonWriter w, string name, List<int> index, List<int> value)
    {
        w.WriteStartObject(name);
        WriteInts(w, "index", index);
        WriteInts(w, "value", value);
        w.WriteEndObject();
    }

    private void WriteLayout(Utf8JsonWriter w)
    {
        int n = _lNode.Count;
        int stride = _styleNames.Length;
        w.WriteStartObject("layout");
        WriteInts(w, "nodeIndex", _lNode);
        w.WriteStartArray("styles");
        for (int i = 0; i < n; i++)
        {
            w.WriteStartArray();
            int at = _lStyleAt[i];
            if (at >= 0)
            {
                for (int k = 0; k < stride; k++)
                {
                    w.WriteNumberValue(_lStyles[at + k]);
                }
            }

            w.WriteEndArray();
        }

        w.WriteEndArray();
        w.WriteStartArray("bounds");
        for (int i = 0; i < n; i++)
        {
            WriteRect(w, _lBounds, i * 4);
        }

        w.WriteEndArray();
        WriteInts(w, "text", _lText);
        w.WriteStartObject("stackingContexts");
        WriteInts(w, "index", _stacking);
        w.WriteEndObject();
        WriteInts(w, "paintOrders", _lPaint);
        WriteRectColumn(w, "offsetRects", 0);
        WriteRectColumn(w, "scrollRects", 8);
        WriteRectColumn(w, "clientRects", 4);
        WriteInts(w, "blendedBackgroundColors", _lBlended);
        w.WriteStartArray("textColorOpacities");
        foreach (double o in _lOpacity)
        {
            // A browser keeps this as a 32-bit float.
            w.WriteNumberValue((double)(float)o);
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    // Offset, client and scroll rectangles were stored per item as twelve integers: offset, client, scroll.
    private void WriteRectColumn(Utf8JsonWriter w, string name, int skip)
    {
        w.WriteStartArray(name);
        for (int i = 0; i < _lNode.Count; i++)
        {
            w.WriteStartArray();
            int at = _lRectAt[i];
            if (at >= 0)
            {
                for (int k = 0; k < 4; k++)
                {
                    w.WriteNumberValue(_lRects[at + skip + k]);
                }
            }

            w.WriteEndArray();
        }

        w.WriteEndArray();
    }

    private static void WriteInts(Utf8JsonWriter w, string name, List<int> values)
    {
        w.WriteStartArray(name);
        foreach (int v in values)
        {
            w.WriteNumberValue(v);
        }

        w.WriteEndArray();
    }

    private static void WriteRect(Utf8JsonWriter w, List<double> values, int at)
    {
        w.WriteStartArray();
        for (int k = 0; k < 4; k++)
        {
            w.WriteNumberValue(values[at + k]);
        }

        w.WriteEndArray();
    }
}
