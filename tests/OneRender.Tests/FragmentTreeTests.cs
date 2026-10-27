using System.Linq;
using System.Threading;
using OneFont;
using OneRender.Fonts;
using OneRender.Layout;
using OneRender.Style;
using OneRender.Tests.Support;
using OneRender.Tree;
using OneRender.TestSupport;
using Xunit;

namespace OneRender.Tests;

/// <summary>The fragment-tree contract, exercised with a deliberately naive layout that only stacks boxes.</summary>
public class FragmentTreeTests
{
    /// <summary>
    /// Stacks block-level children vertically at the full available width, gives every text box a 20 px line and a text
    /// fragment with two fake glyphs, and gives inline boxes the union of their lines. It is a stand-in for a real layout
    /// and shows what a layout must write.
    /// </summary>
    private sealed class StackEngine(FontFace face) : ILayoutEngine
    {
        private readonly int[] _glyphs = [face.GetGlyphIndex('a'), face.GetGlyphIndex('b')];
        private readonly double[] _xs = [0, 8];
        private readonly double[] _ys = [0, 0];
        private readonly int[] _clusters = [0, 1];

        public void Layout(BoxTree boxes, in LayoutInput input, FragmentTree output, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(output.Boxes, boxes))
            {
                throw new ArgumentException("wrong tree", nameof(output));
            }

            output.Clear();
            if (boxes.Root.IsNull)
            {
                return;
            }

            Place(output, default, boxes.Root, 0, 0, input.AvailableWidth);
        }

        private double Place(FragmentTree output, Fragment parent, Box box, double x, double y, double width)
        {
            Fragment f = output.AddBox(parent, box, new Rect(x, y, width, 0));
            ref readonly ComputedValues v = ref box.Values;
            output.SetEdges(f, new Insets(v.BorderTopWidth, v.BorderRightWidth, v.BorderBottomWidth, v.BorderLeftWidth), default);
            double height = 0;
            for (Box c = box.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                switch (c.Kind)
                {
                    case BoxKind.Text:
                        Fragment line = output.AddLine(f, new Rect(0, height, width, 20), 15);
                        GlyphRunId run = output.AddGlyphRun(face, 16, 0, 2, false, _glyphs, _xs, _ys, _clusters);
                        output.AddText(line, c, 0, 2, new Rect(0, 0, 16, 20), run);
                        height += 20;
                        break;
                    case BoxKind.Container:
                    case BoxKind.Inline:
                    case BoxKind.Replaced:
                        height += Place(output, f, c, 0, height, width);
                        break;
                }
            }

            output.SetBorderBox(f, new Rect(x, y, width, height));
            output.SetScrollableOverflow(f, new Rect(0, 0, width, height));
            return height;
        }
    }

    private sealed class NoFonts : IFontProvider
    {
        public FontFallbackChain GetFallbackChain(in ComputedValues style) => throw new NotSupportedException();
    }

    private static FontFace Face() => FontFace.Load(DefaultFonts.GetFaces("Liberation Sans")[0].ReadAllBytes());

    [Fact]
    public void LayoutWritesTheFragmentTreeAndElementsAreFoundByHtmlNode()
    {
        Pipeline p = Pipeline.FromHtml("<body style='margin:0'><div id=a>one</div><div id=b>two<p id=c>three</p></div></body>");
        var fragments = new FragmentTree(p.Boxes);
        new StackEngine(Face()).Layout(p.Boxes, new LayoutInput(300, double.NaN, new NoFonts()), fragments);

        Assert.False(fragments.Root.IsNull);
        Assert.Equal(300, fragments.Root.BorderBox.Width);

        Assert.True(fragments.TryGetBorderBox(p.Document.GetElementById("a"), out Rect a));
        Assert.True(fragments.TryGetBorderBox(p.Document.GetElementById("b"), out Rect b));
        Assert.True(fragments.TryGetBorderBox(p.Document.GetElementById("c"), out Rect c));
        Assert.Equal(new Rect(0, 0, 300, 20), a.Translate(0, -a.Y));
        Assert.Equal(a.Bottom, b.Y);
        Assert.Equal(b.Y + 20, c.Y); // c is offset inside b by b's first (anonymous) line
        Assert.Equal(300, c.Width);
        Assert.True(c.Bottom <= b.Bottom);

        // the root body offset is added up the parent chain; the html element is the root fragment
        Fragment root = fragments.Root;
        Assert.Equal((0.0, 0.0), fragments.AbsoluteOrigin(root));
        Assert.True(fragments.TryGetBorderBox(p.Styled.FindBySource(p.Document.GetElementById("c").Index), out Rect again));
        Assert.Equal(c, again);

        // elements with no box have no border box
        Assert.False(fragments.TryGetBorderBox(p.Document.Root.GetElementsByTagName("head")[0], out _));
        Assert.False(fragments.TryGetBorderBox(default(RecursiveAI.OneHtml.Node), out _));
        Pipeline other = Pipeline.FromHtml("<body><i id=x></i></body>");
        Assert.False(fragments.TryGetBorderBox(other.Document.GetElementById("x"), out _));
    }

    [Fact]
    public void FragmentsCarryTextRangesGlyphRunsAndLines()
    {
        Pipeline p = Pipeline.FromHtml("<body style='margin:0'><div id=a>hello</div></body>");
        FontFace face = Face();
        var fragments = new FragmentTree(p.Boxes);
        new StackEngine(face).Layout(p.Boxes, new LayoutInput(100, 50, new NoFonts()), fragments);
        Assert.True(fragments.TryGetBorderBox(p.Document.GetElementById("a"), out _));

        Fragment divFragment = fragments.FragmentOf(p.Boxes.PrincipalBox(p.Styled.FindBySource(p.Document.GetElementById("a").Index)));
        Fragment line = divFragment.FirstChild;
        Assert.Equal(FragmentKind.Line, line.Kind);
        Assert.Equal(15, line.Baseline);
        Assert.True(line.Box.IsNull);
        Fragment text = line.FirstChild;
        Assert.Equal(FragmentKind.Text, text.Kind);
        Assert.Equal(BoxKind.Text, text.Box.Kind);
        Assert.Equal("hello", text.Box.Text.ToString());
        Assert.Equal((0, 2), (text.TextStart, text.TextLength));
        GlyphRun run = fragments.GetGlyphRun(text.GlyphRun);
        Assert.Same(face, run.Face);
        Assert.Equal(16, run.FontSize);
        Assert.Equal(2, run.GlyphCount);
        Assert.Equal(face.GetGlyphIndex('b'), fragments.GlyphIds(run)[1]);
        Assert.Equal(8, fragments.GlyphX(run)[1]);
        Assert.Equal(1, fragments.GlyphClusters(run)[1]);
        Assert.Equal(new Rect(0, 0, 100, 20), divFragment.ScrollableOverflow);
        Assert.Equal("box div [0, 0, 100 x 20]", fragments.Dump().Split('\n')[2].Trim());
        Assert.Throws<ArgumentException>(() => fragments.AddGlyphRun(face, 16, 0, 1, false, [1], [0, 1], [0], [0]));
    }

    [Fact]
    public void ASplitInlineBoxReportsTheUnionOfItsPieces()
    {
        Pipeline p = Pipeline.FromHtml("<body style='margin:0'><div>x<span id=s>a<p>b</p>c</span></div></body>");
        var fragments = new FragmentTree(p.Boxes);
        new StackEngine(Face()).Layout(p.Boxes, new LayoutInput(100, double.NaN, new NoFonts()), fragments);
        Assert.True(fragments.TryGetBorderBox(p.Document.GetElementById("s"), out Rect span));
        StyledNode s = p.Styled.FindBySource(p.Document.GetElementById("s").Index);
        Box first = p.Boxes.PrincipalBox(s);
        Box second = first.NextPiece;
        Assert.False(second.IsNull);
        Rect r1 = fragments.AbsoluteBorderBox(fragments.FragmentOf(first));
        Rect r2 = fragments.AbsoluteBorderBox(fragments.FragmentOf(second));
        Assert.Equal(r1.Union(r2), span);
        Assert.True(span.Height >= r1.Height + r2.Height);
    }

    [Fact]
    public void LayingOutAgainAllocatesNothing()
    {
        Pipeline p = Pipeline.FromHtml("<body>" + string.Concat(Enumerable.Repeat("<div>some text<p>para</p><span>inline</span></div>", 50)) + "</body>");
        var fragments = new FragmentTree(p.Boxes);
        var engine = new StackEngine(Face());
        var input = new LayoutInput(500, double.NaN, new NoFonts());
        Action layout = () => engine.Layout(p.Boxes, input, fragments);
        layout();
        Assert.True(fragments.Count > 150);
        Allocation.AssertZero(layout, "writing the fragment tree again");
        Assert.Throws<ArgumentException>(() => engine.Layout(new BoxTree(p.Styled), input, fragments));
    }

    [Fact]
    public void FragmentTreeRejectsMisuse()
    {
        Pipeline p = Pipeline.FromHtml("<body><div>x</div></body>");
        var fragments = new FragmentTree(p.Boxes);
        fragments.Clear();
        Fragment root = fragments.AddBox(default, p.Boxes.Root, new Rect(0, 0, 1, 1));
        Assert.Throws<ArgumentException>(() => fragments.AddBox(default, p.Boxes.Root, new Rect(0, 0, 1, 1)));
        Pipeline other = Pipeline.FromHtml("<body><div>x</div></body>");
        Assert.Throws<ArgumentException>(() => fragments.AddBox(root, other.Boxes.Root, new Rect(0, 0, 1, 1)));
        Assert.Throws<ArgumentException>(() => fragments.SetEdges(new FragmentTree(other.Boxes).Root, default, default));
        Assert.True(fragments.FragmentOf(default).IsNull);
    }
}
