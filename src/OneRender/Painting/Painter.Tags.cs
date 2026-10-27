using OneRender.Layout;
using OneRender.Tree;
using RecursiveAI.OneHtml;

namespace OneRender.Painting;

// Semantic tags (HTML-AAM element roles mapped to PDF structure types). Off by default: tags allocate strings and walk the box tree,
// and a game UI that draws the list never reads them.
public sealed partial class Painter
{
    // Part number of tag ids. The owner is the element's box and the sub-number counts how often the element was opened, so a backend
    // that sees several tags with equal (owner, part) knows they are one element (see docs/display-list.md, Tags).
    private const ushort PartTag = 0x7F00;

    // Open tags are limited so tags and the other scopes together stay under the list's 256 levels (MaxScopes is 240).
    private const int MaxTagDepth = 14;

    private bool _tags;
    private string? _baseUrl;
    private byte[] _roleCache = new byte[64];
    private int[] _tagOrdinal = new int[64];
    private readonly int[] _wantBox = new int[MaxTagDepth];
    private readonly byte[] _wantRole = new byte[MaxTagDepth];
    private readonly bool[] _wantVirtual = new bool[MaxTagDepth];
    private int[] _walk = new int[64];

    private const byte RoleUnknown = 0;
    private const byte RoleNone = 255;

    private void PrepareTags(in PaintOptions options)
    {
        _tags = options.Tags;
        _baseUrl = options.BaseUrl;
        if (!_tags)
        {
            return;
        }

        int boxCount = _boxes.Count;
        if (_roleCache.Length < boxCount)
        {
            int size = Math.Max(boxCount, _roleCache.Length * 2);
            _roleCache = new byte[size];
            _tagOrdinal = new int[size];
        }

        Array.Clear(_roleCache, 0, boxCount);
        Array.Clear(_tagOrdinal, 0, boxCount);
    }

    /// <summary>
    /// Makes the open tags the structure that surrounds the content of fragment <paramref name="f"/>: tags that do not belong are
    /// closed (only while they are the innermost scope, since scopes nest), and the missing ones are opened.
    /// </summary>
    /// <param name="f">A fragment.</param>
    /// <param name="self">Whether the fragment's own box is part of the structure (content it paints) or only its ancestors (its decoration and the scopes it opens).</param>
    private void SyncTags(int f, bool self)
    {
        if (!_tags)
        {
            return;
        }

        int box = _tree.BoxOf(f);
        // A repeated table header or footer is not part of the structure: it sits inside its table as an artifact, so the table stays
        // one structure element across pages and the copies are not read again.
        int want = BuildChain(box, self);
        if (((FragmentTraits)_tree.TraitsOf(f) & FragmentTraits.Repeated) != 0 && want < MaxTagDepth)
        {
            _wantBox[want] = box;
            _wantRole[want] = (byte)TagRole.Artifact;
            _wantVirtual[want] = true;
            want++;
        }

        // Open tags, outermost first, as the list sees them.
        int open = 0;
        for (int p = _list.TopIndex; p >= 0; p = _list[p].ParentIndex)
        {
            if (_list[p].Kind == ItemKind.BeginTag)
            {
                open++;
            }
        }

        // Compare from the outside in: the n-th open tag from the outside is the n-th wanted one when their keys are equal.
        int keep = 0;
        if (open > 0)
        {
            Span<ulong> keys = stackalloc ulong[MaxTagDepth + 8];
            int n = 0;
            for (int p = _list.TopIndex; p >= 0 && n < keys.Length; p = _list[p].ParentIndex)
            {
                if (_list[p].Kind == ItemKind.BeginTag)
                {
                    keys[n++] = _list[p].Id.Value >> 16;
                }
            }

            // keys[0] is the innermost; the outermost is keys[n - 1].
            while (keep < n && keep < want && keys[n - 1 - keep] == TagKey(_wantBox[keep], _wantVirtual[keep]))
            {
                keep++;
            }
        }

        while (open > keep && _list.TopIsTag)
        {
            _list.EndTag();
            open--;
        }

        for (int k = open; k < want; k++)
        {
            OpenTag(k);
        }
    }

    // A table cell with nothing in it paints nothing, so no content would ask for its structure element, and a row whose cells are
    // missing is an irregular table (Matterhorn 15-003). A transparent rectangle in the cell is the anchor a backend needs. It is an
    // entry of the inline phase, where text is painted, so that it comes in document order between the cells' text; the entry's number
    // is the fragment's number plus the fragment count.
    private void AddAnchor(int f)
    {
        if (!_tags || _tree.FirstChildOf(f) >= 0)
        {
            return;
        }

        int box = _tree.BoxOf(f);
        TagRole? role = box < 0 ? null : RoleOf(box);
        if (role is not (TagRole.TableDataCell or TagRole.TableHeaderCell))
        {
            return;
        }

        int entry = _n + f;
        _flags[entry] = FlagAnchor;
        _nextEntry[entry] = -1;
        AddEntry(_ownerFor[f], PhaseInlines, entry);
    }

    private void PaintAnchor(int f)
    {
        SyncTags(f, true);
        Rect bb = _tree.BorderBoxOf(f);
        _list.FillRect(Id2(f, PartAnchor, 0), new RectF(C(_absX[f]), C(_absY[f]), Math.Max(1f, C(bb.Width)), Math.Max(1f, C(bb.Height))), PaintColor.Transparent);
    }

    private const int PartAnchor = 0x7F10;

    private static ulong TagKey(int box, bool virtualBody) => ((ulong)(uint)box << 16) | (ushort)(PartTag + (virtualBody ? 1 : 0));

    private void OpenTag(int k)
    {
        int box = _wantBox[k];
        int seq = _tagOrdinal[box]++;
        if (seq >= ushort.MaxValue || _list.OpenDepth >= DisplayListLimits.MaxNestingDepth - 2)
        {
            return;
        }

        var role = (TagRole)_wantRole[k];
        ushort part = (ushort)(PartTag + (_wantVirtual[k] ? 1 : 0));
        ItemId id = ItemId.Of((uint)box, part, (ushort)seq);
        string? alt = null, link = null, lang = null;
        int colSpan = 1, rowSpan = 1;
        if (!_wantVirtual[k] && role is TagRole.TableDataCell or TagRole.TableHeaderCell)
        {
            StyledNode cell = _boxes.Make(box).Node;
            if (!cell.IsNull && cell.Tree is { } tree)
            {
                colSpan = tree.ColumnSpanOf(cell.Index);
                rowSpan = tree.RowSpanOf(cell.Index);
                if (rowSpan == 0)
                {
                    rowSpan = RowsFrom(_boxes.ParentOf(box));
                }
            }
        }

        if (!_wantVirtual[k] && role != TagRole.ListLabel)
        {
            Box b = _boxes.Make(box);
            Node n = DomNodeOf(b);
            if (!n.IsNull)
            {
                if (role == TagRole.Figure)
                {
                    alt = n.GetAttribute("alt") ?? n.GetAttribute("aria-label") ?? n.GetAttribute("title");
                }
                else if (role == TagRole.Link)
                {
                    string? href = n.GetAttribute("href");
                    link = href is null ? null : ResourceAddress.Resolve(_baseUrl, href.Trim());
                    alt = n.GetAttribute("aria-label");
                    if (string.IsNullOrWhiteSpace(alt))
                    {
                        alt = null;
                    }
                }

                string? l = n.GetAttribute("lang");
                if (!string.IsNullOrWhiteSpace(l) && !string.Equals(b.Node.TagName, "html", StringComparison.Ordinal))
                {
                    lang = l.Trim();
                }
            }
        }

        _list.BeginTag(id, role, alt, link, lang, colSpan, rowSpan);
    }

    // HTML Standard 4.9.11: a rowspan of 0 reaches the end of the row group, so it covers this row and the rows after it in the group.
    private int RowsFrom(int row)
    {
        int n = 1;
        if (row < 0 || RoleOf(row) != TagRole.TableRow)
        {
            return n;
        }

        for (int next = _boxes.NextSiblingOf(row); next >= 0 && n < 65534; next = _boxes.NextSiblingOf(next))
        {
            if (RoleOf(next) == TagRole.TableRow)
            {
                n++;
            }
        }

        return n;
    }

    private static Node DomNodeOf(Box b)
    {
        StyledNode node = b.Node;
        Document? doc = node.Tree?.SourceDocument;
        int src = node.SourceIndex;
        return doc is null || src < 0 ? default : doc.GetNode(src);
    }

    // Fills _want* with the structure for a box and its ancestors; returns the number of entries.
    private int BuildChain(int box, bool self)
    {
        int depth = 0;
        for (int b = box; b >= 0 && depth < 4096; b = _boxes.ParentOf(b))
        {
            if (depth == _walk.Length)
            {
                Array.Resize(ref _walk, depth * 2);
            }

            _walk[depth++] = b;
        }

        int n = 0;
        // _walk[depth - 1] is the root; walk inwards.
        for (int i = depth - 1; i >= 0 && n < MaxTagDepth; i--)
        {
            int b = _walk[i];
            if (i == 0 && !self)
            {
                break;
            }

            TagRole? role = RoleOf(b);
            if (role is null)
            {
                continue;
            }

            _wantBox[n] = b;
            _wantRole[n] = (byte)role.Value;
            _wantVirtual[n] = false;
            n++;
            if (role == TagRole.ListItem && n < MaxTagDepth)
            {
                // A list item's content is its body; the marker is its label (ISO 32000-2 14.8.4.3, Table 355).
                bool markerNext = i > 0 && _boxes.Make(_walk[i - 1]).Kind == BoxKind.Marker;
                if (!markerNext && i > 0)
                {
                    _wantBox[n] = b;
                    _wantRole[n] = (byte)TagRole.ListBody;
                    _wantVirtual[n] = true;
                    n++;
                }
            }
        }

        return n;
    }

    private TagRole? RoleOf(int boxIndex)
    {
        byte cached = _roleCache[boxIndex];
        if (cached == RoleUnknown)
        {
            TagRole? r = ComputeRole(boxIndex);
            _roleCache[boxIndex] = r is null ? RoleNone : (byte)((byte)r.Value + 1);
            return r;
        }

        return cached == RoleNone ? null : (TagRole)(cached - 1);
    }

    private TagRole? ComputeRole(int boxIndex)
    {
        Box box = _boxes.Make(boxIndex);
        if (box.Kind == BoxKind.Marker)
        {
            return TagRole.ListLabel;
        }

        if (box.IsAnonymous || box.Kind is BoxKind.Text or BoxKind.LineBreak)
        {
            return null;
        }

        StyledNode node = box.Node;
        if (node.IsNull || node.Kind != StyledNodeKind.Element)
        {
            return null;
        }

        string tag = node.TagName;
        Node dom = DomNodeOf(box);
        string? roleAttr = dom.IsNull ? null : dom.GetAttribute("role");
        bool presentational = roleAttr is not null && (string.Equals(roleAttr, "presentation", StringComparison.OrdinalIgnoreCase) || string.Equals(roleAttr, "none", StringComparison.OrdinalIgnoreCase));
        switch (tag)
        {
            case "h1": return TagRole.Heading1;
            case "h2": return TagRole.Heading2;
            case "h3": return TagRole.Heading3;
            case "h4": return TagRole.Heading4;
            case "h5": return TagRole.Heading5;
            case "h6": return TagRole.Heading6;
            case "p": return TagRole.Paragraph;
            case "pre": return TagRole.Paragraph;
            case "blockquote": return TagRole.BlockQuote;
            case "ul":
            case "ol":
            case "menu":
                return presentational ? null : TagRole.List;
            case "li":
                {
                    string? parent = ParentElementTag(boxIndex);
                    return parent is "ul" or "ol" or "menu" && RoleOfParentElement(boxIndex) == TagRole.List ? TagRole.ListItem : null;
                }

            case "dt":
            case "dd":
                return TagRole.Paragraph;
            case "section":
            case "article":
                return TagRole.Section;
            case "div":
            case "main":
            case "nav":
            case "aside":
            case "header":
            case "footer":
            case "figure":
            case "form":
            case "fieldset":
            case "address":
            case "dl":
                return presentational ? null : TagRole.Div;
            case "figcaption":
            case "caption" when !IsLayoutTable(boxIndex):
                return TagRole.Caption;
            case "table":
                return IsDataTable(boxIndex, dom, presentational) ? TagRole.Table : TagRole.Div;
            case "thead":
                return InDataTable(boxIndex) ? TagRole.TableHead : null;
            case "tbody":
                return InDataTable(boxIndex) ? TagRole.TableBody : null;
            case "tfoot":
                return InDataTable(boxIndex) ? TagRole.TableFoot : null;
            case "tr":
                return InDataTable(boxIndex) ? TagRole.TableRow : null;
            case "th":
                return InDataTable(boxIndex) ? TagRole.TableHeaderCell : null;
            case "td":
                return InDataTable(boxIndex) ? TagRole.TableDataCell : null;
            case "a":
                return !dom.IsNull && dom.GetAttribute("href") is not null && !presentational ? TagRole.Link : null;
            case "code":
            case "kbd":
            case "samp":
                return TagRole.Code;
            case "img":
            case "svg":
                {
                    // An image with no alternative text is decorative as far as the document says: an artifact rather than an invented description.
                    string? alt = dom.IsNull ? null : (dom.GetAttribute("alt") ?? dom.GetAttribute("aria-label") ?? dom.GetAttribute("title"));
                    return presentational || string.IsNullOrWhiteSpace(alt) ? TagRole.Artifact : TagRole.Figure;
                }

            default:
                return null;
        }
    }

    private string? ParentElementTag(int boxIndex)
    {
        int p = _boxes.ParentOf(boxIndex);
        while (p >= 0)
        {
            Box pb = _boxes.Make(p);
            if (!pb.IsAnonymous && pb.Node.Kind == StyledNodeKind.Element)
            {
                return pb.Node.TagName;
            }

            p = _boxes.ParentOf(p);
        }

        return null;
    }

    private TagRole? RoleOfParentElement(int boxIndex)
    {
        int p = _boxes.ParentOf(boxIndex);
        while (p >= 0)
        {
            Box pb = _boxes.Make(p);
            if (!pb.IsAnonymous && pb.Node.Kind == StyledNodeKind.Element)
            {
                return RoleOf(p);
            }

            p = _boxes.ParentOf(p);
        }

        return null;
    }

    // The nearest table element box above (or at) a box.
    private int TableOf(int boxIndex)
    {
        for (int p = boxIndex; p >= 0; p = _boxes.ParentOf(p))
        {
            Box b = _boxes.Make(p);
            if (!b.IsAnonymous && b.Node.Kind == StyledNodeKind.Element && string.Equals(b.Node.TagName, "table", StringComparison.Ordinal))
            {
                return p;
            }
        }

        return -1;
    }

    private bool InDataTable(int boxIndex)
    {
        int t = TableOf(_boxes.ParentOf(boxIndex));
        return t >= 0 && RoleOf(t) == TagRole.Table;
    }

    private bool IsLayoutTable(int boxIndex)
    {
        int t = TableOf(_boxes.ParentOf(boxIndex));
        return t < 0 || RoleOf(t) != TagRole.Table;
    }

    // A table is data when it has header cells and is not marked presentational: without a header cell PDF/UA-1 §7.5 cannot be met,
    // and a table of that kind is used for layout far more often than for data.
    private static bool IsDataTable(int boxIndex, Node dom, bool presentational)
    {
        _ = boxIndex;
        return !presentational && !dom.IsNull && !dom.QuerySelector("th").IsNull;
    }
}
