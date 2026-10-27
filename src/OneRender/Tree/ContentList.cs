using System.Text;

namespace OneRender.Tree;

/// <summary>One piece of a <c>content</c> or <c>string-set</c> value (CSS Generated Content 3 §2, GCPM 3 §2 and §4).</summary>
internal enum GeneratedItemKind : byte
{
    /// <summary>A string; <see cref="GeneratedItem.A"/> is its text.</summary>
    Text,

    /// <summary><c>counter(name, style)</c>: A is the name, B the style.</summary>
    Counter,

    /// <summary><c>counters(name, separator, style)</c>: A name, B separator, C style.</summary>
    Counters,

    /// <summary><c>attr(name)</c>: A is the attribute name.</summary>
    Attr,

    /// <summary><c>open-quote</c>.</summary>
    OpenQuote,

    /// <summary><c>close-quote</c>.</summary>
    CloseQuote,

    /// <summary><c>no-open-quote</c>.</summary>
    NoOpenQuote,

    /// <summary><c>no-close-quote</c>.</summary>
    NoCloseQuote,

    /// <summary><c>string(name, keyword)</c>: A name, B keyword (first, start, last, first-except).</summary>
    StringRef,

    /// <summary><c>target-counter(url, name, style)</c>: A is the link (an attribute name when <see cref="GeneratedItem.AIsAttr"/>), B the counter, C the style.</summary>
    TargetCounter,

    /// <summary><c>target-counters(url, name, separator, style)</c>.</summary>
    TargetCounters,

    /// <summary><c>target-text(url, which)</c>: B is content, before, after or first-letter.</summary>
    TargetText,

    /// <summary><c>leader(type)</c>: A is the pattern text.</summary>
    Leader,

    /// <summary><c>content(which)</c>: B is text, before, after or first-letter.</summary>
    ElementContent,

    /// <summary>Something the engine does not draw (an image, <c>element()</c>); it contributes nothing.</summary>
    Unsupported,
}

/// <summary>A parsed piece of a content value.</summary>
internal readonly record struct GeneratedItem(GeneratedItemKind Kind, string A = "", string B = "", string C = "", string D = "", bool AIsAttr = false);

/// <summary>
/// A <c>content</c> or <c>string-set</c> value parsed into items. Parsing is bounded (items, nesting, length) and never fails: what is not
/// understood becomes <see cref="GeneratedItemKind.Unsupported"/>.
/// </summary>
internal sealed class ContentList
{
    /// <summary>Most items kept from one value.</summary>
    public const int MaxItems = 64;

    /// <summary>Longest value text parsed, in UTF-16 units.</summary>
    public const int MaxLength = 1 << 14;

    /// <summary>Marks the start of a leader in evaluated text: the next character is the pattern, then <see cref="LeaderEnd"/>.</summary>
    public const char LeaderStart = '\uE0FE';

    /// <summary>Ends a leader mark.</summary>
    public const char LeaderEnd = '\uE0FF';

    private ContentList(GeneratedItem[] items)
    {
        Items = items;
        foreach (GeneratedItem it in items)
        {
            switch (it.Kind)
            {
                case GeneratedItemKind.Counter or GeneratedItemKind.Counters when IsPageCounter(it.A):
                    UsesPageCounters = true;
                    break;
                case GeneratedItemKind.TargetCounter or GeneratedItemKind.TargetCounters:
                    UsesTargets = true;
                    if (IsPageCounter(it.B))
                    {
                        UsesPageReferences = true;
                    }

                    break;
                case GeneratedItemKind.TargetText:
                    UsesTargets = true;
                    break;
                case GeneratedItemKind.StringRef:
                    UsesStrings = true;
                    break;
                case GeneratedItemKind.Leader:
                    UsesLeaders = true;
                    break;
            }
        }
    }

    /// <summary>The items in order.</summary>
    public GeneratedItem[] Items { get; }

    /// <summary>True when an item reads <c>counter(page)</c> or <c>counter(pages)</c>.</summary>
    public bool UsesPageCounters { get; }

    /// <summary>True when an item is a <c>target-*</c> function.</summary>
    public bool UsesTargets { get; }

    /// <summary>True when an item is <c>target-counter</c> of <c>page</c> or <c>pages</c>.</summary>
    public bool UsesPageReferences { get; }

    /// <summary>True when an item is <c>string()</c>.</summary>
    public bool UsesStrings { get; }

    /// <summary>True when an item is <c>leader()</c>.</summary>
    public bool UsesLeaders { get; }

    /// <summary>Whether <paramref name="name"/> is one of the two page counters.</summary>
    public static bool IsPageCounter(string name) =>
        name.Equals("page", StringComparison.OrdinalIgnoreCase) || name.Equals("pages", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses the text of a content value. A <c>/</c> (alternative text) ends the list.</summary>
    /// <param name="text">The value as written.</param>
    public static ContentList Parse(string text)
    {
        var items = new List<GeneratedItem>(4);
        string s = text.Length > MaxLength ? text[..MaxLength] : text;
        int i = 0;
        while (i < s.Length && items.Count < MaxItems)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/')
            {
                break;
            }

            if (c is '"' or '\'')
            {
                items.Add(new GeneratedItem(GeneratedItemKind.Text, ReadString(s, ref i)));
                continue;
            }

            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '(' && s[i] != '"' && s[i] != '\'' && s[i] != '/')
            {
                i++;
            }

            string word = s[start..i];
            if (i < s.Length && s[i] == '(')
            {
                int close = FindClose(s, i);
                string inner = s[(i + 1)..close];
                i = Math.Min(s.Length, close + 1);
                items.Add(ParseFunction(word, inner));
                continue;
            }

            if (word.Length == 0)
            {
                i++;
                continue;
            }

            items.Add(word.ToLowerInvariantAscii() switch
            {
                "open-quote" => new GeneratedItem(GeneratedItemKind.OpenQuote),
                "close-quote" => new GeneratedItem(GeneratedItemKind.CloseQuote),
                "no-open-quote" => new GeneratedItem(GeneratedItemKind.NoOpenQuote),
                "no-close-quote" => new GeneratedItem(GeneratedItemKind.NoCloseQuote),
                _ => new GeneratedItem(GeneratedItemKind.Unsupported, word),
            });
        }

        return new ContentList(items.ToArray());
    }

    private static GeneratedItem ParseFunction(string name, string inner)
    {
        List<string> args = SplitArgs(inner);
        string n = name.ToLowerInvariantAscii();
        string Arg(int k) => k < args.Count ? args[k].Trim() : string.Empty;
        switch (n)
        {
            case "counter":
                return new GeneratedItem(GeneratedItemKind.Counter, Arg(0), Arg(1).Length == 0 ? "decimal" : Arg(1));
            case "counters":
                return new GeneratedItem(GeneratedItemKind.Counters, Arg(0), Unquote(Arg(1)), Arg(2).Length == 0 ? "decimal" : Arg(2));
            case "attr":
            {
                // attr(name type?, fallback?): the type or unit after the name is ignored; the value is read as text.
                string a = Arg(0);
                int sp = a.IndexOf(' ', StringComparison.Ordinal);
                return new GeneratedItem(GeneratedItemKind.Attr, sp > 0 ? a[..sp] : a);
            }

            case "string":
                return new GeneratedItem(GeneratedItemKind.StringRef, Arg(0), Arg(1).Length == 0 ? "first" : Arg(1).ToLowerInvariantAscii());
            case "target-counter":
            {
                (string link, bool isAttr) = LinkOf(Arg(0));
                return new GeneratedItem(GeneratedItemKind.TargetCounter, link, Arg(1), Arg(2).Length == 0 ? "decimal" : Arg(2), string.Empty, isAttr);
            }

            case "target-counters":
            {
                (string link, bool isAttr) = LinkOf(Arg(0));
                return new GeneratedItem(GeneratedItemKind.TargetCounters, link, Arg(1), Unquote(Arg(2)), Arg(3).Length == 0 ? "decimal" : Arg(3), isAttr);
            }

            case "target-text":
            {
                (string link, bool isAttr) = LinkOf(Arg(0));
                return new GeneratedItem(GeneratedItemKind.TargetText, link, Arg(1).Length == 0 ? "content" : Arg(1).ToLowerInvariantAscii(), string.Empty, string.Empty, isAttr);
            }

            case "leader":
            {
                // GCPM 3 §3.1: dotted is ". ", solid is "_" (a rule), space is " "; a string is its own pattern.
                string t = Arg(0);
                string pattern = t.ToLowerInvariantAscii() switch
                {
                    "dotted" => ". ",
                    "solid" => "_",
                    "space" => " ",
                    _ => t.Length > 0 && t[0] is '"' or '\'' ? Unquote(t) : ". ",
                };
                return new GeneratedItem(GeneratedItemKind.Leader, pattern.Length == 0 ? " " : pattern);
            }

            case "content":
                return new GeneratedItem(GeneratedItemKind.ElementContent, string.Empty, Arg(0).Length == 0 ? "text" : Arg(0).ToLowerInvariantAscii());
            default:
                return new GeneratedItem(GeneratedItemKind.Unsupported, name);
        }
    }

    // The link of a target-* function: attr(href url) names an attribute, url(#id) and a string are the address itself.
    private static (string Link, bool IsAttr) LinkOf(string arg)
    {
        string a = arg.Trim();
        if (a.StartsWith("attr(", StringComparison.OrdinalIgnoreCase) && a.EndsWith(')'))
        {
            string inner = a[5..^1].Trim();
            int sp = inner.IndexOf(' ', StringComparison.Ordinal);
            return (sp > 0 ? inner[..sp] : inner, true);
        }

        if (a.StartsWith("url(", StringComparison.OrdinalIgnoreCase) && a.EndsWith(')'))
        {
            return (Unquote(a[4..^1].Trim()), false);
        }

        return (Unquote(a), false);
    }

    private static string Unquote(string s)
    {
        string t = s.Trim();
        if (t.Length >= 2 && t[0] is '"' or '\'' && t[^1] == t[0])
        {
            int i = 0;
            return ReadString(t, ref i);
        }

        return t;
    }

    // Splits at top-level commas, outside strings and nested parentheses.
    private static List<string> SplitArgs(string inner)
    {
        var args = new List<string>(3);
        int depth = 0, start = 0;
        for (int i = 0; i < inner.Length && args.Count < 8; i++)
        {
            char c = inner[i];
            if (c is '"' or '\'')
            {
                int j = i;
                ReadString(inner, ref j);
                i = j - 1;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == ',' && depth == 0)
            {
                args.Add(inner[start..i]);
                start = i + 1;
            }
        }

        args.Add(inner[start..]);
        return args;
    }

    // Index of the parenthesis that closes the one at s[open], or the end of the text.
    private static int FindClose(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '"' or '\'')
            {
                int j = i;
                ReadString(s, ref j);
                i = j - 1;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && --depth == 0)
            {
                return i;
            }
        }

        return s.Length;
    }

    // CSS Syntax 3 §4.3.5: a string with backslash escapes; a newline escape continues the string.
    private static string ReadString(string s, ref int i)
    {
        char quote = s[i++];
        var sb = new StringBuilder();
        while (i < s.Length && s[i] != quote)
        {
            char c = s[i++];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            if (i >= s.Length)
            {
                break;
            }

            char e = s[i];
            if (e == '\n')
            {
                i++;
                continue;
            }

            if (Uri.IsHexDigit(e))
            {
                int v = 0, n = 0;
                while (i < s.Length && n < 6 && Uri.IsHexDigit(s[i]))
                {
                    v = (v * 16) + Convert.ToInt32(s[i].ToString(), 16);
                    i++;
                    n++;
                }

                if (i < s.Length && char.IsWhiteSpace(s[i]))
                {
                    i++;
                }

                if (v is 0 or > 0x10FFFF || (v is >= 0xD800 and <= 0xDFFF))
                {
                    v = 0xFFFD;
                }

                sb.Append(char.ConvertFromUtf32(v));
                continue;
            }

            sb.Append(e);
            i++;
        }

        if (i < s.Length)
        {
            i++;
        }

        return sb.ToString();
    }
}

internal static class AsciiCase
{
    /// <summary>Lowercases ASCII letters only, independent of culture.</summary>
    public static string ToLowerInvariantAscii(this string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] is >= 'A' and <= 'Z')
            {
                return string.Create(s.Length, s, static (span, src) =>
                {
                    for (int k = 0; k < src.Length; k++)
                    {
                        char c = src[k];
                        span[k] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
                    }
                });
            }
        }

        return s;
    }
}
