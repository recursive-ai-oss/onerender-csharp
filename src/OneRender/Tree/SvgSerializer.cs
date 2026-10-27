using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml;

namespace OneRender.Tree;

/// <summary>
/// Writes an inline <c>&lt;svg&gt;</c> element and its subtree as XML so the SVG renderer can read it. The walk is iterative
/// and bounded by a node count and a size, so a hostile document costs a bounded amount of work.
/// </summary>
internal static class SvgSerializer
{
    /// <summary>The most nodes one inline svg may have.</summary>
    public const int MaxNodes = 20_000;

    /// <summary>The largest serialization, in characters.</summary>
    public const int MaxChars = 1 << 20;

    /// <summary>Serializes <paramref name="root"/>; null when the subtree has too many nodes or is too large.</summary>
    public static string? Serialize(Node root, StringBuilder sb)
    {
        sb.Clear();
        int nodes = 0;
        Node n = root;
        while (true)
        {
            if (++nodes > MaxNodes || sb.Length > MaxChars)
            {
                return null;
            }

            bool descend = false;
            if (n.Kind == NodeKind.Element)
            {
                sb.Append('<').Append(n.LocalName);
                foreach (Attr a in n.Attributes)
                {
                    sb.Append(' ').Append(a.QualifiedName).Append("=\"");
                    Escape(sb, a.Value, true);
                    sb.Append('"');
                }

                Node first = n.FirstChild;
                if (first.IsNull)
                {
                    sb.Append("/>");
                }
                else
                {
                    sb.Append('>');
                    n = first;
                    descend = true;
                }
            }
            else if (n.Kind == NodeKind.Text)
            {
                Escape(sb, n.Data, false);
            }

            if (descend)
            {
                continue;
            }

            // Advance: next sibling, or up (closing each parent) until the root is closed.
            while (true)
            {
                if (n.Index == root.Index)
                {
                    return sb.ToString();
                }

                Node next = n.NextSibling;
                if (!next.IsNull)
                {
                    n = next;
                    break;
                }

                n = n.Parent;
                sb.Append("</").Append(n.LocalName).Append('>');
                if (n.Index == root.Index)
                {
                    return sb.ToString();
                }
            }
        }
    }

    private static void Escape(StringBuilder sb, ReadOnlySpan<char> text, bool attribute)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"' when attribute: sb.Append("&quot;"); break;
                case < ' ' when c is not ('\t' or '\n' or '\r'): break; // not valid in XML 1.0
                default: sb.Append(c); break;
            }
        }
    }

    /// <summary>SVG 2 §8.2 and HTML §15.4: the natural width and height of the root from its attributes, and the ratio of its viewBox.</summary>
    public static void ReadSize(Node root, out double width, out double height, out double ratio)
    {
        width = height = double.NaN;
        ratio = 0;
        Span<double> nums = stackalloc double[4];
        foreach (Attr a in root.Attributes)
        {
            string name = a.LocalName;
            if (name == "width")
            {
                width = Length(a.Value);
            }
            else if (name == "height")
            {
                height = Length(a.Value);
            }
            else if (name == "viewBox")
            {
                ReadOnlySpan<char> v = a.Value;
                int count = 0, i = 0;
                while (i < v.Length && count < 4)
                {
                    while (i < v.Length && (v[i] is ' ' or ',' or '\t' or '\n' or '\r'))
                    {
                        i++;
                    }

                    int s = i;
                    while (i < v.Length && !(v[i] is ' ' or ',' or '\t' or '\n' or '\r'))
                    {
                        i++;
                    }

                    if (i > s && double.TryParse(v[s..i], NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    {
                        nums[count++] = d;
                    }
                    else if (i > s)
                    {
                        break;
                    }
                }

                if (count == 4 && nums[2] > 0 && nums[3] > 0)
                {
                    ratio = nums[2] / nums[3];
                }
            }
        }

        // With one dimension and a viewBox the other follows the ratio; with neither there is no natural size (the 300 × 150 default applies).
        if (ratio > 0 && !double.IsNaN(width) && double.IsNaN(height))
        {
            height = width / ratio;
        }
        else if (ratio > 0 && double.IsNaN(width) && !double.IsNaN(height))
        {
            width = height * ratio;
        }
    }

    // A plain number or px length; percentages and other units give no natural size.
    private static double Length(ReadOnlySpan<char> s)
    {
        s = s.Trim();
        if (s.EndsWith("px", StringComparison.Ordinal))
        {
            s = s[..^2];
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) && d >= 0 ? d : double.NaN;
    }
}
