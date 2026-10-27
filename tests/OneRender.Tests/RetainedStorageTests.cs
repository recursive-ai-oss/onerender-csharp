using System.Linq;
using System.Text;
using System.Threading;
using OneRender.Style;
using OneRender.Tests.Support;
using OneRender.Tree;
using OneRender.TestSupport;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace OneRender.Tests;

/// <summary>Retained storage: a second build of the same content allocates nothing. Limits hold on hostile shapes.</summary>
public class RetainedStorageTests
{
    private static string ThousandNodeDocument()
    {
        var sb = new StringBuilder("<!DOCTYPE html><html><head><style>.a{color:#336;margin:2px}.b{display:inline-block;width:10em}.c::before{content:'> '}</style></head><body>");
        for (int i = 0; i < 40; i++)
        {
            sb.Append("<div class=a><p class=c>Item ").Append(i).Append(" <b>bold</b> and <span class=b>box</span> text<br>more</p>");
            sb.Append("<ul><li>one</li><li style='list-style-position:inside'>two</li></ul>");
            sb.Append("<div style='display:flex'><span>x</span>y<em>z</em></div><img src=a.png><table><tr><td>t</td></tr></table></div>");
        }

        return sb.Append("</body></html>").ToString();
    }

    [Fact]
    public void RebuildingTheHtmlPipelineAllocatesNothing()
    {
        string html = ThousandNodeDocument();
        Document doc = Document.Parse(html);
        DocumentStyles styles = DocumentStyles.Create(doc, new StyleContext());
        styles.ComputeAll();
        var tree = new StyledTree();
        var front = new HtmlFrontEnd(tree);
        var boxes = new BoxTree(tree);
        Action rebuild = () =>
        {
            front.Build(styles);
            boxes.Build();
        };

        rebuild();
        Assert.True(tree.Count >= 1000, "the document should have at least 1,000 styled nodes, has " + tree.Count);
        int boxCount = boxes.Count;
        string dump = boxes.Dump();
        Allocation.AssertZero(rebuild, "rebuilding styled tree and box tree from a OneHtml document");
        Assert.Equal(boxCount, boxes.Count);
        Assert.Equal(dump, boxes.Dump());
    }

    [Fact]
    public void RebuildingTheBuilderPipelineAllocatesNothing()
    {
        var tree = new StyledTree();
        var boxes = new BoxTree(tree);
        StyleDeclaration block = new StyleDeclaration().Set("display", "block");
        StyleDeclaration row = new StyleDeclaration().Set("display", "flex").Set("padding", "2px");
        StyleDeclaration item = new StyleDeclaration().Set("display", "inline-block").Set("width", "calc(50% - 4px)");
        StyleDeclaration text = new StyleDeclaration().Set("color", "red");
        Action rebuild = () =>
        {
            tree.Clear();
            StyledNode root = tree.AddRoot("div", block);
            for (int i = 0; i < 100; i++)
            {
                StyledNode r = tree.AddElement(root, "div", row);
                for (int j = 0; j < 4; j++)
                {
                    StyledNode it = tree.AddElement(r, "span", item);
                    tree.AddText(it, "label");
                    tree.AddElement(it, "b", text);
                }

                tree.AddText(r, " ");
            }

            boxes.Build();
        };

        rebuild();
        Assert.True(tree.Count >= 1000);
        Allocation.AssertZero(rebuild, "rebuilding a builder tree and its boxes");
    }

    [Fact]
    public void ADeepDocumentBuildsWithoutRecursion()
    {
        const int Depth = 100_000;
        var tree = new StyledTree();
        StyleDeclaration block = new StyleDeclaration().Set("display", "block");
        StyledNode n = tree.AddRoot("div", block);
        for (int i = 0; i < Depth; i++)
        {
            n = tree.AddElement(n, "div", block);
        }

        tree.AddText(n, "leaf");
        Assert.Equal(Depth + 2, tree.Count);
        Assert.True(tree.Diagnostics.Items.Count == 0, string.Join("; ", tree.Diagnostics.Items.Select(d => d.Message)));
        var boxes = new BoxTree(tree);
        boxes.Build();
        int depth = 0;
        for (Box b = boxes.Root; !b.IsNull; b = b.FirstChild)
        {
            depth++;
        }

        int boxCount = boxes.Count;

        Assert.True(depth > Depth, "all levels have boxes: " + depth);
        Assert.True(boxCount >= Depth);
        Assert.True(tree.Diagnostics.Items.Count == 0, string.Join("; ", tree.Diagnostics.Items.Select(d => d.Message)));
        // The dump is iterative too; do not print it, it is hundreds of megabytes of indent. Check that walking the fragment-free tree and rebuilding work.
        boxes.Build();
        Assert.Equal(boxCount, boxes.Count);
    }

    [Fact]
    public void ADeepInlineChainWithBlocksSplitsOnlyUpToTheLimit()
    {
        // 200 nested spans around a block: more than MaxSplitDepth, so the block is laid out as an inline-block and a diagnostic says so.
        var tree = new StyledTree();
        StyledNode n = tree.AddRoot("div", new StyleDeclaration().Set("display", "block"));
        for (int i = 0; i < 200; i++)
        {
            n = tree.AddElement(n, "span");
        }

        StyledNode block = tree.AddElement(n, "p", new StyleDeclaration().Set("display", "block"));
        tree.AddText(block, "x");
        var boxes = new BoxTree(tree);
        boxes.Build();
        Assert.Contains(tree.Diagnostics.Items, d => d.Kind == DiagnosticKind.LimitExceeded && d.Message.Contains("inline-block", StringComparison.Ordinal));
        Assert.True((boxes.PrincipalBox(block).Flags & BoxTraits.UnsplitBlockInInline) != 0);
        Assert.Equal(BoxLevel.Inline, boxes.PrincipalBox(block).Level);

        // 60 nested spans around a block: split, 60 continuation boxes.
        tree.Clear();
        n = tree.AddRoot("div", new StyleDeclaration().Set("display", "block"));
        for (int i = 0; i < 60; i++)
        {
            n = tree.AddElement(n, "span");
        }

        block = tree.AddElement(n, "p", new StyleDeclaration().Set("display", "block"));
        boxes.Build();
        Assert.True(tree.Diagnostics.Items.Count == 0, string.Join("; ", tree.Diagnostics.Items.Select(d => d.Message)));
        Assert.Equal(1 + 60 + 1 + 60 + 2, CountReachable(boxes)); // div, spans, p, continuations, two anonymous blocks
    }

    private static int CountReachable(BoxTree boxes)
    {
        int n = 0;
        var stack = new Stack<Box>();
        stack.Push(boxes.Root);
        while (stack.Count > 0)
        {
            Box b = stack.Pop();
            n++;
            for (Box c = b.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                stack.Push(c);
            }
        }

        return n;
    }

    [Fact]
    public void ManySplitsUnderADeepInlineChainStayLinear()
    {
        // 50 nested spans with 10,000 block children: each block costs at most 50 continuation boxes, never more than the box limit.
        var tree = new StyledTree();
        StyledNode n = tree.AddRoot("div", new StyleDeclaration().Set("display", "block"));
        for (int i = 0; i < 50; i++)
        {
            n = tree.AddElement(n, "span");
        }

        StyleDeclaration block = new StyleDeclaration().Set("display", "block");
        for (int i = 0; i < 10_000; i++)
        {
            tree.AddElement(n, "p", block);
        }

        var boxes = new BoxTree(tree);
        boxes.Build();
        Assert.True(boxes.Count <= 10_000 * 53 + 100);
        Assert.True(boxes.Count > 10_000 * 50);
    }

    [Fact]
    public void AWideDocumentBuildsAndAnonymousWrappingIsLinear()
    {
        const int Width = 100_000;
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div", new StyleDeclaration().Set("display", "block"));
        StyleDeclaration block = new StyleDeclaration().Set("display", "block");
        for (int i = 0; i < Width; i++)
        {
            // alternate text and blocks: Width/2 anonymous wrappers
            if (i % 2 == 0)
            {
                tree.AddText(root, "t");
            }
            else
            {
                tree.AddElement(root, "p", block);
            }
        }

        var boxes = new BoxTree(tree);
        boxes.Build();
        Assert.Equal(1 + Width + Width / 2, boxes.Count);
        Assert.True(tree.Diagnostics.Items.Count == 0, string.Join("; ", tree.Diagnostics.Items.Select(d => d.Message)));
        int kids = 0;
        for (Box c = boxes.Root.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            kids++;
        }

        Assert.Equal(Width, kids);
    }

    [Fact]
    public void NodeAndTextLimitsAreCheckedBeforeStoring()
    {
        var tree = new StyledTree(null, new TreeLimits { MaxNodes = 10, MaxDepth = 5, MaxTextLength = 20 });
        StyledNode root = tree.AddRoot("div");
        StyledNode n = root;
        for (int i = 0; i < 8; i++)
        {
            n = tree.AddElement(n, "div");
        }

        Assert.True(n.IsNull); // depth 5 reached
        Assert.Single(tree.Diagnostics.Items);
        Assert.Equal(DiagnosticKind.LimitExceeded, tree.Diagnostics.Items[0].Kind);

        tree.Clear();
        root = tree.AddRoot("div");
        for (int i = 0; i < 20; i++)
        {
            tree.AddElement(root, "span");
        }

        Assert.Equal(10, tree.Count);
        Assert.Single(tree.Diagnostics.Items);

        tree.Clear();
        root = tree.AddRoot("div");
        tree.AddText(root, "0123456789012345");
        Assert.True(tree.AddText(root, "0123456789").IsNull);
        Assert.Equal(2, tree.Count);
        Assert.Single(tree.Diagnostics.Items);
        new BoxTree(tree).Build();
    }

    [Fact]
    public void BoxLimitStopsGenerationCleanly()
    {
        var tree = new StyledTree(null, new TreeLimits { MaxNodes = 50 });
        StyledNode root = tree.AddRoot("div", new StyleDeclaration().Set("display", "block"));
        StyleDeclaration block = new StyleDeclaration().Set("display", "block");
        for (int i = 0; i < 24; i++)
        {
            tree.AddText(root, "t");
            tree.AddElement(root, "p", block);
        }

        var boxes = new BoxTree(tree);
        boxes.Build();
        Assert.True(boxes.Count <= boxes.MaxBoxes);
        _ = boxes.Dump();
    }

    [Fact]
    public void CancellationStopsABuild()
    {
        var tree = new StyledTree();
        StyledNode root = tree.AddRoot("div");
        for (int i = 0; i < 5000; i++)
        {
            tree.AddElement(root, "span");
        }

        var boxes = new BoxTree(tree);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => boxes.Build(cts.Token));
        boxes.Build();
        Assert.True(boxes.Count > 5000);
    }

    [Fact]
    public void AHostileHtmlTreeBuilds()
    {
        // 3,000 nested divs and 30,000 siblings through the OneHtml front end (the HTML parser's own nesting limit is 4,096 open elements).
        var sb = new StringBuilder("<!DOCTYPE html><html><body>");
        for (int i = 0; i < 3000; i++)
        {
            sb.Append("<div>");
        }

        sb.Append("deep");
        for (int i = 0; i < 3000; i++)
        {
            sb.Append("</div>");
        }

        for (int i = 0; i < 30_000; i++)
        {
            sb.Append("<span>w</span>");
        }

        Pipeline p = Pipeline.FromHtml(sb.ToString());
        Assert.True(p.Boxes.Count > 33_000);
        Assert.Empty(p.Styled.Diagnostics.Items);
    }

    [Fact]
    public void AHundredThousandDeepHtmlDocumentBuilds()
    {
        // Through OneHtml's parser and cascade, with its open-element limit raised for the test.
        const int Depth = 100_000;
        var sb = new StringBuilder("<!DOCTYPE html><html><body>");
        for (int i = 0; i < Depth; i++)
        {
            sb.Append("<div>");
        }

        sb.Append("deep");
        Document doc = Document.Parse(sb.ToString(), new ParseOptions { Limits = new ParseLimits { MaxOpenElements = Depth * 2 } });
        DocumentStyles styles = DocumentStyles.Create(doc, new StyleContext());
        var tree = new StyledTree();
        new HtmlFrontEnd(tree).Build(styles);
        Assert.True(tree.Count > Depth);
        Assert.True(tree.Diagnostics.Items.Count == 0, string.Join("; ", tree.Diagnostics.Items.Select(d => d.Message)));
        var boxes = new BoxTree(tree);
        boxes.Build();
        Assert.True(boxes.Count > Depth);
    }

    [Fact]
    public void AHundredThousandWideHtmlDocumentBuilds()
    {
        var sb = new StringBuilder("<!DOCTYPE html><html><body>");
        for (int i = 0; i < 100_000; i++)
        {
            sb.Append(i % 2 == 0 ? "<div>d</div>" : "<span>s</span>");
        }

        Pipeline p = Pipeline.FromHtml(sb.ToString());
        Assert.True(p.Boxes.Count > 100_000);
        Assert.Empty(p.Styled.Diagnostics.Items);
    }
}
