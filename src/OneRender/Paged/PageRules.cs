using System.Globalization;
using OneRender.Tree;
using System.Text;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Paged;

/// <summary>The size of a page and its margins, in CSS px (CSS Paged Media 3 §3 and §5).</summary>
/// <param name="Width">The page box width, at least 1.</param>
/// <param name="Height">The page box height, at least 1.</param>
/// <param name="MarginTop">The top margin; the page area starts here.</param>
/// <param name="MarginRight">The right margin.</param>
/// <param name="MarginBottom">The bottom margin.</param>
/// <param name="MarginLeft">The left margin.</param>
public readonly record struct PageGeometry(double Width, double Height, double MarginTop, double MarginRight, double MarginBottom, double MarginLeft)
{
    /// <summary>The width of the page area (the content area) in px.</summary>
    public double ContentWidth => Width - MarginLeft - MarginRight;

    /// <summary>The height of the page area in px.</summary>
    public double ContentHeight => Height - MarginTop - MarginBottom;

    /// <summary>A4 portrait with 20 mm margins on every side.</summary>
    public static PageGeometry A4 { get; } = new(793.7007874015748, 1122.5196850393702, 75.59055118110236, 75.59055118110236, 75.59055118110236, 75.59055118110236);
}

/// <summary>One page-margin box's declarations after the cascade of the matching <c>@page</c> rules.</summary>
internal sealed class MarginBoxStyle
{
    /// <summary>The value of <c>content</c> as written, or null when it is absent, <c>none</c> or <c>normal</c> (the box is not generated).</summary>
    public string? Content;

    /// <summary>The value of <c>width</c> (top and bottom boxes) or <c>height</c> (side boxes) when authored.</summary>
    public string? Size;

    /// <summary>The value of <c>vertical-align</c>, or null.</summary>
    public string? VerticalAlign;

    /// <summary>The other declarations to apply to the box as CSS text.</summary>
    public string Css = string.Empty;
}

/// <summary>The cascaded style of one kind of page.</summary>
internal sealed class PageStyle
{
    /// <summary>Size and margins.</summary>
    public PageGeometry Geometry;

    /// <summary>The declarations of the page context other than size and margins (fonts, colour, background) as CSS text.</summary>
    public string Css = string.Empty;

    /// <summary>The sixteen margin boxes in <see cref="PageRuleSet.BoxNames"/> order; null where nothing is declared.</summary>
    public MarginBoxStyle?[] Boxes = new MarginBoxStyle?[16];

    /// <summary>True when some margin box generates content.</summary>
    public bool HasBoxes;
}

/// <summary>
/// The <c>@page</c> rules of a document (CSS Paged Media 3 §4 and §7). Rules are cascaded by the specificity of their page selector
/// (a page name counts most, then <c>:first</c> and <c>:blank</c>, then <c>:left</c> and <c>:right</c>) and by order, per property.
/// </summary>
internal sealed class PageRuleSet
{
    /// <summary>The page-margin box names of §4.2, clockwise from the top-left corner.</summary>
    public static readonly string[] BoxNames =
    [
        "top-left-corner", "top-left", "top-center", "top-right", "top-right-corner",
        "right-top", "right-middle", "right-bottom",
        "bottom-right-corner", "bottom-right", "bottom-center", "bottom-left", "bottom-left-corner",
        "left-bottom", "left-middle", "left-top",
    ];

    /// <summary>Most <c>@page</c> rules read from a document.</summary>
    public const int MaxRules = 1024;

    /// <summary>Most declarations kept in one rule or margin box.</summary>
    public const int MaxDeclarations = 256;

    private const int MaxRuleDepth = 16;
    private const int MaxBlockLength = 1 << 16;

    private sealed class Selector
    {
        public string? Name;
        public bool First;
        public bool Left;
        public bool Right;
        public bool Blank;
        public int Specificity;
    }

    private sealed class Rule
    {
        public Selector Selector = null!;
        public int Order;
        public List<(string Name, string Value)> Decls = [];
        public List<(string Box, List<(string Name, string Value)> Decls)> Boxes = [];
    }

    private readonly List<Rule> _rules = [];
    private readonly Dictionary<string, PageStyle> _cache = new(StringComparer.Ordinal);
    private readonly PageGeometry _defaults;
    private readonly double _em;

    private PageRuleSet(PageGeometry defaults, double em)
    {
        _defaults = defaults;
        _em = em;
    }

    /// <summary>The number of page rules read (a rule with a selector list counts once per selector).</summary>
    public int RuleCount => _rules.Count;

    /// <summary>True when some rule names a page (so pages can differ by name).</summary>
    public bool HasNamedRules { get; private set; }

    /// <summary>Diagnostics for what was ignored or unsupported.</summary>
    public DiagnosticBag Diagnostics { get; } = new();

    /// <summary>Reads the <c>@page</c> rules of the author and user sheets of <paramref name="styles"/>.</summary>
    /// <param name="styles">The cascade.</param>
    /// <param name="media">The media the document is rendered for; groups whose condition fails are skipped.</param>
    /// <param name="defaults">The geometry used where the rules say nothing.</param>
    /// <param name="emPx">The size of <c>em</c> for lengths in page rules (the root font size).</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    public static PageRuleSet Parse(DocumentStyles styles, MediaFeatures media, PageGeometry defaults, double emPx, CancellationToken cancellationToken)
    {
        var set = new PageRuleSet(defaults, emPx > 0 ? emPx : 16);
        var stack = new Stack<IEnumerator<CssNode>>();
        int order = 0;
        foreach (StyleSheetInfo info in styles.Sheets)
        {
            if (info.Origin == StyleOrigin.UserAgent)
            {
                continue;
            }

            stack.Push(((IEnumerable<CssNode>)info.Sheet.Rules).GetEnumerator());
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IEnumerator<CssNode> rules = stack.Peek();
                if (!rules.MoveNext())
                {
                    rules.Dispose();
                    stack.Pop();
                    continue;
                }

                CssNode node = rules.Current;
                if (node.Kind != CssNodeKind.AtRule)
                {
                    continue;
                }

                switch (node.AtRuleKind)
                {
                    case AtRuleKind.Page:
                        if (set._rules.Count < MaxRules)
                        {
                            set.AddRule(info.Sheet.Text, node, ref order);
                        }
                        else
                        {
                            set.Diagnostics.Add(DiagnosticKind.LimitExceeded, "more than " + MaxRules + " @page rules; the rest are ignored");
                        }

                        break;
                    case AtRuleKind.Media:
                        if (node.HasBlock && stack.Count < MaxRuleDepth && MediaQueryList.Parse(node.Prelude.Trimmed()).Matches(media))
                        {
                            stack.Push(node.Rules.GetEnumerator());
                        }

                        break;
                    case AtRuleKind.Layer:
                    case AtRuleKind.Supports:
                        if (node.HasBlock && stack.Count < MaxRuleDepth)
                        {
                            stack.Push(node.Rules.GetEnumerator());
                        }

                        break;
                }
            }
        }

        return set;
    }

    private void AddRule(string sheetText, CssNode node, ref int order)
    {
        if (!node.HasBlock)
        {
            return;
        }

        CssRange body = node.ContentRange;
        if (body.Length > MaxBlockLength)
        {
            Diagnostics.Add(DiagnosticKind.LimitExceeded, "an @page rule longer than " + MaxBlockLength + " characters is ignored");
            return;
        }

        string text = sheetText.Substring(body.Start, body.Length);
        var decls = new List<(string, string)>();
        var boxes = new List<(string, List<(string, string)>)>();
        ParseBlock(text, decls, boxes);

        // §4.1: a selector list gives each selector the same declarations.
        foreach (string part in SplitCommas(node.PreludeText.ToString()))
        {
            Selector? sel = ParseSelector(part.Trim());
            if (sel is null)
            {
                Diagnostics.Add(DiagnosticKind.InvalidValue, "@page selector '" + part.Trim() + "' is not valid; the rule is ignored");
                continue;
            }

            HasNamedRules |= sel.Name is not null;
            _rules.Add(new Rule { Selector = sel, Order = order++, Decls = decls, Boxes = boxes });
        }
    }

    private static List<string> SplitCommas(string s)
    {
        var list = new List<string>();
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == ',')
            {
                list.Add(s[start..i]);
                start = i + 1;
            }
        }

        list.Add(s[start..]);
        return list;
    }

    // <page-selector> = [ <ident-token>? <pseudo-page>* ]!
    private static Selector? ParseSelector(string s)
    {
        var sel = new Selector();
        int i = 0;
        if (s.Length > 0 && s[0] != ':')
        {
            while (i < s.Length && s[i] != ':')
            {
                char c = s[i];
                if (!(char.IsLetterOrDigit(c) || c is '-' or '_' || c > 127))
                {
                    return null;
                }

                i++;
            }

            sel.Name = s[..i];
            if (sel.Name.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                return null; // auto is not a valid page name
            }

            sel.Specificity += 100;
        }

        if (i == 0 && s.Length == 0)
        {
            return sel; // plain @page
        }

        while (i < s.Length)
        {
            if (s[i] != ':')
            {
                return null;
            }

            int j = ++i;
            while (i < s.Length && s[i] != ':')
            {
                i++;
            }

            string pseudo = s[j..i].Trim().ToLowerInvariantAscii();
            switch (pseudo)
            {
                case "first":
                    sel.First = true;
                    sel.Specificity += 10;
                    break;
                case "blank":
                    sel.Blank = true;
                    sel.Specificity += 10;
                    break;
                case "left":
                    sel.Left = true;
                    sel.Specificity += 1;
                    break;
                case "right":
                    sel.Right = true;
                    sel.Specificity += 1;
                    break;
                default:
                    return null;
            }
        }

        return sel;
    }

    // Splits the body of an @page rule into declarations and margin-box at-rules, honouring strings, comments, parentheses and braces.
    private static void ParseBlock(string text, List<(string, string)> decls, List<(string, List<(string, string)>)> boxes)
    {
        int i = 0;
        int n = text.Length;
        while (i < n)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c) || c is ';' or '}')
            {
                i++; // a stray closing brace is skipped, as CSS Syntax 3 §5.4.4 does for an unmatched one
                continue;
            }

            if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? n : end + 2;
                continue;
            }

            if (c == '@')
            {
                int s = ++i;
                while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != '{')
                {
                    i++;
                }

                string name = text[s..i].ToLowerInvariantAscii();
                while (i < n && text[i] != '{' && text[i] != ';')
                {
                    i++;
                }

                if (i >= n || text[i] == ';')
                {
                    continue;
                }

                int close = MatchBrace(text, i);
                string inner = text[(i + 1)..close];
                i = Math.Min(n, close + 1);
                if (Array.IndexOf(BoxNames, name) >= 0)
                {
                    var bd = new List<(string, string)>();
                    ParseDeclarations(inner, bd);
                    boxes.Add((name, bd));
                }

                continue;
            }

            int ds = i;
            int depth = 0;
            while (i < n)
            {
                char d = text[i];
                if (d is '"' or '\'')
                {
                    i = SkipString(text, i);
                    continue;
                }

                if (d == '(')
                {
                    depth++;
                }
                else if (d == ')')
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if ((d == ';' && depth == 0) || d == '}')
                {
                    break;
                }

                i++;
            }

            AddDeclaration(text[ds..i], decls);
        }
    }

    private static void ParseDeclarations(string text, List<(string, string)> decls)
    {
        int i = 0;
        int n = text.Length;
        while (i < n)
        {
            int start = i;
            int depth = 0;
            while (i < n)
            {
                char d = text[i];
                if (d is '"' or '\'')
                {
                    i = SkipString(text, i);
                    continue;
                }

                if (d == '(')
                {
                    depth++;
                }
                else if (d == ')')
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (d == ';' && depth == 0)
                {
                    break;
                }

                i++;
            }

            AddDeclaration(text[start..i], decls);
            i++;
        }
    }

    private static void AddDeclaration(string text, List<(string, string)> decls)
    {
        if (decls.Count >= MaxDeclarations)
        {
            return;
        }

        string stripped = StripComments(text);
        int colon = stripped.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return;
        }

        string name = stripped[..colon].Trim().ToLowerInvariantAscii();
        string value = stripped[(colon + 1)..].Trim();
        int imp = value.LastIndexOf('!');
        if (imp >= 0 && value[(imp + 1)..].Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..imp].Trim();
        }

        if (name.Length == 0 || value.Length == 0 || name.Length > 64)
        {
            return;
        }

        foreach (char ch in name)
        {
            if (!(ch is >= 'a' and <= 'z' || ch == '-' || ch is >= '0' and <= '9'))
            {
                return;
            }
        }

        decls.Add((name, value));
    }

    private static string StripComments(string s)
    {
        if (!s.Contains("/*", StringComparison.Ordinal))
        {
            return s;
        }

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    break;
                }

                i = end + 1;
                continue;
            }

            sb.Append(s[i]);
        }

        return sb.ToString();
    }

    private static int SkipString(string s, int i)
    {
        char q = s[i++];
        while (i < s.Length && s[i] != q)
        {
            if (s[i] == '\\')
            {
                i++;
            }

            i++;
        }

        return Math.Min(s.Length, i + 1);
    }

    private static int MatchBrace(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '"' or '\'')
            {
                i = SkipString(s, i) - 1;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return i;
            }
        }

        return s.Length;
    }

    /// <summary>
    /// The cascaded style of a page.
    /// </summary>
    /// <param name="name">The page name (Paged Media 3 §7.3), or null.</param>
    /// <param name="index">The 0-based index of the page in the document.</param>
    /// <param name="isRight">Whether it is a right-hand page.</param>
    /// <param name="blank">Whether it is a blank page inserted for a side break.</param>
    public PageStyle Resolve(string? name, int index, bool isRight, bool blank)
    {
        bool first = index == 0;
        string key = (name ?? string.Empty) + (first ? "|f" : "|") + (isRight ? "r" : "l") + (blank ? "b" : string.Empty);
        if (_cache.TryGetValue(key, out PageStyle? cached))
        {
            return cached;
        }

        var matching = new List<Rule>();
        foreach (Rule r in _rules)
        {
            Selector s = r.Selector;
            if ((s.Name is null || (name is not null && s.Name.Equals(name, StringComparison.Ordinal)))
                && (!s.First || first) && (!s.Left || !isRight) && (!s.Right || isRight) && (!s.Blank || blank))
            {
                matching.Add(r);
            }
        }

        matching.Sort(static (a, b) =>
        {
            int d = a.Selector.Specificity.CompareTo(b.Selector.Specificity);
            return d != 0 ? d : a.Order.CompareTo(b.Order);
        });

        string? size = null;
        var margin = new string?[4];
        var ctx = new Dictionary<string, string>(StringComparer.Ordinal);
        var ctxOrder = new List<string>();
        var boxDecls = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var boxOrder = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Rule r in matching)
        {
            foreach ((string pn, string pv) in r.Decls)
            {
                switch (pn)
                {
                    case "size":
                        size = pv;
                        break;
                    case "margin":
                        SetMargins(margin, pv);
                        break;
                    case "margin-top":
                        margin[0] = pv;
                        break;
                    case "margin-right":
                        margin[1] = pv;
                        break;
                    case "margin-bottom":
                        margin[2] = pv;
                        break;
                    case "margin-left":
                        margin[3] = pv;
                        break;
                    case "marks":
                    case "bleed":
                    case "page-orientation":
                        break;
                    default:
                        if (ctx.TryAdd(pn, pv))
                        {
                            ctxOrder.Add(pn);
                        }
                        else
                        {
                            ctx[pn] = pv;
                        }

                        break;
                }
            }

            foreach ((string box, List<(string Name, string Value)> bd) in r.Boxes)
            {
                if (!boxDecls.TryGetValue(box, out Dictionary<string, string>? map))
                {
                    map = new Dictionary<string, string>(StringComparer.Ordinal);
                    boxDecls[box] = map;
                    boxOrder[box] = [];
                }

                foreach ((string pn, string pv) in bd)
                {
                    if (map.TryAdd(pn, pv))
                    {
                        boxOrder[box].Add(pn);
                    }
                    else
                    {
                        map[pn] = pv;
                    }
                }
            }
        }

        var style = new PageStyle { Geometry = ResolveGeometry(size, margin) };
        var css = new StringBuilder();
        foreach (string pn in ctxOrder)
        {
            if (IsForwardable(pn))
            {
                AppendDeclaration(css, pn, ctx[pn]);
            }
        }

        style.Css = css.ToString();
        for (int b = 0; b < BoxNames.Length; b++)
        {
            if (!boxDecls.TryGetValue(BoxNames[b], out Dictionary<string, string>? map))
            {
                continue;
            }

            var mb = new MarginBoxStyle();
            var bcss = new StringBuilder();
            foreach (string pn in boxOrder[BoxNames[b]])
            {
                string v = map[pn];
                switch (pn)
                {
                    case "content":
                        mb.Content = v.Equals("none", StringComparison.OrdinalIgnoreCase) || v.Equals("normal", StringComparison.OrdinalIgnoreCase) ? null : v;
                        break;
                    case "width" when b is 1 or 2 or 3 or 9 or 10 or 11:
                    case "height" when b is 5 or 6 or 7 or 13 or 14 or 15:
                        mb.Size = v.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : v;
                        break;
                    case "vertical-align":
                        mb.VerticalAlign = v.ToLowerInvariantAscii();
                        break;
                    default:
                        if (IsForwardable(pn))
                        {
                            AppendDeclaration(bcss, pn, v);
                        }

                        break;
                }
            }

            mb.Css = bcss.ToString();
            style.Boxes[b] = mb;
            style.HasBoxes |= mb.Content is not null;
        }

        _cache[key] = style;
        return style;
    }

    // Properties that would break the geometry of a margin box or the page context are not copied to the generated markup.
    private static bool IsForwardable(string name)
    {
        switch (name)
        {
            case "content":
            case "width":
            case "height":
            case "min-width":
            case "min-height":
            case "max-width":
            case "max-height":
            case "position":
            case "top":
            case "right":
            case "bottom":
            case "left":
            case "display":
            case "float":
            case "clear":
            case "z-index":
            case "vertical-align":
            case "transform":
            case "margin":
            case "margin-top":
            case "margin-right":
            case "margin-bottom":
            case "margin-left":
            case "box-sizing":
            case "overflow":
            case "overflow-x":
            case "overflow-y":
            case "flex":
            case "flex-direction":
            case "justify-content":
            case "align-items":
                return false;
            default:
                return true;
        }
    }

    private static void AppendDeclaration(StringBuilder sb, string name, string value)
    {
        sb.Append(name).Append(':').Append(value).Append(';');
    }

    private static void SetMargins(string?[] margin, string value)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4)
        {
            return;
        }

        margin[0] = parts[0];
        margin[1] = parts.Length > 1 ? parts[1] : parts[0];
        margin[2] = parts.Length > 2 ? parts[2] : parts[0];
        margin[3] = parts.Length > 3 ? parts[3] : margin[1];
    }

    private PageGeometry ResolveGeometry(string? size, string?[] margin)
    {
        double w = _defaults.Width, h = _defaults.Height;
        if (size is not null && !TryParseSize(size, out w, out h))
        {
            Diagnostics.Add(DiagnosticKind.InvalidValue, "page size '" + size + "' is not valid; the default size is used");
            w = _defaults.Width;
            h = _defaults.Height;
        }

        w = Math.Clamp(w, 1, 14400);
        h = Math.Clamp(h, 1, 14400);
        double Edge(int k, double fallback)
        {
            if (margin[k] is null)
            {
                return fallback;
            }

            // Percentages of margins refer to the page box width (CSS Box 3 §6.3).
            if (TryParseLength(margin[k]!, _em, w, out double px) && px >= 0)
            {
                return px;
            }

            Diagnostics.Add(DiagnosticKind.InvalidValue, "page margin '" + margin[k] + "' is not a length; the default is used");
            return fallback;
        }

        double mt = Edge(0, _defaults.MarginTop), mr = Edge(1, _defaults.MarginRight), mb = Edge(2, _defaults.MarginBottom), ml = Edge(3, _defaults.MarginLeft);

        // The page area keeps at least 1 px; an oversized margin pair is cut back evenly.
        if (w - ml - mr < 1)
        {
            double excess = ml + mr - (w - 1);
            double total = Math.Max(ml + mr, 1e-9);
            ml -= excess * ml / total;
            mr -= excess * mr / total;
        }

        if (h - mt - mb < 1)
        {
            double excess = mt + mb - (h - 1);
            double total = Math.Max(mt + mb, 1e-9);
            mt -= excess * mt / total;
            mb -= excess * mb / total;
        }

        return new PageGeometry(w, h, mt, mr, mb, ml);
    }

    // CSS Page Size 3 §3: size: auto | <length>{1,2} | [ <page-size> || [ portrait | landscape ] ]
    private bool TryParseSize(string value, out double w, out double h)
    {
        w = h = 0;
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2)
        {
            return false;
        }

        if (parts.Length == 1 && parts[0].Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            w = _defaults.Width;
            h = _defaults.Height;
            return true;
        }

        bool landscape = false, portrait = false, named = false;
        double pw = 0, ph = 0;
        int lengths = 0;
        double[] ls = new double[2];
        foreach (string raw in parts)
        {
            string p = raw.ToLowerInvariantAscii();
            if (p == "landscape")
            {
                landscape = true;
            }
            else if (p == "portrait")
            {
                portrait = true;
            }
            else if (PaperSize(p, out pw, out ph))
            {
                named = true;
            }
            else if (lengths < 2 && TryParseLength(p, _em, 0, out double px) && px > 0)
            {
                ls[lengths++] = px;
            }
            else
            {
                return false;
            }
        }

        if (landscape && portrait)
        {
            return false;
        }

        if (named && lengths == 0)
        {
            w = pw;
            h = ph;
        }
        else if (!named && lengths > 0 && !landscape && !portrait)
        {
            w = ls[0];
            h = lengths == 2 ? ls[1] : ls[0];
            return true;
        }
        else if (!named && lengths == 0)
        {
            // Only an orientation: keep the default size, turned.
            w = _defaults.Width;
            h = _defaults.Height;
        }
        else
        {
            return false;
        }

        if (landscape && w < h || portrait && w > h)
        {
            (w, h) = (h, w);
        }

        return true;
    }

    private static bool PaperSize(string name, out double w, out double h)
    {
        const double Mm = 96 / 25.4;
        const double In = 96;
        (w, h) = name switch
        {
            "a5" => (148 * Mm, 210 * Mm),
            "a4" => (210 * Mm, 297 * Mm),
            "a3" => (297 * Mm, 420 * Mm),
            "b5" => (176 * Mm, 250 * Mm),
            "b4" => (250 * Mm, 353 * Mm),
            "jis-b5" => (182 * Mm, 257 * Mm),
            "jis-b4" => (257 * Mm, 364 * Mm),
            "letter" => (8.5 * In, 11 * In),
            "legal" => (8.5 * In, 14 * In),
            "ledger" => (11 * In, 17 * In),
            _ => (0, 0),
        };
        return w > 0;
    }

    /// <summary>Parses a CSS length in px, in, cm, mm, Q, pt, pc, em, rem, ex, ch or a percentage of <paramref name="percentBase"/>; <c>0</c> without a unit is allowed.</summary>
    /// <param name="text">The value.</param>
    /// <param name="em">The size of em in px.</param>
    /// <param name="percentBase">What 100% is in px; 0 rejects percentages.</param>
    /// <param name="px">The length in px.</param>
    public static bool TryParseLength(string text, double em, double percentBase, out double px)
    {
        px = 0;
        string s = text.Trim().ToLowerInvariantAscii();
        int i = 0;
        while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] is '.' or '+' or '-'))
        {
            i++;
        }

        if (i == 0 || !double.TryParse(s.AsSpan(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v))
        {
            return false;
        }

        string unit = s[i..];
        double k;
        switch (unit)
        {
            case "px": k = 1; break;
            case "in": k = 96; break;
            case "cm": k = 96 / 2.54; break;
            case "mm": k = 96 / 25.4; break;
            case "q": k = 96 / 101.6; break;
            case "pt": k = 96.0 / 72; break;
            case "pc": k = 16; break;
            case "em":
            case "rem": k = em; break;
            case "ex":
            case "ch": k = em / 2; break;
            case "%":
                if (!(percentBase > 0))
                {
                    return false;
                }

                k = percentBase / 100;
                break;
            case "":
                if (v != 0)
                {
                    return false;
                }

                k = 1;
                break;
            default:
                return false;
        }

        px = v * k;
        return double.IsFinite(px);
    }
}
