using System.Text;

namespace OneRender.WptRunner;

/// <summary>
/// Rewrites the XHTML spelling of a document into HTML that the engine's HTML parser reads the same way an XML parser would
/// read it, for the constructs the reftests in <c>.xht</c> and <c>.xhtml</c> files use. It is not a conforming XML parser: it does
/// no well-formedness checking, no namespace processing, no entity declarations from a DTD and no XML-specific tree building
/// (an XML document gets no implicit <c>tbody</c>, <c>html</c>, <c>head</c> or <c>body</c> elements, and this keeps the HTML ones).
/// </summary>
/// <remarks>
/// What it does: CDATA sections become their character data (XML 1.0 §2.7), kept verbatim inside <c>style</c> and <c>script</c>
/// and escaped elsewhere; an empty-element tag on a non-void element (<c>&lt;div/&gt;</c>) becomes a start tag and an end tag,
/// since the HTML parser would otherwise ignore the slash and nest what follows (XML 1.0 §3.1); the XML declaration and other
/// processing instructions are dropped.
/// </remarks>
public static class XhtmlSource
{
    private static readonly HashSet<string> Void = new(StringComparer.Ordinal)
    {
        "area", "base", "basefont", "br", "col", "embed", "frame", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    /// <summary>True when the file name is an XHTML document by extension.</summary>
    public static bool IsXhtml(string path) =>
        path.EndsWith(".xht", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase);

    /// <summary>Converts XHTML source text to the HTML the engine parses.</summary>
    public static string ToHtml(string xhtml)
    {
        var sb = new StringBuilder(xhtml.Length);
        string? raw = null; // the open style or script element, whose content is raw text
        int i = 0;
        while (i < xhtml.Length)
        {
            char c = xhtml[i];
            if (c != '<')
            {
                sb.Append(c);
                i++;
                continue;
            }

            if (StartsWith(xhtml, i, "<![CDATA["))
            {
                int end = xhtml.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                int stop = end < 0 ? xhtml.Length : end;
                ReadOnlySpan<char> text = xhtml.AsSpan(i + 9, stop - (i + 9));
                if (raw is not null)
                {
                    sb.Append(text);
                }
                else
                {
                    foreach (char t in text)
                    {
                        _ = t switch { '&' => sb.Append("&amp;"), '<' => sb.Append("&lt;"), '>' => sb.Append("&gt;"), _ => sb.Append(t) };
                    }
                }

                i = end < 0 ? xhtml.Length : end + 3;
                continue;
            }

            if (raw is not null)
            {
                if (StartsWith(xhtml, i, "</") && IsNameAt(xhtml, i + 2, raw))
                {
                    raw = null;
                }

                sb.Append(c);
                i++;
                continue;
            }

            if (StartsWith(xhtml, i, "<!--"))
            {
                int end = xhtml.IndexOf("-->", i + 4, StringComparison.Ordinal);
                int stop = end < 0 ? xhtml.Length : end + 3;
                sb.Append(xhtml, i, stop - i);
                i = stop;
                continue;
            }

            if (StartsWith(xhtml, i, "<?"))
            {
                int end = xhtml.IndexOf("?>", i + 2, StringComparison.Ordinal);
                i = end < 0 ? xhtml.Length : end + 2;
                continue;
            }

            if (i + 1 < xhtml.Length && (xhtml[i + 1] == '!' || xhtml[i + 1] == '/'))
            {
                int end = xhtml.IndexOf('>', i);
                int stop = end < 0 ? xhtml.Length : end + 1;
                sb.Append(xhtml, i, stop - i);
                i = stop;
                continue;
            }

            if (i + 1 < xhtml.Length && char.IsAsciiLetter(xhtml[i + 1]))
            {
                i = Tag(xhtml, i, sb, ref raw);
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    // A start tag: copies it, expanding an empty-element tag on a non-void element, and notes a raw-text element.
    private static int Tag(string s, int start, StringBuilder sb, ref string? raw)
    {
        int i = start + 1;
        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '>' && s[i] != '/')
        {
            i++;
        }

        string name = s.Substring(start + 1, i - start - 1);
        char quote = '\0';
        int end = i;
        while (end < s.Length)
        {
            char c = s[end];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                break;
            }

            end++;
        }

        bool unterminated = end >= s.Length;
        int close = unterminated ? s.Length : end + 1;
        bool selfClosing = !unterminated && end > start && s[end - 1] == '/' && quote == '\0';
        string lower = name.ToLowerInvariant();
        if (selfClosing && !Void.Contains(lower))
        {
            sb.Append(s, start, end - 1 - start).Append("></").Append(name).Append('>');
            return close;
        }

        sb.Append(s, start, close - start);
        if (!selfClosing && (lower == "style" || lower == "script"))
        {
            raw = lower;
        }

        return close;
    }

    private static bool StartsWith(string s, int at, string what) => string.CompareOrdinal(s, at, what, 0, what.Length) == 0;

    private static bool IsNameAt(string s, int at, string name)
    {
        if (string.Compare(s, at, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        int after = at + name.Length;
        return after >= s.Length || s[after] is '>' or '/' || char.IsWhiteSpace(s[after]);
    }
}
