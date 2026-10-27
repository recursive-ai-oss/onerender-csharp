using System.Text;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Tree;

/// <summary>An assignment made by <c>string-set</c> on an element (GCPM 3 §2.1).</summary>
internal readonly record struct StringSetEvent(Node Element, string Name, string Value);

/// <summary>
/// What generated content needs to know about other places in the document: the counters in scope at each element with an <c>id</c>,
/// and (once the document is paginated) the page each such element starts on. Built by one pass and read by the next, because a
/// <c>target-counter()</c> may point forward.
/// </summary>
internal sealed class ReferenceTable
{
    /// <summary>Most elements with an id whose counters are remembered.</summary>
    public const int MaxTargets = 200_000;

    /// <summary>The counters in scope at a target and the page it starts on.</summary>
    public sealed class Target
    {
        /// <summary>The element.</summary>
        public Node Element;

        /// <summary>The counters in scope at the element, in nesting order.</summary>
        public (string Name, int Value)[] Counters = [];

        /// <summary>The 1-based page number the element starts on; 0 until known.</summary>
        public int Page;
    }

    /// <summary>Targets by id.</summary>
    public Dictionary<string, Target> Targets { get; } = new(StringComparer.Ordinal);

    /// <summary>The number of pages of the previous pass, for <c>counter(pages)</c>; 0 until known.</summary>
    public int Pages { get; set; }

    /// <summary>The <c>string-set</c> assignments of the last build, in document order.</summary>
    public List<StringSetEvent> StringSets { get; } = [];

    /// <summary>Set when an evaluation needed a page number that is not known yet.</summary>
    public bool NeedsPages { get; set; }
}

/// <summary>
/// Evaluates the generated content of one build over a document: the counters of CSS Lists 3 §4, <c>content</c> values,
/// <c>string-set</c> and the targets of <c>target-counter()</c> (CSS Generated Content 3, GCPM 3). Only a document whose author sheets
/// mention a feature pays for it.
/// </summary>
internal sealed class GeneratedContent
{
    private const int MaxCounters = 4096;
    private const int MaxTextLength = 1 << 16;

    private struct Counter
    {
        public string Name;
        public int Value;
        public int Depth;
    }

    private ReferenceTable _refs = new();
    private DocumentStyles? _styles;
    private int _scanBudget;
    private bool _counters;
    private bool _strings;
    private bool _targets;
    private readonly Dictionary<string, ContentList> _parsed = new(StringComparer.Ordinal);
    private readonly StringBuilder _sb = new();
    private Counter[] _stack = new Counter[16];
    private int _n;
    private int _quoteDepth;

    /// <summary>Creates an evaluator; <see cref="Configure"/> readies it for a build.</summary>
    public GeneratedContent()
    {
    }

    /// <summary>Whether the document has leaders to place (the layout then needs the leader mark).</summary>
    public bool UsesLeaders { get; private set; }

    /// <summary>Whether some content depends on page numbers.</summary>
    public bool UsesPageReferences { get; private set; }

    /// <summary>Whether the author sheets mention a feature that needs work on every element (counters, string-set, targets).</summary>
    public static bool Mentions(DocumentStyles styles)
    {
        IReadOnlyList<StyleSheetInfo> sheets = styles.Sheets;
        for (int i = 0; i < sheets.Count; i++)
        {
            StyleSheetInfo info = sheets[i];
            if (info.Origin == StyleOrigin.Author)
            {
                string text = info.Sheet.Text;
                if (text.Contains("counter", StringComparison.OrdinalIgnoreCase) || text.Contains("string-set", StringComparison.OrdinalIgnoreCase) || text.Contains("target-", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Readies the evaluator for one build, switching on the features the author sheets use, and forgets the previous build.</summary>
    /// <param name="styles">The cascade.</param>
    /// <param name="refs">The table shared between passes.</param>
    public void Configure(DocumentStyles styles, ReferenceTable refs)
    {
        bool counters = false, strings = false, targets = false;
        IReadOnlyList<StyleSheetInfo> sheets = styles.Sheets;
        for (int i = 0; i < sheets.Count; i++)
        {
            StyleSheetInfo info = sheets[i];
            if (info.Origin != StyleOrigin.Author)
            {
                continue;
            }

            string text = info.Sheet.Text;
            counters |= text.Contains("counter", StringComparison.OrdinalIgnoreCase);
            strings |= text.Contains("string-set", StringComparison.OrdinalIgnoreCase);
            targets |= text.Contains("target-", StringComparison.OrdinalIgnoreCase);
        }

        // Style attributes are not in the sheet text; a document that sets counters only there is not supported.
        _refs = refs;
        _styles = styles;
        _scanBudget = MaxScanNodes;
        _counters = counters || targets;
        _strings = strings;
        _targets = targets;
        _n = 0;
        _quoteDepth = 0;
        UsesLeaders = false;
        UsesPageReferences = false;
        _refs.StringSets.Clear();
    }

    /// <summary>
    /// Applies the counter and string-set properties of an element (or a generated box, with <paramref name="pseudo"/>) that has
    /// just been created at nesting depth <paramref name="depth"/> (the root is 0).
    /// </summary>
    public void Enter(ComputedStyle cs, Node owner, int depth, bool pseudo)
    {
        if (_counters)
        {
            string? reset = Declared(cs, "counter-reset");
            if (reset is not null)
            {
                ForEachCounter(reset, 0, depth, 0, owner);
            }

            string? inc = Declared(cs, "counter-increment");
            if (inc is not null)
            {
                ForEachCounter(inc, 1, depth, 1, owner);
            }

            string? set = Declared(cs, "counter-set");
            if (set is not null)
            {
                ForEachCounter(set, 0, depth, 2, owner);
            }
        }

        if (_targets && !pseudo)
        {
            RecordTarget(owner);
        }

        if (_strings && !pseudo)
        {
            string? ss = Declared(cs, "string-set");
            if (ss is not null)
            {
                ApplyStringSet(ss, owner);
            }
        }
    }

    /// <summary>An element at nesting depth <paramref name="depth"/> has ended: the counters its children created go out of scope.</summary>
    public void Exit(int depth)
    {
        while (_n > 0 && _stack[_n - 1].Depth > depth)
        {
            _n--;
        }
    }

    private static string? Declared(ComputedStyle cs, string property)
    {
        StyleValue? v = cs.GetValue(property);
        if (v is null || v.IsInitial)
        {
            return null;
        }

        string t = v.Text;
        return t.Length == 0 || t.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : t;
    }

    // counter-reset / counter-increment / counter-set: [ <name> <integer>? ]+ . mode 0 reset, 1 increment, 2 set.
    private void ForEachCounter(string list, int defaultValue, int depth, int mode, Node owner)
    {
        int i = 0;
        string[] words = list.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        while (i < words.Length)
        {
            string name = words[i++];
            bool reversed = false;
            if (name.Contains('(', StringComparison.Ordinal))
            {
                // reversed(name): the counter counts down from the number of its increments.
                reversed = mode == 0 && name.StartsWith("reversed(", StringComparison.OrdinalIgnoreCase);
                int close = name.IndexOf(')', StringComparison.Ordinal);
                name = close > 0 ? name[(name.IndexOf('(', StringComparison.Ordinal) + 1)..close] : name[(name.IndexOf('(', StringComparison.Ordinal) + 1)..];
            }

            int value = defaultValue;
            bool given = false;
            if (i < words.Length && int.TryParse(words[i], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            {
                value = parsed;
                given = true;
                i++;
            }

            if (reversed && !given && name.Length > 0)
            {
                value = ReversedInitial(owner, name);
            }

            if (name.Length == 0 || name.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (mode)
            {
                case 0:
                    Push(name, value, depth, replaceSibling: true);
                    break;
                case 1:
                {
                    int k = Find(name);
                    if (k < 0)
                    {
                        Push(name, 0, depth, false);
                        k = _n - 1;
                    }

                    if (k >= 0)
                    {
                        _stack[k].Value = (int)Math.Clamp((long)_stack[k].Value + value, int.MinValue, int.MaxValue);
                    }

                    break;
                }

                default:
                {
                    int k = Find(name);
                    if (k < 0)
                    {
                        Push(name, value, depth, false);
                    }
                    else
                    {
                        _stack[k].Value = value;
                    }

                    break;
                }
            }
        }
    }

    private const int MaxScanNodes = 2_000_000;

    // CSS Lists 3 section 4.4.2: a reversed counter without a value starts at the sum of the negated increments of the elements in its
    // scope, plus the last non-zero one, so that the first of them shows the count. Elements in a nested counter of the same name, and
    // after a sibling that makes a new one, are not in this scope. The walk is bounded.
    private int ReversedInitial(Node owner, string name)
    {
        if (_styles is null)
        {
            return 0;
        }

        long num = 0, lastNonZero = 0;
        var stack = new Stack<Node>();
        for (Node sib = owner; !sib.IsNull && _scanBudget > 0; sib = sib.NextSibling)
        {
            if (sib.Kind != NodeKind.Element)
            {
                continue;
            }

            stack.Push(sib);
            while (stack.Count > 0 && _scanBudget-- > 0)
            {
                Node e = stack.Pop();
                ComputedStyle cs = _styles.GetStyle(e);
                if (IsDisplayNone(cs))
                {
                    continue;
                }

                if (e.Index != owner.Index)
                {
                    string? r = Declared(cs, "counter-reset");
                    if (r is not null && MentionsCounter(r, name, 0, out _))
                    {
                        if (e.Parent.Index == owner.Parent.Index)
                        {
                            return Finish(num, lastNonZero);
                        }

                        continue;
                    }
                }

                string? st = Declared(cs, "counter-set");
                int inc = 0;
                bool incremented = false;
                string? incText = Declared(cs, "counter-increment");
                if (incText is not null && MentionsCounter(incText, name, 1, out int incValue))
                {
                    inc = incValue;
                    incremented = true;
                }
                else if (name == "list-item" && IsListItem(cs))
                {
                    inc = -1; // a list item of a reversed list counts down by one
                    incremented = true;
                }

                if (incremented)
                {
                    long negated = -(long)inc;
                    if (negated != 0)
                    {
                        lastNonZero = negated;
                    }

                    num += negated;
                }

                if (st is not null && MentionsCounter(st, name, 0, out int setValue))
                {
                    return Finish(num + setValue, lastNonZero, false);
                }

                // Children are pushed in reverse so they come out in tree order.
                int before = stack.Count;
                for (Node c = e.LastChild; !c.IsNull; c = c.PreviousSibling)
                {
                    if (c.Kind == NodeKind.Element)
                    {
                        stack.Push(c);
                    }
                }

                if (stack.Count - before > 100_000)
                {
                    break;
                }
            }

            stack.Clear();
        }

        return Finish(num, lastNonZero);
    }

    private static int Finish(long num, long lastNonZero, bool addLast = true) =>
        (int)Math.Clamp(addLast ? num + lastNonZero : num, int.MinValue, int.MaxValue);

    private static bool IsDisplayNone(ComputedStyle cs)
    {
        StyleValue? v = cs.GetValue("display");
        return v is not null && !v.IsInitial && v.Text.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsListItem(ComputedStyle cs)
    {
        StyleValue? v = cs.GetValue("display");
        return v is not null && !v.IsInitial && v.Text.Contains("list-item", StringComparison.OrdinalIgnoreCase);
    }

    // Whether a counter-reset / -increment / -set list names the counter; value is its integer, or the default.
    private static bool MentionsCounter(string list, string name, int defaultValue, out int value)
    {
        value = defaultValue;
        string[] words = list.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            int open = w.IndexOf('(', StringComparison.Ordinal);
            if (open >= 0)
            {
                int close = w.IndexOf(')', StringComparison.Ordinal);
                w = close > open ? w[(open + 1)..close] : w[(open + 1)..];
            }

            if (!string.Equals(w, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 < words.Length && int.TryParse(words[i + 1], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            {
                value = parsed;
            }

            return true;
        }

        return false;
    }

    private int Find(string name)
    {
        for (int k = _n - 1; k >= 0; k--)
        {
            if (string.Equals(_stack[k].Name, name, StringComparison.Ordinal))
            {
                return k;
            }
        }

        return -1;
    }

    // CSS Lists 3 §4.4.1: instantiating a counter replaces one of the same name made by this element or an earlier sibling, which is
    // the innermost counter of that name when it was made at this depth.
    private void Push(string name, int value, int depth, bool replaceSibling)
    {
        if (replaceSibling)
        {
            int k = Find(name);
            if (k >= 0 && _stack[k].Depth == depth)
            {
                _stack[k].Value = value;
                return;
            }
        }

        if (_n >= MaxCounters)
        {
            return;
        }

        if (_n == _stack.Length)
        {
            Array.Resize(ref _stack, _n * 2);
        }

        _stack[_n++] = new Counter { Name = name, Value = value, Depth = depth };
    }

    private void RecordTarget(Node element)
    {
        string? id = element.GetAttribute("id");
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        if (!_refs.Targets.TryGetValue(id, out ReferenceTable.Target? t))
        {
            if (_refs.Targets.Count >= ReferenceTable.MaxTargets)
            {
                return;
            }

            t = new ReferenceTable.Target();
            _refs.Targets[id] = t;
        }
        else if (!t.Element.IsNull && t.Element.Index != element.Index)
        {
            return; // the first element with an id owns it
        }

        t.Element = element;
        var snap = new (string, int)[_n];
        for (int k = 0; k < _n; k++)
        {
            snap[k] = (_stack[k].Name, _stack[k].Value);
        }

        t.Counters = snap;
    }

    private void ApplyStringSet(string value, Node owner)
    {
        // string-set: [ <custom-ident> <content-list> ]#
        foreach (string part in SplitTopLevel(value))
        {
            string p = part.Trim();
            int sp = 0;
            while (sp < p.Length && !char.IsWhiteSpace(p[sp]))
            {
                sp++;
            }

            if (sp == 0 || sp >= p.Length)
            {
                continue;
            }

            string name = p[..sp];
            string text = Evaluate(p[sp..].Trim(), owner, forStringSet: true);
            if (_refs.StringSets.Count < 1_000_000)
            {
                _refs.StringSets.Add(new StringSetEvent(owner, name, text));
            }
        }
    }

    private static List<string> SplitTopLevel(string s)
    {
        var parts = new List<string>(2);
        int depth = 0, start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '"' or '\'')
            {
                char q = c;
                for (i++; i < s.Length && s[i] != q; i++)
                {
                    if (s[i] == '\\')
                    {
                        i++;
                    }
                }
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
                parts.Add(s[start..i]);
                start = i + 1;
            }
        }

        parts.Add(s[start..]);
        return parts;
    }

    /// <summary>Evaluates the text of a <c>content</c> value for a generated box of <paramref name="owner"/>.</summary>
    /// <param name="raw">The value as written.</param>
    /// <param name="owner">The element that owns the generated box (its attributes are read by <c>attr()</c>).</param>
    /// <param name="forStringSet">True when evaluating a <c>string-set</c> value, where <c>content()</c> reads the element's own text.</param>
    public string Evaluate(string raw, Node owner, bool forStringSet = false)
    {
        if (!_parsed.TryGetValue(raw, out ContentList? cv))
        {
            cv = ContentList.Parse(raw);
            if (_parsed.Count < 4096)
            {
                _parsed[raw] = cv;
            }
        }

        UsesLeaders |= cv.UsesLeaders;
        UsesPageReferences |= cv.UsesPageReferences || cv.UsesPageCounters;
        _sb.Clear();
        foreach (GeneratedItem it in cv.Items)
        {
            if (_sb.Length >= MaxTextLength)
            {
                break;
            }

            AppendItem(it, owner);
        }

        return _sb.ToString();
    }

    private void AppendItem(in GeneratedItem it, Node owner)
    {
        switch (it.Kind)
        {
            case GeneratedItemKind.Text:
                _sb.Append(it.A);
                break;
            case GeneratedItemKind.Counter:
            {
                if (ContentList.IsPageCounter(it.A))
                {
                    // The page a box lands on is only known after pagination; counter(pages) is the previous pass's count.
                    CounterStyles.Append(_sb, it.A.Equals("pages", StringComparison.OrdinalIgnoreCase) ? _refs.Pages : 0, it.B);
                    break;
                }

                int k = Find(it.A);
                CounterStyles.Append(_sb, k < 0 ? 0 : _stack[k].Value, it.B);
                break;
            }

            case GeneratedItemKind.Counters:
            {
                bool any = false;
                for (int k = 0; k < _n; k++)
                {
                    if (string.Equals(_stack[k].Name, it.A, StringComparison.Ordinal))
                    {
                        if (any)
                        {
                            _sb.Append(it.B);
                        }

                        CounterStyles.Append(_sb, _stack[k].Value, it.C);
                        any = true;
                    }
                }

                if (!any)
                {
                    CounterStyles.Append(_sb, 0, it.C);
                }

                break;
            }

            case GeneratedItemKind.Attr:
                if (!owner.IsNull && owner.GetAttribute(it.A) is { } av)
                {
                    _sb.Append(av);
                }

                break;
            case GeneratedItemKind.OpenQuote:
                _sb.Append(_quoteDepth % 2 == 0 ? '\u201C' : '\u2018');
                _quoteDepth++;
                break;
            case GeneratedItemKind.CloseQuote:
                _quoteDepth = Math.Max(0, _quoteDepth - 1);
                _sb.Append(_quoteDepth % 2 == 0 ? '\u201D' : '\u2019');
                break;
            case GeneratedItemKind.NoOpenQuote:
                _quoteDepth++;
                break;
            case GeneratedItemKind.NoCloseQuote:
                _quoteDepth = Math.Max(0, _quoteDepth - 1);
                break;
            case GeneratedItemKind.TargetCounter:
            case GeneratedItemKind.TargetCounters:
            case GeneratedItemKind.TargetText:
                AppendTarget(in it, owner);
                break;
            case GeneratedItemKind.Leader:
                _sb.Append(ContentList.LeaderStart).Append(it.A).Append(ContentList.LeaderEnd);
                break;
            case GeneratedItemKind.ElementContent:
                if (it.B == "text" || it.B == "content")
                {
                    AppendCollapsed(owner);
                }

                break;
            default:
                break; // string() lives in page margin boxes; images and element() are not drawn
        }
    }

    private void AppendTarget(in GeneratedItem it, Node owner)
    {
        string link = it.A;
        if (it.AIsAttr)
        {
            link = owner.IsNull ? string.Empty : owner.GetAttribute(it.A) ?? string.Empty;
        }

        // Only links into this document are resolved: "#id".
        if (link.Length < 2 || link[0] != '#')
        {
            return;
        }

        string id = Uri.UnescapeDataString(link[1..]);
        if (!_refs.Targets.TryGetValue(id, out ReferenceTable.Target? t))
        {
            if (it.Kind != GeneratedItemKind.TargetText)
            {
                _refs.NeedsPages = true;
            }

            return;
        }

        switch (it.Kind)
        {
            case GeneratedItemKind.TargetText:
                if (it.B == "content" || it.B == "text")
                {
                    AppendCollapsed(t.Element);
                }

                break;
            case GeneratedItemKind.TargetCounter:
            {
                if (it.B.Equals("page", StringComparison.OrdinalIgnoreCase))
                {
                    if (t.Page == 0)
                    {
                        _refs.NeedsPages = true;
                    }

                    CounterStyles.Append(_sb, t.Page, it.C);
                }
                else if (it.B.Equals("pages", StringComparison.OrdinalIgnoreCase))
                {
                    CounterStyles.Append(_sb, _refs.Pages, it.C);
                }
                else
                {
                    int value = 0;
                    for (int k = t.Counters.Length - 1; k >= 0; k--)
                    {
                        if (string.Equals(t.Counters[k].Name, it.B, StringComparison.Ordinal))
                        {
                            value = t.Counters[k].Value;
                            break;
                        }
                    }

                    CounterStyles.Append(_sb, value, it.C);
                }

                break;
            }

            default:
            {
                bool any = false;
                foreach ((string name, int value) in t.Counters)
                {
                    if (string.Equals(name, it.B, StringComparison.Ordinal))
                    {
                        if (any)
                        {
                            _sb.Append(it.C);
                        }

                        CounterStyles.Append(_sb, value, it.D);
                        any = true;
                    }
                }

                break;
            }
        }
    }

    // The element's text with runs of white space collapsed to one space and the ends trimmed.
    private void AppendCollapsed(Node element)
    {
        if (element.IsNull)
        {
            return;
        }

        string text = element.TextContent ?? string.Empty;
        bool space = false;
        int start = _sb.Length;
        foreach (char c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                space = _sb.Length > start;
                continue;
            }

            if (space)
            {
                _sb.Append(' ');
                space = false;
            }

            _sb.Append(c);
            if (_sb.Length - start >= MaxTextLength)
            {
                break;
            }
        }
    }
}
