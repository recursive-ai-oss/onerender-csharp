using OnePdf;
using OneRender.Painting;

namespace OneRender.Pdf;

/// <summary>One open <see cref="ItemKind.BeginTag"/> as a backend sees it.</summary>
/// <param name="Key">Identity of the element: the id of the tag item without its occurrence number (<c>Id &gt;&gt; 16</c>).</param>
/// <param name="Role">The role.</param>
/// <param name="Alt">Alternative text, or null.</param>
/// <param name="Link">Link destination, or null.</param>
/// <param name="Lang">Language, or null.</param>
internal readonly record struct TagRef(ulong Key, TagRole Role, string? Alt, string? Link, string? Lang);

/// <summary>
/// Turns the stream of tag scopes in display lists into PDF structure elements (ISO 32000-2 14.7). An element is begun when the first
/// content that belongs to it is drawn and ended when content of a different branch is drawn, so a tag that encloses nothing makes no
/// element and an element whose tag was closed and opened again between two paragraphs of the same page stays one element.
/// </summary>
internal sealed class TagWriter
{
    private struct Open
    {
        public TagRef Ref;
        public PdfStructureElement Element;
        public bool HasRect;
        public double Left, Bottom, Right, Top;
        public string? Text;
        public int Rows;
        public int RowIndex;
    }

    private readonly PdfDocumentWriter _writer;
    private readonly List<Open> _open = new(16);
    private PdfStructureElement? _document;

    public TagWriter(PdfDocumentWriter writer, bool enabled)
    {
        _writer = writer;
        Enabled = enabled;
    }

    public bool Enabled { get; }

    public int Depth => _open.Count;

    /// <summary>Maximum structure nesting: deeper tags are folded into their parent so a hostile document cannot build an unbounded tree.</summary>
    public const int MaxDepth = 96;

    public void Start()
    {
        if (Enabled && _document is null)
        {
            _document = _writer.BeginStructureElement("Document");
        }
    }

    public static string TypeOf(TagRole role) => role switch
    {
        TagRole.Document => "Document",
        TagRole.Part => "Part",
        TagRole.Section => "Sect",
        TagRole.Div => "Div",
        TagRole.Paragraph => "P",
        TagRole.Span => "Span",
        TagRole.Heading1 => "H1",
        TagRole.Heading2 => "H2",
        TagRole.Heading3 => "H3",
        TagRole.Heading4 => "H4",
        TagRole.Heading5 => "H5",
        TagRole.Heading6 => "H6",
        TagRole.List => "L",
        TagRole.ListItem => "LI",
        TagRole.ListLabel => "Lbl",
        TagRole.ListBody => "LBody",
        TagRole.Table => "Table",
        TagRole.TableRow => "TR",
        TagRole.TableHeaderCell => "TH",
        TagRole.TableDataCell => "TD",
        TagRole.TableHead => "THead",
        TagRole.TableBody => "TBody",
        TagRole.TableFoot => "TFoot",
        TagRole.Figure => "Figure",
        TagRole.Caption => "Caption",
        TagRole.Link => "Link",
        TagRole.BlockQuote => "BlockQuote",
        TagRole.Code => "Code",
        TagRole.Form => "Form",
        _ => "Span",
    };

    /// <summary>
    /// Makes the open elements equal <paramref name="chain"/> (outermost first), ending and beginning elements as needed, and returns
    /// the innermost, or null when the chain is empty (the content then belongs to the document element).
    /// </summary>
    public PdfStructureElement? Reconcile(ReadOnlySpan<TagRef> chain, PdfStreamPageBuilder? page)
    {
        int keep = 0;
        int limit = Math.Min(chain.Length, MaxDepth);
        while (keep < _open.Count && keep < limit && _open[keep].Ref.Key == chain[keep].Key)
        {
            keep++;
        }

        while (_open.Count > keep)
        {
            CloseLast(page);
        }

        for (int i = keep; i < limit; i++)
        {
            Begin(chain[i]);
        }

        return _open.Count > 0 ? _open[^1].Element : _document;
    }

    private void Begin(in TagRef tag)
    {
        string type = TypeOf(tag.Role);
        PdfDictionary? attributes = null;
        int rowIndex = 0;
        if (tag.Role == TagRole.TableRow)
        {
            for (int k = _open.Count - 1; k >= 0; k--)
            {
                if (_open[k].Ref.Role == TagRole.Table)
                {
                    Open t = _open[k];
                    rowIndex = t.Rows++;
                    _open[k] = t;
                    break;
                }
            }
        }
        else if (tag.Role == TagRole.TableHeaderCell)
        {
            bool column = false;
            bool seenRow = false;
            for (int k = _open.Count - 1; k >= 0; k--)
            {
                TagRole r = _open[k].Ref.Role;
                if (r == TagRole.TableRow && !seenRow)
                {
                    seenRow = true;
                    column = _open[k].RowIndex == 0;
                }
                else if (r == TagRole.TableHead)
                {
                    column = true;
                }
                else if (r == TagRole.Table)
                {
                    break;
                }
            }

            attributes = PdfStructureAttributes.TableHeader(column ? "Column" : "Row");
        }

        var options = new PdfStructureElementOptions
        {
            AlternateText = tag.Alt,
            Language = tag.Lang,
            Attributes = attributes,
        };
        PdfStructureElement element = _writer.BeginStructureElement(type, options);
        _open.Add(new Open { Ref = tag, Element = element, RowIndex = rowIndex });
    }

    private void CloseLast(PdfStreamPageBuilder? page)
    {
        int last = _open.Count - 1;
        FlushLink(last, page);
        _writer.EndStructureElement(_open[last].Element);
        _open.RemoveAt(last);
    }

    /// <summary>Records that content with these bounds (page space, points) and text belongs to every open link.</summary>
    public void NoteContent(double left, double bottom, double right, double top, ReadOnlySpan<char> text)
    {
        for (int i = 0; i < _open.Count; i++)
        {
            if (_open[i].Ref.Role != TagRole.Link)
            {
                continue;
            }

            Open o = _open[i];
            if (o.HasRect)
            {
                o.Left = Math.Min(o.Left, left);
                o.Bottom = Math.Min(o.Bottom, bottom);
                o.Right = Math.Max(o.Right, right);
                o.Top = Math.Max(o.Top, top);
            }
            else
            {
                o.HasRect = true;
                (o.Left, o.Bottom, o.Right, o.Top) = (left, bottom, right, top);
            }

            if (!text.IsEmpty && (o.Text is null || o.Text.Length < 120))
            {
                o.Text = string.Concat(o.Text, text.Length > 120 ? text[..120] : text);
            }

            _open[i] = o;
        }
    }

    /// <summary>Writes the link annotations of the elements that have drawn on this page; call before committing the page.</summary>
    public void FlushLinks(PdfStreamPageBuilder page)
    {
        for (int i = 0; i < _open.Count; i++)
        {
            FlushLink(i, page);
        }
    }

    private void FlushLink(int index, PdfStreamPageBuilder? page)
    {
        Open o = _open[index];
        if (o.Ref.Role != TagRole.Link || !o.HasRect)
        {
            return;
        }

        o.HasRect = false;
        string? target = o.Ref.Link;
        // Only external destinations make annotations: the display list does not say where an in-document anchor is.
        if (page is not null && target is { Length: > 0 } && target[0] != '#' && o.Right > o.Left && o.Top > o.Bottom)
        {
            string description = !string.IsNullOrWhiteSpace(o.Ref.Alt) ? o.Ref.Alt : (!string.IsNullOrWhiteSpace(o.Text) ? o.Text.Trim() : target);
            try
            {
                page.AddLink(new PdfRectangle(o.Left, o.Bottom, o.Right, o.Top), target, o.Element, description);
            }
            catch (ArgumentException)
            {
                // A destination OnePdf refuses is dropped; the element stays as a link without an action.
            }
        }

        o.Text = null;
        _open[index] = o;
    }

    public void Finish(PdfStreamPageBuilder? page)
    {
        while (_open.Count > 0)
        {
            CloseLast(page);
        }

        if (_document is not null)
        {
            _writer.EndStructureElement(_document);
            _document = null;
        }
    }
}
