using System.Globalization;
using OneRender.Interaction;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Accessibility;

/// <summary>
/// Reads a laid-out and painted page and hands its boxes, text boxes, paint order and colors straight to OneHtml.Accessibility's
/// <see cref="RenderedFactsBuilder"/>. Nodes are numbered the way a browser's <c>DOMSnapshot</c> numbers them (document order,
/// pseudo-elements included) so that backend ids and snapshot indexes mean the same as in facts read from a capture.
/// </summary>
internal sealed class FactsWriter
{
    private readonly HtmlPage _page;
    private readonly LayoutQueries _queries;
    private readonly PaintTrace _trace;
    private readonly BackgroundBlender _blender;
    private readonly FactsOptions _options;
    private readonly Document _doc;
    private readonly StyledTree _styled;
    private readonly FragmentTree _fragments;
    private readonly string[] _styleNames;
    private readonly RenderedFactsBuilder _builder;
    private readonly StyleContext? _context;
    private int _snapshotCount;

    // Fragment geometry in document coordinates, and the text fragments of every styled node, in layout order (line by line,
    // left to right), which is the order a browser lists text boxes in.
    private double[] _absX = [];
    private double[] _absY = [];
    private int[] _textFirst = [];
    private int[] _textCount = [];
    private Fragment[] _textFragments = [];
    private readonly HashSet<int> _selectedOptions = [];
    private readonly List<TextBox> _textBoxScratch = [];

    private struct Frame
    {
        public Node Element;
        public Node Next;
        public int ParentIndex;
        public StyledNode Styled;
        public StyledNode Cursor;
    }

    public FactsWriter(HtmlPage page, LayoutQueries queries, DisplayList list, PaintTrace trace, FactsOptions options, StyleContext? context)
    {
        _page = page;
        _queries = queries;
        _trace = trace;
        _options = options;
        _context = context;
        _doc = page.Document;
        _styled = page.Styled;
        _fragments = page.Fragments;
        _blender = new BackgroundBlender(list, Math.Max(1, options.BackgroundScanLimit));
        _styleNames = [.. options.ComputedStyles];
        _builder = new RenderedFactsBuilder(_doc, options.Limits);
        _builder.SetComputedStyleNames(_styleNames);
    }

    private static double Finite(double v) => double.IsFinite(v) ? v : 0;

    private static LayoutRect Finite(Rect r) => new(Finite(r.X), Finite(r.Y), Finite(r.Width), Finite(r.Height));

    // Numbers a node the way the snapshot would, and records both numbers on the facts: the snapshot index and the backend id.
    private int Number(Node node)
    {
        int index = _snapshotCount++;
        if (!node.IsNull)
        {
            _builder.SetSnapshotNodeIndex(node, index);
            _builder.SetBackendNodeId(node, index + 1L);
        }

        return index;
    }

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

    // ------------------------------------------------------------------ form state

    private void AddFormState(Node element)
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
            _builder.SetInputValue(element, element.GetAttribute("value") ?? (toggle ? "on" : string.Empty));
            if (toggle && element.HasAttribute("checked"))
            {
                _builder.SetChecked(element, true);
            }
        }
        else if (string.Equals(tag, "textarea", StringComparison.Ordinal))
        {
            // HTML §4.10.11: the raw value is the text content, without a newline that directly follows the start tag.
            _builder.SetTextValue(element, element.FirstChild.IsNull ? string.Empty : TextContent(element));
        }
        else if (string.Equals(tag, "select", StringComparison.Ordinal))
        {
            MarkSelectedOptions(element);
        }
        else if (string.Equals(tag, "option", StringComparison.Ordinal) && _selectedOptions.Contains(element.Index))
        {
            _builder.SetOptionSelected(element, true);
        }
    }

    // ------------------------------------------------------------------ the walk

    private void Walk(CancellationToken ct)
    {
        int docIndex = Number(_doc.Root);
        _builder.AddBox(_doc.Root, new BoxInput
        {
            BorderBox = new LayoutRect(0, 0, _page.Options.ViewportWidth, _page.Options.ViewportHeight),
            PaintOrder = 0,
            IsStackingContext = true,
            TextColorOpacity = 1,
        });
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
                        AddPseudo(last, f.Element);
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
                    int index = AddElement(child, s);
                    for (StyledNode p = s.IsNull ? default : s.FirstChild; !p.IsNull && p.Kind is StyledNodeKind.Marker or StyledNodeKind.Before; p = p.NextSibling)
                    {
                        AddPseudo(p, child);
                    }

                    if (top + 1 == stack.Length)
                    {
                        Array.Resize(ref stack, stack.Length * 2);
                    }

                    stack[++top] = new Frame { Element = child, Next = child.FirstChild, ParentIndex = index, Styled = s, Cursor = s.IsNull ? default : s.FirstChild };
                    break;
                }

                case NodeKind.Text:
                    AddText(child, ref f);
                    break;
                case NodeKind.DocumentType:
                    // The doctype takes a number but is no node of the facts: a snapshot lists it ahead of the document element, outside the tree.
                    _snapshotCount++;
                    break;
                default:
                    // Comments and processing instructions take a number and have no box.
                    Number(child);
                    break;
            }
        }
    }

    private int AddElement(Node element, StyledNode s)
    {
        int index = Number(element);
        AddFormState(element);
        if (BoxBounds(s, out Rect bounds, out Fragment first))
        {
            _builder.AddBox(element, ElementBox(element, s, first, bounds));
        }

        return index;
    }

    private void AddText(Node text, ref Frame frame)
    {
        ReadOnlySpan<char> data = text.Data;
        Number(text);
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
        BoxInput? box = TextBox(c, TextTransforms.Apply(data.ToString(), c.Values.TextTransform), owner, ownerFragment);
        if (box is not null)
        {
            _builder.AddBox(text, box);
        }
    }

    private void AddPseudo(StyledNode p, Node host)
    {
        string type = p.Kind switch { StyledNodeKind.Marker => "marker", StyledNodeKind.Before => "before", _ => "after" };
        Number(default);
        var boxes = new List<BoxInput>(2);
        string? text = null;
        if (BoxBounds(p, out Rect bounds, out Fragment first))
        {
            boxes.Add(PseudoBox(p, first, bounds));
            if (p.Kind != StyledNodeKind.Marker)
            {
                // The generated text is laid out by an anonymous box of its own, listed as a second box of the same pseudo-element.
                string generated = p.Text.ToString();
                BoxInput? textBox = TextBox(p, generated, p, first);
                if (textBox is not null)
                {
                    boxes.Add(textBox);
                    text = generated;
                }
            }
        }

        _builder.AddGeneratedContent(host, new GeneratedBoxInput(type) { NodeCount = 1, Boxes = boxes, LayoutIndex = boxes.Count > 0 ? 0 : -1, Text = text });
    }

    // ------------------------------------------------------------------ boxes

    private BoxInput ElementBox(Node element, StyledNode s, Fragment first, Rect bounds)
    {
        LayoutRect offset, client, scroll;
        if (element.Namespace == ElementNamespace.Html && element.LocalName is "br" or "wbr")
        {
            // A line break has no box of its own to measure in CSSOM View.
            offset = client = scroll = new LayoutRect(0, 0, 0, 0);
        }
        else
        {
            offset = new LayoutRect(_queries.OffsetLeft(element), _queries.OffsetTop(element), _queries.OffsetWidth(element), _queries.OffsetHeight(element));
            client = new LayoutRect(_queries.ClientLeft(element), _queries.ClientTop(element), _queries.ClientWidth(element), _queries.ClientHeight(element));
            scroll = new LayoutRect(
                (int)Math.Round(_queries.ScrollLeft(element), MidpointRounding.AwayFromZero),
                (int)Math.Round(_queries.ScrollTop(element), MidpointRounding.AwayFromZero),
                _queries.ScrollWidth(element),
                _queries.ScrollHeight(element));
        }

        bool blended = TextBackground(s, out string? color, out double opacity);
        return new BoxInput
        {
            BorderBox = Finite(bounds),
            OffsetRect = offset,
            ClientRect = client,
            ScrollRect = scroll,
            PaintOrder = LayerOf(first),
            IsStackingContext = _trace.IsStackingContext(first.Index),
            BlendedBackgroundColor = blended ? color : null,
            TextColorOpacity = blended ? opacity : 1,
            Styles = Styles(s, first),
        };
    }

    private BoxInput PseudoBox(StyledNode s, Fragment first, Rect bounds) => new()
    {
        BorderBox = Finite(bounds),
        PaintOrder = LayerOf(first),
        IsStackingContext = _trace.IsStackingContext(first.Index),
        TextColorOpacity = 1,
        Styles = Styles(s, first),
    };

    private string?[]? Styles(StyledNode s, Fragment fragment)
    {
        if (_styleNames.Length == 0)
        {
            return null;
        }

        var values = new string?[_styleNames.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = ComputedStyleSerializer.Get(s, _styleNames[i], fragment);
        }

        return values;
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

    // The box for the laid-out text of a text node (or of generated content), with its text boxes; null when no text was laid out.
    private BoxInput? TextBox(StyledNode textNode, string layoutText, StyledNode styleOwner, Fragment styleFragment)
    {
        int count = _textCount[textNode.Index];
        if (count == 0)
        {
            return null;
        }

        int at = _textFirst[textNode.Index];
        bool any = false;
        Rect union = default;
        _textBoxScratch.Clear();
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
            _textBoxScratch.Add(new TextBox(start, length, Finite(r)));
            // A forced break is a text box of its own, of no width, at the end of the line it ends.
            int end = start + length;
            if (keepBreaks && end < layoutText.Length && layoutText[end] == '\n' && !(k + 1 < count && _textFragments[at + k + 1].TextStart == end))
            {
                _textBoxScratch.Add(new TextBox(end, 1, Finite(new Rect(r.Right, r.Y, 0, r.Height))));
            }
        }

        if (!any)
        {
            return null;
        }

        return new BoxInput
        {
            BorderBox = Finite(union),
            PaintOrder = LayerOf(_textFragments[at]),
            LayoutText = layoutText,
            TextBoxes = _textBoxScratch.ToArray(),
            TextColorOpacity = 1,
            Styles = Styles(styleOwner, styleFragment),
        };
    }

    // The color behind an element's own text and the opacity it is painted with, when it has text that is painted: a character
    // that is not white space, in a box that is visible.
    private bool TextBackground(StyledNode s, out string? blended, out double opacity)
    {
        blended = null;
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
                blended = _blender.BlendedAt(item, (float)(r.X + (r.Width / 2)), (float)(r.Y + (r.Height / 2)));
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

    public RenderedFacts Build(CancellationToken ct)
    {
        IndexFragments();
        Walk(ct);
        Node rootElement = _doc.DocumentElement;
        _builder.SetViewport(_page.Options.ViewportWidth, _page.Options.ViewportHeight);
        _builder.SetContext(_context);
        _builder.SetSource(OneRender.EngineInfo.Name, DomSnapshot.PinnedProtocolVersion);
        string baseUrl = _page.Options.BaseUrl ?? string.Empty;
        _builder.SetPage(new PageFacts(
            baseUrl,
            baseUrl,
            null,
            "main",
            null,
            "UTF-8",
            rootElement.IsNull ? _page.Options.ViewportWidth : _queries.ScrollWidth(rootElement),
            rootElement.IsNull ? _page.Options.ViewportHeight : _queries.ScrollHeight(rootElement),
            Finite(_page.Scroll.ViewportX),
            Finite(_page.Scroll.ViewportY)));
        return _builder.Build();
    }
}
