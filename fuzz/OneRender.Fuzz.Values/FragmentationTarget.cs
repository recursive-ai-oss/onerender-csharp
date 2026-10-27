using System;
using System.Collections.Generic;
using System.Threading;
using OneFont;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Text;
using OneRender.Tree;

namespace OneRender.Fuzz;

/// <summary>
/// Fragmentation and multi-column layout on random trees. The bytes pick the container's column count, width, height and fill, and then
/// operations that grow a tree of blocks, text and floats and set break, column, sizing and decoration properties from small vocabularies.
/// The same tree is laid out twice, once as a multi-column container and once with every column and break property left out. Every box
/// must keep all of its lines and its fragments (nothing lost, nothing duplicated), the fragments of a box must follow the columns in
/// order, laying out again must give the same fragments, every border box must be finite and not negative, and every fragment must be
/// reachable from the root exactly once.
/// </summary>
internal static class FragmentationTarget
{
    private static readonly string[] Props =
    [
        "break-before", "break-after", "break-inside", "orphans", "widows", "box-decoration-break", "column-span", "column-gap",
        "height", "margin-top", "margin-bottom", "padding", "border", "float", "overflow", "position", "width", "line-height", "min-height",
        "column-count", "column-width", "direction", "clear",
    ];

    private static readonly string[] Values =
    [
        "auto", "avoid", "column", "page", "avoid-column", "avoid-page", "left", "clone", "slice", "all", "none", "1", "2", "3", "5", "10px",
        "40px", "100px", "20px", "3px solid", "hidden", "absolute", "1e9px", "0", "-10px", "50%", "2", "4", "30px", "rtl", "both", "relative", "left",
    ];

    private static readonly string[] Heights = ["auto", "60px", "200px", "1000px", "1px", "auto", "90px", "auto"];

    private sealed class NoFonts : IFontProvider
    {
        public FontCatalog Catalog => throw new NotSupportedException();

        public ResolvedFontList Resolve(in ComputedValues style) => throw new NotSupportedException();

        public ShapedText Shape(ReadOnlySpan<char> text, in ComputedValues style, bool rightToLeft = false) => throw new NotSupportedException();

        public FontMetricsInfo GetMetrics(in ComputedValues style) => throw new NotSupportedException();
    }

    /// <summary>One line per non-blank character, so the number of lines does not depend on the width.</summary>
    private sealed class LinePerCharacter : IInlineLayout
    {
        public InlineLayoutResult Layout(in InlineLayoutRequest request, FragmentTree output, Fragment parent, CancellationToken cancellationToken)
        {
            Box container = request.Container;
            double lh = LineHeight(in container.Values);
            if (!(lh > 0) || double.IsInfinity(lh))
            {
                lh = 10;
            }

            int n = 0;
            for (Box b = container.FirstChild; !b.IsNull; b = b.NextSibling)
            {
                if (b.Kind != BoxKind.Text)
                {
                    continue;
                }

                foreach (char ch in b.Text)
                {
                    if (!char.IsWhiteSpace(ch) && n < 5000)
                    {
                        output.AddLine(parent, new Rect(request.ContentLeft, request.ContentTop + (n * lh), request.AvailableWidth, lh), lh * 0.8);
                        n++;
                    }
                }
            }

            return new InlineLayoutResult(n * lh, n, n > 0 ? lh * 0.8 : double.NaN, n > 0 ? ((n - 1) * lh) + (lh * 0.8) : double.NaN);
        }

        public void Measure(Box container, IFontProvider fonts, out double minContent, out double maxContent)
        {
            minContent = 0;
            maxContent = 0;
        }
    }

    private static double LineHeight(in ComputedValues v) => v.LineHeight.Kind switch
    {
        LengthKind.Number => v.LineHeight.Value * v.FontSize,
        LengthKind.Px => v.LineHeight.Value,
        _ => v.FontSize * 1.2,
    };

    private static bool IsFragmentationProperty(string name) =>
        name.StartsWith("break-", StringComparison.Ordinal) || name.StartsWith("column-", StringComparison.Ordinal) || name is "orphans" or "widows" or "box-decoration-break";

    private static BoxTree Build(ReadOnlySpan<byte> data, bool fragmented)
    {
        var styled = new StyledTree(null, new TreeLimits { MaxNodes = 1 << 12, MaxDepth = 256, MaxTextLength = 1 << 16 });
        var boxes = new BoxTree(styled);
        var root = new StyleDeclaration().Set("width", (100 + (data[1] * 3)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "px")
            .Set("height", Heights[data[2] % Heights.Length]).Set("line-height", "10px");
        if (fragmented)
        {
            root.Set("column-count", (1 + (data[0] % 5)).ToString(System.Globalization.CultureInfo.InvariantCulture)).Set("column-gap", (data[0] & 8) != 0 ? "10px" : "0");
            if ((data[0] & 16) != 0)
            {
                root.Set("column-fill", "auto");
            }
        }

        var stack = new Stack<StyledNode>();
        StyledNode cur = styled.AddRoot("div", root);
        stack.Push(cur);
        var decl = new StyleDeclaration();
        for (int i = 3; i + 1 < data.Length; i += 2)
        {
            int op = data[i] & 7;
            int arg = data[i + 1];
            switch (op)
            {
                case 0:
                case 1:
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
                case 4:
                    styled.AddText(cur, new string('x', 1 + (arg % 12)));
                    break;
                default:
                {
                    string name = Props[(data[i] >> 3) % Props.Length];
                    if (fragmented || !IsFragmentationProperty(name))
                    {
                        decl.Set(name, Values[arg % Values.Length]);
                    }

                    break;
                }
            }
        }

        boxes.Build();
        return boxes;
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
        {
            return;
        }

        BoxTree boxes = Build(data, true);
        BoxTree flat = Build(data, false);
        double width = 100 + (data[1] * 3) + 40;
        var input = new LayoutInput(width, double.NaN, new NoFonts());
        var fragments = new FragmentTree(boxes);
        var engine = new BlockLayoutEngine(new LinePerCharacter());
        engine.Layout(boxes, input, fragments);
        string first = fragments.Dump();
        Check(fragments);
        engine.Layout(boxes, input, fragments);
        if (!string.Equals(first, fragments.Dump(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("laying out again into the retained fragment tree gave different fragments");
        }

        var fresh = new FragmentTree(boxes);
        new BlockLayoutEngine(new LinePerCharacter()).Layout(boxes, input, fresh);
        if (!string.Equals(first, fresh.Dump(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("a fresh layout gave different fragments");
        }

        // Nothing lost, nothing duplicated: the lines and the fragments each box has are the same as without columns.
        var continuous = new FragmentTree(flat);
        new BlockLayoutEngine(new LinePerCharacter()).Layout(flat, input, continuous);
        if (boxes.Count != flat.Count)
        {
            throw new InvalidOperationException("the two trees have different boxes");
        }

        int[] a = LineCounts(fragments, boxes.Count, out int[] boxFragsA);
        int[] b = LineCounts(continuous, flat.Count, out int[] boxFragsB);
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                throw new InvalidOperationException("box " + i + " has " + a[i] + " lines in columns but " + b[i] + " without");
            }

            if ((boxFragsA[i] == 0) != (boxFragsB[i] == 0))
            {
                throw new InvalidOperationException("box " + i + " has fragments in only one of the layouts");
            }
        }

        CheckPieceOrder(fragments);
    }

    private static int[] LineCounts(FragmentTree f, int boxCount, out int[] boxFragments)
    {
        var lines = new int[boxCount];
        boxFragments = new int[boxCount];
        var stack = new Stack<(Fragment Frag, int Owner)>();
        if (!f.Root.IsNull)
        {
            stack.Push((f.Root, -1));
        }

        while (stack.Count > 0)
        {
            (Fragment x, int owner) = stack.Pop();
            int next = owner;
            if (x.Kind == FragmentKind.Box)
            {
                next = x.Box.Index;
                boxFragments[next]++;
            }
            else if (x.Kind == FragmentKind.Line && owner >= 0)
            {
                lines[owner]++;
            }

            for (Fragment c = x.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                stack.Push((c, next));
            }
        }

        return lines;
    }

    private static void CheckPieceOrder(FragmentTree f)
    {
        var stack = new Stack<Fragment>();
        if (!f.Root.IsNull)
        {
            stack.Push(f.Root);
        }

        while (stack.Count > 0)
        {
            Fragment x = stack.Pop();
            if (x.Kind == FragmentKind.Box && !x.NextOfBox.IsNull && x.NextOfBox.FragmentainerIndex < x.FragmentainerIndex)
            {
                throw new InvalidOperationException("the fragments of a box are not in column order");
            }

            for (Fragment c = x.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                stack.Push(c);
            }
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
            if (++reachable > f.Count)
            {
                throw new InvalidOperationException("a fragment is reachable twice");
            }

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
