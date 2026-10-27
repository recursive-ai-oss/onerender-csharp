using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml;

namespace OneRender.Tree;

/// <summary>A <c>&lt;clipPath&gt;</c> of an inline svg, flattened to one path in its user units.</summary>
internal sealed class ClipPathDef(string pathData, bool evenOdd, bool objectBoundingBox)
{
    /// <summary>SVG path data of every shape child, in the clip path's user units.</summary>
    public string PathData { get; } = pathData;

    /// <summary>True when the first shape asks for <c>clip-rule: evenodd</c>.</summary>
    public bool EvenOdd { get; } = evenOdd;

    /// <summary>True for <c>clipPathUnits="objectBoundingBox"</c>: the path is scaled to the referencing box.</summary>
    public bool ObjectBoundingBox { get; } = objectBoundingBox;
}

/// <summary>
/// Reads the <c>&lt;clipPath&gt;</c> elements of an inline svg so that CSS <c>clip-path: url(#id)</c> can use them. Shapes are
/// rect, circle, ellipse, line, polyline, polygon and path; a <c>transform</c> on a shape or nested <c>use</c> is not applied.
/// The walk is iterative and bounded.
/// </summary>
internal static class SvgClipDefs
{
    /// <summary>The most clip paths kept per document.</summary>
    public const int MaxDefs = 256;

    private const int MaxNodes = SvgSerializer.MaxNodes;
    private const int MaxPathChars = 1 << 16;

    /// <summary>Adds the clip paths found under <paramref name="root"/> to <paramref name="defs"/>; the first definition of an id wins.</summary>
    public static void Collect(Node root, Dictionary<string, ClipPathDef> defs, StringBuilder sb)
    {
        int nodes = 0;
        Node n = root;
        while (!n.IsNull)
        {
            if (++nodes > MaxNodes)
            {
                return;
            }

            if (n.Kind == NodeKind.Element && n.LocalName == "clipPath" && defs.Count < MaxDefs)
            {
                string? id = Attribute(n, "id");
                if (id is { Length: > 0 } && !defs.ContainsKey(id))
                {
                    defs[id] = Build(n, sb);
                }
            }

            // Pre-order walk without recursion.
            Node first = n.FirstChild;
            if (!first.IsNull)
            {
                n = first;
                continue;
            }

            while (true)
            {
                if (n.Index == root.Index)
                {
                    return;
                }

                Node next = n.NextSibling;
                if (!next.IsNull)
                {
                    n = next;
                    break;
                }

                n = n.Parent;
                if (n.IsNull || n.Index == root.Index)
                {
                    return;
                }
            }
        }
    }

    private static ClipPathDef Build(Node clip, StringBuilder sb)
    {
        sb.Clear();
        bool evenOdd = false, firstShape = true;
        for (Node c = clip.FirstChild; !c.IsNull && sb.Length < MaxPathChars; c = c.NextSibling)
        {
            if (c.Kind != NodeKind.Element || !Shape(c, sb))
            {
                continue;
            }

            if (firstShape)
            {
                evenOdd = string.Equals(Attribute(c, "clip-rule"), "evenodd", StringComparison.Ordinal);
                firstShape = false;
            }
        }

        bool bbox = string.Equals(Attribute(clip, "clipPathUnits"), "objectBoundingBox", StringComparison.Ordinal);
        return new ClipPathDef(sb.ToString(), evenOdd, bbox);
    }

    private static bool Shape(Node e, StringBuilder sb)
    {
        switch (e.LocalName)
        {
            case "rect":
                {
                    double x = Num(e, "x"), y = Num(e, "y"), w = Num(e, "width"), h = Num(e, "height");
                    if (!(w > 0) || !(h > 0))
                    {
                        return false;
                    }

                    sb.Append('M').Append(F(x)).Append(' ').Append(F(y)).Append('H').Append(F(x + w)).Append('V').Append(F(y + h)).Append('H').Append(F(x)).Append("Z ");
                    return true;
                }

            case "circle":
                {
                    double r = Num(e, "r");
                    return r > 0 && Ellipse(sb, Num(e, "cx"), Num(e, "cy"), r, r);
                }

            case "ellipse":
                {
                    double rx = Num(e, "rx"), ry = Num(e, "ry");
                    return rx > 0 && ry > 0 && Ellipse(sb, Num(e, "cx"), Num(e, "cy"), rx, ry);
                }

            case "polygon":
            case "polyline":
                {
                    string? pts = Attribute(e, "points");
                    if (pts is null || pts.Length > MaxPathChars)
                    {
                        return false;
                    }

                    sb.Append('M').Append(pts.Replace(',', ' '));
                    sb.Append("Z ");
                    return true;
                }

            case "path":
                {
                    string? d = Attribute(e, "d");
                    if (d is null || d.Length > MaxPathChars)
                    {
                        return false;
                    }

                    sb.Append(d).Append(' ');
                    return true;
                }

            default:
                return false;
        }
    }

    private static bool Ellipse(StringBuilder sb, double cx, double cy, double rx, double ry)
    {
        sb.Append('M').Append(F(cx - rx)).Append(' ').Append(F(cy))
            .Append('A').Append(F(rx)).Append(' ').Append(F(ry)).Append(" 0 1 0 ").Append(F(cx + rx)).Append(' ').Append(F(cy))
            .Append('A').Append(F(rx)).Append(' ').Append(F(ry)).Append(" 0 1 0 ").Append(F(cx - rx)).Append(' ').Append(F(cy)).Append("Z ");
        return true;
    }

    private static string F(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    private static string? Attribute(Node e, string name)
    {
        foreach (Attr a in e.Attributes)
        {
            if (string.Equals(a.LocalName, name, StringComparison.Ordinal))
            {
                return a.Value.ToString();
            }
        }

        return null;
    }

    private static double Num(Node e, string name)
    {
        string? v = Attribute(e, name);
        if (v is null)
        {
            return 0;
        }

        ReadOnlySpan<char> s = v.AsSpan().Trim();
        while (s.Length > 0 && !(char.IsAsciiDigit(s[^1]) || s[^1] == '.'))
        {
            s = s[..^1];
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) ? d : 0;
    }
}
