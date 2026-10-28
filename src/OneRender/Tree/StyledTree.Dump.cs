using System.Text;
using OneRender.Style;

namespace OneRender.Tree;

public sealed partial class StyledTree
{
    /// <summary>
    /// A text outline of the tree for tests and debugging: one node per line, two spaces of indent per level, each element with
    /// the computed values that differ from the initial ones. Equivalent trees from the two front ends dump identically.
    /// </summary>
    public string Dump()
    {
        var sb = new StringBuilder();
        if (_count == 0)
        {
            return string.Empty;
        }

        int n = 0;
        int depth = 0;
        while (n >= 0)
        {
            sb.Append(' ', depth * 2);
            DumpNode(sb, n);
            sb.Append('\n');
            if (_first[n] >= 0)
            {
                n = _first[n];
                depth++;
                continue;
            }

            while (n >= 0 && _next[n] < 0)
            {
                n = _parent[n];
                depth--;
            }

            if (n >= 0)
            {
                n = _next[n];
            }
        }

        return sb.ToString();
    }

    private void DumpNode(StringBuilder sb, int n)
    {
        var kind = (StyledNodeKind)_kind[n];
        switch (kind)
        {
            case StyledNodeKind.Text:
                sb.Append("#text \"").Append(TextOf(n).ToString().Replace("\n", "\\n", StringComparison.Ordinal)).Append('"');
                return;
            case StyledNodeKind.Element:
                sb.Append(TagNameOf(n));
                if (_replaced[n] != 0)
                {
                    sb.Append(" [replaced ").Append((ReplacedKind)_replaced[n]).Append(' ')
                        .Append(CssText.Number(_naturalW[n])).Append('x').Append(CssText.Number(_naturalH[n])).Append(']');
                }

                break;
            case StyledNodeKind.Marker:
                sb.Append("::marker");
                break;
            case StyledNodeKind.Before:
                sb.Append("::before \"").Append(TextOf(n)).Append('"');
                break;
            case StyledNodeKind.SliderContainer:
                sb.Append("::slider-container");
                break;
            case StyledNodeKind.SliderTrack:
                sb.Append("::slider-track");
                break;
            case StyledNodeKind.SliderThumb:
                sb.Append("::slider-thumb");
                break;
            default:
                sb.Append("::after \"").Append(TextOf(n)).Append('"');
                break;
        }

        string values = ComputedValueText.NonInitial(in Styles[new StyleId(_style[n])]);
        if (values.Length > 0)
        {
            sb.Append(" { ").Append(values).Append(" }");
        }
    }
}
