using System.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OneRender.Interaction;
using OneRender.Layout;
using OneRender.Painting;
using OneRender.Style;
using OneRender.Tree;

namespace OneRender.TestSupport;

/// <summary>Source of the random choices of an <see cref="EditSession"/>: a seeded generator, or the bytes a fuzzer hands in.</summary>
public sealed class Chooser
{
    private readonly Random? _random;
    private readonly byte[]? _data;
    private int _pos;

    /// <summary>Chooses with a seeded pseudo-random generator.</summary>
    /// <param name="seed">The seed.</param>
    public Chooser(int seed) => _random = new Random(seed);

    /// <summary>Chooses from bytes; reads zeros after the end.</summary>
    /// <param name="data">The bytes.</param>
    public Chooser(byte[] data) => _data = data;

    /// <summary>True when a byte source has been read to its end.</summary>
    public bool Exhausted => _data is not null && _pos >= _data.Length;

    /// <summary>A number in [0, <paramref name="count"/>).</summary>
    /// <param name="count">At least 1.</param>
    public int Next(int count)
    {
        if (_random is not null)
        {
            return _random.Next(count);
        }

        int v = 0;
        for (int i = 0; i < 2; i++)
        {
            v = (v << 8) | (_pos < _data!.Length ? _data[_pos] : 0);
            _pos++;
        }

        return v % count;
    }

    /// <summary>One of the items.</summary>
    /// <param name="items">At least one.</param>
    public T Pick<T>(T[] items) => items[Next(items.Length)];
}

/// <summary>Counts what the flushes of an <see cref="EditSession"/> did.</summary>
public sealed class EditStats
{
    /// <summary>Flushes that changed nothing.</summary>
    public int Nothing { get; private set; }

    /// <summary>Flushes that only repainted.</summary>
    public int PaintOnly { get; private set; }

    /// <summary>Flushes that patched boxes and laid out.</summary>
    public int Patched { get; private set; }

    /// <summary>Flushes that generated the boxes again.</summary>
    public int Rebuilt { get; private set; }

    /// <summary>Subtrees layout took from the layout before, summed over the flushes.</summary>
    public long Reused { get; private set; }

    internal void Count(FlushResult r)
    {
        if (r.Work == FlushWork.None)
        {
            Nothing++;
        }
        else if ((r.Work & FlushWork.BoxesRebuilt) != 0)
        {
            Rebuilt++;
        }
        else if ((r.Work & FlushWork.Relaid) != 0)
        {
            Patched++;
        }
        else
        {
            PaintOnly++;
        }

        Reused += r.ReusedSubtrees;
    }

    /// <inheritdoc />
    public override string ToString() => $"nothing {Nothing}, paint-only {PaintOnly}, patched {Patched}, rebuilt {Rebuilt}, reused subtrees {Reused}";
}

/// <summary>
/// Applies random edits (declarations, states, text, inserts, removals, moves, viewport changes) to a live page and, after every
/// one, checks the page against a page built from scratch from the same content: styles, boxes, fragments and display list must be
/// identical. The session keeps its own model of the tree, so the expected result does not come from the code under test.
/// </summary>
public sealed class EditSession
{
    private sealed class M
    {
        public string Tag = "div";
        public StyleDeclaration? Decl;
        public StyleDeclaration? HoverDecl;
        public bool Hovered;
        public string? Text;
        public M? Parent;
        public readonly List<M> Kids = [];
        public StyledNode Live;
    }

    private static readonly (string Property, string[] Values)[] Pool =
    [
        ("display", ["block", "block", "inline", "inline-block", "flex", "flex", "inline-flex", "grid", "none", "flow-root", "table", "table-row", "table-cell", "list-item", "contents", "inline"]),
        ("width", ["auto", "40px", "120px", "50%", "10em", "min-content", "max-content", "fit-content", "200px"]),
        ("height", ["auto", "20px", "60px", "30%", "auto"]),
        ("min-width", ["0", "30px", "auto"]),
        ("max-width", ["none", "80px", "50%"]),
        ("min-height", ["0", "20px"]),
        ("max-height", ["none", "50px"]),
        ("margin", ["0", "4px", "10px 20px", "auto", "-3px 5px", "0 auto", "8px"]),
        ("padding", ["0", "3px", "8px 2px", "5px"]),
        ("border", ["none", "1px solid #f00", "3px dashed #333", "2px solid #00f"]),
        ("box-sizing", ["border-box", "content-box"]),
        ("position", ["static", "relative", "relative", "absolute", "sticky", "fixed"]),
        ("top", ["0", "5px", "10%", "auto"]),
        ("left", ["0", "5px", "10%", "auto"]),
        ("right", ["0", "auto", "7px"]),
        ("bottom", ["0", "auto", "4px"]),
        ("float", ["none", "left", "right", "none"]),
        ("clear", ["none", "left", "both"]),
        ("overflow", ["visible", "hidden", "auto", "scroll", "visible"]),
        ("font-size", ["10px", "14px", "1.5em", "120%", "20px"]),
        ("line-height", ["normal", "1.2", "20px", "2"]),
        ("text-align", ["left", "center", "right", "justify"]),
        ("white-space", ["normal", "pre", "nowrap", "pre-line", "pre-wrap"]),
        ("letter-spacing", ["0", "1px"]),
        ("word-spacing", ["0", "3px"]),
        ("vertical-align", ["baseline", "top", "middle", "bottom", "3px"]),
        ("flex", ["1", "0 0 50px", "none", "2 1 auto"]),
        ("flex-direction", ["row", "column", "row-reverse"]),
        ("flex-wrap", ["nowrap", "wrap"]),
        ("justify-content", ["center", "space-between", "flex-end", "flex-start"]),
        ("align-items", ["center", "stretch", "flex-start", "baseline"]),
        ("align-self", ["center", "flex-end", "auto"]),
        ("gap", ["0", "5px 10px", "4px"]),
        ("order", ["0", "2", "-1"]),
        ("grid-template-columns", ["1fr 2fr", "repeat(3, 40px)", "auto auto", "none"]),
        ("grid-template-rows", ["auto", "30px 30px", "none"]),
        ("grid-column", ["span 2", "1 / 3", "auto"]),
        ("grid-row", ["span 2", "auto", "2"]),
        ("column-count", ["2", "3", "auto"]),
        ("column-gap", ["5px", "20px"]),
        ("color", ["#123456", "red", "rgb(10 20 30)", "currentcolor"]),
        ("background-color", ["#abcdef", "transparent", "rgb(200 100 50)"]),
        ("opacity", ["0.5", "1"]),
        ("transform", ["rotate(10deg)", "translate(5px, 3px)", "scale(1.1)", "none"]),
        ("translate", ["10px 5px", "none", "3px"]),
        ("rotate", ["15deg", "none"]),
        ("scale", ["1.2", "none", "0.8 1.1"]),
        ("transform-origin", ["0 0", "center", "100% 50%"]),
        ("perspective", ["none", "200px"]),
        ("transform-style", ["flat", "preserve-3d"]),
        ("filter", ["none", "blur(2px)", "opacity(0.5)", "grayscale(1) brightness(1.2)", "drop-shadow(2px 2px 3px #000)"]),
        ("backdrop-filter", ["none", "blur(3px)"]),
        ("clip-path", ["none", "inset(2px 4px)", "circle(40% at 50% 50%)", "polygon(0 0, 100% 0, 50% 100%)"]),
        ("mask-image", ["none", "linear-gradient(black, transparent)"]),
        ("mix-blend-mode", ["normal", "multiply", "screen"]),
        ("isolation", ["auto", "isolate"]),
        ("--host-scope", ["none", "3", "-1", "none", "40"]),
        ("z-index", ["1", "-1", "auto"]),
        ("visibility", ["hidden", "visible"]),
        ("direction", ["ltr", "rtl"]),
        ("text-indent", ["10px", "0"]),
        ("text-transform", ["uppercase", "none"]),
        ("list-style-position", ["inside", "outside"]),
        ("border-collapse", ["collapse", "separate"]),
        ("border-spacing", ["2px", "0"]),
        ("table-layout", ["fixed", "auto"]),
    ];

    // The calm profile leaves out what makes a subtree unreusable or changes which boxes exist, so edits mostly reach the retained paths.
    private static readonly (string Property, string[] Values)[] CalmPool = BuildCalmPool();

    private static (string, string[])[] BuildCalmPool()
    {
        var list = new List<(string, string[])>();
        foreach ((string p, string[] v) in Pool)
        {
            string[] values = p switch
            {
                "display" => ["block", "flex", "flex", "inline-block", "flow-root", "grid", "inline", "block"],
                "position" => ["static", "relative", "sticky"],
                "float" => ["none"],
                "column-count" => ["auto"],
                "overflow" => ["visible", "hidden", "auto", "hidden"],
                _ => v,
            };
            if (p is "clear" or "column-gap" or "top" or "left" or "right" or "bottom" or "z-index" or "border-collapse" or "border-spacing" or "table-layout")
            {
                continue;
            }

            list.Add((p, values));
        }

        return list.ToArray();
    }

    private static readonly string[] CalmTexts = ["a", "Hello world", "lorem ipsum dolor sit amet consectetur adipiscing elit", "x y z", "Gold 100", "Gold 1000"];

    private static readonly string[] Texts = ["a", "Hello world", "lorem ipsum dolor sit amet consectetur adipiscing elit", " ", "\n", "x y z", "Mixed \u05E9\u05DC\u05D5\u05DD text", "longwordwithoutanybreakopportunity"];
    private static readonly string[] Tags = ["div", "span", "p", "section", "b"];

    private readonly bool _calm;
    private readonly (string Property, string[] Values)[] _pool;
    private readonly string[] _texts;
    private readonly IFontProvider _fonts;
    private readonly Chooser _c;
    private readonly M _root = new();
    private double _vw = 800;
    private double _vh = 600;
    private string _lastEdit = "initial";
    private int _steps;

    /// <summary>Builds a random page of about <paramref name="size"/> nodes.</summary>
    /// <param name="fonts">The text service; sessions may share one.</param>
    /// <param name="chooser">The random choices.</param>
    /// <param name="size">The approximate number of nodes, at least 1.</param>
    /// <param name="calm">Leaves out floats, out-of-flow positioning, columns, tables and display changes that remove boxes, and favors edits of text, states and declarations.</param>
    public EditSession(IFontProvider fonts, Chooser chooser, int size = 40, bool calm = false)
    {
        _fonts = fonts;
        _c = chooser;
        _calm = calm;
        _pool = calm ? CalmPool : Pool;
        _texts = calm ? CalmTexts : Texts;
        _root.Decl = RandomDeclaration(2);
        Grow(size);
        Tree = new StyledTree();
        Populate(Tree, _root);
        Page = new LivePage(Tree, Options(_vw, _vh));
    }

    /// <summary>How the flushes went.</summary>
    public EditStats Stats { get; } = new();

    /// <summary>The styled tree being edited.</summary>
    public StyledTree Tree { get; }

    /// <summary>The page under test.</summary>
    public LivePage Page { get; }

    /// <summary>The number of edits applied.</summary>
    public int Steps => _steps;

    /// <summary>True when the chooser reads bytes and has used them all.</summary>
    public bool Exhausted => _c.Exhausted;

    /// <summary>The last edit, described.</summary>
    public string LastEdit => _lastEdit;

    private LivePageOptions Options(double w, double h) => new(_fonts) { ViewportWidth = w, ViewportHeight = h };

    private void Grow(int size)
    {
        var all = new List<M> { _root };
        int guard = 0;
        while (all.Count < size && guard++ < size * 8)
        {
            M parent = all[_c.Next(all.Count)];
            if (parent.Text is not null || Depth(parent) > 4)
            {
                continue;
            }

            M kid = RandomNode(true);
            kid.Parent = parent;
            parent.Kids.Add(kid);
            all.Add(kid);
        }
    }

    private static int Depth(M m)
    {
        int d = 0;
        for (M? p = m.Parent; p is not null; p = p.Parent)
        {
            d++;
        }

        return d;
    }

    private M RandomNode(bool allowText)
    {
        if (allowText && _c.Next(4) == 0)
        {
            return new M { Text = _c.Pick(_texts) };
        }

        var m = new M { Tag = _c.Pick(Tags), Decl = _c.Next(5) == 0 ? null : RandomDeclaration(4) };
        if (_c.Next(5) == 0)
        {
            m.HoverDecl = RandomDeclaration(2);
        }

        return m;
    }

    private string _lastDecl = "";
    private readonly List<string> _history = [];

    private StyleDeclaration RandomDeclaration(int maxProps)
    {
        var d = new StyleDeclaration();
        int n = 1 + _c.Next(maxProps);
        _lastDecl = "";
        for (int i = 0; i < n; i++)
        {
            (string p, string[] v) = _c.Pick(_pool);
            string val = _c.Pick(v);
            d.Set(p, val);
            _lastDecl += $"{p}:{val}; ";
        }

        return d;
    }

    private static void Populate(StyledTree tree, M root)
    {
        var stack = new Stack<M>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            M m = stack.Pop();
            if (m.Text is not null)
            {
                m.Live = tree.AddText(m.Parent!.Live, m.Text);
                continue;
            }

            m.Live = m.Parent is null ? tree.AddRoot(m.Tag, m.Decl) : tree.AddElement(m.Parent.Live, m.Tag, m.Decl);
            if (m.HoverDecl is not null)
            {
                tree.SetStateStyle(m.Live, ElementStates.Hover, m.HoverDecl);
            }

            if (m.Hovered)
            {
                tree.SetStates(m.Live, ElementStates.Hover);
            }

            for (int i = m.Kids.Count - 1; i >= 0; i--)
            {
                stack.Push(m.Kids[i]);
            }
        }
    }

    // A live node of a fresh tree exists only inside this method; the model's own handles belong to the page under test.
    /// <summary>A page built from scratch from the session's current content.</summary>
    public LivePage BuildFresh()
    {
        var saved = new List<(M, StyledNode)>();
        var stack = new Stack<M>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            M m = stack.Pop();
            saved.Add((m, m.Live));
            foreach (M k in m.Kids)
            {
                stack.Push(k);
            }
        }

        var tree = new StyledTree();
        Populate(tree, _root);
        var page = new LivePage(tree, Options(_vw, _vh));
        foreach ((M m, StyledNode live) in saved)
        {
            m.Live = live;
        }

        return page;
    }

    /// <summary>Applies one random edit, flushes, and compares with a fresh build.</summary>
    /// <returns>The edit, described.</returns>
    /// <exception cref="InvalidOperationException">The incremental result differs from the fresh build.</exception>
    public string Step()
    {
        _steps++;
        ApplyRandomEdit();
        _history.Add(_lastEdit);
        if (_history.Count > 6)
        {
            _history.RemoveAt(0);
        }

        FlushResult r = Page.Flush();
        Stats.Count(r);
        CheckAgainstFresh();
        return _lastEdit;
    }

    /// <summary>Compares the page with a fresh build of the same content.</summary>
    /// <exception cref="InvalidOperationException">They differ.</exception>
    public void CheckAgainstFresh()
    {
        LivePage fresh = BuildFresh();
        string a = Snapshot(Page), b = Snapshot(fresh);
        if (!string.Equals(a, b, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Step {_steps} ({_lastEdit}): incremental page differs from a fresh build.\nrecent edits: {string.Join(" | ", _history)}\n{FirstDifference(a, b)}");
        }
    }

    /// <summary>A text form of everything the pipeline produced: styles, boxes, fragments and display list items with their ids.</summary>
    /// <param name="page">A page.</param>
    public static string Snapshot(LivePage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var sb = new StringBuilder();
        sb.Append(page.Styled.Dump()).Append("\n--boxes--\n").Append(page.Boxes.Dump()).Append("\n--fragments--\n").Append(page.Fragments.Dump()).Append("\n--list--\n");
        DisplayList l = page.Display.Current;
        for (int i = 0; i < l.Count; i++)
        {
            DisplayItem it = l[i];
            RectF r = it.RootBounds;
            sb.Append(it.Kind).Append(' ').Append(it.Id.Value.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(r.X.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(r.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(r.Width.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(r.Height.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(it.ContentHash.ToString("x", CultureInfo.InvariantCulture)).Append(' ').Append(it.ContextHash.ToString("x", CultureInfo.InvariantCulture)).Append(' ')
                .Append(it.ParentIndex.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return sb.ToString();
    }

    private static string FirstDifference(string a, string b)
    {
        string[] x = a.Split('\n'), y = b.Split('\n');
        int n = Math.Min(x.Length, y.Length);
        for (int i = 0; i < n; i++)
        {
            if (!string.Equals(x[i], y[i], StringComparison.Ordinal))
            {
                string ctx = string.Join("\n", x.Skip(Math.Max(0, i - 8)).Take(8));
                return $"line {i}:\n  incremental: {x[i]}\n  fresh:       {y[i]}\n  before:\n{ctx}";
            }
        }

        return $"lengths differ: incremental {x.Length} lines, fresh {y.Length} lines";
    }

    private List<M> Attached(bool elementsOnly)
    {
        var list = new List<M>();
        var stack = new Stack<M>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            M m = stack.Pop();
            if (!elementsOnly || m.Text is null)
            {
                list.Add(m);
            }

            for (int i = m.Kids.Count - 1; i >= 0; i--)
            {
                stack.Push(m.Kids[i]);
            }
        }

        return list;
    }

    private void ApplyRandomEdit()
    {
        List<M> elements = Attached(true);
        int kind = _calm && _c.Next(12) != 0 ? _c.Next(18) : _c.Next(26);
        M e = elements[_c.Next(elements.Count)];
        switch (kind)
        {
            case < 10:
            {
                (string p, string[] v) = _c.Pick(_pool);
                string value = _c.Pick(v);
                Tree.SetProperty(e.Live, p, value);
                e.Decl = Tree.GetDeclaration(e.Live);
                _lastEdit = $"set {p}:{value} on {e.Tag}";
                break;
            }

            case < 12:
                e.Decl = _c.Next(8) == 0 ? null : RandomDeclaration(5);
                Tree.SetDeclaration(e.Live, e.Decl);
                _lastEdit = "replace declaration on " + e.Tag + " { " + _lastDecl + "}";
                break;
            case < 15:
                if (e.HoverDecl is null)
                {
                    e.HoverDecl = RandomDeclaration(3);
                    Tree.SetStateStyle(e.Live, ElementStates.Hover, e.HoverDecl);
                    _lastEdit = "add hover declaration";
                }
                else
                {
                    e.Hovered = !e.Hovered;
                    Tree.SetStates(e.Live, e.Hovered ? ElementStates.Hover : ElementStates.None);
                    _lastEdit = "hover " + e.Hovered;
                }

                break;
            case < 18:
            {
                List<M> texts = Attached(false).FindAll(m => m.Text is not null);
                if (texts.Count == 0)
                {
                    _lastEdit = "no text";
                    break;
                }

                M t = texts[_c.Next(texts.Count)];
                t.Text = _c.Pick(_texts);
                Tree.SetText(t.Live, t.Text);
                _lastEdit = $"text '{t.Text}'";
                break;
            }

            case < 21:
            {
                M n = RandomNode(true);
                int at = _c.Next(e.Kids.Count + 1);
                StyledNode before = at < e.Kids.Count ? e.Kids[at].Live : default;
                n.Parent = e;
                e.Kids.Insert(at, n);
                InsertLive(n, before);
                _lastEdit = "insert " + (n.Text ?? n.Tag);
                break;
            }

            case < 23:
            {
                List<M> all = Attached(false);
                if (all.Count <= 1)
                {
                    _lastEdit = "nothing";
                    break;
                }

                M victim = all[1 + _c.Next(all.Count - 1)];
                Tree.Remove(victim.Live);
                victim.Parent!.Kids.Remove(victim);
                victim.Parent = null;
                _lastEdit = "remove " + (victim.Text ?? victim.Tag);
                break;
            }

            case < 25:
            {
                List<M> all = Attached(false);
                if (all.Count <= 1)
                {
                    _lastEdit = "nothing";
                    break;
                }

                M node = all[1 + _c.Next(all.Count - 1)];
                if (node == _root)
                {
                    _lastEdit = "nothing";
                    break;
                }

                bool inside = false;
                for (M? a = e; a is not null; a = a.Parent)
                {
                    if (a == node)
                    {
                        inside = true;
                    }
                }

                if (inside)
                {
                    _lastEdit = "nothing";
                    break;
                }

                int at = _c.Next(e.Kids.Count + 1);
                node.Parent!.Kids.Remove(node);
                node.Parent = e;
                at = Math.Min(at, e.Kids.Count);
                e.Kids.Insert(at, node);
                // The model's order after the removal decides where the node goes among its new siblings.
                StyledNode before = at + 1 < e.Kids.Count ? e.Kids[at + 1].Live : default;
                Tree.Move(node.Live, e.Live, before);
                _lastEdit = "move " + (node.Text ?? node.Tag);
                break;
            }

            default:
                _vw = 300 + _c.Next(1100);
                _vh = 200 + _c.Next(700);
                Page.Resize(_vw, _vh);
                _lastEdit = $"resize {_vw}x{_vh}";
                break;
        }
    }

    // Inserts a model node (and its descendants) into the live tree.
    private void InsertLive(M n, StyledNode before)
    {
        M parent = n.Parent!;
        if (n.Text is not null)
        {
            n.Live = Tree.InsertText(parent.Live, before, n.Text);
            return;
        }

        n.Live = Tree.InsertElement(parent.Live, before, n.Tag, n.Decl);
        if (n.HoverDecl is not null)
        {
            Tree.SetStateStyle(n.Live, ElementStates.Hover, n.HoverDecl);
        }

        foreach (M k in n.Kids)
        {
            InsertLive(k, default);
        }
    }
}
