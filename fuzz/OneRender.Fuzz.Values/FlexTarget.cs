using System;
using System.Collections.Generic;
using OneFont;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Text;
using OneRender.Tree;

namespace OneRender.Fuzz;

/// <summary>
/// Flex layout on random trees. The bytes are pairs of operations that grow a styled tree of flex, block and inline-flex boxes and set
/// flex-related (and sizing) properties from small vocabularies; the tree is laid out at a width taken from the first byte. Laying
/// out again into the retained fragment tree, and into a fresh one, must give identical fragments, every border box must be finite
/// and not negative, and the fragment tree must hold exactly the fragments reachable from its root.
/// </summary>
internal static class FlexTarget
{
    private static readonly string[] Props =
    [
        "flex-direction", "flex-wrap", "flex-grow", "flex-shrink", "flex-basis", "order", "justify-content", "align-items",
        "align-self", "align-content", "row-gap", "column-gap", "gap", "flex", "flex-flow", "place-items", "place-content",
        "width", "height", "min-width", "min-height", "max-width", "max-height", "margin", "margin-left", "margin-top", "padding", "border-width",
        "aspect-ratio", "position", "float", "box-sizing", "overflow", "direction", "display",
    ];

    private static readonly string[] Values =
    [
        "auto", "0", "1", "2", "-1", "10px", "50px", "100px", "200%", "50%", "calc(10% + 3px)", "min-content", "max-content", "fit-content",
        "stretch", "content", "1e9px", "0.5", "normal", "center", "flex-start", "flex-end", "start", "end", "space-between", "space-around",
        "space-evenly", "baseline", "last baseline", "safe center", "unsafe end", "row", "column", "row-reverse", "column-reverse", "wrap",
        "wrap-reverse", "nowrap", "none", "1 1 0", "0 0 auto", "column wrap", "inherit", "3em", "1 / 2", "absolute", "left", "border-box",
        "hidden", "rtl", "flex", "inline-flex", "block",
    ];

    private static readonly string[] Displays = ["flex", "flex", "inline-flex", "block", "flex", "inline-block"];

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
        {
            return;
        }

        var styled = new StyledTree(null, new TreeLimits { MaxNodes = 1 << 12, MaxDepth = 256, MaxTextLength = 1 << 16 });
        var boxes = new BoxTree(styled);
        var stack = new Stack<StyledNode>();
        StyledNode cur = styled.AddRoot("div", new StyleDeclaration().Set("display", "flex"));
        stack.Push(cur);
        var decl = new StyleDeclaration();
        for (int i = 1; i + 1 < data.Length; i += 2)
        {
            int op = data[i] & 7;
            int arg = data[i + 1];
            switch (op)
            {
                case 0:
                case 1:
                    decl.Set("display", Displays[arg % Displays.Length]);
                    StyledNode child = styled.AddElement(cur, "div", decl);
                    if (!child.IsNull)
                    {
                        cur = child;
                        stack.Push(cur);
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
                    styled.AddElement(cur, "div", decl);
                    decl = new StyleDeclaration();
                    break;
                default:
                    decl.Set(Props[(data[i] >> 3) % Props.Length], Values[arg % Values.Length]);
                    break;
            }
        }

        boxes.Build();
        double width = 20 + (data[0] * 5);
        var input = new LayoutInput(width, double.NaN, OneRender.Image.FontProviders.ForCurrentThread());
        var engine = new BlockLayoutEngine();
        var fragments = new FragmentTree(boxes);
        engine.Layout(boxes, input, fragments);
        string first = fragments.Dump();
        Check(fragments);
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

    private static void Check(FragmentTree f)
    {
        int reachable = 0;
        var stack = new Stack<Fragment>();
        if (!f.Root.IsNull)
        {
            stack.Push(f.Root);
        }

        while (stack.Count > 0)
        {
            Fragment x = stack.Pop();
            reachable++;
            Rect r = x.BorderBox;
            if (!double.IsFinite(r.X) || !double.IsFinite(r.Y) || !double.IsFinite(r.Width) || !double.IsFinite(r.Height) || r.Width < 0 || r.Height < 0)
            {
                throw new InvalidOperationException("a fragment has a non-finite or negative border box: " + r);
            }

            for (Fragment c = x.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                stack.Push(c);
            }
        }

        if (reachable != f.Count)
        {
            throw new InvalidOperationException("the fragment tree holds " + f.Count + " fragments but " + reachable + " are reachable");
        }
    }
}
