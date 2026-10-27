using System;
using System.Collections.Generic;
using System.Text;
using OneRender.Style;
using OneRender.Tree;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;

namespace OneRender.Fuzz;

/// <summary>The registry of targets, keyed by name. A target must not throw for any input; a finding is an exception or a broken invariant.</summary>
internal static class Targets
{
    public static readonly SortedDictionary<string, Action<ReadOnlySpan<byte>>> All = new(StringComparer.Ordinal)
    {
        ["noop"] = static _ => { },
        ["values"] = Values,
        ["tree"] = Tree,
        ["html"] = Html,
    };

    private static readonly string[] PropertyNames =
    [
        "font-size", "display", "position", "float", "clear", "box-sizing", "top", "right", "bottom", "left",
        "width", "height", "min-width", "min-height", "max-width", "max-height", "aspect-ratio",
        "margin-top", "margin-right", "margin-bottom", "margin-left", "margin",
        "padding-top", "padding-right", "padding-bottom", "padding-left", "padding",
        "border-top-width", "border-right-width", "border-bottom-width", "border-left-width", "border-width", "border",
        "border-top-style", "border-style", "border-top-color", "border-color",
        "border-top-left-radius", "border-bottom-right-radius", "border-radius",
        "overflow-x", "overflow-y", "overflow", "z-index", "visibility",
        "font-family", "font-weight", "font-style", "font-stretch", "font", "line-height", "vertical-align",
        "white-space-collapse", "text-wrap-mode", "white-space", "text-align", "text-indent", "letter-spacing", "word-spacing",
        "direction", "unicode-bidi", "color", "opacity", "background-color", "background", "content",
        "list-style-type", "list-style-position", "list-style",
    ];

    private static readonly string[] Displays =
    [
        "block", "inline", "inline-block", "flow-root", "list-item", "flex", "inline-flex", "grid", "table", "table-row", "table-cell",
        "table-caption", "contents", "none", "inline list-item", "block ruby", "run-in",
    ];

    private static readonly string[] Tags = ["div", "span", "p", "img", "br", "ul", "li", "b", "table", "video", "svg"];

    /// <summary>
    /// First byte picks a property (and encoding: odd bytes are UTF-16), the rest of the first line is the value. The value is declared on
    /// a parent and a child under a root with a different font size, then the styles are computed through the builder front end.
    /// </summary>
    private static void Values(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        string name = PropertyNames[data[0] % PropertyNames.Length];
        string text = Decode(data);
        int nl = text.IndexOf('\n', StringComparison.Ordinal);
        string first = nl < 0 ? text : text[..nl];
        var parentDecl = new StyleDeclaration();
        var childDecl = new StyleDeclaration();
        parentDecl.TrySet(name, first);
        childDecl.TrySet(name, nl < 0 ? "inherit" : text[(nl + 1)..]);
        var tree = new StyledTree();
        tree.Styles.SetViewport(800, 600);
        StyledNode root = tree.AddRoot("html", new StyleDeclaration().Set("font-size", "20px"));
        StyledNode parent = tree.AddElement(root, "div", parentDecl);
        StyledNode child = tree.AddElement(parent, "span", childDecl);
        tree.AddText(child, "x");
        _ = child.Values;
        var boxes = new BoxTree(tree);
        boxes.Build();
        _ = boxes.Dump();
    }

    /// <summary>
    /// Builds a random tree through the builder front end: each byte is an operation (open an element, close one, add text, add a leaf,
    /// declare a property). Small limits make the limit paths reachable. The box dump must be the same after a second build into the
    /// retained arenas.
    /// </summary>
    private static void Tree(ReadOnlySpan<byte> data)
    {
        var limits = new TreeLimits { MaxNodes = 2048, MaxDepth = 96, MaxTextLength = 1 << 14 };
        var tree = new StyledTree(null, limits);
        var boxes = new BoxTree(tree);
        string first = string.Empty;
        for (int pass = 0; pass < 2; pass++)
        {
            tree.Clear();
            tree.Styles.SetViewport(800, 600);
            StyledNode cur = tree.AddRoot("html", new StyleDeclaration().Set("display", "block"));
            var stack = new Stack<StyledNode>();
            stack.Push(cur);
            var decl = new StyleDeclaration();
            for (int i = 0; i + 1 < data.Length; i += 2)
            {
                int op = data[i] & 7;
                int arg = data[i + 1];
                switch (op)
                {
                    case 0:
                    case 1:
                        decl.Set("display", Displays[arg % Displays.Length]);
                        cur = tree.AddElement(cur, Tags[arg % Tags.Length], decl);
                        if (!cur.IsNull)
                        {
                            stack.Push(cur);
                        }
                        else
                        {
                            cur = stack.Peek();
                        }

                        decl = new StyleDeclaration();
                        break;
                    case 2:
                        if (stack.Count > 1)
                        {
                            stack.Pop();
                        }

                        cur = stack.Peek();
                        break;
                    case 3:
                        tree.AddText(cur, (arg & 1) == 0 ? " " : "ab c" + new string('x', arg & 63));
                        break;
                    case 4:
                        tree.AddElement(cur, Tags[arg % Tags.Length], decl);
                        decl = new StyleDeclaration();
                        break;
                    case 5:
                        decl.Set("float", arg % 3 == 0 ? "left" : arg % 3 == 1 ? "right" : "none");
                        break;
                    case 6:
                        decl.Set("position", arg % 4 == 0 ? "absolute" : arg % 4 == 1 ? "fixed" : arg % 4 == 2 ? "relative" : "static");
                        break;
                    default:
                        decl.Set(PropertyNames[arg % PropertyNames.Length], arg % 5 == 0 ? "calc(10% + 3px)" : arg % 5 == 1 ? "auto" : arg % 5 == 2 ? "-1" : arg % 5 == 3 ? "2em" : "inherit");
                        break;
                }
            }

            boxes.Build();
            string dump = boxes.Dump();
            if (pass == 0)
            {
                first = dump;
            }
            else if (!string.Equals(first, dump, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("a rebuild into the retained arenas gave a different box tree");
            }
        }
    }

    /// <summary>The bytes are an HTML document (and a style sheet after a form feed); both front ends of the pipeline run on it.</summary>
    private static void Html(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        string text = Decode(data);
        int ff = text.IndexOf('\f', StringComparison.Ordinal);
        string css = ff < 0 ? string.Empty : text[(ff + 1)..];
        string html = ff < 0 ? text : text[..ff];
        Document doc = Document.Parse("<style>" + css.Replace("</", "<\\/", StringComparison.Ordinal) + "</style>" + html);
        DocumentStyles styles = DocumentStyles.Create(doc, new StyleContext(), new StyleSourceOptions { IncludeUserAgentSheet = true });
        var tree = new StyledTree(null, new TreeLimits { MaxNodes = 1 << 14, MaxDepth = 256, MaxTextLength = 1 << 18 });
        new HtmlFrontEnd(tree).Build(styles);
        var boxes = new BoxTree(tree);
        boxes.Build();
        _ = boxes.Dump();
    }

    private static string Decode(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> body = data[1..];
        if ((data[0] & 1) == 1)
        {
            var chars = new char[body.Length / 2];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = (char)(body[2 * i] | (body[(2 * i) + 1] << 8));
            }

            return new string(chars);
        }

        return Encoding.UTF8.GetString(body);
    }
}
