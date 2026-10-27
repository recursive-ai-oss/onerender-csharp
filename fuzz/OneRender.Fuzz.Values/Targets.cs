using OneRender.Text;
using System;
using System.Collections.Generic;
using System.Text;
using OneFont;
using OneRender.Layout;
using OneRender.Painting;
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
        ["layout"] = Layout,
        ["flex"] = FlexTarget.Run,
        ["fragmentation"] = FragmentationTarget.Run,
        ["positioning"] = Positioning,
        ["inline"] = Inline,
        ["paint"] = Paint,
        ["motion"] = MotionTarget.Run,
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
        "flex-direction", "flex-wrap", "flex-grow", "flex-shrink", "flex-basis", "flex", "flex-flow", "order", "justify-content", "align-items", "align-self", "align-content",
        "row-gap", "column-gap", "gap", "place-items", "place-content",
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
        DocumentStyles styles = DocumentStyles.Create(doc, new StyleContext(), new StyleSourceOptions { IncludeUserAgentSheet = true, DeclarationValidator = OneRender.Style.StyleValidation.Validator });
        var tree = new StyledTree(null, new TreeLimits { MaxNodes = 1 << 14, MaxDepth = 256, MaxTextLength = 1 << 18 });
        new HtmlFrontEnd(tree).Build(styles);
        var boxes = new BoxTree(tree);
        boxes.Build();
        _ = boxes.Dump();
    }

    /// <summary>
    /// As <c>html</c>, then block layout with floats and clearance at a width taken from the first byte. A second, fresh engine and
    /// fragment tree must give the same fragments as the retained ones laid out again.
    /// </summary>
    private static void Layout(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        string text = Decode(data);
        int ff = text.IndexOf('', StringComparison.Ordinal);
        string css = ff < 0 ? string.Empty : text[(ff + 1)..];
        string html = ff < 0 ? text : text[..ff];
        Document doc = Document.Parse("<style>" + css.Replace("</", "<\\/", StringComparison.Ordinal) + "</style>" + html);
        DocumentStyles styles = DocumentStyles.Create(doc, new StyleContext(), new StyleSourceOptions { IncludeUserAgentSheet = true, DeclarationValidator = OneRender.Style.StyleValidation.Validator });
        var tree = new StyledTree(null, new TreeLimits { MaxNodes = 1 << 14, MaxDepth = 256, MaxTextLength = 1 << 18 });
        new HtmlFrontEnd(tree).Build(styles);
        var boxes = new BoxTree(tree);
        boxes.Build();
        double width = 40 + ((data[0] >> 1) * 7);
        var input = new LayoutInput(width, double.NaN, OneRender.Image.FontProviders.ForCurrentThread());
        var engine = new BlockLayoutEngine();
        var fragments = new FragmentTree(boxes);
        engine.Layout(boxes, input, fragments);
        string first = fragments.Dump();
        engine.Layout(boxes, input, fragments);
        if (!string.Equals(first, fragments.Dump(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("laying out again into the retained fragment tree gave different fragments");
        }

        var fresh = new FragmentTree(boxes);
        new BlockLayoutEngine().Layout(boxes, input, fresh);
        if (!string.Equals(first, fresh.Dump(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("a fresh layout gave different fragments");
        }
    }

    /// <summary>
    /// The bytes are an HTML document and style sheet as for <c>html</c>; the block layout, positioning and overflow run on it, the
    /// result is painted with scroll positions taken from the input, and the scroll patch must equal a fresh paint. Finite geometry
    /// and a repeatable layout are the invariants.
    /// </summary>
    private static void Positioning(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        byte salt = data[^1];
        string text = Decode(data[..^1]);
        int ff = text.IndexOf('\f', StringComparison.Ordinal);
        string css = ff < 0 ? string.Empty : text[(ff + 1)..];
        string html = ff < 0 ? text : text[..ff];
        Document doc = Document.Parse("<style>" + css.Replace("</", "<\\/", StringComparison.Ordinal) + "</style>" + html);
        var context = new StyleContext { Media = new MediaFeatures { Width = 400, Height = 300 } };
        DocumentStyles styles = DocumentStyles.Create(doc, context, new StyleSourceOptions { IncludeUserAgentSheet = true, DeclarationValidator = OneRender.Style.StyleValidation.Validator });
        var tree = new StyledTree(null, new TreeLimits { MaxNodes = 1 << 12, MaxDepth = 256, MaxTextLength = 1 << 16 });
        new HtmlFrontEnd(tree).Build(styles);
        var boxes = new BoxTree(tree);
        boxes.Build();

        var engine = new BlockLayoutEngine();
        var frags = new FragmentTree(boxes);
        var input = new LayoutInput(400, 300, OneRender.Image.FontProviders.ForCurrentThread());
        engine.Layout(boxes, input, frags);
        string first = frags.Dump();
        for (Fragment f = frags.Root; !f.IsNull; f = Next(f))
        {
            Rect r = f.BorderBox;
            Rect o = f.ScrollableOverflow;
            if (!double.IsFinite(r.X + r.Y + r.Width + r.Height) || double.IsNaN(o.X + o.Y + o.Width + o.Height))
            {
                throw new InvalidOperationException("non-finite geometry in " + f);
            }
        }

        engine.Layout(boxes, input, frags);
        if (!string.Equals(first, frags.Dump(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("a second layout of the same tree differs");
        }

        var scroll = new ScrollState();
        var options = new PaintOptions(400, 300) { Scroll = scroll };
        var painter = new Painter();
        var retained = new RetainedDisplayList();
        painter.Paint(frags, retained, options);
        for (int k = 0; k < 3 && boxes.Count > 0; k++)
        {
            double a = ((salt * (k + 3)) % 200) * 7 - 100;
            scroll.SetViewportOffset(a, a * 3);
            scroll.SetOffset(NthBox(boxes, ((salt * 31) + k) % boxes.Count), a, a * 2);
            painter.PatchScroll(retained);
            var fresh = new DisplayList();
            new Painter().Paint(frags, fresh, options);
            DisplayList patched = retained.Current;
            if (fresh.Count != patched.Count)
            {
                throw new InvalidOperationException("a scroll patch changed the number of items");
            }

            for (int i = 0; i < fresh.Count; i++)
            {
                if (fresh[i].ContentHash != patched[i].ContentHash || fresh[i].ContextHash != patched[i].ContextHash)
                {
                    throw new InvalidOperationException("a scroll patch differs from a fresh paint at item " + i);
                }
            }
        }
    }

    /// <summary>
    /// An HTML document and style sheet as for <c>layout</c>, laid out with the real default fonts at a width taken from the first byte.
    /// The inline layout must give the same fragments when run again into the retained tree and into a fresh one, with finite
    /// geometry, text fragments inside their text, lines inside their container and min-content no wider than max-content.
    /// </summary>
    private static void Inline(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        string text = Decode(data);
        int ff = text.IndexOf('\f', StringComparison.Ordinal);
        string css = ff < 0 ? string.Empty : text[(ff + 1)..];
        string html = ff < 0 ? text : text[..ff];
        Document doc = Document.Parse("<style>" + css.Replace("</", "<\\/", StringComparison.Ordinal) + "</style>" + html);
        DocumentStyles styles = DocumentStyles.Create(doc, new StyleContext(), new StyleSourceOptions { IncludeUserAgentSheet = true, DeclarationValidator = OneRender.Style.StyleValidation.Validator });
        var tree = new StyledTree(null, new TreeLimits { MaxNodes = 1 << 12, MaxDepth = 128, MaxTextLength = 1 << 16 });
        new HtmlFrontEnd(tree).Build(styles);
        var boxes = new BoxTree(tree);
        boxes.Build();
        double width = 20 + ((data[0] >> 1) * 5);
        var input = new LayoutInput(width, double.NaN, OneRender.Image.FontProviders.ForCurrentThread());
        var engine = new BlockLayoutEngine();
        var fragments = new FragmentTree(boxes);
        engine.Layout(boxes, input, fragments);
        string first = fragments.Dump();
        for (Fragment f = fragments.Root; !f.IsNull; f = Next(f))
        {
            Rect r = f.BorderBox;
            if (!double.IsFinite(r.X + r.Y + r.Width + r.Height) || r.Width < 0 || r.Height < 0)
            {
                throw new InvalidOperationException("bad geometry in " + f);
            }

            if (f.Kind == FragmentKind.Text && (f.TextStart < 0 || f.TextLength < 0 || f.TextStart + f.TextLength > f.Box.Text.Length))
            {
                throw new InvalidOperationException("text range outside its text in " + f);
            }

            if (f.Kind == FragmentKind.Line && !f.Parent.IsNull && f.Parent.Kind == FragmentKind.Box && f.Parent.Parent.IsNull && r.Y < -1e-6)
            {
                throw new InvalidOperationException("line above its container");
            }
        }

        engine.Layout(boxes, input, fragments);
        if (!string.Equals(first, fragments.Dump(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("laying out again into the retained fragment tree gave different fragments");
        }

        var fresh = new FragmentTree(boxes);
        new BlockLayoutEngine().Layout(boxes, input, fresh);
        if (!string.Equals(first, fresh.Dump(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("a fresh layout gave different fragments");
        }

        engine.GetIntrinsicSizes(boxes.Root, out double min, out double max);
        if (!(min <= max + 1e-6) || double.IsNaN(min) || double.IsNaN(max))
        {
            throw new InvalidOperationException("min-content " + min + " is wider than max-content " + max);
        }
    }

    private sealed class FuzzImages : OneRender.Painting.IImageSource
    {
        public bool TryGetImage(string address, out OneRender.Painting.ImageInfo image)
        {
            image = new OneRender.Painting.ImageInfo(new OneRender.Painting.ImageHandle(address.Length & 3), 1 + (address.Length * 7 % 90), 1 + (address.Length * 13 % 60));
            return (address.Length & 7) != 0;
        }
    }

    /// <summary>
    /// As <c>inline</c> through the whole pipeline to a display list (every paint property reaches the painter), painted twice into
    /// the same list kind and rasterized small: the two paints must agree, the list must be closed and the rasterizer may only throw
    /// its typed exception.
    /// </summary>
    private static void Paint(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        string text = Decode(data);
        int ff = text.IndexOf('\f', StringComparison.Ordinal);
        string css = ff < 0 ? string.Empty : text[(ff + 1)..];
        string html = ff < 0 ? text : text[..ff];
        var images = new FuzzImages();
        var options = new HtmlPageOptions(OneRender.Image.FontProviders.ForCurrentThread())
        {
            ViewportWidth = 20 + ((data[0] >> 1) * 3),
            ViewportHeight = 64,
            Images = images,
        };
        HtmlPage page = HtmlPage.Create("<style>" + css.Replace("</", "<\\/", StringComparison.Ordinal) + "</style>" + html, options);
        var painter = new OneRender.Painting.Painter();
        var a = new OneRender.Painting.DisplayList();
        var b = new OneRender.Painting.DisplayList();
        var popts = new OneRender.Painting.PaintOptions(options.ViewportWidth, 64) { Images = images, Scroll = page.Scroll };
        painter.Paint(page.Fragments, a, popts);
        painter.Paint(page.Fragments, b, popts);
        if (a.Count != b.Count)
        {
            throw new InvalidOperationException("painting twice gave " + a.Count + " and " + b.Count + " items");
        }

        try
        {
            OneRender.Image.DisplayListRasterizer.Render(a, 48, 48, new OneRender.Image.RasterOptions());
        }
        catch (OneRender.Image.RasterizationException)
        {
        }
    }

    // Document order without recursion: down first, then next sibling, then up.
    private static Fragment Next(Fragment f)
    {
        if (!f.FirstChild.IsNull)
        {
            return f.FirstChild;
        }

        for (Fragment p = f; !p.IsNull; p = p.Parent)
        {
            if (!p.NextSibling.IsNull)
            {
                return p.NextSibling;
            }
        }

        return default;
    }

    private static Box NthBox(BoxTree boxes, int n)
    {
        Box b = boxes.Root;
        for (int i = 0; i < n && !b.IsNull; i++)
        {
            if (!b.FirstChild.IsNull)
            {
                b = b.FirstChild;
                continue;
            }

            Box p = b;
            b = default;
            for (; !p.IsNull; p = p.Parent)
            {
                if (!p.NextSibling.IsNull)
                {
                    b = p.NextSibling;
                    break;
                }
            }
        }

        return b.IsNull ? boxes.Root : b;
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
