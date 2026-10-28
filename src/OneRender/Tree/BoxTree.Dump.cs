using System.Text;
using OneRender.Style;

namespace OneRender.Tree;

public sealed partial class BoxTree
{
    /// <summary>
    /// A text outline of the tree for tests and debugging, one box per line, two spaces of indent per level, for example
    /// <c>block div</c>, <c>inline span</c>, <c>text "hello"</c>, <c>block anon</c>. It names only facts both front ends
    /// produce (no node or style numbers), so equivalent trees dump identically.
    /// </summary>
    public string Dump()
    {
        var sb = new StringBuilder();
        if (_count == 0)
        {
            return string.Empty;
        }

        int b = 0;
        int depth = 0;
        while (b >= 0)
        {
            sb.Append(' ', depth * 2);
            DumpBox(sb, b);
            sb.Append('\n');
            if (_first[b] >= 0)
            {
                b = _first[b];
                depth++;
                continue;
            }

            while (b >= 0 && _next[b] < 0)
            {
                b = _parent[b];
                depth--;
            }

            if (b >= 0)
            {
                b = _next[b];
            }
        }

        return sb.ToString();
    }

    private void DumpBox(StringBuilder sb, int b)
    {
        var kind = (BoxKind)_kind[b];
        var level = (BoxLevel)_level[b];
        var ctx = (FormattingContext)_ctx[b];
        var flags = (BoxTraits)_flags[b];
        StyledNode node = Styled.Make(_node[b]);
        switch (kind)
        {
            case BoxKind.Text:
                sb.Append("text \"");
                ReadOnlySpan<char> t = node.Text;
                int n = Math.Min(t.Length, 40);
                for (int i = 0; i < n; i++)
                {
                    char c = t[i];
                    if (c == '\n')
                    {
                        sb.Append("\\n");
                    }
                    else if (c == '\t')
                    {
                        sb.Append("\\t");
                    }
                    else if (c == '"')
                    {
                        sb.Append("\\\"");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                sb.Append(t.Length > n ? "…\"" : "\"");
                break;
            case BoxKind.LineBreak:
                sb.Append("br");
                break;
            case BoxKind.Marker:
                sb.Append("marker");
                break;
            case BoxKind.Replaced:
                sb.Append(level == BoxLevel.Block ? "block-replaced " : "inline-replaced ").Append(node.TagName);
                break;
            case BoxKind.Inline:
                sb.Append("inline ").Append(NameOf(node));
                break;
            default:
                sb.Append(ContainerName(level, ctx)).Append(' ').Append(NameOf(node));
                break;
        }

        if ((flags & BoxTraits.Continuation) != 0)
        {
            sb.Append(" (continuation)");
        }

        if ((flags & BoxTraits.ListItem) != 0)
        {
            sb.Append(" (list-item)");
        }

        if ((flags & BoxTraits.OutsideMarker) != 0)
        {
            sb.Append(" (outside)");
        }

        if ((flags & BoxTraits.OutOfFlow) != 0)
        {
            sb.Append(" (out-of-flow)");
        }

        if ((flags & BoxTraits.Blockified) != 0)
        {
            sb.Append(" (blockified)");
        }

        if ((flags & BoxTraits.UnsplitBlockInInline) != 0)
        {
            sb.Append(" (unsplit)");
        }
    }

    private static string NameOf(StyledNode node)
    {
        if (node.IsNull)
        {
            return "anon";
        }

        return node.Kind switch
        {
            StyledNodeKind.Before => "::before",
            StyledNodeKind.After => "::after",
            StyledNodeKind.Marker => "::marker",
            StyledNodeKind.SliderContainer => "::slider-container",
            StyledNodeKind.SliderTrack => "::slider-track",
            StyledNodeKind.SliderThumb => "::slider-thumb",
            _ => node.TagName,
        };
    }

    private static string ContainerName(BoxLevel level, FormattingContext ctx)
    {
        bool block = level == BoxLevel.Block;
        return ctx switch
        {
            FormattingContext.Block => block ? "block" : "inline-block",
            FormattingContext.FlowRoot => block ? "flow-root" : "inline-block",
            FormattingContext.Flex => block ? "flex" : "inline-flex",
            FormattingContext.Grid => block ? "grid" : "inline-grid",
            FormattingContext.Table => block ? "table" : "inline-table",
            FormattingContext.TableRowGroup => "table-row-group",
            FormattingContext.TableHeaderGroup => "table-header-group",
            FormattingContext.TableFooterGroup => "table-footer-group",
            FormattingContext.TableRow => "table-row",
            FormattingContext.TableCell => "table-cell",
            FormattingContext.TableColumnGroup => "table-column-group",
            FormattingContext.TableColumn => "table-column",
            FormattingContext.TableCaption => "table-caption",
            _ => "container",
        };
    }
}
